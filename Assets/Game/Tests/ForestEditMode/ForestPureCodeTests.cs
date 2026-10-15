using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Gamex.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Gamex.Tests.Forest
{
    /// <summary>
    /// Tests of the forest's pure code: the manifest, the mesh tools, the foliage thinner, the sky
    /// integral and the image statistics. None needs a download, an import or a scene, so they run from
    /// the first milestone on. They are written against the public surface only.
    /// </summary>
    [Category("Forest")]
    public class ForestPureCodeTests
    {
        const int Seed = 1993;

        // ---- Manifest ----

        [Test]
        public void Manifest_LoadsAndPathsAreUnique()
        {
            ForestAssetEntry[] all = ForestManifest.All;
            Assert.IsNotNull(all);
            Assert.Greater(all.Length, 0, "The manifest holds no assets.");

            var problems = new List<string>();
            var ids = new HashSet<string>();
            var paths = new HashSet<string>();
            foreach (ForestAssetEntry entry in all)
            {
                if (string.IsNullOrEmpty(entry.id) || !ids.Add(entry.id))
                    problems.Add($"asset id '{entry.id}' is empty or used twice");
                if (entry.kind != "model" && entry.kind != "texture" && entry.kind != "hdri")
                    problems.Add($"{entry.id}: kind '{entry.kind}' is not model, texture or hdri");
                if (entry.files == null || entry.files.Length == 0)
                {
                    problems.Add($"{entry.id}: no files");
                    continue;
                }
                foreach (ForestAssetFile file in entry.files)
                {
                    if (string.IsNullOrEmpty(file.relPath) || !paths.Add(file.relPath))
                        problems.Add($"{entry.id}: relPath '{file.relPath}' is empty or used twice");
                    if (string.IsNullOrEmpty(file.url) || file.bytes <= 0 || file.md5 == null || file.md5.Length != 32)
                        problems.Add($"{entry.id}: {file.relPath} lacks a url, a byte size or an md5");
                }

                if (entry.kind != "model")
                    continue;
                if (entry.boundsSpace != "source-rh-yup")
                    problems.Add($"{entry.id}: boundsSpace is '{entry.boundsSpace}', expected source-rh-yup");
                if (entry.objectsSource != "fbx" && entry.objectsSource != "gltf")
                    problems.Add($"{entry.id}: objectsSource is '{entry.objectsSource}', expected fbx or gltf");

                var materials = new HashSet<string>();
                foreach (ForestMaterialEntry material in entry.materials ?? Array.Empty<ForestMaterialEntry>())
                    materials.Add(material.name);
                foreach (ForestMaterialEntry material in entry.materials ?? Array.Empty<ForestMaterialEntry>())
                {
                    bool reuses = !string.IsNullOrEmpty(material.sameAs);
                    if (reuses && (material.sameAs == material.name || !materials.Contains(material.sameAs)))
                        problems.Add($"{entry.id}: material {material.name} is sameAs '{material.sameAs}', which is not another material of this asset");
                    if (!string.IsNullOrEmpty(material.noUvFallback) && (material.noUvFallback == material.name || !materials.Contains(material.noUvFallback)))
                        problems.Add($"{entry.id}: material {material.name} falls back to '{material.noUvFallback}', which is not another material of this asset");
                    if (!reuses && material.surface != "opaque" && material.surface != "foliage")
                        problems.Add($"{entry.id}: material {material.name} has surface '{material.surface}'");
                }

                if (entry.objects == null || entry.objects.Length == 0)
                    problems.Add($"{entry.id}: a model without objects");
                foreach (ForestObjectEntry item in entry.objects ?? Array.Empty<ForestObjectEntry>())
                {
                    if (item.boundsMin == null || item.boundsMin.Length != 3 || item.boundsMax == null || item.boundsMax.Length != 3)
                        problems.Add($"{item.name}: bounds are not two triples");
                    int triangles = 0;
                    foreach (ForestSubMeshEntry sub in item.subMeshes ?? Array.Empty<ForestSubMeshEntry>())
                    {
                        triangles += sub.triangles;
                        if (!materials.Contains(sub.material))
                            problems.Add($"{item.name}: sub-mesh material '{sub.material}' is not a material of {entry.id}");
                    }
                    // The fallback mapping of sub-meshes goes by these counts, so they must add up.
                    if (triangles != item.triangles)
                        problems.Add($"{item.name}: sub-meshes hold {triangles} triangles, the object says {item.triangles}");
                }
            }

            foreach (string prefabId in ForestTestSupport.AllPrefabIds())
            {
                ForestObjectEntry item = ForestManifest.FindObjectForPrefab(prefabId);
                if (ForestTestSupport.IsPatch(prefabId))
                {
                    if (item != null)
                        problems.Add($"patch prefab {prefabId} resolves to manifest object {item.name}; patches have none");
                }
                else if (item == null)
                {
                    problems.Add($"prefab id {prefabId} does not resolve to a manifest object");
                }
            }
            ForestTestSupport.AssertNone(problems, "manifest problem(s)");
        }

        // ---- Mesh tools ----

        [Test]
        public void MeshTools_BoundsRuleIsMirrorIndependent()
        {
            var problems = new List<string>();
            int objects = 0;
            foreach (ForestAssetEntry entry in ForestManifest.All)
            {
                foreach (ForestObjectEntry item in entry.objects ?? Array.Empty<ForestObjectEntry>())
                {
                    objects++;
                    Bounds source = ForestManifest.SourceBounds(item);

                    // What an importer may do: mirror X, mirror Z, both, or neither.
                    foreach (float signX in new[] { 1f, -1f })
                    {
                        foreach (float signZ in new[] { 1f, -1f })
                        {
                            var mirrored = new Bounds(new Vector3(source.center.x * signX, source.center.y, source.center.z * signZ), source.size);
                            if (!ForestMeshTools.MatchesSourceBounds(mirrored, source, out string why))
                                problems.Add($"{item.name}: rejected under mirror ({signX:0}, {signZ:0}): {why}");
                        }
                    }

                    // What must never pass. Offsets go away from the pivot, so a mirror cannot cancel them.
                    float awayX = source.center.x >= 0f ? 5f : -5f, awayZ = source.center.z >= 0f ? 5f : -5f;
                    var wrong = new[]
                    {
                        new KeyValuePair<string, Bounds>("offset 5 m in X", new Bounds(source.center + new Vector3(awayX, 0f, 0f), source.size)),
                        new KeyValuePair<string, Bounds>("offset 5 m in Z", new Bounds(source.center + new Vector3(0f, 0f, awayZ), source.size)),
                        new KeyValuePair<string, Bounds>("offset 5 m in Y", new Bounds(source.center + new Vector3(0f, 5f, 0f), source.size)),
                        new KeyValuePair<string, Bounds>("scaled by 100", new Bounds(source.center * 100f, source.size * 100f)),
                        new KeyValuePair<string, Bounds>("scaled by 0.01", new Bounds(source.center * 0.01f, source.size * 0.01f)),
                        new KeyValuePair<string, Bounds>("Y and Z swapped", new Bounds(
                            new Vector3(source.center.x, source.center.z, source.center.y), new Vector3(source.size.x, source.size.z, source.size.y))),
                    };
                    foreach (KeyValuePair<string, Bounds> pair in wrong)
                        if (ForestMeshTools.MatchesSourceBounds(pair.Value, source, out _))
                            problems.Add($"{item.name}: accepted although {pair.Key}");
                }
            }
            Assert.Greater(objects, 0, "The manifest holds no objects to test the rule on.");
            ForestTestSupport.AssertNone(problems, "wrong answer(s) of the bounds rule");
        }

        [Test]
        public void MeshTools_UsableUvRule()
        {
            Vector3[] positions = { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f) };
            Vector3[] normals = { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            int[][] quad = { new[] { 0, 2, 1, 0, 3, 2 } };

            var unit = new ForestMeshTools.MeshData
            {
                positions = positions, normals = normals, subMeshes = quad,
                uv0 = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) },
            };
            Assert.IsTrue(ForestMeshTools.HasUsableUv(unit, 0), "A unit quad has usable UVs.");

            var collapsed = new ForestMeshTools.MeshData
            {
                positions = positions, normals = normals, subMeshes = quad,
                uv0 = new[] { new Vector2(0.3f, 0.7f), new Vector2(0.3f, 0.7f), new Vector2(0.3f, 0.7f), new Vector2(0.3f, 0.7f) },
            };
            Assert.IsFalse(ForestMeshTools.HasUsableUv(collapsed, 0), "UVs that are all equal carry nothing.");

            var line = new ForestMeshTools.MeshData
            {
                positions = positions, normals = normals, subMeshes = quad,
                uv0 = new[] { new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f) },
            };
            Assert.IsFalse(ForestMeshTools.HasUsableUv(line, 0), "UVs that span one axis only are not usable.");

            var none = new ForestMeshTools.MeshData { positions = positions, normals = normals, subMeshes = quad, uv0 = null };
            Assert.IsFalse(ForestMeshTools.HasUsableUv(none, 0), "A mesh without a UV channel has no usable UVs.");

            // The rule is per sub-mesh: one with real UVs beside one whose vertices all share a UV.
            var two = new ForestMeshTools.MeshData
            {
                positions = new[]
                {
                    positions[0], positions[1], positions[2], positions[3],
                    new Vector3(2f, 0f, 0f), new Vector3(3f, 0f, 0f), new Vector3(3f, 1f, 0f), new Vector3(2f, 1f, 0f),
                },
                normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back, Vector3.back, Vector3.back, Vector3.back, Vector3.back },
                uv0 = new[]
                {
                    new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero,
                },
                subMeshes = new[] { new[] { 0, 2, 1, 0, 3, 2 }, new[] { 4, 6, 5, 4, 7, 6 } },
            };
            Assert.IsTrue(ForestMeshTools.HasUsableUv(two, 0));
            Assert.IsFalse(ForestMeshTools.HasUsableUv(two, 1), "The second sub-mesh references only vertices with one UV.");
        }

        [Test]
        public void MeshTools_ClusterDecimateKeepsSurfaceClosed()
        {
            ForestMeshTools.MeshData sphere = ForestTestSupport.Icosphere(4, 1f);
            Assert.AreEqual(0, BoundaryEdges(sphere), "The test sphere itself must be closed.");
            int sourceTriangles = ForestMeshTools.TriangleCount(sphere);

            foreach (float cell in new[] { 0.12f, 0.25f, 0.45f })
            {
                ForestMeshTools.MeshData low = ForestMeshTools.ClusterDecimate(sphere, cell);
                int triangles = ForestMeshTools.TriangleCount(low);
                Assert.Greater(triangles, 0, $"Cell {cell}: nothing is left of the sphere.");
                Assert.Less(triangles, sourceTriangles, $"Cell {cell}: clustering did not reduce the sphere.");
                Assert.AreEqual(0, BoundaryEdges(low), $"Cell {cell}: the clustered sphere has open edges.");

                Bounds source = ForestMeshTools.BoundsOf(sphere), result = ForestMeshTools.BoundsOf(low);
                const float slack = 1e-4f;
                Assert.IsTrue(result.min.x >= source.min.x - slack && result.min.y >= source.min.y - slack && result.min.z >= source.min.z - slack
                    && result.max.x <= source.max.x + slack && result.max.y <= source.max.y + slack && result.max.z <= source.max.z + slack,
                    $"Cell {cell}: the clustered sphere {result} reaches outside the source {source}.");
            }
        }

        // Edges that belong to exactly one triangle, with vertices welded by position: texture seams
        // split vertices without opening the surface.
        static int BoundaryEdges(ForestMeshTools.MeshData mesh)
        {
            var welded = new Dictionary<Vector3Int, int>();
            var weldedIndex = new int[mesh.positions.Length];
            for (int i = 0; i < mesh.positions.Length; i++)
            {
                Vector3 p = mesh.positions[i];
                var key = new Vector3Int(Mathf.RoundToInt(p.x * 1e5f), Mathf.RoundToInt(p.y * 1e5f), Mathf.RoundToInt(p.z * 1e5f));
                if (!welded.TryGetValue(key, out int index))
                    welded[key] = index = welded.Count;
                weldedIndex[i] = index;
            }

            var uses = new Dictionary<long, int>();
            foreach (int[] indices in mesh.subMeshes)
            {
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    int a = weldedIndex[indices[i]], b = weldedIndex[indices[i + 1]], c = weldedIndex[indices[i + 2]];
                    Assert.IsTrue(a != b && b != c && c != a, "A degenerate triangle was left in the mesh.");
                    CountEdge(uses, a, b);
                    CountEdge(uses, b, c);
                    CountEdge(uses, c, a);
                }
            }

            int open = 0;
            foreach (int count in uses.Values)
                if (count == 1)
                    open++;
            return open;
        }

        static void CountEdge(Dictionary<long, int> uses, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            uses.TryGetValue(key, out int count);
            uses[key] = count + 1;
        }

        // ---- Foliage thinner ----

        // 10,000 separate cards of two triangles each, 10 cm apart, around a trunk at the origin.
        static ForestTestSupport.CardSet TwigCards() => ForestTestSupport.Cards(25, 16, 25, 0.1f, 0.06f, 0.03f, 1);

        [Test]
        public void Thinner_FindsPiecesOnSyntheticCards()
        {
            ForestTestSupport.CardSet cards = TwigCards();
            int[] triangles = cards.mesh.subMeshes[0];
            ForestFoliageThinner.Pieces pieces = ForestFoliageThinner.FindPieces(cards.mesh.positions, triangles, Vector3.zero, Seed);

            Assert.AreEqual(10000, cards.cards);
            Assert.AreEqual(cards.cards, pieces.count, "Every separate card is one piece.");
            Assert.IsFalse(pieces.usedSpatialFallback, "10,000 small pieces need no spatial fallback.");
            Assert.AreEqual(triangles.Length / 3, pieces.pieceOfTriangle.Length);
            Assert.AreEqual(pieces.count, pieces.area.Length);
            Assert.AreEqual(pieces.count, pieces.centroid.Length);
            Assert.AreEqual(pieces.count, pieces.anchor.Length);
            Assert.AreEqual(pieces.count, pieces.key.Length);
            Assert.AreEqual(cards.totalArea, pieces.totalArea, cards.totalArea * 0.001f, "The pieces must hold all of the foliage area.");

            // The two triangles of a card are one piece, and no two cards share one.
            var pieceOfCard = new int[cards.cards];
            var seen = new HashSet<int>();
            for (int card = 0; card < cards.cards; card++)
            {
                Assert.AreEqual(pieces.pieceOfTriangle[2 * card], pieces.pieceOfTriangle[2 * card + 1], $"Card {card} was split.");
                pieceOfCard[card] = pieces.pieceOfTriangle[2 * card];
                Assert.IsTrue(seen.Add(pieceOfCard[card]), $"Card {card} shares its piece with another card.");
            }
        }

        [Test]
        public void Thinner_LodsAreNested()
        {
            ForestTestSupport.CardSet cards = TwigCards();
            ForestFoliageThinner.Pieces pieces = ForestFoliageThinner.FindPieces(cards.mesh.positions, cards.mesh.subMeshes[0], Vector3.zero, Seed);

            // With a scale cap of 1 a kept card stays where it was, so the output says which cards were kept.
            HashSet<int> large = KeptCards(cards, ForestFoliageThinner.Thin(cards.mesh, pieces, 8000, 1f, false));
            HashSet<int> middle = KeptCards(cards, ForestFoliageThinner.Thin(cards.mesh, pieces, 2000, 1f, false));
            HashSet<int> small = KeptCards(cards, ForestFoliageThinner.Thin(cards.mesh, pieces, 400, 1f, false));

            Assert.Greater(small.Count, 0, "The smallest level kept nothing.");
            Assert.Less(small.Count, middle.Count);
            Assert.Less(middle.Count, large.Count);
            Assert.IsTrue(small.IsSubsetOf(middle), "A card kept at the smallest target is missing at the middle one.");
            Assert.IsTrue(middle.IsSubsetOf(large), "A card kept at the middle target is missing at the largest one.");
        }

        static HashSet<int> KeptCards(ForestTestSupport.CardSet cards, ForestMeshTools.MeshData thinned)
        {
            var kept = new HashSet<int>();
            foreach (int[] indices in thinned.subMeshes)
            {
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    Vector3 centroid = (thinned.positions[indices[i]] + thinned.positions[indices[i + 1]] + thinned.positions[indices[i + 2]]) / 3f;
                    int card = cards.CardAt(centroid);
                    Assert.Less(Vector3.Distance(centroid, cards.centres[card]), 0.03f, "An output triangle is not where any input card was.");
                    kept.Add(card);
                }
            }
            return kept;
        }

        [Test]
        public void Thinner_MeetsTargetAndPreservesArea()
        {
            ForestTestSupport.CardSet cards = TwigCards();
            ForestFoliageThinner.Pieces pieces = ForestFoliageThinner.FindPieces(cards.mesh.positions, cards.mesh.subMeshes[0], Vector3.zero, Seed);
            float sourceArea = ForestTestSupport.SurfaceArea(cards.mesh);

            // Keeping 60 %, 30 % and 10 % of the triangles needs the kept cards enlarged 1.3, 1.8 and 3.2 times: all under the cap of 8.
            foreach (int target in new[] { 12000, 6000, 2000 })
            {
                ForestMeshTools.MeshData thinned = ForestFoliageThinner.Thin(cards.mesh, pieces, target, 8f, false);
                int triangles = ForestMeshTools.TriangleCount(thinned);
                Assert.AreEqual(target, triangles, target * 0.02f, $"Target {target}: the triangle count is more than 2 % off.");
                float area = ForestTestSupport.SurfaceArea(thinned);
                Assert.AreEqual(sourceArea, area, sourceArea * 0.05f, $"Target {target}: the leaf area changed by more than 5 %.");
            }
        }

        [Test]
        public void Thinner_FlattenReproducesPlanarCard()
        {
            // 600 planar strips of four quads each: enough pieces that they are not regrouped spatially.
            ForestTestSupport.CardSet strips = ForestTestSupport.Cards(10, 6, 10, 0.3f, 0.12f, 0.03f, 4);
            ForestFoliageThinner.Pieces pieces = ForestFoliageThinner.FindPieces(strips.mesh.positions, strips.mesh.subMeshes[0], Vector3.zero, Seed);
            Assert.AreEqual(strips.cards, pieces.count, "Every strip is one piece.");
            Assert.IsFalse(pieces.usedSpatialFallback);

            // A flattened piece costs two triangles, so this target keeps every strip; a cap of 1 leaves them life size.
            ForestMeshTools.MeshData flat = ForestFoliageThinner.Thin(strips.mesh, pieces, 2 * strips.cards, 1f, true);
            Assert.IsNotNull(flat.uv0, "A flattened level still needs UVs.");
            Assert.AreEqual(2 * strips.cards, ForestMeshTools.TriangleCount(flat), "Every strip should have become one quad of two triangles.");

            var trianglesOfStrip = new int[strips.cards];
            var cornersHit = new bool[strips.cards][];
            var problems = new List<string>();
            foreach (int[] indices in flat.subMeshes)
            {
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    Vector3 centroid = (flat.positions[indices[i]] + flat.positions[indices[i + 1]] + flat.positions[indices[i + 2]]) / 3f;
                    int strip = strips.CardAt(centroid);
                    trianglesOfStrip[strip]++;
                    if (cornersHit[strip] == null)
                        cornersHit[strip] = new bool[4];

                    for (int corner = 0; corner < 3; corner++)
                    {
                        Vector3 position = flat.positions[indices[i + corner]];
                        Vector2 uv = flat.uv0[indices[i + corner]];
                        int match = -1;
                        for (int c = 0; c < 4; c++)
                            if (Vector3.Distance(position, strips.corners[strip][c]) < 2e-4f)
                                match = c;
                        if (match < 0)
                            problems.Add($"strip {strip}: vertex {position.ToString("0.0000")} is not one of the strip's corners");
                        else if (Vector2.Distance(uv, strips.cornerUvs[strip][match]) > 1e-4f)
                            problems.Add($"strip {strip}: corner {match} has UV {uv.ToString("0.0000")}, the strip has {strips.cornerUvs[strip][match].ToString("0.0000")}");
                        else
                            cornersHit[strip][match] = true;
                    }
                }
            }
            for (int strip = 0; strip < strips.cards; strip++)
            {
                if (trianglesOfStrip[strip] != 2)
                    problems.Add($"strip {strip} became {trianglesOfStrip[strip]} triangle(s), expected 2");
                else if (!(cornersHit[strip][0] && cornersHit[strip][1] && cornersHit[strip][2] && cornersHit[strip][3]))
                    problems.Add($"strip {strip}: its quad does not reach all four corners");
            }
            ForestTestSupport.AssertNone(problems, "flattened strip(s) differ from their source");
        }

        [Test]
        public void Thinner_IsDeterministic()
        {
            ForestTestSupport.CardSet first = TwigCards(), second = TwigCards();
            ForestFoliageThinner.Pieces piecesA = ForestFoliageThinner.FindPieces(first.mesh.positions, first.mesh.subMeshes[0], Vector3.zero, Seed);
            ForestFoliageThinner.Pieces piecesB = ForestFoliageThinner.FindPieces(second.mesh.positions, second.mesh.subMeshes[0], Vector3.zero, Seed);
            CollectionAssert.AreEqual(piecesA.pieceOfTriangle, piecesB.pieceOfTriangle, "Piece ids differ between two runs.");
            CollectionAssert.AreEqual(piecesA.key, piecesB.key, "Priority keys differ between two runs.");

            foreach (bool flatten in new[] { false, true })
            {
                ForestMeshTools.MeshData a = ForestFoliageThinner.Thin(first.mesh, piecesA, 3000, 4f, flatten);
                ForestMeshTools.MeshData b = ForestFoliageThinner.Thin(second.mesh, piecesB, 3000, 4f, flatten);
                CollectionAssert.AreEqual(a.positions, b.positions, $"Positions differ between two runs (flatten {flatten}).");
                CollectionAssert.AreEqual(a.normals, b.normals, $"Normals differ between two runs (flatten {flatten}).");
                CollectionAssert.AreEqual(a.uv0, b.uv0, $"UVs differ between two runs (flatten {flatten}).");
                Assert.AreEqual(a.subMeshes.Length, b.subMeshes.Length);
                for (int i = 0; i < a.subMeshes.Length; i++)
                    CollectionAssert.AreEqual(a.subMeshes[i], b.subMeshes[i], $"Indices differ between two runs (flatten {flatten}).");
            }

            // The seed must matter, or "deterministic" would only mean "constant".
            ForestFoliageThinner.Pieces other = ForestFoliageThinner.FindPieces(first.mesh.positions, first.mesh.subMeshes[0], Vector3.zero, Seed + 1);
            CollectionAssert.AreNotEqual(piecesA.key, other.key, "A different seed gave the same priority keys.");
        }

        // ---- Sky ----

        [Test]
        public void Sky_ConstantImageIntegratesToPi()
        {
            // A sky of luminance 1 everywhere lights a horizontal surface with pi.
            foreach (int height in new[] { 32, 512 })
            {
                int width = height * 2;
                var pixels = new Color[width * height];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = Color.white;

                ForestSky.Calibration calibration = ForestSky.Integrate(pixels, width, height);
                Assert.AreEqual(Mathf.PI, calibration.upperHemisphereLux, Mathf.PI * 0.01f, $"{width} x {height}: upper hemisphere");
                Assert.AreEqual(Mathf.PI, calibration.lowerHemisphereLux, Mathf.PI * 0.01f, $"{width} x {height}: lower hemisphere");
                // The colour rule of HDRP's Inspector: the sum over its largest channel, times one half.
                Assert.AreEqual(0.5f, calibration.luxColor.x, 0.005f);
                Assert.AreEqual(0.5f, calibration.luxColor.y, 0.005f);
                Assert.AreEqual(0.5f, calibration.luxColor.z, 0.005f);
            }

            // Rows run bottom to top: a sky that is lit only in its upper half must put its light there.
            const int rows = 64, columns = 128;
            var half = new Color[columns * rows];
            for (int row = rows / 2; row < rows; row++)
                for (int column = 0; column < columns; column++)
                    half[row * columns + column] = Color.white;
            ForestSky.Calibration upperOnly = ForestSky.Integrate(half, columns, rows);
            Assert.AreEqual(Mathf.PI, upperOnly.upperHemisphereLux, Mathf.PI * 0.01f);
            Assert.AreEqual(0f, upperOnly.lowerHemisphereLux, 0.01f);
        }

        // ---- Image statistics (this package's own pure code) ----

        [Test]
        public void ImageStats_MeasureLevelsAndErrorColours()
        {
            var pixels = new Color32[1000];
            for (int i = 0; i < 500; i++) pixels[i] = new Color32(128, 128, 128, 255);       // mid grey
            for (int i = 500; i < 700; i++) pixels[i] = new Color32(0, 0, 0, 255);            // crushed
            for (int i = 700; i < 900; i++) pixels[i] = new Color32(255, 255, 255, 255);      // clipped
            for (int i = 900; i < 950; i++) pixels[i] = new Color32(255, 0, 255, 255);        // HDRP's error colour
            for (int i = 950; i < 1000; i++) pixels[i] = new Color32(0, 255, 255, 255);       // a shader still compiling

            ForestImageStats.Stats stats = ForestImageStats.Compute(pixels);
            Assert.AreEqual(0.2f, stats.shareBlack, 1e-4f);
            Assert.AreEqual(0.2f, stats.shareWhite, 1e-4f);
            Assert.AreEqual(0.05f, stats.shareMagenta, 1e-4f);
            Assert.AreEqual(0.05f, stats.shareCyan, 1e-4f);
            Assert.AreEqual(128f / 255f, stats.medianLuminance, 1e-3f);
            Assert.AreEqual((500 * 128 + 200 * 255 + 50 * 255) / 1000f / 255f, stats.meanR, 1e-4f);
            Assert.AreEqual((500 * 128 + 200 * 255 + 50 * 255) / 1000f / 255f, stats.meanG, 1e-4f);
            Assert.AreEqual((500 * 128 + 200 * 255 + 100 * 255) / 1000f / 255f, stats.meanB, 1e-4f);
            float expectedLuminance = (500 * 128f + 200 * 255f + 50 * 255f * (0.2126f + 0.0722f) + 50 * 255f * (0.7152f + 0.0722f)) / 1000f / 255f;
            Assert.AreEqual(expectedLuminance, stats.meanLuminance, 1e-3f);

            // Saturated but natural colours are not error colours.
            var natural = new[] { new Color32(200, 40, 200, 255), new Color32(90, 220, 220, 255), new Color32(240, 80, 240, 255) };
            ForestImageStats.Stats none = ForestImageStats.Compute(natural);
            Assert.AreEqual(0f, none.shareMagenta);
            Assert.AreEqual(0f, none.shareCyan);

            Assert.AreEqual(0f, ForestImageStats.Compute(Array.Empty<Color32>()).meanLuminance, "An empty image gives zeros, not an exception.");
        }

        [Test]
        public void ImageStats_RmseComparesImages()
        {
            var a = new Color32[64];
            var b = new Color32[64];
            for (int i = 0; i < a.Length; i++)
            {
                a[i] = new Color32(100, 100, 100, 255);
                b[i] = new Color32(100, 100, 100, 255);
            }
            Assert.AreEqual(0f, ForestImageStats.Rmse(a, b));

            for (int i = 0; i < b.Length; i++)
                b[i] = new Color32(151, 151, 151, 255);
            Assert.AreEqual(51f / 255f, ForestImageStats.Rmse(a, b), 1e-5f);

            Assert.AreEqual(-1f, ForestImageStats.Rmse(a, new Color32[32]), "Images of different sizes cannot be compared.");
            Assert.AreEqual(-1f, ForestImageStats.Rmse(a, null));
        }

        // ---- Budgets and the greybox ----

        // The PlayMode assembly cannot reference the editor assembly and keeps copies of the numbers it needs.
        const string PlayModeCopy = "Assets/Game/Tests/ForestPlayMode/ForestPlayBudgets.cs";

        [Test]
        public void Budgets_PlayModeCopyMatches()
        {
            Assert.IsTrue(File.Exists(PlayModeCopy), $"{PlayModeCopy} is missing.");
            string source = File.ReadAllText(PlayModeCopy);

            var problems = new List<string>();
            int compared = 0;
            foreach (Match match in Regex.Matches(source, @"public\s+const\s+(?:int|long|float|double)\s+(\w+)\s*=\s*([-+0-9.eE_]+)[fFlLdD]?\s*;"))
            {
                string name = match.Groups[1].Value;
                double copy = double.Parse(match.Groups[2].Value.Replace("_", ""), CultureInfo.InvariantCulture);

                // A constant of the same name in the budgets or in the layout is the original of this copy.
                FieldInfo original = typeof(ForestBudgets).GetField(name, BindingFlags.Public | BindingFlags.Static)
                    ?? typeof(ForestLayout).GetField(name, BindingFlags.Public | BindingFlags.Static);
                if (original == null || !original.IsLiteral)
                    continue;
                compared++;
                double value = Convert.ToDouble(original.GetRawConstantValue(), CultureInfo.InvariantCulture);
                if (Math.Abs(value - copy) > 1e-6 * Math.Max(1.0, Math.Abs(value)))
                    problems.Add($"{name}: the PlayMode copy says {copy}, {original.DeclaringType.Name} says {value}");
            }
            Assert.GreaterOrEqual(compared, 20, "Too few constants were recognised; the copy or this test's pattern has changed.");
            ForestTestSupport.AssertNone(problems, "budget constant(s) differ between the editor code and the PlayMode copy");
        }

        [Test]
        public void Greybox_StillFirstInBuildSettings()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            Assert.Greater(scenes.Length, 0, "Build Settings hold no scene; the greybox tests load 'Greybox' by name.");
            Assert.AreEqual(GreyboxSceneBuilder.ScenePath, scenes[0].path, "The greybox must stay the first scene in Build Settings.");
            Assert.IsTrue(scenes[0].enabled, "The greybox scene is disabled in Build Settings.");
        }
    }
}
