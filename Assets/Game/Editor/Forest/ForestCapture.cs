using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering.HighDefinition.Attributes;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Gamex.EditorTools
{
    /// <summary>
    /// Renders the forest to image files so it can be judged without opening the Editor:
    /// <c>Unity -batchmode -projectPath . -executeMethod Gamex.EditorTools.ForestCapture.Capture -captureSet full -captureOut DIR -quit</c>
    ///
    /// Every image gets a 1:1 centre crop and a JSON file of pixel statistics beside it, and every run a
    /// capture-summary.json that says whether any frame is black, blown out or shows error colours.
    /// These are edit-mode stills: exposure is instant, shader time is 0 and motion blur is off. What the
    /// player sees is the PlayMode test Render_ReferenceStills.
    ///
    /// N1: the player's torch rides on the capture camera at the hand's pose, switched on as the game starts it
    /// (-captureFlashlight off renders without it); -captureViews a,b,c renders only those viewpoints; -captureCullDistance N (W1 I2b)
    /// culls the forest at N m as the night preset does in play;
    /// -forestPreset Name captures another preset of the scene.
    /// </summary>
    public static class ForestCapture
    {
        const int FullWidth = 2560, FullHeight = 1440;
        // SSAO is still 44 % unconverged after 16 frames; 64 leaves under 4 %.
        const int FullFrames = 64;
        const int QuickWidth = 960, QuickHeight = 540, QuickFrames = 32;
        const int DebugWidth = 1280, DebugHeight = 720, DebugFrames = 8;
        // A reviewer who sees a downscaled frame cannot judge sharpness or noise without a 100 % crop.
        const int CropSize = 768;

        const float PlayerFieldOfView = 70f;
        const float PlayerEyeHeight = 1.65f;

        static readonly string[] QuickViews = { "trail_start", "trail_mid_fwd", "clearing", "ground_close", "overview_oblique" };
        static readonly string[] DebugViews = { "trail_start", "clearing", "trunk_close" };
        // First-person views that must sit in the exposure window of the preset, like the PlayMode exposure test.
        static readonly string[] ExposureViews = { "trail_start", "trail_mid_fwd", "clearing" };

        // Any frame outside these is black or white, whatever it shows.
        const float SaneLuminanceMin = 0.02f, SaneLuminanceMax = 0.90f, SaneClippedShare = 0.5f;

        [MenuItem("Gamex/Forest/5 Capture Quick Set")]
        public static void Capture()
        {
            string set = ForestArgs.Value("-captureSet", "quick");
            switch (set)
            {
                case "quick": RunSets(set, quick: true, full: false, debug: false); break;
                case "full": RunSets(set, quick: false, full: true, debug: false); break;
                case "debug": RunSets(set, quick: false, full: false, debug: true); break;
                default: throw new ArgumentException($"-captureSet must be quick, full or debug, not '{set}'.");
            }
        }

        [MenuItem("Gamex/Forest/5 Capture Full Set")]
        public static void CaptureFull() => RunSets("full+debug", quick: false, full: true, debug: true);

        /// <summary>
        /// The capture loop. Renders <paramref name="frames"/> frames so TAA, fog and SSAO converge, and returns
        /// the last one. The caller destroys the texture.
        /// </summary>
        public static Texture2D Render(Camera camera, int width, int height, int frames)
        {
            if (camera == null)
                throw new ArgumentNullException(nameof(camera));
            if (width < 1 || height < 1 || frames < 1)
                throw new ArgumentException($"Cannot render {frames} frame(s) of {width} x {height}.");

            // The camera already has TAA and dithering (set by the builder).
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            bool oldAsync = ShaderUtil.allowAsyncCompilation;
            // Script renders then compile synchronously: no cyan placeholders in the picture.
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                camera.targetTexture = target;
                // Creates the pipeline instance and the HDCamera.
                camera.Render();
                // Camera cut: drop the history of the previous viewpoint.
                if (RenderPipelineManager.currentPipeline is HDRenderPipeline)
                    HDCamera.GetOrCreate(camera).Reset();

                int waitAt = Mathf.Min(1, frames - 1);
                for (int i = 0; i < frames; i++)
                {
                    camera.Render();
                    // The sky's ambient probe arrives by asynchronous readback; without this it can still be black.
                    if (i == waitAt)
                        AsyncGPUReadback.WaitAllRequests();
                }

                RenderTexture.active = target;
                var image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                return image;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                ShaderUtil.allowAsyncCompilation = oldAsync;
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Scene captures
        // ------------------------------------------------------------------------------------------------

        sealed class View
        {
            public string id;
            public Vector3 position;
            public Quaternion rotation;
            public float fieldOfView;
            public bool orthographic;
            public float orthographicSize;
        }

        // JsonUtility writes only [Serializable] types, and ForestImageStats.Stats is declared without the attribute.
        [Serializable]
        struct StatsRecord
        {
            public float meanLuminance, medianLuminance, shareBlack, shareWhite, meanR, meanG, meanB, shareMagenta, shareCyan;

            public StatsRecord(ForestImageStats.Stats stats)
            {
                meanLuminance = stats.meanLuminance;
                medianLuminance = stats.medianLuminance;
                shareBlack = stats.shareBlack;
                shareWhite = stats.shareWhite;
                meanR = stats.meanR;
                meanG = stats.meanG;
                meanB = stats.meanB;
                shareMagenta = stats.shareMagenta;
                shareCyan = stats.shareCyan;
            }
        }

        [Serializable]
        sealed class Sidecar
        {
            public string id, set, file, crop;
            public int width, height, frames;
            public StatsRecord stats;
            public int magentaPixels, cyanPixels;
            public float rmseToPreviousRun;       // -1 when there was no earlier image of this size
            public Vector3 cameraPosition, cameraEuler;
            public float fieldOfView, orthographicSize;
            public bool orthographic;
            public string scene, preset, flashlight, apv, textureCompressionOverride, unityVersion, qualityLevel, writtenUtc;
            public int seed;
            public bool auditPassed, lodsForced;
            public string[] problems = Array.Empty<string>();
        }

        [Serializable]
        sealed class Summary
        {
            public bool passed;
            public string set, scene, preset, flashlight, apv, outputFolder, textureCompressionOverride, unityVersion, writtenUtc;
            public int seed;
            public bool auditPassed;
            public float seconds;
            public string[] images = Array.Empty<string>();
            public string[] debugImages = Array.Empty<string>();
            public string[] missingViews = Array.Empty<string>();
            public string[] problems = Array.Empty<string>();
        }

        static void RunSets(string setName, bool quick, bool full, bool debug)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            string outputFolder = ForestArgs.Value("-captureOut", ForestPaths.DefaultCaptureFolder);
            string scenePath = ForestArgs.Value("-captureScene", ForestPaths.ScenePath);
            bool isForest = scenePath == ForestPaths.ScenePath;
            if (!File.Exists(scenePath))
                throw new FileNotFoundException($"Scene {scenePath} does not exist. Build it first (Gamex/Forest/3 Rebuild Forest Scene), or pass -captureScene.");

            ForestAudit.Report audit = isForest ? RequirePassedAudit() : null;
            // It persists in Library, so every run says what it is: final captures are made with it off.
            string compression = EditorUserBuildSettings.overrideTextureCompression.ToString();
            Debug.Log($"Forest capture: set {setName}, scene {scenePath}, output {Path.GetFullPath(outputFolder)}, texture compression override {compression}.");

            using (ForestSceneSession session = ForestSceneSession.Open(scenePath))
            {
                if (session == null)
                    return;   // cancelled in the save dialog

                Directory.CreateDirectory(outputFolder);
                string originalPreset = ForestLighting.ActivePreset();
                string wantedPreset = ForestArgs.Value("-forestPreset");
                string torchMode = ForestArgs.Value("-captureFlashlight", "on");
                if (torchMode != "on" && torchMode != "off")
                    throw new ArgumentException($"-captureFlashlight must be on or off, not '{torchMode}'.");
                Camera camera = null;
                ForestFlashlight.CaptureTorch torch = null;
                var forced = new List<LODGroup>();
                ForestCullDistance.Restore cull = null;
                try
                {
                    // W1 I2b: -captureCullDistance N renders with the forest culled at N m (ForestCullDistance, which the night preset applies
                    // in play only), for the before-and-after night captures of the owner's decision; undone below, the scene never saved.
                    float cullMetres = float.Parse(ForestArgs.Value("-captureCullDistance", "0"), System.Globalization.CultureInfo.InvariantCulture);
                    if (cullMetres > 0f)
                    {
                        cull = ForestCullDistance.Apply(SceneManager.GetActiveScene(), cullMetres);
                        session.MarkModified();
                        Debug.Log($"Forest capture: forest culled at {Mathf.Max(ForestCullDistance.Minimum, cullMetres)} m ({cull.Count} LOD groups).");
                    }
                    if (!string.IsNullOrEmpty(wantedPreset) && wantedPreset != originalPreset)
                    {
                        if (!ForestLighting.ApplyPreset(wantedPreset))
                            throw new InvalidOperationException($"The scene has no lighting preset '{wantedPreset}'.");
                        session.MarkModified();
                    }
                    string preset = ForestLighting.ActivePreset();

                    Camera playerCamera = Camera.main;
                    camera = CreateCaptureCamera(playerCamera);
                    torch = ForestFlashlight.HoldForCapture(playerCamera, camera, torchMode == "on");
                    List<View> views = FindViews(SceneManager.GetActiveScene(), preset, playerCamera);
                    string viewList = ForestArgs.Value("-captureViews");
                    if (!string.IsNullOrEmpty(viewList))
                        views = Select(views, viewList.Split(','), new List<string>());
                    SwitchOffDebugDisplay(camera);

                    var summary = new Summary
                    {
                        set = setName, scene = scenePath, preset = preset, flashlight = torch.State, outputFolder = Path.GetFullPath(outputFolder),
                        apv = audit != null ? audit.apv : "", seed = audit != null ? audit.seed : 0, auditPassed = audit != null && audit.passed,
                        textureCompressionOverride = compression, unityVersion = Application.unityVersion, writtenUtc = UtcNow(),
                    };
                    var images = new List<string>();
                    var debugImages = new List<string>();
                    var missing = new List<string>();
                    var problems = new List<string>();

                    if (quick || full)
                    {
                        // Outside the forest there is no viewpoint list to pick a quick set from: render what was found.
                        List<View> selected = full || !isForest ? views : Select(views, QuickViews, missing);
                        int width = full ? FullWidth : QuickWidth, height = full ? FullHeight : QuickHeight, frames = full ? FullFrames : QuickFrames;
                        foreach (View view in selected)
                        {
                            Sidecar sidecar = CaptureView(camera, view, width, height, frames, outputFolder, forced);
                            sidecar.set = full ? "full" : "quick";
                            sidecar.scene = scenePath;
                            sidecar.preset = preset;
                            sidecar.flashlight = torch.State;
                            sidecar.apv = summary.apv;
                            sidecar.seed = summary.seed;
                            sidecar.auditPassed = summary.auditPassed;
                            sidecar.problems = Judge(view.id, sidecar.stats, sidecar.magentaPixels + sidecar.cyanPixels, isForest, preset, torch.State).ToArray();
                            File.WriteAllText(Path.Combine(outputFolder, view.id + ".json"), JsonUtility.ToJson(sidecar, true));
                            images.Add(sidecar.file);
                            foreach (string problem in sidecar.problems)
                                problems.Add($"{view.id}: {problem}");
                        }
                        if (selected.Count == 0)
                            problems.Add("no viewpoint was found to render");
                    }

                    if (debug)
                        CaptureDebugViews(camera, Select(views, DebugViews, missing), Path.Combine(outputFolder, "debug"), debugImages);

                    foreach (string id in missing)
                        problems.Add($"viewpoint '{id}' is not in the scene (Gameplay/Viewpoints)");

                    summary.images = images.ToArray();
                    summary.debugImages = debugImages.ToArray();
                    summary.missingViews = missing.ToArray();
                    summary.problems = problems.ToArray();
                    summary.passed = problems.Count == 0;
                    summary.seconds = (float)watch.Elapsed.TotalSeconds;
                    File.WriteAllText(Path.Combine(outputFolder, "capture-summary.json"), JsonUtility.ToJson(summary, true));

                    string line = $"Forest capture: {images.Count} image(s) and {debugImages.Count} debug image(s) written to {summary.outputFolder} in {summary.seconds:0} s " +
                        $"(preset {preset}, flashlight {torch.State}).";
                    if (summary.passed)
                        Debug.Log(line);
                    else
                        // Everything is on disk; the exception makes a batch run end with a failing exit code.
                        throw new InvalidOperationException($"{line} {problems.Count} problem(s):\n  " + string.Join("\n  ", problems));
                }
                finally
                {
                    foreach (LODGroup group in forced)
                        if (group != null)
                            group.ForceLOD(-1);
                    cull?.Undo();
                    torch?.Dispose();
                    if (camera != null)
                        Object.DestroyImmediate(camera.gameObject);
                    if (ForestLighting.ActivePreset() != originalPreset && !string.IsNullOrEmpty(originalPreset))
                        ForestLighting.ApplyPreset(originalPreset);
                }
            }
        }

        // Captures of a scene the audit rejected would be judged as if they showed the design.
        static ForestAudit.Report RequirePassedAudit()
        {
            bool allowed = ForestArgs.Flag("-forestAllowFailedAudit");
            ForestAudit.Report report = File.Exists(ForestPaths.ReportPath)
                ? JsonUtility.FromJson<ForestAudit.Report>(File.ReadAllText(ForestPaths.ReportPath))
                : null;

            if (report == null)
            {
                if (!allowed)
                    throw new InvalidOperationException($"{ForestPaths.ReportPath} does not exist, so the scene has not been audited. " +
                        "Rebuild the scene or run Gamex/Forest/Audit Open Scene, or pass -forestAllowFailedAudit.");
                Debug.LogWarning("Forest capture: the scene has no audit report; capturing anyway because -forestAllowFailedAudit was passed.");
                return null;
            }
            if (!report.passed)
            {
                if (!allowed)
                    throw new InvalidOperationException($"The last audit of the forest scene failed with {report.errors} error(s) ({ForestPaths.ReportPath}). " +
                        "Fix the scene, or pass -forestAllowFailedAudit to capture it as it is.");
                Debug.LogWarning($"Forest capture: the last audit failed with {report.errors} error(s); capturing anyway because -forestAllowFailedAudit was passed.");
            }
            return report;
        }

        static Sidecar CaptureView(Camera camera, View view, int width, int height, int frames, string outputFolder, List<LODGroup> forced)
        {
            ApplyView(camera, view);

            // A camera high above the forest would cull or coarsen everything below it. That also holds
            // for the oblique overview: it hangs 40 m up, and ferns are culled at 40 m, dry branches at
            // 35 m and grass at 25 m, so without this it shows a floor no player ever sees.
            bool forceLods = view.orthographic || view.id == "overview_oblique";
            if (forceLods)
                ForceOverviewLods(forced);
            Color32[] pixels;
            try
            {
                pixels = RenderPixels(camera, width, height, frames);
            }
            finally
            {
                foreach (LODGroup group in forced)
                    if (group != null)
                        group.ForceLOD(-1);
                forced.Clear();
            }

            string file = view.id + ".png";
            string path = Path.Combine(outputFolder, file);
            // Read the image of the previous run before it is replaced. A new folder has none; -captureBaseline DIR names
            // the earlier set to compare with instead (review 1 of the M3 captures: every sidecar of a fresh folder said -1).
            string baseline = ForestArgs.Value("-captureBaseline");
            string previousPath = string.IsNullOrEmpty(baseline) ? path : Path.Combine(baseline, file);
            Color32[] previous = ReadPng(previousPath, out int previousWidth, out int previousHeight);
            float rmse = previous != null && previousWidth == width && previousHeight == height ? ForestImageStats.Rmse(pixels, previous) : -1f;
            WritePng(path, pixels, width, height);

            int cropWidth = Mathf.Min(CropSize, width), cropHeight = Mathf.Min(CropSize, height);
            string crop = view.id + "_crop.png";
            WritePng(Path.Combine(outputFolder, crop), CentreCrop(pixels, width, height, cropWidth, cropHeight), cropWidth, cropHeight);

            ForestImageStats.Stats stats = ForestImageStats.Compute(pixels);
            return new Sidecar
            {
                id = view.id, file = file, crop = crop, width = width, height = height, frames = frames,
                stats = new StatsRecord(stats),
                magentaPixels = Mathf.RoundToInt(stats.shareMagenta * pixels.Length),
                cyanPixels = Mathf.RoundToInt(stats.shareCyan * pixels.Length),
                rmseToPreviousRun = rmse,
                cameraPosition = camera.transform.position, cameraEuler = camera.transform.eulerAngles,
                fieldOfView = camera.fieldOfView, orthographic = camera.orthographic, orthographicSize = camera.orthographicSize,
                textureCompressionOverride = EditorUserBuildSettings.overrideTextureCompression.ToString(),
                unityVersion = Application.unityVersion,
                qualityLevel = QualitySettings.names[QualitySettings.GetQualityLevel()],
                writtenUtc = UtcNow(), lodsForced = forceLods,
            };
        }

        static List<string> Judge(string viewId, StatsRecord stats, int errorPixels, bool isForest, string preset, string flashlight)
        {
            var problems = new List<string>();
            if (errorPixels > 0)
                problems.Add($"{errorPixels} near-magenta or near-cyan pixel(s) ({(stats.shareMagenta + stats.shareCyan) * 100f:0.000} %): a broken material or a shader that was still compiling");
            if (!isForest)
                return problems;   // another scene may be dark or bright by design

            if (stats.meanLuminance < SaneLuminanceMin || stats.shareBlack > SaneClippedShare)
                problems.Add($"the frame is black (mean luminance {stats.meanLuminance:0.000}, {stats.shareBlack * 100f:0} % crushed)");
            else if (stats.meanLuminance > SaneLuminanceMax || stats.shareWhite > SaneClippedShare)
                problems.Add($"the frame is blown out (mean luminance {stats.meanLuminance:0.000}, {stats.shareWhite * 100f:0} % clipped)");
            else if (Array.IndexOf(ExposureViews, viewId) < 0)
                return problems;
            else if (preset == "OvercastMist"
                && (stats.meanLuminance < ForestBudgets.OvercastLuminanceMin || stats.meanLuminance > ForestBudgets.OvercastLuminanceMax))
                problems.Add($"mean luminance {stats.meanLuminance:0.000} is outside {ForestBudgets.OvercastLuminanceMin:0.00} to {ForestBudgets.OvercastLuminanceMax:0.00}");
            else if (preset == "Night" && flashlight == "off"
                && (stats.meanLuminance < ForestBudgets.NightDarkLuminanceMin || stats.meanLuminance > ForestBudgets.NightDarkLuminanceMax))
                problems.Add($"mean luminance {stats.meanLuminance:0.000} without the torch is outside the night's {ForestBudgets.NightDarkLuminanceMin:0.000} to {ForestBudgets.NightDarkLuminanceMax:0.000}");
            else if (preset == "Night" && flashlight == "on")
            {
                if (stats.meanLuminance < ForestBudgets.NightLitLuminanceMin || stats.meanLuminance > ForestBudgets.NightLitLuminanceMax)
                    problems.Add($"mean luminance {stats.meanLuminance:0.000} with the torch is outside the night's {ForestBudgets.NightLitLuminanceMin:0.000} to {ForestBudgets.NightLitLuminanceMax:0.000}");
                if (stats.shareWhite > ForestBudgets.NightClippedWhiteShareLimit)
                    problems.Add($"{stats.shareWhite * 100f:0.00} % of the frame is clipped to white by the torch (limit {ForestBudgets.NightClippedWhiteShareLimit * 100f:0.0} %)");
            }
            return problems;
        }

        // ------------------------------------------------------------------------------------------------
        // Viewpoints
        // ------------------------------------------------------------------------------------------------

        static List<View> FindViews(Scene scene, string preset, Camera template)
        {
            Transform anchors = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == "Gameplay")
                    anchors = root.transform.Find("Viewpoints");
            if (anchors == null || anchors.childCount == 0)
                return FallbackViews(scene, template);

            // The scene holds each pose, so viewpoints follow the layout; the layout holds what a transform cannot.
            var byId = new Dictionary<string, ForestLayout.Viewpoint>();
            var order = new List<string>();
            foreach (ForestLayout.Viewpoint viewpoint in ForestLayout.Viewpoints ?? Array.Empty<ForestLayout.Viewpoint>())
            {
                byId[viewpoint.id] = viewpoint;
                order.Add(viewpoint.id);
            }

            var views = new List<View>();
            var taken = new HashSet<string>();
            foreach (string id in order)
            {
                Transform anchor = anchors.Find(id);
                if (anchor != null && taken.Add(id))
                    views.Add(ToView(anchor, byId, preset));
            }
            foreach (Transform anchor in anchors)
                if (taken.Add(anchor.name))
                    views.Add(ToView(anchor, byId, preset));
            return views;
        }

        static View ToView(Transform anchor, Dictionary<string, ForestLayout.Viewpoint> layout, string preset)
        {
            var view = new View { id = anchor.name, position = anchor.position, rotation = anchor.rotation };
            if (layout.TryGetValue(anchor.name, out ForestLayout.Viewpoint viewpoint))
            {
                view.fieldOfView = viewpoint.fieldOfView;
                view.orthographic = viewpoint.orthographic;
                view.orthographicSize = viewpoint.orthographicSize;
            }

            // Looks toward the key light's source, which depends on the preset being captured.
            ForestLightingPresets.Preset values = ForestLightingPresets.Get(preset);
            if (anchor.name == "backlit" && values != null)
                view.rotation = Quaternion.Euler(anchor.eulerAngles.x, values.keyBearing + 180f, 0f);
            return view;
        }

        // For a scene without forest viewpoints (the greybox): what the player sees, and two looks from above.
        static List<View> FallbackViews(Scene scene, Camera template)
        {
            var views = new List<View>();
            if (template != null)
                views.Add(new View { id = "player_view", position = template.transform.position, rotation = template.transform.rotation });

            bool any = false;
            var bounds = new Bounds();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false))
                {
                    if (!any) bounds = renderer.bounds;
                    else bounds.Encapsulate(renderer.bounds);
                    any = true;
                }
            }
            if (!any)
                return views;

            float reach = Mathf.Max(bounds.extents.x, bounds.extents.z);
            Quaternion oblique = Quaternion.Euler(45f, 0f, 0f);
            views.Add(new View { id = "overview_oblique", position = bounds.center - oblique * Vector3.forward * (reach * 1.2f + 20f), rotation = oblique });
            views.Add(new View
            {
                id = "overview_ortho", position = new Vector3(bounds.center.x, bounds.max.y + 20f, bounds.center.z),
                rotation = Quaternion.Euler(90f, 0f, 0f), orthographic = true, orthographicSize = reach * 1.05f,
            });
            return views;
        }

        static List<View> Select(List<View> views, string[] ids, List<string> missing)
        {
            var selected = new List<View>();
            foreach (string id in ids)
            {
                View view = views.Find(candidate => candidate.id == id);
                if (view != null)
                    selected.Add(view);
                else if (!missing.Contains(id))
                    missing.Add(id);
            }
            return selected;
        }

        static void ApplyView(Camera camera, View view)
        {
            camera.transform.SetPositionAndRotation(view.position, view.rotation);
            camera.orthographic = view.orthographic;
            if (view.orthographic)
            {
                camera.orthographicSize = view.orthographicSize > 0f ? view.orthographicSize : 80f;
                // Everything between the camera and well below the lowest ground must be inside the frustum.
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, view.position.y + 100f);
            }
            else
            {
                camera.fieldOfView = view.fieldOfView > 0f ? view.fieldOfView : PlayerFieldOfView;
            }
        }

        // Trees at the table's LOD1 mesh (1,050 trees at LOD0 would be over a hundred million triangles),
        // everything else at its nearest level.
        static void ForceOverviewLods(List<LODGroup> forced)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (LODGroup group in root.GetComponentsInChildren<LODGroup>(false))
                {
                    int level = 0;
                    if (ForestAudit.TryParseInstanceName(group.name, out string prefabId, out _) && IsTree(prefabId))
                        level = ForestKitSpec.ScanLodDistance(prefabId) > 0f ? 2 : 1;
                    group.ForceLOD(Mathf.Clamp(level, 0, Mathf.Max(0, group.lodCount - 1)));
                    forced.Add(group);
                }
            }
        }

        static bool IsTree(string prefabId)
        {
            return Array.IndexOf(ForestPrefabIds.TallFirs, prefabId) >= 0 || Array.IndexOf(ForestPrefabIds.YoungFirs, prefabId) >= 0;
        }

        /// <summary>A camera of its own, so no object of the scene is moved. It takes the player camera's lens and settings.</summary>
        internal static Camera CreateCaptureCamera(Camera template)
        {
            var holder = new GameObject("Forest Capture Camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = holder.AddComponent<Camera>();
            if (template != null)
            {
                camera.CopyFrom(template);
            }
            else
            {
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 400f;
                camera.fieldOfView = PlayerFieldOfView;
            }
            camera.enabled = false;
            camera.targetTexture = null;
            // CopyFrom also copies the template's aspect ratio as a fixed value; the picture's shape must come from its target.
            camera.ResetAspect();
            holder.tag = "Untagged";

            var data = holder.GetComponent<HDAdditionalCameraData>();
            if (data == null)
                data = holder.AddComponent<HDAdditionalCameraData>();
            var templateData = template != null ? template.GetComponent<HDAdditionalCameraData>() : null;
            if (templateData != null)
                templateData.CopyTo(data);
            // TAA, dithering and NaN stopping, exactly as the builder sets them on the player camera.
            ForestLighting.ConfigureCamera(camera);
            return camera;
        }

        // ------------------------------------------------------------------------------------------------
        // Debug views
        // ------------------------------------------------------------------------------------------------

        sealed class DebugMode
        {
            public string name;
            public Action<DebugDisplaySettings> apply;
        }

        static readonly DebugMode[] DebugModes =
        {
            new DebugMode { name = "albedo", apply = debug => debug.SetDebugViewCommonMaterialProperty(MaterialSharedProperty.Albedo) },
            new DebugMode { name = "normal", apply = debug => debug.SetDebugViewCommonMaterialProperty(MaterialSharedProperty.Normal) },
            new DebugMode { name = "diffuse_lighting", apply = debug => debug.SetDebugLightingMode(DebugLightingMode.DiffuseLighting) },
            new DebugMode
            {
                name = "clay",
                apply = debug =>
                {
                    debug.data.lightingDebugSettings.overrideAlbedo = true;
                    debug.data.lightingDebugSettings.overrideAlbedoValue = new Color(0.5f, 0.5f, 0.5f);
                },
            },
            new DebugMode { name = "cascades", apply = debug => debug.SetDebugLightingMode(DebugLightingMode.VisualizeCascade) },
            new DebugMode { name = "mip_ratio", apply = debug => debug.SetMipMapMode(DebugMipMapMode.MipRatio) },
        };

        static void CaptureDebugViews(Camera camera, List<View> views, string folder, List<string> written)
        {
            if (views.Count == 0)
                return;
            Directory.CreateDirectory(folder);

            // The pipeline instance exists only after a first render.
            ApplyView(camera, views[0]);
            RenderPixels(camera, 64, 64, 1);
            var pipeline = RenderPipelineManager.currentPipeline as HDRenderPipeline;
            if (pipeline == null)
                throw new InvalidOperationException("The active render pipeline is not HDRP; debug views cannot be captured.");

            // The settings object is an Editor singleton: a mode left on would show in the owner's Game view.
            DebugDisplaySettings debug = pipeline.debugDisplaySettings;
            bool oldOverride = debug.data.lightingDebugSettings.overrideAlbedo;
            Color oldValue = debug.data.lightingDebugSettings.overrideAlbedoValue;
            try
            {
                foreach (View view in views)
                {
                    ApplyView(camera, view);
                    foreach (DebugMode mode in DebugModes)
                    {
                        ResetDebugDisplay(debug);
                        mode.apply(debug);
                        Color32[] pixels = RenderPixels(camera, DebugWidth, DebugHeight, DebugFrames);
                        string file = $"{view.id}__{mode.name}.png";
                        WritePng(Path.Combine(folder, file), pixels, DebugWidth, DebugHeight);
                        written.Add("debug/" + file);
                    }
                }
            }
            finally
            {
                ResetDebugDisplay(debug);
                debug.data.lightingDebugSettings.overrideAlbedo = oldOverride;
                debug.data.lightingDebugSettings.overrideAlbedoValue = oldValue;
            }
        }

        // A debug view left on in the Rendering Debugger would be in every picture instead of the scene.
        static void SwitchOffDebugDisplay(Camera camera)
        {
            RenderPixels(camera, 64, 64, 1);
            var pipeline = RenderPipelineManager.currentPipeline as HDRenderPipeline;
            if (pipeline == null || !pipeline.debugDisplaySettings.IsDebugDisplayEnabled())
                return;
            ResetDebugDisplay(pipeline.debugDisplaySettings);
            Debug.LogWarning("Forest capture: the Rendering Debugger had a debug view switched on; it was switched off so the captures show the scene.");
        }

        static void ResetDebugDisplay(DebugDisplaySettings debug)
        {
            debug.SetDebugViewCommonMaterialProperty(MaterialSharedProperty.None);
            debug.SetDebugLightingMode(DebugLightingMode.None);
            debug.SetMipMapMode(DebugMipMapMode.None);
            debug.SetFullScreenDebugMode(FullScreenDebugMode.None);
            debug.data.lightingDebugSettings.overrideAlbedo = false;
        }

        // ------------------------------------------------------------------------------------------------
        // Kit captures
        // ------------------------------------------------------------------------------------------------

        const int TurntableTile = 960, TurntableFrames = 24;
        const int SheetTile = 768, SheetFrames = 16;
        const int DistanceTile = 512;
        const float TurntableFieldOfView = 35f;
        static readonly float[] TurntableYaws = { 0f, 90f, 180f, 270f };
        // Where the thinned tree levels are judged: inside LOD0, inside LOD1 and inside LOD2.
        static readonly float[] TreeDistances = { 8f, 25f, 70f };
        const float FoliageCropDistance = 1.5f;
        const float FoliageSideYaw = 30f;
        static readonly float[] CloseTurntableDistances = { 1.5f, 3f };
        // Tall firs: from under the crown, looking up (FrameBelow). Metres from the trunk axis and the pitch at each.
        static readonly float[] BelowDistances = { 3f, 8f };
        static readonly float[] BelowPitches = { 40f, 30f };

        [Serializable]
        sealed class KitImage
        {
            public string prefabId, kind, file;
            public string[] tiles = Array.Empty<string>();
            public float shareMagenta, shareCyan, meanLuminance;
        }

        [Serializable]
        sealed class KitSummary
        {
            public bool passed;
            public string outputFolder, unityVersion, writtenUtc, textureCompressionOverride;
            public float seconds;
            public KitImage[] images = Array.Empty<KitImage>();
            public string[] skipped = Array.Empty<string>();
            public string[] problems = Array.Empty<string>();
        }

        [MenuItem("Gamex/Forest/6 Capture Kit")]
        public static void CaptureKit()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            string outputFolder = Path.Combine(ForestArgs.Value("-captureOut", ForestPaths.DefaultCaptureFolder), "kit");

            var built = new List<string>();
            var skipped = new List<string>();
            foreach (string prefabId in AllPrefabIds())
            {
                if (ForestPrefabs.Exists(prefabId)) built.Add(prefabId);
                else skipped.Add(prefabId);
            }
            if (built.Count == 0)
                throw new InvalidOperationException("No forest prefab has been built, so there is no kit to capture. Run Gamex/Forest/2 Build Kit first.");

            using (ForestSceneSession session = ForestSceneSession.OpenEmpty())
            {
                if (session == null)
                    return;   // cancelled in the save dialog

                Directory.CreateDirectory(outputFolder);
                var temporary = new List<Object>();
                try
                {
                    Camera camera = BuildKitStage(temporary);
                    var images = new List<KitImage>();
                    foreach (string prefabId in built)
                        CapturePrefab(prefabId, camera, outputFolder, images);

                    var problems = new List<string>();
                    foreach (KitImage image in images)
                        if (image.shareMagenta + image.shareCyan > 0f)
                            problems.Add($"{image.file}: {(image.shareMagenta + image.shareCyan) * 100f:0.000} % near-magenta or near-cyan pixels");

                    var summary = new KitSummary
                    {
                        passed = problems.Count == 0, outputFolder = Path.GetFullPath(outputFolder), unityVersion = Application.unityVersion,
                        writtenUtc = UtcNow(), textureCompressionOverride = EditorUserBuildSettings.overrideTextureCompression.ToString(),
                        seconds = (float)watch.Elapsed.TotalSeconds, images = images.ToArray(), skipped = skipped.ToArray(), problems = problems.ToArray(),
                    };
                    File.WriteAllText(Path.Combine(outputFolder, "kit-summary.json"), JsonUtility.ToJson(summary, true));

                    string line = $"Forest kit capture: {images.Count} sheet(s) of {built.Count} prefab(s) written to {summary.outputFolder} in {summary.seconds:0} s; {skipped.Count} prefab(s) not built yet.";
                    if (!summary.passed)
                        throw new InvalidOperationException($"{line} {problems.Count} problem(s):\n  " + string.Join("\n  ", problems));
                    Debug.Log(line);
                }
                finally
                {
                    foreach (Object item in temporary)
                        if (item != null)
                            Object.DestroyImmediate(item);
                }
            }
        }

        // The inspect profile (the inspect preset's sky, no fog, fixed exposure, neutral grading), its key light and a
        // grey floor at pivot height, which shows how a prop sits on flat ground before any sink is applied.
        static Camera BuildKitStage(List<Object> temporary)
        {
            var volume = new GameObject("Kit Inspect Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = ForestLighting.InspectProfile();
            temporary.Add(volume.gameObject);

            ForestLightingPresets.Preset preset = ForestLightingPresets.Get(ForestLightingPresets.Inspect);
            var key = new GameObject("Kit Key Light");
            temporary.Add(key);
            key.AddComponent<HDAdditionalLightData>();
            var light = key.GetComponent<Light>();
            light.type = LightType.Directional;
            light.lightUnit = LightUnit.Lux;
            light.intensity = preset.keyLux;
            light.useColorTemperature = true;
            light.colorTemperature = preset.keyTemperature;
            light.color = Color.white;
            light.shadows = LightShadows.Soft;
            key.transform.rotation = Quaternion.Euler(preset.keyElevation, preset.keyBearing, 0f);

            var floorMaterial = new Material(Shader.Find("HDRP/Lit")) { hideFlags = HideFlags.HideAndDontSave };
            floorMaterial.SetColor("_BaseColor", new Color(0.2f, 0.2f, 0.2f));
            floorMaterial.SetFloat("_Smoothness", 0.1f);
            HDMaterial.ValidateMaterial(floorMaterial);
            temporary.Add(floorMaterial);

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Kit Floor";
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            floor.transform.localScale = new Vector3(40f, 1f, 40f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            temporary.Add(floor);

            Camera camera = CreateCaptureCamera(null);
            temporary.Add(camera.gameObject);
            return camera;
        }

        static void CapturePrefab(string prefabId, Camera camera, string outputFolder, List<KitImage> images)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(ForestPrefabs.Load(prefabId));
            GameObject source = null;
            try
            {
                var group = instance.GetComponent<LODGroup>();
                int levels = group != null ? group.lodCount : 1;
                Bounds bounds = LevelBounds(instance, group, 0);

                // Turntable: the nearest level from four sides.
                if (group != null)
                    group.ForceLOD(0);
                var tiles = new List<Color32[]>();
                var labels = new List<string>();
                foreach (float yaw in TurntableYaws)
                {
                    FrameWhole(camera, bounds, yaw, TurntableFieldOfView);
                    tiles.Add(RenderPixels(camera, TurntableTile, TurntableTile, TurntableFrames));
                    labels.Add($"yaw {yaw:0}");
                }
                SaveSheet(prefabId, "turntable", tiles, labels, TurntableTile, 2, outputFolder, images);

                if (levels < 2 && !IsTree(prefabId))
                    return;

                // The scan a tree was thinned from is the reference every level is compared with.
                if (IsTree(prefabId) && ForestKitSpec.ScanLodDistance(prefabId) <= 0f)
                {
                    source = BuildSourceObject(prefabId);
                    // The scan stands at the origin unrotated, so its mesh bounds are its world bounds; an inactive renderer has none.
                    if (source != null)
                        bounds.Encapsulate(source.GetComponent<MeshFilter>().sharedMesh.bounds);
                }

                // Every level side by side at one framing.
                FrameWhole(camera, bounds, 30f, TurntableFieldOfView);
                SaveLevelSheet(prefabId, "lods", camera, instance, group, source, levels, SheetTile, outputFolder, images);

                if (!IsTree(prefabId))
                    return;

                // And as the player meets them: at set distances, at the pixel density of a 1440p frame. Also at each
                // distance where one level hands over to the next, which is where a pop would show.
                var distances = new List<float>(TreeDistances);
                float[] switches = ForestKitSpec.LodDistances(prefabId);
                for (int i = 0; i + 1 < switches.Length; i++)
                    if (!distances.Contains(switches[i])) distances.Add(switches[i]);
                distances.Sort();
                foreach (float distance in distances)
                {
                    FrameAtDistance(camera, bounds, distance, DistanceTile);
                    SaveLevelSheet(prefabId, $"lods_{distance:00}m", camera, instance, group, source, levels, DistanceTile, outputFolder, images);
                }

                // The view the thinning is judged on: foliage from 1.5 m, at eye height where there is any.
                FrameFoliage(camera, source != null ? source : instance, group, SheetTile, FoliageSideYaw, FoliageCropDistance);
                SaveLevelSheet(prefabId, "foliage", camera, instance, group, source, levels, SheetTile, outputFolder, images);

                // A close turntable of the level the player sees at arm's length: four sides, 1.5 m and 3 m from the
                // outermost foliage at eye height.
                if (group != null)
                    group.ForceLOD(0);
                var closeTiles = new List<Color32[]>();
                var closeLabels = new List<string>();
                foreach (float distance in CloseTurntableDistances)
                {
                    foreach (float yaw in TurntableYaws)
                    {
                        FrameFoliage(camera, instance, group, SheetTile, FoliageSideYaw + yaw, distance);
                        closeTiles.Add(RenderPixels(camera, SheetTile, SheetTile, SheetFrames));
                        closeLabels.Add($"LOD0, yaw {FoliageSideYaw + yaw:0}, {distance:0.0} m");
                    }
                }
                SaveSheet(prefabId, "close_turntable", closeTiles, closeLabels, SheetTile, TurntableYaws.Length, outputFolder, images);

                // A tall fir (no scan level) is met from below: its foliage starts metres above the eye. From eye height
                // under its crown, looking up, at the pixel density of the player's view: the scan beside every level.
                if (source != null)
                {
                    for (int i = 0; i < BelowDistances.Length; i++)
                    {
                        FrameBelow(camera, BelowDistances[i], BelowPitches[i], SheetTile);
                        SaveLevelSheet(prefabId, $"below_{BelowDistances[i]:00}m", camera, instance, group, source, levels, SheetTile, outputFolder, images);
                    }
                }
            }
            finally
            {
                if (source != null)
                {
                    // A scan is up to 5 million vertices; it must not outlive its sheets.
                    Mesh scan = source.GetComponent<MeshFilter>().sharedMesh;
                    Object.DestroyImmediate(source);
                    Object.DestroyImmediate(scan);
                }
                Object.DestroyImmediate(instance);
            }
        }

        static void SaveLevelSheet(string prefabId, string kind, Camera camera, GameObject instance, LODGroup group, GameObject source,
            int levels, int tile, string outputFolder, List<KitImage> images)
        {
            var tiles = new List<Color32[]>();
            var labels = new List<string>();
            if (source != null)
            {
                instance.SetActive(false);
                source.SetActive(true);
                tiles.Add(RenderPixels(camera, tile, tile, SheetFrames));
                labels.Add("source scan");
                source.SetActive(false);
                instance.SetActive(true);
            }
            for (int level = 0; level < levels; level++)
            {
                if (group != null)
                    group.ForceLOD(level);
                tiles.Add(RenderPixels(camera, tile, tile, SheetFrames));
                labels.Add($"LOD{level}");
            }
            if (group != null)
                group.ForceLOD(0);
            SaveSheet(prefabId, kind, tiles, labels, tile, tiles.Count, outputFolder, images);
        }

        static void SaveSheet(string prefabId, string kind, List<Color32[]> tiles, List<string> labels, int tile, int columns,
            string outputFolder, List<KitImage> images)
        {
            int rows = (tiles.Count + columns - 1) / columns;
            Color32[] sheet = Tile(tiles, tile, tile, columns, rows);
            string file = $"{prefabId}__{kind}.png";
            WritePng(Path.Combine(outputFolder, file), sheet, tile * columns, tile * rows);

            ForestImageStats.Stats stats = ForestImageStats.Compute(sheet);
            images.Add(new KitImage
            {
                prefabId = prefabId, kind = kind, file = file, tiles = labels.ToArray(),
                shareMagenta = stats.shareMagenta, shareCyan = stats.shareCyan, meanLuminance = stats.meanLuminance,
            });
        }

        // The whole object in frame, seen from slightly above.
        static void FrameWhole(Camera camera, Bounds bounds, float yaw, float fieldOfView)
        {
            float radius = Mathf.Max(bounds.extents.magnitude, 0.05f);
            float distance = radius / Mathf.Sin(fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
            Quaternion rotation = Quaternion.Euler(12f, yaw, 0f);
            camera.orthographic = false;
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = Mathf.Max(400f, distance + radius * 2f);
            camera.transform.SetPositionAndRotation(bounds.center - rotation * Vector3.forward * distance, rotation);
        }

        // From eye height at a set distance, through a window that has the pixel density of the player's 1440p view.
        static void FrameAtDistance(Camera camera, Bounds bounds, float distance, int tile)
        {
            float halfTan = Mathf.Tan(PlayerFieldOfView * 0.5f * Mathf.Deg2Rad) * tile / ForestBudgets.FrameHeight;
            float halfHeight = distance * halfTan;
            // Aim at the middle of the object when all of it fits; otherwise keep its foot just inside the frame.
            float aim = bounds.extents.y + 0.3f <= halfHeight ? bounds.center.y : halfHeight - 0.3f;
            var eye = new Vector3(bounds.center.x + distance * Mathf.Sin(30f * Mathf.Deg2Rad), PlayerEyeHeight, bounds.center.z + distance * Mathf.Cos(30f * Mathf.Deg2Rad));
            var target = new Vector3(bounds.center.x, aim, bounds.center.z);
            camera.orthographic = false;
            camera.fieldOfView = 2f * Mathf.Atan(halfTan) * Mathf.Rad2Deg;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 400f;
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, Vector3.up));
        }

        // From eye height at a horizontal distance from the trunk axis (on the FoliageSideYaw side), pitched up toward it.
        static void FrameBelow(Camera camera, float distance, float pitchDegrees, int tile)
        {
            var side = new Vector3(Mathf.Sin(FoliageSideYaw * Mathf.Deg2Rad), 0f, Mathf.Cos(FoliageSideYaw * Mathf.Deg2Rad));
            Vector3 eye = side * distance + Vector3.up * PlayerEyeHeight;
            camera.orthographic = false;
            camera.fieldOfView = 2f * Mathf.Atan(Mathf.Tan(PlayerFieldOfView * 0.5f * Mathf.Deg2Rad) * tile / ForestBudgets.FrameHeight) * Mathf.Rad2Deg;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 400f;
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(-side, Vector3.up) * Quaternion.Euler(-pitchDegrees, 0f, 0f));
        }

        // A set distance from the outermost foliage on one side of the tree (the side at the given yaw), looking at the trunk.
        static void FrameFoliage(Camera camera, GameObject reference, LODGroup group, int tile, float yawDegrees, float cropDistance)
        {
            Vector3[] vertices = ReferenceVertices(reference, group);
            var side = new Vector3(Mathf.Sin(yawDegrees * Mathf.Deg2Rad), 0f, Mathf.Cos(yawDegrees * Mathf.Deg2Rad));
            const float sectorCosine = 0.9f;   // within about 25 degrees of the camera's side

            // Prefer the band at eye height; a tall fir has nothing there, so fall back to the fullest metre of crown.
            float bandBottom = PlayerEyeHeight - 0.45f;
            if (CountInBand(vertices, side, sectorCosine, bandBottom, bandBottom + 0.9f) < 50)
            {
                var perMetre = new Dictionary<int, int>();
                foreach (Vector3 vertex in vertices)
                {
                    if (!InSector(vertex, side, sectorCosine))
                        continue;
                    int metre = Mathf.FloorToInt(vertex.y);
                    perMetre.TryGetValue(metre, out int count);
                    perMetre[metre] = count + 1;
                }
                int best = 0, bestMetre = 1;
                foreach (KeyValuePair<int, int> pair in perMetre)
                {
                    // Ties go to the lower band, so the result does not depend on dictionary order.
                    if (pair.Value > best || (pair.Value == best && pair.Key < bestMetre))
                    {
                        best = pair.Value;
                        bestMetre = pair.Key;
                    }
                }
                bandBottom = bestMetre;
            }

            float tip = 0.2f;
            foreach (Vector3 vertex in vertices)
            {
                if (vertex.y < bandBottom || vertex.y > bandBottom + 1f || !InSector(vertex, side, sectorCosine))
                    continue;
                tip = Mathf.Max(tip, new Vector2(vertex.x, vertex.z).magnitude);
            }

            float height = bandBottom + 0.5f;
            Vector3 eye = side * (tip + cropDistance) + Vector3.up * height;
            camera.orthographic = false;
            camera.fieldOfView = 2f * Mathf.Atan(Mathf.Tan(PlayerFieldOfView * 0.5f * Mathf.Deg2Rad) * tile / ForestBudgets.FrameHeight) * Mathf.Rad2Deg;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 400f;
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(-side, Vector3.up));
        }

        static int CountInBand(Vector3[] vertices, Vector3 side, float sectorCosine, float bottom, float top)
        {
            int count = 0;
            foreach (Vector3 vertex in vertices)
                if (vertex.y >= bottom && vertex.y <= top && InSector(vertex, side, sectorCosine))
                    count++;
            return count;
        }

        static bool InSector(Vector3 vertex, Vector3 side, float sectorCosine)
        {
            var flat = new Vector3(vertex.x, 0f, vertex.z);
            float length = flat.magnitude;
            return length > 0.05f && Vector3.Dot(flat, side) / length >= sectorCosine;
        }

        // Kit meshes are stored in metres with the pivot on the ground and identity child transforms, so mesh space is world space here.
        static Vector3[] ReferenceVertices(GameObject reference, LODGroup group)
        {
            MeshFilter filter = null;
            if (group != null && reference == group.gameObject)
            {
                foreach (Renderer renderer in group.GetLODs()[0].renderers)
                    if (renderer != null && renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly)
                        filter = renderer.GetComponent<MeshFilter>();
            }
            if (filter == null)
                filter = reference.GetComponentInChildren<MeshFilter>(true);
            return filter != null && filter.sharedMesh != null ? filter.sharedMesh.vertices : Array.Empty<Vector3>();
        }

        static Bounds LevelBounds(GameObject instance, LODGroup group, int level)
        {
            if (group != null && group.lodCount > level)
            {
                bool any = false;
                var bounds = new Bounds();
                foreach (Renderer renderer in group.GetLODs()[level].renderers)
                {
                    if (renderer == null)
                        continue;
                    if (!any) bounds = renderer.bounds;
                    else bounds.Encapsulate(renderer.bounds);
                    any = true;
                }
                if (any)
                    return bounds;
            }
            return RendererBounds(instance);
        }

        static Bounds RendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(root.transform.position, Vector3.one * 0.1f);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        // The untouched scan of a tree, brought into metres the way the kit build derives its meshes, with the kit's materials.
        // Null when the model is not imported; the sheet then shows the built levels only.
        static GameObject BuildSourceObject(string prefabId)
        {
            ForestObjectEntry entry = ForestManifest.FindObjectForPrefab(prefabId);
            ForestAssetEntry owner = entry != null ? ForestManifest.FindOwner(entry) : null;
            if (owner == null || owner.files == null)
                return null;

            string modelPath = null;
            foreach (ForestAssetFile file in owner.files)
                if (file.map == "mesh")
                    modelPath = ForestManifest.AssetPath(file.relPath);
            var model = modelPath != null ? AssetDatabase.LoadAssetAtPath<GameObject>(modelPath) : null;
            if (model == null)
                return null;

            // The scan exactly as the baker reads it: the imported FBX in prefab space, or, where the FBX carries no
            // UVs (fir_sapling_medium), the same object from the model's glTF geometry. Materials as the baker resolves them.
            ForestPrefabs.Source scan = ForestPrefabs.DeriveTreeSource(owner, entry);
            ForestMeshTools.MeshData data = ForestPrefabs.GroupByMaterial(scan.data, ForestPrefabs.ResolveMaterials(scan, null), out Material[] materials);
            Mesh mesh = ForestMeshTools.ToMesh(data, prefabId + "__source", true);
            mesh.hideFlags = HideFlags.HideAndDontSave;

            var source = new GameObject(prefabId + " source scan");
            source.AddComponent<MeshFilter>().sharedMesh = mesh;
            source.AddComponent<MeshRenderer>().sharedMaterials = materials;
            source.SetActive(false);
            return source;
        }

        static IEnumerable<string> AllPrefabIds()
        {
            string[][] groups =
            {
                ForestPrefabIds.Boulders, ForestPrefabIds.Logs, ForestPrefabIds.Stumps, ForestPrefabIds.Branches,
                ForestPrefabIds.FernClumps, ForestPrefabIds.FernPatches, ForestPrefabIds.GrassPatches,
                ForestPrefabIds.YoungFirs, ForestPrefabIds.TallFirs,
            };
            foreach (string[] group in groups)
                foreach (string prefabId in group)
                    yield return prefabId;
        }

        // ------------------------------------------------------------------------------------------------
        // Pixels and files
        // ------------------------------------------------------------------------------------------------

        /// <summary>Renders and returns the pixels, rows bottom to top as Unity keeps them.</summary>
        internal static Color32[] RenderPixels(Camera camera, int width, int height, int frames)
        {
            Texture2D image = Render(camera, width, height, frames);
            try
            {
                return image.GetPixels32();
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }

        internal static void WritePng(string path, Color32[] pixels, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply(false);
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>Null when the file is absent or is not an image.</summary>
        internal static Color32[] ReadPng(string path, out int width, out int height)
        {
            width = height = 0;
            if (!File.Exists(path))
                return null;

            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path)))
                    return null;
                width = texture.width;
                height = texture.height;
                return texture.GetPixels32();
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        internal static Color32[] CentreCrop(Color32[] pixels, int width, int height, int cropWidth, int cropHeight)
        {
            int left = (width - cropWidth) / 2, bottom = (height - cropHeight) / 2;
            var crop = new Color32[cropWidth * cropHeight];
            for (int y = 0; y < cropHeight; y++)
                Array.Copy(pixels, (bottom + y) * width + left, crop, y * cropWidth, cropWidth);
            return crop;
        }

        // Tiles left to right, top to bottom. Pixel rows run bottom to top, so the first row of tiles goes last.
        static Color32[] Tile(List<Color32[]> tiles, int tileWidth, int tileHeight, int columns, int rows)
        {
            int width = tileWidth * columns;
            var sheet = new Color32[width * tileHeight * rows];
            for (int i = 0; i < tiles.Count; i++)
            {
                int left = i % columns * tileWidth;
                int bottom = (rows - 1 - i / columns) * tileHeight;
                for (int y = 0; y < tileHeight; y++)
                    Array.Copy(tiles[i], y * tileWidth, sheet, (bottom + y) * width + left, tileWidth);
            }
            return sheet;
        }

        static string UtcNow() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Opens a scene for the length of a capture or probe run. In the open Editor it asks before replacing
    /// unsaved work and puts the previous scene back afterwards; a batch run ends with its process.
    /// </summary>
    internal sealed class ForestSceneSession : IDisposable
    {
        readonly string previousPath;
        readonly string openedPath;
        readonly bool switched;
        bool modified;

        ForestSceneSession(string previousPath, string openedPath, bool switched)
        {
            this.previousPath = previousPath;
            this.openedPath = openedPath;
            this.switched = switched;
        }

        /// <summary>Null when the user cancelled the save dialog.</summary>
        public static ForestSceneSession Open(string scenePath)
        {
            Scene active = SceneManager.GetActiveScene();
            if (active.path == scenePath && !active.isDirty && SceneManager.sceneCount == 1)
                return new ForestSceneSession(scenePath, scenePath, false);
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return null;

            string previous = active.path;
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            return new ForestSceneSession(previous, scenePath, true);
        }

        /// <summary>A new empty scene, for work that needs a stage of its own. Null when the user cancelled.</summary>
        public static ForestSceneSession OpenEmpty()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return null;

            string previous = SceneManager.GetActiveScene().path;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            return new ForestSceneSession(previous, null, true);
        }

        /// <summary>The run changed the opened scene in a way that must not be kept.</summary>
        public void MarkModified() => modified = true;

        public void Dispose()
        {
            if (Application.isBatchMode)
                return;

            if (switched)
            {
                if (!string.IsNullOrEmpty(previousPath) && File.Exists(previousPath))
                    EditorSceneManager.OpenScene(previousPath, OpenSceneMode.Single);
            }
            else if (modified && !string.IsNullOrEmpty(openedPath))
            {
                // The scene was the user's own open scene: reload it so the run's changes are not left as unsaved edits.
                EditorSceneManager.OpenScene(openedPath, OpenSceneMode.Single);
            }
        }
    }
}
