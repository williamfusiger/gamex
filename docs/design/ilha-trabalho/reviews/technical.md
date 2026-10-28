# Technical review of the island design (`docs/design/ilha.md`, version 1)

Reviewer: technical director (Unity 6000.6.0f1, HDRP 17.6.0, macOS player on M4 Max). Date: 2026-10-22.
Read: `docs/design/ilha.md` (all), `Logs/island-work/map-spec.json`, `research/open-world.md` sections 2.2 to 2.7, `research/species.md`
section 5, `research/biomes.md` sections 3.5 and 7, and the forest records `build/issues/D2-density.md`, `N1-night.md`,
`W1-runtime.md` (sections 0, 2, 6, 11.2, 12.3, 13.8, 13.9, 14.8, 14.11) and `build/STATE.md`. I also read the runtime foliage code
(`Assets/Game/Scripts/World/Foliage/*.cs`), `RaptorAI.cs`, `Packages/manifest.json`, and the local 6000.6 and HDRP 17.6 documentation.
I did not edit any project file and did not launch Unity.

Marks: [M] measured in this project, [V] verified in a source (linked), [E] my estimate (the reasoning is given), NÃO VERIFICADO =
not confirmed, treat as a risk.

## Verdict

The architecture is right: additive 256 m terrain tiles with 128 m object cells, one NavMesh per agent type that stays loaded,
three creature tiers, and a director in a persistent scene. The numbers and the order of work are optimistic, and as scheduled
the plan will not deliver the demo. Five problems carry most of the risk:

1. **The loaded-set arithmetic uses the wrong density.** The plan's 52 to 70 thousand GameObjects come from the tile average.
   At the density the design asks for in B1 and B2, the figure is 130 to 245 thousand. The demo zone alone holds 90 to 120
   thousand, 4 to 5 times today's scene. That makes the small-props renderer (SIS-5) a demo prerequisite, not an F4 task.
2. **The 8-layer terrain limit is broken by the palettes on 17 to 31 of the 43 land tiles**, including three of the demo's five.
3. **The 10.0 ms environment cap cannot be shown with the levers listed or with the current instrument.** The levers add up to
   at most 1.1 ms, and they are smaller than the session noise. No traversal or hitch measurement exists.
4. **The NavMesh plan ignores how the forest is actually built.** The bake does not see 40 % of the young firs. The voxel sizes
   do not match the gaps the design relies on. Gates in unloaded cells cannot carve.
5. **Big animals and bought animation are under-scoped.** The foliage push was tuned for bodies the size of a raptor. The packs
   have generic rigs with no foot IK, no kill-cam clips and unknown speed matching.

None of these needs a new direction. Each has a concrete fix, listed below, most of which belong in F1.

## What holds up (keep it)

- **One `NavMeshData` per agent type, built from code-known sources and kept loaded.** This avoids NavMesh Links at every tile
  seam.
- **Island-wide `NoiseSystem`**, which is already static [M].
- **Territories plus a director that sends creatures to an area, never to the player's exact position.**
- **Moon shadows at 30 m in the forest** (saves 1.25 to 1.70 ms [M, W1 12.3]).
- **2K textures for creatures except close-up ones.**
- **Deferring caves** and using the service tunnel instead.
- **No bought renderer.**
- **Probes before building.** S1 to S10 are the right instinct. Several of them need different inputs (issues 1, 4 and 6).

## Issues (most severe first)

| # | Severity | Section of ilha.md | Short label |
|---|---|---|---|
| 1 | critical | 7.1, 7.4, 7.3 (SIS-5), 8.2 | Loaded GameObjects underestimated 2.5 to 3.5x; SIS-5 needed for the demo |
| 2 | high | 3.1, 3.10, 7.3 (SIS-3) | 17 to 31 of 43 land tiles need more than 8 terrain layers |
| 3 | high | 7.4, 9 (decision 10) | 10.0 ms cap unreachable with listed levers; the noise exceeds the levers |
| 4 | high | 7.8, 8.1 | No traversal, hitch or shader-compile measurement |
| 5 | high | 3.1, 6.1, 7.5 | The NavMesh does not see 40 % of the young firs |
| 6 | high | 6.1, 7.5 | Agent sizes, voxel sizes and opening widths contradict each other |
| 7 | high | 7.5, 5.5 | Gates and doors in unloaded cells cannot carve |
| 8 | high | 4.7, 4.8, 6.1, 7.6 | Large bodies through foliage: push band, stamps and window too small |
| 9 | high | 4.3 to 4.8, 7.6, 8.4 | Bought animals: animation coverage, foot IK, speed matching, kill-cam |
| 10 | medium | 2.4, 2.3, 7.3 (SIS-6) | Local fog volumes cannot open the view past 55 m |
| 11 | medium | 3.2, 3.5, 3.7, 3.9 | Open-zone costs assume the forest's 30 m mist |
| 12 | medium | 3.5, 3.9, 7.3 | Ground cover at scale (fern carpet, dune grass, lawns) has no system |
| 13 | medium | 3.7, 5.1, 6.3, 2.3 (U1) | Interiors and the 1 km tunnel: sky ambient and global fog leak in |
| 14 | medium | 7.5, 6.8 | Creature tiers: invisible beyond 150 m in open zones; long paths |
| 15 | medium | 7.3, 7.2 | The wind and push runtime is built for one static scene |
| 16 | medium | 7.7, 5.5, 9 (decisions 11, 12) | Saving: fragile IDs, autosave in danger, determinism across machines |
| 17 | medium | 2.5, 8.1 | The demo starts on a beach that has no water |
| 18 | medium | 8.2, 8.3 | F1 effort (5 to 8 agent-days) is about half of what the list needs |
| 19 | low | 7.4, 7.6, 7.8 (S10) | Creature draw calls and passes are not in the budget |
| 20 | low | 7.4, 1 (PS5) | PS5 numbers are inferred from the M4 Max |

