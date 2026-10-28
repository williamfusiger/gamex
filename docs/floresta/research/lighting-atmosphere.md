# Lighting, sky, fog and post-processing for the forest scene (HDRP 17.6, Unity 6000.6.0f1, Metal)

Research phase only. Nothing here was run in Unity (the editor holds the project lock). Every API name below was read
in package source, in a compiled assembly, or in the local docs; the reference is given next to it. Anything that could
only be confirmed by running the editor is labelled **UNVERIFIED**.

Path shorthands used in references:

- `$HDRP` = `/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/Resources/PackageManager/BuiltInPackages/com.unity.render-pipelines.high-definition`
- `$CORE` = `.../BuiltInPackages/com.unity.render-pipelines.core`
- `$DOC` = `/Applications/Unity/Hub/Editor/6000.6.0f1/Documentation/en`
- `$PROJ` = `/Users/william/Documents/GitHub/gamex`

How compiled-assembly checks were done: `monop.exe` from Unity's bundled Mono, wrapped in
`forest/research/tools/mp.sh <assembly> <type>`. It prints the public outline of a type and is a cheap way to confirm
a name before spending a Unity compile cycle.

---

## 0. The ten findings that change the build

1. **No ray tracing and no path tracing on this Mac, whatever the GPU can do.** HDRP gates both on the *build target*:
   only Windows, Xbox Series, PS5 and Switch 2 pass (`$HDRP/Runtime/RenderPipeline/Raytracing/HDRaytracingManager.cs:937-950`).
   Every ray-tracing compute shader and the ray-tracing passes of `Lit.shader` are compiled with
   `#pragma only_renderers d3d11 xboxseries ps5 switch2` (no `metal`).
2. **Everything else in the question list runs on Metal**: SSGI, SSR, SSAO, contact shadows, micro shadows, volumetric
   fog, local volumetric fog, volumetric clouds, water, STP. All 121 non-ray-tracing HDRP compute shaders list `metal`.
3. **The project's HDRP asset already has APV on** (`lightProbeSystem: 1`), volumetrics on, SSAO on, contact shadows on.
   It has **SSR, SSGI, volumetric clouds and water off**. The default camera frame settings already have all of them on,
   so only the asset needs changing.
4. **The HDRP asset can be changed through public API** in 17.6: `HDRenderPipelineAsset.currentPlatformRenderPipelineSettings`
   has a public getter and setter (`$HDRP/Runtime/RenderPipeline/HDRenderPipelineAsset.cs:139`, confirmed in the compiled
   assembly). `SerializedObject` paths are not required, but are listed in section 8 as a fallback.
5. **Default frame settings have no scripting API** (`$HDRP/Documentation~/Frame-Settings-API.md`: "There is currently no
   scripting API to modify default values for the Frame Settings"; the holder class `RenderingPathFrameSettings` is
   internal). Override per camera instead. Only one optional flag is worth overriding.
6. **APV baking hooks `Lightmapping.bakeStarted`**, so a plain synchronous `Lightmapping.Bake()` bakes probes, and the
   code has explicit `Application.isBatchMode` branches. But there are **three silent headless failure modes** (section 3.5):
   the HDRP pipeline instance must exist, the scene must be saved and contain a `ProbeVolume`, and without sky occlusion
   the static sky is black unless a frame was rendered with ambient mode Static.
7. **Sky occlusion in APV is the right GI mode for this project**: the baker then ignores the environment
   (`$CORE/Editor/Lighting/ProbeVolume/ProbeGIBaking.cs:1135,1155-1156`) and the sky is applied at runtime from the dynamic
   ambient probe. One bake serves all three lighting presets and the black-static-sky problem disappears.
   Caveat for a forest: the sky-occlusion bake treats every sub-mesh as opaque
   (`ProbeGIBaking.SkyOcclusion.cs:224-227`), so alpha-cut leaf cards are solid occluders.
8. **Only one directional light can cast shadows** (`$HDRP/Runtime/Lighting/Shadow/HDShadowManager.cs:1104`). Sun and
   moon can both exist, but shadows must be enabled on one of them at a time.
9. **Quality-tier volume components have private custom fields.** `fog.volumetricFogBudget = x` sets a value but not
   its override state, so it has no effect in a profile. Use the tier (`fog.quality.Override(2)`) and edit the tier
   values in the HDRP asset, or write the private parameters through `SerializedObject`.
10. **The greybox night is about 2 stops over-exposed for its own key light and about 5.6 stops brighter than Unity's
    own moonlit reference**, with a key-to-fill ratio near 30:1 where the real sky gives about 5:1. Details in section 7.

---

## 1. What works on Metal and what does not

Platform support in general: HDRP supports "MacOS (minimum version 10.13) using Metal graphics"
(`$HDRP/Documentation~/System-Requirements.md:32`); `HDUtils.IsSupportedGraphicDevice` lists `GraphicsDeviceType.Metal`
(`$HDRP/Runtime/RenderPipeline/Utility/HDUtils.cs:818-836`).

| Feature | On Metal | Evidence |
|---|---|---|
| Ray-traced reflections, GI, AO, shadows, SSS, recursive rendering | **No** | `HDRaytracingManager.cs:927-950`: `IsSupportedRayTracingTarget(BuildTarget)` returns true only for `StandaloneWindows64`, `StandaloneWindows`, `GameCoreXboxSeries`, `PS5`, `Switch2`. `Ray-Tracing-Getting-Started.md:9`: "HDRP only supports ray tracing using the DirectX 12 API". All 24 compute shaders under `Runtime/RenderPipeline/Raytracing/Shaders` use `only_renderers d3d11 xboxseries ps5 switch2`. |
| Path tracing | **No** | `path-tracing-limitations.md:7`: "you can only use HDR path tracing on platforms that use DX12". Gated by `FrameSettingsField.RayTracing`, which is AND-ed with `m_RayTracingSupported` (`HDRenderPipeline.cs:3102`). |
| SSGI | Yes | `Runtime/Lighting/ScreenSpaceLighting/ScreenSpaceGlobalIllumination.compute` lists `metal`. Needs `supportSSGI` in the asset (off in this project). |
| SSR | Yes | `ScreenSpaceReflections.compute` lists `metal`. Needs `supportSSR` (off in this project). |
| SSAO (GTAO) | Yes | `GTAO*.compute` list `metal`. `supportSSAO` is already on. |
| Contact shadows | Yes | `Runtime/Lighting/Shadow/ContactShadows.compute` lists `metal`. `supportContactShadows` is already on. |
| Micro shadows | Yes | Shader-only feature driven by the material AO map; no platform check. Directional lights only (`Override-Micro-Shadows.md:20`). |
| Volumetric fog | Yes | `VolumetricLighting.compute`, `VolumeVoxelization.compute`, `VolumetricLightingFiltering.compute` list `metal`. `supportVolumetrics` is already on. |
| Local volumetric fog | Yes | Same voxelisation path; no `GraphicsDeviceType` checks in `HDRenderPipeline.VolumetricLighting.cs`. |
| Volumetric clouds | Yes, with a Metal-specific copy path | `VolumetricClouds*.compute` list `metal`; manual blending on Metal in `HDRenderPipeline.VolumetricCloudsSky.cs:195,227,363,383`. Needs `supportVolumetricClouds` (off). |
| Water system | Yes, with one restriction | `WaterSimulation.compute`, `WaterLighting.compute` list `metal`. Tessellation is disabled on Metal for instanced-quad water (`Water/HDRenderPipeline.WaterSystem.cs:723-725`). Needs `supportWater` (off). Enabling water also disables MSAA (`FrameSettings.cs:694`). |
| STP upscaler | Yes | `STP.IsSupported()` needs compute shaders and not GLES3 (`$CORE/Runtime/STP/STP.cs:20-34`). HDRP needs hardware dynamic resolution or a forced fixed scale (`HDRenderPipeline.cs:1422-1424`); Metal supports hardware dynamic resolution (`Dynamic-Resolution.md:10`, `HDUtils.cs:838-845`). |
| TAA upscale, FSR 1.0, CAS, Catmull-Rom | Yes | Built-in shader filters, `DynamicResUpscaleFilter` in `$CORE/Runtime/Common/GlobalDynamicResolutionSettings.cs:24-52`. No platform checks. |
| DLSS | **No** | `deep-learning-super-sampling-in-hdrp.md:17`: "HDRP doesn't support DLSS for Metal". |
| FSR 2 | **No in this project** | Compiled only when `com.unity.modules.amd` is present (`Unity.RenderPipelines.HighDefinition.Runtime.asmdef` version defines); not in `Packages/manifest.json`. Whether that module exists for macOS is UNVERIFIED. |
| MetalFX | Not exposed | No occurrence of "MetalFX" in HDRP or SRP Core source. |
| Motion blur | Yes, gather variant only | The scattering variant is disabled on Metal (`HDRenderPipeline.PostProcess.cs:218-219`). |
| Exposure, bloom, depth of field, tonemapping, grading | Yes | All `PostProcessing/Shaders/*.compute` list `metal`. |
| Physically based sky | Yes | `Sky/PhysicallyBasedSky/SkyLUTGenerator.compute` lists `metal`. |
| Adaptive Probe Volumes (runtime and bake) | Yes | Runtime compute shaders list `metal`; the bake's compute ray-tracing backend has explicit Metal branches (`$CORE/Runtime/UnifiedRayTracing/Compute/RadeonRays/RadeonRaysAPI.cs:258,280,297,414`). |
| Vertex-density debug view | No | `HDRP-Features.md:651`. Irrelevant to the build. |

Notes:

- `SystemInfo.supportsRayTracing` on an M4 Max in 6000.6 is UNVERIFIED and does not matter for HDRP effects, because the
  build-target switch rejects `StandaloneOSX` first. It only matters for the *bake* back end, which falls back to compute.
- A far-future PS5 build *can* use ray tracing (`BuildTarget.PS5` is in the list), so nothing designed here blocks that.
- `ProjectSettings.asset` has `metalAPIValidation: 1`. That is an editor-only validation layer and will distort any
  frame-time measurement taken in the editor. Performance figures were not measured in this phase.

---

## 2. Sky options and how to configure them from code

### 2.1 Common pattern (already used by the greybox)

`VolumeProfile.Add<T>()` plus `AssetDatabase.AddObjectToAsset` as in `$PROJ/Assets/Game/Editor/GreyboxSceneBuilder.cs:153-159`.
Two facts about `VolumeParameter` that bite when scripting (`$CORE/Runtime/Volume/VolumeParameter.cs:183-239`):

- `param.Override(x)` sets `overrideState = true` and writes `m_Value` **directly**. It bypasses the clamping `value`
  setter, so `ClampedFloatParameter` ranges are not enforced. Keep values inside the documented ranges yourself.
