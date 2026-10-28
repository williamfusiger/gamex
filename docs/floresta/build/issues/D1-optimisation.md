# D1: GPU Resident Drawer and GPU occlusion culling (measured, then the drawer switched on)

2026-10-15. Brief: `build/BRIEF-density.md` (owner approval for the drawer's project settings, rule 1's exception).
`B` below is `SCRATCH/build`, `P` the project, `HD` and `CORE` the HDRP 17.6 and Core packages under
`/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/Resources/PackageManager/BuiltInPackages/` (the project's
`Library/PackageCache` copies are identical apart from `package.json`, checked with `diff -rq`).

## 1. Result

- **The GPU Resident Drawer is ON** (HDRP asset: Instanced Drawing; Graphics: BatchRendererGroup Variants = Keep All;
  Player: static batching off for Standalone). **GPU occlusion culling stays OFF**: it culled 0 instances at today's density
  and 1 to 3 of about 1,600 at 3x density, cost 0.6 to 1.9 ms per frame, and blinds the engine's triangle counter.
- **Gain is real but small at today's density**: worst views 10.71 ms -> 10.25 to 10.45 ms (stopwatch median, the team's
  method), p95 11.40 -> 11.03 to 11.34 ms; the mean over 29 views is unchanged (9.50 -> 9.38 to 9.50) because the drawer adds a
  fixed ~0.45 ms per frame (empty-scene floor 4.7-4.9 -> 5.2-5.4 ms) and saves 0.4 to 0.95 ms of scene cost. The saving grows
  with density: 0.3 to 0.8 ms at 2x, 1.2 to 3 ms at 5x.
- **Draw calls (estimated)** 1,812 -> 298 at crouch_hide (worst view 2,041 -> 316); triangles unchanged 10.88 M; SetPass 41-43
  -> 36-39.
- **Images**: the forest renders the same (A vs drawer within the noise of two identical runs everywhere, except one far fogged
  fir at a different LOD level in deadfall); no missing or popping object in a 160-frame walk. The greybox renders the same to
  the eye; in play mode it is not bit-identical (section 6).
- **Tests on the switched, rebuilt project: forest EditMode 35/35, forest PlayMode 10/10, greybox 7/7** (`B/out/D1/tests/run1/`).
  One test file changed: `Budget_Counts` had to learn to count under the drawer (section 5), because two of its inputs go blind.
- **Headroom for D2**: about 0.55 to 0.75 ms at the worst view at today's density. Doubling every LOD-grouped object costs
  +2.2 to +3.0 ms even with the drawer (worst 13.1 to 13.6 ms); the fog-and-cull knob takes 0.6 to 0.8 ms of that back on average. The
  drawer alone does not pay for the density pass (section 8).

## 2. What the drawer and GPU occlusion need in Unity 6000.6 / HDRP 17.6 (sources)

