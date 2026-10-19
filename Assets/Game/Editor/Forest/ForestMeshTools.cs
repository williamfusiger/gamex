using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gamex.EditorTools
{
    /// <summary>
    /// Mesh work for the forest kit on plain arrays: reading an imported mesh, moving it into prefab
    /// space, splitting and merging sub-meshes, decimating connected surfaces and measuring the result.
    /// Only Read, ToMesh and the optional Unity generator touch a Mesh; the rest is array maths, so it
    /// can be unit-tested without a scene.
    /// </summary>
    public static class ForestMeshTools
    {
        public sealed class MeshData
        {
            public Vector3[] positions; public Vector3[] normals; public Vector4[] tangents;   // tangents may be null
            public Vector2[] uv0;                                                               // may be null
            public int[][] subMeshes;                                                           // triangle indices per sub-mesh
        }

        const int CellBias = 1 << 20;            // grid cells are packed three to a long, 21 bits each
        const int CoverageMaxCells = 1 << 28;    // 32 MB of bits per silhouette view
        const int TrunkLevels = 8, TrunkDirections = 32, TrunkMinHits = 16;
        const float TrunkPercentile = 0.9f;

        public static MeshData Read(Mesh mesh)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));

            Vector3[] positions = mesh.vertices;
            if (positions.Length == 0 || positions.Length != mesh.vertexCount)
                throw new InvalidOperationException(
                    $"Mesh '{mesh.name}' returned {positions.Length} of {mesh.vertexCount} vertices. If it comes from a model "
                    + "whose data is not readable in the Editor, set isReadable = true in ForestAssetPostprocessor (probe P1).");

            Vector3[] normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;
            Vector2[] uv = mesh.uv;
            var data = new MeshData
            {
                positions = positions,
                tangents = tangents.Length == positions.Length ? tangents : null,
                uv0 = uv.Length == positions.Length ? uv : null,
                subMeshes = new int[mesh.subMeshCount][],
            };

            for (int i = 0; i < data.subMeshes.Length; i++)
            {
                if (mesh.GetTopology(i) != MeshTopology.Triangles)
                    throw new InvalidOperationException($"Sub-mesh {i} of '{mesh.name}' is not made of triangles.");
                // A mesh that carries generated Mesh LODs would otherwise hand back every level at once.
                data.subMeshes[i] = mesh.lodCount > 1 ? mesh.GetTriangles(i, 0, true) : mesh.GetTriangles(i);
            }

            data.normals = normals.Length == positions.Length ? normals : ComputeNormals(data);
            return data;
        }

        public static void Transform(MeshData data, Matrix4x4 matrix)
        {
            Validate(data, nameof(data));

            // Normals follow the inverse transpose, which is the cofactor matrix of the upper 3x3 divided
            // by the determinant. Normalising makes the division unnecessary; only its sign matters.
            float a = matrix.m00, b = matrix.m01, c = matrix.m02;
            float d = matrix.m10, e = matrix.m11, f = matrix.m12;
            float g = matrix.m20, h = matrix.m21, i = matrix.m22;
            float c00 = e * i - f * h, c01 = f * g - d * i, c02 = d * h - e * g;
            float c10 = c * h - b * i, c11 = a * i - c * g, c12 = b * g - a * h;
            float c20 = b * f - c * e, c21 = c * d - a * f, c22 = a * e - b * d;
            float determinant = a * c00 + b * c01 + c * c02;
            if (determinant == 0f || float.IsNaN(determinant))
                throw new ArgumentException("The matrix flattens the mesh (its determinant is zero).", nameof(matrix));
            float sign = determinant < 0f ? -1f : 1f;

            for (int v = 0; v < data.positions.Length; v++)
                data.positions[v] = matrix.MultiplyPoint3x4(data.positions[v]);

            if (data.normals != null)
            {
                for (int v = 0; v < data.normals.Length; v++)
                {
                    Vector3 n = data.normals[v];
                    var moved = new Vector3(
                        sign * (c00 * n.x + c01 * n.y + c02 * n.z),
                        sign * (c10 * n.x + c11 * n.y + c12 * n.z),
                        sign * (c20 * n.x + c21 * n.y + c22 * n.z));
                    data.normals[v] = SafeNormalize(moved, n);
                }
            }

            if (data.tangents != null)
            {
                for (int v = 0; v < data.tangents.Length; v++)
                {
                    Vector4 t = data.tangents[v];
                    var direction = new Vector3(
                        a * t.x + b * t.y + c * t.z,
                        d * t.x + e * t.y + f * t.z,
                        g * t.x + h * t.y + i * t.z);
                    direction = SafeNormalize(direction, new Vector3(t.x, t.y, t.z));
                    data.tangents[v] = new Vector4(direction.x, direction.y, direction.z, t.w * sign);
                }
            }

            // A mirror turns every triangle inside out; swapping two corners turns it back.
            if (sign < 0f)
            {
                foreach (int[] indices in data.subMeshes)
                {
                    for (int t = 0; t + 2 < indices.Length; t += 3)
                    {
                        int swap = indices[t + 1];
                        indices[t + 1] = indices[t + 2];
                        indices[t + 2] = swap;
                    }
                }
            }
        }

        /// <summary>
        /// A copy that holds the named sub-meshes, each still a sub-mesh of its own and in the order
        /// given, with only the vertices they use. Without indices it copies every sub-mesh.
        /// </summary>
        public static MeshData Extract(MeshData source, params int[] subMeshIndices)          // compact copy
        {
            Validate(source, nameof(source));
            if (subMeshIndices == null || subMeshIndices.Length == 0)
            {
                subMeshIndices = new int[source.subMeshes.Length];
                for (int i = 0; i < subMeshIndices.Length; i++) subMeshIndices[i] = i;
            }

            var remap = new int[source.positions.Length];
            for (int i = 0; i < remap.Length; i++) remap[i] = -1;

            var kept = new List<int>();
            var subMeshes = new int[subMeshIndices.Length][];
            for (int s = 0; s < subMeshIndices.Length; s++)
            {
                int index = subMeshIndices[s];
                if (index < 0 || index >= source.subMeshes.Length)
                    throw new ArgumentOutOfRangeException(nameof(subMeshIndices), $"The mesh has no sub-mesh {index}.");

                int[] from = source.subMeshes[index];
                var to = new int[from.Length];
                for (int i = 0; i < from.Length; i++)
                {
                    int vertex = from[i];
                    if (remap[vertex] < 0)
                    {
                        remap[vertex] = kept.Count;
                        kept.Add(vertex);
                    }
                    to[i] = remap[vertex];
                }
                subMeshes[s] = to;
            }

            var result = new MeshData
            {
                positions = new Vector3[kept.Count],
                normals = source.normals != null ? new Vector3[kept.Count] : null,
                tangents = source.tangents != null ? new Vector4[kept.Count] : null,
                uv0 = source.uv0 != null ? new Vector2[kept.Count] : null,
                subMeshes = subMeshes,
            };
            for (int i = 0; i < kept.Count; i++)
            {
                int vertex = kept[i];
                result.positions[i] = source.positions[vertex];
                if (result.normals != null) result.normals[i] = source.normals[vertex];
                if (result.tangents != null) result.tangents[i] = source.tangents[vertex];
                if (result.uv0 != null) result.uv0[i] = source.uv0[vertex];
            }
            return result;
        }

        /// <summary>
        /// Joins meshes into one. Every part becomes exactly one sub-mesh of the result (a part's own
        /// sub-meshes are joined), so parts that share a material cost one draw call.
        /// </summary>
        public static MeshData Merge(params MeshData[] parts)                                  // one sub-mesh per part
        {
            if (parts == null) throw new ArgumentNullException(nameof(parts));

            int vertexTotal = 0;
            bool anyUv = false, allTangents = parts.Length > 0;
            foreach (MeshData part in parts)
            {
                Validate(part, nameof(parts));
                vertexTotal += part.positions.Length;
                anyUv |= part.uv0 != null;
                allTangents &= part.tangents != null;
            }

            var merged = new MeshData
            {
                positions = new Vector3[vertexTotal],
                normals = new Vector3[vertexTotal],
                // Tangents survive only if every part has them; otherwise ToMesh recalculates them.
                tangents = allTangents ? new Vector4[vertexTotal] : null,
                // A part without UVs (the crown core) gets zeros, so the others keep theirs.
                uv0 = anyUv ? new Vector2[vertexTotal] : null,
                subMeshes = new int[parts.Length][],
            };

            int offset = 0;
            for (int p = 0; p < parts.Length; p++)
            {
                MeshData part = parts[p];
                int count = part.positions.Length;
                Array.Copy(part.positions, 0, merged.positions, offset, count);
                Array.Copy(part.normals ?? ComputeNormals(part), 0, merged.normals, offset, count);
                if (merged.tangents != null) Array.Copy(part.tangents, 0, merged.tangents, offset, count);
                if (merged.uv0 != null && part.uv0 != null) Array.Copy(part.uv0, 0, merged.uv0, offset, count);

                int indexTotal = 0;
                foreach (int[] indices in part.subMeshes) indexTotal += indices.Length;
                var joined = new int[indexTotal];
                int cursor = 0;
                foreach (int[] indices in part.subMeshes)
                    for (int i = 0; i < indices.Length; i++) joined[cursor++] = indices[i] + offset;
                merged.subMeshes[p] = joined;
                offset += count;
            }
            return merged;
        }

        /// <summary>
        /// Vertex clustering. Every vertex snaps to the mean position of its grid cell, so neighbouring
        /// triangles still meet and no crack opens. UV and normal are averaged per (cell, 1/64 UV bucket),
        /// which keeps texture seams apart. Triangles with two corners in one cell are dropped.
        /// </summary>
        public static MeshData ClusterDecimate(MeshData source, float cellSize)
        {
            Validate(source, nameof(source));
            if (!(cellSize > 0f)) return Extract(source);

            Vector3[] positions = source.positions;
            Vector3[] normals = source.normals ?? ComputeNormals(source);
            int vertexCount = positions.Length;
            double inverse = 1.0 / cellSize;

            // 1. One cell per vertex in use; the cell sits at the mean of its vertices.
            var cellOfVertex = new int[vertexCount];
            for (int v = 0; v < vertexCount; v++) cellOfVertex[v] = -1;
            var cellIds = new Dictionary<long, int>();
            foreach (int[] indices in source.subMeshes)
            {
                foreach (int vertex in indices)
                {
                    if (cellOfVertex[vertex] >= 0) continue;
                    long key = CellKey(positions[vertex], inverse);
                    if (!cellIds.TryGetValue(key, out int cell))
                    {
                        cell = cellIds.Count;
                        cellIds.Add(key, cell);
                    }
                    cellOfVertex[vertex] = cell;
                }
            }

            int cellCount = cellIds.Count;
            var cellSum = new double[cellCount * 3];
            var cellMembers = new int[cellCount];
            for (int v = 0; v < vertexCount; v++)
            {
                int cell = cellOfVertex[v];
                if (cell < 0) continue;
                cellSum[cell * 3] += positions[v].x;
                cellSum[cell * 3 + 1] += positions[v].y;
                cellSum[cell * 3 + 2] += positions[v].z;
                cellMembers[cell]++;
            }
            var cellPosition = new Vector3[cellCount];
            for (int cell = 0; cell < cellCount; cell++)
            {
                double share = 1.0 / cellMembers[cell];
                cellPosition[cell] = new Vector3(
                    (float)(cellSum[cell * 3] * share), (float)(cellSum[cell * 3 + 1] * share), (float)(cellSum[cell * 3 + 2] * share));
            }

            var outPositions = new List<Vector3>();
            var outNormals = new List<Vector3>();
            var outMembers = new List<int>();
            var outUv = source.uv0 != null ? new List<Vector2>() : null;
            var outSubMeshes = new int[source.subMeshes.Length][];

            var attributeOfVertex = new int[vertexCount];
            var attributeIds = new Dictionary<AttributeKey, int>();
            var attributeCell = new List<int>();
            var attributeNormal = new List<Vector3>();
            var attributeUv = new List<Vector2>();
            var attributeMembers = new List<int>();
            var tripleIds = new Dictionary<TripleKey, int>();
            var triples = new List<Triple>();

            for (int s = 0; s < source.subMeshes.Length; s++)
            {
                int[] indices = source.subMeshes[s];

                // 2. Shading data per (cell, UV bucket). Sub-meshes never share it: their textures differ.
                attributeIds.Clear();
                attributeCell.Clear();
                attributeNormal.Clear();
                attributeUv.Clear();
                attributeMembers.Clear();
                foreach (int vertex in indices) attributeOfVertex[vertex] = -1;
                foreach (int vertex in indices)
                {
                    if (attributeOfVertex[vertex] >= 0) continue;
                    Vector2 uv = source.uv0 != null ? source.uv0[vertex] : Vector2.zero;
                    var key = new AttributeKey(cellOfVertex[vertex], UvBucket(uv.x), UvBucket(uv.y));
                    if (!attributeIds.TryGetValue(key, out int attribute))
                    {
                        attribute = attributeCell.Count;
                        attributeIds.Add(key, attribute);
                        attributeCell.Add(cellOfVertex[vertex]);
                        attributeNormal.Add(Vector3.zero);
                        attributeUv.Add(Vector2.zero);
                        attributeMembers.Add(0);
                    }
                    attributeNormal[attribute] += normals[vertex];
                    attributeUv[attribute] += uv;
                    attributeMembers[attribute]++;
                    attributeOfVertex[vertex] = attribute;
                }

                // 3. What each surviving triangle becomes. Several source triangles can land on the same
                // three cells, facing either way.
                tripleIds.Clear();
                triples.Clear();
                for (int t = 0; t + 2 < indices.Length; t += 3)
                {
                    int v0 = indices[t], v1 = indices[t + 1], v2 = indices[t + 2];
                    int cell0 = cellOfVertex[v0], cell1 = cellOfVertex[v1], cell2 = cellOfVertex[v2];
                    if (cell0 == cell1 || cell1 == cell2 || cell0 == cell2) continue;

                    int low = cell0, middle = cell1, high = cell2;
                    bool even = true;
                    if (low > middle) { int swap = low; low = middle; middle = swap; even = !even; }
                    if (middle > high) { int swap = middle; middle = high; high = swap; even = !even; }
                    if (low > middle) { int swap = low; low = middle; middle = swap; even = !even; }

                    Vector3 coarse = Vector3.Cross(cellPosition[cell1] - cellPosition[cell0], cellPosition[cell2] - cellPosition[cell0]);
                    Vector3 fine = Vector3.Cross(positions[v1] - positions[v0], positions[v2] - positions[v0]);
                    bool faithful = Vector3.Dot(coarse, fine) > 0f;

                    var tripleKey = new TripleKey(low, middle, high);
                    if (!tripleIds.TryGetValue(tripleKey, out int id))
                    {
                        id = triples.Count;
                        tripleIds.Add(tripleKey, id);
                        triples.Add(new Triple { evenTriangle = -1, oddTriangle = -1 });
                    }
                    Triple triple = triples[id];
                    if (even)
                    {
                        triple.evenCount++;
                        if (triple.evenTriangle < 0 || (faithful && !triple.evenFaithful)) triple.evenTriangle = t;
                        triple.evenFaithful |= faithful;
                    }
                    else
                    {
                        triple.oddCount++;
                        if (triple.oddTriangle < 0 || (faithful && !triple.oddFaithful)) triple.oddTriangle = t;
                        triple.oddFaithful |= faithful;
                    }
                    triples[id] = triple;
                }

                // 4. Emit. Triangles of opposite facing on the same cells cancel: that is a fold the snapping
                // made, and keeping the net count is what keeps a closed surface closed. The exception is a
                // feature thinner than a cell whose two sides both still face the way they did (a slab, a
                // twig): cancelling would delete it, so it stays as a two-sided sheet.
                var outIndices = new List<int>();
                var outOfAttribute = new int[attributeCell.Count];
                for (int i = 0; i < outOfAttribute.Length; i++) outOfAttribute[i] = -1;

                foreach (Triple triple in triples)
                {
                    bool sheet = triple.evenFaithful && triple.oddFaithful;
                    bool emitEven = triple.evenCount > triple.oddCount || sheet;
                    bool emitOdd = triple.oddCount > triple.evenCount || sheet;
                    for (int side = 0; side < 2; side++)
                    {
                        if (side == 0 ? !emitEven : !emitOdd) continue;
                        int t = side == 0 ? triple.evenTriangle : triple.oddTriangle;
                        for (int corner = 0; corner < 3; corner++)
                        {
                            int attribute = attributeOfVertex[indices[t + corner]];
                            if (outOfAttribute[attribute] < 0)
                            {
                                outOfAttribute[attribute] = outPositions.Count;
                                outPositions.Add(cellPosition[attributeCell[attribute]]);
                                outNormals.Add(attributeNormal[attribute]);   // a sum for now
                                outMembers.Add(attributeMembers[attribute]);
                                outUv?.Add(attributeUv[attribute] / attributeMembers[attribute]);
                            }
                            outIndices.Add(outOfAttribute[attribute]);
                        }
                    }
                }
                outSubMeshes[s] = outIndices.ToArray();
            }

            var result = new MeshData
            {
                positions = outPositions.ToArray(),
                normals = outNormals.ToArray(),
                tangents = null,   // averaged tangents would be wrong at every seam; ToMesh recalculates them
                uv0 = outUv?.ToArray(),
                subMeshes = outSubMeshes,
            };

            // Averaged normals keep the scan's shading. Where a cell swallowed faces that point apart, the
            // average is short and means nothing; the new surface then has to say which way it faces.
            Vector3[] geometric = null;
            for (int v = 0; v < result.normals.Length; v++)
            {
                Vector3 sum = result.normals[v];
                float length = sum.magnitude;
                if (length > 0.2f * outMembers[v])
                {
                    result.normals[v] = sum / length;
                    continue;
                }
                if (geometric == null) geometric = ComputeNormals(result);
                result.normals[v] = geometric[v];
            }
            return result;
        }

        /// <summary>
        /// The mesh reduced to at most that many triangles, as close to the number as the method allows.
        /// </summary>
        public static MeshData DecimateToTarget(MeshData source, int targetTriangles)         // clustering or the Unity generator, per ForestKitSpec
        {
            Validate(source, nameof(source));
            if (ForestKitSpec.UseUnityGenerator && TriangleCount(source) > targetTriangles && targetTriangles > 0)
            {
                MeshData generated = GenerateWithUnity(source, targetTriangles);
                if (generated != null) return generated;
                Debug.LogWarning("Forest kit: Unity's Mesh LOD generator gave no usable level; vertex clustering is used instead.");
            }
            return ClusterToTarget(source, targetTriangles);
        }

        public static Mesh ToMesh(MeshData data, string name, bool recalculateTangents) => ToMesh(data, name, recalculateTangents, null);

        /// <summary>
        /// As ToMesh(data, name, recalculateTangents), with one vertex colour per position (W1 wind data). The colours are set before
        /// Optimize, which reorders vertices: set after it, they would pair with the wrong vertices.
        /// </summary>
        public static Mesh ToMesh(MeshData data, string name, bool recalculateTangents, Color32[] colors) => ToMesh(data, name, recalculateTangents, colors, null);

        /// <summary>
        /// W1 R2: as ToMesh(data, name, recalculateTangents, colors), with a UV2 value per position (a fern's or grass blade's anchor,
        /// ForestWindData.ClumpAnchors), also set before Optimize.
        /// </summary>
        public static Mesh ToMesh(MeshData data, string name, bool recalculateTangents, Color32[] colors, Vector4[] uv2)
        {
            Validate(data, nameof(data));
            if (colors != null && colors.Length != data.positions.Length)
                throw new ArgumentException($"{name}: {colors.Length} colours for {data.positions.Length} vertices.", nameof(colors));
            if (uv2 != null && uv2.Length != data.positions.Length)
                throw new ArgumentException($"{name}: {uv2.Length} UV2 values for {data.positions.Length} vertices.", nameof(uv2));

            var mesh = new Mesh { name = name };
            mesh.indexFormat = data.positions.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(data.positions);
            if (data.normals != null) mesh.SetNormals(data.normals);
            if (data.uv0 != null) mesh.SetUVs(0, data.uv0);
            if (uv2 != null) mesh.SetUVs(2, uv2);
            if (colors != null) mesh.SetColors(colors);
            if (data.tangents != null && !recalculateTangents) mesh.SetTangents(data.tangents);

            mesh.subMeshCount = data.subMeshes.Length;
            for (int i = 0; i < data.subMeshes.Length; i++) mesh.SetTriangles(data.subMeshes[i], i, false);

            if (data.normals == null) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (recalculateTangents && data.uv0 != null) mesh.RecalculateTangents();
            // Tree sources are imported without mesh optimisation because only these derived meshes are drawn.
            mesh.Optimize();
            return mesh;
        }

        public static Bounds BoundsOf(MeshData data)
        {
            Validate(data, nameof(data));
            if (data.positions.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);

            Vector3 min = data.positions[0], max = data.positions[0];
            for (int v = 1; v < data.positions.Length; v++)
            {
                min = Vector3.Min(min, data.positions[v]);
                max = Vector3.Max(max, data.positions[v]);
            }
            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return bounds;
        }

        public static int TriangleCount(MeshData data)
        {
            Validate(data, nameof(data));
            int count = 0;
            foreach (int[] indices in data.subMeshes) count += indices.Length / 3;
            return count;
        }

        /// <summary>
        /// Compares a measured box with the manifest's box, which is in the file's right-handed space.
        /// Unity mirrors one horizontal axis on import and which one is not known in advance, so the
        /// rule holds under a mirror of X, of Z, of both or of neither. On success <paramref name="mirror"/>
        /// says which axes came out mirrored: "x", "z", "xz", "none", or "symmetric" when the centre is so
        /// close to an axis that the box cannot tell. On failure it says what did not match.
        /// </summary>
        public static bool MatchesSourceBounds(Bounds measured, Bounds source, out string mirror)   // the rule of section 5.6.1; pure
        {
            Vector3 expected = source.size, found = measured.size;
            float tolerance = Mathf.Max(0.02f * Mathf.Max(expected.x, Mathf.Max(expected.y, expected.z)), 0.01f);

            string problem = null;
            for (int axis = 0; axis < 3 && problem == null; axis++)
            {
                if (!Near(found[axis], expected[axis], tolerance))
                    problem = Mismatch("size " + AxisName(axis), found[axis], expected[axis], tolerance);
            }
            if (problem == null && !Near(measured.min.y, source.min.y, tolerance))
                problem = Mismatch("lowest point", measured.min.y, source.min.y, tolerance);
            if (problem == null && !Near(measured.max.y, source.max.y, tolerance))
                problem = Mismatch("highest point", measured.max.y, source.max.y, tolerance);
            if (problem == null && !Near(Mathf.Abs(measured.center.x), Mathf.Abs(source.center.x), tolerance))
                problem = Mismatch("centre offset on x", Mathf.Abs(measured.center.x), Mathf.Abs(source.center.x), tolerance);
            if (problem == null && !Near(Mathf.Abs(measured.center.z), Mathf.Abs(source.center.z), tolerance))
                problem = Mismatch("centre offset on z", Mathf.Abs(measured.center.z), Mathf.Abs(source.center.z), tolerance);
            if (problem != null)
            {
                mirror = problem;
                return false;
            }

            // An axis says something only when the source box is off centre by more than the tolerance,
            // and a definite answer needs both: one mirrored axis and one unknown axis would otherwise
            // read as two different answers on two prefabs of the same import.
            if (Mathf.Abs(source.center.x) <= tolerance || Mathf.Abs(source.center.z) <= tolerance)
            {
                mirror = "symmetric";
                return true;
            }
            bool mirroredX = measured.center.x * source.center.x < 0f;
            bool mirroredZ = measured.center.z * source.center.z < 0f;
            mirror = mirroredX ? (mirroredZ ? "xz" : "x") : (mirroredZ ? "z" : "none");
            return true;
        }

        /// <summary>
        /// FBX stores UVs per geometry, so a sub-mesh can have a UV channel that carries nothing. It is
        /// usable when the UVs of its own vertices span more than 0.01 on both axes.
        /// </summary>
        public static bool HasUsableUv(MeshData data, int subMeshIndex)                        // the rule of section 5.5; pure
        {
            Validate(data, nameof(data));
            if (subMeshIndex < 0 || subMeshIndex >= data.subMeshes.Length)
                throw new ArgumentOutOfRangeException(nameof(subMeshIndex));
            if (data.uv0 == null || data.uv0.Length == 0) return false;

            int[] indices = data.subMeshes[subMeshIndex];
            if (indices.Length == 0) return false;

            float minU = float.PositiveInfinity, maxU = float.NegativeInfinity;
            float minV = float.PositiveInfinity, maxV = float.NegativeInfinity;
            foreach (int vertex in indices)
            {
                Vector2 uv = data.uv0[vertex];
                if (float.IsNaN(uv.x) || float.IsNaN(uv.y)) return false;
                minU = Mathf.Min(minU, uv.x);
                maxU = Mathf.Max(maxU, uv.x);
                minV = Mathf.Min(minV, uv.y);
                maxV = Mathf.Max(maxV, uv.y);
            }
            return maxU - minU > 0.01f && maxV - minV > 0.01f;
        }

        /// <summary>
        /// Radius of the trunk around the pivot's vertical axis between two heights: the 90th percentile
        /// of the distance from the axis to the wood's surface. The surface is sampled along rays cast
        /// outward from the axis and only the first hit counts, so branches that leave the trunk inside
        /// the band (a young fir has them down to the ground) do not widen the result. Returns the
        /// measurement as it is, 0 when there is no wood in the band; the caller applies its own minimum.
        /// </summary>
        public static float TrunkRadius(MeshData wood, float minHeight, float maxHeight)
        {
            Validate(wood, nameof(wood));
            if (!(maxHeight > minHeight)) throw new ArgumentException("maxHeight must be above minHeight.");

            var hits = new List<float>(TrunkLevels * TrunkDirections);
            var section = new List<Vector4>();
            for (int level = 0; level < TrunkLevels; level++)
            {
                float height = minHeight + (level + 0.5f) * (maxHeight - minHeight) / TrunkLevels;
                section.Clear();
                CrossSection(wood, height, section);
                if (section.Count == 0) continue;

                for (int direction = 0; direction < TrunkDirections; direction++)
                {
                    float angle = (direction + 0.5f) * (2f * Mathf.PI / TrunkDirections);
                    float dx = Mathf.Cos(angle), dz = Mathf.Sin(angle);
                    float nearest = float.PositiveInfinity;
                    foreach (Vector4 segment in section)
                    {
                        // Ray t * (dx, dz) against the segment p + s * e.
                        float ex = segment.z - segment.x, ez = segment.w - segment.y;
                        float denominator = dx * ez - dz * ex;
                        if (Mathf.Abs(denominator) < 1e-12f) continue;
                        float t = (segment.x * ez - segment.y * ex) / denominator;
                        float s = (segment.x * dz - segment.y * dx) / denominator;
                        if (t > 1e-5f && s >= 0f && s <= 1f && t < nearest) nearest = t;
                    }
                    if (!float.IsPositiveInfinity(nearest)) hits.Add(nearest);
                }
            }
            if (hits.Count >= TrunkMinHits) return Percentile(hits, TrunkPercentile);

            // Too little surface around the axis to sample (a trunk that does not enclose its pivot, or a
            // mesh too coarse to cut): fall back to the wood's vertices in the band.
            hits.Clear();
            var seen = new bool[wood.positions.Length];
            foreach (int[] indices in wood.subMeshes)
            {
                foreach (int vertex in indices)
                {
                    if (seen[vertex]) continue;
                    seen[vertex] = true;
                    Vector3 p = wood.positions[vertex];
                    if (p.y >= minHeight && p.y <= maxHeight) hits.Add(Mathf.Sqrt(p.x * p.x + p.z * p.z));
                }
            }
            return hits.Count > 0 ? Percentile(hits, TrunkPercentile) : 0f;
        }

        /// <summary>
        /// How much of the reference's silhouette the candidate fills: both meshes are drawn into a grid
        /// of <paramref name="cellSize"/> cells from two side views (along Z and along X), and the result
        /// is the candidate's covered cells divided by the reference's, averaged over the views. 1 means
        /// the same amount of silhouette; above 1 the candidate is fuller than the reference.
        /// </summary>
        public static float CoverageRatio(MeshData reference, MeshData candidate, float cellSize)   // silhouette cells covered, mean of two side views
        {
            Validate(reference, nameof(reference));
            Validate(candidate, nameof(candidate));
            if (!(cellSize > 0f)) throw new ArgumentOutOfRangeException(nameof(cellSize), "The cell size must be positive.");
            if (TriangleCount(reference) == 0) return 0f;

            // One grid for both meshes, so a cell means the same place in each.
            Bounds a = BoundsOf(reference);
            Vector3 min = a.min, max = a.max;
            if (candidate.positions.Length > 0)
            {
                Bounds b = BoundsOf(candidate);
                min = Vector3.Min(min, b.min);
                max = Vector3.Max(max, b.max);
            }

            double sum = 0.0;
            int views = 0;
            for (int view = 0; view < 2; view++)
            {
                float low = view == 0 ? min.x : min.z, high = view == 0 ? max.x : max.z;
                double cell = cellSize;
                double cells = Math.Ceiling((high - low) / cell + 1.0) * Math.Ceiling((max.y - min.y) / cell + 1.0);
                // A tall fir at a 2 mm cell is 33 million cells. Anything far beyond that is coarsened
                // instead of failing: the ratio barely depends on the last digit of the cell size.
                if (cells > CoverageMaxCells) cell *= Math.Sqrt(cells / CoverageMaxCells) * 1.001;

                int width = (int)Math.Ceiling((high - low) / cell + 1.0);
                int height = (int)Math.Ceiling((max.y - min.y) / cell + 1.0);
                var grid = new ulong[((long)width * height + 63) / 64];

                long referenceCells = Rasterise(reference, view, low, min.y, cell, width, height, grid);
                Array.Clear(grid, 0, grid.Length);
                long candidateCells = Rasterise(candidate, view, low, min.y, cell, width, height, grid);
                if (referenceCells == 0) continue;
                sum += (double)candidateCells / referenceCells;
                views++;
            }
            return views > 0 ? (float)(sum / views) : 0f;
        }

        // ---- For the kit builders ----

        /// <summary>Vertex clustering at the cell size, found by bisection, that leaves at most the target.</summary>
        internal static MeshData ClusterToTarget(MeshData source, int targetTriangles)
        {
            Validate(source, nameof(source));
            if (TriangleCount(source) <= targetTriangles) return Extract(source);

            Vector3 size = BoundsOf(source).size;
            float extent = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (targetTriangles <= 0 || !(extent > 0f)) return Empty(source.subMeshes.Length, source.uv0 != null);

            // The count falls as the cell grows, in small uneven steps. Bisect on a logarithmic scale and
            // keep the fullest result that fits.
            float fine = extent * 1e-5f, coarse = extent * 2f;
            MeshData best = null;
            int bestCount = -1;
            for (int step = 0; step < 48; step++)
            {
                float cell = Mathf.Sqrt(fine * coarse);
                MeshData candidate = ClusterDecimate(source, cell);
                int count = TriangleCount(candidate);
                if (count > targetTriangles)
                {
                    fine = cell;
                }
                else
                {
                    coarse = cell;
                    if (count > bestCount)
                    {
                        best = candidate;
                        bestCount = count;
                    }
                }
                if (bestCount >= targetTriangles * 0.98f || coarse / fine < 1.0005f) break;
            }
            return best ?? Empty(source.subMeshes.Length, source.uv0 != null);
        }

        /// <summary>A mesh with the given number of sub-meshes and nothing in them.</summary>
        internal static MeshData Empty(int subMeshCount, bool withUv)
        {
            var empty = new MeshData
            {
                positions = Array.Empty<Vector3>(),
                normals = Array.Empty<Vector3>(),
                uv0 = withUv ? Array.Empty<Vector2>() : null,
                subMeshes = new int[subMeshCount][],
            };
            for (int i = 0; i < subMeshCount; i++) empty.subMeshes[i] = Array.Empty<int>();
            return empty;
        }

        /// <summary>Area-weighted vertex normals from the triangles (the front of a triangle is its clockwise side).</summary>
        internal static Vector3[] ComputeNormals(MeshData data)
        {
            var normals = new Vector3[data.positions.Length];
            foreach (int[] indices in data.subMeshes)
            {
                for (int t = 0; t + 2 < indices.Length; t += 3)
                {
                    int v0 = indices[t], v1 = indices[t + 1], v2 = indices[t + 2];
                    Vector3 face = Vector3.Cross(data.positions[v1] - data.positions[v0], data.positions[v2] - data.positions[v0]);
                    normals[v0] += face;
                    normals[v1] += face;
                    normals[v2] += face;
                }
            }
            for (int v = 0; v < normals.Length; v++) normals[v] = SafeNormalize(normals[v], Vector3.up);
            return normals;
        }

        internal static double SurfaceArea(MeshData data)
        {
            double area = 0.0;
            foreach (int[] indices in data.subMeshes)
            {
                for (int t = 0; t + 2 < indices.Length; t += 3)
                {
                    Vector3 p0 = data.positions[indices[t]];
                    area += 0.5 * Vector3.Cross(data.positions[indices[t + 1]] - p0, data.positions[indices[t + 2]] - p0).magnitude;
                }
            }
            return area;
        }

        /// <summary>Nearest-rank percentile; sorts the list.</summary>
        internal static float Percentile(List<float> values, float share)
        {
            values.Sort();
            int rank = Mathf.CeilToInt(share * values.Count) - 1;
            return values[Mathf.Clamp(rank, 0, values.Count - 1)];
        }

        /// <summary>Three grid cell numbers in one long; throws when a coordinate is outside the 21 bits a cell has.</summary>
        internal static long CellKey(Vector3 position, double inverseCellSize)
        {
            long x = (long)Math.Floor(position.x * inverseCellSize) + CellBias;
            long y = (long)Math.Floor(position.y * inverseCellSize) + CellBias;
            long z = (long)Math.Floor(position.z * inverseCellSize) + CellBias;
            const long limit = 2L * CellBias;
            if (x < 0 || x >= limit || y < 0 || y >= limit || z < 0 || z >= limit || float.IsNaN(position.x + position.y + position.z))
                throw new ArgumentException(
                    $"Position {position} does not fit a grid of {1.0 / inverseCellSize:G3} m cells: a mesh in metres around its pivot was expected.");
            return (x << 42) | (y << 21) | z;
        }

        // ---- Private ----

        struct AttributeKey : IEquatable<AttributeKey>
        {
            readonly int cell, u, v;

            public AttributeKey(int cell, int u, int v)
            {
                this.cell = cell;
                this.u = u;
                this.v = v;
            }

            public bool Equals(AttributeKey other) => cell == other.cell && u == other.u && v == other.v;
            public override bool Equals(object other) => other is AttributeKey key && Equals(key);
            public override int GetHashCode() => unchecked((cell * 486187739 + u) * 16777619 + v);
        }

        struct TripleKey : IEquatable<TripleKey>
        {
            readonly int low, middle, high;

            public TripleKey(int low, int middle, int high)
            {
                this.low = low;
                this.middle = middle;
                this.high = high;
            }

            public bool Equals(TripleKey other) => low == other.low && middle == other.middle && high == other.high;
            public override bool Equals(object other) => other is TripleKey key && Equals(key);
            public override int GetHashCode() => unchecked((low * 486187739 + middle) * 16777619 + high);
        }

        // The triangles that land on one set of three cells, split by which way round they go.
        struct Triple
        {
            public int evenCount, oddCount;          // source triangles per orientation
            public int evenTriangle, oddTriangle;    // one of them each, to take the shading data from
            public bool evenFaithful, oddFaithful;   // whether any of them still faces the way it did before snapping
        }

        static void Validate(MeshData data, string argument)
        {
            if (data == null) throw new ArgumentNullException(argument);
            if (data.positions == null || data.subMeshes == null)
                throw new ArgumentException("The mesh data has no positions or no sub-mesh list.", argument);
            int count = data.positions.Length;
            if ((data.normals != null && data.normals.Length != count) || (data.tangents != null && data.tangents.Length != count)
                || (data.uv0 != null && data.uv0.Length != count))
                throw new ArgumentException("A vertex attribute does not have one entry per position.", argument);
            foreach (int[] indices in data.subMeshes)
            {
                if (indices == null || indices.Length % 3 != 0)
                    throw new ArgumentException("A sub-mesh is missing or is not a triangle list.", argument);
            }
        }

        static Vector3 SafeNormalize(Vector3 vector, Vector3 fallback)
        {
            float length = vector.magnitude;
            return length > 1e-12f ? vector / length : fallback;
        }

        static int UvBucket(float coordinate)
        {
            if (float.IsNaN(coordinate)) return 0;
            // Tiling UVs can be far outside 0..1; the clamp only keeps the cast defined.
            return (int)Math.Floor(Math.Max(-1.0e6, Math.Min(1.0e6, coordinate)) * 64.0);
        }

        static bool Near(float found, float expected, float tolerance) => Mathf.Abs(found - expected) <= tolerance;

        static string AxisName(int axis) => axis == 0 ? "x" : axis == 1 ? "y" : "z";

        static string Mismatch(string what, float found, float expected, float tolerance)
        {
            return FormattableString.Invariant($"{what} is {found:0.###} m, the manifest says {expected:0.###} m (tolerance {tolerance:0.###} m)");
        }

        // Uses Unity's Mesh LOD generator as an offline decimator: generate on a throw-away mesh, take the
        // first level that fits, and copy out only the vertices that level uses. Null when it cannot.
        static MeshData GenerateWithUnity(MeshData source, int targetTriangles)
        {
            Mesh scratch = null;
            try
            {
                scratch = ToMesh(source, "forest-lod-scratch", false);
                MeshLodUtility.GenerateMeshLods(scratch, -1);
                int levels = scratch.lodCount;
                for (int level = 1; level < levels; level++)
                {
                    var subMeshes = new int[scratch.subMeshCount][];
                    int triangles = 0;
                    for (int s = 0; s < subMeshes.Length; s++)
                    {
                        subMeshes[s] = scratch.GetTriangles(s, level, true);
                        triangles += subMeshes[s].Length / 3;
                    }
                    if (triangles == 0 || triangles > targetTriangles) continue;

                    // The generator adds no vertices: every level indexes the vertex buffer of level 0.
                    Vector3[] normals = scratch.normals;
                    Vector2[] uv = scratch.uv;
                    var level0 = new MeshData
                    {
                        positions = scratch.vertices,
                        normals = normals.Length == scratch.vertexCount ? normals : null,
                        uv0 = uv.Length == scratch.vertexCount ? uv : null,
                        subMeshes = subMeshes,
                    };
                    return Extract(level0);
                }
                return null;
            }
            catch (Exception exception)
            {
                // Whether the generator accepts a script-built mesh is what probe P4 answers. Clustering
                // is the default that works, so a refusal here is reported and not fatal.
                Debug.LogWarning("Forest kit: MeshLodUtility.GenerateMeshLods failed: " + exception.Message);
                return null;
            }
            finally
            {
                if (scratch != null) UnityEngine.Object.DestroyImmediate(scratch);
            }
        }

        // Where the plane y = height cuts the mesh: one segment per crossing triangle, as (x0, z0, x1, z1).
        static void CrossSection(MeshData mesh, float height, List<Vector4> segments)
        {
            foreach (int[] indices in mesh.subMeshes)
            {
                for (int t = 0; t + 2 < indices.Length; t += 3)
                {
                    Vector3 p0 = mesh.positions[indices[t]], p1 = mesh.positions[indices[t + 1]], p2 = mesh.positions[indices[t + 2]];
                    bool above0 = p0.y >= height, above1 = p1.y >= height, above2 = p2.y >= height;
                    if (above0 == above1 && above1 == above2) continue;

                    // Exactly two edges cross. The corner that is alone on its side belongs to both.
                    Vector3 alone, other1, other2;
                    if (above0 != above1 && above0 != above2) { alone = p0; other1 = p1; other2 = p2; }
                    else if (above1 != above0 && above1 != above2) { alone = p1; other1 = p0; other2 = p2; }
                    else { alone = p2; other1 = p0; other2 = p1; }

                    Vector3 first = Vector3.Lerp(alone, other1, (height - alone.y) / (other1.y - alone.y));
                    Vector3 second = Vector3.Lerp(alone, other2, (height - alone.y) / (other2.y - alone.y));
                    segments.Add(new Vector4(first.x, first.z, second.x, second.z));
                }
            }
        }

        // Marks every grid cell a triangle's side view touches and returns how many cells are marked.
        // View 0 looks along Z (across = X), view 1 along X (across = Z). Any overlap counts, so a card
        // seen edge-on still leaves its line.
        static long Rasterise(MeshData mesh, int view, float acrossMin, float upMin, double cell, int width, int height, ulong[] grid)
        {
            long marked = 0;
            double inverse = 1.0 / cell;
            foreach (int[] indices in mesh.subMeshes)
            {
                for (int t = 0; t + 2 < indices.Length; t += 3)
                {
                    Vector3 p0 = mesh.positions[indices[t]], p1 = mesh.positions[indices[t + 1]], p2 = mesh.positions[indices[t + 2]];
                    double u0 = ((view == 0 ? p0.x : p0.z) - acrossMin) * inverse, v0 = (p0.y - upMin) * inverse;
                    double u1 = ((view == 0 ? p1.x : p1.z) - acrossMin) * inverse, v1 = (p1.y - upMin) * inverse;
                    double u2 = ((view == 0 ? p2.x : p2.z) - acrossMin) * inverse, v2 = (p2.y - upMin) * inverse;

                    int firstRow = Math.Max(0, (int)Math.Floor(Math.Min(v0, Math.Min(v1, v2))));
                    int lastRow = Math.Min(height - 1, (int)Math.Floor(Math.Max(v0, Math.Max(v1, v2))));
                    for (int row = firstRow; row <= lastRow; row++)
                    {
                        // The part of the triangle inside this row is a convex polygon whose corners all lie
                        // on the triangle's edges, so the clipped edges give its extent.
                        double from = double.PositiveInfinity, to = double.NegativeInfinity;
                        ClipEdge(u0, v0, u1, v1, row, row + 1.0, ref from, ref to);
                        ClipEdge(u1, v1, u2, v2, row, row + 1.0, ref from, ref to);
                        ClipEdge(u2, v2, u0, v0, row, row + 1.0, ref from, ref to);
                        if (from > to) continue;

                        int firstColumn = Math.Max(0, (int)Math.Floor(from));
                        int lastColumn = Math.Min(width - 1, (int)Math.Floor(to));
                        long bit = (long)row * width + firstColumn;
                        for (int column = firstColumn; column <= lastColumn; column++, bit++)
                        {
                            ulong mask = 1UL << (int)(bit & 63);
                            if ((grid[bit >> 6] & mask) != 0) continue;
                            grid[bit >> 6] |= mask;
                            marked++;
                        }
                    }
                }
            }
            return marked;
        }

        // Extends [from, to] by the across-extent of the part of an edge that lies in the band low..high.
        static void ClipEdge(double pu, double pv, double qu, double qv, double low, double high, ref double from, ref double to)
        {
            if (pv > qv)
            {
                double swap = pu; pu = qu; qu = swap;
                swap = pv; pv = qv; qv = swap;
            }
            if (qv < low || pv > high) return;

            double rise = qv - pv;
            double start = pu, end = qu;
            if (rise > 1e-12)
            {
                if (pv < low) start = pu + (qu - pu) * ((low - pv) / rise);
                if (qv > high) end = pu + (qu - pu) * ((high - pv) / rise);
            }
            from = Math.Min(from, Math.Min(start, end));
            to = Math.Max(to, Math.Max(start, end));
        }
    }
}
