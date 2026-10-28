# M2 integration: what was changed, deviations, and what needs a decision

Written 2026-10-13 by the M2 integrator. Unity 6000.6.0f1, HDRP 17.6.0, Metal, Apple M4 Max, batch mode.
Paths are relative to `build/` unless they start with `Assets/`. The look changes and their evidence are
in `issues/M2-floor.md`; this note is the list.

## 1. Changes to forest files (nine source files and the manifest)

Originals: `backup/M2-pre-edit/`. Diffs: `out/M2/*.diff`.

| # | File | What | Deviation from the spec? |
|---|---|---|---|
| 1 | `ForestTexturePacker.cs` | The mask of `rock_moss_set_01` carries full AO: the AO channel of the downloaded map does not fit the FBX's UVs | Yes, 5.4 (mask green = `arm.R`), for this one material |
| 2 | `ForestPrefabs.cs` | New gate in the kit build: a surface with no AO under more than 2 % of its area throws | Addition |
| 3 | `Tests/ForestEditMode/ForestKitTests.cs` | New test `Prefabs_OcclusionMapsFitTheirMeshes` (the suite has 35 tests now) | Addition. No existing test was changed |
| 4 | `forest-asset-manifest.json` | A `note` on `rock_moss_set_01` (one line; ignored by the code) | No |
| 5 | `ForestMaterials.cs` | Colour multiplier of `dead_tree_trunk_02`: 0.6 to 0.85 | Yes, 5.5 |
| 6 | `ForestKitSpec.cs` | Grass patches: six tufts within 0.4 m at 1.7 to 2.4 times scan size, two leafy clumps per patch. `ToolVersion` `kit-2` | Yes, 5.6.5 (twelve tufts within 0.6 m) |
| 7 | `ForestScatter.cs`, `ForestLayout.cs` | Fern field under its cap: thinned to 0.36 at most, then the drifts are made smaller instead of thinner | Changes WP3's own deviation 8; the density formula of 7.2 and the cap of 900 are unchanged |
| 8 | `ForestCapture.cs` | `overview_oblique` forces LODs like `overview_ortho` | Yes, 12.2 |
| 9 | `ForestTerrain.cs` | The painted trail fades in over 0.5 to 3.5 m at both ends | Yes, 6.4 (small) |

No audit rule, budget, threshold or existing test was changed. No test was removed or skipped. The two
tests still ignored are the tree tests (M3).

## 2. A file that was in the project for the length of two runs

`build/tools/inspect/ForestInspectTemp.cs` is a probe that renders close-ups of props in the built scene
(every log and stump patch, the rocks on the steepest ground, samples of the rest). It uses internal
members of the editor assembly, so it has to be compiled into it: `tools/inspect/run-inspect.sh` copies it
to `Assets/Game/Editor/Forest/`, runs it, and deletes it and its `.meta` again. It was there during
`logs/M2-iter03-inspect.log` and `M2-53-final-inspect.log`. It is not in the project now (28 files in that
folder, as before M2), and every run after it compiled without it (0 `error CS`). The spec has no place
for such a file; if the close-ups are wanted for good, it belongs in `ForestCapture` (WP5).

## 3. Measured for the first time at M2

| Question | Answer | Evidence |
|---|---|---|
| Do the stump and log FBX files match the manifest (risk 22)? | Yes. `fbx_objects.py` on all eight M2 models: MATCH. The log's four authored levels have 83,128 / 41,564 / 20,782 / 10,390 triangles | `logs/M2-01-fetch.log` |
| Importer transform, mirror, slot names (risk 3) | Every bounds check passed on the first build; the five prefabs whose box is off centre all report mirror `x`; no slot was matched by triangle count (0 warnings) | `out/M2/kit-report.json`, `logs/M2-03-kit-build.log` |
| Vertex-cluster LODs of rocks (risk 12) | By eye on the LOD sheets: silhouettes hold at 1/4 and 1/16; the 1/16 level, shown from 30 m, is visibly facetted when framed as large as the scan | `captures/m2-r0/kit/*__lods.png` |
| Does the log float or look half buried (risk 25)? | By eye, neither on the eight instances: bedded, with trunk showing above the ferns. One lies across a hump with an end off the ground. `LogPitchDegrees` stays 0 | `captures/m2-r0/inspect/log_*.png` |
| Grounding | Largest error 5.5 mm (tolerance 3 cm); 89 rocks, 8 logs, 10 stump patches looked at or sampled close up | `out/M2/build-report.json`, `captures/m2-r0/inspect/` |
| Corridors and spawn | Smallest corridor margin 2.57 m (rule 2.0 in the audit, 2.5 in the planner), spawn margin 6.03 m (rule 5), physical corridors free in edit and play mode | `out/M2/build-report.json`, `out/M2/tests/forest-playmode.xml` |
| Budgets with the whole floor and no tree | See the table below | `captures/m2-r0/playmode/budget-counts.txt`, `frame-time.txt` |

