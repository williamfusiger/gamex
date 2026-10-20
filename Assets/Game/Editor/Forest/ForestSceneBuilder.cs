using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace Gamex.EditorTools
{
    /// <summary>
    /// Generates the forest scene from code, headlessly or from the menu:
    /// <c>Unity -batchmode -projectPath . -executeMethod Gamex.EditorTools.ForestSceneBuilder.Build -quit</c>.
    /// It builds with whatever part of the kit exists, so it works from the first milestone on. The
    /// order of the stages is the one of spec section 11.5 and matters: heights are final before any
    /// pivot gets one, and the audit runs last, on the saved scene.
    /// </summary>
    public static class ForestSceneBuilder
    {
        public const string ScenePath = ForestPaths.ScenePath;

        [MenuItem("Gamex/Forest/3 Rebuild Forest Scene")]
        public static void Build()        // -forestSeed, -forestPreset, -forestLayers 4|8
        {
            if (ConfirmRebuild())
                BuildConfirmed();
        }

        /// <summary>Like the greybox, a menu-driven rebuild asks before it replaces the open scene. Batch mode does not ask.</summary>
        internal static bool ConfirmRebuild()
        {
            if (Application.isBatchMode) return true;

            bool confirmed = EditorUtility.DisplayDialog("Rebuild Forest Scene",
                "This regenerates the Forest scene from code. Anything edited by hand in that scene is lost.",
                "Rebuild", "Cancel");
            return confirmed && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        }

        internal static void BuildConfirmed()
        {
            int seed = IntArgument("-forestSeed", ForestLayout.Seed);
            string preset = ForestArgs.Value("-forestPreset", ForestLightingPresets.Default);
            int layers = IntArgument("-forestLayers", ForestLayout.TerrainLayerCount);
            if (layers != 4 && layers != 8)
                throw new ArgumentException($"-forestLayers must be 4 or 8, not {layers}.");

            // The layer count is a static that outlives this build (play mode is entered without a
            // domain reload), so an argument must not leave it changed.
            int layersBefore = ForestLayout.TerrainLayerCount;
            ForestLayout.TerrainLayerCount = layers;
            var watch = Stopwatch.StartNew();
            try
            {
                // 1. The question was asked by the caller.
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                // 2. Folders are created, never deleted: re-importing the kit would dominate the build.
                ForestTerrain.EnsureAssetFolder(ForestPaths.DataFolder);

                // 3. Heights are final except for root mounds.
                ForestFields fields = ForestTerrain.ComputeFields(seed);
                // 4. Where everything stands. It reads slopes and normals and writes no height.
                ForestPlan plan = ForestScatter.Plan(fields, seed);
                // 5. Heights are now final. The slope limits are asserted again in there.
                ForestTerrain.AddRootMounds(fields, plan);
                // 6. Only now does a placement get its height, mounds included.
                ForestScatter.Ground(plan, fields);
                File.WriteAllText(ForestPaths.PlanPath, JsonUtility.ToJson(plan, true));
                AssetDatabase.ImportAsset(ForestPaths.PlanPath);
                long planned = watch.ElapsedMilliseconds;

                // 7. Everything under this object is static level geometry and is what the NavMesh is baked from.
                var environment = new GameObject("Environment");
                Terrain terrain = ForestTerrain.Create(fields, plan, environment.transform);

                // 8.
                int instances = ForestWorldRig.Instantiate(plan, environment.transform);
                ForestWorldRig.BuildBounds(environment.transform);
                ForestWorldRig.BuildGameplayAnchors(terrain);
                // W1 I2: the interaction field and the density map (outside Environment: nothing in it is level geometry).
                ForestWindRig.BuildFoliage(plan);

                // 9.
                Physics.SyncTransforms();
                ForestWorldRig.BakeNavMesh(environment);

                // 10. The spawn height is read from the terrain itself, which is what the audit measures against.
                var spawn = new Vector3(ForestLayout.SpawnXZ.x, 0f, ForestLayout.SpawnXZ.y);
                spawn.y = terrain.SampleHeight(spawn) + terrain.transform.position.y + ForestLayout.SpawnLift;
                GameObject player = ForestWorldRig.BuildPlayer(spawn, ForestLayout.SpawnYaw);
                ForestWorldRig.BuildSession(player);
                ForestLighting.ConfigureCamera(player.GetComponentInChildren<Camera>());

                // 11.
                ForestLighting.Build(preset);

                // 12. Spec 11.5 appends the scene to Build Settings here. That rewrites
                // ProjectSettings/EditorBuildSettings.asset, a file that existed before the forest work and
                // that the build rules protect, so it happens only when asked for with
                // -forestAddToBuildSettings (issues/M1-build-settings.md). Nothing in the forest needs it:
                // the forest tests load the scene by path. When it is added it is appended and never moved,
                // so the greybox stays first, because its tests load it by name.
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException($"The forest scene could not be saved to {ScenePath}.");
                if (ForestArgs.Flag("-forestAddToBuildSettings"))
                    AppendToBuildSettings();
                else
                    Debug.Log("ForestSceneBuilder: the forest scene is not added to Build Settings (pass -forestAddToBuildSettings to append it).");
                AssetDatabase.SaveAssets();
                long built = watch.ElapsedMilliseconds;

                // 13. The audit throws on any error finding and is the only writer of the build report.
                ForestAudit.Run(throwOnError: true);

                Debug.Log($"Forest scene rebuilt: seed {seed}, preset {preset}, {layers} terrain layers asked for, " +
                    $"{plan.placements.Length} placements, {instances} instances. Plan {planned} ms, scene {built - planned} ms, " +
                    $"audit {watch.ElapsedMilliseconds - built} ms.");
            }
            finally
            {
                ForestLayout.TerrainLayerCount = layersBefore;
            }
        }

        static void AppendToBuildSettings()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            foreach (EditorBuildSettingsScene listed in scenes)
                if (listed.path == ScenePath) return;

            var extended = new EditorBuildSettingsScene[scenes.Length + 1];
            Array.Copy(scenes, extended, scenes.Length);
            extended[scenes.Length] = new EditorBuildSettingsScene(ScenePath, true);
            EditorBuildSettings.scenes = extended;
        }

        static int IntArgument(string name, int fallback)
        {
            string text = ForestArgs.Value(name);
            if (text == null) return fallback;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                throw new ArgumentException($"{name} needs a whole number, not '{text}'.");
            return value;
        }
    }
}
