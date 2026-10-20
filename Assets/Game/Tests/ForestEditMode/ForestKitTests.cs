using System;
using System.Collections.Generic;
using System.IO;
using Gamex.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gamex.Tests.Forest
{
    /// <summary>
    /// Tests of what the download, the import and the kit build produced: files, textures, materials,
    /// prefabs and tree levels. Each one ignores itself while the thing it looks at does not exist, so the
    /// suite runs at every milestone.
    /// </summary>
    [Category("Forest")]
    public class ForestKitTests
    {
        // ---- Downloads ----

        [Test]
        public void Manifest_DownloadedFilesMatchSize()
        {
            var problems = new List<string>();
            int present = 0;
            foreach (ForestAssetEntry entry in ForestManifest.All)
            {
                foreach (ForestAssetFile file in entry.files)
                {
                    var info = new FileInfo(ForestManifest.DiskPath(file.relPath));
                    if (!info.Exists)
                        continue;
                    present++;
                    if (info.Length != file.bytes)
                        problems.Add($"{file.relPath} is {info.Length} bytes, the manifest says {file.bytes}");
                }
            }
            if (present == 0)
                Assert.Ignore("No asset has been downloaded yet (Tools/forest/fetch_assets.py).");
            ForestTestSupport.AssertNone(problems, "downloaded file(s) do not have the manifest's size");
        }

        // ---- Textures ----

        const string BaseColorJpg = "__basecolor.jpg", BaseColorPng = "__basecolor.png", NormalPng = "__normal.png", NormalJpg = "__normal.jpg",
            Mask = "__mask.png", TerrainMask = "__tmask.png", Sky = "__sky.hdr";
        static readonly string[] Suffixes = { BaseColorJpg, BaseColorPng, NormalPng, NormalJpg, Mask, TerrainMask, Sky };

        [Test]
        public void Textures_FollowPolicy()
        {
            var folders = new List<string>();
            if (AssetDatabase.IsValidFolder(ForestPaths.PolyHavenRoot)) folders.Add(ForestPaths.PolyHavenRoot);
            if (AssetDatabase.IsValidFolder(ForestPaths.KitTextures)) folders.Add(ForestPaths.KitTextures);
            string[] guids = folders.Count > 0 ? AssetDatabase.FindAssets("t:Texture", folders.ToArray()) : Array.Empty<string>();
            if (guids.Length == 0)
                Assert.Ignore("No forest texture has been imported yet (Gamex/Forest/1 Import Assets).");

            var problems = new List<string>();
            int at4096 = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;   // a texture inside a model file, not a file of its own

                string suffix = Array.Find(Suffixes, candidate => path.EndsWith(candidate, StringComparison.Ordinal));
                if (suffix == null)
                {
                    problems.Add($"{path}: a texture whose name ends in none of the suffixes the import rules know");
                    continue;
                }
                string name = Path.GetFileName(path);
                name = name.Substring(0, name.Length - suffix.Length);
                ForestAssetEntry entry = OwnerOf(path, name, suffix);
                if (entry == null)
                {
                    problems.Add($"{path}: no manifest entry owns this texture");
                    continue;
                }

                void Expect(bool condition, string what)
                {
                    if (!condition)
                        problems.Add($"{path}: {what}");
                }

                importer.GetSourceTextureWidthAndHeight(out int width, out int height);
                int imported = Mathf.Min(Mathf.Max(width, height), importer.maxTextureSize);
                Expect(imported <= 4096, $"imported at {imported}, above 4096");
                Expect(!importer.GetPlatformTextureSettings("Standalone").overridden, "has a Standalone platform override; default-platform settings only");

                if (suffix == Sky)
                {
                    Expect(importer.textureShape == TextureImporterShape.TextureCube, $"shape is {importer.textureShape}, expected a cubemap");
                    Expect(importer.generateCubemap == TextureImporterGenerateCubemap.Cylindrical, $"cubemap mapping is {importer.generateCubemap}, expected Cylindrical");
                    Expect(!importer.sRGBTexture, "an HDR sky must not be sRGB");
                    Expect(importer.mipmapEnabled, "mipmaps are off");
                    Expect(importer.filterMode == FilterMode.Trilinear, $"filter mode is {importer.filterMode}, expected Trilinear");
                    Expect(importer.wrapMode == TextureWrapMode.Clamp, $"wrap mode is {importer.wrapMode}, expected Clamp");
                    Expect(importer.maxTextureSize == 4096, $"maxTextureSize is {importer.maxTextureSize}, expected 4096");
                    // BC6H since probe P8 found it clean at M0 (issues/M0-integration.md). The import rule and this line change together.
                    Expect(importer.textureCompression == TextureImporterCompression.CompressedHQ, $"compression is {importer.textureCompression}, expected CompressedHQ");
                    continue;
                }

                // Layer textures are sampled by the terrain, which cannot stream mips, and are seen at grazing angles.
                bool terrain = entry.kind == "texture";
                bool isMask = suffix == Mask || suffix == TerrainMask;
                int expectedSize = isMask ? 2048 : entry.maxTextureSize;
                Expect(importer.maxTextureSize == expectedSize, $"maxTextureSize is {importer.maxTextureSize}, expected {expectedSize}");
                Expect(importer.ignorePngGamma, "ignorePngGamma is off: a gAMA chunk would shift the data");
                Expect(importer.mipmapEnabled, "mipmaps are off");
                Expect(importer.streamingMipmaps == !terrain, $"streamingMipmaps is {importer.streamingMipmaps}, expected {!terrain}");
                Expect(importer.textureCompression == TextureImporterCompression.CompressedHQ, $"compression is {importer.textureCompression}, expected CompressedHQ");
                Expect(importer.anisoLevel == (terrain ? 8 : 4), $"anisoLevel is {importer.anisoLevel}, expected {(terrain ? 8 : 4)}");
                Expect(importer.textureShape == TextureImporterShape.Texture2D, $"shape is {importer.textureShape}, expected 2D");
                if (imported >= 4096)
                    at4096++;

                if (suffix == NormalPng || suffix == NormalJpg)
                {
                    Expect(importer.textureType == TextureImporterType.NormalMap, $"type is {importer.textureType}, expected NormalMap");
                    Expect(!importer.flipGreenChannel, "the green channel is flipped; Poly Haven nor_gl is already Y+");
                    continue;
                }

                Expect(importer.textureType == TextureImporterType.Default, $"type is {importer.textureType}, expected Default");
                if (suffix == BaseColorJpg)
                {
                    Expect(importer.sRGBTexture, "a base colour must be sRGB");
                    Expect(importer.alphaSource == TextureImporterAlphaSource.None, $"alphaSource is {importer.alphaSource}, expected None");
                }
                else if (suffix == BaseColorPng)
                {
                    ForestMaterialEntry material = ForestManifest.FindMaterial(name);
                    Expect(importer.sRGBTexture, "a base colour must be sRGB");
                    Expect(importer.alphaSource == TextureImporterAlphaSource.FromInput, $"alphaSource is {importer.alphaSource}, expected FromInput");
                    Expect(importer.alphaIsTransparency, "alphaIsTransparency is off");
                    Expect(importer.mipMapsPreserveCoverage, "mipMapsPreserveCoverage is off: leaves thin out with distance");
                    Expect(material != null && Mathf.Abs(importer.alphaTestReferenceValue - material.alphaCutoff) < 1e-4f,
                        $"alphaTestReferenceValue is {importer.alphaTestReferenceValue}, expected the material's cutoff {(material != null ? material.alphaCutoff : float.NaN)}");
                }
                else
                {
                    Expect(!importer.sRGBTexture, "mask data must be linear, not sRGB");
                    Expect(importer.alphaSource == TextureImporterAlphaSource.FromInput, $"alphaSource is {importer.alphaSource}, expected FromInput (smoothness is in alpha)");
                    Expect(!importer.alphaIsTransparency, "alphaIsTransparency is on for a mask");
                }
            }

            if (at4096 > ForestBudgets.Textures4096Budget)
                problems.Add($"{at4096} textures are imported at 4096, the budget is {ForestBudgets.Textures4096Budget}");
            ForestTestSupport.AssertNone(problems, "texture import setting(s) break the policy");
        }

        // The manifest entry a texture belongs to: by its folder under PolyHaven, or by its name in the kit folder.
        static ForestAssetEntry OwnerOf(string path, string name, string suffix)
        {
            if (path.StartsWith(ForestPaths.PolyHavenRoot + "/", StringComparison.Ordinal))
            {
                string rest = path.Substring(ForestPaths.PolyHavenRoot.Length + 1);
                int slash = rest.IndexOf('/');
                return slash > 0 ? ForestManifest.Find(rest.Substring(0, slash)) : null;
            }
            if (suffix == TerrainMask)
                return ForestManifest.Find(name);
            foreach (ForestAssetEntry entry in ForestManifest.All)
                foreach (ForestMaterialEntry material in entry.materials ?? Array.Empty<ForestMaterialEntry>())
                    if (material.name == name)
                        return entry;
            return null;
        }

        // ---- Materials ----

        [Test]
        public void Materials_AreValidHdrp()
        {
            ForestAudit.Report report = ForestAudit.AuditKit();
            if (report.uniqueMaterials == 0 && report.terrainLayers == 0)
                Assert.Ignore("No kit material or terrain layer has been built yet (Gamex/Forest/1 Import Assets).");

            var problems = new List<string>();
            foreach (ForestAudit.Finding finding in report.findings)
                if (finding.severity == "error" && (finding.check.StartsWith("material.", StringComparison.Ordinal) || finding.check.StartsWith("texture.", StringComparison.Ordinal)))
                    problems.Add($"[{finding.check}] {finding.message}");
            ForestTestSupport.AssertNone(problems, "kit material problem(s)");
        }

        [Test]
        public void FoliageMaterial_MatchesHdrpLeafSample()
        {
            // The state of HDRP's own Leaf.mat sample: alpha-tested, double-sided, transmitting, with a normal map.
            string[] keywords = { "_ALPHATEST_ON", "_DOUBLESIDED_ON", "_MATERIAL_FEATURE_TRANSMISSION", "_NORMALMAP" };
            var problems = new List<string>();
            int checkedMaterials = 0;
            foreach (ForestAssetEntry entry in ForestManifest.All)
            {
                foreach (ForestMaterialEntry manifest in entry.materials ?? Array.Empty<ForestMaterialEntry>())
                {
                    if (manifest.surface != "foliage")
                        continue;
                    Assert.IsTrue(ForestMaterials.IsFoliage(manifest.name), $"{manifest.name} is foliage in the manifest but not for ForestMaterials.IsFoliage.");

                    // Tint 1 exists for twigs and ferns only; a missing variant is not a fault here.
                    for (int tint = 0; tint <= 1; tint++)
                    {
                        Material material = ForestMaterials.Get(manifest.name, tint);
                        if (material == null)
                            continue;
                        checkedMaterials++;
                        string label = AssetDatabase.GetAssetPath(material);
                        // W1 (spec 8.6): the wind graph always samples its normal map and has no _NORMALMAP keyword; on it the
                        // keyword is replaced by "the _NormalMap slot holds the material's normal map". Everything else is unchanged.
                        bool windGraph = ForestWindShaderGraphs.IsWindShader(material.shader);
                        foreach (string keyword in keywords)
                        {
                            if (windGraph && keyword == "_NORMALMAP")
                            {
                                Texture normal = material.GetTexture("_NormalMap");
                                string expected = ForestManifest.AssetPath(manifest.maps.normal);
                                if (normal == null || AssetDatabase.GetAssetPath(normal) != expected)
                                    problems.Add($"{label}: the _NormalMap slot holds {(normal != null ? AssetDatabase.GetAssetPath(normal) : "nothing")}, not {expected}");
                                continue;
                            }
                            if (!material.IsKeywordEnabled(keyword))
                                problems.Add($"{label}: keyword {keyword} is off");
                        }
                        if (material.renderQueue != 2475)
                            problems.Add($"{label}: render queue is {material.renderQueue}, expected 2475 (alpha test)");
                        if (!material.HasProperty("_CullMode") || material.GetFloat("_CullMode") != 0f)
                            problems.Add($"{label}: _CullMode is not 0 (off)");
                        if (Mathf.Abs(material.GetFloat("_AlphaCutoff") - manifest.alphaCutoff) > 1e-4f)
                            problems.Add($"{label}: alpha cutoff is {material.GetFloat("_AlphaCutoff")}, the manifest says {manifest.alphaCutoff}");
                    }
                }
            }
            if (checkedMaterials == 0)
                Assert.Ignore("No foliage material has been built yet (fetch milestone M2, then Gamex/Forest/1 Import Assets).");
            ForestTestSupport.AssertNone(problems, "foliage material(s) differ from the expected HDRP leaf state");
        }

        // ---- Prefabs ----

        [Test]
        public void Prefabs_MatchManifestBounds()
        {
            List<string> built = ForestTestSupport.RequireBuiltPrefabs(prefabId => true, "forest prefab");
            ForestKitReport report = ForestKitReport.Load();
            Assert.IsNotNull(report, $"Prefabs exist but {ForestPaths.KitReportPath} does not.");

            var problems = new List<string>();
            var mirrors = new Dictionary<string, List<string>>();
            foreach (string prefabId in built)
            {
                ForestKitPrefabReport entry = report.Find(prefabId);
                if (entry == null)
                    problems.Add($"{prefabId} is built but missing from the kit report");

                ForestObjectEntry source = ForestManifest.FindObjectForPrefab(prefabId);
                if (source == null)
                    continue;   // a patch: merged from clumps, nothing to compare with

                // A log pitched on purpose (section 7.3) no longer has the box of its scan.
                bool pitched = ForestTestSupport.Contains(ForestPrefabIds.Logs, prefabId) && ForestKitSpec.LogPitchDegrees != 0f;
                Mesh mesh = ForestPrefabs.LoadLodMesh(prefabId, 0);
                if (mesh == null)
                    problems.Add($"{prefabId}: the LOD0 mesh cannot be loaded");
                else if (!pitched && !ForestMeshTools.MatchesSourceBounds(mesh.bounds, ForestManifest.SourceBounds(source), out string why))
                    problems.Add($"{prefabId}: LOD0 bounds {mesh.bounds} do not match the manifest ({why})");

                if (entry != null && entry.mirror != "symmetric" && !pitched)
                {
                    if (!mirrors.TryGetValue(entry.mirror ?? "", out List<string> ids))
                        mirrors[entry.mirror ?? ""] = ids = new List<string>();
                    ids.Add(prefabId);
                }
            }

            // One importer mirrors one way. Two answers mean a prefab was derived differently from the others.
            if (mirrors.Count > 1)
            {
                var groups = new List<string>();
                foreach (KeyValuePair<string, List<string>> pair in mirrors)
                    groups.Add($"'{pair.Key}': {string.Join(", ", pair.Value)}");
                problems.Add("the kit report names more than one mirrored axis: " + string.Join("; ", groups));
            }
            ForestTestSupport.AssertNone(problems, "prefab(s) do not match the manifest");
        }

        [Test]
        public void Prefabs_FollowContract()
        {
            List<string> built = ForestTestSupport.RequireBuiltPrefabs(prefabId => true, "forest prefab");
            var problems = new List<string>();
            foreach (string prefabId in built)
                CheckContract(prefabId, problems);
            ForestTestSupport.AssertNone(problems, "prefab contract violation(s)");
        }

        /// <summary>
        /// The mask map of every material carries ambient occlusion wherever the LOD0 mesh that wears it
        /// has UVs. HDRP multiplies sky light by that channel, so a mesh that reaches parts of the map
        /// where nothing was baked renders with black patches: at M2 the six rocks of rock_moss_set_01
        /// did, over 7 to 33 % of their surface, because the AO of the downloaded map belongs to another
        /// UV layout. The same limits as the kit build's own gate (ForestPrefabs.RequireAoUnderUvs);
        /// healthy models measured 0.0 to 0.3 %.
        /// </summary>
        [Test]
        public void Prefabs_OcclusionMapsFitTheirMeshes()
        {
            const byte aoFloor = 8;
            const double maxGapShare = 0.02;

            List<string> built = ForestTestSupport.RequireBuiltPrefabs(prefabId => true, "forest prefab");
            var masks = new Dictionary<string, Color32[]>();
            var widths = new Dictionary<string, int>();
            var problems = new List<string>();
            int measured = 0;
            foreach (string prefabId in built)
            {
                GameObject prefab = ForestPrefabs.Load(prefabId);
                Transform level = prefab != null ? prefab.transform.Find("LOD0") : null;
                Mesh mesh = ForestPrefabs.LoadLodMesh(prefabId, 0);
                var renderer = level != null ? level.GetComponent<MeshRenderer>() : null;
                if (mesh == null || renderer == null)
                {
                    problems.Add($"{prefabId}: LOD0 has no mesh or no renderer");
                    continue;
                }

                Vector3[] positions = mesh.vertices;
                Vector2[] uv = mesh.uv;
                if (uv.Length != positions.Length)
                    continue;   // no UVs: nothing samples a map through them
                Material[] materials = renderer.sharedMaterials;
                for (int subMesh = 0; subMesh < mesh.subMeshCount && subMesh < materials.Length; subMesh++)
                {
                    Material material = materials[subMesh];
                    if (material == null || !material.HasProperty("_MaskMap"))
                        continue;
                    // A triplanar material (_UVBase 5) projects its maps and does not read the mesh UVs.
                    if (material.HasProperty("_UVBase") && material.GetFloat("_UVBase") >= 4.5f)
                        continue;
                    string path = AssetDatabase.GetAssetPath(material.GetTexture("_MaskMap"));
                    if (string.IsNullOrEmpty(path))
                        continue;
                    if (!masks.TryGetValue(path, out Color32[] pixels))
                    {
                        pixels = ForestTexturePacker.ReadPixels(Path.GetFullPath(path), out int width, out int _);
                        masks[path] = pixels;
                        widths[path] = width;
                    }
                    int maskWidth = widths[path], maskHeight = pixels.Length / maskWidth;

                    // An alpha-tested material is drawn only where its base colour passes the cutoff; what
                    // lies under the cut-away part of a leaf card is never shaded and is not counted.
                    Color32[] leaves = null;
                    int leafWidth = 0, leafHeight = 0;
                    float cutoff = 0f;
                    if (material.HasProperty("_AlphaCutoffEnable") && material.GetFloat("_AlphaCutoffEnable") > 0.5f && material.HasProperty("_BaseColorMap"))
                    {
                        string leafPath = AssetDatabase.GetAssetPath(material.GetTexture("_BaseColorMap"));
                        if (!string.IsNullOrEmpty(leafPath))
                        {
                            if (!masks.TryGetValue(leafPath, out leaves))
                            {
                                leaves = ForestTexturePacker.ReadPixels(Path.GetFullPath(leafPath), out int width, out int _);
                                masks[leafPath] = leaves;
                                widths[leafPath] = width;
                            }
                            leafWidth = widths[leafPath];
                            leafHeight = leaves.Length / leafWidth;
                            cutoff = material.GetFloat("_AlphaCutoff") * 255f;
                        }
                    }

                    int[] indices = mesh.GetTriangles(subMesh);
                    double total = 0.0, gaps = 0.0;
                    for (int t = 0; t + 2 < indices.Length; t += 3)
                    {
                        int a = indices[t], b = indices[t + 1], c = indices[t + 2];
                        double area = 0.5 * Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]).magnitude;
                        Vector2 centre = (uv[a] + uv[b] + uv[c]) / 3f;
                        float u = Mathf.Repeat(centre.x, 1f), v = Mathf.Repeat(centre.y, 1f);
                        if (leaves != null)
                        {
                            int leafX = Mathf.Clamp(Mathf.FloorToInt(u * leafWidth), 0, leafWidth - 1);
                            int leafY = Mathf.Clamp(Mathf.FloorToInt(v * leafHeight), 0, leafHeight - 1);
                            if (leaves[leafY * leafWidth + leafX].a < cutoff)
                                continue;
                        }
                        int x = Mathf.Clamp(Mathf.FloorToInt(u * maskWidth), 0, maskWidth - 1);
                        int y = Mathf.Clamp(Mathf.FloorToInt(v * maskHeight), 0, maskHeight - 1);
                        total += area;
                        if (pixels[y * maskWidth + x].g < aoFloor)
                            gaps += area;
                    }
                    if (total <= 0.0)
                        continue;
                    measured++;
                    if (gaps / total > maxGapShare)
                        problems.Add($"{prefabId}: {gaps / total:P1} of the surface that wears {material.name} has no ambient occlusion in {path} (limit {maxGapShare:P0})");
                }
            }
            Assert.Greater(measured, 0, "No prefab has a mask map under its LOD0 mesh, so nothing was measured.");
            ForestTestSupport.AssertNone(problems, "prefab surface(s) would render with black patches");
        }

        static void CheckContract(string prefabId, List<string> problems)
        {
            void Expect(bool condition, string what)
            {
                if (!condition)
                    problems.Add($"{prefabId}: {what}");
            }

            GameObject prefab = ForestPrefabs.Load(prefabId);
            if (prefab == null)
            {
                problems.Add($"{prefabId}: Exists is true but Load returned nothing");
                return;
            }
            Expect(AssetDatabase.GetAssetPath(prefab) == $"{ForestPaths.KitPrefabs}/{prefabId}.prefab", $"is stored at {AssetDatabase.GetAssetPath(prefab)}");
            Expect(ForestPrefabs.PrefabPath(prefabId) == $"{ForestPaths.KitPrefabs}/{prefabId}.prefab", $"PrefabPath says {ForestPrefabs.PrefabPath(prefabId)}");
            Expect(prefab.transform.localRotation == Quaternion.identity && prefab.transform.localScale == Vector3.one, "the root is rotated or scaled");
            Expect(prefab.layer == 0, $"the root is on layer {prefab.layer}, expected Default");
            Expect(GameObjectUtility.GetStaticEditorFlags(prefab) == 0, "the root carries static flags");

            // Children: LOD0 .. LODn, optional Collision, and LOD0_Shadow only beside a scan level.
            int levels = ForestKitSpec.LodCount(prefabId);
            bool scan = ForestKitSpec.ScanLodDistance(prefabId) > 0f;
            Expect(levels >= 1 && levels <= 4, $"the kit spec asks for {levels} levels");
            Expect(ForestPrefabs.LodCount(prefabId) == levels, $"is built with {ForestPrefabs.LodCount(prefabId)} levels, the kit spec says {levels}");
            var levelRenderers = new Renderer[Mathf.Clamp(levels, 0, 4)];
            Transform collision = null, shadow = null;
            foreach (Transform child in prefab.transform)
            {
                Expect(child.localPosition == Vector3.zero && child.localRotation == Quaternion.identity && child.localScale == Vector3.one,
                    $"child {child.name} does not have an identity transform");
                Expect(child.childCount == 0, $"child {child.name} has children of its own");

                if (child.name == "Collision" || child.name == "LOD0_Shadow")
                {
                    if (child.name == "Collision") collision = child;
                    else shadow = child;
                    Expect(GameObjectUtility.GetStaticEditorFlags(child.gameObject) == 0, $"{child.name} carries static flags");
                }
                else if (child.name.Length == 4 && child.name.StartsWith("LOD", StringComparison.Ordinal) && child.name[3] >= '0' && child.name[3] < '0' + levelRenderers.Length)
                {
                    int level = child.name[3] - '0';
                    Expect(levelRenderers[level] == null, $"two children are named {child.name}");
                    levelRenderers[level] = child.GetComponent<MeshRenderer>();
                    Expect(levelRenderers[level] != null && child.GetComponent<MeshFilter>() != null, $"{child.name} lacks a MeshFilter or MeshRenderer");
                    CheckRenderer(prefabId, child, level, problems);
                    bool mayContribute = level == 0 && ForestTestSupport.HasMeshCollider(prefabId);
                    StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(child.gameObject);
                    Expect(flags == (mayContribute ? StaticEditorFlags.ContributeGI : 0), $"{child.name} has static flags {flags}");
                }
                else
                    Expect(false, $"unexpected child '{child.name}'");
            }
            for (int level = 0; level < levelRenderers.Length; level++)
                Expect(levelRenderers[level] != null, $"child LOD{level} is missing");
            Expect(scan == (shadow != null), scan ? "has a scan level but no LOD0_Shadow child" : "has a LOD0_Shadow child without a scan level");
            if (shadow != null)
            {
                var shadowRenderer = shadow.GetComponent<MeshRenderer>();
                Expect(shadowRenderer != null && shadowRenderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly, "LOD0_Shadow is not a shadows-only renderer");
                Expect(levelRenderers.Length > 0 && levelRenderers[0] != null && levelRenderers[0].shadowCastingMode == ShadowCastingMode.Off, "the scan level casts shadows itself");
            }

            // LODGroup.
            var group = prefab.GetComponent<LODGroup>();
            Expect(group != null, "the root has no LODGroup");
            if (group != null)
            {
                LOD[] lods = group.GetLODs();
                Expect(lods.Length == levels, $"the LODGroup has {lods.Length} levels, expected {levels}");
                Expect(group.fadeMode == (ForestKitSpec.UseCrossFade ? LODFadeMode.CrossFade : LODFadeMode.None), $"fadeMode is {group.fadeMode}");
                Expect(!group.animateCrossFading, "animateCrossFading is on, which the GPU Resident Drawer cannot take");
                Expect(group.size > 0f, "the LODGroup has no explicit size");
                float previous = 1f;
                for (int i = 0; i < lods.Length; i++)
                {
                    Expect(lods[i].screenRelativeTransitionHeight < 0.99f && lods[i].screenRelativeTransitionHeight < previous && lods[i].screenRelativeTransitionHeight >= 0f,
                        $"level {i} has transition height {lods[i].screenRelativeTransitionHeight}; heights must fall strictly from below 0.99");
                    previous = lods[i].screenRelativeTransitionHeight;
                    if (ForestKitSpec.UseCrossFade)
                        Expect(Mathf.Abs(lods[i].fadeTransitionWidth - 0.2f) < 1e-4f, $"level {i} has fadeTransitionWidth {lods[i].fadeTransitionWidth}, expected 0.2");

                    var expected = new List<Renderer>();
                    if (i < levelRenderers.Length && levelRenderers[i] != null) expected.Add(levelRenderers[i]);
                    if (i == 0 && shadow != null && shadow.GetComponent<Renderer>() != null) expected.Add(shadow.GetComponent<Renderer>());
                    bool same = lods[i].renderers != null && lods[i].renderers.Length == expected.Count;
                    if (same)
                        foreach (Renderer renderer in expected)
                            same &= Array.IndexOf(lods[i].renderers, renderer) >= 0;
                    Expect(same, $"level {i} of the LODGroup does not hold exactly the renderer(s) of child LOD{i}");
                }
            }

            // Colliders.
            Collider[] colliders = prefab.GetComponentsInChildren<Collider>(true);
            float clearance = ForestKitSpec.ClearanceRadius(prefabId);
            if (ForestTestSupport.IsTree(prefabId))
            {
                var capsule = prefab.GetComponent<CapsuleCollider>();
                Expect(colliders.Length == 1 && capsule != null, "a tree has exactly one collider, a capsule on its root");
                Expect(collision == null, "a tree has no Collision child");
                if (capsule != null)
                {
                    Expect(capsule.direction == 1 && Mathf.Abs(capsule.height - 4f) < 1e-3f && Vector3.Distance(capsule.center, new Vector3(0f, 2f, 0f)) < 1e-3f,
                        $"the capsule is not 4 m tall and upright around (0, 2, 0): height {capsule.height}, centre {capsule.center}");
                    Expect(capsule.radius >= 0.12f - 1e-4f, $"the capsule radius {capsule.radius} is below 0.12 m");
                    Expect(capsule.radius <= clearance + 1e-4f, $"the capsule radius {capsule.radius} exceeds the clearance {clearance} the scatterer plans with");
                }
            }
            else if (ForestTestSupport.HasMeshCollider(prefabId))
            {
                var meshCollider = collision != null ? collision.GetComponent<MeshCollider>() : null;
                Expect(colliders.Length == 1 && meshCollider != null, "expected exactly one collider, a MeshCollider on child Collision");
                if (meshCollider != null)
                {
                    Mesh mesh = meshCollider.sharedMesh;
                    Expect(!meshCollider.convex, "the mesh collider is convex");
                    Expect(collision.GetComponent<Renderer>() == null, "the Collision child has a renderer");
                    Expect(mesh != null, "the mesh collider has no mesh");
                    if (mesh != null)
                    {
                        Expect(AssetDatabase.GetAssetPath(mesh) == $"{ForestPaths.KitMeshes}/{prefabId}__col.asset", $"the collision mesh is stored at {AssetDatabase.GetAssetPath(mesh)}");
                        Expect(mesh.isReadable, "the collision mesh is not readable, which a player build needs");
                        int triangles = ForestTestSupport.TriangleCount(mesh);
                        Expect(triangles > 0 && triangles <= 3000, $"the collision mesh has {triangles} triangles, expected 1 to 3,000");
                        float reach = 0f;
                        foreach (Vector3 vertex in mesh.vertices)
                            reach = Mathf.Max(reach, new Vector2(vertex.x, vertex.z).magnitude);
                        Expect(reach <= clearance + 0.01f, $"the collision mesh reaches {reach:0.000} m from the pivot, beyond the clearance radius {clearance:0.000} m");
                    }
                }
            }
            else
            {
                Expect(colliders.Length == 0 && collision == null, "ferns, grass and branches carry no collider");
                Expect(clearance == 0f, $"has no collider but a clearance radius of {clearance}");
            }
        }

        static void CheckRenderer(string prefabId, Transform child, int level, List<string> problems)
        {
            void Expect(bool condition, string what)
            {
                if (!condition)
                    problems.Add($"{prefabId}/{child.name}: {what}");
            }

            var renderer = child.GetComponent<MeshRenderer>();
            var filter = child.GetComponent<MeshFilter>();
            if (renderer == null || filter == null)
                return;

            Mesh mesh = filter.sharedMesh;
            Expect(mesh != null, "has no mesh");
            if (mesh != null)
            {
                Expect(AssetDatabase.GetAssetPath(mesh) == $"{ForestPaths.KitMeshes}/{prefabId}__lod{level}.asset", $"its mesh is stored at {AssetDatabase.GetAssetPath(mesh)}");
                Expect(mesh.subMeshCount <= 4, $"its mesh has {mesh.subMeshCount} sub-meshes, at most four are allowed");
                Expect(renderer.sharedMaterials.Length == mesh.subMeshCount, $"has {renderer.sharedMaterials.Length} material slots for {mesh.subMeshCount} sub-meshes");
                Expect(mesh == ForestPrefabs.LoadLodMesh(prefabId, level), "LoadLodMesh returns a different mesh than the prefab draws");
            }
            Expect(!renderer.HasPropertyBlock(), "carries a material property block");
            Expect(renderer.receiveGI == ReceiveGI.LightProbes, $"receiveGI is {renderer.receiveGI}, expected LightProbes");
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null)
                {
                    Expect(false, "has an empty material slot");
                    continue;
                }
                string path = AssetDatabase.GetAssetPath(material);
                Expect(!string.IsNullOrEmpty(path) && !path.StartsWith(ForestPaths.ThirdPartyRoot + "/", StringComparison.Ordinal),
                    $"material '{material.name}' is not a shared kit asset ({path})");
                Expect(!material.enableInstancing, $"material '{material.name}' has GPU instancing enabled");
            }
        }

        // ---- Tree levels ----

        [Test]
        public void TreeLods_WithinBudget()
        {
            List<string> trees = ForestTestSupport.RequireBuiltPrefabs(ForestTestSupport.IsTree, "tree prefab (milestone M3)");
            var problems = new List<string>();
            foreach (string prefabId in trees)
            {
                int levels = ForestPrefabs.LodCount(prefabId);
                int previous = int.MaxValue;
                Bounds first = default;
                for (int level = 0; level < levels; level++)
                {
                    Mesh mesh = ForestPrefabs.LoadLodMesh(prefabId, level);
                    if (mesh == null)
                    {
                        problems.Add($"{prefabId}: level {level} has no mesh");
                        continue;
                    }

                    int triangles = ForestTestSupport.TriangleCount(mesh);
                    int target = ForestKitSpec.TriangleTarget(prefabId, level);
                    if (target > 0 && Mathf.Abs(triangles - target) > 0.10f * target)
                        problems.Add($"{prefabId}: level {level} has {triangles} triangles, the target is {target} within 10 %");
                    if (triangles >= previous)
                        problems.Add($"{prefabId}: level {level} has {triangles} triangles, not fewer than the level before ({previous})");
                    previous = triangles;

                    // A level that shrinks or shifts the crown pops when it takes over.
                    if (level == 0)
                    {
                        first = mesh.bounds;
                        continue;
                    }
                    float largest = Mathf.Max(first.size.x, Mathf.Max(first.size.y, first.size.z));
                    for (int axis = 0; axis < 3; axis++)
                        if (Mathf.Abs(mesh.bounds.size[axis] - first.size[axis]) > 0.10f * first.size[axis])
                            problems.Add($"{prefabId}: level {level} measures {mesh.bounds.size[axis]:0.00} m on axis {axis}, level 0 measures {first.size[axis]:0.00} m");
                    if (Vector3.Distance(mesh.bounds.center, first.center) > 0.10f * largest)
                        problems.Add($"{prefabId}: the centre of level {level} is {Vector3.Distance(mesh.bounds.center, first.center):0.00} m from that of level 0");
                }
            }
            ForestTestSupport.AssertNone(problems, "tree level(s) are outside their budget");
        }

        [Test]
        public void TreeLods_KeepCoverage()
        {
            ForestKitReport report = ForestKitReport.Load();
            var trees = new List<ForestKitPrefabReport>();
            if (report != null)
                foreach (ForestKitPrefabReport prefab in report.prefabs)
                    if (ForestTestSupport.IsTree(prefab.prefabId) && ForestPrefabs.Exists(prefab.prefabId))
                        trees.Add(prefab);
            if (trees.Count == 0)
                Assert.Ignore("No tree has been baked yet (milestone M3), so the kit report holds no coverage to check.");

            var problems = new List<string>();
            foreach (ForestKitPrefabReport tree in trees)
            {
                // The table's levels: LOD0, LOD1, LOD2. A scan level in front of them is the reference itself.
                int tableLevel = 0;
                foreach (ForestKitLodReport lod in tree.lods)
                {
                    if (lod.isScan)
                        continue;
                    float minimum = tableLevel >= 2 ? 0.70f : 0.80f;
                    if (lod.coverage < minimum || lod.coverage > 1.30f)
                        problems.Add($"{tree.prefabId}: thinned level {tableLevel} covers {lod.coverage:0.00} of the source's silhouette, expected {minimum:0.00} to 1.30");
                    tableLevel++;
                }
                if (tableLevel == 0)
                    problems.Add($"{tree.prefabId}: the kit report lists no thinned level");
            }
            ForestTestSupport.AssertNone(problems, "tree level(s) lose or gain too much silhouette");
        }
    }
}
