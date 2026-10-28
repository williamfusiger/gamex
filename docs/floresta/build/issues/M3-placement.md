# M3, second half: young-fir placement (thickets, the wall on the bank), budgets, review-2 captures

Written 2026-10-14. Paths are relative to `build/` unless they start with `Assets/`. Originals of every changed file:
`backup/M3b-pre-edit/`; diffs: `out/M3b/*.diff`. Final transcript: `logs/M3b-final-sequence.txt`.

## 1. What the M3a scene looked like (the starting point)

The scene of M3a held the existing planner's 771 young firs (`captures/m3a/final/treescene`). Measured on its plan
(`work/M3b/scripts/firs.py`, map `work/M3b/map-m3a.png`):

- Core 363, bank 408. Nearest neighbour in the core p10 3.49, p50 4.67, p90 7.92 m; on the bank p10 3.73, p50 4.51 m.
- Only 19.3 % of the core had three or more trunks within 5 m. From the ground and from above the young firs were an
  even scatter of single trees (an orchard), and the bank band one fir every 4.5 m from its first metre to its last.

Cause: section 7.2's linear `lerp(7, 3, T)` with a jitter of up to 38 % on top leaves most of the core in the middle
of the ramp (4 to 6 m), and the bank rule (thicket field floored at 0.8) gives 3.8 to 5.2 m everywhere on it.

## 2. Placement changes (`ForestLayout.cs`, `ForestScatter.ScatterYoungFirs`)

