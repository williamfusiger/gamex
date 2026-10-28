# Terrain and vegetation systems driven from editor code

Research for the forest "base scenery" scene. Unity 6000.6.0f1, HDRP 17.6.0, AI Navigation 2.0.12.
Nothing in the project was changed and Unity was not launched. Every claim below cites what was read.
Anything not confirmed by a doc page or source line is labelled **UNVERIFIED**.

## Path abbreviations used in citations

| Short | Absolute path |
|---|---|
| `DOC` | `/Applications/Unity/Hub/Editor/6000.6.0f1/Documentation/en` |
| `XML` | `/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/Resources/Scripting/Managed/UnityEngine` (the `*.xml` API doc files next to the engine DLLs; they give exact member signatures) |
| `HDRP` | `/Users/william/Documents/GitHub/gamex/Library/PackageCache/com.unity.render-pipelines.high-definition@03bdf33c5697` |
| `CORE` | `/Users/william/Documents/GitHub/gamex/Library/PackageCache/com.unity.render-pipelines.core@2d66c71e606e` |
| `SG` | `/Users/william/Documents/GitHub/gamex/Library/PackageCache/com.unity.shadergraph@ef6135652377` |
| `NAV` | `/Users/william/Documents/GitHub/gamex/Library/PackageCache/com.unity.ai.navigation@734c2c4c5591` |
| `SCRATCH` | `/private/tmp/claude-501/-Users-william-Documents-GitHub-gamex/e48fad55-bf2b-4ea1-ae11-7561f14da1a4/scratchpad/forest` |

"Native string" means a message extracted from the engine binaries (`Unity.app/Contents/MacOS/Unity`, `UnityEngine.TerrainModule.dll`, `UnityEditor.TerrainModule.dll`). They are quoted because they state engine rules that the docs do not. The extracted list is at `SCRATCH/probes/unity_strings.txt`.

Helper scripts written for this research (read-only parsers, safe to reuse): `SCRATCH/scripts/sg_inspect.py`, `sg_dump.py`, `sg_notes.py`, `sg_closure.py` (Shader Graph file inspection), `doc.py` (doc HTML to text), `gltf_summary.py`.

---

## 0. The five findings that change the design

1. **A ready HDRP foliage shader with wind ships inside the installed Shader Graph package.** `SG/Samples~/ProductionReady/Environment/Details/` has Fern, Nettle, Grass, Clover and Pebbles shader graphs, each with an active HDRP target, made for terrain detail meshes, with distance dissolve. The dependency closure is 17 subgraphs plus one HLSL file, all inside the same package. It can be copied into the project by script. Details in section 7.
2. **Under HDRP only GPU-instanced detail meshes are a real option for ground cover.** `HDRenderPipelineAsset` does not provide the three legacy detail shaders, and the engine says so in its own warnings (section 4).
3. **Terrain-tree colliders exist only at runtime**, so a NavMesh baked in edit mode from physics colliders (the greybox method) does not see terrain-tree trunks (section 5). This, plus the GPU Resident Drawer only handling MeshRenderer GameObjects, is why trees should be scattered prefab instances and not `TreeInstance`s (section 3).
4. **`Unity.Mathematics` (including `noise`) is an engine module in 6000.6** and is already referenced by `Gamex.EditorTools.csproj`. No package or asmdef change is needed to use simplex, classic Perlin and Worley noise (section 1).
5. **Unity 6.2+ "Mesh LOD" generates LODs automatically at import** (`ModelImporter.generateMeshLods`). This removes the "nobody can model" blocker for rocks, logs and stumps. It is documented as poor for foliage (section 3).

---

## 1. Building a Terrain by script

### 1.1 API surface (all confirmed)

Signatures from `XML/UnityEngine.TerrainModule.xml` and the pages under `DOC/ScriptReference/`.

| Need | API | Source |
|---|---|---|
| Create data | `new TerrainData()` | `DOC/ScriptReference/TerrainData.html` |
| Height grid size | `TerrainData.heightmapResolution` (int). "Unity clamps the value to one of 33, 65, 129, 257, 513, 1025, 2049, or 4097." | `TerrainData-heightmapResolution.html` |
| World size | `TerrainData.size` (Vector3: width, height, length) | `TerrainData-size.html` |
| Sample spacing | `TerrainData.heightmapScale` (x,z = spacing between samples, y = full height range) | `TerrainData-heightmapScale.html` |
| Write heights | `SetHeights(int xBase, int yBase, float[,] heights)`; values 0..1; **array indexed `[y,x]`** (y is world Z) | `TerrainData.SetHeights.html` |
| Cheaper repeated writes | `SetHeightsDelayLOD(...)` then `SyncHeightmap()` | `TerrainData.SetHeightsDelayLOD.html`, `TerrainData.SyncHeightmap.html` |
| Read back | `GetHeights(xBase, yBase, width, height)` returns `[height,width]`, indexed `[y,x]` | `TerrainData.GetHeights.html` |
| Queries for scatter | `GetInterpolatedHeight(float x, float y)` (normalized 0..1, returns metres), `GetSteepness(float x, float y)` (degrees, 0..90 per the SetAlphamaps example), `GetInterpolatedNormal(float x, float y)` (unit vector, Sobel filter) | respective pages |
| Scene object | `Terrain.CreateTerrainGameObject(TerrainData)` — "Creates a Terrain including collider from TerrainData." | `Terrain.CreateTerrainGameObject.html` |
| Material | `Terrain.materialTemplate` — "Unity doesn't make a copy of the custom material internally" | `Terrain-materialTemplate.html` |
| Instanced terrain | `Terrain.drawInstanced` — "The default value is false." | `Terrain-drawInstanced.html` |
| Mesh LOD error | `Terrain.heightmapPixelError` | `Terrain-heightmapPixelError.html` |
| Far-texture switch | `Terrain.basemapDistance`, `TerrainData.baseMapResolution` | `Terrain-basemapDistance.html` |
| Apply changes | `Terrain.Flush()` | `Terrain.Flush.html` |
| Quality overrides | `Terrain.ignoreQualitySettings`; `QualitySettings.terrainQualityOverrides` and friends | `Terrain-ignoreQualitySettings.html`, `XML/UnityEngine.CoreModule.xml` |

The Terrain pivot is the lower-left corner (comment in `NAV/Runtime/NavMeshSurface.cs:486`). To centre a tile on the origin: `terrainObject.transform.position = new Vector3(-size.x / 2, baseY, -size.z / 2)`.

Saving as an asset follows the greybox pattern (`AssetDatabase.CreateAsset`, see `Assets/Game/Editor/GreyboxSceneBuilder.cs:124`). TerrainData, each TerrainLayer (extension `.terrainlayer`, as in `SG/Samples~/Common/Textures/terrain/ground_grass_fells_mossy/ground_grass_fells_mossy.terrainlayer`) and the terrain material must be assets before the scene is saved, or the scene will hold dangling references.

**UNVERIFIED ordering rules (well known, but not stated in the local docs; follow them and the question never arises):**
- Set `heightmapResolution` **before** `size`. Changing the resolution is reported to rescale the size.
- Call `AssetDatabase.CreateAsset(terrainData, path)` **before** assigning `terrainLayers` and calling `SetAlphamaps`. The control (splat) textures are sub-objects of the TerrainData asset (`TerrainData.alphamapTextures`, `TerrainData.AlphamapTextureName`), and creating them while the data is already persistent is the safe order. Finish with `EditorUtility.SetDirty(terrainData)` and `AssetDatabase.SaveAssets()`.

### 1.2 Resolution versus world size

World spacing = `size.x / (heightmapResolution - 1)`.

| Tile | Resolution | Spacing | Comment |
|---|---|---|---|
| 256 m | 513 | 0.50 m | Too coarse for a 2 m trail edge or a stream bank |
| 256 m | **1025** | **0.25 m** | Recommended. 8 samples across a 2 m trail. About 1.05 M samples |
| 256 m | 2049 | 0.125 m | 4.2 M samples; slower collider and NavMesh bake for little visible gain under ground cover |

Heights are normalized 0..1 over `size.y`. With `size.y = 60` the step is under 1 mm, so precision is not a concern.

