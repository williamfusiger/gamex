using System;
using System.IO;
using UnityEngine;

namespace Gamex.EditorTools
{
    /// <summary>Every path the forest build reads or writes. Asset paths are project-relative.</summary>
    public static class ForestPaths
    {
        public const string ThirdPartyRoot = "Assets/ThirdParty";
        public const string PolyHavenRoot = ThirdPartyRoot + "/PolyHaven";
        public const string ManifestPath = "Assets/Game/Editor/Forest/forest-asset-manifest.json";

        public const string ScenePath = "Assets/Game/Scenes/Forest.unity";
        public const string KitFolder = "Assets/Game/Scenes/ForestKit";
        public const string KitTextures = KitFolder + "/Textures";
        public const string KitMaterials = KitFolder + "/Materials";
        public const string KitTerrainLayers = KitFolder + "/TerrainLayers";
        public const string KitMeshes = KitFolder + "/Meshes";
        public const string KitPrefabs = KitFolder + "/Prefabs";
        public const string KitReportPath = KitFolder + "/kit-report.json";

        public const string DataFolder = "Assets/Game/Scenes/ForestData";
        public const string LightingFolder = DataFolder + "/Lighting";
        public const string BakeFolder = DataFolder + "/Bake";               // M5 only
        public const string TerrainDataPath = DataFolder + "/Terrain.asset";
        public const string NavMeshPath = DataFolder + "/NavMesh.asset";
        public const string SurfaceMapPath = DataFolder + "/SurfaceMap.png";
        public const string PlanPath = DataFolder + "/placements.json";
        public const string ReportPath = DataFolder + "/build-report.json";

        /// <summary>Unity may write bake output here. The builder never writes or deletes in it.</summary>
        public const string ReservedSceneFolder = "Assets/Game/Scenes/Forest";

        public const string StampsPath = "Library/GamexForest/stamps.json";
        public const string DefaultCaptureFolder = "Temp/Captures/forest";   // used when -captureOut is absent (menu runs)
        public const string FoliageProfilePath =
            "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipelineResources/FoliageDiffusionProfile.asset";
    }

    /// <summary>
    /// What an object is. Landmark is only the planner's random stream for hand-listed pieces:
    /// a placement's cls is never Landmark. RockFace is reserved (no prefab in version 1). Numbers are frozen.
    /// </summary>
    public enum ForestClass
    {
        Landmark = 0, TallFir = 1, YoungFir = 2, Boulder = 3, RockFace = 4, Log = 5,
        Stump = 6, Branch = 7, FernPatch = 8, FernClump = 9, GrassPatch = 10,
    }

    /// <summary>Terrain layer index = surface id. Frozen: gameplay will read it.</summary>
    public enum ForestSurface
    {
        Litter = 0, Humus = 1, MossyGround = 2, Gravel = 3, StonySoil = 4, Mud = 5, Rock = 6, DryNeedles = 7,
    }

    [Serializable]
    public struct ForestPlacement
    {
        public string prefabId;      // key for ForestPrefabs.Load
        public ForestClass cls;
        public Vector3 position;     // world. x, z from ForestScatter.Plan; y from ForestScatter.Ground (ground height - sink)
        public Quaternion rotation;  // world
        public float scale;          // uniform
        public float sink;           // metres the pivot sits below the ground; negative = raised
        public float clearance;      // metres, already scaled: horizontal radius of the collider's bounding circle; 0 = no collider
        public int tint;             // 0 or 1
        public int cellX, cellZ;     // 0..7
        public string landmark;      // "" for scattered instances
    }

    [Serializable]
    public sealed class ForestPlan
    {
        public int seed;
        public bool grounded;        // false until ForestScatter.Ground has filled every position.y
        public ForestPlacement[] placements = Array.Empty<ForestPlacement>();
    }

