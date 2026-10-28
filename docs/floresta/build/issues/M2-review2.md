# M2, review round 2: findings, verdicts, changes, what is left

Written 2026-10-14 by the engineer who handled review round 2 of `captures/m2-r1`. Unity 6000.6.0f1, HDRP 17.6.0,
Metal, batch mode. Paths are relative to `build/` unless they start with `Assets/`. Final captures:
`captures/m2-r2/` (iterations under `captures/m2-r2/iterations/`, renders made to decide things under
`captures/m2-r2/look/`). Diffs: `out/M2r2/*.diff`. Originals: `backup/M2r2-pre-edit/` (the 27 sources, the manifest,
both test folders). No test file, audit rule, budget or threshold was changed (test folders compared with the backup:
identical).

## 1. Verdict on the nine critical and high findings

All nine are real; none was rejected. Two of them (the thin mist, photorealism and composition) are the same fault.

| # | Finding | Checked how | Verdict | State now |
|---|---|---|---|---|
| 1, 6 | Mist is a thin floor layer, the bank's crest stays sharp | HDRP source: `Fog.cs:207-212, 269-271` turns maximum minus base height into a scale height of 0.1448 times it; `VolumeRendering.hlsl:128-133` applies `exp(-(h - base) / H)` above the base. With 2 / 40 that is H = 5.5 m | Real | **Fixed.** Base 18, maximum 100 (H = 11.9 m), mean free path 70 m. The whole bowl is one even layer below Y 18; the bank fades into the mist in every outward view (`trail_start`, `pano_*`, `clearing`) |
| 2 | Rocks, stumps, branches are charcoal shapes | Looked (`trunk_close`, `trail_start`); measured the same boxes in both rounds | Real | **Mostly fixed.** Spawn boulder (a set piece, same place both rounds), median sRGB luma: face 34 to 43, base 26 to 36, ground beside it 68 to 61, so face/ground 0.50 to 0.70. The face is still just under the reviewer's 45 |
| 3 | Ground texture repeats at eye level and from above | Manifest: litter, gravel and dry needles all at 2.0 m, mossy ground and painted rock both at 3.0 m | Real | **Reduced.** Tiles now 1.73 / 2.11 / 3.0 / 2.0 / 3.15 / 2.35 / 3.37 / 2.43 m (pairwise different, no simple multiples). Clearing and bank mixed. The lattice in `clearing` and the stripes up the bank in `boundary_s` are gone (`work/M2r2/tiling-compare.png`, round 1 left, round 2 right). The anti-tiling graph is still the owner's decision |
| 4 | Boulders set on the ground, not in it | Looked (`rock_close`, `trail_start`, inspection) | Real | **Fixed.** Dark humus skirt against every rock taller than 0.8 m, a fainter moss ring outside it, fern clumps against the foot of 44 large rocks, and soil collars (row 5) |
| 5 | Dark faceted band at the hero boulder's base | Measured every rock's recess from the FBX: `rock11` recedes below a lip 0.17 to 0.55 m above its pivot plane on most bearings (`out/M2r2/measurements/rock_lip.txt`); rendered the rock from 14 bearings (`captures/m2-r2/look/anchor`, `anchor2`) | Real | **Fixed.** The band in `rock_close_crop` is now a thin contact line |
| 7 | Clearing is a bald needle bed with planted sprigs | Nearest neighbour of grass patches 0.53 / 0.56 / 0.62 m (p10 / p50 / p75) in round 1 | Real | **Improved.** Spacing 0.31 / 0.54 / 0.72 / 0.86 m (p10 to p90): swards with dense cores, no lattice. View turned toward the south-west stump patch. Still thin from standing height at the class count the test holds |
| 8 | Fern drifts do not mass outside the hollow | Round 1 plan: nearest neighbour p10 1.31, p50 1.62 m, Clark-Evans 0.85, one size | Real | **Improved.** p10 0.97, p50 1.39 m, Clark-Evans 0.74; scale p10 / p50 / p90 0.84 / 1.23 / 1.48; near level to 24 m. Drifts read as groups; at the cap of 900 they do not become carpets outside the hollow |
| 9 | Boulder clusters are rows of similar stones | Round 1 plan: neighbours 1.8 to 2.3 m apart, scale ratio at most 1.42, axis ratios 2.3 to 4.0 | Real | **Fixed.** Anchor, two or three rocks touching it, two downhill satellites; axis ratios 1.22 to 1.53 (logged), scale ratio up to 2.3 (`captures/m2-r2/look/clusters8`) |

## 2. What was changed