- Setting `param.value = x` without `overrideState = true` has no effect when the profile is blended.

Sky selection lives on `VisualEnvironment` (`$HDRP/Runtime/Sky/VisualEnvironment.cs:49-53`):

| Field | Type | Values |
|---|---|---|
| `skyType` | `NoInterpIntParameter` | `(int)SkyType.HDRI` = 1, `Gradient` = 3, `PhysicallyBased` = 4 (lines 83-93) |
| `cloudType` | `NoInterpIntParameter` | 0 none, `(int)CloudType.CloudLayer` = 1 |
| `skyAmbientMode` | `SkyAmbientModeParameter` | `SkyAmbientMode.Dynamic` (default) or `Static` |
| `windOrientation`, `windSpeed` | clamped float, float | Drive cloud and HDRI-sky scrolling, not vegetation |
| `planetRadius`, `renderingSpace`, `centerMode`, `planetCenter` | planet set-up | In 17.6 these are on `VisualEnvironment`, not on `PhysicallyBasedSky` |

Shared sky fields on the base class `SkySettings` (`$HDRP/Runtime/Sky/SkySettings.cs:107-134`): `rotation` (0-360),
`skyIntensityMode` (`SkyIntensityMode.Exposure | Lux | Multiplier`), `exposure`, `multiplier`,
`upperHemisphereLuxValue`, `upperHemisphereLuxColor`, `desiredLuxValue` (default 20000), `updateMode`
(`EnvironmentUpdateMode.OnChanged` default), `updatePeriod`, `includeSunInBaking`.

Intensity maths (`SkySettings.cs:206-224`):

- Exposure mode: multiplier = `ColorUtils.ConvertEV100ToExposure(-exposure)`, i.e. `2^exposure / 1.2`.
- Multiplier mode: `multiplier`.
- Lux mode: `desiredLuxValue / upperHemisphereLuxValue`.

### 2.2 Sky ambient mode

- `Dynamic`: the ambient probe comes from the sky currently used for lighting. Required for APV sky occlusion
  (`$HDRP/Documentation~/probevolumes-skyocclusion.md`, "set Ambient Mode to Dynamic"). The ambient probe is computed on
  the GPU and read back, "so the lighting is one frame late" (`Environment-Lighting.md`). That is one reason the capture
  script needs warm-up frames.
- `Static`: the ambient probe comes from the scene's `StaticLightingSky`. Use it only if baked data without sky occlusion
  must match exactly.

`StaticLightingSky` is a public component (`$HDRP/Runtime/Sky/StaticLightingSky.cs:13,97,127`). The Lighting window
creates it as a hidden root object (`$HDRP/Editor/Sky/HDLightingWindowEnvironmentSection.cs:94-122`). From script:

```csharp
var go = new GameObject("StaticLightingSky", typeof(StaticLightingSky));
go.hideFlags = HideFlags.HideInHierarchy;
var sls = go.GetComponent<StaticLightingSky>();
sls.profile = profile;                                 // set the profile first: the setter resets the ID to 0
sls.staticLightingSkyUniqueID = (int)SkyType.HDRI;     // must be a sky present in that profile
```

### 2.3 HDRI Sky (recommended for this phase)

Class `HDRISky : SkySettings` (`$HDRP/Runtime/Sky/HDRISky/HDRISky.cs:13-108`). Fields that matter: `hdriSky`
(`CubemapParameter`, a `VolumeParameter<Texture>`), `distortionMode`, `lockSun`, `sunInitialRotation`, `enableBackplate`
and the back-plate group. Inherited: `rotation`, `skyIntensityMode`, `desiredLuxValue`, and so on.

**Lux mode needs a number the Inspector normally computes.** `upperHemisphereLuxValue` is filled only by
`HDRISkyEditor.GetUpperHemisphereLuxValue()` (`$HDRP/Editor/Sky/HDRISky/HDRISkyEditor.cs:134-161`), which blits the
cubemap through the shader `Hidden/HDRP/IntegrateHDRI` into a 1x1 float target and reads alpha. A builder must do the
same. `HDRenderPipelineRuntimeShaders` is internal, so find the shader by name:

```csharp
// Mirrors HDRISkyEditor.cs:134-161. Needs a graphics device, so do not run with -nographics.
static float UpperHemisphereLux(Texture cubemap, out Vector3 luxColor)
{
    var material = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/HDRP/IntegrateHDRI"));
    material.SetTexture("_Cubemap", cubemap);
    var target = RenderTexture.GetTemporary(1, 1, 0, GraphicsFormat.R32G32B32A32_SFloat);
    Graphics.Blit(Texture2D.whiteTexture, target, material);
    var previous = RenderTexture.active;
    RenderTexture.active = target;
    var readback = new Texture2D(1, 1, GraphicsFormat.R32G32B32A32_SFloat, TextureCreationFlags.None);
    readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
    RenderTexture.active = previous;
    Color c = readback.GetPixel(0, 0);
    float max = Mathf.Max(c.r, c.g, c.b);
    if (max == 0f) max = 1f;
    luxColor = new Vector3(c.r / max, c.g / max, c.b / max) * 0.5f;
    RenderTexture.ReleaseTemporary(target);
    Object.DestroyImmediate(readback);
    CoreUtils.Destroy(material);
    return c.a;
}

var sky = AddOverride<HDRISky>(profile);
sky.hdriSky.Override(cubemap);
sky.skyIntensityMode.Override(SkyIntensityMode.Lux);
sky.upperHemisphereLuxValue.Override(UpperHemisphereLux(cubemap, out Vector3 luxColor));
sky.upperHemisphereLuxColor.Override(luxColor);
sky.desiredLuxValue.Override(5000f);
sky.rotation.Override(0f);
```

Runtime behaviour of that helper in batch mode is UNVERIFIED (the shader name and property are verified:
`$HDRP/Runtime/Sky/HDRISky/IntegrateHDRISky.shader:1,6`). Fallback if it misbehaves: `SkyIntensityMode.Multiplier` with
a hand-tuned value.

**Cubemap import settings for a `.hdr` or `.exr` lat-long panorama** (API confirmed in `UnityEditor.CoreModule.dll`):

```csharp
var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
importer.textureShape = TextureImporterShape.TextureCube;
importer.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;   // lat-long layout
importer.sRGBTexture = false;
importer.mipmapEnabled = true;
importer.filterMode = FilterMode.Trilinear;
importer.wrapMode = TextureWrapMode.Clamp;
importer.maxTextureSize = 4096;                                          // at least the source width
importer.textureCompression = TextureImporterCompression.CompressedHQ;   // HDR desktop format is BC6H
importer.SaveAndReimport();
```

- Layout detection is automatic from the aspect ratio (`$DOC/Manual/class-Cubemap-create.html`); setting `Cylindrical`
  makes it explicit.
- Leave cubemap convolution at its default (`TextureImporterCubemapConvolution.None`). HDRP convolves the sky itself.
- UNVERIFIED: whether `maxTextureSize` limits the source width or the face size. Log `cubemap.width` after import.
  A 4k lat-long should give 1024-pixel faces, an 8k one 2048.
- UNVERIFIED: BC6H on Apple silicon. `TextureImporterFormat.BC6H` and `RGBAHalf` both exist; if the compressed import
  shows banding or fails, use `TextureImporterCompression.Uncompressed` (about 200 MB for 2048-pixel faces, about 50 MB
  for 1024).

**CC0 HDRI candidates** (Poly Haven API verified: `https://api.polyhaven.com/files/<id>` returns
`hdri.<1k|2k|4k|8k|16k|24k>.<hdr|exr>.{url,size,md5}`; URL pattern
`https://dl.polyhaven.org/file/ph-assets/HDRIs/hdr/4k/<id>_4k.hdr`). "Pure sky" variants are chosen on purpose: an HDRI
shot *inside* a forest is already canopy-occluded, and our own trees would occlude it a second time.

| Use | Poly Haven id | Notes from the catalogue | 4k .hdr | 8k .hdr |
|---|---|---|---|---|
| Overcast day | `kloofendal_overcast_puresky` | afternoon, overcast, low contrast, white balance 5303 K | 17.0 MB | 64.7 MB |
| Overcast day (alternative) | `overcast_soil_puresky` | afternoon, overcast, white balance 6900 K, warm horizon glow | 16.9 MB | 59.7 MB |
| Dusk | `qwantani_dusk_2_puresky` | time of day "dusk", low contrast, warm glow on one horizon | 18.0 MB | 69.3 MB |
| Night, moonless | `qwantani_night_puresky` | clear night, Milky Way, low contrast | 21.2 MB | 81.3 MB |
| Night, moon in frame | `qwantani_moon_noon_puresky`, `kloppenheim_02_puresky` | moon high in the sky; unclipped moon disc | 18.8 / 22.0 MB | 75.4 / 84.2 MB |

Thumbnails are in `forest/thumbs/lighting/`, file lists in `forest/catalog/lighting/`. The lower hemisphere of these
"pure sky" files is a mirror of the sky, which is harmless once terrain covers it.

### 2.4 Physically Based Sky (alternative; needed later for a moving time of day)

Class `PhysicallyBasedSky : SkySettings` (`$HDRP/Runtime/Sky/PhysicallyBasedSky/PhysicallyBasedSky.cs:27-180`):

| Field | Default | Meaning |
|---|---|---|
| `type` | `PhysicallyBasedSkyModel.EarthAdvanced` | `EarthSimple` hides most parameters |
| `atmosphericScattering` | true | Aerial perspective on scene geometry |
| `airDensityR/G/B`, `airTint`, `airMaximumAltitude` | Earth values | Rayleigh |
| `aerosolDensity`, `aerosolTint`, `aerosolMaximumAltitude`, `aerosolAnisotropy` | 0.8 anisotropy | Mie haze |
| `ozoneDensityDimmer`, `ozoneMinimumAltitude`, `ozoneLayerWidth` | 1.0 | Ozone |
| `groundTint`, `groundColorTexture`, `groundEmissionTexture`, `groundEmissionMultiplier` | (0.12, 0.10, 0.09) | Planet surface seen from the air |
| `spaceEmissionTexture`, `spaceEmissionMultiplier`, `spaceRotation` | none | Star cubemap |
| `colorSaturation`, `alphaSaturation`, `alphaMultiplier`, `horizonTint`, `zenithTint`, `horizonZenithShift` | neutral | Artistic overrides |

