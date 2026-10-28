# W1 package I1: deviations from the spec, and open items

Written 2026-10-17 by the I1 engineer. Spec: `B/W1-spec.md` (revision 2). Every deviation from it is listed here with its reason
and evidence. Logs `B/logs/W1/`, outputs `B/out/W1/`, captures `B/captures/w1-*`. `B` = `SCRATCH/build`.

## 0. Status in one paragraph

**Update 2026-10-17 (I1b, section 6): all three suites pass** (forest EditMode 53 of 53, forest PlayMode 13 of 13, greybox 7 of 7,
`out/W1/tests/final3/`). The coordinator decided both open questions of section 5. The phase test now compares the pieces both levels
keep, and the new test `WindMotion_LodSwitchDoesNotPop` measures the visible motion at every tree and fern LOD switch. That test failed
on the wind-3 data (fir_sapling_medium_a LOD0 to LOD1, 1.62 px), so the wind data changed, not the test: wind-4 has a gentler phase
field. That pair is now 0.73 px, and the tightest pair overall is fern_patch_a at 0.77 px, whose data did not change. Fixing that also showed a second failure that the paragraph below missed. `WindData_IsLodInvariant`
failed on G as well (2 vertices of fir_tree_01_b LOD1, dG 0.075), at the tall firs' root flare. Wind-4 gives the root zone G 0. Kit
sheets pass the absolute rule. The static regression was recaptured on the final kit and passes (`captures/w1-static2`).

The paragraph below is I1's original status, kept as written:

The wind graphs import and compile (88 Metal variants, 0 problems), every moving material is on the graph with an `__mv` twin and the
switch back to HDRP/Lit works on the same assets, every tree, fern and grass mesh carries the wind colour in the 44 B layout, the forced
kit build is deterministic, and the static regression is within the measured run-to-run noise in both modes. Forest PlayMode (13/13) and
greybox (7/7) pass. **Forest EditMode does not pass: `WindData_IsLodInvariant` fails on the phase channel A** (section 2, item 1). That
is a conflict inside the spec between per-piece phases and the test's nearest-vertex matching; it needs the coordinator's decision.
Not done in this run (outside the computed task's deliverables): edit-mode pose sequences (`-captureSet wind`), MP4s, contact sheets,
the speckle count, the zero-wind player cost (step 10), and a conclusive drawer-exclusion count (section 4).

## 1. Shader and graphs

| # | Spec | Done | Why / evidence |
|---|---|---|---|
| 1 | Model the JSON on HDRPLit, Fern and PseudoSubsurface | Also HDRP's own `Runtime/Material/Nature/SpeedTree9_HDRP.shadergraph` | None of the three has the CustomVelocity, NormalWS, Thickness or DiffusionProfileHash blocks; SpeedTree9_HDRP (an HDRP Lit graph Unity wrote) has all four. No id or GUID copied |
| 2 | Blocks listed in the spec | Vertex: Position, Normal, Tangent, CustomVelocity (+ the two interpolators in the triplanar graph). Fragment: BaseColor, NormalWS, Metallic, Smoothness, Occlusion, Alpha, AlphaClipThreshold, Thickness, DiffusionProfileHash (unconnected). Emission, BentNormal and TransmissionMask are left out | The generator fills absent active blocks with their defaults (`SG Generator.cs:640-660`); HDRP's BentNormal field is active only when connected (`LightingSubTarget.cs:88`), so `bentNormalWS = normalWS` as on HDRP/Lit. The DiffusionProfileHash block must be present: an inline (absent) block contributes no properties, and `_DiffusionProfileHash`/`_DiffusionProfileAsset` come from it |
| 3 | (format) | Block nodes are also listed in `GraphData.m_Nodes` | First import failed: `NullReferenceException at GraphData.FindAndReportSlotErrors (GraphData.cs:3076)` (`logs/W1/graphs-1.log`); Fern lists its blocks in `m_Nodes` |
| 4 | `m_GraphPrecision 1` = Single | Written as 1 | In SG 17.6 1 is `GraphPrecision.Graph`, which a non-sub graph resolves to Single (`GraphData.cs:329-341`), as HDRP's own graphs do. The two Custom Function nodes are `m_Precision 1` (Single) so the functions are `_float` |
| 5 | `GamexHash01` = PCG2D `.x / 2^32` | `(.x >> 8) / 2^24` | A float of `.x / 2^32` rounds to 1.0 for the top values; the shifted form stays in [0, 1) |
| 6 | `#if defined(SHADERPASS) && SHADERPASS == SHADERPASS_SHADOWS` | Also `defined(SHADERPASS_SHADOWS)` | An undefined macro compares as 0, which is `SHADERPASS_GBUFFER`. Checked in the MSL: the `0.5 < ProjMatrix[3].w` test appears in ShadowCaster and not in DepthOnly or GBuffer (`out/W1/msl/ForestWind/*_Vertex_foliage_dots.txt`: 2 / 1 / 1 references to `ProjMatrix[3]`) |
| 7 | (not in spec) | `ForestWindShaderGraphs.EnsureRenderPipeline()`: one hidden 8 x 8 camera render when no pipeline instance exists | In a batch session before any render, a Shader Graph material answers `GetTag` from its fallback sub-shader (`ShaderGraphTargetId = FallbackError`) and `HDMaterial.ValidateMaterial` returns false (`logs/W1/graphs-2.log`). Called by `ForestMaterials.BuildAll` (wind mode) and `CheckShaders` |
| 8 | Public surface | Added: `MetaText`, `FixedGuid`, `SurfaceProperties`, `WindProperties`, `WriteAndCheckFromBatch` (batch entry, no menu item) | Used by the tests and the batch runs |
| 9 | Crown core on the graph: `_MetallicRemapMax 0`, smoothness remaps 0.05 | Also `_NormalScale 0` | The crown core has no normal map; the graph always samples `_NormalMap`, whose `bump` default need not decode to a flat derivative through `UnpackDerivativeNormalRGorAG`. Scale 0 gives the vertex normal, as HDRP/Lit without `_NORMALMAP`. No prefab uses the crown core today (`CrownCoreFromLod = 99`) |
| 10 | `CheckShaders` layer 3 | 44 variants per graph (4 passes x 2 stages x foliage/opaque x with/without `DOTS_INSTANCING_ON`, plus the two MotionVectors keywords), compiled with `forExternalTool = true`; output saved to `out/W1/msl/<graph>/` | The saved data is a short binary header followed by MSL text. `-forestMslOut` names the folder (default `Temp/ForestWind/msl`) |
| 11 | `precise` check (section 9) | Not done beyond the MSL dump: `fma(` count is 15 in the DepthOnly, GBuffer and ShadowCaster vertex programs | The decisive speckle count needs the displaced path, which this run did not capture |

## 2. Wind data

| # | Spec | Done | Why / evidence |
|---|---|---|---|
| 1 | A = interpolated unit phasors on the cylinder; `WindData_IsLodInvariant` asserts circular dA <= 0.05 at points matched within 2 cm | A = scalar value noise read modulo 1 (continuous everywhere), blended within 0.3 m of the trunk surface to the mean of its row (the azimuth is undefined on the axis); foliage anchors use the thinner's own rule (`wind-3`). **The test still fails on A** | Phasor sums pass through zero between antipodal corners: the synthetic test measured a 0.44-cycle jump between points 0.5 degree apart, and the kit 0.498 between matched levels. After the fix the synthetic test passes, R and G are invariant (worst dR 1/255, dG 0.075 on 2 of 51,954 points of fir_tree_01_b LOD1), but 9 to 14 % of the young firs' matched points and 1 to 6 % of the tall firs' exceed dA 0.05 (worst 0.498; `logs/W1/tests-lod.log`, `tests-final2-forest-editmode.log`). Cause: the nearest LOD0 vertex within 2 cm often belongs to a neighbouring needle, whose anchor (the needle's inner end) lies up to about 10 cm away, while the phase varies by up to a cycle per whorl (0.35 m). A kept needle keeps its anchor and its phase exactly (the thinner enlarges it about that anchor), so the pop the test guards against is limited to the dropped needles. **Decision needed** (section 5). **Decided and done in I1b (section 6).** Correction: the sentence "a kept needle keeps its anchor" holds only for pieces kept unflattened, and every kit level flattens all its kept pieces into quads or cards. The test also failed on G (2 vertices, dG 0.075, root flare), which this row did not say |
| 2 | Connectivity by `ForestFoliageThinner.FindPieces` | Own union-find, same 0.1 mm weld (`ForestWindData.ConnectedPieces`) | FindPieces switches to 6 cm spatial cells for 50,000 triangles or more when one piece holds over 5 % (`ForestFoliageThinner.cs:51-56`), which a trunk always does: "the largest connected wood piece" would be a 6 cm cell for the tall firs. Same result as FindPieces' connectivity everywhere else. The scan level reuses the baker's pieces and anchors as specified |
| 3 | Anchor = piece vertex nearest c(y) | Vertex nearest the vertical axis through the origin (the thinner's rule) | The thinner enlarges each kept piece about that vertex, so it is the point that stays put across levels |
| 4 | `MeasureTree(prefabId, wood)` | Plus an overload with the scan's top; the baker passes max y over wood and twigs | "H = top of the scan" cannot be read from the wood alone |
| 5 | Fern frond base, grass blade base | Frond base = vertex nearest the clump origin; blade base = lowest vertex; the blade's hash takes the base x and z in centimetres; fronds ranked by base azimuth | Not fixed by the spec beyond the words used |
| 6 | (public surface) | `ForestWindData.ClumpPhase(slot, seed)` | The synthetic test checks fronds against the clump's own phase (0.12 cycle), not the mean of the fronds |
| 7 | `__mv` set by the baker and the prefab builders | Set once in `ForestPrefabs.SavePrefab` for levels below `MotionVectorLevels`; the `LOD0_Shadow` proxy keeps the plain materials | Same result for every builder; a shadow-only renderer is never drawn in the motion-vector pass |
| 8 | `ForestMaterials` surface | Added `MotionVectorTwin(Material)`; in the HDRP/Lit mode a moving model's material drops the `MotionVector` override tag | Used by SavePrefab; the tag is meaningless on HDRP/Lit |

## 3. Tests (section 8.6)

| # | Test | Deviation | Why |
|---|---|---|---|
| 1 | `WindData_IsLodInvariant`, fern far level | Every far-level colour must occur exactly on the near level, instead of nearest-vertex matching | The far level enlarges its clumps, so its vertices do not lie where the near level's do: matching paired unrelated points (dG up to 0.96). Same clump in the same slot = same colours by construction; result 1,630/1,630, 1,649/1,649, 1,649/1,649 |
| 2 | `WindData_IsLodInvariant`, 1,000 matches | min(1,000, a quarter of the level's vertices) | The young firs' card level has 2,400 vertices; 655 to 902 lie within 2 cm of LOD0 |
| 3 | `WindData_LayoutAndChannels`, "G = 0 within the trunk radius" | Checked on trunk-material vertices between 0.5 and 1.5 m: at most 5 % above G 0.05 | The trunk radius is not stored in the mesh; those vertices lie on the trunk surface |
| 4 | `ForestWindProbeSupport` | `RunGust(shader, state, xzt[])`, `RunDeform(shader, state, instance, field, ...)`; globals obtained by `ForestWindGlobals.Write`, read back with `Shader.GetGlobalVector`, set on the compute shader, then `Clear` | Keeps one source for the global vectors without adding API |
| 5 | `FoliageMaterial_MatchesHdrpLeafSample` (adapted as specified) | On the wind graph `_NORMALMAP` is replaced by "the `_NormalMap` slot holds the manifest's normal map" | As spec 8.6 |

## 4. Verification deviations

| # | Spec | Done | Evidence |
|---|---|---|---|
| 1 | Kit capture `w1-kit` within 1.2 x the noise of `w1-pre-kit2` | The kit sheets are deterministic (w1-pre-kit2 against w1-pre-kit: at most 0.002 % of pixels differ, by 1/255), so the noise reference is 0 and no change can meet rule 1. Measured instead: static props bit-identical; wind prefabs at most 0.137 % of pixels differ (fir_tree_01_c__foliage), largest channel difference 8/255 (grass_patch_a turntable), no pixel over 8/255 anywhere; the difference images are isolated pixels without structure (looked at: `out/W1/kit-diff/fir_sapling_medium_c__foliage_diff8.png`) | `out/W1/regress-kit.txt`, `out/W1/kit-imgdiff.txt`, `kit-noise-imgdiff.txt`. Likely the Float16 tangents and the reordered surface arithmetic; a Lit-mode kit capture to separate the two was not made. **Decided in I1b: absolute rule, passes (section 6.3)** |
| 2 | Static regression: structure looked for in each difference image | Also an 8 x 8 block-mean signed-luma map (`tools/w1/regress/blockdiff`), which averages the film grain out: block RMS of w1-static equals that of a noise run within 2 % on the 8 views checked | `out/W1/static-block/` |
| 3 | Drawer exclusions 0 (D1's tool) | Inconclusive. D1's tool reads culling statistics, not exclusions; a temporary script read the drawer's `GRDDebugStats` by reflection and got 0 on the drawer path, 0 excluded, 0 MissingDOTSInstancing: the advanced statistics are not populated in an edit-mode batch render, so this proves nothing | `logs/W1/drawer-2.log`. Indirect: Budget_Counts still reads the drawer's draw commands (668 estimated draws at crouch_hide) |
| 4 | Edit-mode pose sequences, MP4s, contact sheets, speckle count, player cost (steps 8 and 10), tools `tstd`, `mp4`, `paired`, `build-player` | Not done | Outside this run's deliverables; `ForestCapture.cs` is unchanged (`-captureSet wind` not implemented) |

## 5. Decisions for the coordinator

1. **Phase invariance (section 2, item 1).** Options: (a) keep per-needle rigid phases and change the test to compare kept pieces (anchor
   vertices that coincide on both levels, or matched points weighted by G); (b) evaluate A per vertex with a gentler field (lattice values
   over half a cycle, slope at most about 2.1 cycles per metre), which can meet 0.05 at 2 cm but lets needles flex slightly and halves the
   phase range within a tree; (c) a larger phase cell. Until decided, forest EditMode is 51 of 52.
2. Kit-capture rule 1 needs a non-zero noise floor or an absolute rule for deterministic sheets (section 4, item 1).

Both were decided on 2026-10-17. Section 6 records the decisions and what followed.

## 6. I1b: the coordinator's decisions of 2026-10-17, and what followed

Written 2026-10-17 by the I1b engineer. Logs `B/logs/W1/` (`tests-fix1` to `fix3`, `diag-1`, `build-7`, `build-8`, `static-5`,
`kit-capture-3`, `tests-final3-*`, transcript `i1b-final.txt`). Outputs `B/out/W1/`. The script is `B/tools/w1/i1b-final.sh`.

### 6.1 Phase invariance: option (a) plus a displacement check

**Decision.** What must not pop at a LOD switch is visible motion. Nearest-point matching across different needles measures something
that is not a visible defect. So:

1. The nearest-point check of R and G (and B on the fern far level) stays exactly as it was.
2. A is compared only on the pieces both levels keep.
3. A new test, `WindMotion_LodSwitchDoesNotPop`, checks the motion itself.

The coordinator called this a correction of a mis-specified test, not a loosening. If the motion check failed, the wind data or the
phase field had to change, not the test.

**`WindData_IsLodInvariant`, as built.**
- Nearest-vertex matching within 2 cm, the 1,000-match rule and the R and G limits (`dR <= 2/255`, `dG <= 0.06`) are unchanged.
- dA at those points is still computed and reported, but no longer asserted.
- New: `ComparePhaseOfKeptPieces`. For each foliage piece of a level, its anchor is the vertex nearest the vertical axis through the
  origin. This is the rule of the thinner and of `ForestWindData`, with pieces welded at 0.1 mm. When that anchor coincides with a LOD0
  foliage anchor within 0.01 mm, the two phases must agree within 1/255 (float tolerance after 8-bit quantisation).
- New guard: the test fails if no piece of any tree level is compared, so the check cannot silently compare nothing.
- The worst-dG pair is now reported with its positions and materials, which is how the root-flare defect of 6.2 was found.

**Finding: the decision's premise holds only for the tall firs.** "A needle the thinner keeps keeps its exact phase" assumed a kept
piece keeps its anchor. But every kit level flattens all its kept pieces into quads (kit log: `27500 of 71126 pieces (27500 as quads)`,
and likewise for every level), or replaces them with twig cards (young-fir LOD2 and LOD3). A quad's corners are
`anchor + (fitted corner - anchor) x s`, with `s` the level's enlargement. So the level's own anchor, its corner nearest the axis, moves
with `s`. Measured (`out/W1/lod-invariance-final3.txt`):
- **Young firs:** 0 coinciding anchors on every level (scan against quads and cards).
- **Tall firs:** 4,077 of 35,000, 969 of 6,000, 2,059 of 18,000, 705 of 4,500, 943 of 12,000 and 279 of 2,000 pieces per level
  coincide. These are the pieces whose enlargement the crown's envelope caps alike on both levels. Every one has exactly the same
  phase (worst dA 0).

So for young firs the phase of the same needle across levels is covered only by the motion test. Making it exact everywhere would need
the thinner to report each quad's source piece, so that `ForestWindData` could take the phase at the source anchor. That was not done:
the measured motion differences below are well under a pixel without it.

**`WindMotion_LodSwitchDoesNotPop`, as built.**
- **Displacement.** The shader's own `GamexWindDeform`, run by `ForestWindProbe.compute`'s `Deform` kernel. The decision text said
  "the CPU mirror of the shader's displacement (the one `WindShader_GustMatchesCpuOnGpu` checks)", but no CPU mirror of the
  displacement exists: `ForestWindGlobals.SampleGust` mirrors only the gust. The probe runs the shader's code itself, which is the
  stricter choice. Deviation recorded here.
- **Pairs.** Each adjacent pair (LOD k to k+1) of every young fir, tall fir and fern patch. Fern clumps and grass have one level only.
- **Matching.** Each vertex of level k+1 to the nearest level-k vertex within 2 cm, in prefab space. The test then compares
  `s x |(moved - rest) far - (moved - rest) near|`: displacement vectors, so the existing static difference between the two levels'
  geometry is not counted.
- **Instances.** For each prefab, the smallest, median and largest instance in `placements.json` (pivot, yaw, scale). A small instance
  switches nearer, where the distance fade leaves more motion.
- **Camera.** At the switch distance from the pivot: `LODGroup.size x s / (2 tan 35 deg x screenRelativeTransitionHeight[k])`, the
  formula the kit builds the LOD groups with (lodBias 1). The shader's distance fade then applies as in the game.
- **Wind.** `calm = peak = 0.875`, the strongest any preset reaches: OvercastMist's peak 0.70 with the controller's +25 % wander of spec
  2.7. The value is computed from `ForestLightingPresets`, so it follows a retune.
- **Times.** 16 instants from 0.25 to 10.6 s (several sway periods of every class).
- **Limit.** The 95th percentile, over all matched points and times pooled, must be at most one pixel at that switch distance:
  `d x 2 tan 35 deg / 1440` (2,560 x 1,440, 70 degree vertical field of view). The worst value and the share above 1 px are reported.
- **Positive control.** Inside the animation distance, something must move.
- **Sample guard.** At least 100 matched points (1,600 samples), not the 1,000 of the invariance test. Young-fir LOD2 and LOD3 are both
  card levels (cells of 0.118 and 0.35 m) and share only 224 to 331 points within 2 cm.

**Result on the wind-3 data (`tests-fix2.log`): fail.** fir_sapling_medium_a LOD0 to LOD1 was 1.62 px at all three instance scales,
with 8.3 % of samples above 1 px. b and c reached 0.96 and 0.89 px; everything else was at most 0.77 px. Cause: the phase field rose by
up to about 4.3 cycles per metre vertically (values over a whole cycle on 0.35 m cells). Needle anchors a few centimetres apart, which
the scan-to-quad switch trades for each other, therefore bobbed out of phase.

**Change to the wind data (wind-4), not to the test.** `ForestWindData.Phase` lattice values now lie over half a cycle
(`PhaseRange = 0.5`), and the cells are twice as tall (`YoungPhaseCell` 0.35 to 0.70 m, `TallPhaseCell` 0.60 to 1.20 m). Still 8
sectors and still a rigid per-piece phase. The steepest vertical slope falls from 4.3 to 1.07 cycles per metre for young firs, and
from 2.5 to 0.63 for tall firs. Azimuthally it halves.

**Result on the wind-4 data (final run `tests-final3-forest-editmode.log`, table `out/W1/lod-pop-table.md`; worst instance per pair):**

| Pair | wind-3 p95 | wind-4: worst instance (scale, switch, 1 px) | wind-4 p95 | wind-4 worst | above 1 px |
|---|---|---|---|---|---|
| fir_sapling_medium_a LOD0 to LOD1 | **1.62 px** | 1.297, 5.2 m, 5.04 mm | 3.67 mm = **0.73 px** | 40.8 mm = 8.1 px | 3.1 % |
| fir_sapling_medium_a LOD1 to LOD2 | 0.66 px | 1.297, 18.2 m, 17.65 mm | 4.51 mm = 0.26 px | 25.8 mm = 1.5 px | 0.2 % |
| fir_sapling_medium_a LOD2 to LOD3 | 0.01 px | 0.692, 29.0 m, 28.25 mm | 0.09 mm = 0.00 px | 0.3 mm | 0 % |
| fir_sapling_medium_b LOD0 to LOD1 | 0.96 px | 0.550, 2.2 m, 2.14 mm | 0.88 mm = 0.41 px | 22.6 mm = 10.6 px | 1.3 % |
| fir_sapling_medium_b LOD1 to LOD2 | 0.35 px | 0.550, 7.7 m, 7.49 mm | 1.10 mm = 0.15 px | 10.8 mm = 1.5 px | 0.0 % |
| fir_sapling_medium_b LOD2 to LOD3 | 0.16 px | 0.550, 23.1 m, 22.47 mm | 1.30 mm = 0.06 px | 5.5 mm | 0 % |
| fir_sapling_medium_c LOD0 to LOD1 | 0.89 px | 0.676, 2.7 m, 2.63 mm | 0.86 mm = 0.33 px | 16.6 mm = 6.3 px | 0.8 % |
| fir_sapling_medium_c LOD1 to LOD2 | 0.33 px | 0.550, 7.7 m, 7.49 mm | 0.81 mm = 0.11 px | 7.0 mm = 0.9 px | 0 % |
| fir_sapling_medium_c LOD2 to LOD3 | 0.15 px | 0.550, 23.1 m, 22.47 mm | 1.14 mm = 0.05 px | 5.5 mm | 0 % |
| fir_tree_01_a LOD0 to LOD1 | 0.12 px | 1.065, 9.6 m, 9.32 mm | 1.00 mm = 0.11 px | 44.1 mm = 4.7 px | 0.3 % |
| fir_tree_01_a LOD1 to LOD2 | 0.08 px | 0.806, 24.2 m, 23.52 mm | 0.83 mm = 0.04 px | 33.5 mm = 1.4 px | 0 % |
| fir_tree_01_b LOD0 to LOD1 | 0.62 px | 0.623, 5.6 m, 5.45 mm | 1.23 mm = 0.23 px | 22.0 mm = 4.0 px | 1.0 % |
| fir_tree_01_b LOD1 to LOD2 | 0.06 px | 0.623, 18.7 m, 18.18 mm | 0.74 mm = 0.04 px | 29.5 mm = 1.6 px | 0 % |
| fir_tree_01_c LOD0 to LOD1 | 0.32 px | 0.626, 5.6 m, 5.48 mm | 0.88 mm = 0.16 px | 28.7 mm = 5.2 px | 0.4 % |
| fir_tree_01_c LOD1 to LOD2 | 0.06 px | 0.626, 18.8 m, 18.25 mm | 0.78 mm = 0.04 px | 37.1 mm = 2.0 px | 0.1 % |
| fern_patch_a LOD0 to LOD1 | 0.77 px | 0.750, 12.0 m, 11.68 mm | 9.01 mm = **0.77 px** | 29.4 mm = 2.5 px | 3.7 % |
| fern_patch_b LOD0 to LOD1 | 0.30 px | 0.750, 12.0 m, 11.67 mm | 3.45 mm = 0.30 px | 26.2 mm = 2.2 px | 0.5 % |
| fern_patch_c LOD0 to LOD1 | 0.73 px | 0.750, 12.0 m, 11.67 mm | 8.49 mm = 0.73 px | 33.5 mm = 2.9 px | 3.6 % |

Instances whose switch lies beyond the 30 m animation distance give 0 by design: the shader's per-instance branch returns the rest pose
there (for example fir_tree_01_a LOD1 to LOD2 at scales 1.065 and 1.250, switches at 31.9 and 37.5 m).

**What the next engineer and the owner should know.**
- **Variety within a tree is smaller.** Branch phases inside one tree now span at most half a cycle, and branches within about a metre
  move nearly together. Trees still differ from each other through the per-instance phase (spread 1 for firs). This has not been
  judged on moving images: the pose sequences and MP4s are I2's or the coordinator's next step. If the crowns look too uniform, the
  lever is a wider range with larger cells, never a looser test.
- **The fern pair is now the tightest (0.77 px).** Its data did not change. The far level enlarges its clumps, so matched points
  belong to different frond positions, and their displacement differs nearly as much as the motion itself (p95 9.0 mm against a motion
  p95 of 12.5 mm). It is below a pixel only because fern motion is small at the 12 to 25 m switch. A stronger fern profile would need
  the far level's G and phase taken at the near frond's matching point.
- **Single matched points still differ by several pixels (worst 10.6 px, young b at 2.2 m).** These are neighbouring needles on
  opposite sides of a phase gradient, or of a G difference. 0.0 to 3.7 % of samples per pair exceed 1 px.

### 6.2 A second failure the I1 report missed: G at the tall firs' root flare

`WindData_IsLodInvariant` also failed on G before this step. I1's own log line reads `fir_tree_01_b LOD1 ... over the limits R 0, G 2,
A 2449` (`tests-final2-forest-editmode.log`), but the report said "only on the phase channel A". The worst pair
(`tests-fix2.log`) was a LOD1 `fir_tree_01_trunk_b` vertex at (0.554, 0.102, -0.114) with G 19/255, 1 mm from a LOD0 root-skirt vertex
with G forced to 0.

**Cause** (temporary diagnostic, copy in `B/tools/w1/W1DiagTemp.cs`, output `out/W1/diag-trunk-sections.txt`). Below 0.25 to 0.35 m,
a tall fir's trunk piece is a tangle of roots and flare:
- each section is several loops or lobes, with an extent 1.4 to 3.2 times its perimeter radius;
- its centroid wanders by up to 0.4 m;
- so `rho - r_t` gave root vertices G up to 27/255.

That is more than a test problem. Root vertices would bob by up to about 9 mm at the strongest wind, against a skirt that stays still.
I2's push weight `saturate(5 G^(2/3))` would also give them about 0.9, so a passing body would have bent a tall fir's root flare by up to
half a metre. Young-fir trunks are round from the foot (extent within 1.03 of the radius) and have real branches from 0.1 m up, so a
fixed height ramp was not acceptable.

**Change (wind-4).** `MeasureTree` measures a root zone on the trunk piece:
- It samples the section every 0.05 m from the foot up.
- The root zone ends at the first height from which the section stays one round loop (extent between 0.9 and 1.25 times the perimeter
  radius) for 0.5 m.
- If the trunk is round from its foot, there is no root zone.

G is multiplied by `smoothstep(rootTop, rootTop + 0.25 m, y)`. Measured (`build-8.log`): fir_tree_01_a below 0.25 m, b and c below
0.20 m, and no root zone on any young fir. Constants: `ForestWindData.Root*`, all part of the kit hash. Result: the worst dG on every
tree level is now 0.012 or less, on twigs, against the limit of 0.06 (`out/W1/lod-invariance-final3.txt`).

### 6.3 Kit-capture rule 1: the absolute rule

**Decision.** For the deterministic kit sheets: RMSE at most 0.002 and no error pixels against the pre-W1 sheets (`captures/w1-pre-kit`,
rendered with wind 0).

**Measured.** The kit was recaptured on the final wind-4 kit into `captures/w1-kit2` (`kit-capture-3.log`). Results are in
`out/W1/regress-kit2.txt` and `out/W1/kit-absolute.txt`; the tool is `tools/w1/kit-absolute.py`.
- All 113 sheets pass. The worst RMSE is 0.00009 (fir_sapling_medium_c__foliage) and there are 0 error pixels.
- The wind-3 capture `w1-kit` also passes the same rule (`out/W1/kit-absolute-wind3.txt`, same worst value).
- Against `w1-kit`, 111 of 113 sheets are byte-identical. The two close turntables of young firs a and c differ on 0.0003 % and
  0.0001 % of pixels, by at most 4/255 and 1/255 (`out/W1/kit2-vs-kit/imgdiff.txt`). That is consistent with `Mesh.Optimize` ordering
  the scan level's vertices differently once their colours changed.

### 6.4 Static regression, rebuild and suites on the final data

- **Rebuild.** Forced kit and scene builds 7 and 8 give 174 identical mesh, prefab and material files. Wind-4 changed exactly the 21
  tree meshes against build 6; fern, grass, prefab and material files are unchanged. The audit passed, and the placements are
  byte-identical to N1.
- **Static regression.** `captures/w1-static2` against `n1-final` (`out/W1/regress-static2.txt`): 32 of 32 views pass.
  - Worst RMSE ratio: 1.02 times the noise reference (boundary_e, trunk_close; the limit is 1.2).
  - Worst lit-mask bias: 0.00002 over its reference (the limit is 0.002). Error pixels: 0.
  - All 32 views and their difference maps were looked at on contact sheets (`work/W1/look/static2_contact.png`,
    `static2_diff_contact.png`), plus fir_close, trunk_close, boundary_e and crouch_hide at larger size. They show grain whose
    amplitude follows brightness, the same as a noise run of the unchanged project (`w1-pre` against `n1-final`, fir_close heat maps
    side by side).
  - The 8 x 8 block-mean RMS equals the noise run's within -1.5 to +3 % on the 8 views checked (`out/W1/static2-block/summary.txt`).
  - `w1-static-lit` was not recaptured. Wind-4 changed only the colour bytes, which HDRP/Lit does not read; the byte-identical kit
    sheets bear this out.
- **Suites** (`out/W1/tests/final3/`): forest EditMode 53 of 53 (52 plus the new test), forest PlayMode 13 of 13, greybox 7 of 7.
  - 0 `error CS`, 0 `warning CS`, 0 "immutable" and 0 `Shader error in` in every log of the sequence.
  - Materials are untouched by the tests, and `pre-W1.md5` holds.
  - No temporary file is left in the project.

### 6.5 Files changed in I1b

Originals of the edited editor files are in `B/backup/W1-I1b-pre-edit/`.
- `Editor/Forest/ForestWindData.cs`: wind-4 phase field and root zone, `TreeField.RootTop`.
- `ForestKitSpec.cs`: the new constants in the hash.
- `ForestTreeBaker.cs`: one log line per tree with its root zone.
- `Tests/ForestEditMode/ForestWindTests.cs`: the phase check of kept pieces, the worst-dG report, `WindMotion_LodSwitchDoesNotPop`.
- Generated: the 21 tree meshes.
