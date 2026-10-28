# M2, review round 1: what the reviewers found, what was changed, what is left

Written 2026-10-13 by the engineer who handled review round 1 of `captures/m2-r0`. Unity 6000.6.0f1,
HDRP 17.6.0, Metal, batch mode. Paths are relative to `build/` unless they start with `Assets/`.
New captures: `captures/m2-r1/` (iterations under `captures/m2-r1/iterations/`). Diffs: `out/M2r1/*.diff`.
Originals: `backup/M2r1-pre-edit/`. No test file, audit rule, budget or threshold was changed.

## 1. Verdict on the critical and high findings

All eleven high findings are real; none was rejected. I looked at the M2 pictures, ran the debug set on
the M2 scene first (`captures/m2-r1/iterations/r0-debug/debug/`: the clay view shows a sooty blot under
every fern and tuft and dark whole rocks, which is screen-space AO at a 2 m radius) and measured where a
number was claimed.

| # | Finding | Verdict | State after this round |
|---|---|---|---|
| 1 | Litter tile repeats on floor and bank (three reviewers) | Real | **Reduced, not removed.** The bank no longer shows a grid (`work/M2r1/final-tiling-compare.png`, top row). From above a weave is still visible inside the dry-needle patches and faintly in litter. The full fix is the anti-tiling terrain graph: owner decision (risk 13) |
| 2 | Floor reads as a dry, bare, orange moor | Real | **Partly.** Floor is darker and cooler (lower band of `trail_start`: linear 0.075/0.048/0.025 to 0.055/0.043/0.022) with a damp sheen; ferns reach 50 m. It is still a treeless brown heath: the fern cap was not raised (owner decision, a test holds it) |
| 3 | Props near-black on a bright floor (two reviewers) | Real | **Fixed for rocks and logs** by eye and by measurement of the spawn boulders and logs; see section 3 for what the numbers can and cannot show |
| 4 | Clearing does not read as grass / as a place (two reviewers) | Real | **Partly.** Leafy tussocks in three swards on a pale needle bed, green under them. Still tufts with ground between, not a sward. The 2,500-triangle patch and about 220 patches cannot give more |
| 5 | Hero boulder soft at 1 m, knife-edge contact | Real | **Not fixed (softness).** It is texel density (a 4k atlas shared by seven rocks, at scale 1.75) and needs the detail map of M6: owner decision. The contact line is better: large rocks are now bedded where the ground curves away |
| 6 | Ferns are lone rosettes, no drifts | Real | **Fixed.** Median nearest neighbour 2.33 to 1.62 m, 10th percentile 1.88 to 1.31 m, Clark-Evans index 1.05 to 0.85 (clustered) |
| 7 | Fern hollow landmark missing | Real | **Fixed.** 19 to 107 patches within 11 m; ragged mud; new eye-level view `hollow_rim` |
| 8 | North outcrop is a row with gaps, trail ends nowhere | Real | **Partly.** The nine rocks are one connected mass with the two tall shapes behind (`captures/m2-r1/look/sheet-outcrop.png`). They still do not hide the bank: nothing in the kit is taller than 2 m. The apron under the trail's end is faint |
| 9 | Ground repeats as a grid on bank and floor (composition) | Real | As row 1 |

## 2. What was changed (seven source files and the manifest)