    /// <summary>Prefab ids. A tree id is the manifest object name without "_LOD0".</summary>
    public static class ForestPrefabIds
    {
        public static readonly string[] TallFirs = { "fir_tree_01_a", "fir_tree_01_b", "fir_tree_01_c" };
        public static readonly string[] YoungFirs =
            { "fir_sapling_medium_a", "fir_sapling_medium_b", "fir_sapling_medium_c" };
        public static readonly string[] Boulders =
        {
            "rock_moss_set_01_rock01", "rock_moss_set_01_rock02", "rock_moss_set_01_rock03",
            "rock_moss_set_01_rock04", "rock_moss_set_01_rock05", "rock_moss_set_01_rock06",
            "rock_moss_set_02_rock07", "rock_moss_set_02_rock08", "rock_moss_set_02_rock09",
            "rock_moss_set_02_rock10", "rock_moss_set_02_rock11", "rock_moss_set_02_rock12",
            "rock_moss_set_02_rock13",
        };
        public static readonly string[] Logs = { "dead_tree_trunk_02" };
        public static readonly string[] Stumps = { "tree_stump_01", "tree_stump_02" };
        public static readonly string[] Branches =
            { "dry_branches_medium_01_a", "dry_branches_medium_01_b", "dry_branches_medium_01_c" };
        public static readonly string[] FernPatches = { "fern_patch_a", "fern_patch_b", "fern_patch_c" };
        public static readonly string[] FernClumps = { "fern_02_a", "fern_02_b", "fern_02_c", "fern_02_d" };
        public static readonly string[] GrassPatches = { "grass_patch_a", "grass_patch_b" };
    }

    /// <summary>One LOD level of one prefab, as built. Written by WP2, read by WP5.</summary>
    [Serializable]
    public sealed class ForestKitLodReport
    {
        public int triangles, vertices;
        public float untilDistance;          // metres
        public bool isScan;                  // a tree level that is the untouched scan (experiment E1)
        public int pieces;                   // trees: foliage pieces kept
        public float keepRatio, pieceScale;  // trees: share of twig triangles kept; scale applied to the kept pieces
        public float coverage;               // trees: silhouette coverage against the source at this level's start distance
    }

    [Serializable]
    public sealed class ForestKitPrefabReport
    {
        public string prefabId;
        public Vector3 boundsMin, boundsMax;     // LOD0 as measured, Unity space
        public string mirror;                    // "x" | "z" | "xz" | "none" | "symmetric", against the manifest's source-space bounds
        public float colliderRadius;             // horizontal bounding radius of the collider at scale 1; 0 = no collider
        public int sourcePieces;                 // trees
        public float medianPieceTriangles;       // trees
        public bool usedSpatialFallback;         // trees
        public string[] triplanarMaterials = Array.Empty<string>();   // materials replaced by their noUvFallback variant
        public float bakeSeconds;
        public ForestKitLodReport[] lods = Array.Empty<ForestKitLodReport>();
    }

    [Serializable]
    public sealed class ForestKitReport
    {
        public string unityVersion, kitSpecHash;
        public ForestKitPrefabReport[] prefabs = Array.Empty<ForestKitPrefabReport>();

        public ForestKitPrefabReport Find(string prefabId)
        {
            foreach (ForestKitPrefabReport prefab in prefabs)
                if (prefab.prefabId == prefabId) return prefab;
            return null;
        }

        /// <summary>Null when the kit has not been built.</summary>
        public static ForestKitReport Load()
        {
            return File.Exists(ForestPaths.KitReportPath)
                ? JsonUtility.FromJson<ForestKitReport>(File.ReadAllText(ForestPaths.KitReportPath))
                : null;
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ForestPaths.KitReportPath));
            File.WriteAllText(ForestPaths.KitReportPath, JsonUtility.ToJson(this, true));
        }
    }

    /// <summary>Command-line arguments, read the way SceneCapture does.</summary>
    public static class ForestArgs
    {
        public static string Value(string name, string fallback = null)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : fallback;
        }

        public static bool Flag(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;
    }
}
