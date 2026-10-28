# Forest build: state log

Paths: `BUILD` is this folder, `PRJ` is `/Users/william/Documents/GitHub/gamex`.

## 2026-10-13, integrator: milestone M0 (foundations and probe)

**Result: the M0 gate is passed.** Unity compiles the project with 0 errors and 0 warnings, the probe ran
and its answers are applied, the forest EditMode suite is green (23 passed, 11 ignored for content that
does not exist yet, 0 failed), the seven greybox tests pass, and the capture harness works on the greybox.
No forest scene and no prefab was built.

### What was run, in order

| # | Run | Result | Evidence |
|---|---|---|---|
| 1 | `compile.sh` on all 27 editor files, `compile-tests.sh` on both test assemblies (outside Unity) | `OK`, `OK`, `OK`; logs empty. Repeated on the final files | `out/M0/*.dll.log` (0 bytes) |
| 2 | `fetch_assets.py ... --milestone M2 --dry-run` | 68 files in place, 0 to fetch (size and md5 checked) | `logs/M0-fetch-dryrun.log` |
| 3 | First Unity launch (`-batchmode -quit`): compile and import | Exit 0 in 21 s. 0 `error CS`, 0 `warning CS`. Three new assemblies in `Library/ScriptAssemblies`. Scripts compiled before the 543 MB were imported, so the postprocessor applied on the first import. 887 imports, 17.7 s | `logs/M0-01-import.log` |
| 4 | `ForestImport.Run` | Exit 0. 17 assets complete, 68 files hashed, 18 textures packed, 18 assets built (10 materials and 8 terrain layers), 12.1 s | `logs/M0-02-forest-import.log` |
| 5 | `ForestProbe.Run`, first time | Exit 0, 13 items. P7 answered "no" for a reason that was the probe's, and the run rewrote a file in the package cache: both fixed, see `issues/M0-integration.md` | `logs/M0-03-probe.log`, `captures/M0/probe-first-run/` |
| 6 | Diagnostic probe runs (`-forestProbeItems`) | Causes found | `logs/M0-04-*.log` to `M0-06-*.log` |
| 7 | Launch after the sky import rule changed | Exit 0. Everything re-imported under postprocessor version 2 in 25.8 s; the sky is BC6H | `logs/M0-07-reimport-v2.log` |
| 8 | `ForestProbe.Run`, final | Exit 0, 13 items, none "error" or "unknown". No package-cache warning | `logs/M0-08-probe.log`, `captures/M0/probe/probe.json` (+ three PNGs, looked at) |
| 9 | Forest EditMode suite, first time | **1 failed**: `Materials_AreValidHdrp`, a false alarm of the audit in a fresh Editor. Fixed in `ForestAudit.cs` | `logs/M0-09-forest-editmode.log` |
| 10 | Forest EditMode suite, final | Exit 0. 34 tests: **23 passed, 0 failed, 11 ignored** | `out/M0/tests/forest-editmode.xml`, `logs/M0-11-forest-editmode.log` |
| 11 | Greybox PlayMode suite (`Gamex.Tests.PlayMode`) | Exit 0. **7 passed, 0 failed** | `out/M0/tests/greybox-playmode.xml`, `logs/M0-12-greybox-playmode.log` |
| 12 | Forest PlayMode suite | Exit 0. 10 tests, all ignored: `Forest.unity` does not exist | `out/M0/tests/forest-playmode.xml`, `logs/M0-13-forest-playmode.log` |
| 13 | `ForestCapture.Capture -captureSet quick -captureScene Assets/Game/Scenes/Greybox.unity` | Exit 0. Three images with crops and sidecars, 0 error pixels. Looked at all three: the night greybox, correctly lit, from the player's eye, obliquely and from above | `captures/M0/greybox/`, `logs/M0-14-capture-greybox.log` |

The 11 ignored EditMode tests: `Prefabs_FollowContract`, `Prefabs_MatchManifestBounds` (no prefab: M2),
`TreeLods_WithinBudget`, `TreeLods_KeepCoverage` (no tree: M3), and the seven scene tests (no scene: M1).
None of them applies to M0.

The 59 files of `backup/pre-forest.md5` are unchanged (`tools/check-preexisting.sh`: 0 differ, last run after
step 13). No git command that changes anything was run.

### What was changed

Six forest files; each change with its reason and evidence is in `issues/M0-integration.md`. In short:

- `ForestAssetPostprocessor.cs`: the sky imports as BC6H (probe P8), version 2.
- `ForestKitTests.cs`: `Textures_FollowPolicy` expects that.
- `ForestAudit.cs`: the "is this an HDRP shader" check no longer gives a false error before the first render
  of a session.
- `ForestProbe.cs`: P7 measures through HDRP's ambient probe when the Inspector's shader draws nothing; P8
  compares both imports whichever the project uses; P10 clears the dirty mark the engine puts on the
  package's default terrain material; `-forestProbeItems` for partial runs.
- `ForestTerrain.cs`: the same dirty-mark fix in `Create`. **Not executed yet** (no scene at M0).
- `ForestLighting.cs`: one comment.

Originals: `backup/M0-pre-edit/`.

Outside the project, one file in `PRJ/Library/PackageCache` (HDRP's `DefaultHDTerrainMaterial.mat`) was
rewritten by Unity during run 5 and put back from the Unity install's copy of the same package.

New in the project, all where the spec puts them: `.meta` files for the new sources and downloads;
`Assets/Game/Scenes/ForestKit/{Textures,Materials,TerrainLayers}` (121 MB: 18 packed textures, 10 materials,
8 terrain layers). `Library/GamexForest/` holds `stamps.json` and `probe.json`.

### Probe answers (full table in `issues/M0-integration.md`)

- Fallbacks selected: **P5** (counters and frame timing do not report for explicit edit-mode renders: time
  is measured by stopwatch with a one-pixel readback) and **P6** (the explicit readback wait stays). Both
  were already the code's defaults; nothing had to be switched.
- Upgrade taken, as the spec directs: **P8** (BC6H sky).
- Everything else confirmed its default: importer mirrors X and mesh data is readable (P1), eight terrain
  layers render (P10), LODGroups switch where the formula says and cross-fade dithers (P11), edit-mode
  physics is reliable (P12), alphamap 2048 is accepted (P15), the CPU sky lux is right to 1 % (P7), and
  Unity's mesh LOD generator works on script-built meshes (P4; `UseUnityGenerator` stays false, the
  generator is the optional upgrade of risk 12).

### What the next engineer (M1) must know

1. **Use `tools/unity.sh <log name> <unity arguments>`** for every launch. It refuses to start while a
   Unity process runs, never passes `-nographics`, writes the log to `logs/`, and prints the exit code.
   `tools/test-summary.py <result.xml>` prints a result file; `tools/check-preexisting.sh` checks the 59 files.
2. **Runs are fast on this machine**: a launch with nothing to import takes 5 to 12 s, the whole first
   import took 18 s, the EditMode suite 12 s.
3. **Do not write to `PRJ/Temp`**: it does not exist between runs. Pass `-captureOut` and `-testResults`
   under `BUILD` as above.
4. **The audit has never run on a scene.** Its kit half ran (`Materials_AreValidHdrp` passes: 10 materials,
   8 terrain layers, 0 errors). `ForestSceneBuilder.Build` throws if the audit finds an error, so expect the
   first M1 build to need a look at `ForestData/build-report.json`.
5. **`ForestSceneBuilder.Build` will change `ProjectSettings/EditorBuildSettings.asset`** (it appends the
   forest scene; spec 11.5 step 12). After that `check-preexisting.sh` reports that one file as changed. That
   is the spec's instruction; `Greybox_StillFirstInBuildSettings` guards the order.
6. **A fresh batch process has no render pipeline instance until something renders.** Until then
   `Material.GetTag("RenderPipeline", ...)` answers `""` and `Shader.globalRenderPipeline` is empty. The
   audit copes with it now; other code that asks the engine about active sub-shaders will not.
7. **`Terrain.CreateTerrainGameObject` dirties HDRP's default terrain material.** `ForestTerrain.Create` now
   clears the mark. If the first scene build still logs "asset(s) located in immutable packages were
   unexpectedly altered", that fix did not work there: look at it before going on.
8. **The sky calibration warning is a false alarm for this sky.** Every build will log "the lower hemisphere
   gives 0.36 times the light of the upper one". P7 checked the orientation and the value end to end.
9. **Sky numbers for M1**: `upperHemisphereLuxValue` 4.683; asked for 5,000 lux, HDRP's sky delivers 4,946
   on level ground. The cubemap has 2048 faces and is BC6H (about 34 MiB).
10. **Not validated at M0 and due at M1**: whether the profiler counters report in play mode (`Budget_Counts`
    fails on all-zero counters), frame time by stopwatch (`Budget_FrameTime` asserts 11 ms on it now that the
    probe record exists), `ForestLighting.Build`, `ForestTerrain.Create`, `ForestWorldRig`, the audit's scene
    checks, and every forest PlayMode test.
11. **For M3**: with `fadeTransitionWidth` 0.2 a level that ends at 14 m is in cross-fade from about 5.5 m
    on (P11), and both levels are drawn there.
12. **A partial probe run** (`-forestProbeItems`) does not replace the record in `Library`. A full run does.
    Re-run the full probe after a Unity or HDRP upgrade: the record is ignored for another Unity version.

### Not done, by instruction

No kit build (`ForestPrefabs.BuildAll`), no scene build, no tree assets fetched. `fir_sapling_medium` and
`fir_tree_01` still say `objectsSource: "gltf"` in the manifest.

## 2026-10-13, integrator: milestone M1 (walkable ground under a real sky)

**Result: the M1 gate is passed.** `Assets/Game/Scenes/Forest.unity` builds, the in-builder audit passes
(0 errors, 0 warnings), the full capture set shows a correctly exposed, textured, misty valley with a
readable two-rut trail, and all three test runs are green. No kit prop was built: the scene is terrain,
sky, light, player, walls, NavMesh and anchors only. The M2 assets are downloaded and imported but
`ForestPrefabs.BuildAll` has not run.

### What was run (final runs; the earlier ones are in section "iterations")

| # | Run | Result | Evidence |
|---|---|---|---|
| 1 | `fetch_assets.py ... --milestone M1 --dry-run` | 34 files in place, 0 to fetch (size and md5 checked) | `logs/M1-fetch-dryrun.log` |
| 2 | `ForestImport.Run` | Exit 0. 17 assets complete, 18 textures and 18 materials/layers up to date | `logs/M1-01-forest-import.log`, `M1-28-final-compile-check.log` (0 `error CS`, 0 `warning CS`) |
| 3 | `ForestSceneBuilder.Build` (final) | Exit 0. "Forest audit passed: 0 instances, 116 GameObjects, 1 materials, 0 meshes, 0 warning(s)". 2,459 placements planned, 0 instantiated (no prefabs). Plan 5.3 s, scene 3.5 s | `logs/M1-30-final-build.log`; `PRJ/Assets/Game/Scenes/ForestData/build-report.json` (`passed: true`, trail slope 5.2 of 12 degrees, core slope 25.8 of 27, `physicalCorridors: free`) |
| 4 | `ForestCapture.Capture -captureSet full` (final) | Exit 0. 30 images at 2560 x 1440 with crops and sidecars; `passed: true`, 0 error pixels, no pixel clipped white or crushed black, mean luminance 0.25 to 0.78 | `captures/m1/` (`capture-summary.json`), `logs/M1-31-final-capture-full.log`. All 30 looked at |
| 5 | Forest EditMode suite | Exit 0. 34 tests: **30 passed, 0 failed, 4 ignored** (prefabs and tree LODs: M2, M3) | `out/M1/tests/forest-editmode.xml`, `logs/M1-32-final-forest-editmode.log` |
| 6 | Forest PlayMode suite | Exit 0. **10 passed, 0 failed** | `out/M1/tests/forest-playmode.xml`, `logs/M1-33-final-forest-playmode.log`, stills and numbers in `captures/m1/playmode/` |
| 7 | Greybox PlayMode suite (`Gamex.Tests.PlayMode`) | Exit 0. **7 passed, 0 failed** | `out/M1/tests/greybox-playmode.xml`, `logs/M1-34-final-greybox-playmode.log` |
| 8 | `ForestCapture.Capture -captureSet debug` (on iteration 4, same paint as the final) | 18 debug images: albedo, normal, clay, diffuse lighting, cascades, mip ratio | `captures/m1/iterations/iter04-debug/debug/` |

Numbers from the PlayMode run: the trail walked at 3.2 m/s in 44.9 s with 0 of 2,692 frames off the
ground, sprinted at 6 m/s with 0.00 % off the ground; after 20 s of pushing outward the furthest probe is
0.61 m outside the boundary line; mean luminance 0.429 (`trail_start`), 0.412 (`clearing`), 0.277
(`ground_close`); frame time by stopwatch 6.7 ms median, 7.4 ms p95 at 2560 x 1440 (budget 11);
128,933 triangles, 27 SetPass calls, 4 shadow casters, 354 MiB of textures above an empty scene.

The 59 files of `backup/pre-forest.md5` are unchanged (`tools/check-preexisting.sh`: 0 differ, last run
after step 7). No git command that changes anything was run.

### What was changed

Five forest files; each change with its reason and evidence is in `issues/M1-integration.md`.

- `ForestSceneBuilder.cs`: **the scene is no longer appended to Build Settings unless
  `-forestAddToBuildSettings` is passed.** Spec 11.5 step 12 rewrites
  `ProjectSettings/EditorBuildSettings.asset`, which the build rules protect. The first build did append
  it; the file was put back from the backup. `issues/M1-build-settings.md`. **Needs the owner's decision.**
- `ForestMaterials.cs`: each terrain layer's height range is centred on its own mean (same amplitude).
- `ForestTerrain.cs`: painting gains, patch breakup, the gully as a broken stony bed. `issues/M1-ground.md`.
- `Tests/ForestPlayMode/ForestPlayModeTests.cs`: `Budget_Counts` checks the draw-call budget against a
  count made from the scene when the engine's counter reads 0. `issues/M1-draw-call-counter.md`.
- `Tests/ForestEditMode/ForestSceneTests.cs`: `Scene_IsClosed` no longer ignores itself (its probe ray hit
  the player); `Terrain_ExtraLayersRender` no longer dirties HDRP's default terrain material.

Originals: `backup/M1-pre-edit/`. Diffs: `out/M1/*.diff`.

Outside the project, the same file in `PRJ/Library/PackageCache` as at M0 (HDRP's
`DefaultHDTerrainMaterial.mat`) was rewritten by Unity, this time by the EditMode suite, and put back from
the Unity install's copy. The cause is fixed; the final runs above were all made after the restore.

New in the project: `Assets/Game/Scenes/Forest.unity`; `Assets/Game/Scenes/ForestData/` (`Terrain.asset`
47 MB, `NavMesh.asset`, `SurfaceMap.png`, `placements.json`, `build-report.json`,
`Lighting/Atmosphere_OvercastMist.asset`, `Lighting/Atmosphere_Inspect.asset`) with their `.meta` files.
The eight `.terrainlayer` assets were rewritten once (height centring).

### Iterations on the look (`captures/m1/iterations/`)

| Step | Change | What the pictures showed |
|---|---|---|
| iter01 | Spec values | Correct exposure, sky, mist and ruts. Ground: one litter texture everywhere, its tile visible as rows; the gully read as a second track; centre strip not mossy |
| iter02 | Height centring; humus 1.3, moss 2.0, needles 1.6, strip moss 1.5; gully broken up | Moss, humus and needle patches visible from eye level and from above; mossy strip; gully a stony bed |
| iter03, iter04 | Patch breakup noise (iter04: on a 0.5 m lattice, 2.3 s faster) | Patch edges and interiors less uniform. iter04 is the final paint |
| iter05 | Tried 0.4 of humus under the litter everywhere | Tile pattern slightly dimmer, ghost leaves in the close-up. Taken back |

### What the next engineer (M2) must know

1. **`tools/m1-iter.sh <tag> [quick|full]`** runs import, scene build and a capture into
   `captures/m1/<tag>` and prints the statistics. Copy it for M2.
2. **Run `ForestSceneBuilder.Build`, not `ForestBuild.BuildAll`, when no kit build is wanted.** At M2,
   `BuildAll` (or `ForestPrefabs.BuildAll` first) builds the kit; the scene builder then places whatever
   prefabs exist and stamps moss and stony soil around them.
3. **Check the md5 of `DefaultHDTerrainMaterial.mat` in `Library/PackageCache` after every kind of run,
   not only after builds** (`8ce2f7482ff8fb845c1b94b0082b4cf0`). `Terrain.CreateTerrainGameObject` anywhere
   (builder, probe, tests, and any new code) dirties it; a `grep -ci immutable` on the log also shows it.
4. **`Budget_Counts` reports an estimated draw-call figure** (4 at M1: the terrain's shadow casters). From
   M2 on it counts visible renderers; look at whether the number is believable against the kit report.
5. **Exposure has little headroom**: `trail_start` is at 0.43 of a 0.45 window, in edit and in play mode.
   `ForestCapture` throws above 0.45 on `trail_start`, `trail_mid_fwd` and `clearing`, and
   `Render_ExposureIsSane` fails. Rocks and ferns should lower it. If M2 goes over, the cause is the
   exposure clamp of spec 8.2 on open ground, not the props.
6. **The litter tile is still visible** from above and at 15 to 40 m. It is the first thing the M2 ground
   cover has to hide (risk 13). `overview_oblique` is one of the review-1 captures.
7. **The forest scene is not in Build Settings.** Tests do not need it.
8. **Answered at M1** (details in `issues/M1-integration.md`, section 3): the terrain ordering rules, eight
   layers in the real scene, alphamap 2048 with two ruts, which profiler counters report in play mode,
   that enabled cameras render in batch-mode play mode, edit-mode physics on the real scene.
9. **Still open**: whether the eight-layer variant survives a player build (risk 27); the sky calibration
   warning that logs on every build (false alarm for this sky); everything about props and trees.

## 2026-10-13, integrator: milestone M2 (forest floor)

**Result: the M2 gate is passed, with the look left for the owner's review 1.** The kit builds (28
prefabs), `Forest.unity` builds with 1,402 instances, the in-builder audit passes (0 errors, 0 warnings),
the full capture set and the kit sheets are written with 0 error pixels, and all three test runs are
green. No tree asset was fetched or built. What the pictures show is a treeless fern-and-boulder heath in
mist: correct props, correctly lit and grounded, on a floor that is still sparse. Its weak points are
listed under "What the next engineer must know".

### What was run (final runs; earlier ones are in `issues/M2-floor.md`)

| # | Run | Result | Evidence |
|---|---|---|---|
| 1 | `fetch_assets.py ... --milestone M2` (not a dry run: it measures every FBX) | 68 files in place, 0 fetched, 8 models measured: all MATCH, the two stumps and the log included | `logs/M2-01-fetch.log` |
| 2 | `ForestImport.Run` | Exit 0. 18 textures and 18 materials/layers up to date | `logs/M2-50-final-import.log` |
| 3 | `ForestPrefabs.BuildAllFromMenu -forestForce` | Exit 0. "28 prefabs built, 0 up to date, 6 skipped for missing inputs, 0 failed" (the six are the firs) | `logs/M2-51-final-kit.log`, `out/M2/kit-report.json` |
| 4 | `ForestSceneBuilder.Build` | Exit 0. "Forest audit passed: 1402 instances, 4246 GameObjects, 10 materials, 67 meshes, 0 warning(s)". 2,459 placements planned, 1,402 instantiated (trees skipped). Largest grounding error 5.5 mm, corridor margin 2.57 m, spawn margin 6.03 m, `physicalCorridors: free` | `logs/M2-52-final-build.log`, `out/M2/build-report.json` |
| 5 | Inspection close-ups (temporary probe, `issues/M2-integration.md` section 2) | 117 images of logs, stump patches, rocks, branches, grass, ferns in the scene | `captures/m2-r0/inspect/`, `logs/M2-53-final-inspect.log` |
| 6 | `ForestCapture.CaptureKit` | Exit 0. 50 sheets of 28 prefabs, `passed: true`, 0 magenta, 0 cyan | `captures/m2-r0/kit/` (`kit-summary.json`), `logs/M2-54-final-capture-kit.log` |
| 7 | `ForestCapture.Capture -captureSet full` | Exit 0. 30 images at 2560 x 1440, `passed: true`, 0 error pixels, mean luminance 0.25 to 0.78, `trail_start` 0.413 | `captures/m2-r0/` (`capture-summary.json`), `logs/M2-55-final-capture-full.log` |
| 8 | Forest EditMode suite | Exit 0. 35 tests: **33 passed, 0 failed, 2 ignored** (tree LODs: M3) | `out/M2/tests/forest-editmode.xml`, `logs/M2-56-final-forest-editmode.log` |
| 9 | Forest PlayMode suite | Exit 0. **10 passed, 0 failed** | `out/M2/tests/forest-playmode.xml`, `logs/M2-57-final-forest-playmode.log`, stills and numbers in `captures/m2-r0/playmode/` |
| 10 | Greybox PlayMode suite (`Gamex.Tests.PlayMode`) | Exit 0. **7 passed, 0 failed** | `out/M2/tests/greybox-playmode.xml`, `logs/M2-58-final-greybox-playmode.log` |