| Requirement | Where | Project before D1 |
|---|---|---|
| HDRP asset `m_RenderPipelineSettings.gpuResidentDrawerSettings.mode` = 1 (InstancedDrawing); default Disabled | `HD/Runtime/RenderPipeline/GlobalGPUResidentDrawerSettings.cs:11-21`; serialized at `HD/Runtime/RenderPipeline/Settings/RenderPipelineSettings.cs:494` (default `:200`); enum `CORE/Runtime/GPUDriven/IGPUResidentRenderPipeline.cs:9-20`; docs `HD/Documentation~/gpu-resident-drawer.md:19-22` | `P/Assets/Game/Settings/HDRP.asset:525` `mode: 0` |
| GPU occlusion: same struct, `enableOcclusionCullingInCameras` (default off), `useDepthPrepassForOccluders` (default on); only with mode InstancedDrawing | `GlobalGPUResidentDrawerSettings.cs:15-16, 29-32`; `CORE/.../GPUResidentDrawer.cs:87-99`; docs `HD/Documentation~/gpu-culling.md:20-23` | `HDRP.asset:527-528`: 0, 1 |
| Occlusion only for Game, SceneView and Preview cameras; occluders from the depth prepass when the prepass is full or the flag is on | `HD/Runtime/RenderPipeline/HDRenderPipeline.Prepass.cs:215-228` | (the measured camera is a Game camera) |
| HDRP hands the drawer: cross-fade support = `QualitySettings.enableLODCrossFade`, occlusion flag, `allowInEditMode = true` | `HD/Runtime/RenderPipeline/HDRenderPipelineAsset.cs:247-260` | `enableLODCrossFade: 1` (no change needed) |
| Graphics settings: BatchRendererGroup Variants must be **Keep All**, else the drawer refuses to start in the Editor | `CORE/.../GPUResidentDrawer.Validator.cs:61-66` (message `:19-21`); HDRP inspector warning `HD/Editor/RenderPipeline/HDRenderPipelineUI.cs:1634, 1666-1667` | `P/ProjectSettings/GraphicsSettings.asset:58` `m_BrgStripping: 0` (KeepIfEntitiesGraphics; KeepAll = 2, StripAll = 1, probed by compiling) |
| Platform: BatchRendererGroup raw-buffer path and the occluder kernel present | `Validator.cs:44-59` | Metal: `RawBuffer` (logged) |
| Player settings: static batching off is recommended, not required (statically batched renderers stay on the old path) | `HDRenderPipelineUI.cs:1635, 1668-1669`, text `HDRenderPipelineUI.Skin.cs:437-438`; `gpu-resident-drawer.md:46-49`; Manual `optimizing-draw-calls-choose-method.html` ("Static batching isn't compatible with the BRG API or GPU Resident Drawer") | `ProjectSettings.asset:531` `m_BuildTargetBatching: []` (defaults, on). No renderer in either scene is static-flagged (greybox: all 603 `m_StaticEditorFlags: 0`; forest: audit rule), so this changes nothing today |
| Quality settings: nothing required; no level overrides the pipeline asset | `QualitySettings.asset:51..316` `customRenderPipeline: {fileID: 0}` | unchanged |
| Only `MeshRenderer`s are taken (no SkinnedMeshRenderer, particles, terrain) | `CORE/.../GPUResidentDrawer.cs:495-501` (type tracking); docs `HD/Documentation~/make-object-compatible-gpu-rendering.md:3-12` | forest: 9,726 MeshRenderers, all 9,726 taken (logged); terrain stays on the classic path |
| Per-renderer exclusions: LOD Animate Cross-fading, MaterialPropertyBlock, render callbacks (`OnRenderObject`...), non-standard sort key, proxy-volume light probes, blend probes with anchor, Enlighten vertex stream, shader without DOTS instancing, null material, too many sub-meshes, missing mesh, `DisallowGPUDrivenRendering`, animation visibility, TextMesh, inactive/disabled | `CORE/Runtime/GPUDriven/Debug/GPUResidentDrawerDebug.cs:22-46`; component `CORE/Runtime/GPUDriven/Components/DisallowGPUDrivenRendering.cs:8-17` | forest: no property blocks (audit error), `UseCrossFade = false` (no animated fade), HDRP/Lit has `DOTS_INSTANCING_ON` (`HD/Runtime/Material/Lit/Lit.shader:366`...); greybox: HDRP/Lit only, no MPB, no callbacks |
| LOD cross-fade (dithered) is supported by the drawer's own LOD system | `GPUResidentDrawer.cs:476`; `CORE/.../CoreDataSystems/LODGroupDataSystem.cs:49-57` | — |
| Edit mode: HDRP allows the drawer in edit mode; never in an asset-import worker | `HDRenderPipelineAsset.cs:252`; `Validator.cs:84-97` | — |
| The drawer is (re)created per frame when the project setting and its state disagree; HDRP calls this every render | `GPUResidentDrawer.cs:160-168`; `HD/Runtime/RenderPipeline/HDRenderPipeline.cs:2304` | trap: Keep All with mode Disabled re-creates (and refuses) every frame in the Editor; `Revert` therefore resets both |

