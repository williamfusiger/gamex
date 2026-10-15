using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Gamex.Tests.Forest
{
    /// <summary>
    /// Shared by the forest PlayMode tests. This assembly cannot see the editor code, so it reads what it
    /// needs from the scene (Gameplay/TrailWaypoints, Gameplay/Viewpoints), from the build report the
    /// audit wrote, and from the record the M0 probe left in Library.
    /// </summary>
    static class ForestPlaySupport
    {
        public const string ScenePath = "Assets/Game/Scenes/Forest.unity";
        const string ReportPath = "Assets/Game/Scenes/ForestData/build-report.json";
        const string ProbeRecordPath = "Library/GamexForest/probe.json";
        const string DefaultOutputFolder = "Temp/Forest/playmode";

        // ---- The scene ----

        /// <summary>
        /// Loads the forest scene by path, so the tests do not depend on Build Settings. Ignores the calling
        /// test when the scene has not been built: Assert.Ignore, because Unity's command-line runner
        /// counts an inconclusive test as a failed run.
        /// </summary>
        public static IEnumerator LoadForest()
        {
#if UNITY_EDITOR
            if (!File.Exists(ScenePath))
                Assert.Ignore($"{ScenePath} has not been built yet (Gamex/Forest/3 Rebuild Forest Scene).");
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
#else
            Assert.Ignore("The forest suite loads its scene by path, which only the Editor can do.");
            yield break;
#endif
        }

        public static GameObject RequireRoot(string name)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == name)
                    return root;
            Assert.Fail($"The forest scene has no root object '{name}'.");
            return null;
        }

        public static Terrain RequireTerrain()
        {
            Transform terrain = RequireRoot("Environment").transform.Find("Terrain");
            Assert.IsNotNull(terrain, "The forest scene has no Environment/Terrain.");
            var component = terrain.GetComponent<Terrain>();
            Assert.IsNotNull(component, "Environment/Terrain has no Terrain component.");
            return component;
        }

        /// <summary>World height of the terrain surface: SampleHeight is relative to the terrain's own position.</summary>
        public static float GroundHeight(Terrain terrain, float x, float z)
        {
            return terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
        }

        /// <summary>Gameplay/Viewpoints/&lt;id&gt;: where the editor captures render from, too.</summary>
        public static Transform RequireViewpoint(string id)
        {
            Transform viewpoint = RequireRoot("Gameplay").transform.Find("Viewpoints/" + id);
            Assert.IsNotNull(viewpoint, $"The forest scene has no viewpoint Gameplay/Viewpoints/{id}.");
            return viewpoint;
        }

        /// <summary>Gameplay/TrailWaypoints in walking order (WP_00, WP_01, ...).</summary>
        public static List<Transform> RequireWaypoints()
        {
            Transform group = RequireRoot("Gameplay").transform.Find("TrailWaypoints");
            Assert.IsNotNull(group, "The forest scene has no Gameplay/TrailWaypoints.");
            var waypoints = new List<Transform>();
            foreach (Transform child in group)
                waypoints.Add(child);
            waypoints.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            Assert.GreaterOrEqual(waypoints.Count, 2, "The trail needs at least two waypoints to walk between.");
            return waypoints;
        }

        // ---- The player and its stand-in ----

        /// <summary>
        /// Switches the player off so that nothing but the test moves or renders, and returns a controller
        /// of the same build to push around instead. This tests the scene, not the input code.
        /// </summary>
        public static CharacterController SwapPlayerForProbe(Vector3 position)
        {
            GameObject player = RequireRoot("Player");
            var body = player.GetComponent<CharacterController>();
            Assert.IsNotNull(body, "The Player has no CharacterController.");
            CharacterController probe = CreateProbe(body, position);
            player.SetActive(false);
            return probe;
        }

        public static CharacterController CreateProbe(CharacterController template, Vector3 position)
        {
            var holder = new GameObject("Forest Test Probe");
            holder.transform.position = position;
            var probe = holder.AddComponent<CharacterController>();
            probe.height = template.height;
            probe.radius = template.radius;
            probe.center = template.center;
            probe.slopeLimit = template.slopeLimit;
            probe.stepOffset = template.stepOffset;
            probe.skinWidth = template.skinWidth;
            probe.minMoveDistance = template.minMoveDistance;
            return probe;
        }

        /// <summary>One frame of movement the way PlayerController.Move does it: a horizontal speed, gravity, and a pull that keeps it grounded.</summary>
        public static void Step(CharacterController probe, Vector3 horizontalVelocity, ref float verticalVelocity, float deltaTime)
        {
            if (probe.isGrounded && verticalVelocity < 0f)
                verticalVelocity = ForestPlayBudgets.GroundedPull;
            verticalVelocity += ForestPlayBudgets.Gravity * deltaTime;
            Vector3 velocity = horizontalVelocity;
            velocity.y = verticalVelocity;
            probe.Move(velocity * deltaTime);
        }

        // ---- Rendering ----

        /// <summary>
        /// A disabled camera with the player camera's lens and HDRP settings, rendered explicitly once per
        /// frame. Whether enabled cameras render by themselves in batch-mode play mode is not known, and
        /// this way exactly one camera renders. The player is switched off.
        /// </summary>
        public static Camera CreateMeasuredCamera()
        {
            GameObject player = RequireRoot("Player");
            Camera playerCamera = player.GetComponentInChildren<Camera>(true);
            Assert.IsNotNull(playerCamera, "The Player has no camera.");

            var holder = new GameObject("Forest Test Camera");
            var camera = holder.AddComponent<Camera>();
            camera.CopyFrom(playerCamera);
            holder.tag = "Untagged";
            camera.enabled = false;
            camera.targetTexture = null;
            // CopyFrom also copies the player camera's aspect ratio as a fixed value; the frame's shape must come from its target.
            camera.ResetAspect();
            var data = holder.GetComponent<HDAdditionalCameraData>();
            if (data == null)
                data = holder.AddComponent<HDAdditionalCameraData>();
            var playerData = playerCamera.GetComponent<HDAdditionalCameraData>();
            Assert.IsNotNull(playerData, "The player camera has no HDAdditionalCameraData.");
            playerData.CopyTo(data);

            player.SetActive(false);
            return camera;
        }

        public static RenderTexture CreateTarget(int width, int height)
        {
            return new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        }

        /// <summary>Renders <paramref name="frames"/> frames from a viewpoint, one per engine frame, into the camera's target.</summary>
        public static IEnumerator RenderFrames(Camera camera, Transform viewpoint, int frames)
        {
            camera.transform.SetPositionAndRotation(viewpoint.position, viewpoint.rotation);
            camera.Render();
            // A camera cut: the history of the previous viewpoint must not leak into this one.
            if (RenderPipelineManager.currentPipeline is HDRenderPipeline)
                HDCamera.GetOrCreate(camera).Reset();
            for (int i = 0; i < frames; i++)
            {
                camera.Render();
                yield return null;
            }
        }

        public static Color32[] ReadPixels(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                return image.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.Destroy(image);
            }
        }

        /// <summary>What ForestImageStats measures in the editor assembly, for the frames this suite judges.</summary>
        public struct FrameStats { public float meanLuminance, shareBlack, shareWhite, shareMagenta, shareCyan; }

        public static FrameStats Measure(Color32[] pixels)
        {
            double luminanceSum = 0.0;
            long black = 0, white = 0, magenta = 0, cyan = 0;
            foreach (Color32 pixel in pixels)
            {
                // Rec. 709 on the encoded values: how bright the picture looks.
                float luminance = 0.2126f * pixel.r + 0.7152f * pixel.g + 0.0722f * pixel.b;
                luminanceSum += luminance;
                if (luminance < 2f) black++;
                else if (luminance > 253f) white++;
                // HDRP's error colour, and the Editor's colour for a shader that is still compiling.
                if (pixel.r > 230 && pixel.g < 60 && pixel.b > 230) magenta++;
                else if (pixel.r < 60 && pixel.g > 230 && pixel.b > 230) cyan++;
            }
            double count = Math.Max(1, pixels.Length);
            return new FrameStats
            {
                meanLuminance = (float)(luminanceSum / count / 255.0),
                shareBlack = (float)(black / count), shareWhite = (float)(white / count),
                shareMagenta = (float)(magenta / count), shareCyan = (float)(cyan / count),
            };
        }

        // ---- Files ----

        /// <summary>Where stills and measurements go: -captureOut DIR/playmode, or Temp/Forest/playmode (which Unity clears when the Editor quits).</summary>
        public static string OutputFolder()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, "-captureOut");
            string folder = index >= 0 && index + 1 < arguments.Length ? Path.Combine(arguments[index + 1], "playmode") : DefaultOutputFolder;
            Directory.CreateDirectory(folder);
            return folder;
        }

        [Serializable]
        public sealed class Polyline { public string name; public Vector2[] points; }

        [Serializable]
        sealed class BuildReport { public Polyline[] corridors; }

        /// <summary>
        /// The trail centre lines, sampled every metre by the audit with the layout's own spline code and
        /// stored in the build report. Rebuilding them here from the patrol nodes would need a second copy
        /// of that spline code, and a copy that curved slightly differently would sweep the wrong lane.
        /// </summary>
        public static Polyline[] RequireCorridors()
        {
            Assert.IsTrue(File.Exists(ReportPath), $"{ReportPath} is missing: the scene was not audited. Rebuild the forest scene.");
            BuildReport report = JsonUtility.FromJson<BuildReport>(File.ReadAllText(ReportPath));
            Assert.IsTrue(report != null && report.corridors != null && report.corridors.Length == 3,
                $"{ReportPath} does not hold the three sampled trail centre lines. Rebuild the forest scene.");
            foreach (Polyline line in report.corridors)
                Assert.IsTrue(line.points != null && line.points.Length >= 2, $"Centre line '{line.name}' in {ReportPath} has no points.");
            return report.corridors;
        }

        [Serializable]
        sealed class ProbeRecord { public string unityVersion, frameTiming; }

        /// <summary>
        /// What probe P5 found about FrameTimingManager: "yes", "no", or null when the probe has not been run
        /// with this Unity version, in which case no timing method has been validated yet.
        /// </summary>
        public static string ValidatedFrameTiming()
        {
            if (!File.Exists(ProbeRecordPath))
                return null;
            ProbeRecord record = JsonUtility.FromJson<ProbeRecord>(File.ReadAllText(ProbeRecordPath));
            if (record == null || record.unityVersion != Application.unityVersion)
                return null;
            return record.frameTiming == "yes" || record.frameTiming == "no" ? record.frameTiming : null;
        }

        public static float Median(List<double> values) => Percentile(values, 0.5);

        public static float Percentile(List<double> values, double share)
        {
            if (values.Count == 0)
                return 0f;
            var sorted = new List<double>(values);
            sorted.Sort();
            return (float)sorted[Mathf.Clamp(Mathf.CeilToInt((float)(share * sorted.Count)) - 1, 0, sorted.Count - 1)];
        }
    }
}
