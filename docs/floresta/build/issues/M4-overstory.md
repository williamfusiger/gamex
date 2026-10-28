# M4, second half: overstory, backdrop, root mounds, budgets (`captures/m4-r0`)

Written 2026-10-14. Paths are relative to `build/` unless they start with `Assets/`. Originals of every changed file:
`backup/M4b-pre-edit/`; diffs: `out/M4b/*.diff`. Final transcript: `logs/M4b-final-sequence.txt`, then a re-run of the three
test suites after a comment-only edit (`logs/M4b-final-72..74-*.log`, `out/M4b/tests/rerun/`). Iterations:
`captures/m4-r0/iterations/g1` to `g5`; measurements `logs/M4b-measure-*.txt` (temporary PlayMode test
`tools/m4b/ForestM4MeasureTemp.cs`, copied in for one run by `tools/m4b/run-measure.sh` and removed again; 0 left behind).

This stage was interrupted after iteration g5 and resumed: the code and scene of g5 are the final ones. The final run rebuilt
everything from the same sources (same kit hash `e2d51be0...`, same placements) and produced the same capture statistics.

## 1. Starting point (M4a scene, measurement base0 and base1)

The M4a scene held the unchanged planner's 271 tall firs (56 core, 215 bank). Measured on its plan (`work/M4b/scripts/tallmap.py`):
core nearest neighbour p10 12.4, p50 14.0, p90 16.7 m: an even lattice of single crowns on open litter, the orchard finding the
young firs had at M3. Bank: kept with probability `1 - 0.5 d / 45`, so the crest of the bank was its barest part.

Cost (play mode, 2560 x 1440, stopwatch with one-pixel readback, `logs/M4b-measure-base0.txt`): crouch_hide 14.29 M triangles,
19.87 M vertices, 11.95 ms; deadfall 12.84 M, 11.81 ms; pano_90 11.37 ms; trunk_close 11.21 ms. Without any tall fir crouch_hide
was 10.98 M and 10.59 ms, so the floor and the young firs already filled most of the frame.

## 2. Frame and triangle budget: knobs, in the spec's order (section 10)

Knobs 1 and 2 were spent at M3. Applied here:

| Knob | Where | Evidence | Kept? |
|---|---|---|---|
| E1's own fallback: the young firs' scan level to 4 m (spec 5.6.4: "shorten the distance to 4 m if the frame budget is missed"; section 15: never below 4 m) | `ForestKitSpec` tree table (a 4.5, b and c 6 to 4 m) | base1, run-time trial: crouch_hide 14.29 to 11.71 M. That trial was clamped at 0.99 of the screen for b and c, so it tried about 4.85 m for them; the kit built at 4 m is cheaper still (final crouch_hide 9.86 M) | Yes |
| Knob 3, tall half: tall-fir LOD0 12 to 9 m | `ForestKitSpec.TallFirDistances` | base1: crouch_hide 14.29 to 13.52 M, fir_close 8.20 to 7.44 M; no measurable ms change | Yes |
| Knob 3, young half: needle level 14 to 10 m | measured only | g4c: deadfall -1.38 M, about -0.5 ms; it brings the twig-card level (review 2 issue 1: reads as broadleaf) to 10 m everywhere | No |
| Tall LOD1 until 30 m instead of 45 m (not a spec knob) | kit of g3, measured in g2/g4 | crouch_hide -1.2 M, -0.4 ms; the far level (12x needles) then stood at 30 to 45 m | No: taken back at g5 |
| Knob 5: fern patch near level 16 to 12 m; fern cull 40 m; tall-fir cull 90 m | measured only (g4c, g4d) | at most -0.4 M and -0.17 ms, within the noise; the 90 m cull thins the backdrop | No |
| Risk 11's fallback: `UseCrossFade = false` | `ForestKitSpec.UseCrossFade` | Section 4 (a picture fault, not a cost knob); it also cut SetPass calls from 55 to 40 | Yes |

**Consequence for fir_close (the view E1 was judged on).** At 4 m the camera of fir_close no longer reaches its young b's scan
level: it sees the first thinned level (52,000 flattened needles at 1.25x; measurement g5 census: no `young/LOD0` at fir_close).
Side by side with M4a's scan (`work/M4b/look2/fir_close-m4a-vs-m4b.png`) the needles are slightly longer and thicker; the
silhouette and density are the same. The comment in `ForestKitSpec` that said otherwise (it rested on the clamped base1 trial)
was corrected. 4 m stays because deadfall is still over the frame budget (section 6), and the spec's rule is the largest distance
that keeps the budget, never below 4 m. In crouch_hide the nearest understorey firs (scale 0.55 to 0.68) also switch earlier
(their LODGroup scales with them): compare `captures/m4a/final/crouch_hide.png` with `captures/m4-r0/crouch_hide.png`, right fir.

