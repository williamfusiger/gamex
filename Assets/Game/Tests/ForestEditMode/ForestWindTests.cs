using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gamex.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Gamex.Tests.Forest
{
    /// <summary>
    /// W1 package I1 (spec 8.6): the wind Shader Graphs, the wind materials and their switch, the baked wind data and its compact
    /// layout, the CPU gust mirror, and the GPU probe of the shader's own functions.
    /// </summary>
    [Category("Forest")]
    public class ForestWindTests
    {
        static readonly string[][] WindPrefabGroups =
        {
            ForestPrefabIds.YoungFirs, ForestPrefabIds.TallFirs, ForestPrefabIds.FernPatches, ForestPrefabIds.FernClumps, ForestPrefabIds.GrassPatches,
        };
        static readonly string[][] StaticPrefabGroups =
        {
            ForestPrefabIds.Boulders, ForestPrefabIds.Logs, ForestPrefabIds.Stumps, ForestPrefabIds.Branches,
        };

        static IEnumerable<string> WindPrefabs() => WindPrefabGroups.SelectMany(g => g).Where(ForestPrefabs.Exists);

        static List<Material> KitMaterials()
        {
            return AssetDatabase.FindAssets("t:Material", new[] { ForestPaths.KitMaterials })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".mat", StringComparison.Ordinal)).OrderBy(p => p, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<Material>).Where(m => m != null).ToList();
        }

        // The project folder's path of an asset path.
        static string Disk(string assetPath) => Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);

        static string NameOf(Material material) => Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(material));

        static bool IsTwin(string name) => name.EndsWith("__mv", StringComparison.Ordinal);

        static List<Material> RequireWindMaterials()
        {
            List<Material> materials = KitMaterials().Where(m => ForestWindProfiles.ForMaterial(NameOf(m)) != null).ToList();
            if (materials.Count == 0)
                Assert.Ignore("No wind material has been built yet (Gamex/Forest/1 Import Assets).");
            return materials;
        }

        // ---- Shaders ----

        [Test]
        public void WindShaders_ImportAndCompileForMetal()
        {
            if (!File.Exists(Disk(ForestWindShaderGraphs.GraphPath)))
                Assert.Ignore("The wind graphs have not been written yet (Gamex/Forest/Wind/Write Shader Graphs).");
            var problems = new List<string>();
            foreach (bool triplanar in new[] { false, true })
            {
                string path = triplanar ? ForestWindShaderGraphs.TriplanarGraphPath : ForestWindShaderGraphs.GraphPath;
                if (File.ReadAllText(Disk(path)) != ForestWindShaderGraphs.GraphText(triplanar))
                    problems.Add($"{path} differs from ForestWindShaderGraphs.GraphText (re-saved in the Shader Graph window, or not regenerated)");
                if (File.ReadAllText(Disk(path + ".meta")) != ForestWindShaderGraphs.MetaText(triplanar))
                    problems.Add($"{path}.meta differs from ForestWindShaderGraphs.MetaText");
                string name = triplanar ? ForestWindShaderGraphs.TriplanarShaderName : ForestWindShaderGraphs.ShaderName;
                Shader shader = Shader.Find(name);
                if (shader == null)
                {
                    problems.Add($"Shader.Find(\"{name}\") finds nothing");
                    continue;
                }
                var expected = ForestWindShaderGraphs.SurfaceProperties.Concat(ForestWindShaderGraphs.WindProperties).ToList();
                if (triplanar) expected.Add("_TexWorldScale");
                foreach (string property in expected)
                    if (shader.FindPropertyIndex(property) < 0)
                        problems.Add($"{name} has no property {property}");
                int baseMap = shader.FindPropertyIndex("_BaseColorMap");
                if (baseMap >= 0 && (shader.GetPropertyFlags(baseMap) & ShaderPropertyFlags.NoScaleOffset) != 0)
                    problems.Add($"{name}: _BaseColorMap is NoScaleOffset, so _BaseColorMap_ST would not reach the shader");
            }
            problems.AddRange(ForestWindShaderGraphs.CheckShaders());
            ForestTestSupport.AssertNone(problems, "wind shader problem(s)");
        }

        // ---- Materials ----

        [Test]
        public void WindMaterials_FollowTheirRecipes()
        {
            List<Material> materials = RequireWindMaterials();
            ForestWindShaderGraphs.EnsureRenderPipeline();
            bool graph = ForestMaterials.WindShaderActive;
            var problems = new List<string>();
            var names = new HashSet<string>(materials.Select(NameOf));
            foreach (Material material in materials)
            {
                string name = NameOf(material);
                ForestWindProfiles.Profile profile = ForestWindProfiles.ForMaterial(name);
                bool twin = IsTwin(name);
                bool triplanar = name.Contains("__triplanar");
                string baseName = twin ? name.Substring(0, name.Length - 4) : name;
                if (twin ? !names.Contains(baseName) : !names.Contains(name + "__mv"))
                    problems.Add($"{name}: its {(twin ? "plain material" : "__mv twin")} is missing");

                string wantedShader = graph ? (triplanar ? ForestWindShaderGraphs.TriplanarShaderName : ForestWindShaderGraphs.ShaderName) : "HDRP/Lit";
                if (material.shader == null || material.shader.name != wantedShader)
                {
                    problems.Add($"{name}: shader {(material.shader != null ? material.shader.name : "none")}, expected {wantedShader}");
                    continue;
                }
                var copy = new Material(material);
                try
                {
                    if (!HDMaterial.ValidateMaterial(copy))
                        problems.Add($"{name}: HDMaterial.ValidateMaterial returned false");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(copy);
                }

                bool foliage = material.GetFloat("_AlphaCutoffEnable") > 0.5f;
                bool expectFoliage = ForestMaterials.IsFoliage(baseName.Replace("__tint1", "").Replace("__crowncore", "")) && !name.Contains("__crowncore");
                if (foliage != expectFoliage)
                    problems.Add($"{name}: alpha clipping {(foliage ? "on" : "off")}, expected {(expectFoliage ? "on" : "off")}");
                if (expectFoliage)
                {
                    foreach (string keyword in new[] { "_ALPHATEST_ON", "_DOUBLESIDED_ON", "_MATERIAL_FEATURE_TRANSMISSION" })
                        if (!material.IsKeywordEnabled(keyword))
                            problems.Add($"{name}: keyword {keyword} is off");
                    if (material.renderQueue != 2475)
                        problems.Add($"{name}: render queue {material.renderQueue}, expected 2475 (alpha test)");
                    if (Mathf.RoundToInt(material.GetFloat("_MaterialID")) != 5)
                        problems.Add($"{name}: material type {material.GetFloat("_MaterialID")}, expected 5 (translucent)");
                    if (BitConverter.ToUInt32(BitConverter.GetBytes(material.GetFloat("_DiffusionProfileHash")), 0) == 0)
                        problems.Add($"{name}: no diffusion profile hash");
                }
                else if (Mathf.RoundToInt(material.GetFloat("_MaterialID")) != 1)
                {
                    problems.Add($"{name}: material type {material.GetFloat("_MaterialID")}, expected 1 (standard)");
                }

                bool passOn = material.GetShaderPassEnabled("MotionVectors");
                if (graph)
                {
                    if (material.GetTag("ShaderGraphTargetId", false, "") != "HDLitSubTarget")
                        problems.Add($"{name}: ShaderGraphTargetId is '{material.GetTag("ShaderGraphTargetId", false, "")}'");
                    if (material.GetTag("MotionVector", false, "Nothing") != "User")
                        problems.Add($"{name}: tag MotionVector is not User");
                    if (passOn != twin)
                        problems.Add($"{name}: MotionVectors pass {(passOn ? "on" : "off")}, expected {(twin ? "on" : "off")}");
                    var vectors = new (string property, Vector4 value)[]
                    {
                        ("_GamexSway", profile.Sway), ("_GamexFlutter", profile.Flutter), ("_GamexMotion", profile.Motion),
                        ("_GamexPush", profile.Push), ("_GamexStem", profile.Stem),
                    };
                    foreach ((string property, Vector4 value) in vectors)
                        if ((material.GetVector(property) - value).sqrMagnitude > 1e-12f)
                            problems.Add($"{name}: {property} is {material.GetVector(property)}, the profile says {value}");
                }
                else if (passOn)
                {
                    problems.Add($"{name}: MotionVectors pass on in the HDRP/Lit mode");
                }
            }
            ForestTestSupport.AssertNone(problems, "wind material(s) differ from their recipe");
        }

        [Test]
        public void WindMaterials_StaticPropsStayOnHdrpLit()
        {
            List<string> built = StaticPrefabGroups.SelectMany(g => g).Where(ForestPrefabs.Exists).ToList();
            if (built.Count == 0)
                Assert.Ignore("No static prop has been built yet.");
            var problems = new List<string>();
            foreach (string prefabId in built)
                foreach (MeshRenderer renderer in ForestPrefabs.Load(prefabId).GetComponentsInChildren<MeshRenderer>(true))
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null) continue;
                        if (material.shader == null || material.shader.name != "HDRP/Lit")
                            problems.Add($"{prefabId}/{renderer.name}: {material.name} is on {(material.shader != null ? material.shader.name : "no shader")}");
                        if (ForestWindProfiles.ForMaterial(material.name) != null)
                            problems.Add($"{prefabId}/{renderer.name}: uses the wind material {material.name}");
                    }
            foreach (Material ground in KitMaterials().Where(m => NameOf(m).EndsWith("__ground", StringComparison.Ordinal)))
                if (ground.shader.name != "HDRP/Lit")
                    problems.Add($"{ground.name} is on {ground.shader.name}, not HDRP/Lit");
            ForestTestSupport.AssertNone(problems, "static prop(s) touched by the wind");
        }

        [Test]
        public void WindMaterials_ResolveToTheManifest()
        {
            List<Material> materials = RequireWindMaterials();
            var problems = new List<string>();
            int resolved = 0;
            foreach (Material material in materials)
            {
                string name = NameOf(material);
                if (name.Contains("__crowncore")) continue;   // no maps of its own, as before W1
                if (ForestAudit.ManifestEntryOf(material) == null)
                    problems.Add($"{name} does not resolve to a manifest material through ForestAudit.ManifestEntryOf");
                else
                    resolved++;
            }
            Assert.Greater(resolved, 0, "No wind material was checked.");
            ForestTestSupport.AssertNone(problems, "wind material(s) unknown to the audit");
        }

        // ---- Prefabs ----

        [Test]
        public void WindPrefabs_EveryMovingPartSharesTheVertexCode()
        {
            List<string> built = WindPrefabs().ToList();
            if (built.Count == 0)
                Assert.Ignore("No wind prefab has been built yet.");
            bool graph = ForestMaterials.WindShaderActive;
            var problems = new List<string>();
            foreach (string prefabId in built)
            {
                GameObject prefab = ForestPrefabs.Load(prefabId);
                int mvLevels = ForestKitSpec.MotionVectorLevels(prefabId);
                foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
                {
                    bool shadowProxy = renderer.name == "LOD0_Shadow";
                    int level = !shadowProxy && renderer.name.StartsWith("LOD", StringComparison.Ordinal) && int.TryParse(renderer.name.Substring(3), out int l) ? l : -1;
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null) { problems.Add($"{prefabId}/{renderer.name}: an empty material slot"); continue; }
                        string name = NameOf(material);
                        if (name.EndsWith("__ground", StringComparison.Ordinal)) continue;   // the root skirt stays static by design
                        bool wind = ForestWindProfiles.ForMaterial(name) != null;
                        if (!wind || (graph && !ForestWindShaderGraphs.IsWindShader(material.shader)))
                            problems.Add($"{prefabId}/{renderer.name}: {name} does not run the wind vertex code");
                        bool wantTwin = !shadowProxy && level >= 0 && level < mvLevels;
                        if (IsTwin(name) != wantTwin)
                            problems.Add($"{prefabId}/{renderer.name}: {name} {(wantTwin ? "should be" : "should not be")} an __mv twin (MotionVectorLevels {mvLevels})");
                    }
                }
            }

            // ApplyTint keeps the __mv choice.
            foreach (string prefabId in built.Where(id => ForestTestSupport.Contains(ForestPrefabIds.YoungFirs, id) || ForestTestSupport.Contains(ForestPrefabIds.FernPatches, id)))
            {
                var instance = (GameObject)UnityEngine.Object.Instantiate(ForestPrefabs.Load(prefabId));
                try
                {
                    var before = instance.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterials.Select(m => m != null && IsTwin(m.name)).ToArray()).ToList();
                    ForestPrefabs.ApplyTint(instance, 1);
                    var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
                    bool tinted = false;
                    for (int r = 0; r < renderers.Length; r++)
                        for (int m = 0; m < renderers[r].sharedMaterials.Length; m++)
                        {
                            Material material = renderers[r].sharedMaterials[m];
                            if (material == null) continue;
                            tinted |= material.name.Contains("__tint1");
                            if (IsTwin(material.name) != before[r][m])
                                problems.Add($"{prefabId}/{renderers[r].name}: ApplyTint changed the __mv choice of slot {m} ({material.name})");
                        }
                    if (!tinted)
                        problems.Add($"{prefabId}: ApplyTint(1) applied no tint variant");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
            ForestTestSupport.AssertNone(problems, "wind prefab problem(s)");
        }

        // ---- Wind data ----

        static IEnumerable<(string prefabId, string level, Mesh mesh, Material[] materials)> WindMeshes()
        {
            foreach (string prefabId in WindPrefabs())
                foreach (MeshRenderer renderer in ForestPrefabs.Load(prefabId).GetComponentsInChildren<MeshRenderer>(true))
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null)
                        yield return (prefabId, renderer.name, filter.sharedMesh, renderer.sharedMaterials);
                }
        }

        [Test]
        public void WindData_LayoutAndChannels()
        {
            var meshes = WindMeshes().ToList();
            if (meshes.Count == 0)
                Assert.Ignore("No wind prefab has been built yet.");
            var problems = new List<string>();
            foreach ((string prefabId, string level, Mesh mesh, Material[] materials) in meshes)
            {
                string label = $"{prefabId}/{level} ({mesh.name})";
                if (!mesh.isReadable) problems.Add($"{label} is not readable");
                if (mesh.GetVertexAttributeFormat(VertexAttribute.Position) != VertexAttributeFormat.Float32 || mesh.GetVertexAttributeDimension(VertexAttribute.Position) != 3)
                    problems.Add($"{label}: position is not Float32 x 3");
                if (mesh.GetVertexAttributeFormat(VertexAttribute.Normal) != VertexAttributeFormat.Float32 || mesh.GetVertexAttributeDimension(VertexAttribute.Normal) != 3)
                    problems.Add($"{label}: normal is not Float32 x 3");
                if (!mesh.HasVertexAttribute(VertexAttribute.Tangent) || mesh.GetVertexAttributeFormat(VertexAttribute.Tangent) != VertexAttributeFormat.Float16 || mesh.GetVertexAttributeDimension(VertexAttribute.Tangent) != 4)
                    problems.Add($"{label}: tangent is not Float16 x 4");
                if (!mesh.HasVertexAttribute(VertexAttribute.Color) || mesh.GetVertexAttributeFormat(VertexAttribute.Color) != VertexAttributeFormat.UNorm8 || mesh.GetVertexAttributeDimension(VertexAttribute.Color) != 4)
                {
                    problems.Add($"{label}: no UNorm8 x 4 colour");
                    continue;
                }
                if (mesh.HasVertexAttribute(VertexAttribute.TexCoord0) && (mesh.GetVertexAttributeFormat(VertexAttribute.TexCoord0) != VertexAttributeFormat.Float32 || mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0) != 2))
                    problems.Add($"{label}: UV0 is not Float32 x 2");
                // W1 R2 (review of the R1 captures): fern and grass meshes also carry each frond's or blade's anchor in UV2 (Float16 x 4: the
                // base's offset and the azimuth), 52 B; trees keep spec 4.1's 44 B and no UV2.
                bool plant = !ForestTestSupport.IsTree(prefabId);
                int attributes = plant ? 6 : 5, stride = plant ? 52 : 44;
                if (mesh.GetVertexAttributes().Length != attributes || mesh.GetVertexBufferStride(0) != stride)
                    problems.Add($"{label}: {mesh.GetVertexAttributes().Length} attributes, stride {mesh.GetVertexBufferStride(0)} B (expected {attributes}, {stride} B)");
                if (plant != mesh.HasVertexAttribute(VertexAttribute.TexCoord2))
                    problems.Add($"{label}: {(plant ? "no" : "a")} UV2 anchor");
                else if (plant && (mesh.GetVertexAttributeFormat(VertexAttribute.TexCoord2) != VertexAttributeFormat.Float16 || mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord2) != 4))
                    problems.Add($"{label}: the UV2 anchor is not Float16 x 4");
                else if (plant)
                {
                    // Every anchor names a point of the mesh's own box (the frond's base) and an azimuth.
                    var anchors = new List<Vector4>();
                    mesh.GetUVs(2, anchors);
                    Vector3[] at = mesh.vertices;
                    Bounds box = mesh.bounds;
                    box.Expand(0.01f);
                    int outside = 0;
                    for (int v = 0; v < at.Length; v++)
                    {
                        Vector4 a = anchors[v];
                        if (!box.Contains(at[v] + new Vector3(a.x, a.y, a.z)) || Mathf.Abs(a.w) > Mathf.PI + 0.01f || float.IsNaN(a.x + a.y + a.z + a.w)) outside++;
                    }
                    if (outside > 0) problems.Add($"{label}: {outside} anchors outside the mesh's box or with no azimuth");
                }

                Color32[] colours = mesh.colors32;
                Vector3[] positions = mesh.vertices;
                for (int s = 0; s < mesh.subMeshCount && s < materials.Length; s++)
                {
                    string materialName = materials[s] != null ? NameOf(materials[s]) : "";
                    bool skirt = materialName.EndsWith("__ground", StringComparison.Ordinal);
                    string manifestName = materialName.Replace("__mv", "").Replace("__tint1", "").Replace("__triplanar", "").Replace("__crowncore", "");
                    bool wood = !skirt && !ForestMaterials.IsFoliage(manifestName);
                    bool trunk = manifestName.StartsWith("fir_tree_01_trunk_", StringComparison.Ordinal) || materialName.Contains("__triplanar");
                    int badB = 0, badG = 0, badR = 0, trunkG = 0, trunkSamples = 0;
                    foreach (int v in mesh.GetIndices(s).Distinct())
                    {
                        Color32 c = colours[v];
                        if ((wood || skirt) && c.b != 0) badB++;
                        if (skirt && c.g != 0) badG++;
                        if (plant && c.r != 0) badR++;
                        if (trunk && positions[v].y > 0.5f && positions[v].y < 1.5f) { trunkSamples++; if (c.g > 13) trunkG++; }
                    }
                    if (badB > 0) problems.Add($"{label} sub-mesh {s} ({materialName}): B > 0 on {badB} wood or skirt vertices");
                    if (badG > 0) problems.Add($"{label} sub-mesh {s} ({materialName}): G > 0 on {badG} skirt vertices");
                    if (badR > 0) problems.Add($"{label} sub-mesh {s} ({materialName}): R > 0 on {badR} fern or grass vertices");
                    // The trunk between 0.5 and 1.5 m lies within its own radius: G there is (a little over) zero.
                    if (trunkSamples > 0 && trunkG > 0.05f * trunkSamples)
                        problems.Add($"{label} sub-mesh {s} ({materialName}): G above 0.05 on {trunkG} of {trunkSamples} trunk vertices at 0.5 to 1.5 m");
                }
            }

            // Static props keep today's layout: no colour, Float32 tangents.
            foreach (string prefabId in StaticPrefabGroups.SelectMany(g => g).Where(ForestPrefabs.Exists))
            {
                Mesh mesh = ForestPrefabs.LoadLodMesh(prefabId, 0);
                if (mesh == null) continue;
                if (mesh.HasVertexAttribute(VertexAttribute.Color))
                    problems.Add($"{prefabId}: the static mesh {mesh.name} carries a colour");
                if (mesh.HasVertexAttribute(VertexAttribute.Tangent) && mesh.GetVertexAttributeFormat(VertexAttribute.Tangent) != VertexAttributeFormat.Float32)
                    problems.Add($"{prefabId}: the static mesh {mesh.name} has {mesh.GetVertexAttributeFormat(VertexAttribute.Tangent)} tangents");
            }
            ForestTestSupport.AssertNone(problems, "wind data layout or channel problem(s)");
        }

        [Test]
        public void WindData_IsLodInvariant()
        {
            List<string> trees = WindPrefabs().Where(ForestTestSupport.IsTree).ToList();
            if (trees.Count == 0)
                Assert.Ignore("No tree prefab has been built yet.");
            var problems = new List<string>();
            var report = new List<string>();

            // Conifer flutter is off (spec 2.6, 2.8): B is not LOD-invariant, so turning flutter on needs B normalised per level first.
            foreach (string model in new[] { "fir_sapling_medium", "fir_tree_01" })
            {
                ForestWindProfiles.Profile profile = ForestWindProfiles.ForModel(model);
                bool flutter = profile.flutterAmplitude != 0f;
                if (flutter)
                    problems.Add($"{model}: conifer flutter is on ({profile.flutterAmplitude} m); B is not normalised per level (spec 2.8)");
            }

            // Every kept needle of the young firs is flattened into a quad (LOD1) or replaced by twig cards (LOD2, LOD3), and a quad's
            // corners move with its level's enlargement, so young-fir anchors never coincide; the tall firs' pieces whose enlargement is
            // capped at the crown's edge do. The guard keeps the check from comparing nothing at all.
            int keptCompared = 0;
            foreach (string prefabId in trees)
            {
                Mesh lod0 = ForestPrefabs.LoadLodMesh(prefabId, 0);
                var index = new PointIndex(lod0.vertices, 0.02f);
                Color32[] c0 = lod0.colors32;
                Vector3[] p0 = lod0.vertices;
                List<int> anchors0 = FoliageAnchors(prefabId, 0, lod0);
                var anchorIndex = new PointIndex(anchors0.Select(v => p0[v]).ToArray(), AnchorCoincidence);
                for (int lod = 1; lod < ForestPrefabs.LodCount(prefabId); lod++)
                {
                    Mesh mesh = ForestPrefabs.LoadLodMesh(prefabId, lod);
                    if (mesh == null) continue;
                    Compare(prefabId, lod, mesh, index, c0, false, problems, report);
                    keptCompared += ComparePhaseOfKeptPieces(prefabId, lod, mesh, anchors0, anchorIndex, c0, problems, report);
                }
            }
            if (keptCompared == 0)
                problems.Add("no foliage piece of any tree level keeps its LOD0 anchor: the phase of kept pieces compared nothing");
            foreach (string prefabId in ForestPrefabIds.FernPatches.Where(ForestPrefabs.Exists))
            {
                Mesh near = ForestPrefabs.LoadLodMesh(prefabId, 0), far = ForestPrefabs.LoadLodMesh(prefabId, 1);
                if (near == null || far == null) continue;
                // W1 deviation (issues/W1-shader.md): the far level keeps a few clumps in their slots but at another scale, so its
                // vertices do not lie where the near level's do and matching by position pairs unrelated points. The same clump in the
                // same slot carries the same colours, so every far-level colour (R, G, B and A exactly) must occur on the near level.
                var nearColours = new HashSet<Color32>(near.colors32);
                Color32[] farColours = far.colors32;
                int missing = farColours.Count(c => !nearColours.Contains(c));
                report.Add($"{prefabId} LOD1 against LOD0: {farColours.Length - missing} of {farColours.Length} far-level colours occur on the near level");
                if (missing > 0)
                    problems.Add($"{prefabId} LOD1: {missing} of {farColours.Length} vertex colours do not occur on LOD0 (a frond changed its weights or phase)");
            }
            Debug.Log("Forest wind LOD invariance:\n  " + string.Join("\n  ", report));
            ForestTestSupport.AssertNone(problems, "level(s) that bend differently from their LOD0");
        }

        // R, G (and B where asked) at the nearest LOD0 vertex within 2 cm. A is only reported here: the coordinator's decision of
        // 2026-10-17 (issues/W1-shader.md section 6) compares the phase on the pieces both levels keep (ComparePhaseOfKeptPieces), and
        // the visible motion at points matched like these in WindMotion_LodSwitchDoesNotPop. Nearest-point matching pairs neighbouring
        // needles whose anchors lie up to about 10 cm apart, so a phase difference there is not the same needle changing its phase.
        static void Compare(string prefabId, int lod, Mesh mesh, PointIndex index, Color32[] c0, bool exactB, List<string> problems, List<string> report)
        {
            Vector3[] positions = mesh.vertices;
            Color32[] colours = mesh.colors32;
            int matches = 0, badR = 0, badG = 0, overA = 0, badB = 0;
            float worstR = 0f, worstG = 0f, worstA = 0f;
            int worstGVertex = -1, worstGOther = -1;
            for (int v = 0; v < positions.Length; v++)
            {
                int other = index.Nearest(positions[v]);
                if (other < 0) continue;
                matches++;
                float dR = Mathf.Abs(colours[v].r - c0[other].r) / 255f;
                float dG = Mathf.Abs(colours[v].g - c0[other].g) / 255f;
                float dA = Mathf.Abs(colours[v].a - c0[other].a) / 255f;
                dA = Mathf.Min(dA, 1f - dA);
                if (dG > worstG) { worstGVertex = v; worstGOther = other; }
                worstR = Mathf.Max(worstR, dR); worstG = Mathf.Max(worstG, dG); worstA = Mathf.Max(worstA, dA);
                if (dR > 2f / 255f + 1e-6f) badR++;
                if (dG > 0.06f) badG++;
                if (dA > 0.05f) overA++;
                if (exactB && colours[v].b != c0[other].b) badB++;
            }
            string worstGAt = "";
            if (worstGVertex >= 0)
            {
                Vector3[] p0 = ForestPrefabs.LoadLodMesh(prefabId, 0).vertices;
                worstGAt = $" (worst dG at {positions[worstGVertex].ToString("F3")} on {MaterialOfVertex(prefabId, lod, mesh, worstGVertex)}, G {colours[worstGVertex].g}, " +
                           $"against LOD0 {p0[worstGOther].ToString("F3")} on {MaterialOfVertex(prefabId, 0, ForestPrefabs.LoadLodMesh(prefabId, 0), worstGOther)}, G {c0[worstGOther].g})";
            }
            report.Add($"{prefabId} LOD{lod} against LOD0: {matches} of {positions.Length} vertices matched within 2 cm; worst dR {worstR:0.0000}, dG {worstG:0.000}{worstGAt}; " +
                       $"over the limits R {badR}, G {badG}{(exactB ? $", B {badB}" : "")}; (not asserted: dA at these points worst {worstA:0.000}, above 0.05 on {overA})");
            // W1 deviation (issues/W1-shader.md): a level of fewer than 4,000 vertices (the young firs' 2,400-vertex card level) cannot
            // offer 1,000 matches; it needs a quarter of its vertices.
            int required = Math.Min(1000, positions.Length / 4);
            if (matches < required)
            {
                problems.Add($"{prefabId} LOD{lod}: only {matches} vertices lie within 2 cm of LOD0, unable to compare (at least {required} needed)");
                return;
            }
            if (badR > 0) problems.Add($"{prefabId} LOD{lod}: |dR| above 2/255 on {badR} of {matches} matched vertices (worst {worstR:0.0000})");
            if (badG > 0) problems.Add($"{prefabId} LOD{lod}: |dG| above 0.06 on {badG} of {matches} matched vertices (worst {worstG:0.000})");
            if (badB > 0) problems.Add($"{prefabId} LOD{lod}: B differs on {badB} of {matches} matched vertices");
        }

        // The material of the first sub-mesh that uses a vertex, for reports.
        static string MaterialOfVertex(string prefabId, int lod, Mesh mesh, int vertex)
        {
            Material[] materials = ForestPrefabs.Load(prefabId).transform.Find("LOD" + lod).GetComponent<MeshRenderer>().sharedMaterials;
            for (int s = 0; s < mesh.subMeshCount; s++)
                if (Array.IndexOf(mesh.GetTriangles(s), vertex) >= 0)
                    return s < materials.Length && materials[s] != null ? NameOf(materials[s]) : $"sub-mesh {s}";
            return "no sub-mesh";
        }

        // Two anchors coincide when they lie within this distance (metres): the same point, up to float rounding.
        const float AnchorCoincidence = 1e-5f;

        // The phase of a piece both levels keep: a foliage piece of the level whose anchor (the thinner's rule: its vertex nearest the
        // vertical axis through the origin) coincides with the anchor of a LOD0 foliage piece. The phase is a function of the anchor, so
        // it must be the same up to float tolerance, which after 8-bit quantisation is one step (1/255).
        static int ComparePhaseOfKeptPieces(string prefabId, int lod, Mesh mesh, List<int> anchors0, PointIndex anchorIndex, Color32[] c0,
            List<string> problems, List<string> report)
        {
            Vector3[] positions = mesh.vertices;
            Color32[] colours = mesh.colors32;
            List<int> anchors = FoliageAnchors(prefabId, lod, mesh);
            int kept = 0, exact = 0, bad = 0;
            float worst = 0f;
            foreach (int v in anchors)
            {
                int other = anchorIndex.Nearest(positions[v]);
                if (other < 0) continue;
                kept++;
                int u = anchors0[other];
                if (colours[v].a == c0[u].a) exact++;
                float dA = Mathf.Abs(colours[v].a - c0[u].a) / 255f;
                dA = Mathf.Min(dA, 1f - dA);
                worst = Mathf.Max(worst, dA);
                if (dA > 1f / 255f + 1e-6f) bad++;
            }
            report.Add($"{prefabId} LOD{lod} kept pieces: {kept} of {anchors.Count} foliage pieces have their anchor on a LOD0 anchor (within {AnchorCoincidence * 1000f:0.00} mm); " +
                       $"phase exact on {exact}, worst dA {worst:0.0000}");
            if (bad > 0)
                problems.Add($"{prefabId} LOD{lod}: the phase of {bad} of {kept} kept pieces differs from LOD0 by more than 1/255 (worst {worst:0.000})");
            return kept;
        }

        // Per foliage piece of a level (triangles welded at 0.1 mm, as ForestWindData and the thinner weld), the index of its anchor
        // vertex: the vertex nearest the vertical axis through the origin, the first such in triangle order.
        static List<int> FoliageAnchors(string prefabId, int lod, Mesh mesh)
        {
            var anchors = new List<int>();
            Transform level = ForestPrefabs.Load(prefabId).transform.Find("LOD" + lod);
            Material[] materials = level.GetComponent<MeshRenderer>().sharedMaterials;
            Vector3[] positions = mesh.vertices;
            for (int s = 0; s < mesh.subMeshCount && s < materials.Length; s++)
            {
                string materialName = materials[s] != null ? NameOf(materials[s]) : "";
                if (materialName.Contains("__crowncore") || materialName.EndsWith("__ground", StringComparison.Ordinal)) continue;
                string manifestName = materialName.Replace("__mv", "").Replace("__tint1", "").Replace("__triplanar", "");
                if (!ForestMaterials.IsFoliage(manifestName)) continue;
                int[] triangles = mesh.GetTriangles(s);
                int[] pieceOfTriangle = Pieces(positions, triangles, out int count);
                var anchor = new int[count];
                var distance = new float[count];
                for (int p = 0; p < count; p++) { anchor[p] = -1; distance[p] = float.PositiveInfinity; }
                for (int i = 0; i < triangles.Length; i++)
                {
                    int piece = pieceOfTriangle[i / 3], v = triangles[i];
                    float rho = positions[v].x * positions[v].x + positions[v].z * positions[v].z;
                    if (rho < distance[piece]) { distance[piece] = rho; anchor[piece] = v; }
                }
                anchors.AddRange(anchor.Where(v => v >= 0));
            }
            return anchors;
        }

        // Connected pieces of a triangle list, positions welded at 0.1 mm; piece ids in the order of their first triangle.
        static int[] Pieces(Vector3[] positions, int[] triangles, out int count)
        {
            var welded = new Dictionary<long, int>();
            var idOfVertex = new int[positions.Length];
            for (int v = 0; v < positions.Length; v++) idOfVertex[v] = -1;
            int Id(int v)
            {
                if (idOfVertex[v] >= 0) return idOfVertex[v];
                Vector3 p = positions[v];
                long x = (long)Math.Floor(p.x * 10000.0) & 0x1FFFFF, y = (long)Math.Floor(p.y * 10000.0) & 0x1FFFFF, z = (long)Math.Floor(p.z * 10000.0) & 0x1FFFFF;
                long key = (x << 42) | (y << 21) | z;
                if (!welded.TryGetValue(key, out int id)) welded.Add(key, id = welded.Count);
                return idOfVertex[v] = id;
            }
            var parent = new List<int>();
            int Root(int i)
            {
                while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
                return i;
            }
            int triangleCount = triangles.Length / 3;
            var first = new int[triangleCount];
            for (int t = 0; t < triangleCount; t++)
            {
                for (int c = 0; c < 3; c++)
                {
                    int id = Id(triangles[t * 3 + c]);
                    while (parent.Count <= id) parent.Add(parent.Count);
                }
                int root = Root(idOfVertex[triangles[t * 3]]);
                for (int c = 1; c < 3; c++)
                {
                    int other = Root(idOfVertex[triangles[t * 3 + c]]);
                    if (other == root) continue;
                    if (other < root) { parent[root] = other; root = other; }
                    else parent[other] = root;
                }
                first[t] = idOfVertex[triangles[t * 3]];
            }
            var pieceOfRoot = new Dictionary<int, int>();
            var pieceOfTriangle = new int[triangleCount];
            for (int t = 0; t < triangleCount; t++)
            {
                int root = Root(first[t]);
                if (!pieceOfRoot.TryGetValue(root, out int piece)) pieceOfRoot.Add(root, piece = pieceOfRoot.Count);
                pieceOfTriangle[t] = piece;
            }
            count = pieceOfRoot.Count;
            return pieceOfTriangle;
        }

        // ---- Visible motion at LOD switches ----

        /// <summary>
        /// Coordinator's decision of 2026-10-17 (issues/W1-shader.md section 6): what must not pop at a LOD switch is visible motion. For each
        /// tree and fern patch LOD pair, the shader's own displacement (GamexWindDeform, run by ForestWindProbe.compute's Deform kernel) at
        /// points of the farther level matched to the nearer level within 2 cm, at several times, for the smallest, median and largest
        /// instance of the prefab in the plan (smaller instances switch nearer, where the distance fade leaves more motion): the 95th
        /// percentile of the displacement difference must stay below one pixel at 2,560 x 1,440 and a 70 degree field of view at that
        /// pair's switch distance. The wind is the strongest any preset reaches: its gust peak with the controller's +25 % wander (spec 2.7).
        /// </summary>
        [Test]
        public void WindMotion_LodSwitchDoesNotPop()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("No compute shaders on this device.");
            List<string> prefabs = ForestPrefabIds.YoungFirs.Concat(ForestPrefabIds.TallFirs).Concat(ForestPrefabIds.FernPatches)
                .Where(id => ForestPrefabs.Exists(id) && ForestPrefabs.LodCount(id) >= 2).ToList();
            if (prefabs.Count == 0)
                Assert.Ignore("No tree or fern patch prefab has been built yet.");
            if (!File.Exists(ForestPaths.PlanPath))
                Assert.Ignore($"{ForestPaths.PlanPath} has not been built yet (Gamex/Forest/3 Rebuild Forest Scene).");
            ForestPlan plan = JsonUtility.FromJson<ForestPlan>(File.ReadAllText(ForestPaths.PlanPath));
            ComputeShader probe = ForestWindProbeSupport.Load();

            const float wander = 1.25f;
            float strongest = ForestLightingPresets.Names.Max(n => ForestLightingPresets.Get(n).windGustPeak) * wander;
            ForestWindState state = ForestLightingPresets.Get("Night").WindState();
            state.calm = state.peak = strongest;
            float tanHalf = Mathf.Tan(35f * Mathf.Deg2Rad);
            float pixelPerMetre = 2f * tanHalf / 1440f;   // the height of one pixel per metre of distance
            var times = Enumerable.Range(0, 16).Select(i => 0.25f + 0.6875f * i).ToArray();   // 0.25 to 10.6 s: several sway periods

            var problems = new List<string>();
            var report = new List<string>();
            foreach (string prefabId in prefabs)
            {
                GameObject prefab = ForestPrefabs.Load(prefabId);
                var group = prefab.GetComponent<LODGroup>();
                LOD[] lods = group.GetLODs();
                ForestWindProfiles.Profile profile = prefab.transform.Find("LOD0").GetComponent<MeshRenderer>().sharedMaterials
                    .Where(m => m != null).Select(m => ForestWindProfiles.ForMaterial(NameOf(m))).FirstOrDefault(p => p != null);
                if (profile == null) { problems.Add($"{prefabId}: no wind profile on LOD0"); continue; }

                List<ForestPlacement> placed = plan.placements.Where(p => p.prefabId == prefabId).OrderBy(p => p.scale).ToList();
                var instances = placed.Count == 0
                    ? new List<ForestPlacement> { new ForestPlacement { prefabId = prefabId, scale = 1f, rotation = Quaternion.identity } }
                    : new[] { placed[0], placed[placed.Count / 2], placed[placed.Count - 1] }.Distinct().ToList();

                for (int k = 0; k + 1 < lods.Length; k++)
                {
                    Mesh near = ForestPrefabs.LoadLodMesh(prefabId, k), far = ForestPrefabs.LoadLodMesh(prefabId, k + 1);
                    if (near == null || far == null) { problems.Add($"{prefabId}: LOD{k} or LOD{k + 1} has no mesh"); continue; }
                    Vector3[] nearPositions = near.vertices, farPositions = far.vertices;
                    var index = new PointIndex(nearPositions, 0.02f);
                    var farMatched = new List<int>();
                    var nearMatched = new List<int>();
                    for (int v = 0; v < farPositions.Length; v++)
                    {
                        int other = index.Nearest(farPositions[v]);
                        if (other < 0) continue;
                        farMatched.Add(v);
                        nearMatched.Add(other);
                    }
                    // A sample-size guard, not a coverage rule: two card levels (the young firs' 0.118 m and 0.350 m cells) share few
                    // points within 2 cm, and 100 points at 16 times still give 1,600 samples for the 95th percentile.
                    const int required = 100;
                    if (farMatched.Count < required)
                    {
                        problems.Add($"{prefabId} LOD{k} -> LOD{k + 1}: only {farMatched.Count} points matched within 2 cm, unable to compare (at least {required} needed)");
                        continue;
                    }
                    (Vector3[] p, Vector3[] n, Color[] c) Subset(Mesh mesh, List<int> vertices)
                    {
                        Vector3[] positions = mesh.vertices, normals = mesh.normals;
                        Color32[] colours = mesh.colors32;
                        return (vertices.Select(v => positions[v]).ToArray(), vertices.Select(v => normals[v]).ToArray(), vertices.Select(v => (Color)colours[v]).ToArray());
                    }
                    var a = Subset(near, nearMatched);
                    var b = Subset(far, farMatched);

                    string worstLine = null;
                    float worstRatio = -1f;
                    foreach (ForestPlacement instance in instances)
                    {
                        float s = instance.scale;
                        float switchDistance = group.size * s / (2f * tanHalf * lods[k].screenRelativeTransitionHeight);
                        float limit = switchDistance * pixelPerMetre;
                        var differences = new float[farMatched.Count * times.Length];
                        var motion = new float[farMatched.Count * times.Length];
                        for (int ti = 0; ti < times.Length; ti++)
                        {
                            var probeInstance = new ForestWindProbeSupport.Instance
                            {
                                pivot = instance.position, scale = s, yaw = instance.rotation.eulerAngles.y,
                                camera = instance.position + new Vector3(switchDistance, 0f, 0f), time = times[ti],
                                sway = profile.Sway, flutter = profile.Flutter, motion = profile.Motion, push = profile.Push, stem = profile.Stem,
                            };
                            ForestWindProbeSupport.RunDeform(probe, state, probeInstance, default, a.p, a.n, null, a.c, out Vector3[] movedNear, out _, out _);
                            ForestWindProbeSupport.RunDeform(probe, state, probeInstance, default, b.p, b.n, null, b.c, out Vector3[] movedFar, out _, out _);
                            for (int i = 0; i < farMatched.Count; i++)
                            {
                                Vector3 dNear = movedNear[i] - a.p[i], dFar = movedFar[i] - b.p[i];
                                differences[ti * farMatched.Count + i] = s * (dFar - dNear).magnitude;   // object space x uniform scale = world metres
                                motion[ti * farMatched.Count + i] = s * dNear.magnitude;
                            }
                        }
                        float p95 = Percentile(differences, 0.95f), worst = differences.Max();
                        float above = differences.Count(d => d > limit) / (float)differences.Length;
                        float motion95 = Percentile(motion, 0.95f);
                        string line = $"{prefabId} LOD{k} -> LOD{k + 1} at scale {s:0.000}: switch at {switchDistance:0.0} m, 1 px = {limit * 1000f:0.00} mm; " +
                                      $"{farMatched.Count} matched points x {times.Length} times; displacement difference p95 {p95 * 1000f:0.00} mm " +
                                      $"({p95 / limit:0.00} px), worst {worst * 1000f:0.00} mm ({worst / limit:0.00} px), {above:P1} above 1 px; motion p95 {motion95 * 1000f:0.00} mm";
                        report.Add(line);
                        if (p95 / limit > worstRatio) { worstRatio = p95 / limit; worstLine = line; }
                        if (p95 > limit)
                            problems.Add($"{prefabId} LOD{k} -> LOD{k + 1} at scale {s:0.000}: the 95th percentile of the displacement difference is {p95 * 1000f:0.00} mm, " +
                                         $"above one pixel ({limit * 1000f:0.00} mm) at the {switchDistance:0.0} m switch (worst {worst * 1000f:0.00} mm)");
                        // The positive control: inside the animation distance the probe must have moved something.
                        if (switchDistance < state.fadeEnd - 0.5f && motion.Max() <= 0f)
                            problems.Add($"{prefabId} LOD{k} at scale {s:0.000}: nothing moved at {switchDistance:0.0} m (the probe read zeros?)");
                    }
                    report.Add($"  worst instance of {prefabId} LOD{k} -> LOD{k + 1}: {worstLine}");
                }
            }
            Debug.Log($"Forest wind motion at LOD switches (W {strongest:0.000}, fade {state.fadeStart:0} to {state.fadeEnd:0} m):\n  " + string.Join("\n  ", report));
            ForestTestSupport.AssertNone(problems, "LOD switch(es) where the wind motion pops");
        }

        static float Percentile(float[] values, float q)
        {
            var sorted = (float[])values.Clone();
            Array.Sort(sorted);
            return sorted.Length == 0 ? 0f : sorted[Mathf.Clamp(Mathf.CeilToInt(q * sorted.Length) - 1, 0, sorted.Length - 1)];
        }

        /// <summary>Nearest point within a radius, on a grid of that cell size. Ties go to the lower index.</summary>
        sealed class PointIndex
        {
            readonly Vector3[] points;
            readonly float cell;
            readonly Dictionary<long, List<int>> cells = new();

            public PointIndex(Vector3[] points, float radius)
            {
                this.points = points;
                cell = radius;
                for (int i = 0; i < points.Length; i++)
                {
                    long key = Key(Cell(points[i].x), Cell(points[i].y), Cell(points[i].z));
                    if (!cells.TryGetValue(key, out List<int> list)) cells.Add(key, list = new List<int>());
                    list.Add(i);
                }
            }

            int Cell(float value) => Mathf.FloorToInt(value / cell);

            static long Key(int x, int y, int z) => ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);

            public int Nearest(Vector3 p)
            {
                int cx = Cell(p.x), cy = Cell(p.y), cz = Cell(p.z);
                int best = -1;
                float bestDistance = cell * cell;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (!cells.TryGetValue(Key(cx + dx, cy + dy, cz + dz), out List<int> list)) continue;
                            foreach (int i in list)
                            {
                                float d = (points[i] - p).sqrMagnitude;
                                if (d <= bestDistance && (best < 0 || d < bestDistance || i < best)) { bestDistance = d; best = i; }
                            }
                        }
                return best;
            }
        }

        [Test]
        public void WindData_FormulasOnSyntheticTree()
        {
            // A leaning cylinder trunk (radius 0.1 m, 6 m, leaning 2 cm per metre toward +X), two branches and needle quads.
            const float trunkRadius = 0.1f, height = 6f, lean = 0.02f;
            ForestMeshTools.MeshData trunk = Cylinder(y => new Vector2(lean * y, 0f), trunkRadius, 0f, height, 48, 24);
            ForestMeshTools.MeshData branchA = Tube(new Vector3(lean * 2f, 2f, 0f), new Vector3(2f, 2.4f, 0f), 0.03f);
            ForestMeshTools.MeshData branchB = Tube(new Vector3(lean * 3.5f, 3.5f, 0f), new Vector3(-1.5f, 3.8f, 1f), 0.03f);
            ForestMeshTools.MeshData wood = ForestMeshTools.Merge(trunk, ForestMeshTools.Merge(branchA, branchB));
            ForestMeshTools.MeshData needles = Needles(new Vector3(2f, 2.4f, 0f), new Vector3(-1.5f, 3.8f, 1f), 0.08f);

            ForestWindData.TreeField field = ForestWindData.MeasureTree("fir_sapling_medium_a", wood, height);
            var problems = new List<string>();
            for (float y = 0.5f; y < 5.5f; y += 0.5f)
            {
                Vector2 c = field.CentreAt(y);
                if ((c - new Vector2(lean * y, 0f)).magnitude > 0.01f && Mathf.Abs(y - 2f) > 0.3f && Mathf.Abs(y - 3.5f) > 0.3f)
                    problems.Add($"centreline at {y} m is {c}, expected ({lean * y}, 0)");
                float r = field.RadiusAt(y);
                if (Mathf.Abs(r - trunkRadius) > 0.01f && Mathf.Abs(y - 2f) > 0.3f && Mathf.Abs(y - 3.5f) > 0.3f)
                    problems.Add($"trunk radius at {y} m is {r:0.000}, expected {trunkRadius}");
            }
            if (field.RadiusAt(height + 0.5f) != 0f) problems.Add("the radius above the trunk's top is not 0");

            ForestMeshTools.MeshData level = ForestMeshTools.Merge(wood, needles);
            var parts = new[] { ForestWindData.Part.Wood, ForestWindData.Part.Foliage };
            Color32[] colours = ForestWindData.TreeColours(field, level, parts);
            Color32[] again = ForestWindData.TreeColours(field, level, parts);
            if (!colours.SequenceEqual(again)) problems.Add("two runs give different colours");

            float classHeight = ForestWindData.YoungClassHeight;
            Vector4 top = ForestWindData.TreeWeights(field, new Vector3(lean * height, height, 0f), ForestWindData.Part.Wood, 0f, Vector3.zero);
            Vector4 foot = ForestWindData.TreeWeights(field, new Vector3(0f, 0f, 0.1f), ForestWindData.Part.Wood, 0f, Vector3.zero);
            Vector4 mid = ForestWindData.TreeWeights(field, new Vector3(lean * 3f, 3f, trunkRadius), ForestWindData.Part.Wood, 0f, Vector3.zero);
            float x = 0.5f, s = x * x * (6f - 4f * x + x * x) / 3f;
            Check(problems, "R at the top", top.x, height / classHeight, 2e-4f);
            Check(problems, "R at the foot", foot.x, 0f, 1e-6f);
            Check(problems, "R half way", mid.x, s * height / classHeight, 2e-4f);
            Check(problems, "G on the trunk surface", mid.y, 0f, 0.002f);
            Check(problems, "B on wood", mid.z, 0f, 0f);
            Vector4 tip = ForestWindData.TreeWeights(field, new Vector3(2f, 2.4f, 0f), ForestWindData.Part.Wood, 0f, Vector3.zero);
            float rho = (new Vector2(2f, 0f) - field.CentreAt(2.4f)).magnitude;
            Check(problems, "G at a branch tip", tip.y, Mathf.Pow(Mathf.Clamp01((rho - field.RadiusAt(2.4f)) / 3f), 1.5f), 1e-4f);
            Vector4 needle = ForestWindData.TreeWeights(field, new Vector3(2.1f, 2.4f, 0f), ForestWindData.Part.Foliage, 0.12f, new Vector3(2f, 2.4f, 0f));
            Check(problems, "B of a needle point 12 cm from its anchor", needle.z, 0.12f / 0.30f, 1e-5f);
            Vector4 anchor = ForestWindData.TreeWeights(field, new Vector3(2f, 2.4f, 0f), ForestWindData.Part.Wood, 0f, Vector3.zero);
            Check(problems, "a needle takes the phase of its anchor", needle.w, anchor.w, 1e-6f);

            // The phase is continuous around the trunk and across the 1 to 0 wrap.
            float worstStep = 0f;
            for (float y = 0.2f; y < 5.8f; y += 0.37f)
            {
                float previous = -1f;
                for (int k = 0; k <= 720; k++)
                {
                    float angle = k * Mathf.PI * 2f / 720f;
                    Vector2 c = field.CentreAt(y);
                    float a = ForestWindData.TreeWeights(field, new Vector3(c.x + Mathf.Cos(angle), y, c.y + Mathf.Sin(angle)), ForestWindData.Part.Wood, 0f, Vector3.zero).w;
                    if (previous >= 0f)
                    {
                        float step = Mathf.Abs(a - previous);
                        worstStep = Mathf.Max(worstStep, Mathf.Min(step, 1f - step));
                    }
                    previous = a;
                }
            }
            if (worstStep > 0.05f) problems.Add($"the phase jumps by {worstStep:0.000} cycle between neighbouring points 0.5 degree apart");

            // Synthetic fern and grass clumps: one clump phase per slot, every frond and blade within 0.12 cycle of it.
            foreach (bool fern in new[] { true, false })
            {
                ForestMeshTools.MeshData clump = Clump(fern, 9);
                int[] all = clump.subMeshes.SelectMany(t => t).ToArray();
                for (int slot = 0; slot < 4; slot++)
                {
                    Color32[] c = ForestWindData.ClumpColours(clump.positions, all, slot, fern, 4242);
                    float[] phases = c.Select(colour => colour.a / 255f).ToArray();
                    float centre = ForestWindData.ClumpPhase(slot, 4242);
                    float worst = phases.Max(p => Circular(p - centre));
                    if (worst > 0.12f + 1f / 255f)
                        problems.Add($"{(fern ? "fern" : "grass")} slot {slot}: a frond's phase lies {worst:0.000} cycle from the clump's");
                    if (c.Any(colour => colour.r != 0))
                        problems.Add($"{(fern ? "fern" : "grass")} slot {slot}: R is not 0");
                    if (!fern && c.Any(colour => Mathf.Abs(colour.b / 255f - Mathf.Sqrt(colour.g / 255f)) > 0.035f))
                        problems.Add($"grass slot {slot}: B is not sqrt(G)");
                }
            }
            ForestTestSupport.AssertNone(problems, "wind data formula problem(s)");
        }

        static void Check(List<string> problems, string what, float value, float expected, float tolerance)
        {
            if (Mathf.Abs(value - expected) > tolerance) problems.Add($"{what} is {value:0.00000}, expected {expected:0.00000}");
        }

        static float Circular(float d)
        {
            d -= Mathf.Floor(d);
            return Mathf.Min(d, 1f - d);
        }

        static float CircularMean(float[] phases)
        {
            double sx = 0, sy = 0;
            foreach (float p in phases) { sx += Math.Cos(2 * Math.PI * p); sy += Math.Sin(2 * Math.PI * p); }
            double a = Math.Atan2(sy, sx) / (2 * Math.PI);
            return (float)(a - Math.Floor(a));
        }

        static ForestMeshTools.MeshData Cylinder(Func<float, Vector2> centre, float radius, float bottom, float top, int sides, int rings)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float y = Mathf.Lerp(bottom, top, r / (float)rings);
                Vector2 c = centre(y);
                for (int s = 0; s < sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides;
                    positions.Add(new Vector3(c.x + radius * Mathf.Cos(a), y, c.y + radius * Mathf.Sin(a)));
                    normals.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < sides; s++)
                {
                    int a = r * sides + s, b = r * sides + (s + 1) % sides, c = a + sides, d = b + sides;
                    triangles.AddRange(new[] { a, c, b, b, c, d });
                }
            return new ForestMeshTools.MeshData { positions = positions.ToArray(), normals = normals.ToArray(), subMeshes = new[] { triangles.ToArray() } };
        }

        static ForestMeshTools.MeshData Tube(Vector3 from, Vector3 to, float radius)
        {
            Vector3 axis = (to - from).normalized;
            Vector3 side = Vector3.Cross(axis, Vector3.up).normalized, up = Vector3.Cross(side, axis);
            var positions = new List<Vector3>();
            var triangles = new List<int>();
            const int sides = 6;
            for (int end = 0; end < 2; end++)
                for (int s = 0; s < sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides;
                    positions.Add((end == 0 ? from : to) + radius * (Mathf.Cos(a) * side + Mathf.Sin(a) * up));
                }
            for (int s = 0; s < sides; s++)
            {
                int a = s, b = (s + 1) % sides, c = a + sides, d = b + sides;
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }
            return new ForestMeshTools.MeshData { positions = positions.ToArray(), normals = positions.Select(p => Vector3.up).ToArray(), subMeshes = new[] { triangles.ToArray() } };
        }

        static ForestMeshTools.MeshData Needles(Vector3 tipA, Vector3 tipB, float length)
        {
            var positions = new List<Vector3>();
            var triangles = new List<int>();
            foreach (Vector3 tip in new[] { tipA, tipB })
                for (int k = 0; k < 12; k++)
                {
                    Vector3 root = Vector3.Lerp(new Vector3(0.05f, tip.y, 0f), tip, 0.4f + 0.05f * k);
                    Vector3 dir = Quaternion.Euler(0f, 30f * k, 40f) * Vector3.right;
                    int i = positions.Count;
                    positions.Add(root);
                    positions.Add(root + dir * length);
                    positions.Add(root + dir * length + Vector3.up * 0.003f);
                    positions.Add(root + Vector3.up * 0.003f);
                    triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                }
            return new ForestMeshTools.MeshData { positions = positions.ToArray(), normals = positions.Select(p => Vector3.up).ToArray(), subMeshes = new[] { triangles.ToArray() } };
        }

        static ForestMeshTools.MeshData Clump(bool fern, int count)
        {
            var positions = new List<Vector3>();
            var triangles = new List<int>();
            for (int k = 0; k < count; k++)
            {
                float a = k * Mathf.PI * 2f / count;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 side = Vector3.Cross(Vector3.up, dir) * 0.01f;
                Vector3 b = fern ? dir * 0.03f : dir * 0.05f;
                for (int s = 0; s <= 4; s++)
                {
                    float t = s / 4f;
                    Vector3 p = fern ? b + dir * (0.5f * t) + Vector3.up * (0.4f * t - 0.2f * t * t) : b + Vector3.up * (0.3f * t) + dir * (0.02f * t);
                    positions.Add(p - side);
                    positions.Add(p + side);
                }
                int first = k * 10;
                for (int s = 0; s < 4; s++)
                {
                    int i = first + 2 * s;
                    triangles.AddRange(new[] { i, i + 2, i + 1, i + 1, i + 2, i + 3 });
                }
            }
            return new ForestMeshTools.MeshData { positions = positions.ToArray(), normals = positions.Select(p => Vector3.up).ToArray(), subMeshes = new[] { triangles.ToArray() } };
        }

        [Test]
        public void WindData_CompactRoundTrip()
        {
            ForestMeshTools.MeshData data = ForestTestSupport.Icosphere(3, 1f);
            data.uv0 = data.positions.Select(p => new Vector2(p.x * 0.5f + 0.5f, p.y * 0.5f + 0.5f)).ToArray();
            ForestMeshTools.MeshData two = ForestMeshTools.Merge(data, data);
            var colours = new Color32[two.positions.Length];
            for (int i = 0; i < colours.Length; i++) colours[i] = new Color32((byte)(i * 7), (byte)(i * 13), (byte)(i * 29), (byte)(i * 31));
            Mesh mesh = ForestMeshTools.ToMesh(two, "roundtrip", true, colours);
            try
            {
                Vector3[] positions = mesh.vertices, normals = mesh.normals;
                Vector4[] tangents = mesh.tangents;
                Vector2[] uv = mesh.uv;
                Color32[] before = mesh.colors32;
                int[][] indices = Enumerable.Range(0, mesh.subMeshCount).Select(s => mesh.GetIndices(s)).ToArray();
                IndexFormat format = mesh.indexFormat;
                Bounds bounds = mesh.bounds;

                Mesh same = ForestWindData.Compact(mesh);
                var problems = new List<string>();
                if (!ReferenceEquals(same, mesh)) problems.Add("Compact returned another mesh");
                if (mesh.GetVertexBufferStride(0) != 44) problems.Add($"stride {mesh.GetVertexBufferStride(0)} B, expected 44");
                if (mesh.GetVertexAttributeFormat(VertexAttribute.Tangent) != VertexAttributeFormat.Float16) problems.Add("tangents are not Float16");
                if (!mesh.vertices.SequenceEqual(positions)) problems.Add("positions changed");
                if (!mesh.normals.SequenceEqual(normals)) problems.Add("normals changed");
                if (!mesh.uv.SequenceEqual(uv)) problems.Add("UV0 changed");
                if (!mesh.colors32.SequenceEqual(before)) problems.Add("colours changed");
                Vector4[] after = mesh.tangents;
                float worst = 0f;
                for (int i = 0; i < after.Length; i++) worst = Mathf.Max(worst, (after[i] - tangents[i]).magnitude);
                if (worst > 1e-3f) problems.Add($"tangents moved by up to {worst}");
                if (mesh.subMeshCount != indices.Length) problems.Add("the sub-mesh count changed");
                for (int s = 0; s < indices.Length && s < mesh.subMeshCount; s++)
                    if (!mesh.GetIndices(s).SequenceEqual(indices[s])) problems.Add($"indices of sub-mesh {s} changed");
                if (mesh.indexFormat != format) problems.Add("the index format changed");
                if (mesh.bounds != bounds) problems.Add($"bounds changed from {bounds} to {mesh.bounds}");
                ForestTestSupport.AssertNone(problems, "compact round-trip problem(s)");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        // ---- The CPU gust mirror ----

        [Test]
        public void WindGlobals_GustHasLullsAndTravellingFronts()
        {
            ForestLightingPresets.Preset night = ForestLightingPresets.Get("Night");
            ForestWindState state = night.WindState();
            const float tau = Mathf.PI * 2f;
            var problems = new List<string>();
            var report = new List<string>();
            var points = new List<Vector2>();
            foreach (float x in new[] { -500f, -131f, 0f, 131f, 500f })
                foreach (float z in new[] { -500f, -50f, 0f, 80f, 500f })
                    points.Add(new Vector2(x, z));
            const float duration = 3600f, step = 0.1f;
            int n = Mathf.RoundToInt(duration / step);
            var medians = new List<float>();
            foreach (Vector2 xz in points)
            {
                var w = new float[n];
                double sum = 0;
                for (int i = 0; i < n; i++)
                {
                    float t = i * step;
                    float e = ForestWindGlobals.SampleGust(state, xz, t);
                    if (e < 0f || e > 1f) { problems.Add($"e = {e} at {xz}, t {t}"); break; }
                    // The peak wander as gust_tune.py applies it: +-25 % over about 40 s.
                    float wander = 1f + 0.25f * (0.6f * Mathf.Sin(tau * t / 40f) + 0.4f * Mathf.Sin(tau * t / (40f * 1.618f) + 1f));
                    w[i] = state.calm + (state.peak * wander - state.calm) * e;
                    sum += w[i];
                }
                float mean = (float)(sum / n);
                float lowShare = w.Count(v => v < 0.3f * mean) / (float)n;
                float high = state.calm + 0.5f * (state.peak - state.calm), low = state.calm + 0.25f * (state.peak - state.calm);
                var events = new List<float>();
                bool armed = true;
                for (int i = 0; i < n; i++)
                {
                    if (armed && w[i] > high) { events.Add(i * step); armed = false; }
                    else if (!armed && w[i] < low) armed = true;
                }
                var gaps = events.Zip(events.Skip(1), (a, b) => b - a).OrderBy(g => g).ToList();
                if (gaps.Count == 0) { problems.Add($"no gust events at {xz}"); continue; }
                float median = gaps[gaps.Count / 2];
                medians.Add(median);
                if (lowShare < 0.5f) problems.Add($"at {xz} W is below 0.3 x its mean only {lowShare:P1} of the time (at least 50 %)");
                if (gaps[0] < 20f) problems.Add($"at {xz} two gusts come {gaps[0]:0.0} s apart (at least 20 s)");
                report.Add($"{xz}: mean W {mean:0.000}, lull share {lowShare:P1}, {events.Count} gusts, gaps min {gaps[0]:0.0} s, median {median:0.0} s");
            }
            medians.Sort();
            float overallMedian = medians.Count > 0 ? medians[medians.Count / 2] : 0f;
            if (overallMedian < 25f || overallMedian > 45f) problems.Add($"the median gap between gusts is {overallMedian:0.0} s (25 to 45 s)");

            // Fronts travel downwind at gustSpeed at |xz| = 500 m, whatever the lean bearing does (the gust frame never rotates).
            Vector2 down = ForestWindGlobals.Direction(state.gustBearing);
            foreach (Vector2 origin in new[] { new Vector2(500f, 0f), new Vector2(-353.6f, 353.6f), new Vector2(0f, -500f) })
            {
                float measured = FrontSpeed(state, origin, down, tau);
                report.Add($"front speed at {origin}: {measured:0.000} m/s");
                if (Mathf.Abs(measured - state.gustSpeed) > 0.1f * state.gustSpeed)
                    problems.Add($"fronts at {origin} travel at {measured:0.00} m/s, not {state.gustSpeed} +- 10 %");
            }

            // Clear zeroes every global.
            ForestWindGlobals.Write(state);
            ForestWindGlobals.WriteInteraction(Texture2D.whiteTexture, Vector4.one, Texture2D.whiteTexture, Vector4.one);
            ForestWindGlobals.WriteGustTexture(Texture2D.whiteTexture, Vector4.one);
            ForestWindGlobals.Clear();
            foreach (int id in new[] { ForestWindGlobals.Wind, ForestWindGlobals.Gust, ForestWindGlobals.Params, ForestWindGlobals.Fade, ForestWindGlobals.GustRect,
                         ForestWindGlobals.InteractionRect, ForestWindGlobals.InteractionRectPrev })
                if (Shader.GetGlobalVector(id) != Vector4.zero)
                    problems.Add($"global {id} is {Shader.GetGlobalVector(id)} after Clear");
            Debug.Log("Forest wind gust statistics (Night):\n  " + string.Join("\n  ", report) + $"\n  median gap over the points {overallMedian:0.0} s");
            ForestTestSupport.AssertNone(problems, "gust statistic(s) outside the Night preset's character");
        }

        // The lag that best aligns e at a point 30 m downwind with e at the point, while the lean bearing swings +-15 degrees at up to
        // 0.018 rad/s (it must not matter: the gust frame is the preset's fixed bearing).
        static float FrontSpeed(ForestWindState state, Vector2 origin, Vector2 down, float tau)
        {
            const float distance = 30f, step = 0.05f, window = 300f;
            int n = Mathf.RoundToInt(window / step);
            var a = new float[n];
            var b = new float[n];
            ForestWindState swinging = state;
            for (int i = 0; i < n; i++)
            {
                float t = i * step;
                swinging.leanBearing = state.gustBearing + 15f * Mathf.Sin(tau * t / 90f * 1.0f);   // up to 15 x 2 pi / 90 deg/s = 0.018 rad/s
                a[i] = ForestWindGlobals.SampleGust(swinging, origin, t);
                b[i] = ForestWindGlobals.SampleGust(swinging, origin + down * distance, t);
            }
            int bestLag = 0;
            double best = double.NegativeInfinity;
            for (int lag = 1; lag < Mathf.RoundToInt(30f / step); lag++)
            {
                double s = 0;
                for (int i = 0; i + lag < n; i++) s += a[i] * b[i + lag];
                s /= (n - lag);
                if (s > best) { best = s; bestLag = lag; }
            }
            return distance / (bestLag * step);
        }

        // ---- GPU probe ----

        [Test]
        public void WindShader_GustMatchesCpuOnGpu()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("No compute shaders on this device.");
            ComputeShader probe = ForestWindProbeSupport.Load();
            ForestWindState state = ForestLightingPresets.Get("Night").WindState();
            var random = new System.Random(1993);
            var samples = new Vector3[4096];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = new Vector3((float)(random.NextDouble() * 1000.0 - 500.0), (float)(random.NextDouble() * 1000.0 - 500.0), (float)(random.NextDouble() * 600.0));
            float[] gpu = ForestWindProbeSupport.RunGust(probe, state, samples);
            float worst = 0f;
            int worstIndex = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float cpu = ForestWindGlobals.SampleGust(state, new Vector2(samples[i].x, samples[i].y), samples[i].z);
                float d = Mathf.Abs(gpu[i] - cpu);
                if (d > worst) { worst = d; worstIndex = i; }
            }
            // At 3 h: reported, not asserted.
            var late = samples.Take(512).Select(s => new Vector3(s.x, s.y, 10800f + s.z * 0.01f)).ToArray();
            float[] gpuLate = ForestWindProbeSupport.RunGust(probe, state, late);
            float worstLate = 0f;
            for (int i = 0; i < late.Length; i++)
                worstLate = Mathf.Max(worstLate, Mathf.Abs(gpuLate[i] - ForestWindGlobals.SampleGust(state, new Vector2(late[i].x, late[i].y), late[i].z)));
            Debug.Log($"Forest wind GPU gust against the CPU mirror: worst |e| difference {worst:0.000000} for t <= 600 s (at {samples[worstIndex]}), {worstLate:0.000000} at t = 3 h.");
            Assert.LessOrEqual(worst, 2e-3f, $"GamexGust on the GPU differs from ForestWindGlobals.SampleGust by {worst} at {samples[worstIndex]} (tolerance 2e-3).");
            Assert.IsTrue(gpu.Any(e => e > 0.5f) && gpu.Any(e => e < 0.01f), "The GPU gust never rose or never fell: the probe read zeros.");
        }

        [Test]
        public void WindShader_ProfilesGiveFinitePositions()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("No compute shaders on this device.");
            ComputeShader probe = ForestWindProbeSupport.Load();
            ForestWindState state = ForestLightingPresets.Get("Night").WindState();
            state.calm = state.peak = 1f;

            // Synthetic vertices: R, G, B and A over [0, 1], heights 0 to 20 m.
            var positions = new List<Vector3>();
            var colours = new List<Color>();
            for (int i = 0; i < 2048; i++)
            {
                float u = (i % 16) / 15f, v = (i / 16 % 16) / 15f, w = (i / 256) / 7f;
                positions.Add(new Vector3((u - 0.5f) * 4f, w * 20f, (v - 0.5f) * 4f));
                colours.Add(new Color(u, v, w, (u + v + w) % 1f));
            }
            Vector3[] normals = positions.Select(p => Vector3.up).ToArray();
            Vector3[] tangents = positions.Select(p => Vector3.right).ToArray();

            // A valid field at full press: everything pushes away from the pivot, band top 3 m, press 1.
            var texture = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = Enumerable.Repeat(new Color(0.7071f, 0.7071f, 3f, 1f), 16).ToArray();
            texture.SetPixels(pixels);
            texture.Apply();
            var problems = new List<string>();
            try
            {
                var field = new ForestWindProbeSupport.Field { current = texture, previous = texture, rect = new Vector4(-20f, -20f, 1f / 40f, 1f), previousRect = new Vector4(-20f, -20f, 1f / 40f, 1f) };
                var profiles = new List<(string, Vector4, Vector4, Vector4, Vector4, Vector4)>();
                foreach (string model in ForestWindProfiles.Models)
                {
                    ForestWindProfiles.Profile p = ForestWindProfiles.ForModel(model);
                    profiles.Add((model, p.Sway, p.Flutter, p.Motion, p.Push, p.Stem));
                }
                profiles.Add(("all-zero", Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero));
                foreach ((string name, Vector4 sway, Vector4 flutter, Vector4 motion, Vector4 push, Vector4 stem) in profiles)
                    foreach (float time in new[] { 0f, 1.3f, 600f })
                        foreach (float scale in new[] { 0.6f, 1f })
                        {
                            var instance = new ForestWindProbeSupport.Instance
                            {
                                pivot = new Vector3(1.5f, 0.5f, -2f), scale = scale, yaw = 37f, camera = new Vector3(4f, 1.65f, 2f), time = time,
                                sway = sway, flutter = flutter, motion = motion, push = push, stem = stem,
                            };
                            ForestWindProbeSupport.RunDeform(probe, state, instance, field, positions.ToArray(), normals, tangents, colours.ToArray(),
                                out Vector3[] moved, out Vector3[] movedNormals, out Vector3[] velocity);
                            int bad = 0, changed = 0;
                            for (int i = 0; i < moved.Length; i++)
                            {
                                if (!Finite(moved[i]) || !Finite(movedNormals[i]) || !Finite(velocity[i])) bad++;
                                if (moved[i] != positions[i]) changed++;
                            }
                            if (bad > 0) problems.Add($"{name} at t {time}, scale {scale}: {bad} non-finite outputs");
                            if (name == "all-zero" && changed > 0) problems.Add($"the all-zero profile moved {changed} vertices at t {time}");
                            if (name != "all-zero" && changed == 0) problems.Add($"{name} at t {time}, scale {scale}: nothing moved (the probe read zeros?)");
                        }

                // With every global zero, nothing moves at all (the static regression's condition).
                ForestWindProfiles.Profile young = ForestWindProfiles.ForModel("fir_sapling_medium");
                var still = new ForestWindProbeSupport.Instance { pivot = Vector3.zero, scale = 1f, camera = new Vector3(2f, 1.65f, 0f), time = 5f, sway = young.Sway, motion = young.Motion, push = young.Push, stem = young.Stem };
                ForestWindProbeSupport.RunDeform(probe, default, still, default, positions.ToArray(), normals, tangents, colours.ToArray(), out Vector3[] rest, out _, out Vector3[] restVelocity);
                for (int i = 0; i < rest.Length; i++)
                    if (rest[i] != positions[i] || restVelocity[i] != Vector3.zero) { problems.Add($"with every global zero vertex {i} moved to {rest[i]}"); break; }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
            ForestTestSupport.AssertNone(problems, "profile(s) giving bad positions");
        }

        static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    }
}
