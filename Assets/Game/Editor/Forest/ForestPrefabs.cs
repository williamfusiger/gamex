using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gamex.EditorTools
{
    /// <summary>
    /// Builds the kit: one prefab per id of ForestPrefabIds, each with derived meshes in metres, a
    /// LODGroup and its collider (spec section 5.6). Imported models are only read here; a prefab never
    /// references one. Rocks, stump patches, logs, branches, ferns and grass are built in this file,
    /// trees by ForestTreeBaker with the helpers below.
    /// </summary>
    public static class ForestPrefabs
    {
        internal enum Outcome { Built, UpToDate, Skipped }

        /// <summary>One object of an imported model, already in prefab space: metres, Y up, pivot at the origin.</summary>
        internal sealed class Source
        {
            public ForestMeshTools.MeshData data;
            public string[] materialNames;   // the manifest material of each sub-mesh
            public Bounds bounds;
            public string mirror;            // which horizontal axes the importer mirrored, as MatchesSourceBounds reports it
            public bool[] offAtlas;          // per sub-mesh: its triangles left their scan atlas (SplitOffAtlas); null = none did
            public bool[] skirt;             // per sub-mesh: the ground skirt of a root flare (SplitSkirt); null = none
        }

        internal sealed class Level
        {
            public Mesh mesh;
            public Material[] materials;
            public bool castShadows;
            public float untilDistance;
        }

        internal sealed class Parts
        {
            public string prefabId;
            public Level[] levels;
            public Bounds bounds;                // of the first level: gives the LODGroup its size and reference point
            public Mesh shadowMesh;              // a tree's scan level casts its shadow with this mesh instead of its own
            public Material[] shadowMaterials;
            public Mesh collisionMesh;           // rocks, logs, stump patches
            public float capsuleRadius;          // trees; 0 = no capsule
            public bool contributeGI;
        }

        struct ClumpPlacement
        {
            public Vector3 position;
            public float yaw, scale;
        }

        [MenuItem("Gamex/Forest/2 Build Kit")]
        public static void BuildAllFromMenu() => BuildAll(ForestArgs.Flag("-forestForce"));

        public static void BuildAll(bool force)                // every prefab whose model is downloaded, measured and has materials; others are skipped with a log line
        {
            EnsureFolders();

            int built = 0, upToDate = 0, skipped = 0;
            var failures = new List<string>();
            foreach (string prefabId in BuildOrder())
            {
                // One bad asset should not hide what the others would have said: a Unity launch is
                // expensive. Every failure is logged in full and the stage still fails at the end.
                try
                {
                    switch (BuildOne(prefabId, force))
                    {
                        case Outcome.Built: built++; break;
                        case Outcome.UpToDate: upToDate++; break;
                        default: skipped++; break;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    failures.Add($"{prefabId}: {exception.Message}");
                }
            }

            maskCache.Clear();   // 16 MB per material
            AssetDatabase.SaveAssets();
            Debug.Log($"Forest kit: {built} prefabs built, {upToDate} up to date, {skipped} skipped for missing inputs, {failures.Count} failed.");
            if (failures.Count > 0)
                throw new InvalidOperationException("Forest kit: " + failures.Count + " prefab(s) failed.\n" + string.Join("\n", failures));
        }

        public static bool Exists(string prefabId) => Load(prefabId) != null;

        public static string PrefabPath(string prefabId) => ForestPaths.KitPrefabs + "/" + prefabId + ".prefab";

        public static GameObject Load(string prefabId)         // null when not built
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(prefabId));
        }

        public static int LodCount(string prefabId)            // of the built prefab; 0 when not built
        {
            GameObject prefab = Load(prefabId);
            if (prefab == null) return 0;
            var group = prefab.GetComponent<LODGroup>();
            return group != null ? group.lodCount : 0;
        }

        public static Mesh LoadLodMesh(string prefabId, int lod)
        {
            GameObject prefab = Load(prefabId);
            if (prefab == null) return null;
            Transform level = prefab.transform.Find("LOD" + lod);
            if (level == null) return null;
            var filter = level.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        /// <summary>
        /// Gives an instance the tint variant of its foliage material (0 = the plain one). The variant is
        /// a shared material asset, not a property block, so instances keep batching together.
        /// </summary>
        public static void ApplyTint(GameObject instance, int tint)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));

            foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    // Kit materials are named after their manifest material, variants with a "__" suffix.
                    string name = materials[i].name;
                    int suffix = name.IndexOf("__", StringComparison.Ordinal);
                    bool plain = suffix < 0;
                    if (!plain)
                    {
                        if (!name.Substring(suffix).StartsWith("__tint", StringComparison.Ordinal)) continue;
                        name = name.Substring(0, suffix);
                    }
                    if (ForestManifest.FindMaterial(name) == null || !ForestMaterials.IsFoliage(name)) continue;

                    // Not every foliage material has a variant (grass has none); it then keeps what it has.
                    Material wanted = ForestMaterials.Get(name, tint);
                    if (wanted == null || wanted == materials[i]) continue;
                    materials[i] = wanted;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
        }

        // ---- Shared with ForestTreeBaker ----

        internal static string MeshPath(string prefabId, string suffix) => ForestPaths.KitMeshes + "/" + prefabId + "__" + suffix + ".asset";

        internal static void EnsureFolders()
        {
            EnsureFolder(ForestPaths.KitMeshes);
            EnsureFolder(ForestPaths.KitPrefabs);
        }

        /// <summary>The model file of a manifest entry, as an asset path.</summary>
        internal static string ModelPath(ForestAssetEntry owner)
        {
            foreach (ForestAssetFile file in owner.files)
                if (file.map == "mesh") return ForestManifest.AssetPath(file.relPath);
            throw new InvalidOperationException($"Manifest entry {owner.id} lists no mesh file.");
        }

        internal static GameObject LoadModel(ForestAssetEntry owner)
        {
            // Numbers copied from the glTF have been wrong before (the rock faces were 1.4 and 1.8 times
            // larger in the FBX), so nothing is built from a model nobody measured.
            if (!ForestManifest.IsMeasured(owner))
                throw new InvalidOperationException(
                    $"Model {owner.id} has not been measured against its FBX (objectsSource is '{owner.objectsSource}'). Run: {MeasureCommand(owner)}");

            string path = ModelPath(owner);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
                throw new InvalidOperationException($"{path} is not imported. Run Gamex/Forest/1 Import Assets first.");
            return model;
        }

        /// <summary>
        /// Reads one object of a model and moves it into prefab space with whatever transform the importer
        /// gave it. Nothing about that transform is assumed; the result is checked against the manifest instead.
        /// </summary>
        internal static Source DeriveSource(GameObject model, ForestAssetEntry owner, ForestObjectEntry entry, string nodeName, bool assertBounds)
        {
            MeshFilter node = FindNode(model, nodeName, owner.id);

            Matrix4x4 matrix = model.transform.worldToLocalMatrix * node.transform.localToWorldMatrix;
            // A pack lays its variants out side by side; every prefab wants its own pivot at the origin.
            matrix.m03 = 0f;
            matrix.m13 = 0f;
            matrix.m23 = 0f;

            ForestMeshTools.MeshData data = ForestMeshTools.Read(node.sharedMesh);
            ForestMeshTools.Transform(data, matrix);

            var source = new Source { data = data, bounds = ForestMeshTools.BoundsOf(data), mirror = "symmetric" };
            if (assertBounds && !ForestMeshTools.MatchesSourceBounds(source.bounds, ForestManifest.SourceBounds(entry), out source.mirror))
                throw new InvalidOperationException(
                    $"'{nodeName}' of {owner.id} does not match the manifest after import: {source.mirror}. "
                    + $"Measured {source.bounds.min} to {source.bounds.max}.");

            source.materialNames = MaterialNames(node, data, entry, owner);
            return source;
        }

        /// <summary>
        /// One tree object in prefab space, with UVs. Poly Haven's FBX of fir_sapling_medium carries no UV layer
        /// at all (its meshes are Blender curves converted on export: positions, normals and material indices
        /// only), so its twig atlas and its bark cannot be mapped. When a sub-mesh whose material has no
        /// noUvFallback lacks usable UVs, the same object is read from the model's glTF geometry (the .gltf and
        /// .bin listed in the manifest with the tags "gltf" and "gltfBuffer", kept in Source~; the glTF's own
        /// textures are never used), checked against the imported FBX triangle for triangle count, area and
        /// area-weighted centre per material, and used instead. Throws when UVs are missing and no glTF is listed.
        /// </summary>
        internal static Source DeriveTreeSource(ForestAssetEntry owner, ForestObjectEntry entry)
        {
            Source fbx = DeriveSource(LoadModel(owner), owner, entry, entry.name, true);

            var withoutUv = new List<string>();
            for (int i = 0; i < fbx.materialNames.Length; i++)
            {
                if (fbx.data.subMeshes[i].Length == 0 || ForestMeshTools.HasUsableUv(fbx.data, i)) continue;
                ForestMaterialEntry material = ForestManifest.FindMaterial(fbx.materialNames[i]);
                // A material with a noUvFallback is projected triplanar instead (spec 5.5); everything else needs UVs.
                if (material != null && !string.IsNullOrEmpty(material.noUvFallback)) continue;
                if (!withoutUv.Contains(fbx.materialNames[i])) withoutUv.Add(fbx.materialNames[i]);
            }
            if (withoutUv.Count == 0) return SplitSkirt(SplitOffAtlas(fbx, entry.name), entry.name);

            ForestAssetFile gltf = ForestManifest.FileWithMap(owner, "gltf");
            ForestAssetFile buffer = ForestManifest.FileWithMap(owner, "gltfBuffer");
            if (gltf == null || buffer == null)
                throw new InvalidOperationException(
                    $"'{entry.name}' of {owner.id} has no usable UVs for {string.Join(", ", withoutUv)} in its FBX, and the manifest lists no glTF "
                    + "geometry (files tagged 'gltf' and 'gltfBuffer') to take them from. Its textures cannot be mapped.");

            Source source = ForestGltfGeometry.Read(ForestManifest.DiskPath(gltf.relPath), ForestManifest.DiskPath(buffer.relPath), entry.name, owner, fbx);
            if (!ForestMeshTools.MatchesSourceBounds(source.bounds, ForestManifest.SourceBounds(entry), out source.mirror))
                throw new InvalidOperationException($"'{entry.name}' read from the glTF of {owner.id} does not match the manifest: {source.mirror}.");
            Debug.Log($"Forest kit: the FBX of {owner.id} has no usable UVs for {string.Join(", ", withoutUv)}; '{entry.name}' is taken from its glTF geometry "
                + $"({ForestMeshTools.TriangleCount(source.data)} triangles, {source.data.positions.Length} vertices, checked against the imported FBX).");
            return SplitSkirt(SplitOffAtlas(source, entry.name), entry.name);
        }

        /// <summary>
        /// Moves the triangles of a material listed in ForestKitSpec.SkirtMaterials that lie low and face up (centroid under
        /// SkirtMaxHeight above the pivot, mean vertex normal at least SkirtMinNormalY up) into a sub-mesh of their own, flagged in
        /// Source.skirt, which ResolveMaterials draws with the ground variant of ForestKitSpec.SkirtGroundLayer. The root flare's
        /// standing bark (normals near horizontal) stays where it was. Review 1 of the M4 captures: fir_tree_01_c's litter skirt.
        /// </summary>
        internal static Source SplitSkirt(Source source, string nodeName)
        {
            ForestMeshTools.MeshData data = source.data;
            if (data.normals == null) return source;
            var subMeshes = new List<int[]>(data.subMeshes);
            var names = new List<string>(source.materialNames);
            int count = data.subMeshes.Length;
            var offAtlas = new List<bool>(source.offAtlas ?? new bool[count]);
            var skirt = new List<bool>(source.skirt ?? new bool[count]);
            Vector3[] p = data.positions, n = data.normals;
            for (int s = 0; s < count; s++)
            {
                if (Array.IndexOf(ForestKitSpec.SkirtMaterials, source.materialNames[s]) < 0) continue;
                int[] indices = data.subMeshes[s];
                var keep = new List<int>(indices.Length);
                var moved = new List<int>();
                double movedArea = 0, totalArea = 0;
                for (int t = 0; t + 2 < indices.Length; t += 3)
                {
                    int a = indices[t], b = indices[t + 1], c = indices[t + 2];
                    float height = (p[a].y + p[b].y + p[c].y) / 3f;
                    float up = (n[a].y + n[b].y + n[c].y) / 3f;
                    double area = 0.5 * Vector3.Cross(p[b] - p[a], p[c] - p[a]).magnitude;
                    totalArea += area;
                    bool low = height < ForestKitSpec.SkirtMaxHeight && up >= ForestKitSpec.SkirtMinNormalY;
                    List<int> target = low ? moved : keep;
                    target.Add(a); target.Add(b); target.Add(c);
                    if (low) movedArea += area;
                }
                if (moved.Count == 0) continue;
                subMeshes[s] = keep.ToArray();
                subMeshes.Add(moved.ToArray());
                names.Add(source.materialNames[s]);
                offAtlas.Add(false);
                skirt.Add(true);
                Debug.Log($"Forest kit: {moved.Count / 3} triangles ({movedArea:0.00} m2, {movedArea / Math.Max(totalArea, 1e-9) * 100.0:0.0} % of its surface) of "
                    + $"{source.materialNames[s]} on '{nodeName}' are its root skirt (under {ForestKitSpec.SkirtMaxHeight} m, facing up); "
                    + $"they are drawn with the ground variant of {ForestKitSpec.SkirtGroundLayer}.");
            }
            if (names.Count == count) return source;
            var split = new ForestMeshTools.MeshData
            {
                positions = data.positions, normals = data.normals, tangents = data.tangents, uv0 = data.uv0, subMeshes = subMeshes.ToArray(),
            };
            return new Source
            {
                data = split, materialNames = names.ToArray(), bounds = source.bounds, mirror = source.mirror,
                offAtlas = offAtlas.ToArray(), skirt = skirt.ToArray(),
            };
        }

        /// <summary>
        /// Moves the triangles of a material listed in ForestKitSpec.OffAtlasMaterials whose UVs leave its scan atlas into a
        /// sub-mesh of their own, flagged in Source.offAtlas, which ResolveMaterials draws with the triplanar bark. A triangle
        /// leaves the atlas when a UV is outside [0, 1] (the atlas is not tileable) or when its texel density is under
        /// ForestKitSpec.OffAtlasDensityShare of the density of the material's lowest metre, the root flare the scan covers.
        /// fir_tree_01 (M4, build/issues/M4-tall-firs.md): trunk_b tiles everything above 1.8 m across the whole atlas (u 0 to 5,
        /// v -1 to 0.4). (trunk_a maps everything above 3 m to a strip one fiftieth as dense as its base; it is not listed, see
        /// ForestKitSpec.OffAtlasMaterials.)
        /// </summary>
        internal static Source SplitOffAtlas(Source source, string nodeName)
        {
            ForestMeshTools.MeshData data = source.data;
            if (data.uv0 == null) return source;
            var subMeshes = new List<int[]>(data.subMeshes);
            var names = new List<string>(source.materialNames);
            var flags = new List<bool>(source.offAtlas ?? new bool[data.subMeshes.Length]);
            Vector3[] p = data.positions;
            Vector2[] uv = data.uv0;
            for (int s = 0; s < data.subMeshes.Length; s++)
            {
                if (Array.IndexOf(ForestKitSpec.OffAtlasMaterials, source.materialNames[s]) < 0) continue;
                int[] indices = data.subMeshes[s];
                int count = indices.Length / 3;
                if (count == 0) continue;

                var density = new double[count];
                var worldArea = new double[count];
                float lowest = float.PositiveInfinity;
                for (int t = 0; t < count; t++) lowest = Mathf.Min(lowest, Mathf.Min(p[indices[3 * t]].y, Mathf.Min(p[indices[3 * t + 1]].y, p[indices[3 * t + 2]].y)));
                double baseUv = 0, baseWorld = 0;
                for (int t = 0; t < count; t++)
                {
                    int a = indices[3 * t], b = indices[3 * t + 1], c = indices[3 * t + 2];
                    worldArea[t] = 0.5 * Vector3.Cross(p[b] - p[a], p[c] - p[a]).magnitude;
                    double uvArea = 0.5 * Math.Abs((uv[b].x - uv[a].x) * (uv[c].y - uv[a].y) - (uv[c].x - uv[a].x) * (uv[b].y - uv[a].y));
                    density[t] = worldArea[t] > 1e-12 ? uvArea / worldArea[t] : double.PositiveInfinity;
                    float y = (p[a].y + p[b].y + p[c].y) / 3f;
                    if (y < lowest + 1f && worldArea[t] > 1e-12) { baseUv += uvArea; baseWorld += worldArea[t]; }
                }
                double floor = baseWorld > 0 ? ForestKitSpec.OffAtlasDensityShare * baseUv / baseWorld : 0;

                var keep = new List<int>(indices.Length);
                var moved = new List<int>();
                double movedArea = 0, totalArea = 0;
                for (int t = 0; t < count; t++)
                {
                    bool outside = false;
                    for (int k = 0; k < 3; k++)
                    {
                        Vector2 q = uv[indices[3 * t + k]];
                        outside |= q.x < -0.001f || q.x > 1.001f || q.y < -0.001f || q.y > 1.001f;
                    }
                    bool off = outside || density[t] < floor;
                    List<int> target = off ? moved : keep;
                    target.Add(indices[3 * t]); target.Add(indices[3 * t + 1]); target.Add(indices[3 * t + 2]);
                    totalArea += worldArea[t];
                    if (off) movedArea += worldArea[t];
                }
                if (moved.Count == 0) continue;
                subMeshes[s] = keep.ToArray();
                subMeshes.Add(moved.ToArray());
                names.Add(source.materialNames[s]);
                flags.Add(true);
                Debug.Log($"Forest kit: {movedArea / totalArea * 100.0:0.0} % of the surface of {source.materialNames[s]} on '{nodeName}' leaves its scan atlas "
                    + $"(UVs outside [0, 1] or under {ForestKitSpec.OffAtlasDensityShare:0.00} of the root flare's texel density); it is drawn with the triplanar "
                    + $"{ForestKitSpec.OffAtlasFallback}.");
            }
            if (names.Count == source.materialNames.Length) return source;
            var split = new ForestMeshTools.MeshData
            {
                positions = data.positions, normals = data.normals, tangents = data.tangents, uv0 = data.uv0, subMeshes = subMeshes.ToArray(),
            };
            return new Source { data = split, materialNames = names.ToArray(), bounds = source.bounds, mirror = source.mirror, offAtlas = flags.ToArray() };
        }

        /// <summary>
        /// Reads one object of a glTF 2.0 file (as Blender exports it: float positions, normals and UVs, one
        /// primitive per material) into prefab space. The glTF is right-handed with Y up, which is the manifest's
        /// source space; it is mirrored the way the importer mirrored the same object of the FBX, so both
        /// routes give the same prefab. KHR_texture_transform on a material's base colour is baked into the UVs,
        /// because two materials that share one set of maps (sameAs) carry different transforms.
        /// </summary>
        static class ForestGltfGeometry
        {
            [Serializable] sealed class Document { public Node[] nodes; public GltfMesh[] meshes; public Accessor[] accessors; public BufferView[] bufferViews; public GltfBuffer[] buffers; public GltfMaterial[] materials; }
            [Serializable] sealed class Node { public string name; public int mesh; public float[] rotation; public float[] scale; public float[] matrix; }
            [Serializable] sealed class GltfMesh { public string name; public Primitive[] primitives; }
            [Serializable] sealed class Primitive { public Attributes attributes; public int indices; public int material; public int mode; }
            [Serializable] sealed class Attributes { public int POSITION; public int NORMAL; public int TEXCOORD_0; }
            [Serializable] sealed class Accessor { public int bufferView; public int byteOffset; public int componentType; public int count; public string type; }
            [Serializable] sealed class BufferView { public int buffer; public int byteOffset; public int byteLength; public int byteStride; }
            [Serializable] sealed class GltfBuffer { public int byteLength; }
            [Serializable] sealed class GltfMaterial { public string name; public Pbr pbrMetallicRoughness; public TextureInfo normalTexture; }
            [Serializable] sealed class Pbr { public TextureInfo baseColorTexture; }
            [Serializable] sealed class TextureInfo { public TextureExtensions extensions; }
            [Serializable] sealed class TextureExtensions { public TextureTransform KHR_texture_transform; }
            [Serializable] sealed class TextureTransform { public float[] offset; public float[] scale; public float rotation; }

            const int FloatType = 5126, UnsignedByte = 5121, UnsignedShort = 5123, UnsignedInt = 5125;

            internal static Source Read(string gltfPath, string binPath, string nodeName, ForestAssetEntry owner, Source fbx)
            {
                if (!BitConverter.IsLittleEndian) throw new NotSupportedException("glTF buffers are little-endian; this machine is not.");
                string text = File.ReadAllText(gltfPath);
                if (text.Contains("\"sparse\"") || text.Contains("KHR_draco_mesh_compression") || text.Contains("KHR_mesh_quantization"))
                    throw new NotSupportedException($"{gltfPath} uses sparse accessors or compressed geometry, which this reader does not handle.");
                Document document = JsonUtility.FromJson<Document>(text);
                byte[] bin = File.ReadAllBytes(binPath);
                if (document?.buffers == null || document.buffers.Length != 1 || document.buffers[0].byteLength != bin.Length)
                    throw new InvalidDataException($"{gltfPath} must name exactly one buffer of {bin.Length} bytes ({binPath}).");
                // JsonUtility leaves an absent "mode" at 0 (points); Blender writes no mode for triangles.
                bool modesGiven = text.Contains("\"mode\"");

                Node node = null;
                foreach (Node candidate in document.nodes ?? Array.Empty<Node>())
                    if (candidate.name == nodeName) node = candidate;
                if (node == null) throw new InvalidDataException($"{gltfPath} has no node named '{nodeName}'.");
                if (!IsIdentity(node.rotation, 0f, 0f, 0f, 1f) || !IsIdentity(node.scale, 1f, 1f, 1f) || (node.matrix != null && node.matrix.Length > 0))
                    throw new InvalidDataException($"Node '{nodeName}' of {gltfPath} rotates or scales its mesh; only a translation is expected (it is dropped, as for the FBX).");
                if (node.mesh < 0 || node.mesh >= document.meshes.Length) throw new InvalidDataException($"Node '{nodeName}' of {gltfPath} has no mesh.");

                var positions = new List<Vector3>();
                var normals = new List<Vector3>();
                var uvs = new List<Vector2>();
                var subMeshes = new List<int[]>();
                var names = new List<string>();
                foreach (Primitive primitive in document.meshes[node.mesh].primitives)
                {
                    if (modesGiven && primitive.mode != 4) throw new InvalidDataException($"A primitive of '{nodeName}' is not a triangle list (mode {primitive.mode}).");
                    Attributes attributes = primitive.attributes;
                    if (attributes == null || attributes.NORMAL == attributes.POSITION)
                        throw new InvalidDataException($"A primitive of '{nodeName}' lacks normals.");
                    string material = primitive.material >= 0 && primitive.material < document.materials.Length ? document.materials[primitive.material].name : null;
                    if (material == null || !HasMaterial(owner, material))
                        throw new InvalidDataException($"A primitive of '{nodeName}' uses material '{material}', which the manifest does not list for {owner.id}.");

                    float[] p = Floats(document, bin, attributes.POSITION, "VEC3");
                    float[] n = Floats(document, bin, attributes.NORMAL, "VEC3");
                    int count = p.Length / 3;
                    // JsonUtility leaves an absent attribute at 0, which is also a valid accessor index. A primitive has UVs
                    // when it names TEXCOORD_0 and that accessor is a float VEC2 of one element per vertex.
                    bool hasUv = attributes.TEXCOORD_0 != attributes.POSITION && attributes.TEXCOORD_0 != attributes.NORMAL
                        && IsUvAccessor(document, attributes.TEXCOORD_0, count)
                        && (attributes.TEXCOORD_0 != 0 || Regex.IsMatch(text, "\"TEXCOORD_0\"\\s*:\\s*0\\s*[,}]"));
                    float[] t;
                    if (hasUv)
                    {
                        t = Floats(document, bin, attributes.TEXCOORD_0, "VEC2");
                    }
                    else
                    {
                        // fir_tree_01_trunk_c: neither the FBX nor the glTF carries UVs for it (spec 4.2, risk 23). Its UVs stay
                        // at zero, which ResolveMaterials reads as "no usable UVs" and answers with the triplanar fallback.
                        // Any other material without UVs cannot be textured, and stops the build as before.
                        ForestMaterialEntry entry = ForestManifest.FindMaterial(material);
                        if (entry == null || string.IsNullOrEmpty(entry.noUvFallback))
                            throw new InvalidDataException($"A primitive of '{nodeName}' ({material}) has no UVs, and its material has no noUvFallback.");
                        t = new float[count * 2];
                    }
                    if (n.Length / 3 != count || t.Length / 2 != count) throw new InvalidDataException($"A primitive of '{nodeName}' has attributes of different lengths.");
                    TextureTransform transform = Transform(document.materials[primitive.material], nodeName);
                    Vector2 offset = transform != null && transform.offset != null && transform.offset.Length == 2 ? new Vector2(transform.offset[0], transform.offset[1]) : Vector2.zero;
                    Vector2 scale = transform != null && transform.scale != null && transform.scale.Length == 2 ? new Vector2(transform.scale[0], transform.scale[1]) : Vector2.one;

                    int first = positions.Count;
                    for (int v = 0; v < count; v++)
                    {
                        positions.Add(new Vector3(p[3 * v], p[3 * v + 1], p[3 * v + 2]));
                        normals.Add(new Vector3(n[3 * v], n[3 * v + 1], n[3 * v + 2]));
                        // KHR_texture_transform: uv' = offset + scale * uv, in glTF's space (origin top left). Unity's origin is bottom left.
                        float u = offset.x + scale.x * t[2 * v], w = offset.y + scale.y * t[2 * v + 1];
                        uvs.Add(new Vector2(u, 1f - w));
                    }
                    int[] indices = Indices(document, bin, primitive.indices, count);
                    for (int i = 0; i < indices.Length; i++) indices[i] += first;
                    subMeshes.Add(indices);
                    names.Add(material);
                }

                var data = new ForestMeshTools.MeshData
                {
                    positions = positions.ToArray(), normals = normals.ToArray(), uv0 = uvs.ToArray(), subMeshes = subMeshes.ToArray(),
                };
                // Mirror each horizontal axis the way the importer mirrored the same object of the FBX.
                Bounds raw = ForestMeshTools.BoundsOf(data);
                float sx = MirrorOf(raw, fbx.bounds, 0, true, nodeName), sz = MirrorOf(raw, fbx.bounds, 2, false, nodeName);
                if (sx < 0f || sz < 0f) ForestMeshTools.Transform(data, Matrix4x4.Scale(new Vector3(sx, 1f, sz)));

                var source = new Source { data = data, materialNames = names.ToArray(), bounds = ForestMeshTools.BoundsOf(data), mirror = "symmetric" };
                RequireSameSurface(source, fbx, nodeName);
                return source;
            }

            // -1 when the FBX object came out mirrored on this axis, 1 when not. When the box cannot tell (the
            // object is symmetric to 2 mm on that axis) the answer of probe P1 stands: the importer mirrors X.
            static float MirrorOf(Bounds raw, Bounds imported, int axis, bool mirroredWhenUnsure, string nodeName)
            {
                const float tolerance = 0.002f;
                bool same = Mathf.Abs(imported.min[axis] - raw.min[axis]) < tolerance && Mathf.Abs(imported.max[axis] - raw.max[axis]) < tolerance;
                bool mirrored = Mathf.Abs(imported.min[axis] + raw.max[axis]) < tolerance && Mathf.Abs(imported.max[axis] + raw.min[axis]) < tolerance;
                if (same && mirrored) return mirroredWhenUnsure ? -1f : 1f;
                if (mirrored) return -1f;
                if (same) return 1f;
                throw new InvalidDataException(
                    $"'{nodeName}' spans {raw.min[axis]:0.0000} to {raw.max[axis]:0.0000} m on axis {axis} in the glTF and {imported.min[axis]:0.0000} to "
                    + $"{imported.max[axis]:0.0000} m in the imported FBX, mirrored or not: the two files do not hold the same object.");
            }

            // Order and splitting of vertices differ between the two files; what they cover may not.
            static void RequireSameSurface(Source gltf, Source fbx, string nodeName)
            {
                Dictionary<string, double[]> a = SurfaceByMaterial(gltf), b = SurfaceByMaterial(fbx);
                var problems = new List<string>();
                foreach (KeyValuePair<string, double[]> pair in a)
                {
                    if (!b.TryGetValue(pair.Key, out double[] other))
                    {
                        problems.Add($"{pair.Key} is not in the FBX");
                        continue;
                    }
                    double[] mine = pair.Value;
                    if (Math.Abs(mine[0] - other[0]) > Math.Max(2.0, 0.005 * other[0]))
                        problems.Add($"{pair.Key}: {mine[0]} triangles against {other[0]}");
                    if (Math.Abs(mine[1] - other[1]) > 0.002 * other[1])
                        problems.Add($"{pair.Key}: area {mine[1]:0.0000} m2 against {other[1]:0.0000}");
                    for (int axis = 0; axis < 3; axis++)
                        if (Math.Abs(mine[2 + axis] - other[2 + axis]) > 0.002)
                            problems.Add($"{pair.Key}: centre {mine[2 + axis]:0.0000} m against {other[2 + axis]:0.0000} on axis {axis}");
                }
                if (a.Count != b.Count) problems.Add($"{a.Count} materials against {b.Count}");
                if (problems.Count > 0)
                    throw new InvalidDataException($"'{nodeName}' in the glTF is not the object the FBX holds: {string.Join("; ", problems)}.");
            }

            // Per material: triangles, area, and area-weighted centre (x, y, z).
            static Dictionary<string, double[]> SurfaceByMaterial(Source source)
            {
                var result = new Dictionary<string, double[]>();
                for (int s = 0; s < source.materialNames.Length; s++)
                {
                    if (!result.TryGetValue(source.materialNames[s], out double[] sums)) result[source.materialNames[s]] = sums = new double[5];
                    int[] indices = source.data.subMeshes[s];
                    Vector3[] p = source.data.positions;
                    for (int t = 0; t + 2 < indices.Length; t += 3)
                    {
                        Vector3 a = p[indices[t]], b = p[indices[t + 1]], c = p[indices[t + 2]];
                        double area = 0.5 * Vector3.Cross(b - a, c - a).magnitude;
                        Vector3 centre = (a + b + c) / 3f;
                        sums[0] += 1.0;
                        sums[1] += area;
                        sums[2] += area * centre.x;
                        sums[3] += area * centre.y;
                        sums[4] += area * centre.z;
                    }
                }
                foreach (double[] sums in result.Values)
                    if (sums[1] > 0.0)
                        for (int axis = 2; axis < 5; axis++) sums[axis] /= sums[1];
                return result;
            }

            static TextureTransform Transform(GltfMaterial material, string nodeName)
            {
                TextureTransform colour = material.pbrMetallicRoughness?.baseColorTexture?.extensions?.KHR_texture_transform;
                TextureTransform normal = material.normalTexture?.extensions?.KHR_texture_transform;
                if (!SameTransform(colour, normal))
                    throw new InvalidDataException($"Material {material.name} of '{nodeName}' maps its base colour and its normal map differently; one UV set cannot serve both.");
                if (colour != null && colour.rotation != 0f)
                    throw new InvalidDataException($"Material {material.name} of '{nodeName}' rotates its texture, which this reader does not handle.");
                return colour;
            }

            static bool SameTransform(TextureTransform a, TextureTransform b)
            {
                Vector4 Of(TextureTransform t) => t == null ? new Vector4(0f, 0f, 1f, 1f) : new Vector4(
                    t.offset != null && t.offset.Length == 2 ? t.offset[0] : 0f, t.offset != null && t.offset.Length == 2 ? t.offset[1] : 0f,
                    t.scale != null && t.scale.Length == 2 ? t.scale[0] : 1f, t.scale != null && t.scale.Length == 2 ? t.scale[1] : 1f);
                return (Of(a) - Of(b)).sqrMagnitude < 1e-10f && (a?.rotation ?? 0f) == (b?.rotation ?? 0f);
            }

            static bool IsIdentity(float[] values, params float[] identity)
            {
                if (values == null || values.Length == 0) return true;
                if (values.Length != identity.Length) return false;
                for (int i = 0; i < values.Length; i++)
                    if (Mathf.Abs(values[i] - identity[i]) > 1e-6f) return false;
                return true;
            }

            static float[] Floats(Document document, byte[] bin, int index, string type)
            {
                Accessor accessor = AccessorAt(document, index);
                int components = type == "VEC3" ? 3 : 2;
                if (accessor.componentType != FloatType || accessor.type != type)
                    throw new InvalidDataException($"Accessor {index} is {accessor.type} of component type {accessor.componentType}; expected {type} of floats.");
                int element = components * 4;
                long start = Start(document, accessor, element, out int stride);
                var values = new float[accessor.count * components];
                if (stride == element)
                {
                    Buffer.BlockCopy(bin, (int)start, values, 0, values.Length * 4);
                }
                else
                {
                    for (int i = 0; i < accessor.count; i++)
                        Buffer.BlockCopy(bin, (int)(start + (long)i * stride), values, i * element, element);
                }
                foreach (float value in values)
                    if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException($"Accessor {index} holds a value that is not finite.");
                return values;
            }

            static int[] Indices(Document document, byte[] bin, int index, int vertexCount)
            {
                Accessor accessor = AccessorAt(document, index);
                if (accessor.type != "SCALAR" || accessor.count % 3 != 0)
                    throw new InvalidDataException($"Accessor {index} is not a triangle index list.");
                int size = accessor.componentType == UnsignedInt ? 4 : accessor.componentType == UnsignedShort ? 2 : accessor.componentType == UnsignedByte ? 1
                    : throw new InvalidDataException($"Accessor {index} has index component type {accessor.componentType}.");
                long start = Start(document, accessor, size, out int stride);
                var indices = new int[accessor.count];
                for (int i = 0; i < indices.Length; i++)
                {
                    int at = (int)(start + (long)i * stride);
                    long value = size == 4 ? BitConverter.ToUInt32(bin, at) : size == 2 ? BitConverter.ToUInt16(bin, at) : bin[at];
                    if (value >= vertexCount) throw new InvalidDataException($"Accessor {index} points past the {vertexCount} vertices of its primitive.");
                    indices[i] = (int)value;
                }
                return indices;
            }

            static bool IsUvAccessor(Document document, int index, int vertexCount)
            {
                if (document.accessors == null || index < 0 || index >= document.accessors.Length) return false;
                Accessor accessor = document.accessors[index];
                return accessor.type == "VEC2" && accessor.componentType == FloatType && accessor.count == vertexCount;
            }

            static Accessor AccessorAt(Document document, int index)
            {
                if (document.accessors == null || index < 0 || index >= document.accessors.Length) throw new InvalidDataException($"There is no accessor {index}.");
                Accessor accessor = document.accessors[index];
                if (accessor.count <= 0) throw new InvalidDataException($"Accessor {index} is empty.");
                return accessor;
            }

            // Byte offset of the accessor's first element in the buffer; checks that its last one is inside its view.
            static long Start(Document document, Accessor accessor, int element, out int stride)
            {
                if (document.bufferViews == null || accessor.bufferView < 0 || accessor.bufferView >= document.bufferViews.Length)
                    throw new InvalidDataException("An accessor names a buffer view that does not exist.");
                BufferView view = document.bufferViews[accessor.bufferView];
                if (view.buffer != 0) throw new InvalidDataException("A buffer view names a second buffer.");
                stride = view.byteStride > 0 ? view.byteStride : element;
                long start = (long)view.byteOffset + accessor.byteOffset;
                long end = start + (long)(accessor.count - 1) * stride + element;
                if (start < 0 || end > (long)view.byteOffset + view.byteLength || end > document.buffers[0].byteLength)
                    throw new InvalidDataException("An accessor reaches past the end of its buffer view.");
                return start;
            }
        }

        /// <summary>
        /// The kit material of every sub-mesh. A material with a noUvFallback takes the triplanar variant
        /// of that fallback when its sub-mesh has no usable UVs or it is listed in ForceTriplanar; its
        /// name is then added to <paramref name="triplanar"/>.
        /// </summary>
        internal static Material[] ResolveMaterials(Source source, List<string> triplanar)
        {
            string[] forced = ForestKitSpec.ForceTriplanar ?? Array.Empty<string>();
            var materials = new Material[source.materialNames.Length];
            bool anyOffAtlas = source.offAtlas != null && Array.IndexOf(source.offAtlas, true) >= 0;
            Material fallbackBark = anyOffAtlas ? ForestMaterials.Get(ForestKitSpec.OffAtlasFallback) : null;
            for (int i = 0; i < materials.Length; i++)
            {
                string name = source.materialNames[i];
                if (anyOffAtlas && ForestKitSpec.BranchesTakeOffAtlasBark && fallbackBark != null && ForestMaterials.Get(name) == fallbackBark)
                {
                    // A tree whose trunk is partly drawn with the triplanar bark (SplitOffAtlas, fir_tree_01_b) draws its branch bark
                    // with the same material, so the two share one sub-mesh and the root skirt fits in the contract's four
                    // (review 2 of the M4 captures: b stood on a near-black scanned mat). The bark is the same tileable texture.
                    materials[i] = ForestMaterials.GetTriplanar(ForestKitSpec.OffAtlasFallback);
                    if (materials[i] == null)
                        throw new InvalidOperationException(
                            $"The branches of {name} need the triplanar variant of {ForestKitSpec.OffAtlasFallback}, which has not been built. Run Gamex/Forest/1 Import Assets.");
                    continue;
                }
                if (source.skirt != null && i < source.skirt.Length && source.skirt[i])
                {
                    // The ground skirt of a root flare (SplitSkirt): the floor's own texture, projected from above.
                    materials[i] = ForestMaterials.GetGround(ForestKitSpec.SkirtGroundLayer);
                    if (materials[i] == null)
                        throw new InvalidOperationException(
                            $"The root skirt of {name} needs the ground variant of {ForestKitSpec.SkirtGroundLayer}, which has not been built. Run Gamex/Forest/1 Import Assets.");
                    continue;
                }
                if (source.offAtlas != null && i < source.offAtlas.Length && source.offAtlas[i])
                {
                    // The part of a scanned trunk whose UVs left its atlas (SplitOffAtlas): the tileable bark, projected.
                    materials[i] = ForestMaterials.GetTriplanar(ForestKitSpec.OffAtlasFallback);
                    if (materials[i] == null)
                        throw new InvalidOperationException(
                            $"Part of {name} needs the triplanar variant of {ForestKitSpec.OffAtlasFallback}, which has not been built. Run Gamex/Forest/1 Import Assets.");
                    if (triplanar != null && !triplanar.Contains(name)) triplanar.Add(name);
                    continue;
                }
                ForestMaterialEntry entry = ForestManifest.FindMaterial(name);
                bool hasFallback = entry != null && !string.IsNullOrEmpty(entry.noUvFallback);
                if (hasFallback && (Array.IndexOf(forced, name) >= 0 || !ForestMeshTools.HasUsableUv(source.data, i)))
                {
                    // The fallback's textures tile, which a scan atlas does not, so they can be projected.
                    materials[i] = ForestMaterials.GetTriplanar(entry.noUvFallback);
                    if (materials[i] == null)
                        throw new InvalidOperationException(
                            $"Material {name} needs the triplanar variant of {entry.noUvFallback}, which has not been built. Run Gamex/Forest/1 Import Assets.");
                    if (triplanar != null && !triplanar.Contains(name)) triplanar.Add(name);
                    continue;
                }

                materials[i] = ForestMaterials.Get(name);
                if (materials[i] == null)
                    throw new InvalidOperationException($"Material {name} has not been built. Run Gamex/Forest/1 Import Assets.");
                RequireAoUnderUvs(source, i, name);
            }
            return materials;
        }

        // Below this the AO channel of a mask is "nothing was baked here"; a share of the surface above
        // MaxAoGapShare with such texels under it is a map that does not belong to the mesh. Measured on
        // the M2 kit: 0.0 to 0.3 % for twelve healthy models, 7 to 32 % for the six rocks of
        // rock_moss_set_01 before their AO was taken out (ForestTexturePacker.UnusableAo).
        const byte AoFloor = 8;
        const double MaxAoGapShare = 0.02;

        sealed class MaskPixels { public Color32[] pixels; public int width, height; }
        static readonly Dictionary<string, MaskPixels> maskCache = new();

        /// <summary>
        /// Refuses a sub-mesh whose UVs reach parts of its material's mask map that carry no ambient
        /// occlusion. HDRP multiplies sky light by that channel, so such a surface renders with black
        /// patches. The packed mask on disk is sampled under every triangle, weighted by its area.
        /// </summary>
        static void RequireAoUnderUvs(Source source, int subMesh, string materialName)
        {
            ForestMeshTools.MeshData data = source.data;
            if (data.uv0 == null || !ForestMeshTools.HasUsableUv(data, subMesh)) return;
            ForestMaterialEntry material = ForestManifest.ResolveMaterial(materialName);
            if (material == null) return;

            if (!maskCache.TryGetValue(material.name, out MaskPixels mask))
            {
                string diskPath = ForestManifest.ProjectDiskPath(ForestTexturePacker.MaskPath(material.name));
                mask = null;
                if (File.Exists(diskPath))
                {
                    mask = new MaskPixels();
                    mask.pixels = ForestTexturePacker.ReadPixels(diskPath, out mask.width, out mask.height);
                }
                maskCache[material.name] = mask;
            }
            if (mask == null) return;   // a material without a mask map has no AO channel to be wrong

            // A leaf card is drawn only where its atlas passes the alpha test. What lies under the cut-away
            // part of a card is never shaded, so it is not counted: a leaf atlas may well have no AO there.
            MaskPixels leaves = null;
            float cutoff = 0f;
            if (ForestMaterials.IsFoliage(material.name))
            {
                string key = material.name + "|basecolor";
                if (!maskCache.TryGetValue(key, out leaves))
                {
                    string diskPath = ForestManifest.ProjectDiskPath(ForestTexturePacker.FoliageBaseColorPath(material.name));
                    if (File.Exists(diskPath))
                    {
                        leaves = new MaskPixels();
                        leaves.pixels = ForestTexturePacker.ReadPixels(diskPath, out leaves.width, out leaves.height);
                    }
                    maskCache[key] = leaves;
                }
                cutoff = material.alphaCutoff * 255f;
            }

            int[] indices = data.subMeshes[subMesh];
            double total = 0.0, gaps = 0.0;
            for (int t = 0; t + 2 < indices.Length; t += 3)
            {
                int a = indices[t], b = indices[t + 1], c = indices[t + 2];
                double area = 0.5 * Vector3.Cross(data.positions[b] - data.positions[a], data.positions[c] - data.positions[a]).magnitude;
                Vector2 uv = (data.uv0[a] + data.uv0[b] + data.uv0[c]) / 3f;
                float u = Mathf.Repeat(uv.x, 1f), v = Mathf.Repeat(uv.y, 1f);
                if (leaves != null)
                {
                    int lx = Mathf.Clamp(Mathf.FloorToInt(u * leaves.width), 0, leaves.width - 1);
                    int ly = Mathf.Clamp(Mathf.FloorToInt(v * leaves.height), 0, leaves.height - 1);
                    if (leaves.pixels[ly * leaves.width + lx].a < cutoff) continue;
                }
                int x = Mathf.Clamp(Mathf.FloorToInt(u * mask.width), 0, mask.width - 1);
                int y = Mathf.Clamp(Mathf.FloorToInt(v * mask.height), 0, mask.height - 1);
                total += area;
                if (mask.pixels[y * mask.width + x].g < AoFloor) gaps += area;
            }
            if (total <= 0.0) return;

            double share = gaps / total;
            if (share > MaxAoGapShare)
                throw new InvalidOperationException(
                    $"The mask map of material {material.name} has no ambient occlusion under {share:P1} of the surface that uses it "
                    + $"(limit {MaxAoGapShare:P0}): its AO channel was not baked for these UVs, and the surface would render with black patches. "
                    + "If the base colour and normal maps do fit, list the material in ForestTexturePacker.UnusableAo and run Gamex/Forest/1 Import Assets.");
        }

        /// <summary>Sub-meshes that end up with the same material become one, so they cost one draw call.</summary>
        internal static ForestMeshTools.MeshData GroupByMaterial(ForestMeshTools.MeshData data, Material[] perSubMesh, out Material[] materials)
        {
            var distinct = new List<Material>();
            var members = new List<List<int>>();
            for (int i = 0; i < perSubMesh.Length; i++)
            {
                if (data.subMeshes[i].Length == 0) continue;
                int group = distinct.IndexOf(perSubMesh[i]);
                if (group < 0)
                {
                    group = distinct.Count;
                    distinct.Add(perSubMesh[i]);
                    members.Add(new List<int>());
                }
                members[group].Add(i);
            }

            var parts = new ForestMeshTools.MeshData[distinct.Count];
            for (int group = 0; group < parts.Length; group++) parts[group] = ForestMeshTools.Extract(data, members[group].ToArray());
            materials = distinct.ToArray();
            return ForestMeshTools.Merge(parts);
        }

        /// <summary>
        /// Stores a mesh at the path. An existing asset is overwritten in place, so its GUID, which prefabs
        /// refer to, never changes. Returns the asset.
        /// </summary>
        internal static Mesh SaveMesh(Mesh mesh, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            EditorUtility.SetDirty(existing);
            // Written now, not at the end of the stage: a tree baked by itself has no later SaveAssets call.
            AssetDatabase.SaveAssetIfDirty(existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            return existing;
        }

        internal static ForestKitLodReport LevelReport(Mesh mesh, float untilDistance)
        {
            long indices = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) indices += mesh.GetIndexCount(i);
            return new ForestKitLodReport
            {
                triangles = (int)(indices / 3),
                vertices = mesh.vertexCount,
                untilDistance = untilDistance,
                keepRatio = 1f,
                pieceScale = 1f,
                coverage = 1f,
            };
        }

        /// <summary>Writes the prefab asset with the structure every other package relies on (section 5.6.6).</summary>
        internal static void SavePrefab(Parts parts)
        {
            if (parts.levels == null || parts.levels.Length < 1 || parts.levels.Length > 4)
                throw new InvalidOperationException($"{parts.prefabId}: a prefab has one to four levels.");

            var root = new GameObject(parts.prefabId);
            try
            {
                var renderers = new Renderer[parts.levels.Length][];
                for (int i = 0; i < parts.levels.Length; i++)
                {
                    Level level = parts.levels[i];
                    MeshRenderer renderer = AddRenderer(root.transform, "LOD" + i, level.mesh, level.materials,
                        level.castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off, parts.prefabId);
                    renderers[i] = new Renderer[] { renderer };
                }

                if (parts.shadowMesh != null)
                {
                    MeshRenderer shadow = AddRenderer(root.transform, "LOD0_Shadow", parts.shadowMesh, parts.shadowMaterials,
                        ShadowCastingMode.ShadowsOnly, parts.prefabId);
                    renderers[0] = new[] { renderers[0][0], shadow };
                }

                // Only matters for the probe bake of milestone 5; trees, ferns, grass and branches carry no flag.
                if (parts.contributeGI)
                    GameObjectUtility.SetStaticEditorFlags(renderers[0][0].gameObject, StaticEditorFlags.ContributeGI);

                if (parts.collisionMesh != null)
                {
                    var holder = new GameObject("Collision");
                    holder.transform.SetParent(root.transform, false);
                    var collider = holder.AddComponent<MeshCollider>();
                    collider.convex = false;
                    collider.sharedMesh = parts.collisionMesh;
                }
                if (parts.capsuleRadius > 0f)
                {
                    var capsule = root.AddComponent<CapsuleCollider>();
                    capsule.direction = 1;
                    capsule.radius = parts.capsuleRadius;
                    capsule.height = ForestKitSpec.TrunkCapsuleHeight;
                    capsule.center = new Vector3(0f, ForestKitSpec.TrunkCapsuleHeight * 0.5f, 0f);
                }

                // Height for a tree, width for a flat prop: the largest side either way. A tree that is taller on screen
                // than MaxFirstTransitionHeight where its first level ends gets a smaller LOD size (ForestKitSpec.LodGroupSize),
                // so that level ends where the table says instead of where the height clamp puts it.
                Vector3 extent = parts.bounds.size;
                var distances = new float[parts.levels.Length];
                for (int i = 0; i < distances.Length; i++) distances[i] = parts.levels[i].untilDistance;
                float size = ForestKitSpec.LodGroupSize(Mathf.Max(extent.x, Mathf.Max(extent.y, extent.z)), distances[0]);
                float[] heights = ForestKitSpec.ScreenHeights(size, distances);

                var lods = new LOD[parts.levels.Length];
                for (int i = 0; i < lods.Length; i++)
                    lods[i] = new LOD(heights[i], renderers[i]) { fadeTransitionWidth = ForestKitSpec.FadeTransitionWidth };

                var group = root.AddComponent<LODGroup>();
                group.fadeMode = ForestKitSpec.UseCrossFade ? LODFadeMode.CrossFade : LODFadeMode.None;
                // Animated cross-fading would keep the object out of the GPU Resident Drawer later.
                group.animateCrossFading = false;
                group.SetLODs(lods);
                // Set explicitly, after the levels: RecalculateBounds would take a sphere around every level.
                group.localReferencePoint = parts.bounds.center;
                group.size = size;

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(parts.prefabId), out bool saved);
                if (!saved) throw new InvalidOperationException($"Unity could not save {PrefabPath(parts.prefabId)}.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>Replaces one prefab's entry in the kit report and saves the file.</summary>
        internal static void WriteReport(ForestKitPrefabReport entry)
        {
            ForestKitReport report = ForestKitReport.Load() ?? new ForestKitReport();
            report.unityVersion = Application.unityVersion;
            report.kitSpecHash = ForestKitSpec.Hash();

            var prefabs = new List<ForestKitPrefabReport>();
            if (report.prefabs != null)
            {
                foreach (ForestKitPrefabReport other in report.prefabs)
                    if (other != null && other.prefabId != entry.prefabId) prefabs.Add(other);
            }
            prefabs.Add(entry);
            prefabs.Sort((left, right) => string.CompareOrdinal(left.prefabId, right.prefabId));
            report.prefabs = prefabs.ToArray();
            report.Save();
        }

        /// <summary>Everything a prefab is made from, as one hash: source files, every kit constant, the tool version.</summary>
        internal static string InputHash(string prefabId)
        {
            var parts = new List<string> { prefabId, ForestKitSpec.Hash(), Application.unityVersion };
            foreach (ForestAssetEntry owner in SourceModels(prefabId))
            {
                parts.Add(owner.id);
                foreach (ForestAssetFile file in owner.files)
                    // The glTF geometry of a tree whose FBX carries no UVs is a mesh input as well (DeriveTreeSource).
                    if (file.map == "mesh" || file.map == "gltf" || file.map == "gltfBuffer") parts.Add(file.md5);
            }
            return ForestStamps.Hash(parts.ToArray());
        }

        internal static bool IsCurrent(string prefabId, string inputHash)
        {
            if (!ForestStamps.IsCurrent(PrefabPath(prefabId), inputHash) || Load(prefabId) == null) return false;
            ForestKitReport report = ForestKitReport.Load();
            return report != null && report.Find(prefabId) != null;
        }

        internal static void MarkCurrent(string prefabId, string inputHash) => ForestStamps.Set(PrefabPath(prefabId), inputHash);

        // ---- Private ----

        static IEnumerable<string> BuildOrder()
        {
            string[][] groups =
            {
                ForestPrefabIds.Boulders, ForestPrefabIds.Logs, ForestPrefabIds.Stumps, ForestPrefabIds.Branches,
                ForestPrefabIds.FernClumps, ForestPrefabIds.FernPatches, ForestPrefabIds.GrassPatches,
                ForestPrefabIds.YoungFirs, ForestPrefabIds.TallFirs,
            };
            foreach (string[] group in groups)
                foreach (string prefabId in group) yield return prefabId;
        }

        static Outcome BuildOne(string prefabId, bool force)
        {
            ForestClass cls = ForestKitSpec.ClassOf(prefabId);

            string missing = MissingInput(prefabId, out bool needsAttention);
            if (missing != null)
            {
                // A class whose assets belong to a later milestone is simply not there yet. A model that is
                // on disk but unmeasured is different: somebody has to look at it.
                if (needsAttention) Debug.LogWarning($"Forest kit: skipped {prefabId}: {missing}.");
                else Debug.Log($"Forest kit: skipped {prefabId}: {missing}.");
                return Outcome.Skipped;
            }

            if (ForestKitSpec.IsTree(cls))
                return ForestTreeBaker.BakeIfNeeded(prefabId, force) ? Outcome.Built : Outcome.UpToDate;

            string inputHash = InputHash(prefabId);
            if (!force && IsCurrent(prefabId, inputHash)) return Outcome.UpToDate;

            var watch = System.Diagnostics.Stopwatch.StartNew();
            ForestKitPrefabReport report;
            switch (cls)
            {
                case ForestClass.Log:
                    report = BuildLog(prefabId);
                    break;
                case ForestClass.FernPatch:
                case ForestClass.GrassPatch:
                    report = BuildPatch(prefabId, cls);
                    break;
                default:
                    report = BuildScanProp(prefabId, cls);
                    break;
            }
            report.bakeSeconds = (float)watch.Elapsed.TotalSeconds;
            WriteReport(report);
            MarkCurrent(prefabId, inputHash);
            return Outcome.Built;
        }

        // Rocks, stump patches, dry branches and single fern clumps: the scan as the nearest level, then
        // decimated copies.
        static ForestKitPrefabReport BuildScanProp(string prefabId, ForestClass cls)
        {
            ForestObjectEntry entry = ForestManifest.FindObjectForPrefab(prefabId);
            ForestAssetEntry owner = ForestManifest.FindOwner(entry);
            Source source = DeriveSource(LoadModel(owner), owner, entry, entry.name, true);

            var triplanar = new List<string>();
            ForestMeshTools.MeshData scan = GroupByMaterial(source.data, ResolveMaterials(source, triplanar), out Material[] materials);
            int scanTriangles = ForestMeshTools.TriangleCount(scan);

            float[] distances = ForestKitSpec.LodDistances(prefabId);
            var levels = new Level[distances.Length];
            var reports = new ForestKitLodReport[distances.Length];
            for (int lod = 0; lod < levels.Length; lod++)
            {
                ForestMeshTools.MeshData data = scan;
                Material[] levelMaterials = materials;
                if (lod > 0)
                {
                    int target = Mathf.RoundToInt(scanTriangles * ForestKitSpec.ScanPropShare(lod));
                    data = ForestMeshTools.DecimateToTarget(scan, target);
                    DropEmptySubMeshes(ref data, ref levelMaterials);
                    if (levelMaterials.Length == 0)
                        throw new InvalidOperationException($"{prefabId}: nothing is left of level {lod} at {target} triangles.");
                }

                // The scan keeps the tangents it was imported with; a decimated level has new vertices.
                bool newTangents = lod > 0 || data.tangents == null;
                Mesh mesh = SaveMesh(ForestMeshTools.ToMesh(data, prefabId + "__lod" + lod, newTangents), MeshPath(prefabId, "lod" + lod));
                levels[lod] = new Level
                {
                    mesh = mesh, materials = levelMaterials, castShadows = ForestKitSpec.CastsShadows(cls, lod), untilDistance = distances[lod],
                };
                reports[lod] = LevelReport(mesh, distances[lod]);
            }

            var parts = new Parts { prefabId = prefabId, levels = levels, bounds = levels[0].mesh.bounds };
            float colliderRadius = 0f;
            if (ForestKitSpec.IsSolidProp(cls))
            {
                parts.collisionMesh = BuildCollisionMesh(prefabId, scan, out colliderRadius);
                parts.contributeGI = true;
            }
            SavePrefab(parts);

            return new ForestKitPrefabReport
            {
                prefabId = prefabId,
                boundsMin = parts.bounds.min,
                boundsMax = parts.bounds.max,
                mirror = source.mirror,
                colliderRadius = colliderRadius,
                triplanarMaterials = triplanar.ToArray(),
                lods = reports,
            };
        }

        // The log is the one model that ships authored levels: its four FBX objects are used as they are.
        static ForestKitPrefabReport BuildLog(string prefabId)
        {
            ForestObjectEntry entry = ForestManifest.FindObjectForPrefab(prefabId);
            ForestAssetEntry owner = ForestManifest.FindOwner(entry);
            float[] distances = ForestKitSpec.LodDistances(prefabId);
            if (owner.fbxNodes == null || owner.fbxNodes.Length != distances.Length)
                throw new InvalidOperationException(
                    $"{prefabId}: the manifest lists {(owner.fbxNodes == null ? 0 : owner.fbxNodes.Length)} FBX objects, the kit expects {distances.Length} authored levels.");

            GameObject model = LoadModel(owner);
            var triplanar = new List<string>();
            var levels = new Level[distances.Length];
            var reports = new ForestKitLodReport[distances.Length];
            ForestMeshTools.MeshData first = null;
            Bounds firstBounds = default;
            Matrix4x4 pitch = Matrix4x4.identity;
            string mirror = "symmetric";

            for (int lod = 0; lod < levels.Length; lod++)
            {
                // Only the first level has numbers in the manifest. The others must be the same object, coarser.
                Source source = DeriveSource(model, owner, entry, owner.fbxNodes[lod], lod == 0);
                if (lod == 0)
                {
                    firstBounds = source.bounds;
                    mirror = source.mirror;
                    pitch = LogPitch(firstBounds);
                }
                else
                {
                    RequireSameExtent(prefabId, lod, source.bounds, firstBounds);
                }

                ForestMeshTools.MeshData data = GroupByMaterial(source.data, ResolveMaterials(source, triplanar), out Material[] materials);
                if (ForestKitSpec.LogPitchDegrees != 0f) ForestMeshTools.Transform(data, pitch);
                if (lod == 0) first = data;

                Mesh mesh = SaveMesh(ForestMeshTools.ToMesh(data, prefabId + "__lod" + lod, data.tangents == null), MeshPath(prefabId, "lod" + lod));
                levels[lod] = new Level
                {
                    mesh = mesh, materials = materials, castShadows = ForestKitSpec.CastsShadows(ForestClass.Log, lod), untilDistance = distances[lod],
                };
                reports[lod] = LevelReport(mesh, distances[lod]);
            }

            var parts = new Parts { prefabId = prefabId, levels = levels, bounds = levels[0].mesh.bounds, contributeGI = true };
            parts.collisionMesh = BuildCollisionMesh(prefabId, first, out float colliderRadius);
            SavePrefab(parts);

            return new ForestKitPrefabReport
            {
                prefabId = prefabId,
                boundsMin = parts.bounds.min,
                boundsMax = parts.bounds.max,
                mirror = mirror,
                colliderRadius = colliderRadius,
                triplanarMaterials = triplanar.ToArray(),
                lods = reports,
            };
        }

        // Fern and grass patches: several derived clumps merged into one mesh per level, so a patch is one
        // draw call instead of five or twelve.
        static ForestKitPrefabReport BuildPatch(string prefabId, ForestClass cls)
        {
            ForestKitSpec.PatchSpec patch = ForestKitSpec.Patch(prefabId);
            bool fern = cls == ForestClass.FernPatch;
            float[] distances = ForestKitSpec.LodDistances(prefabId);
            if (distances.Length != (patch.farClumps.Length > 0 ? 2 : 1))
                throw new InvalidOperationException($"{prefabId}: the patch table and the distance table disagree on the number of levels.");

            var clumps = new Dictionary<string, Mesh>();
            try
            {
                Material material = null;
                foreach (string objectName in patch.clumps)
                {
                    if (clumps.ContainsKey(objectName)) continue;
                    ForestObjectEntry entry = ForestManifest.FindObject(objectName)
                        ?? throw new InvalidOperationException($"{prefabId}: the manifest has no object '{objectName}'.");
                    ForestAssetEntry owner = ForestManifest.FindOwner(entry);
                    Source source = DeriveSource(LoadModel(owner), owner, entry, entry.name, true);
                    ForestMeshTools.MeshData data = GroupByMaterial(source.data, ResolveMaterials(source, null), out Material[] materials);
                    if (materials.Length != 1 || (material != null && material != materials[0]))
                        throw new InvalidOperationException($"{prefabId}: every clump of a patch must use the same single material.");
                    material = materials[0];
                    clumps.Add(objectName, ForestMeshTools.ToMesh(data, objectName, data.tangents == null));
                }

                ClumpPlacement[] layout = fern
                    ? PatchLayout(patch, ForestKitSpec.FernPatchRadius, ForestKitSpec.FernScaleMin, ForestKitSpec.FernScaleMax)
                    : PatchLayout(patch, ForestKitSpec.GrassPatchRadius, ForestKitSpec.GrassScaleMin, ForestKitSpec.GrassScaleMax);

                var everyClump = new int[patch.clumps.Length];
                for (int i = 0; i < everyClump.Length; i++) everyClump[i] = i;

                var levels = new Level[distances.Length];
                var reports = new ForestKitLodReport[distances.Length];
                for (int lod = 0; lod < levels.Length; lod++)
                {
                    // The far level keeps a few clumps where they stood and makes them larger instead.
                    Mesh combined = lod == 0
                        ? Combine(prefabId + "__lod0", patch, layout, everyClump, 0f, clumps)
                        : Combine(prefabId + "__lod" + lod, patch, layout, patch.farClumps, ForestKitSpec.FernFarScale, clumps);
                    Mesh mesh = SaveMesh(combined, MeshPath(prefabId, "lod" + lod));
                    levels[lod] = new Level
                    {
                        mesh = mesh, materials = new[] { material }, castShadows = ForestKitSpec.CastsShadows(cls, lod), untilDistance = distances[lod],
                    };
                    reports[lod] = LevelReport(mesh, distances[lod]);
                }

                if (!fern && reports[0].triangles > ForestKitSpec.GrassPatchMaxTriangles)
                    throw new InvalidOperationException(
                        $"{prefabId} has {reports[0].triangles} triangles; a grass patch may have {ForestKitSpec.GrassPatchMaxTriangles}. Change its mix in ForestKitSpec.");

                var parts = new Parts { prefabId = prefabId, levels = levels, bounds = levels[0].mesh.bounds };
                SavePrefab(parts);

                return new ForestKitPrefabReport
                {
                    prefabId = prefabId,
                    boundsMin = parts.bounds.min,
                    boundsMax = parts.bounds.max,
                    // A patch has no box in the manifest, so it says nothing about the importer's mirror.
                    mirror = "symmetric",
                    lods = reports,
                };
            }
            finally
            {
                foreach (Mesh clump in clumps.Values) UnityEngine.Object.DestroyImmediate(clump);
            }
        }

        // Where each clump of a patch stands. Seeded per patch, so a patch looks the same in every build.
        static ClumpPlacement[] PatchLayout(ForestKitSpec.PatchSpec patch, float radius, float scaleMin, float scaleMax)
        {
            var random = new System.Random(ForestKitSpec.Seed ^ StableHash(patch.id));
            var layout = new ClumpPlacement[patch.clumps.Length];
            for (int i = 0; i < layout.Length; i++)
            {
                // The first clump stays near the middle. Each later one takes the roomiest of a few random
                // spots, which spreads them out instead of piling them up.
                int tries = i == 0 ? 1 : 24;
                float reach = i == 0 ? radius * 0.25f : radius;
                Vector3 best = Vector3.zero;
                float bestRoom = -1f;
                for (int attempt = 0; attempt < tries; attempt++)
                {
                    float distance = reach * Mathf.Sqrt((float)random.NextDouble());
                    float angle = (float)(random.NextDouble() * 2.0 * Math.PI);
                    var spot = new Vector3(distance * Mathf.Cos(angle), 0f, distance * Mathf.Sin(angle));
                    float room = float.PositiveInfinity;
                    for (int other = 0; other < i; other++) room = Mathf.Min(room, Vector3.Distance(spot, layout[other].position));
                    if (room > bestRoom)
                    {
                        bestRoom = room;
                        best = spot;
                    }
                }
                layout[i] = new ClumpPlacement
                {
                    position = best,
                    yaw = (float)(random.NextDouble() * 360.0),
                    scale = Mathf.Lerp(scaleMin, scaleMax, (float)random.NextDouble()),
                };
            }
            return layout;
        }

        static Mesh Combine(string name, ForestKitSpec.PatchSpec patch, ClumpPlacement[] layout, int[] which, float fixedScale,
            Dictionary<string, Mesh> clumps)
        {
            var instances = new CombineInstance[which.Length];
            long vertices = 0;
            for (int i = 0; i < which.Length; i++)
            {
                ClumpPlacement placement = layout[which[i]];
                Mesh clump = clumps[patch.clumps[which[i]]];
                float scale = fixedScale > 0f ? fixedScale : placement.scale;
                instances[i] = new CombineInstance
                {
                    mesh = clump,
                    subMeshIndex = 0,
                    transform = Matrix4x4.TRS(placement.position, Quaternion.Euler(0f, placement.yaw, 0f), Vector3.one * scale),
                };
                vertices += clump.vertexCount;
            }

            var mesh = new Mesh { name = name };
            mesh.indexFormat = vertices > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.CombineMeshes(instances, true, true, false);
            mesh.RecalculateBounds();
            mesh.Optimize();
            return mesh;
        }

        // A script-built mesh for the MeshCollider: 10 cm clusters, coarser if that is still too many triangles.
        static Mesh BuildCollisionMesh(string prefabId, ForestMeshTools.MeshData scan, out float radius)
        {
            // A collider needs positions only. Without UVs no texture seam splits a cell, which keeps it small.
            ForestMeshTools.MeshData joined = ForestMeshTools.Merge(scan);
            var shape = new ForestMeshTools.MeshData { positions = joined.positions, normals = joined.normals, subMeshes = joined.subMeshes };

            ForestMeshTools.MeshData collision = ForestMeshTools.ClusterDecimate(shape, ForestKitSpec.CollisionCell);
            if (ForestMeshTools.TriangleCount(collision) >= ForestKitSpec.CollisionMaxTriangles)
                collision = ForestMeshTools.ClusterToTarget(shape, ForestKitSpec.CollisionMaxTriangles - 1);
            if (ForestMeshTools.TriangleCount(collision) == 0)
                throw new InvalidOperationException($"{prefabId}: the collision mesh came out empty.");

            radius = 0f;
            foreach (Vector3 point in collision.positions)
                radius = Mathf.Max(radius, Mathf.Sqrt(point.x * point.x + point.z * point.z));
            return SaveMesh(ForestMeshTools.ToMesh(collision, prefabId + "__col", false), MeshPath(prefabId, "col"));
        }

        // The alternative grounding of section 7.3: tip the log about the horizontal axis across its length.
        static Matrix4x4 LogPitch(Bounds bounds)
        {
            if (ForestKitSpec.LogPitchDegrees == 0f) return Matrix4x4.identity;
            Vector3 across = bounds.size.x >= bounds.size.z ? Vector3.forward : Vector3.right;
            return Matrix4x4.Rotate(Quaternion.AngleAxis(ForestKitSpec.LogPitchDegrees, across));
        }

        // An authored lower level has no numbers in the manifest. A wrong scale on one FBX object would
        // still be a prefab that grows or shrinks with distance, so each is held to the first level's size.
        static void RequireSameExtent(string prefabId, int lod, Bounds level, Bounds first)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                float allowed = Mathf.Max(0.1f * first.size[axis], 0.02f);
                if (Mathf.Abs(level.size[axis] - first.size[axis]) > allowed || Mathf.Abs(level.center[axis] - first.center[axis]) > allowed)
                    throw new InvalidOperationException(
                        $"{prefabId}: authored level {lod} spans {level.min} to {level.max}, the first level {first.min} to {first.max}.");
            }
        }

        static void DropEmptySubMeshes(ref ForestMeshTools.MeshData data, ref Material[] materials)
        {
            var kept = new List<int>();
            for (int i = 0; i < data.subMeshes.Length; i++)
                if (data.subMeshes[i].Length > 0) kept.Add(i);
            if (kept.Count == data.subMeshes.Length) return;

            var keptMaterials = new Material[kept.Count];
            for (int i = 0; i < kept.Count; i++) keptMaterials[i] = materials[kept[i]];
            materials = keptMaterials;
            data = ForestMeshTools.Extract(data, kept.ToArray());
        }

        static MeshRenderer AddRenderer(Transform parent, string name, Mesh mesh, Material[] materials, ShadowCastingMode shadows, string prefabId)
        {
            if (mesh == null || materials == null || mesh.subMeshCount != materials.Length)
                throw new InvalidOperationException($"{prefabId}/{name}: a level needs a mesh and one material per sub-mesh.");
            if (materials.Length > 4)
                throw new InvalidOperationException($"{prefabId}/{name} has {materials.Length} sub-meshes; the contract allows four.");

            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = shadows;
            // Nothing in the kit is lightmapped; with a probe bake it is lit from probes.
            renderer.receiveGI = ReceiveGI.LightProbes;
            return renderer;
        }

        // Which sub-mesh is which material: by the name of the imported slot, else by the exact triangle
        // count the manifest recorded, else not at all. Guessing would put bark on needles.
        static string[] MaterialNames(MeshFilter node, ForestMeshTools.MeshData data, ForestObjectEntry entry, ForestAssetEntry owner)
        {
            var renderer = node.GetComponent<MeshRenderer>();
            Material[] slots = renderer != null ? renderer.sharedMaterials : Array.Empty<Material>();

            var names = new string[data.subMeshes.Length];
            for (int i = 0; i < names.Length; i++)
            {
                string slot = i < slots.Length && slots[i] != null ? slots[i].name : null;
                if (slot != null && HasMaterial(owner, slot))
                {
                    names[i] = slot;
                    continue;
                }

                int triangles = data.subMeshes[i].Length / 3;
                string match = null;
                int matches = 0;
                foreach (ForestSubMeshEntry subMesh in entry.subMeshes)
                {
                    if (subMesh.triangles != triangles) continue;
                    match = subMesh.material;
                    matches++;
                }
                if (matches != 1)
                    throw new InvalidOperationException(
                        $"Sub-mesh {i} of '{node.name}' in {owner.id} has the material slot '{slot ?? "(none)"}', which the manifest does not list, "
                        + $"and its {triangles} triangles match {matches} sub-meshes of the manifest. The material cannot be decided.");
                Debug.LogWarning($"Forest kit: slot '{slot ?? "(none)"}' of '{node.name}' is not a manifest material; matched to {match} by its {triangles} triangles.");
                names[i] = match;
            }
            return names;
        }

        static bool HasMaterial(ForestAssetEntry owner, string name)
        {
            if (owner.materials == null) return false;
            foreach (ForestMaterialEntry material in owner.materials)
                if (material.name == name) return true;
            return false;
        }

        static MeshFilter FindNode(GameObject model, string nodeName, string modelId)
        {
            MeshFilter[] filters = model.GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter filter in filters)
                if (filter.name == nodeName && filter.sharedMesh != null) return filter;

            if (filters.Length == 1 && filters[0].sharedMesh != null)
            {
                Debug.LogWarning($"Forest kit: model {modelId} has no object named '{nodeName}'; its only mesh, '{filters[0].name}', is used.");
                return filters[0];
            }

            var names = new List<string>();
            foreach (MeshFilter filter in filters) names.Add(filter.name);
            throw new InvalidOperationException($"Model {modelId} has no object named '{nodeName}'. It holds: {string.Join(", ", names)}.");
        }

        // The manifest objects a prefab is derived from.
        static IEnumerable<string> SourceObjectNames(string prefabId)
        {
            ForestClass cls = ForestKitSpec.ClassOf(prefabId);
            if (cls != ForestClass.FernPatch && cls != ForestClass.GrassPatch)
            {
                yield return prefabId;
                yield break;
            }
            var seen = new List<string>();
            foreach (string objectName in ForestKitSpec.Patch(prefabId).clumps)
            {
                if (seen.Contains(objectName)) continue;
                seen.Add(objectName);
                yield return objectName;
            }
        }

        static ForestObjectEntry FindSourceObject(string prefabId, string objectName)
        {
            return objectName == prefabId ? ForestManifest.FindObjectForPrefab(prefabId) : ForestManifest.FindObject(objectName);
        }

        static List<ForestAssetEntry> SourceModels(string prefabId)
        {
            var owners = new List<ForestAssetEntry>();
            foreach (string objectName in SourceObjectNames(prefabId))
            {
                ForestObjectEntry entry = FindSourceObject(prefabId, objectName);
                ForestAssetEntry owner = entry != null ? ForestManifest.FindOwner(entry) : null;
                if (owner != null && !owners.Contains(owner)) owners.Add(owner);
            }
            return owners;
        }

        // Null when the prefab can be built; otherwise what is missing, in words for the log.
        static string MissingInput(string prefabId, out bool needsAttention)
        {
            needsAttention = false;
            foreach (string objectName in SourceObjectNames(prefabId))
            {
                ForestObjectEntry entry = FindSourceObject(prefabId, objectName);
                ForestAssetEntry owner = entry != null ? ForestManifest.FindOwner(entry) : null;
                if (owner == null)
                {
                    needsAttention = true;
                    return $"the manifest has no object '{objectName}'";
                }
                if (!ForestManifest.IsDownloaded(owner)) return $"model {owner.id} ({owner.milestone}) is not downloaded";
                if (!ForestManifest.IsMeasured(owner))
                {
                    needsAttention = true;
                    return $"model {owner.id} is downloaded but has not been measured against its FBX; run: {MeasureCommand(owner)}";
                }
                foreach (ForestSubMeshEntry subMesh in entry.subMeshes)
                {
                    if (ForestMaterials.Get(subMesh.material) != null) continue;
                    needsAttention = true;
                    return $"material {subMesh.material} has not been built; run Gamex/Forest/1 Import Assets";
                }
            }
            return null;
        }

        static string MeasureCommand(ForestAssetEntry owner)
        {
            string fbx = "<fbx>";
            foreach (ForestAssetFile file in owner.files)
                if (file.map == "mesh") fbx = ForestManifest.DiskPath(file.relPath);
            return $"python3 -I Tools/forest/fbx_objects.py \"{fbx}\" {ForestPaths.ManifestPath} {owner.id}";
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // string.GetHashCode is not guaranteed to be the same on every run; this one (FNV-1a) is.
        static int StableHash(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char character in text) hash = (hash ^ character) * 16777619;
                return (int)hash;
            }
        }
    }
}
