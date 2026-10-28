# M3, review round 1 of the young-fir captures (`captures/m3-r0` to `captures/m3-r1`)

Written 2026-10-14. Paths are relative to `build/` unless they start with `Assets/`. Originals of every changed file:
`backup/M3r1-pre-edit/`; diffs: `out/M3r1/*.diff`. Iterations: `captures/m3-r1/iterations/<tag>` (k = kit, p = scene),
logs `logs/M3r1-*`. Final transcript: `logs/M3r1-final-sequence.txt` (run 2; run 1, before the fern fix of section 5, is
`logs/M3r1-final-sequence-run1.txt`).

## 1. Verdicts on the critical and high findings

No finding was critical. All nine high findings were judged real after looking at the images (`m3-r0/trail_start.png`,
`pano_270.png`, `crouch_hide.png`, `boundary_*`, `young-fir-map.png`, the kit sheets `fir_sapling_medium_b__lods_14m/42m.png`,
and `measure/playmode/m3b-measure-final.txt`). They are three problems, each reported by two or three lenses:

| # | Problem (findings) | Real? | In scope? | Done |
|---|---|---|---|---|
| A | From 14 m young firs read as bamboo or willow; the 14 m hand-over is a change of species (photorealism 1, technical 5, composition 7) | Yes: kit sheet panels 3 and 4, trail_start right half | Yes (risk 1, `ForestKitSpec`, thinner) | Fixed, but **not with the fallbacks the reviewers named**: (b) and (c) were built and failed (section 2). The 14 m and 85 m levels are now twig cards built from the scan's own atlas |
| B | Orchard: even singles of one height on open ground; thickets a lattice; bank wall rows; 22 % thicket share (photorealism 2, technical and composition mediums) | Yes: young-fir map, pano_270, clearing | Yes (`ForestLayout`, `ScatterYoungFirs`) | Fixed: clumps and glades, denser jittered thickets, gapped wall, scale 0.55 to 1.30 with a size gradient and understorey, lean, 29.3 % share, 795 firs |
| C | Thickets give no cover at crouch height; the bank band is see-through (photorealism 3, composition 8) | Yes: crouch_hide looks under the five spawn firs to the trail | Partly. Sight-proxy colliders and AI cover are gameplay phase; the 1 m seedlings are M6 | Improved, not solved: 30 % understorey (0.55 to 0.68) in every dense stand and two understorey firs in the spawn thicket; the scan itself is an open-grown sapling. The fern term under thicket edges was tried and taken out (section 5) |
| D | Frame budget: deadfall over 11 ms and 13.45 M triangles, pano_90 10.84 ms, little headroom (photorealism 4, technical 6, composition 9) | Yes | Yes (knob 5, variant mix, scan distance of `a`) | Fixed: every one of 29 views is under 11 ms by stopwatch (section 4), with the young firs denser than before |

One part of D was **not done**: adding `deadfall` and `pano_90` to `Budget_Counts`. It would strengthen the test, but rule 5
allows only one kind of test edit (a pinned tunable retuned on evidence), and spec 12.3 names the four views. Both views are
in the 29-view measurement instead. Left to the owner.

## 2. Problem A: the far levels

### What the reviewers asked for, built and judged (`iterations/k1`)

`ThinByGroup = true` (0.25 m cells), scale caps 2.0 (14 to 42 m) and 3.0 (42 to 85 m), `CrownCoreFromLod = 1` (the 14 m
level). Coverage passed (b: 0.98 / 1.19 / 1.00). The sheet `iterations/k1/kit/fir_sapling_medium_b__lods_14m.png` shows why it
fails: the core of this open sapling is an opaque olive spindle, wider than the branches and far darker, with a few tufts of
needles stuck on it. It reads as a cypress or a rock, not a fir; at 6 m (`lods_06m`) it fills the frame. Grouping alone left
scattered clumps of single needles. Both switches are back at the spec's defaults (`ThinByGroup` false, `CrownCoreFromLod` 99).

### Why single needles cannot work, and what was done instead