### The command-line flags

- `-force-gpuresidentdrawer` and `-force-gpuocclusion` exist (`GPUResidentDrawer.cs:1` `#define GPU_RESIDENT_DRAWER_ALLOW_FORCE_ON`,
  parsed at `:436-450`), are **Editor only** (`:310-330`; a player build ignores them), force the mode and the occlusion flag
  (`:290-304`) and skip the SRP and project checks, including Keep All (`Validator.cs:99-101`).
- **They work in batch mode, also in play mode**: logs show "GPU Resident Drawer forced on via commandline", "GPU Resident Drawer
  created (forced on via commandline) with GPU Occlusion Culling (forced on via commandline)", and the test read
  `IsInitialized True, occlusion True` inside play mode (`B/logs/D1-measure-smokeB.log`, `D1-measure-ab-G*.log`, `-C*.log`). The
  project enters play mode without a domain reload, which disposes the drawer (`GPUResidentDrawer.cs:355-360`); HDRP creates it
  again at the first render, so a test must ask after rendering.
- With the drawer on by settings, `-force-gpuocclusion` alone still turns occlusion on: D2 can A/B occlusion by flag. There is
  **no flag to force the drawer off**; an A/B against the classic path now needs `ForestProjectSettings.Revert` (or a
  `DisallowGPUDrivenRendering` component).
- zsh does not split an unquoted variable: `$F="-force-gpuresidentdrawer -force-gpuocclusion"` reached Unity as one word and
  forced nothing (run `smoke2C`, used as an extra no-flag run). The D1 scripts are bash.

## 3. Method

- **Frame time**: the team's stopwatch (`camera.Render()` plus a one-pixel readback, 90 frames after 24 + 8 + 10 warm-up frames,
  2560 x 1440, play mode, Editor), median and p95 per view; 29 views (M4's list); per run also the empty-scene floor
  (configuration `nothing`, the Environment switched off). New next to it: the whole engine frame (time between loop
  iterations), which includes the drawer's own per-frame update outside `Render()`.
- **Arms**: A no flag, G `-force-gpuresidentdrawer`, C both flags; **three runs per arm, interleaved A G C A G C A G C, 60 s rest
  between runs**; numbers are medians over runs of the per-run medians. After the switch: three more runs, no flag.
- **Counters** (worst of eight frames, the method of `Budget_Counts`): triangles, vertices, SetPass, shadow casters, 'Draw Calls
  Count', 'Batches Count'. The draw estimate of `Budget_Counts` (visible renderers) and, under the drawer, its own culling
  statistics (Rendering Debugger "Display Culling Stats", read by reflection over eight untimed frames): draw commands per camera
  and shadow view, visible instances, triangles, occlusion events.
- **Images**: per view the first frame after a camera cut and the 64th frame (2560 x 1440), and a 160-frame walk along the trail
  at 0.2 m per frame with the view swinging +-35 degrees (every second frame, 960 x 540); arms A twice (noise floor), G, C.
  Compared with `tools/d1/imgdiff` (identical share, RMSE, largest channel difference, worst 32 x 32 block).
- **Rested machine**: the owner's desktop apps (ClickUp, VS Code) kept a load average of 2 to 3. Two runs (G1, and the first two
  passes of dense-A2) were hit by a transient outside load (the first view 2 to 13 ms slow, decaying; the floor right after was
  6.6 ms); the three-run medians filter G1, and dense-A2's affected passes are not used. No thermal warning (`pmset -g therm`).
