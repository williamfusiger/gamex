# WP2 Kit: deviations, decisions and open points

Written 2026-10-13. Unity was not started. Evidence: the compile log and the harness log in
`build/out/WP2/` (see "What was checked" at the end).

Files: `ForestKitSpec.cs`, `ForestMeshTools.cs`, `ForestFoliageThinner.cs`, `ForestTreeBaker.cs`,
`ForestPrefabs.cs` in `Assets/Game/Editor/Forest/`. The public surface is exactly that of spec 11.4.
Everything else in them is `internal` or private.

## Deviations from the spec text

Each is the smallest change I found that keeps another part of the spec satisfiable.

### 1. Kept pieces are not all scaled by the same factor (spec 5.6.3 step 7)

- **What the spec says.** Scale every kept piece about its anchor by `s = min(maxScale, sqrt(totalArea / keptArea))`.
- **Why that cannot stand alone.** A piece grows away from its anchor, which is its point nearest the
  trunk. At LOD0 (`s` 2.6 to 3.1) a 10 cm twig at the crown's edge would reach 16 to 21 cm further out,
  on every side. The audit and `Prefabs_MatchManifestBounds` hold the thinned LOD0 to the manifest box
  within 2 % (12 to 18 cm for a young fir), and `TreeLods_WithinBudget` holds every level's bounds to
  10 % of the first, with `s` up to 20 at LOD2.
- **What I did.** `Thin` measures the crown's outline from the source foliage (its box, and the farthest
  reach in every 1 m of height by 16 sectors around the trunk axis). A piece stops growing where it
  would leave that outline. The common factor `s` is then solved so that the leaf area is still
  preserved: `sum(area * min(s, limit of the piece)^2) = totalArea`, up to `maxScale`. Pieces inside the
  crown grow a little more, pieces at the edge stay put. The report's `pieceScale` is `s`.
- **Also.** The six pieces that hold the crown's extreme points on each axis get the key `+infinity`, so
  every level keeps them and spans the same box. Sets are still nested.

### 2. The priority key is stored as a logarithm (spec 5.6.3 step 4)

- **Why.** `hash01 ^ (1 / w)` with `w` in square metres is `0.5 ^ 1000` and smaller: it underflows a
  float to 0 for almost every piece, and the order is lost.
- **What I did.** `Pieces.key` holds `ln(hash01) / w`. The order is identical. Keys are negative; larger is
  better. Ties are broken by the piece number.

### 3. The piece guard is not applied below 50,000 triangles (spec 5.6.3 step 2)

- **Why.** The guard ("at least 500 pieces, none above 5 %") turns pieces into 6 cm cells. Applied to a
  small mesh it contradicts the test `Thinner_FlattenReproducesPlanarCard` of spec 12.3: a lone strip is
  one piece, which is 100 % of its mesh, and would be cut into cells and never flatten to one quad.
- **What I did.** `ForestKitSpec.PieceGuardMinTriangles = 50000`. The smallest real foliage mesh has
  124,000 triangles, so real trees behave exactly as the spec says.
- **For WP5.** A test of the fallback needs a welded mesh of at least 50,000 triangles.

### 4. Clustered wood has a triangle budget (spec 5.6.4)

- **Why.** The table gives the wood of LOD1 and LOD2 as "5 cm cluster" and "20 cm cluster". Nobody knows
  how many triangles that is before the mesh is read, but `TriangleTarget` must return a number and
  `TreeLods_WithinBudget` holds the built level to 10 % of it.
- **What I did.** Wood is clustered at the spec's cell size. If that gives more than 16 % of the level's
  foliage target (`WoodBudgetShare`), the cell is coarsened until it fits. `TriangleTarget` for those
  levels is the foliage target plus half that budget, so the real total is inside 10 % whatever the
  wood turns out to be (checked in the harness for all six trees).
- **Numbers.** Targets are: young firs 80,300 / 65,520 / 65,590 at LOD0, 8,640 at LOD1, 1,620 at LOD2;
  tall firs 270,700 / 253,328 / 149,420, then 43,200 / 25,920 / 25,920, then 8,640 / 5,400 / 4,320.

### 5. The trunk radius is measured on the wood's surface, not on its vertices (spec 5.6.6)

