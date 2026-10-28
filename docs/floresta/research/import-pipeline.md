# Import pipeline: scanned FBX + PBR textures into HDRP 17.6 by script

Scope: Unity 6000.6.0f1, HDRP 17.6.0, project `/Users/william/Documents/GitHub/gamex`. Nothing in the project was changed and Unity was not launched. Every claim below is tied to a file I read; anything I could not confirm is marked **UNVERIFIED**.

Path aliases used in citations:

| Alias | Path |
| --- | --- |
| `HDRP/` | `/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/Resources/PackageManager/BuiltInPackages/com.unity.render-pipelines.high-definition/` |
| `CORE/` | `.../BuiltInPackages/com.unity.render-pipelines.core/` |
| `MANUAL/` | `/Applications/Unity/Hub/Editor/6000.6.0f1/Documentation/en/Manual/` |
| `SR/` | `/Applications/Unity/Hub/Editor/6000.6.0f1/Documentation/en/ScriptReference/` |
| `IL` | ikdasm disassembly of `UnityEditor.CoreModule.dll`, saved at `scratchpad/forest/scripts/importpipe/il/UnityEditor.CoreModule.il` (130 MB, grep-able for exact signatures and enum values) |
| `PROBE/` | `scratchpad/forest/catalog/importpipe/` (Poly Haven JSON, three `.gltf` descriptors, one 223 KB FBX) |

The project's package cache copy of HDRP (`Library/PackageCache/com.unity.render-pipelines.high-definition@03bdf33c5697`) has an identical `Runtime/Material/Lit` folder (checked with `diff -rq`), so the built-in package source is the source that compiles.

---

## 0. The ten findings that change the design

1. **Mesh LOD is built in.** `ModelImporter.generateMeshLods`, `maximumMeshLod`, `meshLodGenerationFlags` exist in 6000.6 (SR pages + IL). It is geometry-only, reuses the LOD0 vertex buffer, and is documented as weak on "many disconnected pieces" (foliage).
2. **Poly Haven FBX files are Blender exports with `UnitScaleFactor = 1` (cm) and every mesh node carrying `Lcl Rotation (-90,0,0)` and `Lcl Scaling (100,100,100)`.** Measured in `fern_02`, `pine_tree_01`, `boulder_01`. World size should be right with Unity defaults (to be asserted against glTF bounds), but imported child transforms will not be identity.
3. **One FBX holds several variants laid out side by side** (`fern_02_a..d` at 1 m spacing; `pine_tree_01_a/b/c_LOD0` at x = -6, 1, 8 m). The builder must split them into separate prefabs.
4. **Some FBX files contain authored LODs, some do not.** `boulder_01.fbx` has `boulder_01_LOD0..LOD3`; `pine_tree_01.fbx` (648 MB) has only three `_LOD0` meshes, each with a **6.8 / 4.1 / 5.9 million vertex** needle sub-mesh. That tree cannot be used raw.
5. **Alpha is not always in the base colour file.** `fern_02_diff` is RGB with a separate grey `fern_02_alpha` PNG; `pine_tree_01_twig_diff` PNG is RGBA. HDRP/Lit clips on `_BaseColorMap.a * _BaseColor.a` only, so a packing step is mandatory for foliage.
6. **HDRP mask map = R metallic, G AO, B detail mask, A smoothness** (shader source). Poly Haven `arm` = R AO, G roughness, B metallic. Conversion is a byte shuffle plus `255 - roughness`.
7. **Unity/HDRP expect OpenGL (Y+) normals: use `nor_gl`, no green flip.** Must be imported as `TextureImporterType.NormalMap`.
8. **HDRP ships a ready `FoliageDiffusionProfile.asset` and this project's default volume profile already registers it.** No profile creation, no registration, no hash problem for the MVP. The failure tint in 17.6 is **magenta**, not green.
9. **`HDMaterial.SetDiffusionProfile` requires the material to already be a saved asset** (it adds a sub-asset). The greybox order (validate, then `CreateAsset`) must be reversed for that one call.
10. **GPU Resident Drawer cannot be enabled through a typed setter from the current editor asmdef**: `EditorGraphicsSettings.batchRendererGroupShaderStrippingMode` is read-only, and `GPUResidentDrawerMode` lives in an assembly (`Unity.RenderPipelines.GPUDriven.Runtime`) that `Gamex.EditorTools.asmdef` does not reference. Use `SerializedObject` with the property paths in section 6.

---

## 1. ModelImporter options for static scanned props

### 1.1 Property reference (all names verified in `SR/ModelImporter.html` and in IL property table)

| Purpose | Property (type) | Recommended value | Evidence / note |
| --- | --- | --- | --- |
| Unit conversion | `useFileScale` (bool) | `true` (default) | `SR/ModelImporter-useFileScale.html`. `fileScale` is read-only: "For FBX files, the scaling factor is the multiplier that converts the unit of measurement of the imported FBX file to meters" (`SR/ModelImporter-fileScale.html`). |
| Extra scale | `globalScale` (float) | `1f` | `SR/ModelImporter-globalScale.html`. |
| `useFileUnits` | (bool) | leave alone | ".max files only" (`SR/ModelImporter-useFileUnits.html`). |
| Axis | `bakeAxisConversion` (bool) | `false` (default) | Only converts the *file's* axis system (`SR/ModelImporter-bakeAxisConversion.html`). The Poly Haven files already declare Y-up; the -90 X rotation is a per-node `Lcl Rotation`. Whether this flag also removes it is **UNVERIFIED**; do not depend on it. |
| Hierarchy | `preserveHierarchy` (bool) | `true` | Always creates an explicit root, so single-mesh and multi-mesh files look the same to the builder (`MANUAL/FBXImporter-Model.html`, "Preserve Hierarchy"). |
| Extras | `importCameras`, `importLights`, `importVisibility`, `importBlendShapes`, `importAnimation` (bool), `animationType` | all `false`, `ModelImporterAnimationType.None` | SR pages exist for each. |
| Materials | `materialImportMode` (`ModelImporterMaterialImportMode`) | `ImportViaMaterialDescription` (see 1.3) | Enum: `None`, `ImportStandard`, `ImportViaMaterialDescription` (`SR/ModelImporterMaterialImportMode.html`). |
| | `materialLocation` (`ModelImporterMaterialLocation`) | `InPrefab` | `External` exists but is the legacy "extract" mode (XML doc, `MANUAL/FBXImporter-Materials.html`). |
| | `materialName` (`ModelImporterMaterialName`) | `BasedOnMaterialName` | Keeps the FBX material name, needed for sub-mesh mapping. |
| Normals | `importNormals` (`ModelImporterNormals`) | `Import` | FBX has `LayerElementNormal` (parsed from `PROBE/fern_02_1k.fbx`). |
| Tangents | `importTangents` (`ModelImporterTangents`) | `CalculateMikk` | The files carry no tangents (glTF attributes are only `NORMAL, POSITION, TEXCOORD_0`; pine adds `COLOR_0, COLOR_1`). Enum: `Import, CalculateLegacy, CalculateLegacyWithSplitTangents, CalculateMikk, None`. `normalImportMode` / `tangentImportMode` are obsolete - do not use. |
| Vertex colours | `importVertexColors` (bool) | `false` | HDRP/Lit ignores them; pine meshes carry two colour sets on millions of vertices. |
| Read/Write | `isReadable` (bool) | `false` | "Access is always allowed in the Unity Editor outside of the game and rendering loop, regardless of this setting" (`SR/Mesh-isReadable.html`), so editor scripts can still read meshes. |
| Compression | `meshCompression` (`ModelImporterMeshCompression`) | `Off` | `Off, Low, Medium, High`. Quantises vertex data; not worth it for hero scans. |
| Optimise | `meshOptimizationFlags` (`MeshOptimizationFlags`) | `MeshOptimizationFlags.Everything` | Enum in namespace `UnityEditor`: `PolygonOrder, VertexOrder, Everything`. `optimizeMesh` is obsolete. |
| Index size | `indexFormat` (`ModelImporterIndexFormat`) | `Auto` | Switches to 32-bit above 65 535 vertices. |
| Weld | `weldVertices` (bool) | `true` (default) | |
| Colliders | `addCollider` (bool) | `false` | Adds a MeshCollider to *every* imported mesh (`SR/ModelImporter-addCollider.html`), which would include each LOD and every fern. Add colliders in the prefab builder instead. |
| | `SetPreBakeCollisionMesh(bool isConvex, bool preBake)` | optional | Player-build cooking only. |
| Lightmap UVs | `generateSecondaryUV` (bool) | `false` | No baked lightmaps planned for instanced vegetation. |
| Mesh LOD | `generateMeshLods`, `maximumMeshLod`, `meshLodGenerationFlags` | per asset, see section 2 | |

Collider/readability caveat: `SR/Mesh-isReadable.html` lists the exact MeshCollider cases that need a readable mesh (negative scale, skew/shear, non-default cooking options). `MANUAL/FBXImporter-Model.html` says more broadly that a Mesh Collider "must enable this setting for Unity to build your project". The two pages disagree; this only matters for a player build, not for editor play mode. **UNVERIFIED** which one holds - check at the first standalone build.

### 1.2 What the Poly Haven FBX files actually contain (measured)

