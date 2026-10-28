# Scope review: island design v1 (`docs/design/ilha.md`)

Reviewer: producer (scope, effort, cost, cut list). Date: 2026-10-22. The draft was not edited.

Read: `docs/design/ilha.md` (all), `Logs/island-work/map-spec.json` (sites, zones, paths, fences),
`research/open-world.md` sections 1 and 5, `research/species.md` (buy list), `research/facility.md` section 6,
`Logs/forest-work/build/STATE.md` (milestone dates and review rounds), and the gameplay code under `Assets/Game/Scripts`.

Marks used here are the draft's own: [M] measured in this project, [V] checked at a linked source, [E] estimate with the
arithmetic shown, NOT VERIFIED where nothing confirms it.

---

## Verdict

- **The demo is achievable and is roughly the right slice.** It sits on the forest that already exists, is laid out
  in the final world and uses real gates, and it starts with a threat lighter than the raptors, as the owner asked. It needs
  four trims (issues 7, 8, 13 and 14). With them it is about 34 to 62 agent-days, or 1.5 to 3 calendar months [E]. The draft
  says 20 to 34 agent-days.
- **The full game is not credible at 76 to 130 agent-days.** If the draft's own unit rates are multiplied by its own counts
  (7 species, 29 sites, 6 new biome kits, 7 island systems) and the lines it leaves out are added (audio, playtests,
  tooling, release), the total is **about 140 to 330 agent-days** [E]. That is 1.8 to 2.5 times the draft, or about 6 to 16
  calendar months with the draft's 1.3 to 1.5 calendar factor.
- **Proportions are mostly right.** 13 main objectives for 3 to 5 h (15 to 20 min each), 7 zones (30 to 45 min each) and
  4 learning rings are all in proportion. Three things are oversized: the island is at the top of the research range while
  the walking pace is NOT VERIFIED, about 15 sites have interiors, and 7 species plus sheep all get full AI.
- **Money is not the constraint; time is.** The realistic budget is about $720 to $1,600 [E], against the draft's $550
  to $850. The binding constraints are owner hours (capture reviews, playtests, audio direction) and tuning for feel,
  which agents cannot judge. The forest shows this: wind, a feel feature, took 2.5 of the forest's 5 days [M].
- **The cut list below saves about 25 to 70 agent-days** [E]. It keeps all 7 zones, all 8 biomes, all 7 species and all
  13 main objectives, and the main path stays at 3.1 to 4.3 h.
- **Treat the demo as the calibration run.** Log the real agent-days for each site, species and system in F2 and F3, then
  re-plan F4 and F5 from those rates before buying any full-game asset.

### Draft against this review

| Quantity | Draft | This review |
|---|---|---|
| Agent-days to the demo (F0 to F3) | 20 to 34 | 34 to 62 [E] |
| Agent-days, whole game | 76 to 130 | 140 to 330 as drafted; 110 to 260 after the cuts [E] |
| Calendar (x1.3 to 1.5) | not stated | 6 to 16 months as drafted; 5 to 13 after the cuts [E] |
| Contingency | none | included in the ranges above; replan after the demo |
| Money, whole game | $550 to $850, no audio | about $720 to $1,600, plus a sound designer and a lawyer if used [E] |
| Sites with interiors | not counted | about 15, plus a 969 m tunnel |
| Animated animals | 7 species | 7 species plus sheep (S26) plus birds (6.5) |
| Audio in the project today | "half the horror" | 0 audio files, 0 animation clips or controllers, 0 scripts that use `Animator` or `AudioSource` [M] |

---

## Issues

Each issue appears once, most severe first.

### 1. CRITICAL: the effort table contradicts the draft's own unit rates (8.2, 7.3)

**Problem.** The 76 to 130 agent-day total comes from the forest's pace. The forest measured procedural environment work
only: one biome on 1.87 ha, with no gameplay, animation or audio. The phase totals do not add up to the unit rates the
draft and its research give for the same work:

- **F1 (5 to 8 days)** contains SIS-1. Section 7.3 rates SIS-1 as G ("several weeks"), and F1 also holds ten more items
  (contract, island data, two generation passes, seams, stamps, `Island.unity`, NavMesh per agent type, save skeleton,
  audits, and probes S1 to S8).
- **F3 (8 to 14 days)** contains three new creature AIs. At the draft's 2 to 5 days per species they need 6 to 15 days on
  their own, and F3 also holds the director, the torch rules, concealment, the receiver, objectives, save, audio, UI and S10.
- **F5 (38 to 62 days)** holds about four species (8 to 20 days at the same rate) and 25 sites. `open-world.md` 1.4 rates
  sites at 1 to 3 days each, which gives 25 to 75 days for the sites alone.
- The SIS table sums to **34 to 94 days** at the draft's own sizes (P: a few days; M: 1 to 2 weeks; G: several weeks).
- The draft lists eight gameplay systems with no size: the director, creature tiers, new senses, concealment, save,
  telemetry, door, gate and power state, and NavMesh per type.
- These lines are missing entirely: audio production, playtest rounds, debug tooling, Windows build and minimum spec,
  store and release, and owner review time.

The recount, using the draft's rates where it has them [E]:

| Item | Count | Rate (source) | Agent-days |
|---|---|---|---|
| Species: AI, animation, sound, tuning | 7 | 2 to 5 each (8.2; `open-world.md` 1.4) | 14 to 35 |
| Sites | 29 | 1 to 3 each (`open-world.md` 1.4) | 29 to 87 |
| New biome kits (B2 to B7) | 6 | 1.5 to 2 each (measured M0 to M4, `open-world.md` 1.4) | 9 to 12 |
| Island systems SIS-1 to SIS-7 | 7 | P 2 to 4, M 5 to 10, G 10 to 20 (7.3) | 34 to 94 |
| Gameplay systems listed without a size | 8 | 2 to 4 each [E] | 16 to 32 |
| Objective, act and optional scripting | 13 + 8 | [E] | 4 to 8 |
| Audio: sourcing, integration, mix | | [E] | 8 to 15 |
| Playtest rounds and tuning | about 4 rounds | [E] | 8 to 15 |
| Release: Windows build, store page, builds | | [E] | 4 to 8 |
| F0 decisions and F6 polish | | draft | 11 to 22 |
| **Total** | | | **about 140 to 330** |

**Fix.**

- Replace 8.2 with a bottom-up table like the one above: count times rate, with the source of each rate.
- Add the missing lines.
- Define "agent-day": the forest ran near-continuously for 6 days, Tuesday to Sunday, with the owner reachable. State the
  owner's review hours per week and convert the total to calendar months.
- Add a rule: log the actual agent-days for each site, species and system during F2 and F3, then re-estimate F4 to F6
  from those rates before buying any full-game asset (gate G-replan in 8.2).

### 2. HIGH: the creature-cost probe runs after the art it constrains (7.8 S10, 8.1, decision 10)

**Problem.**

- The demo's exit gate requires the environment at or under 10.0 ms and the whole frame at or under 12.5 ms with creatures
  present.
- The finished biome B1 already misses 10.0 ms by 0.4 to 1.8 ms [M, W1 R2].
- S10, which measures creature cost, is scheduled in F3, after F2 has built Z1's art against the 10.0 ms ceiling.
- Decision 10, which lever pays for that ceiling, is not among the five decisions marked as blocking the demo.

If skinning, feathers and shadows cost more than 1.0 ms, F2's art gets redone.

**Fix.**

- Move S10 into F0. Buy the Deinonychus ($35) and the Dilophosaurus ($50) first, put them in the existing forest on the
  existing `RaptorAI`, and run the harness's creature-stress view.
- Set the environment ceiling from that measurement, and mark decision 10 as blocking F2.
- This also proves the creature import path (mask map from Standard maps, LOD groups, root motion) on the first two models
  before the remaining $209 is spent.

