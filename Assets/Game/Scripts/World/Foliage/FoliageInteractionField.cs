using System.Collections.Generic;
using UnityEngine;

namespace Gamex
{
    /// <summary>
    /// The foliage interaction field (W1 spec 2.2, 6.2): a 256 x 256 RGBA16F texture of 0.2 m texels around the focus (the camera),
    /// stepped once a frame by FoliageInteraction.compute's Step kernel: shift with the window, decay with tau, stamp every
    /// registered <see cref="FoliagePusher"/>. Channels: rg = lateral push (world XZ, direction x press), b = top of the pressing
    /// body (world Y), a = press 0..1. The vertex shader samples it once per vertex; last frame's field (the other texture of the
    /// pair) goes with it for exact motion vectors. When time does not advance the field does not step and both globals name the
    /// same texture, so the motion vectors are zero while paused. Also runs knob 2's Gust kernel when that knob is on.
    /// Lives on the scene root "Foliage"; LateUpdate after the controller and the pushers, guarded by Time.frameCount.
    /// W1 I2b: also runs the Instances kernel once a frame, which computes every registered wind renderer's per-instance terms
    /// (<see cref="ForestWindInstances"/>), independently of <see cref="FieldEnabled"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class FoliageInteractionField : MonoBehaviour
    {
        /// <summary>The gust texture of knob 2: 64 x 64 cells of 1 m around the focus, so it covers 32 m either side.</summary>
        public const int GustResolution = 64;
        public const float GustCell = 1f;
        public const float GustHalfWindow = GustResolution * GustCell * 0.5f;
        /// <summary>At most this many pushers are stamped: the ones nearest the window's centre.</summary>
        public const int MaxStamps = 16;
        /// <summary>
        /// How long a stamp keeps the region it touched in the published box: a full press decays below the field's 1 % clear in
        /// tau x ln 100 = 3.7 s at tau 0.8 s. Longer taus extend it.
        /// </summary>
        public const float BoxMemorySeconds = 4f;
        /// <summary>
        /// W1 R1 (review of the I2c captures): how long a body that appears inside foliage (a spawn, a teleport, a pusher switched on)
        /// takes to press it in. W1 R2: the time the press's critically damped spring (<see cref="SpringPressHz"/>) takes to reach about
        /// 90 % of a contact.
        /// </summary>
        public const float AttackSeconds = 0.2f;
        /// <summary>
        /// W1 R2 (review of the R1 captures): a texel's press is a spring toward this frame's contact. It follows a body pressing in
        /// critically damped at this frequency (Hz), and springs back at <see cref="SpringReleaseHz"/> with damping
        /// <see cref="SpringReleaseDamping"/>, past its bruise and back: fronds return about 83 % within 0.6 s of the body's last contact,
        /// where R1's press decayed with tau 0.8 s and a tail and fronds crept back over 3 to 4 s; 0.3 s after a raptor's rear has passed, a
        /// point is still pressed about 0.38 (spec 8.4's trail: at least 0.3). Tuned on B/probes/W1/R2/field_spring.py: 1.75 Hz and damping
        /// 0.3 left only 0.17 there.
        /// </summary>
        public const float SpringPressHz = 3.5f, SpringReleaseHz = 0.8f, SpringReleaseDamping = 0.45f;
        /// <summary>
        /// W1 R2: the bruise, a share of each contact that the spring returns to instead of rest and that relaxes with this time constant
        /// (s), less <see cref="TailPerSecond"/> a second, so it ends at zero: a faint parting of the low layer stays readable for a few
        /// seconds behind a body.
        /// </summary>
        public const float BruiseShare = 0.3f, BruiseSeconds = 1.5f;
        /// <summary>W1 R1: subtracted from the bruise per second after its decay (R1: from every press), so it ends at zero continuously.</summary>
        public const float TailPerSecond = 0.02f;
        /// <summary>W1 R1: the press a body standing still keeps outside its core (half its radius), as a share of a walking body's.</summary>
        public const float StillFloor = 0.3f;

        [SerializeField] ComputeShader stepShader;                // Assets/Game/Shaders/FoliageInteraction.compute
        [SerializeField] FoliageDensityMap densityMap;            // ForestData/FoliageDensity.asset
        [SerializeField] int resolution = 256;
        [SerializeField] float texelSize = 0.2f;
        [SerializeField] float decaySeconds = 0.8f;               // tau (W1 R2: unused by the step, the press is a spring now; reported by DecaySeconds)

        static FoliageInteractionField active;
        static bool warnedTooMany, warnedNoCompute;

        static readonly int PreviousId = Shader.PropertyToID("_Previous"), CurrentId = Shader.PropertyToID("_Current");
        static readonly int ShiftId = Shader.PropertyToID("_Shift"), RectId = Shader.PropertyToID("_Rect");
        static readonly int StampsId = Shader.PropertyToID("_Stamps"), StampVelocityId = Shader.PropertyToID("_StampVelocity"), StampCountId = Shader.PropertyToID("_StampCount");
        static readonly int GustOutId = Shader.PropertyToID("_GustOut"), GustRectInId = Shader.PropertyToID("_GustRectIn"), GustTimeId = Shader.PropertyToID("_GustTime");
        static readonly int StillFloorId = Shader.PropertyToID("_StillFloor");
        static readonly int SpringId = Shader.PropertyToID("_Spring"), BruiseId = Shader.PropertyToID("_Bruise");
        static readonly int PreviousStateId = Shader.PropertyToID("_PreviousState"), CurrentStateId = Shader.PropertyToID("_CurrentState");

        readonly RenderTexture[] textures = new RenderTexture[2];
        // W1 R2: each texel's press velocity and bruise, ping-ponged with the field.
        readonly RenderTexture[] stateTextures = new RenderTexture[2];
        bool instanceTermsEnabled = true, instanceTermsStill, windDynamicsEnabled = true, resetDynamics;
        int windGeneration = -1;
        RenderTexture gustTexture;
        int currentIndex;
        Vector2Int originTexel;
        bool hasOrigin, gustPublished;
        Vector4 rect, previousRect, gustRect;
        int lastFrame = -1;
        int stepKernel = -1, gustKernel = -1, instancesKernel = -1;
        readonly ForestWindInstances instances = new ForestWindInstances();
        bool instancesPublished;
        RenderTexture publishedPrevious;   // the texture published as last frame's field (this frame's own while paused)
        readonly Vector4[] stamps = new Vector4[2 * MaxStamps];
        readonly Vector4[] stampVelocities = new Vector4[MaxStamps];
        readonly List<FoliagePusher> nearest = new List<FoliagePusher>();
        int stampCount;               // W1 R1: the stamps of the last step (the Instances kernel reads them from the compute shader)
        bool steppedThisFrame;
        // The regions stamped in recent frames (time, minX, minZ, maxX, maxZ), for the box the vertex shader culls by.
        readonly Queue<(float time, Vector4 box)> stamped = new Queue<(float, Vector4)>();
        Vector4 box = new Vector4(1f, 1f, -1f, -1f);

        /// <summary>This frame's box: world XZ (minX, minZ, maxX, maxZ) of every stamp of the last few seconds; min above max = none.</summary>
        public Vector4 Box => box;

        /// <summary>The enabled field, or null.</summary>
        public static FoliageInteractionField Active => active;

        /// <summary>The window's centre; null = Camera.main.</summary>
        public Transform Focus { get; set; }

        /// <summary>Off: no pushes anywhere (both rects invalid). The controller's knob fieldEnabled must also be on.</summary>
        public bool FieldEnabled { get; set; } = true;

        /// <summary>This frame's rect: origin X, origin Z (the window's corner), 1 / window size, valid (0 or 1).</summary>
        public Vector4 Rect => rect;

        /// <summary>This frame's field (tests).</summary>
        public RenderTexture Current => hasOrigin ? textures[currentIndex] : null;

        /// <summary>Knob 2's gust texture; null while ForestWind.Active.Knobs.gustTexture is off.</summary>
        public RenderTexture GustTexture => gustPublished ? gustTexture : null;

        /// <summary>The gust texture's rect: origin X, origin Z, 1 / size, valid.</summary>
        public Vector4 GustRect => gustPublished ? gustRect : Vector4.zero;

        /// <summary>W1 I2b: the registered wind renderers and their per-instance terms.</summary>
        public ForestWindInstances Instances => instances;

        /// <summary>
        /// W1 I2b, harness and tests only: off, every renderer computes its own per-instance terms in the vertex shader (I2's way; the
        /// harness state "noinst"). The pictures are the same either way (Wind_InstanceTermsMatchTheVertexShader).
        /// </summary>
        public bool InstanceTermsEnabled
        {
            get => instanceTermsEnabled;
            set { if (value != instanceTermsEnabled) resetDynamics = true; instanceTermsEnabled = value; }
        }

        /// <summary>
        /// W1 I2b, tests only (the positive control of Wind_InstanceTermsMatchTheVertexShader): the per-instance terms are computed
        /// with no wind while the globals keep it, so every registered renderer stands still and every unregistered one moves.
        /// </summary>
        public bool InstanceTermsStill
        {
            get => instanceTermsStill;
            set { if (value != instanceTermsStill) resetDynamics = true; instanceTermsStill = value; }
        }

        /// <summary>
        /// W1 R2, tests only: off, the Instances kernel gives every slot this frame's wind without its dynamics (the lean's oscillator and
        /// the envelope), so the slots draw exactly what the vertex shader's own terms draw (Wind_InstanceTermsMatchTheVertexShader).
        /// </summary>
        public bool WindDynamicsEnabled
        {
            get => windDynamicsEnabled;
            set { if (value != windDynamicsEnabled) resetDynamics = true; windDynamicsEnabled = value; }
        }

        public FoliageDensityMap DensityMap => densityMap;
        public float TexelSize => texelSize;
        public int Resolution => resolution;
        public float DecaySeconds => decaySeconds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            active = null;
            warnedTooMany = false;
            warnedNoCompute = false;
        }

