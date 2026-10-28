# Island research: the dinosaur roster and the models to buy

Date: 2026-10-21. Scope: species science, their role in this game, legal limits, purchasable models, a recommended roster
and a buying list. Nothing in the project was changed. Prices are list prices in USD, read on 2026-10-21, before tax.

**Evidence marks used below**

| Mark | Meaning |
|---|---|
| [V] | Read directly: the store page or API data was downloaded and parsed (Unity Asset Store pages via their embedded JSON, Sketchfab via its public API), or the source page was fetched |
| [S] | Seen only in a search-engine snippet. Fab, CGTrader, TurboSquid and ArtStation block fetching, so their figures are [S] |
| [U] | Unverified: stated by nobody I could read; my inference or a gap to check before buying |
| [D] | Design value: a starting number for tuning, not a fact |
| [Sp] | Speculative science: plausible, not supported by direct fossil evidence |

---

## 0. Summary

**Recommended roster: 7 species, in order of appearance**

| # | Species (in-game name) | Role | Tier | Model (store) | Price |
|---|---|---|---|---|---|
| 1 | Parasaurolophus | Herd, alarm calls, stampede risk | 0 (information) | Pxltiger, Asset Store | $55.00 |
| 2 | Compsognathus | Mobbing scavenger pack, makes noise | 1 (first threat) | DANKIE, Asset Store | $9.99 |
| 3 | Dilophosaurus | Solitary ambusher in fern thickets | 2 (first big predator) | Pxltiger, Asset Store | $50.00 |
| 4 | Triceratops | Territorial charger, obstacle, can fight raptors | 2 (hazard) | Pxltiger, Asset Store | $24.00 |
| 5 | Deinonychus ("raptors") | Main antagonist: an intelligent night pack | 3 | Pxltiger, Asset Store | $35.00 |
| 6 | Brachiosaurus | Landmark; noise cover; crush hazard | ambient | Pxltiger, Asset Store | $60.00 |
| 7 | Tyrannosaurus rex | Apex of the island's core; hunts by smell at night | 4 (apex) | Pxltiger, Asset Store | $60.00 |

- **Total: $293.99** [V]. **Demo set** (Parasaurolophus, Compsognathus, Dilophosaurus, Deinonychus): **$149.99**. Buying a
  $24.99 sauropod instead (JUNNICHI SUKO's *Brachiosaurus altithorax*) brings the total to $258.98.
- **One main supplier (Pxltiger) covers 6 of the 7 species.** All six share one art style and are modelled from current science
  (feathered Deinonychus, no frill on Dilophosaurus). Each has 2 to 4 real LODs (LOD0 9 to 20 k triangles), 4096 px PBR
  textures and 64 to 158 animations, both root motion and in place. The stalking sets are included: Sneak for the raptor and
  Dilophosaurus, Creep for the T. rex. They are declared for the Built-in pipeline only. That is a material conversion (section 5),
  not a blocker.
- **Changes to the coordinator's preliminary view**:
  1. The raptors should be a **feathered Deinonychus**, not "Velociraptor". The film's raptor is Deinonychus-sized: the
     novel's raptor was "based on Deinonychus in almost every detail" ([Wikipedia: Deinonychus](https://en.wikipedia.org/wiki/Deinonychus)).
     The real Velociraptor was 1.5 to 2.07 m long and 14 to 20 kg. A feathered design is both the science and the best legal
     distance from the film. The owner decides the name (section 7).
  2. **Gallimimus is deferred.** Its stampede role overlaps Parasaurolophus. The usable models are either very heavy (62 k
     triangles, no LOD) or very crude (1.6 k triangles). A flock of 10 to 20 costs the most animator CPU of any species.
  3. **The T. rex hunts by smell and low-frequency sound at night, not by motion.** The "it can't see you if you don't move"
     rule is a Crichton invention that the science contradicts (section 2.8). Wind and scent make a better mechanic and a
     legally clean one.
  4. **Dilophosaurus is tier 2 because it is predictable, not because it is weak.** At 6 to 7 m and 283 to 400 kg it outweighs
     a Deinonychus (60 to 100 kg) about four times over. It gets a readable ambush tell: a crest display when lit.
- **Avoid** these models: Trextor15's Dilophosaurus (it has the film's neck frill), Trextor15's "Hybrid" models, any listing
  named "JP"/"Jurassic Pack" until checked, and Sketchfab replicas ("Blue Raptor", "Indominus Rex"). Also avoid game rips
  that carry a false CC licence (section 6).
- **Performance**: a heavy encounter (T. rex, 3 raptors, 6 compys, 6 Parasaurolophus) costs about **138 k LOD0 triangles**
  with the Pxltiger set. That is 1.3 % of the forest's ~10.6 M. The same encounter with the high-poly no-LOD alternatives is
  about **1.26 M** triangles, all skinned at full resolution. The forest leaves about 1.3 to 2.0 ms of the 12.5 ms budget, so
  creatures and AI need a measured cap (section 5.3).

---

## 1. Context used

- Game: first-person stealth horror at night with a flashlight. Open world with objectives. Demo 20 to 30 min, full game
  3 to 5 h. Unity 6000.6 HDRP, MacBook Pro M4 Max, PS5 later.
- Existing AI (`Assets/Game/Scripts/Dinosaurs/RaptorAI.cs`): sight 28 m and 110 degrees, with crouch scaling 0.6. Detection
  takes 0.25 s near and 2.2 s far. Speeds are patrol 2.2, investigate 4.5 and chase 9 m/s, with noise listeners. The player
  walks at 3.2, crouches at 1.6 and sprints at 6 m/s (`PlayerController.cs`). The flashlight is 300 lm, 70 degree outer cone,
  35 m range (`PlayerFlashlight.cs`).
- Forest costs (`Logs/forest-work/build`): the worst view is about 10.5 to 11.2 ms against the 12.5 ms budget. The torch adds
  +0.53 to +1.33 ms, of which its shadows are +0.08 to +0.81 (`issues/N1-night.md`). The torch's shadow map adds 2.0 to 2.9 M
  triangles a view. The forest holds about 10.6 M triangles in view (`STATE.md`, M4). Texture memory is already 802 MiB.

---

## 2. Species

Each entry gives four things in order: the real animal, film versus science, the role in this game, and models.
The legal points are collected in section 6; the species-specific ones are repeated briefly here.

### 2.1 Compsognathus

