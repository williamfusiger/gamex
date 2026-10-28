# M4 review round 2 (`captures/m4-r1` to `captures/m4-r2`)

Written 2026-10-15. Paths are relative to `build/` unless they start with `Assets/`. Originals of every changed file:
`backup/M4r2-pre-edit/`; diffs: `out/M4r2/*.diff`. Iterations: kit `captures/m4-r2/iterations/k1` to `k6`, scenes `s1` to `s8`
(contact sheets in each); measurements `logs/M4r2-measure-s2.txt` to `-s6.txt` and `-final.txt` (temporary PlayMode test
`tools/m4r2/ForestM4MeasureTemp.cs`, copied in for one run by `tools/m4r2/run-measure.sh` and removed again; configurations
`tallCull80`, `tallLod1At25`, `noBankFerns`, `noMid` added this round). Final transcript: `logs/M4r2-final-sequence.txt`.
Comparison crops: `work/M4r2/look/`. Scripts: `work/M4r2/scripts/` (`uvcrop.swift`: crops UV boxes of an atlas and measures their
alpha coverage; `sight2.py`: the composition lens's porous sightline test). Unity was launched only through
`tools/m4r2/unity2.sh`, one process at a time, after the rule's own check (`pgrep -f "Unity.app/Contents/MacOS/Unity -projectpath"`).

**Result: the rebuild and the audit pass, and the gate's three test runs on the final build are green** (forest EditMode 35 of 35,
forest PlayMode 10 of 10, greybox 7 of 7: `out/M4r2/tests/gate/`, logs `logs/M4r2-gate-6*.log`). **But `Budget_FrameTime` is
marginal and failed 3 of 11 forest PlayMode runs**: each of the three final sequences (`logs/M4r2-final-run1/`, `-run2/`, and
`logs/M4r2-final-sequence.txt`) runs the suite right after the 29-view x 3-pass cost measurement, and there it read 11.22, 11.80 and
11.82 ms against 11 ms; run alone (eight runs on the final or near-final build) it read 9.80 to 10.68 ms and passed. In those same
sequences the empty-scene stopwatch floor (configuration `nothing`, measured last) was 5.5 to 6.1 ms against 4.9 to 5.2 ms earlier in
the session and 4.4 to 5.1 ms at M4r1: the machine itself was about 1 ms slower after minutes of sustained rendering. The scene's own
cost at trail_start is about 0.3 ms above M4r1's (rested 9.8 to 10.1 ms, M4r1 9.54). The owner should treat the frame budget as met
with about 1 ms of margin on a rested machine and not met under sustained load in the Editor; the real number needs the M6
development build.

Full set: `captures/m4-r2` (32 views and crops, 113 kit sheets, 18 debug images, 3 play-mode stills, contact sheets `s1` to `s6`,
`debug`, `playmode`, `kit-tall-final`, `kit-young-final`, `tall-fir-map.png`). All looked at; no black, white, magenta or tinted frame
(0 magenta, 0 cyan, 0 clipped, crushed black at most 0.01 %, luminance 0.183 to 0.623).

## 1. The critical and high findings, one by one

Every finding was looked at in the m4-r1 images it cites before anything changed. All fifteen are real; none was rejected
outright. Several repeat each other (tall crowns: 1, 6, 7, 15; young card levels: 2, 8, 15; stand structure: 3, 9, 12; world
edge: 5, 14), so they were fixed together.