Budgets, 2560 x 1440, play mode in the Editor. The test reports each counter at the worst of its four
views; at M2 that is `trail_start` for every one:

| Metric | M1 | M2 | Budget (warn at) |
|---|---|---|---|
| Triangles, all passes | 128,933 | 2,671,443 | 8.0 M (6.5 M) |
| Vertices | 73,758 | 1,910,212 | 10.0 M (8.0 M) |
| Draw calls (estimated, see `M1-draw-call-counter.md`) | 4 | 732 | 4,000 (3,000) |
| SetPass calls | 27 | 52 | 250 (200) |
| Shadow casters | 4 | 214 | 2,500 (2,000) |
| Texture memory above an empty scene | 354 MiB | 738 MiB | 1,024 MiB (819 MiB) |
| Mesh memory above an empty scene | | 36.7 MiB | 300 MiB (220 MiB) |
| Frame time, stopwatch, median / p95 | 6.7 / 7.4 ms | 7.85 / 8.25 ms | 11 ms (9.5 ms) |

## 4. Needs a decision or a look

1. **Triangles: the floor alone takes a third of the budget.** 2.67 M at `trail_start` (all passes,
   shadows included) without a single tree. The spec's bottom-up figure for the same floor is 0.79 M
   unique triangles, about 1.6 M with the depth prepass. Its figure for the trees is 1.92 M unique, 3.84 M
   with the prepass, plus their share of about 1.1 M for the shadow cascades. Added to what was measured
   that is roughly 6.5 to 7.6 M, between the warning line (6.5 M) and the budget (8.0 M), before E1 has
   chosen a denser arm. This is arithmetic on the spec's estimate, not a measurement. Frame time (7.85 ms
   of 11) has room. Which class of prop is the largest term was not measured.
2. **Texture memory is 738 MiB of 1,024**, 384 MiB more than at M1 for eight model texture sets (two of
   them at 4096). The fir sets come on top. It is under the warning line (819 MiB) now and may cross it at
   M3 or M4. Why eight sets cost that much was not investigated.
3. **Grass at twice life size** (`M2-floor.md`, 3): accept, or give the clearing something else.
4. **Fern cap 900** (`M2-floor.md`, 4): the floor is a sparse fern heath. The instance budget would allow
   more (2,459 planned of 3,500 with all trees), the triangle budget would not without a lighter LOD0.
5. **The litter tile is still visible** from above and on the bank (`M2-floor.md`, "Still visible"): the
   anti-tiling terrain graph is the owner's to approve (risk 13, section 15).
6. **AO of `rock_moss_set_01`** is dropped, not repaired (`M2-floor.md`, 1).
7. **Still open from M1, unchanged:** the draw-call figure is an estimate (732 now, which is believable
   against 1,402 instances with culling at 25 to 110 m); the forest scene is not in Build Settings.

## 5. Found on the way, not changed

1. **The sky calibration warning** logs on every build, as before (false alarm for this sky).
2. **`FrameTiming` reports 7 of 240 frames** (24 ms median on those 7), as at M1; the stopwatch is the method.
3. **Rocks and logs disappear at 90 and 110 m, ferns at 40 m, grass at 25 m.** In mist with a 80 m mean
   free path the rock cull is not noticeable in the captures; the fern cull is (ferns end 40 m out on open
   ground). Trees will stand in that band from M3.
4. **Decimated rock levels carry more vertices than a welded mesh** (WP2's note): for example
   `rock_moss_set_01_rock05` LOD2 has 1,281 vertices for 1,022 triangles. Not visible in the LOD sheets.
5. **`dry_branches_medium_01_b` is two separate small fragments** in the scan (inside a box 1.04 m long).
   It reads as debris; as "one branch touching the stump's edge" (ring rule) it is nearly invisible.