---

### 1. Critical: the loaded set is underestimated 2.5 to 3.5x, so SIS-5 is needed for the demo (sections 7.1, 7.4, 7.3 SIS-5, 8.2)

**Problem.**

- **The design contradicts itself.** Section 7.1 gives 12,446 GameObjects per hectare of forest core and about 81,000 per tile
  at forest density. Section 7.4 then budgets "9 to 12 cells, or 52 to 70 thousand GameObjects".
- **Where the 52 to 70 thousand comes from.** `open-world.md` (line 315) used the whole-tile average: 23,274 GameObjects over
  6.55 ha is about 3,550 per hectare. Most of the tile is sparse backdrop.
- **What the design actually asks for.** B1 is meant to be the D2 forest across 48.6 ha, and B2 is as dense. A 128 m cell is
  1.64 ha, so a dense cell holds 1.64 x 12,446 ≈ **20,400 GameObjects**. A loaded set of 9 to 12 cells is **184 to 245
  thousand**, 8 to 10 times today's scene [E, arithmetic on M].
- **The lower bound.** The 12,446 figure charges the whole tile's count to the core. About 70 % of the tile's trees stand in the
  core (529 of 796 tall firs and 1,244 of 1,706 young firs, D2 sections 1 and 6), so the true core density is nearer 9,000 per
  hectare. Even then a dense loaded set is **130 to 175 thousand**, 2.5 to 3.5 times the plan's figure [E].
- **The demo zone.** Z1 has 8.03 ha of B1 and 1.80 ha of B2 (computed from `map-spec.json` regions), which is **90 to 120
  thousand GameObjects**, 4 to 5 times today's 23,274.
- **SIS-5 is scheduled too late.** It is placed "before any area over about 2 tiles (F4)", but the demo covers 5 land tiles:
  T(-3,-1) 6.55 ha, T(-3,-2) 3.10, T(-4,-1) 1.93, T(-2,-2) 1.93 and T(-2,-1) 1.58.
- **Probe S2 would under-test.** It loads "3 x 3 copies of the cells", and a copy of today's tile is mostly sparse.

**Fix.**

- **Move SIS-5 into F1**, before any Z1 art.
- **Rerun S1 and S2 with dense cells.** Cut 128 m cells from the dense core (about 20 thousand GameObjects each) and tile them
  3 x 3.
