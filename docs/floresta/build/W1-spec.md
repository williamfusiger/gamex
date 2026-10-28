# W1 build specification: wind in the foliage, and foliage that parts around bodies

Written 2026-10-15 for the two engineers of step W1; **revision 2**, begun the same day after two reviews of the first
version and completed 2026-10-16 (what changed and why: "Revision notes" at the end). It is self-contained: you do not
need the research reports to build from it. They hold the evidence and the source line numbers behind every statement, if
you need them: `research-W1/shader.md`, `research-W1/wind-data.md`, `research-W1/runtime-perf.md`. Read `BRIEF-wind.md`
first (project, rules); this spec does not repeat it in full. The revision's own evidence (simulations, compile probes) is
in `B/probes/W1/rev/`, with the re-runs of 2026-10-16 in `B/probes/W1/rev/out/recheck/`. The first version is kept as
`B/probes/W1/rev/W1-spec.v1.md`.

Abbreviations:

| Name | Path |
|---|---|
| `P` | `/Users/william/Documents/GitHub/gamex` (the project) |
| `F` | `P/Assets/Game/Editor/Forest` (editor code, namespace `Gamex.EditorTools`) |
| `SCRATCH` | `/private/tmp/claude-501/-Users-william-Documents-GitHub-gamex/e48fad55-bf2b-4ea1-ae11-7561f14da1a4/scratchpad/forest` |
| `B` | `SCRATCH/build` |
| `HD`, `CORE`, `SG` | the HDRP, Core RP and Shader Graph 17.6 packages in `P/Library/PackageCache` |

Two work packages, done in order by two engineers:

- **I1, shader and data**: the wind Shader Graphs and their HLSL, the wind materials with a switch back to HDRP/Lit, the
  baked per-vertex wind data, the kit rebuild, and the static regression. I1 ships no runtime controller: with every
  wind global at zero the forest must render as it does today.
- **I2, runtime and performance**: the wind controller, the interaction field and pushers, the player's pusher, a
  raptor-sized proxy for tests, scene integration, the thin-stem navigation rule, motion captures, the player-build
  measurement and the knob tuning.

I2 starts only when I1's done criteria (section 5.4) hold.

---

## 0. Rules that shape this step (the brief's rules, applied)