| File | Change | Deviation from the spec |
|---|---|---|
| `ForestLightingPresets.cs` | Fog base 2 to 18, maximum 40 to 100, mean free path 60 (round 1) to 70. White balance 0 to -5. Exposure compensation tried at -0.15 and put back to -0.3: on open ground the meter asks for 11.0 EV and the clamp holds 10.5, so it moved nothing (0.374 before, 0.372 after) | 8.2 (fog, white balance) |
| `ForestLighting.cs` | Micro-shadowing opacity 1 to 0.5; fog tint (0.93, 0.97, 1.0) | 8.3 |
| `ForestMaterials.cs` | `_AORemapMin` 0.4 on both rock sets, 0.5 on stumps, log, branches. Rock set 01 tint (0.80, 0.92, 1.0) to (0.92, 0.96, 1.0). Dry branches colour 1.25. Mossy ground tint (0.74, 0.84, 0.62) to (0.38, 0.64, 0.66): the reviewer's (0.42, 0.72, 0.40) rendered as fresh salad leaves (`captures/m2-r2/iterations/it01full`). Gravel tint 0.72 to 0.64. Minimum smoothness: mud 0.30 to 0.55, gravel 0.10 to 0.25, humus 0.18 to 0.26 | 5.5, 6.3 |
| `forest-asset-manifest.json` | `tileSizeMeters`: litter 2.0 to 1.73, dry needles 2.0 to 2.43, painted rock 3.0 to 3.37; the litter note rewritten | 6.3 |
| `ForestKitSpec.cs` | Rock scan level to 20 m (was 12; risk 12's fallback). Fern patch near level 14 to 24 m. Grass cull 25 to 35 m | 5.6.4 |
| `ForestLayout.cs` | Cluster constants (anchors, scales, touch factor 0.88, axis ratio 1.8, satellites 0.55 to 0.75). Fern spacing floor 0.55 with a per-sample jitter of 0.6 to 1.4, thinning floor 0.8 to 0.95, narrowing up to 0.9, patch scale by density (0.75 to 1.0 at the edge, 1.25 to 1.65 in the core). Grass spacing 0.32 (sward core) to 0.9 m (rim) with 25 % jitter, scale 0.6 to 1.6, threshold raised step by step to meet the cap. Branch settle 0.3. Rim sink cap 0.12 to 0.04 times scale. The measured `RockLips` table (16 bearings per rock) with its helpers. Soil collar constants. Ring clumps on large rocks; 120 fern clumps on the foot of the bank, 30 on the clearing's rim. `rock_close` 2.4 to 2.8 m from the pivot. `clearing` view turned 12 degrees left of the south-west stump patch | 3.4, 7.2, 7.3, 12.2 |
| `ForestScatter.cs` | Clusters rebuilt around an anchor, spaced by measured rims, axis ratio checked. Fern spacing jitter and size by density. Grass radius from the sward noise. Branch settle. Ring rule for large rocks (`RingRocks`), loose clumps on the bank and the clearing's rim (`ScatterLooseClumps`). No fern within 2 m in front of a crouched viewpoint (a frond had filled `rock_close`) | 7.2, 7.3 |
| `ForestTerrain.cs` | Mounds 0.35 m at 9 m to 0.22 m at 9 m plus 0.10 m at 4.3 m, under a 50 m mask (0.25 to 1). Clearing: dry needles 2.0 to 1.0 through the breakup noise, litter 0.4 to 0.65, humus 0.45 as a second surface. Bank: stony soil through the breakup, litter 0.35 to 0.5, humus 0.45. Moss patches 0.45-0.65 to 0.50-0.70, gain 2.0 to 1.5. Rocks: humus skirt (0.8, full to 0.5 times scale beyond the measured rim, gone by 1.0) on rocks taller than 0.8 m; moss ring gain 1.3 to 0.6, starting outside the skirt. Soil collars at step 8 (below). A log line with the paint's dominance in the clearing and on the bank | 6.2, 6.4 |

### The soil collars (the largest deviation: spec 6.2 step 8 and 7.3)

At step 8, after the root mounds, the ground is raised against every rock whose measured lip (capped at 0.35 m
times scale above the pivot plane) stands above the ground beside it, to 3 cm over the lip at the rim. The soil
falls off outward with a raised cosine no steeper than 23 degrees minus the ground's own slope along the ramp (at
most 2.5 m wide). Where a lip needs more than the ramp under the rock can give, the ground under the rock is raised
as a plateau (at most 0.25 times scale) and the rock's `sink` grows by exactly the lift at its pivot, so no rock
moves. The plateau lies under the rock and cannot be seen. A collar fades out over other rocks' footprints, so a
cluster does not bury itself. A collar that would push a core sample above 24 degrees, or above its own earlier
slope, is lowered in steps (1, 0.85, 0.7, 0.55, 0.4, 0.25) until it fits. Final build: 67 collars, 15 lowered, 0
dropped, highest 0.52 m at a rim, plateau up to 0.43 m. The hero rock's collar stands at 0.70 of full height with
0.43 m of soil on the camera side. Audit after it: grounding error 7.1 mm, core slope 25.8, trail slope 4.9 degrees,
corridors free.

