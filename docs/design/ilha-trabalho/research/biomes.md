# Island biomes and vegetation

Research note for the whole-island design. Written 2026-10-21 by the biomes researcher. Nothing in the project was changed,
Unity was not launched, no git command was run. Catalogue data and scripts are in the session scratchpad
(`.../scratchpad/biomes/data`, `.../scratchpad/biomes/scripts`); they are not needed to act on this note.

Labels used below:

- **[M]** measured in this project. The record file is named.
- **[V]** verified today from a catalogue API, a store page or a cited source (link in the text and in section 12).
- **[E]** my estimate. The reasoning is given. Not measured.
- **UNVERIFIED**: could not be confirmed. Treat it as a risk.

---

## 0. The answer in one page

**Eight zones** (seven natural biomes plus the facility grounds). They fit one volcanic island with a wet windward side and a dry
leeward side, as Kauai or Lord Howe Island do. Working names, with a short Portuguese name for `ilha.md`:

| # | Biome | Share of land [E] | Main residents | Sightline (open beyond 30 m) [E target] | Build class |
|---|---|---|---|---|---|
| B1 | Misty fir forest (Mata de abetos) | 20 % | Raptors; Compsognathus at the edges | 8 % (measured) | **Exists** |
| B2 | Fern-and-cycad ravine (Grota das samambaias) | 8 % | Dilophosaurus; Compsognathus | under 5 % | Same code, **bought kit** |
| B3 | Fern savanna with lone conifers (Savana de samambaias) | 20 % | Gallimimus herds, Parasaurolophus, Triceratops, sauropod; T. rex hunts it | 60 to 85 % standing | Same code + bought kit + **far-view system** |
| B4 | Blowdown and burn scar (Clareira queimada) | 8 % | T. rex crossing; Compsognathus scavenging | 30 to 50 % | **Almost free** (CC0) |
| B5 | Basalt highlands, scree and lava tubes (Planalto e cavernas) | 12 % | Raptor dens; caves as refuges | 70 to 90 % outside | **New systems** (caves, cliffs, far view) |
| B6 | Leeward cycad scrub on dry ridges (Cerrado de cicas) | 12 % | Triceratops, Gallimimus | 20 to 35 % | **Almost free** (CC0) + optional cycads |
| B7 | Windward coast: cliffs, dunes, beach (Costa) | 15 % | Compsognathus; rescue point | 80 to 95 % | **New systems** (sea surface, grass at scale) |
| B8 | Facility grounds and outposts (Instalações) | 5 % | Raptors indoors; Compsognathus | 20 to 60 % | Hard-surface kit (other topic) + lawn grass |

What matters most:

1. **The Mesozoic signature plants do not exist as CC0 scans.** Poly Haven (521 models) and ambientCG (34 models, 32 of them food)
   have no tree fern, cycad, horsetail, ginkgo, araucaria or bracken-height fern [V, section 3]. The only cheap way to put them on the
   island is to buy single models (about $6 to $70 each) and push them through the existing tree baker and foliage thinner. A
   $40 test purchase and one arm's-length capture under the torch, like experiment E1 was for the young firs, should come before
   any biome that depends on them (B2, B3).
2. **Two new biomes are almost free.** The burn scar (B4) reuses the forest's own kit (snags are the fir's wood sub-mesh without its
   twigs) plus Poly Haven's `burned_ground_01`. The dry ridge (B6) is Poly Haven's `namaqualand` collection, a second coherent scan set
   like `pine_forest`: shrubs with authored LODs, boulders, cliffs and four matching ground textures.
3. **Open biomes are not cheaper by default.** The forest pays for its density with a 30 m night mist and culls at 50 to 55 m [M]. A
   savanna, a ridge or a coast seen by moonlight needs distant tree silhouettes, which the pipeline cannot make yet (no impostors,
   spec 1.2). Fewer leaves near the player do help with the torch's shadow cost, two thirds of which is dense alpha-tested
   foliage [M].
4. **Grass at scale, sea surface, caves and multi-tile streaming are the new systems.** None of them exists. Grass is optional: it
   is not period-appropriate for open ground (section 2.2), so it can be kept to the facility lawns and the dunes.
5. **Density decides which predator can follow the player.** The forest's young firs stand 2.2 to 3.2 m apart [M]. An agent of 2 m
   radius (a large theropod) cannot pass through them and has to keep to corridors, while a raptor can. If gaps follow this rule
   in every biome, the map tells the player where the T. rex cannot follow without any invisible walls.

---

## 1. What the forest records say (rules that carry over)

All from `Logs/forest-work` (spec, `build/STATE.md`, `build/issues/D1-optimisation.md`, `D2-density.md`, `N1-night.md`, `W1-runtime.md`).

| Fact | Value | Record |
|---|---|---|
| Playable core | 1.87 ha in one 256 m tile (6.55 ha) | spec 3.1, D2 |
| Instances / GameObjects | 5,825 / 23,274, i.e. **3,115 instances and 12,446 GameObjects a hectare** | D2 section 1 |
| Worst view, player build, 2560 x 1440 | 9.6 to 10.5 ms (D2, no torch); 11.3 to 12.5 ms torch on (N1); **10.7 to 11.8 ms** after the wind work with the moon's shadows shortened (W1 R2 paired estimates); 12.0 to 12.1 ms with one to three raptor-sized pushers | D2, N1, W1 14.8 |
| Empty-scene floor (player) | about 3.3 ms; so forest content costs about 7.4 to 8.5 ms at the worst view | D2 section 7 |
| Budget | 12.5 ms median, 15 ms p95 (16-frame windows) | D2 |
| Triangles / vertices budget | 21.5 M / 36 M (worst view 17 to 21.5 M) | N1 section 6, W1 12.3 |
| What density costs | about +2.7 ms per "1x" of everything, even with the GPU Resident Drawer | STATE D1, note 1 |
| What pays for density | mist 20 to 30 m with culls at 50 to 55 m (saves 0.5 to 1.1 ms); moon shadows 50 m / 3 cascades -> 30 m / 2 (saves 1.25 to 1.7 ms and 3.4 to 4.5 M triangles) | D1, W1 12.3 |
| Torch | +0.5 to +1.3 ms a view, two thirds of it its shadow casters (dense alpha-tested foliage, +2 to +2.9 M triangles); 512 px costs the same as 1,024 px | N1 4.2 |
| Wind and bending | +0.56 to +1.23 ms (on - off) | W1 12.3 |
| GPU occlusion culling | culled 0 instances through alpha-tested needles and cost 0.6 to 1.9 ms; "revisit only with solid occluders (rock faces, ridges)" | STATE D1, note 4 |
| Memory | textures 930 MiB, meshes 358 to 382 MiB for the forest kit alone | D2, W1 14.9 |
| Terrain | HDRP Terrain Lit: **at most 8 layers per terrain tile**; the forest uses all 8; layers 0 to 3 alone are a complete look | spec 6.3; HDRP `terrain-lit-material.md` line 3 |
| Frozen code surface | `ForestClass` (11 classes), `ForestSurface` (8 surfaces, "gameplay will read it"), `ForestPrefabIds` (fir ids), one tile, cells 0..7 | `Assets/Game/Editor/Forest/ForestContracts.cs` |
| Fern size limit | `fern_02` is 0.21 to 0.43 m tall; scaling past 1.6 makes the frond anatomy look fake | `research/assets-vegetation.md` section 5 |
| Player | eye 1.65 m standing, 0.95 m crouched, crouched capsule 1.1 m; walk 3.2 m/s (noise 8 m), crouch 1.6 m/s (noise 2 m), sprint 6 m/s (noise 18 m) | `Scripts/Player/PlayerController.cs` lines 17 to 33 |
| Raptor | sight 28 m (a crouched player only within 0.6 of that, 16.8 m), 110 degrees, chase 9 m/s | `Scripts/Dinosaurs/RaptorAI.cs` lines 29 to 59 |

Three consequences for every new biome:

- **Any hiding plant must reach 1.1 to 1.2 m** to cover a crouched player (capsule 1.1 m, eye 0.95 m). `fern_02` tops out at about
  0.7 m even at scale 1.6, so it can never be hiding cover. Waist-high fern needs another asset.
- **Every biome needs its own measured slice.** The forest's method works as it is: a 1 to 2 ha test area, the player harness
  (`Assets/Game/Diagnostics/ForestFrameHarness.cs`), 29 views, paired runs. The budget for a biome is 12.5 ms with the torch on, the
  wind on and at least one large pusher.