From `PROBE/fern_02_1k.fbx` (full file, 223 KB) and header-hopping range requests on `boulder_01` (6.5 MB) and `pine_tree_01` (648 MB, 50 requests of about 600 bytes each, no geometry downloaded):

```
magic "Kaydara FBX Binary", version 7400, Creator "Blender (stable FBX IO)"
GlobalSettings: UpAxis=1 (+Y), FrontAxis=2, CoordAxis=0, UnitScaleFactor=1.0 (centimetres)

fern_02:      Model fern_02_b  Lcl Translation (0,0,0)       Lcl Rotation (-90,0,0)  Lcl Scaling (100,100,100)
              Model fern_02_c  Lcl Translation (100,0,0)     same rotation/scale      (100 cm = 1 m apart)
              Model fern_02_a  Lcl Translation (0,0,100)
              Model fern_02_d  Lcl Translation (100,0,100)
              Material fern_02            (no Texture objects in this file)
boulder_01:   Models boulder_01_LOD0, _LOD1, _LOD2, _LOD3   (geometry 3.9 / 1.4 / 0.72 / 0.37 MB)
              Material boulder_01
pine_tree_01: Models pine_tree_01_a_LOD0, _b_LOD0, _c_LOD0  at x = -600, 100, 800 cm
              (geometry 262 / 160 / 227 MB; no LOD1+ in the file)
              Materials pine_tree_01_bark, _trunk_a, _twig, _dead_branches, _trunk_b, _trunk_c
```

A teammate's sweep (`research/_fbx_headers.txt`) shows the same header values (v7400, Up=Y, Unit=1) for about 40 other Poly Haven nature models, so treat this as the house format.

Consequences:

- With `useFileScale = true` the world size should come out right (Blender metres x node scale 100 x file scale 0.01 = 1), with each imported child GameObject keeping `localScale (100,100,100)`, `localEulerAngles (-90,0,0)` and a position offset from the layout. This is arithmetic on the file contents, not an observed import: **UNVERIFIED** until the bounds assertion below passes.
- Do not hard-code how to undo that. Read each child's transform from the imported prefab and either keep it on the child of our prefab, or bake it. Then **assert the world bounds**.
- The reliable size reference is the `.gltf` descriptor, not the catalogue. `PROBE/fern_02_1k.gltf` gives per-mesh POSITION min/max in metres (for example `fern_02_b`: min (-0.528, -0.031, -0.422), max (0.462, 0.397, 0.472)). The catalogue `dimensions` field is in millimetres and matched for the fern (1972 x 1724 x 427) but not for `pine_tree_01` (26720 x 1301 x 17578, which does not fit trees whose glTF bounds reach y = 20.3 m).
- The `.gltf` descriptor is a few kilobytes and also lists vertex/index counts per primitive. Fetch it for every candidate *before* downloading geometry. It is how I found the 6.8 M-vertex pine sub-mesh without downloading 648 MB.

### 1.3 Mapping imported sub-meshes to materials we create

Four mechanisms exist; all names are verified:

| Mechanism | API | Trade-off |
| --- | --- | --- |
| A. Ignore importer materials, assign in our prefab builder | read `renderer.sharedMaterials[i].name`, set `renderer.sharedMaterials` on our instance | No reimport, no ordering constraint. **Recommended.** Requires `materialImportMode != None`, because with `None` every slot is the default material and the FBX names are lost. |
| B. External object remap | `importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "fern_02"), mat)` then `AssetDatabase.WriteImportSettingsIfDirty(path); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate)` (pattern from `SR/AssetImporter.AddRemap.html`) | Imported prefab itself shows our materials, but costs a second import of the FBX. |
| C. Search and remap | `importer.SearchAndRemapMaterials(ModelImporterMaterialName.BasedOnMaterialName, ModelImporterMaterialSearch.Local)` in `OnPreprocessModel` (`SR/ModelImporter.SearchAndRemapMaterials.html`) | Our `.mat` files must exist, with the FBX material names, before the model imports. |
| D. Postprocessor hook | `public Material OnAssignMaterialModel(Material material, Renderer renderer)` returning a persistent asset (`SR/AssetPostprocessor.OnAssignMaterialModel.html`) | Same ordering constraint as C. |

Material and texture names line up: FBX material `pine_tree_01_bark` uses files `pine_tree_01_bark_<map>_<res>`. But the binding is not always one-to-one: in `PROBE/pine_tree_01_1k.gltf`, materials `pine_tree_01_dead_branches` and `pine_tree_01_trunk_c` point at the **bark** textures. Take the material-to-texture table from the `.gltf` descriptor (it also gives `alphaMode`, `alphaCutoff`, `doubleSided`; the fern is `MASK`, cutoff 0.5, doubleSided true).

With `ImportViaMaterialDescription`, HDRP's own `FBXMaterialDescriptionPreprocessor` builds the embedded materials on `HDRP/Lit` (`HDRP/Editor/AssetProcessors/FBXMaterialDescriptionPostprocessor.cs:10-34`). They are unused by our prefabs and harmless.

### 1.4 Where to set the options

- `AssetPostprocessor.OnPreprocessModel()` / `OnPreprocessTexture()` set options before the first import, so a 648 MB FBX is imported once. Override `public override uint GetVersion()` and bump it when rules change (`SR/AssetPostprocessor.GetVersion.html`).
- Keep the postprocessor pure. With parallel import "any code you write for post processors must be self-contained, deterministic... don't use an AssetPostprocessor to change the Editor settings or create new assets on disk" (`MANUAL/ParallelImport.html`). Material, prefab and packed-texture creation belongs in the builder method.
- `OnPostprocessModel(GameObject)` objects "only exist during the import"; a prefab in another file cannot reference them from inside the callback (`SR/AssetPostprocessor.OnPostprocessModel.html`). Build prefabs afterwards from `AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath)`.
- HDRP already runs `ModelPostprocessor.OnPostprocessModel` (adds HD additional data to imported cameras/lights only, `HDRP/Editor/AssetProcessors/ModelPostProcessor.cs`). No conflict.

---

## 2. Automatic LOD generation in 6000.6

### 2.1 Yes: "Mesh LOD" is built into ModelImporter

Exact members (SR pages plus IL):

```csharp
// UnityEditor.ModelImporter
public bool generateMeshLods;                                   // SR/ModelImporter-generateMeshLods.html
public int  maximumMeshLod;                                     // negative = no limit; "if you set this property to 5, the generated LOD levels will only go up to LOD5"
public MeshLodUtility.LodGenerationFlags meshLodGenerationFlags;

// UnityEditor.MeshLodUtility (static)
[Flags] public enum LodGenerationFlags { DiscardOddLevels = 1 } // no "None" member: use default / (MeshLodUtility.LodGenerationFlags)0
public static void GenerateMeshLods(Mesh mesh, int meshLodLimit = -1);
public static void GenerateMeshLods(Mesh mesh, MeshLodUtility.LodGenerationFlags flags, int meshLodLimit = -1);

// Runtime
int Mesh.lodCount;  MeshLodRange Mesh.GetLod(int subMeshIndex, int levelIndex);  MeshLodRange[] Mesh.GetLods(int submesh);
Mesh.LodSelectionCurve Mesh.lodSelectionCurve;                  // lodBias, lodSlope
short Renderer.forceMeshLod;                                    // -1 = automatic
float Renderer.meshLodSelectionBias;                            // added to the computed level, result is floored
static float QualitySettings.meshLodThreshold;                  // higher = earlier switch to coarser LODs
ushort MeshRenderer.globalIlluminationMeshLod;
int RenderParams.forceMeshLod;
```

Inspector names: "Generate Mesh LODs", "Discard Odd Levels", "Limit LODs", "Maximum Levels" (`MANUAL/FBXImporter-Model.html`).

### 2.2 How it works (`MANUAL/lod/mesh-lod-generator.html`, `mesh-lod-introduction.html`)

- Runs at import. Edge-collapse simplification; each level has "approximately half the number of indices as the previous one".
- "The generator doesn't add any new vertices. All LODs reuse the same vertex buffer." Only the index buffer grows, by at most 100%.
- Needs "at least 256 triangles" to start. Stops at the limit, when no more vertices can be removed, or at "around 64" triangles (the API page says "around 64 indices").
- "Works best for organic shapes or relatively flat surfaces with a high vertex density, for example: living creatures, rocks, sculptures." "Might produce undesired results on meshes with a high curvature or meshes that consist of many disconnected pieces."
- No extra GameObjects or components; materials and renderer settings cannot differ per LOD.
- "Generating LODs increases import time."
- This project already has `meshLodThreshold: 1` on all six quality levels (`ProjectSettings/QualitySettings.asset`).

### 2.3 Limits

Documented (`MANUAL/lod/mesh-lod-introduction.html`, `mesh-lod-troubleshooting.html`):

- Particle System, VFX Graph and **static batching always draw LOD0**.
- Cross-fade works only with **GPU Resident Drawer enabled**, and "does not support custom transition zones".
- Skinned meshes: the generator ignores skin weights and blend shapes, so deformation can break; and skinning cost is not reduced ("a Skinned Mesh Renderer deforms LOD0 regardless"). Relevant for the dinosaurs later, not for the forest.
- Triangle topology only.
- "Using Mesh LOD in combination with LOD Group might lead to unexpected outcomes and is not recommended."
- `Graphics.RenderMeshInstanced` and `RenderMeshPrimitives` do not select a Mesh LOD automatically; set `RenderParams.forceMeshLod`.
- Normals are not recalculated on simplified LODs.
- The troubleshooting page shows a tree where "the foliage parts disappear" at LOD4 and texturing breaks on the trunk at LOD2; its advice is to cap levels with Limit LODs or fall back to LOD Group.

