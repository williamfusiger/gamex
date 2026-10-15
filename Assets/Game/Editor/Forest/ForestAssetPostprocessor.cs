using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Gamex.EditorTools
{
    /// <summary>
    /// Import settings for the forest kit (spec 5.2 and 5.3): the downloaded models and textures under
    /// Assets/ThirdParty/PolyHaven and the packed textures under ForestKit/Textures. Nothing else in
    /// the project is touched.
    ///
    /// It only sets importer properties. It creates no asset and reads no file, because imports may
    /// run in parallel worker processes; what it needs to know per asset is in the small tables below.
    /// ForestImport.Run compares those tables with the manifest and refuses to continue if they differ.
    /// </summary>
    public sealed class ForestAssetPostprocessor : AssetPostprocessor
    {
        // Raise this whenever a rule below changes, so Unity re-imports what the rule applies to.
        // 2: the sky imports as BC6H (probe P8, M0).
        const uint Version = 2;

        const string PolyHavenPrefix = ForestPaths.PolyHavenRoot + "/";
        const string KitTexturesPrefix = ForestPaths.KitTextures + "/";

        const int DefaultMaxSize = 2048;
        const int LargeMaxSize = 4096;
        const int SkyMaxSize = 4096;
        const int TerrainAniso = 8;
        const int DefaultAniso = 4;

        // BC6H. Probe P8 (M0, Apple M4 Max, Metal) found it clean: a sky-only render differs from the uncompressed
        // import by an RMSE of 0.0012. The faces of a 4k panorama are 2048, not 1024, so uncompressed half floats
        // would cost about 268 MiB per sky; BC6H costs about 34 MiB. To go back: Uncompressed here, raise Version,
        // and change the matching line of Textures_FollowPolicy.
        const TextureImporterCompression SkyCompression = TextureImporterCompression.CompressedHQ;

        // ---- the per-asset table. Keep it equal to forest-asset-manifest.json ----

        /// <summary>Assets whose base colour and normal map import at 4096. Everything else, and every mask, is 2048.</summary>
        static readonly HashSet<string> LargeTextureIds = new()
        {
            "rock_moss_set_01", "rock_moss_set_02", "rock_face_01", "rock_face_02",
        };

        /// <summary>Terrain layer ids: no mip streaming (terrain samples them everywhere) and anisotropy 8.</summary>
        static readonly HashSet<string> TerrainLayerIds = new()
        {
            "forest_leaves_04", "forest_ground_06", "forest_leaves_02", "rocky_trail",
            "forest_ground_04", "mud_forest", "mossy_rock", "forrest_ground_03",
        };

        /// <summary>Alpha cutoff per foliage material: the reference value that keeps coverage in the mips.</summary>
        static readonly Dictionary<string, float> FoliageCutoffs = new()
        {
            { "fern_02", 0.5f }, { "grass_medium_01", 0.5f },
            { "fir_sapling_medium_twigs", 0.4f }, { "fir_tree_01_twig", 0.4f },
        };

        /// <summary>
        /// The firs: millions of vertices that are only ever read by the tree baker. Its derived meshes
        /// recalculate tangents and call Optimize(), so the import does neither.
        /// </summary>
        static readonly HashSet<string> UnoptimizedModelIds = new() { "fir_sapling_medium", "fir_tree_01" };

        public override uint GetVersion() => Version;

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(PolyHavenPrefix, StringComparison.Ordinal)) return;
            var importer = (ModelImporter)assetImporter;
            string id = FolderId(assetPath);

            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.bakeAxisConversion = false;
            // Always an explicit root, so a file with one mesh looks like a file with several.
            importer.preserveHierarchy = true;

            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.importBlendShapes = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;

            // The imported materials are never used, but this mode keeps the FBX material names on the
            // slots, and the kit maps sub-meshes to its own materials by those names.
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;

            importer.importNormals = ModelImporterNormals.Import;
            importer.importVertexColors = false;
            // Editor scripts can read mesh data regardless; the scene never references these meshes.
            importer.isReadable = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.weldVertices = true;
            importer.addCollider = false;
            importer.generateSecondaryUV = false;
            // LODs are built by the kit's own code.
            importer.generateMeshLods = false;

            bool unoptimized = UnoptimizedModelIds.Contains(id);
            importer.importTangents = unoptimized ? ModelImporterTangents.None : ModelImporterTangents.CalculateMikk;
            importer.meshOptimizationFlags = unoptimized ? (MeshOptimizationFlags)0 : MeshOptimizationFlags.Everything;
        }

        void OnPreprocessTexture()
        {
            bool thirdParty = assetPath.StartsWith(PolyHavenPrefix, StringComparison.Ordinal);
            bool packed = assetPath.StartsWith(KitTexturesPrefix, StringComparison.Ordinal);
            if (!thirdParty && !packed) return;

            // File names are "<material or id>__<map>.<ext>", fixed at download and packing time.
            string fileName = assetPath.Substring(assetPath.LastIndexOf('/') + 1);
            int split = fileName.LastIndexOf("__", StringComparison.Ordinal);
            if (split <= 0) return;
            string stem = fileName.Substring(0, split);
            string suffix = fileName.Substring(split);
            string id = thirdParty ? FolderId(assetPath) : stem;

            var importer = (TextureImporter)assetImporter;
            switch (suffix)
            {
                case "__sky.hdr":
                    if (thirdParty) ApplySky(importer);
                    return;

                case "__basecolor.jpg":
                    importer.textureType = TextureImporterType.Default;
                    importer.sRGBTexture = true;
                    importer.alphaSource = TextureImporterAlphaSource.None;
                    importer.alphaIsTransparency = false;
                    ApplyCommon(importer, TerrainLayerIds.Contains(id), LargeTextureIds.Contains(id) ? LargeMaxSize : DefaultMaxSize);
                    return;

                case "__basecolor.png":   // foliage: colour with the leaf mask in alpha, written by the packer
                    importer.textureType = TextureImporterType.Default;
                    importer.sRGBTexture = true;
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    // Bleeds leaf colour into the transparent texels, so edges do not filter toward black.
                    importer.alphaIsTransparency = true;
                    // Without this, distant foliage thins out as the mips average the mask away.
                    importer.mipMapsPreserveCoverage = true;
                    importer.alphaTestReferenceValue = FoliageCutoffs.TryGetValue(stem, out float cutoff) ? cutoff : 0.5f;
                    ApplyCommon(importer, false, DefaultMaxSize);
                    return;

                case "__normal.png":
                case "__normal.jpg":
                    importer.textureType = TextureImporterType.NormalMap;
                    // Poly Haven "nor_gl" is Y+, which is what Unity expects.
                    importer.flipGreenChannel = false;
                    ApplyCommon(importer, TerrainLayerIds.Contains(id), LargeTextureIds.Contains(id) ? LargeMaxSize : DefaultMaxSize);
                    return;

                case "__mask.png":
                case "__tmask.png":
                    if (!packed) return;
                    importer.textureType = TextureImporterType.Default;
                    importer.sRGBTexture = false;   // data, not colour
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;   // alpha is smoothness
                    importer.alphaIsTransparency = false;
                    ApplyCommon(importer, suffix == "__tmask.png", DefaultMaxSize);
                    return;
            }
        }

        static void ApplyCommon(TextureImporter importer, bool terrainLayer, int maxSize)
        {
            importer.textureShape = TextureImporterShape.Texture2D;
            // Some Poly Haven PNGs carry a gAMA chunk or an ICC profile that would shift normal and mask data.
            importer.ignorePngGamma = true;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = !terrainLayer;
            importer.anisoLevel = terrainLayer ? TerrainAniso : DefaultAniso;
            importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            ClearPlatformOverride(importer);
        }

        static void ApplySky(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.TextureCube;
            importer.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = SkyMaxSize;
            importer.textureCompression = SkyCompression;
            ClearPlatformOverride(importer);
        }

        // Default-platform settings only, so a later console target picks its own formats.
        static void ClearPlatformOverride(TextureImporter importer)
        {
            if (importer.GetPlatformTextureSettings("Standalone").overridden)
                importer.ClearPlatformTextureSettings("Standalone");
        }

        /// <summary>The Poly Haven id of a path under Assets/ThirdParty/PolyHaven/&lt;id&gt;/.</summary>
        static string FolderId(string path)
        {
            int end = path.IndexOf('/', PolyHavenPrefix.Length);
            return end < 0 ? "" : path.Substring(PolyHavenPrefix.Length, end - PolyHavenPrefix.Length);
        }

        /// <summary>
        /// Differences between the table above and the manifest, as readable lines; empty when they agree.
        /// The table exists so that an import reads no file. This is the check that keeps it honest.
        /// </summary>
        internal static List<string> TableProblems(ForestAssetEntry[] manifest)
        {
            var problems = new List<string>();
            var terrainSeen = new HashSet<string>();
            var foliageSeen = new HashSet<string>();
            var largeSeen = new HashSet<string>();

            foreach (ForestAssetEntry entry in manifest)
            {
                if (entry.kind == "texture")
                {
                    terrainSeen.Add(entry.id);
                    if (!TerrainLayerIds.Contains(entry.id))
                        problems.Add($"{entry.id} is a terrain layer in the manifest but not in the postprocessor table");
                }
                if (entry.kind == "texture" || entry.kind == "model")
                {
                    bool large = LargeTextureIds.Contains(entry.id);
                    if (large) largeSeen.Add(entry.id);
                    int expected = large ? LargeMaxSize : DefaultMaxSize;
                    if (entry.maxTextureSize != expected)
                        problems.Add($"{entry.id}: maxTextureSize is {entry.maxTextureSize} in the manifest, {expected} in the postprocessor table");
                }
                foreach (ForestMaterialEntry material in entry.materials)
                {
                    if (material.surface != "foliage") continue;
                    foliageSeen.Add(material.name);
                    if (!FoliageCutoffs.TryGetValue(material.name, out float cutoff))
                        problems.Add($"foliage material {material.name} has no alpha cutoff in the postprocessor table");
                    else if (Mathf.Abs(cutoff - material.alphaCutoff) > 0.0001f)
                        problems.Add($"{material.name}: alphaCutoff is {material.alphaCutoff} in the manifest, {cutoff} in the postprocessor table");
                }
            }

            foreach (string id in TerrainLayerIds)
                if (!terrainSeen.Contains(id))
                    problems.Add($"{id} is a terrain layer in the postprocessor table but not in the manifest");
            foreach (string id in LargeTextureIds)
                if (!largeSeen.Contains(id))
                    problems.Add($"{id} imports at {LargeMaxSize} in the postprocessor table but is not a model or texture of the manifest");
            foreach (string name in FoliageCutoffs.Keys)
                if (!foliageSeen.Contains(name))
                    problems.Add($"{name} has an alpha cutoff in the postprocessor table but is not a foliage material of the manifest");
            foreach (string id in UnoptimizedModelIds)
                if (ForestManifest.Find(id) == null)
                    problems.Add($"{id} is a fir model in the postprocessor table but not in the manifest");

            problems.Sort(StringComparer.Ordinal);
            return problems;
        }
    }
}
