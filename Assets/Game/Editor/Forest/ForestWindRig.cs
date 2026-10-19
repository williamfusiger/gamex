using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace Gamex.EditorTools
{
    /// <summary>
    /// W1 I2 (spec 2.9, 2.10, 6.2): puts the wind and the foliage interaction into the generated scene. The controller on each
    /// preset object, the "Foliage" root with the interaction field and its density map, the player's pusher, and the thin-stem
    /// rule (a young fir of instance scale at most <see cref="ForestLayout.PassableYoungFirMaxScale"/> gets its trunk capsule
    /// disabled on the instance; the prefab is untouched, so the NavMesh bake of stage 9 and the player see no collider there).
    /// </summary>
    public static class ForestWindRig
    {
        public const string FoliageRootName = "Foliage";
        public const string PusherChildName = "FoliagePusher";
        public const string ComputePath = "Assets/Game/Shaders/FoliageInteraction.compute";
        public const string DensityPath = ForestPaths.DataFolder + "/FoliageDensity.asset";

        /// <summary>The density bake's cell, and the radius of each class's disc at instance scale 1 (spec 6.2).</summary>
        public const float DensityCell = 0.5f;
        public const float FernPatchRadius = 1.0f, FernClumpRadius = 0.4f, GrassPatchRadius = 0.5f;
        /// <summary>The lower crown of a young fir: its LOD1 foliage between these heights (prefab space).</summary>
        public const float LowerCrownBottom = 0.3f, LowerCrownTop = 2.0f;
        /// <summary>A disc is 1 inside and falls to 0 over its outer 30 %.</summary>
        public const float DiscFalloff = 0.30f;
        const float Margin = 5f;

        /// <summary>The wind of a preset on its preset object (called by ForestLighting.Build for each preset object).</summary>
        public static ForestWind AddController(GameObject presetObject, ForestLightingPresets.Preset preset)
        {
            if (presetObject == null) throw new ArgumentNullException(nameof(presetObject));
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            var wind = presetObject.GetComponent<ForestWind>();
            if (wind == null) wind = presetObject.AddComponent<ForestWind>();
            var serialized = new SerializedObject(wind);
            Set(serialized, "bearing", preset.windBearing);
            Set(serialized, "calm", preset.windCalm);
            Set(serialized, "gustPeak", preset.windGustPeak);
            Set(serialized, "gustWavelength", preset.windGustWavelength);
            Set(serialized, "gustSpeed", preset.windGustSpeed);
            Set(serialized, "gustSeed", ForestLightingPresets.WindSeed);
            // The shipped knobs (spec 7.2), from the preset's own record of the distances.
            ForestWindState shipped = preset.WindState();
            Set(serialized, "knobs.fadeEnd", shipped.fadeEnd);
            Set(serialized, "knobs.flutterEnd", shipped.flutterEnd);
            serialized.FindProperty("knobs.moonCascades").intValue = shipped.moonCascades;
            Set(serialized, "knobs.strengthScale", 1f);
            serialized.FindProperty("knobs.fieldEnabled").boolValue = true;
            serialized.FindProperty("knobs.gustTexture").boolValue = false;
            Set(serialized, "knobs.torchShadowEnd", shipped.torchShadowEnd);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return wind;
        }

        /// <summary>
        /// W1 I2b: the preset's forest cull distance on its preset object (ForestCullDistance; 0 = the kit's own), so it switches with the
        /// preset like the wind.
        /// </summary>
        public static ForestCullDistance AddCullDistance(GameObject presetObject, ForestLightingPresets.Preset preset)
        {
            if (presetObject == null) throw new ArgumentNullException(nameof(presetObject));
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            var cull = presetObject.GetComponent<ForestCullDistance>();
            if (cull == null) cull = presetObject.AddComponent<ForestCullDistance>();
            var serialized = new SerializedObject(cull);
            Set(serialized, "distance", preset.forestCullDistance);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return cull;
        }

        /// <summary>The root "Foliage" with the interaction field, its compute shader and the density map wired.</summary>
        public static GameObject BuildFoliage(ForestPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath)
                ?? throw new InvalidOperationException($"{ComputePath} did not import as a compute shader: foliage could not part around bodies.");
            FoliageDensityMap density = BakeDensity(plan);
            var root = new GameObject(FoliageRootName);
            var field = root.AddComponent<FoliageInteractionField>();
            var serialized = new SerializedObject(field);
            serialized.FindProperty("stepShader").objectReferenceValue = compute;
            serialized.FindProperty("densityMap").objectReferenceValue = density;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }

        /// <summary>The player's pusher: child "FoliagePusher" of the Player root, isPlayer, radius 0.35 (the body's).</summary>
        public static FoliagePusher AddPlayerPusher(GameObject player)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            Transform child = player.transform.Find(PusherChildName);
            if (child == null)
            {
                child = new GameObject(PusherChildName).transform;
                child.SetParent(player.transform, false);
            }
            var pusher = child.GetComponent<FoliagePusher>();
            if (pusher == null) pusher = child.gameObject.AddComponent<FoliagePusher>();
            var serialized = new SerializedObject(pusher);
            Set(serialized, "radius", 0.35f);
            serialized.FindProperty("isPlayer").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return pusher;
        }

        /// <summary>True when the placement is a passable young fir (scale at most the threshold).</summary>
        public static bool IsPassable(ForestPlacement placement) =>
            placement.cls == ForestClass.YoungFir && placement.scale <= ForestLayout.PassableYoungFirMaxScale;

        /// <summary>
        /// Disables the young fir's trunk capsule on the instance when its scale is at most PassableYoungFirMaxScale (0.705); the
        /// prefab is untouched. Returns whether it did.
        /// </summary>
        public static bool ApplyPassableStem(GameObject instance, ForestPlacement placement)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (!IsPassable(placement)) return false;
            var capsule = instance.GetComponent<CapsuleCollider>()
                ?? throw new InvalidOperationException($"{instance.name} is a young fir without a trunk capsule on its root.");
            capsule.enabled = false;
            if (PrefabUtility.IsPartOfPrefabInstance(capsule))
                PrefabUtility.RecordPrefabInstancePropertyModifications(capsule);
            return true;
        }

        /// <summary>
        /// The CPU density map of spec 2.10 and 6.2, written to ForestData/FoliageDensity.asset (updated in place, so its GUID stays):
        /// low layer = fern patch discs 1.0 m x scale, fern clumps 0.4 m x scale, grass patches 0.5 m x scale; mid layer = young firs,
        /// a disc of the lower crown's radius x scale. 1 inside, falling linearly to 0 over the outer 30 % of each disc, combined by
        /// maximum.
        /// </summary>
        public static FoliageDensityMap BakeDensity(ForestPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            // The whole plan, the backdrop beyond the boundary included (it reaches about 85 x 95 m), plus a margin for the discs.
            var min = new Vector2(-ForestLayout.HalfExtentX, -ForestLayout.HalfExtentZ);
            var max = new Vector2(ForestLayout.HalfExtentX, ForestLayout.HalfExtentZ);
            foreach (ForestPlacement placement in plan.placements)
            {
                min = Vector2.Min(min, new Vector2(placement.position.x, placement.position.z));
                max = Vector2.Max(max, new Vector2(placement.position.x, placement.position.z));
            }
            var origin = new Vector2(Mathf.Floor(min.x - Margin), Mathf.Floor(min.y - Margin));
            int width = Mathf.CeilToInt((max.x + Margin - origin.x) / DensityCell);
            int height = Mathf.CeilToInt((max.y + Margin - origin.y) / DensityCell);
            var low = new float[width * height];
            var mid = new float[width * height];

            var crowns = new Dictionary<string, float>();
            int discs = 0;
            foreach (ForestPlacement placement in plan.placements)
            {
                float radius;
                float[] layer;
                switch (placement.cls)
                {
                    case ForestClass.FernPatch: radius = FernPatchRadius; layer = low; break;
                    case ForestClass.FernClump: radius = FernClumpRadius; layer = low; break;
                    case ForestClass.GrassPatch: radius = GrassPatchRadius; layer = low; break;
                    case ForestClass.YoungFir:
                        if (!crowns.TryGetValue(placement.prefabId, out radius))
                            crowns.Add(placement.prefabId, radius = LowerCrownRadius(placement.prefabId));
                        layer = mid;
                        break;
                    default: continue;
                }
                if (radius <= 0f) continue;
                Stamp(layer, width, height, origin, new Vector2(placement.position.x, placement.position.z), radius * placement.scale);
                discs++;
            }

            var map = AssetDatabase.LoadAssetAtPath<FoliageDensityMap>(DensityPath);
            bool created = map == null;
            if (created) map = ScriptableObject.CreateInstance<FoliageDensityMap>();
            map.origin = origin;
            map.cellSize = DensityCell;
            map.width = width;
            map.height = height;
            map.low = Quantise(low);
            map.mid = Quantise(mid);
            if (created)
            {
                ForestTerrain.EnsureAssetFolder(ForestPaths.DataFolder);
                AssetDatabase.CreateAsset(map, DensityPath);
            }
            else
                EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssetIfDirty(map);

            var radii = new List<string>();
            foreach (KeyValuePair<string, float> crown in crowns)
                radii.Add($"{crown.Key} {crown.Value.ToString("0.00", CultureInfo.InvariantCulture)} m");
            radii.Sort(StringComparer.Ordinal);
            Debug.Log($"ForestWindRig: density map {width} x {height} cells of {DensityCell} m from {discs} discs; lower-crown radii at scale 1: {string.Join(", ", radii)}.");
            return map;
        }

        /// <summary>The largest horizontal reach of a young fir's LOD1 branches (G above 0) between 0.3 and 2.0 m, prefab space.</summary>
        public static float LowerCrownRadius(string prefabId)
        {
            Mesh mesh = LevelMesh(prefabId, "LOD1");
            if (mesh == null) return 0f;
            Vector3[] positions = mesh.vertices;
            Color32[] colours = mesh.colors32;
            if (colours == null || colours.Length != positions.Length) return 0f;
            float reach = 0f;
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 p = positions[i];
                if (colours[i].g == 0 || p.y < LowerCrownBottom || p.y > LowerCrownTop) continue;
                reach = Mathf.Max(reach, new Vector2(p.x, p.z).magnitude);
            }
            return reach;
        }

        /// <summary>
        /// The stem's radius at <paramref name="heightAboveBase"/> (prefab space, scale 1) of a young fir's scan level: the median
        /// horizontal distance of its trunk vertices (wind colour G = 0, the wood) within 5 cm of that height from their centroid.
        /// </summary>
        public static float StemRadius(string prefabId, float heightAboveBase)
        {
            Mesh mesh = LevelMesh(prefabId, "LOD0");
            if (mesh == null) return 0f;
            Vector3[] positions = mesh.vertices;
            Color32[] colours = mesh.colors32;
            if (colours == null || colours.Length != positions.Length) return 0f;
            var ring = new List<Vector2>();
            for (int i = 0; i < positions.Length; i++)
                if (colours[i].g == 0 && colours[i].r > 0 && Mathf.Abs(positions[i].y - heightAboveBase) <= 0.05f)
                    ring.Add(new Vector2(positions[i].x, positions[i].z));
            if (ring.Count < 3) return 0f;
            Vector2 centre = Vector2.zero;
            foreach (Vector2 p in ring) centre += p;
            centre /= ring.Count;
            var distances = new List<float>(ring.Count);
            foreach (Vector2 p in ring) distances.Add((p - centre).magnitude);
            distances.Sort();
            return distances[distances.Count / 2];
        }

        static Mesh LevelMesh(string prefabId, string child)
        {
            GameObject prefab = ForestPrefabs.Load(prefabId);
            Transform level = prefab != null ? prefab.transform.Find(child) : null;
            var filter = level != null ? level.GetComponent<MeshFilter>() : null;
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            return mesh != null && mesh.isReadable ? mesh : null;
        }

        static void Stamp(float[] layer, int width, int height, Vector2 origin, Vector2 centre, float radius)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt((centre.x - radius - origin.x) / DensityCell));
            int x1 = Mathf.Min(width - 1, Mathf.FloorToInt((centre.x + radius - origin.x) / DensityCell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((centre.y - radius - origin.y) / DensityCell));
            int z1 = Mathf.Min(height - 1, Mathf.FloorToInt((centre.y + radius - origin.y) / DensityCell));
            float inner = radius * (1f - DiscFalloff);
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var cell = new Vector2(origin.x + (x + 0.5f) * DensityCell, origin.y + (z + 0.5f) * DensityCell);
                    float d = (cell - centre).magnitude;
                    float value = d <= inner ? 1f : Mathf.Clamp01((radius - d) / (radius - inner));
                    int index = z * width + x;
                    if (value > layer[index]) layer[index] = value;
                }
            }
        }

        static byte[] Quantise(float[] values)
        {
            var bytes = new byte[values.Length];
            for (int i = 0; i < values.Length; i++)
                bytes[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(values[i] * 255f), 0, 255);
            return bytes;
        }

        static void Set(SerializedObject serialized, string property, float value)
        {
            SerializedProperty found = serialized.FindProperty(property)
                ?? throw new ArgumentException($"{serialized.targetObject.GetType().Name} has no serialized field '{property}'.");
            found.floatValue = value;
        }
    }
}
