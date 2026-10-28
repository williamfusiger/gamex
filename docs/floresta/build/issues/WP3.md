# WP3 World: deviations, spec gaps and what was measured

Written 2026-10-13 by the WP3 owner. Unity was not started. Files: `ForestLayout.cs`, `ForestFields.cs`,
`ForestTerrain.cs`, `ForestScatter.cs`, `ForestWorldRig.cs`, `ForestSceneBuilder.cs`, `ForestBuild.cs` in
`Assets/Game/Editor/Forest/`.

## What was checked, and how

| Check | Result | Evidence |
|---|---|---|
| The seven files compile against the contract and the stubs of WP1, WP2, WP4, WP5 | 0 errors, 0 warnings | `out/WP3/Gamex.EditorTools.dll.log` (empty), last line of `compile.sh` was `OK` |
| The consumer probe `probes/SurfaceUse.cs` compiles against the real WP3 files | `OK` | `out/WP3/SurfaceUse.dll.log` |
| The public surface of the real files equals the stub's (types, fields, constant values, method signatures, parameter names, menu paths) | identical, 71 lines each | `out/WP3/harness/surface/stubs.txt` and `real.txt` (`diff` is empty) |
| The pure code (layout, fields, plan, root mounds, grounding, painter) run outside Unity, on Unity's own Mono 6.13 and on the bundled .NET 8, seeds 1993, 7, 42, 2024, 123456, 99 | every check passed on every seed | `out/WP3/harness/run/mono/harness-seed*.log`, `out/WP3/harness/run/harness-seed1993.log` |

The harness (`out/WP3/harness/`, `build-and-run.sh` and `run-on-mono.sh`) compiles the real WP3 files with
small fakes of the other packages (`src/Fake*.cs`: clearance radii and bounds copied from the manifest) and
runs them with the real `UnityEngine.CoreModule.dll` and `UnityEngine.MathematicsModule.dll`. It proves
arithmetic, not engine behaviour: nothing that touches a `TerrainData`, a prefab, the NavMesh or the
`AssetDatabase` has run. Pictures of the plan and of the painted weights are in `out/WP3/harness/run/*.png`.

Measured with seed 1993 on Unity's Mono (the numbers the Editor should reproduce; a few placements differ
between runtimes because of floating point, the totals do not):

- 2,438 placements: 277 tall firs (59 core, 218 bank), 764 young firs (354 core, 410 bank), 84 rocks, 8 logs,
  10 stump patches, 65 branches, 900 fern patches (the cap), 94 fern clumps, 236 grass patches.
- Core slope at most 25.7 degrees (limit 27), trail at most 5.2 degrees (limit 12), heights -1.9 to 20.7 m.
- Smallest corridor distance minus clearance 2.57 m (rule 2.5); smallest spawn distance minus clearance 5.11 m.
- `ComputeFields` 3.3 s, `Plan` 0.6 s on Mono; the painter 1.3 s on Mono (1.5 s on .NET 8).
- Across the six seeds on Mono: 252 to 277 tall firs, 764 to 815 young firs, 84 to 93 rocks, 213 to 236
  grass patches; 38 checks passed and none failed on each.

## Deviations from the spec text

Each is the smallest change that made the rule work. None changes the frozen surface.

1. **The spawn is 0.25 m from the trail's centre line.** `SpawnXZ` is frozen at (0, -62) and the control points
   are the spec's. Any Catmull-Rom curve through (0, -66), (0, -58), (7, -42) bulges west before the second
   point. The centripetal form is used because it bulges least without changing the rest of the trail
   (the uniform form: about 0.44 m, by arithmetic). The spawn is still on the tread (half-width 0.8 m). **An audit or test of "spawn on the
   trail" needs a tolerance of at least 0.3 m.**
2. **Root mounds need gentle ground under the whole mound** (16 degrees at most within its radius), not only
   12 degrees at the trunk. The spec's argument that a mound cannot break the 27 degree limit holds at the
   trunk only: with the trunk-only rule, seed 123456 produced 27.4 degrees beside the gully and the build
   threw. With the extra rule the worst case is 25.5 degrees. 59 of 277 tall firs get a mound on seed 1993.
3. **The talus clamp limits the step between neighbouring samples to 24 degrees.** That bounds the gradient of
   central differences at 25.7 degrees, so a different slope stencil (Unity's, a test's) still reads under 27.
   A first version that smoothed steep samples did not converge next to the pinned tread.
4. **The gully gets wider where its bed is deeper than 0.35 m.** The bed is the running minimum toward the
   hollow, as specified; where the ground rises along the way that makes it deeper, and a 3.5 m wide cut would
   then be steeper than the core allows. Half-width = max(1.75 m, 4.5 x depth). The head fades in over 6 m.
5. **Micro relief (5 cm and 2 cm) is added after the trail is carved**, faded out on the tread, instead of
   before the bank. The trail profile is then filtered from clean heights. The clearing therefore keeps its
   micro relief at full strength. The fBm amplitude of 1.2 m is the peak of the four-octave sum.
