# Research: building the whole island as an open world (technical and as a horror game)

Written 2026-10-21 for the island design (`docs/design/ilha.md`). Project: `/Users/william/Documents/GitHub/gamex`, Unity 6000.6.0f1,
HDRP 17.6.0, AI Navigation 2.0.12, MacBook Pro M4 Max.

**Status.** Research only. Nothing here was run in Unity. Numbers come from three kinds of source, marked as follows:

| Mark | Meaning |
|---|---|
| **MEASURED** | Read from the forest records in `Logs/forest-work` (spec, `build/STATE.md`, `build/issues/*.md`) or from project files on disk |
| **DOC** | Read in Unity 6000.6's local manual and scripting reference (`/Applications/Unity/Hub/Editor/6000.6.0f1/Documentation/en`) or in package documentation shipped with the project |
| **WEB** | A web source, linked in the text and in the list at the end |
| **ESTIMATE** | My arithmetic or judgement from the above. Each one has a probe in section 2.9 or a playtest that settles it |
| **UNVERIFIED** | A claim I could not check. Treat it as a guess |

Process note: I ran one read-only `git log` to date the forest's commits, although the brief says no git commands. Nothing was changed.

---

## 0. The answer in brief

1. **Size.** Build a playable island of **1.2 to 1.5 km²** (120 to 150 ha). That is 65 to 80 times the current forest core (1.87 ha). It sits
   inside a land-and-backdrop footprint of about 1.8 x 1.5 km (about 2.7 km², about 40 terrain tiles of 256 m). Split it into **7 zones and
   16 sites**. The demo is **one zone of 15 to 25 ha** (4 to 6 tiles). Do not go past about 2 km² playable. The night (mist with a 30 m
   mean free path, everything culled at 50 to 55 m) makes a small island feel large. Area is cheap for a code generator. Sites, species
   and objectives are what cost (section 1).
2. **Streaming.** Unity 6000.6 has no built-in world partition, so the island is built as follows:
   - one persistent scene;
   - 256 m terrain tiles as additive scenes, loaded within about 200 m;
   - 128 m object cells as additive scenes, loaded within about 150 m.

   The forest generator grows in two passes. An island pass computes coarse fields at 1 m and places every large object island-wide. A
   tile pass adds the fine relief, the paint and the small cover. Tiles share world-space fields, so their seams match exactly. Frame cost
   does not grow with the island, because everything past about 55 m is culled anyway. Memory grows with the number of biome kits, not
   with area (section 2).
3. **NavMesh.** Do not give each tile its own NavMesh surface. Unity cannot connect overlapping separate NavMeshes; they need links (DOC).
   Instead, the generator builds **one NavMesh per agent type for the whole island** from the build sources it already knows: the terrain
   data plus collider primitives taken from the placement plans. That NavMesh stays loaded all the time, so off-screen dinosaurs can still
   find paths. Use 4 agent types. Bake each one only over its species' territory (section 2.6).
4. **Horror pacing.** Use a two-brain AI:
   - a **director** that knows where the player is and runs a tension gauge, with build-up, peak, withdraw and relax phases;
   - creature AIs that have to find the player with their own senses (the Alien: Isolation design, WEB).

   Around it: species territories, 5 to 6 safe outposts, one night that advances with objectives in 4 acts, and objectives placed so the
   short route crosses danger (section 3).
5. **Objectives.** Use **11 main objectives** (about 175 to 245 minutes) and **8 optional ones** (20 to 60 minutes), which gives 3.2 to
   5.1 hours. Three threads can be played in any order after the first outpost. The gates are tools, keycards, power, codes and the
   territories themselves. The game ends with the rescue call from the summit relay and a dawn extraction by helicopter. A boat ending is
   not recommended: it needs water gameplay (section 4).
6. **Plan.** Work in this order:
   - the technical spine first: island data, multi-tile generator, streaming, NavMesh, saving, 8 probes. 5 to 8 agent-days;
   - then the demo zone in the real island layout: the landing coast and fir valley, with compsognathus and one raptor. 14 to 24
     agent-days;
   - then the whole island in greybox with all 11 objectives, so the pacing is proved before art. 8 to 14 days;
   - then the zone art passes. 38 to 62 days.

   In total that is roughly **75 to 130 agent-days** at the forest's pace (section 5; ESTIMATE).

---

## 1. Size: how big an island supports 3 to 5 hours of stealth with objectives

### 1.1 What comparable games did

Map sizes for these games are almost never official. The figures below are fan or press estimates and disagree with each other, so read
them as orders of magnitude (WEB, UNVERIFIED unless stated).