- **The forest at its own density does not scale to the island as GameObjects.** At 12,446 GameObjects a hectare, one 256 m tile
  of forest is about 81,000 GameObjects, and 100 ha of forest-type biome would be about 1.2 million. Streaming tiles and drawing the
  small props (ferns, branches, grass) without one GameObject each are prerequisites (section 7.1). This is arithmetic on the
  measured rate, not a measurement.

---

## 2. One island, eight biomes

### 2.1 Why these zones can sit on one island

A volcanic island 20 to 40 km from nowhere has a wet windward side, a dry leeward side in the rain shadow of a central peak, deep
erosion ravines, a summit of bare rock and lava tubes, and a cliff and dune coast. Kauai shows this in one place: the Nā Pali cliffs,
the wet interior, the dry Waimea Canyon side ([Nā Pali](https://commons.wikimedia.org/wiki/Category:N%C4%81_Pali_Coast_State_Park),
[Waimea Canyon](https://commons.wikimedia.org/wiki/Category:Waimea_Canyon), [Kauai](https://commons.wikimedia.org/wiki/Category:Kauai)).
[Lord Howe Island](https://commons.wikimedia.org/wiki/Category:Lord_Howe_Island) shows mist forest and palms over sea cliffs at a small scale.
Proposed layout [E]:

```
                 windward (wet, misty)                        leeward (dry)
   coast B7 -> fir forest B1 (cloud belt) -> ravines B2 ->  plateau B3 + B4 -> highlands B5 -> dry ridges B6 -> coast B7
                                                    \___ the island's core: the T. rex basin ___/
   facility B8: main station on the coast, outposts and fence lines crossing B1, B3 and B6
```

How this keeps horror pacing in an open world (biome view only; the structure itself is another topic):

- **Rings of danger.** The coast and outer facility have the light threat (Compsognathus). The forests and ravines have the clever
  or ambushing ones (raptors, Dilophosaurus). The central basin has the apex. Objectives can sit anywhere. Every route to the
  later ones still crosses at least one exposed biome.
- **Light alternates by itself.** Under canopy (B1, B2) the night is near black without the torch: frame means 0.03 to 0.08 [M, N1].
  The open biomes get the full 0.3 lux of the moon, so the player can move without the torch there, but a lit torch is seen from far
  off. Going from forest to savanna changes the player's habit: torch on, then torch off.
- **Sound masks.** Surf on the coast, wind on the ridges and dripping in the ravine are places where footsteps carry less far. They
  are safe windows that the player learns to read.
- **Refuges by size.** Lava tubes, tree-fern thickets and facility rooms with doors are places a 12 m animal cannot enter. They are
  the open world's "safe rooms", and only the small threats can follow there.
- **Telegraphing.** Parasaurolophus alarm calls and Gallimimus stampedes in B3, snapped trees in B4 (the T. rex's path) and distant
  footfalls give the player warning before a big encounter.

### 2.2 A Mesozoic palette that a player still reads as real

Use living plants from ancient lineages, so every plant is real and still says "dinosaur era". The ages below are general botany
knowledge unless a source is linked. They were not verified species by species.

| Group | Living genera to model on | Era signal | Where |
|---|---|---|---|
| Araucarian conifers | Araucaria araucana (monkey puzzle), A. heterophylla (Norfolk Island pine), Wollemia | Jurassic | B3, B6, B7 |
| Other conifers | firs and pines (already in), podocarps | Cretaceous | B1, B5 krummholz |
| Tree ferns | Dicksonia, Cyathea, Cibotium | Jurassic | B2, B4 regrowth |
| Ground ferns | royal fern Osmunda (180-million-year-old fossils nearly identical: Bomfleur et al. 2014, *Science*), king fern Angiopteris, Gleichenia/Dicranopteris, bracken-like tall fern | Jurassic | B2, B3 |
| Cycads | Macrozamia, Encephalartos, Cycas | Jurassic, "age of cycads" | B2 banks, B6, B8 planted beds |
| Ginkgo | Ginkgo biloba | Jurassic | B3 groves |
| Horsetails | Equisetum hyemale, E. giganteum (several metres tall) | Carboniferous to now | B2 bed, B3 hollows |
| Early flowering plants | magnolias, laurels, plane trees, palms (Late Cretaceous) | Cretaceous | B1 edges, B7, B8 |

**On grass.** Grasses did exist by the latest Cretaceous: grass phytoliths turn up in dinosaur dung from India
([Prasad et al. 2005, *Science* 310:1177](https://repository.ias.ac.in/43622)). Grasslands did not, and open Jurassic ground was
fern savanna. The Morrison Formation is described as a mosaic of gallery forests of conifers and tree ferns, and fern savannas with
scattered Araucaria-like conifers
([Paleobiota of the Morrison Formation](https://en.wikipedia.org/wiki/Paleobiota_of_the_Morrison_Formation);
[USGS synthesis](https://pubs.usgs.gov/publication/70027121)). A 2021 paper calls open fern cover "a Mesozoic analogue of today's
meadows" ([Annales Societatis Geologorum Poloniae](https://bibliotekanauki.pl/articles/1835986.pdf)). Caveat: for the Late
Cretaceous (T. rex, Triceratops), open ground may have been mostly herbaceous flowering plants. That claim comes from a weak source,
a game wiki ([Saurian wiki](https://sauriangame.fandom.com/wiki/Fern_prairie)), and is UNVERIFIED.
**Recommendation:** open ground is fern savanna. Grass appears only around the facility, as escaped lawn and verge, which also tells
the story that people brought it.

### 2.3 What the evidence says about the coordinator's species placement

- **Dilophosaurus in the ravine: supported.** The real animal was about 7 m and 400 kg
  ([Wikipedia](https://en.wikipedia.org/wiki/Dilophosaurus)). The Kayenta Formation was "a river system in a seasonally dry setting",
  with conifers along the waterways and dunes moving in and out, "compared to a river oasis" (same page). A seasonal creek in a ravine
  (B2), with dry ridges (B6) above it, is its real habitat. Avoid the frill and the venom spit: they are the film's inventions.
- **Compsognathus at the coast and the facility: supported.** It is 0.7 to 1.4 m long and 0.3 to 3.5 kg. Late Jurassic Europe was "a
  dry, tropical archipelago" with lagoons "between the beaches and coral reefs" ([Wikipedia](https://en.wikipedia.org/wiki/Compsognathus)).
  So the first, light threat belongs on the coast and in dry scrub, which also suits a demo that starts near the shore.
- **The sauropod as a landmark in the fern savanna: supported** by the Morrison picture above.
- **The T. rex in the core: workable,** if the core is open. The forest's tree spacing already keeps an agent of 2 m radius on
  corridors (section 5). The core should be B3 and B4 inside a ring of B5 highlands, with three or four passes, so geography gates it.

---

## 3. What exists as CC0 (checked by API today)

- **Poly Haven** (`https://api.polyhaven.com/assets?t=models|textures|hdris`): 521 models, 869 textures, 997 HDRIs [V]. The model list
  has the same 521 ids as the forest research of 2026-10-13. One texture is new since then (`raw_wooden_floor_01`, not relevant).
  Nature content comes in coherent collections: `pine_forest` (in use), `namaqualand` (South African dry shrubland: shrubs,
  aloes, boulders, cliffs, ground), `verdant_trail` (grassveld trail: rocks, cliffs, roots, shrubs), `smugglers_cove` (coast:
  coastal cliffs, shore rocks, sand slabs, tropical houseplants, pier). There is no palm, tree fern, cycad, horsetail, ginkgo,
  araucaria, liana or bamboo model. That matches the forest research (`research/assets-vegetation.md`, section 1).
- **ambientCG** (`https://ambientcg.com/api/v2/full_json`): 2,925 assets [V]. Of the 34 `3DModel` assets, 32 are food; the other
  two are `3DTreeStump001` and `3DStick001`. The useful parts are materials: Ground 128 (including a November 2025 set of
  dune and coastal sands, `Ground092A` to `Ground101`), Rock 67, Rocks 26, Gravel 44, Moss 4, Bark 15, Grass 8, Lava 5,
  ScatteredLeaves 9, Fence 12 (wire mesh with opacity, usable as chain-link), Asphalt 46, Concrete 61, MetalWalkway 14. There are also
  61 atlases (30 LeafSet, 8 Foliage grass cards, 1 PineNeedles) and 5 terrain heightmaps (`Terrain001` to `005`).

Asset pages are `https://polyhaven.com/a/<id>` and `https://ambientcg.com/view?id=<id>`. Triangle counts for Poly Haven
vegetation and rocks are in `Logs/forest-work/research/assets-vegetation.md` (2.1) and `assets-ground-rocks-sky.md`.

---

## 4. The biomes

Each card follows the same pattern: role, look at night, sound, movement, analogue and photos, plants, CC0 kit, purchases,
stealth, cost and build class. "Ground noise" is a proposed multiplier on the footstep noise radius (walk 8 m), read from
the surface map that the terrain painter already writes (`ForestData/SurfaceMap.png`, 0.5 m texels, layer index = surface id).
These multipliers are a design proposal [E]. Nothing in the game reads them yet.

### B1 Misty fir forest (exists)

- **Role.** Windward cloud belt, the starting forest, raptor territory.
- **Night look.** In the torch: trunks, fir boughs and ferns, with a shaft of light in the mist. Outside it: black spires against
  the moon's glow in a 30 m mist [M].
- **Sound.** Wind in the needles, dripping, creaking boughs. The rustle hook exists (W1).
- **Movement.** Thickets part around the player's body (W1). Ground: litter 1.0, dry needles 1.1, mud 0.8, gravel trail 1.3.
- **Analogue and photos.** Pacific Northwest temperate rainforest:
  [Hoh Rain Forest](https://commons.wikimedia.org/wiki/Category:Hoh_Rain_Forest),
  [Olympic National Park](https://commons.wikimedia.org/wiki/Category:Olympic_National_Park).
- **CC0 to add for variety.** `pine_sapling_medium` and `pine_tree_01` (heavy, 205 and 660 MB). `fir_sapling` and `pine_sapling_small`
  (seedlings, 120 to 160 thousand triangles each, so they need the thinner). `moss_01`, `pine_roots`, `bark_debris_01`.
  ambientCG `Bark014` (fir bark) and `Ground070`, `Ground076`, `Ground084` (forest floors with roots).
- **Stealth.** Measured: from 200 core points, 8 % of eye-level rays are clear beyond 30 m and 33 % beyond 15 m [M, D2]. Thickets
  give visual cover only: the raptor's sight test is a physics linecast, and a thicket has only thin trunk capsules (spec 7.4).
- **Cost.** 10.7 to 11.8 ms [M].
- **Build.** Done. Edges toward B2 and B4 need shared terrain layers (section 7.2).

### B2 Fern-and-cycad ravine

- **Role.** Erosion ravines (barrancos) cut into the windward slopes. A seasonal creek bed that is dry or a trickle, with no water
  gameplay. Dilophosaurus ambush ground; Compsognathus nest here.
- **Night look.** A roof of tree-fern umbrellas 3 to 8 m up, cycad crowns on the banks, horsetail stems in the bed, glistening wet
  rock, and mist pooling in the bottom. Silhouette: radiating fronds against a strip of sky between the walls, different from B1's
  spires.
- **Sound.** Dripping, trickling water, frogs (Jurassic lineage) and close echo off the walls. The most claustrophobic soundscape.
- **Movement.** Slow and close. Fronds bend around bodies; the existing push system already handles fern fronds per frond (W1 14.4).
  A moving frond gives away whatever moves through it, and that works both ways. Ground: wet moss 0.7, mud 0.8, creek pebbles 1.3,
  splash 1.6 if a trickle exists.
- **Analogue and photos.** Carnarvon Gorge, Queensland: a sandstone gorge with Macrozamia moorei cycads up to about 7 m, palms and
  ferns ([Wikipedia](https://en.wikipedia.org/wiki/Carnarvon_Gorge),
  [Carnarvon NP](https://commons.wikimedia.org/wiki/Category:Carnarvon_National_Park),
  [Macrozamia moorei](https://commons.wikimedia.org/wiki/Category:Macrozamia_moorei)). Tree-fern gullies:
  [Tarra-Bulga NP](https://commons.wikimedia.org/wiki/Category:Tarra-Bulga_National_Park),
  [Lamington NP](https://commons.wikimedia.org/wiki/Category:Lamington_National_Park),
  [Dicksonia antarctica](https://commons.wikimedia.org/wiki/Category:Dicksonia_antarctica),
  [Cyathea](https://commons.wikimedia.org/wiki/Category:Cyathea), Hawaii's
  [Cibotium glaucum](https://commons.wikimedia.org/wiki/Category:Cibotium_glaucum),
  [king fern Angiopteris](https://commons.wikimedia.org/wiki/Category:Angiopteris_evecta),
  [Equisetum giganteum](https://commons.wikimedia.org/wiki/Category:Equisetum_giganteum).
- **Dominant plants.** Tree ferns (canopy), king fern and royal fern (understorey 1 to 2 m), cycads on the banks, horsetails in the
  bed, moss.
- **CC0 kit (support only).** `fern_02` (low layer), `moss_01`, `rock_moss_set_01` and `_02`, `single_root`, `root_cluster_01` and
  `_02` (red soil baked in, which needs a tint or ferns over the rim). Textures: `mud_forest`, `river_small_rocks`,
  `dry_riverbed_rock`, `rock_pitted_mossy`, `mossy_rock` (Poly Haven); `Ground021`, `Ground022` (river pebbles), `Rocks024L`
  (flat river rocks), `Rock036`, `Rock057`, `Rock059`, `Rock064` (mossy rock) (ambientCG). Possible stand-in to test:
  `anthurium_botany_01` scaled 2.5 to 3 for giant-leaf clumps (UNVERIFIED in `assets-vegetation.md` 4).
- **Purchases (all of the signature plants).** None verified as game-ready scans:
  - BotaniCraft3D on RenderHub: [Dicksonia Tree Plant](https://www.renderhub.com/botanicraft3d/dicksonia-tree-plant), $8.25,
    126,279 polygons, FBX, 2K, Extended Use licence; [Equisetis plant](https://www.renderhub.com/botanicraft3d/equisetis-plant),
    $7.50, 45,801 polygons; [Weichselia](https://www.renderhub.com/botanicraft3d/weichselia-plant-2) (a Mesozoic fern, price not
    read). "Game ready" is not claimed on these pages [V].
  - 3D Tree Team [Fern Garden](https://3dtreeteam.gumroad.com/l/FernGarden): Cyathea cooperi and Dicksonia antarctica, FBX, $70 per
    a search snippet. The page could not be read. UNVERIFIED.
  - Cycads: a Cycas revoluta (sago palm) pack on [Fab](https://www.fab.com/listings/33d9a24c-0413-40f5-9703-bb2a02d5bbd5), built for
    Nanite per its snippet, price unread. SpeedTree once sold a Sago Palm for Unity at $27, but single models are no longer sold
    (section 8).
- **Stealth.** Target under 5 % of rays clear beyond 30 m and under 10 % beyond 15 m: the densest biome. Cover is everywhere but
  short. The ravine floor is 3 to 10 m wide, too narrow for a T. rex (2 m agent radius plus fern trunks), so this is a refuge from it
  and a trap with the Dilophosaurus.
- **Cost [E].** 10 to 12.5 ms: 0.8 to 1.1 times the forest's content. Tree-fern crowns are alpha-tested like fir boughs, but views
  are short (along the bed, about 40 m), so culls of 35 to 40 m are possible. The walls are the solid occluders D1 said to wait for:
  GPU occlusion culling deserves one measured try here. Risk: over budget if the kit is heavy.
- **Build.** Same code, bought kit. The terrain already has a gully carve (spec 6.2 step 5), the scatter has a moisture term near the
  gully (spec 7.2), and the thinner and the frond push exist. The work is the kit and its wind profiles. A dry bed avoids any water
  system.

### B3 Fern savanna with lone conifers

- **Role.** The central plateau: herds, the sauropod landmark, the T. rex's hunting ground. The most "Jurassic" view on the island.
- **Night look.** A sea of waist-high fern silvered by the moon. Lone monkey-puzzle candelabras and ginkgo groves stand on the
  skyline, with boulder piles (kopjes) as dark islands. The sky is the biggest light. The torch is seen from very far.
- **Sound.** Wind through fronds in long waves, herbivore calls carrying far, insects. Silence when a predator is near is a cue.
- **Movement.** Crouched inside fern at 1.2 to 1.8 m, the player is hidden but each step rustles. Standing, the player sees over it
  and is seen. Ground: fern litter 0.9, bare soil 0.8, stones 1.3.
- **Analogue and photos.** Araucaria on volcanic ground in
  [Conguillío NP, Chile](https://commons.wikimedia.org/wiki/Category:Conguill%C3%ADo_National_Park) and
  [Araucaria araucana](https://commons.wikimedia.org/wiki/Category:Araucaria_araucana); tall fern fields:
  [bracken](https://commons.wikimedia.org/wiki/Category:Pteridium_aquilinum) and Hawaii's uluhe fern
  [Dicranopteris linearis](https://commons.wikimedia.org/wiki/Category:Dicranopteris_linearis);
  [Ginkgo](https://commons.wikimedia.org/wiki/Category:Ginkgo_biloba). The Morrison "fern savanna" of section 2.2 is the paleo model.
- **Dominant plants.** Tall fern (1.2 to 2 m) over 60 to 80 % of the ground, horsetail flats in hollows, araucaria and ginkgo at 10 to
  30 a hectare in clumps, cycads on rocky rises.
- **CC0 kit.** Rocks: `namaqualand_cliff_01` (the only "pile of boulders" shape, 8.3 x 5.0 x 4.4 m), `boulder_01`, `rock_face_01` and
  `_02` bedded into rises. A landmark broadleaf: `jacaranda_tree` (19.5 m, 24 m crown, 3.9 M triangles before thinning). Lone
  `fir_tree_01`. Ground: `dry_decay_leaves`, `dirt`, `rocks_ground_02`, `sparse_grass` (Poly Haven). `fern_02` only as a low
  layer under the tall fern.
- **Purchases.** Tall fern: BotaniCraft3D
  [Bracken mature plant](https://www.renderhub.com/botanicraft3d/bracken-mature-plant) (low-poly, FBX, price not read), or
  EVERYPlant Bracken on CGTrader ([listing](https://www.cgtrader.com/3d-models/plant/other/everyplant-bracken-20-models), price not
  read). Araucaria: BotaniCraft3D [Araucaria Tree series](https://www.renderhub.com/botanicraft3d/araucaria-tree) (88,000 to
  308,000 polygons per search snippet) or the CGTrader
  [monkey puzzle](https://www.cgtrader.com/3d-models/plant/bush/araucaria-araucana-monkey-puzzle-tree) (six variants, FBX). Ginkgo:
  no candidate checked.
- **Stealth.** Standing, 60 to 85 % of rays should be clear beyond 30 m; crouched inside a fern patch, under 15 %. Cover comes in
  patches with gaps of open ground. That suits throwables: a stone into the fern draws a head toward a rustle. A T. rex roams
  freely, because trees are 10 to 30 a hectare, about 18 to 30 m apart. The kopjes are the only hard cover.
- **Cost [E].** 8.5 to 11 ms, 0.6 to 0.9 times the forest's content near the player. Ferns are cheap: a fern patch is about 5,600
  triangles within 16 m and 2,400 to 40 m [M, spec 5.6.4]. At 70 % cover that is roughly 1 to 1.5 M triangles in the view cone
  [E]. There are fewer torch shadow casters. The open question is distance: the 30 m mist can be thinned (for example 60 m, a
  biome-local fog volume), but then trees beyond the 55 m cull pop out of a clear sky. Far silhouettes need impostors, a new system.
  Herds of 10 to 20 skinned animals are an animation cost and are not counted here.
- **Build.** Same scatter and patch code with a bought kit, plus the far-view system (section 7.1, P4).

### B4 Blowdown and burn scar

- **Role.** A storm-felled, partly burned stand on the plateau rim, between B1 and B3. It is the visible trail of something huge
  (snapped trunks) and a Compsognathus feeding ground (carcasses).
- **Night look.** Bare grey snags and root plates in silhouette. Charcoal-black ground with pale ash. Young firs and ferns pushing
  through. Under the moon it is the most graphic biome: bones of a forest.
- **Sound.** Creaking leaning trunks, wind whistling through dead branches, ash and twigs crunching underfoot.
- **Movement.** Linear cover: crouch behind or under fallen trunks, climbing over them is slow, gaps line up into firing lanes for
  sight. The noisiest floor on the island: charcoal and dead branches 1.4 to 1.6, ash 1.0.
- **Analogue and photos.** [Yellowstone after the 1988 fires](https://commons.wikimedia.org/wiki/Category:Yellowstone_fires_of_1988),
  [windthrow](https://commons.wikimedia.org/wiki/Category:Windthrow),
  [Mount St. Helens](https://commons.wikimedia.org/wiki/Category:Mount_St._Helens) blast zone, and tree ferns regrowing after the
  [2009 Victorian bushfires](https://commons.wikimedia.org/wiki/Category:2009_Victorian_bushfires). Tree ferns are "often the
  first plants to show signs of recovery" after fire
  ([University of Melbourne](https://findanexpert.unimelb.edu.au/news/12942-tree-ferns-are-older-than-dinosaurs.-and-that's-not-even-the-most-interesting-thing-about-them));
  growth of 335 tree ferns after the 2009 fire was measured by Blair et al. (PLOS ONE, doi:10.1371/journal.pone.0176908).
- **Dominant plants.** Dead firs (snags 8 to 18 m), fallen trunks, upturned root plates, regrowth of young firs (seedlings 0.7 to
  1.3 m), ferns and a few tree ferns if B2 kit exists.
- **CC0 kit, nearly all of it in the project already.** Snags: `fir_tree_01` with its twig sub-mesh dropped. The wood is a separate
  sub-mesh of 59,420 to 110,700 triangles [`assets-vegetation.md` 6.1]; the tree baker already treats it separately, and a third of
  the stand can use the half-dead `fir_tree_01_c` as it is. Charred bark: a darker tint variant of the bark material, the same
  mechanism as the existing `__tint1` swap (spec 7.5). Logs and debris: `dead_tree_trunk`, `dead_tree_trunk_02`,
  `dry_branches_medium_01`, `tree_stump_01` and `_02`, `bark_debris_01`. Root plate: `root_cluster_01` (1.51 m, red soil, tinted
  darker). Regrowth: `fir_sapling`, `fir_sapling_medium`, `fern_02`. Ground: `burned_ground_01`, `wood_chips` (splinters),
  `forest_leaves_04` (unburned patches), `forest_ground_04` (Poly Haven); ambientCG `Ground028` (dead grey), `Ground038`.
- **Purchases (optional).** Real charred trunks: Burned Wood (GreenBugGames),
  [$24.99](https://assetstore.unity.com/packages/slug/173899), HDRP listed for Unity 2020.3, last update October 2023 (content per a
  snippet: 14 burned tree types with LODs, UNVERIFIED). Dead Tree Pack (Jesse Mario),
  [$26.99](https://marketplace.unity.com/packages/3d/vegetation/trees/dead-tree-pack-239578), HDRP listed, 955 MB, updated February
  2026.
- **Stealth.** Target 30 to 50 % of rays clear beyond 30 m: long lanes between snags, blocked low by logs. Logs are crouch cover
  (0.3 to 1 m), root plates are standing cover (1.5 to 3 m). The T. rex can cross where the logs leave 4 m gaps.
- **Cost [E].** 7.5 to 9.5 ms, 0.5 to 0.7 times the forest's content. Snags are opaque wood with no alpha-tested foliage, so the torch
  and moon shadow passes are cheap. The regrowth is low. The risk is longer sight in the mist; the trees are skeletal, so popping
  is less visible.
- **Build. Almost free.** It needs a "drop the foliage sub-mesh" option in the tree baker, a fallen-tree placement rule (the log
  grounding rule exists, spec 7.3), one tint variant, and a terrain palette (section 7.2). This is the cheapest new look, and the
  best biome to force the generalisation of the frozen contract (section 7.1, P2).

### B5 Basalt highlands, scree and lava tubes

- **Role.** The ring around the central basin and the summit. The passes into the core. Caves are refuges (only small animals
  follow) and raptor dens (deep ones). Possible objective sites (an antenna on the summit for the rescue radio).
- **Night look.** Black rock and grey scree under a big sky, wind-stunted firs (krummholz), steam or fog in cracks. Inside a
  tube: total darkness except the torch, wet walls, roots hanging through the roof.
- **Sound.** Wind howling over the ridges (masks footsteps), stones clattering down scree, cave reverb and drips.
- **Movement.** Slow on scree, which is the loudest ground at 1.6. Ledges and boulders give standing cover. On the skyline the
  player is a silhouette against the sky, which is the main exposure. In tubes, crouch-only passages give gaps too small for big
  agents.
- **Analogue and photos.** [Hawaii Volcanoes NP](https://commons.wikimedia.org/wiki/Category:Hawaii_Volcanoes_National_Park),
  [Thurston Lava Tube](https://commons.wikimedia.org/wiki/Category:Thurston_Lava_Tube),
  [lava tubes](https://commons.wikimedia.org/wiki/Category:Lava_tubes), [scree](https://commons.wikimedia.org/wiki/Category:Scree).
- **Dominant plants.** Little: krummholz firs, ferns and moss in cracks, a few cycads on sheltered ledges.
- **CC0 kit.** Rocks: `mountainside` (10 x 10.5 m backdrop, 153,472 triangles at LOD0), `rock_face_01` and `_02` (open sheets, so they
  must be bedded into slopes: spec section 0), `boulder_01`, `rock_07`, `rock_09`, `stone_01`, recoloured `namaqualand_boulder_*`.
  Ground and faces: `rocky_terrain`, `_02`, `_03` (aerial), `rocks_ground_05` (scree), `lichen_rock`, `cliff_side`, `rock_face_03`,
  `rock_boulder_cracked` (Poly Haven); `Rock031`, `Rock033`, `Rock035` (black, "cave"), `Rock047`, `Rock048` (dark rough),
  `Rocks006` (sharp gravel) (ambientCG). Heightmaps `Terrain001` to `005` can serve as shape references. Plants: `fir_sapling`,
  `fir_sapling_medium` scaled down, `fern_02`, `moss_01`, `grass_medium_01`.
- **Purchases.** Caves: Modular Cave (Ancient Cave, Ruins, Cavern),
  [$79.99](https://marketplace.unity.com/packages/3d/environments/dungeons/modular-cave-ancient-cave-ancient-ruins-mayan-ruins-cavern-306634),
  built for HDRP, Unity 2021.3; Modular Caves (ProAssets), [$19.99](https://marketplace.unity.com/packages/3d/environments/dungeons/modular-caves-112763),
  2018, pipeline not stated. The volcanic look: NatureManufacture L.V.E 2023,
  [$70](https://marketplace.unity.com/packages/tools/terrain/l-v-e-2023-lava-volcano-environment-2023-253816), HDRP listed up to Unity
  6000.3, updated March 2026 (keywords: volcano, cave, river; contents UNVERIFIED). None of these was checked for scan quality.
- **Stealth.** Outside, 70 to 90 % of rays are clear beyond 30 m; cover comes from the terrain itself (ledges, boulder fields). Inside
  tubes there is no sight beyond the next bend. The ground's noise is high.
- **Cost [E].** Outside 7 to 9 ms (0.4 to 0.6 times the forest content: opaque rock, few plants), plus far terrain from the summit.
  Inside caves, 6 to 8 ms. Rock walls are solid occluders, the second place to try GPU occlusion culling.
- **Build. New systems:** caves (terrain holes, an HDRP asset setting, cave meshes, NavMesh links, lighting, reverb), standing rock
  faces (the CC0 sheets are open; closed cliff meshes must be bought or modelled), and the far view from the summit. Section 7.3 has
  the details.

### B6 Leeward cycad scrub on dry ridges

- **Role.** The dry side: rocky ridges with head-high evergreen scrub and cycads. Triceratops browsing, Gallimimus passing, outposts
  on the ridgelines. Warmer, clearer air than the windward side.
- **Night look.** Dense dark shrubs at 1 to 3 m, cycad and aloe rosettes as spiky silhouettes, pale granite boulders that catch the
  moonlight, thin haze. The palette is the warmest on the island (tan, grey-green).
- **Sound.** Wind in stiff leaves, insects (the loudest insect night), stones underfoot.
- **Movement.** Weaving between shrubs. They are visual cover at standing height and hard cover only at the boulders. Ground: sandy
  gravel 1.2, stones 1.3, granite 1.0.
- **Analogue and photos.** New Caledonia's [maquis minier](https://commons.wikimedia.org/wiki/Category:Maquis_minier) (shrubland with
  araucarias, a living Gondwanan relict), the Cape [fynbos](https://commons.wikimedia.org/wiki/Category:Fynbos), and South African
  cycads on rocky slopes ([Encephalartos](https://commons.wikimedia.org/wiki/Category:Encephalartos)). Poly Haven's own field
  photos are on the [Namaqualand project page](https://blog.polyhaven.com/namaqualand).
- **Dominant plants.** Evergreen shrubs, cycads, aloe-like rosettes, scattered araucaria on the crest.
- **CC0 kit: one coherent collection** (`namaqualand`), and much of it ships its own LODs, which the fir trees lacked [V, `assets-vegetation.md` 2.1]:
  - Shrubs: `searsia_burchellii` (1.9 to 3.2 m, LOD0 to LOD2), `searsia_lucida` (0.7 to 2.3 m), `othonna_cerarioides` (0.5 to 2.0 m,
    LOD0 to LOD3), `didelta_spinosa` (0.9 to 2.1 m, LOD0 to LOD2, translucency map), `wild_rooibos_bush`.
  - Cycad and aloe stand-ins: `quiver_tree_02` (1.47 m, the closest silhouette to a cycad, 82,074 triangles), `quiver_tree_01`.
  - Dead wood: `dead_quiver_trunk`, `dead_quiver_branch_01` and `_02`, `bark_debris_01`.
  - Rocks: `namaqualand_boulder_02` to `_06` (0.7 to 2.5 m wide), `namaqualand_boulders_01`, `namaqualand_rocks_01`,
    `namaqualand_stones_01`, `namaqualand_cliff_01` and `_02`.
  - Ground: `sandy_gravel_02`, `rock_face`, `tiger_rock`, `cliff_side` (all from the collection), plus `gravelly_sand`,
    `red_laterite_soil_stones` and `dry_ground_rocks`; ambientCG `Ground096A` to `C` (dry eroded), `Rocks012` to `021` (desert pebbles).
  - Leave out the flowers and succulents. They read as "desert in bloom" and say nothing at night.
- **Purchases (optional).** Real cycads (see B2). If `quiver_tree_02` passes a render check under the torch, the biome can launch with
  no purchase.
- **Stealth.** Target 20 to 35 % of rays clear beyond 30 m. Shrubs of 1.9 to 3.2 m hide a standing player, so this is the best open
  cover outside the forests. The shrub crowns are dense down to the ground, so a sight-proxy collider rule (a gameplay decision,
  spec 7.4) matters most here.
- **Cost [E].** 8 to 10 ms, 0.6 to 0.8 times the forest content. Shrubs are alpha-tested foliage, like young firs, but lower and at
  lower density. Triangle counts are high per plant (`searsia_burchellii_large` 361,558 at LOD0), so the thinner or the authored LODs
  are needed.
- **Build. Almost free.** The same pipeline from manifest to scatter, with one new kit and one terrain palette. Only the shrub classes
  are new for the planner.

### B7 Windward coast: cliffs, dunes, beach

- **Role.** The edge of the world and the way out: the shore where the player starts or where rescue comes. Compsognathus on the
  tide line. Exposure everywhere, and relief at the same time.
- **Night look.** The horizon, a moon path on the sea, surf as pale lines, Norfolk-Island-pine silhouettes on the cliffs, wind-bent
  coastal scrub. It is the brightest biome, because nothing blocks the sky.
- **Sound.** Surf (a footstep-masking zone, for example noise times 0.5 within 30 m of breaking waves [E]), wind, insects in the
  backdune. No gulls: they are a modern bird and would break the period feel.
- **Movement.** Dry sand 0.6 and wet sand 0.5 (the quietest ground on the island, but footprints could be a later mechanic), shore
  rock 1.2. Dune hollows give crouch cover. Cliffs channel the routes.
- **Analogue and photos.** [Nā Pali Coast](https://commons.wikimedia.org/wiki/Category:N%C4%81_Pali_Coast_State_Park),
  [Lord Howe Island](https://commons.wikimedia.org/wiki/Category:Lord_Howe_Island),
  [Norfolk Island pine](https://commons.wikimedia.org/wiki/Category:Araucaria_heterophylla),
  [sand dunes](https://commons.wikimedia.org/wiki/Category:Sand_dunes).
- **Dominant plants.** Araucaria heterophylla on headlands, low gnarled coastal trees, dune grass, cycads in the backdune.
- **CC0 kit: the `smugglers_cove` collection.** Cliffs `coastal_cliff_01`, `_02`, `_04` (40 to 92 m long, 0.46 to 1.54 M triangles at
  LOD0); shore pieces `coast_rocks_01`, `_02`, `_03`, `_05`, `coast_land_rocks_02` to `_04`, `coast_line_01` and `_02`,
  `sand_rocks_small_01` (0.54 to 1.29 M triangles each, FBX 40 to 98 MB). The heavy scans need the same vertex-clustering decimation
  the rocks already get (spec 5.6.2); whether they are closed surfaces is UNVERIFIED. Coastal scrub: `island_tree_01` to `_03` (2.6 to
  5 m, milkwood-like). Grass: `grass_bermuda_01`. A dock: `modular_wooden_pier`. Ground: `coast_sand_01` to `_05`,
  `coast_sand_rocks_02`, `coast_land_rocks_01`, `damp_beach_sand` and `_02` (new in August and September 2026), `low_tide_rocks`,
  `seaside_rock` (Poly Haven); `Ground092A` to `Ground095C` (dune and coastal sands, November 2025), `Ground056`, `Ground060`
  (shells) (ambientCG).
- **Purchases.** NatureManufacture
  [Coast & Dunes Environment - Dynamic Nature](https://marketplace.unity.com/packages/3d/vegetation/coast-dunes-environment-dynamic-nature-beach-sea-islands-dry-des-309456):
  $60, HDRP compatible with Unity 6000.3, version 1.1.1 of May 2026 [V]. Per its store snippet it is 100 % scanned with 350 prefabs
  and 178 textures; which plants it holds is UNVERIFIED. Palms, if wanted: Tropical Island Vegetation (BSP),
  [$9.50](https://marketplace.unity.com/packages/3d/vegetation/tropical-island-vegetation-built-in-shaders-urp-hdrp-209186), HDRP on
  6000.3, updated 2026-10-14; Tropical Forest Pack (Baldinoboy),
  [$24](https://marketplace.unity.com/packages/3d/environments/tropical-forest-pack-49391), HDRP on 6000.0. Contents of both are
  UNVERIFIED. The sea needs no purchase: HDRP ships a Water System.
- **Stealth.** 80 to 95 % of rays clear beyond 30 m on the beach, with cover only in the backdune and under the cliffs. The player
  should cross it, not linger.
- **Cost [E].** 7.5 to 9.5 ms for the land, plus the sea. The HDRP Water System's cost on the M4 Max is unmeasured. Its own
  performance page lists tessellation, simulation-band fades and resolutions as the knobs
  (`HDRP Documentation~/water-quality-and-performance-decisions.md`). Planning figure: 0.5 to 3 ms, UNVERIFIED. A simple custom
  sea shader is the fallback if it is too costly.
- **Build. New systems:** a sea surface and shoreline (visual only, plus a barrier that stops the player at the waterline), dune grass
  at scale, and far view to the horizon (impostors for headland trees).

### B8 Facility grounds and outposts

- **Role.** The research project's footprint: a main station by the coast, outposts with radios, fence lines across biomes. This is
  where documents and objectives live, raptors use doors, and Compsognathus pick through rubbish. Structures are another topic.
  This card covers only the vegetation and the ground.
- **Night look.** Pools of sodium or LED light from the few lamps still working (exposure!), cut grass gone wild, concrete
  cracked by roots, planted ornamental cycads and palms in beds, fences sagging into scrub.
- **Sound.** Generator hum, buzzing lamps, a flapping tarp, a door banging in the wind. These are the only mechanical sounds on
  the island, so they are easy to read.
- **Movement.** Hard ground (concrete 1.0, gravel 1.3, metal grating 1.8 [E]). Lit areas are no-go zones. Overgrown lawn at 0.5 to
  1 m hides nothing standing.
- **Analogue and photos.** Abandoned island bases: [Midway Atoll](https://commons.wikimedia.org/wiki/Category:Midway_Atoll),
  [Johnston Atoll](https://commons.wikimedia.org/wiki/Category:Johnston_Atoll),
  [abandoned buildings in Hawaii](https://commons.wikimedia.org/wiki/Category:Abandoned_buildings_in_Hawaii); overgrowth in
  [Pripyat](https://commons.wikimedia.org/wiki/Category:Pripyat).
- **Plants.** Escaped lawn and weeds, ornamental beds of cycads, palms and broadleaf houseplants (which explains the Poly Haven
  tropical houseplants), a planted specimen tree.
- **CC0 kit.** Lawn and weeds: `grass_medium_01` and `_02`, `weed_plant_02`, `nettle_plant`, `dandelion_01`, `celandine_01` (lawn
  weeds fit here and nowhere else). Planted beds: `periwinkle_plant`, `anthurium_botany_01`, `calathea_orbifolia_01`,
  `pachira_aquatica_01`, `potted_plant_01`, `_02`, `_04`. Specimen trees: `jacaranda_tree`, `quiver_tree_01`. Props:
  `street_lamp_01` and `_02`, `modular_electricity_poles`, `fire_hydrant`, `water_manhole_cover`, `trashbag` (Poly Haven). Ground:
  `gravel_road`, `worn_asphalt`, `road_damaged_2`, `cracked_concrete`, `concrete_moss`, `grass_concrete_pavement`, `leafy_grass`
  (Poly Haven); Asphalt (46), Concrete (61), `Fence001` to `006` (wire mesh with opacity: chain-link), MetalWalkway (14),
  `AsphaltDamageSet001` and `002` (ambientCG).
- **Purchases.** Buildings and interiors are outside this note. One cheap HDRP lab interior exists (Laboratory Multipurpose Pack,
  [$9.99](https://marketplace.unity.com/packages/3d/environments/laboratory-multipurpose-pack-hdrp-186787), low-poly, 2021), likely
  below the forest's realism.
- **Legal.** Use generic chain-link and concrete posts and a research-station look. No big timber gates, no park signage, no
  visitor-centre architecture.
- **Stealth.** 20 to 60 % of rays clear beyond 30 m, depending on buildings. Hard cover is plentiful. Light is the hazard.
- **Cost [E].** 8 to 10 ms for grounds and buildings (opaque walls are good occluders), plus each extra shadow-casting lamp in view.
  The torch alone costs 0.5 to 1.3 ms [M], so allow at most two shadowed lamps in view besides it. Others light without shadows.
- **Build.** Vegetation and ground on the same pipeline. Lawn at scale is the grass system (section 7.3). Structures belong to the
  facility topic.

---

## 5. Stealth across the biomes

| Biome | Sightline beyond 30 m [E target] | Hard cover | Visual cover | Ground noise [E] | Torch seen from [E] | Large theropod (2 m radius) moves |
|---|---|---|---|---|---|---|
| B1 fir forest | 8 % (measured, core points) | rocks 0.4 to 1.1 m, logs | thickets 6 to 9 m | 0.8 to 1.3 | 30 to 60 m (mist 30 m, trunks) | corridors only: young firs at 2.2 to 3.2 m spacing [M] |
| B2 ravine | under 5 % | banks, boulders | tree ferns, king fern | 0.7 to 1.6 | 20 to 40 m | no (floor 3 to 10 m wide, filled) |
| B3 fern savanna | 60 to 85 % standing, under 15 % crouched in fern | kopjes | tall fern 1.2 to 2 m | 0.8 to 1.3 | 200 m and more | freely (trees 18 to 30 m apart) |
| B4 burn scar | 30 to 50 % | root plates, logs | regrowth | 1.0 to 1.6 | 100 to 150 m | along 4 m gaps |
| B5 highlands | 70 to 90 % outside, 0 in tubes | ledges, boulders | little | 1.0 to 1.6 | on the skyline, km | outside yes; tubes no |
| B6 dry scrub | 20 to 35 % | boulders | shrubs 1.9 to 3.2 m | 1.0 to 1.3 | 60 to 100 m | between shrub clumps |
| B7 coast | 80 to 95 % | cliffs, dune hollows | coastal scrub | 0.5 to 1.2 | km | yes, on the beach |
| B8 facility | 20 to 60 % | walls, vehicles | weeds | 1.0 to 1.8 | lit areas | outside buildings |

How to check the sightline targets: run the D2 porous sightline model (`build/tools/d2/sight3.py`) on each biome's plan, with the
crown and trunk porosity of its own plants, and record the "beyond 15 m / 30 m" numbers as the forest did. The "torch seen from"
column is a design proposal for when the AI starts reading `PlayerFlashlight.IsOn`: about the line of sight, capped near three times
the mist's mean free path. Nothing reads the torch yet [M, N1 section 7].

---

## 6. Performance against the measured forest

Basis: the player floor is about 3.3 ms and the forest content about 7.4 to 8.5 ms at its worst view, torch and wind on [M].
Below, "content" is my estimate relative to the forest, from the cost drivers the records identify. Nothing here is measured.

| Biome | Content vs forest [E] | Worst view [E] | Main drivers | First lever |
|---|---|---|---|---|
| B1 | 1.0 | 10.7 to 11.8 ms (measured) | alpha-tested fir foliage, torch shadows | already tuned |
| B2 | 0.8 to 1.1 | 10 to 12.5 ms | tree-fern crowns (alpha-tested), density | 35 to 40 m culls; GPU occlusion with walls (measure) |
| B3 | 0.6 to 0.9 | 8.5 to 11 ms | wide view, far trees, fern carpet | biome fog volume; impostors for trees past 55 m |
| B4 | 0.5 to 0.7 | 7.5 to 9.5 ms | opaque snags, logs | none needed |
| B5 | 0.4 to 0.6 | 7 to 9 ms outside | far terrain, scan rocks | decimate scans; occlusion behind ridges |
| B6 | 0.6 to 0.8 | 8 to 10 ms | shrubs at 44,000 to 361,000 triangles each | authored LODs, thinner |
| B7 | 0.5 to 0.7 | 7.5 to 9.5 ms + sea | 0.5 to 1.5 M-triangle cliff scans, sea surface | decimation; sea quality knobs |
| B8 | 0.6 to 0.9 | 8 to 10 ms + lamps | lights with shadows, chain-link alpha | two shadowed lamps at most |

Margins that the dinosaurs need: the forest has 0.7 to 1.8 ms left at its worst view [M]. The other biomes should leave 2 ms or more
on paper, because herds (B3, B6) and several raptors (B8) cost animation and AI time that the forest has not measured.

Memory [E]: one biome kit at the forest's scale is about 1.3 GiB (930 MiB textures plus 360 to 380 MiB meshes [M]). At a border two
kits are resident, about 2.6 GiB. That is fine on the M4 Max. On PS5 (16 GB shared) it argues for 2K textures on everything not seen
at arm's length, and for kits that share ground layers.

---

## 7. What the pipeline builds almost free, and what needs new systems

### 7.1 Prerequisites for any biome beyond B1 (island-wide, not per biome)

| # | System | Why | Size [E] |
|---|---|---|---|
| P1 | Several terrain tiles and streaming (additive scenes per tile, or per 2 x 2 tiles) | the builder makes one 256 m tile and one scene; the forest's rate is 81,000 GameObjects a tile | L (weeks) |
| P2 | Data-driven biome kits | `ForestClass`, `ForestSurface` and `ForestPrefabIds` are frozen to the fir forest (`ForestContracts.cs`); every biome needs its own classes, surfaces and ids from data, not code | M |
| P3 | Terrain palettes per tile | HDRP allows 8 layers per tile; give each biome a core of 4 layers (section 7.2) so any tile on a border holds two biomes | S to M |
| P4 | Far view: impostors or billboards for trees beyond the 50 to 55 m culls, and a skyline | open biomes (B3, B5, B6, B7) are seen past the forest's mist; spec 1.2 excluded impostors | M to L |
| P5 | Drawing small props without one GameObject each | ferns, branches and grass are most of the 12,446 GameObjects a hectare; the GPU Resident Drawer only takes MeshRenderer GameObjects (HDRP `gpu-resident-drawer.md`); the wind and push system works per renderer slot (W1 11.2), so a new path must keep it | M to L |
| P6 | Biome-local fog and exposure | different mist per biome (HDRP Local Volumetric Fog, `local-volumetric-fog-volume-reference.md`) | S |
| P7 | Ambience and footstep noise by surface | no audio exists (spec 1.2); the surface map exists and gameplay can read it | M |

S: a few days of agent work; M: one to two weeks; L: several weeks. These are rough guesses from the forest's own pace: version 1
in 3 days (M0 to M4), density 1 day, night 1 day, wind 3 days, all with one coherent CC0 kit.

### 7.2 Terrain palettes (four core layers per biome, all CC0)

| Biome | Core 4 layers (Poly Haven unless marked) | Shared with |
|---|---|---|
| B1 | `forest_leaves_04`, `forest_ground_06`, `forest_leaves_02`, `rocky_trail` (spec 6.3: "a complete look by themselves") | B2, B4 |
| B2 | `forest_ground_06`, `mud_forest`, `river_small_rocks`, `rock_pitted_mossy` | B1 |
| B3 | `dry_decay_leaves`, `dirt`, `rocks_ground_02`, `sparse_grass` | B4, B6 |
| B4 | `burned_ground_01`, `wood_chips`, `forest_leaves_04`, `forest_ground_04` | B1, B3 |
| B5 | `rocky_terrain_02`, `rocks_ground_05`, `lichen_rock`, ambientCG `Rock035` | B6 |
| B6 | `sandy_gravel_02`, `gravelly_sand`, `tiger_rock`, `red_laterite_soil_stones` | B3, B5 |
| B7 | `coast_sand_02`, `damp_beach_sand`, `coast_land_rocks_01`, `coast_sand_rocks_02` | B6 |
| B8 | `gravel_road`, `worn_asphalt`, `cracked_concrete`, `leafy_grass` | any |

Check before committing: each pair must look right side by side under the night preset and the torch, and the 8-layer keyword on
script-made materials needs testing on every new palette, as in spec 6.3.

### 7.3 Per biome

| Biome | Class | Same code, new kit | New system |
|---|---|---|---|
| B1 | exists | pines and seedlings optional | none |
| B4 | **almost free** | snags (drop the twig sub-mesh), charred tint, fallen-tree rule, palette | none |
| B6 | **almost free** | `namaqualand` kit through manifest, import, LODs, prefabs, scatter | none (the sight-proxy rule is a gameplay decision) |
| B2 | same code, **bought kit** | tree ferns, cycads and horsetails through the baker and thinner, wind profiles per frond; dry creek bed | none if the bed stays dry; a stream strip shader if not |
| B3 | same code, bought kit, **plus P4** | tall fern patches, araucaria, ginkgo | far view (P4) |
| B5 | **new systems** | rocks, scree, krummholz | caves: terrain holes need "Terrain Hole" switched on in the HDRP asset or they vanish in builds (HDRP `terrain-lit-material.md` "Using the Paint Holes Tool"), a cave mesh kit, NavMesh links, cave lighting and reverb. Closed standing rock faces. Far view |
| B7 | **new systems** | cliffs and shore rocks (decimated), coastal scrub, sand palette | sea surface and shoreline (HDRP Water System or custom), a waterline barrier, dune grass at scale |
| B8 | vegetation same code; structures other topic | lawn, weeds, beds | lawn grass at scale; lamps with a shadow budget |

**Grass at scale (B7 dunes, B8 lawns, and B3 only if the owner wants real grass).** Two routes:

- **Unity terrain details**, "Instanced mesh" mode. Unity's manual says instanced details "work with all render pipelines, including
  HDRP" (`Documentation/en/Manual/terrain-Grass.html`). Spec section 0 rejected them for version 1 because three behaviours were
  UNVERIFIED. They also bypass the W1 push system.
- **Our own indirect-instanced grass** that samples the existing interaction field.

Both are new work. Budget arithmetic: today's grass patch is up to 2,500 triangles for 1.1 m² [M, spec 5.6.4]. Dense grass to 20 m
over a 70-degree view cone (about 245 m²) would be about 0.5 M triangles. With the heavier clumps (up to 6,422 triangles each in
`grass_medium_01`) it grows fast. Light tufts (`grass_bermuda_01`, 8 to 268 triangles) and a density falloff keep it small [E].

**Bought renderers** are an alternative to P4 and P5 and the grass system: Nature Renderer 6 Pro
([$70 today](https://marketplace.unity.com/packages/tools/terrain/nature-renderer-6-pro-285950), HDRP on 6000.3, version 6.1.0 of
2026-10-02; an earlier listing read $120) and GPU Instancer Pro
([$128](https://marketplace.unity.com/packages/tools/utilities/gpu-instancer-pro-290293), version 0.12.20 of 2026-10-07; HDRP listed
for 2022.3; a fallback page lists Unity 6000.0.23 and later). Both would draw outside the project's own wind Shader Graph and push
field, so adopting either means porting W1's wind into their shaders or losing it. UNVERIFIED whether that is possible.

---

## 8. Purchases: a shortlist

General rule: **buy meshes and textures, not shaders.** Import through the existing manifest and FBX pipeline. Rebuild every
material in `ForestMaterials` on the project's wind Shader Graph, so wind, bending and the night grade stay uniform. Ignore pack
shaders and demo scenes.

| Priority | What | Price (2026-10-21) | Why | Status |
|---|---|---|---|---|
| 1 | Test buy for B2 and B3: BotaniCraft3D Dicksonia ($8.25), Equisetis ($7.50), one Araucaria and one Bracken (prices not read) on [RenderHub](https://www.renderhub.com/botanicraft3d/dicksonia-tree-plant) | about $30 to $40 [E] | decides whether modelled (not scanned) plants pass beside the fir scans at arm's length under the torch, like E1 did | modelled, game-ready not claimed; Extended Use licence, redistribution terms in games UNVERIFIED |
| 2 | [Coast & Dunes Environment - Dynamic Nature](https://marketplace.unity.com/packages/3d/vegetation/coast-dunes-environment-dynamic-nature-beach-sea-islands-dry-des-309456) (NatureManufacture) | $60 | coast kit: scanned dune and shore assets, HDRP on Unity 6000.3, updated May 2026 | contents UNVERIFIED |
| 3 | Real cycads and tree ferns at quality | see below | the signature of B2, B6, B8 | no verified game-ready scan found |
| 4 | [L.V.E 2023 Lava & Volcano](https://marketplace.unity.com/packages/tools/terrain/l-v-e-2023-lava-volcano-environment-2023-253816) (NatureManufacture) | $70 | basalt highlands and caves | contents UNVERIFIED |
| 5 | [Modular Cave](https://marketplace.unity.com/packages/3d/environments/dungeons/modular-cave-ancient-cave-ancient-ruins-mayan-ruins-cavern-306634) | $79.99 | lava-tube interiors | HDRP, Unity 2021.3; realism unchecked |
| 6 | [Burned Wood](https://assetstore.unity.com/packages/slug/173899) or [Dead Tree Pack](https://marketplace.unity.com/packages/3d/vegetation/trees/dead-tree-pack-239578) | $24.99 / $26.99 | real charred trunks for B4 | optional: B4 works from CC0 |
| 7 | [Forest Environment - Dynamic Nature](https://marketplace.unity.com/packages/3d/vegetation/forest-environment-dynamic-nature-150668) (NatureManufacture) | $60 | more forest-floor plants and bushes for B1 and B2 | HDRP on 6000.3, updated March 2026; contents UNVERIFIED |

For quality cycads, tree ferns, ginkgo and araucaria, two sources may have them; neither could be checked:

- **SpeedTree Library.** Since May 2026 it is a subscription at $999 a year, and its assets "are no longer available for individual
  purchase" ([Unity support](https://support.unity.com/hc/en-us/articles/49699142648980-How-do-I-receive-my-SpeedTree-Library-purchase)).
  The modeler is $19 a month for Indie (revenue under $100k) ([unity.com/products/speedtree](https://unity.com/products/speedtree)).
  Old store pages show a Sago Palm (a cycad) with wind and LODs. Whether the current library holds tree ferns, ginkgo or araucaria,
  and whether models stay usable after the subscription ends, is UNVERIFIED. Ask licensing@speedtree.com before paying.
- **Fab** (Epic). Megascans have been paid since 2025, and the Fab Standard licence covers any engine, Unity included
  ([80.lv](https://80.lv/articles/megascans-no-longer-free-after-2024/)). Fab pages refused automated reads (HTTP 403), so its plant
  catalogue was not checked. Search it by hand for "tree fern", "cycad", "horsetail", "monkey puzzle" and "ginkgo", and filter for
  FBX with LODs (not Nanite-only).

---

## 9. Suggested order

1. **The owner reviews the biome list** and the decisions in section 10. No code yet.
2. **Test buy and arm's-length capture** for tree fern, horsetail, araucaria and tall fern (about $40). Put each through the
   existing import, thinner and wind pipeline. Capture it beside a young fir at 1.5 m and 6 m under the torch, and look at it. This
   is a go or no-go for B2 and B3, as E1 was for the forest.
3. **P2 and P3 first, proven on B4 (burn scar),** as a second area next to the forest in the same tile. B4 needs no purchase, reuses
   the kit, and shows every place the frozen contract assumed firs. Measure it with the harness (29 views, paired).
4. **B6 dry ridge** from `namaqualand`, as the first kit from a second collection, with its own palette.
5. **Decide P1 (tiling and streaming) and P5 (small props without GameObjects)** before building any area larger than about two tiles.
6. **B2 ravine** with the kit chosen in step 2. A good candidate for the demo, with the Dilophosaurus.
7. **P4 far view, then B3 savanna.** The biggest visual payoff and the riskiest cost.
8. **B5 and B7** with their new systems (caves, sea). B8 follows the facility topic.

For a 20 to 30 minute demo: B1 (exists), B2 (ravine), B4 (burn scar as the visible trail of something big) and one small B8 outpost
with the radio. All four avoid the sea, the caves and the far-view system.

---

## 10. Decisions for the owner

1. Open ground as **fern savanna** (period-correct, cheap ferns, needs a tall-fern purchase) or as **grass savanna** (familiar, needs
   the grass-at-scale system)?
2. Is it acceptable to buy **modelled (not scanned) plants** for the Mesozoic signature species if they pass the arm's-length test?
   No CC0 scan of them exists.
3. Budget ceiling for vegetation purchases: the shortlist is about $300 to $400 one-off. SpeedTree Library would add $999 a year.
4. Is the sea **seen** (a water surface, a new system) or kept behind cliffs and haze for the demo?
5. Should caves be in the game (terrain holes need an HDRP asset setting changed) or only shallow overhangs?
6. Is the T. rex's core gated by geography (a basin with three or four passes)?

---

## 11. Unverified and risks

- Contents of every Asset Store pack listed. Their description sections came back empty to an automated reader; only price,
  pipeline table and date were read.
- Whether modelled plants hold up beside the fir scans at arm's length. This is the main realism risk, as it was in E1.
- The cost of the HDRP Water System on Metal and the M4 Max.
- Whether Poly Haven's coastal cliffs and shore slabs are closed surfaces (the forest found `rock_face_01` and `_02` open, spec section 0).
- The "torch seen from" ranges and the ground-noise multipliers are design proposals, not measurements.
- Area shares and frame-time estimates per biome are reasoned from the forest's measured drivers. Each biome needs its own measured slice.
- The palaeo claims cited from Wikipedia and the 2021 paper are secondary sources. The Hell Creek "open ground was herbaceous
  angiosperms" claim comes from a game wiki.

---

## 12. Sources

Project records: `Logs/forest-work/spec.md`, `build/STATE.md`, `build/issues/D2-density.md`, `N1-night.md`, `W1-runtime.md`,
`research/assets-vegetation.md`, `research/assets-ground-rocks-sky.md`; `Assets/Game/Editor/Forest/ForestContracts.cs`,
`Assets/Game/Scripts/Player/PlayerController.cs`, `Assets/Game/Scripts/Dinosaurs/RaptorAI.cs`. Local Unity 6000.6 documentation:
`Documentation/en/Manual/terrain-Grass.html`, `terrain-PaintHoles.html`; HDRP `Documentation~/terrain-lit-material.md`,
`gpu-resident-drawer.md`, `water-quality-and-performance-decisions.md`, `local-volumetric-fog-volume-reference.md`.

Catalogues (queried 2026-10-21): [Poly Haven API](https://api.polyhaven.com/assets?t=models),
[ambientCG API](https://ambientcg.com/api/v2/full_json), [Poly Haven Namaqualand](https://blog.polyhaven.com/namaqualand),
[Poly Haven Verdant Trail](https://blog.polyhaven.com/verdant-trail/).

Palaeo and botany:
[Dilophosaurus](https://en.wikipedia.org/wiki/Dilophosaurus), [Compsognathus](https://en.wikipedia.org/wiki/Compsognathus),
[Paleobiota of the Morrison Formation](https://en.wikipedia.org/wiki/Paleobiota_of_the_Morrison_Formation),
[USGS Morrison synthesis](https://pubs.usgs.gov/publication/70027121),
[USGS Jurassic "savannah"](https://pubs.usgs.gov/publication/70027238),
[Prasad et al. 2005](https://repository.ias.ac.in/43622),
[Mesozoic ferns paper (2021)](https://bibliotekanauki.pl/articles/1835986.pdf),
[Saurian wiki: fern prairie](https://sauriangame.fandom.com/wiki/Fern_prairie) (weak),
[Carnarvon Gorge](https://en.wikipedia.org/wiki/Carnarvon_Gorge),
[Macrozamia moorei (conifers.org)](https://conifers.org/za/Macrozamia_moorei.php),
[University of Melbourne on tree ferns after fire](https://findanexpert.unimelb.edu.au/news/12942-tree-ferns-are-older-than-dinosaurs.-and-that's-not-even-the-most-interesting-thing-about-them);
Bomfleur et al. 2014, *Science* 343:1376 (royal fern genome stasis), cited from knowledge, not re-read.

Reference photos (Wikimedia Commons categories): every link in the biome cards of section 4. Each was checked to resolve (HTTP 200)
on 2026-10-21.

Stores: [Unity support: SpeedTree Library](https://support.unity.com/hc/en-us/articles/49699142648980-How-do-I-receive-my-SpeedTree-Library-purchase),
[SpeedTree plans](https://unity.com/products/speedtree), [Megascans on Fab](https://80.lv/articles/megascans-no-longer-free-after-2024/),
[NatureManufacture](https://naturemanufacture.com/), and every Asset Store, RenderHub, CGTrader, Gumroad and Fab link in sections 4 and 8.
