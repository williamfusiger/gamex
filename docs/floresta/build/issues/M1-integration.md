# M1 integration: what was changed, and what Unity turned out to do

Written 2026-10-13 by the M1 integrator. Unity 6000.6.0f1, HDRP 17.6.0, Metal, Apple M4 Max, batch mode.
Everything below was run in Unity; the log named with each item is under `build/logs/`.

The scene built and passed its audit on the first launch (`M1-02-scene-build.log`), with the spec's values
untouched. No stage threw, and nothing in `ForestTerrain.Create`, `ForestWorldRig`, `ForestLighting.Build`
or the audit's scene checks had to be repaired. The changes below are what the first runs then showed.

## 1. Changes to forest files (five files)

| # | File | What | Why | Evidence | Note |
|---|---|---|---|---|---|
| 1 | `ForestSceneBuilder.cs` | The forest scene is appended to Build Settings only with `-forestAddToBuildSettings` | Spec 11.5 step 12 rewrites `ProjectSettings/EditorBuildSettings.asset`, a pre-existing file the build rules protect. Restored from the backup | `M1-02` (appended), `M1-03` on (not appended); `check-preexisting.sh`: 0 differ | `M1-build-settings.md` |
| 2 | `ForestMaterials.cs` | Each terrain layer's height remap is centred on that layer's mean height; same amplitude on all | The height blend pushed low-lying scans (needles, humus, stony soil) out of every mix | `M1-iter02-import.log` (8 layers rebuilt); captures `iterations/iter01` against `iter02` | `M1-ground.md` |
| 3 | `ForestTerrain.cs` | Painting gains for humus, moss, needles and the centre strip; patch breakup; the gully painted as a broken stony bed | One litter texture from wall to wall; the gully read as a second track | captures `iterations/iter01` to `iter04`, final set in `captures/m1/` | `M1-ground.md` |
| 4 | `Tests/ForestPlayMode/ForestPlayModeTests.cs` | `Budget_Counts`: when the engine's `Draw Calls Count` reads 0 although triangles were drawn, the draw-call budget is checked against a count made from the scene | The engine does not feed that counter here: 0 with the terrain and with 50 cubes. The test could not pass for any scene | Failure `M1-06`; measurements `M1-07-diag-counters*.log`; pass `M1-09` and the final run `M1-33` | `M1-draw-call-counter.md` |
| 5 | `Tests/ForestEditMode/ForestSceneTests.cs`, `Scene_IsClosed` | The probe ray that asks "does edit-mode physics answer?" looks for the terrain collider's hit among all hits, not for the first hit | The ray comes down on the spawn, where the player stands: the first hit was the player's `CharacterController`, 1.85 m above the ground, so the test ignored itself with "physics does not answer" although probe P12 says it does. The wall check itself is unchanged and now runs | Ignored in `M1-05`; passed in `M1-08` and in the final run `M1-32` (0.23 s, 20 points x 64 bearings) | |
| 6 | `Tests/ForestEditMode/ForestSceneTests.cs`, `Terrain_ExtraLayersRender` | After `Terrain.CreateTerrainGameObject` the dirty mark on HDRP's default terrain material is cleared (only if it was clean before) | Same engine behaviour the M0 probe found. This test creates five test terrains; at the end of the first M1 run of the suite Unity rewrote the file in `Library/PackageCache` | Warning `M1-05-forest-editmode.log:625`; clean: `M1-27`, `M1-32` (0 "immutable" lines, md5 equal to the Unity install's copy) | section 2 |

The originals of all 27 editor files and all test files as they were before M1 are in
`build/backup/M1-pre-edit/`; the five diffs are `build/out/M1/*.diff`.

No budget, threshold or audit rule was changed. No test was removed or skipped. The four tests still
ignored are the kit and tree tests (M2, M3).

## 2. Outside the project: one file in `Library/PackageCache` was put back, again

