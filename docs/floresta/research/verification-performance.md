# Verification and performance: knowing the forest is good, and fast enough, without looking at the editor

Scope: research only. Unity was not launched and nothing in the project was changed. Every claim below is tied to a file I read; anything I could not confirm by reading is marked **UNVERIFIED**. Budget numbers in section 4 and time estimates in section 5 are engineering estimates, not measurements, and are labelled as such.

Path abbreviations used in citations:

- `HD` = `/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/Resources/PackageManager/BuiltInPackages/com.unity.render-pipelines.high-definition`
- `CORE` = same folder, `com.unity.render-pipelines.core`
- `DOC` = `/Applications/Unity/Hub/Editor/6000.6.0f1/Documentation/en`
- `PRJ` = `/Users/william/Documents/GitHub/gamex`

## Findings that change the plan

1. **The triangle budget is the binding constraint, not textures.** The Poly Haven trees that look like a forest are 0.3 M to 17.4 M polygons each (`pine_tree_01` 17,427,094; `fir_tree_01` 7,853,731; `island_tree_03` 4,760,490; `island_tree_02` 1,762,064 — `catalog/ph_models.json`). The FBX is the same size at every texture tier (pine: 648.5 MB at 1k, 2k, 4k and 8k — `catalog/vp_files/pine_tree_01.json`), so a "1k tier" does not make geometry cheaper. Blender is not installed, so reduction must happen inside Unity.
2. **The current capture has no anti-aliasing.** `HDAdditionalCameraData.antialiasing` defaults to `None` (`HD/Runtime/RenderPipeline/Camera/HDAdditionalCameraData.cs:310`) and the greybox camera is created with defaults (`PRJ/Assets/Game/Editor/GreyboxSceneBuilder.cs:311`). Foliage will shimmer and alias until TAA is set explicitly.
3. **Repeated `Camera.Render()` in edit mode does converge TAA, volumetric fog and SSAO**, but 16 frames is too few for SSAO, and time-driven effects are frozen or forced (shader time is 0, exposure adaptation is instant, motion blur is off).
4. **Opaque overdraw and vertex-density debug views do not exist on Metal** (nor on PS5): `HD/Runtime/Debug/DebugDisplay.cs:555-562`. Overdraw and density must be estimated from script.
5. **Per-pass GPU timing is not available on this Mac.** The GPU Usage profiler module is "Not supported" on macOS Metal (`DOC/Manual/ProfilerGPU.html`). Only whole-frame GPU time (`FrameTimingManager`) exists, and cost per feature has to be measured by switching features off.
6. **`ScreenCapture` and `WaitForEndOfFrame` are the wrong tools here.** `WaitForEndOfFrame` "does not run when you run the Editor in batch mode" (`DOC/ScriptReference/WaitForEndOfFrame.html`), and the `com.unity.modules.screencapture` module is not in `PRJ/Packages/manifest.json`, so `ScreenCapture` does not compile in the runtime or PlayMode test assemblies (`UnityEngine.ScreenCaptureModule` is absent from `PRJ/Gamex.Tests.PlayMode.csproj` and `PRJ/Gamex.Runtime.csproj`, present only in `PRJ/Gamex.EditorTools.csproj`).
7. **Unity has a built-in proxy mode for textures**: `EditorUserBuildSettings.overrideMaxTextureSize` and `overrideTextureCompression` (`DOC/ScriptReference/EditorUserBuildSettings-overrideMaxTextureSize.html`).
8. **Batch mode cannot run while the owner has the project open** ("only a single instance of Unity can run at a time" — `DOC/Manual/EditorCommandLineArguments.html`, `-batchmode`). The verification loop needs either an agreed "editor closed" window or an in-editor command runner.

---

## 1. Does repeated `Camera.Render()` converge temporal effects, and what is a more faithful headless capture?

### What `SceneCapture.cs` does today

`PRJ/Assets/Game/Editor/SceneCapture.cs:45-62` creates an `ARGB32` sRGB `RenderTexture`, assigns it to the camera, calls `camera.Render()` 16 times (`WarmupFrames`, line 19) and reads the pixels back. It runs in edit mode from `-executeMethod`.

### Why history survives between the calls

| Mechanism | Evidence |
|---|---|
| In the Editor, HDRP's frame counter advances once per `Render()` call that contains a non-preview camera, not once per engine frame | `HD/Runtime/RenderPipeline/HDRenderPipeline.cs:2319-2337` (`#if UNITY_EDITOR … newCount++`) |
| The per-camera state (`HDCamera`) is cached by `(camera, xrMultipassId, historyChannel)` and reused | `HD/Runtime/RenderPipeline/Camera/HDCamera.cs:179-207` |
| Game cameras are never disposed by the cleanup pass, even when disabled, "They can still be used for rendering from script via Camera.Render" | `HDCamera.cs:1558-1571` |
| `cameraFrameCount` and `taaFrameIndex` advance on every `HDCamera.Update` | `HDCamera.cs:1456-1463` |

### Effect by effect

