using System;
using System.Collections.Generic;
using Gamex.EditorTools;
using NUnit.Framework;
using UnityEngine;

namespace Gamex.Tests.Forest
{
    /// <summary>
    /// Tests of the layout, the terrain shape and the scatter plan, all of which are plain array maths
    /// that needs no asset and no scene. They build the world once (fields, plan, root mounds, grounding,
    /// in the order the scene builder uses) and look at it from several sides.
    /// </summary>
    [Category("Forest")]
    public class ForestWorldTests
    {
        /// <summary>The world as the scene builder computes it, with what had to be measured before each later step changed it.</summary>
        sealed class World
        {
            public ForestFields fields;                 // final: root mounds included
            public ForestPlan plan;                     // grounded
            public string firstDifference;              // between two plans of the same seed; null when identical
            public bool groundedAfterPlan;
            public float largestHeightAfterPlan;        // Plan must leave every height at 0
            public float[] groundBeforeMounds;          // per placement
            public float coreSlopeBefore, trailSlopeBefore, coreSlopeAfter, trailSlopeAfter;
        }

        static World cached;

        static World GetWorld()
        {
            if (cached != null)
                return cached;

            var world = new World();
            world.fields = ForestTerrain.ComputeFields(ForestLayout.Seed);
            world.coreSlopeBefore = SteepestCoreSlope(world.fields);
            world.trailSlopeBefore = SteepestTrailSlope(world.fields);

            world.plan = ForestScatter.Plan(world.fields, ForestLayout.Seed);
            ForestPlan again = ForestScatter.Plan(world.fields, ForestLayout.Seed);
            world.firstDifference = FirstDifference(world.plan, again);
            world.groundedAfterPlan = world.plan.grounded;
            world.groundBeforeMounds = new float[world.plan.placements.Length];
            for (int i = 0; i < world.plan.placements.Length; i++)
            {
                Vector3 position = world.plan.placements[i].position;
                world.largestHeightAfterPlan = Mathf.Max(world.largestHeightAfterPlan, Mathf.Abs(position.y));
                world.groundBeforeMounds[i] = world.fields.HeightAt(position.x, position.z);
            }

            ForestTerrain.AddRootMounds(world.fields, world.plan);
            world.coreSlopeAfter = SteepestCoreSlope(world.fields);
            world.trailSlopeAfter = SteepestTrailSlope(world.fields);
            ForestScatter.Ground(world.plan, world.fields);

            cached = world;
            return world;
        }

        // ---- Layout ----

