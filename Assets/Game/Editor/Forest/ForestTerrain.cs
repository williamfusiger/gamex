using System;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gamex.EditorTools
{
    /// <summary>
    /// Shapes and paints the ground. <see cref="ComputeFields"/> and <see cref="AddRootMounds"/> are
    /// plain array maths and touch no Unity object, so the planner and the tests can run them
    /// anywhere; <see cref="Create"/> turns the finished arrays into the terrain asset and object.
    /// Every number here is a starting value that has not been judged in a render yet.
    /// </summary>
    public static class ForestTerrain
    {
        // Floor (section 6.2, step 1). The fBm amplitude is the peak of the whole sum, not of its first octave.
        const float FloorAmplitude = 1.2f, FloorWavelength = 70f, FloorGain = 0.5f, FloorLacunarity = 2f;
        const int FloorOctaves = 4;
        // Spec 6.2 gives one mound term of 0.35 m at 9 m. Everywhere at full strength it made the floor beyond
        // 15 m a field of evenly spaced, evenly rounded humps, like dunes, and hid the gully and the hollow in
        // relief of their own size (review 2, three reviewers). Now a smaller term at 9 m and an irregular one at
        // 4.3 m, both scaled by a 50 m mask so hummocky stretches and flats alternate (a recorded deviation).
        const float MoundAmplitude = 0.22f, MoundWavelength = 9f, MoundAmplitudeB = 0.10f, MoundWavelengthB = 4.3f;
        const float MoundMaskWavelength = 50f, MoundMaskFloor = 0.25f;
        const float MicroAmplitudeA = 0.05f, MicroWavelengthA = 2.5f, MicroAmplitudeB = 0.02f, MicroWavelengthB = 1.2f;

        // The gully bed falls toward the hollow; where that makes it deeper than planned it also gets
        // wider, so its banks stay walkable instead of being left to the talus clamp.
        const float GullyTaper = 6f, GullyWidthPerDepth = 4.5f;

        // The generator aims below the limits it is checked against, so a different slope stencil
        // (Unity's own, or a test's) still finds the terrain inside them. The talus target bounds the
        // step between neighbours; the gradient of central differences can then reach 1.08 times its
        // tangent, which is 25.7 degrees.
        const float TrailTargetSlope = 10.5f, TalusTargetSlope = 24f;
        const int MaxTrailFilterPasses = 64, MaxTalusPasses = 4000;
        const float TalusMargin = 2f, SlopeCheckMargin = 0.5f;

        // The thicket noise is centred and stretched so that thickets (a field value of 0.5 or more)
        // cover roughly 30 % of the core and about a third of that reaches full density.
        // D2 (density pass): the centre was 0.4525 (thickets on about 30 % of the core); 0.66 puts them on most of it. The ground
        // paint keeps the old field (PaintThicketCentre, ForestFields.paintThicket).
        const float ThicketWavelength = 38f, ThicketCentre = 0.66f, ThicketGain = 0.65f, PaintThicketCentre = 0.4525f;
        const int ThicketOctaves = 3;

        // Painting (section 6.4). Surfaces are indexed by ForestSurface.
        const int SurfaceCount = 8;
        const int SurfaceMapResolution = 512;
        const float TrailUnblurredBand = 2.5f;
        const float MossWavelength = 11f, NeedleWavelength = 17f, PuddleWavelength = 4f, RutBreakupWavelength = 2.5f;
        const float RutInner = 0.30f, RutOuter = 0.75f, CentreStrip = 0.25f;
        // The painted trail thins out over its last metres instead of stopping at a line. The spawn stands
        // 4 m from the south end, where the bands are complete again. At the north end the ruts run out
        // into an apron of stony soil and loose gravel that reaches the foot of the outcrop's rocks: at M2
        // the trail faded out on bare litter 9 m short of them and ended nowhere (review 1, trail_strip_08).
        const float TrailEndFadeFrom = 0.5f, TrailEndFadeTo = 3.5f;
        const float ApronBearing = -20f, ApronOffset = 4.4f, ApronInner = 2.8f, ApronOuter = 6.5f, ApronStonyGain = 1.6f, ApronGravelGain = 0.5f;
        // The ruts are not two clean ribbons: gravel thins out in stretches where litter has closed over it,
        // and mud stands in separate puddles on the tread (review 1: "nothing is clean").
        const float RutGravelGain = 1.2f, RutBreakupLow = 0.45f, PuddleMudGain = 0.5f;
        const float TrunkHumusRadius = 1.2f, StumpSoilRadius = 1.5f, RockMossFalloff = 2.5f;
        // Young firs (review 1 of the M3 captures): humus under the crown, full to YoungFirHumusInner and gone by
        // YoungFirHumusOuter times the fir's scale (the crowns reach 1.5 to 2.5 m); moss patches and dry needles are cut
        // under the crown, out to its drip line.
        const float YoungFirHumusInner = 0.6f, YoungFirHumusOuter = 1.9f, YoungFirHumusGain = 1.1f;
        const float YoungFirCrownInner = 1.0f, YoungFirCrownOuter = 2.2f, YoungFirCrownMossCut = 0.9f;
        // Tall firs (M4, second half): the same under their crowns, which are about 6 m across. Section 6.4 gives humus 0.6
        // within 1.2 m of the trunk only, so a grove's crowns met over open litter and the floor under the overstory was the
        // floor of the heath (build/issues/M4-overstory.md). Deviation from 6.4, like the young firs'.
        // Review 1 of the M4 captures: still weak against the bright litter (trunks at 15 to 40 m "float" over the floor); gain 0.9 to
        // 1.2 and out to the crown's edge (3.2 to 3.6 m times the scale).
        // Review 2 of the M4 captures: the floor under the groves was still as bright as open litter (all lenses). Gain 1.6, and the
        // ring at the trunk (spec 6.4: 0.6 within 1.2 m) 1.2, so the floor meets the root flare's value.
        const float TallFirHumusInner = 1.0f, TallFirHumusOuter = 3.6f, TallFirHumusGain = 1.6f, TallFirTrunkHumus = 1.2f;
        const float TallFirCrownInner = 1.5f, TallFirCrownOuter = 3.4f;
        // Moss on the ground around logs, and under every grass patch, so the clearing's swards carry colour at
        // distance. Around a rock, round 1 stamped mossy ground (after tinting the brightest floor layer) right up
        // to the stone: the darkest object stood in a pale halo, set on the ground, not in it (review 2). Now the
        // ground against a rock taller than RockSkirtMinTop is dark damp humus, full to RockSkirtFull and gone by
        // RockSkirtFade (times the scale) beyond its measured rim, and a fainter moss ring starts outside that.
        const float RockMossGain = 0.6f, RockMossFrom = 0.35f, RockMossTo = 0.6f, LogMossGain = 1.2f, GrassMossGain = 3.0f, GrassMossInner = 0.45f, GrassMossOuter = 1.3f;
        const float RockSkirtGain = 0.8f, RockSkirtFull = 0.5f, RockSkirtFade = 1.0f, RockSkirtMinTop = 0.8f;

        // Raw weights against litter's 1 (section 6.4). The spec's starting values (0.9, 0.8, 0.7 and 0.6) never let
        // a second surface win, so the first M1 captures showed one litter texture from wall to wall, with its
        // tile visible as rows. These let moss, humus and dry needles own their patches (issues/M1-ground.md).
        //
        // After M2 the litter was still alone on 46 % of the core and on most of the bank, and its tile was the
        // first thing every reviewer named: a herringbone from above, rows and hatching on the bank, the same
        // dark blotch every 1.5 m near the camera (issues/M2-review1.md). Stock TerrainLit has no anti-tiling
        // (risk 13), so the paint does what it can: the moss and needle patches are wider (thresholds 0.45 to
        // 0.65 and 0.40 to 0.60 of their noise, were 0.55 to 0.75 and 0.50 to 0.70), and two more noise fields
        // lay dark humus and stony soil over the open floor, so that litter is rarely the only layer for more
        // than a few metres and two textures with different tile sizes overlap almost everywhere. Those two
        // textures are even, low-contrast soil: they also make the floor the dark, damp ground of section 2.
        // Review 2: the moss patches were as wide as the needle and humus patches and, tinted ochre, read as dry
        // heath everywhere. With the layer now tinted to a darker green the patches are narrowed half way back
        // toward the spec (0.55 to 0.75, gain 0.8): an accent, not a second floor.
        const float HumusThicketGain = 1.6f, MossPatchGain = 1.5f, NeedlePatchGain = 1.6f, StripMossGain = 1.5f;
        const float MossPatchFrom = 0.50f, MossPatchTo = 0.70f, NeedlePatchFrom = 0.40f, NeedlePatchTo = 0.60f;
        const float FloorHumusWavelength = 9f, FloorHumusGain = 1.1f, FloorHumusFrom = 0.30f, FloorHumusTo = 0.60f;
        const float FloorStonyWavelength = 13f, FloorStonyGain = 0.9f, FloorStonyFrom = 0.36f, FloorStonyTo = 0.64f;
        // The floor mix stays off the trail and its shoulders: there the ground is seen from 0.4 m
        // (ground_close), and a second leaf texture under the needle bed shows as ghost leaves.
        const float FloorMixTrailFrom = 2.2f, FloorMixTrailTo = 4.5f;
        // Patches are broken up at the scale of a few steps, so a patch is not one flat mix from edge to edge
        // and its outline is not the smooth contour of one noise field.
        const float BreakupCoarse = 4.3f, BreakupFine = 1.9f, BreakupLow = 0.35f, BreakupHigh = 1.65f;
        // The bank is seen face on from 60 to 130 m, which is where a tile shows most. Out there litter is a
        // minor layer; stony soil (the largest tile of the kit, and an even texture) carries the slope, and
        // the moss and needle patches go on up the bank although it is steep and the fir wall will shade it.
        //
        // Review 2: painted mostly stony soil, the bank was one scan for tens of metres, and its repeat lined up
        // into stripes running up the slope in every boundary view. No layer should own it: the stony soil now
        // goes through the same breakup as the floor's patches, litter is raised and humus is a second surface.
        //
        // Review 2 of the M4 captures: every boundary view looked up a bare, scraped brown slope (the stony soil of the slope rule
        // carried it). Under the young-fir wall (the first BankForestWidth metres) the slope's stony soil is cut by BankSlopeStonyCut,
        // the noise stony soil is lower, humus doubles and moss patches are added: a forest floor that happens to slope.
        const float BankPaintWidth = 10f, BankLitter = 0.5f, BankStonyLow = 0.5f, BankStonyHigh = 0.75f, BankStonyWavelength = 15f, BankPatchShare = 0.75f;
        const float BankHumusGain = 0.9f, BankMossGain = 0.8f, BankForestWidth = 22f, BankSlopeStonyCut = 0.65f;
        // Painted rock (layer 6) needs a slope the bank does not have: its steepest part is about 31 degrees,
        // so with the spec's ramp of 32 to 42 only the noise bumps crossed the threshold and each got an
        // isolated pale blotch, a ring of stains at one height around the whole bowl (review 1). Flat rock
        // paint belongs back on the bank with the rock faces of M6, which give it a shape to sit on.
        const float RockSlopeFrom = 36f, RockSlopeTo = 46f;
        // The gully is a dry stream bed, not a second track: mostly washed-out stony soil, with gravel and
        // the mud of its banks in long stretches, and its paint fades out at both ends instead of stopping
        // in a round cap beside the hollow (review 1).
        const float GullyBreakupWavelength = 10f, GullyStonyGain = 1.3f, GullyGravelGain = 0.4f, GullyMudGain = 0.5f, GullyEndFade = 6f;
        // The hollow's mud: a ragged patch on the bottom of the bowl with dark humus around it, not a smooth
        // round disc of the darkest texture in the kit (review 1: "a soot-black blot").
        const float HollowMudGain = 0.65f, HollowHumusGain = 0.9f, HollowRagged = 0.5f;
        // The clearing is a pale needle bed with green swards on it (the grass patches stamp moss under
        // themselves). With the spec's 0.5 it had the hue of the litter around it and could not be told
        // from the rest of the floor, from the ground or from above (review 1). At round 1's 2.0 it was nearly
        // pure dry needles, and that one scan's tile showed as quilted bands at eye level and a herringbone from
        // above (review 2). The needles now go through the breakup noise and share the bed with litter and humus.
        const float ClearingNeedleGain = 1.0f, ClearingLitter = 0.65f, ClearingHumusGain = 0.45f;

        /// <summary>
        /// Steps 1 to 7 of section 6.2: the floor, the bank, the clearing, the hollow, the gully, the
        /// trail and the talus clamp, then the derived fields. Throws if the result breaks a slope limit.
        /// </summary>
        public static ForestFields ComputeFields(int seed)                               // steps 1 to 7 of section 6.2
        {
            int size = ForestLayout.HeightmapResolution;
            var fields = new ForestFields
            {
                resolution = size,
                height = new float[size, size],
                slopeDegrees = new float[size, size],
                thicket = new float[size, size],
                trailDistance = new float[size, size],
                gullyDistance = new float[size, size],
                boundaryDistance = new float[size, size],
            };
            NoiseOffsets offsets = NoiseOffsets.For(seed);

            ComputeDistances(fields);
            float[,] micro = ShapeFloorAndBank(fields, offsets);
            FlattenClearing(fields);
            SinkHollow(fields);
            CarveGully(fields);
            CarveTrail(fields, micro);
            ClampTalus(fields);

            fields.RefreshSlope();
            ComputeThicket(fields, offsets);
            AssertSlopeLimits(fields, "after the talus clamp");
            return fields;
        }

        /// <summary>
        /// Step 8: a low mound under every tall fir that stands on gentle ground, so no trunk rises
        /// out of a plane. Must run after the plan exists and before any placement gets its height.
        /// </summary>
        public static void AddRootMounds(ForestFields fields, ForestPlan plan)           // step 8; also refreshes slopeDegrees
        {
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            // Decide on the heights as they were: a mound must not depend on the mound of a neighbour
            // or on the order of the list.
            var mounds = new List<Vector4>();
            foreach (ForestPlacement placement in plan.placements)
            {
                if (placement.cls != ForestClass.TallFir) continue;
                float x = placement.position.x, z = placement.position.z;
                float rise = Mathf.Lerp(ForestLayout.RootMoundMinHeight, ForestLayout.RootMoundMaxHeight, Hash01(x, z, 11));
                float radius = Mathf.Lerp(ForestLayout.RootMoundMinRadius, ForestLayout.RootMoundMaxRadius, Hash01(x, z, 23));
                if (fields.SlopeAt(x, z) > ForestLayout.RootMoundMaxSlope) continue;
                // Gentle at the trunk is not enough. The steepest flank of a mound adds a gradient of 0.19;
                // on ground of 16 degrees that makes 25.5, on a gully bank it would cross the 27 degree limit.
                if (SteepestWithin(fields, x, z, radius + fields.SampleSpacing) > ForestLayout.RootMoundMaxFootprintSlope) continue;
                // The tread is never touched. Planned trunks stand 2.9 m from the line, so this only guards hand edits.
                if (fields.Sample(fields.trailDistance, x, z) < radius + ForestLayout.TreadHalfWidth) continue;
                mounds.Add(new Vector4(x, z, rise, radius));
            }

            foreach (Vector4 mound in mounds)
            {
                float radius = mound.w;
                int minX = Mathf.Max(Mathf.CeilToInt(fields.WorldToIndex(mound.x - radius)), 0);
                int maxX = Mathf.Min(Mathf.FloorToInt(fields.WorldToIndex(mound.x + radius)), fields.resolution - 1);
                int minZ = Mathf.Max(Mathf.CeilToInt(fields.WorldToIndex(mound.y - radius)), 0);
                int maxZ = Mathf.Min(Mathf.FloorToInt(fields.WorldToIndex(mound.y + radius)), fields.resolution - 1);
                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        float dx = fields.IndexToWorld(x) - mound.x, dz = fields.IndexToWorld(z) - mound.y;
                        float distance = Mathf.Sqrt(dx * dx + dz * dz);
                        if (distance >= radius) continue;
                        fields.height[z, x] += mound.z * 0.5f * (1f + Mathf.Cos(Mathf.PI * distance / radius));
                    }
                }
                fields.RefreshSlope(minX - 1, maxX + 1, minZ - 1, maxZ + 1);
            }

            string collars = AddRockCollars(fields, plan);
            AssertSlopeLimits(fields, $"after {mounds.Count} root mounds and the rock collars");
            Debug.Log($"ForestTerrain: {mounds.Count} root mounds; {collars}.");
        }

        /// <summary>
        /// Raises a soil collar against every rock whose lip stands above the ground beside it (see
        /// ForestLayout.RockCollarCover and the RockLips table): the ground comes up to the lip at the measured rim,
        /// capped at RockCollarMaxLip, and falls off outward no steeper than the slope allowance. Where the lip
        /// needs more than a ramp under the rock can give, the ground under the rock is raised as a plateau. Every
        /// rock's sink then grows by the lift at its pivot, so no rock moves; only the soil comes up. A collar that
        /// would push a sample of the core above TalusTargetSlope (or above its own slope where it was steeper
        /// already) is lowered step by step (CollarShares) and dropped when even a quarter of it does not fit. Deterministic: rocks in plan order, heights as they
        /// were before any collar.
        /// </summary>
        // What share of its height a collar is tried at, in turn, until it passes the slope rule.
        static readonly float[] CollarShares = { 1f, 0.85f, 0.7f, 0.55f, 0.4f, 0.25f };
        // A collar fades out over another rock's footprint: from this share of that rock's rim, to this many
        // metres beyond it.
        const float NeighbourCore = 0.3f, NeighbourFade = 0.3f;

        static string AddRockCollars(ForestFields fields, ForestPlan plan)
        {
            int size = fields.resolution, last = size - 1, bearings = ForestLayout.RockLipBearings;
            float spacing = fields.SampleSpacing;
            float step = 2f * Mathf.PI / bearings;
            float limit = Mathf.Tan(ForestLayout.RockCollarTargetSlope * Mathf.Deg2Rad);
            var baseHeight = (float[,])fields.height.Clone();
            var lift = new float[size, size];
            ForestPlacement[] placements = plan.placements;
            int built = 0, softened = 0, dropped = 0;
            float highestCollar = 0f, highestPlateau = 0f;

            var need = new float[bearings];
            var reach = new float[bearings];
            var allowance = new float[bearings];
            var width = new float[bearings];

            // Every measured rock, so that one rock's soil does not climb over its neighbour: in a cluster the
            // skirts of touching rocks otherwise buried each other and the cluster sank into the ground (rendered
            // in build/captures/m2-r2/look/clusters).
            var rocks = new List<int>();
            var rockYaw = new float[placements.Length];
            var rockWidest = new float[placements.Length];
            for (int i = 0; i < placements.Length; i++)
            {
                if (placements[i].cls != ForestClass.Boulder || placements[i].prefabId == null || !ForestLayout.RockLips.TryGetValue(placements[i].prefabId, out ForestLayout.RockLip measured)) continue;
                rocks.Add(i);
                rockYaw[i] = ForestLayout.YawOf(placements[i].rotation);
                foreach (float radius in measured.rimRadius) rockWidest[i] = Mathf.Max(rockWidest[i], radius * placements[i].scale);
            }
            var neighbours = new List<int>();

            for (int i = 0; i < placements.Length; i++)
            {
                ForestPlacement rock = placements[i];
                if (rock.cls != ForestClass.Boulder || rock.prefabId == null || !ForestLayout.RockLips.TryGetValue(rock.prefabId, out ForestLayout.RockLip lip)) continue;
                float px = rock.position.x, pz = rock.position.z;
                float pivotY = fields.Sample(baseHeight, px, pz) - rock.sink;
                float firstBearing = 0f, nearest = float.MaxValue, widest = 0f, deepest = 0f;
                for (int k = 0; k < bearings; k++)
                {
                    float radians = k * step;
                    float lipHeight = Mathf.Min(lip.lipHeight[k], ForestLayout.RockCollarMaxLip);
                    Vector3 offset = rock.rotation * (new Vector3(Mathf.Sin(radians) * lip.rimRadius[k], lipHeight, Mathf.Cos(radians) * lip.rimRadius[k]) * rock.scale);
                    float x = px + offset.x, z = pz + offset.z;
                    reach[k] = Mathf.Sqrt(offset.x * offset.x + offset.z * offset.z);
                    if (k == 0) firstBearing = ForestLayout.Bearing(new Vector2(offset.x, offset.z));
                    need[k] = pivotY + offset.y + ForestLayout.RockCollarCover - fields.Sample(baseHeight, x, z);
                    // The ground's own slope along the whole ramp limits how steep the collar may be (taken at the rim
                    // alone, a ramp that crossed steeper ground further out broke the slope rule and was halved).
                    Vector2 outward = new Vector2(offset.x, offset.z).normalized;
                    float slope = 0f;
                    for (float along = 0f; along <= ForestLayout.RockCollarMaxWidth + 0.01f; along += 0.5f)
                        slope = Mathf.Max(slope, fields.SlopeAt(x + outward.x * along, z + outward.y * along));
                    allowance[k] = Mathf.Max(0.06f, limit - Mathf.Tan(Mathf.Min(slope, 80f) * Mathf.Deg2Rad));
                    nearest = Mathf.Min(nearest, reach[k]);
                    widest = Mathf.Max(widest, reach[k]);
                    deepest = Mathf.Max(deepest, need[k]);
                }
                if (deepest <= 0.01f) continue;

                // Around the rim the collar may not change faster than it may rise: neighbours of a deep bearing are raised.
                for (int pass = 0; pass < 3; pass++)
                    for (int k = 0; k < bearings; k++)
                    {
                        int before = (k + bearings - 1) % bearings, after = (k + 1) % bearings;
                        float run = allowance[k] * reach[k] * step;
                        need[k] = Mathf.Max(need[k], Mathf.Max(need[before], need[after]) - run);
                    }

                float inner = Mathf.Max(0.4f, 0.45f * nearest);
                float plateau = 0f;
                for (int k = 0; k < bearings; k++)
                    if (need[k] > 0f) plateau = Mathf.Max(plateau, need[k] - allowance[k] * Mathf.Max(reach[k] - inner, 0f) * 2f / Mathf.PI);
                plateau = Mathf.Min(plateau, ForestLayout.RockCollarMaxPlateau * rock.scale);
                for (int k = 0; k < bearings; k++)
                {
                    // Under the rock the ground may also fall from the plateau no faster than the allowance, so on a
                    // side without a lip the soil still reaches part way up the rim.
                    float ramp = allowance[k] * Mathf.Max(reach[k] - inner, 0f) * 2f / Mathf.PI;
                    need[k] = Mathf.Clamp(Mathf.Max(need[k], plateau - ramp), 0f, plateau + ramp);
                    width[k] = Mathf.Clamp(need[k] * Mathf.PI / (2f * allowance[k]), ForestLayout.RockCollarMinWidth, ForestLayout.RockCollarMaxWidth);
                    need[k] = Mathf.Min(need[k], allowance[k] * width[k] * 2f / Mathf.PI);
                }

                // The window the collar can touch, and a copy of it to undo a collar that breaks the slope rule.
                float outer = widest + ForestLayout.RockCollarMaxWidth + spacing;
                neighbours.Clear();
                foreach (int j in rocks)
                {
                    if (j == i) continue;
                    float apart = Vector2.Distance(new Vector2(placements[j].position.x, placements[j].position.z), new Vector2(px, pz));
                    if (apart < outer + rockWidest[j] + NeighbourFade) neighbours.Add(j);
                }
                int minX = Mathf.Clamp(Mathf.FloorToInt(fields.WorldToIndex(px - outer)), 1, last - 1);
                int maxX = Mathf.Clamp(Mathf.CeilToInt(fields.WorldToIndex(px + outer)), 1, last - 1);
                int minZ = Mathf.Clamp(Mathf.FloorToInt(fields.WorldToIndex(pz - outer)), 1, last - 1);
                int maxZ = Mathf.Clamp(Mathf.CeilToInt(fields.WorldToIndex(pz + outer)), 1, last - 1);
                int windowX = maxX - minX + 3, windowZ = maxZ - minZ + 3;
                var savedLift = new float[windowZ, windowX];
                var savedSlope = new float[windowZ, windowX];
                for (int z = 0; z < windowZ; z++)
                    for (int x = 0; x < windowX; x++)
                    {
                        savedLift[z, x] = lift[minZ - 1 + z, minX - 1 + x];
                        savedSlope[z, x] = fields.slopeDegrees[minZ - 1 + z, minX - 1 + x];
                    }

                bool kept = false;
                float share = 1f;
                for (int attempt = 0; attempt < CollarShares.Length && !kept; attempt++)
                {
                    share = CollarShares[attempt];
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        for (int x = minX; x <= maxX; x++)
                        {
                            float dx = fields.IndexToWorld(x) - px, dz = fields.IndexToWorld(z) - pz;
                            float distance = Mathf.Sqrt(dx * dx + dz * dz);
                            float index = Mathf.Repeat(ForestLayout.Bearing(new Vector2(dx, dz)) - firstBearing, 360f) / (360f / bearings);
                            int low = Mathf.FloorToInt(index) % bearings, high = (low + 1) % bearings;
                            float t = index - Mathf.Floor(index);
                            float rim = Mathf.Lerp(reach[low], reach[high], t), rise = Mathf.Lerp(need[low], need[high], t) * share;
                            float fall = Mathf.Lerp(width[low], width[high], t), floor = plateau * share;
                            float value;
                            if (distance <= inner) value = floor;
                            else if (distance < rim) value = floor + (rise - floor) * 0.5f * (1f - Mathf.Cos(Mathf.PI * (distance - inner) / Mathf.Max(rim - inner, 0.01f)));
                            else if (distance < rim + fall) value = rise * 0.5f * (1f + Mathf.Cos(Mathf.PI * (distance - rim) / fall));
                            else value = 0f;
                            // The tread and its shoulders are never touched.
                            value *= ForestLayout.Smooth(ForestLayout.RockCollarTrailFrom, ForestLayout.RockCollarTrailTo, fields.trailDistance[z, x]);
                            // Nor the footprint of another rock: the soil fades out across its rim.
                            for (int n = 0; n < neighbours.Count && value > 0f; n++)
                            {
                                ForestPlacement other = placements[neighbours[n]];
                                float ox = fields.IndexToWorld(x) - other.position.x, oz = fields.IndexToWorld(z) - other.position.z;
                                float apart = Mathf.Sqrt(ox * ox + oz * oz);
                                if (apart >= rockWidest[neighbours[n]] + NeighbourFade) continue;
                                float otherRim = ForestLayout.RockRimRadius(other.prefabId, other.scale, rockYaw[neighbours[n]], ForestLayout.Bearing(new Vector2(ox, oz)));
                                value *= ForestLayout.Smooth(NeighbourCore * otherRim, otherRim + NeighbourFade, apart);
                            }
                            float previous = savedLift[z - minZ + 1, x - minX + 1];
                            lift[z, x] = Mathf.Max(previous, value);
                            fields.height[z, x] = baseHeight[z, x] + lift[z, x];
                        }
                    }
                    fields.RefreshSlope(minX - 1, maxX + 1, minZ - 1, maxZ + 1);

                    kept = true;
                    for (int z = minZ - 1; z <= maxZ + 1 && kept; z++)
                        for (int x = minX - 1; x <= maxX + 1; x++)
                        {
                            if (fields.boundaryDistance[z, x] > SlopeCheckMargin) continue;
                            float before = savedSlope[z - minZ + 1, x - minX + 1];
                            if (fields.slopeDegrees[z, x] <= Mathf.Max(TalusTargetSlope, before + 0.01f)) continue;
                            kept = false;
                            if (Vector2.Distance(new Vector2(px, pz), ForestLayout.BoulderFieldAnchor) < 0.05f)
                            {
                                var at = new Vector2(fields.IndexToWorld(x) - px, fields.IndexToWorld(z) - pz);
                                Debug.Log($"ForestTerrain: the rock_close boulder's collar at {share:0.00} breaks the slope rule {at.magnitude:0.00} m from its pivot, " +
                                    $"bearing {ForestLayout.Bearing(at):0} (local {Mathf.Repeat(ForestLayout.Bearing(at) - firstBearing, 360f):0}): {before:0.0} to {fields.slopeDegrees[z, x]:0.0} degrees.");
                            }
                            break;
                        }
                    if (kept) { if (attempt > 0) softened++; break; }

                    // Undo, and try again with a lower collar.
                    for (int z = 0; z < windowZ; z++)
                        for (int x = 0; x < windowX; x++)
                        {
                            int wz = minZ - 1 + z, wx = minX - 1 + x;
                            lift[wz, wx] = savedLift[z, x];
                            fields.height[wz, wx] = baseHeight[wz, wx] + lift[wz, wx];
                            fields.slopeDegrees[wz, wx] = savedSlope[z, x];
                        }
                }
                if (!kept) { dropped++; continue; }
                built++;
                for (int k = 0; k < bearings; k++) highestCollar = Mathf.Max(highestCollar, need[k]);
                if (Vector2.Distance(new Vector2(px, pz), ForestLayout.BoulderFieldAnchor) < 0.05f)
                    Debug.Log($"ForestTerrain: the rock_close boulder's collar at {share:0.00} of its height: plateau {plateau:0.00} m, soil at the rim per bearing from its local +Z " +
                        $"[{string.Join(", ", Array.ConvertAll(need, value => value.ToString("0.00")))}] m, ramp width up to {Mathf.Max(width):0.00} m.");
                highestPlateau = Mathf.Max(highestPlateau, plateau);
            }

            // No rock moves: each one sinks by as much as the ground under its pivot came up.
            float largestSink = 0f;
            for (int i = 0; i < placements.Length; i++)
            {
                if (placements[i].cls != ForestClass.Boulder) continue;
                float raised = fields.Sample(lift, placements[i].position.x, placements[i].position.z);
                if (raised <= 0f) continue;
                placements[i].sink += raised;
                largestSink = Mathf.Max(largestSink, raised);
            }
            return $"{built} rock collars ({softened} lowered to fit the slope rule, {dropped} dropped), highest {highestCollar:0.00} m at a rim, " +
                $"highest plateau {highestPlateau:0.00} m, largest extra sink {largestSink:0.00} m";
        }

        /// <summary>
        /// Builds the terrain asset (heights, layers, alphamaps), the surface map and the scene object.
        /// The asset is reused when it exists, so its GUID stays stable across rebuilds.
        /// </summary>
        public static Terrain Create(ForestFields fields, ForestPlan plan, Transform parent)   // TerrainData asset, layers, alphamaps, surface map, GameObject
        {
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            EnsureAssetFolder(ForestPaths.DataFolder);

            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(ForestPaths.TerrainDataPath);
            bool isNew = data == null;
            if (isNew)
                data = new TerrainData();

            // Resolution before size: changing the resolution is reported to rescale the size.
            data.heightmapResolution = fields.resolution;
            data.size = new Vector3(ForestLayout.TileSize, ForestLayout.HeightRange, ForestLayout.TileSize);
            data.alphamapResolution = ForestLayout.AlphamapResolution;
            data.baseMapResolution = 1024;
            if (data.heightmapResolution != fields.resolution)
                throw new InvalidOperationException($"TerrainData took heightmap resolution {data.heightmapResolution}, not the requested {fields.resolution}.");
            if (data.alphamapResolution != ForestLayout.AlphamapResolution)
                throw new InvalidOperationException($"TerrainData took alphamap resolution {data.alphamapResolution}, not the requested " +
                    $"{ForestLayout.AlphamapResolution}. Set ForestLayout.AlphamapResolution to 1024 (the fallback of spec 6.1).");

            // The asset exists before layers and alphamaps: the control textures are its sub-objects.
            if (isNew)
                AssetDatabase.CreateAsset(data, ForestPaths.TerrainDataPath);

            data.SetHeights(0, 0, NormalisedHeights(fields));

            TerrainLayer[] layers = ChooseLayers();
            data.terrainLayers = layers;
            int resolution = data.alphamapResolution;
            float[] surfaces = PaintSurfaces(fields, plan, resolution);
            if (layers.Length > 0)
                data.SetAlphamaps(0, 0, ToAlphamaps(surfaces, resolution, layers.Length));
            WriteSurfaceMap(surfaces, resolution);
            EditorUtility.SetDirty(data);

            // Terrain.CreateTerrainGameObject assigns the pipeline's default terrain material and marks it dirty (measured by
            // the M0 probe). Left alone, the build's SaveAssets rewrites that file inside the package cache and Unity warns
            // that an immutable package was altered. Nothing in it changed, so the mark is taken back.
            Material packageDefault = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.defaultTerrainMaterial : null;
            bool defaultWasDirty = packageDefault != null && EditorUtility.IsDirty(packageDefault);
            GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
            if (packageDefault != null && !defaultWasDirty)
                EditorUtility.ClearDirty(packageDefault);
            terrainObject.name = "Terrain";
            terrainObject.transform.SetParent(parent, false);
            terrainObject.transform.position = new Vector3(-ForestLayout.TileSize * 0.5f, ForestLayout.TerrainBaseY, -ForestLayout.TileSize * 0.5f);
            // Matters only for a later probe bake.
            GameObjectUtility.SetStaticEditorFlags(terrainObject, StaticEditorFlags.ContributeGI);

            var terrain = terrainObject.GetComponent<Terrain>();
            Material material = ForestMaterials.GetTerrainMaterial();
            if (material == null)
            {
                Debug.LogWarning("ForestTerrain: the forest terrain material is not built yet; using the pipeline's default terrain material.");
                material = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.defaultTerrainMaterial : null;
            }
            if (material != null)
                terrain.materialTemplate = material;
            terrain.drawInstanced = true;
            terrain.ignoreQualitySettings = true;
            terrain.heightmapPixelError = 5f;
            // Further than anything can be seen, so the low-resolution basemap is never shown.
            terrain.basemapDistance = 1000f;
            terrain.shadowCastingMode = ShadowCastingMode.On;

            // There is no public Terrain.receiveGI; without this a bake would try to lightmap the terrain.
            var serialized = new SerializedObject(terrain);
            SerializedProperty receiveGI = serialized.FindProperty("m_ReceiveGI");
            if (receiveGI != null)
            {
                receiveGI.intValue = (int)ReceiveGI.LightProbes;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            terrain.Flush();
            Debug.Log($"ForestTerrain: {fields.resolution} x {fields.resolution} heights, {layers.Length} layers at alphamap {resolution}, " +
                $"surface map {ForestPaths.SurfaceMapPath}.");
            return terrain;
        }

        // ---- Shared by the files of work package 3 ----

        /// <summary>
        /// Section 3.2: at most 12 degrees along the trail centre line and at most 27 degrees anywhere
        /// inside the boundary. The player leaves the ground when sprinting down anything much steeper.
        /// </summary>
        internal static void AssertSlopeLimits(ForestFields fields, string stage)
        {
            float worstCore = 0f;
            int worstX = 0, worstZ = 0;
            for (int z = 0; z < fields.resolution; z++)
            {
                for (int x = 0; x < fields.resolution; x++)
                {
                    if (fields.boundaryDistance[z, x] > SlopeCheckMargin || fields.slopeDegrees[z, x] <= worstCore) continue;
                    worstCore = fields.slopeDegrees[z, x]; worstX = x; worstZ = z;
                }
            }
            if (worstCore > ForestLayout.MaxCoreSlope)
                throw new InvalidOperationException($"Forest terrain {stage}: the core is {worstCore:0.0} degrees steep at " +
                    $"({fields.IndexToWorld(worstX):0.0}, {fields.IndexToWorld(worstZ):0.0}); the limit is {ForestLayout.MaxCoreSlope}.");

            float step = fields.SampleSpacing;
            float length = ForestLayout.SplineLength(ForestLayout.Trail);
            float worstTrail = 0f, worstAt = 0f;
            Vector2 previous = ForestLayout.PointAtDistance(ForestLayout.Trail, 0f, out _);
            float previousHeight = fields.HeightAt(previous.x, previous.y);
            for (float metres = step; metres <= length; metres += step)
            {
                Vector2 point = ForestLayout.PointAtDistance(ForestLayout.Trail, metres, out _);
                float pointHeight = fields.HeightAt(point.x, point.y);
                // Both readings of "along the centre line": the gradient in the direction of travel, and the
                // steepness of the ground under the line.
                float along = Mathf.Atan(Mathf.Abs(pointHeight - previousHeight) / Vector2.Distance(point, previous)) * Mathf.Rad2Deg;
                float steepness = Mathf.Max(along, fields.SlopeAt(point.x, point.y));
                if (steepness > worstTrail) { worstTrail = steepness; worstAt = metres; }
                previous = point; previousHeight = pointHeight;
            }
            if (worstTrail > ForestLayout.MaxTrailSlope)
                throw new InvalidOperationException($"Forest terrain {stage}: the trail is {worstTrail:0.0} degrees steep {worstAt:0.0} m from its " +
                    $"south end; the limit is {ForestLayout.MaxTrailSlope}.");
        }

        /// <summary>A number in [0, 1) that depends only on the position (to the centimetre) and the salt.</summary>
        internal static float Hash01(float x, float z, int salt)
        {
            unchecked
            {
                uint h = (uint)Mathf.RoundToInt(x * 100f) * 0x9E3779B1u;
                h ^= (uint)Mathf.RoundToInt(z * 100f) * 0x85EBCA77u;
                h ^= (uint)salt * 0xC2B2AE3Du;
                h ^= h >> 15; h *= 0x2C1B3C6Du;
                h ^= h >> 12; h *= 0x297A2D39u;
                h ^= h >> 15;
                return (h >> 8) * (1f / 16777216f);
            }
        }

        /// <summary>Simplex noise in about [-1, 1] with the given feature size in metres.</summary>
        internal static float Noise(float x, float z, float wavelength, Vector2 offset)
        {
            return noise.snoise(new float2(x / wavelength + offset.x, z / wavelength + offset.y));
        }

        /// <summary>The same, remapped to [0, 1].</summary>
        internal static float Noise01(float x, float z, float wavelength, Vector2 offset)
        {
            return Mathf.Clamp01(0.5f + 0.5f * Noise(x, z, wavelength, offset));
        }

        /// <summary>Where in noise space a given field starts. Derived from the seed, one stream per purpose.</summary>
        internal static Vector2 NoiseOffset(int seed, int purpose)
        {
            var random = new System.Random(unchecked(seed * 31 + purpose * 104729));
            return new Vector2((float)(random.NextDouble() * 512.0), (float)(random.NextDouble() * 512.0));
        }

        /// <summary>Creates an asset folder and its parents when they are missing. Never deletes anything.</summary>
        internal static void EnsureAssetFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        // ---- Heights ----

        sealed class NoiseOffsets
        {
            public Vector2[] floor;
            public Vector2 mound, moundB, moundMask, microA, microB, bank;
            public Vector2[] thicket;

            public static NoiseOffsets For(int seed)
            {
                var offsets = new NoiseOffsets { floor = new Vector2[FloorOctaves], thicket = new Vector2[ThicketOctaves] };
                for (int i = 0; i < FloorOctaves; i++) offsets.floor[i] = NoiseOffset(seed, 1 + i);
                offsets.mound = NoiseOffset(seed, 10);
                offsets.moundB = NoiseOffset(seed, 14);
                offsets.moundMask = NoiseOffset(seed, 15);
                offsets.microA = NoiseOffset(seed, 11);
                offsets.microB = NoiseOffset(seed, 12);
                offsets.bank = NoiseOffset(seed, 13);
                for (int i = 0; i < ThicketOctaves; i++) offsets.thicket[i] = NoiseOffset(seed, 20 + i);
                return offsets;
            }
        }

        /// <summary>Fractal noise whose peak is 1: octaves of halving amplitude and wavelength, divided by their sum.</summary>
        static float Fbm(float x, float z, float wavelength, float gain, float lacunarity, Vector2[] offsets)
        {
            float sum = 0f, amplitude = 1f, total = 0f;
            for (int i = 0; i < offsets.Length; i++)
            {
                sum += amplitude * Noise(x, z, wavelength, offsets[i]);
                total += amplitude;
                amplitude *= gain;
                wavelength /= lacunarity;
            }
            return sum / total;
        }

        /// <summary>
        /// A function that varies slowly over the ground, evaluated once on a coarse lattice over the
        /// tile and read back bilinearly. Simplex noise is most of the cost of building the ground, and
        /// a field whose features are many metres across loses under two millimetres this way.
        /// </summary>
        sealed class CoarseField
        {
            readonly float[] values;
            readonly int size;
            readonly float step;

            public CoarseField(float step, Func<float, float, float> function)
            {
                this.step = step;
                size = Mathf.CeilToInt(ForestLayout.TileSize / step) + 2;
                values = new float[size * size];
                float half = ForestLayout.TileSize * 0.5f;
                for (int z = 0; z < size; z++)
                    for (int x = 0; x < size; x++)
                        values[z * size + x] = function(x * step - half, z * step - half);
            }

            public float At(float x, float z)
            {
                float half = ForestLayout.TileSize * 0.5f;
                float fx = Mathf.Clamp((x + half) / step, 0f, size - 1.001f), fz = Mathf.Clamp((z + half) / step, 0f, size - 1.001f);
                int x0 = (int)fx, z0 = (int)fz;
                float tx = fx - x0, tz = fz - z0;
                int at = z0 * size + x0;
                float south = values[at] + (values[at + 1] - values[at]) * tx;
                float north = values[at + size] + (values[at + size + 1] - values[at + size]) * tx;
                return south + (north - south) * tz;
            }
        }

        static void ComputeDistances(ForestFields fields)
        {
            for (int z = 0; z < fields.resolution; z++)
            {
                float worldZ = fields.IndexToWorld(z);
                // Neighbouring samples share their closest spline piece, which the hint exploits.
                int trailHint = -1, gullyHint = -1;
                for (int x = 0; x < fields.resolution; x++)
                {
                    var point = new Vector2(fields.IndexToWorld(x), worldZ);
                    fields.boundaryDistance[z, x] = ForestLayout.SignedDistanceToBoundary(point);
                    fields.trailDistance[z, x] = ForestLayout.DistanceToSpline(ForestLayout.Trail, point, out _, ref trailHint);
                    fields.gullyDistance[z, x] = ForestLayout.DistanceToSpline(ForestLayout.Gully, point, out _, ref gullyHint);
                }
            }
        }

        /// <summary>
        /// Steps 1 and 2. Returns the micro relief separately: it is added after the trail is carved,
        /// faded out on the tread, so the trail's own profile is filtered from clean heights.
        /// </summary>
        static float[,] ShapeFloorAndBank(ForestFields fields, NoiseOffsets offsets)
        {
            int size = fields.resolution;
            var micro = new float[size, size];
            // The shortest octave of the floor is 8.75 m long; half a metre resolves it.
            var floor = new CoarseField(0.5f, (x, z) => Fbm(x, z, FloorWavelength, FloorGain, FloorLacunarity, offsets.floor));
            var moundMask = new CoarseField(1f, (x, z) => Mathf.Lerp(MoundMaskFloor, 1f, ForestLayout.Smooth(0.3f, 0.7f, Noise01(x, z, MoundMaskWavelength, offsets.moundMask))));
            for (int z = 0; z < size; z++)
            {
                float worldZ = fields.IndexToWorld(z);
                for (int x = 0; x < size; x++)
                {
                    float worldX = fields.IndexToWorld(x);
                    float mounds = MoundAmplitude * Noise(worldX, worldZ, MoundWavelength, offsets.mound)
                        + MoundAmplitudeB * Noise(worldX, worldZ, MoundWavelengthB, offsets.moundB);
                    float h = ForestLayout.FloorRisePerMetre * worldZ
                        + FloorAmplitude * floor.At(worldX, worldZ)
                        + mounds * moundMask.At(worldX, worldZ);

                    float outside = Mathf.Max(fields.boundaryDistance[z, x], 0f);
                    if (outside > 0f)
                    {
                        h += ForestLayout.BankRise * ForestLayout.Smooth(0f, ForestLayout.BankRiseWidth, outside);
                        float roll = ForestLayout.Smooth(ForestLayout.BankRiseWidth, ForestLayout.BankRiseWidth + 20f, outside);
                        if (roll > 0f)
                            h += ForestLayout.BankRollAmplitude * roll * Noise(worldX, worldZ, ForestLayout.BankRollWavelength, offsets.bank);
                    }

                    fields.height[z, x] = h;
                    micro[z, x] = MicroAmplitudeA * Noise(worldX, worldZ, MicroWavelengthA, offsets.microA)
                        + MicroAmplitudeB * Noise(worldX, worldZ, MicroWavelengthB, offsets.microB);
                }
            }
            return micro;
        }

        /// <summary>Step 3: the clearing is pulled 60 % toward its own mean height, fully inside 9 m, fading to 13 m.</summary>
        static void FlattenClearing(ForestFields fields)
        {
            Vector2 centre = ForestLayout.ClearingCentre;
            float radius = ForestLayout.ClearingRadius;
            IndexWindow(fields, centre, radius, out int minX, out int maxX, out int minZ, out int maxZ);

            double sum = 0.0;
            int count = 0;
            for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                    if (DistanceTo(fields, x, z, centre) <= radius) { sum += fields.height[z, x]; count++; }
            if (count == 0) return;
            float mean = (float)(sum / count);

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float distance = DistanceTo(fields, x, z, centre);
                    if (distance >= radius) continue;
                    float weight = ForestLayout.ClearingFlatten * (1f - ForestLayout.Smooth(ForestLayout.ClearingFlatRadius, radius, distance));
                    fields.height[z, x] = Mathf.Lerp(fields.height[z, x], mean, weight);
                }
            }
        }

        /// <summary>Step 4: a raised-cosine bowl.</summary>
        static void SinkHollow(ForestFields fields)
        {
            Vector2 centre = ForestLayout.HollowCentre;
            float radius = ForestLayout.HollowRadius;
            IndexWindow(fields, centre, radius, out int minX, out int maxX, out int minZ, out int maxZ);
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float distance = DistanceTo(fields, x, z, centre);
                    if (distance >= radius) continue;
                    fields.height[z, x] -= ForestLayout.HollowDepth * 0.5f * (1f + Mathf.Cos(Mathf.PI * distance / radius));
                }
            }
        }

        /// <summary>
        /// Step 5: a parabolic bed along the gully spline whose height never rises on the way to the hollow.
        /// </summary>
        static void CarveGully(ForestFields fields)
        {
            float step = fields.SampleSpacing;
            float length = ForestLayout.SplineLength(ForestLayout.Gully);
            int count = Mathf.CeilToInt(length / step) + 1;

            var bed = new float[count];
            var halfWidth = new float[count];
            var ground = new float[count];
            float running = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                float metres = Mathf.Min(i * step, length);
                Vector2 point = ForestLayout.PointAtDistance(ForestLayout.Gully, metres, out _);
                ground[i] = fields.HeightAt(point.x, point.y);
                // The head of the gully fades in instead of starting as a pit.
                float depth = ForestLayout.GullyDepth * ForestLayout.Smooth(0f, GullyTaper, metres);
                running = Mathf.Min(running, ground[i] - depth);
                bed[i] = running;
            }
            // A moving average of a falling sequence still falls, and takes the steps out of it.
            bed = BoxFilter(bed, Mathf.RoundToInt(3f / step));
            float widest = ForestLayout.GullyHalfWidth;
            for (int i = 0; i < count; i++)
            {
                halfWidth[i] = Mathf.Max(ForestLayout.GullyHalfWidth, (ground[i] - bed[i]) * GullyWidthPerDepth);
                widest = Mathf.Max(widest, halfWidth[i]);
            }
            halfWidth = BoxFilter(halfWidth, Mathf.RoundToInt(4f / step));

            for (int z = 0; z < fields.resolution; z++)
            {
                for (int x = 0; x < fields.resolution; x++)
                {
                    if (fields.gullyDistance[z, x] >= widest) continue;
                    var point = new Vector2(fields.IndexToWorld(x), fields.IndexToWorld(z));
                    float distance = ForestLayout.DistanceToSpline(ForestLayout.Gully, point, out float metres);
                    float width = SampleProfile(halfWidth, metres / step);
                    if (distance >= width) continue;

                    float across = distance / width;
                    float carved = Mathf.Lerp(fields.height[z, x], SampleProfile(bed, metres / step), 1f - across * across);
                    if (carved < fields.height[z, x])
                        fields.height[z, x] = carved;
                }
            }
        }

        /// <summary>
        /// Step 6: the tread follows a low-pass-filtered copy of the ground along the spline, 6 cm lower,
        /// flat across, blending out over the shoulders. Then the micro relief goes on everywhere else.
        /// </summary>
        static void CarveTrail(ForestFields fields, float[,] micro)
        {
            float step = fields.SampleSpacing;
            float length = ForestLayout.SplineLength(ForestLayout.Trail);
            int count = Mathf.CeilToInt(length / step) + 1;

            var profile = new float[count];
            for (int i = 0; i < count; i++)
            {
                Vector2 point = ForestLayout.PointAtDistance(ForestLayout.Trail, Mathf.Min(i * step, length), out _);
                profile[i] = fields.HeightAt(point.x, point.y);
            }

            int window = Mathf.RoundToInt(ForestLayout.TrailFilterWindow / step);
            float limit = Mathf.Tan(TrailTargetSlope * Mathf.Deg2Rad);
            int passes = 0;
            do
            {
                if (passes++ >= MaxTrailFilterPasses)
                    throw new InvalidOperationException($"Forest terrain: the trail profile is still steeper than {TrailTargetSlope} degrees " +
                        $"after {MaxTrailFilterPasses} passes of the {ForestLayout.TrailFilterWindow} m filter.");
                profile = BoxFilter(profile, window);
            }
            while (SteepestStep(profile, step) > limit);

            for (int z = 0; z < fields.resolution; z++)
            {
                for (int x = 0; x < fields.resolution; x++)
                {
                    float weight = 0f;
                    if (fields.trailDistance[z, x] < ForestLayout.ShoulderOuter)
                    {
                        var point = new Vector2(fields.IndexToWorld(x), fields.IndexToWorld(z));
                        float distance = ForestLayout.DistanceToSpline(ForestLayout.Trail, point, out float metres);
                        weight = 1f - ForestLayout.Smooth(ForestLayout.TreadHalfWidth, ForestLayout.ShoulderOuter, distance);
                        float target = SampleProfile(profile, metres / step) - ForestLayout.TrailCarveDepth;
                        fields.height[z, x] = Mathf.Lerp(fields.height[z, x], target, weight);
                    }
                    fields.height[z, x] += micro[z, x] * (1f - weight);
                }
            }
        }

        /// <summary>
        /// Step 7: no two neighbouring samples inside the boundary may differ by more than the talus
        /// slope allows. Where they do, the higher one comes down and the lower one goes up until
        /// they just fit, which is repeated until nothing moves: steep faces spread into ramps at the
        /// limit, as loose ground does. The tread is pinned (it has its own, tighter limit and must
        /// keep its filtered profile), and so is everything outside the boundary.
        /// </summary>
        static void ClampTalus(ForestFields fields)
        {
            int size = fields.resolution, last = size - 1;
            float spacing = fields.SampleSpacing;
            float axisStep = Mathf.Tan(TalusTargetSlope * Mathf.Deg2Rad) * spacing;
            float diagonalStep = axisStep * Mathf.Sqrt(2f);
            float pinnedWithin = ForestLayout.TreadHalfWidth + spacing;

            float reach = Mathf.Max(ForestLayout.HalfExtentX, ForestLayout.HalfExtentZ) + TalusMargin + 2f * spacing;
            int min = Mathf.Max(Mathf.FloorToInt(fields.WorldToIndex(-reach)), 1), max = Mathf.Min(Mathf.CeilToInt(fields.WorldToIndex(reach)), last - 1);

            var free = new bool[size * size];
            var active = new List<int>();
            for (int z = min; z <= max; z++)
            {
                for (int x = min; x <= max; x++)
                {
                    free[z * size + x] = fields.boundaryDistance[z, x] <= TalusMargin && fields.trailDistance[z, x] >= pinnedWithin;
                    active.Add(z * size + x);
                }
            }

            // Each sample owns the four pairs it forms with its east, north, north-east and north-west
            // neighbours, so every pair of the eight-neighbourhood is looked at exactly once per pass.
            int[] stepX = { 1, 0, 1, -1 }, stepZ = { 0, 1, 1, 1 };
            var stamp = new int[size * size];
            var next = new List<int>();
            for (int pass = 1; pass <= MaxTalusPasses; pass++)
            {
                next.Clear();
                foreach (int cell in active)
                {
                    int x = cell % size, z = cell / size;
                    for (int pair = 0; pair < 4; pair++)
                    {
                        int otherX = x + stepX[pair], otherZ = z + stepZ[pair];
                        int other = otherZ * size + otherX;
                        if (!free[cell] && !free[other]) continue;

                        float difference = fields.height[z, x] - fields.height[otherZ, otherX];
                        float excess = Mathf.Abs(difference) - (pair < 2 ? axisStep : diagonalStep);
                        // A tenth of a millimetre of tolerance, or rounding would keep the pass alive for ever.
                        if (excess <= 1e-4f) continue;

                        float move = difference > 0f ? excess : -excess;
                        if (free[cell] && free[other])
                        {
                            fields.height[z, x] -= move * 0.5f;
                            fields.height[otherZ, otherX] += move * 0.5f;
                        }
                        else if (free[cell]) fields.height[z, x] -= move;
                        else fields.height[otherZ, otherX] += move;

                        // Whatever moved has to be looked at again, by itself and by the neighbours that own a pair with it.
                        Requeue(x, z, size, min, max, pass, stamp, next);
                        Requeue(otherX, otherZ, size, min, max, pass, stamp, next);
                    }
                }
                if (next.Count == 0) return;

                // In index order, so the result does not depend on the order things were queued in.
                next.Sort();
                List<int> swap = active; active = next; next = swap;
            }
            throw new InvalidOperationException($"Forest terrain: the talus clamp was still moving {active.Count} samples after {MaxTalusPasses} passes " +
                $"(target {TalusTargetSlope} degrees).");
        }

        static void Requeue(int x, int z, int size, int min, int max, int pass, int[] stamp, List<int> next)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx < min || nx > max || nz < min || nz > max) continue;
                    int cell = nz * size + nx;
                    if (stamp[cell] == pass) continue;
                    stamp[cell] = pass;
                    next.Add(cell);
                }
            }
        }

        /// <summary>
        /// Section 7.2: where young firs crowd into thickets. Forced high on the foot of the bank (the
        /// wall of firs), raised on the rim of the clearing, zero inside it.
        /// </summary>
        static void ComputeThicket(ForestFields fields, NoiseOffsets offsets)
        {
            var noise = new CoarseField(1f, (x, z) => Fbm(x, z, ThicketWavelength, 0.5f, 2f, offsets.thicket));
            fields.paintThicket = new float[fields.resolution, fields.resolution];
            for (int z = 0; z < fields.resolution; z++)
            {
                float worldZ = fields.IndexToWorld(z);
                for (int x = 0; x < fields.resolution; x++)
                {
                    float worldX = fields.IndexToWorld(x);
                    float sample = noise.At(worldX, worldZ);
                    float value = ForestLayout.Smooth(0.45f, 0.65f, ThicketCentre + ThicketGain * sample);
                    float paint = ForestLayout.Smooth(0.45f, 0.65f, PaintThicketCentre + ThicketGain * sample);

                    // The wall of firs on the foot of the bank. It fades in over the last six metres of the
                    // core and out beyond the band: a hard step here would be painted as a line on the ground.
                    float outside = fields.boundaryDistance[z, x];
                    float wall = ForestLayout.ThicketBankFloor * ForestLayout.Smooth(-6f, 0f, outside)
                        * (1f - ForestLayout.Smooth(ForestLayout.ThicketBankBand, ForestLayout.ThicketBankBand + 4f, outside));
                    value = Mathf.Max(value, wall);
                    paint = Mathf.Max(paint, wall);

                    // Sightline breaks along the trail (ForestLayout.SightlineStations, review 1 of the M4 captures): a clump of young
                    // firs at each station, with a soft edge.
                    foreach (Vector2 station in ForestLayout.SightlineStations)
                    {
                        float dx = worldX - station.x, dz = worldZ - station.y;
                        float reach = ForestLayout.SightlineStationRadius + 1.5f;
                        if (dx * dx + dz * dz > reach * reach) continue;
                        float toStation = Mathf.Sqrt(dx * dx + dz * dz);
                        float stationValue = ForestLayout.SightlineStationThicket
                            * (1f - ForestLayout.Smooth(ForestLayout.SightlineStationRadius - 1.5f, reach, toStation));
                        value = Mathf.Max(value, stationValue);
                        paint = Mathf.Max(paint, stationValue);
                    }

                    // Nothing in the clearing, half as much again on its rim, with soft edges for the same reason. Review 1 of the M4
                    // captures: the rim also has a floor (ForestLayout.ClearingRimThicket), open where a trail enters.
                    float toClearing = DistanceTo(fields, x, z, ForestLayout.ClearingCentre);
                    // D2: the open part is the glade (ForestLayout.ClearingOpenRadius); its rim runs ClearingRimWidth beyond it.
                    float rimOuter = ForestLayout.ClearingOpenRadius + ForestLayout.ClearingRimWidth;
                    float beyondClearing = ForestLayout.Smooth(ForestLayout.ClearingOpenRadius, ForestLayout.ClearingOpenRadius + 2f, toClearing);
                    float onRim = 1f - ForestLayout.Smooth(rimOuter - 1f, rimOuter + 1f, toClearing);
                    if (onRim > 0f && toClearing > ForestLayout.ClearingOpenRadius)
                    {
                        float open = ForestLayout.Smooth(ForestLayout.ClearingRimTrailGap - 2f, ForestLayout.ClearingRimTrailGap,
                            ForestLayout.DistanceToAnyTrail(new Vector2(worldX, worldZ)));
                        value = Mathf.Max(value, ForestLayout.ClearingRimThicket * onRim * open);
                        paint = Mathf.Max(paint, ForestLayout.ClearingRimThicket * onRim * open);
                    }
                    value *= beyondClearing * (1f + 0.5f * onRim);
                    paint *= beyondClearing * (1f + 0.5f * onRim);

                    fields.thicket[z, x] = value;
                    fields.paintThicket[z, x] = paint;
                }
            }
        }

        /// <summary>The steepest sample of the slope field within a radius of a point.</summary>
        static float SteepestWithin(ForestFields fields, float x, float z, float radius)
        {
            var centre = new Vector2(x, z);
            IndexWindow(fields, centre, radius, out int minX, out int maxX, out int minZ, out int maxZ);
            float steepest = fields.SlopeAt(x, z);
            for (int iz = minZ; iz <= maxZ; iz++)
                for (int ix = minX; ix <= maxX; ix++)
                    if (DistanceTo(fields, ix, iz, centre) <= radius) steepest = Mathf.Max(steepest, fields.slopeDegrees[iz, ix]);
            return steepest;
        }

        static float DistanceTo(ForestFields fields, int x, int z, Vector2 centre)
        {
            float dx = fields.IndexToWorld(x) - centre.x, dz = fields.IndexToWorld(z) - centre.y;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static void IndexWindow(ForestFields fields, Vector2 centre, float radius, out int minX, out int maxX, out int minZ, out int maxZ)
        {
            int last = fields.resolution - 1;
            minX = Mathf.Clamp(Mathf.FloorToInt(fields.WorldToIndex(centre.x - radius)), 0, last);
            maxX = Mathf.Clamp(Mathf.CeilToInt(fields.WorldToIndex(centre.x + radius)), 0, last);
            minZ = Mathf.Clamp(Mathf.FloorToInt(fields.WorldToIndex(centre.y - radius)), 0, last);
            maxZ = Mathf.Clamp(Mathf.CeilToInt(fields.WorldToIndex(centre.y + radius)), 0, last);
        }

        /// <summary>Moving average over <paramref name="window"/> samples; the ends repeat their last value.</summary>
        static float[] BoxFilter(float[] values, int window)
        {
            int half = Mathf.Max(window / 2, 1);
            var result = new float[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                double sum = 0.0;
                for (int k = -half; k <= half; k++)
                    sum += values[Mathf.Clamp(i + k, 0, values.Length - 1)];
                result[i] = (float)(sum / (2 * half + 1));
            }
            return result;
        }

        static float SteepestStep(float[] profile, float step)
        {
            float steepest = 0f;
            for (int i = 1; i < profile.Length; i++)
                steepest = Mathf.Max(steepest, Mathf.Abs(profile[i] - profile[i - 1]) / step);
            return steepest;
        }

        static float SampleProfile(float[] profile, float index)
        {
            index = Mathf.Clamp(index, 0f, profile.Length - 1);
            int low = Mathf.Min((int)index, profile.Length - 2);
            return Mathf.Lerp(profile[low], profile[low + 1], index - low);
        }

        // ---- Terrain asset ----

        static float[,] NormalisedHeights(ForestFields fields)
        {
            int size = fields.resolution;
            var normalised = new float[size, size];
            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    float value = (fields.height[z, x] - ForestLayout.TerrainBaseY) / ForestLayout.HeightRange;
                    // Anything near the ends of the range would be clipped by the 16-bit heightmap.
                    if (value < 0.02f || value > 0.98f)
                        throw new InvalidOperationException($"Forest terrain: height {fields.height[z, x]:0.00} m at " +
                            $"({fields.IndexToWorld(x):0.0}, {fields.IndexToWorld(z):0.0}) is outside the range the tile can store.");
                    normalised[z, x] = value;
                }
            }
            return normalised;
        }

        /// <summary>
        /// The layers to paint: 8 or 4 as <see cref="ForestLayout.TerrainLayerCount"/> asks, fewer when
        /// fewer are built, none when the ground textures are not imported yet.
        /// </summary>
        static TerrainLayer[] ChooseLayers()
        {
            if (ForestLayout.TerrainLayerCount != 4 && ForestLayout.TerrainLayerCount != 8)
                throw new InvalidOperationException($"ForestLayout.TerrainLayerCount must be 4 or 8, not {ForestLayout.TerrainLayerCount}.");

            TerrainLayer[] built = ForestMaterials.GetTerrainLayers() ?? Array.Empty<TerrainLayer>();
            int available = built.Length >= 8 ? 8 : built.Length >= 4 ? 4 : 0;
            int count = Mathf.Min(ForestLayout.TerrainLayerCount, available);
            if (count == 0)
                Debug.LogWarning($"ForestTerrain: {built.Length} terrain layers are built; the terrain is left unpainted until the ground textures are imported.");
            else if (count < ForestLayout.TerrainLayerCount)
                Debug.LogWarning($"ForestTerrain: {ForestLayout.TerrainLayerCount} layers were asked for but only {built.Length} are built; painting {count}.");

            var layers = new TerrainLayer[count];
            Array.Copy(built, layers, count);
            return layers;
        }

        // ---- Painting ----

        /// <summary>
        /// Section 6.4: the weight of each of the eight surfaces per alphamap texel, normalised to sum
        /// to 1, as a flat array indexed [(z * resolution + x) * 8 + surface].
        /// </summary>
        static float[] PaintSurfaces(ForestFields fields, ForestPlan plan, int resolution)
        {
            var weights = new float[resolution * resolution * SurfaceCount];
            var unblurred = new bool[resolution * resolution];
            float texel = ForestLayout.TileSize / resolution;
            float half = ForestLayout.TileSize * 0.5f;

            // The stamps go into their surface's slot first, as a maximum, so two overlapping rocks do
            // not add up to twice the moss. The pass below adds the field terms on top.
            var crown = new float[resolution * resolution];
            StampPlacements(weights, crown, plan, resolution);

            Vector2 mossOffset = NoiseOffset(plan.seed, 30), needleOffset = NoiseOffset(plan.seed, 31),
                puddleOffset = NoiseOffset(plan.seed, 32), breakupOffset = NoiseOffset(plan.seed, 33),
                gullyGravelOffset = NoiseOffset(plan.seed, 34), gullyMudOffset = NoiseOffset(plan.seed, 35);
            var mossNoise = new CoarseField(0.5f, (x, z) => Noise01(x, z, MossWavelength, mossOffset));
            var needleNoise = new CoarseField(0.5f, (x, z) => Noise01(x, z, NeedleWavelength, needleOffset));
            Vector2 patchOffsetA = NoiseOffset(plan.seed, 36), patchOffsetB = NoiseOffset(plan.seed, 37);
            var patchA = new CoarseField(0.5f, (x, z) => 0.65f * Noise01(x, z, BreakupCoarse, patchOffsetA) + 0.35f * Noise01(x, z, BreakupFine, patchOffsetB));
            var patchB = new CoarseField(0.5f, (x, z) => 0.65f * Noise01(x, z, BreakupCoarse, patchOffsetB) + 0.35f * Noise01(x, z, BreakupFine, patchOffsetA));
            Vector2 floorHumusOffset = NoiseOffset(plan.seed, 38), floorStonyOffset = NoiseOffset(plan.seed, 39), bankStonyOffset = NoiseOffset(plan.seed, 43);
            var floorHumus = new CoarseField(0.5f, (x, z) => Noise01(x, z, FloorHumusWavelength, floorHumusOffset));
            var floorStony = new CoarseField(0.5f, (x, z) => Noise01(x, z, FloorStonyWavelength, floorStonyOffset));
            var bankStony = new CoarseField(1f, (x, z) => Noise01(x, z, BankStonyWavelength, bankStonyOffset));

            Vector2 trailStart = ForestLayout.Trail[0], trailEnd = ForestLayout.Trail[ForestLayout.Trail.Length - 1];
            Vector2 apronCentre = trailEnd + ForestLayout.FromBearing(ApronBearing) * ApronOffset;
            float gullyLength = ForestLayout.SplineLength(ForestLayout.Gully);
            for (int z = 0; z < resolution; z++)
            {
                float worldZ = (z + 0.5f) * texel - half;
                for (int x = 0; x < resolution; x++)
                {
                    float worldX = (x + 0.5f) * texel - half;
                    int at = (z * resolution + x) * SurfaceCount;

                    float slope = fields.Sample(fields.slopeDegrees, worldX, worldZ);
                    float thicket = Mathf.Clamp01(fields.Sample(fields.paintThicket ?? fields.thicket, worldX, worldZ));
                    float gully = fields.Sample(fields.gullyDistance, worldX, worldZ);
                    float trail = fields.Sample(fields.trailDistance, worldX, worldZ);
                    // Interpolating a distance field rounds off its crease on the centre line, which is
                    // exactly where the strip and the ruts are. Near the trail the distance is measured.
                    if (trail < TrailUnblurredBand + 1f)
                        trail = ForestLayout.DistanceToSpline(ForestLayout.Trail, new Vector2(worldX, worldZ));
                    unblurred[z * resolution + x] = trail < TrailUnblurredBand;

                    float toHollow = Vector2.Distance(new Vector2(worldX, worldZ), ForestLayout.HollowCentre);
                    float hollow = toHollow < ForestLayout.HollowRadius ? 0.5f * (1f + Mathf.Cos(Mathf.PI * toHollow / ForestLayout.HollowRadius)) : 0f;
                    float toClearing = Vector2.Distance(new Vector2(worldX, worldZ), ForestLayout.ClearingCentre);
                    // D2: the clearing's paint follows the glade (ForestLayout.ClearingOpenRadius); beyond it the forest floor.
                    float clearing = 1f - ForestLayout.Smooth(ForestLayout.ClearingOpenRadius - 1f, ForestLayout.ClearingOpenRadius + 1.5f, toClearing);

                    // The trail bands are analytic and anti-aliased over one texel where they are evaluated.
                    float strip = 0f, ruts = 0f, tread = 0f;
                    var here = new Vector2(worldX, worldZ);
                    if (trail < ForestLayout.ShoulderOuter)
                    {
                        float toEnd = Mathf.Min(Vector2.Distance(here, trailStart), Vector2.Distance(here, trailEnd));
                        float present = ForestLayout.Smooth(TrailEndFadeFrom, TrailEndFadeTo, toEnd);
                        strip = present * (1f - Edge(CentreStrip, trail, texel));
                        ruts = present * Edge(RutInner, trail, texel) * (1f - Edge(RutOuter, trail, texel));
                        tread = present * (1f - Edge(ForestLayout.TreadHalfWidth, trail, texel));
                    }

                    // Around 1 on average: 0.3 to 0.7 of the noise covers nearly all of it.
                    float rawA = patchA.At(worldX, worldZ), rawB = patchB.At(worldX, worldZ);
                    float varyA = Mathf.Lerp(BreakupLow, BreakupHigh, ForestLayout.Smooth(0.3f, 0.7f, rawA));
                    float varyB = Mathf.Lerp(BreakupLow, BreakupHigh, ForestLayout.Smooth(0.3f, 0.7f, rawB));

                    // The apron where the trail runs out at the foot of the north outcrop, with a ragged edge.
                    float apron = 0f;
                    float toApron = Vector2.Distance(here, apronCentre);
                    if (toApron < ApronOuter)
                        apron = ForestLayout.Smooth(0.25f, 0.7f, (1f - ForestLayout.Smooth(ApronInner, ApronOuter, toApron)) * (0.55f + 0.9f * rawB));

                    // The gully terms are only evaluated in and beside the cut.
                    float gullyBed = 0f, gullyBank = 0f, gullyCut = 0f, gravelStretch = 1f, mudStretch = 1f;
                    if (gully < ForestLayout.GullyHalfWidth + 0.6f)
                    {
                        ForestLayout.DistanceToSpline(ForestLayout.Gully, here, out float alongGully);
                        float gullyPresent = ForestLayout.Smooth(0f, GullyEndFade, alongGully) * ForestLayout.Smooth(0f, GullyEndFade, gullyLength - alongGully);
                        gullyBed = gullyPresent * (1f - ForestLayout.Smooth(0.6f, 1.0f, gully));
                        gullyBank = gullyPresent * ForestLayout.Smooth(0.6f, 1.0f, gully) * (1f - ForestLayout.Smooth(ForestLayout.GullyHalfWidth, ForestLayout.GullyHalfWidth + 0.6f, gully));
                        gullyCut = (0.35f + 0.65f * gullyPresent) * (1f - ForestLayout.Smooth(ForestLayout.GullyHalfWidth - 0.6f, ForestLayout.GullyHalfWidth + 0.4f, gully));
                        gravelStretch = Mathf.Lerp(0.15f, 1f, ForestLayout.Smooth(0.35f, 0.6f, Noise01(worldX, worldZ, GullyBreakupWavelength, gullyGravelOffset)));
                        mudStretch = Mathf.Lerp(0.2f, 1f, ForestLayout.Smooth(0.4f, 0.65f, Noise01(worldX, worldZ, GullyBreakupWavelength, gullyMudOffset)));
                    }

                    // Outside the boundary the litter gives way to stony soil, and the patches ignore slope and thicket.
                    float outside = fields.Sample(fields.boundaryDistance, worldX, worldZ);
                    float bank = ForestLayout.Smooth(0f, BankPaintWidth, outside);
                    // Humus and stony soil over the open floor, away from the trail.
                    float floorMix = ForestLayout.Smooth(FloorMixTrailFrom, FloorMixTrailTo, trail) * (1f - clearing);
                    // The mud of the hollow: its edge moves in and out with the breakup noise, its middle is always mud.
                    float raggedHollow = hollow + HollowRagged * (2f * rawA - 1f) * 4f * hollow * (1f - hollow);

                    float litter = (1f - (1f - BankLitter) * bank) * (1f - (1f - ClearingLitter) * clearing);
                    float humus = HumusThicketGain * thicket * varyA + weights[at + (int)ForestSurface.Humus]
                        + FloorHumusGain * floorMix * ForestLayout.Smooth(FloorHumusFrom, FloorHumusTo, floorHumus.At(worldX, worldZ)) * varyB
                        + HollowHumusGain * hollow
                        + ClearingHumusGain * clearing * varyB + BankHumusGain * bank * varyA;
                    float mossGround = Mathf.Lerp(1f - ForestLayout.Smooth(14f, 16f, slope), BankPatchShare, bank);
                    float bankForest = bank * (1f - ForestLayout.Smooth(BankForestWidth - 6f, BankForestWidth, outside));
                    // Under a young fir's crown the floor is its own needle cast: no moss patch, no pale dry needles
                    // (review 1 of the M3 captures: moss and the paler needle layer ran on right under the thickets).
                    float underCrown = crown[z * resolution + x];
                    float moss = MossPatchGain * ForestLayout.Smooth(MossPatchFrom, MossPatchTo, mossNoise.At(worldX, worldZ)) * mossGround * varyB
                        * (1f - YoungFirCrownMossCut * underCrown)
                        + BankMossGain * bankForest * ForestLayout.Smooth(0.35f, 0.65f, mossNoise.At(worldX, worldZ)) * varyB
                        + weights[at + (int)ForestSurface.MossyGround] + StripMossGain * strip;
                    float gravel = GullyGravelGain * gullyBed * gravelStretch;
                    if (ruts > 0f)
                        gravel += RutGravelGain * ruts * Mathf.Lerp(RutBreakupLow, 1f, ForestLayout.Smooth(0.3f, 0.65f, Noise01(worldX, worldZ, RutBreakupWavelength, breakupOffset)));
                    if (apron > 0f)
                        gravel += ApronGravelGain * apron * ForestLayout.Smooth(0.35f, 0.7f, rawA);
                    float stony = ForestLayout.Smooth(14f, 26f, slope) * (1f - BankSlopeStonyCut * bankForest) + weights[at + (int)ForestSurface.StonySoil] + GullyStonyGain * gullyCut
                        + FloorStonyGain * floorMix * ForestLayout.Smooth(FloorStonyFrom, FloorStonyTo, floorStony.At(worldX, worldZ)) * (BreakupLow + BreakupHigh - varyB)
                        + bank * Mathf.Lerp(BankStonyLow, BankStonyHigh, bankStony.At(worldX, worldZ)) * (BreakupLow + BreakupHigh - varyA)
                        + ApronStonyGain * apron;
                    float mud = HollowMudGain * ForestLayout.Smooth(0.35f, 0.6f, raggedHollow) + GullyMudGain * gullyBank * mudStretch;
                    if (tread > 0f)
                        mud += PuddleMudGain * tread * ForestLayout.Smooth(0.55f, 0.8f, Noise01(worldX, worldZ, PuddleWavelength, puddleOffset));
                    float rock = ForestLayout.Smooth(RockSlopeFrom, RockSlopeTo, slope);
                    float needleGround = Mathf.Lerp(1f - thicket, BankPatchShare, bank);
                    float needles = (NeedlePatchGain * needleGround * ForestLayout.Smooth(NeedlePatchFrom, NeedlePatchTo, needleNoise.At(worldX, worldZ)) * (BreakupLow + BreakupHigh - varyA)
                        + ClearingNeedleGain * clearing * (BreakupLow + BreakupHigh - varyB)) * (1f - underCrown);

                    // Rock first, then gravel and mud, then the rest share what is left.
                    rock = Mathf.Clamp01(rock);
                    float left = 1f - rock;
                    float hard = gravel + mud;
                    if (hard > 1f) { gravel /= hard; mud /= hard; hard = 1f; }
                    gravel *= left; mud *= left;
                    left *= 1f - hard;
                    float soft = litter + humus + moss + stony + needles;

                    weights[at + (int)ForestSurface.Litter] = left * litter / soft;
                    weights[at + (int)ForestSurface.Humus] = left * humus / soft;
                    weights[at + (int)ForestSurface.MossyGround] = left * moss / soft;
                    weights[at + (int)ForestSurface.Gravel] = gravel;
                    weights[at + (int)ForestSurface.StonySoil] = left * stony / soft;
                    weights[at + (int)ForestSurface.Mud] = mud;
                    weights[at + (int)ForestSurface.Rock] = rock;
                    weights[at + (int)ForestSurface.DryNeedles] = left * needles / soft;
                }
            }

            Blur(weights, unblurred, resolution);
            Normalise(weights);
            LogDominance(fields, weights, resolution);
            return weights;
        }

        /// <summary>
        /// How often one layer has the ground to itself (more than 60 % of the weight) in the clearing and on the
        /// bank, where a lone scan showed its tile for tens of metres (review 2). Logged, not enforced.
        /// </summary>
        static void LogDominance(ForestFields fields, float[] weights, int resolution)
        {
            float texel = ForestLayout.TileSize / resolution, half = ForestLayout.TileSize * 0.5f;
            int clearingTotal = 0, clearingLone = 0, bankTotal = 0, bankLone = 0;
            var clearingHeaviest = new int[SurfaceCount];
            var bankHeaviest = new int[SurfaceCount];
            for (int z = 0; z < resolution; z += 2)
            {
                float worldZ = (z + 0.5f) * texel - half;
                for (int x = 0; x < resolution; x += 2)
                {
                    float worldX = (x + 0.5f) * texel - half;
                    var here = new Vector2(worldX, worldZ);
                    bool inClearing = Vector2.Distance(here, ForestLayout.ClearingCentre) < ForestLayout.ClearingOpenRadius - 1.5f;
                    float outside = ForestLayout.SignedDistanceToBoundary(here);
                    bool onBank = outside > 2f && outside < 35f;
                    if (!inClearing && !onBank) continue;
                    int at = (z * resolution + x) * SurfaceCount, heaviest = 0;
                    for (int surface = 1; surface < SurfaceCount; surface++)
                        if (weights[at + surface] > weights[at + heaviest]) heaviest = surface;
                    bool lone = weights[at + heaviest] > 0.6f;
                    if (inClearing) { clearingTotal++; clearingHeaviest[heaviest]++; if (lone) clearingLone++; }
                    else { bankTotal++; bankHeaviest[heaviest]++; if (lone) bankLone++; }
                }
            }
            Debug.Log($"ForestTerrain: one layer above 60 % of the weight on {100f * clearingLone / Mathf.Max(clearingTotal, 1):0.0} % of the clearing " +
                $"and {100f * bankLone / Mathf.Max(bankTotal, 1):0.0} % of the bank (2 to 35 m out). Heaviest layer per texel, clearing " +
                $"[{string.Join(", ", clearingHeaviest)}], bank [{string.Join(", ", bankHeaviest)}] (litter, humus, moss, gravel, stony, mud, rock, needles).");
        }

        /// <summary>0 below the edge, 1 above it, with a one-texel ramp across it.</summary>
        static float Edge(float edge, float distance, float texel)
        {
            return ForestLayout.Smooth(edge - texel * 0.5f, edge + texel * 0.5f, distance);
        }

        /// <summary>
        /// Humus under tall trunks, moss around rocks and logs, stony soil under stump patches (it hides
        /// the rim of ground baked into those scans). Only for prefabs that are built: a milestone
        /// without trees gets no dark discs where trees will stand.
        /// </summary>
        static void StampPlacements(float[] weights, float[] crown, ForestPlan plan, int resolution)
        {
            var built = new Dictionary<string, bool>();
            foreach (ForestPlacement placement in plan.placements)
            {
                if (string.IsNullOrEmpty(placement.prefabId)) continue;
                if (!built.TryGetValue(placement.prefabId, out bool exists))
                {
                    exists = ForestPrefabs.Exists(placement.prefabId);
                    built[placement.prefabId] = exists;
                }
                if (!exists) continue;

                var centre = new Vector2(placement.position.x, placement.position.z);
                switch (placement.cls)
                {
                    case ForestClass.TallFir:
                        Stamp(weights, resolution, ForestSurface.Humus, centre, centre, TrunkHumusRadius - 0.2f, TrunkHumusRadius + 0.2f, TallFirTrunkHumus);
                        Stamp(weights, resolution, ForestSurface.Humus, centre, centre, TallFirHumusInner * placement.scale, TallFirHumusOuter * placement.scale, TallFirHumusGain);
                        StampMask(crown, resolution, centre, TallFirCrownInner * placement.scale, TallFirCrownOuter * placement.scale);
                        break;
                    case ForestClass.YoungFir:
                    {
                        // Dark humus under the crown, fading out by the drip line (review 1 of the M3 captures: the M2 floor
                        // ran on unchanged under the firs, trunks stood in clean litter). Deviation from section 6.4.
                        float scale = placement.scale;
                        Stamp(weights, resolution, ForestSurface.Humus, centre, centre, YoungFirHumusInner * scale, YoungFirHumusOuter * scale, YoungFirHumusGain);
                        StampMask(crown, resolution, centre, YoungFirCrownInner * scale, YoungFirCrownOuter * scale);
                        break;
                    }
                    case ForestClass.Boulder:
                    {
                        float yaw = ForestLayout.YawOf(placement.rotation);
                        if (ForestLayout.RockRimRadius(placement.prefabId, placement.scale, yaw, 0f) < 0f)
                        {
                            float edge = placement.clearance * 0.8f;
                            Stamp(weights, resolution, ForestSurface.MossyGround, centre, centre, edge, edge + RockMossFalloff, RockMossGain);
                            break;
                        }
                        // Dark damp soil against the stone, a faint moss ring beyond it, both following the measured rim.
                        float scale = placement.scale;
                        if (RockTop(placement) > RockSkirtMinTop)
                            StampRim(weights, resolution, ForestSurface.Humus, placement, yaw, -1f, -1f, RockSkirtFull * scale, RockSkirtFade * scale, RockSkirtGain);
                        StampRim(weights, resolution, ForestSurface.MossyGround, placement, yaw, RockMossFrom * scale, RockMossTo * scale,
                            RockMossTo * scale, RockMossTo * scale + RockMossFalloff, RockMossGain);
                        break;
                    }
                    case ForestClass.Log:
                    {
                        // A log is long and thin: the moss follows its axis (local X), not its bounding circle.
                        Vector3 axis = placement.rotation * Vector3.right;
                        Vector2 reach = new Vector2(axis.x, axis.z) * (placement.clearance * 0.85f);
                        Stamp(weights, resolution, ForestSurface.MossyGround, centre - reach, centre + reach, 0.3f * placement.scale, 0.3f * placement.scale + RockMossFalloff, LogMossGain);
                        break;
                    }
                    case ForestClass.Stump:
                        Stamp(weights, resolution, ForestSurface.StonySoil, centre, centre, StumpSoilRadius - 0.2f, StumpSoilRadius + 0.2f, 1f);
                        break;
                    case ForestClass.GrassPatch:
                        Stamp(weights, resolution, ForestSurface.MossyGround, centre, centre, GrassMossInner * placement.scale, GrassMossOuter * placement.scale, GrassMossGain);
                        break;
                }
            }
        }

        /// <summary>
        /// Raises a surface's slot around a measured rock, by the distance outside its rim on each bearing:
        /// fading in from <paramref name="fadeInFrom"/> to <paramref name="fadeInTo"/> (no fade-in when negative),
        /// full to <paramref name="full"/> and gone at <paramref name="fade"/>.
        /// </summary>
        static void StampRim(float[] weights, int resolution, ForestSurface surface, ForestPlacement rock, float yaw,
            float fadeInFrom, float fadeInTo, float full, float fade, float value)
        {
            float texel = ForestLayout.TileSize / resolution;
            float half = ForestLayout.TileSize * 0.5f;
            float widest = 0f;
            for (int k = 0; k < ForestLayout.RockLipBearings; k++)
                widest = Mathf.Max(widest, ForestLayout.RockRimRadius(rock.prefabId, rock.scale, yaw, yaw + k * 22.5f));
            float reach = widest + fade;
            var centre = new Vector2(rock.position.x, rock.position.z);
            int minX = Mathf.Clamp(Mathf.FloorToInt((centre.x - reach + half) / texel), 0, resolution - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt((centre.x + reach + half) / texel), 0, resolution - 1);
            int minZ = Mathf.Clamp(Mathf.FloorToInt((centre.y - reach + half) / texel), 0, resolution - 1);
            int maxZ = Mathf.Clamp(Mathf.CeilToInt((centre.y + reach + half) / texel), 0, resolution - 1);
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var offset = new Vector2((x + 0.5f) * texel - half, (z + 0.5f) * texel - half) - centre;
                    float distance = offset.magnitude;
                    if (distance >= reach) continue;
                    float beyond = distance - ForestLayout.RockRimRadius(rock.prefabId, rock.scale, yaw, ForestLayout.Bearing(offset));
                    if (beyond >= fade) continue;
                    float stamped = value * (1f - ForestLayout.Smooth(full, fade, beyond));
                    if (fadeInFrom >= 0f) stamped *= ForestLayout.Smooth(fadeInFrom, fadeInTo, beyond);
                    int at = (z * resolution + x) * SurfaceCount + (int)surface;
                    if (stamped > weights[at]) weights[at] = stamped;
                }
            }
        }

        /// <summary>Height of a placed rock's top above the ground at its pivot.</summary>
        static float RockTop(ForestPlacement rock)
        {
            ForestObjectEntry entry = ForestManifest.FindObjectForPrefab(rock.prefabId);
            return entry == null ? 0f : ForestManifest.SourceBounds(entry).max.y * rock.scale - rock.sink;
        }

        /// <summary>
        /// Raises a surface's slot to <paramref name="value"/> within <paramref name="inner"/> metres of
        /// the segment from a to b, fading to nothing at <paramref name="outer"/>.
        /// </summary>
        /// <summary>A disc of 1 to <paramref name="inner"/>, fading to 0 at <paramref name="outer"/>, kept as a maximum.</summary>
        static void StampMask(float[] mask, int resolution, Vector2 centre, float inner, float outer)
        {
            float texel = ForestLayout.TileSize / resolution;
            float half = ForestLayout.TileSize * 0.5f;
            int minX = Mathf.Clamp(Mathf.FloorToInt((centre.x - outer + half) / texel), 0, resolution - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt((centre.x + outer + half) / texel), 0, resolution - 1);
            int minZ = Mathf.Clamp(Mathf.FloorToInt((centre.y - outer + half) / texel), 0, resolution - 1);
            int maxZ = Mathf.Clamp(Mathf.CeilToInt((centre.y + outer + half) / texel), 0, resolution - 1);
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var point = new Vector2((x + 0.5f) * texel - half, (z + 0.5f) * texel - half);
                    float distance = Vector2.Distance(point, centre);
                    if (distance >= outer) continue;
                    float value = 1f - ForestLayout.Smooth(inner, outer, distance);
                    int at = z * resolution + x;
                    if (value > mask[at]) mask[at] = value;
                }
            }
        }

        static void Stamp(float[] weights, int resolution, ForestSurface surface, Vector2 a, Vector2 b, float inner, float outer, float value)
        {
            float texel = ForestLayout.TileSize / resolution;
            float half = ForestLayout.TileSize * 0.5f;
            int minX = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x, b.x) - outer + half) / texel), 0, resolution - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.x, b.x) + outer + half) / texel), 0, resolution - 1);
            int minZ = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.y, b.y) - outer + half) / texel), 0, resolution - 1);
            int maxZ = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.y, b.y) + outer + half) / texel), 0, resolution - 1);

            Vector2 ab = b - a;
            float lengthSquared = ab.sqrMagnitude;
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var point = new Vector2((x + 0.5f) * texel - half, (z + 0.5f) * texel - half);
                    float t = lengthSquared > 0f ? Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSquared) : 0f;
                    float distance = Vector2.Distance(point, a + ab * t);
                    if (distance >= outer) continue;
                    float stamped = value * (1f - ForestLayout.Smooth(inner, outer, distance));
                    int at = (z * resolution + x) * SurfaceCount + (int)surface;
                    if (stamped > weights[at]) weights[at] = stamped;
                }
            }
        }

        /// <summary>
        /// A 3 by 3 box blur that softens thresholded noise. Texels near the trail keep their value:
        /// the ruts are only three or four texels wide and were anti-aliased when they were painted.
        /// </summary>
        static void Blur(float[] weights, bool[] unblurred, int resolution)
        {
            var source = new float[resolution * resolution];
            for (int surface = 0; surface < SurfaceCount; surface++)
            {
                for (int i = 0; i < source.Length; i++)
                    source[i] = weights[i * SurfaceCount + surface];

                for (int z = 0; z < resolution; z++)
                {
                    int zLow = Mathf.Max(z - 1, 0) * resolution, zMid = z * resolution, zHigh = Mathf.Min(z + 1, resolution - 1) * resolution;
                    for (int x = 0; x < resolution; x++)
                    {
                        if (unblurred[zMid + x]) continue;
                        int xLow = Mathf.Max(x - 1, 0), xHigh = Mathf.Min(x + 1, resolution - 1);
                        float sum = source[zLow + xLow] + source[zLow + x] + source[zLow + xHigh]
                            + source[zMid + xLow] + source[zMid + x] + source[zMid + xHigh]
                            + source[zHigh + xLow] + source[zHigh + x] + source[zHigh + xHigh];
                        weights[(zMid + x) * SurfaceCount + surface] = sum / 9f;
                    }
                }
            }
        }

        static void Normalise(float[] weights)
        {
            for (int at = 0; at < weights.Length; at += SurfaceCount)
            {
                float sum = 0f;
                for (int surface = 0; surface < SurfaceCount; surface++) sum += weights[at + surface];
                if (sum <= 0f) { weights[at] = 1f; continue; }
                for (int surface = 0; surface < SurfaceCount; surface++) weights[at + surface] /= sum;
            }
        }

        /// <summary>
        /// The array SetAlphamaps wants, [z, x, layer]. With four layers the other four surfaces are
        /// folded into the look they are closest to: stony soil and dry needles into litter, mud into
        /// humus, rock into mossy ground.
        /// </summary>
        static float[,,] ToAlphamaps(float[] weights, int resolution, int layerCount)
        {
            var map = new float[resolution, resolution, layerCount];
            if (layerCount == SurfaceCount)
            {
                Buffer.BlockCopy(weights, 0, map, 0, weights.Length * sizeof(float));
                return map;
            }

            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int at = (z * resolution + x) * SurfaceCount;
                    map[z, x, (int)ForestSurface.Litter] = weights[at + (int)ForestSurface.Litter]
                        + weights[at + (int)ForestSurface.StonySoil] + weights[at + (int)ForestSurface.DryNeedles];
                    map[z, x, (int)ForestSurface.Humus] = weights[at + (int)ForestSurface.Humus] + weights[at + (int)ForestSurface.Mud];
                    map[z, x, (int)ForestSurface.MossyGround] = weights[at + (int)ForestSurface.MossyGround] + weights[at + (int)ForestSurface.Rock];
                    map[z, x, (int)ForestSurface.Gravel] = weights[at + (int)ForestSurface.Gravel];
                }
            }
            return map;
        }

        /// <summary>
        /// The future footstep and noise map: the heaviest surface per half metre, as an 8-bit
        /// greyscale PNG whose pixel value is the <see cref="ForestSurface"/> number. Row 0 is the south
        /// edge of the tile, column 0 the west edge. It always holds the eight surfaces, also when the
        /// terrain is painted with four layers.
        /// </summary>
        static void WriteSurfaceMap(float[] weights, int resolution)
        {
            int block = Mathf.Max(resolution / SurfaceMapResolution, 1);
            int size = resolution / block;
            var pixels = new byte[size * size];
            var sums = new float[SurfaceCount];
            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    Array.Clear(sums, 0, SurfaceCount);
                    for (int dz = 0; dz < block; dz++)
                    {
                        for (int dx = 0; dx < block; dx++)
                        {
                            int at = ((z * block + dz) * resolution + x * block + dx) * SurfaceCount;
                            for (int surface = 0; surface < SurfaceCount; surface++) sums[surface] += weights[at + surface];
                        }
                    }
                    int heaviest = 0;
                    for (int surface = 1; surface < SurfaceCount; surface++)
                        if (sums[surface] > sums[heaviest]) heaviest = surface;
                    pixels[z * size + x] = (byte)heaviest;
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.R8, false, true);
            try
            {
                texture.LoadRawTextureData(pixels);
                texture.Apply(false);
                File.WriteAllBytes(ForestPaths.SurfaceMapPath, texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
            AssetDatabase.ImportAsset(ForestPaths.SurfaceMapPath, ImportAssetOptions.ForceUpdate);

            // The pixel values are ids, not a picture: no colour conversion, no compression, no mips, readable.
            if (AssetImporter.GetAtPath(ForestPaths.SurfaceMapPath) is TextureImporter importer
                && (importer.sRGBTexture || importer.mipmapEnabled || !importer.isReadable
                    || importer.textureCompression != TextureImporterCompression.Uncompressed || importer.filterMode != FilterMode.Point))
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.mipmapEnabled = false;
                importer.isReadable = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.filterMode = FilterMode.Point;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.SaveAndReimport();
            }
        }
    }
}