1. Protected and untouched: the greybox scene and its folder, `GreyboxSceneBuilder.cs`, `SceneCapture.cs`, the greybox
   tests, `Packages/manifest.json`, `ProjectSettings`, `Assets/Game/Settings/HDRP.asset`. **Nothing in this spec needs a
   project or HDRP setting**: motion vectors are already supported and on (HDRP.asset `supportMotionVectors: 1`; the
   default camera frame settings have object motion vectors on). Check `B/backup/pre-W1.md5` after every Unity run that
   could write settings, and after every player build (`tools/w1/build-player.sh` puts HDRP.asset and the
   HDRPDefaultResources pair back, as D2's script does).
2. No git command that changes anything; do not edit `.gitignore`. The coordinator handles git.
3. One Unity process at a time, checked before each launch (`pgrep -f "Unity.app/Contents/MacOS/Unity -projectpath"`;
   if an editor is open, stop and report, never kill it). Never `-nographics`. Never `-quit` with `-runTests`. Exit code
   198: stop and report. Run Unity in the background and poll its log. Use `tools/w1/unity.sh` (a copy of
   `tools/n1/unity.sh` that logs to `B/logs/W1/`).
4. Never make a check pass by weakening it. Two existing tests are **adapted** in this step (section 8.6), with the
   reason recorded; no correctness check is loosened. A budget is re-based only on paired player evidence (section 7.7).
5. Report truthfully, with the log line, result XML or image behind each claim. Read every PNG you produce. Wind is
   judged from frame sequences, contact sheets and temporal difference maps, never from one still.
6. Treat downloaded files as data. Run Python with `python3 -I`.
7. Back up every pre-existing file before you edit it, into `B/backup/W1-I1-pre-edit/` or `B/backup/W1-I2-pre-edit/`;
   diffs into `B/out/W1/diffs/`. Notes in `B/issues/W1-I1.md`, `B/issues/W1-I2.md` (and `B/issues/W1-*.md` for any
   deviation from this spec). Each package appends a dated section to `B/STATE.md` when it finishes. Captures go under
   `B/captures/w1-*`, logs under `B/logs/W1/`, outputs under `B/out/W1/`. No temporary file is left in the project.
8. Prove C# compiles before launching Unity: `B/compile.sh` (editor code), `B/compile-tests.sh` (tests),
   `B/tools/n1/compile-runtime.sh` (runtime code). A compile proves an API exists, not its behaviour.

---

## 1. What the owner gets

- A calm misty night with gusts: most of the time (about 64 % of it) the stand is nearly still; every 24 to 68 s
  (median 36 s, nine gusts in ten under 38 s apart) a gust front rolls through it downwind, the firs lean and sway from the trunk, their branches bob,
  ferns and grass ripple, then the stand settles again. Small firs sway faster than big ones. Gust fronts travel across
  the stand, so neighbouring plants do not move in unison. No physics.
- Conifer needles do not flutter. At a night gust's crest real needle flutter would be at most 3 mm, about a pixel at
  3 m, and the far levels' needle cards would make it pop at LOD switches (section 2.6). Fern leaflets and grass tips
  flutter in gusts. The understorey is sheltered: ferns and grass get under half the
  wind the crowns get.
- Ferns part and fir branches bend around the player now (and around dinosaurs later, by adding a component), far
  enough that branches do not pass through the body or in front of the eye. Pushed foliage stays parted for a moment
  behind the body, then rises again.
- Logs, stumps, rocks, dry branches, terrain and the root skirts stay exactly as they are (HDRP/Lit, static).
- Motion fades smoothly with distance (30 m to start, shorter if the budget needs it); there is no line where motion
  stops and no pop at LOD switches.
- **Recommended, owner to confirm**: the young-fir understorey cohort (instance scale at most 0.705: 690 of 1,706 young
  firs) no longer blocks the player or the NavMesh; a body passing through bends such a sapling over from its base.
  Everything thicker blocks as today. The coordinator's earlier default, 0.60, cuts through the densest band of sapling
  sizes, so look-alike neighbours would behave differently (section 2.9).
- A clean hook for rustle sounds and stealth noise later: `FoliageInteraction.Rustled`, `FoliagePusher.Contact` and
  `FoliageInteraction.SampleDensity`. No audio now.
- The player build still meets the owner's budget: worst standard view median at most 12.5 ms. W1 also keeps within a
  cost cap of its own, so the dinosaurs keep room: provisionally 0.5 ms (paired wind on minus off) at the worst view,
  for the owner to confirm (section 7.1). The model expects the starting configuration to miss that cap at pano_90.
  Section 7.4 orders the remedies so that none changes N1's approved night without the owner.

---

## 2. Decisions, with reasons

### 2.1 Shader approach: our own HDRP Lit Shader Graph, with all vertex logic in one HLSL file

**Decision.** Two HDRP Lit Shader Graphs written by us and generated by an editor script: `ForestWind.shadergraph`
(UV0 mapping) and `ForestWindTriplanar.shadergraph` (triplanar mapping, used only by the tall firs' triplanar bark).
Both call the same vertex function in `Assets/Game/Shaders/ForestWind.hlsl` through one File-mode Custom Function node.
The fragment side is one Custom Function per graph in `Assets/Game/Shaders/ForestSurface.hlsl`. Properties carry
HDRP/Lit's reference names, so `ForestMaterials` keeps its recipes.

**Reasons.**

- HDRP runs a graph's vertex function in every pass (depth prepass, G-buffer, motion vectors, every shadow view) with
  the same time and the main camera's position, so depth, colour and shadows agree by construction.
- One graph family can reproduce every property `ForestMaterials` sets today (alpha clip, double-sided Mirror,
  Translucent with the foliage diffusion profile and thickness, base/normal/mask maps with the same remaps, tint
  variants). The same graph serves the opaque bark and dead branches (per material: `_AlphaCutoffEnable 0`,
  `_DoubleSidedEnable 0`, `_MaterialID 1`), which **must** run the same vertex code or needles separate from branches.
- A second graph is needed because tall fir b draws its branches, and tall fir c its trunk, with
  `fir_tree_01_bark__triplanar` (prefab material lists: b `[bark__triplanar, trunk_b, ground, twig]`, c
  `[bark, bark__triplanar, ground, twig]`). A triplanar surface that moves must be projected from its **rest** position
  or its texture swims. HDRP's "Predisplacement" position does **not** give it: without tessellation,
  `positionPredisplacementRWS` is written after the Shader Graph vertex function has run
  (`HD/Runtime/RenderPipeline/ShaderPass/VertMesh.hlsl`: `ApplyMeshModification` at 171-181, then `positionRWS` at 184,
  `output.positionPredisplacementRWS = positionRWS` at 221), so it is the displaced position. The triplanar graph
  therefore carries the vertex-stage (pre-graph) absolute position and world normal to the fragment through two Custom
  Interpolators (section 3.2). A keyword or a dynamic branch in one graph would either fight
  `HDMaterial.ValidateMaterial` or make every foliage pixel pay for nine texture samples; a second graph generated from
  the same template costs nothing at run time.
- Logic in HLSL instead of nodes: reviewable, diffable, and the CPU mirror of the gust function can be checked against it.
- Every HDRP Shader Graph pass is compiled with `DOTS_INSTANCING_ON`, so the GPU Resident Drawer accepts the graphs
  (to be confirmed in Unity: zero `MissingDOTSInstancing` exclusions).

**What is taken from Unity's samples, and what is not.** Nothing is copied as a file; ideas are ported into our HLSL,
with a header line naming their origin (the Shader Graph package is under the Unity Companion License, which allows it).

| From `com.unity.shadergraph/Samples~` | Taken? |
|---|---|
| `FoliageWind.shadersubgraph`: gusts as a field of the pivot position in a wind-aligned frame, scrolled downwind over time | **The idea**, as a sum of three sines shaped into gusts and lulls (cheaper than per-vertex gradient noise), in a frame that never rotates (section 3.5). Not the sub graph: its calm direction ignores `WindDirection`, so any direction other than 0 makes the direction swing and the intensity collapse (0.17 at 90 degrees) |
| Per-object random from the pivot position | **Yes** (as an integer hash): it survives the GPU Resident Drawer and LOD switches. Not Instance ID, which is not stable under the drawer |
| `DistanceCutoff.shadersubgraph`: amplitude fades continuously to 0 with camera distance per object | **Yes**: no pop where motion stops, works with LOD cross-fade off |
| `BendDeformer`: bend that preserves length | **The principle** (our trunk bend preserves the distance to the pivot). Not the camera-facing blade billboard |
| Whole-plant rotation about the pivot weighted root to tip (`Wind.shadersubgraph`) | **The principle**, through our baked weights. Not its UV1 input (our meshes have no UV1; UV1 stays free for lightmaps) |
| `FeatureExamples/.../FoliageWind.shadergraph` | **No**: it only paints the wind as a colour and moves no vertex |
| Fern / Nettle / Grass production graphs, their materials | **No**: URP-only translucency, Instance ID, terrain-detail assumptions, the normal left unrotated, and their HDRP materials have the motion vector pass off |

Never import a sample through the Package Manager and never copy a sample file with its `.meta`: a later sample import
would clash on GUIDs.

### 2.2 Pushers: a decaying interaction texture, not a stateless array

**Decision.** A 256 x 256 RGBA16F field around the camera (0.2 m texels, a 51.2 m window), stepped once a frame by a
small compute shader: shift with the camera, decay, then stamp every registered `FoliagePusher`. The vertex shader
samples it once per vertex. A copy of last frame's field is kept for motion vectors.

**Reasons.**

- One texture fetch per vertex whatever the number of pushers: an estimated 0.1 to 0.2 ms at the worst view, against
  0.4 to 0.9 ms for eight stateless pushers, whose loop would run in up to five passes (model, `runtime-perf.md` 3.3).
- Recovery and trails come from the decay: ferns stay parted for a moment behind a raptor and then rise, which is what
  the owner will judge by eye. A stateless array springs back the instant the body has passed.
- Any number and shape of pushers (several dinosaurs, a tail sweep, a thrown rock) without touching the shader.
- Costs accepted: a compute shader and render-texture life cycle, a 2D field with a height band instead of true 3D
  capsules, and pushes beyond 25 m of the camera do not show (the mist and the LODs hide them anyway).
- Precision of the band-top channel (half float): 1/64 m between 16 and 32 m of altitude, 1/32 m up to 64 m. The forest
  spans y = -1.8 to 18.1 m (`out/N1/final/placements.json`), so W1 stores world Y directly. Above 256 m half floats step
  0.25 m. For the island map, store the band top relative to a reference height carried in the rect (risk in section 9).

### 2.3 Motion vectors

- **Wind is a function of the Shader Graph Time node only.** HDRP's motion vector pass re-evaluates the vertex graph
  for the previous frame by substituting only that time input (`_LastTimeParameters`). Everything that moves within a
  frame (sway, branch bob, flutter, gusts, ripples) is computed from it. The CPU only sets slowly varying values: the
  lean direction (wandering plus or minus 15 degrees over about 90 s, at most 0.018 rad/s, so 0.03 mm a frame at a
  0.1 m lean) and the gust peak (plus or minus 25 % over about 40 s). Both change by far less than a pixel per frame.
  The gust field's own frame never rotates (section 2.7). The one exception is the optional gust texture of knob 2
  (section 7.4): its previous-frame evaluation reads the current texture. The steepest front changes W by about 0.007
  a frame at 60 fps (peak 0.69 x the shape's largest slope of 0.6 per second), at most 3 mm at the top of the tallest
  young fir: under half a pixel at 8 m.
- **Pushes use Custom Velocity.** The graph has Custom Velocity on; the vertex function outputs
  `Velocity = push(current field) - push(previous field)` in object space. HDRP subtracts it from the previous-frame
  position, which makes the previous position exact. (Fallback in section 9: detect the previous-frame evaluation by
  comparing the Time input with `_TimeParameters.x` and sample the previous field there.)
- **The motion vector pass only where it pays.** A Shader Graph material starts with its `MotionVectors` pass off: on
  its first validation HDRP turns the pass off and sets the override tag `MotionVector = User` on **every** Shader Graph
  material, whatever the variant. After that, a pass that has been turned on stays on through later validations
  (`HD/Runtime/Material/Unlit/BaseUnlitAPI.cs:363-392`: `allowMotionVectorPass = !GetShaderPassEnabled(...)`). Under
  the GPU Resident Drawer, a material whose pass is on is drawn in the motion vector pass every frame, moving or not. So
  every wind material comes in two variants: the plain one (pass off) and `__mv` (pass on). The `__mv` variants are
  assigned only to the "pushed set": the young-fir scan level (LOD0), fern patch LOD0, single fern clumps, grass.
  Reason: wind moves foliage 0.15 to 0.5 px a frame at 4 to 8 m, which TAA's neighbourhood clamp absorbs; pushes move
  fronds 4 to 8 px a frame and need vectors. The TAA ghosting check (section 8.3) decides whether young-fir LOD1 (where
  a passable sapling is bent over at 3 to 8 m) or tall LOD0 also need them, and whether the scan level can go without.

### 2.4 Wind data: one RGBA8 vertex colour, and a compact vertex layout that pays for it

**Decision.** Every level of every tree, fern and grass mesh carries one vertex colour, UNorm8 x 4:

| Channel | Meaning |
|---|---|
| R | trunk sway weight, 0..1 |
| G | branch / frond weight, 0..1 (0 on the trunk) |
| B | flutter weight, 0..1 (0 on wood) |
| A | branch / frond phase, 0..1, wraps |

It is computed on the **finished** level from per-tree constants measured once on the source scan, so it is a function
of position in the tree and identical across LODs (formulas in section 4). The same change stores those meshes'
tangents as Float16 x 4: the vertex goes from 48 B to 44 B **with** the colour.

**Reasons.**

- COLOR is free: HDRP/Lit reads it only for displacement and layered blending, which no kit material uses. UV1 stays
  free (lightmaps), UV4 and UV5 are taken by HDRP's motion vector semantics, UV2/UV3 stay free for later tiers.
- Computing on the finished level leaves the thinner, `Extract`, `Merge` and `ClusterDecimate` untouched.
- Mesh memory is the binding constraint: 382.4 MiB against the 400 MiB budget (`Budget_Counts`), because readable meshes
  are counted twice. The colour alone would add 24.4 MiB (about 407 MiB, a failure). With Float16 tangents the wind
  meshes shrink by 4 B a vertex: expected about 358 MiB. UV0 stays Float32 (bark UVs reach u 5.0 and v -1.0..1.47).
  Normals stay Float32 (second lever if ever needed). Static props keep today's 48 B layout.

### 2.5 What moves, and how

| Prefabs (instances) | Materials on the wind graphs | What moves |
|---|---|---|
| Young firs `fir_sapling_medium_a/b/c` (1,706), every level and the `LOD0_Shadow` proxy | `fir_sapling_medium_branches`, `fir_sapling_medium_twigs`, `__tint1`, `__crowncore`, and their `__mv` variants | Stem lean and sway (R), branch bob (G), pushes on branches (push weight derived from G, section 3.5 step 10); passable saplings (scale <= 0.705) also lean over from the base under a push. No needle flutter (B is baked but the profile's flutter is 0) |
| Tall firs `fir_tree_01_a/b/c` (796), every level | `fir_tree_01_bark`, `fir_tree_01_trunk_a/b/c`, `fir_tree_01_twig`, `__tint1`, and `fir_tree_01_bark__triplanar` on the triplanar graph; `__mv` variants of all | Slow trunk lean and sway at the top (R), branch bob (G), pushes on the lowest branches (under the body's height band). No needle flutter |
| Fern patches `fern_patch_a/b/c` (both levels) and single clumps `fern_02_a..d` | `fern_02`, `fern_02__tint1`, `__mv` variants | Frond sway and bob (G) in sheltered wind with a ripple along the wind, leaflet flutter (B), strong parting and flattening under pushes |
| Grass patches `grass_patch_a/b` | `grass_medium_01`, `__mv` | Blade sway (G) in sheltered wind with a ripple along the wind, tip flutter (B), parting under pushes |
| Logs, stumps, rocks, dry branches, terrain, `forest_ground_06__ground` (the tall firs' root skirt) | none: **HDRP/Lit, unchanged** | Nothing. The skirt lies below 0.3 m where R is about 0.001 of the top amplitude, so the trunk flare meets it without a gap |

Before switching any material, I1 confirms that no static prop shares it (today: none does; the map of materials to
prefabs is in `B/out/W1/material-users.txt`, to be written by I1 from the prefab files).

### 2.6 Motion character

Starting values. I1 tunes them on pose sequences and I2 on play-mode sequences, within half to twice these values, with
the reason recorded. Push amounts and push gains may only go up, because section 8.4 checks a floor on them. The owner
reviews the MP4s and contact sheets. Amplitudes are object-space metres at effective wind W = 1, so a larger instance
sways more. Push amounts are world metres. Periods are at instance scale `s = 1` and scale as `s^k`.

| Class | Trunk amplitude at R = 1 / period | Branch amplitude at G = 1 / period | Period exponent k | Flutter at B = 1 / frequency | Shelter | Ripple wavelength | Instance phase spread | Branch push / push gain / flatten share | Stem lean at full press |
|---|---|---|---|---|---|---|---|---|---|
| Young fir | 0.30 m / 2.2 s | 0.10 m / 1.25 s | 0.5 | **0 (off)** | 1 | none | 1 | 0.55 m / 5 / 0.15 | 50 degrees (0.873 rad), passable saplings only |
| Tall fir | 0.45 m / 4.5 s | 0.15 m / 2.4 s | 0.5 | **0 (off)** | 1 | none | 1 | 0.55 m / 5 / 0.10 | none |
| Fern | none (0) | 0.12 m / 1.1 s | 0 | 0.020 m / 3.5 Hz | 0.45 | 6 m | 0.25 | 0.45 m / 2 / 0.70 | none |
| Grass | none (0) | 0.08 m / 0.9 s | 0 | 0.015 m / 4.0 Hz | 0.45 | 4 m | 0.25 | 0.30 m / 2 / 0.60 | none |

- **Conifers stiff and slow, and small ones faster**. The effective period is `period x s^k x (0.85 + 0.30 h)`, with
  `h` the pivot hash. Young-fir scales run from 0.550 to 1.545 (median 0.902; 3.2 to 13.5 m tall), so trunk periods run
  1.4 to 3.1 s. Tall-fir scales run from 0.623 to 1.250, so trunk periods run 3.0 to 5.8 s (`out/N1/final/placements.json`).
  Geometrically scaled cantilevers have a natural frequency proportional to d / H^2, so proportional to 1/s. k = 0.5
  is the conservative start, tunable from 0.5 to 1. Each fir has a mean downwind lean (60 % of the amplitude) with a
  slow oscillation of 40 % about it, from two incommensurate sines (ratio 1.618), plus a small cross-wind wander (15 %).
  Branches bob mostly vertically. At a night gust's crest (W = 0.55) the top of a young fir at scale 1 and R = 1 leans
  about 0.10 m and swings about 0.07 m either side: 13 px and 9 px seen from 8 m (7.8 mm a pixel there at 70 degrees
  over 1,440 rows). In a lull (W = 0.03) it leans 5 mm and swings 4 mm, which reads as still.
- **No needle flutter on conifers** (profile flutter 0; step 9 is skipped by a uniform branch, so it costs nothing).
  B = d_a / 0.30 m, and the scan's 5 to 8 cm needles have B = 0.17 to 0.27 (`wind-data.md:175`). At a night crest a
  2 cm flutter would move them `0.020 x 0.55 x 0.27` = 3 mm at most: about 1 px at 3 m and half a pixel at 6 m (2.9 and
  5.8 mm a pixel), on needles a pixel or two wide. At the old mean strength it was 1.9 mm, 0.3 to 0.65 px. Only 30 cm
  cards (B = 1.0) would reach 11 mm, and that would make flutter pop at LOD switches (section 2.8). The B channel stays
  baked for a later tier.
- **Ferns and grass sheltered, and carried by the gust**. Under a dense canopy, the wind near the floor is a fraction
  of the wind at the crowns, so their W is multiplied by the shelter 0.45. At a night crest, fern tips bob
  `0.7 x 0.12 x 0.55 x 0.45` = plus or minus 2.1 cm and flutter plus or minus 5 mm. In a lull they bob about 1 mm. The
  gust, not a constant bob, carries the visible motion. Periods are 0.9 to 1.1 s, with higher flutter frequency.
- **No unison, but carpets ripple instead of boiling**:
  - Firs: per-instance phase and a period jitter of plus or minus 15 % from the pivot hash (instance phase spread 1),
    and per-branch phase from the A channel.
  - Ferns and grass: one phase per clump. A carries a clump phase (spread 0.25 cycle) plus or minus 0.12 cycle per
    frond or blade (section 4.3), and the instance hash adds only 0.25 cycle. A ripple term travels along the gust
    direction (wavelength 6 m for ferns, 4 m for grass) downwind at wavelength / period, 4 to 6 m/s.
  - Per-vertex flutter phase comes from the rest position.
  - Gust fronts travel downwind, so a stand leans in sequence as a gust crosses it.
- **Pushes move branches out of bodies**:
  - The push weight is derived in the shader from G (section 3.5 step 10). It reaches 1 at 0.6 m from the trunk surface
    for firs (gain 5) and at half the frond or blade reach for ferns and grass (gain 2). The old weight was G itself,
    which gave 2, 6 and 10 cm at 0.5, 1 and 1.5 m from a young-fir stem.
  - The fir branch push, 0.55 m, is at least the largest pusher's radius (0.45 m, the raptor proxy) plus 0.1 m.
  - Its magnitude comes from the field's press channel. The push direction flips across the body axis, so under
    bilinear filtering the direction channels cancel there, and branches would stay inside the body. Vertices on each
    side of the axis go to their own side, as fronds do when something walks through them.
  - Passable saplings lean over from the base, keeping their length: 50 degrees at full press. A point at height h then
    lies `h x sin 50` from the axis, so from 0.7 m up the stem clears a 0.45 m body (0.7 x 0.766 = 0.54 m). It is 1.26 m
    aside at eye height. A cantilever loaded at 1 m that deflects 0.5 m there tilts about 43 degrees above the load.
  - Section 8.4 checks both geometrically.

### 2.7 Night wind values

Misty nights are calm (mist forms in light air), and for a stealth-horror forest the scary beat is a gust rolling
through a still stand, not constant motion. So the wind rests at a calm value most of the time and rises only in gusts:
`W = calm + (peak x wander(t) - calm) x e`, with `e` the shaped gust of section 3.5 (0 in a lull, 1 at a crest). Values
live in `ForestLightingPresets.Preset` so the wind switches with the preset:

| Field | Night (default) | OvercastMist |
|---|---|---|
| `windBearing` (degrees, the direction the wind blows **toward**, clockwise from +Z) | 70 | 70 |
| `windCalm` (W between gusts) | 0.03 | 0.10 |
| `windGustPeak` (W at a gust's crest, before the wander) | 0.55 | 0.70 |
| `windGustWavelength` (m between gust fronts) | 120 | 150 |
| `windGustSpeed` (m/s the fronts travel) | 3.5 (a crest every 34 s at a point) | 5.0 (every 30 s) |

What these values give, from the CPU mirror of the gust (`B/probes/W1/rev/gust_tune.py`, `final_check.py`; 25 points on
a 1 km grid, including the forest edge at 131 m and 500 m out, 7,200 s at 0.1 s). A gust event is W rising above half
way from calm to peak; it is re-armed when W falls below a quarter of the way.

| Preset | W range, mean | Share of time with W below 0.3 x mean (worst point) | Gust events apart: minimum, median | Share of time in a gust |
|---|---|---|---|---|
| Night | 0.03 to 0.69, mean 0.133 | 64 % | 24.1 s, 35.9 s (seeds 0, 0.13, 0.81: 23.8 to 24.4 s, 35.8 to 36.0 s); 90th percentile 38.0 s, longest 68.0 s (`recheck_gaps.py`) | 19 % |
| OvercastMist | 0.10 to 0.88, mean 0.219 | (no lull target: a windier preset) | 19.7 s, 31.4 s | 19 % |
| First version (strength 0.35, gustiness 0.6, wavelength 40 m) | 0.14 to 0.56, mean 0.35 | 0 % | 6.2 gusts a minute, median 10 s | (`gust_old.py`) |

The first version was a steady pulse: the three components are pure sinusoids at a fixed point, and the wind never fell
below 40 % of its mean.

`ForestWind` adds a slow wander on the CPU, from two incommensurate sines of `Time.time` each (deterministic, smooth,
no integration):
- the **lean direction**: the preset's bearing plus or minus 15 degrees over about 90 s;
- the **gust peak**: plus or minus 25 % over about 40 s (the calm value does not wander).

The gust field and the fern and grass ripple keep the preset's fixed bearing. Rotating their frame about the world
origin would shift the gust phase by `|xz| x dtheta / L`. At the wander's peak rate of 0.018 rad/s that is
`131 x 0.018 / 3.5` = 0.67 of the front's own travel at the forest edge, more than its travel beyond 200 m, so fronts
would stall, race or reverse on a larger map. A fixed frame makes the fronts move at exactly `windGustSpeed` everywhere.
A lean direction up to 15 degrees off the gust direction is physically unremarkable.

### 2.8 Distance: a per-instance fade, every LOD animated

- Every level carries the wind data and the wind material; motion fades with the camera distance of the instance's
  pivot: full until `fadeStart`, zero at `fadeEnd`, smoothstep between. Start: `fadeEnd` 30 m (the night mist hides
  detail beyond about 30 m), `fadeStart` = 0.7 x `fadeEnd` (21 m). Flutter (ferns and grass only) fades out between 7
  and 10 m.
- Beyond `fadeEnd` a uniform per-instance branch returns the rest pose, so the vertex work is really skipped. Every
  term with a zero amplitude, flutter outside its range, and the push without a valid field are skipped by uniform
  branches as well (section 3.5).
- **LOD invariance**. R, G and A are functions of position in the tree, so neighbouring levels bend alike (checked by
  `WindData_IsLodInvariant`). B is not: scan needles 0.17 to 0.27, LOD1's enlarged needles about 0.22 to 0.4, LOD2+
  cards 1.0 (`wind-data.md:41-48, 175`). Young-fir LOD1 to LOD2 switches start at 14 m x 0.55 = 7.7 m, inside a 10 m
  flutter range, and the `LOD0_Shadow` proxy draws the LOD2 cards. With conifer flutter on, flutter would roughly
  triple at the switch, and the torch shadow would flutter at card amplitude. This is the second reason the conifer
  profiles have flutter 0. Turning it on later needs B normalised per level, so that cards match the needles at matched
  anchors, plus a dB check in `WindData_IsLodInvariant`, or a `flutterEnd` of 7 m or less. Fern levels reuse the same
  clumps, so their B is identical across levels.
- The fade uses the Camera node (the main camera's absolute position), which HDRP keeps in every shadow pass, so shadows
  fade exactly as the visible foliage does.
- "Animate only near LODs" is **not** the distance mechanism: young-fir LOD switches fall anywhere from 7.7 to 21.6 m
  depending on scale, and with cross-fade off a static far level would cut motion at a patchy distance and pop. LOD
  levels only carry the motion-vector choice (2.3).
- The owner accepts a shorter distance if performance needs it; the rule is in section 7.5.

### 2.9 Thin-stem navigation rule

- **Rule**: at scene build, a young fir whose instance scale is at most `ForestLayout.PassableYoungFirMaxScale = 0.705`
  gets its trunk `CapsuleCollider` **disabled** on the instance (prefab untouched). That is 690 of 1,706 young firs
  (40.4 %): 479 of variant c, 210 of b, 1 of a. They are the whole understorey cohort (scales 0.550 to 0.680, 679 firs)
  plus 11 sparse ones up to 0.7013, with stem radius at 1 m up to about 2.4 cm and about 3.2 to 6 m tall (I2 records
  the measured range). Every other tree keeps its capsule.
- **Why 0.705 and not 0.60** (counted from `out/N1/final/placements.json`):
  - Young-fir scales are dense from 0.55 to 0.68, about 100 to 115 firs per 0.02 band, and nearly empty from 0.68 to
    0.74 (7 to 9 per band).
  - 0.60 sits inside the densest band: 87 firs between 0.58 and 0.60, 104 between 0.60 and 0.62, 46 within plus or
    minus 0.005, the nearest at 0.5999 and 0.6005. Neighbouring saplings that look identical would behave differently:
    one blocks, the next lets you through.
  - No gap exists below 0.68, so any smaller threshold splits look-alikes.
  - 0.705 is the value in the sparse zone farthest from any instance scale (3.7e-3), so the CPU rule and the shader,
    which recomputes the scale from the matrix and compares `s <= threshold + 1e-3`, cannot disagree.
- **Reasons for a scale rule**: today every young-fir capsule is the 0.12 m floor times the instance scale (0.066 to
  0.185 m), about 3.5 times the real stem, and each cuts a NavMesh hole of 0.57 to 0.69 m. A scale rule (not a
  stem-radius rule) lets the shader apply the same number from the instance matrix to lean those saplings over when a
  body passes. The NavMesh is a static bake from physics colliders (`ForestWorldRig`: `PhysicsColliders`, children of
  the surface), with no carving, so a disabled collider (if the bake ignores it, to be verified) simply leaves no hole.
- **Unchanged**: prefabs (the contract test pins one capsule per tree), the planner's clearance (0.15 x scale, which
  also drives spacing and the trail and spawn keep-outs), the audit's analytic corridor and spawn checks. The physical
  corridor checks and NavMesh connectivity can only gain margin.
- **Not owner-confirmed**: 0.705 is this revision's recommendation; the coordinator's earlier default was 0.60. The owner
  chooses among three options, each one constant:
  - 0.705, the whole cohort (recommended);
  - 0, no passable stems, which is today's behaviour;
  - 0.60, which has the look-alike inconsistency above. It also lies 1e-4 from an instance scale, and no value near it
    is 2e-3 clear of every scale (the largest gap near 0.60 is 1.05e-3, `probes/W1/rev/out/recheck/threshold.txt`). So
    it would also need the shader's margin cut from 1e-3 to 5e-5 and `Scene_PassableStemsFollowTheRule`'s spacing from
    2e-3 to 1e-4, with the reason recorded. The GPU recomputes the scale to about 1e-6. 0.70 (688 firs) is not offered,
    for the same reason: it lies 2.7e-4 from a scale.
  
  With 0.705 the thickets open for the player and, later, for the dinosaurs' NavMesh. That is a gameplay change he
  should see in the walk-through MP4s before confirming.

### 2.10 The rustle and stealth hook (no audio now)

- A CPU density map baked from the plan at scene build (`FoliageDensityMap`, 0.5 m cells, a low layer for ferns and
  grass and a mid layer for young-fir crowns), sampled by `FoliageInteraction.SampleDensity`. Gameplay never reads the
  GPU field.
- Each `FoliagePusher` exposes a per-frame `Contact` (position, density, layer, speed, intensity) and raises
  `FoliageRustle` events at a footstep-like cadence (one per metre travelled in foliage, plus one on entering), both on
  itself and on the static `FoliageInteraction.Rustled`.
- Later (the dinosaur step, not now): one enum value `NoiseKind.Foliage` and a bridge component that forwards
  `Rustled` to `NoiseSystem.Emit`; rustle audio subscribes to the same event.

### 2.11 Not done in W1

No physics, no audio, no change to `NoiseKind`, `RaptorAI`, `PlayerController` or any player code (the pusher is a
separate component on its own child of the forest player, added by the scene builder), no mesh-bounds padding (it
would change LOD group sizes and break the bounds checks; see the culling risk in section 9), no project or HDRP setting.

---

## 3. The shader, exactly

### 3.1 Files and names

| Asset | Shader name / function | Notes |
|---|---|---|
| `Assets/Game/Shaders/ForestWind.hlsl` | `ForestWindVertex_float` (the Shader Graph entry), `GamexWindDeform`, `GamexGust`, `GamexHash01` | All wind and push maths; declares the global properties of 3.4. `GamexWindDeform` takes the world-to-object 3x3 as an argument and uses no pipeline matrix, so a compute kernel can run it; `ForestWindVertex_float` only gathers `GetWorldToObjectMatrix()` and calls it. Header names the sample ideas it ports. Declares no sampler: HDRP declares `s_linear_clamp_sampler` (`HD/Runtime/ShaderLibrary/ShaderVariables.hlsl:123`), and a compute kernel that includes the file declares its own |
| `Assets/Game/Shaders/ForestSurface.hlsl` | `ForestSurfaceUV_float`, `ForestSurfaceTriplanar_float` | Fragment sampling and normal resolution, equal to HDRP/Lit's (3.6). Includes `Packages/com.unity.render-pipelines.core/ShaderLibrary/NormalSurfaceGradient.hlsl` itself (it has an include guard) |
| `Assets/Game/Shaders/ForestWind.shadergraph` | `Gamex/ForestWind` (`m_Path` "Gamex") | Generated; UV0 mapping |
| `Assets/Game/Shaders/ForestWindTriplanar.shadergraph` | `Gamex/ForestWindTriplanar` | Generated; triplanar from the rest position carried by Custom Interpolators |
| `Assets/Game/Tests/ForestEditMode/ForestWindProbe.compute` (I1) | kernels `Gust`, `Deform` | Test-only: includes `ForestWind.hlsl` and evaluates `GamexGust` and `GamexWindDeform` on given inputs, so tests compare the GPU code with the CPU mirror and check displaced positions geometrically (8.4, 8.6). Never referenced by a scene, so it is not in the player |
| `Assets/Game/Shaders/FoliageInteraction.compute` (I2) | kernels `Step`, `Gust` | The field step; `Gust` writes the optional gust texture of knob 2 (section 7.4) |

Rules for the two includes and the two compute files:
- Both `.hlsl` files have include guards (`GAMEX_FOREST_WIND_HLSL`, `GAMEX_FOREST_SURFACE_HLSL`).
- `ForestWindVertex_float` is the only function in `ForestWind.hlsl` that names pipeline symbols (`GetWorldToObjectMatrix`,
  `UNITY_MATRIX_P`, `SHADERPASS`). It sits inside `#ifndef GAMEX_WIND_COMPUTE`. HLSL compilers check every function, not
  only those that are called, so without the guard a compute file would not compile.
- Each compute file defines `GAMEX_WIND_COMPUTE`, includes
  `Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl` (for the texture macros), declares
  `SAMPLER(s_linear_clamp_sampler)`, then includes `ForestWind.hlsl`.
- The C# that dispatches a kernel sets every value the kernel reads on the `ComputeShader` itself, by the global's id
  (`SetVector`, `SetTexture`), the wind globals included. Whether a compute shader sees values set with
  `Shader.SetGlobal*` is not shown for this Unity version: `ComputeShader.SetTextureFromGlobal` exists, which suggests it
  may not. Explicit values remove the question.

Every new asset gets a `.meta` written by us **before** Unity first imports it, with a fixed GUID: the first 32 hex
digits of MD5(`"gamex-w1/" + file name`). Importer GUIDs: `.shadergraph` ScriptedImporter
`625f186215c104763be7675aa2d941aa`, and the meta is exactly the format of
`SG/Samples~/ProductionReady/Lit/HDRPLit.shadergraph.meta`: a `ScriptedImporter` block with `internalIDToNameTable`,
`externalObjects`, `serializedVersion 2`, empty `userData`, `assetBundleName` and `assetBundleVariant`, and `script`,
and **no importer fields**. Do not write `m_UseAsTemplate`: the importer hides a template's shader
(`SG/Editor/Importers/ShaderGraphImporter.cs:159`, `hidden: m_UseAsTemplate && !m_ExposeTemplateAsShader`), so
`Shader.Find("Gamex/ForestWind")` would fail. `.hlsl`: ShaderIncludeImporter (copy the format of
`SG/Samples~/Common/Subgraphs/PseudoSubsurface.hlsl.meta`, our own GUID); `.compute`: ComputeShaderImporter (copy the
format of an existing `.compute.meta` in the HDRP package, our own GUID). Custom Function nodes reference the `.hlsl` by
that GUID (`m_FunctionSource`).

### 3.2 Graph settings (both graphs; serialized names from `shader.md` 2.2)

| Setting | Value | Serialized |
|---|---|---|
| Targets | HDRP only, Lit | `HDTarget`, `HDLitSubTarget` |
| Surface / rendering pass | Opaque / Default | `m_SurfaceType 0`, `m_RenderingPass 1` |
| Alpha clipping | On (materials switch it per material with `_AlphaCutoffEnable`) | `SystemData.m_AlphaTest true` |
| Alpha clip shadow threshold | Off | `BuiltinData.m_AlphaTestShadow false` |
| Double-sided | Mirrored Normals (materials switch with `_DoubleSidedEnable`) | `m_DoubleSidedMode 3` |
| Fragment normal space | **World** in both graphs: the HLSL resolves the normal map with HDRP/Lit's surface-gradient maths (3.6) | `LightingData.m_NormalDropOffSpace 2` |
| Material type mask | Standard + Translucent | `HDLitData.m_MaterialTypeMask 34` (not Colored Translucent) |
| Receive decals / SSR | On / On (keeps today's render queue 2475 for alpha-tested foliage) | defaults |
| Specular occlusion | From AO | `m_SpecularOcclusionMode 1` |
| LOD cross-fade | Off | `m_SupportLodCrossFade false` |
| Custom Velocity | **On** (adds the Vertex block Velocity) | `SystemData.m_CustomVelocity true` |
| Precomputed velocity, depth offset, tessellation, distortion, ray tracing | Off | |
| Precision | Single | `GraphData.m_GraphPrecision 1` |
| Diffusion Profile block | **Left unconnected**, so `HDMaterial.SetDiffusionProfile` works unchanged | |

Blocks:
- Vertex: Position, Normal, Tangent (object space), Velocity (object space). The triplanar graph adds two Custom
  Interpolator blocks, `VertexDescription.RestPositionAWS#3` and `VertexDescription.RestNormalWS#3`, serialized like
  `Fern.shadergraph`'s `VertexDescription.Falloff#1` (`Fern.shadergraph:9606`; the suffix is the component count).
- Fragment: Base Color, Normal (World), Metallic, Smoothness, Ambient Occlusion, Alpha, Alpha Clip Threshold
  (`_AlphaCutoff`), Thickness (`_Thickness`).
- Transmission Mask is left at its default 1; Emission and Bent Normal are unconnected.

HDRP's Lit target carries Custom Interpolators through every pass (`HD/Editor/Material/ShaderGraph/HDShaderPasses.cs`,
`customInterpolators = CoreCustomInterpolators.Common` on each pass; `HDTarget.cs:1948`). The motion vector pass
re-evaluates the vertex graph for the previous frame with a dummy interpolator struct, so the current values are kept
(`HD/Runtime/RenderPipeline/ShaderPass/MotionVectorVertexShaderCommon.hlsl:186-198`). The graphs use six channels. Shader
Graph warns once the custom channels exceed 4 x 16 (the warning setting, `SG/Editor/ShaderGraphProjectSettings.cs:19-21`)
minus HDRP's padding of 4 x 8 (`HDTarget.padCustomInterpolatorLimit`) minus the vertex blocks' own channels
(`SG/Editor/Data/Graphs/GraphData.cs:3013-3047`). That leaves about 16 channels here, so six stay well clear of it.

Nodes:
- Vertex stage, wired into the vertex Custom Function: Position (Object), Position (Absolute World), Normal Vector
  (Object), Tangent Vector (Object), Vertex Color, Time (Time output), Object (Position, Scale), Camera (Position), and
  one Property node per wind vector.
- Triplanar graph, vertex stage: Position (Absolute World) and Normal Vector (World) also feed the two Custom
  Interpolator blocks. In the vertex stage they are the mesh's rest values, because they are built from the vertex
  attributes before the graph's outputs replace them (`Vertex.template.hlsl`, `ApplyMeshModification`).
- Fragment stage, wired into the fragment Custom Function: UV (UV0); Normal Vector (World), Tangent Vector (World) and
  Bitangent Vector (World), which are HDRP/Lit's `tangentToWorld` rows after the double-sided flip
  (`HD/Editor/Material/ShaderGraph/Templates/Pixel.template.hlsl:6-19`, `ShaderPass.template:277`); one Property node
  per surface property. The triplanar graph reads the rest values through two `CustomInterpolatorNode`s, serialized
  like `Fern.shadergraph:5013`.
- Neither graph uses the Position node's Predisplacement source.

### 3.3 Material properties (blackboard reference names)

Exposed, per material, in `UnityPerMaterial`:

| Reference | Type, default | Used for |
|---|---|---|
| `_BaseColorMap` | Texture2D, white, serialized with **`useTilingAndOffset: true`** | base colour (rgb) and leaf mask (a). Its scale and offset (`BaseColorMap.scaleTranslate` in the HLSL, which is `_BaseColorMap_ST`) are applied to every map's UV, as HDRP/Lit does. Without that flag the property is `[NoScaleOffset]`, `_BaseColorMap_ST` is not declared and `scaleTranslate` is (1, 1, 0, 0) (`SG/Editor/Data/Graphs/Texture2DShaderProperty.cs:50, 58, 93-95, 121`). No change in pixels today: every wind material has scale (1, 1) and offset (0, 0); only the static `forest_ground_06__ground` has an offset (0.66, 0.66) |
| `_BaseColor` | Color, white | `albedo = tex.rgb * _BaseColor.rgb`, `alpha = tex.a * _BaseColor.a` |
| `_NormalMap` | Texture2D, default **bump** | decoded and resolved as HDRP/Lit does on its UV0 path (section 3.6): `UnpackDerivativeNormalRGorAG(tex, _NormalScale)`, `SurfaceGradientFromTBN`, `SurfaceGradientResolveNormal` (Core `NormalSurfaceGradient.hlsl:22, 61, 97`). HDRP/Lit defines `SURFACE_GRADIENT` (`HD/Runtime/Material/Lit/LitData.hlsl:8`); it does not use `UnpackNormalMapRGorAG` there |
| `_NormalScale` | Float, 1 | |
| `_MaskMap` | Texture2D, white | R metallic, G AO, A smoothness, always sampled (no `_MASKMAP` keyword exists in the graph) |
| `_MetallicRemapMin`, `_MetallicRemapMax` | Float, 0 / 1 | `lerp(min, max, R)` |
| `_AORemapMin`, `_AORemapMax` | Float, 0 / 1 | `lerp(min, max, G)` |
| `_SmoothnessRemapMin`, `_SmoothnessRemapMax` | Float, 0 / 1 | `lerp(min, max, A)` |
| `_AlphaCutoff` | Float, 0.5 | Alpha Clip Threshold; `HDMaterial.SetAlphaCutoff` writes it |
| `_Thickness` | Float, 1 | Thickness |
| `_TexWorldScale` | Float, 1 (triplanar graph only) | tiles per metre, as HDRP/Lit |
| `_GamexSway` | Vector4, 0 | (trunk amplitude m, trunk period s, branch amplitude m, branch period s); periods at instance scale 1 |
| `_GamexFlutter` | Vector4, 0 | (flutter amplitude m, flutter frequency Hz, 0, 0) |
| `_GamexMotion` | Vector4, 0 | (period size exponent k, shelter 0..1, ripple 1 / wavelength in 1/m (0 = none), instance phase spread 0..1) |
| `_GamexPush` | Vector4, 0 | (branch push m, flatten share 0..1, push gain, 0) |
| `_GamexStem` | Vector4, 0 | (passable scale (0 = none), stem lean at full press in radians, 0, 0) |

HDRP adds and manages `_AlphaCutoffEnable`, `_DoubleSidedEnable`, `_DoubleSidedNormalMode`, `_MaterialID`,
`_DiffusionProfileHash`, `_DiffusionProfileAsset`, `_CullMode` and the rest; `ForestMaterials` sets them as today.

**A zero amplitude makes its term static, and a zero period never reaches a division.** Every term of section 3.5
runs under a uniform branch on its own amplitude and period (trunk: `Sway.x > 0 && Sway.y > 0`; branch: `Sway.z > 0 &&
Sway.w > 0`; flutter: `Flutter.x > 0 && Flutter.y > 0`). Every period is used as `rcp(max(period, 0.05))`. Without
both guards, ferns and grass, whose trunk amplitude and period are 0, would compute `TAU / 0 = inf`, `sin(inf) = NaN`,
and `R x 0 x NaN = NaN` even where R = 0. Their vertices would vanish (or, with fast math, be undefined). A material whose
wind vectors are all zero is therefore static, and `WindShader_ProfilesGiveFinitePositions` (8.6) proves positions stay
finite for every profile.

### 3.4 Global shader properties

Declared at file scope in `ForestWind.hlsl` (outside `UnityPerMaterial`), written only by `ForestWindGlobals`. **All
zero when unset, and all zero means no displacement at all**: the greybox, edit mode and every scene without a
controller render the rest pose.

| Global | Contents |
|---|---|
| `_GamexWind` (float4) | leanX, leanZ (unit, world XZ: the direction the trees lean toward, with the CPU's wander), calm (W between gusts, 0..1), peak (W at a crest, with the wander, 0..1.5) |
| `_GamexWindGust` (float4) | gustX, gustZ (unit, world XZ: the preset's bearing, never wandered), 1 / wavelength (1/m), front speed (m/s) |
| `_GamexWindParams` (float4) | time offset (s, added to the Time input; 0 in play; captures set it for edit-mode pose sequences, whose game camera has HDRP time 0), seed (0..1), torch shadow fade end (m; 0 = the fade end; knob 4's `torch15`), 0 |
| `_GamexWindFade` (float4) | fadeStart (m), fadeEnd (m), flutterEnd (m), moon-cascade mode (0 rest pose, 1 sway only, 2 full) |
| `_GamexGustTex` (Texture2D, R16F) and `_GamexGustRect` (float4) | knob 2 only (I2, section 7.4): `e` per 1 m cell around the focus; rect = origin X, origin Z, 1 / size, valid (0 or 1). Zero = not used: the shader evaluates `GamexGust` |
| `_GamexInteractionTex`, `_GamexInteractionTexPrev` (Texture2D) | the field this frame and last frame |
| `_GamexInteractionRect`, `_GamexInteractionRectPrev` (float4) | origin X, origin Z (world, the window's corner), 1 / window size (1/m), valid (0 or 1) |

Sampled with the inline linear-clamp sampler (`s_linear_clamp_sampler`), `SAMPLE_TEXTURE2D_LOD(..., 0)`.

### 3.5 The vertex function

```hlsl
// Shader Graph entry (File-mode Custom Function, float precision). Gathers what only the pipeline knows, then calls
// GamexWindDeform, which uses no pipeline matrix and no SHADERPASS test, so ForestWindProbe.compute can run it too.
void ForestWindVertex_float(
    float3 PositionOS, float3 NormalOS, float3 TangentOS, float4 VertexColor,
    float3 PositionAWS,            // Position node, Absolute World, vertex stage: the rest position
    float3 PivotAWS,               // Object node, Position (absolute)
    float3 ObjectScale,            // Object node, Scale (uniform; use .y)
    float3 CameraAWS,              // Camera node, Position (absolute; the main camera's in every shadow pass)
    float  Time,                   // Time node, Time (HDRP substitutes the previous time in the motion vector pass)
    float4 Sway, float4 Flutter, float4 Motion, float4 Push, float4 Stem,
    out float3 OutPositionOS, out float3 OutNormalOS, out float3 OutTangentOS, out float3 OutVelocityOS)
{
    bool moonPass = false, torchPass = false;
#if defined(SHADERPASS) && (SHADERPASS == SHADERPASS_SHADOWS)
    moonPass = UNITY_MATRIX_P._m33 > 0.5;      // orthographic: the directional light's cascades
    torchPass = !moonPass;                     // perspective: the torch's spot shadow, the only other shadow caster
#endif
    GamexWindDeform(PositionOS, NormalOS, TangentOS, VertexColor, PositionAWS, PivotAWS, ObjectScale.y, CameraAWS, Time,
                    Sway, Flutter, Motion, Push, Stem, (float3x3)GetWorldToObjectMatrix(), moonPass, torchPass,
                    OutPositionOS, OutNormalOS, OutTangentOS, OutVelocityOS);
}
```

Let `p0, n0, t0` be the inputs, `(R, G, B, A) = VertexColor`, `s` the scale, `w2o` the world-to-object 3x3, `TAU = 2 pi`.
Every sine takes its argument in **cycles** and wraps it first: `S(x) = sin(TAU * (frac(x + 0.5) - 0.5))`, so no argument
grows with play time and every `sin` is called on [-pi, pi). That is the domain where Metal's fast-math `sin` has its
documented accuracy (2^-13 absolute). Outside it the error is unspecified, and Unity compiles Metal shaders with fast
math. A nested phase goes inside the wrap, never added to a wrapped angle (see the gust function). Use only `w2o` (from `GetWorldToObjectMatrix()`, which is DOTS-instancing safe), never `unity_ObjectToWorld`.
Steps of `GamexWindDeform`, in order:

1. **Outputs start at the rest pose**; velocity 0.
2. **Global gate**: if `_GamexWind.z <= 0`, `_GamexWind.w <= 0` and `_GamexInteractionRect.w <= 0`, return. This makes
   the static regression bit-exact in position.
3. **Instance distance**: `d = distance(CameraAWS, PivotAWS)`; `end = _GamexWindFade.y`, or
   `min(end, _GamexWindParams.z)` when `torchPass` and `_GamexWindParams.z > 0` (knob 4's `torch15`; 0 at the start, so
   every pass uses the same distances); `start = 0.7 end` in that case, else `_GamexWindFade.x`. If `d >= end`, return
   (a uniform per-instance `[branch]`). `fade = 1 - smoothstep(start, end, d)`;
   `flutterFade = 1 - smoothstep(0.7 * _GamexWindFade.z, _GamexWindFade.z, d)`;
   `flutterOn = Flutter.x > 0 && Flutter.y > 0 && d < _GamexWindFade.z`;
   `pushOn = _GamexInteractionRect.w > 0 && (Push.x > 0 || Stem.x > 0)`.
4. **Moon cascades** (`moonPass` only): mode 0 returns; mode 1 sets `flutterOn = pushOn = false` (both are branches, so
   mode 1 really skips their work); mode 2 changes nothing. There is no other pass-dependent code than this and the
   torch-shadow distance of step 3, both only in shadow passes: the depth prepass and the G-buffer must compute
   identical positions (deferred alpha-tested foliage uses ZTest Equal in the G-buffer pass).
   Mark the final position `precise`. Whether `precise` survives Unity's Metal compile with fast math is unproven; I1
   checks it (section 9).
5. **Per-instance values**:
   - `t = Time + _GamexWindParams.x`;
   - `h = GamexHash01(PivotAWS.xz)` (PCG2D of `int2(floor(PivotAWS.xz * 100))`, divided by 2^32); `h2 = frac(7.31 h)`,
     `h3 = frac(3.17 h)`, `h4 = frac(5.3 h)`;
   - `spread = Motion.w`; `sizeScale = pow(max(s, 0.05), Motion.x)`;
   - `dirOS = normalize(mul(w2o, float3(_GamexWind.x, 0, _GamexWind.y)))` (the lean direction),
     `upOS = normalize(mul(w2o, float3(0, 1, 0)))`, `crossOS = cross(upOS, dirOS)`;
   - `gustDir = _GamexWindGust.xy` (world, fixed).
6. **Gust and effective wind**: `e = _GamexGustRect.w > 0 ? SampleGustTex(PivotAWS.xz) : GamexGust(PivotAWS.xz, t)`
   (below; the texture is knob 2's); `W = (_GamexWind.z + (_GamexWind.w - _GamexWind.z) * e) * fade * Motion.y`
   (calm, crest, distance fade, shelter).
7. **Trunk (R)**, `[branch] if (Sway.x > 0 && Sway.y > 0)`:
   - `fT = rcp(max(Sway.y * sizeScale * (0.85 + 0.30 h), 0.05))` (cycles per second);
   - `osc = 0.65 S(fT t + spread h) + 0.35 S(1.618 fT t + h2)`;
   - `trunk = R * Sway.x * W * (dirOS * (0.6 + 0.4 osc) + crossOS * 0.15 S(0.77 fT t + h3))`;
   - `L = length(p0)`; `P = L > 1e-3 ? normalize(p0 + trunk) * L : p0 + trunk` (the distance to the pivot is kept, so
     the tree bends and does not stretch);
   - tilt N and T (step 11) by `aT = 2 length(trunk) / max(p0.y, 0.5)` about `normalize(cross(upOS, trunk))`.
8. **Branch (G)**, `[branch] if (Sway.z > 0 && Sway.w > 0)`:
   - `fB = rcp(max(Sway.w * sizeScale * (0.9 + 0.2 h4), 0.05))`;
   - `ripple = frac(Motion.z * dot(PositionAWS.xz, gustDir))` (0 for firs; it uses the fixed gust direction, never the
     wandering lean, for the reason given in 2.7);
   - `bo = S(fB t + A + spread h - ripple)`;
   - `P += G * Sway.z * W * (upOS * 0.7 bo + dirOS * 0.5 (0.5 + 0.5 bo))`.
9. **Flutter (B)**, `[branch] if (flutterOn)`:
   `fl = S(Flutter.y t + frac(7.13 A + h) + dot(p0, float3(2.753, 1.767, 2.180)))`;
   `P += B * Flutter.x * W * flutterFade * fl * n0`. The conifer profiles have `Flutter.x = 0`, so firs never run it.
10. **Push**, `[branch] if (pushOn)`. Define `PushAt(tex, rect)`:
    - `uv = (PositionAWS.xz - rect.xy) * rect.z`; `f = inside(uv) ? SAMPLE_TEXTURE2D_LOD(tex, s_linear_clamp_sampler, uv, 0) : 0`
      (field channels: rg = lateral push, world XZ, direction times magnitude 0..1; b = band top, world Y; a = press 0..1);
    - **push weight**: `wp = G > 0 ? saturate(Push.z * pow(G, 2.0 / 3.0)) : 0`. For firs `G^(2/3) = clamp01((rho - r_t) / 3)`
      (section 4.3), so with gain 5, `wp = saturate((rho - r_t) / 0.6)`: 0.83 at 0.5 m from the trunk surface and 1
      from 0.6 m on, where the old weight G gave 0.064, 0.19 and 0.35 at 0.5, 1 and 1.5 m. For ferns
      `G^(2/3) = d_f / reach_f` and gain 2 gives 1 from half the reach; for grass it is 1 from 59 % of the blade height.
      No new vertex data;
    - `w = wp * (1 - smoothstep(f.b, f.b + 0.25, PositionAWS.y)) * fade` (only below the top of the body);
    - **direction and magnitude**: `dirW = length(f.rg) > 1e-4 ? f.rg / length(f.rg) : 0`; the magnitude is the press `f.a`.
      The direction channels hold `dir x m`, and `dir` points away from the body axis, so it flips across the axis.
      Bilinear filtering of `rg` cancels within a texel (0.2 m) of the axis. Used as the push, it would leave exactly
      the vertices inside the body unpushed. `a` is a non-negative scalar and does not cancel. Normalising `rg` keeps
      each vertex's own side, so the foliage parts along the axis line. Fronds part that way when something walks
      through them; a branch crossing the line splits there, inside the body, or edge-on to the first-person eye;
    - `lateral = mul(w2o, float3(dirW.x, 0, dirW.y)) * f.a * Push.x * w` (unnormalised: divides by the scale, so `Push.x`
      is world metres);
    - `drop = Push.y * f.a * w * min(p0.y, 1.0 / s)` (pressed fronds flatten; at most `Push.y` of their height, capped at
      1 m world);
    - **passable saplings**: if `Stem.x > 0` and `s <= Stem.x + 1e-3` (0.705 lies 3.7e-3 from every instance scale, so
      the margin cannot flip a fir; section 2.9):
      - sample the field again at `PivotAWS.xz` (`fp`); `m = fp.a`; when `m > 1e-3` and `length(fp.rg) > 1e-4`:
      - `gb = smoothstep(PivotAWS.y + 0.2, PivotAWS.y + 0.6, fp.b)` (the body stands taller than the stem's foot);
      - `theta = Stem.y * m * gb * fade` (`Stem.y` = 0.873 rad, 50 degrees at full press);
      - `k = normalize(cross(upOS, normalize(mul(w2o, float3(fp.r, 0, fp.g)))))` (lean away from the body axis);
      - `stem = Rotate(p0, k, theta) - p0` (Rodrigues, about the pivot). The sapling leans over from its base and keeps
        its length: a point at height h moves to `h sin theta` from the stem, 0.54 m at 0.7 m and 1.26 m at eye height
        at full press. This replaces the first version's shear of the lowest metre, which translated the crown sideways
        and stretched the stem;
      - `lateral += stem`, and keep `(k, theta)` to rotate N and T exactly;
    - return `lateral - float3(0, drop, 0)`.

    Then `pushNow = PushAt(_GamexInteractionTex, _GamexInteractionRect)`,
    `pushPrev = _GamexInteractionRectPrev.w > 0 ? PushAt(_GamexInteractionTexPrev, _GamexInteractionRectPrev) : pushNow`,
    `P += pushNow`, `OutVelocityOS = pushNow - pushPrev` (the stem lean included, so its motion vectors are exact). Tilt
    N and T by `min(length(lateral without the stem term) / max(p0.y, 0.15), 0.8)` about
    `normalize(cross(upOS, pushNow))`, and rotate them by the stem's `(k, theta)`.
11. **Tilt** (used by 7 and 10): `v = normalize(v + angle * cross(axis, v))` for N and for T.xyz; skip when the angle is
    below 1e-4 or the axis is degenerate.

**Gust function** (shared by the vertex function, `FoliageInteraction.compute`'s `Gust` kernel and the probe kernel; the
CPU mirror `ForestWindGlobals.SampleGust` implements the same formula):

```
d = _GamexWindGust.xy; c = float2(-d.y, d.x); invL = _GamexWindGust.z; v = _GamexWindGust.w; seed = _GamexWindParams.y
u = (dot(xz, d) - v * t) * invL          // in wavelengths; fronts travel downwind at v m/s, in a frame that never rotates
w = dot(xz, c) * invL * 0.5              // across the wind, fronts twice as wide as long
s = 0.70 S(u + (1.3 / TAU) S(w + seed))
  + 0.20 S(1.73 u + 2.1 / TAU + (0.7 / TAU) S(1.31 w + 2 seed))
  + 0.10 S(2.71 u - 0.6 w + seed + 4.2 / TAU)
g = 0.5 + 0.5 s
GamexGust = smoothstep(0.62, 0.92, g)    // e: 0 in a lull, 1 at a crest
```

This is the tuned formula of `gust_tune.py` written in cycles: `0.70 sin(TAU u + 1.3 sin(TAU (w + seed))) + ...`, with
every sine's whole argument, nested phase included, inside `S`. Write the constants as the expressions shown
(`1.3 / TAU`, not a rounded decimal) in both the HLSL and the CPU mirror. With five-digit decimals the two forms differ
by up to 7e-6 in `g` (`B/probes/W1/rev/recheck_wrap.py`).

The weights (0.70, 0.20, 0.10, against 0.55, 0.30, 0.15 before) and the smoothstep give lulls and well separated
gusts; section 2.7 has the measured statistics. The CPU mirror and the GPU agree by measurement, not by assertion:
`WindShader_GustMatchesCpuOnGpu` (8.6) reads `GamexGust` back from the probe kernel. The test's tolerance is 2e-3 in
`e` for `t <= 600 s`, and the error at 3 h is reported, not asserted. Where 2e-3 comes from:
- With every `sin` off by its documented 2^-13, `e` moves by at most 5e-4 (`recheck_wrap.py`, 100,000 random samples).
- Float rounding of `u` adds under 1e-4 for `t <= 600 s` (`u` stays below about 25 cycles, spacing 2e-6).
- That leaves a margin of about 3x. A larger difference at small `t` is a formula mismatch, so fix it, never loosen
  the tolerance (section 9).

Precision note: `Time` is float; after several hours its spacing reaches a few milliseconds and the 3.5 to 4 Hz fern and
grass flutter steps slightly (accepted, section 9). The wrapped sines keep the slower terms smooth.

### 3.6 The fragment functions

- `ForestSurfaceUV_float(UnityTexture2D BaseColorMap, UnityTexture2D NormalMap, UnityTexture2D MaskMap, float2 UV,
  float3 NormalWS, float3 TangentWS, float3 BitangentWS, float4 BaseColor, float NormalScale, float MetallicRemapMin,
  float MetallicRemapMax, float AORemapMin, float AORemapMax, float SmoothnessRemapMin, float SmoothnessRemapMax,
  out float3 Albedo, out float Alpha, out float3 NormalOutWS, out float Metallic, out float AO, out float Smoothness)`:
  - Sampling as HDRP/Lit does it (`LitDataIndividualLayer.hlsl`): each texture with its own sampler and HDRP's global
    mip bias, and `uv = UV * BaseColorMap.scaleTranslate.xy + BaseColorMap.scaleTranslate.zw` for every map (3.3), with
    the remaps above.
  - Normal:
    `deriv = UnpackDerivativeNormalRGorAG(SAMPLE(NormalMap, uv), NormalScale)`;
    `grad = SurfaceGradientFromTBN(deriv, TangentWS, BitangentWS)`;
    `NormalOutWS = SurfaceGradientResolveNormal(NormalWS, grad)`.
  - With the fragment inputs of 3.2 (`tangentToWorld[0]`, `[1]` and the normalised, double-sided-flipped `[2]`) this is
    HDRP/Lit's UV0 path term for term:
    - `LitData.hlsl:62-68` (`vertexTangentWS0 = tangentToWorld[0]`, `vertexBitangentWS0 = tangentToWorld[1]`);
    - the SURFACE_GRADIENT branch of `CORE/ShaderLibrary/Sampling/SampleUVMappingNormalInternal.hlsl`;
    - `GetNormalWS_SG` in `HD/Runtime/Material/MaterialUtilities.hlsl`, where in Mirror mode `doubleSidedConstants.x`
      is 1, so the gradient is not flipped, and the vertex normal was already flipped by `ApplyDoubleSidedFlipOrMirror`
      in both.
  - What remains different: Shader Graph passes a World normal through `GetNormalWS_SrcWS`
    (`DecalCommon.template.hlsl:38`). On back faces this reflects a resolved normal that points away from the vertex
    normal back into its hemisphere, a step HDRP/Lit does not have. Resolved normals stay in that hemisphere except
    under extreme gradients, so the step is expected to be inactive. The static regression judges it.
  - Why not Shader Graph's Tangent-space Normal block: it is resolved with `TransformTangentToWorld` (the
    non-surface-gradient `GetNormalWS`, `MaterialUtilities.hlsl:60-73`). That equals Lit's result only when the tangent
    frame is orthonormal, and the mikkt frame is not normalised.
- `ForestSurfaceTriplanar_float(..., float3 RestPositionAWS, float3 RestNormalWS, float3 NormalWS, float TexWorldScale,
  ..., out float3 NormalOutWS, ...)`: HDRP/Lit's triplanar path, ported. Output is in world space.
  - Projection: `GetTriplanarCoordinate(RestPositionAWS * TexWorldScale, ...)`. HDRP/Lit projects the absolute world
    position times `_TexWorldScale` (`LitDataIndividualLayer.hlsl`, `posForTriplanar`).
  - Weights: `ComputeTriplanarWeights(RestNormalWS)` (`CORE/ShaderLibrary/CommonMaterial.hlsl:324-346`; it takes
    `abs(normal)`, so the double-sided flip does not matter).
  - Sampling and normal blend: the SURFACE_GRADIENT triplanar branch of `SampleUVMappingNormalInternal.hlsl`, with
    per-plane derivatives and `SurfaceGradientFromVolumeGradient(NormalWS, volumeGrad)`, then
    `SurfaceGradientResolveNormal(NormalWS, ...)`.
  - Projection and weights come from the rest position and rest normal (the Custom Interpolators), so the bark does not
    swim and its blend does not shift when the tree sways. The gradient is resolved against the current normal, so the
    lighting follows the swayed surface. A tall fir's trunk tilts about 0.016 rad at a night crest's mean lean
    (`2 x 0.45 x 0.55 x 0.6 m / 18.84 m`), and at most about 0.033 rad (1.9 degrees) at the swing's extreme with the peak
    wander (`2 x 0.45 x 0.69 m / 18.84 m`). So the rest-space gradient against the current normal is a negligible mismatch.
- Acceptance for both: the static regression (8.1). Any systematic difference is fixed in the HLSL, never hidden.

### 3.7 Validation layers (I1 implements them in `ForestWindShaderGraphs.CheckShaders`)

1. Synchronous import (`ForceSynchronousImport | ForceUpdate`); any importer error (`Shader Graph at`, `Shader error in`)
   fails, caught through `Application.logMessageReceived` around the import and, independently,
   `ShaderUtil.GetShaderMessages(shader)` (probe-compiled, `probes/W1/VerifyW1Editor2.cs`).
2. The shader is not an error shader: a material on it has tag `ShaderGraphTargetId == "HDLitSubTarget"` and
   `HDMaterial.ValidateMaterial` returns true; `ShaderUtil.ShaderHasError` false; `Shader.Find(name)` finds it (so the
   importer did not hide it).
3. **Explicit Metal variant compile, with keyword sets built per pass**, the decisive check. For each of the passes
   `GBuffer`, `DepthOnly`, `ShadowCaster` and `MotionVectors`:
   - read the keywords the pass declares per stage with `ShaderUtil.GetPassKeywords(shader, passId, ShaderType.Vertex)`
     and `ShaderType.Fragment` (probe-compiled);
   - build from them the foliage set (whichever of `_ALPHATEST_ON`, `_DOUBLESIDED_ON`, `_MATERIAL_FEATURE_TRANSMISSION`
     the pass declares) and the opaque set {}, each with and without `DOTS_INSTANCING_ON` where declared. For
     `MotionVectors`, also build each with `WRITE_NORMAL_BUFFER` and with `WRITE_DECAL_BUFFER_AND_RENDERING_LAYER` where
     declared;
   - never pass a pass a keyword it does not declare;
   - compile each set with `CompileVariant(stage, keywords, ShaderCompilerPlatform.Metal, BuildTarget.StandaloneOSX)`.
     Every `VariantCompileInfo.Success` must be true.
   
   Save each variant's `ShaderData` (the generated MSL) to `B/out/W1/msl/<graph>/<pass>_<stage>_<set>.txt`. It serves
   two uses: the cost metric of knob 2 (section 7.4), and the `precise` check of section 9. The Metal output has no
   instruction count, so the metric is the count of transcendental calls (`sin`, `cos`, `atan`, `pow`, `exp2`, `log2`)
   and of `sample` calls in the vertex function.
4. Captures run with `ShaderUtil.allowAsyncCompilation = false` (ForestCapture already does).

---

## 4. Wind data, exactly

### 4.1 Vertex layout of every wind mesh (single stream, 44 B)

Position Float32 x 3 (12) | Normal Float32 x 3 (12) | Tangent **Float16 x 4** (8) | Color **UNorm8 x 4** (4) |
TexCoord0 Float32 x 2 (8). Static props and collision meshes keep their layout. Written by `ForestWindData.Compact`
after `Mesh.Optimize()`, from the optimised mesh's own arrays (same vertex order), keeping sub-meshes, index format and
bounds. A test checks the round trip (positions and UV0 exact, normals exact, tangents within 1e-3, colours exact).

### 4.2 Per-tree constants (measured once per prefab on the source scan, prefab space, before any level is built)

- `H` = top of the scan (bounds max y). `H_class` = 8.74 m for young firs, 18.84 m for tall firs (constants in
  `ForestWindData`; the bake throws if a measured `H` exceeds its class height by more than 1 cm).
- Trunk centreline `c(y)` and radius `r_t(y)`: cross-sections of the **largest connected wood piece** (the trunk:
  1,100 triangles for young firs, 46,268 and 78,776 for tall c and b) every 0.25 m: centroid of the section's segments,
  radius = section perimeter / 2 pi; linear interpolation; above the trunk's top `c = c(top)`, `r_t = 0`. Connectivity
  by `ForestFoliageThinner.FindPieces` on the merged wood. Fallback if a section is empty: the pivot axis and the
  previous radius.
- `rho(p)` = horizontal distance from `c(p.y)`; `theta(p)` = azimuth around `c(p.y)`.

### 4.3 Formulas

`clamp01` clamps to [0, 1]. Quantised to 8 bits (1/255; phase 1.4 degrees, consumed per vertex, never interpolated).

**Trees, every level and sub-mesh:**

- **R** = `clamp01(S(y / H) * H / H_class)`, `S(h) = h^2 (6 - 4h + h^2) / 3` (static deflection of a uniformly loaded
  cantilever, 1 at the top), 0 for `y <= 0`.
- **G** = `clamp01((rho - r_t(y)) / 3.0)^1.5`; 0 on the trunk (`rho <= r_t`) and forced to 0 on the root-skirt sub-mesh.
- **B** = `clamp01(d_a / 0.30)` on foliage, `d_a` = distance to the anchor of the vertex's foliage piece, the anchor being
  the piece's vertex nearest `c(y)`; 0 on wood, skirt and crown core. Pieces: the scan level reuses the baker's own
  `pieces` (FindPieces on the twigs); every thinned level runs FindPieces on its own foliage sub-mesh (each needle, stick
  and card is a separate quad; a card's anchor is its inner, stem end). Baked although the conifer profiles do not use
  it (flutter 0, section 2.6); it is not LOD-invariant (section 2.8).
- **A** = wrap-safe value noise on the cylinder around the centreline: lattice `i = floor(y / dy)` (`dy` = 0.35 m young,
  0.60 m tall, about one whorl), `j = floor(8 theta / 2 pi) mod 8`; `V(i, j) = Hash01(i, j, prefab seed)` with the
  thinner's splitmix hash; interpolate the unit phasors `exp(2 pi i V)` with smoothstep weights in both directions;
  `A = atan2 / 2 pi mod 1`. Per vertex on wood; per foliage piece at its anchor (a needle moves rigidly with its branch).

**Fern patches and single clumps** (clump space, per frond piece, per slot of the patch layout):
R = 0; G = `clamp01(d_f / reach_f)^1.5` with `d_f` the distance from the frond's base (its vertex nearest the clump
centre) and `reach_f` the frond's largest `d_f`; B = `clamp01(d_f / reach_f)`;
A = `frac(0.25 Hash01(slot, patch seed) + 0.12 (2 Hash01(slot, frond rank by base azimuth, patch seed) - 1))`: one
phase per clump (spread 0.25 cycle) and plus or minus 0.12 cycle per frond, so a clump sways together and neighbouring
clumps stay close enough in phase for the ripple of section 3.5 step 8 to read. The far level reuses the same clumps in
the same slots, so every frond keeps its phase across the 16 m switch. Single clumps use slot 0; their instance hash
(times the profile's spread 0.25) separates them.

**Grass patches** (per blade piece): R = 0; G = `clamp01((y - y_base) / h_blade)^2`; B = `sqrt(G)`;
A = `frac(0.25 Hash01(slot, patch seed) + 0.12 (2 Hash01(slot, blade base quantised to 1 cm, patch seed) - 1))`.

### 4.4 Where it goes in the code, and determinism

- `ForestTreeBaker.BakeIfNeeded`: measure the field once from `wood` (after step 2); compute colours for each thinned
  level from `Assemble(foliage)` and for the scan level from its merged parts, knowing which sub-mesh is wood, skirt
  (its material is the ground variant), foliage or crown core; pass them to `ToMesh`; then `ForestWindData.Compact`.
  The `LOD0_Shadow` renderer draws the LOD2 mesh asset, which then carries the data.
- `ForestMeshTools.ToMesh(MeshData, string, bool, Color32[] colors)`: sets the colours (aligned with `data.positions`)
  **before** `Optimize()`. Setting them after `Optimize` would pair them with the wrong vertices.
- `ForestPrefabs.BuildPatch` / `Combine`: per slot, instantiate a copy of the clump mesh, set its slot colours (computed
  from that mesh's own vertex order), then `CombineMeshes` (which carries colours), `Optimize`, `Compact`.
  `BuildScanProp` colours single fern clumps (slot 0) and leaves rocks, stumps and branches alone.
- Determinism: seeded hashes only, no unordered iteration (no `Dictionary`/`HashSet` enumeration in the bake), so two
  forced kit builds give byte-identical mesh assets (the existing verifier compares all 104).

---

## 5. Work package I1: shader and data

### 5.1 Scope

Shaders, graphs, wind materials and their switch, wind data in every wind mesh, the compact layout, kit rebuild,
static regression, edit-mode pose sequences proving the motion character, tests, a player cost check at zero wind.

### 5.2 Files and public surface

New files:

| File | Public surface |
|---|---|
| `Assets/Game/Shaders/ForestWind.hlsl`, `ForestSurface.hlsl`, `ForestWind.shadergraph`, `ForestWindTriplanar.shadergraph` (+ `.meta` with fixed GUIDs) | Section 3 |
| `F/ForestWindShaderGraphs.cs` | see below |
| `F/ForestWindProfiles.cs` | see below |
| `F/ForestWindData.cs` | see below |
| `Assets/Game/Scripts/World/Foliage/ForestWindGlobals.cs` (Gamex.Runtime, namespace `Gamex`) | see below |
| `Assets/Game/Tests/ForestEditMode/ForestWindTests.cs` | section 8.6 |
| `Assets/Game/Tests/ForestEditMode/ForestWindProbe.compute` (+ `.meta`) and `ForestWindProbeSupport.cs` (helpers: `RunGust(xzt[])`, `RunDeform(points, colours, perInstance, globals)`, shared with the PlayMode tests by a copy of the same file in `ForestPlayMode/`, as the assemblies cannot reference each other) | sections 3.1, 8.6 |

```csharp
namespace Gamex.EditorTools
{
    /// Writes the two wind graphs from a fixed template (object ids derived from names, so the text is deterministic),
    /// writes their .meta files before the first import, and checks the shaders (section 3.7).
    public static class ForestWindShaderGraphs
    {
        public const string Folder = "Assets/Game/Shaders";
        public const string GraphPath = Folder + "/ForestWind.shadergraph";
        public const string TriplanarGraphPath = Folder + "/ForestWindTriplanar.shadergraph";
        public const string VertexIncludePath = Folder + "/ForestWind.hlsl";
        public const string SurfaceIncludePath = Folder + "/ForestSurface.hlsl";
        public const string ShaderName = "Gamex/ForestWind";
        public const string TriplanarShaderName = "Gamex/ForestWindTriplanar";

        public static string GraphText(bool triplanar);        // pure: the exact file content
        public static bool EnsureGraphs();                     // writes graph and meta files only where the content differs, imports synchronously; true if anything was written; throws on import errors
        [MenuItem("Gamex/Forest/Wind/Write Shader Graphs")] public static void WriteGraphsFromMenu();
        public static Shader Load(bool triplanar);             // null when not imported
        public static bool IsWindShader(Shader shader);
        public static List<string> CheckShaders();             // problems of section 3.7 layers 1 to 3; empty = good
        [MenuItem("Gamex/Forest/Wind/Check Shaders")] public static void CheckShadersFromMenu();   // logs the result; throws in batch mode on any problem
    }

    /// The per-model wind values of section 2.6. A material moves if its manifest model has a profile.
    public static class ForestWindProfiles
    {
        public sealed class Profile
        {
            public string modelId;
            public float trunkAmplitude, trunkPeriod, branchAmplitude, branchPeriod;     // periods at instance scale 1
            public float flutterAmplitude, flutterFrequency;                              // 0 for both conifers
            public float periodExponent, shelter, rippleWavelength, phaseSpread;          // rippleWavelength 0 = none
            public float pushAmount, flattenShare, pushGain;
            public bool passableStems; public float stemLean;                             // young firs only; radians at full press
            public Vector4 Sway { get; }      // _GamexSway
            public Vector4 Flutter { get; }   // _GamexFlutter
            public Vector4 Motion { get; }    // _GamexMotion (z = 1 / rippleWavelength, or 0)
            public Vector4 Push { get; }      // _GamexPush
            public Vector4 Stem { get; }      // _GamexStem (x = ForestLayout.PassableYoungFirMaxScale when passableStems, else 0)
        }
        public static readonly string[] Models;                // fir_sapling_medium, fir_tree_01, fern_02, grass_medium_01
        public static Profile ForModel(string modelId);        // null for a static model
        public static Profile ForMaterial(string materialName); // follows sameAs; null for a static material
    }

    public static class ForestWindData
    {
        public const string Version = "wind-1";               // part of ForestKitSpec.Hash
        public const float YoungClassHeight = 8.74f, TallClassHeight = 18.84f;
        public const float BranchReach = 3.0f, FlutterReach = 0.30f, SectionStep = 0.25f;
        public const float YoungPhaseCell = 0.35f, TallPhaseCell = 0.60f; public const int PhaseSectors = 8;
        public enum Part { Wood, Skirt, Foliage, CrownCore }
        public sealed class TreeField
        {
            public float height, classHeight, phaseCell; public int seed;
            public Vector2 CentreAt(float y); public float RadiusAt(float y);
        }
        public static TreeField MeasureTree(string prefabId, ForestMeshTools.MeshData wood);
        public static Color32[] TreeColours(TreeField field, ForestMeshTools.MeshData level, Part[] partOfSubMesh);
        public static Color32[] TreeColours(TreeField field, ForestMeshTools.MeshData level, Part[] partOfSubMesh, ForestFoliageThinner.Pieces scanPieces);   // the scan level
        public static Vector4 TreeWeights(TreeField field, Vector3 position, Part part, float anchorDistance, Vector3 anchor);   // R, G, B, A before quantising; for tests
        public static Color32[] ClumpColours(Vector3[] positions, int[] triangles, int slot, bool fern, int seed);
        public static readonly VertexAttributeDescriptor[] CompactLayout;
        public static Mesh Compact(Mesh mesh);                 // in place, after Optimize; returns the same mesh
    }
}

namespace Gamex   // Gamex.Runtime: UnityEngine APIs only
{
    [Serializable]
    public struct ForestWindState
    {
        public float gustBearing, leanBearing;                                            // section 2.7: gust frame fixed, lean wandered
        public float calm, peak, gustWavelength, gustSpeed, gustSeed;                     // peak includes the wander
        public float fadeStart, fadeEnd, flutterEnd; public int moonCascades;             // section 2.8, 3.4
        public float torchShadowEnd;                                                      // 0 = fadeEnd (knob 4)
        public float timeOffset;                                                          // captures only
    }

    /// The only writer of the wind and interaction globals of section 3.4.
    public static class ForestWindGlobals
    {
        public static readonly int Wind, Gust, Params, Fade, GustTex, GustRect, InteractionTex, InteractionTexPrev, InteractionRect, InteractionRectPrev;
        public const float GustLow = 0.62f, GustHigh = 0.92f;  // the smoothstep of GamexGust
        public static void Write(in ForestWindState state);
        public static void WriteGustTexture(Texture gust, Vector4 rect);   // knob 2 (I2); rect.w = 0 clears it
        public static void WriteInteraction(Texture current, Vector4 rect, Texture previous, Vector4 previousRect);
        public static void ClearInteraction();                 // both rects to zero
        public static void Clear();                            // every global of 3.4 to zero
        public static Vector2 Direction(float bearing);        // unit XZ: (sin bearing, cos bearing); 0 = +Z, 90 = +X
        public static float SampleGust(in ForestWindState state, Vector2 xz, float time);       // e, the shader's GamexGust, on the CPU
        public static float SampleStrength(in ForestWindState state, Vector2 xz, float time);   // calm + (peak - calm) e, before fade and shelter
        public static float GustCentredOffset(in ForestWindState state, Vector2 xz, float seconds); // time offset that puts a crest (largest e within one gust period) over xz, seconds into a window; used by ForestCapture (I1) and the PlayMode helper (I2)
    }
}
```

Changed files (back up first):

| File | Change |
|---|---|
| `F/ForestMaterials.cs` | `ToolVersion` "materials-3". A wind recipe family: every material whose model has a profile is built on `Gamex/ForestWind` (or `Gamex/ForestWindTriplanar` for `__triplanar`) with the same property names and order as today, then the profile vectors (`_GamexSway`, `_GamexFlutter`, `_GamexMotion`, `_GamexPush`, `_GamexStem`), then `ValidateMaterial`, save, diffusion profile, **then** `SetShaderPassEnabled("MotionVectors", motionVectors)` after the last validation. The first validation tags every graph material `MotionVector = User` and turns its pass off; a pass turned on afterwards stays on (2.3). Each wind material also gets an `__mv` twin (path `<name>` + an optional `__tint1`, `__triplanar` or `__crowncore` + `__mv.mat`), identical except the pass is on. In the HDRP/Lit mode the twins keep the pass off (`motionVectors` false): HDRP/Lit would turn it off at its next validation anyway (`BaseUnlitAPI.cs`, non-graph path: the pass follows `addPrecomputedVelocity`). On a reset, clear keywords before validating (a material moving from HDRP/Lit must not keep `_NORMALMAP`/`_MASKMAP`). Crown core on the graph: `_MetallicRemapMax 0`, `_SmoothnessRemapMin = _SmoothnessRemapMax = 0.05` (no mask map: the graph reads white). Stamps add the shader mode, the graph files' MD5, the profile values and the variant. The `__ground` and the static materials are untouched. New members below |
| `F/ForestMeshTools.cs` | `public static Mesh ToMesh(MeshData data, string name, bool recalculateTangents, Color32[] colors)`; the old overload calls it with `null` |
| `F/ForestTreeBaker.cs` | Measure the field; colours on every level and the scan; `Compact`; `__mv` materials on levels below `ForestKitSpec.MotionVectorLevels` |
| `F/ForestPrefabs.cs` | `BuildPatch`/`Combine` per-slot colours and `Compact`; `BuildScanProp` colours fern clumps; `__mv` materials per level; `ApplyTint` parses `<name>[__tint<k>][__mv]` and keeps the `__mv` choice |
| `F/ForestKitSpec.cs` | `ToolVersion` "kit-11"; `public static int MotionVectorLevels(string prefabId)` (young firs 1, fern patches 1, fern clumps 1, grass 1, tall firs 0, everything else 0); `Hash()` adds `ForestWindData.Version`, its constants and the table |
| `F/ForestCapture.cs` | `-captureSet wind` (section 8.2): edit-mode pose sequences through `ForestWindGlobals`, cleared in `finally` |
| `F/ForestLayout.cs` | `public const float PassableYoungFirMaxScale = 0.705f;` (used by the profile now and by I2's rule; section 2.9) |
| `F/ForestLightingPresets.cs` | `Preset` gains `windBearing, windCalm, windGustPeak, windGustWavelength, windGustSpeed`, with the values of 2.7, plus `public ForestWindState WindState()`: the preset's wind with seed 0.37 and the shipped distances of 7.2 (fade 21 to 30 m, flutter 10 m, moon mode 0), without the wander, which the run-time controller adds. In I1 because the edit-mode pose sequences (8.2) and `WindGlobals_GustHasLullsAndTravellingFronts` (8.6) use the Night values; I2's controller reads the same fields |
| `F/ForestAudit.cs` | `ManifestEntryOf` (`ForestAudit.cs:486-500`) strips only a trailing `__tint1` or `__triplanar`, so `X__mv`, `X__tint1__mv` and `X__triplanar__mv` would resolve to null. The audit would then silently skip its manifest texture-slot checks (base colour, normal and mask present) for exactly the materials on the nearest levels. Strip a trailing `__mv` first, then the existing suffixes. `__crowncore` stays unresolved as today: it has no mask map by design, so resolving it to the twigs' entry would report a false missing mask. Make `ManifestEntryOf` `public static` for `WindMaterials_ResolveToTheManifest` (8.6). The keyword half of `CheckTextureSlot` already runs only for `HDRP/Lit` (`:438`), so wind materials pass it |
| `Assets/Game/Tests/ForestEditMode/ForestKitTests.cs` | Adapt `FoliageMaterial_MatchesHdrpLeafSample` (section 8.6) |

New members of `ForestMaterials`:

```csharp
public const bool WindShaderDefault = true;                          // THE switch: false builds every material on HDRP/Lit again
public static bool? WindShaderOverride;                              // A/B only; null = WindShaderDefault, or -forestWindShader on|off
public static bool WindShaderActive { get; }
public static Material Get(string materialName, int tint, bool motionVectors);
public static Material GetTriplanar(string materialName, bool motionVectors);
public static bool IsWindMaterial(string materialName);              // manifest material of a profiled model (sameAs followed)
[MenuItem("Gamex/Forest/Wind/Materials: Wind Shader")] public static void UseWindShaderFromMenu();     // BuildAll(force) with the wind shader
[MenuItem("Gamex/Forest/Wind/Materials: HDRP Lit (A-B)")] public static void UseLitFromMenu();         // BuildAll(force) on HDRP/Lit, same assets and GUIDs
```

The switch back to HDRP/Lit rebuilds the same material assets (same GUIDs, prefabs and scene untouched; `__mv` twins
are then plain HDRP/Lit copies). The vertex colours stay in the meshes and are ignored by HDRP/Lit. Because the stamp
records the mode, the next ordinary build returns to the constant; a permanent fallback is changing the constant.
`ForestMaterials.BuildAll` calls `ForestWindShaderGraphs.EnsureGraphs()` first when the wind shader is active.

### 5.3 Steps

1. **Baselines (before any edit).** Write the scratch tools of 6.2 (all but `i2-final.sh`; `tstd` and `mp4` can wait
   until step 8). Confirm no editor is open and `pre-W1.md5` holds. Run the full edit-mode capture of
   the unchanged project twice, into `captures/w1-pre` and `captures/w1-pre2`, and the kit capture twice, into
   `captures/w1-pre-kit` and `captures/w1-pre-kit2`. Per view, the run-to-run noise reference is the larger of the two runs' RMSE to `n1-final`
   (expected 0.0017 to 0.0030), and the signed lit-mask bias reference is the larger of their absolute biases (8.1).
   Write the material-to-prefab map `out/W1/material-users.txt` and confirm no static prop uses a material that will
   move.
2. **Shaders.** Write the HLSL includes, `ForestWindGlobals`, the generator. Compile the C# (`compile.sh`,
   `compile-runtime.sh`). In Unity: `WriteGraphsFromMenu`, then `CheckShadersFromMenu`; iterate until empty. Model the
   generator's JSON objects on graphs Unity itself wrote, never with their ids or GUIDs:
   - `SG/Samples~/ProductionReady/Lit/HDRPLit.shadergraph` for the HDRP target and properties. Do **not** copy its
     `HDTarget`'s `m_CustomEditorGUI` `"Rendering.HighDefinition.LitGUI"` (`HDRPLit.shadergraph:27326`): leave it
     `""` as `Fern.shadergraph:10803` does.
   - `SG/Samples~/ProductionReady/Environment/Details/Ferns/Fern.shadergraph` for the vertex blocks, the `HDTarget`
     plus `HDLitSubTarget` objects and the Custom Interpolator (block `:9606`, node `:5013`). Fern has **no** Custom
     Function node.
   - `SG/Samples~/Common/Subgraphs/PseudoSubsurface.shadersubgraph` for a File-mode `CustomFunctionNode` (`:342`):
     `m_SourceType 0`, `m_FunctionName`, and `m_FunctionSource` set to the include's `.meta` GUID (`:386-388`; there
     `42c69bce689ca1845a44cd416f3614ef`, the GUID of `PseudoSubsurface.hlsl.meta`).
   
   Rules of the format: UTF-8, LF, JSON objects separated by exactly one blank line, no blank line inside an object.
   Do not open and re-save the graphs in the Shader Graph window.
3. **Materials.** Profiles and the `ForestMaterials` changes. Build materials in both modes once, check them (tests of
   8.6), leave them in the wind mode.
4. **Wind data.** `ForestWindData`, `ToMesh` overload, baker and prefab changes, kit spec. Compile; then unit tests on
   synthetic data first (`WindData_FormulasOnSyntheticTree`).
5. **Rebuild.** `ForestBuild.BuildAll -forestForce`: exit 0, audit passed, placements byte-identical to N1's
   (`out/N1/final/placements.json`). Note the kit time against the 40.9 to 43.6 s of earlier runs. Then a second forced
   kit build: all mesh, prefab and material assets byte-identical (md5 lists in `out/W1/`).
6. **Drawer check.** With D1's temporary measuring script (`tools/d1/ForestD1MeasureTemp.cs`, copied in for the run and
   removed after): the drawer excludes 0 renderers for `MissingDOTSInstancing`.
7. **Static regression** (section 8.1): `captures/w1-static-lit` (meshes new, materials on HDRP/Lit through
   `-forestWindShader off`), then back to the wind shader and `captures/w1-static`; kit capture `captures/w1-kit`.
8. **Motion proof in edit mode** (section 8.2, edit-mode part): pose sequences of at least 20 s with `-captureSet wind`,
   std, range and phase maps over the whole window, MP4s, contact sheets, LOD consistency sheets at a strong pose, torch
   shadow match, the speckle count of 8.1, and the MSL `precise` check of section 9. Tune the profile values if needed.
9. **Tests and suites**: new and adapted EditMode tests, including the GPU probe tests
   (`WindShader_GustMatchesCpuOnGpu`, `WindShader_ProfilesGiveFinitePositions`); forest EditMode, forest PlayMode and
   greybox (7 of 7) all pass. `Budget_Counts` Mesh Memory below 400 MiB (expected about 358). The `__mv` materials
   already draw the motion vector pass in play mode, so the vertex counter rises (about +2 M at crouch_hide, to about
   33.6 M: above the 33 M warning, below the 36 M budget); record it.
10. **Player cost at zero wind.** Build `Builds/ForestW1I1` with `tools/w1/build-player.sh`, then three runs interleaved
    with N1's player (`Builds/ForestN1`) on the eight worst views, torch on. Report the fixed cost of the new shaders and
    the `__mv` motion vector pass per view. **Stop condition**: if the median `W1I1 - N1` at pano_90 exceeds 0.3 ms,
    stop and report to the coordinator before I2 starts. In the model the motion vector pass adds only about 0.04 ms
    there, so the excess would be the shaders' own fixed cost. With the 0.5 ms cap of 7.1 for the wind itself, that
    would leave the dinosaurs too little. At crouch_hide the `__mv` pass of the scan level dominates (0.47 to 0.83 ms in
    the model): report it; I2's ghosting check decides it. Confirm no pink, no cyan, drawer on.
11. Notes in `issues/W1-I1.md`, dated section in `STATE.md`, diffs, backups; `pre-W1.md5` check; no temporary file left.

### 5.4 I1 done criteria

- Both graphs import; `CheckShaders()` returns no problem; every wind material validates, with the intended material
  type, keywords, diffusion profile hash and motion vector pass state.
- Static regression within noise (8.1) for `w1-static` and `w1-static-lit`: RMSE, signed lit-mask bias, 0 error
  pixels; every difference image looked at. Wind-on pose frames show no more speckles than wind off (8.1).
- Kit build deterministic; drawer exclusions 0; Mesh Memory under 400 MiB.
- Edit-mode pose sequences (20 s or more) show each class moving with the character of 2.6, with lulls and gusts,
  neighbouring LOD levels bending alike, and the torch's shadows following the branches. MP4s per class made and
  watched.
- The GPU probe agrees with the CPU gust mirror, and every profile gives finite positions.
- Zero-wind player cost at pano_90 at most 0.3 ms over N1 (step 10), or the coordinator has decided after the report.
- Forest EditMode (all, including the new tests), forest PlayMode (13 of 13) and greybox (7 of 7) pass; protected files
  unchanged.

---

## 6. Work package I2: runtime and performance

### 6.1 Scope

The wind controller, the interaction field and its compute shader, pushers (player now), the rustle hook and density
map, a raptor-sized proxy for tests, scene integration, the thin-stem rule, the measurement harness mode, play-mode motion
captures and checks, the player build, measurement and knob tuning.

### 6.2 Files and public surface

New runtime files, `Assets/Game/Scripts/World/Foliage/` (Gamex.Runtime, namespace `Gamex`, UnityEngine APIs only):

```csharp
/// One per lighting preset object (Lighting/Preset_<name>); only the active preset's is enabled. Not [ExecuteAlways]:
/// in edit mode every global stays zero. Writes the globals in LateUpdate; clears them in OnDisable.
[DisallowMultipleComponent]
public sealed class ForestWind : MonoBehaviour
{
    [SerializeField] float bearing = 70f, calm = 0.03f, gustPeak = 0.55f, gustWavelength = 120f, gustSpeed = 3.5f, gustSeed = 0.37f;
    [SerializeField] float leanWander = 15f, leanWanderPeriod = 90f, peakWander = 0.25f, peakWanderPeriod = 40f;   // the gust frame never wanders (2.7)
    [SerializeField] ForestWindKnobs knobs = ForestWindKnobs.Shipped;

    public static ForestWind Active { get; }                       // the enabled controller, or null
    public ForestWindState Current { get; }                        // what was written this frame
    public ForestWindKnobs Knobs { get; set; }                     // run-time overrides (harness, tests); never saved
    public void ResetKnobs();                                      // back to the serialized knobs
    public float SampleGust(Vector3 position);                     // e now, 0..1, same as the shader
    public float SampleStrength(Vector3 position);                 // W at a point now (calm + (peak - calm) e; before the distance fade and shelter)
    public static void SetMotionVectorPass(WindMotionVectors mode);   // on every loaded wind material; Shipped restores the recorded states
}

[Serializable]
public struct ForestWindKnobs
{
    public float fadeEnd;          // m; fadeStart = 0.7 x fadeEnd; at most 32 m when gustTexture is on (the texture's half window)
    public float flutterEnd;       // m; never above 0.7 x fadeEnd (ferns and grass only)
    public int moonCascades;       // 0 rest pose, 1 sway only, 2 full
    public float strengthScale;    // 1 shipped; scales calm and peak; 0 = the "off" state
    public bool fieldEnabled;
    public bool gustTexture;       // knob 2: e from FoliageInteraction.compute's Gust kernel instead of per vertex
    public float torchShadowEnd;   // knob 4: m; 0 = the torch's shadow map animated to fadeEnd
    public static readonly ForestWindKnobs Shipped;   // 30, 10, 0, 1, true, false, 0 (section 7.2)
}

public enum WindMotionVectors { Shipped, All, None }

/// Steps the interaction field once per frame (LateUpdate, guarded by Time.frameCount) on the root "Foliage" object.
[DisallowMultipleComponent]
public sealed class FoliageInteractionField : MonoBehaviour
{
    [SerializeField] ComputeShader stepShader;                // Assets/Game/Shaders/FoliageInteraction.compute
    [SerializeField] FoliageDensityMap densityMap;            // ForestData/FoliageDensity.asset
    [SerializeField] int resolution = 256;
    [SerializeField] float texelSize = 0.2f;
    [SerializeField] float decaySeconds = 0.8f;               // tau
    public static FoliageInteractionField Active { get; }
    public Transform Focus { get; set; }                       // window centre; null = Camera.main
    public bool FieldEnabled { get; set; }
    public Vector4 Rect { get; }
    public RenderTexture Current { get; }                      // for tests
    public RenderTexture GustTexture { get; }                  // knob 2; null while ForestWind.Active.Knobs.gustTexture is off
}

/// A body that parts foliage. The player's sits on its own child "FoliagePusher" of the Player root and reads the
/// CharacterController in its parents (radius, height: crouching lowers the band); dinosaurs get one later.
public sealed class FoliagePusher : MonoBehaviour
{
    [SerializeField] float radius = 0.35f, length = 0f, bandTop = 1.8f, softness = 0.25f, strength = 1f, rustleStep = 1.0f;
    [SerializeField] bool isPlayer;
    public float Radius { get; } public float Length { get; } public float BandTopWorld { get; } public float Strength { get; }
    public bool IsPlayer { get; } public Vector3 Velocity { get; }
    public FoliageContact Contact { get; }                     // updated every frame from the density map
    public event Action<FoliageRustle> Rustled;
    public void GetStamp(out Vector2 from, out Vector2 to);    // the body axis this frame (world XZ); from == to for a round body
}

public static class FoliageInteraction
{
    public static IReadOnlyList<FoliagePusher> Pushers { get; }
    public static event Action<FoliageRustle> Rustled;
    public static FoliageDensityMap Density { get; set; }
    public static float SampleDensity(Vector3 position, float radius, float bandBottom, float bandTop);   // 0..1
}

public enum FoliageLayer { None = 0, Low = 1, Mid = 2 }
public readonly struct FoliageContact { public readonly Vector3 Position; public readonly float Density, Speed, Intensity; public readonly FoliageLayer Layer; }
public readonly struct FoliageRustle
{
    public readonly Vector3 Position; public readonly float Intensity, Density, Speed, Time;
    public readonly FoliageLayer Layer; public readonly FoliagePusher Source; public readonly bool IsPlayer;
}

public sealed class FoliageDensityMap : ScriptableObject
{
    public Vector2 origin; public float cellSize = 0.5f; public int width, height;
    public byte[] low, mid;
    public float Sample(Vector2 xz, FoliageLayer layer);       // bilinear, 0 outside
}
```

The project enters play mode **without a domain reload**: every static (`Active`, `Pushers`, the events, the recorded
material states) is reset in a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`
method, and `OnDisable` of `ForestWind` and `FoliageInteractionField` clears their globals.

New shader: `Assets/Game/Shaders/FoliageInteraction.compute`, kernel `Step` (8 x 8 threads), inputs `_Previous`
(Texture2D float4), `_Current` (RW), `_Shift` (int2 texels the window moved), `_Decay` (`exp(-dt / tau)`), `_Rect`
(origin x, z, texel size, resolution), `_Stamps` (float4[32]: per stamp `(ax, az, bx, bz)` then
`(radius, bandTopWorldY, strength, softness)`), `_StampCount` (at most 16: the 16 pushers nearest the window centre;
any more are skipped, with one warning per session). Per texel: read the shifted previous value (0 outside), multiply rg and a by `_Decay`, clear it below 1 %;
for each stamp: `off` = texel centre minus the closest point on segment a-b; `m = strength * (1 - smoothstep(0.4 r,
r + softness, |off|))`; direction `off / |off|` when `|off| > 0.02 m`, else the left perpendicular of the pusher's
velocity (if moving faster than 0.1 m/s), else none (press only); if `m` exceeds the stored press, store
`(dir * m, bandTopWorldY, m)`. When `Time.deltaTime == 0` the field does not step and the previous field and rect are set
equal to the current ones, so the motion vectors are zero while paused.

Kernel `Gust` (knob 2, section 7.4; 8 x 8 threads). It writes `e = GamexGust(cellCentre.xz, _GustTime)` into a
64 x 64 R16F texture of 1 m cells centred on the focus and recentred by whole cells. It includes `ForestWind.hlsl`, so
the function is the vertex shader's own, and uses the same globals. `_GustTime = Time.time + timeOffset`, which is the
Time node's value in play (HDRP takes `Time.time` for a game camera: `HD/Runtime/RenderPipeline/Camera/HDCamera.cs:1204`).
`ForestWindGlobals.WriteGustTexture` publishes it with its rect. The vertex function samples it bilinearly at the pivot
in place of the per-vertex gust. With the knob off, the rect's valid flag is 0 and the analytic gust runs.

New editor file `F/ForestWindRig.cs`:

```csharp
public static class ForestWindRig
{
    public static ForestWind AddController(GameObject presetObject, ForestLightingPresets.Preset preset);   // called by ForestLighting.Build for each preset object
    public static FoliageDensityMap BakeDensity(ForestPlan plan);       // writes ForestPaths.DataFolder + "/FoliageDensity.asset"
    public static GameObject BuildFoliage(ForestPlan plan);             // root "Foliage" with FoliageInteractionField (compute shader and density map wired)
    public static FoliagePusher AddPlayerPusher(GameObject player);     // child "FoliagePusher" of the Player root, isPlayer = true, radius 0.35
    public static bool ApplyPassableStem(GameObject instance, ForestPlacement placement);   // disables the young fir's capsule when scale <= PassableYoungFirMaxScale (0.705)
}
```

Density bake: low layer = fern patch discs 1.0 m x scale, fern clumps 0.4 m x scale, grass patches 0.5 m x scale; mid
layer = young firs, a disc of the lower crown's radius x scale (the largest horizontal reach of LOD1's foliage vertices
between 0.3 and 2.0 m, measured once per prefab). Density 1 inside, falling linearly to 0 over the outer 30 % of each
disc, layers combined by maximum.

Changed files (back up first):

| File | Change |
|---|---|
| `F/ForestLightingPresets.cs` | None in I2: I1 added the wind fields (5.2). `ForestWindRig.AddController` copies them into the controller's serialized fields |
| `F/ForestLighting.cs` | `Build` calls `ForestWindRig.AddController` on each preset object, so a lighting-only rebuild keeps the wind |
| `F/ForestWorldRig.cs` | `Instantiate` calls `ApplyPassableStem` after `ApplyTint`; `BuildPlayer` calls `AddPlayerPusher` |
| `F/ForestSceneBuilder.cs` | After step 8 (instances): `ForestWindRig.BuildFoliage(plan)`; the NavMesh bake (step 9) then sees the disabled capsules |
| `Assets/Game/Diagnostics/ForestFrameHarness.cs` | `-forestMeasureWindAB <states>` (section 7.3) and `-forestMeasureWind <state>` |
| `Assets/Game/Diagnostics/Gamex.Diagnostics.asmdef` | One added reference, `"Gamex.Runtime"` (recorded): the harness calls `ForestWind` directly |
| `Assets/Game/Tests/ForestEditMode/ForestSceneTests.cs` | Adapt `PlayerRig_MatchesGreybox`; add `Scene_PassableStemsFollowTheRule`, `Scene_WindIsWired` |

New test files: `Assets/Game/Tests/ForestPlayMode/ForestWindPlayTests.cs`, `ForestWindSupport.cs` and the PlayMode
copy of `ForestWindProbeSupport.cs` (section 5.2). The helpers:
- `RenderSequence(camera, viewpoint, warmup, frames, every, onFrame)`;
- `TemporalStats` (Welford: mean, std, range, share above a threshold);
- `SaveHeat`;
- `MotionVectorView(bool on, float scale)`, restored in `finally`;
- `MeasurementVolume()` (film grain 0 at priority 1000);
- `CreateRaptorProxy(Vector3 position, float yaw)`: a grey HDRP/Lit capsule mesh, trigger collider, `FoliagePusher`
  radius 0.45, length 2.6 along its forward, band top 1.6 m;
- `WalkProxy(...)`;
- `GustCentredOffset(view, seconds)`: calls `ForestWindGlobals.GustCentredOffset` (I1), the time offset that puts a gust crest over
  the view's look-at point `seconds` into a window;
- `ReadField()`: an `AsyncGPUReadback` of the interaction field, probe-compiled (`probes/W1/rev/RevApiProbe.cs`).

Scratch tools, `B/tools/w1/`. **I1 writes all of them except `i2-final.sh`**, because its own steps 1 and 8 to 10 use
them (Unity runs, temporal maps, MP4s, the zero-wind player build and its paired runs). I1's `paired.sh` and `paired.py`
take the N1 player as the second state. I2 extends them to the named states of 7.3 and adds `i2-final.sh`.
- `unity.sh`;
- `build-player.sh <name> <tag>`: a copy of N1's, into `Builds/<name>`, which puts the settings back and checks them;
- `run-player.sh`;
- `paired.sh <prefix> <runs> <states> [views] [args]`;
- `paired.py`: N1's, generalised to named states;
- `tests.sh <tag>`: the three suites;
- `tstd/tstd.swift` (section 8.2);
- `mp4/mp4.swift`: PNG folder to H.264 MP4 with `AVAssetWriter`. `ffmpeg` is not installed, and `/usr/bin/avconvert`
  converts movies, not image sequences. Typecheck-probed (`probes/W1/rev/mp4probe.swift`);
- `i2-final.sh`.

### 6.3 Steps

1. Runtime classes and the compute shader; compile with `compile-runtime.sh`; harness compiles against Gamex.Runtime.
2. Editor integration; rebuild the scene only (`ForestSceneBuilder.Build`; the kit is unchanged unless the motion-vector
   level table changes); audit passes; placements unchanged.
3. EditMode tests of 8.6; the three suites.
4. Play-mode motion sequences of 24 s, temporal maps over the whole window, MP4s and contact sheets (8.2);
   motion-vector view (8.3); look at every image and watch every MP4; tune the profile values and night values within
   the ranges of 2.6, keeping the gust statistics of 2.7 (the CPU test pins them).
5. TAA ghosting check (8.3) at fir_close, crouch_hide and the pusher site: decide the motion-vector set. If it changes,
   change `ForestKitSpec.MotionVectorLevels`, rebuild the kit and the scene.
6. Pusher walk-through (8.4), including the geometric clearance checks, and the rustle hook test.
7. Harness mode, player build `Builds/ForestW1`, measurement and knob tuning (section 7). **Before this step** the
   coordinator asks the owner to confirm or change the W1 cost cap of 7.1. Until he answers, the provisional cap holds.
8. Final: static regression again (edit mode, `captures/w1-final-static`), the full capture `captures/w1-final`, the
   three suites twice, the final paired runs, the material folder's md5 unchanged by the test runs, notes, STATE.

### 6.4 I2 done criteria

- Every check of section 8 done and looked at; all tests of 8.6 pass; forest EditMode, forest PlayMode and greybox
  (7 of 7) pass twice.
- Player build: worst standard view 16-frame median at most 12.5 ms, and W1 within its cost cap (section 7.1), with the
  knob decisions recorded. Or, if knobs 1 to 4 cannot reach that, the stop-and-ask report of 7.4 has been answered by
  the owner and his decision is applied.
- Static regression still within noise in edit mode; protected files unchanged; no temporary file in the project.

---

## 7. Performance plan

### 7.1 Budget, W1's cost cap, and how they are judged

- **Budget** (owner-approved): in the macOS development player at 2,560 x 1,440, vsync off, 16-frame windows, the
  **worst standard view median is at most 12.5 ms**. Reported alongside: the 16-frame p95 (15 ms is the reference used
  by D2 and N1).
- **Where N1 left it**: the worst view (pano_90, torch on) reads 11.26 to 12.48 ms across sessions, and 11.46 and
  11.57 ms in N1's final paired runs (`out/N1/final-paired.txt`). N1's handover (`STATE.md:1243`): "Margin: 1.0 to
  1.2 ms at the worst view in a normal session, about none in a slow one. Dinosaurs need room."
- **W1 cost cap (provisional; the owner confirms or changes it before I2 step 7)**: the median paired `on - off` is at
  most **0.5 ms** at pano_90 and at each of the eight worst views. In addition, I1's zero-wind fixed cost is at most
  0.3 ms at pano_90 (I1 step 10). W1 thus takes at most about 0.8 ms of N1's 1.0 to 1.2 ms. The coordinator puts the
  question to the owner: keep 0.5 ms, or another figure. His answer, recorded in `issues/W1-I2.md`, replaces the
  provisional cap.
- **What to expect**:
  - The model puts the starting configuration plus the field at 0.65 to 1.3 ms at pano_90 (`runtime-perf.md` 3.3,
    configuration G plus the field). pano_90 would then read about 12.1 to 12.9 ms, and `on - off` would exceed the
    cap. **The starting configuration is expected to miss at pano_90.**
  - Shortening distances or removing motion vectors barely moves that view in the model. The scan level's pass is worth
    0.04 ms there (G against J), and 30 to 15 m is worth 0.02 to 0.05 ms (G against I).
  - The levers that act on pano_90 are the ones that make every evaluation cheaper and change nothing visible:
    conifer flutter off (already in, 2.6) and the lighter vertex function (knob 2).
  - The model's absolute cost is uncertain by up to a factor of two; only the paired player runs decide.
- **Gate**: in the final session, the W1 `on` worst-view median is at most 12.5 ms in every final run, **and** the cap
  holds. A session is slow, and is repeated later, when N1's player (interleaved) reads a worst median above 11.8 ms in
  it.

### 7.2 Starting configuration (shipped unless the measurement says otherwise)

| Knob | Start |
|---|---|
| Motion vector pass | `__mv` materials on the pushed set only: young-fir scan level, fern patch LOD0, fern clumps, grass |
| Vertex function | analytic gust per vertex (`gustTexture` off); conifer flutter off by profile; zero terms skipped by uniform branches |
| Animation distance | fade 21 to 30 m, per-instance early-out |
| Flutter | ferns and grass within 10 m (fade 7 to 10 m) |
| Moon cascades | rest pose (mode 0); the torch's shadow map gets the full wind |
| Torch shadow map | animated to the full distance |
| Interaction field | on, 256 x 256, 0.2 m texels, tau 0.8 s |

### 7.3 Measurement protocol (built on D2 and N1)

- Harness: `-forestMeasureWindAB <states>`, modelled on `-forestMeasureTorchAB`: for every view, each state in a row,
  each after its own 120-frame warm-up and 300 recorded frames; torch on, preset Night. Worst-view figures cover the
  `on` rows.
- **States**:
  - `off`: strength scale 0, field off, motion vector pass off on every wind material (the new shaders doing no work);
  - `on`: shipped;
  - `mvall` and `nomv`: the pass on every wind material, and on none;
  - `moon1` and `moon2`: cascades sway only, and full;
  - `flutter21`: fern and grass flutter to the fade start (21 m), the farthest the rule of 7.5 allows (`flutterEnd <=
    fadeStart`);
  - `gusttex`: knob 2's gust texture;
  - `torch15`: the torch's shadow map animated within 15 m only (`_GamexWindParams.z`, a knob field
    `torchShadowEnd`; 0 = the fade end);
  - `d25`, `d20`, `d15`: animation distance;
  - `nofield`;
  - `push`: a raptor proxy walking a 6 m loop 4 to 8 m in front of the camera. The harness builds its own proxy, a
    capsule mesh with a `FoliagePusher` of the test proxy's size, because it cannot use test code.
  
  The harness sets each state through `ForestWind.Active.Knobs`, `ForestWind.SetMotionVectorPass` and
  `FoliageInteractionField.Active.FieldEnabled`, and points `FoliageInteractionField.Active.Focus` at its measurement
  camera.
- **Wind during measurement**: the Night preset's calm and lulls would make `on` measure mostly the cheap calm state.
  But the vertex cost does not depend on W, only on which branches run, and those depend on amplitudes and distances,
  not on W. So `on` uses the shipped preset as it is. The `push` state adds the field's cost under a moving body.
- **Self-check by counters**: `mvall` must add vertices over `on`, and `nomv` remove them (`runtime-perf.md` 3.3 gives the
  expected order: about 7.8 M at pano_90 for all foliage). A zero delta means the drawer did not re-read the material at
  run time; that knob is then measured with separate builds.
- **Iterations**: the eight worst views (pano_90, hollow_rim, deadfall, pano_0, long_sightline, trail_mid_fwd,
  trail_strip_05, crouch_hide), three paired runs 20 s apart (`tools/w1/paired.sh`).
- **Final**: all 29 views, two paired runs with `off,on,<the costliest knob>`, interleaved with two runs of N1's player;
  then `Budget_Counts` and `Budget_FrameTime`.
- **Analysis** (`paired.py`): per view and state the 16-frame median, p95 and mean; `on - off` and `knob - on` per run
  and their median; an empirical cost per million wind evaluations from `on - off` (replaces the research estimate).

### 7.4 Knobs, in order (apply in this order until the gate and the cap hold)

Knobs 1 and 2 change nothing the owner sees. Knobs 3 and 4 trade W1's own new motion. Anything that changes N1's
approved night, or goes below the owner's offer, waits for him (step 5).

1. **Motion vectors only where the ghosting check needs them** (8.3). This is already the start. If TAA copes on the
   young-fir scan level, take its `__mv` off: the largest single saving at crouch_hide (0.47 to 0.83 ms in the model),
   though only 0.04 ms at pano_90. There is no visible change, by the check's own rule.
2. **A lighter vertex function**. Each change is measured paired, and kept only if the pose sequences of 8.2 at the
   same time offsets match the analytic version within the noise of identical runs:
   - (a) **The gust texture.** The `Gust` kernel (section 6.2) writes `e` per 1 m cell over a 64 x 64 window around the
     focus, and the vertex function samples it at the pivot. One fetch replaces five sines and their arithmetic in
     every vertex. This is the mechanism for "the gust once per instance": a vertex shader cannot run work once per
     instance, and per-instance MaterialPropertyBlocks would take the renderers off the GPU Resident Drawer. It requires
     `fadeEnd <= 32 m` (the texture's half window). The motion-vector error it introduces is under half a pixel (2.3).
   - (b) The trunk oscillation's second harmonic from a cheap polynomial of the first sine instead of its own sine,
     if the phase maps show no change.
   - (c) Already in: every zero-amplitude term and the flutter outside its range are skipped by uniform branches.
   
   **Metric**: the count of transcendental calls and texture fetches in the vertex function of the generated MSL (3.7),
   before and after, and the paired `on - off` per million wind evaluations. There is no instruction-count target: the
   Metal output gives none.
3. **Animation distance**, by the rule of 7.5. The owner offered this trade.
4. **W1's own motion trades**:
   - moon cascades stay at mode 0 (already the start; mode 1 costs back 0.2 to 0.4 ms if 8.5 shows swimming);
   - fern and grass flutter within 10 m, then 6 m;
   - last, the torch's shadow map animated within 15 m only (`torch15`; 0.05 to 0.17 ms in the model), last because
     the torch's shadows are the visible ones.
5. **Stop and ask.** If the gate or the cap still fails, apply nothing more. Report to the coordinator with the paired
   numbers and these options for the owner:
   - (a) fund from N1's night: moon shadows from 50 m and 3 cascades to about 30 m and 2 cascades
     (`ForestLightingPresets.Night`; 0.4 to 0.6 ms in the model; N1's own next lever). This changes N1's approved
     night, so its stills are re-captured and shown with the question;
   - (b) a higher W1 cap;
   - (c) an animation distance below 15 m, which breaks the pusher-site rule of 7.5;
   - (d) motion vectors off where ghosting showed trails.

If the gate and the cap hold with at least 0.3 ms to spare, restore quality in reverse order, each step only if both
still hold: torch shadows to the full distance, flutter distance, moon mode 1 if swimming showed, and motion vectors on
young LOD1 or tall LOD0 if ghosting showed trails.

### 7.5 Rule for the animation distance (the owner accepts shortening it)

- Start at 30 m (fade from 21 m). Shorten in 5 m steps (25, 20, 15 m) only while the gate or the cap fails after knobs
  1 and 2, and only when the paired `dX - on` delta of that step saves at least 0.1 ms. The model says a step saves
  under 0.1 ms at pano_90 once the other knobs are in, so expect to stop at 30 or 25 m and go on to knob 4, then step 5
  of 7.4.
- Always `fadeStart = 0.7 x fadeEnd`, `flutterEnd <= fadeStart`, per instance, identical in every pass. With the gust
  texture, `fadeEnd <= 32 m`.
- Never below 15 m without the owner (7.4 step 5): the pusher sites and the closest LOD switches (young-fir scan to
  LOD1 at 2.2 to 6.2 m, fern patch LOD0 at 12 m and more) must stay inside the animated range.
- Every change is checked with the distance-band pose sequences of 8.2 (no visible line where motion stops) and noted
  for the owner with its measured saving.

### 7.6 Counters

`Budget_Counts` will move: the motion vector pass adds its triangles and vertices to the counters (about +2 M vertices
at crouch_hide with the starting set: 31.6 to about 33.6 M, above the 33 M warning, below the 36 M budget). Triangles
are expected to stay under 21.5 M (19.45 M at long_sightline after N1). A budget constant is re-based only with the
paired player evidence that the frame budget holds, as N1 did, with the reason in `issues/W1-I2.md`. Texture memory
(994.9 of 1,024 MiB after N1) gains 1 MiB for the two field targets.

---

## 8. Verification

### 8.1 Static regression (I1, and again at the end of I2)

- **Condition**: edit mode, no wind controller running, so every global of 3.4 is zero (no wind, no pushers).
- **What**: the full edit-mode capture (`ForestCapture.CaptureFull`, torch on, Night), `-captureBaseline
  captures/n1-final`, into `captures/w1-static`. Also `captures/w1-static-lit`: the same with the materials switched
  back to HDRP/Lit, which isolates the mesh change (colour channel, Float16 tangents) from the shader change.
- **What it proves, and what it does not**: with every global zero, step 2 of 3.5 returns before any wind code. So
  this checks the fragment port, the property set and the mesh compaction, and never runs the displaced path. The
  displaced path is checked by the speckle count below, the pose sequences (8.2) and the GPU probe tests (8.6).
- **Pass**, for every view, all of:
  1. RMSE to `n1-final` at most **1.2 x that view's noise reference**: the larger of the `w1-pre` and `w1-pre2` RMSEs
     (I1 step 1). There is no absolute floor; the first version's 0.0030 was up to 1.75 x the measured noise on the
     quiet night views. If a view fails while its difference image shows no structure, capture `w1-pre3` and take the
     largest of the three, recorded. The factor 1.2 never changes.
  2. **Signed bias on the lit mask**: over the pixels whose encoded Rec. 709 luma in `n1-final` exceeds 0.10,
     `|mean(w1 - n1)|` (encoded values) is at most the view's bias reference (I1 step 1) plus 0.002. The lit mask covers
     21.0 % of pano_90, 29.2 % of fir_close, 19.5 % of crouch_hide and 33.7 % of trail_start at 640 px
     (`probes/W1/rev/litshare.swift`). Night frames are mostly black, so a uniform 3 % darkening alone gives a
     whole-frame RMSE of only 0.0023 to 0.0031 (review measurement), and a 3 to 5 % shift on foliage alone would pass
     check 1. This check catches it.
  3. 0 error (magenta or cyan) pixels; the capture summary's judgement unchanged.
  
  Every view's difference image (`tools/d1/imgdiff`, amplified) is looked at. A difference that follows geometry
  (needle edges, trunk silhouettes, shading bands on foliage or bark) is a failure even within the thresholds, because
  noise has no structure.
- **Speckle count** (I1; the displaced path, depth prepass against G-buffer):
  - Views: fir_close and crouch_hide at full size (2,560 x 1,440).
  - Frames: four edit-mode poses at a constant W of 0.9 (time offsets 0, 0.7, 1.9 and 3.1 s), each against the
    wind-off frame of the same view.
  - Count, inside the moving mask (pixels that differ by more than 2/255 between any two of the four poses, dilated by
    2 px): isolated outliers, that is pixels whose encoded luma differs from the median of their 3 x 3 neighbourhood
    by more than 0.15 and lies above or below all eight neighbours. The wind-off frame is counted inside the same mask.
  - Pass: each pose's count is at most 1.2 x the off frame's count plus 50 pixels. This is provisional; it is frozen
    after the first look and recorded.
  - The worst pose's outliers are shown in a 200 % crop.
- **Kit**: `captures/w1-kit` against `captures/w1-pre-kit` (turntables, LOD sheets) with rules 1 to 3 (its noise
  reference: a second kit capture `w1-pre-kit2` in I1 step 1).
- If `w1-static-lit` passes and `w1-static` does not, the difference is in the graph: fix the HLSL (normal resolution,
  remaps, triplanar port, mip bias), never the threshold.

### 8.2 Motion sequences, MP4s, contact sheets and temporal difference maps

**Every sequence covers at least 20 s**: several sway periods (young firs 1.4 to 3.1 s, tall firs 3.0 to 5.8 s) and at
least one gust crest. `GustCentredOffset` places the crest over the view's look-at point 10 s into the window. All maps
(std, range, phase, time of maximum) are built over the whole window. The first version's windows (2 s in play, 3 s of
poses) were shorter than one trunk period and about a fifth of a gust cycle. A phase map over less than one period
cannot show whether motion is independent.

**Edit-mode pose sequences (I1)**: `ForestCapture -captureSet wind -captureWindPoses 300 -captureWindStep 0.0667
-captureWindStates night,steady:0.35 -captureWindViews fir_close,crouch_hide,hollow_rim,trail_start,long_sightline,canopy_up
-captureWindSize 960x540 -captureWindFrames 32`: 20 s at 15 poses a second.
- **Each pose** sets the globals through `ForestWindGlobals`, with `_GamexWindParams.x = offset + pose x step`. It is
  converged with 32 frames on a static pose (TAA history reset between poses) and saved with its sidecar into
  `captures/w1-poses/<view>/<state>/pose_NNN.png`.
- **States**: `night` is the preset, with a crest over the view's look-at point 10 s into the window
  (`ForestWindGlobals.GustCentredOffset`). `steady:S` sets calm = peak = S, the sway character
  without gusts. `strong:S` is the same at a higher value.
- **The capture camera** must be a game camera, which has HDRP time 0 in edit mode. A preview camera would animate with
  `realtimeSinceStartup` (`CORE/Runtime/Utilities/CoreUtils.cs:1502-1528`).
- **Cost**: a full capture renders 64 frames at 2,560 x 1,440 per view, 32 views in 38 s (`n1-final`). 6 views x 2
  states x 300 poses x 32 frames at 960 x 540 is expected at roughly 30 to 40 min; record it.
- **Also**:
  - distance bands at fade ends of infinity, 25 and 10 m (150 poses at 0.133 s = 20 s, fir_close and long_sightline,
    `steady:0.6`). The region that goes still between them is the band, and a smooth fall-off of the range across it
    is the no-pop evidence;
  - kit LOD sheets at a strong pose (`-captureWindKit`);
  - fir_close and crouch_hide at `strong:0.9` with the torch, for the shadow match and the speckle count of 8.1.
- **Limits**: edit-mode poses show positions, not TAA behaviour (each pose is converged). 15 poses a second resolves
  the 3.5 to 4 Hz fern and grass flutter.

**Play-mode sequences (I2)**: test `Wind_MovesFoliageAndStillWithoutIt` with `CreateMeasuredCamera(withFlashlight:
true)` and the measurement volume (grain off).
- **Views**: fir_close, crouch_hide, hollow_rim, trail_start, long_sightline, canopy_up, pano_90.
- **States**: `night` (the preset), `steady` (calm = peak = 0.35) and `gusty` (calm 0.2, peak 0.9), all with a fixed
  seed and the crest centred 10 s into the window. Each view also gets a **wind-off twin** (the noise floor: TAA
  jitter, volumetric noise, dithering).
- **Frames**: 180 warm-up frames, then 24 s at `Time.captureDeltaTime = 1/60` (1,440 frames), every 4th saved at
  960 x 540 (360 frames at 15 fps), frame 0 at full size, and the frame's mean luminance recorded per frame. The off
  twins run 8 s (120 saved frames), because noise is stationary.
- **Output**: `captures/w1-seq/<view>/<state>/`. Disk: about 8,400 frames at 0.3 to 0.5 MB, 3 to 4 GB; check the free
  space first.

**Tool `tools/w1/tstd`** (Swift, built like `tools/n1/swift/clipmask.swift`) takes a folder of PNGs and writes:
- the per-pixel std heat map, the range map and `|last - first|`;
- a **phase map**: the phase of each pixel's signal at a given frequency (`--freq <Hz>`, the class's sway frequency),
  as a hue. A uniform colour over a stand means unison; a speckle of colours means independent motion;
- a **time-of-maximum map**: a smooth gradient along the wind is a travelling front, and a uniform map is unison;
- a per-frame series of the share of pixels whose frame-to-frame change exceeds the floor;
- JSON: mean std; the share of pixels above 1, 2, 4 and 8 / 255; the shares by thirds of the frame.

Options: `--minus <off folder>` (the on - off map) and `--normalise` (divides by each frame's mean luminance, so exposure
drift is not read as motion).

**MP4s**: `tools/w1/mp4/mp4` writes an H.264 MP4 at 15 fps from each saved sequence, plus a side-by-side `on | off` for
fir_close. **For the owner**: one MP4 per class and per state (night, steady, gusty). The classes and their views are
young fir in fir_close, tall fir in canopy_up, fern in hollow_rim, and grass in the view where it fills most of the
frame (recorded). The push walk-throughs of 8.4 get MP4s too. The engineer judges from the contact sheets and maps,
because a PNG can be read and a movie cannot. The MP4 is what the owner watches.

**Contact sheets**: `tools/contact/contact <out> <tile> grid 6 <36 frames: every 10th>`, plus a crop row of a 512 px
region where the deviation peaks (needles at 100 %), plus a "first | crest | last | on - off std | phase | time of
maximum" sheet per view and state.

**Assertions** (thresholds set after a first look, then frozen and recorded):
- `steady`: at the near views, the share of pixels whose std exceeds the off twin's by more than delta is at least a
  minimum.
- `night`, the lull and the gust: the moving-pixel share, averaged over the 3 s around the crest, is at least 3 x its
  average over the quietest 3 s of the window. Gusts are at least 24 s apart and in a gust 19 % of the time (2.7), so a
  24 s window always holds a lull.
- Wind off: the frames are still (std at the floor).
- No error pixels; the night exposure window holds on frame 0; two runs of the same sequence agree to the noise of
  identical runs (determinism).

### 8.3 Motion vectors and TAA ghosting (I2, play mode only: in edit mode time and last time are both 0)

- **Motion-vector view** (`Wind_MotionVectorsCoverMovingFoliage`): `SetFullScreenDebugMode(MotionVectorsIntensity)`,
  heat off, read into a linear `ARGBHalf` target, settings restored in `finally`.
  - **Scale**: `motionVecVisualizationScale = W / 8`, with W the render width, so grey 1.0 = a 4 px horizontal move a
    frame: 160 at 1,280 wide, 320 at 2,560. HDRP stores motion vectors as NDC deltas, so the full width is 2.0, and the
    debug view outputs `length(mv) x scale`, unclamped with heat off (`HD/Runtime/Material/BuiltinUtilities.hlsl:13-28`,
    `Builtin/BuiltinData.hlsl:51-65`, `HD/Runtime/Debug/DebugFullScreen.shader:301-310`). A move of n px at width W is
    therefore `2n / W`. The first version's 640 meant 2 px at 2,560 and 1 px at 1,280. Vertical motion scales with the
    height instead: 4 px vertical reads `W / H` = 1.78 at 16:9.
  - **Static camera, wind on**, in the `steady` state (W = 0.35, not the night preset). HDRP zeroes each axis of a
    motion vector below `0.01 x _ScreenSize.zw` in NDC, about 0.005 px (`Builtin/BuiltinData.hlsl:23-24`,
    `KILL_MICRO_MOVEMENT`).
    In a night lull a sapling's top swings about 0.02 px a frame at 8 m, and its lower branches fall under that cut.
    Non-zero on every `__mv` material inside the animation distance. That includes the
    young-fir scan level's wood: its trunk sways with R, so its vectors are non-zero within the scan range. Zero on
    every renderer whose materials have the pass off (tall firs and every far level at the start) and on static props.
  - **Wind off**: zero everywhere except the raptor proxy (a moving renderer, the positive control).
  - **During a push**: the pushed fronds light up, which proves Custom Velocity.
- **Ghosting** (`Wind_TaaGhosting`):
  - Render frame A normally at a 1/90 step. Then freeze time without a camera cut (`Time.timeScale = 0`, so delta
    time, HDRP's last time and the motion vectors are zero and the field does not step), and render 48 more frames to
    let TAA converge on the held pose (frame B). Ghost = the share of the moving mask (8.2) where `|A - B|` exceeds
    the wind-off control's level. If `timeScale = 0` does not give a zero delta time under `captureDeltaTime`, use a
    step of 1e-6.
  - Configurations: the pass on and off for each candidate set (scan level, young LOD1 with a bent passable sapling,
    tall LOD0, ferns), at fir_close and crouch_hide in gusty wind, and at the pusher site as the proxy passes. Output:
    200 % crops `A | B | heat`.
  - **This decides knob 1, by comparing each set with itself**: keep the pass off for a set when `ghost(off) -
    ghost(on)` is within the tolerance (set after a first look at the crops, then frozen and recorded); otherwise turn
    it on. The wind-off control is only the noise floor. Even with perfect motion vectors, a TAA frame in motion
    differs from the converged still (reprojection blur, history rejected at disocclusions), so no moving
    configuration reaches the control's level. The first version's rule ("off where the ghost is at the control's
    level") would have turned the pass on everywhere.
  - **Assertion**: at every site, the shipped set's ghost is at most the all-on configuration's ghost plus the
    tolerance.

### 8.4 Pusher walk-through (I2)

- **Proxy**: `ForestWindSupport.CreateRaptorProxy` (radius 0.45 m, 2.6 m long, band top 1.6 m, trigger collider; it
  never touches physics or the NavMesh). Straight paths, grounded with `ForestPlaySupport.GroundHeight`, clear every
  **non-passable** trunk by radius + 0.1 m (checked against `placements.json`). The thicket path crosses at least two
  passable saplings (scale at most 0.705). Walk at 1.5 m/s, then trot at 3.5 m/s.
- **Sites**: the fern hollow around (28.3, -20.7) (15 fern patches and clumps within 2.5 m), camera 6 m to the side of
  the path at 1.65 m looking at its middle; the young-fir thicket around (-2.7, -50.5), from fir_close's pose
  (2.3, -53.1, yaw 302) with the path crossing left to right.
- **Recording**: from 1 s before the approach to 4 s after the pass, saved at 30 Hz, 960 x 540, into
  `captures/w1-push/<site>/<speed>/`, and an MP4 of each; the motion-vector view and the ghosting pair at the moment of
  the pass. Each site runs twice: with the night wind, and **with wind strength 0** (push only), which the trunk and
  geometry assertions use.
- **Assertions** (`Foliage_ProxyPartsFernsAndRecovers`, `Foliage_ProxyBendsThicketBranchesNotTrunks`):
  1. during the pass, change against the pre-approach frame is above the floor in a band around the path's screen
     projection, and at the floor outside it;
  2. 4 s after the pass the frame is back to the pre-approach frame within the floor (no lasting offset);
  3. in the push-only run, the trunks of **non-passable** trees do not move (a mask from those trunk renderers).
     Passable saplings bend whole by design and are left out; with the wind on, the scan level's wood sways, so it is
     not asserted then;
  4. fronds behind the body stay parted for more than 0.3 s (the field's trail);
  5. the rustle hook raised events inside both sites with density at least 0.5, and none while the proxy walked the
     open trail;
  6. motion vectors light the pushed fronds;
  7. **geometric clearance** (push-only run):
     - At the frame where the proxy's axis passes nearest each fir within 2 m of the path: read the field back
       (`ReadField`), then run `ForestWindProbe.compute`'s `Deform` kernel on those firs' LOD0 vertices. Use that
       frame's field, rect, instance matrices and camera, with wind strength 0.
     - Every vertex with a push weight above 0 (fir branches) whose rest height lies between 0.3 m above the ground
       and the band top must end at least `radius - 0.1 m` (0.35 m) from the body axis. Report the worst vertex and
       its distance either way.
     - Passable saplings: points on the stem's pivot axis every 0.1 m from 0.7 m to the band top (colour 0) end at
       least the radius (0.45 m) from the body axis.
     - The first version's push gave 2, 6 and 10 cm at 0.5, 1 and 1.5 m from a young-fir stem, and its stem push of
       0.30 m left the stem inside the body by 0.05 to 0.15 m and about 0.3 m from the first-person eye.
- **Player**: the CharacterController probe (`SwapPlayerForProbe`, with a `FoliagePusher` added as on the player) walks
  through the hollow's ferns, seen from a fixed third-person camera, plus one first-person sequence looking 45 degrees
  down while walking (`Foliage_PlayerPartsFernsAndRustles`).
- **`Foliage_PassableStemLetsThePlayerThrough`**:
  - The probe walks through an isolated passable sapling and ends beyond it. During the pass, the stem centreline
    from 0.7 m to the band top (the `Deform` kernel, as in assertion 7) stays at least the player's radius (0.35 m)
    from the capsule axis.
  - Against an isolated non-passable young fir, the probe stops at least 0.3 m short.

### 8.5 Other checks

- **Shadow match** (I1 edit mode, I2 play mode): the torch's shadows on trunks and litter move with the branches; no
  detached shadows, no acne bands; the young-fir scan level against its `LOD0_Shadow` proxy; moon mode 0 against mode 1
  side by side. With conifer flutter off, the proxy's LOD2 cards move only with sway and bob, which the needles share.
- **LOD consistency**: neighbouring levels bend the same way by the same amount at a strong pose (no cross-fade, so this
  is what makes switches invisible).
  - Walk check: a deterministic walk along the trail waypoints at walking speed (fixed `captureDeltaTime`, fixed seed,
    the same frames), run once with wind on and once with wind off.
  - For each LOD switch event (logged from the LOD groups' current level), take the frame-to-frame difference spike in
    a window around the switching renderer's screen bounds, wind on against wind off at the same frame.
  - Pass: the wind adds no more than the noise of two identical off runs at each event. The first version's rule,
    "spikes no larger than the off run's pops", let wind-induced pops hide behind the largest existing pop.
- **Speckled holes**: the speckle count of 8.1, plus near foliage looked at in the first play-mode captures at full
  size.
- **Edge culling**: displaced foliage at the screen edges in the walk sequence (no mesh-bounds padding in W1). A
  passable sapling bent 50 degrees moves its crown up to about 4.6 m, well outside its bounds; check it at the screen
  edge and in the torch's shadow (section 9).
- **Materials untouched by tests**: md5 of `ForestKit/Materials` before and after every test run (run-time pass toggles
  must be restored).
- **Drawer**: 0 `MissingDOTSInstancing` exclusions (I1) and the drawer on in the player (harness `DrawerState`).

### 8.6 Tests to add or adapt

| Test | Package, assembly | What it checks |
|---|---|---|
| `WindShaders_ImportAndCompileForMetal` | I1, EditMode | `ForestWindShaderGraphs.CheckShaders()` is empty. The files on disk equal `GraphText()`. `Shader.Find` finds both shaders by name (so the importer did not hide them). Every property name of 3.3 exists on both shaders (`Shader.GetPropertyName`), so a renamed property fails loudly. `_BaseColorMap` is not flagged `NoScaleOffset` (`Shader.GetPropertyFlags`) |
| `WindMaterials_FollowTheirRecipes` | I1, EditMode | Every wind material: on the wind graph (or HDRP/Lit when `WindShaderActive` is false), `ValidateMaterial` true, target tag, material type, diffusion profile hash for translucent, alpha-test/double-sided/transmission keywords for foliage, render queue 2475 for foliage, profile vectors equal to the table. **Tag `MotionVector = User` on every wind-graph material**, which HDRP sets on every Shader Graph material at its first validation. **The `MotionVectors` pass on exactly on the `__mv` twins and off on the plain variants** (off on all of them in the HDRP/Lit mode) |
| `WindMaterials_StaticPropsStayOnHdrpLit` | I1, EditMode | Every static prop's renderers and the `__ground` material use HDRP/Lit; no static prop uses a wind material |
| `WindMaterials_ResolveToTheManifest` | I1, EditMode | Every wind material except the `__crowncore` pair (no maps of its own, as today) resolves to a manifest entry through the audit's `ForestAudit.ManifestEntryOf`, which is made `public` for this test: plain, `__tint1`, `__triplanar` and every `__mv` twin |
| `WindPrefabs_EveryMovingPartSharesTheVertexCode` | I1, EditMode | Every renderer of a wind prefab, `LOD0_Shadow` included, uses wind-graph materials except the root-skirt ground material; `__mv` materials exactly on the levels below `MotionVectorLevels`; `ApplyTint` keeps the `__mv` choice |
| `WindData_LayoutAndChannels` | I1, EditMode | Wind meshes: the 44 B layout of 4.1, readable; static meshes unchanged; B = 0 on wood, G = 0 within the trunk radius and on the skirt, R = 0 on ferns and grass, every channel in range |
| `WindData_IsLodInvariant` | I1, EditMode | For each tree, matched points of each level against LOD0 (nearest vertex within 2 cm; at least 1,000 matches per level, else the test fails as unable to compare): `abs(dR) <= 2/255`, `abs(dG) <= 0.06`, circular `abs(dA) <= 0.05`. For the fern far level against near at the same slots: the same, plus `dB` exact. Tree `dB` (at most 0.06) is asserted whenever a conifer profile's flutter is non-zero. With flutter 0 (the start) the test asserts instead that both conifer profiles have `flutterAmplitude == 0`, so turning flutter on forces the B fix of 2.8 |
| `WindData_FormulasOnSyntheticTree` | I1, EditMode (pure) | A synthetic leaning cylinder trunk with two branches and needle quads: R, G, B, A as in 4.3, phase continuous across the 1 to 0 wrap, deterministic. Synthetic fern and grass clumps: one clump phase per slot, with fronds and blades within 0.12 cycle of it |
| `WindData_CompactRoundTrip` | I1, EditMode | `Compact` keeps vertex order, indices, sub-meshes, bounds; tangents within 1e-3 |
| `WindGlobals_GustHasLullsAndTravellingFronts` | I1, EditMode (pure) | The CPU mirror at the Night preset (`ForestLightingPresets` values, with the peak wander of 2.7 applied by the test exactly as `gust_tune.py` does), 3,600 s in 0.1 s steps at 25 points on a 1 km grid (out to `abs(xz)` = 500 m): `e` in [0, 1]; at every point the share of time with W below 0.3 x its mean is at least 50 %; gust events (rule of 2.7) at least 20 s apart at every point, median 25 to 45 s; fronts move downwind at `gustSpeed` plus or minus 10 % at `abs(xz)` = 500 m while the test swings `leanBearing` by plus or minus 15 degrees at up to 0.018 rad/s (the controller's wander is I2's; the gust frame ignores it either way); `Clear` zeroes every global. It pins the tunables of 2.7: a retune may change it with the reason recorded (rule 4) |
| `WindShader_GustMatchesCpuOnGpu` | I1, EditMode (GPU) | `ForestWindProbe.compute`'s `Gust` kernel (it includes `ForestWind.hlsl`, so it is the shader's own function) at 4,096 samples of `(xz, t)`, with `abs(xz)` up to 500 m and t up to 600 s, read back and compared with `SampleGust`: the largest `abs(GPU - CPU)` in `e` is at most 2e-3. The error at t = 3 h is reported, not asserted |
| `WindShader_ProfilesGiveFinitePositions` | I1, EditMode (GPU) | The `Deform` kernel on synthetic vertices (R, G, B and A over [0, 1], heights 0 to 20 m). Profiles: young fir, tall fir, fern, grass and an all-zero profile. Conditions: calm = peak = 1, t in {0, 1.3, 600}, a valid field at full press. Every output is finite. Fern and grass (trunk amplitude and period 0) give finite positions. The all-zero profile returns the rest pose exactly |
| `FoliageMaterial_MatchesHdrpLeafSample` (adapted) | I1, EditMode | For a material on the wind graph, the `_NORMALMAP` keyword (which the graph does not have: it always samples the normal map) is replaced by "the `_NormalMap` slot holds the material's normal map". Everything else unchanged |
| `Scene_PassableStemsFollowTheRule` | I2, EditMode | Every young fir with scale at most 0.705 has its capsule disabled, and every other tree an enabled one. No young-fir scale lies within 2e-3 of the threshold, so the shader's recomputed scale agrees with the rule. NavMesh, for at least 20 isolated firs of each kind (no other collider within 2.0 m of the stem): a `NavMesh.Raycast` across the stem, from 1.0 m on one side to 1.0 m on the other at 4 azimuths, is free for passable firs and blocked for non-passable ones. The negative cases are chosen among non-passable firs at scale 0.75 or more (994 of them). Why: agent radius 0.5 m and 0.167 m cells put a thin capsule's hole edge anywhere from about 0.4 to 0.6 m, so the first version's fixed `SamplePosition` distances could be flaky; a ray across the stem asks only whether a hole exists (probe-compiled, `probes/W1/rev/RevApiProbe.cs`) |
| `Scene_WindIsWired` | I2, EditMode | One `ForestWind` on each preset object with that preset's values (bearing, calm, peak, wavelength, speed); one `FoliageInteractionField` with the compute shader and the density map; exactly one `FoliagePusher` on the forest player, on child `FoliagePusher`, `isPlayer` |
| `FoliageDensity_MatchesThePlan` | I2, EditMode | Density at least 0.5 at fern patch centres, about 0 on the trail centre line, mid layer at least 0.5 next to young-fir stems |
| `PlayerRig_MatchesGreybox` (adapted) | I2, EditMode | As N1 did for the torch: the `FoliagePusher` child subtree is the one more difference allowed; exactly one on the forest player, none on the greybox's |
| `Wind_MovesFoliageAndStillWithoutIt` | I2, PlayMode | 8.2 |
| `Wind_MotionVectorsCoverMovingFoliage` | I2, PlayMode | 8.3 (scale render width / 8; in the `steady` state, non-zero on every `__mv` material including the scan level's wood; zero where the pass is off and on static props) |
| `Wind_TaaGhosting` | I2, PlayMode | 8.3. Writes its decision table (`ghost(off) - ghost(on)` per set and site) to the output folder, and asserts the shipped set's ghost is at most the all-on configuration's plus the tolerance at every site |
| `WindGust_TextureMatchesAnalytic` | I2, PlayMode | Knob 2 on: the gust texture at 200 pivots inside its window against `SampleGust`, at most 1e-2 (bilinear over 1 m cells). Pose frames at the same time with the knob on and off agree within the noise of identical runs |
| `Foliage_ProxyPartsFernsAndRecovers`, `Foliage_ProxyBendsThicketBranchesNotTrunks` | I2, PlayMode | 8.4 (trunk stillness only for non-passable trunks, in the push-only run; geometric clearance) |
| `Foliage_PlayerPartsFernsAndRustles`, `Foliage_PassableStemLetsThePlayerThrough` | I2, PlayMode | 8.4 (with the stem-centreline clearance) |

All play-mode wind tests restore knobs, material pass states, `Time.timeScale` and debug settings in `TearDown` /
`finally`. GPU probe tests release their buffers in `finally`. **The forest suites (EditMode, PlayMode) and the
greybox's seven tests must all pass**, at the end of I1 and twice at the end of I2.

---

## 9. Risks, each with its fallback

| Risk | Fallback |
|---|---|
| A generated `.shadergraph` imports as the error shader or fails validation | Base every JSON object on graphs Unity wrote (5.3 step 2: HDRPLit for the target and properties, Fern for the vertex blocks and the Custom Interpolator, PseudoSubsurface for the File-mode Custom Function), read the importer's messages, iterate; `CheckShaders` and the variant compile are the gate. If the generator cannot be made valid in reasonable time: copy `HDRPLit.shadergraph` with a new GUID and new object ids, blank its `m_CustomEditorGUI`, and add our two Custom Function nodes, the interpolators and the wind properties to it |
| Shader Graph Lit does not match HDRP/Lit within noise (static regression) | `w1-static-lit` isolates mesh from shader; fix the HLSL (normal resolution, remaps, mip bias, specular occlusion), never the threshold. **If the triplanar port cannot match**: (1) bake UV0 from the rest position for the triangles that draw `fir_tree_01_bark__triplanar`, which are c's UV-less trunk, b's off-atlas upper trunk (`SplitOffAtlas`) and the far trunk. Use a box projection: each triangle on the plane of its face normal's largest axis, vertices duplicated where the plane changes, at the triplanar's tiles per metre. b's branches keep their own UVs (they drew `fir_tree_01_bark` before `BranchesTakeOffAtlasBark`). Then draw them all with `fir_tree_01_bark` on the UV0 graph, in the same sub-mesh slots, so b keeps the contract's four. A box map is not the triplanar blend, so the static regression will show a difference on those surfaces: record it with before and after crops, and ship it only with the owner's sign-off. (2) Otherwise keep the triplanar graph and accept a small recorded static difference on the dark triplanar bark, also with the owner's sign-off. **Never ship static tall firs**: the first version's "flutter only" fallback would leave all 796 tall firs still (conifer flutter is 0, and real needle flutter would be at most about 3 mm at a crest, 2.6), which undoes the owner's request |
| Speckled holes in near foliage: the depth prepass, the G-buffer (ZTest Equal for alpha-tested deferred) and the motion vector pass, which runs after the G-buffer, compute different positions (`HD/Runtime/RenderPipeline/HDRenderPipeline.Prepass.cs:564-586`) | The same code path in all three (no pass-dependent code outside shadow passes), `precise` on the final position. **Check, not assumption** (I1 step 8): in the saved MSL (3.7) of `DepthOnly`, `GBuffer` and `MotionVectors` for the foliage set, the displaced-position code must be the same sequence of operations, `fma` use included. That shows whether `precise` survived fast math. The speckle count of 8.1 at full size decides. If it fails: (a) quantise the final object-space displacement to a 2^-12 m grid (`round(offset * 4096) / 4096`, 0.24 mm) before adding it to the rest position, so a last-bit difference between passes almost never changes the result, then recount; (b) if speckles remain, report to the coordinator with the 200 % crops before considering any frame setting (a project setting: back up, record, greybox check) |
| Float16 tangents rejected or lost (`SetVertexBufferParams`, `CopySerialized`, `RecalculateTangents` order) | Normals and tangents both Float16 x 4; else non-readable render meshes after checking the kit tests and capture code on them; last, re-base Mesh Memory with the recorded reason (rule 4) |
| Colours paired with the wrong vertices | Colours always set before `Optimize`, compaction always after; `WindData_IsLodInvariant` and the synthetic test catch it |
| The drawer excludes the wind renderers (`MissingDOTSInstancing`) | Check the include uses only `UNITY_MATRIX_M` and the Transform helpers; compare the generated pragmas with HDRPLit's; the cost without the drawer would be measured and reported |
| The gate or W1's cost cap (7.1; 0.5 ms provisional) fails at pano_90, which the model expects for the starting configuration | The order of 7.4, the only one: knob 1 (motion vectors where ghosting needs them) and knob 2 (the lighter vertex function, gust texture first), which change nothing the owner sees; then knob 3 (distance, by the rule of 7.5, the owner's own offer); then knob 4 (W1's own motion trades). Then **stop and ask** (7.4 step 5) with the paired numbers. Never fund W1 from N1's approved night (moon shadow distance and cascades) or go below 15 m without the owner |
| I1's zero-wind fixed cost exceeds 0.3 ms at pano_90 (I1 step 10) | Stop and report before I2 starts (5.3 step 10). With it: the `GBuffer` and `DepthOnly` MSL of the graph against HDRP/Lit's for the same material (extra samples: the graph always samples the mask and normal maps; the normal path), the `__mv` pass's share from the counters, and the drawer state. The coordinator decides |
| Custom Velocity gives no or doubled push motion vectors | Detect the previous-frame evaluation in the function (`abs(Time - _TimeParameters.x) > 1e-6`) and sample `_GamexInteractionTexPrev` there; the motion-vector view proves either |
| The drawer does not re-read a material's pass toggled at run time (paired `off`, `mvall`, `nomv` states) | The counter self-check shows it; measure those states with separate builds |
| Static moon cascades make shadows swim on moving needles | Mode 1 (sway only), at its measured cost |
| Displaced foliage culled at screen edges or out of a shadow cascade (bounds not padded) | Wind amplitudes are small: a trunk top moves under 0.4 m at a night crest with the peak wander. Pushes move fronds up to 0.45 m and fir branches up to 0.55 m. The large case is a passable sapling bent 50 degrees: its crown moves up to about 4.6 m (8.5). For the player's own push the camera stands within the sapling's bounds, so the case that matters is a raptor bending one seen from a distance. If the walk sequence or the push sites show culling: a renderer local-bounds override set by the kit builder, if it is serialized and used by the drawer; else padded mesh bounds, with the LOD group size computed from the unpadded bounds and the bounds tests reading the kit report's box, with the coordinator's approval |
| The NavMesh bake still collects a disabled capsule, or the override does not stick | `NavMeshModifier.ignoreFromBuild` on the instance, or remove the component (a "removed component" override); the scene test proves it |
| The owner does not want 0.705: 690 passable young firs (40 %) is a gameplay change | One constant, `ForestLayout.PassableYoungFirMaxScale`, read by the profile, the scene rule and the shader. Options of 2.9: 0 (today's behaviour) or 0.60, which needs the margin and test-spacing change recorded there |
| Interaction field band top above 256 m of altitude (the island map) | Half floats step 0.25 m there. Store the band top relative to a reference height carried in the rect. Not needed in this forest (y from -1.8 to 18.1 m: at most 1/64 m steps) |
| `WindShader_GustMatchesCpuOnGpu` exceeds 2e-3 | Already above it at small `t`: the HLSL and the C# formulas differ (constants, wrap, sign), so fix the code. Growing with `t`: float precision, so report the error curve and agree with the coordinator a tolerance derived from it, recorded (rule 4). Never loosen it silently |
| `VariantCompileInfo.ShaderData` is not readable MSL text (for example a binary library) | Knob 2's metric then uses the paired `on - off` per million evaluations alone. The `precise` check then rests on the speckle count of 8.1. Record it |
| A compute kernel reads zero for a wind global | The dispatching C# sets every input on the `ComputeShader` explicitly (3.1); `WindShader_GustMatchesCpuOnGpu` would show zeros at once |
| The field's previous-frame copy is wrong across a window scroll or a pause | The rect travels with each texture; a pause sets previous = current; the motion-vector view during a scroll and a pause shows it |
| Static registries leak across play sessions (no domain reload) | Reset at `SubsystemRegistration`; `OnDisable` clears globals; a test enters play twice |
| Run-time pass toggles in the Editor dirty the material assets | Restored in `finally`; md5 of the materials folder before and after every test run |
| Wind looks uniform, mechanical or boiling | The phase and time-of-maximum maps of `tstd` over 20 s or more (8.2); per-branch phase from A, per-instance period jitter and size scaling for firs, one phase per clump plus the ripple for ferns and grass; tune within the ranges of 2.6, keeping the gust statistics of 2.7, and show the owner the MP4s |
| `PlayerRig_MatchesGreybox` fails with the pusher | Adapted as N1 did for the torch (8.6) |
| Budget_Counts crosses a warning or a budget through the motion vector pass | Warnings recorded; a budget re-based only on paired player evidence |
| Float time precision after hours of play | Flutter steps slightly after about six hours; accepted and noted |
| Edit-mode pose sequences cannot show pushes | Pushes are judged in play mode only (8.3, 8.4) |
| Kit rebuild slower (estimated +1 to 3 s), shader variants lengthen the first player build | Recorded; no action |
| The ALU estimate is wrong by up to 2x | The paired `on - off` measurement replaces it before any knob decision |

---

## 10. Reporting

- `issues/W1-I1.md` and `issues/W1-I2.md`: what was built, every number with its log line, result XML or image, every
  deviation from this spec with its reason, every tuned value with its before and after.
- `STATE.md`: one dated section per package at the end, in the style of N1's (result, what was run, what changed in the
  project, what the next engineer and the owner must know).
- Captures: `captures/w1-pre`, `w1-pre2`, `w1-pre-kit`, `w1-pre-kit2`, `w1-static`, `w1-static-lit`, `w1-kit`,
  `w1-poses`, `w1-seq`, `w1-mv`, `w1-ghost`, `w1-push`, `w1-tests`, `w1-final-static`, `w1-final`. Every image looked at,
  every MP4 made from a sequence the engineer has judged from its contact sheets and maps.
- MSL dumps in `out/W1/msl/` (3.7), with the transcendental and `sample` counts before and after each knob 2 change.
- **For the owner**:
  - one MP4 per class (young fir, tall fir, fern, grass) and per state (night, steady, gusty), the push walk-through
    MP4s, and the `on | off` side by side at fir_close;
  - the contact sheets beside them;
  - the chosen animation distance and moon mode, with their measured costs;
  - W1's measured cost against the cap of 7.1, with his answer on the cap;
  - any stop-and-ask options of 7.4 step 5;
  - the passable threshold to confirm: 0.705 recommended, 0 or 0.60 the alternatives (2.9). Show it with the
    walk-through MP4s, because it changes where the player and, later, the dinosaurs can go;
  - any static difference that needs his sign-off (the triplanar fallback of section 9).

---

## Revision notes

Revision 2 answers the two reviews of the first version (`B/probes/W1/rev/W1-spec.v1.md`). Most of it was written on
2026-10-15. It was completed on 2026-10-16: sections 9 and 10, these notes, and a re-check of every correction against
the 17.6 package source and the project data. Each correction below was confirmed before it was applied. Re-runs and new
evidence are in `B/probes/W1/rev/out/recheck/`: the gust statistics reproduce exactly (`final_check.txt`), as do the
scale counts (`scales.txt`, `threshold.txt`) and the lit-mask shares (`litshare.txt`); `RevApiProbe.cs`, `VerifyW1Editor*.cs` and `mp4probe.swift`
compile again.

**Wrong in the first version, corrected**

| Review item | Now | Where |
|---|---|---|
| "Predisplacement" position gives the rest pose | It is the displaced position without tessellation (`VertMesh.hlsl:171-221`). Rest position and normal now go through two Custom Interpolators | 2.1, 3.2, 3.6 |
| `UnpackNormalmapRGorAG`, "exactly as HDRP/Lit" | The name does not exist (Core has `UnpackNormalMapRGorAG`). Lit uses the surface-gradient path (`LitData.hlsl:8`), so the HLSL resolves a World normal that way | 3.2, 3.3, 3.6 |
| Zero period divides by zero (NaN) | Each term runs under a uniform branch on its amplitude and period, and every period goes through `rcp(max(period, 0.05))`. New test `WindShader_ProfilesGiveFinitePositions` | 3.3, 3.5, 8.6 |
| `MotionVector = User` only on `__mv` | HDRP tags every Shader Graph material (`BaseUnlitAPI.cs:363-392`). The test asserts the tag on all of them and the pass state per variant | 2.3, 5.2, 8.6 |
| Fern graph as the Custom Function model | Fern has none. PseudoSubsurface is the model; HDRPLit's `m_CustomEditorGUI` is not copied | 5.3 |
| Motion-vector debug scale 640 | Motion vectors are NDC deltas: scale = render width / 8 | 8.3 |
| Night values, a steady pulse | Calm, peak and smoothstep shape: 64 % lull and gusts 24 to 68 s apart (median 36 s). The test pins it | 1, 2.7, 3.5, 8.6 |
| Gust frame rotating about the origin | The gust frame and the ripple keep the preset's bearing; only the lean wanders | 2.3, 2.7, 3.5, 8.6 |
| 2 to 3 s sequences, stills for the owner | 20 to 24 s windows, maps over the whole window, MP4s via `AVAssetWriter` | 5.3, 8.2, 10 |
| One period whatever the size | Period scales with size: `period x s^0.5`; young firs 2.2 s at scale 1 | 2.6, 3.5 |
| Ferns and grass boil | One phase per clump (plus or minus 0.12 cycle per frond or blade), a travelling ripple, shelter 0.45 | 2.6, 3.5, 4.3 |
| Flutter 7 mm and knobs that save nothing | Conifer flutter is off. Flutter and push run under uniform branches, and moon mode 1 skips them | 2.6, 2.8, 3.5 |
| Headroom and knob order | W1 cap 0.5 ms (provisional, owner to confirm). I1 stops at 0.3 ms fixed cost. Knob order: invisible changes first; N1's night only with the owner. 7.4, 7.5 and section 9 agree | 1, 5.3, 7.1, 7.4, 9 |
| Pushes from G, stems sheared | Push weight `saturate(gain x G^(2/3))`. 0.55 m branch push, magnitude from the press channel, each side kept. Saplings lean over from the base. Geometric clearance checks added | 2.6, 3.5, 8.4 |
| 0.60 "at a gap" | 0.60 sits in the densest band. 0.705 is recommended (690 firs, 3.7e-3 from any scale). The owner chooses | 1, 2.9, 9, 10 |
| TAA rule against the wind-off control | Each set is compared with itself (off against on). Shipped must be within tolerance of all-on | 8.3, 8.6 |
| Audit misses `__mv` | `ManifestEntryOf` strips `__mv` first; new test `WindMaterials_ResolveToTheManifest` | 5.2, 8.6 |
| Trunk motion-vector and stillness wording | Limited to non-passable trunks, in a push-only run | 8.3, 8.4 |
| LOD pop from flutter | Conifer flutter off, a `dB` guard in `WindData_IsLodInvariant`, and the walk check compares on against off per switch | 2.8, 8.5, 8.6 |
| Section 9 fallback "tall firs flutter only" | Bake UV0 (box projection) for the triplanar triangles, or a recorded difference with the owner's sign-off. Never static tall firs | 9 |

**Unverified in the first version: confirmed, replaced or made a risk**

| Review item | Outcome |
|---|---|
| `_BaseColorMap_ST` | Confirmed and fixed: the property is serialized with `useTilingAndOffset: true`, and a test checks the flag. Only `__ground` has an offset today (material scan) |
| CPU gust to 1e-5 | Replaced by a GPU readback test at 2e-3. Every sine's argument is now wrapped to [-pi, pi), where Metal's fast-math `sin` is specified. 2^-13 per `sin` gives at most 5e-4 in `e` (`recheck_wrap.py`); risk row in 9 |
| `precise` across three passes | The pass order is confirmed (`Prepass.cs:564-586`). Made a check: MSL comparison plus a full-size speckle count, with a quantisation fallback in 9 |
| Static regression strength | Replaced: per-view noise factor (no floor), signed lit-mask bias, and a speckle count on the displaced path |
| Scene NavMesh distances | Replaced by `NavMesh.Raycast` across the stem; negatives among the 994 firs at scale 0.75 or more |
| "120 instructions", "gust once per instance" | Replaced: the gust texture is the mechanism, and the metric is MSL transcendental and `sample` counts plus paired ms. Risk row if the MSL is not text |

**Found while completing the revision**
- A compute file that includes `ForestWind.hlsl` would not compile against the Shader Graph entry point: it is now
  guarded. Compute inputs are set explicitly, because compute shaders may not see `Shader.SetGlobal*` values (3.1).
- The motion-vector coverage test runs in `steady`, because HDRP zeroes moves under about 0.005 px (8.3).
- `flutter30` broke the rule `flutterEnd <= fadeStart`: it is now `flutter21` (7.3).
- The Night wind fields, `GustCentredOffset` and the scratch tools moved to I1, because I1 uses them first (5.2, 5.3, 6.2).
- The I1 gust test applies the peak wander itself (8.6).
- Corrected figures: the longest gust gap (68 s), the tall-fir tilt (up to 0.033 rad), and the custom-interpolator
  warning margin (about 16 channels). The gust texture's bilinear error was measured: at most 7.8e-3 against its 1e-2
  tolerance (`gusttex.txt`).