- Tools: `B/tools/d1/` (`ForestD1MeasureTemp.cs` copied into the forest PlayMode folder for one run and removed,
  `run-measure.sh`, `ab.sh`, `dense.sh`, `after.sh`, `d1table.py`, `imgdiff`, `diff-ab.sh`, `GreyboxD1CaptureTemp.cs`,
  `run-greybox.sh`, `greybox.sh`, `tests.sh`, `unity.sh`, `check-preexisting-d1.sh`).

## 4. A/B before the switch (flags only, nothing changed)

Tables: `B/out/D1/ab-table.txt` (all 29 views x 3 arms), logs `B/logs/D1-measure-ab-{A,G,C}{1,2,3}.log`.

| View | A no flag | G drawer | C drawer + occlusion |
|---|---|---|---|
| crouch_hide | 10.71 (p95 11.39) | **10.28** (10.85) | 11.50 (12.13) |
| pano_90 | 10.71 (11.40) | **10.25** (11.03) | 11.33 (11.91) |
| fir_close | 10.06 (10.72) | 10.02 (10.47) | 11.24 (11.99) |
| deadfall | 10.06 (10.85) | 9.88 (10.76) | 10.88 (11.97) |
| hollow_rim | 9.82 (10.57) | 9.58 (10.50) | 10.59 (11.01) |
| trail_start | 9.48 (10.15) | 9.29 (9.84) | 9.92 (10.57) |
| 29 views: over 11 / over 9.5 / mean | 0 / 11 / 9.50 | 0 / 12 / 9.38 | 3 / 24 / 10.17 |
| Floor (empty scene), per run | 4.86, 4.66, 4.94 | 5.30, 5.17, 5.29 | 5.26, 5.48, 5.43 |
| Triangles at crouch_hide (counter) | 10.88 M | 10.88 M | 3.99 M (blind, see below) |
| SetPass, worst | 43 | 39 | 40 |
| Draws: Budget_Counts estimate / drawer commands (2 x camera + shadow) | 1,812 at crouch_hide, worst 2,041 | 298, worst 316 | 298, worst 316 |

- G against A: worst views -0.43 / -0.46 ms; scene cost above the floor -0.17 to -0.95 ms at every view; seven views
  +0.15 to +0.28 ms (the four boundary views, trunk_close, trail_strip_05, ground_close: the fixed overhead wins there).
- C: the drawer's occlusion test runs (5 occlusion events per frame) and reports **0 instances culled** at 1x and 2x and 1 to 3 of
  ~1,600 at 3x (`B/logs/D1-measure-after-dense-occ.log`: "culling 0 instances ... passing 848 instances", so the counters are
  live). Alpha-tested needles leave gaps in every tile of the depth pyramid and trunks are thin, so nothing is a full occluder.
  It costs +0.6 to +1.2 ms against G at 1x and +1.0 to +1.9 ms at 2x/3x (`B/out/D1/occlusion-dense-table.txt`).
- **Images** (`B/out/D1/image-diff.txt`, heat maps `B/captures/d1-ab/diff/`, sheets `B/captures/d1-ab/sheets/`): A1 vs A2 (noise):
  largest difference 6/255, worst block 0.57. A vs G and A vs C: the same everywhere except deadfall, one far fir deep in the fog
  at a different LOD level under the drawer (block mean 2.3/255, max 44/255, about 70 pixels; identical in G and C, absent between
  A1 and A2; `B/work/D1/look/df_zoom.png`). Walk (80 frames per arm): A vs G and A vs C at the noise level (worst block 0.52 to
  0.56, max 20/255): no missing, late or flickering object while moving and turning. Cut frames: same as the 64th frames.
- **Density** (`B/out/D1/dense-table.txt`, `after-dense-table.txt`; every LOD group copied 2.5 to 5.5 m away; clean runs A1, G1, G2):
  A 12.4 to 13.8 ms at 2x vs G 12.2 to 13.1 (-0.3 to -0.8 at the same view); at 5x (the first "dense3" runs were 5x by a bug,
  same objects in both arms) A 20.5 to 25.2 vs G 18.5 to 22.4 (-1.2 to -3.0 ms). Triangles grow with density (19.8 M at 2x, 32 M at 3x, 51 M at 5x); the stopwatch follows
  the triangles, not the draws, once the draws are instanced.