### 3. HIGH: no debug tooling and no playtest plan for a 3 to 5 h open game (8.1 exit, 8.2 F4 exit)

**Problem.**

- The draft never mentions debug tools: no jump to an objective or act, no teleport, no creature spawn, no director
  overlay.
- F4's exit needs a full 3 to 5 h playthrough by 2 testers. The demo's exit needs 3 to 5 first-time players.
- The owner cannot be a first-time player, and agents cannot judge feel.
- Lines A, B and C in free order give 6 strict orders, and O7 changes Z5 for line C. One sweep of the orders costs 18 to 30
  tester hours [E].
- The forest needed 7 capture-review rounds in 5 days for a single tile (M2 r1 and r2, M3 r1, M4 r1 and r2, W1 r1 and r2)
  [M, STATE.md].

**Fix.**

- **Debug menu, built in F1:** start at act N with lines X done, teleport to any site, spawn or remove creatures, an
  overlay with the director's T value, state and territories, and god mode.
- **Run logger, built in F3:** position at 1 Hz, speed, director state, deaths with cause, minutes per objective, and the
  path taken. Agents analyse the logs; the owner watches the recordings.
- **Testers:** recruit 8 to 10 external testers in tranches. Use 3 to 5 for the demo and 2 or 3 per F4 round, giving each
  round different line orders.
- **Owner hours:** budget them explicitly, about 2 to 3 review rounds per zone and 2 per species [E].

### 4. HIGH: sites with interiors are the biggest under-costed content (5.1, 6.3, 8.2 F5, 8.4)

**Problem.** About 15 sites have interiors, and the tunnel adds another:

| Site | Interior |
|---|---|
| S02 Field Station | Quonset hut |
| S05 Staff Village | 16 to 24 houses, mess hall, laundry, infirmary, security post |
| S07 Port | office, cold store |
| S08 Military Station | command bunker |
| S11 Pump Station | pump house |
| S12 Power Plant | turbine hall, control room |
| S13 Summit Station | equipment shelter |
| S15 Labs and Incubation | 2-floor block with airlocks |
| S16 Security Office | office |
| S17 Vet Clinic | clinic rooms |
| S18 Carnivore House | keeper corridor |
| S19 Paddock Control | control room |
| S22 Sauropod Shelter | concrete shelter |
| S23 Hangar | hangar office |
| S10 Greenhouses | 4 to 6 glasshouses |
| U1 service tunnel | 969 m, with niches every 25 to 30 m |

- No interior has been built in this project.
- HDRP interior lighting (light and reflection probes) has never been done here; the forest deferred its probe bake to M5.
- Each room must satisfy LV-5 to LV-7 (two exits, no straight run over 12 m, a look-in point per room).
- The interior props budget is $50 to $150, and the one named pack is judged below the forest's realism.

**Fix.**

- Cap the full game at **6 hero interiors**: S02 Field Station, S08 bunker, S12 power-plant control room and turbine hall,
  S15 labs, S23 hangar office and S13 summit shelter.
- Reduce the village to 6 houses with 2 enterable (plus the infirmary); board up the rest.
- Make every other site an exterior plus at most one room.
- Use the demo's Quonset as interior probe S11: log its agent-days, frame cost and look, then re-rate the 5 remaining hero
  interiors from it.
- Budget $150 to $400 for interior props (issue 15).

### 5. HIGH: audio has no budget and no effort line (8.4, decision 5, 8.2 F3)

**Problem.**

- The draft calls audio "half the horror", and its design leans on sound everywhere: "hear before you see", sound
  landmarks, silence bubbles, alarm calls, a low-frequency noise type and the receiver.
- The project has 0 audio files and 0 scripts using `AudioSource` [M].
- In the plan, audio is one item inside F3 and its money is open decision 5.
- The demo alone needs vocals for 3 or 4 species, plus fire, surf, wind, footsteps on 6 or more surfaces, the generator, the
  radio, the receiver's beep and stingers.