        void OnEnable()
        {
            if (active != null && active != this)
                Debug.LogWarning($"FoliageInteractionField: {name} is enabled while {active.name} is; the last one enabled drives the field.");
            active = this;
            if (densityMap != null)
                FoliageInteraction.Density = densityMap;
            hasOrigin = false;
            lastFrame = -1;
            stamped.Clear();
            int slots = instances.Register();
            Debug.Log($"FoliageInteractionField: {slots} wind renderers registered for per-instance terms ({instances.Skipped} left to the vertex shader).");
        }

        void OnDisable()
        {
            if (active == this)
            {
                active = null;
                ForestWindGlobals.ClearInteraction();
                if (gustPublished)
                    ForestWindGlobals.WriteGustTexture(null, Vector4.zero);
                if (FoliageInteraction.Density == densityMap)
                    FoliageInteraction.Density = null;
                ForestWindGlobals.WriteInstances(null, null, 0);
            }
            instances.Unregister();
            instancesPublished = false;
            stampCount = 0;
            gustPublished = false;
            hasOrigin = false;
            rect = previousRect = Vector4.zero;
            Release();
        }

        void LateUpdate()
        {
            if (active != this || Time.frameCount == lastFrame)
                return;
            lastFrame = Time.frameCount;
            StepNow();
        }

