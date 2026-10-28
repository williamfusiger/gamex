# Review: horror and stealth design of `docs/design/ilha.md` (v1, 2026-10-21)

Reviewer role: horror and stealth game designer. Read: `docs/design/ilha.md` (all), `Logs/island-work/map-spec.json`
(territories, objectives, safe rooms, sites), `research/open-world.md` §3 and `research/species.md` §3 to §4, and the
prototype code the design builds on (`RaptorAI.cs`, `HidingSpot.cs`, `PlayerController.cs`, `GameSession.cs`,
`NoiseSystem.cs`, `Throwable.cs`, `PlayerFlashlight.cs`), plus `forest-work/build/issues/N1-night.md` for the
torch-off numbers. I did not edit the draft. Territory and objective overlaps below come from a point-in-polygon check on
`map-spec.json` (script in the session scratchpad, not in the project).

Marks: [M] measured or read from code in this project; [V] verified in an external source (linked); [E] my estimate,
with the arithmetic given; NOT VERIFIED where I could not confirm.

---

## Verdict

The foundation is right: an *Alien: Isolation*-style director that sends predators to the general area only, never spawns
in view, and unlocks behaviours by act rather than by deaths; territories instead of spawns; light as the tension knob; a
spread of safe rooms; and species roles that are mostly distinct. **As written, though, it will not be both frightening and
fair.**

- **Hiding is a guaranteed escape, and the director guarantees the predator leaves.** "Hide and wait about 90 s" beats
  every predator.
- **What happens on death is never defined.** A death can cost 25 to 35 minutes.
- **The torch is only on or off, and the draft says it must stay on under the canopy,** where most predators live. By the
  draft's own numbers the player is therefore always seen first. That breaks the pillar "see before you are seen".
- **Several counters depend on tells or player verbs that do not exist.** Missing tells: the Dilophosaurus has a tell
  with no correct answer, and the raptors' flanking and adapting have no tell. Missing verbs: kick, food, climbing,
  ladders, crawling, holding breath.
- **The middle of the game sags, and the end comes too fast.**
  - Act 2 is about half of the main game at a single director setting.
  - A player who does line C first meets no hunter there.
  - The apex can be met for the first time in the finale.
  - The finale's 3-minute wait can be spent inside a secure shelter.

The 2 critical and 8 high issues should be settled in the document before F3 (demo gameplay). Issues 1, 2, 3, 4, 5 and 25
shape the demo directly.

---

## Answers to the five questions

**1. Frightening and fair at night with a flashlight, in an open world?** Frightening: yes in intent. Fair: not yet. The
fear rests on three things: hearing a threat before it is near, the cost of using the light, and the predators being
almost unbeatable. Fairness needs four things the draft leaves open: a readable tell before every lethal hit; a hiding
place that can fail, and fails in a way the player can read; a death that costs minutes, not half an hour; and a light
setting between "blind" and "beacon". Issues 1 to 5.

**2. Does each species create a distinct fear and a distinct counter?**

| Species | Fear | Main counter | Distinct? | Gap (issue) |
|---|---|---|---|---|
| Parasaurolophus | Dread by proxy: the island screams | Stay out of its 25 m night sight; spook it on purpose to cover a dash | Yes | The two calls must be learnable (4); stampede deaths (19) |
| Compsognathus | A swarming nuisance that turns noise into danger; punishes camping | Keep moving, throw objects or food | Yes | Becomes an escort, not a fear (14); verbs missing (15) |
| Dilophosaurus | Ambush in thickets | Read signs, go around, probe bushes | On paper | The light repels it, its tell has no answer, the receiver finds it (5) |
| Triceratops | A territorial, avoidable hazard | No beam in its face, keep away from the calf, break line of sight | Partly: another "don't light it" rule | (19), (26) |
| Deinonychus | Being hunted by something clever | Light discipline, hiding, splitting the pack with throws | Yes | Flank and adapt tells (4); hiding absolute (1); doors (9) |
| Brachiosaurus | Awe | Noise mask (x0.5 within 25 m) | Yes, as a tool | Deaths under the feet (19) |
| T. rex | Inevitability; being scented | Wind, scent breakers, gaps | Yes | Taught too late (7); fixed wind (23); can be outrun for about 40 s (24) |

**3. Is the progression from first threat to apex right?** The *ladder* is right: compys, then Dilophosaurus, then
Triceratops, then raptors, then T. rex. The Compsognathus is the right first threat, and I agree with the coordinator and
the draft here. The *open order breaks the ladder in two places.* First, nothing guarantees that the apex is taught before
act 4 (issue 7). Second, line C before O7 has no hunter at all (issue 8). Escalation is also back-loaded (issue 6).

**4. Do the AI director, the hiding spots and the danger signals work together?** Not yet.

- **Director and hiding.** Together they make waiting dominant (issue 1).
- **Director and two species.** The director's "send" and "withdraw" jobs cannot apply to the Dilophosaurus or the
  Triceratops (issue 11).
- **Director cap and compys.** The cap of 80 m contradicts the compy-to-raptor chain (issue 12).
- **Signals.** The danger signals have a hole between 15 and 50 m for raptors, and nothing yet exists to fill it: there
  is no audio (issue 4).

**5. Where will players get bored, frustrated or feel cheated?**