`Library/PackageCache/com.unity.render-pipelines.high-definition@03bdf33c5697/Runtime/RenderPipelineResources/Material/DefaultHDTerrainMaterial.mat`
was rewritten by Unity at the end of the first EditMode run of M1 (item 6; one added line,
`m_AllowLocking: 1`, as at M0). I found it only at the end, by checking the md5. Copied back from
`/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/Resources/PackageManager/BuiltInPackages/com.unity.render-pipelines.high-definition/`
(md5 `8ce2f7482ff8fb845c1b94b0082b4cf0` on both). The altered copy is
`build/out/M1/DefaultHDTerrainMaterial.altered-by-editmode-test.mat`. The next launch logged the
"unexpectedly altered" warning once while re-importing the restored file (`M1-26`, at start-up, before any
test); the run after that is clean (`M1-27`).

Everything the M1 result rests on was run again after the restore, with the file as shipped: the final
build (`M1-30`), the final full capture (`M1-31`), and the three test runs (`M1-32` to `M1-34`). None of
those logs holds the warning, and the md5 is still the install's after them. The images of the repeated
capture differ from the set captured before the restore by an RMSE of at most 0.0033 (film grain).

`ForestTerrain.Create`'s own dirty-mark fix (M0 item 6, never executed before) works: no scene build of
M1 logged the warning.

## 3. Open questions of the spec that M1 answered

| Question | Answer | Evidence |
|---|---|---|
| `TerrainData` ordering rules (6.1) | The order of 6.1 works: 1025 heights, size (256, 60, 256), alphamap 2048 read back as asked | `M1-02`: "1025 x 1025 heights, 8 layers at alphamap 2048" |
| Do eight layers render in the real scene (P10, risk 4)? | Yes: `Terrain_ExtraLayersRender` passes for layers 4 to 7 on the built material; mud, rock, stony soil and needles are visible in the captures | `out/M1/tests/forest-editmode.xml`; `captures/m1/overview_ortho.png` |
| Does the trail read as two ruts at alphamap 2048 (risk 24)? | Yes, at the spec's rut widths | `captures/m1/trail_start.png`, `ground_close.png` |
| Do the profiler counters report in play mode (open half of P5)? | Triangles, vertices, SetPass calls, shadow casters and both memory counters: yes. Draw calls and batches: never | `M1-07-diag-counters-b.log`, `-d.log` |
| Do enabled cameras render by themselves in batch-mode play mode (12.3)? | Yes | `M1-07-diag-counters-d.log`, stages 2 and 3 |
| Frame time by stopwatch | 6.7 ms median, 7.4 ms p95 at 2560 x 1440 on the M1 scene (budget 11). `FrameTiming` reported 7 of 240 frames | `captures/m1/playmode/frame-time.txt` |
| Does a `CharacterController` rest one skin width up (WP5 deviation 5)? | `Player_SpawnsOnTheGround` passes with that reading | `out/M1/tests/forest-playmode.xml` |
| Edit-mode physics on the real scene (P12) | Works: the audit reports `physicalCorridors: free`, `Scene_IsClosed` runs | `ForestData/build-report.json` |

## 4. Found on the way, not changed

1. **The sky calibration warning** ("the lower hemisphere gives 0.36 times the light of the upper one") is
   logged on every build, as M0 said. Still a false alarm for this sky.
2. **`build-report.json` keeps its first timestamp** (`writtenUtc` 23:40:13Z, the first M1 build): the audit
   rewrites the file only when its content changes, and the paint retuning changes nothing it records.
3. **The trail simply stops at its north end** (`trail_strip_08`): a square end of gravel on open ground
   until the north outcrop's rocks stand there (M2).
4. **`trunk_close`, `fir_close`, `rock_close`, `crouch_hide` and `canopy_up` show bare ground or sky** at
   M1: their subjects do not exist yet. They are in the set so the full set is the same at every milestone.
5. **Exposure sits at its upper clamp everywhere** (open ground is neutral at 10.94 EV, the clamp is 10.5).
   Without a probe bake trees do not take sky light away, so this will stay so under the firs. It is what
   spec 8.2 and 8.5 describe; `trail_start` is at 0.43 of a 0.45 window.
