# M4 review round 1 (`captures/m4-r0` to `captures/m4-r1`)

Written 2026-10-14. Paths are relative to `build/` unless they start with `Assets/`. Originals of every changed file:
`backup/M4r1-pre-edit/`; diffs: `out/M4r1/*.diff`. Iterations: kit `captures/m4-r1/iterations/k1`, scenes `i1` to `i4`
(contact sheets in each); measurements `logs/M4r1-measure-i1.txt` to `-i4.txt` and `-final.txt` (temporary PlayMode test
`tools/m4r1/ForestM4MeasureTemp.cs`, copied in for one run by `tools/m4r1/run-measure.sh` and removed again). Final transcript:
`logs/M4r1-final-sequence.txt`. Comparison crops: `work/M4r1/look/`. Scripts: `work/M4r1/scripts/` (`atlasmean.swift`: mean
colour of an atlas box; `table3.py`: three-pass measurement table; `sight.py`: the composition lens's sightline estimate).

**Result: rebuild, audit and all three test runs are green**, with every one of the 29 measured views inside the 11 ms frame
budget (median of three passes, stopwatch). Forest EditMode 35 of 35, forest PlayMode 10 of 10, greybox 7 of 7; audit passed with 0
errors and 2 warnings (instances 3,097 of 3,500 and GameObjects 12,203 of 14,000, both past their warning lines). The 15 critical and
high findings were all judged real except one diagnosis (row 4): 8 are fixed (rows 1, 3, 5, 6, 7, 8, 11, 13), 5 partly fixed (2, 10,
12, 14, 15; the young-fir card levels of row 15 are not fixed), row 4 partly rejected, and row 9 (cross-fade) measured and left for
the owner. Full set: `captures/m4-r1`.

The final sequence ran in two parts. The first (`logs/M4r1-final-sequence.txt`: import, forced kit, scene, kit sheets, full capture)
finished; every later launch was refused by `tools/unity.sh` with "EDITOR OPEN". No editor was open: the rule's own check
(`pgrep -f "Unity.app/Contents/MacOS/Unity -projectpath"`) found nothing, and the process unity.sh matched was a zsh shell of the
orchestrating workflow (pid 6890), waiting for this task, whose command line contains the text `Unity.app/Contents/MacOS/Unity
-batchmode` (the false positive STATE.md's M2 review 1 warns about). It was not touched. The rest (`logs/M4r1-final-sequence-2.txt`:
debug set, measurement, the three test runs, checks) ran with `tools/m4r1/unity2.sh`, which refuses while any process whose executable
is Unity runs (`ps comm`) or while the rule's check matches, and is otherwise `unity.sh`.

## 1. What was measured before changing anything

- **Crown colour** (`work/M4b/scripts/crowntone.py` on the kit LOD sheets, crown pixels darker than the sky): the scan's crown
  reads green 1.03 times red and blue 0.68 times green; every thinned tall-fir level 1.21 to 1.29 and 0.40 to 0.47, a saturated
  yellow-green. The atlas itself (`work/M4r1/scripts/atlasmean.swift`, needle box): fir_tree_01_twig linear (0.104, 0.141, 0.042),
  G/R 1.36, B/G 0.30, 9 % lighter than the young firs' atlas of the same hue. The scan only reads olive-grey because its
  hair-thin needles mix with what is behind them; thinned levels show the needle's own colour.
- **Young-fir twig sprays** (the card levels): same hue as the needles (G/R 1.29 to 1.40) and darker (luminance 0.064 to 0.126
  against 0.116). Their broadleaf read is the card shape, not colour.
- **Fern atlas**: linear (0.070, 0.104, 0.017), G/R 1.48, B/G 0.16, a saturated lime.
- **Census of the frame** (g5 log): the deadfall view drew 14 young-fir needle levels and one scan; crouch_hide already drew four
  young-fir **scans** (`young/LOD0 4 x = 1.697 M`).

## 2. The critical and high findings, one by one

| # | Lens, finding | Real? In scope? | What was done | Evidence |
|---|---|---|---|---|
| 1 | Photorealism: tall firs at 9 to 110 m read as poplar, birch, bamboo (LOD1 6.4x / 6.1x, LOD2 12x, lighter and greener) | Real, in scope | (a) `fir_tree_01_twig` darkened and taken toward grey-green, linear factor (0.78, 0.68, 0.82) (`ForestMaterials.BaseColourTint`): LOD1's crown now G/R 1.03 to 1.09, B/G 0.59 to 0.68, the scan's old perceived colour. (b) LOD1 keeps 35,000 (a) and 18,000 (b) needles, 4.8x and 4.9x (was 6.4x, 6.1x). (c) Risk 1(d) for a: bank only. (d) LOD1 cap 4 not set: at the new counts no cap is reached and area is preserved. (e) Crown core at LOD2 (risk 1(c)) tried and rejected (see 3). LOD2 is unchanged but for the colour: 12x needles at 35 to 110 m. | k1 sheets `captures/m4-r1/iterations/k1/kit/fir_tree_01_*__lods_25m.png`; crowntone numbers in section 1 and 3; final views |
| 2 | Photorealism: parkland, not a dim stand (44 core firs/ha, open litter, 30 to 45 % sky) | Real, in scope; partly fixed | Groves larger and tighter (spacing 6.5 to 5.5 m, noise threshold 0.52-0.64 to 0.48-0.60, lone firs 18 to 16 m): core tall firs 83 to 116 (78 in groves; nearest neighbour p50 9.9 to 7.5 m). The finding asked for 140 to 160: at 152 (i1/i2) 14 of 29 views were over 11 ms. Bank foot closed (no gaps in the first 8 m). Fern cap 900 to 1,050 with a fern band along stand edges. Branches 67 to 102 (25 inside thickets). Not done: young-fir wall clumping and bank understorey; sky share is still large in eye-level frames | `out/M4r1/placements.json`, `captures/m4-r1/tall-fir-map.png`, measurement i2 |
| 3 | Photorealism: frame time not shown inside budget (deadfall 11.34 ms, 12.43 M; FrameTiming 22.78 ms unexplained) | Real, in scope | Young firs keep 7.5 m from the deadfall centre; fern patches keep 1.5 m from its log axes. Deadfall 10.68 ms and 10.34 M triangles (was 11.34 ms, 12.43 M). Every view measured three times (section 4). The stopwatch's own floor measured: 4.4 to 5.1 ms with the whole Environment (terrain included) switched off, so the forest's net cost is the reading minus that. FrameTiming still reports 8 of 240 frames at about 22 ms in the Editor: not explained; a development-build number is M6 (out of scope). Budget_Counts still asserts the spec's four views; adding deadfall and pano_90 is a test change left to the owner | `logs/M4r1-measure-final.txt`, `captures/m4-r1/playmode/frame-time.txt` |
| 4 | Photorealism: flat, glossy straps at the hide spots (crouch_hide, hollow_rim, fir_close) | Partly rejected | Twig smoothness remap 0.4 to 0.28 on both twig materials (the sheen). Rejected: the diagnosis for crouch_hide. The census shows crouch_hide already drew four young-fir scans at M4b; the strap-like needles at under a metre are the scan's own needle cards, not a thinned level. Holding the scan to 4 m for firs below scale 1 (per-instance first transition times the scale) was built and measured (i2/i3): one more scan at crouch_hide, +0.27 ms, about 0.5 M triangles at a Budget_Counts view then at 11.40 of 11.6 M, and no visible change in the crops (`work/M4r1/look/hide_compare.png`): taken out. Returning b and c to a 6 m scan: rejected on E1/base1's numbers (the 4 to 6 m difference was 2.6 M triangles at crouch_hide). fir_close shows the first thinned level (1.25x) at the 4 m switch, as M4b reported; the owner should know E1's judged view no longer shows the chosen arm | `work/M4r1/look/hide_compare.png`, `logs/M4r1-measure-i2.txt` (noHold) |
| 5 | Photorealism: c's root skirt is a dark bark mat | Real, fixed | `ForestPrefabs.SplitSkirt`: trunk_c triangles under 0.3 m with an upward normal (27,406 triangles, 1.16 m2) are their own sub-mesh, drawn with `forest_ground_06__ground.mat`, the humus terrain layer's maps, tint, wetness and normal strength projected from above at its own tile and offset, so it continues the terrain's mapping. LOD0 of c has 4 sub-meshes (contract allows 4) | `work/M4r1/look/skirt_i3.png` (pano_180), final views |
| 6 | Photorealism: canopy_up a bamboo crown; trunk_a smeared above 3 m | Real, fixed by risk 1(d) | The spawn's tall fir (spec 3.5: a) is c; a stands on the bank only, so neither a's LOD0 nor trunk_a's strip is seen up close. canopy_up now shows c's half-dead crown at 1.6x from below; it is still one crown against open sky | final `canopy_up.png`, `trunk_close.png` |
| 7 | Technical: crowns at 9 to 45 m read as bamboo, willow, poplar | Real, in scope | As 1. `TallFirLod0Factor = 1` (knob 4 back) tried: Mesh Memory 408.4 MiB, over the 400 MiB budget (`out/M4r1/tests/pre/`), and no longer needed once canopy_up and trunk_close look at c: kept at 0.7. Tall LOD1 now ends at 35 m (spec 45): with the denser LOD1 and groves the frame was over budget, and 35 m measured -0.1 to -0.5 ms | measurements i2 (tallLod1At35), pre-run PlayMode |
| 8 | Technical: frame and triangle budgets missed at deadfall, nearly at others | Real, fixed | As 3. Knob 3 for the young firs measured (i2: -0.1 to -0.3 ms) and not applied: it brings the card level, which still reads broadleaf, to 10 m | `logs/M4r1-measure-i2.txt` (youngKnob3) |
| 9 | Technical: LOD pops with cross-fade off | Real (not visible in stills), not applied | Cross-fade between levels with a hard cull on the last level (no fade band to nothing, so no fleck clouds) was measured at run time (i2, config `fadeLevels`): +0.5 to +0.9 ms and SetPass 40 to 57 at the eight heaviest views, all eight over 11 ms. The frame budget is the gate, so `UseCrossFade` stays false; the owner decides between pops and budget. Applying it would also change `Prefabs_FollowContract`'s pinned width (0.2 on every level). No walking capture was made | `logs/M4r1-measure-i2.txt` |
| 10 | Technical: canopy closure about 15 %; plantation rows; bare bank soil | Real; partly fixed | Groves (2); bank tall firs ask for 0.75 to 1.3 times the 8 m spacing by position hash (rows broken; still a band along the boundary from above); branches 102; humus stamp under tall crowns gain 0.9 to 1.2 and out to 3.6 m x scale. Not done: stump patches under groves; canopy closure from above is still low | final `overview_ortho.png` |
| 11 | Technical: c's root skirt | Real, fixed | As 5 | as 5 |
| 12 | Composition: open even-aged park | Real; partly fixed | As 2; sightline stations and the clearing rim add young-fir stands. Not done: glade spacing 26 to 14 m, thicket ramp 0.55 to 0.48, young-fir trail clearance 5 m | |
| 13 | Composition: sightlines far too long | Real, fixed against the lens's own target | Sightline-break stations (`ForestLayout.SightlineStations`: every 27 m, alternate sides, 11 m off the line, thicket field at least 0.9 in a 3.6 m disc). Estimate (`work/M4r1/scripts/sight.py`, the lens's method): median clear sight along the trail 25.7 to 21.5 m, rays reaching 50 m 21 to 13 %, per 25 m of trail 19 / 29 / 37 / 28 / 20 / 23 to 20 / 22 / 19 / 24 / 23 / 17 m (target: under 25 m along the whole trail). Not done: boulder clusters and scattered logs near the trail | `out/M4r1/sightlines.txt` |
| 14 | Composition: world edge reads as walkable forest on a hill | Real; partly fixed | Wall gaps only beyond 8 m outside the boundary; young firs keep 2.6 m (was 1.8 m) from first-person capture cameras (a scale-1.19 fir's branch crossed boundary_n). Not done: 2.0 m foot spacing, bank understorey, perimeter rocks or logs, a steeper bank | final `boundary_*.png` |
| 15 | Composition: species read fails in the middle distance (tall and young) | Tall firs: fixed (1). Young-fir card levels: not fixed | The sprays are the needles' hue and darker (section 1); the broadleaf read is the card shape. Returning 14 to 42 m to flattened needles was the M3 bamboo finding. Left open for the owner | |

## 3. Tried and taken back

| Change | Why it went |
|---|---|
| Crown core for tall a and b at LOD2 (risk 1(c)) | A solid, faceted brown lump as wide as the crown, b's twisted into a spiral (`iterations/k1/kit/fir_tree_01_b__lods_45m.png`, `fir_tree_01_a__lods_70m.png`); coverage 1.15 to 1.23. `TallFirCrownCoreFromLevel = 99`; the generated `fir_tree_01_twig__crowncore.mat` was removed |
| Core groves at 5 m, threshold 0.45-0.57, lone 15 m (152 core firs) and b's LOD1 at 22,000 needles | i2: 6 of 8 heavy views over 11 ms, crouch_hide 13.5 M triangles |
| Scan held to 4 m for young firs below scale 1 | Section 2, row 4. Its first version also reset each LODGroup's capped size (SetLODs recomputes it), which put scans in every view (i1) |
| `TallFirLod0Factor = 1` | Mesh Memory 408.4 MiB of 400 |
| Cross-fade between levels | +0.5 to +0.9 ms (row 9) |

## 4. Final numbers

| Metric (Budget_Counts, play mode, 2560 x 1440, worst of the four views) | Value | Budget | M4b |
|---|---|---|---|
| Triangles | 10.51 M (crouch_hide) | 11.6 M (re-based at M3a; spec 8.0 M) | 9.86 M |
| Vertices | 15.47 M (crouch_hide) | 19.0 M | 14.68 M |
| Draw calls | 2,590 (long_sightline), **estimated** (the engine counter reads 0) | 4,000 | 2,452 |
| SetPass calls | 40 | 250 | 40 |
| Shadow casters | 495 | 2,500 | 447 |
| Texture memory | 930.3 MiB | 1,024 MiB (warning 819) | 930.3 |
| Mesh memory | 383.0 MiB | 400 MiB (warning 320) | 374.7 |
| Frame time at trail_start | 9.54 ms median, p95 10.61 (**stopwatch**, an upper bound; FrameTiming 22.47 ms on 8 of 240 frames, unexplained) | 11.0 ms | 9.79 to 10.23 |

All 29 views (`out/M4r1/measure-final-table.txt`; three passes, median of the passes' medians; delta against M4b's g5):
0 over 11 ms (M4b: 1, deadfall), 24 over the 9.5 ms warning line (M4b: 16). Worst: pano_90 10.70 ms (passes 10.66 to 10.82,
p95 11.84; M4b 10.69), deadfall 10.68 ms (10.47 to 10.85, p95 11.40; M4b 11.34) and 10.34 M triangles (M4b 12.43 M), crouch_hide
10.68 ms (M4b 10.39). Best: ground_close 8.56 ms. The p95 of six views is above 11 ms; the budget is on the median.
Stopwatch floor (configuration `nothing`: the whole Environment, terrain included, switched off; sky, fog, post-processing, the
readback): 4.4 to 5.1 ms at every view, so the forest itself costs about 4 to 6 ms of these readings.

Scene: 3,097 placements, 369 tall firs (core 116: b 67, c 49; bank 253: a 117, b 122, c 14), 900 young firs, 93 rocks, 8 logs,
10 stump patches, 102 branches, 1,050 fern patches, 328 fern clumps, 237 grass patches; 161 root mounds; grounding error 10.5 mm
(3 cm allowed), trail slope 4.9 and core slope 25.8 degrees, spawn margin 5.70 m, corridor margin 2.58 m, physical corridors free;
19 materials, 88 meshes, 4 textures at 4096 (`out/M4r1/build-report.json`). Kit: 34 prefabs, tall-fir levels (`out/M4r1/kit-report.json`):
a 222,700 / 73,825 / 8,453 triangles (needles 3.71x / 4.76x / 12x), b 205,328 / 39,708 / 5,465 (2.69x / 4.89x / 12x), c 122,420 /
27,800 / 4,402 (1.63x / 2.68x / 7.09x); coverage 0.98 to 1.12; LOD0 to 9 m, LOD1 to 35 m, culled at 110 m.

## 5. Medium and low findings

| Finding | Done? |
|---|---|
| Young firs' card levels read broadleaf (photorealism, technical) | No (section 2, row 15) |
| No sense of standing under a canopy; floor as bright as open ground | Humus stamp under tall crowns stronger (gain 1.2, to 3.6 m); denser groves. The probe bake with sky occlusion is M5: out of scope, recommended to the owner |
| Ferns bright lime | Yes: `fern_02` base colour linear factor (0.80, 0.68, 0.90) |
| Dead-branch slivers beside tall trunks | Yes: `TallFirTrunkOnlyFromLevel` 2 to 1 (LOD1 draws the trunk alone) |
| LOD pops, walk capture | Measured, not applied (row 9); no walk capture |
| Hollow mud a grey asphalt disc (three lenses) | Yes: mud tint (0.72, 0.64, 0.56), smoothness lift 0.55 to 0.40, ferns down the sides (inner ring 2.2 to 1.2 m). No log or branches in the bowl |
| Litter too warm and uniform | No: it changes the floor the owner accepted at M2; for the owner |
| Mist a uniform veil (fog base) | No: a preset change that moves every view; for the owner |
| Sky's brightest patch vs key light | Out of scope (M5, sky rotation) |
| Rectangular moss and litter blocks | Height-blend transition 0.015 to 0.025 (`ForestMaterials.TerrainHeightTransition`); ground_close's ruts unchanged at 1:1 (`work/M4r1/look/height_ab.png`) |
| fir_close no longer shows E1's arm | Reported (row 4); the 4 m scan distance stays |
| Hooked leader of a recurring | Resolved by risk 1(d) (a only on the bank) |
| Deadfall illegible (two lenses) | Yes: young-fir keep-out 7.5 m, no fern patch within 1.5 m of a log axis, the two patches between the trunks are single clumps, ring clumps at the log ends |
| Frame-time method not validated | Stopwatch floor measured (row 3); FrameTiming unexplained; reported as a stopwatch upper bound |
| Duplicate textures; readable render meshes | No: memory-layout changes whose effect in the Editor is unverified; for the owner. Mesh Memory is 383.0 of 400 MiB, Texture Memory 930.3 of 1,024 |
| Upper trunk of a smeared (low) | a is on the bank only now |
| Hero rock's lip (low) | No |
| Clearing does not read as a clearing | Rim floor of the thicket field 0.75 between 13 and 18 m, open within 6 m of a trail. No grove added behind the rim firs |
| Thickets poor concealment at crouch height | Fern band along stand edges, 25 branches inside thickets. Understorey share unchanged |
| Age structure (veterans, snags, deadwood) | c is 40 % of the core (was 15 %); branches 102. Scale range and log count unchanged |
| Trunk_b seam at 1.8 m | No: a tinted triplanar bark is a new material and was not built; b's scanned root flare also renders darker than the floor (b at 5 m in pano_0): giving it a ground skirt would make b's LOD0 five sub-meshes, over the contract's four |
| Trail rhythm, shoulder grass (low) | Stations give the alternation; shoulder grass unchanged (the grass count is pinned at 220 within 15 %) |
| Game-trail cue (low, optional) | No |

## 6. Deviations recorded here

| # | Spec | Now | Why |
|---|---|---|---|
| 1 | 3.5: `fir_tree_01_a` at the spawn; 3.4: the clearing's rim firs | c at the spawn; rim b, c, b | Risk 1(d) (row 6) |
| 2 | 7.2: core mix a 45 : b 40 : c 15 | 0 : 60 : 40 | Risk 1(d); c alone reads as a dying stand |
| 3 | 7.2 / M4b: core groves 6.5 m, lone 18 m, threshold 0.52-0.64 | 5.5 m, 16 m, 0.48-0.60 | Row 2 |
| 4 | 7.2: bank spacing 8 m | 8 m x 0.75 to 1.3 by position hash | Row 10 |
| 5 | 7.2: young firs on the bank (M3: gaps everywhere) | No gaps in the first 8 m | Row 14 |
| 6 | (none) | Young firs keep 7.5 m from the deadfall; fern patches 1.5 m from its log axes | Rows 3, 8 |
| 7 | 7.2: thicket field | Plus sightline stations and a clearing-rim floor | Rows 12, 13 |
| 8 | 7.2: 45 scattered branches; fern cap 900 | 80 (25 in thickets); 1,050 with a stand-edge band | Rows 2, 10 |
| 9 | 5.6.4: tall LOD1 foliage 40,000 / 24,000; LOD1 until 45 m | 70,000 / 36,000; until 35 m | Rows 1, 7 |
| 10 | 5.6.4: wood of the far levels | Tall firs: trunk alone from LOD1 (was LOD2) | Medium finding (slivers) |
| 11 | 5.5: materials | `fir_tree_01_twig` and `fern_02` base colour factors; twig smoothness 0.28; mud tint and wetness; `forest_ground_06__ground.mat` (new) | Rows 1, 4, 5; medium findings |
| 12 | 5.5: terrain `_HeightTransition` 0.04 (M2: 0.015) | 0.025 | Medium finding |
| 13 | Spawn-camera rule (M3 review 1): 1.8 m | 2.6 m | Row 14 |
| 14 | `ForestWorldTests.ExpectedCounts` (rule 5: a pinned count changes with its tunable) | Tall firs 280 to 360, young firs 770 to 880, branches 65 to 100, fern cap 900 to 1,050 | Rows 2, 8, 12, 13. The correctness tests are unchanged |