| # | Lens, finding | Real? In scope? | What was done | Evidence |
|---|---|---|---|---|
| 1 | Photorealism: tall crowns 9 to 110 m read as poplar, eucalyptus, bamboo (LOD2 straps at 12x, LOD1 lollipops) | Real (kit sheets `m4-r1/kit/fir_tree_01_b__lods_35m.png` last column: straws; `pano_270_crop`) . In scope | Far level (LOD2): needle cap 12 to 6 (a: 8) and more needles kept (a 8,000 to 12,000, b 5,000 to 9,000 triangles); straps are gone, the crown is a fine ragged fir crown (`work/M4r2/look/cmp_tall_far.png`, `cmp_b25_k6.png`). Middle level (LOD1): cap 3.5 (was 4.8 / 4.9x). The suggested "outerness weight 0.85 at LOD2 only" was not built: the priority key is shared by all levels so that the kept sets are nested (spec 5.6.3, `Thinner_LodsAreNested`); a level-specific key would break that. Shoot cards for the tall firs (see row 2) were built and rejected (k2): fir_tree_01's tufts filled into the rounded clumps of a broadleaf crown | kit `iterations/k3`, `k6`; final `kit/fir_tree_01_*__lods_25m/35m/70m.png` |
| 2 | Photorealism: young firs 14 to 85 m (card levels) read as broadleaf saplings | Real (`m4-r1/kit/fir_sapling_medium_b__lods_14m.png`, `backlit_crop`). In scope | Cards now use single shoots only: five tight UV boxes around one bottle-brush shoot each, cut from the atlas's sprays (aspect 0.36 to 0.46; `work/M4r2/look/shoots1.png`), never the whole round sprays. Card cells are cylindrical and 2.5 times as long along the radius as across (`ForestFoliageThinner.CardCellKey`), so each card is a stroke along its branch, as the reviewers asked ("orient each card along its branch axis"); fill 0.85. The silhouette is now horizontal brushy tiers (`work/M4r2/look/cmp_y3.png`, `cmp_y.png`). Card count unchanged (the suggested 16,000 was not needed and not affordable); the switch stays at 14 m (moving it to 20 m doubles the needle level's count) | kit `iterations/k1` to `k3`; final `backlit_crop`, `pano_270_crop`, `trail_start` |
| 3 | Photorealism: thinned parkland, not a dim stand; canopy_up one crown against sky | Real. In scope (canopy and placement), partly fixed | Groves stand over thickets (thicket cut 0.7 to 0.35), larger (threshold 0.48-0.60 to 0.44-0.56), lone tall firs 16 to 13 m: core tall firs 116 to 152 (110 in groves; nearest neighbour p50 7.3 m). A pole-stage cohort (risk 1(e)): 8 % of eligible core young firs at 1.40 to 1.55, held to the regular LOD distances. Spawn: one more tall fir near canopy_up (c beside the camera), so canopy_up shows two overlapping crowns and a third's branch (was one crown); a b behind the thicket was built and taken out again for the frame budget. Still a lot of sky in eye-level frames; the stand is not closed | `tall-fir-map.png`, `canopy_up.png`, `overview_ortho.png` |
| 4 | Photorealism: light under trees flat | Real. Full fix out of scope (M5 probe bake with sky occlusion; recommended to the owner). Partial fix in scope, done | Humus layer darker and cooler (`diffuseRemapMax` 1.0 to (0.80, 0.82, 0.86)); humus under tall crowns gain 1.2 to 1.6; the ring at every tall trunk 0.6 to 1.2 | `debugset/`, `trail_mid_fwd.png` |
| 5 | Photorealism: world edge a bare scraped slope, firs in rows | Real. In scope; partly fixed | Bank young firs: size and spacing follow a clump noise (scale varies along the bank, clumps and gaps beyond the closed foot). Bank tall firs: spacing factor 0.6 to 1.5 from a 22 m clump noise (60 %) and the hash (40 %). Fern patches up the bank to 14 m (bank drifts; cap 1,050 to 1,250); 25 dry branches on the bank. Paint: under the young-fir wall the slope's stony soil is cut by 65 %, humus doubled, moss patches added | `boundary_n/e/s/w.png`, `overview_ortho.png` |
| 6 | Technical: tall crowns bamboo/poplar (4.76x / 4.89x / 12x) | Real; as row 1 | LOD1 cap 3.5 (coverage 0.96 to 0.98, gate 0.80), LOD2 cap 6 (a 8). `ThinByGroup` not tried: it is a global switch (young firs too) and grouped thinning failed on the young firs at M3 review 1 | as row 1 |
| 7 | Technical: pom-pom on a stick (LOD1 trunk alone) | Real. In scope; fixed | LOD1 draws the trunk and the branches inside the crown again (`ForestKitSpec.WoodAt`: `TrunkAndCrownBranches` for thinned level 1); the dead branches below the crown base (3rd percentile of twig height), which made the slivers of M4r1, stay out. Wood is chosen per source sub-mesh, so b's triplanar group (branches and off-atlas trunk) keeps its trunk on the far level. No slivers in the below sheets | final `kit/fir_tree_01_b__below_08m.png` |
| 8 | Technical: young card levels broadleaf | Real; as row 2 | As row 2 | as row 2 |
| 9 | Technical: open parkland, ferns isolated | Real; partly fixed | As row 3; fern patch cap 1,250 (the extra demand is the bank); thicket spacing 2.8 to 2.4 m, glade spacing 26 to 32 m, understorey share 0.3 to 0.4. Young-fir knob 3 measured and not taken (cards at 8 to 10 m are blotchy, `work/M4r2/look/cmp_c08.png`) | |
| 10 | Technical: frame budget not robustly met, FrameTiming 22 ms unexplained | Real. Partly in scope | Paid for this round's density: tall LOD1 35 to 30 m, tall cull 110 to 85 m, fern far level 50 to 40 m, young LOD1 needles 52,000 to 47,000 (a 61,000 to 55,000), pole-stage firs held to the regular switch distances. The stopwatch is bound by draw submission in the Editor (0.4 M fewer triangles from `tallLod1At25` moved it by 0.05 ms; switching off the first view's firs moved it by 1.1 to 1.6 ms), so draws were cut where nothing shows: the young firs' far level (42 to 85 m) draws its cards without the 20 cm wood (one draw, not two; this also removes the dark wedge at the foot of the far cards), the tall firs' root skirt is drawn by LOD0 only, and the tall far trunk is one draw (b's flare and upper trunk share the triplanar bark from 30 m). Measured -0.25 to -0.37 ms at the heavy views (`logs/M4r2-measure-f2.txt` against `-f1.txt`). Result in section 3. FrameTiming: not explained (see section 3). Adding deadfall, pano_90 and boundary_s to `Budget_Counts` / `Budget_FrameTime` is a test change rule 5 does not cover: left to the owner. Development build and Resident Drawer are M6: out of scope | `out/M4r2/measure-final-table.txt` |
| 11 | Composition: first view does not match spec 2's sentence | Real. In scope; fixed in part | `ForestLayout.SpawnExtras`, placed from a stream of their own (so neither the set pieces and boulder field nor the classes' scatter reshuffle): a second rank of three young firs behind the thicket (1.05 to 1.20; 1.15 to 1.30 cost crouch_hide its triangle budget), two young firs right of the trail behind the boulders, a five-patch fern drift under the thicket, two b firs on the axis 30 to 35 m out, and the canopy c of row 3. Built with four, three and two respectively and trimmed: every fir here is drawn by trail_start, the view `Budget_FrameTime` asserts, and that test read 11.22 and 11.80 ms in the first two final runs (section 3) | `trail_start.png` |
| 12 | Composition: even-aged plantation | Real; partly fixed | As rows 3 and 9: groves over thickets, tighter thickets, wider glades, more understorey, pole-stage cohort, tall-fir scale 0.90-1.15 to 0.80-1.25 | `overview_ortho.png` |
| 13 | Composition: sightlines not broken | Real; partly fixed | Stations every 27 to 17 m, 3.6 to 4.5 m across, alternating sides. With the lens's porous test (`work/M4r2/scripts/sight2.py`: crowns pass 60 %, trunks block): rays clear beyond 30 m 63 to 58 %, median distance to half-blocked 38.1 to 35.0 m (`out/M4r2/sightlines.txt`; the review-1 opaque estimate: median clear sight 21.5 to 19.5 m). Target (under 30 %) not reached: wider and nearer stations moved it by one point for 22 more firs near the trail views (s8, not kept). Boulder clusters and logs nearer the trail: not done | `out/M4r2/sightlines.txt` |
| 14 | Composition: world edge an open walkable hillside | Real; partly fixed | As row 5. Not done: 2.0 to 2.5 m spacing through the full 20 m band (cost), rocks on the bank foot | `boundary_*.png` |
| 15 | Composition: species read fails beyond 9 to 14 m | Real; fixed for both | As rows 1, 2, 6, 7 | |

