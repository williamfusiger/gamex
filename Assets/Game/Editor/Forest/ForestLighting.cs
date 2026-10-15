using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Preset = Gamex.EditorTools.ForestLightingPresets.Preset;

namespace Gamex.EditorTools
{
    /// <summary>
    /// Builds the lighting of the forest scene from the numbers in <see cref="ForestLightingPresets"/>.
    /// Every preset is a child of the "Lighting" object with its own global volume and its own key light,
    /// and exactly one child is active, so switching the look is one SetActive call and two suns never
    /// cast shadows at once. Nothing here needs a bake or a project setting.
    /// </summary>
    public static class ForestLighting
    {
        const string RootName = "Lighting";
        const string PresetPrefix = "Preset_";
        const string ProfilePrefix = "Atmosphere_";
        const string InspectName = "Inspect";

        // The sky is scaled to the preset's lux with the value ForestSky integrates from the 1k EXR. If that
        // calibration ever misbehaves, set this to false: the sky then uses a plain multiplier that assumes
        // the unscaled image gives AssumedRawSkyLux, and the exposure clamp limits how wrong the frame can be.
        static readonly bool CalibrateSkyInLux = true;
        // What a sky of uniform radiance 1 gives. Never rendered: tune it from a capture before relying on it.
        const float AssumedRawSkyLux = Mathf.PI;

        // The same in every preset (section 8.3).
        const float ContactShadowLength = 0.10f;
        const float ContactShadowMaxDistance = 40f;
        const float ContactShadowFadeDistance = 8f;
        // Micro-shadowing darkens with the material's AO on top of the AO itself. At full opacity the scans' own
        // occlusion was applied twice on a light that is four-fifths sky, and the faces of rocks, stumps and
        // branches fell to a third of the ground beside them (review 2 of the M2 captures).
        const float MicroShadowOpacity = 0.5f;
        // In SkyColor mode the fog's colour is the sky's, multiplied by this tint (HD Fog.cs:252). The sky is
        // slightly warm; a cool tint lets distance read as grey air instead of faded brown (review 2).
        static readonly Color FogTint = new Color(0.93f, 0.97f, 1.0f);
        // The spec's starting values (intensity 1.5, radius 2 m) were meant to take back the open-sky light on
        // the floor under the firs. On the M2 floor a 2 m radius darkened whole props instead of their contacts:
        // every grass tuft and fern stood in a sooty blot half a metre wide, and rocks, logs and branches fell
        // to near-black beside the litter (review 1 of the M2 captures; issues/M2-review1.md). A radius of one
        // metre keeps contact shading and crevices and leaves the faces of a boulder alone.
        const float AmbientOcclusionIntensity = 1.0f;
        const float AmbientOcclusionRadius = 1f;
        const float AmbientOcclusionDirectStrength = 0.25f;
        const int KeyShadowResolution = 2048;

        // The same in every preset (section 9.2).
        static readonly Vector2 HistogramPercentages = new(40f, 90f);
        const float BloomScatter = 0.65f;
        const float BloomThreshold = 0f;
        const float VignetteSmoothness = 0.4f;
        const float GrainResponse = 0.8f;
        // The project default of 0.5 smears thin foliage, and the player's job is to spot movement.
        const float MotionBlurIntensity = 0f;

        const float TaaSharpenStrength = 0.5f;

        // A sky on disk together with the number that scales it.
        struct SkyInput
        {
            public Cubemap cubemap;
            public ForestSky.Calibration calibration;   // filled only when CalibrateSkyInLux
        }