| Spec (7.2, 3.6) | Now | Why |
|---|---|---|
| Core spacing `lerp(7.0, 3.0, T)` | `lerp(8.5, 3.0, smoothstep(0.45, 0.85, T))`: 3.0 m inside a thicket (the spec's value), 8.5 m on open ground | A steep ramp: thickets are dense and open ground is open, instead of a uniform 4 to 6 m |
| (jitter 0.38 in the code, not in the spec) | Jitter 0.08 inside thickets, 0.35 on open ground | A thicket is dense by definition |
| Bank band 1 to 20 m at the thicket rule (`T >= 0.8`) | The wall: 3.0 m spacing on the foot of the bank (to 7 m outside the boundary), rising to 8 m at the band's outer edge, jitter 0.12 | "A 20 m band of dense young firs" (3.6) that the player sees from the core |
| About 360 core, 410 bank | 360 core, 411 bank (771; the test's expectation of 770 within 15 % is unchanged) | Count kept at the spec's |

Result (`out/M3b/placements.json`, map `captures/m3-r0/young-fir-map.png`): core nearest neighbour p10 3.17, p25 3.32,
p50 3.74, p90 7.49 m; bank p10 3.24, p50 3.71 m; 22.0 % of the core has three or more trunks within 5 m (was 19.3 %;
the spec asks for thickets on "roughly 30 %"). Iterations (all built, mapped and looked at):

| Run | Thicket / open / wall spacing | Young firs (core / bank) | Thicket share | Kept? |
|---|---|---|---|---|
| p1 | 2.7 / 9 / 2.9 to 9 m, outer 6 | 995 (466 / 529) | 31.7 % | No: 995 is over the 885 the count test allows |
| p2 | 2.7 / 10 / 3.0 to 7 m, outer 7 | 826 (386 / 440) | 25.5 % | Looked right; costlier than p4 |
| p3 | 2.7 / 8.5 / outer 8 | 849 (427 / 422) | 28.1 % | No: views facing a thicket from 6 to 14 m had 13 to 17 firs at the 60k level (deadfall 15.8 M triangles, 12.6 ms; rock_close 13.1 M) |
| p4 to final | 3.0 / 8.5 / outer 8 | 771 (360 / 411) | 22.0 % | Yes: the spec's 3 m, the density of the spawn thicket that crouch_hide stands in |

Grounding, corridors and the spawn are untouched: largest grounding error 8.6 mm (3 cm allowed), corridor margin
2.58 m, spawn margin 5.55 m, trail slope 4.9, core slope 25.8 degrees, physical corridors free
(`out/M3b/build-report.json`).

## 3. Where the triangles were (attribution, `tools/m3b/ForestM3bMeasureTemp.cs`)

A temporary PlayMode test (copied into `Assets/Game/Tests/ForestPlayMode` for one run by `tools/m3b/run-measure.sh`
and removed again; 0 left behind) measures, per view and configuration, the worst `Triangles Count`, `Vertices Count`,
`Shadow Casters Count` and `SetPass Calls Count` of 8 frames after 24 warm-up frames (the method of `Budget_Counts`),
the stopwatch frame time of 90 renders with a one-pixel readback (the method `Budget_FrameTime` asserts on), and a
census of the renderers the frame touched. Configurations switch renderers or camera settings off at run time only.

On the M3a scene at crouch_hide (`captures/m3-r0/work/base0/playmode/m3b-measure-base0.txt`): 12.48 M in all; floor
without young firs 2.45 M; the five spawn-thicket scans 5.97 M (2 x 2.38 M in the depth prepass and G-buffer, plus
their `LOD0_Shadow` in four cascades); the other 766 firs 4.07 M; **young-fir shadows 3.87 M (31 %)**; the bank firs
0.98 M (mostly shadows). Arm C's scan level is the single largest item wherever three or more young firs stand within
about 5 m of the camera.

## 4. Knobs 1 and 2 of spec section 10 (applied, in the spec's order)

With thickets the frame missed 11 ms at standard views (no knobs, same scene p4: hollow_rim 11.19 ms, deadfall 12.45,
crouch_hide 11.43; p3: trail_strip_01 11.08, rock_close 11.47) and crouch_hide had no room left, so the knobs were
turned in order, each measured as an A/B on the same scene:

| Knob | Where | Effect, same scene (final run, `captures/m3-r0/measure/playmode/m3b-measure-final.txt`) |
|---|---|---|
| 1: decals off per camera | `ForestLighting.ConfigureCamera`: `customRenderingSettings = true`, override mask and frame setting `Decals` off; every other frame setting stays on the project default | 0.12 to 0.65 M triangles fewer per view (deadfall 14.10 to 13.45 M, trail_start 6.08 to 5.72 M); 0.07 to 0.66 ms in the same run |
| 2: shadows 80 to 50 m, three cascades | `ForestLightingPresets` (OvercastMist): 50 m, 3 cascades, splits 0.12 / 0.35 (6 and 17.5 m, where the old near splits were in metres) | Alone (knob 1 off in both): 0.49 to 2.48 M triangles fewer per view (crouch_hide 11.21 to 9.76 M, deadfall 16.58 to 14.10 M). Both knobs: 0.64 to 3.13 M fewer (crouch_hide 11.21 to 9.53 M, pano_90 11.16 to 8.99 M) |

Knob 3 (young-fir first thinned level 14 to 10 m) was measured at run time on p4 and not applied: 0 to 0.4 ms by view
(within the noise at several), and it moves the visibly coarser 8,000-triangle level to 10 m. Neither knob changes the picture: play-mode stills of
trail_start, crouch_hide, fir_close and long_sightline with knobs 1 and 2 off and on are indistinguishable
(`captures/m3-r0/knob-ab/ab-sheet.png`, `ab-sheet-fir_close.png`). The parity test compares `HDAdditionalCameraData` by
presence only, so the rig still matches the greybox (`PlayerRig_MatchesGreybox` passes). Neither knob is a project or
HDRP-asset setting: one is the forest camera's own override, the other the forest's own volume profile.

The timing deltas drift within one run (the GPU warms; the no-knob pass at the end of the final run reads up to 2 ms
slower than the same configuration earlier), so take triangle numbers as exact and millisecond deltas as approximate.

## 5. The tan planks in the near firs (fixed at the cause, `ForestFoliageThinner.TryFitStick`, kit-5)

In trail_start the spawn-thicket firs (6 to 13 m, the first thinned level) showed straight, flat, tan bars standing out
of their crowns (`work/M3b/crops/p5_trail_start_fir.png`; also in M3a's sheets, `captures/m3a/final/kit/*__lods_08m.png`,
second column). They are the M3a "sticks" that replace twig stems. Their width was twice the vertices' mean distance from
the straight axis; for a curved stem that distance is mostly the bow of the curve, so the sticks came out several times
wider than the stem, and as flat cards of the translucent twig material they were lit through from above.

Fix: the radius is also read from the tube's surface, `r = area / (2 pi length)`, which a bow does not change, and the
smaller of the two is used. `ForestKitSpec.ToolVersion` "kit-5". Same triangle and vertex counts per level; coverage
moves by at most 0.03 (a, far level: 0.758 to 0.747, gate 0.70; `out/M3b/kit-report.json`). Prop meshes were not
rewritten (their assets keep their M2/M3a dates). After: `work/M3b/crops/p6_trail_start_fir.png` (thin twigs, no bars),
`captures/m3-r0/kit/fir_sapling_medium_*__lods_08m.png`. `Thinner_FlattenReproducesPlanarCard` (cards) is unaffected.

## 6. Final results (`tools/m3b/final.sh`, `logs/M3b-final-sequence.txt`)

| Run | Result | Evidence |
|---|---|---|
| Import | Exit 0, all up to date | `logs/M3b-final-50-import.log` |
| Kit, forced | 31 prefabs built, 3 skipped (tall firs), 0 failed | `logs/M3b-final-51-kit.log`, `out/M3b/kit-report.json` |
| Scene | Audit passed: 2,383 instances (771 young firs), 9,285 GameObjects, 13 materials, 79 meshes, 0 errors, 0 warnings | `logs/M3b-final-52-build.log`, `out/M3b/build-report.json` |
| Kit sheets | 80 sheets of 31 prefabs, `passed: true` | `captures/m3-r0/kit/` |
| Full set | 32 views + 32 crops, `passed: true`, 0 magenta, 0 cyan, nothing clipped or crushed | `captures/m3-r0/capture-summary.json` |
| Debug set | 18 images | `captures/m3-r0/debugset/debug/` |
| Cost measurement | 29 views x 3 configurations | `captures/m3-r0/measure/playmode/m3b-measure-final.txt`, `logs/M3b-measure-final.log` |
| Forest EditMode | **35 passed, 0 failed, 0 ignored** (both tree-LOD tests run and pass) | `out/M3b/tests/forest-editmode.xml` |
| Forest PlayMode | **10 passed, 0 failed** (`Budget_Counts` included) | `out/M3b/tests/forest-playmode.xml`, `captures/m3-r0/playmode/` |
| Greybox PlayMode | **7 passed** | `out/M3b/tests/greybox-playmode.xml` |

Every final log: 0 `error CS`, 0 `warning CS`, 0 "immutable". PackageCache `DefaultHDTerrainMaterial.mat` md5
`8ce2f7482ff8fb845c1b94b0082b4cf0`. 59 pre-existing files: 0 differ. No test file was changed (both test folders equal
`backup/M3b-pre-edit/Tests`). No git command that changes anything was run.

### Budgets of section 10 (play mode, 2560 x 1440)

| Metric | Value, worst of the four `Budget_Counts` views | Budget (re-based at M3a) | Spec | How obtained |
|---|---|---|---|---|
| Triangles | 9.53 M (crouch_hide; was 12.48 M at M3a) | 11.6 M | 8.0 M | Measured (`ProfilerRecorder`) |
| Vertices | 12.27 M | 19.0 M | 10.0 M | Measured |
| SetPass calls | 45 | 250 | 250 | Measured |
| Shadow casters | 424 (was 910) | 2,500 | 2,500 | Measured |
| Texture memory | 802.2 MiB above an empty scene | 1,024 MiB | 1,024 | Measured |
| Mesh memory | 273.4 MiB | 400 MiB | 300 | Measured |
| Draw calls | 2,060 | 4,000 | 4,000 | **Estimated** from visible renderers (the engine counter reads 0); with knob 1 the prepass draws only alpha-tested objects, so the estimate errs high |
| Frame time, median | trail_start 9.24, crouch_hide 10.08, clearing 8.47, long_sightline 8.90 ms (measurement run); `Budget_FrameTime` 10.34 ms (p95 13.82) in the test run two minutes later | 11.0 ms | 11.0 | **Stopwatch with a one-pixel readback**, an upper bound (probe P5); `FrameTiming` reported 8 of 240 frames |
| Scene totals | 2,383 instances, 9,285 GameObjects, 13 materials, 79 meshes, 4 textures at 4096 | 3,500 / 14,000, 40 / 120, 8 | same | From the audit |

Beyond the four views (same measurement run, 29 views): every view is under the triangle budget and at or under 10.84 ms
**except deadfall: 13.45 M triangles and 11.14 ms (p95 11.85)**. It stands 3.7 to 4.7 m from three young firs (two `a`,
685,000-triangle scans) beside a dense fern field (1.3 M unique fern triangles in view; the floor without any young fir renders 6.85 M there,
knobs off, against 2.40 M at crouch_hide). This is the scan level's cost (arm C) at a spot where a player can stand; it is not one of the four
views the spec measures. pano_90 is 10.84 ms, trail_strip_01 9.87, hollow_rim 9.73.

## 7. Deviations recorded here

| # | Spec | Now | Why |
|---|---|---|---|
| 1 | 7.2: core spacing `lerp(7, 3, T)` | Steep ramp, 8.5 to 3.0 m (section 2) | Distinct thickets |
| 2 | 7.2 / 3.6: bank band at the thicket rule | Wall profile, 3.0 m on the foot, 8 m at 20 m | A wall the player sees |
| 3 | 9.1: "Custom frame settings: none" | Decals off on the forest camera (section 10 knob 1) | Frame budget; no decals in the scene |
| 4 | 8.2: shadows 80 m, 4 cascades, 0.08 / 0.22 / 0.50 | 50 m, 3 cascades, 0.12 / 0.35 (section 10 knob 2) | Frame budget; young-fir shadows were 31 % of crouch_hide's triangles |
| 5 | 5.6.3 step 6 (and M3a's stick rule) | Stick width from the tube's surface | Tan bars in trail_start |

## 8. Open, for the owner and for M4

1. **Deadfall view over budget** (13.45 M, 11.14 ms by stopwatch). Options, none taken: knob 3 (measured 0 to 0.4 ms),
   knob 5 (fern near level, which review 2 of M2 deliberately pushed to 24 m), or the scan distance (E1 keeps 6 m because
   crouch_hide is inside the budget; 4 m would move fir_close off the scan).
2. **Frame-time headroom for M4**: about 0.9 ms at crouch_hide, 0.2 (pano_90) to 2.5 ms (clearing) at the other views, before any tall fir. Knobs 1 and 2
   are spent; 3 to 5 remain.
3. **Thicket share 22 %**, not the spec's ~30 %: at 3 m spacing 30 % needs about 500 core firs, which the count test (770
   within 15 %) and the triangle cost do not allow. p3 (2.7 m, 28 %) cost 1 to 3 M more triangles at views facing thickets.
4. The open ground between stands is bare litter at middle range (the floor of M2); the tall firs of M4 are the next
   layer there. Distant young firs read grey-green in the mist.
5. The triangle and vertex budgets stay at their M3a re-base (11.6 M, 19.0 M); the measured worst is now 9.53 M and
   12.27 M. They were not lowered back, and the spec's own 8.0 M is still exceeded by 1.5 M.
6. Frame-time readings drift by up to 1 ms within a run on this machine; single numbers near 11 ms are not decisive.

## 9. Incident

The first measurement runs passed a relative `-captureOut` (`captures/m3-r0/...`), which Unity resolved against the
project, so a `captures/` folder appeared in the project root. Its contents (two measurement texts, one capture set of
iteration p2) were moved to `build/captures/m3-r0` and the folder was deleted; nothing else was there. The scripts now
pass absolute paths, and `tools/m3b/final.sh` checks for the folder.
