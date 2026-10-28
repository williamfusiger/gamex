# M2: what the first floor pictures showed, and what was changed

Written 2026-10-13 by the M2 integrator. Unity 6000.6.0f1, HDRP 17.6.0, Metal, batch mode.
Paths are relative to `build/` unless they start with `Assets/`. Captures of every step are under
`captures/m2-r0/iterations/` (iter01 is the spec's values untouched); measurements are in
`out/M2/measurements/`.

The kit built on the first launch (28 prefabs, every bounds check passed) and the scene built and passed
its audit on the first launch (`logs/M2-03-kit-build.log`, `M2-04-scene-build.log`). Nothing in the kit or
scatter code had to be repaired to run. The six changes below are what the pictures then showed.

## 1. The six rocks of `rock_moss_set_01` had black patches (a fault in the downloaded map)

**Seen:** the kit sheets of iter01 (`iterations/iter01/kit/rock_moss_set_01_*`): rocks of set 01 had
large, hard-edged black areas. Set 02, lit the same way, had none.

**Cause, measured on the files** (`out/M2/measurements/ao-under-uvs.txt`, picture
`rock_moss_set_01-uv-vs-ao.png`: yellow = used and has AO, red = used by the mesh but AO is zero,
green = AO island no triangle uses):

| | Surface whose AO texel is zero | Triangles whose roughness texel is zero | Triangles whose base colour texel is near black |
|---|---|---|---|
| `rock01` to `rock06` (set 01) | 23.2, 7.2, 32.9, 32.4, 27.0, 23.6 % of the area (the kit gate's own numbers, `logs/M2-07-kit-gate-proof.log`) | 0.0 % | 0, 0, 1, 0, 0, 0 triangles |
| `rock07` to `rock13` (set 02) | 0.0 % of the triangles | 0.0 % | not measured |
| stumps, log, branches, ferns | 0.0 to 0.3 % of the triangles | 0.0 % | not measured |

(Rows two and three are counted per triangle on 1k copies of the maps; the first row's AO column is
weighted by area on the packed 2k mask.) At 512 px with AO counted as present above 64/255, 25,519 texels
lie under set 01's triangles and have no AO, and 36,490 AO texels (31 % of the AO islands) lie under no
triangle. For set 02 the same counts are 12 and 8,868. So the AO channel of
`rock_moss_set_01__arm.jpg` was baked for another UV layout than the FBX carries; roughness, base colour
and normal fit. HDRP multiplies sky light by AO, and the sky is three quarters of the light here.

**Changed:**
- `ForestTexturePacker.cs`: materials listed in `UnusableAo` (only `rock_moss_set_01`) get full AO (255)
  in their mask. Screen-space AO still shades them. The stamp of that one mask changed; no other mask was
  repacked.
- `ForestPrefabs.cs`, `RequireAoUnderUvs`: the kit build now samples the packed mask under every triangle
  and throws when more than 2 % of a surface (by area) has AO below 8/255. Leaf texels cut away by the
  alpha test are not counted.
- `Tests/ForestEditMode/ForestKitTests.cs`: new test `Prefabs_OcclusionMapsFitTheirMeshes`, the same
  measurement on the built prefabs.
- `forest-asset-manifest.json`: a `note` on the `rock_moss_set_01` entry (one added line; the code ignores it).

**Proof the gate can fail:** `logs/M2-07-kit-gate-proof.log` is a kit build with the gate and the old
mask: "22 prefabs built ... 6 failed", the six rocks at 7.2 to 32.9 %. After the repack:
`logs/M2-iter02-kit.log`, 0 failed.

**Not done:** fetching Poly Haven's separate AO map for this set (it is not in the manifest; its URL would
have to be read from the `/files` API) or re-baking the AO. Either would give these six rocks their
crevice shading back.

## 2. The fallen trunk rendered as a black silhouette

**Seen:** `clearing` and `pano_*` of iter01: logs at 10 m and more were near-black shapes.

**Cause:** spec 5.5 takes the scan for sun-bleached and multiplies its colour by 0.6. Mean linear albedo
under the UVs (`out/M2/measurements/albedo-under-uvs.txt`): log 0.105, stumps 0.093 and 0.088, rocks
0.077 to 0.093. The log is 13 to 19 % brighter than the stumps, not bleached; at 0.6 it was 0.063, darker
than everything around it.

**Changed:** `ForestMaterials.cs`, `BaseColourGrey`: 0.6 to 0.85 (0.089, level with the stumps).
Deviation from spec 5.5.

## 3. Grass did not read as grass

**Seen:** `clearing` of iter01: a patch was a dozen single stalks on bare ground. The scanned clumps are
lawn height (9 to 32 cm tall); the spec's mix (twelve tufts within 0.6 m) holds one leafy clump, four seed
stalks and seven "tiny" clumps of a few blades.

**Changed:** `ForestKitSpec.cs`: a patch is six tufts within 0.4 m, two of them leafy (`small_a`,
`small_b`, `mid_b`), at 1.7 to 2.4 times scan size (was 0.9 to 1.2). 2,488 and 2,420 triangles (limit
2,500, checked by the build). Only clumps from the spec's list of eight are used. `ToolVersion` is
`kit-2`, so every prefab was rebuilt once.

**Deviation from spec 5.6.5, and a scale decision for the owner:** the grass is now about twice life
size: a patch is 0.57 to 0.65 m tall, up to 0.85 m at the scatter's largest scale. Up close it reads as a
tussock (`captures/m2-r0/inspect/grass*.png`). From standing height in `clearing` it is still thin:
stalks on a needle bed, not a sward.

## 4. Ferns were an even sprinkle, not drifts

**Seen:** `trail_mid_fwd`, `overview_oblique` of iter01: one small fern every three metres, everywhere.

**Cause:** the density field of spec 7.2 asks for several thousand patches; the cap is 900. WP3 scaled the
whole field down by one factor (its deviation 8), which turned every drift into isolated patches. Nearest
neighbour of a fern patch in iter01: median 3.05 m, 10th percentile 2.56 m.

**Changed:** `ForestScatter.cs` (`FernThinning`, `FernDensity`), two constants in `ForestLayout.cs`: the
density is scaled down to 0.36 at most; what is still over the cap is taken off the size of the drifts by
raising the noise threshold (by 0.45 at most). Count unchanged: 900. Nearest neighbour now: median 2.33 m,
10th percentile 1.88 m (`out/M2/measurements/plan-stats.txt`, map `plan-final.png`).

**Still true:** with 900 patches on 1.87 ha the floor is a sparse fern heath. A patch is five clumps
within one metre and 0.35 to 0.53 m tall at scale 1. Which thinning and narrowing the solver chose is not
logged. Denser drifts need a higher cap, which is a spec constant that a test holds (`Scatter_IsDeterministic`).

## 5. `overview_oblique` showed a floor no player sees

**Seen:** iter01: no fern, no grass, no branch in the picture. The camera hangs 40 m above (0, -90);
ferns are culled at 40 m, branches at 35 m, grass at 25 m.

**Changed:** `ForestCapture.cs`: `overview_oblique` forces every LODGroup to its nearest level, as
`overview_ortho` already did (spec 12.2 names only the orthographic view). Its sidecar says
`lodsForced: true`. It now shows ferns 150 m away that the player would not see either: it is a map of
what is placed, not a player's view.

## 6. The trail ended in a square cut

**Seen:** `trail_strip_08` (noted at M1): the gravel stopped at a line, 8 m short of the outcrop's rocks.

**Changed:** `ForestTerrain.cs`: ruts, centre strip and puddle paint fade in over 0.5 to 3.5 m from both
ends of the trail. The spawn is 4 m from the south end, where the bands are complete. Heights are
unchanged.

## What the final pictures show (`captures/m2-r0/`)

- `trail_start`: two ruts with a mossy strip, ferns loosely on both sides, two moss boulders on the right
  shoulder, rock groups fading into the mist. Mean luminance 0.413.
- `rock_close`: the 1.75-scale boulder of the boulder field, lit, with scan detail and no black patch.
  It has an undercut on its near side that is in shadow.
- `ground_close`: gravel and needle bed at scan sharpness, fern fronds over the rut's edge.
- `clearing`: sparse grass and the two stump patches with their fern rings; logs beyond.
- Close-ups of all 8 logs, all 10 stump patches, 14 rocks on the steepest ground, 8 largest rocks and a
  sample of the rest, branches, grass, ferns (`inspect/`, 117 images): nothing floats, no gap shows under
  a rock, logs are bedded with some trunk showing. One log (`...__01120`) lies across a hump with one end
  off the ground (a few decimetres, judged by eye), as a rigid trunk would.
- Kit sheets (`kit/`, 50): scale, orientation and materials are right; decimated rock levels keep their
  silhouette; no magenta, no shine.

## Still visible, not fixed

1. **The litter tile.** From above (`overview_oblique`, `overview_ortho`) the 1.5 m tile of
   `forest_leaves_04` is a regular herringbone wherever litter is alone, and it shows on the bare bank
   (`boundary_*`). 900 fern patches do not hide it. From eye level on the floor it is faint. Stock
   TerrainLit has no anti-tiling (risk 13); the fallbacks left are trees (M3, M4) or the anti-tiling
   terrain graph, which needs the owner's approval.
2. **The bank is a bare bowl** with a hard skyline (M3, M4).
3. **Grass is thin from standing height** (change 3).
4. **`trunk_close`, `fir_close`, `crouch_hide`, `canopy_up`** show ground, rocks and sky: no tree exists.
5. **Stump patches are almost hidden** by their fern rings: at 0.3 to 0.4 m tall they barely show from
   10 m. That is the spec's ring rule working on the real sizes.