6. **Counts are reached with two additions to the spacing rules.** A dense Poisson-disc fill of the spec's
   spacings gave 1,045 young firs in the harness, not 770, and by the same packing would give about 90 core
   tall firs, not 60 (that second number is arithmetic, it was not run).
   - Tall firs in the core: spacing varies from 11 m (the spec's minimum) to 18 m with a 55 m noise field.
   - Young firs: each point asks for 1 to 1.38 times `lerp(7, 3, T)`, by a hash of its position. The spec's
     value stays the least distance any two trunks keep.
7. **The thicket field has soft edges.** It rises to the bank's 0.8 over the last 6 m of the core, fades out
   over 4 m beyond the 18 m band, and ramps over 2 m at the clearing. A hard step is painted as a line on the
   ground (humus is `0.9 x thicket`). The noise is mapped as `0.4525 + 0.65 x fBm`, which puts thickets
   (T of 0.5 or more) on about a third of the core.
8. **Fern patches: the density field is thinned by one factor** so that the expected count is 93 % of the cap
   of 900; any excess is dropped by position hash. Without it the field asks for several thousand. Patches are
   allowed up to 6 m outside the boundary, none within 1 m of the boundary line.
9. **Branches are raised, not sunk.** The spec's sink table has no row for dry branches. Their pivot is at the
   middle of the piece (the manifest boxes reach 5 to 14 cm below it), so `sink = 0.02 + scale x boundsMin.y`,
   which is negative: the branch rests on its lowest twigs, 2 cm in the litter. This uses
   `ForestManifest.SourceBounds(...).min.y`, which the spec lists for the rock sink only.
10. **The boulder field, the two clearing stumps, the hollow stump and the three rim firs are set pieces**,
    placed in the landmark stage before the trees, with `landmark` set. One boulder-field rock is fixed at
    (36, 44) (`rock_moss_set_02_rock11`, scale 1.75) so that `rock_close` has something to face; the other
    nineteen are scattered there with the landmark random stream. Placed after the trees they did not fit.
11. **A seventh landmark, `SpawnView`**, holds the composed first view of section 3.5 (its pieces need a name
    under `Environment/Landmarks`). `ForestLayout.Landmarks` therefore has seven entries.
12. **Logs do not bridge the gully** and avoid ground steeper than 15 degrees under any part of them. The
    sink of section 7.3 beds a log on even ground only.
13. **Scattered trunks and rocks keep 0.8 m (plus most of their radius) from first-person capture viewpoints.**
    Set pieces are exempt; they are composed around their viewpoints.
14. **Terrain paint discs (humus under tall trunks, moss around rocks and logs, stony soil under stumps) are
    stamped only for prefabs that are built.** At M1 and M2 no dark discs appear where trees will stand.
    Root mounds are always there (they are part of the pure height field and 6 to 12 cm high).
15. **The surface map always holds the eight surfaces**, also when the terrain is painted with four layers.
    Its importer is set to uncompressed, linear, readable, point-filtered. Row 0 is the south edge.
16. **`ForestBuild.BuildAll` asks the rebuild question before the import**, not after it; `Build` itself asks
    as specified. Both go through the same internal method, so the stage order of 11.5 is one piece of code.
17. **`backlit` faces the default preset's key light** (`ForestLightingPresets.Get(Default).keyBearing + 180`,
    read once when `ForestLayout` is first used). A capture with another preset must turn that camera itself.
18. **Slowly varying noise is evaluated on a coarse lattice and interpolated** (floor fBm at 0.5 m, thicket at
    1 m, moss and needle noise at 0.5 m). The difference is under 2 mm of height; it halves the build time.

## Things the spec leaves open that someone should know

- **`ForestSceneBuilder.Build` changes `ProjectSettings/EditorBuildSettings.asset`** the first time it runs:
  step 12 of section 11.5 appends the forest scene (the greybox stays first). That file is in
  `backup/pre-forest.md5`, so its checksum will differ after the first build. This is the spec's instruction,
  not an accident.
- **The landmark points of `SouthBank` and `NorthOutcrop` are at the bank.** (2, 71) lies inside the outcrop's
  rocks by the spec's own geometry. A NavMesh test should path to `Gameplay/DinoSpawns/NorthOutcrop`, which
  is on open ground 2 m beyond the trail's end, or sample with a generous radius. `Landmark.radius` is the
  size of the set piece; for those two the circle crosses the boundary.
- **Fern clumps come to about 90, not 400.** The ring rule and the spawn composition are their only sources
  (section 7.2); 400 is a cap. A count test should treat 400 as a maximum.
- **Litter stays the heaviest surface on 92 % of the core.** Humus is `0.9 x thicket` against litter's 1, so
  it never wins. The surface map is therefore mostly litter. That follows the weights of section 6.4; if
  thickets should read as humus, its weight needs to exceed 1.
- **The bank reaches 45 degrees in places.** The smoothstep alone gives 31; the floor noise of step 1 applies
  on the bank too. The rock layer (32 to 42 degrees) is painted there. The walls, not the slope, hold the player.
- **The yaw rule cannot always be met in a thicket** (more same-variant neighbours within 8 m than 40 degree
  slots). The planner takes the best of twelve tries. On seed 1993 no young fir pair breaks it.
- **`ForestLayout.TerrainLayerCount` is restored after a build** that was given `-forestLayers`.
- **The plan differs slightly between Mono and .NET** (a handful of placements), so numbers from the harness
  on .NET are not bit-for-bit what Unity will produce. On one runtime, two plans of the same seed are equal.
