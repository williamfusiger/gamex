# D2: dense forest (density pass), measured in a macOS development player

2026-10-15. Brief: `build/BRIEF-density.md`; coordinator's budget decision: a real macOS development player at 2560 x 1440,
vsync off, the worst of the standard views, median <= 12.5 ms and p95 <= 15 ms; the Editor stopwatch stays as a secondary
number and `Budget_FrameTime` is re-based on the measured relation. `B` = `SCRATCH/build`, `P` = the project.

## 1. Result

- **Density (final plan, core of 1.87 ha)**: tall firs 152 -> 529 (81 -> **282 a hectare**, 30 % a younger cohort); young firs
  530 -> 1,244 (283 -> **664 a hectare**, thickets, clumps and understorey in the gaps); fern coverage 10.9 -> **26.4 %** of the
  core (2.4x; 12.0 patches per 100 m2 under tall crowns against 5.5 elsewhere); logs / stump patches / branches 8 / 10 / 127 ->
  **22 / 23 / 309**; the clearing a glade of 10 m. The bank's wall is unchanged in kind. Mist 70 -> 20 m, every class culled at
  50 to 55 m.
- **Sightlines** (porous model of M4 review 2, eye level): from the trail, clear beyond 30 m **58 -> 17 %**, beyond 15 m 82 -> 47 %;
  from 200 random core points, beyond 30 m **42 -> 8 %**, beyond 15 m 70 -> 33 % (target under 25 % beyond 30 m: met).
- **Player (macOS development build, 2560 x 1440 full-screen window, vsync off, 29 views)**, interleaved with the sparse build in
  the same session: worst view pano_90 in every run. 16-frame-window median **7.58 ms before -> 9.71 ms after** (per-run worst
  9.56 to 10.48 ms in 7 runs), 16-frame p95 **9.04 -> 12.32 ms** (per run 11.60 to 12.86), mean 7.56 -> 9.75 ms: inside the
  12.5 / 15 ms budget. The per-frame p95 of single frames is 23 to 45 ms in every run of both builds, because all runs of this
  session presented in bursts (section 2); no steady run of the final build was obtained in 7 attempts (11 attempts in all).
- **Editor stopwatch (secondary)**: worst pano_90 10.21 -> 12.99 ms, floor 5.1 ms. `Budget_FrameTime` re-based to 14.0 ms (warning
  13.0) through the measured relation (section 5); trail_start reads 10.69 to 10.76 ms.
- **Counts (Budget_Counts, worst of its four views)**: triangles 10.88 -> 16.86 M, vertices 16.49 -> 27.48 M, draws (estimated,
  drawer) 463 -> 549, SetPass 39 -> 40-41, shadow casters (drawer-aware) 645 -> 1,042, texture 930.3 MiB and mesh 382.4 MiB
  unchanged. Worst of the 29 views by the Editor counter: 18.39 M (pano_90). Scene: 3,420 -> **5,825 instances**, 13,433 ->
  **23,274 GameObjects**, 9,726 -> 17,138 renderers, 19 materials, 88 meshes.
- **Techniques**: the shorter mist with matching culls; density placed at eye level; the GPU Resident Drawer (D1); planner order.
  No far-quad level, no merging, no occlusion culling (section 4).
- **Final state**: audit passed (0 errors, 0 warnings); captures `captures/d2-final` (32 views, 0 error pixels) and, after a
  pre-existing pair the build had rewritten was put back, `captures/d2-final-restored` (RMSE to d2-final 0.0017 to 0.0026, the
  noise of two identical runs); forest EditMode **35/35**, forest PlayMode **10/10**, greybox **7/7**, three times
  (`out/D2/tests/final/`; `final2/` after the restore; `final3/` after comment-only corrections to `ForestBudgets.cs`).

## 2. Calibration (step 1): the player harness, the build, the measurement mode

### What was added (new folder, own asmdefs; nothing in `Assets/Game/Scripts` touched)