## 5. What changed

| File | Change |
|---|---|
| `P/Assets/Game/Settings/HDRP.asset` (pre-existing, rule 1 exception) | line 525 `mode: 0` -> `mode: 1` |
| `P/ProjectSettings/GraphicsSettings.asset` (pre-existing, exception) | line 58 `m_BrgStripping: 0` -> `2` (Keep All) |
| `P/ProjectSettings/ProjectSettings.asset` (pre-existing, exception) | line 531 `m_BuildTargetBatching: []` -> one entry `Standalone`, `m_StaticBatching: 0`, `m_DynamicBatching: 0` (HDRP ignores dynamic batching) |
| `P/ProjectSettings/QualitySettings.asset` | not changed |
| `P/Assets/Game/Editor/Forest/ForestProjectSettings.cs` (+ `.meta`) | new: `EnableGpuResidentDrawer` (`-forestGpuOcclusion` adds occlusion), `Revert`, `Report`; batch only, no menu item, refuses without `-forestAllowProjectSettings`, logs before and after, backs the four files up to `P/Library/GamexForest/ProjectSettingsBackup/<UTC>/`, reads every value back and throws if one did not take (spec Appendix C.4) |
| `P/Assets/Game/Tests/ForestPlayMode/ForestPlayModeTests.cs` | `Budget_Counts` counts under the drawer (diff `B/out/D1/ForestPlayModeTests.cs.diff`, original `B/backup/D1-pre-edit/Tests/`) |
| Generated by the forced rebuild | `Forest.unity`, `ForestData/*`, `ForestKit` textures and reports: placements and all 104 meshes byte-identical to before |

Originals: `B/backup/pre-D1/` (the four settings files; md5 = `pre-forest.md5`). Expected checksums after D1:
`B/backup/post-D1.md5`; `B/tools/d1/check-preexisting-d1.sh` checks the 59 files against `pre-forest.md5` except these three
(result: 0 differ). `tools/check-preexisting.sh` now reports exactly these 3 as changed.

Command used: `Unity -batchmode -projectPath P -executeMethod Gamex.EditorTools.ForestProjectSettings.EnableGpuResidentDrawer
-forestAllowProjectSettings -quit` (`B/logs/D1-switch-enable.log`). Tested afterwards: `Revert` gives back the pre-D1 HDRP
asset and Graphics settings byte for byte and ProjectSettings with an explicit "static batching on" entry; `EnableGpuResidentDrawer`
then reproduces `post-D1.md5` exactly (`D1-switch-revert-test.log`, `D1-switch-reenable.log`).

**Why the test changed, and why this is not a weaker check.** Under the drawer (measured): every MeshRenderer it owns reports
`Renderer.isVisible` true whether or not it is drawn, so the old estimate counted all 9,726 renderers (28,222 "draws", the test
would fail on a number that means nothing); 'Shadow Casters Count' drops from 483 to 165 because the drawer's casters are not in
it (the budget would pass blind); 'Draw Calls Count' still reads 0. `Budget_Counts` now asks the drawer (reflection, after the
first render), and when it is on: draws = 2 x its camera draw commands + its shadow draw commands + the old estimate over
renderers it does not draw (if it has not taken every active MeshRenderer, all MeshRenderers are counted on top, high side);
shadow casters = the engine's count + the instances the drawer drew into the shadow maps (summed over cascades, so higher than
the old count: 645 vs 483 at crouch_hide); it asserts the drawer reported draws; and it **fails** if GPU occlusion culling is on,
because 'Triangles Count' and 'Vertices Count' do not see the drawer's indirect draws (10.88 M read 3.99 M in arm C). Any
reflection failure throws. Triangles and vertices without occlusion are unchanged (10,880,228 in A and G). No budget constant,
no other test, no correctness check changed.