With this sky, directional lights with `interactsWithSky` are attenuated by the atmosphere
(`$HDRP/Runtime/Lighting/Light/HDGpuLightsBuilder.LightLoop.cs:329`), which is why Unity's cheat sheet lists
"Sun (PBS Sky) 130,000 lx" against "Sun (Noon) 100,000 lx". It cannot make an overcast sky by itself; that needs
Volumetric Clouds (`VolumetricClouds.cloudPreset = CloudPresets.Overcast`, `VolumetricClouds.cs:62-71,216`) and
`supportVolumetricClouds`.

### 2.5 Gradient Sky

`GradientSky` (`$HDRP/Runtime/Sky/GradientSky/GradientSky.cs:17-26`): `top`, `middle`, `bottom` (HDR colours),
`gradientDiffusion`. The colours are luminance in nits before the sky intensity multiplier. It has no sun, no horizon
detail and no relation to any light, so it is a placeholder only.

### 2.6 Sun and moon: `Light` plus `HDAdditionalLightData`

`HDAdditionalLightData` has `[RequireComponent(typeof(Light))]` (`$HDRP/Runtime/Lighting/Light/HDAdditionalLightData.cs:37`).
Units and colour are on the built-in `Light` (confirmed in `UnityEngine.CoreModule.dll`): `lightUnit`
(`LightUnit.Lumen | Candela | Lux | Nits | Ev100`), `intensity`, `useColorTemperature`, `colorTemperature`, `color`,
`shadows`, `lightmapBakeType`. `GraphicsSettings.asset` already has `m_LightsUseLinearIntensity: 1` and
`m_LightsUseColorTemperature: 1`.

```csharp
var go = new GameObject("Sun");
var hd = go.AddComponent<HDAdditionalLightData>();
var light = go.GetComponent<Light>();
light.type = LightType.Directional;
light.lightUnit = LightUnit.Lux;
light.intensity = 1500f;
light.useColorTemperature = true;
light.colorTemperature = 6000f;
light.color = Color.white;
light.shadows = LightShadows.Soft;
light.lightmapBakeType = LightmapBakeType.Realtime;
go.transform.rotation = Quaternion.Euler(elevationDegrees, azimuthDegrees, 0f);

hd.angularDiameter = 0.53f;                   // degrees; also drives PCSS penumbra
hd.SetShadowResolutionOverride(true);         // HDAdditionalLightData.cs:3291-3325
hd.SetShadowResolution(2048);                 // script-created lights default to 512 (line 123)
hd.useContactShadow.useOverride = true;       // BoolScalableSettingValue, line 1513
hd.useContactShadow.@override = true;
hd.affectsVolumetric = true;                  // line 1821
hd.volumetricDimmer = 1f;                     // 0..16, line 210
hd.volumetricShadowDimmer = 1f;               // line 1473
hd.interactsWithSky = false;                  // true only with Physically Based Sky
```

Celestial-body fields, all public on `HDAdditionalLightData` (lines 649-796; described in
`$HDRP/Documentation~/reference-light-component.md:74-103`). They only draw anything with Physically Based Sky:

| Field | Default | Meaning |
|---|---|---|
| `interactsWithSky` | true | Draw a disc and light the atmosphere |
| `angularDiameter` | 0.5 | Degrees. Sun 0.53, moon 0.52 |
| `diameterMultiplerMode` (sic), `diameterMultiplier`, `diameterOverride` | false, 1, 0.5 | Disc size on screen only |
| `distance` | 1.5e11 | Sort order between discs; smaller draws on top |
| `celestialBodyShadingSource` | `Emission` | `Emission` = sun, `ReflectSunLight` = moon lit by a directional light, `Manual` |
| `sunLightOverride` | null | Light that illuminates a `ReflectSunLight` body; default is the strongest directional |
| `moonPhase`, `moonPhaseRotation` | 0.2, 0 | Manual mode; 0.5 means fully lit |
| `earthshine` | 1 | Light reflected from the planet onto the body |
| `flareSize`, `flareFalloff`, `flareTint`, `flareMultiplier` | 2, 4, white, 1 | Glow around the disc |
| `surfaceTexture`, `surfaceTint` | null, white | Disc texture |

Up to 16 celestial bodies are supported (`PhysicallyBasedSkyRenderer.cs:345`), but only one directional light may cast
shadows (`HDShadowManager.cs:1104`: "only one directional light can cast shadows at a time"). The light HDRP treats as
"the sun" is the directional light that has shadows enabled (`LightLoop.cs:1391-1395`).

---

## 3. Global illumination without ray tracing: Adaptive Probe Volumes

### 3.1 Enabling

Already enabled in this project:

- HDRP asset: `lightProbeSystem: 1` = `RenderPipelineSettings.LightProbeSystem.AdaptiveProbeVolumes`
  (`RenderPipelineSettings.cs:49-56,434`); `probeVolumeSHBands: 1` (L1); `probeVolumeMemoryBudget: 1024` (Medium).
- Frame settings: `AdaptiveProbeVolume` is on for Camera, Baked/Custom Reflection and Realtime Reflection (decoded from
  `$PROJ/Assets/HDRPDefaultResources/HDRenderPipelineGlobalSettings.asset` with `forest/research/tools/decode_fs.py`).

Related asset switches: `supportProbeVolumeScenarios` (off), `supportProbeVolumeScenarioBlending` (on),
`supportProbeVolumeGPUStreaming` and `supportProbeVolumeDiskStreaming` (off; not needed for a bounded area).

Scene requirements (`$HDRP/Documentation~/probevolumes-use.md`):

- A `ProbeVolume` component (`$CORE/Runtime/Lighting/ProbeVolume/ProbeVolume.cs:14-81`): `mode`
  (`ProbeVolume.Mode.Global | Scene | Local`, default `Local`), `size`, `fillEmptySpaces`, `overridesSubdivLevels`,
  `lowestSubdivLevelOverride`, `highestSubdivLevelOverride`, `overrideRendererFilters`, `minRendererVolumeSize`.
- Probes are placed around renderers that have the `ContributeGI` static flag, a `MeshFilter`, are active and are LOD0,
  plus terrains with `ContributeGI` including their tree prototypes
  (`$CORE/Runtime/Lighting/ProbeVolume/ProbeVolumeGIContributor.cs:111-112,166-175`).
- Set `MeshRenderer.receiveGI = ReceiveGI.LightProbes` on those renderers. The default is `ReceiveGI.Lightmaps`, which
  would make a full bake try to produce lightmaps for scanned meshes that have no lightmap UVs.
- Lights only enter the bake when `lightmapBakeType` is `Mixed` or `Baked`.

### 3.2 Baking set

Class `ProbeVolumeBakingSet` (`$CORE/Runtime/Lighting/ProbeVolume/ProbeVolumeBakingSet.cs`). Public fields (lines 194-270):
`probeOffset`, `simplificationLevels` (2-5, default 3), `minDistanceBetweenProbes` (default 1 m),
`renderersLayerMask`, `minRendererVolumeSize`, `skyOcclusion` (default false), `skyOcclusionBakingSamples` (2048),
`skyOcclusionBakingBounces` (2), `skyOcclusionAverageAlbedo` (0.6), `skyOcclusionBackFaceCulling`,
`skyOcclusionShadingDirection`. Public method `TryAddScene(string guid)` (`ProbeVolumeBakingSet.Editor.cs:79`).

Derived sizes (lines 298-300): minimum brick = 3 x `minDistanceBetweenProbes`; cell = 3^`simplificationLevels` bricks;
maximum probe spacing = `minDistanceBetweenProbes` x 3^`simplificationLevels`. With 2 m and 3 levels: 6 m bricks, 162 m
cells, 54 m maximum spacing.

If the scene has no baking set, the bake creates one automatically, even when the Lighting window was never opened
(`$CORE/Editor/Lighting/ProbeVolume/ProbeVolumeLightingTab.cs:109` static `instance = new()`, and
`PrepareAPVBake` lines 973-1024: "Also handles the case where the UI was never opened"). It is saved as
`<scene folder>/<scene name>/<scene name> Baking Set.asset` (lines 515, 522-538).

To set options such as sky occlusion *before* the first bake, create the set yourself. `SetDefaults()` is internal
(`ProbeVolumeBakingSet.Editor.cs:380`), and without it virtual offset is off and the scenario list is empty, so call it
by reflection:

```csharp
var set = ScriptableObject.CreateInstance<ProbeVolumeBakingSet>();
typeof(ProbeVolumeBakingSet)
    .GetMethod("SetDefaults", BindingFlags.Instance | BindingFlags.NonPublic)
    .Invoke(set, null);                                    // verified name; internal
set.name = "Forest Baking Set";
set.minDistanceBetweenProbes = 2f;
set.simplificationLevels = 3;
set.skyOcclusion = true;
AssetDatabase.CreateAsset(set, bakeFolder + "/Forest Baking Set.asset");
set.TryAddScene(AssetDatabase.AssetPathToGUID(scenePath)); // scene must already be saved
// Optional, serialized internal fields (ProbeVolumeBakingProcessSettings.cs:4-45):
var so = new SerializedObject(set);
so.FindProperty("settings.dilationSettings.enableDilation").boolValue = true;
so.FindProperty("settings.dilationSettings.dilationDistance").floatValue = 1f;
so.ApplyModifiedPropertiesWithoutUndo();
EditorUtility.SetDirty(set);
AssetDatabase.SaveAssets();
```

`singleSceneMode` is an internal serialized field that defaults to true (line 88), which is what a one-scene level wants.
Note a doc/source mismatch: the docs say dilation is "Enabled by default"
(`probevolumes-lighting-panel-reference.md`), the source sets `enableDilation = false` (`ProbeVolumeBakingProcessSettings.cs:14`).

### 3.3 Bake API

| Call | Assembly | Behaviour |
|---|---|---|
| `UnityEditor.Lightmapping.Bake()` | UnityEditor | Synchronous. "Returns when the lightmapping has finished" (`$DOC/ScriptReference/Lightmapping.Bake.html`). Triggers APV through `Lightmapping.bakeStarted` -> `OnBakeStarted` -> `Lightmapping.AddBakeDelegate(BakeDelegate)` (`ProbeGIBaking.cs:889-892,1097-1104`) and HDRP baked reflection probes through `HDBakedReflectionSystem` (`$HDRP/Editor/Lighting/Reflection/HDBakedReflectionSystem.cs:41-45`). |
| `UnityEditor.Lightmapping.BakeAsync()` | UnityEditor | "If -quit is included in the command-line arguments, this method falls back to the synchronous Lightmapping.Bake" (`Lightmapping.BakeAsync.html`). |
| `UnityEngine.Rendering.AdaptiveProbeVolumes.BakeAsync()` | `Unity.RenderPipelines.Core.Editor` | Probes only, with lightmaps disabled (`SetEnableBakedLightmaps(false)`, `AdaptiveProbeVolumes.BakePipelineDriver.cs:32`). Asynchronous: it steps from `EditorApplication.update` (`ProbeGIBaking.cs:1923-1940`), so it cannot finish under `-quit`. Poll `AdaptiveProbeVolumes.isRunning` and call `EditorApplication.Exit` yourself. |
| `AdaptiveProbeVolumes.isRunning`, `AdaptiveProbeVolumes.Cancel()` | same | `ProbeGIBaking.cs:1945,1951` |
| `UnityEditor.Rendering.HighDefinition.LightmappingHDRP.BakeProbe(HDProbe, string path, BakeProbeOptions)` | `Unity.RenderPipelines.HighDefinition.Editor` | Bakes one cube reflection probe synchronously; returns an `Exception` or null (`$HDRP/Editor/Lighting/LightmappingHDRP.cs:30-67`). |