| Game | Structure | Area | Length (main) | What it teaches here |
|---|---|---|---|---|
| The Forest (2018) | Open survival-horror island; caves; cannibals | Estimates range from about 1 to 4 km² ([vcgamers](https://www.vcgamers.com/news/ukuran-map-sons-of-the-forest/), [wepc](https://www.wepc.com/gaming/is-sons-of-the-forest-the-same-island-map-size-location/)); UNVERIFIED | About 16 h main story ([isthereanydeal / HowLongToBeat](https://isthereanydeal.com/game/the-forest/history/)) | Threat escalates with days survived: cannibal aggression rises each day ([wiki talk page](https://theforest.fandom.com/wiki/Talk:Cannibals); [Wikipedia](https://en.wikipedia.org/wiki/The_Forest_(video_game))) |
| Sons of the Forest (2024) | Same, larger | "About four times" The Forest, Endnight via GamesRadar+ ([PCGamesN](https://www.pcgamesn.com/sons-of-the-forest/map), [GamesRadar+ guide](https://gamesradar.com/sons-of-the-forest-guide)); absolute figures quoted from 9 to 20 km², UNVERIFIED | Longer than The Forest; no reliable figure found | A survival sandbox with crafting and base building fills its area with resources. A stealth game without crafting cannot fill area that way |
| Green Hell | Open jungle survival with a story | No area figure found ([Steam thread](https://steamcommunity.com/app/815370/discussions/0/3338745825895176297)) | Not found | (no number) |
| Subnautica | Ocean crater; land is two small islands plus wrecks | Crater about 2 x 2 km within a 4 x 4 km world (forum estimates, [Steam](https://steamcommunity.com/app/264710/discussions/0/1693785669872919984), [PC Gamer map](https://pcgamer.com/subnautica-map)); UNVERIFIED | Not checked | Its land areas are tiny. Its lessons are gating by danger and depth, and a boundary enforced by predators: the Crater Edge's leviathans turn players back ([wiki](https://subnautica.fandom.com/wiki/Ghost_Leviathan_Juvenile)) |
| Alien: Isolation | Sevastopol station: interconnected areas, revisited, with tool-gated access | Not an area game | 12 to 18 h campaign ([Game Developer](https://www.gamedeveloper.com/design/the-perfect-organism-the-ai-of-alien-isolation)) | One stalker plus a director carries a long game. Tools open earlier areas ([MobyGames](https://www.mobygames.com/game/68903/)). Reviewers also criticise the late backtracking (same page) |
| Amnesia: The Bunker (2023) | Semi-open bunker; one ever-present stalker; generator fuel keeps the safe lights on; randomised item placement ([Wikipedia](https://en.wikipedia.org/wiki/Amnesia:_The_Bunker), [Epic feature](https://store.epicgames.com/en-US/news/amnesia-the-bunker-is-a-chilling-underground-obstacle-course-of-horror)) | Small (one bunker); no figure | About 5.5 h ([gg.deals](https://gg.deals/game/amnesia-the-bunker/)), 6 to 8 h in reviews ([Pro Game Guides](https://progameguides.com/amnesia/how-long-does-it-take-to-beat-amnesia-the-bunker/)) | **The closest match to the brief's length and loop.** A small, dense, semi-open space holds 5 to 6 hours of stalker horror |
| Far Cry 3 / Far Cry 5 | Open world with vehicles, outposts and many activities | About 21 km² / about 30 km², fan figures ([expertbeacon](https://expertbeacon.com/which-far-cry-has-the-biggest-map/), [cyberpost](https://cyberpost.co/how-big-is-far-cry-5-world/)); UNVERIFIED | 20 h and more | That density per km² assumes vehicles at 10 to 30 m/s and combat activities. Our player moves at about 1 m/s and can only hide. It does not transfer |

**Conclusion from the comparisons.** Horror games with a stalker are long because of density and revisits, not area. The Bunker and
Sevastopol are small. Survival sandboxes are large because crafting and resources fill the space. Our game has no crafting, so the island
should be sized like a stalker game that happens to be outdoors: about 1 to 2 km², not 10 to 30 km².

### 1.2 Sizing from this game's own numbers

| Quantity | Value | Source |
|---|---|---|
| Walk, crouch, sprint | 3.2, 1.6, 6.0 m/s | MEASURED, `Assets/Game/Scripts/Player/PlayerController.cs:21-23` |
| Footstep noise radius (crouch, walk, sprint) | 2, 8, 18 m | same file, lines 31-33 |
| Raptor's view distance | 28 m (crouched player: 0.6 x) | `RaptorAI.cs:30-33` |
| Torch reach | Grey card readable at 20 to 25 m, fades at 30 m | MEASURED, `issues/N1-night.md` section 5 |
| Night mist | Mean free path 30 m: 19 % of a silhouette's contrast left at 50 m; every class culled at 50 to 55 m | MEASURED, `ForestLightingPresets.cs:217`, `issues/D2-density.md` section 3 |
| Sight in the dense forest | From the trail, 17 % of eye-level rays are clear beyond 30 m; from random core points, 8 % | MEASURED, D2 section 1 |

So the player perceives a disc of about **30 m radius** at any moment. A valley 60 m wide feels as enclosing as one 600 m wide. That is
the main reason a small island is enough.

**Distance budget (ESTIMATE; measure the average speed in the demo playtests).**

- Ground speed averaged over a stealth session, including waiting, hiding and reading: 0.6 to 1.0 m/s. A 4-hour game at 0.8 m/s covers
  about 11.5 km. 3 to 5 hours covers 6.5 to 18 km.
- In an open world with a hub, each stretch of route is walked about 2 to 2.5 times (out, back, revisits). That leaves **4 to 7 km of
  distinct routes**.
- An open world needs route choice: a parallel route or loop for each main route, 150 to 300 m apart. The area is then about route length
  x spacing: 5 km x 0.25 km ≈ **1.25 km²** (range 0.8 to 2.0 km²).
- The ground the player actually sees up close is a band about 60 m wide along the routes: 5 km x 60 m ≈ 30 ha, plus about 16 sites of
  1 to 3 ha. The rest of the island is the ground between routes. It gives freedom of route, and the generator fills it for free.

### 1.3 Recommendation

| | Demo (20 to 30 min) | Full game (3 to 5 h) |
|---|---|---|
| Playable area | 15 to 25 ha (8 to 13 times today's core) | 120 to 150 ha (ceiling about 200 ha) |
| Footprint with coast and backdrop | 4 to 6 terrain tiles of 256 m (26 to 39 ha) | About 1.8 x 1.5 km, about 40 tiles (land tiles plus coast) |
| Zones (biome plus resident threat) | 1 | 7 |
| Sites (labs, outposts, set pieces) | 2 to 3 | 16 |
| Main objectives | 3 | 11 (+ 8 optional) |
| Distinct routes | 1.0 to 1.5 km | 4 to 7 km |
| Safe outposts | 1 | 5 to 6 |

The 7 zones are the frame used in sections 3 and 4. Species and vegetation choices belong to the other research notes, so check this
table against them:

| Zone | Ground and look | Resident threat | Sites |
|---|---|---|---|
| Z1 Landing coast and fir valley (**demo**) | Fir and fern forest (the existing kit), coastal cliffs, a small beach | Compsognathus packs; a raptor scout late in the demo | Beach wreck (start), ranger outpost (safe 1), boathouse and fuel shed |
| Z2 Worker village | Scrub, grass, buildings | Compsognathus; raptors at night from act 2 | Staff quarters, maintenance depot, canteen (safe 2) |
| Z3 Fern gullies and bog | Dense ferns, dead trees, mud, no open water | Dilophosaurus (solitary ambusher) | Pump station, observation hide |
| Z4 Ridge and power plant | Rock, scree, steam vents | Raptor patrols; Dilophosaurus at the edge | Plant control (safe 3), substation |
| Z5 Meadows and paddocks | Grassland, open views, longer mist | Herds (Gallimimus, Parasaurolophus, Triceratops); T. rex at the edges from act 2 | Paddock control, airstrip hangar (safe 4) |
| Z6 Research compound | Interiors and fenced grounds | Raptor pack (the main antagonist) | Main lab, genetics wing, security office (safe 5) |
| Z7 Summit | Bare rock, wind, a caldera or ruins below | T. rex's core territory below; a sauropod landmark on the far slope | Relay tower, helipad (extraction) |

Why not bigger:

- Each extra hectare of route needs hiding spots every 10 to 20 m (the raptor sees 28 m), cover, a reason to be there, and audio.
- The player has no vehicle. At 0.8 m/s a 1 km detour costs about 20 minutes, which is a large share of a 3 to 5 hour game.
- Horror needs encounters often. Section 3 asks the director to act after at most 4 to 6 minutes of quiet. A larger island means more empty
  ground for it to cover.

### 1.4 What is realistic for a solo developer with code-generated environments

| Cost driver | What the forest shows | How it scales on the island |
|---|---|---|
| Area of an existing biome | One 256 m tile: 5,825 instances, 23,274 GameObjects, scene build 20 s, BuildAll 71 s (MEASURED, D2 section 7) | Almost free: compute and disk only, about 20 to 40 min for a full island rebuild (ESTIMATE) |
| A new biome kit (assets in, LODs, materials, scatter rules, review) | About 1.5 to 2 days from research to version 1 (M0 to M4), with three owner reviews and 1.5 GB of downloads (MEASURED: research files 2026-10-13 16:30, version 1 on 2026-10-15 04:00) | Paid once per biome. 4 to 5 biomes; some reuse the fir kit |
| Polish features (density, night, wind) | Density 0.3 day, night 0.2 day, wind with interaction 2.5 days (two review rounds) (MEASURED, STATE.md dates) | Mostly global. Wind and night already work everywhere |
| Sites (labs, outposts, fences) | None built yet | Hand-planned layouts from a bought modular kit, assembled by code from a site description: 1 to 3 days each (ESTIMATE) |
| Species (model, animation, AI, sound, tuning) | One raptor in the greybox | 2 to 5 days each (ESTIMATE); the largest unknown |

---

## 2. Streaming in Unity 6000.6 HDRP

### 2.1 What the engine gives (and does not give)

| Fact | Source |
|---|---|
| No first-party world-streaming system. The manual's "3D World Building" feature set is ProBuilder, Terrain Tools and the FBX Exporter. Open worlds are built from additive scenes or Addressables, often with third-party tools | DOC `Manual/WorldBuildingFeature.html`; WEB [Unity Discussions](https://discussions.unity.com/t/in-an-open-world-scenario-what-is-better-to-do-everything-in-a-single-scene-or-split/925938) |
| `SceneManager.LoadSceneAsync` reads and deserialises on a background thread and integrates on the main thread. Main-thread integration per frame is capped by `Application.backgroundLoadingPriority`: Low 2 ms, BelowNormal 4 ms (the default), Normal 10 ms, High 50 ms. It has no effect in the Editor | DOC `ScriptReference/Application-backgroundLoadingPriority.html` |
| `Awake` and `OnEnable` of loaded objects still run on the main thread when a scene activates (forum, old) | WEB [Unity Discussions](https://discussions.unity.com/t/prevent-loadleveladditiveasync-from-halting-the-app/120033); UNVERIFIED for 6000.6 |
| Textures and meshes go up through an async upload ring buffer (2 to 2,047 MB) with a time slice in ms per frame | DOC `Manual/configure-asynchronous-upload-pipeline.html` |
| Terrain tiles connect for LOD seams with `Terrain.SetNeighbors`, or automatically with `allowAutoConnect` and a shared `groupingID` | DOC `ScriptReference/Terrain.SetNeighbors.html`, `Manual/terrain-CreateNeighborTerrains.html` |
| **Mipmap streaming does not support terrain textures**: Unity keeps their top mip loaded at all times | DOC `Manual/TextureStreaming-introduction.html` |
| Baked occlusion culling loads one data asset at a time; additive scenes must be baked together | DOC `Manual/occlusion-culling-scene-loading.html`. Not usable for tiles. GPU occlusion culling culled 0 instances through the foliage (MEASURED, D1) |
| Additive light-probe scenes need `LightProbes.TetrahedralizeAsync` | DOC `Manual/light-probes-and-scene-loading.html` |
| Adaptive Probe Volumes can stream cells from disk and to the GPU | DOC HDRP `Documentation~/probevolumes-streaming.md` |
| The GPU Resident Drawer takes any MeshRenderer and "updates with the changes" when GameObjects are created | DOC HDRP `Documentation~/gpu-resident-drawer.md`. The cost of registering a whole tile's renderers in one frame is UNVERIFIED (probe S1) |
| Content directories (6000.6) replace AssetBundles for local content builds and work with `LoadableSceneId` | DOC `Manual/content-directories-introduction.html`. Not needed now; maybe for PS5 packaging later |

### 2.2 The layout I recommend

```
Island.unity (persistent, always loaded)
  Player, Game (session, objectives, save), Director, Population (all creatures' abstract state)
  Lighting/ night preset (one global Volume, moon, sky), zone fog Volumes (local, blended)
  Navigation/ one NavMeshData per agent type, island-wide (section 2.6)
  Backdrop/ landmark silhouettes and lights (relay tower beacon, lab floodlights, sauropod), a low-res island mesh for open zones
  WorldStreamer
Tiles/T_<x>_<z>.unity              256 m terrain tile: Terrain + TerrainCollider, zone bounds
Cells/C_<x>_<z>.unity              128 m object cell: the 4 x 4 forest cells of 32 m it holds, colliders, interactables
Sites/<site>.unity                 hand-planned sites (labs, outposts), loaded with their cell
```

| Setting | Starting value | Reason |
|---|---|---|
| Terrain tile | 256 m, heightmap 1025 (0.25 m), alphamap 2048 on routes and 1024 on backdrop tiles | The forest's proven settings (spec 3.1, 6.4). 1024 saves 24 MiB per backdrop tile (2.4) |
| Terrain load radius | Tile within 200 m of the player; unload past 260 m | 3 x 3 tiles at most |
| Object cell | 128 m (the forest's 32 m cells grouped 4 x 4) | The forest already parents every instance under `Cell_<cx>_<cz>` "so cells can become additive scenes later" (spec 3.7) |
| Cell load radius | 150 m; unload past 190 m | Cull 55 m + sprint 6 m/s x about 8 s of load-to-visible margin (ESTIMATE; probe S1 measures the real load time) + hysteresis |
| Loads at once | 1, nearest first, biased toward the direction of travel | Keeps integration inside one 4 ms slice |
| `backgroundLoadingPriority` | BelowNormal (4 ms) | The frame is GPU-bound. The player's main thread took 1.2 to 2.8 ms a frame in D2 (MEASURED, D2 section 2), so up to 4 ms of integration may hide under the GPU's 10 to 12 ms. UNVERIFIED (probe S1) |
| Coordinates | Island centred on the origin | At 1 km from the origin float32 resolves 0.06 mm (2^-14 m); at 2 km, 0.12 mm. No floating origin needed |
| Persistent `NoiseSystem`, director, population | In `Island.unity` | `NoiseSystem` is already a global static list (`Core/NoiseSystem.cs`), so it works across scenes |
| Foliage interaction field | Unchanged | It follows the player in a 51.2 m window (256 texels of 0.2 m) with up to 16 bodies (`FoliageInteractionField.cs`, W1 section 1.1), independent of tiles. Each cell brings its own CPU density map (0.9 MB per tile today) |

### 2.3 How the single-tile forest generator grows to many tiles

**What is single-tile today (MEASURED, code and spec):**

- `ForestLayout` is a static class of constants: `TileSize = 256`, `HalfExtentX/Z = 65/75`, `CornerRadius = 30`, `SpawnXZ`, hand-listed
  `Landmarks` and `Viewpoints`, three trail splines, `Seed = 1993` (`ForestLayout.cs:21-70`).
- `ForestFields` holds one 1025 x 1025 height field and the masks for that tile.
- `ForestScatter.Plan` plans one tile with one occupancy grid. The bank, walls and boundary are the edge of the world.
- The frozen contract `ForestContracts.cs` gives `ForestPlacement` the fields `cellX, cellZ` (range 0..7) and no tile index or stable ID
  (spec 11.2). Its rules say nobody edits it.
- `ForestSceneBuilder.Build` writes one scene, one `Terrain.asset`, one `NavMesh.asset` and one `placements.json` (stage order, spec
  11.5).

**What to build (ESTIMATE of shape; each step keeps the forest scene buildable, as the forest milestones did):**

1. **A new contract instead of editing the frozen one.** `IslandContracts.cs` adds three things:
   - `IslandPlacement`: the forest fields plus `tileX, tileZ` and a stable `id` (tile index plus placement index), so saves and the audit
     can address an instance;
   - `IslandTile`;
   - `IslandPaths`.

   The forest code keeps compiling; its classes are refactored to take a tile context instead of reading statics.
2. **An island data asset as the single source of truth** (`Assets/Game/World/IslandData/`, JSON or a ScriptableObject written by code):
   - coastline as a signed distance field;
   - macro height recipe: ridges, the summit, valleys, the caldera;
   - zone map: a zone id per 4 m cell;
   - routes and game-trail splines in world space;
   - site footprints (reserved areas, as the forest reserves landmarks);
   - species territories;
   - safe outposts.
3. **Island pass (coarse, island-wide, once per build).** Fields at 1 m: a 1,800 x 1,500 grid is 2.7 M floats, 10.8 MB per field. They are
   macro heights, zone weights, distance to routes, coast and sites, and slope. All large objects are placed island-wide in this one pass,
   so Poisson spacing never breaks at a seam: tall and young trees, rocks, logs, stumps. They are the objects that have colliders and
   matter for the corridors.
4. **Tile pass (fine, per tile, in parallel or in any order).** For each tile:
   - sample the same world-space functions at 0.25 m with a 16 m halo: micro relief, root mounds, carve and paint;
   - scatter the small classes (ferns, grass, branches) with a seed hashed from (island seed, tile x, tile z, class), excluding against
     the island pass's large objects.

   Small, collider-free classes may overlap slightly at a seam, which is invisible.
5. **Exact seams.** Both tiles evaluate the height function at the same world coordinates on the shared edge, so the edge samples are
   bit-identical. Then call `SetNeighbors`, or use `allowAutoConnect` with one `groupingID`. Assert in the audit that every shared edge has
   a mismatch of 0.
6. **Zone blending.** The forest's per-class density fields (`ForestLayout` tunables, the thicket and fern fields) become per-zone tables,
   blended by zone weight. The terrain's 8 layers per tile are chosen per tile from the zones it touches; HDRP's TerrainLit allows 8
   (spec 6.3).
7. **Per-tile stamps** (the `ForestStamps` pattern): a tile rebuilds only when the hash of its inputs changes. Those inputs are the island
   data inside its bounds plus halo, the kit hash and the code version.
8. **Audit and tests per tile, plus seam and island tests:**
   - the existing grounding, corridor, slope and spawn rules per tile;
   - seam heights equal;
   - no large object missing or duplicated at a seam;
   - `NavMesh.CalculatePath` complete between every pair of linked sites;
   - every site reachable by its required agent types;
   - the full set of objective gates satisfiable in order (a graph test).
9. **Outputs per tile:**
   - `Tiles/T_x_z.unity` with `Terrain.asset`;
   - the cells' scenes;
   - `placements.json`;
   - the density map;
   - a report.

**Disk (MEASURED for one tile, ESTIMATE for the island).** Today one tile writes:

| File | Size |
|---|---|
| `Terrain.asset` | 46.9 MB |
| `Forest.unity` | 20 MB |
| `placements.json` | 3.9 MB |
| `NavMesh.asset` | 1.9 MB |
| `FoliageDensity.asset` | 0.9 MB |
| **Total** | **about 74 MB** |

`ForestKit` adds 531 MB, shared. Over about 40 tiles, with backdrop tiles at about 20 MB, the island comes to about **2 to 3 GB
generated**. That is above GitHub's limits, which the forest already exceeds (three files over 100 MB; STATE.md, verify-M4). Decide early
to regenerate it on each machine and keep it out of git (owner decision 7).

### 2.4 Budgets per tile, with the measured forest as the reference

**Frame (MEASURED).** The final forest, at night with the torch on, wind and foliage interaction, paired estimate at the worst view
(pano_90) against the 12.5 ms player budget (macOS development player, 2,560 x 1,440):

| Measurement | Worst view | Source |
|---|---|---|
| I2c, 29-view confirmation | 11.06 to 11.20 ms | STATE.md |
| R1 | 10.42 to 10.60 ms | STATE.md |
| R2 | 10.74 to 11.78 ms; at most 12.08 ms with pushers | STATE.md |

The margin is 0.7 to 2.0 ms, and no dinosaur, AI, audio or UI is in it yet. The player's frame times drift by 1 to 1.5 ms between
sessions, so A/B runs must be paired (D2 section 2).

Cost depends on what is inside about 55 m of the camera, not on the size of the island. Loaded geometry past the cull is not drawn, and
the GPU Resident Drawer culls instances on the GPU. The cost of having 2.5 to 3 times today's instance count loaded is UNVERIFIED (probe
S2).

**Rule for every zone (owner decision 4):** the environment alone, at night with the torch on, must read **≤ 10.0 ms at its worst view**.
That leaves 2.5 ms:

| Use | Allowance |
|---|---|
| Creatures on the GPU (skinning, their shadows into the moon and torch maps) | About 1.0 ms |
| Streaming jitter | About 0.5 ms |
| Reserve | About 1.0 ms |

These are ESTIMATES to be replaced by the demo's measurements. The dense fir forest misses this rule by 0.4 to 1.8 ms. These levers have
been measured:

| Lever | Saves | Cost |
|---|---|---|
| Night cull 55 to 45 m | 0.22 ms | Visible on the far treeline (W1 I2b) |
| Night cull 55 to 40 m | 0.67 ms | Visible on the far treeline (W1 I2b) |
| Motion vectors off on young-fir LOD1 | About 0.45 ms | Was the owner's I2b choice, reversed in R1 |
| Fewer simultaneous creatures in the densest zone | (not measured) | — |

Alternatively, make D2 density the ceiling of one zone only and keep the other zones cheaper.

**Counts and memory per tile (MEASURED where marked, the rest ESTIMATE).**

| Item | One forest tile today | Island implication |
|---|---|---|
| Instances / GameObjects / renderers | 5,825 / 23,274 / 17,138 (MEASURED, D2) | About 890 instances and 3,550 GameObjects per hectare of tile. Loaded set within 150 m: 9 to 12 cells of 1.64 ha ≈ 13 to 17 k instances, 52 to 70 k GameObjects (2.5 to 3 times today's scene) |
| Triangles at the worst view | 17 to 21 M (MEASURED, I2c and R1 counters) | Same: set by the cull, not the island |
| Texture memory above an empty scene | 930 MiB (MEASURED, verify-M4): the shared kit plus one uncompressed sky | **Per biome kit, not per tile.** 4 to 5 kits at 0.4 to 0.9 GiB each cannot all stay resident on a PS5 (about 12.5 GB for games, WEB [Notebookcheck / Digital Foundry](https://notebookcheck.com/Sony-PlayStation-5-Pro-soll-Qualitaet-aelterer-Spiele-verbessern-und-mehr-RAM-fuer-Spiele-bieten-laut-Digital-Foundry.815212.0.html)). Turn on mipmap streaming for kit textures, which the spec planned from the start; that is fine on the Mac's unified memory |
| Mesh memory | 358 to 382 MiB (MEASURED) | Shared kit, per biome |
| Terrain GPU memory per tile | Two control maps of 2048² RGBA8 = 32 MiB, heightmap 1025² R16 = 2 MiB, base map about 4 MiB: **about 40 MiB** (ESTIMATE) | Terrain textures cannot mip-stream (DOC), so each loaded tile costs all of it. 9 tiles ≈ 360 MiB. Backdrop tiles at 1024: about 16 MiB each |
| NavMesh, one agent type, r = 0.5 m | 1.9 MB on disk for the tile (MEASURED, binary) | Section 2.6 |
| Scene load | 2.1 s to load the forest scene in Editor play mode (MEASURED, D2 section 4) | A 128 m cell holds about a quarter of that. Probe S1 measures it in the player |

**A first total to hold the design to (ESTIMATE):** environment resident memory ≤ 4 GiB. That is two biome kits (about 2.5 GiB with
streaming caps), 9 terrain tiles (0.36 GiB), the loaded cells and the NavMesh (under 0.3 GiB). It is well inside the M4 Max and close to
what a PS5 port could afford next to creatures and audio.

### 2.5 What the fog and the night allow

- **A short view.** Mist with a 30 m mean free path and culls at 50 to 55 m (MEASURED). Nothing past about 60 m needs geometry. That is
  why the streaming radii in 2.2 are small and the frame cost is flat.
- **No global illumination bake outdoors.** The forest never baked probes; its night look is the sky at 0.1 lux, the moon at 0.3 lux, the
  torch and local lights (N1 section 5). This saves the APV work the forest spec deferred (M5) for the whole outdoor island. Interiors
  (labs) can use APV in a baking set, or reflection probes and local lights only. Decide this per site.
- **Moon shadows end at 30 m** with 2 cascades (MEASURED, W1 I2c: saved 1.25 to 1.70 ms). That works island-wide.
- **Open zones need more view.** Meadows, the summit and the coast are where the player sees landmarks, and where the player is seen.
  Give them local fog volumes with a longer mean free path (80 to 150 m, ESTIMATE), blended over about 20 m at the zone edge. Include the
  backdrop silhouettes there. HDRP's volumetric grid has a fixed resolution (240 x 135 x 64 at 1080p by default), so a longer volumetric
  distance costs quality rather than time (DOC HDRP `create-a-global-fog-effect.md`); quality is UNVERIFIED for this scene.
- **Orientation in the dark comes from lights and sounds, not views:**
  - the relay tower's red beacon and the labs' floodlights through the mist (volumetric lights);
  - the plant's hum, surf at the coast, a siren;
  - outpost map boards that fill in as the player explores, as in The Bunker (WEB, Wikipedia).
- **The sea.** No aquatic species and no swimming, so the player should never enter deep water. Close most of the coast with cliffs. On
  the few beaches, block the player knee-deep. Use one cheap ocean surface seen only from the coast. HDRP's Water System cost was not
  measured: keep it out of the frame at the worst view (UNVERIFIED).

### 2.6 NavMesh across tiles for several species

**The trap.** "You can't overlap separate NavMeshes to create a link between them"; a NavMesh Link is needed (DOC,
`com.unity.ai.navigation/Documentation~/NavMeshLink.md`). Per-tile surfaces would need links along every seam, for every agent type,
through dense vegetation. And creatures in unloaded tiles would have no NavMesh at all.

**Recommendation.**

1. **Build one `NavMeshData` per agent type for the whole island in the editor.** Use `NavMeshBuilder.BuildNavMeshData` with
   `NavMeshBuildSource`s of shape Terrain (from each tile's `TerrainData`) and Capsule or Box (from the plans' collider primitives: trunks,
   rocks, logs, site walls). The API is DOC: `ScriptReference/AI.NavMeshBuildSourceShape.html`. The generator already knows every
   collider, because the corridor rule and the audit compute clearance from the plan (spec 7.1). So the bake does not need the tile scenes
   open.
2. **Keep the result loaded in `Island.unity`.** Add it with `NavMesh.AddNavMeshData`. When one tile changes, rebuild only that part:
   `NavMeshBuilder.UpdateNavMeshDataAsync` hashes the sources and rebuilds only the changed parts (DOC).
3. **Four agent types.** Their sizes are ESTIMATES; match them to the species research:

| Agent type | Radius / height | Species | Where baked |
|---|---|---|---|
| Small | 0.25 / 0.5 m | Compsognathus | Z1, Z2, outskirts of Z3 and Z6 |
| Medium | 0.5 / 1.8 m (today's only type, `ProjectSettings/NavMeshAreas.asset`) | Raptors, Gallimimus | Most of the island |
| Large | 1.0 / 2.5 m | Dilophosaurus | Z3, Z4, edges of Z1 and Z2 |
| Huge | 2.0 / 5 m | Triceratops, Parasaurolophus, T. rex | Z5, Z7, the corridors between them (the forest's 2 m corridor rule, spec 3.3, was made for this) |

   The sauropod walks a scripted spline and needs no NavMesh.
4. **Memory.** At 1.9 MB per tile per type (MEASURED for the medium type), 40 tiles x 4 types would be about 300 MB. Baking each type only
   over its territories and routes brings that to about **80 to 150 MB** (ESTIMATE; probe S4). If it is still too large, load NavMesh
   per region within 600 m and let distant creatures move on the territory graph only.
5. **Three creature tiers, so off-screen creatures still exist:**

| Tier | Distance | What runs |
|---|---|---|
| Materialised | Within the loaded cells (≤ 150 m) | The full AI: senses, `NavMeshAgent`, animation. Sight needs the cells' colliders, because `RaptorAI` uses physics linecasts (`RaptorAI.cs:36`) |
| Simulated | 150 to 600 m | No GameObject animation. Moves along `NavMesh.CalculatePath` corners at 2 to 4 Hz; hears global noise events |
| Abstract | Beyond 600 m | A node on the territory graph plus a timer |

   The director promotes and demotes creatures between tiers.
6. **Carving.** Doors, fences and breakable gates that change during play use `NavMeshObstacle` carving or area costs (a "fence-powered"
   area type), not rebuilds.

### 2.7 Saving

| Question | Answer |
|---|---|
| What is saved | The player (position, rotation, health or state, inventory), objective flags, world switches (power per sector, doors, gates, fences, keycards used), pickups taken (by stable ID), documents read, the night clock and the act, the director state, every creature's tier, position, territory and behaviour unlocks, and the outposts unlocked |
| What is not saved | All generated scenery; it is rebuilt identically from the seed, as the forest's determinism tests already prove (`placements.json` byte-identical across builds, STATE.md). Thrown objects reset |
| IDs | Every interactable gets a stable ID from the generator (tile, cell, index) or, for sites, a GUID stored in the site description. Never use instance IDs |
| Format | JSON with `JsonUtility` (already used across the forest code) in `Application.persistentDataPath`, with a `version` field and a migration step per version. A few tens of KB |
| When | At save terminals in safe outposts. Saving takes 3 to 5 s and is refused while any creature is in Investigate or Chase within 40 m. Also a checkpoint autosave at each main objective. No free saving: free saving removes the tension (owner decision 5) |
| Load | Load `Island.unity`, apply the save, stream the cells around the saved position before giving control (a fade, no visible pop) |

### 2.8 Risks

| Risk | What happens | Detected by | Fallback |
|---|---|---|---|
| Activating a cell of 6 to 15 k GameObjects spikes the main thread | A visible hitch at a cell edge | S1 | Smaller cells (64 m); split activation over frames by keeping cell roots inactive and enabling children in batches; fewer GameObjects per instance (today 4 per instance: root plus LOD children) |
| Terrain tiles' memory (no mip streaming) | Memory grows with the load radius | S5 | 1024 alphamaps outside routes; 200 m radius; four layers on backdrop tiles |
| Island NavMesh too large or too slow to build | Long builds, memory | S4 | Regional NavMesh within 600 m plus the territory graph |
| Biome kits exceed memory | Textures stream poorly at zone borders | S5 | At most two biomes meet at any border; kit streaming caps |
| Seams show (lighting, LOD, paint) | Lines in the ground | S3, captures | Shared edge samples (exact), `SetNeighbors`, paint fields in world space |
| Frame margin consumed by creatures | Over 12.5 ms in the densest zone | Demo measurements | The levers in 2.4; a cap on materialised creatures per zone |

### 2.9 Probes to run first (each in the macOS development player, paired as D2 and N1 learned)

| # | Question | How | Pass |
|---|---|---|---|
| S1 | What does loading one cell cost? | Split today's tile into four 128 m cell scenes; stand in one, load another additively at BelowNormal; log every frame | No frame over 20 ms; cell visible within 3 s |
| S2 | What does the loaded set cost? | 3 x 3 copies of the tile's cells loaded against 1 x 1, same view | ≤ 0.5 ms worse at the worst view |
| S3 | Do seams match? | Two adjacent generated tiles from world-space fields | Edge mismatch 0; no crack in captures across the seam |
| S4 | Can the island NavMesh be built from sources? | 2 x 2 tiles, 4 agent types, `BuildNavMeshData` from sources | Build under 5 min; size per type per tile in the table; `CalculatePath` complete across seams |
| S5 | How much memory does it take? | `Texture`, `Mesh`, `Total Used Memory` counters with 9 tiles and 12 cells loaded | ≤ 4 GiB environment |
| S6 | Does a save survive streaming? | Change switches, save, quit, load elsewhere | Identical state; no orphaned IDs |
| S7 | How much CPU does off-screen AI take? | 20 abstract, 6 simulated, 3 materialised creatures | Main thread ≤ 4 ms (the GPU-bound frame hides it) |
| S8 | Does a zone fog transition hold? | Forest (30 m) to meadow (120 m) over 20 m | Exposure stable; no froxel shimmer in a walk capture |

---

## 3. Horror pacing in an open world

### 3.1 What goes wrong when the player can go anywhere

1. The player avoids the threat by routing around it, so scripted scares at fixed places are missed or seen out of order.
2. The player lingers in safe or cleared ground, and tension drains.
3. Backtracking through the same ground without anything new kills dread. It is the criticism of Sevastopol's second half (WEB,
   MobyGames).
4. The player reaches the climax area early.
5. Safe zones become a crutch. Reviewers say this of RE2's safe rooms against Mr. X (WEB, search summary of reviews).
6. In the dark the player gets lost, and lost turns into bored, not scared.

### 3.2 What the reference games do

| Tool | How it works there | Source |
|---|---|---|
| **Two brains** | A director that always knows where the player and the creature are. It sends the creature toward the player's *general area*, never the exact spot, and the creature AI must find the player with its own senses. The director keeps a **menace gauge** fed by: the creature within short walking distance, the player seeing it, and the motion tracker showing it close and able to reach the player quickly. At its peak, or after too long near peak, the creature is pulled "backstage" into the vents. Difficulty is tuned by time backstage, menace duration and the patrol radius, not by damage. The creature's behaviour tree has over 100 nodes; some branches unlock over the campaign (never from player deaths), so it seems to learn | WEB [Game Developer: The Perfect Organism](https://www.gamedeveloper.com/design/the-perfect-organism-the-ai-of-alien-isolation), [Game Developer: Revisiting the AI of Alien: Isolation](https://www.gamedeveloper.com/design/revisiting-the-ai-of-alien-isolation) |
| **Dramatic pacing** | The Left 4 Dead director estimates each player's "stress" and alternates pressure with relief. Valve added breaks because constant action burned players out | WEB [Mike Booth's GDC 2009 slides](https://steamcdn-a.akamaihd.net/apps/valve/2009/ai_systems_of_l4d_mike_booth.pdf) (its text could not be extracted here; the phase names build-up, sustain peak, peak fade, relax are from memory of the talk, UNVERIFIED), [Shacknews](https://www.shacknews.com/article/69960/devs-discuss-left-4-dead-in-retrospect), [Dread Central](https://dreadcentral.com/news/1833/tons-of-left-4-dead-details) |
| **A roaming stalker heard before it is seen** | RE2's Mr. X roams the station and tracks the player across rooms; his footsteps do much of the work | WEB [PC Gamer interview with director Kazunori Kadoi](https://www.pcgamer.com/uk/resident-evil-2s-director-talks-mr-xs-ai-scary-footsteps-and-the-dmx-mod/) |
| **Territories as soft gates and boundaries** | Subnautica's Crater Edge spawns leviathans that turn players back, and more appear if they stay | WEB [Subnautica wiki](https://subnautica.fandom.com/wiki/Ghost_Leviathan_Juvenile) (via search; UNVERIFIED details) |
| **Light as safety** | Dying Light's safe zones are protected by UV light the night hunters will not cross; at night the open countryside is hard to hide in | WEB [Dying Light wiki: Safe Zone](https://dyinglight.fandom.com/wiki/Safe_Zone), [Volatile](https://dyinglight.fandom.com/wiki/Volatile) |
| **Light as a resource** | The Bunker's safe lights run on one fuel-hungry generator; when fuel runs out the base goes dark and the Beast hunts. Safe rooms save and show a map that fills in. Item placement is randomised | WEB [Wikipedia](https://en.wikipedia.org/wiki/Amnesia:_The_Bunker), [Epic feature](https://store.epicgames.com/en-US/news/amnesia-the-bunker-is-a-chilling-underground-obstacle-course-of-horror) |
| **Explore, then hold out** | Darkwood: scavenge by day, get back to a hideout and survive the night; how far you roam decides how much time you have to get back | WEB [Bloody Disgusting review](https://bloody-disgusting.com/reviews/3563151/review-survival-game-darkwood-stands-getting-lost-deep-forest/) |
| **Escalation over time** | The Forest raises cannibal aggression with every day survived | WEB [Wikipedia](https://en.wikipedia.org/wiki/The_Forest_(video_game)), [wiki talk page](https://theforest.fandom.com/wiki/Talk:Cannibals) |
| **Landmarks that pull** | Breath of the Wild's "triangle rule": hills hide what is behind them and give a choice of over or around. Towers are points seen from afar | WEB [GMTK](https://gmtk.substack.com/p/how-nintendo-solved-zeldas-open-world), [Gigazine CEDEC 2017 summary](https://gigazine.net/gsc_news/en/20171004-breath-of-the-wild-design-secret) |

### 3.3 A design for this game (starting values: ESTIMATES, tuned in playtests)

**The director (macro brain), ticking at 4 Hz in `Island.unity`.**

- **Inputs:**
  - each materialised predator's distance, state and awareness (`RaptorAI.State`, `Awareness` 0..1, already public: `RaptorAI.cs:84-89`);
  - whether the player can see or hear it (line of sight inside the torch cone, footsteps within 25 m);
  - the player: hidden in a `HidingSpot`, torch on (`PlayerFlashlight.IsOn` and `IsOnChanged` exist, N1 section 2), noise radius;
  - time since the last chase.
- **Tension T (0..1), modelled on the menace gauge:**
  - rises +0.15 per second when a predator is within 15 m;
  - +0.05 per second at 15 to 40 m;
  - +0.10 per second when the player sees or hears it;
  - +0.30 per second while any predator is in Chase;
  - decays 0.03 per second otherwise.
- **States and rules:**

| State | Rule |
|---|---|
| Quiet | T < 0.2. After Q seconds of quiet the director picks the nearest suitable creature (simulated or abstract tier, allowed in this zone by its territory) and gives it a job: "investigate a point 40 to 60 m from the player, upwind". Q per act: 240 to 360 s, 180 to 300 s, 90 to 180 s, 60 s |
| Build-up | The creature approaches on its own senses. Ambient events from a pool play on a cooldown of 45 to 90 s: a distant roar, compys chittering in ferns, herbivore alarm calls, a power flicker, a carcass |
| Peak | T > 0.8 or a chase. Held at most P seconds (P = 60 to 90 s; 30 s in act 4). Then the creature gets a "withdraw" job to a node 150 m or more away, out of sight ("backstage"), unless it is in Chase with the player in plain view |
| Relax | At least R seconds without a director-sent threat (R per act: 150, 120, 75, 45 s). Pickups, documents and the map are placed to fill this time |

- **Caps per zone and act:**
  - at most 2 materialised predators plus 1 compy pack in acts 1 to 2;
  - 3 plus 1 in acts 3 to 4.

  This is also the frame budget of 2.4.

**Territories.** Each species has a home zone and a hunting range that widens by act. Examples:

- raptors leave the compound from act 2;
- the T. rex reaches the meadows after the power-plant objective.

The director may only send a species within its current range. Herbivores never hunt. Their stampedes and alarm calls warn of predators
nearby; that is the information role the coordinator's view gives them. The island's outer boundary is cliffs and sea. Where a cliff is
missing, a territory nobody survives (the T. rex's core) plays the role of Subnautica's leviathans.

**Safe outposts (5 to 6 in the whole game).**

- Each has a door that closes, a save terminal, a map board and lights.
- Each needs power restored before it works. This copies The Bunker: light is safety, and it costs a task.
- Creatures do not enter a closed, lit outpost. The director never sends a creature at a player inside one.
- One scripted breach late in act 3 keeps them from being absolute.

**One night, in four acts.** The game is one night, from about 21:00 to dawn. The clock **advances at objective completions** (about 40 to
60 game minutes each), not in real time, so there is no time-out failure. The moon's position changes between acts, which is cheap: it is
one light. Pre-dawn light rises in act 4.

| Act | Story beat | Unlocks |
|---|---|---|
| 1 (demo) | Landing and the outpost | Compys and one raptor scout; director at its gentlest |
| 2 | Power restored | Fences electrified: some routes become safe, others close. The T. rex paddock fails. Raptors hunt in pairs |
| 3 | Labs and parts | The raptor pack of 3 with flanking (behaviour branches unlocked, as in Alien: Isolation). Possibly a storm that shortens hearing radii by 0.6 times and sight further |
| 4 | Call made | Extraction: every predator's range covers the summit; the director's quiet time is 60 s |

**The torch is the tension knob the player holds.**

- With the torch on, a creature's sight range doubles toward the beam (ESTIMATE).
- Compys and raptors notice a beam crossing them at up to 40 m.
- Torch off, the frame is near black: means 0.03 to 0.08 (MEASURED, N1). The player trades sight for safety.
- Switching off costs 2 to 3 s of blindness while the exposure falls (MEASURED, N1 section 7). That is a good, real cost.

**Against the failure modes of 3.1:**

| Failure mode | Countermeasure |
|---|---|
| (1) Missed scares | Director-placed events instead of fixed scares |
| (2) Lingering | The quiet timer Q |
| (3) Dead backtracking | Every revisit changes something: power on or off, a breached fence, a new resident, the moon. Plus one-way shortcuts that open from the far side (ladders, doors with bars inside) |
| (4) Early climax | The summit is gated hard by three items (section 4) |
| (5) Safe-zone crutch | Few outposts, saving takes time, one breach |
| (6) Getting lost | Light and sound landmarks and the filling map (2.5) |

---

## 4. Objective structure: a research site gone wrong, ending in a rescue call

### 4.1 Kinds of gate

| Gate | Opens | Example |
|---|---|---|
| Tool | A class of obstacle everywhere | Bolt cutters cut chain-link gates, a crowbar opens jammed doors |
| Keycard (2 levels) | Doors by security level | Level 1 staff areas, level 2 labs and the relay |
| Power (per sector) | Lights, electric fences, gates, lifts, outposts | Restarting the plant powers Z4 to Z7 |
| Knowledge | A code or a route | A document gives the relay's access code |
| Territory (soft) | Ground that is "open" but deadly at the wrong time | The meadows after the T. rex escapes |

### 4.2 The chain

Times are first-play minutes, ESTIMATE.

| # | Objective | Zone | Needs | Opens | Threat met | Minutes |
|---|---|---|---|---|---|---|
| 1 | Reach the ranger outpost from the beach wreck | Z1 | — | Outpost 1 (map, save once powered) | Compys, a raptor heard | 15 to 20 |
| 2 | Fuel from the boathouse shed; start the outpost generator | Z1 | — | Save, radio, lights | Compy pack around the shed | 10 to 15 |
| 3 | Use the outpost radio: learn that the relay tower needs plant power, a transmitter module and a code | Z1 | 2 | The three threads | Raptor scout outside (end of demo) | 3 to 5 |
| 4 | Bolt cutters from the maintenance depot | Z2 | 3 | Chain-link gates island-wide | Compys, raptors from act 2 | 15 to 20 |
| 5 | Keycard level 1 from the staff quarters | Z2 | 3 | Staff doors, the pump station | Compys, a raptor patrol | 15 to 20 |
| 6 | Restart the pump station (cooling for the plant) | Z3 | 4 or 5 | Plant start-up | Dilophosaurus ambushes in the gullies | 20 to 25 |
| 7 | Restart the power plant: three breakers in three buildings | Z4 | 6, keycard 1 | Power to Z4 to Z7, outposts 3 to 5; **T. rex paddock fails; act 2 starts** | Raptor patrols, steam masking sound | 25 to 35 |
| 8 | Security office: keycard level 2 and the director's notes (relay code) | Z6 | 7 | The relay door, the lab wings | The raptor pack (act 3) | 25 to 35 |
| 9 | Transmitter module and antenna cable from the airstrip hangar | Z5 | 7 (fenced airstrip gate) | Relay repair | Herds, a stampede, T. rex at the edges | 20 to 30 |
| 10 | Climb to the relay, fit the module, enter the code, make the call | Z7 | 8 and 9 | Act 4 | T. rex territory below, raptors following | 15 to 25 |
| 11 | Survive to the helipad at dawn | Z7 | 10 | Ending | Everything, director at its harshest | 10 to 15 |
| | **Main total** | | | | | **173 to 245** |

Order: objectives 1 to 3 are fixed (the demo). After 3, threads A (4, 5, 6, 7) and B (8 and 9 need 7) can be interleaved: 4 and 5 in
either order, 6 needs either 4 or 5, and 8 and 9 in either order after 7. Objective 10 is the hard gate: it needs 8 and 9.

**Optional, 8 items, 20 to 60 minutes.** Research logs and staff notes that tell what went wrong, 3 supply caches, 2 one-way shortcuts, the
sauropod lookout and a sixth outpost. With them the game is **3.2 to 5.1 hours**, matching the brief.

**Why a helicopter, not a boat.** A boat ending needs:

- a dock where the player stands close to water;
- an ocean seen up close;
- boat handling.

That is water gameplay the owner excluded. The relay call plus a helipad on the summit keeps the ending on land.

**Names.** The island, the company, the logos and the look of the site must be the game's own. Species names are free. Universal's
inventions are not: the island and company names from the films, the frilled and venom-spitting Dilophosaurus, the park's look. A real
Dilophosaurus was about 6 m long ([University of Texas, on Marsh and Rowe 2020](https://news.utexas.edu/2020/07/07/famous-jurassic-park-dinosaur-is-less-lizard-more-bird),
[paper](https://resolve.cambridge.org/core/journals/journal-of-paleontology/article/comprehensive-anatomical-and-phylogenetic-evaluation-of-dilophosaurus-wetherilli-dinosauria-theropoda-with-descriptions-of-new-specimens-from-the-kayenta-formation-of-northern-arizona/39C2921EDC6E951AC9F94A22158CA4E5)).
The 7 m in the coordinator's note is UNVERIFIED; either way it is larger than a film raptor, which supports its "large" agent type in
2.6.

---

## 5. Production plan

### 5.1 What the forest took (MEASURED from file dates and STATE.md)

| Stage | Dates | Elapsed |
|---|---|---|
| Research (6 notes) and three competing designs, then the spec | 2026-10-13, 16:30 to 19:00 | About half a day |
| M0 to M4: kit, terrain, floor, young and tall firs, three owner reviews | 10-13 19:00 to 10-15 04:00 | About 1.4 days |
| D1 (Resident Drawer) and D2 (density, first player measurement) | 10-15 06:00 to 11:00 | About 0.2 day |
| N1 (night and torch) | 10-15 12:00 to 15:00 | About 0.15 day |
| W1 (wind and bending, two review rounds) | 10-15 16:00 to 10-18 06:00 | About 2.5 days |

About 5 days from nothing to a dense night forest with wind, at near-continuous agent work with the owner deciding. Two lessons carry
over:

1. Players drift, so measure in the player and pair the A/B runs.
2. Owner reviews of captures decide quality, and each round took a few hours.

### 5.2 Phases

Agent-days, ESTIMATE. Calendar time with owner reviews is about 1.3 to 1.5 times longer.

| Phase | Content | Effort | Exit gate (numbers) |
|---|---|---|---|
| **P0 Technical spine** | Island data model and contract; island pass and tile pass; seams; per-tile stamps; `Island.unity`; `WorldStreamer`; island NavMesh per agent type; save skeleton; audit and tests per tile and seam; probes S1 to S8 | 5 to 8 | S1 to S8 pass; today's forest rebuilt as tiles of the new generator with its captures matching (RMSE within the noise of two identical runs, as the forest regressions did) |
| **P1 Demo zone art (Z1)** | 4 to 6 tiles of fir and fern forest (existing kit, density per zone); coast cliffs (the two rock faces the forest spec kept for "bedded into the bank", M6) and an ocean plane; beach wreck; ranger outpost and boathouse from a bought modular kit; edges gated by the real gates (a chained gate, a cliff, a collapsed bridge) | 6 to 10 | Environment ≤ 10.0 ms worst view at night with the torch; captures reviewed by the owner |
| **P2 Demo gameplay** | Director v1; compsognathus pack AI (bought model and animation); the raptor upgraded to a bought model with the torch read; objectives 1 to 3; save terminal; ambient audio and creature sound (CC0 or bought); minimal UI; tutorial beats | 8 to 14 | 20 to 30 min median over 3 to 5 first-time players; at least 2 director peaks per playthrough; 0 soft-locks; frame ≤ 12.5 ms worst with creatures |
| **P3 Whole island in greybox** | All 7 zones generated with placeholder kits; sites as boxes; objectives 4 to 11; territories and acts; the gate graph test | 8 to 14 | A full playthrough in 3 to 5 h by 2 testers; no gate unsatisfiable; the director's quiet time never exceeds Q |
| **P4 Zone art passes** | Z2 village 5 to 8; Z3 gullies 6 to 10; Z4 ridge and plant 6 to 10; Z6 labs (interiors; revisit GPU occlusion with solid walls, D1 note 4) 8 to 14; Z5 meadows (new grass kit, long fog, herds) 8 to 12; Z7 summit 5 to 8 | 38 to 62 | Each zone ≤ 10.0 ms; memory ≤ 4 GiB environment at its borders |
| **P5 Polish** | Performance, saves across versions, menus, accessibility, audio mix, a 29-view-style confirmation per zone | 10 to 20 | A clean 3 to 5 h run |
| **Total** | | **75 to 128** | |

Species work beyond compsognathus and the raptor (Dilophosaurus, the herbivores, T. rex, the sauropod) falls inside P3 and P4 at 2 to 5
days each. It is the least certain line: it depends on what can be bought.

### 5.3 The demo inside the open world

- **Laid out in the real island.** The island data model is laid out for the whole island from P0. Only Z1's tiles are built at art
  quality. The demo is a slice of the final world, not a separate level.
- **Its edges are real gates the full game opens later:**
  - the chained gate to the village (bolt cutters, objective 4);
  - the cliff path to the gullies (keycard 1);
  - the collapsed bridge on the ridge side (power).

  Physically the demo needs only invisible walls behind those gates, as the forest's 24 boundary boxes do today (spec 3.6).
- **What it contains:**
  - about 20 ha;
  - objectives 1 to 3;
  - one safe outpost;
  - compys as the first, lighter threat: harmless alone, dangerous in a pack, drawn by noise and by the beam;
  - one raptor heard throughout and met at the end;
  - act 1 of the director.
- **Why compys and not the Dilophosaurus first.** Compys teach noise, light and hiding at low stakes. They are small, cheap to render and
  live in the existing fir biome. The Dilophosaurus needs the dense gullies of Z3 and a "large" agent type.

### 5.4 Order of the rest, and why

1. **P3 before P4.** Pacing and the gate graph are proved in greybox, where changes are cheap. The forest spec's own rule: every risky
   feature sits on a default that already works.
2. **Z2 first** of the art passes. It reuses the fir kit plus buildings, so the modular site pipeline matures on easy ground.
3. **Z3 and Z4 next.** They carry act 2's turn (power) and a new species.
4. **Z6 labs.** Interiors are a new rendering case: lights, probes, occlusion.
5. **Z5 meadows.** The new grass kit and long views are the riskiest for frame time. Do them after the measurement pipeline has run on
   four zones.
6. **Z7 last.** It is short and depends on everything.

### 5.5 Decisions for the owner

1. Size: 1.2 to 1.5 km² playable, ceiling about 2 km²; demo of 15 to 25 ha.
2. One night in four acts, the clock advancing with objectives (no day-night cycle, no time-out).
3. Free order after objective 3, with the summit as the hard gate.
4. Environment target ≤ 10.0 ms worst view, which takes 0.4 to 1.8 ms back from the dense forest (levers in 2.4).
5. Saving only at powered outposts plus checkpoint autosaves.
6. A helicopter ending, no boat.
7. Generated tiles (2 to 3 GB) kept out of git and regenerated on each machine.
8. Original names for the island and the company.

---

## Sources

**Project records (MEASURED):**

- `Logs/forest-work/spec.md` (sections 0, 3, 6, 7, 10, 11, 13)
- `Logs/forest-work/build/STATE.md` (verify-M4, D1, D2, N1, W1 I2 to R2)
- `Logs/forest-work/build/issues/D2-density.md`
- `Logs/forest-work/build/issues/N1-night.md`
- `Logs/forest-work/build/issues/W1-runtime.md` (sections 0, 1)
- Code:
  - `Assets/Game/Scripts/Player/PlayerController.cs`
  - `Assets/Game/Scripts/Dinosaurs/RaptorAI.cs`
  - `Assets/Game/Scripts/Core/NoiseSystem.cs`
  - `Assets/Game/Scripts/World/Foliage/FoliageInteractionField.cs`
  - `Assets/Game/Editor/Forest/ForestSceneBuilder.cs`
  - `Assets/Game/Editor/Forest/ForestLayout.cs`
  - `Assets/Game/Editor/Forest/ForestLightingPresets.cs`
- Data:
  - `ProjectSettings/NavMeshAreas.asset`
  - `Packages/manifest.json`
  - file sizes in `Assets/Game/Scenes/ForestData`

**Unity documentation (DOC, local 6000.6 copies; web versions where linked):**

- `Manual/WorldBuildingFeature.html`
- `ScriptReference/Application-backgroundLoadingPriority.html` ([web](https://docs.unity3d.com/ScriptReference/Application-backgroundLoadingPriority.html))
- `Manual/configure-asynchronous-upload-pipeline.html`
- `ScriptReference/Terrain.SetNeighbors.html`, `Terrain-allowAutoConnect.html`, `Manual/terrain-CreateNeighborTerrains.html` ([TerrainUtility.AutoConnect](https://docs.unity3d.com/ScriptReference/TerrainUtils.TerrainUtility.AutoConnect.html))
- `Manual/TextureStreaming-introduction.html`
- `Manual/occlusion-culling-scene-loading.html`
- `Manual/light-probes-and-scene-loading.html`
- `Manual/content-directories-introduction.html`, `content-directories-scenes.html`
- `ScriptReference/AI.NavMeshBuildSourceShape.html`, `AI.NavMeshBuilder.UpdateNavMeshDataAsync.html`
- HDRP 17.6 `Documentation~/probevolumes-streaming.md`, `gpu-resident-drawer.md`, `create-a-global-fog-effect.md`
- AI Navigation 2.0.12 `Documentation~/NavMeshLink.md`, `NavMeshSurface.md`, `NavMeshBuildingComponents.md` ([web](https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/NavMeshSurface))

**Web:**

- Alien: Isolation:
  - [The Perfect Organism (Game Developer)](https://www.gamedeveloper.com/design/the-perfect-organism-the-ai-of-alien-isolation)
  - [Revisiting the AI of Alien: Isolation (Game Developer)](https://www.gamedeveloper.com/design/revisiting-the-ai-of-alien-isolation)
  - [MobyGames entry](https://www.mobygames.com/game/68903/)
- Left 4 Dead:
  - [Booth, AI Systems of Left 4Dead (Valve, 2009, PDF)](https://steamcdn-a.akamaihd.net/apps/valve/2009/ai_systems_of_l4d_mike_booth.pdf)
  - [The Discomfort Zone (Game Developer)](https://www.gamedeveloper.com/design/the-discomfort-zone-the-hidden-potential-of-valve-s-ai-director)
  - [Shacknews retrospective](https://www.shacknews.com/article/69960/devs-discuss-left-4-dead-in-retrospect)
  - [Dread Central](https://dreadcentral.com/news/1833/tons-of-left-4-dead-details)
- Amnesia: The Bunker:
  - [Wikipedia](https://en.wikipedia.org/wiki/Amnesia:_The_Bunker)
  - [Epic Games feature](https://store.epicgames.com/en-US/news/amnesia-the-bunker-is-a-chilling-underground-obstacle-course-of-horror)
  - [gg.deals](https://gg.deals/game/amnesia-the-bunker/)
  - [Pro Game Guides](https://progameguides.com/amnesia/how-long-does-it-take-to-beat-amnesia-the-bunker/)
- Resident Evil 2: [PC Gamer, Kadoi on Mr. X](https://www.pcgamer.com/uk/resident-evil-2s-director-talks-mr-xs-ai-scary-footsteps-and-the-dmx-mod/)
- Dying Light: [Safe Zone](https://dyinglight.fandom.com/wiki/Safe_Zone), [Volatile](https://dyinglight.fandom.com/wiki/Volatile)
- Darkwood: [Bloody Disgusting review](https://bloody-disgusting.com/reviews/3563151/review-survival-game-darkwood-stands-getting-lost-deep-forest/)
- The Forest and Sons of the Forest:
  - [The Forest on Wikipedia](https://en.wikipedia.org/wiki/The_Forest_(video_game))
  - [The Forest wiki talk: Cannibals](https://theforest.fandom.com/wiki/Talk:Cannibals)
  - [isthereanydeal (HowLongToBeat figure)](https://isthereanydeal.com/game/the-forest/history/)
  - [PCGamesN on the Sons of the Forest map](https://www.pcgamesn.com/sons-of-the-forest/map)
  - [GamesRadar+ guide](https://gamesradar.com/sons-of-the-forest-guide)
  - [Sons of the Forest on Wikipedia](https://en.wikipedia.org/wiki/Sons_of_the_Forest)
  - [wepc](https://www.wepc.com/gaming/is-sons-of-the-forest-the-same-island-map-size-location/) (search snippet; the page returned 403)
  - [vcgamers](https://www.vcgamers.com/news/ukuran-map-sons-of-the-forest/)
- Subnautica:
  - [PC Gamer map](https://pcgamer.com/subnautica-map)
  - [Steam discussion](https://steamcommunity.com/app/264710/discussions/0/1693785669872919984)
  - [Ghost Leviathan Juvenile wiki](https://subnautica.fandom.com/wiki/Ghost_Leviathan_Juvenile)
- Green Hell: [Steam discussion](https://steamcommunity.com/app/815370/discussions/0/3338745825895176297) (no figure)
- Far Cry: [expertbeacon](https://expertbeacon.com/which-far-cry-has-the-biggest-map/), [cyberpost](https://cyberpost.co/how-big-is-far-cry-5-world/)
- Breath of the Wild: [GMTK](https://gmtk.substack.com/p/how-nintendo-solved-zeldas-open-world), [Gigazine](https://gigazine.net/gsc_news/en/20171004-breath-of-the-wild-design-secret)
- Dilophosaurus:
  - [University of Texas news, 2020](https://news.utexas.edu/2020/07/07/famous-jurassic-park-dinosaur-is-less-lizard-more-bird)
  - [Marsh and Rowe 2020, Journal of Paleontology](https://resolve.cambridge.org/core/journals/journal-of-paleontology/article/comprehensive-anatomical-and-phylogenetic-evaluation-of-dilophosaurus-wetherilli-dinosauria-theropoda-with-descriptions-of-new-specimens-from-the-kayenta-formation-of-northern-arizona/39C2921EDC6E951AC9F94A22158CA4E5)
- PS5 memory: [Notebookcheck citing Digital Foundry](https://notebookcheck.com/Sony-PlayStation-5-Pro-soll-Qualitaet-aelterer-Spiele-verbessern-und-mehr-RAM-fuer-Spiele-bieten-laut-Digital-Foundry.815212.0.html)
- Unity community:
  - [Unity Discussions: async additive load stalls](https://discussions.unity.com/t/prevent-loadleveladditiveasync-from-halting-the-app/120033)
  - [Unity Discussions: open world, single scene or split](https://discussions.unity.com/t/in-an-open-world-scenario-what-is-better-to-do-everything-in-a-single-scene-or-split/925938)

## What remains unverified, and what settles it

| Claim | Settled by |
|---|---|
| Map areas of The Forest, Sons of the Forest, Subnautica and Far Cry (fan estimates) | Nothing needed: they only set the order of magnitude |
| Average stealth ground speed of 0.6 to 1.0 m/s, and so the 1.2 to 1.5 km² sizing | Log distance over time in the demo playtests (P2) |
| Cell activation cost, loaded-set cost, terrain memory per tile, island NavMesh size and build time | Probes S1, S2, S5, S4 |
| Integration time hiding under a GPU-bound frame | S1 |
| HDRP Water System cost; long-distance fog quality | S8, and one coast view measured in P1 |
| The Left 4 Dead phase names | Reading Booth's slides (the PDF text could not be extracted in this session) |
| All director constants (rates, Q, P, R) | P2 and P3 playtests; they are starting values like the forest's, tuned on evidence |
| Effort ranges | Re-estimate after P0 and P2 with the measured days |
