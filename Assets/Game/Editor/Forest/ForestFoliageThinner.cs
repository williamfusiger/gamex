using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gamex.EditorTools
{
    /// <summary>
    /// Makes the lower levels of a tree's foliage (spec section 5.6.3). A scanned crown is hundreds of
    /// thousands of small separate pieces; a level keeps some of them whole and enlarges the survivors,
    /// so the crown stays as full with a fraction of the triangles. Plain array maths with no Unity
    /// objects: every result depends only on the arrays and the seed.
    /// </summary>
    public static class ForestFoliageThinner
    {
        public sealed class Pieces
        {
            public int count; public int[] pieceOfTriangle; public float[] area; public Vector3[] centroid;
            public Vector3[] anchor; public float[] key; public float totalArea; public bool usedSpatialFallback;

            // The trunk axis the anchors were measured from. Thin keeps enlarged pieces inside the
            // crown's own outline around this axis.
            internal Vector3 trunkAxisOrigin;
        }

        const float MaxScaleLimit = 1000f;

        /// <summary>What one call of Thin did, for the kit report.</summary>
        internal struct ThinStats
        {
            public int piecesKept, piecesFlattened, sourceTrianglesKept, sourceTriangles;
            public float scale;            // the common enlargement; a piece at the crown's edge gets less
            public bool scaleCapReached;   // the kept pieces could not be made large enough to keep the leaf area
        }

        /// <summary>
        /// Splits foliage into pieces (triangles that touch, whatever their UV seams say) and gives each a
        /// priority key. The key is the logarithm of the spec's hash01 ^ (1 / w), w = area * (0.35 + 0.65 *
        /// outerness): the same order, without the underflow a float has at 1 / w near a thousand. Thin
        /// keeps pieces from the largest key down, so the sets of two levels are nested. The key does not
        /// depend on the level, and the pieces that hold the crown's six extreme points come first, so
        /// every level spans the same box.
        /// </summary>
        public static Pieces FindPieces(Vector3[] positions, int[] triangles, Vector3 trunkAxisOrigin, int seed)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            if (triangles == null) throw new ArgumentNullException(nameof(triangles));
            if (triangles.Length % 3 != 0) throw new ArgumentException("The index list is not a triangle list.", nameof(triangles));
            int triangleCount = triangles.Length / 3;

            int[] pieceOfTriangle = ConnectedPieces(positions, triangles, out int count);
            bool spatial = false;
            // Welded branches instead of separate twigs cannot be thinned piece by piece: cut them into cells.
            if (triangleCount >= ForestKitSpec.PieceGuardMinTriangles && !PiecesAreSmall(pieceOfTriangle, count))
            {
                pieceOfTriangle = SpatialPieces(positions, triangles, out count);
                spatial = true;
            }

            var pieces = new Pieces
            {
                count = count,
                pieceOfTriangle = pieceOfTriangle,
                area = new float[count],
                centroid = new Vector3[count],
                anchor = new Vector3[count],
                key = new float[count],
                usedSpatialFallback = spatial,
                trunkAxisOrigin = trunkAxisOrigin,
            };
            if (count == 0) return pieces;

            var areaSum = new double[count];
            var weighted = new double[count * 3];     // area-weighted centroid sums
            var plain = new double[count * 3];        // unweighted, for pieces without area
            var triangleTally = new int[count];
            var anchorDistance = new double[count];
            for (int p = 0; p < count; p++) anchorDistance[p] = double.PositiveInfinity;

            // The pieces that carry the lowest and highest point on each axis.
            var extremeValue = new float[6];
            var extremePiece = new int[6];
            for (int e = 0; e < 6; e++)
            {
                extremeValue[e] = e % 2 == 0 ? float.PositiveInfinity : float.NegativeInfinity;
                extremePiece[e] = -1;
            }

            for (int t = 0; t < triangleCount; t++)
            {
                int piece = pieceOfTriangle[t];
                Vector3 p0 = positions[triangles[t * 3]], p1 = positions[triangles[t * 3 + 1]], p2 = positions[triangles[t * 3 + 2]];
                double area = 0.5 * Vector3.Cross(p1 - p0, p2 - p0).magnitude;
                double cx = (p0.x + p1.x + p2.x) / 3.0, cy = (p0.y + p1.y + p2.y) / 3.0, cz = (p0.z + p1.z + p2.z) / 3.0;
                areaSum[piece] += area;
                weighted[piece * 3] += area * cx;
                weighted[piece * 3 + 1] += area * cy;
                weighted[piece * 3 + 2] += area * cz;
                plain[piece * 3] += cx;
                plain[piece * 3 + 1] += cy;
                plain[piece * 3 + 2] += cz;
                triangleTally[piece]++;

                for (int corner = 0; corner < 3; corner++)
                {
                    Vector3 point = corner == 0 ? p0 : corner == 1 ? p1 : p2;
                    double dx = point.x - trunkAxisOrigin.x, dz = point.z - trunkAxisOrigin.z;
                    double distance = dx * dx + dz * dz;
                    if (distance < anchorDistance[piece])
                    {
                        anchorDistance[piece] = distance;
                        pieces.anchor[piece] = point;
                    }
                    for (int axis = 0; axis < 3; axis++)
                    {
                        float value = point[axis];
                        if (value < extremeValue[axis * 2]) { extremeValue[axis * 2] = value; extremePiece[axis * 2] = piece; }
                        if (value > extremeValue[axis * 2 + 1]) { extremeValue[axis * 2 + 1] = value; extremePiece[axis * 2 + 1] = piece; }
                    }
                }
            }

            double total = 0.0;
            int lowestBand = int.MaxValue, highestBand = int.MinValue;
            var band = new int[count];
            var distanceFromAxis = new float[count];
            for (int p = 0; p < count; p++)
            {
                total += areaSum[p];
                pieces.area[p] = (float)areaSum[p];
                pieces.centroid[p] = areaSum[p] > 0.0
                    ? new Vector3((float)(weighted[p * 3] / areaSum[p]), (float)(weighted[p * 3 + 1] / areaSum[p]), (float)(weighted[p * 3 + 2] / areaSum[p]))
                    : new Vector3((float)(plain[p * 3] / triangleTally[p]), (float)(plain[p * 3 + 1] / triangleTally[p]), (float)(plain[p * 3 + 2] / triangleTally[p]));

                Vector3 centre = pieces.centroid[p];
                float dx = centre.x - trunkAxisOrigin.x, dz = centre.z - trunkAxisOrigin.z;
                distanceFromAxis[p] = Mathf.Sqrt(dx * dx + dz * dz);
                band[p] = Mathf.FloorToInt((centre.y - trunkAxisOrigin.y) / ForestKitSpec.BandHeight);
                lowestBand = Math.Min(lowestBand, band[p]);
                highestBand = Math.Max(highestBand, band[p]);
            }
            pieces.totalArea = (float)total;

            // Outerness: how far out a piece sits compared with the widest piece at its height.
            var widest = new float[highestBand - lowestBand + 1];
            for (int p = 0; p < count; p++)
                widest[band[p] - lowestBand] = Mathf.Max(widest[band[p] - lowestBand], distanceFromAxis[p]);

            double quantum = ForestKitSpec.ThinByGroup ? ForestKitSpec.GroupCell : ForestKitSpec.KeyCentroidQuantum;
            for (int p = 0; p < count; p++)
            {
                float reach = widest[band[p] - lowestBand];
                float outerness = reach > 0f ? distanceFromAxis[p] / reach : 1f;
                double weight = areaSum[p] * (ForestKitSpec.KeyInnerWeight + (1.0 - ForestKitSpec.KeyInnerWeight) * outerness);

                // The hash comes from where the piece is, not from its number, so a re-import that orders
                // pieces differently keeps the same crown. With ThinByGroup the hash is that of a 0.25 m
                // cell, and neighbours live or die together.
                Vector3 centre = pieces.centroid[p];
                double chance = Hash01(
                    (long)Math.Floor(centre.x / quantum), (long)Math.Floor(centre.y / quantum), (long)Math.Floor(centre.z / quantum), seed);
                pieces.key[p] = weight > 0.0 ? (float)(Math.Log(chance) / weight) : float.NegativeInfinity;
            }
            foreach (int piece in extremePiece)
                if (piece >= 0) pieces.key[piece] = float.PositiveInfinity;

            return pieces;
        }

        /// <summary>
        /// One level of the foliage: the pieces with the largest keys, as many as the triangle target
        /// allows, each scaled about its anchor so that the leaf area stays what it was (up to maxScale).
        /// A piece stops growing where it would leave the crown's own outline, and the others grow a little
        /// more to make up for it. With flattenToQuads a kept piece becomes one quad where a plane fits it.
        /// </summary>
        public static ForestMeshTools.MeshData Thin(ForestMeshTools.MeshData foliage, Pieces pieces,
            int targetTriangles, float maxScale, bool flattenToQuads)
        {
            return Thin(foliage, pieces, targetTriangles, maxScale, flattenToQuads, out _);
        }

        /// <summary>
        /// A closed hull that fills the inside of a crown: heightBands rings of `sectors` points around the
        /// trunk axis plus a point at the top and one at the bottom (2 * heightBands * sectors triangles).
        /// Each ring point sits at radiusFraction of the 70th percentile distance of the foliage in its cell.
        /// </summary>
        public static ForestMeshTools.MeshData BuildCrownCore(ForestMeshTools.MeshData foliage, Vector3 trunkAxisOrigin,
            int heightBands, int sectors, float radiusFraction)
        {
            if (foliage == null) throw new ArgumentNullException(nameof(foliage));
            if (heightBands < 1) throw new ArgumentOutOfRangeException(nameof(heightBands));
            if (sectors < 3) throw new ArgumentOutOfRangeException(nameof(sectors));

            int[] triangles = JoinSubMeshes(foliage.subMeshes);
            if (triangles.Length == 0) return ForestMeshTools.Empty(1, true);

            float lowest = float.PositiveInfinity, highest = float.NegativeInfinity;
            foreach (int vertex in triangles)
            {
                lowest = Mathf.Min(lowest, foliage.positions[vertex].y);
                highest = Mathf.Max(highest, foliage.positions[vertex].y);
            }
            float bandHeight = Mathf.Max((highest - lowest) / heightBands, 1e-6f);

            // Distances per cell, bucketed with a counting sort so five million vertices need two arrays.
            int cells = heightBands * sectors;
            var seen = new bool[foliage.positions.Length];
            var start = new int[cells + 1];
            foreach (int vertex in triangles)
            {
                if (seen[vertex]) continue;
                seen[vertex] = true;
                start[CoreCell(foliage.positions[vertex], trunkAxisOrigin, lowest, bandHeight, heightBands, sectors) + 1]++;
            }
            for (int cell = 0; cell < cells; cell++) start[cell + 1] += start[cell];
            var distances = new float[start[cells]];
            var cursor = new int[cells];
            Array.Copy(start, cursor, cells);
            foreach (int vertex in triangles)
            {
                if (!seen[vertex]) continue;
                seen[vertex] = false;
                Vector3 point = foliage.positions[vertex];
                float dx = point.x - trunkAxisOrigin.x, dz = point.z - trunkAxisOrigin.z;
                distances[cursor[CoreCell(point, trunkAxisOrigin, lowest, bandHeight, heightBands, sectors)]++] = Mathf.Sqrt(dx * dx + dz * dz);
            }

            int ringPoints = heightBands * sectors;
            var core = new ForestMeshTools.MeshData
            {
                positions = new Vector3[ringPoints + 2],
                uv0 = new Vector2[ringPoints + 2],   // the core material has no texture
                subMeshes = new int[1][],
            };
            for (int b = 0; b < heightBands; b++)
            {
                for (int s = 0; s < sectors; s++)
                {
                    int cell = b * sectors + s;
                    float radius = 0f;
                    int members = start[cell + 1] - start[cell];
                    if (members > 0)
                    {
                        Array.Sort(distances, start[cell], members);
                        int rank = Mathf.Clamp(Mathf.CeilToInt(ForestKitSpec.CrownCorePercentile * members) - 1, 0, members - 1);
                        radius = distances[start[cell] + rank] * radiusFraction;
                    }
                    float angle = -Mathf.PI + (s + 0.5f) * (2f * Mathf.PI / sectors);
                    core.positions[cell] = new Vector3(
                        trunkAxisOrigin.x + radius * Mathf.Cos(angle), lowest + (b + 0.5f) * bandHeight, trunkAxisOrigin.z + radius * Mathf.Sin(angle));
                }
            }
            int bottom = ringPoints, top = ringPoints + 1;
            core.positions[bottom] = new Vector3(trunkAxisOrigin.x, lowest, trunkAxisOrigin.z);
            core.positions[top] = new Vector3(trunkAxisOrigin.x, highest, trunkAxisOrigin.z);

            var indices = new List<int>(6 * heightBands * sectors);
            for (int s = 0; s < sectors; s++)
            {
                int next = (s + 1) % sectors;
                AddFacing(core.positions, indices, bottom, s, next, trunkAxisOrigin, -1);
                for (int b = 0; b + 1 < heightBands; b++)
                {
                    int lower = b * sectors, upper = (b + 1) * sectors;
                    AddFacing(core.positions, indices, lower + s, upper + s, upper + next, trunkAxisOrigin, 0);
                    AddFacing(core.positions, indices, lower + s, upper + next, lower + next, trunkAxisOrigin, 0);
                }
                int last = (heightBands - 1) * sectors;
                AddFacing(core.positions, indices, top, last + s, last + next, trunkAxisOrigin, 1);
            }
            core.subMeshes[0] = indices.ToArray();
            core.normals = ForestMeshTools.ComputeNormals(core);
            return core;
        }

        // ---- For the tree baker ----

        internal static ForestMeshTools.MeshData Thin(ForestMeshTools.MeshData foliage, Pieces pieces,
            int targetTriangles, float maxScale, bool flattenToQuads, out ThinStats stats)
        {
            stats = default;
            if (foliage == null) throw new ArgumentNullException(nameof(foliage));
            if (pieces == null) throw new ArgumentNullException(nameof(pieces));

            int[] triangles = JoinSubMeshes(foliage.subMeshes);
            int triangleCount = triangles.Length / 3;
            if (pieces.pieceOfTriangle == null || pieces.pieceOfTriangle.Length != triangleCount)
                throw new ArgumentException("The pieces were not found on this mesh: the triangle counts differ.", nameof(pieces));
            if (pieces.area == null || pieces.anchor == null || pieces.key == null || pieces.area.Length < pieces.count
                || pieces.anchor.Length < pieces.count || pieces.key.Length < pieces.count)
                throw new ArgumentException("The pieces are incomplete.", nameof(pieces));

            stats.sourceTriangles = triangleCount;
            stats.scale = 1f;
            bool hasUv = foliage.uv0 != null;
            if (pieces.count == 0 || targetTriangles <= 0) return ForestMeshTools.Empty(1, hasUv);

            // Below 1 the compensation would shrink what is kept; an unbounded cap would turn the area sum
            // of a zero-area piece into 0 times infinity.
            maxScale = float.IsNaN(maxScale) ? 1f : Mathf.Clamp(maxScale, 1f, MaxScaleLimit);
            Vector3[] positions = foliage.positions;
            Vector3[] normals = foliage.normals ?? ForestMeshTools.ComputeNormals(foliage);

            // Triangles grouped by piece, so a piece can be handled as a unit.
            var start = new int[pieces.count + 1];
            for (int t = 0; t < triangleCount; t++) start[pieces.pieceOfTriangle[t] + 1]++;
            for (int p = 0; p < pieces.count; p++) start[p + 1] += start[p];
            var trianglesOfPiece = new int[triangleCount];
            var cursor = new int[pieces.count];
            Array.Copy(start, cursor, pieces.count);
            for (int t = 0; t < triangleCount; t++) trianglesOfPiece[cursor[pieces.pieceOfTriangle[t]]++] = t;

            // Largest key first. The piece number breaks ties, so the order is total and repeatable.
            var order = new int[pieces.count];
            for (int p = 0; p < order.Length; p++) order[p] = p;
            float[] key = pieces.key;
            Array.Sort(order, (left, right) =>
            {
                int byKey = key[right].CompareTo(key[left]);
                return byKey != 0 ? byKey : left.CompareTo(right);
            });

            // Keep a prefix of that order: only a prefix makes the set of a far level a subset of a nearer one.
            var scratch = new Scratch(positions.Length);
            var slotOfPiece = new int[pieces.count];
            for (int p = 0; p < slotOfPiece.Length; p++) slotOfPiece[p] = -1;
            var keptPieces = new List<int>();
            var keptQuads = new List<Quad>();
            int cost = 0;
            foreach (int piece in order)
            {
                int pieceTriangles = start[piece + 1] - start[piece];
                if (pieceTriangles == 0) continue;

                Quad quad = default;
                int pieceCost = pieceTriangles;
                if (flattenToQuads && hasUv && TryFitQuad(foliage, normals, triangles, trianglesOfPiece, start[piece], start[piece + 1], scratch, out quad))
                    pieceCost = 2;

                // The piece that crosses the target is taken only if that ends nearer to the target.
                bool crosses = cost + pieceCost > targetTriangles;
                if (crosses && cost + pieceCost - targetTriangles >= targetTriangles - cost) break;

                slotOfPiece[piece] = keptPieces.Count;
                keptPieces.Add(piece);
                keptQuads.Add(quad);
                cost += pieceCost;
                stats.sourceTrianglesKept += pieceTriangles;
                if (quad.valid) stats.piecesFlattened++;
                if (crosses) break;
            }
            stats.piecesKept = keptPieces.Count;
            if (keptPieces.Count == 0) return ForestMeshTools.Empty(1, hasUv);

            // Stems (closed tubes) carry no leaf area. Enlarged with the needles they grew into straight dark wires
            // 0.5 to 1.5 m long that stuck out of the crowns of the far levels against the sky (review 1 of the M3
            // captures). They keep about their own size, and the leaf area the needles make up for is the needles'
            // own: the area balance counts cards only.
            bool[] tube = TubePieces(normals, triangles, trianglesOfPiece, start, pieces.count, scratch);
            double cardArea = 0.0;
            for (int p = 0; p < pieces.count; p++)
                if (!tube[p]) cardArea += pieces.area[p];
            if (!(cardArea > 0.0)) cardArea = pieces.totalArea;

            // How far each kept piece may grow before it leaves the crown's outline.
            Envelope envelope = Envelope.Build(positions, triangles, pieces.trunkAxisOrigin);
            var limit = new float[keptPieces.Count];
            var keptArea = new double[keptPieces.Count];
            for (int slot = 0; slot < limit.Length; slot++)
            {
                int piece = keptPieces[slot];
                keptArea[slot] = tube[piece] ? 0.0 : pieces.area[piece];
                scratch.points.Clear();
                Quad quad = keptQuads[slot];
                if (quad.valid)
                {
                    scratch.points.Add(quad.p0);
                    scratch.points.Add(quad.p1);
                    scratch.points.Add(quad.p2);
                    scratch.points.Add(quad.p3);
                }
                else
                {
                    scratch.visit++;
                    for (int i = start[piece]; i < start[piece + 1]; i++)
                    {
                        int t = trianglesOfPiece[i] * 3;
                        for (int corner = 0; corner < 3; corner++)
                        {
                            int vertex = triangles[t + corner];
                            if (scratch.stamp[vertex] == scratch.visit) continue;
                            scratch.stamp[vertex] = scratch.visit;
                            scratch.points.Add(positions[vertex]);
                        }
                    }
                }
                limit[slot] = envelope.MaxScale(scratch.points, pieces.anchor[piece], tube[piece] ? Mathf.Min(maxScale, ForestKitSpec.StickMaxScale) : maxScale);
            }

            float scale = SolveScale(keptArea, limit, cardArea, maxScale, out stats.scaleCapReached);
            stats.scale = scale;

            // Write the level in the source's piece order, which keeps neighbours together in the buffers.
            var outPositions = new List<Vector3>();
            var outNormals = new List<Vector3>();
            var outUv = hasUv ? new List<Vector2>() : null;
            var outIndices = new List<int>(cost * 3);
            for (int piece = 0; piece < pieces.count; piece++)
            {
                int slot = slotOfPiece[piece];
                if (slot < 0) continue;

                float pieceScale = Mathf.Min(scale, limit[slot]);
                Vector3 anchor = pieces.anchor[piece];
                Quad quad = keptQuads[slot];
                if (quad.valid)
                {
                    int first = outPositions.Count;
                    // A fitted corner can lie a little outside the piece it replaces; it must not widen the crown.
                    outPositions.Add(envelope.ClampToBox(anchor + (quad.p0 - anchor) * pieceScale));
                    outPositions.Add(envelope.ClampToBox(anchor + (quad.p1 - anchor) * pieceScale));
                    outPositions.Add(envelope.ClampToBox(anchor + (quad.p2 - anchor) * pieceScale));
                    outPositions.Add(envelope.ClampToBox(anchor + (quad.p3 - anchor) * pieceScale));
                    outNormals.Add(quad.n0);
                    outNormals.Add(quad.n1);
                    outNormals.Add(quad.n2);
                    outNormals.Add(quad.n3);
                    outUv.Add(new Vector2(quad.uvMin.x, quad.uvMin.y));
                    outUv.Add(new Vector2(quad.uvMax.x, quad.uvMin.y));
                    outUv.Add(new Vector2(quad.uvMax.x, quad.uvMax.y));
                    outUv.Add(new Vector2(quad.uvMin.x, quad.uvMax.y));
                    int second = quad.reversed ? 2 : 1, third = quad.reversed ? 1 : 2;
                    int fourth = quad.reversed ? 3 : 2, fifth = quad.reversed ? 2 : 3;
                    outIndices.Add(first);
                    outIndices.Add(first + second);
                    outIndices.Add(first + third);
                    outIndices.Add(first);
                    outIndices.Add(first + fourth);
                    outIndices.Add(first + fifth);
                    continue;
                }

                scratch.visit++;
                for (int i = start[piece]; i < start[piece + 1]; i++)
                {
                    int t = trianglesOfPiece[i] * 3;
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int vertex = triangles[t + corner];
                        // A vertex belongs to one piece, except after the spatial fallback, where a cell
                        // border can run through it; then each piece gets its own copy.
                        if (scratch.stamp[vertex] != scratch.visit)
                        {
                            scratch.stamp[vertex] = scratch.visit;
                            scratch.mapped[vertex] = outPositions.Count;
                            outPositions.Add(anchor + (positions[vertex] - anchor) * pieceScale);
                            outNormals.Add(normals[vertex]);
                            outUv?.Add(foliage.uv0[vertex]);
                        }
                        outIndices.Add(scratch.mapped[vertex]);
                    }
                }
            }

            return new ForestMeshTools.MeshData
            {
                positions = outPositions.ToArray(),
                normals = outNormals.ToArray(),
                tangents = null,   // the tree sources are imported without tangents; ToMesh calculates them
                uv0 = outUv?.ToArray(),
                subMeshes = new[] { outIndices.ToArray() },
            };
        }

        /// <summary>
        /// A level made of twig cards instead of single needles (review 1 of the M3 captures). Each piece of the young
        /// firs is one needle, so any level that keeps a few thousand of them is a spray of single enlarged needles:
        /// bamboo from 14 m on, whatever the scale cap, grouping or crown core (all tried, build/issues/M3-review1.md).
        /// The scan's own atlas holds seven photographed twig sprays that its mesh never uses. Here the needles are
        /// gathered into cubic cells, as large as it takes for the cells to fit the triangle target, and every cell
        /// becomes one quad in the plane its needles span, pointing away from the trunk, as long and wide as the needles
        /// reach, mapped to the sprite whose shape fits best. Stems are left to the clustered wood. Every cell is kept,
        /// so the crown has no holes; the cards of a farther level come from larger cells.
        /// </summary>
        internal static ForestMeshTools.MeshData TwigCards(ForestMeshTools.MeshData foliage, Pieces pieces, int targetTriangles,
            ForestKitSpec.TwigSprite[] sprites, float fill, out ThinStats stats, out float cellSize)
        {
            stats = default;
            cellSize = 0f;
            if (foliage == null) throw new ArgumentNullException(nameof(foliage));
            if (pieces == null) throw new ArgumentNullException(nameof(pieces));
            if (sprites == null || sprites.Length == 0) throw new ArgumentException("No twig sprites.", nameof(sprites));
            if (foliage.uv0 == null) throw new ArgumentException("Twig cards need the foliage's UVs to find the atlas.", nameof(foliage));

            int[] triangles = JoinSubMeshes(foliage.subMeshes);
            int triangleCount = triangles.Length / 3;
            if (pieces.pieceOfTriangle == null || pieces.pieceOfTriangle.Length != triangleCount)
                throw new ArgumentException("The pieces were not found on this mesh: the triangle counts differ.", nameof(pieces));
            stats.sourceTriangles = triangleCount;
            stats.scale = fill;
            int cardBudget = targetTriangles / 2;
            if (pieces.count == 0 || cardBudget <= 0) return ForestMeshTools.Empty(1, true);

            Vector3[] positions = foliage.positions;
            Vector3[] normals = foliage.normals ?? ForestMeshTools.ComputeNormals(foliage);
            var start = new int[pieces.count + 1];
            for (int t = 0; t < triangleCount; t++) start[pieces.pieceOfTriangle[t] + 1]++;
            for (int p = 0; p < pieces.count; p++) start[p + 1] += start[p];
            var trianglesOfPiece = new int[triangleCount];
            var cursor = new int[pieces.count];
            Array.Copy(start, cursor, pieces.count);
            for (int t = 0; t < triangleCount; t++) trianglesOfPiece[cursor[pieces.pieceOfTriangle[t]]++] = t;
            var scratch = new Scratch(positions.Length);
            bool[] tube = TubePieces(normals, triangles, trianglesOfPiece, start, pieces.count, scratch);

            var needles = new List<int>(pieces.count);
            for (int p = 0; p < pieces.count; p++)
                if (!tube[p] && start[p + 1] > start[p]) needles.Add(p);
            if (needles.Count == 0) return ForestMeshTools.Empty(1, true);

            // The smallest cell whose number of occupied cells fits the budget.
            var keys = new long[needles.Count];
            int CellsAt(float size)
            {
                double inverse = 1.0 / size;
                for (int i = 0; i < needles.Count; i++) keys[i] = CardCellKey(pieces.centroid[needles[i]], pieces.trunkAxisOrigin, inverse);
                Array.Sort(keys);
                int distinct = 1;
                for (int i = 1; i < keys.Length; i++) if (keys[i] != keys[i - 1]) distinct++;
                return distinct;
            }
            float fine = 0.01f, coarse = 4f;
            if (CellsAt(fine) <= cardBudget) coarse = fine;
            else
                for (int step = 0; step < 30; step++)
                {
                    float middle = Mathf.Sqrt(fine * coarse);
                    if (CellsAt(middle) > cardBudget) fine = middle; else coarse = middle;
                    if (coarse / fine < 1.002f) break;
                }
            cellSize = coarse;

            // Needles in cell order; the piece number breaks ties, so the order is total and repeatable.
            double cellInverse = 1.0 / cellSize;
            var cellOfNeedle = new long[needles.Count];
            var order = new int[needles.Count];
            for (int i = 0; i < needles.Count; i++)
            {
                cellOfNeedle[i] = CardCellKey(pieces.centroid[needles[i]], pieces.trunkAxisOrigin, cellInverse);
                order[i] = i;
            }
            Array.Sort(order, (left, right) =>
            {
                int byCell = cellOfNeedle[left].CompareTo(cellOfNeedle[right]);
                return byCell != 0 ? byCell : needles[left].CompareTo(needles[right]);
            });

            Envelope envelope = Envelope.Build(positions, triangles, pieces.trunkAxisOrigin);
            var outPositions = new List<Vector3>(cardBudget * 4);
            var outNormals = new List<Vector3>(cardBudget * 4);
            var outUv = new List<Vector2>(cardBudget * 4);
            var outIndices = new List<int>(cardBudget * 6);
            var corners = new List<Vector3>();
            int cards = 0, sourceKept = 0;
            for (int first = 0; first < order.Length;)
            {
                int last = first;
                while (last + 1 < order.Length && cellOfNeedle[order[last + 1]] == cellOfNeedle[order[first]]) last++;

                // The corners of the cell's needles, and their mean normal: the cards are shaded as the needles were, so
                // the hand-over from the needle level does not brighten the crown.
                corners.Clear();
                Vector3 normalSum = Vector3.zero;
                for (int i = first; i <= last; i++)
                {
                    int piece = needles[order[i]];
                    sourceKept += start[piece + 1] - start[piece];
                    for (int k = start[piece]; k < start[piece + 1]; k++)
                    {
                        int t = trianglesOfPiece[k] * 3;
                        for (int corner = 0; corner < 3; corner++)
                        {
                            corners.Add(positions[triangles[t + corner]]);
                            normalSum += normals[triangles[t + corner]];
                        }
                    }
                }
                long cellKey = cellOfNeedle[order[first]];
                first = last + 1;
                if (AddTwigCard(corners, normalSum, pieces.trunkAxisOrigin, cellKey, sprites, fill, envelope, outPositions, outNormals, outUv, outIndices))
                    cards++;
            }

            stats.piecesKept = cards;
            stats.piecesFlattened = cards;
            stats.sourceTrianglesKept = sourceKept;
            return new ForestMeshTools.MeshData
            {
                positions = outPositions.ToArray(),
                normals = outNormals.ToArray(),
                tangents = null,
                uv0 = outUv.ToArray(),
                subMeshes = new[] { outIndices.ToArray() },
            };
        }

        // The cell a needle of a card level belongs to. With ForestKitSpec.TwigCardRadialStretch above 1 the cells are cylindrical:
        // TwigCardRadialStretch times as long along the radius from the trunk as they are high and wide, so the needles of one cell
        // lie along a stretch of branch and its card is a stroke along the branch, not a round tuft (review 2 of the M4 captures:
        // cubic cells of shoot cards still read as the rounded clumps of a broadleaf crown).
        static long CardCellKey(Vector3 point, Vector3 axisOrigin, double inverse)
        {
            float stretch = ForestKitSpec.TwigCardRadialStretch;
            if (!(stretch > 1f)) return ForestMeshTools.CellKey(point, inverse);
            double dx = point.x - axisOrigin.x, dz = point.z - axisOrigin.z;
            double radius = Math.Sqrt(dx * dx + dz * dz);
            long ring = (long)Math.Floor(radius * inverse / stretch);
            long level = (long)Math.Floor((point.y - axisOrigin.y) * inverse);
            double middle = (ring + 0.5) * stretch / inverse;                      // the ring's middle radius, metres
            long sectors = Math.Max(1L, (long)Math.Floor(2.0 * Math.PI * middle * inverse));
            double turn = (Math.Atan2(dz, dx) + Math.PI) / (2.0 * Math.PI);        // 0 to 1
            long sector = Math.Min(sectors - 1, (long)Math.Floor(turn * sectors));
            return ForestMeshTools.CellKey(new Vector3(ring + 0.5f, level + 0.5f, sector + 0.5f), 1.0);
        }

        // One card for the needles of one cell. The plane is the one the needles spread in (the two main axes of their
        // corners); the card's long axis is the first of them, turned to point away from the trunk (up for the leader), so
        // the sprite's stem sits on the inner side. A cell whose needles span almost nothing is skipped.
        static bool AddTwigCard(List<Vector3> corners, Vector3 normalSum, Vector3 axisOrigin, long cellKey, ForestKitSpec.TwigSprite[] sprites,
            float fill, Envelope envelope, List<Vector3> outPositions, List<Vector3> outNormals, List<Vector2> outUv, List<int> outIndices)
        {
            if (corners.Count < 3) return false;
            Vector3 centre = Vector3.zero;
            foreach (Vector3 corner in corners) centre += corner;
            centre /= corners.Count;
            double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (Vector3 corner in corners)
            {
                Vector3 d = corner - centre;
                xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z; yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z;
            }

            var radial = new Vector3(centre.x - axisOrigin.x, 0f, centre.z - axisOrigin.z);
            float radialLength = radial.magnitude;
            Vector3 outward = radialLength > 0.05f ? (radial / radialLength + Vector3.up * 0.35f).normalized : Vector3.up;

            Vector3 Multiply(Vector3 v, Vector3 deflate, double lambda) => new Vector3(
                (float)(xx * v.x + xy * v.y + xz * v.z - lambda * deflate.x * Vector3.Dot(deflate, v)),
                (float)(xy * v.x + yy * v.y + yz * v.z - lambda * deflate.y * Vector3.Dot(deflate, v)),
                (float)(xz * v.x + yz * v.y + zz * v.z - lambda * deflate.z * Vector3.Dot(deflate, v)));
            Vector3 main = outward;
            for (int step = 0; step < 24; step++)
            {
                Vector3 next = Multiply(main, Vector3.zero, 0.0);
                if (next.sqrMagnitude < 1e-20f) { main = outward; break; }
                main = next.normalized;
            }
            double lambdaMain = Vector3.Dot(main, Multiply(main, Vector3.zero, 0.0));
            if (Vector3.Dot(main, outward) < 0f) main = -main;

            Vector3 across = Vector3.Cross(main, Vector3.up);
            if (across.sqrMagnitude < 1e-4f) across = Vector3.Cross(main, radialLength > 0.05f ? radial : Vector3.right);
            across.Normalize();
            for (int step = 0; step < 24; step++)
            {
                Vector3 next = Multiply(across, main, lambdaMain);
                next -= main * Vector3.Dot(next, main);
                if (next.sqrMagnitude < 1e-20f) break;
                across = next.normalized;
            }
            Vector3 normal = Vector3.Cross(main, across).normalized;
            if (normal.y < 0f || (Mathf.Abs(normal.y) < 0.05f && Vector3.Dot(normal, radial) < 0f)) normal = -normal;

            float alongMin = float.PositiveInfinity, alongMax = float.NegativeInfinity, sideMin = float.PositiveInfinity, sideMax = float.NegativeInfinity;
            foreach (Vector3 corner in corners)
            {
                Vector3 d = corner - centre;
                float a = Vector3.Dot(d, main), s = Vector3.Dot(d, across);
                alongMin = Mathf.Min(alongMin, a); alongMax = Mathf.Max(alongMax, a);
                sideMin = Mathf.Min(sideMin, s); sideMax = Mathf.Max(sideMax, s);
            }
            float length = alongMax - alongMin, width = sideMax - sideMin;
            if (!(length > 0.005f)) return false;

            // The sprite whose shape is nearest the cell's, with a hash so that neighbours of the same shape vary.
            float ratio = Mathf.Max(width, 0.005f) / length;
            int chosen = 0;
            float best = float.PositiveInfinity;
            for (int i = 0; i < sprites.Length; i++)
            {
                float score = Mathf.Abs(Mathf.Log(sprites[i].Aspect / ratio)) + 0.45f * (float)Hash01(cellKey, i, 17, 1993);
                if (score < best) { best = score; chosen = i; }
            }
            ForestKitSpec.TwigSprite sprite = sprites[chosen];
            // A narrow shoot covers the needles' spread across only up to TwigCardMaxStretch times their reach along.
            float height = Mathf.Min(Mathf.Max(length, width / sprite.Aspect), length * ForestKitSpec.TwigCardMaxStretch) * fill;
            float cardWidth = height * sprite.Aspect;
            if (Hash01(cellKey, 3, 29, 1993) < 0.5) across = -across;   // mirror half the sprites

            float alongMid = 0.5f * (alongMin + alongMax), sideMid = 0.5f * (sideMin + sideMax);
            Vector3 Corner(float alongShare, float sideShare) => envelope.ClampToBox(centre
                + main * (alongMid + (alongShare - 0.5f) * height) + across * (sideMid + (sideShare - 0.5f) * cardWidth));

            int firstVertex = outPositions.Count;
            outPositions.Add(Corner(0f, 0f));
            outPositions.Add(Corner(0f, 1f));
            outPositions.Add(Corner(1f, 1f));
            outPositions.Add(Corner(1f, 0f));
            // u across the sprite, v from its stem (bottom of the image, low v) to its tip.
            outUv.Add(new Vector2(sprite.uvMin.x, sprite.uvMin.y));
            outUv.Add(new Vector2(sprite.uvMax.x, sprite.uvMin.y));
            outUv.Add(new Vector2(sprite.uvMax.x, sprite.uvMax.y));
            outUv.Add(new Vector2(sprite.uvMin.x, sprite.uvMax.y));
            // Shading normal: away from the trunk, a little up, as a crown is lit (the needles of a cell point every way
            // and shade one another; a card facing straight up caught the whole sky and read lighter than the needle
            // level it follows). The leader, on the axis, keeps the needles' own mean or the card's plane.
            Vector3 shading = radialLength > 0.05f
                ? (radial / radialLength + Vector3.up * ForestKitSpec.TwigCardNormalLift).normalized
                : normalSum.magnitude > 0.3f * corners.Count ? normalSum.normalized : normal;
            for (int i = 0; i < 4; i++) outNormals.Add(shading);

            Vector3 face = Vector3.Cross(outPositions[firstVertex + 1] - outPositions[firstVertex], outPositions[firstVertex + 2] - outPositions[firstVertex]);
            bool reversed = Vector3.Dot(face, shading) < 0f;   // the front face faces the way it is shaded
            outIndices.Add(firstVertex);
            outIndices.Add(firstVertex + (reversed ? 2 : 1));
            outIndices.Add(firstVertex + (reversed ? 1 : 2));
            outIndices.Add(firstVertex);
            outIndices.Add(firstVertex + (reversed ? 3 : 2));
            outIndices.Add(firstVertex + (reversed ? 2 : 3));
            return true;
        }

        /// <summary>
        /// Which pieces are closed tubes (a twig's stem) rather than cards: the mean of their vertex normals is
        /// shorter than ForestKitSpec.FlattenTubeCoherence, the same test the flattening uses. A card of the
        /// synthetic tests has one normal and is never a tube.
        /// </summary>
        static bool[] TubePieces(Vector3[] normals, int[] triangles, int[] trianglesOfPiece, int[] start, int count, Scratch scratch)
        {
            var tube = new bool[count];
            for (int piece = 0; piece < count; piece++)
            {
                scratch.visit++;
                Vector3 sum = Vector3.zero;
                int vertices = 0;
                for (int i = start[piece]; i < start[piece + 1]; i++)
                {
                    int t = trianglesOfPiece[i] * 3;
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int vertex = triangles[t + corner];
                        if (scratch.stamp[vertex] == scratch.visit) continue;
                        scratch.stamp[vertex] = scratch.visit;
                        sum += normals[vertex];
                        vertices++;
                    }
                }
                tube[piece] = vertices >= 3 && (sum / vertices).magnitude < ForestKitSpec.FlattenTubeCoherence;
            }
            return tube;
        }

        /// <summary>Triangle count of the middle piece, for the kit report.</summary>
        internal static float MedianPieceTriangles(Pieces pieces)
        {
            if (pieces.count == 0) return 0f;
            var tally = new int[pieces.count];
            foreach (int piece in pieces.pieceOfTriangle) tally[piece]++;
            Array.Sort(tally);
            int middle = tally.Length / 2;
            return tally.Length % 2 == 1 ? tally[middle] : 0.5f * (tally[middle - 1] + tally[middle]);
        }

        // ---- Private ----

        sealed class Scratch
        {
            public readonly int[] stamp;       // per source vertex: the visit that last touched it
            public readonly int[] mapped;      // per source vertex: its place in the output during that visit
            public readonly List<int> vertices = new List<int>();
            public readonly List<Vector3> points = new List<Vector3>();
            public int visit;

            public Scratch(int vertexCount)
            {
                stamp = new int[vertexCount];
                mapped = new int[vertexCount];
            }
        }

        // A piece replaced by one quad. Corners are at the piece's UV box: (min, min), (max, min), (max, max), (min, max).
        struct Quad
        {
            public bool valid, reversed;
            public Vector3 p0, p1, p2, p3;
            public Vector3 n0, n1, n2, n3;
            public Vector2 uvMin, uvMax;
        }

        // The crown's outline as the thinner needs it: the foliage's box, and for every 1 m of height and
        // every sector around the trunk the farthest the foliage reaches.
        sealed class Envelope
        {
            const float Slack = 1e-4f;

            Vector3 min, max;
            float axisX, axisZ;
            int bands;
            float[] reach;

            public static Envelope Build(Vector3[] positions, int[] triangles, Vector3 axis)
            {
                var envelope = new Envelope { axisX = axis.x, axisZ = axis.z };
                Vector3 min = positions[triangles[0]], max = min;
                foreach (int vertex in triangles)
                {
                    min = Vector3.Min(min, positions[vertex]);
                    max = Vector3.Max(max, positions[vertex]);
                }
                envelope.min = min;
                envelope.max = max;
                envelope.bands = Mathf.Max(1, Mathf.CeilToInt((max.y - min.y) / ForestKitSpec.BandHeight));
                envelope.reach = new float[envelope.bands * ForestKitSpec.EnvelopeSectors];
                foreach (int vertex in triangles)
                {
                    Vector3 point = positions[vertex];
                    float dx = point.x - axis.x, dz = point.z - axis.z;
                    int cell = envelope.Cell(point.y, dx, dz);
                    envelope.reach[cell] = Mathf.Max(envelope.reach[cell], Mathf.Sqrt(dx * dx + dz * dz));
                }
                return envelope;
            }

            // The largest scale about the anchor, up to the limit, at which every point is still inside.
            // Walks up from 1 and stops at the first step that fails, so every smaller scale fits too.
            public float MaxScale(List<Vector3> points, Vector3 anchor, float limit)
            {
                float best = 1f;
                if (limit <= 1f) return best;
                for (float step = ForestKitSpec.EnvelopeScaleStep; ; step *= ForestKitSpec.EnvelopeScaleStep)
                {
                    float scale = Mathf.Min(step, limit);
                    foreach (Vector3 point in points)
                        if (!Contains(anchor + (point - anchor) * scale)) return best;
                    best = scale;
                    if (scale >= limit) return best;
                }
            }

            public Vector3 ClampToBox(Vector3 point) => Vector3.Max(min, Vector3.Min(max, point));

            bool Contains(Vector3 point)
            {
                if (point.x < min.x - Slack || point.x > max.x + Slack || point.y < min.y - Slack || point.y > max.y + Slack
                    || point.z < min.z - Slack || point.z > max.z + Slack)
                    return false;
                float dx = point.x - axisX, dz = point.z - axisZ;
                float allowed = reach[Cell(point.y, dx, dz)] + Slack;
                return dx * dx + dz * dz <= allowed * allowed;
            }

            int Cell(float y, float dx, float dz)
            {
                int band = Mathf.Clamp(Mathf.FloorToInt((y - min.y) / ForestKitSpec.BandHeight), 0, bands - 1);
                return band * ForestKitSpec.EnvelopeSectors + Sector(dx, dz, ForestKitSpec.EnvelopeSectors);
            }
        }

        static int Sector(float dx, float dz, int sectors)
        {
            float turn = (Mathf.Atan2(dz, dx) + Mathf.PI) / (2f * Mathf.PI);
            return Mathf.Clamp(Mathf.FloorToInt(turn * sectors), 0, sectors - 1);
        }

        static int CoreCell(Vector3 point, Vector3 axis, float lowest, float bandHeight, int heightBands, int sectors)
        {
            int band = Mathf.Clamp(Mathf.FloorToInt((point.y - lowest) / bandHeight), 0, heightBands - 1);
            return band * sectors + Sector(point.x - axis.x, point.z - axis.z, sectors);
        }

        // Adds a triangle of the hull turned so that it faces out: away from the axis for the sides
        // (facing 0), down for the bottom cap (-1), up for the top cap (1).
        static void AddFacing(Vector3[] positions, List<int> indices, int a, int b, int c, Vector3 axis, int facing)
        {
            Vector3 normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            float outward;
            if (facing != 0)
            {
                outward = normal.y * facing;
            }
            else
            {
                Vector3 centre = (positions[a] + positions[b] + positions[c]) / 3f;
                outward = normal.x * (centre.x - axis.x) + normal.z * (centre.z - axis.z);
            }
            indices.Add(a);
            indices.Add(outward >= 0f ? b : c);
            indices.Add(outward >= 0f ? c : b);
        }

        static int[] JoinSubMeshes(int[][] subMeshes)
        {
            if (subMeshes == null) throw new ArgumentException("The mesh data has no sub-mesh list.");
            if (subMeshes.Length == 1) return subMeshes[0];
            int total = 0;
            foreach (int[] indices in subMeshes) total += indices.Length;
            var joined = new int[total];
            int cursor = 0;
            foreach (int[] indices in subMeshes)
            {
                Array.Copy(indices, 0, joined, cursor, indices.Length);
                cursor += indices.Length;
            }
            return joined;
        }

        // Triangles that share a position belong together. Positions are compared on a 0.1 mm grid, not by
        // vertex index, because a UV seam duplicates the vertices of what is still one twig.
        static int[] ConnectedPieces(Vector3[] positions, int[] triangles, out int count)
        {
            int vertexCount = positions.Length;
            var cellKey = new long[vertexCount];
            var order = new int[vertexCount];
            double inverse = 1.0 / ForestKitSpec.WeldQuantum;
            for (int v = 0; v < vertexCount; v++)
            {
                cellKey[v] = ForestMeshTools.CellKey(positions[v], inverse);
                order[v] = v;
            }
            // Sorting instead of a dictionary: five million vertices then cost two arrays, not a hash table.
            Array.Sort(cellKey, order);

            var welded = new int[vertexCount];
            int weldedCount = 0;
            for (int i = 0; i < vertexCount; i++)
            {
                if (i > 0 && cellKey[i] != cellKey[i - 1]) weldedCount++;
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
                    if (other == root) continue;
                    // The smaller number becomes the root, so the result does not depend on visiting order.
                    if (other < root)
                    {
                        parent[root] = other;
                        root = other;
                    }
                    else
                    {
                        parent[other] = root;
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

        static int Root(int[] parent, int node)
        {
            while (parent[node] != node)
            {
                parent[node] = parent[parent[node]];
                node = parent[node];
            }
            return node;
        }

        static bool PiecesAreSmall(int[] pieceOfTriangle, int count)
        {
            if (count < ForestKitSpec.MinPieces) return false;
            var tally = new int[count];
            int largest = 0;
            foreach (int piece in pieceOfTriangle) largest = Math.Max(largest, ++tally[piece]);
            return largest <= ForestKitSpec.MaxPieceShare * pieceOfTriangle.Length;
        }

        // The fallback: a piece is whatever has its triangle centroids in one 6 cm cell.
        static int[] SpatialPieces(Vector3[] positions, int[] triangles, out int count)
        {
            int triangleCount = triangles.Length / 3;
            var pieceOfTriangle = new int[triangleCount];
            var pieceOfCell = new Dictionary<long, int>();
            double inverse = 1.0 / ForestKitSpec.SpatialPieceCell;
            for (int t = 0; t < triangleCount; t++)
            {
                Vector3 centre = (positions[triangles[t * 3]] + positions[triangles[t * 3 + 1]] + positions[triangles[t * 3 + 2]]) / 3f;
                long cell = ForestMeshTools.CellKey(centre, inverse);
                if (!pieceOfCell.TryGetValue(cell, out int piece))
                {
                    piece = pieceOfCell.Count;
                    pieceOfCell.Add(cell, piece);
                }
                pieceOfTriangle[t] = piece;
            }
            count = pieceOfCell.Count;
            return pieceOfTriangle;
        }

        // Least-squares fit of position (and normal) as a plane over UV, evaluated at the corners of the
        // piece's UV box. False when the piece has no UV area or bends too far from any plane.
        static bool TryFitQuad(ForestMeshTools.MeshData mesh, Vector3[] normals, int[] triangles, int[] trianglesOfPiece,
            int from, int to, Scratch scratch, out Quad quad)
        {
            quad = default;
            Vector3[] positions = mesh.positions;
            Vector2[] uv = mesh.uv0;

            scratch.visit++;
            scratch.vertices.Clear();
            for (int i = from; i < to; i++)
            {
                int t = trianglesOfPiece[i] * 3;
                for (int corner = 0; corner < 3; corner++)
                {
                    int vertex = triangles[t + corner];
                    if (scratch.stamp[vertex] == scratch.visit) continue;
                    scratch.stamp[vertex] = scratch.visit;
                    scratch.vertices.Add(vertex);
                }
            }
            int count = scratch.vertices.Count;
            if (count < 3) return false;

            double meanU = 0, meanV = 0, meanX = 0, meanY = 0, meanZ = 0, meanNx = 0, meanNy = 0, meanNz = 0;
            float minU = float.PositiveInfinity, maxU = float.NegativeInfinity, minV = float.PositiveInfinity, maxV = float.NegativeInfinity;
            Vector3 low = positions[scratch.vertices[0]], high = low;
            foreach (int vertex in scratch.vertices)
            {
                Vector2 coordinate = uv[vertex];
                Vector3 point = positions[vertex], normal = normals[vertex];
                meanU += coordinate.x; meanV += coordinate.y;
                meanX += point.x; meanY += point.y; meanZ += point.z;
                meanNx += normal.x; meanNy += normal.y; meanNz += normal.z;
                minU = Mathf.Min(minU, coordinate.x); maxU = Mathf.Max(maxU, coordinate.x);
                minV = Mathf.Min(minV, coordinate.y); maxV = Mathf.Max(maxV, coordinate.y);
                low = Vector3.Min(low, point);
                high = Vector3.Max(high, point);
            }
            if (!(maxU - minU > 1e-6f) || !(maxV - minV > 1e-6f)) return false;
            meanU /= count; meanV /= count;
            meanX /= count; meanY /= count; meanZ /= count;
            meanNx /= count; meanNy /= count; meanNz /= count;

            // Normal equations with the UVs centred: the constant term drops out and two unknowns remain
            // per fitted quantity. p[0..2] is the position, p[3..5] the normal.
            double uu = 0, uv2 = 0, vv = 0;
            var uSum = new double[6];
            var vSum = new double[6];
            foreach (int vertex in scratch.vertices)
            {
                double du = uv[vertex].x - meanU, dv = uv[vertex].y - meanV;
                Vector3 point = positions[vertex], normal = normals[vertex];
                uu += du * du; uv2 += du * dv; vv += dv * dv;
                uSum[0] += du * (point.x - meanX); vSum[0] += dv * (point.x - meanX);
                uSum[1] += du * (point.y - meanY); vSum[1] += dv * (point.y - meanY);
                uSum[2] += du * (point.z - meanZ); vSum[2] += dv * (point.z - meanZ);
                uSum[3] += du * (normal.x - meanNx); vSum[3] += dv * (normal.x - meanNx);
                uSum[4] += du * (normal.y - meanNy); vSum[4] += dv * (normal.y - meanNy);
                uSum[5] += du * (normal.z - meanNz); vSum[5] += dv * (normal.z - meanNz);
            }
            double determinant = uu * vv - uv2 * uv2;
            // UVs on one line span no area in the atlas: there is no plane to fit.
            if (!(determinant > 1e-9 * uu * vv)) return false;

            var alongU = new double[6];
            var alongV = new double[6];
            for (int k = 0; k < 6; k++)
            {
                alongU[k] = (vv * uSum[k] - uv2 * vSum[k]) / determinant;
                alongV[k] = (uu * vSum[k] - uv2 * uSum[k]) / determinant;
            }

            // The residual is the farthest any vertex lies from where the plane puts it.
            double worst = 0;
            foreach (int vertex in scratch.vertices)
            {
                double du = uv[vertex].x - meanU, dv = uv[vertex].y - meanV;
                Vector3 point = positions[vertex];
                double ex = meanX + alongU[0] * du + alongV[0] * dv - point.x;
                double ey = meanY + alongU[1] * du + alongV[1] * dv - point.y;
                double ez = meanZ + alongU[2] * du + alongV[2] * dv - point.z;
                worst = Math.Max(worst, ex * ex + ey * ey + ez * ez);
            }
            double size = (high - low).magnitude;
            var mean = new Vector3((float)meanNx, (float)meanNy, (float)meanNz);

            // A closed tube (a twig's stem) is no card: its normals point all around and cancel, the plane fitted over
            // its unrolled UVs cuts through it at an arbitrary angle, and its UV box is the whole unrolled bark. Fitted
            // like a card it became a straight brown plank wider than the stem, shaded by meaningless normals (the
            // young firs, build/issues/M3-E1.md). It becomes a stick instead: one quad along the stem's own axis, as
            // wide as the stem. Measured on fir_sapling_medium_b: 658 stem pieces at a mean-normal length of 0.15
            // to 0.18, 43,154 needle cards at 0.98 to 0.99.
            if (mean.magnitude < ForestKitSpec.FlattenTubeCoherence)
            {
                double surface = 0;
                for (int i = from; i < to; i++)
                {
                    int t = trianglesOfPiece[i] * 3;
                    Vector3 a = positions[triangles[t]], b = positions[triangles[t + 1]], c = positions[triangles[t + 2]];
                    surface += 0.5 * Vector3.Cross(b - a, c - a).magnitude;
                }
                return TryFitStick(positions, uv, scratch.vertices, surface, meanX, meanY, meanZ, alongU, alongV, minU, maxU, minV, maxV, low, high, out quad);
            }

            if (Math.Sqrt(worst) > ForestKitSpec.FlattenMaxResidual * size) return false;
            Vector3 Corner(float u, float v, int offset)
            {
                double du = u - meanU, dv = v - meanV;
                double baseX = offset == 0 ? meanX : meanNx, baseY = offset == 0 ? meanY : meanNy, baseZ = offset == 0 ? meanZ : meanNz;
                return new Vector3(
                    (float)(baseX + alongU[offset] * du + alongV[offset] * dv),
                    (float)(baseY + alongU[offset + 1] * du + alongV[offset + 1] * dv),
                    (float)(baseZ + alongU[offset + 2] * du + alongV[offset + 2] * dv));
            }

            quad.valid = true;
            quad.uvMin = new Vector2(minU, minV);
            quad.uvMax = new Vector2(maxU, maxV);
            quad.p0 = Corner(minU, minV, 0);
            quad.p1 = Corner(maxU, minV, 0);
            quad.p2 = Corner(maxU, maxV, 0);
            quad.p3 = Corner(minU, maxV, 0);

            // The quad faces the way the piece's own normals did, whatever the UV layout's handedness.
            Vector3 face = Vector3.Cross(quad.p1 - quad.p0, quad.p2 - quad.p0);
            if (mean.sqrMagnitude < 1e-12f) mean = face;
            quad.reversed = Vector3.Dot(face, mean) < 0f;
            Vector3 fallback = mean.normalized;
            quad.n0 = CornerNormal(Corner(minU, minV, 3), fallback);
            quad.n1 = CornerNormal(Corner(maxU, minV, 3), fallback);
            quad.n2 = CornerNormal(Corner(maxU, maxV, 3), fallback);
            quad.n3 = CornerNormal(Corner(minU, maxV, 3), fallback);
            return true;
        }

        // The stick that replaces a tube. Its axis is the main direction of the tube's vertices (power iteration on
        // their covariance), its length their extent along it, its width the tube's diameter. The UV direction whose
        // fitted change of position runs further along the axis is laid along the stick, the other across it, so the
        // bark texture keeps its direction. All four corners face up, away from the ground.
        //
        // The diameter was twice the vertices' mean distance from the straight axis. A twig's stem is curved, and for
        // a curved tube that distance is mostly the bow of the curve, not the radius: the sticks came out two to four
        // times as wide as the stems, flat tan bars lit through from above that stood out of the crowns of the near
        // firs in trail_start (M3 placement, build/issues/M3-placement.md). The radius is now also read from the
        // surface, r = area / (2 pi length), which a bow does not change, and the smaller of the two is used.
        static bool TryFitStick(Vector3[] positions, Vector2[] uv, List<int> vertices, double surface, double meanX, double meanY, double meanZ,
            double[] alongU, double[] alongV, float minU, float maxU, float minV, float maxV, Vector3 low, Vector3 high, out Quad quad)
        {
            quad = default;
            var centre = new Vector3((float)meanX, (float)meanY, (float)meanZ);
            double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (int vertex in vertices)
            {
                Vector3 d = positions[vertex] - centre;
                xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z;
                yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z;
            }

            var spanU = new Vector3((float)alongU[0], (float)alongU[1], (float)alongU[2]) * (maxU - minU);
            var spanV = new Vector3((float)alongV[0], (float)alongV[1], (float)alongV[2]) * (maxV - minV);
            Vector3 axis = spanV.sqrMagnitude >= spanU.sqrMagnitude ? spanV : spanU;
            if (axis.sqrMagnitude < 1e-20f) axis = Vector3.up;
            axis.Normalize();
            for (int step = 0; step < 32; step++)
            {
                var next = new Vector3(
                    (float)(xx * axis.x + xy * axis.y + xz * axis.z),
                    (float)(xy * axis.x + yy * axis.y + yz * axis.z),
                    (float)(xz * axis.x + yz * axis.y + zz * axis.z));
                if (next.sqrMagnitude < 1e-30f) return false;
                axis = next.normalized;
            }

            float tMin = float.PositiveInfinity, tMax = float.NegativeInfinity;
            double radius = 0.0;
            foreach (int vertex in vertices)
            {
                Vector3 d = positions[vertex] - centre;
                float t = Vector3.Dot(d, axis);
                tMin = Mathf.Min(tMin, t);
                tMax = Mathf.Max(tMax, t);
                radius += (d - axis * t).magnitude;
            }
            radius /= vertices.Count;
            if (!(tMax - tMin > 1e-5f) || !(radius > 0.0)) return false;

            Vector3 side = Vector3.Cross(axis, Vector3.up);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(axis, Vector3.right);
            side.Normalize();
            Vector3 normal = Vector3.Cross(side, axis).normalized;
            if (normal.y < 0f) normal = -normal;

            bool alongIsV = Mathf.Abs(Vector3.Dot(spanV, axis)) >= Mathf.Abs(Vector3.Dot(spanU, axis));
            bool forward = Vector3.Dot(alongIsV ? spanV : spanU, axis) >= 0f;
            float halfWidth = (float)radius;
            if (surface > 0.0)
                halfWidth = Mathf.Min(halfWidth, (float)(surface / (2.0 * Math.PI * (tMax - tMin))));
            Vector3 Corner(float u, float v)
            {
                float along = alongIsV ? Mathf.InverseLerp(minV, maxV, v) : Mathf.InverseLerp(minU, maxU, u);
                float across = alongIsV ? Mathf.InverseLerp(minU, maxU, u) : Mathf.InverseLerp(minV, maxV, v);
                float t = forward ? Mathf.Lerp(tMin, tMax, along) : Mathf.Lerp(tMax, tMin, along);
                // The axis of a curved or forked stem runs along its chord, and its ends can lie beyond the stem
                // itself; a stick never reaches past the box of the piece it replaces (review 1 of the M3 captures:
                // straight dark lines running out of the crowns).
                Vector3 corner = centre + axis * t + side * ((across * 2f - 1f) * halfWidth);
                Vector3 pad = Vector3.one * halfWidth;
                return Vector3.Max(low - pad, Vector3.Min(high + pad, corner));
            }

            quad.valid = true;
            quad.uvMin = new Vector2(minU, minV);
            quad.uvMax = new Vector2(maxU, maxV);
            quad.p0 = Corner(minU, minV);
            quad.p1 = Corner(maxU, minV);
            quad.p2 = Corner(maxU, maxV);
            quad.p3 = Corner(minU, maxV);
            quad.reversed = Vector3.Dot(Vector3.Cross(quad.p1 - quad.p0, quad.p2 - quad.p0), normal) < 0f;
            quad.n0 = quad.n1 = quad.n2 = quad.n3 = normal;
            return true;
        }

        // A normal extrapolated to a corner can collapse or turn over; the piece's mean normal is the safe answer then.
        static Vector3 CornerNormal(Vector3 fitted, Vector3 fallback)
        {
            float length = fitted.magnitude;
            if (length < 0.5f || Vector3.Dot(fitted, fallback) <= 0f) return fallback;
            return fitted / length;
        }

        // The common scale s with sum(area * min(s, limit)^2) = totalArea: pieces that are stopped by the
        // crown's outline keep their limit and the rest make up the area, up to maxScale.
        static float SolveScale(double[] area, float[] limit, double totalArea, float maxScale, out bool capReached)
        {
            double AreaAt(double scale)
            {
                double sum = 0.0;
                for (int i = 0; i < area.Length; i++)
                {
                    double s = Math.Min(scale, limit[i]);
                    sum += area[i] * s * s;
                }
                return sum;
            }

            capReached = false;
            if (AreaAt(1.0) >= totalArea) return 1f;
            double atMost = AreaAt(maxScale);
            if (atMost <= totalArea)
            {
                capReached = atMost < totalArea * (1.0 - 1e-6);
                return maxScale;
            }

            double low = 1.0, high = maxScale;
            for (int step = 0; step < 48; step++)
            {
                double middle = 0.5 * (low + high);
                if (AreaAt(middle) < totalArea) low = middle;
                else high = middle;
            }
            return (float)high;
        }

        // A number in (0, 1) from three grid coordinates and the seed (the splitmix64 finaliser, chained).
        static double Hash01(long x, long y, long z, int seed)
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
    }
}