| Effect | Converges with repeated edit-mode `Camera.Render()`? | Evidence and caveat |
|---|---|---|
| TAA | Yes, if TAA is selected on the camera. | The "TAA only with animated materials" restriction applies to `CameraType.SceneView` only (`HDCamera.cs:2015-2023`). Jitter index increments per render (`HDCamera.cs:1456-1459`). Default history weight `taaBaseBlendFactor = 0.875` (`HDAdditionalCameraData.cs:350`): residual of the first frame is 0.875^16 = 12 %, 0.875^32 = 1.4 %. |
| Volumetric fog reprojection | Yes. | Reprojection is enabled for `CameraType.Game` regardless of play mode (`HDCamera.cs:1168-1177`). Frame index is `cameraFrameCount % 14` (`HD/Runtime/Lighting/VolumetricLighting/HDRenderPipeline.VolumetricLighting.cs:360-364`); history becomes valid after two frames (`…VolumetricLighting.cs:1503-1506`); history weight is 6/7 (`VolumetricLighting.compute:148-152`): residual 8.5 % after 16 frames, 0.7 % after 32. |
| SSAO temporal accumulation | Yes, but slowly. | Uses `cameraFrameCount` (`HD/Runtime/Lighting/ScreenSpaceLighting/HDRenderPipeline.AmbientOcclusion.cs:57-78`). Feedback is 0.85 to 0.95 (`GTAOTemporalDenoise.compute:94`): at 0.95 the residual is 44 % after 16 frames, 19 % after 32, 3.8 % after 64. **16 warm-up frames is not enough.** |
| SSGI | Would converge the same way (history validity is "rendered on the previous `cameraFrameCount`", `HDCamera.cs:918-930`). | Not relevant now: `supportSSGI: 0` and `supportSSR: 0` in `PRJ/Assets/Game/Settings/HDRP.asset`. |
| Sky cubemap, GGX convolution, GPU ambient probe | Computed inside the same render in which the sky hash changes. | `HD/Runtime/Sky/SkyManager.cs:1186-1230`. The GPU buffer `_AmbientProbeData` is what the light loop falls back to when APV is the probe system and no APV data is loaded (`HD/Runtime/Lighting/LightLoop/LightLoop.hlsl:455-459, 496-518`). The project uses APV (`lightProbeSystem: 1`; enum at `HD/Runtime/RenderPipeline/Settings/RenderPipelineSettings.cs:49-55`). |
| CPU ambient probe (`RenderSettings.ambientProbe`) | **Not guaranteed.** | It is filled by an `AsyncGPUReadback` callback (`SkyManager.cs:887-888`, `SkyRenderingContext.cs:82-96`, applied at `SkyManager.cs:722`). HDRP's own comment: "For one-off rendering the dynamic ambient probe will be set to black until they are not processed" (`HDRenderPipeline.cs:2102-2108`). Whether the callback fires between back-to-back `Render()` calls inside one `-executeMethod` call is **UNVERIFIED**. `AsyncGPUReadback.WaitAllRequests()` forces completion (`DOC/ScriptReference/Rendering.AsyncGPUReadback.WaitAllRequests.html`). It matters for realtime reflection probes and for any renderer lit through `unity_SH*` (legacy probe path, `HD/Runtime/Material/BuiltinGIUtilities.hlsl:10-21`). |
| Exposure | Converges, but not like the game. | In edit mode a Game camera has `animateMaterials == false` (`CORE/Runtime/Utilities/CoreUtils.cs:1502-1507`), and then adaptation is forced to `Fixed`: `if (!hdCamera.animateMaterials || hdCamera.resetPostProcessingHistory) adaptationMode = AdaptationMode.Fixed;` (`HD/Runtime/RenderPipeline/HDRenderPipeline.PostProcess.cs:1502-1505`). Automatic exposure therefore snaps to its target within a couple of frames instead of adapting over time. |
| Shader time (wind, scrolling clouds, any Shader Graph `Time`) | Frozen at 0. | `HDCamera.cs:1199-1217` sets `time = 0` when `animateMaterials` is false; `_Time` is built from it (`HDCamera.cs:1663-1683`). Cloud layer scrolling is zeroed (`HD/Runtime/Sky/CloudSystem/CloudLayer/CloudLayerRenderer.cs:109-112`). |
| Motion blur | Off in edit mode. | `MotionBlurPass` requires `m_AnimatedMaterialsEnabled` (`HDRenderPipeline.PostProcess.cs:4552`). |
| Camera moved between viewpoints | History from the previous viewpoint leaks in unless reset. | `HDCamera.Reset()` is public and documented for camera cuts (`HDCamera.cs:288-310`). |

Conclusion: the existing approach is sound for a static environment still, with three fixes — select TAA, render about 64 frames instead of 16, and reset history when the camera is moved. It does not show what the player sees for anything driven by time.

### Shader compilation during capture

Script-driven renders compile shaders synchronously: "By default, asynchronous Shader compilation is only enabled for requests that originate from the Game View or Scene View" (`DOC/ScriptReference/ShaderUtil-allowAsyncCompilation.html`). So a headless capture blocks on new variants rather than showing the cyan placeholder. The project has async compilation on for the Game view (`m_AsyncShaderCompilation: 1` in `PRJ/ProjectSettings/EditorSettings.asset`), so the owner can briefly see cyan where a capture would not. Setting `ShaderUtil.allowAsyncCompilation = false` around capture code, and asserting `!ShaderUtil.anythingCompiling` before reading pixels, makes this explicit.

### More faithful options, with exact APIs

**A. Edit-mode `Camera.Render()` loop (keep, with fixes).** Cheapest; no play mode. Sketch (not compiled):

```csharp
// Editor assembly. using UnityEditor; UnityEngine.Rendering; UnityEngine.Rendering.HighDefinition;
var hd = camera.GetComponent<HDAdditionalCameraData>();
hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing; // HDAdditionalCameraData.cs:239-250, 310
hd.dithering = true;                                                            // default false, HDAdditionalCameraData.cs:314
bool oldAsync = ShaderUtil.allowAsyncCompilation;
ShaderUtil.allowAsyncCompilation = false;
camera.targetTexture = target;
camera.Render();                              // creates the pipeline instance and the HDCamera
HDCamera.GetOrCreate(camera).Reset();         // camera cut, HDCamera.cs:179, 293
for (int i = 0; i < 64; i++)
{
    camera.Render();
    if (i == 1) AsyncGPUReadback.WaitAllRequests();   // sky ambient probe readback
}
// ReadPixels as today, then restore targetTexture and allowAsyncCompilation in a finally block.
```

**B. PlayMode test with an explicitly rendered camera (recommended as the reference "what the player sees").** In play mode `animateMaterials` is true (`CoreUtils.cs:1506`), so time, exposure adaptation and motion blur behave as in the Game view. Do not rely on the Game view or on `WaitForEndOfFrame` in batch mode. Use a fixed time step so the result is repeatable: `Time.captureDeltaTime = 1f / 60f` makes `Time.time` advance by a fixed step "regardless of real time" (`DOC/ScriptReference/Time-captureDeltaTime.html`). Sketch:

```csharp
// PlayMode test assembly; needs references to Unity.RenderPipelines.Core.Runtime and
// Unity.RenderPipelines.HighDefinition.Runtime (names as in PRJ/Assets/Game/Editor/Gamex.EditorTools.asmdef).
yield return EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single)); // #if UNITY_EDITOR
Time.captureDeltaTime = 1f / 60f;
camera.enabled = false;                 // one render per frame, driven by the test
camera.targetTexture = target;
for (int i = 0; i < 180; i++) { camera.Render(); yield return null; }   // 3 s of game time
```

`EditorSceneManager.LoadSceneAsyncInPlayMode` loads a scene by path without it being in Build Settings (`DOC/ScriptReference/SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode.html`). That matters because `GreyboxSceneBuilder.cs:105` overwrites the whole Build Settings scene list.

Whether enabled cameras are rendered automatically in batch-mode play mode (there is no Game view) is **UNVERIFIED**; rendering explicitly avoids depending on it.

**C. Render requests.** `RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = rt })`, guarded by `RenderPipeline.SupportsRenderRequest` (`DOC/ScriptReference/Rendering.RenderPipeline.SubmitRenderRequest.html`). HDRP supports only `StandardRequest` (`HDRenderPipeline.cs:2651-2656`), disables AOV requests for it, and renders with a separate persistent history channel (`HDRenderPipeline.cs:2670-2740`, `HDCamera.HistoryChannel.CustomUserHistory0`). Use it when the same camera is also being rendered by the normal loop and you do not want the capture to disturb that history. For a dedicated, disabled capture camera it is equivalent to `Camera.Render()`.

**D. HDRP multi-frame accumulation (optional "reference" stills only).** Public API on `HDRenderPipeline`: `BeginRecording(int samples, float shutterInterval, float shutterFullyOpen = 0.0f, float shutterBeginsClosing = 1.0f)`, `PrepareNewSubFrame()`, `EndRecording()`, `IsFrameCompleted(HDCamera)` (`HD/Runtime/RenderPipeline/Accumulation/SubFrameManager.cs:379-421`; documented in `HD/Documentation~/rendering-multiframe-recording-api.md`). Facts to respect:

- Get the pipeline with `RenderPipelineManager.currentPipeline as HDRenderPipeline`; it is null until one frame has rendered (`DOC/ScriptReference/Rendering.RenderPipelineManager-currentPipeline.html`). `HDRenderPipeline.currentPipeline` is `internal` (`HDRenderPipeline.cs:52`).
- It rewrites `Time.captureDeltaTime`, `Time.fixedDeltaTime` and `Time.timeScale` (`SubFrameManager.cs:213-238, 272-296`).
- Accumulation happens on the colour buffer before post-processing (`HD/Runtime/RenderPipeline/HDRenderPipeline.RenderGraph.cs:322-326`), or after depth of field when DoF is active (`HDRenderPipeline.PostProcess.cs:751-753`).
- It averages sub-frames; it only anti-aliases if each sub-frame is jittered. Unity's own example jitters `camera.projectionMatrix` in `RenderPipelineManager.beginContextRendering` (`HD/Documentation~/rendering-high-quality-antialiasing-with-accumulation.md`).
- It produces an image better than the game can show in real time. Label such stills "reference" and never use them to judge shipping quality.
- Path tracing on Apple GPUs is **UNVERIFIED** (`HD/Documentation~/raytracing-requirements.md` lists only NVIDIA and AMD) and `supportRayTracing: 0` in the project asset, so treat accumulation as rasterised supersampling only.

**E. Recorder package: not recommended.** `com.unity.recorder` is neither in the manifest nor among the built-in packages (`…/PackageManager/BuiltInPackages/` has no recorder folder), so it needs a registry download and a manifest change. Its behaviour in batch mode is **UNVERIFIED**. Options A and B cover the need.

---

## 2. Capture plan for judging realism from stills

### Output conventions

- Render at 2560×1440 and save the full frame as PNG. Also save a 1:1 centre crop (for example 768×768) of the same render: reviewers who see a downscaled full frame cannot judge texture sharpness, aliasing or noise without a 100 % crop.
- Write a JSON sidecar per image computed in C# from the pixel array (Pillow is not installed, so do not plan on Python image analysis): mean and median luminance, share of pixels below 2/255 and above 253/255, per-channel means, share of near-magenta and near-cyan pixels (error and placeholder shaders, see section 6), and RMSE against the previous run's image for the same viewpoint.
- Record camera pose, FOV, frame count, quality level, texture tier, seed and git commit in the sidecar so two runs can be compared.
- `sips` is available at `/usr/bin/sips` for resizing if needed; ImageMagick and ffmpeg are not installed.

### Viewpoints

Eye height and FOV follow the existing player rig: camera pivot at 1.65 m, FOV 70, near plane 0.05 (`GreyboxSceneBuilder.cs:304-311`); crouch eye height 0.95 m (`PRJ/Assets/Game/Scripts/Player/PlayerController.cs:18`).

| Id | Camera | What the reviewer judges |
|---|---|---|
| `trail_start` | Spawn, eye level, along the trail | First impression, composition, exposure |
| `trail_mid_fwd`, `trail_mid_back` | Mid-trail, both directions | Front-lit versus back-lit look of the same assets |
| `trail_strip_01..08` | Eight evenly spaced points along the trail | Holes, repetition, LOD pops between adjacent frames |
| `ground_close` | 0.4 m high, pitched 35° down | Ground texture resolution, tiling, blend with ground cover, contact shadows |
| `trunk_close` | 0.6 m from a hero trunk | Bark texel density, normal detail, moss or ground intersection |
| `canopy_up` | Pitched 75° up | Leaf alpha edges, translucency, sky exposure, canopy density |
| `backlit` | Looking at the sun azimuth, low pitch | Transmission through leaves, volumetric shafts, bloom, exposure |
| `long_sightline` | Longest clear line in the playable area | Fog depth, LOD silhouettes, shadow distance fade, far tiling |
| `crouch_hide` | 0.95 m inside or behind cover | The stealth view; near-plane clipping |
| `boundary_n/e/s/w` | At the edge of the playable area, looking out | That the world edge is masked |
| `pano_0/90/180/270` | Mid-trail, four yaws | Gaps anywhere around the player |
| `overview_ortho` | Top-down orthographic (as today, `SceneCapture.cs:32-39`) | Layout, coverage |
| `overview_oblique` | 40 m up, pitched 45° | Density and variation |

Add one fixed-exposure variant of `trail_start` and `long_sightline` if the scene is a night scene, because automatic exposure can hide a scene that is actually too dark or too bright.

### Debug views obtainable from script

`HDRenderPipeline.debugDisplaySettings` is public (`HD/Runtime/RenderPipeline/HDRenderPipeline.Debug.cs:52`) and is applied to every camera that is not a reflection or preview camera, including a Game camera rendered by script (`HDRenderPipeline.cs:2866-2877`).

| View | Call | Source |
|---|---|---|
| Albedo, Normal, Smoothness, AmbientOcclusion, Metal, Specular, Alpha | `debug.SetDebugViewCommonMaterialProperty(MaterialSharedProperty.Albedo)` — enum is in namespace `UnityEngine.Rendering.HighDefinition.Attributes` | `HD/Runtime/Debug/DebugDisplay.cs:853`; `HD/Runtime/Debug/MaterialDebug.cs:102-120` |
| Lighting only (diffuse, specular, direct, indirect, reflection, emissive) | `debug.SetDebugLightingMode(DebugLightingMode.DiffuseLighting)` and siblings; also `LuxMeter`, `LuminanceMeter`, `VisualizeCascade` | `DebugDisplay.cs:1015`; `HD/Runtime/Debug/LightingDebug.cs:10-47` |
| "Clay" render (full lighting, neutral albedo) | `debug.data.lightingDebugSettings.overrideAlbedo = true; overrideAlbedoValue = new Color(0.5f, 0.5f, 0.5f);` also `overrideSmoothness`, `overrideNormal` | `LightingDebug.cs:255-275` |
| PBR albedo validation | `debug.SetFullScreenDebugMode(FullScreenDebugMode.ValidateDiffuseColor)` | `DebugDisplay.cs:919`; enum `DebugDisplay.cs:67-175` |
| SSAO, contact shadows, volumetric fog only, depth, motion vectors, NaN tracker | `SetFullScreenDebugMode(FullScreenDebugMode.ScreenSpaceAmbientOcclusion / ContactShadows / VolumetricFog / DepthPyramid / MotionVectors / NanTracker)` | same enum |
| Transparent overdraw | `FullScreenDebugMode.TransparencyOverdraw` | same enum; transparent surfaces only |
| Mip debug | `debug.SetMipMapMode(DebugMipMapMode.MipRatio)` (also `MipCount`, `MipStreamingStatus`…) | `DebugDisplay.cs:1048`; `HD/Runtime/Debug/MipMapDebug.cs:10-25` |
| Texcoords, instancing | `debug.SetDebugViewVarying(DebugViewVarying.Instancing)` | `DebugDisplay.cs:886`; `MaterialDebug.cs:14-36` |

Rules for using them:

