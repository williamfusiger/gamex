using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Gamex.EditorTools
{
    /// <summary>
    /// Looks at the built forest the way a suspicious reviewer would, without rendering anything: pink
    /// or magenta materials, floating props, a blocked lane, a spawn inside a rock, two suns. It runs at
    /// the end of every scene build and throws there, so a scene that is wrong never counts as built.
    ///
    /// The check names below are stable: tests and people filter findings by them.
    ///   scene.forest, scene.structure, plan.file
    ///   material.missing, material.shader, material.pipeline, material.shader-error,
    ///   material.diffusion-profile, material.texture-slots, texture.normal-type, texture.mask-linear
    ///   prefab.bounds
    ///   placement.grounding, placement.instance, placement.tree-base
    ///   spawn.position, spawn.clearance
    ///   terrain.trail-slope, terrain.core-slope
    ///   lighting.volume, lighting.shadow-light, lighting.sky, lighting.exposure
    ///   scene.source-assets, scene.property-block, scene.batching-static
    ///   budget.instances, budget.gameobjects, budget.materials, budget.meshes, budget.textures-4096, budget.texture-size
    ///   corridor.analytic, corridor.physical
    /// </summary>
    public static class ForestAudit
    {
        [Serializable] public sealed class Finding { public string severity, check, message; }   // severity: "error" | "warning" | "info"

        [Serializable]
        public sealed class Report
        {
            public bool passed; public string apv, preset; public int seed; public Finding[] findings = Array.Empty<Finding>();

            // ---- counts, totals and metrics. Only this package relies on anything below. ----

            [Serializable] public sealed class ClassCount { public string cls; public int planned, instantiated; }
            [Serializable] public sealed class Metric { public string name; public float value, warnAt, budget; }
            [Serializable] public sealed class Timing { public string stage; public float seconds; }
            /// <summary>A centre line sampled every metre. The PlayMode suite sweeps its corridor capsule along these.</summary>
            [Serializable] public sealed class Polyline { public string name; public Vector2[] points = Array.Empty<Vector2>(); }

            public string kind;                      // "scene" | "kit"
            public string scenePath, unityVersion, writtenUtc;
            public int errors, warnings;
            public bool planGrounded;
            public int placements, instances, gameObjects, renderers, lodGroups, colliders;
            public int uniqueMaterials, uniqueMeshes, textures4096, terrainLayers;
            public long trianglesAtLod0;
            public ClassCount[] classCounts = Array.Empty<ClassCount>();
            public Metric[] budgets = Array.Empty<Metric>();

            public float maxGroundingErrorMetres;
            public float maxTrailSlopeDegrees, maxCoreSlopeDegrees;
            public float spawnHeightAboveTerrain;
            public float minSpawnMarginMetres;       // smallest "distance to the spawn minus clearance" over all colliders
            public float minCorridorMarginMetres;    // smallest "distance to a trail minus clearance" over all colliders
            public string physicalCorridors;         // "free" | "blocked" | "not run: <why>"
            public float kitBakeSeconds;             // sum over the kit report
            public string textureCompressionOverride;
            public Timing[] timings = Array.Empty<Timing>();
            public Polyline[] corridors = Array.Empty<Polyline>();
        }

        const string Error = "error", Warning = "warning", Info = "info";

        // What HDRP draws with when a material cannot be rendered. All three show as pink.
        static readonly string[] ErrorShaders = { "Hidden/InternalErrorShader", "Hidden/HDRP/FallbackError", "Hidden/HDRP/MaterialError" };

        // HDRP/Lit material types that read a diffusion profile (MaterialId.LitSSS and LitTranslucent).
        const int MaterialIdSubsurface = 0, MaterialIdTranslucent = 5;

        const string TintSuffix = "__tint1", TriplanarSuffix = "__triplanar";

        // One finding per check with a count and a few examples reads better than 2,700 findings.
        const int MaxExamples = 5;

        /// <summary>
        /// Audits the open scene and writes the build report. This is the only code that writes
        /// <see cref="ForestPaths.ReportPath"/>. With <paramref name="throwOnError"/> any finding of
        /// severity "error" throws after the report is on disk.
        /// </summary>
        public static Report Run(bool throwOnError)
        {
            var total = System.Diagnostics.Stopwatch.StartNew();
            var collector = new Collector();
            Scene scene = SceneManager.GetActiveScene();
            Report report = NewReport("scene");
            report.scenePath = scene.path;

            bool isForest = scene.path == ForestPaths.ScenePath;
            if (!isForest)
                collector.Add(Error, "scene.forest",
                    $"The open scene is '{scene.path}', not {ForestPaths.ScenePath}. Its findings are returned but the build report is left alone.");

            SceneRefs refs = FindSceneObjects(scene, collector);
            ForestPlan plan = LoadPlan(collector);
            report.seed = plan != null ? plan.seed : 0;
            report.planGrounded = plan != null && plan.grounded;
            report.preset = ForestLighting.ActivePreset();
            report.apv = ProbeDataState();

            collector.Time("materials", () => CheckSceneMaterials(refs, collector, report));
            collector.Time("prefab bounds", () => CheckPrefabBounds(collector));
            if (plan != null && refs.terrain != null)
            {
                collector.Time("placements", () => CheckPlacements(refs, plan, collector, report));
                collector.Time("spawn", () => CheckSpawn(refs, plan, collector, report));
                collector.Time("corridors", () => CheckCorridors(refs, plan, collector, report));
            }
            if (refs.terrain != null)
                collector.Time("slopes", () => CheckSlopes(refs.terrain, collector, report));
            collector.Time("lighting", () => CheckLighting(scene, report.preset, collector));
            collector.Time("renderers", () => CheckEnvironmentRenderers(refs, collector));
            collector.Time("totals", () => CheckTotals(scene, refs, plan, collector, report));

            ForestKitReport kit = ForestKitReport.Load();
            if (kit != null)
                foreach (ForestKitPrefabReport prefab in kit.prefabs)
                    report.kitBakeSeconds += prefab.bakeSeconds;

            collector.AddTiming("audit", (float)total.Elapsed.TotalSeconds);
            Finish(report, collector);

            if (isForest)
                WriteReport(report);

            string summary = Summary(report);
            if (report.passed)
                Debug.Log(summary);
            else if (throwOnError)
                throw new InvalidOperationException(summary);
            else
                // Not an error log: a test that calls Run(false) asserts on the report and words its own failure.
                Debug.LogWarning(summary);
            return report;
        }

        // Rewritten only when what it says has changed. An audit that is run again on an unchanged scene (the test
        // Scene_PassesAudit does that) then leaves the file inside Assets alone.
        static void WriteReport(Report report)
        {
            string json = JsonUtility.ToJson(report, true);
            if (File.Exists(ForestPaths.ReportPath))
            {
                Report previous = JsonUtility.FromJson<Report>(File.ReadAllText(ForestPaths.ReportPath));
                if (previous != null)
                {
                    // The moment and the durations differ on every run and are not findings.
                    previous.writtenUtc = report.writtenUtc;
                    previous.timings = report.timings;
                    if (JsonUtility.ToJson(previous, true) == json)
                        return;
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(ForestPaths.ReportPath));
            File.WriteAllText(ForestPaths.ReportPath, json);
        }

        /// <summary>
        /// Audits what the kit build produced, without a scene: every kit material and terrain layer, the
        /// texture sizes, and each prefab's size against the manifest. Writes nothing.
        /// </summary>
        public static Report AuditKit()
        {
            var total = System.Diagnostics.Stopwatch.StartNew();
            var collector = new Collector();
            Report report = NewReport("kit");
            report.preset = "";
            report.apv = "none";

            var materials = new List<Material>();
            if (AssetDatabase.IsValidFolder(ForestPaths.KitMaterials))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { ForestPaths.KitMaterials }))
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    if (material != null)
                        materials.Add(material);
                }
            }

            Material terrainMaterial = ForestMaterials.GetTerrainMaterial();
            if (terrainMaterial != null && !materials.Contains(terrainMaterial))
                materials.Add(terrainMaterial);
            TerrainLayer[] layers = ForestMaterials.GetTerrainLayers() ?? Array.Empty<TerrainLayer>();

            if (materials.Count == 0 && layers.Length == 0)
                collector.Add(Info, "material.missing", "No kit material or terrain layer has been built yet; nothing to audit.");

            collector.Time("materials", () =>
            {
                foreach (Material material in materials)
                    CheckMaterial(material, AssetDatabase.GetAssetPath(material), collector);
                CheckTerrainLayers(layers, collector);
            });
            collector.Time("prefab bounds", () => CheckPrefabBounds(collector));
            collector.Time("textures", () => CheckImportedTextureSizes(collector, report));

            report.uniqueMaterials = materials.Count;
            report.terrainLayers = layers.Length;
            collector.AddTiming("audit", (float)total.Elapsed.TotalSeconds);
            Finish(report, collector);
            return report;
        }

        [MenuItem("Gamex/Forest/Audit Open Scene")]
        public static void RunFromMenu()
        {
            // A batch run must end with a failing exit code when the scene is wrong; a menu run reports and returns.
            Report report = Run(Application.isBatchMode);
            if (report.passed)
                return;
            foreach (Finding finding in report.findings)
                if (finding.severity == Error)
                    Debug.LogError($"Forest audit [{finding.check}] {finding.message}");
        }

        // ------------------------------------------------------------------------------------------------
        // Scene objects
        // ------------------------------------------------------------------------------------------------

        sealed class SceneRefs
        {
            public GameObject environment, lighting, gameplay, player;
            public Terrain terrain;
            public Transform cells, landmarks, bounds;
        }

        static SceneRefs FindSceneObjects(Scene scene, Collector collector)
        {
            var refs = new SceneRefs();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                switch (root.name)
                {
                    case "Environment": refs.environment = root; break;
                    case "Lighting": refs.lighting = root; break;
                    case "Gameplay": refs.gameplay = root; break;
                    case "Player": refs.player = root; break;
                }
            }

            if (refs.environment == null)
                collector.Add(Error, "scene.structure", "The scene has no root object 'Environment'.");
            else
            {
                Transform environment = refs.environment.transform;
                Transform terrain = environment.Find("Terrain");
                refs.terrain = terrain != null ? terrain.GetComponent<Terrain>() : null;
                refs.cells = environment.Find("Cells");
                refs.landmarks = environment.Find("Landmarks");
                refs.bounds = environment.Find("Bounds");

                if (refs.terrain == null || refs.terrain.terrainData == null)
                {
                    refs.terrain = null;
                    collector.Add(Error, "scene.structure", "Environment/Terrain is missing or has no terrain data.");
                }
                if (refs.bounds == null)
                    collector.Add(Error, "scene.structure", "Environment/Bounds is missing: nothing keeps the player inside.");
                else if (refs.bounds.GetComponentsInChildren<BoxCollider>(true).Length != ExpectedWalls)
                    collector.Add(Warning, "scene.structure",
                        $"Environment/Bounds holds {refs.bounds.GetComponentsInChildren<BoxCollider>(true).Length} wall boxes, expected {ExpectedWalls}.");
                else if (refs.bounds.GetComponentsInChildren<Renderer>(true).Length > 0)
                    collector.Add(Warning, "scene.structure", "A boundary wall has a renderer; the walls are meant to be invisible.");
            }

            if (refs.player == null)
                collector.Add(Error, "scene.structure", "The scene has no root object 'Player'.");
            if (refs.lighting == null)
                collector.Add(Error, "scene.structure", "The scene has no root object 'Lighting'.");
            if (refs.gameplay == null || refs.gameplay.transform.Find("Viewpoints") == null)
                collector.Add(Warning, "scene.structure", "Gameplay/Viewpoints is missing: captures have no viewpoints to render from.");
            return refs;
        }

        // One wall per straight side and five chords per corner arc.
        const int ExpectedWalls = 24;

        static ForestPlan LoadPlan(Collector collector)
        {
            if (!File.Exists(ForestPaths.PlanPath))
            {
                collector.Add(Error, "plan.file", $"{ForestPaths.PlanPath} does not exist, so no placement can be checked.");
                return null;
            }

            ForestPlan plan = JsonUtility.FromJson<ForestPlan>(File.ReadAllText(ForestPaths.PlanPath));
            if (plan == null || plan.placements == null)
            {
                collector.Add(Error, "plan.file", $"{ForestPaths.PlanPath} could not be read as a placement plan.");
                return null;
            }
            if (!plan.grounded)
                collector.Add(Error, "plan.file", "The placement plan is not grounded: its heights were never filled in from the final terrain.");
            return plan;
        }

        // "baked" once the M5 bake stage exists and reports data for the open scene. ForestProbeBake is not part
        // of version 1, so it is looked up by name: the audit then needs no edit when that file arrives.
        static string ProbeDataState()
        {
            Type bake = typeof(ForestAudit).Assembly.GetType("Gamex.EditorTools.ForestProbeBake");
            var method = bake?.GetMethod("HasBakedData", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, Type.EmptyTypes, null);
            if (method == null || method.ReturnType != typeof(bool))
                return "none";
            return (bool)method.Invoke(null, null) ? "baked" : "none";
        }

        // ------------------------------------------------------------------------------------------------
        // Materials and textures
        // ------------------------------------------------------------------------------------------------

        static void CheckSceneMaterials(SceneRefs refs, Collector collector, Report report)
        {
            var seen = new HashSet<Material>();
            var missing = new Tally();
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] materials = renderer.sharedMaterials;
                    if (materials.Length == 0)
                        missing.Add($"{PathOf(renderer.transform)} has no material slot");
                    for (int i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] == null)
                            missing.Add($"{PathOf(renderer.transform)} slot {i}");
                        else if (seen.Add(materials[i]))
                            CheckMaterial(materials[i], PathOf(renderer.transform), collector);
                    }
                }
            }
            missing.Emit(collector, Error, "material.missing", "renderer material slot(s) are empty");

            if (refs.terrain == null)
                return;
            Material terrainMaterial = refs.terrain.materialTemplate;
            if (terrainMaterial == null)
                collector.Add(Error, "material.missing", "The terrain has no material template.");
            else if (seen.Add(terrainMaterial))
                CheckMaterial(terrainMaterial, "Environment/Terrain", collector);

            TerrainLayer[] layers = refs.terrain.terrainData.terrainLayers ?? Array.Empty<TerrainLayer>();
            report.terrainLayers = layers.Length;
            if (layers.Length == 0)
                collector.Add(Error, "material.texture-slots", "The terrain has no layers: it would render without any ground texture.");
            CheckTerrainLayers(layers, collector);
        }

        static void CheckMaterial(Material material, string usedBy, Collector collector)
        {
            string name = MaterialLabel(material);
            Shader shader = material.shader;
            if (shader == null || Array.IndexOf(ErrorShaders, shader.name) >= 0)
            {
                collector.Add(Error, "material.shader", $"{name} uses {(shader == null ? "no shader" : shader.name)} and would render pink (used by {usedBy}).");
                return;
            }
            if (!UsesHdrpShader(material, out string pipelineTag))
                collector.Add(Error, "material.pipeline", $"{name} uses {shader.name}, which is not an HDRP shader: its RenderPipeline tag is '{pipelineTag}' (used by {usedBy}).");
            if (ShaderUtil.ShaderHasError(shader))
                collector.Add(Error, "material.shader-error", $"{name}: shader {shader.name} has compile errors.");

            // A material type that reads a diffusion profile, without one, is tinted magenta by HDRP.
            if (material.HasProperty("_MaterialID"))
            {
                int type = Mathf.RoundToInt(material.GetFloat("_MaterialID"));
                bool needsProfile = type == MaterialIdSubsurface || type == MaterialIdTranslucent;
                if (needsProfile && (!material.HasProperty("_DiffusionProfileHash") || material.GetFloat("_DiffusionProfileHash") == 0f))
                    collector.Add(Error, "material.diffusion-profile",
                        $"{name} is translucent or subsurface but has no diffusion profile; HDRP would tint it magenta. It must carry a profile or be of type Standard.");
            }

            CheckTextureSlot(material, name, "_NormalMap", "_NORMALMAP", collector);
            CheckTextureSlot(material, name, "_MaskMap", "_MASKMAP", collector);

            // Which slots a kit material must fill is what the manifest says it has maps for.
            ForestMaterialEntry entry = ManifestEntryOf(material);
            if (entry == null || !material.HasProperty("_BaseColorMap"))
                return;
            ForestMaterialMaps maps = entry.maps;
            if (maps == null)
                return;
            if ((!string.IsNullOrEmpty(maps.basecolor) || !string.IsNullOrEmpty(maps.diffuse)) && material.GetTexture("_BaseColorMap") == null)
                collector.Add(Error, "material.texture-slots", $"{name} has no base colour map although the manifest lists one.");
            if (!string.IsNullOrEmpty(maps.normal) && material.GetTexture("_NormalMap") == null)
                collector.Add(Error, "material.texture-slots", $"{name} has no normal map although the manifest lists one.");
            if (!string.IsNullOrEmpty(maps.arm) && material.GetTexture("_MaskMap") == null)
                collector.Add(Error, "material.texture-slots", $"{name} has no mask map although the manifest lists an arm map to pack.");
        }

        // The tag of the sub-shader the engine picked for this pipeline. Until the pipeline instance exists (it is created
        // by the first render of an Editor session, and the audit renders nothing) the engine has picked none and answers
        // with the default, as measured at M0 for every HDRP/Lit material. Then the shader's own declaration is read instead.
        static bool UsesHdrpShader(Material material, out string pipelineTag)
        {
            const string hdrp = "HDRenderPipeline";
            pipelineTag = material.GetTag("RenderPipeline", false, "");
            if (pipelineTag == hdrp)
                return true;
            if (RenderPipelineManager.currentPipeline != null)
                return false;

            Shader shader = material.shader;
            var tagName = new ShaderTagId("RenderPipeline");
            for (int i = 0; i < shader.subshaderCount; i++)
            {
                string declared = shader.FindSubshaderTagValue(i, tagName).name;
                if (declared == hdrp)
                {
                    pipelineTag = declared;
                    return true;
                }
                if (!string.IsNullOrEmpty(declared))
                    pipelineTag = declared;
            }
            return false;
        }

        // A texture in a slot whose keyword is off is not sampled: HDRP derives the keyword from the slot when it
        // validates the material, so this means the texture was assigned after validation.
        static void CheckTextureSlot(Material material, string name, string property, string keyword, Collector collector)
        {
            if (!material.HasProperty(property))
                return;
            Texture texture = material.GetTexture(property);
            if (texture == null)
                return;

            if (material.shader.name == "HDRP/Lit" && !material.IsKeywordEnabled(keyword))
                collector.Add(Error, "material.texture-slots", $"{name} has a texture in {property} but keyword {keyword} is off, so it is not used.");
            if (property == "_NormalMap")
                CheckNormalTexture(texture, name, collector);
            else
                CheckMaskTexture(texture, name, collector);
        }

        static void CheckTerrainLayers(TerrainLayer[] layers, Collector collector)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                TerrainLayer layer = layers[i];
                if (layer == null)
                {
                    collector.Add(Error, "material.texture-slots", $"Terrain layer {i} is missing.");
                    continue;
                }

                string name = $"terrain layer {i} ({layer.name})";
                if (layer.diffuseTexture == null)
                    collector.Add(Error, "material.texture-slots", $"{name} has no diffuse texture.");
                if (layer.normalMapTexture == null)
                    collector.Add(Error, "material.texture-slots", $"{name} has no normal map.");
                else
                    CheckNormalTexture(layer.normalMapTexture, name, collector);
                if (layer.maskMapTexture == null)
                    collector.Add(Error, "material.texture-slots", $"{name} has no mask map.");
                else
                    CheckMaskTexture(layer.maskMapTexture, name, collector);
            }
        }

        static void CheckNormalTexture(Texture texture, string usedBy, Collector collector)
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.NormalMap)
                collector.Add(Error, "texture.normal-type", $"{importer.assetPath} is the normal map of {usedBy} but is imported as {importer.textureType}.");
        }

        static void CheckMaskTexture(Texture texture, string usedBy, Collector collector)
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
            if (importer != null && importer.sRGBTexture)
                collector.Add(Error, "texture.mask-linear", $"{importer.assetPath} is the mask map of {usedBy} but is imported as sRGB; mask data must be linear.");
        }

        // The manifest material a kit material asset was built from: its file name without the variant suffix.
        static ForestMaterialEntry ManifestEntryOf(Material material)
        {
            string path = AssetDatabase.GetAssetPath(material);
            if (string.IsNullOrEmpty(path) || !path.StartsWith(ForestPaths.KitMaterials + "/", StringComparison.Ordinal))
                return null;

            string name = Path.GetFileNameWithoutExtension(path);
            if (name.EndsWith(TintSuffix, StringComparison.Ordinal))
                name = name.Substring(0, name.Length - TintSuffix.Length);
            else if (name.EndsWith(TriplanarSuffix, StringComparison.Ordinal))
                name = name.Substring(0, name.Length - TriplanarSuffix.Length);

            ForestMaterialEntry entry = ForestManifest.FindMaterial(name);
            // A material that reuses another has no maps of its own; the one it points at has.
            if (entry != null && !string.IsNullOrEmpty(entry.sameAs))
                entry = ForestManifest.FindMaterial(entry.sameAs);
            return entry;
        }

        static string MaterialLabel(Material material)
        {
            string path = AssetDatabase.GetAssetPath(material);
            return string.IsNullOrEmpty(path) ? $"material '{material.name}' (not an asset)" : path;
        }

        // Every imported texture of the forest, by importer: nothing above 4096 and only a few at 4096.
        static void CheckImportedTextureSizes(Collector collector, Report report)
        {
            var folders = new List<string>();
            if (AssetDatabase.IsValidFolder(ForestPaths.PolyHavenRoot)) folders.Add(ForestPaths.PolyHavenRoot);
            if (AssetDatabase.IsValidFolder(ForestPaths.KitTextures)) folders.Add(ForestPaths.KitTextures);
            if (folders.Count == 0)
                return;

            int at4096 = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture", folders.ToArray()))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;

                importer.GetSourceTextureWidthAndHeight(out int width, out int height);
                int imported = Mathf.Min(Mathf.Max(width, height), importer.maxTextureSize);
                if (imported > ForestBudgets.LargestTextureSize)
                    collector.Add(Error, "budget.texture-size", $"{path} is imported at {imported}, above the limit of {ForestBudgets.LargestTextureSize}.");
                // The sky is a 4k lat-long turned into a cubemap; the budget row is about surface textures.
                if (imported >= 4096 && importer.textureShape == TextureImporterShape.Texture2D)
                    at4096++;
            }

            report.textures4096 = at4096;
            AddBudget(collector, report, "budget.textures-4096", "textures imported at 4096", at4096,
                ForestBudgets.Textures4096Warn, ForestBudgets.Textures4096Budget);
        }

        // ------------------------------------------------------------------------------------------------
        // Prefabs
        // ------------------------------------------------------------------------------------------------

        static void CheckPrefabBounds(Collector collector)
        {
            foreach (string prefabId in AllPrefabIds())
            {
                if (!ForestPrefabs.Exists(prefabId))
                    continue;
                ForestObjectEntry entry = ForestManifest.FindObjectForPrefab(prefabId);
                if (entry == null)
                    continue;   // patch prefabs are merged from clumps and have no source object to compare with

                // A log pitched on purpose (section 7.3) no longer has the box of its scan.
                if (Array.IndexOf(ForestPrefabIds.Logs, prefabId) >= 0 && ForestKitSpec.LogPitchDegrees != 0f)
                {
                    collector.Add(Warning, "prefab.bounds",
                        $"{prefabId} is pitched by {ForestKitSpec.LogPitchDegrees.ToString(CultureInfo.InvariantCulture)} degrees, so its size is not compared with the manifest.");
                    continue;
                }

                Bounds source = ForestManifest.SourceBounds(entry);
                CheckLevelBounds(prefabId, 0, source, collector);
                // With a scan level in front, level 0 is the scan itself; the first mesh our code made is level 1.
                if (ForestKitSpec.ScanLodDistance(prefabId) > 0f)
                    CheckLevelBounds(prefabId, 1, source, collector);
            }
        }

        static void CheckLevelBounds(string prefabId, int level, Bounds source, Collector collector)
        {
            Mesh mesh = ForestPrefabs.LoadLodMesh(prefabId, level);
            if (mesh == null)
            {
                collector.Add(Error, "prefab.bounds", $"{prefabId} is built but its level {level} mesh cannot be loaded.");
                return;
            }
            if (!ForestMeshTools.MatchesSourceBounds(mesh.bounds, source, out string mirror))
                collector.Add(Error, "prefab.bounds",
                    $"{prefabId} level {level} measures {Describe(mesh.bounds)} but the manifest says {Describe(source)} (mirror: {mirror}). Wrong scale, a tree on its side, or a pack offset.");
        }

        static IEnumerable<string> AllPrefabIds()
        {
            string[][] groups =
            {
                ForestPrefabIds.TallFirs, ForestPrefabIds.YoungFirs, ForestPrefabIds.Boulders, ForestPrefabIds.Logs,
                ForestPrefabIds.Stumps, ForestPrefabIds.Branches, ForestPrefabIds.FernPatches, ForestPrefabIds.FernClumps,
                ForestPrefabIds.GrassPatches,
            };
            foreach (string[] group in groups)
                foreach (string prefabId in group)
                    yield return prefabId;
        }

        // ------------------------------------------------------------------------------------------------
        // Placements
        // ------------------------------------------------------------------------------------------------

        static void CheckPlacements(SceneRefs refs, ForestPlan plan, Collector collector, Report report)
        {
            Terrain terrain = refs.terrain;
            ForestPlacement[] placements = plan.placements;
            Dictionary<int, Transform> instances = CollectInstances(refs, placements.Length, collector);
            report.placements = placements.Length;
            report.instances = instances.Count;

            var planned = new int[ClassNames.Length];
            var built = new int[ClassNames.Length];
            var floating = new Tally();
            var misplaced = new Tally();
            var absent = new Tally();
            var treeBases = new Tally();
            var prefabBuilt = new Dictionary<string, bool>();
            float worst = 0f;

            for (int i = 0; i < placements.Length; i++)
            {
                ForestPlacement placement = placements[i];
                int cls = (int)placement.cls;
                if (cls >= 0 && cls < planned.Length)
                    planned[cls]++;

                // Recomputed from the terrain as built, with the sink the planner recorded.
                float ground = TerrainHeight(terrain, placement.position.x, placement.position.z);
                float error = Mathf.Abs(placement.position.y - (ground - placement.sink));
                worst = Mathf.Max(worst, error);
                if (error > ForestBudgets.GroundingToleranceMetres)
                    floating.Add($"#{i} {placement.prefabId} at ({F(placement.position.x)}, {F(placement.position.z)}) is {F(error, "0.000")} m off");

                bool isTree = placement.cls == ForestClass.TallFir || placement.cls == ForestClass.YoungFir;
                if (isTree && !TrunkBaseIsInTheGround(terrain, placement))
                    treeBases.Add($"#{i} {placement.prefabId} at ({F(placement.position.x)}, {F(placement.position.z)})");

                if (instances.TryGetValue(i, out Transform instance))
                {
                    if (cls >= 0 && cls < built.Length)
                        built[cls]++;
                    string problem = InstanceProblem(instance, placement);
                    if (problem != null)
                        misplaced.Add($"#{i} {instance.name}: {problem}");
                }
                else
                {
                    // A class whose prefabs are not built yet is skipped by the scene builder; that is not a fault.
                    if (!prefabBuilt.TryGetValue(placement.prefabId, out bool exists))
                        prefabBuilt[placement.prefabId] = exists = ForestPrefabs.Exists(placement.prefabId);
                    if (exists)
                        absent.Add($"#{i} {placement.prefabId}");
                }
            }

            report.maxGroundingErrorMetres = worst;
            floating.Emit(collector, Error, "placement.grounding",
                $"placement(s) are not at terrain height minus their sink within {F(ForestBudgets.GroundingToleranceMetres)} m: floating, buried, or grounded on stale heights");
            treeBases.Emit(collector, Error, "placement.tree-base", "tree(s) have their pivot above the terrain somewhere on the trunk-base circle, so the trunk would show a gap");
            misplaced.Emit(collector, Error, "placement.instance", "instance(s) do not match their placement");
            absent.Emit(collector, Error, "placement.instance", "placement(s) have a built prefab but no instance in the scene");

            var counts = new List<Report.ClassCount>();
            for (int i = 0; i < ClassNames.Length; i++)
                if (planned[i] > 0 || built[i] > 0)
                    counts.Add(new Report.ClassCount { cls = ClassNames[i], planned = planned[i], instantiated = built[i] });
            report.classCounts = counts.ToArray();
        }

        static readonly string[] ClassNames = Enum.GetNames(typeof(ForestClass));

        // Instances are named <prefabId>__<index>, the index being the placement's position in placements.json.
        static Dictionary<int, Transform> CollectInstances(SceneRefs refs, int placementCount, Collector collector)
        {
            var instances = new Dictionary<int, Transform>();
            var strays = new Tally();
            foreach (Transform group in new[] { refs.cells, refs.landmarks })
            {
                if (group == null)
                    continue;
                foreach (Transform parent in group)
                {
                    foreach (Transform instance in parent)
                    {
                        if (!TryParseInstanceName(instance.name, out _, out int index))
                            strays.Add($"{PathOf(instance)} is not named <prefabId>__<index>");
                        else if (index >= placementCount)
                            strays.Add($"{PathOf(instance)} has no placement with that index");
                        else if (instances.ContainsKey(index))
                            strays.Add($"{PathOf(instance)} shares its index with {PathOf(instances[index])}");
                        else
                            instances.Add(index, instance);
                    }
                }
            }
            strays.Emit(collector, Error, "placement.instance", "object(s) under Cells or Landmarks cannot be matched to a placement");
            return instances;
        }

        internal static bool TryParseInstanceName(string name, out string prefabId, out int index)
        {
            prefabId = null;
            index = -1;
            int separator = name.LastIndexOf("__", StringComparison.Ordinal);
            if (separator <= 0)
                return false;
            prefabId = name.Substring(0, separator);
            return int.TryParse(name.Substring(separator + 2), NumberStyles.None, CultureInfo.InvariantCulture, out index);
        }

        static string InstanceProblem(Transform instance, ForestPlacement placement)
        {
            if (!TryParseInstanceName(instance.name, out string prefabId, out _) || prefabId != placement.prefabId)
                return $"the placement is a {placement.prefabId}";
            if (Vector3.Distance(instance.position, placement.position) > 0.001f)
                return $"stands at {instance.position.ToString("0.000")}, the placement is at {placement.position.ToString("0.000")}";
            if (Quaternion.Angle(instance.rotation, placement.rotation) > 0.05f)
                return "is rotated differently from its placement";
            Vector3 scale = instance.localScale;
            if (Mathf.Abs(scale.x - placement.scale) > 0.0005f || Mathf.Abs(scale.y - placement.scale) > 0.0005f || Mathf.Abs(scale.z - placement.scale) > 0.0005f)
                return $"has scale {scale.ToString("0.000")}, the placement says {F(placement.scale, "0.000")}";

            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(instance.gameObject);
            string expected = ForestPrefabs.PrefabPath(placement.prefabId);
            if (source == null || AssetDatabase.GetAssetPath(source) != expected)
                return $"is not an instance of {expected}";

            string parent = string.IsNullOrEmpty(placement.landmark) ? $"Cell_{placement.cellX}_{placement.cellZ}" : placement.landmark;
            if (instance.parent == null || instance.parent.name != parent)
                return $"is filed under '{(instance.parent != null ? instance.parent.name : "")}', expected '{parent}'";
            return null;
        }

        // A trunk on a slope shows a gap on its downhill side unless the pivot is below the ground all the way round.
        static bool TrunkBaseIsInTheGround(Terrain terrain, ForestPlacement placement)
        {
            const int points = 8;
            const float slack = 0.005f;
            float radius = placement.clearance;
            for (int i = 0; i < points; i++)
            {
                float angle = i * (2f * Mathf.PI / points);
                float x = placement.position.x + radius * Mathf.Cos(angle);
                float z = placement.position.z + radius * Mathf.Sin(angle);
                if (placement.position.y > TerrainHeight(terrain, x, z) + slack)
                    return false;
            }
            return true;
        }

        // ------------------------------------------------------------------------------------------------
        // Spawn and corridors
        // ------------------------------------------------------------------------------------------------

        static void CheckSpawn(SceneRefs refs, ForestPlan plan, Collector collector, Report report)
        {
            if (refs.player != null)
            {
                Vector3 position = refs.player.transform.position;
                var xz = new Vector2(position.x, position.z);
                if (Vector2.Distance(xz, ForestLayout.SpawnXZ) > 0.01f)
                    collector.Add(Error, "spawn.position", $"The player stands at ({F(xz.x)}, {F(xz.y)}), not at the spawn point ({F(ForestLayout.SpawnXZ.x)}, {F(ForestLayout.SpawnXZ.y)}).");

                float offTrail = ForestLayout.DistanceToSpline(ForestLayout.Trail, xz);
                if (offTrail > ForestBudgets.TrailTreadHalfWidthMetres)
                    collector.Add(Error, "spawn.position", $"The player starts {F(offTrail)} m from the trail centre line, off the tread.");

                float lift = position.y - TerrainHeight(refs.terrain, position.x, position.z);
                report.spawnHeightAboveTerrain = lift;
                if (Mathf.Abs(lift - ForestBudgets.SpawnLiftMetres) > ForestBudgets.SpawnLiftToleranceMetres)
                    collector.Add(Error, "spawn.position",
                        $"The player starts {F(lift, "0.000")} m above the terrain, expected {F(ForestBudgets.SpawnLiftMetres)} m: it would fall or start in the ground.");

                if (Mathf.Abs(Mathf.DeltaAngle(refs.player.transform.eulerAngles.y, ForestLayout.SpawnYaw)) > 0.1f)
                    collector.Add(Error, "spawn.position", $"The player faces {F(refs.player.transform.eulerAngles.y)} degrees, expected {F(ForestLayout.SpawnYaw)} (north along the trail).");
            }

            var crowded = new Tally();
            float smallest = float.PositiveInfinity;
            for (int i = 0; i < plan.placements.Length; i++)
            {
                ForestPlacement placement = plan.placements[i];
                if (placement.clearance <= 0f)
                    continue;
                float margin = Vector2.Distance(new Vector2(placement.position.x, placement.position.z), ForestLayout.SpawnXZ) - placement.clearance;
                smallest = Mathf.Min(smallest, margin);
                if (margin < ForestLayout.SpawnClearance - 0.001f)
                    crowded.Add($"#{i} {placement.prefabId} leaves {F(margin)} m");
            }
            report.minSpawnMarginMetres = float.IsInfinity(smallest) ? -1f : smallest;
            crowded.Emit(collector, Error, "spawn.clearance", $"collider(s) reach within {F(ForestLayout.SpawnClearance)} m of the spawn point");
        }

        static void CheckCorridors(SceneRefs refs, ForestPlan plan, Collector collector, Report report)
        {
            // Analytic, always: the planner keeps 2.5 m to every collider's surface, the agent needs 2.0 m.
            var blocked = new Tally();
            float smallest = float.PositiveInfinity;
            for (int i = 0; i < plan.placements.Length; i++)
            {
                ForestPlacement placement = plan.placements[i];
                if (placement.clearance <= 0f)
                    continue;
                float margin = ForestLayout.DistanceToAnyTrail(new Vector2(placement.position.x, placement.position.z)) - placement.clearance;
                smallest = Mathf.Min(smallest, margin);
                if (margin < ForestLayout.CorridorAgentRadius - 0.001f)
                    blocked.Add($"#{i} {placement.prefabId} leaves {F(margin)} m");
            }
            report.minCorridorMarginMetres = float.IsInfinity(smallest) ? -1f : smallest;
            blocked.Emit(collector, Error, "corridor.analytic", $"collider(s) reach within {F(ForestLayout.CorridorAgentRadius)} m of a trail centre line: a large agent cannot pass");

            report.corridors = new[]
            {
                SampleLine("Trail", ForestLayout.Trail),
                SampleLine("WestTrail", ForestLayout.WestTrail),
                SampleLine("EastTrail", ForestLayout.EastTrail),
            };

            // Physical, only when the M0 probe found that physics queries answer correctly in edit mode.
            if (!ForestProbe.EditModePhysicsIsReliable(out string why))
            {
                report.physicalCorridors = "not run: " + why;
                collector.Add(Info, "corridor.physical",
                    $"The capsule sweep along the trails was not run in edit mode ({why}). The PlayMode test Corridors_ArePhysicallyFree does the same sweep.");
                return;
            }

            Physics.SyncTransforms();
            float radius = ForestLayout.CorridorAgentRadius;
            float height = Mathf.Max(ForestBudgets.CorridorCapsuleHeightMetres, 2f * radius);
            Transform environment = refs.environment.transform;
            var touched = new Tally();
            foreach (Report.Polyline line in report.corridors)
            {
                foreach (Vector2 point in line.points)
                {
                    float ground = TerrainHeight(refs.terrain, point.x, point.y) + ForestBudgets.CorridorCapsuleLiftMetres;
                    var bottom = new Vector3(point.x, ground + radius, point.y);
                    var top = new Vector3(point.x, ground + height - radius, point.y);
                    foreach (Collider hit in Physics.OverlapCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore))
                    {
                        // The terrain is the floor of the lane, and only the static world can block it.
                        if (hit is TerrainCollider || !hit.transform.IsChildOf(environment))
                            continue;
                        touched.Add($"{PathOf(hit.transform)} on {line.name} near ({F(point.x)}, {F(point.y)})");
                    }
                }
            }
            report.physicalCorridors = touched.count == 0 ? "free" : "blocked";
            touched.Emit(collector, Error, "corridor.physical",
                $"collider contact(s) with a capsule of radius {F(radius)} m swept along the trails: something reaches further than its planned clearance");
        }

        static Report.Polyline SampleLine(string name, Vector2[] controlPoints)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(ForestLayout.SplineLength(controlPoints)));
            var points = new Vector2[steps + 1];
            for (int i = 0; i <= steps; i++)
                points[i] = ForestLayout.SampleSpline(controlPoints, i / (float)steps);
            return new Report.Polyline { name = name, points = points };
        }

        // ------------------------------------------------------------------------------------------------
        // Terrain slopes
        // ------------------------------------------------------------------------------------------------

        static void CheckSlopes(Terrain terrain, Collector collector, Report report)
        {
            // Along the trail: the gradient in the direction of travel, sampled every half metre.
            const float step = 0.5f;
            float length = ForestLayout.SplineLength(ForestLayout.Trail);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / step));
            float worstTrail = 0f;
            Vector2 worstTrailAt = default;
            Vector2 previous = ForestLayout.SampleSpline(ForestLayout.Trail, 0f);
            float previousHeight = TerrainHeight(terrain, previous.x, previous.y);
            for (int i = 1; i <= steps; i++)
            {
                Vector2 point = ForestLayout.SampleSpline(ForestLayout.Trail, i / (float)steps);
                float height = TerrainHeight(terrain, point.x, point.y);
                float run = Vector2.Distance(point, previous);
                if (run > 0.01f)
                {
                    float slope = Mathf.Atan(Mathf.Abs(height - previousHeight) / run) * Mathf.Rad2Deg;
                    if (slope > worstTrail)
                    {
                        worstTrail = slope;
                        worstTrailAt = point;
                    }
                }
                previous = point;
                previousHeight = height;
            }
            report.maxTrailSlopeDegrees = worstTrail;
            if (worstTrail > ForestBudgets.TrailSlopeLimitDegrees + ForestBudgets.SlopeReadbackToleranceDegrees)
                collector.Add(Error, "terrain.trail-slope",
                    $"The trail climbs at {F(worstTrail)} degrees near ({F(worstTrailAt.x)}, {F(worstTrailAt.y)}); the limit is {F(ForestBudgets.TrailSlopeLimitDegrees)}. A sprinting player would leave the ground.");

            // Inside the boundary: the slope of the surface itself, by central differences on the heightmap.
            TerrainData data = terrain.terrainData;
            int resolution = data.heightmapResolution;
            float[,] heights = data.GetHeights(0, 0, resolution, resolution);
            Vector3 origin = terrain.transform.position;
            float cellX = data.size.x / (resolution - 1);
            float cellZ = data.size.z / (resolution - 1);
            float metres = data.size.y;

            float worstCore = 0f;
            Vector2 worstCoreAt = default;
            int steep = 0;
            float limit = ForestBudgets.CoreSlopeLimitDegrees + ForestBudgets.SlopeReadbackToleranceDegrees;
            for (int z = 1; z < resolution - 1; z++)
            {
                float worldZ = origin.z + z * cellZ;
                if (Mathf.Abs(worldZ) > ForestLayout.HalfExtentZ)
                    continue;
                for (int x = 1; x < resolution - 1; x++)
                {
                    float worldX = origin.x + x * cellX;
                    if (Mathf.Abs(worldX) > ForestLayout.HalfExtentX || ForestLayout.SignedDistanceToBoundary(new Vector2(worldX, worldZ)) >= 0f)
                        continue;

                    float gradientX = (heights[z, x + 1] - heights[z, x - 1]) * metres / (2f * cellX);
                    float gradientZ = (heights[z + 1, x] - heights[z - 1, x]) * metres / (2f * cellZ);
                    float slope = Mathf.Atan(Mathf.Sqrt(gradientX * gradientX + gradientZ * gradientZ)) * Mathf.Rad2Deg;
                    if (slope > limit)
                        steep++;
                    if (slope > worstCore)
                    {
                        worstCore = slope;
                        worstCoreAt = new Vector2(worldX, worldZ);
                    }
                }
            }
            report.maxCoreSlopeDegrees = worstCore;
            if (steep > 0)
                collector.Add(Error, "terrain.core-slope",
                    $"{steep} heightmap sample(s) inside the boundary are steeper than {F(ForestBudgets.CoreSlopeLimitDegrees)} degrees; the steepest is {F(worstCore)} near ({F(worstCoreAt.x)}, {F(worstCoreAt.y)}).");
        }

        // ------------------------------------------------------------------------------------------------
        // Lighting
        // ------------------------------------------------------------------------------------------------

        static void CheckLighting(Scene scene, string activePreset, Collector collector)
        {
            var volumes = new List<Volume>();
            var suns = new List<Light>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Volume volume in root.GetComponentsInChildren<Volume>(false))
                    if (volume.isActiveAndEnabled && volume.isGlobal)
                        volumes.Add(volume);
                foreach (Light light in root.GetComponentsInChildren<Light>(false))
                    if (light.isActiveAndEnabled && light.type == LightType.Directional && light.shadows != LightShadows.None)
                        suns.Add(light);
            }

            if (volumes.Count != 1)
                collector.Add(Error, "lighting.volume", $"{volumes.Count} global volumes are active, expected exactly one: {Names(volumes)}.");
            if (suns.Count != 1)
                collector.Add(Error, "lighting.shadow-light", $"{suns.Count} shadow-casting directional lights are active, expected exactly one: {Names(suns)}.");
            if (volumes.Count != 1)
                return;

            VolumeProfile profile = volumes[0].sharedProfile;
            if (profile == null)
            {
                collector.Add(Error, "lighting.volume", $"The global volume {PathOf(volumes[0].transform)} has no profile.");
                return;
            }

            CheckSky(profile, activePreset, collector);
            CheckExposure(profile, activePreset, collector);
        }

        static void CheckSky(VolumeProfile profile, string activePreset, Collector collector)
        {
            if (!profile.TryGet(out HDRISky sky))
            {
                collector.Add(Error, "lighting.sky", $"Profile {profile.name} has no HDRI Sky override.");
                return;
            }
            if (!sky.hdriSky.overrideState || sky.hdriSky.value == null)
                collector.Add(Error, "lighting.sky", $"Profile {profile.name}: the HDRI Sky has no cubemap.");

            ForestLightingPresets.Preset preset = ForestLightingPresets.Get(activePreset);
            if (!sky.skyIntensityMode.overrideState)
            {
                collector.Add(Error, "lighting.sky", $"Profile {profile.name}: the sky intensity mode is not set, so the sky is not scaled to a known brightness.");
            }
            else if (sky.skyIntensityMode.value == SkyIntensityMode.Lux)
            {
                // Left at its default of 1, HDRP would multiply the sky by the raw lux number.
                float raw = sky.upperHemisphereLuxValue.value;
                if (!sky.upperHemisphereLuxValue.overrideState || !IsFinitePositive(raw))
                    collector.Add(Error, "lighting.sky", $"Profile {profile.name}: the sky's measured lux ({F(raw)}) is not set, finite and positive.");
                float wanted = sky.desiredLuxValue.value;
                if (!sky.desiredLuxValue.overrideState || !IsFinitePositive(wanted))
                    collector.Add(Error, "lighting.sky", $"Profile {profile.name}: the sky's wanted lux ({F(wanted)}) is not set, finite and positive.");
                else if (preset != null && !Mathf.Approximately(wanted, preset.skyLux))
                    collector.Add(Error, "lighting.sky", $"Profile {profile.name}: the sky is set to {F(wanted)} lux, preset {activePreset} says {F(preset.skyLux)}.");
            }
            else if (sky.skyIntensityMode.value == SkyIntensityMode.Multiplier)
            {
                float multiplier = sky.multiplier.value;
                if (!sky.multiplier.overrideState || !IsFinitePositive(multiplier))
                    collector.Add(Error, "lighting.sky", $"Profile {profile.name}: the sky multiplier ({F(multiplier)}) is not set, finite and positive.");
                else
                    collector.Add(Warning, "lighting.sky", $"Profile {profile.name}: the sky is scaled by a hand-set multiplier ({F(multiplier)}), the fallback for a failed lux calibration.");
            }
            else
            {
                collector.Add(Error, "lighting.sky", $"Profile {profile.name}: the sky uses intensity mode {sky.skyIntensityMode.value}; only Lux and the Multiplier fallback are calibrated.");
            }
        }

        static void CheckExposure(VolumeProfile profile, string activePreset, Collector collector)
        {
            ForestLightingPresets.Preset preset = ForestLightingPresets.Get(activePreset);
            if (preset == null)
            {
                collector.Add(Error, "lighting.exposure", $"The active lighting preset '{activePreset}' is not a known forest preset, so its exposure cannot be checked.");
                return;
            }
            if (!profile.TryGet(out Exposure exposure))
            {
                collector.Add(Error, "lighting.exposure", $"Profile {profile.name} has no Exposure override.");
                return;
            }

            ExposureMode expectedMode = preset.fixedExposure ? ExposureMode.Fixed : ExposureMode.AutomaticHistogram;
            if (!exposure.mode.overrideState || exposure.mode.value != expectedMode)
                collector.Add(Error, "lighting.exposure", $"Profile {profile.name}: exposure mode is {exposure.mode.value}, preset {activePreset} says {expectedMode}.");

            if (preset.fixedExposure)
            {
                CheckExposureValue(profile, "fixed exposure", exposure.fixedExposure, preset.exposureFixed, activePreset, collector);
            }
            else
            {
                CheckExposureValue(profile, "lower exposure limit", exposure.limitMin, preset.exposureMin, activePreset, collector);
                CheckExposureValue(profile, "upper exposure limit", exposure.limitMax, preset.exposureMax, activePreset, collector);
                // With curve remapping on, HDRP ignores both limits.
                if (exposure.histogramUseCurveRemapping.overrideState && exposure.histogramUseCurveRemapping.value)
                    collector.Add(Error, "lighting.exposure", $"Profile {profile.name}: histogram curve remapping is on, which makes HDRP ignore the exposure limits.");
            }
            CheckExposureValue(profile, "exposure compensation", exposure.compensation, preset.exposureCompensation, activePreset, collector);
        }

        static void CheckExposureValue(VolumeProfile profile, string what, FloatParameter parameter, float expected, string preset, Collector collector)
        {
            // A value without its override flag is not applied at all.
            if (!parameter.overrideState || Mathf.Abs(parameter.value - expected) > 0.0001f)
                collector.Add(Error, "lighting.exposure",
                    $"Profile {profile.name}: {what} is {(parameter.overrideState ? F(parameter.value) : "not overridden")}, preset {preset} says {F(expected)}.");
        }

        // ------------------------------------------------------------------------------------------------
        // Environment renderers and scene totals
        // ------------------------------------------------------------------------------------------------

        static void CheckEnvironmentRenderers(SceneRefs refs, Collector collector)
        {
            if (refs.environment == null)
                return;

            var sources = new Tally();
            var blocks = new Tally();
            var batching = new Tally();
            foreach (Renderer renderer in refs.environment.GetComponentsInChildren<Renderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && IsSourceAsset(filter.sharedMesh))
                    sources.Add($"{PathOf(renderer.transform)} draws mesh {AssetDatabase.GetAssetPath(filter.sharedMesh)}");
                foreach (Material material in renderer.sharedMaterials)
                    if (IsSourceAsset(material))
                        sources.Add($"{PathOf(renderer.transform)} uses material {AssetDatabase.GetAssetPath(material)}");

                if (renderer.HasPropertyBlock())
                    blocks.Add(PathOf(renderer.transform));
            }
            foreach (MeshCollider collider in refs.environment.GetComponentsInChildren<MeshCollider>(true))
                if (IsSourceAsset(collider.sharedMesh))
                    sources.Add($"{PathOf(collider.transform)} collides with mesh {AssetDatabase.GetAssetPath(collider.sharedMesh)}");
            foreach (Transform transform in refs.environment.GetComponentsInChildren<Transform>(true))
                if ((GameObjectUtility.GetStaticEditorFlags(transform.gameObject) & StaticEditorFlags.BatchingStatic) != 0)
                    batching.Add(PathOf(transform));

            sources.Emit(collector, Error, "scene.source-assets",
                $"reference(s) under Environment point into {ForestPaths.ThirdPartyRoot}: a scanned source mesh or its import material is in the scene");
            blocks.Emit(collector, Error, "scene.property-block", "renderer(s) under Environment carry a material property block, which the GPU Resident Drawer cannot take");
            batching.Emit(collector, Error, "scene.batching-static", "object(s) under Environment are flagged for static batching");
        }

        static bool IsSourceAsset(Object asset)
        {
            if (asset == null)
                return false;
            string path = AssetDatabase.GetAssetPath(asset);
            return !string.IsNullOrEmpty(path) && path.StartsWith(ForestPaths.ThirdPartyRoot + "/", StringComparison.Ordinal);
        }

        static void CheckTotals(Scene scene, SceneRefs refs, ForestPlan plan, Collector collector, Report report)
        {
            var materials = new HashSet<Material>();
            var meshes = new HashSet<Mesh>();
            var textures = new HashSet<Texture>();
            var triangles = new Dictionary<Mesh, long>();
            int gameObjects = 0, renderers = 0, lodGroups = 0, colliders = 0;
            long trianglesAtLod0 = 0;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                gameObjects += root.GetComponentsInChildren<Transform>(true).Length;
                colliders += root.GetComponentsInChildren<Collider>(true).Length;

                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    renderers++;
                    foreach (Material material in renderer.sharedMaterials)
                        if (material != null)
                            materials.Add(material);
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null)
                        meshes.Add(filter.sharedMesh);
                }

                foreach (LODGroup group in root.GetComponentsInChildren<LODGroup>(true))
                {
                    lodGroups++;
                    LOD[] levels = group.GetLODs();
                    if (levels.Length == 0)
                        continue;
                    foreach (Renderer renderer in levels[0].renderers)
                    {
                        // A shadows-only helper draws no picture triangles of its own.
                        if (renderer == null || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                            continue;
                        var filter = renderer.GetComponent<MeshFilter>();
                        if (filter != null && filter.sharedMesh != null)
                            trianglesAtLod0 += TriangleCount(filter.sharedMesh, triangles);
                    }
                }
            }

            if (refs.terrain != null)
            {
                if (refs.terrain.materialTemplate != null)
                    materials.Add(refs.terrain.materialTemplate);
                foreach (TerrainLayer layer in refs.terrain.terrainData.terrainLayers ?? Array.Empty<TerrainLayer>())
                {
                    if (layer == null)
                        continue;
                    if (layer.diffuseTexture != null) textures.Add(layer.diffuseTexture);
                    if (layer.normalMapTexture != null) textures.Add(layer.normalMapTexture);
                    if (layer.maskMapTexture != null) textures.Add(layer.maskMapTexture);
                }
            }
            foreach (Material material in materials)
            {
                if (material.shader == null)
                    continue;
                foreach (int property in material.GetTexturePropertyNameIDs())
                {
                    Texture texture = material.GetTexture(property);
                    if (texture != null)
                        textures.Add(texture);
                }
            }

            int at4096 = 0;
            foreach (Texture texture in textures)
            {
                int size = Mathf.Max(texture.width, texture.height);
                if (size > ForestBudgets.LargestTextureSize && texture is Texture2D)
                    collector.Add(Error, "budget.texture-size", $"{AssetDatabase.GetAssetPath(texture)} is {size} pixels, above the limit of {ForestBudgets.LargestTextureSize}.");
                if (size >= 4096 && texture is Texture2D)
                    at4096++;
            }

            report.gameObjects = gameObjects;
            report.renderers = renderers;
            report.lodGroups = lodGroups;
            report.colliders = colliders;
            report.uniqueMaterials = materials.Count;
            report.uniqueMeshes = meshes.Count;
            report.textures4096 = at4096;
            report.trianglesAtLod0 = trianglesAtLod0;
            report.textureCompressionOverride = EditorUserBuildSettings.overrideTextureCompression.ToString();
            if (plan == null)
                report.instances = CountInstances(refs);

            AddBudget(collector, report, "budget.instances", "prefab instances", report.instances, ForestBudgets.PrefabInstancesWarn, ForestBudgets.PrefabInstancesBudget);
            AddBudget(collector, report, "budget.gameobjects", "GameObjects", gameObjects, ForestBudgets.GameObjectsWarn, ForestBudgets.GameObjectsBudget);
            AddBudget(collector, report, "budget.materials", "unique materials", materials.Count, ForestBudgets.UniqueMaterialsWarn, ForestBudgets.UniqueMaterialsBudget);
            AddBudget(collector, report, "budget.meshes", "unique meshes", meshes.Count, ForestBudgets.UniqueMeshesWarn, ForestBudgets.UniqueMeshesBudget);
            AddBudget(collector, report, "budget.textures-4096", "textures at 4096", at4096, ForestBudgets.Textures4096Warn, ForestBudgets.Textures4096Budget);
        }

        static int CountInstances(SceneRefs refs)
        {
            int count = 0;
            foreach (Transform group in new[] { refs.cells, refs.landmarks })
                if (group != null)
                    foreach (Transform parent in group)
                        count += parent.childCount;
            return count;
        }

        static long TriangleCount(Mesh mesh, Dictionary<Mesh, long> cache)
        {
            if (cache.TryGetValue(mesh, out long count))
                return count;
            for (int i = 0; i < mesh.subMeshCount; i++)
                count += mesh.GetIndexCount(i) / 3;
            cache[mesh] = count;
            return count;
        }

        static void AddBudget(Collector collector, Report report, string check, string what, int value, int warnAt, int budget)
        {
            var metrics = new List<Report.Metric>(report.budgets) { new Report.Metric { name = check, value = value, warnAt = warnAt, budget = budget } };
            report.budgets = metrics.ToArray();
            if (value > budget)
                collector.Add(Error, check, $"{value} {what}, above the budget of {budget}.");
            else if (value > warnAt)
                collector.Add(Warning, check, $"{value} {what}, above the warning level of {warnAt} (budget {budget}).");
        }

        // ------------------------------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------------------------------

        /// <summary>World height of the terrain surface: SampleHeight is relative to the terrain's own position.</summary>
        internal static float TerrainHeight(Terrain terrain, float x, float z)
        {
            return terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
        }

        internal static string PathOf(Transform transform)
        {
            string path = transform.name;
            for (Transform parent = transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }

        static bool IsFinitePositive(float value) => value > 0f && !float.IsInfinity(value) && !float.IsNaN(value);

        static string F(float value, string format = "0.00") => value.ToString(format, CultureInfo.InvariantCulture);

        static string Describe(Bounds bounds) => $"size {bounds.size.ToString("0.000")}, min.y {F(bounds.min.y, "0.000")}, centre {bounds.center.ToString("0.000")}";

        static string Names<T>(List<T> components) where T : Component
        {
            if (components.Count == 0)
                return "none";
            var names = new string[components.Count];
            for (int i = 0; i < names.Length; i++)
                names[i] = PathOf(components[i].transform);
            return string.Join(", ", names);
        }

        static Report NewReport(string kind)
        {
            return new Report
            {
                kind = kind,
                unityVersion = Application.unityVersion,
                writtenUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                physicalCorridors = "not run: no placement plan or no terrain",
                textureCompressionOverride = EditorUserBuildSettings.overrideTextureCompression.ToString(),
            };
        }

        static void Finish(Report report, Collector collector)
        {
            report.findings = collector.Findings();
            report.timings = collector.Timings();
            report.errors = collector.Count(Error);
            report.warnings = collector.Count(Warning);
            report.passed = report.errors == 0;
        }

        static string Summary(Report report)
        {
            if (report.passed)
                return $"Forest audit passed: {report.instances} instances, {report.gameObjects} GameObjects, {report.uniqueMaterials} materials, " +
                    $"{report.uniqueMeshes} meshes, {report.warnings} warning(s). Preset {report.preset}, seed {report.seed}, probes {report.apv}.";

            var text = new System.Text.StringBuilder();
            text.Append($"Forest audit failed with {report.errors} error(s):");
            int shown = 0;
            foreach (Finding finding in report.findings)
            {
                if (finding.severity != Error)
                    continue;
                if (shown++ == 20)
                {
                    text.Append($"\n  ... and {report.errors - 20} more; see {ForestPaths.ReportPath}");
                    break;
                }
                text.Append($"\n  [{finding.check}] {finding.message}");
            }
            return text.ToString();
        }

        /// <summary>Counts occurrences of one fault and remembers the first few, to become a single finding.</summary>
        sealed class Tally
        {
            public int count;
            readonly List<string> examples = new List<string>();

            public void Add(string example)
            {
                count++;
                if (examples.Count < MaxExamples)
                    examples.Add(example);
            }

            public void Emit(Collector collector, string severity, string check, string what)
            {
                if (count == 0)
                    return;
                string more = count > examples.Count ? $" (first {examples.Count} shown)" : "";
                collector.Add(severity, check, $"{count} {what}{more}: {string.Join("; ", examples)}.");
            }
        }

        sealed class Collector
        {
            readonly List<Finding> findings = new List<Finding>();
            readonly List<Report.Timing> timings = new List<Report.Timing>();

            public void Add(string severity, string check, string message)
            {
                findings.Add(new Finding { severity = severity, check = check, message = message });
            }

            public void Time(string stage, Action action)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                action();
                AddTiming(stage, (float)watch.Elapsed.TotalSeconds);
            }

            public void AddTiming(string stage, float seconds) => timings.Add(new Report.Timing { stage = stage, seconds = seconds });

            public int Count(string severity)
            {
                int count = 0;
                foreach (Finding finding in findings)
                    if (finding.severity == severity)
                        count++;
                return count;
            }

            public Finding[] Findings() => findings.ToArray();
            public Report.Timing[] Timings() => timings.ToArray();
        }
    }
}