        [Test]
        public void Layout_SplinesAndLandmarksAreInsideTheBoundary()
        {
            var problems = new List<string>();

            // A trail and its corridor (2.5 m to every collider) lie inside the boundary; the gully's bed is 1.75 m to each side.
            CheckSpline("Trail", ForestLayout.Trail, ForestLayout.CorridorClearance, problems);
            CheckSpline("WestTrail", ForestLayout.WestTrail, ForestLayout.CorridorClearance, problems);
            CheckSpline("EastTrail", ForestLayout.EastTrail, ForestLayout.CorridorClearance, problems);
            CheckSpline("Gully", ForestLayout.Gully, 1.75f, problems);

            float trail = ForestLayout.SplineLength(ForestLayout.Trail);
            if (trail < 130f || trail > 170f)
                problems.Add($"the trail is {trail:0.0} m long, expected about 150 m");

            // Nothing solid may stand within 5 m of the spawn, so the walls must be further away than that.
            float spawnInside = -ForestLayout.SignedDistanceToBoundary(ForestLayout.SpawnXZ);
            if (spawnInside < ForestLayout.SpawnClearance)
                problems.Add($"the spawn is only {spawnInside:0.00} m inside the boundary");
            float spawnOffTrail = ForestLayout.DistanceToSpline(ForestLayout.Trail, ForestLayout.SpawnXZ);
            if (spawnOffTrail > 0.8f)
                problems.Add($"the spawn is {spawnOffTrail:0.00} m from the trail centre line, off the 1.6 m tread");
            if (Vector2.Distance(ForestLayout.TrailPoint(0f, 0f), ForestLayout.SpawnXZ) > 0.8f + 0.01f)
                problems.Add("TrailPoint(0, 0) is not the point of the trail beside the spawn");
            // Trail coordinates: along is north at the spawn, right is east.
            Vector2 ahead = ForestLayout.TrailPoint(10f, 0f), right = ForestLayout.TrailPoint(10f, 4f);
            if (ahead.y <= ForestLayout.SpawnXZ.y + 5f)
                problems.Add("TrailPoint(10, 0) does not lie north of the spawn");
            if (right.x <= ahead.x + 2f)
                problems.Add("TrailPoint(10, 4) does not lie east of TrailPoint(10, 0)");

            Assert.IsNotNull(ForestLayout.Landmarks);
            Assert.Greater(ForestLayout.Landmarks.Length, 0, "The layout names no landmarks.");
            var names = new HashSet<string>();
            foreach (ForestLayout.Landmark landmark in ForestLayout.Landmarks)
            {
                if (string.IsNullOrEmpty(landmark.name) || !names.Add(landmark.name))
                    problems.Add($"landmark name '{landmark.name}' is empty or used twice");
                // Every pivot in the core stays at least a metre inside the boundary.
                float inside = -ForestLayout.SignedDistanceToBoundary(landmark.xz);
                if (inside < 1f)
                    problems.Add($"landmark {landmark.name} at {landmark.xz} is {inside:0.00} m inside the boundary, expected at least 1 m");
            }

            Assert.IsNotNull(ForestLayout.Viewpoints);
            var ids = new HashSet<string>();
            foreach (ForestLayout.Viewpoint viewpoint in ForestLayout.Viewpoints)
            {
                if (string.IsNullOrEmpty(viewpoint.id) || !ids.Add(viewpoint.id))
                    problems.Add($"viewpoint id '{viewpoint.id}' is empty or used twice");
                // The boundary views stand inside the wall and look out.
                if (viewpoint.id != null && viewpoint.id.StartsWith("boundary_", StringComparison.Ordinal)
                    && ForestLayout.SignedDistanceToBoundary(new Vector2(viewpoint.position.x, viewpoint.position.z)) >= 0f)
                    problems.Add($"viewpoint {viewpoint.id} stands outside the boundary");
            }
            foreach (string id in new[] { "trail_start", "trail_mid_fwd", "clearing", "ground_close", "overview_oblique", "crouch_hide", "long_sightline", "overview_ortho" })
                if (!ids.Contains(id))
                    problems.Add($"the layout has no viewpoint '{id}'");

            // The boundary itself: a rounded rectangle of 65 by 75 m with 30 m corners.
            Assert.AreEqual(-ForestLayout.HalfExtentX, ForestLayout.SignedDistanceToBoundary(Vector2.zero), 1e-3f);
            Assert.AreEqual(0f, ForestLayout.SignedDistanceToBoundary(new Vector2(ForestLayout.HalfExtentX, 0f)), 1e-3f);
            Assert.AreEqual(0f, ForestLayout.SignedDistanceToBoundary(new Vector2(0f, -ForestLayout.HalfExtentZ)), 1e-3f);
            Assert.AreEqual(10f, ForestLayout.SignedDistanceToBoundary(new Vector2(0f, ForestLayout.HalfExtentZ + 10f)), 1e-3f);
            float corner = ForestLayout.CornerRadius * (1f - 1f / Mathf.Sqrt(2f));
            Assert.AreEqual(0f, ForestLayout.SignedDistanceToBoundary(new Vector2(ForestLayout.HalfExtentX - corner, ForestLayout.HalfExtentZ - corner)), 1e-3f,
                "The corner of the boundary is not a 30 m arc.");

            ForestTestSupport.AssertNone(problems, "layout problem(s)");
        }

