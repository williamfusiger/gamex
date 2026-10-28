# M0 integration: what was changed, and what Unity turned out to do

Written 2026-10-13 by the integrator. Unity 6000.6.0f1, HDRP 17.6.0, Metal, Apple M4 Max, batch mode.
Everything below was run in Unity; the log named with each item is under `build/logs/`.

No mismatch between the five packages had to be fixed: the 27 editor files and both test assemblies
compiled together as delivered, outside Unity and inside it (0 errors, 0 warnings).

## 1. Changes to forest files (six files)

| # | File | What | Why | Evidence |
|---|---|---|---|---|
| 1 | `ForestAssetPostprocessor.cs` | `SkyCompression` is `CompressedHQ` (BC6H); `Version` 1 to 2 | Spec 5.3: "Probe P8 tests `CompressedHQ`; if it is clean, switch." P8 found it clean (RMSE 0.0012 against the uncompressed import). The probe also found the cubemap faces are 2048, not the 1024 the spec assumed, so the uncompressed sky costs about 268 MiB, not 67; BC6H costs about 34 | `M0-03-probe.log`, `M0-08-probe.log`, `captures/M0/probe/probe.json` item P8 |
| 2 | `Tests/ForestEditMode/ForestKitTests.cs` | `Textures_FollowPolicy` expects `CompressedHQ` on `__sky.hdr` | The test's own comment says the import rule and this line change together once P8 is clean. The check is as strict as before, on the new rule | `out/M0/tests/forest-editmode.xml` |
| 3 | `ForestAudit.cs` | `material.pipeline` check: new `UsesHdrpShader`. It asks the engine as the spec says (`material.GetTag("RenderPipeline", false, "")`); only while no render pipeline instance exists does it read the shader's own sub-shader tags instead | Before the first render of an Editor session `RenderPipelineManager.currentPipeline` is null and `GetTag` answers `""` for every HDRP/Lit and TerrainLit material (measured). The audit renders nothing, so a scene build or a test run in a fresh Editor reported all ten kit materials as "not an HDRP shader". After a render `GetTag` answers `HDRenderPipeline` (measured), and then the spec's check is the one applied | Failure: `M0-09-forest-editmode.log`. Values: `M0-10-editmode-materials-diag.log`, `M0-10-gettag-after-render-diag.log`. Pass: `M0-11-forest-editmode.log` |
| 4 | `ForestProbe.cs`, P7 | The GPU side of the comparison has two routes. The Inspector's blit of `Hidden/HDRP/IntegrateHDRI` is tried first (as a blit and as a full-screen triangle, each against a marker so a draw that does not happen is seen). If it draws nothing, the sky is rendered in Lux mode with the CPU value and the lux it delivers is read from HDRP's dynamic ambient probe | The integration shader draws nothing in batch mode on this machine. The pass is compiled, the pipeline exists, the cubemap holds data, a plain blit into the same target works; the cause was not found. The first run therefore answered "no (GPU 0)", which said nothing about the CPU value | `M0-03-probe.log` (GPU 0), `M0-04-probe-diag-P7*.log`, final: `M0-08-probe.log` |
| 5 | `ForestProbe.cs`, P8 | The temporary copy is imported the other way from the project's sky, so the comparison stays uncompressed against BC6H after change 1 | Otherwise P8 would compare BC6H with BC6H from now on | `M0-08-probe.log` |
| 6 | `ForestProbe.cs`, P10 and `ForestTerrain.cs`, `Create` | After `Terrain.CreateTerrainGameObject`, the dirty mark on the pipeline's default terrain material is cleared (only if it was clean before) | `Terrain.CreateTerrainGameObject` assigns `DefaultHDTerrainMaterial` and marks it dirty (measured: `EditorUtility.IsDirty` false before, true after). The next save of assets then rewrote that file in `Library/PackageCache` and Unity warned "asset(s) located in immutable packages were unexpectedly altered". The rewritten file differed by one added line (`m_AllowLocking: 1`) | Warning: `M0-03-probe.log:780`. Bisect: `M0-06-probe-diag-*.log`. Fixed: `M0-06-probe-diag-dirty-fixed.log`, `M0-08-probe.log` (no warning, file md5 equals the Unity install's copy) |
| 7 | `ForestProbe.cs` | New argument `-forestProbeItems P7,P10`: runs only those items, writes `probe.json` to `-captureOut` and leaves `Library/GamexForest/probe.json` alone | Needed to bisect items 4 and 6. A partial run must not replace the record the audit and `Budget_FrameTime` act on | `M0-04` to `M0-06` logs |
| 8 | `ForestLighting.cs` | One comment: cubemap faces are 2048, not "about 1024" | Measured by P8 | |

Change 6 in `ForestTerrain.cs` has **not been executed**: no scene was built at M0. It is the same four
lines as in the probe, where it was.

The six originals are in `build/backup/M0-pre-edit/`.

## 2. Outside the project: one file in `Library/PackageCache` was put back