`$PROJ/Assets/Game/Editor/Gamex.EditorTools.asmdef` currently references only the two *Runtime* SRP assemblies. Calling
`AdaptiveProbeVolumes` or `LightmappingHDRP` needs `Unity.RenderPipelines.Core.Editor` and
`Unity.RenderPipelines.HighDefinition.Editor` added. `Lightmapping`, `ProbeVolume`, `ProbeVolumeBakingSet` and
`ProbeReferenceVolume` need nothing new.

Bake settings come from `LightingSettings` (confirmed in `UnityEngine.CoreModule.dll`). The probe job uses
`directSampleCount`, `indirectSampleCount`, `environmentSampleCount`, each multiplied by
`lightProbeSampleCountMultiplier`, plus `maxBounces` and `indirectScale`
(`ProbeGIBaking.LightTransport.cs:240-273`):

```csharp
var settings = new LightingSettings { name = "ForestLighting" };
settings.bakedGI = true;
settings.realtimeGI = false;
settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;   // enum: Enlighten, ProgressiveCPU, ProgressiveGPU, UnityComputeGPU
settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
settings.directSampleCount = 32;
settings.indirectSampleCount = 256;
settings.environmentSampleCount = 256;
settings.lightProbeSampleCountMultiplier = 4f;
settings.maxBounces = 2;
AssetDatabase.CreateAsset(settings, bakeFolder + "/Forest.lighting");
Lightmapping.SetLightingSettingsForScene(scene, settings);
```

Back end on Apple silicon: "The Apple silicon version of the Unity Editor is not compatible with the CPU Progressive
Lightmapper. However, it is compatible with the Progressive GPU Lightmapper" (`$DOC/Manual/progressive-lightmapper.html`).
`ProgressiveCPU` is also marked obsolete in 6000.6. The alternative is `Lightmapper.UnityComputeGPU` together with
`UnityEditor.Rendering.EditorGraphicsSettings.defaultLightBaker = LightBaker.UnityComputeLightBaker`
(`$DOC/ScriptReference/Rendering.EditorGraphicsSettings-defaultLightBaker.html`). `GraphicsSettings.asset` has
`m_DefaultLightBaker: 0`, the first enum member, `ProgressiveLightBaker`. Which back end is faster or more reliable in
batch mode on an M4 Max is UNVERIFIED. The manual warns that on macOS "fallback to CPU is more likely"
(`GPUProgressiveLightmapper.html`), and on Apple silicon there is no CPU lightmapper to fall back to.

### 3.4 Where baked data is stored

- Baking set asset plus streamable blobs in the *same folder as the baking set asset*
  (`ProbeVolumeBakingSet.Editor.cs:410-440`): `<set>.CellBricksData.bytes`, `<set>.CellSharedData.bytes`,
  `<set>.CellSupportData.bytes`, and per scenario `<set>-<scenario>.CellData.bytes`,
  `<set>-<scenario>.CellOptionalData.bytes` (L2), `<set>-<scenario>.CellProbeOcclusionData.bytes`. The default scenario
  name is `ProbeReferenceVolume.defaultLightingScenario`.
- A hidden `ProbeVolumePerSceneData` object is added to the scene and references the set
  (`ProbeVolumeBakingSet.Editor.cs:494-506`). **The scene must be saved again after the bake**, otherwise that reference
  is lost.
- Baked HDRP reflection probes: `<scene folder>/<scene name>/ReflectionProbe-<index>.exr`
  (`$HDRP/Runtime/Utilities/HDBakingUtilities.cs:10,23-31`).
- `Lightmapping.Bake()` also writes the scene's `LightingData.asset` in `<scene folder>/<scene name>/`.

**Folder clash to avoid.** `GreyboxSceneBuilder.Build()` deletes `Assets/Game/Scenes/Greybox`, the scene-named folder,
on every rebuild (`GreyboxSceneBuilder.cs:74`). That is exactly where the auto-created baking set, the reflection probe
textures and the lighting data go. The forest builder must keep its generated materials, profiles and nav mesh in a
folder that is *not* `<scene folder>/<scene name>`, or accept that every rebuild deletes the bake.

### 3.5 Headless failure modes

| # | Failure | Evidence | Mitigation |
|---|---|---|---|
| 1 | APV bake is skipped silently because the HDRP pipeline instance does not exist yet. `InitializeBake()` returns false when `!ProbeReferenceVolume.instance.isInitialized or !enabledBySRP`, with no error. The probe reference volume is initialised in the `HDRenderPipeline` constructor. | `ProbeGIBaking.cs:1258-1259`; `HDRenderPipeline.cs:671-673` | Render at least one frame with any camera before baking (as `SceneCapture` does) and assert `RenderPipelineManager.currentPipeline is HDRenderPipeline` and `ProbeReferenceVolume.instance.isInitialized` (public, `ProbeReferenceVolume.cs:750`). When the editor creates the pipeline in batch mode is UNVERIFIED. |
| 2 | Bake is skipped or errors because the scene is unsaved, has no `ProbeVolume`, or has no `ContributeGI` renderers. In batch mode HDRP will not add a probe volume for you. | `ProbeGIBaking.cs:1281-1283` ("We have no probe volumes"), `ProbeVolumeLightingTab.cs:993-1003`, `HDBakedReflectionSystem.cs:64` (`AreAllOpenedSceneSaved`) | Save the scene first, add the `ProbeVolume` in the builder, and check `ProbeReferenceVolume.instance.DataHasBeenLoaded()` or the size of the `.CellData.bytes` file afterwards. |
| 3 | Sky lighting bakes as black. The static sky is only refreshed when a camera renders with ambient mode `Static` or is a Scene view camera; there is no Scene view in batch mode. At bake start HDRP falls back to `CoreUtils.blackCubeTexture` and logs "Static Lighting Sky is not ready for baking". | `$HDRP/Runtime/Sky/SkyManager.cs:1272-1296,1738-1781` | Preferred: enable `skyOcclusion` on the baking set so the environment is ignored at bake time. Otherwise: add `StaticLightingSky`, temporarily set `skyAmbientMode` to `Static`, render a few frames, then bake. |
| 4 | More than one scene loaded in single-scene mode: returns false with a warning in batch mode. | `ProbeVolumeLightingTab.cs:1005-1014` | Keep one scene open during the build. |
| 5 | Re-bake with a changed cell layout while scenarios exist: returns false in batch mode. | `ProbeVolumeLightingTab.cs:1049-1072` | Delete the old baking set asset and its `.bytes` files before a rebuild that changes probe spacing. |
| 6 | `AdaptiveProbeVolumes.BakeAsync()` under `-quit` never completes. | `ProbeGIBaking.cs:1923-1940` | Use `Lightmapping.Bake()`, or drop `-quit` and exit from an update callback. |
| 7 | Reflection probe bake does nothing: "HDBakedReflectionSystem only works with HDRP" when the pipeline instance is missing. | `HDBakedReflectionSystem.cs:412-417` | Same as 1. |
| 8 | GPU baker runs out of memory or OpenCL fails. | Manual page quoted in 3.3 | Keep sample counts moderate; switch to `UnityComputeGPU` if `ProgressiveGPU` fails. UNVERIFIED on this machine. |

### 3.6 Changing lighting after a bake: sky occlusion against lighting scenarios

From `$HDRP/Documentation~/probevolumes-understand-changing-lighting-at-runtime.md` and `probevolumes-skyocclusion.md`:

- **Sky occlusion**: one bake stores a static sky-visibility value per probe; at runtime the sky colour comes from the
  ambient probe. "You only need to bake once to set up the data you need for a day-night cycle." Limits: surfaces are
  treated as "opaque and a gray color", so "transparent or translucent GameObjects like windows or leaves bounce light
  away instead of transmitting it through"; the doc's advice is to remove `ContributeGI` from such objects.
  Runtime trim: `ProbeVolumesOptions.skyOcclusionIntensityMultiplier` (0-5, `ProbeVolumesOptions.cs:89`).
- **Lighting scenarios**: one bake per lighting set-up, more accurate, includes bounced light from scene lights. Needs
  `supportProbeVolumeScenarios` in the asset, `TryAddScenario(name)` on the set, and
  `ProbeReferenceVolume.instance.lightingScenario = name` at runtime (`ProbeReferenceVolume.cs:818`). Probe positions
  must be frozen between bakes to allow blending.

For a forest where the key lights stay real-time and the three presets differ mainly in sky, sky occlusion is the
cheaper and more robust first step. The leaf-card caveat is real and can only be tuned by looking at renders:
either let canopies contribute GI and raise `skyOcclusionAverageAlbedo` and `skyOcclusionIntensityMultiplier`, or leave
leaf renderers out of `ContributeGI` and let trunks, terrain and rocks define occlusion.

### 3.7 Bake time expectations (estimates, UNVERIFIED)

No bake was run. Reasoning from the code: cost scales with probe count times
(`skyOcclusionBakingSamples` x bounces + the light-transport samples). A local probe volume of 150 x 40 x 150 m holds
at most about 112,000 probes at 2 m spacing and about 900,000 at 1 m if fully subdivided, which a forest nearly is.
With no baked lights and sky occlusion at 2048 samples, expect tens of seconds to a few minutes at 2 m and several
minutes at 1 m on a 40-core Apple GPU, plus editor start-up and shader compilation on the first run. Runtime memory
is small either way (tens of megabytes against a 1024 budget). Start at 2 m, drop to 1 m only if light leaks show.

### 3.8 Reflection probes and planar probes

- Component pair: `ReflectionProbe` + `HDAdditionalReflectionData` (`[RequireComponent(typeof(ReflectionProbe))]`,
  `HDAdditionalReflectionData.cs:11`). API on the base `HDProbe` (`$HDRP/Runtime/Lighting/Reflection/HDProbe.cs`):
  `mode` (`ProbeSettings.Mode.Baked | Realtime | Custom`), `realtimeMode`
  (`ProbeSettings.RealtimeMode.EveryFrame | OnEnable | OnDemand`), `cubeResolution`, `influenceVolume` (`shape`,
  `boxSize`, `boxBlendDistancePositive/Negative`, `sphereRadius`), `multiplier`, `weight`, `fadeDistance`,
  `frameSettings`, `frameSettingsOverrideMask`, `RequestRenderNextUpdate()`.
