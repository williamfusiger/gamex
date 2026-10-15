using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gamex.EditorTools
{
    /// <summary>
    /// Decides where every tree, rock, log, fern and tuft of grass stands. It runs on the plain arrays
    /// of <see cref="ForestFields"/> before any Unity object exists and is deterministic: the same
    /// fields and seed give the same plan, object for object. <see cref="Plan"/> writes no height;
    /// <see cref="Ground"/> fills heights in once the root mounds are in the ground, so no pivot is
    /// ever grounded on a height that changes afterwards.
    /// </summary>
    public static class ForestScatter
    {
        /// <summary>
        /// Positions (x and z), rotation, scale, tint, sink and clearance of every instance, sorted by
        /// cell, class, x and z. <c>position.y</c> stays 0 and <c>grounded</c> false.
        /// </summary>
        public static ForestPlan Plan(ForestFields fields, int seed)                     // pure and deterministic; position.y = 0, grounded = false
        {
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            return new Planner(fields, seed).Run();
        }

        /// <summary>Sets every pivot height to the ground under it minus the placement's sink.</summary>
        public static void Ground(ForestPlan plan, ForestFields fields)                  // position.y = fields.HeightAt(x, z) - sink; grounded = true
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (fields == null) throw new ArgumentNullException(nameof(fields));

            ForestPlacement[] placements = plan.placements;
            for (int i = 0; i < placements.Length; i++)
            {
                Vector3 position = placements[i].position;
                position.y = fields.HeightAt(position.x, position.z) - placements[i].sink;
                placements[i].position = position;
            }
            plan.grounded = true;
        }

        // Bodies are what later classes must keep away from. A solid's body is a little smaller than
        // its collider's bounding circle, because a scan fills that circle only in places.
        const float TallFirBody = 0.9f, YoungFirBody = 0.6f, RockBodyShare = 0.8f, StumpBodyShare = 0.9f;
        const float LogDiscOffset = 1.4f, LogDiscRadius = 0.8f;
        const float SolidGap = 0.2f;
        // No scattered trunk or rock comes this close to where a first-person capture camera stands.
        const float ViewpointGap = 0.8f;
        // A camera that lies on the ground to show the ground (ground_close, 0.4 m up) keeps a fern patch
        // this far away: a patch is over two metres across, and one frond in front of the lens hides what
        // the view is for.
        const float GroundViewHeight = 0.6f, GroundViewFernGap = 2.6f, CrouchViewHeight = 1.0f, CrouchViewFernGap = 2.0f;
        // The corridor and spawn rules are met with two centimetres to spare, so a check that
        // recomputes them with different rounding still passes.
        const float RuleMargin = 0.02f;
        const float OccupancyCell = 4f;
        const int PoissonAttempts = 30;
        // What share of the densest possible packing a Poisson-disc run reaches: used to predict counts.
        const float PoissonPacking = 0.70f;

        struct Body
        {
            public float x, z;
            public float radius;       // nothing solid may come closer than this
            public float edge;         // where the object visibly ends: ferns gather outside it
            public ForestClass cls;
            public int variant;        // prefab number, for the yaw rule; -1 for the extra discs of a log
            public float yaw;
            public int group;          // rocks of one cluster share a group and are spaced by the cluster's own rule
        }

        struct Sample
        {
            public Vector2 point;
            public float radius;
        }

        sealed class PrefabInfo
        {
            public int variant;
            public float clearance;            // ForestKitSpec.ClearanceRadius, at scale 1
            public bool measured;              // the manifest has an object for it
            public float minY, footprint;      // from the manifest: lowest point below the pivot, horizontal radius
        }

        sealed class Planner
        {
            readonly ForestFields fields;
            readonly int seed;
            readonly List<ForestPlacement> placements = new List<ForestPlacement>(3200);
            readonly List<Body> bodies = new List<Body>(3600);
            readonly List<int>[] occupancy;
            readonly int occupancyCells;
            readonly Dictionary<string, PrefabInfo> prefabs = new Dictionary<string, PrefabInfo>();
            readonly List<string> warnings = new List<string>();
            float largestBody;
            int groups;

            public Planner(ForestFields fields, int seed)
            {
                this.fields = fields;
                this.seed = seed;
                occupancyCells = Mathf.CeilToInt(ForestLayout.TileSize / OccupancyCell);
                occupancy = new List<int>[occupancyCells * occupancyCells];
            }

            public ForestPlan Run()
            {
                // Large to small, so the small things fit themselves around the large ones.
                PlaceSetPieces();
                ScatterTallFirs();
                // D2 (density pass): the rocks and the fallen logs before the young firs. In the denser stand the clusters found no
                // free ground after them (78 of 90 rocks) and no log fitted beside the game trails (0 of 5); now the thickets grow
                // around the rocks and in the gaps the logs opened.
                ScatterBoulders();
                ScatterLogs(Stream(ForestClass.Log));
                ScatterForestLogs(new System.Random(seed ^ ((int)ForestClass.Log * 7919) ^ 0x10C5));
                ScatterYoungFirs();
                ScatterDeadwood();
                ScatterFernPatches();
                RingStumpsAndLogs();
                RingRocks();
                ScatterGrass();
                ScatterLooseClumps();

                ForestPlacement[] sorted = placements.ToArray();
                Array.Sort(sorted, Compare);

                foreach (string warning in warnings)
                    Debug.LogWarning("ForestScatter: " + warning);
                Debug.Log("ForestScatter: " + Summary(sorted));
                return new ForestPlan { seed = seed, grounded = false, placements = sorted };
            }

            // By cell, class, x and z. The remaining keys only make the order total, so the sort cannot
            // depend on how the list happened to be filled.
            static int Compare(ForestPlacement a, ForestPlacement b)
            {
                int order = a.cellX.CompareTo(b.cellX);
                if (order == 0) order = a.cellZ.CompareTo(b.cellZ);
                if (order == 0) order = ((int)a.cls).CompareTo((int)b.cls);
                if (order == 0) order = a.position.x.CompareTo(b.position.x);
                if (order == 0) order = a.position.z.CompareTo(b.position.z);
                if (order == 0) order = string.CompareOrdinal(a.prefabId, b.prefabId);
                if (order == 0) order = a.scale.CompareTo(b.scale);
                return order;
            }

            static string Summary(ForestPlacement[] sorted)
            {
                var counts = new int[Enum.GetValues(typeof(ForestClass)).Length];
                int core = 0;
                foreach (ForestPlacement placement in sorted)
                {
                    counts[(int)placement.cls]++;
                    if (ForestLayout.SignedDistanceToBoundary(new Vector2(placement.position.x, placement.position.z)) < 0f) core++;
                }
                return $"{sorted.Length} placements ({core} in the core): {counts[(int)ForestClass.TallFir]} tall firs, " +
                    $"{counts[(int)ForestClass.YoungFir]} young firs, {counts[(int)ForestClass.Boulder]} rocks, {counts[(int)ForestClass.Log]} logs, " +
                    $"{counts[(int)ForestClass.Stump]} stump patches, {counts[(int)ForestClass.Branch]} branches, " +
                    $"{counts[(int)ForestClass.FernPatch]} fern patches, {counts[(int)ForestClass.FernClump]} fern clumps, " +
                    $"{counts[(int)ForestClass.GrassPatch]} grass patches.";
            }

            // ---- 1. Set pieces ----

            void PlaceSetPieces()
            {
                System.Random random = Stream(ForestClass.Landmark);
                int fieldGroup = ++groups;
                var field = new List<Sample>();

                foreach (ForestLayout.SetPiece piece in ForestLayout.SetPieces)
                {
                    string prefabId = piece.prefabId ?? Pick(random, IdsOf(piece.cls));
                    float scale = Range(random, piece.minScale, piece.maxScale);
                    float clearance = Info(prefabId).clearance * scale;
                    Vector2 point = Validate(piece, prefabId, clearance);
                    float yaw = float.IsNaN(piece.yaw) ? PickYaw(random, point, Info(prefabId).variant) : piece.yaw;

                    bool inField = piece.landmark == ForestLayout.BoulderField && piece.cls == ForestClass.Boulder;
                    Place(prefabId, piece.cls, point, yaw, scale, random, piece.landmark, inField ? fieldGroup : 0);
                    if (inField)
                        field.Add(new Sample { point = point, radius = clearance });
                }

                ScatterBoulderField(random, fieldGroup, field);
            }

            /// <summary>
            /// A hand-listed piece obeys the same corridor and spawn rules as everything else. One that
            /// does not is pushed away from the nearest trail, a tenth of a metre at a time, for at most
            /// two metres.
            /// </summary>
            Vector2 Validate(ForestLayout.SetPiece piece, string prefabId, float clearance)
            {
                if (!InCore(piece.xz))
                    throw new InvalidOperationException($"Forest plan: the {piece.landmark} piece {prefabId} at ({piece.xz.x:0.0}, {piece.xz.y:0.0}) " +
                        $"is less than {ForestLayout.CoreInset} m inside the boundary.");
                if (SetPieceAllowed(piece.cls, piece.xz, clearance)) return piece.xz;

                Vector2 outward = piece.xz - ForestLayout.NearestTrailPoint(piece.xz);
                outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector2.right;
                for (int step = 1; step <= 20; step++)
                {
                    Vector2 moved = piece.xz + outward * (0.1f * step);
                    if (!InCore(moved) || !SetPieceAllowed(piece.cls, moved, clearance)) continue;
                    warnings.Add($"the {piece.landmark} piece {prefabId} at ({piece.xz.x:0.0}, {piece.xz.y:0.0}) broke the corridor or spawn rule " +
                        $"(clearance {clearance:0.00} m) and was pushed {0.1f * step:0.0} m away from the trail.");
                    return moved;
                }
                throw new InvalidOperationException($"Forest plan: the {piece.landmark} piece {prefabId} at ({piece.xz.x:0.0}, {piece.xz.y:0.0}) breaks the " +
                    $"corridor or spawn rule (clearance {clearance:0.00} m), and pushing it 2 m away from the trail did not free it.");
            }

            bool SetPieceAllowed(ForestClass cls, Vector2 point, float clearance)
            {
                if (clearance > 0f && !SolidAllowed(point, clearance)) return false;
                return cls != ForestClass.YoungFir || ForestLayout.DistanceToSpline(ForestLayout.Trail, point) >= ForestLayout.YoungFirTrailDistance;
            }

            /// <summary>
            /// The first view's additions of review 2 of the M4 captures (ForestLayout.SpawnExtras) of one class, from a stream of their
            /// own (so adding or removing one does not reshuffle the class's scatter), under the same corridor and spawn rules as the set
            /// pieces; one that breaks a rule or stands in something solid is left out with a warning.
            /// </summary>
            void PlaceSpawnExtras(ForestClass cls)
            {
                var random = new System.Random(seed ^ ((int)cls * 7919) ^ SpawnExtrasSalt);
                foreach (ForestLayout.SetPiece piece in ForestLayout.SpawnExtras)
                {
                    if (piece.cls != cls) continue;
                    string prefabId = piece.prefabId ?? Pick(random, IdsOf(piece.cls));
                    float scale = Range(random, piece.minScale, piece.maxScale);
                    float clearance = Info(prefabId).clearance * scale;
                    float body = cls == ForestClass.TallFir ? TallFirBody : cls == ForestClass.YoungFir ? YoungFirBody : 0f;
                    if (!InCore(piece.xz) || !SetPieceAllowed(cls, piece.xz, clearance) || (body > 0f && SolidBlocked(piece.xz, body, 0)))
                    {
                        warnings.Add($"the first view's added {prefabId} at ({piece.xz.x:0.0}, {piece.xz.y:0.0}) breaks the corridor, spawn or spacing rule and is left out.");
                        continue;
                    }
                    float yaw = cls == ForestClass.FernPatch ? Range(random, 0f, 360f) : PickYaw(random, piece.xz, Info(prefabId).variant);
                    Place(prefabId, cls, piece.xz, yaw, scale, random, piece.landmark, 0);
                }
            }

            /// <summary>
            /// The other nineteen rocks of the boulder field, on both sides of the east game trail. They
            /// may overlap each other by almost half and pile into a mass; the corridor stays free.
            /// </summary>
            void ScatterBoulderField(System.Random random, int group, List<Sample> field)
            {
                int tall = ForestLayout.BoulderFieldTallRocks - field.Count;
                string[] tallRocks = { "rock_moss_set_01_rock04", "rock_moss_set_02_rock11" };

                for (int attempt = 0; attempt < 6000 && field.Count < ForestLayout.BoulderFieldRocks; attempt++)
                {
                    bool isTall = tall > 0;
                    string prefabId = isTall ? Pick(random, tallRocks) : Pick(random, ForestPrefabIds.Boulders);
                    float scale = isTall
                        ? Range(random, ForestLayout.BoulderFieldTallMinScale, ForestLayout.BoulderFieldTallMaxScale)
                        : Range(random, ForestLayout.BoulderFieldMinScale, ForestLayout.BoulderFieldMaxScale);
                    float distance = ForestLayout.BoulderFieldRadius * Mathf.Sqrt((float)random.NextDouble());
                    Vector2 point = ForestLayout.BoulderFieldCentre + ForestLayout.FromBearing(Range(random, 0f, 360f)) * distance;

                    float clearance = Info(prefabId).clearance * scale;
                    if (!InCore(point) || !ScatterAllowed(point, clearance)) continue;
                    if (TooClose(field, point, clearance, 0.55f)) continue;
                    if (SolidBlocked(point, clearance * RockBodyShare, group)) continue;

                    Place(prefabId, ForestClass.Boulder, point, PickYaw(random, point, Info(prefabId).variant), scale, random, ForestLayout.BoulderField, group);
                    field.Add(new Sample { point = point, radius = clearance });
                    if (isTall) tall--;
                }
                if (field.Count < ForestLayout.BoulderFieldRocks)
                    warnings.Add($"the boulder field holds {field.Count} rocks, not {ForestLayout.BoulderFieldRocks}: no free ground was left.");
            }

            static bool TooClose(List<Sample> others, Vector2 point, float radius, float share)
            {
                foreach (Sample other in others)
                    if (Vector2.Distance(other.point, point) < share * (other.radius + radius)) return true;
                return false;
            }

            // ---- 2. Tall firs ----

            void ScatterTallFirs()
            {
                System.Random random = Stream(ForestClass.TallFir);
                Vector2 groveOffset = ForestTerrain.NoiseOffset(seed, 40), bankClumpOffset = ForestTerrain.NoiseOffset(seed, 47);
                float clearance = LargestClearance(ForestPrefabIds.TallFirs, ForestLayout.TallFirMaxScale);
                PlaceSpawnExtras(ForestClass.TallFir);

                Func<Vector2, float> radiusAt = point =>
                {
                    float outside = ForestLayout.SignedDistanceToBoundary(point);
                    bool core = outside <= -ForestLayout.CoreInset;
                    bool bank = outside >= ForestLayout.TallFirBankFrom && outside <= ForestLayout.TallFirBankTo;
                    if (!core && !bank) return 0f;
                    if (!ScatterAllowed(point, clearance) || SolidBlocked(point, TallFirBody, 0)) return 0f;
                    // Each point asks for its own spacing (review 1 of the M4 captures: at one radius the bank's firs stood in rows,
                    // the columns at the sides of overview_ortho).
                    if (bank)
                    {
                        float clump = ForestTerrain.Noise01(point.x, point.y, ForestLayout.TallFirBankClumpWavelength, bankClumpOffset);
                        float factor = Mathf.Lerp(ForestTerrain.Hash01(point.x, point.y, 7), ForestLayout.Smooth(0.3f, 0.7f, clump), ForestLayout.TallFirBankClumpShare);
                        return ForestLayout.TallFirBankSpacing * Mathf.Lerp(ForestLayout.TallFirBankJitterLow, ForestLayout.TallFirBankJitterHigh, factor);
                    }

                    if (fields.SlopeAt(point.x, point.y) > ForestLayout.TreeMaxCoreSlope) return 0f;
                    if (Vector2.Distance(point, ForestLayout.ClearingCentre) < ForestLayout.ClearingOpenRadius + 1f) return 0f;
                    foreach (ForestLayout.Landmark landmark in ForestLayout.Landmarks)
                    {
                        // D2: only the composed views keep the margin; the hollow keeps its wet bottom, the clearing its glade (above),
                        // and the rocks of the other landmarks keep firs off by the solid rule (ForestLayout.TallFirHollowKeepOut).
                        float keepOut = landmark.name == ForestLayout.SpawnView || landmark.name == ForestLayout.Deadfall
                            ? landmark.radius + ForestLayout.TallFirLandmarkMargin
                            : landmark.name == ForestLayout.FernHollow ? ForestLayout.TallFirHollowKeepOut : 0f;
                        if (Vector2.Distance(point, landmark.xz) < keepOut) return 0f;
                    }
                    // Groves and lone firs (ForestLayout.TallFirGroveSpacing): groves on the open ground between the thickets.
                    float thicket = ForestLayout.Smooth(ForestLayout.YoungFirThicketFrom, ForestLayout.YoungFirThicketTo,
                        Mathf.Clamp01(fields.Sample(fields.thicket, point.x, point.y)));
                    float grove = ForestLayout.Smooth(ForestLayout.TallFirGroveFrom, ForestLayout.TallFirGroveTo,
                        ForestTerrain.Noise01(point.x, point.y, ForestLayout.TallFirGroveNoiseWavelength, groveOffset))
                        * (1f - ForestLayout.TallFirGroveThicketCut * thicket);
                    return Mathf.Lerp(ForestLayout.TallFirLoneSpacing, ForestLayout.TallFirGroveSpacing, grove);
                };

                float extent = Mathf.Max(ForestLayout.HalfExtentX, ForestLayout.HalfExtentZ) + ForestLayout.TallFirBankTo + 1f;
                List<Sample> points = Poisson(random, extent, 6f, ForestLayout.TallFirLoneSpacing, radiusAt,
                    Existing(ForestClass.TallFir, ForestLayout.TallFirCoreSpacing));
                int groveFirs = 0;
                foreach (Sample sample in points)
                    if (ForestLayout.SignedDistanceToBoundary(sample.point) < 0f && sample.radius < 0.5f * (ForestLayout.TallFirGroveSpacing + ForestLayout.TallFirLoneSpacing)) groveFirs++;
                Debug.Log($"ForestScatter: {groveFirs} core tall firs stand in groves (spacing under {0.5f * (ForestLayout.TallFirGroveSpacing + ForestLayout.TallFirLoneSpacing):0} m).");

                foreach (Sample sample in points)
                {
                    float outside = ForestLayout.SignedDistanceToBoundary(sample.point);
                    bool bank = outside > 0f;
                    // The backdrop thins toward the foot of the bank (ForestLayout.TallFirBankThinning): kept with probability 1 - 0.5 (1 - d / 45).
                    if (bank && random.NextDouble() < ForestLayout.TallFirBankThinning * (1f - outside / ForestLayout.TallFirBankTo)) continue;

                    string prefabId = PickWeighted(random, ForestPrefabIds.TallFirs, bank ? ForestLayout.TallFirBankMix : ForestLayout.TallFirCoreMix);
                    // D2: a younger cohort among the core's firs (ForestLayout.TallFirYoungShare), so the stand is of mixed age.
                    bool younger = !bank && ForestTerrain.Hash01(sample.point.x, sample.point.y, 29) < ForestLayout.TallFirYoungShare;
                    float scale = younger
                        ? Range(random, ForestLayout.TallFirYoungMinScale, ForestLayout.TallFirYoungMaxScale)
                        : Range(random, ForestLayout.TallFirMinScale, ForestLayout.TallFirMaxScale);
                    Place(prefabId, ForestClass.TallFir, sample.point, PickYaw(random, sample.point, Info(prefabId).variant), scale, random, "", 0);
                }
            }

            // ---- 3. Young firs ----

            void ScatterYoungFirs()
            {
                System.Random random = Stream(ForestClass.YoungFir);
                float clearance = LargestClearance(ForestPrefabIds.YoungFirs, ForestLayout.YoungFirMaxScale);
                Vector2 deadfallCentre = LandmarkCentre(ForestLayout.Deadfall);
                Vector2 clumpOffset = ForestTerrain.NoiseOffset(seed, 45), gapOffset = ForestTerrain.NoiseOffset(seed, 46), bankClumpOffset = ForestTerrain.NoiseOffset(seed, 48);

                // The wanted spacing at a point (0 where no fir may stand), how far inside a stand it is (0 at an edge,
                // 1 deep inside: small firs at the edges, large ones inside) and whether it is a dense stand at all
                // (thicket, clump or wall), where a share of the firs is understorey.
                float Setting(Vector2 point, out float interior, out bool dense)
                {
                    interior = 0f;
                    dense = false;
                    float outside = ForestLayout.SignedDistanceToBoundary(point);
                    bool core = outside <= -ForestLayout.CoreInset;
                    bool bank = outside >= ForestLayout.CoreInset && outside <= ForestLayout.YoungFirBankBand;
                    if (!core && !bank) return 0f;
                    if (!ScatterAllowed(point, clearance) || SolidBlocked(point, YoungFirBody, 0)) return 0f;
                    // A scattered fir's crown stays off the lens of a first-person capture camera (review 1 of the M3
                    // captures: boundary_n stood inside a crown). The hand-placed firs of fir_close and crouch_hide are
                    // composed on purpose and not affected.
                    if (NearViewpoint(point, ForestLayout.YoungFirViewpointGap)) return 0f;
                    if (core)
                    {
                        if (fields.SlopeAt(point.x, point.y) > ForestLayout.TreeMaxCoreSlope) return 0f;
                        if (Vector2.Distance(point, ForestLayout.ClearingCentre) < ForestLayout.ClearingOpenRadius + 0.5f) return 0f;
                        // The fallen trunks lie in the gap they opened (review 1 of the M4 captures).
                        if (Vector2.Distance(point, deadfallCentre) < ForestLayout.YoungFirDeadfallKeepOut) return 0f;
                        if (Vector2.Distance(point, ForestLayout.HollowCentre) < ForestLayout.YoungFirHollowKeepOut) return 0f;
                        // Branches reach the ground: this far back they do not sweep across the tread.
                        if (fields.Sample(fields.trailDistance, point.x, point.y) < ForestLayout.YoungFirTrailDistance + 1f
                            && ForestLayout.DistanceToSpline(ForestLayout.Trail, point) < ForestLayout.YoungFirTrailDistance) return 0f;
                    }
                    // The rule's spacing is the least any two trunks keep. A stand is not packed as tight as it
                    // could be, so each point asks for a little more room, by a hash of where it is.
                    float hash = ForestTerrain.Hash01(point.x, point.y, 3);
                    if (bank)
                    {
                        // The wall: full density on the foot of the bank, thinning up the slope, with a few gaps; small firs
                        // in front (the mantle a forest edge has), larger ones behind.
                        float foot = 1f - ForestLayout.Smooth(ForestLayout.YoungFirWallFootTo, ForestLayout.YoungFirBankBand, outside);
                        // Gaps open only up the slope; the foot of the bank is closed (review 1 of the M4 captures).
                        float gap = ForestLayout.Smooth(ForestLayout.YoungFirWallGapFrom, ForestLayout.YoungFirWallGapTo,
                            ForestTerrain.Noise01(point.x, point.y, ForestLayout.YoungFirWallGapWavelength, gapOffset))
                            * ForestLayout.Smooth(ForestLayout.YoungFirWallGapStart - 2f, ForestLayout.YoungFirWallGapStart, outside);
                        // Review 2 of the M4 captures: one size, one spacing. Size and spacing also follow a clump noise along the bank;
                        // the clumps' spacing applies beyond the wall's closed foot.
                        float bankClump = ForestLayout.Smooth(0.35f, 0.65f, ForestTerrain.Noise01(point.x, point.y, ForestLayout.YoungFirBankClumpWavelength, bankClumpOffset));
                        interior = Mathf.Max(0.6f * ForestLayout.Smooth(1f, 10f, outside), bankClump);
                        dense = foot > 0.5f && gap < 0.5f;
                        float clumping = Mathf.Lerp(1f, Mathf.Lerp(ForestLayout.YoungFirBankClumpOpen, ForestLayout.YoungFirBankClumpClosed, bankClump),
                            ForestLayout.Smooth(ForestLayout.YoungFirWallGapStart - 2f, ForestLayout.YoungFirWallGapStart, outside));
                        return Mathf.Lerp(ForestLayout.YoungFirBankOuterSpacing, ForestLayout.YoungFirWallSpacing, foot)
                            * (1f + ForestLayout.YoungFirWallGapStretch * gap) * (1f + ForestLayout.YoungFirWallJitter * hash) * clumping;
                    }
                    float thicket = ForestLayout.Smooth(ForestLayout.YoungFirThicketFrom, ForestLayout.YoungFirThicketTo,
                        Mathf.Clamp01(fields.Sample(fields.thicket, point.x, point.y)));
                    // Outside the thickets: clumps of a few firs where the clump noise is high, glades between them.
                    float clump = ForestLayout.Smooth(ForestLayout.YoungFirClumpFrom, ForestLayout.YoungFirClumpTo,
                        ForestTerrain.Noise01(point.x, point.y, ForestLayout.YoungFirClumpWavelength, clumpOffset));
                    float open = Mathf.Lerp(ForestLayout.YoungFirGladeSpacing, ForestLayout.YoungFirClumpSpacing, clump);
                    interior = Mathf.Max(ForestLayout.Smooth(0.5f, 1f, thicket), 0.4f * clump);
                    dense = thicket > 0.5f || clump > 0.5f;
                    float spacing = Mathf.Lerp(open, ForestLayout.YoungFirThicketSpacing, thicket);
                    return spacing * (1f + Mathf.Lerp(ForestLayout.YoungFirOpenJitter, ForestLayout.YoungFirThicketJitter, Mathf.Max(thicket, clump)) * hash);
                }

                // The spawn thicket's understorey (ForestLayout.SpawnThicketUnderstorey): small c firs in the gaps of its
                // first row, under the same corridor and spawn rules as the hand-placed pieces.
                foreach (Vector2 along in ForestLayout.SpawnThicketUnderstorey)
                {
                    Vector2 point = ForestLayout.TrailPoint(along.x, along.y);
                    string prefabId = ForestPrefabIds.YoungFirs[2];
                    float scale = Range(random, ForestLayout.SpawnUnderstoreyMinScale, ForestLayout.SpawnUnderstoreyMaxScale);
                    if (!InCore(point) || !SetPieceAllowed(ForestClass.YoungFir, point, Info(prefabId).clearance * scale))
                    {
                        warnings.Add($"the spawn thicket's understorey fir at ({point.x:0.0}, {point.y:0.0}) breaks the corridor or spawn rule and is left out.");
                        continue;
                    }
                    Place(prefabId, ForestClass.YoungFir, point, PickYaw(random, point, Info(prefabId).variant), scale, random, ForestLayout.SpawnView, 0);
                }

                PlaceSpawnExtras(ForestClass.YoungFir);

                Func<Vector2, float> radiusAt = point => Setting(point, out _, out _);
                float extent = Mathf.Max(ForestLayout.HalfExtentX, ForestLayout.HalfExtentZ) + ForestLayout.YoungFirBankBand + 1f;
                float largest = Mathf.Max(ForestLayout.YoungFirGladeSpacing * (1f + ForestLayout.YoungFirOpenJitter),
                    ForestLayout.YoungFirBankOuterSpacing * (1f + ForestLayout.YoungFirWallGapStretch) * (1f + ForestLayout.YoungFirWallJitter));
                List<Sample> points = Poisson(random, extent, 2.5f, largest, radiusAt,
                    Existing(ForestClass.YoungFir, ForestLayout.YoungFirThicketSpacing));

                int understorey = 0, mid = 0;
                foreach (Sample sample in points)
                {
                    Setting(sample.point, out float interior, out bool dense);
                    float scale;
                    string prefabId;
                    // D2: in dense stands a share is understorey, as before; on the open ground between stands most firs are
                    // understorey in the canopy's gaps (ForestLayout.YoungFirGapUnderstoreyShare).
                    double roll = random.NextDouble();
                    bool understoreyHere = dense ? roll < ForestLayout.YoungFirUnderstoreyShare
                        : InCore(sample.point) && roll < ForestLayout.YoungFirGapUnderstoreyShare;
                    if (understoreyHere)
                    {
                        prefabId = PickWeighted(random, ForestPrefabIds.YoungFirs, ForestLayout.YoungFirUnderstoreyMix);
                        scale = Range(random, ForestLayout.YoungFirMinScale, ForestLayout.YoungFirUnderstoreyMaxScale);
                        understorey++;
                    }
                    else if (interior >= ForestLayout.YoungFirMidInterior && InCore(sample.point)
                        && ForestTerrain.Hash01(sample.point.x, sample.point.y, 17) < ForestLayout.YoungFirMidShare
                        && !NearViewpoint(sample.point, ForestLayout.YoungFirViewpointGap * ForestLayout.YoungFirMidMaxScale)
                        // A larger crown keeps further from the tread, and the larger trunk keeps the corridor rule.
                        && ForestLayout.DistanceToSpline(ForestLayout.Trail, sample.point) >= ForestLayout.YoungFirTrailDistance * ForestLayout.YoungFirMidMaxScale
                        && ScatterAllowed(sample.point, ForestKitSpec.YoungFirClearance * ForestLayout.YoungFirMidMaxScale))
                    {
                        // The pole-stage cohort (ForestLayout.YoungFirMidShare): b and c, never the heavy a.
                        prefabId = PickWeighted(random, ForestPrefabIds.YoungFirs, ForestLayout.YoungFirUnderstoreyMix);
                        scale = Range(random, ForestLayout.YoungFirMidMinScale, ForestLayout.YoungFirMidMaxScale);
                        mid++;
                    }
                    else
                    {
                        prefabId = PickWeighted(random, ForestPrefabIds.YoungFirs, ForestLayout.YoungFirMix);
                        scale = Range(random, Mathf.Lerp(ForestLayout.YoungFirEdgeMinScale, ForestLayout.YoungFirInnerMinScale, interior),
                            Mathf.Lerp(ForestLayout.YoungFirEdgeMaxScale, ForestLayout.YoungFirMaxScale, interior));
                    }
                    Place(prefabId, ForestClass.YoungFir, sample.point, PickYaw(random, sample.point, Info(prefabId).variant), scale, random, "", 0);
                }
                int bankFirs = 0;
                foreach (Sample sample in points)
                    if (ForestLayout.SignedDistanceToBoundary(sample.point) > 0f) bankFirs++;
                Debug.Log($"ForestScatter: {points.Count} young firs scattered beside {CountOf(ForestClass.YoungFir) - points.Count} hand-placed: " +
                    $"{points.Count - bankFirs} in the core, {bankFirs} on the bank; {understorey} of them understorey, {mid} pole-stage.");
            }

            // ---- 4. Boulders ----

            void ScatterBoulders()
            {
                System.Random random = Stream(ForestClass.Boulder);
                ScatterBoulderClusters(random);
                ScatterSingleBoulders(random);
                ScatterTrailBoulders(random);
            }

            /// <summary>
            /// Seven clusters on the steeper ground and the gully banks. Each is one large anchor, two or three rocks
            /// touching it and two small satellites on its downhill side, spaced by their measured rims so they
            /// overlap into one mass (section 7.2), and kept from stretching into a chain.
            /// </summary>
            void ScatterBoulderClusters(System.Random random)
            {
                var centres = new List<Vector2>();
                for (int cluster = 0; cluster < ForestLayout.BoulderClusters; cluster++)
                {
                    bool found = false;
                    Vector2 centre = default;
                    float bestScore = float.MinValue;
                    for (int attempt = 0; attempt < 80; attempt++)
                    {
                        Vector2 candidate = RandomCorePoint(random);
                        float score = (float)random.NextDouble();
                        if (ForestLayout.SignedDistanceToBoundary(candidate) > -6f) continue;
                        if (ForestLayout.DistanceToAnyTrail(candidate) < 7f) continue;
                        if (Vector2.Distance(candidate, ForestLayout.SpawnXZ) < 20f) continue;
                        // A cluster is a place of its own: clear of every set piece, and on ground a rock fits on.
                        bool crowded = SolidBlocked(candidate, 1.5f, 0);
                        foreach (ForestLayout.Landmark landmark in ForestLayout.Landmarks)
                            if (Vector2.Distance(landmark.xz, candidate) < landmark.radius + 4f) { crowded = true; break; }
                        foreach (Vector2 other in centres)
                            if (Vector2.Distance(other, candidate) < ForestLayout.BoulderClusterCentreSpacing) { crowded = true; break; }
                        if (crowded) continue;

                        score += Mathf.Clamp01(fields.SlopeAt(candidate.x, candidate.y) / 10f);
                        float gully = fields.Sample(fields.gullyDistance, candidate.x, candidate.y);
                        if (gully >= 2.5f && gully <= 7f) score += 1f;
                        if (score <= bestScore) continue;
                        bestScore = score; centre = candidate; found = true;
                    }
                    if (!found)
                    {
                        warnings.Add($"boulder cluster {cluster + 1} found no free ground {ForestLayout.BoulderClusterCentreSpacing} m from the others.");
                        continue;
                    }
                    centres.Add(centre);

                    int group = ++groups;
                    // One kind of stone per cluster. The two scanned sets differ in colour and surface; mixed in
                    // one group they read as rocks brought from two places (review 1 of the M2 captures).
                    bool set01 = random.Next(2) == 0;
                    string[] clusterRocks = RockSet(set01 ? "rock_moss_set_01" : "rock_moss_set_02");
                    string[] anchors = set01 ? ForestLayout.BoulderClusterAnchorsSet01 : ForestLayout.BoulderClusterAnchorsSet02;
                    var members = new List<ClusterRock>();

                    // The anchor: one large rock on or near the centre.
                    for (int attempt = 0; attempt < 60 && members.Count == 0; attempt++)
                    {
                        string prefabId = Pick(random, anchors);
                        float scale = Range(random, ForestLayout.BoulderAnchorMinScale, ForestLayout.BoulderAnchorMaxScale);
                        Vector2 point = attempt == 0 ? centre : centre + ForestLayout.FromBearing(Range(random, 0f, 360f)) * Range(random, 0f, 3f);
                        float yaw = PickYaw(random, point, Info(prefabId).variant);
                        float clearance = Info(prefabId).clearance * scale;
                        if (!InCore(point) || !ScatterAllowed(point, clearance) || SolidBlocked(point, clearance * RockBodyShare, group)) continue;
                        Place(prefabId, ForestClass.Boulder, point, yaw, scale, random, "", group);
                        members.Add(new ClusterRock { point = point, prefabId = prefabId, scale = scale, yaw = yaw });
                    }
                    if (members.Count == 0)
                    {
                        warnings.Add($"boulder cluster {cluster + 1} at ({centre.x:0}, {centre.y:0}) found no place for its anchor.");
                        continue;
                    }

                    // Two satellites go to the downhill side: what rolled off the pile.
                    Vector3 normal = FootprintNormal(members[0].point, 2f);
                    Vector2 downhill = new Vector2(normal.x, normal.z);
                    float downhillBearing = downhill.sqrMagnitude > 1e-6f ? ForestLayout.Bearing(downhill) : Range(random, 0f, 360f);

                    int wantedMembers = random.Next(ForestLayout.BoulderMembersMin, ForestLayout.BoulderMembersMax + 1);
                    int placedMembers = AddTouching(random, group, clusterRocks, members, wantedMembers, true,
                        ForestLayout.BoulderMemberMinScale, ForestLayout.BoulderMemberMaxScale, float.NaN);
                    int placedSatellites = AddTouching(random, group, clusterRocks, members, ForestLayout.BoulderSatellites, false,
                        ForestLayout.BoulderSatelliteMinScale, ForestLayout.BoulderSatelliteMaxScale, downhillBearing);
                    if (members.Count < ForestLayout.BoulderClusterMin)
                        warnings.Add($"boulder cluster {cluster + 1} at ({centre.x:0}, {centre.y:0}) holds only {members.Count} rocks.");
                    clusterLog.Add($"{members.Count} ({placedMembers} touching the anchor, {placedSatellites} satellites, axis ratio {AxisRatio(members, null):0.00})");
                }
                Debug.Log("ForestScatter: boulder clusters " + string.Join("; ", clusterLog) + ".");
            }

            readonly List<string> clusterLog = new List<string>();

            struct ClusterRock
            {
                public Vector2 point;
                public string prefabId;
                public float scale, yaw;
            }

            /// <summary>
            /// Adds up to <paramref name="wanted"/> rocks to a cluster, each one touching a rock already in it:
            /// the anchor only (<paramref name="anchorOnly"/>) or any member, toward any bearing or within 60
            /// degrees of <paramref name="towardBearing"/>. Two rocks stand at BoulderTouch times the sum of their
            /// measured rims facing each other, so they overlap a little and read as piled.
            /// </summary>
            int AddTouching(System.Random random, int group, string[] rocks, List<ClusterRock> members, int wanted, bool anchorOnly,
                float minScale, float maxScale, float towardBearing)
            {
                int placed = 0;
                for (int attempt = 0; attempt < 200 && placed < wanted; attempt++)
                {
                    string prefabId = Pick(random, rocks);
                    float scale = Range(random, minScale, maxScale);
                    ClusterRock host = anchorOnly ? members[0] : members[random.Next(members.Count)];
                    float bearing = float.IsNaN(towardBearing) ? Range(random, 0f, 360f) : towardBearing + Range(random, -60f, 60f);
                    float yaw = Range(random, 0f, 360f);

                    float reach = ForestLayout.BoulderTouch * (RimToward(host.prefabId, host.scale, host.yaw, bearing) + RimToward(prefabId, scale, yaw, bearing + 180f));
                    Vector2 point = host.point + ForestLayout.FromBearing(bearing) * reach;
                    float clearance = Info(prefabId).clearance * scale;
                    if (!InCore(point) || !ScatterAllowed(point, clearance)) continue;
                    if (SolidBlocked(point, clearance * RockBodyShare, group)) continue;

                    // Not into another member more than the touch allows.
                    bool crowded = false;
                    foreach (ClusterRock other in members)
                    {
                        if (other.point == host.point) continue;
                        float toward = ForestLayout.Bearing(point - other.point);
                        float least = 0.8f * ForestLayout.BoulderTouch * (RimToward(other.prefabId, other.scale, other.yaw, toward) + RimToward(prefabId, scale, yaw, toward + 180f));
                        if (Vector2.Distance(point, other.point) < least) { crowded = true; break; }
                    }
                    if (crowded) continue;

                    var candidate = new ClusterRock { point = point, prefabId = prefabId, scale = scale, yaw = yaw };
                    if (members.Count >= 2 && AxisRatio(members, candidate) > ForestLayout.BoulderClusterMaxAxisRatio) continue;

                    Place(prefabId, ForestClass.Boulder, point, yaw, scale, random, "", group);
                    members.Add(candidate);
                    placed++;
                }
                return placed;
            }

            /// <summary>How far a rock reaches toward a world bearing, from its measured rim (its bounding circle when unmeasured).</summary>
            float RimToward(string prefabId, float scale, float yaw, float worldBearing)
            {
                float radius = ForestLayout.RockRimRadius(prefabId, scale, yaw, worldBearing);
                return radius >= 0f ? radius : Info(prefabId).clearance * scale * RockBodyShare;
            }

            /// <summary>
            /// Elongation of a cluster: the square root of the ratio of the principal second moments of its rocks,
            /// each counted as a disc of its mean rim radius, so two touching rocks are not already a line.
            /// </summary>
            float AxisRatio(List<ClusterRock> members, ClusterRock? extra)
            {
                var rocks = new List<ClusterRock>(members);
                if (extra.HasValue) rocks.Add(extra.Value);
                double weight = 0, mx = 0, mz = 0;
                var weights = new double[rocks.Count];
                for (int i = 0; i < rocks.Count; i++)
                {
                    float radius = MeanRim(rocks[i].prefabId) * rocks[i].scale;
                    weights[i] = radius * radius;
                    weight += weights[i]; mx += weights[i] * rocks[i].point.x; mz += weights[i] * rocks[i].point.y;
                }
                mx /= weight; mz /= weight;
                double xx = 0, zz = 0, xz = 0, own = 0;
                for (int i = 0; i < rocks.Count; i++)
                {
                    double dx = rocks[i].point.x - mx, dz = rocks[i].point.y - mz;
                    xx += weights[i] * dx * dx; zz += weights[i] * dz * dz; xz += weights[i] * dx * dz;
                    own += weights[i] * weights[i] / 4.0;   // a disc's second moment about any axis through it: w * r^2 / 4
                }
                xx = (xx + own) / weight; zz = (zz + own) / weight; xz /= weight;
                double mean = 0.5 * (xx + zz), spread = Math.Sqrt(0.25 * (xx - zz) * (xx - zz) + xz * xz);
                double major = mean + spread, minor = Math.Max(mean - spread, 1e-6);
                return (float)Math.Sqrt(major / minor);
            }

            float MeanRim(string prefabId)
            {
                if (!ForestLayout.RockLips.TryGetValue(prefabId, out ForestLayout.RockLip lip)) return Info(prefabId).clearance * RockBodyShare;
                float sum = 0f;
                foreach (float radius in lip.rimRadius) sum += radius;
                return sum / lip.rimRadius.Length;
            }

            /// <summary>The boulders of one scanned set, in the order of <see cref="ForestPrefabIds.Boulders"/>.</summary>
            static string[] RockSet(string setId)
            {
                var rocks = new List<string>();
                foreach (string id in ForestPrefabIds.Boulders)
                    if (id.StartsWith(setId, StringComparison.Ordinal)) rocks.Add(id);
                if (rocks.Count == 0)
                    throw new InvalidOperationException($"Forest plan: no boulder prefab belongs to the set '{setId}'.");
                return rocks.ToArray();
            }

            void ScatterSingleBoulders(System.Random random)
            {
                int placed = 0;
                for (int attempt = 0; attempt < 3000 && placed < ForestLayout.BoulderSingles; attempt++)
                {
                    string prefabId = Pick(random, ForestPrefabIds.Boulders);
                    float scale = Range(random, ForestLayout.BoulderMinScale, ForestLayout.BoulderMaxScale);
                    Vector2 point = RandomCorePoint(random);
                    float clearance = Info(prefabId).clearance * scale;
                    if (!InCore(point) || !ScatterAllowed(point, clearance)) continue;
                    if (Vector2.Distance(point, ForestLayout.ClearingCentre) < ForestLayout.ClearingRadius) continue;
                    // A single is a rock on its own: well away from every other rock.
                    if (NearestOfClass(point, ForestClass.Boulder) < 8f || SolidBlocked(point, clearance * RockBodyShare, 0)) continue;

                    Place(prefabId, ForestClass.Boulder, point, PickYaw(random, point, Info(prefabId).variant), scale, random, "", 0);
                    placed++;
                }
                if (placed < ForestLayout.BoulderSingles)
                    warnings.Add($"only {placed} of {ForestLayout.BoulderSingles} single boulders found free ground.");
            }

            /// <summary>Six rocks beside the first 30 m of trail, their surface 2.5 to 5 m from the centre line.</summary>
            void ScatterTrailBoulders(System.Random random)
            {
                int placed = 0;
                for (int attempt = 0; attempt < 3000 && placed < ForestLayout.BoulderTrailRocks; attempt++)
                {
                    string prefabId = Pick(random, ForestPrefabIds.Boulders);
                    float scale = Range(random, ForestLayout.BoulderMinScale, ForestLayout.BoulderMaxScale);
                    float clearance = Info(prefabId).clearance * scale;
                    float side = (placed + attempt) % 2 == 0 ? 1f : -1f;
                    float lateral = clearance + Range(random, ForestLayout.CorridorClearance + 0.1f, 5f);
                    Vector2 point = ForestLayout.TrailPoint(Range(random, 2f, ForestLayout.BoulderTrailLength), side * lateral);

                    float surface = ForestLayout.DistanceToSpline(ForestLayout.Trail, point) - clearance;
                    if (surface > 5f || !InCore(point) || !ScatterAllowed(point, clearance)) continue;
                    if (NearestOfClass(point, ForestClass.Boulder) < 3.5f + clearance || SolidBlocked(point, clearance * RockBodyShare, 0)) continue;

                    Place(prefabId, ForestClass.Boulder, point, PickYaw(random, point, Info(prefabId).variant), scale, random, "", 0);
                    placed++;
                }
                if (placed < ForestLayout.BoulderTrailRocks)
                    warnings.Add($"only {placed} of {ForestLayout.BoulderTrailRocks} trail-side boulders found free ground.");
            }

            // ---- 5. Deadwood ----

            void ScatterDeadwood()
            {
                // The logs are placed before the young firs (Run).
                ScatterStumps(Stream(ForestClass.Stump));
                ScatterBranches(Stream(ForestClass.Branch));
            }

            /// <summary>Five logs beside the game trails, their surface 3 to 6 m from the centre line.</summary>
            void ScatterLogs(System.Random random)
            {
                string prefabId = ForestPrefabIds.Logs[0];
                int placed = 0;
                for (int attempt = 0; attempt < 4000 && placed < ForestLayout.ScatteredLogs; attempt++)
                {
                    Vector2[] spline = random.Next(2) == 0 ? ForestLayout.WestTrail : ForestLayout.EastTrail;
                    float scale = Range(random, ForestLayout.LogMinScale, ForestLayout.LogMaxScale);
                    float clearance = Info(prefabId).clearance * scale;
                    Vector2 onTrail = ForestLayout.PointAtDistance(spline, Range(random, 0f, ForestLayout.SplineLength(spline)), out Vector2 tangent);
                    float side = random.Next(2) == 0 ? 1f : -1f;
                    Vector2 point = onTrail + ForestLayout.RightOf(tangent) * (side * (clearance + Range(random, 3.1f, 6f)));
                    float yaw = Range(random, 0f, 360f);

                    float surface = ForestLayout.DistanceToGameTrails(point) - clearance;
                    if (surface < 3f || surface > 6f || !InCore(point) || !ScatterAllowed(point, clearance)) continue;
                    if (fields.SlopeAt(point.x, point.y) > 15f) continue;
                    if (Vector2.Distance(point, ForestLayout.ClearingCentre) < ForestLayout.ClearingOpenRadius + 2f) continue;
                    if (NearestOfClass(point, ForestClass.Log) < 18f || LogBlocked(point, yaw, scale)) continue;

                    Place(prefabId, ForestClass.Log, point, yaw, scale, random, "", 0);
                    placed++;
                }
                if (placed < ForestLayout.ScatteredLogs)
                    warnings.Add($"only {placed} of {ForestLayout.ScatteredLogs} scattered logs found free ground.");
            }

            /// <summary>
            /// D2 (density pass): fallen trunks anywhere in the core among the trees, not only beside the game trails, at least
            /// ForestLayout.LogSpacing apart, under the same corridor, bedding and solid rules as the five beside the trails.
            /// </summary>
            void ScatterForestLogs(System.Random random)
            {
                string prefabId = ForestPrefabIds.Logs[0];
                Vector2 deadfallCentre = LandmarkCentre(ForestLayout.Deadfall);
                int placed = 0;
                for (int attempt = 0; attempt < 8000 && placed < ForestLayout.ForestLogs; attempt++)
                {
                    float scale = Range(random, ForestLayout.ForestLogMinScale, ForestLayout.LogMaxScale);
                    float clearance = Info(prefabId).clearance * scale;
                    Vector2 point = RandomCorePoint(random);
                    float yaw = Range(random, 0f, 360f);
                    if (!InCore(point) || ForestLayout.SignedDistanceToBoundary(point) > -3f || !ScatterAllowed(point, clearance)) continue;
                    if (fields.SlopeAt(point.x, point.y) > 15f) continue;
                    if (Vector2.Distance(point, ForestLayout.ClearingCentre) < ForestLayout.ClearingOpenRadius + 2f) continue;
                    if (Vector2.Distance(point, deadfallCentre) < 9f || NearViewpoint(point, 3f + 2f * scale)) continue;
                    if (NearestOfClass(point, ForestClass.Log) < ForestLayout.LogSpacing || LogBlocked(point, yaw, scale)) continue;

                    Place(prefabId, ForestClass.Log, point, yaw, scale, random, "", 0);
                    placed++;
                }
                if (placed < ForestLayout.ForestLogs)
                    warnings.Add($"only {placed} of {ForestLayout.ForestLogs} logs among the trees found free ground.");
            }

            /// <summary>True when a log here would lie in something solid, bridge the gully or hang over a bank.</summary>
            bool LogBlocked(Vector2 point, float yaw, float scale)
            {
                Vector2 axis = LogAxis(yaw);
                for (int disc = -1; disc <= 1; disc++)
                {
                    Vector2 part = point + axis * (disc * LogDiscOffset * scale);
                    if (SolidBlocked(part, LogDiscRadius * scale, 0)) return true;
                    // The sink beds a log on even ground. Across the gully or on a bank its middle would float.
                    if (fields.Sample(fields.gullyDistance, part.x, part.y) < ForestLayout.GullyHalfWidth + LogDiscRadius * scale) return true;
                    if (fields.SlopeAt(part.x, part.y) > 15f) return true;
                }
                // A rigid trunk across a mound is buried in the middle and has an end in the air. The ground
                // under both ends must lie near the line through the pivot that the log's tilt can follow.
                float reach = ForestLayout.LogBedReach * scale;
                float centre = fields.HeightAt(point.x, point.y);
                float ahead = fields.HeightAt(point.x + axis.x * reach, point.y + axis.y * reach);
                float behind = fields.HeightAt(point.x - axis.x * reach, point.y - axis.y * reach);
                if (Mathf.Abs(0.5f * (ahead + behind) - centre) > ForestLayout.LogBedTolerance) return true;
                return false;
            }

            /// <summary>Where a log's long axis (its local X) points on the ground after a yaw.</summary>
            static Vector2 LogAxis(float yaw)
            {
                float radians = yaw * Mathf.Deg2Rad;
                return new Vector2(Mathf.Cos(radians), -Mathf.Sin(radians));
            }

            /// <summary>Seven stump patches on open litter, at least 12 m from each other and from the three set pieces.</summary>
            void ScatterStumps(System.Random random)
            {
                int placed = 0;
                for (int attempt = 0; attempt < 4000 && placed < ForestLayout.ScatteredStumps; attempt++)
                {
                    string prefabId = Pick(random, ForestPrefabIds.Stumps);
                    float scale = Range(random, ForestLayout.StumpMinScale, ForestLayout.StumpMaxScale);
                    Vector2 point = RandomCorePoint(random);
                    float clearance = Info(prefabId).clearance * scale;
                    if (!InCore(point) || !ScatterAllowed(point, clearance)) continue;
                    // Litter or stony soil: not the humus of a thicket, not the mud of the hollow or the gully.
                    if (fields.Sample(fields.thicket, point.x, point.y) > 0.5f) continue;
                    if (Vector2.Distance(point, ForestLayout.HollowCentre) < ForestLayout.HollowRadius) continue;
                    if (fields.Sample(fields.gullyDistance, point.x, point.y) < 4f) continue;
                    if (Vector2.Distance(point, ForestLayout.ClearingCentre) < ForestLayout.ClearingOpenRadius + 2f) continue;
                    if (fields.SlopeAt(point.x, point.y) > 14f) continue;
                    if (NearestOfClass(point, ForestClass.Stump) < ForestLayout.StumpSpacing || SolidBlocked(point, clearance * StumpBodyShare, 0)) continue;

                    Place(prefabId, ForestClass.Stump, point, PickYaw(random, point, Info(prefabId).variant), scale, random, "", 0);
                    placed++;
                }
                if (placed < ForestLayout.ScatteredStumps)
                    warnings.Add($"only {placed} of {ForestLayout.ScatteredStumps} scattered stump patches found free ground.");
            }

            /// <summary>Forty-five dry branches where they would have fallen: under the tall firs of the core.</summary>
            void ScatterBranches(System.Random random)
            {
                var trunks = new List<Vector2>();
                foreach (ForestPlacement placement in placements)
                {
                    var trunk = new Vector2(placement.position.x, placement.position.z);
                    if (placement.cls == ForestClass.TallFir && InCore(trunk)) trunks.Add(trunk);
                }
                if (trunks.Count == 0)
                {
                    warnings.Add("no tall fir stands in the core, so no dry branches were scattered.");
                    return;
                }

                int underTrees = ForestLayout.ScatteredBranches - ForestLayout.ScatteredBranchesInThickets;
                int placed = 0;
                for (int attempt = 0; attempt < 6000 && placed < underTrees; attempt++)
                {
                    Vector2 trunk = trunks[random.Next(trunks.Count)];
                    Vector2 point = trunk + ForestLayout.FromBearing(Range(random, 0f, 360f)) * Range(random, 1.2f, 3.5f);
                    if (!InCore(point) || !BranchAllowed(point)) continue;
                    PlaceBranch(random, point, Range(random, 0f, 360f), "", WholeBranches);
                    placed++;
                }

                // Review 1 of the M4 captures: deadwood inside the young-fir thickets too, where a crouching player hides.
                int inThickets = 0;
                for (int attempt = 0; attempt < 6000 && inThickets < ForestLayout.ScatteredBranchesInThickets; attempt++)
                {
                    Vector2 point = RandomCorePoint(random);
                    if (!InCore(point) || fields.Sample(fields.thicket, point.x, point.y) < 0.75f || !BranchAllowed(point)) continue;
                    PlaceBranch(random, point, Range(random, 0f, 360f), "", WholeBranches);
                    inThickets++;
                }
                if (placed + inThickets < ForestLayout.ScatteredBranches)
                    warnings.Add($"only {placed} of {underTrees} branches under tall firs and {inThickets} of {ForestLayout.ScatteredBranchesInThickets} in thickets found free ground.");

                // Review 2 of the M4 captures: deadwood on the bank, which was bare soil.
                int onBank = 0;
                float reach = Mathf.Max(ForestLayout.HalfExtentX, ForestLayout.HalfExtentZ) + ForestLayout.BankBranchOuter;
                for (int attempt = 0; attempt < 6000 && onBank < ForestLayout.BankBranches; attempt++)
                {
                    var point = new Vector2(Range(random, -reach, reach), Range(random, -reach, reach));
                    float outside = ForestLayout.SignedDistanceToBoundary(point);
                    if (outside < ForestLayout.CoreInset || outside > ForestLayout.BankBranchOuter) continue;
                    if (fields.SlopeAt(point.x, point.y) > 30f || !BranchAllowed(point) || NearViewpoint(point, 2f)) continue;
                    PlaceBranch(random, point, Range(random, 0f, 360f), "", WholeBranches);
                    onBank++;
                }
                if (onBank < ForestLayout.BankBranches)
                    warnings.Add($"only {onBank} of {ForestLayout.BankBranches} branches on the bank found free ground.");
            }

            // Review 1 of the M4 captures: the deadfall's three trunks were buried in fern patches (each over two metres across)
            // and only a few pale fragments showed. No patch stands this close to the long axis of a deadfall trunk.
            const float DeadfallFernGap = 1.5f;
            // Half the length of dead_tree_trunk_02 (4.05 m) at scale 1.
            const float LogHalfLength = 2.0f;
            List<(Vector2 a, Vector2 b)> deadfallLogs;

            bool NearDeadfallLog(Vector2 point, float gap)
            {
                if (deadfallLogs == null)
                {
                    deadfallLogs = new List<(Vector2, Vector2)>();
                    foreach (ForestPlacement placement in placements)
                    {
                        if (placement.cls != ForestClass.Log || placement.landmark != ForestLayout.Deadfall) continue;
                        Vector3 axis3 = placement.rotation * Vector3.right;
                        Vector2 axis = new Vector2(axis3.x, axis3.z).normalized * (LogHalfLength * placement.scale);
                        var centre = new Vector2(placement.position.x, placement.position.z);
                        deadfallLogs.Add((centre - axis, centre + axis));
                    }
                }
                foreach ((Vector2 a, Vector2 b) in deadfallLogs)
                {
                    Vector2 ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                    if (Vector2.Distance(point, a + ab * t) < gap) return true;
                }
                return false;
            }

            static Vector2 LandmarkCentre(string name)
            {
                foreach (ForestLayout.Landmark landmark in ForestLayout.Landmarks)
                    if (landmark.name == name) return landmark.xz;
                throw new InvalidOperationException($"ForestLayout has no landmark named {name}.");
            }

            bool BranchAllowed(Vector2 point)
            {
                // Off the tread and its shoulders, outside anything solid, not on top of another branch.
                if (fields.Sample(fields.trailDistance, point.x, point.y) < ForestLayout.ShoulderOuter) return false;
                return !CoverBlocked(point, 0.3f) && NearestOfClass(point, ForestClass.Branch) >= 1f;
            }

            // dry_branches_medium_01_b is two finger-size fragments in the scan. Alone on open litter they are
            // stray dark lumps (review 1), so only the two whole branches are scattered; the fragments stay
            // in the ring rule, as debris beside a stump or a log.
            static readonly string[] WholeBranches = { "dry_branches_medium_01_a", "dry_branches_medium_01_c" };

            void PlaceBranch(System.Random random, Vector2 point, float yaw, string landmark, string[] from)
            {
                string prefabId = Pick(random, from);
                Place(prefabId, ForestClass.Branch, point, yaw, Range(random, ForestLayout.BranchMinScale, ForestLayout.BranchMaxScale), random, landmark, 0);
            }

            // ---- 6. Fern patches ----

            void ScatterFernPatches()
            {
                System.Random random = Stream(ForestClass.FernPatch);
                Vector2 noiseOffset = ForestTerrain.NoiseOffset(seed, 41);
                PlaceSpawnExtras(ForestClass.FernPatch);
                float extent = Mathf.Max(ForestLayout.HalfExtentX, ForestLayout.HalfExtentZ) + ForestLayout.FernPatchBankBand + 1f;
                List<Sample> existing = Existing(ForestClass.FernPatch, 1.2f);
                int room = ForestLayout.FernPatchCap - existing.Count;

                // The field asks for more patches than the cap allows. Thinning the whole field by one
                // factor left patches three metres apart everywhere: single ferns on bare ground, no
                // drift to be seen (first M2 captures). So the density is thinned only as far as fronds
                // still meet inside a drift, and what is left over the cap is taken off the drifts' size.
                // The floor of the fern hollow is not thinned at all.
                float thinning = FernThinning(noiseOffset, extent, room * 0.93f, out float narrowing);
                Func<Vector2, float> radiusAt = point =>
                {
                    float density = FernDensity(point, noiseOffset, narrowing, out float floor);
                    if ((density < ForestLayout.FernMinDensity && floor < ForestLayout.FernMinDensity) || CoverBlocked(point, 0.2f)) return 0f;
                    if (NearGroundViewpoint(point, GroundViewFernGap) || InFrontOfCrouchViewpoint(point)) return 0f;
                    if (NearDeadfallLog(point, DeadfallFernGap)) return 0f;
                    return FernSpacing(Mathf.Max(density * thinning, floor)) * FernSpacingJitter(point);
                };
                List<Sample> points = Poisson(random, extent, 2.5f, 6f * ForestLayout.FernSpacingJitterHigh, radiusAt, existing);

                // The estimate is not exact. Whatever is over the cap goes by position hash, not from the end of the list.
                if (points.Count > room)
                {
                    var ranked = new List<Sample>(points);
                    ranked.Sort((a, b) => ForestTerrain.Hash01(a.point.x, a.point.y, 5).CompareTo(ForestTerrain.Hash01(b.point.x, b.point.y, 5)));
                    float cut = ForestTerrain.Hash01(ranked[room].point.x, ranked[room].point.y, 5);
                    points.RemoveAll(sample => ForestTerrain.Hash01(sample.point.x, sample.point.y, 5) >= cut);
                }
                Debug.Log($"ForestScatter: fern field thinned to {thinning:0.00}, drift threshold raised by {narrowing:0.00}; " +
                    $"{points.Count} patches scattered beside {existing.Count} hand-placed (cap {ForestLayout.FernPatchCap}).");

                foreach (Sample sample in points)
                {
                    string prefabId = Pick(random, ForestPrefabIds.FernPatches);
                    // Large where the ferns are densest (a drift's core, a collar, the hollow), small at a drift's
                    // edge and on its own: a size hierarchy, not one size.
                    float density = FernDensity(sample.point, noiseOffset, narrowing, out float floor);
                    float core = ForestLayout.Smooth(ForestLayout.FernEdgeDensity, ForestLayout.FernCoreDensity, Mathf.Max(density * thinning, floor));
                    float scale = Range(random, Mathf.Lerp(ForestLayout.FernEdgeMinScale, ForestLayout.FernCoreMinScale, core),
                        Mathf.Lerp(ForestLayout.FernEdgeMaxScale, ForestLayout.FernCoreMaxScale, core));
                    Place(prefabId, ForestClass.FernPatch, sample.point, Range(random, 0f, 360f), scale, random, "", 0);
                }
            }

            static float FernSpacing(float density) => Mathf.Clamp(1.1f / Mathf.Sqrt(density), ForestLayout.FernSpacingMin, 6f);

            /// <summary>Each sample asks for its own share of the rule's spacing, by a hash of where it is.</summary>
            static float FernSpacingJitter(Vector2 point)
            {
                return Mathf.Lerp(ForestLayout.FernSpacingJitterLow, ForestLayout.FernSpacingJitterHigh, ForestTerrain.Hash01(point.x, point.y, 13));
            }

            // The mean of 1 / f^2 for f spread evenly between the jitter's ends: how much denser the jittered run packs.
            static readonly float FernJitterPacking =
                (1f / ForestLayout.FernSpacingJitterLow - 1f / ForestLayout.FernSpacingJitterHigh) / (ForestLayout.FernSpacingJitterHigh - ForestLayout.FernSpacingJitterLow);

            /// <summary>
            /// Section 7.2: ferns per square metre. Drifts from noise (denser in the hollow and along the
            /// gully), a collar on one side of some rocks, stumps and logs, runs along the trail's shoulders;
            /// none on the tread, fewer on the game trails and in the clearing, none on rock-steep ground.
            /// <paramref name="floor"/> is the part the cap may not thin: the ring of the fern hollow.
            /// </summary>
            float FernDensity(Vector2 point, Vector2 noiseOffset, float narrowing, out float floor) => FernDensity(point, noiseOffset, narrowing, out floor, out _);

            float FernDensity(Vector2 point, Vector2 noiseOffset, float narrowing, out float floor, out float drift)
            {
                floor = 0f;
                drift = 0f;
                float outside = ForestLayout.SignedDistanceToBoundary(point);
                if (outside > ForestLayout.FernPatchBankBand || (outside > -ForestLayout.CoreInset && outside < ForestLayout.CoreInset)) return 0f;
                if (fields.SlopeAt(point.x, point.y) > 35f) return 0f;

                float trail = fields.Sample(fields.trailDistance, point.x, point.y);
                float onTrail = ForestLayout.Smooth(ForestLayout.TreadHalfWidth, 1.8f, trail);
                if (onTrail <= 0f) return 0f;

                float toHollow = Vector2.Distance(point, ForestLayout.HollowCentre);
                bool wet = toHollow < ForestLayout.HollowRadius || fields.Sample(fields.gullyDistance, point.x, point.y) < 6f;
                // narrowing raises the threshold: the drifts keep their places and get smaller, with a steeper edge.
                float low = 0.5f + narrowing;
                float edge = Mathf.Lerp(ForestLayout.FernDriftEdge, ForestLayout.FernDriftNarrowEdge, narrowing / ForestLayout.FernMaxNarrowing);
                drift = ForestLayout.Smooth(low, low + edge, ForestTerrain.Noise01(point.x, point.y, ForestLayout.FernNoiseWavelength, noiseOffset));
                float density = 0.04f + 1.1f * drift * (wet ? 1.5f : 1f);
                density += ForestLayout.FernCollarDensity * FernCollar(point, 1.5f, drift);
                // D2: carpets in the shade of the tall firs' crowns (ForestLayout.FernShade*).
                density += ForestLayout.FernShadeDensity * ForestLayout.Smooth(ForestLayout.FernShadeFrom, ForestLayout.FernShadeTo, TallFirShade(point));
                // Review 1 of the M4 captures: ferns along the edges of the young-fir stands (and the foot of the bank), where the
                // bottom metre of a stand was bare stems over bare litter.
                float stand = fields.Sample(fields.thicket, point.x, point.y);
                density += ForestLayout.FernThicketEdgeDensity
                    * ForestLayout.Smooth(ForestLayout.FernThicketEdgeFrom, ForestLayout.FernThicketEdgeFrom + 0.12f, stand)
                    * (1f - ForestLayout.Smooth(ForestLayout.FernThicketEdgeTo, ForestLayout.FernThicketEdgeTo + 0.12f, stand));

                // Runs of ferns on the shoulders, up to the edge of the tread.
                if (trail < ForestLayout.FernShoulderOuter + 0.5f)
                {
                    float band = ForestLayout.Smooth(ForestLayout.FernShoulderInner - 0.2f, ForestLayout.FernShoulderInner + 0.2f, trail)
                        * (1f - ForestLayout.Smooth(ForestLayout.FernShoulderOuter - 0.5f, ForestLayout.FernShoulderOuter + 0.5f, trail));
                    float run = ForestLayout.Smooth(0.52f, 0.70f, ForestTerrain.Noise01(point.x, point.y, ForestLayout.FernShoulderWavelength, noiseOffset + new Vector2(37.5f, 91.25f)));
                    density += ForestLayout.FernShoulderDensity * band * run;
                }
                // Review 2 of the M4 captures: drifts up the bank, which had no ground cover.
                if (outside > ForestLayout.CoreInset)
                    density += ForestLayout.BankFernDensity
                        * ForestLayout.Smooth(ForestLayout.BankFernFrom, ForestLayout.BankFernTo,
                            ForestTerrain.Noise01(point.x, point.y, ForestLayout.BankFernWavelength, noiseOffset + new Vector2(113.5f, 57.25f)))
                        * (1f - ForestLayout.Smooth(ForestLayout.FernPatchBankBand - 3f, ForestLayout.FernPatchBankBand, outside));
                density = Mathf.Min(density, ForestLayout.FernMaxDensity);

                float keep = onTrail;
                if (ForestLayout.DistanceToGameTrails(point) < 1.5f) keep *= 0.5f;
                if (toHollow < ForestLayout.HollowRadius)
                {
                    float ring = ForestLayout.Smooth(ForestLayout.FernHollowInner, ForestLayout.FernHollowInner + 1.5f, toHollow)
                        * (1f - ForestLayout.Smooth(ForestLayout.FernHollowFull, ForestLayout.HollowRadius, toHollow));
                    floor = ForestLayout.FernHollowDensity * ring * keep;
                }
                if (Vector2.Distance(point, ForestLayout.ClearingCentre) < ForestLayout.ClearingOpenRadius) keep *= 0.3f;
                return density * keep;
            }

            // The shade of the tall firs on a 1 m grid: per cell the sum of exp(-d^2 / 2 sigma^2) over the tall trunks within
            // FernShadeReach. Built once, the first time the fern field asks for it (every tall fir is planned by then).
            float[] shadeGrid;
            int shadeCells;
            float shadeExtent;

            float TallFirShade(Vector2 point)
            {
                if (shadeGrid == null)
                {
                    shadeExtent = ForestLayout.TileSize * 0.5f;
                    shadeCells = Mathf.CeilToInt(2f * shadeExtent);
                    shadeGrid = new float[shadeCells * shadeCells];
                    float reach = ForestLayout.FernShadeReach, twoSigma2 = 2f * ForestLayout.FernShadeSigma * ForestLayout.FernShadeSigma;
                    foreach (ForestPlacement placement in placements)
                    {
                        if (placement.cls != ForestClass.TallFir) continue;
                        float tx = placement.position.x, tz = placement.position.z;
                        int minX = Mathf.Max(0, Mathf.FloorToInt(tx - reach + shadeExtent)), maxX = Mathf.Min(shadeCells - 1, Mathf.CeilToInt(tx + reach + shadeExtent));
                        int minZ = Mathf.Max(0, Mathf.FloorToInt(tz - reach + shadeExtent)), maxZ = Mathf.Min(shadeCells - 1, Mathf.CeilToInt(tz + reach + shadeExtent));
                        for (int cz = minZ; cz <= maxZ; cz++)
                        {
                            float dz = cz + 0.5f - shadeExtent - tz;
                            for (int cx = minX; cx <= maxX; cx++)
                            {
                                float dx = cx + 0.5f - shadeExtent - tx;
                                float d2 = dx * dx + dz * dz;
                                if (d2 <= reach * reach) shadeGrid[cz * shadeCells + cx] += Mathf.Exp(-d2 / twoSigma2);
                            }
                        }
                    }
                }
                int ix = Mathf.Clamp(Mathf.FloorToInt(point.x + shadeExtent), 0, shadeCells - 1);
                int iz = Mathf.Clamp(Mathf.FloorToInt(point.y + shadeExtent), 0, shadeCells - 1);
                return shadeGrid[iz * shadeCells + ix];
            }

            /// <summary>
            /// What brings the expected number of patches down to the budget: a factor on the density field,
            /// not below FernThinningFloor, and then a raised drift threshold (<paramref name="narrowing"/>).
            /// Only if the smallest drifts are still too many is the density thinned further.
            /// </summary>
            float FernThinning(Vector2 noiseOffset, float extent, float budget, out float narrowing)
            {
                narrowing = 0f;
                List<Vector2> densities = FernDensities(noiseOffset, extent, 0f);
                if (ExpectedFerns(densities, 1f) <= budget) return 1f;

                float thinning = SolveFernThinning(densities, budget);
                if (thinning >= ForestLayout.FernThinningFloor) return thinning;

                thinning = ForestLayout.FernThinningFloor;
                if (ExpectedFerns(FernDensities(noiseOffset, extent, ForestLayout.FernMaxNarrowing), thinning) > budget)
                {
                    narrowing = ForestLayout.FernMaxNarrowing;
                    return SolveFernThinning(FernDensities(noiseOffset, extent, narrowing), budget);
                }

                float low = 0f, high = ForestLayout.FernMaxNarrowing;
                for (int i = 0; i < 12; i++)
                {
                    float middle = 0.5f * (low + high);
                    if (ExpectedFerns(FernDensities(noiseOffset, extent, middle), thinning) > budget) low = middle; else high = middle;
                }
                narrowing = high;
                return thinning;
            }

            /// <summary>
            /// The density field where a patch may stand, one sample per square metre: enough to predict the
            /// count. x is the part the cap thins, y the floor it does not.
            /// </summary>
            List<Vector2> FernDensities(Vector2 noiseOffset, float extent, float narrowing)
            {
                var densities = new List<Vector2>();
                for (float z = -extent + 0.5f; z < extent; z += 1f)
                {
                    for (float x = -extent + 0.5f; x < extent; x += 1f)
                    {
                        var point = new Vector2(x, z);
                        float density = FernDensity(point, noiseOffset, narrowing, out float floor);
                        if ((density >= ForestLayout.FernMinDensity || floor >= ForestLayout.FernMinDensity) && !CoverBlocked(point, 0.2f))
                            densities.Add(new Vector2(density, floor));
                    }
                }
                return densities;
            }

            static float SolveFernThinning(List<Vector2> densities, float budget)
            {
                float low = 0.001f, high = 1f;
                for (int i = 0; i < 24; i++)
                {
                    float middle = 0.5f * (low + high);
                    if (ExpectedFerns(densities, middle) > budget) high = middle; else low = middle;
                }
                return low;
            }

            static float ExpectedFerns(List<Vector2> densities, float thinning)
            {
                double expected = 0.0;
                foreach (Vector2 density in densities)
                {
                    float spacing = FernSpacing(Mathf.Max(density.x * thinning, density.y));
                    expected += PoissonPacking * FernJitterPacking / (spacing * spacing);
                }
                return (float)expected;
            }

            // ---- 7. The ring rule ----

            /// <summary>
            /// Every stump patch gets four to six single fern clumps around its mound and a dry branch
            /// against its edge; every log gets three to five clumps along its sides and a branch.
            /// Nothing scanned lies on bare ground.
            /// </summary>
            void RingStumpsAndLogs()
            {
                System.Random ferns = Stream(ForestClass.FernClump);
                System.Random branches = new System.Random(seed ^ ((int)ForestClass.Branch * 7919) ^ 0x51ED);
                int clumps = CountOf(ForestClass.FernClump);

                // The list grows while it is read, so the ringed pieces are collected first.
                var ringed = new List<ForestPlacement>();
                foreach (ForestPlacement placement in placements)
                    if (placement.cls == ForestClass.Stump || placement.cls == ForestClass.Log) ringed.Add(placement);

                foreach (ForestPlacement piece in ringed)
                {
                    var centre = new Vector2(piece.position.x, piece.position.z);
                    string landmark = piece.landmark;
                    if (piece.cls == ForestClass.Stump)
                    {
                        int count = ferns.Next(4, 7);
                        float turn = Range(ferns, 0f, 360f);
                        for (int i = 0; i < count; i++)
                        {
                            float bearing = turn + (i + Range(ferns, 0f, 0.7f)) * 360f / count;
                            Vector2 point = centre + ForestLayout.FromBearing(bearing) * (Range(ferns, 0.6f, 1.8f) * piece.scale);
                            clumps += PlaceClump(ferns, point, landmark, clumps);
                        }
                        Vector2 beside = centre + ForestLayout.FromBearing(Range(branches, 0f, 360f)) * (0.75f * piece.scale + 0.25f);
                        if (InCore(beside) && fields.Sample(fields.trailDistance, beside.x, beside.y) >= ForestLayout.ShoulderOuter)
                            PlaceBranch(branches, beside, Range(branches, 0f, 360f), landmark, ForestPrefabIds.Branches);
                    }
                    else
                    {
                        Vector3 axis3 = piece.rotation * Vector3.right;
                        Vector2 axis = new Vector2(axis3.x, axis3.z).normalized;
                        Vector2 across = ForestLayout.RightOf(axis);
                        int count = ferns.Next(3, 6);
                        // The deadfall's trunks keep their clumps at their ends, so the trunks show (review 1 of the M4 captures).
                        bool deadfall = landmark == ForestLayout.Deadfall;
                        for (int i = 0; i < count; i++)
                        {
                            float along = deadfall
                                ? (i < 2 ? 1f : -1f) * Range(ferns, 1.6f, 2.2f) * piece.scale
                                : Range(ferns, -1.7f, 1.7f) * piece.scale;
                            float side = i % 2 == 0 ? 1f : -1f;
                            Vector2 point = centre + axis * along + across * (side * Range(ferns, 0.45f, 0.95f) * piece.scale);
                            clumps += PlaceClump(ferns, point, landmark, clumps);
                        }
                        float branchSide = branches.Next(2) == 0 ? 1f : -1f;
                        Vector2 beside = centre + axis * (Range(branches, -1.2f, 1.2f) * piece.scale) + across * (branchSide * 0.65f * piece.scale);
                        if (InCore(beside) && fields.Sample(fields.trailDistance, beside.x, beside.y) >= ForestLayout.ShoulderOuter)
                            PlaceBranch(branches, beside, Range(branches, 0f, 360f), landmark, ForestPrefabIds.Branches);
                    }
                }
            }

            /// <summary>
            /// The ring rule carried over to the large rocks (review 2: boulders "set on the ground, not in it",
            /// nothing gathered at their base): two or three fern clumps against the contact line, on one side
            /// of every boulder-field and outcrop rock and of every rock of anchor size, at its measured rim.
            /// </summary>
            void RingRocks()
            {
                var random = new System.Random(seed ^ ((int)ForestClass.FernClump * 7919) ^ 0x2A17);
                int clumps = CountOf(ForestClass.FernClump);
                var rocks = new List<ForestPlacement>();
                foreach (ForestPlacement placement in placements)
                {
                    if (placement.cls != ForestClass.Boulder) continue;
                    if (placement.landmark == ForestLayout.BoulderField || placement.landmark == ForestLayout.NorthOutcrop
                        || placement.scale >= ForestLayout.BoulderAnchorMinScale)
                        rocks.Add(placement);
                }

                int added = 0;
                foreach (ForestPlacement rock in rocks)
                {
                    int count = random.Next(ForestLayout.RockRingClumpsMin, ForestLayout.RockRingClumpsMax + 1);
                    int first = random.Next(ForestLayout.RockLipBearings);
                    var pivot = new Vector2(rock.position.x, rock.position.z);
                    bool measured = ForestLayout.TryRockRim(rock, out Vector3[] rim);
                    for (int i = 0; i < count; i++)
                    {
                        // On one side of the rock, a bearing step or two apart, just outside its rim.
                        int k = (first + 2 * i + random.Next(2)) % ForestLayout.RockLipBearings;
                        float push = Range(random, 0.08f, 0.3f);
                        Vector2 offset = measured ? new Vector2(rim[k].x, rim[k].z) : ForestLayout.FromBearing(k * 22.5f) * rock.clearance * RockBodyShare;
                        if (offset.sqrMagnitude < 0.01f) continue;
                        Vector2 point = pivot + offset + offset.normalized * push;
                        if (InsideAnotherRock(point, pivot)) { random.Next(); random.NextDouble(); random.NextDouble(); continue; }
                        int placed = PlaceClump(random, point, rock.landmark, clumps);
                        clumps += placed; added += placed;
                    }
                }
                Debug.Log($"ForestScatter: {added} fern clumps against the foot of {rocks.Count} large rocks.");
            }

            /// <summary>True inside the measured rim of a rock other than the one standing at <paramref name="own"/>.</summary>
            bool InsideAnotherRock(Vector2 point, Vector2 own)
            {
                foreach (ForestPlacement other in placements)
                {
                    if (other.cls != ForestClass.Boulder) continue;
                    var pivot = new Vector2(other.position.x, other.position.z);
                    if (pivot == own) continue;
                    Vector2 offset = point - pivot;
                    if (offset.sqrMagnitude > 16f) continue;
                    float yaw = ForestLayout.YawOf(other.rotation);
                    if (offset.magnitude < 0.9f * RimToward(other.prefabId, other.scale, yaw, ForestLayout.Bearing(offset))) return true;
                }
                return false;
            }

            /// <summary>
            /// Single fern clumps where one ground layer otherwise ran for tens of metres with nothing over it
            /// (risk 13's fallback, review 2): the rim of the clearing between its swards, and the foot of the
            /// bank in runs, out to where the fir wall of M3 will stand.
            /// </summary>
            void ScatterLooseClumps()
            {
                var random = new System.Random(seed ^ ((int)ForestClass.FernClump * 7919) ^ 0x3C29);
                int clumps = CountOf(ForestClass.FernClump);
                Vector2 runOffset = ForestTerrain.NoiseOffset(seed, 44);
                var loose = new List<Vector2>();
                var grass = new List<Vector2>();
                foreach (ForestPlacement placement in placements)
                    if (placement.cls == ForestClass.GrassPatch) grass.Add(new Vector2(placement.position.x, placement.position.z));

                int clearing = 0;
                for (int attempt = 0; attempt < 3000 && clearing < ForestLayout.ClearingFernClumps; attempt++)
                {
                    float distance = Range(random, 3.5f, ForestLayout.ClearingOpenRadius - 0.3f);
                    Vector2 point = ForestLayout.ClearingCentre + ForestLayout.FromBearing(Range(random, 0f, 360f)) * distance;
                    if (Nearest(grass, point) < 0.9f || Nearest(loose, point) < 1.2f) continue;
                    if (ForestLayout.DistanceToGameTrails(point) < 1.5f || CoverBlocked(point, 0.2f)) continue;
                    int placed = PlaceLooseClump(random, point, "", clumps, true);
                    if (placed == 0) continue;
                    clumps++; clearing++; loose.Add(point);
                }

                int bank = 0;
                float extent = Mathf.Max(ForestLayout.HalfExtentX, ForestLayout.HalfExtentZ) + ForestLayout.BankClumpOuter;
                for (int attempt = 0; attempt < 20000 && bank < ForestLayout.BankFernClumps; attempt++)
                {
                    var point = new Vector2(Range(random, -extent, extent), Range(random, -extent, extent));
                    float outside = ForestLayout.SignedDistanceToBoundary(point);
                    if (outside < 1f || outside > ForestLayout.BankClumpOuter) continue;
                    // In runs along the bank, denser toward its foot.
                    float run = ForestTerrain.Noise01(point.x, point.y, 11f, runOffset) * (1f - 0.4f * outside / ForestLayout.BankClumpOuter);
                    if (run < 0.5f || fields.SlopeAt(point.x, point.y) > 28f) continue;
                    if (Nearest(loose, point) < ForestLayout.BankClumpSpacing || CoverBlocked(point, 0.2f)) continue;
                    int placed = PlaceLooseClump(random, point, "", clumps, false);
                    if (placed == 0) continue;
                    clumps++; bank++; loose.Add(point);
                }
                Debug.Log($"ForestScatter: {clearing} loose fern clumps on the clearing's rim, {bank} on the foot of the bank (fern clumps now {clumps}, cap {ForestLayout.FernClumpCap}).");
            }

            static float Nearest(List<Vector2> points, Vector2 point)
            {
                float nearest = float.MaxValue;
                foreach (Vector2 other in points) nearest = Mathf.Min(nearest, (other - point).sqrMagnitude);
                return Mathf.Sqrt(nearest);
            }

            int PlaceLooseClump(System.Random random, Vector2 point, string landmark, int soFar, bool core)
            {
                string prefabId = Pick(random, ForestPrefabIds.FernClumps);
                float scale = Range(random, ForestLayout.FernClumpMinScale, ForestLayout.FernClumpMaxScale);
                float yaw = Range(random, 0f, 360f);
                if (soFar >= ForestLayout.FernClumpCap || (core && !InCore(point)) || InFrontOfCrouchViewpoint(point)) return 0;
                if (fields.Sample(fields.trailDistance, point.x, point.y) < 1.2f) return 0;
                Place(prefabId, ForestClass.FernClump, point, yaw, scale, random, landmark, 0);
                return 1;
            }

            int PlaceClump(System.Random random, Vector2 point, string landmark, int soFar)
            {
                // The draws happen before the checks, so a skipped clump does not shift the ones after it.
                string prefabId = Pick(random, ForestPrefabIds.FernClumps);
                float scale = Range(random, ForestLayout.FernClumpMinScale, ForestLayout.FernClumpMaxScale);
                float yaw = Range(random, 0f, 360f);
                if (soFar >= ForestLayout.FernClumpCap || !InCore(point) || InFrontOfCrouchViewpoint(point)) return 0;
                if (fields.Sample(fields.trailDistance, point.x, point.y) < 1.2f) return 0;
                Place(prefabId, ForestClass.FernClump, point, yaw, scale, random, landmark, 0);
                return 1;
            }

            // ---- 8. Grass ----

            void ScatterGrass()
            {
                System.Random random = Stream(ForestClass.GrassPatch);
                Vector2 noiseOffset = ForestTerrain.NoiseOffset(seed, 42);

                // The clearing: a few swards on about a quarter of it, two of them around its stump patches,
                // tufts close enough inside a sward to touch and nothing between the swards.
                var stumps = new List<Vector2>();
                foreach (ForestPlacement placement in placements)
                    if (placement.cls == ForestClass.Stump && placement.landmark == ForestLayout.Clearing)
                        stumps.Add(new Vector2(placement.position.x, placement.position.z));

                float extent = Mathf.Max(Mathf.Abs(ForestLayout.ClearingCentre.x), Mathf.Abs(ForestLayout.ClearingCentre.y)) + ForestLayout.ClearingRadius;
                float largest = ForestLayout.GrassSpacingEdge * (1f + ForestLayout.GrassSpacingJitter);
                float threshold = ForestLayout.GrassSwardThreshold;
                List<Sample> swards = null;
                int passes = 0;
                // Too many patches for the cap: the swards shrink (a higher threshold) and keep their dense cores.
                // Each pass draws from the same stream, so the result depends only on the threshold it ends at.
                for (; passes < 30; passes++, threshold += ForestLayout.GrassSwardThresholdStep)
                {
                    float limit = threshold;
                    Func<Vector2, float> radiusAt = point =>
                    {
                        if (Vector2.Distance(point, ForestLayout.ClearingCentre) > ForestLayout.ClearingOpenRadius - 0.5f) return 0f;
                        float sward = GrassSward(point, noiseOffset, stumps);
                        if (sward < limit) return 0f;
                        if (ForestLayout.DistanceToGameTrails(point) < 0.7f || CoverBlocked(point, 0.2f) || InFrontOfCrouchViewpoint(point)) return 0f;
                        float core = ForestLayout.Smooth(limit, limit + ForestLayout.GrassSwardCoreSpan, sward);
                        float spacing = Mathf.Lerp(ForestLayout.GrassSpacingEdge, ForestLayout.GrassSpacingCore, core);
                        return spacing * (1f + ForestLayout.GrassSpacingJitter * (2f * ForestTerrain.Hash01(point.x, point.y, 14) - 1f));
                    };
                    swards = Poisson(new System.Random(seed ^ ((int)ForestClass.GrassPatch * 7919) ^ 0x6A55), extent, 2f, largest, radiusAt, new List<Sample>());
                    if (swards.Count <= ForestLayout.GrassClearingCap) break;
                }
                Debug.Log($"ForestScatter: {swards.Count} grass patches in the clearing's swards at threshold {threshold:0.000} after {passes + 1} pass(es), " +
                    $"spacing {ForestLayout.GrassSpacingCore} to {ForestLayout.GrassSpacingEdge} m (cap {ForestLayout.GrassClearingCap}).");
                // Still over the cap after the last pass: patches go by position hash, so the swards thin evenly.
                if (swards.Count > ForestLayout.GrassClearingCap)
                {
                    var ranked = new List<Sample>(swards);
                    ranked.Sort((a, b) => ForestTerrain.Hash01(a.point.x, a.point.y, 6).CompareTo(ForestTerrain.Hash01(b.point.x, b.point.y, 6)));
                    float cut = ForestTerrain.Hash01(ranked[ForestLayout.GrassClearingCap].point.x, ranked[ForestLayout.GrassClearingCap].point.y, 6);
                    swards.RemoveAll(sample => ForestTerrain.Hash01(sample.point.x, sample.point.y, 6) >= cut);
                }
                foreach (Sample sample in swards)
                    PlaceGrass(random, sample.point);

                // The trail shoulders: one patch every four metres, alternating sides.
                float length = ForestLayout.SplineLength(ForestLayout.Trail);
                int index = 0;
                for (float metres = 2f; metres < length - 1f; metres += ForestLayout.GrassShoulderSpacing, index++)
                {
                    Vector2 centre = ForestLayout.PointAtDistance(ForestLayout.Trail, metres + Range(random, -1f, 1f), out Vector2 tangent);
                    float side = index % 2 == 0 ? 1f : -1f;
                    Vector2 point = centre + ForestLayout.RightOf(tangent) * (side * Range(random, 1.15f, 1.85f));
                    if (CoverBlocked(point, 0.2f)) continue;
                    PlaceGrass(random, point);
                }
            }

            /// <summary>The sward field of the clearing: noise, raised around its stump patches.</summary>
            static float GrassSward(Vector2 point, Vector2 noiseOffset, List<Vector2> stumps)
            {
                float nearestStump = float.MaxValue;
                foreach (Vector2 stump in stumps) nearestStump = Mathf.Min(nearestStump, Vector2.Distance(stump, point));
                return ForestTerrain.Noise01(point.x, point.y, ForestLayout.GrassNoiseWavelength, noiseOffset)
                    + ForestLayout.GrassSwardStumpBonus * (1f - ForestLayout.Smooth(1.2f, ForestLayout.GrassSwardStumpReach, nearestStump));
            }

            void PlaceGrass(System.Random random, Vector2 point)
            {
                string prefabId = Pick(random, ForestPrefabIds.GrassPatches);
                Place(prefabId, ForestClass.GrassPatch, point, Range(random, 0f, 360f), Range(random, ForestLayout.GrassMinScale, ForestLayout.GrassMaxScale), random, "", 0);
            }

            // ---- Placing one instance ----

            /// <summary>
            /// Works out the rotation and sink a class calls for, records the placement and marks the
            /// ground it occupies. The random stream is the caller's: every class has its own.
            /// </summary>
            void Place(string prefabId, ForestClass cls, Vector2 point, float yaw, float scale, System.Random random, string landmark, int group)
            {
                PrefabInfo info = Info(prefabId);
                float clearance = ForestKitSpec.ClearanceRadius(prefabId) * scale;
                float slope = fields.SlopeAt(point.x, point.y);
                Quaternion rotation;
                float sink;
                int tint = 0;

                switch (cls)
                {
                    case ForestClass.TallFir:
                    {
                        // Old trees stand almost, not exactly, upright.
                        Vector2 leanToward = ForestLayout.FromBearing(Range(random, 0f, 360f));
                        float lean = Range(random, 0f, ForestLayout.TallFirMaxLean);
                        rotation = AxisAngle(new Vector3(leanToward.y, 0f, -leanToward.x), lean) * YawRotation(yaw);
                        sink = TreeSink(clearance, slope);
                        AddBody(point, TallFirBody, TallFirBody, cls, info.variant, yaw, group);
                        break;
                    }
                    case ForestClass.YoungFir:
                    {
                        // Review 1 of the M3 captures: young firs stood bolt upright, like planted stock. Only scattered firs
                        // lean: a hand-placed one would take numbers from the set pieces' stream and move the boulder field.
                        rotation = YawRotation(yaw);
                        if (string.IsNullOrEmpty(landmark))
                        {
                            Vector2 leanToward = ForestLayout.FromBearing(Range(random, 0f, 360f));
                            float lean = Range(random, 0f, ForestLayout.YoungFirMaxLean);
                            rotation = AxisAngle(new Vector3(leanToward.y, 0f, -leanToward.x), lean) * rotation;
                        }
                        sink = TreeSink(clearance, slope);
                        tint = ForestTerrain.Hash01(point.x, point.y, 7) < 0.5f ? 1 : 0;
                        AddBody(point, YoungFirBody, YoungFirBody, cls, info.variant, yaw, group);
                        break;
                    }
                    case ForestClass.Boulder:
                    {
                        RequireMeasured(info, prefabId);
                        Vector3 footprintNormal = FootprintNormal(point, clearance * 0.7f);
                        rotation = Aligned(footprintNormal, ForestLayout.BoulderMaxTilt, yaw);
                        // The lowest point of the scan ends 8 cm under the ground at the pivot, plus the drop of
                        // the ground across the footprint that the tilt did not take up.
                        float residual = Mathf.Max(0f, slope - ForestLayout.RockTiltTakenUp);
                        sink = Mathf.Max(0f, ForestLayout.RockSink + scale * (info.minY + info.footprint * Mathf.Tan(residual * Mathf.Deg2Rad)));
                        // Convex ground: where it curves away under the rim, the rim goes down to meet it.
                        float rimDrop = RimDrop(point, clearance * ForestLayout.RockRimShare, footprintNormal, ForestLayout.BoulderMaxTilt);
                        if (rimDrop > 0f)
                            sink = Mathf.Max(sink, Mathf.Min(rimDrop + ForestLayout.RockRimBury, ForestLayout.RockRimMaxSink * scale));
                        AddBody(point, clearance * RockBodyShare, clearance, cls, info.variant, yaw, group);
                        break;
                    }
                    case ForestClass.Log:
                    {
                        rotation = Aligned(FootprintNormal(point, 1.5f * scale), 20f, yaw);
                        // The trunk rests on a branch stub; this beds its whole underside.
                        sink = Range(random, ForestLayout.LogMinSink, ForestLayout.LogMaxSink) * scale;
                        Vector2 axis = LogAxis(yaw);
                        for (int disc = -1; disc <= 1; disc++)
                            AddBody(point + axis * (disc * LogDiscOffset * scale), LogDiscRadius * scale, 0.5f * scale, cls, disc == 0 ? info.variant : -1, yaw, group);
                        break;
                    }
                    case ForestClass.Stump:
                        rotation = Aligned(FootprintNormal(point, 0.6f * scale), 25f, yaw);
                        // The rim of the scanned ground goes under; the stony-soil paint hides the seam.
                        sink = Range(random, ForestLayout.StumpMinSink, ForestLayout.StumpMaxSink) * scale;
                        AddBody(point, clearance * StumpBodyShare, 0.75f * scale, cls, info.variant, yaw, group);
                        break;
                    case ForestClass.Branch:
                        RequireMeasured(info, prefabId);
                        rotation = Aligned(FootprintNormal(point, 0.5f), 30f, yaw);
                        // A branch pivots at its middle and rests on its lowest twigs: raised until they
                        // are five centimetres in the litter (a negative sink), then settled by a share of the
                        // depth its scan reaches below the pivot, so an arched branch lies in the litter.
                        sink = ForestLayout.BranchSink + scale * info.minY * (1f - ForestLayout.BranchSettle);
                        AddBody(point, 0.35f, 0.35f, cls, info.variant, yaw, group);
                        break;
                    case ForestClass.FernPatch:
                        rotation = Aligned(FootprintNormal(point, 0.8f), 25f, yaw);
                        sink = ForestLayout.GroundCoverSink;
                        tint = ForestTerrain.Hash01(point.x, point.y, 9) < 0.5f ? 1 : 0;
                        AddBody(point, 0.45f, 0.45f, cls, info.variant, yaw, group);
                        break;
                    case ForestClass.FernClump:
                    case ForestClass.GrassPatch:
                        rotation = Aligned(FootprintNormal(point, 0.4f), 25f, yaw);
                        sink = ForestLayout.GroundCoverSink;
                        AddBody(point, 0.3f, 0.3f, cls, info.variant, yaw, group);
                        break;
                    default:
                        throw new InvalidOperationException($"Forest plan: nothing is placed with class {cls}.");
                }

                placements.Add(new ForestPlacement
                {
                    prefabId = prefabId,
                    cls = cls,
                    position = new Vector3(point.x, 0f, point.y),
                    rotation = rotation,
                    scale = scale,
                    sink = sink,
                    clearance = clearance,
                    tint = tint,
                    cellX = ForestLayout.CellIndex(point.x),
                    cellZ = ForestLayout.CellIndex(point.y),
                    landmark = landmark ?? "",
                });
            }

            static float TreeSink(float clearance, float slopeDegrees)
            {
                // Deep enough that the downhill side of the trunk's base is still in the ground.
                return ForestLayout.TreeSink + clearance * Mathf.Tan(slopeDegrees * Mathf.Deg2Rad);
            }

            static void RequireMeasured(PrefabInfo info, string prefabId)
            {
                if (!info.measured)
                    throw new InvalidOperationException($"Forest plan: the asset manifest has no object for '{prefabId}', so it cannot be grounded.");
            }

            /// <summary>The ground normal over a footprint, not at a point: a rock sits on the slope under all of it.</summary>
            Vector3 FootprintNormal(Vector2 point, float radius)
            {
                float reach = Mathf.Max(radius, fields.SampleSpacing);
                float gradientX = (fields.HeightAt(point.x + reach, point.y) - fields.HeightAt(point.x - reach, point.y)) / (2f * reach);
                float gradientZ = (fields.HeightAt(point.x, point.y + reach) - fields.HeightAt(point.x, point.y - reach)) / (2f * reach);
                return new Vector3(-gradientX, 1f, -gradientZ).normalized;
            }

            /// <summary>
            /// How far the ground under a rock's rim lies below the plane the rock is tilted to: the second
            /// largest of eight readings around a ring, so one notch in the ground does not decide it.
            /// </summary>
            float RimDrop(Vector2 point, float radius, Vector3 normal, float maxTilt)
            {
                // The plane the rock actually takes: the ground normal, but tilted by no more than maxTilt.
                float tilt = Mathf.Acos(Mathf.Clamp(normal.y, -1f, 1f)) * Mathf.Rad2Deg;
                float horizontal = Mathf.Sqrt(normal.x * normal.x + normal.z * normal.z);
                float gradientX = 0f, gradientZ = 0f;
                if (horizontal > 1e-6f && tilt > 0.01f)
                {
                    float steepness = Mathf.Tan(Mathf.Min(tilt, maxTilt) * Mathf.Deg2Rad);
                    // The plane falls in the direction the normal leans.
                    gradientX = -normal.x / horizontal * steepness;
                    gradientZ = -normal.z / horizontal * steepness;
                }

                float centre = fields.HeightAt(point.x, point.y);
                float largest = 0f, second = 0f;
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * (Mathf.PI / 4f);
                    float dx = Mathf.Cos(angle) * radius, dz = Mathf.Sin(angle) * radius;
                    float drop = centre + gradientX * dx + gradientZ * dz - fields.HeightAt(point.x + dx, point.y + dz);
                    if (drop > largest) { second = largest; largest = drop; }
                    else if (drop > second) second = drop;
                }
                return second;
            }

            // Rotations are built from sines and cosines here, not with Quaternion.Euler or AngleAxis,
            // so the planner stays plain arithmetic that runs anywhere.
            static Quaternion YawRotation(float degrees) => AxisAngle(Vector3.up, degrees);

            static Quaternion AxisAngle(Vector3 axis, float degrees)
            {
                float half = degrees * Mathf.Deg2Rad * 0.5f;
                float sine = Mathf.Sin(half);
                return new Quaternion(axis.x * sine, axis.y * sine, axis.z * sine, Mathf.Cos(half));
            }

            /// <summary>Yaw first, then a tilt of the up axis toward the ground normal, by at most <paramref name="maxTilt"/> degrees.</summary>
            static Quaternion Aligned(Vector3 normal, float maxTilt, float yaw)
            {
                float tilt = Mathf.Acos(Mathf.Clamp(normal.y, -1f, 1f)) * Mathf.Rad2Deg;
                var axis = new Vector3(normal.z, 0f, -normal.x);   // up cross normal
                if (tilt < 0.01f || axis.sqrMagnitude < 1e-12f) return YawRotation(yaw);
                return AxisAngle(axis.normalized, Mathf.Min(tilt, maxTilt)) * YawRotation(yaw);
            }

            // ---- Rules ----

            /// <summary>
            /// Section 7.1, for anything with a collider: the corridor and the spawn stay free, measured
            /// to the collider's surface, and nothing stands in the gully bed or the middle of the hollow.
            /// </summary>
            bool SolidAllowed(Vector2 point, float clearance)
            {
                float needed = ForestLayout.CorridorClearance + clearance + RuleMargin;
                // The field is only an estimate near the line; it is good enough to reject the obvious.
                if (fields.Sample(fields.trailDistance, point.x, point.y) < needed - 0.3f) return false;
                if (Vector2.Distance(point, ForestLayout.SpawnXZ) < ForestLayout.SpawnClearance + clearance + RuleMargin) return false;
                if (fields.Sample(fields.gullyDistance, point.x, point.y) < ForestLayout.GullyHalfWidth) return false;
                if (Vector2.Distance(point, ForestLayout.HollowCentre) < ForestLayout.HollowKeepOut) return false;
                return ForestLayout.DistanceToAnyTrail(point) >= needed;
            }

            /// <summary>
            /// The rule for scattered solids: <see cref="SolidAllowed"/>, and not where a capture camera
            /// stands. The set pieces are composed around their viewpoints by hand and skip the second part.
            /// </summary>
            bool ScatterAllowed(Vector2 point, float clearance)
            {
                if (!SolidAllowed(point, clearance)) return false;
                float keepOut = clearance * RockBodyShare + ViewpointGap;
                foreach (ForestLayout.Viewpoint view in ForestLayout.Viewpoints)
                {
                    if (!view.heightIsAboveGround || view.position.y > 3f) continue;
                    float dx = view.position.x - point.x, dz = view.position.z - point.y;
                    if (dx * dx + dz * dz < keepOut * keepOut) return false;
                }
                return true;
            }

            /// <summary>True within <paramref name="gap"/> metres of a first-person capture camera (on the ground, up to 3 m high).</summary>
            static bool NearViewpoint(Vector2 point, float gap)
            {
                foreach (ForestLayout.Viewpoint view in ForestLayout.Viewpoints)
                {
                    if (!view.heightIsAboveGround || view.position.y > 3f) continue;
                    float dx = view.position.x - point.x, dz = view.position.z - point.y;
                    if (dx * dx + dz * dz < gap * gap) return true;
                }
                return false;
            }

            /// <summary>True within <paramref name="gap"/> metres of a capture camera that lies on the ground.</summary>
            static bool NearGroundViewpoint(Vector2 point, float gap)
            {
                foreach (ForestLayout.Viewpoint view in ForestLayout.Viewpoints)
                {
                    if (!view.heightIsAboveGround || view.position.y > GroundViewHeight) continue;
                    float dx = view.position.x - point.x, dz = view.position.z - point.y;
                    if (dx * dx + dz * dz < gap * gap) return true;
                }
                return false;
            }

            /// <summary>
            /// True just in front of a crouched capture camera (rock_close, crouch_hide, hollow_rim): a fern a metre
            /// from the lens fills the frame the view exists for (review 2: a frond across rock_close).
            /// </summary>
            static bool InFrontOfCrouchViewpoint(Vector2 point)
            {
                foreach (ForestLayout.Viewpoint view in ForestLayout.Viewpoints)
                {
                    if (!view.heightIsAboveGround || view.position.y <= GroundViewHeight || view.position.y > CrouchViewHeight) continue;
                    var offset = new Vector2(point.x - view.position.x, point.y - view.position.z);
                    float distance = offset.magnitude;
                    if (distance >= CrouchViewFernGap) continue;
                    if (distance < 0.8f || Mathf.Abs(Mathf.DeltaAngle(view.euler.y, ForestLayout.Bearing(offset))) < 70f) return true;
                }
                return false;
            }

            static bool InCore(Vector2 point) => ForestLayout.SignedDistanceToBoundary(point) <= -ForestLayout.CoreInset;

            static bool IsSolid(ForestClass cls)
            {
                return cls == ForestClass.TallFir || cls == ForestClass.YoungFir || cls == ForestClass.Boulder
                    || cls == ForestClass.Log || cls == ForestClass.Stump;
            }

            /// <summary>True when a solid of this radius would stand in another solid. Members of its own group are not counted.</summary>
            bool SolidBlocked(Vector2 point, float radius, int group)
            {
                float reach = radius + largestBody + SolidGap;
                CellRange(point, reach, out int minX, out int maxX, out int minZ, out int maxZ);
                for (int cz = minZ; cz <= maxZ; cz++)
                {
                    for (int cx = minX; cx <= maxX; cx++)
                    {
                        List<int> cell = occupancy[cz * occupancyCells + cx];
                        if (cell == null) continue;
                        foreach (int index in cell)
                        {
                            Body body = bodies[index];
                            if (!IsSolid(body.cls) || (group != 0 && body.group == group)) continue;
                            float required = radius + body.radius + SolidGap;
                            float dx = body.x - point.x, dz = body.z - point.y;
                            if (dx * dx + dz * dz < required * required) return true;
                        }
                    }
                }
                return false;
            }

            /// <summary>True when ground cover here would be hidden inside a rock, a log, a stump mound or a trunk.</summary>
            bool CoverBlocked(Vector2 point, float margin)
            {
                float reach = largestBody + margin;
                CellRange(point, reach, out int minX, out int maxX, out int minZ, out int maxZ);
                for (int cz = minZ; cz <= maxZ; cz++)
                {
                    for (int cx = minX; cx <= maxX; cx++)
                    {
                        List<int> cell = occupancy[cz * occupancyCells + cx];
                        if (cell == null) continue;
                        foreach (int index in cell)
                        {
                            Body body = bodies[index];
                            if (!IsSolid(body.cls)) continue;
                            float inside = (body.cls == ForestClass.TallFir || body.cls == ForestClass.YoungFir ? 0.4f : body.edge * 0.8f) + margin;
                            float dx = body.x - point.x, dz = body.z - point.y;
                            if (dx * dx + dz * dz < inside * inside) return true;
                        }
                    }
                }
                return false;
            }

            /// <summary>
            /// How much of a fern collar a point gets from the rocks, stump patches and logs within
            /// <paramref name="distance"/> metres of their visible edge: 1 on the collar side of a piece that
            /// has one, otherwise as much as a fern drift reaches the piece (<paramref name="drift"/>, 0 to 1).
            /// Which pieces have a collar, and on which side, comes from a hash of where the piece stands.
            /// </summary>
            float FernCollar(Vector2 point, float distance, float drift)
            {
                float reach = largestBody / RockBodyShare + distance;
                CellRange(point, reach, out int minX, out int maxX, out int minZ, out int maxZ);
                float collar = 0f;
                for (int cz = minZ; cz <= maxZ; cz++)
                {
                    for (int cx = minX; cx <= maxX; cx++)
                    {
                        List<int> cell = occupancy[cz * occupancyCells + cx];
                        if (cell == null) continue;
                        foreach (int index in cell)
                        {
                            Body body = bodies[index];
                            if (body.cls != ForestClass.Boulder && body.cls != ForestClass.Stump && body.cls != ForestClass.Log) continue;
                            float within = body.edge + distance;
                            float dx = point.x - body.x, dz = point.y - body.z;
                            if (dx * dx + dz * dz >= within * within) continue;

                            collar = Mathf.Max(collar, drift);
                            if (ForestTerrain.Hash01(body.x, body.z, 17) >= ForestLayout.FernCollarShare) continue;
                            float side = ForestTerrain.Hash01(body.x, body.z, 19) * 360f;
                            float off = Mathf.Abs(Mathf.DeltaAngle(side, ForestLayout.Bearing(new Vector2(dx, dz))));
                            if (off <= 0.5f * ForestLayout.FernCollarArc) return 1f;
                        }
                    }
                }
                return collar;
            }

            /// <summary>Distance to the nearest planned pivot of a class. Slow, and only used for the few dozen pieces that need it.</summary>
            float NearestOfClass(Vector2 point, ForestClass cls)
            {
                float nearest = float.MaxValue;
                foreach (ForestPlacement placement in placements)
                {
                    if (placement.cls != cls) continue;
                    float dx = placement.position.x - point.x, dz = placement.position.z - point.y;
                    nearest = Mathf.Min(nearest, dx * dx + dz * dz);
                }
                return nearest == float.MaxValue ? float.MaxValue : Mathf.Sqrt(nearest);
            }

            /// <summary>
            /// Section 2: no two neighbours of the same variant within 8 m share a yaw within 40 degrees.
            /// In a thicket that cannot always be met; then the yaw furthest from its neighbours' is taken.
            /// </summary>
            float PickYaw(System.Random random, Vector2 point, int variant)
            {
                float best = 0f, bestGap = -1f;
                CellRange(point, ForestLayout.SameVariantRadius, out int minX, out int maxX, out int minZ, out int maxZ);
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    float yaw = Range(random, 0f, 360f);
                    float gap = 180f;
                    for (int cz = minZ; cz <= maxZ; cz++)
                    {
                        for (int cx = minX; cx <= maxX; cx++)
                        {
                            List<int> cell = occupancy[cz * occupancyCells + cx];
                            if (cell == null) continue;
                            foreach (int index in cell)
                            {
                                Body body = bodies[index];
                                if (body.variant != variant) continue;
                                float dx = body.x - point.x, dz = body.z - point.y;
                                if (dx * dx + dz * dz > ForestLayout.SameVariantRadius * ForestLayout.SameVariantRadius) continue;
                                gap = Mathf.Min(gap, Mathf.Abs(Mathf.DeltaAngle(yaw, body.yaw)));
                            }
                        }
                    }
                    if (gap >= ForestLayout.SameVariantYawGap) return yaw;
                    if (gap > bestGap) { bestGap = gap; best = yaw; }
                }
                return best;
            }

            // ---- Occupancy ----

            void AddBody(Vector2 point, float radius, float edge, ForestClass cls, int variant, float yaw, int group)
            {
                bodies.Add(new Body { x = point.x, z = point.y, radius = radius, edge = edge, cls = cls, variant = variant, yaw = yaw, group = group });
                int cell = OccupancyIndex(point.y) * occupancyCells + OccupancyIndex(point.x);
                (occupancy[cell] ?? (occupancy[cell] = new List<int>())).Add(bodies.Count - 1);
                largestBody = Mathf.Max(largestBody, Mathf.Max(radius, edge));
            }

            int OccupancyIndex(float worldCoordinate)
            {
                return Mathf.Clamp(Mathf.FloorToInt((worldCoordinate + ForestLayout.TileSize * 0.5f) / OccupancyCell), 0, occupancyCells - 1);
            }

            void CellRange(Vector2 point, float reach, out int minX, out int maxX, out int minZ, out int maxZ)
            {
                minX = OccupancyIndex(point.x - reach); maxX = OccupancyIndex(point.x + reach);
                minZ = OccupancyIndex(point.y - reach); maxZ = OccupancyIndex(point.y + reach);
            }

            /// <summary>What is already planned of a class, as fixed points for that class's own spacing.</summary>
            List<Sample> Existing(ForestClass cls, float radius)
            {
                var existing = new List<Sample>();
                foreach (ForestPlacement placement in placements)
                    if (placement.cls == cls)
                        existing.Add(new Sample { point = new Vector2(placement.position.x, placement.position.z), radius = radius });
                return existing;
            }

            int CountOf(ForestClass cls)
            {
                int count = 0;
                foreach (ForestPlacement placement in placements)
                    if (placement.cls == cls) count++;
                return count;
            }

            // ---- Poisson-disc sampling ----

            /// <summary>
            /// Bridson's algorithm with a radius that varies over the ground. <paramref name="radiusAt"/>
            /// returns the spacing wanted at a point, or zero where nothing may stand; two points keep at
            /// least the mean of their radii apart. The front cannot cross ground where nothing may stand,
            /// so it is restarted from a shuffled grid of seed points until every island has been tried.
            /// </summary>
            List<Sample> Poisson(System.Random random, float extent, float seedStep, float largestRadius, Func<Vector2, float> radiusAt, List<Sample> existing)
            {
                float cellSize = Mathf.Max(largestRadius * 0.5f, 0.5f);
                int cells = Mathf.CeilToInt(2f * extent / cellSize) + 1;
                var grid = new List<int>[cells * cells];
                var samples = new List<Sample>(existing);
                for (int i = 0; i < samples.Count; i++)
                    AddToGrid(grid, cells, cellSize, extent, samples[i].point, i);
                int firstNew = samples.Count;

                var seeds = new List<Vector2>();
                for (float z = -extent; z < extent; z += seedStep)
                    for (float x = -extent; x < extent; x += seedStep)
                        seeds.Add(new Vector2(x + Range(random, 0f, seedStep), z + Range(random, 0f, seedStep)));
                for (int i = seeds.Count - 1; i > 0; i--)
                {
                    int swap = random.Next(i + 1);
                    Vector2 held = seeds[i]; seeds[i] = seeds[swap]; seeds[swap] = held;
                }

                var active = new List<int>();
                foreach (Vector2 seedPoint in seeds)
                {
                    if (!TryAddSample(grid, cells, cellSize, extent, largestRadius, samples, seedPoint, radiusAt)) continue;
                    active.Add(samples.Count - 1);
                    while (active.Count > 0)
                    {
                        int pick = random.Next(active.Count);
                        Sample from = samples[active[pick]];
                        bool grew = false;
                        for (int attempt = 0; attempt < PoissonAttempts; attempt++)
                        {
                            float angle = Range(random, 0f, 2f * Mathf.PI);
                            float distance = from.radius * Range(random, 1f, 2f);
                            var candidate = new Vector2(from.point.x + Mathf.Cos(angle) * distance, from.point.y + Mathf.Sin(angle) * distance);
                            if (!TryAddSample(grid, cells, cellSize, extent, largestRadius, samples, candidate, radiusAt)) continue;
                            active.Add(samples.Count - 1);
                            grew = true;
                            break;
                        }
                        if (grew) continue;
                        active[pick] = active[active.Count - 1];
                        active.RemoveAt(active.Count - 1);
                    }
                }

                samples.RemoveRange(0, firstNew);
                return samples;
            }

            static bool TryAddSample(List<int>[] grid, int cells, float cellSize, float extent, float largestRadius, List<Sample> samples,
                Vector2 point, Func<Vector2, float> radiusAt)
            {
                if (Mathf.Abs(point.x) >= extent || Mathf.Abs(point.y) >= extent) return false;
                float radius = radiusAt(point);
                if (radius <= 0f) return false;

                float reach = 0.5f * (radius + largestRadius);
                int minX = GridIndex(point.x - reach, cells, cellSize, extent), maxX = GridIndex(point.x + reach, cells, cellSize, extent);
                int minZ = GridIndex(point.y - reach, cells, cellSize, extent), maxZ = GridIndex(point.y + reach, cells, cellSize, extent);
                for (int cz = minZ; cz <= maxZ; cz++)
                {
                    for (int cx = minX; cx <= maxX; cx++)
                    {
                        List<int> cell = grid[cz * cells + cx];
                        if (cell == null) continue;
                        foreach (int index in cell)
                        {
                            Sample other = samples[index];
                            float apart = 0.5f * (radius + other.radius);
                            float dx = other.point.x - point.x, dz = other.point.y - point.y;
                            if (dx * dx + dz * dz < apart * apart) return false;
                        }
                    }
                }

                samples.Add(new Sample { point = point, radius = radius });
                AddToGrid(grid, cells, cellSize, extent, point, samples.Count - 1);
                return true;
            }

            static void AddToGrid(List<int>[] grid, int cells, float cellSize, float extent, Vector2 point, int index)
            {
                int cell = GridIndex(point.y, cells, cellSize, extent) * cells + GridIndex(point.x, cells, cellSize, extent);
                (grid[cell] ?? (grid[cell] = new List<int>())).Add(index);
            }

            static int GridIndex(float worldCoordinate, int cells, float cellSize, float extent)
            {
                return Mathf.Clamp(Mathf.FloorToInt((worldCoordinate + extent) / cellSize), 0, cells - 1);
            }

            // ---- Small helpers ----

            /// <summary>One random stream per class, so tuning one class does not reshuffle the others.</summary>
            System.Random Stream(ForestClass cls) => new System.Random(seed ^ ((int)cls * 7919));
            const int SpawnExtrasSalt = 0x51A7;

            static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

            static string Pick(System.Random random, string[] ids) => ids[random.Next(ids.Length)];

            static string PickWeighted(System.Random random, string[] ids, float[] weights)
            {
                float total = 0f;
                foreach (float weight in weights) total += weight;
                float roll = (float)random.NextDouble() * total;
                for (int i = 0; i < ids.Length; i++)
                {
                    roll -= weights[i];
                    if (roll < 0f) return ids[i];
                }
                return ids[ids.Length - 1];
            }

            Vector2 RandomCorePoint(System.Random random)
            {
                return new Vector2(Range(random, -ForestLayout.HalfExtentX, ForestLayout.HalfExtentX), Range(random, -ForestLayout.HalfExtentZ, ForestLayout.HalfExtentZ));
            }

            static string[] IdsOf(ForestClass cls)
            {
                switch (cls)
                {
                    case ForestClass.TallFir: return ForestPrefabIds.TallFirs;
                    case ForestClass.YoungFir: return ForestPrefabIds.YoungFirs;
                    case ForestClass.Boulder: return ForestPrefabIds.Boulders;
                    case ForestClass.Log: return ForestPrefabIds.Logs;
                    case ForestClass.Stump: return ForestPrefabIds.Stumps;
                    case ForestClass.Branch: return ForestPrefabIds.Branches;
                    case ForestClass.FernPatch: return ForestPrefabIds.FernPatches;
                    case ForestClass.FernClump: return ForestPrefabIds.FernClumps;
                    case ForestClass.GrassPatch: return ForestPrefabIds.GrassPatches;
                    default: throw new InvalidOperationException($"Forest plan: class {cls} has no prefabs.");
                }
            }

            float LargestClearance(string[] ids, float scale)
            {
                float largest = 0f;
                foreach (string id in ids) largest = Mathf.Max(largest, Info(id).clearance * scale);
                return largest;
            }

            /// <summary>What the kit specification and the manifest say about a prefab. Asked once per prefab.</summary>
            PrefabInfo Info(string prefabId)
            {
                if (prefabs.TryGetValue(prefabId, out PrefabInfo info)) return info;

                info = new PrefabInfo { variant = prefabs.Count, clearance = ForestKitSpec.ClearanceRadius(prefabId) };
                // Patch prefabs are assembled from several objects and have no entry of their own.
                ForestObjectEntry entry = ForestManifest.FindObjectForPrefab(prefabId);
                if (entry != null)
                {
                    info.measured = true;
                    info.minY = ForestManifest.SourceBounds(entry).min.y;
                    info.footprint = ForestManifest.FootprintRadius(entry);
                }
                prefabs.Add(prefabId, info);
                return info;
            }
        }
    }
}
