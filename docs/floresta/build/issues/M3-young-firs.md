# M3, first half: the young fir prefabs (fetch, measure, import, bake)

Written 2026-10-14. The experiment and its numbers are in `issues/M3-E1.md`. Paths are relative to `build/` unless they
start with `Assets/` or `Tools/`. Originals of every changed file: `backup/M3a-pre-edit/`; diffs: `out/M3a/*.diff`.

## 1. The FBX of `fir_sapling_medium` has no UVs (risk 22)

**Found.** `fetch_assets.py --id fir_sapling_medium` (`logs/M3a-01-fetch.log`) fetched the 8 files (84.5 MB, md5 OK) and
`fbx_objects.py` measured the FBX: triangles, triangles per material and bounds of all three objects equal the manifest
(largest difference 0.0000 m), so it printed MATCH and set `objectsSource` to `"fbx"`. No `--write` was needed: no
compared value differs, and the layout numbers that depend on the sizes stand.

The same log says `UV layers 0` for every object and `no UV layer` for every material. Read directly
(`work/M3a/scripts/fbx_tree.py`): each `Geometry` (`NurbsPath.005` to `.007`, curves converted by Blender) holds
`Vertices`, `PolygonVertexIndex`, `Edges`, `LayerElementNormal` and `LayerElementMaterial`, and no `LayerElementUV`. The
manifest's `hasUv0: true` was read from the glTF. `fbx_objects.py` does not compare UVs (the spec does not ask it to),
so risk 22's check passed while the file is unusable for textured foliage: imported as is, every twig card would be a
solid ribbon and the bark untextured. Nothing in the spec covers it: `noUvFallback` (triplanar) is defined only for
`fir_tree_01_trunk_c`, and a needle atlas cannot be projected.

**Smallest working change.** Poly Haven's `/files` API lists a glTF of the same model
(`work/M3a/dl-api/files.json`): a 17 KB `.gltf` and a 74.2 MB `.bin` with positions, normals and UVs per material. Only
those two files were added to the manifest, from the API's URLs, sizes and md5 (map tags `"gltf"` and `"gltfBuffer"`,
destination `Source~`, so Unity does not import them), and fetched with the project's tool
(`logs/M3a-02-fetch-gltf.log`). The glTF's own textures (spec 4.1: "its packages drop the leaf alpha") are not
downloaded; the textures stay the manifest's JPG/PNG maps.

