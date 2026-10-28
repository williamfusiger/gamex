# Forest base scenery: build specification (version 1, revision 2)

Written 2026-10-13 for Unity 6000.6.0f1, HDRP 17.6.0, macOS Metal (MacBook Pro M4 Max). Project: `/Users/william/Documents/GitHub/gamex`.

**Status.** Nothing in this document has been run in Unity. Every number for lighting, density, LOD and performance is a starting value. API names were read in package source, compiled assemblies or local documentation, and the ones most likely to be mistyped were compiled with Unity's own Roslyn against the project's reference assemblies (a compile proves a name exists, not how it behaves); Appendix A says where. Anything that could not be confirmed without running Unity is marked **UNVERIFIED** and has a probe (section 13) and a fallback (section 14).

**Revision 2** applies the findings of two independent verifications (API layer; assets and scope). The list of changes and of what is still unverified is in "Verification notes" at the end.

This specification merges three designs (robustness-first, gameplay-first, realism-first). The backbone is robustness-first: every risky feature sits on a default that already renders correctly. Section 0 lists what was grafted from the other two and why.

Companion file: `asset-manifest.json` (same folder as this spec). It lists every file to download, with URL, byte size and md5 copied from Poly Haven's `/files` API on 2026-10-13, the destination path, and per-model object names, bounds, triangle counts and material bindings. It holds 24 assets and 107 files: 988 MB for version 1 (milestones M1 to M4), 68 MB of deferred skies (M5) and 175 MB of two deferred rock faces (M6).

Path shorthands used below:

| Short | Path |
|---|---|
| `PRJ` | `/Users/william/Documents/GitHub/gamex` |
| `HD` | `/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/Resources/PackageManager/BuiltInPackages/com.unity.render-pipelines.high-definition` |
| `CORE` | same folder, `com.unity.render-pipelines.core` |
| `DOC` | `/Applications/Unity/Hub/Editor/6000.6.0f1/Documentation/en` |
| `XML` | `/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/Resources/Scripting/Managed/UnityEngine` |

---

## 0. Decisions at a glance

| Topic | Decision | Taken from | Why |
|---|---|---|---|
| Scope of version 1 | Milestones M0 to M4: one bounded area, one lighting preset (OvercastMist), no probe bake, no rock faces. Three owner reviews: after M2 (biome and floor), M3 (young firs: go or no-go for the tall firs) and M4 | Verification | The owner asked for a limited first version. The earlier plan limited the area but not the build |
| Biome | Misty temperate fir-and-fern forest built around the Poly Haven `pine_forest` collection | All three | Most of the kit is one published set with the same soil, season and authors: 9 of the 10 models and 3 of the 8 ground textures used in version 1 belong to it. The rest needs colour matching (section 2). No palm, tree fern, liana or large-leaved plant exists on Poly Haven or ambientCG |
| Tree species | Fir only (`fir_sapling_medium`, `fir_tree_01`). Pines are optional later content | Gameplay, robustness | Fir twigs are probably small separate pieces (1.15 to 1.34 vertices per triangle), which is what the thinner needs. Pines need 26 to 43 times reduction, a 648 MB FBX, and their needles may be solid geometry |
| Playable area | Rounded rectangle 130 m by 150 m (1.87 ha) inside one 256 m terrain tile | Robustness | Smallest of the three; the owner asked for a limited first version |
| Tree LODs | Our own editor code derives three meshes per tree from the scanned LOD0 by thinning foliage pieces and enlarging the survivors. Whether the untouched scan is also kept as a nearest level inside about 6 m is decided by a measured experiment at the start of M3 (section 5.6.4) | All three (same idea); verification (the experiment) | Unity Mesh LOD is documented as weak on disconnected pieces; Blender is not installed. Thinning enlarges the surviving twigs 2.6 to 3.1 times on the trees the player stands next to, which is the main realism risk |
| Thinner details | Weighted nested selection and optional flatten-to-quad at LOD1 and LOD2 | Gameplay | Seven times more pieces for the same triangle count at distance; nested sets keep cross-fades quiet |
| LOD system | `LODGroup` with dithered cross-fade on every prop. No run-time Mesh LOD | Gameplay | Whether Mesh LOD is selected under HDRP without the GPU Resident Drawer is UNVERIFIED; LODGroup is not in doubt |
| Lower LODs of rocks and wood | Vertex clustering in our own code by default; Unity's generator used as an offline decimator only if the M0 probe confirms it | Robustness (default), gameplay (upgrade) | Clustering depends on no engine behaviour |
| Ground cover | Prefab instances, with ferns and grass merged into patch meshes that have LODs. No terrain detail system | Gameplay | About five times fewer draw calls than single ferns; avoids three unverified terrain-detail behaviours |
| Default lighting | Overcast mist, lux-calibrated HDRI sky, one real-time key light, no bake required | Robustness, gameplay | Scans look most photographic in diffuse light; the picture tolerates a missing probe bake |
| More lighting presets | HazySun (the showcase for the comparison with paid packs), Dusk and Night are **deferred to M5**, after version 1. Their starting values are kept in Appendix C | Realism (values), verification (deferral) | HazySun needs the probe bake to look right; none of the three has ever been rendered |
| Probe bake | Adaptive Probe Volumes with sky occlusion, probes-only route, baked against temporary low-LOD tree proxies. **Deferred to M5** (Appendix C) | Robustness (route), realism (proxies), verification (deferral) | It needs reflection into internal SRP Core methods and an asynchronous bake whose batch-mode behaviour is unverified. The overcast default does not need it |
| Sky calibration | Upper-hemisphere lux integrated on the CPU from a 1k EXR | Robustness, gameplay | Removes the dependency on an Inspector-only shader blit in batch mode |
| Trail look | Two gravel ruts with a mossy centre strip; root mounds at tall trunks; ferns ringing every scan patch | Realism | Cheap painter and scatter rules that remove the "clean path" and "trunk on a plane" tells |
| Gameplay hooks | Corridors on the trail and two game-trail loops that a 2 m-radius agent can pass; patrol, spawn and viewpoint anchors as empty transforms. Sight and cover metrics and sight-proxy colliders are deferred to the gameplay phase | Gameplay (hooks), verification (deferral) | Several dinosaurs are coming. Corridors and anchors cost almost nothing now and are expensive to retrofit. The cover metric as designed would have been satisfied by thin trunk capsules and measured nothing |
| Project settings | No change to the HDRP asset, Graphics, Quality, Player, tags, layers or existing asmdefs in version 1. No existing file is edited | Robustness, gameplay | The greybox and its tests cannot regress |
| Player rig | A copy of the greybox player set-up, guarded by a parity test | Robustness, realism | No edit to, and no reflection into, `GreyboxSceneBuilder` |
| Verification | In-builder audit that throws; EditMode and PlayMode suites; captures with a JSON sidecar of pixel statistics; automated foliage coverage gate | Robustness (structure), gameplay (coverage gate) | |
| Download location | `Assets/ThirdParty/PolyHaven/<id>/`, with packer-only inputs in a `Source~` sub-folder that Unity does not import | This spec | Follows the project convention for third-party files without importing maps that are only packer inputs |

Rejected, with reasons:

- **Pine canopy and 3.47 GB kit (realism).** Heaviest reduction, largest import, least certain foliage structure, and an estimated 21 million triangles per frame. Kept as optional later content.
- **Terrain detail system for grass, moss and sorrel (realism).** Index order, batch-capture behaviour and validation are all UNVERIFIED.
- **SSGI, SSR, PCSS and the GPU Resident Drawer as defaults (realism).** Each needs a project-wide setting change. They are owner-approved options in M6.
- **Transient import of tree sources (realism).** Deleting and re-importing sources contradicts the verified advice never to re-import by accident. Sources stay imported; the scene never references them.
- **Reflection into private greybox methods (gameplay).** A copy with a parity test has no hidden coupling.
- **Twelve `HidingSpot` components and gameplay maps beyond the surface map (gameplay).** Gameplay content is outside this version. The anchors and the surface map are kept because they are nearly free.
- **`rock_face_01` and `rock_face_02` as outcrops and standing cover (all three designs).** Measured from the files, both are open one-sided sheets cut from a sloping cliff (886 and 754 open boundary edges, no back face), 1.4 and 1.8 times larger in the FBX than in the glTF the first plan measured, and from a different collection with different rock. Placed in the open they can be seen through and do not stop a sight ray from behind. They stay in the manifest as milestone M6 with corrected bounds, for a later use bedded into the bank.

---

## 1. Goal and non-goals

### 1.1 Goal

A scene, `Assets/Game/Scenes/Forest.unity`, that:

1. Looks as photorealistic as CC0 scanned assets and HDRP allow on this Mac, judged from first-person stills and a walk.
2. Is built entirely by editor scripts that run headlessly (`-batchmode -executeMethod`) and also as menu items. No hand work in the Editor.
3. Is walkable in first person with the existing player code, inside a bounded area the player cannot leave.
4. Can later host several dinosaurs and stealth play: it has a NavMesh, corridors for large agents, and anchors. Cover in version 1 is visual (thickets) and crouch-height (rocks) only; see section 7.4.
5. Can be put beside a paid asset pack for comparison: a fixed set of capture viewpoints exists for that.

**Version 1 is milestones M0 to M4** (section 13). The owner looks at captures three times: after M2 (is this biome and this forest floor acceptable?), after M3 (do the young firs pass at eye level? This decides whether the tall firs are fetched at all) and after M4. M5 (more lighting presets and the probe bake) and M6 (optional upgrades) come after version 1 and only if the owner asks.

### 1.2 Non-goals for version 1

- No dinosaurs, AI, hiding-spot components, throwables, objectives or HUD in this scene. The greybox stays the gameplay testbed.
- No water, no wind, no animated foliage, no weather, no audio.
- One lighting preset (OvercastMist). No probe bake. The code keeps the one-child-per-preset structure so presets can be added without a rewrite (M5).
- No sight or cover metrics, no sight-proxy colliders, no standing-height hard cover: no CC0 scan in the kit provides it (section 7.4).
- No rock faces or cliffs (section 0, rejected list).
- No ray tracing or path tracing (HDRP rejects the macOS build target: `HD/Runtime/RenderPipeline/Raytracing/HDRaytracingManager.cs:937-950`).
- No SSGI, SSR, PCSS, GPU Resident Drawer, decals, custom shaders, Shader Graph, tessellation, terrain holes, impostors or billboards.
- No terrain trees and no terrain detail system.
- No streaming, no second tile, not the whole island, no coast.
- No PS5 tuning. Choices are only kept compatible with it.
- No change to player code, to any existing file, or to project settings.
- No tropical vegetation. The allowed sources do not have it.

---

## 2. Art direction

**Biome.** A cool, wet, misty fir forest. Young firs with branches down to the ground stand in thickets between taller firs with bare lower trunks. Ferns grow in drifts. Grey boulders carry moss. The floor is needle litter, dark humus, moss and stones. Rotted stumps sunk into litter mounds and a few fallen trunks lie among the ferns.

**Why this biome.** The Poly Haven `pine_forest` collection is published as one matching set. Of what version 1 uses, 9 of the 10 models (both firs, both rock sets, ferns, grass, both stumps, dry branches) and 3 of the 8 ground textures (`forest_leaves_04`, `rocky_trail`, `forest_ground_04`) belong to it. The rest come from elsewhere and need colour matching, which is why the tints of sections 5.5 and 6.3 exist: `dead_tree_trunk_02` is a sun-bleached South African scan from the `verdant_trail` collection, and `forest_ground_06`, `forest_leaves_02`, `mud_forest`, `mossy_rock` and `forrest_ground_03` belong to no collection (four different authors). Conifers over ferns is a real forest type and also what Mesozoic vegetation looked like. It reads as the Pacific Northwest or Scandinavia, not as a tropical island. That is the ceiling of CC0 scans and needs the owner's acceptance (section 15); the M2 review exists to get that answer before the tree work.

**Mood and palette.** Heavy overcast, soft light with faint shadows, mist that swallows the distance. Dark, damp-looking ground. Deep greens kept slightly desaturated, red-brown litter, grey rock, yellow-green moss. No flowers. Grass only as tufts in the one clearing and on the trail shoulders.

**Rules the builder enforces.**

| Rule | How |
|---|---|
| Realism comes from scan fidelity under diffuse light, not from effects | Default preset is overcast; post-processing is mild (section 9) |
| Nothing is clean | The trail is two gravel ruts with a mossy centre strip; litter and ferns creep over its edges; every stump is ringed with ferns and a dead branch |
| Density at eye level comes from thickets | Tall firs have bare trunks. Young firs are planted in thickets on about 30 % of the core and as a wall on the bank |
| Trees that reduce best stand closest | Young firs (7 to 10 times fewer twig triangles at LOD0) at eye level; tall firs (5 to 25 times) overhead and behind. Whether that reduction survives a look from 1 to 3 m is unproven: experiment E1 (section 5.6.4) |
| Dark floor | The two brightest ground layers are tinted to 0.8 with `TerrainLayer.diffuseRemapMax` |
| Hide repetition where it is cheap | Random yaw; scale ranges per class; two tint variants for twigs and ferns; the half-dead `fir_tree_01_c` is rare; no two neighbours of the same variant within 8 m share a yaw within 40 degrees |
| Nothing moves | Accepted for version 1. In a stealth game a still forest also makes the one moving thing readable |

**The first view, in one sentence.** Standing at the south end of a rutted gravel trail, the player looks north into a dim stand of young firs whose branches sweep down into fern drifts, with low moss-covered boulders just off the trail's shoulder and the pale trunks of taller firs fading into grey mist under a heavy sky.

---

## 3. Scene layout

Coordinates are world X (east) and Z (north) in metres. The origin is the centre of the tile. +Z is north, as in the greybox (player in the south, objective in the north: `PRJ/Assets/Game/Editor/GreyboxSceneBuilder.cs:32-33`).

### 3.1 Frame

| Item | Value |
|---|---|
| Terrain tile | 256 m by 256 m. `TerrainData.size = (256, 60, 256)`. Terrain object at `(-128, -20, -128)`, so world heights run from -20 to +40 and the valley floor is near Y = 0 |
| Heightmap | 1025 by 1025 (0.25 m per sample) |
| Playable area ("core") | Rounded rectangle centred on the origin: half-extents 65 m (X) by 75 m (Z), corner radius 30 m. About 18,700 m². Its outline is the *boundary* |
| Cell grid | 8 by 8 cells of 32 m. Cell index `cx = floor((x + 128) / 32)`, `cz = floor((z + 128) / 32)` |
| Master seed | 1993 (the greybox seed, `GreyboxSceneBuilder.cs:27`) |

### 3.2 Terrain features

| Feature | Position | Shape |
|---|---|---|
| Valley floor | Inside the boundary | Rise of 1.5 % to the north; fBm of plus or minus 1.2 m (4 octaves, 70 m base wavelength); mounds of plus or minus 0.35 m at 9 m wavelength; micro relief of 5 cm at 2.5 m and 2 cm at 1.2 m, faded to zero on the trail |
| Bank | Outside the boundary | Rises 16 m over the first 40 m (smoothstep, steepest part about 31 degrees), then rolls at plus or minus 3 m to the tile edge |
| Clearing | Centre (-26, 2), radius 13 m | Flattened 60 % toward its mean height |
| Hollow | Centre (34, -24), radius 11 m | 1.1 m deep raised-cosine bowl. Mud and the densest ferns |
| Dry gully | Spline (58, 52), (44, 26), (38, 2), (34, -16) | 0.35 m deep, 3.5 m wide, parabolic section, bed height forced to fall toward the hollow. It stays east of the trail |
| Root mounds | At each tall-fir trunk where the slope is at most 12 degrees | Raised cosine of 0.06 to 0.12 m over a radius of 1.0 to 1.4 m. Added after the scatter plan exists and **before any placement gets its height** (section 11.5) |

Slope limits enforced by the generator (the build throws if either fails):

- 12 degrees along the trail centre line.
- 27 degrees anywhere inside the boundary (a talus-clamp pass).

Root mounds cannot break either limit: the steepest mound adds a gradient of `0.12 * pi / (2 * 1.0) = 0.19` to at most `tan 12 = 0.21`, which is 22 degrees, and no mound reaches the trail centre line (tall trunks stay 2.9 m from it, mound radius is at most 1.4 m). The limits are checked again on the final heights.

The reason is the player code: vertical speed is pinned to -2 m/s when grounded and sprint speed is 6 m/s, so the controller leaves the ground when sprinting down more than about 18 degrees (`PRJ/Assets/Game/Scripts/Player/PlayerController.cs:23, 135-138`).

### 3.3 Trail and game trails (Catmull-Rom control points)

| Spline | Control points | Treatment |
|---|---|---|
| Trail, about 150 m | (0, -66), (0, -58), (7, -42), (-6, -24), (-10, -6), (-2, 12), (14, 30), (10, 48), (2, 62) | Tread 1.6 m wide with 1.2 m shoulders, carved 6 cm into a low-pass-filtered copy of the terrain. Painted as two ruts (section 6.4) |
| West game trail | (-10, -6), (-26, 2), (-34, 22), (-14, 38), (10, 48) | Not carved. Corridor rule only |
| East game trail | (7, -42), (26, -36), (34, -24), (38, 2), (30, 38), (14, 30) | Not carved. Corridor rule only. It follows the gully bed between the hollow and (38, 2) |

**Corridor rule (for large agents later).** No collider comes within 2.5 m of the centre line of the trail or of either game trail. The distance is measured to the collider's **surface**, not to the pivot: a placement is allowed only if `distance(pivot, centre line) >= 2.5 m + clearance`, where `clearance` is the horizontal radius of the collider's bounding circle after scaling (section 7.1). Nothing is exempt: the hand-listed landmark and spawn pieces obey the same rule. That leaves room for a capsule of radius 2.0 m plus a 0.5 m margin for rock tilt, and the audit checks exactly that capsule. Two visual rules sit on top: young firs keep their trunk at least 3.5 m from the centre line of the main trail, so their branches do not sweep across the tread, and fern density is halved within 1.5 m of a game-trail centre line. The two loops share the trail and form a figure of eight for patrols.

### 3.4 Landmarks

Rocks here are the closed moss rocks of the two sets. Sizes are real: at scale 1 they stand 0.43 to 1.11 m above the ground (section 4.2), so they are crouch cover and set dressing, not walls.

| Name | Position | What is there |
|---|---|---|
| South bank | (-5, -72) | A wall of firs behind the spawn (the bank band of section 7.2) and three moss rocks at scale 1.2 to 1.5 at the foot of the bank |
| Deadfall | (12, -14) | Three `dead_tree_trunk_02` trunks (scale 1.2 to 1.5) fallen the same way: 1.5 to 2.5 m apart, yaws within 35 degrees of each other, not crossing. Ferns and two dry branches between them |
| Fern hollow | (34, -24) | Mud, the densest ferns, one stump patch |
| Clearing | (-26, 2) | Open sky, grass patches, two stump patches, three tall firs on its rim |
| Boulder field | (30, 38) | Twenty moss rocks at scale 1.3 to 1.8 within 14 m of the centre, on both sides of the east game trail, whose corridor stays free. Only `rock_moss_set_01_rock04` and `rock_moss_set_02_rock11` are used above scale 1.6; at 1.7 to 1.8 their peaks reach 1.8 to 2.0 m. Everything else here is 0.6 to 1.3 m high: crouch cover |
| North outcrop | (2, 71) | The trail's end: nine moss rocks (the six of set 01 plus `rock10`, `rock11`, `rock13`) at scale 1.5 to 1.9, piled in an arc 8 to 11 m from the trail's last point between bearings -60 and +60 degrees, each overlapping its neighbour by about a third, at the foot of the north bank. They close the view; the bank and the wall close the way. The natural place for a later objective |

### 3.5 Spawn and first view

Spawn at (0, terrain height + 0.05, -62), yaw 0 (facing north along the trail). The first 15 m ahead are composed by rule, not by chance. Positions are given in trail coordinates: metres along the trail from the spawn, and metres to the left (west) or right (east) of the centre line.

| Piece | Along | Lateral | Details |
|---|---|---|---|
| Young-fir thicket, five trees | 6 to 13 m | 3.6 to 8 m left | Mixed variants, scale 0.9 to 1.2 |
| Moss boulder 1 | 8.5 m | 4.3 m right | `rock_moss_set_02_rock11`, scale 1.25: clearance 1.71 m, top about 1.3 m |
| Moss boulder 2 | 11.5 m | 3.9 m right | `rock_moss_set_02_rock12`, scale 1.2: clearance 1.32 m, top about 0.8 m |
| Tall fir | 14 m | 3.0 m left | `fir_tree_01_a` (from M4) |
| Fern patches | 2 to 15 m | 1.8 to 4 m, both sides | Up to the shoulder; the tread stays clear |

Nothing solid within 5 m of the spawn point, measured like the corridor: `distance(pivot, spawn) >= 5 m + clearance`. The pieces above satisfy both rules with their real sizes (boulder 1: 4.3 - 1.71 = 2.59 m from the centre line and 9.5 - 1.71 = 7.8 m from the spawn). The planner still validates every hand-listed piece: one that violates a rule is pushed outward along the trail normal in 0.1 m steps, at most 2 m, with a warning; if it still violates, the plan throws.

### 3.6 What bounds the area

1. **Visual:** the bank, and a 20 m band of dense young firs on it, backed by 45 m of tall firs. Mist hides the rest.
2. **Physical:** 24 invisible `BoxCollider` walls (no renderer), 8 m tall and 1 m thick, 1.5 m outside the boundary: one per straight side and five chords per corner arc.
3. **Tested:** a player-sized capsule cannot leave (section 12.3).

### 3.7 Scene hierarchy

```
Lighting/                     Preset_OvercastMist (active). M5 adds further Preset_<Name> children and a ProbeVolume
Environment/                  everything static; the NavMesh is baked from this object's children
  Terrain
  Bounds/                     24 wall boxes
  Landmarks/<name>/           the fixed set pieces of 3.4 and 3.5
  Cells/Cell_<cx>_<cz>/       scattered instances, grouped by 32 m cell
                              every instance is named <prefabId>__<index>, its index in placements.json (five digits)
Gameplay/                     empty transforms only
  TrailWaypoints/WP_00..      one every 10 m along the trail, on the ground
  PatrolNodes/<spline>_<n>    one per control point of the trail and both game trails
  DinoSpawns/                 Hollow, BoulderField, NorthOutcrop
  Viewpoints/<id>             capture viewpoints (section 12.2): position and rotation
Player                        the greybox rig
Game                          GameSession (cursor lock)
```

The cell hierarchy costs nothing now and lets cells become additive scenes later.

---

## 4. Asset kit

All assets are Poly Haven, CC0, downloadable without an account. The full list with URLs is `asset-manifest.json`: 24 assets and 107 files. Version 1 (M1 to M4) uses 19 of them: 93 files, 988 MB. Sizes below are sums of the API's byte counts. All 107 URLs answered a HEAD request with the recorded size on 2026-10-13.

### 4.1 Download recipe

- Mesh: the FBX only. Never the glTF (its packages drop the leaf alpha) and never the official texture packages (16-bit PNG and EXR).
- Per material: base colour as JPG, `nor_gl` normal, `arm` as JPG, the alpha map as PNG where one exists. For terrain also the displacement as JPG.
- Normal maps are PNG for surfaces seen up close (terrain, rocks, bark, stumps, logs) and JPG for foliage and small debris. JPG normals measured about 5 % channel error at 1k on leaf litter.
- The displacement and `arm` maps are JPG because the packer reads them at 8 bits anyway.
- Skies: the 4k `.hdr` for rendering and the 1k `.exr` for the lux calibration.

### 4.2 The kit

| Milestone | Id | Role | Res | Objects used | Triangles per object (LOD0) | MB |
|---|---|---|---|---|---|---|
| M1 | `forest_leaves_04` | Terrain layer 0: needle and leaf litter (base) | 2k | tile 1.50 m | | 27.4 |
| M1 | `forest_ground_06` | Terrain layer 1: dark humus | 2k | tile 2.11 m | | 33.7 |
| M1 | `forest_leaves_02` | Terrain layer 2: mossy ground | 2k | tile 3.00 m | | 13.8 |
| M1 | `rocky_trail` | Terrain layer 3: gravel | 2k | tile 2.00 m | | 37.3 |
| M1 | `forest_ground_04` | Terrain layer 4: stony soil | 2k | tile 3.15 m | | 18.6 |
| M1 | `mud_forest` | Terrain layer 5: wet mud | 2k | tile 2.35 m | | 33.0 |
| M1 | `mossy_rock` | Terrain layer 6: rock | 2k | tile 3.00 m | | 17.7 |
| M1 | `forrest_ground_03` | Terrain layer 7: pale dry needles | 2k | tile 2.00 m | | 17.1 |
| M1 | `overcast_soil_puresky` | Sky, preset OvercastMist | 4k | | | 22.0 |
| M2 | `rock_moss_set_01` | Closed mossy boulders 1.8 to 3.4 m across, 0.63 to 1.11 m above the pivot plane and 0.41 to 0.66 m below it: crouch cover, clusters, boulder field, north outcrop | 4k | `rock01` to `rock06` | 5,000 to 16,548 | 104.3 |
| M2 | `rock_moss_set_02` | Closed mossy rocks 1.0 to 2.2 m across, 0.43 to 1.05 m above the pivot plane. `rock08`, `09`, `10` and `12` have flat undersides only 1 to 4 cm below the pivot (section 7.3) | 4k | `rock07` to `rock13` | 7,894 to 9,826 | 108.0 |
| M2 | `fern_02` | Fern clumps `a` to `d` | 2k | 4 | 784 to 2,384 | 3.7 |
| M2 | `grass_medium_01` | Grass clumps (8 of 17 used: `tall_a/b/c`, `mid_b`, `small_a/b`, `tiny_a`, `tiny_e`) | 2k | 8 | 62 to 1,257 | 9.6 |
| M2 | `tree_stump_01`, `tree_stump_02` | Stump patches: a litter mound 1.4 to 1.6 m across with a rotted stump at ground level. Highest point 0.38 m and 0.29 m above the pivot. Floor dressing, not cover | 2k | 1 each | 41,046 and 62,345 | 30.0 + 32.7 |
| M2 | `dead_tree_trunk_02` | A bent, split trunk piece 4.05 m long and 0.31 to 0.48 m thick, propped on a branch stub (its bounding box is 1.06 m high and wide because of the bend and the stub). Low clutter, not cover. The FBX holds `_LOD0` to `_LOD3` | 2k | 1 | 83,128 | 35.2 |
| M2 | `dry_branches_medium_01` | Dead branches `a`, `b`, `c` | 2k | 3 | 5,254 to 5,933 | 11.0 |
| M3 | `fir_sapling_medium` | Young firs `a` (8.83 m), `b` (7.80 m), `c` (5.91 m) | 2k | 3 | 685,046 / 420,822 / 427,645 | 84.5 |
| M4 | `fir_tree_01` | Tall firs `a` (18.93 m), `b` (14.06 m), `c` (14.52 m, half dead). Includes the dedicated `trunk_c` texture set (23.0 MB) | 2k | 3 | 4,176,819 / 2,300,624 / 505,494 | 348.6 |

| Through | Cumulative download | What exists |
|---|---|---|
| M1 | 221 MB | Walkable terrain under a real sky |
| M2 | 555 MB | Complete forest floor with rocks, deadwood and ferns. First owner review |
| M3 | 640 MB | A young fir forest. Second owner review; go or no-go for M4 |
| M4 | 988 MB | Version 1: the default look |