- The legal table forbids the films' vocalizations, so stock packs must be layered and changed, not dropped in.

**Fix.** Add an audio line: 3 to 5 agent-days for the demo and 8 to 15 for the whole game [E]. Sources:

- **Sonniss GDC bundles** for ambience and foley. They are free, royalty-free, need no attribution, and cover unlimited
  projects [V, [Sonniss](https://sonniss.com/gameaudiogdc)].
- **A creature-vocal library as raw material**, for example Magic Sound Effects "Dinosaur Vocalizations" (228 SFX, $15
  minimum) [V, [itch.io](https://magicsoundeffects.itch.io/dinosaur-vocalizations)]. Its page states two different licence
  scopes (unlimited, and "one commercial project"); confirm with the seller.
- Give each species its own layered set, so no species shares a stock call.

Further rules:

- Keep the demo's documents text-only, and make the radio static with subtitles.
- Decide in F0 whether a sound designer is contracted for the creature voices. Fee NOT VERIFIED.

### 6. MEDIUM: F4 cannot meet its exit because the species AIs it needs are booked in F5 (8.2)

**Problem.**

- F4's exit is a full 3 to 5 h playthrough with paced quiet time ("quiet never exceeds Q").
- That pacing depends on the T. rex (scent, wind, low-frequency noise), the full raptor packs (flanking, adaptation),
  the Triceratops charge and the herd alarms.
- 8.2 puts "Triceratops, Brachiosaurus, T. rex and the full packs, 2 to 5 days per species" inside F5's art passes.

**Fix.** Move all species behaviour into F4: they can run on bought models with placeholder materials, or on capsules. Leave
only art integration in F5 (HDRP materials, LODs, retexture, vocals). Issue 1's recount already makes this move.

### 7. MEDIUM: the demo builds three new AIs but never shows the main antagonist (8.1, 4.1, decision 3)

**Problem.**

- The demo needs new compy (alone and pack), Dilophosaurus and herd AIs. The herd also brings stampedes and the GG agent.
- The game's sales hook and core loop are absent: raptors that read the torch beam (T1 to T3 are tuned around a raptor
  seeing the beam at 80 m).
- The Deinonychus ($35) sits in the demo purchase list, but no player sees it.
- The raptor is the one AI that already exists: `RaptorAI` (357 lines) patrols, investigates, chases and searches [M].

**Fix.**

- **Herd:** audio only in the demo, plus at most a spline-driven silhouette crossing the burn. Defer the Parasaurolophus
  ($55) to F4.
- **Raptor:** add a 2 to 4 minute beat after O3, at the Forest Gate. One raptor, on the existing AI with the bought body,
  first seen as eyeshine and avoidable, ending on the hook.
- **Kept as drafted:** compys first and the Dilophosaurus as the climax, which respects the owner's "lighter threat first".
- The swap is about cost-neutral (a herd AI out, a re-bodied raptor in), and it proves the creature pipeline on the animal
  that matters most.

### 8. MEDIUM: the demo waits behind the whole F1 spine, and SIS-5's order contradicts the demo's size (8.2, 7.3, 7.4)

**Problem.**

- The demo cannot start until all of F1 is done, including the island-wide NavMesh for 4 agent types, the three creature
  tiers, save across streaming (S6), memory at 9 tiles (S5) and AI cost off-screen (S7).
- SIS-5 is placed "before any area larger than about 2 tiles (F4)", yet Z1 covers 5 land tiles and 15.3 ha, 8 times
  today's core.
- 7.4's own table puts the loaded set at 52,000 to 70,000 GameObjects until SIS-5, which is 2.5 to 3 times today's scene.

**Fix.**

- Split F1 into two parts:
  - **F1a**, what the demo needs: island data and contract, multi-tile generation, seams, cell activation for Z1, and S1
    to S3.
  - **F1b**, moved to F4: island-wide NavMesh per type, creature tiers, S4 to S7.
- Make S2 a hard gate for the demo. If it fails, move SIS-5 into F1a.

### 9. MEDIUM: SIS-4 (distant view) is the costliest optional system, and only Z5's clear air needs it (3.5, 7.3, 8.3)

**Problem.**

- SIS-4 is rated M to G, 5 to 20 days [E from 7.3's sizes].
- It exists only because Z5's plateau has 80 to 150 m of clear air, where trees culled at 55 m would pop in.
- 8.3 calls Z5 "the riskiest zone for the frame".
- The demo already shows that emissive lights plus low-resolution silhouettes are enough at vantages.

**Fix.**

- Set Z5's mist to 50 to 60 m, as in the burn clearing, except at the towers and the summit.
- Use the demo's backdrop technique (emissive lights and low-resolution silhouettes) everywhere.
- The plateau still reads as open, at twice the forest's 30 m, and the final crossing keeps its little cover.
- Defer SIS-4 to a post-launch update. This is the largest single saving.

### 10. MEDIUM: the island is at the top of the research range while the pace is unverified, and Z3 is big for its content (2.2, 2.3)

**Problem.**

- Playable land is 140 to 150 ha and main routes total 7.0 km. The research range is 120 to 150 ha (centre about 125 ha)
  and 4 to 7 km of routes (`open-world.md` 1.3).
- Everything rests on 0.6 to 1.0 m/s, NOT VERIFIED.
- **Z3** has 29.0 ha (16 % of the zones' 182.3 ha) of B1 and B2, the two costliest biomes to render. It carries one main
  objective (O6, 20 to 25 min, about 10 % of main time) and 6 to 10 days of art.
- **Road R8** is 794 m, 19 % of the 4.24 km of road, and serves only the optional lighthouse (S25).
- Z7's caldera is large too (46.2 ha), but its 25 m fog makes it cheap, so keep it.

**Fix.**

- In F4's greybox, fold Z3 into a gully corridor of about 10 ha that keeps S11 (pump, O6) and DL-01 and DL-02.
- Make R8 and S25 non-playable. Keep the L2 lighthouse as a backdrop light.
- Target about 120 ha and 5.5 km of main routes.
- Grow the island only if the demo's measured pace says so (pace above 1.0 m/s means more content is needed).
- B2 still appears in Z1 and in Z6's warm valley, so no vegetation type is lost.

### 11. MEDIUM: animals beyond the seven species, and AI depth that is not needed (4.3, 4.6, 4.8, 5.1 S26, 6.5)

**Problem.** The draft hides more animal work than its seven species suggest:

- S26 has "loose sheep, live bait": an 8th animated animal with AI, not in the purchase list.
- "A flock of birds lifting from the canopy" is a further animal effect.
- Triceratops "fights raptors", and the T. rex "may be eating a Triceratops". Both need paired animations, a known sink.
- Herds stampede at 8 m/s with lethal impact, which needs the GG NavMesh agent and impact physics.

**Fix.** Give 4 species full AI: compy, Dilophosaurus, Deinonychus and T. rex. Give the rest scripted behaviour:

- **Triceratops:** charge only. Its bellow becomes a threat event that turns raptors away, with no combat.
- **Parasaurolophus and Brachiosaurus:** splines, with stampedes on authored lanes. The GG bake then covers only the T. rex
  and Triceratops ranges.
- **T. rex feeding:** an eating clip on a static carcass.
- **Sheep:** cut. The farm's lure becomes a carcass.
- **Birds:** a particle burst.

This saves about 4 to 8 agent-days [E].

### 12. MEDIUM: free order lets players skip the staged species intros, and the lock graph omits reachability (1.6 rule 5, 2.3, 5.3)

**Problem.**

- 5.3 lists objective dependencies but not which ground is reachable after O3.
- R6, R7, T5 and the caldera crossings T6a and T6b have no lock (2.3), and the pass fences are "dead until O7".
- So a player can go hub → R6 → R7 → summit → T5 → Z5 and skip TRI-2, the Triceratops intro on R5.
- A player can also reach the T. rex basin by T6b before the staged sighting from V7.
- Rule 5 ("each species is introduced in scene") then needs one authored intro per entry route.

**Fix.**

- Add a "reachable by act" table to `map-spec.json`.
- Add an audit to 7.3 step 8: every route into a species' first territory passes its intro trigger.
- Place intros at chokepoints every route crosses. For example, put the T. rex sighting on the R3 bunker compound that every
  route passes, and either gate T5 or add a second Triceratops intro at its Z5 end.

### 13. MEDIUM: no target platform or minimum spec (7.4, 8.1 exit)

**Problem.**

- Every budget is measured on an M4 Max at 2560x1440.
- The draft never says where the demo ships.
- If the demo ships on Steam, the September 2026 survey shows Windows at 95.03 %, macOS at 1.92 % and 1920x1080 as the
  primary display for 47.91 % of users [V, [Steam survey](https://store.steampowered.com/hwsurvey/)].
- HDRP on DX12 or Vulkan is a different backend from Metal, and nothing in the project has been measured on it.

**Fix.**

- Decide the demo's platform and audience in F0 (a public Steam demo, a pitch, or internal).
- If it is Steam: add a Windows build plus one mid-range PC measurement at 1920x1080 to the demo's exit, and set the
  environment ceiling per platform.
- Store-page work then goes into the release line from issue 1.

### 14. MEDIUM: failure and checkpoints are undefined, so a death at the climax costs about 10 minutes (5.5, 7.7)

**Problem.**

- Saves happen at secured shelters plus an autosave per main objective.
- In the demo, SR1 opens only at minute 26 to 30. The autosaves land after O1 (about minute 10) and O2 (about minute 22).
- A first-time player killed by the Dilophosaurus (minutes 13 to 22) goes back about 10 to 14 minutes. That inflates the
  measured demo length and drives players away.
- In the full game, objectives are 15 to 35 minutes apart.
- Today `GameSession` reloads the whole scene on death [M].

**Fix.**

- Add silent checkpoints before every staged first encounter and on entering each zone. Keep manual saves at shelters.
- Define for each species whether contact kills outright or does damage.
- Add "deaths" and "minutes replayed" to the demo's exit metrics.

### 15. MEDIUM: the money table leaves out audio, realistic interiors, the store fee and trademarks (8.4)

**Problem.** The draft's $550 to $850 leaves out audio and the store fee, budgets interiors at $50 to $150, and leaves the
trademark search unpriced. The revised table:

| Line | Draft | Revised | Basis |
|---|---|---|---|
| Creatures (7) | $293.99 | $293.99 | [V] in the draft; T. rex $60 and compy $9.99 re-checked today |
| Plant test E2 and plant kits | $100 to $190 | $100 to $190 | draft |
| Coast & Dunes, L.V.E | $130 | $60 to $130 (L.V.E optional if caves stay deferred) | draft |
| Interior props | $50 to $150 | $150 to $400 | Laboratory PACK HDRP $35 (v1.0, 2020) [V, [Asset Store](https://assetstore.unity.com/packages/3d/props/furniture/laboratory-pack-hdrp-181195)]; most lab packs are not HDRP; plan 3 to 5 packs [E] |
| Audio libraries | none | $15 to $150 | issue 5 [V] |
| Steam Direct fee | none | $100, recouped after $1,000 of revenue | [V, [Steamworks](https://partner.steamgames.com/doc/gettingstarted/appfee)] |
| Unity | none | $0 under $200,000 a year (Personal) | [V, [Unity](https://unity.com/products/unity-personal)] |
| US trademark filing (if filed) | none | $350 per class | [V, secondary: [Fish & Richardson](https://www.fr.com/insights/thought-leadership/blogs/new-uspto-trademark-fees-go-into-effect-january-18-2025/)] |
| Sound designer, IP lawyer | open | NOT VERIFIED | quotes needed |
| **Total** | $550 to $850 | **about $720 to $1,600**, plus a sound designer and a lawyer if used [E] | low: 294 + 100 + 60 + 150 + 15 + 100; high: 294 + 190 + 130 + 400 + 150 + 100 + 350 (one class) |

**Fix.** Replace 8.4's totals with this table. Keep the draft's rule of buying each zone's kit only in that zone's phase.

### 16. MEDIUM: optional content and systems that can wait (2.3 U1, 5.6, 5.7, 7.5, 5.4)

**Problem.** Several items cost a lot for little play:

- **Tunnel U1:** 969 m underground, with niches every 25 to 30 m, ventilation shafts, and parallel ducts every 100 to 150 m.
  It is a shortcut.
- **Optional items:** 8 of them, with 7 at separate sites.
- **Documents:** 70 to 100, in two languages.
- **Creature tiers:** three, where the "simulated" one adds a 2 to 4 Hz path-following layer.
- **Small flourishes:** an optional storm in act 3, retroreflective signs (a shader trick, NOT VERIFIED) and tunnel echo.

**Fix.** Savings are [E].

| Cut or defer | Saves (agent-days) |
|---|---|
| U1 tunnel → post-launch; replace it with one-way gates and the lit road after O7 | 3 to 6 |
| Optional sites S09, S10, S26, S27, S28 and S29 → cut. Keep S07 (manifest), S17 (species manual) and S14 (T. rex lore). The posts' map boards move into shelters. Optional play becomes about 10 to 20 min | 6 to 15 |
| Village 16 to 24 houses → 6 (issue 4) | 2 to 4 |
| Z3 folded and R8/S25 non-playable (issue 10) | 4 to 8 |
| SIS-4 deferred (issue 9) | 5 to 20 |
| Herds, sauropod and fights scripted; sheep cut (issue 11) | 4 to 8 |
| Creature tiers 3 → 2 (materialized within 150 m, abstract beyond); add "simulated" only if playtests show pop-in | 2 to 4 |
| Documents 70 to 100 → 40 to 60 (one every 4 to 5 min); storm, sign shader and tunnel echo deferred | 1 to 3 |
| **Total** | **about 25 to 70** |

The main path stays at 13 objectives and 3.1 to 4.3 h. With the optional content, play time is 3.3 to 4.6 h, still inside
the owner's 3 to 5 h.

### 17. LOW: the demo's first threat rides on the weakest purchase (4.4)

**Problem.** The DANKIE compy ($9.99) is version 1.0 from January 2019, shows "not enough ratings", and lists neither
animations nor a pipeline [V, [Asset Store](https://assetstore.unity.com/packages/3d/characters/animals/prehistoric-dinosaur-compsognathus-135639)].

**Fix.** Buy it in F0 together with the S10 models. If its animations fail at import, switch at once to the hiepvu
fallback ($27, 10 animations; NOT VERIFIED, as in the draft).

### 18. LOW: names will be public with the demo, before any trademark search (1.2, 4.9)

**Problem.** "Pellbrook", "Fallow Island" and "ATAVUS" appear in the demo's documents and on any store page, and the
trademark search is listed as not done.

**Fix.** Run the free database searches (USPTO, INPI, EUIPO) in F0, before the names go into any capture or document. File
only if the demo goes public, at $350 per class in the US (issue 15).

---

## The demo, revised (answers "is it the right slice?")

**Keep from the draft:**

- the place: Z1 on the existing forest, laid out in the final world, with real gates;
- the order: compys, then the Dilophosaurus as the climax;
- objectives O1 to O3 and shelter SR1;
- no sea and no caves.

**Change:**

| Item | Draft | Revised |
|---|---|---|
| Herd | animated herd crossing at night, with AI | audio only, plus at most a spline silhouette |
| Raptors | heard only | heard, then a 2 to 4 min avoidable beat at the Forest Gate after O3 (existing AI, bought body) |
| S10 creature probe | in F3 | in F0, with the first two models |
| F1 | the whole spine | F1a only; F1b moves to F4 |
| Checkpoints | autosave per objective | plus one before each staged encounter |
| Documents | 8 to 12, 2 or 3 as audio | 8 to 12, text only; the radio is static with subtitles |
| Exit metrics | length, peaks, soft-locks, frame time, captures | the same, plus deaths, minutes replayed, a Windows measurement if the demo ships on Steam, and the actual agent-days per item |

**Effort [E]:**

| Phase | Agent-days |
|---|---|
| F0, with S10 and the trademark search | 2 to 4 |
| F1a | 5 to 8 |
| F2 | 6 to 10 |
| F3: compy 2 to 5, Dilophosaurus 2 to 5, raptor body 1 to 3, director v1 2 to 4, torch and concealment 2 to 3, receiver 1 to 2, objectives, save and checkpoints 2 to 3, audio 3 to 5, UI 1 to 2, debug and logger 2 to 3, playtest rounds 3 to 5 | 21 to 40 |
| **Total** | **34 to 62**, or 1.5 to 3 calendar months |

---

## What to keep as drafted

- **Greybox the whole island (F4) before any zone art.** It is the cheapest place to prove pacing and the lock graph.
- **Buy each kit only in its own zone's phase**, and prefer CC0.
- **Run the probes before building** (E2, S1 to S9), paired in the player, as the forest learned to do.
- **Order species by what the player learns** (noise, then terrain, then territory, then light, then scent), with the
  most-measured biome (B1) carrying the demo.
- **Keep the legal table in 4.9 and the "raptors don't open doors" rule.** Both are cheap insurance.
- **Keep one night in four acts, with the clock moving per objective.** There is no time-out to tune.

## Unverified here

- All [E] effort figures. The demo's logged agent-days replace them.
- Sound-designer and lawyer fees.
- The compy fallback's quality.
- Whether a mid-range Windows PC holds the frame budget.
- The vocal pack's licence scope (two statements on one page).

## Sources

- [Sonniss GDC Game Audio Bundle: licence](https://sonniss.com/gameaudiogdc)
- [Magic Sound Effects: Dinosaur Vocalizations (itch.io)](https://magicsoundeffects.itch.io/dinosaur-vocalizations) and
  [the same pack on Construct](https://construct.net/game-assets/sounds/sound-effects/dinosaur-vocalizations-2517)
- [Steamworks: Steam Direct fee](https://partner.steamgames.com/doc/gettingstarted/appfee)
- [Steam Hardware & Software Survey, September 2026](https://store.steampowered.com/hwsurvey/)
- [Unity Personal: eligibility](https://unity.com/products/unity-personal) and [Unity pricing update](https://unity.com/pricing-updates)
- [Fish & Richardson: USPTO fees from 2025-01-18](https://www.fr.com/insights/thought-leadership/blogs/new-uspto-trademark-fees-go-into-effect-january-18-2025/)
- [Asset Store: Laboratory PACK HDRP](https://assetstore.unity.com/packages/3d/props/furniture/laboratory-pack-hdrp-181195)
- [Asset Store: Pxltiger T-Rex](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/dinosaur-t-rex-175294)
  ($60, version 1.3 of 2023-04-19, Built-in only)
- [Asset Store: DANKIE Compsognathus](https://assetstore.unity.com/packages/3d/characters/animals/prehistoric-dinosaur-compsognathus-135639)
- Project records: `Logs/forest-work/build/STATE.md` (dates and review rounds), `Logs/island-work/research/open-world.md`
  1.4 and 5.1 (unit rates), `Assets/Game/Scripts` (no `Animator` or `AudioSource`), and `Core/GameSession.cs` (reload on
  death).