Alpha-cutout foliage is not named in the docs, but card foliage is exactly "many disconnected pieces", and a card that loses triangles loses whole leaves. Treat Mesh LOD as **unsuitable for card foliage beyond one or two levels**. The Poly Haven fern clumps are only about 800 to 2400 triangles each (`PROBE/fern_02_1k.gltf` index counts 2352 to 7152), so they need distance culling, not LODs.

Memory limit that matters here: because the vertex buffer never shrinks, a Mesh-LOD pine still keeps all 6.8 M vertices resident per variant. Mesh LOD reduces triangles drawn, not the footprint.

### 2.4 Interaction with LODGroup and with authored `_LODn` meshes

- "Add the suffix `_LODX` to the name of each mesh... Unity automatically creates a LOD Group component" (`MANUAL/lod-group-configure.html`). Maximum eight levels.
- "If the model contains meshes named `_LODx`, Unity imports them as LOD Group components. If you select Generate mesh LODs for such models, Unity generates Mesh LODs for each imported LOD Group index. This workflow is not recommended" (`MANUAL/lod/mesh-lod-generate-lods.html`).
- Trap: `pine_tree_01_a_LOD0`, `_b_LOD0`, `_c_LOD0` are three different trees that all end in `_LOD0`. How the importer groups them is **UNVERIFIED**; assume it is wrong and build LODGroups ourselves: group children by name with the `_LOD\d+` suffix removed, then `lodGroup.SetLODs(new[] { new LOD(screenRelativeTransitionHeight, renderers), ... }); lodGroup.RecalculateBounds();` (`LOD(float, Renderer[])`, `LOD.fadeTransitionWidth`, `LODGroup.fadeMode`, `LODGroup.animateCrossFading` verified in `UnityEngine.CoreModule.xml`).

### 2.5 Interaction with HDRP

- The HDRP package has no Mesh LOD code of its own except a ray-tracing culling flag (`HDRP/Runtime/RenderPipeline/Raytracing/HDRaytracingManager.HDRTASManager.cs:279`) and no mention in `HDRP/Documentation~`. Selection happens in engine culling, and in SRP Core when the GPU Resident Drawer is on (`CORE/Runtime/GPUDriven/Culling/InstanceCuller.cs`, `ComputeMeshLODLevel` and `ComputeMeshLODCrossfade`, around lines 495-560 and 634-661).
- `HDRP/Lit` declares `#pragma multi_compile _ LOD_FADE_CROSSFADE` in every pass (`HDRP/Runtime/Material/Lit/Lit.shader:457` for GBuffer, and the same in each pass), so dithered cross-fade has a shader path.
- HDRP feeds the drawer `supportDitheringCrossFade = QualitySettings.enableLODCrossFade` (`HDRP/Runtime/RenderPipeline/HDRenderPipelineAsset.cs:250`). The project has `enableLODCrossFade: 1` on every quality level. The manual's cross-fade steps name only the URP Asset; the HDRP equivalent is this quality setting. End-to-end cross-fade in HDRP is **UNVERIFIED** until rendered.
- GPU Resident Drawer excludes renderers whose LODGroup has Animate Cross-fading on (`HDRP/Documentation~/make-object-compatible-gpu-rendering.md:9`; exclusion reason `LODAnimateCrossFading` in `CORE/Runtime/GPUDriven/Debug/GPUResidentDrawerDebug.cs:27`). Use `fadeMode = LODFadeMode.CrossFade` with `animateCrossFading = false` and per-LOD `fadeTransitionWidth`.

### 2.6 If Mesh LOD is not enough (offline, no modelling)