Other grids to set explicitly: `alphamapResolution` (control map; 1024 gives 0.25 m texels on a 256 m tile), `baseMapResolution`, and `SetDetailResolution(int detailResolution, int resolutionPerPatch)` (section 4).

### 1.3 Noise available without adding anything

- `Mathf.PerlinNoise(float x, float y)` — returns about 0..1 ("might be slightly below 0.0 or beyond 1.0"), deterministic for the same coordinates, no seed parameter (`DOC/ScriptReference/Mathf.PerlinNoise.html`). Seed it by adding a seed-derived offset to the coordinates.
- `Unity.Mathematics.noise` — in this Unity version the package is an empty shell: "The package is now empty and unnecessary, because it has been moved to a module" (`/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/Resources/PackageManager/BuiltInPackages/com.unity.mathematics/package.json`). The types live in `UnityEngine.MathematicsModule.dll`, which `/Users/william/Documents/GitHub/gamex/Gamex.EditorTools.csproj` already references. Confirmed functions (`DOC/ScriptReference/Unity.Mathematics.noise*.html`, `XML/UnityEngine.MathematicsModule.xml`):
  - `float noise.snoise(float2)` / `(float3)` / `(float4)` — simplex, about -1..1
  - `float noise.cnoise(float2)` / `(float3)` / `(float4)` — classic Perlin
  - `float2 noise.cellular(float2)` — Worley F1/F2 (good for stand boundaries and rocky breakup)
  - `float3 noise.srdnoise(float2)` — simplex with analytical derivative (value plus gradient), which enables derivative-damped fBm that looks eroded without a simulation
  - `float noise.pnoise(float2, float2)`, `psrnoise`, `psrdnoise` — periodic variants
- `Unity.Mathematics.Random(uint seed)` — "The seed must be non-zero" (`Unity.Mathematics.Random-ctor.html`). `System.Random(seed)` (already used at `GreyboxSceneBuilder.cs:69`) is equally fine.

There is no built-in erosion or smoothing API on `TerrainData`. Erosion tools exist only as interactive editor brushes (`DOC/Manual/terrain-Erosion*.html`, which belong to the Terrain Tools package, not installed). Everything below is plain C# on a `float[,]`.

### 1.4 Recommended height pipeline (all plain array maths, deterministic)