- Material and mip debug views remove post-processing (`DebugDisplay.cs:2275-2278`, applied at `HDRenderPipeline.cs:3109-3118`), so they are single-frame and un-anti-aliased; no warm-up is needed.
- The settings object is a serialized editor singleton (`CORE/Runtime/Debugging/DebugDisplaySerializer.cs:16-30`). Reset every mode in a `finally` block, or a failed capture leaves the owner's Game view in a debug mode.
- The first use of each debug view compiles `DEBUG_DISPLAY` shader variants for every material; expect a one-time stall.
- **Not available on Metal:** `QuadOverdraw` and `VertexDensity`. The code removes both on Metal, PlayStation and Switch 2 (`DebugDisplay.cs:555-562`) and the documentation says "currently not supported on Metal" (`HD/Documentation~/rendering-debugger-window-reference.md:279-280`).
- **No LOD colouring view exists** in HDRP's debugger (no LOD-related entry in `HD/Runtime/Debug` or `CORE/Runtime/Debugging`).

The AOV API is an alternative that leaves the global debug state alone: `new AOVRequest(AOVRequest.NewDefault()).SetFullscreenOutput(MaterialSharedProperty.Albedo)`, added with `new AOVRequestBuilder().Add(request, bufferAllocator, includedLightList, new[] { AOVBuffers.Color }, callback).Build()` and installed with `HDAdditionalCameraData.SetAOVRequests(...)` (`HD/Runtime/RenderPipeline/RenderPass/AOV/AOVRequest.cs:47-135`, `AOVRequestBuilder.cs:19-31`, `AOVBuffers.cs`, `HDAdditionalCameraData.cs:639`; worked example in `HD/Documentation~/AOVs.md`). `supportRuntimeAOVAPI` only strips variants from player builds (`HD/Editor/BuildProcessors/HDRPPreprocessShaders.cs:119`), so AOVs work in the Editor with the current asset. Prefer the simpler `debugDisplaySettings` route unless you need several outputs from one render.

### Replacing the views that do not exist

- **LOD:** render the same viewpoint with every `LODGroup` forced to each level (`LODGroup.ForceLOD`, `DOC/ScriptReference/LODGroup.ForceLOD.html`) or with `Renderer.forceMeshLod` for Mesh LOD (`DOC/ScriptReference/Renderer-forceMeshLod.html`), and report a histogram of which level each visible renderer would select. The selection formula for the histogram would have to be re-implemented and is **UNVERIFIED**.
- **Triangle density:** per visible renderer, triangles of the selected LOD divided by projected screen area, from `Mesh.GetIndexCount` and `Renderer.bounds`. Report the worst 20 as a table.
- **Overdraw of alpha-tested foliage:** not obtainable from a built-in view. A custom pass that draws renderers with an additive material would do it; this is a design suggestion, **UNVERIFIED**.

---

## 3. Measuring performance headlessly

### What exists and what the documentation promises

| Need | API | Status on this machine |
|---|---|---|
| Draw calls, SetPass, triangles, vertices, shadow casters, BRG and SRP Batcher counts | `ProfilerRecorder` on counters named exactly `Draw Calls Count`, `SetPass Calls Count`, `Triangles Count`, `Vertices Count`, `Shadow Casters Count`, `Hybrid Renderer (BRG) Draw Calls Count`, `Hybrid Renderer (BRG) Instances Count`, `SRP Batcher Draw Calls Count`, `Render Textures Bytes`, `Render Textures Count`, `Used Buffers Bytes`, `Used Textures Bytes`, `Used Textures Count` | Names from `DOC/Manual/profiler-counters-reference.html`. Usable "in Editor and Player builds" (`DOC/ScriptReference/Unity.Profiling.ProfilerRecorder.html`). Expected to work in batch mode; **UNVERIFIED** by a run. |
| Memory | Counters `Texture Memory`, `Texture Count`, `Mesh Memory`, `Mesh Count`, `Material Count`, `Gfx Used Memory`, `System Used Memory`, `Total Used Memory`; `Profiler.GetAllocatedMemoryForGraphicsDriver()`; `Profiler.GetRuntimeMemorySizeLong(Object)`; `Texture.currentTextureMemory`, `Texture.totalTextureMemory` | Same reference page; `DOC/ScriptReference/Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver.html`; `Texture-totalTextureMemory.html`. The Editor's own textures are included, so always report the difference against an empty scene. |
| Frame time | `FrameTimingManager.CaptureFrameTimings()` then `GetLatestTimings(uint, FrameTiming[])`; fields `cpuFrameTime`, `cpuMainThreadFrameTime`, `cpuRenderThreadFrameTime`, `cpuMainThreadPresentWaitTime`, `gpuFrameTime` in milliseconds | Supported on macOS Metal, "always active in development builds and the Unity Editor", results delayed by four frames (`DOC/Manual/frame-timing-manager.html`, `frame-timing-manager-enable.html`). SRP Core uses exactly this pattern and treats the fields as milliseconds (`CORE/Runtime/Debugging/DebugFrameTiming.cs:51-61`). |
| Frame time through counters | `CPU Main Thread Frame Time`, `CPU Render Thread Frame Time`, `CPU Total Frame Time`, `GPU Frame Time` (nanoseconds) | `DOC/Manual/frame-timing-manager-counter-reference.html`. |
| Per-pass GPU time | none | GPU Usage profiler module is "Not supported" on macOS Metal (`DOC/Manual/ProfilerGPU.html`). Xcode is not installed, so the Metal debugger is not an option either. |

Resolve counters by name instead of guessing categories: the documentation's own example reads `CPU Main Thread Frame Time` with `ProfilerCategory.Internal` (`DOC/Manual/frame-timing-manager-record-timing-data.html`) although the reference page lists it with the rendering counters. `ProfilerRecorderHandle.GetAvailable(List<ProfilerRecorderHandle>)` plus `ProfilerRecorderHandle.GetDescription(handle)` gives `Name` and `Category` for every counter (`DOC/ScriptReference/Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetAvailable.html`); construct `new ProfilerRecorder(desc.Category, desc.Name, capacity, ProfilerRecorderOptions.Default)` and fail the test if `recorder.Valid` is false.

`UnityEditor.UnityStats` exists in `UnityEditor.CoreModule.dll` (getters `drawCalls`, `setPassCalls`, `triangles`, `vertices`, `shadowCasters`, `usedTextureMemorySize`…) but has no documentation page and its accessibility is **UNVERIFIED**. Do not use it.

### What is reliable in batch mode on macOS