- Baking: `Lightmapping.Bake()` bakes every probe in Baked mode, or bake one with `LightmappingHDRP.BakeProbe`.
- **Planar probes cannot be baked**: `LightmappingHDRP.BakeProbe` returns "Planar reflection probe baking is not
  supported" (`LightmappingHDRP.cs:64-65`). They are real-time only and cost a second scene render. Not for this phase.
- A baked probe captures *one* lighting state. With three presets, a **Realtime probe in `OnEnable` mode** is simpler:
  it renders once when the scene loads under whatever preset is active, and leaves no baked files.
- Without any probe, glossy surfaces under the canopy reflect the open sky. The frame setting
  `NormalizeReflectionProbeWithProbeVolume` ("Normalize Reflection Probes", `FrameSettings.cs:388-390`) scales probe
  reflections by APV data and is currently **off** in this project's defaults. It is the one frame setting worth
  overriding per camera.

---

## 4. Shadows for dense foliage

### 4.1 Where each setting lives

| Setting | Location | API |
|---|---|---|
| Maximum shadow distance | Volume: `HDShadowSettings.maxShadowDistance` (default 500; project default profile overrides to 150) | `$HDRP/Runtime/Lighting/Shadow/HDShadowSettings.cs:99` |
| Cascade count | Volume: `HDShadowSettings.cascadeShadowSplitCount` (1-4, default 4) | line 107 |
| Cascade splits | Volume: `cascadeShadowSplit0/1/2` (defaults 0.05, 0.15, 0.3) | lines 110-116. Values are **fractions of the maximum distance** (passed as ratios to `ComputeDirectionalShadowMatricesAndCullingPrimitives`, `HDShadowUtils.cs:134`). `Override()` skips the neighbour clamp, so keep them increasing. |
| Cascade borders | Volume: `cascadeShadowBorder0..3` | lines 119-128. Ignored unless `HDRenderPipeline.s_UseCascadeBorders`; otherwise the last cascade fades over 20 % (lines 64-67). |
| Per-light resolution | Light: `HDAdditionalLightData.SetShadowResolutionOverride(bool)`, `SetShadowResolution(int)`, `SetShadowResolutionLevel(int)` | `HDAdditionalLightData.cs:3291-3325`. Default for a script-created light: override 512 (`k_DefaultShadowResolution`, line 123). |
| Resolution tiers | Asset: `hdShadowInitParams.shadowResolutionDirectional` (256/512/1024/2048) | `HDShadowManager.cs:447` |
| Resolution cap | Asset: `hdShadowInitParams.maxDirectionalShadowMapResolution` (2048) | `HDShadowManager.cs:454`; applied in `HDAdditionalLightData.cs:2318` |
| Shadow map precision | Asset: `hdShadowInitParams.directionalShadowsDepthBits` (32) | line 409 |
| Filtering quality | Asset: `hdShadowInitParams.directionalShadowFilteringQuality` (`HDShadowFilteringQuality.Low, Medium, High`; project: Medium) | line 422 |
| PCSS tuning (High only) | Light: `angularDiameter`, `dirLightPCSSBlockerSampleCount`, `dirLightPCSSFilterSampleCount`, `dirLightPCSSMaxPenumbraSize`, `dirLightPCSSMaxSamplingDistance`, `dirLightPCSSMinFilterSizeTexels` | `HDAdditionalLightData.cs:1185-1323` |
| Bias | Light: `normalBias`, `slopeBias`, `shadowNearPlane` | lines 1577, 1600, 1092 |
| Strength and tint | Light: `shadowDimmer`, `shadowTint`, `penumbraTint`, `volumetricShadowDimmer` | lines 1453-1557 |
| Update mode | Light: `shadowUpdateMode` (`ShadowUpdateMode.EveryFrame, OnEnable, OnDemand`) | line 1623 |
| Contact shadows, global switch | Asset: `hdShadowInitParams.supportContactShadows` (on) | line 467 |
| Contact shadows, per-light tier | Asset: `lightSettings.useContactShadow` (false, false, true) | `RenderPipelineSettings.cs:209-217` |
| Contact shadows, per light | Light: `useContactShadow` (`useOverride`, `@override`, `level`); a script-created light has `useOverride = true`, `@override = false`, so they are **off** until set | `HDAdditionalLightData.cs:1508-1513`; `ScalableSettingValue.cs:23-49` |
| Contact shadows, look | Volume: `ContactShadows` (`enable`, `length` 0.15, `opacity`, `distanceScaleFactor` 0.5, `maxDistance` 50, `minDistance`, `fadeDistance` 5, `fadeInDistance`, `rayBias` 0.2, `thicknessScale` 0.15, `quality`) | `$HDRP/Runtime/Lighting/Shadow/ContactShadows.cs:18-63` |
| Contact shadow samples per tier | Asset: `lightingQualitySettings.ContactShadowSampleCount` (6/10/16) | `GlobalLightingQualitySettings.cs:37-39` |
| Micro shadows | Volume: `MicroShadowing` (`enable`, `opacity`) | `$HDRP/Runtime/Lighting/Shadow/MicroShadowing.cs:19-25` |
| Screen-space shadows | Asset: `hdShadowInitParams.supportScreenSpaceShadows` (off) | Only useful with ray tracing; leave off |
| Frame settings | `ShadowMaps`, `ContactShadows`, `Shadowmask`, `ScreenSpaceShadows` | All on in the project defaults |

Asset settings: use the public setter (section 8). `ScalableSetting<T>` is read-only once built
(`ScalableSetting.cs:45`), so replace the object:
`new IntScalableSetting(new[] { 512, 1024, 2048, 4096 }, ScalableSettingSchemaId.With4Levels)`.

### 4.2 Filtering quality

From `$HDRP/Documentation~/shadows-soften.md`: Low and Medium use a 5x5 tent PCF (9 taps) for directional lights; High
uses PCSS, "more resource-intensive to compute so it's not recommended for consoles". PCSS is what gives foliage
shadows a sharp edge near the caster and a soft edge far from it, which is most of what makes dappled forest light
look real. Use High on the Mac; keep it a single asset switch so a console profile can drop to Medium.

### 4.3 Recommended starting values

| Setting | Value | Reason |
|---|---|---|
| `maxShadowDistance` | 120 m | Sight lines in a bounded forest with fog are short |
| `cascadeShadowSplitCount` | 4 | |
| `cascadeShadowSplit0/1/2` | 0.06, 0.17, 0.42 | Cascade ends at about 7, 20, 50 and 120 m |
| Per-light resolution | 2048 | Cascade atlas becomes 4096 x 4096 (`HDShadowManager.cs:915-924`: width and height double for 3-4 cascades), 64 MB at 32 bit |
| `directionalShadowFilteringQuality` | High (PCSS) | Section 4.2 |
| `angularDiameter` | 0.53 sun, 0.52 moon, 2-4 for a sun behind thin cloud | Drives penumbra |
| Contact shadows | enable, `length` 0.12, `maxDistance` 40, `fadeDistance` 8, tier High (16 samples) | Re-attaches small props and grass once cascade texels get coarse |
| Micro shadows | enable, `opacity` 1 | Free detail from scanned AO maps |
| `maxDirectionalShadowMapResolution` 4096 | optional later | Atlas becomes 8192 x 8192, 256 MB |

Cost note: every cascade re-renders the alpha-tested foliage, so shadow passes will be the first thing to dominate the
frame as vegetation density rises. Measure before raising resolution.

---

## 5. Volumetric fog and light shafts

### 5.1 `Fog` volume component

`Fog : VolumeComponentWithQuality` (`$HDRP/Runtime/Lighting/AtmosphericScattering/Fog.cs:13-166`):

| Field | Default | Notes |
|---|---|---|
| `enabled` | false | |
| `colorMode` | `FogColorMode.SkyColor` | or `ConstantColor` |
| `color`, `tint` | grey, white | HDR. `tint` multiplies the sky colour |
| `maxFogDistance` | 5000 | Keep above the camera far plane |
| `mipFogNear`, `mipFogFar`, `mipFogMaxMip` | 0, 1000, 0.5 | Which sky mip colours the fog |
| `baseHeight`, `maximumHeight` | 0, 50 | Constant density below base; exponential fall-off above |
| `meanFreePath` | 400 | "Fog Attenuation Distance": 63 % of background light is lost over this distance |
| `enableVolumetricFog` | false | |
| `albedo` | white | Scattering colour |
| `globalLightProbeDimmer` | 1 | "GI Dimmer": how much APV or ambient light the fog receives |
| `depthExtent` | 64 | Volumetric range in metres; beyond it the fog is analytic and has no shafts |
| `denoisingMode` | `FogDenoisingMode.Gaussian` | `None`, `Reprojection`, `Gaussian`, `Both` |
| `anisotropy` | 0 | -1..1. Positive values scatter forward and brighten shafts seen against the light |
| `sliceDistributionUniformity` | 0.75 | 0 exponential slices, 1 uniform |
| `multipleScatteringIntensity` | 0 | 0..2, blurs the scene through thick fog |
| `quality` | Medium tier | `ScalableSettingLevelParameter`: 0 Low, 1 Medium, 2 High, 3 Custom |
| `directionalLightsOnly` | false | True is cheaper but drops the beacon and any torch |
| `volumetricLightingDensityCutoff` | 0 | Skips lighting in thin fog |
| `screenResolutionPercentage`, `volumeSliceCount` | 12.5 %, 64 | Manual control mode only |

Scripting trap: `fogControlMode`, `volumetricFogBudget` and `resolutionDepthRatio` are plain C# properties over
private or `m_`-prefixed parameters (`Fog.cs:92-157`). Their setters write `.value` only, never the override state, so
in a profile they do nothing. With the tier left at Low, Medium or High, HDRP reads the HDRP asset instead
(`UsesQualitySettings()`, `VolumeComponentWithQuality.cs:78-81`). Two working options:

- `fog.quality.Override((int)ScalableSettingLevelParameter.Level.High);` and, if needed, change
  `lightingQualitySettings.Fog_Budget[2]` and `Fog_DepthRatio[2]` in the asset.
- `fog.quality.Override(ScalableSettingLevelParameter.LevelCount);` (Custom) and write
  `m_VolumetricFogBudget.m_OverrideState`, `m_VolumetricFogBudget.m_Value`, `m_ResolutionDepthRatio.*`,
  `m_FogControlMode.*` through `new SerializedObject(fog)`.