- **Why.** "90th percentile of the distance of wood vertices between 0.8 and 1.6 m" works on a bare
  trunk. A young fir has branches down to the ground in the same sub-mesh as its trunk; their vertices
  lie up to 2 m out, the percentile would be far above the 0.15 m class constant, and the baker would
  throw on every young fir.
- **What I did.** `TrunkRadius` cuts the wood at eight heights in the band, casts 32 rays outward from
  the pivot axis at each, and takes the 90th percentile of the first hit. On a bare trunk this is the
  same statistic. With fewer than 16 hits it falls back to the vertex rule. It returns the measurement;
  the 0.12 m minimum is applied by the baker. Harness: 0.078 m on a bare tapering trunk, 0.075 m with
  80 branches added.
- **Still a risk.** A trunk that does not enclose its pivot axis is measured wrong, and a trunk wider
  than the class constant still stops the bake, as the spec requires.

### 6. A failed coverage gate throws after the prefab is written (spec 11.4)

- **What.** `Bake` throws on a failed bounds or coverage gate. A failed manifest check of the source
  throws before anything is written. A failed coverage gate, or a thinned LOD0 that no longer fits the
  manifest box, throws after the meshes, the prefab and the kit report entry are written, and the
  stamp is not set.
- **Why.** Experiment E1 and the kit sheets need the tree to look at, and the report needs the numbers.
  The build still fails, and `TreeLods_KeepCoverage` fails on the reported value.
- **Consequence.** A later scene build finds the prefab and uses it. The red test is what keeps it out
  of a milestone.

### 7. `BuildAll` goes on after a failed prefab and throws at the end

Every failure is logged in full with `Debug.LogException`; the stage then throws one exception that
lists them. A Unity launch is expensive, so one bad asset should not hide the others.

### 8. A downloaded but unmeasured model is skipped by `BuildAll`, with a warning

Spec 5.6.1 step 0 says refuse and throw; spec 11.4 says `BuildAll` skips prefabs whose model is not
"downloaded, measured and has materials" with a log line. `BuildAll` follows 11.4 (a warning with the
`fbx_objects.py` command). `ForestTreeBaker.Bake` and every direct derivation follow 5.6.1 and throw.
No prefab is built from unmeasured numbers either way.

## Readings of the spec where the text leaves a choice

| Point | Reading |
|---|---|
| `Extract(source, a, b)` | A compact copy that keeps `a` and `b` as two sub-meshes, in that order. `Merge` then makes one sub-mesh per part. `Merge(Extract(x, a, b))` is how two sub-meshes with one material become one |
| `Thin` on several sub-meshes | They are thinned as one triangle list, and `pieces` must have been found on the same list. The result has one sub-mesh |
| `MatchesSourceBounds`, `mirror` | A definite answer (`x`, `z`, `xz`, `none`) only when the source box is off centre by more than the tolerance on **both** horizontal axes; otherwise `symmetric`. One known and one unknown axis would otherwise read as different answers on two prefabs of one import, which `Prefabs_MatchManifestBounds` forbids. On failure `mirror` holds the reason in words |
| "The piece's size" for the 35 % flatten rule | The diagonal of the piece's bounding box. The residual is the largest distance of a vertex from the fitted plane. This is lenient: a strip bent into a half circle (29 %) is still flattened; a hairpin is not |
| `CoverageRatio` | Cells the candidate covers divided by cells the reference covers (it can exceed 1), any overlap counts, mean of the views along Z and along X. The baker compares a whole level (wood, twigs, crown core) with the whole scan |
| "Pixel size of its start distance" for LOD0 | LOD0 starts at 0 m, so it is measured at 2 m (`CoverageNearDistance`): a 1.9 mm cell |
| `CrownCoreFromLod` | Counts the table's thinned levels (0 to 2), not prefab levels, so a scan level does not shift it |
| LODGroup `size` | The largest side of the LOD0 bounds: the height of a tree, the width of a flat prop |
| `TriangleTarget` of the log's LOD1 to LOD3 | 0 (unknown) unless the manifest has objects for the `fbxNodes`. The built counts are in the kit report |
| Collision mesh | "At most 3,000" in 5.6.6 and "under 3,000" in 12.3: built to at most 2,999 |
| Kit report fields that are for trees | Other prefabs get `keepRatio`, `pieceScale` and `coverage` of 1 and `pieces` of 0. Patches get `mirror` `symmetric` |