- **Counts are the trustworthy part.** They do not depend on a window or on presentation. Make sure only the measured camera renders in the frame (disable every other camera), because the counters are per frame, not per camera.
- **GPU frame time is the doubtful part.** Whether `gpuFrameTime` is non-zero when no window presents a frame is **UNVERIFIED**. The documentation also warns that on tile-based deferred GPUs such as Apple's "the reported GPU time might be larger than the reported frame time" (`DOC/Manual/frame-timing-manager.html`).
- **Fallback that does not depend on presentation:** time a block of N explicit renders with `System.Diagnostics.Stopwatch`, forcing the GPU to finish with a one-pixel `Texture2D.ReadPixels` or an `AsyncGPUReadback` request followed by `WaitForCompletion()`. Take (a) the average over N frames with a single sync at the end — throughput, close to real frame time — and (b) per-frame time with a sync every frame — an upper bound, because it serialises CPU and GPU. Report both.
- **Cost per feature:** since per-pass timing is missing, measure the frame with one feature off and subtract. Frame settings can be overridden per camera: `hd.customRenderingSettings = true; hd.renderingPathCustomFrameSettingsOverrideMask.mask[(int)FrameSettingsField.Volumetrics] = true; hd.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.Volumetrics, false);` (`HD/Documentation~/Frame-Settings-API.md`; override is applied only when `customRenderingSettings` is true, `HD/Runtime/RenderPipeline/Settings/FrameSettings.cs:815-821`). Useful fields: `ShadowMaps`, `Volumetrics`, `SSAO`, `ContactShadows`, `Postprocess`, `SubsurfaceScattering`, `Decals`, `OpaqueObjects` (`FrameSettings.cs`, enum `FrameSettingsField`).
- **Editor overheads that inflate CPU numbers:** `metalAPIValidation: 1` in `PRJ/ProjectSettings/ProjectSettings.asset` enables a per-draw-call check in the Editor on macOS (`DOC/Manual/PlayerSettings-macOS.html`, "Metal API Validation"). Report whether it was on.
- **Pacing:** the active quality level is Ultra with `vSyncCount: 1` (`PRJ/ProjectSettings/QualitySettings.asset`, `m_CurrentQuality: 5`). In a test set `QualitySettings.vSyncCount = 0` and `Application.targetFrameRate = -1`, and restore them afterwards.
- **Background throttling by macOS (App Nap) for a windowless Editor: UNVERIFIED.** Run the batch command under `caffeinate -i` and compare once.
- **Play mode enters without a domain reload** (`m_EnterPlayModeOptions: 3` in `EditorSettings.asset`), so static state leaks between tests; reset anything static the measurement code keeps.

### Test shape and report

- One `[UnityTest]` per viewpoint class: load the scene, render 120 warm-up frames, then sample 300 frames. Use real time here, not `Time.captureDeltaTime`.
- Write `Temp/Perf/forest-perf.json` with, per viewpoint: resolution, frames, median / p95 / max for each timing, mean and max for each counter, memory figures minus the empty-scene baseline, plus machine, Unity version, graphics device (`SystemInfo.graphicsDeviceName`), quality level, texture tier, GPU Resident Drawer on or off, Metal API Validation on or off, commit.
- Assert budgets on counts and memory (deterministic). Assert on time with a generous margin and only after the measurement method has been validated once; otherwise log time as information.
- Keep a baseline file and report differences; a 10 % regression in triangles or draw calls between two builds is more useful to a reviewer than an absolute number.
- The Performance Testing package (`com.unity.test-framework.performance` 6.6.0) is already resolved as a transitive dependency (`PRJ/Packages/packages-lock.json`) and offers `Measure.Frames()`, `Measure.Custom(SampleGroup, value)` and a `-perfTestResults` JSON output (`…/BuiltInPackages/com.unity.test-framework.performance/Documentation~/measure-frames.md`, `cmd-line-args.md`). It is optional; plain `ProfilerRecorder` plus a hand-written JSON has fewer moving parts.

Command line: `Unity -batchmode -projectPath . -runTests -testPlatform PlayMode -testCategory Forest -testResults <xml>`; do not pass `-quit` with `-runTests` (`DOC/Manual/test-framework/reference-command-line.html`).

---

## 4. Budgets

**Status of these numbers:** engineering estimates to start from, not measurements and not taken from Unity documentation. The gate that matters is measured frame time; the counts are leading indicators that are deterministic and cheap to assert.

### Facts the budgets rest on

- Deferred lit only (`supportedLitShaderMode: 2` = `DeferredOnly`, `RenderPipelineSettings.cs:36-44`); decals are enabled (`supportDecals: 1`), and "If you enable Decals then HDRP forces a depth prepass" (`HD/Documentation~/frame-settings-reference.md:25-27`). Every visible opaque triangle is therefore drawn at least twice in the main view, plus once per shadow cascade that contains it. `Triangles Count` is a whole-frame total over all of these.
- Desktop compressed formats are 4 bits per pixel (DXT1) or 8 bits per pixel (BC7, DXT5, BC5) (`DOC/Manual/texture-choose-format-by-platform.html`). A full mip chain adds one third.
- The owner's displays are 1920×1080 at 75 Hz (`system_profiler SPDisplaysDataType`), so 1440p exists only as an off-screen target; the Game view will be at most 1080p.
- GPU Resident Drawer is off (`gpuResidentDrawerSettings.mode: 0` in `HDRP.asset`) and cannot turn on until "BatchRendererGroup Variants" is "Keep All" (`CORE/Runtime/GPUDriven/GPUResidentDrawer.Validator.cs:60-65`; project value `m_BrgStripping: 0`, which by the documented enum order `KeepIfEntitiesGraphics, StripAll, KeepAll` is not "Keep All" — the numeric mapping is inferred). The script-side switches are `EditorGraphicsSettings.batchRendererGroupShaderStrippingMode` (`DOC/ScriptReference/Rendering.EditorGraphicsSettings-batchRendererGroupShaderStrippingMode.html`) and `HDRenderPipelineAsset.gpuResidentDrawerMode` (`HD/Runtime/RenderPipeline/HDRenderPipelineAsset.cs:265-276`); HDRP allows the drawer in edit mode too (`allowInEditMode = true`, `HDRenderPipelineAsset.cs:252`). SRP Batcher is on (`enableSRPBatcher: 1`).
- LOD cross-fade is unsupported by the current asset (`supportDitheringCrossFade: 0`), so LOD changes will pop.

### Texture memory arithmetic

| Size | BC7 / BC5 / DXT5 with mips | DXT1 with mips | Uncompressed RGBA32 with mips |
|---|---|---|---|
| 1024² | 1.3 MiB | 0.7 MiB | 5.3 MiB |
| 2048² | 5.3 MiB | 2.7 MiB | 21.3 MiB |
| 4096² | 21.3 MiB | 10.7 MiB | 85.3 MiB |
| 8192² | 85.3 MiB | 42.7 MiB | 341 MiB |

One material set (base colour + normal + mask, all 8 bpp) costs 4 MiB at 1k, 16 MiB at 2k, 64 MiB at 4k, 256 MiB at 8k. `pine_tree_01` alone ships 16 maps: at 4k that is about 340 MiB for one tree species.

### Targets for 60 fps at 2560×1440 on the M4 Max, in the Editor, play mode

| Metric | Budget | Warn at | Why |
|---|---|---|---|
| GPU-bound frame time, median | 11.0 ms | 9.5 ms | Leaves about a third of 16.7 ms for dinosaurs, effects, UI and Editor noise |
| Frame time p95 | 13.5 ms | 12 ms | Stutter matters more than the average in a stealth game |
| CPU main thread | 6 ms | 5 ms | Scripts and AI for several dinosaurs are still to come |
| `Triangles Count` (all passes) | 5.0 M | 4.0 M | Roughly 1.2 M unique visible triangles ×2 for prepass and G-buffer, plus shadows |
| `Vertices Count` | 6.0 M | 5.0 M | Scanned meshes have many UV and normal splits |
| `Draw Calls Count` without GPU Resident Drawer | 4,000 | 3,000 | Render-thread cost; Metal API Validation makes each call dearer in the Editor |
| `Draw Calls Count` with GPU Resident Drawer | 1,000 | 700 | Instancing should collapse repeated meshes |
| `SetPass Calls Count` | 350 | 250 | Proxy for material and keyword variety |
| `Shadow Casters Count` | 2,500 | 2,000 | Four cascades re-submit geometry |
| `Texture Memory` minus empty-scene baseline | 2.0 GiB | 1.5 GiB | See PS5 below |
| `Mesh Memory` minus baseline | 600 MiB | 450 MiB | One un-reduced scanned tree can exceed this alone |
| Unique materials in the scene | 60 | 45 | |
| Unique meshes in the scene | 120 | 90 | |
| Largest texture dimension | 4096 | — | 8k is never justified at 1440p |
| Textures at 4096 | 12 | 8 | Hero ground and hero trunks only; everything else 2048 or less |
| Triangles in the most detailed LOD actually rendered: tree / rock / ground-cover clump | 150 k / 30 k / 3 k | — | Derived from the frame total and expected on-screen counts |