All nine final logs: 0 `error CS`, 0 `warning CS`, 0 "immutable". `DefaultHDTerrainMaterial.mat` in
`Library/PackageCache` still has md5 `8ce2f7482ff8fb845c1b94b0082b4cf0`. The 59 files of
`backup/pre-forest.md5` are unchanged (`tools/check-preexisting.sh`: 0 differ, after run 10). No git
command that changes anything was run.

Looked at: all 30 final views and their 30 crops, the 3 play-mode stills, all 50 kit sheets and all 117
inspection images, as contact sheets (`captures/m2-r0/contact-sheets/`), with `trail_start`, `clearing`
and `overview_oblique` also one by one. The iteration sets were looked at the same way while tuning.

Numbers from the PlayMode run: trail walked with 0 of 2,692 frames off the ground, sprinted with 0.00 %;
furthest probe 0.61 m outside the boundary line; mean luminance 0.412 (`trail_start`), 0.407 (`clearing`),
0.259 (`ground_close`); frame time by stopwatch 7.85 ms median, 8.25 ms p95 (budget 11); 2,671,443
triangles, 52 SetPass calls, 214 shadow casters, 732 draw calls (estimated), 738 MiB of textures and
37 MiB of meshes above an empty scene.

### What was changed

Nine source files and the manifest; table in `issues/M2-integration.md`, reasons and evidence in
`issues/M2-floor.md`, diffs in `out/M2/*.diff`, originals in `backup/M2-pre-edit/`.