`ForestPrefabs.DeriveTreeSource` (used by the tree baker and by the kit capture's "source scan"):

1. derives the object from the imported FBX as before (bounds asserted against the manifest);
2. if a sub-mesh whose material has no `noUvFallback` has no usable UVs (the rule of spec 5.5), reads the same node of
   the glTF (`ForestGltfGeometry`, inside `ForestPrefabs.cs`: float positions, normals, UVs, 8/16/32-bit indices; node
   rotation, scale or matrix, sparse accessors, Draco and quantisation are refused);
3. bakes `KHR_texture_transform` of each material's base colour into its UVs (`fir_sapling_medium_branches`: scale
   (1.2, 0.1), offset (0, 0.9); `_branches_dead`: scale 0.8, offset (0, 0.2); twigs: none), because the two bark
   materials share one set of maps (`sameAs`) with different transforms; flips V (glTF's origin is top left);
4. mirrors each horizontal axis exactly as the importer mirrored the same object of the FBX (decided from the two boxes;
   for these trees X, as probe P1 found), which also reverses the winding;
5. refuses the glTF unless, per material, triangle count, surface area (0.2 %) and area-weighted centre (2 mm) equal the
   imported FBX's, and its box passes the manifest's bounds rule;
6. throws if UVs are missing and the manifest lists no glTF.

Evidence: `logs/M3a-51-final-kit.log` ("the FBX of fir_sapling_medium has no usable UVs ...; 'fir_sapling_medium_b_LOD0'
is taken from its glTF geometry (420822 triangles, 479822 vertices, checked against the imported FBX)"); the AO-under-UV
gate of `ForestPrefabs.RequireAoUnderUvs` passes for both materials with the transformed UVs; the bark and needles are
textured in every kit sheet and view. The glTF's md5 is part of the kit stamp (`InputHash`). `THIRD_PARTY_ASSETS.md`
records the two files (sapling 10 files, 158.6 MB; version 1 now 1,062 MB).

**For M4.** `fir_tree_01.fbx` comes from the same exporter; if it has no UV layer either, add its `.gltf` and `.bin` the
same way (the code is generic) before the kit build, which otherwise now throws instead of building untextured trees.

## 2. Spec deviations and decisions of this stage

| # | Spec | Now | Why, evidence |
|---|---|---|---|
| 1 | 4.1, 5.1: the FBX only | FBX imported and measured as before; positions, normals and UVs of the young firs from the glTF geometry, checked against it | Section 1 |
| 2 | 4.4: model map tags are `mesh` or `<material>:<map>` | Two new tags, `gltf` and `gltfBuffer` | The loader accepts any non-empty tag; `Manifest_LoadsAndPathsAreUnique` passes |
| 3 | 5.6.4 E1: bake `b` three ways | All three variants per arm; arm B 175,000 / 150,000 / 150,000 | `M3-E1.md`, "How it was run" |
| 4 | 5.6.4: `ScanLodDistance` 0 until E1 | 6 m for the three young firs | E1 decision |
| 5 | 5.6.4: LOD0 keeps the piece triangles, flatten off | With a scan level, every thinned level is flattened (`ForestKitSpec.FlattenAt(prefabId, level)`); without one, as the spec | The arm's-length view is the scan; flattened, the first thinned level keeps 26,000 needles at 1.2 times instead of 3,000 at 3.2 (risk 1, fallback b: "toggle flatten per LOD") |
| 6 | 5.6.3 step 6: flatten a piece to the quad of its UV box | A closed tube (mean vertex normal shorter than 0.5, `ForestKitSpec.FlattenTubeCoherence`) becomes a stick along its axis, as wide as the tube, texture along it | The quad of a stem's unrolled bark was a brown plank wider than the stem (`captures/m3a/e1/compare/trail_start_plank_A_vs_final.png`). Measured on `b`: 658 stem pieces at 0.15 to 0.18, 43,154 needle cards at 0.98 to 0.99 (`work/M3a/scripts/piece_normals.py`). The synthetic cards of the unit tests have one normal per card and are unaffected (`Thinner_FlattenReproducesPlanarCard` passes) |
| 7 | 5.6.4 table: young firs 70,000 / 60,000 / 60,000, then 8,000, then 1,500; scale caps 3.2 / 10 / 20 | 61,000 / 52,000 / 52,000, then 8,000, then 1,200; caps 3.2 / 10 / 6 | Frame budget at `crouch_hide` and the 42 m straps (`M3-E1.md`, tuning table). `TreeLods_WithinBudget` holds each level to `TriangleTarget`, which is computed from this table: the tunable and the test's expectation move together |
| 8 | 10: triangles 8.0 M, vertices 10.0 M, mesh memory 300 MiB | 11.6 M, 19.0 M, 400 MiB (warnings 10.1 M, 17.0 M, 320 MiB), in `ForestBudgets` and its PlayMode copy | The re-base section 10 asks for (`M3-E1.md`, "Budget re-base"). Frame time unchanged |
| 9 | 12.2 kit set: levels at 8, 25 and 70 m, foliage at 1.5 m | Also at each switch distance (6, 14, 42 m for a young fir) and a close turntable of the nearest level from four sides at 1.5 and 3 m | The task asks for a close turntable and for no pop between levels; the spec's distances fall inside the levels, not at the hand-overs |
| 10 | 12.2: the kit's "source scan" read from the imported FBX | Read through `DeriveTreeSource`, materials resolved as the baker does | The FBX has no UVs; the sheet must show what the baker used |
| 11 | | `ForestKitSpec.ToolVersion` "kit-4" | Thinner output changed; every prefab is restamped |

## 3. The scene

The tree placement of M3 (thickets, the fir wall on the bank, scatter tuning) was **not** done. But once the young-fir
prefabs exist, the M2 scene is no longer consistent with them: the audit row "each placement has its instance" fails
(`Scene_PassesAudit`: "771 placement(s) have a built prefab but no instance in the scene", `logs/M3a-57-final-forest-editmode.log`,
run on the M2 scene restored byte for byte from `backup/M3a-pre-edit/scene`). So the scene was rebuilt with the existing
builder and planner, unchanged: 2,684 placements, 2,413 instances (771 young firs, the 271 tall firs skipped), audit
passed with 0 errors and 0 warnings (`logs/M3a-60-final-build.log`, `out/M3a/build-report.treescene.json`). This is the
same scene the E1 runs used.

Prop prefabs rebuilt with `-forestForce` are byte-identical to M2's (`rock_moss_set_02_rock11.prefab`,
`fern_patch_a.prefab` compared with `backup/M3a-pre-edit/scene/`).

## 4. Tests and checks (final runs, `tools/m3a-final-1.sh` and `tools/m3a-final-2.sh`)

| Run | Result | Evidence |
|---|---|---|
| Import | Exit 0 | `logs/M3a-50-final-import.log` |
| Kit, forced | Exit 0: "31 prefabs built, 0 up to date, 3 skipped for missing inputs, 0 failed" (the tall firs) | `logs/M3a-51-final-kit.log`, `out/M3a/kit-report.json` |
| Scene | Exit 0, audit passed | `logs/M3a-60-final-build.log` |
| Kit sheets | 80 sheets of 31 prefabs, `passed: true`, 0 magenta, 0 cyan | `captures/m3a/final/kit/kit-summary.json` |
| Full set (scene with young firs) | 32 views, `passed: true`, 0 error pixels, luminance 0.211 to 0.681 | `captures/m3a/final/treescene/capture-summary.json` |
| Forest EditMode | **35 passed, 0 failed, 0 ignored** (`TreeLods_KeepCoverage` and `TreeLods_WithinBudget` included) | `out/M3a/tests/forest-editmode.xml`, `logs/M3a-62-final-forest-editmode.log` |
| Forest PlayMode | **9 passed, 1 failed**: `Budget_Counts`, triangles 12,482,801 at `crouch_hide` against 11,600,000 | `out/M3a/tests/forest-playmode.xml`, `logs/M3a-63-final-forest-playmode.log`, `captures/m3a/final/finalscene/playmode/budget-counts.txt` |
| Greybox PlayMode | **7 passed** | `out/M3a/tests/greybox-playmode.xml`, `logs/M3a-64-final-greybox-playmode.log` |
| Logs | 0 `error CS`, 0 `warning CS`, 0 "immutable" in every final log | `logs/M3a-final-sequence-1.txt`, `-2.txt` |
| Pre-existing files | 59 checked, 0 differ | `tools/check-preexisting.sh` |
| PackageCache | `DefaultHDTerrainMaterial.mat` md5 `8ce2f7482ff8fb845c1b94b0082b4cf0` | final sequence |
| Temporary test | Removed (0 files named `*Temp*` left in the forest folders) | final sequence |

Play mode, `budget-counts.txt` of the final run: triangles 12.48 M, vertices 17.02 M, 2,857 draw calls (estimated),
59 SetPass, 910 shadow casters, 802 MiB of textures (budget 1,024), 273 MiB of meshes (budget 400) above an empty scene.
Texture memory rose by 64 MiB with the three young-fir texture sets.

## 5. Sizes

`Assets/ThirdParty/PolyHaven/fir_sapling_medium`: 161 MB. `ForestKit/Meshes`: the three scan meshes are 91, 56 and
57 MB as text-serialised assets; `ForestKit` is 408 MB in all (owner's ignore decision, spec section 15).