Enable chain (`Fog.cs:168-183`, `FrameSettings.cs:723-727`): asset `supportVolumetrics` (on) and frame settings
`AtmosphericScattering` and `Volumetrics` (on) and `fog.enabled` and `fog.enableVolumetricFog`. Reprojection also needs
frame setting `ReprojectionForVolumetrics` (on).

### 5.2 Cost of each tier

Voxel grid from `HDRenderPipeline.VolumetricLighting.cs:366-412` and the asset's tier values
(`Fog_Budget` 0.166 / 0.33 / 0.666, `Fog_DepthRatio` 0.666 / 0.666 / 0.5):

| Tier | Screen fraction | Slices | Grid at 1920 x 1080 | Voxels |
|---|---|---|---|---|
| Low | 8.7 % | 57 | 167 x 94 x 57 | 0.9 million |
| Medium (current greybox) | 11.1 % | 113 | 213 x 120 x 113 | 2.9 million |
| High | 20.8 % | 170 | 400 x 225 x 170 | 15.3 million |

High is about five times Medium and scales with pixel count (about 44 million voxels at 3024 x 1964). Frame times were
not measured.

### 5.3 What makes shafts through trees

1. A shadow-casting directional light with `affectsVolumetric = true` and `volumetricShadowDimmer = 1`.
2. Shadow maps sharp enough to keep gaps between leaves: 2048 per cascade.
3. Fog dense enough to see near the camera: `meanFreePath` 60-150 m, a low `maximumHeight` for ground mist.
4. `anisotropy` 0.6-0.75. Real mist scatters strongly forward.
5. Enough depth resolution: tier High, `depthExtent` 64-100 m, `sliceDistributionUniformity` 0.75.
6. `denoisingMode` `Both` for stills and static lights; `Gaussian` once lights or shadows move, because reprojection
   ghosts behind moving lights (`Volumetric-Lighting.md`). High anisotropy can flicker with reprojection
   (`fog-volume-override-reference.md`, Anisotropy row).
7. `volumetricDimmer` above 1 (range 0-16) is a non-physical boost for the shafts only.
8. APV must be baked, otherwise fog under the canopy is lit by the unoccluded ambient probe and glows.

Shafts need direct light, so they belong to a hazy-sun variant of the day preset and to the moonlit night. A fully
overcast sky and dusk after sunset have none.

### 5.4 Local Volumetric Fog

Component `LocalVolumetricFog` with a public struct `parameters` of type `LocalVolumetricFogArtistParameters`
(`$HDRP/Runtime/Lighting/VolumetricLighting/LocalVolumetricFog.cs:24-131,201-204`): `albedo`, `meanFreePath`
(default 10), `blendingMode` (default `Additive`), `priority`, `size`, `positiveFade`, `negativeFade`, `invertFade`,
`distanceFadeStart`, `distanceFadeEnd`, `falloffMode` (`Linear | Exponential`), `maskMode` (`Texture | Material`),
`volumeMask` (3D texture), `materialMask`, `textureScrollingSpeed`, `textureTiling`, `textureOffset`, `scaleMode`.

Limits (`$HDRP/Documentation~/create-a-local-fog-effect.md`): no volumetric shadowing of surfaces, and aliasing at the
box edges unless global fog is also on and fades are used. The asset allows 256 on screen
(`maxLocalVolumetricFogOnScreen`). Use a handful of large, soft boxes in hollows and clearings, `meanFreePath` 8-25 m,
exponential fall-off. Not required for the first version.

---

## 6. Exposure, tonemapping, grading and lens effects

Exposure maths used throughout (`$CORE/Runtime/Utilities/ColorUtils.cs:10-11,247-257,279-289`):

- Linear exposure multiplier = `1 / (1.2 x 2^EV100)`.
- Automatic exposure meters to 12.5 % grey, "equivalent ... [to] an incident light meter with a calibration constant of
  C=314". So the neutral exposure for a scene lit with E lux is **EV100 = log2(E / 3.14)**.
- `Exposure.compensation` is subtracted from the EV (`Exposure.compute:24-25`, `HistogramExposure.compute:182`):
  positive is brighter. It also applies in Fixed mode.

### 6.1 Components and fields

| Component | Fields (defaults) | Source |
|---|---|---|
| `Exposure` | `mode` (`ExposureMode.Fixed, Automatic, AutomaticHistogram, CurveMapping, UsePhysicalCamera`), `fixedExposure` (0), `compensation` (0), `limitMin` (-1), `limitMax` (14), `meteringMode` (`Average, Spot, CenterWeighted, MaskWeighted, ProceduralMask`), `adaptationMode` (`Fixed, Progressive`), `adaptationSpeedDarkToLight` (3), `adaptationSpeedLightToDark` (1), `histogramPercentages` (40, 90), `histogramUseCurveRemapping`, `targetMidGray`, `curveMap` | `$HDRP/Runtime/PostProcessing/Components/Exposure.cs:18-165` |
| `Tonemapping` | `mode` (`TonemappingMode.None, Neutral, ACES, Custom, External`), `useFullACES`, custom curve (`toeStrength`, `toeLength`, `shoulderStrength`, `shoulderLength`, `shoulderAngle`, `gamma`), `lutTexture`, `lutContribution` | `Tonemapping.cs:10-268` |
| `ColorAdjustments` | `postExposure` (0), `contrast` (-100..100), `colorFilter`, `hueShift`, `saturation` (-100..100) | `ColorAdjustments.cs:17-41` |
| `WhiteBalance` | `temperature` (-100..100), `tint` (-100..100) | `WhiteBalance.cs:17-23` |
| `LiftGammaGain`, `ShadowsMidtonesHighlights` | `Vector4` trackballs; `shadowsStart/End`, `highlightsStart/End` | same folder |
| `Bloom` | `threshold` (0), `intensity` (0..1), `scatter` (0.7), `tint`, `dirtTexture`, `dirtIntensity`, `anamorphic`, `quality` | `Bloom.cs:37-75` |
| `DepthOfField` | `focusMode` (`DepthOfFieldMode.Off, UsePhysicalCamera, Manual`), `focusDistance`, `focusDistanceMode`, `nearFocusStart/End`, `farFocusStart/End`, `quality` | `DepthOfField.cs:81-129` |
| `MotionBlur` | `intensity` (0), `maximumVelocity` (200), `minimumVelocity` (2), `cameraMotionBlur` (true), `specialCameraClampMode`, `cameraRotationVelocityClamp`, `depthComparisonExtent`, `quality` | `MotionBlur.cs:63-121` |
| `FilmGrain` | `type` (`FilmGrainLookup.Thin1 ... Large02, Custom`), `intensity` (0..1), `response` (0.8) | `FilmGrain.cs:81-99` |
| `Vignette` | `mode`, `color`, `center`, `intensity` (0..1), `smoothness` (0.2), `roundness` (1), `rounded` | `Vignette.cs:35-83` |
| `ChromaticAberration` | `intensity` (0..1), `spectralLut`, `quality` | `ChromaticAberration.cs:19-25` |
| `ScreenSpaceAmbientOcclusion` | `intensity` (0..4), `directLightingStrength` (0..1), `radius` (0.25..5), `temporalAccumulation`, `specularOcclusion`, `quality` | `$HDRP/Runtime/Lighting/ScreenSpaceLighting/ScreenSpaceAmbientOcclusion.cs:19-67` |
| `ScreenSpaceReflection` | `enabled` (true), `usedAlgorithm`, `depthBufferThickness`, `screenFadeDistance`, `accumulationFactor`, `quality` | `ScreenSpaceReflection.cs:67-143` |
| `GlobalIllumination` (SSGI) | `enable` (false), `tracing`, `rayMiss`, `depthBufferThickness`, `fullResolutionSS`, `quality` | `$HDRP/Runtime/Lighting/GlobalIllumination.cs:26-59` |
| `IndirectLightingController` | `indirectDiffuseLightingMultiplier`, `reflectionLightingMultiplier`, `reflectionProbeIntensityMultiplier` | `IndirectLightingController.cs:16-27` |
| `ProbeVolumesOptions` | `normalBias`, `viewBias`, `samplingNoise`, `leakReductionMode`, `occlusionOnlyReflectionNormalization`, `intensityMultiplier`, `skyOcclusionIntensityMultiplier` | `$CORE/Runtime/Lighting/ProbeVolume/ProbeVolumesOptions.cs:34-95` |

What the project's default volume profile already sets (`$PROJ/Assets/HDRPDefaultResources/DefaultSettingsVolumeProfile.asset`):
tonemapping ACES, exposure Automatic, bloom intensity 0.2, motion blur intensity 0.5, ambient occlusion intensity 0.5
with radius 1.5, contact shadows enabled, maximum shadow distance 150, sky type HDRI with HDRP's default sky at
exposure 11. A scene profile only needs to override what differs.

Camera-side settings on `HDAdditionalCameraData` (`$HDRP/Runtime/RenderPipeline/Camera/HDAdditionalCameraData.cs:310-379`):
`antialiasing` (`AntialiasingMode.None | FastApproximateAntialiasing | TemporalAntialiasing | SubpixelMorphologicalAntiAliasing`;
the greybox camera uses the default, None), `TAAQuality`, `taaSharpenStrength`, `taaAntiFlicker`, `dithering` (false),
`stopNaNs` (false), `allowDynamicResolution` (false), `customRenderingSettings` (false).

### 6.2 Physically sensible values

| Effect | Value | Reason |
|---|---|---|
| Anti-aliasing | `TemporalAntialiasing`, `TAAQuality` High, `taaSharpenStrength` 0.5 | Alpha-tested foliage shimmers without it; SSR, SSGI, AO and volumetrics all rely on temporal accumulation |
| `dithering` | true | Hides banding in dark fog gradients |
| `stopNaNs` | true | Cheap insurance with HDR skies and bloom |
| Tonemapping | ACES | Filmic shoulder; already the project default |
| Bloom | `intensity` 0.1-0.2, `scatter` 0.6-0.7, `threshold` 0 | Energy-conserving glow; more reads as a dirty lens |
| Depth of field | Off for gameplay | First-person stealth needs the whole view readable. `UsePhysicalCamera` at f/2.8-f/5.6 only for stills |
| Motion blur | `intensity` 0.2-0.3 | The default 0.5 smears foliage while the player scans for movement |
| Film grain | Thin1 at 0.1-0.15 by day; Medium1 at 0.25-0.35 at night; `response` 0.8 | Real sensors get noisier in low light; also masks banding |
| Vignette | `intensity` 0.2-0.3, `smoothness` 0.4 | Natural lens fall-off is about half to one stop in the corners |
| Chromatic aberration | 0 to 0.05 | Good lenses show almost none |
| Ambient occlusion | `intensity` 1.0, `radius` 1.5-2.0, `directLightingStrength` 0.2 | Grounds vegetation; a little direct occlusion compensates for missing small-scale shadows |
| Contrast, saturation | `contrast` +5 to +10; `saturation` -5 day, -10 dusk, -25 to -35 night | Human colour vision fades in low light |
| White balance | 0 day, about -8 dusk, about -20 to -25 night | The blue night look belongs in grading, not in the light colour |