## 2. Tried and taken back

| Change | Why it went |
|---|---|
| Shoot cards for the tall firs at LOD1 and LOD2 (k1, k2) | Rounded dark clumps of a broadleaf crown at 25 to 70 m (`work/M4r2/look/cmp_b25.png`, `cmp_tall_far.png`) |
| Cubic card cells with shoot sprites (k1) | Still blotchy tufts; the radial cells (k2) made branch strokes |
| Tall far level 16,000 (a) / 12,000 (b) triangles; tall cull 90 m; tall LOD1 to 35 m; second rank at 1.15 to 1.30; pole-stage 12 % at 1.45 to 1.65 | crouch_hide 12.47 M and then 11.76 M triangles against the 11.6 M `Budget_Counts` budget (s2, s3) |
| Young-fir knob 3 (needle level 14 to 10 m) | Cards at 8 to 10 m blotchy (`cmp_c08.png`); the measurement configuration of M4b gave more triangles, not fewer, at this kit |
| Understorey share 0.35 (s7) | Fewer understorey firs means more full-size ones with longer LOD ranges, not fewer firs |
| Sightline stations 5.5 m across at 9.5 m (s8) | 1 point of the sight test for 22 more young firs near the trail views |
| First-view additions at full size (four second-rank firs, three right of the trail, a b canopy fir) | `Budget_FrameTime` at trail_start 11.22 and 11.80 ms in final sequences 1 and 2; trimmed to three, two and none |

