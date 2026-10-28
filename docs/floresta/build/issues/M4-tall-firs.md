# M4, first half: the tall fir prefabs (fetch, measure, import alone, trunk_c, bake)

Written 2026-10-14. Paths are relative to `build/` unless they start with `Assets/` or `Tools/`. Originals of every changed
file: `backup/M4a-pre-edit/`; diffs: `out/M4a/*.diff`. Final transcript: `logs/M4a-final-sequence.txt` (run 2); run 1 (the
same except section 6, trunk_b) is kept in `logs/M4a-final-run1/`, `captures/m4a/final-run1/`, `out/M4a-run1/`.
Iterations: `captures/m4a/iterations/k1` to `k6` (kit sheets), logs `logs/M4a-k*-*`.

## 1. Fetch and measure (spec 5.1, risk 22)

`fetch_assets.py --id fir_tree_01` (`logs/M4a-01-fetch.log`): 17 files, 348.6 MB, md5 OK (the FBX in 3.6 s).
`fbx_objects.py` measured all three objects: triangles, triangles per material and bounds equal the manifest (largest
difference 0.0000 m), so it printed MATCH and set `objectsSource` to `"fbx"`. **No `--write` was needed; no layout number
that depends on the tall firs' sizes changes.** (The manifest copy in `backup/M4a-pre-edit/` already carries that flag:
the backup was taken after the fetch. Before it, the entry said `"gltf"`; nothing else differed.)

The same log says `UV layers 0` for every object, like `fir_sapling_medium` at M3. As at M3 (`issues/M3-young-firs.md`
section 1), the glTF geometry of the same model was added to the manifest from Poly Haven's `/files` API
(`work/M4a/dl-api/files.json`, read as data; URLs, sizes and md5 copied by `work/M4a/scripts/add_gltf.py`):
`Source~/fir_tree_01.gltf` (30,279 bytes) and `Source~/fir_tree_01.bin` (478,462,204 bytes, the one buffer every resolution
of the glTF shares), fetched with the project's tool (`logs/M4a-02-fetch-gltf.log`, md5 OK). Read as data
(`work/M4a/scripts/twig_uv.py`, `trunk_ao.py`): every primitive has `TEXCOORD_0` **except `fir_tree_01_trunk_c`**; nodes
carry translations only; `fir_tree_01_bark` has a texture transform (scale 1.2 / 0.1, offset 0 / 0.9), `trunk_c` one
(1.4, offset 0 / -0.4) that binds it to the bark images. `THIRD_PARTY_ASSETS.md` records the pair (version 1 now 1,540 MB).

Facts measured on the files that shaped the bake:
- The twig atlas of `fir_tree_01_twig` is the sapling's atlas with another colour: alpha, normal and arm maps have the same
  md5 as `fir_sapling_medium_twigs`', only the diffuse differs; the bark maps equal the sapling's branch maps.
- Its twigs use the same single-needle region and none of the seven sprays (45 % of the twig UVs in the needle box, 0 % on
  sprays). Pieces: 812,086 (a), 437,120 (b), 91,639 (c), median 4 triangles, no spatial fallback.

## 2. Import alone (spec 5.2, risk 2)

`tools/m4a/import-alone.sh`, memory sampled every 2 s by `tools/m4a/memwatch.sh`:

| Run | What it imported | Wall time | Peak resident memory | Evidence |
|---|---|---|---|---|
| A | The nine fir_tree_01 textures, the FBX held outside the project | 6 s | 1,156 MB | `logs/M4a-10-import-textures.log`, `.mem.tsv` |
| B | **`fir_tree_01.fbx` alone** (exactly one `Importing` line) | 6 s; the import itself 1.624 s | 1,922 MB | `logs/M4a-11-import-fbx.log`, `.mem.tsv` |

Risk 2 did not occur. The heaviest step is the kit build (all 34 prefabs, the three tall firs read from the 478 MB glTF):
44 s, peak 5.2 to 5.3 GB (`logs/M4a-final-51-kit.mem.tsv`); `fir_tree_01_a` bakes in 16.9 s.