### What must hold for a later PS5 port

Assumption, **UNVERIFIED** and deliberately rough: the PS5 GPU delivers about 0.5 to 0.65 of the M4 Max 40-core GPU, a PS5 CPU core about 0.4 to 0.5 of an M4 performance core, and the console has 16 GB shared between CPU and GPU. Under that assumption a frame that takes 11 ms of GPU time here takes 17 to 22 ms on PS5 at the same resolution: 60 fps would need a lower internal resolution with temporal upscaling, or the game ships at 30 fps. Consequences:

- Keep motion vectors and TAA-class anti-aliasing on (`supportMotionVectors: 1` already), so a temporal upscaler can be added without redesign.
- Keep the forest's resident textures under about 2 GiB so there is room for dinosaurs; plan for mip streaming.
- Keep CPU-side draw submission low enough that halving CPU speed still fits: this is what the GPU Resident Drawer budget row is for.

Choices now that would be costly to undo:

| Choice | Why it is expensive later | Cheap now |
|---|---|---|
| Meshes without LODs, or LOD0 kept at scan density | Every asset must be re-imported and every placement re-validated; mesh memory and triangle budgets fail on both platforms | Decide the LOD path per asset at import (source `_LODx` meshes, or `ModelImporter.generateMeshLods`), and gate on it in tests |
| Instances that break GPU instancing | The GPU Resident Drawer needs MeshRenderers, DOTS-instancing shaders, no `MaterialPropertyBlock`, no per-instance callbacks, no animated cross-fade (`HD/Documentation~/make-object-compatible-gpu-rendering.md`) | Vary instances by transform and by a small set of shared materials, never by property block |
| Unity Terrain with terrain trees and details | Terrain textures cannot use mip streaming (`DOC/Manual/TextureStreaming-introduction.html`); the Resident Drawer handles only MeshRenderers (`HD/Documentation~/gpu-resident-drawer.md`) | Decide mesh ground versus Terrain before placing anything |
| Texture import settings chosen late (`streamingMipmaps`, max size, compression, channel packing) | Each change re-imports every texture | Apply settings on first import from an `AssetPostprocessor`; turn `streamingMipmaps` on from the start (`DOC/ScriptReference/TextureImporter-streamingMipmaps.html`) |
| A look that depends on SSGI, ray tracing or very dense volumetrics | Cost scales badly on the weaker GPU; ray tracing is unavailable in the current asset anyway | Base lighting on baked Adaptive Probe Volumes plus sun, fog and SSAO |
| Custom shaders with tessellation or pixel displacement as a requirement | More variants, more GPU cost, harder instancing | Stock `HDRP/Lit` and `HDRP/LayeredLit` |
| One flat scene of thousands of objects | Hard to stream or cull by region later | Have the generator emit a grid-cell hierarchy now; cells can become additive scenes later |
| Never building a player | Variant stripping and "Keep All" BRG variants only bite in builds ("Build times are longer", `gpu-resident-drawer.md`) | Make one macOS player build early, even if nobody plays it |

Format compatibility is not a risk: desktop and console both use the BC family, so compression choices carry over (console side **UNVERIFIED** from local docs).

---

## 5. Import and build time, and keeping iteration short

### Sizes (measured from the Poly Haven API, files saved in `catalog/vp_files/`)

| Asset | Polygons | FBX | 1k textures | 4k textures |
|---|---|---|---|---|
| `pine_tree_01` | 17.4 M | 648.5 MB | 56 MB | 736 MB |
| `fir_tree_01` | 7.9 M | 249.3 MB | 22 MB | 285 MB |
| `island_tree_02` | 1.8 M | 32.3 MB | 22 MB | 276 MB |
| `coast_rocks_05` | 1.4 M | 64.0 MB | 4 MB | 59 MB |

Of 113 nature models in the catalogue, 29 exceed 500 k polygons and 12 exceed 2 M. All 12 are flagged `lods: true`, but that is only a flag: whether the FBX contains separate `_LOD0/_LOD1…` meshes, and whether `polycount` is LOD0 or the sum, is **UNVERIFIED** until one file is downloaded and inspected. If the meshes are named `_LODx`, "Unity imports them as LOD Group components" (`DOC/Manual/lod/mesh-lod-generate-lods.html`).

### Cost of the first import (estimates, UNVERIFIED — measure on the first run)

- **Textures.** "Larger textures will be scaled down to this size at import time" (`DOC/ScriptReference/TextureImporter-maxTextureSize.html`). The importer default is 2048 to my knowledge, but the local documentation does not state the default, so treat it as **UNVERIFIED** and set `TextureImporter.maxTextureSize` explicitly for every texture; otherwise 4k sources may be silently reduced. Expect on the order of 1 to 6 s per 4k texture (decode, mip generation, BC7 or BC5 compression). Poly Haven's FBX packages reference `.exr` normal and roughness maps for some assets (`coast_rocks_05`): these need explicit importer settings or they import as HDR colour textures.
- **Models.** A 650 MB, 17 M-polygon FBX is on the order of minutes to import and several GB of RAM, before any LOD generation. "Generating LODs increases import time" (`DOC/Manual/lod/mesh-lod-generate-lods.html`).
- **Mesh LOD quality on foliage.** Unity's own troubleshooting page shows a tree whose foliage disappears at a generated LOD4 (`DOC/Manual/lod/mesh-lod-troubleshooting.html`). Cap the generated levels with `ModelImporter.maximumMeshLod` and check LOD stills (section 2).
- **Shaders.** Variants compile on first use and are cached in `Library/ShaderCache` (20 MB today). The first capture after a new material type appears, and the first use of each debug view, will stall; later runs do not. Compile time on Metal is **UNVERIFIED**.
- **Disk.** `Library` is 1.7 GB today. Budget roughly the size of the source assets again for imported artifacts.

How to measure rather than guess: the Editor log prints `Asset Pipeline Refresh (id=…): Total: N seconds` for every refresh (seen in `PRJ/Logs/Editor.log`); add `-timestamps` to the command line (`DOC/Manual/EditorCommandLineArguments.html`) and record stage durations in the metrics JSON.

### Keeping loops short