## 3. Final numbers

| Metric (Budget_Counts, gate run, play mode, 2560 x 1440, worst of the four views) | Value | Budget | M4r1 |
|---|---|---|---|
| Triangles | 10.88 M (crouch_hide) | 11.6 M (re-based at M3a; spec 8.0 M) | 10.51 M |
| Vertices | 16.49 M (crouch_hide) | 19.0 M | 15.47 M |
| Draw calls | 1,812 (crouch_hide), **estimated** (the engine counter reads 0) | 4,000 | 2,590 |
| SetPass calls | 41 | 250 | 40 |
| Shadow casters | 483 | 2,500 | 495 |
| Texture memory | 930.3 MiB | 1,024 MiB (warning 819) | 930.3 |
| Mesh memory | 382.4 MiB | 400 MiB (warning 320) | 383.0 |
| Frame time at trail_start | 9.81 ms median, p95 10.54 (**stopwatch**, an upper bound, gate run); 11.22 / 11.80 / 11.82 ms in the three final sequences (failed) | 11.0 ms | 9.54 |

FrameTiming: 8 of 240 frames reported in every run (22.5 to 39.1 ms), the same pattern as M4r1. Not explained: with
`enableFrameTimingStats: 0` the Editor returns a few stale frames; probe P5 rejected the method at M0 and the test does not use it.

