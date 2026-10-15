using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gamex.EditorTools
{
    /// <summary>
    /// Where everything in the forest is: the tile, the boundary of the playable core, the trail and
    /// the two game trails, the landmarks, the hand-placed set pieces and the capture viewpoints.
    /// Coordinates are world X (east) and Z (north) in metres with the origin at the centre of the
    /// tile; a <see cref="Vector2"/> holds (x, z). Every coordinate, density and seed of the world
    /// lives here, so retuning the layout never means hunting through the generators.
    /// </summary>
    public static class ForestLayout
    {
        // Declared first on purpose: the initialisers further down sample splines, and C# runs static
        // initialisers in the order they are written.
        static readonly object polylineLock = new object();
        static readonly Dictionary<Vector2[], Polyline> polylines = new Dictionary<Vector2[], Polyline>();

        public const int Seed = 1993;
        public const float TileSize = 256f, HeightRange = 60f, TerrainBaseY = -20f;
        public const float HalfExtentX = 65f, HalfExtentZ = 75f, CornerRadius = 30f;
        public const float CorridorClearance = 2.5f, CorridorAgentRadius = 2.0f, SpawnClearance = 5f;
        public static int TerrainLayerCount = 8;                      // 8; set 4 for the fallback
        public static int AlphamapResolution = 2048;                  // 2048; 1024 is the fallback
        public static readonly Vector2 SpawnXZ = new Vector2(0f, -62f);
        public const float SpawnYaw = 0f;

        public static readonly Vector2[]
            Trail =
            {
                new Vector2(0f, -66f), new Vector2(0f, -58f), new Vector2(7f, -42f), new Vector2(-6f, -24f),
                new Vector2(-10f, -6f), new Vector2(-2f, 12f), new Vector2(14f, 30f), new Vector2(10f, 48f),
                new Vector2(2f, 62f),
            },
            WestTrail =
            {
                new Vector2(-10f, -6f), new Vector2(-26f, 2f), new Vector2(-34f, 22f), new Vector2(-14f, 38f),
                new Vector2(10f, 48f),
            },
            EastTrail =
            {
                new Vector2(7f, -42f), new Vector2(26f, -36f), new Vector2(34f, -24f), new Vector2(38f, 2f),
                new Vector2(30f, 38f), new Vector2(14f, 30f),
            },
            Gully =
            {
                new Vector2(58f, 52f), new Vector2(44f, 26f), new Vector2(38f, 2f), new Vector2(34f, -16f),
            };

        public struct Landmark { public string name; public Vector2 xz; public float radius; }

        /// <summary>
        /// The set pieces of spec sections 3.4 and 3.5. <c>radius</c> is the circle the piece fits in.
        /// The names are also the children of Environment/Landmarks and the values of
        /// <see cref="ForestPlacement.landmark"/>.
        /// </summary>
        public static readonly Landmark[] Landmarks;

        public struct Viewpoint { public string id; public Vector3 position; public Vector3 euler; public float fieldOfView; public bool orthographic; public float orthographicSize; public bool heightIsAboveGround; }

        public static readonly Viewpoint[] Viewpoints;            // section 12.2

        // ---- Everything below is private to work package 3. "internal" only shares it between the package's files. ----

        internal const string SouthBank = "SouthBank", Deadfall = "Deadfall", FernHollow = "FernHollow",
            Clearing = "Clearing", BoulderField = "BoulderField", NorthOutcrop = "NorthOutcrop", SpawnView = "SpawnView";

        internal const int HeightmapResolution = 1025;
        internal const float CellSize = 32f;
        internal const int CellsPerSide = 8;

        // Terrain features (section 3.2).
        internal static readonly Vector2 ClearingCentre = new Vector2(-26f, 2f);
        internal static readonly Vector2 ClearingStumpWest = new Vector2(-31f, -4f);
        internal const float ClearingRadius = 13f, ClearingFlatRadius = 9f, ClearingFlatten = 0.6f, ClearingRimOuter = 18f;
        // D2 (density pass; the owner: "shrink the existing open areas to small glades", deliberate clearings come later): what stays
        // open of the clearing is a glade of this radius. The terrain keeps its flattened disc (ClearingRadius, ClearingFlatRadius);
        // trees, the thicket field, the grass swards and the fern rule use the glade. Its rim of thickets (ClearingRimThicket) runs
        // from the glade's edge ClearingRimWidth outward.
        // At 8.5 m the clearing camera (9.4 m from the centre) stood inside the rim's thicket, a fir's branches across a third of
        // its frame; at 10 m it stands in the glade's edge.
        internal const float ClearingOpenRadius = 10f, ClearingRimWidth = 5f;
        internal static readonly Vector2 HollowCentre = new Vector2(34f, -24f);
        internal const float HollowRadius = 11f, HollowDepth = 1.1f, HollowKeepOut = 4f;
        internal const float GullyDepth = 0.35f, GullyHalfWidth = 1.75f;
        internal const float TreadHalfWidth = 0.8f, ShoulderOuter = 2.0f, TrailCarveDepth = 0.06f, TrailFilterWindow = 12f;
        internal const float FloorRisePerMetre = 0.015f;
        internal const float BankRise = 16f, BankRiseWidth = 40f, BankRollAmplitude = 3f, BankRollWavelength = 45f;
        internal const float MaxTrailSlope = 12f, MaxCoreSlope = 27f;
        internal const float RootMoundMaxSlope = 12f, RootMoundMaxFootprintSlope = 16f, RootMoundMinHeight = 0.06f, RootMoundMaxHeight = 0.12f,
            RootMoundMinRadius = 1.0f, RootMoundMaxRadius = 1.4f;

        // Scatter rules shared by the planner and the painter (section 7).
        internal const float CoreInset = 1f;                 // every core pivot stays this far inside the boundary
        internal const float YoungFirTrailDistance = 3.5f;   // trunks to the main trail, so branches do not sweep the tread
        internal const float YoungFirBankBand = 20f, ThicketBankBand = 18f, ThicketBankFloor = 0.8f;
        internal const float TallFirBankFrom = 3f, TallFirBankTo = 45f;
        internal const float TreeMaxCoreSlope = 28f;
        internal const float FernPatchBankBand = 14f;

        // Densities and variation per class (section 7.2). Starting values: the counts they give are
        // logged by the planner and should land near 280 tall firs, 770 young firs, 90 rocks,
        // 8 logs, 10 stump patches, 65 branches, at most 900 fern patches and about 220 grass patches.
        // Section 7.2's minimum spacing of the core's tall firs: what the set pieces (the spawn's fir, the clearing's rim) keep
        // from scattered ones.
        internal const float TallFirCoreSpacing = 11f;
        // The overstory in groves (M4, second half; deviation from section 7.2's "minimum spacing 11 m", recorded in
        // build/issues/M4-overstory.md). At 11 to 18 m with groves only stretching the spacing, the 56 core firs came out as an
        // even lattice (nearest neighbour p10 12.4 m, p50 14.0 m): every tall fir a lone crown on open litter, so from the
        // trail the middle distance read as a heath with trees on it, the orchard finding the young firs had at M3. Now the
        // core holds groves where a grove noise is high, firs TallFirGroveSpacing apart so their crowns (about 6 m across)
        // meet, and lone firs TallFirLoneSpacing apart elsewhere. Groves stand on the open ground between the young-fir
        // thickets (the thicket field takes them out: TallFirGroveThicketCut), so the two layers alternate the way section 2
        // describes ("young firs ... stand in thickets between taller firs with bare lower trunks"). The count stays near
        // section 7.2's 60.
        //
        // Review 1 of the M4 captures (build/issues/M4-review1.md): with 83 core firs (44 a hectare, 52 in groves) the stand read as
        // parkland, lone lollipop firs on open litter, canopy closure about 15 % from above. Groves are larger (the noise threshold is
        // lower) and tighter (5.5 m: crowns about 6 m across meet), lone firs closer (16 m). At 5 m, 15 m and a threshold of 0.45 the
        // core held 152 firs and the frame was over budget nearly everywhere (measurement i2).
        //
        // Review 2 of the M4 captures (build/issues/M4-review2.md): still parkland, the overstory and the thickets segregated (the
        // 70 % cut kept groves off the thickets, so young firs never stood under tall ones, which is spec 2's picture). Groves now
        // stand over thickets (cut 0.35), are larger (threshold 0.44 to 0.56) and lone firs closer (13 m).
        //
        // D2, the density pass (build/issues/D2-density.md; the owner, 2026-10-15: "far too sparse", the view must be blocked at
        // short range): from about 80 to about 300 tall firs a hectare in the core. Groves cover most of the core (threshold 0.26 to
        // 0.40), their firs 3.9 m apart; lone firs stand 5.5 m apart, not 13; groves stand over thickets almost freely (cut 0.15).
        internal const float TallFirGroveSpacing = 3.9f, TallFirLoneSpacing = 5.5f, TallFirGroveNoiseWavelength = 30f;
        internal const float TallFirGroveFrom = 0.26f, TallFirGroveTo = 0.40f, TallFirGroveThicketCut = 0.15f;
        // The bank (section 7.2: spacing 8 m from 3 to 45 m outside, "kept with probability 1 - 0.5 d / 45"). With the backdrop
        // thinning outward, the upper slope and the crest were the barest part of the bank, and the boundary views looked up a
        // brown slope at a bare skyline (M4 second half, iterations g2 and g3). The thinning now runs the other way: half of the
        // firs on the foot of the bank, where the young-fir wall is the cover at eye level, all of them at the crest, where the
        // crowns make the skyline: kept with probability 1 - TallFirBankThinning (1 - d / 45). Same spacing and about the same
        // count as the spec's rule. Deviation recorded in build/issues/M4-overstory.md.
        internal const float TallFirBankSpacing = 8f, TallFirBankThinning = 0.5f;
        // Review 1 of the M4 captures: at one Poisson radius the bank's firs stood in rows along the boundary (overview_ortho). Each
        // point now asks for 0.75 to 1.3 times the spacing, by a hash of where it is.
        // Review 2: still two or three bands parallel to the boundary from above. The factor now follows a clump noise of
        // TallFirBankClumpWavelength (60 %) and the position hash (40 %), from 0.6 to 1.5 times the spacing: clumps and gaps.
        internal const float TallFirBankJitterLow = 0.6f, TallFirBankJitterHigh = 1.5f, TallFirBankClumpWavelength = 22f, TallFirBankClumpShare = 0.6f;
        // Scattered tall firs keep this far outside the circle of every landmark (section 3.4 and the first view of 3.5): those
        // are composed by hand, and a fir that the grove noise drops into one is chance, not composition. At iteration g3 two
        // landed within 9 m of the spawn camera and doubled the cost of the first view.
        internal const float TallFirLandmarkMargin = 3f;
        // D2 (density pass): with every landmark circle plus the margin closed to them, tall firs were kept off 22 % of the core.
        // The margin now holds for the composed views only (the first view and the deadfall); the clearing keeps its glade, the
        // hollow its wet bottom (TallFirHollowKeepOut), and the boulder field, the outcrop and the south bank take firs between their
        // rocks, kept off them by the solid rule.
        internal const float TallFirHollowKeepOut = 6.5f;
        // Review 2 (composition, medium): two cohorts of near-identical form. Spec 7.2 gives 0.90 to 1.15; 0.80 to 1.25 now.
        // D2: a mixed-age stand. A share of the core's firs (TallFirYoungShare) is a younger cohort at 0.62 to 0.82 (9 to 12 m for b
        // and c), the step between the pole-stage young firs (8 to 13 m) and the mature crowns.
        internal const float TallFirMinScale = 0.80f, TallFirMaxScale = 1.25f, TallFirMaxLean = 2f;
        internal const float TallFirYoungShare = 0.3f, TallFirYoungMinScale = 0.62f, TallFirYoungMaxScale = 0.82f;
        // Review 1 of the M4 captures: risk 1(d) for variant a, the spec's own fallback (section 14, risk 1; section 15). a is the
        // tree whose thinned levels enlarge most (LOD0 3.1, LOD1 4.8 times life size) and all three lenses read its crown as a
        // poplar or bamboo from 9 to 45 m and from below; it now stands on the bank only, where it is seen at 45 m and more. The
        // core holds b and c. c alone in the core (the spec's full 1(d)) was not taken: c is the half-dead variant (kit sheets:
        // a bare pole with a thin crown), and a core of it reads as a dying stand; c's share rises from 15 to 40 % instead
        // (composition lens: more snag-like, half-dead firs for an unmanaged stand). Spec 7.2 gives 45 : 40 : 15.
        internal static readonly float[] TallFirCoreMix = { 0f, 60f, 40f }, TallFirBankMix = { 50f, 45f, 5f };
        // Young firs (section 7.2: spacing lerp(7, 3, T) in the core, a band on the bank). At the spec's linear lerp,
        // with a jitter of up to 38 % on top, most of the core was the middle of the ramp: one fir every 4 to 6 m
        // everywhere, an orchard of single trees, and the "thickets" had their nearest neighbour at 3.5 to 4.6 m
        // (p10 to p50), so nothing was dense at eye level (M3 placement, build/issues/M3-placement.md). The spacing
        // now follows a steep ramp of the thicket field (YoungFirThicketFrom to To): inside a thicket the trunks
        // stand YoungFirThicketSpacing apart, the 3 m of the spawn thicket and of the spec, with little jitter; on
        // open ground they stand YoungFirOpenSpacing apart, loosely. The count stays near section 7.2's 360.
        //
        // Review 1 of the M3 captures: outside the thickets the firs stood as evenly spaced singles of one height on bare
        // litter (an orchard or a nursery), the thickets were near-lattices of 3 m, and nothing filled the band between
        // the ferns and the lowest whorl. Now (deviation from 7.2, build/issues/M3-review1.md):
        // - open ground holds clumps of a few firs about 3 m apart where a clump noise is high, and glades elsewhere;
        // - thickets are denser (2.5 m) and less regular (jitter 0.25);
        // - scale runs from 0.55 to 1.30: small at stand edges, in the clumps and on the foot of the bank, large inside
        //   thickets, and a share of every dense stand is understorey at 0.55 to 0.68 (its lowest whorl at about 0.6 m);
        // - firs lean up to 3 degrees; the heavy variant a is rarer (its scan level costs 1.6 times b's or c's).
        // Review 2 of the M4 captures: firs spread too evenly, each one see-through, so no stand closed into a mass. Thickets are
        // tighter (2.4 m) and the glades between clumps more open (32 m), so open ground is clearing, not an even scatter.
        // D2 (density pass): from about 290 to about 700 young firs a hectare. The open ground between stands holds a fir every 3.2 m
        // (understorey in the canopy gaps; was a glade of 32 m); thickets are 2.2 m and clumps 2.4 m apart (were 2.4 and 3.0); clumps
        // are more frequent (noise 0.52 to 0.66, was 0.69 to 0.78); the thicket field covers more of the core
        // (ForestTerrain.ThicketCentre) and its full spacing starts earlier (0.45 to 0.82, was 0.55 to 0.92).
        internal const float YoungFirGladeSpacing = 3.2f, YoungFirClumpSpacing = 2.4f, YoungFirThicketSpacing = 2.2f;
        internal const float YoungFirThicketFrom = 0.45f, YoungFirThicketTo = 0.82f;
        internal const float YoungFirClumpWavelength = 15f, YoungFirClumpFrom = 0.52f, YoungFirClumpTo = 0.66f;
        internal const float YoungFirOpenJitter = 0.35f, YoungFirThicketJitter = 0.25f;
        // The wall on the bank (sections 3.6 and 7.2): dense on the foot of the bank, where the player sees it from
        // the core, then thinning up the slope into the tall firs of M4. At the M2 rule (the thicket field floored at
        // 0.8) the band was one fir every 4.5 m from its first metre to its last: a plantation the eye saw through.
        // Review 1 of the M3 captures: at 3.0 m with a jitter of 0.12 it was still rows of single trunks with bare bank
        // between them. Now 2.5 m with a jitter of 0.25, opened by a noise into a few gaps instead of rows.
        internal const float YoungFirWallSpacing = 2.5f, YoungFirWallFootTo = 7f, YoungFirBankOuterSpacing = 8f, YoungFirWallJitter = 0.25f;
        internal const float YoungFirWallGapWavelength = 13f, YoungFirWallGapFrom = 0.62f, YoungFirWallGapTo = 0.74f, YoungFirWallGapStretch = 2.4f;
        // Review 1 of the M4 captures: looking out from 3 m inside the wall the eye went through the gaps up a bare slope, and nothing
        // said "impassable" (boundary_n/s/w). The gaps now open only beyond this many metres outside the boundary, so the foot of the
        // bank, where the player meets it, is a closed wall; the slope above keeps its gaps.
        internal const float YoungFirWallGapStart = 8f;
        // Young firs keep this far from the deadfall's centre (landmark radius 6 m plus 1.5 m). Review 1 of the M4 captures: two
        // firs 2 to 4 m in front of the deadfall camera hid the three logs, and fourteen needle-level firs made it the one view over
        // the frame budget (11.34 ms, 12.43 M triangles). Fallen trunks lie in a gap they opened.
        internal const float YoungFirDeadfallKeepOut = 7.5f;
        // D2 (density pass): the denser thicket field put young firs into the fern hollow's wet bowl, one 4 m in front of the
        // hollow_rim camera, hiding the mud and the ferns (captures/d2-i6). The bowl keeps them out to this distance from its centre.
        internal const float YoungFirHollowKeepOut = 8f;
        // Sightline breaks (review 1 of the M4 captures, composition lens): from the middle trail the eye ran across open floor to the
        // bank 60 to 100 m away, and nothing broke the view at 10 to 25 m; a stalking game needs cover that alternates along the
        // path. Every SightlineStationStep metres of trail, on alternating sides, the thicket field is raised to at least
        // SightlineStationThicket inside a disc SightlineStationRadius across whose centre stands SightlineStationLateral metres off the
        // centre line, so a clump of young firs grows there (ForestTerrain.ComputeThicket; the planner's own rules still apply).
        // A station is left out where it would fall in the clearing, a landmark, the bank band or on another trail.
        // Review 2 of the M4 captures: at every 27 m, one side, 3.6 m across, they left 10 to 30 m of open view along most of the
        // trail. Every 17 m now, still alternating sides, 4.5 m across. (Wider and nearer stations, 5.5 m across at 9.5 m, were
        // tried: the porous sight test of build/work/M4r2/scripts/sight2.py moved from 51 to 50 % of rays clear beyond 30 m for 22
        // more young firs near the trail views; not kept.)
        internal const float SightlineStationStart = 20f, SightlineStationStep = 17f, SightlineStationLateral = 11f;
        internal const float SightlineStationRadius = 4.5f, SightlineStationThicket = 0.9f;
        internal static readonly Vector2[] SightlineStations;
        // The clearing's rim (spec 7.2: thicket field times 1.5 on the 13 to 18 m ring). Review 1 of the M4 captures: its
        // surroundings were as open and bright as the clearing itself, so it did not read as a clearing. The field is now at least
        // ClearingRimThicket on the ring, except within ClearingRimTrailGap of a trail, so the trail and the west game trail still
        // enter through openings.
        internal const float ClearingRimThicket = 0.75f, ClearingRimTrailGap = 6f;
        internal const float YoungFirMinScale = 0.55f, YoungFirMaxScale = 1.30f;
        internal const float YoungFirEdgeMinScale = 0.65f, YoungFirEdgeMaxScale = 0.95f, YoungFirInnerMinScale = 0.95f;
        // Review 2: 30 to 40 % understorey in dense stands, to fill the band under the lowest whorls.
        internal const float YoungFirUnderstoreyShare = 0.4f, YoungFirUnderstoreyMaxScale = 0.68f;
        // D2: on the open ground between stands (neither thicket nor clump), most firs are understorey in the gaps of the canopy.
        internal const float YoungFirGapUnderstoreyShare = 0.6f;
        // Review 2 (risk 1(e) taken as a cohort): a share of the core's young firs inside stands grows to pole stage, 1.40 to 1.55
        // times (8 to 13 m), the missing layer between the saplings and the tall firs' crowns. A LODGroup's distances grow with the
        // instance's scale, so such a fir draws its needle level to 20 m: 8 % of the eligible firs, not the 12 % first tried.
        internal const float YoungFirMidShare = 0.08f, YoungFirMidMinScale = 1.40f, YoungFirMidMaxScale = 1.55f, YoungFirMidInterior = 0.35f;
        // The bank (review 2: a nursery of one size and spacing on a bare slope): the scale follows a clump noise as well as the
        // distance up the slope, and beyond the wall's foot the spacing opens and closes with the same noise.
        internal const float YoungFirBankClumpWavelength = 11f, YoungFirBankClumpOpen = 1.5f, YoungFirBankClumpClosed = 0.8f;
        internal const float YoungFirMaxLean = 3f;
        // No scattered young fir's trunk within this of a first-person capture camera: its crown would fill the lens.
        // Review 1 of the M4 captures: 1.8 m let a b fir at scale 1.19 stand 2.1 m behind boundary_n, and its lowest branch crossed
        // the bottom of the frame as a row of blades. A crown reaches about 1.5 m times the scale.
        // D2 (density pass): 3.0 m. In the denser stand a fir 2.6 m beside the deadfall camera put a branch across a quarter of the
        // frame.
        internal const float YoungFirViewpointGap = 3.0f;
        internal static readonly float[] YoungFirMix = { 15f, 42.5f, 42.5f };
        // Understorey firs are mostly c, the shortest variant (5.9 m, so 3.2 to 4.0 m at their scale), never the heavy a.
        internal static readonly float[] YoungFirUnderstoreyMix = { 0f, 30f, 70f };
        internal const int BoulderClusters = 7, BoulderClusterMin = 3, BoulderClusterMax = 7, BoulderSingles = 14, BoulderTrailRocks = 6;
        internal const float BoulderClusterCentreSpacing = 28f, BoulderTrailLength = 30f;
        internal const float BoulderMinScale = 0.8f, BoulderMaxScale = 1.3f, BoulderMaxTilt = 15f;
        // A cluster (section 7.2: "so they overlap into one mass"). At a fixed 1.8 m between pivots, whatever
        // the rocks' sizes, every cluster came out as a row of similar stones at even gaps, like teeth on the
        // horizon: nearest neighbours 1.8 to 2.3 m, largest-to-smallest scale at most 1.42, six of seven
        // clusters chains with a principal-axis ratio of 2.3 to 4.0 (review 2 of the M2 captures). Now each is
        // built around one large anchor, with two or three rocks touching it and two small satellites on its
        // downhill side; rocks are spaced by their measured rims (RockLips), not by a constant, and a rock that
        // would stretch the cluster beyond BoulderClusterMaxAxisRatio is refused. The satellites are smaller
        // than spec 7.2's 0.8 (a deviation, recorded in issues/M2-review2.md).
        internal static readonly string[] BoulderClusterAnchorsSet01 = { "rock_moss_set_01_rock01", "rock_moss_set_01_rock02", "rock_moss_set_01_rock04" };
        internal static readonly string[] BoulderClusterAnchorsSet02 = { "rock_moss_set_02_rock10", "rock_moss_set_02_rock11", "rock_moss_set_02_rock12" };
        internal const float BoulderAnchorMinScale = 1.2f, BoulderAnchorMaxScale = 1.3f;
        internal const float BoulderMemberMinScale = 0.9f, BoulderMemberMaxScale = 1.1f;
        internal const float BoulderSatelliteMinScale = 0.55f, BoulderSatelliteMaxScale = 0.75f;
        internal const int BoulderMembersMin = 2, BoulderMembersMax = 3, BoulderSatellites = 2;
        // Two rocks of a cluster stand with their rims overlapping by this share of the sum of the two rims
        // facing each other, so they read as piled, not as set side by side.
        internal const float BoulderTouch = 0.88f, BoulderClusterMaxAxisRatio = 1.8f;
        internal const float BoulderFieldMinScale = 1.3f, BoulderFieldMaxScale = 1.6f, BoulderFieldTallMinScale = 1.7f, BoulderFieldTallMaxScale = 1.8f;
        internal const int BoulderFieldTallRocks = 4;        // rock04 and rock11 only, the anchor included
        // Review 1 of the M4 captures: 67 dry branches in 1.87 ha left the litter between trunks bare (an even-aged plantation, not a
        // wild stand). Branches are cheap (no shadow beyond 10 m, culled at 35 m): 80 scattered, under the tall firs and inside the
        // young-fir thickets (ScatteredBranchesInThickets of them), where the composition lens asked for deadwood at crouch height.
        // Review 2: dry branches on the bank as well (BankBranches), which was bare soil.
        // D2 (density pass: "more fallen logs, branches and stumps"): 5 logs beside the game trails as before, and ForestLogs more
        // anywhere in the core at least LogSpacing apart, at 0.9 to 1.5 (a shorter, younger windthrow among the old trunks); 20 stump
        // patches 9 m apart (was 7 at 12 m); 220 branches, 70 of them in thickets, 40 on the bank.
        internal const int ScatteredLogs = 5, ForestLogs = 14, ScatteredStumps = 20, ScatteredBranches = 220, ScatteredBranchesInThickets = 70, BankBranches = 40;
        internal const float BankBranchOuter = 16f, LogSpacing = 9f, ForestLogMinScale = 0.9f;
        internal const float LogMinScale = 1.2f, LogMaxScale = 1.5f, StumpMinScale = 0.9f, StumpMaxScale = 1.15f, StumpSpacing = 9f;
        internal const float BranchMinScale = 0.9f, BranchMaxScale = 1.3f;
        // Review 1 of the M4 captures: the cap of 900 was full, so the thicket edges had no ferns (the bottom metre of a stand was
        // bare stems over bare litter, poor concealment at crouch height) and nothing grew where the bank meets the core. The cap is
        // 1,050; the extra demand is a band of ferns along the stand edges (FernThicketEdgeDensity), so the open drifts keep theirs.
        // Review 2 of the M4 captures: the bank had no ground cover (fern patches stopped 6 m out). Patches now reach
        // FernPatchBankBand up the bank in drifts of their own (BankFernDensity), and the cap is 1,250 so the core keeps its drifts.
        // D2 (density pass: "about twice the fern coverage, carpets where it is shady"): the cap is 2,300 and the field gains a carpet
        // term under the tall firs' crowns (FernShade*: the shade of the tall firs within FernShadeReach, summed with a Gaussian of
        // FernShadeSigma, from FernShadeFrom to FernShadeTo). The clump cap follows the ring rule's more logs and stumps.
        internal const int FernPatchCap = 2300, FernClumpCap = 800;
        internal const float FernShadeDensity = 0.7f, FernShadeReach = 6f, FernShadeSigma = 3f, FernShadeFrom = 0.6f, FernShadeTo = 1.6f;
        internal const float BankFernDensity = 0.5f, BankFernWavelength = 11f, BankFernFrom = 0.42f, BankFernTo = 0.6f;
        // The band of the thicket field where a stand ends: young firs stand dense from about 0.55 on (YoungFirThicketFrom).
        internal const float FernThicketEdgeDensity = 0.55f, FernThicketEdgeFrom = 0.40f, FernThicketEdgeTo = 0.66f;
        internal const float FernPatchMinScale = 0.85f, FernPatchMaxScale = 1.5f, FernClumpMinScale = 0.8f, FernClumpMaxScale = 1.6f;
        internal const float FernNoiseWavelength = 14f, FernMaxDensity = 1.6f, FernMinDensity = 0.05f;
        // How the fern field is brought under its cap (ForestScatter.FernThinning): its density is scaled
        // down to this share at most; beyond that the drifts are made smaller instead of thinner, by
        // raising the noise threshold by up to FernMaxNarrowing. At the 0.36 of M2 the core of a drift had
        // patches 1.7 to 2.3 m apart: lone rosettes with bare ground between them, nothing that read as a
        // drift (review 1). At 0.8 a core has them 1.1 to 1.3 m apart, closer than a patch is wide, and the
        // cap is met with fewer and smaller drifts. As the threshold rises the drifts' edges also get
        // steeper (FernDriftEdge to FernDriftNarrowEdge of noise), so a small drift still has a full core.
        internal const float FernThinningFloor = 0.95f, FernMaxNarrowing = 0.9f;
        internal const float FernDriftEdge = 0.25f, FernDriftNarrowEdge = 0.10f;
        // Spacing inside a drift (section 7.2's clamp(1.1 / sqrt(D), 0.9, 6)). With the 0.9 m floor a drift
        // core was a saturated Poisson run of one radius: nearest neighbour 1.31 m at p10 and 1.62 at p50, half
        // of them inside a 0.3 m band, so beyond 14 m drifts read as single rosettes at even intervals (review
        // 2). The floor is lower and each sample's radius is drawn between FernSpacingJitterLow and High times
        // the rule's, so crowns overlap in drift cores and the spacing is not a lattice. The solver then spends
        // the cap of 900 on fewer, fuller drifts (FernMaxNarrowing 0.6 to 0.9).
        internal const float FernSpacingMin = 0.55f, FernSpacingJitterLow = 0.6f, FernSpacingJitterHigh = 1.4f;
        // Patch scale follows the density: large where it reaches FernCoreDensity (a drift's core, a collar, the
        // hollow), small at FernEdgeDensity and below (review 2: "all one size"). Section 7.2 gives 0.85 to 1.5
        // for every patch (a deviation, recorded). The thinning floor is 0.95 (was 0.8): the cap is met by
        // fewer drifts at nearly full density, not by thinner ones.
        internal const float FernCoreMinScale = 1.25f, FernCoreMaxScale = 1.65f, FernEdgeMinScale = 0.75f, FernEdgeMaxScale = 1.0f;
        internal const float FernEdgeDensity = 0.25f, FernCoreDensity = 1.0f;
        // The fern hollow (section 3.4: "mud and the densest ferns"). Its moisture only multiplied the drift
        // noise, so where the noise was low the hollow had one patch per 20 m2 (review 1). It now has a floor
        // of its own that the cap does not thin: a ring around the muddy bottom, full to FernHollowFull
        // metres from the centre and fading to the rim.
        // Review 1 of the M4 captures: the bare mud disc in the middle read as a flat pad; the ferns now spill further down the sides
        // (inner edge of the ring 2.2 to 1.2 m).
        internal const float FernHollowDensity = 0.85f, FernHollowInner = 1.2f, FernHollowFull = 8f;
        // The "+0.8 within 1.5 m of rocks, stumps and logs" of section 7.2 put the same even collar of
        // ferns around every one of 107 pieces, which read as a rule (review 1), and took 41 % of the
        // patches. A collar now grows on this share of the pieces, on one side of each (an arc this wide
        // around a bearing drawn from the piece's position), and everywhere a drift touches a piece.
        internal const float FernCollarDensity = 0.8f, FernCollarShare = 0.5f, FernCollarArc = 200f;
        // Ferns on the trail's shoulders, in runs: "litter and ferns creep over its edges" (section 2).
        internal const float FernShoulderDensity = 0.6f, FernShoulderInner = 1.0f, FernShoulderOuter = 2.6f, FernShoulderWavelength = 8f;
        // Grass (section 7.2: "patches on 25 % of the clearing"). The M2 plan spread 200 patches thinly over
        // half the disc (one per 1.3 m2), which read as bare ground with stalks on it. They now stand in a
        // few swards: where the noise passes GrassSwardThreshold, and around the clearing's two stump
        // patches, at GrassClearingSpacing inside (closer than a patch is wide, so tussocks touch) and
        // nothing between. GrassClearingCap keeps the class at the count of section 7.2 (about 220 with the
        // shoulder patches); at that count and this spacing the swards cover about 15 % of the disc, not 25:
        // a quarter of the disc at one patch per 0.65 m2 read as scattered tufts, not as grass.
        //
        // Review 2: one Poisson radius of 0.52 m over every sward is a lattice (nearest neighbour 0.53 m at p10,
        // 0.56 at p50, 0.62 at p75): planted seedlings in rows. The radius now follows the sward noise, from
        // GrassSpacingCore where the sward is thickest to GrassSpacingEdge at its rim, with a jitter per sample,
        // and the patches vary in size (section 7.2 gives 0.8 to 1.3, a recorded deviation). When the swards ask
        // for more patches than the cap, their threshold is raised (smaller swards, full cores) instead of
        // thinning them evenly.
        internal const float GrassMinScale = 0.6f, GrassMaxScale = 1.6f, GrassShoulderSpacing = 4f, GrassNoiseWavelength = 9f;
        internal const float GrassSpacingCore = 0.32f, GrassSpacingEdge = 0.9f, GrassSpacingJitter = 0.25f, GrassSwardCoreSpan = 0.16f;
        internal const float GrassSwardThreshold = 0.74f, GrassSwardThresholdStep = 0.015f, GrassSwardStumpBonus = 0.30f, GrassSwardStumpReach = 4.0f;
        // D2: the clearing is a glade of ClearingOpenRadius, 59 % of its old area; the cap follows.
        internal const int GrassClearingCap = 120;
        internal const float SameVariantRadius = 8f, SameVariantYawGap = 40f;

        // Grounding (section 7.3): how far each class sinks its pivot into the ground.
        internal const float TreeSink = 0.10f, RockSink = 0.08f, RockTiltTakenUp = 15f, GroundCoverSink = 0.02f;
        // The rock rule of section 7.3 beds a rock on the ground at its pivot "plus the drop of the ground
        // across the footprint", and takes that drop from the slope alone. On the floor's mounds (0.35 m at
        // 9 m) the ground also curves away under a large rock: the rim of a 1.75-scale boulder stood up to
        // two decimetres clear, and the scan's dark underside showed as a knife-edged ledge (review 1,
        // rock_close). The planner now measures the drop under the rim (a ring at this share of the bounding
        // circle, against the plane the rock is tilted to) and sinks the rock by it plus RockRimBury, but
        // never by more than RockRimMaxSink times its scale, so the two tall shapes keep their height.
        // Since review 2 the soil collars (RockCollar*, below) raise the ground where it falls away under a rim,
        // which keeps the rock's height; sinking the whole rock for the same reason took 0.1 to 0.35 m off the
        // cluster rocks (build/captures/m2-r2/look/clusters). The rim sink is now only a light bedding.
        internal const float RockRimShare = 0.6f, RockRimBury = 0.03f, RockRimMaxSink = 0.04f;
        // A dry branch rests on its lowest twigs. Raised until they were 2 cm in the litter, the arched scans
        // stood clear of the ground along their middle (review 1); 5 cm beds them. At 5 cm the arch of branch
        // _a still showed daylight under its middle (review 2): it now also settles by BranchSettle of the depth
        // its scan reaches below its pivot, as a branch that has lain a season does.
        internal const float BranchSink = 0.05f, BranchSettle = 0.3f;

        // Soil collars under the rocks (a deviation from spec 6.2, recorded in issues/M2-review2.md). Several
        // scans recede below a lip (rock11 0.17 to 0.55 m above its pivot plane on most sides, rock10, rock12 and
        // rock13 on some): the scan's blurred, poorly photographed underside. Where the ground falls away
        // beside a rock that underside stood clear as a dark faceted band (review 2, rock_close and the spawn
        // boulders). The sink of section 7.3 only puts the lowest vertex under the ground and cannot reach a lip
        // that high without burying a third of the rock. So at step 8 the ground is raised against each rock
        // until it covers the lip by RockCollarCover, on the sides where the lip stands above it: up to the lip
        // at the rim, falling off outward with a raised cosine no steeper than RockCollarGradient allows above
        // the ground's own slope. Where a lip is too high for that ramp, the ground under the whole rock is
        // raised as well (a plateau, at most RockCollarMaxPlateau times the scale) and the rock's sink grows by
        // the same amount, so the rock stays exactly where the planner put it and only the soil comes up. The
        // plateau is hidden under the rock; outside the rim the soil rises only on the sides where the ground had
        // fallen away below the lip, which gives back the height the rock would show on level ground. The soil
        // covers a lip up to RockCollarMaxLip (times the scale) above the pivot plane: every measured lip but one
        // bearing of rock11 (0.55 m). With a cover of 0.10 and then 0.18 the foot of rock11's recess still showed
        // in rock_close (build/captures/m2-r2/iterations/it01full and it02).
        internal const float RockCollarCover = 0.03f, RockCollarMaxLip = 0.35f, RockCollarMaxPlateau = 0.25f, RockCollarMinWidth = 0.5f, RockCollarMaxWidth = 2.5f;
        // The slope a collar may reach, as a target below the core limit of 27 degrees; RockCollarTrailFrom/To
        // keep the tread untouched.
        internal const float RockCollarTargetSlope = 23f, RockCollarTrailFrom = 1.3f, RockCollarTrailTo = 2.2f;

        /// <summary>
        /// Measured from the FBX geometry of the two moss-rock sets (build/tools/measure/rock_lip.py, in Unity
        /// space with the importer's X mirror, scale 1): for 16 bearings around the pivot (0 = local +Z, then
        /// clockwise seen from above in 22.5 degree steps, 90 = local +X), how far the rock reaches
        /// (<c>rimRadius</c>) and the lowest height above the pivot plane at which it reaches within 5 % of that
        /// (<c>lipHeight</c>). Below the lip the rock recedes.
        /// </summary>
        internal struct RockLip { public float[] lipHeight, rimRadius; }

        internal static readonly Dictionary<string, RockLip> RockLips = new Dictionary<string, RockLip>
        {
            { "rock_moss_set_01_rock01", new RockLip { lipHeight = new[] { -0.33f, -0.37f, -0.57f, -0.65f, -0.53f, -0.37f, -0.35f, -0.33f, -0.03f, 0.11f, 0.13f, 0.09f, -0.35f, -0.39f, -0.39f, -0.39f }, rimRadius = new[] { 1.67f, 1.34f, 1.19f, 1.17f, 1.11f, 1.31f, 1.61f, 1.71f, 1.70f, 1.60f, 1.35f, 1.01f, 1.12f, 1.61f, 1.89f, 1.90f } } },
            { "rock_moss_set_01_rock02", new RockLip { lipHeight = new[] { -0.40f, -0.38f, -0.38f, -0.38f, -0.44f, -0.46f, -0.28f, -0.28f, -0.24f, -0.24f, -0.14f, -0.36f, -0.40f, -0.40f, -0.40f, -0.40f }, rimRadius = new[] { 1.62f, 1.30f, 0.80f, 1.02f, 1.27f, 1.39f, 1.67f, 1.79f, 1.64f, 1.26f, 0.72f, 0.88f, 1.38f, 1.78f, 1.95f, 1.85f } } },
            { "rock_moss_set_01_rock03", new RockLip { lipHeight = new[] { -0.26f, -0.24f, -0.26f, -0.30f, -0.38f, -0.30f, -0.20f, -0.26f, -0.28f, -0.26f, -0.22f, -0.22f, -0.16f, -0.24f, -0.26f, -0.28f }, rimRadius = new[] { 1.06f, 1.30f, 1.41f, 1.30f, 1.02f, 1.10f, 1.16f, 1.08f, 1.02f, 1.06f, 1.07f, 0.97f, 1.09f, 1.27f, 1.31f, 1.16f } } },
            { "rock_moss_set_01_rock04", new RockLip { lipHeight = new[] { -0.13f, -0.09f, -0.33f, -0.45f, -0.37f, -0.61f, -0.59f, -0.53f, -0.45f, -0.49f, -0.17f, -0.13f, -0.17f, -0.13f, -0.45f, -0.11f }, rimRadius = new[] { 1.08f, 1.06f, 1.07f, 1.01f, 0.95f, 1.07f, 1.13f, 1.05f, 0.89f, 0.98f, 1.20f, 1.26f, 1.18f, 1.08f, 0.97f, 1.06f } } },
            { "rock_moss_set_01_rock05", new RockLip { lipHeight = new[] { 0.02f, 0.04f, 0.12f, 0.16f, -0.16f, -0.16f, -0.16f, -0.36f, -0.36f, -0.30f, -0.30f, -0.30f, -0.32f, -0.26f, -0.12f, -0.04f }, rimRadius = new[] { 1.65f, 1.57f, 1.35f, 1.05f, 0.86f, 1.10f, 1.24f, 1.29f, 1.35f, 1.37f, 1.42f, 1.28f, 0.96f, 0.82f, 1.24f, 1.53f } } },
            { "rock_moss_set_01_rock06", new RockLip { lipHeight = new[] { -0.36f, -0.32f, -0.32f, -0.32f, -0.32f, -0.30f, 0.28f, -0.24f, -0.24f, -0.22f, -0.20f, -0.20f, -0.10f, -0.12f, -0.20f, -0.34f }, rimRadius = new[] { 1.28f, 1.18f, 0.96f, 1.12f, 1.20f, 1.10f, 0.90f, 1.23f, 1.48f, 1.53f, 1.36f, 0.99f, 0.93f, 1.07f, 1.22f, 1.30f } } },
            { "rock_moss_set_02_rock07", new RockLip { lipHeight = new[] { -0.21f, -0.11f, -0.11f, -0.15f, -0.17f, -0.19f, -0.19f, -0.21f, -0.25f, -0.19f, -0.17f, -0.17f, -0.17f, -0.25f, -0.25f, -0.27f }, rimRadius = new[] { 0.60f, 0.77f, 0.87f, 0.85f, 0.89f, 0.82f, 0.81f, 0.77f, 0.61f, 0.75f, 0.88f, 0.91f, 0.80f, 0.71f, 0.74f, 0.67f } } },
            { "rock_moss_set_02_rock08", new RockLip { lipHeight = new[] { 0.10f, 0.10f, 0.12f, 0.12f, 0.02f, -0.00f, -0.00f, -0.00f, 0.04f, 0.02f, 0.02f, -0.00f, -0.00f, -0.00f, 0.04f, 0.04f }, rimRadius = new[] { 0.54f, 0.66f, 0.69f, 0.63f, 0.53f, 0.62f, 0.64f, 0.56f, 0.44f, 0.43f, 0.52f, 0.57f, 0.67f, 0.70f, 0.68f, 0.58f } } },
            { "rock_moss_set_02_rock09", new RockLip { lipHeight = new[] { 0.01f, 0.09f, 0.09f, 0.09f, 0.07f, 0.01f, 0.01f, -0.01f, -0.01f, 0.09f, 0.03f, 0.03f, -0.03f, -0.03f, -0.03f, 0.11f }, rimRadius = new[] { 0.49f, 0.67f, 0.83f, 0.87f, 0.78f, 0.60f, 0.54f, 0.55f, 0.53f, 0.66f, 0.82f, 0.89f, 0.85f, 0.75f, 0.56f, 0.44f } } },
            { "rock_moss_set_02_rock10", new RockLip { lipHeight = new[] { -0.01f, -0.01f, 0.01f, 0.15f, 0.15f, 0.17f, 0.09f, 0.09f, 0.21f, 0.27f, 0.35f, 0.23f, 0.17f, 0.05f, 0.03f, -0.01f }, rimRadius = new[] { 0.77f, 0.97f, 1.04f, 1.04f, 1.06f, 1.05f, 1.01f, 0.93f, 0.78f, 0.66f, 0.85f, 1.03f, 1.14f, 1.09f, 0.94f, 0.70f } } },
            { "rock_moss_set_02_rock11", new RockLip { lipHeight = new[] { 0.29f, 0.25f, 0.25f, 0.17f, 0.17f, 0.27f, 0.27f, 0.25f, 0.19f, 0.13f, -0.01f, 0.03f, 0.17f, 0.55f, 0.31f, 0.31f }, rimRadius = new[] { 0.58f, 0.70f, 0.77f, 0.92f, 0.99f, 0.98f, 1.07f, 1.01f, 0.95f, 1.13f, 1.19f, 1.15f, 0.97f, 0.89f, 0.76f, 0.67f } } },
            { "rock_moss_set_02_rock12", new RockLip { lipHeight = new[] { 0.16f, 0.14f, 0.14f, 0.16f, 0.18f, 0.08f, 0.12f, 0.18f, 0.12f, 0.12f, 0.10f, 0.08f, -0.00f, 0.02f, 0.10f, 0.32f }, rimRadius = new[] { 0.72f, 0.84f, 0.87f, 0.84f, 0.76f, 0.63f, 0.60f, 0.68f, 0.73f, 0.84f, 0.94f, 0.93f, 0.82f, 0.77f, 0.66f, 0.62f } } },
            { "rock_moss_set_02_rock13", new RockLip { lipHeight = new[] { -0.37f, 0.25f, 0.25f, 0.09f, -0.01f, -0.13f, -0.29f, -0.35f, -0.61f, -0.47f, -0.45f, -0.43f, -0.45f, -0.45f, -0.43f, -0.45f }, rimRadius = new[] { 0.05f, 0.34f, 0.64f, 0.86f, 1.08f, 1.16f, 1.10f, 1.15f, 1.13f, 1.10f, 0.97f, 1.01f, 1.01f, 0.88f, 0.63f, 0.31f } } },
        };

        internal const int RockLipBearings = 16;

        /// <summary>How far a measured rock reaches toward a world bearing, at its scale and yaw; negative when it has no measured rim.</summary>
        internal static float RockRimRadius(string prefabId, float scale, float yaw, float worldBearing)
        {
            if (prefabId == null || !RockLips.TryGetValue(prefabId, out RockLip lip)) return -1f;
            float local = Mathf.Repeat(worldBearing - yaw, 360f) / (360f / RockLipBearings);
            int low = Mathf.FloorToInt(local) % RockLipBearings, high = (low + 1) % RockLipBearings;
            return Mathf.Lerp(lip.rimRadius[low], lip.rimRadius[high], local - Mathf.Floor(local)) * scale;
        }

        /// <summary>The yaw of a placed object: the compass bearing its local +Z points to.</summary>
        internal static float YawOf(Quaternion rotation)
        {
            Vector3 forward = rotation * Vector3.forward;
            return Bearing(new Vector2(forward.x, forward.z));
        }

        /// <summary>The rim of a placed rock in the world: per measured bearing, its horizontal offset from the pivot and the lip's height above the pivot.</summary>
        internal static bool TryRockRim(ForestPlacement placement, out Vector3[] rim)
        {
            rim = null;
            if (placement.cls != ForestClass.Boulder || placement.prefabId == null || !RockLips.TryGetValue(placement.prefabId, out RockLip lip)) return false;
            rim = new Vector3[RockLipBearings];
            for (int k = 0; k < RockLipBearings; k++)
            {
                float radians = k * (2f * Mathf.PI / RockLipBearings);
                var local = new Vector3(Mathf.Sin(radians) * lip.rimRadius[k], lip.lipHeight[k], Mathf.Cos(radians) * lip.rimRadius[k]) * placement.scale;
                rim[k] = placement.rotation * local;
            }
            return true;
        }

        // Ground dressing at the foot of a rock (section 7.2's ring rule, extended to the boulder field, the
        // outcrop and the cluster anchors, review 2): fern clumps touching the contact line.
        internal const int RockRingClumpsMin = 2, RockRingClumpsMax = 3;
        // Fern clumps on the foot of the bank and in the clearing, where one ground layer otherwise ran for tens
        // of metres with nothing over it (risk 13's fallback, review 2). Section 7.2 puts single clumps only in
        // the ring rule and the spawn composition: a recorded deviation.
        internal const int BankFernClumps = 120, ClearingFernClumps = 30;
        internal const float BankClumpOuter = 9f, BankClumpSpacing = 2.2f;
        // A log is rigid and up to 6 m long; the floor has mounds of 0.35 m at 9 m. Where the ground 1.5 m
        // (times scale) along its axis lies this far above or below the ground at its pivot, a log would be
        // buried in the middle with an end in the air (one of eight was, review 1), so it is not put there.
        internal const float LogBedReach = 1.5f, LogBedTolerance = 0.15f;
        internal const float StumpMinSink = 0.04f, StumpMaxSink = 0.08f, LogMinSink = 0.22f, LogMaxSink = 0.26f;

        // The physical edge (section 3.6).
        internal const float WallOffset = 1.5f, WallHeight = 8f, WallThickness = 1f;
        internal const int WallChordsPerCorner = 5;

        internal const float WaypointSpacing = 10f;
        internal const float EyeHeight = 1.65f, CrouchEyeHeight = 0.95f, SpawnLift = 0.05f;

        /// <summary>One hand-placed piece of sections 3.4 and 3.5. The planner still validates each one.</summary>
        internal struct SetPiece
        {
            public string landmark;
            public ForestClass cls;
            public string prefabId;       // null: the planner draws one from the class
            public Vector2 xz;
            public float minScale, maxScale;
            public float yaw;             // NaN: the planner draws one
        }

        // The spawn thicket in trail coordinates (metres along the trail from the spawn, metres to the right).
        // Every pair is at least 3 m apart and every trunk at least 3.6 m left of the centre line.
        static readonly Vector2[] SpawnThicket =
        {
            new Vector2(6.3f, -3.9f), new Vector2(9.5f, -3.7f), new Vector2(12.4f, -5.0f),
            new Vector2(7.6f, -6.9f), new Vector2(10.6f, -7.4f),
        };
        // Two understorey firs at the front of the spawn thicket, in the gaps of its first row (review 1 of the M3
        // captures: at crouch height the view passed under the five firs' lowest whorls to the trail and the boulders,
        // and the first view's branches did not "sweep down into fern drifts"). About 2 m from the crouch_hide camera.
        // Spec 3.5 lists five trees; these two are a recorded deviation. Placed from the young firs' own random stream, so
        // the set pieces and the boulder field, which share the landmark stream, do not move.
        internal static readonly Vector2[] SpawnThicketUnderstorey = { new Vector2(7.9f, -3.6f), new Vector2(11.0f, -4.0f) };
        internal const float SpawnUnderstoreyMinScale = 0.58f, SpawnUnderstoreyMaxScale = 0.64f;
        static readonly Vector2 SpawnTallFir = new Vector2(14f, -3.0f);
        // Review 2 of the M4 captures (composition lens, trail_start): the thicket read as three or four separate open saplings on
        // bare litter, the ferns as single tufts, the background as leafy card levels, and canopy_up as one crown against open sky.
        // Spec 3.5 lists five firs, two boulders, one tall fir and fern patches; these are recorded additions to that composition:
        // - a second rank of three young firs behind the thicket, larger, so the crowns close into one dark mass;
        // (Every fir here is drawn by trail_start, the view Budget_FrameTime asserts; with the first view's own firs switched off
        // that view measured 1.1 ms less. A fourth second-rank fir, a third fir right of the trail and a b canopy fir behind the
        // thicket were built and taken out again to keep the frame budget: build/issues/M4-review2.md.)
        static readonly Vector2[] SpawnSecondRank =
        {
            new Vector2(8.4f, -10.6f), new Vector2(13.2f, -9.6f), new Vector2(15.4f, -6.8f),
        };
        internal const float SpawnSecondRankMinScale = 1.05f, SpawnSecondRankMaxScale = 1.20f;
        // - three young firs right of the trail behind the boulders, framing the view;
        static readonly Vector2[] SpawnRightFirs = { new Vector2(13.5f, 7.5f), new Vector2(16.8f, 7.8f) };
        // - a fern drift under the thicket (patches 1.2 to 1.6 m apart);
        static readonly Vector2[] SpawnFernDrift =
        {
            new Vector2(11.6f, -6.3f), new Vector2(12.8f, -7.4f), new Vector2(14.1f, -6.1f), new Vector2(11.0f, -9.0f), new Vector2(12.4f, -8.8f),
        };
        // - one more tall fir beside the spawn's, so canopy_up looks into overlapping crowns (c beside the camera);
        static readonly Vector2 SpawnCanopyFirC = new Vector2(15.5f, 3.5f);
        // - two b firs on the axis 30 to 35 m out, off the trail, so pale trunks recede into the mist.
        static readonly Vector2[] SpawnAxisFirs = { new Vector2(31f, -5.5f), new Vector2(35f, 7.0f) };
        static readonly Vector2 SpawnBoulder1 = new Vector2(8.5f, 4.3f), SpawnBoulder2 = new Vector2(11.5f, 3.9f);

        internal static readonly Vector2 BoulderFieldCentre = new Vector2(30f, 38f);
        internal const float BoulderFieldRadius = 14f;
        internal const int BoulderFieldRocks = 20;
        // The boulder-field rock the rock_close viewpoint looks at. It is one of the twenty.
        internal static readonly Vector2 BoulderFieldAnchor = new Vector2(36f, 44f);
        internal const string BoulderFieldAnchorPrefab = "rock_moss_set_02_rock11";
        internal const float BoulderFieldAnchorScale = 1.75f;
        // The viewpoint stands on the side the key light comes from (the north-west) and the rock turns its
        // broad lichen face to it. At M2 the camera stood south-west and looked into the shaded undercut of
        // the scan's other side: the weakest side of the best rock (review 1).
        // Review 2: the scan recedes below a lip on the camera's side (0.29 x 1.75 = 0.51 m above its pivot plane),
        // and the foot of that recess read as a dark band. The soil collars (RockCollar*) cover its lower part; the
        // camera also stands a little further back (2.4 to 2.8 m from the pivot, about 1.8 m from the rim), so the
        // rest is a shadow line, not a band. Other bearings were rendered (build/captures/m2-r2/look/anchor2):
        // north and east are crowded by the field's other rocks, and the broad lichen face is this one.
        internal const float BoulderFieldAnchorViewBearing = 315f, BoulderFieldAnchorYaw = 315f, BoulderFieldAnchorViewDistance = 2.8f;

        // The deadfall: three trunks fallen the same way. A log's long axis is its local X.
        internal const float DeadfallYaw = 25f;

        internal static readonly SetPiece[] SetPieces;
        // Review 2 of the M4 captures: the additions to the first view (see SpawnSecondRank). They are placed by their class's own
        // random stream at the start of that class (ForestScatter), not with SetPieces, so the set pieces and the boulder field,
        // which share the landmark stream, do not move.
        internal static readonly SetPiece[] SpawnExtras;

        /// <summary>Where a dinosaur can later be dropped in: free ground beside each named landmark.</summary>
        internal static readonly Landmark[] DinoSpawns;

        // The tables that are computed from the splines are filled here, after every plain field
        // initialiser above and below has run, so none of them can see a half-initialised class.
        static ForestLayout()
        {
            Landmarks = new[]
            {
                new Landmark { name = SouthBank, xz = new Vector2(-5f, -72f), radius = 8f },
                new Landmark { name = Deadfall, xz = new Vector2(12f, -14f), radius = 6f },
                new Landmark { name = FernHollow, xz = HollowCentre, radius = HollowRadius },
                new Landmark { name = Clearing, xz = ClearingCentre, radius = ClearingRadius },
                new Landmark { name = BoulderField, xz = BoulderFieldCentre, radius = BoulderFieldRadius },
                new Landmark { name = NorthOutcrop, xz = new Vector2(2f, 71f), radius = 11f },
                // The first view of section 3.5, centred on the trail ahead of the spawn.
                new Landmark { name = SpawnView, xz = TrailPoint(9.5f, 0f), radius = 10f },
            };
            DinoSpawns = new[]
            {
                new Landmark { name = "Hollow", xz = HollowCentre },
                new Landmark { name = "BoulderField", xz = BoulderFieldCentre },
                // In front of the rocks, not inside them: two metres beyond the trail's last point.
                new Landmark { name = "NorthOutcrop", xz = PointAtDistance(Trail, SplineLength(Trail) + 2f, out _) },
            };
            SetPieces = BuildSetPieces();
            SpawnExtras = BuildSpawnExtras();
            Viewpoints = BuildViewpoints();
            SightlineStations = BuildSightlineStations();
        }

        static Vector2[] BuildSightlineStations()
        {
            var stations = new List<Vector2>();
            float length = SplineLength(Trail);
            int index = 0;
            for (float along = SpawnArcLength() + SightlineStationStart; along < length - 6f; along += SightlineStationStep, index++)
            {
                Vector2 onTrail = PointAtDistance(Trail, along, out Vector2 tangent);
                float side = index % 2 == 0 ? 1f : -1f;   // right first: the spawn thicket stands on the left
                Vector2 centre = onTrail + RightOf(tangent) * (side * SightlineStationLateral);
                float reach = SightlineStationRadius + 2f;
                if (SignedDistanceToBoundary(centre) > -(reach + 4f)) continue;
                if (Vector2.Distance(centre, ClearingCentre) < ClearingRadius + reach) continue;
                bool inLandmark = false;
                foreach (Landmark landmark in Landmarks)
                    if (landmark.name != SpawnView && Vector2.Distance(centre, landmark.xz) < landmark.radius + reach) inLandmark = true;
                if (inLandmark) continue;
                if (DistanceToGameTrails(centre) < SightlineStationRadius + CorridorClearance + 1f) continue;
                stations.Add(centre);
            }
            return stations.ToArray();
        }

        /// <summary>Negative inside the playable core, positive on the bank.</summary>
        public static float SignedDistanceToBoundary(Vector2 xz)
        {
            // Signed distance of a rounded rectangle: shrink the box by the corner radius, measure to it, grow back.
            float qx = Mathf.Abs(xz.x) - (HalfExtentX - CornerRadius);
            float qz = Mathf.Abs(xz.y) - (HalfExtentZ - CornerRadius);
            float outsideX = Mathf.Max(qx, 0f), outsideZ = Mathf.Max(qz, 0f);
            float outside = Mathf.Sqrt(outsideX * outsideX + outsideZ * outsideZ);
            float inside = Mathf.Min(Mathf.Max(qx, qz), 0f);
            return outside + inside - CornerRadius;
        }

        /// <summary>A point on the spline, with <paramref name="t01"/> proportional to the distance travelled.</summary>
        public static Vector2 SampleSpline(Vector2[] controlPoints, float t01)
        {
            Polyline line = GetPolyline(controlPoints);
            return line.PointAt(Mathf.Clamp01(t01) * line.length, out _);
        }

        public static float SplineLength(Vector2[] controlPoints) => GetPolyline(controlPoints).length;

        public static float DistanceToSpline(Vector2[] controlPoints, Vector2 xz)
        {
            return GetPolyline(controlPoints).Distance(xz, out _, out _);
        }

        /// <summary>Smallest distance to the trail and both game trails.</summary>
        public static float DistanceToAnyTrail(Vector2 xz)
        {
            float trail = DistanceToSpline(Trail, xz);
            float west = DistanceToSpline(WestTrail, xz);
            float east = DistanceToSpline(EastTrail, xz);
            return Mathf.Min(trail, Mathf.Min(west, east));
        }

        /// <summary>
        /// Trail coordinates of section 3.5: metres along the trail from the spawn (north is positive),
        /// and metres to the right of the centre line for someone walking north (east is positive).
        /// </summary>
        public static Vector2 TrailPoint(float metresFromSpawn, float metresToTheRight)
        {
            Vector2 centre = PointAtDistance(Trail, SpawnArcLength() + metresFromSpawn, out Vector2 tangent);
            return centre + RightOf(tangent) * metresToTheRight;
        }

        // ---- Spline helpers for the other files of the package ----

        /// <summary>Distance to the spline, and how far along it the closest point lies (metres from its start).</summary>
        internal static float DistanceToSpline(Vector2[] controlPoints, Vector2 xz, out float arcLength)
        {
            return GetPolyline(controlPoints).Distance(xz, out arcLength, out _);
        }

        /// <summary>
        /// The same query for a caller that walks a grid: <paramref name="hint"/> carries the segment
        /// found for the previous point (start with -1), which lets most of the spline be skipped.
        /// </summary>
        internal static float DistanceToSpline(Vector2[] controlPoints, Vector2 xz, out float arcLength, ref int hint)
        {
            float distance = GetPolyline(controlPoints).Distance(xz, out arcLength, out int segment, hint);
            hint = segment;
            return distance;
        }

        /// <summary>
        /// The point <paramref name="metres"/> along the spline and the unit direction of travel there.
        /// Beyond either end the spline continues as a straight line.
        /// </summary>
        internal static Vector2 PointAtDistance(Vector2[] controlPoints, float metres, out Vector2 tangent)
        {
            return GetPolyline(controlPoints).PointAt(metres, out tangent);
        }

        /// <summary>The closest point on the trail or either game trail.</summary>
        internal static Vector2 NearestTrailPoint(Vector2 xz)
        {
            Vector2 best = xz;
            float bestDistance = float.MaxValue;
            foreach (Vector2[] spline in AllTrails)
            {
                float distance = DistanceToSpline(spline, xz, out float arcLength);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = PointAtDistance(spline, arcLength, out _);
            }
            return best;
        }

        internal static float DistanceToGameTrails(Vector2 xz)
        {
            return Mathf.Min(DistanceToSpline(WestTrail, xz), DistanceToSpline(EastTrail, xz));
        }

        internal static readonly Vector2[][] AllTrails = { Trail, WestTrail, EastTrail };
        internal static readonly string[] AllTrailNames = { "Trail", "WestTrail", "EastTrail" };

        /// <summary>How far along the trail the spawn stands.</summary>
        internal static float SpawnArcLength()
        {
            DistanceToSpline(Trail, SpawnXZ, out float arcLength);
            return arcLength;
        }

        /// <summary>The right-hand side of a direction of travel: east when walking north.</summary>
        internal static Vector2 RightOf(Vector2 tangent) => new Vector2(tangent.y, -tangent.x);

        /// <summary>Compass bearing of a direction: 0 is north (+Z), 90 is east (+X). Also a Unity yaw.</summary>
        internal static float Bearing(Vector2 direction) => Mathf.Repeat(Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg, 360f);

        internal static Vector2 FromBearing(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
        }

        internal static int CellIndex(float worldCoordinate)
        {
            return Mathf.Clamp(Mathf.FloorToInt((worldCoordinate + TileSize * 0.5f) / CellSize), 0, CellsPerSide - 1);
        }

        /// <summary>GLSL smoothstep. Mathf.SmoothStep is a different function (an eased interpolation).</summary>
        internal static float Smooth(float edge0, float edge1, float value)
        {
            float t = Mathf.Clamp01((value - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        // ---- Set pieces ----

        static SetPiece[] BuildSetPieces()
        {
            var pieces = new List<SetPiece>();

            // The first view (section 3.5): a young-fir thicket on the left, two moss boulders on the
            // right, the first tall fir beyond the thicket, ferns up to both shoulders.
            string[] thicketMix =
            {
                ForestPrefabIds.YoungFirs[0], ForestPrefabIds.YoungFirs[1], ForestPrefabIds.YoungFirs[2],
                ForestPrefabIds.YoungFirs[1], ForestPrefabIds.YoungFirs[2],
            };
            for (int i = 0; i < SpawnThicket.Length; i++)
                pieces.Add(Piece(SpawnView, ForestClass.YoungFir, thicketMix[i], TrailPoint(SpawnThicket[i].x, SpawnThicket[i].y), 0.9f, 1.2f));
            pieces.Add(Piece(SpawnView, ForestClass.Boulder, "rock_moss_set_02_rock11", TrailPoint(SpawnBoulder1.x, SpawnBoulder1.y), 1.25f, 1.25f));
            pieces.Add(Piece(SpawnView, ForestClass.Boulder, "rock_moss_set_02_rock12", TrailPoint(SpawnBoulder2.x, SpawnBoulder2.y), 1.2f, 1.2f));
            // Spec 3.5 puts fir_tree_01_a here. Review 1 of the M4 captures: risk 1(d) keeps a on the bank (TallFirCoreMix), and this
            // fir is the one canopy_up looks up into and trunk_close stands beside, where a's LOD0 showed blades and its scanned
            // trunk a smeared strip above 3 m. It is c, whose LOD0 needles are 1.6 times life size (a recorded deviation).
            pieces.Add(Piece(SpawnView, ForestClass.TallFir, ForestPrefabIds.TallFirs[2], TrailPoint(SpawnTallFir.x, SpawnTallFir.y), 1f, 1f));

            // Fern patches from 2 to 15 m, 1.8 to 4 m off the line on both sides; the tread stays clear.
            float[] patchAlong = { 2.4f, 4.9f, 7.3f, 10.1f, 12.7f, 14.8f };
            float[] patchLeft = { 2.1f, 3.3f, 1.9f, 2.0f, 2.6f, 1.9f };
            float[] patchRight = { 2.6f, 1.9f, 2.2f, 2.1f, 1.9f, 2.9f };
            for (int i = 0; i < patchAlong.Length; i++)
            {
                pieces.Add(Piece(SpawnView, ForestClass.FernPatch, null, TrailPoint(patchAlong[i], -patchLeft[i]), 0.85f, 1.5f));
                pieces.Add(Piece(SpawnView, ForestClass.FernPatch, null, TrailPoint(patchAlong[i] + 1.1f, patchRight[i]), 0.85f, 1.5f));
            }
            // Single clumps fill the gaps and ring the two boulders.
            Vector2[] clumps =
            {
                new Vector2(3.6f, -1.9f), new Vector2(6.1f, 2.0f), new Vector2(8.8f, -2.4f), new Vector2(11.4f, -1.9f),
                new Vector2(9.6f, 2.3f), new Vector2(7.0f, 3.4f), new Vector2(13.2f, 2.4f), new Vector2(13.9f, -1.9f),
            };
            foreach (Vector2 clump in clumps)
                pieces.Add(Piece(SpawnView, ForestClass.FernClump, null, TrailPoint(clump.x, clump.y), 0.8f, 1.6f));

            // South bank: three moss rocks at the foot of the bank behind the spawn.
            // They leave the landmark's own point, (-5, -72), on open ground between them.
            pieces.Add(Piece(SouthBank, ForestClass.Boulder, null, new Vector2(-11.2f, -72.4f), 1.2f, 1.5f));
            pieces.Add(Piece(SouthBank, ForestClass.Boulder, null, new Vector2(-8.3f, -73.4f), 1.2f, 1.5f));
            pieces.Add(Piece(SouthBank, ForestClass.Boulder, null, new Vector2(-1.4f, -73.2f), 1.2f, 1.5f));

            // Deadfall: three trunks 2 m apart, within 18 degrees of each other, never crossing.
            Vector2 deadfall = new Vector2(12f, -14f);
            float axisRadians = DeadfallYaw * Mathf.Deg2Rad;
            Vector2 along = new Vector2(Mathf.Cos(axisRadians), -Mathf.Sin(axisRadians));   // where local X points after the yaw
            Vector2 across = new Vector2(Mathf.Sin(axisRadians), Mathf.Cos(axisRadians));
            // Fallen the same way, but not three copies in a row: the middle trunk lies end for end, and the
            // three are staggered along the fall (as a raft of parallel twins they were plainly one mesh, review 1).
            float[] logAcross = { -2.1f, 0f, 2.0f }, logAlong = { 1.6f, -1.3f, 0.5f }, logYaw = { -9f, 186f, 12f };
            for (int i = 0; i < 3; i++)
            {
                SetPiece log = Piece(Deadfall, ForestClass.Log, ForestPrefabIds.Logs[0], deadfall + across * logAcross[i] + along * logAlong[i], 1.2f, 1.5f);
                log.yaw = DeadfallYaw + logYaw[i];
                pieces.Add(log);
            }
            pieces.Add(Piece(Deadfall, ForestClass.Branch, ForestPrefabIds.Branches[0], deadfall + across * -1.05f + along * -1.6f, 0.9f, 1.3f));
            pieces.Add(Piece(Deadfall, ForestClass.Branch, ForestPrefabIds.Branches[2], deadfall + across * 1.0f + along * 1.9f, 0.9f, 1.3f));
            // Between the trunks single clumps, not patches (review 1 of the M4 captures: a patch 1 m from two logs buried both).
            pieces.Add(Piece(Deadfall, ForestClass.FernClump, null, deadfall + across * -1.0f + along * 1.4f, 0.8f, 1.1f));
            pieces.Add(Piece(Deadfall, ForestClass.FernClump, null, deadfall + across * 1.0f + along * -1.5f, 0.8f, 1.1f));
            pieces.Add(Piece(Deadfall, ForestClass.FernPatch, null, deadfall + across * -3.3f + along * -0.4f, 0.85f, 1.5f));
            pieces.Add(Piece(Deadfall, ForestClass.FernPatch, null, deadfall + across * 3.2f + along * 0.9f, 0.85f, 1.5f));

            // Fern hollow: one stump patch on the west side of the bowl, clear of the game trail.
            pieces.Add(Piece(FernHollow, ForestClass.Stump, ForestPrefabIds.Stumps[1], new Vector2(27.5f, -21.5f), 0.9f, 1.15f));

            // Clearing: two stump patches either side of the game trail, three tall firs on the rim.
            pieces.Add(Piece(Clearing, ForestClass.Stump, ForestPrefabIds.Stumps[0], new Vector2(-21f, 8f), 0.9f, 1.15f));
            pieces.Add(Piece(Clearing, ForestClass.Stump, ForestPrefabIds.Stumps[1], ClearingStumpWest, 0.9f, 1.15f));
            float[] rimBearings = { 45f, 200f, 275f };
            // b, c, b (was a, b, a): risk 1(d), see TallFirCoreMix.
            string[] rimFirs = { ForestPrefabIds.TallFirs[1], ForestPrefabIds.TallFirs[2], ForestPrefabIds.TallFirs[1] };
            for (int i = 0; i < rimBearings.Length; i++)
                pieces.Add(Piece(Clearing, ForestClass.TallFir, rimFirs[i], ClearingCentre + FromBearing(rimBearings[i]) * 14.5f, 0.9f, 1.15f));

            // Boulder field: the planner scatters nineteen rocks; this one is fixed so a viewpoint can face it.
            SetPiece anchor = Piece(BoulderField, ForestClass.Boulder, BoulderFieldAnchorPrefab, BoulderFieldAnchor, BoulderFieldAnchorScale, BoulderFieldAnchorScale);
            anchor.yaw = BoulderFieldAnchorYaw;
            pieces.Add(anchor);

            // North outcrop (section 3.4): nine rocks piled in an arc 8 to 11 m from the trail's last point,
            // between bearings -60 and +60. At M2 they stood in single file every 15 degrees with random yaw,
            // radius alternating 8.7 and 10.4 m: a row with gaps one could see through, centred on north
            // while the trail arrives heading north-north-west (review 1). Now seven lie in a front row with
            // their long axes along the arc, close enough to overlap by a third and more, and the two tall
            // shapes (rock04 and rock11, 2 m at this scale) stand behind the middle of it, where the trail
            // points. Bearings and radii are uneven on purpose. Each entry: prefab, bearing, radius, which
            // local axis is the long one (0 = none, 1 = X, 2 = Z), yaw offset, smallest and largest scale.
            Vector2 trailEnd = Trail[Trail.Length - 1];
            var outcrop = new (string prefab, float bearing, float radius, int longAxis, float turn, float minScale, float maxScale)[]
            {
                ("rock_moss_set_02_rock13", -59f, 9.0f, 1, 14f, 1.5f, 1.7f),
                ("rock_moss_set_01_rock05", -47f, 8.4f, 2, -11f, 1.6f, 1.8f),
                ("rock_moss_set_01_rock01", -31f, 8.9f, 2, 9f, 1.7f, 1.9f),
                ("rock_moss_set_01_rock02", -13f, 8.2f, 2, -16f, 1.7f, 1.9f),
                ("rock_moss_set_01_rock06", 5f, 8.7f, 2, 12f, 1.6f, 1.8f),
                ("rock_moss_set_02_rock10", 21f, 8.3f, 1, -8f, 1.6f, 1.9f),
                ("rock_moss_set_01_rock03", 36f, 9.1f, 0, 0f, 1.5f, 1.7f),
                ("rock_moss_set_01_rock04", -24f, 10.7f, 0, 0f, 1.8f, 1.9f),
                ("rock_moss_set_02_rock11", -2f, 10.9f, 1, 10f, 1.8f, 1.9f),
            };
            foreach (var rock in outcrop)
            {
                SetPiece piece = Piece(NorthOutcrop, ForestClass.Boulder, rock.prefab, trailEnd + FromBearing(rock.bearing) * rock.radius, rock.minScale, rock.maxScale);
                // Local Z points along the yaw and local X a quarter turn further; the arc runs a quarter turn from the bearing.
                if (rock.longAxis == 2) piece.yaw = Mathf.Repeat(rock.bearing + 90f + rock.turn, 360f);
                else if (rock.longAxis == 1) piece.yaw = Mathf.Repeat(rock.bearing + rock.turn, 360f);
                pieces.Add(piece);
            }
            // Two dry branches at the foot of the pile, where the trail's apron ends.
            pieces.Add(Piece(NorthOutcrop, ForestClass.Branch, ForestPrefabIds.Branches[0], trailEnd + FromBearing(-38f) * 5.6f, 0.9f, 1.3f));
            pieces.Add(Piece(NorthOutcrop, ForestClass.Branch, ForestPrefabIds.Branches[2], trailEnd + FromBearing(12f) * 5.9f, 0.9f, 1.3f));

            return pieces.ToArray();
        }

        static SetPiece[] BuildSpawnExtras()
        {
            var pieces = new List<SetPiece>();
            string[] rankMix = { ForestPrefabIds.YoungFirs[1], ForestPrefabIds.YoungFirs[2], ForestPrefabIds.YoungFirs[1] };
            for (int i = 0; i < SpawnSecondRank.Length; i++)
                pieces.Add(Piece(SpawnView, ForestClass.YoungFir, rankMix[i], TrailPoint(SpawnSecondRank[i].x, SpawnSecondRank[i].y),
                    SpawnSecondRankMinScale, SpawnSecondRankMaxScale));
            string[] rightMix = { ForestPrefabIds.YoungFirs[2], ForestPrefabIds.YoungFirs[1] };
            for (int i = 0; i < SpawnRightFirs.Length; i++)
                pieces.Add(Piece(SpawnView, ForestClass.YoungFir, rightMix[i], TrailPoint(SpawnRightFirs[i].x, SpawnRightFirs[i].y), 0.9f, 1.2f));
            foreach (Vector2 fern in SpawnFernDrift)
                pieces.Add(Piece(SpawnView, ForestClass.FernPatch, null, TrailPoint(fern.x, fern.y), 1.1f, 1.5f));
            pieces.Add(Piece(SpawnView, ForestClass.TallFir, ForestPrefabIds.TallFirs[2], TrailPoint(SpawnCanopyFirC.x, SpawnCanopyFirC.y), 0.95f, 1.05f));
            foreach (Vector2 fir in SpawnAxisFirs)
                pieces.Add(Piece(SpawnView, ForestClass.TallFir, ForestPrefabIds.TallFirs[1], TrailPoint(fir.x, fir.y), 1.0f, 1.2f));
            return pieces.ToArray();
        }

        static SetPiece Piece(string landmark, ForestClass cls, string prefabId, Vector2 xz, float minScale, float maxScale)
        {
            return new SetPiece { landmark = landmark, cls = cls, prefabId = prefabId, xz = xz, minScale = minScale, maxScale = maxScale, yaw = float.NaN };
        }

        // ---- Viewpoints ----

        static Viewpoint[] BuildViewpoints()
        {
            var views = new List<Viewpoint>();
            float trailLength = SplineLength(Trail);
            Vector2 mid = PointAtDistance(Trail, trailLength * 0.5f, out Vector2 midTangent);
            float midYaw = Bearing(midTangent);

            // The quick set.
            views.Add(Eye("trail_start", SpawnXZ, EyeHeight + SpawnLift, 0f, SpawnYaw));
            views.Add(Eye("trail_mid_fwd", mid, EyeHeight, 0f, midYaw));
            // From where the west game trail enters the clearing, looking across it. At M2 it looked at the
            // clearing's centre, between its two stump patches (34 degrees left and 44 right), so the middle of
            // one of the five owner review frames was empty (review 2). It now turns toward the south-west
            // stump patch, which stands left of centre with the swards beyond it.
            Vector2 clearingEdge = new Vector2(-17.5f, -2.5f);
            views.Add(Eye("clearing", clearingEdge, EyeHeight, 0f, Bearing(ClearingStumpWest - clearingEdge) + 12f));
            // Low on the trail's edge, looking along a rut.
            Vector2 groundClose = PointAtDistance(Trail, SpawnArcLength() + 24f, out Vector2 groundTangent);
            views.Add(Eye("ground_close", groundClose + RightOf(groundTangent) * 0.9f, 0.4f, 35f, Bearing(groundTangent)));
            views.Add(Eye("overview_oblique", new Vector2(0f, -90f), 40f, 45f, 0f));

            // The full set.
            views.Add(Eye("trail_mid_back", mid, EyeHeight, 0f, midYaw + 180f));
            for (int i = 1; i <= 8; i++)
            {
                Vector2 point = PointAtDistance(Trail, trailLength * (i - 0.5f) / 8f, out Vector2 tangent);
                views.Add(Eye("trail_strip_0" + i, point, EyeHeight, 0f, Bearing(tangent)));
            }

            // 0.6 m from the bark of the spawn's tall fir, on its trail side.
            Vector2 tallFir = TrailPoint(SpawnTallFir.x, SpawnTallFir.y);
            Vector2 trunkClose = TrailPoint(SpawnTallFir.x, SpawnTallFir.y + 1.0f);
            views.Add(Eye("trunk_close", trunkClose, 1.3f, 0f, Bearing(tallFir - trunkClose)));

            // On the centre line beside the nearest thicket fir: its branch tips end about 1.5 m short of the camera.
            Vector2 nearFir = TrailPoint(SpawnThicket[1].x, SpawnThicket[1].y);
            Vector2 firClose = TrailPoint(SpawnThicket[1].x, 0f);
            views.Add(Eye("fir_close", firClose, EyeHeight, 0f, Bearing(nearFir - firClose)));

            // Crouched about a metre from the lit face of the anchored boulder-field rock.
            Vector2 rockClose = BoulderFieldAnchor + FromBearing(BoulderFieldAnchorViewBearing) * BoulderFieldAnchorViewDistance;
            views.Add(Eye("rock_close", rockClose, CrouchEyeHeight, 0f, Bearing(BoulderFieldAnchor - rockClose)));

            Vector2 canopy = TrailPoint(12f, -1.0f);
            views.Add(Eye("canopy_up", canopy, EyeHeight, -75f, SpawnYaw));
            // Toward the key light's source, tipped up a little so needles and fronds are seen against the sky.
            views.Add(Eye("backlit", PointAtDistance(Trail, SpawnArcLength() + 40f, out _), EyeHeight, -10f, KeyLightSourceBearing()));
            Vector2 sightline = new Vector2(46f, 30f);
            views.Add(Eye("long_sightline", sightline, EyeHeight, 0f, Bearing(Gully[2] - sightline)));
            // Crouched between the five spawn-thicket firs, looking back at the trail.
            Vector2 hide = TrailPoint(8.6f, -5.4f);
            views.Add(Eye("crouch_hide", hide, CrouchEyeHeight, 0f, Bearing(TrailPoint(8.6f, 0f) - hide)));

            // Three metres inside each wall, looking out. Off the axes, so they miss the outcrop and the south-bank rocks.
            float wallInset = 3f - (WallOffset - WallThickness * 0.5f);
            views.Add(Eye("boundary_n", new Vector2(-20f, HalfExtentZ - wallInset), EyeHeight, 0f, 0f));
            views.Add(Eye("boundary_e", new Vector2(HalfExtentX - wallInset, 4f), EyeHeight, 0f, 90f));
            views.Add(Eye("boundary_s", new Vector2(20f, -HalfExtentZ + wallInset), EyeHeight, 0f, 180f));
            views.Add(Eye("boundary_w", new Vector2(-HalfExtentX + wallInset, -20f), EyeHeight, 0f, 270f));

            views.Add(Eye("pano_0", mid, EyeHeight, 0f, 0f));
            views.Add(Eye("pano_90", mid, EyeHeight, 0f, 90f));
            views.Add(Eye("pano_180", mid, EyeHeight, 0f, 180f));
            views.Add(Eye("pano_270", mid, EyeHeight, 0f, 270f));

            // Two landmarks no view of the set looked at from the ground (review 1 of the M2 captures):
            // crouched on the south-west rim of the fern hollow, looking down across its muddy bottom to the
            // gully mouth, and standing on the trail side of the deadfall.
            Vector2 hollowRim = HollowCentre + FromBearing(225f) * (HollowRadius - 1.5f);
            views.Add(Eye("hollow_rim", hollowRim, CrouchEyeHeight, 6f, Bearing(HollowCentre - hollowRim)));
            Vector2 deadfallCentre = new Vector2(12f, -14f);
            Vector2 deadfallView = deadfallCentre + new Vector2(-7.5f, -3f);
            views.Add(Eye("deadfall", deadfallView, EyeHeight, 6f, Bearing(deadfallCentre - deadfallView)));

            // Straight down on the whole core. North is up in the image.
            views.Add(new Viewpoint
            {
                id = "overview_ortho", position = new Vector3(0f, 250f, 0f), euler = new Vector3(90f, 0f, 0f),
                fieldOfView = 70f, orthographic = true, orthographicSize = 80f, heightIsAboveGround = false,
            });
            return views.ToArray();
        }

        static Viewpoint Eye(string id, Vector2 xz, float heightAboveGround, float pitch, float yaw)
        {
            return new Viewpoint
            {
                id = id, position = new Vector3(xz.x, heightAboveGround, xz.y), euler = new Vector3(pitch, Mathf.Repeat(yaw, 360f), 0f),
                fieldOfView = 70f, orthographic = false, orthographicSize = 0f, heightIsAboveGround = true,
            };
        }

        // The backlit view faces where the default preset's key light comes from (section 8.2).
        static float KeyLightSourceBearing()
        {
            ForestLightingPresets.Preset preset = ForestLightingPresets.Get(ForestLightingPresets.Default);
            return (preset != null ? preset.keyBearing : 135f) + 180f;
        }

        // ---- Splines ----

        const int SamplesPerSegment = 64;
        const int SegmentsPerChunk = 16;
        const int MaxCachedSplines = 32;

        static Polyline GetPolyline(Vector2[] controlPoints)
        {
            if (controlPoints == null || controlPoints.Length < 2)
                throw new ArgumentException("A spline needs at least two control points.", nameof(controlPoints));

            lock (polylineLock)
            {
                if (polylines.TryGetValue(controlPoints, out Polyline cached) && cached.Matches(controlPoints))
                    return cached;
                // Callers that build a new array per call must not grow the cache without limit.
                if (polylines.Count >= MaxCachedSplines)
                    polylines.Clear();
                var line = new Polyline(controlPoints);
                polylines[controlPoints] = line;
                return line;
            }
        }

        /// <summary>
        /// A centripetal Catmull-Rom spline through the control points, flattened to short straight
        /// pieces. Centripetal, because the uniform kind overshoots where a short span meets a long
        /// one, and the trail's first span is exactly that. Every query in the forest build goes
        /// through this one polyline, so planner, painter, audit and tests agree to the millimetre.
        /// </summary>
        sealed class Polyline
        {
            public readonly Vector2[] points;
            public readonly float length;
            readonly Vector2[] control;
            readonly float[] cumulative;
            readonly float[] chunkMinX, chunkMaxX, chunkMinZ, chunkMaxZ;

            public Polyline(Vector2[] controlPoints)
            {
                control = (Vector2[])controlPoints.Clone();
                int spans = control.Length - 1;
                points = new Vector2[spans * SamplesPerSegment + 1];
                for (int span = 0; span < spans; span++)
                {
                    Vector2 p1 = control[span], p2 = control[span + 1];
                    // Mirrored phantom points give the two ends a natural direction.
                    Vector2 p0 = span > 0 ? control[span - 1] : p1 * 2f - p2;
                    Vector2 p3 = span + 2 < control.Length ? control[span + 2] : p2 * 2f - p1;
                    for (int i = 0; i < SamplesPerSegment; i++)
                        points[span * SamplesPerSegment + i] = CatmullRom(p0, p1, p2, p3, i / (float)SamplesPerSegment);
                }
                points[points.Length - 1] = control[control.Length - 1];

                cumulative = new float[points.Length];
                for (int i = 1; i < points.Length; i++)
                    cumulative[i] = cumulative[i - 1] + Vector2.Distance(points[i - 1], points[i]);
                length = cumulative[cumulative.Length - 1];

                int segments = points.Length - 1;
                int chunks = (segments + SegmentsPerChunk - 1) / SegmentsPerChunk;
                chunkMinX = new float[chunks]; chunkMaxX = new float[chunks];
                chunkMinZ = new float[chunks]; chunkMaxZ = new float[chunks];
                for (int chunk = 0; chunk < chunks; chunk++)
                {
                    int first = chunk * SegmentsPerChunk, last = Mathf.Min(first + SegmentsPerChunk, segments);
                    float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                    for (int i = first; i <= last; i++)
                    {
                        minX = Mathf.Min(minX, points[i].x); maxX = Mathf.Max(maxX, points[i].x);
                        minZ = Mathf.Min(minZ, points[i].y); maxZ = Mathf.Max(maxZ, points[i].y);
                    }
                    chunkMinX[chunk] = minX; chunkMaxX[chunk] = maxX; chunkMinZ[chunk] = minZ; chunkMaxZ[chunk] = maxZ;
                }
            }

            public bool Matches(Vector2[] controlPoints)
            {
                if (controlPoints.Length != control.Length) return false;
                for (int i = 0; i < control.Length; i++)
                    if (controlPoints[i] != control[i]) return false;
                return true;
            }

            static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float u)
            {
                float t0 = 0f;
                float t1 = t0 + Knot(p0, p1);
                float t2 = t1 + Knot(p1, p2);
                float t3 = t2 + Knot(p2, p3);
                float t = t1 + (t2 - t1) * u;
                Vector2 a1 = p0 * ((t1 - t) / (t1 - t0)) + p1 * ((t - t0) / (t1 - t0));
                Vector2 a2 = p1 * ((t2 - t) / (t2 - t1)) + p2 * ((t - t1) / (t2 - t1));
                Vector2 a3 = p2 * ((t3 - t) / (t3 - t2)) + p3 * ((t - t2) / (t3 - t2));
                Vector2 b1 = a1 * ((t2 - t) / (t2 - t0)) + a2 * ((t - t0) / (t2 - t0));
                Vector2 b2 = a2 * ((t3 - t) / (t3 - t1)) + a3 * ((t - t1) / (t3 - t1));
                return b1 * ((t2 - t) / (t2 - t1)) + b2 * ((t - t1) / (t2 - t1));
            }

            // Square root of the chord: the centripetal parameterisation. Never zero, so repeated points do not divide by it.
            static float Knot(Vector2 a, Vector2 b) => Mathf.Max(Mathf.Sqrt(Vector2.Distance(a, b)), 1e-4f);

            public Vector2 PointAt(float metres, out Vector2 tangent)
            {
                int last = points.Length - 1;
                if (metres <= 0f)
                {
                    tangent = (points[1] - points[0]).normalized;
                    return points[0] + tangent * metres;
                }
                if (metres >= length)
                {
                    tangent = (points[last] - points[last - 1]).normalized;
                    return points[last] + tangent * (metres - length);
                }

                int low = 0, high = last;
                while (high - low > 1)
                {
                    int middle = (low + high) / 2;
                    if (cumulative[middle] <= metres) low = middle; else high = middle;
                }
                float span = cumulative[high] - cumulative[low];
                float t = span > 0f ? (metres - cumulative[low]) / span : 0f;
                tangent = (points[high] - points[low]).normalized;
                return Vector2.LerpUnclamped(points[low], points[high], t);
            }

            public float SegmentDistanceSquared(int segment, Vector2 p, out float t)
            {
                Vector2 a = points[segment], b = points[segment + 1];
                float abx = b.x - a.x, abz = b.y - a.y;
                float lengthSquared = abx * abx + abz * abz;
                t = lengthSquared > 0f ? Mathf.Clamp01(((p.x - a.x) * abx + (p.y - a.y) * abz) / lengthSquared) : 0f;
                float dx = p.x - (a.x + abx * t), dz = p.y - (a.y + abz * t);
                return dx * dx + dz * dz;
            }

            public float Distance(Vector2 p, out float arcLength, out int segment, int seedSegment = -1)
            {
                int segments = points.Length - 1;
                float best = float.MaxValue;
                int bestSegment = 0;
                float bestT = 0f;
                if (seedSegment >= 0 && seedSegment < segments)
                {
                    best = SegmentDistanceSquared(seedSegment, p, out bestT);
                    bestSegment = seedSegment;
                }

                for (int chunk = 0; chunk < chunkMinX.Length; chunk++)
                {
                    // Nothing in this chunk can beat the best so far if its bounding box is already further away.
                    float dx = p.x < chunkMinX[chunk] ? chunkMinX[chunk] - p.x : p.x > chunkMaxX[chunk] ? p.x - chunkMaxX[chunk] : 0f;
                    float dz = p.y < chunkMinZ[chunk] ? chunkMinZ[chunk] - p.y : p.y > chunkMaxZ[chunk] ? p.y - chunkMaxZ[chunk] : 0f;
                    if (dx * dx + dz * dz >= best) continue;

                    int first = chunk * SegmentsPerChunk, end = Mathf.Min(first + SegmentsPerChunk, segments);
                    for (int i = first; i < end; i++)
                    {
                        float squared = SegmentDistanceSquared(i, p, out float t);
                        if (squared >= best) continue;
                        best = squared; bestSegment = i; bestT = t;
                    }
                }

                segment = bestSegment;
                arcLength = cumulative[bestSegment] + (cumulative[bestSegment + 1] - cumulative[bestSegment]) * bestT;
                return Mathf.Sqrt(best);
            }
        }
    }
}