        /// <summary>Steps the field now, once per frame (LateUpdate calls it; a second call in the same frame does nothing).</summary>
        public void StepNow()
        {
            bool computeOk = stepShader != null && SystemInfo.supportsComputeShaders;
            if (!computeOk && !warnedNoCompute)
            {
                warnedNoCompute = true;
                Debug.LogWarning("FoliageInteractionField: no compute shader (or no compute support): foliage does not part around bodies.");
            }
            if (computeOk && stepKernel < 0)
            {
                stepKernel = stepShader.FindKernel("Step");
                gustKernel = stepShader.FindKernel("Gust");
                instancesKernel = stepShader.FindKernel("Instances");
            }

            Transform focus = Focus != null ? Focus : Camera.main != null ? Camera.main.transform : null;
            ForestWind wind = ForestWind.Active;
            bool fieldOn = computeOk && FieldEnabled && (wind == null || wind.Knobs.fieldEnabled) && (focus != null || hasOrigin);
            steppedThisFrame = false;
            if (fieldOn)
                StepField(focus);
            else
            {
                stampCount = 0;
                if (hasOrigin || rect.w > 0f)
                {
                    hasOrigin = false;
                    rect = previousRect = Vector4.zero;
                    ForestWindGlobals.ClearInteraction();
                }
            }
            // W1 R1: the pressed bits of the registered renderers within reach of this step's stamps (cleared with the field off).
            if (steppedThisFrame || !fieldOn)
                instances.UpdatePressed(stamps, fieldOn ? stampCount : 0, Time.time, fieldOn);

            bool gustOn = computeOk && wind != null && wind.Knobs.gustTexture && focus != null;
            if (gustOn)
                StepGust(focus, wind);
            else if (gustPublished)
            {
                gustPublished = false;
                ForestWindGlobals.WriteGustTexture(null, Vector4.zero);
            }

            // W1 I2b: the per-instance terms at this frame's time and at last frame's, as HDRP's camera sets _TimeParameters (Time.time)
            // and _LastTimeParameters (Time.time - Time.deltaTime). Without wind and field the vertex shader returns before reading them.
            // W1 R1: also with the field alone (no wind): the saplings' leans and the solid firs' swings are integrated here every frame.
            bool windOn = wind != null && (wind.Current.calm > 0f || wind.Current.peak > 0f);
            bool instancesOn = computeOk && InstanceTermsEnabled && instances.Count > 0 && (windOn || fieldOn);
            if (instancesOn)
                instancesOn = DispatchInstances(wind, true);
            if (instancesOn)
            {
                ForestWindGlobals.WriteInstances(instances.CurrentTexture, instances.PreviousTexture, instances.Count);
                instancesPublished = true;
            }
            else if (instancesPublished)
            {
                ForestWindGlobals.WriteInstances(null, null, 0);
                instancesPublished = false;
            }
        }