Cost of all 29 views, third final sequence (`out/M4r2/measure-final-table.txt`, median of three passes, against M4r1's final): 3 over
11 ms (pano_90 11.61, passes 11.19 to 13.88; deadfall 11.23; crouch_hide 11.13), 28 over 9.5 ms; worst triangles 10.88 M, worst
vertices 16.49 M. These passes include one slow pass per view (range up to +2.7 ms) and a floor of 5.5 to 6.1 ms; the same views
measured rested (`logs/M4r2-measure-f2.txt`, `-f3.txt`): crouch_hide 10.69 to 10.94, deadfall 10.72 to 10.89, trunk_close 10.75 to
10.84, pano_90 10.56, fir_close 10.64, trail_start 9.86 to 10.07 ms. deadfall, pano_90 and boundary_s are still not asserted by any
test (owner decision, rule 5 does not cover it).

Scene (`out/M4r2/build-report.json`): 3,420 placements: 400 tall firs (core 152: b 89, c 63; bank 248: a 113, b 117, c 18), 976 young
firs (12 hand-placed; 323 understorey, 22 pole-stage), 91 rocks, 8 logs, 10 stump patches, 127 branches, 1,249 fern patches, 322 fern
clumps, 237 grass patches; 186 root mounds; grounding error 7.9 mm; 19 materials, 88 meshes, 4 textures at 4096. Audit passed with 2
warnings: instances 3,420 of 3,500 and GameObjects 13,433 of 14,000 (both past their warning lines; little room for more instances).

Kit (`out/M4r2/kit-report.json`): tall a 222,700 / 79,340 / 12,453 triangles (needles 3.71x / 3.50x / 8x), b 205,328 / 40,933 / 9,387
(2.69x / 3.50x / 6x), c 122,420 / 27,772 / 4,314 (1.63x / 2.68x / 6x); LOD0 to 9 m, LOD1 to 30 m, culled at 85 m. Young: scan to 4 m,
needles a 65,300 (1.54x), b 52,520 (1.31x), c 52,590 (1.32x) to 14 m, shoot cards 9,220 to 9,273 to 42 m and 1,196 to 1,200 (no wood) to
85 m. Coverage 0.89 to 1.09, every level inside the unchanged gate.

## 4. Medium and low findings

| Finding | Done? |
|---|---|
| b's near-black root mat and the trunk seam at 1.8 m (three lenses) | Yes, the mat: b's branch bark is drawn with the triplanar bark of its off-atlas trunk (`ForestKitSpec.BranchesTakeOffAtlasBark`), which merges two sub-meshes and frees the slot; `fir_tree_01_trunk_b` is in `SkirtMaterials` (50,534 triangles, 1.32 m2 of root pad drawn with the humus ground material). Four sub-meshes, as the contract allows. The seam at 1.8 m: not changed (tint of the triplanar bark not built) |
| Twig smoothness glossy at hide distance (two lenses) | Yes: `_SmoothnessRemapMax` 0.28 to 0.15 on both twig materials |
| Hollow mud grey disc (two lenses) | Partly: mud wetness 0.40 to 0.30; no blend or bowl branch added |
| Floor warm and uniform; humus under groves | Partly: humus darker and cooler (row 4 above). The litter tint is the floor accepted at M2: owner |
| fir_close shows the first thinned level, not E1's scan | Unchanged: the 4 m scan distance stays (frame budget); young LOD1 needles are now 1.31x (were 1.25x) after the 47,000 target |
| LOD pops (cross-fade off), no walk capture | Not changed: +0.5 to +0.9 ms measured at M4r1; owner's trade-off |
| Mist a uniform veil (fog base) | Not changed: preset change for the owner |
| Rock sets colour, white streaks; hero rock lip | Not changed |
| Clearing grass and rim | Not changed (grass count pinned at 220) |
| Deadfall trunks touching, log colour | Not changed |
| Trail rut breakup, branches across the tread | Not changed |
| Moss rectangles beside the trail | Not changed |
| Age mix (composition) | Partly: pole-stage cohort, tall scale 0.80 to 1.25. Logs and stumps not added |
| c's far trunk ribbon (low) | Not changed (far wood still 400 to 470 triangles, trunk only) |
| Budget re-statement, readable meshes, duplicate textures | Owner decisions; draw calls still estimated |

## 5. Deviations recorded here

| # | Spec | Now | Why |
|---|---|---|---|
| 1 | 5.6.4: twig cards are not in the spec; M3r1 added them with the whole sprays | Single-shoot sprites, radial cells, fill 0.85 | Row 2 |
| 2 | 5.6.4: tall LOD2 max scale 20 (M4a: 12), targets 8,000 / 5,000; LOD1 until 45 (M4r1: 35); cull 110 | Cap 6 (a: 8), 12,000 (a) / 9,000 (b); LOD1 cap 3.5, until 30 m; cull 85 m | Rows 1, 6, 10 |
| 3 | 5.6.4: wood of the thinned levels | Tall LOD1: trunk and crown branches; LOD2: trunk alone (per source sub-mesh), one material; root skirt on LOD0 only. Young far level (LOD3): no wood | Rows 7, 10 |
| 4 | 5.6.4: young LOD0 (first thinned) 60,000 (M3a: 52,000 / 61,000) | 47,000 / 55,000 | Row 10 |
| 5 | 5.6.4: fern patch until 16 / 40 (M2: 50) | 16 / 40 (the spec's value again) | Row 10 |
| 6 | 5.5: materials | Twig smoothness 0.15; humus tint; mud wetness 0.30; b's branch bark triplanar; b's root skirt in the ground material | Rows 4, medium findings |
| 7 | 3.5: the first view's pieces | Plus 5 young firs, 5 fern patches, 3 tall firs (`SpawnExtras`) | Row 11 |
| 8 | 7.2: tall firs (M4r1: groves 0.48-0.60, lone 16 m, cut 0.7; scale 0.90-1.15; bank 0.75-1.3 by hash) | 0.44-0.56, 13 m, cut 0.35; 0.80-1.25; bank 0.6-1.5 by clump noise and hash | Rows 3, 5, 12 |
| 9 | 7.2: young firs (M4r1: thicket 2.8 m, glades 26 m, understorey 30 %, scale up to 1.30) | 2.4 m, 32 m, 40 %, a pole-stage cohort of 1.40 to 1.55 (its LODGroup size held to scale 1.30), bank clump noise | Rows 3, 5, 9, 12 |
| 10 | 7.2: fern cap 900 (M4r1: 1,050), bank band 6 m; 45 branches (M4r1: 80) | 1,250, 14 m with bank drifts; 80 + 25 on the bank | Rows 5, 9 |
| 11 | 6.4: humus 0.6 within 1.2 m of a tall trunk; bank paint | 1.2; crown stamp 1.6; bank stony cut 65 %, humus 0.9, bank moss 0.8 | Rows 4, 5 |
| 12 | M4r1 sightline stations every 27 m, 3.6 m | Every 17 m, 4.5 m | Row 13 |
| 13 | `ForestWorldTests.ExpectedCounts` (rule 5: a pinned count changes with its tunable) | Tall firs 360 to 400 (groves 0.44-0.56 over thickets, lone 13 m: 400 placed), young firs 880 to 970 (thicket 2.4 m, glades 32 m, bank clumps, first-view additions: 976 placed), branches 100 to 125 (80 scattered + 25 on the bank + ring and set pieces: 127), fern patch cap 1,050 to 1,250 (the bank drifts) | Rows 3, 5, 9, 11, 12. The 15 % tolerance, the cap rule and every correctness test are unchanged |