        static void CheckSpline(string name, Vector2[] controlPoints, float margin, List<string> problems)
        {
            Assert.IsNotNull(controlPoints, $"ForestLayout.{name} is not set.");
            Assert.GreaterOrEqual(controlPoints.Length, 2, $"ForestLayout.{name} needs at least two control points.");

            float length = ForestLayout.SplineLength(controlPoints);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / 0.5f));
            float worst = float.NegativeInfinity;
            Vector2 worstAt = default;
            for (int i = 0; i <= steps; i++)
            {
                Vector2 point = ForestLayout.SampleSpline(controlPoints, i / (float)steps);
                float signed = ForestLayout.SignedDistanceToBoundary(point);
                if (signed > worst)
                {
                    worst = signed;
                    worstAt = point;
                }
            }
            if (worst > -margin)
                problems.Add($"{name} comes within {-worst:0.00} m of the boundary at {worstAt}, expected at least {margin:0.00} m");

            // A Catmull-Rom spline passes through its control points, and its ends are its first and last.
            foreach (Vector2 control in controlPoints)
                if (ForestLayout.DistanceToSpline(controlPoints, control) > 0.05f)
                    problems.Add($"{name} misses its control point {control} by {ForestLayout.DistanceToSpline(controlPoints, control):0.00} m");
            if (Vector2.Distance(ForestLayout.SampleSpline(controlPoints, 0f), controlPoints[0]) > 0.05f
                || Vector2.Distance(ForestLayout.SampleSpline(controlPoints, 1f), controlPoints[controlPoints.Length - 1]) > 0.05f)
                problems.Add($"{name} does not start and end at its first and last control point");