Every piece of `fir_sapling_medium` is one needle card (43,154 of the `b`'s 43,812 pieces). A level of 8,000 triangles holds at
most 4,000 of them: enlarged, they are bamboo blades; at life size, a haze. But the twig atlas holds **seven photographed twig
sprays** that the scan's mesh never uses: its needle cards map only to four single needles at the bottom left
(`work/M3r1/twig_alpha.png`, `twig_diffuse.png`). Measured on the alpha map (`work/M3r1/scripts/sprites.py`), the boxes are
in `ForestKitSpec.SaplingTwigSprites`.

`ForestFoliageThinner.TwigCards` (new, internal; used from the 14 m level on, `ForestKitSpec.TwigCardsAt`):

1. needles (not stems) are grouped into cubic cells, as large as it takes for the number of occupied cells to fit the
   triangle target (two triangles per card): b 0.117 m at 14 to 42 m, 0.339 m at 42 to 85 m;
2. each cell becomes one quad in the plane its needles spread in (principal axes of their corners), its long axis pointing
   away from the trunk (up for the leader), as long and wide as the needles reach, clamped to the crown's box;
3. it is mapped to the sprite whose aspect fits best (with a hash, so neighbours vary; half are mirrored);
4. every cell is kept, so the crown has no holes; stems are left to the clustered wood.

Same triangle budget (b: 9,253 and 1,377), coverage 1.11 and 1.09 (gate 0.80 / 0.70 to 1.30, unchanged). The level that the
reviewers saw as a spray of blades is now the same silhouette as the needle level, made of twig sprays
(`iterations/k2` to `k6`, final `captures/m3-r1/kit/fir_sapling_medium_*__lods_14m.png`, `_42m`, `_06m`).

Two follow-ups, both measured:

- **Cards read lighter than the needles** (`iterations/k2`, `k3`). Not the texture: the sprays are darker texels than the
  needles (linear luminance 0.062 to 0.094 against 0.106, `work/M3r1/scripts/sprite_colour.py`). Not the normals either
  (radial normals tilted up `k4` or down `k5`: no visible change). It is the AO channel: the needle region averages AO 0.43,
  the sprays 0.78, and HDRP multiplies the sky light (four-fifths of the light) by it. `ForestTexturePacker.MatchSpriteAo`
  scales the AO inside the sprite boxes by 0.56 so their mean equals the needles'; nothing the scan maps changes
  (`logs/M3r1-final-50-import.log` in run 1 and `M3r1-p7-import.log`: "AO scaled by 0.56 (needles 0.43, sprites 0.78)").
  After: `iterations/k6/kit/fir_sapling_medium_b__lods_14m.png`, levels 1 and 2 in the same tone. Card shading normals point
  away from the trunk, horizontal (`TwigCardNormalLift` 0).
- **Sticks**: stems (closed tubes) no longer take the needles' enlargement (`StickMaxScale` 1.2), the area balance counts needle
  cards only, and a stick is clamped to the box of the stem it replaces (the technical and photorealism lows about wires and
  sticks running out of the crown). This touches the 6 to 14 m needle level, whose scale moved from 1.20 to 1.25 (b).

`ToolVersion` "kit-7". The coverage gate is unchanged; `TreeLods_WithinBudget` and `TreeLods_KeepCoverage` pass.

### Not fixed in A

- **The 85 m level's trunk** is still a twisted ribbon with a dark flag (`kit/*__lods_14m.png`, panel 4). The wood of that
  level is capped at 16 % of its 1,200-triangle foliage target, about 190 triangles, so a finer cluster cell (8 cm tried,
  `iterations/k7`) gives the same mesh. It shows only from 42 m, a few pixels in mist. A proper fix (a fixed tube along the
  trunk, or a larger wood share) is left for M4, where the tall firs need the same decision.
- The 85 m cards come from 0.34 to 0.47 m cells and read slightly leafy at the sheet's 6 m; from 42 m on, in the scene, they
  read as a small fir.

## 3. Problems B and C: placement (`ForestLayout`, `ForestScatter.ScatterYoungFirs`)

| Spec 7.2 / M3b value | Now | Why |
|---|---|---|
| Open ground: Poisson at 8.5 m, jitter 0.35 | A clump noise (15 m wavelength, threshold 0.69 to 0.78): clumps of firs 3.0 m apart where it is high, glades at 26 m spacing elsewhere | The even 8.5 m disc read as planted (orchard). Same idea as the reviewers' parents and children, done with the planner's own variable-radius Poisson |
| Thickets 3.0 m, jitter 0.08, ramp T 0.45 to 0.85 | 2.8 m, jitter 0.25, ramp 0.55 to 0.92 | Denser and less regular, on slightly smaller areas so the count holds |
| Bank wall 3.0 m to 7 m out, jitter 0.12 | 2.5 m, jitter 0.25, opened by a gap noise (13 m) into a few 6 to 10 m gaps | Rows of single trunks |
| Scale 0.85 to 1.20 | 0.55 to 1.30: edges 0.65 to 0.95, deep inside 0.95 to 1.30; 30 % of every dense stand is understorey at 0.55 to 0.68, variant b or c only (c 70 %); on the bank small in front, larger behind | Flat crown line; nothing between the ferns and the lowest whorl |
| Upright | Scattered firs lean up to 3 degrees (hand-placed ones do not: they would move the boulder field's random stream) | Planted look |
| Mix a 30 : b 35 : c 35 | a 15 : b 42.5 : c 42.5 | `a`'s scan costs 1.6 times `b`'s or `c`'s |
| Spawn thicket: five firs (spec 3.5) | Plus two understorey `c` firs (0.58 to 0.64) in the gaps of its first row, about 2 m from the crouch_hide camera, placed from the young firs' own stream | crouch_hide looked under the five firs to the trail |
| Viewpoints keep trunks 0.8 m away | Scattered young firs also keep 1.8 m from first-person capture cameras | A crown filled the lens of boundary_n (low finding). It removed no fir in this plan |

Iterations (built and mapped, `iterations/p1` to `p4`): p1 979 firs, 42.3 % share (over the count test's 885); p2 864,
36.9 %; p3 860, 35.6 %; p4 782, 30.8 %. Final: **795 young firs (393 core, 402 bank), 29.3 % thicket share** (spec "roughly 30
%"), core nearest neighbour p50 3.61 m, bank 3.28 m; `Scatter_IsDeterministic` keeps its expectation of 770 within 15 %.
Map: `captures/m3-r1/young-fir-map.png`.

The floor under the firs (photorealism and composition mediums, deviation from spec 6.4, `ForestTerrain`): every young fir
stamps humus (1.1, full to 0.6 and gone by 1.9 times its scale) and a crown mask (1.0 to 2.2 times its scale) that cuts the
moss patches by 90 % and the pale dry needles entirely under the crown.

## 4. Problem D: cost

In the spec's order of section 10, after knobs 1 and 2 (M3b):

| Change | Where | Measured effect |
|---|---|---|
| Knob 5: fern patch near level 24 to 16 m (the spec's value; M2 review 2 had moved it out) | `ForestKitSpec.FernPatchDistances` | deadfall 14.78 to 13.75 M, 11.26 to 10.93 ms (`iterations/p4`, `p5`) |
| Variant `a` 30 to 15 %; understorey never `a` | `ForestLayout.YoungFirMix`, `YoungFirUnderstoreyMix` | deadfall 13.75 to 12.84 M (`p6`) |
| Scan level of `a` 6 to 4.5 m (spec floor 4 m; fir_close looks at a `b`, which keeps 6 m) | `ForestKitSpec` tree table | part of the above |
| The scan level's shadow renderer draws the first card level, not the needle level | `ForestKitSpec.ScanShadowThinnedLevel`, baker | crouch_hide 11.30 to 10.25 M; 0.05 to 0.2 ms (`p7/measure-knobs`) |
| Knob 3 (needle level 14 to 10 m) | measured only | 0.07 to 0.35 ms, within the 0.3 ms run-to-run noise; not applied |

The final measurement (section 6) is three interleaved passes of all 29 views.

## 5. Tried and taken back

- **Crown core and grouped thinning** (section 2).
- **Ferns under thicket edges** (+0.6 per m², then +0.2, in the thicket field's edge band). The fern patches are capped at 900
  (spec 7.2), so the edge band, which covers much of the core, took its ferns from the open-ground drifts: the solver thinned
  the field to 0.50 and raised the drift threshold to its maximum (0.90), and the drifts loosened from a median neighbour of
  1.41 m to 1.96 m (visible in `work/M3r1/ferns-compare.png`, left round 0, right run 1). At 0.2 the threshold still hit its
  maximum. Removed; the drifts are back at 0.95 / 0.32 and 1.43 m. Final run 1 had the 0.6 term; run 2 does not.
- **8 cm wood cell at 85 m** (section 2).

## 6. Final results (run 2, `tools/m3r1/final.sh`, `logs/M3r1-final-sequence.txt`)

| Run | Result | Evidence |
|---|---|---|
| Compile outside Unity (editor, both test assemblies) | OK, OK, OK | transcript |
| Import | Exit 0, all up to date (the mask and materials were rebuilt in the iterations) | `logs/M3r1-final-50-import.log` |
| Kit, forced | 31 prefabs built, 3 skipped (tall firs), 0 failed | `logs/M3r1-final-51-kit.log`, `out/M3r1/kit-report.json` |
| Scene | Audit passed: 2,438 instances (795 young firs), 9,512 GameObjects, 13 materials, 79 meshes, 0 errors, 0 warnings | `logs/M3r1-final-52-build.log`, `out/M3r1/build-report.json` |
| Kit sheets | 80 sheets of 31 prefabs, `passed: true` | `captures/m3-r1/kit/` |
| Full set | 32 views and crops, `passed: true`, 0 magenta, 0 cyan, nothing clipped or crushed, mean luminance 0.209 (crouch_hide) to 0.678 (canopy_up), trail_start 0.314; RMSE against m3-r0 recorded on all 32 (0.015 to 0.191) | `captures/m3-r1/capture-summary.json` |
| Debug set | 18 images | `captures/m3-r1/debugset/debug/` |
| Cost, 29 views x 3 passes | Section below | `captures/m3-r1/measure/playmode/m3b-measure-r1-final.txt` |
| Forest EditMode | **35 passed, 0 failed, 0 ignored** | `out/M3r1/tests/forest-editmode.xml` |
| Forest PlayMode | **10 passed, 0 failed** | `out/M3r1/tests/forest-playmode.xml`, `captures/m3-r1/playmode/` |
| Greybox PlayMode | **7 passed** | `out/M3r1/tests/greybox-playmode.xml` |

Every final log: 0 `error CS`, 0 `warning CS`, 0 "immutable". PackageCache `DefaultHDTerrainMaterial.mat` md5
`8ce2f7482ff8fb845c1b94b0082b4cf0`. 59 pre-existing files: 0 differ. Both test folders equal `backup/M3r1-pre-edit/Tests` (no
test was changed). Temporary measurement test removed (0 left). No git command that changes anything was run.

Kit (`out/M3r1/kit-report.json`): `a` 71,300 / 9,246 / 1,386 triangles behind the 685,046 scan (needle scale 1.46; cards from
0.163 and 0.471 m cells; coverage 1.09 / 1.11 / 1.14); `b` 57,520 / 9,253 / 1,377 (1.25; 0.117, 0.339 m; 1.05 / 1.11 /
1.09); `c` 57,590 / 9,277 / 1,389 (1.26; 0.117, 0.332 m; 1.05 / 1.10 / 1.08).

`Budget_Counts` (play mode, worst of its four views): triangles 10.63 M at crouch_hide (budget 11.6 M; round 0 9.53 M), vertices
13.36 M (19.0 M), SetPass 45, shadow casters 479, draw calls 2,118 (estimated, the counter reads 0), texture memory 802.2 MiB,
mesh memory 273.3 MiB. `Budget_FrameTime`: trail_start 10.01 ms median, p95 12.06 (stopwatch; FrameTiming again 8 of 240
frames at 28.9 ms).

29 views, median of three passes' medians (stopwatch, ms) and triangles, against round 0:

| View | Triangles M (r0) | ms (r0) | | View | Triangles M (r0) | ms (r0) |
|---|---|---|---|---|---|---|
| deadfall | 11.21 (13.45) | 10.87 (11.14) | | trail_strip_08 | 4.33 (5.74) | 8.98 (9.41) |
| pano_90 | 7.38 (8.99) | 10.36 (10.84) | | trail_strip_02 | 4.39 (4.36) | 8.91 (9.10) |
| crouch_hide | 10.63 (9.53) | 10.29 (10.08) | | trail_strip_05 | 3.94 (5.51) | 8.86 (9.48) |
| trail_strip_01 | 8.42 (7.45) | 10.16 (9.87) | | trail_strip_06 | 3.88 (5.90) | 8.73 (9.22) |
| hollow_rim | 8.13 (7.12) | 10.11 (9.73) | | ground_close | 3.99 (4.07) | 8.71 (8.89) |
| trunk_close | 7.97 (6.24) | 10.03 (9.57) | | trail_mid_fwd | 4.88 (6.06) | 8.63 (9.34) |
| boundary_e | 6.28 (8.53) | 9.67 (9.51) | | trail_strip_04 | 3.32 (6.32) | 8.48 (9.72) |
| fir_close | 6.26 (6.28) | 9.65 (9.48) | | pano_0 | 4.59 (5.88) | 8.43 (9.54) |
| trail_strip_07 | 5.33 (5.88) | 9.33 (9.35) | | trail_mid_back | 3.36 (4.88) | 8.27 (8.82) |
| rock_close | 6.58 (6.67) | 9.24 (9.39) | | pano_270 | 2.59 (4.67) | 8.25 (9.16) |
| long_sightline | 4.98 (4.14) | 9.17 (8.90) | | boundary_n | 3.67 (7.58) | 8.22 (9.32) |
| pano_180 | 6.07 (6.37) | 9.14 (9.31) | | boundary_w | 3.21 (6.66) | 8.21 (9.61) |
| trail_start | 5.20 (5.72) | 9.10 (9.24) | | trail_strip_03 | 2.49 (5.51) | 8.11 (9.17) |
| boundary_s | 5.30 (7.43) | 9.09 (9.33) | | clearing | 2.12 (3.01) | 8.02 (8.47) |
| | | | | backlit | 2.27 (5.06) | 7.75 (8.78) |

0 of 29 views over 11 ms (round 0: 1); 8 over the 9.5 ms warning line (round 0: 10); 0 over the re-based 11.6 M triangles (1);
4 over the spec's 8.0 M (4); 5 over the spec's 10 M vertices (7). The three passes of a view agree within 0.6 ms (pano_270 8.13 to 8.70, trunk_close 9.94 to 10.41).

**Headroom for M4 is still small**: 0.13 ms at deadfall, 0.6 to 0.9 ms at pano_90, crouch_hide, trail_strip_01, hollow_rim and
trunk_close, by a stopwatch that reads 0.3 to 1 ms high from run to run (trail_start 9.10 here, 10.01 in `Budget_FrameTime`
minutes later). Views beside dense stands (trail_strip_01, hollow_rim, trunk_close) became dearer: more firs, many of them
small, stand within their scan distance. Knob 3 (about 0.1 to 0.35 ms) and knob 4 for the tall firs are the next levers.

## 7. Medium and low findings

Done: floor under young firs (humus, moss and needle cut); sticks at life size and inside their piece; twig smoothness
(`_SmoothnessRemapMax` 0.4 on `fir_sapling_medium_twigs`; the needle region's smoothness averages 0.48); twig tint variant
0.88 grey to (0.82, 0.79, 0.64), darker and olive (`ApplyTint` does reach the young firs: 407 of 795 carry tint 1); bank wall
jitter and gaps; size hierarchy; variant `c` biased to the understorey; `rmseToPreviousRun` (`ForestCapture -captureBaseline
DIR`, the final set is compared with `captures/m3-r0`); crowns kept off capture cameras.

Not done, with the reason:

| Finding | Reason |
|---|---|
| Probe bake with sky occlusion (light under the crowns) | M5, out of scope. Recommended to the owner as the real fix for the flat, X-ray-like light inside thickets |
| Ferns under thicket edges, fern patches on the bank | The 900 cap: section 5 |
| Dry branches in thicket interiors and on the bank foot | Not cheap: a new placement rule and more instances; left for M4 with the tall firs' branch rule |
| Deadfall ferns halved on the trail side; north outcrop two tiers, no soil collar | Landmark composition, not the trees; not cheap to judge; left |
| The 85 m trunk ribbon | Section 2 |
| Each frame-time configuration measured three times interleaved, median of medians | Done for the final all-views run (three passes); the knob A/Bs were single runs |
| FrameTiming's 8 of 240 frames at 30 to 35 ms against the stopwatch | Still unexplained, as since M1 |
| A real GPU number from a development build | M6 |
| Adding deadfall and pano_90 to `Budget_Counts` | Rule 5 (section 1) |
| Clearing rim ring broken into clumps | Partly by the clump noise; the thicket field's rim factor was not changed |

## 8. Deviations recorded here

| # | Spec | Now |
|---|---|---|
| 1 | 5.6.3: thinned levels are kept pieces (needles) | From the 14 m level, twig cards from the atlas's own sprites (section 2) |
| 2 | 5.4: the mask is the packed `arm` map | Inside the twig sprites' boxes (unused by the scan) the AO is scaled to the needles' mean |
| 3 | 5.6.4 E1: `LOD0_Shadow` draws the first thinned level | It draws the first card level |
| 4 | 5.6.4: young-fir scan distance 6 m | `a` 4.5 m (spec floor 4 m) |
| 5 | 5.6.4 table: fern patch near level (M2r2: 24 m) | 16 m, the spec's own value (knob 5) |
| 6 | 7.2: young-fir scale 0.85 to 1.20, mix 30 : 35 : 35, no lean | 0.55 to 1.30 with gradient and understorey, 15 : 42.5 : 42.5, lean to 3 degrees |
| 7 | 7.2: spacing `lerp(7, 3, T)` (M3b: 8.5 to 3.0 m) | Clumps and glades on open ground, thickets 2.8 m |
| 8 | 3.5: five spawn-thicket firs | Plus two understorey firs |
| 9 | 6.4: humus `0.9 * thicket`; dry needles `0.7 * (1 - thicket)` | Plus a humus stamp and a moss and dry-needle cut under every young-fir crown |
| 10 | 5.5: twig tint variant 0.88 | (0.82, 0.79, 0.64); twig smoothness remap maximum 0.4 |
