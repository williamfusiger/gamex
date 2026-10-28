# M1: the engine's draw-call counter reads 0, so `Budget_Counts` counts draws from the scene

Written 2026-10-13 by the M1 integrator. Unity 6000.6.0f1, HDRP 17.6.0, Metal, batch mode, play mode.

## What happened

The first M1 run of the forest PlayMode suite failed one test (`logs/M1-06-forest-playmode.log`):

```
Budget_Counts: 'Draw Calls Count' read 0 for every frame, so the counters did not see the measured
camera and no budget was checked.
```

The other counters of the same frames did report: `Triangles Count` 128,933, `Vertices Count` 73,758,
`SetPass Calls Count` 27, `Shadow Casters Count` 4. So the counters did see the camera; the message was wrong
about the cause.

## What was measured

A temporary diagnostic inside the test (removed again) started a recorder for every count counter of
the Render category (23 of them) and printed the ones that were not zero, with the terrain alone and with
50 extra cubes in front of the camera:

| Case | Non-zero counters |
|---|---|
| Terrain only, explicit `camera.Render()` | Render Textures Count 85, Triangles 128,933, Vertices 73,758, SetPass Calls 27, Shadow Casters 4, Render Textures Changes 44 |
| Plus 50 cubes, explicit render | Triangles 132,269, Vertices 80,430, SetPass Calls 34, Shadow Casters 132 |
| Terrain only, camera enabled (no explicit render) | Triangles 128,933, Vertices 73,758, SetPass Calls 27, Shadow Casters 4 |
| Plus 50 cubes, camera enabled | Triangles 131,669, Vertices 79,230, SetPass Calls 33, Shadow Casters 132 |

`Draw Calls Count`, `Batches Count` and every batched/instanced counter stayed 0 in all four cases. Setting
`GraphicsSettings.useScriptableRenderPipelineBatching` to false at run time changed nothing either (SetPass
stayed 34 with the cubes, so the switch probably had no effect on a pipeline that already existed).

Logs: `logs/M1-07-diag-counters.log`, `-b.log` (first attempt did not compile), `-c.log` (SRP batcher
switch), `-d.log` (enabled camera).

So on this Unity version and pipeline the engine does not feed the draw-call counters at all. The test as
written could not pass for any scene.

Answered in passing: **enabled cameras do render by themselves in batch-mode play mode** (spec 12.3 had it
as UNVERIFIED), and **the render counters do report in play mode** except the draw-call family (the open
half of probe P5).

## What I changed (`Assets/Game/Tests/ForestPlayMode/ForestPlayModeTests.cs`)

`Budget_Counts` keeps every recorder and every budget. Two things are different:

1. When `Draw Calls Count` is 0 for every frame although `Triangles Count` shows the camera rendered, the
   draw-call budget (4,000) is checked against a number counted from the scene, `EstimateDrawCalls`:
   - every enabled renderer the frame's culling found visible (`Renderer.isVisible`) costs one draw per
     material slot in the depth prepass and one in the main pass (decals are enabled in the project, so
     HDRP always runs the prepass; spec section 10 argues the same way for triangles);
   - every shadow caster the engine counted costs one draw per material slot of the average visible caster.
   A renderer that only a shadow cascade sees is counted in the main passes too, so the number errs high.
   The terrain is not a `Renderer`: its instanced patches are in the count only through the shadow casters.
2. The guard against a silent pass is kept: the test fails when the triangle counter is 0, and when the
   draw-call figure in use (the engine's or the counted one) is 0.

`budget-counts.txt` and the log say when the figure is an estimate:

```
Draw Calls Count: 4 (ESTIMATED from the renderers the frame's culling found visible; the engine's
counter reads 0 in this Unity version) at trail_start (budget 4000)
```

If a later Unity version feeds the counter again, the test uses the engine's number without any change.

## What this is and is not

It is not a measurement of draw calls. It is a count of what the frame had to draw, made the way the
spec's own bottom-up estimate is made, and it is the only way I found to keep a draw-call check that can
fail. At M1 the scene holds no renderer (only the terrain), so the figure is 4 and says nothing yet; it
starts to mean something at M2. Motion-vector and transparent passes are not in it. `SetPass Calls Count`
(27 at M1, budget 250) is measured by the engine and is the number that tracks CPU cost under the SRP
batcher.

Not changed: no budget, no threshold, no other test. The spec has no fallback for this case (Appendix B
only names the stopwatch for frame time), which is why it is written down here.

## Needs a decision

Whether a counted draw-call figure is acceptable as the budget check, or whether the draw-call budget
should be dropped in favour of SetPass calls and shadow casters, is for whoever owns section 10.