        /// <summary>
        /// Creates the "Lighting" root in the active scene with one child per preset whose sky is downloaded,
        /// and writes the profile assets. Throws when the requested preset cannot be built: a scene without
        /// its lighting renders black or white.
        /// </summary>
        public static GameObject Build(string activePreset)
        {
            if (string.IsNullOrEmpty(activePreset))
                activePreset = ForestLightingPresets.Default;
            if (ForestLightingPresets.Get(activePreset) == null)
                throw new ArgumentException($"Unknown lighting preset '{activePreset}'. Known presets: {string.Join(", ", ForestLightingPresets.Names)}.");

            // Skies are read and profiles written before the scene is touched, so a missing or broken sky
            // cannot leave half a lighting rig behind.
            var presets = new List<Preset>();
            var skies = new List<SkyInput>();
            foreach (string name in ForestLightingPresets.Names)
            {
                Preset preset = ForestLightingPresets.Get(name);
                Validate(preset);
                if (TryLoadSky(preset, out SkyInput sky))
                {
                    presets.Add(preset);
                    skies.Add(sky);
                }
                else
                {
                    Debug.Log($"Forest lighting: preset {name} is left out, its sky '{preset.skyId}' has not been downloaded.");
                }
            }

            int active = presets.FindIndex(preset => preset.name == activePreset);
            if (active < 0)
                throw new InvalidOperationException($"Lighting preset {activePreset} cannot be built: its sky " +
                    $"'{ForestLightingPresets.SkyId(activePreset)}' has not been downloaded (Tools/forest/fetch_assets.py, milestone M1).");

            var profiles = new List<VolumeProfile>();
            for (int i = 0; i < presets.Count; i++)
            {
                profiles.Add(WritePresetProfile(presets[i], skies[i]));
                LogExposure(presets[i]);
            }

            // Kit turntables look at assets under the default sky; refreshed here so it follows a retuned preset.
            int inspect = presets.FindIndex(preset => preset.name == ForestLightingPresets.Default);
            if (inspect >= 0)
                WriteInspectProfile(presets[inspect], skies[inspect]);

            Scene scene = SceneManager.GetActiveScene();
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
            {
                if (IsLightingRoot(sceneRoot))
                    Object.DestroyImmediate(sceneRoot);
            }

            var root = new GameObject(RootName);
            for (int i = 0; i < presets.Count; i++)
            {
                var child = new GameObject(PresetPrefix + presets[i].name);
                child.transform.SetParent(root.transform, false);

                var volume = child.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.sharedProfile = profiles[i];

                AddKeyLight(child, presets[i]);

                // Set up while active, then switched off: an inactive child keeps its light and volume but HDRP
                // does not count them, and it allows only one shadow-casting directional light.
                child.SetActive(i == active);
            }

            if (!Application.isPlaying)
                EditorSceneManager.MarkSceneDirty(scene);

            WarnAboutOtherLighting(scene, root.transform);
            Debug.Log($"Forest lighting built: preset {activePreset} active, {presets.Count} of {ForestLightingPresets.Names.Length} presets present, " +
                $"sky in {(CalibrateSkyInLux ? "Lux" : "Multiplier")} mode, graphics device {SystemInfo.graphicsDeviceType}.");
            return root;
        }

        /// <summary>
        /// The player camera settings of section 9.1. Done by the builder, not only by the capture code, so the
        /// Game view and the captures agree.
        /// </summary>
        public static void ConfigureCamera(Camera camera)
        {
            if (camera == null)
                throw new ArgumentNullException(nameof(camera));

            var data = camera.GetComponent<HDAdditionalCameraData>();
            if (data == null)
                data = camera.gameObject.AddComponent<HDAdditionalCameraData>();

            // The default is no anti-aliasing at all, and alpha-tested foliage shimmers without it.
            data.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            data.TAAQuality = HDAdditionalCameraData.TAAQualityLevel.High;
            data.taaSharpenStrength = TaaSharpenStrength;
            // Hides banding in mist gradients.
            data.dithering = true;
            // Insurance with HDR skies.
            data.stopNaNs = true;
            // Knob 1 of spec section 10: the scene has no decals, but with decals enabled in the project HDRP draws every
            // opaque object in a full depth prepass before the G-buffer. This camera alone turns the Decals frame setting
            // off, so the prepass draws the alpha-tested foliage only, which it needs anyway. Every other frame setting
            // stays on the project's default, like the greybox. Measured as an A/B on the M3 scene: 0.15 to 0.65 M
            // triangles and up to 0.3 ms fewer per view (build/issues/M3-placement.md).
            data.customRenderingSettings = true;
            data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.Decals] = true;
            data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.Decals, false);