## 3. Placement (`ForestLayout`, `ForestScatter.ScatterTallFirs`): overstory and backdrop

| Spec 7.2 | Now | Why |
|---|---|---|
| Core: minimum spacing 11 m (the code stretched it to 18 m with a grove noise) | Groves and lone firs: where a grove noise (30 m wavelength, threshold 0.52 to 0.64) is high, firs 6.5 m apart so their crowns (about 6 m across) meet; elsewhere 18 m. The thicket field cuts groves by 70 %, so groves stand on the open ground between young-fir thickets | The lattice (section 1). Section 2 of the spec: young firs "in thickets between taller firs" |
| Bank: spacing 8 m, kept with probability `1 - 0.5 d / 45` | Same spacing, kept with probability `1 - 0.5 (1 - d / 45)`: half on the foot of the bank (where the young-fir wall is the cover at eye level), all at the crest | g2 and g3: the boundary views looked up a bare brown slope at a bare skyline |
| (none) | Scattered tall firs keep 3 m outside every landmark circle | g3: two firs landed within 9 m of the spawn camera and doubled the first view's cost (trail_start 9.04 M) |
| Floor (spec 6.4): humus 0.6 within 1.2 m of a trunk | Plus a humus stamp 1.0 to 3.2 m x scale (gain 0.9) and the crown mask that cuts moss and dry needles under the crown (as the young firs have since M3 review 1) | Grove crowns met over open pale litter |

Result (`out/M4b/placements.json`, map `captures/m4-r0/tall-fir-map.png`): **316 tall firs, 83 core (a 33, b 34, c 16; 52 in
groves) and 233 bank (a 108, b 118, c 7)**; spec "about 60" core and "about 220" bank, test expectation 280 within 15 % (238 to
322). Core nearest neighbour p10 6.9, p50 9.9, p90 14.3 m; bank p10 8.1, p50 8.5 m; bank firs per 10 m band outside the
boundary 22 / 44 / 45 / 65 / 57. The set pieces stand where spec 3.4 and 3.5 put them: `fir_tree_01_a` at the spawn view
(14 m along the trail, 3 m left) and three on the clearing's rim. 831 young firs (the young firs' planner runs after the tall
firs, so the count moved from 795).

| Iteration | Tall firs (core / bank) | What it showed |
|---|---|---|
| g1 | 282 (61 / 221) | First groves; core still sparse |
| g2 | 311 (97 / 214) | Groves read; bare bank crest in the boundary views; crouch_hide 10.96 M, 11.31 ms |
| g3 | 380 (101 / 279) | Bank inverted (too many); two firs at the spawn camera; tall LOD1 to 30 m |
| g4 | 316 (83 / 233) | Bank at the spec's count, landmark margin; swarms of loose dots in the mist (section 4) |
| g5 = final | 316 (83 / 233) | Cross-fade off; tall LOD1 back to 45 m |

## 4. The cross-fade (risk 11 fallback)

In g4 every tall fir between about 88 and 110 m drew as a cloud of loose flecks across the middle of trail_mid_fwd and pano_0
(`work/M4b/look/g4_speckle.png`): the dithered LOD cross-fade is static in this pipeline, and 64 accumulated TAA frames do not
resolve it, so a tree inside a fade band shows its dither pattern. A same-run A/B with the tall firs' fade off
(`work/M4b/look/fade_ab.png`, measurement g4f) showed solid misty crowns. Spec 5.6.4 names the fallback:
`ForestKitSpec.UseCrossFade = false` (every LODGroup `LODFadeMode.None`). `Prefabs_FollowContract` already follows that switch.
Cost effect: g4f, tall firs only, same run: -0.05 to -0.07 M triangles, -0.14 ms. Across the whole kit (g4 against g5, different
runs and a different tall LOD1 distance, so not a clean A/B) the 29 views read 0.23 to 0.86 ms faster, median 0.57 ms, and SetPass
calls fell from 45 to 62 per view (g4) to 36 to 45 (g5). The price: levels now switch without a band, so a switch can be seen as a pop while walking
(not visible in stills).

## 5. Root mounds

Already in `ForestTerrain.AddRootMounds` (spec 6.2 step 8), unchanged: a raised cosine of 0.06 to 0.12 m over 1.0 to 1.4 m at
every tall trunk where the slope is at most 12 degrees and the footprint at most 16, before any placement gets its height.
Final build: **125 root mounds** for 316 tall firs (most bank firs stand on 16 to 31 degree slopes and get none). Slope limits are
re-checked after them (trail 4.9, core 25.8 degrees); `Scatter_GroundsOnFinalHeights` (a tall fir on a mound above 3 cm) passes;
largest grounding error 7.2 mm of 3 cm.