## 6. After the switch

- **Rebuild** (`ForestBuild.BuildAll -forestForce`, `B/logs/D1-rebuild-buildall.log`): exit 0, 34 prefabs, audit passed (3,420
  instances, 13,433 GameObjects, 19 materials, 88 meshes, 2 budget warnings); `placements.json` and 104 meshes byte-identical to
  before (`B/out/D1/before|after/`).
- **Tests** (`B/tools/d1/tests.sh run1`): forest EditMode **35/35** `B/out/D1/tests/run1/forest-editmode.xml`; forest PlayMode
  **10/10** `.../forest-playmode.xml` (Budget_Counts: triangles 10.88 M, draws 463 estimated, SetPass 39, shadow casters 645,
  texture 930.3 MiB, mesh 382.4 MiB; Budget_FrameTime stopwatch 9.58 ms, p95 10.19); greybox **7/7** `.../greybox-playmode.xml`.
  All 51 D1 Unity logs: 0 `error CS`, 0 "immutable"; the only `warning CS` (28) are 4 per run of the temporary greybox capture
  (an obsolete overload, 7 runs; fixed in the master copy). 65 Unity launches, all exit 0. No exit code 198. The greybox suite renders nothing in batch mode (no drawer line in its
  log): it tests gameplay; its rendering is covered by the captures below.
- **Greybox before/after** (`B/captures/d1-greybox/`, `B/out/D1/greybox-diff.txt`):
  - edit mode, the greybox's own `SceneCapture.Capture` (2 images) and the forest harness `-captureScene` (3 images): **100 %
    identical** before and after;
  - play mode (temporary capture, 7 views, game frozen): before1 = before2 bit for bit. With the drawer: everything within 4/255
    except (a) the raptor's anti-aliased outline in raptor_close (max 42/255, ~70 pixels; in every drawer run) and (b) in 1 of 5
    drawer runs (after1) the four throwable rocks (Rigidbodies) drawn about one pixel lower (outline rings, max 143/255, ~125
    pixels in player_view; after2 = after3 = after4 = the flag run, bit for bit). Same image to the eye; not bit-identical. Cause
    not established (the drawer uploads transforms in PostLateUpdate; a one-frame difference in a settling rigidbody would do it).
- **Forest edit-mode captures** (`ForestCapture -captureSet full -captureBaseline captures/verify-m4`, `B/captures/d1-after/full/`):
  32 views passed, 0 error pixels, RMSE to verify-m4 0.0017 to 0.0030 (verify-m4 to m4-r2 was 0.0018 to 0.0030).
- **29 views after** (no flag, 3 runs; `B/out/D1/after-table.txt`, logs `D1-measure-after-{1,2,3}.log`): worst pano_90 10.45 ms
  (p95 11.08), crouch_hide 10.42 (11.34), fir_close 10.22, deadfall 10.15; 0 over 11 ms, 14 over 9.5; mean 9.50; floor 5.21 to
  5.38. These were not interleaved with A runs; the interleaved G arm (10.25 / 10.28) is the cleaner comparison.

## 7. Knobs for D2 (measured with the drawer on, not applied)

`B/out/D1/knobs-table.txt` (2 runs; the unchanged scene was measured at the start and end of each run and moved by up to 0.5 ms
between them, so deltas under ~0.25 ms are noise), `B/out/D1/dense-knobs-table.txt`, images `B/captures/d1-after/knob-fog-sheet.png`.

