using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace Gamex.EditorTools
{
    /// <summary>
    /// Measures, once, the things the forest design could not find out without running Unity: how the
    /// importer treats a Blender FBX, whether eight terrain layers render, where LODGroups switch,
    /// whether physics answers in edit mode, and so on (the M0 probe list of the specification).
    /// <c>Unity -batchmode -projectPath . -executeMethod Gamex.EditorTools.ForestProbe.Run -captureOut DIR -quit</c>
    ///
    /// Every question has a default that already works, so no answer blocks the build. The answers go to
    /// probe.json in the capture folder and to a copy in Library, which the audit and the frame-time test
    /// read to decide whether a method the probe validated may be relied on.
    /// </summary>
    public static class ForestProbe
    {
        /// <summary>The copy that outlives the Temp folder. The PlayMode suite reads the same path.</summary>
        internal const string RecordPath = "Library/GamexForest/probe.json";

        // P8 and P15 need real assets for a moment. The folder is deleted again before the run ends.
        const string TempFolder = "Assets/ForestProbeTemp";

        const string Yes = "yes", No = "no", Info = "info", Skipped = "skipped", Failed = "error", Unknown = "unknown";

        [Serializable] sealed class Detail { public string key, value; }

        [Serializable]
        sealed class Item
        {
            public string id, question, answer = Unknown, summary = "", ifNo;
            public float seconds;
            public Detail[] details = Array.Empty<Detail>();

            [NonSerialized] readonly List<Detail> list = new List<Detail>();

            public void Set(string key, object value)
            {
                list.Add(new Detail { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) });
                details = list.ToArray();
            }

            public void Answer(string value, string text)
            {
                answer = value;
                summary = text;
            }
        }

        [Serializable]
        sealed class Record
        {
            public string unityVersion, platform, graphicsDevice, graphicsApi, qualityLevel, textureCompressionOverride, writtenUtc;
            public bool batchMode, metalApiValidation;
            public float lodBias;

            // The answers other code acts on: "yes", "no" or "unknown".
            public string meshDataReadable = Unknown;         // P1
            public string importerMirror = Unknown;           // P1: "x" | "z" | "xz" | "none"
            public string unityMeshLodGenerator = Unknown;    // P4
            public string profilerCounters = Unknown;         // P5
            public string frameTiming = Unknown;              // P5
            public string readbacksCompleteUnaided = Unknown; // P6
            public string bc6hSkyClean = Unknown;             // P8
            public string terrainEightLayers = Unknown;       // P10
            public string lodCrossFadeDithers = Unknown;      // P11
            public string editModePhysics = Unknown;          // P12
            public string alphamap2048 = Unknown;             // P15

            public float skyLuxGpuOverCpu;                    // P7; 0 when not measured
            public int cubemapFaceSize;                       // P8
            public float lodDistanceScale;                    // P11: suggested ForestKitSpec.LodDistanceScale; 0 when not measured
            public float firstRenderSeconds;                  // P14

            public Item[] items = Array.Empty<Item>();
        }

        sealed class SkipException : Exception
        {
            public SkipException(string message) : base(message) { }
        }

        [MenuItem("Gamex/Forest/0 Run Probe")]
        public static void Run()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("The forest probe runs in edit mode; leave play mode first.");

            string outputFolder = ForestArgs.Value("-captureOut", ForestPaths.DefaultCaptureFolder);
            // Diagnostic: -forestProbeItems P7,P10 runs only those items. Such a run is not a probe record.
            string only = ForestArgs.Value("-forestProbeItems");
            selectedItems = string.IsNullOrEmpty(only) ? null : new HashSet<string>(only.Split(','));
            var record = new Record
            {
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                graphicsDevice = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                qualityLevel = QualitySettings.names[QualitySettings.GetQualityLevel()],
                textureCompressionOverride = EditorUserBuildSettings.overrideTextureCompression.ToString(),
                writtenUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                batchMode = Application.isBatchMode,
                metalApiValidation = PlayerSettings.enableMetalAPIValidation,
                lodBias = QualitySettings.lodBias,
            };
            var items = new List<Item>();

            using (ForestSceneSession session = ForestSceneSession.OpenEmpty())
            {
                if (session == null)
                    return;   // cancelled in the save dialog

                Directory.CreateDirectory(outputFolder);
                // A folder left behind by a run that crashed must not be mistaken for project content.
                DeleteTempFolder();
                try
                {
                    ProbeGreybox(record, items);
                    ProbeLab(record, items, outputFolder);
                    ProbeAssets(record, items);
                }
                finally
                {
                    DeleteTempFolder();
                }
            }

            items.Sort((a, b) => Number(a.id).CompareTo(Number(b.id)));
            record.items = items.ToArray();
            string json = JsonUtility.ToJson(record, true);
            string path = Path.Combine(outputFolder, "probe.json");
            File.WriteAllText(path, json);
            if (selectedItems == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RecordPath));
                File.WriteAllText(RecordPath, json);
            }
            else
            {
                // The audit and the frame-time test act on the record in Library; a partial run must not replace it.
                Debug.LogWarning($"Forest probe: only {string.Join(", ", selectedItems)} ran (-forestProbeItems), so {RecordPath} was left as it is.");
            }

            var broken = new List<string>();
            foreach (Item item in items)
                if (item.answer == Failed)
                    broken.Add($"{item.id}: {item.summary}");
            Debug.Log($"Forest probe: {items.Count} item(s) written to {Path.GetFullPath(path)}" + (selectedItems == null ? $" (copy in {RecordPath})." : "."));
            // A probe item that crashed has not answered its question; the run must not look complete.
            if (broken.Count > 0)
                throw new InvalidOperationException($"Forest probe: {broken.Count} item(s) could not be measured:\n  " + string.Join("\n  ", broken));
        }

        /// <summary>
        /// True when the probe, run with this Unity version, found that physics queries answer correctly in
        /// edit mode. Until then the audit keeps to its analytic corridor check.
        /// </summary>
        internal static bool EditModePhysicsIsReliable(out string why)
        {
            if (!File.Exists(RecordPath))
            {
                why = "the M0 probe has not been run (Gamex/Forest/0 Run Probe)";
                return false;
            }

            Record record = JsonUtility.FromJson<Record>(File.ReadAllText(RecordPath));
            if (record == null || record.unityVersion != Application.unityVersion)
            {
                why = $"the probe record is from Unity {(record != null ? record.unityVersion : "?")}, not {Application.unityVersion}";
                return false;
            }
            if (record.editModePhysics != Yes)
            {
                why = $"probe P12 answered '{record.editModePhysics}' for edit-mode physics";
                return false;
            }
            why = "";
            return true;
        }

        // Null: every item runs. Set from -forestProbeItems for a diagnostic run of a few items.
        static HashSet<string> selectedItems;

        static Item RunItem(List<Item> items, string id, string question, string ifNo, Action<Item> body)
        {
            var item = new Item { id = id, question = question, ifNo = ifNo };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (selectedItems != null && !selectedItems.Contains(id))
                    throw new SkipException("Not selected by -forestProbeItems.");
                body(item);
            }
            catch (SkipException skip)
            {
                item.Answer(Skipped, skip.Message);
            }
            catch (Exception exception)
            {
                // Recorded, logged and re-raised in sum at the end of Run: one broken item must not cost the others their answers.
                item.Answer(Failed, $"{exception.GetType().Name}: {exception.Message}");
                Debug.LogException(exception);
            }
            item.seconds = (float)watch.Elapsed.TotalSeconds;
            items.Add(item);
            Debug.Log($"Forest probe {id}: {item.answer}. {item.summary}");
            return item;
        }

        static int Number(string id) => int.TryParse(id.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : 0;

        // ------------------------------------------------------------------------------------------------
        // On the greybox: P14 (first shader compile) and P5 (counters and frame timing for explicit renders)
        // ------------------------------------------------------------------------------------------------

        static readonly string[] RenderCounters = { "Triangles Count", "Vertices Count", "Draw Calls Count", "SetPass Calls Count", "Shadow Casters Count" };
        static readonly string[] MemoryCounters = { "Texture Memory", "Mesh Memory" };

        static void ProbeGreybox(Record record, List<Item> items)
        {
            const string p14 = "How long is the first shader compile?";
            const string p5 = "Do ProfilerRecorder counters and FrameTiming.gpuFrameTime report for explicit renders, and is FrameTimingManager enabled in the Editor?";
            const string p5IfNo = "Stopwatch around a one-pixel readback as the timing method.";

            if (!File.Exists(GreyboxSceneBuilder.ScenePath))
            {
                RunItem(items, "P14", p14, "Informational.", item => throw new SkipException($"{GreyboxSceneBuilder.ScenePath} does not exist."));
                RunItem(items, "P5", p5, p5IfNo, item => throw new SkipException($"{GreyboxSceneBuilder.ScenePath} does not exist."));
                return;
            }

            EditorSceneManager.OpenScene(GreyboxSceneBuilder.ScenePath, OpenSceneMode.Single);
            Camera camera = ForestCapture.CreateCaptureCamera(Camera.main);
            var target = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var pixel = new Texture2D(1, 1, TextureFormat.RGB24, false);
            bool oldAsync = ShaderUtil.allowAsyncCompilation;
            RenderTexture oldActive = RenderTexture.active;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                camera.targetTexture = target;

                RunItem(items, "P14", p14, "Informational.", item =>
                {
                    // The first script render of a session compiles every variant it needs, synchronously.
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    camera.Render();
                    Sync(target, pixel);
                    double first = watch.Elapsed.TotalSeconds;
                    watch.Restart();
                    camera.Render();
                    Sync(target, pixel);
                    double second = watch.Elapsed.TotalSeconds;

                    record.firstRenderSeconds = (float)first;
                    item.Set("firstRenderSeconds", first);
                    item.Set("secondRenderSeconds", second);
                    item.Set("anythingCompilingAfter", ShaderUtil.anythingCompiling);
                    item.Answer(Info, $"First render of the greybox took {first:0.00} s, the second {second:0.000} s. A warm shader cache makes the first one short.");
                });

                RunItem(items, "P5", p5, p5IfNo, item => ProbeCountersAndTiming(record, item, camera, target, pixel));
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = oldActive;
                ShaderUtil.allowAsyncCompilation = oldAsync;
                Object.DestroyImmediate(camera.gameObject);
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(pixel);
            }
        }

        static void ProbeCountersAndTiming(Record record, Item item, Camera camera, RenderTexture target, Texture2D pixel)
        {
            const int frames = 8;   // frame timings lag four frames
            var recorders = new List<KeyValuePair<string, ProfilerRecorder>>();
            foreach (string name in RenderCounters)
                recorders.Add(new KeyValuePair<string, ProfilerRecorder>(name, ProfilerRecorder.StartNew(ProfilerCategory.Render, name)));
            foreach (string name in MemoryCounters)
                recorders.Add(new KeyValuePair<string, ProfilerRecorder>(name, ProfilerRecorder.StartNew(ProfilerCategory.Memory, name)));

            try
            {
                var before = new long[recorders.Count];
                for (int i = 0; i < recorders.Count; i++)
                    before[i] = recorders[i].Value.Valid ? recorders[i].Value.CurrentValue : 0;

                bool enabled = FrameTimingManager.IsFeatureEnabled();
                var timings = new FrameTiming[1];
                uint received = 0;
                for (int i = 0; i < frames; i++)
                {
                    camera.Render();
                    FrameTimingManager.CaptureFrameTimings();
                    received = FrameTimingManager.GetLatestTimings(1, timings);
                }
                double gpu = received > 0 ? timings[0].gpuFrameTime : 0.0;
                double cpu = received > 0 ? timings[0].cpuFrameTime : 0.0;

                bool allValid = true, anyCount = false;
                for (int i = 0; i < recorders.Count; i++)
                {
                    ProfilerRecorder recorder = recorders[i].Value;
                    allValid &= recorder.Valid;
                    long current = recorder.Valid ? recorder.CurrentValue : 0;
                    long last = recorder.Valid ? recorder.LastValue : 0;
                    item.Set(recorders[i].Key, $"valid {recorder.Valid}, current {current}, last {last}, change over {frames} renders {current - before[i]}");
                    if (recorders[i].Key == "Triangles Count")
                        anyCount = current > 0 || last > 0;
                }

                // The fallback: wall time around renders, with a one-pixel readback to make the GPU finish.
                var watch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 20; i++)
                {
                    camera.Render();
                    Sync(target, pixel);
                }
                double perFrameSynced = watch.Elapsed.TotalMilliseconds / 20.0;
                watch.Restart();
                for (int i = 0; i < 20; i++)
                    camera.Render();
                Sync(target, pixel);
                double throughput = watch.Elapsed.TotalMilliseconds / 20.0;

                record.profilerCounters = allValid && anyCount ? Yes : No;
                record.frameTiming = enabled && received > 0 && gpu > 0.0 ? Yes : No;
                item.Set("frameTimingFeatureEnabled", enabled);
                item.Set("frameTimingsReceived", received);
                item.Set("gpuFrameTimeMs", gpu);
                item.Set("cpuFrameTimeMs", cpu);
                item.Set("stopwatchMsPerFrameSyncedEveryFrame", perFrameSynced);
                item.Set("stopwatchMsPerFrameSyncedOnce", throughput);
                item.Set("resolution", $"{target.width} x {target.height}");
                item.Set("note", "Edit mode, one editor tick: counters are per engine frame, so 'last' can be 0 here while the PlayMode suite still reads them per frame.");
                item.Answer(record.profilerCounters == Yes && record.frameTiming == Yes ? Yes : No,
                    $"Counters: {record.profilerCounters} (all valid: {allValid}). Frame timing: {record.frameTiming} (enabled {enabled}, gpu {gpu:0.00} ms). " +
                    $"Stopwatch: {perFrameSynced:0.00} ms per synced frame, {throughput:0.00} ms throughput at 960 x 540.");
            }
            finally
            {
                foreach (KeyValuePair<string, ProfilerRecorder> pair in recorders)
                    pair.Value.Dispose();
            }
        }

        // Reading one pixel back makes the GPU finish what was submitted.
        static void Sync(RenderTexture target, Texture2D pixel)
        {
            RenderTexture.active = target;
            pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
        }

        // ------------------------------------------------------------------------------------------------
        // In a small scene of its own: P6, P10, P11, P12, P15, P7, P8
        // ------------------------------------------------------------------------------------------------

        static void ProbeLab(Record record, List<Item> items, string outputFolder)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var owned = new List<Object>();
            try
            {
                Camera camera = BuildLab(owned, out Volume volume);

                RunItem(items, "P6", "Do readbacks complete between back-to-back renders?", "Keep the explicit wait (already in the capture loop).",
                    item => ProbeReadback(record, item, camera));

                Terrain terrain = null;
                RunItem(items, "P10", "Do terrain layers beyond four render on a script-made material in the Editor, and does ValidateMaterial accept it?",
                    "Four layers (ForestLayout.TerrainLayerCount = 4).",
                    item => terrain = ProbeTerrainLayers(record, item, camera, owned, outputFolder));

                RunItem(items, "P11", "Where do LODGroups switch, and does cross-fade dither under HDRP without the Resident Drawer?",
                    "Fit ForestKitSpec.LodDistanceScale; ForestKitSpec.UseCrossFade = false.",
                    item => ProbeLodGroups(record, item, camera, owned, outputFolder));

                RunItem(items, "P12", "Do physics queries work in edit mode after Physics.SyncTransforms()?",
                    "The audit keeps its analytic corridor check; the physical one runs only in the PlayMode suite.",
                    item => ProbePhysics(record, item, terrain, owned));

                RunItem(items, "P15", "Is TerrainData.alphamapResolution = 2048 accepted, and how long do SetAlphamaps with eight layers and saving the asset take?",
                    "ForestLayout.AlphamapResolution = 1024, trail band still unblurred.",
                    item => ProbeAlphamap(record, item));

                RunItem(items, "P7", "Does the CPU lux integral agree with the Inspector's GPU blit, or failing that with the lux HDRP's own sky delivers?", "Keep the CPU value.",
                    item => ProbeSkyLux(record, item, camera, volume, owned));

                RunItem(items, "P8", "What is the cubemap face size from a 4k lat-long, and does CompressedHQ (BC6H) import cleanly?", "Stay uncompressed.",
                    item => ProbeSkyCompression(record, item, camera, volume, owned));
            }
            finally
            {
                for (int i = owned.Count - 1; i >= 0; i--)
                    if (owned[i] != null)
                        Object.DestroyImmediate(owned[i]);
            }
        }

        // A bright gradient sky, a sun, fixed exposure and no fog: enough light to tell colours apart, nothing that needs an asset.
        static Camera BuildLab(List<Object> owned, out Volume volume)
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            owned.Add(profile);
            var environment = AddOverride<VisualEnvironment>(profile, owned);
            environment.skyType.Override((int)SkyType.Gradient);
            environment.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            var sky = AddOverride<GradientSky>(profile, owned);
            sky.top.Override(new Color(0.45f, 0.55f, 0.75f));
            sky.middle.Override(new Color(0.6f, 0.65f, 0.7f));
            sky.bottom.Override(new Color(0.25f, 0.25f, 0.25f));
            sky.skyIntensityMode.Override(SkyIntensityMode.Exposure);
            sky.exposure.Override(10f);
            var exposure = AddOverride<Exposure>(profile, owned);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(10f);
            AddOverride<Fog>(profile, owned).enabled.Override(false);

            var holder = new GameObject("Probe Volume");
            owned.Add(holder);
            volume = holder.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;

            var sun = new GameObject("Probe Sun");
            owned.Add(sun);
            sun.AddComponent<HDAdditionalLightData>();
            var light = sun.GetComponent<Light>();
            light.type = LightType.Directional;
            light.lightUnit = LightUnit.Lux;
            light.intensity = 3000f;
            light.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, 30f, 0f);

            Camera camera = ForestCapture.CreateCaptureCamera(null);
            owned.Add(camera.gameObject);
            return camera;
        }

        static T AddOverride<T>(VolumeProfile profile, List<Object> owned) where T : VolumeComponent
        {
            T component = profile.Add<T>();
            owned.Add(component);
            return component;
        }

        // P6. The sky's ambient probe reaches the CPU through an asynchronous readback.
        static void ProbeReadback(Record record, Item item, Camera camera)
        {
            var target = new RenderTexture(256, 144, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                camera.transform.SetPositionAndRotation(new Vector3(0f, 2f, 0f), Quaternion.identity);
                camera.targetTexture = target;
                camera.Render();
                camera.Render();
                float before = AmbientLevel();
                AsyncGPUReadback.WaitAllRequests();
                float afterWait = AmbientLevel();
                camera.Render();
                float afterRender = AmbientLevel();

                item.Set("ambientLevelAfterTwoRenders", before);
                item.Set("ambientLevelAfterWait", afterWait);
                item.Set("ambientLevelAfterAnotherRender", afterRender);
                float settled = Mathf.Max(afterWait, afterRender);
                if (settled <= 1e-5f)
                {
                    item.Answer(Info, "RenderSettings.ambientProbe stayed black even after the wait, so there is nothing to compare. The capture loop keeps its explicit wait.");
                    return;
                }
                bool unaided = Mathf.Abs(before - settled) <= 0.05f * settled;
                record.readbacksCompleteUnaided = unaided ? Yes : No;
                item.Answer(unaided ? Yes : No, unaided
                    ? "The ambient probe was already complete after two back-to-back renders."
                    : $"The ambient probe was {before:0.###} after two renders and {settled:0.###} once the readbacks were waited for: the explicit wait is needed.");
            }
            finally
            {
                camera.targetTexture = null;
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        // The constant term of the ambient probe, as one brightness number.
        static float AmbientLevel()
        {
            SphericalHarmonicsL2 probe = RenderSettings.ambientProbe;
            return 0.2126f * probe[0, 0] + 0.7152f * probe[1, 0] + 0.0722f * probe[2, 0];
        }

        // P10. Eight stripes, each painted with one flat-colour layer, seen from straight above.
        static readonly Color[] StripeColours =
        {
            new Color(0.80f, 0.10f, 0.10f), new Color(0.10f, 0.70f, 0.10f), new Color(0.10f, 0.20f, 0.80f), new Color(0.80f, 0.80f, 0.10f),
            new Color(0.90f, 0.50f, 0.10f), new Color(0.50f, 0.10f, 0.70f), new Color(0.10f, 0.60f, 0.60f), new Color(0.85f, 0.85f, 0.85f),
        };

        static Terrain ProbeTerrainLayers(Record record, Item item, Camera camera, List<Object> owned, string outputFolder)
        {
            const int stripes = 8, alphamap = 64, image = 512;
            const float size = 64f, height = 10f;

            var data = new TerrainData();
            owned.Add(data);
            data.heightmapResolution = 33;
            data.size = new Vector3(size, height, size);
            data.alphamapResolution = alphamap;
            data.baseMapResolution = 64;
            // Half way up, so the physics probe has a surface that is not at the terrain's base.
            var heights = new float[33, 33];
            for (int z = 0; z < 33; z++)
                for (int x = 0; x < 33; x++)
                    heights[z, x] = 0.5f;
            data.SetHeights(0, 0, heights);

            var layers = new TerrainLayer[stripes];
            for (int i = 0; i < stripes; i++)
            {
                var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = $"Probe Layer {i}" };
                var fill = new Color[16];
                for (int p = 0; p < fill.Length; p++)
                    fill[p] = StripeColours[i];
                texture.SetPixels(fill);
                texture.Apply();
                owned.Add(texture);
                layers[i] = new TerrainLayer { name = $"Probe Layer {i}", diffuseTexture = texture, tileSize = new Vector2(4f, 4f) };
                owned.Add(layers[i]);
            }
            data.terrainLayers = layers;

            var weights = new float[alphamap, alphamap, stripes];
            for (int z = 0; z < alphamap; z++)
                for (int x = 0; x < alphamap; x++)
                    weights[z, x, x * stripes / alphamap] = 1f;
            data.SetAlphamaps(0, 0, weights);

            // Terrain.CreateTerrainGameObject assigns the pipeline's default terrain material and marks it dirty (measured
            // here at M0). Left alone, the next save of assets rewrites that file inside the package cache and Unity warns
            // that an immutable package was altered. Nothing in it changed, so the mark is taken back.
            Material packageDefault = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.defaultTerrainMaterial : null;
            bool defaultWasDirty = packageDefault != null && EditorUtility.IsDirty(packageDefault);
            GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
            bool creationDirtiedDefault = packageDefault != null && !defaultWasDirty && EditorUtility.IsDirty(packageDefault);
            if (creationDirtiedDefault)
                EditorUtility.ClearDirty(packageDefault);
            item.Set("createTerrainGameObjectDirtiesDefaultMaterial", creationDirtiedDefault);
            owned.Add(terrainObject);
            terrainObject.name = "Probe Terrain";
            terrainObject.transform.position = new Vector3(-size * 0.5f, 0f, -size * 0.5f);
            var terrain = terrainObject.GetComponent<Terrain>();

            Material template = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.defaultTerrainMaterial : null;
            if (template == null)
                throw new InvalidOperationException("The render pipeline asset has no default terrain material to copy.");
            var material = new Material(template) { name = "Probe Terrain Material" };
            owned.Add(material);
            terrain.materialTemplate = material;
            terrain.drawInstanced = true;
            terrain.basemapDistance = 1000f;
            item.Set("shader", material.shader.name);

            // Straight down: image x is world x, so stripe i fills one eighth of the picture's width.
            camera.transform.SetPositionAndRotation(new Vector3(0f, height * 0.5f + 30f, 0f), Quaternion.Euler(90f, 0f, 0f));
            camera.orthographic = true;
            camera.orthographicSize = size * 0.5f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;

            bool allPass = true, firstFourPass = true;
            foreach (bool heightBlend in new[] { false, true })
            {
                material.SetFloat("_EnableHeightBlend", heightBlend ? 1f : 0f);
                material.SetFloat("_HeightTransition", 0.04f);
                bool valid = HDMaterial.ValidateMaterial(material);
                Color32[] pixels = ForestCapture.RenderPixels(camera, image, image, 8);
                string label = heightBlend ? "heightBlendOn" : "heightBlendOff";
                ForestCapture.WritePng(Path.Combine(outputFolder, $"probe_p10_{label}.png"), pixels, image, image);

                var rendered = new string[stripes];
                for (int i = 0; i < stripes; i++)
                {
                    Vector3 mean = MeanColour(pixels, image, (int)((i + 0.25f) * image / stripes), (int)((i + 0.75f) * image / stripes), image / 4, image * 3 / 4);
                    float luminance = 0.2126f * mean.x + 0.7152f * mean.y + 0.0722f * mean.z;
                    var expected = new Vector3(StripeColours[i].r, StripeColours[i].g, StripeColours[i].b);
                    // Lighting changes how bright a stripe is, not which colour it is.
                    float likeness = mean.magnitude > 1e-4f ? Vector3.Dot(mean.normalized, expected.normalized) : 0f;
                    bool pass = luminance > 0.02f && likeness > 0.9f;
                    rendered[i] = $"{(pass ? "ok" : "WRONG")} rgb({mean.x:0.00}, {mean.y:0.00}, {mean.z:0.00})";
                    allPass &= pass;
                    if (i < 4)
                        firstFourPass &= pass;
                }
                item.Set(label + ".validateMaterial", valid);
                item.Set(label + ".keywords", string.Join(" ", material.shaderKeywords));
                item.Set(label + ".layers", string.Join("; ", rendered));
            }
            item.Set("globalKeyword_TERRAIN_8_LAYERS", Shader.IsKeywordEnabled("_TERRAIN_8_LAYERS"));

            camera.orthographic = false;
            camera.farClipPlane = 400f;
            camera.nearClipPlane = 0.05f;

            if (!firstFourPass)
            {
                item.Answer(Info, "Even the first four layers did not show their colours, so this run says nothing about layers five to eight. See the probe_p10 images.");
                return terrain;
            }
            record.terrainEightLayers = allPass ? Yes : No;
            item.Answer(allPass ? Yes : No, allPass
                ? "All eight flat-colour layers rendered in their own colour, with height blend off and on."
                : "Layers one to four rendered, but at least one of layers five to eight came out black or in the wrong colour. See the probe_p10 images.");
            return terrain;
        }

        static Vector3 MeanColour(Color32[] pixels, int width, int left, int right, int bottom, int top)
        {
            double r = 0.0, g = 0.0, b = 0.0;
            int count = 0;
            for (int y = bottom; y < top; y++)
            {
                for (int x = left; x < right; x++)
                {
                    Color32 pixel = pixels[y * width + x];
                    r += pixel.r;
                    g += pixel.g;
                    b += pixel.b;
                    count++;
                }
            }
            if (count == 0)
                return Vector3.zero;
            return new Vector3((float)(r / count / 255.0), (float)(g / count / 255.0), (float)(b / count / 255.0));
        }

        // P11. A red detailed sphere that turns into a green coarse one: the picture says which level is drawn.
        static void ProbeLodGroups(Record record, Item item, Camera camera, List<Object> owned, string outputFolder)
        {
            const float plannedSwitch = 14f, plannedCull = 42f, objectSize = 2f;
            const int width = 960, height = 540;
            // Far enough east that the camera, which backs away to the west, never comes near the probe terrain.
            var centre = new Vector3(600f, 1f, 0f);

            Mesh fine = Icosphere(4, objectSize * 0.5f, "Probe Sphere Fine");
            Mesh coarse = Icosphere(1, objectSize * 0.5f, "Probe Sphere Coarse");
            Material red = FlatMaterial(new Color(0.9f, 0.05f, 0.05f));
            Material green = FlatMaterial(new Color(0.05f, 0.9f, 0.05f));
            owned.Add(fine);
            owned.Add(coarse);
            owned.Add(red);
            owned.Add(green);

            var root = new GameObject("Probe LOD Object");
            owned.Add(root);
            root.transform.position = centre;
            Renderer near = Child(root, "LOD0", fine, red);
            Renderer far = Child(root, "LOD1", coarse, green);

            // The formula of the kit: a level is kept until the object is this share of the screen height.
            float tanHalf = Mathf.Tan(35f * Mathf.Deg2Rad);
            float nearHeight = objectSize / (2f * plannedSwitch * tanHalf);
            float farHeight = objectSize / (2f * plannedCull * tanHalf);
            var group = root.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.None;
            group.animateCrossFading = false;
            group.SetLODs(new[] { new LOD(nearHeight, new[] { near }), new LOD(farHeight, new[] { far }) });
            group.size = objectSize;
            group.localReferencePoint = Vector3.zero;

            // Dither shows as a pixel pattern only without temporal anti-aliasing, which would average it away.
            var data = camera.GetComponent<HDAdditionalCameraData>();
            HDAdditionalCameraData.AntialiasingMode oldAntialiasing = data.antialiasing;
            data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
            camera.orthographic = false;
            camera.fieldOfView = 70f;
            try
            {
                Shares Look(float distance)
                {
                    camera.transform.SetPositionAndRotation(centre - Vector3.right * distance, Quaternion.LookRotation(Vector3.right, Vector3.up));
                    Color32[] pixels = ForestCapture.RenderPixels(camera, width, height, 3);
                    // A window well inside the sphere's outline, so no sky is counted.
                    float radiusInPixels = objectSize * 0.5f / (distance * tanHalf) * (height * 0.5f);
                    return CountColours(pixels, width, height, Mathf.Max(1, Mathf.FloorToInt(radiusInPixels * 0.4f)));
                }

                Shares close = Look(2f);
                item.Set("at2m", close.ToString());
                if (!close.IsRed)
                {
                    item.Answer(Info, "The detailed level did not show at 2 m, so the test object does not behave as planned and nothing can be read from it.");
                    return;
                }

                // Bisect for the distance at which the detailed level stops being the one drawn.
                float low = 2f, high = 120f;
                for (int i = 0; i < 14; i++)
                {
                    float middle = 0.5f * (low + high);
                    if (Look(middle).IsRed) low = middle;
                    else high = middle;
                }
                float measuredSwitch = 0.5f * (low + high);

                // And for the distance at which nothing is drawn any more.
                float lowCull = measuredSwitch, highCull = 400f;
                Shares beyond = Look(measuredSwitch * 1.1f);
                bool coarseShows = beyond.IsGreen;
                if (coarseShows)
                {
                    for (int i = 0; i < 14; i++)
                    {
                        float middle = 0.5f * (lowCull + highCull);
                        if (Look(middle).IsGreen) lowCull = middle;
                        else highCull = middle;
                    }
                }
                float measuredCull = 0.5f * (lowCull + highCull);

                // A level that lasts to distance D' when D was asked for needs its screen height scaled by D' / D.
                float scale = measuredSwitch / plannedSwitch;
                record.lodDistanceScale = scale;
                item.Set("plannedSwitchMetres", plannedSwitch);
                item.Set("measuredSwitchMetres", measuredSwitch);
                item.Set("plannedCullMetres", plannedCull);
                item.Set("measuredCullMetres", coarseShows ? measuredCull.ToString("0.00", CultureInfo.InvariantCulture) : "the coarse level never showed");
                item.Set("suggestedLodDistanceScale", scale);
                item.Set("lodBias", QualitySettings.lodBias);
                item.Set("fieldOfView", camera.fieldOfView);

                // Cross-fade: inside the fade band both levels are drawn, each on part of the pixels.
                group.fadeMode = LODFadeMode.CrossFade;
                LOD[] levels = group.GetLODs();
                for (int i = 0; i < levels.Length; i++)
                    levels[i].fadeTransitionWidth = 0.2f;
                group.SetLODs(levels);

                float bestMix = 0f, bestDistance = 0f;
                var samples = new List<string>();
                for (int i = 0; i <= 12; i++)
                {
                    float distance = measuredSwitch * Mathf.Lerp(0.30f, 0.99f, i / 12f);
                    Shares shares = Look(distance);
                    samples.Add($"{distance:0.0} m: {shares}");
                    float mix = Mathf.Min(shares.red, shares.green);
                    if (mix > bestMix)
                    {
                        bestMix = mix;
                        bestDistance = distance;
                    }
                }
                item.Set("crossFadeSamples", string.Join("; ", samples));
                bool dithers = bestMix > 0.1f;
                record.lodCrossFadeDithers = dithers ? Yes : No;
                if (dithers)
                {
                    camera.transform.SetPositionAndRotation(centre - Vector3.right * bestDistance, Quaternion.LookRotation(Vector3.right, Vector3.up));
                    ForestCapture.WritePng(Path.Combine(outputFolder, "probe_p11_fade.png"), ForestCapture.RenderPixels(camera, width, height, 3), width, height);
                    item.Set("fadeCapture", $"probe_p11_fade.png at {bestDistance:0.0} m");
                }

                item.Answer(dithers ? Yes : No,
                    $"The detailed level lasted to {measuredSwitch:0.0} m where {plannedSwitch:0} m was planned (LodDistanceScale {scale:0.00}); " +
                    (dithers ? $"cross-fade dithers (both levels on {bestMix * 100f:0} % of the pixels at {bestDistance:0.0} m)."
                             : "no distance showed both levels at once, so cross-fade does not dither here."));
            }
            finally
            {
                data.antialiasing = oldAntialiasing;
            }
        }

        struct Shares
        {
            public float red, green;
            public bool IsRed => red > 0.5f && red > green;
            public bool IsGreen => green > 0.5f && green > red;
            public override string ToString() => $"red {red * 100f:0} %, green {green * 100f:0} %";
        }

        static Shares CountColours(Color32[] pixels, int width, int height, int halfWindow)
        {
            int red = 0, green = 0, count = 0;
            int centreX = width / 2, centreY = height / 2;
            for (int y = centreY - halfWindow; y <= centreY + halfWindow; y++)
            {
                for (int x = centreX - halfWindow; x <= centreX + halfWindow; x++)
                {
                    Color32 pixel = pixels[y * width + x];
                    if (pixel.r > 2 * pixel.g && pixel.r > 20) red++;
                    else if (pixel.g > 2 * pixel.r && pixel.g > 20) green++;
                    count++;
                }
            }
            return new Shares { red = red / (float)count, green = green / (float)count };
        }

        static Renderer Child(GameObject parent, string name, Mesh mesh, Material material)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        static Material FlatMaterial(Color colour)
        {
            var material = new Material(Shader.Find("HDRP/Lit")) { hideFlags = HideFlags.HideAndDontSave };
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", 0.1f);
            HDMaterial.ValidateMaterial(material);
            return material;
        }

        // P12. Known colliders, known answers.
        static void ProbePhysics(Record record, Item item, Terrain terrain, List<Object> owned)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            owned.Add(box);
            box.name = "Probe Box";
            Object.DestroyImmediate(box.GetComponent<Renderer>());
            box.transform.position = new Vector3(100f, 1f, 100f);
            box.transform.localScale = new Vector3(2f, 2f, 2f);

            // A mesh collider on a script-built mesh, as rocks and logs have.
            Mesh rock = Icosphere(1, 1f, "Probe Collision Mesh");
            owned.Add(rock);
            var meshObject = new GameObject("Probe Mesh Collider");
            owned.Add(meshObject);
            meshObject.transform.position = new Vector3(140f, 1f, 100f);
            meshObject.AddComponent<MeshCollider>().sharedMesh = rock;

            Physics.SyncTransforms();
            const float radius = 0.35f;
            int failures = 0;
            void Expect(string name, bool actual, bool expected)
            {
                item.Set(name, actual == expected ? $"{actual} (as expected)" : $"{actual}, EXPECTED {expected}");
                if (actual != expected)
                    failures++;
            }

            Expect("capsuleInsideBox", Physics.CheckCapsule(new Vector3(100f, 0.5f, 100f), new Vector3(100f, 1.5f, 100f), radius), true);
            Expect("capsuleAwayFromBox", Physics.CheckCapsule(new Vector3(110f, 0.5f, 100f), new Vector3(110f, 1.5f, 100f), radius), false);
            bool cast = Physics.CapsuleCast(new Vector3(90f, 0.5f, 100f), new Vector3(90f, 1.5f, 100f), radius, Vector3.right, out RaycastHit castHit, 50f);
            Expect("capsuleCastHitsBox", cast && castHit.collider != null && castHit.collider.gameObject == box, true);
            if (cast)
                Expect("capsuleCastDistanceIsRight", Mathf.Abs(castHit.distance - (9f - radius)) < 0.05f, true);
            // A mesh collider is a surface, not a solid: the capsule has to cross it, here at the sphere's side.
            Expect("capsuleTouchesMeshCollider", Physics.CheckCapsule(new Vector3(141f, 0.5f, 100f), new Vector3(141f, 1.5f, 100f), radius), true);
            Expect("capsuleAwayFromMeshCollider", Physics.CheckCapsule(new Vector3(150f, 0.5f, 100f), new Vector3(150f, 1.5f, 100f), radius), false);

            // A collider that moves is found at its new place only after the transforms are synced.
            box.transform.position = new Vector3(100f, 1f, 130f);
            Physics.SyncTransforms();
            Expect("boxGoneFromOldPlace", Physics.CheckCapsule(new Vector3(100f, 0.5f, 100f), new Vector3(100f, 1.5f, 100f), radius), false);
            Expect("boxFoundAtNewPlace", Physics.CheckCapsule(new Vector3(100f, 0.5f, 130f), new Vector3(100f, 1.5f, 130f), radius), true);

            if (terrain != null)
            {
                float surface = terrain.SampleHeight(Vector3.zero) + terrain.transform.position.y;
                bool down = Physics.Raycast(new Vector3(0f, surface + 50f, 0f), Vector3.down, out RaycastHit ground, 200f);
                Expect("rayHitsTerrain", down && ground.collider is TerrainCollider, true);
                if (down)
                    Expect("rayHitsTerrainAtItsHeight", Mathf.Abs(ground.point.y - surface) < 0.05f, true);
                Expect("capsuleInTerrainSurface", Physics.CheckCapsule(new Vector3(0f, surface, 0f), new Vector3(0f, surface + 1f, 0f), radius), true);
                Expect("capsuleAboveTerrain", Physics.CheckCapsule(new Vector3(0f, surface + 2f, 0f), new Vector3(0f, surface + 3f, 0f), radius), false);
            }
            else
            {
                item.Set("terrain", "not tested: the probe terrain of P10 was not built");
            }

            if (terrain == null)
            {
                item.Answer(Info, failures == 0
                    ? "Box and mesh colliders answered correctly, but the terrain collider could not be tested, so the audit keeps to the analytic corridor check."
                    : $"{failures} physics query answer(s) were wrong in edit mode.");
                if (failures > 0)
                    record.editModePhysics = No;
                return;
            }
            record.editModePhysics = failures == 0 ? Yes : No;
            item.Answer(failures == 0 ? Yes : No, failures == 0
                ? "Capsule checks, a capsule cast and a raycast against a box, a mesh collider and a terrain all answered correctly in edit mode."
                : $"{failures} physics query answer(s) were wrong in edit mode; see the details.");
        }

        // P15. The alphamap of the real terrain: 2048 with eight layers, saved as an asset.
        static void ProbeAlphamap(Record record, Item item)
        {
            const int wanted = 2048, layerCount = 8;
            EnsureTempFolder();

            // The same order as the real build: heightmap resolution before size.
            var data = new TerrainData();
            data.heightmapResolution = 1025;
            data.size = new Vector3(ForestLayout.TileSize, ForestLayout.HeightRange, ForestLayout.TileSize);
            data.alphamapResolution = wanted;
            data.baseMapResolution = 1024;
            int accepted = data.alphamapResolution;
            item.Set("requested", wanted);
            item.Set("readBack", accepted);

            string path = TempFolder + "/ProbeTerrain.asset";
            AssetDatabase.CreateAsset(data, path);
            var layers = new TerrainLayer[layerCount];
            for (int i = 0; i < layerCount; i++)
            {
                layers[i] = new TerrainLayer();
                AssetDatabase.CreateAsset(layers[i], $"{TempFolder}/ProbeLayer{i}.terrainlayer");
            }
            data.terrainLayers = layers;

            int resolution = data.alphamapResolution;
            var weights = new float[resolution, resolution, layerCount];
            for (int z = 0; z < resolution; z++)
                for (int x = 0; x < resolution; x++)
                    weights[z, x, (x * layerCount / resolution + z * layerCount / resolution) % layerCount] = 1f;

            var watch = System.Diagnostics.Stopwatch.StartNew();
            data.SetAlphamaps(0, 0, weights);
            double setSeconds = watch.Elapsed.TotalSeconds;
            watch.Restart();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            double saveSeconds = watch.Elapsed.TotalSeconds;

            item.Set("alphamapLayers", data.alphamapLayers);
            item.Set("alphamapTextures", data.alphamapTextureCount);
            item.Set("setAlphamapsSeconds", setSeconds);
            item.Set("saveAssetSeconds", saveSeconds);
            item.Set("assetBytes", new FileInfo(path).Length);

            record.alphamap2048 = accepted == wanted ? Yes : No;
            item.Answer(accepted == wanted ? Yes : No,
                $"alphamapResolution {wanted} read back as {accepted}. SetAlphamaps with {layerCount} layers took {setSeconds:0.00} s, saving the asset {saveSeconds:0.00} s ({new FileInfo(path).Length / 1048576.0:0.0} MB on disk).");
        }

        // P7. The Inspector computes the sky's lux with a shader; the builder integrates a 1k copy on the CPU.
        static void ProbeSkyLux(Record record, Item item, Camera camera, Volume volume, List<Object> owned)
        {
            string skyId = ForestLightingPresets.SkyId(ForestLightingPresets.Inspect);
            Cubemap cubemap = ForestSky.LoadCubemap(skyId);
            if (cubemap == null)
                throw new SkipException($"Sky {skyId} is not downloaded and imported (fetch milestone M1, then Gamex/Forest/1 Import Assets).");

            ForestAssetEntry entry = ForestManifest.Find(skyId);
            foreach (ForestAssetFile file in entry != null ? entry.files : Array.Empty<ForestAssetFile>())
                if (file.map == "skyCalibration" && !File.Exists(ForestManifest.DiskPath(file.relPath)))
                    throw new SkipException($"The 1k calibration image of sky {skyId} is not downloaded ({file.relPath}).");

            ForestSky.Calibration calibration = ForestSky.Calibrate(skyId);
            item.Set("cpuUpperHemisphereLux", calibration.upperHemisphereLux);
            item.Set("cpuLowerHemisphereLux", calibration.lowerHemisphereLux);
            item.Set("cpuLowerOverUpper", calibration.lowerHemisphereLux / calibration.upperHemisphereLux);
            item.Set("cpuLuxColor", calibration.luxColor.ToString("0.000"));
            item.Set("brightestAzimuthDegrees", calibration.brightestAzimuthDegrees);

            // End to end, through HDRP's own sky pipeline: give the sky the CPU value, ask for a known lux, and read what
            // arrives on level ground. This also shows which way up the importer put the panorama, which the CPU integral
            // cannot know: it reads the 1k image, not the cubemap.
            SkyLuxDelivered(item, camera, volume, owned, cubemap, calibration, out float requestedLux, out float luxUp, out float luxDown);
            bool cubemapBrightSideUp = luxUp > luxDown;
            bool imageBrightSideUp = calibration.upperHemisphereLux > calibration.lowerHemisphereLux;

            // The Inspector's own way, where it works.
            float shaderLux = IntegrationShaderLux(item, cubemap);

            float gpu;
            string method;
            if (shaderLux > 0f)
            {
                gpu = shaderLux;
                method = "HDRP's integration shader, as the Inspector";
            }
            else
            {
                gpu = calibration.upperHemisphereLux * luxUp / requestedLux;
                method = "HDRP's dynamic ambient probe under the sky in Lux mode (the integration shader drew nothing)";
            }
            item.Set("gpuUpperHemisphereLux", gpu);
            item.Set("gpuMethod", method);

            if (!(gpu > 0f) || !(calibration.upperHemisphereLux > 0f))
            {
                item.Answer(No, $"One of the two values is not positive (GPU {gpu}, CPU {calibration.upperHemisphereLux}); keep the CPU value only if it is the positive one.");
                return;
            }
            float ratio = gpu / calibration.upperHemisphereLux;
            record.skyLuxGpuOverCpu = ratio;
            item.Set("gpuOverCpu", ratio);
            bool close = Mathf.Abs(ratio - 1f) <= 0.10f;
            bool sameWayUp = cubemapBrightSideUp == imageBrightSideUp;
            item.Answer(close && sameWayUp ? Yes : No, $"GPU {gpu:0.###} lux, CPU {calibration.upperHemisphereLux:0.###} lux: ratio {ratio:0.000}" +
                (close ? "." : ", more than 10 % apart.") + $" GPU value from: {method}. Asked for {requestedLux:0} lux, the sky delivers {luxUp:0} on ground facing up and {luxDown:0} facing down." +
                (sameWayUp ? "" : " The cubemap and the 1k image disagree about which half is the bright one."));
        }

        // What HDRISkyEditor.GetUpperHemisphereLuxValue does: a blit into one pixel, the lux value comes back in alpha.
        // 0 when the shader is missing or draws nothing.
        static float IntegrationShaderLux(Item item, Cubemap cubemap)
        {
            Shader shader = Shader.Find("Hidden/HDRP/IntegrateHDRI");
            if (shader == null)
            {
                item.Set("integrationShader", "Hidden/HDRP/IntegrateHDRI was not found");
                return 0f;
            }

            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            RenderTexture oldActive = RenderTexture.active;
            bool oldAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                material.SetTexture("_Cubemap", cubemap);
                ShaderUtil.CompilePass(material, 0, true);
                item.Set("integrationShader", $"pipeline {(RenderPipelineManager.currentPipeline != null ? RenderPipelineManager.currentPipeline.GetType().Name : "none")}, " +
                    $"pass compiled {ShaderUtil.IsPassCompiled(material, 0)}");

                // A draw that does not happen leaves the target as it was, so each attempt starts from a marker.
                const float marker = -7f;
                Color Draw(bool asBlit)
                {
                    var commands = new CommandBuffer { name = "Forest probe P7" };
                    commands.SetRenderTarget(target);
                    commands.ClearRenderTarget(false, true, new Color(marker, marker, marker, marker));
                    if (!asBlit)
                        // As HDRP draws its own full-screen passes: one procedural triangle.
                        commands.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1);
                    Graphics.ExecuteCommandBuffer(commands);
                    commands.Release();
                    if (asBlit)
                        Graphics.Blit(Texture2D.whiteTexture, target, material);
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                    return readback.GetPixel(0, 0);
                }
                string Describe(Color colour) => colour.a == marker
                    ? "nothing was drawn"
                    : $"rgb ({colour.r:0.####}, {colour.g:0.####}, {colour.b:0.####}), lux {colour.a:0.######}";

                Color blit = Draw(true);
                Color triangle = Draw(false);
                item.Set("integrationShader.blit", Describe(blit));
                item.Set("integrationShader.fullScreenTriangle", Describe(triangle));
                if (blit.a != marker && blit.a > 0f) return blit.a;
                if (triangle.a != marker && triangle.a > 0f) return triangle.a;
                return 0f;
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = oldAsync;
                RenderTexture.active = oldActive;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(material);
            }
        }

        // Renders the sky in Lux mode with the CPU calibration and reads, from the ambient probe HDRP computes from the sky
        // it renders, the lux on a surface facing up and on one facing down. The probe is order 2 spherical harmonics, so a
        // few per cent of error are in the method.
        static void SkyLuxDelivered(Item item, Camera camera, Volume volume, List<Object> owned, Cubemap cubemap, ForestSky.Calibration calibration,
            out float requestedLux, out float luxUp, out float luxDown)
        {
            requestedLux = 5000f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            owned.Add(profile);
            var environment = AddOverride<VisualEnvironment>(profile, owned);
            environment.skyType.Override((int)SkyType.HDRI);
            environment.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            var sky = AddOverride<HDRISky>(profile, owned);
            sky.hdriSky.Override(cubemap);
            sky.skyIntensityMode.Override(SkyIntensityMode.Lux);
            sky.desiredLuxValue.Override(requestedLux);
            sky.upperHemisphereLuxValue.Override(calibration.upperHemisphereLux);
            sky.upperHemisphereLuxColor.Override(calibration.luxColor);
            var exposure = AddOverride<Exposure>(profile, owned);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(10f);
            AddOverride<Fog>(profile, owned).enabled.Override(false);

            VolumeProfile oldProfile = volume.sharedProfile;
            volume.sharedProfile = profile;
            try
            {
                camera.orthographic = false;
                camera.fieldOfView = 70f;
                camera.transform.SetPositionAndRotation(new Vector3(0f, 500f, 0f), Quaternion.identity);
                // The probe arrives by readback and is applied by the render after it (P6), so render on both sides of the wait.
                ForestCapture.RenderPixels(camera, 256, 144, 6);
                AsyncGPUReadback.WaitAllRequests();
                ForestCapture.RenderPixels(camera, 256, 144, 4);

                var results = new Color[2];
                RenderSettings.ambientProbe.Evaluate(new[] { Vector3.up, Vector3.down }, results);
                // Evaluate gives irradiance over pi (the radiance a white diffuse surface sends back).
                luxUp = Mathf.PI * (0.2126f * results[0].r + 0.7152f * results[0].g + 0.0722f * results[0].b);
                luxDown = Mathf.PI * (0.2126f * results[1].r + 0.7152f * results[1].g + 0.0722f * results[1].b);
                item.Set("skyInLuxMode.requestedLux", requestedLux);
                item.Set("skyInLuxMode.luxOnGroundFacingUp", luxUp);
                item.Set("skyInLuxMode.luxFacingDown", luxDown);
                item.Set("skyInLuxMode.downOverUp", luxUp > 0f ? luxDown / luxUp : 0f);
            }
            finally
            {
                volume.sharedProfile = oldProfile;
            }
        }

        // P8. The same sky imported both ways, uncompressed and BC6H, rendered side by side. The project's own sky is one of
        // the two; a temporary copy is imported the other way.
        static void ProbeSkyCompression(Record record, Item item, Camera camera, Volume volume, List<Object> owned)
        {
            string skyId = ForestLightingPresets.SkyId(ForestLightingPresets.Inspect);
            Cubemap cubemap = ForestSky.LoadCubemap(skyId);
            ForestAssetEntry entry = ForestManifest.Find(skyId);
            if (cubemap == null || entry == null)
                throw new SkipException($"Sky {skyId} is not downloaded and imported (fetch milestone M1, then Gamex/Forest/1 Import Assets).");

            bool projectIsCompressed = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsCompressedFormat(cubemap.graphicsFormat);
            record.cubemapFaceSize = cubemap.width;
            item.Set("faceSize", cubemap.width);
            item.Set("format", cubemap.format);
            item.Set("mipCount", cubemap.mipmapCount);
            item.Set("projectSkyIsCompressed", projectIsCompressed);

            string source = null;
            foreach (ForestAssetFile file in entry.files)
                if (file.map == "sky")
                    source = ForestManifest.DiskPath(file.relPath);
            if (source == null || !File.Exists(source))
                throw new SkipException($"The sky file of {skyId} is not on disk.");

            EnsureTempFolder();
            string copyPath = TempFolder + "/probe_sky_copy.hdr";
            File.Copy(source, copyPath, true);
            AssetDatabase.ImportAsset(copyPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(copyPath) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException($"{copyPath} was not imported as a texture.");
            importer.textureShape = TextureImporterShape.TextureCube;
            importer.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 4096;
            importer.textureCompression = projectIsCompressed ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            importer.SaveAndReimport();
            item.Set(projectIsCompressed ? "uncompressedImportSeconds" : "compressedImportSeconds", watch.Elapsed.TotalSeconds);

            var copy = AssetDatabase.LoadAssetAtPath<Cubemap>(copyPath);
            if (copy == null)
            {
                record.bc6hSkyClean = No;
                item.Answer(No, $"Face size {cubemap.width}. The copy imported the other way did not import as a cubemap.");
                return;
            }
            Cubemap uncompressed = projectIsCompressed ? copy : cubemap;
            Cubemap compressed = projectIsCompressed ? cubemap : copy;
            item.Set("uncompressedFormat", uncompressed.format);
            item.Set("compressedFormat", compressed.format);
            item.Set("compressedFaceSize", compressed.width);

            // Both skies through the same camera with automatic exposure: the pictures should be the same picture.
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            owned.Add(profile);
            AddOverride<VisualEnvironment>(profile, owned).skyType.Override((int)SkyType.HDRI);
            var sky = AddOverride<HDRISky>(profile, owned);
            var exposure = AddOverride<Exposure>(profile, owned);
            exposure.mode.Override(ExposureMode.AutomaticHistogram);
            exposure.limitMin.Override(-10f);
            exposure.limitMax.Override(20f);
            AddOverride<Fog>(profile, owned).enabled.Override(false);

            VolumeProfile oldProfile = volume.sharedProfile;
            volume.sharedProfile = profile;
            try
            {
                camera.orthographic = false;
                camera.fieldOfView = 70f;
                camera.transform.SetPositionAndRotation(new Vector3(0f, 500f, 0f), Quaternion.Euler(-25f, 0f, 0f));
                sky.hdriSky.Override(uncompressed);
                Color32[] reference = ForestCapture.RenderPixels(camera, 640, 360, 8);
                sky.hdriSky.Override(compressed);
                Color32[] candidate = ForestCapture.RenderPixels(camera, 640, 360, 8);

                float rmse = ForestImageStats.Rmse(reference, candidate);
                ForestImageStats.Stats stats = ForestImageStats.Compute(candidate);
                item.Set("rmseAgainstUncompressed", rmse);
                item.Set("meanLuminanceCompressed", stats.meanLuminance);
                item.Set("meanLuminanceUncompressed", ForestImageStats.Compute(reference).meanLuminance);

                bool isBc6h = compressed.format == TextureFormat.BC6H;
                bool clean = rmse >= 0f && rmse < 0.02f && stats.shareMagenta == 0f && stats.meanLuminance > 0.02f;
                record.bc6hSkyClean = clean && isBc6h ? Yes : No;
                item.Answer(record.bc6hSkyClean, $"Face size {cubemap.width}. Uncompressed is {uncompressed.format}, CompressedHQ is {compressed.format} " +
                    $"(the project's sky is the {(projectIsCompressed ? "compressed" : "uncompressed")} one); " +
                    $"a sky-only render of one differs from the other by RMSE {rmse:0.0000}" + (clean ? "." : ", which is not clean."));
            }
            finally
            {
                volume.sharedProfile = oldProfile;
            }
        }

        // ------------------------------------------------------------------------------------------------
        // On imported assets: P1, P2, P3, P4
        // ------------------------------------------------------------------------------------------------

        // The M0 probe set: one foliage model, one rock model, one terrain texture and the default sky.
        const string ProbeFoliage = "fern_02", ProbeRocks = "rock_moss_set_02", ProbeGround = "forest_leaves_02";
        const string MirrorObjectA = "fern_02_b", MirrorObjectB = "rock_moss_set_02_rock13", GeneratorObject = "rock_moss_set_02_rock11";

        static void ProbeAssets(Record record, List<Item> items)
        {
            RunItem(items, "P1", "What transforms and material slot names does the importer give a Blender FBX, which horizontal axis does it mirror, and is mesh data readable with isReadable = false?",
                "Nothing: the derivation, the bounds rule and the triangle-count mapping do not assume them. Set isReadable = true if reading fails.",
                item => ProbeImportedModels(record, item));

            RunItem(items, "P2", "Which GPU formats are chosen?", "Informational.", item => ProbeTextureFormats(item));

            RunItem(items, "P3", "How long does a 2k and a 4k texture set take to import?",
                "UnityEditor.Build.OverrideTextureCompression.ForceFastCompressor while iterating.", item => ProbeImportTimes(item));

            RunItem(items, "P4", "Does MeshLodUtility.GenerateMeshLods accept a script-built mesh, and does GetTriangles(sub, lod, true) return that level?",
                "Keep vertex clustering (already the default).", item => ProbeMeshLodGenerator(record, item));
        }

        static GameObject LoadModel(string assetId, out ForestAssetEntry entry, out string path)
        {
            entry = ForestManifest.Find(assetId);
            path = null;
            if (entry == null || entry.files == null)
                return null;
            foreach (ForestAssetFile file in entry.files)
                if (file.map == "mesh")
                    path = ForestManifest.AssetPath(file.relPath);
            return path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
        }

        static void ProbeImportedModels(Record record, Item item)
        {
            var mirrors = new List<string>();
            int models = 0, objects = 0, matching = 0, readable = 0, unreadableFlag = 0;
            foreach (string assetId in new[] { ProbeFoliage, ProbeRocks })
            {
                GameObject model = LoadModel(assetId, out ForestAssetEntry entry, out string path);
                if (model == null)
                {
                    item.Set(assetId, "not imported");
                    continue;
                }
                models++;

                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer != null)
                    item.Set(assetId + ".importer", $"isReadable {importer.isReadable}, useFileScale {importer.useFileScale}, globalScale {importer.globalScale}, " +
                        $"bakeAxisConversion {importer.bakeAxisConversion}, preserveHierarchy {importer.preserveHierarchy}, materialImportMode {importer.materialImportMode}");
                item.Set(assetId + ".root", $"rotation {model.transform.localEulerAngles.ToString("0.##")}, scale {model.transform.localScale.ToString("0.####")}, children {model.transform.childCount}");

                foreach (ForestObjectEntry source in entry.objects ?? Array.Empty<ForestObjectEntry>())
                {
                    objects++;
                    Transform child = null;
                    foreach (Transform candidate in model.GetComponentsInChildren<Transform>(true))
                        if (candidate.name == source.name)
                            child = candidate;
                    var filter = child != null ? child.GetComponent<MeshFilter>() : null;
                    if (filter == null || filter.sharedMesh == null)
                    {
                        item.Set(source.name, "no child of that name with a mesh");
                        continue;
                    }

                    Mesh mesh = filter.sharedMesh;
                    int read = mesh.vertices.Length;
                    if (!mesh.isReadable) unreadableFlag++;
                    if (read == mesh.vertexCount && read > 0) readable++;

                    // The derivation of the kit build: whatever transform the importer chose, without the pack offset.
                    Matrix4x4 matrix = model.transform.worldToLocalMatrix * child.localToWorldMatrix;
                    matrix.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
                    ForestMeshTools.MeshData data = ForestMeshTools.Read(mesh);
                    ForestMeshTools.Transform(data, matrix);
                    Bounds measured = ForestMeshTools.BoundsOf(data);
                    bool matches = ForestMeshTools.MatchesSourceBounds(measured, ForestManifest.SourceBounds(source), out string mirror);
                    if (matches) matching++;
                    // Only an object that sits clearly off its pivot can say which axis was mirrored.
                    if (matches && mirror != "symmetric" && !mirrors.Contains(mirror))
                        mirrors.Add(mirror);
                    if (source.name == MirrorObjectA || source.name == MirrorObjectB)
                        item.Set("mirror." + source.name, matches ? mirror : "bounds do not match: " + mirror);

                    var slots = new List<string>();
                    var renderer = child.GetComponent<Renderer>();
                    if (renderer != null)
                        foreach (Material material in renderer.sharedMaterials)
                            slots.Add(material != null ? material.name : "(none)");
                    item.Set(source.name, $"localRotation {child.localEulerAngles.ToString("0.##")}, localScale {child.localScale.ToString("0.####")}, " +
                        $"localPosition {child.localPosition.ToString("0.###")}, raw bounds size {mesh.bounds.size.ToString("0.###")}, " +
                        $"derived min {measured.min.ToString("0.###")} max {measured.max.ToString("0.###")}, matches manifest {matches}, mirror {mirror}, " +
                        $"isReadable {mesh.isReadable}, vertices read {read} of {mesh.vertexCount}, sub-meshes {mesh.subMeshCount}, slots [{string.Join(", ", slots)}]");
                }
            }

            if (models == 0)
                throw new SkipException($"Neither {ProbeFoliage} nor {ProbeRocks} is downloaded and imported (fetch them, then Gamex/Forest/1 Import Assets).");

            // One answer is the importer's rule; several mean the objects were not all treated alike.
            if (mirrors.Count > 0)
                record.importerMirror = string.Join("|", mirrors);
            record.meshDataReadable = readable == objects && objects > 0 ? Yes : No;
            item.Set("objects", objects);
            item.Set("objectsMatchingManifestBounds", matching);
            item.Set("objectsWithIsReadableFalse", unreadableFlag);
            item.Answer(matching == objects && readable == objects ? Yes : No,
                $"{models} model(s), {objects} object(s): {matching} match the manifest under the mirror-independent rule, mirror '{record.importerMirror}', " +
                $"mesh data read on {readable} ({unreadableFlag} have isReadable off).");
        }

        static void ProbeTextureFormats(Item item)
        {
            // One example per kind of texture the pipeline imports or writes (names as fixed in section 5.1).
            var examples = new[]
            {
                new KeyValuePair<string, string>("basecolor.jpg", $"{ForestPaths.PolyHavenRoot}/{ProbeGround}/{ProbeGround}__basecolor.jpg"),
                new KeyValuePair<string, string>("normal.png", $"{ForestPaths.PolyHavenRoot}/{ProbeGround}/{ProbeGround}__normal.png"),
                new KeyValuePair<string, string>("normal.jpg", $"{ForestPaths.PolyHavenRoot}/{ProbeFoliage}/{ProbeFoliage}__normal.jpg"),
                new KeyValuePair<string, string>("basecolor.jpg at 4096", $"{ForestPaths.PolyHavenRoot}/{ProbeRocks}/{ProbeRocks}__basecolor.jpg"),
                new KeyValuePair<string, string>("mask.png", $"{ForestPaths.KitTextures}/{ProbeRocks}__mask.png"),
                new KeyValuePair<string, string>("tmask.png", $"{ForestPaths.KitTextures}/{ProbeGround}__tmask.png"),
                new KeyValuePair<string, string>("foliage basecolor.png", $"{ForestPaths.KitTextures}/{ProbeFoliage}__basecolor.png"),
                new KeyValuePair<string, string>("sky.hdr", $"{ForestPaths.PolyHavenRoot}/{ForestLightingPresets.SkyId(ForestLightingPresets.Inspect)}/{ForestLightingPresets.SkyId(ForestLightingPresets.Inspect)}__sky.hdr"),
            };

            int found = 0;
            foreach (KeyValuePair<string, string> example in examples)
            {
                var importer = AssetImporter.GetAtPath(example.Value) as TextureImporter;
                var texture = AssetDatabase.LoadAssetAtPath<Texture>(example.Value);
                if (importer == null || texture == null)
                {
                    item.Set(example.Key, $"{example.Value}: not imported");
                    continue;
                }
                found++;
                item.Set(example.Key, $"{example.Value}: automatic format {importer.GetAutomaticFormat("Standalone")}, imported as {texture.graphicsFormat}, " +
                    $"{texture.width} x {texture.height}, {texture.mipmapCount} mips, compression {importer.textureCompression}, sRGB {importer.sRGBTexture}");
            }
            if (found == 0)
                throw new SkipException("None of the probe textures is imported yet (fetch the M0 probe set, then Gamex/Forest/1 Import Assets).");
            item.Answer(Info, $"Formats of {found} of {examples.Length} texture kinds recorded.");
        }

        static void ProbeImportTimes(Item item)
        {
            if (ForestArgs.Flag("-forestProbeNoReimport"))
                throw new SkipException("-forestProbeNoReimport was passed. The Editor log has one 'Start importing ... in N seconds' line per asset.");

            // A forced re-import of six files gives the same artifacts again; nothing is renamed or deleted.
            var sets = new[]
            {
                new KeyValuePair<string, string[]>("2k set " + ProbeGround, new[]
                {
                    $"{ForestPaths.PolyHavenRoot}/{ProbeGround}/{ProbeGround}__basecolor.jpg",
                    $"{ForestPaths.PolyHavenRoot}/{ProbeGround}/{ProbeGround}__normal.png",
                    $"{ForestPaths.KitTextures}/{ProbeGround}__tmask.png",
                }),
                new KeyValuePair<string, string[]>("4k set " + ProbeRocks, new[]
                {
                    $"{ForestPaths.PolyHavenRoot}/{ProbeRocks}/{ProbeRocks}__basecolor.jpg",
                    $"{ForestPaths.PolyHavenRoot}/{ProbeRocks}/{ProbeRocks}__normal.png",
                    $"{ForestPaths.KitTextures}/{ProbeRocks}__mask.png",
                }),
            };

            var totals = new List<string>();
            foreach (KeyValuePair<string, string[]> set in sets)
            {
                double total = 0.0;
                int imported = 0;
                foreach (string path in set.Value)
                {
                    if (AssetImporter.GetAtPath(path) == null)
                    {
                        item.Set(path, "not imported");
                        continue;
                    }
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                    double seconds = watch.Elapsed.TotalSeconds;
                    item.Set(path, $"{seconds:0.00} s");
                    total += seconds;
                    imported++;
                }
                if (imported > 0)
                    totals.Add($"{set.Key}: {total:0.0} s for {imported} file(s)");
            }
            if (totals.Count == 0)
                throw new SkipException("None of the probe textures is imported yet (fetch the M0 probe set, then Gamex/Forest/1 Import Assets).");
            item.Set("textureCompressionOverride", EditorUserBuildSettings.overrideTextureCompression);
            item.Answer(Info, string.Join("; ", totals) + ".");
        }

        static void ProbeMeshLodGenerator(Record record, Item item)
        {
            var created = new List<Object>();
            try
            {
                Mesh sphere = Icosphere(5, 1f, "Probe Generator Sphere");
                created.Add(sphere);
                bool sphereWorks = TryGenerator(item, "sphere", sphere);

                bool rockTested = false, rockWorks = false;
                GameObject model = LoadModel(ProbeRocks, out _, out _);
                Transform child = null;
                if (model != null)
                    foreach (Transform candidate in model.GetComponentsInChildren<Transform>(true))
                        if (candidate.name == GeneratorObject)
                            child = candidate;
                var filter = child != null ? child.GetComponent<MeshFilter>() : null;
                if (filter != null && filter.sharedMesh != null)
                {
                    // A script-built copy: the importer's own mesh must never be changed.
                    Mesh copy = ForestMeshTools.ToMesh(ForestMeshTools.Read(filter.sharedMesh), "Probe Generator Rock", false);
                    created.Add(copy);
                    rockTested = true;
                    rockWorks = TryGenerator(item, GeneratorObject, copy);
                }
                else
                {
                    item.Set(GeneratorObject, "not imported; only the sphere was tried");
                }

                bool works = sphereWorks && (!rockTested || rockWorks);
                record.unityMeshLodGenerator = works && rockTested ? Yes : works ? Unknown : No;
                item.Answer(works ? (rockTested ? Yes : Info) : No, works
                    ? (rockTested ? "The generator accepted a script-built sphere and a script-built copy of a scanned rock, and each level read back with fewer triangles."
                                  : "The generator worked on a script-built sphere; the scanned rock is not imported yet, so the answer for real scans is still open.")
                    : "The generator did not give usable levels on a script-built mesh; see the details. Vertex clustering stays the route.");
            }
            finally
            {
                foreach (Object leftover in created)
                    if (leftover != null)
                        Object.DestroyImmediate(leftover);
            }
        }

        static bool TryGenerator(Item item, string label, Mesh mesh)
        {
            int sourceTriangles = (int)(mesh.GetIndexCount(0) / 3);
            try
            {
                MeshLodUtility.GenerateMeshLods(mesh, 6);
            }
            catch (Exception exception)
            {
                // The engine refusing the mesh is the answer to this question, not a fault of the probe.
                item.Set(label, $"GenerateMeshLods threw {exception.GetType().Name}: {exception.Message}");
                return false;
            }

            int levels = mesh.lodCount;
            var counts = new List<int>();
            bool indicesValid = true;
            for (int level = 0; level < levels; level++)
            {
                int[] triangles = mesh.GetTriangles(0, level, true);
                counts.Add(triangles.Length / 3);
                foreach (int index in triangles)
                    if (index < 0 || index >= mesh.vertexCount)
                        indicesValid = false;
            }

            bool decreasing = levels > 1;
            for (int level = 1; level < counts.Count; level++)
                if (counts[level] >= counts[level - 1])
                    decreasing = false;
            item.Set(label, $"{sourceTriangles} triangles, lodCount {levels}, triangles per level [{string.Join(", ", counts)}], indices valid {indicesValid}");
            return levels > 1 && decreasing && indicesValid && counts[0] > 0;
        }

        // ------------------------------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------------------------------

        static void EnsureTempFolder()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(TempFolder), Path.GetFileName(TempFolder));
        }

        static void DeleteTempFolder()
        {
            if (AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.DeleteAsset(TempFolder);
        }

        /// <summary>A sphere made by subdividing an icosahedron: closed, evenly triangulated, 20 * 4^n triangles.</summary>
        internal static Mesh Icosphere(int subdivisions, float radius, string name)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var vertices = new List<Vector3>
            {
                new Vector3(-1f, t, 0f), new Vector3(1f, t, 0f), new Vector3(-1f, -t, 0f), new Vector3(1f, -t, 0f),
                new Vector3(0f, -1f, t), new Vector3(0f, 1f, t), new Vector3(0f, -1f, -t), new Vector3(0f, 1f, -t),
                new Vector3(t, 0f, -1f), new Vector3(t, 0f, 1f), new Vector3(-t, 0f, -1f), new Vector3(-t, 0f, 1f),
            };
            for (int i = 0; i < vertices.Count; i++)
                vertices[i] = vertices[i].normalized;
            var triangles = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };

            for (int pass = 0; pass < subdivisions; pass++)
            {
                var midpoints = new Dictionary<long, int>();
                int Midpoint(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (midpoints.TryGetValue(key, out int index))
                        return index;
                    vertices.Add(((vertices[a] + vertices[b]) * 0.5f).normalized);
                    midpoints[key] = vertices.Count - 1;
                    return vertices.Count - 1;
                }

                var next = new List<int>(triangles.Count * 4);
                for (int i = 0; i < triangles.Count; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    int ab = Midpoint(a, b), bc = Midpoint(b, c), ca = Midpoint(c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                triangles = next;
            }

            var positions = new Vector3[vertices.Count];
            var normals = new Vector3[vertices.Count];
            var uvs = new Vector2[vertices.Count];
            for (int i = 0; i < positions.Length; i++)
            {
                normals[i] = vertices[i];
                positions[i] = vertices[i] * radius;
                uvs[i] = new Vector2(Mathf.Atan2(vertices[i].z, vertices[i].x) / (2f * Mathf.PI) + 0.5f, Mathf.Asin(Mathf.Clamp(vertices[i].y, -1f, 1f)) / Mathf.PI + 0.5f);
            }

            var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            mesh.indexFormat = positions.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = positions;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