1. **Stages with stamps.** Split the build into independently runnable stages — fetch, import, derived assets (materials, prefabs, LOD setup), scene assembly, lighting bake, NavMesh, capture, tests — selected by a command-line argument, each skipping itself when a hash of its inputs is unchanged. Scene assembly from a seed should take seconds; it must not trigger imports.
2. **Never delete and recreate imported assets.** `GreyboxSceneBuilder.cs:74-76` deletes its asset folder on every build. That is harmless for a few materials and ruinous for gigabytes of textures and meshes. Keep source assets and their `.meta` files stable; regenerate only the scene and light derived assets.
3. **Set importer settings before the first import**, from an `AssetPostprocessor` keyed on path (or pre-written `.meta` files). Importing with defaults and then changing settings imports everything twice.
4. **Proxy tier for textures.** `EditorUserBuildSettings.overrideMaxTextureSize = 1024` and `EditorUserBuildSettings.overrideTextureCompression = OverrideTextureCompression.ForceFastCompressor` (enum in `UnityEditor.Build`, `DOC/ScriptReference/Build.OverrideTextureCompression.html`), followed by `AssetDatabase.Refresh()`. Documented as "mostly useful for local development, to speed up texture importing". Two cautions: the override "persists in the Library/EditorUserBuildSettings.asset file", and switching it re-imports all textures. The equivalent command-line flags `-overrideMaxTextureSize` and `-overrideTextureCompression` exist, but the accepted values for the second are **UNVERIFIED**; use the C# properties.
5. **Better still, download the 1k files first.** Work at 1k until layout and lighting are approved, then fetch and import 2k or 4k once. Note this helps textures only; geometry is identical at every tier.
6. **Parallel import.** The project imports sequentially (`m_RefreshImportMode: 0`). `EditorSettings.refreshImportMode = AssetDatabase.RefreshImportMode.OutOfProcessPerQueue` with `AssetDatabase.DesiredWorkerCount` imports textures and models in worker processes (`DOC/Manual/ParallelImport.html`). Post-processors then run in the workers and "must be self-contained, deterministic". This is a project-setting change, so it is the owner's decision.
7. **Runtime proxy without re-import.** `QualitySettings.globalTextureMipmapLimit = 2` renders with quarter-size textures (`DOC/ScriptReference/QualitySettings-globalTextureMipmapLimit.html`); `QualitySettings.maximumLODLevel`, `lodBias` and `meshLodThreshold` do the same for geometry. Useful for fast captures; not for memory or import time.
8. **Low-resolution captures while iterating** (960×540, 16 frames), full plan only on request.
9. **Avoid Editor start-up per iteration.** A batch run pays project load each time (about 4.6 s project loading in the current log, before any forest content). An in-editor command runner (an `EditorApplication.update` hook that executes a queued stage) removes that and the project-lock conflict, at the price of blocking the owner's Editor during imports.

---

## 6. Automated checks worth writing

Assembly note: `PRJ/Assets/Game/Tests/PlayMode/Gamex.Tests.PlayMode.asmdef` references only `Gamex.Runtime` and `UnityEngine.TestRunner`. Forest tests need `Unity.RenderPipelines.Core.Runtime`, `Unity.RenderPipelines.HighDefinition.Runtime`, and for input simulation `Unity.InputSystem` and `Unity.InputSystem.TestFramework`. A separate EditMode test assembly (Editor platform) is the right home for asset and scene audits, because those need `UnityEditor`.

### EditMode (fast, no rendering)

| Check | How |
|---|---|
| No missing or error materials | For every `Renderer` in the scene, every entry of `sharedMaterials`: not null; `shader != null`; `shader.name` is none of `Hidden/InternalErrorShader` (`HDRenderPipeline.cs:653`), `Hidden/HDRP/FallbackError` (`HD/Runtime/Material/FallbackError.shader`, the `FallBack` of `Lit.shader:1538`), `Hidden/HDRP/MaterialError` (`HDRenderPipelineAsset.cs:259`); `shader.isSupported`; `!ShaderUtil.ShaderHasError(shader)` (`DOC/ScriptReference/ShaderUtil.ShaderHasError.html`) |
| Every material is an HDRP material | `material.GetTag("RenderPipeline", false, "") == "HDRenderPipeline"` (`DOC/ScriptReference/Material.GetTag.html`; tag declared at `HD/Runtime/Material/Lit/Lit.shader:350`). HDRP draws anything with legacy pass tags (`Always`, `ForwardBase`, `Deferred`, `PrepassBase`, `Vertex`…) with the pink error shader (`HDRenderPipeline.cs:200`, `HDRenderPipeline.RenderGraph.cs:1020-1046`). FBX materials are converted to `HDRP/Lit` on import by `HD/Editor/AssetProcessors/FBXMaterialDescriptionPostprocessor.cs:10-39`, so a failure here usually means a hand-made or mis-imported material. |
| No missing textures on used slots | For each material, base, normal and mask map properties that the keywords enable are non-null |
| Texture policy | Via `TextureImporter`: `maxTextureSize <= 4096`, count at 4096 within budget, normal maps typed as normal maps, masks linear (`sRGBTexture == false`), `mipmapEnabled`, `streamingMipmaps`; no large uncompressed formats |
| Mesh policy | For each unique mesh: triangles from `Mesh.GetIndexCount`; `Mesh.lodCount > 1` (`DOC/ScriptReference/Mesh-lodCount.html`) or a parent `LODGroup` when above a threshold; `!mesh.isReadable` unless needed |
| Scene totals | Renderer count, unique mesh and material counts, summed triangles at LOD0 and at lowest LOD, all against budgets |
| Instancing compatibility | No `MaterialPropertyBlock` use on environment renderers; shared mesh and material references rather than copies (compare instance IDs) |
| Playable area is closed | Raycast or `Physics.CheckCapsule` sweep around the boundary: the player capsule cannot pass |
| Ground under the trail | From each trail sample, `Physics.Raycast` down hits ground within a tolerance; slope below the `CharacterController` limit |
| NavMesh coverage | `NavMesh.SamplePosition` on a grid over the playable area with a small radius: share of walkable samples above a threshold; `NavMesh.CalculatePath` between consecutive trail waypoints and between planned dinosaur patrol points returns `NavMeshPathStatus.PathComplete`; area from `NavMesh.CalculateTriangulation()` within an expected range |
| Sight lines for stealth | From sample points at eye height, share of rays blocked within N metres by colliders: guards against a forest that is visually dense but has no colliders on cover, or the opposite |
| Lighting data present | Sun light exists with shadows on; a global `Volume` with the expected overrides; baked probe data referenced if the design bakes |

### PlayMode

| Check | How |
|---|---|
| Player spawns on the ground | Load the scene, wait a few frames, assert `CharacterController.isGrounded` and that the player's height is within tolerance of the ground hit below the spawn |
| Player can walk the trail | Drive real input: the reader binds `<Keyboard>/w` and friends in code (`PRJ/Assets/Game/Scripts/Player/PlayerInputReader.cs:37-55`). Use `InputTestFixture` (add a `Keyboard` with `InputSystem.AddDevice<Keyboard>()`, then `Press(keyboard.wKey)`), steering with `PlayerController.Teleport(position, yaw)` only to face the next waypoint (`PlayerController.cs:81-89`). `InputTestFixture` must not be combined with `[SetUp]`/`[TearDown]` methods; override its `Setup`/`TearDown` (`PRJ/Library/PackageCache/com.unity.inputsystem@21a28c3a6c83/Documentation~/Testing.md`). Assert each waypoint is reached within a time limit and the player never falls below the ground. In batch mode there is no physical keyboard, so the device must be added. |
| No error or placeholder pixels | Render two or three viewpoints and count pixels near pure magenta (255, 0, 255) and pure cyan (0, 255, 255); fail above a small share. This catches cases the material audit cannot, such as a variant that fails only at draw time. Pure-magenta share as a detector is a heuristic. |
| Image sanity | Mean luminance inside a window, not more than a few percent clipped or crushed, non-trivial variance (catches black frames and blown exposure) |
| Budgets | The counters and memory figures of section 3 against the table in section 4 |
| No errors logged | Unity Test Framework already fails a test on an unexpected error log; keep it that way and do not blanket-ignore HDRP messages |
| Dinosaur path smoke test | A `NavMeshAgent` of the planned dinosaur radius and height can complete a route across the area (anticipates the gameplay phase cheaply) |