## 3. The trunk_c choice (spec 5.5, risk 23)

**Case 2 of spec 5.5, automatically: `fir_tree_01_trunk_c` is drawn with the triplanar variant of `fir_tree_01_bark`.**
Evidence: the FBX has no UV layer at all (`logs/M4a-01-fetch.log`: `material fir_tree_01_trunk_c ... no UV layer`); the
glTF's `trunk_c` primitive has no `TEXCOORD_0` (section 1); the kit report lists `triplanarMaterials: ["fir_tree_01_trunk_c"]`
(`out/M4a/kit-report.json`). `ForceTriplanar` stays empty: it is for UVs that exist but are garbage, and there are none.
The `trunk_c` texture set (23 MB) is downloaded as the manifest says but unused; its atlas (root flare, litter, bark
panels: `work/M4a/look/fir_tree_01_trunk_c__basecolor.png`) is not tileable and cannot be projected.

To get there the glTF reader had to accept a primitive without UVs: `ForestPrefabs.ForestGltfGeometry.Read` now does so
only when the primitive's material has a `noUvFallback` (its UVs are left at zero, which `ResolveMaterials` reads as "no
usable UVs"); any other material without UVs still stops the build.

Looked at: `fir_tree_01_c__close_turntable.png`, `__below_03m.png`, `__lods_08m.png` (`captures/m4a/final/kit/`). The trunk
reads as bark at 1.5 to 3 m from four sides, with no stretching or seam; it is darker and greyer than the scanned trunks of
a and b, and the root skirt that belongs to the trunk_c mesh is bark-textured too, where a's and b's carry litter.

## 4. trunk_a: the AO gate (M2's `RequireAoUnderUvs`)

The first kit build (`logs/M4a-k1-kit.log`) refused `fir_tree_01_a`: "the mask map of material fir_tree_01_trunk_a has no
ambient occlusion under 17.5 % of the surface". Measured (`work/M4a/scripts/trunk_ao.py`, `uv_overlay.py`,
`uv_density.py`; picture `work/M4a/look/trunk_a_uv.png`): trunk_a's atlas islands cover only the root flare and the lowest
~3 m (texel density 38 to 53 times the rest); **from 3 m to the top the trunk is mapped to a thin strip along the atlas's
left edge** that crosses two litter islands and the unbaked gaps between them (AO 0). trunk_b, the control, has 0 %.