- **Bored:**
  - act 2 (issue 6);
  - line C done first (issue 8);
  - long walks through ground already cleared (issue 21);
  - being lost in 30 m fog with no map (issue 20);
  - a compy escort (issue 14).
- **Frustrated:**
  - replaying after a death (issue 2);
  - a save refused because compys followed the player to the door (issue 2);
  - a predator waiting at a shelter door (issue 17);
  - the finale (issue 10).
- **Cheated:**
  - flank kills from outside the view (issue 4);
  - a Dilophosaurus lunge in the dark (issue 5);
  - stampede and sauropod deaths (issue 19);
  - "the third distraction is ignored" with no tell (issue 4).

---

## Issues (most severe first)

### 1. CRITICAL: hiding is absolute and the director guarantees withdrawal, so "hide and wait" wins (§6.1, §6.4 LV-2, §6.8)

**Problem.**

- **Hiding is absolute in the code.** `RaptorAI.CheckSight` returns only `witnessedHiding` for a hidden player [M]
  (`RaptorAI.cs` lines 179 and 180). That flag is true only if the raptor was in Chase and saw the player within 0.5 s of
  getting in (line 141, `WitnessWindow` on line 70). No predator ever checks a hiding spot.
- **Spots are everywhere.** LV-2 puts a spot within 16 m of hard cover, which is itself within 8 m of every route point,
  across about 80 ha of raptor territory (DEIN-A 27.0, A3 28.3, B 17.7, B3 6.8 ha).
- **The predator always leaves.** The director's peak lasts at most P (60 to 90 s), and then the predator withdraws to
  150 m or more.
- **The one counter is rare.** Only compys can expel a hidden player, and their four territories total 20.6 ha. None of
  them is in Z4, Z3 or Z7, where pack B and the Dilophosaurus live.