| File | Change | Deviation from the spec |
|---|---|---|
| `ForestLighting.cs` | SSAO intensity 1.5 to 1.0, radius 2.0 to 1.0 m | 8.3 |
| `ForestLightingPresets.cs` | Fog `meanFreePath` 80 to 60 m (11 % contrast left at 130 m, was 20 %; 63 % at 28 m, was 70 %) | 8.2 |
| `ForestMaterials.cs` | Terrain layer tints as colours: litter (0.74, 0.78, 0.82), mossy ground (0.74, 0.84, 0.62), gravel 0.72, dry needles 0.72, painted rock (0.86, 0.82, 0.72). Minimum smoothness on six layers (0.10 to 0.30). Normal scale 1.3 to 1.4 on gravel, litter, stony soil. `_HeightTransition` 0.04 to 0.015. Log colour 0.85 to 1.0 (no tint). Rock set 01 tinted (0.80, 0.92, 1.0) toward set 02. Grass tinted (1.15, 1.40, 1.0). `ToolVersion` `materials-2`. The pipeline's default terrain material is no longer left dirty (section 5) | 5.5, 6.3 |
| `forest-asset-manifest.json` | `forest_leaves_04` `tileSizeMeters` 1.5 to 2.0, with a note | 6.3 (the scan is 1.5 m; needles are drawn 1.33 times life size) |
| `ForestKitSpec.cs` | Fern patch: clumps within 0.7 m (was 1.0) at scale 1.1 to 1.6 (was 0.85 to 1.3), far scale 1.6. Fern LODs 14 / 50 m (was 16 / 40). Grass patch: leafy clumps only, 2.3 to 2.9 times scan size (2,421 and 2,372 triangles). Dry branch LOD0 casts shadows. `ToolVersion` `kit-3` | 5.6.4, 5.6.5 |
| `ForestLayout.cs` | Fern thinning floor 0.36 to 0.8, narrowing up to 0.6 with steeper drift edges; hollow fern floor; collar and shoulder constants; grass swards (spacing 0.52 m, threshold 0.78, cap 205); rock rim sink; branch sink 0.05 m; log bedding rule; outcrop as a pile; deadfall staggered with the middle trunk end for end; `rock_close` moved to the anchor's lit side and the anchor's yaw fixed; new viewpoints `hollow_rim` and `deadfall` | 3.4, 7.2, 7.3, 12.2 |
| `ForestScatter.cs` | Fern density: collar on half the solids, one side only, or where a drift passes; shoulder runs; hollow floor exempt from the cap; no patch within 2.6 m of the `ground_close` camera. One rock set per cluster. Logs rejected where the ground under their ends is off the pivot's line by more than 0.15 m. Fragment branch `_b` only in the ring rule. Rock sink also follows the ground under the rim. Two log lines say what the fern and grass solvers chose | 7.2, 7.3 |
| `ForestTerrain.cs` | Paint: moss and needle patches wider; humus and stony soil over the open floor away from the trail; bank painted mostly stony soil with litter at 0.35; painted rock ramp 32-42 to 36-46 degrees (none on the bank); ruts break up (0.45 to 1.0 at 2.5 m), puddle mud 0.25 to 0.5 in separate puddles; gully gravel 0.7 to 0.4 with both ends faded; hollow mud 0.9 to 0.65 with a ragged edge and humus around it; moss under rocks, logs and grass patches; clearing as a needle bed; an apron of stony soil and gravel at the trail's north end | 6.4 |

The clearing's grass now covers about 15 % of the disc, not the 25 % of 7.2: at the class count of 7.2
(about 220) a quarter of the disc is one patch per 0.65 m2, which read as scattered tufts.

## 3. What was measured

