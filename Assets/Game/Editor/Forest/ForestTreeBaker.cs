using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gamex.EditorTools
{
    /// <summary>
    /// Turns one scanned fir into a prefab (spec sections 5.6.3 and 5.6.4). The scan is hundreds of
    /// thousands to millions of triangles, nearly all of them twigs, and its FBX holds no lower levels.
    /// The baker derives three: the twigs thinned by ForestFoliageThinner, the wood kept or clustered.
    /// Optionally the untouched scan becomes a nearest level in front of them (experiment E1).
    /// </summary>
    public static class ForestTreeBaker
    {
        /// <summary>
        /// Meshes and prefab for one tree, and its entry in the kit report. Skips a tree whose stamp is
        /// current unless <paramref name="force"/> is set. Throws on a failed bounds or coverage gate.
        /// </summary>
        public static void Bake(string prefabId, bool force) => BakeIfNeeded(prefabId, force);

        /// <summary>True when the tree was baked, false when its stamp said nothing had changed.</summary>
        internal static bool BakeIfNeeded(string prefabId, bool force)
        {
            ForestClass cls = ForestKitSpec.ClassOf(prefabId);
            if (!ForestKitSpec.IsTree(cls))
                throw new ArgumentException($"'{prefabId}' is not a tree; ForestPrefabs.BuildAll builds it.", nameof(prefabId));

            ForestObjectEntry entry = ForestManifest.FindObjectForPrefab(prefabId)
                ?? throw new InvalidOperationException($"The manifest has no object for tree '{prefabId}'.");
            ForestAssetEntry owner = ForestManifest.FindOwner(entry)
                ?? throw new InvalidOperationException($"The manifest object '{entry.name}' belongs to no model.");

            string inputHash = ForestPrefabs.InputHash(prefabId);
            if (!force && ForestPrefabs.IsCurrent(prefabId, inputHash)) return false;

            ForestPrefabs.EnsureFolders();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            ForestKitSpec.TreeSpec tree = ForestKitSpec.Tree(prefabId);

            // 1. The scan, in prefab space and checked against the manifest (throws on a mismatch). Where the FBX
            // carries no UVs, its geometry comes from the model's glTF, checked against the FBX.
            ForestPrefabs.Source source = ForestPrefabs.DeriveTreeSource(owner, entry);
            var triplanar = new List<string>();
            Material[] subMeshMaterials = ForestPrefabs.ResolveMaterials(source, triplanar);

            // 2. Twigs apart from wood; wood sub-meshes that share a material become one.
            var twigIndices = new List<int>();
            string twigMaterialName = null;
            Material twigMaterial = null;
            var woodMaterials = new List<Material>();
            var woodIndices = new List<List<int>>();
            for (int i = 0; i < source.materialNames.Length; i++)
            {
                if (source.data.subMeshes[i].Length == 0) continue;
                if (ForestMaterials.IsFoliage(source.materialNames[i]))
                {
                    if (twigMaterial != null && twigMaterial != subMeshMaterials[i])
                        throw new InvalidOperationException($"{prefabId} has two foliage materials; the thinner handles one.");
                    twigMaterial = subMeshMaterials[i];
                    twigMaterialName = source.materialNames[i];
                    twigIndices.Add(i);
                    continue;
                }
                int group = woodMaterials.IndexOf(subMeshMaterials[i]);
                if (group < 0)
                {
                    group = woodMaterials.Count;
                    woodMaterials.Add(subMeshMaterials[i]);
                    woodIndices.Add(new List<int>());
                }
                woodIndices[group].Add(i);
            }
            if (twigIndices.Count == 0) throw new InvalidOperationException($"{prefabId} has no foliage sub-mesh.");
            if (woodMaterials.Count == 0) throw new InvalidOperationException($"{prefabId} has no wood sub-mesh.");

            ForestMeshTools.MeshData twigs = ForestMeshTools.Merge(ForestMeshTools.Extract(source.data, twigIndices.ToArray()));
            var woodParts = new ForestMeshTools.MeshData[woodMaterials.Count];
            for (int group = 0; group < woodParts.Length; group++)
                woodParts[group] = ForestMeshTools.Extract(source.data, woodIndices[group].ToArray());
            // One sub-mesh per wood material, in the order the materials list has.
            ForestMeshTools.MeshData wood = ForestMeshTools.Merge(woodParts);
            int twigTriangles = ForestMeshTools.TriangleCount(twigs);
            int woodTriangles = ForestMeshTools.TriangleCount(wood);
            if (Mathf.Abs(woodTriangles - tree.scanWood) > 0.02f * tree.scanWood)
                Debug.LogWarning($"Forest kit: {prefabId} has {woodTriangles} wood triangles; ForestKitSpec expects {tree.scanWood}. Correct the tree table.");

            // 3. The trunk capsule. The scatterer plans with the class constant before any prefab exists,
            // so a trunk that measures wider must stop the build, not surprise the corridor audit.
            float measured = ForestMeshTools.TrunkRadius(wood, ForestKitSpec.TrunkBandMin, ForestKitSpec.TrunkBandMax);
            float radius = Mathf.Max(measured, ForestKitSpec.MinTrunkRadius);
            float clearance = ForestKitSpec.TreeClearance(cls);
            if (radius > clearance)
                throw new InvalidOperationException(
                    $"{prefabId}: the trunk measures {measured:0.000} m in radius between {ForestKitSpec.TrunkBandMin} and {ForestKitSpec.TrunkBandMax} m, "
                    + $"wider than the {clearance} m its class is planned with.");

            // 4. The pieces the thinner works on. Their keys do not depend on the level.
            ForestFoliageThinner.Pieces pieces = ForestFoliageThinner.FindPieces(twigs.positions, twigs.subMeshes[0], Vector3.zero, ForestKitSpec.Seed);
            if (pieces.usedSpatialFallback)
                Debug.LogWarning($"Forest kit: the twigs of {prefabId} are not separate small pieces; they were cut into {pieces.count} cells of 6 cm instead.");

            // 5. Levels.
            float[] distances = ForestKitSpec.LodDistances(prefabId);
            int first = ForestKitSpec.ScanLodDistance(prefabId) > 0f ? 1 : 0;   // prefab level of the nearest thinned level
            if (distances.Length != first + ForestKitSpec.ThinnedLevels)
                throw new InvalidOperationException($"{prefabId}: {distances.Length} distances for {first + ForestKitSpec.ThinnedLevels} levels.");

            // What the coverage gate compares with: the whole scan.
            ForestMeshTools.MeshData scan = ForestMeshTools.Merge(wood, twigs);
            var levels = new ForestPrefabs.Level[distances.Length];
            var reports = new ForestKitLodReport[distances.Length];
            var gateFailures = new List<string>();

            for (int thinned = 0; thinned < ForestKitSpec.ThinnedLevels; thinned++)
            {
                int lod = first + thinned;
                var woodOfLevel = new List<ForestMeshTools.MeshData>();
                var materials = new List<Material>();

                // Wood: the scan for the nearest thinned level, so the trunk at eye level is exactly the
                // scan; clustered beyond, and coarser still if that takes more than its share of triangles.
                ForestMeshTools.MeshData levelWood = wood;
                if (thinned > 0)
                {
                    // Which wood a thinned level clusters (ForestKitSpec.WoodAt): all of it (young firs), the trunk and the
                    // branches inside the crown (the tall firs' middle level), or the trunk alone (their far level). Decided per
                    // source sub-mesh, so a group that draws trunk and branches with one material (b's triplanar bark) keeps its
                    // trunk part.
                    ForestKitSpec.WoodKind kind = ForestKitSpec.WoodAt(prefabId, thinned);
                    bool dropSkirt = thinned > ForestKitSpec.SkirtUntilThinnedLevel && source.skirt != null && Array.IndexOf(source.skirt, true) >= 0;
                    ForestMeshTools.MeshData woodInput = kind == ForestKitSpec.WoodKind.All && !dropSkirt
                        ? wood
                        : FilterWood(source, woodIndices, kind == ForestKitSpec.WoodKind.TrunkAndCrownBranches ? CrownBase(twigs)
                            : kind == ForestKitSpec.WoodKind.All ? float.NegativeInfinity : float.PositiveInfinity, kind == ForestKitSpec.WoodKind.None, dropSkirt);
                    int budget = ForestKitSpec.WoodBudget(prefabId, thinned);
                    levelWood = ForestMeshTools.TriangleCount(woodInput) == 0 ? woodInput : ForestMeshTools.ClusterDecimate(woodInput, ForestKitSpec.WoodClusterCell(thinned));
                    if (ForestMeshTools.TriangleCount(levelWood) > budget) levelWood = ForestMeshTools.ClusterToTarget(woodInput, budget);
                    if (kind == ForestKitSpec.WoodKind.TrunkOnly && ForestKitSpec.FarTrunkOneDraw)
                        levelWood = OneWoodGroup(levelWood, woodMaterials);
                    if (kind != ForestKitSpec.WoodKind.All)
                        Debug.Log($"Forest kit: {prefabId} level {first + thinned}: wood ({kind}) {ForestMeshTools.TriangleCount(woodInput)} triangles clustered to "
                            + $"{ForestMeshTools.TriangleCount(levelWood)} (budget {budget}).");
                }
                for (int group = 0; group < woodMaterials.Count; group++)
                {
                    if (levelWood.subMeshes[group].Length == 0) continue;
                    woodOfLevel.Add(ForestMeshTools.Extract(levelWood, group));
                    materials.Add(woodMaterials[group]);
                }
                materials.Add(twigMaterial);

                ForestMeshTools.MeshData core = null;
                if (ForestKitSpec.CrownCoreAt(prefabId, thinned))
                {
                    Material coreMaterial = ForestMaterials.GetCrownCore(twigMaterialName);
                    if (coreMaterial == null)
                        throw new InvalidOperationException($"{prefabId}: the crown core is switched on but {twigMaterialName} has no crown-core material.");
                    core = ForestFoliageThinner.BuildCrownCore(twigs, Vector3.zero, ForestKitSpec.CrownCoreBands,
                        ForestKitSpec.CrownCoreSectors, ForestKitSpec.CrownCoreRadiusFraction);
                    materials.Add(coreMaterial);
                }

                // Sub-meshes in the order of the materials: wood, twigs, crown core.
                ForestMeshTools.MeshData Assemble(ForestMeshTools.MeshData thinnedTwigs)
                {
                    var parts = new List<ForestMeshTools.MeshData>(woodOfLevel) { thinnedTwigs };
                    if (core != null) parts.Add(core);
                    return ForestMeshTools.Merge(parts.ToArray());
                }

                int target = tree.foliage[thinned];
                float scaleCap = tree.maxScale[thinned];
                bool flatten = ForestKitSpec.FlattenAt(prefabId, thinned);
                // From 14 m a young fir's needles become twig cards (review 1 of the M3 captures): kept as single
                // needles, a few thousand of them read as bamboo whatever their scale.
                bool cards = ForestKitSpec.TwigCardsAt(prefabId, thinned, twigMaterialName);
                ForestFoliageThinner.ThinStats stats;
                float cardCell = 0f;
                ForestMeshTools.MeshData foliage = cards
                    ? ForestFoliageThinner.TwigCards(twigs, pieces, target, ForestKitSpec.TwigSprites(twigMaterialName), ForestKitSpec.TwigCardFill, out stats, out cardCell)
                    : ForestFoliageThinner.Thin(twigs, pieces, target, scaleCap, flatten, out stats);
                if (ForestMeshTools.TriangleCount(foliage) == 0)
                    throw new InvalidOperationException($"{prefabId}: thinning to {target} triangles left no foliage at level {lod}.");
                ForestMeshTools.MeshData level = Assemble(foliage);

                // The gate: how much of the scan's silhouette the level fills, at the size of a pixel where
                // the level first shows.
                float cell = ForestKitSpec.PixelSize(lod == 0 ? 0f : distances[lod - 1]);
                float coverage = ForestMeshTools.CoverageRatio(scan, level, cell);
                if (coverage < ForestKitSpec.CoverageMin(thinned) || coverage > ForestKitSpec.CoverageMax)
                {
                    string failure = $"level {lod} covers {coverage:0.00} of the scan's silhouette (allowed {ForestKitSpec.CoverageMin(thinned):0.00} "
                        + $"to {ForestKitSpec.CoverageMax:0.00}) at piece scale {stats.scale:0.0}";
                    if (coverage > ForestKitSpec.CoverageMax)
                        failure += cards
                            ? " (twig cards: lower ForestKitSpec.TwigCardFill)"
                            : SuggestScaleCap(scan, twigs, pieces, target, scaleCap, flatten, cell, Assemble);
                    gateFailures.Add(failure);
                }

                Mesh mesh = ForestPrefabs.SaveMesh(ForestMeshTools.ToMesh(level, prefabId + "__lod" + lod, true), ForestPrefabs.MeshPath(prefabId, "lod" + lod));
                levels[lod] = new ForestPrefabs.Level { mesh = mesh, materials = materials.ToArray(), castShadows = true, untilDistance = distances[lod] };

                ForestKitLodReport report = ForestPrefabs.LevelReport(mesh, distances[lod]);
                report.pieces = stats.piecesKept;
                report.keepRatio = twigTriangles > 0 ? (float)stats.sourceTrianglesKept / twigTriangles : 0f;
                report.pieceScale = stats.scale;
                report.coverage = coverage;
                reports[lod] = report;
                Debug.Log(cards
                    ? $"Forest kit: {prefabId} level {lod}: {report.triangles} triangles, {stats.piecesKept} twig cards from cells of {cardCell:0.000} m, "
                        + $"fill {stats.scale:0.00}, coverage {coverage:0.00}."
                    : $"Forest kit: {prefabId} level {lod}: {report.triangles} triangles, {stats.piecesKept} of {pieces.count} pieces "
                        + $"({stats.piecesFlattened} as quads), scale {stats.scale:0.00}{(stats.scaleCapReached ? " (cap reached)" : "")}, coverage {coverage:0.00}.");
            }

            // The audit holds the nearest thinned level to the manifest's box as well; say so here, where the cause is known.
            Bounds thinnedBounds = levels[first].mesh.bounds;
            if (!ForestMeshTools.MatchesSourceBounds(thinnedBounds, ForestManifest.SourceBounds(entry), out string thinnedProblem))
                gateFailures.Add($"level {first} no longer fits the manifest's box: {thinnedProblem}");

            var prefab = new ForestPrefabs.Parts { prefabId = prefabId, levels = levels, capsuleRadius = radius };
            if (first == 1)
            {
                // The untouched scan, shown only up close. It does not cast shadows: a second renderer in
                // the same level draws a thinned mesh (ForestKitSpec.ScanShadowThinnedLevel) into the shadow maps instead.
                var scanParts = new List<ForestMeshTools.MeshData>();
                var scanMaterials = new List<Material>(woodMaterials);
                for (int group = 0; group < woodMaterials.Count; group++) scanParts.Add(ForestMeshTools.Extract(wood, group));
                scanParts.Add(twigs);
                scanMaterials.Add(twigMaterial);

                Mesh mesh = ForestPrefabs.SaveMesh(ForestMeshTools.ToMesh(ForestMeshTools.Merge(scanParts.ToArray()), prefabId + "__lod0", true),
                    ForestPrefabs.MeshPath(prefabId, "lod0"));
                levels[0] = new ForestPrefabs.Level { mesh = mesh, materials = scanMaterials.ToArray(), castShadows = false, untilDistance = distances[0] };
                reports[0] = ForestPrefabs.LevelReport(mesh, distances[0]);
                reports[0].isScan = true;
                reports[0].pieces = pieces.count;
                ForestPrefabs.Level shadowLevel = levels[first + Mathf.Clamp(ForestKitSpec.ScanShadowThinnedLevel, 0, ForestKitSpec.ThinnedLevels - 1)];
                prefab.shadowMesh = shadowLevel.mesh;
                prefab.shadowMaterials = shadowLevel.materials;
            }
            prefab.bounds = levels[0].mesh.bounds;
            ForestPrefabs.SavePrefab(prefab);

            ForestPrefabs.WriteReport(new ForestKitPrefabReport
            {
                prefabId = prefabId,
                boundsMin = prefab.bounds.min,
                boundsMax = prefab.bounds.max,
                mirror = source.mirror,
                colliderRadius = radius,
                sourcePieces = pieces.count,
                medianPieceTriangles = ForestFoliageThinner.MedianPieceTriangles(pieces),
                usedSpatialFallback = pieces.usedSpatialFallback,
                triplanarMaterials = triplanar.ToArray(),
                bakeSeconds = (float)watch.Elapsed.TotalSeconds,
                lods = reports,
            });

            // A failed gate still leaves the meshes, the prefab and the report behind, so the kit sheets and
            // the numbers can be looked at. The stamp is not set, so the next build bakes the tree again.
            if (gateFailures.Count > 0)
                throw new InvalidOperationException(
                    $"{prefabId} failed its gate: {string.Join("; ", gateFailures)}. See ForestKitSpec (CrownCoreFromLod, ThinByGroup, the tree table) and risk 1 of the spec.");

            ForestPrefabs.MarkCurrent(prefabId, inputHash);
            return true;
        }

        /// <summary>
        /// The wood of the source, one sub-mesh per wood group as in the baker's wood, compacted: every trunk sub-mesh
        /// (ForestKitSpec.IsTrunkMaterial) whole, and of the other (branch) sub-meshes the triangles whose centroid lies at or
        /// above <paramref name="branchesFrom"/> metres (PositiveInfinity: none; NegativeInfinity: all). With no trunk sub-mesh at all
        /// the source's whole wood is returned, as the trunk-only rule did. <paramref name="none"/> returns no wood at all;
        /// <paramref name="dropSkirt"/> leaves out the root-skirt sub-meshes.
        /// </summary>
        static ForestMeshTools.MeshData FilterWood(ForestPrefabs.Source source, List<List<int>> woodIndices, float branchesFrom, bool none, bool dropSkirt)
        {
            Vector3[] p = source.data.positions;
            var subMeshes = new int[woodIndices.Count][];
            bool anyTrunk = false;
            for (int group = 0; group < woodIndices.Count; group++)
            {
                var indices = new List<int>();
                foreach (int s in woodIndices[group])
                {
                    if (none) continue;
                    // The root skirt (ForestPrefabs.SplitSkirt) is drawn by the nearest level only (ForestKitSpec.SkirtUntilThinnedLevel).
                    if (dropSkirt && source.skirt != null && s < source.skirt.Length && source.skirt[s]) continue;
                    int[] from = source.data.subMeshes[s];
                    if (ForestKitSpec.IsTrunkMaterial(source.materialNames[s]))
                    {
                        indices.AddRange(from);
                        anyTrunk |= from.Length > 0;
                        continue;
                    }
                    if (float.IsPositiveInfinity(branchesFrom)) continue;
                    for (int t = 0; t + 2 < from.Length; t += 3)
                    {
                        if ((p[from[t]].y + p[from[t + 1]].y + p[from[t + 2]].y) / 3f < branchesFrom) continue;
                        indices.Add(from[t]); indices.Add(from[t + 1]); indices.Add(from[t + 2]);
                    }
                }
                subMeshes[group] = indices.ToArray();
            }
            if (!anyTrunk && !none)
            {
                for (int group = 0; group < woodIndices.Count; group++)
                {
                    var all = new List<int>();
                    foreach (int s in woodIndices[group]) all.AddRange(source.data.subMeshes[s]);
                    subMeshes[group] = all.ToArray();
                }
            }
            var filtered = new ForestMeshTools.MeshData
            {
                positions = source.data.positions, normals = source.data.normals, tangents = source.data.tangents, uv0 = source.data.uv0, subMeshes = subMeshes,
            };
            return ForestMeshTools.Extract(filtered);
        }

        /// <summary>
        /// The far trunk drawn with one material (ForestKitSpec.FarTrunkOneDraw): every wood group's triangles move into one group, the
        /// one drawn with the triplanar bark when there is one (it needs no UVs), else the largest. fir_tree_01_b's trunk is split between
        /// its scanned flare (trunk_b) and the triplanar bark above 1.8 m; from 30 m on that is two draws for about 400 triangles.
        /// </summary>
        static ForestMeshTools.MeshData OneWoodGroup(ForestMeshTools.MeshData wood, List<Material> woodMaterials)
        {
            int nonEmpty = 0, target = -1, largest = -1;
            Material triplanar = ForestMaterials.GetTriplanar(ForestKitSpec.OffAtlasFallback);
            for (int group = 0; group < wood.subMeshes.Length; group++)
            {
                if (wood.subMeshes[group].Length == 0) continue;
                nonEmpty++;
                if (triplanar != null && group < woodMaterials.Count && woodMaterials[group] == triplanar) target = group;
                if (largest < 0 || wood.subMeshes[group].Length > wood.subMeshes[largest].Length) largest = group;
            }
            if (nonEmpty < 2) return wood;
            if (target < 0) target = largest;
            var all = new List<int>();
            foreach (int[] indices in wood.subMeshes) all.AddRange(indices);
            var subMeshes = new int[wood.subMeshes.Length][];
            for (int group = 0; group < subMeshes.Length; group++) subMeshes[group] = group == target ? all.ToArray() : Array.Empty<int>();
            return new ForestMeshTools.MeshData { positions = wood.positions, normals = wood.normals, tangents = wood.tangents, uv0 = wood.uv0, subMeshes = subMeshes };
        }

        /// <summary>The height (metres above the pivot) below which the twigs hold ForestKitSpec.CrownBaseShare of their vertices.</summary>
        static float CrownBase(ForestMeshTools.MeshData twigs)
        {
            var heights = new float[twigs.positions.Length];
            for (int i = 0; i < heights.Length; i++) heights[i] = twigs.positions[i].y;
            Array.Sort(heights);
            return heights.Length == 0 ? 0f : heights[Mathf.Clamp(Mathf.FloorToInt(ForestKitSpec.CrownBaseShare * heights.Length), 0, heights.Length - 1)];
        }

        // For a level that came out fuller than the scan: the largest cap on the piece scale that would put
        // it back in the middle of the allowed range. Nothing is built from the answer. It goes into the
        // error message, because the cap is a number somebody then has to choose in ForestKitSpec, and
        // every guess costs a bake.
        static string SuggestScaleCap(ForestMeshTools.MeshData scan, ForestMeshTools.MeshData twigs, ForestFoliageThinner.Pieces pieces,
            int target, float scaleCap, bool flatten, float cell, Func<ForestMeshTools.MeshData, ForestMeshTools.MeshData> assemble)
        {
            float wanted = 0.5f * (1f + ForestKitSpec.CoverageMax);
            float low = 1f, high = scaleCap, best = 0f, bestCoverage = 0f;
            for (int step = 0; step < 5; step++)
            {
                float middle = 0.5f * (low + high);
                ForestMeshTools.MeshData foliage = ForestFoliageThinner.Thin(twigs, pieces, target, middle, flatten);
                float coverage = ForestMeshTools.CoverageRatio(scan, assemble(foliage), cell);
                if (coverage > wanted)
                {
                    high = middle;
                }
                else
                {
                    low = middle;
                    best = middle;
                    bestCoverage = coverage;
                }
            }
            return best > 0f
                ? $" (a scale cap of {best:0.0} instead of {scaleCap:0.0} would cover {bestCoverage:0.00})"
                : " (no scale cap above 1 brings it into range)";
        }
    }
}
