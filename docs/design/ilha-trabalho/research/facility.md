# The research facility, the island's places, and level design for stealth

Research note for the island plan. Written 2026-10-21. Topic: (1) what the research project physically has, and the role of each place in play; (2) naming and story frame; (3) level design for stealth horror; (4) landmarks at night.

Every number taken from the project is cited to its file. Every outside claim has a link. **UNVERIFIED** marks anything that is a game value, an estimate, or a claim I could not check against a primary source. Nothing here was run in Unity.

---

## 0. Summary

1. **The facility should look like an industrial science outpost built inside an old Cold War base on a cold, misty northern island, not like a park.** Its parts are concrete bunkers, Quonset huts, container modules, steel-mesh fences, pipelines and lattice masts. There are no visitors, no visitor centre, no tour vehicles, no gate with torches, and nothing tropical. The fir forest already built is the right biome for that frame (spec section 2: it "reads as the Pacific Northwest or Scandinavia, not as a tropical island").
2. **Twenty places are proposed (section 2.3).** Each has one main job (shelter, objective, danger or story) and a biome. They sit in four rings of rising threat: coast, lowland, paddocks, core. That lets an open world keep a horror rise: the player picks the order, and the map picks the intensity.
3. **The radio call should be the midpoint, not the ending.** Two works in the franchise already end that way. In *Trespasser* (1998) the heroine reaches a summit transmitter in a corrugated-steel shack and calls the Navy. In *The Lost World* (1997) the survivors reach the worker village's radio station ([Wikipedia](https://en.wikipedia.org/wiki/Trespasser_(video_game), [walkthrough](https://www.trescom.org/?p=3863), [film](https://en.wikipedia.org/wiki/The_Lost_World:_Jurassic_Park)). Proposal: the call gets an answer ("we land at the strip if it is lit"), and the last act is reaching and lighting the extraction site.
4. **Story frame, recommended: "Quarantine".** The island is *Fallow Island* (PT *Ilha Pousio*), the company is *Pellbrook Life Sciences*, the project is *ATAVUS*. An illness among the staff forced an evacuation 11 days before the game. The animals were left on automatic systems, and the fences failed 5 days ago. Two other frames are in section 3. Quick web searches found no game, film or company with these exact names; a trademark search is still owed.
5. **The forest's night fog sets the landmark rules.** Below Y 18 m the mean free path is 30 m: a target keeps 3.6 % of its contrast at 100 m and 0.13 % at 200 m (`ForestLightingPresets.cs` lines 125-147 and 219-221). So the island can only be **read from vantage points above the mist**, and inside the mist the player steers by **light points, sound and paths**. At 45 m above the fog base the effective mean free path is about 290 m; at 60 m it is about 1 km (section 5.1).
6. **Each district gets its own light signature, based on real lighting rules.** The summit mast is red at 30 flashes a minute (FAA L-864). The lighthouse is two white flashes every 10 s. The airstrip beacon alternates white and green. The power plant is orange sodium light on its steam plume. The labs have an amber rotating beacon and a siren. The player can find a district by the kind of light without a map.
7. **The main diegetic danger tool is a wildlife VHF telemetry receiver.** Real collars beep at a set rate and double it after hours without movement ([ADF&G moose study](https://www.adfg.alaska.gov/static/home/library/pdfs/wildlife/research_pdfs/alces/5197.pdf)). Only the original stock wears collars; animals born on the loose do not. So the tool never makes the player fully safe.
8. **Sightline and hiding rules are derived from the greybox numbers.** The raptor sees a standing player to 28 m and a crouched one to 16.8 m. It runs at 9 m/s against the player's 6 m/s sprint. Hiding fails if the raptor saw the player within the last 0.5 s (`RaptorAI.cs`). From these: in raptor zones, hard cover within 8 m and a hiding spot within 16 m of that cover, on every main route (section 4.6).
9. **Safe rooms follow one rule set:** no animal enters, they save the game, they hold a site map, and they are spaced 6 to 8 minutes of stealth travel apart (about 290 to 380 m at an assumed 0.8 m/s). The precedent is RE2's save rooms, which Mr. X does not enter, and Amnesia: The Bunker's safe room with its generator.
10. **The demo fits the forest already built.** The field station goes at the north outcrop (2, 71), the spot the spec calls "the natural place for a later objective". The demo ends with a climb up the station's tower and the first view of the island's lights through a local break in the mist (section 6).

Decisions the owner needs to make are in section 7.

---

## 1. What the existing build fixes (constraints for every rule below)

| Fact | Value | Source |
|---|---|---|
| Raptor sight | 28 m, 110 degree cone; a crouched player only within 0.6 x 28 = 16.8 m; noticed within 2.5 m in any direction | `Assets/Game/Scripts/Dinosaurs/RaptorAI.cs` lines 30-35 |
| Raptor detection time | 0.25 s near to 2.2 s at 28 m; x1.5 when the player crouches; awareness decays 0.4/s | same, lines 40-45 |
| Chase | Raptor 9 m/s; keeps tracking 1.5 s after losing sight; gives up after 3 s and searches a 10 m radius for 14 s | same, lines 54-67 |
| Hiding | A hidden player is invisible unless the raptor saw them get in during a chase within 0.5 s (`WitnessWindow`) | same, lines 70, 139-143, 179-180 |
| Player | Walk 3.2 m/s, crouch 1.6, sprint 6; eyes at 1.65 m standing, 0.95 m crouched | `PlayerController.cs` lines 17-25 |
| Noise radii | Crouch 2 m, walk 8 m, sprint 18 m, thrown object 24 m | `PlayerController.cs` lines 31-33; `Throwable.cs` line 12 |
| Night mist | Mean free path 30 m below Y 18 m; above it density falls as exp(-(h-18)/11.9 m); volumetric range 55 m | `Assets/Game/Editor/Forest/ForestLightingPresets.cs` lines 125-147, 219-221; `Logs/forest-work/build/issues/N1-night.md` line 154 |
| Moon | Light travels toward bearing 135 degrees, so the moon stands in the **north-west** (bearing 315), elevation 50 degrees | `ForestLightingPresets.cs` lines 35, 202-203 |
| Torch | 300 lm, range 35 m; a grey card reads at 20 to 25 m and fades at 30 m | `Logs/forest-work/build/STATE.md` lines 1206-1214 |
| Forest sightlines (model) | On the trail, 17 % of eye-level rays are clear beyond 30 m and 47 % beyond 15 m; at core points 8 % and 33 % | `STATE.md` line 1142; `issues/D2-density.md` line 144 |
| Standing cover | **None in the CC0 kit**: moss rocks 0.43 to 1.11 m, logs 0.1 to 0.5 m; thickets hide from the eye, not from the AI's linecast | `Logs/forest-work/spec.md` section 7.4 |
| Frame budget | Worst view 10.7 to 11.8 ms (paired estimate) against 12.5 ms; "Dinosaurs need room" | `STATE.md` lines 1641-1642, 1243 |
| Visual tells already built | A sapling a raptor walks into leans over and whips back; ferns part around bodies | `STATE.md` lines 1679-1683 |
| Hooks already placed | Trail, two game-trail loops (figure of eight for patrols), 2.5 m agent corridors, dino spawns at Hollow, BoulderField, NorthOutcrop | `spec.md` sections 3.3, 3.7 |

Three consequences:
- **Buildings must supply the hard cover the forest lacks.** Containers, walls, vehicles, generator sets and pipe racks block a linecast. Ferns and thickets do not, until the gameplay phase adds sight-proxy colliders or concealment rules (section 4.2).
- **Interiors are where frame time is cheap.** Solid walls are what occlusion culling needs. D1 measured that GPU occlusion culling culls nothing through alpha-tested foliage (`issues/D2-density.md` line 177). So lit facility areas should sit in clearings, not under dense canopy.
- **The fog is dense and stays dense.** Orientation must be designed for it, not around it (section 5).

---

## 2. The research project and the island's places

### 2.1 What makes it NOT Jurassic Park

**Different purpose.** No tourists, ever. The project exists for drug research and patents, not entertainment. One real-world anchor makes the purpose plausible: crocodilian blood is a studied source of antimicrobial peptides. A George Mason University team identified 45 candidate peptides in alligator plasma, in a five-year, $7.57 M study funded by the US Defense Threat Reduction Agency ([NatGeo 2008](https://www.nationalgeographic.com/animals/2008/04/alligator-blood-antibiotics), [GEN](https://genengnews.com/topics/drug-discovery/its-no-crock-that-alligator-peptides-act-as-potent-antibacterials)). The fiction pushes this to "the archosaur ancestors": the animals are **research stock**, numbered and collared, not exhibits.

**Different place.** A cold, wet, misty island in the northern Pacific (or North Atlantic), not tropical Costa Rica. It matches the kit already built, which is fir and fern, with no palms or tree ferns available as CC0 (`spec.md` section 0). A geothermal field is what makes cold-island dinosaurs plausible: heated barns, greenhouses for herbivore food plants, and warm ground the animals cling to. It also gives the island a power plant and a steam plume to use as a landmark.

**Different look.** It is a layered industrial site. A real precedent is Adak in the Aleutians. Its Cold War naval base closed in 1997, and it still has bunkers on its hillsides, a rotting Loran navigation station, whole neighbourhoods of empty base housing, piers, runways and large fuel storage ([NEH](https://www.neh.gov/humanities/2017/winter/statement/adak-island-far-out-in-the-bering-sea-may-be-where-the-wind-begins), [US Navy BRAC](https://www.bracpmo.navy.mil/BRAC-Bases/Other-West/Former-Naval-Air-Facility-Adak/), [SCMP](https://scmp.com/lifestyle/travel-leisure/article/3144954/grid-getaway-alaskas-remote-aleutian-islands-abandoned)). The fictional company leased such a base and added modern prefab labs, so the island has two layers:
- **1960s military:** concrete bunkers, Quonset huts, a radar dome, an old airstrip, utility tunnels.
- **2020s company:** container modules on stilts, polycarbonate greenhouses, welded-mesh fences on concrete posts, numbered buildings ("B-07"), safety yellow, stencilled codes.

A second precedent for "dangerous biology on an island" is the US Plum Island Animal Disease Center: 49 to 50+ buildings, about 400 staff, its own ferry vessels, marine bulkheads, fuel-oil facilities and BSL-3 containment ([GAO-08-306R](https://www.gao.gov/assets/a95284.html), [ASRC Federal](https://asrcfederal.com/?p=1285)). Breeding practice also has a precedent: the Galápagos tortoise centre rears hatchlings in screened corrals for about 2 years, then in "pre-adaptation" corrals to about 5 years ([expedition report](https://www.expeditions.com/expedition-stories/daily-expedition-reports/santa-cruz-island-2017-03-01)), and incubator temperature sets hatchling sex ([PBS](https://www.pbs.org/odyssey/class/20000718_question.html)). That gives real-sounding lab paperwork.

**What not to use** (I am not a lawyer; a legal check is owed before release):

| Category | Avoid | Why |
|---|---|---|
| Names | Jurassic Park/World, InGen, Isla Nublar, Isla Sorna, "Site B", Biosyn, Masrani, Hammond, "Mr. DNA", "lysine contingency", "Asset Containment Unit", "spared no expense", "clever girl", "life finds a way" | Universal's names, inventions and lines |
| Creature designs | The frilled, venom-spitting Dilophosaurus | A film invention; no evidence for a frill or venom ([NatGeo 2020](https://www.nationalgeographic.com/science/article/jurassic-park-got-almost-everything-wrong-about-iconic-dinosaur-dilophosaurus), [Sci Am](https://www.scientificamerican.com/article/the-real-dilophosaurus-would-have-eaten-the-jurassic-park-version-for-breakfast/)) |
| Look | Wooden gate with torches, visitor-centre rotunda with banner, tour vehicles, monorail, gyrosphere, a T. rex skeleton logo, a red, yellow and black badge, thatched safari-lodge roofs | The park's look and trade dress |
| Scenes not to restage | Water glass rippling to footsteps; T. rex through a paddock fence in the rain and a car pushed over a wall; raptors hunting children in a kitchen; a raptor turning a door handle; a fence re-energised while someone climbs it; a goat tethered as bait; a feeding crane lowering a cow; raptors in long grass; radioing from the worker village; the summit transmitter as the ending | Iconic expression from the films or *Trespasser*. The idea of a heavy animal, tall grass or a radio is free; the staged sequence is what to avoid |
| Other dinosaur games | *Dino Crisis*: island research facility, time-rift "Third Energy", special-forces heroine ([Wikipedia](https://en.wikipedia.org/wiki/Dino_Crisis_(video_game))) | The premise "island facility plus dinosaurs" is shared; keep the cause and the hero different |

### 2.2 Building kit: one vocabulary that code can generate

No art team means every building should come from a small set of parametric parts, generated by editor code the way the forest is. They use CC0 surface textures (Poly Haven and ambientCG have concrete, corrugated and rusted metal; exact assets **UNVERIFIED**, check when choosing). Interiors need bought or CC0 props.

| Part | Real dimensions | Use |
|---|---|---|
| ISO container module | 20 ft: 6.06 x 2.44 x 2.59 m; 40 ft: 12.19 m long (ISO 668 standard sizes) | Labs, offices, bunk rooms, stores; on stilts 0.6 to 0.9 m high, which leaves a crawlspace (a hiding spot for the player, a way in for Compies) |
| Quonset hut | Most common model 20 x 48 ft (6.1 x 14.6 m), semicircular steel ribs under corrugated sheet ([Wikipedia](https://en.wikipedia.org/wiki/Quonset_hut)) | Military layer: field station, workshops, hangar annex, barns |
| Concrete bunker | Generated boxes, 0.4 to 0.6 m walls, earth bermed on three sides | Safe rooms, the military command bunker, keeper shelters in paddocks |
| Steel-frame shed | Portal frame, 12 to 30 m span, roller doors 4 m | Feed mill, garage, turbine hall, hangar |
| Lattice mast and tower | Triangular lattice, 1.5 to 3 m face; 12 to 20 m observation towers, 60 to 90 m comms mast | Vantages and landmarks |
| Fence | Welded mesh 3 to 5 m on concrete posts at 3 m spacing, live wires on standoffs; vehicle gates as **double-gated airlocks** | Paddocks. The UK's new zoo standards require "double-gated entry systems" for dangerous animals ([gov.uk](https://www.gov.uk/government/news/animal-welfare-rules-in-british-zoos-set-for-major-overhaul), [standards PDF](https://assets.publishing.service.gov.uk/media/697393b8d345446f8ce71ea5/Standards_of_modern_zoo_practice.pdf)) |
| Pipeline | 0.6 to 1.2 m pipes on supports 1 to 2 m high | Geothermal lines across the land: paths, crouch cover, crawl-under gaps |

**Live fences as a sound cue.** Wildlife fence energisers pulse about once a second (45 to 60 pulses a minute) at 5,000 to 6,000 V and about 0.7 J for bears ([Texas A&M](https://agrilifeextension.tamu.edu/library/wildlife-nature-environment/deterring-bears-with-electrified-fences-a-beginners-guide), [Maine IF&W](https://www.maine.gov/IFW/docs/Electric-Fencing-for-Black-Bears.pdf), [USDA FS](https://www.fs.usda.gov/t-d/pubs/htmlpubs/htm99232321/index.html)). A live fence therefore **ticks** once a second. A silent fence means a dead fence, and the player can hear the difference. Restoring power makes fences live again and changes where animals can go. Avoid restaging the film's climbing scene (section 2.1).

**Company signage.** The company has its own plain wordmark and a fern fiddlehead (crozier) icon. Buildings carry codes, paddocks carry P-numbers, and animals carry IDs such as "DL-03". There is no dinosaur silhouette in the logo.

### 2.3 The places

Biome names are provisional; use the biome report's names when they exist. Rings: **C** = coast (threat 1), **L** = lowland (2), **P** = paddocks (3), **K** = core (4). Species are suggestions for the species report to confirm.

| # | Place | Ring / biome | Main role | Also | Threat (suggested) | Footprint |
|---|---|---|---|---|---|---|
| 1 | **Harbour and supply dock**: pier, cargo crane, harbour office, cold store, fish-feed store | C / rocky cove, wind-bent firs | **Shelter** (harbour office = safe room) | Objective: fuel, boat parts. Story: evacuation chaos, luggage left on the pier, the last manifest | Compies scavenging the cold store | 120 x 80 m |
| 2 | **Lighthouse** (automated, older than the company) | C / headland grass, cliff | **Landmark** and vantage (gallery about 20 to 30 m up) | Story: island history before the company | Exposed cliff path, nowhere to hide | 30 x 30 m |
| 3 | **Airstrip and hangar**: gravel strip, hangar, windsock, rotating beacon, crashed supply plane | C / coastal grassland | **Objective**: the extraction site, lit in the last act | Shelter: hangar office. Hiding: culverts under the strip | Gallimimus herd (stampede), open ground | Strip 600 to 900 m x 30 m (**UNVERIFIED**: a light STOL plane's real need) |
| 4 | **Fuel farm**: tanks, drum store, pump house | C / grassland edge | **Danger and tool**: a one-use explosion as the biggest distraction; burns as a landmark afterwards | Story: refuelling log | Raptors drawn to the blast | 60 x 40 m |
| 5 | **Field station**: 1 Quonset, 2 containers, generator, 18 m radio mast, 12 to 15 m watchtower, compound fence | L / fir forest (the existing tile) | **Shelter** (first safe room) and **demo objective** | Telemetry receiver, first animal cards, local radio | Compies, first raptor | 40 x 30 m compound |
| 6 | **Staff village**: 16 to 24 prefab houses, mess hall, laundry, rec room, infirmary | L / forest edge, clearings | **Story** (personal lives) and resupply (batteries) | Infirmary = safe room | Raptor pack uses the houses as a den | 150 x 120 m |
| 7 | **Farm and feed**: livestock barns (feed sheep and goats, now loose), meat processing, fodder silos, feed mill | L / meadows | **Danger** (smell, carcasses, scavengers) | Loose sheep: moving noise sources and living bait | Compy packs; raptors visit | 120 x 90 m |
| 8 | **Greenhouses**: polycarbonate, geothermally heated, food plants for herbivores | L-K / warm fern valley | **Story**: why the "Mesozoic" vegetation has spread over the island | Dense concealment inside; noisy broken panels | Dilophosaurus ambush in the overgrowth | 4 to 6 houses of 10 x 40 m |
| 9 | **Veterinary and quarantine clinic**: squeeze chute, transport crates, darting store, X-ray, necropsy room | L / forest edge | **Story** (bite marks and case notes reveal behaviour) | Hiding inside animal transport crates | Mixed | 40 x 25 m |
| 10 | **Old military station**: hillside bunkers, command bunker, Quonset ruins | L-P / ridge flank | **Shelter**: the central hub safe room (concrete) | Story: the island's past; map room | Low inside, high around | 200 x 150 m, scattered |
| 11 | **Herbivore paddocks**: 3 to 4 large fenced meadows, feeding stations, keeper shelters with viewing slits, double-gated vehicle airlocks | P / paddock meadows | **Danger and information** (herd behaviour, alarm calls) | Crossing puzzles, live and dead fences | Triceratops (territorial), Parasaurolophus (alarm calls) | Each 300 to 600 m across |
| 12 | **Observation towers**, one per paddock group | P / meadows | **Vantage and refuge** from large animals | A trap if a raptor waits below: one ladder, one way down | | 15 to 20 m tall |
| 13 | **Carnivore house**: indoor dens, shift doors, a keeper corridor behind mesh | P / forest edge | **Danger**: the raptor pack's original home, so they return | Hide-and-seek between mesh walls: you see them, they see you | Raptors | 50 x 30 m |
| 14 | **Labs and incubation**: genetics lab, walk-in incubation rooms (warm, humid), hatchling nursery, server room, quarantine airlocks, decontamination showers | K / warm fern valley | **Objective** (data, frequencies, the transmitter part) and **story** (the cause) | Amber beacon and siren still running = landmark | Raptors inside; Dilophosaurus outside | Main block 60 x 40 m, 2 floors, plus 3 annexes |
| 15 | **Geothermal power plant**: wellheads, separators, pipelines, cooling tower, turbine hall, control room | K / volcanic highland | **Objective**: restart, which powers the island | Control room = safe room once secured; steam vents = concealment and noise masking | T. rex territory (warm ground) | 200 x 150 m plus 1 to 2 km of pipelines |
| 16 | **Utility tunnels**: 2.5 x 2.5 m concrete ducts from the plant to the labs and the village; vent shafts | K-L / underground | **Route**: a "backstage" for the player that avoids the T. rex | Hiding in alcoves and side rooms | Compies through broken hatches; raptors at the hatches | 1 to 1.5 km total |
| 17 | **Summit comms station**: old radar dome, 60 to 90 m lattice mast, equipment shelter, backup generator | K / summit ridge | **Objective**: the radio call (midpoint) | Shelter = safe room; red beacon = the island's main landmark | Exposed approach; T. rex below | 80 x 60 m |
| 18 | **Outposts** (3 or 4): weather station, seismic hut, relay hut | All rings | **Side objectives**: battery caches, map boards, short logs, one hiding spot each | Vantage | Varies | 10 x 10 m |
| 19 | **Incinerator and carcass pit** | L-P / quarry | **Story and horror**: what the staff burned before leaving | Strongest smell cue; Compy swarm | Compies | 40 x 40 m |
| 20 | **Sauropod range**: open upland meadow with a concrete observation hide | P / high meadow | **Landmark (living)** and wonder | The hide is a safe vantage | Indirect (stampede, being stepped on) | 600 m+ across |

### 2.4 Layout in rings

```
  coast ring (C)      harbour --- fuel farm --- airstrip              lighthouse on the headland
       |                 |                         |
  lowland ring (L)    field station   staff village   farm/feed   vet   incinerator
       |                       \          |            /
  paddock ring (P)     paddocks + towers   military station (hub)   carnivore house   sauropod range
       |                               \        |       /
  core ring (K)              greenhouses -> labs <== tunnels ==> power plant ---- summit comms
```

Spacing (estimates):
- **Safe rooms** sit 290 to 380 m apart on main routes, which is 6 to 8 minutes at an effective stealth speed of 0.8 m/s. That speed includes stops, crouching, waiting and detours. It is **UNVERIFIED**: measure it in the greybox with timed runs before the island is scaled.
- **The full game.** Of 3 to 5 hours, about half is movement. At 0.8 m/s that is roughly 4 to 7 km of walked path, with backtracking cut by shortcuts. The island's size is the scale report's call; this note only fixes the spacings above.

### 2.5 The objective chain (open world, the owner's choice)

1. **Act 1, lowland.** Survive the first night. Reach the field station (the demo) and learn that its radio cannot reach the mainland. Learn there are three things to do.
2. **Act 2, any order; 2 of 3 open the summit.**
   - (a) Restart the power plant (core).
   - (b) Recover the transmitter's missing module and the emergency frequencies from the labs (core).
   - (c) Recover the field codes from the military station's map room (paddock ring).
   - Each one changes the world: power brings lights and live fences; the labs' siren stops (a landmark changes); the tunnels open.
3. **Act 2 end, midpoint.** Make the call from the summit. The answer: "a plane can land at dawn if the strip is lit", or a boat, if the owner prefers the harbour.
4. **Act 3.** Cross the island back down to the coast and light the strip: runway lights plus the beacon, with power or the fuel farm's generator. Survive until the extraction arrives. This ending differs from *Trespasser* (section 2.1) and uses the open coastal ground for a different kind of tension: no cover, a herd, the apex animal.

---

## 3. Naming and story frame: three options

All three share the facts in section 2.1 (purpose, cold island, military layer). They differ in **when** and **why** it went wrong, which sets how much of the facility still works, and therefore the lighting and pacing tools available.

| | **A. Quarantine (recommended)** | **B. The cull** | **C. The long silence** |
|---|---|---|---|
| Island | **Fallow Island** (PT *Ilha Pousio*: land left to rest) | **Morrow Island** (*morrow*, archaic "tomorrow") | **Kelder Island** (Dutch *kelder*, "cellar") |
| Company | **Pellbrook Life Sciences**: mid-size agricultural biotech with a pharma arm; public face is livestock vaccines and fisheries research | **Corvale Biosciences**: sells cloning techniques to conservation programmes | **Ostrand Biologics**: biotech arm of a mining and energy group that also ran the geothermal plant |
| Project | **ATAVUS** (Latin *atavus*, "ancestor") | **SECOND SPRING** | **HINDCAST** (a weather model run backwards: "predicting the past") |
| Cover story | "Fallow Island Agricultural Research Station" | "Morrow Island Seabird Reserve" | "Kelder geothermal and seismic research site" |
| What went wrong | 11 days before the game, a respiratory illness spread among the staff. The documents trace it to fungus in the incubation rooms' humidifiers, not to the animals. The authority ordered an emergency quarantine and the staff left by sea in 30 hours. The animals stayed on automatic feeders and live fences. On day 6 a turbine trip, with no one there to reset it, dropped the fences. The company stalls the inspection to hide ATAVUS | The board cancelled the programme after a keeper died. A contract team came to put the animals down and wipe the data. On the cull night the head keeper opened the paddocks to save "her" animals, and the team was scattered | An earthquake damaged the plant and the fence grid. The company went bankrupt in the lawsuits and the site was left 3 years ago. The animals are feral, and a second generation has never seen people |
| The player (pick one) | A fisher whose boat sheltered in the harbour in a storm; or a technician sent by Pellbrook to wipe the servers quietly (a moral turn) | The cull team's boat pilot or medic: no soldier, no gun beyond a flare pistol | A technician sent to service the seismic station that still sends data; the helicopter goes down |
| Why rescue is hard | The island is a declared quarantine zone. Boats keep out, so the call must reach someone who will come anyway | The company's own people are the only ones nearby, and they want no witnesses | No one is listening on the old frequencies |
| State of the facility | **Fresh and silent**: battery emergency lights still on but dying, documents days old, food rotting, doors sealed | **Live**: lights, alarms, open gates; the world falls apart over the game | **Dead and overgrown**: dark, vines, collapsed roofs |
| Pacing tool it gives | Dying battery lights act as a clock with no timer on screen; sealed quarantine doors gate progress | The strongest drama, with the island changing by the hour | Cheapest to build (no working lights), the strongest "nature has taken over" mood |
| Distance from the franchise | **Farthest.** Shared beat: fences fail after the power goes, but here it happens off-screen, from neglect, not sabotage or a storm | Medium-close: an insider opening the gates echoes the film's saboteur, and the cull team echoes *The Lost World*'s hunters | **Closest**: an abandoned site with feral, breeding animals is *The Lost World*'s Site B; a crash and a radio is *Trespasser* |
| Build cost | Medium (some working lights; light states per district) | High (many dynamic states) | Low |

**Recommendation: A.**
- It explains every place: the biosafety look, airlocks, decontamination showers, evacuation aftermath, the reason the power fails.
- It gives the documents a fresh, personal voice.
- It makes rescue hard without inventing a villain.
- It stays farthest from the three films' premises.
- Its build cost is in the middle.

From B, keep the head-keeper character as the breadcrumb person the player follows (section 4.8): in A, she is the one who refused to leave.

**Name checks.** On 2026-10-21 a web search for each exact name ("Fallow Island", "Pellbrook", "Project Atavus"/"Atavus", "Morrow Island", "Kelder Island", "Corvale Biosciences", "Project Hindcast", "Ostrand Biologics", "Project Second Spring") found no game, film or company using it. "Calder Island" was rejected: a small real island of that name seems to exist. A **trademark search (INPI Brazil, USPTO, EUIPO) is still owed** before any name is fixed. Avoid naming the island after a real indigenous place or saint.

---

## 4. Level design for stealth horror

### 4.1 Keeping horror pacing in an open world

The risk: in an open world the designer no longer controls the order of events, so the peaks of a horror film can't be scripted. Eight rules, with their sources:

1. **Threat rises by geography, not by script.** The four rings (2.4) make distance from the coast the intensity dial. The main chain moves inward, then the last act comes back out. Subnautica works the same way with depth. Its GDC 2019 talk also covers "using radio signals to add structure to the sandbox", which is close to our radio-driven objectives ([GDC Vault](https://gdcvault.com/play/1025745/The-Design-of-Subnautica)).
2. **A director manages tension; the animals stay honest.** *Alien: Isolation* splits a "macro" director AI from the alien's own AI. The director keeps a "menace gauge" that rises with line of sight and with closeness on the motion tracker. At its peak, the director sends the alien elsewhere or "backstage" into the vents. The director always knows where the player is, but the alien has to find out for itself ([Game Developer](https://www.gamedeveloper.com/design/the-perfect-organism-the-ai-of-alien-isolation), [revisited](https://www.gamedeveloper.com/design/revisiting-the-ai-of-alien-isolation)). Left 4 Dead's director runs a build-up / peak / relax cycle ([Booth, "The AI Systems of Left 4 Dead", 2009, PDF](https://steamcdn-a.akamaihd.net/apps/valve/2009/ai_systems_of_l4d_mike_booth.pdf); the phase names are from my memory of the paper, whose text could not be extracted here: **UNVERIFIED**).
   - **Proposal:** a menace value from sightings, from predators within 15 m, and from chases. After a peak (a chase, or awareness at 0.8 or more) comes **90 to 180 s of relax**, during which the director steers nearby predators' patrols away. It never spawns or teleports an animal in view. Target on average: no more than one chase per 8 to 10 minutes. All values **UNVERIFIED**; tune in play.
3. **Each species is introduced in a staged way, even in an open world.** The first time the player enters a species' zone, a trigger stages a **glimpse**: the player sees the animal from a vantage before it can see them, and a document nearby explains one behaviour. The arc is unknown, then seen, then understood, then mastered, then a twist (for example, the uncollared animals of rule 7).
4. **Safe rooms set the rhythm.** Reaching one ends a tension cycle. Spacing: 6 to 8 minutes of stealth travel on main routes (2.4, 4.5).
5. **The world escalates, and its changes stay.** Power, fires, opened fences, broken doors and a stopped siren all persist. Lit areas draw animals and expose the player. In *Amnesia: The Bunker* the "semi-open", hub-focused world and a stalking monster replaced linear play, and the monster adapts: block one way in and it finds a quicker route ([GamingBolt interview](https://gamingbolt.com/amnesia-the-bunker-will-avoid-linearity-and-evolve-differently-with-each-playthrough), [Checkpoint review](https://checkpointgaming.net/reviews/2023/07/amnesia-the-bunker-review-it-follows/)).
6. **Nights as acts (optional; owner's call).** The full game spans three nights. Dawn ends an act in a safe room. In frame A, the battery emergency lights die during night 2: a clock with no timer on screen.
7. **Keep an unknown.** Telemetry tracks only the collared original stock (4.7). Animals born on the island carry nothing, so the receiver never makes the player fully safe. The apex animal is understood only in the core.
8. **Shortcuts shorten backtracking.** Gates opened from the inside, tunnel hatches and cut fences stay open. A cut fence is also a way through for animals.

### 4.2 Kinds of hiding, and who fits where

| Kind | How it works in code | Status |
|---|---|---|
| **Hard cover** | Blocks the sight linecast. Standing cover is 1.8 m or more; crouch cover is 1.1 m or more (crouched eyes at 0.95 m) | The forest has only crouch cover (rocks, logs); buildings and props supply standing cover |
| **Concealment** | Hides from the eye, not from physics: ferns, tall grass, thickets, darkness, steam | **Needs a new AI rule**, because today a thicket's only colliders are trunk capsules (`spec.md` 7.4). Proposal: inside a concealment volume, detection time x3 and sight range capped at 8 m standing, 4 m crouched (**UNVERIFIED** values) |
| **Hiding spot** | `HidingSpot` interaction: the player is invisible unless witnessed within 0.5 s | Exists (greybox) |
| **Refuge** | A spot the large animals can't reach but the small ones can | Needs size classes (below) |
| **Safe room** | No AI enters; save point | New |

**Size classes** (the species report sets the real sizes):
- **S** (Compy-sized): through any gap 0.3 m or more (vents, crawlspaces, broken windows).
- **M** (raptor, Dilophosaurus): through openings about 1.2 m wide or more (double doors, roller doors, broken walls). Whether single 0.9 m doors admit raptors depends on the species report's body width.
- **L** (Triceratops, T. rex, sauropod): outdoors only. Breaks sheds and dead fences; stopped by concrete and live fences.

Every refuge says which classes it stops. A crawlspace under a stilted container stops M and L but not S. So Compies **flush** the player out: their chatter is a noise event that brings the raptors. That pairs the light threat with the heavy one systemically, which fits the coordinator's view of Compies as "drawn by noise".

### 4.3 Hiding places by biome

| Biome | Hard cover | Concealment | Hiding spots | Refuges | Noisy ground |
|---|---|---|---|---|---|
| Fir forest (built) | Boulder field peaks 1.8 to 2.0 m (few), root mounds, bank cuts | Young-fir thickets (about 30 % of the core), fern carpets (26 % cover) | Hollow logs, root caves under tall-fir root plates, the fern hollow (1.1 m deep) | A fallen-trunk "tunnel" (S only) | Dry branches (309 placed), gravel ruts |
| Coast and headland | Sea stacks, cliff overhangs, boat hulls on the shore, net stores | Coastal grass, sea spray at night (a fog bank) | Under the pier (walkway over rocks, no swimming), upturned boats | Lighthouse gallery (one way down) | Shingle (loud), dock planks |
| Coastal grassland and airstrip | Hangar, wrecked plane, fuel drums, windsock hut | Tall-grass bands **beside** the strip, never on it | Drainage culverts under the strip (0.9 to 1.2 m pipes) | Culverts (stop M and L) | Gravel strip (loud) |
| Paddock meadows | Feeding-station bunkers, hay-bale stacks, keeper shelters | Long grass where fences have lapsed | Keeper shelters with viewing slits | Observation towers (stop L; M waits below) | Fence tick masks steps within 3 m (proposal) |
| Warm fern valley | Greenhouse frames, boulders | The densest concealment on the island: escaped ferns and horsetails, steam | Broken greenhouse sheds, pipe gaps | Pipe crawl-unders (S only can follow) | Broken polycarbonate (very loud) |
| Volcanic highland | Lava blocks, pipelines, wellhead housings | **Steam vents**: concealment plus noise masking | Lava tubes (2 to 4 m across), separator housings | Narrow fissures | Scoria (loud) |
| Summit ridge | Radome base, rock tors | Almost none (wind-scoured) | Equipment shelter only | None: the most exposed place on purpose | Rock (quiet) |

### 4.4 Hiding places by building

| Building | Hiding spots | Refuges | Loops (circle the predator) | Special rule |
|---|---|---|---|---|
| Field station | Locker, under the bunk, Quonset crawlspace | Watchtower | Around the Quonset (about 45 m perimeter) | Generator start emits a large noise (proposal: 40 m) |
| Staff houses | Wardrobes, under beds, bathrooms | Crawlspaces under the stilted units (S only follows) | Through houses with front and back doors | Many small rooms: raptors search room by room |
| Mess hall | Store room, under serving counters | — | Around the counter island | **Do not stage a kitchen chase** (2.1) |
| Labs | Lab-bench cupboards, fume-hood cabinets, server racks | Ventilation ducts (S only) | Ring corridor around the central core (40 to 60 m) | Quarantine airlocks: one door opens at a time, a slow, tense transit. Decontamination showers make noise when they start |
| Incubation rooms | Between incubator rows | — | Row-to-row loops | Warm and humid: torch beam scatters (local fog) |
| Carnivore house | Keeper corridor behind mesh | The keeper corridor itself, if its doors are shut | Corridor plus den loop | The mesh blocks bodies, not sight: you see them, they see you |
| Vet clinic | Animal transport crates, X-ray booth | — | Treatment-room loop | Necropsy room = a story tableau |
| Power plant | Pipe galleries, under turbine-hall catwalks | Control room (safe room) | Catwalk over the turbine hall | **Loud zone**: within 15 m of a running turbine, player footsteps x0.5 and animal hearing x0.5. Both sides hear less, which is fair |
| Tunnels | Alcoves every 25 to 30 m, side rooms | Vent shafts (S only) | Parallel ducts joined every 100 to 150 m | Footsteps echo: noise x1.25 on concrete (proposal) |
| Comms station | Cable vault | Equipment shelter (safe room) | None | The final approach has no cover: timing only |

**Safe-room rule taken from real zoos.** Licence conditions for dangerous-animal houses ask keepers to be able to view the area before entering, by panel or CCTV. The search summary attributed this to a council licence-condition document I could not open (**UNVERIFIED**; the UK double-gating requirement is cited in 2.2). So every double door in the facility has a viewing panel, and the player uses the keepers' own system to look before going in.

### 4.5 Safe rooms

Rules:
1. **No animal enters.** RE2's save rooms are rooms Mr. X does not enter (player reports, [Steam](https://steamcommunity.com/app/883710/discussions/0/2155476542891775832); his music and footsteps grow louder as he approaches, [Wikipedia](https://en.wikipedia.org/wiki/Mr._X_(Resident_Evil))). The AI's NavMesh simply has no path in. A shut door is shown shut and stays shut.
2. **Save, site map, resupply.** Each safe room has a "you are here" board and a battery cache. Amnesia: The Bunker puts its only save point beside the generator that powers the bunker's lights, and fuel management is the loop ([Gaming Trend](https://gamingtrend.com/reviews/amnesia-the-bunker-review-even-if-you-think-youre-safe-youre-not), [Wikipedia](https://en.wikipedia.org/wiki/Amnesia:_The_Bunker)).
3. **Unlocked, then kept.** A safe room becomes safe only once the player has secured it: shut the airlock, start its generator. It stays safe after that.
4. **The outside can still be heard**, quietly. Safety is not silence.
5. **Spacing**: 6 to 8 minutes of stealth travel on main routes. The demo has 2; the full game has 8 to 10.

List: harbour office (C), field station (L), village infirmary (L), military command bunker (central hub, P), power-plant control room (K, after the restart), lab quarantine office (K), summit equipment shelter (K), hangar office (C, the last act).

### 4.6 Sightline rules (starting values; re-derive when the AI changes)

Derivation: from the raptor's 9 m/s against the player's 6 m/s sprint, the gap closes at 3 m/s. A chase starting at gap *d* ends at attack range 1.8 m after (d - 1.8)/3 s, in which the player sprints 2(d - 1.8) m:
- From 10 m: 2.7 s and 16 m.
- From 16.8 m (a crouched player seen at the edge): 5.0 s and 30 m.
- From 28 m: 8.7 s and 52 m.

Hiding works only if the player gets in more than 0.5 s after the raptor last saw them.

| # | Rule | Number |
|---|---|---|
| S1 | In raptor zones, from any point of a main route, **hard cover** that breaks line of sight | within 8 m |
| S2 | From that cover, a **hiding spot** | within 16 m (the player can cover 8 + 8 m in the 2.7 s of a close chase) |
| S3 | **Observe before committing**: every objective's guarded area can be watched from at least one vantage beyond raptor sight | 30 to 60 m away (raptor sight 28 m) |
| S4 | **No open run** in a predator zone without cover or concealment on at least one side | 25 m or less; longer open ground (airstrip, meadows) is designed as a deliberate crossing with a timing window |
| S5 | **Interiors**: no straight corridor without a turn, door or alcove; every room has two exits except safe rooms and signposted traps | 12 m or less |
| S6 | **Loops** around a central block, so the player can circle a searching predator | 25 to 60 m perimeter, one per building of 300 m² or more |
| S7 | **Peek first**: windows, slits or panels into every room on a main route | 1 per room |
| S8 | **Vertical**: a tower or roof gives sight over the 2 to 4 m vegetation, has one way down, and keeps the player out of L reach | 12 to 20 m |
| S9 | **Light pools** (working floodlights): standing and crouched sight both at the full 28 m, detection x2 inside the pool | proposal, **UNVERIFIED** |
| S10 | **Torch** (section 4.7) | proposal |

### 4.7 Signalling danger

The rule: **hear it before you see it, see it before it sees you, and pay for every look.** Cues by distance band:

| Band | Cue | Real basis / precedent | Cost to build |
|---|---|---|---|
| over 200 m | Herbivore alarm calls (Parasaurolophus); stampede rumble; apex roar; the labs' siren; a large flock lifting off the canopy | About 70 vertebrate species are shown to eavesdrop on other species' alarm calls ([Magrath et al. 2015, *Biol. Rev.* 90:560-586](https://ro.uow.edu.au/smhpapers/2748)). A CT-based model of the Parasaurolophus crest gives a "resonating low-frequency rumbling sound that can change in pitch" ([Sandia 1997](https://www.sandia.gov/labnews/1997/12/19/dinosaur-story/)) | Audio events at herd or AI positions |
| 50 to 200 m | Raptor pack calls; Compy chatter; birds flushing (wingbeats, alarm calls, a small particle burst); the telemetry receiver starts to beep | Herd calls also give the player away: a startled herd is a noise event at the herd (proposal: radius 30 m) | Audio plus a particle system |
| 15 to 50 m | **Silence bubble**: insects and frogs fall quiet around a large predator; branches snap; saplings lean and whip back (built, W1); **eyeshine in the torch beam**; the telemetry beep strengthens | The silence is a game convention with weak support: treefrogs cut their calling after hearing a distress call, but the study could not separate fear from masking ([Ethology 2017 abstract](https://bio.acousti.ca/node/52564)). Spotlight surveys find crocodilians by eyeshine, at 120 m or more in one report ([UF/IFAS](https://edis.ifas.ufl.edu/publication/UW198), [Rufford report](https://media.rufford.org/media/project_reports/17868-B%20Progress%20Report%20I.pdf)). Whether dinosaurs had a tapetum is unknown: an artistic licence | Ambient emitters that check predator distance (cheap); an eyeshine sprite or emissive on the eyes, visible only in the torch cone |
| under 15 m | Footfalls, breathing, claws on concrete or metal | — | Animation-driven audio |

**The telemetry receiver** (Alien: Isolation's motion tracker, made real):
- Real VHF collars pulse at a set rate. One collar ran 40 pulses a minute at rest and 60 active, then switched to 80 (a mortality signal) after 2.5 h without movement; others double or triple after 1 to 4 h ([ADF&G moose study](https://www.adfg.alaska.gov/static/home/library/pdfs/wildlife/research_pdfs/alces/5197.pdf), [ADF&G caribou](https://www.adfg.alaska.gov/static/home/library/pdfs/wildlife/federal_aid/ipr_akw_29_p10.0_mulchatna_caribou_calf_survival_and_mortality_sfy20.pdf)). Handheld units are a receiver plus a 62 to 82 cm Yagi antenna, with signal shown on LEDs and a loudspeaker or earphones ([Perdix](https://perdixwildlifesupplies.com/products/perdix-vhf-yagi-antenna)).
- **In play:** tune to a frequency printed on an animal's record card (a document with a payload). The beep's loudness and rate tell direction and distance. A fast "mortality" beep means a dead animal, **or a collar torn off**.
- **Game ranges** (**UNVERIFIED**): 150 m open, 60 m through forest, 20 m through concrete.
- **The speaker beeps** (proposal: a 3 m noise radius) unless switched to earphone mode, which hides the visual LED.

**The torch gives the player away** (proposal for the AI and species work; the current `RaptorAI` has no light model):
- **T1.** The lamp seen face-on (the player inside the animal's cone, the beam within 30 degrees of the animal) is visible to 60 m at night, with detection x2.
- **T2.** A lit spot on a surface inside an animal's cone within 30 m is a stimulus: the animal walks to the **spot**, not the player. The torch doubles as a lure.
- **T3.** Torch off at night: a standing player is seen to 0.5 x 28 = 14 m, a crouched one to 8.4 m. Without this, the night means nothing to the AI.
- **The trade:** eyeshine shows at up to about 50 m, but a lamp shown face-on is seen at 60 m. **By the time you see eyes facing you, they have seen your lamp.** Also built: switching the torch off leaves the frame dark for 2 to 3 s while the exposure adapts (`STATE.md` line 1245). That is a real cost for flicking it on and off.

### 4.8 Telling the story without cutscenes

Precedents:
- **Smith and Worch**, "What Happened Here? Environmental Storytelling" (GDC 2010): build spaces that let the player infer past events, and use systems to show the environment's reaction ([GDC Vault](https://gdcvault.com/play/1012647/What-Happened-Here-Environmental)).
- **Jenkins' four forms of spatial storytelling**: evoked, enacted, embedded, emergent ([MIT text](https://web.mit.edu/~21fms/People/henry3/games&narrative.html)).
- **BioShock's audio diaries** suit background listening: they tie the player to no place and no screen, but can collide with other dialogue ([Destructoid](https://destructoid.com/?p=332338)).
- **Gone Home** (a story followed through a missing person's traces) and **Firewatch** (one voice on the radio) are the obvious shapes. They are named from knowledge of the games; I did not re-check them for this note.

Rules:
1. **Every document carries a payload or sits next to one.** 30 to 50 % carry direct play information: a code, a route, a collar frequency, a species behaviour, the location of a hiding spot or a safe room. The rest say who and why.
2. **Short**: 120 words or less on paper, 40 s or less of audio.
3. **Where it happened**: the room shows the aftermath, and the document gives the cause.
4. **Types** (cheap first):
   - **Paper:** animal record cards (ID, species, collar frequency, behaviour notes), feeding whiteboards (which show paddock assignments), incident reports, quarantine orders, maintenance tickets (the power plant's restart steps), the evacuation manifest (who left, and who did not), personal notes.
   - **Screens and recordings:** printed CCTV stills, terminal emails (only once power is back, so power unlocks story), radio logs.
5. **One thread to follow**: the head keeper who refused to evacuate. Her trail leads from the field station to the summit, giving both motive and direction.
6. **Audio never pauses the world.** It plays from the device (option: a small noise radius, the owner's call).
7. **Key beats are seen from vantages, not cut to.** For example, the apex animal crossing the strip below a paddock tower.
8. **Write in Portuguese and English from day one.** Text is cheap to translate; voice is not.

Counts: the demo has 8 to 12 items (2 or 3 audio). The full game has 70 to 100 items, about one per 2 to 3 minutes of play, with 10 to 15 audio logs (voice is a paid cost) and optionally one recurring radio voice.

---

## 5. Landmark plan at night

### 5.1 What the mist allows (computed from the forest's night preset; not rendered)

Transmission over distance *d* in the floor layer (below Y 18 m, mean free path 30 m):

| Distance | 25 m | 50 m | 100 m | 150 m | 200 m | 300 m |
|---|---|---|---|---|---|---|
| Contrast left, exp(-d/30) | 43 % | 19 % | 3.6 % | 0.7 % | 0.13 % | 0.005 % |

Above the base, density falls as exp(-(h-18)/11.9). For a horizontal view at height *h*:

| Eye height Y | 30 m | 45 m | 60 m | 80 m |
|---|---|---|---|---|
| Effective mean free path | 82 m | about 290 m | about 1,030 m | about 5.4 km |
| Contrast left at 300 m | 2.6 % | 36 % | 75 % | 95 % |

What this means:
- **Inside the mist, nothing unlit can be seen beyond about 60 m.** A distant dark mountain is gone even when it rises far above the fog: a ray from the valley floor to a peak 1.5 km away climbs out of the layer and still keeps only about 2 % contrast (rough estimate).
- **Only bright points reach far inside the mist.** An emissive light 1,000 times brighter than the moonlit scene keeps about 1.3 times scene brightness at 200 m, and is gone by 300 m (estimate, **UNVERIFIED**).
- **The HDRP Lens Flare (SRP) component** can carry a light farther. Its "Environment Occlusion" option decides whether fog, clouds and water attenuate the flare. It also has distance attenuation curves and geometric occlusion (local docs: `.../high-definition/Documentation~/shared/lens-flare/lens-flare-srp-reference.md`, `choose-a-lens-flare-type.md`). With environment occlusion off and a distance curve that fades at a chosen range, a beacon reads as a pulse through gaps in the canopy. Cost and occlusion through alpha-tested foliage on Metal are **UNVERIFIED**.
- **From above the mist** (eye at about 45 m or more above the local fog base), the island opens up. **That is the design**: the island can be read only from vantage points, and climbing to one is a deliberate, exposed act (BotW's towers, below).

**Recommendation for the biome and lighting reports.** Make the fog follow the terrain, not one global height:
- A thinner global fog based near sea level.
- Local Volumetric Fog volumes that add density in valleys and forest, at about 30 m mean free path.
- Open coast and grassland at 80 to 150 m.
- Ridges and summits clear.

HDRP's "Max Fog Distance" also sets fog on the sky and background (`fog-volume-override-reference.md` line 19), which matters for the far silhouettes. A probe is owed (5.6).

### 5.2 The landmarks

| # | Landmark | Light / sound signature | Real basis | Seen from | Changes with play |
|---|---|---|---|---|---|
| L1 | **Summit comms mast** (60 to 90 m) plus the white radar dome | **Red, 30 flashes a minute**, top and middle | FAA L-864 medium-intensity red beacon, 30 fpm under AC 70/7460-1M ([FAA AC 150/5345-43H](https://www.faa.gov/documentLibrary/media/Advisory_Circular/150-5345-43GH.pdf), [Flight Light](https://flightlight.com/what-is-an-faa-l-864-obstruction-light/)); solar obstruction lights exist ([Avlite](https://www.avlite.com/wp-content/uploads/2017/08/Solar_Powered_Spec-Sheet_Systems_Reference_Guide.pdf)) | Every vantage; a faint pulse through canopy gaps inside the mist | Dim solar beacon at the start; the full set lights when power returns, so progress is visible from anywhere |
| L2 | **Lighthouse** | **White, two flashes every 10 s, Fl(2) 10s**; a beam sweeping the fog | Light characteristics identify each light ([Wikipedia](https://en.wikipedia.org/wiki/Light_characteristic)) | The whole coast ring; the beam's sweep across low fog | Constant: "the sea is where the lighthouse is" |
| L3 | **Power-plant steam plume** | **Orange sodium floods** lighting the plume from below; red light on the stack; steady hum, periodic vent roar | — | Vantages; the hum is audible through the mist (game value 300 m or more) | Plume gets bigger and brighter after the restart |
| L4 | **Labs** | **Amber rotating beacon** plus cold white emergency windows; siren cycle (proposal: 20 s every 3 min) | — | Core and paddock vantages; the siren island-wide | Siren draws animals; switching it off removes a landmark (a trade) |
| L5 | **Airstrip** | **Rotating beacon alternating white and green, 24 to 30 flashes a minute**; runway edges white, taxiway edges blue | FAA civil land-airport beacon ([Wikipedia](https://en.wikipedia.org/wiki/Aerodrome_beacon)); runway and taxiway colours ([Pilot Institute](https://pilotinstitute.com/runway-lights-explained-colors-spacing-types/)) | Coast ring | Dark until the last act: lighting it is the goal |
| L6 | **Harbour** | Red and green entrance lights; a **foghorn** (proposal: a 2 s blast every 30 s) | — | Coast ring; the foghorn far inland (sound landmark) | Constant |
| L7 | **Fires** (the wreck the player arrives from; the fuel farm if the player blows it) | Flickering orange glow on the underside of the mist | — | Wide: the skyglow lights the fog layer itself | Temporary; the player can create one |
| L8 | **The moon** | Fixed in the north-west, elevation 50 degrees (built) | — | Disc from vantages; a brighter patch of mist below | **Rule: never move the moon within a night.** It is a compass for players who notice |
| L9 | **Sauropods** | Low calls; silhouettes over the high meadow | — | High meadow and its vantages | Living, moving: wonder, not orientation |
| L10 | **Staff village windows** | A few warm lamps in a dark village | — | Lowland vantages | A lure: someone alive? (a story beat) |

**Colour code by district**: red = summit, white flashes = coast, orange = power, amber = labs, white and green = airstrip, warm windows = village. The player learns it without being told, as Disney used tall "weenies" to pull guests toward a place from anywhere ([Scott Rogers, GDC 2009 notes](https://boingboing.net/2009/03/27/gamedesign-lessons-f.html), [Clarke-Willson](https://www.above-the-garage.com/wienies/)).

### 5.3 Inside the mist: paths, edges, nodes, sound

Lynch's five elements of a mental map are paths, edges, districts, nodes and landmarks ([*The Image of the City*, 1960](https://en.wikipedia.org/wiki/The_Image_of_the_City)). In the mist the near elements carry the load:

- **Paths**: service roads and the power line. Poles every 50 m, from the plant to the core, the village and the field station: follow the wires. Game trails are the animals' paths (built).
- **Retroreflective road posts** every 50 m on service roads. The torch makes them flare back at up to about 40 to 60 m, read through the mist twice (out and back). This is a shader trick (emissive scaled by the angle between the torch and the post) and **UNVERIFIED**. The cost: using it means shining the torch.
- **Edges**: fences (live ones tick), cliffs, the coast (surf), pipelines.
- **Nodes**: junctions with company signposts ("B-09 VET 400 m") on retroreflective plates; safe rooms.
- **Sound landmarks**: foghorn (coast), turbine hum (plant), siren (labs), surf (coast), herd calls (meadows), generator (field station once started). Sound reaches farther than sight in fog, so it is the main long-range compass in the forest.

### 5.4 Vantages

- **8 to 12 vantage points**: paddock towers, the field-station tower, the lighthouse gallery, cooling-tower stairs, the radome platform, ridges, roofs.
- **From each, at least 3 of L1 to L6 are visible in different directions**, enough to triangulate.
- **Each district has one vantage.**
- **Vantages are exposed.** The first one teaches "torch off up here" (T1).

The model is BotW's towers: points visible from far that reveal the land around them. BotW's "triangle rule" uses peaks to hide what lies behind and to offer a choice between climbing over and going around (CEDEC 2017 talk via [GIGAZINE](https://gigazine.net/gsc_news/en/20171004-breath-of-the-wild-design-secret); [GMTK](https://gmtk.substack.com/p/how-nintendo-solved-zeldas-open-world)). For us, ridges and spoil heaps hide the next district until the player tops them.

### 5.5 Map policy

- **No full-screen map.**
- **"You are here" boards** in every safe room and at main junctions (diegetic, in company style).
- **Optional:** a folded paper site map the player carries, which marks only what the player has visited (the owner's call). Firewatch's held paper map and compass is the precedent; that is from knowledge of the game, not re-checked.

### 5.6 Cost and the probes owed

- **Far landmarks cost almost nothing.** They are emissive plus a lens flare, animated by script, with no shadow-casting light. Silhouettes beyond the playable tiles are low-poly meshes.
- **Near light pools are real HDRP lights without shadows.** The forest's worst view has only 1.0 to 1.2 ms of margin (`STATE.md` line 1243), and dinosaurs need room. So lit areas belong in clearings and interiors, and every lit district gets measured with the existing harness (`tools/n1/paired.sh`).
- **Probe (owed before landmarks are fixed).** In a copy of the forest night scene:
  - emissive cards at 50, 100, 200 and 300 m at three brightnesses;
  - one Lens Flare (SRP) with environment occlusion on and off;
  - one red 30 fpm blinker behind the canopy;
  - a camera at Y 0, 30, 45 and 60 m.
  - Measure: what reads, and the frame cost.

---

## 6. Demo slice (20 to 30 minutes) on the forest already built

| Minutes | Beat | Built / to build |
|---|---|---|
| 0-3 | Arrival at the south bank (spawn (0, -62), facing north). Behind the player, the burning wreck that brought them (L7) | Fire light and audio behind the bank |
| 3-8 | Trail north. First Compy: alone, curious, drawn by noise, harmless. Teaches "noise matters" | Compy AI (new) |
| 8-12 | Deadfall (12, -14) and fern hollow (34, -24): a dead staffer's **telemetry receiver** and an animal record card with a frequency | Receiver item; 3 documents |
| 12-18 | The raptor patrols the figure-eight game trails (built). The receiver beeps, birds flush, saplings lean (built) | Bird audio and particles |
| 18-24 | **Field station** at the north outcrop (2, 71). Starting the generator is loud (proposal 40 m), and it brings the raptor: hide in the Quonset crawlspace or a locker | Quonset, 2 containers, compound fence, generator, safe room |
| 24-28 | The radio gives static and a broken reply: "the main mast". Climb the 12 to 15 m tower; a local volume lowers the mist; **first view of the island**: red mast (L1), lighthouse (L2), plume (L3). That view promises the full game | Tower; far silhouettes and lights outside the tile; local fog volume |

Pieces to build for the demo: 1 Quonset (6.1 x 14.6 m), 2 container modules, 1 tower, 1 mast of 18 m, about 140 m of compound fence, 8 to 12 documents, the receiver, Compy AI, birds, far landmarks, 2 safe rooms (the wreck's shelter or the start, and the field station).

---

## 7. Decisions for the owner

1. **Story frame**: A (Quarantine, recommended), B (The cull) or C (The long silence)?
2. **Who is the player?** Fisher, contractor sent to wipe the servers, pilot, or medic?
3. **The ending**: radio call as the ending (close to *Trespasser*), or as the midpoint with the extraction at the airstrip or harbour (recommended)?
4. **Fog**: accept the principle "the island can be read only from vantage points above the mist, with per-district fog"?
5. **Telemetry receiver** as the main danger tool, with uncollared animals as the unknown?
6. **Torch rules T1 to T3**: is the torch a stimulus the AI sees?
7. **Audio**: voiced audio logs (paid) or text plus one radio voice?
8. **Tools**: any one-use non-lethal item, such as a tranquilliser dart? It changes the vet clinic and the balance against "near-unbeatable".
9. **Nights as acts**: three nights, with dawn as the act break?
10. **Setting**: cold northern Pacific or Atlantic island (matches the fir kit and the geothermal frame)? Confirm with the biome report.

## 8. Unverified items and what settles each

| Item | Settled by |
|---|---|
| Fog visibility figures (5.1) are computed from the preset's formula, not rendered | The probe in 5.6 |
| Lens Flare (SRP) cost on Metal, and occlusion through alpha-tested foliage | The same probe, with the paired harness |
| Effective stealth speed 0.8 m/s (sets safe-room spacing and island scale) | Timed greybox runs by 2 or 3 players |
| Concealment, torch, light-pool and loud-zone multipliers | AI work, then playtest |
| Telemetry ranges (game values) | Playtest |
| Director values (90 to 180 s relax, one chase per 8 to 10 min) | Playtest |
| Silence bubble (weak real basis) | A design choice, not science; keep as a convention |
| L4D phase names (paper text not extracted here) | Read the PDF |
| Zoo "view before entering" licence wording | Read the UK standards PDF (linked in 2.2) |
| Names clear of conflicts | Trademark search (INPI, USPTO, EUIPO) |
| Airstrip length for a light STOL plane | Aircraft data for the chosen plane |
| CC0 textures for corrugated metal, concrete and rust | Check Poly Haven and ambientCG when choosing |
| Firewatch map and compass, Gone Home | From knowledge of the games; low stakes |
| Legal reading of 2.1 | A lawyer, before release |