**Cost, measured** (top of a rock above the ground at its pivot, against the spec's own sink): cluster rocks of set
02 lost 0.1 to 0.35 m (`rock10` at 1.22: 0.65 m against 0.86). Some of that is the plateau, which is hidden; part
of it is real height lost on the side where the soil comes up. Clusters are lower crouch cover than spec 7.4
assumes (0.6 to 1.3 m). The hero rock's peak stands about 1.7 m above its pivot ground, as in round 1. Spec 3.4
asks for 1.8 to 2.0 m and that was already not met.

## 3. Measured

| Question | Round 1 (`m2-r1`) | Now (`m2-r2`) | Source |
|---|---|---|---|
| Spawn boulder face / base / ground, median sRGB luma | 34 / 26 / 68 | 43 / 36 / 61 | `tools/regionstats` on `trail_start.png`, same boxes per round |
| Mean luminance `trail_start` / `clearing` / `ground_close` | 0.405 / 0.405 / 0.231 | 0.372 / 0.374 / 0.227 | capture sidecars (gate 0.45) |
| Fern patches: nearest neighbour p10 / p50, Clark-Evans | 1.31 / 1.62 m, 0.85 | 0.97 / 1.39 m, 0.74 | `out/M2r2/measurements/plan-stats.txt` |
| Grass in the clearing: nearest neighbour p10 / p50 / p75 | 0.53 / 0.56 / 0.62 m | 0.31 / 0.54 / 0.72 m | same |
| Cluster axis ratio | 2.3 to 4.0 (reviewer) | 1.22 to 1.53 | build log `M2r2-52-final-build.log` |
| One layer above 60 % of the weight | not measured | 27.5 % of the clearing, 17.1 % of the bank | build log |
| Triangles / shadow casters at `trail_start` | 2.73 M / 183 | 3.82 M / 237 | `captures/m2-r2/playmode/budget-counts.txt` |
| Frame time (stopwatch) median / p95 | 7.73 / 8.36 ms | 8.07 / 8.67 ms | `captures/m2-r2/playmode/frame-time.txt` |
| Class counts | 88 rocks, 877 fern patches, 94 clumps, 241 grass | 95, 900, 325, 237 | build log; all inside the test's 15 % |

## 4. Not done, or not taken as suggested

1. **Rock set 01's AO map.** Poly Haven's `arm` red channel is the AO map; M2 measured that it does not fit this
   FBX's UVs (7 to 33 % of each rock's surface at zero). A separate download of the same bake is expected to misfit
   the same way, and fetching it needs a new manifest entry read from `/files`. Not attempted. The two sets now meet
   half way through set 02's AO remap and set 01's lighter tint.
2. **Shadow lift in the grading** (the reviewer's fallback if faces stay under 45): faces measure 43. Not added: it
   would be a new volume component outside spec 8.3 and would lift every shadow in the frame. Exposure compensation
   cannot do it while the clamp holds open ground at 10.5 EV.
3. **SSAO radius 1.5 m:** not tried. The collars and skirts address the contact directly.
4. **`GrassPatchMaxTriangles` 2,500 to 4,000:** not taken. The patch stays at spec 5.6.4's 2,500, because 3.82 M of 8 M
   triangles are spent before the first tree. Grass coverage stays under the 25 % of 7.2, because the class count
   that `Scatter_IsDeterministic` holds (about 220) is a spec figure the owner would have to change.
5. **Branches on the bank and in the clearing** (risk 13): fern clumps only. The branch count must stay within 56 to
   74 (67 now).
6. **Owner decisions, unchanged:** the anti-tiling terrain graph; detail maps on rock (the hero boulder is still soft
   at 1 m); the grass count and fern cap.
7. **Medium and low findings not taken up:**
   - A/B captures for the ground streaks and the mist curtains. Faint streaks still fan out in `boundary_s`.
   - Sky rotation.
   - Moving the hero to `rock12` or scale 1.4.
   - Rut width modulation and carving.
   - `SpawnYaw` (a public constant of the frozen contract, 0 in spec 3.5).
   - The outcrop arc and apron.
   - Trail-side rocks along the whole trail.
   - The boulder field's empty middle.
   - Bank height by bearing, and a wider bank fern band.
   - The stump ring as an arc.
   - Deadfall spacing and position.
   - Log pitch.
   - The fern fade into the hollow's mud.

## 5. Side effects to know

- **`overview_oblique` and `overview_ortho` now look down through the mist.** From 40 m and 250 m up the layout is
  harder to read than in round 1. That is the uniform layer of section 1 seen from above, not a capture fault.
- **The frame is a little darker** (`trail_start` 0.405 to 0.372) and greyer, with more mist in the upper half.
- **Rocks show less height above their pivot ground** (section 2, cost).
- The final build's play-mode test logs contain the word "Exception" 17 and 9 times. These are stack-trace frames
  of ordinary log lines (`UnityLogCheckDelegatingCommand:CaptureException`); round 1 had 7 and 9. No exception was
  thrown.

## 6. Temporary files that were in the project during single runs

`build/tools/inspect/ForestLookTemp.cs` (anchor bearings, rock04, clusters) and `ForestInspectTemp.cs` (final
inspection) were copied into `Assets/Game/Editor/Forest/` for one run each and removed with their `.meta` files.
After the last run the folder holds its 27 sources and the manifest (`ls | grep -c Temp` gives 0).