            // Arc-length parameterised: equal steps of the parameter are equal steps along the line.
            Vector2 a = ForestLayout.SampleSpline(controlPoints, 0.25f), b = ForestLayout.SampleSpline(controlPoints, 0.25f + 1f / length);
            if (Mathf.Abs(Vector2.Distance(a, b) - 1f) > 0.05f)
                problems.Add($"{name} is not parameterised by arc length: a 1 m step of the parameter moved {Vector2.Distance(a, b):0.000} m");
        }

        // ---- Scatter ----

        // Section 7.2. Tall firs: about 60 in the core and 220 on the bank. Young firs: about 360 and 410.
        // Branches: 45 scattered, one per stump patch or log, two more at the deadfall.
        // Review 1 of the M4 captures retuned the tunables these counts follow (ForestLayout; rule: a pinned count changes only with
        // its tunable, on evidence; build/issues/M4-review1.md): the overstory read as parkland, so the core holds groves of tall
        // firs (about 115 core and 250 bank firs, 360); sightline breaks, the clearing's rim and a closed bank foot add young firs
        // (about 880); 80 scattered dry branches instead of 45 (about 100 with the ring rule); the fern patch cap is 1,050.
        // D2, the density pass (build/issues/D2-density.md; the owner: "far too sparse"), retuned the same tunables on purpose: about
        // 280 tall and 660 young firs a hectare in the core (were 80 and 290), so 800 tall firs and 1,700 young firs in all; 22 logs
        // (5 beside the game trails, 14 among the trees, 3 at the deadfall), 23 stump patches, 310 branches; the clearing a glade of
        // 10 m (grass 134); fern caps 2,300 and 800. The counts below are the measured plan of the final build; the 15 % band is unchanged.
        static readonly KeyValuePair<ForestClass, int>[] ExpectedCounts =
        {
            new KeyValuePair<ForestClass, int>(ForestClass.TallFir, 800),
            new KeyValuePair<ForestClass, int>(ForestClass.YoungFir, 1700),
            new KeyValuePair<ForestClass, int>(ForestClass.Boulder, 90),
            new KeyValuePair<ForestClass, int>(ForestClass.Log, 22),
            new KeyValuePair<ForestClass, int>(ForestClass.Stump, 23),
            new KeyValuePair<ForestClass, int>(ForestClass.Branch, 310),
            new KeyValuePair<ForestClass, int>(ForestClass.GrassPatch, 135),
        };
        const int FernPatchCap = 2300, FernClumpCap = 800;

        [Test]
        public void Scatter_IsDeterministic()
        {
            World world = GetWorld();
            Assert.IsNull(world.firstDifference, "Two plans of the same seed differ: " + world.firstDifference);
            Assert.AreEqual(ForestLayout.Seed, world.plan.seed);

            var counts = new Dictionary<ForestClass, int>();
            var problems = new List<string>();
            for (int i = 0; i < world.plan.placements.Length; i++)
            {
                ForestPlacement placement = world.plan.placements[i];
                counts.TryGetValue(placement.cls, out int count);
                counts[placement.cls] = count + 1;

                if (placement.cls == ForestClass.Landmark || placement.cls == ForestClass.RockFace)
                    problems.Add($"placement {i} has class {placement.cls}, which no placement may have");
                if (!IsPrefabOfClass(placement.prefabId, placement.cls))
                    problems.Add($"placement {i}: prefab '{placement.prefabId}' is not a {placement.cls}");
                if (placement.tint != 0 && placement.tint != 1)
                    problems.Add($"placement {i} has tint {placement.tint}");
                if (placement.landmark == null)
                    problems.Add($"placement {i} has a null landmark; scattered instances carry \"\"");
                int cellX = Mathf.FloorToInt((placement.position.x + 128f) / 32f), cellZ = Mathf.FloorToInt((placement.position.z + 128f) / 32f);
                if (placement.cellX != cellX || placement.cellZ != cellZ)
                    problems.Add($"placement {i} at ({placement.position.x:0.0}, {placement.position.z:0.0}) is filed under cell ({placement.cellX}, {placement.cellZ}), expected ({cellX}, {cellZ})");
            }

            foreach (KeyValuePair<ForestClass, int> expected in ExpectedCounts)
            {
                counts.TryGetValue(expected.Key, out int actual);
                if (Mathf.Abs(actual - expected.Value) > 0.15f * expected.Value)
                    problems.Add($"{actual} placements of class {expected.Key}, expected {expected.Value} within 15 %");
            }
            counts.TryGetValue(ForestClass.FernPatch, out int fernPatches);
            counts.TryGetValue(ForestClass.FernClump, out int fernClumps);
            if (fernPatches == 0 || fernPatches > FernPatchCap)
                problems.Add($"{fernPatches} fern patches, expected some and at most {FernPatchCap}");
            if (fernClumps == 0 || fernClumps > FernClumpCap)
                problems.Add($"{fernClumps} fern clumps, expected some and at most {FernClumpCap}");
            ForestTestSupport.AssertNone(problems, "scatter plan problem(s)");
        }

        static bool IsPrefabOfClass(string prefabId, ForestClass cls)
        {
            switch (cls)
            {
                case ForestClass.TallFir: return ForestTestSupport.Contains(ForestPrefabIds.TallFirs, prefabId);
                case ForestClass.YoungFir: return ForestTestSupport.Contains(ForestPrefabIds.YoungFirs, prefabId);
                case ForestClass.Boulder: return ForestTestSupport.Contains(ForestPrefabIds.Boulders, prefabId);
                case ForestClass.Log: return ForestTestSupport.Contains(ForestPrefabIds.Logs, prefabId);
                case ForestClass.Stump: return ForestTestSupport.Contains(ForestPrefabIds.Stumps, prefabId);
                case ForestClass.Branch: return ForestTestSupport.Contains(ForestPrefabIds.Branches, prefabId);
                case ForestClass.FernPatch: return ForestTestSupport.Contains(ForestPrefabIds.FernPatches, prefabId);
                case ForestClass.FernClump: return ForestTestSupport.Contains(ForestPrefabIds.FernClumps, prefabId);
                case ForestClass.GrassPatch: return ForestTestSupport.Contains(ForestPrefabIds.GrassPatches, prefabId);
                default: return false;
            }
        }

        static string FirstDifference(ForestPlan a, ForestPlan b)
        {
            if (a.seed != b.seed) return $"seed {a.seed} against {b.seed}";
            if (a.grounded != b.grounded) return "the grounded flag";
            if (a.placements.Length != b.placements.Length) return $"{a.placements.Length} placements against {b.placements.Length}";
            for (int i = 0; i < a.placements.Length; i++)
            {
                ForestPlacement p = a.placements[i], q = b.placements[i];
                // Exact comparison on purpose: the same seed must give the same bits.
                bool same = p.prefabId == q.prefabId && p.cls == q.cls && p.position == q.position && p.rotation == q.rotation
                    && p.scale == q.scale && p.sink == q.sink && p.clearance == q.clearance && p.tint == q.tint
                    && p.cellX == q.cellX && p.cellZ == q.cellZ && p.landmark == q.landmark
                    && p.position.Equals(q.position) && p.rotation.Equals(q.rotation);
                if (!same)
                    return $"placement {i}: {p.prefabId} at {p.position} against {q.prefabId} at {q.position}";
            }
            return null;
        }

        [Test]
        public void Scatter_KeepsCorridorsAndSpawnClear()
        {
            World world = GetWorld();
            var problems = new List<string>();
            int colliders = 0;
            for (int i = 0; i < world.plan.placements.Length; i++)
            {
                ForestPlacement placement = world.plan.placements[i];
                var xz = new Vector2(placement.position.x, placement.position.z);

                // Checked on every placement, so a prop with a collider cannot slip through with a clearance of 0.
                float expected = ForestKitSpec.ClearanceRadius(placement.prefabId) * placement.scale;
                if (Mathf.Abs(placement.clearance - expected) > 1e-3f)
                    problems.Add($"placement {i} ({placement.prefabId}, scale {placement.scale:0.00}) has clearance {placement.clearance:0.000}, expected {expected:0.000}");
                if (placement.clearance <= 0f)
                    continue;
                colliders++;

                float toTrail = ForestLayout.DistanceToAnyTrail(xz) - placement.clearance;
                if (toTrail < ForestLayout.CorridorClearance - 1e-3f)
                    problems.Add($"placement {i} ({placement.prefabId}) leaves {toTrail:0.00} m to a trail centre line, expected {ForestLayout.CorridorClearance:0.0} m");
                float toSpawn = Vector2.Distance(xz, ForestLayout.SpawnXZ) - placement.clearance;
                if (toSpawn < ForestLayout.SpawnClearance - 1e-3f)
                    problems.Add($"placement {i} ({placement.prefabId}) leaves {toSpawn:0.00} m to the spawn, expected {ForestLayout.SpawnClearance:0.0} m");
            }
            Assert.Greater(colliders, 0, "The plan holds no placement with a collider, so the rule was not exercised.");
            ForestTestSupport.AssertNone(problems, "placement(s) break the corridor or spawn rule");
        }

        [Test]
        public void Scatter_GroundsOnFinalHeights()
        {
            World world = GetWorld();
            Assert.IsFalse(world.groundedAfterPlan, "Plan must not mark the plan as grounded.");
            Assert.AreEqual(0f, world.largestHeightAfterPlan, "Plan must leave every position.y at 0: heights are final only after the root mounds.");
            Assert.IsTrue(world.plan.grounded, "Ground must mark the plan as grounded.");

            var problems = new List<string>();
            float largestRise = 0f;
            for (int i = 0; i < world.plan.placements.Length; i++)
            {
                ForestPlacement placement = world.plan.placements[i];
                float ground = world.fields.HeightAt(placement.position.x, placement.position.z);
                if (Mathf.Abs(placement.position.y - (ground - placement.sink)) > 0.001f)
                    problems.Add($"placement {i} ({placement.prefabId}) has y {placement.position.y:0.0000}, the final ground minus its sink is {ground - placement.sink:0.0000}");
                if (placement.cls == ForestClass.TallFir)
                    largestRise = Mathf.Max(largestRise, ground - world.groundBeforeMounds[i]);
            }
            ForestTestSupport.AssertNone(problems, "placement(s) are not on the final heights");

            // The case the first plan got wrong: a pivot fixed before the mound under it was raised.
            Assert.Greater(largestRise, 0.03f, "No tall fir stands on a root mound that raised the ground by more than 3 cm, so this test did not exercise the stage order.");
        }

        // ---- Terrain ----

        const float TrailSlopeLimit = 12f, CoreSlopeLimit = 27f;

        [Test]
        public void Terrain_MeetsSlopeLimits()
        {
            World world = GetWorld();
            Assert.AreEqual(1025, world.fields.resolution);
            Assert.LessOrEqual(world.trailSlopeBefore, TrailSlopeLimit + 1e-3f, "The trail is too steep before the root mounds.");
            Assert.LessOrEqual(world.coreSlopeBefore, CoreSlopeLimit + 1e-3f, "The core is too steep before the root mounds.");
            Assert.LessOrEqual(world.trailSlopeAfter, TrailSlopeLimit + 1e-3f, "The trail is too steep after the root mounds.");
            Assert.LessOrEqual(world.coreSlopeAfter, CoreSlopeLimit + 1e-3f, "The core is too steep after the root mounds.");

            // The field and the lookups must describe the same ground.
            ForestFields fields = world.fields;
            Assert.AreEqual(512f, fields.WorldToIndex(0f), 1e-3f, "The origin is the centre of the 1025 grid.");
            Assert.AreEqual(fields.height[512, 512], fields.HeightAt(0f, 0f), 1e-4f);
            Assert.AreEqual(fields.height[600, 400], fields.HeightAt(400 * 0.25f - 128f, 600 * 0.25f - 128f), 1e-3f, "Fields are indexed [z, x].");
            Assert.Greater(fields.NormalAt(0f, 0f).y, 0.8f, "The ground normal at the origin does not point up.");
        }

        // The steepest slope of the field inside the boundary.
        static float SteepestCoreSlope(ForestFields fields)
        {
            float step = ForestLayout.TileSize / (fields.resolution - 1);
            float worst = 0f;
            for (int z = 0; z < fields.resolution; z++)
            {
                float worldZ = z * step - ForestLayout.TileSize * 0.5f;
                if (Mathf.Abs(worldZ) > ForestLayout.HalfExtentZ)
                    continue;
                for (int x = 0; x < fields.resolution; x++)
                {
                    float worldX = x * step - ForestLayout.TileSize * 0.5f;
                    if (Mathf.Abs(worldX) > ForestLayout.HalfExtentX || fields.slopeDegrees[z, x] <= worst)
                        continue;
                    if (ForestLayout.SignedDistanceToBoundary(new Vector2(worldX, worldZ)) < 0f)
                        worst = fields.slopeDegrees[z, x];
                }
            }
            return worst;
        }

        // The steepest gradient in the direction of travel along the trail centre line, in half-metre steps.
        static float SteepestTrailSlope(ForestFields fields)
        {
            float length = ForestLayout.SplineLength(ForestLayout.Trail);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / 0.5f));
            Vector2 previous = ForestLayout.SampleSpline(ForestLayout.Trail, 0f);
            float previousHeight = fields.HeightAt(previous.x, previous.y);
            float worst = 0f;
            for (int i = 1; i <= steps; i++)
            {
                Vector2 point = ForestLayout.SampleSpline(ForestLayout.Trail, i / (float)steps);
                float height = fields.HeightAt(point.x, point.y);
                float run = Vector2.Distance(point, previous);
                if (run > 0.01f)
                    worst = Mathf.Max(worst, Mathf.Atan(Mathf.Abs(height - previousHeight) / run) * Mathf.Rad2Deg);
                previous = point;
                previousHeight = height;
            }
            return worst;
        }
    }
}
