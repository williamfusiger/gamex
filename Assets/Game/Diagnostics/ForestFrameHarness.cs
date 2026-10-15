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

        void Update()
        {
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
                Light pairedLight = HasFlag("-forestMeasureTorchAB") && torch != null && torch.parent == camera.transform ? torch.GetComponent<Light>() : null;
                string[] states = pairedLight != null ? new[] { "off", "on", "noshadow" } : new string[] { null };
                foreach (Transform viewpoint in viewpoints)
                {
                    foreach (string state in states)
                    {
                        if (pairedLight != null)
                        {
                            pairedLight.enabled = state != "off";
                            pairedLight.shadows = state == "noshadow" ? LightShadows.None : LightShadows.Soft;
                        }
                        Cut(camera, viewpoint);
                        for (int i = 0; i < warmup; i++)
                            yield return null;
                        ViewResult view = null;
                        yield return Record(state == null ? viewpoint.name : viewpoint.name + "|" + state, viewpoint, frames, recorders, timings, r => view = r);
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