Kept in the manifest but **not part of version 1**:

| Milestone | Id | What | MB |
|---|---|---|---|
| M5 | `kloofendal_misty_morning_puresky`, `qwantani_dusk_2_puresky`, `qwantani_night_puresky` | Skies for the presets HazySun, Dusk and Night (Appendix C) | 18.3 + 23.0 + 26.4 |
| M6 | `rock_face_01` | Open one-sided cliff sheet, 7.11 x 4.98 (height) x 5.62 m in the FBX, sloping about 42 degrees. Only usable bedded into a bank with the terrain raised behind it | 85.6 |
| M6 | `rock_face_02` | Open one-sided cliff sheet, 4.87 x 4.73 (height) x 3.47 m in the FBX, sloping about 54 degrees | 89.0 |

Facts about these files that the code must respect. Each says how it is known:

- Every FBX that was parsed is a Blender export, binary version 7400, right-handed with Y up (`UpAxis 1`, `FrontAxis 2`, `CoordAxis 0`), `UnitScaleFactor 1`. Every mesh node has `Lcl Rotation` (-90, 0, 0) and `Lcl Scaling` (100, 100, 100), except `rock_moss_set_02_rock07` (scaling 67.899). Variants in one file sit side by side (for example the three young firs at x = 0, 5 and 10 m). *Parsed in full: the seven FBX files of `rock_moss_set_01`, `rock_moss_set_02`, `rock_face_01`, `rock_face_02`, `fern_02`, `grass_medium_01`, `dry_branches_medium_01`. For the other five models only node names and transforms were read from the file tails.* The transform Unity gives the imported children is UNVERIFIED, so the code measures it (section 5.6).
- **The FBX is not always the glTF.** For the seven parsed files the FBX geometry equals the glTF's, except the two rock faces (scaled 1.435 and 1.80 times and re-oriented in the FBX) and `rock07` (the glTF stores its 0.679 node scale separately). The manifest's `objects[]` of the seven parsed models are measured from the FBX (`objectsSource: "fbx"`). For `tree_stump_01`, `tree_stump_02`, `dead_tree_trunk_02`, `fir_sapling_medium` and `fir_tree_01` they still come from the glTF (`objectsSource: "gltf"`) and are **UNVERIFIED against the FBX**. Section 5.1 makes measuring the FBX a step of the download.
- Tree objects are named `<variant>_LOD0` and the FBX holds no lower LODs. Only `dead_tree_trunk_02` ships `_LOD0` to `_LOD3` (its catalogue polycount of 155,864 is 83,128 plus three halvings, which supports that).
- 88.2 to 98.7 % of each fir's triangles are the twig sub-mesh (`fir_tree_01_c` 88.2 %, `_b` 95.9 %, `_a` 97.3 %, the young firs 98.5 to 98.7 %). The wood is 5,520 to 110,700 triangles.
- `fir_tree_01_dead_branches` uses the bark textures and `fir_sapling_medium_branches_dead` the branches textures; the manifest records these as `sameAs`. `fir_tree_01_trunk_c` is a special case. Poly Haven ships a `trunk_c` texture set, but neither the glTF nor the FBX package references it (only the `.blend` does), the glTF binds `trunk_c` to the bark images, and its glTF primitive has no UV set. Whether the FBX carries usable UVs for it is UNVERIFIED. The manifest therefore downloads the `trunk_c` set and gives the material a `noUvFallback` (section 5.5 says how the choice is made after import).
- The catalogue's `polycount` and `dimensions` fields are not a substitute for measuring: for the two firs they cover more than the three exported trees (0.77 and 0.87 million triangles more, heights 0.34 m larger). They are exact for the two rock faces and equal to the glTF sizes for the stumps and the log.
- No asset has wind data, colliders or thickness maps.

### 4.3 Left out on purpose

| Asset | Reason |
|---|---|
| `pine_tree_01`, `pine_sapling_medium` | Section 0. Optional in M6 |
| `fir_sapling`, `pine_sapling_small` (1 m seedlings) | 120 to 160 thousand triangles each. Optional in M6 once the thinner is proven |
| `dead_tree_trunk` | 101,802 triangles for a thin 3 m branch; logs and dry branches cover the role |
| `pine_roots`, `bark_debris_01`, `moss_01`, `boulder_01`, `rock_09` | Scan patches under the wrong species, 38 to 65 thousand triangles per bark chip, 1 to 4 cm cards, red-brown rock, hand-size stone. None is needed for the look |
| `rock_face_03`, `pine_bark` as detail-map sources | Optional M6 upgrade if `rock_close` or `trunk_close` crops look soft |
| A closed rock, stump or log of standing height | None exists in the kit. Standing-height hard cover is a gameplay-phase problem (section 7.4) |
| Every glTF and official package, every Namaqualand, coast and flower asset, ambientCG | Wrong biome or wrong format |
| Unity sample meshes and textures | Unity Companion License, not CC0 |

When an M6 asset is approved, its URLs must be read from `https://api.polyhaven.com/files/<id>`, never constructed.

### 4.4 Manifest format

`asset-manifest.json` is a JSON array with one entry per asset:

| Field | Meaning |
|---|---|
| `id`, `source`, `kind`, `role`, `resolution` | Poly Haven id; `"polyhaven"`; `"model"`, `"texture"` or `"hdri"`; what it is for; `"2k"` or `"4k"` |
| `files[]` | `url` (from the `/files` API), `relPath` (destination relative to `Assets/ThirdParty`), `bytes` and `md5` (from the API), and `map`, which says what the file is. For texture and sky entries the tag is bare: `basecolor`, `normal`, `arm`, `height`, `sky`, `skyCalibration`. For model entries it is `mesh` or `<material>:<map>` with `<map>` one of `basecolor`, `diffuse`, `normal`, `arm`, `alpha` |
| `milestone`, `license` | `"M1"` to `"M6"`; `"CC0-1.0"`. Version 1 is M1 to M4 |
| `maxTextureSize` | Import size for the entry's base colour and normal maps |
| `terrainLayer`, `tileSizeMeters` | Textures only: layer index and tile size |
| `preset` | Skies only: the lighting preset that uses it |
| `materials[]` | Models only: `name` (the FBX material name), then either `sameAs` (reuse another material of the same entry) or `surface` (`opaque` or `foliage`), `alphaCutoff` (foliage) and `maps` (destination paths per map). Optional `noUvFallback`: the material whose triplanar variant is used instead when the imported sub-mesh has no usable UV set (section 5.5) |
| `objects[]` | Models only: `name` (the object's node name, identical in glTF and FBX wherever both were read), `boundsMin`, `boundsMax`, `triangles`, `subMeshes[]` (`material`, `triangles`, `vertices`, `hasUv0`). `vertices` and `hasUv0` are what the glTF showed; the build does not rely on them (section 5.5) |
| `objectsSource` | Models only: `"fbx"` when `objects[]` was measured from the FBX that is downloaded, `"gltf"` when it still comes from the glTF descriptor and has not been checked against the FBX |
| `boundsSpace` | Models only, always `"source-rh-yup"`: bounds are in metres, in the file's own right-handed, Y-up space, relative to the object's pivot, with the node's rotation and scale applied and its translation ignored. **Unity is left-handed, so an imported mesh is mirrored on one horizontal axis relative to these numbers** (section 5.6.1) |
| `note` | Free text for the reader; the code ignores it |
| `fbxNodes[]` | `dead_tree_trunk_02` only: the four LOD objects in the FBX (its glTF holds LOD0 only) |

A copy of the file is committed as `Assets/Game/Editor/Forest/forest-asset-manifest.json`; after step zero (section 11.2) that copy is the one the tools read and update.

---

## 5. Asset import pipeline

### 5.1 Download and folder layout

Downloads are done outside Unity from `asset-manifest.json` (the tool is `Tools/forest/fetch_assets.py`, section 11). Rules for any downloader:

- One file at a time, whole files only, `curl --fail --location --retry 3 --user-agent "gamex-forest-builder/1.0" --max-filesize <bytes + 1024>`. Poly Haven asks for a unique User-Agent. Never rely on HTTP ranges: the server once answered a range request with a 559 MB body.
- Write to `<relPath>.part`, verify byte size and md5 against the manifest, then rename.
- A file that already exists with the right size and md5 is skipped.
- **Measure every FBX before Unity sees it.** After a model's FBX is fetched, `Tools/forest/fbx_objects.py` (section 11.4) parses the file and compares every object with the manifest: triangle count, triangles per material, and bounds within 2 % of the largest dimension (at least 1 cm). For an entry with `objectsSource: "fbx"` a mismatch is an error: the file changed. For an entry with `objectsSource: "gltf"` there are two outcomes. If everything matches, the tool sets `objectsSource` to `"fbx"` in the project's manifest copy and the fetch goes on. If anything differs, the fetch stops with exit code 2, prints the measured values and changes nothing; someone then checks that the layout still holds with the real sizes (the rock faces turned out 1.4 and 1.8 times larger than planned) and re-runs the tool with `--write`, which stores the measured values and sets the flag. The kit build refuses a model whose entry still says `"gltf"`, so no prefab is ever built from unmeasured numbers.

```
Assets/ThirdParty/PolyHaven/
  THIRD_PARTY_ASSETS.md                      id, source page, "CC0 1.0" per asset
  <id>/<id>.fbx                              imported by Unity
  <id>/<material>__basecolor.jpg             imported (opaque base colour)
  <id>/<material>__normal.png | .jpg         imported (normal map)
  <id>/<id>__sky.hdr                         imported (sky cubemap)
  <id>/Source~/<material>__arm.jpg           NOT imported: packer input
  <id>/Source~/<material>__diffuse.jpg       NOT imported: foliage colour without alpha
  <id>/Source~/<material>__alpha.png         NOT imported: leaf mask
  <id>/Source~/<id>__height.jpg              NOT imported: terrain displacement
  <id>/Source~/<id>__sky1k.exr               NOT imported: lux calibration
Assets/Game/Scenes/Forest.unity              the scene
Assets/Game/Scenes/ForestKit/                derived assets that are expensive and stable
  Textures/<material>__mask.png              HDRP mask map
  Textures/<material>__basecolor.png         RGBA base colour for foliage
  Textures/<layer>__tmask.png                terrain mask map
  Materials/  TerrainLayers/  Meshes/  Prefabs/
Assets/Game/Scenes/ForestData/               per-scene data, overwritten file by file
  Terrain.asset  NavMesh.asset  SurfaceMap.png  placements.json  build-report.json
  Lighting/Atmosphere_<Preset>.asset         version 1: Atmosphere_OvercastMist and Atmosphere_Inspect
  Bake/                                      M5 only: baking set and probe data (written by the bake stage only)
Assets/Game/Scenes/Forest/                   reserved for Unity's own bake output; the builder never writes or deletes here
Library/GamexForest/stamps.json              input hashes per product, for skipping unchanged work
```

- Unity ignores files and folders whose name ends with `~` (`DOC/Manual/SpecialFolders.html`, "Hidden assets"). That keeps packer-only inputs out of the import pipeline.
- `<material>` is the FBX material name (for example `fir_tree_01_bark`); for single-material assets it equals the id. File names are fixed at download time because Poly Haven's own suffixes vary. No name ends in `_NF` or `_OSNF`: HDRP's own postprocessor takes over such textures (`HD/Editor/AssetProcessors/NormalMapFilteringTexturePostprocessor.cs:13-14`).
- **Never delete or rename anything under `Assets/ThirdParty/PolyHaven` or `ForestKit` from a build.** Re-importing gigabytes dominates build time and churns GUIDs. The greybox habit of deleting its scene folder (`GreyboxSceneBuilder.cs:74`) is not copied.

### 5.2 Model import settings

Applied by `ForestAssetPostprocessor.OnPreprocessModel`, only for paths under `Assets/ThirdParty/PolyHaven/`, before the first import. The postprocessor only sets importer properties: it creates no assets and reads no files.

| Property | Value |
|---|---|
| `useFileScale`, `globalScale` | `true`, `1f` |
| `bakeAxisConversion` | `false` |
| `preserveHierarchy` | `true` |
| `importCameras`, `importLights`, `importVisibility`, `importBlendShapes`, `importAnimation` | `false` |
| `animationType` | `ModelImporterAnimationType.None` |
| `materialImportMode` | `ModelImporterMaterialImportMode.ImportViaMaterialDescription` (keeps the FBX material names on the slots) |
| `materialLocation`, `materialName` | `ModelImporterMaterialLocation.InPrefab`, `ModelImporterMaterialName.BasedOnMaterialName` |
| `importNormals` | `ModelImporterNormals.Import` |
| `importVertexColors` | `false` |
| `isReadable` | `false` (mesh data is readable in the Editor regardless: `DOC/ScriptReference/Mesh-isReadable.html`) |
| `meshCompression` | `ModelImporterMeshCompression.Off` |
| `weldVertices`, `addCollider`, `generateSecondaryUV` | `true`, `false`, `false` |
| `generateMeshLods` | `false` for every model (LODs are built by our code) |

| Model group | `importTangents` | `meshOptimizationFlags` |
|---|---|---|
| Rocks, stumps, log, branches, ferns, grass | `ModelImporterTangents.CalculateMikk` | `MeshOptimizationFlags.Everything` |
| `fir_sapling_medium`, `fir_tree_01` | `ModelImporterTangents.None` (derived meshes recalculate) | `(MeshOptimizationFlags)0` (derived meshes call `Optimize()`) |

`fir_tree_01.fbx` (249 MB, up to 5.4 million vertices per tree) is imported alone in its own run. Import time and memory are UNVERIFIED.

### 5.3 Texture import settings

Applied by `ForestAssetPostprocessor.OnPreprocessTexture` by file suffix, for paths under `Assets/ThirdParty/PolyHaven/` and `Assets/Game/Scenes/ForestKit/Textures/`.

| Suffix | Settings |
|---|---|
| `__basecolor.jpg` | `textureType = Default`, `sRGBTexture = true`, `alphaSource = TextureImporterAlphaSource.None` |
| `__basecolor.png` (foliage) | `Default`, `sRGBTexture = true`, `alphaSource = FromInput`, `alphaIsTransparency = true`, `mipMapsPreserveCoverage = true`, `alphaTestReferenceValue` = the material's cutoff from the manifest |
| `__normal.png`, `__normal.jpg` | `textureType = TextureImporterType.NormalMap`, `flipGreenChannel = false` (Poly Haven `nor_gl` is Y+, which is what Unity expects) |
| `__mask.png`, `__tmask.png` | `Default`, `sRGBTexture = false`, `alphaSource = FromInput`, `alphaIsTransparency = false` |
| All of the above | `ignorePngGamma = true`, `mipmapEnabled = true`, `streamingMipmaps = true` (false for terrain layer textures), `textureCompression = TextureImporterCompression.CompressedHQ`, `anisoLevel` 8 for terrain and 4 otherwise, **explicit** `maxTextureSize`, no per-platform override |
| `__sky.hdr` | `textureShape = TextureImporterShape.TextureCube`, `generateCubemap = TextureImporterGenerateCubemap.Cylindrical`, `sRGBTexture = false`, `mipmapEnabled = true`, `filterMode = Trilinear`, `wrapMode = Clamp`, `maxTextureSize = 4096`, `textureCompression = Uncompressed` |

`maxTextureSize`: 4096 for base colour and normal of the two moss rock sets (4 textures at 4096); 2048 for everything else, including every mask.

Per-asset options (which ids import at 4096, which are terrain layers, the cutoff of each foliage material) come from a small static table inside the postprocessor, so it reads no file during import. `Manifest_LoadsAndPathsAreUnique` checks that table against the manifest.

Notes:

- `ignorePngGamma` matters: some Poly Haven PNGs carry a `gAMA` chunk or an ICC profile that would silently shift normal and mask data.
- Default-platform settings only, so a later console target picks its own formats.
- The sky is uncompressed (about 67 MiB per sky) because BC6H on Apple silicon is UNVERIFIED. Probe P8 tests `CompressedHQ`; if it is clean, switch.
- While iterating, `EditorUserBuildSettings.overrideTextureCompression = UnityEditor.Build.OverrideTextureCompression.ForceFastCompressor` is allowed. The enum lives in the namespace `UnityEditor.Build`; without that qualifier or a `using UnityEditor.Build;` the line does not compile (compiled both ways). It persists in `Library`, so every run prints its value and final captures are made with it off.

### 5.4 Texture packing

`ForestTexturePacker` reads the **source files on disk** with `ImageConversion.LoadImage(Texture2D, byte[], bool)` (JPG loads as RGB24, PNG as ARGB32, at 8 bits per channel, without gamma correction: `DOC/ScriptReference/ImageConversion.LoadImage.html`), shuffles bytes with `GetPixels32`, and writes PNG with `EncodeToPNG`. One texture set in memory at a time (a 4k image is 67 MB per array).

| Output | R | G | B | A |
|---|---|---|---|---|
| `<material>__mask.png` (HDRP/Lit mask map: metallic, AO, detail mask, smoothness) | `arm.B` | `arm.R` | 255 | `255 - arm.G` |
| `<layer>__tmask.png` (TerrainLit mask map: metallic, AO, height, smoothness) | 0 | `arm.R` | height | `255 - arm.G` |
| `<material>__basecolor.png` (foliage) | diffuse R | diffuse G | diffuse B | first channel of the alpha PNG |

Evidence for the layouts: `HD/Runtime/Material/Lit/LitDataIndividualLayer.hlsl:256`; `HD/Runtime/Material/TerrainLit/TerrainLit.shader:98-101`; Poly Haven `arm` is AO, roughness, metallic (measured on `forest_leaves_04`: `arm.R` mean equals the AO map's mean, `arm.G` equals the roughness map's).

Rules:

- Foliage alpha is always composed from the separate alpha file, even where a diffuse PNG claims to carry alpha. Read one channel: some alpha files are RGB or RGBA.
- Masks of 4k sets are written at 2048 (box-filter down by two).
- The packer throws when: sizes differ between the maps of a set; the mean of `arm.B` exceeds 0.1 (not a metal map); foliage alpha coverage is outside 3 % to 97 %.
- The packer also returns, per foliage atlas, the alpha-weighted mean colour (used by the crown core, section 5.6.5).

### 5.5 Materials

All `HDRP/Lit`, created in code. Three rules from the HDRP source (`HD/Runtime/Material/MaterialExtension.cs:361-369`):

1. Never call `EnableKeyword`. `HDMaterial.ValidateMaterial` clears keywords and rebuilds them from property values.
2. Assign textures **before** validating; `_NORMALMAP` and `_MASKMAP` are derived from the texture slots being non-null.
3. Create the asset **before** `HDMaterial.SetDiffusionProfile`: it attaches a sub-asset to the material (`HD/Runtime/Material/MaterialExternalReferences.cs:48-74`).

| Kind | Recipe |
|---|---|
| Opaque (rock, bark, stump, log, branch) | `_BaseColorMap`, `_NormalMap`, `_MaskMap`; `_BaseColor` white (0.6 grey for `dead_tree_trunk_02`, whose scan is sun-bleached); `_Metallic` 0; `HDMaterial.ValidateMaterial`; `AssetDatabase.CreateAsset` |
| Foliage (twigs, fern, grass) | The opaque recipe with the RGBA base colour, plus `_AlphaCutoffEnable = 1`; `HDMaterial.SetAlphaCutoff(material, cutoff)` (0.5 fern and grass, 0.4 fir twigs); `_DoubleSidedEnable = 1`; `_DoubleSidedNormalMode = 1` (Mirror); `_MaterialID = 5` (Translucent); `_Thickness = 0.5`; `ValidateMaterial`; `CreateAsset`; **then** `HDMaterial.SetDiffusionProfile(material, profile)` and `EditorUtility.SetDirty` |
| Tint variant 1 (twigs 0.88, fern 0.85) | A second asset `<name>__tint1.mat` with `_BaseColor` multiplied |
| Triplanar variant `<name>__triplanar.mat` | The opaque recipe plus `_UVBase = 5` (Triplanar), `_TexWorldScale = 1` (`HD/Runtime/Material/Lit/Lit.shader:157, 159`). Built only for a material that is named as some other material's `noUvFallback`; in this kit that is `fir_tree_01_bark` |
| Crown core (only if enabled) | `HDRP/Lit`, opaque, `_BaseColor` = twig atlas mean colour times 0.6, `_Smoothness` 0.05 |
| Terrain | A copy of `GraphicsSettings.currentRenderPipeline.defaultTerrainMaterial` (shader `HDRP/TerrainLit`) with `_EnableHeightBlend = 1`, `_HeightTransition = 0.04`, then `HDMaterial.ValidateMaterial`; `enableInstancing = true` |

**Diffusion profile.** Use the package asset `Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipelineResources/FoliageDiffusionProfile.asset`. This project's default volume profile already registers it (`PRJ/Assets/HDRPDefaultResources/DefaultSettingsVolumeProfile.asset:180-187`). Each forest preset profile also adds a `DiffusionProfileList` override containing it; the parameter accumulates across volumes, so the scene does not depend on the project default staying as it is. No custom profile is created.

**Guard against the missing-profile tint.** After creation the factory reads `_DiffusionProfileHash`. If it is 0, or the profile asset cannot be loaded, the material is rebuilt as Standard (`_MaterialID = 1`): no translucency, and no possibility of the tint. The tint is **magenta** in HDRP 17.6, not green (`HD/Documentation~/whats-new-14.md:127`).

**Expected foliage state** (from HDRP's own `HD/Samples~/MaterialSamples/Materials/Lit/Leaf.mat`, used as a test oracle): keywords `_ALPHATEST_ON`, `_DOUBLESIDED_ON`, `_MATERIAL_FEATURE_TRANSMISSION`, `_NORMALMAP`; render queue 2475; `_CullMode` 0.

**Sub-mesh mapping.** Materials are assigned by the name of the imported material slot. If a name is not in the manifest, fall back to the exact per-sub-mesh triangle count recorded in the manifest. If that fails too, throw. Never guess.

**Usable UVs and `noUvFallback`.** FBX stores UVs per geometry, not per material, so an imported sub-mesh can have a UV channel that carries nothing. A sub-mesh *has usable UVs* when `mesh.uv` is not empty and the UV bounding box of the vertices that sub-mesh references is wider than 0.01 on both axes. The manifest's `hasUv0` is only what the glTF showed and is not used for this decision. For a material with `noUvFallback` (only `fir_tree_01_trunk_c`):

1. Usable UVs and the material is not listed in `ForestKitSpec.ForceTriplanar`: use the material's own maps, exactly like `trunk_a` and `trunk_b`.
2. Otherwise: use the triplanar variant of the fallback material (`fir_tree_01_bark`, a tileable bark). The `trunk_c` scan textures are not projected triplanar: a scan atlas is not tileable.

Which case applied is written to the kit report. The kit turntable of `fir_tree_01_c` decides whether case 1 looks right; if its UVs turn out to be garbage, add the material to `ForceTriplanar`. This matters because `fir_tree_01_c` is the fallback hero tree if thinning the other two fails (risk 1).

HDRP/Lit has no wind (`HD/Runtime/Material/Lit/BaseLitAPI.cs:93` forces `_VERTEX_WIND` off).

### 5.6 Meshes, LODs and prefabs

#### 5.6.1 One rule for every prefab

Every mesh a prefab uses is a **derived mesh asset** in `ForestKit/Meshes`, stored in metres, Y up, with its pivot at ground level. Every prefab child has an identity transform. Imported models are build inputs only: the scene never references them.

Derivation, per object named in the manifest:

0. Refuse a model whose manifest entry has `objectsSource` other than `"fbx"` (section 5.1): throw with the command to run.
1. Load the model with `AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath)` and find the child by name (for the log, the four `fbxNodes`). If the name is not found and the model holds exactly one mesh, use that mesh and log a warning; otherwise throw.
2. `M = modelRoot.transform.worldToLocalMatrix * child.localToWorldMatrix`, with the translation column zeroed. This handles whatever transform the importer produced without assuming it.
3. Read positions, normals, tangents (where imported), UV0 and per-sub-mesh indices. Apply `M` to positions; its rotation to normals and tangent directions.
4. **Assert the bounds** of the transformed positions against the manifest (rule below). On failure the stage throws for that asset.
5. Build LOD meshes (below), then the prefab.

**The bounds rule (`ForestMeshTools.MatchesSourceBounds`).** The manifest's bounds are in the file's right-handed space; Unity is left-handed (`DOC/ScriptReference/ModelImporter-bakeAxisConversion.html`: "Unity's (left handed, Z forward, Y-up)"), so the importer mirrors one horizontal axis and the box of an off-centre object cannot be compared corner for corner. Which axis is mirrored is UNVERIFIED locally (X is the usual one; then `min.x = -source max.x` and `max.x = -source min.x`). The rule is therefore written so that it does not depend on the answer. With `tol = max(0.02 * largest source dimension, 0.01 m)`, all of these must hold:

- the size on each of X, Y and Z equals the source size within `tol`;
- `min.y` and `max.y` equal the source values within `tol`;
- `|centre.x|` and `|centre.z|` equal the absolute source values within `tol`.

That still catches a scale of 100 or 0.01, a tree lying on its side, a swapped axis and a pack-layout offset that was not zeroed, and it passes under a mirror of X, of Z, of both, or of neither. (Checked on the manifest: all 48 objects pass under each of the four mirrors, and a 5 m offset or a factor of 100 fails every one. A corner-for-corner comparison would have failed 38 of the 48 under an X mirror.) The baker also records, per prefab, which horizontal axes came out mirrored (`"x"`, `"z"`, `"xz"`, `"none"`, or `"symmetric"` when the centre offset is inside the tolerance) in the kit report. Probe P1 reads the same thing from `fern_02_b` and `rock_moss_set_02_rock13`, whose centres are off by more than the tolerance. Nothing in the build depends on the mirror: every consumer of `ForestManifest.ExpectedBounds` uses only the size, the Y range and `FootprintRadius`, which are the same under any mirror.

#### 5.6.2 Decimating connected surfaces (rocks, stumps, branches, tree wood)

Default: **vertex clustering** in `ForestMeshTools`. Positions are snapped to a grid of the given cell size and averaged per cell, so no cracks open. UV and normal are averaged per (cell, UV bucket of 1/64) so texture seams do not smear. Degenerate triangles are dropped. `DecimateToTarget` finds the cell size by bisection.

Upgrade, only if probe P4 passes: `MeshLodUtility.GenerateMeshLods(Mesh mesh, int meshLodLimit)` on a temporary script-built copy, then read one level back with `Mesh.GetTriangles(int submesh, int meshLod, bool applyBaseVertex)` and compact the vertices (signatures: `DOC/ScriptReference/MeshLodUtility.GenerateMeshLods.html`, `DOC/ScriptReference/Mesh.GetTriangles.html`). Whether the generator accepts a script-created mesh is UNVERIFIED. One constant, `ForestKitSpec.UseUnityGenerator`, selects the route.

#### 5.6.3 Thinning foliage (the tree baker)

Input: the twig sub-mesh of one tree, already in metres. Output: one compact twig mesh per LOD. The code is pure array maths in `ForestFoliageThinner` (WP2). Its unit tests on synthetic cards, like those of `ForestMeshTools`, are written by WP5 against the public surface of section 11.4 only, because every test file belongs to WP5.

1. **Pieces.** Weld vertices by position quantised to 0.1 mm (for connectivity only, so UV seams do not split a piece). Union-find over triangles. One connected component is one piece.
2. **Guard.** At least 500 pieces, and no piece above 5 % of the foliage triangles. Otherwise pieces are redefined as 6 cm spatial-hash cells of triangle centroids, and the report says so.
3. **Per piece:** triangle count, area `A`, centroid, the *anchor* (the piece's vertex with the smallest horizontal distance to the trunk axis) and *outerness* (horizontal distance of the centroid from the axis, divided by the largest such distance in its 1 m height band).
4. **Priority key.** `key = hash01(quantised centroid, seed) ^ (1 / w)` with `w = A * (0.35 + 0.65 * outerness)`. Outer pieces make the silhouette. The key does not depend on the LOD, so the kept sets are nested: a piece in LOD2 is also in LOD1 and LOD0, in the same place.
5. **Select.** Keep pieces in descending key order until the triangle target is met.
6. **Flatten (per-LOD switch).** Replace a kept piece with one quad: least-squares affine fit of position as a function of UV over the piece's vertices, evaluated at the corners of its UV bounding box. If the fit residual exceeds 35 % of the piece's size, keep the original triangles. A flattened piece costs 2 triangles.
7. **Compensate.** Scale every kept piece about its anchor by `s = min(maxScale, sqrt(totalArea / keptArea))`, which preserves total leaf area and keeps the piece attached to its branch.
8. **Write** a compact mesh: referenced vertices only, 32-bit indices above 65,535 vertices, original normals, `RecalculateBounds`, `RecalculateTangents`, `Optimize`.

Variant if the LOD sheet looks noisy: hash the cell of a 0.25 m grid instead of the piece, so neighbours live or die together (`ForestKitSpec.ThinByGroup`).

**Crown core (off by default).** A closed hull of 12 height bands by 8 sectors (192 triangles) whose radius in each cell is 80 % of the 70th-percentile distance of the original foliage from the trunk axis, drawn with the crown-core material. Enabled per LOD by `ForestKitSpec.CrownCoreFromLod` if the coverage gate fails at LOD2.

#### 5.6.4 LOD tables (starting values, all constants in `ForestKitSpec`)

Trees. "Foliage" is the triangle target for the thinned twig mesh.

| Tree | Source foliage | LOD0 foliage / wood | LOD1 foliage / wood | LOD2 foliage / wood | Max piece scale (LOD0 / 1 / 2) | LOD0 keeps; pieces enlarged |
|---|---|---|---|---|---|---|
| `fir_sapling_medium_a` | 674,746 | 70,000 / 10,300 (scan) | 8,000 / 5 cm cluster | 1,500 / 20 cm cluster | 3.2 / 10 / 20 | 10.4 %; 3.1 times |
| `fir_sapling_medium_b` | 415,302 | 60,000 / 5,520 | 8,000 | 1,500 | 3.2 / 10 / 20 | 14.4 %; 2.6 times |
| `fir_sapling_medium_c` | 422,055 | 60,000 / 5,590 | 8,000 | 1,500 | 3.2 / 10 / 20 | 14.2 %; 2.7 times |
| `fir_tree_01_a` | 4,066,119 | 160,000 / 110,700 | 40,000 | 8,000 | 5.1 / 10 / 20 | 3.9 %; 5.0 times |
| `fir_tree_01_b` | 2,207,296 | 160,000 / 93,328 | 24,000 | 5,000 | 5.1 / 10 / 20 | 7.2 %; 3.7 times |
| `fir_tree_01_c` | 446,074 | 90,000 / 59,420 | 24,000 | 4,000 | 5.1 / 10 / 20 | 20.2 %; 2.2 times |

- LOD0 keeps the original piece triangles (flatten off) and the scanned wood untouched, so the trunk at eye level is exactly the scan.
- LOD1 and LOD2 have flatten on.
- Sub-meshes that end with the same material are merged: 2 draws per young fir (branches, twigs), 3 per tall fir (bark, trunk, twigs).
- The last column is arithmetic on the targets: the share of twig triangles LOD0 keeps, and the linear enlargement `sqrt(source / target)` that preserves leaf area.

**Experiment E1: is thinned foliage good enough at arm's length?** This is the main realism risk of the plan, and it sits on the trees the player crouches inside. Twigs three times life size on a 2k atlas may read as a different, blurry plant at 1 to 3 m. What a piece is (a short card strip or a welded branch) is unknown until the mesh is read. E1 is therefore the **first task of M3**, before any scatter tuning, and its result is the go or no-go for M4.

The baker supports an optional nearest level, switched per prefab by `ForestKitSpec.ScanLodDistance(prefabId)` (metres; 0 means off). When it is on, child `LOD0` is the **untouched scan** (every twig triangle and the scanned wood: 421 to 685 thousand triangles for a young fir), shown inside that distance, and the thinned levels of the table follow as `LOD1`, `LOD2` and `LOD3` at their unchanged distances. When it is off, the prefab has the three levels of the table.

E1 bakes `fir_sapling_medium_b` three ways and captures each from `fir_close` (eye height, 1.5 m from the branch tips of the nearest spawn-thicket fir), `crouch_hide` and `trail_start`, recording `Triangles Count` and frame time:

| Arm | Nearest level | Cost |
|---|---|---|
| A | Thinned to the table's target (60,000 twig triangles: pieces 2.6 times life size) | As budgeted |
| B | Thinned to 150,000 (keeps 36 %, pieces 1.7 times life size) | About 2.5 times arm A's LOD0 triangles |
| C | The scan inside 6 m, then arm A. The scan level does not cast shadows; its shadow comes from a second renderer in the same level, child `LOD0_Shadow`, that draws arm A's mesh with `ShadowCastingMode.ShadowsOnly` | 421 thousand triangles per fir inside 6 m. In a thicket that is about ten firs: about 5 million twig triangles, drawn in the depth prepass and again in the main pass, but not in the shadow passes |

Decision: take the cheapest arm whose `fir_close` crop passes beside arm C's (the scan is the reference). If only C passes, measure `crouch_hide` and shorten the distance to 4 m if the frame budget is missed. The triangle budgets of section 10 are re-based on the chosen arm; frame time stays the gate. If no arm passes inside the frame budget, that is the no-go: do not fetch `fir_tree_01`, and take the fallbacks of risk 1. Until E1 has run, `ScanLodDistance` is 0 for every tree. Tall firs get no scan level in version 1 (`fir_tree_01_a` alone is 4.2 million triangles and 5.4 million vertices).

| Class | LOD0 until | LOD1 until | LOD2 until | LOD3 until | Culled beyond |
|---|---|---|---|---|---|
| Young fir | 14 m | 42 m | 85 m | | 85 m |
| Tall fir | 12 m | 45 m | 110 m | | 110 m |
| Moss rock | 12 m (scan) | 30 m (about 1/4) | 90 m (about 1/16) | | 90 m |
| Log (four authored levels) | 8 m (`_LOD0`) | 18 m (`_LOD1`) | 40 m (`_LOD2`) | 110 m (`_LOD3`) | 110 m |
| Stump patch | 8 m (scan) | 25 m (about 1/4) | 70 m (about 1/16) | | 70 m |
| Dry branch | 10 m (scan) | 35 m (about 1/4) | | | 35 m |
| Fern patch | 16 m (5 clumps, about 5,600 triangles) | 40 m (3 light clumps at scale 1.35, about 2,400, no shadows) | | | 40 m |
| Fern clump (single) | 30 m | | | | 30 m |
| Grass patch | 25 m (at most 2,500 triangles, no shadows) | | | | 25 m |

The log uses its four authored levels (their triangle counts beyond LOD0 are inferred as halves, about 41,600, 20,800 and 10,400, and are read after import). A prefab has between one and four levels. `ForestKitSpec.LodDistances(prefabId)` returns one "until" distance per level, nearest first; the last value is also the cull distance. `float.PositiveInfinity` as the last value would mean "never culled"; no prefab of version 1 uses it. With a scan level on, its distance is inserted in front.

**LODGroup settings on every prefab:** `fadeMode = LODFadeMode.CrossFade`, `animateCrossFading = false`, `fadeTransitionWidth = 0.2` on each `LOD`; explicit `size` (object height, or width for flat props) and `localReferencePoint` (bounds centre). Animated cross-fading would exclude the object from the GPU Resident Drawer later.

Cross-fade, what is known and what is not. The project has `enableLODCrossFade: 1` on every quality level (`PRJ/ProjectSettings/QualitySettings.asset:39, 92, 145, 198, 251, 304`; the active level is 5, "Ultra", with `lodBias: 1`). HDRP's own switch is obsolete and points at that quality setting (`HD/Runtime/RenderPipeline/Settings/RenderPipelineSettings.cs:421-422`: "This setting has no effect, use LOD Quality Setting instead"; `HD/Documentation~/Upgrading-from-2023.1-to-2023.2.md:11`), so `supportDitheringCrossFade: 0` in `PRJ/Assets/Game/Settings/HDRP.asset:275` changes nothing. `HDRP/Lit` compiles a `LOD_FADE_CROSSFADE` variant and dithers on `unity_LODFade` in it (`HD/Runtime/Material/Lit/Lit.shader:368`, `HD/Runtime/Material/Lit/LitData.hlsl:209-210`). The only HDRP C# that reads the quality setting copies it into the GPU Resident Drawer's settings (`HD/Runtime/RenderPipeline/HDRenderPipelineAsset.cs:247-250`), which this project does not use. That the engine enables the keyword for these LODGroups on the plain SRP path is a run-time fact that no local source shows: **UNVERIFIED**, probe P11. `ForestKitSpec.UseCrossFade = false` switches every group to `LODFadeMode.None`.

**Distance to screen height.** `screenRelativeTransitionHeight = size / (2 * distance * tan(35 degrees)) * ForestKitSpec.LodDistanceScale`, clamped below 0.99 and strictly decreasing. The formula assumes field of view 70 and `lodBias` 1 and is UNVERIFIED; probe P11 measures a real switch distance and sets `LodDistanceScale`. Tests assert triangle counts at set distances, not the formula.

**Shadows.** Every thinned tree level casts shadows; a scan level does not (its `LOD0_Shadow` renderer does). Rocks, logs and stump patches cast. Fern patch LOD0 casts; its LOD1, single clumps, grass and dry branches do not.

#### 5.6.5 Patch meshes

`Mesh.CombineMeshes(CombineInstance[] combine, bool mergeSubMeshes = true, bool useMatrices = true, bool hasLightmapData = false)` on derived clump meshes:

| Prefab | LOD0 content | LOD1 content |
|---|---|---|
| `fern_patch_a`, `_b`, `_c` | Five clumps (four of the light `fern_02_a`, `_d`, one of the heavy `_b` or `_c`) at fixed seeded offsets within 1.0 m, yaw and scale 0.85 to 1.3 varied | Three light clumps at scale 1.35 |
| `grass_patch_a`, `_b` | Twelve light tufts (`tall_*`, `small_*`, `tiny_*`, `mid_b`) within 0.6 m | none |

#### 5.6.6 Colliders and the prefab contract

| Prefab | Collider | Notes |
|---|---|---|
| Tree | `CapsuleCollider` on the root. Radius = 90th percentile of the horizontal distance from the trunk axis of wood vertices between 0.8 and 1.6 m height, at least 0.12 m. Height 4 m, centre at 2 m | The baker throws if the radius exceeds the class's clearance constant (0.15 m young fir, 0.40 m tall fir), because the scatterer plans with that constant before any prefab exists. No sight-proxy collider in version 1: the raptor's sight test is a physics linecast against every collider (`PRJ/Assets/Game/Scripts/Dinosaurs/RaptorAI.cs:36, 188`), so a thicket does not hide the player from the AI yet. That is a gameplay-phase decision (section 7.4) |
| Moss rock, log, stump patch | `MeshCollider` (not convex) on child `Collision`, using a script-built cluster mesh (10 cm cell, at most 3,000 triangles) | Script-created meshes are readable, which a player build requires for mesh colliders. The seven rocks of set 02 and the log were measured as closed surfaces (no open rim; `rock12` has one 2 cm pinhole), so their colliders have no open back. Set 01 and the stump patches were not measured; a stump patch is probably open underneath, which does not matter for a 0.3 m mound that is only ever touched from above |
| Fern, grass, dry branch | none | Never counted as cover |

`ForestKitSpec.ClearanceRadius(prefabId)` is the horizontal radius, at scale 1, of the circle around the pivot that contains the collider: the class constant for trees, `ForestManifest.FootprintRadius` of the object for rocks, stump patches and the log (1.52 to 2.15 m for set 01, 0.86 to 1.57 m for set 02, 1.14 and 1.08 m for the stumps, 2.20 m for the log), and 0 for prefabs without a collider. The corridor and spawn rules use it.

Contract every prefab meets (other packages rely on it):

- Path `Assets/Game/Scenes/ForestKit/Prefabs/<prefabId>.prefab`. Ids are listed in `ForestPrefabIds` (section 11.2).
- Root: identity rotation and scale, pivot on the ground, `LODGroup`, layer Default.
- Static flags: only `StaticEditorFlags.ContributeGI`, and only on the `LOD0` child of moss rocks, logs and stump patches (set with `GameObjectUtility.SetStaticEditorFlags`; it matters only for the M5 probe bake). Trees, ferns, grass and branches carry no static flag.
- Children `LOD0`, `LOD1`, `LOD2`, `LOD3` (as many as the prefab has levels, one to four: the log has four, and so does a tree with a scan level), each with `MeshFilter` and `MeshRenderer`; optional `Collision`; and, only on a tree with a scan level, `LOD0_Shadow` (a shadows-only renderer that belongs to the first level). No other children.
- Renderers: shared material assets only. No `MaterialPropertyBlock`, no "Enable GPU Instancing" on materials, no `BatchingStatic` flag, `receiveGI = ReceiveGI.LightProbes`.
- At most four sub-meshes per renderer.
- Mesh assets `ForestKit/Meshes/<prefabId>__lod<N>.asset` (`N` from 0 to 3, matching the child names) and `<prefabId>__col.asset`.

---

## 6. Terrain

### 6.1 Terrain data and object

Order matters (the two ordering rules are common practice but UNVERIFIED in the local docs; following them means the question never arises):

1. `new TerrainData()`; `heightmapResolution = 1025` **before** `size = (256, 60, 256)`; `alphamapResolution = ForestLayout.AlphamapResolution` (2048, see 6.4); `baseMapResolution = 1024`. Read `alphamapResolution` back and throw if it is not the requested value (that 2048 is accepted is UNVERIFIED locally; the fallback is 1024, one constant).
2. `AssetDatabase.CreateAsset(terrainData, "Assets/Game/Scenes/ForestData/Terrain.asset")` **before** assigning layers and alphamaps. If the asset already exists, load it instead and re-apply every property, so its GUID stays stable.
3. `SetHeights(0, 0, float[1025, 1025])` once, array indexed `[z, x]`, values `(worldHeight + 20) / 60`. Assert every value is inside 0.02 to 0.98.
4. `terrainLayers = ...`; `SetAlphamaps(0, 0, float[2048, 2048, layerCount])`, indexed `[z, x, layer]`.
5. `Terrain.CreateTerrainGameObject(terrainData)` (it adds the `TerrainCollider`); parent under `Environment`; position `(-128, -20, -128)`.

Terrain component: `materialTemplate` = the terrain material of 5.5; `drawInstanced = true`; `ignoreQualitySettings = true`; `heightmapPixelError = 5`; `basemapDistance = 1000` (the low-resolution basemap is never shown, so nothing depends on its GPU generation in batch mode); `shadowCastingMode = ShadowCastingMode.On`. The terrain object gets `StaticEditorFlags.ContributeGI` (it matters only for the M5 probe bake). The serialized field `m_ReceiveGI` is set to `(int)ReceiveGI.LightProbes` through `SerializedObject` when the property exists (field name seen in `PRJ/Library/PackageCache/com.unity.shadergraph@ef6135652377/Samples~/Terrain/Scenes/Terrain/TerrainSample.prefab:132`; there is no public `Terrain.receiveGI`).

### 6.2 Heights

Everything is plain array maths on `float[,]`, deterministic, using `Unity.Mathematics.noise.snoise(float2)` with seed-derived offsets. `Unity.Mathematics` is an engine module in this version and already referenced by the editor assembly (`PRJ/Gamex.EditorTools.csproj` lists `UnityEngine.MathematicsModule.dll`).

World height `H(x, z)` is built in this order:

| Step | Formula |
|---|---|
| 1. Floor | `0.015 * z` + fBm(4 octaves, base wavelength 70 m, amplitude 1.2 m, gain 0.5, lacunarity 2.0) + `0.35 * snoise` at 9 m + `0.05 * snoise` at 2.5 m + `0.02 * snoise` at 1.2 m |
| 2. Bank | With `d` = distance outside the boundary: `+ 16 * smoothstep(0, 40, d)` `+ 3 * snoise(45 m) * smoothstep(40, 60, d)` |
| 3. Clearing | Within 13 m of (-26, 2): blend 60 % toward the disc's mean height, full inside 9 m, fading to 13 m |
| 4. Hollow | Within 11 m of (34, -24): `- 1.1 * 0.5 * (1 + cos(pi * r / 11))` |
| 5. Gully | Bed target = running minimum of the height along the spline toward the hollow. Parabolic carve 0.35 m deep, half-width 1.75 m |
| 6. Trail | Target = low-pass (12 m window) of the height along the spline, minus 0.06 m. Full within 0.8 m of the centre line, fading to zero at 2.0 m. Re-filter until the gradient along the line is at most 12 degrees. The micro relief terms are faded out with the same mask |
| 7. Talus clamp | Iterative relaxation inside the boundary until no slope exceeds 27 degrees |
| 8. Root mounds | After the scatter plan exists and before any placement gets its height (section 11.5): `+ m * 0.5 * (1 + cos(pi * r / R))` at each tall-fir trunk where the slope is at most 12 degrees, `m` 0.06 to 0.12 m, `R` 1.0 to 1.4 m, both from a hash of the trunk position so they do not depend on list order |

The build throws if the trail or core slope limit of section 3.2 is not met after step 7, and again after step 8.

**Reading a height back.** Before the terrain exists, use `ForestFields.HeightAt(x, z)` (bilinear on the same 1025 grid, world metres). From a `Terrain`, world height is `terrain.SampleHeight(worldPosition) + terrain.transform.position.y`: `SampleHeight` returns the height "relative to the Terrain space" (`DOC/ScriptReference/Terrain.SampleHeight.html`), and the terrain sits at y = -20. `TerrainData.GetInterpolatedHeight(x, y)` takes **normalised** coordinates in [0, 1] (`DOC/ScriptReference/TerrainData.GetInterpolatedHeight.html`) and also returns the height above the terrain base. The two interpolations differ by under 1 cm on this terrain (the heightmap is 16-bit over 60 m, 0.9 mm per step; the twist term of the 2 cm micro relief over one 0.25 m cell is about 0.9 cm).

### 6.3 Layers

Material: section 5.5. One `TerrainLayer` asset per layer in `ForestKit/TerrainLayers/`: `diffuseTexture`, `normalMapTexture`, `maskMapTexture`, `tileSize` from the manifest, `normalScale = 1`, and `maskMapRemapMin = (0, 0, -0.04, 0)`, `maskMapRemapMax = (1, 1, 0.04, 1)`.

The height remap (blue channel, in metres: `HD/Editor/Material/TerrainLit/TerrainLitGUI.cs:279-293`) is **identical on all layers** (8 cm amplitude around zero). The height blend compares each layer's height with the maximum over all layers, painted or not (`HD/Runtime/Material/TerrainLit/TerrainLit_Splatmap.hlsl:171-205`), so a layer with a larger amplitude would suppress the others.

| # | Id | Surface | Tile (m) | Tint (`diffuseRemapMax` rgb) | Other |
|---|---|---|---|---|---|
| 0 | `forest_leaves_04` | Litter (base) | 1.50 | 1.0 | |
| 1 | `forest_ground_06` | Humus | 2.11 | 1.0 | |
| 2 | `forest_leaves_02` | Mossy ground | 3.00 | 0.8 | Brightest layer |
| 3 | `rocky_trail` | Gravel | 2.00 | 1.0 | |
| 4 | `forest_ground_04` | Stony soil | 3.15 | 1.0 | Same collection as the stumps |
| 5 | `mud_forest` | Wet mud | 2.35 | 1.0 | `maskMapRemapMin.w = 0.25` so it reads wet |
| 6 | `mossy_rock` | Rock | 3.00 | 1.0 | |
| 7 | `forrest_ground_03` | Dry needles | 2.00 | 0.8 | Pale |

Layers 0 to 3 are a complete look by themselves. That is deliberate: it is the four-layer fallback.

**Layer count.** HDRP documents eight layers (`HD/Documentation~/terrain-lit-material.md:3`: "up to eight Terrain Layers"), and the shader declares it (tag `"SplatCount" = "8"`, `HD/Runtime/Material/TerrainLit/TerrainLit.shader:97`; `#pragma shader_feature_local _TERRAIN_8_LAYERS`, line 60). What sets that keyword on a script-made material is **UNVERIFIED**. No C# in HDRP or SRP Core does it for a non-Shader-Graph material (the only C# hit is `HD/Editor/Material/TerrainLit/ShaderGraph/TerrainLitSubTarget.cs:557`). The engine binary contains the format string `_TERRAIN_%d_LAYERS`, but it sits next to `BaseMapGenShader` and `Hidden/TerrainEngine/Splatmap/Standard-BaseGen` in the string table, so it may serve base-map generation only (and `TerrainLit_BasemapGen.shader:24` declares the same keyword). Because it is a `shader_feature`, the variant can also be stripped from a player build that has no material asset with the keyword on: a question for the M6 standalone build, not for the Editor. The first M1 build uses layers 0 to 3. The second adds 4 to 7, and a test checks that a patch painted 100 % with layer 5 is not black in a top-down capture. If it is black, `ForestLayout.TerrainLayerCount = 4` and the painter folds the extra weights into the first four (stony soil and dry needles into litter, mud into humus, rock into mossy ground).

Stock TerrainLit has no triplanar, parallax, tessellation or anti-tiling (`TerrainLit.shader:9`, `TerrainLit_Splatmap.hlsl:80-81`). Different tile sizes per layer plus ground cover hide the tiling.

### 6.4 Painting rules

Alphamap 2048 by 2048 (0.125 m per texel). At 1024 a rut (0.45 m wide) and the centre strip (0.5 m) would be about two texels each and the blur below would span 0.75 m, leaving one soft gravel band with a faint centre: the two-rut look needs the finer map. In memory the weights are a `float[2048, 2048, 8]` (134 MB) and the terrain stores two RGBA control textures of 2048. Inputs are fields computed once from the finished heights and the scatter plan: slope in degrees, distance to the trail, distance to the gully, clearing and hollow masks, the thicket field (section 7.2), three independent noise fields, and discs stamped from placements.

| Layer | Raw weight |
|---|---|
| 0 litter | 1 everywhere |
| 1 humus | `0.9 * thicket` + 0.6 inside 1.2 m of every tall trunk |
| 2 mossy ground | `0.8 * smoothstep(0.55, 0.75, mossNoise)` where slope is under 15 degrees + 0.9 falling off over 2.5 m around every rock and log + 0.6 on the trail's centre strip (within 0.25 m of the line) |
| 3 gravel | 1.2 in the two ruts (0.30 to 0.75 m from the trail line), times a 0.75 to 1.0 breakup noise + 0.7 in the gully bed |
| 4 stony soil | `smoothstep(14, 26, slope)` + 1.0 inside 1.5 m of every stump (hides the rim of the ground baked into the scan) |
| 5 mud | `0.9 * hollowMask` + 0.5 on the gully banks + `0.25 * puddleNoise` on the trail |
| 6 rock | `smoothstep(32, 42, slope)` |
| 7 dry needles | `0.7 * (1 - thicket) * smoothstep(0.5, 0.7, needleNoise)` + 0.5 in the clearing |

Compositing: rock first, then gravel and mud, then the rest share what is left. Normalise so the weights sum to 1, blur with a 3 by 3 box **except within 2.5 m of the trail centre line**, normalise again, write once. The blur exists to soften thresholded noise. The trail masks are analytic distance bands and are anti-aliased where they are evaluated instead, with a one-texel `smoothstep` on each edge, so the ruts stay 3 to 4 texels wide. Whether the result reads as two ruts and a strip is judged on `ground_close` and `trail_start` at M1; if it does not, widen the ruts to 0.25 to 0.80 m before touching anything else.

**Surface map.** `ForestData/SurfaceMap.png`, 512 by 512, 8-bit: the index of the heaviest layer per 0.5 m texel. It is the future footstep and noise map. The layer index is the surface id and is frozen in `ForestSurface` (section 11.2).

---

## 7. Vegetation scatter

### 7.1 Method

One deterministic scatterer runs on plain arrays before any Unity object exists and returns a `ForestPlan` (a sorted list of `ForestPlacement`). `ForestScatter.Plan` decides x, z, rotation, scale, tint, `sink` and `clearance` and leaves `position.y` at 0. Heights are filled in afterwards by `ForestScatter.Ground`, once the root mounds are in the height field (section 11.5), so no placement is ever grounded on a height that later changes.

- Variable-radius Poisson-disc sampling (Bridson) with one shared occupancy grid, classes placed from large to small.
- Master seed 1993. One `System.Random` per class, seeded `seed ^ (classId * 7919)`, so tuning one class does not reshuffle the others.
- Never `UnityEngine.Random`. Never iterate a `HashSet` or `Dictionary` to produce output order. Sort the final list by (cell, class, x, z) before returning.
- Scattering twice with the same seed must return identical lists (tested).

Shared exclusions:

- Colliders, measured to their surface: `distance(pivot, trail or game-trail centre line) >= 2.5 m + clearance` (section 3.3) and `distance(pivot, spawn) >= 5 m + clearance`, with `clearance = ForestKitSpec.ClearanceRadius(prefabId) * scale` (section 5.6.6). With the real sizes that puts the pivot of a moss rock at scale 1 between 3.4 and 4.7 m from a centre line, and a tall-fir trunk at 2.9 m. Also not in the gully bed (1.75 m) or the hollow centre (4 m). The rule applies to every class and to the hand-listed pieces of 3.4 and 3.5.
- Trees: not on slopes above 28 degrees in the core; not inside the clearing (except its three rim firs). Young firs: trunk at least 3.5 m from the centre line of the main trail.
- Everything in the core keeps its pivot at least 1.0 m inside the boundary.

### 7.2 Classes

**Thicket field** `T = smoothstep(0.45, 0.65, fBm at 38 m wavelength)`, forced to at least 0.8 in the first 18 m outside the boundary, multiplied by 1.5 on the rim of the clearing (13 to 18 m from its centre), and 0 inside it.

| Order | Class | Prefabs | Where and how dense | Variation |
|---|---|---|---|---|
| 1 | Landmarks | Sections 3.4 and 3.5 | Hand-listed positions in `ForestLayout` | Fixed |
| 2 | Tall firs | `fir_tree_01_a/b/c` | Core: minimum spacing 11 m, about 60 trees. Bank: spacing 8 m from 3 m to 45 m outside the boundary, kept with probability `1 - 0.5 * d / 45`, about 220 trees | Yaw 0 to 360; scale 0.90 to 1.15; lean up to 2 degrees. Core mix a 45 : b 40 : c 15; bank 50 : 45 : 5 |
| 3 | Young firs | `fir_sapling_medium_a/b/c` | Spacing `lerp(7.0, 3.0, T)`. About 360 in the core (thickets on roughly 30 % of it), about 410 in the 20 m band on the bank | Yaw; scale 0.85 to 1.20; mix a 30 : b 35 : c 35; tint variant by position hash |
| 4 | Boulders | `rock_moss_set_01_*`, `rock_moss_set_02_*` | Seven clusters (centres at least 28 m apart, biased to slopes over 10 degrees and the gully banks) of 3 to 7 rocks at 1.8 m spacing, so they overlap into one mass; 14 singles; 6 along the first 30 m of trail with their surface 2.5 to 5 m from the centre line; 20 in the boulder field. The outcrop (9), the south bank (3) and the spawn pair are landmarks | Yaw; tilted toward the ground normal, at most 15 degrees; scale 0.8 to 1.3 (1.3 to 1.8 in the boulder field, section 3.4) |
| 5 | Deadwood | `dead_tree_trunk_02`, `tree_stump_01/02`, `dry_branches_medium_01_a/b/c` | Logs: 3 at the deadfall, 5 scattered with their surface 3 to 6 m from a game-trail centre line. Stump patches: 10 (two in the clearing, one in the hollow, seven scattered), at least 12 m apart, on litter or stony soil. Branches: 45, under tall firs | Yaw; aligned to the ground normal; logs at scale 1.2 to 1.5 |
| 6 | Fern patches | `fern_patch_a/b/c` | From the density field below: Poisson radius `clamp(1.1 / sqrt(D), 0.9, 6)` m where `D` is at least 0.05. Cap 900 | Yaw; scale 0.85 to 1.5; aligned to the ground normal; tint variant by hash |
| 7 | Fern clumps | `fern_02_a/b/c/d` | The ring rule (below) and the spawn composition only. Cap 400 | Yaw; scale 0.8 to 1.6 |
| 8 | Grass patches | `grass_patch_a/b` | Clearing: patches on 25 % of it. Trail shoulders: one per 4 m, alternating sides. About 220 | Yaw; scale 0.8 to 1.3 |

**Fern density** `D` (clumps per m², capped at 1.6): `0.04` + `1.1 * smoothstep(0.5, 0.75, fernNoise at 14 m) * moisture` (moisture 1.5 in the hollow and within 6 m of the gully, 1 elsewhere) + 0.8 within 1.5 m of rocks, stumps and logs. Multiplied by: 0 within 0.8 m of the trail centre rising to 1 at 1.8 m; 0.5 within 1.5 m of a game trail; 0.3 inside the clearing; 0 on slopes above 35 degrees.

**Ring rule.** Every stump gets 4 to 6 single fern clumps in an annulus of 0.6 to 1.8 m and one dry branch touching its edge. Every log gets 3 to 5 clumps along its sides and one dry branch.

Expected totals: about 1,050 trees, about 90 rocks, about 75 deadwood pieces (8 logs, 10 stump patches, 45 scattered branches and one ring branch per stump patch or log, two more at the deadfall), up to 900 fern patches, up to 400 fern clumps, about 220 grass patches: roughly 2,700 instances and 11,000 GameObjects.

### 7.3 Grounding (prevents floating objects)

Pivot Y = ground height minus *sink*. The ground height is `ForestFields.HeightAt(x, z)` on the **final** heights, root mounds included; `ForestScatter.Ground` writes it (section 11.5). The sink is decided by the planner and stored in `ForestPlacement.sink`, so the audit can recompute every pivot. A negative sink means raised.

| Class | Sink |
|---|---|
| Trees | `0.10 m + clearance * tan(slope)` |
| Moss rocks | `max(0, 0.08 m + scale * (boundsMin.y + R * tan(residual)))`, where `boundsMin.y` is the manifest value (negative: how far the scan reaches below its pivot), `R` is `FootprintRadius` and `residual` is the part of the slope the tilt did not take up, `max(0, slope - 15 degrees)` |
| Stump patches | 0.04 to 0.08 m times scale: the rim of the scanned ground goes under, and the stony-soil paint of section 6.4 hides the seam |
| Logs | 0.22 to 0.26 m times scale |
| Fern and grass patches, clumps | 0.02 m |

Why the rock rule looks like that. It asks for the lowest point of the scan to end at least 8 cm under the ground at the pivot, plus the drop of the ground across the footprint. The six rocks of set 01 and `rock07`, `rock11` and `rock13` reach 0.26 to 0.66 m below their pivot, so on gentle ground the rule gives 0: they are already bedded. `rock08`, `rock09`, `rock10` and `rock12` have flat undersides only 1 to 4 cm below the pivot; the rule gives them 4 to 7 cm at scale 1 on flat ground and more on a slope, so no gap shows under them on mounds. No rock is buried deeper than that: the first plan sank boulder-field rocks by 20 to 35 % of their height, which left none taller than 1.6 m.

Why the log sinks so far. `dead_tree_trunk_02` rests on a branch stub: along about 2.5 m of its 4.05 m the underside is 0.08 to 0.25 m **above** the pivot plane, the stub reaches 0.33 m below it, and both ends hang at about 0.24 m (measured in 16 slices of the glTF mesh). A sink of 0.24 m times scale beds the whole underside at or below the ground and leaves 0.1 to 0.5 m of trunk showing. If the M2 kit turntable shows too little trunk, the alternative is to pitch the derived mesh by about 4 degrees so the long free end comes down, with a sink of 0.12 m times scale: `ForestKitSpec.LogPitchDegrees`, default 0. Its sign depends on which axis the importer mirrored, so it is set by looking at the turntable, not by calculation.

The audit then checks every instance (section 12.1).

### 7.4 Cover for the later stealth game

The mature forest is open at eye height, and the kit has **no object with a collider that reaches standing height**. The player is 1.8 m tall with eyes at 1.65 m standing and 0.95 m crouched (`PRJ/Assets/Game/Scripts/Player/PlayerController.cs:17-25`). Measured tops above the ground at scale 1: moss rocks 0.43 to 1.11 m, stump patches 0.29 and 0.38 m, a bedded log 0.1 to 0.5 m. For comparison the greybox boulders are 1.6 to 3.0 m tall (`GreyboxSceneBuilder.cs:277`).

What version 1 does about it:

- **Visual cover:** young-fir thickets on about 30 % of the core. They hide the player from a human eye, not from the AI: the raptor's sight test is a physics linecast, and a thicket's only colliders are thin trunk capsules.
- **Crouch cover:** moss rocks in clusters, along the trail and in the boulder field (0.6 to 1.3 m there, with two rock shapes whose peaks reach 1.8 to 2.0 m at scale 1.7 to 1.8; a peak is not a wall).
- **Terrain:** the hollow (1.1 m deep) and the gully (0.35 m).

What is left to the gameplay phase, and not built or measured now: sight-proxy colliders in thickets, sight-line and cover metrics, hiding-spot components, and any standing-height hard cover (which needs either terrain shaped for it, the rock faces bedded into a bank, or an asset from outside the CC0 rule). The first plan's cover metric would have passed on 4 m trunk capsules of 0.12 m radius alone, so it was removed instead of being reported.

### 7.5 Instantiation

`(GameObject)PrefabUtility.InstantiatePrefab(prefab)` (the method returns `UnityEngine.Object`; without the cast it does not compile) then `SetParent` under `Environment/Cells/Cell_<cx>_<cz>` or `Environment/Landmarks/<name>`. Each instance is named `<prefabId>__<index>`, where `index` is its position in `placements.json` written with five digits, so the audit can match an object to its placement. A class whose prefabs do not exist yet is skipped with one log line. That is what makes every milestone a working scene.

Tint variant 1 is applied by `ForestPrefabs.ApplyTint(instance, 1)`, which swaps the foliage material on the instance's renderers for the `__tint1` asset (a shared asset, not a property block).

---

## 8. Lighting

### 8.1 Structure and switching

Each preset is a child of `Lighting` holding its own global `Volume` (one profile asset, `ForestData/Lighting/Atmosphere_<Name>.asset`) and its own key `Light`. Exactly one preset child is active. Switching a preset means activating a different child:

- `ForestLighting.ApplyPreset(name)` on the open scene, also from the menu and with `-forestPreset <name>`;
- by hand in the hierarchy;
- later at run time, one `SetActive` call.

**Version 1 builds exactly one preset, OvercastMist.** The structure above is kept because it costs nothing and lets M5 add presets without touching the builder: `ForestLightingPresets.Names` lists the presets that exist, and a preset whose sky has not been downloaded is not created. HDRP allows only one shadow-casting directional light at a time (`HD/Runtime/Lighting/Shadow/HDShadowManager.cs:1104`); inactive lights do not count.

Why OvercastMist:

1. Scanned assets are de-lit, and diffuse light is where they look most like photographs. Texture resolution, normal detail and leaf edges are all readable, which is what a comparison with a paid pack needs.
2. An overcast sky dominates its sun three to one, so the picture tolerates a missing probe bake, needs no PCSS, and gives automatic exposure an easy target.
3. Soft light is the most forgiving for thinned foliage.
4. Mist and a dim canopy already read as horror.

HazySun (the showcase for comparison frames), Dusk and Night are deferred to M5; their starting values are in Appendix C.

### 8.2 Preset values

Light direction is `transform.rotation = Quaternion.Euler(elevation, bearing, 0)`: the light travels toward compass bearing `bearing` (0 = north, 90 = east) and downward at `elevation`. The sun therefore stands at bearing + 180.

| Field | OvercastMist |
|---|---|
| Sky HDRI | `overcast_soil_puresky` |
| Sky `desiredLuxValue` | 5,000 |
| Key light, lux | 1,500 |
| Key colour temperature (K) | 6,000 |
| Key `angularDiameter` (degrees) | 3 |
| Key `Euler(elevation, bearing, 0)` | (52, 135, 0): sun in the north-west, ahead-left of the first view |
| Key `volumetricDimmer` | 1 |
| Shadow distance; cascades; splits 0/1/2 | 80 m; 4; 0.08 / 0.22 / 0.50 |
| Fog `meanFreePath` | 80 m |
| Fog `baseHeight` / `maximumHeight` (world Y) | 2 / 40 |
| Fog `albedo` | (0.92, 0.93, 0.95) |
| Fog `anisotropy` | 0.6 |
| Fog `depthExtent` | 80 |
| Fog `quality` tier | Medium |
| Exposure | `AutomaticHistogram`, `limitMin` 7.5, `limitMax` 10.5, `compensation` -0.3 |
| White balance `temperature` | 0 |
| `saturation` / `contrast` | -6 / +6 |
| Bloom `intensity` | 0.12 |
| Film grain `type`, `intensity` | `Thin1`, 0.10 |
| Vignette `intensity` | 0.20 |

Arithmetic behind the exposure clamp (neutral exposure for E lux is `EV100 = log2(E / 3.14)`; positive `compensation` is brighter: `CORE/Runtime/Utilities/ColorUtils.cs:247-289`): on open ground 5,000 + 1,500 x sin 52 = 6,200 lux, neutral 11.0 EV. The clamp of 7.5 to 10.5 lets the eye adapt under the canopy but cannot push the scene to black or white, and keeps the clearing visibly brighter than the forest.

Fog visibility at 80 m mean free path (contrast = exp(-d / 80)): 70 % at 28 m (the raptor's sight range), 47 % at 60 m, 29 % at 100 m, 20 % at 130 m (the bank and backdrop).

These values are exposed to the other packages as a read-only record, `ForestLightingPresets.Get(name)` (section 11.4): the audit compares the scene's exposure mode and limits with it, and the `backlit` viewpoint takes the key light's bearing from it.

### 8.3 Components in every preset profile

| Component | Fields |
|---|---|
| `VisualEnvironment` | `skyType.Override((int)SkyType.HDRI)`; `skyAmbientMode.Override(SkyAmbientMode.Dynamic)` |
| `HDRISky` | `hdriSky` = the imported cubemap; `skyIntensityMode.Override(SkyIntensityMode.Lux)`; `desiredLuxValue`; `upperHemisphereLuxValue` and `upperHemisphereLuxColor` from 8.4; `rotation` from 8.4 |
| `HDShadowSettings` | `maxShadowDistance`, `cascadeShadowSplitCount`, `cascadeShadowSplit0/1/2` (fractions of the maximum distance, kept increasing) |
| `ContactShadows` | `enable` true, `length` 0.10, `maxDistance` 40, `fadeDistance` 8 |
| `MicroShadowing` | `enable` true, `opacity` 1 |
| `Fog` | `enabled` true, `enableVolumetricFog` true, `colorMode` `FogColorMode.SkyColor`, `meanFreePath`, `baseHeight`, `maximumHeight`, `albedo`, `anisotropy`, `depthExtent`, `denoisingMode` `FogDenoisingMode.Gaussian`, `quality.Override((int)ScalableSettingLevelParameter.Level.Medium)` (or `High`) |
| `ScreenSpaceAmbientOcclusion` | `intensity` 1.5 without a probe bake (1.0 with one), `radius` 2.0, `directLightingStrength` 0.25 |
| `Exposure`, `ColorAdjustments`, `WhiteBalance`, `Bloom`, `FilmGrain`, `Vignette`, `MotionBlur` | Sections 8.2 and 9 |
| `DiffusionProfileList` | `diffusionProfiles` = the package foliage profile |

Key light (on the preset child): `Light.type = Directional`, `lightUnit = LightUnit.Lux`, `intensity`, `useColorTemperature = true`, `colorTemperature`, `color = white`, `shadows = LightShadows.Soft`, `lightmapBakeType = LightmapBakeType.Realtime`. On its `HDAdditionalLightData`: `angularDiameter`, `SetShadowResolutionOverride(true)`, `SetShadowResolution(2048)`, `useContactShadow.useOverride = true`, `useContactShadow.@override = true`, `affectsVolumetric = true`, `volumetricDimmer`, `interactsWithSky = false`.

Not used in version 1: reflection probes, SSR, SSGI, PCSS, local volumetric fog. Scanned forest materials are rough and sky reflection is on by default. Shadow filtering stays at the asset's Medium (5 by 5 PCF); with a sun of 2 to 3 degrees behind haze, uniformly soft shadows are right anyway.

**Scripting traps (each has cost a compile-and-run cycle elsewhere):**

- `param.Override(x)` sets the override state and writes the value directly, bypassing clamping. Assigning `.value` alone does nothing in a profile (`CORE/Runtime/Volume/VolumeParameter.cs:183-239`). Keep values inside the documented ranges.
- Fog budget and depth ratio cannot be set through their property setters; use `quality.Override(tier)` (`HD/Runtime/Lighting/AtmosphericScattering/Fog.cs:92-157`).
- A script-created light has shadow resolution 512 and contact shadows off until set (`HD/Runtime/Lighting/Light/HDAdditionalLightData.cs:123, 1508-1513`).
- Add volume components with the greybox's `AddOverride<T>` pattern (`GreyboxSceneBuilder.cs:153-159`): `profile.Add<T>()`, hide flags, `AssetDatabase.AddObjectToAsset`.

### 8.4 Sky import and lux calibration

HDRI Sky in Lux mode divides `desiredLuxValue` by `upperHemisphereLuxValue`, and only the Inspector computes that value (`HD/Editor/Sky/HDRISky/HDRISkyEditor.cs:134-161`). A script-built profile left at the default of 1 would multiply the sky by the raw lux number. The builder computes it on the CPU:

1. Load `Source~/<id>__sky1k.exr` with `ImageConversion.LoadImage` (EXR loads as `RGBAFloat` on Mac). `GetPixels()` returns rows bottom to top, so the upper hemisphere is rows `H/2` to `H-1`.
2. Integrate `E = sum of L * cos(theta) * sin(theta) * (pi / H) * (2 pi / W)` over the upper half, per channel, with `theta` measured from the zenith. `upperHemisphereLuxValue` is the Rec. 709 luminance of that sum (0.2126, 0.7152, 0.0722). `upperHemisphereLuxColor` is the sum divided by its largest channel, times 0.5. This is the integral of `HD/Runtime/Sky/HDRISky/IntegrateHDRISky.shader:57-86` and the colour rule of `HDRISkyEditor`.
3. Guards: the value must be finite and positive. The lower half is integrated too and logged; in a pure-sky HDRI it is a blurred mirror of the upper half, so even a row-order mistake cannot produce a wild number. A unit test feeds a constant image of luminance 1 and expects pi within 1 %.
4. The same pass records the azimuth of the brightest sky column.

`HDRISky.rotation` is 0 in version 1: the overcast sky is nearly uniform, so nothing depends on where its brightest part ends up. What M5 needs to know about the rotation is in Appendix C.

Fallback if the calibration misbehaves: `SkyIntensityMode.Multiplier` with a hand-tuned value; the exposure clamp already limits the damage.

### 8.5 Indirect light

**Version 1 uses no bake.** With Adaptive Probe Volumes as the project's probe system and no probe data loaded, HDRP lights everything from the dynamic sky ambient probe (`HD/Runtime/Lighting/LightLoop/LightLoop.hlsl:455-459`). The floor under the firs is then somewhat too bright and too even. Under an overcast sky that is a small error, and SSAO at 1.5 takes most of it back. Nothing unverified is needed. The build report says `"apv": "none"`.

The probe bake with sky occlusion (Adaptive Probe Volumes, probes only, against low-LOD tree proxies) is M5. Its procedure, with the corrections from verification, is in Appendix C.

---

## 9. Post-processing and camera

### 9.1 Player camera

The greybox rig is reproduced (section 11.6): field of view 70, near plane 0.05, far plane 400, eye height 1.65 m. `ForestLighting.ConfigureCamera` then sets on its `HDAdditionalCameraData`:

| Field | Value | Reason |
|---|---|---|
| `antialiasing` | `HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing` | The default is `None` (`HD/Runtime/RenderPipeline/Camera/HDAdditionalCameraData.cs:310`); alpha-tested foliage shimmers without it |
| `TAAQuality` | `HDAdditionalCameraData.TAAQualityLevel.High` | |
| `taaSharpenStrength` | 0.5 | Default |
| `dithering` | `true` | Hides banding in mist gradients |
| `stopNaNs` | `true` | Insurance with HDR skies |
| Custom frame settings | none | The camera stays on the project defaults, like the greybox |

Set in the builder, not only in the capture code, so the Game view and the captures agree.

### 9.2 Post-processing (in every preset profile; colour values per preset in 8.2)

| Component | Values | Reason |
|---|---|---|
| `Exposure` | Per preset. `meteringMode` `MeteringMode.CenterWeighted`, `histogramPercentages` (40, 90) | |
| `Tonemapping` | Inherited ACES from the project default profile | |
| `ColorAdjustments` | `contrast`, `saturation` per preset | Mild; the scans carry the look |
| `WhiteBalance` | `temperature` per preset | The blue of night belongs in grading, not in the light colour |
| `Bloom` | `intensity` per preset, `scatter` 0.65, `threshold` 0 | |
| `Vignette` | `intensity` per preset, `smoothness` 0.4 | |
| `FilmGrain` | `type`, `intensity` per preset, `response` 0.8 | Hides banding in mist |
| `MotionBlur` | `intensity` 0 | The project default of 0.5 smears thin foliage, and the player's job is to spot movement. One constant if the owner wants some |
| Depth of field, chromatic aberration | Not added | A stealth view must stay readable |

A second profile, `Atmosphere_Inspect` (no fog, fixed exposure, neutral grading, the overcast sky), is used only by kit turntables and LOD sheets.

In edit-mode captures exposure adaptation is instant, shader time is 0 and motion blur is off (`HD/Runtime/RenderPipeline/HDRenderPipeline.PostProcess.cs:1502-1505, 4552`). The PlayMode stills are the reference for what the player sees.

---

## 10. Performance budgets

Provisional, for 60 frames per second at 2560 x 1440 on the M4 Max, in play mode in the Editor. Counts and memory are asserted in tests from the first milestone. Frame time is logged first and asserted only after the M0 probe has validated the measurement method. The owner's displays are 1920 x 1080, so 1440p is an off-screen target.

| Metric (`ProfilerRecorder` counter name where one exists) | Budget (test fails above) | Warn at | Bottom-up estimate at M4 |
|---|---|---|---|
| GPU frame time, median | 11.0 ms | 9.5 ms | not measurable yet |
| `Triangles Count`, all passes | 8.0 M | 6.5 M | about 6.5 M |
| `Vertices Count` | 10.0 M | 8.0 M | about 7 M |
| `Draw Calls Count` (no Resident Drawer) | 4,000 | 3,000 | about 2,500 |
| `SetPass Calls Count` | 250 | 200 | |
| `Shadow Casters Count` | 2,500 | 2,000 | |
| `Texture Memory` above an empty scene | 1.0 GiB | 0.8 GiB | about 0.5 GiB with one uncompressed sky |
| `Mesh Memory` above an empty scene | 300 MiB | 220 MiB | about 110 MiB |
| Unique materials / unique meshes in the scene | 40 / 120 | 30 / 100 | about 21 / 85 |
| Textures at 4096 | 8 | 6 | 4 |
| Prefab instances / GameObjects in the scene | 3,500 / 14,000 | 3,000 / 12,000 | about 2,700 / 11,000 |

How the triangle estimate is built, for a mid-trail view. Unique visible triangles: 0.65 M in LOD0 trees (four young firs, one or two tall firs), 0.66 M in LOD1 (about 37 young and 8 tall), 0.61 M in LOD2 (about 130 young and 55 tall), 0.34 M in fern patches, 0.30 M in rocks and deadwood, 0.15 M in terrain: 2.7 M. Decals are enabled in the project, so HDRP forces a depth prepass and every opaque triangle is drawn at least twice (`HD/Documentation~/frame-settings-reference.md:25-27`): 5.4 M. Shadow cascades add about 1.1 M.

That is above the research's provisional 5 M. Holding 5 M would mean cutting the LOD0 foliage the player looks at. Frame time is the real gate. The estimate assumes arm A of experiment E1 (section 5.6.4). If E1 chooses a denser nearest level, the triangle, vertex and mesh-memory budgets are re-based on the measured numbers of the chosen arm in the same change (the three young-fir scans alone add about 105 MiB of mesh memory), and the frame-time budget stays where it is. If frame time is missed, turn these knobs in this order:

1. Per-camera frame-setting override that turns `FrameSettingsField.Decals` off (no decals exist in this scene; removes the forced prepass of opaque geometry). Measure as an A/B.
2. Shadow distance 80 to 50 m and three cascades.
3. LOD0 distances 14 and 12 m to 10 and 9 m.
4. LOD0 foliage targets times 0.7.
5. Fern patch LOD0 distance 16 to 12 m, cull 40 to 30 m.
6. With the owner's approval: GPU Resident Drawer and GPU occlusion culling (section 13, M6).

Known measurement caveats: per-pass GPU timing does not exist on macOS Metal; `metalAPIValidation: 1` in the project inflates CPU cost per draw call in the Editor; the Editor is slower than a player build. Report headless numbers as an optimistic bound and get one real number from a macOS development build (M6).

For a later PS5 port nothing here is a dead end: BC-family textures through default-platform settings only, TAA and motion vectors on, MeshRenderers that the Resident Drawer can take, a cell hierarchy, and a look that rests on sky, fog, SSAO and optionally baked probes. The foliage budgets would have to drop, which on real hardware means authored LODs.

---

## 11. Code architecture

### 11.1 Conventions

- All editor code lives in `PRJ/Assets/Game/Editor/Forest/`, namespace `Gamex.EditorTools`. It compiles into the existing `Gamex.EditorTools` assembly, because the asmdef at `Assets/Game/Editor/` covers sub-folders. **The asmdef is not edited**: everything used is in `UnityEditor`, `UnityEngine`, `Unity.AI.Navigation`, `Unity.RenderPipelines.Core.Runtime` or `Unity.RenderPipelines.HighDefinition.Runtime`. Nothing in version 1 needs anything else. (The two things outside those, APV `BakeAsync` and the Resident Drawer settings, belong to M5 and M6 and would be reached by reflection and by `SerializedObject` paths.)
- Static builder classes. Every entry point of version 1 is a `public static void` method with no parameters that carries a `[MenuItem]` under `Gamex/Forest/` (the ten entries are in 11.4: import, kit, scene, build all, audit, preset, three captures, probe), so each one works with `-executeMethod` and from the open Editor. Arguments come from the command line with the greybox pattern (`SceneCapture.cs:66-71`); a menu run has no arguments and uses the defaults (`ForestPaths.DefaultCaptureFolder` for output).
- Three compile traps, each confirmed by compiling the wrong and the right form: `PrefabUtility.InstantiatePrefab` returns `UnityEngine.Object` and needs a cast to `GameObject`; `MaterialSharedProperty` (for `DebugDisplaySettings.SetDebugViewCommonMaterialProperty`) lives in `UnityEngine.Rendering.HighDefinition.Attributes`; `OverrideTextureCompression` lives in `UnityEditor.Build`. A fourth, for M6 only: `HDRenderPipelineAsset.currentPlatformRenderPipelineSettings` returns a struct, so it is copied, modified and assigned back.
- A stage that produces something wrong **throws**. In batch mode an exception from `-executeMethod` makes Unity exit with return code 1 (`DOC/Manual/EditorCommandLineArguments.html`).
- Like the greybox, a menu-driven scene rebuild asks before replacing the open scene (`GreyboxSceneBuilder.cs:60-67`); batch mode does not ask.
- JSON is read with `JsonUtility` (`UnityEngine.JSONSerializeModule` is referenced by the editor assembly). `JsonUtility` cannot parse a top-level array, so the manifest text is wrapped: `"{\"assets\":" + text + "}"`. It has no dictionaries; the manifest uses arrays and fixed-field objects only.
- No runtime code is added. No file outside the lists below is created or edited, except `.gitignore` if the owner chooses the ignore policy (section 15).

### 11.2 The shared contract (written first, then frozen)

`Assets/Game/Editor/Forest/ForestContracts.cs`, owned by work package 1. Other packages code against it and never edit it. It holds every type that more than one package reads or writes, including the kit report that WP2 writes and WP5 reads. (This block and stubs of every signature of 11.4 were compiled together against the project's reference assemblies.)

```csharp
using System;
using System.IO;
using UnityEngine;

namespace Gamex.EditorTools
{
    /// <summary>Every path the forest build reads or writes. Asset paths are project-relative.</summary>
    public static class ForestPaths
    {
        public const string ThirdPartyRoot = "Assets/ThirdParty";
        public const string PolyHavenRoot = ThirdPartyRoot + "/PolyHaven";
        public const string ManifestPath = "Assets/Game/Editor/Forest/forest-asset-manifest.json";

        public const string ScenePath = "Assets/Game/Scenes/Forest.unity";
        public const string KitFolder = "Assets/Game/Scenes/ForestKit";
        public const string KitTextures = KitFolder + "/Textures";
        public const string KitMaterials = KitFolder + "/Materials";
        public const string KitTerrainLayers = KitFolder + "/TerrainLayers";
        public const string KitMeshes = KitFolder + "/Meshes";
        public const string KitPrefabs = KitFolder + "/Prefabs";
        public const string KitReportPath = KitFolder + "/kit-report.json";

        public const string DataFolder = "Assets/Game/Scenes/ForestData";
        public const string LightingFolder = DataFolder + "/Lighting";
        public const string BakeFolder = DataFolder + "/Bake";               // M5 only
        public const string TerrainDataPath = DataFolder + "/Terrain.asset";
        public const string NavMeshPath = DataFolder + "/NavMesh.asset";
        public const string SurfaceMapPath = DataFolder + "/SurfaceMap.png";
        public const string PlanPath = DataFolder + "/placements.json";
        public const string ReportPath = DataFolder + "/build-report.json";

        /// <summary>Unity may write bake output here. The builder never writes or deletes in it.</summary>
        public const string ReservedSceneFolder = "Assets/Game/Scenes/Forest";

        public const string StampsPath = "Library/GamexForest/stamps.json";
        public const string DefaultCaptureFolder = "Temp/Captures/forest";   // used when -captureOut is absent (menu runs)
        public const string FoliageProfilePath =
            "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipelineResources/FoliageDiffusionProfile.asset";
    }

    /// <summary>
    /// What an object is. Landmark is only the planner's random stream for hand-listed pieces:
    /// a placement's cls is never Landmark. RockFace is reserved (no prefab in version 1). Numbers are frozen.
    /// </summary>
    public enum ForestClass
    {
        Landmark = 0, TallFir = 1, YoungFir = 2, Boulder = 3, RockFace = 4, Log = 5,
        Stump = 6, Branch = 7, FernPatch = 8, FernClump = 9, GrassPatch = 10,
    }

    /// <summary>Terrain layer index = surface id. Frozen: gameplay will read it.</summary>
    public enum ForestSurface
    {
        Litter = 0, Humus = 1, MossyGround = 2, Gravel = 3, StonySoil = 4, Mud = 5, Rock = 6, DryNeedles = 7,
    }

    [Serializable]
    public struct ForestPlacement
    {
        public string prefabId;      // key for ForestPrefabs.Load
        public ForestClass cls;
        public Vector3 position;     // world. x, z from ForestScatter.Plan; y from ForestScatter.Ground (ground height - sink)
        public Quaternion rotation;  // world
        public float scale;          // uniform
        public float sink;           // metres the pivot sits below the ground; negative = raised
        public float clearance;      // metres, already scaled: horizontal radius of the collider's bounding circle; 0 = no collider
        public int tint;             // 0 or 1
        public int cellX, cellZ;     // 0..7
        public string landmark;      // "" for scattered instances
    }

    [Serializable]
    public sealed class ForestPlan
    {
        public int seed;
        public bool grounded;        // false until ForestScatter.Ground has filled every position.y
        public ForestPlacement[] placements = Array.Empty<ForestPlacement>();
    }

    /// <summary>Prefab ids. A tree id is the manifest object name without "_LOD0".</summary>
    public static class ForestPrefabIds
    {
        public static readonly string[] TallFirs = { "fir_tree_01_a", "fir_tree_01_b", "fir_tree_01_c" };
        public static readonly string[] YoungFirs =
            { "fir_sapling_medium_a", "fir_sapling_medium_b", "fir_sapling_medium_c" };
        public static readonly string[] Boulders =
        {
            "rock_moss_set_01_rock01", "rock_moss_set_01_rock02", "rock_moss_set_01_rock03",
            "rock_moss_set_01_rock04", "rock_moss_set_01_rock05", "rock_moss_set_01_rock06",
            "rock_moss_set_02_rock07", "rock_moss_set_02_rock08", "rock_moss_set_02_rock09",
            "rock_moss_set_02_rock10", "rock_moss_set_02_rock11", "rock_moss_set_02_rock12",
            "rock_moss_set_02_rock13",
        };
        public static readonly string[] Logs = { "dead_tree_trunk_02" };
        public static readonly string[] Stumps = { "tree_stump_01", "tree_stump_02" };
        public static readonly string[] Branches =
            { "dry_branches_medium_01_a", "dry_branches_medium_01_b", "dry_branches_medium_01_c" };
        public static readonly string[] FernPatches = { "fern_patch_a", "fern_patch_b", "fern_patch_c" };
        public static readonly string[] FernClumps = { "fern_02_a", "fern_02_b", "fern_02_c", "fern_02_d" };
        public static readonly string[] GrassPatches = { "grass_patch_a", "grass_patch_b" };
    }

    /// <summary>One LOD level of one prefab, as built. Written by WP2, read by WP5.</summary>
    [Serializable]
    public sealed class ForestKitLodReport
    {
        public int triangles, vertices;
        public float untilDistance;          // metres
        public bool isScan;                  // a tree level that is the untouched scan (experiment E1)
        public int pieces;                   // trees: foliage pieces kept
        public float keepRatio, pieceScale;  // trees: share of twig triangles kept; scale applied to the kept pieces
        public float coverage;               // trees: silhouette coverage against the source at this level's start distance
    }

    [Serializable]
    public sealed class ForestKitPrefabReport
    {
        public string prefabId;
        public Vector3 boundsMin, boundsMax;     // LOD0 as measured, Unity space
        public string mirror;                    // "x" | "z" | "xz" | "none" | "symmetric", against the manifest's source-space bounds
        public float colliderRadius;             // horizontal bounding radius of the collider at scale 1; 0 = no collider
        public int sourcePieces;                 // trees
        public float medianPieceTriangles;       // trees
        public bool usedSpatialFallback;         // trees
        public string[] triplanarMaterials = Array.Empty<string>();   // materials replaced by their noUvFallback variant
        public float bakeSeconds;
        public ForestKitLodReport[] lods = Array.Empty<ForestKitLodReport>();
    }

    [Serializable]
    public sealed class ForestKitReport
    {
        public string unityVersion, kitSpecHash;
        public ForestKitPrefabReport[] prefabs = Array.Empty<ForestKitPrefabReport>();

        public ForestKitPrefabReport Find(string prefabId)
        {
            foreach (ForestKitPrefabReport prefab in prefabs)
                if (prefab.prefabId == prefabId) return prefab;
            return null;
        }

        /// <summary>Null when the kit has not been built.</summary>
        public static ForestKitReport Load()
        {
            return File.Exists(ForestPaths.KitReportPath)
                ? JsonUtility.FromJson<ForestKitReport>(File.ReadAllText(ForestPaths.KitReportPath))
                : null;
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ForestPaths.KitReportPath));
            File.WriteAllText(ForestPaths.KitReportPath, JsonUtility.ToJson(this, true));
        }
    }

    /// <summary>Command-line arguments, read the way SceneCapture does.</summary>
    public static class ForestArgs
    {
        public static string Value(string name, string fallback = null)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : fallback;
        }

        public static bool Flag(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;
    }
}
```

**Step zero** (the owner of work package 1, one commit before anyone else starts): create every version-1 file of 11.3 with the public signatures of 11.4 and bodies that throw `NotImplementedException`, so the assembly compiles. After that commit each engineer edits only the files of their own package. The two deferred files of WP4 are not created at step zero; they are new files when their milestone comes, so adding them edits nothing that is frozen.

### 11.3 File ownership plan

Five work packages. No file belongs to two packages. All `.cs` paths are relative to `Assets/Game/Editor/Forest/` unless they start with another folder.

| Package | Files (version 1) |
|---|---|
| **WP1 Assets in**: manifest, download and measuring tools, import rules, texture packing, materials | `ForestContracts.cs`, `ForestManifest.cs`, `forest-asset-manifest.json` (a copy of `asset-manifest.json`), `ForestAssetPostprocessor.cs`, `ForestTexturePacker.cs`, `ForestMaterials.cs`, `ForestImport.cs`, `ForestStamps.cs`, `Tools/forest/fetch_assets.py`, `Tools/forest/fbx_objects.py`, `Assets/ThirdParty/PolyHaven/THIRD_PARTY_ASSETS.md` |
| **WP2 Kit**: mesh tools, foliage thinner, tree baker, prefabs | `ForestKitSpec.cs`, `ForestMeshTools.cs`, `ForestFoliageThinner.cs`, `ForestTreeBaker.cs`, `ForestPrefabs.cs` |
| **WP3 World**: layout, fields, terrain, scatter, scene assembly, player, bounds, NavMesh, build entry points | `ForestLayout.cs`, `ForestFields.cs`, `ForestTerrain.cs`, `ForestScatter.cs`, `ForestWorldRig.cs`, `ForestSceneBuilder.cs`, `ForestBuild.cs` |
| **WP4 Light**: the preset, sky calibration, camera | `ForestLightingPresets.cs`, `ForestSky.cs`, `ForestLighting.cs`. Deferred, not in version 1: `ForestProbeBake.cs` (M5), `ForestProjectSettings.cs` (M6) |
| **WP5 Proof**: audit, captures, probe run, every test | `ForestAudit.cs`, `ForestImageStats.cs`, `ForestCapture.cs`, `ForestProbe.cs`, `ForestBudgets.cs`, `Assets/Game/Tests/ForestEditMode/**` (asmdef `Gamex.Tests.Forest.EditMode` and tests), `Assets/Game/Tests/ForestPlayMode/**` (asmdef `Gamex.Tests.Forest.PlayMode` and tests) |

That is 27 editor C# files, two Python tools and two test assemblies.

Generated files have one writer each:

| File | Only writer |
|---|---|
| `ForestKit/kit-report.json` | WP2, through `ForestKitReport.Save` |
| `ForestData/placements.json` | WP3, `ForestSceneBuilder.Build` |
| `ForestData/build-report.json` | WP5, `ForestAudit.Run`. Nothing else writes it: a stage that wants a fact in the report makes it derivable from the scene or the assets and calls `ForestAudit.Run(false)` |
| `Library/GamexForest/stamps.json` | WP1, `ForestStamps` |
| `probe.json` and capture sidecars | WP5 |
| The manifest copy in the project | WP1 (by hand, or `fbx_objects.py --write`) |

Tunables live with their owner: LOD targets and distances in `ForestKitSpec` (WP2); coordinates, densities and the seed in `ForestLayout` (WP3); preset values in `ForestLightingPresets` (WP4); budgets in `ForestBudgets` (WP5). A value another package needs is exposed through a signature below, not copied. Unit tests of WP2's pure code (thinner, mesh tools), of WP3's (layout, scatter, slope limits) and of WP4's (sky integral) are written by WP5 against the public surface below; a package that needs a helper tested exposes it there.

### 11.4 Public surface of each package

Signatures are exact. Anything not listed is private to its package.

#### WP1 Assets in

```csharp
[Serializable] public sealed class ForestAssetFile { public string url, relPath, md5, map; public long bytes; }
[Serializable] public sealed class ForestMaterialMaps { public string basecolor, diffuse, normal, arm, alpha; }
[Serializable] public sealed class ForestMaterialEntry
{
    public string name, surface, sameAs, noUvFallback;   // surface: "opaque" | "foliage"; sameAs: reuse another material;
    public float alphaCutoff; public ForestMaterialMaps maps;   // noUvFallback: material whose triplanar variant replaces this one without usable UVs
}
[Serializable] public sealed class ForestSubMeshEntry { public string material; public int triangles, vertices; public bool hasUv0; }
[Serializable] public sealed class ForestObjectEntry
{
    public string name; public float[] boundsMin, boundsMax; public int triangles; public ForestSubMeshEntry[] subMeshes;
}
[Serializable] public sealed class ForestAssetEntry
{
    public string id, source, kind, role, resolution, milestone, license, preset;   // kind: "model" | "texture" | "hdri"
    public string objectsSource, boundsSpace, note;                                   // models: "fbx" | "gltf"; "source-rh-yup"
    public int terrainLayer; public float tileSizeMeters; public int maxTextureSize;  // terrainLayer is valid only when kind == "texture"
    public ForestAssetFile[] files; public ForestMaterialEntry[] materials; public ForestObjectEntry[] objects; public string[] fbxNodes;
}

public static class ForestManifest
{
    public static ForestAssetEntry[] All { get; }                    // cached; throws if the manifest is missing or malformed
    public static ForestAssetEntry Find(string id);                   // null when absent
    public static ForestObjectEntry FindObject(string objectName);    // searches every model
    public static ForestObjectEntry FindObjectForPrefab(string prefabId);   // tries prefabId, then prefabId + "_LOD0"; null for patch prefabs
    public static ForestAssetEntry FindOwner(ForestObjectEntry entry);       // the model an object belongs to
    public static ForestMaterialEntry FindMaterial(string materialName);
    public static bool IsDownloaded(ForestAssetEntry entry);          // every file exists with the manifest byte size
    public static bool IsMeasured(ForestAssetEntry entry);            // objectsSource == "fbx"
    public static string AssetPath(string relPath);                   // "Assets/ThirdParty/" + relPath
    public static string DiskPath(string relPath);                    // absolute path on disk
    public static Bounds SourceBounds(ForestObjectEntry entry);       // exactly the manifest numbers: right-handed source space, metres
    public static Bounds ExpectedBounds(ForestObjectEntry entry);     // Unity space ASSUMING the importer mirrors X. Use only its size and Y range
    public static float FootprintRadius(ForestObjectEntry entry);     // horizontal radius about the pivot that contains the box; same under any mirror
}

public sealed class ForestAssetPostprocessor : AssetPostprocessor     // sections 5.2 and 5.3; no public members beyond the Unity callbacks

public static class ForestTexturePacker
{
    public static int PackAll(bool force);                             // writes into ForestPaths.KitTextures; returns files written
    public static string MaskPath(string materialName);                // ".../<material>__mask.png"
    public static string FoliageBaseColorPath(string materialName);    // ".../<material>__basecolor.png"
    public static string TerrainMaskPath(string layerId);              // ".../<layer>__tmask.png"
    public static Color MeanFoliageColor(string materialName);         // alpha-weighted mean of the atlas, linear
    public static Color32[] ReadPixels(string diskPath, out int width, out int height);
    public static void WritePng(string diskPath, Color32[] pixels, int width, int height);
}

public static class ForestMaterials
{
    public static void BuildAll(bool force);                           // materials, terrain layers and terrain material for every downloaded entry
    public static Material Get(string materialName, int tint = 0);     // resolves sameAs; null when not built
    public static Material GetTriplanar(string materialName);          // "<name>__triplanar"; null when not built
    public static Material GetCrownCore(string twigMaterialName);
    public static TerrainLayer[] GetTerrainLayers();                   // ordered by terrainLayer; length 0, 4 or 8
    public static Material GetTerrainMaterial();
    public static bool IsFoliage(string materialName);
}

public static class ForestStamps
{
    public static bool IsCurrent(string product, string inputHash);    // product: any stable key, for example an asset path
    public static void Set(string product, string inputHash);
    public static string Hash(params string[] parts);
}

public static class ForestImport
{
    [MenuItem("Gamex/Forest/1 Import Assets")]
    public static void Run();   // verify size and md5 of present files, pack, AssetDatabase.Refresh, BuildAll materials. -forestForce re-does everything
}
```

`Tools/forest/fetch_assets.py <manifest> <project root> [--milestone M1..M6] [--id <asset id>]`: Python standard library only, run with `python3 -I`, shells out to `curl` as in 5.1. `--milestone Mn` fetches every entry up to and including that milestone; the default is M4, so the deferred skies and rock faces are fetched only when asked for. After each model it runs the measuring tool below and stops on a mismatch.

`Tools/forest/fbx_objects.py <file.fbx> <manifest> <asset id> [--write]`: Python standard library only, run with `python3 -I`. It reads the binary FBX directly: walk the node records (32-bit offsets below version 7500, 64-bit from 7500); take `UnitScaleFactor` from `GlobalSettings`; for each `Model` its name, `Lcl Rotation` and `Lcl Scaling`; for its `Geometry` the zlib-compressed `Vertices` and `PolygonVertexIndex` arrays, `LayerElementMaterial` and the number of `LayerElementUV` layers. With `Lcl Rotation` (-90, 0, 0), a source-space point is `(x, z, -y) * Lcl Scaling / 100 * UnitScaleFactor` in metres; any other rotation is an error to be looked at, not guessed. It prints, per object, triangles, triangles per material, UV layers and bounds, compares them with the manifest (2 % of the largest dimension, at least 1 cm), and exits 0 on a match, 2 on a mismatch. On a match it sets `objectsSource` to `"fbx"` if it was `"gltf"`. `--write` is the explicit override after a mismatch: it stores the measured `objects[]` values and sets the flag. Use `array.array` for the big arrays: `fir_tree_01.fbx` holds 5.4 million vertices in one object. A working reference that reproduced the manifest on seven files is `scripts/verify/fbx_objects.py` next to this spec; it is a starting point, not project code.

May assume from others: nothing.

#### WP2 Kit

```csharp
public static class ForestKitSpec        // every constant of section 5.6: targets, distances, switches
{
    public static bool UseUnityGenerator;   // default false
    public static bool UseCrossFade;        // default true
    public static bool ThinByGroup;         // default false
    public static int CrownCoreFromLod;     // default 99 (off)
    public static float LodDistanceScale;   // default 1
    public static float LogPitchDegrees;    // default 0 (section 7.3)
    public static string[] ForceTriplanar;  // default empty: materials that take their noUvFallback even with UVs (section 5.5)
    public static float ScanLodDistance(string prefabId);   // metres; 0 = no scan level (the default until experiment E1 decides)
    public static int LodCount(string prefabId);            // 1 to 4, scan level included
    public static float[] LodDistances(string prefabId);    // metres, one "until" per level, nearest first; the last is also the cull distance (PositiveInfinity = never culled)
    public static int TriangleTarget(string prefabId, int lod);   // expected triangles of that level's mesh (foliage + wood); 0 when unknown
    public static float ClearanceRadius(string prefabId);   // metres at scale 1 (section 5.6.6); 0 for prefabs without a collider. Needs no built prefab
}

public static class ForestMeshTools
{
    public sealed class MeshData
    {
        public Vector3[] positions; public Vector3[] normals; public Vector4[] tangents;   // tangents may be null
        public Vector2[] uv0;                                                               // may be null
        public int[][] subMeshes;                                                           // triangle indices per sub-mesh
    }
    public static MeshData Read(Mesh mesh);
    public static void Transform(MeshData data, Matrix4x4 matrix);
    public static MeshData Extract(MeshData source, params int[] subMeshIndices);          // compact copy
    public static MeshData Merge(params MeshData[] parts);                                  // one sub-mesh per part
    public static MeshData ClusterDecimate(MeshData source, float cellSize);
    public static MeshData DecimateToTarget(MeshData source, int targetTriangles);         // clustering or the Unity generator, per ForestKitSpec
    public static Mesh ToMesh(MeshData data, string name, bool recalculateTangents);
    public static Bounds BoundsOf(MeshData data);
    public static int TriangleCount(MeshData data);
    public static bool MatchesSourceBounds(Bounds measured, Bounds source, out string mirror);   // the rule of section 5.6.1; pure
    public static bool HasUsableUv(MeshData data, int subMeshIndex);                        // the rule of section 5.5; pure
    public static float TrunkRadius(MeshData wood, float minHeight, float maxHeight);
    public static float CoverageRatio(MeshData reference, MeshData candidate, float cellSize);   // silhouette cells covered, mean of two side views
}

public static class ForestFoliageThinner
{
    public sealed class Pieces
    {
        public int count; public int[] pieceOfTriangle; public float[] area; public Vector3[] centroid;
        public Vector3[] anchor; public float[] key; public float totalArea; public bool usedSpatialFallback;
    }
    public static Pieces FindPieces(Vector3[] positions, int[] triangles, Vector3 trunkAxisOrigin, int seed);
    public static ForestMeshTools.MeshData Thin(ForestMeshTools.MeshData foliage, Pieces pieces,
        int targetTriangles, float maxScale, bool flattenToQuads);
    public static ForestMeshTools.MeshData BuildCrownCore(ForestMeshTools.MeshData foliage, Vector3 trunkAxisOrigin,
        int heightBands, int sectors, float radiusFraction);
}

public static class ForestTreeBaker
{
    public static void Bake(string prefabId, bool force);   // meshes and prefab for one tree; updates the kit report; throws on a failed bounds or coverage gate
}

public static class ForestPrefabs
{
    [MenuItem("Gamex/Forest/2 Build Kit")]
    public static void BuildAllFromMenu();                  // BuildAll(ForestArgs.Flag("-forestForce"))
    public static void BuildAll(bool force);                // every prefab whose model is downloaded, measured and has materials; others are skipped with a log line
    public static bool Exists(string prefabId);
    public static string PrefabPath(string prefabId);
    public static GameObject Load(string prefabId);         // null when not built
    public static int LodCount(string prefabId);            // of the built prefab; 0 when not built
    public static Mesh LoadLodMesh(string prefabId, int lod);
    public static void ApplyTint(GameObject instance, int tint);
}
```

Kit report (`ForestPaths.KitReportPath`, type `ForestKitReport` of the contract): per prefab, measured bounds, mirror, collider radius, triangles and vertices per level; for trees also piece count, median piece triangles, keep ratio and scale per level, coverage ratios, bake time. `BuildAll` loads the existing report, replaces the entries of the prefabs it rebuilt and saves it.

May assume from others: `ForestManifest` (object names, bounds, sub-mesh materials, `IsMeasured`), `ForestMaterials.Get*`, `ForestStamps`, the contract.

#### WP3 World

```csharp
public static class ForestLayout
{
    public const int Seed = 1993;
    public const float TileSize = 256f, HeightRange = 60f, TerrainBaseY = -20f;
    public const float HalfExtentX = 65f, HalfExtentZ = 75f, CornerRadius = 30f;
    public const float CorridorClearance = 2.5f, CorridorAgentRadius = 2.0f, SpawnClearance = 5f;
    public static int TerrainLayerCount;                      // 8; set 4 for the fallback
    public static int AlphamapResolution;                     // 2048; 1024 is the fallback
    public static readonly Vector2 SpawnXZ;                   // (0, -62)
    public const float SpawnYaw = 0f;
    public static readonly Vector2[] Trail, WestTrail, EastTrail, Gully;   // control points of section 3
    public struct Landmark { public string name; public Vector2 xz; public float radius; }
    public static readonly Landmark[] Landmarks;
    public struct Viewpoint { public string id; public Vector3 position; public Vector3 euler; public float fieldOfView; public bool orthographic; public float orthographicSize; public bool heightIsAboveGround; }
    public static readonly Viewpoint[] Viewpoints;            // section 12.2
    public static float SignedDistanceToBoundary(Vector2 xz); // negative inside
    public static Vector2 SampleSpline(Vector2[] controlPoints, float t01);   // arc-length parameterised Catmull-Rom
    public static float SplineLength(Vector2[] controlPoints);
    public static float DistanceToSpline(Vector2[] controlPoints, Vector2 xz);
    public static float DistanceToAnyTrail(Vector2 xz);       // smallest distance to the trail and both game trails
    public static Vector2 TrailPoint(float metresFromSpawn, float metresToTheRight);   // trail coordinates of section 3.5
}

public sealed class ForestFields              // plain arrays; no Unity objects
{
    public int resolution;                    // 1025
    public float[,] height;                   // world metres, [z, x]
    public float[,] slopeDegrees, thicket, trailDistance, gullyDistance, boundaryDistance;
    public float WorldToIndex(float worldCoordinate);
    public float HeightAt(float x, float z);  // bilinear
    public float SlopeAt(float x, float z);
    public Vector3 NormalAt(float x, float z);
}

public static class ForestTerrain
{
    public static ForestFields ComputeFields(int seed);                               // steps 1 to 7 of section 6.2
    public static void AddRootMounds(ForestFields fields, ForestPlan plan);           // step 8; also refreshes slopeDegrees
    public static Terrain Create(ForestFields fields, ForestPlan plan, Transform parent);   // TerrainData asset, layers, alphamaps, surface map, GameObject
}

public static class ForestScatter
{
    public static ForestPlan Plan(ForestFields fields, int seed);                     // pure and deterministic; position.y = 0, grounded = false
    public static void Ground(ForestPlan plan, ForestFields fields);                  // position.y = fields.HeightAt(x, z) - sink; grounded = true
}

public static class ForestWorldRig
{
    public static int Instantiate(ForestPlan plan, Transform environment);            // returns instances created; skips classes whose prefabs are missing; throws if !plan.grounded
    public static void BuildBounds(Transform environment);
    public static void BuildGameplayAnchors(Terrain terrain);                         // the Gameplay root of section 3.7
    public static void BakeNavMesh(GameObject environment);
    public static GameObject BuildPlayer(Vector3 position, float yaw);                // returns the Player root
    public static GameObject BuildSession(GameObject player);
}

public static class ForestSceneBuilder
{
    public const string ScenePath = ForestPaths.ScenePath;
    [MenuItem("Gamex/Forest/3 Rebuild Forest Scene")]
    public static void Build();        // -forestSeed, -forestPreset, -forestLayers 4|8
}

public static class ForestBuild
{
    [MenuItem("Gamex/Forest/Build All")]
    public static void BuildAll();     // ForestImport.Run, ForestPrefabs.BuildAll, ForestSceneBuilder.Build
}
```

May assume from others: `ForestPrefabs.Load`, `Exists`, `ApplyTint`; `ForestKitSpec.ClearanceRadius`; `ForestMaterials.GetTerrainLayers`, `GetTerrainMaterial`; `ForestManifest.FindObjectForPrefab`, `SourceBounds` (only `min.y`, for the rock sink), `ExpectedBounds` (size only) and `FootprintRadius` (before any prefab exists); `ForestLighting.Build` and `ConfigureCamera`; `ForestAudit.Run`; `ForestImport.Run`.

#### WP4 Light

```csharp
public static class ForestLightingPresets
{
    public const string Default = "OvercastMist";
    public static readonly string[] Names = { "OvercastMist" };   // M5 appends the presets of Appendix C

    /// <summary>The values of section 8.2. Read-only for everyone outside WP4.</summary>
    public sealed class Preset
    {
        public string name, skyId;                 // skyId: the manifest id of the preset's HDRI
        public float skyLux, skyRotation;
        public float keyLux, keyTemperature, keyAngularDiameter, keyVolumetricDimmer;
        public float keyElevation, keyBearing;     // the light travels toward keyBearing; its source stands at keyBearing + 180
        public float shadowDistance; public int cascades; public Vector3 cascadeSplits;
        public float fogMeanFreePath, fogBaseHeight, fogMaximumHeight, fogAnisotropy, fogDepthExtent; public Color fogAlbedo; public bool fogHighQuality;
        public bool fixedExposure; public float exposureFixed, exposureMin, exposureMax, exposureCompensation;
        public float whiteBalanceTemperature, saturation, contrast, bloomIntensity, grainIntensity, vignetteIntensity; public string grainType;
    }
    public static Preset Get(string name);                  // null for an unknown name
    public static string SkyId(string preset);              // Get(preset).skyId
}

public static class ForestSky
{
    public struct Calibration { public float upperHemisphereLux; public Vector3 luxColor; public float lowerHemisphereLux; public float brightestAzimuthDegrees; }
    public static Calibration Integrate(Color[] latLongPixels, int width, int height);   // pure; rows bottom to top
    public static Calibration Calibrate(string skyId);       // loads Source~/<id>__sky1k.exr
    public static Cubemap LoadCubemap(string skyId);         // null when not downloaded
}

public static class ForestLighting
{
    public static GameObject Build(string activePreset);     // creates the "Lighting" root in the active scene, one child per available preset, and the profile assets
    public static void ConfigureCamera(Camera camera);       // section 9.1
    public static bool ApplyPreset(string preset);           // open scene; false when the preset is absent
    public static string ActivePreset();                     // "" when no forest lighting is in the scene
    public static VolumeProfile InspectProfile();            // Atmosphere_Inspect, created on demand
    [MenuItem("Gamex/Forest/Preset/Apply From Arguments")]
    public static void ApplyPresetFromArgs();                // -forestPreset <name>; saves the scene
}
```

`brightestAzimuthDegrees` is a compass bearing in image terms: 0 at the centre column of the lat-long image, increasing to the right (Appendix C says what M5 does with it).

Not in version 1: `ForestProbeBake` (M5, Appendix C) and `ForestProjectSettings` (M6).

May assume from others: `ForestManifest` (sky files), the scene hierarchy of section 3.7, `Camera.main` being the player camera.

#### WP5 Proof

```csharp
public static class ForestBudgets { /* the constants of section 10 */ }

public static class ForestImageStats
{
    public struct Stats { public float meanLuminance, medianLuminance, shareBlack, shareWhite, meanR, meanG, meanB, shareMagenta, shareCyan; }
    public static Stats Compute(Color32[] pixels);
    public static float Rmse(Color32[] a, Color32[] b);
}

public static class ForestAudit
{
    [Serializable] public sealed class Finding { public string severity, check, message; }   // severity: "error" | "warning" | "info"
    [Serializable] public sealed class Report { public bool passed; public string apv, preset; public int seed; public Finding[] findings; /* plus counts, totals and metrics */ }
    public static Report Run(bool throwOnError);             // audits the open scene; the ONLY writer of ForestPaths.ReportPath
    public static Report AuditKit();                         // materials, textures, prefabs; no scene needed
    [MenuItem("Gamex/Forest/Audit Open Scene")]
    public static void RunFromMenu();
}

public static class ForestCapture
{
    [MenuItem("Gamex/Forest/5 Capture Quick Set")]
    public static void Capture();                            // -captureOut DIR, -captureSet quick|full|debug, -forestPreset NAME, -captureScene PATH; menu default: quick
    [MenuItem("Gamex/Forest/5 Capture Full Set")]
    public static void CaptureFull();                        // the full and debug sets; for menu runs, where arguments cannot be passed
    [MenuItem("Gamex/Forest/6 Capture Kit")]
    public static void CaptureKit();                         // turntables and LOD sheets of every prefab; -captureOut DIR
    public static Texture2D Render(Camera camera, int width, int height, int frames);   // the capture loop of section 12.2
}

public static class ForestProbe
{
    [MenuItem("Gamex/Forest/0 Run Probe")]
    public static void Run();                                // section 13, M0; -captureOut DIR; writes probe.json
}
```

Without `-captureOut`, captures and the probe write to `ForestPaths.DefaultCaptureFolder` (`Temp/Captures/forest`, outside `Assets`).

May assume from others: everything public above, the scene hierarchy of 3.7, the prefab contract of 5.6.6.

### 11.5 Stage order and re-runs

`ForestSceneBuilder.Build` does, in this order:

1. Ask (unless batch mode), then `EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single)`.
2. Create `ForestData` if missing. Never delete folders.
3. `fields = ForestTerrain.ComputeFields(seed)`. Heights are final except for root mounds.
4. `plan = ForestScatter.Plan(fields, seed)`. It decides x, z, rotation, scale, tint, sink and clearance. It reads slopes and normals from `fields` and writes no height.
5. `ForestTerrain.AddRootMounds(fields, plan)`. Heights are now final. Assert the slope limits again.
6. `ForestScatter.Ground(plan, fields)`: `position.y = fields.HeightAt(x, z) - sink` for every placement, mounds included. Write `placements.json`.
7. Create `Environment`; `terrain = ForestTerrain.Create(fields, plan, environment)`.
8. `ForestWorldRig.Instantiate(plan, environment)` (it throws on a plan that is not grounded); `BuildBounds`; `BuildGameplayAnchors`.
9. `Physics.SyncTransforms()`; `ForestWorldRig.BakeNavMesh(environment)`: the greybox call unchanged (`NavMeshSurface` on `Environment`, `CollectObjects.Children`, `NavMeshCollectGeometry.PhysicsColliders`, `BuildNavMesh()`, `AssetDatabase.CreateAsset(surface.navMeshData, ForestPaths.NavMeshPath)`: `GreyboxSceneBuilder.cs:284-292`). It collects the terrain collider, trunk capsules and collision meshes.
10. `player = ForestWorldRig.BuildPlayer(...)`; `BuildSession(player)`; `ForestLighting.ConfigureCamera(...)`.
11. `ForestLighting.Build(preset)`.
12. `EditorSceneManager.SaveScene(scene, ScenePath)`; append the scene to `EditorBuildSettings.scenes` if it is missing (never remove or reorder: `StealthLoopTests` loads `Greybox` by name, `PRJ/Assets/Game/Tests/PlayMode/StealthLoopTests.cs:29`); `AssetDatabase.SaveAssets()`.
13. `ForestAudit.Run(throwOnError: true)`, the only writer of `build-report.json`.

The order of steps 4 to 6 is what keeps the grounding audit honest: the first plan fixed every pivot height in step 4 and raised the ground under each tall fir by 6 to 14 cm in step 5, against an audit tolerance of 3 cm.

| Stage | Skips itself when | Never does |
|---|---|---|
| Fetch and measure (outside Unity) | The file exists with the manifest's size and md5 | Assume ranges; hand an unmeasured FBX to the kit build |
| `ForestImport.Run` | A product's stamp matches source md5 + tool version | Delete or rename a source asset |
| `ForestPrefabs.BuildAll` | A product's stamp matches source md5 + `ForestKitSpec` hash + tool version | Reference source models from prefabs |
| `ForestSceneBuilder.Build` | Never (it takes seconds and imports nothing) | Touch `Assets/Game/Scenes/Forest/` or `ForestData/Bake` |
| Capture, tests | Never | Write inside `Assets` (output goes to `Temp/` or `-captureOut`) |

Commands (Editor closed):

```
U=/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity
P=/Users/william/Documents/GitHub/gamex
python3 -I $P/Tools/forest/fetch_assets.py $P/Assets/Game/Editor/Forest/forest-asset-manifest.json $P --milestone M2
$U -batchmode -projectPath $P -executeMethod Gamex.EditorTools.ForestBuild.BuildAll -logFile $P/Logs/forest-build.log -timestamps -quit
$U -batchmode -projectPath $P -executeMethod Gamex.EditorTools.ForestCapture.Capture -captureSet full -captureOut $P/Temp/Captures/forest -logFile $P/Logs/forest-capture.log -quit
$U -batchmode -projectPath $P -runTests -testPlatform EditMode -assemblyNames Gamex.Tests.Forest.EditMode -testResults $P/Temp/forest-editmode.xml -logFile $P/Logs/forest-editmode.log
$U -batchmode -projectPath $P -runTests -testPlatform PlayMode -assemblyNames Gamex.Tests.Forest.PlayMode -testResults $P/Temp/forest-playmode.xml -logFile $P/Logs/forest-playmode.log
$U -batchmode -projectPath $P -runTests -testPlatform PlayMode -assemblyNames Gamex.Tests.PlayMode -testResults $P/Temp/greybox-playmode.xml -logFile $P/Logs/greybox-playmode.log
```

The last line is the existing greybox suite, and it is a separate run on purpose. `StealthLoopTests` has no `[Category]` attribute and may not be edited, so a run filtered with `-testCategory Forest` would silently skip it. The runs are therefore selected by assembly (`-assemblyNames`, `PRJ/Library/PackageCache/com.unity.test-framework@7a3849e09bd0/UnityEditor.TestRunner/CommandLineTest/SettingsBuilder.cs:51`; `-testCategory` is at line 47 and still works for picking forest tests by hand). All three result files must be green for a milestone gate. (One unfiltered PlayMode run, which executes both PlayMode assemblies, is an acceptable substitute for the last two lines.)

Never `-nographics` (captures and the sky need a graphics device). Never `-quit` with `-runTests`.

**The project lock.** Batch runs fail while the owner's Editor has the project open. Every version-1 entry point has a menu item (11.4), so the whole pipeline, the captures and the M0 probe included, can run inside the open Editor; imports then block it for minutes. Tests are run from the Test Runner window in that case: all of `Gamex.Tests.Forest.EditMode`, `Gamex.Tests.Forest.PlayMode` and `Gamex.Tests.PlayMode`. The download and measuring tools never need Unity.

### 11.6 The player rig and the greybox

`ForestWorldRig.BuildPlayer` **copies** the set-up of `GreyboxSceneBuilder.BuildPlayer` and `BuildSession` (`GreyboxSceneBuilder.cs:294-333`), because those methods are private and making them reachable would mean editing the greybox builder:

- `Player`: `CharacterController` (height 1.8, radius 0.35, centre (0, 0.9, 0)); child `CameraPivot` at (0, 1.65, 0) tagged `MainCamera` with `Camera` (near 0.05, far 400, field of view 70), `HDAdditionalCameraData`, `AudioListener`; grandchild `HoldPoint` at (0.35, -0.3, 0.7); `PlayerInputReader`, `PlayerController`, `PlayerInteractor`; fields `cameraPivot` and `holdPoint` wired through `SerializedObject` as the greybox does.
- `Game`: `GameSession` with `player` wired. No `PrototypeHud`, so the view is clean for judging realism.
- The player is placed by setting the root transform (terrain height + 0.05). `PlayerController.Teleport` cannot be used from the builder: it needs a field that `Awake` assigns, and `Awake` does not run in edit mode. The controller reads its yaw from the transform when play starts (`PlayerController.cs:56-61`).

How the greybox stays untouched:

- No existing file is edited: not `GreyboxSceneBuilder.cs`, `SceneCapture.cs`, `Greybox.unity`, `StealthLoopTests.cs`, either existing asmdef, or anything under `Assets/Game/Scripts`.
- The forest scene is appended to Build Settings; `Greybox` stays at index 0. Rebuilding the greybox resets the list to itself (`GreyboxSceneBuilder.cs:105`); that is harmless because forest tests load their scene by path.
- No project setting changes in version 1, so the greybox renders exactly as before.
- Tests: `PlayerRig_MatchesGreybox` and `Greybox_StillFirstInBuildSettings` (section 12.3); the existing `StealthLoopTests` is part of every milestone's gate, as its own test run (section 11.5).

---

## 12. Verification

The gate for every milestone: build exits 0, the forest EditMode suite green, captures written with zero error pixels and luminance in range, the forest PlayMode suite green, the existing `StealthLoopTests` green in its own run (section 11.5), then a person looks at the capture set (and at the kit set when a new asset arrived). At M2, M3 and M4 that person is the owner.

### 12.1 In-builder audit (every build)

`ForestAudit.Run` executes after the scene is saved and writes `ForestData/build-report.json` (counts per class, totals, timings, findings, preset, seed, and `apv`, which is `"none"` throughout version 1). It is the only code that writes that file. Any finding of severity "error" throws. `ForestCapture.Capture` refuses to run on a failed report unless `-forestAllowFailedAudit` is passed.

| Check | Severity | Catches |
|---|---|---|
| Every renderer material: not null; shader is none of `Hidden/InternalErrorShader`, `Hidden/HDRP/FallbackError`, `Hidden/HDRP/MaterialError`; `material.GetTag("RenderPipeline", false, "") == "HDRenderPipeline"`; `!ShaderUtil.ShaderHasError(shader)` | error | Pink materials |
| Foliage materials: `_DiffusionProfileHash != 0`, or material type Standard | error | Magenta foliage |
| Used texture slots are non-null; masks linear; normals typed `NormalMap` | error | Wrong-looking surfaces |
| Each prefab's `LOD0` bounds against the manifest with the mirror-independent rule of 5.6.1 (`ForestMeshTools.MatchesSourceBounds`); for a tree with a scan level, the first thinned level too | error | Scale 100, lying trees, pack offsets |
| Each placement of `placements.json`: `position.y` within 3 cm of `terrain.SampleHeight(position) + terrain.transform.position.y - sink`, with the sink read from the placement. Each instance (matched by the index in its name) sits at its placement's position. For trees, the pivot is not above the terrain at eight points on the trunk-base circle | error | Floating or buried objects; a plan grounded on stale heights |
| Spawn: on the trail, 0.05 m above the terrain; for every placement with `clearance > 0`, `distance(pivot, spawn) - clearance >= 5 m` | error | A player who starts inside a rock or falls |
| Trail slope at most 12 degrees; core slope at most 27 | error | Sprint losing the ground |
| Exactly one active global `Volume`; exactly one active shadow-casting directional light | error | Double exposure, shadow conflicts |
| Sky lux value finite and positive; exposure mode and limits equal to `ForestLightingPresets.Get(activePreset)` | error | Black or white frames |
| No renderer under `Environment` references a mesh or material from `Assets/ThirdParty` | error | A multi-million-triangle source mesh in the scene |
| No renderer has a property block (`Renderer.HasPropertyBlock()`); no `BatchingStatic` flag | error | Losing Resident Drawer compatibility |
| Instance counts and scene totals inside the budgets of section 10 | error | Runaway density |
| Corridors, analytic (always): for every placement with `clearance > 0`, `ForestLayout.DistanceToAnyTrail(pivot) - clearance >= 2.0 m`. The planner's own rule is 2.5 m, so this passes with 0.5 m to spare | error | A blocked lane |
| Corridors, physical (only when probe P12 found edit-mode physics queries reliable; otherwise the same check runs in the PlayMode suite): a capsule of radius 2.0 m and height 4 m, tested with `Physics.CheckCapsule` every metre along the trail and both game trails, 0.3 m above the ground, touches no collider except the terrain | error | A collider that reaches further than its planned clearance |

### 12.2 Captures

`ForestCapture` is a new class; `SceneCapture.cs` stays as it is. The capture loop, with the fixes the research derived from HDRP source:

```csharp
// The camera already has TAA and dithering (set by the builder).
bool oldAsync = ShaderUtil.allowAsyncCompilation;
ShaderUtil.allowAsyncCompilation = false;          // script renders then compile synchronously: no cyan placeholders
try
{
    camera.targetTexture = target;                 // ARGB32, sRGB, depth 24, as in SceneCapture.cs:47
    camera.Render();                               // creates the pipeline instance and the HDCamera
    HDCamera.GetOrCreate(camera).Reset();          // camera cut: drop history from the previous viewpoint
    for (int i = 0; i < frames; i++)               // 64: at 16 frames SSAO is still 44 % unconverged
    {
        camera.Render();
        if (i == 1) AsyncGPUReadback.WaitAllRequests();   // sky ambient probe readback
    }
    // ReadPixels as SceneCapture does.
}
finally { /* restore targetTexture, RenderTexture.active, allowAsyncCompilation, debug display state */ }
```

Why repeated `Camera.Render()` works in edit mode: HDRP advances its frame counter per render call in the Editor and keeps per-camera history for Game cameras (`HD/Runtime/RenderPipeline/HDRenderPipeline.cs:2319-2337`, `HD/Runtime/RenderPipeline/Camera/HDCamera.cs:179-207`). Do not use `ScreenCapture`, `WaitForEndOfFrame` (it does not run in batch mode) or the Recorder package.

Output per viewpoint: `<id>.png` at 2560 x 1440 (960 x 540 and 32 frames for the quick set), `<id>_crop.png` (768 x 768 centre crop at 1:1), `<id>.json` computed in C# (mean and median luminance, share of pixels under 2/255 and over 253/255, channel means, share of near-magenta and near-cyan pixels, RMSE against the previous run, camera pose, preset, seed, `apv`, texture-compression override, Unity version). Near-magenta: r > 230, g < 60, b > 230. Near-cyan: r < 60, g > 230, b > 230.

Viewpoints are defined in `ForestLayout.Viewpoints` and exist in the scene as `Gameplay/Viewpoints/<id>`, so they follow the layout when it is retuned.

| Set | Views | Judged for |
|---|---|---|
| `quick` | `trail_start` (spawn pose), `trail_mid_fwd`, `clearing`, `ground_close`, `overview_oblique` | Fast loop |
| `full` (every milestone) | `quick` plus `trail_mid_back`, `trail_strip_01` to `_08` (evenly spaced along the trail), `trunk_close` (0.6 m from the spawn's tall fir), `fir_close` (eye height, 1.5 m from the branch tips of the nearest spawn-thicket fir: the view experiment E1 is judged on), `rock_close` (0.95 m high, 1 m from a boulder-field rock), `canopy_up` (pitched 75 degrees up), `backlit` (yaw = `ForestLightingPresets.Get(preset).keyBearing + 180`, toward the key light's source), `long_sightline` (from (46, 30) along the gully), `crouch_hide` (0.95 m high inside the spawn thicket), `boundary_n/e/s/w` (3 m inside the wall, looking out), `pano_0/90/180/270` (mid-trail), `overview_ortho` | Composition, exposure, repetition, holes, LOD pops between neighbours, the world edge |
| `debug` (for `trail_start`, `clearing`, `trunk_close`) | Albedo, normal, diffuse lighting only, clay (`overrideAlbedo` 0.5 grey), cascades, mip ratio, through `HDRenderPipeline.debugDisplaySettings`; every mode reset in `finally` | Material and lighting faults in isolation |
| Kit (`CaptureKit`, after a kit build) | Each prefab from four yaws under `Atmosphere_Inspect`; each tree's source and every level side by side at one framing and again at 8, 25 and 70 m; a close crop of the foliage of the source and of each level at 1.5 m | Asset correctness; grounding of the log and the stump patches; the `trunk_c` material; the tree LOD decision |

Details: `ground_close` is 0.4 m high, pitched 35 degrees down, on the trail edge. `overview_oblique` is 40 m above (0, -90), pitched 45 degrees, looking north. `overview_ortho` is top-down orthographic with size 80 and every `LODGroup` forced for that shot (restored with `ForceLOD(-1)`), because a camera 250 m up would cull or coarsen everything: trees to the table's LOD1 mesh (level index 1, or 2 when the prefab has a scan level; 1,050 trees at LOD0 would be over a hundred million triangles), everything else to level 0. `trail_start`, `ground_close` and `trunk_close` are the three images to put beside a paid pack's screenshots.

`QuadOverdraw` and `VertexDensity` debug views do not exist on Metal, and HDRP has no LOD colouring view. Triangle counts per LOD come from the kit report instead.

### 12.3 Automated tests

Two new assemblies; every test in them carries `[Category("Forest")]`. The existing `Gamex.Tests.PlayMode` is not touched and runs as its own gate (section 11.5). Every test file belongs to WP5, including the unit tests of other packages' pure code, which are written against the public surface of 11.4. Tests that need an asset or the scene call `Assert.Ignore` when it is absent, so the suites run at every milestone.

**`Assets/Game/Tests/ForestEditMode/Gamex.Tests.Forest.EditMode.asmdef`**: Editor platform only; references `Gamex.Runtime`, `Gamex.EditorTools`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner`, `Unity.AI.Navigation`, `Unity.RenderPipelines.Core.Runtime`, `Unity.RenderPipelines.HighDefinition.Runtime`; `overrideReferences` true with `nunit.framework.dll`; `defineConstraints` `UNITY_INCLUDE_TESTS`; `autoReferenced` false (the pattern of the existing test asmdef).

| Test | Asserts | Needs |
|---|---|---|
| `Manifest_LoadsAndPathsAreUnique` | The manifest parses; every `relPath` is unique; every material referenced by an object exists; every `sameAs` and `noUvFallback` names a material of the same entry; every model has `boundsSpace` `"source-rh-yup"` and an `objectsSource`; every prefab id of `ForestPrefabIds` that is not a patch resolves with `FindObjectForPrefab` | nothing |
| `MeshTools_BoundsRuleIsMirrorIndependent` | For every manifest object, `MatchesSourceBounds` accepts the source box mirrored on X, on Z, on both and on neither, and rejects it offset by 5 m, scaled by 100, or with Y and Z swapped | nothing |
| `MeshTools_UsableUvRule` | A sub-mesh whose UVs are all equal has no usable UVs; a unit quad has | nothing |
| `Thinner_FindsPiecesOnSyntheticCards` | 10,000 separate quads give 10,000 pieces | nothing |
| `Thinner_LodsAreNested` | The piece set kept for a smaller target is a subset of the set for a larger one | nothing |
| `Thinner_MeetsTargetAndPreservesArea` | Triangle count within 2 % of the target; total area within 5 % of the source when the scale cap is not reached | nothing |
| `Thinner_FlattenReproducesPlanarCard` | A planar strip flattens to a quad with the same corners and UVs | nothing |
| `Thinner_IsDeterministic` | Same seed, same output | nothing |
| `MeshTools_ClusterDecimateKeepsSurfaceClosed` | A closed sphere stays closed (no boundary edges) and inside its bounds | nothing |
| `Sky_ConstantImageIntegratesToPi` | Within 1 % | nothing |
| `Layout_SplinesAndLandmarksAreInsideTheBoundary` | With the margins of section 3 | nothing |
| `Scatter_IsDeterministic` | Two plans with the same seed are identical; class counts within 15 % of section 7.2 | nothing |
| `Scatter_KeepsCorridorsAndSpawnClear` | For every placement with `clearance > 0`, hand-listed pieces included: `clearance` equals `ForestKitSpec.ClearanceRadius(prefabId) * scale`, the distance to every trail minus `clearance` is at least 2.5 m, and the distance to the spawn minus `clearance` is at least 5 m | nothing |
| `Scatter_GroundsOnFinalHeights` | After `Plan`, `AddRootMounds`, `Ground`: `plan.grounded`, and every `position.y` equals `fields.HeightAt(x, z) - sink` within 1 mm; at least one tall fir stands on a mound that raised the ground by more than 3 cm, which is the case the first plan got wrong | nothing |
| `Terrain_MeetsSlopeLimits` | Section 3.2, on the fields before and after root mounds | nothing |
| `Manifest_DownloadedFilesMatchSize` | Every present file has the manifest's byte size | downloads |
| `Textures_FollowPolicy` | Sizes, types, sRGB flags, mips, coverage settings per suffix; none above 4096; at most 8 at 4096 | import |
| `Materials_AreValidHdrp` | The material rows of 12.1 on every kit material | kit |
| `FoliageMaterial_MatchesHdrpLeafSample` | The expected state of section 5.5 | kit |
| `Prefabs_MatchManifestBounds` | `MatchesSourceBounds` on the `LOD0` mesh of every prefab that has a manifest object; the `mirror` values in the kit report agree across all prefabs that are not `"symmetric"` | kit |
| `Prefabs_FollowContract` | Section 5.6.6: children named `LOD0` to `LOD3` only, as many as `ForestKitSpec.LodCount` says (one to four), plus optional `Collision` and, with a scan level, `LOD0_Shadow`; `LODGroup` settings; collider rules, with the collider inside `ClearanceRadius`; no property block; at most four sub-meshes; collision meshes readable and under 3,000 triangles | kit |
| `TreeLods_WithinBudget` | Triangle counts per level within 10 % of `ForestKitSpec.TriangleTarget`, strictly decreasing; each level's bounds within 10 % of the first | kit |
| `TreeLods_KeepCoverage` | From `ForestKitReport.Load()`: silhouette coverage of each thinned level against the source, measured at the pixel size of its start distance, is 0.80 to 1.30 for the table's LOD0 and LOD1 and 0.70 to 1.30 for LOD2. A scan level is skipped | kit |
| `Scene_PassesAudit` | Re-runs `ForestAudit.Run(false)`; no error findings | scene |
| `Scene_IsClosed` | A player-sized capsule cast outward on 64 bearings from 20 interior points always hits a wall | scene |
| `Scene_NavMeshConnectsLandmarks` | `NavMesh.CalculatePath` from the spawn to each landmark and between consecutive patrol nodes is `PathComplete`; trail waypoints are within 0.5 m of the NavMesh | scene |
| `Scene_HasNoSourceMeshes` | The audit row about `Assets/ThirdParty` | scene |
| `Lighting_OneVolumeOneShadowLight` | One active global volume; one active shadow-casting directional light; one preset child per name in `ForestLightingPresets.Names` whose sky is downloaded | scene |
| `Terrain_ExtraLayersRender` | A top-down render of a test patch painted 100 % with layer 5 is not black | scene, 8 layers |
| `PlayerRig_MatchesGreybox` | The forest `Player` hierarchy has the same component types and the same controller and camera values as the greybox's, so the copied rig cannot drift | scene |
| `Greybox_StillFirstInBuildSettings` | `EditorBuildSettings.scenes[0]` is the greybox and is enabled | nothing |

**`Assets/Game/Tests/ForestPlayMode/Gamex.Tests.Forest.PlayMode.asmdef`**: any platform; references `Gamex.Runtime`, `UnityEngine.TestRunner`, `Unity.RenderPipelines.Core.Runtime`, `Unity.RenderPipelines.HighDefinition.Runtime`; otherwise as the existing test asmdef. It cannot reference the editor assembly, so it reads what it needs from the scene (`Gameplay/TrailWaypoints`, `Gameplay/Viewpoints`) and keeps its own copies of the budget constants.

The scene is loaded by path inside `#if UNITY_EDITOR` with `EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single))`, so the tests do not depend on Build Settings. Cameras are rendered explicitly (a disabled camera, `camera.Render()` once per frame); whether enabled cameras render by themselves in batch-mode play mode is UNVERIFIED.

| Test | Asserts |
|---|---|
| `Player_SpawnsOnTheGround` | After 30 frames `CharacterController.isGrounded` and the player's height is within 5 cm of the terrain |
| `Trail_IsWalkable` | A probe `CharacterController` with the player's dimensions, moved with `Move` at 3.2 m/s plus gravity from waypoint to waypoint, reaches the north end without stalling for 2 s and never drops below the terrain. This tests the scene, not the input code, and needs no input simulation |
| `Trail_IsSprintable` | The same at 6 m/s with the player's -2 m/s grounding: airborne frames under 2 % |
| `Bank_CannotBeClimbed` | The probe pushed outward on 16 bearings for 20 s each ends with x within plus or minus 67 and z within plus or minus 77 |
| `Render_HasNoErrorPixels` | Three viewpoints at 960 x 540 after 32 frames with `Time.captureDeltaTime = 1f / 60f`: near-magenta and near-cyan each under 0.1 % |
| `Render_ExposureIsSane` | Mean luminance of the tonemapped image between 0.10 and 0.45 (OvercastMist); under 5 % clipped white; under 20 % crushed black |
| `Render_ReferenceStills` | Writes three play-mode stills to `Temp/Forest/playmode/` for review. These, not the edit-mode captures, show what the player sees |
| `Budget_Counts` | `ProfilerRecorder` on `Triangles Count`, `Vertices Count`, `Draw Calls Count`, `SetPass Calls Count`, `Shadow Casters Count`, `Texture Memory`, `Mesh Memory` at four viewpoints (`trail_start`, `crouch_hide`, `clearing`, `long_sightline`), against section 10, memory as the difference from an empty scene. Fails if a recorder is not `Valid`. Only the measured camera renders |
| `Corridors_ArePhysicallyFree` | The physical corridor check of 12.1 (`Physics.CheckCapsule`, radius 2.0 m, every metre of the trail and both game trails, ignoring the terrain collider), in play mode, where physics queries are not in doubt |
| `Budget_FrameTime` | Logs median and 95th percentile of the GPU frame time. Method: once per frame `FrameTimingManager.CaptureFrameTimings()`, then `uint n = FrameTimingManager.GetLatestTimings(1, timings)` into a `FrameTiming[1]` and, when `n > 0`, `timings[0].gpuFrameTime` (milliseconds; a field of the `FrameTiming` struct, there is no `FrameTimingManager.gpuFrameTime`). Results lag four frames. If `FrameTimingManager.IsFeatureEnabled()` is false or the values are 0 (the project has `enableFrameTimingStats: 0` and it is not changed), fall back to a stopwatch around a one-pixel readback. Asserts only after probe P5 has validated the method. Sets `QualitySettings.vSyncCount = 0` and restores it |

No test ignores log errors: the framework already fails a test on an unexpected error log.

---

## 13. Milestones

Each one ends in the gate of section 12 and leaves something usable. **Version 1 is M0 to M4.** Stopping after any milestone leaves a working scene.

| # | Name | Adds | New download | Usable result if work stops here | Owner review |
|---|---|---|---|---|---|
| M0 | Foundations and probe | Step zero of 11.2; manifest loader; download and measuring tools; packer; postprocessor; mesh tools and thinner (WP2) with their unit tests (WP5); capture harness; audit skeleton; both test assemblies; `ForestProbe.Run` | None that is not reused: the probe uses `fern_02`, `rock_moss_set_02`, `forest_leaves_02` and the default sky (148 MB) | A verified import pipeline, a capture harness that also works on the greybox (`-captureScene`), and measured answers to the open questions | |
| M1 | Walkable ground under a real sky | Terrain shape; four layers, then eight; calibrated HDRI sky; mist; key light; post-processing; player; boundary; NavMesh; gameplay anchors; surface map | 221 MB | A walkable misty valley with photographed ground and correct exposure | |
| M2 | Forest floor | Moss rocks, stump patches, logs, branches, fern and grass patches, landmarks, collision meshes. The stump and log FBX files are measured here for the first time | 335 MB | A rocky fern heath in mist: the full floor, no trees yet | **Review 1.** Is this biome and this floor acceptable for the island? Captures `trail_start`, `ground_close`, `rock_close`, `clearing`, `overview_oblique` |
| M3 | Young firs (the tree gate) | Experiment E1 first (section 5.6.4); then the tree baker on `fir_sapling_medium`; thickets; the fir wall on the bank; coverage gate and LOD sheet | 85 MB | A young conifer forest. A complete scene by itself | **Review 2.** Do the young firs pass at eye level (`fir_close`, `crouch_hide`, `trail_start`)? This is the go or no-go for M4 |
| M4 | Tall firs and backdrop | `fir_tree_01`, fetched only after a go, measured, then imported alone in its own run; the `trunk_c` material choice; overstory; backdrop; root mounds; budgets asserted | 349 MB | **Version 1: the default look** | **Review 3.** Is it good enough to compare with a paid pack, and is frame time inside budget? |

After version 1, only if the owner asks:

| # | Name | Adds | New download |
|---|---|---|---|
| M5 | Lighting depth and presets | Probe bake with sky occlusion; HazySun, Dusk and Night; sky rotation calibration; the comparison frames under a low sun (Appendix C) | 68 MB |
| M6 | Optional, each item needs an owner decision | Resident Drawer A/B with `-force-gpuresidentdrawer -force-gpuocclusion`, then enablement; PCSS and SSR; detail maps on rock and bark; pines and seedlings; the two rock faces bedded into the bank as cliffs (175 MB, already in the manifest); wind on ferns; one macOS development build for a frame time without Editor overhead; sight-proxy colliders and cover metrics with the gameplay phase | up to about 1.4 GB |

**Decision points.**

- After M2: is the biome accepted? If the owner wants a tropical island, stop: the vegetation kit cannot deliver it inside the CC0 rule, and everything except that kit carries over (risk 20).
- After M3: do thinned young firs pass review at eye level, in the arm E1 chose, inside the frame budget? If not, apply the fallbacks of risk 1 and do not download `fir_tree_01`.
- After M4: is the default look good enough to spend time on M5?

**M0 probe list.** Each item has a default that already works, so no answer blocks M1. Results go to `probe.json`. Numbers are kept from the first plan; P9 and P13 moved out with their milestones.

| # | Question | How | If the answer is "no" |
|---|---|---|---|
| P1 | What transforms and material slot names does the importer give a Blender FBX? Which horizontal axis does it mirror? Is mesh data readable with `isReadable = false`? | Import `fern_02` and `rock_moss_set_02`; log child rotation, scale, bounds, slot names; read `mesh.vertices`; compare the signs of the centre offsets of `fern_02_b` and `rock_moss_set_02_rock13` with the manifest | Nothing: the matrix-based derivation, the mirror-independent bounds rule and the triangle-count mapping do not assume them. Set `isReadable = true` if reading fails |
| P2 | Which GPU formats are chosen? | `importer.GetAutomaticFormat("Standalone")` per texture kind | Informational |
| P3 | How long does a 2k and a 4k texture set take to import? | Timestamps in the log (`Importing ...` / `Imported ... in N seconds`) | `UnityEditor.Build.OverrideTextureCompression.ForceFastCompressor` while iterating |
| P4 | Does `MeshLodUtility.GenerateMeshLods` accept a script-built mesh, and does `GetTriangles(sub, lod, true)` return that level? | A subdivided sphere and `rock_moss_set_02_rock11` | Keep vertex clustering (already the default) |
| P5 | Do `ProfilerRecorder` counters and `FrameTiming.gpuFrameTime` (read through `FrameTimingManager.CaptureFrameTimings()` and `GetLatestTimings(1, FrameTiming[])`) report for explicit renders in batch mode? Is `FrameTimingManager.IsFeatureEnabled()` true in the Editor although the project has `enableFrameTimingStats: 0`? | Read them around explicit renders on the greybox, at least five frames after the first capture (results lag four frames) | Stopwatch with a one-pixel readback as the timing method |
| P6 | Do readbacks complete between back-to-back renders? | Compare the ambient probe before and after `WaitAllRequests` | Keep the explicit wait (already in the loop) |
| P7 | Does the CPU lux integral agree with the Inspector's GPU blit (`Hidden/HDRP/IntegrateHDRI`)? | Both on the default sky; warn above 10 % difference | Keep the CPU value |
| P8 | Cubemap face size from a 4k lat-long; does `CompressedHQ` (BC6H) import cleanly? | Log `cubemap.width`; import a compressed copy | Stay uncompressed |
| P9 | (Moved to M6: does the Resident Drawer start in a batch capture?) | | |
| P10 | Do terrain layers beyond four render on a script-made material, in the Editor? Does `ValidateMaterial` return true for it? | An eight-layer flat-colour test terrain, top-down capture; log the material's keywords after a render | Four layers |
| P11 | Where do LODGroups switch, and does cross-fade dither under HDRP without the Resident Drawer? | A two-level test object at set distances, reading `Triangles Count`; a capture inside the fade band | Fit `LodDistanceScale`; `UseCrossFade = false` |
| P12 | Do physics queries work in edit mode after `Physics.SyncTransforms()`? | A capsule check against a fresh collider and a terrain | The audit keeps its analytic corridor check; the physical one runs only in the PlayMode suite (`Corridors_ArePhysicallyFree`) |
| P13 | (Moved to M5: does an APV sky-occlusion bake complete through `BakeAsync` in batch mode? Appendix C) | | |
| P14 | How long is the first shader compile? | Timestamps | Informational |
| P15 | Is `TerrainData.alphamapResolution = 2048` accepted, and how long do `SetAlphamaps` with eight layers and saving the asset take? | A test `TerrainData`; read the property back | `ForestLayout.AlphamapResolution = 1024`, trail band still unblurred |

---

## 14. Risks and fallbacks

| # | Risk | Likelihood and impact | Detected by | Fallback, in order |
|---|---|---|---|---|
| 1 | Thinned foliage looks sparse, blobby or like a different plant at arm's length. At the LOD0 targets young firs keep 10 to 14 % of their twig triangles (each surviving piece 2.6 to 3.1 times life size); tall firs `a` and `b` keep 4 and 7 % (5.0 and 3.7 times). What a piece is (card strip or welded branch) is unknown until the first run | Medium to high for young firs at 1 to 3 m, medium to high for tall firs. **The main content risk, and it sits on the hero asset** | Experiment E1 at the start of M3 (`fir_close`); kit LOD sheets; `TreeLods_KeepCoverage`; thinner statistics in the kit report; owner review 2 | (a) E1 arm B (denser LOD0) or arm C (the scan inside 4 to 6 m), whichever passes inside the frame budget. (b) Flip `ThinByGroup`, toggle flatten per LOD, change scale caps. (c) Enable the crown core at LOD2, then LOD1. (d) Tall `a` and `b` on the bank only (never at LOD0); `c` (2.2 times) plus young firs in the core. (e) Stop at M3: a young fir forest is a complete scene; scale some young firs to 1.6 as canopy. (f) With the owner's approval, install Blender and export the authored LODs as a better starting mesh (they only halve the count per level, so the thinner is still needed), or relax the CC0 rule for trees |
| 2 | Importing the 249 MB `fir_tree_01` FBX is very slow or runs out of memory | Low to medium; delays M4 only | Import timestamps | It is imported alone, without tangents or mesh optimisation. If it fails, M3 stands |
| 3 | The importer's transforms, hierarchy, mirrored axis or material names differ from what the file contents suggest | Medium | The bounds assertion, at kit build time; P1 | Already handled: matrix-based derivation, a bounds rule that holds under any mirror, and triangle-count mapping. The build throws instead of shipping a wrong prefab |
| 4 | Terrain layers 5 to 8 render black, or height blend looks wrong | Low | `Terrain_ExtraLayersRender`, P10, `overview_ortho` | `TerrainLayerCount = 4`; `_EnableHeightBlend = 0` |
| 5 | Sky intensity is wrong, so frames come out black or white | Low | Lux guards; `Render_ExposureIsSane`; sidecar luminance | The exposure clamp limits the error; then `Multiplier` mode with a tuned value; then fixed exposure |
| 6 | (M5, not version 1) The probe bake fails or hangs headless on Apple silicon, or its data is invalid after the proxies are removed | Medium | Timeout; `DataHasBeenLoaded()`; P13 | The scene stays unbaked, which is the version-1 state anyway. Bake from the menu in the open Editor; `UnityComputeGPU`; keep the proxies deactivated; route B |
| 7 | (M5, not version 1) Sky occlusion makes the floor too dark or too bright | Medium | `presets` captures | `skyOcclusionIntensityMultiplier`; `skyOcclusionAverageAlbedo`; proxies from LOD1 or wood only |
| 8 | Foliage turns magenta | Low | Factory read-back, audit, magenta pixel count | Automatic: Standard material type |
| 9 | Too many draw calls or too much CPU without the Resident Drawer | Medium | `Budget_Counts` | Shorter cull distances; fewer bank trees; then the Resident Drawer with approval |
| 10 | Frame time misses 11 ms | Unknown until measured | `Budget_FrameTime`, P5 | The knob list of section 10 |
| 11 | LOD cross-fade misbehaves or LODs switch at the wrong distances | Low to medium | `trail_strip` captures, P11 | `UseCrossFade = false`; `LodDistanceScale` |
| 12 | Vertex-cluster LODs of rocks and wood look crude at 12 to 30 m | Low to medium | Kit sheets | Push LOD0 distances out (instance caps bound the cost: about 90 rocks at about 10,000 triangles); the Unity generator if P4 passed |
| 13 | Tiling and scan-patch rims show on open ground | Medium | `ground_close`, `clearing`, `overview_oblique` | More ferns and branches at the offending spots; later the HDRP anti-tiling terrain graph from the Shader Graph sample, which only swaps the terrain material (Unity-licensed shader: owner decision) |
| 14 | The forest has no hard cover of standing height, and the AI sees through thickets | Certain for the later gameplay phase, none for this one | Known from the measured sizes (section 7.4); nothing in version 1 measures it | Not solved here on purpose. Gameplay-phase options: sight-proxy colliders in thickets; terrain shaped into ridges and hollows; the two rock faces bedded into the bank (M6); a non-CC0 rock or log asset |
| 15 | Batch runs are blocked because the Editor is open | Certain at times | Unity refuses to start | Every version-1 entry point has a menu item, captures and probe included (11.4); tests from the Test Runner window; or agree run windows |
| 16 | Edit-mode captures differ from what the player sees | Certain for time-driven effects | Compare with `Render_ReferenceStills` | Judge from the play-mode stills |
| 17 | About 11,000 GameObjects make the Editor sluggish | Low | Scene load time in the log | Merge more ferns per patch; lower caps |
| 18 | Poly Haven changes or removes a file | Low | md5 mismatch at fetch | Keep a copy of `Assets/ThirdParty/PolyHaven` outside the machine |
| 19 | The repository grows by gigabytes | Certain if binaries are committed | | Owner decision (section 15) |
| 20 | The owner wants a tropical island | Unknown | Owner review 1, at M2, before any tree work | Not solvable inside the CC0 rule. Everything except the vegetation kit carries over to another kit |
| 21 | The static forest feels dead in motion | Certain to be noticed | Walk-through | Accepted for version 1. Wind on ferns by copying Unity's detail shader graphs (licence decision) is an M6 item |
| 22 | The FBX of the stump patches, the log or a fir differs from the glTF numbers in the manifest, as the rock faces did (1.4 and 1.8 times larger) | Low to medium; would change spacing, LOD distances and compositions | `fbx_objects.py` at fetch time, before Unity imports anything (section 5.1) | Look at the measured table; if the difference is a plain scale or re-orientation, check the layout numbers that depend on it, then `--write`. The kit build cannot start on unmeasured numbers |
| 23 | `fir_tree_01_trunk_c` has no usable UVs in the FBX, or UVs that do not fit the `trunk_c` textures | Medium; one tree, but it is the fallback hero | Kit report (`triplanarMaterials`); kit turntable of `fir_tree_01_c`; `trunk_close` | Automatic triplanar bark without usable UVs; `ForestKitSpec.ForceTriplanar` if the UVs are garbage |
| 24 | The trail does not read as two ruts with a mossy strip | Medium | `ground_close`, `trail_start` at M1 | Alphamap 2048 and no blur in the trail band are already in; widen the ruts; as a last resort paint one gravel band and rely on ferns and litter at its edges |
| 25 | The log looks half-buried, or its mid-section floats | Medium; eight instances | Kit turntable at M2; `Scene` captures at the deadfall | `ForestKitSpec.LogPitchDegrees` with a smaller sink (section 7.3); fewer logs; ring ferns |
| 26 | `alphamapResolution` 2048 is refused or makes the terrain asset slow to save | Low | P15; the read-back assertion in 6.1 | `ForestLayout.AlphamapResolution = 1024` |
| 27 | The eight-layer terrain variant is stripped from a player build | Unknown; no effect in the Editor | The M6 standalone build | Four layers; or a terrain material asset saved with the keyword on |
| 28 | Version 1 is still too large for a first look | Medium: 27 C# files, two tools, about 40 tests | The owner reviews at M2 and M3 | Each milestone is a stopping point. M1 plus M2 alone give a walkable, photographed forest floor |

---

## 15. Decisions that need the owner

| Decision | Default if nobody decides |
|---|---|
| Is a misty fir forest acceptable for the island's base scenery? Asked with the M2 captures in hand (review 1) | Yes. It is the only biome the CC0 rule can deliver at this quality |
| After M3 (review 2): fetch and build the tall firs? | Only if the young firs pass at eye level inside the frame budget. Otherwise stop at M3 with the fallbacks of risk 1 |
| If experiment E1 shows that only the untouched scan passes up close: accept its cost (several million extra triangles inside a thicket)? | Yes, at the largest distance that keeps the frame budget; never below 4 m |
| After M4: go on to M5 (three more lighting presets and the probe bake)? | No. Version 1 ends at M4 |
| Commit downloaded and generated binaries (with Git LFS), or keep them reproducible and ignored? | Ignored: add `Assets/ThirdParty/PolyHaven/`, `Assets/Game/Scenes/ForestKit/`, `Assets/Game/Scenes/ForestData/`, `Assets/Game/Scenes/Forest/` and `Assets/Game/Scenes/Forest.unity` (with their `.meta` files) to `.gitignore`; commit code, manifest and tests. A fresh clone downloads and runs `BuildAll` once. The scene is ignored too because its references point at assets whose GUIDs exist only on the machine that imported them |
| When can the Editor be closed for batch runs? | Stages are run from the `Gamex/Forest` menu inside the open Editor |
| If thinned tall firs do not pass review: install Blender, or allow non-CC0 trees? | Neither. Ship young firs plus `fir_tree_01_c`, with tall `a` and `b` on the bank only |
| Standing-height hard cover for stealth (no CC0 scan in the kit provides it): shape the terrain, bed the rock faces into the bank, or allow one non-CC0 rock asset? | Decided in the gameplay phase. Version 1 builds none |
| M6: may the HDRP asset and Graphics settings change (Resident Drawer with "Keep All" variants, PCSS, SSR)? | No. The command-line A/B run comes first and brings numbers |
| M6: may Unity-licensed sample shader graphs (not meshes or textures) be copied for fern wind and terrain anti-tiling? | No |

---

## Appendix A: API names and where each was confirmed

Read for this specification (source line, doc page or XML doc of the compiled assembly):

| Area | Names | Confirmed in |
|---|---|---|
| Mesh LOD read-back | `Mesh.GetTriangles(int submesh, int meshLod, bool applyBaseVertex)`; `MeshLodUtility.GenerateMeshLods(Mesh mesh, int meshLodLimit)`; `Mesh.lodCount` | `DOC/ScriptReference/Mesh.GetTriangles.html`, `MeshLodUtility.GenerateMeshLods.html`, `Mesh-lodCount.html` |
| Mesh merge | `Mesh.CombineMeshes(CombineInstance[] combine, bool mergeSubMeshes = true, bool useMatrices = true, bool hasLightmapData = false)` | `DOC/ScriptReference/Mesh.CombineMeshes.html` |
| Image I/O | `ImageConversion.LoadImage(Texture2D tex, byte[] data, bool markNonReadable)`; PNG to ARGB32, EXR to RGBAFloat (PC, Mac, Linux) | `DOC/ScriptReference/ImageConversion.LoadImage.html` |
| Hidden folders | Names ending in `~` are not imported | `DOC/Manual/SpecialFolders.html` |
| LOD | `LODGroup.SetLODs(LOD[])`, `ForceLOD(int)`, `fadeMode`, `animateCrossFading`, `size`, `localReferencePoint`; `LOD.fadeTransitionWidth`; `LODFadeMode.CrossFade` | `XML/UnityEngine.CoreModule.xml:22988-23100` |
| Renderers | `MeshRenderer.receiveGI`, `ReceiveGI.LightProbes`, `Renderer.HasPropertyBlock()` | `XML/UnityEngine.CoreModule.xml:28996, 34350, 35676` |
| Camera | `HDAdditionalCameraData.AntialiasingMode`, `TAAQualityLevel`, `antialiasing`, `TAAQuality`, `taaSharpenStrength`, `dithering`, `stopNaNs`, `customRenderingSettings`, `renderingPathCustomFrameSettingsOverrideMask`, `renderingPathCustomFrameSettings` | `HD/Runtime/RenderPipeline/Camera/HDAdditionalCameraData.cs:239, 271, 310-323, 379, 514-520` |
| Capture | `HDCamera.GetOrCreate(Camera camera, int xrMultipassId = 0)`, `HDCamera.Reset()`; `AsyncGPUReadback.WaitAllRequests()`; `HDRenderPipeline.debugDisplaySettings` | `HD/Runtime/RenderPipeline/Camera/HDCamera.cs:179, 293`; `XML/UnityEngine.CoreModule.xml:36639`; `HD/Runtime/RenderPipeline/HDRenderPipeline.Debug.cs:52` |
| Frame settings | `FrameSettingsField.Decals`, `NormalizeReflectionProbeWithProbeVolume` | `HD/Runtime/RenderPipeline/Settings/FrameSettings.cs:170, 390` |
| Materials | `HDMaterial.ValidateMaterial(Material)`, `SetAlphaCutoff(Material, float)`, `SetDiffusionProfile(Material, DiffusionProfileSettings)`; `_UVBase` (Triplanar = 5), `_TexWorldScale`; `_EnableHeightBlend`, `_HeightTransition`; package `FoliageDiffusionProfile.asset` exists | `HD/Runtime/Material/MaterialExtension.cs:369, 504, 513`; `HD/Runtime/Material/Lit/Lit.shader:157, 159`; `HD/Runtime/Material/TerrainLit/TerrainLit.shader:5-6, 97`; `HD/Runtime/RenderPipelineResources/` |
| Sky | `VisualEnvironment.skyType`, `skyAmbientMode`; `HDRISky.hdriSky`; `SkySettings.rotation` (0 to 360), `skyIntensityMode`, `multiplier`, `upperHemisphereLuxValue`, `upperHemisphereLuxColor`, `desiredLuxValue` | `HD/Runtime/Sky/VisualEnvironment.cs:49, 53`; `HD/Runtime/Sky/HDRISky/HDRISky.cs:30`; `HD/Runtime/Sky/SkySettings.cs:107-125` |
| Sky rotation (M5) | The renderer applies `-rotation`; the view direction is rotated about Y before the cubemap lookup; HDRP's lat-long helpers put the image centre at +Z and increase `u` toward +X | `HD/Runtime/Sky/HDRISky/HDRISkyRenderer.cs:42`; `HD/Runtime/Sky/SkyUtils.hlsl:20-37`; `HD/Runtime/Sky/HDRISky/HDRISky.shader:213-230`; `CORE/ShaderLibrary/Common.hlsl:1146-1166` |
| Fog | `Fog.enabled`, `colorMode`, `baseHeight`, `maximumHeight`, `meanFreePath`, `enableVolumetricFog`, `albedo`, `globalLightProbeDimmer`, `denoisingMode`, `anisotropy`, `sliceDistributionUniformity`, `depthExtent`; `FogDenoisingMode.Gaussian`; `VolumeComponentWithQuality.quality`; `ScalableSettingLevelParameter.Level.Low/Medium/High` | `HD/Runtime/Lighting/AtmosphericScattering/Fog.cs:17-77`; `HD/Runtime/Utilities/VolumeComponentWithQuality.cs:23`; `HD/Runtime/RenderPipeline/GlobalPostProcessingQualitySettings.cs:9-22` |
| Shadows and light | `HDShadowSettings.maxShadowDistance`, `cascadeShadowSplitCount`, `cascadeShadowSplit0`; `ContactShadows.enable`, `length`, `maxDistance`, `fadeDistance`; `MicroShadowing.enable`, `opacity`; `HDAdditionalLightData.angularDiameter`, `affectsVolumetric`, `volumetricDimmer`, `interactsWithSky`, `useContactShadow`, `SetShadowResolution(int)`, `SetShadowResolutionOverride(bool)` | `HD/Runtime/Lighting/Shadow/HDShadowSettings.cs:99-110`, `ContactShadows.cs:18-51`, `MicroShadowing.cs:19-25`; `HD/Runtime/Lighting/Light/HDAdditionalLightData.cs:210, 649, 670, 1513, 1821, 3291, 3317` |
| Post-processing | `Exposure.mode`, `meteringMode`, `fixedExposure`, `compensation`, `limitMin`, `limitMax`, `histogramPercentages` (a `Vector2`); `ExposureMode.Fixed`, `AutomaticHistogram`; `MeteringMode.CenterWeighted`; `Bloom.threshold`, `intensity`, `scatter`; `Vignette.intensity`, `smoothness`; `ColorAdjustments.contrast`, `saturation`; `WhiteBalance.temperature`; `MotionBlur.intensity`; `FilmGrain.type`, `intensity`, `response`; `FilmGrainLookup.Thin1`, `Thin2`, `Medium1`; `ScreenSpaceAmbientOcclusion.intensity`, `directLightingStrength`, `radius`; `IndirectLightingController.indirectDiffuseLightingMultiplier`; `DiffusionProfileList.diffusionProfiles` | The files under `HD/Runtime/PostProcessing/Components/`; `HD/Runtime/Lighting/ScreenSpaceLighting/ScreenSpaceAmbientOcclusion.cs:24-32`; `HD/Runtime/Lighting/IndirectLightingController.cs:16`; `HD/Runtime/Lighting/DiffusionProfileList.cs:19` |
| Probes | `ProbeVolume.mode`, `ProbeVolume.Mode.Local`, `size`; `ProbeVolumeBakingSet.simplificationLevels`, `minDistanceBetweenProbes`, `skyOcclusion`, `skyOcclusionAverageAlbedo`, `TryAddScene(string guid)`, internal `SetDefaults()`; `ProbeReferenceVolume.instance.isInitialized`, `DataHasBeenLoaded()`; `ProbeVolumesOptions.skyOcclusionIntensityMultiplier`; `UnityEngine.Rendering.AdaptiveProbeVolumes.BakeAsync()` (returns bool), `isRunning`, `Cancel()` in assembly `Unity.RenderPipelines.Core.Editor` | `CORE/Runtime/Lighting/ProbeVolume/ProbeVolume.cs:17-36`, `ProbeVolumeBakingSet.cs:200-260`, `ProbeVolumeBakingSet.Editor.cs:79, 380`, `ProbeReferenceVolume.cs:750, 1784`, `ProbeVolumesOptions.cs:89`; `CORE/Editor/Lighting/ProbeVolume/ProbeGIBaking.cs:1923-1951`, `AdaptiveProbeVolumes.BakePipelineDriver.cs:32` |
| Lightmapping | `Lightmapping.Bake()`, `Lightmapping.SetLightingSettingsForScene(Scene, LightingSettings)`; `LightingSettings.Lightmapper.ProgressiveGPU`, `lightProbeSampleCountMultiplier`; `LightmapSettings.lightmaps`; `RenderPipelineManager.currentPipeline` | `XML/UnityEditor.CoreModule.xml:33351, 33553`; `XML/UnityEngine.CoreModule.xml:21282, 21452, 21532, 48738` |
| Editor utilities | `EditorUtility.CopySerialized`, `AssetDatabase.SaveAssetIfDirty`, `EditorBuildSettings.scenes`, `PrefabUtility.SaveAsPrefabAsset`, `PrefabUtility.InstantiatePrefab` (returns `UnityEngine.Object`: cast), `GameObjectUtility.SetStaticEditorFlags`, `StaticEditorFlags.ContributeGI`, `EditorSceneManager.LoadSceneAsyncInPlayMode`, `ShaderUtil.allowAsyncCompilation`, `EditorApplication.Exit` | `XML/UnityEditor.CoreModule.xml` |
| Texture compression override | `EditorUserBuildSettings.overrideTextureCompression`; `UnityEditor.Build.OverrideTextureCompression.ForceFastCompressor` | `XML/UnityEditor.CoreModule.xml:24055, 8706, 8711`; compiled with and without the namespace |
| Frame timing | `FrameTimingManager.CaptureFrameTimings()`; `uint FrameTimingManager.GetLatestTimings(uint numFrames, FrameTiming[] timings)`; `FrameTimingManager.IsFeatureEnabled()`; field `FrameTiming.gpuFrameTime` (double, ms). There is no `FrameTimingManager.gpuFrameTime` | `XML/UnityEngine.CoreModule.xml:13631, 13656, 13677, 13695`; `DOC/ScriptReference/FrameTimingManager.html` (four-frame delay; designed for players); compiled both ways |
| Terrain heights | `Terrain.SampleHeight(Vector3)` (relative to the terrain's position); `TerrainData.GetInterpolatedHeight(float x, float y)` (normalised coordinates); `TerrainData.alphamapResolution` | `DOC/ScriptReference/Terrain.SampleHeight.html`, `TerrainData.GetInterpolatedHeight.html`, `TerrainData-alphamapResolution.html`; `XML/UnityEngine.TerrainModule.xml:635, 778` |
| Test runner arguments | `-testPlatform`, `-testCategory`, `-assemblyNames`, `-testResults` | `PRJ/Library/PackageCache/com.unity.test-framework@7a3849e09bd0/UnityEditor.TestRunner/CommandLineTest/SettingsBuilder.cs:43, 47, 51, 103` |
| Debug display | `DebugDisplaySettings.SetDebugViewCommonMaterialProperty(MaterialSharedProperty)`, with the enum in `UnityEngine.Rendering.HighDefinition.Attributes` | compiled with and without the namespace |
| Physics, navigation, profiling, noise | `Physics.SyncTransforms()`, `Physics.CheckCapsule`, `Physics.CapsuleCast`; `NavMesh.CalculatePath`, `NavMesh.SamplePosition`; `ProfilerRecorder.StartNew(ProfilerCategory, string, int, ProfilerRecorderOptions)`, `Valid`, `LastValue`; `Time.captureDeltaTime`; `Unity.Mathematics.noise.snoise(float2)` | `XML/UnityEngine.PhysicsModule.xml`, `UnityEngine.AIModule.xml`, `UnityEngine.CoreModule.xml`, `UnityEngine.MathematicsModule.xml:58001` |
| Resident Drawer (M6) | Command-line switches `-force-gpuresidentdrawer`, `-force-gpuocclusion` | `CORE/Runtime/GPUDriven/GPUResidentDrawer.cs:307, 320, 439` |
| Project state | `supportSSR: 0`, `supportSSGI: 0`, `supportDecals: 1`, `lightProbeSystem: 1`, `directionalShadowFilteringQuality: 1`, Resident Drawer `mode: 0`; `m_BrgStripping: 0`; `enableLODCrossFade: 1`; `m_CurrentQuality: 5` (`lodBias: 1`); `supportDitheringCrossFade: 0` (obsolete, no effect); `enableFrameTimingStats: 0`; `metalAPIValidation: 1`; `m_QueriesHitBackfaces: 0`; assets serialised as text (`m_SerializationMode: 2`); play mode entered without domain reload (`m_EnterPlayModeOptions: 3`) | `PRJ/Assets/Game/Settings/HDRP.asset:214-527`; `PRJ/ProjectSettings/GraphicsSettings.asset:58`; `PRJ/ProjectSettings/QualitySettings.asset:7, 301, 304`; `PRJ/ProjectSettings/ProjectSettings.asset:160, 248`; `PRJ/ProjectSettings/DynamicsManager.asset:15`; `PRJ/ProjectSettings/EditorSettings.asset:7, 30` |
| Existing code | Raptor sight 28 m by `Physics.Linecast` against all layers; player speeds 1.6 / 3.2 / 6 m/s, eye heights 1.65 and 0.95 m, body heights 1.8 and 1.1 m; greybox boulders 1.6 to 3.0 m tall (`GreyboxSceneBuilder.cs:277`); `GameSession` needs only `player`; existing asmdef references | `PRJ/Assets/Game/Scripts/Dinosaurs/RaptorAI.cs:30-36, 188`; `PRJ/Assets/Game/Scripts/Player/PlayerController.cs:17-23`; `PRJ/Assets/Game/Scripts/Core/GameSession.cs:19-36`; the three `.asmdef` files under `PRJ/Assets/Game` |

Taken from the research reports and the robustness-first design without re-reading the source here (each report cites its own file and line): every `ModelImporter` and `TextureImporter` property name of sections 5.2 and 5.3 (`research/import-pipeline.md` sections 1.1 and 3.1, checked there against the ScriptReference pages and the IL of `UnityEditor.CoreModule.dll`); `TerrainData`, `Terrain` and `TerrainLayer` members (`research/terrain-vegetation.md` section 1.1 and 2.1); the Resident Drawer `SerializedObject` paths `m_BrgStripping` and `m_RenderPipelineSettings.gpuResidentDrawerSettings.mode` (`research/import-pipeline.md` section 6.5); the debug display calls `SetDebugViewCommonMaterialProperty`, `SetDebugLightingMode`, `SetFullScreenDebugMode`, `SetMipMapMode` and the profiler counter names (`research/verification-performance.md` sections 2 and 3); `HDRenderPipelineAsset.currentPlatformRenderPipelineSettings` (`research/lighting-atmosphere.md` section 8.2).

## Appendix B: Unverified items and what settles each

| Item | Where it matters | Settled by | Default that works meanwhile |
|---|---|---|---|
| Imported child transforms and material slot names of Poly Haven FBX files; which horizontal axis the importer mirrors | 5.6.1 | P1; the bounds assertion; `mirror` in the kit report | Matrix-based derivation; a bounds rule that holds under any mirror |
| The manifest's `objects[]` of `tree_stump_01`, `tree_stump_02`, `dead_tree_trunk_02`, `fir_sapling_medium` and `fir_tree_01` (glTF numbers, not checked against the FBX) | 4.2, 5.1, every size in sections 3, 5.6 and 7 that uses them | `fbx_objects.py` when each file is fetched | The kit build refuses an unmeasured model |
| Whether the FBX has usable UVs for `fir_tree_01_trunk_c`, and whether they fit the `trunk_c` textures | 5.5 | The M4 kit build and turntable | Triplanar bark; `ForceTriplanar` |
| That fir twig sub-meshes are separate small pieces | 5.6.3 | Thinner statistics on the first M3 run | Spatial-hash fallback |
| How thinned foliage looks at 1 to 3 m, and what the untouched scan costs per frame | 5.6.4 | Experiment E1; M3 and M4 kit sheets; coverage gate; owner review 2 | Risk 1 |
| That grounding the log by sinking alone looks right | 7.3 | M2 kit turntable | `LogPitchDegrees` |
| That the trail reads as two ruts at alphamap 2048; that 2048 is accepted | 6.1, 6.4 | P15; `ground_close` at M1 | 1024; wider ruts |
| `GenerateMeshLods` on a script-built mesh and level read-back | 5.6.2 | P4 | Vertex clustering |
| LODGroup switch formula; that the engine enables `LOD_FADE_CROSSFADE` for these LODGroups on the plain SRP path (the shader side and the quality setting are confirmed) | 5.6.4 | P11 | `LodDistanceScale`, `UseCrossFade` |
| What sets `_TERRAIN_8_LAYERS` on a script-made TerrainLit material, hence whether layers 5 to 8 render; `ValidateMaterial` on that material; whether the variant survives a player build | 6.3 | P10; `Terrain_ExtraLayersRender`; the M6 build | Four layers |
| `TerrainData` ordering rules | 6.1 | First M1 build | The order of 6.1 |
| Terrain `m_ReceiveGI` written through `SerializedObject` | 6.1, Appendix C | First bake (M5) | Probes-only bake route does not need it |
| CPU lux integral against HDRP's blit; `LoadImage` on Poly Haven's 1k EXR, including its channel order (Unity reads EXR channels by position, not by name) | 8.4 | P7 | `Multiplier` mode |
| Cubemap face size; BC6H on Apple silicon | 5.3 | P8 | Uncompressed |
| (M5) How the texture importer maps a lat-long column to a cubemap direction. The sign of `HDRISky.rotation` is now known from source | Appendix C | Eight-direction sky capture | Rotation 0 for the default sky |
| (M5) `AdaptiveProbeVolumes.BakeAsync` completing in batch mode on Apple silicon; lightmapper back end; data validity after proxies are destroyed; that `Lightmapping.Bake()` leaves the terrain unlightmapped with `m_ReceiveGI` set to light probes | Appendix C | P13; the first M5 bake | No bake, which is the version-1 state |
| (M5) Floor darkness under sky occlusion | Appendix C | M5 captures | The three knobs |
| `ProfilerRecorder` counters, `FrameTiming` data (with `enableFrameTimingStats: 0` in the Editor) and readbacks for explicit renders in batch mode | 10, 12 | P5, P6 | Counts only; time by stopwatch, logged |
| Physics queries in edit mode | 12.1 | P12 | The analytic corridor check, plus `Corridors_ArePhysicallyFree` in play mode |
| Whether enabled cameras render by themselves in batch-mode play mode | 12.3 | Not needed | Explicit `camera.Render()` |
| Import time and memory of the 249 MB FBX; texture import times; first shader compile | 13 | P3, P14, the M4 log | One heavy file per run |
| MeshCollider on non-readable meshes in a player build (the docs disagree) | 5.6.6 | First standalone build (M6) | Collision meshes are script-built, hence readable |
| Every lighting value, density, LOD distance and budget | 7, 8, 10 | Captures and measurements | They are starting points |

## Appendix C: Deferred work (M5 and M6), with the corrections from verification

Nothing in this appendix is built, fetched or tested in version 1. It is kept so the research is not lost and so the contradictions the verifiers found in it are already resolved when it is picked up.

### C.1 Preset starting values (M5)

Same fields as section 8.2. None of these has ever been rendered.

| Field | HazySun | Dusk | Night |
|---|---|---|---|
| Sky HDRI | `kloofendal_misty_morning_puresky` | `qwantani_dusk_2_puresky` | `qwantani_night_puresky` |
| Sky `desiredLuxValue` | 8,000 | 30 | 0.06 |
| Key light, lux | 18,000 | 3 | 0.3 |
| Key colour temperature (K) | 5,400 | 3,500 | 4,100 |
| Key `angularDiameter` (degrees) | 2 | 15 | 0.52 |
| Key `Euler(elevation, bearing, 0)` | (30, 160, 0): sun low in the north-north-west; the walk north is back-lit | (6, 160, 0) | (50, 135, 0) |
| Key `volumetricDimmer` | 1.2 | 1 | 2 |
| Shadow distance; cascades; splits 0/1/2 | 90 m; 4; 0.07 / 0.18 / 0.42 | 80; 4; 0.08 / 0.22 / 0.50 | 60; 4; 0.10 / 0.25 / 0.50 |
| Fog `meanFreePath` | 75 m | 70 m | 50 m |
| Fog `baseHeight` / `maximumHeight` (world Y) | 2 / 45 | 2 / 25 | 2 / 12 |
| Fog `albedo` | (0.93, 0.95, 0.97) | (0.90, 0.92, 0.95) | (0.88, 0.92, 1.0) |
| Fog `anisotropy` | 0.7 | 0.6 | 0.7 |
| Fog `depthExtent` | 90 | 80 | 64 |
| Fog `quality` tier | High | Medium | Medium |
| Exposure | `AutomaticHistogram`, 9.0 to 12.0, -0.3 | `AutomaticHistogram`, 1.0 to 3.5, -1.0 | `Fixed`, `fixedExposure` -1.0 |
| White balance `temperature` | 0 | -8 | -22 |
| `saturation` / `contrast` | -4 / +6 | -10 / +8 | -30 / +10 |
| Bloom `intensity` | 0.12 | 0.18 | 0.15 |
| Film grain `type`, `intensity` | `Thin1`, 0.10 | `Thin2`, 0.20 | `Medium1`, 0.30 |
| Vignette `intensity` | 0.22 | 0.25 | 0.35 |

- HazySun, open ground: 8,000 + 18,000 x sin 30 = 17,000 lux, neutral 12.4 EV; under canopy at 10 to 20 % of that, 9.1 to 10.1 EV. It is the showcase for comparison frames (low sun through mist, canopy shadows, light through needles), and it needs the probe bake: without it the fog under the canopy is lit by an unoccluded sky and glows.
- Night: 0.3 + 0.06 lux, neutral -3.1 EV; fixed -1.0 is about two stops under neutral, where night reads as night. (The greybox's 6 lux "moon" at -1 EV is about two stops over neutral.) The night sky is moonless, so the moon light is not counted twice.

At M5, `ForestLightingPresets.Names` grows to four, the three skies are fetched (`--milestone M5`), and a capture set `presets` is added: `trail_start`, `long_sightline`, `clearing` per preset; a fixed-exposure variant at night; eight sky-only directions for the rotation calibration below.

### C.2 Sky rotation (M5)

What the source settles:

- The renderer applies the rotation negated: `phi = -Mathf.Deg2Rad * hdriSky.rotation.value` (`HD/Runtime/Sky/HDRISky/HDRISkyRenderer.cs:42`). The range is 0 to 360 (`HD/Runtime/Sky/SkySettings.cs:107`).
- The shader rotates the view direction about Y by `phi` before the cubemap lookup: `x' = cos(phi) x - sin(phi) z`, `z' = sin(phi) x + cos(phi) z` (`HD/Runtime/Sky/SkyUtils.hlsl:31-37`, used at `HD/Runtime/Sky/HDRISky/HDRISky.shader:213-230`).
- Together: a camera looking toward compass bearing `B` (0 = +Z, 90 = +X) samples the cubemap at bearing `B + rotation`. A feature at cubemap bearing `b` therefore appears at world bearing `b - rotation`: increasing `rotation` turns the sky anticlockwise seen from above.
- So, to put the brightest part of the sky where the key light comes from: `rotation = (b_brightest - (keyBearing + 180)) mod 360`.

What is still **UNVERIFIED**: how the texture importer maps a lat-long column to a cubemap bearing, that is, the value of `b_brightest` for a given column. HDRP's own shader helpers put the image centre at +Z and increase `u` toward +X (`HD/Runtime/Sky/SkyUtils.hlsl:20-28`; `CORE/ShaderLibrary/Common.hlsl:1146-1166`), which is what `ForestSky.Calibration.brightestAzimuthDegrees` assumes (0 at the centre column, increasing to the right). The eight-direction sky-only capture checks that one assumption; if it is off, the correction is a constant offset added in `ForestLightingPresets`. The constant `SkyRotationSign` of the first plan is gone: the sign is no longer in question.

### C.3 Probe bake (M5)

Adaptive Probe Volumes with sky occlusion, probes only. One bake serves all presets, because sky occlusion stores sky visibility and the sky colour arrives at run time. The stage is `ForestProbeBake.Bake`, run **without `-quit`**. It calls the scene-agnostic `BakeScene` on the forest scene; probe P13 calls the same helper on a small test scene first.

1. Open the saved scene. Delete the previous contents of `ForestData/Bake` (a changed probe layout refuses to re-bake in batch mode).
2. Contributors. The terrain and the `LOD0` renderers of rocks, logs and stumps carry `StaticEditorFlags.ContributeGI` already (prefab contract) and receive from light probes. Trees do not. For the duration of the bake, create `Environment/BakeProxies`: one plain `MeshRenderer` per tree instance, at the instance's transform, with that tree's **coarsest level** (`ForestPrefabs.LoadLodMesh(prefabId, ForestPrefabs.LodCount(prefabId) - 1)`), flagged `ContributeGI`, `receiveGI = ReceiveGI.LightProbes`. This keeps the baker's input at a few million triangles and makes the canopy porous instead of solid.
3. `ProbeVolume`, created by the bake stage under `Lighting` if it is missing: `mode = ProbeVolume.Mode.Local`, `size = (180, 45, 200)`, centred at (0, 12, 0).
4. Baking set created by script: `ScriptableObject.CreateInstance<ProbeVolumeBakingSet>()`; call the internal `SetDefaults()` by reflection (`CORE/Runtime/Lighting/ProbeVolume/ProbeVolumeBakingSet.Editor.cs:380`); `minDistanceBetweenProbes = 2`; `simplificationLevels = 3`; `skyOcclusion = true`; `AssetDatabase.CreateAsset` into `ForestData/Bake/`; `TryAddScene(AssetDatabase.AssetPathToGUID(scenePath))`.
5. `LightingSettings` asset in the same folder: `lightmapper = LightingSettings.Lightmapper.ProgressiveGPU`, `bakedGI = true`, `realtimeGI = false`, `lightProbeSampleCountMultiplier = 4`; `Lightmapping.SetLightingSettingsForScene(scene, settings)`.
6. Save the scene. Render three frames with the player camera. Assert `RenderPipelineManager.currentPipeline is HDRenderPipeline` and `ProbeReferenceVolume.instance.isInitialized`; without the pipeline instance the bake is skipped silently.
7. Start `UnityEngine.Rendering.AdaptiveProbeVolumes.BakeAsync()` (assembly `Unity.RenderPipelines.Core.Editor`, reached by reflection so the asmdef is not edited; `CORE/Editor/Lighting/ProbeVolume/ProbeGIBaking.cs:1923`). It bakes probes with lightmaps disabled (`AdaptiveProbeVolumes.BakePipelineDriver.cs:32`), so the terrain is never lightmapped. It steps from `EditorApplication.update`, so poll `AdaptiveProbeVolumes.isRunning` there. Timeout 45 minutes.
8. When finished: assert `ProbeReferenceVolume.instance.DataHasBeenLoaded()` and a non-empty `*.CellData.bytes` in `ForestData/Bake`. Destroy `BakeProxies`. Set SSAO intensity to 1.0 in every preset profile (`ForestLighting.SetBakedState(true)`, a method WP4 adds to its own file at M5). **Save the scene again** (it now references the baking set). Then call `ForestAudit.Run(false)`: the audit derives `apv` from `ForestProbeBake.HasBakedData()` and remains the only writer of the build report. The bake stage never writes that file itself. In batch mode call `EditorApplication.Exit(0)`.
9. On failure or timeout: cancel, delete partial output, destroy the proxies, leave the scene unbaked, exit with code 1. Nothing is broken.

Rebuilding the scene invalidates the bake (the scene file is rewritten). The audit of the rebuilt scene then reports `"apv": "none"` and captures print it.

UNVERIFIED, each with its knob: whether the async bake completes in batch mode on Apple silicon (fallback: run the menu item in the open Editor; then `Lightmapper.UnityComputeGPU`; then route B); whether the data stays valid after the proxies are destroyed (fallback: keep `BakeProxies` and deactivate it); how dark the floor becomes (knobs in order: `ProbeVolumesOptions.skyOcclusionIntensityMultiplier`, `skyOcclusionAverageAlbedo` on the baking set, proxies from LOD1 for darker or wood-only for brighter).

**Route B (fallback):** `Lightmapping.Bake()`, which is synchronous and works with `-quit`, with the terrain's `m_ReceiveGI` set to light probes; afterwards assert `LightmapSettings.lightmaps.Length == 0`.

Public surface added by WP4 at M5 (a new file, `ForestProbeBake.cs`; compiled as a stub against the version-1 surface):

```csharp
public static class ForestProbeBake
{
    [MenuItem("Gamex/Forest/4 Bake Probes")]
    public static void Bake();                               // the forest scene; never run with -quit
    public static void BakeScene(UnityEngine.SceneManagement.Scene scene, Bounds volume, string outputFolder, Action<bool> onFinished);   // scene-agnostic; P13 runs it on a test scene
    public static bool HasBakedData();                       // for the open scene
}
```

At M5 WP4 may additionally assume `ForestPrefabs.LoadLodMesh` and `LodCount` (bake proxies) and `ForestAudit.Run`; WP5's audit reads `ForestProbeBake.HasBakedData()`. Probe P13 (does an APV sky-occlusion bake of a few cubes complete through `BakeAsync` in batch mode, and with which lightmapper back end?) is the first task of M5 and uses `BakeScene`; if it fails, plan to run the bake from the menu in the open Editor, or route B.

API names for this section were compiled by the first verification (the probe sources are under `probes/api/src`, `P_Bake*.cs`); the behaviour is entirely unverified.

### C.4 Project settings (M6)

`ForestProjectSettings` (a new WP4 file at M6; never called by any build stage): `EnableGpuResidentDrawer()`, `EnableHighQualityLighting()` (PCSS and SSR support) and `Revert()`. Each refuses to run without `-forestAllowProjectSettings`, logs before and after, and writes a backup. Because these change project-wide settings they get no menu item, on purpose: they run only as explicit batch commands after an owner decision (section 15). `HDRenderPipelineAsset.currentPlatformRenderPipelineSettings` returns a struct: copy it, modify the copy, assign it back.

---

## Verification notes

Two independent verifiers checked revision 1 of this spec and of the manifest: one compiled every API name against the project's reference assemblies and looked for contradictions; the other re-measured the assets from the files and judged the scope. Revision 2 re-checked each finding before applying it (the measuring scripts were re-run on the seven FBX files and four glTF buffers that are on disk; the two compile errors and the three usage traps were reproduced with new probes; the cited source lines were re-read). New probes and scripts are under `probes/rev` and `scripts/rev` next to this spec. Nothing was run in Unity.

### What changed

| # | Finding | Checked how | Change |
|---|---|---|---|
| 1 | `FrameTimingManager.gpuFrameTime` does not exist | Compiled both forms (`probes/rev/src/R_FrameBad.cs` fails with CS0117, `R_FrameOk.cs` compiles) | `Budget_FrameTime` and P5 use `CaptureFrameTimings`, `GetLatestTimings(1, FrameTiming[])` and the field `FrameTiming.gpuFrameTime`, guarded by `n > 0` and `IsFeatureEnabled()`. Also noted: the project has `enableFrameTimingStats: 0` |
| 2 | `OverrideTextureCompression` needs its namespace | Compiled both forms (CS0103 without) | Section 5.3 and P3 write `UnityEditor.Build.OverrideTextureCompression.ForceFastCompressor` |
| 3 | The category filter skipped `StealthLoopTests` | Read the test file (no `[Category]`) and `SettingsBuilder.cs` | Three test runs selected by `-assemblyNames`, the third being the existing greybox suite (11.5, 12) |
| 4 | Captures, kit captures and the probe had no menu item | Read 11.4 | `[MenuItem]` on `Capture`, `CaptureFull`, `CaptureKit` and `ForestProbe.Run`; a default output folder for menu runs. `ForestProjectSettings` left version 1 and has no menu item by design |
| 5 | Spawn boulders violated the corridor and spawn clearances | Recomputed with the real rock footprints | Both rules are measured to the collider surface through a per-placement `clearance` and exempt nothing; the spawn composition has explicit positions that satisfy them; the planner validates hand-listed pieces; new test `Scatter_KeepsCorridorsAndSpawnClear` |
| 6 | Placement heights were fixed before root mounds, against a 3 cm audit | Read the stage order | `Plan` writes no height; `AddRootMounds` runs; then the new `ForestScatter.Ground` writes every height. The audit reads `sink` from the placement and uses `Terrain.SampleHeight` plus the terrain's Y. Mounds are limited so they cannot break a slope limit. New test `Scatter_GroundsOnFinalHeights` |
| 7 | `build-report.json` had two writers | Read 8.5 and 11.4 | Only `ForestAudit.Run` writes it; a single-writer table in 11.3; the M5 bake ends by calling the audit, which derives `apv` |
| 8 | Four values were needed across packages but not exposed | Read 11.2 to 11.4 | `ForestPlacement.sink` and `clearance`; the `ForestLightingPresets.Preset` record; the kit report types in `ForestContracts.cs`; a scene-agnostic `BakeScene` (M5). The contract and all signatures were compiled again, with a consumer probe (`R_Use.cs`) |
| 9 | Prefab contract and test ownership inconsistencies | Read | `LOD3` and `__lod3` allowed (one to four levels); the cull-distance encoding defined; WP5 writes every test against public surfaces; the manifest's two tag styles documented |
| 10 | Rock faces: wrong size, open one-sided sheets, other geology | Re-ran `fbx_objects.py` and `topology.py`: FBX 7.11 x 4.98 x 5.62 m and 4.87 x 4.73 x 3.47 m; 886 and 754 open boundary edges | Dropped from version 1 (layout, kit, contract ids, budgets). Kept in the manifest as M6 with bounds measured from the FBX. The north outcrop and south bank are built from moss rocks |
| 11 | `rock07` bounds ignored its 0.679 scale | Measured from the FBX (`Lcl Scaling` 67.899) | Manifest corrected: 1.69 x 0.74 x 1.22 m |
| 12 | Manifest bounds are right-handed; a corner-for-corner comparison fails after import | Simulated on all 48 objects under four mirrors | Bounds stay in source space (`boundsSpace`), and the assertion became a rule that holds under any mirror (5.6.1). Consumers use only size, Y range and footprint radius |
| 13 | Objects of the stumps, log and firs describe the glTF, not the FBX | Not checkable here: those FBX files are not on disk and bulk downloads are not allowed in this phase | `objectsSource` per model; `fbx_objects.py` measures every FBX at fetch time; the kit build refuses unmeasured models. Twig share corrected to 88.2 to 98.7 % |
| 14 | No moss rock, stump or log gives standing cover; rocks with flat undersides would float | Recomputed tops and undersides from the measured bounds; re-ran `slices.py` on the log | Honest roles everywhere; a sink rule derived from each rock's own underside; the log is bedded by 0.24 m times scale; stumps are floor dressing; section 7.4 rewritten; cover and sight metrics and `SightProxy` removed from version 1 |
| 15 | The `trunk_c` texture set was missing | Read the `/files` listing and the glTF; HEAD on the three URLs (200, sizes match) | Three files added to the manifest (23.0 MB); `noUvFallback` replaces `triplanarIfNoUv`; a defined test for usable UVs; `ForceTriplanar` |
| 16 | "One photographed place" overstated | Read categories and authors of all 24 assets | Section 2 says which assets belong to `pine_forest` and which do not |
| 17 | Thinned foliage at eye level is the main risk | Recomputed the keep ratios and enlargements | Experiment E1 with an optional scan level is the first task of M3 and the go or no-go for M4; risk 1 rewritten |
| 18 | Version 1 was not limited | Counted files and tests | Version 1 is M0 to M4 with one preset and no bake: 988 MB instead of 1,207 MB, 27 C# files instead of 29, owner reviews at M2, M3 and M4. M5 moved to Appendix C |
| 19 | Trail ruts were two texels wide under a three-texel blur | Arithmetic | Alphamap 2048, no blur within 2.5 m of the trail, analytic edges; fallback and probe P15 |
| 20 | Cross-fade citation was wrong; eight-layer evidence weaker than stated; sky rotation sign | Re-read the cited sources | Section 5.6.4 cites what is known and names the one run-time unknown; section 6.3 states the weaker evidence; the rotation sign is derived in C.2 |

One verifier statement was not taken over as written: "the kit has no closed rock of standing height" is true at scale 1 and with the first plan's deep sink, but with the corrected sink two rock shapes reach 1.8 to 2.0 m at scale 1.7 to 1.8. The spec uses them in the boulder field and still does not count a peak as standing cover.

### What remains unverified

Everything in Appendix B. The items that matter most, in the order they will be met:

1. **Which axis the importer mirrors, and the imported transforms and slot names** (P1, M0). The build does not depend on the answer.
2. **Whether `alphamapResolution` 2048 is accepted and whether layers 5 to 8 render** (P15, P10, M1). Fallbacks: 1024; four layers.
3. **Whether LOD cross-fade dithers and where LODs switch** (P11). Fallback: one switch, one scale constant.
4. **The stump, log and fir FBX geometry against the manifest** (fetch time, M2 to M4). The kit build cannot start before it is measured.
5. **How thinned fir foliage looks at 1 to 3 m, and what the scan costs** (E1, M3). This decides whether M4 happens.
6. **Whether `fir_tree_01.fbx` (249 MB) imports in reasonable time and memory, and whether `trunk_c` has usable UVs** (M4).
7. **Frame time.** No number exists; `FrameTiming` data in the Editor with frame-timing stats off is itself unverified (P5).
8. Run-time behaviour of the probe bake and the importer's lat-long mapping: M5 only.

Asset files that were not re-checked in this revision: the 1k EXR skies (channel order and row order are handled by guards), and every texture's content (only sizes and md5 values come from the API).