        // The per-instance terms and push states (W1 R1: integrated over this frame's delta time from this step's stamps when
        // integrate is set).
        bool DispatchInstances(ForestWind wind, bool integrate)
        {
            ForestWindState state = wind != null ? wind.Current : default;
            if (wind == null || InstanceTermsStill)
                state.calm = state.peak = 0f;
            float now = Time.time + state.timeOffset, before = Time.time - Time.deltaTime + state.timeOffset;
            bool fieldOn = hasOrigin && rect.w > 0f;
            // W1 R2: the wind's dynamics start again from this frame's wind whenever the controller's knobs, overrides or time offset
            // changed (a test's or the harness's jump, never the game's) or a test switch changed what the slots compute.
            int generation = wind != null ? wind.Generation : -1;
            if (generation != windGeneration) { windGeneration = generation; resetDynamics = true; }
            int flags = (windDynamicsEnabled ? 0 : ForestWindInstances.NoDynamicsFlag) | (integrate && resetDynamics ? ForestWindInstances.ResetDynamicsFlag : 0);
            bool done = instances.Dispatch(stepShader, instancesKernel, state, now, before, Time.deltaTime, fieldOn ? stampCount : 0, integrate, flags);
            if (done && integrate) resetDynamics = false;
            return done;
        }

        /// <summary>
        /// W1 R1 (tests): the per-instance terms evaluated again at this frame's time and last frame's, the push states left as they are,
        /// so a frame rendered from a test coroutine (which runs before this frame's LateUpdate) shows this frame's pose. Call
        /// <see cref="ForestWind.WriteNow"/> first.
        /// </summary>
        public void RefreshInstanceTerms()
        {
            if (stepShader == null || instancesKernel < 0 || !InstanceTermsEnabled || instances.Count == 0) return;
            if (DispatchInstances(ForestWind.Active, false))
            {
                ForestWindGlobals.WriteInstances(instances.CurrentTexture, instances.PreviousTexture, instances.Count);
                instancesPublished = true;
            }
        }

        void StepField(Transform focus)
        {
            EnsureTextures();
            int half = resolution / 2;
            Vector2Int origin = originTexel;
            if (focus != null)
            {
                Vector3 p = focus.position;
                origin = new Vector2Int(Mathf.FloorToInt(p.x / texelSize) - half, Mathf.FloorToInt(p.z / texelSize) - half);
            }
            float dt = Time.deltaTime;
            Vector4 newRect = new Vector4(origin.x * texelSize, origin.y * texelSize, 1f / (resolution * texelSize), 1f);

            if (hasOrigin && dt <= 0f)
            {
                // Paused: no step, and last frame's field is this frame's, so the pushes' motion vectors are zero.
                // (The state textures stay as they are.)
                previousRect = rect;
                ForestWindGlobals.WriteInteraction(textures[currentIndex], rect, textures[currentIndex], rect);
                ForestWindGlobals.WriteInteractionParams(Vector4.zero);
                publishedPrevious = textures[currentIndex];
                return;
            }

            // Far beyond the window on the first frame: every texel reads zero from the previous field.
            Vector2Int shift = hasOrigin ? origin - originTexel : new Vector2Int(4 * resolution, 4 * resolution);
            int read = currentIndex, write = 1 - currentIndex;
            int count = GatherStamps(newRect, focus);
            stampCount = count;
            steppedThisFrame = true;

            stepShader.SetTexture(stepKernel, PreviousId, textures[read]);
            stepShader.SetTexture(stepKernel, CurrentId, textures[write]);
            stepShader.SetTexture(stepKernel, PreviousStateId, stateTextures[read]);
            stepShader.SetTexture(stepKernel, CurrentStateId, stateTextures[write]);
            stepShader.SetVector(ShiftId, new Vector4(shift.x, shift.y, 0f, 0f));
            float step = Mathf.Max(0f, dt);
            stepShader.SetVector(SpringId, new Vector4(2f * Mathf.PI * SpringPressHz, 2f * Mathf.PI * SpringReleaseHz, SpringReleaseDamping, Mathf.Min(step, 0.1f)));
            stepShader.SetVector(BruiseId, new Vector4(BruiseShare, Mathf.Exp(-step / BruiseSeconds), TailPerSecond * step, 0f));
            stepShader.SetFloat(StillFloorId, StillFloor);
            stepShader.SetVector(RectId, new Vector4(newRect.x, newRect.y, texelSize, resolution));
            stepShader.SetVectorArray(StampsId, stamps);
            stepShader.SetVectorArray(StampVelocityId, stampVelocities);
            stepShader.SetInt(StampCountId, count);
            int groups = (resolution + 7) / 8;
            stepShader.Dispatch(stepKernel, groups, groups, 1);

            previousRect = hasOrigin ? rect : newRect;
            rect = newRect;
            currentIndex = write;
            originTexel = origin;
            hasOrigin = true;
            ForestWindGlobals.WriteInteraction(textures[write], rect, textures[read], previousRect);
            ForestWindGlobals.WriteInteractionParams(new Vector4(dt > 0f ? 1f / dt : 0f, 0f, 0f, 0f));
            publishedPrevious = textures[read];
            UpdateBox(count);
        }