1. **Macro shape**: 4 to 6 octaves of fBm (`snoise` or derivative-damped `srdnoise`), domain-warped by a second low-frequency noise. Keep the playable core gentle.
2. **Containment**: add a rim that rises with distance from the playable core (distance-to-edge falloff plus noise) so the area reads as a valley and the player cannot leave or see the tile edge. Keep the invisible boundary colliders from the greybox (`GreyboxSceneBuilder.cs:169-174`).
3. **Optional erosion**: thermal (talus) passes and a droplet hydraulic pass. A few hundred thousand droplets on 1025² is seconds of C#. Seed the RNG.
4. **Trail**: define a Catmull-Rom spline through fixed control points. For each sample compute distance `d` to the spline. Target height is the low-pass-filtered terrain height along the spline (this limits the trail's own gradient). Blend `h = lerp(h, target - 0.08, 1 - smoothstep(halfWidth, halfWidth + shoulder, d))`. Keep the distance field as `trailMask`.
5. **Stream bed**: a second spline. Make the bed height monotonically decreasing along it (running minimum with a small forced drop), carve a parabolic cross-section 0.5 to 1.0 m deep and 3 to 5 m wide, keep `streamMask`. A water strip mesh along the same spline is easy to generate by script.
6. **Smoothing**: separable box or Gaussian blur, masked so it only touches carved shoulders.
7. One `SetHeights(0, 0, heights)` call at the end.

The masks from steps 4 and 5 are reused three times: painting layers (section 2), excluding trees and thinning ground cover (section 6), and NavMesh area marking (section 5).

**Constraint from the existing player code.** `Assets/Game/Scripts/Player/PlayerController.cs:135-141` pins vertical velocity to -2 m/s while grounded. Sprinting (6 m/s, line 23) down a slope steeper than about 18 degrees (tan⁻¹(2/6)) makes the controller leave the ground each frame. Keep trails under about 15 degrees and the general playable ground under 25 to 30 degrees, or plan a later code tweak.

---

## 2. TerrainLayers, alphamaps and HDRP TerrainLit

### 2.1 TerrainLayer by script

Properties (`XML/UnityEngine.TerrainModule.xml`): `diffuseTexture`, `normalMapTexture`, `maskMapTexture`, `tileSize`, `tileOffset`, `normalScale`, `metallic`, `smoothness`, `smoothnessSource`, `specular`, `diffuseRemapMin`, `diffuseRemapMax`, `maskMapRemapMin`, `maskMapRemapMax`.

How HDRP reads them (all from `HDRP/Editor/Material/TerrainLit/TerrainLitGUI.cs` and `HDRP/Runtime/Material/TerrainLit/TerrainLit_Splatmap.hlsl`):

| Field | Meaning under HDRP TerrainLit | Evidence |
|---|---|---|
| `maskMapTexture` | R metallic, G ambient occlusion, **B height**, A smoothness | `HDRP/Runtime/Material/TerrainLit/TerrainLit.shader:98-101`; `DOC/Manual/class-TerrainLayer.html` |
| `diffuseTexture` alpha | Smoothness when there is no mask map; **density** once a mask map is assigned | `TerrainLit.shader:102-103` |
| `normalScale` | Per-layer normal strength (`_NormalScale##n`) | `TerrainLit_Splatmap.hlsl:88`; `TerrainLit_Splatmap_Includes.hlsl:16` |
| `diffuseRemapMax.xyz` | Colour tint (`albedo.rgb *= _DiffuseRemapScale.xyz`) | `TerrainLitGUI.cs:210-215`; `TerrainLit_Splatmap.hlsl:111` |
| `diffuseRemapMin.w = 1` | Enables "Opacity as Density" for that layer (only when height blend is off) | `TerrainLitGUI.cs:217-226`; `TerrainLit_Splatmap.hlsl:210-215` |
| `maskMapRemapMin.z` / `Max.z` | Height offset and amplitude **in metres** (the UI shows centimetres and divides by 100) | `TerrainLitGUI.cs:279-293` |
| `maskMapRemapMin/Max.x,y,w` | Remap of metallic, AO, smoothness | `TerrainLitGUI.cs:266-299` |
| `smoothnessSource` | `TerrainLayerSmoothnessSource` enum | `TerrainLit_Splatmap.hlsl:114-125` |

Mask map textures must be imported as linear (the sample's is `sRGBTexture: 0`, `SG/Samples~/Common/Textures/terrain/ground_grass_fells_mossy/ground_grass_fells_mossy_m.png.meta`). Poly Haven "arm" maps are AO/roughness/metal and need repacking into metallic/AO/height/smoothness; that belongs to the materials research, but the terrain needs the **height in blue** for height-based blending.

### 2.2 Painting alphamaps

`TerrainData.SetAlphamaps(int x, int y, float[,,] map)` — "the order of the array is `[y,x,i]`" (`DOC/ScriptReference/TerrainData.SetAlphamaps.html`; its example blends two layers by `GetSteepness`). Size is `alphamapWidth` by `alphamapHeight` by layer count. Normalize each texel so the weights sum to 1; the engine does not promise to.

Suggested rule set per texel, evaluated from the height data and the masks of section 1.4:
- forest floor (leaf litter / moss): base weight
- bare dirt: `trailMask`, plus noise patches
- rock: `smoothstep(30°, 45°, steepness)`
- wet gravel / pebbles: `streamMask`
- moss or grass variant: low-frequency noise multiplied by a flatness term
- optional altitude term near the rim

Blur the weights slightly (one or two texels) before writing. Height-based blending then turns the soft weights into crisp, natural transitions.

### 2.3 What HDRP TerrainLit can and cannot do

| Feature | Status | Evidence |
|---|---|---|
| Layers | **8 maximum, single pass.** More than 8 "appear in the Inspector window, but they don't render ... Any areas painted with these extra layers appear black." | `HDRP/Documentation~/terrain-lit-material.md:3`; `DOC/Manual/class-TerrainLayer.html`; `TerrainLit.shader:97` (`"SplatCount" = "8"`) |
| Height-based blend | Yes. Material float `_EnableHeightBlend`, `_HeightTransition` (0..1). Keyword `_TERRAIN_BLEND_HEIGHT` is set by validation | `TerrainLit.shader:5-6`; `HDRP/Runtime/Material/TerrainLit/TerrainLitAPI.cs:26-27`; `HDRP/Documentation~/terrain-lit-material-inspector-reference.md:17-18` |
| Density blend | Default when height blend is off | `TerrainLit_Splatmap_Includes.hlsl:7-9` |
| Per-pixel normal | `_EnableInstancedPerPixelNormal`; "It only works if you enable Draw Instanced on the terrain." | `terrain-lit-material-inspector-reference.md:19` |
| Decals | `_SupportDecals` defaults to 1 | `TerrainLit.shader:50` |
| Triplanar | **No.** `// TODO: support tri-planar?` | `TerrainLit.shader:9`; `TerrainLit_Splatmap.hlsl:80` |
| Parallax / POM | **No.** `// TODO: POM` | `TerrainLit_Splatmap.hlsl:81` |
| Tessellation / displacement | **No.** The shader has no hull or domain stage (0 matches for `pragma hull`, `pragma domain`, `Tessellation` in `TerrainLit.shader`) | file itself |
| Anti-tiling / macro variation / detail map | **No** such inputs exist in the property block or the splat code | `TerrainLit.shader:3-54` |

Creating the material by script: copy the pipeline default (`HDRP/Runtime/RenderPipelineResources/Material/DefaultHDTerrainMaterial.mat`: shader HDRP/TerrainLit, `m_EnableInstancingVariants: 1`, keyword `_TERRAIN_INSTANCED_PERPIXEL_NORMAL`; reachable as `GraphicsSettings.currentRenderPipeline.defaultTerrainMaterial`, overridden for the editor at `HDRP/Runtime/RenderPipeline/HDRenderPipelineAsset.DefaultResources.cs:23`, or by `AssetDatabase.LoadAssetAtPath<Material>` on the `Packages/com.unity.render-pipelines.high-definition/...` path), set `_EnableHeightBlend = 1` and `_HeightTransition`, then call `HDMaterial.ValidateMaterial(material)` exactly as the greybox does for Lit (`GreyboxSceneBuilder.cs:446`). The dispatch table maps HDRP/TerrainLit to `TerrainLitAPI.ValidateMaterial` (`HDRP/Runtime/Material/MaterialExtension.cs:296-297, 326-327`). Keep `material.enableInstancing = true` and set `terrain.drawInstanced = true`.

### 2.4 Looking good at first-person distance

Ranked by value for effort:

1. **Layer hygiene with the stock shader**: mask maps with real height in blue, height blend on with a small transition, per-layer `normalScale` around 1 to 2 (the Unity sample layer uses 2), and a **different `tileSize` per layer** (2 to 5 m) so blends break each other's repetition. Unity's own forest tutorial relies on exactly this: "we're able to hide the tiling by giving each material a different tiling frequency ... We also cover the terrain with detail meshes ... which further hides the tiling" (`SG/Documentation~/Shader-Graph-Sample-Production-Ready-Tutorial.md:31`).
2. **Cover the ground**: scattered twigs, roots, rocks, leaf litter decals, ferns and grass hide more tiling than any shader feature.
3. **Terrain Shader Graph sample for anti-tiling**: `SG/Samples~/Terrain/Shaders/` holds terrain graphs with an active HDRP `TerrainLitSubTarget`. `CNM Rotation Height` and `CNM Hex Height` use the **standard** colour/normal/mask packing, so ordinary TerrainLayers work unchanged. Cost relative to stock TerrainLit: rotation 2.32x, hex 4.08x (`SG/Documentation~/Shader-Graph-Sample-Terrain-Performance.md:16,21`). A `Detail` close-up layer, `Distance Fade` to hide the basemap seam, `Layer Triplanar` and parallax layers are also there (`Shader-Graph-Sample-Terrain-Layers.md`). Parallax exists only for the repacked CSNOH scheme and is costly ("CSNOH Hex Parallax Height is 20 times as expensive as Terrain Lit", `...-Performance.md:42`).
   - Their subgraph dependencies resolve inside the package (`SG/GraphTemplates/Subgraphs/Terrain/*.shadersubgraph`, `SG/GraphTemplates/Subgraphs/HeightMask.shadersubgraph`, and `SG/Samples~/Terrain/Subgraphs/`).
   - **Risk found in source**: `HDMaterial.ValidateMaterial` identifies shader-graph materials by matching the `ShaderGraphTargetId` tag against `s_SubTargetIds` (`MaterialExtension.cs:301-315, 343-354`). That list has 12 names and no `TerrainLitSubTarget`, while the enum has 13 shader-graph entries including `SG_TerrainLit` (`:270-282`). A terrain shader-graph material created by script may therefore not be validated (the call would return false). If this route is taken, check the return value, and prefer copying one of the sample's ready `.mat` files (`SG/Samples~/Terrain/Materials/CNM Hex Height.mat`), which already carries its keywords.
4. **True displacement** is not available on Unity Terrain in HDRP. The only way is a tessellated mesh (`HDRP/LitTessellation` or `LayeredLitTessellation`, which list `metal` in `#pragma only_renderers`, `HDRP/Runtime/Material/Lit/LitTessellation.shader:1011`) replacing terrain in a hero area. Not recommended for version 1.

Recommendation: version 1 uses stock `HDRP/TerrainLit` with height blend (items 1 and 2). Item 3 is a contained later upgrade because it only swaps `terrain.materialTemplate`.

---

## 3. Trees: three options compared

### 3.1 What each option is

**A. Terrain `TreeInstance`s** (`TerrainData.treePrototypes`, `SetTreeInstances(TreeInstance[], bool snapToHeightmap)`).
- `TreeInstance.position` is normalized 0..1 in terrain space; `widthScale`, `heightScale`, `rotation` (radians), `color`, `prototypeIndex` (`DOC/ScriptReference/TreeInstance*.html`). After changing prototypes call `TerrainData.RefreshPrototypes()` (`TerrainData-treePrototypes.html`).
- The engine has three internal tree renderers: `LODTreeInstanceRenderer`, `LegacyTreeInstanceRenderer`, `BillboardTreeInstanceRenderer` (native strings). Which one applies depends on the prefab:
  - Editor string: "The selected tree does not have an LOD group, so it will use the default impostor system and will not support rotation."
  - Editor string: "The selected tree does not have an LOD group, but has a hierarchy of MeshRenderers, only MeshRenderer on root GameObject in the trees hierarchy will be used. Use a tree with LOD group if you want a tree with hierarchy of MeshRenderers."
  - Editor string: "Randomize tree rotation. This only works when the tree has an LOD group."
  - The impostor path uses built-in shaders (`Hidden/TerrainEngine/BillboardTree`, `Hidden/Nature/Tree Creator ... Rendertex`, native strings) that are not HDRP shaders.
  - **So under HDRP a terrain tree prefab must have a LODGroup at its root.** The editor says `Tree Distance`, `Billboard Start`, `Fade Length` and `Max Mesh Trees` "have no effect on SpeedTree trees. Please use the LOD Group component on the tree prefab to control LOD settings" (editor string; same in `DOC/Manual/terrain-OtherSettings.html`). In engine terms "SpeedTree trees" means LOD-group trees: `Terrain.treeLODBiasMultiplier` is described as applying to "LOD trees (i.e. SpeedTree trees)" (`DOC/ScriptReference/Terrain-treeLODBiasMultiplier.html`). That this holds for a non-SpeedTree prefab with a LODGroup is an inference, not something a doc states outright.
- `TreeInstance.rotation` is documented as "Read-only" (`TreeInstance-rotation.html`) although it is a public field. Whether a scripted value is honoured is **UNVERIFIED**.
- Colliders: "Unity only generates Tree Prefab colliders at runtime" (`DOC/Manual/terrain-colliders-introduction.html`). The toggle is the serialized field `m_EnableTreeColliders`; `TerrainCollider` exposes only `terrainData` publicly (`XML/UnityEngine.TerrainPhysicsModule.xml`).
- Not drawn by the GPU Resident Drawer, which "works only with ... GameObjects that have a Mesh Renderer component" (`HDRP/Documentation~/gpu-resident-drawer.md:6-9`).
- Not included in ray tracing (`DOC/Manual/render-pipelines-feature-comparison.html`: "Heightmap only. No support for terrain details and terrain trees").

**B. Scattered prefab instances with LODGroup** (what the greybox does with primitives, `GreyboxSceneBuilder.cs:233-263`).
- LODGroup API: `LODGroup.SetLODs(LOD[])`, `new LOD(float screenRelativeTransitionHeight, Renderer[] renderers)`, `RecalculateBounds()`, `fadeMode` (`LODFadeMode.None/CrossFade/SpeedTree`), `animateCrossFading`, `ForceLOD(int)` (`XML/UnityEngine.CoreModule.xml`). Screen heights must strictly decrease (native string: "SetLODs: Attempting to set LOD where the screen relative size is greater then or equal to a higher detail LOD level").
- The model importer builds LODGroups by itself: "If the model contains meshes named _LODx, Unity imports them as LOD Group components" (`DOC/Manual/lod/mesh-lod-generate-lods.html`).
- Colliders are ordinary components, present in edit mode, so the existing NavMesh bake sees them.

**C. The same GameObjects drawn through the GPU Resident Drawer** (not a separate placement method; it is option B with instanced submission and optional GPU occlusion culling).
- Enable: `HDRenderPipelineAsset.gpuResidentDrawerMode = GPUResidentDrawerMode.InstancedDrawing` (public setter, `HDRP/Runtime/RenderPipeline/HDRenderPipelineAsset.cs:265-275`; enum in `CORE/Runtime/GPUDriven/IGPUResidentRenderPipeline.cs:9-20`) and `EditorGraphicsSettings.batchRendererGroupShaderStrippingMode = BatchRendererGroupStrippingMode.KeepAll` (checked at `CORE/Runtime/GPUDriven/GPUResidentDrawer.Validator.cs:61-66`; without it the drawer logs a warning and stays off). Occlusion culling is `gpuResidentDrawerSettings.enableOcclusionCullingInCameras` (`HDRP/Runtime/RenderPipeline/GlobalGPUResidentDrawerSettings.cs:28-29`).
- HDRP allows it in edit mode (`allowInEditMode = true`, `HDRenderPipelineAsset.cs:252`), so headless captures use it too.
- Object requirements (`HDRP/Documentation~/make-object-compatible-gpu-rendering.md:5-12`): MeshRenderer; Light Probes not "Use Proxy Volume"; static GI only; shader supports DOTS instancing; no `MaterialPropertyBlock`; no per-instance callbacks; LOD Group "Animate Cross-fading" disabled.
- Shader side is satisfied: `HDRP/Lit` has `#pragma multi_compile _ DOTS_INSTANCING_ON` (`HDRP/Runtime/Material/Lit/Lit.shader:366`), and every HDRP Shader Graph pass is generated with `CorePragmas.DotsInstanced` (`HDRP/Editor/Material/ShaderGraph/HDShaderPasses.cs:63` and following; definition at `HDTarget.cs:1096-1100`), regardless of the per-graph `m_DOTSInstancing` flag.
- Docs advise disabling Static Batching when using it (`gpu-resident-drawer.md:48`), so do not set `StaticEditorFlags.BatchingStatic` on scattered objects.

### 3.2 Verdict

**Use B drawn through C: prefab instances with LODGroup and a trunk collider, GPU Resident Drawer on.**

| Criterion | Terrain trees (A) | Prefab instances + GRD (B+C) |
|---|---|---|
| Works with scanned meshes in HDRP | Only with a LODGroup prefab | Yes |
| Trunks carved from an edit-mode NavMesh bake | No from colliders (runtime only); native inclusion **UNVERIFIED** | Yes, unchanged greybox method |
| Line-of-sight raycasts in edit-mode checks | No colliders in edit mode | Yes |
| Instancing and GPU occlusion culling | Legacy tree renderer, no GRD | GRD |
| Tilt, non-uniform placement, per-tree components (hiding spots, sound triggers) | No | Yes |
| Scene size | Tiny | A few thousand prefab instances; acceptable |
| Later dinosaur gameplay (cover, noise, destructible brush) | Awkward | Natural |

Estimated count for a 256 m tile with a bounded core: 1,500 to 3,000 trees. The greybox already places 240 as GameObjects.

### 3.3 LODs when nobody can model

- **Authored LODs**: 78 of the 521 Poly Haven models carry `"lods": true` (`SCRATCH/catalog/ph_models.json`), including most trees. The glTF node is named with the `_LOD0` suffix (`island_tree_02_LOD0`, `pine_tree_01_a_LOD0`; `SCRATCH/catalog/gltf_island_tree_02.json`, `gltf_pine_tree_01.json`), which matches Unity's automatic LODGroup naming rule. **UNVERIFIED**: the glTF descriptors read here contain only LOD0, so lower LODs must be in the FBX or .blend download; that needs checking when assets are fetched.
- **Scale warning**: LOD0 of these scans is far too heavy to be a game LOD0. `island_tree_02_LOD0` is about 1.07 million triangles (27k trunk + 715k leaves + 330k branches); `pine_tree_01` has three variants of 4 to 7 million triangles each, and its FBX is 648 MB (`SCRATCH/catalog/files_pine_tree_01.json`). The pipeline must start from a lower source LOD or decimate.
- **Mesh LOD (new in Unity 6.2+)**: `ModelImporter.generateMeshLods`, `maximumMeshLod`, `meshLodGenerationFlags`; or `UnityEditor.MeshLodUtility.GenerateMeshLods(Mesh mesh, int meshLodLimit)` (`DOC/ScriptReference/MeshLodUtility.GenerateMeshLods.html`). Each LOD halves the index count and reuses the vertex buffer; needs at least 256 triangles (`DOC/Manual/lod/mesh-lod-generator.html`). Per-renderer control: `Renderer.meshLodSelectionBias`, `Renderer.forceMeshLod`; global: `QualitySettings.meshLodThreshold`.
  - Documented as good for "living creatures, rocks, sculptures" and bad for "meshes that consist of many disconnected pieces"; the troubleshooting page shows foliage disappearing at LOD4 on a tree (`DOC/Manual/lod/mesh-lod-troubleshooting.html`).
  - "Using Mesh LOD in combination with LOD Group might lead to unexpected outcomes and is not recommended" (`mesh-lod-introduction.html`). Pick one per asset.
  - Cross-fade for Mesh LOD requires the GPU Resident Drawer (same page).
- **No billboards or impostors** are available without the SpeedTree path. With volumetric fog limiting sight to roughly 80 to 120 m, culling the last LOD at that distance and ringing the playable core with dense low-LOD trees is sufficient for version 1. Baking impostor cards by rendering each tree from several angles in batch mode is feasible later.

### 3.4 Per-prefab requirements checklist

- Root: `LODGroup` (strictly decreasing screen heights; last entry sets the cull distance), `CapsuleCollider` on the trunk only (radius about trunk radius, height 3 to 4 m is enough for player and agents), no Rigidbody.
- Leaves: alpha-clipped, double-sided material; no `MaterialPropertyBlock`.
- A single material instance per species part shared across all instances (required for instancing to batch).
- Static flags: `ContributeGI` only if lighting research wants it. Not `BatchingStatic`.
- Canopy pieces carry no collider (as the greybox already notes at line 254).

---

## 4. Ground cover

### 4.1 Terrain detail meshes: rules

- Under HDRP the only usable mode is the **instanced mesh**. Docs: "Instancing details work with all render pipelines, including the High Definition Render Pipeline (HDRP)" and "With GPU instancing, Unity uses the material and the shader specified on the prefab" (`DOC/Manual/terrain-Grass.html`). The other three modes depend on shaders the pipeline asset must supply (`RenderPipelineAsset.terrainDetailLitShader`, `terrainDetailGrassShader`, `terrainDetailGrassBillboardShader`); `HDRP/Runtime/RenderPipeline/HDRenderPipelineAsset.DefaultResources.cs` (67 lines) overrides none of them. The engine's own warnings confirm the consequence (strings in `UnityEngine.TerrainModule.dll`):
  - "The current render pipeline does not support Billboard details. Details will not be rendered."
  - "The current render pipeline does not support Grass details. Details will be rendered using the default shader without alpha test and animation."
  - "The current render pipeline does not support VertexLit details. Details will be rendered using the default shader."
- `DetailPrototype` settings for an instanced mesh (`DOC/ScriptReference/DetailPrototype*.html`):
  - `prototype` = prefab with MeshFilter + MeshRenderer at the root (the layout of Unity's own `SG/Samples~/ProductionReady/Environment/Details/Ferns/Fern_A.prefab`)
  - `usePrototypeMesh = true`
  - `renderMode = DetailRenderMode.VertexLit` — required: `useInstancing` "is only effective when you specify DetailRenderMode.VertexLit", and for VertexLit `usePrototypeMesh` "must" be true "Otherwise, this detail prototype won't render"
  - `useInstancing = true`
  - `minWidth`, `maxWidth`, `minHeight`, `maxHeight` (still applied per instance), `noiseSeed`, `noiseSpread`, `alignToGround`, `positionJitter`, `useDensityScaling`, `density`, `targetCoverage`, `holeEdgePadding`
  - `Validate(out string errorMessage)` returns the reason a prototype is rejected. Call it and log the message.
- Hard limits:
  - "A detail prototype must have exactly one material." (native string). Multi-material plants cannot be details.
  - "Detail instancing is only supported on mesh details." (native string)
  - Batches of 1,023 or fewer; "Instanced light probe lighting or lightmap lighting aren't supported" (`terrain-Grass.html`). Adaptive Probe Volumes light per pixel, not per object (`HDRP/Documentation~/probevolumes.md:3`), so they are the lighting route for details; the detail is lighting research.
  - Healthy/dry colour tint is not applied to instanced details (`terrain-Grass.html`).
  - No colliders: the collider system "does not generate colliders for Prefabs that you add with the Details Brush" (`DOC/Manual/terrain-colliders-introduction.html`). Details never block sight rays or movement.
  - No LODs. Whether the detail renderer honours Mesh LOD is **UNVERIFIED**; assume LOD0 always.
  - Mesh Read/Write: the manual says it "must" be enabled for any detail mesh; the native message scopes the requirement to non-instanced prototypes. Enable `ModelImporter.isReadable` anyway.
- Material: GPU instancing enabled (`material.enableInstancing = true`; the sample materials have `m_EnableInstancingVariants: 1`).

### 4.2 Density controls

- `TerrainData.SetDetailResolution(int detailResolution, int resolutionPerPatch)`. Inspector ranges: resolution 0 to 4048, per patch 8 to 128, "recommended value is 16. If your Detail Distance is high and your grass is sparse, use a higher value" (`DOC/Manual/terrain-OtherSettings.html`). The per-patch value "must be a divisor of the detail resolution" (editor string).
- `TerrainData.SetDetailScatterMode(DetailScatterMode)`:
  - `InstanceCountMode`: map values 0 to 16 = instances per cell (`DetailScatterMode.InstanceCountMode.html`, `TerrainData-maxDetailScatterPerRes.html`)
  - `CoverageMode`: values 0 to 255 = area coverage; the count derives from prototype size and `density`; `ComputeDetailCoverage(int)` reports it and is unavailable in the other mode
- `TerrainData.SetDetailLayer(int xBase, int yBase, int layer, int[,] details)`; layer index = index in `detailPrototypes`. **UNVERIFIED index order**: heights and alphamaps are documented `[y,x]`, but the `SetDetailLayer` page example indexes `map[x, y]` on a square map, which does not settle it. Write an asymmetric test pattern once and look at the overview capture.
- `Terrain.detailObjectDistance` (cull distance) and `Terrain.detailObjectDensity` (0..1 multiplier). Quality settings can override them (`QualitySettings.terrainDetailDistance`, `terrainDetailDensityScale`, `terrainQualityOverrides`); set `terrain.ignoreQualitySettings = true` to keep scripted values.
- `QualitySettings.useLegacyDetailDistribution` must stay false; `positionJitter` only works with the newer distribution (`terrain-Grass.html`).

Worked numbers for a 256 m tile with `detailResolution = 1024` (0.25 m cells) in `InstanceCountMode`: one instance per cell is 16 per m². Grass at 8 per m² is an average of 0.5 per cell, written by stochastic rounding with a seeded hash. Ferns at 0.5 per m² are one instance per 32 cells. With `resolutionPerPatch = 32` a patch is 8 m.

### 4.3 What is feasible at first-person density

Unity's own tutorial gives the reference budget: details fade out at 30 m; three grass meshes with different fade distances, the densest visible only to 10 m; only one of the three casts shadows (`SG/Documentation~/Shader-Graph-Sample-Production-Ready-Tutorial.md:77-79`). Its grass blade mesh is 12 vertices and 10 triangles with no UVs or normals (`Shader-Graph-Sample-Production-Ready-Detail.md:76`); its fern is 327 vertices and 360 triangles (`SG/Samples~/ProductionReady/Environment/Details/Ferns/Fern_A.asset`).

Poly Haven for comparison: `fern_02` has four variants of 784 to 2,384 triangles, one alpha-masked double-sided material, attributes position/normal/UV0 only (`SCRATCH/catalog/gltf_fern_02.json`). That is usable as a detail mesh (single material) but 2 to 6 times heavier than Unity's fern. Poly Haven grass sets are scan clusters of 0.2 to 1.6 million triangles in total (`ph_models.json`: `grass_medium_01`, `grass_medium_02`, `grass_bermuda_01`); per-clump counts are **UNVERIFIED** and likely too heavy for dense use.

Budget sketch inside a 30 m radius (about 2,800 m²) on the M4 Max: 100 blades per m² of the 10-triangle blade is 2.8 M triangles; ferns at 0.5 per m² and 1,500 triangles add 2.1 M. Feasible on this machine, with shadows restricted to the larger plants. Not a PS5 budget, which is acceptable for this phase.

### 4.4 Split of responsibilities

| Tier | What | System | Why |
|---|---|---|---|
| 1 | Trees, saplings, large bushes, boulders, logs, stumps | Prefab instances + LODGroup or Mesh LOD + colliders, GRD | Need LOD, collision, NavMesh carving, sight blocking, multi-material |
| 2 | Ferns, small shrubs, nettles, flowers | Terrain instanced details when single-material and under about 2k triangles; otherwise tier 1 without collider | Density with zero GameObjects; wind shader ready |
| 3 | Grass blades or tufts, clover, pebbles, twigs, leaf clutter | Terrain instanced details | Very high counts, fade at 15 to 40 m |

Hiding spots for the stealth game need tier 1 objects (a detail has no collider or component).

---

## 5. Collision and NavMesh

### 5.1 Player on terrain

- `Terrain.CreateTerrainGameObject` adds the `TerrainCollider` (section 1.1). A `CharacterController` collides with it like any collider. Relevant controller properties: `slopeLimit` (degrees), `stepOffset`, `skinWidth`, `isGrounded` (`DOC/ScriptReference/CharacterController-*.html`). The greybox does not set `slopeLimit` or `stepOffset` (`GreyboxSceneBuilder.cs:299-302`), so component defaults apply.
- Slope budget from the current movement code: section 1.4.
- For placement queries in the builder use `TerrainData.GetInterpolatedHeight` or `Terrain.SampleHeight(Vector3)`, not raycasts. They need no physics sync.
- Spawn the player at `GetInterpolatedHeight(...) + 0.05`, as the greybox does with its flat ground (`GreyboxSceneBuilder.cs:297`).

### 5.2 NavMesh on terrain

- The greybox bake (`GreyboxSceneBuilder.cs:284-292`) uses `CollectObjects.Children` and `NavMeshCollectGeometry.PhysicsColliders`. It carries over unchanged **if the Terrain GameObject is parented under `Environment`**.
- Terrain is a first-class source: `NavMeshBuilder.CollectSources` "Collects renderers or physics colliders, and terrains" (`DOC/ScriptReference/AI.NavMeshBuilder.CollectSources.html`); `NavMeshBuildSourceShape.Terrain` "Describes a TerrainData source". `NavMeshSurface` handles it when the terrain module is present (`NAV/Runtime/NavMeshSurface.cs:482-491`, define `NMC_CAN_ACCESS_TERRAIN` from `NAV/Runtime/Unity.AI.Navigation.asmdef`; the module is in `Packages/manifest.json`). "Physics Colliders: Use geometry from Colliders and Terrains" (`NAV/Documentation~/NavMeshSurface.md:23`).
- **Trunks as obstacles**: with trees as prefab instances carrying a `CapsuleCollider` under `Environment`, they are collected as physics colliders and carved out, exactly like the greybox cylinders.
- If terrain trees were used instead: tree colliders are generated "at runtime" only, so they are absent during an edit-mode bake. The engine does contain a NavMesh path for terrain trees (`TreePrototype.navMeshLod`; native string "The tree '%s' is missing a mesh suitable for baking the NavMesh. Verify that specified NavMesh LOD prototype for the tree has a valid mesh assigned."), which would use the tree's render mesh at that LOD. Whether `NavMeshSurface.BuildNavMesh` goes through that path is **UNVERIFIED**; neither the AI Navigation docs nor its changelog mention trees.
- Stream and other soft zones: `NavMeshModifierVolume` (`size`, `center`, `area`) and `NavMeshModifier` (`overrideArea`, `area`, `ignoreFromBuild`, `applyToChildren`) (`NAV/Runtime/NavMeshModifierVolume.cs:32-41`, `NavMeshModifier.cs:52-73`).
- Useful surface options: `minRegionArea` (default 2, removes tiny islands between trunks), `overrideVoxelSize` / `voxelSize`, `overrideTileSize` / `tileSize`, `buildHeightMesh` (`NAV/Runtime/NavMeshSurface.cs:81-159`).
- Several dinosaur sizes later means one `NavMeshSurface` per agent type (`agentTypeID`, `NavMeshSurface.cs:103`). Out of scope now, but leave trunk spacing wide enough for the largest planned agent along intended routes.
- Save the result as the greybox does: `AssetDatabase.CreateAsset(surface.navMeshData, ...)`.

Suggested acceptance test (PlayMode, like `Assets/Game/Tests/PlayMode/StealthLoopTests.cs`): `NavMesh.CalculatePath` from the spawn to each objective is complete; a capsule cast along the trail finds no trunk.

---

## 6. Scatter algorithms worth implementing

All run on the CPU in the builder, before any Unity object is created, and write plain lists that are then turned into prefab instances and detail maps.

1. **Shared fields**, sampled from the finished height data: slope (`GetSteepness`), altitude, normal, `trailMask`, `streamMask`, distance-to-stream (moisture), clearing discs, and the reserved circles for gameplay points (keep the greybox `Reserve` / `IsFree` idea, `GreyboxSceneBuilder.cs:451-478`).
2. **Variable-radius Poisson-disc sampling (Bridson)** with a background grid. The local radius comes from a density field: `r(x,z) = rMin / max(density, epsilon)`. Linear time, no clumping artefacts, natural spacing.
3. **Class order, large to small**: canopy trees, understory trees and saplings, large bushes and boulders, logs and stumps, then tier 2 plants. Each class rejects candidates that fall inside the footprint of earlier classes through one shared occupancy grid with per-pair clearances.
4. **Species clustering**: one low-frequency noise field per species, optionally modulated by `noise.cellular` for stand boundaries, multiplied by ecological masks (ferns near the stream and in shade, pines on drier slopes). Choose the species by weighted random from those fields at each accepted point. This produces stands with mixed edges.
5. **Masks**: no trees above about 35 degrees, inside the trail corridor plus 1.5 m, inside the stream bed, or inside reserved circles. Ground cover is allowed up to the trail edge and thinned on it.
6. **Clearings and paths**: explicit discs and the two splines, plus thresholded low-frequency noise for natural gaps. Tall-plant density should rise at clearing edges, which also sells the lighting.
7. **Per-instance variation**: Y rotation 0 to 360, uniform scale about 0.8 to 1.25, trees vertical with at most a few degrees of lean, ground props aligned to `GetInterpolatedNormal`, everything sunk 5 to 15 cm.
8. **Detail maps from the same fields**: `value = density(x,z) * maxPerCell`, converted to an integer by stochastic rounding with a position hash.
9. **Determinism**:
   - one master seed constant (the greybox uses `Seed = 1993`)
   - a derived RNG per class and per species (`seed ^ classId * largePrime`), so tuning one class does not reshuffle the others
   - never `UnityEngine.Random` global state; never iterate a `HashSet` or `Dictionary` to produce output order
   - sort final lists by a stable key before instantiating
   - log counts per class at the end, like the greybox summary line (`GreyboxSceneBuilder.cs:108`)

---

## 7. Wind

### 7.1 Inventory: HDRP package samples (`HDRP/Samples~`)

Sample list from `HDRP/package.json`. None contains a foliage wind shader.

| Sample | Relevance |
|---|---|
| Environment Samples | Sky, fog, volumetric clouds, cloud layer. "Wind" here is cloud motion only (`Scripts/TimeOfDay.cs`) |
| Volumetric Samples | Fog-volume shader graphs (ground smoke, local clouds). Useful for mist, not foliage |
| Water Samples | HDRP water system scenes |
| Material Samples | **A static translucent leaf**: `MaterialSamples/Materials/Lit/Leaf.mat` (shader HDRP/Lit, `_MaterialID: 5` translucent, alpha clip 0.5, double-sided, transmission on), with `Meshes/Leaf.fbx`, `Textures/LeafBasecolor.png`, `LeafNormal.png`, `LeafThickness.png`, and `Scenes/Scene Resources/Diffusion Profiles/LeafDiffusionProfile.asset`. A reference for static leaf settings |
| Transparent, Lens Flare, Fullscreen, Post-processing Data, Procedural Sky, Renderer Shader User Value | Not relevant |

HDRP also ships `HDRP/Runtime/RenderPipelineResources/FoliageDiffusionProfile.asset` (a ready foliage diffusion profile) and SpeedTree graphs (`Runtime/Material/Nature/SpeedTree8.shadergraph`, `SpeedTree9_HDRP.shadergraph`, referenced at `HDRP/Runtime/Settings/HDRenderPipelineEditorShaders.cs:62,72`). The SpeedTree graphs depend on SpeedTree vertex data and wind buffers and are not usable on scanned meshes.

### 7.2 Inventory: Shader Graph package samples (`SG/Samples~`)

| Path | What it is |
|---|---|
| `ProductionReady/Environment/Details/Ferns/Fern.shadergraph` (+ `Fern.asset` material, `Fern_A/B/C` meshes and prefabs, 4 textures) | Fern with wind, distance dissolve, ground AO. **HDRP target active** |
| `ProductionReady/Environment/Details/Nettle/TerrainNettle.shadergraph` (+ `Nettle.mat`, `nettle1.fbx`, one texture) | Broad-leaf undergrowth, variation of the fern shader with a single texture sample. HDRP target active |
| `ProductionReady/Environment/Details/Grass/GrassWindTerrainDetails.shadergraph` (+ three `.mat`, three blade FBX) | Per-blade mesh grass, all work in the vertex shader, no textures. HDRP target active |
| `ProductionReady/Environment/Details/clover/clover.shadergraph`, `Pebbles/pebbles.shadergraph` | Static, distance-faded clutter. HDRP target active |
| `ProductionReady/Environment/Water/WaterStream.shadergraph`, `WaterStreamFalls`, `WaterLake`, `WaterSimple_FoamMask` | Stream and pond water |
| `ProductionReady/Environment/Decals/DecalWaterWetness`, `DecalWaterCaustics`, `DecalWaterRunning`, `DecalMaterialProjection` | Stream-bank integration decals |
| `ProductionReady/Environment/Rock/Rock.shadergraph` | Rock with macro/micro detail and moss deposition |
| `ProductionReady/Weather/` | Rain and snow subgraphs |
| `FeatureExamples/VertexAnimation/FoliageWind/FoliageWind.shadergraph`, `.../BendDeformer/` | Teaching examples of the same wind maths |
| `Common/Subgraphs/Wind.shadersubgraph`, `FoliageWind`, `AnimatedGrassPhase`, `BendDeformer`, `DistanceCutoff`, `DistanceMask`, `PseudoSubsurface` (+ `.hlsl`), `RandomFromPosition`, `BranchOnRP`, ... | Shared building blocks |
| `Terrain/` | Terrain shader graphs (section 2.4) and eight ready TerrainLayers with textures |
| `Common/Textures/terrain/ground_grass_fells_mossy`, `ground_rockgrass_fellsdirty` | Two forest-floor TerrainLayer sets in standard packing |

The remaining Shader Graph samples (Procedural Patterns, Node Reference, UGUI Shaders, Custom Material Property Drawers, Custom Lighting, Particle Shaders) contain nothing for environment, foliage or wind (sample list in `SG/package.json`).

Unity documents the Details set as made for this exact use and includes a "forest stream" build tutorial (`SG/Documentation~/Shader-Graph-Sample-Production-Ready-Detail.md:68-82`, `...-Tutorial.md`).

### 7.3 Is there a ready foliage graph with wind that a script can copy in? Yes.

Facts established by parsing the files:

- **Targets**: Fern, Nettle, Grass, Clover and Pebbles each list `HDTarget` with `HDLitSubTarget` plus a Universal target. URP is not installed; Shader Graph keeps an uninstalled target as an `UnknownTargetType` and imports the rest (`SG/Editor/Serialization/MultiJsonInternal.cs:84,130-147`; `SG/Editor/Importers/ShaderGraphImporter.cs:303-305`). The graphs come from the same 17.6.0 package, so there is no version upgrade step.
- **Settings in the fern graph**: opaque, alpha test on, double-sided, material type mask Standard + Colored Translucent.
- **Dependency closure** of the five graphs (computed with `SCRATCH/scripts/sg_closure.py`): 17 subgraphs in `SG/Samples~/Common/Subgraphs/` (AlphaMerge, AlphaSplit, AnimatedGrassPhase, BendDeformer, BillboardCylindrical, BranchOnRP, BrightnessContrast, DistanceCutoff, DistanceMask, ExpandVec2, FoliageWind, Hash21, MainLight, PixelClip, PseudoSubsurface, RandomFromPosition, Wind), the custom-function file `PseudoSubsurface.hlsl` in the same folder, and three default textures (`nettle_na.png`, `clover.png`, `Common/Textures/Rock_Projection_01.png`). Under 4 MB in total. `PseudoSubsurface.hlsl` is the only file-based custom function in the set; the other custom functions (`BranchOnRP`, `Hash21`, `MainLight`, `PixelClip`, `RandomFromPosition`) are inline strings. Two more GUIDs in `Fern.shadergraph` are default textures on slots that are driven by properties and do not matter.
- **How to copy**: `Samples~` folders are invisible to the AssetDatabase, so use `System.IO` copy followed by `AssetDatabase.Refresh()`, and **copy every `.meta` file alongside**, because graphs reference subgraphs by GUID. `Path.GetFullPath("Packages/com.unity.shadergraph/Samples~/...")` is the resolution Unity's own importer uses (`CORE/Editor/SampleDependencyImportSystem/SampleDependencyImporter.cs:96`).
  - Alternative: the official API `UnityEditor.PackageManager.UI.Sample.FindByPackage("com.unity.shadergraph", null)` then `sample.Import(Sample.ImportOptions.OverridePreviousImports)` (`DOC/ScriptReference/PackageManager.UI.Sample.FindByPackage.html`, `...Sample.Import.html`). It lands in `Assets/Samples/Shader Graph/17.6.0/` and a hook copies the two `Common` dependency folders (`SampleDependencyImporter.cs:34,88-110`). That brings about 144 MB (27 + 110 + 7.3) including demo scenes. Behaviour in batch mode is **UNVERIFIED**. The minimal file copy is preferable.
- **Material property names** to set by script on the fern shader (reference names read from the graph): textures `Texture2D_E1B0D043` (Base Map, alpha = opacity), `Texture2D_9DCAAA49` (Normal Map), `Texture2D_A5E0646` (Mask Map; G = AO, A = smoothness are the channels sampled), `Texture2D_8713F080` (Thickness Map); floats `Wind_Intensity`, `Wind_Speed`, `Wind_Yaw`, `Wind_Blast`, `Wind_Ripples`, `Wind_Turbulence`, `Wind_Wavelength`, `Distance_Fade_Start`, `Distance_Fade_End`, `Animation_Cutoff`, `_GroundFalloff`, `Vector1_a5b8b09028ce49a39f4d090894c89e22` (alpha clip threshold), `Vector1_a6983181c8dc4691ba6a28a34c4223a6` (normal scale), `Vector1_8651797e3e304e108dbd25f9d5a426ba` (smoothness scale). Safer still: duplicate the shipped material (`Ferns/Fern.asset`, which has `m_EnableInstancingVariants: 1`) and only swap textures.
- **Mesh requirement for wind — the one real piece of work**: the `Wind` subgraph reads **UV channel 1** (`UVNode` output channel 1). `UV1.y` is the bend weight (absolute value, then a power) and `UV1.x` is a per-leaf phase offset; the fern graph rotates object-space position about the wind axis by that per-vertex strength. Unity's fern mesh has UV0 and UV1 (`Fern_A.asset` vertex channels 4 and 5). Poly Haven `fern_02` has only UV0. Without UV1 the plant gets no animated motion, only a constant lean from `Wind_Blast`. A script can author it: for each vertex set `uv1.y = saturate(height above pivot / plant height)` and `uv1.x = a hash of the frond` (for example from the vertex's angle around the pivot), then `mesh.SetUVs(1, list)` on a mesh copy saved as an asset. No modelling skill is involved.
- **What the HDRP branch does not include**: the hand-made sun translucency term only runs under URP. `PseudoSubsurface.hlsl` and `MainLight.shadersubgraph` return constants unless `UNIVERSAL_PIPELINE_CORE_INCLUDED` is defined, and the fern routes that term to Emission through `BranchOnRP`'s URP input only. Under HDRP, translucency comes from switching the material to the Colored Translucent type (the `TransmissionTint` block is present in the graph). How to switch the type by script is **UNVERIFIED** and should be handed to the materials research. The shipped fern material is Standard (`_MaterialID: 1`).
- **Motion vectors**: the shipped materials disable the `MOTIONVECTORS` pass, so moving foliage has no per-object motion vectors. Acceptable for version 1.
- **Licence**: the package is under the Unity Companion License (`SG/LICENSE.md`), not CC0. Using the shader graphs in a Unity project is the intended use. Using the sample **meshes and textures** (Unity fern, nettle, grass blades, clover) would be a deviation from the "CC0 only" asset rule and needs the owner's decision. The shaders can be used with Poly Haven meshes and textures.

### 7.4 Trees

There is no ready wind shader for arbitrary tree meshes in either package. The fern shader rotates vertices about the object pivot by a per-vertex weight; applied to a 15 m tree it would shear leaves away from a static trunk unless the trunk used the same shader and weights. That is a version 2 task.

### 7.5 Can an agent write a `.shadergraph` by hand?

Honest assessment: **authoring one from scratch is not a good bet; copying one and making small text edits is fine.**

- The format is a sequence of JSON objects cross-referenced by object ids. Every node, every slot, every property, the target and each of its data blocks (`SystemData`, `BuiltinData`, `LightingData`, `HDLitData`) is a separate object with its own version field. `Fern.shadergraph` is 342 objects and 263 KB; the `Wind` subgraph alone is 127 objects.
- Block nodes use serialized descriptors (`VertexDescription.Position`, `SurfaceDescription.BaseColor`, ...) that must match what the active target expects. Edges reference node id plus numeric slot id.
- There is no feedback loop other than a full Unity import and reading `Editor.log`. A structural mistake yields an error shader or a silently wrong picture.
- Low-risk edits to a copied graph: changing a default value or a boolean in the target data, and editing the plain-text `PseudoSubsurface.hlsl`.
- The Shader Graph editor types (`GraphData` and nodes) are internal, so generating graphs through the C# API is not an option either.

### 7.6 Fallback

Static foliage in version 1 is fully acceptable:
- Trees and anything multi-material: `HDRP/Lit`, alpha clipping, double-sided, configured like the HDRP `Leaf.mat` sample. No motion.
- If the copied detail graphs give any import trouble: the same `HDRP/Lit` setup on ferns and grass. Instanced details only need an instancing-enabled HDRP material.
- `WindZone` is not available at all today: `com.unity.modules.wind` exists as a built-in module but is not in `Packages/manifest.json`. None of the sample shaders use it (they are driven by material properties and time), so nothing is lost.

---

## 8. Batch-mode behaviour

| # | Topic | Finding | Status |
|---|---|---|---|
| 1 | Project lock | "You can't open a project in batch mode while the Editor has the same project open" | Confirmed, `DOC/Manual/EditorCommandLineArguments.html` |
| 2 | `-nographics` | "Unity doesn't initialize the graphics device ... doesn't allow you to bake GI". No rendering, so no captures. The existing capture command does not use it (`Assets/Game/Editor/SceneCapture.cs:11`). Do not use it for the forest builder either: terrain basemap generation is a GPU render of the terrain shader onto a quad (`HDRP/Documentation~/terrain-master-stack-reference.md:14-17`) | Confirmed for captures and GI; effect on terrain asset building **UNVERIFIED** |
| 3 | Shader compilation during `camera.Render()` | "By default, asynchronous Shader compilation is only enabled for requests that originate from the Game View or Scene View" (`DOC/ScriptReference/ShaderUtil-allowAsyncCompilation.html`). A scripted render therefore compiles synchronously: no cyan placeholders, but the first capture of a new scene is slow (TerrainLit and each shader-graph variant). Setting `ShaderUtil.allowAsyncCompilation = false` and checking `ShaderUtil.anythingCompiling` costs nothing | Confirmed by doc |
| 4 | Terrain-tree colliders | Generated "at runtime" only. Edit-mode physics queries and collider-based NavMesh bakes do not see them | Confirmed, `DOC/Manual/terrain-colliders-introduction.html` |
| 5 | Dialogs | Batch mode suppresses dialogs; keep the `Application.isBatchMode` guard the greybox uses (`GreyboxSceneBuilder.cs:60`) | Confirmed |
| 6 | GPU Resident Drawer in edit mode | Allowed under HDRP (`allowInEditMode = true`). Disabled inside asset-import worker processes (`CORE/Runtime/GPUDriven/GPUResidentDrawer.Validator.cs:94-97`). Off, with a warning, unless BatchRendererGroup variants are "Keep All" | Confirmed in source |
| 7 | GPU occlusion culling in captures | Uses depth from "the current frame and the previous frame" (`HDRP/Documentation~/gpu-culling.md:11`). A camera that just teleported needs a few frames; the 16 warm-up renders in `SceneCapture.cs:19,50-51` cover it | Reasoned from doc |
| 8 | Detail patches and tree instance data on the first rendered frames | Built on demand per camera. How many frames a fresh camera needs is **UNVERIFIED**. If ground cover is missing or sparse in the first captures, raise the warm-up count or set `Terrain.collectDetailPatches = false` (documented to keep all patches in memory) for the capture | UNVERIFIED |
| 9 | LOD selection in the orthographic overview | LODGroup and terrain patch LOD are chosen per camera from projected size. A top-down orthographic camera covering 250 m will pick low LODs or cull small objects. Raise `QualitySettings.lodBias` or call `LODGroup.ForceLOD` for that shot | Reasoned; not run |
| 10 | Time during captures | Repeated `camera.Render()` calls in one editor tick do not advance time, so wind is frozen in stills. Fine for stills | Reasoned; not run |
| 11 | Sample import through the Package Manager API | Should not need UI, but **UNVERIFIED** in batch mode. Use the file copy | UNVERIFIED |
| 12 | Rebuild cost | The greybox deletes and recreates its asset folder on every build (`GreyboxSceneBuilder.cs:74-76`). Do not put imported textures, models or copied shaders in a folder that is wiped; reimporting them dominates build time and churns GUIDs | Design consequence |
| 13 | Terrain resources in a player build | A scene with a Terrain in the build list pulls the terrain engine resources in automatically (`DOC/Manual/terrain-Runtime.html`). The builder already registers its scene in `EditorBuildSettings` (`GreyboxSceneBuilder.cs:105`) | Confirmed |

---

## Recommendations for the build

### Scope for version 1

One 256 m × 256 m terrain tile centred on the origin, a playable core of roughly 160 to 180 m inside a raised, densely wooded rim, one trail and one shallow stream. Everything generated by one new static builder class next to `GreyboxSceneBuilder`, same conventions (`-executeMethod`, fixed seed, assets under a scene-specific folder, PlayMode test, capture).

### Build order and exact settings

1. **Assets that survive rebuilds** live outside the regenerated folder: imported textures and models, the copied Unity shader graphs, generated meshes with UV1, terrain layers.
2. **TerrainData**: `new TerrainData()`; `heightmapResolution = 1025`; then `size = (256, 60, 256)`; `alphamapResolution = 1024`; `baseMapResolution = 1024`; `AssetDatabase.CreateAsset` immediately; then heights, layers, alphamaps, details.
3. **Heights**: fBm from `Unity.Mathematics.noise` plus rim, optional erosion, trail and stream carving from two splines, masked blur, one `SetHeights`. Keep `trailMask` and `streamMask` arrays for the later steps. Trail gradient under 15 degrees.
4. **Terrain object**: `Terrain.CreateTerrainGameObject`, parent under `Environment`, position `(-128, 0, -128)`, `drawInstanced = true`, `ignoreQualitySettings = true`, `heightmapPixelError` about 5, `basemapDistance` beyond the fog range, `materialTemplate` = a copy of the default HDRP terrain material with `_EnableHeightBlend = 1`, validated with `HDMaterial.ValidateMaterial`.
5. **Layers**: 4 to 6 `TerrainLayer` assets (hard limit 8) with diffuse, normal and a mask map carrying height in blue; a different `tileSize` per layer; alphamaps painted from slope, masks and noise, normalized.
6. **Trees and large props**: prefab instances with LODGroup (from `_LODn` meshes in the FBX, or Mesh LOD for rocks, logs and stumps) and a trunk `CapsuleCollider`, placed by variable-radius Poisson-disc sampling in class order with species noise fields. Instantiate with `PrefabUtility.InstantiatePrefab(prefab)` then `SetParent` (the only documented overloads are `(Object)` and `(Object, Scene)`). Turn on the GPU Resident Drawer (`gpuResidentDrawerMode = InstancedDrawing`, BatchRendererGroup variants Keep All). Do not use terrain `TreeInstance`s.
7. **Ground cover**: terrain instanced details only. `SetDetailResolution(1024, 32)`, `SetDetailScatterMode(DetailScatterMode.InstanceCountMode)`, prototypes with `usePrototypeMesh = true`, `renderMode = VertexLit`, `useInstancing = true`, single-material readable meshes, instancing-enabled materials; `detailObjectDistance` 40 to 60. Log `DetailPrototype.Validate(out message)` for each.
8. **Wind**: copy the five detail graphs and their dependencies (17 subgraphs, one HLSL file, three textures, each with its `.meta`) from `SG/Samples~` by file copy; generate UV1 bend weights on fern and plant meshes by script; trees static.
9. **NavMesh**: the existing `BakeNavMesh` on `Environment`, unchanged. Add a `NavMeshModifierVolume` over the stream if dinosaurs should avoid or pay for it.
10. **Player**: spawn height from `GetInterpolatedHeight`; leave the controller as is.
11. **Capture**: reuse `SceneCapture` with more warm-up frames if details are missing, async shader compilation disallowed, and a LOD bias for the overview.

### First probes to run once Unity can be launched (each settles an UNVERIFIED item cheaply)

1. Does `HDMaterial.ValidateMaterial` return true for the terrain material, and does height blend visibly work?
2. `SetDetailLayer` index order: write an L-shaped pattern, look at the overview capture.
3. Do instanced details appear in a batch capture after 16 warm-up frames?
4. Do the copied detail shader graphs import without errors in `Editor.log` with URP absent?
5. Do FBX downloads of `lods: true` Poly Haven models contain `_LOD1`, `_LOD2` meshes and does Unity build the LODGroup automatically?
6. Is the GPU Resident Drawer active in the capture (the log shows the validator warning when it is not)?
7. Is the NavMesh carved around trunk colliders and continuous along the trail?

### Decisions that need the owner

- Whether Unity sample **meshes and textures** (Unity Companion License, not CC0) may be used, or only the shader graphs with Poly Haven content.
- Whether changing HDRP asset and Graphics settings (GPU Resident Drawer, BatchRendererGroup variants) is acceptable in this phase.