| File | What |
|---|---|
| `P/Assets/Game/Diagnostics/Gamex.Diagnostics.asmdef` | runtime assembly, references Core and HDRP runtime only, not auto-referenced |
| `P/Assets/Game/Diagnostics/ForestFrameHarness.cs` | `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]`: does nothing in the Editor or without `-forestMeasure`. With it: `Application.runInBackground = true`, vsync 0, no frame cap, a 2560 x 1440 backbuffer (full-screen window by default, see below), a copy of the player camera (lens and HDRP data, TAA) with the rig switched off, a pre-warm pass over every view (30 frames), then per view a camera cut, 120 warm-up frames and 300 recorded frames (`Time.unscaledDeltaTime`; FrameTimingManager CPU/GPU times, which a development build always collects; render counters); optional empty-scene floor (`-forestMeasureFloor`), optional stress load (`-forestMeasureDense k`, the D1 stand-in); writes JSON with every frame time and quits. Views default to the 29 of the M4/D1 Editor measurement. |
| `P/Assets/Game/Diagnostics/Editor/Gamex.Diagnostics.Editor.asmdef` + `ForestPlayerBuild.cs` | `BuildMacDevelopment`: `BuildPipeline.BuildPlayer` with an explicit scene list (`Forest.unity` only; Build Settings neither read nor changed), StandaloneOSX, `BuildOptions.Development`, output `-forestBuildOut` (default `Builds/ForestDev/Forest.app`, git-ignored) |
| `B/tools/d2/build-player.sh`, `run-player.sh`, `steady.py`, `frames.py`, `runtable.py`, `relate.py` | build (with the HDRP.asset restore below), run one measurement (`open -W -n`), classify, tabulate, relate to the Editor |

No Player setting was changed: FrameTimingManager is always active in development builds (local Scripting Reference,
`FrameTimingManager.html`), so `enableFrameTimingStats` stayed 0.

### A pre-existing file the build writes, and how it is put back

Every HDRP player build with the GPU Resident Drawer on writes `m_PrefilterUseLegacyLightmaps: 1` into
`Assets/Game/Settings/HDRP.asset` (line 208; `HD/Editor/BuildProcessors/HDRPPreprocessBuild.cs:546-583`: the shader-keyword
prefilter, editor-only, set from `gpuResidentDrawerMode != Disabled` and marked dirty). The file is pre-existing (rule 1) and its
D1 state is pinned by `backup/post-D1.md5`. `build-player.sh` therefore puts the post-D1 file back byte for byte after every build
(`backup/D2-pre-edit/Settings/HDRP.post-D1.asset`, md5 `c7df2bd2...` = post-D1) and diffs every file under `ProjectSettings`,
`Packages` and `Assets/Game/Settings` before and after (`out/D2/settings-*.md5`): after every build "settings: unchanged" and
`check-preexisting-d1.sh`: 0 differ. The field only filters shader keywords during a build; the built player already has it.
(Those checks did not cover a second pair the build writes; see below.)

### The measurement mode: drawables come back in bursts

On the owner's MacBook Pro (M4 Max, built-in 3456 x 2234 display, 120 Hz) the per-frame times of a vsync-off player come in two
kinds of run, independent of the scene (`out/D2/player-before-all-runs.txt`, 19 runs of the same build):

- **steady**: frame times within a few per cent of each other, FrameTimingManager reports GPU times, median = mean;
- **bursty**: single frames of 1.5 to 3 ms alternate with frames of 15 to 40 ms, FrameTimingManager reports no GPU time at all,
  the per-frame median falls to 2 to 5 ms and the per-frame p95 rises to 20 to 36 ms, while the mean frame time is the same as a
  steady run's median (pano_90: steady 7.74 / 8.32 ms, bursty means 7.5 to 8.1 ms).

Tried, with the window focused or not, windowed or full screen, CPU paced by a one-int GPU readback 1, 2 or 3 frames back
(`-forestMeasurePace`), launched in the background (`open -g`): about one run in three is steady (5 of 19), whatever the mode.
The bursts are the presentation (the window server hands drawables back in clumps; CPU frame N waits for a drawable, not for
the GPU), not the rendering: the mean is unchanged and the CPU main thread takes 1.2 to 2.8 ms a frame in both kinds.

What is reported therefore, for every run, every view:

- the per-frame median and p95 (the coordinator's figures), meaningful in steady runs only;
- the median and p95 of the mean of 16 consecutive frames (`frames.py`, about 130 ms): on steady runs equal to the per-frame
  figures within 1 to 3 % (pano_90: 7.74 / 7.76 vs 7.79 / 7.94 ms); on bursty runs its median equals the steady median and its
  p95 is still raised by the bursts (8.7 to 10.3 ms against 7.8 to 8.4), so it is a conservative stand-in for the p95;
- the mean frame time.

The judgement against 12.5 / 15 ms uses the 16-frame figures of every run, and the per-frame figures of the steady runs.

### A second pre-existing pair the build writes, found late

Found after the final build, by listing every project file newer than the first build: the first player build (08:25) had also
rewritten `Assets/HDRPDefaultResources/DefaultSettingsVolumeProfile.asset` (every volume component added with its overrides
switched on at default values; 2,798 diff lines) and `HDRenderPipelineGlobalSettings.asset` (a list of 21 graphics-settings
references filled in). Both are pre-existing (in `backup/pre-forest.tgz`, not in the 59-file md5 list that `build-player.sh` and
`check-preexisting-d1.sh` checked, so the "settings: unchanged" lines above did not cover them). The build's versions and diffs
are kept in `out/D2/hdrp-default-after-build/`; the files were put back from the archive (`backup/D2-pre-edit/HDRPDefaultResources/`),
`build-player.sh` now restores them after every build, and `tools/d2/check-tgz.sh` checks all 108 files of the archive (0 differ,
with the three D1 settings files against `post-D1.md5`). Every Editor run of this pass from 08:25 until the restore (the
iterations, the final captures, the first final test run) ran with the build-written pair; the captures and the three suites were
therefore run again after the restore (section 7).

### The machine drifts between sessions; comparisons are interleaved

The same build measured 1 to 1.5 ms apart at different times of the day: i5 at 09:35 (worst 16-frame median 9.1 ms) and at 10:03
(10.5 ms); the sparse scene at 08:51 (7.7 ms) and at 10:20 (6.4 to 8.1 ms, section 7). The Editor stopwatch did not drift (its
empty-scene floor stayed 5.11 to 5.19 ms all day). The cause is not known (the owner's desktop apps keep a load average of 2 to 6;
no thermal warning). Before/after figures therefore come from interleaved runs of the two builds (before, final, before, final,
...), and A/B checks of iterations were interleaved too (`ab-i5-*`, `ab-i8-*`: i5 10.54 / 10.60, i8 10.99 ms; the fourth run was
refused by the script's player check, which matched its own watcher's command line; fixed to check executables).

Runs are also interrupted when the owner switches away from the full-screen player: the window loses focus and single frames take
0.5 to 1.4 s. `frames.py` marks a view interrupted when it lost focus or had a frame of 250 ms or more; such views are left out of
that run's figures (listed per run in `out/D2/final-player-verdict.txt`).

## 3. Density (step 2): what changed and what it gives

All in the planner's and painter's tunables and rules (`ForestLayout`, `ForestScatter`, `ForestTerrain`, `ForestFields`); every
correctness rule (corridors of 2.5 m + clearance, spawn 5 m, young firs 3.5 m from the main trail, grounding, slopes, viewpoint
gaps, solid spacing) is unchanged and still enforced by the planner, the audit and the tests.

| Tunable | Before | D2 | Why |
|---|---|---|---|
| Tall firs: grove / lone spacing, grove noise threshold, thicket cut | 5.5 / 13 m, 0.44 to 0.56, 0.35 | 3.9 / 5.5 m, 0.26 to 0.40, 0.15 | about 280 a hectare, clustered |
| Tall firs: landmark keep-out | every landmark circle + 3 m (22 % of the core) | first view and deadfall + 3 m; hollow 6.5 m; clearing its glade; others by the solid rule | firs between the boulder-field and outcrop rocks |
| Tall firs: sizes | 0.80 to 1.25 | 30 % of the core's firs a younger cohort at 0.62 to 0.82 (9 to 12 m) | mixed age |
| Young firs: thicket field centre (`ForestTerrain.ThicketCentre`) | 0.4525 (thickets on ~30 % of the core) | 0.66 | thickets on most of the core |
| Young firs: thicket ramp, spacing thicket / clump / open | 0.55 to 0.92; 2.4 / 3.0 / 32 m | 0.45 to 0.82; 2.2 / 2.4 / 3.2 m | about 660 a hectare |
| Young firs: understorey on open ground | none | 60 % of the open-ground firs at 0.55 to 0.68 | understorey in canopy gaps |
| Young firs: gap to a first-person capture camera | 2.6 m | 3.0 m | a branch across a quarter of the deadfall frame |
| Order of the planner | tall, young, rocks, deadwood | tall, rocks, logs, young, stumps and branches | rocks fell from 90 to 78 and trail-side logs to 0 of 5 in the dense stand; thickets now grow around them |
| Logs | 5 beside the game trails (+3 deadfall) | + 14 among the trees (0.9 to 1.5, 9 m apart, all corridor, bedding and solid rules) | "more fallen logs" |
| Stump patches, branches (scattered / in thickets / bank) | 7 at 12 m; 80 / 25 / 25 | 20 at 9 m; 220 / 70 / 40 | "more branches and stumps" |
| Fern patches: cap; field | 1,250 | 2,300; + a carpet term under tall crowns (sum of Gaussians, sigma 3 m, within 6 m, 0.6 to 1.6 -> +0.7 a m2) | about twice the coverage, carpets in shade |
| Fern clump cap | 400 | 800 | the ring rule of more logs and stumps |
| Clearing | open to 13 m | a glade of 10 m (`ClearingOpenRadius`; terrain shape unchanged; its rim thicket 10 to 15 m; grass cap 205 -> 120) | "shrink the open areas to small glades" |
| Ground paint | thicket field | the thicket field as it was (`ForestFields.paintThicket`, centre 0.4525) | with the denser field the floor turned uniform humus (ground_close lost its litter; `captures/d2-i4/sheets/cmp2.png`) |
| Mist (`ForestLightingPresets`) | mean free path 70 m, volumetric range 80 m | 20 m, 55 m | contrast exp(-d/20): 47 % at 15 m, 37 % at 20 m, 13 % at 40 m, 8 % at 50 m |
| Culls (`ForestKitSpec`, last LOD) | young 85, tall 85, rocks 90, logs 110, stumps 70 m | 50, 55, 55, 55, 50 m | gone in the mist (D1: a 55 m cull is invisible in a 20 m mist) |

Iterations i1 to i9 (`out/D2/density-iterations.txt` for i1 to i8, `out/D2/i9/`; logs `logs/D2/scene-i*.log`, `build-i1.log`; the
scene log of i2 was overwritten by a player-build log of the same name, its plan is in `out/D2/i2/`):

| | before | i2 | i4 | i9 = final |
|---|---|---|---|---|
| tall firs in the core | 152 (81/ha) | 428 (229/ha) | 528 (282/ha) | **529 (282/ha)** |
| young firs in the core | 530 (283/ha) | 829 (443/ha) | 1,144 (611/ha) | **1,244 (664/ha)** |
| fern coverage of the core | 10.9 % | 25.3 % | 26.1 % | **26.4 % (2.4x)** |
| logs / stump patches / branches (all) | 8 / 10 / 127 | 22 / 23 / 309 | 19 / 23 / 306 | **22 / 23 / 309** |
| sightline, trail: clear beyond 30 m / 15 m | 58 % / 82 % | 28 % / 64 % | 19 % / 53 % | **17 % / 47 %** |
| sightline, core points: beyond 30 m / 15 m | 42 % / 70 % | 18 % / 51 % | 9 % / 35 % | **8 % / 33 %** |

Core = the rounded rectangle, 18,727 m2 (`tools/d2/density.py`; fern coverage = union of discs 1.0 m x scale per patch and
0.4 m x scale per clump on a 0.25 m grid). Ferns in shade (sum of the tall firs' Gaussians above 1, 74 % of the final core): 12.0
patches per 100 m2 there against 5.5 elsewhere (before: 1.9 and 4.1). Sightlines: `tools/d2/sight3.py`, the porous model of M4 review 2 (`work/M4r2/scripts/sight2.py`:
young-fir crowns 1.1 m x scale passing 60 %, tall trunks 0.25 m x scale blocking, 72 eye-level rays per point; trail every 5 m,
plus 200 random core points at least 1.2 m from a trunk); the before figure reproduces the brief's 58 %.

### Late fix after looking at iteration i6

hollow_rim (`captures/d2-i6/sheets/cmp10.png`): the denser thicket field put young firs into the fern hollow's bowl, one about 4 m
in front of the crouched camera, hiding the mud and the ferns the view is for. `ForestLayout.YoungFirHollowKeepOut = 8 m` keeps
them out of the bowl (radius 11 m; the tall firs keep 6.5 m). Applied before the final build (i9 / final).

## 4. Performance (step 3): techniques applied and not applied

Applied:

1. **The mist shortened and every class culled where it is gone** (section 3): young firs at 50 m, tall firs, rocks and logs at
   55 m, stumps at 50 m; the volumetric range 55 m. This is what pays for the density: in the first dense iteration (i2: 2.8x the
   tall firs, 1.6x the young firs) the player's worst view cost about what the sparse scene had cost (8.5 ms against 7.7 to 8.3,
   in different sessions; the interleaved final comparison is in section 7).
2. **Density at eye level**: the added objects are young firs (thickets, clumps, understorey at 0.55 to 0.68), logs, stumps,
   branches and ferns; the tall firs carry a younger cohort at 0.62 to 0.82 (smaller crowns, the same LOD meshes).
3. **The GPU Resident Drawer** (D1) keeps the draws instanced: about 5,800 instances render in a few hundred draw commands.
4. **Planner order** (rocks and logs before the young firs), so the dense stand does not crowd the cover pieces out.

Not applied, and why:

- **A far level of crossed quads**: not needed. Triangles grew (crouch_hide 10.9 -> 16.7 M, deadfall 9.8 -> 19.7 M by the Editor
  counter), but the player stays inside its budget (section 5), and the culls removed the far levels' share. Not built, so no
  new shader, material or texture.
- **GPU occlusion culling**: D1 measured it culling nothing through alpha-tested foliage; the dense stand is still all
  alpha-tested needles. Not re-measured.
- **Merging objects**: 23,274 GameObjects (was 13,433). The Editor did not become sluggish by the measures available: the full
  capture of 32 views took 37 s (33 s before), the audit 0.32 s (0.31 s), the scene build 20 s, loading the scene in play mode
  2.1 s (1.9 s). Nothing was merged.
- **Shadow distance, LOD0 distances, the young-fir scan level (4 m)**: unchanged.

## 5. The Editor stopwatch and the player: the relation, and `Budget_FrameTime`

`tools/d2/relate.py`: per view, the Editor stopwatch median (median of three runs of the D1 temporary measurement, 29 views,
`logs/D2/measure-*.log`) against the player's 16-frame median (median of its clean runs), and a least-squares line over all pairs.

| Pairs | Fit | r | residual sd | player 12.5 ms at stopwatch |
|---|---|---|---|---|
| sparse scene (08:51 runs) + i5 (09:35 runs), 58 | player = 0.671 x stopwatch + 0.58 ms | 0.973 | 0.18 ms | 17.75 ms |
| sparse scene + i8 (09:50 runs), 58 | 1.078 x - 3.05 ms | 0.995 | 0.13 ms | 14.43 ms |
| sparse + i8 + i8 with every LOD group doubled (`dense2`, stopwatch up to 18.4 ms, player up to 17.1 ms), 87 | 1.032 x - 2.61 ms | 0.998 | 0.16 ms | 14.63 ms |
| final session: sparse and final builds interleaved (10:19 to 10:34) against the Editor's sparse (08:5x) and final (10:16) runs, 58 | 0.752 x - 0.33 ms | 0.988 | 0.15 ms | 17.06 ms |

(`out/D2/relation-*.txt`.) The relation is linear up to and beyond the budget (the doubled stand puts deadfall at 18.4 ms in the
Editor and 16.4 to 17.1 ms in the player), but its slope depends on the player's session state (section 2). The steeper fits,
measured when the player was slow, protect the budget; `GpuFrameMsBudget` is 14.0 ms (below every fit: 14.4 to 17.8 ms) and the
warning 13.0 ms (a player frame of about 11 ms). `Budget_FrameTime` measures trail_start: 10.76 ms in the final test run.

Editor stopwatch, 29 views, three runs each (`out/D2/editor-before-final-table.txt`): sparse scene worst pano_90 10.21 ms
(crouch_hide 10.10), 0 of 29 over 11 ms, floor 5.12 to 5.18 ms; final worst pano_90 12.99 ms (deadfall 12.41, hollow_rim 12.30),
19 of 29 over 11 ms, floor 5.11 to 5.14 ms; worst triangles 10.88 -> 18.39 M (Editor counter, worst of eight frames).

## 6. Caps and tests re-based (rule 4: a pinned tunable changes with its tunable, on evidence)

| What | Before | D2 | Evidence and reason |
|---|---|---|---|
| `ForestWorldTests.ExpectedCounts` (15 % band unchanged) | tall 400, young 970, rocks 90, logs 8, stumps 10, branches 125, grass 220 | 800, 1,700, 90, 22, 23, 310, 135 | the measured plan of the final build (796, 1,706, 88, 22, 23, 309, 134); the tunables they follow are the point of the pass |
| `ForestWorldTests` fern caps (copies of `ForestLayout`) | 1,250 / 400 | 2,300 / 800 | the caps were raised for twice the fern coverage and the ring rule of more logs and stumps |
| `ForestBudgets.PrefabInstancesBudget` / warn (audit) | 3,500 / 3,000 | 6,500 / 6,100 | 5,825 instances in the final plan at 9.6 to 10.5 ms in the player (budget 12.5) |
| `ForestBudgets.GameObjectsBudget` / warn (audit) | 14,000 / 12,000 | 26,000 / 24,500 | 23,274 GameObjects; the Editor stayed responsive (section 4) |
| `TrianglesBudget` / warn (+ PlayMode copy) | 11.6 M / 10.1 M | 19.0 M / 17.5 M | worst budget view 16.66 M at i8 (16.86 M final, crouch_hide), worst of all views 18.4 to 19.7 M (pano_90, deadfall) at 9.6 to 11 ms in the player |
| `VerticesBudget` / warn (+ copy) | 19.0 M / 17.0 M | 31.0 M / 28.5 M | worst budget view 27.22 M at i8 (27.48 M final) |
| `GpuFrameMsBudget` / warn, the Editor stopwatch of `Budget_FrameTime` (+ copy) | 11.0 / 9.5 ms | 14.0 / 13.0 ms | the player budget mapped through the measured relation (section 5) |

Not changed: every correctness check (grounding, corridors, spawn, slopes, NavMesh connectivity, coverage, materials, error pixels,
exposure window, determinism), the draw-call, SetPass, shadow-caster, texture- and mesh-memory budgets, the scan distance, every
other test. The test files changed: `ForestEditMode/ForestWorldTests.cs` (the counts and caps above, with a comment) and
`ForestPlayMode/ForestPlayBudgets.cs` (the copies); originals in `backup/D2-pre-edit/Tests/`.

## 7. Final runs

Transcripts `logs/D2/final-sequence.txt` (`tools/d2/final.sh`) and `logs/D2/final-verify.txt` (`tools/d2/final-verify.sh`).

| # | Run | Result | Evidence |
|---|---|---|---|
| 1 | Compile outside Unity: editor, both test assemblies, harness, build script | OK x5 | final-sequence.txt |
| 2 | `ForestBuild.BuildAll -forestForce` | exit 0 in 71 s; 34 prefabs, 0 failed; audit passed: 5,825 instances, 23,274 GameObjects, 19 materials, 88 meshes, 0 warnings; grounding 9.8 mm, corridor margin 2.54 m, spawn margin 5.52 m, physical corridors free, core slope 25.8 deg, trail 4.9 deg; `placements.json` byte-identical to the unforced build i9 (determinism) | `logs/D2/final-buildall.log`, `out/D2/final/` |
| 3 | `ForestCapture -captureSet full` (RMSE against `captures/verify-m4`) and `-captureSet debug` | 32 views `passed: true`, 0 magenta, 0 cyan, 0 black or white share, gameplay views mean luminance 0.18 to 0.29 (overviews 0.38 / 0.42); 18 debug images | `captures/d2-final/`, `out/D2/final-luminance.txt`, sheets `captures/d2-final/sheets/cmp1..11.png`, `debug.png` |
| 4 | Forest EditMode / forest PlayMode / greybox PlayMode | **35/35, 10/10, 7/7** | `out/D2/tests/final/*.xml` |
| 5 | Pre-existing files | 59: 0 differ (3 against post-D1.md5); then the HDRPDefaultResources pair found and put back; 108-file archive: 0 differ | final-verify.txt |
| 6 | After the restore: full capture again (RMSE against d2-final) and the three suites again | RMSE 0.0017 to 0.0026, 0 error pixels; **35/35, 10/10, 7/7**; Budget_Counts identical but SetPass 40; Budget_FrameTime 10.69 ms | `captures/d2-final-restored/`, `out/D2/tests/final2/*.xml` |
| 7 | Editor stopwatch, 29 views, 3 runs (D1's temporary test, removed after) | worst pano_90 12.99 ms (p95 13.78), deadfall 12.41, hollow_rim 12.30; floor 5.11 to 5.14 | `logs/D2/measure-final-*.log`, `out/D2/editor-before-final-table.txt` |
| 8 | Player: sparse and final builds interleaved, 3 + 3 runs, then 1 sparse and 4 final more | table below | `out/D2/player/fin-*.json`, `out/D2/final-player-verdict*.txt` |
| 9 | The three suites once more, after comment-only corrections to the budget remarks in `ForestBudgets.cs` | **35/35, 10/10, 7/7**; Budget_FrameTime 10.57 ms; 108-file archive and 59 files: 0 differ | `out/D2/tests/final3/*.xml` |

All Unity logs of the final runs: 0 `error CS`, 0 `warning CS`, 0 "immutable". No temporary file left in the project.

Player runs (worst view per statistic, interrupted views left out; floor = mean of the empty-scene view in steady terms, about
3.3 ms in both builds):

| Run | Mode | 16-frame median | 16-frame p95 | per-frame median | per-frame p95 | mean |
|---|---|---|---|---|---|---|
| fin-before-1 | bursty, 15 of 29 views (focus lost at backlit) | 7.40 (rock_close) | 7.97 | 7.31 | 14.79 | 7.43 |
| fin-before-2 | bursty | 7.66 (pano_90) | 9.46 | 3.79 | 30.72 | 7.67 |
| fin-before-3 | bursty | 7.58 (pano_90) | 9.53 | 5.16 | 29.47 | 7.54 |
| fin-before-4 | bursty | 7.54 (pano_90) | 9.15 | 3.70 | 26.81 | 7.56 |
| **fin-final-1** | bursty | **9.63 (pano_90)** | **11.60** | 6.81 | 33.39 | 9.51 |
| **fin-final-2** | bursty | **9.56 (pano_90)** | **11.75** | 6.01 | 33.90 | 9.56 |
| **fin-final-3** | bursty | **10.48 (pano_90)** | **12.85** | 3.45 | 38.50 | 10.47 |
| fin-final-s-a1 to a4 | bursty (4 attempts at a steady run) | 9.62 to 10.11 (pano_90) | 11.95 to 12.86 | 6.56 to 7.38 | 35.0 to 45.0 | 9.73 to 10.16 |

Per view, median over the clean runs: before pano_90 7.58 / 9.04 ms (16-frame median / p95), final pano_90 9.71 / 12.32 ms over
all 7 final runs (9.63 / 11.75 over the 3 interleaved ones); next worst final views deadfall 9.19, hollow_rim 9.06, long_sightline
8.79, trail_mid_fwd 8.71 ms. Earlier sessions for scale: the sparse build steady at 7.74 and 8.32 ms (08:51, 09:03, per-frame p95
7.94 and 8.38), the near-final i8 at 10.85 / 11.72 ms (09:50, a slow session; its deadfall view ran steady at 10.55 ms with a
per-frame p95 of 11.85 ms).

## 8. What changed in the project

| File | Change |
|---|---|
| `Assets/Game/Diagnostics/` (new folder, own asmdefs) | `Gamex.Diagnostics.asmdef`, `ForestFrameHarness.cs`, `Editor/Gamex.Diagnostics.Editor.asmdef`, `Editor/ForestPlayerBuild.cs` (+ metas) |
| `Editor/Forest/ForestLayout.cs` | density tunables (tall groves, younger cohort, young-fir spacing and ramp, gap understorey, viewpoint gap 3 m, hollow keep-out, glade, deadwood counts, fern caps and shade term, grass cap) |
| `Editor/Forest/ForestScatter.cs` | planner order (rocks and logs before young firs), tall-fir landmark keep-out, younger cohort, gap understorey, hollow keep-out, `ScatterForestLogs`, stump thicket rule, fern shade term (`TallFirShade`), glade in every clearing rule |
| `Editor/Forest/ForestTerrain.cs` | `ThicketCentre` 0.66, the paint's own thicket field, the glade in the thicket field and the clearing paint |
| `Editor/Forest/ForestFields.cs` | internal `paintThicket` |
| `Editor/Forest/ForestKitSpec.cs` | culls 50 / 55 m (kit hash changed, all 34 prefabs rebuilt; meshes unchanged) |
| `Editor/Forest/ForestLightingPresets.cs` | mist 20 m, volumetric range 55 m |
| `Editor/Forest/ForestBudgets.cs` | frame-time, triangle, vertex, instance and GameObject budgets re-based (section 6) |
| `Tests/ForestEditMode/ForestWorldTests.cs`, `Tests/ForestPlayMode/ForestPlayBudgets.cs` | section 6 |
| Generated by the forced rebuild | `Forest.unity`, `ForestData/*`, `ForestKit` prefabs, textures and report |
| `Builds/ForestDev-before`, `Builds/ForestDev-final` (git-ignored) | the two measured development players |

Diffs: `out/D2/diffs/`; originals `backup/D2-pre-edit/` (Forest sources, tests, the post-D1 HDRP.asset, the HDRP default resources).
No project setting, no pre-existing file (59-file md5 list and the 108-file archive: 0 differ apart from D1's three), no greybox
file, nothing in `Assets/Game/Scripts`, no `.gitignore`, no EditorBuildSettings changed; no Player setting (FrameTimingManager is
always on in development builds). No git command that changes anything. Unity was launched through `tools/d2/unity.sh` only (the
rule's `pgrep` check plus a check for any running Unity executable), one process at a time, never `-nographics`, never `-quit` with
`-runTests`; no exit code 198.

## 9. Not verified, weak, open

- **The per-frame p95 of a bursty run is over 15 ms, before the pass as after it** (sparse build 15 to 31 ms, final build 33 to
  45 ms in this session's runs, all bursty). It
  is the presentation on this machine (section 2), but this pass could not make the player present evenly on demand: one run in
  three to five was steady in the morning, none of 11 after 10:19. The budget judgement rests on the 16-frame figures; a
  per-frame p95 under 15 ms was not obtained for the final build (the near-final i8's one steady view, deadfall, had 11.85 ms).
- **Session drift of the player** (1 to 1.5 ms for the same build at different hours; cause unknown). Before/after is interleaved;
  the absolute numbers carry that uncertainty.
- GPU time per frame: FrameTimingManager gives it in steady runs only, and in some steady runs it reads twice the frame interval
  (overlapped frames on the tile-based GPU), so it was not used.
- The player's 'Triangles Count' now and then reports two frames at once (exactly twice the value); the harness records the median.
- The sightline figures are a model (crowns porous discs, trunks opaque discs), as in M4 review 2; no render-based sightline test.
- Look: the mist greys everything near as well (frames 0.18 to 0.29 mean luminance, colour mostly gone beyond 10 m); the overview
  views are now mostly mist; boundary_n, boundary_s and deadfall have a young fir's branch across the frame edge at about 3 m;
  the trail corridor itself is still open litter for 2.5 to 3.5 m each side (the corridor rule). For the owner's review.
- Not measured: walking (LOD pops with cross-fade off, in a denser stand), memory in the player, the dinosaurs in the dense stand
  (NavMesh connectivity is tested; agent movement among 2,500 trunks is not).
- `Logs/forest-work/` (the in-project copy of the records) was not updated.