- **Young-fir scan level**: it is already at **4 m** (`ForestKitSpec.cs:161-163`, since M4b), not 6 m. Moving it back to 6 m:
  +0.6 to +2.45 M triangles, -0.35 to +0.31 ms (mean +0.01). "3 m": **not reachable** with screen-relative LOD heights: all 976
  groups clamp at 0.99 of the screen (a 3.5 to 6 m fir fills the screen at 3 m), so this measures "scan only when the tree fills
  the screen" (about 3 to 4 m): mean -0.24 ms, crouch_hide -0.26 ms / -1.83 M, trunk_close -0.66. Spec section 15: never below
  4 m without the owner.
- **Shorter fog plus matching culls** (temporary volume: fog mean free path 20 m instead of 70 m, depth extent = cull distance;
  every LOD group culled at the distance; contrast left exp(-d/20): 47 % at 15 m, 37 % at 20 m, 8 % at 50 m, 5 % at 60 m):

| At | fog 20 m alone | + cull 70 m | + cull 55 m | + cull 45 m |
|---|---|---|---|---|
| today's density, mean of 10 heavy views | -0.27 ms | -0.39 ms | -0.51 ms | -0.64 ms |
| today's density, worst views | pano_90 -0.32, crouch -0.05 | -0.52, -0.12 | -0.54, -0.16 | -0.73, -0.37 |
| today's density, triangles | 0 | -0.1 to -0.6 M | -0.2 to -1.1 M | -0.5 to -1.5 M |
| 2x density, mean (worst) | | | -0.56 (pano_90 -1.12) | -0.82 (-1.17) |
| 2x density, triangles | | | -0.5 to -2.1 M | -1.1 to -3.0 M |

  Visually (sheet): at 55 m the cull is invisible in the 20 m fog (max 18 to 25/255 on 1 to 2 % of pixels); at 45 m faint far
  silhouettes go (max 24 to 30/255 on 13 to 20 %). The fog itself (20 m) is a strong look change for the owner: everything past
  ~40 m dissolves. The "fog alone" saving is near the noise level.
- **Density** (stand-in: every LOD group duplicated nearby, drawer on): 2x -> worst 13.1 to 13.6 ms, 20.6 M triangles; 3x -> 16.0 ms,
  32 M. Draw commands stay ~300 to 430 at any density (instancing), SetPass 36 to 38.

## 8. Headroom for D2

At today's density the worst view is 10.25 to 10.45 ms (11 ms budget): **about 0.55 to 0.75 ms** of headroom (was ~0.3). Adding
density costs about 2.7 ms per "1x" of everything (mostly geometry: the stopwatch follows triangles once draws are instanced). So,
on these numbers: fog 20 m + cull 55 m (-0.6 ms at 2x) and the young-fir scan clamp (-0.25 ms) buy roughly +30 to +40 % more
objects, not +100 to +200 %; the rest of D2's density has to be paid by cheaper levels (knobs 3 and 4 of spec section 10: LOD0
distances and foliage targets of the new objects), by placing the density near the eye (thickets, understorey) where it blocks
sight instead of uniformly, and by re-measuring. Revisit GPU occlusion only if solid occluders (rock faces, terrain ridges,
dense trunk walls) are added: the A/B is one flag (`-force-gpuocclusion`).

## 9. Not verified, open

- GPU frame time itself (the stopwatch is CPU + GPU serialised; FrameTiming still unusable: 8 of 240 frames, 34.8 ms) and a player
  build: the drawer saves CPU, which a development build would show better than the Editor.
- The greybox's one-pixel rigidbody offset in 1 of 5 drawer runs: cause not established; nothing moved in the frozen game.
  Gameplay with moving dinosaurs was not watched under the drawer beyond the 7 tests.
- The drawer-aware shadow-caster count is defined differently from the engine's (summed over cascades): the 2,500 budget is
  now checked against a higher number than before (645 vs 483 at crouch_hide), never a lower one.
- 'Draw Calls Count' and 'Batches Count' still read 0 with and without the drawer.
- Knob numbers are two runs each with ~0.25 ms noise; the 2x knob row is one run.
- `Logs/forest-work/` (the in-project copy of the records) was not updated.
