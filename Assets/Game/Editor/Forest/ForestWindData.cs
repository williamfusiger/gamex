using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gamex.EditorTools
{
    /// <summary>
    /// The baked per-vertex wind data of W1 (spec section 4): one RGBA8 vertex colour on every level of every tree, fern and
    /// grass mesh, and the compact 44-byte vertex layout that pays for it.
    ///
    /// R trunk sway weight, G branch / frond weight, B flutter weight, A branch / frond phase (wraps). Computed on the finished
    /// level from per-tree constants measured once on the source scan, so R, G and A are functions of the position in the tree
    /// and neighbouring levels bend alike. Deterministic: seeded hashes, arrays and sorts only (no hash-set enumeration).
    /// </summary>
    public static class ForestWindData
    {
        // Part of ForestKitSpec.Hash. wind-2: scalar phase noise; wind-3: thinner anchors, axial phase band; wind-4 (coordinator's decision of
        // 2026-10-17, issues/W1-shader.md section 6): a gentler phase field (values over half a cycle, cells twice as tall) so that neighbouring
        // needles bob alike across a LOD switch, and no branch weight G in the trunk's root zone.
        // wind-5 (W1 review round 2): fern and grass meshes carry each frond's or blade's anchor in UV2 (ClumpAnchors).
        public const string Version = "wind-5";
        public const float YoungClassHeight = 8.74f, TallClassHeight = 18.84f;
        public const float BranchReach = 3.0f, FlutterReach = 0.30f, SectionStep = 0.25f;
        public const float YoungPhaseCell = 0.70f, TallPhaseCell = 1.20f; public const int PhaseSectors = 8;
        /// <summary>The lattice values of the phase lie in [0, PhaseRange) cycles (wind-3: a whole cycle).</summary>
        public const float PhaseRange = 0.5f;
        /// <summary>
        /// The root zone: from the trunk piece's foot up to where its cross-section (sampled every RootSampleStep) is one round loop
        /// (extent from its centroid between RootRoundMin and RootRoundMax times its perimeter radius) for at least RootRoundRun metres.
        /// G is 0 there and rises to its formula over RootRamp metres above it. A trunk that is round from its foot has no root zone.
        /// </summary>
        public const float RootSampleStep = 0.05f, RootRoundMin = 0.9f, RootRoundMax = 1.25f, RootRoundRun = 0.5f, RootSearch = 3.0f, RootRamp = 0.25f;

        // The band outside the trunk over which the phase goes from the row's mean (on the axis) to the sector noise.
        const float AxialBand = 0.3f;

        // Triangles that share a position closer than this touch (ForestKitSpec.WeldQuantum, the thinner's weld).
        const float Weld = 0.0001f;

        public enum Part { Wood, Skirt, Foliage, CrownCore }

        /// <summary>The per-tree constants of spec 4.2: height, class height, the trunk's centreline and radius, the phase lattice.</summary>
        public sealed class TreeField
        {
            public float height, classHeight, phaseCell; public int seed;
            internal float sectionBottom, trunkTop;
            internal float rootTop = float.NegativeInfinity;

            /// <summary>The top of the trunk's root zone (prefab-space y); negative infinity when the trunk is round from its foot.</summary>
            public float RootTop => rootTop;
            internal Vector2[] centres = Array.Empty<Vector2>();
            internal float[] radii = Array.Empty<float>();

            /// <summary>The trunk centreline (prefab-space XZ) at a height; above the trunk's top, the top's.</summary>
            public Vector2 CentreAt(float y)
            {
                if (centres.Length == 0) return Vector2.zero;
                float f = (y - sectionBottom) / SectionStep;
                if (f <= 0f) return centres[0];
                int i = Mathf.FloorToInt(f);
                if (i >= centres.Length - 1) return centres[centres.Length - 1];
                return Vector2.Lerp(centres[i], centres[i + 1], f - i);
            }

            /// <summary>The trunk radius at a height; 0 above the trunk's top.</summary>
            public float RadiusAt(float y)
            {
                if (radii.Length == 0 || y > trunkTop) return 0f;
                float f = (y - sectionBottom) / SectionStep;
                if (f <= 0f) return radii[0];
                int i = Mathf.FloorToInt(f);
                if (i >= radii.Length - 1) return radii[radii.Length - 1];
                return Mathf.Lerp(radii[i], radii[i + 1], f - i);
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Trees
        // ------------------------------------------------------------------------------------------------

        /// <summary>The field of a tree, with H the top of its wood. The baker uses the overload that takes the scan's top.</summary>
        public static TreeField MeasureTree(string prefabId, ForestMeshTools.MeshData wood)
        {
            float top = 0f;
            foreach (Vector3 p in wood.positions) top = Mathf.Max(top, p.y);
            return MeasureTree(prefabId, wood, top);
        }

        /// <summary>
        /// Measures the field once per prefab on the source scan (spec 4.2): H = <paramref name="scanTop"/> (the scan's bounds max y),
        /// the trunk's centreline and radius from cross-sections of the largest connected wood piece every SectionStep metres.
        /// Throws if H exceeds its class height by more than 1 cm.
        /// </summary>
        public static TreeField MeasureTree(string prefabId, ForestMeshTools.MeshData wood, float scanTop)
        {
            if (wood == null) throw new ArgumentNullException(nameof(wood));
            ForestClass cls = ForestKitSpec.ClassOf(prefabId);
            bool young = cls == ForestClass.YoungFir;
            var field = new TreeField
            {
                height = Mathf.Max(scanTop, 0.01f),
                classHeight = young ? YoungClassHeight : TallClassHeight,
                phaseCell = young ? YoungPhaseCell : TallPhaseCell,
                seed = ForestKitSpec.Seed ^ StableHash(prefabId),
            };
            if (field.height > field.classHeight + 0.01f)
                throw new InvalidOperationException($"{prefabId}: the scan is {field.height:0.000} m tall, above its class height {field.classHeight} m (ForestWindData).");

            // The largest connected wood piece is the trunk.
            var all = new List<int>();
            foreach (int[] indices in wood.subMeshes) all.AddRange(indices);
            int[] triangles = all.ToArray();
            int[] pieceOfTriangle = ConnectedPieces(wood.positions, triangles, out int count);
            var tally = new int[Math.Max(count, 1)];
            int trunk = 0;
            for (int t = 0; t < pieceOfTriangle.Length; t++)
                if (++tally[pieceOfTriangle[t]] > tally[trunk]) trunk = pieceOfTriangle[t];

            float bottom = float.PositiveInfinity, topOfTrunk = float.NegativeInfinity;
            var trunkTriangles = new List<int>();
            for (int t = 0; t < pieceOfTriangle.Length; t++)
            {
                if (pieceOfTriangle[t] != trunk) continue;
                trunkTriangles.Add(t);
                for (int c = 0; c < 3; c++)
                {
                    float y = wood.positions[triangles[t * 3 + c]].y;
                    bottom = Mathf.Min(bottom, y);
                    topOfTrunk = Mathf.Max(topOfTrunk, y);
                }
            }
            if (trunkTriangles.Count == 0)
                throw new InvalidOperationException($"{prefabId}: the wood has no triangles to measure a trunk from.");

            float start = Mathf.Max(0f, Mathf.Ceil(bottom / SectionStep) * SectionStep);
            int sections = Mathf.Max(1, Mathf.FloorToInt((topOfTrunk - start) / SectionStep) + 1);
            field.sectionBottom = start;
            field.trunkTop = topOfTrunk;
            field.centres = new Vector2[sections];
            field.radii = new float[sections];
            for (int k = 0; k < sections; k++)
            {
                float y = start + k * SectionStep;
                double perimeter = 0.0, cx = 0.0, cz = 0.0;
                foreach (int t in trunkTriangles)
                {
                    if (!Section(wood.positions[triangles[t * 3]], wood.positions[triangles[t * 3 + 1]], wood.positions[triangles[t * 3 + 2]], y,
                            out Vector2 a, out Vector2 b))
                        continue;
                    double length = (b - a).magnitude;
                    perimeter += length;
                    cx += length * 0.5 * (a.x + b.x);
                    cz += length * 0.5 * (a.y + b.y);
                }
                if (perimeter > 1e-6)
                {
                    field.centres[k] = new Vector2((float)(cx / perimeter), (float)(cz / perimeter));
                    field.radii[k] = (float)(perimeter / (2.0 * Math.PI));
                }
                else
                {
                    // An empty section: the pivot axis and the previous radius.
                    field.centres[k] = Vector2.zero;
                    field.radii[k] = k > 0 ? field.radii[k - 1] : 0f;
                }
            }

            // The root zone (wind-4): the tall firs' trunk piece carries its roots and flare, whose sections near the ground are several
            // loops or lobes (extent 1.4 to 3.2 times the perimeter radius, the centroid wandering by up to 0.4 m), so rho - r_t gave
            // root and flare vertices G up to 0.1. Measured from the foot up, every RootSampleStep metres.
            float first = float.NaN;      // the lowest sample that cuts the trunk piece
            float runStart = float.NaN;   // the start of the current run of round sections
            float searchTop = Mathf.Min(topOfTrunk, bottom + RootSearch);
            for (int k = Mathf.CeilToInt(bottom / RootSampleStep); k * RootSampleStep <= searchTop; k++)
            {
                float y = k * RootSampleStep;
                bool round = RoundSection(wood.positions, triangles, trunkTriangles, y, out bool any);
                if (!any)
                {
                    runStart = float.NaN;
                    continue;
                }
                if (float.IsNaN(first)) first = y;
                if (!round)
                {
                    runStart = float.NaN;
                    continue;
                }
                if (float.IsNaN(runStart)) runStart = y;
                if (y - runStart >= RootRoundRun - 1e-4f)
                {
                    if (runStart > first + 1e-4f) field.rootTop = runStart;   // round from the foot: no root zone
                    break;
                }
            }
            return field;
        }

        // Whether the trunk piece's cross-section at a height is one round loop; any = the plane cuts the piece at all.
        static bool RoundSection(Vector3[] positions, int[] triangles, List<int> trunkTriangles, float y, out bool any)
        {
            double perimeter = 0.0, cx = 0.0, cz = 0.0;
            var points = new List<Vector2>();
            foreach (int t in trunkTriangles)
            {
                if (!Section(positions[triangles[t * 3]], positions[triangles[t * 3 + 1]], positions[triangles[t * 3 + 2]], y, out Vector2 a, out Vector2 b))
                    continue;
                double length = (b - a).magnitude;
                perimeter += length;
                cx += length * 0.5 * (a.x + b.x);
                cz += length * 0.5 * (a.y + b.y);
                points.Add(a);
                points.Add(b);
            }
            any = perimeter > 1e-6;
            if (!any) return false;
            var centre = new Vector2((float)(cx / perimeter), (float)(cz / perimeter));
            float extent = 0f;
            foreach (Vector2 p in points) extent = Mathf.Max(extent, (p - centre).magnitude);
            float radius = (float)(perimeter / (2.0 * Math.PI));
            return extent >= RootRoundMin * radius && extent <= RootRoundMax * radius;
        }

        /// <summary>Colours of a thinned level (or any level whose foliage pieces are found on the level itself).</summary>
        public static Color32[] TreeColours(TreeField field, ForestMeshTools.MeshData level, Part[] partOfSubMesh)
            => TreeColours(field, level, partOfSubMesh, null);

        /// <summary>
        /// Colours aligned with level.positions. <paramref name="scanPieces"/> (the scan level) are the baker's own pieces of its twigs,
        /// which are the level's foliage sub-mesh triangle for triangle; without them every foliage sub-mesh is split into its own
        /// connected pieces (each needle, stick and card is a separate quad).
        /// </summary>
        public static Color32[] TreeColours(TreeField field, ForestMeshTools.MeshData level, Part[] partOfSubMesh, ForestFoliageThinner.Pieces scanPieces)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (level == null) throw new ArgumentNullException(nameof(level));
            if (partOfSubMesh == null || partOfSubMesh.Length != level.subMeshes.Length)
                throw new ArgumentException("One part per sub-mesh is needed.", nameof(partOfSubMesh));

            int n = level.positions.Length;
            var part = new Part[n];
            var assigned = new bool[n];
            var pieceOfVertex = new int[n];
            for (int v = 0; v < n; v++) pieceOfVertex[v] = -1;
            var anchors = new List<Vector3>();
            var anchorDistance = new List<float>();
            bool scanUsed = false;

            for (int s = 0; s < level.subMeshes.Length; s++)
            {
                int[] indices = level.subMeshes[s];
                foreach (int v in indices)
                {
                    if (assigned[v]) continue;
                    assigned[v] = true;
                    part[v] = partOfSubMesh[s];
                }
                if (partOfSubMesh[s] != Part.Foliage || indices.Length == 0) continue;

                int[] pieceOfTriangle;
                int count;
                if (scanPieces != null && !scanUsed && scanPieces.pieceOfTriangle != null && scanPieces.pieceOfTriangle.Length == indices.Length / 3)
                {
                    pieceOfTriangle = scanPieces.pieceOfTriangle;
                    count = scanPieces.count;
                    scanUsed = true;
                }
                else
                {
                    pieceOfTriangle = ConnectedPieces(level.positions, indices, out count);
                }

                // The anchor of a piece is its vertex nearest the vertical axis through the prefab's origin, the thinner's own rule
                // (ForestFoliageThinner.FindPieces): every thinned level enlarges a kept piece about that vertex, so a needle keeps
                // its anchor, and with it its phase, on every level. The scan level takes the baker's pieces with their anchors.
                bool scanAnchors = ReferenceEquals(pieceOfTriangle, scanPieces?.pieceOfTriangle) && scanPieces.anchor != null;
                int first = anchors.Count;
                for (int p = 0; p < count; p++)
                {
                    anchors.Add(scanAnchors ? scanPieces.anchor[p] : Vector3.zero);
                    anchorDistance.Add(scanAnchors ? 0f : float.PositiveInfinity);
                }
                for (int t = 0; t < indices.Length / 3; t++)
                {
                    int piece = first + pieceOfTriangle[t];
                    for (int c = 0; c < 3; c++)
                    {
                        int v = indices[t * 3 + c];
                        if (pieceOfVertex[v] < 0) pieceOfVertex[v] = piece;
                        if (scanAnchors) continue;
                        Vector3 p = level.positions[v];
                        float rho = p.x * p.x + p.z * p.z;
                        if (rho < anchorDistance[piece])
                        {
                            anchorDistance[piece] = rho;
                            anchors[piece] = p;
                        }
                    }
                }
            }
            if (scanPieces != null && !scanUsed)
                throw new ArgumentException("The scan's pieces do not match the level's foliage sub-mesh.", nameof(scanPieces));

            var colours = new Color32[n];
            for (int v = 0; v < n; v++)
            {
                Vector3 p = level.positions[v];
                Part kind = part[v];
                Vector4 w;
                if (kind == Part.Foliage && pieceOfVertex[v] >= 0)
                {
                    Vector3 anchor = anchors[pieceOfVertex[v]];
                    w = TreeWeights(field, p, kind, Vector3.Distance(p, anchor), anchor);
                }
                else
                {
                    w = TreeWeights(field, p, kind == Part.Foliage ? Part.Wood : kind, 0f, p);
                }
                colours[v] = Quantise(w);
            }
            return colours;
        }

        /// <summary>R, G, B, A of spec 4.3 before quantising; for tests. For foliage, A is taken at the anchor of its piece.</summary>
        public static Vector4 TreeWeights(TreeField field, Vector3 position, Part part, float anchorDistance, Vector3 anchor)
        {
            float h = field.height;
            float r = 0f;
            if (position.y > 0f)
            {
                float x = Mathf.Clamp01(position.y / h);
                float s = x * x * (6f - 4f * x + x * x) / 3f;
                r = Mathf.Clamp01(s * h / field.classHeight);
            }
            float g = 0f;
            if (part != Part.Skirt)
            {
                float rho = Horizontal(position, field.CentreAt(position.y));
                float reach = Mathf.Clamp01((rho - field.RadiusAt(position.y)) / BranchReach);
                g = reach * Mathf.Sqrt(reach);   // ^1.5; 0 within the trunk radius
                // wind-4: 0 in the root zone (roots and flare neither bob nor take pushes), rising over RootRamp metres above it.
                if (!float.IsNegativeInfinity(field.rootTop))
                    g *= (float)SmoothStep01(Mathf.Clamp01((position.y - field.rootTop) / RootRamp));
            }
            float b = part == Part.Foliage ? Mathf.Clamp01(anchorDistance / FlutterReach) : 0f;
            float a = Phase(field, part == Part.Foliage ? anchor : position);
            return new Vector4(r, g, b, a);
        }

        /// <summary>
        /// Value noise on the cylinder around the trunk's centreline (spec 4.3, A): lattice i = floor(y / phaseCell), j = sector of 8,
        /// wrapping around the trunk, V(i, j) = PhaseRange x Hash01(i, j, seed), smoothstep weights in both directions, A = frac of
        /// the result. The spec interpolates unit phasors; their sum passes through zero between antipodal corners, where the angle
        /// jumps by up to half a cycle (measured 0.44 cycle between points 0.5 degree apart, and 0.498 between matched levels). A
        /// scalar noise read modulo 1 is a continuous phase everywhere (issues/W1-shader.md). wind-4: values over half a cycle and
        /// cells twice as tall (0.70 m young, 1.20 m tall) bound the slope at 1.5 x 0.5 / cell, 1.07 and 0.63 cycle per metre
        /// vertically (wind-3: up to 4.3 and 2.5), so needles a few centimetres apart, which a LOD switch trades for each other,
        /// bob nearly alike (WindMotion_LodSwitchDoesNotPop).
        /// </summary>
        static float Phase(TreeField field, Vector3 p)
        {
            Vector2 c = field.CentreAt(p.y);
            float theta = Mathf.Atan2(p.z - c.y, p.x - c.x);
            double fy = p.y / field.phaseCell;
            double ft = PhaseSectors * (theta / (2.0 * Math.PI));
            long i = (long)Math.Floor(fy), j = (long)Math.Floor(ft);
            double ty = SmoothStep01(fy - i), tt = SmoothStep01(ft - j);
            double value = 0.0, axial = 0.0;
            for (int di = 0; di < 2; di++)
            {
                double rowWeight = di == 0 ? 1.0 - ty : ty;
                for (int dj = 0; dj < 2; dj++)
                {
                    long jj = ((j + dj) % PhaseSectors + PhaseSectors) % PhaseSectors;
                    value += rowWeight * (dj == 0 ? 1.0 - tt : tt) * PhaseRange * Hash01(i + di, jj, 0, field.seed);
                }
                // The azimuth is undefined on the axis: within the trunk and up to AxialBand outside it, the phase blends to the
                // mean of the row's sectors, so a point that moves a centimetre near the trunk does not change its phase by half a cycle.
                for (int k = 0; k < PhaseSectors; k++)
                    axial += rowWeight * PhaseRange * Hash01(i + di, k, 0, field.seed) / PhaseSectors;
            }
            float r = field.RadiusAt(p.y);
            double outward = SmoothStep01(Mathf.Clamp01((Horizontal(p, c) - r) / AxialBand));
            value = axial + (value - axial) * outward;
            return (float)(value - Math.Floor(value));
        }

        /// <summary>The phase of a fern or grass clump in a slot (spec 4.3): 0.25 Hash01(slot, seed). Fronds and blades lie within 0.12 cycle of it.</summary>
        public static float ClumpPhase(int slot, int seed) => (float)(0.25 * Hash01(slot, 0, 0, seed));

        // ------------------------------------------------------------------------------------------------
        // Ferns and grass
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Colours of one fern or grass clump in clump space, aligned with <paramref name="positions"/> (spec 4.3): per frond or blade
        /// piece, R = 0; ferns G = (d / reach)^1.5 and B = d / reach from the frond's base (its vertex nearest the clump centre),
        /// grass G = ((y - base) / height)^2 and B = sqrt(G); A one phase per clump (slot) plus or minus 0.12 cycle per frond or blade.
        /// </summary>
        public static Color32[] ClumpColours(Vector3[] positions, int[] triangles, int slot, bool fern, int seed)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            if (triangles == null) throw new ArgumentNullException(nameof(triangles));
            int n = positions.Length;
            int[] pieceOfTriangle = ConnectedPieces(positions, triangles, out int count);
            var pieceOfVertex = new int[n];
            for (int v = 0; v < n; v++) pieceOfVertex[v] = -1;
            for (int t = 0; t < triangles.Length / 3; t++)
                for (int c = 0; c < 3; c++)
                {
                    int v = triangles[t * 3 + c];
                    if (pieceOfVertex[v] < 0) pieceOfVertex[v] = pieceOfTriangle[t];
                }

            var baseVertex = new int[count];
            var baseDistance = new float[count];
            var minY = new float[count];
            var maxY = new float[count];
            for (int p = 0; p < count; p++)
            {
                baseVertex[p] = -1;
                baseDistance[p] = float.PositiveInfinity;
                minY[p] = float.PositiveInfinity;
                maxY[p] = float.NegativeInfinity;
            }
            for (int v = 0; v < n; v++)
            {
                int p = pieceOfVertex[v];
                if (p < 0) continue;
                Vector3 q = positions[v];
                float d = fern ? q.magnitude : q.y;   // ferns: nearest the clump centre; grass: the lowest point is the base
                if (d < baseDistance[p])
                {
                    baseDistance[p] = d;
                    baseVertex[p] = v;
                }
                minY[p] = Mathf.Min(minY[p], q.y);
                maxY[p] = Mathf.Max(maxY[p], q.y);
            }
            var reach = new float[count];
            for (int v = 0; v < n; v++)
            {
                int p = pieceOfVertex[v];
                if (p < 0 || baseVertex[p] < 0) continue;
                reach[p] = Mathf.Max(reach[p], Vector3.Distance(positions[v], positions[baseVertex[p]]));
            }

            // Fern fronds are ranked by the azimuth of their base, so the hash does not depend on the mesh's piece order.
            var rank = new int[count];
            if (fern)
            {
                var order = new int[count];
                var azimuth = new float[count];
                for (int p = 0; p < count; p++)
                {
                    order[p] = p;
                    Vector3 b = baseVertex[p] >= 0 ? positions[baseVertex[p]] : Vector3.zero;
                    azimuth[p] = Mathf.Atan2(b.z, b.x);
                }
                Array.Sort(order, (x, y) =>
                {
                    int byAngle = azimuth[x].CompareTo(azimuth[y]);
                    return byAngle != 0 ? byAngle : x.CompareTo(y);
                });
                for (int k = 0; k < count; k++) rank[order[k]] = k;
            }

            double clumpPhase = ClumpPhase(slot, seed);
            var colours = new Color32[n];
            for (int v = 0; v < n; v++)
            {
                int p = pieceOfVertex[v];
                if (p < 0 || baseVertex[p] < 0)
                {
                    colours[v] = new Color32(0, 0, 0, Quantise01((float)clumpPhase));
                    continue;
                }
                float g, b;
                double offset;
                if (fern)
                {
                    float d = Vector3.Distance(positions[v], positions[baseVertex[p]]);
                    float f = reach[p] > 1e-6f ? Mathf.Clamp01(d / reach[p]) : 0f;
                    g = f * Mathf.Sqrt(f);
                    b = f;
                    offset = Hash01(slot, rank[p] + 1, 1, seed);
                }
                else
                {
                    float height = maxY[p] - minY[p];
                    float f = height > 1e-6f ? Mathf.Clamp01((positions[v].y - minY[p]) / height) : 0f;
                    g = f * f;
                    b = Mathf.Sqrt(g);
                    Vector3 bladeBase = positions[baseVertex[p]];
                    offset = Hash01(slot, (long)Math.Round(bladeBase.x * 100.0) * 100003L + (long)Math.Round(bladeBase.z * 100.0), 2, seed);
                }
                double a = clumpPhase + 0.12 * (2.0 * offset - 1.0);
                a -= Math.Floor(a);
                colours[v] = Quantise(new Vector4(0f, g, b, (float)a));
            }
            return colours;
        }

        /// <summary>
        /// W1 R2 (review of the R1 captures): the anchor of every vertex of one fern or grass clump, in clump space, aligned with
        /// <paramref name="positions"/>: xyz = the base of its frond or blade minus the vertex (the frond's vertex nearest the clump
        /// centre, as for its colours; a blade's lowest vertex), w = the frond's or blade's azimuth (radians, atan2(z, x) of the horizontal
        /// direction from its base to its farthest vertex; the base's own azimuth from the clump centre when that is under 1 cm). The
        /// vertex shader turns each frond rigidly about its base by the press there, so a frond neither stretches nor splits across a
        /// body's axis. Vertices in no piece get 0.
        /// </summary>
        public static Vector4[] ClumpAnchors(Vector3[] positions, int[] triangles, bool fern)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            if (triangles == null) throw new ArgumentNullException(nameof(triangles));
            int n = positions.Length;
            int[] pieceOfTriangle = ConnectedPieces(positions, triangles, out int count);
            var pieceOfVertex = new int[n];
            for (int v = 0; v < n; v++) pieceOfVertex[v] = -1;
            for (int t = 0; t < triangles.Length / 3; t++)
                for (int c = 0; c < 3; c++)
                {
                    int v = triangles[t * 3 + c];
                    if (pieceOfVertex[v] < 0) pieceOfVertex[v] = pieceOfTriangle[t];
                }
            var baseVertex = new int[count];
            var baseDistance = new float[count];
            for (int p = 0; p < count; p++)
            {
                baseVertex[p] = -1;
                baseDistance[p] = float.PositiveInfinity;
            }
            for (int v = 0; v < n; v++)
            {
                int p = pieceOfVertex[v];
                if (p < 0) continue;
                float d = fern ? positions[v].magnitude : positions[v].y;
                if (d < baseDistance[p]) { baseDistance[p] = d; baseVertex[p] = v; }
            }
            var tip = new int[count];
            var tipDistance = new float[count];
            for (int p = 0; p < count; p++) tip[p] = -1;
            for (int v = 0; v < n; v++)
            {
                int p = pieceOfVertex[v];
                if (p < 0 || baseVertex[p] < 0) continue;
                float d = Vector3.Distance(positions[v], positions[baseVertex[p]]);
                if (tip[p] < 0 || d > tipDistance[p]) { tipDistance[p] = d; tip[p] = v; }
            }
            var azimuth = new float[count];
            for (int p = 0; p < count; p++)
            {
                if (baseVertex[p] < 0) continue;
                Vector3 b = positions[baseVertex[p]];
                Vector3 toTip = tip[p] >= 0 ? positions[tip[p]] - b : Vector3.zero;
                var horizontal = new Vector2(toTip.x, toTip.z);
                if (horizontal.magnitude < 0.01f) horizontal = new Vector2(b.x, b.z);
                azimuth[p] = horizontal.magnitude < 1e-6f ? 0f : Mathf.Atan2(horizontal.y, horizontal.x);
            }
            var anchors = new Vector4[n];
            for (int v = 0; v < n; v++)
            {
                int p = pieceOfVertex[v];
                if (p < 0 || baseVertex[p] < 0) continue;
                Vector3 offset = positions[baseVertex[p]] - positions[v];
                anchors[v] = new Vector4(offset.x, offset.y, offset.z, azimuth[p]);
            }
            return anchors;
        }

        /// <summary>
        /// W1 R2: anchors of a clump moved into its patch by <paramref name="slot"/> (rotation and uniform scale; a translation does not
        /// change an offset): the offsets are rotated and scaled, the azimuths rotated.
        /// </summary>
        public static Vector4[] TransformAnchors(Vector4[] anchors, Matrix4x4 slot)
        {
            var result = new Vector4[anchors.Length];
            for (int v = 0; v < anchors.Length; v++)
            {
                Vector4 a = anchors[v];
                Vector3 offset = slot.MultiplyVector(new Vector3(a.x, a.y, a.z));
                Vector3 direction = slot.MultiplyVector(new Vector3(Mathf.Cos(a.w), 0f, Mathf.Sin(a.w)));
                result[v] = new Vector4(offset.x, offset.y, offset.z, Mathf.Atan2(direction.z, direction.x));
            }
            return result;
        }

        // ------------------------------------------------------------------------------------------------
        // The compact layout (spec 4.1)
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Position Float32 x 3 | Normal Float32 x 3 | Tangent Float16 x 4 | Color UNorm8 x 4 | TexCoord0 Float32 x 2: 44 B. W1 R2: fern and
        /// grass meshes add TexCoord2 Float16 x 4 (their anchors, <see cref="ClumpAnchors"/>): 52 B.
        /// </summary>
        public static readonly VertexAttributeDescriptor[] CompactLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float16, 4),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float16, 4),
        };

        /// <summary>
        /// Rewrites the mesh's vertex buffer in the compact layout, in place, after Optimize: the same vertex order, sub-meshes,
        /// index format and bounds. Attributes the mesh lacks are left out of the layout (a mesh without UVs keeps none). Returns
        /// the same mesh.
        /// </summary>
        public static Mesh Compact(Mesh mesh)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            int n = mesh.vertexCount;
            foreach (VertexAttributeDescriptor attribute in mesh.GetVertexAttributes())
            {
                VertexAttribute kind = attribute.attribute;
                if (kind != VertexAttribute.Position && kind != VertexAttribute.Normal && kind != VertexAttribute.Tangent
                    && kind != VertexAttribute.Color && kind != VertexAttribute.TexCoord0 && kind != VertexAttribute.TexCoord2)
                    throw new InvalidOperationException($"{mesh.name} has a {kind} attribute, which the compact wind layout does not carry.");
            }
            Vector3[] positions = mesh.vertices;
            Vector3[] normals = mesh.HasVertexAttribute(VertexAttribute.Normal) ? mesh.normals : null;
            Vector4[] tangents = mesh.HasVertexAttribute(VertexAttribute.Tangent) ? mesh.tangents : null;
            Color32[] colours = mesh.HasVertexAttribute(VertexAttribute.Color) ? mesh.colors32 : null;
            Vector2[] uv = mesh.HasVertexAttribute(VertexAttribute.TexCoord0) ? mesh.uv : null;
            Vector4[] anchors = null;
            if (mesh.HasVertexAttribute(VertexAttribute.TexCoord2))
            {
                var list = new List<Vector4>(n);
                mesh.GetUVs(2, list);
                anchors = list.ToArray();
            }
            Bounds bounds = mesh.bounds;

            var layout = new List<VertexAttributeDescriptor> { CompactLayout[0] };
            if (normals != null) layout.Add(CompactLayout[1]);
            if (tangents != null) layout.Add(CompactLayout[2]);
            if (colours != null) layout.Add(CompactLayout[3]);
            if (uv != null) layout.Add(CompactLayout[4]);
            if (anchors != null) layout.Add(CompactLayout[5]);
            int stride = 12 + (normals != null ? 12 : 0) + (tangents != null ? 8 : 0) + (colours != null ? 4 : 0) + (uv != null ? 8 : 0) + (anchors != null ? 8 : 0);

            var data = new byte[n * stride];
            int o = 0;
            for (int v = 0; v < n; v++)
            {
                o = PutFloat(data, o, positions[v].x); o = PutFloat(data, o, positions[v].y); o = PutFloat(data, o, positions[v].z);
                if (normals != null) { o = PutFloat(data, o, normals[v].x); o = PutFloat(data, o, normals[v].y); o = PutFloat(data, o, normals[v].z); }
                if (tangents != null)
                {
                    o = PutHalf(data, o, tangents[v].x); o = PutHalf(data, o, tangents[v].y);
                    o = PutHalf(data, o, tangents[v].z); o = PutHalf(data, o, tangents[v].w);
                }
                if (colours != null) { data[o++] = colours[v].r; data[o++] = colours[v].g; data[o++] = colours[v].b; data[o++] = colours[v].a; }
                if (uv != null) { o = PutFloat(data, o, uv[v].x); o = PutFloat(data, o, uv[v].y); }
                if (anchors != null)
                {
                    o = PutHalf(data, o, anchors[v].x); o = PutHalf(data, o, anchors[v].y);
                    o = PutHalf(data, o, anchors[v].z); o = PutHalf(data, o, anchors[v].w);
                }
            }

            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            mesh.SetVertexBufferParams(n, layout.ToArray());
            mesh.SetVertexBufferData(data, 0, 0, data.Length, 0, flags);
            mesh.bounds = bounds;
            return mesh;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        struct FloatBits
        {
            [System.Runtime.InteropServices.FieldOffset(0)] public float value;
            [System.Runtime.InteropServices.FieldOffset(0)] public int bits;
        }

        static int PutFloat(byte[] data, int offset, float value)
        {
            int bits = new FloatBits { value = value }.bits;
            data[offset] = (byte)bits; data[offset + 1] = (byte)(bits >> 8); data[offset + 2] = (byte)(bits >> 16); data[offset + 3] = (byte)(bits >> 24);
            return offset + 4;
        }

        static int PutHalf(byte[] data, int offset, float value)
        {
            ushort bits = Mathf.FloatToHalf(value);
            data[offset] = (byte)bits; data[offset + 1] = (byte)(bits >> 8);
            return offset + 2;
        }

        // ------------------------------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------------------------------

        static Color32 Quantise(Vector4 w) => new Color32(Quantise01(w.x), Quantise01(w.y), Quantise01(w.z), Quantise01(w.w));

        static byte Quantise01(float value) => (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(value) * 255f), 0, 255);

        static float Horizontal(Vector3 p, Vector2 centre)
        {
            float dx = p.x - centre.x, dz = p.z - centre.y;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static double SmoothStep01(double t) => t * t * (3.0 - 2.0 * t);

        // The segment where a triangle crosses the plane y = level, in XZ. False when it does not cross.
        static bool Section(Vector3 a, Vector3 b, Vector3 c, float level, out Vector2 from, out Vector2 to)
        {
            from = to = Vector2.zero;
            var points = new Vector2[2];
            int found = 0;
            void Edge(Vector3 p, Vector3 q)
            {
                if (found >= 2) return;
                float dp = p.y - level, dq = q.y - level;
                if ((dp < 0f) == (dq < 0f)) return;
                float t = dp / (dp - dq);
                points[found++] = new Vector2(p.x + t * (q.x - p.x), p.z + t * (q.z - p.z));
            }
            Edge(a, b);
            Edge(b, c);
            Edge(c, a);
            if (found < 2) return false;
            from = points[0];
            to = points[1];
            return true;
        }

        /// <summary>
        /// Connected pieces of a triangle list: triangles that share a position (welded at 0.1 mm, as the thinner welds) are one piece.
        /// The same answer as ForestFoliageThinner's connectivity, without its spatial fallback for welded geometry (a trunk is one
        /// large piece by design). Piece ids follow the first triangle of each piece.
        /// </summary>
        internal static int[] ConnectedPieces(Vector3[] positions, int[] triangles, out int count)
        {
            int vertexCount = positions.Length;
            var key = new long[vertexCount];
            var order = new int[vertexCount];
            double inverse = 1.0 / Weld;
            for (int v = 0; v < vertexCount; v++)
            {
                Vector3 p = positions[v];
                long x = (long)Math.Floor(p.x * inverse) & 0x1FFFFF, y = (long)Math.Floor(p.y * inverse) & 0x1FFFFF, z = (long)Math.Floor(p.z * inverse) & 0x1FFFFF;
                key[v] = (x << 42) | (y << 21) | z;
                order[v] = v;
            }
            Array.Sort(key, order);
            var welded = new int[vertexCount];
            int weldedCount = 0;
            for (int i = 0; i < vertexCount; i++)
            {
                if (i > 0 && key[i] != key[i - 1]) weldedCount++;
                welded[order[i]] = weldedCount;
            }
            if (vertexCount > 0) weldedCount++;

            var parent = new int[weldedCount];
            for (int i = 0; i < weldedCount; i++) parent[i] = i;
            int triangleCount = triangles.Length / 3;
            for (int t = 0; t < triangleCount; t++)
            {
                int root = Root(parent, welded[triangles[t * 3]]);
                for (int corner = 1; corner < 3; corner++)
                {
                    int other = Root(parent, welded[triangles[t * 3 + corner]]);
                    if (other != root)
                    {
                        if (other < root) { parent[root] = other; root = other; }
                        else parent[other] = root;
                    }
                }
            }
            var pieceOfRoot = new int[weldedCount];
            for (int i = 0; i < weldedCount; i++) pieceOfRoot[i] = -1;
            var pieceOfTriangle = new int[triangleCount];
            count = 0;
            for (int t = 0; t < triangleCount; t++)
            {
                int root = Root(parent, welded[triangles[t * 3]]);
                if (pieceOfRoot[root] < 0) pieceOfRoot[root] = count++;
                pieceOfTriangle[t] = pieceOfRoot[root];
            }
            return pieceOfTriangle;
        }

        static int Root(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        // A number in (0, 1) from three coordinates and the seed: the thinner's splitmix64 finaliser, chained.
        internal static double Hash01(long x, long y, long z, int seed)
        {
            unchecked
            {
                ulong hash = Mix((ulong)seed + 0x9E3779B97F4A7C15UL);
                hash = Mix(hash ^ (ulong)x);
                hash = Mix(hash ^ (ulong)y);
                hash = Mix(hash ^ (ulong)z);
                return ((hash >> 11) + 0.5) * (1.0 / 9007199254740992.0);
            }
        }

        static ulong Mix(ulong value)
        {
            unchecked
            {
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                return value ^ (value >> 31);
            }
        }

        /// <summary>FNV-1a of a prefab or patch id, as ForestPrefabs.StableHash.</summary>
        internal static int StableHash(string text)
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