| Question | M2 (`m2-r0`) | Now (`m2-r1`) | How |
|---|---|---|---|
| Fern patches, nearest neighbour p10 / p50 | 1.88 / 2.33 m | 1.31 / 1.62 m | `out/M2r1/measurements/plan-stats.txt` |
| Clark-Evans index of core fern patches | 1.05 (the reviewer's figure) | 0.85 | final `placements.json`; appended to `plan-stats.txt` |
| Fern patches within 11 m of the hollow centre | 19 (the reviewer's figure) | 107 | same |
| Solids with no fern patch within 1.5 m | 0 of 107 (the reviewer's figure) | 20 of 106 | same. Fewer than the half intended: drifts still reach most pieces |
| Fern patches spent within 1.5 m of a solid | 373 of 900 (the reviewer's figure) | 252 of 877 | same |
| Ground band of `trail_start` (rows 760 to 1440), linear RGB | 0.075 / 0.048 / 0.025 | 0.055 / 0.043 / 0.022 | `work/M2r1/pngstats` on the PNGs |
| Mean luminance `trail_start` / `clearing` / `ground_close` | 0.413 / 0.407 / 0.259 | 0.405 / 0.405 / 0.231 | capture sidecars. The exposure gate (0.45) keeps its margin |
| Rocks with a sink | few (rule gives 0 for most shapes) | 74 of 88, largest 0.24 m, mean 0.09 m | `placements.json` |
| Triangles / shadow casters / frame time at `trail_start` | 2.67 M / 214 / 7.85 ms | 2.73 M / 183 / 7.73 ms | `captures/m2-r1/playmode/*.txt` |

**What the rock numbers do not show.** The reviewers measured the boulder face in `rock_close` at a median
of 23 to 25 of 255. The same view now reads 51, but the viewpoint moved from the shaded undercut to the lit
face, so that is not a like-for-like number. The like-for-like evidence is by eye: the spawn boulders in
`trail_start`, the logs in `deadfall` and `inspect/log_*`, and the kit sheets, where rock and wood now show
their scan colour beside the ground instead of a silhouette.

**Mips are not the cause of dark distant rocks.** A third of the rock atlases is black background, so I
checked whether lower mips bleed it into the islands: sampled under the UVs, base colour and AO hold to
32 px (`work/M2r1/mips/`, numbers in the log of that step). They do not.

## 4. Not fixed, and why

1. **The tile is still visible from above** in the dry-needle patches (rows of orange dashes at its 2.0 m
   tile) and faintly in litter. I did not enlarge the needle tile: it would have been a second scan drawn
   larger than life. The anti-tiling graph stays the owner's decision.
2. **The hero boulder is soft at 1 m.** Detail maps on rock are an M6 item that needs approval. I did not
   take the reviewer's fallback (cap boulder-field scale at 1.4): spec 3.4 asks for 1.3 to 1.8 there.
3. **The clearing is still thin** (section 1, row 4). Grass still ends 25 m from the camera.
4. **The fern cap of 900 was not raised**, and no third fern level was added.
5. **The bowl.** The bank is still one even rim on every horizon; its shape was not touched. It is the
   place of the fir wall (M3).
6. **Rock set 01 still has no AO map** (full AO in its mask, as at M2). Fetching Poly Haven's separate AO
   file needs a manifest entry read from the `/files` API; not done in this round.
7. **Faint vertical streaks on the bank** (two low findings): not investigated, no fog-off A/B was run.
8. **Not taken up from the medium list:** trail-side rocks along the whole trail and a tall anchor per
   cluster (crouch cover), tighter cluster spacing, rut width modulation, more shoulder grass, the
   `_AORemapMin` on wood materials, fog base height.

## 5. Found on the way

1. **A file in the package cache was rewritten once, and put back.** The first import of this round
   rebuilt the terrain material in place for the first time. `CopyPropertiesFromMaterial` from the
   pipeline's default terrain material left that default marked dirty, and `SaveAssets` rewrote
   `Library/PackageCache/com.unity.render-pipelines.high-definition@03bdf33c5697/.../DefaultHDTerrainMaterial.mat`
   (one added line, `m_AllowLocking: 1`; log `logs/M2r1-iter01-import.log`, "immutable"). I copied the
   original back from the Unity install (md5 `8ce2f7482ff8fb845c1b94b0082b4cf0` again; the altered copy is
   `out/M2r1/DefaultHDTerrainMaterial.altered-by-import.mat`) and fixed the cause in
   `ForestMaterials.BuildTerrainMaterial` (the dirty mark is taken back, as `ForestTerrain.Create` does).
   Proof: a forced rebuild of every material afterwards (`logs/M2r1-iter04-import-forced.log`) leaves the
   md5 unchanged and logs no warning. The next launch after the restore logged the warning once more
   (`M2r1-iter01-debug.log`): Unity noticing that the file had changed back. The nine final logs have none.
2. **Two close-ups of the inspection probe look broken and are not.** `inspect/stump_a__tree_stump_01__02109.png`
   (and, in the run before the rock sink, `rock_big03__...__01697.png`) show dark shards in front of the lens.
   The probe puts its camera at a fixed offset from its target, and there it stands inside a neighbouring
   boulder. Shown with a second temporary probe that renders the same pose with the boulders within 3 m
   hidden: the shards are gone (`captures/m2-r1/look/sheet-probes.png`, made on the build before the rock
   sink was added). No player camera can be there (the rocks have colliders). In the final set
   `rock_big03__...__01697.png` is a blurred rock surface filling the frame, for the same reason.
3. **`tools/unity.sh` refuses to start when the calling command line itself contains the text it greps
   for.** A shell command that runs `pgrep -f "Unity.app/Contents/MacOS/Unity"` and then `unity.sh` in one
   line makes `unity.sh` see the shell's own command and report "EDITOR OPEN". It happened twice here; no
   editor was open. Run the check and the launch as separate commands.

## 6. Temporary files that were in the project during single runs

`build/tools/inspect/ForestInspectTemp.cs` (as at M2) and the new `build/tools/inspect/ForestLookTemp.cs`
were each copied into `Assets/Game/Editor/Forest/` for one run and removed with their `.meta` files.
The folder holds its 27 source files again, and every later run compiled without them.