Capture behaviour in edit mode, which is what `SceneCapture` uses: game cameras have `animateMaterials = false` outside
Play mode (`$CORE/Runtime/Utilities/CoreUtils.cs:1502-1507`), and HDRP then forces exposure adaptation to instantaneous
(`HDRenderPipeline.PostProcess.cs:1504-1505`) and skips motion blur (line 4552). Automatic exposure will therefore
settle immediately in headless captures. Shader time is frozen at zero, so wind and grain do not animate. Raise the
warm-up from 16 to about 32-48 frames once TAA and volumetric reprojection are on.

For converged stills without path tracing, HDRP's accumulation API exists and does not need ray tracing:
`HDRenderPipeline.BeginRecording(int samples, float shutterInterval, ...)`, `PrepareNewSubFrame()`, `EndRecording()`
(`$HDRP/Runtime/RenderPipeline/Accumulation/SubFrameManager.cs:379-408`). Not needed for the first version.

---

## 7. Three lighting presets, and what is wrong with the greybox night

### 7.1 Reference values used

From the local HDRP docs (`$HDRP/Documentation~/Physical-Light-Units.md` and `Images/LightCheatSheet.png`):

| Quantity | Value |
|---|---|
| Very bright sunlight; bright sunlight | 120,000; 110,000 lux |
| Blue sky at midday | 20,000 lux |
| Overcast sky at midday | 1,000-2,000 lux |
| Moonlight with a clear night sky | below 1 lux; cheat sheet: 0.5 lux |
| Starry night without moon, with airglow | 0.002 lux |
| Colour temperature | candle 1,900 K; incandescent 3,000 K; sunlight at noon 5,500 K; cloudy 6,500 K; shade 8,000 K; blue sky 15,000 K |
| Exposure cheat sheet | moonless -2 EV; moonlit 1 EV; interior 4 EV; low sun 7 EV; cloudy 10 EV; sunlit 14 EV |

From general photometry, not in the local docs: full moon 0.05-0.3 lux at about 4,100 K (slightly *warmer* than
sunlight); end of civil twilight about 3.4 lux; clear sunset about 400 lux.

### 7.2 What is physically off in the greybox

Greybox values (`GreyboxSceneBuilder.cs:111-151`): a directional "Moon" of 6 lux, colour (0.62, 0.72, 1.0), elevation
48 degrees; fixed exposure -1; gradient sky with colours between 0.01 and 0.13; shadows at the script default of 512.

1. **Six lux is not moonlight.** It is 12 to 24 times a full moon and sits near the end of civil twilight.
2. **The exposure does not match the light.** Neutral exposure for 6 lux is log2(6 / 3.14) = 0.9 EV. Fixed -1 EV is
   therefore about **1.9 stops brighter than neutral**: an 18 % grey ground under that light lands at 0.43 linear before
   tonemapping, four times the 0.10 that HDRP's own meter aims for. Against Unity's reference (0.5 lux at 1 EV) the
   picture is 6 / 2^-1 divided by 0.5 / 2^1 = 48 times, or **5.6 stops, brighter**. The comment in the code says the
   fixed exposure keeps the night dark; with this light it does the opposite.
3. **The sky is not connected to the light.** The gradient averages roughly 0.03-0.06 nits, about 0.1-0.2 lux over the
   hemisphere. Key to fill is about 30:1. A real sky gives about 5:1 (110,000 lux sun against 20,000 lux sky, and the
   same scattering applies to moonlight). Result: lit patches look like bright day tinted blue and everything else is
   black, which reads as floodlighting, not night.
4. **The blue is in the light.** Moonlight is a warm-neutral 4,100 K. The blue of night is an effect of human vision
   and belongs in white balance and saturation.
5. **Practical lights are out of scale.** The 250 lumen beacon is about 20 candela, so it gives 6 lux at 1.8 m. Beyond
   that the "moon" outshines it. Under a 0.3 lux moon the same beacon dominates out to 8 m and would carry through fog.
6. Cosmetic: 512-pixel cascades, no anti-aliasing, fog at the Medium tier.

### 7.3 What a horror game should do instead

- Keep the **ratios** physical: key light, sky and practical lights in real units, with the sky tied to the key through
  a lux-calibrated HDRI or the physically based sky.
- Decide **how dark** with exposure only: fixed, or automatic within a clamp of about 2 EV so the eye adapts a little
  but can never lift night to day. Night reads as night at roughly 2 to 3 stops under neutral.
- Put the **colour of night** in grading: cooler white balance, lower saturation.
- Get **readability from fog and silhouette**, not from raised ambient: mist lit by the moon behind dark trunks.
- Let **practical lights** be bright by contrast. They then work as landmarks and as stealth risk.
- Derive the stealth "how visible am I" value from the same lux numbers, so what the player sees and what the dinosaurs
  detect stay consistent.

### 7.4 The presets

All three share: HDRI sky in Lux mode, `skyAmbientMode` Dynamic, APV with sky occlusion, tonemapping ACES, TAA,
dithering, shadow settings from section 4.3, fog tier High, volumetric fog on. Light elevations are
`transform.rotation = Quaternion.Euler(elevation, azimuth, 0)`.

These are starting values derived from the references above. They have not been rendered. Each row marked "tune" is
the knob to turn first.

| | **A. Overcast day** | **B. Dusk / blue hour** | **C. Moonlit night** |
|---|---|---|---|
| HDRI | `kloofendal_overcast_puresky` | `qwantani_dusk_2_puresky` | `qwantani_night_puresky` (moonless, so the moon is not counted twice) |
| Sky `desiredLuxValue` | 5,000 lux (dark overcast 1,000-2,000; bright overcast 10,000+) | 30 lux (blue hour spans about 5-100) | 0.04 lux (physical 0.002 moonless to about 0.05 under a full moon); tune up to 0.08 for playability |
| Key light | "Sun behind cloud": 1,500 lux, 6,000 K, angular diameter 3, elevation 55, shadows on | "Afterglow": 3 lux, 3,500 K, angular diameter 15, elevation 6, aimed from the bright horizon of the HDRI, shadows on | "Moon": 0.3 lux, 4,100 K, angular diameter 0.52, elevation 50, shadows on |
| Key `volumetricDimmer` | 1 | 1 | 2 (tune; non-physical shaft boost) |
| Key to sky ratio | 1 : 3.3 (overcast is sky-dominated) | 1 : 10 | 7.5 : 1 |
| Fog `meanFreePath` | 250 m | 120 m | 60 m |
| Fog `baseHeight` / `maximumHeight` | ground / ground + 40 m | ground / ground + 25 m | ground / ground + 12 m (low mist) |
| Fog `albedo` | (0.95, 0.95, 0.95) | (0.90, 0.92, 0.95) | (0.88, 0.92, 1.0) |
| Fog `anisotropy` | 0.6 | 0.6 | 0.75 |
| Fog `depthExtent` | 80 m | 80 m | 64 m |
| Fog `denoisingMode` | Both | Both | Both (Gaussian once a torch moves) |
| Neutral EV, open ground | log2(6,500 / 3.14) = 11.0 | log2(33 / 3.14) = 3.4 | log2(0.34 / 3.14) = -3.2 |
| Exposure | `AutomaticHistogram`, `limitMin` 6.5, `limitMax` 10, `compensation` -0.5 | `AutomaticHistogram`, `limitMin` 1.0, `limitMax` 3.5, `compensation` -1.0 | `Fixed`, `fixedExposure` -1.0 (2.2 stops under neutral); tune between -2 and 0 |
| Expected EV under canopy | 7-8 | 1-1.5 (clamped, so the interior stays gloomy) | fixed |
| White balance `temperature` | 0 | -8 | -22 |
| `saturation` / `contrast` | -5 / +5 | -10 / +8 | -30 / +10 |
| Bloom `intensity` | 0.12 | 0.18 | 0.15 |
| Film grain | Thin1, 0.12 | Thin2, 0.2 | Medium1, 0.3 |
| Vignette `intensity` | 0.2 | 0.25 | 0.35 |
| Practical lights | none needed | beacon 400 lumen, 2,700 K | beacon 250-400 lumen, 2,700 K; torch 100-300 lumen |
| Light shafts | faint; for visible shafts use the variant below | none (no direct sun) | yes, moon through mist |

Variant of A for light shafts, "hazy sun breaking through": sky 8,000 lux, sun 20,000 lux at 5,600 K, angular
diameter 1, elevation 35, fog `meanFreePath` 120 m, `anisotropy` 0.7, exposure limits 8-11.5.

Note on preset C: its fixed exposure is the same number the greybox uses, -1 EV. The difference is that the moon is
twenty times dimmer and the sky is tied to it, so the image lands about 2 stops under neutral instead of 2 stops over.

Physically based sky alternative for C, if a visible moon disc is wanted: `skyType` PhysicallyBased; a "Sun" light
below the horizon with shadows off and `interactsWithSky` true; a "Moon" light at 0.3 lux with shadows on,
`celestialBodyShadingSource = CelestialBodyShadingSource.ReflectSunLight`, `sunLightOverride` = the sun,
`earthshine` 1, `angularDiameter` 0.52. Stars need a cubemap in `spaceEmissionTexture`. Not rendered or tuned here.

---

## 8. Frame settings and the HDRP asset

### 8.1 State of `$PROJ/Assets/Game/Settings/HDRP.asset` (created with defaults)