## 6. Final results

| Run | Result | Evidence |
|---|---|---|
| Compile outside Unity (editor, both test assemblies) | OK, OK, OK; again after the comment edit: OK | transcript |
| Import; kit forced; scene | Exit 0; 34 prefabs built, 0 failed (peak 5.8 GB); audit passed: 2,783 instances, 11,006 GameObjects, 18 materials, 88 meshes, 4 textures at 4096, **0 errors, 0 warnings**; grounding 7.2 mm, trail 4.9, core 25.8 degrees, spawn margin 5.82 m, corridor margin 2.58 m, physical corridors free | `logs/M4b-final-50..52-*.log`, `out/M4b/build-report.json` |
| Kit sheets | 113 sheets of 34 prefabs, `passed: true` | `captures/m4-r0/kit/`, contact sheets `contact-sheets/kit-*.png` |
| Full set (RMSE against `m4a/final`) | 32 views and crops, `passed: true`, 0 magenta, 0 cyan, nothing clipped, near-black at most 0.01 % (hollow_rim), mean luminance 0.189 (crouch_hide) to 0.623 (canopy_up), trail_start 0.281; RMSE 0.029 to 0.176 | `captures/m4-r0/capture-summary.json` |
| Debug set | 18 images (albedo, normal, clay, diffuse lighting, cascades, mip ratio of trail_start, clearing, trunk_close) | `captures/m4-r0/debugset/debug/` |
| Forest EditMode | **35 passed, 0 failed, 0 ignored** (twice) | `out/M4b/tests/forest-editmode.xml`, `rerun/` |
| Forest PlayMode | **10 passed, 0 failed** (twice) | `out/M4b/tests/forest-playmode.xml`, `rerun/`, `captures/m4-r0/playmode/` |
| Greybox PlayMode | **7 passed** (twice) | `out/M4b/tests/greybox-playmode.xml`, `rerun/` |
| Logs | 0 `error CS`, 0 `warning CS`, 0 "immutable" in all twelve | transcript |
| Checks | PackageCache md5 `8ce2f748...` unchanged; 59 pre-existing files, 0 differ; both test folders equal `backup/M4b-pre-edit/Tests`; no temporary file left; no stray `captures` folder in the project | transcript |

### Budgets of section 10 (`Budget_Counts` and `Budget_FrameTime`, play mode, 2560 x 1440)

| Metric | Value (worst of the four views) | Budget | Spec | How obtained |
|---|---|---|---|---|
| Triangles | 9.86 M (crouch_hide; M4a 14.29 M) | 11.6 M (re-based at M3a) | 8.0 M | Measured |
| Vertices | 14.68 M (crouch_hide) | 19.0 M | 10.0 M | Measured |
| Draw calls | 2,452 (long_sightline) | 4,000 | 4,000 | **Estimated** from visible renderers (the engine counter reads 0) |
| SetPass calls | 40 | 250 | 250 | Measured |
| Shadow casters | 447 | 2,500 | 2,500 | Measured |
| Texture memory | 930.3 MiB above an empty scene | 1,024 MiB (warning 819) | same | Measured; past the warning line |
| Mesh memory | 374.7 MiB | 400 MiB (warning 320) | 300 | Measured; past the warning line |
| Frame time, median at trail_start | 10.23 ms (p95 10.57); re-run 9.79 ms (p95 10.64) | 11.0 ms | 11.0 | **Stopwatch with one-pixel readback**, an upper bound (probe P5); FrameTiming reported 8 of 240 frames at about 22.7 ms, as since M1 |
| Scene totals | 2,783 instances, 11,006 GameObjects, 18 materials, 88 meshes, 4 textures at 4096 | 3,500 / 14,000, 40 / 120, 8 | same | Audit |

### All 29 views (measurement g5 on the final scene, three passes, median of the passes' medians; `logs/M4b-measure-g5.txt`)