        // The union of every region stamped within the box memory (each stamp's segment grown by its radius and softness), published
        // for the vertex shader's per-instance cull. Nothing stamped lately: an empty box (min above max), so no vertex samples the field.
        void UpdateBox(int count)
        {
            float now = Time.time;
            if (count > 0)
            {
                var frame = new Vector4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
                for (int i = 0; i < count; i++)
                {
                    Vector4 segment = stamps[2 * i], body = stamps[2 * i + 1];
                    float grow = body.x + body.w;
                    frame.x = Mathf.Min(frame.x, Mathf.Min(segment.x, segment.z) - grow);
                    frame.y = Mathf.Min(frame.y, Mathf.Min(segment.y, segment.w) - grow);
                    frame.z = Mathf.Max(frame.z, Mathf.Max(segment.x, segment.z) + grow);
                    frame.w = Mathf.Max(frame.w, Mathf.Max(segment.y, segment.w) + grow);
                }
                stamped.Enqueue((now, frame));
            }
            // W1 R2: a press now ends with its bruise (BruiseSeconds, then the tail), within the box's 4 s.
            float memory = Mathf.Max(BoxMemorySeconds, BruiseSeconds * Mathf.Log(BruiseShare / (TailPerSecond * BruiseSeconds)) + 0.5f);
            while (stamped.Count > 0 && now - stamped.Peek().time > memory)
                stamped.Dequeue();
            box = new Vector4(1f, 1f, -1f, -1f);
            bool any = false;
            foreach ((float time, Vector4 region) entry in stamped)
            {
                box = any
                    ? new Vector4(Mathf.Min(box.x, entry.region.x), Mathf.Min(box.y, entry.region.y), Mathf.Max(box.z, entry.region.z), Mathf.Max(box.w, entry.region.w))
                    : entry.region;
                any = true;
            }
            ForestWindGlobals.WriteInteractionBox(box);
        }

        // The pushers nearest the window's centre, at most MaxStamps of them, into the stamp arrays.
        int GatherStamps(Vector4 windowRect, Transform focus)
        {
            float size = 1f / windowRect.z;
            var centre = new Vector2(windowRect.x + size * 0.5f, windowRect.y + size * 0.5f);
            nearest.Clear();
            foreach (FoliagePusher pusher in FoliageInteraction.Pushers)
                if (pusher != null && pusher.isActiveAndEnabled && pusher.Strength > 0f)
                    nearest.Add(pusher);
            if (nearest.Count > MaxStamps)
            {
                if (!warnedTooMany)
                {
                    warnedTooMany = true;
                    Debug.LogWarning($"FoliageInteractionField: {nearest.Count} pushers, only the {MaxStamps} nearest the camera part foliage.");
                }
                nearest.Sort((a, b) => Distance(a, centre).CompareTo(Distance(b, centre)));
                nearest.RemoveRange(MaxStamps, nearest.Count - MaxStamps);
            }
            for (int i = 0; i < MaxStamps; i++)
            {
                if (i < nearest.Count)
                {
                    FoliagePusher pusher = nearest[i];
                    pusher.GetStamp(out Vector2 from, out Vector2 to);
                    stamps[2 * i] = new Vector4(from.x, from.y, to.x, to.y);
                    stamps[2 * i + 1] = new Vector4(pusher.Radius, pusher.BandTopWorld, pusher.Strength, pusher.Softness);
                    Vector3 v = pusher.Velocity;
                    stampVelocities[i] = new Vector4(v.x, v.z, 0f, 0f);
                }
                else
                {
                    stamps[2 * i] = stamps[2 * i + 1] = stampVelocities[i] = Vector4.zero;
                }
            }
            return nearest.Count;
        }