1. **Use the authored `_LODn` meshes** where the FBX has them (boulders, many rocks, logs; a teammate's `research/_fbx_tails.txt` lists `_LOD0.._LOD3` names across many assets). Zero tooling.
2. **Mesh LOD as a decimator** (feasible, **UNVERIFIED end to end**): generate Mesh LODs, then write a new compact mesh from one level: `Mesh.GetLod(subMesh, level)` gives an index range "relative to the sub-mesh" (add `SubMeshDescriptor.indexStart`, `SR/MeshLodRange.html`); copy only referenced vertices. That drops the giant vertex buffer. Needs a small probe to confirm how the LOD indices are read back.
3. **Pick lighter source assets.** Catalogue polycounts: `jacaranda_tree` 312 k, `quiver_tree_01` 270 k, `fir_sapling` 433 k, `pine_sapling_small` 398 k, versus `pine_tree_01` 17.4 M, `fir_tree_01` 7.9 M, `island_tree_01` 3.7 M.
4. Blender, gltfpack, meshoptimizer and gltf-transform are **not installed** on this machine (`which` returned nothing, no `/Applications/Blender.app`).

---

## 3. TextureImporter settings per map type

### 3.1 Settings table

All property names verified against the `SR/TextureImporter-*.html` pages.

The last column is what the manual and HDRP's source lead me to expect; the automatic choice is made in native code, so confirm each with `importer.GetAutomaticFormat("Standalone")` (`SR/TextureImporter.GetAutomaticFormat.html`).

| Map | `textureType` | `sRGBTexture` | Alpha | Other | Expected GPU format on macOS Standalone |
| --- | --- | --- | --- | --- | --- |
| Base colour, opaque | `Default` | `true` | `alphaSource = TextureImporterAlphaSource.None` | `mipmapEnabled = true` | DXT1 (4 bpp) with `Compressed`, BC7 (8 bpp) with `CompressedHQ` |
| Base colour + alpha (foliage) | `Default` | `true` | `alphaSource = FromInput`, `alphaIsTransparency = true`, `mipMapsPreserveCoverage = true`, `alphaTestReferenceValue = <material cutoff>` | source must be a real RGBA file (section 4.4) | BC7 or DXT5 (8 bpp) |
| Normal (`nor_gl`) | `NormalMap` | ignored (linear) | - | `flipGreenChannel = false`, `ignorePngGamma = true` | BC5 or DXT5nm. **UNVERIFIED** which |
| Mask map (packed) | `Default` | **`false`** | `alphaSource = FromInput`, `alphaIsTransparency = false` | | BC7 (8 bpp) |
| Height / displacement (optional) | `SingleChannel` or `Default` | `false` | - | 16-bit grey source | not checked |

Notes with evidence:

- HDRP's rule for mask maps: "make sure you disable sRGB (Color Texture) and you set Texture Type to Default" (`HDRP/Documentation~/Mask-Map-and-Detail-Map.md:7`).
- `alphaIsTransparency` dilates colour into transparent texels and "prevents filtering artifacts from forming on their edges" (`SR/TextureImporter-alphaIsTransparency.html`). `mipMapsPreserveCoverage` "rescale[s] the alpha values of computed mipmaps so coverage is preserved"; "always match the alpha cutoff value used in the alpha test" (`SR/TextureImporter-alphaTestReferenceValue.html`). Without it distant foliage thins out.
- `TextureImporterCompression`: `Uncompressed, Compressed, CompressedHQ, CompressedLQ`; HQ is "BC7, ASTC4x4, ..." (`SR/TextureImporterCompression.html`). HDRP's own code comments `TextureImporterCompression.CompressedHQ; // This is BC7 for Mac/PC` (`HDRP/Editor/AssetProcessors/NormalMapFilteringTexturePostprocessor.cs:46`).
- **Set `ignorePngGamma = true` on Poly Haven PNGs.** I read the chunk lists: `fern_02_arm_1k.png` carries `gAMA=0.45455` and `pine_tree_01_bark_nor_gl_1k.png` carries an embedded `iCCP` profile; others carry neither. A gamma applied to a normal or mask map silently shifts the data.
- **Do not end a file name in `_NF` or `_OSNF`.** HDRP's `NormalMapFilteringTexturePostprocessor` takes over any such texture and rewrites its settings (`.../NormalMapFilteringTexturePostprocessor.cs:13-14`).
- Readability: keep `isReadable = false`. The packer reads source files with `ImageConversion.LoadImage`, not imported textures.
- `maxTextureSize` downsizes at import, so 4k sources can be retuned to 2k later without re-downloading. Setting it on the importer "only affect[s] the default platform setting".

### 3.2 Formats on Apple Silicon

`MANUAL/texture-formats-reference.html`, "Supported texture formats, by platform": on **macOS** DXT1, DXT5, BC5, BC7 are "yes" (BC7 with the note "except... macOS when using WebGL or OpenGL") and ASTC is "no". So a macOS Standalone build on M4 Max uses the desktop BCn family, the same family the manual recommends for desktop: "RGB textures - DXT1 at four bits/pixel. RGBA textures - BC7 (higher quality, slower to compress) or DXT5 (faster to compress), both at eight bits/pixel" (`MANUAL/texture-choose-format-by-platform.html`).

Memory, from the same reference table (BC7/BC5/DXT5 = 1 MB per 1024x1024, DXT1 = 0.5 MB) plus one third for mips:

| Set (base + normal + mask) | 4k | 2k |
| --- | --- | --- |
| BC7 + BC5 + BC7 | about 64 MB | about 16 MB |
| DXT1 + BC5 + BC7 | about 53 MB | about 13 MB |

Thirty 4k sets is roughly 1.6 to 1.9 GB of texture memory. Fine on a 48 GB M4 Max, too much for a console budget.

### 3.3 What transfers to PS5

- No PS5 module is installed (`PlaybackEngines` holds only `MacStandaloneSupport` and `WebGLSupport`), and the local manual tables have no console column. PS5 using BCn natively is general knowledge, **UNVERIFIED locally**.
- What makes the work portable regardless: keep the source images, and set compression only through the **default** platform settings (`textureCompression`, `maxTextureSize`) with **no "Standalone" override**. Each platform then picks its own format when the target is switched.
- Later a PS5-only size cap is one call: `importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "PS5", overridden = true, maxTextureSize = 2048, format = TextureImporterFormat.Automatic })`. `NamedBuildTarget.PS5` exists (XML doc) and the string `"PS5"` appears in IL. The override pattern (`GetPlatformTextureSettings("Standalone")`, `settings.overridden = true`, `SetPlatformTextureSettings(settings)`) is used by HDRP itself at `NormalMapFilteringTexturePostprocessor.cs:49-52`.
- Mesh LOD, GPU Resident Drawer and GPU occlusion culling need only compute shader support (`HDRP/Documentation~/gpu-resident-drawer.md:8`), so they are not Mac-specific.

### 3.4 Import time for 4k sets

**UNVERIFIED - no local measurement exists.** The project logs contain no TextureImporter timings yet (I parsed `Logs/Editor.log` and the four `AssetImportWorkerHW*.log` files: 6974 imports, all search indexing). What is known:

- Source sizes (Poly Haven API, measured): 4k JPG diffuse 3 to 15 MB, 4k `arm` JPG 2.5 to 12 MB, 4k `nor_gl` PNG 33 to 90 MB (16-bit for most models).
- BC7 is the slow format ("higher quality, slower to compress"). `EditorUserBuildSettings.overrideTextureCompression = OverrideTextureCompression.ForceFastCompressor` is "up to 10 times faster... The only downside is that textures might have more compression artifacts" and keeps formats and memory identical (`SR/Build.OverrideTextureCompression.html`). It can also be set with the `-overrideTextureCompression` command line argument, and it persists in `Library/EditorUserBuildSettings.asset`.
- Parallel import exists: `EditorSettings.refreshImportMode = AssetDatabase.RefreshImportMode.OutOfProcessPerQueue` (`SR/EditorSettings-refreshImportMode.html`); the project currently has `m_RefreshImportMode: 0` (in-process). It only applies to a "standard Asset Database refresh".
- My working estimate, to be replaced by a measurement: a few seconds to a few tens of seconds per 4k texture at normal BC7 quality, so plan for up to about a minute per three-map 4k set and 15 to 30 minutes for a first import of 30 sets; results are cached in `Library` afterwards.
- How to measure: Unity 6000.6 writes `Importing 'GUID: <g> - Path: <p> - Importer: <i>'` and `Imported 'GUID: <g>' -> (Import Result ID: ...) in <N> seconds` to the log. A parser for that format is at `scratchpad/forest/scripts/importpipe/logtimes2.py`.

---

## 4. Mapping Poly Haven texture sets to HDRP/Lit

### 4.1 Poly Haven API shapes (verified by fetching `files/<id>` for three models and two textures)

- Single-material assets: top-level keys `Diffuse`, `nor_gl`, `nor_dx`, `arm`, `Rough`, `AO`, `Displacement`, optional `Alpha`, then `<res>` (`1k`..`8k`), then `<format>` (`jpg`, `png`, `exr`), then `{url, size, md5}`.
- Multi-material models: keys are `<part>_<map>`, for example `bark_diff`, `bark_nor_gl`, `bark_arm`, `twig_alpha`, `twig_mask`.
- Geometry: `fbx`, `gltf`, `blend`, `usd`, each with an `include` list of texture paths. The FBX is the same size at every resolution (223 KB for the fern at 1k to 8k); only texture names differ. Download the FBX once and choose maps separately.
- The FBX packages reference `nor_gl` and separate `rough` (EXR or PNG), not `arm`. The glTF packages reference `arm` + `nor_gl` JPGs.
- **File suffixes are not consistent**: `forest_leaves_02_diffuse_4k.jpg` versus `brown_mud_leaves_01_diff_4k.jpg`. Key import rules on the API map key recorded at download time, or rename files at download.
- PNG headers (64-byte range requests): model PNGs are mostly 16-bit; `arm` and `nor_gl` are RGB; `Alpha`, `Rough`, `AO`, `Displacement` are greyscale; `fern_02_diff` is RGB (no alpha), `pine_tree_01_twig_diff` is RGBA.

### 4.2 Property and channel mapping

| Poly Haven map | HDRP/Lit property (`Lit.shader` line) | Notes |
| --- | --- | --- |
| `Diffuse` / `*_diff` | `_BaseColorMap` (10), tint `_BaseColor` (9) | Alpha channel is opacity: `alphaTex * _BaseColor.a` is what gets clipped (`HDRP/Runtime/Material/Lit/LitData.hlsl:231-247`). |
| `nor_gl` | `_NormalMap` (24), strength `_NormalScale` (26, range 0-8) | Tangent space; `_NormalMapSpace` (162) default 0. |
| `arm` (repacked) | `_MaskMap` (14) | See below. |
| `Displacement` | `_HeightMap` (31) | Only with a displacement mode; skip for the MVP. |
| `Alpha` | merged into base colour alpha | No separate opacity slot on HDRP/Lit. |

**Mask map layout, from the shader**: `HDRP/Runtime/Material/Lit/LitDataIndividualLayer.hlsl:256` says `// MaskMap is RGBA: Metallic, Ambient Occlusion (Optional), detail Mask (Optional), Smoothness`, and the samples confirm it: metallic `.r` (line 258), ambient occlusion `.g` (260), detail mask `.b` (199, used only when a detail map is set), smoothness `.a` (242). Same table in `HDRP/Documentation~/Mask-Map-and-Detail-Map.md:15-20`. Each channel is remapped by `_MetallicRemapMin/Max`, `_AORemapMin/Max`, `_SmoothnessRemapMin/Max` (defaults 0 and 1, `Lit.shader:15-22`). Without a mask map the shader uses `_Metallic`, `_Smoothness` and AO = 1.

**Poly Haven `arm` layout**: R = ambient occlusion, G = roughness, B = metallic. Evidence: Poly Haven's own glTF files bind the `arm` image as `pbrMetallicRoughness.metallicRoughnessTexture` (checked in `PROBE/fern_02_1k.gltf`, `boulder_01_1k.gltf`, `pine_tree_01_1k.gltf`), and glTF 2.0 defines roughness in G and metalness in B for that texture. R = AO follows from the name and the same convention; the fern glTF does not bind an occlusion texture, so add a numeric sanity check when packing (mean of B near 0 for rock and plants, mean of R bright).

Conversion: `mask.R = arm.B`, `mask.G = arm.R`, `mask.B = 255`, `mask.A = 255 - arm.G`.

### 4.3 Normal convention

- "Unity uses Y+ normal maps, sometimes known as OpenGL format" (`MANUAL/StandardShaderMaterialParameterNormalMap.html`).
- Use Poly Haven **`nor_gl`** with `flipGreenChannel = false`. If only a DirectX map exists, set `importer.flipGreenChannel = true` ("useful if the normal map uses a different convention to what Unity expects", `SR/TextureImporter-flipGreenChannel.html`).
- It must be imported as `TextureImporterType.NormalMap`: HDRP samples `_NormalMap` through `SAMPLE_UVMAPPING_NORMALMAP` (`LitDataIndividualLayer.hlsl:108`), whose unpack function is `UnpackNormalMapRGorAG` on desktop (`CORE/ShaderLibrary/Sampling/SampleUVMappingInternal.hlsl:33-40`), which expects Unity's packed normal encoding.
- Prefer the PNG (16-bit for most model assets) over JPG for normals, with `ignorePngGamma = true`.

### 4.4 Converting inside the editor without Pillow

Recommended: read the **source files** with `ImageConversion.LoadImage`, shuffle bytes, write a PNG into the project. Verified API facts (`SR/ImageConversion.LoadImage.html`): signature `public static bool LoadImage(Texture2D tex, byte[] data, bool markNonReadable)`; "JPG files are loaded into RGB24, PNG files are loaded into ARGB32, EXR into RGBAFloat"; "loads the texture data without gamma correction". So 16-bit PNGs are read at 8 bits, which is all a mask or alpha channel needs. `EncodeToPNG` is already used by `Assets/Game/Editor/SceneCapture.cs`.

```csharp
static Color32[] ReadPixels(string filePath, out int width, out int height)
{
    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
    if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(filePath), false))
        throw new IOException($"Cannot decode {filePath}");
    width = texture.width; height = texture.height;
    Color32[] pixels = texture.GetPixels32();
    Object.DestroyImmediate(texture);
    return pixels;
}

static void WritePng(string filePath, Color32[] pixels, int width, int height)
{
    var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
    texture.SetPixels32(pixels);
    texture.Apply(false, false);
    File.WriteAllBytes(filePath, texture.EncodeToPNG());
    Object.DestroyImmediate(texture);
}

// arm (R=AO, G=roughness, B=metallic)  ->  HDRP mask (R=metallic, G=AO, B=detail mask, A=smoothness)
static void WriteMaskMap(string armPath, string maskPngPath)
{
    Color32[] p = ReadPixels(armPath, out int w, out int h);
    for (int i = 0; i < p.Length; i++)
        p[i] = new Color32(p[i].b, p[i].r, 255, (byte)(255 - p[i].g));
    WritePng(maskPngPath, p, w, h);
}

// diffuse RGB + separate grey alpha  ->  RGBA base colour
static void WriteBaseColourWithAlpha(string diffusePath, string alphaPath, string outPngPath)
{
    Color32[] colour = ReadPixels(diffusePath, out int w, out int h);
    Color32[] alpha = ReadPixels(alphaPath, out int aw, out int ah);
    if (aw != w || ah != h) throw new InvalidDataException("alpha and diffuse sizes differ");
    for (int i = 0; i < colour.Length; i++) colour[i].a = alpha[i].r;
    WritePng(outPngPath, colour, w, h);
}
```

Then `AssetDatabase.ImportAsset(path)` and let the postprocessor apply section 3 settings by file name. A 4096 x 4096 image is 16.7 M `Color32` values (67 MB per array); process one set at a time.

Alternatives considered:

- **Importer swizzle, no extra file**: `TextureImporter.swizzleR/G/B/A` of type `TextureImporterSwizzle` (`R, G, B, A, OneMinusR, OneMinusG, OneMinusB, OneMinusA, Zero, One`) can express the whole arm-to-mask conversion (`swizzleR = B, swizzleG = R, swizzleB = One, swizzleA = OneMinusG`). The API is verified, but whether an alpha channel is produced when the source file has none is **UNVERIFIED** and there is a warning sign: HDRP's own postprocessor notes that with `TextureImporterAlphaSource.FromInput` and no alpha in the input, Unity "will ignore our alpha values" and works around it with `FromGrayScale` (`NormalMapFilteringTexturePostprocessor.cs:38-43`). Smoothness lives in alpha, so do not rely on swizzle without a pixel read-back probe. It also cannot merge two files, which foliage needs anyway.
- **`Graphics.Blit` with a small shader**: works, but adds a shader asset and a render target read-back for no gain over the byte loop.

---

## 5. Foliage materials in HDRP/Lit

### 5.1 The target state (taken from HDRP's own sample)

`HDRP/Samples~/MaterialSamples/Materials/Lit/Leaf.mat` is an HDRP/Lit leaf and shows exactly what a correct foliage material serialises to:

```
m_ValidKeywords: _ALPHATEST_ON, _DISABLE_SSR_TRANSPARENT, _DOUBLESIDED_ON, _MATERIAL_FEATURE_TRANSMISSION,
                 _NORMALMAP, _NORMALMAP_TANGENT_SPACE, _THICKNESSMAP
m_CustomRenderQueue: 2475        stringTagMap: RenderType: TransparentCutout       m_DoubleSidedGI: 1
_AlphaCutoffEnable: 1   _AlphaCutoff: 0.5   _CullMode: 0   _CullModeForward: 0   _ZTestGBuffer: 3
_DoubleSidedEnable: 1   _DoubleSidedNormalMode: 1   _DoubleSidedConstants: (1, 1, -1, 0)
_MaterialID: 5   _TransmissionEnable: 1   _Thickness: 1   _SurfaceType: 0
_DiffusionProfileHash: <float>   _DiffusionProfileAsset: <guid as Vector4>
+ a hidden MaterialExternalReferences sub-asset holding m_DiffusionProfileReferences
```

Use it as the expected value set in a test. Queue 2475 is `OpaqueDecalAlphaTest = RenderQueue.AlphaTest + 25` (`HDRP/Runtime/RenderPipeline/HDRenderQueue.cs:22`): alpha clipping on with `_SupportDecals` left at its default of 1. With decals disabled on the material it would be 2450.

### 5.2 Properties and keywords (source: `HDRP/Runtime/Material/Lit/Lit.shader`)

| Feature | Property (line) | Values | Keyword / state set by `ValidateMaterial` |
| --- | --- | --- | --- |
| Alpha clipping | `_AlphaCutoffEnable` (97) | 0 / 1 | `_ALPHATEST_ON`; render queue becomes alpha-test; `_ZTestGBuffer = Equal`; tag `RenderType = TransparentCutout` (`Runtime/Material/Unlit/BaseUnlitAPI.cs:19-20, 39-49, 68`) |
| | `_AlphaCutoff` (98), GI mirror `_Cutoff` (205) | 0..1, default 0.5 | |
| Shadow cutoff | `_UseShadowThreshold` (96), `_AlphaCutoffShadow` (99) | | Used in shadow passes (`LitData.hlsl:241-242`) |
| Double sided | `_DoubleSidedEnable` (152) | 0 / 1 | `_DOUBLESIDED_ON`, `_CullMode = Off`, `_CullModeForward = Off`, `doubleSidedGI` (`BaseUnlitAPI.cs:203-224, 263-264`) |
| Normal mode | `_DoubleSidedNormalMode` (153) | `Flip = 0, Mirror = 1, None = 2`, default 1 | writes `_DoubleSidedConstants`: Mirror (1,1,-1,0), Flip (-1,-1,-1,0), None (1,1,1,0) (`Lit/BaseLitAPI.cs:25-43`) |
| Material type | `_MaterialID` (167) | SSS 0, Standard 1, Anisotropy 2, Iridescence 3, Specular Color 4, **Translucent 5** | `_MATERIAL_FEATURE_TRANSMISSION` when Translucent, or SSS with `_TransmissionEnable > 0`; `_MATERIAL_FEATURE_SUBSURFACE_SCATTERING` when SSS (`Lit/LitAPI.cs:108-119`) |
| Transmission toggle | `_TransmissionEnable` (168) | only read for SSS type | |
| Thickness | `_Thickness` (64), `_ThicknessMap` (65), `_ThicknessRemap` (66) | | `_THICKNESSMAP` if a map is set |
| Masks | `_TransmissionMask` (62), `_SubsurfaceMask` (60) | 0..1 | |
| Profile | `_DiffusionProfileHash` (217), `_DiffusionProfileAsset` (216) | set by API | |
| Textures | `_BaseColorMap`, `_NormalMap`, `_MaskMap` | | `_NORMALMAP`, `_NORMALMAP_TANGENT_SPACE`, `_MASKMAP` (`LitAPI.cs:29-47`) |

Which normal mode: "**Mirror**: ...useful when you want to keep the same shapes on both sides of the geometry, for example, for leaves" (`HDRP/Documentation~/Double-Sided.md:15`). It is also the shader default.

Which material type: **Translucent** "only models transmission"; Subsurface Scattering adds the screen-space blur pass (`HDRP/Documentation~/skin-and-diffusive-surfaces-subsurface-scattering.md`). For thin leaves use Translucent (5). "Materials... that use subsurface scattering do not use the Metallic property" (same page), which is fine for plants.

### 5.3 Public `HDMaterial` API versus raw properties

Public members of `UnityEngine.Rendering.HighDefinition.HDMaterial` (`HDRP/Runtime/Material/MaterialExtension.cs`):

| Member (line) | What it writes |
| --- | --- |
| `bool ValidateMaterial(Material)` (369) | Clears all keywords on standard shaders (`material.shaderKeywords = null`, line 361) and rebuilds every keyword, pass state, stencil and queue from property values |
| `void SetSurfaceType(Material, bool transparent)` (419) | `_SurfaceType`, then validates |
| `void SetRenderingPass(Material, HDMaterial.RenderingPass)` (429) | render queue |
| `void SetAlphaClipping(Material, bool)` (495) | `_AlphaCutoffEnable`, then base keyword setup |
| `void SetAlphaCutoff(Material, float)` (504) | `_AlphaCutoff` and `_Cutoff` |
| `void SetDiffusionProfile(Material, DiffusionProfileSettings)` (513) | `_DiffusionProfileHash`; in the editor also `_DiffusionProfileAsset` and the external-reference sub-asset |
| `MaterialId GetMaterialType(this Material)` (542), `bool SetMaterialType(this Material, MaterialId)` (553) | `_MaterialID`, then validates. `MaterialId`: `LitSSS = 0, LitStandard = 1, LitAniso = 2, LitIridescence = 3, LitSpecular = 4, LitTranslucent = 5, LitColoredTranslucent = 6` (117-133) |
| `SetEmissiveColor`, `SetUseEmissiveIntensity`, `SetEmissiveIntensity`, `SetDiffusionProfileShaderGraph`; editor-only `GetDiffusionProfile` | |

Everything else is raw `SetFloat` / `SetTexture` followed by `HDMaterial.ValidateMaterial(material)`: double-sided enable and normal mode, thickness, transmission mask, shadow threshold, `_NormalScale`, the remap ranges, `_SupportDecals`, `_ReceivesSSR`, `_EnableGeometricSpecularAA`, and all texture slots. The enums behind them (`DoubleSidedNormalMode`, `SurfaceType`, ...) are internal, so pass numbers.

Three rules that follow from the source:

1. Never call `EnableKeyword` on an HDRP/Lit material. `ValidateMaterial` wipes keywords first; set properties and validate.
2. Assign textures **before** validating. `_NORMALMAP` and `_MASKMAP` are derived from `material.GetTexture(...)` being non-null.
3. **Save the material before `SetDiffusionProfile`.** It calls `MaterialExternalReferences.GetMaterialExternalReferences(material)`, which does `AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(material))` and `AssetDatabase.AddObjectToAsset(..., material)` (`HDRP/Runtime/Material/MaterialExternalReferences.cs:48-74`). On an unsaved material there is no asset to attach to.

Foliage material, extending the greybox `CreateMaterial` pattern:

```csharp
const string FoliageProfilePath =
    "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipelineResources/FoliageDiffusionProfile.asset";

static Material CreateFoliageMaterial(string assetPath, Texture2D baseColourWithAlpha, Texture2D normal, Texture2D mask, float cutoff)
{
    var material = new Material(Shader.Find("HDRP/Lit"));
    material.SetTexture("_BaseColorMap", baseColourWithAlpha);
    material.SetColor("_BaseColor", Color.white);
    if (normal != null) material.SetTexture("_NormalMap", normal);
    if (mask != null) material.SetTexture("_MaskMap", mask);
    material.SetFloat("_Metallic", 0f);

    material.SetFloat("_AlphaCutoffEnable", 1f);
    HDMaterial.SetAlphaCutoff(material, cutoff);          // writes _AlphaCutoff and _Cutoff
    material.SetFloat("_DoubleSidedEnable", 1f);
    material.SetFloat("_DoubleSidedNormalMode", 1f);      // Mirror
    material.SetFloat("_MaterialID", 5f);                 // MaterialId.LitTranslucent
    material.SetFloat("_Thickness", 1f);                  // 0..1 across the profile's thickness range; tune by eye
    HDMaterial.ValidateMaterial(material);

    AssetDatabase.CreateAsset(material, assetPath);       // must exist on disk before the next call

    var profile = AssetDatabase.LoadAssetAtPath<DiffusionProfileSettings>(FoliageProfilePath);
    HDMaterial.SetDiffusionProfile(material, profile);
    EditorUtility.SetDirty(material);
    return material;
}
```

Limitation to plan around: HDRP/Lit has no wind. `BaseLitAPI.SetupBaseLitKeywords` forces `_VERTEX_WIND` off (`Lit/BaseLitAPI.cs:93`). Moving foliage needs a Shader Graph later; the first version will be static.

### 5.4 Diffusion profile: what exists, how registration works, how the tint happens

**How a material finds its profile.** The material stores a float hash. Each frame HDRP builds a table: slot 0 is the default profile, slots 1+ come from the `DiffusionProfileList` volume component in the camera's volume stack (`HDRP/Runtime/RenderPipeline/HDRenderPipeline.SubsurfaceScattering.cs:82-99`). The shader looks the hash up and **falls back to index 0 when it is not found or is 0** (`HDRP/Runtime/Material/SubsurfaceScattering/SubsurfaceScattering.hlsl:257-276`). Index 0 is `defaultDiffusionProfile.asset`, whose scattering colour is deliberately (0.749, 0, 0.749) times 80. That is the "missing profile" look: **magenta** since HDRP 14 ("Materials that use an unregistered diffusion profile now appear magenta instead of green", `HDRP/Documentation~/whats-new-14.md:127`). One image caption in the docs still says green.

The tint appears when: no profile is assigned, the profile is not in any active Diffusion Profile List, or the profile's hash is still 0 (`SetDiffusionProfileAtIndex` skips those, `.SubsurfaceScattering.cs:108`). Limit: 15 custom profiles per view (`DIFFUSION_PROFILE_COUNT = 16` including the default, `DiffusionProfileSettings.cs:9`).

**State of this project (read from disk):**

- `Assets/HDRPDefaultResources/HDRenderPipelineGlobalSettings.asset:547-550`: `HDRPDefaultVolumeProfileSettings.m_VolumeProfile` points to guid `aa609ae5...`, which is `Assets/HDRPDefaultResources/DefaultSettingsVolumeProfile.asset`.
- That profile already contains a `DiffusionProfileList` with two entries (`DefaultSettingsVolumeProfile.asset:180-187`): guid `404820c4cf36ad944862fa59c56064f0` = HDRP `SkinDiffusionProfile.asset`, and guid `2384dbf2c1c420f45a792fbc315fbfb1` = HDRP **`FoliageDiffusionProfile.asset`** (guids matched against the package `.meta` files).
- The foliage profile: `transmissionMode: 1` (Thin Object), `scatteringDistance (0.749, 0.698, 0.243)`, `thicknessRemap (0, 0.287)` mm, `hash: 1081692787` (already non-zero).
- Auto-registration is **off**: `DiffusionProfileDefaultSettings.m_AutoRegisterDiffusionProfiles: 0` (`HDRenderPipelineGlobalSettings.asset:478-481`). The older top-level `autoRegisterDiffusionProfiles: 1` on line 49 is a deprecated field.
- `supportSubsurfaceScattering: 1` in `Assets/Game/Settings/HDRP.asset:218`; default frame settings include `SubsurfaceScattering` and `Transmission` (`HDRP/Runtime/RenderPipeline/Settings/FrameSettingsDefaults.cs:17-19`).

So for the MVP: load the package's foliage profile and assign it. Nothing to create or register.

**If a custom profile is wanted later.** `DiffusionProfileSettings` is a public `ScriptableObject`; public properties are `scatteringDistance`, `transmissionTint`, `indexOfRefraction`, `worldScale`, `lobeMix`, `diffuseShadingPower`, `primarySmoothnessMultiplier`, `secondarySmoothnessMultiplier`, `borderAttenuationColor` (`DiffusionProfileSettings.cs:263-353`). Transmission mode, thickness remap and texturing mode sit in the internal `profile` field; reach them with `SerializedObject` paths `profile.transmissionMode`, `profile.thicknessRemap`, `profile.texturingMode` (names confirmed by the asset YAML).

The trap is the hash. It is derived from the asset GUID (`HDRP/Runtime/Utilities/DiffusionProfileHashTable.cs:43-58`) and "in case the diffusion profile is not yet saved on the disk, we don't generate the hash". `OnEnable` runs during `CreateInstance`, before a path exists, so a profile made with `CreateInstance` + `CreateAsset` still has hash 0 in that session. HDRP's own Create menu fixes this by calling `DiffusionProfileHashTable.UpdateDiffusionProfileHashNow(asset)` after `CreateAsset` (`HDRP/Editor/RenderPipeline/HDRenderPipelineMenuItems.cs:250-256`), but that class is internal. Options, in order of preference:

1. **Copy the package profile**: `AssetDatabase.CopyAsset(FoliageProfilePath, "Assets/.../ForestFoliage.asset")`, then `LoadAssetAtPath`. On load, `OnEnable` sees a hash that is not registered for this object and regenerates it from the new GUID (`DiffusionProfileHashTable.cs:84-95`). Then adjust through the public setters. Reasoned from source, **UNVERIFIED at runtime**: read back `profile.hash` through `SerializedObject` and assert it is non-zero and not 1081692787.
2. Reflection on the internal method (`typeof(DiffusionProfileSettings).Assembly.GetType("UnityEditor.Rendering.HighDefinition.DiffusionProfileHashTable")`).
3. Create the asset in one Unity run and use it in the next.

Then register it, by either route:

- **Scene volume (no project-wide asset touched)**: add a `DiffusionProfileList` override to the scene's global volume profile, the same way `GreyboxSceneBuilder.AddOverride<T>` adds fog: `AddOverride<DiffusionProfileList>(profile).diffusionProfiles.Override(new[] { myProfile });`. `DiffusionProfileList` and its `diffusionProfiles` field are public (`HDRP/Runtime/Lighting/DiffusionProfileList.cs:12, 19`), and the parameter **accumulates** profiles across volumes instead of replacing them (`Interp`, lines 98-137), so the default list stays valid.
- **Default volume profile**: `GraphicsSettings.GetRenderPipelineSettings<HDRPDefaultVolumeProfileSettings>().volumeProfile`, `TryGet(out DiffusionProfileList list)`, append to `list.diffusionProfiles.value`, `EditorUtility.SetDirty`. HDRP's helper for this (`VolumeUtils.TryAddDiffusionProfiles`, `HDRP/Runtime/Utilities/VolumeUtils.cs:143`) is internal.
- Auto-register on material import exists (`HDRP/Editor/AssetProcessors/MaterialPostProcessor.cs:250-263`) but is disabled in this project.

---

## 6. Rendering thousands of instances

### 6.1 What Unity recommends for HDRP (`MANUAL/optimizing-draw-calls-choose-method.html`)

| Feature | HDRP recommendation in the 6000.6 manual |
| --- | --- |
| SRP Batcher | "Enable" (on by default: `enableSRPBatcher = true`, `HDRenderPipelineAsset.cs:149`) |
| GPU Resident Drawer | "Enable" |
| BatchRendererGroup API directly | "Use the GPU Resident Drawer instead, except for advanced use cases" |
| "Enable GPU Instancing" checkbox on materials | "**Disable, to avoid extra shader variants**" |
| Static batching | "**Disable. Static batching isn't compatible with the BRG API or GPU Resident Drawer**" |

Also: "Avoid using the MaterialPropertyBlock API in URP and HDRP", and with everything on, static meshes are taken by static batching first.

### 6.2 Classic GPU instancing

- `Material.enableInstancing = true` plus identical mesh and material. But "if you use URP or HDRP... GPU instancing works only if you disable the SRP Batcher" (`MANUAL/gpu-instancing-enable.html`). In HDRP with the SRP Batcher on, the checkbox mostly just adds variants.
- `Graphics.RenderMeshInstanced(rparams, mesh, submeshIndex, instanceData, instanceCount = -1, startInstance = 0)` (`SR/Graphics.RenderMeshInstanced.html`; check the parameter modifier on `rparams` there before writing the call) draws without GameObjects, but then culling, LOD choice (`RenderParams.forceMeshLod`, no automatic Mesh LOD), occlusion and colliders are all ours to write. Not worth it for a first version.

### 6.3 GPU Resident Drawer (GRD)

What it is: it "automatically uses the BatchRendererGroup API to draw GameObjects with GPU instancing" (`HDRP/Documentation~/gpu-resident-drawer.md:4`). Scene objects stay ordinary GameObjects with MeshRenderers, which suits a code-built scene, NavMesh baking and colliders.

Requirements, each with its check in source:

| Requirement | Source |
| --- | --- |
| Graphics API with compute shaders; GameObjects with a **MeshRenderer** | `gpu-resident-drawer.md:8-9` |
| Project Settings > Graphics > BatchRendererGroup Variants = **Keep All** | `gpu-resident-drawer.md:21`; enforced at `CORE/Runtime/GPUDriven/GPUResidentDrawer.Validator.cs:61` |
| HDRP Asset > Rendering > GPU Resident Drawer = **Instanced Drawing** | `gpu-resident-drawer.md:22` |
| `BatchRendererGroup.BufferTarget == RawBuffer` on the platform | `Validator.cs:44` (expected on Metal; **UNVERIFIED** until run) |
| Works in edit mode for HDRP | HDRP passes `allowInEditMode = true` (`HDRenderPipelineAsset.cs:252`; check at `Validator.cs:87`). Not created inside asset import workers (`Validator.cs:96`). |
| Shader supports DOTS instancing | `HDRP/Lit` has `#pragma multi_compile _ DOTS_INSTANCING_ON` in every pass (`Lit.shader:455` and others) |
| Per renderer: no MaterialPropertyBlock, no `OnRenderObject`-style callbacks, Light Probes not "Use Proxy Volume", no realtime GI, LODGroup "Animate Cross-fading" off | `HDRP/Documentation~/make-object-compatible-gpu-rendering.md:5-12`; full reason list (`CustomMaterialPropertyBlock`, `MissingDOTSInstancing`, `TooManySubmeshes`, `NullMaterial`, ...) at `CORE/Runtime/GPUDriven/Debug/GPUResidentDrawerDebug.cs:22-46` |
| Static batching off | "might speed up" (`gpu-resident-drawer.md:48`) and "isn't compatible" (manual table above) |

Costs: "Build times are longer because Unity compiles all the BatchRendererGroup shader variants" (`gpu-resident-drawer.md:15`). Benefit is largest when "multiple GameObjects use the same mesh".

Extra knob: `smallMeshScreenPercentage` (0 to 20) culls GPU-driven renderers below a screen size, ignored for renderers in a LODGroup (`HDRP/Runtime/RenderPipeline/GlobalGPUResidentDrawerSettings.cs:23-26`; tooltip in `HDRP/Editor/RenderPipeline/HDRenderPipelineUI.Skin.cs:425`). That is a cheap distance cull for ferns and ground clutter without LODGroups. `shadowSmallMeshScreenPercentages` does the same per shadow cascade.

### 6.4 GPU occlusion culling

- Uses depth "from the current frame and the previous frame... Unity culls the remaining objects, which are occluded in both frames" (`HDRP/Documentation~/gpu-culling.md:11`). Enable GRD first, then "GPU Occlusion" on the HDRP Asset (`gpu-culling.md:22-23`).
- Applies to Game, SceneView and Preview cameras (`HDRP/Runtime/RenderPipeline/HDRenderPipeline.Prepass.cs:217-218`). The capture cameras in `SceneCapture.cs` are Game cameras, so they qualify.
- "If occlusion culling doesn't have a big effect on your scene, rendering time might increase" (`gpu-culling.md:18`). A dense forest with trunks and boulders is a good case; measure with and without.
- Alpha-tested foliage writes depth in the depth prepass (HDRP always pre-passes alpha-tested materials, `BaseUnlitAPI.cs:37-38`), so it does contribute to the occluder depth.

### 6.5 Enabling it by script

Current project state: `m_BrgStripping: 0` (= `KeepIfEntitiesGraphics`) in `ProjectSettings/GraphicsSettings.asset:58`; `gpuResidentDrawerSettings.mode: 0`, `enableOcclusionCullingInCameras: 0` in `Assets/Game/Settings/HDRP.asset:524-529`; `m_BuildTargetBatching: []` (platform defaults) in `ProjectSettings.asset:531`.

Two compile traps:

- `EditorGraphicsSettings.batchRendererGroupShaderStrippingMode` has **only a getter** (the DLL contains `get_batchRendererGroupShaderStrippingMode` and no setter). Enum values from IL: `KeepIfEntitiesGraphics = 0, StripAll = 1, KeepAll = 2`. The serialized field is `m_BrgStripping` (URP's UI scrolls to that name: `com.unity.render-pipelines.universal/Editor/UniversalRenderPipelineAsset/UniversalRenderPipelineAssetUI.Drawers.cs:204`).
- `HDRenderPipelineAsset.gpuResidentDrawerMode` has a public setter (`HDRenderPipelineAsset.cs:265-276`), but its type `GPUResidentDrawerMode` and the interface `IGPUResidentRenderPipeline` are defined in `CORE/Runtime/GPUDriven/` which compiles to **`Unity.RenderPipelines.GPUDriven.Runtime`**. `Assets/Game/Editor/Gamex.EditorTools.asmdef` references only `Gamex.Runtime`, `Unity.AI.Navigation`, `Unity.RenderPipelines.Core.Runtime`, `Unity.RenderPipelines.HighDefinition.Runtime`. Either add that assembly to the asmdef, or avoid the types entirely as below.

```csharp
static void EnableGpuResidentDrawer()
{
    var graphics = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
    graphics.FindProperty("m_BrgStripping").intValue = 2;                  // BatchRendererGroupStrippingMode.KeepAll
    graphics.ApplyModifiedPropertiesWithoutUndo();

    var pipeline = new SerializedObject(GraphicsSettings.defaultRenderPipeline);
    SerializedProperty drawer = pipeline.FindProperty("m_RenderPipelineSettings.gpuResidentDrawerSettings");
    drawer.FindPropertyRelative("mode").intValue = 1;                       // GPUResidentDrawerMode.InstancedDrawing
    drawer.FindPropertyRelative("enableOcclusionCullingInCameras").boolValue = true;
    drawer.FindPropertyRelative("smallMeshScreenPercentage").floatValue = 0f;
    pipeline.ApplyModifiedPropertiesWithoutUndo();

    PlayerSettings.SetStaticBatchingForPlatform(EditorUserBuildSettings.activeBuildTarget, false);
    AssetDatabase.SaveAssets();
}
```

Evidence for each line: `GraphicsSettings.GetGraphicsSettings()` returns the settings `Object` (`SR/Rendering.GraphicsSettings.GetGraphicsSettings.html`); field `m_RenderPipelineSettings` (`HDRenderPipelineAsset.cs:132-133`), `gpuResidentDrawerSettings` (`HDRP/Runtime/RenderPipeline/Settings/RenderPipelineSettings.cs:494`), members `mode`, `smallMeshScreenPercentage`, `enableOcclusionCullingInCameras`, `useDepthPrepassForOccluders`, `shadowSmallMeshScreenPercentages` (`GlobalGPUResidentDrawerSettings.cs:21-35`, and visible in the asset YAML); HDRP's inspector writes `mode.intValue` the same way (`HDRenderPipelineUI.cs:1636`); `PlayerSettings.SetStaticBatchingForPlatform(BuildTarget, bool)` is public (IL and SR). Whether the serialized write to `m_BrgStripping` is picked up immediately is **UNVERIFIED**: log `EditorGraphicsSettings.batchRendererGroupShaderStrippingMode` right after.

How to see that it worked without opening the editor:

- On creation the drawer prints `GPU Resident Drawer created with GPU Occlusion Culling.` (`CORE/Runtime/GPUDriven/GPUResidentDrawer.cs:539`, via `Console.WriteLine`).
- `GPUResidentDrawer.IsInstanceOcclusionCullingEnabled()` is public static (`GPUResidentDrawer.cs:87`) but lives in the GPUDriven assembly.
- HDRP re-checks every frame (`GPUResidentDrawer.ReinitializeIfNeeded()`, called from `HDRenderPipeline.cs:2304`), so a settings change takes effect on the next render.
- For an A/B capture with **no project change at all**, the editor accepts `-force-gpuresidentdrawer` and `-force-gpuocclusion` on the command line (`GPUResidentDrawer.cs:1, 436-449`); forcing skips the stripping check (`Validator.cs:100`).

### 6.6 Comparison

| | GPU Resident Drawer | Material "Enable GPU Instancing" | `Graphics.RenderMeshInstanced` |
| --- | --- | --- | --- |
| Scene representation | normal GameObjects | normal GameObjects | no GameObjects |
| Works with SRP Batcher on | yes | effectively no | n/a |
| Culling / LODGroup / Mesh LOD | automatic, including Mesh LOD cross-fade | automatic | manual (`forceMeshLod`) |
| GPU occlusion culling | yes | no | no |
| Colliders, NavMesh, raycasts for stealth | unchanged | unchanged | must be rebuilt separately |
| Setup cost | two settings + stripping mode | none | a custom system |

---

## 7. Probe artefacts left for the team

| Path (under `scratchpad/forest/`) | Content |
| --- | --- |
| `catalog/importpipe/files_*.json`, `info_*.json` | Poly Haven file listings for `fern_02`, `pine_tree_01`, `boulder_01`, `forest_leaves_02`, `brown_mud_leaves_01` |
| `catalog/importpipe/*.gltf` | glTF descriptors (JSON only) for the three models |
| `catalog/importpipe/fern_02_1k.fbx` | the one complete geometry file fetched (223 KB), to verify node transforms |
| `scripts/importpipe/phfiles.py`, `gltfprobe.py`, `pngprobe.py`, `pngchunks.py` | listing and header probes (run with `python3 -I`) |
| `scripts/importpipe/fbxparse.py`, `fbxhop.py` | binary FBX node walker (local file) and remote header-hopper that never downloads geometry |
| `scripts/importpipe/logtimes2.py` | parses Unity 6000.6 import timings from a log |
| `scripts/importpipe/il/UnityEditor.CoreModule.il` | full IL of the editor core module |

Network use was limited to JSON descriptors, 64 to 2048 byte range reads of image headers, about 80 range reads of FBX node headers, and one 223 KB FBX.

## 8. Open items (nothing below has been run in Unity)

| # | Item | How to close it |
| --- | --- | --- |
| U1 | Import time for a 4k set on this machine | Import one set, read the log with `logtimes2.py` |
| U2 | GPU format chosen for normal maps at `CompressedHQ` on macOS | `Debug.Log(importer.GetAutomaticFormat("Standalone"))` |
| U3 | Swizzle producing alpha from an RGB source | Only if the extra mask PNG becomes a problem: blit and read back alpha |
| U4 | Mesh LOD selection and cross-fade under HDRP, with and without GRD | Render one rock at three distances with `Renderer.forceMeshLod = -1`; assert `mesh.lodCount > 1` |
| U5 | GRD initialises in a batch-mode edit-mode capture on Metal | grep the log for `GPU Resident Drawer created` |
| U6 | `m_BrgStripping` write is effective in the same session | log the getter after writing |
| U7 | How the importer groups `a_LOD0 / b_LOD0 / c_LOD0` | Irrelevant if we build LODGroups ourselves |
| U8 | Copying the foliage profile yields a fresh non-zero hash | Only needed for a custom profile |
| U9 | MeshCollider on non-readable meshes in a player build (docs disagree) | First standalone build |
| U10 | PS5 texture formats | Needs the PS5 module; avoided by not using platform overrides |
| U11 | Import time and memory for multi-million-vertex FBX meshes | Avoid: do not import `pine_tree_01` raw |
| U12 | Imported scale and child transforms of a Blender-exported FBX (section 1.2 is derived from the file, not from an import) | Bounds assertion against the glTF descriptor on the first imported model |
| U13 | Embedded material names equal the FBX material names with `materialName = BasedOnMaterialName` | Log `sharedMaterials[i].name` on the first imported model |

---

## Recommendations for the build

**Pipeline shape**

1. Split the work into a download step outside Unity and three editor steps run with `-executeMethod`: (a) pack textures, (b) build materials and prefabs, (c) build the scene. Keep one manifest file as the contract between them: per asset the FBX path, per material the base/normal/arm/alpha files, `alphaMode`, cutoff, double-sided, LOD strategy, collider type.
2. Build the manifest from each asset's **`.gltf` descriptor** (a few KB): it gives material-to-texture bindings, alpha mode, node names, per-mesh bounds in metres and vertex/index counts. Use those counts to reject or flag assets before downloading geometry.
3. Rename textures at download to fixed suffixes (for example `<material>__basecolor`, `__normal`, `__arm`, `__alpha`) because Poly Haven's own suffixes vary (`diff` / `diffuse`). Avoid names ending in `_NF` or `_OSNF`.
4. Put everything under one folder (for example `Assets/Game/Art/Forest/`) and gate an `AssetPostprocessor` on that path so the greybox and package assets are untouched. Keep the postprocessor free of asset creation.

**Models**

5. Importer settings: section 1.1 table as written. Leave `useFileScale = true`, `globalScale = 1`, `preserveHierarchy = true`, `materialImportMode = ImportViaMaterialDescription`, `importTangents = CalculateMikk`, `addCollider = false`.
6. In the prefab builder, treat each FBX child as a variant: group by name without `_LODn`, create one prefab per group with an identity root, keep the child's imported rotation and scale, zero its position. Assign our materials by reading `sharedMaterials[i].name`.
7. Add a test that compares each prefab's renderer bounds with the glTF POSITION min/max (1 to 2% tolerance). That single assertion catches every scale and axis mistake.
8. LOD strategy per asset, never mixed on one object:
   - FBX has `_LOD1+` (boulders, many rocks and logs): build a LODGroup from them, `fadeMode = CrossFade`, `animateCrossFading = false`, `generateMeshLods = false`.
   - Single dense opaque mesh (stumps, roots, trunks, cliffs): `generateMeshLods = true`, start with `maximumMeshLod = -1`, cap it if a low level looks wrong.
   - Card foliage and small plants: no LODs; cull by distance with the drawer's `smallMeshScreenPercentage` or a one-level LODGroup.
9. **Do not use `pine_tree_01` raw**, and treat the other multi-million-polygon trees the same way until their glTF descriptors are checked. Verified for `pine_tree_01`: 4 to 7 million vertices per variant in a single sub-mesh. Not broken down per mesh, catalogue totals only: `pine_sapling_medium` 9.8 M, `fir_tree_01` 7.9 M, `island_tree_03` 4.8 M, `tree_small_02` 4.7 M, `island_tree_01` 3.7 M. For the first forest pick trees whose descriptor shows a workable count, or run the Mesh-LOD-as-decimator probe first. This is the main content risk of the whole effort and should be decided before any bulk download.
10. Colliders by rule in the builder: capsule for trunks, MeshCollider using the coarsest authored LOD for rocks, none for ferns and grass. Sight-line and hiding logic later can use the same colliders.

**Textures**

11. Download: normal as `nor_gl` PNG; base colour and `arm` as JPG; base colour as PNG only when it carries alpha; the `Alpha` map when present. Start at 2k for everything and 4k for ground and trunks; `maxTextureSize` lets us retune without re-downloading.
12. Always produce two packed files with the section 4.4 code: `__mask.png` from `arm`, and for foliage an RGBA `__basecolor.png` from diffuse + alpha. One code path, only APIs the project already uses.
13. Importer rules by suffix: section 3.1 table. Set `ignorePngGamma = true` everywhere, `sRGBTexture = false` on masks, `NormalMap` type on normals with no green flip, coverage-preserving mips on foliage base colour.
14. Use default-platform compression only (`CompressedHQ`), no per-platform overrides, so a later console target chooses its own formats. Turn on `OverrideTextureCompression.ForceFastCompressor` while iterating and switch it back for final captures.
15. Measure one 4k set before importing thirty.

**Materials**

16. Extend the greybox `CreateMaterial` pattern: set textures and floats, `HDMaterial.ValidateMaterial`, `CreateAsset`. Never touch keywords directly.
17. Foliage: alpha clip on, double-sided with Mirror, `_MaterialID = 5`, and the **package `FoliageDiffusionProfile.asset`**, assigned after the material is saved. Compare the result with `Samples~/MaterialSamples/Materials/Lit/Leaf.mat` in a test (keywords, queue 2475, `_CullMode 0`).
18. Add a capture-based check for the failure mode: a magenta cast on foliage means the profile hash is 0 or unregistered.
19. Accept static foliage for this version; note wind as a later Shader Graph task.

**Instancing**

20. Enable GPU Resident Drawer and GPU occlusion culling with the `SerializedObject` snippet in section 6.5, set BatchRendererGroup variants to Keep All, turn static batching off, leave "Enable GPU Instancing" off on materials, and never use MaterialPropertyBlock on forest renderers. Share meshes and materials aggressively: variation comes from rotation, scale and a small number of variants.
21. Before committing to it, run the capture once with `-force-gpuresidentdrawer -force-gpuocclusion` and once without, and compare frame time and the log line. It needs no project change and settles U5.
22. Do not mark forest objects `BatchingStatic`. Navigation and occlusion flags are separate concerns.
