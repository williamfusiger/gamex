using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Gamex.EditorTools
{
    /// <summary>
    /// Every number the kit build can be tuned with (spec section 5.6): LOD distances, triangle
    /// targets, thinning parameters, collider sizes and the switches that select a fallback route.
    /// All of them are starting values. None has been judged on a rendered tree yet.
    /// </summary>
    public static class ForestKitSpec        // every constant of section 5.6: targets, distances, switches
    {
        public static bool UseUnityGenerator;   // default false
        // Risk 11's fallback (spec 5.6.4, section 14): false. The dithered cross-fade is static in this pipeline (64 accumulated
        // TAA frames do not resolve it), so a tree inside a fade band rendered as a cloud of loose flecks. With the tall firs
        // placed (M4, second half) that was every tall fir between about 88 and 110 m, where the mist still passes a fifth of
        // the contrast: swarms of dots across the middle of trail_mid_fwd and pano_0 (build/work/M4b/look/fade_ab.png: the same
        // frame with the tall firs' fade off shows solid misty crowns). Levels now switch without a fade band; the A/B also drew
        // 70,000 fewer triangles (no band where two levels are drawn). The contract test follows this switch.
        public static bool UseCrossFade = false;       // default true; risk 11 fallback taken (build/issues/M4-overstory.md)
        // Review 1 of the M3 captures tried risk 1's fallbacks (b) and (c) on the young firs: grouped thinning and the crown
        // core from the 14 m level. The core of this open sapling is an opaque olive spindle far wider than its branches
        // (build/captures/m3-r1/iterations/k1/kit/fir_sapling_medium_b__lods_14m.png), so both stay at the spec's
        // defaults; the far levels are twig cards instead (TwigCardsAt).
        public static bool ThinByGroup;         // default false
        public static int CrownCoreFromLod = 99;     // default 99 (off)
        public static float LodDistanceScale = 1f;   // default 1
        public static float LogPitchDegrees;    // default 0 (section 7.3)
        public static string[] ForceTriplanar = Array.Empty<string>();  // default empty: materials that take their noUvFallback even with UVs (section 5.5)

        // Part of every stamp. Raise it when the kit code changes what it writes, so old prefabs are rebuilt.
        internal const string ToolVersion = "kit-10";   // kit-10: M4 review 2: shoot cards for young and tall firs. kit-9: M4 review 1: per-tree crown core, trunk_c root skirt in ground material. kit-8: tall firs (M4): glTF geometry without UVs for a noUvFallback material, LOD group size cap. kit-7: twig cards from 14 m (M3 review 1)

        // The greybox seed. It only feeds hashes and patch layouts, so the kit is the same on every run.
        internal const int Seed = 1993;

        // ---- LOD distances (section 5.6.4): one "until" per level, nearest first; the last one culls ----

        // D2 (density pass, build/issues/D2-density.md): the mist is shorter (ForestLightingPresets: mean free path 20 m, a 2 m
        // silhouette reads at 15 to 20 m and is gone by 40 to 50 m), and every class is culled where it is gone: young firs at 50 m
        // (was 85), tall firs, rocks and logs at 55 m (85, 90, 110), stump patches at 50 m (70). The mist leaves 8 % of the contrast
        // at 50 m and 6 % at 55 m; D1 measured the cull at 55 m as invisible in a 20 m mist (build/issues/D1-optimisation.md, section 7).
        static readonly float[] YoungFirDistances = { 14f, 42f, 50f };
        // Knob 3 of spec section 10, the tall-fir half (M4, second half): LOD0 ends at 9 m, not 12 m. With the tall firs placed,
        // crouch_hide was over the frame and triangle budgets; 9 m took 0.77 M triangles off it (the spawn's tall fir leaves its
        // 222,700-triangle LOD0 for the crouched camera) and nothing off views that do not stand near a tall fir
        // (build/issues/M4-overstory.md, measurement base1). The young-fir half (14 to 10 m) stays off: it moves the coarser
        // needle level nearer (M3 placement), and the scan distance below was the spec's own first answer.
        // Review 1 of the M4 captures: LOD1 now ends at 35 m, not 45 m (the spec gives 45). LOD1 holds two to three times the needles it
        // held (a 35,000, b 18,000), and with the denser groves of review 1 the frame was over budget at most views (measurement i2:
        // ending LOD1 at 35 m saved 0.1 to 0.5 ms and 0.6 to 2.2 M triangles). At 35 m the mist already takes 40 % of the contrast,
        // and the far level is darkened like the rest.
        // Review 2 of the M4 captures: the far level keeps two to three times the triangles (see TallFirScales) and the denser stand
        // needs the frame time; tall firs are culled at 85 m (spec 110), where the mist leaves under 30 % of the contrast and the bank's
        // crest is 45 to 90 m from most trail views (90 m was measured first and left crouch_hide over its triangle budget).
        // LOD1 ends at 30 m (was 35, spec 45): the far level holds two to three times what it did, and the denser stand of review 2 put
        // crouch_hide over the triangle budget (measurement s2; build/issues/M4-review2.md).
        static readonly float[] TallFirDistances = { 9f, 30f, 55f };
        // The spec gives 12 m for the scan level. At 12 to 30 m the quarter-count level flattens the broken end
        // faces of set 01 into single dark facets (review 2: rock03 at 21 to 31 m read as a black cut-out). Risk
        // 12's fallback: the scan is kept to 20 m. About 90 rocks at about 10,000 triangles bound the cost.
        static readonly float[] BoulderDistances = { 20f, 30f, 55f };
        static readonly float[] LogDistances = { 8f, 18f, 40f, 55f };
        static readonly float[] StumpDistances = { 8f, 25f, 50f };
        static readonly float[] BranchDistances = { 10f, 35f };
        // The spec gives 16 and 40 m. At 40 m the mist still passes half of the contrast, so on open ground the
        // fern cover ended at a line that moved with the player (review 1 of the M2 captures). The far level
        // (three clumps, no shadows) now reaches 50 m; the near level (five clumps, shadows) ends two metres
        // earlier to pay for part of it, which is the first half of knob 5 of section 10.
        //
        // Review 2: at 14 m the five clumps were swapped for three light ones where a patch is still about 35
        // pixels tall, so the drifts thinned exactly where composition is read. The near level now reaches 24 m
        // (2.73 M of 8 M triangles were in use before this; the fern near level adds about 0.1 M at trail_start).
        //
        // Review 1 of the M3 captures: with the young firs, deadfall was over the frame budget, 1.3 M of its triangles
        // unique fern triangles of this level. Knob 5 of section 10: the near level is back at the spec's 16 m.
        //
        // Review 2 of the M4 captures: the fern patches reach up the bank now; the far level ends at the spec's 40 m again (it was 50).
        static readonly float[] FernPatchDistances = { 16f, 40f };
        static readonly float[] FernClumpDistances = { 30f };
        // The spec gives 25 m. The clearing is 26 m across, so its far half was bare from its own rim (review 2).
        static readonly float[] GrassPatchDistances = { 35f };

        // Share of the scan's triangles each level of a connected prop keeps ("about 1/4", "about 1/16").
        static readonly float[] ScanPropShares = { 1f, 0.25f, 0.0625f };

        // LODGroup. The formula assumes the player camera (field of view 70) and lodBias 1; probe P11
        // measures a real switch distance and corrects it through LodDistanceScale.
        internal const float TanHalfFieldOfView = 0.70020754f;   // tan(35 degrees)
        internal const float MaxScreenHeight = 0.98f;        // "clamped below 0.99": strictly below
        internal const float FadeTransitionWidth = 0.2f;

        // The LODGroup size is the object's height (spec 5.6.4). For a tree that is taller on screen than the screen where
        // its first level ends, the transition height was clamped to 0.98: fir_sapling_medium_a's 4.5 m scan level ran to
        // 6.4 m, with a cross-fade band of 0.02 of a screen height, which is none, and fir_tree_01_a's 12 m LOD0 would run
        // to 13.8 m (review 2, technical lens). The size only scales every transition height alike, so a smaller one moves
        // no distance: it is capped where the first level ends at this height, and every level ends where the table says.
        internal const float MaxFirstTransitionHeight = 0.8f;

        // ---- Trees (section 5.6.4) ----

        internal const int ThinnedLevels = 3;

        internal sealed class TreeSpec
        {
            public string id;
            public int[] foliage;        // twig triangle target per thinned level
            public float[] maxScale;     // cap on the enlargement of the kept pieces per thinned level
            public int scanWood;         // triangles of the untouched wood, which the nearest thinned level keeps
            public float scanDistance;   // metres the untouched scan is shown to; 0 = no scan level (until experiment E1 decides)
        }

        // Young firs: experiment E1 (spec 5.6.4, build/issues/M3-E1.md). A "piece" of fir_sapling_medium is one needle
        // card or one stem strip (median 8 triangles, 43,812 to 71,126 per tree), so enlarging the survivors turns
        // needles into blades: at the table's 60,000 triangles (arm A, 3.2 times life size) the near firs read as
        // bamboo at fir_close, and at 150,000 (arm B, 1.7 times) as a coarser conifer with loose needles. Only the
        // untouched scan (arm C) passed beside itself, so it is the nearest level, to 6 m: crouch_hide stayed inside the
        // 11 ms frame budget there, and the spec shortens it to 4 m only when the budget is missed.
        //
        // The thinned levels behind it were then tuned so the tree does not change species where they take over
        // (risk 1, fallback b; switch sheets at 6, 14 and 42 m in the kit capture). Every thinned level is flattened
        // (FlattenAt): a needle card of about eight triangles becomes a quad of two, so the first thinned level keeps
        // 26,000 needles at 1.2 times life size where the unflattened table level kept 3,000 at 3.2 times, and the
        // middle level (the table's 8,000) keeps 4,000 at 2.9 times instead of 1,600 at 5.4. Stems are tubes and
        // become thin sticks, not planks (ForestKitSpec.FlattenTubeCoherence). The far level has its enlargement capped
        // at 6 instead of 20 (needles at 20 times were straps, the 42 m pop) and 1,200 triangles instead of 1,500
        // (coverage 0.76 to 0.96 against the 0.70 gate). The frame budget sets the numbers: the first thinned level
        // is 52,000 (a: 61,000) instead of 60,000 (70,000), because a quad costs four vertices for two triangles and
        // crouch_hide was 0.3 ms over at 60,000; denser middle and far levels looked better and cost 0.3 to 1.6 ms
        // more there (build/issues/M3-E1.md, runs t2 to t8 and K1 to K8).
        //
        // Review 1 of the M3 captures: at 2.8 to 3.6 times (14 to 42 m) and 6 times (42 to 85 m) a needle was a blade 8 to
        // 15 cm long and the crowns read as bamboo. Those two levels are twig cards now (TwigCardsAt), which take no
        // scale cap; the caps below apply to the needle level only.
        static readonly float[] YoungFirScales = { 3.2f, 10f, 6f };
        // Tall firs (M4): the far level's cap is 12, not the spec's 20. Uncapped, its 4,000 (a) or 2,500 (b) needles grew 16.8 times
        // and the crown read as a bright mass of straws from 45 m on (build/captures/m4a/iterations/k3/kit/fir_tree_01_a__lods_45m.png),
        // the young firs' 42 m finding of M3a; at 8 it was a sparse spray of sticks (iterations/k4).
        // Review 2 of the M4 captures: at 12 the far level's needles were straps 12 times life size (poplar, eucalyptus, bamboo from 35 to
        // 110 m in every lens). The cap is 6 and the far level keeps more needles (a 12,000, b 9,000 triangles, was 8,000 and 5,000; 16,000
        // and 12,000 were tried and cost crouch_hide its triangle budget), so b keeps about 40 % and c about 80 % of their leaf area. a has
        // 812,000 needles: at 6 it kept a third of it and read as an airy birch at 70 m (build/work/M4r2/look/cmp_tall_far.png); it takes 8.
        // a stands on the bank only, beyond 45 m. Shoot cards for these levels were built and rejected: the tufts of fir_tree_01's crown
        // filled into the rounded clumps of a broadleaf crown (build/captures/m4-r2/iterations/k2/kit, fir_tree_01_b__lods_25m.png).
        // The middle level (9 to 30 m) is capped at 3.5 (a and b grew 4.8 and 4.9 times: blade-like needles in loose fans, the bamboo
        // read of the technical lens); the leaf area it loses shows as a crown more see-through, which is how the scan reads there.
        static readonly float[] TallFirScales = { 5.1f, 3.5f, 6f };
        static readonly float[] TallFirAScales = { 5.1f, 3.5f, 8f };

        static readonly TreeSpec[] Trees =
        {
            // a is the 685,000-triangle scan, 1.6 times b or c. Its scan level ended at 4.5 m from review 1 of the M3 captures
            // (deadfall stood 3.7 to 4.7 m from two of them).
            //
            // M4, second half: all three end at 4 m, spec 5.6.4's own rule for arm C ("shorten the distance to 4 m if the frame
            // budget is missed"; section 15: never below 4 m). With the tall firs in the scene crouch_hide measured 11.86 to
            // 11.95 ms and 14.29 M triangles against 11 ms and 11.6 M. The run-time trial of base1 took 2.58 M triangles off it,
            // but its override was clamped at 0.99 of the screen for b and c, so it tried about 4.85 m for them, not 4 m. With
            // the kit built at 4 m the frame is cheaper still, and fir_close no longer reaches its b's scan: it sees the first
            // thinned level (52,000 flattened needles at 1.25x). Side by side with M4a's scan the difference is small
            // (build/work/M4b/look2/fir_close-m4a-vs-m4b.png). 4 m stays because deadfall is still over the frame budget
            // (section 15: the largest distance that keeps the budget, never below 4 m). build/issues/M4-overstory.md.
            new TreeSpec { id = "fir_sapling_medium_a", foliage = new[] { 55000, 8000, 1200 }, maxScale = YoungFirScales, scanWood = 10300, scanDistance = 4f },
            new TreeSpec { id = "fir_sapling_medium_b", foliage = new[] { 47000, 8000, 1200 }, maxScale = YoungFirScales, scanWood = 5520, scanDistance = 4f },
            new TreeSpec { id = "fir_sapling_medium_c", foliage = new[] { 47000, 8000, 1200 }, maxScale = YoungFirScales, scanWood = 5590, scanDistance = 4f },
            // Tall firs (M4): no scan level (spec 5.6.4). LOD0's foliage is the table's times TallFirLod0Factor (knob 4 of spec
            // section 10).
            //
            // Review 1 of the M4 captures (build/issues/M4-review1.md): from 9 to 45 m the crowns of a and b read as poplar, birch or
            // bamboo, because LOD1 kept 20,000 (a) and 12,000 (b) needles at 6.4 and 6.1 times life size: 15 cm blades, six pixels long
            // at 25 m. LOD1 of a and b now keeps 35,000 and 18,000 needles (about 4.8 and 4.9 times); c keeps the table's 12,000 (2.8
            // times, the level all three lenses passed). The cost is paid in placement: a no longer stands in the core (risk 1(d)),
            // and young firs keep out of the deadfall. b keeps 18,000 (4.9 times) rather than 22,000: at 22,000, with the denser core,
            // crouch_hide drew 32 tall LOD1 firs and 13.5 M triangles against the 11.6 M budget (measurement i2).
            new TreeSpec { id = "fir_tree_01_a", foliage = new[] { Knob4(160000), 70000, 12000 }, maxScale = TallFirAScales, scanWood = 110700 },
            new TreeSpec { id = "fir_tree_01_b", foliage = new[] { Knob4(160000), 36000, 9000 }, maxScale = TallFirScales, scanWood = 93328 },
            new TreeSpec { id = "fir_tree_01_c", foliage = new[] { Knob4(90000), 24000, 4000 }, maxScale = TallFirScales, scanWood = 59420 },
        };

        // Knob 4 of spec section 10 ("LOD0 foliage targets times 0.7"), turned for the tall firs at M4a; 1 takes it back.
        // Review 1 of the M4 captures asked for 1, because a's LOD0 needles (3.7 times life size) read as blades from below in
        // canopy_up. Tried at 1 (a 3.1, b 2.2, c 1.4 times) and kept at 0.7: risk 1(d) took a out of the core, and the fir canopy_up
        // and trunk_close look at is now c, whose LOD0 at 0.7 (1.6 times) passed from below at M4a; at 1 the larger LOD0 meshes put
        // Mesh Memory at 408.4 MiB, over the 400 MiB budget (Budget_Counts, build/out/M4r1/tests/pre).
        internal const float TallFirLod0Factor = 0.7f;
        static int Knob4(int foliage) => Mathf.RoundToInt(foliage * TallFirLod0Factor);

        // Wood of the thinned levels: untouched, then clustered at 5 cm, then at 20 cm.
        static readonly float[] WoodClusterCells = { 0f, 0.05f, 0.20f };

        // How many triangles clustered wood may take, as a share of the level's foliage target. The cell
        // sizes above decide the count, which nobody knows before the mesh is read; the cap makes the
        // level's total predictable: between the foliage target and 1.16 times it, whatever the wood is.
        internal const float WoodBudgetShare = 0.16f;

        // Trunk capsule (section 5.6.6). The scatterer plans with the clearance constants before any
        // prefab exists, so the baker refuses a trunk that measures wider.
        internal const float YoungFirClearance = 0.15f, TallFirClearance = 0.40f;
        internal const float TrunkBandMin = 0.8f, TrunkBandMax = 1.6f, MinTrunkRadius = 0.12f;
        internal const float TrunkCapsuleHeight = 4f;

        // ---- Trunk scans whose UVs leave their atlas (M4, build/issues/M4-tall-firs.md) ----

        // fir_tree_01's trunk_a and trunk_b atlases are scans of the root flare and the lower trunk. Above 3 m (a) and 1.8 m (b)
        // the exported UVs leave them: a stretched one-texel strip along the atlas edge (a) or a 5-fold tiling of the whole,
        // non-tileable atlas (b). On b that is a smeared band from 2.5 m up in the close turntable (iterations/k5); those
        // triangles take the triplanar variant of the tileable bark, the fallback spec 5.5 gives trunk_c (ForestPrefabs.SplitOffAtlas),
        // which looks the same as before from 12 m on. trunk_a is not listed: done the same way (iterations/k6), its upper trunk
        // turned from the scan's pale tan to the darker bark with a seam at 3 m on every a, worse from every distance but the
        // closest; its strip stays (smeared only when looked at from a few metres above 3 m).
        internal static readonly string[] OffAtlasMaterials = { "fir_tree_01_trunk_b" };
        internal const string OffAtlasFallback = "fir_tree_01_bark";
        internal const float OffAtlasDensityShare = 0.1f;

        // ---- Root skirts (review 1 of the M4 captures) ----

        // fir_tree_01_c's trunk has no UVs (risk 23, case 2), so its whole sub-mesh is the triplanar bark, including the litter
        // skirt the scan carries around its root flare (0.1 to 0.2 m high, 0.8 m out: build/work/M4b/scripts/skirt.py). Drawn as
        // bark it was a dark rubber mat on the litter in long_sightline and pano_90. Its triangles that lie low and face up go
        // into a sub-mesh of their own, drawn with SkirtGroundLayer's textures projected from above at the terrain layer's own
        // tile (ForestMaterials.GetGround), so the skirt reads as the floor it stands in: soil over roots.
        // Review 2 of the M4 captures: b stood on its scan's own ground patch, a near-black mat with a hard edge (three lenses). b's
        // LOD0 already used the contract's four sub-meshes; its branch bark now shares the triplanar bark of its off-atlas trunk
        // (BranchesTakeOffAtlasBark, ForestPrefabs.ResolveMaterials), which frees the slot for its skirt.
        internal static readonly string[] SkirtMaterials = { "fir_tree_01_trunk_c", "fir_tree_01_trunk_b" };
        internal const bool BranchesTakeOffAtlasBark = true;
        internal const float SkirtMaxHeight = 0.3f, SkirtMinNormalY = 0.5f;
        // Humus: the floor under a tall fir is painted humus (spec 6.4, plus the crown stamp of M4b).
        internal const string SkirtGroundLayer = "forest_ground_06";

        // ---- Thinner (section 5.6.3) ----

        internal const float WeldQuantum = 0.0001f;          // 0.1 mm: only decides which triangles touch
        internal const int MinPieces = 500;
        internal const float MaxPieceShare = 0.05f;
        internal const float SpatialPieceCell = 0.06f;
        internal const float KeyInnerWeight = 0.35f;         // w = area * (0.35 + 0.65 * outerness)
        internal const float BandHeight = 1f;
        internal const float KeyCentroidQuantum = 0.001f;
        internal const float GroupCell = 0.25f;              // ThinByGroup
        internal const float FlattenMaxResidual = 0.35f;
        // A piece whose vertex normals average to less than this length is a closed tube (a twig's stem), not a card:
        // it is flattened into a stick along its axis, not into the quad of its UV box (ForestFoliageThinner.TryFitStick).
        internal const float FlattenTubeCoherence = 0.5f;
        // Largest enlargement of a stem (tube) piece. Stems carry no leaf area; enlarged with the needles they became
        // straight dark wires standing out of the far crowns (review 1 of the M3 captures).
        internal const float StickMaxScale = 1.2f;
        internal const int EnvelopeSectors = 16;
        internal const float EnvelopeScaleStep = 1.1f;

        // Below this many triangles the piece guard is not applied: with a handful of pieces one of
        // them always holds more than 5 %, and a lone card would be cut into 6 cm cells.
        internal const int PieceGuardMinTriangles = 50000;

        // ---- Twig cards (review 1 of the M3 captures; build/issues/M3-review1.md) ----

        /// <summary>One photographed twig spray of a foliage atlas: its UV box (u right, v up; the stem at low v).</summary>
        internal struct TwigSprite
        {
            public Vector2 uvMin, uvMax;
            public TwigSprite(float u0, float v0, float u1, float v1) { uvMin = new Vector2(u0, v0); uvMax = new Vector2(u1, v1); }
            public float Aspect => (uvMax.x - uvMin.x) / Mathf.Max(uvMax.y - uvMin.y, 1e-6f);   // the atlas is square
        }

        // The seven twig sprays of fir_sapling_medium_twigs (2048 x 2048), measured on its alpha map: connected regions of
        // alpha >= 0.4 on an 8 px grid (build/work/M3r1/scripts/sprites.py). The scan's needle cards use only the four
        // single needles at the bottom left of the atlas; these sprays are unused by its mesh.
        static readonly TwigSprite[] SaplingTwigSprites =
        {
            new TwigSprite(0.6523f, 0.6289f, 0.9414f, 0.9609f),   // forked twig end, top right
            new TwigSprite(0.1875f, 0.6875f, 0.4297f, 0.9531f),   // forked twig end, top left
            new TwigSprite(0.5391f, 0.7695f, 0.5898f, 0.9023f),   // single shoot
            new TwigSprite(0.4922f, 0.5977f, 0.6328f, 0.7422f),   // small spray
            new TwigSprite(0.3867f, 0.6172f, 0.4609f, 0.7109f),   // small spray
            new TwigSprite(0.3047f, 0.2227f, 0.6445f, 0.6016f),   // large spray, centre
            new TwigSprite(0.6328f, 0.1680f, 0.9609f, 0.5508f),   // large spray, right
        };

        // Review 2 of the M4 captures (build/issues/M4-review2.md): drawn with the whole sprays above, the card levels read as
        // broadleaf saplings (birch, beech) from 14 to 85 m in every lens: a spray is as wide as it is long, and once its needles
        // blur in the mip chain its outline is a leaf. The cards now use single shoots only: tight boxes around one bottle-brush
        // shoot each, cut from the same sprays (build/work/M4r2/scripts/uvcrop.swift, build/work/M4r2/look/shoots1.png), about
        // 0.4 times as wide as long. Blurred, a shoot stays a narrow dark brush, which is how a fir twig reads at distance. Every box
        // lies inside one of the spray boxes above, so the packed mask (whose AO the packer matches inside the spray boxes) is
        // unchanged.
        static readonly TwigSprite[] SaplingShootSprites =
        {
            new TwigSprite(0.5391f, 0.7695f, 0.5898f, 0.9023f),   // the single shoot
            new TwigSprite(0.7860f, 0.7610f, 0.8590f, 0.9610f),   // leading shoot of the forked twig end, top right
            new TwigSprite(0.2640f, 0.8100f, 0.3320f, 0.9560f),   // leading shoot of the forked twig end, top left
            new TwigSprite(0.7520f, 0.1700f, 0.8150f, 0.3160f),   // stem shoot of the large spray, right
            new TwigSprite(0.4440f, 0.2240f, 0.5130f, 0.3750f),   // stem shoot of the large spray, centre
        };

        /// <summary>The sprites twig cards are drawn with; null when the atlas has none measured.</summary>
        /// <remarks>
        /// fir_tree_01_twig is the same atlas with another colour: its alpha, normal and arm maps are byte for byte the
        /// sapling's (same md5 in the manifest), only the diffuse differs. Its twigs map to the same single needles and none
        /// to the sprays (build/work/M4a/scripts/twig_uv.py: 45 % of the twig UVs in the needle box, 0 % on any spray), so
        /// the sapling's measurement holds for it.
        /// </remarks>
        internal static TwigSprite[] TwigSprites(string twigMaterial) =>
            SharesSaplingAtlas(twigMaterial) ? SaplingShootSprites : null;

        /// <summary>The atlas regions whose AO the packer matches to the needles' (the whole sprays); null when none.</summary>
        internal static TwigSprite[] AoMatchedSprites(string twigMaterial) =>
            SharesSaplingAtlas(twigMaterial) ? SaplingTwigSprites : null;

        static bool SharesSaplingAtlas(string twigMaterial) =>
            twigMaterial == "fir_sapling_medium_twigs" || twigMaterial == "fir_tree_01_twig";

        // The four single needles the scan's needle cards are mapped to (same measurement). Their AO in the atlas averages
        // 0.43, the sprays' 0.74 to 0.79; HDRP multiplies the sky light by it, so cards drawn with the sprays read about
        // 1.8 times lighter than the needles they replace. The packer brings the sprays' AO to the needles' mean
        // (ForestTexturePacker.MatchSpriteAo); no part of the atlas the scan uses changes.
        static readonly TwigSprite SaplingNeedleBox = new TwigSprite(0.2070f, 0.0000f, 0.2930f, 0.1953f);

        internal static bool TwigNeedleBox(string twigMaterial, out TwigSprite box)
        {
            box = SaplingNeedleBox;
            return SharesSaplingAtlas(twigMaterial);
        }

        // Thinned levels from this one on are twig cards (1 = the 14 to 42 m level of a young fir), for trees behind a scan
        // level whose atlas has measured sprites. A card is as long and wide as its cell's needles reach, times the fill.
        internal const int TwigCardsFromLevel = 1;
        internal const float TwigCardFill = 0.85f;
        // A card is as long as its cell's needles reach along its main axis, and long enough that the sprite's width covers
        // their spread across it, but never more than this many times their reach along it: a shoot sprite is 0.4 times as wide
        // as long, and covering the whole spread of a round cell would make it two and a half times the cell (review 2 of M4).
        internal const float TwigCardMaxStretch = 1.5f;
        // Card cells are this many times as long along the radius from the trunk as they are high and wide
        // (ForestFoliageThinner.CardCellKey), so a card is a stroke along its branch (review 2 of the M4 captures).
        internal const float TwigCardRadialStretch = 2.5f;
        // A card's shading normal points away from the trunk and this much up (ForestFoliageThinner.TwigCards).
        internal const float TwigCardNormalLift = 0f;

        // Which thinned level the scan level's shadows-only renderer (LOD0_Shadow) draws. E1 (spec 5.6.4, arm C) gives it
        // the first thinned level; review 1 of the M3 captures moved it to the first card level: at crouch_hide, where
        // seven young firs stand inside their scan distance, that is 1.05 M triangles fewer (11.30 to 10.25 M against
        // the 11.6 M budget) for a shadow that the overcast light keeps soft (build/issues/M3-review1.md).
        internal const int ScanShadowThinnedLevel = 1;

        // Tall firs (M4) took no twig cards: built from the whole sprays, their 12 to 45 m and 45 to 110 m levels read as a light
        // broadleaf tree at every distance the kit sheets show (build/captures/m4a/iterations/k2/kit/fir_tree_01_*__lods_25m.png),
        // the finding review 2 made on the young firs, and their levels were flattened needles (FlattenAt). Review 2 of the M4
        // captures: as needles 4.8 times (LOD1) and 12 times (LOD2) life size, every lens read their crowns as poplar, eucalyptus
        // or bamboo from 9 to 110 m. With the shoot sprites (TwigSprites) their thinned levels from this one on are shoot cards too.
        internal const int TallFirTwigCardsFromLevel = 99;

        internal static bool TwigCardsAt(string prefabId, int thinnedLevel, string twigMaterial)
        {
            if (TwigSprites(twigMaterial) == null) return false;
            if (ClassOf(prefabId) == ForestClass.TallFir) return thinnedLevel >= TallFirTwigCardsFromLevel;
            return thinnedLevel >= TwigCardsFromLevel && ScanLodDistance(prefabId) > 0f;
        }

        // Crown core (off by default): a hull of 12 bands by 8 sectors at 80 % of the 70th percentile radius.
        internal const int CrownCoreBands = 12, CrownCoreSectors = 8;
        internal const float CrownCoreRadiusFraction = 0.8f, CrownCorePercentile = 0.7f;
        internal const int CrownCoreTriangles = 2 * CrownCoreBands * CrownCoreSectors;

        // Coverage gate: silhouette cells of a thinned level against the source, at the pixel size of the
        // distance the level starts at. The nearest level starts at 0 m, so it is measured at this distance.
        internal const float CoverageMax = 1.30f;
        internal const float CoverageNearDistance = 2f;
        internal const float CoverageScreenHeight = 1440f;

        // ---- Connected props ----

        internal const float CollisionCell = 0.10f;
        internal const int CollisionMaxTriangles = 3000;     // the collision mesh stays under this

        // ---- Patches (section 5.6.5) ----

        // Ferns: the spec gives five clumps within 1.0 m at scale 0.85 to 1.3. The scanned clumps are 0.2 to
        // 0.43 m tall and 0.55 to 1.0 m across, so that patch was five separate small rosettes with bare ground
        // between them, and a drift of such patches never closed (review 1 of the M2 captures: "lone rosettes
        // dotted on bare litter"). Drawn together and a little larger, the fronds of a patch interlock into one
        // mass of sword-fern height, and neighbouring patches of a drift touch.
        internal const float FernPatchRadius = 0.7f, FernScaleMin = 1.1f, FernScaleMax = 1.6f, FernFarScale = 1.6f;
        // Grass: the spec gives twelve tufts within 0.6 m at about life size. The scanned clumps are lawn
        // height (9 to 32 cm tall, 15 to 20 cm wide), and the first M2 captures showed a patch as a dozen
        // single blades on bare ground: nothing read as grass from eye level. A patch is now one ragged
        // tussock: the leafy tufts only, drawn together and two and a half times life size (40 to 55 cm
        // across, 25 to 50 cm tall: a sedge tussock, no longer a lawn grass). At life size, and at twice
        // life size with seed stalks, nothing in the clearing read as grass from standing height.
        internal const float GrassPatchRadius = 0.42f, GrassScaleMin = 2.3f, GrassScaleMax = 2.9f;
        internal const int GrassPatchMaxTriangles = 2500;

        internal sealed class PatchSpec
        {
            public string id;
            public string[] clumps;      // manifest object names; the first one stands nearest the centre
            public int[] farClumps;      // which of them the far level keeps; empty = the patch has one level
        }

        static readonly int[] FernFarClumps = { 1, 2, 3 };
        static readonly int[] NoFarClumps = Array.Empty<int>();

        static readonly PatchSpec[] Patches =
        {
            // One heavy clump in the middle, four light ones around it.
            new PatchSpec { id = "fern_patch_a", farClumps = FernFarClumps,
                clumps = new[] { "fern_02_b", "fern_02_a", "fern_02_d", "fern_02_a", "fern_02_d" } },
            new PatchSpec { id = "fern_patch_b", farClumps = FernFarClumps,
                clumps = new[] { "fern_02_c", "fern_02_a", "fern_02_d", "fern_02_d", "fern_02_a" } },
            new PatchSpec { id = "fern_patch_c", farClumps = FernFarClumps,
                clumps = new[] { "fern_02_b", "fern_02_d", "fern_02_a", "fern_02_d", "fern_02_a" } },
            // The triangle cap is spent on leaves. The spec gives twelve tufts; with one leafy clump, four
            // seed stalks and seven "tiny" clumps of a few blades each, a patch was a handful of stalks with
            // nothing under them (first M2 captures), and with two leafy clumps and three stalks it was
            // still stalks on a needle bed (review 1). The leafy clumps cost 650 to 1,260 triangles, so the
            // cap of GrassPatchMaxTriangles buys three of the lighter ones, or the heavy one and a light
            // one, and a few small fillers: 2,421 and 2,372 triangles. No seed stalks: at this scale they
            // would stand a metre tall. Only clumps from the spec's list of eight.
            new PatchSpec { id = "grass_patch_a", farClumps = NoFarClumps, clumps = new[]
            {
                Grass("small_a"), Grass("small_b"), Grass("small_b"), Grass("tiny_a"), Grass("tiny_e"), Grass("tiny_a"), Grass("tiny_e"),
            } },
            new PatchSpec { id = "grass_patch_b", farClumps = NoFarClumps, clumps = new[]
            {
                Grass("mid_b"), Grass("small_a"), Grass("tiny_a"), Grass("tiny_e"), Grass("tiny_a"), Grass("tiny_e"),
            } },
        };

        static string Grass(string variant) => "grass_medium_01_" + variant + "_LOD0";

        // ---- Public surface ----

        public static float ScanLodDistance(string prefabId)   // metres; 0 = no scan level (the default until experiment E1 decides)
        {
            // Tall firs get no scan level in version 1: one of them is 4.2 million triangles.
            if (ClassOf(prefabId) != ForestClass.YoungFir) return 0f;
            return Mathf.Max(0f, Tree(prefabId).scanDistance);
        }

        public static int LodCount(string prefabId)            // 1 to 4, scan level included
        {
            return ClassDistances(ClassOf(prefabId)).Length + (ScanLodDistance(prefabId) > 0f ? 1 : 0);
        }

        public static float[] LodDistances(string prefabId)    // metres, one "until" per level, nearest first; the last is also the cull distance (PositiveInfinity = never culled)
        {
            float[] table = ClassDistances(ClassOf(prefabId));
            float scan = ScanLodDistance(prefabId);
            if (scan <= 0f) return (float[])table.Clone();
            if (scan >= table[0])
                throw new InvalidOperationException(
                    $"The scan level of {prefabId} ends at {scan} m, which is not inside the first thinned level ({table[0]} m).");

            var distances = new float[table.Length + 1];
            distances[0] = scan;
            Array.Copy(table, 0, distances, 1, table.Length);
            return distances;
        }

        public static int TriangleTarget(string prefabId, int lod)   // expected triangles of that level's mesh (foliage + wood); 0 when unknown
        {
            ForestClass cls = ClassOf(prefabId);
            if (lod < 0 || lod >= LodCount(prefabId)) return 0;

            switch (cls)
            {
                case ForestClass.YoungFir:
                case ForestClass.TallFir:
                    if (ScanLodDistance(prefabId) <= 0f) return ThinnedLevelTarget(prefabId, lod);
                    return lod == 0 ? SourceTriangles(prefabId) : ThinnedLevelTarget(prefabId, lod - 1);
                case ForestClass.Boulder:
                case ForestClass.Stump:
                case ForestClass.Branch:
                    return Mathf.RoundToInt(SourceTriangles(prefabId) * ScanPropShares[lod]);
                case ForestClass.Log:
                    return lod == 0 ? SourceTriangles(prefabId) : AuthoredLevelTriangles(prefabId, lod);
                case ForestClass.FernClump:
                    return SourceTriangles(prefabId);
                case ForestClass.FernPatch:
                case ForestClass.GrassPatch:
                    return PatchTriangles(Patch(prefabId), lod);
                default:
                    return 0;
            }
        }

        public static float ClearanceRadius(string prefabId)   // metres at scale 1 (section 5.6.6); 0 for prefabs without a collider. Needs no built prefab
        {
            switch (ClassOf(prefabId))
            {
                case ForestClass.YoungFir:
                    return YoungFirClearance;
                case ForestClass.TallFir:
                    return TallFirClearance;
                case ForestClass.Boulder:
                case ForestClass.Stump:
                case ForestClass.Log:
                    // The collision mesh is cut from the scan, so the scan's footprint contains it.
                    ForestObjectEntry entry = ForestManifest.FindObjectForPrefab(prefabId)
                        ?? throw new InvalidOperationException($"The manifest has no object for prefab '{prefabId}'.");
                    return ForestManifest.FootprintRadius(entry);
                default:
                    return 0f;
            }
        }

        // ---- For the kit builders ----

        internal static bool TryClassOf(string prefabId, out ForestClass cls)
        {
            if (Has(ForestPrefabIds.TallFirs, prefabId)) cls = ForestClass.TallFir;
            else if (Has(ForestPrefabIds.YoungFirs, prefabId)) cls = ForestClass.YoungFir;
            else if (Has(ForestPrefabIds.Boulders, prefabId)) cls = ForestClass.Boulder;
            else if (Has(ForestPrefabIds.Logs, prefabId)) cls = ForestClass.Log;
            else if (Has(ForestPrefabIds.Stumps, prefabId)) cls = ForestClass.Stump;
            else if (Has(ForestPrefabIds.Branches, prefabId)) cls = ForestClass.Branch;
            else if (Has(ForestPrefabIds.FernPatches, prefabId)) cls = ForestClass.FernPatch;
            else if (Has(ForestPrefabIds.FernClumps, prefabId)) cls = ForestClass.FernClump;
            else if (Has(ForestPrefabIds.GrassPatches, prefabId)) cls = ForestClass.GrassPatch;
            else
            {
                cls = ForestClass.Landmark;
                return false;
            }
            return true;
        }

        internal static ForestClass ClassOf(string prefabId)
        {
            if (!TryClassOf(prefabId, out ForestClass cls))
                throw new ArgumentException($"'{prefabId}' is not a forest prefab id (see ForestPrefabIds).", nameof(prefabId));
            return cls;
        }

        internal static bool IsTree(ForestClass cls) => cls == ForestClass.YoungFir || cls == ForestClass.TallFir;

        internal static TreeSpec Tree(string prefabId)
        {
            foreach (TreeSpec tree in Trees)
                if (tree.id == prefabId) return tree;
            throw new ArgumentException($"'{prefabId}' has no row in the tree table of ForestKitSpec.", nameof(prefabId));
        }

        internal static PatchSpec Patch(string prefabId)
        {
            foreach (PatchSpec patch in Patches)
                if (patch.id == prefabId) return patch;
            throw new ArgumentException($"'{prefabId}' has no row in the patch table of ForestKitSpec.", nameof(prefabId));
        }

        internal static float TreeClearance(ForestClass cls) => cls == ForestClass.YoungFir ? YoungFirClearance : TallFirClearance;

        /// <summary>Cell size the wood of a thinned level is clustered at; 0 = the scan, untouched.</summary>
        internal static float WoodClusterCell(int thinnedLevel) => WoodClusterCells[thinnedLevel];

        // Tall firs (M4): from this thinned level on (45 to 110 m) the wood budget goes to the trunk alone. Clustered together with
        // the branches into WoodBudget triangles, the 19 m trunk was a twisted ribbon (k2, k3 sheets); branches thinner than a pixel
        // there are dropped. The young firs' far trunk is unchanged (review 1 of M3 left it to this decision).
        // Review 1 of the M4 captures: from LOD1 (9 m) on. Clustered at 5 cm, the thin dead branches of the lower trunk broke into
        // disconnected black slivers floating beside the trunks at middle distance (trail_strip_01; kit sheets, 25 m column 3).
        // Review 2 of the M4 captures: with the trunk alone from 9 m the needle tufts floated around a bare pole ("pom-pom on a
        // stick", the strongest poplar cue at 9 to 35 m). LOD1 (thinned level 1) draws the trunk and the branches inside the crown
        // again; the dead branches below the crown, which made the slivers, stay out (TallFirCrownWoodLevel, CrownBaseShare).
        // The far level keeps the trunk alone.
        internal const int TallFirTrunkOnlyFromLevel = 2;
        internal const int TallFirCrownWoodLevel = 1;
        // The crown begins where this share of the twig vertices lies below (ForestTreeBaker.CrownBase).
        internal const float CrownBaseShare = 0.03f;

        internal enum WoodKind { All, TrunkAndCrownBranches, TrunkOnly, None }

        // Review 2 of the M4 captures (the frame budget of the denser stand, measured in the Editor where every draw counts): the young
        // firs' far level (42 to 85 m) draws its twig cards only. Its wood, clustered at 20 cm, was a second draw and a shadow draw per
        // fir for a trunk of under a pixel, and it showed as a dark wedge at the foot of the cards (kit sheets, 42 m column 4).
        internal const int YoungFirNoWoodFromLevel = 2;
        // The same for the root skirt of the tall firs (ForestPrefabs.SplitSkirt): a fourth draw per tall fir from 9 m on, for a patch
        // of ground the terrain already paints. Thinned levels from this one on leave it out.
        internal const int SkirtUntilThinnedLevel = 0;
        // And the far trunk (trunk only, from 30 m) is one draw: b's scanned flare and triplanar upper trunk share the triplanar bark there.
        internal const bool FarTrunkOneDraw = true;

        /// <summary>Which wood a thinned level clusters (0 is the untouched scan wood and is never asked).</summary>
        internal static WoodKind WoodAt(string prefabId, int thinnedLevel)
        {
            if (ClassOf(prefabId) == ForestClass.YoungFir) return thinnedLevel >= YoungFirNoWoodFromLevel ? WoodKind.None : WoodKind.All;
            if (ClassOf(prefabId) != ForestClass.TallFir) return WoodKind.All;
            if (thinnedLevel >= TallFirTrunkOnlyFromLevel) return WoodKind.TrunkOnly;
            return thinnedLevel >= TallFirCrownWoodLevel ? WoodKind.TrunkAndCrownBranches : WoodKind.All;
        }

        /// <summary>A trunk material of a tree (fir_tree_01_trunk_a, _b, _c), as opposed to its branch bark.</summary>
        internal static bool IsTrunkMaterial(string materialName) =>
            materialName != null && materialName.StartsWith("fir_tree_01_trunk_", StringComparison.Ordinal);

        /// <summary>Most triangles the clustered wood of a thinned level may have; 0 = no cap (the scan).</summary>
        internal static int WoodBudget(string prefabId, int thinnedLevel)
        {
            return thinnedLevel == 0 ? 0 : Mathf.RoundToInt(Tree(prefabId).foliage[thinnedLevel] * WoodBudgetShare);
        }

        /// <summary>
        /// The table's LOD0 keeps the piece triangles because it is what the player sees at arm's length; the farther
        /// levels replace each piece with one quad. Behind a scan level (experiment E1) the arm's-length view is the
        /// scan, so every thinned level is flattened: a needle of the young firs is a piece of about eight triangles,
        /// and as a quad it costs two, so a level keeps about four times as many needles at a much smaller enlargement
        /// for the same triangles (risk 1, fallback b: flatten per level).
        /// </summary>
        /// <remarks>
        /// Tall firs (M4) have no scan level, but they take E1's recipe all the same: their twigs are the same single needle
        /// cards (fir_tree_01_twig uses the sapling's atlas and needle region), and flattened, LOD0 keeps about four times as
        /// many needles at half the enlargement for its triangles.
        /// </remarks>
        internal static bool FlattenAt(string prefabId, int thinnedLevel) =>
            thinnedLevel >= 1 || ScanLodDistance(prefabId) > 0f || ClassOf(prefabId) == ForestClass.TallFir;

        internal static bool CrownCoreAt(int thinnedLevel) => thinnedLevel >= CrownCoreFromLod;

        // Risk 1(c) for the tall firs a and b only, as review 1 of the M4 captures proposed: a dark crown core inside the 12x needles of
        // the far level (45 to 110 m), for a dense dark spire under the mist. Tried (build/captures/m4-r1/iterations/k1/kit,
        // fir_tree_01_*__lods_45m.png and _70m.png) and switched off: the hull came out as a solid, faceted brown lump as wide as the
        // crown (coverage 1.15 to 1.23), b's twisted into a spiral, a rock pillar with straws on it at every distance, as the young firs'
        // core was an olive spindle at M3 review 1. 99 = off; the mechanism stays for the owner (risk 1).
        internal const int TallFirCrownCoreFromLevel = 99;
        static readonly string[] CrownCoreTrees = { "fir_tree_01_a", "fir_tree_01_b" };

        /// <summary>Whether a thinned level of this tree carries the crown core: CrownCoreFromLod for every tree, and the tall firs' own rule.</summary>
        internal static bool CrownCoreAt(string prefabId, int thinnedLevel) =>
            CrownCoreAt(thinnedLevel) || (Has(CrownCoreTrees, prefabId) && thinnedLevel >= TallFirCrownCoreFromLevel);

        internal static float CoverageMin(int thinnedLevel) => thinnedLevel >= 2 ? 0.70f : 0.80f;

        /// <summary>World size of one pixel of a 1440-line frame at that distance, for the coverage gate.</summary>
        internal static float PixelSize(float distance)
        {
            return 2f * Mathf.Max(distance, CoverageNearDistance) * TanHalfFieldOfView / CoverageScreenHeight;
        }

        /// <summary>Share of the scan's triangles a level of a rock, stump patch or dry branch keeps.</summary>
        internal static float ScanPropShare(int lod) => ScanPropShares[lod];

        internal static bool CastsShadows(ForestClass cls, int lod)
        {
            switch (cls)
            {
                case ForestClass.YoungFir:
                case ForestClass.TallFir:      // a scan level is the exception; the baker handles it
                case ForestClass.Boulder:
                case ForestClass.Log:
                case ForestClass.Stump:
                    return true;
                case ForestClass.FernPatch:
                    return lod == 0;
                // The spec gives dry branches no shadow at all. Lying on open litter without one they looked
                // pasted on (review 1 of the M2 captures). Only the scan level casts: it ends at 10 m, and
                // 65 instances are nothing against a budget of 2,500 casters.
                case ForestClass.Branch:
                    return lod == 0;
                default:                       // single clumps and grass
                    return false;
            }
        }

        /// <summary>Rocks, logs and stump patches: a mesh collider, and ContributeGI on LOD0 for the later probe bake.</summary>
        internal static bool IsSolidProp(ForestClass cls)
        {
            return cls == ForestClass.Boulder || cls == ForestClass.Log || cls == ForestClass.Stump;
        }

        /// <summary>
        /// screenRelativeTransitionHeight per level for an object of that size: the height on screen
        /// (as a share of the screen) at each "until" distance, clamped below 0.99 and strictly decreasing.
        /// </summary>
        internal static float[] ScreenHeights(float size, float[] untilDistances)
        {
            var heights = new float[untilDistances.Length];
            for (int i = 0; i < heights.Length; i++)
            {
                float distance = untilDistances[i];
                float height = float.IsPositiveInfinity(distance)
                    ? 0f
                    : size / (2f * distance * TanHalfFieldOfView) * LodDistanceScale;
                // A tall fir 12 m away is taller than the screen, and LODGroup rejects levels that do not fall.
                float ceiling = i == 0 ? MaxScreenHeight : heights[i - 1] * 0.9f;
                heights[i] = Mathf.Clamp(height, 0f, ceiling);
            }
            return heights;
        }

        /// <summary>
        /// The LODGroup size for an object of that largest side whose first level ends at that distance: the side itself,
        /// or less when the object would fill more than MaxFirstTransitionHeight of the screen there (see that constant).
        /// </summary>
        internal static float LodGroupSize(float objectSize, float firstUntilDistance)
        {
            if (!(firstUntilDistance > 0f) || float.IsPositiveInfinity(firstUntilDistance)) return objectSize;
            float cap = MaxFirstTransitionHeight * 2f * firstUntilDistance * TanHalfFieldOfView / LodDistanceScale;
            return Mathf.Min(objectSize, cap);
        }

        /// <summary>Changes whenever a number in this file changes, so stamped prefabs are rebuilt.</summary>
        internal static string Hash()
        {
            var text = new StringBuilder(ToolVersion);
            void Add(string name, object value)
            {
                text.Append('|').Append(name).Append('=').Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
            void AddAll(string name, float[] values)
            {
                for (int i = 0; i < values.Length; i++) Add(name + i, values[i]);
            }

            Add("generator", UseUnityGenerator);
            Add("crossFade", UseCrossFade);
            Add("byGroup", ThinByGroup);
            Add("core", CrownCoreFromLod);
            Add("distanceScale", LodDistanceScale);
            Add("logPitch", LogPitchDegrees);
            Add("triplanar", string.Join(",", ForceTriplanar ?? Array.Empty<string>()));
            Add("seed", Seed);

            AddAll("young", YoungFirDistances);
            AddAll("tall", TallFirDistances);
            AddAll("boulder", BoulderDistances);
            AddAll("log", LogDistances);
            AddAll("stump", StumpDistances);
            AddAll("branch", BranchDistances);
            AddAll("fernPatch", FernPatchDistances);
            AddAll("fernClump", FernClumpDistances);
            AddAll("grassPatch", GrassPatchDistances);
            AddAll("share", ScanPropShares);
            AddAll("woodCell", WoodClusterCells);
            Add("fade", FadeTransitionWidth);

            foreach (TreeSpec tree in Trees)
            {
                Add(tree.id, string.Join(",", tree.foliage));
                AddAll(tree.id + ".scale", tree.maxScale);
                Add(tree.id + ".wood", tree.scanWood);
                Add(tree.id + ".scan", tree.scanDistance);
            }
            foreach (PatchSpec patch in Patches)
            {
                Add(patch.id, string.Join(",", patch.clumps));
                Add(patch.id + ".far", string.Join(",", patch.farClumps));
            }

            AddAll("trunk", new[] { YoungFirClearance, TallFirClearance, TrunkBandMin, TrunkBandMax, MinTrunkRadius, TrunkCapsuleHeight });
            AddAll("thin", new[]
            {
                WoodBudgetShare, WeldQuantum, MinPieces, MaxPieceShare, SpatialPieceCell, KeyInnerWeight, BandHeight,
                KeyCentroidQuantum, GroupCell, FlattenMaxResidual, FlattenTubeCoherence, StickMaxScale, EnvelopeSectors, EnvelopeScaleStep, PieceGuardMinTriangles,
            });
            AddAll("core", new[] { CrownCoreBands, CrownCoreSectors, CrownCoreRadiusFraction, CrownCorePercentile });
            AddAll("cards", new[] { TwigCardsFromLevel, TwigCardFill, TwigCardNormalLift, ScanShadowThinnedLevel });
            AddAll("lodSize", new[] { MaxFirstTransitionHeight, TallFirLod0Factor, TallFirTrunkOnlyFromLevel, TallFirCrownWoodLevel, CrownBaseShare,
                YoungFirNoWoodFromLevel, SkirtUntilThinnedLevel, FarTrunkOneDraw ? 1 : 0 });
            Add("offAtlas", string.Join(",", OffAtlasMaterials) + "|" + OffAtlasFallback + "|" + OffAtlasDensityShare.ToString("R", CultureInfo.InvariantCulture));
            Add("skirt", string.Join(",", SkirtMaterials) + "|" + BranchesTakeOffAtlasBark + "|" + SkirtGroundLayer + "|" + SkirtMaxHeight.ToString("R", CultureInfo.InvariantCulture)
                + "|" + SkirtMinNormalY.ToString("R", CultureInfo.InvariantCulture));
            Add("tallCore", TallFirCrownCoreFromLevel + "|" + string.Join(",", CrownCoreTrees));
            foreach (TwigSprite sprite in SaplingTwigSprites)
                AddAll("sprite", new[] { sprite.uvMin.x, sprite.uvMin.y, sprite.uvMax.x, sprite.uvMax.y });
            foreach (TwigSprite sprite in SaplingShootSprites)
                AddAll("shoot", new[] { sprite.uvMin.x, sprite.uvMin.y, sprite.uvMax.x, sprite.uvMax.y });
            AddAll("shootCards", new[] { TwigCardMaxStretch, TallFirTwigCardsFromLevel, TwigCardRadialStretch });
            AddAll("coverage", new[] { CoverageMin(0), CoverageMin(1), CoverageMin(2), CoverageMax, CoverageNearDistance, CoverageScreenHeight });
            AddAll("collision", new[] { CollisionCell, CollisionMaxTriangles });
            AddAll("patch", new[]
            {
                FernPatchRadius, FernScaleMin, FernScaleMax, FernFarScale, GrassPatchRadius, GrassScaleMin, GrassScaleMax,
                GrassPatchMaxTriangles,
            });
            return ForestStamps.Hash(text.ToString());
        }

        // ---- Private ----

        static bool Has(string[] ids, string prefabId) => Array.IndexOf(ids, prefabId) >= 0;

        static float[] ClassDistances(ForestClass cls)
        {
            switch (cls)
            {
                case ForestClass.YoungFir: return YoungFirDistances;
                case ForestClass.TallFir: return TallFirDistances;
                case ForestClass.Boulder: return BoulderDistances;
                case ForestClass.Log: return LogDistances;
                case ForestClass.Stump: return StumpDistances;
                case ForestClass.Branch: return BranchDistances;
                case ForestClass.FernPatch: return FernPatchDistances;
                case ForestClass.FernClump: return FernClumpDistances;
                case ForestClass.GrassPatch: return GrassPatchDistances;
                default: throw new ArgumentException($"Class {cls} has no prefab in version 1.");
            }
        }

        // Thinned level 0 keeps the scanned wood. Farther levels add clustered wood that may be anything
        // from nothing to its budget, so the expected value is half the budget: the real total then stays
        // inside 10 % of this number whatever the wood turns out to be.
        static int ThinnedLevelTarget(string prefabId, int thinnedLevel)
        {
            TreeSpec tree = Tree(prefabId);
            if (thinnedLevel >= tree.foliage.Length) return 0;
            int wood = thinnedLevel == 0 ? tree.scanWood : WoodAt(prefabId, thinnedLevel) == WoodKind.None ? 0 : WoodBudget(prefabId, thinnedLevel) / 2;
            int core = CrownCoreAt(prefabId, thinnedLevel) ? CrownCoreTriangles : 0;
            return tree.foliage[thinnedLevel] + wood + core;
        }

        static int SourceTriangles(string prefabId)
        {
            ForestObjectEntry entry = ForestManifest.FindObjectForPrefab(prefabId);
            return entry != null ? entry.triangles : 0;
        }

        // The log's lower levels are authored. Their counts are known only once the FBX has been measured
        // and its nodes are in the manifest; until then the answer is "unknown".
        static int AuthoredLevelTriangles(string prefabId, int lod)
        {
            ForestObjectEntry first = ForestManifest.FindObjectForPrefab(prefabId);
            ForestAssetEntry owner = first != null ? ForestManifest.FindOwner(first) : null;
            if (owner == null || owner.fbxNodes == null || lod >= owner.fbxNodes.Length) return 0;
            ForestObjectEntry level = ForestManifest.FindObject(owner.fbxNodes[lod]);
            return level != null && level != first ? level.triangles : 0;
        }

        static int PatchTriangles(PatchSpec patch, int lod)
        {
            int total = 0;
            if (lod == 0)
            {
                foreach (string clump in patch.clumps) total += ClumpTriangles(clump);
            }
            else
            {
                foreach (int index in patch.farClumps) total += ClumpTriangles(patch.clumps[index]);
            }
            return total;
        }

        static int ClumpTriangles(string objectName)
        {
            ForestObjectEntry entry = ForestManifest.FindObject(objectName);
            return entry != null ? entry.triangles : 0;
        }
    }
}
