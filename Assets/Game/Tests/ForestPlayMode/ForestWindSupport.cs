using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace Gamex.Tests.Forest
{
    /// <summary>
    /// W1 I2 (spec 6.2, 8.2 to 8.4): helpers of the wind play-mode tests. Sequences, temporal statistics, heat maps, the motion-vector
    /// debug view, the measurement volume, the raptor proxy, the pusher sites, and reading the interaction field back.
    /// </summary>
    static class ForestWindSupport
    {
        public const int SequenceWidth = 960, SequenceHeight = 540;
        const string PlanPath = "Assets/Game/Scenes/ForestData/placements.json";

        // ---- The plan, read as data (the editor assembly's ForestPlan is not visible here) ----

        [Serializable] public sealed class Placement { public string prefabId; public int cls; public Vector3 position; public Quaternion rotation; public float scale, sink, clearance; public int tint; }
        [Serializable] public sealed class Plan { public Placement[] placements = Array.Empty<Placement>(); }

        public const int TallFir = 1, YoungFir = 2, FernPatch = 8, FernClump = 9, GrassPatch = 10;
        /// <summary>A copy of ForestLayout.PassableYoungFirMaxScale (spec 2.9).</summary>
        public const float PassableScale = 0.705f;

        static Plan plan;
        public static Plan RequirePlan()
        {
            if (plan != null) return plan;
            Assert.IsTrue(File.Exists(PlanPath), $"{PlanPath} is missing: rebuild the forest scene.");
            plan = JsonUtility.FromJson<Plan>(File.ReadAllText(PlanPath));
            Assert.IsTrue(plan != null && plan.placements.Length > 0, $"{PlanPath} holds no placements.");
            return plan;
        }

        public static bool IsPassable(Placement p) => p.cls == YoungFir && p.scale <= PassableScale;

        /// <summary>The scene instance of a placement: Environment/Cells|Landmarks/&lt;group&gt;/&lt;prefabId&gt;__&lt;index&gt;.</summary>
        public static Transform RequireInstance(int index)
        {
            Placement p = RequirePlan().placements[index];
            string name = p.prefabId + "__" + index.ToString("00000", System.Globalization.CultureInfo.InvariantCulture);
            GameObject environment = ForestPlaySupport.RequireRoot("Environment");
            foreach (string group in new[] { "Cells", "Landmarks" })
            {
                Transform root = environment.transform.Find(group);
                if (root == null) continue;
                foreach (Transform parent in root)
                {
                    Transform found = parent.Find(name);
                    if (found != null) return found;
                }
            }
            Assert.Fail($"The forest scene has no instance {name}.");
            return null;
        }

        // ---- Sites (found by B/probes/W1/I2/sites.py and stems.py on the N1 plan; RequireClearPath re-checks them) ----

        /// <summary>A straight walk, the camera beside it, and what it must clear.</summary>
        public sealed class Site
        {
            public string name;
            public Vector2 from, to;          // the proxy's path, world XZ
            public Vector2 camera;            // the third-person camera, 1.65 m above the ground, looking at the path's middle
        }

        /// <summary>The fern hollow: 12 fern patches and clumps within 0.8 m of the path (spec 8.4: around (28.3, -20.7)).</summary>
        public static readonly Site FernSite = new Site { name = "fern", from = new Vector2(28.07f, -24.37f), to = new Vector2(31.03f, -18.03f), camera = new Vector2(34.99f, -23.74f) };

        /// <summary>
        /// A young-fir thicket the proxy can cross without touching a solid trunk: three passable saplings within 0.3 m of the path.
        /// Not the spec's (-2.7, -50.5): no straight path there clears its solid firs by the proxy's radius plus 0.1 m.
        /// </summary>
        /// The camera stands on the path's line 2 m behind its start, so the proxy walks away from it through the three saplings: from
        /// 6 m beside the path (sites.py's choice) the solid tall fir #1792 hid the thicket from the south (w1-motion-try4), and two
        /// large young firs crowd a camera on the north side.
        /// W1 R1 (review of the I2c captures): the path starts 2.5 m earlier, at (-12.36, -33.36) instead of (-14.71, -32.5), on the same line.
        /// Sapling #1840 stood 0.12 m along the old path, inside the body from its first frame, so the walk began with a body appearing in
        /// a crown (the onset pop the reviews saw at frame 30, staged); now the body walks in from outside it, starting 0.5 m behind the
        /// camera (under it: the body's top is at 1.35 m, the camera at 1.65 m). Every solid collider still clears the path by 1.14 m.
        public static readonly Site ThicketSite = new Site { name = "thicket", from = new Vector2(-12.36f, -33.36f), to = new Vector2(-21.29f, -30.1f), camera = new Vector2(-12.83f, -33.19f) };

        /// <summary>
        /// W1 I2b: solid firs whose branches cross a body's path (B/probes/W1/I2b/solid_sites.py). The path passes solid young fir #2092
        /// (scale 0.99) with its trunk 0.70 m from the axis, clearing its collider by 0.553 m (the spec's radius + 0.1 m is 0.55); tall fir
        /// #2048 and young firs #2083 and #2087 stand within 2 m. W1 I2c: the camera stands 3.5 m east of the path's middle, 0.9 m north of
        /// the path's line beyond its end, and looks back along the path at the middle, abeam #2092, so #2092's branches that cross the
        /// path are seen side on (B/probes/W1/I2c/solid_camera.py: no young fir's crown between; I2b's camera 4 m north-west looked through
        /// a near branch, and the pass changed 1 % of its pixels, as much as the noise: issues/W1-runtime.md section 12).
        /// </summary>
        public static readonly Site SolidSite = new Site { name = "solid", from = new Vector2(-21.03f, -21.92f), to = new Vector2(-15.03f, -21.92f), camera = new Vector2(-14.65f, -21.01f) };

        /// <summary>An isolated passable young fir (#4944, scale 0.63) and an isolated solid one (#824, scale 1.01), approached along +X.</summary>
        public const int IsolatedPassable = 4944, IsolatedSolid = 824;

        /// <summary>
        /// Fails when the path no longer clears every solid collider (placement clearance &gt; 0, passable saplings excepted) by
        /// <paramref name="margin"/>; returns the passable saplings within 0.3 m of it.
        /// </summary>
        public static List<int> RequireClearPath(Site site, float margin, int except = -1)
        {
            var crossed = new List<int>();
            Placement[] placements = RequirePlan().placements;
            for (int i = 0; i < placements.Length; i++)
            {
                Placement p = placements[i];
                if (p.clearance <= 0f || i == except) continue;
                float d = SegmentDistance(new Vector2(p.position.x, p.position.z), site.from, site.to);
                if (IsPassable(p)) { if (d < 0.3f) crossed.Add(i); continue; }
                Assert.GreaterOrEqual(d - p.clearance, margin,
                    $"The {site.name} path comes within {d - p.clearance:0.00} m of #{i} {p.prefabId}'s collider (needs {margin} m): the plan changed, find a new site (probes/W1/I2/sites.py).");
            }
            return crossed;
        }

        public static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b) => (p - NearestOnSegment(p, a, b)).magnitude;

        /// <summary>W1 R2: the point of segment a-b nearest p.</summary>
        public static Vector2 NearestOnSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return a + ab * t;
        }

        public static Vector3 OnGround(Terrain terrain, Vector2 xz, float lift = 0f) =>
            new Vector3(xz.x, ForestPlaySupport.GroundHeight(terrain, xz.x, xz.y) + lift, xz.y);

        /// <summary>The site's camera: 1.65 m above the ground, looking at the path's middle 0.5 m above the ground.</summary>
        public static void SiteCamera(Terrain terrain, Site site, out Vector3 position, out Quaternion rotation)
        {
            position = OnGround(terrain, site.camera, 1.65f);
            Vector3 target = OnGround(terrain, (site.from + site.to) * 0.5f, 0.5f);
            rotation = Quaternion.LookRotation(target - position, Vector3.up);
        }

        // ---- Wind and field ----

        public static ForestWind RequireWind()
        {
            ForestWind wind = ForestWind.Active;
            Assert.IsNotNull(wind, "No ForestWind is enabled: the active preset object has no wind controller.");
            return wind;
        }

        public static FoliageInteractionField RequireField()
        {
            FoliageInteractionField field = FoliageInteractionField.Active;
            Assert.IsNotNull(field, "No FoliageInteractionField is enabled: the scene has no Foliage root.");
            return field;
        }

        /// <summary>Everything a wind test may have changed, back to the scene's state.</summary>
        public static void Restore()
        {
            ForestWind wind = ForestWind.Active;
            if (wind != null)
            {
                wind.ResetKnobs();
                wind.StrengthOverride = null;
                wind.TimeOffset = 0f;
                wind.WanderTimeOffset = 0f;
            }
            ForestWind.SetMotionVectorPass(WindMotionVectors.Shipped);
            FoliageInteractionField field = FoliageInteractionField.Active;
            if (field != null)
            {
                field.Focus = null;
                field.FieldEnabled = true;
                field.InstanceTermsEnabled = true;
                field.InstanceTermsStill = false;
                field.WindDynamicsEnabled = true;
            }
        }

        /// <summary>
        /// The time offset that puts a gust crest over <paramref name="xz"/> <paramref name="seconds"/> into a window that starts now
        /// (spec 6.2): ForestWindGlobals.GustCentredOffset (the crest at offset + seconds) minus the Time.time the window starts at.
        /// </summary>
        public static float GustCentredOffset(ForestWind wind, Vector2 xz, float seconds) =>
            ForestWindGlobals.GustCentredOffset(wind.StateAt(Time.time), xz, seconds) - Time.time;

        /// <summary>
        /// The offset that puts the middle of the longest calm of a gust period (e under 0.01) over xz, seconds into a window starting now.
        /// W1 R2: R1 took the first moment whose 2 s average was the smallest, which with a calm whose e is exactly 0 for many seconds was
        /// the calm's first second, just after a gust; with the wind's dynamics (the stand rings down for a few seconds after a gust, by
        /// design) that is not yet a lull.
        /// </summary>
        public static float LullCentredOffset(ForestWind wind, Vector2 xz, float seconds)
        {
            ForestWindState state = wind.StateAt(Time.time);
            float period = state.gustWavelength / Mathf.Max(0.01f, state.gustSpeed);
            const float step = 0.05f;
            int n = Mathf.Max(1, Mathf.CeilToInt(period / step));
            var calm = new bool[n];
            for (int i = 0; i < n; i++) calm[i] = ForestWindGlobals.SampleGust(state, xz, i * step) < 0.01f;
            int bestStart = 0, bestLength = 0;
            for (int i = 0; i < n; i++)
            {
                if (!calm[i] || calm[(i - 1 + n) % n]) continue;   // a run starts here (wrapping round the period)
                int length = 0;
                while (length < n && calm[(i + length) % n]) length++;
                if (length > bestLength) { bestLength = length; bestStart = i; }
            }
            float best = bestLength > 0 ? (bestStart + 0.5f * bestLength) * step : 0f;
            if (bestLength == 0 && calm.Length > 0 && calm[0]) best = 0.5f * period;   // calm the whole period
            return best - seconds - Time.time;
        }

        /// <summary>A point 8 m ahead of a pose on the ground plane: where a view looks.</summary>
        public static Vector2 LookAt(Vector3 position, Quaternion rotation)
        {
            Vector3 forward = rotation * Vector3.forward;
            var flat = new Vector2(forward.x, forward.z);
            if (flat.sqrMagnitude < 1e-6f) flat = Vector2.up;
            return new Vector2(position.x, position.z) + flat.normalized * 8f;
        }

        /// <summary>The interaction field read back to the CPU (rgba per texel, row-major from the window's corner).</summary>
        public static Color[] ReadField(RenderTexture field)
        {
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(field, 0, TextureFormat.RGBAHalf);
            request.WaitForCompletion();
            Assert.IsFalse(request.hasError, "The interaction field could not be read back.");
            Unity.Collections.NativeArray<ushort> halves = request.GetData<ushort>();
            var colors = new Color[halves.Length / 4];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = new Color(Mathf.HalfToFloat(halves[4 * i]), Mathf.HalfToFloat(halves[4 * i + 1]), Mathf.HalfToFloat(halves[4 * i + 2]), Mathf.HalfToFloat(halves[4 * i + 3]));
            return colors;
        }

        /// <summary>The single-channel half texture of knob 2 read back.</summary>
        public static float[] ReadHalf(RenderTexture texture)
        {
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(texture, 0, TextureFormat.RHalf);
            request.WaitForCompletion();
            Assert.IsFalse(request.hasError, "The gust texture could not be read back.");
            Unity.Collections.NativeArray<ushort> halves = request.GetData<ushort>();
            var values = new float[halves.Length];
            for (int i = 0; i < values.Length; i++) values[i] = Mathf.HalfToFloat(halves[i]);
            return values;
        }

        /// <summary>A field texel at a world point (nearest texel), or zero outside the window.</summary>
        public static Color FieldAt(Color[] field, Vector4 rect, int resolution, Vector2 xz)
        {
            float size = 1f / rect.z, texel = size / resolution;
            int x = Mathf.FloorToInt((xz.x - rect.x) / texel), z = Mathf.FloorToInt((xz.y - rect.y) / texel);
            if (x < 0 || z < 0 || x >= resolution || z >= resolution) return Color.clear;
            return field[z * resolution + x];
        }

        /// <summary>
        /// W1 R1: every registered slot's push state (texel 3 of the instance texture: a passable sapling's lean (direction x, z, angle,
        /// height while it moves), a solid fir's swing sense), read back; index slot - 1.
        /// </summary>
        public static Vector4[] ReadInstanceStates(RenderTexture texture, int count)
        {
            var states = new Vector4[count];
            if (texture == null || count == 0) return states;
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(texture, 0, TextureFormat.RGBAFloat);
            request.WaitForCompletion();
            Assert.IsFalse(request.hasError, "The instance texture could not be read back.");
            Unity.Collections.NativeArray<float> values = request.GetData<float>();
            int width = texture.width;
            for (int i = 0; i < count; i++)
            {
                int x = (i % ForestWindInstances.Columns) * ForestWindInstances.TexelsPerSlot + 3, y = i / ForestWindInstances.Columns;
                int o = 4 * (y * width + x);
                states[i] = new Vector4(values[o], values[o + 1], values[o + 2], values[o + 3]);
            }
            return states;
        }

        /// <summary>
        /// W1 R2: every registered slot's whole push state, texels 3, 4 and 5 (ForestWind.hlsl's GamexPushState: a passable sapling's lean,
        /// its bow, clearance press and latched body; a solid fir's swing), read back in one request; slot i (1-based) at 3 (i - 1).
        /// </summary>
        public static Vector4[] ReadPushStates(RenderTexture texture, int count)
        {
            var states = new Vector4[3 * count];
            if (texture == null || count == 0) return states;
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(texture, 0, TextureFormat.RGBAFloat);
            request.WaitForCompletion();
            Assert.IsFalse(request.hasError, "The instance texture could not be read back.");
            Unity.Collections.NativeArray<float> values = request.GetData<float>();
            int width = texture.width;
            for (int i = 0; i < count; i++)
                for (int k = 0; k < 3; k++)
                {
                    int x = (i % ForestWindInstances.Columns) * ForestWindInstances.TexelsPerSlot + 3 + k, y = i / ForestWindInstances.Columns;
                    int o = 4 * (y * width + x);
                    states[3 * i + k] = new Vector4(values[o], values[o + 1], values[o + 2], values[o + 3]);
                }
            return states;
        }

        /// <summary>A copy of a render texture the GPU keeps (Graphics.CopyTexture), for probing the field after a walk.</summary>
        public static RenderTexture Snapshot(RenderTexture source)
        {
            var copy = new RenderTexture(source.width, source.height, 0, source.format, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            copy.Create();
            Graphics.CopyTexture(source, copy);
            return copy;
        }

        // ---- The raptor proxy ----

        /// <summary>
        /// A grey HDRP/Lit capsule 2.6 m long along its forward, 0.9 m thick, its belly 0.45 m above the ground, with a trigger collider
        /// (it never touches physics or the NavMesh) and a FoliagePusher of radius 0.45 m, length 2.6 m, band top 1.6 m (spec 6.2, 8.4).
        /// The root stands on the ground.
        /// </summary>
        public static FoliagePusher CreateRaptorProxy(Vector3 position, float yaw)
        {
            var root = new GameObject("Raptor Proxy");
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            body.transform.localScale = new Vector3(0.9f, 1.3f, 0.9f);
            var collider = body.GetComponent<Collider>();
            if (collider != null) collider.isTrigger = true;
            // W1 R1 (review of the I2c captures): a dark, rough body (albedo 0.04) instead of the primitive's default material, which blew
            // out to a white ellipsoid under the torch and covered the band the night walks are judged by.
            // W1 R2 (review of the R1 captures): it still blew out at 1 to 2 m in the torch (the light's inverse square against the night's
            // exposure); an unlit near-black body now, a dark silhouette in front of the lit foliage it parts.
            var renderer = body.GetComponent<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null)
            {
                Shader unlit = Shader.Find("HDRP/Unlit");
                Material dark;
                if (unlit != null)
                {
                    dark = new Material(unlit) { name = "Raptor Proxy (unlit dark)" };
                    if (dark.HasProperty("_UnlitColor")) dark.SetColor("_UnlitColor", new Color(0.012f, 0.011f, 0.010f, 1f));
                }
                else
                {
                    dark = new Material(renderer.sharedMaterial) { name = "Raptor Proxy (dark)" };
                    if (dark.HasProperty("_BaseColor")) dark.SetColor("_BaseColor", new Color(0.04f, 0.038f, 0.035f, 1f));
                    if (dark.HasProperty("_Smoothness")) dark.SetFloat("_Smoothness", 0.1f);
                }
                renderer.sharedMaterial = dark;
            }
            var pusher = root.AddComponent<FoliagePusher>();
            pusher.Configure(0.45f, 2.6f, 1.6f);
            return pusher;
        }

        /// <summary>The wind vectors of a renderer's first wind material (the profile the shader reads), for the GPU probe.</summary>
        public static void ProfileOf(Renderer renderer, ref ForestWindProbeSupport.Instance instance)
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (!ForestWind.IsWindMaterial(material)) continue;
                instance.sway = material.GetVector("_GamexSway");
                instance.flutter = material.GetVector("_GamexFlutter");
                instance.motion = material.GetVector("_GamexMotion");
                instance.push = material.GetVector("_GamexPush");
                instance.stem = material.GetVector("_GamexStem");
                return;
            }
            Assert.Fail($"{renderer.name} has no wind material.");
        }

        /// <summary>
        /// The motion vector pass on every loaded wind material whose name starts with <paramref name="prefix"/> (for example every
        /// young-fir level beyond the scan), recording the previous states in <paramref name="record"/> for <see cref="RestorePasses"/>.
        /// </summary>
        public static void SetPassFor(string prefix, bool on, Dictionary<Material, bool> record)
        {
            foreach (Material material in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (!ForestWind.IsWindMaterial(material) || !material.name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (!record.ContainsKey(material)) record.Add(material, material.GetShaderPassEnabled("MotionVectors"));
                material.SetShaderPassEnabled("MotionVectors", on);
            }
        }

        public static void RestorePasses(Dictionary<Material, bool> record)
        {
            foreach (KeyValuePair<Material, bool> entry in record)
                if (entry.Key != null) entry.Key.SetShaderPassEnabled("MotionVectors", entry.Value);
            record.Clear();
        }

        /// <summary>Hides or shows the proxy's body (its pusher keeps pushing).</summary>
        public static void ShowProxy(FoliagePusher proxy, bool visible)
        {
            foreach (Renderer renderer in proxy.GetComponentsInChildren<Renderer>(true)) renderer.enabled = visible;
        }

        /// <summary>Puts the proxy at a point of a path, on the ground, facing along it.</summary>
        public static void PlaceProxy(FoliagePusher proxy, Terrain terrain, Vector2 from, Vector2 to, float along)
        {
            Vector2 direction = (to - from).normalized;
            Vector2 xz = from + direction * along;
            proxy.transform.SetPositionAndRotation(OnGround(terrain, xz), Quaternion.LookRotation(new Vector3(direction.x, 0f, direction.y), Vector3.up));
        }

        // ---- Rendering ----

        /// <summary>A global volume with film grain off, at priority 1000 (the measurement volume of spec 6.2).</summary>
        public static Volume MeasurementVolume()
        {
            var holder = new GameObject("Forest Wind Measurement Volume");
            var volume = holder.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1000f;
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            FilmGrain grain = profile.Add<FilmGrain>(true);
            grain.intensity.Override(0f);
            volume.sharedProfile = profile;
            return volume;
        }

        public static void ReleaseVolume(Volume volume)
        {
            if (volume == null) return;
            Object.Destroy(volume.sharedProfile);
            Object.Destroy(volume.gameObject);
        }

        /// <summary>A camera cut to a pose: moved, rendered once, its history reset.</summary>
        public static void Cut(Camera camera, Vector3 position, Quaternion rotation)
        {
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.Render();
            if (RenderPipelineManager.currentPipeline is HDRenderPipeline)
                HDCamera.GetOrCreate(camera).Reset();
        }

        /// <summary>
        /// Renders <paramref name="warmup"/> frames and then <paramref name="frames"/> more, one per engine frame, and hands every
        /// <paramref name="every"/>-th of the latter to <paramref name="onFrame"/> (its index among the saved ones, its pixels).
        /// </summary>
        public static IEnumerator RenderSequence(Camera camera, int warmup, int frames, int every, Action<int, Color32[]> onFrame)
        {
            RenderTexture target = camera.targetTexture;
            for (int i = 0; i < warmup; i++)
            {
                camera.Render();
                yield return null;
            }
            for (int f = 0; f < frames; f++)
            {
                camera.Render();
                if (f % every == 0)
                    onFrame(f / every, ForestPlaySupport.ReadPixels(target));
                yield return null;
            }
        }

        /// <summary>The full-screen motion vector intensity view (spec 8.3), heat off, restored by Dispose.</summary>
        public sealed class MotionVectorView : IDisposable
        {
            readonly DebugDisplaySettings settings;
            readonly FullScreenDebugMode mode;
            readonly float scale;
            readonly bool heat;

            public MotionVectorView(bool on, float visualisationScale)
            {
                var pipeline = RenderPipelineManager.currentPipeline as HDRenderPipeline;
                Assert.IsNotNull(pipeline, "The motion-vector view needs HDRP.");
                settings = pipeline.debugDisplaySettings;
                mode = settings.data.fullScreenDebugMode;
                scale = settings.data.motionVecVisualizationScale;
                heat = settings.data.motionVecIntensityHeat;
                if (on)
                {
                    settings.SetFullScreenDebugMode(FullScreenDebugMode.MotionVectorsIntensity);
                    settings.data.motionVecVisualizationScale = visualisationScale;
                    settings.data.motionVecIntensityHeat = false;
                }
            }

            public void Dispose()
            {
                settings.SetFullScreenDebugMode(mode);
                settings.data.motionVecVisualizationScale = scale;
                settings.data.motionVecIntensityHeat = heat;
            }
        }

        /// <summary>A linear half-float target for reading the debug view's values unclamped.</summary>
        public static RenderTexture CreateLinearTarget(int width, int height) =>
            new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);

        public static float[] ReadRed(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                Color[] pixels = image.GetPixels();
                var red = new float[pixels.Length];
                for (int i = 0; i < pixels.Length; i++) red[i] = pixels[i].r;
                return red;
            }
            finally
            {
                RenderTexture.active = previous;
                Object.Destroy(image);
            }
        }

        // ---- Statistics and images ----

        public static float Luma(Color32 c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>Per-pixel temporal statistics of the encoded luma (0 to 255), Welford's method.</summary>
        public sealed class TemporalStats
        {
            public readonly int width, height;
            readonly double[] mean, m2;
            readonly float[] min, max;
            public int Count { get; private set; }

            public TemporalStats(int width, int height)
            {
                this.width = width;
                this.height = height;
                int n = width * height;
                mean = new double[n];
                m2 = new double[n];
                min = new float[n];
                max = new float[n];
            }

            public void Add(Color32[] pixels)
            {
                Count++;
                for (int i = 0; i < mean.Length; i++)
                {
                    float y = Luma(pixels[i]);
                    double delta = y - mean[i];
                    mean[i] += delta / Count;
                    m2[i] += delta * (y - mean[i]);
                    if (Count == 1) { min[i] = max[i] = y; }
                    else { if (y < min[i]) min[i] = y; if (y > max[i]) max[i] = y; }
                }
            }

            public float[] Std()
            {
                var std = new float[mean.Length];
                for (int i = 0; i < std.Length; i++) std[i] = Count > 1 ? (float)Math.Sqrt(m2[i] / (Count - 1)) : 0f;
                return std;
            }

            public float[] Range()
            {
                var range = new float[mean.Length];
                for (int i = 0; i < range.Length; i++) range[i] = max[i] - min[i];
                return range;
            }
        }

        /// <summary>The share of pixels where a exceeds b (or 0 without b) by more than delta.</summary>
        public static float ShareAbove(float[] a, float[] b, float delta)
        {
            int count = 0;
            for (int i = 0; i < a.Length; i++) if (a[i] - (b != null ? b[i] : 0f) > delta) count++;
            return a.Length > 0 ? count / (float)a.Length : 0f;
        }

        /// <summary>The share of pixels whose luma changed by more than delta between two frames.</summary>
        public static float ChangedShare(Color32[] a, Color32[] b, float delta, bool[] mask = null)
        {
            int count = 0, total = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (mask != null && !mask[i]) continue;
                total++;
                if (Mathf.Abs(Luma(a[i]) - Luma(b[i])) > delta) count++;
            }
            return total > 0 ? count / (float)total : 0f;
        }

        public static float MeanAbsDiff(Color32[] a, Color32[] b, bool[] mask = null)
        {
            double sum = 0.0;
            int total = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (mask != null && !mask[i]) continue;
                total++;
                sum += Mathf.Abs(Luma(a[i]) - Luma(b[i]));
            }
            return total > 0 ? (float)(sum / total) : 0f;
        }

        /// <summary>Black, blue, red, yellow, white over 0 to max.</summary>
        public static void SaveHeat(string path, float[] values, int width, int height, float maxValue)
        {
            var pixels = new Color32[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                float t = Mathf.Clamp01(values[i] / Mathf.Max(1e-6f, maxValue));
                Color c = t < 0.25f ? Color.Lerp(Color.black, new Color(0.1f, 0.2f, 1f), t / 0.25f)
                        : t < 0.5f ? Color.Lerp(new Color(0.1f, 0.2f, 1f), Color.red, (t - 0.25f) / 0.25f)
                        : t < 0.75f ? Color.Lerp(Color.red, Color.yellow, (t - 0.5f) / 0.25f)
                        : Color.Lerp(Color.yellow, Color.white, (t - 0.75f) / 0.25f);
                pixels[i] = c;
            }
            SavePng(path, pixels, width, height);
        }

        public static void SavePng(string path, Color32[] pixels, int width, int height)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.SetPixels32(pixels);
            image.Apply(false);
            File.WriteAllBytes(path, image.EncodeToPNG());
            Object.Destroy(image);
        }

        /// <summary>A grey PNG of linear values times a gain, clamped (the motion-vector view).</summary>
        public static void SaveGrey(string path, float[] values, int width, int height, float gain)
        {
            var pixels = new Color32[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                byte v = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(Mathf.Clamp01(values[i] * gain)) * 255f), 0, 255);
                pixels[i] = new Color32(v, v, v, 255);
            }
            SavePng(path, pixels, width, height);
        }

        /// <summary>
        /// Where the wind tests write their sequences: -windCaptureOut &lt;dir&gt; when given (the motion captures), else the PlayMode
        /// suite's output folder + "/wind".
        /// </summary>
        public static string OutputFolder(params string[] parts)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, "-windCaptureOut");
            string folder = index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : Path.Combine(ForestPlaySupport.OutputFolder(), "wind");
            foreach (string part in parts) folder = Path.Combine(folder, part);
            Directory.CreateDirectory(folder);
            return folder;
        }

        /// <summary>Saves frames only when -windSaveFrames is given or -windCaptureOut is (a suite run keeps only maps and stills).</summary>
        public static bool SaveFrames
        {
            get
            {
                string[] arguments = Environment.GetCommandLineArgs();
                return Array.IndexOf(arguments, "-windCaptureOut") >= 0 || Array.IndexOf(arguments, "-windSaveFrames") >= 0;
            }
        }

        /// <summary>A mask of the pixels within <paramref name="pixels"/> of the projected path (ground and 0.8 m above it).</summary>
        public static bool[] PathBand(Camera camera, Terrain terrain, Vector2 from, Vector2 to, int width, int height, float pixels, bool inside)
        {
            var points = new List<Vector2>();
            for (int i = 0; i <= 40; i++)
            {
                Vector2 xz = Vector2.Lerp(from, to, i / 40f);
                foreach (float lift in new[] { 0f, 0.4f, 0.8f })
                {
                    Vector3 screen = camera.WorldToViewportPoint(OnGround(terrain, xz, lift));
                    if (screen.z > 0f) points.Add(new Vector2(screen.x * width, screen.y * height));
                }
            }
            var near = new bool[width * height];
            float r2 = pixels * pixels;
            int r = Mathf.CeilToInt(pixels);
            foreach (Vector2 p in points)
            {
                int cx = Mathf.RoundToInt(p.x), cy = Mathf.RoundToInt(p.y);
                for (int y = Mathf.Max(0, cy - r); y <= Mathf.Min(height - 1, cy + r); y++)
                    for (int x = Mathf.Max(0, cx - r); x <= Mathf.Min(width - 1, cx + r); x++)
                        if ((p.x - x) * (p.x - x) + (p.y - y) * (p.y - y) <= r2) near[y * width + x] = true;
            }
            if (inside) return near;
            var mask = new bool[near.Length];
            for (int i = 0; i < mask.Length; i++) mask[i] = !near[i];
            return mask;
        }
    }
}