## Vertex clustering: what happens to triangles that land on the same cells

Not in the spec, but it decides whether "a closed sphere stays closed". Several source triangles can
snap to the same three cells, facing either way. Per set of three cells the result keeps one triangle
of the majority facing (opposite ones are folds made by the snapping and cancel). If both facings are
present and both still face the way their source did, the feature is thinner than a cell (a slab, a
twig) and both sides are kept as a two-sided sheet instead of vanishing. Harness: a sphere of 32,768
triangles stays closed with every edge used exactly twice at seven cell sizes from 3 cm to 90 cm, and
a 1 cm slab clustered at 5 cm keeps 97 % of both sides.

## Measured on the real M2 meshes (outside Unity)

The M2 models were on disk by the end of this stage, so nine objects were read straight from their
FBX files (`build/out/WP2/harness/tools/dump_fbx_mesh.py`, the same triangles and vertex counts the
manifest lists) and put through the code the kit build will run on them:

| Object | Scan | LOD1 (1/4) | LOD2 (1/16) | Collision at 10 cm |
|---|---|---|---|---|
| `rock_moss_set_01_rock05` | 16,548 triangles, closed | 4,082, closed, 98 % of the area | 1,022, closed, 96 % | 2,960, closed |
| `rock_moss_set_02_rock07` | 9,826, closed | 2,420, closed, 98 % | 604, closed, 94 % | 1,208, closed |
| `rock_moss_set_02_rock12` | 7,999, 3 open edges (the pinhole) | 1,981, closed, 94 % | 498, closed, 90 % | 1,260, closed |
| `tree_stump_02` | 62,345, 551 open edges (open underneath) | 15,450, 270 open | 3,829, 145 open | 717, 63 open |
| `dead_tree_trunk_02_LOD0` | 83,128, closed | (authored levels are used) | | 1,534, closed |
| `dry_branches_medium_01_a` | 5,933 | 1,458, 87 % of the area | | none |

Every decimation took under 50 ms. Two things to look at on the M2 kit sheets:

- **Decimated levels carry more vertices than a welded mesh would** (rock05 LOD2: 1,280 vertices for
  1,022 triangles). The spec's "(cell, UV bucket of 1/64)" rule splits a cell wherever a bucket border
  runs through it, and on these atlases a bucket is about 13 cm of rock, the size of an LOD2 cell. Two
  vertices at one position then carry UVs up to half a cell apart, so the texture shifts by a few
  centimetres from triangle to triangle. Whether that shows at 12 m and beyond is a question for the
  sheets (risk 12). If it does, grouping a cell's vertices by whether they are connected inside the
  cell, instead of by bucket, keeps seams apart without splitting inside a UV island; it is a change
  to one loop of `ClusterDecimate`. It was not made, because the spec names the bucket rule.
- **rock05's collision mesh is 2,960 triangles at 10 cm**, just under the limit. The larger rocks of
  set 01 will go over and get a slightly coarser cell (the code does this; it is not an error).

## What the next run must know

1. **Coverage will probably come out above 1.30 with the table's scale caps.** On a synthetic fir
   (60,000 twig strips, 360,000 triangles, the proportions of `fir_sapling_medium_b`) the three levels
   preserve the leaf area to 2 % and still cover 1.36, 1.41 and 1.43 of the scan's silhouette; with
   4.2 million triangles 1.49 to 1.60. Enlarged pieces overlap each other less than the twigs they
   replace. If real firs behave the same, the gate fails high and the fix is a lower `maxScale` in the
   tree table of `ForestKitSpec`. The baker's error message then names the cap that would pass. This
   was not changed in advance: it is a property of a stand-in tree, not a measurement.
2. **Kept numbers from the spec table.** `scanWood` per tree (10,300 to 110,700) is the glTF count. The
   baker warns if the FBX differs by more than 2 %.
