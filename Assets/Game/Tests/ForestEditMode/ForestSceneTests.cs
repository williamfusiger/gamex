using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Gamex.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Gamex.Tests.Forest
{
    /// <summary>
    /// Tests of the built forest scene, in edit mode. Every one opens the scene itself and is ignored
    /// until the scene has been built, so the suite is green from the first milestone on.
    /// </summary>
    [Category("Forest")]
    public class ForestSceneTests
    {
        // The player's body, as the greybox rig and its copy in the forest set it up.
        const float PlayerRadius = 0.35f, PlayerHeight = 1.8f;

        [Test]
        public void Scene_PassesAudit()
        {
            ForestTestSupport.RequireForestScene();
            ForestAudit.Report report = ForestAudit.Run(false);

            var errors = new List<string>();
            foreach (ForestAudit.Finding finding in report.findings)
                if (finding.severity == "error")
                    errors.Add($"[{finding.check}] {finding.message}");
            ForestTestSupport.AssertNone(errors, "audit error(s)");
            Assert.IsTrue(report.passed);
            Assert.AreEqual("none", report.apv, "Version 1 has no probe bake.");
            Assert.IsTrue(File.Exists(ForestPaths.ReportPath), "The audit of the forest scene must write the build report.");
        }

        [Test]
        public void Scene_IsClosed()
        {
            Scene scene = ForestTestSupport.RequireForestScene();
            Terrain terrain = ForestTestSupport.RequireTerrain(scene);
            Transform walls = ForestTestSupport.RequireChild(ForestTestSupport.RequireRoot(scene, "Environment"), "Bounds");

            // Whether physics answers at all in edit mode is one of the probe's open questions (P12). If it
            // does not, this test cannot say anything; the PlayMode test Bank_CannotBeClimbed still can.
            Physics.SyncTransforms();
            Vector2 spawn = ForestLayout.SpawnXZ;
            float spawnGround = ForestTestSupport.GroundHeight(terrain, spawn.x, spawn.y);
            // The ray comes down on the spawn, where the player stands: its CharacterController is the first
            // thing hit, 1.85 m above the ground (found at M1, where that made this test ignore itself). The
            // question is whether the terrain's own collider answers, so that is the hit looked for.
            var terrainCollider = terrain.GetComponent<TerrainCollider>();
            bool terrainAnswers = false;
            foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(spawn.x, spawnGround + 50f, spawn.y), Vector3.down, 100f, ~0, QueryTriggerInteraction.Ignore))
                terrainAnswers |= hit.collider == terrainCollider && Mathf.Abs(hit.point.y - spawnGround) <= 0.1f;
            if (!terrainAnswers)
                Assert.Ignore("Physics queries do not answer in edit mode here (probe P12), so the walls cannot be tested from the Editor. Bank_CannotBeClimbed tests them in play mode.");

            List<Vector2> starts = InteriorPoints(20);
            Assert.AreEqual(20, starts.Count, "Could not find 20 points well inside the boundary.");

            const int bearings = 64;
            // The walls stand 1.5 m outside the boundary and are a metre thick.
            const float wallLine = 1.5f, reach = 4f;
            var problems = new List<string>();
            foreach (Vector2 start in starts)
            {
                for (int b = 0; b < bearings; b++)
                {
                    float angle = b * (360f / bearings) * Mathf.Deg2Rad;
                    var direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));

                    // Where this bearing leaves the area, and the ground the player would stand on there.
                    float travelled = 0f;
                    while (ForestLayout.SignedDistanceToBoundary(start + direction * travelled) < wallLine && travelled < 400f)
                        travelled += 0.25f;
                    Vector2 exit = start + direction * travelled;
                    float feet = ForestTestSupport.GroundHeight(terrain, exit.x, exit.y) + 0.05f;

                    // A player-sized capsule at that height, swept outward from the interior point.
                    var bottom = new Vector3(start.x, feet + PlayerRadius, start.y);
                    var top = new Vector3(start.x, feet + PlayerHeight - PlayerRadius, start.y);
                    bool stopped = false;
                    foreach (RaycastHit hit in Physics.CapsuleCastAll(bottom, top, PlayerRadius, new Vector3(direction.x, 0f, direction.y), travelled + reach, ~0, QueryTriggerInteraction.Ignore))
                        stopped |= hit.collider is BoxCollider && hit.collider.transform.IsChildOf(walls);
                    if (!stopped)
                        problems.Add($"from ({start.x:0.0}, {start.y:0.0}) on bearing {b * (360f / bearings):0.0}: no wall within {travelled + reach:0.0} m (exit near ({exit.x:0.0}, {exit.y:0.0}))");
                }
            }
            ForestTestSupport.AssertNone(problems, "way(s) out of the playable area");
        }

        // Deterministic points at least five metres inside the boundary: the spawn, the landmarks, the trail, and a coarse grid.
        static List<Vector2> InteriorPoints(int count)
        {
            var candidates = new List<Vector2> { ForestLayout.SpawnXZ };
            foreach (ForestLayout.Landmark landmark in ForestLayout.Landmarks)
                candidates.Add(landmark.xz);
            for (int i = 1; i < 8; i++)
                candidates.Add(ForestLayout.SampleSpline(ForestLayout.Trail, i / 8f));
            for (int z = -60; z <= 60; z += 30)
                for (int x = -50; x <= 50; x += 25)
                    candidates.Add(new Vector2(x + 3.5f, z + 2.5f));

            var points = new List<Vector2>();
            foreach (Vector2 candidate in candidates)
                if (points.Count < count && ForestLayout.SignedDistanceToBoundary(candidate) < -5f)
                    points.Add(candidate);
            return points;
        }

        [Test]
        public void Scene_NavMeshConnectsLandmarks()
        {
            Scene scene = ForestTestSupport.RequireForestScene();
            Terrain terrain = ForestTestSupport.RequireTerrain(scene);
            GameObject gameplay = ForestTestSupport.RequireRoot(scene, "Gameplay");
            var problems = new List<string>();

            Vector3 spawn = OnGround(terrain, ForestLayout.SpawnXZ);
            if (!NavMesh.SamplePosition(spawn, out NavMeshHit spawnHit, 1.5f, NavMesh.AllAreas))
                Assert.Fail("There is no NavMesh within 1.5 m of the spawn point. Was the NavMesh baked and saved with the scene?");

            // From the spawn to every landmark. A landmark is a set piece with a size: its centre may be inside a
            // rock, and two of them lean on the bank. Reaching any walkable ground inside its circle is reaching it.
            var path = new NavMeshPath();
            foreach (ForestLayout.Landmark landmark in ForestLayout.Landmarks)
            {
                float radius = Mathf.Max(landmark.radius, 6f);
                bool anyGround = false, reached = false;
                for (int ring = 0; ring <= 2 && !reached; ring++)
                {
                    int bearings = ring == 0 ? 1 : 8;
                    for (int b = 0; b < bearings && !reached; b++)
                    {
                        float angle = b * (2f * Mathf.PI / bearings);
                        Vector2 xz = landmark.xz + new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * (radius * 0.45f * ring);
                        if (ForestLayout.SignedDistanceToBoundary(xz) >= 0f || !NavMesh.SamplePosition(OnGround(terrain, xz), out NavMeshHit hit, 2f, NavMesh.AllAreas))
                            continue;
                        anyGround = true;
                        reached = NavMesh.CalculatePath(spawnHit.position, hit.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                    }
                }
                if (!anyGround)
                    problems.Add($"landmark {landmark.name}: no NavMesh within its {radius:0} m circle");
                else if (!reached)
                    problems.Add($"landmark {landmark.name}: no complete path from the spawn to any walkable point inside its {radius:0} m circle");
            }

            // Where a dinosaur can later be dropped in: each anchor stands on walkable ground the spawn can be reached from.
            Transform dinoSpawns = ForestTestSupport.RequireChild(gameplay, "DinoSpawns");
            if (dinoSpawns.childCount != 3)
                problems.Add($"{dinoSpawns.childCount} dinosaur spawn anchors, expected Hollow, BoulderField and NorthOutcrop");
            foreach (Transform anchor in dinoSpawns)
            {
                if (!NavMesh.SamplePosition(anchor.position, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                    problems.Add($"dinosaur spawn {anchor.name}: no NavMesh within 2.5 m");
                else if (!NavMesh.CalculatePath(spawnHit.position, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                    problems.Add($"dinosaur spawn {anchor.name}: no complete path from the player spawn ({path.status})");
            }

            // Between consecutive patrol nodes of each trail: <spline>_<n>, one per control point.
            var nodes = new Dictionary<string, SortedDictionary<int, Transform>>();
            foreach (Transform node in ForestTestSupport.RequireChild(gameplay, "PatrolNodes"))
            {
                int separator = node.name.LastIndexOf('_');
                if (separator <= 0 || !int.TryParse(node.name.Substring(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
                {
                    problems.Add($"patrol node '{node.name}' is not named <spline>_<n>");
                    continue;
                }
                string spline = node.name.Substring(0, separator);
                if (!nodes.TryGetValue(spline, out SortedDictionary<int, Transform> ordered))
                    nodes[spline] = ordered = new SortedDictionary<int, Transform>();
                ordered[index] = node;
            }
            int expectedNodes = ForestLayout.Trail.Length + ForestLayout.WestTrail.Length + ForestLayout.EastTrail.Length;
            int foundNodes = 0;
            foreach (KeyValuePair<string, SortedDictionary<int, Transform>> spline in nodes)
            {
                foundNodes += spline.Value.Count;
                Transform previous = null;
                foreach (Transform node in spline.Value.Values)
                {
                    // The corridor around a trail is free for 2.5 m, so the NavMesh is never further away than that.
                    if (!NavMesh.SamplePosition(node.position, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                        problems.Add($"patrol node {node.name}: no NavMesh within 2.5 m");
                    else if (previous != null && NavMesh.SamplePosition(previous.position, out NavMeshHit from, 2.5f, NavMesh.AllAreas)
                        && (!NavMesh.CalculatePath(from.position, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete))
                        problems.Add($"no complete path from patrol node {previous.name} to {node.name} ({path.status})");
                    previous = node;
                }
            }
            if (foundNodes != expectedNodes)
                problems.Add($"{foundNodes} patrol nodes, expected one per control point of the three trails ({expectedNodes})");

            Transform waypoints = ForestTestSupport.RequireChild(gameplay, "TrailWaypoints");
            if (waypoints.childCount < 10)
                problems.Add($"{waypoints.childCount} trail waypoints, expected one every 10 m of a trail of about 150 m");
            foreach (Transform waypoint in waypoints)
                if (!NavMesh.SamplePosition(waypoint.position, out _, 0.5f, NavMesh.AllAreas))
                    problems.Add($"trail waypoint {waypoint.name} is more than 0.5 m from the NavMesh");

            ForestTestSupport.AssertNone(problems, "NavMesh problem(s)");
        }

        static Vector3 OnGround(Terrain terrain, Vector2 xz) => new Vector3(xz.x, ForestTestSupport.GroundHeight(terrain, xz.x, xz.y), xz.y);

        [Test]
        public void Scene_HasNoSourceMeshes()
        {
            Scene scene = ForestTestSupport.RequireForestScene();
            GameObject environment = ForestTestSupport.RequireRoot(scene, "Environment");
            string sources = ForestPaths.ThirdPartyRoot + "/";

            // A scanned source mesh in the scene would be millions of triangles nobody budgeted for.
            var problems = new List<string>();
            foreach (Renderer renderer in environment.GetComponentsInChildren<Renderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                string mesh = filter != null && filter.sharedMesh != null ? AssetDatabase.GetAssetPath(filter.sharedMesh) : "";
                if (mesh.StartsWith(sources, StringComparison.Ordinal))
                    problems.Add($"{renderer.name} draws {mesh}");
                foreach (Material material in renderer.sharedMaterials)
                {
                    string path = material != null ? AssetDatabase.GetAssetPath(material) : "";
                    if (path.StartsWith(sources, StringComparison.Ordinal))
                        problems.Add($"{renderer.name} uses material {path}");
                }
            }
            foreach (MeshCollider collider in environment.GetComponentsInChildren<MeshCollider>(true))
            {
                string mesh = collider.sharedMesh != null ? AssetDatabase.GetAssetPath(collider.sharedMesh) : "";
                if (mesh.StartsWith(sources, StringComparison.Ordinal))
                    problems.Add($"{collider.name} collides with {mesh}");
            }
            ForestTestSupport.AssertNone(problems, $"reference(s) under Environment into {ForestPaths.ThirdPartyRoot}");
        }

        [Test]
        public void Lighting_OneVolumeOneShadowLight()
        {
            Scene scene = ForestTestSupport.RequireForestScene();
            GameObject lighting = ForestTestSupport.RequireRoot(scene, "Lighting");

            var volumes = new List<string>();
            var suns = new List<string>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Volume volume in root.GetComponentsInChildren<Volume>(false))
                    if (volume.isActiveAndEnabled && volume.isGlobal)
                        volumes.Add(volume.name);
                foreach (Light light in root.GetComponentsInChildren<Light>(false))
                    if (light.isActiveAndEnabled && light.type == LightType.Directional && light.shadows != LightShadows.None)
                        suns.Add(light.name);
            }
            Assert.AreEqual(1, volumes.Count, "Exactly one global volume may be active: " + string.Join(", ", volumes));
            Assert.AreEqual(1, suns.Count, "Exactly one shadow-casting directional light may be active: " + string.Join(", ", suns));

            // One preset child per preset whose sky is downloaded, and exactly one of them active.
            int active = 0;
            foreach (string preset in ForestLightingPresets.Names)
            {
                ForestAssetEntry sky = ForestManifest.Find(ForestLightingPresets.SkyId(preset));
                Assert.IsNotNull(sky, $"Preset {preset} names a sky that is not in the manifest.");
                Transform child = lighting.transform.Find("Preset_" + preset);
                if (ForestManifest.IsDownloaded(sky))
                    Assert.IsNotNull(child, $"The sky of preset {preset} is downloaded, but Lighting has no child Preset_{preset}.");
                else
                    Assert.IsNull(child, $"Lighting has a child Preset_{preset} although its sky is not downloaded.");
                if (child != null && child.gameObject.activeSelf)
                    active++;
            }
            Assert.AreEqual(1, active, "Exactly one preset child must be active.");
            Assert.IsNotEmpty(ForestLighting.ActivePreset(), "ForestLighting does not report an active preset.");
        }

        [Test]
        public void Terrain_ExtraLayersRender()
        {
            Scene scene = ForestTestSupport.RequireForestScene();
            TerrainLayer[] layers = ForestMaterials.GetTerrainLayers();
            Material material = ForestMaterials.GetTerrainMaterial();
            // The scene says how it was built: a build with -forestLayers 4 leaves eight layer assets but uses four.
            int used = ForestTestSupport.RequireTerrain(scene).terrainData.terrainLayers.Length;
            if (used < 8 || layers == null || layers.Length < 8)
                Assert.Ignore($"The scene's terrain uses {used} layers ({(layers != null ? layers.Length : 0)} are built), so there are no layers beyond the fourth to test.");
            Assert.IsNotNull(material, "Eight terrain layers exist but the terrain material does not.");

            // A small patch far above the forest, painted with one layer, seen from straight above under the scene's own light.
            // Layer 0 is the control: if even that is black, the render says nothing about the layers behind the fourth.
            // N1: under the daylight preset. The scene's default is now the night, under which every patch, the control too,
            // renders near black and this test could only ignore itself.
            Camera camera = null;
            var luminance = new Dictionary<int, float>();
            string presetBefore = ForestLighting.ActivePreset();
            Assert.IsTrue(ForestLighting.ApplyPreset(ForestLightingPresets.Inspect), $"The scene has no {ForestLightingPresets.Inspect} preset to render the patches under.");
            try
            {
                camera = new GameObject("Layer Test Camera") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>();
                camera.enabled = false;
                ForestLighting.ConfigureCamera(camera);
                camera.orthographic = true;
                camera.orthographicSize = 5f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 50f;
                foreach (int layer in new[] { 0, 4, 5, 6, 7 })
                    luminance[layer] = RenderPatch(camera, layers, material, layer);
            }
            finally
            {
                if (camera != null)
                    Object.DestroyImmediate(camera.gameObject);
                if (!string.IsNullOrEmpty(presetBefore))
                    ForestLighting.ApplyPreset(presetBefore);
            }

            const float notBlack = 0.03f;
            if (luminance[0] < notBlack)
                Assert.Ignore($"The control patch painted with layer 0 rendered black (mean luminance {luminance[0]:0.000}), so this render cannot judge the extra layers.");
            var problems = new List<string>();
            foreach (int layer in new[] { 4, 5, 6, 7 })
                if (luminance[layer] < notBlack)
                    problems.Add($"a patch painted 100 % with layer {layer} ({layers[layer].name}) rendered black: mean luminance {luminance[layer]:0.000}, the control has {luminance[0]:0.000}");
            ForestTestSupport.AssertNone(problems, "terrain layer(s) beyond the fourth do not render; fall back to ForestLayout.TerrainLayerCount = 4");
        }

        static float RenderPatch(Camera camera, TerrainLayer[] layers, Material material, int paintedLayer)
        {
            const int alphamap = 16;
            const float size = 16f;
            var origin = new Vector3(2000f, 400f, 2000f);

            var data = new TerrainData { hideFlags = HideFlags.HideAndDontSave };
            GameObject patch = null;
            try
            {
                data.heightmapResolution = 33;
                data.size = new Vector3(size, 1f, size);
                data.alphamapResolution = alphamap;
                data.terrainLayers = layers;
                var weights = new float[alphamap, alphamap, layers.Length];
                for (int z = 0; z < alphamap; z++)
                    for (int x = 0; x < alphamap; x++)
                        weights[z, x, paintedLayer] = 1f;
                data.SetAlphamaps(0, 0, weights);

                // Terrain.CreateTerrainGameObject assigns the pipeline's default terrain material and marks it dirty.
                // The next save of assets would then rewrite that file inside the package cache (it did, on the first
                // M1 run of this suite), so the mark is taken back, as in ForestTerrain.Create.
                Material packageDefault = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                    ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.defaultTerrainMaterial : null;
                bool defaultWasDirty = packageDefault != null && EditorUtility.IsDirty(packageDefault);
                patch = Terrain.CreateTerrainGameObject(data);
                if (packageDefault != null && !defaultWasDirty)
                    EditorUtility.ClearDirty(packageDefault);
                patch.hideFlags = HideFlags.HideAndDontSave;
                patch.transform.position = origin;
                var terrain = patch.GetComponent<Terrain>();
                terrain.materialTemplate = material;
                terrain.drawInstanced = true;
                terrain.basemapDistance = 1000f;

                camera.transform.SetPositionAndRotation(origin + new Vector3(size * 0.5f, 20f, size * 0.5f), Quaternion.Euler(90f, 0f, 0f));
                Texture2D image = ForestCapture.Render(camera, 128, 128, 8);
                try
                {
                    return ForestImageStats.Compute(image.GetPixels32()).meanLuminance;
                }
                finally
                {
                    Object.DestroyImmediate(image);
                }
            }
            finally
            {
                if (patch != null)
                    Object.DestroyImmediate(patch);
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void PlayerRig_MatchesGreybox()
        {
            ForestTestSupport.RequireForestScene();
            if (!File.Exists(GreyboxSceneBuilder.ScenePath))
                Assert.Ignore($"{GreyboxSceneBuilder.ScenePath} does not exist, so there is no rig to compare with.");

            // The forest rig is a copy of the greybox rig, because the greybox builder may not be edited. This keeps the copy honest.
            // N1: the forest's player carries a torch the greybox's does not (the greybox does not change in that step). The torch,
            // CameraPivot/Flashlight and everything on it, is the one difference allowed; Flashlight_IsOnTheForestPlayer tests it.
            Scene greybox = EditorSceneManager.OpenScene(GreyboxSceneBuilder.ScenePath, OpenSceneMode.Single);
            GameObject greyboxPlayer = ForestTestSupport.Root(greybox, "Player");
            Assert.IsNotNull(greyboxPlayer, "The greybox scene has no Player.");
            Assert.AreEqual(0, greyboxPlayer.GetComponentsInChildren<PlayerFlashlight>(true).Length, "The greybox's player has a torch; the greybox must not change in N1.");
            List<string> expected = DescribeRig(greyboxPlayer, null);

            Scene forest = EditorSceneManager.OpenScene(ForestPaths.ScenePath, OpenSceneMode.Single);
            GameObject forestPlayer = ForestTestSupport.RequireRoot(forest, "Player");
            PlayerFlashlight[] torches = forestPlayer.GetComponentsInChildren<PlayerFlashlight>(true);
            Assert.AreEqual(1, torches.Length, "The forest's player must carry exactly one torch.");
            Assert.AreSame(forestPlayer.transform.Find("CameraPivot"), torches[0].transform.parent, "The torch must hang on the camera pivot.");
            Assert.AreEqual(0, torches[0].transform.childCount, "The torch's object must hold nothing else of the rig.");
            List<string> actual = DescribeRig(forestPlayer, torches[0].transform);

            var problems = new List<string>();
            foreach (string line in expected)
                if (!actual.Contains(line))
                    problems.Add("only in the greybox:  " + line);
            foreach (string line in actual)
                if (!expected.Contains(line))
                    problems.Add("only in the forest:   " + line);
            ForestTestSupport.AssertNone(problems, "difference(s) between the forest player rig and the greybox player rig");

            // The session object: a GameSession that knows the player. No HUD in the forest, on purpose.
            GameObject game = ForestTestSupport.RequireRoot(forest, "Game");
            var session = game.GetComponent<GameSession>();
            Assert.IsNotNull(session, "The forest's Game object has no GameSession.");
            Assert.AreSame(forestPlayer.GetComponent<PlayerController>(), session.Player, "The forest's GameSession is not wired to the forest's player.");
            Assert.AreEqual("MainCamera", forestPlayer.transform.Find("CameraPivot").tag, "The player camera must be the main camera.");
        }

        // One line per fact about the rig, so two rigs can be compared as sets and every difference is named. The part
        // <paramref name="leaveOut"/> (the forest's torch) and everything below it are not described.
        static List<string> DescribeRig(GameObject player, Transform leaveOut)
        {
            var lines = new List<string>();
            foreach (Transform part in player.GetComponentsInChildren<Transform>(true))
            {
                if (leaveOut != null && part.IsChildOf(leaveOut))
                    continue;
                string path = part == player.transform ? "Player" : "Player/" + AnimationUtility.CalculateTransformPath(part, player.transform);
                lines.Add($"{path}: tag {part.tag}, layer {part.gameObject.layer}, active {part.gameObject.activeSelf}");
                // The root stands at each scene's own spawn point; everything below it must sit the same.
                if (part != player.transform)
                    lines.Add($"{path}: local position {part.localPosition.ToString("0.###")}, rotation {part.localEulerAngles.ToString("0.#")}, scale {part.localScale.ToString("0.###")}");

                foreach (Component component in part.GetComponents<Component>())
                {
                    if (component == null)
                    {
                        lines.Add($"{path}: a missing script");
                        continue;
                    }
                    if (component is Transform)
                        continue;
                    string type = component.GetType().FullName;
                    lines.Add($"{path}: has {type}");

                    if (component is CharacterController body)
                        lines.Add($"{path}: body height {body.height}, radius {body.radius}, centre {body.center.ToString("0.###")}, slope limit {body.slopeLimit}, " +
                            $"step {body.stepOffset}, skin {body.skinWidth}, minimum move {body.minMoveDistance}");
                    else if (component is Camera camera)
                        lines.Add($"{path}: camera field of view {camera.fieldOfView}, near {camera.nearClipPlane}, far {camera.farClipPlane}, orthographic {camera.orthographic}, " +
                            $"culling mask {camera.cullingMask}, depth {camera.depth}");
                    else if (component is MonoBehaviour behaviour && type.StartsWith("Gamex.", StringComparison.Ordinal))
                        DescribeFields(path, behaviour, player.transform, lines);
                    // HDAdditionalCameraData is compared by presence only: the forest sets anti-aliasing on purpose.
                }
            }
            return lines;
        }

        static void DescribeFields(string path, MonoBehaviour behaviour, Transform player, List<string> lines)
        {
            var serialized = new SerializedObject(behaviour);
            SerializedProperty property = serialized.GetIterator();
            for (bool enter = true; property.NextVisible(enter); enter = false)
            {
                if (property.name == "m_Script")
                    continue;
                string value;
                switch (property.propertyType)
                {
                    case SerializedPropertyType.Float: value = property.floatValue.ToString("R", CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.Integer: value = property.intValue.ToString(CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.Boolean: value = property.boolValue.ToString(); break;
                    case SerializedPropertyType.String: value = property.stringValue; break;
                    case SerializedPropertyType.Enum: value = property.enumValueIndex.ToString(CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.Vector2: value = property.vector2Value.ToString("0.###"); break;
                    case SerializedPropertyType.Vector3: value = property.vector3Value.ToString("0.###"); break;
                    case SerializedPropertyType.ObjectReference:
                        // A reference into the rig is named by its path; anything else by its type.
                        Object target = property.objectReferenceValue;
                        Transform inside = target is Component c ? c.transform : target is GameObject g ? g.transform : null;
                        if (target == null) value = "nothing";
                        else if (inside != null && inside.IsChildOf(player)) value = "Player/" + AnimationUtility.CalculateTransformPath(inside, player);
                        else value = "an object outside the rig of type " + target.GetType().Name;
                        break;
                    default: value = "(" + property.propertyType + ", not compared)"; break;
                }
                lines.Add($"{path}: {behaviour.GetType().Name}.{property.name} = {value}");
            }
        }
    }
}