        static float Distance(FoliagePusher pusher, Vector2 centre)
        {
            Vector3 p = pusher.transform.position;
            return (new Vector2(p.x, p.z) - centre).sqrMagnitude;
        }

        void StepGust(Transform focus, ForestWind wind)
        {
            if (gustTexture == null)
            {
                gustTexture = new RenderTexture(GustResolution, GustResolution, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear)
                {
                    name = "Foliage Gust", enableRandomWrite = true, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, useMipMap = false,
                };
                gustTexture.Create();
            }
            Vector3 p = focus.position;
            int half = GustResolution / 2;
            float ox = (Mathf.FloorToInt(p.x / GustCell) - half) * GustCell, oz = (Mathf.FloorToInt(p.z / GustCell) - half) * GustCell;
            // Every value the kernel reads is set on the compute shader itself (spec 3.1), from what the controller wrote this frame.
            ForestWindState state = wind.Current;
            Vector2 gust = ForestWindGlobals.Direction(state.gustBearing);
            stepShader.SetVector(ForestWindGlobals.Gust, new Vector4(gust.x, gust.y, state.gustWavelength > 0f ? 1f / state.gustWavelength : 0f, state.gustSpeed));
            stepShader.SetVector(ForestWindGlobals.Params, new Vector4(state.timeOffset, state.gustSeed, state.torchShadowEnd, 0f));
            stepShader.SetVector(GustRectInId, new Vector4(ox, oz, GustCell, GustResolution));
            // The Time node's value in play is Time.time (HDCamera.Update); the controller's offset is added there too.
            stepShader.SetFloat(GustTimeId, Time.time + state.timeOffset);
            stepShader.SetTexture(gustKernel, GustOutId, gustTexture);
            stepShader.Dispatch(gustKernel, GustResolution / 8, GustResolution / 8, 1);
            gustRect = new Vector4(ox, oz, 1f / (GustResolution * GustCell), 1f);
            gustPublished = true;
            ForestWindGlobals.WriteGustTexture(gustTexture, gustRect);
        }

        void EnsureTextures()
        {
            for (int i = 0; i < 2; i++)
            {
                if (textures[i] != null && textures[i].IsCreated() && stateTextures[i] != null && stateTextures[i].IsCreated())
                    continue;
                if (textures[i] == null || !textures[i].IsCreated())
                    textures[i] = Create(RenderTextureFormat.ARGBHalf, "Foliage Interaction " + i);
                if (stateTextures[i] == null || !stateTextures[i].IsCreated())
                    stateTextures[i] = Create(RenderTextureFormat.RGHalf, "Foliage Interaction State " + i);
                hasOrigin = false;
            }
        }

        RenderTexture Create(RenderTextureFormat format, string label)
        {
            var texture = new RenderTexture(resolution, resolution, 0, format, RenderTextureReadWrite.Linear)
            {
                name = label, enableRandomWrite = true, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, useMipMap = false,
            };
            texture.Create();
            // A new render texture holds whatever memory it was given; the shader must never read that as a push.
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previous;
            return texture;
        }

        void Release()
        {
            for (int i = 0; i < 2; i++)
            {
                if (textures[i] != null)
                {
                    textures[i].Release();
                    DestroyTexture(textures[i]);
                    textures[i] = null;
                }
                if (stateTextures[i] != null)
                {
                    stateTextures[i].Release();
                    DestroyTexture(stateTextures[i]);
                    stateTextures[i] = null;
                }
            }
            if (gustTexture != null)
            {
                gustTexture.Release();
                DestroyTexture(gustTexture);
                gustTexture = null;
            }
        }

        static void DestroyTexture(RenderTexture texture)
        {
            if (Application.isPlaying) Destroy(texture);
            else DestroyImmediate(texture);
        }

        void OnDestroy()
        {
            Release();
            instances.Release();
        }
    }
}