`Library/PackageCache/com.unity.render-pipelines.high-definition@03bdf33c5697/Runtime/RenderPipelineResources/Material/DefaultHDTerrainMaterial.mat`
was rewritten by Unity during the first probe run (item 6 above). I copied the original back from
`/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/Resources/PackageManager/BuiltInPackages/com.unity.render-pipelines.high-definition/`
(same package, same version; md5 `8ce2f7482ff8fb845c1b94b0082b4cf0` on both now). The altered copy is kept
as `build/out/M0/DefaultHDTerrainMaterial.altered-by-probe.mat`. Nothing in the Unity install was touched.

## 3. Probe answers and what each one set

Record: `build/captures/M0/probe/probe.json`, copy in `Library/GamexForest/probe.json` (run `M0-08-probe.log`).

| Item | Answer | Switch |
|---|---|---|
| P1 | The importer gives each mesh node rotation (-90, 0, 0) and scale 100 (67.9 for `rock07`), mirrors **X**, keeps the FBX material names on the slots; mesh data is readable with `isReadable` off. 11 of 11 objects match the manifest | None. `isReadable` stays false |
| P2 | BC7 for base colour, normal, mask and foliage atlas; the sky is BC6H after change 1; 4k sets import at 4096 | None |
| P3 | A 2k set re-imports in 0.8 s, a 4k set in 2.5 s | None. The fast-compressor override is not needed |
| P4 | `GenerateMeshLods` accepts a script-built mesh and each level reads back (rock11: 8,000, 4,094, 2,046, ... 128) | None. `ForestKitSpec.UseUnityGenerator` stays false: clustering is the spec's default and the generator is the upgrade of risk 12, to be taken only if the M2 kit sheets show crude levels. It is now known to be available |
| P5 | **No.** In edit mode, across eight explicit renders, the render counters stay 0 and `FrameTimingManager` returns no timings (feature enabled: true) | The spec's fallback, already wired by WP5 through the probe record: `Budget_FrameTime` asserts on the stopwatch with a one-pixel readback. Whether the counters report in play mode is still open; `Budget_Counts` fails on all-zero counters, so M1 will show it |
| P6 | **No.** The ambient probe is not there after two renders; it arrives after the wait **and one more render** | None: the capture loop already waits and keeps rendering |
| P7 | Yes. CPU 4.683 lux; HDRP's sky in Lux mode, given that value and asked for 5,000 lux, delivers 4,946 on ground facing up (ratio 0.989) and 1,782 facing down (0.3604 of up; the CPU says 0.3603) | None. `ForestLighting.CalibrateSkyInLux` stays true |
| P8 | Yes. Faces 2048; BC6H differs from uncompressed by RMSE 0.0012 | Change 1 |
| P10 | Yes. Eight flat-colour layers render in their own colours on a script-made TerrainLit material, height blend off and on; `ValidateMaterial` true. The material never shows `_TERRAIN_8_LAYERS`; the engine sets it per draw | None. `ForestLayout.TerrainLayerCount` stays 8. The player-build question (risk 27) is still open |
| P11 | Yes. A level planned to 14 m lasts to 14.00 m, cull planned at 42 m is at 42.00 m; cross-fade dithers | None. `LodDistanceScale` stays 1, `UseCrossFade` stays true |
| P12 | Yes. Capsule checks, a capsule cast and a raycast against a box, a mesh collider and a terrain answer correctly in edit mode | The audit's physical corridor check now runs in edit mode (it reads the record) |
| P14 | First render of the greybox 0.4 to 1.0 s with a warm cache | None |
| P15 | Yes. 2048 is accepted; `SetAlphamaps` with eight layers 0.02 s, saving 0.07 s, 44.7 MB on disk | None. `ForestLayout.AlphamapResolution` stays 2048 |

## 4. Found on the way, not changed

1. **The sky's lower half is darker than the spec assumed.** `overcast_soil_puresky` gives 4.68 lux from
   above and 1.69 from below, so the spec's argument that a row-order mistake "cannot produce a wild
   number" does not hold for it (the mistake would be a factor of 2.8). P7 now checks the orientation
   directly, and it is right. `ForestSky.Calibrate` still logs its warning ("the lower hemisphere gives
   0.36 times the light of the upper one") on every build; for this sky it is a false alarm.
2. **`fadeTransitionWidth` 0.2 is a wide band.** In P11 a level that ends at 14 m starts cross-fading at
   about 5.5 m (8 % of the other level at 5.8 m, 47 % at 9 m). Both levels are drawn in that band. For a
   young fir (LOD0 to 14 m) that is most of LOD0's range. Something to look at with the first trees (M3).
3. **`ShaderUtil.anythingCompiling` is true after the first renders** although the capture loop switches
   asynchronous compilation off. No capture so far showed a cyan pixel or a missing object.
4. **A sky imports with 2048 faces.** The texture-memory estimate of spec section 10 ("about 0.5 GiB with
   one uncompressed sky") was built on 1024.
5. **Before the first render of a session, the engine has not picked sub-shaders.** Besides item 3 of
   section 1 this means any code that asks a material or shader about its active sub-shader in a fresh
   batch process gets the default answer.
6. **The integration shader of the HDRI Sky inspector draws nothing in batch mode** (section 1, item 4).
   Not investigated further: nothing in the build uses it.
