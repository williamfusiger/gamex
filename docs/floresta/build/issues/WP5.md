# WP5 (Proof): deviations, interpretations and things found

Written 2026-10-13 by the WP5 owner. Unity was not started. Everything below was established by
reading and compiling; where something was actually executed (outside Unity) it says so.

## 1. Deviations from the spec text

| # | What | Why | What was done |
|---|---|---|---|
| 1 | The probe writes its answers twice: `<captureOut>/probe.json` (spec) and a copy at `Library/GamexForest/probe.json` | The spec makes two later checks depend on probe answers (the audit's physical corridor check "only when probe P12 found edit-mode physics reliable"; `Budget_FrameTime` "asserts only after probe P5 has validated the method") but names no place they are read from. The default capture folder is `Temp/...`, and the project's `Temp` folder does not survive the Editor quitting (it is absent right now although the Editor ran today) | `ForestProbe.RecordPath`. The audit reads it through `ForestProbe.EditModePhysicsIsReliable`; the PlayMode suite reads the same file by path. A record from another Unity version is ignored. No record means: analytic corridor check only, frame time logged and not asserted |
| 2 | `build-report.json` also holds the three trail centre lines sampled every metre (`corridors`), and the PlayMode test `Corridors_ArePhysicallyFree` reads them from that file | Spec 12.3 says the PlayMode assembly reads what it needs from the scene. The scene has only the control points (`Gameplay/PatrolNodes`) and waypoints every 10 m. Sweeping a 2 m capsule "every metre" needs the real centre line, and rebuilding it in the test would need a second copy of WP3's private spline code; a copy that curved differently would sweep the wrong lane | The audit (the only writer of the report) samples `ForestLayout.SampleSpline`. The test fails with a clear message if the report or the lines are missing |
| 3 | `ForestAudit.Run` does not write the report when the open scene is not `Forest.unity`; it returns the findings with an error `scene.forest` | A menu click on "Audit Open Scene" with the greybox open would otherwise replace the forest's build report with a failed one, and captures would then refuse to run | One `if` in `Run` |
| 4 | `ForestAudit.Run` rewrites the report only when its content changed (timestamp and timings are ignored in the comparison) | 11.5 says tests never write inside `Assets`, 12.3 says `Scene_PassesAudit` re-runs `ForestAudit.Run(false)`, and `Run` is the report's writer. With this, re-running the audit on an unchanged scene touches nothing | `ForestAudit.WriteReport` |
| 5 | `Player_SpawnsOnTheGround` compares `position.y - skinWidth` with the terrain, within 5 cm | A `CharacterController` rests one skin width (0.08 m by default) above what it stands on, so "the player's height within 5 cm of the terrain" taken literally could fail on a correct scene. Not verifiable without running | Stated in the test. If the first run shows the controller rests at +0.05, both readings pass |
| 6 | `ForestCapture` ends with an exception when a captured frame has error pixels, is black or blown out, or when one of `trail_start`, `trail_mid_fwd`, `clearing` is outside the luminance window 0.10 to 0.45 under OvercastMist. All files are written first, and `capture-summary.json` lists every problem | The milestone gate says "captures written with zero error pixels and luminance in range" but the spec does not say what tells a batch run that this failed. A stage that produces something wrong throws (11.1) | Debug views and non-forest scenes (`-captureScene`) are not judged for exposure. Error pixels are counted strictly (any pixel) in captures; the PlayMode test uses the spec's 0.1 % |
| 7 | The log's bounds are not compared with the manifest while `ForestKitSpec.LogPitchDegrees` is not 0 (audit: a warning; `Prefabs_MatchManifestBounds`: skipped for the log) | 7.3 offers pitching the derived log mesh by about 4 degrees; a pitched 4 m log misses the 2 % bounds rule by design | Default is 0, so nothing is skipped today |
| 8 | Extra checks beyond the table of 12.1: scene structure (roots present: error; wall count and renderers on walls: warning), spawn yaw and exact spawn XZ, instance is an instance of the right prefab and filed under the right cell or landmark, a texture in `_NormalMap`/`_MaskMap` whose keyword is off, no texture above 4096 | Each catches a build fault that the listed rows would let through, at no cost | Listed with their check names at the top of `ForestAudit.cs` |
| 9 | Extra tests beyond 12.3: `ImageStats_MeasureLevelsAndErrorColours`, `ImageStats_RmseComparesImages`, `Budgets_PlayModeCopyMatches` | WP5's own pure code had no test; and the PlayMode suite must keep copies of the budgets (12.3), which nothing guarded against drift | In `ForestPureCodeTests.cs` |
| 10 | `Terrain_ExtraLayersRender` paints one patch each for layers 4 to 7 (and layer 0 as a control), not layer 5 only | Same code, four times the coverage. If the control patch is black the test is ignored, because then the render says nothing | In `ForestSceneTests.cs` |
| 11 | The probe uses a temporary asset folder `Assets/ForestProbeTemp` for P8 (BC6H copy of the sky) and P15 (a 2048 alphamap saved as an asset) and deletes it before it ends, also when an item throws | "Import a compressed copy" and "saving the asset" need real assets. Nothing is left in the project | A folder left by a crashed run is deleted at the start of the next |
| 12 | P3 measures import time by forcing a re-import of six textures (one 2k set, one 4k set) instead of reading log timestamps | The import usually happened in an earlier Editor session whose log the probe cannot see. A forced re-import gives the same artifacts again; nothing is renamed or deleted | `-forestProbeNoReimport` skips it |
| 13 | `Manifest_LoadsAndPathsAreUnique` does not compare the postprocessor's per-asset table with the manifest, although 5.3 says it does | The postprocessor has no public member (11.4; `issues/contract.md`, inconsistency 1). WP1 runs that comparison at the start of `ForestImport.Run` instead (`issues/WP1.md`, section 1) | `Textures_FollowPolicy` checks the table's effect on every imported texture |
| 14 | `Scene_NavMeshConnectsLandmarks` counts a landmark as reached when any walkable point inside its circle (`Landmark.radius`, at least 6 m) has a complete path from the spawn; it also requires each `Gameplay/DinoSpawns` anchor to be on the NavMesh and reachable | Two landmark points lie in the bank and (2, 71) lies inside the outcrop's rocks by the spec's own geometry (`issues/WP3.md`). The dinosaur spawn anchors are what later code will path to | In `ForestSceneTests.cs` |
| 15 | Probe P1 reports the mirrored axis from every object of `fern_02` and `rock_moss_set_02` that sits clearly off its pivot, not from `fern_02_b` and `rock13` alone | `rock13` is 3.9 cm off-centre against a tolerance of 4.2 cm and can only answer "symmetric" (`issues/WP2.md`). The two named objects are still listed in the item's details | `ForestProbe.ProbeImportedModels` |

## 2. Things the spec leaves open that had to be decided

- **Assert.Ignore, never Assert.Inconclusive.** Unity's command-line runner treats an inconclusive test
  as a failed run (`com.unity.test-framework/UnityEditor.TestRunner/CommandLineTest/RunStateCallbacks.cs:35`
  sets `OneOrMoreTestsExecutedWithOneOrMoreFailed` for `TestStatus.Inconclusive`). Every test whose
  content does not exist yet therefore ignores itself with a reason, as 12.3 prescribes. An ignore
  thrown from `[UnitySetUp]` is recorded as ignored and the test body is skipped
  (`TestResultExtensions.cs:38-43`, `BeforeAfterTestCommandBase.cs:178-185`).
- **`Budget_FrameTime`.** It always logs both methods (FrameTiming GPU time; stopwatch around a
  one-pixel readback) and writes `frame-time.txt`. It asserts the 11 ms budget only when the probe
  record exists for this Unity version: with P5 "yes" on FrameTiming if FrameTiming also reports in
  play mode, otherwise on the stopwatch (the spec's fallback, an upper bound). Before the probe has
  run it logs and passes, which is what the spec's "logs ... asserts only after" describes.
- **`Budget_Counts`.** A counter that is valid but reads 0 for every frame fails the test: it means
  the counters did not see the measured camera, and a budget "met" by a zero would be a silent pass.
  Memory is the forest's counters minus the same counters in an empty scene measured afterwards.
- **`Scene_IsClosed`.** The capsule is swept horizontally from each interior point at the ground
  height of the place where that bearing meets the wall line, which is where the player would stand
  when reaching the wall. If a raycast onto the terrain at the spawn does not answer in edit mode the
  test is ignored (probe P12 is the open question; `Bank_CannotBeClimbed` covers the edge in play mode).
- **Slopes read back from the terrain.** The audit uses central differences on the heightmap inside the
  boundary (the same stencil as `ForestFields`) and allows 0.15 degrees for the 16-bit quantisation
  (`ForestBudgets.SlopeReadbackToleranceDegrees`); along the trail it measures the gradient in the
  direction of travel in 0.5 m steps.
- **`ForestImageStats.Stats`** is declared in 11.4 without `[Serializable]`, so `JsonUtility` would
  skip it. The surface was kept as written; the capture sidecar copies the numbers into a private
  serializable struct.

## 3. Found on the way, for the integrator

1. **The spec's command lines write into `Temp`**, which Unity removes when the Editor quits:
   `-captureOut $P/Temp/Captures/forest`, `-testResults $P/Temp/forest-editmode.xml`, and the default
   of `Render_ReferenceStills` (`Temp/Forest/playmode/`). For batch runs pass paths outside `Temp`
   (for example under the build folder's `captures/`). `Render_ReferenceStills`, `Budget_Counts` and
   `Budget_FrameTime` honour `-captureOut DIR` and write to `DIR/playmode/`. The claim about `Temp`
   rests on the folder being absent now and on general knowledge of Unity; it was not tested here.
2. **The audit's bounds row can fail for thinned trees.** 12.1 compares each tree's first thinned level
   with the manifest box at 2 % of the largest dimension (0.18 m for an 8.8 m fir), while 5.6.3 enlarges
   surviving twig pieces about their anchor. WP2's thinner says it stops a piece "where it would leave
   the crown's own outline", which should keep the box; this is M3 content and could not be tried.
3. **18 of the EditMode tests can be run without Unity.** `out/WP5/harness/run-pure-tests.sh` compiles
   the real editor sources and runs the manifest, mesh-tool, thinner, sky, image-statistics, layout,
   terrain and scatter tests on .NET 8 with Unity's managed assemblies (a small stand-in replaces
   NUnit, and the manifest is read with System.Text.Json because `JsonUtility` is native). Last result,
   against the files of all five packages as they were at that moment: 18 passed, 0 failed
   (`out/WP5/pure-tests-outside-unity.log`). Seven deliberately broken copies of those tests were
   reported as failed, so the run is not vacuous. This is not a Unity run: Mono may round differently,
   and nothing that touches a native engine call was exercised.
4. **Nothing in the capture harness, the probe, the audit's scene checks or the scene and kit tests has
   been executed.** They compile against the real files of all packages; their behaviour in Unity is
   unverified until the first launch.
