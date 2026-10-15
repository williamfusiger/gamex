using System;
using System.Collections.Generic;
using System.IO;
using Gamex.EditorTools;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gamex.Tests.Forest
{
    /// <summary>
    /// Shared by the forest EditMode tests: opening the scene (or ignoring the test when it has not
    /// been built), the prefab id lists, and synthetic meshes for the pure mesh code.
    ///
    /// A test whose content does not exist yet calls Assert.Ignore, never Assert.Inconclusive: Unity's
    /// command-line runner counts an inconclusive test as a failed run.
    /// </summary>
    static class ForestTestSupport
    {
        // ---- The scene ----

        /// <summary>Opens the forest scene unless it is the open one. Ignores the calling test when it has not been built.</summary>
        public static Scene RequireForestScene()
        {
            if (!File.Exists(ForestPaths.ScenePath))
                Assert.Ignore($"{ForestPaths.ScenePath} has not been built yet (Gamex/Forest/3 Rebuild Forest Scene).");

            Scene active = SceneManager.GetActiveScene();
            if (active.path != ForestPaths.ScenePath || SceneManager.sceneCount != 1 || active.isDirty)
                active = EditorSceneManager.OpenScene(ForestPaths.ScenePath, OpenSceneMode.Single);
            return active;
        }

        public static GameObject Root(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == name)
                    return root;
            return null;
        }

        public static GameObject RequireRoot(Scene scene, string name)
        {
            GameObject root = Root(scene, name);
            Assert.IsNotNull(root, $"The forest scene has no root object '{name}'.");
            return root;
        }

        public static Transform RequireChild(GameObject root, string path)
        {
            Transform child = root.transform.Find(path);
            Assert.IsNotNull(child, $"The forest scene has no object '{root.name}/{path}'.");
            return child;
        }

        public static Terrain RequireTerrain(Scene scene)
        {
            var terrain = RequireChild(RequireRoot(scene, "Environment"), "Terrain").GetComponent<Terrain>();
            Assert.IsNotNull(terrain, "Environment/Terrain has no Terrain component.");
            return terrain;
        }

        /// <summary>World height of the terrain surface: SampleHeight is relative to the terrain's own position.</summary>
        public static float GroundHeight(Terrain terrain, float x, float z)
        {
            return terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
        }

        // ---- The kit ----

        public static readonly string[][] PrefabGroups =
        {
            ForestPrefabIds.TallFirs, ForestPrefabIds.YoungFirs, ForestPrefabIds.Boulders, ForestPrefabIds.Logs, ForestPrefabIds.Stumps,
            ForestPrefabIds.Branches, ForestPrefabIds.FernPatches, ForestPrefabIds.FernClumps, ForestPrefabIds.GrassPatches,
        };

        public static IEnumerable<string> AllPrefabIds()
        {
            foreach (string[] group in PrefabGroups)
                foreach (string prefabId in group)
                    yield return prefabId;
        }

        public static bool IsTree(string prefabId) => Contains(ForestPrefabIds.TallFirs, prefabId) || Contains(ForestPrefabIds.YoungFirs, prefabId);

        /// <summary>Patches are merged from clumps by our own code and have no object in the manifest.</summary>
        public static bool IsPatch(string prefabId) => Contains(ForestPrefabIds.FernPatches, prefabId) || Contains(ForestPrefabIds.GrassPatches, prefabId);

        /// <summary>Rocks, logs and stump patches: the props that carry a mesh collider.</summary>
        public static bool HasMeshCollider(string prefabId)
        {
            return Contains(ForestPrefabIds.Boulders, prefabId) || Contains(ForestPrefabIds.Logs, prefabId) || Contains(ForestPrefabIds.Stumps, prefabId);
        }

        public static bool Contains(string[] ids, string id) => Array.IndexOf(ids, id) >= 0;

        /// <summary>The prefabs that exist. Ignores the calling test when none does.</summary>
        public static List<string> RequireBuiltPrefabs(Predicate<string> filter, string what)
        {
            var built = new List<string>();
            foreach (string prefabId in AllPrefabIds())
                if (filter(prefabId) && ForestPrefabs.Exists(prefabId))
                    built.Add(prefabId);
            if (built.Count == 0)
                Assert.Ignore($"No {what} has been built yet (Gamex/Forest/2 Build Kit).");
            return built;
        }

        public static int TriangleCount(Mesh mesh)
        {
            long indices = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
                indices += mesh.GetIndexCount(i);
            return (int)(indices / 3);
        }

        // ---- Synthetic meshes ----

        /// <summary>A hash that is the same on every machine and every run.</summary>
        public static float Hash01(int a, int b)
        {
            unchecked
            {
                uint h = (uint)a * 374761393u + (uint)b * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        /// <summary>
        /// Separate rectangular cards on a lattice around a trunk at the origin, like the twig cards of a
        /// scanned fir: no two cards share a vertex, and every card has its own corner of a texture atlas.
        /// Each card is a strip of <paramref name="segments"/> quads.
        /// </summary>
        public sealed class CardSet
        {
            public ForestMeshTools.MeshData mesh;
            public int cards, segments;
            public Vector3[] centres;            // per card
            public Vector3[][] corners;          // per card: its four outer corners
            public Vector2[][] cornerUvs;        // per card: the UV of each corner
            public float[] areas;                // per card
            public float totalArea;
            public float spacing;
            public Vector3 origin;               // centre of card (0, 0, 0)
            public int countX, countY, countZ;

            /// <summary>The card whose lattice point is closest to a position.</summary>
            public int CardAt(Vector3 position)
            {
                int x = Mathf.Clamp(Mathf.RoundToInt((position.x - origin.x) / spacing), 0, countX - 1);
                int y = Mathf.Clamp(Mathf.RoundToInt((position.y - origin.y) / spacing), 0, countY - 1);
                int z = Mathf.Clamp(Mathf.RoundToInt((position.z - origin.z) / spacing), 0, countZ - 1);
                return (y * countZ + z) * countX + x;
            }
        }

        public static CardSet Cards(int countX, int countY, int countZ, float spacing, float length, float width, int segments)
        {
            int cards = countX * countY * countZ;
            int verticesPerCard = 2 * (segments + 1);
            var set = new CardSet
            {
                cards = cards, segments = segments, spacing = spacing, countX = countX, countY = countY, countZ = countZ,
                origin = new Vector3(-0.5f * (countX - 1) * spacing, 0.5f, -0.5f * (countZ - 1) * spacing),
                centres = new Vector3[cards], corners = new Vector3[cards][], cornerUvs = new Vector2[cards][], areas = new float[cards],
            };
            var positions = new Vector3[cards * verticesPerCard];
            var normals = new Vector3[positions.Length];
            var uvs = new Vector2[positions.Length];
            var indices = new int[cards * segments * 6];

            for (int card = 0; card < cards; card++)
            {
                int x = card % countX, z = card / countX % countZ, y = card / (countX * countZ);
                Vector3 centre = set.origin + new Vector3(x, y, z) * spacing;
                // Every card has its own orientation and size, so areas and fits differ from card to card.
                // The frame is built from sines and cosines, so this needs nothing from the engine.
                float yaw = Hash01(card, 1) * 2f * Mathf.PI, pitch = (Hash01(card, 2) - 0.5f) * Mathf.PI * 0.9f, roll = Hash01(card, 3) * 2f * Mathf.PI;
                var forward = new Vector3(Mathf.Cos(pitch) * Mathf.Cos(yaw), Mathf.Sin(pitch), Mathf.Cos(pitch) * Mathf.Sin(yaw));
                Vector3 side = new Vector3(-Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                Vector3 up = Vector3.Cross(forward, side);
                float cardLength = length * Mathf.Lerp(0.7f, 1f, Hash01(card, 4));
                float cardWidth = width * Mathf.Lerp(0.7f, 1f, Hash01(card, 5));
                Vector3 along = forward * cardLength;
                Vector3 across = (side * Mathf.Cos(roll) + up * Mathf.Sin(roll)) * cardWidth;
                Vector3 normal = Vector3.Cross(forward, across).normalized;

                // One of 8 x 8 atlas tiles, with a margin, as a scanned twig atlas is laid out.
                int tile = (int)(Hash01(card, 6) * 64f) % 64;
                var uvMin = new Vector2((tile % 8 + 0.1f) / 8f, (tile / 8 + 0.1f) / 8f);
                var uvSize = new Vector2(0.8f / 8f, 0.8f / 8f);

                int first = card * verticesPerCard;
                for (int s = 0; s <= segments; s++)
                {
                    float t = s / (float)segments;
                    positions[first + 2 * s] = centre + along * (t - 0.5f) - across * 0.5f;
                    positions[first + 2 * s + 1] = centre + along * (t - 0.5f) + across * 0.5f;
                    uvs[first + 2 * s] = uvMin + new Vector2(uvSize.x * t, 0f);
                    uvs[first + 2 * s + 1] = uvMin + new Vector2(uvSize.x * t, uvSize.y);
                    normals[first + 2 * s] = normals[first + 2 * s + 1] = normal;
                }
                for (int s = 0; s < segments; s++)
                {
                    int a = first + 2 * s, at = (card * segments + s) * 6;
                    indices[at] = a; indices[at + 1] = a + 1; indices[at + 2] = a + 2;
                    indices[at + 3] = a + 2; indices[at + 4] = a + 1; indices[at + 5] = a + 3;
                }

                int last = first + 2 * segments;
                set.centres[card] = centre;
                set.corners[card] = new[] { positions[first], positions[first + 1], positions[last], positions[last + 1] };
                set.cornerUvs[card] = new[] { uvs[first], uvs[first + 1], uvs[last], uvs[last + 1] };
                set.areas[card] = cardLength * cardWidth;
                set.totalArea += set.areas[card];
            }

            set.mesh = new ForestMeshTools.MeshData { positions = positions, normals = normals, uv0 = uvs, subMeshes = new[] { indices } };
            return set;
        }

        public static float SurfaceArea(ForestMeshTools.MeshData mesh)
        {
            double area = 0.0;
            foreach (int[] indices in mesh.subMeshes)
            {
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    Vector3 a = mesh.positions[indices[i]], b = mesh.positions[indices[i + 1]], c = mesh.positions[indices[i + 2]];
                    area += 0.5 * Vector3.Cross(b - a, c - a).magnitude;
                }
            }
            return (float)area;
        }

        /// <summary>A closed sphere made by subdividing an icosahedron: 20 * 4^n triangles, every edge shared by exactly two.</summary>
        public static ForestMeshTools.MeshData Icosphere(int subdivisions, float radius)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var vertices = new List<Vector3>
            {
                new Vector3(-1f, t, 0f), new Vector3(1f, t, 0f), new Vector3(-1f, -t, 0f), new Vector3(1f, -t, 0f),
                new Vector3(0f, -1f, t), new Vector3(0f, 1f, t), new Vector3(0f, -1f, -t), new Vector3(0f, 1f, -t),
                new Vector3(t, 0f, -1f), new Vector3(t, 0f, 1f), new Vector3(-t, 0f, -1f), new Vector3(-t, 0f, 1f),
            };
            for (int i = 0; i < vertices.Count; i++)
                vertices[i] = vertices[i].normalized;
            var triangles = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };

            for (int pass = 0; pass < subdivisions; pass++)
            {
                var midpoints = new Dictionary<long, int>();
                var next = new List<int>(triangles.Count * 4);
                for (int i = 0; i < triangles.Count; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    int ab = Midpoint(vertices, midpoints, a, b), bc = Midpoint(vertices, midpoints, b, c), ca = Midpoint(vertices, midpoints, c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                triangles = next;
            }

            var positions = new Vector3[vertices.Count];
            var normals = new Vector3[vertices.Count];
            var uvs = new Vector2[vertices.Count];
            for (int i = 0; i < positions.Length; i++)
            {
                normals[i] = vertices[i];
                positions[i] = vertices[i] * radius;
                uvs[i] = new Vector2(Mathf.Atan2(vertices[i].z, vertices[i].x) / (2f * Mathf.PI) + 0.5f, Mathf.Asin(Mathf.Clamp(vertices[i].y, -1f, 1f)) / Mathf.PI + 0.5f);
            }
            return new ForestMeshTools.MeshData { positions = positions, normals = normals, uv0 = uvs, subMeshes = new[] { triangles.ToArray() } };
        }

        static int Midpoint(List<Vector3> vertices, Dictionary<long, int> midpoints, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (midpoints.TryGetValue(key, out int index))
                return index;
            vertices.Add(((vertices[a] + vertices[b]) * 0.5f).normalized);
            midpoints[key] = vertices.Count - 1;
            return vertices.Count - 1;
        }

        /// <summary>Joins failure lines into one assertion, so a run shows every violation and not only the first.</summary>
        public static void AssertNone(List<string> problems, string what)
        {
            if (problems.Count == 0)
                return;
            const int shown = 25;
            string more = problems.Count > shown ? $"\n  ... and {problems.Count - shown} more" : "";
            Assert.Fail($"{problems.Count} {what}:\n  " + string.Join("\n  ", problems.GetRange(0, Mathf.Min(shown, problems.Count))) + more);
        }
    }
}