**Real animal** [V] ([Wikipedia](https://en.wikipedia.org/wiki/Compsognathus))

| | |
|---|---|
| Size | French specimen (the largest): 1.25 to 1.4 m long *including the tail*, 29 cm at the hip, 2.5 to 3.5 kg. German specimen (juvenile): 0.70 to 0.89 m, 0.32 to 0.58 kg |
| Age, place | Late Jurassic, early Tithonian, about 150 Ma; Bavaria (Germany) and Canjuers (France) |
| Diet | Stomach contents: a small lizard (German specimen); lizards or sphenodontids (French) |
| Senses | Large eyes relative to the skull; Ostrom inferred sharp vision from fast prey (inference only) |
| Speed | 17.8 m/s from one computer model (Sellers & Manning 2007), criticised by others |

**Film versus science.** *The Lost World* shows packs that kill people and calls the species "Compsognathus triassicus".
That name is a mash-up with *Procompsognathus triassicus*, a different animal. In Crichton's novel the "compys" have a mildly
venomous bite [U: from memory of the novel, not re-checked]. The real animal is chicken-sized and ate lizards. No evidence
exists for packs or for attacks on large animals.

**Role in this game: tier 1, the first threat** [D]

- **What it is**: a scavenger pack of 4 to 10 that shadows the player at 6 to 15 m. One alone is harmless: a nip does 1 to 2 %
  health, and it flees from a stomp or kick. Five or more mob a player who is hurt, prone or hiding still, at 3 to 5 % health
  per second until the player moves or throws food. Packs that mob a large animal are game fiction [Sp].
- **The real danger is indirect.** When excited they chitter and squeal. The squeal is a noise event of radius 25 m [D] that
  draws the predators of the area. This is the tutorial for "noise has consequences".
- **Senses** [D]: sight 15 m (day) and 8 m (night). Hearing 1.2x the raptor's. Smell for carrion and the player's blood up to
  30 m downwind.
- **Night and the flashlight**: beyond 10 m the beam draws them (curiosity). A beam full on them within 5 m scatters them for
  3 to 5 s, but scattering squeals. So the light is never free.
- **How the player survives**: throw food or objects to pull the pack away, keep moving, climb onto rocks or vehicles, and
  never let them squeal inside raptor territory.
- **Where**: everywhere at the edges: around rubbish at outposts, carcass sites, forest margins, rocky shores. This is the one
  species that roams across zones, so it can be the open world's "ambient pressure".
- **Speed** [D]: 7 m/s in bursts (faster than the player's 6), 3 s at most. Then they stop and regroup.
- **Legal**: avoid a venomous or sedating bite (from the novel) and the film's staging (compys attacking a girl on a beach, a
  pack killing a lost hunter). The species name is free.

**Models**

| Model | Store, link | Price | Triangles | Textures | Rig / animations | Unity | Look | Verdict |
|---|---|---|---|---|---|---|---|---|
| Prehistoric Dinosaur - Compsognathus (DANKIE) | [Asset Store 135639](https://assetstore.unity.com/packages/3d/characters/animals/prehistoric-dinosaur-compsognathus-135639) | $9.99 [V] | 2,980 (2,063 verts) [V] | not stated [U] | Generic rig; "30+ animations", 4 skins (default, jungle, desert, arctic); "100 % compatible with Emerald AI" [V]; list not on page [U] | Last release 2019-01-07; no pipeline declared [V] | Scaly green, natural; looks good in its HDRP-like render [V: image seen] | **Buy.** The triangle count suits a pack of 10 |
| Compsognathus - Compy dinosaurs (hiepvu) | [Sketchfab](https://sketchfab.com/3d-models/none-39d35f2d269d4e30ab5178d24b433fdd) | $27.00 [V] | 12,788 (6,725 verts) [V] | PBR (AO, base, roughness, metallic, normal), size not given [V] | 10: idle, inactive idle, walk, sprint, bite, slash, dodge, taunt, roar, eat [V] | Maya/Blender files; FBX not stated [U] | Scaly green, clean; the seller also sells film replicas, but this model looks generic [V: image seen] | Second choice |
| Dinosaur-Compsognathus - 110 Animations (Trextor15) | [Asset Store 367442](https://assetstore.unity.com/packages/3d/characters/creatures/dinosaur-compsognathus-110-animations-367442) | $77.99 [V] | 111,512, no LOD, 151 bones [V] | 4096 to 1024, 22 textures [V] | 110: walk, run, ambush run, backward, idle, sit, stand up, sleep, roar, attack, death, drink, eat, sniff, leg broken, get hit, wake up, dodge L/R, turn L/R, swim [V] | Standard shader, HDRP via the conversion wizard [V] | Very detailed scaly sculpt [V: image seen] | Too heavy: 8 animals = 0.9 M skinned triangles. Only usable after decimation |
| JP Compsognathus Dinosaur (Muelsa) | [Asset Store 45319](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/jp-compsognathus-dinosaur-45319) | $19.90 [V] | 3 LODs, lowest < 1,500 [V] | 3 skins at 2048 [V] | In-place only. AI, foot IK and herd behaviour included [V]. A reviewer reports baked root-motion curves that block Unity's root motion [S] | Built-in, 2020.3.37 [V] | "JP Assets" branding [V: video thumbnail] | Fallback only; check the design first |
| Compsognathus Longipes (kyan0s) | [Sketchfab](https://sketchfab.com/3d-models/none-f6e2f391d2de43db95ec15f674cb1d30), also [Fab](https://www.fab.com/listings/c3c025cc-bdd1-422f-8114-5969f41713cd) | $39.99 [V] | 72,236 [V] | not stated | Sketchfab shows 5 animations [V] | Blender [V] | The only feathered, current-science compy found | Too heavy and too few animations for pack AI |
| 3DRT Dinosaurs - Compsognathus | [Sketchfab](https://sketchfab.com/3d-models/none-bb1b5ba717b8446db16700f4b0663402) | $29.00 [V] | 1,852 [V] | 1024 JPG [V] | 22 (3 idles, eat, walk, walk back, run, run fast, turns, death, 2 attacks, head kick, roar...) [V] | FBX [U] | Old, low detail | No |

### 2.2 Dilophosaurus

**Real animal** [V] ([Wikipedia](https://en.wikipedia.org/wiki/Dilophosaurus), [National Geographic, 2020](https://www.nationalgeographic.com/science/article/jurassic-park-got-almost-everything-wrong-about-iconic-dinosaur-dilophosaurus))

| | |
|---|---|
| Size | Largest specimen about 7 m and 400 kg. Holotype 6.03 m and 283 kg (Wikipedia). National Geographic, reporting Marsh & Rowe 2020: about 20 ft (6 m), about three-quarters of a ton |
| Age, place | Early Jurassic, Sinemurian to Toarcian, about 186 Ma; Kayenta Formation, Arizona |
| Crest | Two thin bony crests reinforced with air pockets, perhaps holding inflatable air sacs (untested). Probably for display: species recognition, mates, rivals. Possibly keratin-covered and brightly coloured [Sp] |
| Jaws | Marsh & Rowe 2020: the upper-jaw joint was "immobile and much more robust than previously thought". It was a macropredator that could take large prey and grasp it with the forelimbs |
| Social | If it hunted small prey, groups would have been small (Therrien et al. 2005) |
| Senses | No published data found [U] |

**Film versus science.** The venom comes from Crichton's 1990 novel and the expanding neck frill from the 1993 film. Neither has
fossil support. The film also shows it much smaller than the real animal. These are Universal's inventions.

**Role in this game: tier 2, the first big predator, the demo's climax** [D]

- **What it is**: one animal per territory of 1 to 2 ha of dense ferns and young firs along a stream or game trail. The existing
  forest scene is its habitat as built. It waits in cover and commits to a lunge only within 8 m. If the lunge misses, it
  pursues for at most 10 s at 6.5 m/s (slightly faster than the player's sprint) and then returns to its ambush spots.
- **The tell**: lit by the flashlight inside 12 m, it raises its head and displays the crest with a low hiss for 1.5 s before
  lunging. That tell is grounded in the crest-as-display science; the film's frill is not used. Beyond 12 m, light makes it
  withdraw into cover. As an ambusher it avoids open chases.
- **Senses** [D]: sight 25 m in daylight and 12 m at night. Hearing 1.0x. It notices movement in the ferns (the existing
  `Rustled` events of `FoliagePusher`).
- **How the player survives**: read the signs (crushed fern beds, prey remains, a glimpse of the crest). Move slowly around
  thickets, not through them. Throw an object into a thicket to make it reveal itself or lunge at the noise.
- **Legal**: no frill, no spitting (the Pxltiger package even ships an "Attack-Spit" clip; do not use it), no film colour
  scheme, no film-style rattling call.

**Models**

| Model | Store, link | Price | Triangles | Textures | Animations | Unity | Look | Verdict |
|---|---|---|---|---|---|---|---|---|
| Dilophosaurus wetherilli (Pxltiger) | [Asset Store 286641](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/dilophosaurus-wetherilli-286641) | $50.00 [V] | LOD0 20,306, LOD1 7,908, LOD2 2,420; cinematic 78,652 [V]. The listing heads these figures "Polygon count of Ankylosaurus", a copy-paste error; check on import | 4096: 5 albedo, metallic, AO, normal; 1024 proto-feathers [V] | 121, root motion and in place [V]: walk and run in four directions with starts and stops, a **Sneak set** (idle, forward, back, left, right, start), bites x3, leg and tail attacks, spit, roars x3, sit, sleep, eat x2, drink, dig, wallow, hit L/R, stun, falling, jump, turns, walk-roar, 9 additive head poses | Built-in Standard, 2022.3.30f1 [V]; no scripts except a demo controller [V] | **No frill**, double crest, sparse proto-feathers on the neck, naturalistic [V: image seen] | **Buy** |
| Dinosaur Dilophosaurus (Trextor15) | [Asset Store 254544](https://assetstore.unity.com/packages/3d/characters/creatures/dilophosaurus-254544) | $79.99 [V] | 82,488, no LOD [V] | 8192 to 2048 [V] | 91 [V] | Standard to HDRP via wizard [V] | **Has the film's neck frill** [V: image seen] | **Avoid** (legal) |
| Dilophosaurus Jurassic Dinosaur (Vipera Games) | [Asset Store 96926](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/dilophosaurus-jurassic-dinosaur-96926) | $5.00 [V] | LOD0 61,204 down to 6,110 [V] | Standard [V] | 7: attack, die, idle x2, roar, walk, run [V] | 2019 [V] | not seen [U] | Too few animations |
| JP Dilophosaurus Dinosaur (Muelsa) | [Asset Store 46008](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/jp-dilophosaurus-dinosaur-46008) | $19.90 [V] | LODs, lowest < 1,500 [V] | 2048 [V] | In place only [V] | Built-in [V] | Frill not checked [U] | Avoid unless checked |
| Dilophosaurus Wetherilli (kyan0s) | [Sketchfab](https://sketchfab.com/3d-models/none-797e4ed06c8e4faeaafce4a5531f6850), [Fab](https://www.fab.com/fr/listings/430fc620-ad6c-46f6-be78-45b0bea77f9c) | $39.99 [V] | 32,418 [V] | not stated | 1 shown on Sketchfab [V] | Blender | Realistic | Too few animations |
| Fab listing "Dilophosaurus wetherilli", 106 animations | [Fab](https://fab.com/listings/495bd921-9136-41da-b1ba-79571ce29305) | not seen | not seen | not seen | 106 animations, root bone, not on the Epic skeleton [S] | FBX not confirmed [S] | not seen [U] | Possibly the Pxltiger model on Fab [U] |

### 2.3 The raptors: Velociraptor, Deinonychus or Utahraptor

**Real animals** [V]

| | Velociraptor ([Wikipedia](https://en.wikipedia.org/wiki/Velociraptor)) | Deinonychus ([Wikipedia](https://en.wikipedia.org/wiki/Deinonychus)) | Utahraptor ([Wikipedia](https://en.wikipedia.org/wiki/Utahraptor)) |
|---|---|---|---|
| Length | 1.5 to 2.07 m (some say 2.5 m) | up to 3.3 to 3.4 m (Paul) | 6 to 7 m (Kirkland); 4.9 m for the largest referred specimen (Molina-Pérez & Larramendi 2019) |
| Mass | 14.1 to 19.7 kg (up to 28 kg) | 60 to 73 kg (Paul); 70 to 100 kg (other authors) | 250 to 350 kg typical; 290 kg (Zanno & Makovicky 2013); 391 to 777 kg (Pintore et al. 2024) |
| Age, place | 75 to 71 Ma; Mongolia, China | Early Cretaceous, Aptian to Albian; Montana, Wyoming, Oklahoma | 139 to 135 Ma; Utah |
| Feathers | Quill knobs on one ulna (the specimen's referral is now questioned) | Inferred from relatives; no skin found | Inferred |
| Groups | No evidence specific to Velociraptor | Four adults and one juvenile with Tenontosaurus remains (Ostrom); six Tenontosaurus with one Deinonychus (Oklahoma); parallel trackways (Li et al. 2007). Against: a Komodo-dragon-like mob (Roach & Brinkman 2007) | At least 7 individuals (an adult, 4 juveniles, a hatchling) in one block, possibly a quicksand trap |
| Night | Scleral rings suggest it **may have been nocturnal** (Schmitz & Motani 2011, *Science*; [NPR](https://www.npr.org/2011/04/14/135385317/dinosaur-eyes-yield-clues-to-hunting-habits), [Smithsonian](https://www.smithsonianmag.com/science-nature/just-when-you-thought-velociraptor-couldnt-get-scarier-166739736/)). The method was debated after publication [U: comment papers not re-checked] | No data | No data |

A related finding: the small *Shuvuuia* had owl-like hearing and night vision (Choiniere et al. 2021, *Science*;
[Sci.News](https://www.sci.news/paleontology/shuvuuia-deserti-dinosaur-vision-hearing-09633.html)). Some small theropods
were built for the dark.

**Film versus science.** The film's "Velociraptor" has Deinonychus's size, proportions and snout. Crichton chose the name as
"more dramatic". The first two films show it featherless. *Jurassic Park III* adds quills that do not resemble real feathers.
The real Velociraptor was turkey-sized and feathered. Pack hunting is a reasonable but contested hypothesis for Deinonychus.

**Recommendation.** Use **Deinonychus** at real size, feathered. Its 3.4 m length and 73 to 100 kg give a head at roughly adult
chest height [U: estimate]. A real-size Velociraptor (waist-high, 15 kg) cannot carry the main antagonist role in first
person. A Utahraptor (300 to 500 kg) is a different threat class and would crowd the Dilophosaurus and T. rex tiers.

**Role in this game: tier 3, the main antagonist** [D]

- **What it is**: one pack of 3 (4 at most) per region. They are active from dusk to dawn (nocturnal, consistent with
  Schmitz & Motani) and rest in dens in rocky outcrops or ruined buildings by day.
- **Pack behaviour**: noise is checked by a pair while a third circles. A "scout call" brings the others within 8 s. They flank:
  one shows itself in the beam while two close from 90 degrees off it. They adapt: the same distraction trick in the same area
  works twice, and the third time they ignore it. This makes them read as intelligent without the film's door-opening.
- **Senses** [D]: night sight 20 m with the flashlight off, against the player's roughly 5 to 10 m. They see the lit beam and
  anything it lights from 80 m, and the beam's glow through foliage from 40 m. Hearing 1.5x. Smell 40 m downwind.
- **Night and the flashlight**: the flashlight is their best cue. Light means prey. Eyeshine in the beam is the player's tell
  ("two glints in the ferns"). Eyeshine in dinosaurs is speculative: crocodilians have a tapetum lucidum, but nothing is known
  for non-avian dinosaurs [Sp].
- **How the player survives**: light off, crouch in dense cover, use hiding spots, throw objects to split the pack. Lure them into
  Triceratops territory. Reach lit outposts with doors (the raptors do not open doors in this game).
- **Speeds**: keep the existing chase speed of 9 m/s, with a pounce of 4 m.
- **Legal**: avoid a featherless film-style raptor and the specific film head sculpt. Avoid the *JP III* quill crest, the stripes
  and name of "Blue", trained raptors, the raptor pen, door-opening, and the "clever girl" side-ambush staging.

**Models**

| Model | Store, link | Price | Triangles | Textures | Animations | Unity | Look | Verdict |
|---|---|---|---|---|---|---|---|---|
| Deinonychus (Pxltiger) | [Asset Store 182587](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/deinonychus-182587) | $35.00 [V] | LOD0 12,644, LOD1 6,123, LOD2 2,988; cinematic 51,810; separate FBX LODs included [V] | 4096: 5 albedo, AO, metallic, normal [V] | 90 [V]: **Sneak set** (idle, forward, back, left, right, start), walk and run with starts and stops, run-jump, claw, palm, head and bite attacks, claw attack pivoting in place, roars x4, jump and landing, lie down, sleep, wake, sit x3, eat, drink, hit L/R, stun, falling L/R, swim, turns; additive attacks, roars and hits | Built-in, 2021.3.16f1 [V]; a reviewer says it works in URP and HDRP [S] | **Fully feathered**, current science; orange with stripes; 5 albedo variants [V: image seen] | **Buy** (retexture darker for night) |
| Velociraptor (Raptor) (Rifat_Bilalov) | [Asset Store 238785](https://assetstore.unity.com/packages/3d/characters/animals/velociraptor-raptor-238785) | $29.99 [V] | 23,600, 16,600, 11,300, 6,650 [V] | 2048: diffuse, normal, metallic/roughness [V] | 105 [V]: attacks 1 to 6, walk in 6 directions, run in 3, run attacks, run-jump, trot with attacks, jumps, hits, lie, sleep, sit, idles x3, roars, deaths x3, eat x2, pick up food x2, drink, turns 45 and 90 degrees, swim | Built-in, URP and HDRP declared, 2020.3.36f1 [V] | Featherless, generic, plain grey preview [V: image seen] | Backup if the owner wants a scaly raptor |
| Velociraptor (Animated) (WDallgraphics) | [Asset Store 316879](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/velociraptor-animated-316879) | $29.99 [V] | 7,762 quads, 8,110 verts [V] | 4096: D, N, S [V] | 35 [V]: walk, threatened walk, run, chase run (each L/R), backward, turns, idle, lay, sleep, bite, jump attack, run attack, scratch, roars, drink, eat, die L/R, shake, call | Built-in, URP and HDRP declared [V] | Scaly film-style raptor with pronated hands; Fab calls the textures hand-painted and stylized [V: image; S] | No (film look) |
| Dinosaur UtahRaptor Real dinosaurs Series (Trextor15) | [Asset Store 216085](https://assetstore.unity.com/packages/3d/characters/animals/dinosaur-utahraptor-real-dinosaurs-series-216085) | $74.99 [V] | 72,714, no LOD [V] | 8192 to 2048 [V] | 88 [V] | via wizard [V] | Head-quill crest close to *JP III*'s raptors [V: image seen] | No |
| Raptor (3dFoin) | [Asset Store 42531](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/raptor-42531) | $29.90 [V] | 9,552 [V] | 4096 [V] | 14 [V] | 2017 [V] | Old; its blurb mentions "venom" raptors [V] | No |
| 3D Deinonychus animated (MisterJon) | [Asset Store 332696](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/3d-deinonychus-animated-332696) | $12.00 [V] | 6,510 [V] | 4096 [V] | 23 incl. alert, guard, intimidation, exhausted [V] | Built-in, 6000.0.27f1 [V] | not seen [U] | Cheap alternative; look unchecked |
| Utahraptor low poly (WobblyG) | [Sketchfab](https://sketchfab.com/3d-models/none-1c04938f74ec4081bbbf83aa00824af0) | $85.00 [V] | 17,311 [V] | | 11: idle, stretch, bite, pounce, eat, sit, look around, stand, walk, run [V] | | Stylised low poly | No |
| Raptor, feathered (vfxstevenz) | [Sketchfab](https://sketchfab.com/3d-models/none-52422e9bd9534221bbce1ff57ecbb60f) | $29.99 [V] | 87,984 [V] | 4096 [V] | 2 [V] | Blender | Cinematic | Not game-ready |
| Velociraptor Rigged Feathered (V-Multi) | [ArtStation](https://www.artstation.com/marketplace/p/ynow/velociraptor-rigged-feathered) | $209 to $219 [S] | 5,866 polygons [S] | | Walk, run, lie down and jump cycles; Blender hair-particle feathers, FBX exported **without** feathers [S] | | | No |
| Blue Raptor (hiepvu) | [Sketchfab](https://sketchfab.com/3d-models/none-87f7318c80fa4cceae47265afc04a3bd) | $25.00 [V] | | | | | A replica of *Jurassic World*'s "Blue" [V] | **Never** |

### 2.4 Gallimimus (deferred)

**Real animal** [V] ([Wikipedia](https://en.wikipedia.org/wiki/Gallimimus)): about 6 m long and 1.9 m at the hip, 400 to 490 kg.
Early Maastrichtian, about 70 Ma, Nemegt Formation, Mongolia. Top speed 42 to 56 km/h (Thulborn 1982). The eyes were large and
lateral with limited binocular vision, so it turned its head to look. Feathers are highly likely (quill knobs in ornithomimosaurs).
A bonebed of several individuals suggests group living. The diet is debated: omnivore, high-fibre herbivore, or filter-feeder
(Barrett 2005 found filtering could not feed a 440 kg animal).

**Film versus science.** The film shows a featherless flock stampeding with bird-like uniform turns, animated from ostrich footage.
The real animal was probably feathered.

**Role if kept** [D]: a flock of 8 to 15 in open grassland. It bolts at 12 to 15 m/s when startled. A stampede is a hazard and a
distraction that masks the player's noise but draws predators. It overlaps Parasaurolophus, which also warns and stampedes but
adds sound-based information. **Recommendation: defer to the full game or cut.**

**Models**: Trextor15 "Dinosaur Gallimimus Real Dinosaur" ([Asset Store 278219](https://assetstore.unity.com/packages/3d/characters/creatures/dinosaur-gallimimus-real-dinosaur-278219)):
$79.99, 61,814 triangles with no LOD, 8192 to 2048 textures, 99 animations, scaly [V]. JUNNICHI SUKO "Gallimimus"
([Asset Store 38331](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/gallimimus-38331)): $19.99, 1,626
triangles, root-motion walk and run, dated look [V]. Muelsa "JP Gallimimus" ([Asset Store 67352](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/jp-gallimimus-dinosaur-67352)):
$19.90, LODs, in place only [V]. CGTrader "Gallimimus The Ostrich Mimic of the Cretaceous"
([link](https://www.cgtrader.com/3d-models/animal/dinosaur/gallimimus-the-ostrich-mimic-of-the-cretaceous)): $88.88,
animations on one timeline [S]. No Pxltiger Gallimimus exists.

### 2.5 Triceratops

**Real animal** [V] ([Wikipedia](https://en.wikipedia.org/wiki/Triceratops)): 8 to 9 m long, 6 to 10 t ("Kelsey": 6.7 to 7.3 m,
about 5.4 t). Late Maastrichtian, 69 to 66 Ma, western North America (Hell Creek, Lance and others). The forelimbs were upright,
elbows flexed back like a rhino's. The horns and frill are now thought to be mainly for courtship display. There is also healed
combat damage: a 14 % injury frequency (2009 study) and healed tyrannosaur bites on a brow horn. **There is little evidence for
herds**: one juvenile bonebed and a possible family of three.

**Film versus science.** The film's sick Triceratops lies on its side and the design is broadly faithful. Low legal risk.

**Role in this game: tier 2 hazard, not a hunter** [D]

- **What it is**: solitary, or a mother and young, on meadows, shrubland and overgrown road clearings. It holds a territory of
  30 to 40 m radius.
- **Senses**: smell 30 m; sight is unknown [U], so use 20 m.
- **Light and proximity**: a beam in its face within 20 m, or the player within 15 m of a calf, triggers a charge at 7 to 8 m/s
  for up to 40 m. Its bellow is also a noise event.
- **How the player survives**: get behind trunks thicker than 40 cm or rocks, break line of sight, leave the territory.
- **Systemic use**: lure the raptors into its territory and it fights them. At the core, a T. rex may be feeding on one: a
  "safe while it eats" window.
- **Legal**: avoid the sick-Triceratops veterinary scene and the film's tour staging.

**Models**

| Model | Store, link | Price | Triangles | Textures | Animations | Unity | Look | Verdict |
|---|---|---|---|---|---|---|---|---|
| Realistic dinosaur Triceratops (Pxltiger) | [Asset Store 126881](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/realistic-dinosaur-triceratops-126881) | $24.00 [V] | LOD0 9,224, LOD1 3,950, LOD2 1,876, LOD3 496; cinematic 38,096 [V] | 4096: 3 albedo, normal, AO, metallic [V] | 91 plus 9 poses [V]: idle, eat, walk four ways, strafes, turns, run L/R, attacks L/R, hit, stun, lie down, sleep, wake, falling, and a full **aggressive set** (idle, walk, strafe, turn, run, attack); swim; additive attacks | Built-in, 2021.3.13f1 [V]; reviewers praise root motion and textures [S] | Naturalistic, upright limbs [V: image seen] | **Buy** |
| Triceratops Animated (WDallgraphics) | [Asset Store 268547](https://assetstore.unity.com/packages/3d/characters/triceratops-animated-268547) | $24.99 [V] | 4,916 quads [V] | D, N, S [V] | 30: includes bellow, charge, prancing, trot [V] | Built-in, URP and HDRP declared [V] | Stylized textures [U] | No |
| Dinosaur Triceratops Real Dinosaur Series (Trextor15) | [Asset Store 235091](https://assetstore.unity.com/packages/3d/characters/creatures/dinosaur-triceratops-real-dinosaur-series-235091) | $79.99 [V] | 69,116, no LOD [V] | 8192 [V] | 105 [V] | via wizard [V] | | Heavy |
| Triceratops Horridus (kyan0s) | [Sketchfab](https://sketchfab.com/3d-models/none-4cdcba98ef12406fbdc294043c6950a6) | $39.99 [V] | 90,906 [V] | | 1 shown [V] | | Realistic | No |

### 2.6 Parasaurolophus

**Real animal** [V] ([Wikipedia](https://en.wikipedia.org/wiki/Parasaurolophus)): the type specimen is about 9.45 m. G. S. Paul
gives an average adult of 7.5 m and 2.6 t; a 9 m animal would be over 5 t. Campanian, 76.5 to 73 Ma; Alberta, New Mexico,
Utah. It moved on four legs while foraging and ran on two. **The crest was a resonator**: Weishampel (1981) estimated calls of 55
to 720 Hz, and modelling of *P. tubicen* found a main resonance near 30 Hz. Its hearing was probably sensitive (Hopson 1975).
Herding is inferred from hadrosaurids in general, not shown for this genus [U].

**Film versus science.** The films show generic herds. Low legal risk.

**Role in this game: tier 0, the island's alarm system** [D]

- **What it is**: herds of 4 to 8 on wet meadows, at marsh and stream edges (no swimming needed), and in horsetail and fern
  flats. They are active day and night (Schmitz & Motani found large herbivores cathemeral).
- **Alarm calls**: a herd member that sees a predator or the player inside 40 m, or hears a loud noise, gives a booming call that
  carries 300 m. Use two calls: "predator" and "unknown". Low resonant calls carry well; the 300 m is design.
- **Information both ways**: predator calls tell the player where danger is. Startling a herd tells the predators where the player
  is.
- **Stampede**: spooked herds run at 8 m/s. At 2.6 t or more, a hit is lethal.
- **Light**: a beam on the herd triggers the "unknown" alarm.
- **Use**: spook a herd on purpose to cover a dash, at the cost of drawing raptors.
- **Legal**: low risk.

**Models**

| Model | Store, link | Price | Triangles | Textures | Animations | Unity | Look | Verdict |
|---|---|---|---|---|---|---|---|---|
| Parasaurolophus (walkeri, cyrtocristatus) (Pxltiger) | [Asset Store 223710](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/parasaurolophus-walkeri-cyrtocristatus-dinosaur-223710) | $55.00 [V] | LOD0 10,406, LOD1 4,462, LOD2 1,936; cinematic 42,632 [V] | 4096: 5 albedo, normal, metallic, AO [V] | 158 files [V]: a four-legged set and a **two-legged "Stand" set**, trot and gallop with stops, run, roar, eat and drink with start, idle and end, lie down, sleep, head, tail and leg attacks, hit, stun, falling, turns, swim, walk-to-run blends, additive head poses. Includes **two species** (long and short crest) | Built-in, 2022.3.62f3 [V] | Naturalistic [V: image seen] | **Buy** |
| Parasaurolophus pack (JUNNICHI SUKO) | [Asset Store 208518](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/parasaurolophus-pack-208518) | $19.99 [V] | 6,522 and 26,088 [V] | 4096 albedo and normal [V] | Root-motion walk; names only in a screenshot [V] | Built-in; URP on 6000.0.60f1 [V] | | Budget alternative |
| Dinosaur Parasaurolophus Real Dinosaur Series (Trextor15) | [Asset Store 264109](https://assetstore.unity.com/packages/3d/characters/creatures/dinosaur-parasaurolophus-real-dinosaur-series-264109) | $74.99 [V] | 48,600, no LOD [V] | 8192 [V] | 105 [V] | via wizard [V] | | Heavy for a herd |

### 2.7 Brachiosaurus (the sauropod)

**Real animal** [V] ([Wikipedia](https://en.wikipedia.org/wiki/Brachiosaurus)): 18 to 22 m long, 9.4 to 13 m tall, 28.3 to
46.9 t for the holotype (some estimates up to 56 to 58 t, questioned). Late Jurassic, about 154 to 153 Ma, Morrison Formation,
Colorado. "Unlike other sauropods, it was unsuited for rearing on its hindlimbs." Most popular depictions, the film's included, use
*Giraffatitan* proportions. Sauropods swallowed without chewing [U: widely held, not re-checked]. Large herbivores were active day
and night (Schmitz & Motani).

**Film versus science.** The film's Brachiosaurus rears up, chews and sneezes on a child, and has Giraffatitan proportions.

**Role in this game: landmark and environmental tool** [D]

- **What it is**: 1 to 3 animals in the tall conifer forest and on the high plateau. Conifers were sauropod food, so the existing
  tall firs fit. It feeds through the night.
- **Heard before seen**: footfalls shake the camera and pad within 60 m, plus low rumbles.
- **Noise cover**: within 25 m of a walking sauropod, the player's noise radius is halved, so walking beside it is safe from
  predators.
- **Hazard**: inside 8 m of its feet, or behind its tail.
- **Senses**: none that matter; it ignores the player.
- **Cost**: needs few animations: walk, idle, eat high, eat low, drink, lie down, call.
- **Legal**: avoid the rearing "first sight" reveal with the film's music cue, chewing, and the sneeze.

**Models**

| Model | Store, link | Price | Triangles | Textures | Animations | Unity | Verdict |
|---|---|---|---|---|---|---|---|
| Brachiosaurus (giraffatitan) (Pxltiger) | [Asset Store 160378](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/brachiosaurus-giraffatitan-160378) | $60.00 [V] | LOD0 11,954, LOD1 3,212; cinematic 50,092 [V] | 4096: 3 albedo colours, normal, metallic, AO [V] | 64 [V]: walk, back, sideways, turns, run, idle, roars, eat x2, eat on hind legs, drink, leg, neck, tail and two-leg attacks, hit, lie down, sleep, wake, falling, swim, 9 head poses; root motion and in place [V]. A reviewer counts about 50 and calls it accurate [S] | Built-in, 2021.3.10f1 [V] | **Buy** (matches the set). Do not stage the "eat on hind legs" clip as the film's rearing reveal |
| Brachiosaurus altithorax (JUNNICHI SUKO) | [Asset Store 311025](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/brachiosaurus-altithorax-311025) | $24.99 [V] | 24,118; high-poly blendshape version 96,440; 89 bones [V] | 4096 or 2048 [V] | Walk (root motion and in place) and others, not listed [U] | Built-in, 2022.3.5f1 [V] | Budget option, and the true *Brachiosaurus* species |
| Diplodocus dinosaur (Pxltiger) | [Asset Store 249856](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/diplodocus-dinosaur-249856) | $55.00 [V] | LOD0 27,538 down to LOD3 1,656 [V] | 4096 [V] | 48 plus 14 additive, including eat high [V] | Built-in [V] | Alternative sauropod, a different silhouette from the film's |
| Dinosaur Brachiosaurus Sauropod (Trextor15) | [Asset Store 240113](https://assetstore.unity.com/packages/3d/characters/creatures/dinosaur-brachiosaurus-sauropod-240113) | $69.99 [V] | 78,742, no LOD [V] | 8192 [V] | 67 [V] | via wizard [V] | Heavy |

### 2.8 Tyrannosaurus rex

**Real animal** [V] ([Wikipedia](https://en.wikipedia.org/wiki/Tyrannosaurus), from offset 100k)

| | |
|---|---|
| Size | Sue 12.3 to 12.8 m long, 3.66 to 3.96 m at the hip. Sue-class adults 8.4 to 9.22 t; most adult estimates 5.4 to 8.0 t. Scotty 13 m, 8.87 t |
| Age, place | Late Maastrichtian, 69 to 66 Ma, western North America |
| Speed | Top speed mostly estimated around 9 m/s, but bone-strength and muscle studies give 18 to 27 km/h (5 to 7.5 m/s). Preferred walk 1.28 m/s (Van Bijlert et al. 2021) |
| Vision | 55 degree binocular field, wider than a hawk's; acuity up to 13x human (Stevens 2006; also [Science News](https://www.sciencenews.org/article/sight-saur-eyes-t-rex-vision-was-among-natures-best)). Eye f-number 3 to 3.8 (Carpenter) suggests **poor low-light vision and daytime hunting** (single source) |
| Smell | The best of 21 non-avian dinosaurs sampled (olfactory bulbs) |
| Hearing | Long cochlea, best at **low frequencies** (Witmer & Ridgely 2009) |
| Skin, mouth | Scaly; any feathers limited to the back (Bell et al. 2017). Lips probably covered the teeth (Cullen et al. 2023, *Science*; [ScienceDaily](https://www.sciencedaily.com/releases/2023/03/230330172120.htm)); Carr disagrees |
| Groups | Currie's pack idea is weak, not peer-reviewed |

**Film versus science.** The film says it cannot see you if you stand still: Stevens built his study to test that scene, and
the evidence points to excellent vision. The film also shows it outrunning a jeep, with a lipless, fully exposed tooth row.

**Role in this game: tier 4, the apex at the island's core** [D]

- **What it is**: one animal in the core valley: open forest with clearings, ravines, and the ruins of the main facility. It leaves
  the core only in scripted moments.
- **At night it hunts by smell and low-frequency sound**, consistent with poor low-light vision:
  - **Smell**: a scent cone downwind up to 150 m. Fresh blood doubles it. Wind direction is shown to the player through
    particles and foliage, which the wind system already drives. Shallow streams, mud and strong smells (fuel, carcasses) break
    the trail.
  - **Low-frequency hearing**: a new noise kind for generators, engines, heavy falls and big impacts, heard at 200 m.
    Footsteps are heard at the normal radius.
  - **Sight**: at night it sees the beam or the player only within 30 m. At dawn and in daylight it sees to 100 m or more, and
    standing still does not hide the player.
- **Movement**: chase at 7 m/s (above the player's 6) after 4 s of acceleration. It turns slowly; rotational-inertia studies
  suggest large theropods turned slowly [U: not re-checked]. The player escapes by weaving between trees and into gaps under
  2.5 m.
- **Warning**: footfalls felt at 100 m through camera shake and controller rumble.
- **Distraction**: only large stimuli work: a car alarm, a flare, a carcass.
- **Legal**: avoid the motion-vision rule, the water-cup ripple shot, the goat on a chain, the paddock fence breakout, the jeep
  chase and its mirror shot, the name "Rexy" and her scars, the film's head sculpt, and the film's roar.

**Models**

| Model | Store, link | Price | Triangles | Textures | Animations | Unity | Look | Verdict |
|---|---|---|---|---|---|---|---|---|
| Dinosaur T-Rex (Pxltiger) | [Asset Store 175294](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/dinosaur-t-rex-175294) | $60.00 [V] | LOD0 19,278, LOD1 9,020, LOD2 3,712, LOD3 1,468; cinematic 76,644 [V] | 4096: 5 albedo, AO, metallic, normal [V] | 104 [V]: a **Creep set** (stalking: forward, back, idle, left, right, start), walk and run with starts and back-steps, head, tail and low attacks, roars x4, idle breaks (explore, roar, scratch), eat x2, drink, sit, sleep, lie down, wake, stun, hit, falling, swim, turns, walk-roars; additive attacks and roars; head and torso poses. One reviewer counts 103 and says it is very realistic; others note foot-motion issues [S] | Built-in, 2021.3.23f1 [V] | Modern lipped look, horizontal posture [V: image seen] | **Buy** |
| Dinosaur Tyrannosaurus Rex Stan (Trextor15) | [Asset Store 273389](https://assetstore.unity.com/packages/3d/characters/creatures/dinosaur-tyrannosaurus-rex-stan-273389) | $114.99 [V] | 80,630, no LOD [V] | 8192 to 2048 [V] | 127, with muscle and jiggle motion [V] | via wizard [V] | Lipless, heavy-browed, the closest to the film look [V: image seen] | No |
| Tyrannosaurus Rex Animated (WDallgraphics) | [Asset Store 266114](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/tyrannosaurus-rex-animated-266114) | $39.99 [V] | 5,288 quads [V] | 4K D, N, S [V] | 25 [V] | Built-in, URP and HDRP declared [V] | Upright, film-like pose [V: image seen] | No |
| Tyrannosaurus Rex (Male version) (kyan0s) | [Sketchfab](https://sketchfab.com/3d-models/none-30e9e38ac7554062be7406658881d105) | $89.99 [V] | 46,528 [V] | | 11 shown [V] | Blender | Lipped, current science [V] | Cinematic alternative |
| Tyrannosaurus Rex 3.1 (vfxstevenz) | [Sketchfab](https://sketchfab.com/3d-models/none-f1bd8fcd553241a695712edab73ad81f) | $29.99 [V] | 46,500 [V] | 4096 [V] | 3: run, walk, waiting pose [V] | Blender | Cinematic | No |

### 2.9 Other famous land species considered

| Species | Real animal | Fit and verdict |
|---|---|---|
| Stegosaurus ([Wikipedia](https://en.wikipedia.org/wiki/Stegosaurus)) | 6.5 to 7 m, 3.5 to 3.8 t (some 7.5 to 9 m and 5 to 5.3 t); 155 to 145 Ma; Morrison Formation [V] | Alternative to Triceratops as the territorial hazard (tail swing instead of a charge). Pxltiger "Dinosaur Stegosaurus" ([Asset Store 189558](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/dinosaur-stegosaurus-189558)): $60, LOD0 14,472, 78 animations [V]. **Optional 8th species** |
| Pachycephalosaurus ([Wikipedia](https://en.wikipedia.org/wiki/Pachycephalosaurus)) | 4.5 m, 370 to 450 kg; 70 to 66 Ma; 22 % of domes show lesions consistent with fighting (Peterson et al. 2013) [V] | A small charging obstacle. Low priority |
| Ceratosaurus ([Wikipedia](https://en.wikipedia.org/wiki/Ceratosaurus)) or Allosaurus | Ceratosaurus 5.3 to 7 m, 418 to 1,240 kg; about 155 Ma; Morrison Formation [V] | A second mid-tier predator if Dilophosaurus wears out. Not needed for 3 to 5 h |
| Ankylosaurus | about 6 to 8 m, several tonnes, late Maastrichtian [U: not re-checked] | Passive armoured obstacle; adds little to stealth |
| Troodon or Stenonychosaurus | Small, big-eyed troodontid; possibly crepuscular [Sp]. Not famous from the films | Fits "night predator" but adds roster scope. No |
| Excluded | Spinosaurus, Baryonyx (semi-aquatic or fish-eaters), Mosasaurus (marine), Pteranodon (a flier, not a dinosaur) | Excluded by the owner's no-water rule and by complexity |

---

## 3. Senses and starting AI values (all [D], to tune)

The existing `RaptorAI` gives the reference: sight 28 m and 110 degrees, hearing 1.0, chase 9 m/s.

| Species | Sight day / night (flashlight off) | Sees the beam from | FOV | Hearing x | Smell | Reaction to the beam | Top speed | Group |
|---|---|---|---|---|---|---|---|---|
| Parasaurolophus | 60 / 25 m | 60 m | 300 (lateral eyes) | 1.5 (low frequency) | 30 m | Alarm call, then flight | 8 m/s | 4 to 8 |
| Compsognathus | 15 / 8 m | 30 m | 270 | 1.2 | 30 m, carrion and blood | Drawn beyond 10 m; scatters and squeals within 5 m | 7 m/s bursts | 4 to 10 |
| Dilophosaurus | 25 / 12 m | 25 m | 140 | 1.0 | 20 m | Crest display (1.5 s tell) then lunge within 12 m; withdraws beyond | 10 m/s lunge, 6.5 m/s chase for 10 s | 1 |
| Triceratops | 20 / 10 m | 20 m | 200 | 0.8 | 30 m | Charge within 20 m | 7.5 m/s for 40 m | 1 to 3 |
| Deinonychus | 40 / 20 m | 80 m (glow through foliage 40 m) | 120 | 1.5 | 40 m | Investigate: light means prey | 9 m/s, pounce 4 m | 3 (4 max) |
| Brachiosaurus | ignores | ignores | | | | none | 2 m/s | 1 to 3 |
| T. rex | 100+ / 30 m | 30 m | 150 (55 binocular) | 1.0, low-frequency kind at 200 m | 150 m downwind, x2 with blood | Little at night; full sight at dawn and in day | 7 m/s after 4 s | 1 |

What the existing AI needs added: scent and wind (T. rex, raptors, compys), a low-frequency noise kind (T. rex), a beam-visibility
check separate from body sight (all predators), alarm calls as noise and information events (Parasaurolophus), and pack roles
(raptors, compys).

---

## 4. Roster, progression and places

### 4.1 Difficulty order

| Order | Species | What the player learns | Demo (20 to 30 min) | Full game (3 to 5 h) |
|---|---|---|---|---|
| 1 | Parasaurolophus | Listen: the island tells you where danger is | Heard at 0 to 5 min, then seen | Throughout |
| 2 | Compsognathus | Noise has consequences; throwables | 5 to 15 min | Throughout, every zone |
| 3 | Dilophosaurus | Read the terrain and the tell; do not cross thickets | Climax at 15 to 25 min (the existing forest) | Act 1 |
| 4 | Triceratops | Territories; use animals against animals | | Act 2 |
| 5 | Deinonychus | Light discipline, hiding, splitting a pack | Heard only, a tease at the end | Act 2 to 3, the main antagonist |
| 6 | Brachiosaurus | Use big animals as noise cover | | Act 2 landmark, plateau |
| 7 | T. rex | Wind and scent; never in the open | | Act 3, core |

### 4.2 Habitat and vegetation per species (input for the island map)

| Species | Habitat | Cover the gameplay needs [D] | Space [D] |
|---|---|---|---|
| Parasaurolophus | Wet meadows, marsh and stream edges, horsetail and fern flats | Open ground with sightlines of 60 m or more, so calls and stampedes read | 2 to 4 ha per herd |
| Compsognathus | Edges, undergrowth, outposts, carcass sites, rocky shore | Rocks and vehicles to climb | Anywhere |
| Dilophosaurus | Dense ferns and young firs along streams and gullies: the existing forest | Thickets 1.5 m or taller, trails through them | 1 to 2 ha per animal |
| Triceratops | Meadows, shrubland, low cycad and palm scrub [Sp: diet], overgrown roads | Trees thicker than 40 cm and boulders to hide behind | 30 to 40 m territory radius |
| Deinonychus | Mixed forest, rocky outcrops and dens, ruined buildings | Hiding spots, doors, crawlspaces, dark interiors | One pack per region of 10 ha or more |
| Brachiosaurus | Tall conifer forest and high plateau | Trunk spacing above 6 m along its routes | 5 ha or more, visible from far |
| T. rex | Core valley: open forest, clearings, ravines, facility ruins | Lanes 6 m or wider for it; gaps under 2.5 m and timber spaced under 2 m to escape | The core, 20 ha or more |

### 4.3 Horror pacing in an open world (the owner chose open world with objectives)

1. **Territories, not spawns.** Each predator owns a home range (section 4.2). Objectives sit inside or across ranges, so the
   player chooses the order but each area keeps its predator and tension.
2. **Soft gating by threat tier.** The objectives that open the core (fuses, keycards, radio parts) are in tier 2 to 3 zones.
   The T. rex is met only after the player has learned light, noise and scent.
3. **A director on top of the creature AI**, as in *Alien: Isolation*: a "menace gauge" from proximity and line of sight. When it
   peaks, the director sends the predator away for relief; the creature still has to find the player by its own senses
   ([Game Developer](https://www.gamedeveloper.com/design/the-perfect-organism-the-ai-of-alien-isolation)). Cap the active
   threats within 80 m of the player at one large predator plus one pack.
4. **Night escalation.** Raptors range widely only at night. Dawn and day hand over to the T. rex's sight. A day/night clock
   changes routes without new content.
5. **Information species keep the world readable**: Parasaurolophus calls, compy squeals and sauropod footfalls tell the player
   what is near without a HUD.
6. **Safe places**: lit outposts with doors, where saves and documents are, give relief between pressure peaks.

---

## 5. Buying list, import plan and budget

### 5.1 What to buy, and when

| Step | Item | Store | Price |
|---|---|---|---|
| Now (demo) | Pxltiger Parasaurolophus (walkeri, cyrtocristatus) | Asset Store 223710 | $55.00 |
| Now (demo) | DANKIE Prehistoric Dinosaur - Compsognathus | Asset Store 135639 | $9.99 |
| Now (demo) | Pxltiger Dilophosaurus wetherilli | Asset Store 286641 | $50.00 |
| Now (demo) | Pxltiger Deinonychus: a real body for the existing raptor AI | Asset Store 182587 | $35.00 |
| | **Demo subtotal** | | **$149.99** |
| Later (full game) | Pxltiger Realistic dinosaur Triceratops | Asset Store 126881 | $24.00 |
| Later | Pxltiger Brachiosaurus (giraffatitan); or JUNNICHI SUKO B. altithorax at $24.99 | Asset Store 160378 / 311025 | $60.00 |
| Later | Pxltiger Dinosaur T-Rex | Asset Store 175294 | $60.00 |
| | **Total** | | **$293.99** (or $258.98 with the cheaper sauropod) |
| Optional | Pxltiger Dinosaur Stegosaurus (8th species) | Asset Store 189558 | $60.00 |
| Optional | hiepvu Compsognathus (if DANKIE disappoints) | Sketchfab | $27.00 |

The Asset Store runs regular sales (its pages list a "Sale - 50% off" category) [V]. Waiting for one could halve the bill [U].
**Audio is not included** in any recommended pack, and vocalisations are central to this design. Budget separately for them.

### 5.2 Import plan (the forest's code-built pipeline makes this cheap)

1. **Rig**: Generic (none is humanoid). Use root motion for the lunge, charge and pounce clips. For locomotion, either use the
   in-place clips with the NavMeshAgent driving position, or use root motion through `OnAnimatorMove` with
   `agent.updatePosition = false`.
2. **Materials**: Pxltiger ships Standard metallic maps: albedo, normal, a metallic TGA with smoothness in alpha, and AO. HDRP
   Lit needs a mask map (R metallic, G AO, B detail mask, A smoothness). Pack it in an editor script, as `ForestMaterials.cs`
   does for the forest.
   - Optional skin subsurface scattering with a Diffusion Profile.
   - An eye material whose emission scales with the flashlight's angle gives the eyeshine tell.
3. **Scale**: check each model against its real length on import (Deinonychus 3.4 m, Dilophosaurus 6 to 7 m, T. rex 12 m).
   One review of a different seller's Parasaurolophus reported a very small default scale [S].
4. **LODs**: use the shipped LOD meshes in an LOD Group. Unity 6.6's automatic Mesh LOD is not a substitute for skinned
   characters. It ignores skin weights, and "using Mesh LODs with Skinned Mesh Renderers does not reduce the workload related to
   calculating mesh deformations" ([Unity 6.6 manual](https://docs.unity3d.com/Manual/lod/mesh-lod-introduction.html)).
5. **Feathers** (Deinonychus, Dilophosaurus neck): Pxltiger uses textured shells and cards [U: check alpha usage]. Alpha-tested
   cards cost more in the torch's shadow map. Measure.
6. **Retexture for night**: the raptor's orange should go to dark mottled tones. Use the 5 albedo variants as a base.

### 5.3 Performance budget for creatures

- **Headroom**: the forest's worst view is 10.5 to 11.2 ms against 12.5 ms. Creatures, AI and audio must share about
  1.3 to 2.0 ms.
- **Triangles in a heavy encounter**:
  - Pxltiger set, at LOD0: T. rex 19.3 k + 3 raptors 37.9 k + 6 compys 18 k + 6 Parasaurolophus 62.4 k = **about 138 k**. That
    is 1.3 % of the forest's ~10.6 M, about doubled in the torch's shadow pass.
  - High-poly alternatives (Trextor15, no LOD): 80.6 k + 218 k + 669 k + 292 k = **about 1.26 M**, all skinned at full
    resolution.
- **Texture memory** (BC7 or BC5, 1 byte per pixel, plus 33 % for mips): a 4096 map is about 22 MB and a 2048 map about 5.6 MB.
  - At about 4 maps a species, 7 species cost about 630 MB at 4K or about 160 MB at 2K. The forest already uses 802 MiB.
  - **Default to 2048.** Keep 4096 albedo and normal only for the T. rex and the raptor, which the flashlight shows close up.
  - Load only the albedo variants used.
- **CPU**: set animator culling to `CullUpdateTransforms` and cap animated creatures within 60 m at about 16. Distant herds
  update at a lower rate. Measure all of this with a "creature stress" view added to the existing frame harness before buying
  the full set. Skinning and animator costs on the M4 Max were not measured here [U].

---

## 6. Legal

Not legal advice. Ideas (a dinosaur, a stampede, a predator that hunts at night) are not protected by copyright. Specific
expressions (designs, sculpts, sounds, scenes, characters) and trademarks (names, logos) are. Ask an IP lawyer before release.

**Universal's inventions to avoid**

| Area | Avoid |
|---|---|
| Names and marks | Jurassic Park, Jurassic World, Isla Nublar, Isla Sorna, InGen, Biosyn, Masrani, Mr. DNA, the T. rex skeleton logo, the park gate, "Rexy", "Blue", Indominus rex, Indoraptor and the other hybrids |
| Designs | Dilophosaurus with frill or venom spit; a featherless Deinonychus-sized "Velociraptor" with the film's head; the *JP III* quill-crested raptors; the film T. rex head sculpt and colours; the compy's venomous bite (novel); the films' recorded vocalisations |
| Scenes and devices | Frog DNA filling the gaps; the lysine contingency (novel); raptors opening doors; "it can't see you if you don't move"; the water-cup ripple; the goat on a chain; the T. rex paddock breakout and the jeep chase with its mirror shot; the Brachiosaurus rearing reveal and its sneeze; the sick Triceratops; trained raptors; the park's tour vehicles, fences, signage and visitor centre; the amber mosquito; the films' catchphrases |

**Evidence that Universal enforces**

- Universal ordered the fan game *Jurassic Park: Operations* to remove all Jurassic Park branding, "from the iconic gates to the
  title". It became *Cretaceous Kingdom* and kept its dinosaurs
  ([Inside the Magic, 2024](https://insidethemagic.net/2024/05/jurassic-park-fan-made-game-cretaceous-kingdom-dr1/)).
  Whether the gate and paddock designs themselves are protected was left open there.
- In 1993 Utahraptor was nearly named "spielbergi". The name was dropped, reportedly while Universal threatened museums over
  "Jurassic" in exhibit titles. Kirkland's own quote gives a different reason; the threat is secondary-sourced
  ([Wikipedia](https://en.wikipedia.org/wiki/Utahraptor), [Deseret News, 1993](https://www.deseret.com/1993/6/15/19051844/director-loses-utahraptor-name-game)).

**Store rules and traps**

- Film replicas are sold openly: Trextor15's frilled Dilophosaurus, Sketchfab's "Blue Raptor" and "Indominus Rex", and a compy
  "roughly based on the version from JP". A Fab forum thread complains of copyrighted content on sale
  ([forum](https://forums.unrealengine.com/t/issue-copyrighted-content-sold-on-fab-due-to-missing-quality-control/2095205/20)).
  Buying one does not license Universal's design.
- Sketchfab's own Standard licence is "meant for generic original content" without "designs that may be copyrighted by someone
  else" ([Sketchfab FAQ](https://help.sketchfab.com/en/articles/16152225-store-license-usage-faq)).
- Sketchfab also hosts game rips with false CC licences: "Jurassic Park the Game - Dilophosaurus" (CC Attribution, 84
  animations) and "primal carnage dilophosaurus" [V]. **Never use rips.**
- Asset Store packs named "JP" or "Jurassic Pack" (Muelsa) and Trextor15's "Hybrid" models trade on the films. Avoid them unless
  checked model by model.

**Licences**

| Store | Terms |
|---|---|
| Unity Asset Store | Standard Unity Asset Store EULA, "Single Entity" licence, as shown on every page fetched [V]. Every page also showed the label "Extension Asset", including pure 3D models, so this may be a page default [U]. Extension Assets are licensed per seat; others are per entity ([EULA FAQ](https://assetstore.unity.com/browse/eula-faq), [Unity support](https://support.unity.com/hc/en-us/articles/208601846)). A solo developer needs one licence either way |
| Sketchfab Store | "Standard" licence: worldwide, any media, commercial or not, derivative works; royalty-free, no credit needed. Reselling the model file itself is not allowed [S, third party] |
| Fab | Standard Licence: commercial use in any engine; Personal and Professional tiers (split at $100 k revenue) grant the same rights; no redistribution of the asset itself ([Epic docs](https://dev.epicgames.com/documentation/en-us/fab/licenses-and-pricing-in-fab)) [S] |

---

## 7. Open questions for the owner

1. **Feathered raptors?** I recommend yes: Pxltiger Deinonychus, retextured dark. It is the science and the strongest legal
   distance. The scaly backup is Rifat_Bilalov's Velociraptor ($29.99).
2. **The raptors' name**: "Deinonychus" (accurate, recommended; staff can call them "raptors") or "Velociraptor" (the owner's word,
   but the real animal is 2 m and 15 kg)?
3. **Gallimimus**: cut, defer to the full game, or swap it in for Parasaurolophus as the herd species?
4. **Sauropod**: Pxltiger Brachiosaurus ($60, matches the set, Giraffatitan proportions), JUNNICHI SUKO *B. altithorax* ($24.99),
   or Pxltiger Diplodocus ($55, a different silhouette from the film's)?
5. **Demo content**: Dilophosaurus as the demo climax with the raptors only heard (recommended), or a raptor encounter in the demo?
6. **New senses**: add scent with wind and a low-frequency noise kind to the AI framework (needed for the T. rex design)?
7. **Buying**: buy the demo set ($149.99) now, or wait for an Asset Store sale? Who sources the vocalisations, and with what
   budget?

---

## 8. Sources

**Science**
- [Compsognathus](https://en.wikipedia.org/wiki/Compsognathus), [Dilophosaurus](https://en.wikipedia.org/wiki/Dilophosaurus),
  [Velociraptor](https://en.wikipedia.org/wiki/Velociraptor), [Deinonychus](https://en.wikipedia.org/wiki/Deinonychus),
  [Utahraptor](https://en.wikipedia.org/wiki/Utahraptor), [Gallimimus](https://en.wikipedia.org/wiki/Gallimimus),
  [Triceratops](https://en.wikipedia.org/wiki/Triceratops), [Parasaurolophus](https://en.wikipedia.org/wiki/Parasaurolophus),
  [Brachiosaurus](https://en.wikipedia.org/wiki/Brachiosaurus), [Tyrannosaurus](https://en.wikipedia.org/wiki/Tyrannosaurus),
  [Stegosaurus](https://en.wikipedia.org/wiki/Stegosaurus), [Pachycephalosaurus](https://en.wikipedia.org/wiki/Pachycephalosaurus),
  [Ceratosaurus](https://en.wikipedia.org/wiki/Ceratosaurus) (Wikipedia)
- [National Geographic: Jurassic Park got almost everything wrong about Dilophosaurus (2020)](https://www.nationalgeographic.com/science/article/jurassic-park-got-almost-everything-wrong-about-iconic-dinosaur-dilophosaurus)
- Schmitz & Motani 2011 on nocturnality: [NPR](https://www.npr.org/2011/04/14/135385317/dinosaur-eyes-yield-clues-to-hunting-habits),
  [Smithsonian](https://www.smithsonianmag.com/science-nature/just-when-you-thought-velociraptor-couldnt-get-scarier-166739736/)
- Choiniere et al. 2021: [Sci.News](https://www.sci.news/paleontology/shuvuuia-deserti-dinosaur-vision-hearing-09633.html)
- Stevens 2006: [Science News](https://www.sciencenews.org/article/sight-saur-eyes-t-rex-vision-was-among-natures-best),
  [Science News Explores](https://www.snexplores.org/article/supersight-dino-king)
- Cullen et al. 2023: [ScienceDaily](https://www.sciencedaily.com/releases/2023/03/230330172120.htm)

**Design and technology**
- [Game Developer: the AI of Alien: Isolation](https://www.gamedeveloper.com/design/the-perfect-organism-the-ai-of-alien-isolation)
- [Unity 6.6 manual: Mesh LOD](https://docs.unity3d.com/Manual/lod/mesh-lod-introduction.html)

**Legal and licences**
- [Inside the Magic: Cretaceous Kingdom (2024)](https://insidethemagic.net/2024/05/jurassic-park-fan-made-game-cretaceous-kingdom-dr1/),
  [Deseret News (1993)](https://www.deseret.com/1993/6/15/19051844/director-loses-utahraptor-name-game)
- [Sketchfab store licence FAQ](https://help.sketchfab.com/en/articles/16152225-store-license-usage-faq),
  [Fab licences](https://dev.epicgames.com/documentation/en-us/fab/licenses-and-pricing-in-fab),
  [Unity EULA FAQ](https://assetstore.unity.com/browse/eula-faq),
  [Unity support on Asset Store licences](https://support.unity.com/hc/en-us/articles/208601846),
  [Fab forum: copyrighted content](https://forums.unrealengine.com/t/issue-copyrighted-content-sold-on-fab-due-to-missing-quality-control/2095205/20)

**Stores**: every model link is in its table above. Unity Asset Store figures (price, publisher, version, pipelines,
descriptions) were read from the pages' embedded JSON on 2026-10-21. Sketchfab figures (price in cents, triangles, animation
count, licence) came from `api.sketchfab.com/v3`. Fab, CGTrader, TurboSquid and ArtStation returned 403 or empty pages to
fetching, so their figures are from search snippets [S].

**Not verified**
- The exact animation lists of DANKIE's compy and JUNNICHI SUKO's sauropod.
- Whether Muelsa's JP Dilophosaurus has a frill.
- The feather construction in Pxltiger's models.
- Skinning and animator costs on the target hardware.
- Sauropod chewing and T. rex turning studies (not re-checked).
- The Asset Store sale cadence.