| Field under `m_RenderPipelineSettings` | Current | Needed | Action |
|---|---|---|---|
| `lightProbeSystem` | 1 (APV) | APV | none |
| `probeVolumeSHBands` | 1 (L1) | L1 is enough to start | optional: L2 for better directionality |
| `supportProbeVolumeScenarios` | 0 | 0 with sky occlusion | only if lighting scenarios are adopted |
| `supportVolumetrics` | 1 | 1 | none |
| `supportSSAO` | 1 | 1 | none |
| `hdShadowInitParams.supportContactShadows` | 1 | 1 | none |
| `lightSettings.useContactShadow` | false, false, true | per-light override is simpler | none |
| `supportSSR` | **0** | 1 (wet leaves, rock, later water) | **set true** |
| `supportSSGI` | **0** | optional | set true only if SSGI is tried |
| `supportVolumetricClouds` | 0 | 0 with HDRI skies | only with physically based sky |
| `supportWater` | 0 | 0 for now | later; note it disables MSAA |
| `hdShadowInitParams.directionalShadowFilteringQuality` | 1 (Medium) | 2 (High, PCSS) | **set High** |
| `hdShadowInitParams.maxDirectionalShadowMapResolution` | 2048 | 2048 | optional 4096 later |
| `hdShadowInitParams.shadowResolutionDirectional` | 256/512/1024/2048 | fine | none (per-light override used) |
| `lightingQualitySettings.Fog_Budget` / `Fog_DepthRatio` | 0.166/0.33/0.666 and 0.666/0.666/0.5 | fine | optional tuning of the High tier |
| `supportedLitShaderMode` | 2 (DeferredOnly) | fine | none. MSAA would need Forward; TAA is used instead |
| `colorBufferFormat` | 74 (R11G11B10) | fine | none |
| `dynamicResolutionSettings.enabled` | 0, with `advancedUpscalerNames: [STP]` and `dynResType: 1` (Hardware) already present | 0 for now | to use STP later: `enabled = true`, optionally `forceResolution = true` with `forcedPercentage`, and `allowDynamicResolution = true` on the camera |
| `gpuResidentDrawerSettings.mode` | 0 (disabled) | another topic | `hdrp.gpuResidentDrawerMode` has a public setter if instancing is wanted |

### 8.2 Changing the asset: public API

```csharp
var hdrp = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>("Assets/Game/Settings/HDRP.asset");
RenderPipelineSettings s = hdrp.currentPlatformRenderPipelineSettings;        // struct copy
s.supportSSR = true;
s.hdShadowInitParams.directionalShadowFilteringQuality = HDShadowFilteringQuality.High;
// s.supportSSGI = true;
// s.probeVolumeSHBands = ProbeVolumeSHBands.SphericalHarmonicsL2;
// s.lightingQualitySettings.Fog_Budget[(int)ScalableSettingLevelParameter.Level.High] = 0.666f;  // class instance shared with the asset
hdrp.currentPlatformRenderPipelineSettings = s;                               // setter rebuilds the pipeline (OnValidate)
EditorUtility.SetDirty(hdrp);
AssetDatabase.SaveAssetIfDirty(hdrp);
```

Evidence: `public RenderPipelineSettings currentPlatformRenderPipelineSettings { get => ...; set { ...; OnValidate(); } }`
(`HDRenderPipelineAsset.cs:139`) and `{ get; set; }` in
`$PROJ/Library/ScriptAssemblies/Unity.RenderPipelines.HighDefinition.Runtime.dll`. `RenderPipelineSettings` is a public
struct with public fields (`RenderPipelineSettings.cs:31,255-494`); `HDShadowInitParameters`, `GlobalLightLoopSettings`
and `LightSettings` are public structs; `GlobalLightingQualitySettings` and `GlobalPostProcessingQualitySettings` are
classes with public arrays.

Fallback through `SerializedObject` (names match the YAML keys in the asset, lines 212-545):
`m_RenderPipelineSettings.supportSSR`, `.supportSSGI`, `.supportVolumetricClouds`, `.supportWater`, `.lightProbeSystem`,
`.probeVolumeSHBands`, `.probeVolumeMemoryBudget`, `.supportProbeVolumeScenarios`,
`.hdShadowInitParams.directionalShadowFilteringQuality`, `.hdShadowInitParams.maxDirectionalShadowMapResolution`,
`.hdShadowInitParams.shadowResolutionDirectional.m_Values` (array), `.hdShadowInitParams.supportContactShadows`,
`.lightSettings.useContactShadow.m_Values` (array), `.lightingQualitySettings.Fog_Budget` (array),
`.lightingQualitySettings.Fog_DepthRatio` (array), `.lightingQualitySettings.ContactShadowSampleCount` (array),
`.dynamicResolutionSettings.enabled`, `.dynamicResolutionSettings.forceResolution`,
`.dynamicResolutionSettings.forcedPercentage`.

Changing the asset changes shader variants, so the first editor run after the change will recompile shaders.

### 8.3 Frame settings

The effective value of each frame setting is the default AND the asset support flag (`FrameSettings.Sanitize`,
`FrameSettings.cs:658-790`). For example SSR needs `supportSSR`, SSGI needs `supportSSGI`, volumetrics need
`supportVolumetrics` and atmospheric scattering, volumetric clouds need `supportVolumetricClouds`.

Decoded project defaults (`HDRenderPipelineGlobalSettings.asset`, `m_Camera.bitDatas`):

- **On** for cameras: `ShadowMaps`, `ContactShadows`, `Shadowmask`, `ScreenSpaceShadows`, `SSR`, `TransparentSSR`,
  `SSAO`, `SSGI`, `SubsurfaceScattering`, `Transmission`, `AtmosphericScattering`, `Volumetrics`,
  `ReprojectionForVolumetrics`, `ExposureControl`, `ReflectionProbe`, `PlanarProbe`, `SkyReflection`,
  `AdaptiveProbeVolume`, `VolumetricClouds`, `Water`, `MotionVectors`, `ObjectMotionVectors`, `Decals`, `Postprocess`,
  and every individual post effect.
- **Off** for cameras: `NormalizeReflectionProbeWithProbeVolume`, `FullResolutionCloudsForSky`,
  `DepthPrepassWithDeferredRendering`, `ClearGBuffers`.
- Baked or custom reflection probes: `Volumetrics` and `AdaptiveProbeVolume` on; `AtmosphericScattering`, `SSAO`,
  `ContactShadows` off.
- Realtime reflection probes: `Volumetrics`, `AtmosphericScattering`, `SSAO`, `ContactShadows`, `AdaptiveProbeVolume`,
  `VolumetricClouds` on.

So nothing in the frame settings has to be switched on for sections 1-7. The one optional override, per camera:

```csharp
var cam = cameraObject.GetComponent<HDAdditionalCameraData>();
cam.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
cam.TAAQuality = HDAdditionalCameraData.TAAQualityLevel.High;
cam.dithering = true;
cam.stopNaNs = true;
cam.customRenderingSettings = true;
cam.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.NormalizeReflectionProbeWithProbeVolume] = true;
cam.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.NormalizeReflectionProbeWithProbeVolume, true);
```

(`HDAdditionalCameraData.cs:310-379,514-520`; pattern from `$HDRP/Documentation~/Frame-Settings-API.md`.)

---

## Recommendations for the build

**Scope for this first version**

1. HDRI sky in Lux mode for all three presets, from Poly Haven "pure sky" files at 4k. Leave the physically based sky
   and volumetric clouds for a later time-of-day phase.
2. One key directional light per preset, real-time, shadows on. Never two shadow-casting directional lights at once.
3. APV with **sky occlusion**, one bake, no baked lights. `ProbeVolume` in Local mode sized to the playable area plus a
   margin, 2 m minimum probe spacing to start.
4. One or two reflection probes in **Realtime / OnEnable** mode with a box influence over the play area. No planar probes.
5. Volumetric fog at the High tier. Local volumetric fog is optional polish.
6. No ray tracing, no path tracing, no SSGI at first (add SSGI only if bounce light looks missing by day), no water,
   no dynamic resolution.

**Changes to make**

7. HDRP asset through the public setter: `supportSSR = true`,
   `hdShadowInitParams.directionalShadowFilteringQuality = High`. Nothing else is required.
8. Camera: TAA High, dithering, stop NaNs, and the Normalize Reflection Probes override.
9. Lights: lux units, colour temperature rather than tint, shadow resolution override 2048, contact shadows override on.
10. Volume profile per preset as one asset each (`Atmosphere_Overcast`, `Atmosphere_Dusk`, `Atmosphere_Night`), swapped
    on a single global `Volume`. Use `quality.Override(tier)` on fog, AO and contact shadows; do not rely on the
    property setters of quality-tier fields.
11. `Gamex.EditorTools.asmdef`: add `Unity.RenderPipelines.Core.Editor` and
    `Unity.RenderPipelines.HighDefinition.Editor` only if `AdaptiveProbeVolumes` or `LightmappingHDRP` are called.
    `Lightmapping.Bake()` alone needs neither.

**Build order for the headless script**

12. Build the scene, mark occluders `ContributeGI` with `receiveGI = LightProbes`, add the `ProbeVolume`, **save the scene**.
13. Create the baking set (reflection call to `SetDefaults`, `skyOcclusion = true`, `TryAddScene`) and the
    `LightingSettings`.
14. Render a few frames with the player camera so the HDRP pipeline, the sky and the probe reference volume exist.
    Assert `RenderPipelineManager.currentPipeline is HDRenderPipeline` and `ProbeReferenceVolume.instance.isInitialized`.
15. `Lightmapping.Bake()`; check its return value; check that `ProbeReferenceVolume.instance.DataHasBeenLoaded()` and
    that the `.CellData.bytes` file exists and is not empty.
16. **Save the scene again**, then `AssetDatabase.SaveAssets()`.
17. Keep generated materials, profiles and the nav mesh out of `<scene folder>/<scene name>/`, because that folder
    receives the bake output and the greybox builder's pattern of deleting it would delete the bake.
18. Captures: warm up 32-48 frames; log `SystemInfo.graphicsDeviceType`, the imported cubemap face size, and the
    computed upper-hemisphere lux for each HDRI so a wrong import is visible in the log.

**Risks to check on the first real run** (all UNVERIFIED here)

- Whether `Lightmapping.Bake()` completes the APV delegate in batch mode on Apple silicon, and with which lightmapper.
- Whether the HDRI lux helper returns a sensible value in batch mode.
- How dark the canopy gets with sky occlusion treating leaf cards as solid. Expect to tune
  `skyOcclusionAverageAlbedo` and `ProbeVolumesOptions.skyOcclusionIntensityMultiplier`, or to exclude leaf renderers
  from `ContributeGI`.
- Frame time of fog at the High tier and of four 2048 cascades with dense alpha-tested foliage. Not measured.
- BC6H cubemap import on Apple silicon and the meaning of `maxTextureSize` for lat-long sources.
- All preset numbers in section 7.4 are derived, not rendered.

**Method notes**

- Tools written for this research are in `forest/research/tools/` (`mp.sh`, `decode_fs.py`, `striphtml.py`,
  `hdri_pick.py`, `hdri_files.py`).
- Network use: the Poly Haven HDRI catalogue, six `files` and `info` lookups, six 256-pixel thumbnails. While fetching
  the catalogue I wrote to `forest/catalog/ph_hdris.json`, which already existed from another team member; the download
  was byte-for-byte the same size (1,075,884 bytes), so its content should be unchanged. Later files went into
  `forest/catalog/lighting/` and `forest/thumbs/lighting/`.