- **Add an audit cap per cell**, for example 6,000 GameObjects per 128 m cell outside sites [D]. Make a tile build fail above it.
- **Choose the SIS-5 route with a 1-day probe:**
  - **(a) Mesh LOD on ferns, branches and grass.** One renderer per instance instead of a root plus LOD children, so roughly 4
    GameObjects become 1. Unity's manual says Mesh LOD "does not create new GameObjects or components" and supports cross-fade
    only with the GPU Resident Drawer
    ([Mesh LOD](https://docs.unity3d.com/Manual/lod/mesh-lod-introduction.html), local
    `Documentation/en/Manual/lod/mesh-lod-introduction.html`) [V]. The cost: the material cannot change per LOD, so the fern's
    LOD0-only motion-vector material (W1 knob 1) would apply at every level. Measure that.
  - **(b) A custom `BatchRendererGroup` per cell**, fed from `placements.json`, with the cell's colliders kept as GameObjects.
    The GPU Resident Drawer is itself built on BRG (`gpu-resident-drawer.md`, line 3), so the wind Shader Graph already
    compiles DOTS-instancing variants. The per-instance slot would travel as `unity_RendererUserValue`, exactly as W1 11.2 uses
    it. NÃO VERIFICADO: whether a custom BRG can supply that value the same way. Prove it with a capture test like
    `Wind_InstanceTermsMatchTheVertexShader`.
- **Re-size SIS-5 as G** and give it its own line in F1 (issue 18).

### 2. High: 17 to 31 of the 43 land tiles need more than 8 terrain layers (sections 3.1, 3.10, 7.3 SIS-3)

**Problem.**

- **The design's assumption.** It counts on "4-layer cores so a border tile holds two biomes", but most tiles touch three or more
  biomes, and the cores share few layers.
- **What the map shows.** I rasterised `map-spec.json` regions at 8 m and took the union of the section 3.10 palettes per tile
  (script: `Logs/island-work/reviews/tools/tile-layers.py`):
  - counting every biome with at least 400 m² in a tile, B8 included, **31 of 43** land tiles exceed 8 layers (up to 20);
  - counting only biomes with at least 0.2 ha and leaving B8 out, **17 of 43** still exceed.
- **The demo tiles:**
  - T(-3,-2) (B1, B2, B7): 11 layers;
  - T(-2,-2) (B1, B2, B7): 11 layers;
  - T(-2,-1) (B1, B2, B4): 10 layers;
  - T(-3,-1), today's forest: 7 layers with B1 and B4, 10 once the low grotto (B2) is painted.
- **The limit is real.** HDRP Terrain Lit takes "up to eight Terrain Layers" (local HDRP 17.6 `terrain-lit-material.md`,
  line 3) [V].

**Fix**, in this order:

1. **Take B8 off the terrain.** Roads, pads and yards become meshes or decals from the facility generator, which saves 4 layers
   wherever B8 is.
2. **Rebuild the palettes as 4 island-wide shared layers plus 1 or 2 per biome**, so that three biomes fit in 8. For example, the
   shared four could be humus, wet mud, gravel and rock.
3. **If that is not enough, write a terrain Shader Graph.** HDRP 17.6 has a Terrain master stack (`terrain-master-stack-reference.md`).
   It would sample an index map plus a `Texture2DArray` of 16 to 32 layers, with 2 to 4 layers per texel, which ends the
   per-tile limit. The same index map can feed the surface-noise multipliers of section 3.2. Size: M [E].
4. **Add the check "distinct layers per tile ≤ 8" to the island pass in F1**, so the map cannot drift back over the limit.
5. **Re-size SIS-3 from "P to M" to M.**

### 3. High: the 10.0 ms environment cap cannot be reached with the listed levers, or shown with this instrument (sections 7.4, 9 decision 10)

**Problem.**

- **The B1 baseline is an estimate.** "10.7 to 11.8 ms [M]" are paired estimates (N1's 11.51 ms reading plus measured deltas),
  not direct readings. W1 R2 states that "a 29-view confirmation in a normal session is still owed" (W1 14.8). With pushers
  the estimate reaches 12.08 ms.
- **The levers fall short.** The largest pair is a 40 m cull (0.67 ms) plus motion vectors off on young-fir LOD1 (about 0.45 ms),
  which is ≤ 1.12 ms. The 45 m cull is not additive with the 40 m one. From 11.8 ms that does not reach 10.0.
- **The instrument cannot resolve the levers:**
  - the same build drifts 1 to 1.5 ms between sessions (D2 section 2);
  - the paired delta at pano_90 scattered from -0.70 to +0.48 ms between rounds (W1 14.8);
  - every lever is inside that noise.
- **"Environment" leaves out environment costs.** Not measured and not reserved:
  - transparent FX in fog: the steam plume L3, the fumaroles, the burning wreck L7, the fuel-depot fire;
  - lamp lights with volumetrics;
  - Local Volumetric Fog volumes;
  - water;
  - interiors.
- **The 2.5 ms reserve is unmeasured.** Its split (creatures 1.0, streaming 0.5, reserve 1.0) is [E].

**Fix.**

- **Add render scale as the main lever.** HDRP 17.6 runs dynamic resolution on Metal with TAA Upscale or STP. STP needs a fixed
  render scale without hardware support (local `Dynamic-Resolution.md`, lines 10 and 115 to 116) [V], and the forest already
  uses TAA (D2 section 2).
  - Measure render scale 0.85 and 0.75 with TAAU and STP against today's TAA at 2560 x 1440, paired, 29 views.
  - Volumetrics, lighting and post scale with pixels. The vertex-bound wind and shadow passes do not. Expect a partial saving
    [E]; NÃO VERIFICADO how much.
- **Add a calibration view to every session.** Today's forest pano_90 is run at the start and end of every session, and a zone
  is judged as a paired delta against it. The design rules should say this explicitly.
- **Give "environment" a fixed definition**: terrain, vegetation, buildings, lights, fog volumes, FX and water.
- **Change decision 10.** Accept B1 at 11.0 to 11.5 ms and lower the director's cap to 1 materialised predator in the densest B1
  cells. Or keep D2 density in stealth pockets only (≤ 30 % of B1 area [D]) and use a cheaper B1 elsewhere, which also eases
  issue 1.

### 4. High: there is no traversal, hitch or shader-compile measurement (sections 7.8, 8.1)

**Problem.**

- **Every frame figure comes from a parked camera.** The figures are 16-frame medians over 29 static views. D2 section 9 lists
  "walking (LOD pops with cross-fade off)" as not measured, and STATE.md line 1190 lists "dinosaurs among 2,500 trunks" as
  not tested.
- **The open world adds costs a 16-frame median hides:**
  - streaming integration;
  - first-use pipeline-state compiles on Metal for every new material (creatures, buildings, biome kits);
  - garbage collection;
  - AI path spikes.
- **The 4 ms loading slice does not cover everything.** `backgroundLoadingPriority` caps the main-thread integration of
  textures, meshes and audio (local `ScriptReference/Application-backgroundLoadingPriority.html`) [V]. The doc does not say that
  object activation is sliced: Awake and OnEnable of 20 thousand components, GPU Resident Drawer registration, and the wind
  registry (issue 15). NÃO VERIFICADO; plan as if it all lands in one frame.
- **S1 tests object cells only.** It does not cover a terrain tile scene: a 46.9 MB `TerrainData`, a 1025² `TerrainCollider` and
  two 2048² alphamaps.

**Fix.**

- **Add probe S11, "traversal".** Run a scripted route over the demo path at walk speed and at a 6 m/s sprint, with streaming, AI
  and audio on and vsync on at 60 Hz.
  - Report p99, the counts of frames over 16.7, 25 and 50 ms, and the maximum.
  - Demo exit criterion: 0 frames over 50 ms, and at most 1 frame per minute over 25 ms [D].
- **Extend S1** to a full terrain tile scene and to a dense cell (issue 1), and time the activation frame on its own.
- **Prewarm pipeline states with `GraphicsStateCollection`.** It is in 6000.6 and "generally replaces the
  ShaderVariantCollection class for prewarming" (local `ScriptReference/Rendering.GraphicsStateCollection.html`,
  [web](https://docs.unity3d.com/ScriptReference/Rendering.GraphicsStateCollection.html)) [V]. Trace it over the S11 route.

### 5. High: the NavMesh does not see 40 % of the young firs, so the refuge rule can leak (sections 3.1, 6.1, 7.5)

**Problem.**

- **The design's rule.** "Young firs stand 2.2 to 3.2 m apart; a 2 m-radius agent does not pass, a raptor does" (3.1 and 6.1).
- **What the bake actually sees.** The W1 thin-stem rule disabled the trunk capsules of **690 of 1,706 young firs (40 %)**: every
  fir of scale ≤ 0.705, which includes all of the open-ground understorey. The NavMesh bake "collects enabled physics colliders
  only" (W1 section 2) [M].
- **Consequence.** The 2.2 to 3.2 m spacing counts stems that no NavMesh sees. With 60 % of the young stems left, solid spacing
  is about 1/√0.6 ≈ 1.3 times wider [E]. The refuges and the "the map tells the player where the T. rex does not follow" rule
  are untested against the real G and GG meshes.

**Fix.**

- **Build each agent type's NavMesh from its own source list.** The design already plans `BuildNavMeshData` from code-known
  sources. Include passable stems as obstacles for G and GG (big animals avoid thickets) and leave them out for M and P.
- **Add audits:**
  - the GG area reachable inside the B1 refuges and the caldera fir islands is 0;
  - the share of B1 core reachable by G is reported, since the Dilophosaurus ambush points must lie on G;
  - `Scene_PassableStemsFollowTheRule`'s `NavMesh.Raycast` test runs once per agent type.

### 6. High: agent sizes, voxel sizes and opening widths contradict each other (sections 6.1, 7.5)

**Problem.**

- **P (Compsognathus, radius 0.25 m) "passes any gap ≥ 0.3 m".** It cannot: the agent is 0.5 m wide, so it needs about 0.6 m
  with voxel margin. Its 0.5 m height under containers on 0.6 to 0.9 m stilts is marginal.
- **M (radius 0.5 m) "passes openings ≥ 1.2 m".** Erosion by 0.5 m on each side leaves a 0.2 m strip, about 1 voxel at the default
  r/3 = 0.167 m, so connection is unreliable [E].
- **GG (radius 2.0 m) at the default r/3 = 0.67 m voxels.** The ±0.67 m quantisation makes the "4 m gap" rule fuzzy. A 5 m forest
  corridor (no collider within 2.5 m of the centre line, forest spec 3.3) erodes to a 1.0 m strip of 1.5 voxels and may break
  [E].
- **Build time and size grow fast at small voxels.** Unity's manual says to aim for 4 to 6 voxels per agent diameter and that
  "doubling the resolution will result in an approximate quadrupling of the build time" (local
  `ScriptReference/AI.NavMeshBuildSettings-voxelSize.html`,
  [web](https://docs.unity3d.com/ScriptReference/AI.NavMeshBuildSettings-voxelSize.html)) [V]. P at 0.083 m therefore costs
  about 4 times M per area.
- **The memory figure is measured for one type only.** "1.9 MB per tile per type" was measured for M.

**Fix.**

- **Set explicit voxel overrides:** P 0.10 m, M 0.167 m, G 0.25 m, GG 0.33 m [D].
- **Derive the design rules from the bake:**
  - M-passable openings ≥ 1.5 m;
  - M-blocking openings ≤ 0.9 m;
  - GG lanes ≥ 6 m clear.
- **Make P squeeze points `NavMeshLink`s that the site generator writes**: vents, under-container gaps and broken windows. Do not
  rely on voxel gaps.
- **Check every rule per type with `NavMesh.Raycast` in the audit**, including "no path into any shelter for any type", because
  vents lead into the labs.
- **Make S4 report per type** the bake time, the size per tile and the Editor's peak memory. S4 covers 2 x 2 tiles; the island has
  43 land tiles, and all their `TerrainData` must be loaded to bake.

### 7. High: gates and doors in unloaded cells cannot carve (sections 7.5, 5.5)

**Problem.**

- **The design's plan.** Gates, fences and doors that change in play use `NavMeshObstacle` carving or an area cost.
- **Carving needs the obstacle to exist.** It exists only while its cell is loaded.
- **Simulated creatures path across unloaded cells.** At 150 to 600 m they follow `CalculatePath` corners through unloaded
  cells, so they cross closed gates, or stop at opened ones, according to the baked state.
- **Area costs are per area type, not per gate.** Unity has 32 area types.

**Fix.**

- **Bake every gameplay opening closed:** gate, door, fence breach, the East Pass breach and shelter doors.
- **Represent each opening as a NavMesh link owned by `Island.unity`.** Use `NavMeshLink` components there, or links added with
  `NavMesh.AddLink`, enabled or disabled by the world-state store (issue 16), so they work with any cell loaded.
- **Keep carving only for physics props near the player.**

### 8. High: large bodies through dense foliage (sections 4.7, 4.8, 6.1, 7.6)

**Problem.** The push system was tuned and measured with the player and raptor-sized proxies only.

**Current limits** [M, code]:

- **Push band 0.3 to 2.2 m** (`ForestWindInstances.BandBottom/BandTop`, `ForestWindInstances.cs:42`).
- **16 stamps at most** (`FoliageInteractionField.MaxStamps`, line 26).
- **Field window 51.2 m around the camera** (256 texels of 0.2 m, lines 60 to 61).
- **Worst measured load.** "Three raptors" press 135 to 251 instances and cost +0.20 to +1.06 ms (W1 13.8, 13.9, 14.8).
- **An open defect.** Crossing branches still pass through bodies (W1 14.11).

**What breaks with the island's animals** [E]:

- **Crowns above 2.2 m do not react.** A T. rex torso (2 to 4 m up) passes through young-fir crowns.
- **The stamps run out.** Player + 3 raptors + 6 Parasaurolophus + a 10-compy pack is 20 stamps, against 16.
- **Nothing bends beyond 25.6 m from the camera.** In B3 at 80 to 150 m visibility, a T. rex wading through 1.2 to 2 m ferns
  leaves them still.
- **A cylinder is not a 12 m animal.** A `NavMeshAgent` is a cylinder, so the head and tail of a 12 m T. rex or a 9 m Triceratops
  swing through trunks on 5 m corridors with bends: the forest's game trails turn at control points 14 to 37 m apart (forest spec
  3.3).
- **Pressed-instance cost for GG bodies has not been measured.**

**Fix.**

- **Add a "large body" probe:** a pusher of 2 m radius and 12 m length, band to 4.5 m, through a thicket edge and the fern
  hollow. Measure pressed instances and ms with the existing paired harness.
- **Budget stamps by priority:** the player and the nearest large bodies first; a compy pack shares 1 or 2 stamps.
- **For open zones, add a second coarse field** (for example 128 x 128 texels of 1 m) for large bodies only.
- **Give GG agents their own locomotion:** steering with a minimum turning radius taken from the root-motion turn clips, and lanes
  with a curve radius ≥ 15 m [D].
- **Keep the T. rex out of B1 except on marked lanes ≥ 6 m.**
- **Pushers on creatures.** `FoliagePusher` looks for a `CharacterController` in its parents (`FoliagePusher.cs:92`), and creatures
  use a `NavMeshAgent`, so they need the `Configure()` path that the harness proxy uses.

### 9. High: bought animals will import, but animation coverage and fit are unverified (sections 4.3 to 4.8, 7.6, 8.4)

**What is low risk.**

- **Meshes and materials.** Standard maps to an HDRP Lit mask map by script, like `ForestMaterials.cs`, plus the LOD groups and
  the scale checks.
- **Built-in only, as the design notes.** The Deinonychus page declares Built-in only and was tested on 2021.3.16f1
  ([Asset Store 182587](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/deinonychus-182587), read
  2026-10-22) [V].

**What is under-scoped.**

1. **No foot IK on generic rigs.**
   - Unity's built-in IK is Humanoid-only
     ([Animancer docs](https://kybernetik.com.au/animancer-v7-4/docs/examples/jobs/two-bone-ik)) [V].
   - The Animation Rigging package is not in `Packages/manifest.json`. Its Two Bone IK has a known offset bug on Generic rigs
     whose avatar is "Create From This Model", reproduced on 6000.0 with 1.3.0
     ([Unity issue tracker](https://issuetracker.unity.com/issues/9969/animation-rigging-two-bone-ik-gets-offset-when-a-generic-rig-avatar-is-created-from-an-existing-model))
     [V].
   - The forest core reaches 25.8° of slope [M]. Without IK and body pitch, the feet of 2 to 9 t animals float or sink in the
     torch beam.
2. **Speed matching.** The design speeds are raptor 9 m/s, T. rex 7 m/s and Dilophosaurus 6.5 or 10 m/s; the clips' root speeds
   are unknown. Driving in-place clips with a `NavMeshAgent` at other speeds makes the feet slide. Reviewers already report foot
   problems on the Pxltiger T. rex (species.md).
3. **No first-person kill or catch clips.** No pack has a capture or kill synchronised with a camera. Each predator's death
   sequence must be built from bite clips plus a camera script.
4. **Behaviour clips the design needs and the packs may not have:**
   - sniffing or tracking (T. rex scent, raptors);
   - the Dilophosaurus crest display: only roars and 9 additive head poses exist;
   - compy nip, harass and scatter: the DANKIE list is not published (NÃO VERIFICADO);
   - Triceratops against raptors: needs paired alignment.
   Generic rigs cannot share clips across species, and there is no animator, so a missing clip is expensive.
5. **Feather construction is unknown.** Shells or alpha cards (species.md: [U]) would add cost to the torch's shadow map.

**Fix.**

- **Make a coverage matrix before buying:** every AI state of section 6, times species, times clip name.
- **Buy the Deinonychus and the compy first ($44.99)** and run a 2 to 3 agent-day import probe:
  - the material script;
  - a root-speed table extracted by script for every locomotion clip;
  - foot IK and body pitch on a 25° slope (Animation Rigging with "No Avatar", or custom IK jobs);
  - a kill-cam prototype;
  - S10.
- **Budget 4 to 8 agent-days per predator species, not 2 to 5** [E].

### 10. Medium: local fog volumes cannot open the view past 55 m (sections 2.4, 2.3, 7.3 SIS-6)

**Problem.**

- **The design's plan.** Per-zone fog by Local Volumetric Fog blended over 20 m (2.4, SIS-6), with "a local volume that opens the
  mist" at the demo tower (8.1).
- **Local fog lives only inside the froxel volume.** HDRP voxelises Local Volumetric Fog into the froxel grid (local
  `create-a-local-fog-effect.md`, line 7), which ends at Volumetric Fog Distance (`fog-volume-override-reference.md`, line 29).
  That distance is 55 m in the night preset [M]. Beyond it the global fog sets visibility.
- **Removing fog locally works, but only inside that range.** HDRP 17.6 has Overwrite and Min blend modes that can thin fog
  locally (`local-volumetric-fog-volume-reference.md`, line 14) [V].
- **Consequence.** Plateau views of 80 to 150 m, and the tower's first view of the island, need the global fog to change.

**Fix.**

- **Respecify SIS-6:** the global Fog override (mean free path and height) per zone, through Volumes blended by camera position
  over 20 to 40 m, with local volumes only for detail (pooling in ravines, steam).
- **Extend S8** to look across a zone border in both directions, from the forest into the plateau and back. With a camera-blended
  global fog, one of the two directions will look wrong.

### 11. Medium: open-zone costs assume the forest's 30 m mist (sections 3.2, 3.5, 3.7, 3.9)

**Problem.**

- **The estimates rest on the forest's trick.** B3 at 8.5 to 11 ms, B5 at 7 to 9 and B7 at 7.5 to 9.5 assume 30 m mist, culls at
  55 m and moon shadows at 30 m.
- **With 80 to 150 m of visibility, three things change:**
  - **The fern carpet must draw past 55 m**, or it visibly ends. At 70 % cover, D2's ratio (2,300 patches on 1.87 ha at 26.4 %
    cover) gives about 3,260 patches per hectare. A 70° cone to 120 m then holds about 2,900 patches, against about 600 to
    55 m [E].
  - **Moon shadows that end at 30 m will show**: shadowless araucarias at 40 m on a moonlit savanna.
  - **Impostors (SIS-4)** for the far trees.

**Fix.**

- **Make moon shadow distance per zone**, through the same camera-blended Volume as issue 10: 50 to 80 m with 3 cascades in open
  zones, where there are far fewer casters. Measure it.
- **Paint the terrain colour under the fern carpet to match**, so a 60 to 80 m cull dissolves into the ground.
- **Give ferns a far LOD of cards or an impostor.**
- **Measure a 1 to 2 ha B3 slice at 120 m visibility before buying the fern kit.**

### 12. Medium: ground cover at scale has no system (sections 3.5, 3.9, 7.3)

**Problem.**

- **Which areas.** The B3 fern carpet (29.4 ha), B7 dune grass (27.6 ha) and B8 lawns are ground cover at scale.
- **The forest's own grass is tiny.** It is 120 to 205 GameObject clumps in total.
- **The design does not plan it.** It names "grass at scale" for B7 but has no SIS line, no budget and no chosen method.

**Fix.** Run a 1 to 2 day probe on two routes and choose before Z2:

- **Unity terrain details in instanced mode, with the wind graph material.** Details ship inside `TerrainData`, so they need no
  GameObjects and stream with the tiles.
  - Slot-less renderers compute their own wind terms (W1 11.2).
  - The field-based push might also work: NÃO VERIFICADO.
  - Details have no LODs, so they need light meshes.
- **The SIS-5 BRG path** (issue 1).

### 13. Medium: interiors and the 1 km tunnel will glow and fill with mist (sections 3.7, 5.1, 6.3, 2.3 U1)

**Problem.**

- **No baked indirect light exists.** The forest has no Adaptive Probe Volumes. HDRP lights everything from the dynamic sky
  ambient probe (forest spec 8.5: `"apv": "none"`) [M].
- **What leaks into interiors.** The labs (2 floors), bunker, hangar, 16 to 24 houses, the turbine hall, the 0.97 km tunnel and the
  lava tube would get unoccluded sky ambient, which auto-exposure (up to 10 EV) would brighten. The global height fog would also
  fill them.
- **The tunnel does not fit 2D streaming.** It crosses about 4 tiles under the terrain, and XZ-distance streaming loads the surface
  above it, not the tunnel.
- **The effort table has no line for interior lighting, fog or reverb.**

**Fix.**

- **Generate a local Volume with every building interior:** indirect lighting multipliers near 0, a fog override and exposure
  limits.
- **Or decide on APV baking sets per site in a probe.** HDRP 17.6 supports APV disk and GPU streaming (local
  `probevolumes-streaming.md`, line 12) [V]. The forest deferred APV because its batch bake was NÃO VERIFICADO (forest spec,
  line 40).
- **Stream the tunnel as its own sections keyed by portals.**
- **Add 3 to 6 agent-days** to Z4 and Z6 in F5 [E].
- **Keep caves deferred** (agreed).

### 14. Medium: creatures are invisible beyond 150 m in open zones, and long paths may come back partial (sections 7.5, 6.8)

**Problem.**

- **The simulated tier has nothing to draw.** At 150 to 600 m it has no visuals. In Z5 (80 to 150 m visibility), from the 18 m
  towers, the summit and the demo tower (the PARA-2 herd seen from afar), animals past 150 m should be visible.
- **Paths are synchronous.** `CalculatePath` blocks the main thread.
- **Long paths may be cut short.** Long, complex paths can come back partial when the search tracks too many polygons. This is a
  forum report from Unity 5.4
  ([Unity Discussions](https://discussions.unity.com/t/is-there-a-cap-on-navmesh-calculatepath/177160)); NÃO VERIFICADO in
  6000.6.

**Fix.**

- **Add a "visual" tier**: LOD2 or LOD3 skinned meshes with the Animator at a low rate, no AI, out to each zone's visibility.
- **Promote by distance and line of sight together.**
- **Force-materialise staged reveals.**
- **Move simulated creatures on a precomputed waypoint graph along trails and territories**, and use the NavMesh only for legs of
  150 to 200 m or less.

### 15. Medium: the wind and push runtime is built for one static scene (sections 7.3, 7.2)

**Problem** [M, code]:

- **Registration is a one-shot scan.** `ForestWindInstances.Register()` runs `FindObjectsByType<MeshRenderer>(Include)` once, when
  the field is enabled, and drops any earlier registration (`ForestWindInstances.cs:100-107`; `FoliageInteractionField.cs:179`).
- **Streamed renderers lose the tuned push.** A renderer loaded later gets no slot, so it falls back to I2's push box. The push
  state of saplings and solid firs that R2 tuned lives in the slots.
- **Re-registering per cell costs a full scan.** Calling `Register()` on every cell load means a full scan plus a reset of every
  slot's state.
- **The density map is single-scene.** `FoliageInteraction.Density` is one static map baked for one plan (460 x 500 cells).
- **The kernel cost grows with the loaded set.** The Instances kernel evaluates every slot every frame: 16,099 slots today, about
  10 times that in a dense 3 x 3 set [E].

**Fix, in F1:**

- **Allocate slots per cell**, freeing them on unload, with state kept per slot.
- **Make one island-wide density map at 0.5 m**: 3,500 x 2,570 cells x 2 layers x 1 byte ≈ 18 MB, or one map per tile.
- **Dispatch the Instances kernel only over slots within the 30 m wind distance**, from a compacted list.

### 16. Medium: saving has fragile IDs, can autosave in danger, and assumes determinism across machines (sections 7.7, 5.5, 9 decisions 11 and 12)

**Problem.**

- **(a) Index-based IDs break on regeneration.** IDs "from the generator (tile, cell, index)" break whenever a tile is regenerated
  with any change of kit hash, tunable or code version. That will happen weekly during F2 to F6, and later in patches.
- **(b) The objective autosave can fire mid-danger.** It runs at the moment of completion: O2 next to DL-03, O7 with pack B
  around, O13. Loading that save restarts the player in danger.
- **(c) The world-state store is not specified.** The design lists what is saved, but not that stateful objects in unloaded cells
  read a live store when their cell loads.
- **(d) Determinism is proven on one machine only.** Placements are byte-identical on this machine. Decision 12's "regenerate on
  every machine" assumes the same holds across machines and Unity versions: NÃO VERIFICADO.

**Fix.**

- **Persist authored objects only.** Only site objects from the island data carry saved IDs (GUIDs). Scatter is never saved.
- **Defer the objective autosave** until the director is Quiet and no creature is in Investigate or Chase within 40 m, with the
  last shelter save as the fallback.
- **Add a `WorldState` registry in `Island.unity`** that every stateful prefab queries on Awake and that also drives the NavMesh
  links of issue 7.
- **Pin generated output.** Keep a per-tile hash manifest in git, and ship only from the build machine's output.

### 17. Medium: the demo starts on a beach with no water (sections 2.5, 8.1)

**Problem.** The player "wakes on the cove's beach" at S01, with the burning boat on the rocks. Yet the design says "the demo has
no water surface" and "the sea appears only as darkness below the cliff". On the beach the sea is in frame at 0 to 20 m for the
first 3 minutes.

**Fix.** Choose one:

- **Start on the cliff top**, with the boat burning below; or
- **Build a minimal cove sea.** One plane with a Shader Graph: normals, a shoreline foam band from the island's coast distance
  field, and the fire's glow. About 1 to 3 agent-days [E], measured in the harness.

### 18. Medium: F1 effort is about half of what its list needs (sections 8.2, 8.3)

**Problem.**

- **F1's list is long.** For 5 to 8 agent-days it covers: a new contract, the island data asset, splitting a frozen single-tile
  generator into island and tile passes, seams, stamps, `Island.unity`, `WorldStreamer`, a NavMesh per type, the save skeleton,
  audits and 8 probes.
- **The forest's own pace was slower than that suggests:**
  - W1 alone took five rounds (I2, I2b, I2c, R1, R2) from 15 to 18 October;
  - D2 and N1 took about a day each.
- **This review adds work:**
  - SIS-5 moved into F1;
  - incremental wind slots;
  - the terrain-layer solution;
  - per-type NavMesh sources and voxel sizes;
  - the traversal probe;
  - creature integration at 4 to 8 days per predator.

**Fix.**

- **Re-estimate:** F1 at 12 to 20 agent-days, and the total at about **100 to 170** rather than 76 to 130 [E].
- **Or take streaming out of the demo.** If SIS-5 brings Z1 under a measured GameObject and memory cap, load Z1's five tiles whole
  behind a loading screen, and build streaming in F4. The demo still sits on the final layout.

### 19. Low: creature draw calls and passes are not in the budget (sections 7.4, 7.6, 7.8 S10)

**Problem.** The GPU Resident Drawer takes only Mesh Renderers (`gpu-resident-drawer.md`) [V], so skinned creatures draw the
classic way. Each submesh is drawn in every pass: depth, GBuffer, motion vectors, 2 moon cascades, the torch and any lamps. That
is about 7 draws.

- 20 creatures x about 4 submeshes ≈ 500 to 600 draws, as many as the whole forest's drawer commands [E].
- The cost is on the CPU render thread, not the GPU. "138 thousand triangles" does not capture it.

**Fix.** Make S10 also report draw calls, SetPass calls and render-thread time.

### 20. Low: PS5 numbers are inferred from the M4 Max (sections 7.4, 1)

**Problem.** Every figure is from an M4 Max at native 2560 x 1440. How the alpha-tested foliage cost transfers to PS5 is
NÃO VERIFICADO.

**Fix.** Do not promise parity. Plan a PS5 preset from day one: render scale with upscaling (consoles have hardware dynamic
resolution), 2K textures, and D2 density only in stealth pockets. Re-measure only once a dev kit is available.

---

## Sources

**Project records.**

- Forest issue notes:
  - `Logs/forest-work/build/issues/D2-density.md` sections 1, 2, 7 and 9;
  - `N1-night.md` sections 4 and 7;
  - `W1-runtime.md` sections 0, 2, 6, 11.2, 12.3, 13.8, 13.9, 14.8 and 14.11.
- Forest build log and spec: `build/STATE.md` line 1190; `Logs/forest-work/spec.md` lines 40, 158 and 837.
- Island research: `Logs/island-work/research/open-world.md` lines 143 to 190, 300 to 396 and 402 to 418;
  `research/species.md` section 5; `research/biomes.md` sections 3.5 and 7.
- Map spec regions: `map-spec.json`.
- Code:
  - `Assets/Game/Scripts/World/Foliage/ForestWindInstances.cs` (lines 15, 42 and 100 to 107);
  - `FoliageInteractionField.cs` (lines 26, 59 to 61 and 169 to 179);
  - `FoliageDensityMap.cs`;
  - `FoliagePusher.cs` (line 92);
  - `Assets/Game/Scripts/Dinosaurs/RaptorAI.cs` (line 188);
  - `Packages/manifest.json`.
- Layer count script: `Logs/island-work/reviews/tools/tile-layers.py`.

**Unity documentation, local 6000.6.0f1 and HDRP 17.6.0** (`Library/PackageCache/com.unity.render-pipelines.high-definition@03bdf33c5697/Documentation~`):

- HDRP: `terrain-lit-material.md`, `terrain-master-stack-reference.md`, `local-volumetric-fog-volume-reference.md`,
  `create-a-local-fog-effect.md`, `fog-volume-override-reference.md`, `Dynamic-Resolution.md`, `gpu-resident-drawer.md` and
  `probevolumes-streaming.md`.
- Script Reference: `Application-backgroundLoadingPriority`, `AI.NavMeshBuildSettings-voxelSize`,
  `AI.NavMesh-pathfindingIterationsPerFrame` and `Rendering.GraphicsStateCollection`.
- Manual: `lod/mesh-lod-introduction`.
- Web equivalents:
  - [backgroundLoadingPriority](https://docs.unity3d.com/ScriptReference/Application-backgroundLoadingPriority.html)
  - [voxelSize](https://docs.unity3d.com/ScriptReference/AI.NavMeshBuildSettings-voxelSize.html)
  - [GraphicsStateCollection](https://docs.unity3d.com/ScriptReference/Rendering.GraphicsStateCollection.html)
  - [Mesh LOD](https://docs.unity3d.com/Manual/lod/mesh-lod-introduction.html)

**Web.**

- [Animancer: two-bone IK, built-in IK is Humanoid-only](https://kybernetik.com.au/animancer-v7-4/docs/examples/jobs/two-bone-ik)
- [Unity issue: Animation Rigging Two Bone IK offset on Generic rigs](https://issuetracker.unity.com/issues/9969/animation-rigging-two-bone-ik-gets-offset-when-a-generic-rig-avatar-is-created-from-an-existing-model)
- [Unity Discussions: cap on NavMesh.CalculatePath](https://discussions.unity.com/t/is-there-a-cap-on-navmesh-calculatepath/177160)
- [Asset Store: Pxltiger Deinonychus](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/deinonychus-182587)
