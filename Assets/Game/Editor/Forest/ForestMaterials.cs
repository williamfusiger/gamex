using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Gamex.EditorTools
{
    /// <summary>
    /// Creates every material the forest uses, in code (spec 5.5 and 6.3): HDRP/Lit materials for the
    /// scanned models, the terrain layers and the terrain material. Assets are created once and
    /// updated in place afterwards, so their GUIDs, and every prefab that points at them, stay valid.
    ///
    /// Three rules from the HDRP source shape the code: keywords are never enabled by hand
    /// (ValidateMaterial clears and rebuilds them from property values); textures are assigned before
    /// validating (the normal and mask keywords come from the slots being filled); and a material is
    /// saved before it gets a diffusion profile (the profile reference is a sub-asset of the material).
    ///
    /// W1: every material of a model with a wind profile (young and tall firs, fern, grass) is built on the wind Shader Graph
    /// (Gamex/ForestWind, or Gamex/ForestWindTriplanar for the triplanar bark) with HDRP/Lit's property names, and gets an
    /// "__mv" twin whose MotionVectors pass is on (the levels that are pushed, ForestKitSpec.MotionVectorLevels). The switch
    /// WindShaderDefault (or -forestWindShader on|off, or WindShaderOverride) rebuilds the same assets on HDRP/Lit again.
    /// </summary>
    public static class ForestMaterials
    {
        // Part of every stamp: raise it when a recipe changes, so every material is rebuilt.
        // materials-3 (W1): the wind recipe family on the wind Shader Graphs, the __mv twins, the shader mode in every stamp.
        const string ToolVersion = "materials-3";

        const string LitShaderName = "HDRP/Lit";
        const string MotionVectorSuffix = "__mv";
        const string MotionVectorPass = "MotionVectors";

        /// <summary>THE switch (W1): false builds every material on HDRP/Lit again, same assets and GUIDs.</summary>
        public const bool WindShaderDefault = true;

        /// <summary>A/B only. Null = WindShaderDefault, or the command line's -forestWindShader on|off.</summary>
        public static bool? WindShaderOverride;

        /// <summary>Whether the materials of moving models are built on the wind Shader Graphs.</summary>
        public static bool WindShaderActive
        {
            get
            {
                if (WindShaderOverride.HasValue) return WindShaderOverride.Value;
                string mode = ForestArgs.Value("-forestWindShader");
                if (mode == "on") return true;
                if (mode == "off") return false;
                if (!string.IsNullOrEmpty(mode))
                    throw new ArgumentException($"-forestWindShader must be on or off, not '{mode}'.");
                return WindShaderDefault;
            }
        }
        const string TerrainMaterialPath = ForestPaths.KitMaterials + "/ForestTerrain.mat";
        const string ImportHint =
            "Run Gamex/Forest/1 Import Assets: it packs the textures, refreshes the asset database and then builds the materials.";

        const float MaterialIdStandard = 1f;      // HDRP MaterialId.LitStandard
        const float MaterialIdTranslucent = 5f;   // HDRP MaterialId.LitTranslucent: transmission without the SSS blur pass
        const float UvBaseUv0 = 0f;
        const float UvBaseTriplanar = 5f;
        const float FoliageThickness = 0.5f;
        const float CrownCoreDarkening = 0.6f;
        const float CrownCoreSmoothness = 0.05f;

        // Height blend between terrain layers. If layers blend wrongly (risk 4), set this to false.
        const bool TerrainHeightBlend = true;
        // How far below the highest layer a layer may lie and still show. The spec's 0.04 m is as wide as a
        // layer's whole height range, so the blend never picked a winner: where two layers met (the edge of a
        // rut, every mixed patch) they were cross-faded, stones half transparent with needles drawn through
        // them (review 1 of the M2 captures, ground_close). At 0.015 m the higher of two layers cuts through.
        // Review 1 of the M4 captures: at 0.015 moss and litter patches had straight, world-aligned edges and rectangular blocks
        // (the layers' tiling height maps decide the boundary). Tried at 0.025 (build/issues/M4-review1.md).
        const float TerrainHeightTransition = 0.025f;
        // The height remap is identical on all layers on purpose: the blend compares each layer's height
        // with the maximum over all layers, so a layer with a larger amplitude would suppress the others.
        const float TerrainHeightAmplitude = 0.04f;   // metres either side of the layer's mean height
        // Part of the terrain layer stamps: each layer's height range is centred on its own mean (see MeanHeight).
        const string TerrainHeightCentring = "height-centred-on-mean-1";
        const int MaxTerrainLayers = 8;
        const int FallbackTerrainLayers = 4;

        /// <summary>Second, darker variant of a foliage material, used on some instances to hide repetition.</summary>
        /// <remarks>
        /// Review 1 of the M3 captures: at 0.88 the young firs' second tint could not be seen; all 771 firs had one cool
        /// green. It is now darker and a little olive (spec 5.5 tunable, recorded in build/issues/M3-review1.md).
        /// </remarks>
        static readonly Dictionary<string, Color> TintVariants = new()
        {
            { "fern_02", new Color(0.85f, 0.85f, 0.85f) },
            { "fir_sapling_medium_twigs", new Color(0.82f, 0.79f, 0.64f) },
            { "fir_tree_01_twig", new Color(0.88f, 0.88f, 0.88f) },
        };

        /// <summary>
        /// _SmoothnessRemapMax per foliage material (HDRP's default 1). Review 1 of the M3 captures: needles nearer than
        /// about 0.5 m read as glossy plastic straps with a bright sheen along their edges; the needle region of the
        /// twig atlas carries a smoothness of 0.48 on average.
        /// </summary>
        /// <remarks>
        /// Review 1 of the M4 captures: at the hide spots (crouch_hide, hollow_rim) the nearest young firs show their first thinned
        /// level, whose flattened needle quads carry one normal each, and at 0.4 every quad caught the same waxy sheen (straps of
        /// yew or bamboo). 0.28 for both twig materials; the scan uses the same material and is duller by as much.
        /// Review 2 of the M4 captures (technical and composition lenses): at hide distance the needles still showed a glossy midline
        /// under the diffuse sky. 0.15.
        /// </remarks>
        static readonly Dictionary<string, float> SmoothnessRemapMax = new()
        {
            { "fir_sapling_medium_twigs", 0.15f },
            // M4: the tall firs' twig atlas carries the same arm map (same md5), so the same smoothness.
            { "fir_tree_01_twig", 0.15f },
        };

        /// <summary>
        /// Base colour multiplier per material (spec 5.5), for scans that do not match the rest of the kit.
        ///
        /// The spec took the fallen trunk for sun-bleached and gave it 0.6. Measured under its UVs its mean
        /// albedo is 0.105 (linear luminance) against 0.093 and 0.088 for the two stumps: it is not bleached,
        /// at 0.6 it rendered as a black silhouette, and at the 0.85 of M2 it was still the darkest thing in
        /// the clearing (median luminance 27 of 255 beside litter at 64, review 1). It now keeps the scan's
        /// own colour, so it is not listed.
        ///
        /// The six rocks of set 01 are ochre (mean linear albedo 0.124, 0.076, 0.030) where the seven of set 02
        /// are grey-olive (0.087, 0.077, 0.049); side by side they read as two kinds of stone. The tint takes
        /// red out of set 01 and leaves its blue alone, which moves it toward set 02 without making it darker
        /// than the scan in the channels that carry its lichen.
        ///
        /// The grass scan is dark and yellow-olive (0.104, 0.103, 0.029, about the luminance of the litter
        /// it stands on), so a tuft was a dark wiry thing on dark ground. It is lifted toward a living green.
        ///
        /// The values are factors on linear albedo. _BaseColor is a colour property, which Unity converts
        /// from sRGB before the shader sees it, so SurfaceRecipe stores the sRGB equivalent.
        /// </summary>
        static readonly Dictionary<string, Color> BaseColourTint = new()
        {
            // Round 1 cut set 01's red to 0.80; with its AO left at 1 (see UnusableAo) its broken faces then read
            // as cold, dark planes beside the warm litter (review 2). Nearer neutral now that set 02 is lifted by
            // its AO remap below and the two sets meet half way.
            { "rock_moss_set_01", new Color(0.92f, 0.96f, 1.0f) },
            { "grass_medium_01", new Color(1.15f, 1.40f, 1.0f) },
            // The scan's darkest tenth is about 9 of 255 and its mean albedo 0.073, darker than every soil layer:
            // dry branches read as burnt black sticks on open ground (review 2). Dead wood that has lain a year is
            // grey-brown, not darker than the litter.
            { "dry_branches_medium_01", new Color(1.25f, 1.25f, 1.25f) },
            // Review 1 of the M4 captures: from 9 to 110 m the tall firs' crowns read as a light yellow-green broadleaf wood. The
            // needle region of this atlas is a saturated yellow-green (linear 0.104, 0.141, 0.042: green 1.36 times red, blue 0.30
            // times green; build/work/M4r1/scripts/atlasmean.swift) and 9 % lighter than the young firs'. The scan's crown looks
            // dark olive-grey only because its hair-thin needles mix with what is behind them (kit sheets at 25 and 45 m: green 1.03
            // times red in the scan's column, 1.21 to 1.29 in every thinned level). Every tall-fir level is thinned (no scan level,
            // spec 5.6.4), so the whole material is taken darker and toward grey-green: green most, red less, blue least.
            { "fir_tree_01_twig", new Color(0.78f, 0.68f, 0.82f) },
            // Review 1 of the M4 captures: ferns read bright lime, pasted onto the muted floor and the darkened firs. The atlas is
            // a saturated yellow-green (linear 0.070, 0.104, 0.017: green 1.48 times red), lit again from behind through the
            // foliage profile. A quarter darker, green most, toward olive.
            { "fern_02", new Color(0.80f, 0.68f, 0.90f) },
        };

        /// <summary>
        /// _AORemapMin per opaque material (spec 5.5 leaves it at 0). HDRP multiplies the sky light, four-fifths
        /// of the light here, by the mask's AO. The scans' AO is strong (set 02 averages 0.73 with a tenth of its
        /// texels near 0; the stumps 0.70 to 0.76), so with every other occlusion term on top (SSAO, micro
        /// shadows, a sky-only ambient with no bounce from the ground) whole faces of rocks, stumps and branches
        /// fell to 30 to 45 % of the ground beside them (review 2 of the M2 captures). Remapped, the scan's AO
        /// still shades crevices and contacts without multiplying whole faces toward black.
        /// </summary>
        static readonly Dictionary<string, float> AoRemapMin = new()
        {
            { "rock_moss_set_01", 0.4f }, { "rock_moss_set_02", 0.4f },
            { "tree_stump_01", 0.5f }, { "tree_stump_02", 0.5f },
            { "dead_tree_trunk_02", 0.5f }, { "dry_branches_medium_01", 0.5f },
        };

        /// <summary>
        /// Terrain layers tinted with diffuseRemapMax (spec 6.3, "dark floor"). The spec tints "the two
        /// brightest layers" to 0.8 and names layers 2 and 7. Measured (issues/M1-ground.md), the brightest are
        /// gravel (0.23) and dry needles (0.24), then mossy ground (0.19) and litter (0.14, and strongly orange:
        /// red 0.226 against blue 0.047), while the props that stand on them have albedos of 0.05 to 0.10. The
        /// M2 captures showed the result: a bright, dry, orange moor with near-black rocks and logs on it. The
        /// tints below bring every layer the player walks on to about 0.10 to 0.16, cool the litter, and push
        /// the mossy layer (a leaf scan with little green in it) toward the yellow-green of the art direction.
        /// </summary>
        static readonly Dictionary<string, Color> LayerTints = new()
        {
            { "forest_leaves_04", new Color(0.74f, 0.78f, 0.82f) },    // litter: 0.137 to about 0.105
            // mossy ground: the scan is leaf litter (linear 0.298, 0.182, 0.059). Round 1's (0.74, 0.84, 0.62) left
            // red above green, an ochre heath accent (review 2). The reviewer's (0.42, 0.72, 0.40) made fresh green
            // salad leaves (rendered in build/captures/m2-r2/iterations/it01full). This gives about (0.113, 0.116,
            // 0.039): green level with red, a quarter darker than round 1 and less saturated, a damp olive.
            { "forest_leaves_02", new Color(0.38f, 0.64f, 0.66f) },
            // gravel: 0.23 to about 0.15. Wet stone is darker and glossier than dry (review 2: "dry and chalky").
            { "rocky_trail", new Color(0.64f, 0.64f, 0.64f) },
            { "forrest_ground_03", new Color(0.72f, 0.72f, 0.72f) },   // dry needles: 0.24 to 0.17, the palest bed
            { "mossy_rock", new Color(0.86f, 0.82f, 0.72f) },          // painted rock: cyan-grey toward the warm ground
            // Review 1 of the M4 captures: the hollow's mud read as a flat grey disc of asphalt with a sky sheen (all three lenses).
            // Darker and warmer, a wet brown.
            { "mud_forest", new Color(0.72f, 0.64f, 0.56f) },
            // Review 2 of the M4 captures: the floor under the groves and thickets, which is mostly humus, was as bright as the open
            // litter (the probe bake that would shade it is M5). The humus is darker and a little cooler: damp soil.
            { "forest_ground_06", new Color(0.80f, 0.82f, 0.86f) },
        };

        /// <summary>
        /// Terrain layers whose smoothness starts above zero, so they read wet (maskMapRemapMin.w). The spec
        /// lifts only the mud. A forest floor under mist is damp everywhere: with every other layer fully
        /// matte the ground looked like dry heath. Small lifts, so the sky shows on the floor at a low angle.
        /// </summary>
        static readonly Dictionary<string, float> LayerMinSmoothness = new()
        {
            // The mud's own mask averages 0.08, so at 0.30 it landed near 0.36: a broad dull sheen that lifted its
            // dark albedo to grey and nothing read as wet (review 2, hollow_rim). At 0.55 the sky shows in it at a
            // low angle. The gravel and humus get a smaller lift for the same reason.
            // Review 1 of the M4 captures: at 0.55 the grey sky over the mud's grey albedo made the hollow an asphalt disc; 0.40 with
            // the darker, warmer tint above.
            // Review 2 (technical, composition): still a grey-blue disc; 0.30.
            { "mud_forest", 0.30f }, { "forest_ground_06", 0.26f }, { "forest_leaves_02", 0.16f },
            { "forest_leaves_04", 0.12f }, { "forest_ground_04", 0.12f }, { "rocky_trail", 0.25f },
        };

        /// <summary>
        /// Normal map strength per terrain layer (spec 6.3 gives 1 for all). Under an overcast sky the layers
        /// with real relief (stones, twigs) looked like a printed photograph at crouch height.
        /// </summary>
        static readonly Dictionary<string, float> LayerNormalScale = new()
        {
            { "rocky_trail", 1.4f }, { "forest_leaves_04", 1.3f }, { "forest_ground_04", 1.3f },
        };

        enum Surface { Opaque, Foliage, Triplanar, CrownCore }

        sealed class Recipe
        {
            public string assetPath, stamp;
            public Surface surface;
            // W1: on the wind graph (else HDRP/Lit), its profile, and whether this is the __mv twin (MotionVectors pass on).
            public bool wind, motionVectors;
            public ForestWindProfiles.Profile profile;
            public Texture2D baseColour, normal, mask;
            public Color colour = Color.white;
            public float cutoff = 0.5f, smoothness = 0.5f, aoRemapMin, smoothnessRemapMax = 1f;
            // Triplanar only: tiles per metre and the base map's offset (a ground variant lines up with its terrain layer).
            public float worldScale = 1f, smoothnessRemapMin, normalScale = 1f;
            public Vector2 uvOffset;
        }

        /// <summary>
        /// Terrain layers that are also built as an opaque HDRP/Lit material projected from above, "&lt;layer&gt;__ground": the
        /// layer's own maps, tint, wetness and normal strength, at its tile and lined up with the terrain's own mapping, so a mesh
        /// that lies on the floor reads as the floor. Review 1 of the M4 captures: the litter skirt of fir_tree_01_c's root flare
        /// (ForestKitSpec.SkirtGroundLayer).
        /// </summary>
        static readonly string[] GroundVariantLayers = { "forest_ground_06" };

        /// <summary>
        /// Builds the materials, terrain layers and terrain material of every downloaded asset. The
        /// packed textures must exist and be imported (ForestImport.Run does both first). A product whose
        /// inputs are unchanged is left alone unless <paramref name="force"/> is set.
        /// </summary>
        public static void BuildAll(bool force)
        {
            EnsureFolder(ForestPaths.KitMaterials);
            EnsureFolder(ForestPaths.KitTerrainLayers);

            Shader lit = FindLitShader();
            bool windShader = WindShaderActive;
            if (windShader)
            {
                ForestWindShaderGraphs.EnsureGraphs();
                // Without a pipeline instance a Shader Graph material does not validate (ForestWindShaderGraphs.EnsureRenderPipeline).
                ForestWindShaderGraphs.EnsureRenderPipeline();
            }
            DiffusionProfileSettings profile = AssetDatabase.LoadAssetAtPath<DiffusionProfileSettings>(ForestPaths.FoliageProfilePath);
            if (profile == null)
                Debug.LogWarning($"Forest materials: the foliage diffusion profile is missing at {ForestPaths.FoliageProfilePath}. " +
                                 "Foliage is built as Standard, without translucency.");
            HashSet<string> triplanarSources = NoUvFallbackTargets();

            // Stamps are recorded only after SaveAssets. Until then the changes exist in memory alone, and
            // a run that stops half-way must find these assets "not current" the next time.
            var pending = new List<(string assetPath, string stamp)>();
            int built = 0, kept = 0, layers = 0;
            void Lit(Recipe recipe) => Count(BuildLit(lit, profile, recipe, force, pending, out _), ref built, ref kept);
            // A moving model's material and its __mv twin (W1).
            void Both(Func<bool, Recipe> recipe)
            {
                Recipe plain = recipe(false);
                Lit(plain);
                if (plain.profile != null) Lit(recipe(true));
            }

            foreach (ForestAssetEntry entry in ForestManifest.All)
            {
                if (!ForestManifest.IsDownloaded(entry)) continue;
                if (entry.kind == "texture")
                {
                    Count(BuildTerrainLayer(entry, force, pending), ref built, ref kept);
                    if (Array.IndexOf(GroundVariantLayers, entry.id) >= 0)
                        Lit(GroundRecipe(entry));
                    layers++;
                    continue;
                }
                if (entry.kind != "model") continue;

                foreach (ForestMaterialEntry material in entry.materials)
                {
                    if (material.sameAs != "") continue;   // Get() resolves it to the other material's asset
                    Both(mv => SurfaceRecipe(entry, material, 0, profile != null, mv));
                    if (TintVariants.ContainsKey(material.name))
                        Both(mv => SurfaceRecipe(entry, material, 1, profile != null, mv));
                    if (triplanarSources.Contains(material.name))
                        Both(mv => TriplanarRecipe(entry, material, mv));

                    // A crown core is first made when the tree baker asks for one (GetCrownCore). Once it
                    // exists it is kept in step with the atlas here.
                    bool hasCrownCore = AssetDatabase.LoadAssetAtPath<Material>(CrownCorePath(material.name, false)) != null;
                    if (material.surface == "foliage" && hasCrownCore)
                        Both(mv => CrownCoreRecipe(entry, material, mv));
                }
            }
            if (layers > 0)
                Count(BuildTerrainMaterial(force, pending), ref built, ref kept);

            AssetDatabase.SaveAssets();
            Commit(pending);
            Debug.Log($"Forest materials: {built} assets built, {kept} already up to date; {GetTerrainLayers().Length} terrain layers usable; " +
                      $"moving models on {(windShader ? "the wind Shader Graphs" : LitShaderName)}.");
        }

        /// <summary>
        /// The material asset for an FBX material name; sameAs is followed. Tint 1 exists only for the
        /// fern and the two twig materials. Null when the asset has not been built.
        /// </summary>
        public static Material Get(string materialName, int tint = 0) => Get(materialName, tint, false);

        /// <summary>
        /// As Get(materialName, tint), or its "__mv" twin (MotionVectors pass on, W1) when <paramref name="motionVectors"/> is set.
        /// A static material has no twin: null.
        /// </summary>
        public static Material Get(string materialName, int tint, bool motionVectors)
        {
            ForestMaterialEntry material = ForestManifest.ResolveMaterial(materialName);
            if (material == null || tint < 0) return null;
            return AssetDatabase.LoadAssetAtPath<Material>(SurfacePath(material.name, tint, motionVectors));
        }

        /// <summary>
        /// The triplanar variant "&lt;name&gt;__triplanar", which exists only for a material that another
        /// one names as its noUvFallback. Null when not built.
        /// </summary>
        public static Material GetTriplanar(string materialName) => GetTriplanar(materialName, false);

        /// <summary>As GetTriplanar(materialName), or its "__mv" twin (W1).</summary>
        public static Material GetTriplanar(string materialName, bool motionVectors)
        {
            ForestMaterialEntry material = ForestManifest.ResolveMaterial(materialName);
            return material == null ? null : AssetDatabase.LoadAssetAtPath<Material>(TriplanarPath(material.name, motionVectors));
        }

        /// <summary>The "__mv" twin of a kit material (W1), the material itself when it is a twin, or null for a static material.</summary>
        public static Material MotionVectorTwin(Material material)
        {
            if (material == null) return null;
            string path = AssetDatabase.GetAssetPath(material);
            if (string.IsNullOrEmpty(path) || !path.StartsWith(ForestPaths.KitMaterials + "/", StringComparison.Ordinal)) return null;
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.EndsWith(MotionVectorSuffix, StringComparison.Ordinal)) return material;
            if (ForestWindProfiles.ForMaterial(name) == null) return null;
            return AssetDatabase.LoadAssetAtPath<Material>($"{ForestPaths.KitMaterials}/{name}{MotionVectorSuffix}.mat");
        }

        /// <summary>True for a manifest material of a model with a wind profile (sameAs followed): it moves, on the wind graphs.</summary>
        public static bool IsWindMaterial(string materialName) => ForestWindProfiles.ForMaterial(materialName) != null;

        [MenuItem("Gamex/Forest/Wind/Materials: Wind Shader")]
        public static void UseWindShaderFromMenu() => RebuildInMode(true);

        [MenuItem("Gamex/Forest/Wind/Materials: HDRP Lit (A-B)")]
        public static void UseLitFromMenu() => RebuildInMode(false);

        // The same assets (same GUIDs) rebuilt in the other mode; prefabs and scene untouched. The stamps record the mode, so the
        // next ordinary build returns to WindShaderDefault.
        static void RebuildInMode(bool windShader)
        {
            bool? previous = WindShaderOverride;
            WindShaderOverride = windShader;
            try
            {
                BuildAll(true);
            }
            finally
            {
                WindShaderOverride = previous;
            }
        }

        /// <summary>
        /// The ground variant "&lt;layer&gt;__ground" of a terrain layer (GroundVariantLayers): its maps projected from above at
        /// its tile, lined up with the terrain. Null when not built.
        /// </summary>
        public static Material GetGround(string layerId)
        {
            return AssetDatabase.LoadAssetAtPath<Material>(GroundPath(layerId));
        }

        /// <summary>
        /// The opaque material of a tree's crown core: the twig atlas's mean colour, darkened. The core
        /// is off by default, so the import stage does not build it; it is created here the first time
        /// the tree baker asks, and updated by BuildAll from then on. Null when the twig atlas has not
        /// been packed, or for a material that is not foliage.
        /// </summary>
        public static Material GetCrownCore(string twigMaterialName)
        {
            ForestMaterialEntry material = ForestManifest.ResolveMaterial(twigMaterialName);
            if (material == null || material.surface != "foliage") return null;
            string path = CrownCorePath(material.name, false);
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            ForestAssetEntry owner = ForestManifest.FindMaterialOwner(material.name);
            string atlas = ForestManifest.ProjectDiskPath(ForestTexturePacker.FoliageBaseColorPath(material.name));
            if (owner == null || !File.Exists(atlas)) return null;

            EnsureFolder(ForestPaths.KitMaterials);
            var pending = new List<(string assetPath, string stamp)>();
            BuildLit(FindLitShader(), null, CrownCoreRecipe(owner, material, false), true, pending, out Material core);
            // Only this asset: the caller is in the middle of a bake and may have much else unsaved.
            AssetDatabase.SaveAssetIfDirty(core);
            if (IsWindMaterial(material.name))
            {
                BuildLit(FindLitShader(), null, CrownCoreRecipe(owner, material, true), true, pending, out Material twin);
                AssetDatabase.SaveAssetIfDirty(twin);
            }
            Commit(pending);
            return core;
        }

        /// <summary>
        /// The terrain layers in manifest order (index = ForestSurface). All eight when all are built,
        /// the first four when only those are, otherwise none.
        /// </summary>
        public static TerrainLayer[] GetTerrainLayers()
        {
            var slots = new TerrainLayer[MaxTerrainLayers];
            foreach (ForestAssetEntry entry in ForestManifest.All)
            {
                if (entry.kind != "texture" || entry.terrainLayer < 0 || entry.terrainLayer >= MaxTerrainLayers) continue;
                slots[entry.terrainLayer] = AssetDatabase.LoadAssetAtPath<TerrainLayer>(TerrainLayerPath(entry.id));
            }
            int complete = 0;
            while (complete < MaxTerrainLayers && slots[complete] != null) complete++;

            int usable = complete >= MaxTerrainLayers ? MaxTerrainLayers : complete >= FallbackTerrainLayers ? FallbackTerrainLayers : 0;
            var result = new TerrainLayer[usable];
            Array.Copy(slots, result, usable);
            return result;
        }

        /// <summary>The terrain material (HDRP/TerrainLit with height blend). Null when not built.</summary>
        public static Material GetTerrainMaterial() => AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);

        /// <summary>True for an alpha-tested, double-sided material (twigs, fern, grass); sameAs is followed.</summary>
        public static bool IsFoliage(string materialName)
        {
            ForestMaterialEntry material = ForestManifest.ResolveMaterial(materialName);
            return material != null && material.surface == "foliage";
        }

        // ---- asset paths ----

        static string SurfacePath(string materialName, int tint, bool motionVectors = false) =>
            $"{ForestPaths.KitMaterials}/{materialName}{(tint == 0 ? "" : "__tint" + tint.ToString(CultureInfo.InvariantCulture))}{(motionVectors ? MotionVectorSuffix : "")}.mat";

        static string TriplanarPath(string materialName, bool motionVectors = false) =>
            $"{ForestPaths.KitMaterials}/{materialName}__triplanar{(motionVectors ? MotionVectorSuffix : "")}.mat";

        static string CrownCorePath(string materialName, bool motionVectors) =>
            $"{ForestPaths.KitMaterials}/{materialName}__crowncore{(motionVectors ? MotionVectorSuffix : "")}.mat";

        static string GroundPath(string layerId) => $"{ForestPaths.KitMaterials}/{layerId}__ground.mat";

        static string TerrainLayerPath(string layerId) => $"{ForestPaths.KitTerrainLayers}/{layerId}.terrainlayer";

        // ---- recipes ----

        static Recipe SurfaceRecipe(ForestAssetEntry entry, ForestMaterialEntry material, int tint, bool profileAvailable, bool motionVectors = false)
        {
            bool foliage = material.surface == "foliage";
            Color colour = BaseColourTint.TryGetValue(material.name, out Color matched) ? matched.gamma : Color.white;
            if (tint != 0) colour *= TintVariants[material.name];
            // The multiplier changes the colour only. Alpha stays 1: it scales the leaf mask.
            colour.a = 1f;
            float smoothnessMax = SmoothnessRemapMax.TryGetValue(material.name, out float duller) ? duller : 1f;

            float aoRemapMin = !foliage && AoRemapMin.TryGetValue(material.name, out float lifted) ? lifted : 0f;
            var recipe = new Recipe
            {
                assetPath = SurfacePath(material.name, tint, motionVectors),
                surface = foliage ? Surface.Foliage : Surface.Opaque,
                colour = colour,
                aoRemapMin = aoRemapMin,
                cutoff = material.alphaCutoff,
                smoothnessRemapMax = smoothnessMax,
                normal = LoadTexture(ForestManifest.AssetPath(material.maps.normal), material.name),
                mask = LoadTexture(ForestTexturePacker.MaskPath(material.name), material.name),
                baseColour = LoadTexture(foliage
                    ? ForestTexturePacker.FoliageBaseColorPath(material.name)
                    : ForestManifest.AssetPath(material.maps.basecolor), material.name),
            };
            recipe.stamp = ForestStamps.Hash(ToolVersion, recipe.surface.ToString(), recipe.assetPath,
                Md5(entry, foliage ? material.maps.diffuse : material.maps.basecolor), Md5(entry, material.maps.alpha),
                Md5(entry, material.maps.normal), Md5(entry, material.maps.arm),
                Number(colour.r), Number(colour.g), Number(colour.b), Number(material.alphaCutoff), profileAvailable ? "profile" : "no profile",
                "ao-remap", Number(aoRemapMin), "smoothness-remap", Number(smoothnessMax));
            return WithWind(recipe, material.name, motionVectors);
        }

        /// <summary>
        /// W1: the shader mode (wind graph or HDRP/Lit), the graph files' MD5, the profile values and the variant, in the recipe and
        /// its stamp. A material whose model has no profile is static and unchanged.
        /// </summary>
        static Recipe WithWind(Recipe recipe, string materialName, bool motionVectors)
        {
            recipe.profile = ForestWindProfiles.ForMaterial(materialName);
            if (recipe.profile == null)
            {
                if (motionVectors)
                    throw new InvalidOperationException($"{materialName} does not move, so it has no __mv twin.");
                return recipe;
            }
            recipe.wind = WindShaderActive;
            // In the HDRP/Lit mode the twins are plain HDRP/Lit copies with the pass off (HDRP/Lit would turn it off at its next validation).
            recipe.motionVectors = motionVectors && recipe.wind;
            recipe.stamp = ForestStamps.Hash(recipe.stamp, "wind", recipe.wind ? "wind-graph" : "hdrp-lit", recipe.wind ? GraphFilesMd5() : "",
                ForestWindProfiles.Describe(recipe.profile), motionVectors ? "mv" : "plain");
            return recipe;
        }

        static string GraphFilesMd5()
        {
            var parts = new List<string>();
            foreach (string path in new[] { ForestWindShaderGraphs.GraphPath, ForestWindShaderGraphs.TriplanarGraphPath,
                         ForestWindShaderGraphs.VertexIncludePath, ForestWindShaderGraphs.SurfaceIncludePath })
            {
                string disk = ForestManifest.ProjectDiskPath(path);
                parts.Add(File.Exists(disk) ? ForestStamps.Hash(File.ReadAllText(disk)) : "missing");
            }
            return string.Join("+", parts);
        }

        static Recipe TriplanarRecipe(ForestAssetEntry entry, ForestMaterialEntry material, bool motionVectors = false)
        {
            // Only a tileable opaque texture can be projected: a scan atlas or a leaf card cannot.
            if (material.surface != "opaque")
                throw new InvalidDataException($"Material {material.name} is named as a noUvFallback but is not opaque.");
            Recipe recipe = SurfaceRecipe(entry, material, 0, false, motionVectors);
            recipe.assetPath = TriplanarPath(material.name, motionVectors);
            recipe.surface = Surface.Triplanar;
            recipe.stamp = ForestStamps.Hash(recipe.stamp, "triplanar", recipe.assetPath);
            return recipe;
        }

        /// <summary>
        /// A terrain layer as an opaque HDRP/Lit material projected in world space (GroundVariantLayers). The terrain maps a
        /// layer as (world - terrain corner) / tile, the corner being at -TileSize / 2, so the projection takes the layer's tile
        /// and the corner's fractional offset: where the mesh lies on the floor, its texture continues the floor's.
        /// </summary>
        static Recipe GroundRecipe(ForestAssetEntry layer)
        {
            ForestAssetFile baseColour = ForestManifest.FileWithMap(layer, "basecolor");
            ForestAssetFile normal = ForestManifest.FileWithMap(layer, "normal");
            Color tint = LayerTints.TryGetValue(layer.id, out Color darker) ? darker : Color.white;
            tint.a = 1f;
            float minSmoothness = LayerMinSmoothness.TryGetValue(layer.id, out float wet) ? wet : 0f;
            float normalScale = LayerNormalScale.TryGetValue(layer.id, out float relief) ? relief : 1f;
            float tile = layer.tileSizeMeters;
            float corner = 0.5f * ForestLayout.TileSize / tile;
            var recipe = new Recipe
            {
                assetPath = GroundPath(layer.id),
                surface = Surface.Triplanar,
                colour = tint.gamma,   // diffuseRemapMax of the layer, a factor on linear albedo
                baseColour = LoadTexture(ForestManifest.AssetPath(baseColour.relPath), layer.id),
                normal = LoadTexture(ForestManifest.AssetPath(normal.relPath), layer.id),
                mask = LoadTexture(ForestTexturePacker.TerrainMaskPath(layer.id), layer.id),
                worldScale = 1f / tile,
                uvOffset = new Vector2(corner - Mathf.Floor(corner), corner - Mathf.Floor(corner)),
                smoothnessRemapMin = minSmoothness,
                normalScale = normalScale,
            };
            recipe.stamp = ForestStamps.Hash(ToolVersion, "ground", recipe.assetPath, baseColour.md5, normal.md5,
                ForestManifest.FileWithMap(layer, "arm").md5, Number(tile), Number(tint.r), Number(tint.g), Number(tint.b),
                Number(minSmoothness), Number(normalScale));
            return recipe;
        }

        static Recipe CrownCoreRecipe(ForestAssetEntry entry, ForestMaterialEntry material, bool motionVectors)
        {
            // Review 1 of the M4 captures: the core takes the twig material's own colour multiplier too, so it is the same
            // family as the needles around it (fir_tree_01_twig is darkened toward grey-green).
            Color twigTint = BaseColourTint.TryGetValue(material.name, out Color matched) ? matched : Color.white;
            Color mean = ForestTexturePacker.MeanFoliageColor(material.name) * twigTint * CrownCoreDarkening;   // linear
            mean.a = 1f;
            var recipe = new Recipe
            {
                assetPath = CrownCorePath(material.name, motionVectors),
                surface = Surface.CrownCore,
                colour = mean.gamma,   // _BaseColor is a colour property: Unity converts it back to linear for the shader
                smoothness = CrownCoreSmoothness,
            };
            recipe.stamp = ForestStamps.Hash(ToolVersion, "crowncore", recipe.assetPath,
                Md5(entry, material.maps.diffuse), Md5(entry, material.maps.alpha), Number(CrownCoreDarkening), Number(CrownCoreSmoothness),
                Number(twigTint.r), Number(twigTint.g), Number(twigTint.b));
            return WithWind(recipe, material.name, motionVectors);
        }

        // ---- HDRP/Lit ----

        /// <summary>
        /// Creates the material asset, or updates it in place. Returns false when it was already up to date. A wind recipe is built on
        /// the wind Shader Graph instead of <paramref name="lit"/>, with the same property names and order (W1).
        /// </summary>
        static bool BuildLit(Shader lit, DiffusionProfileSettings profile, Recipe recipe, bool force,
            List<(string assetPath, string stamp)> pending, out Material material)
        {
            material = AssetDatabase.LoadAssetAtPath<Material>(recipe.assetPath);
            if (!force && material != null && ForestStamps.IsCurrent(recipe.assetPath, recipe.stamp)) return false;
            ForestStamps.Forget(recipe.assetPath);

            Shader shader = lit;
            if (recipe.wind)
            {
                shader = ForestWindShaderGraphs.Load(recipe.surface == Surface.Triplanar);
                if (shader == null)
                    throw new InvalidOperationException($"{recipe.assetPath}: the wind Shader Graph has not been imported (Gamex/Forest/Wind/Write Shader Graphs).");
            }

            bool isNew = material == null;
            if (isNew) material = new Material(shader);

            bool translucent = recipe.surface == Surface.Foliage && profile != null;
            Configure(material, shader, recipe, translucent, !isNew);
            if (isNew) AssetDatabase.CreateAsset(material, recipe.assetPath);

            if (translucent)
            {
                // Only now: the profile reference is stored in a sub-asset of the saved material.
                HDMaterial.SetDiffusionProfile(material, profile);
                if (DiffusionProfileHashBits(material) == 0)
                {
                    // A translucent material without a registered profile renders magenta in HDRP 17.
                    // Standard has no translucency, and no possibility of that tint.
                    Debug.LogWarning($"Forest materials: {recipe.assetPath} got no diffusion profile hash. " +
                                     "It is built as Standard, without translucency.");
                    Configure(material, shader, recipe, false, true);
                }
            }

            // W1: after the last validation. The first validation of a Shader Graph material tags it MotionVector = User and turns
            // the pass off; a pass turned on afterwards stays on through later validations (BaseUnlitAPI.cs:363-392).
            if (recipe.wind)
                material.SetShaderPassEnabled(MotionVectorPass, recipe.motionVectors);

            EditorUtility.SetDirty(material);
            pending.Add((recipe.assetPath, recipe.stamp));
            return true;
        }

        /// <summary>Records the stamps of assets that have just been saved.</summary>
        static void Commit(List<(string assetPath, string stamp)> pending)
        {
            foreach ((string assetPath, string stamp) in pending)
                ForestStamps.Set(assetPath, stamp);
            pending.Clear();
        }

        static void Configure(Material material, Shader lit, Recipe recipe, bool translucent, bool reset)
        {
            if (reset)
            {
                // Back to the shader's defaults first, so an updated material ends up identical to a new one.
                var pristine = new Material(lit);
                material.shader = lit;
                material.CopyPropertiesFromMaterial(pristine);
                UnityEngine.Object.DestroyImmediate(pristine);
                // W1: a material moving between HDRP/Lit and the wind graph must not keep the other shader's keywords
                // (_NORMALMAP, _MASKMAP); ValidateMaterial rebuilds the ones that belong.
                if (recipe.wind || ForestWindShaderGraphs.IsWindShader(lit) || recipe.profile != null)
                    material.shaderKeywords = null;
            }

            bool foliage = recipe.surface == Surface.Foliage;
            bool triplanar = recipe.surface == Surface.Triplanar;
            if (recipe.wind)
            {
                ConfigureWind(material, recipe, foliage, triplanar, translucent);
                return;
            }

            material.SetTexture("_BaseColorMap", recipe.baseColour);
            material.SetTexture("_NormalMap", recipe.normal);
            material.SetTexture("_MaskMap", recipe.mask);
            material.SetColor("_BaseColor", recipe.colour);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", recipe.smoothness);   // read only where there is no mask map (the crown core)
            // The mask's AO is remapped to [min, 1] (LitDataIndividualLayer.hlsl:261); 0 is HDRP's default.
            material.SetFloat("_AORemapMin", recipe.aoRemapMin);
            material.SetFloat("_AORemapMax", 1f);
            material.SetFloat("_SmoothnessRemapMin", recipe.smoothnessRemapMin);
            material.SetFloat("_SmoothnessRemapMax", recipe.smoothnessRemapMax);
            material.SetFloat("_NormalScale", recipe.normalScale);

            material.SetFloat("_AlphaCutoffEnable", foliage ? 1f : 0f);
            HDMaterial.SetAlphaCutoff(material, foliage ? recipe.cutoff : 0.5f);
            material.SetFloat("_DoubleSidedEnable", foliage ? 1f : 0f);
            material.SetFloat("_DoubleSidedNormalMode", 1f);   // Mirror: the same leaf shape on both sides
            material.SetFloat("_MaterialID", translucent ? MaterialIdTranslucent : MaterialIdStandard);
            material.SetFloat("_Thickness", translucent ? FoliageThickness : 1f);

            // Triplanar projects the textures in world space at one tile per metre. The mapping mask and
            // the inverse tiling scale are what the HDRP inspector writes next to "_UVBase".
            material.SetFloat("_UVBase", triplanar ? UvBaseTriplanar : UvBaseUv0);
            material.SetFloat("_TexWorldScale", recipe.worldScale);
            material.SetColor("_UVMappingMask", triplanar ? new Color(0f, 0f, 0f, 0f) : new Color(1f, 0f, 0f, 0f));
            material.SetFloat("_InvTilingScale", 1f / recipe.worldScale);   // what the HDRP inspector writes for tiling 1
            material.SetTextureOffset("_BaseColorMap", recipe.uvOffset);

            // Prefab contract: no "Enable GPU Instancing" on kit materials.
            material.enableInstancing = false;
            // W1: a moving model's material rebuilt on HDRP/Lit (the A/B mode) drops the Shader Graph's override tag.
            if (recipe.profile != null)
                material.SetOverrideTag("MotionVector", "");

            if (!HDMaterial.ValidateMaterial(material))
                throw new InvalidOperationException($"HDRP did not accept {recipe.assetPath} as one of its materials.");
        }

        /// <summary>
        /// The wind graph's version of the recipe (W1 spec 3.3): HDRP/Lit's property names in HDRP/Lit's order, then the profile
        /// vectors, then validation. The graph always samples its normal and mask maps (it has no _NORMALMAP / _MASKMAP keyword),
        /// so a map-less crown core reads the defaults: white mask, bump normal.
        /// </summary>
        static void ConfigureWind(Material material, Recipe recipe, bool foliage, bool triplanar, bool translucent)
        {
            bool core = recipe.surface == Surface.CrownCore;
            material.SetTexture("_BaseColorMap", recipe.baseColour);
            material.SetTexture("_NormalMap", recipe.normal);
            material.SetTexture("_MaskMap", recipe.mask);
            material.SetColor("_BaseColor", recipe.colour);
            // HDRP/Lit reads _Metallic and _Smoothness only without a mask map. On the graph the crown core's white mask gives
            // metallic = _MetallicRemapMax and smoothness = _SmoothnessRemapMax, so they carry HDRP/Lit's values instead.
            material.SetFloat("_MetallicRemapMin", 0f);
            material.SetFloat("_MetallicRemapMax", core ? 0f : 1f);
            material.SetFloat("_AORemapMin", recipe.aoRemapMin);
            material.SetFloat("_AORemapMax", 1f);
            material.SetFloat("_SmoothnessRemapMin", core ? recipe.smoothness : recipe.smoothnessRemapMin);
            material.SetFloat("_SmoothnessRemapMax", core ? recipe.smoothness : recipe.smoothnessRemapMax);
            // The crown core has no normal map: a scale of 0 keeps the vertex normal whatever the default texture decodes to,
            // as HDRP/Lit without _NORMALMAP does.
            material.SetFloat("_NormalScale", core ? 0f : recipe.normalScale);

            material.SetFloat("_AlphaCutoffEnable", foliage ? 1f : 0f);
            HDMaterial.SetAlphaCutoff(material, foliage ? recipe.cutoff : 0.5f);
            material.SetFloat("_DoubleSidedEnable", foliage ? 1f : 0f);
            material.SetFloat("_DoubleSidedNormalMode", 1f);   // Mirror: the same leaf shape on both sides
            material.SetFloat("_MaterialID", translucent ? MaterialIdTranslucent : MaterialIdStandard);
            material.SetFloat("_Thickness", translucent ? FoliageThickness : 1f);
            if (triplanar)
                material.SetFloat("_TexWorldScale", recipe.worldScale);
            material.SetTextureOffset("_BaseColorMap", recipe.uvOffset);

            ForestWindProfiles.Profile wind = recipe.profile;
            material.SetVector("_GamexSway", wind.Sway);
            material.SetVector("_GamexFlutter", wind.Flutter);
            material.SetVector("_GamexMotion", wind.Motion);
            material.SetVector("_GamexPush", wind.Push);
            material.SetVector("_GamexStem", wind.Stem);

            // Prefab contract: no "Enable GPU Instancing" on kit materials.
            material.enableInstancing = false;

            if (!HDMaterial.ValidateMaterial(material))
                throw new InvalidOperationException($"HDRP did not accept {recipe.assetPath} on {material.shader.name} as one of its materials.");
        }

        static uint DiffusionProfileHashBits(Material material)
        {
            // The hash is a uint stored in a float property, so it is compared by its bits.
            return BitConverter.ToUInt32(BitConverter.GetBytes(material.GetFloat("_DiffusionProfileHash")), 0);
        }

        static Shader FindLitShader()
        {
            Shader lit = Shader.Find(LitShaderName);
            if (lit == null) throw new InvalidOperationException($"Shader {LitShaderName} was not found. The forest kit needs HDRP.");
            return lit;
        }

        /// <summary>Materials that some other material falls back to when its sub-mesh has no usable UVs.</summary>
        static HashSet<string> NoUvFallbackTargets()
        {
            var targets = new HashSet<string>();
            foreach (ForestAssetEntry entry in ForestManifest.All)
            {
                foreach (ForestMaterialEntry material in entry.materials)
                {
                    if (material.noUvFallback == "") continue;
                    ForestMaterialEntry target = ForestManifest.ResolveMaterial(material.noUvFallback);
                    if (target != null) targets.Add(target.name);
                }
            }
            return targets;
        }

        // ---- terrain ----

        static bool BuildTerrainLayer(ForestAssetEntry entry, bool force, List<(string assetPath, string stamp)> pending)
        {
            string path = TerrainLayerPath(entry.id);
            ForestAssetFile baseColour = ForestManifest.FileWithMap(entry, "basecolor");
            ForestAssetFile normal = ForestManifest.FileWithMap(entry, "normal");
            Color tint = LayerTints.TryGetValue(entry.id, out Color darker) ? darker : Color.white;
            float minSmoothness = LayerMinSmoothness.TryGetValue(entry.id, out float wet) ? wet : 0f;
            float normalScale = LayerNormalScale.TryGetValue(entry.id, out float relief) ? relief : 1f;
            ForestAssetFile heightMap = ForestManifest.FileWithMap(entry, "height");
            string stamp = ForestStamps.Hash(ToolVersion, "terrainlayer", TerrainHeightCentring, path, baseColour.md5, normal.md5,
                ForestManifest.FileWithMap(entry, "arm").md5, heightMap.md5,
                Number(entry.tileSizeMeters), Number(tint.r), Number(tint.g), Number(tint.b), Number(minSmoothness), Number(normalScale),
                Number(TerrainHeightAmplitude));

            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (!force && layer != null && ForestStamps.IsCurrent(path, stamp)) return false;
            ForestStamps.Forget(path);

            bool isNew = layer == null;
            if (isNew) layer = new TerrainLayer();

            layer.diffuseTexture = LoadTexture(ForestManifest.AssetPath(baseColour.relPath), entry.id);
            layer.normalMapTexture = LoadTexture(ForestManifest.AssetPath(normal.relPath), entry.id);
            layer.maskMapTexture = LoadTexture(ForestTexturePacker.TerrainMaskPath(entry.id), entry.id);
            layer.tileSize = new Vector2(entry.tileSizeMeters, entry.tileSizeMeters);
            layer.tileOffset = Vector2.zero;
            layer.normalScale = normalScale;
            layer.metallic = 0f;
            layer.smoothness = 0f;
            layer.diffuseRemapMin = Vector4.zero;
            layer.diffuseRemapMax = new Vector4(tint.r, tint.g, tint.b, 1f);
            // Mask map channels are metallic, AO, height (metres), smoothness. The height range has the same
            // width on every layer and is shifted so that the layer's own mean height lands on zero.
            float shift = (0.5f - MeanHeight(heightMap)) * 2f * TerrainHeightAmplitude;
            layer.maskMapRemapMin = new Vector4(0f, 0f, -TerrainHeightAmplitude + shift, minSmoothness);
            layer.maskMapRemapMax = new Vector4(1f, 1f, TerrainHeightAmplitude + shift, 1f);

            if (isNew) AssetDatabase.CreateAsset(layer, path);
            EditorUtility.SetDirty(layer);
            pending.Add((path, stamp));
            return true;
        }

        /// <summary>
        /// Mean of a layer's displacement map, 0 to 1. The scans do not agree on where "level" is: the means
        /// run from 0.27 (dry needles) to 0.62 (gravel). The height blend weighs every layer by its height
        /// against the highest layer, so with one remap for all a low-lying texture is pushed out of every
        /// mix it is painted into, at any distance (by arithmetic on those means: beside litter, needles keep
        /// about 0.4 of their painted share, humus 0.6, stony soil 0.7; the first M1 captures showed litter
        /// only). Centring keeps the amplitude identical and takes the bias away (issues/M1-ground.md).
        /// </summary>
        static float MeanHeight(ForestAssetFile heightMap)
        {
            Color32[] pixels = ForestTexturePacker.ReadPixels(ForestManifest.DiskPath(heightMap.relPath), out _, out _);
            if (pixels.Length == 0)
                throw new InvalidOperationException($"The height map {heightMap.relPath} holds no pixels.");
            long sum = 0;
            foreach (Color32 pixel in pixels) sum += pixel.r;
            return sum / (255f * pixels.Length);
        }

        static bool BuildTerrainMaterial(bool force, List<(string assetPath, string stamp)> pending)
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            Material source = pipeline != null ? pipeline.defaultTerrainMaterial : null;
            if (source == null)
                throw new InvalidOperationException("The active render pipeline has no default terrain material. The forest kit needs HDRP.");

            string stamp = ForestStamps.Hash(ToolVersion, "terrain", source.shader.name, Application.unityVersion,
                TerrainHeightBlend ? "blend" : "no blend", Number(TerrainHeightTransition));
            var material = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
            if (!force && material != null && ForestStamps.IsCurrent(TerrainMaterialPath, stamp)) return false;
            ForestStamps.Forget(TerrainMaterialPath);

            // Updating an existing copy from the pipeline's default terrain material leaves that default marked
            // dirty, although nothing in it changed. BuildAll's SaveAssets then rewrites the file inside the
            // package cache and Unity warns that an immutable package was altered (seen the first time this
            // material was rebuilt in place, after review 1 of the M2 captures). The mark is taken back, as
            // ForestTerrain.Create does after Terrain.CreateTerrainGameObject.
            bool sourceWasDirty = EditorUtility.IsDirty(source);

            // A copy of the pipeline's own terrain material (HDRP/TerrainLit), so its hidden state is right.
            bool isNew = material == null;
            if (isNew)
            {
                material = new Material(source);
            }
            else
            {
                material.shader = source.shader;
                material.CopyPropertiesFromMaterial(source);
            }
            material.SetFloat("_EnableHeightBlend", TerrainHeightBlend ? 1f : 0f);
            material.SetFloat("_HeightTransition", TerrainHeightTransition);
            if (!HDMaterial.ValidateMaterial(material))
                throw new InvalidOperationException($"HDRP did not accept the terrain material (shader {material.shader.name}).");
            material.enableInstancing = true;   // the terrain is drawn instanced

            if (isNew) AssetDatabase.CreateAsset(material, TerrainMaterialPath);
            EditorUtility.SetDirty(material);
            if (!sourceWasDirty && EditorUtility.IsDirty(source))
                EditorUtility.ClearDirty(source);
            pending.Add((TerrainMaterialPath, stamp));
            return true;
        }

        // ---- helpers ----

        static void Count(bool built, ref int builtCount, ref int keptCount)
        {
            if (built) builtCount++;
            else keptCount++;
        }

        static Texture2D LoadTexture(string assetPath, string what)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture == null)
                throw new FileNotFoundException($"{what}: texture '{assetPath}' is missing or not imported. {ImportHint}", assetPath);
            return texture;
        }

        static string Md5(ForestAssetEntry entry, string relPath)
        {
            ForestAssetFile file = ForestManifest.FileAt(entry, relPath);
            return file != null ? file.md5 : "";
        }

        static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>
        /// Makes a folder known to the asset database. A folder that already exists on disk is imported,
        /// never created through AssetDatabase.CreateFolder, which would add a second folder named "... 1".
        /// </summary>
        static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder)) return;

            string[] parts = assetFolder.Split('/');
            string path = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string parent = path;
                path += "/" + parts[i];
                if (AssetDatabase.IsValidFolder(path)) continue;
                if (Directory.Exists(ForestManifest.ProjectDiskPath(path)))
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                else
                    AssetDatabase.CreateFolder(parent, parts[i]);
            }
            if (!AssetDatabase.IsValidFolder(assetFolder))
            {
                AssetDatabase.Refresh();
                if (!AssetDatabase.IsValidFolder(assetFolder))
                    throw new IOException($"The folder {assetFolder} could not be added to the asset database.");
            }
        }
    }
}