- **Rock set 01 had black patches**: the AO channel of its downloaded map does not fit the FBX's UVs
  (7 to 33 % of each rock's surface had zero AO). Its mask now carries full AO. The kit build has a gate
  for this and the EditMode suite a new test, `Prefabs_OcclusionMapsFitTheirMeshes`.
- **The log was a black silhouette**: its colour multiplier is 0.85, not the spec's 0.6.
- **Grass patches** are six tufts drawn together at about twice scan size, not twelve at life size.
- **Ferns** keep the cap of 900 but stand in smaller, denser drifts (median neighbour 2.33 m, was 3.05 m).
- **`overview_oblique`** forces LODs, like `overview_ortho`.
- **The painted trail** fades out at its ends.

New in the project, where the spec puts them: `Assets/Game/Scenes/ForestKit/Meshes/` (83 mesh assets,
37 MB), `ForestKit/Prefabs/` (28), `ForestKit/kit-report.json`, with their `.meta` files.
`rock_moss_set_01__mask.png` and `dead_tree_trunk_02.mat` were rewritten; `Forest.unity` and `ForestData/*`
were rebuilt.

### What the next engineer (M3) must know

1. **`tools/m2-iter.sh <tag> [quick|full|none] [kit]`** runs import, kit, scene and captures into
   `captures/m2-r0/iterations/<tag>`; **`tools/m2-final.sh`** is the whole final sequence;
   **`tools/m2-tests.sh <tag> <captureOut>`** is the three test runs. **`tools/contact/contact`** (Swift,
   source beside it) makes contact sheets: 30 captures can be looked at in five images.
2. **The kit refuses a mesh whose mask has no AO under its UVs.** If a fir material trips it, measure
   first (`out/M2/measurements/scripts/`), then decide between `ForestTexturePacker.UnusableAo` and a
   better map. Leaf texels cut by the alpha test are not counted.
3. **Triangles: 2.67 M of 8 M are used before the first tree** (`issues/M2-integration.md`, section 4).
   Arithmetic on the spec's tree estimate lands between the warning line and the budget. Measure after the
   first fir, not at the end.
4. **Texture memory is 738 MiB of 1,024** before the fir sets.
5. **Exposure headroom is a little better**: `trail_start` 0.413 (was 0.430) against 0.45.
6. **The weak points of the floor, in the order they show:** the litter tile from above and on the bank
   (`overview_oblique`, `boundary_*`); grass that is thin from standing height (`clearing`); ferns that are
   a sparse heath, culled at 40 m; a bare bank. None is a broken asset. The first and the last are what
   the firs are for; whether the anti-tiling terrain graph is wanted is the owner's call.
7. **`rock_close` faces a rock with an undercut** on the camera's side, in shadow. It is the scan's shape
   (`rock_moss_set_02_rock11` at scale 1.75), not a gap.
8. **Changing a number in `ForestKitSpec` rebuilds all 28 prefabs** (a 7 s launch). A changed grass or
   fern patch needs no scene code change.
9. **Decisions waiting for the owner:** the biome and this floor (review 1, with `captures/m2-r0`); grass
   at twice life size; the fern cap; the AO of rock set 01; and from M1 the draw-call estimate and Build
   Settings.

### Not done

No tree download, no tree bake, no experiment E1. The debug capture set was not run at M2. The AO of
`rock_moss_set_01` was not replaced with a fitting map. Nothing was done about the litter tile.

## 2026-10-13, review round 1 of the M2 captures (`captures/m2-r0` to `captures/m2-r1`)

**Result: rebuild, audit and all three test runs are green. The look is better on every high finding but
four of them are only partly fixed, and one (the soft hero boulder) is not fixed.** Details, evidence and
the full list of deviations: `issues/M2-review1.md`. No tree asset was fetched or built.

### What was run (final runs; `tools/m2r1-final.sh`, transcript `logs/M2r1-final-sequence.txt`)

| # | Run | Result | Evidence |
|---|---|---|---|
| 1 | `ForestCapture.Capture -captureSet debug` on the unchanged M2 scene | 18 debug images; the clay view shows the SSAO blots the reviewers described | `captures/m2-r1/iterations/r0-debug/debug/`, `logs/M2r1-00-debug-r0.log` |
| 2 | `ForestImport.Run` | Exit 0. 18 textures and 18 materials/layers up to date (built in the iterations) | `logs/M2r1-50-final-import.log` |
| 3 | `ForestPrefabs.BuildAllFromMenu -forestForce` | Exit 0. "28 prefabs built, 0 up to date, 6 skipped for missing inputs, 0 failed" | `logs/M2r1-51-final-kit.log`, `out/M2r1/kit-report.json` |
| 4 | `ForestSceneBuilder.Build` | Exit 0. "Forest audit passed: 1385 instances, 4190 GameObjects, 10 materials, 67 meshes, 0 warning(s)". 2,427 placements. Largest grounding error 5.8 mm, corridor margin 2.58 m, spawn margin 5.28 m, trail slope 5.2, core slope 25.8 degrees, `physicalCorridors: free` | `logs/M2r1-52-final-build.log`, `out/M2r1/build-report.json` |
| 5 | Inspection close-ups (temporary probe, removed again) | 118 images | `captures/m2-r1/inspect/`, `logs/M2r1-53-final-inspect.log` |
| 6 | `ForestCapture.CaptureKit` | Exit 0. 50 sheets of 28 prefabs, `passed: true` | `captures/m2-r1/kit/kit-summary.json`, `logs/M2r1-54-final-capture-kit.log` |
| 7 | `ForestCapture.Capture -captureSet full` | Exit 0. 32 images (the 30 of M2 plus `hollow_rim` and `deadfall`), `passed: true`, 0 error pixels, no pixel clipped or crushed, mean luminance 0.23 to 0.78, `trail_start` 0.405 | `captures/m2-r1/capture-summary.json`, `logs/M2r1-55-final-capture-full.log` |
| 8 | Forest EditMode suite | Exit 0. 35 tests: **33 passed, 0 failed, 2 ignored** (tree LODs: M3) | `out/M2r1/tests/forest-editmode.xml`, `logs/M2r1-56-final-forest-editmode.log` |
| 9 | Forest PlayMode suite | Exit 0. **10 passed, 0 failed** | `out/M2r1/tests/forest-playmode.xml`, `logs/M2r1-57-final-forest-playmode.log`, stills in `captures/m2-r1/playmode/` |
| 10 | Greybox PlayMode suite (`Gamex.Tests.PlayMode`) | Exit 0. **7 passed, 0 failed** | `out/M2r1/tests/greybox-playmode.xml`, `logs/M2r1-58-final-greybox-playmode.log` |

All nine final logs: 0 `error CS`, 0 `warning CS`, 0 "immutable". `DefaultHDTerrainMaterial.mat` in
`Library/PackageCache` has md5 `8ce2f7482ff8fb845c1b94b0082b4cf0`. The 59 files of
`backup/pre-forest.md5` are unchanged (`tools/check-preexisting.sh`: 0 differ, after run 10). No git
command that changes anything was run. No test file was touched (compared with `backup/M2r1-pre-edit/Tests`).

Looked at: all 32 final views as six contact sheets (`captures/m2-r1/contact-sheets/s1.png` to `s6.png`),
with `clearing`, `rock_close` and its crop, `ground_close_crop`, `trail_strip_08` and `overview_oblique`
one by one; the three play-mode stills; all 50 kit sheets and all 118 inspection images as sheets; a
before/after pair of the bank and the top-down floor at 1:1 (`work/M2r1/final-tiling-compare.png`).

Numbers from the PlayMode run: 2,734,853 triangles, 52 SetPass calls, 183 shadow casters, 855 draw calls
(estimated), 738 MiB of textures and 37 MiB of meshes above an empty scene; frame time by stopwatch
7.73 ms median, 8.36 ms p95 (budget 11).

### What was changed

Seven forest source files and the manifest; table and reasons in `issues/M2-review1.md`, diffs in
`out/M2r1/*.diff`, originals in `backup/M2r1-pre-edit/`. In short: SSAO 1.0 at 1 m; fog 60 m; the ground
tinted darker, cooler and damp, litter tiled at 2.0 m, the height blend sharpened; ferns in real drifts
with a full hollow; grass as leafy tussocks in swards; logs and rock set 01 colour-matched; branches cast
shadows; rocks bedded on convex ground; the outcrop piled, the deadfall varied; the paint rules for bank,
gully, hollow, ruts and clearing; two new viewpoints.

### What the next engineer must know

1. **`tools/m2r1-iter.sh <tag> [quick|full|none] [kit]`**, **`tools/m2r1-final.sh`**, **`tools/m2r1-tests.sh`**
   and **`tools/m2r1-sheets.sh <capture folder>`** are this round's copies of the M2 scripts; they write to
   `captures/m2-r1` and `out/M2r1`. **`tools/inspect/run-look.sh`** renders any pose three ways (as is,
   without ferns and grass, without the boulders near the camera).
2. **Do not put the text `Unity.app/Contents/MacOS/Unity` on the same command line as `unity.sh`**: its own
   `pgrep` then finds the calling shell and refuses to launch ("EDITOR OPEN"). It happened twice.
3. **The package-cache file was rewritten once more and put back** (first in-place rebuild of the terrain
   material); the cause is fixed in `ForestMaterials.BuildTerrainMaterial`. Keep checking the md5.
4. **Changing a terrain layer's tint, smoothness, normal scale or tile rebuilds that layer on the next
   import**; changing `ToolVersion` in `ForestMaterials` rebuilds all 18.
5. **The fern solver now logs what it chose** ("fern field thinned to 0.80, drift threshold raised by
   0.29"), and the grass solver logs how many patches fit its swards against its cap.
6. **Spec deviations of this round are listed in `issues/M2-review1.md`, section 2.** The larger ones: SSAO
   and fog values, every terrain tint, the litter tile, grass at 2.3 to 2.9 times scan size on 15 % of the
   clearing, the fern LOD distances, the extra rock sink.
7. **Waiting for the owner**, in addition to the M2 list: the anti-tiling terrain graph (the tile still
   shows from above), detail maps on rock (the hero boulder is soft at 1 m), the fern cap, a fitting AO
   map for rock set 01.
8. **Still weak in the pictures:** the clearing's grass; the bare bowl of the bank on every horizon; the
   outcrop does not hide the bank; the dry-needle tile from above.

### Not done

No tree download, bake or experiment E1. Of the reviewers' medium and low findings: the streaks on the
bank, the bank's shape, crouch cover along the trail, cluster spacing, rut width, shoulder grass, a third
fern level, the AO map of rock set 01 (`issues/M2-review1.md`, section 4).

## 2026-10-14, review round 2 of the M2 captures (`captures/m2-r1` to `captures/m2-r2`)

**Result: rebuild, audit and all three test runs are green.** All nine critical and high findings were judged
real (none rejected). The mist now fills the bowl; rocks are grey stone seated in soil; the hero boulder's dark
band is gone; clusters are one mass; the clearing's lattice is gone. Two are improved, not finished: the fern
drifts outside the hollow, and the clearing's grass. Details, numbers and every deviation:
`issues/M2-review2.md`. No tree asset was fetched or built.

### What was run (final runs; `tools/m2r2-final.sh`, transcript `logs/M2r2-final-sequence.txt`)

| # | Run | Result | Evidence |
|---|---|---|---|
| 1 | `ForestImport.Run` | Exit 0. 18 textures and 18 materials/layers up to date (built in the iterations) | `logs/M2r2-50-final-import.log` |
| 2 | `ForestPrefabs.BuildAllFromMenu -forestForce` | Exit 0. "28 prefabs built, 0 up to date, 6 skipped for missing inputs, 0 failed" | `logs/M2r2-51-final-kit.log`, `out/M2r2/kit-report.json` |
| 3 | `ForestSceneBuilder.Build` | Exit 0. "Forest audit passed: 1642 instances, 4751 GameObjects, 10 materials, 67 meshes, 0 warning(s)". 2,684 placements. Grounding error 7.1 mm, trail slope 4.9, core slope 25.8 degrees, corridor margin 2.54 m, spawn margin 5.28 m, `physicalCorridors: free` | `logs/M2r2-52-final-build.log`, `out/M2r2/build-report.json` |
| 4 | Inspection close-ups (temporary probe, removed again) | 118 images | `captures/m2-r2/inspect/`, `logs/M2r2-53-final-inspect.log` |
| 5 | `ForestCapture.CaptureKit` | Exit 0. 50 sheets of 28 prefabs, `passed: true` | `captures/m2-r2/kit/kit-summary.json` |
| 6 | `ForestCapture.Capture -captureSet full` | Exit 0. 32 images, `passed: true`, 0 error pixels, none clipped or crushed, mean luminance 0.23 to 0.72, `trail_start` 0.372 | `captures/m2-r2/capture-summary.json`, `logs/M2r2-55-final-capture-full.log` |
| 7 | Forest EditMode suite | Exit 0. 35 tests: **33 passed, 0 failed, 2 ignored** (tree LODs: M3) | `out/M2r2/tests/forest-editmode.xml`, `logs/M2r2-56-final-forest-editmode.log` |
| 8 | Forest PlayMode suite | Exit 0. **10 passed, 0 failed** | `out/M2r2/tests/forest-playmode.xml`, `logs/M2r2-57-final-forest-playmode.log`, stills in `captures/m2-r2/playmode/` |
| 9 | Greybox PlayMode suite (`Gamex.Tests.PlayMode`) | Exit 0. **7 passed, 0 failed** | `out/M2r2/tests/greybox-playmode.xml`, `logs/M2r2-58-final-greybox-playmode.log` |

All nine final logs have 0 `error CS`, 0 `warning CS` and 0 "immutable". `DefaultHDTerrainMaterial.mat` in
`Library/PackageCache` has md5 `8ce2f7482ff8fb845c1b94b0082b4cf0`. The 59 files of `backup/pre-forest.md5` are
unchanged (`tools/check-preexisting.sh`: 0 differ, after run 9). Both test folders are identical to
`backup/M2r2-pre-edit/Tests`. No git command that changes anything was run.

Looked at: all 32 final views as six contact sheets (`captures/m2-r2/contact-sheets/s1.png` to `s6.png`), with
`rock_close` and its crop, `trail_start`, `clearing` and `boundary_s` also at full size (`work/M2r2/`); the three
play-mode stills (`contact-sheets/playmode.png`); all 50 kit sheets (`kit-tt-1`, `kit-tt-2`, `kit-lods`); all 118
inspection images (`insp-1` to `insp-5`). No black, white or magenta frame.

Play mode, `trail_start`: 3.82 M triangles (was 2.73 M), 52 SetPass calls, 237 shadow casters, 1,049 draw calls
(estimated), 738 MiB of textures and 36.7 MiB of meshes above an empty scene. Frame time by stopwatch 8.07 ms
median, 8.67 ms p95 (budget 11).

### What was changed

Seven forest sources and the manifest. Table and reasons in `issues/M2-review2.md`, diffs in `out/M2r2/*.diff`,
originals in `backup/M2r2-pre-edit/`.

- **Lighting:** fog base 18 m, maximum 100 m, mean free path 70 m; white balance -5; fog tint; micro-shadowing 0.5.
- **Materials:** AO remap (rocks 0.4, wood 0.5); rock set 01 nearer neutral; branches lighter; mossy ground an olive
  green; wetter mud, gravel and humus.
- **Tiles and paint:** eight pairwise-different tile sizes; the clearing and the bank painted as mixes; moss patches
  narrower; humus skirts and moss rings around rocks; mounds smaller, two-scale and masked.
- **Soil collars:** at step 8 the ground is raised under each rock's measured lip, and its sink grows by the same
  amount so no rock moves.
- **Clusters:** each one is an anchor with touching rocks and downhill satellites.
- **Ferns:** denser drifts with jittered spacing and a size hierarchy; near level to 24 m.
- **Grass:** spacing follows the swards; scale 0.6 to 1.6; cull at 35 m.
- **Loose fern clumps:** 81 against large rocks, 120 on the foot of the bank, 30 on the clearing's rim.
- **Branches:** settled into the litter.
- **Viewpoints:** `rock_close` at 2.8 m; `clearing` turned toward its south-west stump patch.

### What the next engineer must know

1. **Scripts:** `tools/m2r2-iter.sh <tag> [quick|full|none] [kit]`, `tools/m2r2-final.sh` (build and captures only;
   the tests follow as three separate `unity.sh` runs, see the table above), `tools/m2r2-tests.sh`,
   `tools/inspect/run-inspect-r2.sh`, `tools/inspect/run-look.sh` (now logs as `M2r2-*`).
2. **New tools:**
   - `tools/regionstats/regionstats <png> x0 y0 x1 y1 ...` measures median luma per box.
   - `tools/measure/rock_lip.py` measures each rock's rim and lip per bearing from the FBX; its table is
     `ForestLayout.RockLips`.
   - `tools/measure/plan_stats.py <placements.json>` prints fern and grass spacing.
3. **Triangle headroom before the trees is smaller:** 3.82 M of 8 M (warning line 6.5 M), against 2.73 M after round 1.
   The fern near level at 24 m and the rock scan level at 20 m are the first knobs to take back (section 10, knob 5).
   Measure after the first fir.
4. **Rock collars raise the terrain at step 8.** `AddRootMounds` now also changes boulder `sink` values, so placements
   change after step 8. Each build logs "rock collars (N lowered ...)" and the hero rock's collar. A collar is lowered
   when it would steepen the core; the audit's slope check is unchanged. Collars cost cluster rocks visible height
   (`issues/M2-review2.md`, section 2).
5. **Exposure compensation does nothing on open ground** while the meter (11.0 EV) is above `limitMax` (10.5).
6. **The overview captures now look through the mist.** The layout is harder to read from above.
7. **Waiting for the owner**, in addition to the earlier list:
   - the anti-tiling graph;
   - detail maps on rock (the hero is still soft at 1 m);
   - grass count and fern cap (coverage under spec 7.2's 25 %, drifts not carpets);
   - the hero's peak (about 1.7 m; spec 3.4 asks for 1.8 to 2.0);
   - the deviations listed in `issues/M2-review2.md`, section 2.
8. **Still weak in the pictures:**
   - the clearing's grass, thin from standing height;
   - fern drifts that read as groups, not carpets;
   - faint streaks fanning out on the ground in `boundary_s`;
   - the bare bank (the fir wall is M3's job);
   - the soft hero boulder at 1 m.

### Not done

No tree download, bake or experiment E1. The medium and low findings not taken up are listed with reasons in
`issues/M2-review2.md`, section 4. They include:
- the SSAO and fog A/B for the streaks and curtains;
- sky rotation;
- ruts;
- `SpawnYaw`;
- trail-side cover;
- the outcrop arc;
- the bank's shape;
- the stump arc;
- deadfall spacing;
- log pitch;
- rock set 01's AO map;
- a grading shadow lift.

## 2026-10-14, independent verifier: M0 to M2 (run2)

**Result: every gate I could run is green, and I saw the evidence myself.** Nothing in the project was fixed or
edited. The only project files that changed are the forest scene and the two `ForestData/Lighting` profiles that
`ForestBuild.BuildAll` regenerates on each run; their content is identical to a clean build apart from object IDs.
Logs and results: `logs/verify/run2/`. Images: `captures/verify/run2/`. Contact sheets: `work/verify2-sheets/`.
The files directly in `logs/verify/` (V01 to V03) and directly in `captures/verify/` come from an earlier, unfinished
verify attempt. I did not use them.

### What was run (`logs/verify/run2/vrun2.sh`: refuses to start while any editor runs, never `-nographics`)

| # | Run | Result | Evidence |
|---|---|---|---|
| 0 | `fetch_assets.py --milestone M2` on a clean copy's manifest | Exit 0: 68 files in place, size and md5 OK; 8 models measured, all MATCH; manifest unchanged | `R00-fetch-M2-cleancopy.log` |
| 1 | `ForestBuild.BuildAll` on a **clean copy** (`work/verify2-clean/gamex`: no Library, no ForestKit/ForestData/Forest.unity) | Exit 0 in 93 s, 0 `error CS`. "28 prefabs built ... 0 failed"; "Forest audit passed: 1642 instances, 4751 GameObjects, 10 materials, 67 meshes, 0 warning(s)". Four FXC crashes on HDRP-internal shaders during the fresh import, all restarted by Unity, with no shader errors | `R01-clean-copy-buildall.log` |
| 2 | `ForestBuild.BuildAll` on the project (the 11.5 command) | Exit 0, 0 `error CS`, 0 immutable; same audit line | `R02-project-buildall.log`, `project-build-report.after-R02.json` |
| 3 | `ForestCapture.Capture -captureSet full` | Exit 0. 32 views and 32 crops, `passed: true`, 0 magenta, 0 cyan, nothing clipped or crushed, mean luminance 0.227 to 0.721 | `captures/verify/run2/` |
| 4 | Forest EditMode suite | 35 tests: 33 passed, 0 failed, 2 ignored (`TreeLods_*`: no tree before M3) | `R04-forest-editmode.xml` |
| 5 | Forest PlayMode suite | 10 of 10 passed | `R05-forest-playmode.xml` |
| 6 | Greybox suite `Gamex.Tests.PlayMode` | 7 of 7 passed (the seven `StealthLoopTests`) | `R06-greybox-playmode.xml` |
| 7 | Forest PlayMode again with `-captureOut`, to keep the stills (Temp is wiped on exit) | 10 of 10 | `R07-*.xml`, `captures/verify/run2/playmode/` |
| 8, 9 | `CaptureKit`; `Capture -captureSet debug` | 50 kit sheets and 18 debug images, `passed: true` | `captures/verify/run2/kit`, `debugset` |
| 10, 11 | `ForestProbe.Run` and a greybox capture (`-captureScene`), both **in the clean copy**, because the probe writes `Assets/ForestProbeTemp` and re-imports assets | All 13 probe answers match the project's `Library/GamexForest/probe.json`; the greybox renders through the harness | `captures/verify/run2/probe-cleancopy`, `greybox-cleancopy` |

I looked at every image: the 32 views, the 32 crops, 3 play-mode stills, 50 kit sheets, 18 debug views, 3 probe
images and 3 greybox images. There is no black, white or magenta frame. `trunk_close`, `fir_close` and `canopy_up`
have no subject until M3 and M4. The overviews are mostly mist. The grey side bars in `overview_ortho` show where the
frame (about 284 m wide) runs past the 256 m tile.

### Regression check

- The 59 files of `backup/pre-forest.md5` match, before and after every run.
- The 108 files of `pre-forest.tgz` are byte-identical: they include the `.meta` files, `HDRPDefaultResources`, both
  asmdefs, `EditorBuildSettings.asset` and `packages-lock.json`.
- No test or capture run changed any project file (`snap-before-tests.md5` against `snap-after-all.md5`).
- `DefaultHDTerrainMaterial.mat` in the PackageCache still has md5 `8ce2f748...`.

### Determinism (clean copy against project)

- `placements.json`, `SurfaceMap.png`, all 83 meshes and all 18 packed textures are byte-identical.
- Prefabs and materials differ only in Unity's random fileIDs and GUIDs.
- The build reports differ only in one info finding in the clean copy: the edit-mode corridor sweep is skipped
  without the probe record in Library, as spec 12.1 allows.

### Budgets (play mode, 2560 x 1440, worst of four views)

| Metric | Value | Budget | How it was obtained |
|---|---|---|---|
| Triangles | 3.82 M | 8 M | Measured |
| Vertices | 2.68 M | 10 M | Measured |
| SetPass calls | 52 | 250 | Measured |
| Shadow casters | 237 | 2,500 | Measured |
| Texture memory | 738 MiB | 1,024 MiB (warning at 819) | Measured |
| Mesh memory | 36.7 MiB | 300 MiB | Measured |
| Draw calls | 1,049 | 4,000 | **Estimated**: the engine counter reads 0 |
| Frame time, median | 8.09 ms (p95 8.83) | 11 ms | **Stopwatch fallback** (P5) |
| Scene totals | 1,642 instances, 4,751 GameObjects, 10 materials, 67 meshes, 4 textures at 4096 | Section 10 | From the audit |

On the frame time: `FrameTiming` reports only 7 of 240 frames, at a median of 25.6 ms. That has been the case since
M1 and nobody has explained it.

### What the next engineer must know

1. Spec 11.5 step 12 (Build Settings) is still not applied, by the M1 decision; the owner has to decide.
2. Many tuned values differ from sections 8.2, 8.3, 5.6.4 and 6.3. I confirmed these in code:
   - fog base 18 m, maximum 100 m, mean free path 70 m;
   - micro-shadowing 0.5;
   - rock LOD0 to 20 m;
   - fern patches 24/50 m;
   - grass cull 35 m.
   Every one is recorded in `issues/M2-review*.md`.
3. `work/verify2-clean/` (2.6 GB) is a throwaway copy and can be deleted.

## 2026-10-14, M3 first half: experiment E1 and the young fir prefabs

**Result: E1 is decided (arm C, the untouched scan to 6 m), the three young-fir prefabs build, the forest EditMode suite
is 35 of 35 (no test ignored any more), greybox 7 of 7. The forest PlayMode suite is 9 of 10: `Budget_Counts` fails on
triangles.** Frame time is inside the 11 ms budget by 0.08 ms at the worst view. The tree placement of M3 (thickets,
bank wall) was not done. Details: `issues/M3-E1.md` (the experiment, its numbers and pictures) and
`issues/M3-young-firs.md` (deviations, the UV problem, tests).

### What was found and done

1. **The sapling FBX has no UV layer** (curves converted by Blender: positions, normals, material indices only).
   `fbx_objects.py` matched the manifest on everything it compares and set `objectsSource` to "fbx". The kit now takes
   positions, normals and UVs from Poly Haven's glTF geometry of the same model (`.gltf` + `.bin` only, in `Source~`,
   added to the manifest from the `/files` API), bakes the bark's texture transforms into the UVs, mirrors it as the
   importer mirrored the FBX, and refuses it unless triangles, area and centre per material equal the imported FBX's.
2. **E1** (all three variants per arm, the existing planner's 771 young firs, measured in play mode at 2560 x 1440 by a
   temporary test that was removed again):

   | Arm | `fir_close` | `crouch_hide` | `trail_start` | Look at `fir_close` |
   |---|---|---|---|---|
   | A (table) | 6.18 M, 9.35 ms | 8.90 M, 9.71 ms | 8.25 M, 9.92 ms | Bamboo blades: fails |
   | B (150,000) | 9.60 M, 9.93 ms | 13.84 M, 10.34 ms | 11.67 M, 10.35 ms | Coarse loose needles: fails |
   | C (scan to 6 m) | 8.10 M, 9.78 ms | 12.98 M, 10.39 ms | 8.25 M, 9.76 ms | The reference |
   | Final (C, retuned levels) | 7.69 M, 10.22 ms | 12.48 M, 10.92 ms (p95 11.87) | 7.81 M, 10.17 ms | Young firs at 1 to 3 m |

3. **The levels behind the scan were retuned** so the tree does not turn into bamboo at 6 m or straps at 42 m: every
   thinned level is flattened (a needle card becomes a quad), stems become sticks instead of planks, the far level's
   enlargement is capped at 6, and the first thinned level is 52,000 (`a`: 61,000) twig triangles to hold the frame
   budget. Coverage 0.76 to 1.12, all inside the gate.
4. **Budgets re-based** as spec section 10 asks: triangles 11.6 M, vertices 19.0 M, mesh memory 400 MiB (plus what arm C
   measured over arm A), frame time unchanged, in `ForestBudgets` and its PlayMode copy together.

### What was run (final; `tools/m3a-final-1.sh`, `tools/m3a-final-2.sh`, transcripts `logs/M3a-final-sequence-1.txt`, `-2.txt`)

| # | Run | Result | Evidence |
|---|---|---|---|
| 1 | `fetch_assets.py --id fir_sapling_medium` (twice: FBX and maps, then the glTF pair) | 10 files, md5 OK, MATCH | `logs/M3a-01-fetch.log`, `M3a-02-fetch-gltf.log` |
| 2 | `ForestImport.Run` | Exit 0 | `logs/M3a-50-final-import.log` |
| 3 | `ForestPrefabs.BuildAllFromMenu -forestForce` | "31 prefabs built ... 0 failed"; young firs from the glTF geometry | `logs/M3a-51-final-kit.log`, `out/M3a/kit-report.json` |
| 4 | `ForestSceneBuilder.Build` | Audit passed: 2,413 instances, 9,378 GameObjects, 13 materials, 79 meshes | `logs/M3a-60-final-build.log` |
| 5 | `CaptureKit` | 80 sheets, `passed: true` | `captures/m3a/final/kit/` |
| 6 | `Capture -captureSet full` | 32 views, `passed: true`, 0 error pixels | `captures/m3a/final/treescene/` |
| 7 | E1 measurement | Table above | `captures/m3a/final/treescene/playmode/e1-measure-final.txt` |
| 8 | Forest EditMode | 35 passed, 0 failed, 0 ignored | `out/M3a/tests/forest-editmode.xml` |
| 9 | Forest PlayMode | 9 passed, 1 failed (`Budget_Counts`: 12,482,801 triangles at `crouch_hide`, budget 11,600,000) | `out/M3a/tests/forest-playmode.xml` |
| 10 | Greybox PlayMode | 7 passed | `out/M3a/tests/greybox-playmode.xml` |

All final logs: 0 `error CS`, 0 `warning CS`, 0 "immutable". PackageCache md5 unchanged. 59 pre-existing files: 0 differ.
No git command that changes anything was run. No EditMode test file was changed; the one PlayMode change is the budget
copy in `ForestPlayBudgets.cs` (rule 5).

Looked at: every E1 arm's `fir_close` (full and crop), the comparison sheets, all 32 views of six full sets (A, B, C6,
C6t, C6f, C6final, as contact sheets) and of the final set, all 48 play-mode measurement stills, the final quick set and
play-mode stills, the young-fir kit sheets of every tuning step that was judged (`captures/m3a/tune/t1` to `t7`) and all
final young-fir sheets, the 28 prop turntables and 22 prop LOD sheets of the final kit. No black, white, magenta or
tinted frame. Checked by their statistics only (`kit-summary.json`: 0 magenta, 0 cyan, luminance 0.233 to 0.466), not
looked at one by one: the prop sheets of the intermediate kit runs (the props are byte-identical), the young-fir sheets
of `tune/t8` (superseded by run K8) and of the arm runs C6t, C6f and C6final.

### What was changed

Project: `ForestPrefabs.cs` (tree source with the glTF fallback), `ForestTreeBaker.cs`, `ForestFoliageThinner.cs`
(stems as sticks), `ForestKitSpec.cs` (tree table, scan distance, flatten rule, `kit-4`), `ForestCapture.cs` (source
scan, switch-distance sheets, close turntable), `ForestBudgets.cs` and `Tests/ForestPlayMode/ForestPlayBudgets.cs`
(re-based budgets), `forest-asset-manifest.json` (measured flag, the glTF pair), `THIRD_PARTY_ASSETS.md`. New:
`Assets/ThirdParty/PolyHaven/fir_sapling_medium/` (161 MB), the young-fir textures, materials, 12 meshes and 3 prefabs in
`ForestKit`; `Forest.unity` and `ForestData` rebuilt. Diffs `out/M3a/*.diff`, originals `backup/M3a-pre-edit/`.

### What the next engineer must know

1. **Scripts:** `tools/e1/e1-arm.sh <tag>` (kit, scene, captures, measurement), `tools/e1/e1-cost.sh <tag>` (kit, scene,
   measurement), `tools/e1/kit-iter.sh <tag>` (kit and sheets), `tools/e1/run-e1-measure.sh <tag> <out>` (the
   temporary play-mode measurement of `fir_close`, `crouch_hide`, `trail_start`; it removes itself). A measurement run
   takes 14 s; it repeats to about 0.05 ms.
2. **Frame-time headroom is 0.08 ms at `crouch_hide`** before any tall fir. Try knob 1 of spec section 10 (decals off
   per camera) before M4, and measure with `run-e1-measure.sh`.
3. **`Budget_Counts` fails on triangles** by 0.88 M; arm A was over the spec's budget by the same amount (8.90 M against
   8.0 M), so it is the scene's tree count and floor, not E1. Placing the trees is where it gets fixed.
4. **The young firs stand where the existing planner put them** (771). The kit sheets now include the hand-over
   distances (`lods_06m`, `_14m`, `_42m`) and `close_turntable`.
5. **For M4:** `fir_tree_01.fbx` is from the same exporter; if `fbx_objects.py` prints `UV layers 0`, add its glTF pair to
   the manifest the same way. The kit build now throws on a tree without UVs instead of building it untextured.
6. **The mesh assets are large** as text: 91, 56 and 57 MB for the three scans; `ForestKit` is 408 MB.

## 2026-10-14, M3 second half: young-fir placement, budgets, review-2 capture set (`captures/m3-r0`)

**Result: the M3 gate is passed.** The scene builds with 771 young firs in thickets and a wall on the bank, the audit
passes (0 errors, 0 warnings), forest EditMode 35 of 35 (nothing ignored, both tree-LOD tests run), forest PlayMode 10 of
10 (`Budget_Counts` now passes: 9.53 M triangles at crouch_hide against 11.6 M), greybox 7 of 7. The review-2 set is
`captures/m3-r0` (32 views and crops, 80 kit sheets, 18 debug images, 3 play-mode stills), all looked at. Details,
numbers and deviations: `issues/M3-placement.md`.

### What was changed (diffs `out/M3b/*.diff`, originals `backup/M3b-pre-edit/`)

- `ForestLayout.cs`, `ForestScatter.cs`: young-fir spacing on a steep ramp of the thicket field (3.0 m in thickets,
  8.5 m on open ground, little jitter inside thickets); the bank band as a wall (3.0 m on its foot to 7 m out, 8 m at
  20 m). 360 core + 411 bank. Thicket share (3+ trunks within 5 m) 22.0 % of the core, was 19.3 %; core nearest
  neighbour p50 3.74 m, was 4.67; bank p50 3.71 m, was 4.51.
- `ForestLighting.cs`: knob 1 of spec section 10, decals off on the forest camera only.
- `ForestLightingPresets.cs`: knob 2, shadows 50 m and three cascades (splits 0.12 / 0.35).
- `ForestFoliageThinner.cs`, `ForestKitSpec.cs` (kit-5): the stick that replaces a twig stem is as wide as the stem
  (radius from area / (2 pi length)); the tan bars in the near firs of trail_start are gone.
- No test file, no pre-existing file, no project setting changed.

### What was run (final: `tools/m3b/final.sh`, transcript `logs/M3b-final-sequence.txt`)

| # | Run | Result |
|---|---|---|
| 1 | Import; kit forced; scene | Exit 0; 31 prefabs, 0 failed; audit passed: 2,383 instances, 9,285 GameObjects, 13 materials, 79 meshes; grounding 8.6 mm, corridor margin 2.58 m, spawn margin 5.55 m, physical corridors free |
| 2 | `CaptureKit`, `Capture full`, `Capture debug` | 80 sheets, 32 views (`passed: true`, 0 error pixels), 18 debug images |
| 3 | Cost measurement, 29 views x {knobs on, knob 1 off, both off} (temporary test, removed) | `captures/m3-r0/measure/playmode/m3b-measure-final.txt` |
| 4 | Forest EditMode / forest PlayMode / greybox PlayMode | 35/35, 10/10, 7/7 (`out/M3b/tests/*.xml`) |

All final logs: 0 `error CS`, 0 `warning CS`, 0 "immutable". PackageCache md5 `8ce2f7482ff8fb845c1b94b0082b4cf0`.
59 pre-existing files: 0 differ. No git command that changes anything was run.

Budgets (play mode, 2560 x 1440, worst of the four `Budget_Counts` views): triangles 9.53 M (measured; budget 11.6 M,
spec 8.0 M), vertices 12.27 M (19.0 M), SetPass 45 (250), shadow casters 424 (2,500), texture memory 802.2 MiB (1,024),
mesh memory 273.4 MiB (400), draw calls 2,060 (**estimated**, counter reads 0), frame time median 9.24 / 10.08 / 8.47 /
8.90 ms at trail_start / crouch_hide / clearing / long_sightline (**stopwatch**, an upper bound; the test run read
10.34 ms at trail_start two minutes later). Of 29 views measured, only **deadfall** is over: 13.45 M, 11.14 ms.

### What the next engineer must know

1. **Scripts:** `tools/m3b/iter.sh <tag> [quick|full|none]` (scene, placement map and stats, captures into
   `captures/m3-r0/iterations/<tag>`), `tools/m3b/kit-iter.sh <tag>`, `tools/m3b/run-measure.sh <tag> <abs out> <configs>
   <views> [stills]` (configs: all, noYoung, spawnOnly, noSpawn, noBank, noYoungShadow, noKnob1, noKnob2, noKnobs,
   decalsOff, shadow50, knob3, knob12, knob123, lod0ShadowL2; `CENSUS_ALL=1` lists every renderer class the frame
   touched), `tools/m3b/sheets.sh <folder>`, `tools/m3b/final.sh`, `work/M3b/scripts/firs.py` (young-fir statistics and map).
2. **Pass absolute paths to `-captureOut`.** A relative one lands in the project root (it happened once; cleaned up).
3. **Headroom for M4 is small:** about 0.9 ms at crouch_hide, 0.2 (pano_90) to 2.5 ms (clearing) at the other views, by stopwatch. Knobs 1 and 2 are spent.
   The scan level (arm C) is the largest cost wherever three or more young firs stand within about 5 m of the camera;
   young-fir shadows were a third of crouch_hide's triangles before knob 2.
4. **Open:** the deadfall view over budget; thicket share 22 % against the spec's ~30 %; bare litter between stands;
   the spec's original 8.0 M triangle budget is still exceeded (the re-based 11.6 M is met).
5. Frame-time readings drift by up to 1 ms within one run; judge triangle counts as exact and milliseconds as approximate.

## 2026-10-14, review round 1 of the M3 captures (`captures/m3-r0` to `captures/m3-r1`)

**Result: rebuild, audit and all three test runs are green** (forest EditMode 35 of 35, forest PlayMode 10 of 10, greybox 7 of
7; audit passed with 0 errors and 0 warnings). All nine high findings were judged real; they are three problems plus the frame
budget. Details, evidence and every deviation: `issues/M3-review1.md`. No tall fir was fetched or built.

### What changed in the picture

- **Young firs no longer turn into bamboo at 14 m.** The reviewers' fallbacks (grouped thinning, scale caps, crown core) were
  built and failed: the core of this open sapling is an opaque olive spindle (`captures/m3-r1/iterations/k1/kit/`). Instead the
  14 to 42 m and 42 to 85 m levels are **twig cards**: needles grouped into cells, one quad per cell in the plane of its needles,
  mapped to one of the seven twig sprays the scan's atlas carries but its mesh never uses. Same triangle budgets, coverage 1.08
  to 1.14 (gate unchanged). The sprays' AO is brought to the needles' mean in the packed mask so the cards match the needle
  level's tone. Stems keep their size and stay inside their own piece.
- **Stands instead of an orchard**: clumps and glades on open ground, denser and less regular thickets, a gapped bank wall,
  scale 0.55 to 1.30 with small firs at edges and 30 % understorey in dense stands, lean to 3 degrees, two understorey firs in
  the spawn thicket. 795 young firs, 29.3 % thicket share (was 22 %). Humus under every crown; moss and dry needles cut there.
- **Cost**: knob 5 (fern near level 24 to 16 m), variant `a` 30 to 15 % and never understorey, `a`'s scan to 4.5 m, the scan's
  shadow drawn by the card level. All 29 views under 11 ms (median of three passes; worst deadfall 10.87, was 11.14 and 13.45 M
  triangles, now 11.21 M).
- Twig smoothness remap 0.4, an olive twig tint variant, `-captureBaseline` for the RMSE sidecar field.

### What was run (final: `tools/m3r1/final.sh`, transcript `logs/M3r1-final-sequence.txt`)

| # | Run | Result |
|---|---|---|
| 1 | Import; kit forced; scene | Exit 0; 31 prefabs, 0 failed; audit passed: 2,438 instances, 9,512 GameObjects, 13 materials, 79 meshes |
| 2 | `CaptureKit`, `Capture full` (RMSE against m3-r0), `Capture debug` | 80 sheets, 32 views (`passed: true`, 0 error pixels, luminance 0.209 to 0.678), 18 debug images |
| 3 | Cost, 29 views x 3 passes (temporary test, removed) | `captures/m3-r1/measure/playmode/m3b-measure-r1-final.txt` |
| 4 | Forest EditMode / forest PlayMode / greybox PlayMode | 35/35, 10/10, 7/7 (`out/M3r1/tests/*.xml`) |

All final logs: 0 `error CS`, 0 `warning CS`, 0 "immutable". PackageCache md5 `8ce2f7482ff8fb845c1b94b0082b4cf0`. 59
pre-existing files: 0 differ. No test file changed. No git command that changes anything was run. A first final run (with a fern
term that was then removed) is in `logs/M3r1-final-sequence-run1.txt`; it was also all green.

Looked at: all 32 views and crops (`captures/m3-r1/contact-sheets/s1` to `s6`, `crops1`, `crops2`, `trail_start`, `crouch_hide`,
`pano_270` at full size in the iterations), the 80 kit sheets (`kit-young-1` to `3`, `kit-props-1`, `2`), the 18 debug images and
the 3 play-mode stills. No black, white, magenta or tinted frame.

Budgets (`Budget_Counts`, worst of four views): triangles 10.63 M (11.6 M), vertices 13.36 M (19.0 M), SetPass 45, shadow casters
479, draw calls 2,118 (estimated), texture memory 802.2 MiB, mesh memory 273.3 MiB. `Budget_FrameTime` 10.01 ms (stopwatch).

### What changed in the code (diffs `out/M3r1/*.diff`, originals `backup/M3r1-pre-edit/`)

`ForestFoliageThinner.cs` (twig cards, tube pieces, stick clamp, card-only area balance), `ForestKitSpec.cs` (sprite table, card
switches, `StickMaxScale`, `ScanShadowThinnedLevel`, `a` scan 4.5 m, fern near 16 m, `kit-7`), `ForestTreeBaker.cs`,
`ForestTexturePacker.cs` (`MatchSpriteAo`), `ForestMaterials.cs` (smoothness remap, tint colour), `ForestLayout.cs` and
`ForestScatter.cs` (young-fir placement, understorey, lean, spawn understorey, viewpoint gap), `ForestTerrain.cs` (humus and crown
mask under young firs), `ForestCapture.cs` (`-captureBaseline`).

### What the next engineer (and the owner at review 2) must know

1. **Scripts**: `tools/m3r1/kit-iter.sh <tag>` (forced kit and sheets), `tools/m3r1/iter.sh <tag> [quick|full|none]`,
   `tools/m3r1/final.sh`; the measurement is still `tools/m3b/run-measure.sh`. `work/M3r1/scripts/sprites.py` measures the twig
   sprites of an atlas; `sprite_colour.py` compares their colour and AO.
2. **For M4**: `fir_tree_01_twig` has no sprite table yet, so the tall firs would be thinned as needles (the bamboo of round 0).
   Measure its atlas with `sprites.py`, add a table and a needle box to `ForestKitSpec.TwigSprites` / `TwigNeedleBox`, and the
   tall firs get cards from their second level on; or decide otherwise with the owner.
3. **Headroom is still small**: 0.13 ms at deadfall, 0.6 to 0.9 ms at five other views (stopwatch, 0.3 to 1 ms noise). Knob 3
   (measured 0.1 to 0.35 ms) and knob 4 for the tall firs are next. Views beside dense stands became dearer.
4. **The fern patch cap (900) is full.** Any new fern demand (thicket edges, the bank) takes ferns from the open-ground drifts;
   tried and removed this round.
5. **Not done, for the owner**: the probe bake (M5) is the real fix for the flat light inside thickets; adding deadfall and
   pano_90 to `Budget_Counts` (a test change rule 5 does not cover); the 85 m trunk ribbon; dry branches in thickets and on the
   bank; the outcrop and deadfall legibility findings.

## 2026-10-14, M4 first half: the tall fir prefabs (`captures/m4a`)

**Result: the three tall-fir prefabs build (34 prefabs, 0 failed) and the forest EditMode suite is 35 of 35, nothing
ignored.** Greybox 7 of 7. The forest PlayMode suite is 9 of 10: `Budget_Counts` fails at crouch_hide (14.29 M triangles
against 11.6 M, 19.87 M vertices against 19.0 M), because the unchanged planner's 271 tall firs are now instanced; tall-fir
placement is the second half of M4 and was not touched. `Budget_FrameTime` passed (10.66 ms median, p95 11.37, stopwatch).
Details, measurements, pictures and deviations: `issues/M4-tall-firs.md`.

### What was found and done

1. **Fetch and measure (risk 22):** `fir_tree_01` fetched (348.6 MB, md5 OK); `fbx_objects.py` MATCH on all three objects,
   `objectsSource` set to "fbx", no `--write` needed. The FBX has **no UV layer**, like the sapling's: its glTF pair
   (`Source~/fir_tree_01.gltf` + 478.5 MB `.bin`, from the `/files` API) was added to the manifest and fetched.
2. **Import alone (risk 2):** textures first with the FBX held outside the project, then the FBX as the only asset of a run:
   imported in 1.62 s, peak resident memory 1.92 GB (`logs/M4a-11-import-fbx.log`, `.mem.tsv`). The kit build peaks at 5.3 GB.
3. **trunk_c (risk 23): case 2, automatically**: no UVs in the FBX nor in the glTF (no `TEXCOORD_0`), so the triplanar
   `fir_tree_01_bark`; kit report `triplanarMaterials: ["fir_tree_01_trunk_c"]`. The glTF reader now accepts a UV-less
   primitive only for a material with a `noUvFallback`. Looked at: bark without seams, darker than the scanned trunks.
4. **Scan UV defects of the trunks:** trunk_a maps its trunk above 3 m to a strip that crosses unbaked AO (the M2 kit gate
   refused it): unbaked AO texels take the baked mean (`ForestTexturePacker.UnbakedAoFilled`). trunk_b tiles its whole
   trunk above 1.8 m across the atlas (a smeared band at 2.5 to 3.5 m): those triangles take the triplanar bark
   (`ForestPrefabs.SplitOffAtlas`). The same for trunk_a was tried and taken back (darker trunks with a seam).
5. **Levels:** no scan level (spec); knob 4 on LOD0 (x 0.7, `TallFirLod0Factor`); every level flattened needles; twig cards
   tried and rejected (they read as a broadleaf tree at every distance, like the young firs' cards in review 2); far
   level cap 12 instead of 20; far level wood is the trunk only. LOD0: a 222,700 triangles (needles 3.71x), b 205,328
   (2.69x), c 122,420 (1.63x); coverage 0.98 to 1.13. c passes at arm's length, b is coarser but a fir, a is the weakest
   (blades from 3 m below): risk 1(d) stays ready for the owner.
6. **LODGroup size** (review 2 technical condition 2): capped so the first level ends at 0.8 screen height at its distance;
   young a's scan now ends at 4.5 m (was about 6.4 m), tall firs switch at exactly 12 / 45 / 110 m.
7. **Kit sheets** gain `below_03m` and `below_08m` for trees without a scan level (the player meets a tall fir from below).

### What was run (final: `tools/m4a/final.sh`, transcript `logs/M4a-final-sequence.txt`; run 1 in `logs/M4a-final-run1/`)

| # | Run | Result |
|---|---|---|
| 1 | Compile outside Unity | OK, OK, OK |
| 2 | Import; kit forced; scene | Exit 0; 34 prefabs, 0 failed; audit passed: 2,709 instances, 10,617 GameObjects, 18 materials, 88 meshes, 0 warnings, grounding 12.3 mm |
| 3 | `CaptureKit`, `Capture full` (RMSE against m3-r1) | 113 sheets and 32 views, `passed: true`, 0 error pixels, luminance 0.190 to 0.586 |
| 4 | Forest EditMode / forest PlayMode / greybox PlayMode | **35/35**, 9/10 (`Budget_Counts`), 7/7 (`out/M4a/tests/*.xml`) |

All final logs: 0 `error CS`, 0 `warning CS`, 0 "immutable". PackageCache md5 `8ce2f7482ff8fb845c1b94b0082b4cf0`. 59
pre-existing files: 0 differ. No test file changed. No git command that changes anything was run. Unity was launched only
through `tools/unity.sh`, one process at a time.

Looked at: every tall-fir kit sheet of k2 to k6 that a decision rests on and all of the final ones (turntables, LOD sheets,
switch and distance sheets, foliage, close turntables, below sheets; contact sheets `captures/m4a/final/contact-sheets/`),
comparison crops in `work/M4a/look/`, all 32 final views (`s1` to `s4`) and the 3 play-mode stills. No black, white,
magenta or tinted frame. The prop and young-fir sheets of the final kit were checked by statistics only (`kit-summary.json`).

### What was changed (diffs `out/M4a/*.diff`, originals `backup/M4a-pre-edit/`)

`ForestKitSpec.cs` (tall-fir table with knob 4, caps, flatten and card rules, `LodGroupSize`, trunk-only far wood,
off-atlas trunks, kit-8), `ForestPrefabs.cs` (glTF primitive without UVs, `SplitOffAtlas`, LOD size), `ForestTreeBaker.cs`
(trunk-only far wood), `ForestTexturePacker.cs` (`UnbakedAoFilled`), `ForestMaterials.cs` (tall twig smoothness),
`ForestCapture.cs` (below sheets), `forest-asset-manifest.json` (measured flag, glTF pair), `THIRD_PARTY_ASSETS.md`. New:
`Assets/ThirdParty/PolyHaven/fir_tree_01/` (827 MB with the glTF), its textures and materials in `ForestKit`, 9 meshes and
3 prefabs; all prefabs rebuilt; `Forest.unity` and `ForestData` rebuilt.

### What the next engineer (M4 second half) must know

1. **Scripts:** `tools/m4a/kit-iter.sh <tag> [noimport] [nosheets]` (kit and sheets into `captures/m4a/iterations/<tag>`),
   `tools/m4a/final.sh`, `tools/m4a/memwatch.sh <out.tsv>`; measuring scripts in `work/M4a/scripts/` (twig UVs, trunk AO,
   UV range and density, sprite colour).
2. **Frame and triangle budget:** with the planner's 271 tall firs, crouch_hide is 14.29 M triangles (was 10.63 M). Texture
   memory is 930 MiB of 1,024, mesh memory 375 of 400 MiB. Placement has to bring the triangles under 11.6 M; the knobs
   left are risk 1(d), knob 3 for tall firs, fewer bank firs. `TallFirLod0Factor = 1` undoes knob 4 if there is room.
3. **Open for review 3:** tall a's LOD0 needles (3.71x) from below; trunk_a's strip above 3 m; trunk_c's darker bark and
   bark-textured skirt; rounded pale crowns at 20 to 50 m in the mist; the young firs' broadleaf card levels (review 2
   issue 1, not addressed here).

## 2026-10-14, M4 second half: overstory, backdrop, root mounds, budgets (`captures/m4-r0`); version 1 complete

**Result: the scene builds, the audit passes (0 errors, 0 warnings), and all three test runs are green: forest EditMode 35 of 35
(nothing ignored), forest PlayMode 10 of 10 (`Budget_Counts` included: 9.86 M triangles at crouch_hide against 11.6 M), greybox
7 of 7.** The middle and long range now read as a conifer forest in mist, not a moor. One view outside the four the tests assert
is over budget: **deadfall, 11.34 ms by stopwatch and 12.43 M triangles**. This stage was interrupted after iteration g5 and
resumed; the final run rebuilt everything from the g5 sources. Details, numbers, pictures and deviations:
`issues/M4-overstory.md`.

### What was done (diffs `out/M4b/*.diff`, originals `backup/M4b-pre-edit/`)

- **Overstory** (`ForestLayout`, `ForestScatter`): core tall firs in groves (6.5 m apart where a grove noise is high, 18 m
  elsewhere, kept off young-fir thickets) instead of an even 12 to 18 m lattice; 83 core firs, 52 of them in groves.
- **Backdrop**: bank firs at the spec's 8 m spacing, but thinned toward the foot of the bank instead of toward the crest, so the
  skyline above the young-fir wall is full; 233 bank firs. Scattered tall firs keep 3 m outside landmark circles.
- **Floor under the tall firs** (`ForestTerrain`): a crown-wide humus stamp and the crown mask the young firs already had.
- **Root mounds**: the existing step 8, unchanged: 125 mounds (316 tall firs; most bank firs stand too steep).
- **Budgets** (`ForestKitSpec`): young-fir scan level 4 m (the spec's E1 fallback); knob 3 for the tall firs (LOD0 to 9 m).
  Knob 3 for the young firs, tall LOD1 to 30 m, knob 5 and a 90 m tall cull were measured and not kept.
- **`UseCrossFade = false`** (risk 11 fallback): dithered fade bands drew far tall firs as clouds of loose flecks in the mist.
- No test, no budget constant, no pre-existing file and no project setting changed.

### What was run (`tools/m4b/final.sh`, transcript `logs/M4b-final-sequence.txt`; tests re-run after a comment-only edit)

| # | Run | Result |
|---|---|---|
| 1 | Compile outside Unity | OK, OK, OK |
| 2 | Import; kit forced; scene | Exit 0; 34 prefabs, 0 failed; audit passed: 2,783 instances, 11,006 GameObjects, 18 materials, 88 meshes, grounding 7.2 mm, corridor margin 2.58 m, spawn margin 5.82 m |
| 3 | `CaptureKit`, `Capture full` (RMSE against m4a/final), `Capture debug` | 113 sheets, 32 views (`passed: true`, 0 error pixels, luminance 0.189 to 0.623), 18 debug images |
| 4 | Cost, 29 views x 3 passes (temporary test, removed) | `logs/M4b-measure-g5.txt`: 28 views under 11 ms, deadfall 11.34 ms |
| 5 | Forest EditMode / forest PlayMode / greybox PlayMode | 35/35, 10/10, 7/7; re-run 35/35, 10/10, 7/7 (`out/M4b/tests/`, `out/M4b/tests/rerun/`) |

All twelve Unity logs: 0 `error CS`, 0 `warning CS`, 0 "immutable". PackageCache md5 `8ce2f7482ff8fb845c1b94b0082b4cf0`. 59
pre-existing files: 0 differ. Test folders equal `backup/M4b-pre-edit/Tests`. No git command that changes anything was run. Unity
was launched only through `tools/unity.sh`, one process at a time.

Budgets (play mode, 2560 x 1440, worst of the four views): triangles 9.86 M (11.6 M), vertices 14.68 M (19.0 M), SetPass 40,
shadow casters 447, draw calls 2,452 (estimated), texture memory 930.3 MiB (1,024; past the 819 warning line), mesh memory 374.7 MiB
(400; past the 320 warning line), frame time at trail_start 10.23 ms, re-run 9.79 ms (stopwatch, an upper bound).

Looked at: all 32 views and crops (`captures/m4-r0/contact-sheets/s1` to `s6`, `crops1`, `crops2`; trail_start, trail_mid_fwd,
long_sightline, crouch_hide, trunk_close, boundary_e and fir_close's crop at full size), all 113 kit sheets (`kit-tall-1` to `4`,
`kit-young-1` to `3`, `kit-props-1`, `2`), the 18 debug images (`debug.png`) and the 3 play-mode stills (`playmode.png`). No black,
white, magenta or tinted frame.

### What the next engineer (and the owner at review 3) must know

1. **Scripts:** `tools/m4b/iter.sh <tag> [quick|full|none] [kit]`, `tools/m4b/final.sh`, `tools/m4b/run-measure.sh <tag> <abs out>
   <configs> <views>` (configs include all, noTall, noTallBank, noTallCore, youngScan4, youngKnob3, tallKnob3, tallLod1At30,
   tallCull90, tallNoFade, fernNear12, fernCull40), `work/M4b/scripts/tallmap.py` (tall-fir statistics and map),
   `work/M4b/scripts/skirt.py` (trunk base geometry from the glTF).
2. **Over budget, not asserted:** deadfall 11.34 ms and 12.43 M triangles. The remaining knobs cost the look or do nothing
   measurable; knob 6 (Resident Drawer) is the owner's.
3. **fir_close no longer shows the young-fir scan** at the 4 m scan distance; it shows the 52,000-needle level (side by side:
   `work/M4b/look2/fir_close-m4a-vs-m4b.png`). Base1's trial that said otherwise was clamped at 0.99 of the screen.
4. **Open for review 3:** leafy, poplar-like tall-fir crowns at 9 to 45 m in the mist; open litter between groves; trunk_c's dark
   bark root skirt (needs a ground-textured triplanar material); tall a's LOD0 from below; LOD pops while walking now that cross-fade
   is off; the young firs' card levels; texture and mesh memory past their warning lines.

## 2026-10-14, review round 1 of the M4 captures (`captures/m4-r0` to `captures/m4-r1`)

**Result: rebuild, audit and all three test runs are green** (forest EditMode 35 of 35, forest PlayMode 10 of 10, greybox 7 of 7;
audit passed, 0 errors, 2 budget warnings: instances 3,097 of 3,500 and GameObjects 12,203 of 14,000). **All 29 measured views are
inside the 11 ms frame budget** (stopwatch, median of three passes; worst pano_90 10.70 ms, deadfall 10.68 ms, was 11.34). Details,
every finding with its decision and evidence, and every deviation: `issues/M4-review1.md`. New set: `captures/m4-r1` (32 views and
crops, 113 kit sheets, 18 debug images, 3 play-mode stills, contact sheets `contact-sheets/s1` to `s6`, `debug`, `playmode`,
`kit-tall-final`).

### What changed in the picture

- **Tall firs read as firs, not poplars, from 9 to 45 m**: their twig material is darker and grey-green (the atlas is a saturated
  yellow-green that only the scan's hair-thin needles hide); LOD1 keeps 35,000 (a) and 18,000 (b) needles at 4.8x and 4.9x (was
  6.4x and 6.1x); risk 1(d): a stands on the bank only, the core is b 60 : c 40, the spawn's tall fir and the clearing's rim are b and c.
- **Denser stand**: core groves (116 core tall firs, was 83); sightline-break stands every 27 m along the trail (median clear sight
  25.7 to 21.5 m, `out/M4r1/sightlines.txt`); a closed young-fir wall on the foot of the bank; a thicket rim around the clearing;
  ferns along stand edges (cap 1,050); 102 dry branches.
- **c's root skirt** is drawn with the humus terrain layer projected from above (`forest_ground_06__ground.mat`), no dark mat.
- **Deadfall** legible and back in budget (young firs keep 7.5 m, ferns off the log axes). **Hollow**: browner mud, ferns down the
  sides. Ferns olive. Dead-branch slivers gone (tall LOD1 draws the trunk alone).
- Still weak: far tall firs (35 to 110 m, 12x needles) pale and airy in the mist; young-fir card levels broadleaf; open floor in
  pano_270 and toward the clearing; bare bank slope; canopy_up is one crown against open sky; b's scanned root flare darker than the
  floor.

### What was run (final: `tools/m4r1/final.sh`, then `tools/m4r1/final-rest.sh`; transcripts `logs/M4r1-final-sequence.txt`, `-2.txt`)

| # | Run | Result |
|---|---|---|
| 1 | Compile outside Unity | OK, OK, OK |
| 2 | Import; kit forced; scene | Exit 0; 34 prefabs, 0 failed; audit passed (3,097 instances, 19 materials, 88 meshes), grounding 10.5 mm, spawn margin 5.70 m, corridor margin 2.58 m, physical corridors free |
| 3 | `CaptureKit`; `Capture full` (RMSE against m4-r0); `Capture debug` | 113 sheets `passed: true`; 32 views `passed: true`, 0 magenta, 0 cyan, none clipped or crushed, luminance 0.191 to 0.658; 18 debug images |
| 4 | Cost, 29 views x 3 passes + floor (temporary test, removed) | `out/M4r1/measure-final-table.txt`: 0 over 11 ms; floor 4.4 to 5.1 ms with the Environment off |
| 5 | Forest EditMode / forest PlayMode / greybox PlayMode | 35/35, 10/10, 7/7 (`out/M4r1/tests/*.xml`) |

All nine Unity logs: 0 `error CS`, 0 `warning CS`, 0 "immutable". PackageCache md5 `8ce2f7482ff8fb845c1b94b0082b4cf0`. 59 pre-existing
files: 0 differ. No temporary file left in the project. No git command that changes anything was run. One Unity process at a time.

Budgets (`Budget_Counts`, worst of four views): triangles 10.51 M (11.6 M), vertices 15.47 M (19.0 M), draw calls 2,590 (estimated),
SetPass 40, shadow casters 495, texture memory 930.3 MiB (1,024; past the warning line), mesh memory 383.0 MiB (400; past the warning
line), frame time at trail_start 9.54 ms (stopwatch; FrameTiming 22.47 ms on 8 of 240 frames, still unexplained).

Looked at: all 32 final views and the i1, i3, i4 iteration sets as contact sheets, crops at 1:1 (skirt, hide spots, height blend,
`work/M4r1/look/`), the tall-fir kit sheets of k1 and the final kit, the 18 debug images and the 3 play-mode stills. No black, white,
magenta or tinted frame.

### What changed in the code (diffs `out/M4r1/*.diff`, originals `backup/M4r1-pre-edit/`)

`ForestKitSpec.cs` (kit-9: tall LOD1 targets, LOD1 to 35 m, trunk-only wood from LOD1, per-tree crown core switch (off), skirt
constants), `ForestMaterials.cs` (twig and fern colour, twig smoothness 0.28, mud, height blend 0.025, ground variant material),
`ForestPrefabs.cs` (`SplitSkirt`), `ForestTreeBaker.cs` (per-tree crown core), `ForestLayout.cs` (core mix, spawn and rim firs,
groves, bank jitter, gap-free bank foot, deadfall keep-out, sightline stations, clearing rim, branches, fern cap and edge band,
viewpoint gap, hollow ferns), `ForestScatter.cs`, `ForestTerrain.cs` (stations and rim in the thicket field, tall-fir humus).
**One test file**: `ForestWorldTests.ExpectedCounts` (tall firs 280 to 360, young firs 770 to 880, branches 65 to 100, fern cap 900
to 1,050), changed together with the tunables they pin, as rule 5 allows; reason in `issues/M4-review1.md` section 6. No budget
constant, no correctness test, no pre-existing file and no project setting changed. Removed: the generated, unused
`ForestKit/Materials/fir_tree_01_twig__crowncore.mat`.

### What the next engineer (and the owner) must know

1. **Scripts**: `tools/m4r1/kit-iter.sh`, `iter.sh`, `run-measure.sh` (configs added: `fadeLevels`, `nothing`, `noHold`,
   `tallLod1At35`), `final.sh`, `final-rest.sh`, `sheets.sh`; `work/M4r1/scripts/` (`atlasmean.swift`, `table3.py`, `sight.py`).
2. **`tools/unity.sh` can refuse with a false "EDITOR OPEN"** while any shell's command line contains the Unity path; this round it
   was a waiting shell of the orchestrating workflow. `tools/m4r1/unity2.sh` checks the running executables instead (and the rule's
   `-projectpath` check). Never kill the matching process: it is not an editor.
3. **Headroom is small again**: 24 of 29 views over the 9.5 ms warning line; pano_90, deadfall and crouch_hide at 10.7 ms. Measured
   and not applied, for the owner: cross-fade between levels (+0.5 to +0.9 ms), young-fir knob 3 (-0.1 to -0.3 ms).
4. **Mesh memory is 383 of 400 MiB**: `TallFirLod0Factor = 1` measured 408.4 MiB. Marking render meshes non-readable is untested.
5. **For the owner** (review 3): risk 1(d) taken for a; the spawn fir is c (spec 3.5 says a); tall LOD1 ends at 35 m; cross-fade off
   (pops while walking, never checked in motion); fir_close shows the first thinned level, not E1's scan; the young-fir card
   levels; the probe bake (M5) for light under the canopy; adding deadfall and pano_90 to `Budget_Counts`.

## 2026-10-15, review round 2 of the M4 captures (`captures/m4-r1` to `captures/m4-r2`)

**Result: rebuild and audit pass; the gate's three test runs on the final build are green** (forest EditMode 35 of 35, forest
PlayMode 10 of 10, greybox 7 of 7: `out/M4r2/tests/gate/`). **`Budget_FrameTime` is marginal: it failed in all three final
sequences** (11.22, 11.80, 11.82 ms against 11 ms, each time right after the 29-view x 3-pass cost measurement, when the empty-scene
stopwatch floor had risen from about 5.0 to 5.5-6.1 ms) and passed in all eight standalone runs (9.80 to 10.68 ms). Details, every
finding with its decision and evidence, and every deviation: `issues/M4-review2.md`. New set: `captures/m4-r2`.

### What changed in the picture

- **Young firs no longer read as broadleaf at 14 to 85 m**: the card levels use five single-shoot sprites (narrow bottle-brush
  shoots cut from the atlas's sprays) on cylindrical cells 2.5 times longer along the branch, so the far crowns are horizontal brushy
  tiers. The far level (42 to 85 m) draws cards only.
- **Tall firs**: far level needles capped at 6 (a: 8; was 12) with more of them kept, middle level capped at 3.5 (was 4.8 to 4.9);
  the straws, straps and fans are gone. LOD1 draws the trunk and the branches inside the crown again (no pom-poms on a pole, no
  slivers). Shoot cards for tall firs were built and rejected (broadleaf clumps). They still read as thin, airy firs in the mist,
  not dense spires.
- **b's black root mat**: b's branch bark shares the triplanar bark of its off-atlas trunk, which frees the fourth sub-mesh for a
  root skirt in the humus ground material (as c's).
- **Stand**: groves stand over thickets, more and tighter (core tall firs 116 to 152), tall scale 0.80 to 1.25, a pole-stage cohort
  of young firs (22, held to the regular LOD distances), thickets 2.4 m, glades 32 m, understorey 40 %, stations every 17 m;
  porous sight test 63 to 58 % of rays clear beyond 30 m (target under 30 % not reached).
- **First view**: three second-rank firs, two firs behind the boulders, a fern drift, two b firs on the axis 30 to 35 m out and a c
  beside canopy_up (`ForestLayout.SpawnExtras`, own random stream); trimmed from a larger set to hold trail_start's frame time.
- **World edge**: bank young firs and tall firs follow clump noises (no rows from above), fern drifts and 25 branches on the bank,
  bank paint humus and moss instead of the slope's stony soil.
- **Floor**: humus darker and cooler, 1.6 under tall crowns and 1.2 at every trunk; twig smoothness 0.15; mud wetness 0.30.
- Still weak (for the owner): much sky in eye-level frames, light under the stand flat (M5 probe bake), tall crowns airy, the litter's
  warmth (M2-accepted floor), LOD pops (cross-fade off), deadfall, rocks, trail grooming and clearing medium findings not done.

### What was run (`tools/m4r2/final.sh` three times: `logs/M4r2-final-run1/`, `logs/M4r2-final-run2/`, `logs/M4r2-final-sequence.txt`; then the gate runs)

| # | Run | Result |
|---|---|---|
| 1 | Compile outside Unity | OK, OK, OK |
| 2 | Import; kit forced; scene | Exit 0; 34 prefabs, 0 failed; audit passed (3,420 instances, 13,433 GameObjects, 19 materials, 88 meshes, 2 budget warnings), grounding 7.9 mm |
| 3 | `CaptureKit`; `Capture full` (RMSE against m4-r1); `Capture debug` | 113 sheets and 32 views `passed: true`, 0 magenta, 0 cyan; 18 debug images |
| 4 | Cost, 29 views x 3 passes + floor | `out/M4r2/measure-final-table.txt`: 3 over 11 ms (pano_90 11.61, deadfall 11.23, crouch_hide 11.13; floor 5.5 to 6.1 ms) |
| 5 | Forest EditMode / PlayMode / greybox, in the third sequence | 35/35, **9/10 (`Budget_FrameTime` 11.82 ms)**, 7/7 (`out/M4r2/tests/run3/`) |
| 6 | Gate runs on the same build, alone, in order (`logs/M4r2-gate-6*.log`) | **35/35, 10/10 (9.81 ms), 7/7** (`out/M4r2/tests/gate/`) |

All Unity logs: 0 `error CS`, 0 `warning CS`, 0 "immutable". PackageCache md5 `8ce2f7482ff8fb845c1b94b0082b4cf0`. 59 pre-existing
files: 0 differ. No temporary file left in the project. No git command that changes anything was run. Unity was launched only through
`tools/m4r2/unity2.sh`, one process at a time, after the rule's own check.

Budgets (gate run, `captures/m4-r2/playmode/budget-counts.txt`): triangles 10.88 M (11.6 M), vertices 16.49 M (19.0 M), draw calls
1,812 (estimated; was 2,590), SetPass 41, shadow casters 483, texture memory 930.3 MiB, mesh memory 382.4 MiB, frame time at
trail_start 9.81 ms (stopwatch; FrameTiming again 8 of 240 frames, unexplained).

Looked at: every kit iteration sheet a decision rests on (`iterations/k1` to `k6`, comparison crops `work/M4r2/look/`), the scene
iterations' contact sheets (`s1` to `f5`), all 32 final views as contact sheets `s1` to `s6`, the final kit sheets
(`kit-tall-final`, `kit-young-final`), the 18 debug images and the 3 play-mode stills. No black, white, magenta or tinted frame.

### What changed in the code (diffs `out/M4r2/*.diff`, originals `backup/M4r2-pre-edit/`)

`ForestKitSpec.cs` (kit-10: shoot sprites, radial card cells, card fill and stretch, tall caps and targets, LOD distances 9/30/85 m,
young LOD1 targets, wood kinds per level, skirt for b, fern far 40 m), `ForestFoliageThinner.cs` (`CardCellKey`, stretch cap),
`ForestTreeBaker.cs` (per-level wood filter, one-draw far trunk), `ForestPrefabs.cs` (b's branch bark triplanar),
`ForestTexturePacker.cs` (AO matching keeps the spray boxes), `ForestMaterials.cs` (twig smoothness, humus tint, mud wetness),
`ForestLayout.cs`, `ForestScatter.cs` (groves, bank clumps, pole-stage cohort, first-view extras, bank ferns and branches, stations),
`ForestTerrain.cs` (humus, bank paint), `ForestWorldRig.cs` (pole-stage LOD size). **One test file**: `ForestWorldTests.ExpectedCounts`
(tall firs 360 to 400, young firs 880 to 970, branches 100 to 125, fern cap 1,050 to 1,250), changed with the tunables they pin
(rule 5); no other test, budget constant, pre-existing file or project setting changed.

### What the next engineer (and the owner) must know

1. **Scripts**: `tools/m4r2/kit-iter.sh`, `iter.sh`, `run-measure.sh` (configs added: `tallCull80`, `tallLod1At25`, `noBankFerns`,
   `noMid`), `final.sh`, `sheets.sh`, `unity2.sh`; `work/M4r2/scripts/uvcrop.swift` (atlas boxes), `sight2.py` (porous sight test).
2. **The frame budget has no margin under sustained load.** The stopwatch is bound by draw submission in the Editor (triangle cuts
   barely move it; removing the first view's firs moves it 1.1 to 1.6 ms) and by the machine's state. Run the gate's test runs before
   any long measurement, or the frame-time test can fail on heat alone. Any new density must be paid in draws.
3. **Instances 3,420 of 3,500 and GameObjects 13,433 of 14,000**: little room left.
4. **For the owner**: the frame-time method (stopwatch, Editor) and a development build (M6); adding deadfall, pano_90, boundary_s to
   the budget tests; the M5 probe bake for light under the stand; cross-fade; the litter warmth and fog layering presets.

## 2026-10-15, independent verifier: M4 (version 1 complete), `logs/verify-m4`, `captures/verify-m4`

**Result: every gate I could run is green, and I saw the evidence myself.** Build exit 0, audit passed (0 errors, 2 budget
warnings), forest EditMode 35 of 35 (nothing ignored; both `TreeLods_*` ran and passed), forest PlayMode 10 of 10 (twice),
greybox 7 of 7, 32 views written with 0 error pixels and all looked at, the 59 pre-existing files unchanged. Nothing in the
project was fixed or edited. Unity was launched only through `logs/verify-m4/vm4.sh` (the rule's `pgrep` check plus a check
for any running Unity executable; never `-nographics`; no `-quit` with `-runTests`), one process at a time. No exit code 198.

| # | Run | Result | Evidence |
|---|---|---|---|
| V00 | md5 of the 59 pre-forest files (the task's `find ... -exec md5 -r`) and the 108 files of `pre-forest.tgz` (with `.meta`, `EditorBuildSettings.asset`) | 59 identical, 0 changed, 0 missing; 108 identical | `V00-regression-before.txt`, `V00-tgz-before.txt` |
| V01 | `ForestBuild.BuildAll -forestForce -quit` on the project | Exit 0 in 72 s. "34 prefabs built, 0 up to date, 0 skipped, 0 failed"; "Forest audit passed: 3420 instances, 13433 GameObjects, 19 materials, 88 meshes, 2 warning(s)" | `V01-buildall-force.log`, `build-report.after-V01.json`, `kit-report.after-V01.json` |
| V02 | Forest EditMode | 35 passed, 0 failed, 0 inconclusive, 0 skipped | `V02-forest-editmode.xml`, `.summary.txt` |
| V03 | Forest PlayMode (`-captureOut captures/verify-m4`) | 10 passed, 0 failed | `V03-forest-playmode.xml`, `captures/verify-m4/playmode/` |
| V04 | Greybox `Gamex.Tests.PlayMode` | 7 passed (the seven `StealthLoopTests`) | `V04-greybox-playmode.xml` |
| V05 | `ForestCapture.Capture -captureSet full` (RMSE against `captures/m4-r2`) | Exit 0. 32 views and 32 crops, `passed: true`, 0 magenta, 0 cyan, 0 clipped, crushed black at most 0.007 %, mean luminance 0.183 to 0.623, RMSE to m4-r2 0.0018 to 0.0030 | `captures/verify-m4/capture-summary.json`, sidecars |
| V06 | `fetch_assets.py --milestone M4 --dry-run` | 97 files in place, size and md5 match the manifest | `V06-fetch-M4-dryrun.log` |
| V07 | Forest PlayMode again (repeatability of the frame-time test) | 10 passed; frame time 9.82 ms median, p95 10.63 | `V07-forest-playmode-rerun.xml`, `captures/verify-m4/rerun/playmode/` |
| V99 | Regression after all runs | 59 and 108 files identical; PackageCache `DefaultHDTerrainMaterial.mat` md5 `8ce2f748...`; only `Forest.unity`, the two `ForestData/Lighting` profiles, `build-report.json` and `kit-report.json` changed in the project (outside ThirdParty) | `V99-*.txt` |

All seven Unity logs: 0 `error CS`, 0 `warning CS`, 0 "immutable", no exception thrown (the 13 "Exception" matches in the
PlayMode log are the test framework's `ExecuteEnumerableAndRecordExceptions` stack frames).

**Determinism:** the forced rebuild reproduced `placements.json` byte for byte (= `out/M4r2/placements.json`), all 104 mesh
assets byte for byte, and every LOD row of the kit report (triangles, vertices, distances, coverage).

**Looked at:** all 32 views (`contact-sheets/s1` to `s6`), all 32 crops (`crops1`, `crops2`), the 6 play-mode stills
(`playmode`, `playmode-rerun`), and `overview_ortho`, `crouch_hide`, `trail_start` at full size. No black, white, magenta or
tinted frame. The middle and long range read as an open fir forest in mist, not a moor. Seen, not judged as failures: thin,
airy tall crowns; much open litter and sky; flat, blade-like needles at 0.5 to 1 m in `crouch_hide`; a rust-red rock in
`trail_strip_02`; bank tall firs in loose bands from above; grey side bars in `overview_ortho` (frame wider than the tile).

**Test changes since M2 (checked against `backup/*-pre-edit/Tests`):** only `ForestWorldTests.ExpectedCounts` (tall 280 to 400,
young 770 to 970, branches 65 to 125, fern cap 900 to 1,250; the comment still describes review 1's values) and
`ForestPlayBudgets` (triangles 8.0 to 11.6 M, vertices 10 to 19 M, mesh memory 300 to 400 MiB, the E1 re-base). Both are of the
kinds rule 5 and spec section 10 allow; no correctness test changed.

**Budgets (play mode, 2560 x 1440, worst of trail_start, crouch_hide, clearing, long_sightline):** triangles 10.88 M (measured;
re-based 11.6 M, spec 8.0 M exceeded), vertices 16.49 M (measured; 19 M, spec 10 M exceeded), SetPass 41 (measured), shadow
casters 483 (measured), texture memory 930.3 MiB above an empty scene (measured; 1,024, past the 819 warning), mesh memory
382.4 MiB (measured; 400, spec 300 exceeded), draw calls 1,812 (ESTIMATED, the engine counter reads 0), frame time 9.82 ms
median, p95 10.50 / 10.63 (stopwatch around a one-pixel readback in the Editor, an upper bound, not GPU time; FrameTiming gave 8
of 240 frames at 22.5 ms, rejected by probe P5). Scene: 3,420 instances (3,500), 13,433 GameObjects (14,000), 19 materials, 88
meshes, 4 textures at 4096.

**Sizes for the Git LFS decision:** `Assets/ThirdParty` 1,541 MB in 172 files (97 downloads of 1,540.8 MB plus metas; spec 4.2
planned 988 MB, the difference is the two glTF geometry files added at M3 and M4); three files exceed GitHub's 100 MB limit
(`fir_tree_01/Source~/fir_tree_01.bin` 478.5 MB, `fir_tree_01.fbx` 249.3 MB, `rock_moss_set_02__normal.png` 101.2 MB).
Generated: `ForestKit` 580.6 MB (meshes 401.2 MB as text, largest `fir_sapling_medium_a__lod0.asset` 91.3 MB; textures
178.8 MB), `ForestData` 50.9 MB (`Terrain.asset` 46.9 MB), `Forest.unity` 12.3 MB: about 644 MB. Code and tests about 1.3 MB.
`.gitignore` is still the pre-forest one: none of these is ignored yet.

**Not verified:** the 29-view cost measurement (it needs a temporary test copied into the project; deadfall, pano_90 and
boundary_s are asserted by no test); a clean-copy build at M4; the kit and debug capture sets; frame time under sustained load
(the team's three failures); the owner's review-2 go and review 3; GPU frame time itself.

## 2026-10-15, D1: GPU Resident Drawer and GPU occlusion culling (measured; the drawer switched on)

**Result: the GPU Resident Drawer is on; GPU occlusion culling stays off. Rebuild, audit and all three test runs are green on the
switched project** (forest EditMode 35 of 35, forest PlayMode 10 of 10, greybox 7 of 7: `out/D1/tests/run1/`). The gain is real
but small at today's density: worst views 10.71 ms -> 10.25 to 10.45 ms (stopwatch median), draws (estimated) 1,812 -> 298 at
crouch_hide, triangles unchanged 10.88 M, SetPass 41-43 -> 36-39; the drawer adds a fixed ~0.45 ms per frame (empty-scene floor),
so the 29-view mean is unchanged. Occlusion culled 0 instances and cost 0.6 to 1.9 ms. Details, sources, tables and every image
comparison: `issues/D1-optimisation.md`.

### What was run

| # | Run | Result |
|---|---|---|
| 1 | Sources and docs (HDRP 17.6, Core 17.6, Manual) | Requirements with file and line in the issue file, section 2. Flags `-force-gpuresidentdrawer` / `-force-gpuocclusion` exist (Editor only) and work in batch play mode |
| 2 | A/B by flag, nothing changed: arms none / drawer / drawer + occlusion, 29 views, 3 runs each interleaved, 60 s rest, floor per run (`tools/d1/ab.sh`) | `out/D1/ab-table.txt`: crouch_hide 10.71 / 10.28 / 11.50 ms, pano_90 10.71 / 10.25 / 11.33; over 11 ms 0 / 0 / 3 of 29 |
| 3 | Images: cut and 64th frame of 29 views, 160-frame walk; arms none (twice), drawer, drawer + occlusion | `out/D1/image-diff.txt`: same as the noise of two identical runs, except one far fogged fir at another LOD level in deadfall; walk at noise level (no missing, late or flickering object) |
| 4 | Density stand-in (every LOD group copied nearby), none vs drawer (`tools/d1/dense.sh`) | drawer saves 0.3 to 0.8 ms at 2x, 1.2 to 3.0 ms at 5x |
| 5 | Greybox before (edit-mode `SceneCapture`, forest harness `-captureScene`, temporary play-mode capture x2) | play mode deterministic (before1 = before2 bit for bit) |
| 6 | `ForestProjectSettings.EnableGpuResidentDrawer -forestAllowProjectSettings` (new script) | 3 lines changed in 3 files (below); backups `backup/pre-D1/`, checksums `backup/post-D1.md5` |
| 7 | `ForestBuild.BuildAll -forestForce` | exit 0, audit passed (3,420 instances, 13,433 GameObjects); placements and 104 meshes byte-identical |
| 8 | Forest EditMode / forest PlayMode / greybox (`tools/d1/tests.sh run1`) | 35/35, 10/10, 7/7 |
| 9 | Greybox after; forest full capture against `captures/verify-m4` | edit mode 100 % identical; play mode within 4/255 except the raptor's outline (max 42/255) and, in 1 of 5 drawer runs, the throwable rocks one pixel lower; forest RMSE 0.0017 to 0.0030, 0 error pixels |
| 10 | 29 views after, no flag, 3 runs (`tools/d1/after.sh`) | `out/D1/after-table.txt`: worst pano_90 10.45 ms (p95 11.08), crouch_hide 10.42 (11.34); 0 over 11 ms |
| 11 | D2 knobs with the drawer on (2 runs), occlusion at 2x / 3x, knobs at 2x | `out/D1/knobs-table.txt`, `occlusion-dense-table.txt`, `dense-knobs-table.txt` |
| 12 | `Revert`, then `EnableGpuResidentDrawer` again | the pre-D1 values back; re-enable reproduces `post-D1.md5` exactly |

All 51 D1 Unity logs: 0 `error CS`, 0 "immutable"; 28 `warning CS`, all from the temporary greybox capture (obsolete overload).
65 launches, all exit 0, never two at once, never `-nographics`, no `-quit` with `-runTests`, no exit 198. PackageCache md5
`8ce2f7482ff8fb845c1b94b0082b4cf0`. 59 pre-existing files: the 3 settings files differ (rule 1's exception), 0 others
(`tools/d1/check-preexisting-d1.sh`). No temporary file left in the project. No git command that changes anything was run.

### What changed in the project

- `Assets/Game/Settings/HDRP.asset:525` GPU Resident Drawer `mode: 0` -> `1`; `ProjectSettings/GraphicsSettings.asset:58`
  `m_BrgStripping: 0` -> `2` (Keep All); `ProjectSettings/ProjectSettings.asset:531` static batching off for Standalone.
  QualitySettings unchanged. Owner-approved (brief, rule 1).
- New `Assets/Game/Editor/Forest/ForestProjectSettings.cs`: `EnableGpuResidentDrawer` (`-forestGpuOcclusion` adds occlusion),
  `Revert`, `Report`; batch only, refuses without `-forestAllowProjectSettings`, backs up to `Library/GamexForest/ProjectSettingsBackup/`.
- **One test file**: `ForestPlayModeTests.Budget_Counts` counts under the drawer (diff `out/D1/ForestPlayModeTests.cs.diff`). Under
  the drawer every renderer reports `isVisible` (the old estimate read 28,222 draws) and 'Shadow Casters Count' leaves out the
  drawer's casters (483 -> 165); the test now takes draws and shadow casters from the drawer's own culling statistics (463 draws and
  645 casters at crouch_hide, the casters summed over cascades) and fails if GPU occlusion is on (the triangle counter then misses
  indirect draws: 10.88 M read 3.99 M). No budget constant and no other test changed.

### What the next engineer (D2) must know

1. **Headroom**: about 0.55 to 0.75 ms at the worst view (pano_90, crouch_hide). Density costs about +2.7 ms per "1x" of
   everything even with the drawer (2x: worst 13.1 to 13.6 ms, 20.6 M triangles; 3x: 16.0 ms, 32 M); the stopwatch now follows
   triangles. Fog 20 m + cull 55 m saves 0.5 ms today and 0.6 ms (up to 1.1) at 2x, cull 45 m 0.64 / 0.82 ms. The drawer alone
   does not fund the density pass: pay with cheaper levels for new objects and put density where it blocks sight.
2. **Young-fir scan level is already 4 m** (not 6). 6 m costs up to +2.45 M triangles for ~0 ms; 3 m is not reachable with
   screen-relative LOD heights (all 976 groups clamp at 0.99 of the screen); the clamp saves ~0.24 ms. Spec 15: not below 4 m.
3. **Measuring under the drawer**: `Renderer.isVisible` is true for every drawer renderer; 'Shadow Casters Count' misses the drawer's
   casters; with occlusion on, triangles and vertices miss its indirect draws; 'Draw Calls Count' still reads 0. Use
   `tools/d1/ForestD1MeasureTemp.cs` (drawer statistics by reflection; configurations `all`, `nothing`, `youngScanAt<m>`,
   `fogL<L>`, `cull<m>`, `fogL<L>cull<m>`, `dense<k>`, and `a+b` combinations) with `tools/d1/run-measure.sh` (bash; zsh does
   not split a flag variable). The empty-scene floor with the drawer is ~5.2 to 5.4 ms.
4. **A/B**: `-force-gpuocclusion` alone turns occlusion on for a test; nothing forces the drawer off (use `Revert`, then
   `EnableGpuResidentDrawer`). Revisit occlusion only with solid occluders (rock faces, ridges).
5. **Open**: GPU time and a player build (the drawer's CPU saving shows better there); the greybox rocks drawn one pixel lower in
   1 of 5 drawer runs (cause not established); `Logs/forest-work/` not updated.

## 2026-10-15, D2: dense forest, measured in a macOS development player

**Result: the forest is about three times as dense at eye level, inside the new player frame budget, and the final build passes
the audit and all three suites** (forest EditMode 35 of 35, forest PlayMode 10 of 10, greybox 7 of 7, three times:
`out/D2/tests/final/`, `final2/`, `final3/`). Core (1.87 ha): tall firs 81 -> 282 a hectare, young firs 283 -> 664, fern coverage
10.9 -> 26.4 % (carpets under the tall crowns), logs / stump patches / branches 8 / 10 / 127 -> 22 / 23 / 309, the clearing a 10 m
glade, mist 70 -> 20 m with every class culled at 50 to 55 m. Sightlines (porous model): trail 58 -> 17 % of eye-level rays clear
beyond 30 m (82 -> 47 % beyond 15 m), core points 42 -> 8 % (70 -> 33 %). Player, worst view (pano_90), sparse and final builds
interleaved: 16-frame median 7.58 -> 9.71 ms, 16-frame p95 9.04 -> 12.32 ms (budget 12.5 / 15). Details: `issues/D2-density.md`.

### What was run

| # | Run | Result |
|---|---|---|
| 1 | New harness and build script (`Assets/Game/Diagnostics`, own asmdefs); macOS development player through `BuildPipeline` with an explicit scene list into `Builds/` | builds in 8 to 115 s; 2560 x 1440 full-screen window, vsync 0, drawer on in the player |
| 2 | Calibration on the sparse scene: 19 player runs (modes compared), 3 Editor stopwatch runs | player worst 7.4 to 8.3 ms steady; Editor worst 10.21 ms; frame times bursty in most runs (below) |
| 3 | Density iterations i1 to i9: scene build, density and sightline scripts, full captures of i4, i6, a quick set of i7, player and Editor measurements of i2, i5, i8 (+ a doubled stand) | `out/D2/density-iterations.txt`, `captures/d2-i4`, `d2-i6`, `d2-i7q` |
| 4 | `tools/d2/final.sh`: forced rebuild, audit, full and debug captures, three suites | audit passed (5,825 instances, 23,274 GameObjects, 0 warnings), placements identical to i9; `captures/d2-final` 32 views, 0 error pixels; 35/35, 10/10, 7/7 |
| 5 | Pre-existing HDRPDefaultResources pair put back (the player build had rewritten it); `tools/d2/final-verify.sh` | 108-file archive 0 differ; `captures/d2-final-restored` RMSE 0.0017 to 0.0026 to d2-final; 35/35, 10/10, 7/7 |
| 6 | Editor stopwatch on the final scene, 3 runs | worst pano_90 12.99 ms, floor 5.1 ms |
| 7 | Player: sparse and final builds interleaved 3 + 3, then 1 + 4 more | final worst 16-frame median 9.56 to 10.48 ms, p95 11.60 to 12.86 ms per run |
| 8 | Three suites after comment-only corrections | 35/35, 10/10, 7/7 (`final3`) |

All Unity logs of the final runs: 0 `error CS`, 0 `warning CS`, 0 "immutable"; no exit code 198; one Unity process at a time
(`tools/d2/unity.sh`), never `-nographics`, never `-quit` with `-runTests`. No git command that changes anything. No temporary file
left in the project.

### What changed in the project

- New: `Assets/Game/Diagnostics/` (`ForestFrameHarness.cs`, runtime, inert without `-forestMeasure`; `Editor/ForestPlayerBuild.cs`).
- `ForestLayout`, `ForestScatter`, `ForestTerrain`, `ForestFields` (density, planner order: rocks and logs before young firs, forest
  logs, fern shade carpets, glade, the paint's own thicket field, hollow keep-out), `ForestKitSpec` (culls), `ForestLightingPresets`
  (mist), `ForestBudgets` (re-based budgets). Diffs `out/D2/diffs/`, originals `backup/D2-pre-edit/`.
- **Re-based, with reasons in `issues/D2-density.md` section 6**: `ForestWorldTests.ExpectedCounts` and fern caps; instances 3,500 ->
  6,500 and GameObjects 14,000 -> 26,000 (audit); triangles 11.6 -> 19.0 M, vertices 19 -> 31 M; `Budget_FrameTime` (Editor
  stopwatch) 11.0 -> 14.0 ms, warning 9.5 -> 13.0, from the measured relation player = a x stopwatch + b (fits 14.4 to 17.8 ms for
  a 12.5 ms player frame; the lowest protects the player budget). No correctness check changed.
- No project or Player setting changed; the post-D1 `HDRP.asset` and the HDRPDefaultResources pair are put back after every player
  build (`tools/d2/build-player.sh`).

### What the next engineer (and the owner) must know

1. **Measuring in the player**: `tools/d2/build-player.sh <name> <tag>`, `run-player.sh <app> <tag> [args]`, `verdict.py`,
   `relate.py`. On this MacBook Pro the vsync-off player often hands back frames in bursts (single frames 2 ms or 30 ms, mean
   unchanged, no GPU timing); all runs after 10:19 did. Judge by the 16-frame figures and interleave A/B runs: the player drifts
   by 1 to 1.5 ms between sessions. The owner switching away from the full-screen window stalls frames for 0.5 to 1.4 s; such views
   are marked interrupted and left out.
2. **Per-frame p95 under 15 ms was not shown for the final build** (bursty runs: 33 to 45 ms; the sparse build's bursty runs 15 to
   31 ms). The coordinator should decide whether the 16-frame p95 (12.3 ms) is accepted as the measure.
3. **Headroom**: about 2 to 2.8 ms at the worst view (pano_90) against 12.5 ms. The doubled stand (`-forestMeasureDense 2`) costs
   16.4 to 17.1 ms in the player: there is no room for another doubling without a cheaper far level.
4. **For the owner's review**: heavy grey mist (frames 0.18 to 0.29 mean luminance, colour gone beyond 10 m), overview views now
   mostly mist, branches of the bank's wall across the frame edge in boundary_n / boundary_s / deadfall, the trail corridor still
   open litter for 2.5 to 3.5 m each side, the clearing now a 10 m glade.
5. **Open**: walking in the dense stand (LOD pops, cross-fade off); dinosaurs among 2,500 trunks (NavMesh connectivity is tested,
   agent movement is not); `Logs/forest-work/` not updated.

## 2026-10-15, N1: night as the main mood, and the player's torch

**Result: the forest is built at night by default, the player carries a torch, the player build stays inside its frame budget with
the torch on and casting shadows, and all three suites pass** (forest EditMode 40 of 40, forest PlayMode 13 of 13, greybox 7 of 7:
`out/N1/tests/final2/`). OvercastMist stays in the scene and buildable, and renders as before. Details: `issues/N1-night.md`.

### What the night and the torch are

- **Night preset** (`ForestLightingPresets.Night`, now `Default`; `Inspect = "OvercastMist"` keeps kit turntables, the probe and the
  layout's backlit bearing on daylight, so the plan did not move): sky `qwantani_night_puresky` 0.1 lux, moon 0.3 lux 4,100 K at
  (50, 135) with volumetric dimmer 2, moon shadows 50 m / 3 cascades, mist 30 m, anisotropy 0.6, automatic exposure -1.2 .. 10 EV with
  +0.5 compensation metered on a disc in the middle of the screen (radius 0.45, histogram 40 .. 98 %), Lift Gamma Gain gamma +0.25,
  white balance -22, saturation -30, contrast +10, bloom 0.15, grain Medium1 0.3, vignette 0.35.
- **Torch** (`Assets/Game/Scripts/Player/PlayerFlashlight.cs`, HDRP side `Editor/Forest/ForestFlashlight.cs`): 300 lm (peak 2,860 cd),
  cones 10 / 70 degrees, range 35 m, 4,500 K, soft shadows 1,024 px every frame, code-generated beam pattern (asset
  `ForestData/Lighting/FlashlightBeamPattern.asset`), volumetric dimmer 0.04, at the hand (0.22, -0.30, 0.25) m aimed 12 m ahead,
  lag 0.07 s capped at 10 degrees, snaps on jumps over 1.5 m; F and gamepad left shoulder (`PlayerInputReader`, one action); starts on;
  `IsOn`, `IsOnChanged`, `SetOn`, `Toggle`, `SnapToView`. Forest player only; greybox unchanged.
- Look (captures `captures/n1-final`, `n1-dark`, play-mode stills `captures/n1-tests/final*/playmode`, all looked at): without the
  torch, black trunks against the moon's glow in the mist, the trail and floor barely readable for the first metres (means 0.03 to
  0.08); with it, a shaft from the hand, the lit trail, firs, ferns and logs, a grey card readable at 20 to 25 m that fades at 30 m,
  near foliage not blown out (at most 0.19 % clipped), the forest around the beam faintly there (at most 41 % crushed, rock_close).

### Player frame time (macOS development player, 2,560 x 1,440, vsync off, 16-frame windows; all runs bursty)

| Final session, interleaved (`out/N1/final-paired.txt`) | Worst 16-frame median | Worst 16-frame p95 |
|---|---|---|
| D2's final player (OvercastMist, no torch) | 9.62 ms (pano_90) | 11.73 ms |
| Night, torch off | 10.18 ms (pano_90) | 11.42 ms |
| Night, torch on (game start) | **11.51 ms (pano_90)** | **12.67 ms** (long_sightline) |

Across the day's sessions torch on at pano_90: 11.26 to 12.48 ms (budget 12.5). The torch costs +0.5 to +1.3 ms a view (paired, same
run), two thirds of it its shadow casters (+2.0 to +2.9 M triangles); 512 px costs the same as 1,024 px, a 25 m range removes 0.3 M
triangles and dims the beam. Chosen: 1,024 px, every frame, 35 m.

### What changed in the project

New: `PlayerFlashlight.cs`, `ForestFlashlight.cs`, `ForestFlashlightTests.cs` (+ metas). Changed: `PlayerInputReader.cs` (one action),
`ForestFrameHarness.cs` (torch, preset, overrides, paired mode), `ForestLightingPresets.cs`, `ForestLighting.cs` (per-preset metering,
shadow gamma, menu items Night / Overcast Mist / Rebuild Lighting), `ForestWorldRig.cs`, `ForestCapture.cs` (`-captureFlashlight`,
`-captureViews`, per-preset windows), `ForestBudgets.cs`, `ForestLayout.cs`, `ForestProbe.cs`, `ForestSceneTests.cs`,
`ForestPlayModeTests.cs`, `ForestPlaySupport.cs`, `ForestPlayBudgets.cs`. **Re-based** (rule 4, evidence in the issue file, section 6):
triangle budget 19.0 -> 21.5 M (warn 20.0), vertex budget 31.0 -> 36.0 M (warn 33.0), because the torch's shadow map adds its casters
and the player holds its frame budget at 21.30 M / 35.64 M. Two tests adapted, not weakened: the rig comparison leaves out the torch's
subtree only (and requires exactly one torch on the forest player and none on the greybox's); the terrain-layer render runs under the
inspect preset. `HDRP.asset`, `ProjectSettings`, `Packages`, the greybox untouched (pre-N1.md5: only `PlayerInputReader.cs` differs).
Night sky downloaded. No git command; no temporary file left in the project.

### What the next engineer (and the owner) must know

1. **Margin**: 1.0 to 1.2 ms at the worst view in a normal session, about none in a slow one. Dinosaurs need room. Next levers, not
   built: shorter moon shadows at night; the drawer's small-mesh culling for shadow views (untested for a spot light).
2. **Switching the torch off** leaves the frame dark for 2 to 3 s while the exposure falls back (default adaptation); on, a short
   overexposure. Intended, untested.
3. **Measuring**: `tools/n1/paired.sh` / `-forestMeasureTorchAB` compares torch off / on / no shadows inside one run, free of the
   1 to 1.5 ms drift between runs; separate runs could not resolve the torch's cost.
4. For the owner: ground_close's 0.4 m camera holds the torch at the litter (a narrow lit strip); the torch is on in daylight too;
   the beam's shaft is a froxel effect, judged in stills and one turning still only.

## 2026-10-17, W1 package I1: wind shader, wind materials, baked wind data (static regression green; one new test fails)

**Result: the forest renders exactly as N1 with every wind global at zero, on the new wind Shader Graphs and the new meshes; forest PlayMode
13 of 13 and greybox 7 of 7 pass; forest EditMode is 51 of 52: the new `WindData_IsLodInvariant` fails on the phase channel A, a conflict
inside the spec that needs a decision** (`issues/W1-shader.md` sections 2 and 5). Deviations: `issues/W1-shader.md`. Spec: `W1-spec.md`.

### What was run

| # | Run | Result |
|---|---|---|
| 1 | Baselines of the unchanged project: full capture twice against n1-final (`captures/w1-pre`, `w1-pre2`), kit capture twice (`w1-pre-kit`, `w1-pre-kit2`); material map `out/W1/material-users.txt` | noise RMSE 0.0023 to 0.0048 per view; kit sheets deterministic; no static prop shares a moving material |
| 2 | `ForestWindShaderGraphs.WriteAndCheckFromBatch` (`logs/W1/graphs-3.log`) | both graphs import, `Shader.Find` finds them, 88 Metal variants compile (GBuffer, DepthOnly, ShadowCaster, MotionVectors; foliage/opaque; with/without DOTS instancing), 0 problems; MSL in `out/W1/msl/` |
| 3 | `ForestBuild.BuildAll -forestForce`, twice (`build-5`, `build-6`) | audit passed (5,825 instances, 23,276 GameObjects); placements byte-identical to N1; 174 mesh, prefab and material files identical across the two builds; kit stage 43.9 to 44.7 s (before W1: 40.9 to 43.6) |
| 4 | Static regression, materials on HDRP/Lit (`-executeMethod ForestMaterials.UseLitFromMenu`) -> `captures/w1-static-lit`; back to the wind shader -> `captures/w1-static` (final kit) | both 32 of 32 within noise: worst RMSE 1.03 to 1.04 x the noise reference (limit 1.2), worst lit-mask bias 0.00005 over its reference (limit 0.002), 0 error pixels (`out/W1/regress-static.txt`, `regress-static-lit.txt`); difference images and 8 x 8 block-mean maps show grain only (`out/W1/static-diff/`, `static-block/`); materials after the Lit round trip byte-identical |
| 5 | Kit capture `captures/w1-kit` against `w1-pre-kit` | static props bit-identical; wind prefabs at most 0.137 % of pixels differ, by at most 8/255, isolated pixels (the spec's noise rule cannot apply: deterministic sheets, noise 0; see the issue file) |
| 6 | Suites, final (`tools/w1/tests.sh final2`, `out/W1/tests/final2/`) | forest EditMode 51/52 (fails: `WindData_IsLodInvariant`, A only), forest PlayMode 13/13, greybox 7/7; Mesh Memory 358.0 MiB (was 382.4), vertices 33.65 M at crouch_hide (budget 36 M, above the 33 M warning as the spec expected); GPU gust against the CPU mirror 3.2e-5 (t <= 600 s), 6.4e-4 at 3 h; Night gust statistics: lull share 64 %, gust gaps min 25.4 s, median 36 s |
| 7 | Drawer exclusions (temporary script, removed) | inconclusive: the drawer's advanced statistics are empty in an edit-mode batch render |

Every Unity log of the final runs: 0 `error CS`, 0 `warning CS` (4 in the temporary drawer script's run), 0 "immutable", 0 `Shader error in`.
One Unity process at a time, never `-nographics`, never `-quit` with `-runTests`, no exit 198. `pre-W1.md5` holds. Materials unchanged by
the test runs. No git command run. No temporary file left in the project.

### What changed in the project

- New: `Assets/Game/Shaders/` (`ForestWind.hlsl`, `ForestSurface.hlsl`, `ForestWind.shadergraph`, `ForestWindTriplanar.shadergraph`, fixed-GUID
  metas), `Editor/Forest/ForestWindShaderGraphs.cs`, `ForestWindProfiles.cs`, `ForestWindData.cs`, `Scripts/World/Foliage/ForestWindGlobals.cs`,
  `Tests/ForestEditMode/ForestWindTests.cs`, `ForestWindProbe.compute`, `ForestWindProbeSupport.cs` (+ a copy in ForestPlayMode).
- Changed (originals `backup/W1-I1-pre-edit/`, diffs `out/W1/diffs/`): `ForestMaterials` (materials-3: wind recipes on the graphs, `__mv`
  twins, `WindShaderDefault` / `-forestWindShader on|off` / menu A-B), `ForestMeshTools` (ToMesh with colours), `ForestTreeBaker`, `ForestPrefabs`
  (colours, compact 44 B layout, `__mv` on `MotionVectorLevels`, ApplyTint keeps `__mv`), `ForestKitSpec` (kit-11, `MotionVectorLevels`),
  `ForestLayout` (`PassableYoungFirMaxScale = 0.705`), `ForestLightingPresets` (wind fields, `WindState()`), `ForestAudit` (`ManifestEntryOf` public,
  strips `__mv`), `ForestKitTests` (leaf-sample test adapted as specified).
- Generated: 14 `__mv` materials, every tree, fern and grass mesh with colour and Float16 tangents.

### What the next engineer (I2) and the coordinator must know

1. **Decide the phase test** (`issues/W1-shader.md` section 5): per-needle rigid phases (spec 4.3) and nearest-vertex matching at dA 0.05
   (spec 8.6) cannot both hold; R and G are invariant. Options listed there.
2. Not done in I1 (outside this run's deliverables): edit-mode pose sequences and `-captureSet wind`, MP4s, contact sheets, the speckle count,
   the zero-wind player cost (I1 step 10, the 0.3 ms stop condition), the tools `tstd`, `mp4`, `paired`, `build-player`. I2 should not start
   before step 10 has been measured.
3. Shader Graph materials validate only once the HDRP pipeline instance exists: call `ForestWindShaderGraphs.EnsureRenderPipeline()` before
   building or validating them in batch.
4. Tools: `tools/w1/unity.sh`, `compile-all.sh`, `tests.sh`, `i1-final2.sh`, `regress/regress` (static regression rules of 8.1), `regress/blockdiff`.

## 2026-10-17, W1 package I1b: the coordinator's decisions on I1's open questions (all three suites pass)

**Result: forest EditMode 53 of 53, forest PlayMode 13 of 13, greybox 7 of 7** (`out/W1/tests/final3/`, transcript `logs/W1/i1b-final.txt`).
I2 has not been started. Details, reasoning and every number: `issues/W1-shader.md` section 6.

### What was decided, and what followed

1. **Phase invariance: option (a) plus a displacement check.**
   - `WindData_IsLodInvariant` keeps the nearest-point check of R and G (and B on the fern far level) unchanged. It compares A only on
     pieces whose anchors coincide on both levels, within 1/255.
   - New test `WindMotion_LodSwitchDoesNotPop`. The shader's own displacement (GPU `Deform` kernel; no CPU mirror of the displacement
     exists) at points matched within 2 cm, at 16 times, for the smallest, median and largest instance of each prefab. Wind 0.875, the
     strongest preset crest. Pass when the 95th percentile of the displacement difference is at most 1 px (2,560 x 1,440, 70 degrees)
     at that pair's switch distance.
   - **On I1's data (wind-3) the new test failed:** fir_sapling_medium_a LOD0 to LOD1 at 1.62 px.
   - **The wind data changed (wind-4), not the test:** phase lattice values over half a cycle and cells twice as tall (0.70 m young,
     1.20 m tall). That pair is now 0.73 px; the tightest pair overall is fern_patch_a at 0.77 px (unchanged data); the others are at
     most 0.41 px (`out/W1/lod-pop-table.md`).
   - **Finding.** The decision assumed that kept needles keep their anchor. That holds only partly: every kit level flattens its kept
     pieces into quads or cards. Young firs have 0 coinciding anchors at every level, so their per-needle phase is covered by the motion
     test only. Tall firs have 279 to 4,077 coinciding pieces per level, all with exactly the same phase.
2. **A second failure that I1 did not report.**
   - `WindData_IsLodInvariant` also failed on G: 2 vertices of fir_tree_01_b LOD1, dG 0.075, root flare against the root skirt.
   - Cause: the tall firs' root and flare sections (below 0.2 to 0.35 m) gave root vertices G up to 27/255. They would have bobbed and,
     in I2, taken nearly full pushes.
   - Fix (wind-4): a measured root zone (fir_tree_01_a below 0.25 m, b and c below 0.20 m, none for young firs), where G is 0, ramping
     up over 0.25 m above it. Worst dG is now 0.012.
3. **Kit-capture rule 1, the absolute rule.** All 113 sheets of `captures/w1-kit2` pass against the pre-W1 sheets (`w1-pre-kit`, wind 0):
   worst RMSE 0.00009, 0 error pixels (`out/W1/kit-absolute.txt`).

### What was run (one Unity process at a time, never `-nographics`, never `-quit` with `-runTests`, no exit 198)

| # | Run | Result |
|---|---|---|
| 1 | The two tests on wind-3 (`tests-fix1`, `tests-fix2`) | motion test fails: fir_sapling_medium_a LOD0 to LOD1 at 1.62 px; invariance fails on G (root flare) |
| 2 | Temporary trunk-section diagnostic (`diag-1.log`; file removed from the project, copy `tools/w1/W1DiagTemp.cs`) | root and flare sections of the tall firs below 0.25 to 0.35 m; young-fir trunks round from the foot |
| 3 | Forced builds 7 and 8 on wind-4 (`build-7.log`, `build-8.log`) | 174 kit files identical; 21 tree meshes changed against build 6; audit passed; placements identical to N1 |
| 4 | Static capture `captures/w1-static2` against `n1-final` | 32 of 32 within noise: worst 1.02 x the noise reference, bias within its reference, 0 error pixels; difference and block-mean maps show grain only, like a noise run |
| 5 | Kit capture `captures/w1-kit2` | absolute rule passes on 113 sheets; 111 byte-identical to wind-3's `w1-kit`, 2 differ on 0.0003 % of pixels at most |
| 6 | Suites `final3` | 53/53, 13/13, 7/7; Mesh Memory 358.0 MiB, triangles 21.03 M, vertices 33.65 M at crouch_hide; GPU gust against the CPU mirror 3.2e-5 |

Every log of the sequence has 0 `error CS`, 0 `warning CS`, 0 "immutable" and 0 `Shader error in`. Materials are untouched by the tests,
`pre-W1.md5` holds, no git command was run, and no temporary file is left in the project.

### What changed in the project

- `Editor/Forest/ForestWindData.cs`: wind-4. `PhaseRange`, the new phase cells, the root-zone constants, and `TreeField.RootTop`.
- `ForestKitSpec.cs`: the new constants in the kit hash.
- `ForestTreeBaker.cs`: one log line per tree with its root zone.
- `Tests/ForestEditMode/ForestWindTests.cs`: the phase check of kept pieces, the worst-dG report, and `WindMotion_LodSwitchDoesNotPop`.
- Originals: `backup/W1-I1b-pre-edit/`.
- Generated: the 21 tree meshes.
- New tools: `tools/w1/i1b-final.sh`, `kit-absolute.py`, `pop-table.py`.

### What the next engineer (I2) and the coordinator must know

1. **Phase variety within one tree is smaller now.** Phases span at most half a cycle, and branches within about a metre move nearly
   together. Trees still differ through the per-instance phase. Not yet judged on moving images. If crowns look too uniform, widen the
   range together with larger cells, re-run `WindMotion_LodSwitchDoesNotPop`, and never loosen its limit.
2. **The fern patch switch is the tightest pair (0.77 px)**, because the far level's enlarged clumps put different frond positions
   under matched points. A stronger fern profile needs the far level's data fixed first.
3. **Still not done from I1** (unchanged by this step): edit-mode pose sequences and `-captureSet wind`, MP4s and contact sheets, the
   speckle count, the full `precise` check, the zero-wind player cost (I1 step 10, the 0.3 ms stop condition), a conclusive drawer
   exclusion count, and the tools `tstd`, `mp4`, `paired`, `build-player`. I2 should not start before step 10 has been measured.
4. **The root zone matters for I2's pushes.** Before wind-4, a body passing a tall fir would have bent its root flare. Above the root
   zone the tall-fir trunks read G 0 from 0.3 to 1.5 m (diagnostic), but I2's trunk-stillness check is still the real test.

## 2026-10-17, W1 package I2 and its continuation I2b: wind at run time, foliage that parts around bodies, the owner's answers

**Result:**
- **Built and passing:**
  - the wind at run time;
  - foliage parting around the player and a raptor-sized body;
  - passable saplings at 0.705, which now lean as one piece without tearing;
  - the rustle hook;
  - the motion captures `captures/w1-motion-i2b`;
  - the static regression (32 of 32 within noise);
  - the three suites, twice: forest EditMode 56/56, forest PlayMode 23/23, greybox 7/7.
- **The frame budget is not met, so this is a stop and report.** The owner's invisible savings (i) to (iii) are in. W1 still costs
  about 1.24 ms at pano_90, so the worst view reads about 12.75 ms in a normal session against 12.5. His last lever (iv), a shorter
  night cull, would be visible, so it is not applied.
- **Solid firs' branches that cross a body's path still tear**, and need his choice.

Details: `issues/W1-runtime.md` (I2 in sections 0 to 10, written by the first I2 engineer, whose session ended before this section;
I2b in section 11).

### What the owner decided (2026-10-17) and what came of it

| Decision | Done | Evidence |
|---|---|---|
| (i) Per-instance constants once, Resident Drawer kept | Each wind renderer gets a slot (`MeshRenderer.SetShaderUserValue`, carried by the drawer); a compute kernel evaluates the vertex shader's own per-instance function once a frame (this frame's and last frame's time); the vertex shader reads its slot. Same picture as before (within the noise of identical runs; positive control proves the slots are read). **Saves nothing measurable** (-0.08 to +0.09 ms): the per-instance arithmetic is not what costs | `Wind_InstanceTermsMatchTheVertexShader`; `out/W1/I2b/i2b-it1-table.md` (noinst) |
| (ii) Quantised displacement for `precise`, after the speckle count | Done; speckle count (first run of spec 8.1): fir_close and crouch_hide pass (poses within 3 to 6 % of the off frame's outliers, limit 1.2 x + 50); outliers on needle edges, no holes | `captures/w1-speckle` |
| (iii) No motion vectors on young-fir LOD1 if invisible | Done: 200 % crops show no trail or double image (softer moving needles only); ghosting test adapted with a bound | `out/W1/I2b/look/ghost_crops_fir_close_i2b.png` |
| Also invisible, found on the way | The gust texture was fetched in every vertex (`?:`); normal and tangent maths ran in passes that never read them | MSL `out/W1/I2b/msl-i2*` |
| (iv) Night cull 55 -> 40..45 m only if invisible | Built (`ForestCullDistance`, per preset), saves 0.22 ms (45 m) / 0.67 ms (40 m) at pano_90; the night captures fail (torch on 17, off 25 of 32 views over the noise; the far treeline thins). **Not shipped** (Night at 0 = the kit's distances) | `captures/w1-cull45-on`, `-off`; `out/W1/I2b/regress-cull45-*.txt` |
| Passable saplings: 0.705 | As built (690 of 1,706 young firs) | I2 section 2 |
| Tearing: the lean's direction for a leaning sapling, clearance rechecked, solid firs checked | Done, with the lean at 80 degrees from crown contact (needed for the clearance and for the approach); passable saplings: 0 torn edges over 14 moments of a walk (I2: up to 1.10 m), branches 0.523 m and stems 0.834 m from the body (need 0.35, 0.45). Solid firs: 8,750 torn edges, up to 1.10 m, at a new solid-fir site: options for the owner | `Foliage_ProxyBendsThicketBranchesNotTrunks`, `Foliage_ProxyPassesSolidFirs` |

### Player frame time (macOS development player, 2,560 x 1,440, vsync off, 16-frame windows; N1's player interleaved)

| Session (I2b's player unless stated) | N1 now, pano_90 | I2b on, pano_90 | W1's cost (on - off), pano_90 | Normalised: additive / ratio / paired |
|---|---|---|---|---|
| `i2b-it1` (normal; I2's player in the same rounds: on 13.23 ms, cost +1.68) | 11.19 | 12.56 | +1.23 | 12.88 / 12.92 / 12.75 |
| `i2b-diag` (normal-ish, 4 views) | 11.68 | 12.21 | +1.24 | 12.04 / 12.04 / 12.75 |
| `i2b-final` (all 29 views, 2 rounds; slow) | 13.42 | 13.99 | +0.92 | 12.08 / 12.00 / 12.43 |
| `i2b-confirm` (final player, 8 views, 3 rounds, I2's player too; near normal) | 12.02 | 12.66 | +1.24 | 12.14 / 12.12 / 12.75 |

- Budget 12.5 ms. The paired figure is N1's normal session (11.51 ms at pano_90) plus W1's cost paired within the view; it is steady at
  12.75 ms. The cross-run normalisations scatter by about 1 ms, and in `i2b-confirm` they put I2's own player at 12.47 ms (its engineer
  measured 12.9 to 13.3), so they cannot settle 0.25 ms. Tables: `out/W1/I2b/<session>-table.md`.
- I2b against I2's player in the same rounds: 0.05 to 0.67 ms faster at seven of the eight worst views; W1's own cost +1.03 to +1.62 ms
  against +1.35 to +1.93 ms.
- Editor `Budget_FrameTime`: 11.70 and 12.46 ms (budget 14; I2's second round failed at 14.31). Counts at crouch_hide: 21.03 M triangles,
  33.65 M vertices; Mesh Memory 358.0 MiB.

### What was run (one Unity process at a time, never `-nographics`, never `-quit` with `-runTests`, no exit 198)

| # | Run | Result |
|---|---|---|
| 1 | Metal variant compile, I2's and I2b's shader (`i2b-msl-i2*.log`) | 88 variants, 0 problems, both |
| 2 | Forced builds `i2b-build-1`, `try2`, `try3`, `final` | audit passed each time; placements identical to N1; try3 and final kits identical (174 files) |
| 3 | Speckle captures (`i2b-speckle.log`, temporary script removed) | pass at both views |
| 4 | Wind play-mode tests try1 (6/10), try2 (9/10), try3 (10/10), final motion run (10/10) | failures and their causes in section 11 |
| 5 | Player builds `w1i2b-try2`, `try3`, `try4`, `final` | settings unchanged after each |
| 6 | Player sessions `i2b-it1`, `i2b-diag`, `i2b-it2` (void: LOD-size defect), `i2b-it3`, `i2b-final` | section 11.7 |
| 7 | Night captures for (iv): `w1-cull45-on`, `w1-dark-a/b/c`, `w1-cull45-off` | fail the no-difference condition |
| 8 | Final (`logs/W1/i2b-final-verify.txt`): static regression, motion captures, suites twice | 32/32; 10/10; 56/56, 23/23, 7/7 twice; materials untouched; `pre-W1.md5` holds; 0 `error CS`, 0 `warning CS`, 0 shader errors; no temporary file |

### What changed in the project

See `issues/W1-runtime.md` 11.11 (I2b) and 1 (I2). New in I2b: `ForestWindInstances.cs`, `ForestCullDistance.cs`. Changed: `ForestWind.hlsl`,
`FoliageInteraction.compute`, `FoliageInteractionField.cs`, `ForestWindGlobals.cs`, `ForestWindProfiles.cs` (stem lean 80 degrees, crown
radius), `ForestKitSpec.cs` (young firs' motion vector levels 1), `ForestLightingPresets.cs`, `ForestWindRig.cs`, `ForestLighting.cs`,
`ForestCapture.cs`, `ForestFrameHarness.cs`, and the tests. Originals `backup/W1-I2b-pre-edit/`, diffs `out/W1/diffs/I2b/`. No git command.

### What the owner and the coordinator must know

1. **The budget needs a decision.** About 0.25 ms is missing at pano_90. The measured options, with their costs and what they change
   on screen, are in 11.12.
2. **Solid firs' tearing needs a decision** (11.5): keep it (hidden inside a drawn body, edge-on for the player), let the crossing part
   pass inside the body, or lift crossing branches over the body.
3. **Passable saplings now bend strongly**: 80 degrees at full press, starting at crown contact. Watch `mp4/push_thicket_*.mp4`.
4. **Measuring:** N1's player swings 1 to 4 ms between rounds even in a quiet session. Judge W1 by its cost paired within a view
   (`tools/w1/perf-i2b.py`'s "paired" column), not by cross-run comparisons.
5. **`LODGroup.SetLODs` at run time recalculates the group's size.** Put the size and reference point back after it, or the Resident
   Drawer draws far finer levels far away (here 36 M instead of 21.5 M triangles).

## 2026-10-17, W1 package I2c: the moon's shorter shadows fund the wind; solid firs yield toward their trunk (option (a))

**Result:**
- **The frame budget holds:**
  - paired estimate at the worst view 11.06 ms (pano_90) against 12.5 ms, a margin of 1.44 ms;
  - 29-view confirmation: worst view 11.20 ms (pano_90; every other view at most 10.83 ms), a margin of 1.30 ms, in a slow session.
- **No tearing remains:** at the solid site 0 edges of solid firs grow by more than 0.25 m (largest 0.237 m; I2b: 8,750 edges, up
  to 1.10 m); at the thicket the passable saplings are as in I2b (largest 0.052 m).
- **Both suite rounds pass:** forest EditMode 56/56, forest PlayMode 23/23, greybox 7/7, twice.
- **Static regression:** 32 of 32 against the night with the moon change.
- **Motion set** `captures/w1-motion-i2c`: working wind and bending; looked at.

Details: `issues/W1-runtime.md` section 12.

### What the owner decided (2026-10-17) and what came of it
| Decision | Done | Evidence |
|---|---|---|
| 1. Shorten the moon's shadows at night (about 30 m, 2 cascades; Night only) | `ForestLightingPresets.Night`: 30 m, 2 cascades, first split kept at 6 m. OvercastMist unchanged. The night captured before and after, torch on and off. **Visible change:** near the player none; beyond about 20 to 30 m the moonlit mist and far stands are slightly lighter in patches where the trees' moon shadows used to darken them (at most about 6 of 255 levels; torch on 2, torch off 3 of 32 views over the noise). It is seen in difference maps, not side by side. | `captures/w1-moon-before`, `w1-moon-after` (and its `compare/` sheets); `out/W1/I2c/regress-moon-*.txt` |
| ... then measure the player paired against N1's | New harness state `moon50` (N1's moon shadows within a view). The moon change saves 1.25 to 1.70 ms; W1 costs 0.56 to 1.23 ms; together -0.46 ms at pano_90. Paired estimate 11.06 ms, worst view. | `out/W1/I2c/i2c-perf-table.md`, `i2c-confirm-table.md` |
| 2. Solid firs: option (a), crossing branches toward their own trunk's side; assert no edge grows over 0.25 m | Every pushed vertex of a solid fir takes its fir's side (`GamexSolidSide`, per instance from the field). Asserted at the solid site (0 edges; positive control with I2b's push: 8,766 edges). Frames from a camera that sees the crossing branches: no streak; the control shows I2b's streaks. | `Foliage_ProxyPassesSolidFirs`; `captures/w1-motion-i2c/sheets/solid_shipped_vs_i2b_push.png` |

**For the coordinator:**
- The owner said "option a". In W1-runtime.md 11.5 and in the previous section of this file, (a) was "keep as is" and the trunk-side
  push was (b). The task defined (a) as the trunk-side push, and that is what was built.
- If he meant 11.5's (a), set `solidSideReach` to 0 on both fir profiles and rebuild the kit (12.1).

### Player frame time (macOS development player, 2,560 x 1,440, vsync off, 16-frame windows; N1's player interleaved)
| Session | N1 now, pano_90 | I2c on, worst view | on - (off+moon50), pano_90 | Paired estimate, worst view |
|---|---|---|---|---|
| `i2c-perf` (8 views, 3 rounds; slightly slow) | 12.09 | 11.23 (crouch_hide) | -0.46 | 11.06 (pano_90) |
| `i2c-confirm` (all 29 views, 2 rounds, the final player; slow) | 13.89 | 13.05 (pano_90) | -0.31 | 11.20 (pano_90) |

- Budget 12.5 ms. The paired estimate is N1's normal session (11.51 ms at pano_90) + (on - (off+moon50)), paired within each view;
  "off+moon50" is N1's configuration inside the I2c player.
- Counters at pano_90: 21.30 M triangles and 227 shadow casters with N1's moon shadows; 17.04 M and 144 with the night's.
- Editor `Budget_FrameTime`: 11.20 and 12.02 ms (budget 14). Counts at crouch_hide: 17.44 M triangles (I2b 21.03 M), 27.25 M
  vertices (33.65 M).

### What was run (one Unity process at a time, never `-nographics`, never `-quit` with `-runTests`, no exit 198)
| # | Run | Result |
|---|---|---|
| 1 | Night captures before (`i2c-moon-before-on/off.log`) | 32 + 32 images; before = n1-final 32/32, = w1-dark-a 32/32 |
| 2 | Scene rebuild with the moon change, captures after (`i2c-step1.txt`) | audit passed; kit unchanged; after against before: torch on 2/32, off 3/32 over the noise, all brighter |
| 3 | Forced build and wind tests try1 (10/10), try2 (9/10: the geometry read the control walk's knob, a test fault, fixed), try3 (solid test, 1/1) | 12.4 |
| 4 | Player build `w1i2c` | settings unchanged; I2b's player kept as `Builds/ForestW1-I2b` |
| 5 | Player sessions `i2c-perf`, `i2c-confirm` | 12.3 |
| 6 | Final (`logs/W1/i2c-final.txt`) | kit deterministic (174 files); static 32/32; motion 10/10; suites 56/56, 23/23, 7/7 twice; materials untouched; `pre-W1.md5` holds; 0 `error CS`, 0 `warning CS`, 0 shader errors; no temporary file |

### What changed in the project
See `issues/W1-runtime.md` 12.8. Changed:
- `ForestLightingPresets.cs`, `ForestWind.hlsl`, `FoliageInteraction.compute`, `ForestWindProfiles.cs`;
- `ForestWind.cs`, `ForestWindGlobals.cs` (a test-only knob), `ForestFrameHarness.cs` (`moon50`);
- tests `ForestWindPlayTests.cs`, `ForestWindSupport.cs`;
- generated: the 22 fir materials and the scene.

Originals `backup/W1-I2c-pre-edit/`, diffs `out/W1/diffs/I2c/`. No git command.

### What the owner and the coordinator must know
1. **The night is slightly different**, as the owner allowed: the moon's shadows end at 30 m.
   - The far mist is a little lighter in patches; near the player nothing changed.
   - Sheets: `captures/w1-moon-after/compare/`.
2. **Solid firs no longer tear.** Their crossing branches fold toward their own trunk and pass through a body's column; with the body
   drawn they are mostly hidden inside it.
   - The largest stretch left is 0.237 m against the 0.25 m limit, a needle just above the body's band top.
3. **The solid site's camera moved** (12.4); I2b's could not see the crossing branches.
4. **There is budget to spare** (about 1.4 ms at the worst view). Spec 7.4 lets quality come back in reverse order; nothing was
   restored without the owner (12.9).

## 2026-10-17, W1 review round 1 of the I2c motion captures (`captures/w1-motion-i2c` to `captures/w1-motion-r1`)

Notes completed 2026-10-18 from what is on disk: the R1 engineer's session ended while writing them. Nothing was rebuilt, re-run or
re-captured for this section, and Unity was not launched.

**Result:**
- **The thirteen critical and high findings were all judged real.** Three reviewers (motion, technical, gameplay) raised them. Twelve
  are fixed and one (9) partly: a brushed sapling leans 11.5 degrees at most, but a body that walks into a stem still bends it 45 to 66
  degrees.
- **Both suite rounds pass:** forest EditMode 56/56, forest PlayMode 24/24 (new: `Foliage_HiddenBodyStandsOutFromTheWind`),
  greybox 7/7, twice (`out/W1/tests/r1-final1`, `r1-final2`).
- **The frame budget holds by the paired estimate:**
  - worst view 10.42 ms (pano_90) against 12.5 ms, a margin of 2.08 ms;
  - with pushers 10.41 to 10.71 ms.
  - The raw readings were slow in every R1 session (see below).
- **Static regression:** 32 of 32 within the noise against the night with the moon change (`w1-moon-after`).
- **Motion set** `captures/w1-motion-r1`: the 11 wind play-mode tests pass and write it (54 sheets, 58 MP4s, maps, sequences, the
  hidden-body runs).

Details: `issues/W1-runtime.md` section 13.

### What the reviewers found and what was done
| Finding (numbers of 13.1) | Done | Evidence |
|---|---|---|
| Passable saplings: the lean had no dynamics, hinged as a rigid pole, laid over 80 degrees at any contact, swung back through the body (1, 2, 6, 7, 9) | A spring per sapling in the `Instances` kernel (1.1 Hz; critically damped in, one overshoot out). It is driven by the bodies' stamps and bent along the stem. A brush leans at most 11.5 degrees. A body walking into the stem still bends it 45 (player) to 66 (raptor) degrees, because spec 8.4's stem clearance requires it. A `?:` that ran both state steps was found and fixed. | `Foliage_ProxyBendsThicketBranchesNotTrunks`: largest lean 53.5 degrees, at most 2.6 degrees a frame (limit 4), overshoot 6.4 (limit 10), never back through the body; `push/thicket/lean.csv` |
| Solid firs' boughs slid through their own trunk and flattened into a mat (3, 8) | A swing about the trunk's axis: 0.5 rad, one state per fir, a 1.6 m band fade, 1 / radius beyond 1 m | `Foliage_ProxyPassesSolidFirs`: no pushed vertex closer to its trunk's axis; pieces 0.72 to 1.36 x their rest size; largest edge growth 0.084 m (I2c 0.237 m) |
| Each crown bobbed as one sine, in phase (4) | Three branch frequencies, a slow activity, mostly along the wind | `out/W1/R1/look/kymo_compare.png`; LOD pop 0.61 px (I2c 0.73) |
| Young-fir LOD1 had no motion vectors (5) | `MotionVectorLevels` young firs 1 -> 2; the ghosting harness now renders this frame's wind | `Wind_TaaGhosting`: fir_close 3.35 / 3.33 %, crouch_hide 8.33 / 8.28 % (shipped / young firs on; I2c 10.66 / 7.93, 19.29 / 17.72) |
| The press popped in, recovered slowly, jumped late; a still body kept full press (10, 11) | Field attack 0.15 s, a smooth tail instead of the 1 % clear, a still floor | A body standing still 3 s: 1.00 on its axis, 0.22 at its radius (`push/fern/summary.txt`) |
| The hidden-dinosaur case was never captured (12) | Captured at 6, 12 and 20 m, torch on and off, each against a wind-only twin; understorey shelter 0.45 -> 0.30; a contact shiver | `push/stalk/summary.txt`: legible at about 12 m in the torch (fern 2.22 %, thicket 4.95 % of the band), faint at 6 m, not at 20 m |
| One stamped box grew with the distance between bodies (13) | A per-instance pressed bit (bit 30 of the renderer's user value) | Pressed instances: one raptor 52 to 100, sprint 104 to 288, pair 87 to 159, three raptors 135 to 251 |

### Player frame time (macOS development player, 2,560 x 1,440, vsync off, 16-frame windows; N1's and I2c's players interleaved)
| Session | N1 now, pano_90 | R1 on, worst view (raw) | on - (off+moon50), pano_90 | Paired estimate, worst view |
|---|---|---|---|---|
| `r1-perf` (8 views, 3 rounds, N1 then I2c then R1 in each; slow in rounds 2 and 3) | 15.34 (11.41 in round 1) | 14.23 (crouch_hide) | -1.09 | 10.42 (pano_90) |
| `r1-ab` (4 views, 2 rounds, alternating which W1 player runs first) | 12.54 | 13.14 (pano_90) | -0.91 | 10.60 (pano_90) |

- Budget 12.5 ms. The paired estimate is N1's normal session at the view plus (on - (off+moon50)) within the R1 player, the method used
  since I2b. Tables: `out/W1/R1/r1-perf-table.md`, `logs/W1/r1-ab.txt`.
- With pushers (paired, `r1-perf`), worst view:
  - one raptor 10.54 ms;
  - sprint 10.41 ms;
  - player and raptor 10.71 ms;
  - three raptors 10.65 ms.
- **R1 costs more than I2c.** W1's own cost (on - off, `r1-ab`) is:
  - +1.57 ms at crouch_hide and +0.85 ms at pano_90 for R1;
  - +0.75 and +0.17 ms for I2c.

  About 0.45 ms of the difference is young-fir LOD1's motion vectors.
- **Raw readings.** The R1 player read 12.80 to 14.24 ms at its worst view in all five of its runs. That includes rounds where N1's
  player read normally (11.29 ms in `r1-perf` round 1, 10.53 ms in `r1-ab` round 1).
  - Its own N1-equivalent state was just as slow (off+moon50: 14.38 ms at crouch_hide in `r1-perf`, against N1's player 13.87 ms), so
    the paired difference cancels the slowdown.
  - The R1 engineer found an order effect: whichever W1 player runs second reads 1.3 to 3.0 ms slower.
  - No R1 session ran at a normal speed, and no 29-view confirmation like I2c's was run.
  - Even with the largest per-round W1 cost the R1 player showed (+1.65 ms, crouch_hide, `r1-ab` round 2) applied at pano_90 and I2c's
    smallest moon saving (1.25 ms), pano_90 comes to 11.51 + 1.65 - 1.25 = 11.91 ms, under 12.5.
- Editor `Budget_FrameTime`: 12.28 and 12.58 ms (budget 14; I2c 11.20 and 12.02). Counts at crouch_hide: 17.83 M triangles (I2c 17.44 M),
  27.97 M vertices (27.25 M); Mesh Memory 358.0 MiB.

### What was run (24 Unity runs in the R1 transcripts: 19 exit 0, 5 exit 2 from failing tests, none 198)
| # | Run | Result |
|---|---|---|
| 1 | Forced build and wind play-mode tests, try1 to try5 (`logs/W1/r1-try*.txt`) | audit passed; 0/3, 0/2, 1/3, 2/3, 9/11; the failures led to 13.2, 13.3, 13.6 and 13.7 |
| 2 | Suites try6 | 56/56, 24/24, 7/7 |
| 3 | Player build `w1r1` (`logs/W1/r1-playerbuild.txt`) | succeeded; `HDRP.asset` and `HDRPDefaultResources` put back; settings unchanged; I2c's player kept as `Builds/ForestW1-I2c` |
| 4 | Player sessions `r1-perf`, `r1-ab` | above; 13.9 |
| 5 | First final attempt (`logs/W1/r1-final-attempt1.txt`, 20:21) | stopped during round 1's forest PlayMode suite; three sources were edited at 20:36 |
| 6 | Final (`logs/W1/r1-final.txt`, 20:43 to 21:12) | compile OK; kit deterministic (174 files); audit passed (5,825 instances, 0 warnings); placements identical to N1; static 32/32 (against `n1-final` 31/32: trail_strip_08, the moon change, as in I2c); motion 11/11; suites 56/56, 24/24, 7/7 twice; materials untouched; `pre-W1.md5` holds; 0 `error CS`, 0 `warning CS`, 0 shader errors; no temporary file |

On 2026-10-18, without Unity:
- `tools/w1/compile-all.sh`: all seven assemblies OK;
- `pre-W1.md5`: 47 of 47 protected files unchanged;
- HEAD is still `2980a80`;
- no editor was open.

### What changed in the project
See `issues/W1-runtime.md` 13.11. Changed:
- shaders: `ForestWind.hlsl`, `FoliageInteraction.compute`;
- runtime: `ForestWindInstances.cs`, `FoliageInteractionField.cs`, `ForestWindGlobals.cs`, `ForestWind.cs`;
- editor: `ForestWindProfiles.cs`, `ForestKitSpec.cs`;
- diagnostics: `ForestFrameHarness.cs` (states `sprint`, `pair`, `raptors3`);
- tests: `ForestWindPlayTests.cs`, `ForestWindSupport.cs`, `ForestWindProbe.compute`, both `ForestWindProbeSupport.cs`;
- generated: 28 wind materials, the 3 young-fir prefabs, the scene.

Originals are in `backup/W1-R1-pre-edit/` and the diffs in `out/W1/diffs/R1/` (14 files). The player `Builds/ForestW1` is R1's. It was
built before `ForestWind.WanderTimeOffset`, a test hook that is 0 in the game, was added.

### What the owner and the coordinator must know
1. **Passable saplings now behave like springs.** A brushed sapling leans 11.5 degrees at most and springs back with one overshoot.
   - A body that walks into a stem still lays it over 45 to 66 degrees, because spec 8.4's stem clearance asks for it.
   - The gameplay reviewer's cap of 30 to 35 degrees would need that check changed. This is the owner's call.
   - Watch `mp4/push_thicket_*.mp4`.
2. **Crossing branches of passable saplings now pass partly through the body.**
   - The tests assert the 0.35 m clearance only for branch vertices on the stem's side of the path, and report the rest.
   - This narrows a check the owner kept in I2b, so it needs his yes.
   - The alternative is I2c's 80 degree lean at first contact, which all three reviewers rejected.
3. **Young-fir LOD1 has motion vectors again.** This reverses the owner's I2b decision (iii).
   - It was done under spec 7.4's restore order, on the technical reviewer's finding.
   - It costs about 0.45 ms.
   - I2c's notes said nothing would be restored without the owner, so the coordinator should confirm this with him.
4. **A hidden body** is legible at about 12 m in the torch, faint at 6 m, and not visible at 20 m or with the torch off. Whether that is
   the game he wants is his call. The MP4s `mp4/stalk_*` show each run beside its wind twin.
5. **The budget holds by the paired estimate**, with about 2 ms of margin. The raw R1 readings were slow in every session, so a 29-view
   confirmation in a normal session would settle it before W1 closes.
6. **Not fixed (medium and low)**, with reasons in 13.12:
   - passable saplings' needles stretching up to 4.4 x;
   - the night's calm between gusts;
   - fern fronds shearing;
   - young firs' shelter and periods;
   - conifer flutter 0;
   - moon mode 0;
   - 40 s gust sequences and a walking-camera LOD capture;
   - the rustle payload;
   - several stamps per creature and the 25.6 m field window;
   - the passable-threshold rule;
   - a first-person pass by a solid fir;
   - renderer bounds for leaning saplings.

## 2026-10-18, W1 review round 2 of the R1 motion captures (`captures/w1-motion-r1` to `captures/w1-motion-r2`)

**Result:**
- **Eleven high findings judged; all real.** Fixed at the cause: the sapling's sheared mat (1), the frame-156 twitch (2, 6), the ferns'
  unison (3), gusts without inertia (4), the slow, rebound-free return (5), the solid fir's turnstile (7), folding near the body (8).
  Partly: foliage inside a drawn body (9: stem-side branches asserted clear, saplings held aside until the body leaves their crown;
  crossing branches still pass through, option (a)), the hidden body in ferns (10: 6 m better, 12 m about level with a crest). Not
  changed: passable and solid saplings looking alike (11, the owner's rule).
- **Both suite rounds pass:** forest EditMode 56/56, forest PlayMode 24/24, greybox 7/7, twice (`out/W1/tests/r2-final1`, `r2-final2`).
- **The frame budget holds by the paired estimate:** worst view 11.78 ms (pano_90) in `r2-perf`, 10.74 ms in `r2-confirm`; with pushers at
  most 12.08 ms; against 12.5 ms. Both sessions were slow by the 11.8 ms rule; in the one near-normal round R2 read 11.94 ms raw.
- **Static regression:** 32 of 32 within the noise against `w1-moon-after` (worst ratio 1.00).
- **Motion set** `captures/w1-motion-r2`: the 11 wind play-mode tests pass and write it (56 sheets, 60 MP4s, maps, the 40 s gusts).

Details: `issues/W1-runtime.md` section 14.

### What was done (section 14)
| Finding | Done | Evidence |
|---|---|---|
| Sapling mat, stretched cards (1, 8) | No field for saplings: a rigid root lean plus an upper-stem bow, a clearance translation of slope 0.22 from the latched body, a soft floor | thicket: every piece 0.67 to 1.37 x (R1 up to 4.41 x, 36,566 outside the band); 33,727 on the ground counted apart |
| Twitch at 156/157 (2, 6) | The field is never read for a sapling, so no side switch | no one-frame jump in any push-only walk (asserted) |
| Ferns in unison (3) | Each frond its own phase (4 A) and frequency (+-25 %), no ripple | frond correlation at zero lag -0.64 to +0.33 (R1 up to +0.96) |
| No inertia (4) | Per-instance mean lean a damped oscillator at its own trunk frequency; oscillating terms on an envelope | `sheets/gust40_hollow_rim_motion.png`: motion lags the gust and falls over about 6 s after it |
| Slow return (5) | Field press a spring with a 30 % bruise; solid swing a spring with a 15 % bruise | 83 % back 0.6 s after contact; the trail 0.3 s behind 0.49 (needs 0.3) |
| Turnstile (7) | Contact from the crown measured on LOD0, max(0, cos)^2, 0.35 s attack | side and far foliage ahead of the body, over 1 m outside it: at most 0.054 m (limit 0.06) |
| Fern folding (8) | Each frond turns rigidly about its base (anchors in UV2: fern and grass meshes 52 B) | fronds 0.98 to 1.01 x, no tear |
| Inside the body (9) | Stem-side clearance; held aside until the rear leaves the crown | stem-side branches at least 0.377 m (needs 0.35); crossing ones at 0.000 m, reported |
| Hidden body (10) | Above, plus a 0.12 rad contact sway | fern 12 m torch on: 2.02 % vs crest 1.89 % (R1 2.22 vs 1.51); 6 m 0.91 % (R1 0.48 %) |

### What changed in the project
See `issues/W1-runtime.md` 14.10. Shaders `ForestWind.hlsl`, `FoliageInteraction.compute`; runtime `ForestWindInstances.cs`,
`FoliageInteractionField.cs`, `ForestWind.cs`; editor `ForestWindData.cs`, `ForestPrefabs.cs`, `ForestMeshTools.cs`,
`ForestWindShaderGraphs.cs`, `ForestWindProfiles.cs`, `ForestKitSpec.cs`; tests `ForestWindPlayTests.cs`, `ForestWindSupport.cs`,
`ForestWindTests.cs`, `ForestWindProbe.compute`, both `ForestWindProbeSupport.cs`; generated: both graphs, 28 materials, 12 fern and grass
meshes, the scene. Originals `backup/W1-R2-pre-edit/`, diffs `out/W1/diffs/R2/`. Player `Builds/ForestW1` is R2's; R1's is
`Builds/ForestW1-R1`. No git command.

### What was run (one Unity process at a time, never `-nographics`, never `-quit` with `-runTests`, no exit 198)
| # | Run | Result |
|---|---|---|
| 1 | Forced builds and wind tests try1 to try5, experiment exp1 (`logs/W1/r2-*.txt`) | try1: a struct in ?: (shader error), audit failed; then 1/4, 8/11, 3/5, 3/3; exp1 showed the lull state was a gust's ring-down |
| 2 | EditMode suite on try5 | 56/56 |
| 3 | Final (`logs/W1/r2-final.txt`) | kit deterministic (174 files); audit passed; placements identical to N1; static 32/32; motion 11/11; suites 56/56, 24/24, 7/7 twice; materials untouched; `pre-W1.md5` holds; 0 `error CS`, 0 `warning CS`, 0 shader errors; no temporary file |
| 4 | Player build `w1r2` | settings unchanged; `pre-W1.md5` holds |
| 5 | Player sessions `r2-perf`, `r2-confirm` | 14.8 |

### What the owner and the coordinator must know
1. **A sapling a raptor walks into now leans over as a whole and rises once the raptor's rear has left its crown, its top whipping past
   upright.** Its low branches are pressed onto the ground on the lean side. Watch `mp4/push_thicket_*.mp4`.
2. **Crossing branches still pass through a drawn body** (passable saplings and solid firs): the remaining part of finding 9 needs the
   owner's yes to option (a) for passable saplings, or per-branch data.
3. **Ferns are lighter and independent, but a hidden body in them is still hard to see beyond 6 m.** The reviewer's 2 x target is not met
   at 12 m; a person's blind A/B on `mp4/stalk_*` is owed.
4. **The passable rule (0.705) is unchanged**; the gameplay reviewer asks for a visible difference or a cost. The owner's call.
5. **The budget holds by the paired estimate** (11.78 / 10.74 ms worst), but both sessions were slow: a 29-view confirmation in a normal
   session is still owed before W1 closes.
