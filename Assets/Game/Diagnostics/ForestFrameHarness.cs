using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace Gamex.Diagnostics
{
    /// <summary>
    /// Frame-time measurement in a player build (D2 of the forest work). It does nothing in the Editor, and nothing in a player
    /// that was not started with <c>-forestMeasure</c>. With the flag it switches vsync off, takes the player's camera (a copy of
    /// it, with the player rig switched off so nothing else moves or renders), visits the capture viewpoints the forest scene
    /// keeps under Gameplay/Viewpoints, waits for warm-up at each, records the length of every frame (Time.unscaledDeltaTime;
    /// FrameTimingManager's CPU and GPU times next to it, which a development build always collects), writes one JSON file and
    /// quits.
    ///
    /// Arguments (all optional except the flag):
    ///   -forestMeasureOut &lt;file.json&gt;        where the result goes (default: persistentDataPath/forest-measure.json)
    ///   -forestMeasureViews a,b,c              viewpoint ids (default: the 29 views of the M4/D1 Editor measurement)
    ///   -forestMeasureWidth / -Height          backbuffer size (default 2560 x 1440)
    ///   -forestMeasureMode fullscreen|windowed how to get that size (default fullscreen: a full-screen window whose
    ///                                          backbuffer is 2560 x 1440, scaled to the display). Measured on the owner's
    ///                                          MacBook Pro (2026-10-15, build/issues/D2-density.md): a focused window with vsync
    ///                                          off gets its drawables back in bursts, single frames read 2 ms or 30 ms around
    ///                                          the true mean and FrameTimingManager reports no GPU time; the full-screen window
    ///                                          paces evenly (median = mean) with GPU times. Windowed falls back to full screen
    ///                                          if the window does not get the size.
    ///   -forestMeasurePrewarm &lt;frames&gt;        frames per view in a first, unrecorded pass over every view (default 30)
    ///   -forestMeasureWarmup &lt;frames&gt;         frames after each camera cut before recording (default 120)
    ///   -forestMeasureFrames &lt;frames&gt;         recorded frames per view (default 300)
    ///   -forestMeasureFloor                    also record the first view with Environment switched off (the empty-scene floor)
    ///   -forestMeasureTag &lt;text&gt;              copied into the result
    ///   -forestMeasureDense &lt;k&gt;               a stress load for calibration: every LOD group of Environment copied k - 1 times
    ///                                          2.5 to 5.5 m away (seed 4242), as the D1 Editor measurement's "dense&lt;k&gt;"
    ///   -forestMeasurePace &lt;n&gt;                pace the CPU by the GPU: at most n frames in flight (a one-int readback queued
    ///                                          after every frame, waited for before frame n later begins). 0 = off
    ///                                          (default). Without it, a focused window on macOS hands the drawables back in bursts
    ///                                          with vsync off, and single frames read 2 ms or 30 ms while the mean is the real cost.
    ///   -forestMeasureFlashlight on|off        N1: the player's torch (Player/CameraPivot/Flashlight) rides on the measurement camera at
    ///                                          the hand's pose, switched on as the game starts it (on, the default), or stays dark with
    ///                                          the switched-off rig (off). Before N1 the scene had no torch.
    ///   -forestMeasurePreset &lt;name&gt;         N1: activate this lighting preset of the scene (Lighting/Preset_&lt;name&gt;) first
    ///   -forestMeasureTorchShadowRes &lt;px&gt;   N1: the torch's shadow map resolution for this run (A/B of the shadow settings)
    ///   -forestMeasureTorchShadows off         N1: the torch casts no shadows in this run
    ///   -forestMeasureTorchRange &lt;m&gt;         N1: the torch's range (and so its shadow map's far plane) for this run
    ///   -forestMeasureTorchVolumetric off      N1: the torch does not light the mist in this run
    ///   -forestMeasureTorchAB                  N1: paired measurement, free of the drift between runs: every view is recorded three
    ///                                          times in a row, the torch off ("&lt;view&gt;|off"), on ("|on") and on without shadows
    ///                                          ("|noshadow"), each after its own warm-up. The worst-view figures cover the "|on" rows.
    ///   -forestMeasureWindAB &lt;s1,s2,...&gt;     W1: paired measurement of wind states, every view recorded once per state in a row
    ///                                          ("&lt;view&gt;|&lt;state&gt;"), each after its own warm-up; the torch on. States (spec 7.3): off
    ///                                          (strength 0, field off, motion vector pass off on every wind material), on (as shipped),
    ///                                          mvall, nomv, mvyoung (the pass on every young-fir level), moon1, moon2, flutter21,
    ///                                          gusttex, torch15, d25, d20, d15, nofield, push (a
    ///                                          raptor-sized pusher walking 4 to 8 m in front of the camera), noinst (W1 I2b: every
    ///                                          vertex computes its instance's terms itself, as in I2), cull&lt;N&gt; (W1 I2b: every
    ///                                          forest LOD group ends at N m, at least 40), nocull (the scene's night cull off),
    ///                                          moon50 (W1 I2c: N1's moon shadows, 50 m and 3 cascades, on the scene's volumes),
    ///                                          sprint, pair, raptors3 (W1 R1: a player sprinting 6 m/s back and forth along 24 m
    ///                                          across the view 6 m ahead; the player standing at the camera and a raptor walking
    ///                                          16 to 20 m ahead; three raptors walking 4 to 8, 10 to 14 and 16 to 20 m ahead).
    ///                                          "a+b" applies both on top of
    ///                                          the shipped knobs. The worst-view figures cover the "|on" rows.
    ///   -forestMeasureWind &lt;state&gt;           W1: one wind state (as above) for the whole run.
    /// </summary>
    public sealed class ForestFrameHarness : MonoBehaviour
    {
        public const string Flag = "-forestMeasure";

        public static readonly string[] DefaultViews =
        {
            "trail_start", "trail_mid_fwd", "trail_mid_back", "clearing", "ground_close", "rock_close",
            "trail_strip_01", "trail_strip_02", "trail_strip_03", "trail_strip_04", "trail_strip_05", "trail_strip_06",
            "trail_strip_07", "trail_strip_08", "long_sightline", "backlit", "crouch_hide", "fir_close", "trunk_close",
            "boundary_n", "boundary_e", "boundary_s", "boundary_w", "pano_0", "pano_90", "pano_180", "pano_270",
            "hollow_rim", "deadfall",
        };

        const double MaxRunSeconds = 1800.0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.isEditor || Array.IndexOf(Environment.GetCommandLineArgs(), Flag) < 0)
                return;
            var holder = new GameObject("Forest Frame Harness");
            DontDestroyOnLoad(holder);
            holder.AddComponent<ForestFrameHarness>();
        }

        // ---- Result (JsonUtility) ----

        [Serializable]
        public sealed class ViewResult
        {
            public string id;
            public Vector3 position;
            public Vector3 euler;
            public int frames;
            public float medianMs, p95Ms, p99Ms, meanMs, minMs, maxMs;
            public int frameTimingFrames;
            public float gpuMedianMs, gpuP95Ms, cpuMedianMs, cpuP95Ms, cpuMainMedianMs, cpuRenderMedianMs;
            public long trianglesWorst, verticesWorst, setPassWorst, drawCallsWorst, batchesWorst, shadowCastersWorst;
            // The median over the recorded frames: in a player the counters now and then report two frames at once (exactly twice
            // the value), which the worst value above catches.
            public long trianglesMedian, verticesMedian, setPassMedian, shadowCastersMedian;
            public bool focusedThroughout;
            public string wind;                  // W1: the wind state and knobs this view was recorded with
            public float[] frameMs;
            public float[] gpuMs;
        }

        [Serializable]
        public sealed class RunResult
        {
            public string tag;
            public string startedUtc, finishedUtc;
            public string unityVersion, scene, deviceModel, graphicsDevice, graphicsDeviceType, operatingSystem, processor;
            public bool developmentBuild;
            public int requestedWidth, requestedHeight, screenWidth, screenHeight, cameraPixelWidth, cameraPixelHeight;
            public string fullScreenMode;
            public bool resolutionMatches;
            public int vSyncCount, targetFrameRate;
            public bool frameTimingFeatureEnabled;
            public string residentDrawer;
            public string flashlight, preset;
            public int torchShadowResolution;
            public bool torchShadows, torchVolumetric;
            public float torchRange;
            public int prewarmFrames, warmupFrames, recordedFrames, paceLag, denseFactor, denseCopies;
            public string windStates, windController, windShipped, windMaterials;
            public string worstViewByMedian, worstViewByP95;
            public float worstMedianMs, worstP95Ms;
            public float floorMedianMs, floorP95Ms;
            public string error;
            public List<ViewResult> views = new List<ViewResult>();
        }

        RunResult result;
        int paceLag;
        ComputeBuffer paceBuffer;
        readonly Queue<AsyncGPUReadbackRequest> pacing = new Queue<AsyncGPUReadbackRequest>();
        string outputPath;
        double startTime;
        bool finished;

        static string Value(string name, string fallback)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : fallback;
        }

        static int IntValue(string name, int fallback)
        {
            return int.TryParse(Value(name, null), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
        }

        static bool HasFlag(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        // ---- W1: wind states ----

        Camera measureCamera;
        string windDescription;
        // mvyoung: the young firs' plain materials (every level beyond the scan) with the motion vector pass on; restored per state.
        readonly Dictionary<Material, bool> youngPasses = new Dictionary<Material, bool>();
        // W1 R1: the pushers of the push states (review of the I2c captures: one box around every stamp grew with the distance between
        // bodies). Each walks its own path in front of the measurement camera (MoveProxies); kinds: 0 a raptor's loop, 1 a player's
        // sprint back and forth across the view, 2 a player standing at the camera.
        sealed class MeasureProxy { public FoliagePusher pusher; public int kind; public float centre, phase; }
        readonly List<MeasureProxy> proxies = new List<MeasureProxy>();
        string proxyMode = "";
        // W1 I2b: the night cull of a state ("cull<N>": every forest LOD group ends at N m on top of the scene's) and the scene's own
        // night cull components a state switched off ("nocull"); both undone when the next state is applied.
        ForestCullDistance.Restore stateCull;
        readonly List<ForestCullDistance> culledOff = new List<ForestCullDistance>();
        // W1 I2c: "moon50" puts N1's moon shadows (50 m, 3 cascades, splits 0.12 / 0.35 / 0.70) back on the shadow settings of every
        // enabled scene volume for one state, so the moon's saving (the owner's answer of 2026-10-17: 30 m and 2 cascades at night) and
        // W1's own cost are paired within a view; undone when the next state is applied.
        readonly List<(HDShadowSettings settings, float distance, int count, float split0, float split1, float split2)> moonRestore =
            new List<(HDShadowSettings, float, int, float, float, float)>();

        void RestoreMoon()
        {
            // The count first: a split is clamped by its neighbours only while enough cascades are in use.
            foreach ((HDShadowSettings settings, float distance, int count, float split0, float split1, float split2) in moonRestore)
            {
                if (settings == null) continue;
                settings.cascadeShadowSplitCount.value = count;
                settings.maxShadowDistance.value = distance;
                settings.cascadeShadowSplit0.value = split0;
                settings.cascadeShadowSplit1.value = split1;
                settings.cascadeShadowSplit2.value = split2;
            }
            moonRestore.Clear();
        }

        int ApplyMoon50()
        {
            foreach (Volume volume in FindObjectsByType<Volume>(FindObjectsInactive.Exclude))
            {
                if (!volume.isActiveAndEnabled || volume.sharedProfile == null) continue;
                if (!volume.sharedProfile.TryGet(out HDShadowSettings shadows) || !shadows.maxShadowDistance.overrideState) continue;
                moonRestore.Add((shadows, shadows.maxShadowDistance.value, shadows.cascadeShadowSplitCount.value,
                    shadows.cascadeShadowSplit0.value, shadows.cascadeShadowSplit1.value, shadows.cascadeShadowSplit2.value));
                shadows.cascadeShadowSplitCount.value = 3;
                shadows.maxShadowDistance.value = 50f;
                shadows.cascadeShadowSplit0.value = 0.12f;
                shadows.cascadeShadowSplit1.value = 0.35f;
                shadows.cascadeShadowSplit2.value = 0.70f;
            }
            return moonRestore.Count;
        }

        /// <summary>
        /// Puts the wind into a named state of spec 7.3 (or "a+b": each token on top of the shipped knobs, in order); null when it
        /// worked, else what is wrong. Always starts from the shipped state: knobs reset, motion vector pass as built, field on.
        /// </summary>
        string ApplyWind(string state)
        {
            ForestWind wind = ForestWind.Active;
            FoliageInteractionField field = FoliageInteractionField.Active;
            if (wind == null) return "no ForestWind is enabled";
            wind.ResetKnobs();
            ForestWind.SetMotionVectorPass(WindMotionVectors.Shipped);
            foreach (KeyValuePair<Material, bool> entry in youngPasses)
                if (entry.Key != null) entry.Key.SetShaderPassEnabled("MotionVectors", entry.Value);
            youngPasses.Clear();
            bool young = false;
            if (field != null) field.FieldEnabled = true;
            bool ownTerms = false;
            stateCull?.Undo();
            stateCull = null;
            foreach (ForestCullDistance cull in culledOff)
                if (cull != null) cull.enabled = true;
            culledOff.Clear();
            RestoreMoon();
            bool moon50 = false;
            int cullMetres = 0;
            string push = "";
            ForestWindKnobs k = wind.Knobs;
            WindMotionVectors passes = WindMotionVectors.Shipped;
            foreach (string raw in (state ?? "on").Split('+'))
            {
                string token = raw.Trim();
                switch (token)
                {
                    case "on": break;
                    case "off": k.strengthScale = 0f; k.fieldEnabled = false; passes = WindMotionVectors.None; break;
                    case "mvall": passes = WindMotionVectors.All; break;
                    case "nomv": passes = WindMotionVectors.None; break;
                    case "mvyoung": young = true; break;
                    case "moon1": k.moonCascades = 1; break;
                    case "moon2": k.moonCascades = 2; break;
                    case "flutter21": k.flutterEnd = 0.7f * k.fadeEnd; break;
                    case "gusttex": k.gustTexture = true; break;
                    case "nogusttex": k.gustTexture = false; break;
                    case "torch15": k.torchShadowEnd = 15f; break;
                    case "nofield": k.fieldEnabled = false; break;
                    case "push": push = "push"; break;
                    case "sprint": push = "sprint"; break;       // W1 R1: a player sprinting at 6 m/s across the view, 24 m back and forth
                    case "pair": push = "pair"; break;           // W1 R1: the player at the camera and a raptor walking 18 m ahead
                    case "raptors3": push = "raptors3"; break;   // W1 R1: three raptors walking 6, 12 and 18 m ahead
                    case "noinst": ownTerms = true; break;   // W1 I2b: every vertex computes its instance's terms itself (I2's way)
                    case "moon50": moon50 = true; break;     // W1 I2c: N1's moon shadows (50 m, 3 cascades) for this state
                    case "nocull":                             // W1 I2b: the scene's night cull off (the kit's own cull distances)
                        foreach (ForestCullDistance cull in FindObjectsByType<ForestCullDistance>())
                            if (cull.enabled) { cull.enabled = false; culledOff.Add(cull); }
                        break;
                    default:
                        if (token.Length > 1 && token[0] == 'd' && int.TryParse(token.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int metres) && metres > 0)
                        {
                            k.fadeEnd = metres;
                            k.flutterEnd = Mathf.Min(k.flutterEnd, 0.7f * metres);
                            break;
                        }
                        if (token.Length > 4 && token.StartsWith("cull", StringComparison.Ordinal) && int.TryParse(token.Substring(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cullAt) && cullAt > 0)
                        {
                            cullMetres = cullAt;
                            break;
                        }
                        if (token.Length > 7 && token.StartsWith("flutter", StringComparison.Ordinal) && int.TryParse(token.Substring(7), NumberStyles.Integer, CultureInfo.InvariantCulture, out int flutter))
                        {
                            k.flutterEnd = Mathf.Min(flutter, 0.7f * k.fadeEnd);
                            break;
                        }
                        return $"unknown wind state '{token}'";
                }
            }
            wind.Knobs = k;
            ForestWind.SetMotionVectorPass(passes);
            if (young)
                foreach (Material material in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if (!ForestWind.IsWindMaterial(material) || !material.name.StartsWith("fir_sapling_medium", StringComparison.Ordinal)) continue;
                    if (!youngPasses.ContainsKey(material)) youngPasses.Add(material, material.GetShaderPassEnabled("MotionVectors"));
                    material.SetShaderPassEnabled("MotionVectors", true);
                }
            if (field != null) field.FieldEnabled = k.fieldEnabled;
            if (field != null) field.InstanceTermsEnabled = !ownTerms;
            if (cullMetres > 0)
                stateCull = ForestCullDistance.Apply(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), cullMetres);
            int moonVolumes = moon50 ? ApplyMoon50() : 0;
            if (moon50 && moonVolumes == 0) return "moon50: no enabled volume overrides the shadow settings";
            SetProxies(push);
            string pushers = push == "push" ? ", raptor proxy walking 4 to 8 m ahead" : push == "sprint" ? ", a player sprinting 6 m/s across the view (24 m) 6 m ahead"
                : push == "pair" ? ", the player at the camera and a raptor walking 16 to 20 m ahead" : push == "raptors3" ? ", three raptors walking 4 to 8, 10 to 14 and 16 to 20 m ahead" : "";
            windDescription = $"{state}: {k}, motion vectors {passes}{(young ? " + young firs' every level" : "")}{pushers}" +
                (field != null ? $", per-instance terms {(field.InstanceTermsEnabled ? $"from {field.Instances.Count} slots" : "in the vertex shader")}" : "") +
                (stateCull != null ? $", forest culled at {Mathf.Max(ForestCullDistance.Minimum, cullMetres)} m ({stateCull.Count} LOD groups)" : "") +
                (culledOff.Count > 0 ? ", the scene's night cull off" : "") +
                (moon50 ? $", moon shadows 50 m / 3 cascades (N1's) on {moonVolumes} volume(s)" : "");
            Debug.Log("Forest harness: wind " + windDescription);
            return null;
        }

        // The pushers of a push state. A raptor: radius 0.45 m, 2.6 m long, band top 1.6 m, as the PlayMode tests' proxy; a player: radius
        // 0.35 m, round, band top 1.8 m. No renderer and no collider: only their effect on the foliage (the field's stamps, the pressed
        // instances and the pushed vertices) is measured.
        void SetProxies(string mode)
        {
            if (mode != proxyMode)
            {
                foreach (MeasureProxy old in proxies)
                    if (old.pusher != null) Destroy(old.pusher.gameObject);
                proxies.Clear();
                void Add(int kind, float centre, float phase)
                {
                    var holder = new GameObject("Forest Measure Proxy " + proxies.Count);
                    DontDestroyOnLoad(holder);
                    var pusher = holder.AddComponent<FoliagePusher>();
                    if (kind == 0) pusher.Configure(0.45f, 2.6f, 1.6f);
                    else pusher.Configure(0.35f, 0f, 1.8f, isPlayer: true);
                    proxies.Add(new MeasureProxy { pusher = pusher, kind = kind, centre = centre, phase = phase });
                }
                switch (mode)
                {
                    case "push": Add(0, 6f, 0f); break;
                    case "sprint": Add(1, 6f, 0f); break;
                    case "pair": Add(2, 0f, 0f); Add(0, 18f, 0f); break;
                    case "raptors3": Add(0, 6f, 0f); Add(0, 12f, 2.1f); Add(0, 18f, 4.2f); break;
                }
                proxyMode = mode;
            }
            foreach (MeasureProxy proxy in proxies)
            {
                proxy.phase = proxy.kind == 0 ? proxy.phase : 0f;
                MoveProxy(proxy, 0f);
                proxy.pusher.gameObject.SetActive(true);
            }
        }

        // A raptor walks an ellipse on the ground in front of the camera, centre m ahead, 2 m either way along the view and 1 m to either
        // side (about 9.7 m round), at 1.5 m/s, facing where it walks. A sprinting player runs 6 m/s back and forth along a 24 m line across
        // the view, centre m ahead. A standing player stays under the camera.
        void MoveProxy(MeasureProxy proxy, float deltaTime)
        {
            if (proxy.pusher == null || measureCamera == null) return;
            Transform view = measureCamera.transform;
            Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 position, heading;
            if (proxy.kind == 0)
            {
                const float depth = 2f, side = 1f, speed = 1.5f;
                // The perimeter is about 2 pi sqrt((a^2 + b^2) / 2), so this angular speed walks it at the given speed.
                proxy.phase += deltaTime * speed / Mathf.Sqrt(0.5f * (depth * depth + side * side));
                Vector3 Point(float a) => view.position + forward * (proxy.centre + depth * Mathf.Cos(a)) + right * (side * Mathf.Sin(a));
                position = Point(proxy.phase);
                heading = Point(proxy.phase + 0.05f) - position;
            }
            else if (proxy.kind == 1)
            {
                const float halfLength = 12f, speed = 6f;
                proxy.phase += deltaTime * speed;
                float u = Mathf.PingPong(proxy.phase, 2f * halfLength) - halfLength;
                bool outward = Mathf.Repeat(proxy.phase, 4f * halfLength) < 2f * halfLength;
                position = view.position + forward * proxy.centre + right * u;
                heading = outward ? right : -right;
            }
            else
            {
                position = view.position;
                heading = forward;
            }
            Terrain terrain = Terrain.activeTerrain;
            position.y = terrain != null ? terrain.SampleHeight(position) + terrain.transform.position.y : view.position.y - 1.65f;
            heading.y = 0f;
            proxy.pusher.transform.SetPositionAndRotation(position, heading.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(heading.normalized, Vector3.up) : Quaternion.identity);
        }

        void Update()
        {
            foreach (MeasureProxy proxy in proxies)
                if (proxy.pusher != null && proxy.pusher.gameObject.activeSelf)
                    MoveProxy(proxy, Time.deltaTime);

            // Pacing: at most paceLag frames in flight. Before this frame's work begins, the GPU must have finished every frame
            // but the last paceLag - 1 (1: the previous frame, CPU and GPU serialised; 2: the one before it, one frame overlaps).
            while (paceLag > 0 && pacing.Count > paceLag - 1)
            {
                AsyncGPUReadbackRequest request = pacing.Dequeue();
                if (!request.done) request.WaitForCompletion();
            }

            // A watchdog: a run that hangs must still end and say so.
            if (!finished && Time.realtimeSinceStartupAsDouble - startTime > MaxRunSeconds)
                Fail($"the run took longer than {MaxRunSeconds:0} s");
        }

        IEnumerator Start()
        {
            startTime = Time.realtimeSinceStartupAsDouble;
            outputPath = Value("-forestMeasureOut", Path.Combine(Application.persistentDataPath, "forest-measure.json"));
            int width = IntValue("-forestMeasureWidth", 2560), height = IntValue("-forestMeasureHeight", 1440);
            int prewarm = IntValue("-forestMeasurePrewarm", 30), warmup = IntValue("-forestMeasureWarmup", 120), frames = IntValue("-forestMeasureFrames", 300);
            string viewList = Value("-forestMeasureViews", null);
            string[] viewIds = string.IsNullOrEmpty(viewList) ? DefaultViews : viewList.Split(',');
            bool fullscreenFirst = Value("-forestMeasureMode", "fullscreen") != "windowed";
            paceLag = Mathf.Max(0, IntValue("-forestMeasurePace", 0));

            result = new RunResult
            {
                tag = Value("-forestMeasureTag", ""),
                startedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                scene = SceneManager.GetActiveScene().path,
                deviceModel = SystemInfo.deviceModel,
                graphicsDevice = SystemInfo.graphicsDeviceName,
                graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
                operatingSystem = SystemInfo.operatingSystem,
                processor = SystemInfo.processorType,
                developmentBuild = Debug.isDebugBuild,
                requestedWidth = width,
                requestedHeight = height,
                prewarmFrames = prewarm,
                warmupFrames = warmup,
                recordedFrames = frames,
                paceLag = paceLag,
            };
            if (paceLag > 0)
            {
                paceBuffer = new ComputeBuffer(1, sizeof(int));
                StartCoroutine(PaceLoop());
            }
            Debug.Log($"Forest harness: start, output {outputPath}, {viewIds.Length} views, prewarm {prewarm}, warm-up {warmup}, frames {frames}");

            // Keep running when the window loses focus (the project's Player setting says otherwise), no vsync, no frame cap.
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;

            // The backbuffer size. A Retina window holds 2560 x 1440 pixels on the owner's 3456 x 2234 display.
            FullScreenMode[] modes = fullscreenFirst
                ? new[] { FullScreenMode.FullScreenWindow }
                : new[] { FullScreenMode.Windowed, FullScreenMode.FullScreenWindow };
            foreach (FullScreenMode mode in modes)
            {
                Screen.SetResolution(width, height, mode);
                for (int i = 0; i < 30; i++)
                    yield return null;
                Debug.Log($"Forest harness: asked for {width} x {height} {mode}, screen is {Screen.width} x {Screen.height} {Screen.fullScreenMode}");
                if (Screen.width == width && Screen.height == height)
                    break;
            }

            GameObject player = FindRoot("Player");
            Camera playerCamera = player != null ? player.GetComponentInChildren<Camera>(true) : null;
            if (playerCamera == null) { Fail("the scene has no Player root with a camera"); yield break; }
            Camera camera = CreateCamera(playerCamera);
            string torchMode = Value("-forestMeasureFlashlight", "on");
            Transform torch = playerCamera.transform.Find("Flashlight");
            if (torch != null && torchMode == "on")
            {
                // Keeps its local pose (the hand) and its own component, which follows the new parent with its lag.
                torch.SetParent(camera.transform, false);
                var light = torch.GetComponent<Light>();
                if (light != null) light.enabled = true;
            }
            result.flashlight = torch == null ? "none" : torchMode == "on" ? "on" : "off";
            if (torch != null)
            {
                var torchLight = torch.GetComponent<Light>();
                var torchData = torch.GetComponent<HDAdditionalLightData>();
                int resolution = IntValue("-forestMeasureTorchShadowRes", 0);
                if (resolution > 0 && torchData != null) torchData.SetShadowResolution(resolution);
                if (Value("-forestMeasureTorchShadows", "on") == "off" && torchLight != null) torchLight.shadows = LightShadows.None;
                float range = IntValue("-forestMeasureTorchRange", 0);
                if (range > 0f && torchLight != null) torchLight.range = range;
                if (Value("-forestMeasureTorchVolumetric", "on") == "off" && torchData != null) torchData.affectsVolumetric = false;
                result.torchRange = torchLight != null ? torchLight.range : 0f;
                result.torchVolumetric = torchData != null && torchData.affectsVolumetric;
                result.torchShadows = torchLight != null && torchLight.shadows != LightShadows.None;
                result.torchShadowResolution = torchData != null && torchData.shadowResolution.useOverride ? torchData.shadowResolution.@override : -1;
                Debug.Log($"Forest harness: torch shadows {result.torchShadows}, resolution {result.torchShadowResolution}, update {torchData?.shadowUpdateMode}");
            }
            player.SetActive(false);

            string preset = Value("-forestMeasurePreset", null);
            if (!string.IsNullOrEmpty(preset) && !UsePreset(preset)) { Fail($"the scene has no lighting preset Lighting/Preset_{preset}"); yield break; }
            result.preset = ActivePreset();
            Debug.Log($"Forest harness: preset {result.preset}, flashlight {result.flashlight}");

            int dense = IntValue("-forestMeasureDense", 1);
            if (dense > 1)
            {
                result.denseFactor = dense;
                result.denseCopies = Densify(dense);
                Debug.Log($"Forest harness: dense{dense}: {result.denseCopies} copies added");
                yield return null;
            }

            GameObject gameplay = FindRoot("Gameplay");
            Transform viewpointGroup = gameplay != null ? gameplay.transform.Find("Viewpoints") : null;
            if (viewpointGroup == null) { Fail("the scene has no Gameplay/Viewpoints"); yield break; }
            var viewpoints = new List<Transform>();
            foreach (string id in viewIds)
            {
                Transform viewpoint = viewpointGroup.Find(id.Trim());
                if (viewpoint == null) { Fail($"the scene has no viewpoint Gameplay/Viewpoints/{id}"); yield break; }
                viewpoints.Add(viewpoint);
            }

            yield return null;
            result.screenWidth = Screen.width;
            result.screenHeight = Screen.height;
            result.fullScreenMode = Screen.fullScreenMode.ToString();
            result.cameraPixelWidth = camera.pixelWidth;
            result.cameraPixelHeight = camera.pixelHeight;
            result.resolutionMatches = camera.pixelWidth == width && camera.pixelHeight == height;
            result.vSyncCount = QualitySettings.vSyncCount;
            result.targetFrameRate = Application.targetFrameRate;
            result.frameTimingFeatureEnabled = FrameTimingManager.IsFeatureEnabled();
            Debug.Log($"Forest harness: camera renders {camera.pixelWidth} x {camera.pixelHeight}; vsync {QualitySettings.vSyncCount}; frame timing {result.frameTimingFeatureEnabled}");

            // W1: the wind controller of the active preset and the interaction field, centred on the measurement camera.
            string windAB = Value("-forestMeasureWindAB", null), windSingle = Value("-forestMeasureWind", null);
            bool windPaired = !string.IsNullOrEmpty(windAB);
            measureCamera = camera;
            if (windPaired || !string.IsNullOrEmpty(windSingle))
            {
                ForestWind wind = ForestWind.Active;
                if (wind == null) { Fail("a wind state was asked for, but no ForestWind is enabled (Lighting/Preset_<name>)"); yield break; }
                result.windController = wind.name;
                result.windShipped = wind.Knobs.ToString();
                result.windStates = windPaired ? windAB : windSingle;
                int windMaterials = 0, passOn = 0;
                foreach (Material material in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if (!ForestWind.IsWindMaterial(material)) continue;
                    windMaterials++;
                    if (material.GetShaderPassEnabled("MotionVectors")) passOn++;
                }
                result.windMaterials = $"{windMaterials} wind materials loaded, motion vector pass on {passOn}";
                Debug.Log($"Forest harness: wind {wind.name}, shipped knobs {result.windShipped}; {result.windMaterials}; states {result.windStates}");
                if (!windPaired)
                {
                    string problem = ApplyWind(windSingle);
                    if (problem != null) { Fail(problem); yield break; }
                }
            }
            if (FoliageInteractionField.Active != null)
                FoliageInteractionField.Active.Focus = camera.transform;

            // A first pass over every view, not recorded: shaders, pipeline states and streaming settle here.
            foreach (Transform viewpoint in viewpoints)
            {
                Cut(camera, viewpoint);
                for (int i = 0; i < prewarm; i++)
                    yield return null;
            }
            result.residentDrawer = DrawerState();
            Debug.Log("Forest harness: " + result.residentDrawer);

            var recorders = new Dictionary<string, ProfilerRecorder>();
            foreach (string name in new[] { "Triangles Count", "Vertices Count", "SetPass Calls Count", "Draw Calls Count", "Batches Count", "Shadow Casters Count" })
                recorders[name] = ProfilerRecorder.StartNew(ProfilerCategory.Render, name);
            var timings = new FrameTiming[1];
            try
            {
                Light pairedLight = !windPaired && HasFlag("-forestMeasureTorchAB") && torch != null && torch.parent == camera.transform ? torch.GetComponent<Light>() : null;
                string[] states = windPaired ? windAB.Split(',') : pairedLight != null ? new[] { "off", "on", "noshadow" } : new string[] { null };
                foreach (Transform viewpoint in viewpoints)
                {
                    foreach (string state in states)
                    {
                        if (pairedLight != null)
                        {
                            pairedLight.enabled = state != "off";
                            pairedLight.shadows = state == "noshadow" ? LightShadows.None : LightShadows.Soft;
                        }
                        if (windPaired)
                        {
                            string problem = ApplyWind(state);
                            if (problem != null) { Fail(problem); yield break; }
                        }
                        Cut(camera, viewpoint);
                        for (int i = 0; i < warmup; i++)
                            yield return null;
                        ViewResult view = null;
                        yield return Record(state == null ? viewpoint.name : viewpoint.name + "|" + state, viewpoint, frames, recorders, timings, r => view = r);
                        view.wind = windDescription + (FoliageInteractionField.Active != null ? $"; {FoliageInteractionField.Active.Instances.PressedCount} instances pressed at the end" : "");
                        result.views.Add(view);
                        Debug.Log(string.Format(CultureInfo.InvariantCulture, "Forest harness: {0} median {1:0.00} ms p95 {2:0.00} ms (gpu median {3:0.00}, cpu median {4:0.00}, {5} timing frames), triangles {6}",
                            view.id, view.medianMs, view.p95Ms, view.gpuMedianMs, view.cpuMedianMs, view.frameTimingFrames, view.trianglesWorst));
                    }
                }
                if (pairedLight != null)
                {
                    pairedLight.enabled = true;
                    pairedLight.shadows = LightShadows.Soft;
                }
                if (windPaired)
                    ApplyWind("on");

                if (HasFlag("-forestMeasureFloor"))
                {
                    GameObject environment = FindRoot("Environment");
                    if (environment != null)
                    {
                        environment.SetActive(false);
                        Cut(camera, viewpoints[0]);
                        for (int i = 0; i < warmup; i++)
                            yield return null;
                        ViewResult floor = null;
                        yield return Record("floor_" + viewpoints[0].name, viewpoints[0], frames, recorders, timings, r => floor = r);
                        result.floorMedianMs = floor.medianMs;
                        result.floorP95Ms = floor.p95Ms;
                        floor.frameMs = null;
                        floor.gpuMs = null;
                        result.views.Add(floor);
                        environment.SetActive(true);
                        Debug.Log($"Forest harness: floor (Environment off) median {floor.medianMs:0.00} ms p95 {floor.p95Ms:0.00} ms");
                    }
                }
            }
            finally
            {
                foreach (ProfilerRecorder recorder in recorders.Values)
                    recorder.Dispose();
            }

            foreach (ViewResult view in result.views)
            {
                if (view.id.StartsWith("floor_", StringComparison.Ordinal)) continue;
                if (view.id.Contains("|") && !view.id.EndsWith("|on", StringComparison.Ordinal)) continue;
                if (view.medianMs > result.worstMedianMs) { result.worstMedianMs = view.medianMs; result.worstViewByMedian = view.id; }
                if (view.p95Ms > result.worstP95Ms) { result.worstP95Ms = view.p95Ms; result.worstViewByP95 = view.id; }
            }
            Finish(0);
        }

        IEnumerator PaceLoop()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (!finished)
            {
                yield return endOfFrame;
                pacing.Enqueue(AsyncGPUReadback.Request(paceBuffer));
            }
        }

        IEnumerator Record(string id, Transform viewpoint, int frames, Dictionary<string, ProfilerRecorder> recorders, FrameTiming[] timings, Action<ViewResult> done)
        {
            var frameMs = new List<double>(frames);
            var gpu = new List<double>(frames);
            var cpu = new List<double>(frames);
            var cpuMain = new List<double>(frames);
            var cpuRender = new List<double>(frames);
            var triangles = new List<double>(frames);
            var vertices = new List<double>(frames);
            var setPass = new List<double>(frames);
            var casters = new List<double>(frames);
            var view = new ViewResult { id = id, position = viewpoint.position, euler = viewpoint.eulerAngles, focusedThroughout = true };
            for (int i = 0; i < frames; i++)
            {
                yield return null;
                frameMs.Add(Time.unscaledDeltaTime * 1000.0);
                if (!Application.isFocused) view.focusedThroughout = false;
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                {
                    if (timings[0].gpuFrameTime > 0.0) gpu.Add(timings[0].gpuFrameTime);
                    if (timings[0].cpuFrameTime > 0.0) cpu.Add(timings[0].cpuFrameTime);
                    if (timings[0].cpuMainThreadFrameTime > 0.0) cpuMain.Add(timings[0].cpuMainThreadFrameTime);
                    if (timings[0].cpuRenderThreadFrameTime > 0.0) cpuRender.Add(timings[0].cpuRenderThreadFrameTime);
                }
                triangles.Add(Last(recorders, "Triangles Count"));
                vertices.Add(Last(recorders, "Vertices Count"));
                setPass.Add(Last(recorders, "SetPass Calls Count"));
                casters.Add(Last(recorders, "Shadow Casters Count"));
                view.trianglesWorst = Math.Max(view.trianglesWorst, Last(recorders, "Triangles Count"));
                view.verticesWorst = Math.Max(view.verticesWorst, Last(recorders, "Vertices Count"));
                view.setPassWorst = Math.Max(view.setPassWorst, Last(recorders, "SetPass Calls Count"));
                view.drawCallsWorst = Math.Max(view.drawCallsWorst, Last(recorders, "Draw Calls Count"));
                view.batchesWorst = Math.Max(view.batchesWorst, Last(recorders, "Batches Count"));
                view.shadowCastersWorst = Math.Max(view.shadowCastersWorst, Last(recorders, "Shadow Casters Count"));
            }
            view.frames = frameMs.Count;
            view.trianglesMedian = (long)Percentile(triangles, 0.5);
            view.verticesMedian = (long)Percentile(vertices, 0.5);
            view.setPassMedian = (long)Percentile(setPass, 0.5);
            view.shadowCastersMedian = (long)Percentile(casters, 0.5);
            view.medianMs = Percentile(frameMs, 0.5);
            view.p95Ms = Percentile(frameMs, 0.95);
            view.p99Ms = Percentile(frameMs, 0.99);
            view.minMs = Percentile(frameMs, 0.0);
            view.maxMs = Percentile(frameMs, 1.0);
            double sum = 0.0;
            foreach (double value in frameMs) sum += value;
            view.meanMs = (float)(sum / Math.Max(1, frameMs.Count));
            view.frameTimingFrames = gpu.Count;
            view.gpuMedianMs = Percentile(gpu, 0.5);
            view.gpuP95Ms = Percentile(gpu, 0.95);
            view.cpuMedianMs = Percentile(cpu, 0.5);
            view.cpuP95Ms = Percentile(cpu, 0.95);
            view.cpuMainMedianMs = Percentile(cpuMain, 0.5);
            view.cpuRenderMedianMs = Percentile(cpuRender, 0.5);
            view.frameMs = Round(frameMs);
            view.gpuMs = Round(gpu);
            done(view);
        }

        static long Last(Dictionary<string, ProfilerRecorder> recorders, string name)
        {
            return recorders.TryGetValue(name, out ProfilerRecorder recorder) && recorder.Valid ? recorder.LastValue : 0;
        }

        static float[] Round(List<double> values)
        {
            var rounded = new float[values.Count];
            for (int i = 0; i < values.Count; i++)
                rounded[i] = (float)Math.Round(values[i], 3);
            return rounded;
        }

        /// <summary>The value below which the given share of the samples lies (nearest rank), as ForestPlaySupport.Percentile.</summary>
        static float Percentile(List<double> values, double share)
        {
            if (values.Count == 0) return 0f;
            var sorted = new List<double>(values);
            sorted.Sort();
            int rank = (int)Math.Ceiling(share * sorted.Count) - 1;
            return (float)sorted[Math.Max(0, Math.Min(sorted.Count - 1, rank))];
        }

        /// <summary>The D1 stand-in for density: each LOD group of Environment copied factor - 1 times nearby, on the ground.</summary>
        static int Densify(int factor)
        {
            GameObject environment = FindRoot("Environment");
            Transform terrainTransform = environment != null ? environment.transform.Find("Terrain") : null;
            Terrain terrain = terrainTransform != null ? terrainTransform.GetComponent<Terrain>() : null;
            if (terrain == null) return 0;
            var random = new System.Random(4242);
            LODGroup[] groups = environment.GetComponentsInChildren<LODGroup>(false);
            int copies = 0;
            foreach (LODGroup group in groups)
            {
                Transform source = group.transform;
                float ground = terrain.SampleHeight(source.position) + terrain.transform.position.y;
                float lift = source.position.y - ground;
                for (int k = 1; k < factor; k++)
                {
                    double angle = random.NextDouble() * 2.0 * Math.PI, radius = 2.5 + random.NextDouble() * 3.0;
                    float x = source.position.x + (float)(Math.Cos(angle) * radius), z = source.position.z + (float)(Math.Sin(angle) * radius);
                    var position = new Vector3(x, terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y + lift, z);
                    Quaternion rotation = Quaternion.Euler(0f, (float)(random.NextDouble() * 360.0), 0f) * source.rotation;
                    Instantiate(group.gameObject, position, rotation, source.parent);
                    copies++;
                }
            }
            return copies;
        }

        static bool UsePreset(string preset)
        {
            GameObject lighting = FindRoot("Lighting");
            Transform target = lighting != null ? lighting.transform.Find("Preset_" + preset) : null;
            if (target == null) return false;
            foreach (Transform child in lighting.transform)
                if (child != target && child.name.StartsWith("Preset_", StringComparison.Ordinal)) child.gameObject.SetActive(false);
            target.gameObject.SetActive(true);
            return true;
        }

        static string ActivePreset()
        {
            GameObject lighting = FindRoot("Lighting");
            if (lighting == null) return "";
            foreach (Transform child in lighting.transform)
                if (child.gameObject.activeSelf && child.name.StartsWith("Preset_", StringComparison.Ordinal)) return child.name.Substring("Preset_".Length);
            return "";
        }

        static GameObject FindRoot(string name)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == name) return root;
            return null;
        }

        /// <summary>A copy of the player camera: lens, HDRP settings (TAA and the rest), rendering to the screen.</summary>
        static Camera CreateCamera(Camera playerCamera)
        {
            var holder = new GameObject("Forest Measure Camera");
            var camera = holder.AddComponent<Camera>();
            camera.CopyFrom(playerCamera);
            camera.targetTexture = null;
            camera.ResetAspect();
            camera.enabled = true;
            holder.tag = "MainCamera";
            var data = holder.GetComponent<HDAdditionalCameraData>();
            if (data == null) data = holder.AddComponent<HDAdditionalCameraData>();
            var playerData = playerCamera.GetComponent<HDAdditionalCameraData>();
            if (playerData != null) playerData.CopyTo(data);
            DontDestroyOnLoad(holder);
            return camera;
        }

        /// <summary>Moves the camera and clears its history (TAA, exposure adaptation stays per camera): a camera cut.</summary>
        static void Cut(Camera camera, Transform viewpoint)
        {
            camera.transform.SetPositionAndRotation(viewpoint.position, viewpoint.rotation);
            if (RenderPipelineManager.currentPipeline is HDRenderPipeline)
                HDCamera.GetOrCreate(camera).Reset();
        }

        static string DrawerState()
        {
            try
            {
                Type drawer = Type.GetType("UnityEngine.Rendering.GPUResidentDrawer, Unity.RenderPipelines.GPUDriven.Runtime");
                if (drawer == null) return "GPU Resident Drawer: type not found";
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                object initialized = drawer.GetMethod("IsInitialized", flags, null, Type.EmptyTypes, null)?.Invoke(null, null);
                object occlusion = drawer.GetMethod("IsInstanceOcclusionCullingEnabled", flags, null, Type.EmptyTypes, null)?.Invoke(null, null);
                return $"GPU Resident Drawer: initialized {initialized ?? "unknown"}, occlusion culling {occlusion ?? "unknown"}";
            }
            catch (Exception exception)
            {
                return "GPU Resident Drawer: unreadable (" + exception.GetType().Name + ")";
            }
        }

        void Fail(string message)
        {
            Debug.LogError("Forest harness: " + message);
            if (result != null) result.error = message;
            Finish(1);
        }

        void Finish(int exitCode)
        {
            if (finished) return;
            finished = true;
            try
            {
                if (result == null) result = new RunResult { error = "no result" };
                result.finishedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                string folder = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(outputPath, JsonUtility.ToJson(result, true));
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "Forest harness: wrote {0}; worst median {1:0.00} ms at {2}, worst p95 {3:0.00} ms at {4}; exit {5}",
                    outputPath, result.worstMedianMs, result.worstViewByMedian, result.worstP95Ms, result.worstViewByP95, exitCode));
            }
            catch (Exception exception)
            {
                Debug.LogError("Forest harness: could not write the result: " + exception);
                exitCode = 2;
            }
            Application.Quit(exitCode);
        }
    }
}
