using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Gamex.Tests.Forest
{
    /// <summary>
    /// Plays the forest scene and checks what only play mode can show: that the player stands and walks
    /// on it, that the edge holds, that a rendered frame is neither broken nor badly exposed, and what a
    /// frame costs. Every test loads the scene afresh and is ignored until the scene has been built.
    /// </summary>
    [Category("Forest")]
    public class ForestPlayModeTests
    {
        // The three frames the render tests judge and save for review.
        static readonly string[] RenderViews = { "trail_start", "clearing", "ground_close" };
        // N1: the night is judged where the player stands: on the trail, at the glade, and where the torch meets foliage at arm's
        // length (crouched in a thicket, a young fir at 3.5 m). Not ground_close: its camera at 0.4 m puts the hand-held torch a
        // hand's breadth above the litter, a pose the player cannot take.
        static readonly string[] NightViews = { "trail_start", "clearing", "crouch_hide", "fir_close" };
        // N1: what the player sees at night, saved for review with the torch on and off.
        static readonly string[] NightStillViews = { "trail_start", "trail_mid_fwd", "clearing", "ground_close", "crouch_hide", "long_sightline", "hollow_rim", "deadfall" };
        // Where the frame is most expensive in different ways: the first view, inside a thicket, open sky, the longest view.
        static readonly string[] BudgetViews = { "trail_start", "crouch_hide", "clearing", "long_sightline" };

        float savedCaptureDeltaTime, savedTimeScale;
        int savedVSync, savedTargetFrameRate;
        bool settingsSaved;

        [UnitySetUp]
        public IEnumerator LoadForest()
        {
            savedCaptureDeltaTime = Time.captureDeltaTime;
            savedTimeScale = Time.timeScale;
            savedVSync = QualitySettings.vSyncCount;
            savedTargetFrameRate = Application.targetFrameRate;
            settingsSaved = true;

            yield return ForestPlaySupport.LoadForest();
            // A fixed step: the result must not depend on how fast this machine renders a frame.
            Time.captureDeltaTime = 1f / 60f;
        }

        [TearDown]
        public void RestoreSettings()
        {
            // Play mode is entered without a domain reload in this project, so nothing may be left changed.
            // Restoring values that were never saved would set the time scale to 0.
            if (!settingsSaved)
                return;
            settingsSaved = false;
            Time.captureDeltaTime = savedCaptureDeltaTime;
            Time.timeScale = savedTimeScale;
            QualitySettings.vSyncCount = savedVSync;
            Application.targetFrameRate = savedTargetFrameRate;
        }

        // ---- Standing and walking ----

        [UnityTest]
        public IEnumerator Player_SpawnsOnTheGround()
        {
            GameObject player = ForestPlaySupport.RequireRoot("Player");
            var body = player.GetComponent<CharacterController>();
            Assert.IsNotNull(body, "The Player has no CharacterController.");
            Terrain terrain = ForestPlaySupport.RequireTerrain();

            for (int i = 0; i < 30; i++)
                yield return null;

            Vector3 position = player.transform.position;
            float ground = ForestPlaySupport.GroundHeight(terrain, position.x, position.z);
            Assert.IsTrue(body.isGrounded, $"After 30 frames the player is not grounded (y {position.y:0.000}, terrain {ground:0.000}).");
            // A CharacterController rests one skin width above what it stands on.
            float rest = position.y - body.skinWidth;
            Assert.AreEqual(ground, rest, ForestPlayBudgets.SpawnHeightTolerance,
                $"The player's feet are at {position.y:0.000} (skin {body.skinWidth:0.00}), the terrain is at {ground:0.000}.");
            Assert.Less(ForestPlayBudgets.SignedDistanceToBoundary(new Vector2(position.x, position.z)), 0f, "The player is outside the playable area.");
        }

        sealed class WalkResult { public int frames, airborneFrames; public float seconds; }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Trail_IsWalkable()
        {
            var result = new WalkResult();
            yield return WalkTheTrail(ForestPlayBudgets.WalkSpeed, result);
            Assert.Greater(result.frames, 0);
            Debug.Log($"Forest trail walked at {ForestPlayBudgets.WalkSpeed} m/s in {result.seconds:0.0} s of game time; {result.airborneFrames} of {result.frames} frames off the ground.");
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Trail_IsSprintable()
        {
            var result = new WalkResult();
            yield return WalkTheTrail(ForestPlayBudgets.SprintSpeed, result);
            float airborne = result.airborneFrames / (float)Mathf.Max(1, result.frames);
            Debug.Log($"Forest trail sprinted at {ForestPlayBudgets.SprintSpeed} m/s in {result.seconds:0.0} s of game time; {airborne * 100f:0.00} % of the frames off the ground.");
            Assert.Less(airborne, ForestPlayBudgets.AirborneShareLimit,
                $"A sprinting player is off the ground on {airborne * 100f:0.0} % of the frames: somewhere the trail falls away faster than the controller is pulled down.");
        }

        // Moves a stand-in of the player from waypoint to waypoint to the north end of the trail.
        static IEnumerator WalkTheTrail(float speed, WalkResult result)
        {
            Terrain terrain = ForestPlaySupport.RequireTerrain();
            List<Transform> waypoints = ForestPlaySupport.RequireWaypoints();
            CharacterController probe = ForestPlaySupport.SwapPlayerForProbe(waypoints[0].position + Vector3.up * 0.1f);

            float pathLength = 0f;
            for (int i = 1; i < waypoints.Count; i++)
                pathLength += Vector3.Distance(waypoints[i - 1].position, waypoints[i].position);
            float timeLimit = pathLength / speed * 3f + 10f;

            int target = 1;
            float vertical = 0f, stalledFor = 0f;
            Vector3 lastProgress = probe.transform.position;
            // Let it settle on the ground before the frames are counted.
            for (int i = 0; i < 20; i++)
            {
                ForestPlaySupport.Step(probe, Vector3.zero, ref vertical, Time.deltaTime);
                yield return null;
            }

            while (target < waypoints.Count)
            {
                float deltaTime = Time.deltaTime;
                Vector3 toTarget = waypoints[target].position - probe.transform.position;
                toTarget.y = 0f;
                if (toTarget.magnitude < 0.6f)
                {
                    target++;
                    continue;
                }

                ForestPlaySupport.Step(probe, toTarget.normalized * speed, ref vertical, deltaTime);
                result.frames++;
                result.seconds += deltaTime;
                if (!probe.isGrounded)
                    result.airborneFrames++;

                Vector3 position = probe.transform.position;
                float ground = ForestPlaySupport.GroundHeight(terrain, position.x, position.z);
                if (position.y < ground - 0.05f)
                    Assert.Fail($"The probe dropped below the terrain at ({position.x:0.0}, {position.z:0.0}): y {position.y:0.00}, terrain {ground:0.00}.");

                // Less than half a metre of ground covered in two seconds is a stall.
                Vector3 covered = position - lastProgress;
                covered.y = 0f;
                if (covered.magnitude > 0.5f)
                {
                    lastProgress = position;
                    stalledFor = 0f;
                }
                else if ((stalledFor += deltaTime) > 2f)
                    Assert.Fail($"The probe stalled for 2 s at ({position.x:0.0}, {position.z:0.0}) on its way to {waypoints[target].name}.");

                if (result.seconds > timeLimit)
                    Assert.Fail($"The probe did not reach the north end within {timeLimit:0} s of game time; it got to {waypoints[target - 1].name}.");
                yield return null;
            }
            Object.Destroy(probe.gameObject);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Bank_CannotBeClimbed()
        {
            Terrain terrain = ForestPlaySupport.RequireTerrain();
            GameObject player = ForestPlaySupport.RequireRoot("Player");
            var body = player.GetComponent<CharacterController>();
            Assert.IsNotNull(body, "The Player has no CharacterController.");

            // Sixteen stand-ins, each ten metres inside the boundary, each pushed straight outward at sprint speed.
            const int bearings = 16;
            var probes = new CharacterController[bearings];
            var directions = new Vector3[bearings];
            var verticals = new float[bearings];
            for (int i = 0; i < bearings; i++)
            {
                float angle = i * (360f / bearings) * Mathf.Deg2Rad;
                var direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                float distance = 0f;
                while (ForestPlayBudgets.SignedDistanceToBoundary(direction * (distance + 0.25f)) < -10f)
                    distance += 0.25f;
                Vector2 start = direction * distance;
                directions[i] = new Vector3(direction.x, 0f, direction.y);
                probes[i] = ForestPlaySupport.CreateProbe(body, new Vector3(start.x, ForestPlaySupport.GroundHeight(terrain, start.x, start.y) + 0.1f, start.y));
            }
            player.SetActive(false);

            float elapsed = 0f;
            while (elapsed < ForestPlayBudgets.EscapeSeconds)
            {
                float deltaTime = Time.deltaTime;
                for (int i = 0; i < bearings; i++)
                    ForestPlaySupport.Step(probes[i], directions[i] * ForestPlayBudgets.SprintSpeed, ref verticals[i], deltaTime);
                elapsed += deltaTime;
                yield return null;
            }

            var escaped = new List<string>();
            float furthest = float.NegativeInfinity;
            for (int i = 0; i < bearings; i++)
            {
                Vector3 position = probes[i].transform.position;
                furthest = Mathf.Max(furthest, ForestPlayBudgets.SignedDistanceToBoundary(new Vector2(position.x, position.z)));
                if (Mathf.Abs(position.x) > ForestPlayBudgets.EscapeLimitX || Mathf.Abs(position.z) > ForestPlayBudgets.EscapeLimitZ)
                    escaped.Add($"bearing {i * (360f / bearings):0.0}: ended at ({position.x:0.0}, {position.y:0.0}, {position.z:0.0})");
                Object.Destroy(probes[i].gameObject);
            }
            Debug.Log($"Forest boundary: after {ForestPlayBudgets.EscapeSeconds:0} s of pushing outward the furthest probe is {furthest:0.00} m outside the boundary line.");
            Assert.IsEmpty(escaped, "A player-sized body left the playable area:\n  " + string.Join("\n  ", escaped));
        }

        [UnityTest]
        public IEnumerator Corridors_ArePhysicallyFree()
        {
            ForestPlaySupport.Polyline[] lines = ForestPlaySupport.RequireCorridors();
            Terrain terrain = ForestPlaySupport.RequireTerrain();
            // The player stands on the trail and is not part of the scenery.
            ForestPlaySupport.RequireRoot("Player").SetActive(false);
            yield return new WaitForFixedUpdate();
            Physics.SyncTransforms();

            float radius = ForestPlayBudgets.CorridorAgentRadius;
            float height = Mathf.Max(ForestPlayBudgets.CorridorCapsuleHeightMetres, 2f * radius);
            var touched = new List<string>();
            var seen = new HashSet<Collider>();
            int samples = 0;
            foreach (ForestPlaySupport.Polyline line in lines)
            {
                foreach (Vector2 point in line.points)
                {
                    samples++;
                    float feet = ForestPlaySupport.GroundHeight(terrain, point.x, point.y) + ForestPlayBudgets.CorridorCapsuleLiftMetres;
                    var bottom = new Vector3(point.x, feet + radius, point.y);
                    var top = new Vector3(point.x, feet + height - radius, point.y);
                    foreach (Collider hit in Physics.OverlapCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore))
                    {
                        // The terrain is the floor of the lane.
                        if (hit is TerrainCollider || !seen.Add(hit))
                            continue;
                        touched.Add($"{hit.name} (under {(hit.transform.parent != null ? hit.transform.parent.name : "the scene root")}) on {line.name} near ({point.x:0.0}, {point.y:0.0})");
                    }
                }
            }
            Assert.Greater(samples, 300, "The build report holds too few centre-line samples to cover the trails.");
            Assert.IsEmpty(touched, $"A capsule of radius {radius} m swept along the trails touches {touched.Count} collider(s); a large agent cannot pass:\n  " + string.Join("\n  ", touched));
        }

        // ---- The torch (N1) ----

        [UnityTest]
        public IEnumerator Flashlight_StartsOnAndToggles()
        {
            GameObject player = ForestPlaySupport.RequireRoot("Player");
            PlayerFlashlight torch = player.GetComponentInChildren<PlayerFlashlight>(true);
            Assert.IsNotNull(torch, "The forest's player carries no torch.");
            yield return null;

            Assert.IsTrue(torch.IsOn, "The torch must start switched on.");
            Assert.IsTrue(torch.Light.isActiveAndEnabled, "The torch is on but its light is not.");
            var events = new List<bool>();
            torch.IsOnChanged += events.Add;
            torch.Toggle();
            yield return null;
            Assert.IsFalse(torch.IsOn);
            Assert.IsFalse(torch.Light.enabled, "The torch is off but its light is not.");
            torch.Toggle();
            yield return null;
            Assert.IsTrue(torch.IsOn);
            Assert.IsTrue(torch.Light.enabled);
            CollectionAssert.AreEqual(new[] { false, true }, events, "IsOnChanged must report every switch, once.");

            // The binding, read by reflection (this assembly does not reference the Input System): the reader's Flashlight action,
            // F on the keyboard and a button on the gamepad, enabled, and not pressed by itself.
            var reader = player.GetComponent<PlayerInputReader>();
            object action = typeof(PlayerInputReader).GetField("flashlight", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(reader);
            Assert.IsNotNull(action, "PlayerInputReader has no 'flashlight' action.");
            Assert.AreEqual("Flashlight", action.GetType().GetProperty("name").GetValue(action));
            Assert.IsTrue((bool)action.GetType().GetProperty("enabled").GetValue(action), "The Flashlight action is not enabled.");
            var paths = new List<string>();
            foreach (object binding in (IEnumerable)action.GetType().GetProperty("bindings").GetValue(action))
                paths.Add((string)binding.GetType().GetProperty("path").GetValue(binding));
            CollectionAssert.Contains(paths, "<Keyboard>/f", "The torch must be on F.");
            Assert.IsTrue(paths.Exists(path => path.StartsWith("<Gamepad>/", StringComparison.Ordinal)), "The torch has no gamepad button: " + string.Join(", ", paths));
            Assert.IsFalse(reader.FlashlightPressed);
            Debug.Log("Forest torch bindings: " + string.Join(", ", paths));
        }

        [UnityTest]
        public IEnumerator Flashlight_LagsBehindTheView()
        {
            GameObject player = ForestPlaySupport.RequireRoot("Player");
            var controller = player.GetComponent<PlayerController>();
            PlayerFlashlight torch = player.GetComponentInChildren<PlayerFlashlight>(true);
            Assert.IsNotNull(torch, "The forest's player carries no torch.");
            Transform pivot = torch.transform.parent;
            // The test turns and moves the body itself; locked controls leave the view to it.
            controller.ControlsLocked = true;
            for (int i = 0; i < 10; i++)
                yield return null;

            Quaternion aim = Quaternion.Inverse(pivot.rotation) * torch.transform.rotation;
            float Behind() => Quaternion.Angle(torch.transform.rotation, pivot.rotation * aim);
            Assert.Less(Vector3.Angle(torch.transform.forward, pivot.forward), 5f, "At rest the beam must point where the player looks.");

            // A sudden turn of 30 degrees: the beam trails, at most by its limit, and catches up within half a second.
            controller.Teleport(player.transform.position, player.transform.eulerAngles.y + 30f);
            yield return null;
            float first = Behind();
            Assert.Greater(first, 1f, "The beam does not lag behind a turn.");
            Assert.LessOrEqual(first, torch.MaxLagDegrees + 0.01f, "The beam trails the view by more than its limit.");
            for (int i = 0; i < 30; i++)
                yield return null;
            Assert.Less(Behind(), 0.1f, "The beam does not catch up with the view.");

            // A jump of the view (a teleport, a cut) is not a turn: the beam must not swing after it.
            controller.Teleport(player.transform.position + new Vector3(8f, 0f, 0f), player.transform.eulerAngles.y - 90f);
            yield return null;
            Assert.Less(Behind(), 0.05f, "The beam swings after a teleport.");
            Debug.Log($"Forest torch lag: {first:0.0} degrees one frame after a 30 degree turn (limit {torch.MaxLagDegrees}, time constant {torch.LagSeconds} s).");
        }

        // ---- Rendering ----

        [UnityTest]
        public IEnumerator Render_HasNoErrorPixels()
        {
            // N1: as the game starts (the night, the torch on), and under the overcast day.
            var problems = new List<string>();
            Action<string, ForestPlaySupport.FrameStats> judge = (view, stats) =>
            {
                if (stats.shareMagenta >= ForestPlayBudgets.ErrorPixelShareLimit)
                    problems.Add($"{view}: {stats.shareMagenta * 100f:0.000} % near-magenta pixels: a broken material or a missing diffusion profile");
                if (stats.shareCyan >= ForestPlayBudgets.ErrorPixelShareLimit)
                    problems.Add($"{view}: {stats.shareCyan * 100f:0.000} % near-cyan pixels: a shader that was not compiled");
            };
            yield return RenderViewsAndJudge(ForestPlayBudgets.RenderWidth, ForestPlayBudgets.RenderHeight, ForestPlayBudgets.RenderFrames, null,
                (view, stats) => judge(view + " (night, torch on)", stats), withFlashlight: true);
            ForestPlaySupport.UsePreset("OvercastMist");
            yield return RenderViewsAndJudge(ForestPlayBudgets.RenderWidth, ForestPlayBudgets.RenderHeight, ForestPlayBudgets.RenderFrames, null,
                (view, stats) => judge(view + " (overcast)", stats));
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [UnityTest]
        public IEnumerator Render_ExposureIsSane()
        {
            // The window below is the overcast day's (N1: no longer the scene's default preset, so it is switched to). The night has
            // its own test, Render_NightExposure.
            ForestPlaySupport.UsePreset("OvercastMist");
            var problems = new List<string>();
            yield return RenderViewsAndJudge(ForestPlayBudgets.RenderWidth, ForestPlayBudgets.RenderHeight, ForestPlayBudgets.RenderFrames, null, (view, stats) =>
            {
                Debug.Log($"Forest exposure at {view}: mean luminance {stats.meanLuminance:0.000}, {stats.shareWhite * 100f:0.0} % clipped, {stats.shareBlack * 100f:0.0} % crushed.");
                if (stats.meanLuminance < ForestPlayBudgets.OvercastLuminanceMin || stats.meanLuminance > ForestPlayBudgets.OvercastLuminanceMax)
                    problems.Add($"{view}: mean luminance {stats.meanLuminance:0.000} is outside {ForestPlayBudgets.OvercastLuminanceMin:0.00} to {ForestPlayBudgets.OvercastLuminanceMax:0.00}");
                if (stats.shareWhite >= ForestPlayBudgets.ClippedWhiteShareLimit)
                    problems.Add($"{view}: {stats.shareWhite * 100f:0.0} % of the frame is clipped to white (limit {ForestPlayBudgets.ClippedWhiteShareLimit * 100f:0} %)");
                if (stats.shareBlack >= ForestPlayBudgets.CrushedBlackShareLimit)
                    problems.Add($"{view}: {stats.shareBlack * 100f:0.0} % of the frame is crushed to black (limit {ForestPlayBudgets.CrushedBlackShareLimit * 100f:0} %)");
            });
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator Render_ReferenceStills()
        {
            // These, not the edit-mode captures, show what the player sees: time runs, exposure adapts and the torch lags.
            // N1: the night with the torch on (<view>.png, as the game starts), the same views without it (<view>__torch_off.png),
            // one still in the middle of a quick turn (trail_start__turning.png), and the overcast day (<view>__overcast.png).
            string folder = ForestPlaySupport.OutputFolder();
            var written = new List<string>();
            Action<string, ForestPlaySupport.FrameStats> record = (file, stats) =>
            {
                written.Add(Path.Combine(folder, file + ".png"));
                Debug.Log($"Forest still {file}: mean luminance {stats.meanLuminance:0.000}, {stats.shareWhite * 100f:0.00} % clipped, {stats.shareBlack * 100f:0.00} % crushed.");
            };
            yield return RenderViewsAndJudge(ForestPlayBudgets.FrameWidth, ForestPlayBudgets.FrameHeight, ForestPlayBudgets.StillFrames, folder, record,
                withFlashlight: true, views: NightStillViews);
            yield return RenderViewsAndJudge(ForestPlayBudgets.FrameWidth, ForestPlayBudgets.FrameHeight, ForestPlayBudgets.StillFrames, folder, record,
                withFlashlight: true, views: NightStillViews, torchOn: false, fileSuffix: "__torch_off");
            yield return RenderTurningStill(folder, record);
            ForestPlaySupport.UsePreset("OvercastMist");
            yield return RenderViewsAndJudge(ForestPlayBudgets.FrameWidth, ForestPlayBudgets.FrameHeight, ForestPlayBudgets.StillFrames, folder, record,
                fileSuffix: "__overcast");

            Assert.AreEqual(2 * NightStillViews.Length + 1 + RenderViews.Length, written.Count);
            foreach (string path in written)
                Assert.IsTrue(File.Exists(path) && new FileInfo(path).Length > 0, $"The still {path} was not written.");
            Debug.Log($"Forest play-mode stills written to {Path.GetFullPath(folder)}: {written.Count} files.");
        }

        // trail_start while the view turns right at 180 degrees a second: the beam trails the view by its lag.
        static IEnumerator RenderTurningStill(string folder, Action<string, ForestPlaySupport.FrameStats> record)
        {
            int width = ForestPlayBudgets.FrameWidth, height = ForestPlayBudgets.FrameHeight;
            Camera camera = ForestPlaySupport.CreateMeasuredCamera(withFlashlight: true);
            PlayerFlashlight torch = ForestPlaySupport.Flashlight(camera);
            RenderTexture target = ForestPlaySupport.CreateTarget(width, height);
            camera.targetTexture = target;
            try
            {
                Transform view = ForestPlaySupport.RequireViewpoint("trail_start");
                yield return ForestPlaySupport.RenderFrames(camera, view, ForestPlaySupport.TurningWarmupFrames);
                for (int i = 0; i < 20; i++)
                {
                    camera.transform.rotation = Quaternion.AngleAxis(3f, Vector3.up) * camera.transform.rotation;
                    camera.Render();
                    yield return null;
                }
                camera.Render();
                Color32[] pixels = ForestPlaySupport.ReadPixels(target);
                float behind = Vector3.Angle(torch.transform.forward, camera.transform.forward);
                Debug.Log($"Forest torch while turning at 180 degrees a second: the beam's axis is {behind:0.0} degrees from the view's (limit {torch.MaxLagDegrees}).");
                SavePng(Path.Combine(folder, "trail_start__turning.png"), pixels, width, height);
                record("trail_start__turning", ForestPlaySupport.Measure(pixels));
            }
            finally
            {
                camera.targetTexture = null;
                target.Release();
                Object.Destroy(target);
                ForestPlaySupport.ReleaseMeasuredCamera(camera);
            }
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Render_NightExposure()
        {
            // N1: under the night, each view twice, without and with the torch. Without it the frame is dark but never black; with it
            // the frame stays a night frame, the torch lights a readable part of it, and near foliage in the beam is not clipped.
            ForestPlaySupport.UsePreset("Night");
            int width = ForestPlayBudgets.RenderWidth, height = ForestPlayBudgets.RenderHeight;
            Camera camera = ForestPlaySupport.CreateMeasuredCamera(withFlashlight: true);
            PlayerFlashlight torch = ForestPlaySupport.Flashlight(camera);
            RenderTexture target = ForestPlaySupport.CreateTarget(width, height);
            camera.targetTexture = target;
            var problems = new List<string>();
            try
            {
                foreach (string view in NightViews)
                {
                    Transform viewpoint = ForestPlaySupport.RequireViewpoint(view);
                    torch.SetOn(false);
                    yield return ForestPlaySupport.RenderFrames(camera, viewpoint, ForestPlayBudgets.RenderFrames);
                    Color32[] dark = ForestPlaySupport.ReadPixels(target);
                    torch.SetOn(true);
                    yield return ForestPlaySupport.RenderFrames(camera, viewpoint, ForestPlayBudgets.RenderFrames);
                    Color32[] lit = ForestPlaySupport.ReadPixels(target);

                    ForestPlaySupport.FrameStats off = ForestPlaySupport.Measure(dark), on = ForestPlaySupport.Measure(lit);
                    float[] blocksOff = ForestPlaySupport.BlockLuminance(dark, width, height, ForestPlayBudgets.LitBlockSize, out int columns, out int rows);
                    float[] blocksOn = ForestPlaySupport.BlockLuminance(lit, width, height, ForestPlayBudgets.LitBlockSize, out _, out _);
                    int litBlocks = 0;
                    double litSum = 0.0;
                    for (int i = 0; i < blocksOn.Length; i++)
                    {
                        if (blocksOn[i] - blocksOff[i] < ForestPlayBudgets.LitBlockGain)
                            continue;
                        litBlocks++;
                        litSum += blocksOn[i];
                    }
                    float litShare = litBlocks / (float)(columns * rows);
                    float litMean = litBlocks > 0 ? (float)(litSum / litBlocks) : 0f;
                    Debug.Log($"Forest night at {view}: torch off mean luminance {off.meanLuminance:0.000}, {off.shareBlack * 100f:0.0} % crushed; " +
                        $"torch on mean {on.meanLuminance:0.000}, {on.shareWhite * 100f:0.00} % clipped, lit area {litShare * 100f:0.0} % of the frame at mean {litMean:0.000}.");

                    if (off.meanLuminance < ForestPlayBudgets.NightDarkLuminanceMin || off.meanLuminance > ForestPlayBudgets.NightDarkLuminanceMax)
                        problems.Add($"{view}, torch off: mean luminance {off.meanLuminance:0.000} is outside {ForestPlayBudgets.NightDarkLuminanceMin:0.000} to {ForestPlayBudgets.NightDarkLuminanceMax:0.000}");
                    if (off.shareBlack >= ForestPlayBudgets.CrushedBlackShareLimit)
                        problems.Add($"{view}, torch off: {off.shareBlack * 100f:0.0} % of the frame is crushed to black (limit {ForestPlayBudgets.CrushedBlackShareLimit * 100f:0} %)");
                    if (on.meanLuminance < ForestPlayBudgets.NightLitLuminanceMin || on.meanLuminance > ForestPlayBudgets.NightLitLuminanceMax)
                        problems.Add($"{view}, torch on: mean luminance {on.meanLuminance:0.000} is outside {ForestPlayBudgets.NightLitLuminanceMin:0.000} to {ForestPlayBudgets.NightLitLuminanceMax:0.000}");
                    if (litShare < ForestPlayBudgets.NightLitAreaShareMin)
                        problems.Add($"{view}, torch on: the torch brightens only {litShare * 100f:0.0} % of the frame (at least {ForestPlayBudgets.NightLitAreaShareMin * 100f:0} %)");
                    else if (litMean < ForestPlayBudgets.NightLitAreaLuminanceMin)
                        problems.Add($"{view}, torch on: the lit area's mean luminance {litMean:0.000} is under {ForestPlayBudgets.NightLitAreaLuminanceMin:0.00}: not readable");
                    if (on.shareWhite > ForestPlayBudgets.NightClippedWhiteShareLimit)
                        problems.Add($"{view}, torch on: {on.shareWhite * 100f:0.00} % of the frame is clipped to white (limit {ForestPlayBudgets.NightClippedWhiteShareLimit * 100f:0.0} %)");
                }
            }
            finally
            {
                camera.targetTexture = null;
                target.Release();
                Object.Destroy(target);
                ForestPlaySupport.ReleaseMeasuredCamera(camera);
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        // Renders each view with a camera that copies the player's, and hands the frame's statistics to the caller. N1: with the
        // player's torch on that camera when asked (on or off), other views than the three render views, and a suffix for the files.
        static IEnumerator RenderViewsAndJudge(int width, int height, int frames, string saveFolder, Action<string, ForestPlaySupport.FrameStats> judge,
            bool withFlashlight = false, string[] views = null, bool torchOn = true, string fileSuffix = "")
        {
            Camera camera = ForestPlaySupport.CreateMeasuredCamera(withFlashlight);
            if (withFlashlight)
                ForestPlaySupport.Flashlight(camera).SetOn(torchOn);
            RenderTexture target = ForestPlaySupport.CreateTarget(width, height);
            camera.targetTexture = target;
            try
            {
                foreach (string view in views ?? RenderViews)
                {
                    yield return ForestPlaySupport.RenderFrames(camera, ForestPlaySupport.RequireViewpoint(view), frames);
                    Color32[] pixels = ForestPlaySupport.ReadPixels(target);
                    if (saveFolder != null)
                        SavePng(Path.Combine(saveFolder, view + fileSuffix + ".png"), pixels, width, height);
                    judge(view + fileSuffix, ForestPlaySupport.Measure(pixels));
                }
            }
            finally
            {
                camera.targetTexture = null;
                target.Release();
                Object.Destroy(target);
                ForestPlaySupport.ReleaseMeasuredCamera(camera);
            }
        }

        static void SavePng(string path, Color32[] pixels, int width, int height)
        {
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.SetPixels32(pixels);
            image.Apply(false);
            File.WriteAllBytes(path, image.EncodeToPNG());
            Object.Destroy(image);
        }

        // ---- Budgets ----

        sealed class Counter
        {
            public string name;
            public ProfilerCategory category;
            public long warnAt, budget;
            public bool isMemory;
            public ProfilerRecorder recorder;
            public long worst;
            public string worstView = "";
        }

        // Positions in the counters array of Budget_Counts.
        const int TrianglesIndex = 0, DrawCallsIndex = 2, ShadowCastersIndex = 4;

        /// <summary>
        /// The GPU Resident Drawer, on in this project since step D1 (build/issues/D1-optimisation.md), read by reflection: this
        /// assembly does not reference Unity.RenderPipelines.GPUDriven.Runtime. Under the drawer the engine's counters see only part
        /// of a frame (measured in D1): every MeshRenderer the drawer owns reports Renderer.isVisible whether it is drawn or not, so
        /// the draw estimate from visible renderers counts the whole scene; 'Shadow Casters Count' leaves out the drawer's shadow
        /// casters; and with GPU occlusion culling on, 'Triangles Count' and 'Vertices Count' leave out its indirect draws. The
        /// drawer's own culling statistics (the Rendering Debugger's "Display Culling Stats") give the missing numbers.
        /// Any reflection failure throws: a budget the test cannot read is a failure, not a pass.
        /// </summary>
        static class ResidentDrawer
        {
            const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            static readonly Type Drawer = Type.GetType("UnityEngine.Rendering.GPUResidentDrawer, Unity.RenderPipelines.GPUDriven.Runtime");

            static object Call(string name) => Drawer?.GetMethod(name, AnyStatic, null, Type.EmptyTypes, null)?.Invoke(null, null);

            public static bool IsActive => Call("IsInitialized") is bool on && on;
            public static bool OcclusionCulling => Call("IsInstanceOcclusionCullingEnabled") is bool on && on;

            /// <summary>The renderers the drawer has taken over (its RenderWorld.instanceCount).</summary>
            public static int RegisteredRenderers()
            {
                object drawer = Drawer.GetField("s_Instance", AnyStatic).GetValue(null);
                object system = drawer.GetType().GetField("m_InstanceDataSystem", AnyInstance).GetValue(drawer);
                object world = system.GetType().GetField("m_RenderWorld", AnyInstance).GetValue(system);
                return (int)world.GetType().GetProperty("instanceCount", AnyInstance).GetValue(world);
            }

            public static void Statistics(bool on)
            {
                object settings = Drawer?.GetProperty("debugDisplaySettings", AnyStatic)?.GetValue(null);
                PropertyInfo flag = settings?.GetType().GetProperty("displayBatcherStats", AnyInstance);
                if (flag == null)
                {
                    if (on) throw new InvalidOperationException("The GPU Resident Drawer's culling statistics (debugDisplaySettings.displayBatcherStats) cannot be reached.");
                    return;
                }
                flag.SetValue(settings, on);
            }

            /// <summary>The last frame's draw commands and visible instances, summed over the camera view and the shadow-map views.</summary>
            public static void Read(out long cameraDraws, out long shadowDraws, out long shadowInstances)
            {
                cameraDraws = shadowDraws = shadowInstances = 0;
                object stats = Drawer.GetMethod("GetDebugStats", AnyStatic, null, Type.EmptyTypes, null).Invoke(null, null);
                object list = stats.GetType().GetField("instanceCullerStats", AnyInstance).GetValue(stats);
                object array = list.GetType().GetMethod("AsArray", Type.EmptyTypes).Invoke(list, null);
                foreach (object view in (Array)array.GetType().GetMethod("ToArray", Type.EmptyTypes).Invoke(array, null))
                {
                    Type type = view.GetType();
                    string kind = type.GetField("viewType").GetValue(view).ToString();
                    int draws = (int)type.GetField("drawCommands").GetValue(view);
                    if (kind == "Camera")
                        cameraDraws += draws;
                    else if (kind == "Light")
                    {
                        shadowDraws += draws;
                        shadowInstances += (int)type.GetField("visibleInstancesOnCPU").GetValue(view);
                    }
                }
            }
        }

        /// <summary>
        /// Draw calls of the frame that has just been rendered, counted from the scene. Used only where the
        /// engine's own 'Draw Calls Count' reads 0. Every renderer the frame's culling found visible costs one
        /// draw per material slot in the depth prepass and one in the main pass (decals are enabled in this
        /// project, so HDRP always runs the prepass); every shadow caster the engine counted costs one draw per
        /// material slot of the average visible caster. A renderer that only a shadow cascade sees is counted
        /// in the main passes too, so the number errs on the high side. The terrain is not a Renderer: its
        /// instanced patches (a handful of draws per pass) are in the count only through the shadow casters.
        /// With the GPU Resident Drawer on, MeshRenderers are left out (<paramref name="skipMeshRenderers"/>):
        /// the drawer draws them, and every one of them reports isVisible whether it is drawn or not.
        /// </summary>
        static long EstimateDrawCalls(long shadowCasters, bool skipMeshRenderers = false)
        {
            long slots = 0, casterSlots = 0, casters = 0;
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false))
                {
                    if (!renderer.enabled || !renderer.isVisible || (skipMeshRenderers && renderer is MeshRenderer))
                        continue;
                    int count = Mathf.Max(renderer.sharedMaterials.Length, 1);
                    if (renderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
                        slots += count;
                    if (renderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)
                    {
                        casterSlots += count;
                        casters++;
                    }
                }
            }
            double slotsPerCaster = casters > 0 ? (double)casterSlots / casters : 1.0;
            return 2 * slots + (long)Math.Ceiling(shadowCasters * slotsPerCaster);
        }

        static int ActiveMeshRenderers()
        {
            int count = 0;
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(false))
                    if (renderer.enabled && renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                        count++;
            return count;
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator Budget_Counts()
        {
            var counters = new[]
            {
                new Counter { name = "Triangles Count", category = ProfilerCategory.Render, warnAt = ForestPlayBudgets.TrianglesWarn, budget = ForestPlayBudgets.TrianglesBudget },
                new Counter { name = "Vertices Count", category = ProfilerCategory.Render, warnAt = ForestPlayBudgets.VerticesWarn, budget = ForestPlayBudgets.VerticesBudget },
                new Counter { name = "Draw Calls Count", category = ProfilerCategory.Render, warnAt = ForestPlayBudgets.DrawCallsWarn, budget = ForestPlayBudgets.DrawCallsBudget },
                new Counter { name = "SetPass Calls Count", category = ProfilerCategory.Render, warnAt = ForestPlayBudgets.SetPassCallsWarn, budget = ForestPlayBudgets.SetPassCallsBudget },
                new Counter { name = "Shadow Casters Count", category = ProfilerCategory.Render, warnAt = ForestPlayBudgets.ShadowCastersWarn, budget = ForestPlayBudgets.ShadowCastersBudget },
                new Counter { name = "Texture Memory", category = ProfilerCategory.Memory, warnAt = ForestPlayBudgets.TextureMemoryWarn, budget = ForestPlayBudgets.TextureMemoryBudget, isMemory = true },
                new Counter { name = "Mesh Memory", category = ProfilerCategory.Memory, warnAt = ForestPlayBudgets.MeshMemoryWarn, budget = ForestPlayBudgets.MeshMemoryBudget, isMemory = true },
            };
            foreach (Counter counter in counters)
                counter.recorder = ProfilerRecorder.StartNew(counter.category, counter.name);

            // N1: as the game starts, at night with the torch on and casting shadows.
            Camera camera = ForestPlaySupport.CreateMeasuredCamera(withFlashlight: true);
            RenderTexture target = ForestPlaySupport.CreateTarget(ForestPlayBudgets.FrameWidth, ForestPlayBudgets.FrameHeight);
            camera.targetTexture = target;
            var lines = new List<string>();
            long worstDrawEstimate = 0;
            string worstDrawEstimateView = "";
            bool drawer = false;
            try
            {
                foreach (Counter counter in counters)
                    Assert.IsTrue(counter.recorder.Valid, $"The profiler counter '{counter.name}' is not available, so the budget cannot be measured.");

                // The counters are per engine frame, and the measured camera is the only one that renders.
                foreach (string view in BudgetViews)
                {
                    yield return ForestPlaySupport.RenderFrames(camera, ForestPlaySupport.RequireViewpoint(view), 24);
                    // HDRP creates the drawer on its first render after entering play mode, so it is asked here, after rendering.
                    drawer = ResidentDrawer.IsActive;
                    Assert.IsFalse(drawer && ResidentDrawer.OcclusionCulling,
                        "GPU occlusion culling is on: 'Triangles Count' and 'Vertices Count' do not count its indirect draws (D1), so the triangle " +
                        "and vertex budgets cannot be measured by this test. Turn it off (HDRP asset) or teach this test the drawer's triangle statistics.");
                    var worstHere = new long[counters.Length];
                    long estimateHere = 0;
                    // Under the drawer the visible-renderer count leaves out the MeshRenderers only if the drawer has taken all of them.
                    bool skipMeshRenderers = false;
                    if (drawer)
                    {
                        int registered = ResidentDrawer.RegisteredRenderers(), active = ActiveMeshRenderers();
                        skipMeshRenderers = registered >= active;
                        if (!skipMeshRenderers)
                            Debug.LogWarning($"Forest budget: the GPU Resident Drawer has taken {registered} of {active} active MeshRenderers; " +
                                "the draw estimate counts every visible MeshRenderer on top of the drawer's own draws (high side).");
                    }
                    for (int sample = 0; sample < 8; sample++)
                    {
                        camera.Render();
                        yield return null;
                        // LastValue is the frame that has just ended: the one this render was part of.
                        for (int i = 0; i < counters.Length; i++)
                            worstHere[i] = Math.Max(worstHere[i], counters[i].recorder.LastValue);
                        estimateHere = Math.Max(estimateHere, EstimateDrawCalls(counters[ShadowCastersIndex].recorder.LastValue, skipMeshRenderers));
                    }
                    if (drawer)
                    {
                        // The drawer's draw commands (instanced draws) and shadow casters, from its culling statistics. A camera
                        // draw command is drawn in the depth prepass and in the main pass, as in the estimate above.
                        long drawerDraws = 0, drawerCasters = 0;
                        ResidentDrawer.Statistics(true);
                        try
                        {
                            for (int sample = 0; sample < 8; sample++)
                            {
                                camera.Render();
                                yield return null;
                                if (sample == 0)
                                    continue;   // the first frame's statistics were gathered before they were switched on
                                ResidentDrawer.Read(out long cameraDraws, out long shadowDraws, out long shadowInstances);
                                drawerDraws = Math.Max(drawerDraws, 2 * cameraDraws + shadowDraws);
                                drawerCasters = Math.Max(drawerCasters, shadowInstances);
                            }
                        }
                        finally
                        {
                            ResidentDrawer.Statistics(false);
                        }
                        Assert.Greater(drawerDraws, 0, $"The GPU Resident Drawer is on but reported no draw command at {view}, so no draw-call budget can be checked.");
                        estimateHere += drawerDraws;
                        worstHere[ShadowCastersIndex] += drawerCasters;
                    }
                    if (estimateHere > worstDrawEstimate)
                    {
                        worstDrawEstimate = estimateHere;
                        worstDrawEstimateView = view;
                    }
                    for (int i = 0; i < counters.Length; i++)
                    {
                        if (worstHere[i] > counters[i].worst)
                        {
                            counters[i].worst = worstHere[i];
                            counters[i].worstView = view;
                        }
                    }
                    lines.Add($"{view}: " + string.Join(", ", Array.ConvertAll(counters, c => $"{c.name} {worstHere[Array.IndexOf(counters, c)]}"))
                        + (drawer ? $", draw calls estimated from the GPU Resident Drawer's draw commands and the renderers it does not draw {estimateHere}"
                                  : $", draw calls estimated from visible renderers {estimateHere}"));
                }

                // Memory is judged as what the forest adds: the same counters again in an empty scene.
                camera.targetTexture = null;
                Object.Destroy(camera.gameObject);
                camera = null;
                Scene forest = SceneManager.GetActiveScene();
                Scene empty = SceneManager.CreateScene("Forest Budget Baseline");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(forest);
                yield return Resources.UnloadUnusedAssets();
                GC.Collect();
                for (int i = 0; i < 5; i++)
                    yield return null;
                var baseline = new long[counters.Length];
                for (int i = 0; i < counters.Length; i++)
                    baseline[i] = counters[i].isMemory ? counters[i].recorder.LastValue : 0;

                // In Unity 6000.6 with HDRP 17.6 on this machine the engine never feeds 'Draw Calls Count' (nor
                // 'Batches Count'): both read 0 with the terrain and with 50 extra cubes in view, for explicit renders
                // and for an enabled camera alike, while the triangle, vertex, SetPass and shadow-caster counters
                // report (measured at M1, issues/M1-draw-call-counter.md). A budget "met" by that zero would be
                // a silent pass, so the draw-call budget is then checked against a count made from the scene.
                bool drawCallsEstimated = counters[DrawCallsIndex].worst == 0 && counters[TrianglesIndex].worst > 0;
                if (drawCallsEstimated)
                {
                    counters[DrawCallsIndex].worst = worstDrawEstimate;
                    counters[DrawCallsIndex].worstView = worstDrawEstimateView;
                }

                var problems = new List<string>();
                var report = new List<string>();
                for (int i = 0; i < counters.Length; i++)
                {
                    Counter counter = counters[i];
                    long value = counter.worst - baseline[i];
                    string shown = counter.isMemory ? $"{value / 1048576.0:0.0} MiB above an empty scene" : value.ToString(CultureInfo.InvariantCulture);
                    if (i == DrawCallsIndex && drawCallsEstimated)
                        shown += drawer
                            ? " (ESTIMATED: 2 x the GPU Resident Drawer's camera draw commands + its shadow draw commands, from its culling statistics, plus the visible renderers it does not draw; the engine's counter reads 0 in this Unity version)"
                            : " (ESTIMATED from the renderers the frame's culling found visible; the engine's counter reads 0 in this Unity version)";
                    if (i == ShadowCastersIndex && drawer)
                        shown += " (the engine's count plus the instances the GPU Resident Drawer drew into the shadow maps, from its culling statistics)";
                    string limit = counter.isMemory ? $"{counter.budget / 1048576.0:0} MiB" : counter.budget.ToString(CultureInfo.InvariantCulture);
                    report.Add($"{counter.name}: {shown} at {counter.worstView} (budget {limit})");
                    if (value > counter.budget)
                        problems.Add($"{counter.name} is {shown} at {counter.worstView}, above the budget of {limit}");
                    else if (value > counter.warnAt)
                        Debug.LogWarning($"Forest budget warning: {counter.name} is {shown} at {counter.worstView}, above the warning level (budget {limit}).");
                }
                Debug.Log("Forest budget counts at 2560 x 1440:\n  " + string.Join("\n  ", report) + "\nPer view:\n  " + string.Join("\n  ", lines));
                File.WriteAllLines(Path.Combine(ForestPlaySupport.OutputFolder(), "budget-counts.txt"), report.ToArray());

                // A counter that is valid but reads nothing did not see the measured camera: that is not a pass.
                Assert.Greater(counters[TrianglesIndex].worst, 0, "'Triangles Count' read 0 for every frame, so the counters did not see the measured camera and no budget was checked.");
                Assert.Greater(counters[DrawCallsIndex].worst, 0, "'Draw Calls Count' read 0 for every frame and the count made from the scene found no draw either, so no draw-call budget was checked.");
                Assert.IsEmpty(problems, "Over budget:\n  " + string.Join("\n  ", problems));
            }
            finally
            {
                foreach (Counter counter in counters)
                    counter.recorder.Dispose();
                if (camera != null)
                {
                    camera.targetTexture = null;
                    Object.Destroy(camera.gameObject);
                }
                target.Release();
                Object.Destroy(target);
            }
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator Budget_FrameTime()
        {
            // Real time here: a fixed capture step would make every frame look the same length.
            Time.captureDeltaTime = 0f;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;

            // N1: as the game starts, at night with the torch on and casting shadows.
            Camera camera = ForestPlaySupport.CreateMeasuredCamera(withFlashlight: true);
            RenderTexture target = ForestPlaySupport.CreateTarget(ForestPlayBudgets.FrameWidth, ForestPlayBudgets.FrameHeight);
            var pixel = new Texture2D(1, 1, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            try
            {
                yield return ForestPlaySupport.RenderFrames(camera, ForestPlaySupport.RequireViewpoint("trail_start"), 60);

                // Method one: the engine's own frame timing. Its results lag four frames and may be empty in the Editor.
                bool featureEnabled = FrameTimingManager.IsFeatureEnabled();
                var timings = new FrameTiming[1];
                var gpu = new List<double>();
                const int timedFrames = 240;
                for (int i = 0; i < timedFrames; i++)
                {
                    camera.Render();
                    yield return null;
                    FrameTimingManager.CaptureFrameTimings();
                    uint received = FrameTimingManager.GetLatestTimings(1, timings);
                    if (received > 0 && timings[0].gpuFrameTime > 0.0)
                        gpu.Add(timings[0].gpuFrameTime);
                }
                bool frameTimingWorks = featureEnabled && gpu.Count >= timedFrames / 2;

                // Method two: wall time around a render, with a one-pixel readback so the GPU has finished.
                // It serialises CPU and GPU, so it is an upper bound.
                var wall = new List<double>();
                var watch = new System.Diagnostics.Stopwatch();
                RenderTexture previous = RenderTexture.active;
                for (int i = 0; i < 120; i++)
                {
                    watch.Restart();
                    camera.Render();
                    RenderTexture.active = target;
                    pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                    wall.Add(watch.Elapsed.TotalMilliseconds);
                    RenderTexture.active = previous;
                    yield return null;
                }

                float gpuMedian = ForestPlaySupport.Median(gpu), gpuP95 = ForestPlaySupport.Percentile(gpu, 0.95);
                float wallMedian = ForestPlaySupport.Median(wall), wallP95 = ForestPlaySupport.Percentile(wall, 0.95);
                string validated = ForestPlaySupport.ValidatedFrameTiming();
                string summary = $"Forest frame time at trail_start, 2560 x 1440, in the Editor: " +
                    $"FrameTiming GPU median {gpuMedian:0.00} ms, p95 {gpuP95:0.00} ms ({gpu.Count} of {timedFrames} frames reported, feature enabled {featureEnabled}); " +
                    $"stopwatch with readback median {wallMedian:0.00} ms, p95 {wallP95:0.00} ms. Probe P5 says frame timing: {validated ?? "not probed"}.";
                Debug.Log(summary);
                File.WriteAllText(Path.Combine(ForestPlaySupport.OutputFolder(), "frame-time.txt"), summary);

                // Asserted only once the M0 probe has said which timing method can be trusted on this machine.
                if (validated == null)
                {
                    Debug.Log("Forest frame time was logged, not asserted: the M0 probe (Gamex/Forest/0 Run Probe) has not validated a timing method for this Unity version.");
                    yield break;
                }

                bool useFrameTiming = validated == "yes" && frameTimingWorks;
                float median = useFrameTiming ? gpuMedian : wallMedian;
                string method = useFrameTiming ? "FrameTiming.gpuFrameTime" : "a stopwatch around a one-pixel readback (an upper bound)";
                if (median > ForestPlayBudgets.GpuFrameMsWarn && median <= ForestPlayBudgets.GpuFrameMsBudget)
                    Debug.LogWarning($"Forest frame time warning: median {median:0.00} ms by {method}, above the warning level of {ForestPlayBudgets.GpuFrameMsWarn} ms.");
                Assert.LessOrEqual(median, ForestPlayBudgets.GpuFrameMsBudget,
                    $"The median frame time is {median:0.00} ms, measured by {method}; the budget is {ForestPlayBudgets.GpuFrameMsBudget} ms.");
            }
            finally
            {
                camera.targetTexture = null;
                Object.Destroy(camera.gameObject);
                target.Release();
                Object.Destroy(target);
                Object.Destroy(pixel);
            }
        }
    }
}