3. **Scan level (experiment E1).** Set `scanDistance` in the tree table (for example 6 for
   `fir_sapling_medium_b`). The prefab then has `LOD0` (scan, no shadows), `LOD0_Shadow` (the first
   thinned mesh, shadows only), and the thinned levels as `LOD1` to `LOD3`. Tall firs never get one.
   Arm B of E1 is the first number of the tree's `foliage` row (150,000).
4. **`LogPitchDegrees` other than 0.** The pitched log no longer has the box of its scan (a 4 degree
   pitch moves the ends by 14 cm, the tolerance is 8 cm). The audit and `Prefabs_MatchManifestBounds`
   already leave a pitched log out of the bounds comparison. What they do not allow for: tipping can
   move a high point of the log up to about 2 cm further from the pivot, and `Prefabs_FollowContract`
   gives the collision mesh 1 cm beyond `ClearanceRadius`. If that fails with a pitch set, it is this.
5. **Probe P1 and `rock_moss_set_02_rock13`.** Its centre is 3.9 cm off on X against a tolerance of
   4.2 cm, so it reports `symmetric`, not a mirror. Of the M2 kit, `rock_moss_set_01_rock04`,
   `rock_moss_set_01_rock06`, `rock_moss_set_02_rock08`, `fern_02_b` and `tree_stump_01` give a definite
   answer.
6. **Grass mixes.** `grass_patch_a` is 2,385 and `grass_patch_b` 2,483 triangles with the manifest's
   counts (limit 2,500, checked at build time). `grass_medium_01_small_a` is in neither: it does not
   fit beside `mid_b`.
7. **Nothing is ever deleted** under `ForestKit`. A tree that had a scan level and loses it leaves an
   unused `__lod3.asset` behind.
8. **Static switches** (`UseCrossFade`, `ThinByGroup`, ...) are part of the stamp hash, so changing one
   rebuilds every prefab on the next kit build.

## What was checked, and what was not

- **Compiled**: the five files with the contract and the stubs of WP1, WP3, WP4 and WP5:
  `build/out/WP2/Gamex.EditorTools.dll.log` is empty (0 errors, 0 warnings).
- **Run outside Unity**: `build/out/WP2/harness/run.sh heavy` compiles the three pure files with a
  scratch test program and runs it on the .NET runtime inside the Unity install. 25 test groups pass
  (`build/out/WP2/harness.log`): the bounds rule on all 48 manifest objects under four mirrors and
  against offsets, scale 100, scale 0.01 and a Y/Z swap; the UV rule; mirrored transforms; clustering;
  trunk radius; coverage; pieces, nesting, targets, area, flattening, determinism, the fallback, the
  crown core and edge cases. A 4.2 million triangle tree goes through `FindPieces` in 0.6 s, each
  `Thin` in 0.3 s and the coverage gate in 3 s.
- **WP5's own tests of this code, run outside Unity**: `build/out/WP2/harness/run-wp5.sh` compiles all
  27 real editor files with `ForestPureCodeTests.cs` and runs the seven tests that need neither the
  manifest nor Unity's native side (`MeshTools_UsableUvRule`, `MeshTools_ClusterDecimateKeepsSurfaceClosed`
  and the five `Thinner_*` tests): 7 passed (`build/out/WP2/harness-wp5.log`). One change was needed to
  run them there, in a scratch copy of `ForestTestSupport.cs` only: `Quaternion.Euler` is native code,
  so the card generator calls a managed equivalent. The project's test files were not touched.
  `MeshTools_BoundsRuleIsMirrorIndependent` needs the manifest loader, which needs Unity; the harness
  runs the same cases on the same 48 boxes.
- **Read, not run**: `ForestKitTests.cs` (`Prefabs_FollowContract`, `Prefabs_MatchManifestBounds`,
  `TreeLods_WithinBudget`, `TreeLods_KeepCoverage`) against `ForestPrefabs.SavePrefab` and the baker.
  One mismatch was found and fixed: the first LOD threshold was clamped to exactly 0.99, the test wants
  it below 0.99; the clamp is now 0.98.
- **Not run at all**: everything that needs Unity. `Read`, `ToMesh`, the Unity generator route, every
  line of `ForestPrefabs` and `ForestTreeBaker` (model lookup, material slots, mesh assets, prefab
  assembly, LODGroup, colliders, stamps, the kit report). They compile; their behaviour is unverified
  until the first kit build.