Keep the existing `StealthLoopTests` running against the greybox; the forest builder must not drop `Greybox` from Build Settings (see section 1, option B).

---

## 7. Risks: batch-mode rendering versus what the owner sees in the Game view

| Risk | Detail | Mitigation |
|---|---|---|
| Edit-mode capture is not play mode | Shader time 0, instant exposure, no motion blur, no cloud motion (section 1) | Use the PlayMode capture as the reference; use edit-mode capture for speed |
| Resolution and aspect | Captures are 16:9 at a chosen size; the Game view uses its window size, on 1080p 75 Hz displays; FOV is vertical, so horizontal coverage changes with aspect | Agree one Game view preset (16:9) and capture at that aspect; state the resolution in every report |
| Performance is not comparable | Batch: no window, no presentation, no vsync. Game view: vsync (`vSyncCount: 1` at Ultra), other Editor windows repainting, Metal API Validation on. Unity's own note: "Rendering usually speeds up less in the Scene view and the Game view, compared to Play mode or a final built project" (`gpu-resident-drawer.md`) | Report headless numbers as an optimistic bound; get one real number from a development player build |
| Anti-aliasing default | `None` unless set (finding 2) | Set it in the builder, not only in the capture code, so both see the same |
| Dithering default | `dithering = false` (`HDAdditionalCameraData.cs:314`): banding in fog and sky gradients in both, more visible on the owner's display than in a downscaled PNG | Enable it on the camera |
| Asynchronous shader compilation | Game view may show cyan placeholders briefly; script renders compile synchronously | Nothing to fix; tell the owner. Realtime reflection probes re-render after compilation (`HD/Runtime/Lighting/Reflection/HDProbe.cs:349-352`) |
| Debug state leaking | Debug display settings are a serialized Editor singleton | Reset in `finally`; add a test that asserts all debug modes are off |
| Persistent import overrides | `overrideTextureCompression` persists in `Library` | Print the override values at the start of every run and in every report |
| Texture streaming | Off today (`streamingMipmapsActive: 0`). Once on, a capture can catch textures before they stream in | Set `Texture.streamingTextureForceLoadAll = true` during capture, or wait |
| Quality level | Batch mode and the open Editor read the same asset (`m_CurrentQuality: 5`), but a test that changes quality settings and does not restore them alters what the owner sees | Restore in teardown |
| HDR display output | `allowHDRDisplaySupport: 0`, so both are SDR today | Re-check if HDR output is enabled later |
| Scene view is not the Game view | The Scene view has its own toggles for fog, post-processing and animated materials, and disables TAA unless "always refresh" is on (`HDCamera.cs:2015-2023`) | Ask the owner to judge in the Game view |
| Ambient lighting on the first frames | CPU ambient probe arrives by asynchronous readback | Warm-up frames plus `AsyncGPUReadback.WaitAllRequests()` |
| GPU architecture hides problems | Apple GPUs are tile-based deferred; the documentation flags their GPU timing as potentially inflated, and their behaviour with heavy alpha-tested overdraw differs from a console GPU (**UNVERIFIED** in which direction for this content) | Track overdraw and triangle density analytically; do not infer PS5 behaviour from Mac timings alone |
| The project lock | Batch runs fail while the owner's Editor is open | Agree run windows, or build the in-editor runner |
| Unverified measurement path | GPU timing and automatic camera rendering in batch-mode play mode have not been exercised | Run the probe in the first recommendation below before building on them |

---

## Recommendations for the build

1. **Run a ten-minute probe before anything else** (one batch run, greybox scene, no new assets) to settle the UNVERIFIED items: does `FrameTimingManager` return non-zero `gpuFrameTime` in batch-mode play mode; do the named `ProfilerRecorder` counters report plausible values for an explicit `Camera.Render()`; do `AsyncGPUReadback` callbacks fire between renders in `-executeMethod`; do enabled cameras render on their own in batch-mode play mode; how long the first shader compile takes. Write the answers into this folder.
2. **Download one large tree FBX and inspect it** before choosing assets: are there `_LODx` meshes, and what is the real LOD0 count. Filter the asset list by polygon count; do not plan around `pine_tree_01` or `fir_tree_01` at LOD0.
3. **Fix the camera in the builder:** TAA, dithering on, so captures and the Game view agree.
4. **Replace the capture routine** with: history reset per viewpoint, 64 frames, synchronous shader compilation, one `AsyncGPUReadback.WaitAllRequests()`, a JSON sidecar of image statistics, a 1:1 crop. Keep it in edit mode for the fast loop.
5. **Add a PlayMode capture-and-measure test** with an explicitly rendered camera and a fixed time step as the reference for "what the player sees", and the home of the budget assertions.
6. **Do not use** `ScreenCapture`, `WaitForEndOfFrame`, the Recorder package, `UnityStats`, or the `QuadOverdraw`/`VertexDensity` debug modes.
7. **Capture the debug set** (albedo, normal, smoothness, diffuse-lighting-only, clay render, albedo validation, SSAO, fog only, cascades, mip ratio) for three or four key viewpoints only, with debug state reset in `finally`.
8. **Adopt the budgets in section 4 as provisional**, assert counts and memory in tests from day one, and replace the time budgets with measured ones after the probe.
9. **Stage the build and never re-import by accident:** stable asset paths and metas, importer settings applied on first import, proxy textures (1k downloads or `overrideMaxTextureSize`) until the look is approved, low-resolution captures while iterating.
10. **Decide three things now because they are expensive later:** the LOD path per asset, mesh ground versus Terrain, and whether to enable the GPU Resident Drawer (needs "BatchRendererGroup Variants: Keep All" and the asset mode; both are project-setting changes for the owner to approve).
11. **Write the audits in section 6 as an EditMode suite** that runs in seconds after every scene rebuild, and the walk, pixel and budget checks as a PlayMode suite.
12. **Agree the workflow around the project lock** with the owner: batch runs only while the Editor is closed, or an in-editor command runner.
13. **Make one macOS development player build early** to obtain a frame-time number that does not carry Editor overhead and to surface shader-variant problems before they are expensive.

### Small files this research left in the scratchpad

- `forest/scripts/vp_doc.py` — strips a local Unity documentation page to text.
- `forest/catalog/vp_files/*.json`, `forest/catalog/vp_info/*.json` — Poly Haven file and info records for `pine_tree_01`, `fir_tree_01`, `island_tree_02`, `coast_rocks_05`, `fern_02`.
