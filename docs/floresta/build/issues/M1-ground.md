# M1: what the first ground pictures showed, and what was retuned

Written 2026-10-13 by the M1 integrator. Captures of every step are under `captures/m1/iterations/`.

## The first picture (`iterations/iter01`, `iter01-full`)

Built with the spec's values untouched. It was already a correct picture: photographed ground, a real sky,
sane exposure (mean luminance 0.43 at `trail_start`), mist with depth, two ruts with a centre strip. Three
things were wrong with the ground:

1. **One texture from wall to wall.** Litter was the heaviest surface on 92 % of the core (WP3 had said so).
   From `overview_oblique` and `overview_ortho` the valley was one red-brown sheet; from eye level the
   litter's 1.5 m tile showed as rows running away from the camera (`trail_mid_fwd`, `long_sightline`).
   No moss, humus or needle patch could be told apart.
2. **The gully read as a second, wider track.** A continuous gravel band with two parallel dark mud
   stripes; in `long_sightline` it looked more like the road than the trail does.
3. **The centre strip of the trail was litter, not moss.**

## Cause 1: the height blend was biased against low-lying scans

HDRP's terrain height blend weighs each painted layer by `height - (highest of all eight layers) +
transition`. The spec gives every layer the same height remap (-0.04 to +0.04 m) so no layer has a larger
amplitude. But the scans do not agree on where "level" is. Mean of each displacement map (0 to 1):

| Layer | Mean height | Albedo (linear luminance) |
|---|---|---|
| 0 litter `forest_leaves_04` | 0.50 | 0.14 |
| 1 humus `forest_ground_06` | 0.34 | 0.07 |
| 2 mossy ground `forest_leaves_02` | 0.42 | 0.19 (0.15 after the 0.8 tint) |
| 3 gravel `rocky_trail` | 0.62 | 0.23 |
| 4 stony soil `forest_ground_04` | 0.39 | 0.11 |
| 5 mud `mud_forest` | 0.58 | 0.02 |
| 6 rock `mossy_rock` | 0.61 | 0.15 |
| 7 dry needles `forrest_ground_03` | 0.27 | 0.24 (0.19 after the tint) |

(Measured with `work/M1/scripts/bmpstats.py` on 256 px copies of the source maps.) With one remap for
all, a layer whose mean is low loses in every mix at every distance. By arithmetic on these means (far
enough away for the height maps to have blurred to their means): beside litter, dry needles keep about 0.4
of their painted share, humus 0.6, stony soil 0.7. This was not measured in a render; what the render
showed is that no patch could be seen before the change and that they can be seen after it.

**Change (`ForestMaterials.cs`, `BuildTerrainLayer`, `MeanHeight`):** the remap keeps the same width on
every layer (the spec's reason stands) and is shifted so that each layer's own mean lands on zero:
`maskMapRemapMin.z = -0.04 + shift`, `maskMapRemapMax.z = +0.04 + shift`, `shift = (0.5 - mean) * 0.08`.
The mean is read from `Source~/<id>__height.jpg` when the layer is built. The stamp of the terrain layers
includes the rule, so the eight layers were rebuilt once (`logs/M1-iter02-import.log`: "8 assets built").

`_HeightTransition` stays 0.04. A smaller value would give crisper mixes, but the blend compares with the
highest of all eight layers, painted or not, so below about the full height range the height pattern of an
unpainted layer (rock) starts to decide where painted layers show. Not tried for that reason.

## Cause 2: the raw weights never let a second surface win

Spec 6.4 gives litter 1 everywhere, humus `0.9 x thicket`, moss `0.8 x noise`, needles `0.7 x noise`. None
can exceed litter, so every patch was a tint on litter. **Changes (`ForestTerrain.cs`, `PaintSurfaces`;
all are named constants at the top of the file):**

| Term | Spec | Now |
|---|---|---|
| Humus under thickets | 0.9 x thicket | 1.3 x thicket |
| Moss patches | 0.8 x smoothstep(0.55, 0.75, noise) | 2.0 x the same |
| Dry needle patches | 0.7 x (1 - thicket) x smoothstep(0.5, 0.7, noise) | 1.6 x the same |
| Moss on the trail's centre strip | 0.6 | 1.5 |
| Patch breakup (new) | none | humus, moss and needle patch terms are multiplied by 0.55 to 1.35 from two noise fields (4.3 m and 1.9 m), so a patch is not one flat mix and its edge is not one smooth contour |
| Gravel in the gully bed | 0.7 | 0.7 x a 0.15 to 1 stretch noise (5 m): gravel comes in stretches |
| Mud on the gully banks | 0.5 | 0.5 x a 0.2 to 1 stretch noise |
| Stony soil in the gully cut (new) | none | 1.0 across the cut: washed-out soil between the gravel |

Unchanged: every threshold and wavelength of the spec's noise fields, litter 1 everywhere, the ruts, the
puddle mud, the rock and stony-soil slope ramps, the stamps around props, the compositing order, the blur
and the unblurred trail band, the tints of 6.3.

The surface map now says who is heaviest in the core: litter 46 %, mossy ground 23 %, humus 15 %, dry
needles 13 %, gravel 1.3 %, mud 1.0 % (was: litter 92 %).

## What the final pictures show (`captures/m1/`)

- `trail_start`, `trail_mid_fwd`, `trail_strip_01` to `_08`: two gravel ruts with a green-brown mossy
  strip; litter, moss, needle beds and dark humus on either side; the far bank fading into mist.
- `ground_close` and its 1:1 crop: needle bed and gravel at scan sharpness.
- `long_sightline`: the gully is a broken stony bed, no longer a road.
- `overview_ortho`: patches of humus, moss and needles; trail, gully and hollow readable.

## Still visible, and not fixable with the named tunables

1. **The litter tile.** From `overview_oblique` (40 m up) the 1.5 m tile of `forest_leaves_04` is a
   regular pattern wherever litter is alone, and at eye level it still shows as faint rows at 15 to 40 m
   (`trail_strip_06`, right of the trail). Stock TerrainLit has no anti-tiling (spec 6.3, risk 13). The
   mixing above halved the area where litter is the heaviest surface (92 % to 46 % of the core). What is left is for ferns, grass and branches (M2)
   and trees (M3, M4) to cover, or for the anti-tiling terrain graph (owner decision, risk 13).
   Tried and taken back: a constant 0.4 of humus under the litter everywhere (`iterations/iter05`). It
   dimmed the pattern a little and put ghost leaves into the close-up (`ground_close_crop`), which is the
   picture meant to stand beside a paid pack.
2. **The bank is a bare bowl** with a hard skyline and the tile pattern in the mist (`boundary_*`,
   `pano_*`). It is the fir wall's place (M3, M4).
3. **Rut edges are soft.** A stone at the edge of a rut fades out over about 15 cm instead of ending
   (`ground_close`, bottom left). That is the alphamap texel (12.5 cm) times the blend's transition.
4. **Stony soil almost never wins in the core** (0.1 %): the core has few slopes above 14 degrees. The
   "stones" of the art direction arrive as meshes at M2.

## Exposure, sky, mist: not changed

Every value of spec 8.2 is as written. Measured on the final build:

- Sky: `upperHemisphereLuxValue` 4.683, Lux mode, 5,000 lux asked for. Open ground 6,182 lux, neutral
  10.94 EV; automatic exposure sits at its upper clamp (10.5, compensation -0.3).
- Mean luminance of the first-person views 0.39 to 0.44; no pixel clipped white or crushed black in any
  of the 30 captures; 0 error pixels.
- **The margin is thin**: `trail_start` is at 0.430 (edit mode) and 0.429 (play mode) against the
  window's upper end of 0.45. Half the frame is sky. Rocks, ferns and trees will lower it; a bright
  change to the ground paint would not have room.