            // SetDirty also marks the scene as changed, which a throwaway capture camera must not do.
            if ((camera.gameObject.hideFlags & HideFlags.DontSaveInEditor) == 0)
                EditorUtility.SetDirty(data);
        }

        /// <summary>
        /// Activates one preset of the open scene and deactivates the others. False when the scene has no forest
        /// lighting or no such preset. Does not save.
        /// </summary>
        public static bool ApplyPreset(string preset)
        {
            Transform root = FindLightingRoot();
            if (root == null || string.IsNullOrEmpty(preset))
                return false;
            Transform target = root.Find(PresetPrefix + preset);
            if (target == null)
                return false;

            bool changed = false;
            // The others go off first, so two shadow-casting suns are never active together.
            foreach (Transform child in root)
            {
                if (child != target && IsPreset(child) && child.gameObject.activeSelf)
                {
                    child.gameObject.SetActive(false);
                    changed = true;
                }
            }
            if (!target.gameObject.activeSelf)
            {
                target.gameObject.SetActive(true);
                changed = true;
            }

            if (changed && !Application.isPlaying)
                EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            return true;
        }

        /// <summary>The active preset of the open scene, or "" when no forest lighting is in the scene.</summary>
        public static string ActivePreset()
        {
            Transform root = FindLightingRoot();
            if (root == null)
                return "";
            foreach (Transform child in root)
            {
                if (IsPreset(child) && child.gameObject.activeInHierarchy)
                    return child.name.Substring(PresetPrefix.Length);
            }
            return "";
        }

        /// <summary>
        /// Atmosphere_Inspect: the default sky with no fog, fixed exposure and neutral grading, for kit turntables
        /// and LOD sheets. Created when it is missing or has lost its sky; an existing one is returned untouched,
        /// because captures must not write inside Assets. A scene build refreshes it.
        /// </summary>
        public static VolumeProfile InspectProfile()
        {
            var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath(InspectName));
            if (IsUsableInspectProfile(existing))
                return existing;

            Preset preset = ForestLightingPresets.Get(ForestLightingPresets.Default);
            Validate(preset);
            if (!TryLoadSky(preset, out SkyInput sky))
                throw new InvalidOperationException($"The inspect profile needs the sky '{preset.skyId}', which has not been downloaded " +
                    "(Tools/forest/fetch_assets.py, milestone M1).");
            return WriteInspectProfile(preset, sky);
        }

        /// <summary>
        /// Switches the open scene to the preset named by -forestPreset (the default preset without it, as in a
        /// menu run) and saves the scene.
        /// </summary>
        [MenuItem("Gamex/Forest/Preset/Apply From Arguments")]
        public static void ApplyPresetFromArgs()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Lighting presets are applied to the saved scene; leave play mode first.");

            string preset = ForestArgs.Value("-forestPreset", ForestLightingPresets.Default);

            // A batch run starts in whatever scene Unity opens by itself. Only the forest scene can be meant.
            if (FindLightingRoot() == null && Application.isBatchMode && File.Exists(ForestPaths.ScenePath))
                EditorSceneManager.OpenScene(ForestPaths.ScenePath, OpenSceneMode.Single);

            if (!ApplyPreset(preset))
                throw new InvalidOperationException(FindLightingRoot() == null
                    ? "The open scene has no forest lighting. Open or rebuild the forest scene first."
                    : $"The open scene has no lighting preset '{preset}'. Its sky may not be downloaded; rebuild the scene after fetching it.");

            Scene scene = FindLightingRoot().gameObject.scene;
            if (string.IsNullOrEmpty(scene.path))
                throw new InvalidOperationException("The open scene has never been saved, so the preset cannot be saved with it.");
            if (!EditorSceneManager.SaveScene(scene))
                throw new IOException($"Could not save {scene.path}.");

            Debug.Log($"Forest lighting: preset {preset} applied and saved in {scene.path}.");
        }

        // ---- Skies ----

        // False when the sky is not on disk. A sky that is on disk but unusable throws inside ForestSky.
        static bool TryLoadSky(Preset preset, out SkyInput sky)
        {
            sky = default;
            ForestAssetEntry entry = ForestManifest.Find(preset.skyId)
                ?? throw new InvalidOperationException($"Lighting preset {preset.name} names the sky '{preset.skyId}', which is not in the asset manifest.");
            if (!ForestManifest.IsDownloaded(entry))
                return false;

            sky.cubemap = ForestSky.LoadCubemap(preset.skyId);
            if (sky.cubemap == null)
                return false;

            // A wrong import shows here first: a 4k lat-long gives faces of 2048 (measured by probe P8) in an HDR format.
            Debug.Log($"Forest sky {preset.skyId}: cubemap faces {sky.cubemap.width} px, format {sky.cubemap.format}.");
            if (!UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsHDRFormat(sky.cubemap.format))
                Debug.LogWarning($"Forest sky {preset.skyId}: the cubemap format {sky.cubemap.format} is not HDR, so bright parts of the sky are clipped. " +
                    "Check the import settings of the __sky.hdr file.");

            if (CalibrateSkyInLux)
                sky.calibration = ForestSky.Calibrate(preset.skyId);
            return true;
        }

        // ---- Profiles ----

        static string ProfilePath(string name) => $"{ForestPaths.LightingFolder}/{ProfilePrefix}{name}.asset";

        static VolumeProfile WritePresetProfile(Preset preset, SkyInput sky)
        {
            RequireCalibration(preset, sky);
            VolumeProfile profile = OpenProfile(ProfilePath(preset.name));
            AddSky(profile, preset, sky);
            AddShadows(profile, preset);
            AddAmbientOcclusion(profile);

            var fog = AddOverride<Fog>(profile);
            fog.enabled.Override(true);
            fog.enableVolumetricFog.Override(true);
            fog.colorMode.Override(FogColorMode.SkyColor);
            fog.tint.Override(FogTint);
            fog.meanFreePath.Override(preset.fogMeanFreePath);
            fog.baseHeight.Override(preset.fogBaseHeight);
            fog.maximumHeight.Override(preset.fogMaximumHeight);
            fog.albedo.Override(preset.fogAlbedo);
            fog.anisotropy.Override(preset.fogAnisotropy);
            fog.depthExtent.Override(preset.fogDepthExtent);
            fog.denoisingMode.Override(FogDenoisingMode.Gaussian);
            // The fog budget and depth ratio have setters that write no override state; the tier is the way in.
            fog.quality.Override((int)(preset.fogHighQuality ? ScalableSettingLevelParameter.Level.High : ScalableSettingLevelParameter.Level.Medium));

            var exposure = AddOverride<Exposure>(profile);
            exposure.compensation.Override(preset.exposureCompensation);
            if (preset.fixedExposure)
            {
                exposure.mode.Override(ExposureMode.Fixed);
                exposure.fixedExposure.Override(preset.exposureFixed);
            }
            else
            {
                exposure.mode.Override(ExposureMode.AutomaticHistogram);
                exposure.meteringMode.Override(MeteringMode.CenterWeighted);
                exposure.limitMin.Override(preset.exposureMin);
                exposure.limitMax.Override(preset.exposureMax);
                exposure.histogramPercentages.Override(HistogramPercentages);
                // With curve remapping on, HDRP takes both limits from a curve and ignores the two above.
                // The project default leaves it off; pinned here so the clamp cannot be lost to a default.
                exposure.histogramUseCurveRemapping.Override(false);
            }

            AddGrading(profile, preset.contrast, preset.saturation, preset.whiteBalanceTemperature);
            AddLens(profile, preset.bloomIntensity, ParseGrain(preset), preset.grainIntensity, preset.vignetteIntensity);
            AddFoliageProfile(profile);
            return Finish(profile);
        }

        static VolumeProfile WriteInspectProfile(Preset preset, SkyInput sky)
        {
            RequireCalibration(preset, sky);
            VolumeProfile profile = OpenProfile(ProfilePath(InspectName));
            AddSky(profile, preset, sky);
            AddShadows(profile, preset);
            AddAmbientOcclusion(profile);

            // Every look-defining effect is overridden to "off" instead of left out, so the project default
            // profile cannot leak mist, bloom or a moving exposure into a picture meant for judging an asset.
            AddOverride<Fog>(profile).enabled.Override(false);

            var exposure = AddOverride<Exposure>(profile);
            exposure.mode.Override(ExposureMode.Fixed);
            // Neutral for an open surface under this sky alone, so every turntable is exposed alike.
            exposure.fixedExposure.Override(NeutralEv(preset.skyLux));
            exposure.compensation.Override(0f);

            AddGrading(profile, 0f, 0f, 0f);
            AddLens(profile, 0f, FilmGrainLookup.Thin1, 0f, 0f);
            AddFoliageProfile(profile);
            return Finish(profile);
        }

        // An existing profile is emptied and refilled in place: the asset keeps its GUID, so a scene or a
        // capture that already points at it stays valid, and nothing is ever deleted from ForestData.
        static VolumeProfile OpenProfile(string path)
        {
            EnsureFolder(ForestPaths.LightingFolder);

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
                return profile;
            }

            foreach (Object part in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (part == null || part == profile)
                    continue;
                AssetDatabase.RemoveObjectFromAsset(part);
                Object.DestroyImmediate(part, true);
            }
            profile.components.Clear();
            return profile;
        }

        static VolumeProfile Finish(VolumeProfile profile)
        {
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            return profile;
        }

        // Folders are created through the asset database, never deleted. One that another stage made on disk and
        // Unity has not seen yet is imported: CreateFolder would put "<name> 1" beside it.
        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;

            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent))
                throw new IOException($"'{folder}' is not a folder inside the project.");
            EnsureFolder(parent);

            if (!Directory.Exists(folder))
            {
                AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
            }
            else
            {
                AssetDatabase.ImportAsset(folder);
                if (!AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.Refresh();
            }

            if (!AssetDatabase.IsValidFolder(folder))
                throw new IOException($"Could not create the folder {folder}.");
        }

        // Override() is used for every field: a value assigned without its override state does nothing in a
        // profile. It also bypasses the parameter's clamping, which is why Validate checks the ranges.
        static T AddOverride<T>(VolumeProfile profile) where T : VolumeComponent
        {
            T component = profile.Add<T>();
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        static void AddSky(VolumeProfile profile, Preset preset, SkyInput sky)
        {
            var environment = AddOverride<VisualEnvironment>(profile);
            environment.skyType.Override((int)SkyType.HDRI);
            environment.skyAmbientMode.Override(SkyAmbientMode.Dynamic);

            var hdri = AddOverride<HDRISky>(profile);
            hdri.hdriSky.Override(sky.cubemap);
            hdri.rotation.Override(preset.skyRotation);

            if (!CalibrateSkyInLux)
            {
                hdri.skyIntensityMode.Override(SkyIntensityMode.Multiplier);
                hdri.multiplier.Override(preset.skyLux / AssumedRawSkyLux);
                Debug.LogWarning($"Forest lighting: sky {preset.skyId} is scaled by an assumed multiplier of {preset.skyLux / AssumedRawSkyLux}, not by its measured lux.");
                return;
            }

            // HDRP scales the sky by desired / measured. The measured value defaults to 1 and only the Inspector
            // would ever fill it in, so it is always written here.
            hdri.skyIntensityMode.Override(SkyIntensityMode.Lux);
            hdri.desiredLuxValue.Override(preset.skyLux);
            hdri.upperHemisphereLuxValue.Override(sky.calibration.upperHemisphereLux);
            hdri.upperHemisphereLuxColor.Override(sky.calibration.luxColor);
        }

        // ForestSky.Calibrate has checked the number already. It is checked again before a profile is opened,
        // because this is where a wrong one does its damage: it multiplies the sky by thousands.
        static void RequireCalibration(Preset preset, SkyInput sky)
        {
            float rawLux = sky.calibration.upperHemisphereLux;
            if (CalibrateSkyInLux && (!(rawLux > 0f) || float.IsInfinity(rawLux)))
                throw new InvalidOperationException($"Sky {preset.skyId} has an unusable calibration ({rawLux} lux); no profile was written.");
        }

        static void AddShadows(VolumeProfile profile, Preset preset)
        {
            var shadows = AddOverride<HDShadowSettings>(profile);
            shadows.maxShadowDistance.Override(preset.shadowDistance);
            shadows.cascadeShadowSplitCount.Override(preset.cascades);
            // Fractions of the maximum distance.
            shadows.cascadeShadowSplit0.Override(preset.cascadeSplits.x);
            shadows.cascadeShadowSplit1.Override(preset.cascadeSplits.y);
            shadows.cascadeShadowSplit2.Override(preset.cascadeSplits.z);

            // Re-attaches ferns and small props to the ground where the cascades get coarse.
            var contact = AddOverride<ContactShadows>(profile);
            contact.enable.Override(true);
            contact.length.Override(ContactShadowLength);
            contact.maxDistance.Override(ContactShadowMaxDistance);
            contact.fadeDistance.Override(ContactShadowFadeDistance);

            // Free small-scale shadowing from the occlusion maps of the scans.
            var micro = AddOverride<MicroShadowing>(profile);
            micro.enable.Override(true);
            micro.opacity.Override(MicroShadowOpacity);
        }

        static void AddAmbientOcclusion(VolumeProfile profile)
        {
            var occlusion = AddOverride<ScreenSpaceAmbientOcclusion>(profile);
            occlusion.intensity.Override(AmbientOcclusionIntensity);
            occlusion.radius.Override(AmbientOcclusionRadius);
            occlusion.directLightingStrength.Override(AmbientOcclusionDirectStrength);
        }

        static void AddGrading(VolumeProfile profile, float contrast, float saturation, float temperature)
        {
            var colour = AddOverride<ColorAdjustments>(profile);
            colour.contrast.Override(contrast);
            colour.saturation.Override(saturation);

            AddOverride<WhiteBalance>(profile).temperature.Override(temperature);
        }

        static void AddLens(VolumeProfile profile, float bloomIntensity, FilmGrainLookup grainType, float grainIntensity, float vignetteIntensity)
        {
            var bloom = AddOverride<Bloom>(profile);
            bloom.intensity.Override(bloomIntensity);
            bloom.scatter.Override(BloomScatter);
            bloom.threshold.Override(BloomThreshold);

            var grain = AddOverride<FilmGrain>(profile);
            grain.type.Override(grainType);
            grain.intensity.Override(grainIntensity);
            grain.response.Override(GrainResponse);

            var vignette = AddOverride<Vignette>(profile);
            vignette.intensity.Override(vignetteIntensity);
            vignette.smoothness.Override(VignetteSmoothness);

            AddOverride<MotionBlur>(profile).intensity.Override(MotionBlurIntensity);
        }

        // The project default profile already registers the foliage diffusion profile. Listing it again makes
        // the scene independent of that default: the list accumulates across volumes, and a foliage material
        // whose profile is not registered renders magenta.
        static void AddFoliageProfile(VolumeProfile profile)
        {
            var foliage = AssetDatabase.LoadAssetAtPath<DiffusionProfileSettings>(ForestPaths.FoliageProfilePath);
            if (foliage == null)
            {
                // The material factory then builds foliage as the Standard material type, which needs no profile.
                Debug.LogWarning($"Forest lighting: the foliage diffusion profile was not found at {ForestPaths.FoliageProfilePath}; no profile list was added.");
                return;
            }
            AddOverride<DiffusionProfileList>(profile).diffusionProfiles.Override(new[] { foliage });
        }

        static bool IsUsableInspectProfile(VolumeProfile profile)
        {
            return profile != null
                && profile.TryGet(out HDRISky sky) && sky.hdriSky.overrideState && sky.hdriSky.value != null
                && profile.TryGet(out Exposure exposure) && exposure.mode.overrideState && exposure.mode.value == ExposureMode.Fixed;
        }

        // ---- Key light ----

        static void AddKeyLight(GameObject owner, Preset preset)
        {
            // Adding the HDRP component also adds the Light it requires.
            var data = owner.AddComponent<HDAdditionalLightData>();
            var light = owner.GetComponent<Light>();
            light.type = LightType.Directional;
            light.lightUnit = LightUnit.Lux;
            light.intensity = preset.keyLux;
            // The colour of the light is its temperature; any tint of the mood belongs in the grading.
            light.useColorTemperature = true;
            light.colorTemperature = preset.keyTemperature;
            light.color = Color.white;
            light.shadows = LightShadows.Soft;
            light.lightmapBakeType = LightmapBakeType.Realtime;

            data.angularDiameter = preset.keyAngularDiameter;
            // A script-created light starts with 512-pixel shadows and contact shadows off.
            data.SetShadowResolutionOverride(true);
            data.SetShadowResolution(KeyShadowResolution);
            data.useContactShadow.useOverride = true;
            data.useContactShadow.@override = true;
            data.affectsVolumetric = true;
            data.volumetricDimmer = preset.keyVolumetricDimmer;
            // Only a physically based sky draws a disc or is lit by the light.
            data.interactsWithSky = false;

            // The light travels toward compass bearing keyBearing (0 = north, 90 = east), downward at keyElevation.
            owner.transform.rotation = Quaternion.Euler(preset.keyElevation, preset.keyBearing, 0f);
        }

        // ---- Guards ----

        // Override() writes values past the clamping of the volume parameters, and a preset outside the ranges
        // below renders black, white or not at all. Better to stop here, with the name of the field.
        static void Validate(Preset preset)
        {
            Require(!string.IsNullOrEmpty(preset.name) && !string.IsNullOrEmpty(preset.skyId), preset, "needs a name and a sky id");

            Require(IsPositive(preset.skyLux), preset, "skyLux must be positive and finite");
            Require(IsWithin(preset.skyRotation, 0f, 360f), preset, "skyRotation must be 0 to 360");

            Require(IsPositive(preset.keyLux), preset, "keyLux must be positive and finite");
            Require(IsWithin(preset.keyTemperature, 1000f, 20000f), preset, "keyTemperature must be 1,000 to 20,000 K");
            Require(IsWithin(preset.keyAngularDiameter, 0f, 90f), preset, "keyAngularDiameter must be 0 to 90 degrees");
            Require(IsWithin(preset.keyVolumetricDimmer, 0f, 16f), preset, "keyVolumetricDimmer must be 0 to 16");
            Require(preset.keyElevation > 0f && preset.keyElevation <= 90f, preset, "keyElevation must be above 0 and at most 90 degrees: the light has to travel downward");
            Require(!float.IsNaN(preset.keyBearing) && !float.IsInfinity(preset.keyBearing), preset, "keyBearing must be finite");

            Require(IsPositive(preset.shadowDistance), preset, "shadowDistance must be positive and finite");
            Require(preset.cascades >= 1 && preset.cascades <= 4, preset, "cascades must be 1 to 4");
            Vector3 splits = preset.cascadeSplits;
            Require(splits.x > 0f && splits.x < splits.y && splits.y < splits.z && splits.z < 1f, preset,
                "cascadeSplits must increase and lie between 0 and 1 (they are fractions of shadowDistance)");

            Require(preset.fogMeanFreePath >= 1f && !float.IsInfinity(preset.fogMeanFreePath), preset, "fogMeanFreePath must be at least 1 m");
            Require(preset.fogMaximumHeight > preset.fogBaseHeight && !float.IsInfinity(preset.fogMaximumHeight) && !float.IsInfinity(preset.fogBaseHeight),
                preset, "fogMaximumHeight must be above fogBaseHeight");
            Require(IsWithin(preset.fogAnisotropy, -1f, 1f), preset, "fogAnisotropy must be -1 to 1");
            Require(preset.fogDepthExtent >= 0.1f && !float.IsInfinity(preset.fogDepthExtent), preset, "fogDepthExtent must be at least 0.1 m");
            Color albedo = preset.fogAlbedo;
            Require(IsWithin(albedo.r, 0f, 1f) && IsWithin(albedo.g, 0f, 1f) && IsWithin(albedo.b, 0f, 1f), preset, "fogAlbedo must be 0 to 1 per channel");

            Require(IsWithin(preset.exposureCompensation, -10f, 10f), preset, "exposureCompensation must be -10 to 10 EV");
            if (preset.fixedExposure)
                Require(IsWithin(preset.exposureFixed, -20f, 30f), preset, "exposureFixed must be a plausible EV100");
            else
                Require(IsWithin(preset.exposureMin, -20f, 30f) && IsWithin(preset.exposureMax, -20f, 30f) && preset.exposureMin < preset.exposureMax,
                    preset, "exposureMin must be below exposureMax, both plausible EV100 values");

            Require(IsWithin(preset.whiteBalanceTemperature, -100f, 100f), preset, "whiteBalanceTemperature must be -100 to 100");
            Require(IsWithin(preset.saturation, -100f, 100f), preset, "saturation must be -100 to 100");
            Require(IsWithin(preset.contrast, -100f, 100f), preset, "contrast must be -100 to 100");
            Require(IsWithin(preset.bloomIntensity, 0f, 1f), preset, "bloomIntensity must be 0 to 1");
            Require(IsWithin(preset.grainIntensity, 0f, 1f), preset, "grainIntensity must be 0 to 1");
            Require(IsWithin(preset.vignetteIntensity, 0f, 1f), preset, "vignetteIntensity must be 0 to 1");
            ParseGrain(preset);
        }

        static void Require(bool condition, Preset preset, string rule)
        {
            if (!condition)
                throw new InvalidOperationException($"Lighting preset '{preset.name}': {rule}.");
        }

        // NaN fails both.
        static bool IsWithin(float value, float min, float max) => value >= min && value <= max;

        static bool IsPositive(float value) => value > 0f && !float.IsInfinity(value);

        static FilmGrainLookup ParseGrain(Preset preset)
        {
            // Names only: Enum.Parse would also take "7". Custom needs a texture the preset cannot name.
            if (string.IsNullOrEmpty(preset.grainType) || !Enum.IsDefined(typeof(FilmGrainLookup), preset.grainType)
                || preset.grainType == nameof(FilmGrainLookup.Custom))
                throw new InvalidOperationException($"Lighting preset '{preset.name}': grainType '{preset.grainType}' is not a film grain HDRP ships (Thin1, Thin2, Medium1, ...).");
            return (FilmGrainLookup)Enum.Parse(typeof(FilmGrainLookup), preset.grainType);
        }

        // Neutral exposure for a surface lit with this many lux: HDRP meters to 12.5 % grey, EV100 = log2(E / 3.14).
        static float NeutralEv(float lux) => Mathf.Log(lux / 3.14f, 2f);

        // The arithmetic behind the exposure of a preset, in the log, and a warning when the numbers cannot give
        // a readable frame on open ground. The limits are heuristics, so this warns instead of stopping the build.
        static void LogExposure(Preset preset)
        {
            float openGroundLux = preset.skyLux + preset.keyLux * Mathf.Sin(preset.keyElevation * Mathf.Deg2Rad);
            float neutral = NeutralEv(openGroundLux);

            if (preset.fixedExposure)
            {
                Debug.Log($"Forest lighting {preset.name}: open ground {openGroundLux} lux, neutral at {neutral} EV; exposure fixed at {preset.exposureFixed} EV.");
                if (Mathf.Abs(preset.exposureFixed - neutral) > 3f)
                    Debug.LogWarning($"Forest lighting {preset.name}: the fixed exposure is {preset.exposureFixed - neutral} stops from neutral on open ground. " +
                        "Positive is darker; beyond three stops either way the frame is close to black or white.");
                return;
            }

            Debug.Log($"Forest lighting {preset.name}: open ground {openGroundLux} lux, neutral at {neutral} EV; " +
                $"automatic exposure clamped to {preset.exposureMin} .. {preset.exposureMax} EV, compensation {preset.exposureCompensation}.");
            if (preset.exposureMax < neutral - 3f)
                Debug.LogWarning($"Forest lighting {preset.name}: exposureMax is {neutral - preset.exposureMax} stops under neutral for open ground, which will clip to white there.");
            if (preset.exposureMin > neutral + 1f)
                Debug.LogWarning($"Forest lighting {preset.name}: exposureMin is {preset.exposureMin - neutral} stops over neutral for open ground, so even the brightest part of the scene stays dark.");
        }

        // The audit fails a scene with two global volumes or two shadow-casting suns. Saying so at build time
        // names the object while it is still obvious where it came from.
        static void WarnAboutOtherLighting(Scene scene, Transform lightingRoot)
        {
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
            {
                if (sceneRoot.transform == lightingRoot)
                    continue;
                foreach (Volume volume in sceneRoot.GetComponentsInChildren<Volume>(false))
                {
                    if (volume.enabled && volume.isGlobal)
                        Debug.LogWarning($"Forest lighting: '{volume.name}' is another active global volume in the scene; it blends with the preset.", volume);
                }
                foreach (Light light in sceneRoot.GetComponentsInChildren<Light>(false))
                {
                    if (light.enabled && light.type == LightType.Directional && light.shadows != LightShadows.None)
                        Debug.LogWarning($"Forest lighting: '{light.name}' is another shadow-casting directional light; HDRP renders shadows for only one.", light);
                }
            }
        }

        // ---- Scene lookup ----

        static bool IsPreset(Transform child) => child.name.StartsWith(PresetPrefix, StringComparison.Ordinal);

        static bool IsLightingRoot(GameObject candidate)
        {
            if (candidate.name != RootName)
                return false;
            foreach (Transform child in candidate.transform)
            {
                if (IsPreset(child))
                    return true;
            }
            return false;
        }

        // The active scene first; another loaded scene only if the active one has no forest lighting.
        static Transform FindLightingRoot()
        {
            Scene active = SceneManager.GetActiveScene();
            Transform root = FindLightingRoot(active);
            for (int i = 0; root == null && i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != active)
                    root = FindLightingRoot(scene);
            }
            return root;
        }

        static Transform FindLightingRoot(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return null;
            foreach (GameObject candidate in scene.GetRootGameObjects())
            {
                if (IsLightingRoot(candidate))
                    return candidate.transform;
            }
            return null;
        }
    }
}