| View | Triangles | ms | | View | Triangles | ms |
|---|---|---|---|---|---|---|
| **deadfall** | **12.43 M** | **11.34** | | trail_strip_06 | 6.85 M | 9.76 |
| pano_90 | 8.69 M | 10.69 | | trail_strip_05 | 5.68 M | 9.67 |
| hollow_rim | 9.49 M | 10.60 | | trail_strip_07 | 5.87 M | 9.64 |
| trail_strip_01 | 7.80 M | 10.43 | | trail_mid_fwd | 6.11 M | 9.55 |
| crouch_hide | 9.86 M | 10.39 | | boundary_w | 7.05 M | 9.38 |
| long_sightline | 7.20 M | 9.99 | | clearing | 4.79 M | 9.37 |
| rock_close | 8.38 M | 9.95 | | trail_strip_04 | 4.97 M | 9.33 |
| fir_close | 6.72 M | 9.94 | | pano_270 | 4.84 M | 9.32 |
| trunk_close | 7.37 M | 9.84 | | pano_0 | 5.69 M | 9.28 |
| boundary_e | 8.66 M | 9.84 | | pano_180 | 5.95 M | 9.26 |
| trail_strip_08 | 6.88 M | 9.83 | | trail_mid_back | 5.08 M | 9.25 |
| trail_start | 7.36 M | 9.78 | | boundary_n | 5.55 M | 9.10 |
| | | | | trail_strip_02 | 4.70 M | 9.08 |
| | | | | boundary_s | 5.15 M | 8.97 |
| | | | | backlit | 4.99 M | 8.86 |
| | | | | trail_strip_03 | 4.30 M | 8.78 |
| | | | | ground_close | 4.22 M | 8.45 |

**28 of 29 views are under 11 ms; deadfall is over: 11.34 ms (passes 11.31 to 11.40) and 12.43 M triangles**, above both the
frame budget and the re-based triangle budget. It is not one of the four views spec 12.3 asserts, so no test fails on it. It was
10.87 ms at M3 review 1; the tall firs add about 1 M triangles there (one LOD0 c, 12 LOD1, 68 LOD2). The knobs left either do
nothing measurable (knob 5, fern cull) or cost the look (young knob 3 brings the broadleaf-looking card level to 10 m; a 90 m
tall cull thins the backdrop); knob 6 (Resident Drawer) is out of scope. Left for the owner, with the numbers above.

## 7. How it looks (all 32 views, crops, 113 kit sheets, 18 debug images, 3 play-mode stills looked at)

- **Middle and long range read as forest.** Every eye-level view has tall firs and young firs from the near ground to the mist:
  trail_mid_fwd, trail_mid_back, pano_*, trail_strip_*, long_sightline (`contact-sheets/s1` to `s5`). The bank views look through
  the young-fir wall at tall firs up the slope to the crest. No black, white, magenta or tinted frame; no fleck clouds.
- **Weak, not broken** (open for review 3):
  - Tall firs at 9 to 45 m (LOD1, needles 6.4x for a) and many crowns in the mist read rounded and leafy, more poplar or larch
    than fir (trail_mid_fwd centre, long_sightline right). The outline is the scan's; the leafiness is the enlarged needles.
  - The floor between groves is still open litter at middle range; the overstory does not change the M2 floor.
  - `fir_tree_01_c`'s root skirt is triplanar bark, a dark flat flare on the litter (long_sightline, nearest trunk; crop
    `work/M4b/look2/ls_skirt.png`). Measured (`work/M4b/scripts/skirt.py`): the skirt reaches 0.8 m out at 0.1 to 0.2 m height,
    so sinking c cannot hide it without burying the root flare; the fix is a ground-textured triplanar material for it (not done).
  - Tall a's LOD0 from below (canopy_up) still shows needles as blades (M4a finding; risk 1(d) not applied).
  - fir_close shows the first thinned level, not the scan (section 2); crouch_hide's nearest understorey firs too.
  - overview_ortho shows the bank's young-fir wall as rows of crowns from above.

## 8. Deviations recorded here

| # | Spec | Now | Why |
|---|---|---|---|
| 1 | 7.2: core tall firs, minimum spacing 11 m, about 60 | Groves at 6.5 m and lone firs at 18 m, thicket-aware; 83 in the core | Section 3 |
| 2 | 7.2: bank kept with probability `1 - 0.5 d / 45` | `1 - 0.5 (1 - d / 45)` | Section 3 |
| 3 | (none) | Scattered tall firs keep 3 m outside every landmark circle | Section 3 |
| 4 | 6.4: humus 0.6 within 1.2 m of a tall trunk | Plus a crown-wide humus stamp and the crown mask | Section 3 |
| 5 | 5.6.4: young-fir scan level 6 m (a 4.5 m) | 4 m for all three (spec's own fallback) | Section 2 |
| 6 | 5.6.4: tall-fir LOD0 until 12 m | 9 m (knob 3, tall half) | Section 2 |
| 7 | 5.6.4: `UseCrossFade = true` | `false` (risk 11 fallback) | Section 4 |
