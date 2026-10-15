using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Gamex.EditorTools
{
    // The manifest as JsonUtility reads it: field names are the JSON keys of forest-asset-manifest.json.
    // After loading, no string, array or maps object is null: an absent key reads as "" or as empty.

    /// <summary>One downloaded file: where it comes from, where it goes and how to recognise it.</summary>
    [Serializable] public sealed class ForestAssetFile { public string url, relPath, md5, map; public long bytes; }

    /// <summary>Destination paths of a material's maps, relative to Assets/ThirdParty. "" when the map does not exist.</summary>
    [Serializable] public sealed class ForestMaterialMaps { public string basecolor, diffuse, normal, arm, alpha; }

    [Serializable] public sealed class ForestMaterialEntry
    {
        public string name, surface, sameAs, noUvFallback;   // surface: "opaque" | "foliage"; sameAs: reuse another material;
        public float alphaCutoff; public ForestMaterialMaps maps;   // noUvFallback: material whose triplanar variant replaces this one without usable UVs
    }

    [Serializable] public sealed class ForestSubMeshEntry { public string material; public int triangles, vertices; public bool hasUv0; }

    [Serializable] public sealed class ForestObjectEntry
    {
        public string name; public float[] boundsMin, boundsMax; public int triangles; public ForestSubMeshEntry[] subMeshes;
    }

    [Serializable] public sealed class ForestAssetEntry
    {
        public string id, source, kind, role, resolution, milestone, license, preset;   // kind: "model" | "texture" | "hdri"
        public string objectsSource, boundsSpace, note;                                   // models: "fbx" | "gltf"; "source-rh-yup"
        public int terrainLayer; public float tileSizeMeters; public int maxTextureSize;  // terrainLayer is valid only when kind == "texture"
        public ForestAssetFile[] files; public ForestMaterialEntry[] materials; public ForestObjectEntry[] objects; public string[] fbxNodes;
    }

    /// <summary>
    /// Reads the asset manifest (spec 4.4) and answers the questions the build asks of it: which files
    /// belong to an asset, whether they are on disk, how large an object is and which material a
    /// sub-mesh takes. It never downloads or imports anything.
    /// </summary>
    public static class ForestManifest
    {
        const string ThirdPartyPrefix = ForestPaths.ThirdPartyRoot + "/";
        const string BoundsSpace = "source-rh-yup";
        const int MaxTerrainLayers = 8;
        const int MaxProblemsReported = 12;

        [Serializable] sealed class Wrapper { public ForestAssetEntry[] assets; }

        static ForestAssetEntry[] cache;
        static DateTime cacheWriteTime;
        static long cacheLength;
        static string projectRoot;

        static readonly Dictionary<string, ForestAssetEntry> byId = new();
        static readonly Dictionary<string, ForestObjectEntry> objectsByName = new();
        static readonly Dictionary<string, ForestAssetEntry> ownerByObjectName = new();
        static readonly Dictionary<string, ForestMaterialEntry> materialsByName = new();
        static readonly Dictionary<string, ForestAssetEntry> ownerByMaterialName = new();

        /// <summary>Every asset of the manifest. Cached; throws if the manifest is missing or malformed.</summary>
        public static ForestAssetEntry[] All
        {
            get
            {
                // fbx_objects.py rewrites the file while the Editor may be open. Without this check a model
                // that has just been measured would stay "unmeasured" until the next script reload.
                var file = new FileInfo(ManifestDiskPath);
                if (!file.Exists)
                    throw new FileNotFoundException($"The forest asset manifest is missing: {ForestPaths.ManifestPath}", file.FullName);
                if (cache == null || file.LastWriteTimeUtc != cacheWriteTime || file.Length != cacheLength)
                    Load(file);
                return cache;
            }
        }

        /// <summary>The asset with this id, or null.</summary>
        public static ForestAssetEntry Find(string id)
        {
            ForestAssetEntry[] unused = All;
            return !string.IsNullOrEmpty(id) && byId.TryGetValue(id, out ForestAssetEntry entry) ? entry : null;
        }

        /// <summary>The object with this node name in any model, or null.</summary>
        public static ForestObjectEntry FindObject(string objectName)
        {
            ForestAssetEntry[] unused = All;
            return !string.IsNullOrEmpty(objectName) && objectsByName.TryGetValue(objectName, out ForestObjectEntry entry) ? entry : null;
        }

        /// <summary>
        /// The manifest object a prefab is derived from: the prefab id itself, or the id plus "_LOD0"
        /// (trees and grass). Null for patch prefabs, which are merged from several objects.
        /// </summary>
        public static ForestObjectEntry FindObjectForPrefab(string prefabId)
        {
            if (string.IsNullOrEmpty(prefabId)) return null;
            return FindObject(prefabId) ?? FindObject(prefabId + "_LOD0");
        }

        /// <summary>The model an object belongs to, or null for an object that is not in the manifest.</summary>
        public static ForestAssetEntry FindOwner(ForestObjectEntry entry)
        {
            if (entry == null) return null;
            ForestAssetEntry[] unused = All;
            // By name, not by reference: the caller may hold an object from before the manifest was re-read.
            return !string.IsNullOrEmpty(entry.name) && ownerByObjectName.TryGetValue(entry.name, out ForestAssetEntry owner) ? owner : null;
        }

        /// <summary>The material entry with this FBX material name, as written (sameAs is not followed), or null.</summary>
        public static ForestMaterialEntry FindMaterial(string materialName)
        {
            ForestAssetEntry[] unused = All;
            return !string.IsNullOrEmpty(materialName) && materialsByName.TryGetValue(materialName, out ForestMaterialEntry entry) ? entry : null;
        }

        /// <summary>True when every file of the entry exists with the manifest's byte size. The md5 is checked by ForestImport.</summary>
        public static bool IsDownloaded(ForestAssetEntry entry)
        {
            if (entry == null || entry.files == null || entry.files.Length == 0) return false;
            foreach (ForestAssetFile file in entry.files)
            {
                var onDisk = new FileInfo(DiskPath(file.relPath));
                if (!onDisk.Exists || onDisk.Length != file.bytes) return false;
            }
            return true;
        }

        /// <summary>True when objects[] was measured from the FBX that is downloaded (spec 5.1). Prefabs are built only then.</summary>
        public static bool IsMeasured(ForestAssetEntry entry) => entry != null && entry.objectsSource == "fbx";

        /// <summary>Project-relative asset path of a manifest relPath.</summary>
        public static string AssetPath(string relPath) => ThirdPartyPrefix + relPath;

        /// <summary>Absolute path on disk of a manifest relPath.</summary>
        public static string DiskPath(string relPath) => ProjectRoot + "/" + ThirdPartyPrefix + relPath;

        /// <summary>Exactly the manifest numbers: the file's right-handed, Y-up space, metres, relative to the pivot.</summary>
        public static Bounds SourceBounds(ForestObjectEntry entry)
        {
            var bounds = new Bounds();
            bounds.SetMinMax(Corner(entry, entry.boundsMin), Corner(entry, entry.boundsMax));
            return bounds;
        }

        /// <summary>
        /// The source box mirrored on X, which is what the importer is assumed (not known) to do.
        /// Use only its size and Y range: those are the same whichever axis is mirrored.
        /// </summary>
        public static Bounds ExpectedBounds(ForestObjectEntry entry)
        {
            Vector3 min = Corner(entry, entry.boundsMin);
            Vector3 max = Corner(entry, entry.boundsMax);
            var bounds = new Bounds();
            bounds.SetMinMax(new Vector3(-max.x, min.y, min.z), new Vector3(-min.x, max.y, max.z));
            return bounds;
        }

        /// <summary>
        /// Radius of the horizontal circle around the pivot that contains the whole box, at scale 1.
        /// It uses absolute values only, so it is the same under a mirror of X, of Z or of both.
        /// </summary>
        public static float FootprintRadius(ForestObjectEntry entry)
        {
            Vector3 min = Corner(entry, entry.boundsMin);
            Vector3 max = Corner(entry, entry.boundsMax);
            float x = Mathf.Max(Mathf.Abs(min.x), Mathf.Abs(max.x));
            float z = Mathf.Max(Mathf.Abs(min.z), Mathf.Abs(max.z));
            return Mathf.Sqrt(x * x + z * z);
        }

        // ---- shared inside the package ----

        /// <summary>The folder that holds Assets/, with forward slashes and no trailing slash.</summary>
        internal static string ProjectRoot
        {
            get
            {
                if (projectRoot == null)
                    projectRoot = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/').TrimEnd('/');
                return projectRoot;
            }
        }

        /// <summary>Absolute path on disk of a project-relative path such as "Assets/..." or "Library/...".</summary>
        internal static string ProjectDiskPath(string projectRelativePath) => ProjectRoot + "/" + projectRelativePath;

        /// <summary>The material whose maps this name ends up using: sameAs followed once. Null for an unknown name.</summary>
        internal static ForestMaterialEntry ResolveMaterial(string materialName)
        {
            ForestMaterialEntry entry = FindMaterial(materialName);
            return entry != null && entry.sameAs != "" ? FindMaterial(entry.sameAs) : entry;
        }

        /// <summary>The model that declares this material, or null.</summary>
        internal static ForestAssetEntry FindMaterialOwner(string materialName)
        {
            ForestAssetEntry[] unused = All;
            return !string.IsNullOrEmpty(materialName) && ownerByMaterialName.TryGetValue(materialName, out ForestAssetEntry owner) ? owner : null;
        }

        /// <summary>The file of an entry with this destination path, or null.</summary>
        internal static ForestAssetFile FileAt(ForestAssetEntry entry, string relPath)
        {
            if (entry == null || string.IsNullOrEmpty(relPath)) return null;
            foreach (ForestAssetFile file in entry.files)
                if (file.relPath == relPath) return file;
            return null;
        }

        /// <summary>The file of a texture or sky entry with this bare map tag ("basecolor", "arm", "sky", ...), or null.</summary>
        internal static ForestAssetFile FileWithMap(ForestAssetEntry entry, string map)
        {
            if (entry == null) return null;
            foreach (ForestAssetFile file in entry.files)
                if (file.map == map) return file;
            return null;
        }

        /// <summary>How many files of the entry exist on disk, whatever their size.</summary>
        internal static int PresentFileCount(ForestAssetEntry entry)
        {
            int present = 0;
            foreach (ForestAssetFile file in entry.files)
                if (File.Exists(DiskPath(file.relPath))) present++;
            return present;
        }

        // ---- loading ----

        static string ManifestDiskPath => ProjectDiskPath(ForestPaths.ManifestPath);

        static void Load(FileInfo file)
        {
            // JsonUtility cannot parse a top-level array, so the text is wrapped in an object.
            string text = File.ReadAllText(file.FullName);
            Wrapper wrapper;
            try
            {
                wrapper = JsonUtility.FromJson<Wrapper>("{\"assets\":" + text + "}");
            }
            catch (ArgumentException error)
            {
                throw new InvalidDataException($"{ForestPaths.ManifestPath} is not valid JSON: {error.Message}", error);
            }
            if (wrapper == null || wrapper.assets == null || wrapper.assets.Length == 0)
                throw new InvalidDataException($"{ForestPaths.ManifestPath} holds no assets. It must be a JSON array of asset entries.");

            ForestAssetEntry[] entries = wrapper.assets;
            foreach (ForestAssetEntry entry in entries)
                Normalise(entry);

            List<string> problems = Validate(entries);
            if (problems.Count > 0)
            {
                int shown = Mathf.Min(problems.Count, MaxProblemsReported);
                string more = problems.Count > shown ? $"\n  ... and {problems.Count - shown} more" : "";
                throw new InvalidDataException(
                    $"{ForestPaths.ManifestPath} is malformed ({problems.Count} problems):\n  " +
                    string.Join("\n  ", problems.GetRange(0, shown)) + more);
            }

            byId.Clear();
            objectsByName.Clear();
            ownerByObjectName.Clear();
            materialsByName.Clear();
            ownerByMaterialName.Clear();
            foreach (ForestAssetEntry entry in entries)
            {
                byId[entry.id] = entry;
                foreach (ForestObjectEntry item in entry.objects)
                {
                    objectsByName[item.name] = item;
                    ownerByObjectName[item.name] = entry;
                }
                foreach (ForestMaterialEntry material in entry.materials)
                {
                    materialsByName[material.name] = material;
                    ownerByMaterialName[material.name] = entry;
                }
            }

            cache = entries;
            cacheWriteTime = file.LastWriteTimeUtc;
            cacheLength = file.Length;
        }

        // What JsonUtility leaves in a field whose key is absent is not documented. After this, readers
        // never meet a null: a missing string is "", a missing array is empty.
        static void Normalise(ForestAssetEntry entry)
        {
            entry.id ??= ""; entry.source ??= ""; entry.kind ??= ""; entry.role ??= ""; entry.resolution ??= "";
            entry.milestone ??= ""; entry.license ??= ""; entry.preset ??= "";
            entry.objectsSource ??= ""; entry.boundsSpace ??= ""; entry.note ??= "";
            entry.files ??= Array.Empty<ForestAssetFile>();
            entry.materials ??= Array.Empty<ForestMaterialEntry>();
            entry.objects ??= Array.Empty<ForestObjectEntry>();
            entry.fbxNodes ??= Array.Empty<string>();

            for (int i = 0; i < entry.files.Length; i++)
            {
                ForestAssetFile file = entry.files[i] ??= new ForestAssetFile();
                file.url ??= ""; file.relPath ??= ""; file.md5 ??= ""; file.map ??= "";
            }
            for (int i = 0; i < entry.materials.Length; i++)
            {
                ForestMaterialEntry material = entry.materials[i] ??= new ForestMaterialEntry();
                material.name ??= ""; material.surface ??= ""; material.sameAs ??= ""; material.noUvFallback ??= "";
                ForestMaterialMaps maps = material.maps ??= new ForestMaterialMaps();
                maps.basecolor ??= ""; maps.diffuse ??= ""; maps.normal ??= ""; maps.arm ??= ""; maps.alpha ??= "";
            }
            for (int i = 0; i < entry.objects.Length; i++)
            {
                ForestObjectEntry item = entry.objects[i] ??= new ForestObjectEntry();
                item.name ??= "";
                item.boundsMin ??= Array.Empty<float>();
                item.boundsMax ??= Array.Empty<float>();
                item.subMeshes ??= Array.Empty<ForestSubMeshEntry>();
                for (int s = 0; s < item.subMeshes.Length; s++)
                {
                    ForestSubMeshEntry sub = item.subMeshes[s] ??= new ForestSubMeshEntry();
                    sub.material ??= "";
                }
            }
            for (int i = 0; i < entry.fbxNodes.Length; i++)
                entry.fbxNodes[i] ??= "";
        }

        // Only what the lookups and the build rely on. A manifest that passes cannot make them return
        // nonsense or throw a null reference half-way through an import.
        static List<string> Validate(ForestAssetEntry[] entries)
        {
            var problems = new List<string>();
            var ids = new HashSet<string>();
            var relPaths = new HashSet<string>();
            var objectNames = new HashSet<string>();
            var materialNames = new HashSet<string>();
            var layers = new HashSet<int>();

            foreach (ForestAssetEntry entry in entries)
            {
                string who = entry.id == "" ? "an entry without id" : entry.id;
                if (entry.id == "") problems.Add("an entry has no id");
                else if (!ids.Add(entry.id)) problems.Add($"{who}: the id is used twice");

                if (entry.files.Length == 0) problems.Add($"{who}: no files");
                foreach (ForestAssetFile file in entry.files)
                {
                    if (!file.relPath.StartsWith("PolyHaven/", StringComparison.Ordinal) || file.relPath.Contains(".."))
                        problems.Add($"{who}: relPath '{file.relPath}' is not under PolyHaven/");
                    else if (!relPaths.Add(file.relPath))
                        problems.Add($"{who}: relPath '{file.relPath}' is used twice");
                    if (file.url == "" || file.map == "" || file.md5.Length != 32 || file.bytes <= 0)
                        problems.Add($"{who}: file '{file.relPath}' lacks a url, map tag, md5 or byte size");
                }

                switch (entry.kind)
                {
                    case "model":
                        ValidateModel(entry, who, objectNames, materialNames, problems);
                        break;
                    case "texture":
                        if (entry.terrainLayer < 0 || entry.terrainLayer >= MaxTerrainLayers || !layers.Add(entry.terrainLayer))
                            problems.Add($"{who}: terrainLayer {entry.terrainLayer} is outside 0..{MaxTerrainLayers - 1} or used twice");
                        if (entry.tileSizeMeters <= 0f) problems.Add($"{who}: tileSizeMeters must be positive");
                        if (entry.maxTextureSize <= 0) problems.Add($"{who}: maxTextureSize must be positive");
                        foreach (string map in new[] { "basecolor", "normal", "arm", "height" })
                            if (CountMap(entry, map) != 1) problems.Add($"{who}: needs exactly one file tagged '{map}'");
                        break;
                    case "hdri":
                        if (entry.preset == "") problems.Add($"{who}: a sky needs a preset");
                        foreach (string map in new[] { "sky", "skyCalibration" })
                            if (CountMap(entry, map) != 1) problems.Add($"{who}: needs exactly one file tagged '{map}'");
                        break;
                    default:
                        problems.Add($"{who}: kind '{entry.kind}' is not model, texture or hdri");
                        break;
                }
            }
            return problems;
        }

        static void ValidateModel(ForestAssetEntry entry, string who, HashSet<string> objectNames,
            HashSet<string> materialNames, List<string> problems)
        {
            if (CountMap(entry, "mesh") != 1) problems.Add($"{who}: needs exactly one file tagged 'mesh'");
            if (entry.maxTextureSize <= 0) problems.Add($"{who}: maxTextureSize must be positive");
            if (entry.boundsSpace != BoundsSpace) problems.Add($"{who}: boundsSpace must be '{BoundsSpace}', found '{entry.boundsSpace}'");
            if (entry.objectsSource != "fbx" && entry.objectsSource != "gltf")
                problems.Add($"{who}: objectsSource must be 'fbx' or 'gltf', found '{entry.objectsSource}'");
            if (entry.materials.Length == 0) problems.Add($"{who}: no materials");
            if (entry.objects.Length == 0) problems.Add($"{who}: no objects");

            var own = new Dictionary<string, ForestMaterialEntry>();
            foreach (ForestMaterialEntry material in entry.materials)
            {
                if (material.name == "" || !materialNames.Add(material.name))
                    problems.Add($"{who}: material name '{material.name}' is empty or used twice in the manifest");
                own[material.name] = material;
            }
            foreach (ForestMaterialEntry material in entry.materials)
            {
                string what = $"{who}: material {material.name}";
                if (material.sameAs != "")
                {
                    // One step only: ResolveMaterial follows sameAs once.
                    if (!own.TryGetValue(material.sameAs, out ForestMaterialEntry target) || target.sameAs != "" || target == material)
                        problems.Add($"{what}: sameAs '{material.sameAs}' is not a material of this model with its own maps");
                }
                else
                {
                    bool foliage = material.surface == "foliage";
                    if (!foliage && material.surface != "opaque")
                        problems.Add($"{what}: surface must be 'opaque' or 'foliage', found '{material.surface}'");
                    string[] needed = foliage
                        ? new[] { material.maps.diffuse, material.maps.normal, material.maps.arm, material.maps.alpha }
                        : new[] { material.maps.basecolor, material.maps.normal, material.maps.arm };
                    foreach (string relPath in needed)
                        if (FileAt(entry, relPath) == null)
                            problems.Add($"{what}: map '{relPath}' is missing or is not one of the model's files");
                    if (foliage && (material.alphaCutoff <= 0f || material.alphaCutoff >= 1f))
                        problems.Add($"{what}: alphaCutoff {material.alphaCutoff} is outside 0..1");
                }
                if (material.noUvFallback != "" &&
                    (!own.ContainsKey(material.noUvFallback) || material.noUvFallback == material.name))
                    problems.Add($"{what}: noUvFallback '{material.noUvFallback}' is not another material of this model");
            }

            foreach (ForestObjectEntry item in entry.objects)
            {
                string what = $"{who}: object {item.name}";
                if (item.name == "" || !objectNames.Add(item.name))
                    problems.Add($"{who}: object name '{item.name}' is empty or used twice in the manifest");
                if (item.boundsMin.Length != 3 || item.boundsMax.Length != 3)
                {
                    problems.Add($"{what}: boundsMin and boundsMax need three numbers each");
                }
                else
                {
                    for (int axis = 0; axis < 3; axis++)
                        if (!(item.boundsMin[axis] <= item.boundsMax[axis]))
                            problems.Add($"{what}: boundsMin exceeds boundsMax on axis {axis}");
                }
                if (item.triangles <= 0 || item.subMeshes.Length == 0)
                    problems.Add($"{what}: needs a triangle count and at least one sub-mesh");
                int sum = 0;
                foreach (ForestSubMeshEntry sub in item.subMeshes)
                {
                    sum += sub.triangles;
                    if (!own.ContainsKey(sub.material))
                        problems.Add($"{what}: sub-mesh material '{sub.material}' is not a material of this model");
                }
                if (item.subMeshes.Length > 0 && sum != item.triangles)
                    problems.Add($"{what}: sub-meshes hold {sum} triangles, the object {item.triangles}");
            }
        }

        static int CountMap(ForestAssetEntry entry, string map)
        {
            int count = 0;
            foreach (ForestAssetFile file in entry.files)
                if (file.map == map) count++;
            return count;
        }

        static Vector3 Corner(ForestObjectEntry entry, float[] values)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (values == null || values.Length != 3)
                throw new InvalidDataException($"Object '{entry.name}' has no bounds in the forest asset manifest.");
            return new Vector3(values[0], values[1], values[2]);
        }
    }
}