Smallest change: `ForestTexturePacker.UnbakedAoFilled = { "fir_tree_01_trunk_a" }`: mask texels with no baked AO (under the
gate's own floor, 8) take the mean of the baked ones (0.82); 26.7 % of the texels, every baked texel unchanged
(`logs/M4a-k2-import.log`). The gate and `Prefabs_OcclusionMapsFitTheirMeshes` are unchanged and pass. `UnusableAo` (full
AO everywhere) was not used, because it would also throw away the AO of the lower trunk, which does fit.

## 5. The levels (spec 5.6.4, risk 1, the review-2 conditions)

E1 chose arm C (scan to 6 m) for the young firs; the spec gives the tall firs no scan level. What E1 settled for the
levels *behind* the scan was applied to the tall firs where the sheets bore it out:

| Step | Kit run | Result | Kept? |
|---|---|---|---|
| Knob 4 of spec 10 on LOD0 foliage (x 0.7: 112,000 / 112,000 / 63,000), asked for by all three lenses of review 2 | all | `ForestKitSpec.TallFirLod0Factor`; 1 takes it back. Costs about 1.2 times the needle enlargement (count estimate: a 3.2 -> 3.8, b 2.3 -> 2.8, c 1.4 -> 1.7) | Yes |
| Every level flattened (a needle card becomes one quad), as behind the young firs' scan | all | LOD0 keeps 56,000 needles of a at 3.71x instead of about 14,000 at the 5.1 cap | Yes |
| Twig cards from the 12 m level, as the young firs from 14 m | k2 | Coverage 1.01 to 1.03, but at 12, 25, 45 and 70 m both card levels read as a light broadleaf tree, all three variants (`iterations/k2/kit/fir_tree_01_*__lods_25m.png`, crops `work/M4a/look/a25_crops.png`). Not the albedo (the sprays are 0.66 to 0.80 of the needles' value) and not AO (matched) | **No**: the finding review 2 made on the young firs |
| Flattened needles at 12 to 45 m and 45 to 110 m (risk 1 fallback b) | k3 | LOD1 (6.4x) reads as a conifer at 25 and 45 m with LOD0's hue. LOD2 at the spec's cap 20 (16.8x): a bright mass of straws | LOD1 yes |
| LOD2 cap 8 | k4 | A sparse spray of sticks | No |
| LOD2 cap 12 | k5 | Fill like the source and LOD1, crown shape kept; greener and slightly stick-like, at distances where the mist takes half the contrast or more (`work/M4a/look/cap/compare.png`) | Yes |
| LOD2 wood: the trunk alone gets the wood budget | k4 | Clustered with the branches into 1,280 triangles the 19 m trunk was a twisted ribbon (`iterations/k2/kit/*__lods_12m.png`, panel 4); trunk alone it is straight at 45 m | Yes (`TallFirTrunkOnlyFromLevel`) |

`ToolVersion` "kit-8". Final levels (`out/M4a/kit-report.json`):

| Tree | Level | Until | Triangles | Needles kept, scale | Coverage (gate) |
|---|---|---|---|---|---|
| a (18.9 m) | 0 | 12 m | 222,700 (110,700 scanned wood) | 56,000 at 3.71 | 1.09 (0.80 to 1.30) |
| | 1 | 45 m | 46,372 | 20,000 at 6.41 | 1.11 (0.80 to 1.30) |
| | 2 | 110 m | 8,453 | 4,000 at 12 (cap) | 1.08 (0.70 to 1.30) |
| b (14.0 m) | 0 | 12 m | 205,328 | 56,000 at 2.69 | 1.07 |
| | 1 | 45 m | 27,832 | 12,000 at 6.08 | 1.13 |
| | 2 | 110 m | 5,465 | 2,500 at 12 (cap) | 1.13 |
| c (14.4 m) | 0 | 12 m | 122,420 | 31,500 at 1.63 | 1.07 |
| | 1 | 45 m | 27,837 | 12,000 at 2.68 | 1.02 |
| | 2 | 110 m | 4,398 | 2,000 at 7.09 | 0.98 |

Trunk capsules 0.154 / 0.141 / 0.148 m (clearance 0.40). Mirror: X, reported "symmetric" (centre offset inside tolerance).

### The LODGroup size (review 2, technical condition 2)

`ForestKitSpec.LodGroupSize`: the LOD size is the object's largest side, capped where the first level would end above 0.8
of the screen height (`MaxFirstTransitionHeight`). The size scales every transition height alike, so no distance moves.
Before, the clamp at 0.98 ran young fir a's 4.5 m scan to about 6.4 m with no cross-fade band, and would have run tall a's
12 m LOD0 to 13.8 m. Now (prefab YAML): young a size 5.04, heights 0.80 / 0.257 / 0.086 / 0.042 (4.5, 14, 42, 85 m); young b
6.72; young c unchanged; tall a, b, c 13.44, heights 0.80 / 0.213 / 0.087 (12, 45, 110 m). For the young firs only the
LODGroups changed: their rebuilt levels have the same triangle and vertex counts as at M3 review 1 (kit report; not
compared byte for byte).

### How the levels look (risk 1; the owner decides at review 3)

Sheets: `captures/m4a/final/kit/fir_tree_01_*__{turntable,lods,lods_08m..70m,foliage,close_turntable,below_03m,below_08m}.png`;
contact sheets `captures/m4a/final/contact-sheets/kit-tall-*.png`. `below_03m` / `below_08m` are new in `CaptureKit`: from eye
height 3 and 8 m from the trunk, looking up 40 and 30 degrees at the player's pixel density, the scan beside every level,
because a tall fir is met from below (its foliage starts metres above the eye).

- **c, LOD0 (1.63x): passes.** From 3 m below it is hard to tell from the scan.
- **b, LOD0 (2.69x): coarser than the scan, still a fir.** From 3 m below the needles are longer and thicker.
- **a, LOD0 (3.71x): the weakest.** From 3 m below the needles read as blades; from 8 m below and at 12 to 70 m it reads
  as a dense, darker-green fir. Risk 1(d) (a and b on the bank only, never at LOD0) stays ready; knob 4 made this about
  1.2 times worse, so `TallFirLod0Factor = 1` is the first thing to try if frame time allows.
- LOD1 and LOD2 read as conifers at the distances they show; at 3 m they are straws (never shown there).
- All thinned levels are greener and denser than the scan's dark olive; the hand-overs between levels keep one hue.
- Close turntables (the trunk at eye height, 1.5 and 3 m): a and b show their scanned bark and dead branch stubs.

## 6. trunk_b: UVs that leave the atlas

Run 1's close turntable of b (`captures/m4a/final-run1/kit/fir_tree_01_b__close_turntable.png`) showed a band of smeared
texture at 2.5 to 3.5 m, from every side. Measured (`work/M4a/scripts/uv_range.py`, `uv_density.py`): **62 % of trunk_b's
surface, everything from 1.8 m to 12.4 m, has UVs outside [0, 1]** (u 0 to 5, v -1 to 0.41): it tiles the non-tileable scan
atlas about 35 times stretched along the trunk.

`ForestPrefabs.SplitOffAtlas` (for materials in `ForestKitSpec.OffAtlasMaterials`, now only trunk_b): triangles whose UVs
leave [0, 1], or whose texel density is under 0.1 of the root flare's, go into their own sub-mesh, drawn with the same
triplanar bark as trunk_c (`ResolveMaterials`); the kit report lists `fir_tree_01_trunk_b` under `triplanarMaterials`.
After: `captures/m4a/final/kit/fir_tree_01_b__close_turntable.png`, no band, scan base and tiled bark above; from 12 m on
the same as before (`work/M4a/look/seam/seam.png`). LOD0 of b has 4 sub-meshes (the contract allows 4).

**trunk_a was tried the same way and taken back** (`iterations/k6`): its upper trunk went from the scan's pale tan to the
darker bark with a hard seam at 3 m on every a, worse from every distance but the closest (`work/M4a/look/seam/seam.png`
columns 1 to 4), and the art direction wants pale trunks. Its strip stays: smeared only when looked at from a few metres
at the trunk above 3 m. A bark variant tinted to the scan's colour would fix both; not done (a new material).

## 7. The scene

Once the tall-fir prefabs exist, the M3 scene fails the audit row "each placement has its instance" (271 tall-fir
placements). It was rebuilt with the existing builder and planner **unchanged**, as at M3a: 2,709 placements, 2,709
instances (271 tall firs: 56 core, 215 bank; a 138, b 116, c 17), 10,617 GameObjects, 18 materials, 88 meshes, audit passed
with 0 errors and 0 warnings; grounding 12.3 mm (3 cm allowed), spawn margin 5.02 m, corridor margin 2.58 m, physical
corridors free (`out/M4a/build-report.json`). Tall-fir placement (risk 1(d), the deadfall keep-out, knob 3, sightlines and
cover) is the second half of M4 and was not touched.

## 8. Tests and checks (run 2)

| Run | Result | Evidence |
|---|---|---|
| Compile outside Unity (editor, both test assemblies) | OK, OK, OK | transcript |
| Import / kit forced / scene | Exit 0 / "34 prefabs built ... 0 failed" / audit passed | `logs/M4a-final-50..52-*.log` |
| Kit sheets | 113 sheets of 34 prefabs, `passed: true`, 0 magenta, 0 cyan | `captures/m4a/final/kit/kit-summary.json` |
| Full set (RMSE against m3-r1) | 32 views + crops, `passed: true`, no error pixel, luminance 0.190 to 0.586, near-black at most 0.046 % (hollow_rim) | `captures/m4a/final/capture-summary.json` |
| **Forest EditMode** | **35 passed, 0 failed, 0 ignored** | `out/M4a/tests/forest-editmode.xml` |
| Forest PlayMode | **9 passed, 1 failed: `Budget_Counts`**, 14,290,584 triangles at crouch_hide (budget 11.6 M) and 19,866,617 vertices (19.0 M) | `out/M4a/tests/forest-playmode.xml`, `captures/m4a/final/playmode/budget-counts.txt` |
| Greybox PlayMode | 7 passed | `out/M4a/tests/greybox-playmode.xml` |
| Logs | 0 `error CS`, 0 `warning CS`, 0 "immutable" in all eight | transcript |
| PackageCache, pre-existing files, test folders | md5 `8ce2f748...` unchanged; 59 checked, 0 differ; both test folders equal `backup/M4a-pre-edit/Tests` | transcript |

`Budget_FrameTime` passed: trail_start 10.66 ms median, p95 11.37 ms (stopwatch; FrameTiming 8 of 240 frames at 24.2 ms).
Other counts: 2,859 draw calls (estimated), 55 SetPass, 620 shadow casters, texture memory 930.3 MiB (budget 1,024, warning
line passed; the fir_tree_01 maps add 128 MiB), mesh memory 374.7 MiB (budget 400). The triangle overshoot is the unchanged
planner's 271 tall firs (crouch_hide was 10.63 M at M3 review 1), the same situation as M3a before the young firs were
placed; the budget was not raised. No 29-view cost measurement was run in this stage.

## 9. Deviations recorded here

| # | Spec | Now | Why |
|---|---|---|---|
| 1 | 4.1, 5.1: the FBX only | Positions, normals, UVs of the tall firs from the glTF geometry, checked against the imported FBX | The FBX has no UV layer (section 1), as at M3 |
| 2 | 5.4: the mask is the packed arm map | trunk_a: unbaked AO texels take the baked mean | Section 4 |
| 3 | 5.5: `noUvFallback` per sub-mesh without usable UVs | Also trunk_b's triangles whose UVs leave the atlas | Section 6 |
| 4 | 5.6.4: tall-fir LOD0 keeps the piece triangles (flatten off) | Flattened at every level | E1's finding; section 5 |
| 5 | 5.6.4 table: LOD0 foliage 160,000 / 160,000 / 90,000 | x 0.7 (knob 4) | Review 2, all three lenses |
| 6 | 5.6.4 table: scale caps 5.1 / 10 / 20 | 5.1 / 10 / 12 | Straws at 16.8x (section 5) |
| 7 | 5.6.4: wood of the farther levels clustered | LOD2 of a tall fir: trunk only | Twisted ribbon (section 5) |
| 8 | 5.6.4: LODGroup size = object height | Capped at 0.8 screen height at the first distance | Review 2 technical condition 2 |
| 9 | 12.2 kit set | Plus `below_03m`, `below_08m` for trees without a scan level | A tall fir is met from below |
| 10 | 5.5: twig smoothness | `fir_tree_01_twig` `_SmoothnessRemapMax` 0.4, like the sapling's | Same arm map |

## 10. Open, for the second half of M4 and review 3

1. **`Budget_Counts` fails** (14.29 M triangles, 19.87 M vertices at crouch_hide) with the planner's tall firs. Placement
   decides it: risk 1(d), knob 3 for the tall firs, fewer bank firs, the deadfall keep-out. Measure all 29 views.
2. **Tall a's LOD0** is the weakest level (3.71x); decide (d) on the sheets and in the scene (trunk_close and canopy_up look
   at the spawn's tall a; with (d) that fir becomes a c).
3. trunk_a's strip above 3 m (section 6); trunk_c's darker bark and bark-textured root skirt (section 3).
4. In the mist some tall firs at 20 to 50 m have rounded, pale crowns that can read as deciduous (`captures/m4a/final`,
   backlit, trail_mid_fwd); the outline is the scan's own (source column of the LOD sheets).
5. The young firs' card levels still read as broadleaf (review 2, issue 1); the tall firs avoid cards, but that decision
   for the young firs was not taken here.
6. `TwigSprites` now also maps `fir_tree_01_twig` (same atlas), so its packed mask has the sprays' AO matched; harmless, no
   tall-fir level draws the sprays.