The dominant strategy is therefore: break line of sight for 0.5 s, enter any spot, and wait 90 s at most. *Alien:
Isolation* used the same director, but its alien could reach into a locker: a pre-release preview describes
breath-control prompts while the alien sniffs at the locker
([Shacknews](https://shacknews.com/article/83607/alien-isolation-preview-extra-terrifying)) [V]. Players also report
that it checks lockers more often when the player relies on them
([Steam forum](https://steamcommunity.com/app/214490/discussions/0/3109144584165201158), community observation, NOT
VERIFIED as the real rule).

**Fix.** Add a "spot check" to the predator AI, gated by a reason and by act.

- **When a check can happen.** Only in Search or Investigate, and only when the search point is within 4 m of a spot's
  `ExitPoint`, and only with a reason: a noise within 4 m of the spot in the last 10 s; the player's scent within 10 m
  downwind (raptors); or the player seen within 20 m in the last 20 s.
- **Chance of a check, by act [D].** 0 in act 1, 0.25 in act 2, 0.5 in act 3, 0.6 in act 4. A spot used twice within 10
  minutes in the same territory is checked first.
- **Tell.** 2 to 3 s of audible sniffing and approach, visible through the slats or gap of the spot.
- **Counter.** A hold-breath input that lasts at most 6 s and ends in a gasp (a 6 m noise), and no movement. Failing it
  means death (raptor, T. rex) or being dragged out (compys).
- **Scent.** A hidden player keeps emitting scent. This is what makes wind matter for raptors and compys too.

Add the behaviour to the new-AI list in §4.2 and §9 decision 6.

### 2. CRITICAL: the failure loop is undefined (§4.4, §5.5, §7.7; `GameSession.cs`)

**Problem.**

- **Death.** It reloads the whole scene today (`GameSession.KillPlayer` → `End`, restart on input, lines 34, 35 and 38)
  [M]. The draft never says what death costs.
- **Health.** §4.4 gives compys damage as a percentage of health ("1 a 2 % da vida", "3 a 5 % por segundo"), but there
  is no health system, and the no-HUD policy gives no way to read one.
- **Bleeding.** §4.8 doubles the T. rex's smell "com sangue", but nothing makes the player bleed.
- **Saving.** Saves happen only at shelter terminals (3 to 5 s), plus an autosave per main objective, and an objective
  takes 15 to 35 minutes.
- **Worst case.** O7 takes 25 to 35 minutes inside DEIN-B, and its shelter SR4 opens only *after* O7. A death at the third
  breaker can cost the whole objective plus the walk back. *Alien: Isolation's* manual saves were criticised for losing
  "up to fifteen minutes of progress" and for leaving the player "vulnerable for a couple seconds" while saving
  ([VGChartz review](https://www.vgchartz.com/game/77802/alien-isolation/reviews/254064/)) [V].

**Fix.** Add a "Morte e ferimento" subsection.

- **A death matrix.**
  - One hit kills: raptor, T. rex, a direct Triceratops hit, and a Dilophosaurus lunge after the first (issue 5).
  - Injury only: compys, a graze from a stampede, a fall, sauropod feet.
- **Injury without a HUD.**
  - Healthy → **Bleeding**: 90 s, with blood drops on the ground, T. rex smell x2, compys drawn from 30 m, and heavy
    breathing plus a bloody hand as the cues.
  - Bleeding → **Downed** if hit again: compys swarm, and the player has 5 s to kick free.
  - A bandage at any shelter clears it.
- **Checkpoints.**
  - Autosave on every sub-step: each breaker of O7, each keycard, each code document, entering a new zone, and leaving a
    secure shelter.
  - Target a median of 8 minutes or less of lost replay per death, and 12 minutes or less in the worst case. Log it in
    the demo playtests.
  - Keep manual saves at shelters for the feel.
- **Saving.**
  - Save is never refused because of compys, herbivores or Investigate.
  - It is refused only during a Chase within 40 m. Once the door is secured nothing can enter, so a refusal there only
    frustrates.
- **On reload,** start the director in Relax for R seconds, so the player does not reload straight into the same chase.

### 3. HIGH: the torch is binary and forced on under the canopy, so the player is always seen first (§6.6, §1.3)

**Problem.**

- **The pillar.** §1.3 and §6.5 say "veja antes de ser visto".
- **Detection ranges.**
  - With the torch on: a raptor sees the beam at 80 m (§4.2) or its glow through foliage at 40 m, and sees the lamp head-on
    at 60 m (T1).
  - The player sees eyeshine at about 50 m, and only if the beam crosses the eyes. The fog's mean free path is 30 m.
  - With the torch off: the frame mean is 0.03 to 0.08, with "trail ruts, ferns and a boulder barely readable for the
    first metres" (N1) [M]. A raptor still sees a standing player at 14 m and a crouched one at 8.4 m (T3).
- **The draft's own rule.** §6.6 says the torch must stay on under the canopy (B1, B2). B1 and B2 hold DILO-01 to 03,
  DEIN-B3 and most of DEIN-A3. So in the zones where most predators live, the choice "light or safety" disappears: the
  player is seen first in both cases.
- **Two inconsistencies.**
  - T1 says 60 m and table 4.2 says 80 m.
  - Eyeshine at 50 m is beyond `PlayerFlashlight.range` = 35 m (`PlayerFlashlight.cs` line 32) [M]. It only works as the
    faked emissive of §7.6. Say so.

**Fix.** Make the light three-state.

| State | Light | Readable to | A raptor sees the beam at | Glow through foliage | Eyeshine |
|---|---|---|---|---|---|
| High | 300 lm, range 35 m | ~30 m | 60 m | 40 m | ~50 m |
| Low (hand over the lens, or a red filter) | ~30 lm | 8 to 10 m | 20 m | none | 12 m |
| Off | — | the first metres | — | — | — |

- Switching between High and Low has no exposure dip; only Off pays the measured 2 to 3 s.
- Low becomes the habit in the forest.
- Write one beam-visibility table per species and state, and drop the duplicate T1 value.
- **Batteries.** The draft gives shelters a battery stock but no battery rule. State that the torch never drains; the
  cost of the light is exposure. Batteries power the receiver and the shelters. If the torch drained, players would walk
  in the dark to save charge, and that would be boring.

### 4. HIGH: the danger signals have no tell matrix, and flanking and adapting have no tells (§6.5, §4.7)

**Problem.**

- **The 15 to 50 m band.** For raptors it has only two cues: the "silence bubble", which the draft itself calls a
  convention with weak evidence, and eyeshine, which needs the light on and aimed.
- **Audio.** "Hear before you see" rests on audio that does not exist yet (decision 5).
- **Flanking.** "Um se mostra no facho e dois fecham a 90 graus", combined with a one-hit kill at 1.8 m (`attackRange`,
  `RaptorAI.cs` line 59) [M], means deaths from outside the view with no warning. It also stages the side attack that §4.7
  lists under "Evitar".
- **Adapting.** "Na terceira é ignorada" has no tell, so the player only learns that throwing stopped working.
- **The two herbivore calls.** "Predador" and "desconhecido" must be told apart by ear, and nothing in the demo teaches
  them.

**Fix.**

- **A tell matrix per species and state** (Patrol, Suspicious, Hunting, Attack wind-up), each with its audible range.
  Raptor values [D]:
  - Patrol: claw ticks and breath clicks, audible at 25 to 30 m;
  - Suspicious: a low chirp at 40 m;
  - the scout call: 120 m;
  - an attack wind-up of at least 1.0 s.
- **A fairness rule.** No kill by an attacker outside the player's 90-degree view unless that attacker made a spatialised
  tell in the last 2 s, and at least 1.0 s before contact. Flankers vocalise when they start to close.
- **An adapting tell.**
  - Second use: the raptor checks the landing spot for up to 3 s, then looks back along the throw line.
  - Third use (within 10 m of the same spot and within 5 minutes): it turns and investigates where the throw came from.
- **Eye height and spacing name the species,** for free with the emissive eye: compy about 0.25 m, raptor about 1 m,
  Dilophosaurus about 1.7 m, Triceratops low and wide, T. rex about 3.5 to 4 m.
- **The two calls.** Make them differ in rhythm (for example, one long call versus three short ones), not only in pitch.
  Stage them once in the demo.
- **A test.** Before signing off F3, run a blind audio test: testers name the species and state by ear 80 % of the time
  or more.

### 5. HIGH: the Dilophosaurus's counter-play is undefined and partly defeats itself (§4.5, §8.1)

**Problem.**

1. **The light repels it.** Beyond 12 m the beam makes it retreat, so sweeping with the torch keeps the demo's climax
   away.
2. **The tell has no answer.** Inside 12 m it displays for 1.5 s and then lunges at 10 m/s from 8 m or less (0.8 s), but
   the draft never says what the right response to the display is.
3. **No tell in the dark.** With the torch off there is no display, so the first warning is a lunge from 8 m or less in
   near-black.
4. **The receiver finds it.** DL-03 wears a collar. The receiver arrives at minutes 8 to 12, and "volume and rhythm give
   direction and distance", so the ambusher is located before it can ambush.
5. **Lethality.** Whether the lunge kills is not stated.
6. **The chase is irrelevant.** It runs at 6.5 m/s for 10 s against an unlimited 6 m/s sprint (`PlayerController.cs` line
   23) [M], so it gains 5 m.

**Fix.**

- **Beyond 12 m,** the beam makes it freeze, then reposition silently to the next ambush node ahead on the player's path.
  It never retreats.
- **Inside 12 m, the display is an answerable tell.** Cut the beam or turn it away, *and* step 2 m or more sideways or
  behind hard cover within 1.5 s: the lunge goes to the last lit point and misses. Keep the beam on it and the lunge lands.
- **An unlit tell:** a dry hiss audible at 10 m, 1.0 s before the lunge.
- **The first lunge of the game knocks the player down** (Bleeding, issue 2) **and does not kill.** After that, lunges
  kill.
- **DL-03's collar is torn off and lies at the shed,** beeping the mortality rhythm. The receiver leads to the collar, not
  the animal. This is the "twist" of §1.6 rule 5, and the ambush survives.

### 6. HIGH: escalation stalls through act 2 (§5.4)

**Problem.** Act 3 begins only when 2 of the 3 lines are done.

- **Act 2 length.** Act 2 = O4 + O5 + two lines, which is 90 min (30 + C 25 + B 35) to 150 min (40 + A 60 + B 50) [E,
  from the table in §5.3].
- **Share.** That is about 48 to 58 % of the 188 to 260 main minutes, all at one Q, P and R setting, with raptors in pairs.
- **Back-loading.** The third line always falls in act 3, so the climb is concentrated at the end.

**Fix.** Step the intensity per completed line, not per act.

| Step | Starts after | Q | Change |
|---|---|---|---|
| 2a | O3 | 240 to 300 s | — |
| 2b | the first line | 180 to 240 s | Trios without flanking |
| 3 | the second line | 90 to 180 s | Flanking and spot checks |
| 4 | O12 | as now | — |

Unlock one new behaviour per step (spot checks, flanking, adapting).

**Measure in F4.** No more than 12 minutes between director peaks, and no step longer than 70 minutes.

### 7. HIGH: the apex can be taught too late, or only in the finale (§4.8, §5.3, §1.6 rule 8)

**Problem.**

- **No main objective enters T-REX-1.** O12 sits 60 m outside the basin (map-spec), and S14, T6a and T6b are optional.
- **The first meeting can be the finale.** T-REX-2 opens only with O7, so a player who does C, then B, then A can meet
  the T. rex for the first time in act 3 on the plateau, or in the act 4 finale, where Q is 60 s.
- **Rule 8 does not hold.** It says "O T. rex só é entendido de verdade no núcleo", but the core is optional. The lesson
  the draft orders last, wind and scent, is then learned under maximum pressure, with a one-hit kill.

**Fix.** Add a staged, low-risk T. rex lesson on the mandatory approach to O12, in every order.

- **Where:** R7 along the West Pass rim.
- **The setup:**
  - the player and the T. rex are 10 to 20 m apart, with a live fence (after O7) or a rim drop between them;
  - a fumarole plume shows the wind;
  - the T. rex follows the player's scent along the fence, loses it at a mud wallow or fumarole, and turns away.
- **What it teaches:** scent, wind and scent breakers, safely.
- **Plus issue 8's rule:** T-REX-2 opens at O7 *or* at the start of act 3, whichever comes first.

### 8. HIGH: line C before O7 has no hunter, and it is the strictly easier order (§2.3, §5.3, map-spec)

**Problem.**

- **No hunter on line C.** O10 (480, -40) lies only inside T-REX-2, which needs O7. O11 (522, 334) lies inside T-REX-2,
  T-REX-3 and BRACH. DEIN-A, which covers acts 2 to 4, ends at z = -150. So line C in act 2 means 25 to 35 minutes with
  only the Triceratops, Parasaurolophus, Brachiosaurus and compys, in 80 to 150 m air.
- **Order dominance.** Doing C first is strictly easier. Players who know this always do it, and players who do A first
  meet the T. rex plus full packs there.

**Fix.**

- Extend DEIN-A in act 2 to S19 and the East Road as far as S20, with a pair on patrol.
- In act 2, make the T. rex heard and smelled at the broken East Pass: it comes to the gap and huffs, but does not come out.
- Open T-REX-2 at O7 or at act 3, as in issue 7.

### 9. HIGH: the size classes make every single-door interior raptor-proof (§6.1, §6.3)

**Problem.**

- **The rule.** M agent: radius 0.5 m; "uma porta simples de 0,9 m o barra, aberta ou fechada".
- **Interiors that need raptor access.** The village has 16 to 24 prefab houses, where "os raptors procuram cômodo por
  cômodo". The labs have "bando A por dentro". The security office, clinic and other buildings are the same case. All of
  them need openings of 1.2 m or more, and the draft never says which ones have them.
- **The result.** As drawn, every single-door room is a safe room without saving. Together with LV-5 (two exits per
  room), a player can chain houses through the village.

**Fix.**

- **Keep "a narrow door is safe from raptors, not from compys" as a deliberate, learnable rule.** It is a good rule.
- **Write a raptor-access table per building:**
  - double doors, roll-up doors and smashed walls of 1.2 m or more;
  - at least 60 % of village houses with a wide breach;
  - a lab ring corridor with double doors.
- **A visual language for the openings:** claw-scored frames, doors torn off and smears mean raptors can come through; an
  intact narrow door means they cannot.
- **A raptor outside a narrow door** waits there, and the director's withdrawal ends the wait.

### 10. HIGH: the finale can be waited out in a secure shelter, or is a coin toss (§5.3 O13, §5.5 SR6)

**Problem.** O13 is "espera de uns 3 min". SR6, the hangar office, is a secure shelter and "ponto de espera do ato 4":
no animal enters it, and the director never targets a player inside. The climax can be spent in a closet. If the player
waits outside instead, the scene becomes a coin toss:

- the strip is open (450 x 30 m);
- the T. rex sees at 100 m or more at dawn, and "ficar parado não esconde ninguém";
- the generator is a low-frequency noise it hears at 200 m.

**Fix.** Define the beat.

- **SR6 is the act 4 save,** but it stops being secure once the strip generator runs. The generator is outside.
- **The generator stalls** at about 60 s and about 120 s, and each restart is an exposed run of about 15 m.
- **The helicopter lands only after a flare from the pad,** which ends in a 40 m dash.
- **The fuel park explosion (S24)** is the one big tool for pulling the T. rex away.
- **Culverts.** The culverts under the strip bar M, G and GG, but COMPY-4 covers the strip and compys enter them. Either
  remove compys in act 4, or make them flee the generator.

### 11. MEDIUM: the director's "send" and "withdraw" jobs do not fit the ambusher or the territorial animals (§6.8)

**Problem.**

- **Send.** In Quiet, the director sends "a criatura apropriada mais próxima" to a point 40 to 60 m from the player.
- **Withdraw.** At Peak, it withdraws the creature to 150 m or more.
- **The territories are too small.**
  - DILO territories of 1.66 to 2.04 ha have an equivalent radius of 73 to 81 m.
  - TRI territories of 0.38 to 0.5 ha have an equivalent radius of 35 to 40 m [E].
  - A 150 m withdrawal therefore breaks rule 2 (stay in territory).
- **Predictability.** Sending the Dilophosaurus breaks the predictability that §4.1 point 4 gives as the reason it is
  level 2.

**Fix.** Give each species a director role.

- **Hunter (raptors, T. rex).** The director may send and withdraw them, as written.
- **Static hazard (Dilophosaurus, Triceratops, herds).** Never sent. The director only raises or lowers their alertness.
  At Peak they go to their den or feeding node for R seconds.
- **Pressure (compys).** May be sent anywhere in a COMPY territory. They do not count toward Peak unless a hunter is within
  80 m.

### 12. MEDIUM: the 80 m cap contradicts the compy-to-raptor chain (§6.8 rule 3, §6.1)

**Problem.** The rule is "a até 80 m, no máximo 1 predador grande e 1 bando", and it never says whether a raptor pack is
the large predator or the pack. The chain needs both:

- the squeal reaches 25 m, and raptor hearing is x1.5, so a raptor must be within about 38 m of the compys;
- the compys follow the player at 6 to 15 m;
- so both a raptor pack and a compy pack must be within about 50 m of the player.

If the raptor pack counts as "the pack", the cap forbids the draft's main systemic link.

**Fix.** Name the slots.

- **Hunter slot:** one raptor pack of up to 3, *or* one Dilophosaurus, *or* the T. rex.
- **Pressure slot:** one compy pack.
- **Herbivores:** no cap.

Three raptors plus six compys is then the designed worst case. It already matches the heavy encounter in §7.6.

### 13. MEDIUM: which animals wear collars, and how the receiver gives direction, are undefined (§6.5, §1.6 rule 8)

**Problem.**

- **The unknown.** It depends on which animals are "plantel original", and there is no list.
- **If the raptors are collared,** the receiver is a motion tracker for the main antagonist. If none are, it is useless
  against them.
- **Direction.** "Volume e ritmo dão direção e distância" is vague. Real wildlife tracking uses a hand-held directional
  Yagi antenna ([Perdix, VHF Yagi for wildlife telemetry](https://perdixwildlifesupplies.com/products/perdix-vhf-yagi-antenna))
  [V]. Bearing is found by sweeping it for the peak signal (general practice,
  [informal forum account](https://forums.radioreference.com/threads/directional-tracking.247573/), NOT VERIFIED in a
  field guide).

**Fix.**

- **A collar table:**
  - DL-01 and DL-02 collared; DL-03's collar torn off (issue 5);
  - Triceratops, Parasaurolophus and Brachiosaurus collared, as information;
  - raptor pack A: 2 of 3 collared, and the third born free;
  - pack B: none (born in the lava tube);
  - the T. rex's collar beeps "mortality" from S14 (torn off): a story beat, and a lie.
- **The receiver:**
  - the omni whip antenna gives distance by rhythm only;
  - a Yagi found at S19 (a line C reward) adds direction by a 2 to 3 s sweep;
  - while sweeping, the torch is limited to Low, and the speaker is a 3 m noise.

### 14. MEDIUM: compys become an escort, not a fear (§4.4)

**Problem.**

- **Escort.** They are the only species that crosses zones (20.6 ha of territory), they follow the player at 6 to 15 m,
  and they squeal when the beam is within 5 m.
- **Fear turns to irritation.** A pack that tags along for minutes stops being frightening and only annoys.
- **Every squeal** pulls hunters toward the player, so the irritation also feeds frustration.

**Fix.**

- **Interest timer.**
  - A pack follows for 60 to 90 s at most, unless the player is Bleeding or carries food or the fuel can.
  - It loses interest if the player stays still out of the light for 10 s.
- **Satiation.** After feeding at a carcass, a pack is sated for 10 minutes.
- **One pack at a time.** Only one pack follows the player.
- **The demo's carcass beat** (minutes 22 to 26) places a throwable within 5 m of the path, so it teaches the throw.

### 15. MEDIUM: the counters depend on player verbs that do not exist (§4.4, §6.1 to §6.3, §2.4, §5.3)

**Problem.** `PlayerController` has walk, crouch and sprint only: no jump, mantle, prone or ladder [M]. The crouch
capsule is 1.1 m. The draft relies on these missing verbs:

- kicking a compy;
- throwing food;
- climbing rocks or vehicles;
- ladders on the towers (V1 15 m, V3 and V4 18 m, V5 30 m);
- carrying the fuel can with both hands;
- holding breath (issue 1);
- tuning the receiver;
- a bandage.

`HidingSpot` teleports the player, so spots under containers on 0.6 to 0.9 m stilts work. But *passages* do not: the
0.9 to 1.2 m culverts, the fallen-log tunnel and the narrow fissures need a crawl.

**Fix.** Add a "Verbos do jogador" section. Give each verb a spec and the species it counters [D]:

| Verb | Spec |
|---|---|
| Crawl | Capsule 0.6 m |
| Mantle | 0.6 to 1.4 m |
| Ladder | An exposed climb of 3 to 5 s, plus ladder noise |
| Kick | 1 m reach, scatters 1 or 2 compys, 6 m noise |
| Food | Thrown; compys eat it for 15 to 20 s |
| Carry | Both hands: no throwing, torch on Low only; 6 m slosh noise when walking, 12 m when sprinting |

Then budget the verbs in F3.

### 16. MEDIUM: "observe before entering" (LV-3) is impossible in fog at night unless the site is lit (§6.4)

**Problem.**

- **The rule.** LV-3 asks for every guarded objective area to be watchable from 30 to 60 m.
- **The fog.** By the draft's own fog maths in §2.4, about 13.5 % of contrast is left at 60 m (e^-2) [E], in the dark.
- **The torch off.** It shows only the first metres (N1) [M].
- **The result.** The observe-and-plan phase of stealth cannot be visual.

**Fix.**

- **Light pools.** Every objective site gets 2 to 4 lamps without shadows, which fits the "at most 2 shadowed lamps"
  budget. Place them on predator patrol paths, with lit windows as backlights for silhouettes.
- **Observation points** 25 to 35 m from the pools, behind hard cover.
- **Audio.** Footsteps on metal grating (x1.8) and the receiver also count as observation.
- **Test.** Check it in probe S9.

### 17. MEDIUM: shelter exits can be camped, and securing a shelter makes noise (§5.5)

**Problem.**

- **Every shelter is inside a territory.** All seven shelters after SR1 lie inside a predator territory (map-spec):
  - SR2 and SR3: DEIN-A3;
  - SR4: DEIN-B;
  - SR5: DEIN-A;
  - SR6: COMPY-4;
  - SR7: T-REX-3;
  - SR8: T-REX-2 and 3.
- **The director only protects the inside.** It never targets a player inside, but nothing stops a raptor in Search from
  waiting outside.
- **Securing draws attention.** Starting a generator is a 40 m noise, and a low-frequency one the T. rex hears at 200 m.
  The exit can mean death without warning.

**Fix.**

- **A viewport.** Each shelter gets a viewport, periscope or camera monitor covering the exit to 20 to 30 m (LV-7).
- **Clear the door.** After the player has been inside for 30 s, the director moves any hunter within 60 m to 100 m or
  more.
- **Quiet securing.** Shelters are secured with a battery, which is silent. Only SR1 (the tutorial) and SR6 use a
  generator.

### 18. MEDIUM: the scripted breach of SR5 may never fire, and must obey the draft's own rules (§5.5 rule 5)

**Problem.**

- **It may never fire.** In open order, line B can be finished in act 2 and SR5 never entered again in act 3.
- **It may break the rules.** A breach must respect "raptors não abrem portas" and the 1.2 m opening of the M class.

**Fix.**

- **Trigger.** The first entry in act 3 or later into SR5, or into SR3 (the hub) if the player never returns. Author the
  breach geometry in both.
- **Telegraph.**
  - Never trigger on a save load.
  - Disable the save terminal 60 s before it.
  - The tell: scratching, then a loose 1.2 m vent panel.
- **An escape** through a second door into the lab ring.

### 19. MEDIUM: deaths from non-hunters will feel random (§4.3, §4.6, §4.8)

**Problem.**

- **Undefined deaths.** A stampede ("com 2,6 t o impacto mata"), sauropod feet or tail within 8 m, and the Triceratops
  charge have no defined tell or path.
- **An expensive system.** "Pode lutar com raptors" needs creature-against-creature combat, and that is not in the
  new-AI list.

**Fix.**

- **Stampedes** run along authored flight lanes away from what triggered them, after a 2 to 3 s ground rumble and the
  alarm call. A player crouched behind hard cover of 1 m or more is passed around (avoidance radius 2 m).
- **Sauropods** knock the player down; they do not kill.
- **The Triceratops** gives a 1.5 s tell (head lowered, foot scrape) before charging. The charge kills only on a direct
  hit and ends at 40 m.
- **The cheap version of "Triceratops fights raptors":** raptors break off a chase when a Triceratops charges within 15 m.
  No fight animations are needed.

### 20. MEDIUM: getting lost in an open world with 30 m fog and no map (§2.4)

**Problem.** There is no full map, the paper map is optional, the fog's mean free path is 30 m, and the moon is not
visible under the canopy. In the open world, getting lost turns into boredom, not fear (`open-world.md` §3.1, point 6).

**Fix.**

- **The paper map is given at O1** (the Field Station). It fills in with visited places and marks the sites learned from
  documents. The radio at O3 marks the three lines roughly.
- **A compass early,** from the fisherman's own kit.
- **Measure in F4.** Time spent more than 60 s without progress toward any objective is 10 % of a session or less.

### 21. MEDIUM: travel between sites is most of the post-demo game, with no content specified for the routes (§1.4, §5.3, §2.2)

**Problem.**

- **Distance.** The main chain, from S02 through all lines to S23, is about 4.8 to 5.1 km in straight lines [E, from the
  site points]. At an assumed route factor of 1.3 that is 6.3 to 6.6 km.
- **Time.** At 0.8 to 1.0 m/s that is 105 to 138 minutes, about 55 to 75 % of the roughly 160 to 235 main minutes after
  the demo.
- **Counted as play.** The draft counts this time as play, through the 0.6 to 1.0 m/s stealth speed, but the routes have
  no content beyond the director's pool of ambient events. The hub (Z2) is crossed 3 or 4 times.

**Fix.**

- **Route beats.** Author one vignette per 150 m of main route, about 40 to 45 in all: a carcass, a document, a light
  flicker, a herd crossing, a broken fence.
- **An earlier shortcut.** Open part of the service tunnel with keycard 1 (O5), so the trips between the hub, the labs
  and the plant are shorter.
- **Measure in F4.** Each segment is walked 2.0 times or fewer.

### 22. MEDIUM: the map spec contradicts the text where threat is concerned (§2.3, §5.3, §5.4; map-spec)

**Problem.** The generator and the director will read the JSON.

- **Pack A in the village.** DEIN-A (acts 2 to 4) spans x 170 to 690 and does not reach the village: O4 is at x = -335,
  O5 at x = -198, and SR2 is in DEIN-A3 only (act 3 and later). Table 2.3 and the O4 and O5 rows, however, say pack A
  hunts the village from act 2.
- **T-REX-2.** It has `acts: [3, 4]`, while §5.4 says it opens at O7 in any act.

**Fix.**

- Decide whether pairs hunt the village in act 2. If they do, add an act-2 village polygon.
- Make T-REX-2 open on an event (O7, or act 3 as in issues 7 and 8), not on an act list.
- Add an audit test that checks the text tables against the JSON.

### 23. MEDIUM: the T. rex's scent is a fixed rule and has no tell (§4.8, §2.1)

**Problem.**

- **The wind never changes.** It is fixed from 250 degrees (map-spec meta), so "wind and scent" becomes "keep it to your
  west".
- **The T. rex smells far beyond what the player can sense.**
  - Scent reaches 150 m, while the player perceives about 30 m and feels its footfalls at 100 m.
  - That leaves a 50 m band where the T. rex has the player's scent and the player has no cue at all.
- **Scent breakers.** The draft names streams, but the T. rex territories (the savanna and the plateau) have few streams,
  and there is no water gameplay.

**Fix.**

- **The wind changes per act:** the act 3 storm veers it to come from about 200 degrees, and the act 4 dawn brings a land
  breeze from the east.
- **Telltales that show the wind:** fumarole plumes, a windsock at the strip, particles in the beam.
- **A scent-acquisition tell:** a long, low huff audible at 150 to 200 m when the T. rex locks onto the player's scent.
- **Scent breakers** every 80 m or less along routes in T. rex territory: mud wallows, fumaroles, fuel drums, carcasses.

### 24. MEDIUM: the apex can be outrun, because only raptors have chase rules and the sprint is unlimited (§4.8, §6.4)

**Problem.**

- **Sprint.** It is 6 m/s with no stamina [M].
- **The T. rex chase** is 7 m/s after 4 s of acceleration [E]:
  - from detection at 30 m, the T. rex covers about 14 m while the player covers 24 m, so the gap grows to about 40 m;
  - it then closes at 1 m/s, so contact comes after about 41 s and about 240 m of sprinting.
- **Only raptors have rules.** LV-1 and LV-2 (refuge spacing) apply to raptors only.

**Fix.**

- **A "time to catch" table per species,** each with a refuge-spacing rule.
- **T. rex [D]:**
  - 2 s of acceleration to 8 m/s in the open, and turns of 45 degrees per second or slower;
  - a refuge that bars GG within 60 to 80 m of every route point in its territory;
  - that gives 12 to 15 s of terror, which can be escaped by reading the terrain.
- **Keep sprint unlimited,** because it is simpler. Tune the predators' speeds instead.

### 25. MEDIUM: the demo's arc peaks too early and ends on the weakest threat (§8.1)

**Problem.**

- **Order.** The Dilophosaurus climax falls at minutes 12 to 22, and minutes 22 to 30 belong to compys, the weakest
  species.
- **The main antagonist** is only a sound.
- **Judged on the wrong game.** A demo player may judge the game as a puzzle about a monster in a ravine.

**Fix.**

- **The shed (O2).** The Dilophosaurus first shows itself here: it displays and repositions.
- **The return trip is the climax** (minutes 20 to 26). The fuel can sloshes, so the player cannot sprint quietly while
  the Dilophosaurus stalks the way back. The distance from S03 to S02 is 238 m.
- **The generator scene with compys** (minutes 26 to 30) becomes the falling action.
- **A hook to end on.** After saving, the player sees two raptors cross the SR1 floodlight through the window, with
  eyeshine at about 1 m. There is no chase. This fits the "raptors only heard" default of decision 3.

### 26. LOW: the reactions to light blur together, and the T. rex roars too often (§4.2, §6.6, §4.1)

**Problem.**

- **Too many thresholds.** The player cannot see these distances:
  - compys: drawn beyond 10 m, scatter within 5 m;
  - Dilophosaurus: 12 m;
  - Triceratops: 20 m;
  - Parasaurolophus: 60 m;
  - raptors: 80 m.
- **Constant roars.** T. rex "rugidos desde a demo" for 3 to 5 hours wear the fear out.

**Fix.**

- **Two rules the player can learn:**
  - "hunters come to light": raptors, and compys beyond 10 m;
  - "anything else hates light in its face within about 15 m": Dilophosaurus, Triceratops, Parasaurolophus.
  - Visible eyeshine means "you are within its range".
- **Ration the roar:** one T. rex roar per 20 minutes at most, each tied to a visible consequence (a herd alarm, birds
  flushing, a fence going silent).

---

## What to keep as it is

These parts are sound, and the fixes above build on them.

- **The director.** It sends predators to the general area only, never spawns or teleports them in view, unlocks
  behaviour by act and never by deaths, and caps the threats near the player. This copies what made *Alien: Isolation*
  fair. Its developers say the alien never cheats on the player's position, and that designers can mark areas it never
  searches, so it does not catch players unfairly
  ([Game Developer, "The Perfect Organism"](https://www.gamedeveloper.com/design/the-perfect-organism-the-ai-of-alien-isolation))
  [V].
- **Space.** Territories that widen by act; a shelter at most 382 m from any main route; landmarks colour-coded by light.
- **Information species.** The Parasaurolophus as an alarm that informs both sides, and compys as the light, systemic
  link to the heavy threats.
- **The torch as a lure** (T2): predators go to the lit spot, not to the player.
- **The unknown:** animals born free carry no collar.
- **The cast and its order.** The Compsognathus as the first threat, then the Dilophosaurus as the first killer, the
  raptors as the main antagonist and the T. rex as the apex. The Gallimimus is rightly deferred.

---

## Sources

- Project: `docs/design/ilha.md`; `Logs/island-work/map-spec.json`; `Logs/island-work/research/open-world.md` §3 and
  `species.md` §3 and §4; `Logs/forest-work/build/issues/N1-night.md` (torch-off frame means, exposure);
  `Assets/Game/Scripts/Dinosaurs/RaptorAI.cs`, `World/HidingSpot.cs`, `Player/PlayerController.cs`,
  `Player/PlayerFlashlight.cs`, `Core/GameSession.cs`.
- [Game Developer: The Perfect Organism, the AI of Alien: Isolation](https://www.gamedeveloper.com/design/the-perfect-organism-the-ai-of-alien-isolation):
  the menace gauge, backstage, unlocks never from deaths, no cheating, designer-marked no-search areas.
- [Shacknews: Alien: Isolation preview](https://shacknews.com/article/83607/alien-isolation-preview-extra-terrifying):
  breath prompts in lockers; the alien reaches in if they fail (pre-release impression).
- [VGChartz: Alien: Isolation review](https://www.vgchartz.com/game/77802/alien-isolation/reviews/254064/): manual saves
  lose "up to fifteen minutes", and saving leaves the player vulnerable.
- [Steam community thread](https://steamcommunity.com/app/214490/discussions/0/3109144584165201158): players report more
  locker checks after repeated use (NOT VERIFIED as the game's real rule).
- [Perdix VHF Yagi antenna](https://perdixwildlifesupplies.com/products/perdix-vhf-yagi-antenna): hand-held directional
  antenna for wildlife telemetry.
- [RadioReference forum: directional tracking](https://forums.radioreference.com/threads/directional-tracking.247573/):
  finding the peak with a Yagi (informal; NOT VERIFIED in a field guide).
