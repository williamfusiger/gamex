using System;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Gamex.EditorTools
{
    /// <summary>
    /// The numbers behind each lighting preset of the forest scene, each tuned on renders (OvercastMist in the M2 to D2
    /// reviews, Night in N1). <see cref="ForestLighting"/> turns a preset into a volume profile and a key light; the audit
    /// and the capture viewpoints read the same record, so a retuned value cannot drift away from what is checked.
    /// </summary>
    public static class ForestLightingPresets
    {
        /// <summary>
        /// The preset a scene build activates. N1 (build/issues/N1-night.md): night is the game's main mood, so the forest is built
        /// at night; OvercastMist stays in the scene beside it (Gamex/Forest/Preset/Overcast Mist, or -forestPreset OvercastMist).
        /// </summary>
        public const string Default = "Night";

        /// <summary>
        /// The daylight preset that judges assets rather than mood: kit turntables and LOD sheets (the inspect profile and its key
        /// light), the probe, and the bearing of the backlit viewpoint (which the layout's planner reads, so the plan does not move
        /// when the scene's default preset changes).
        /// </summary>
        public const string Inspect = "OvercastMist";

        public static readonly string[] Names = { "OvercastMist", "Night" };   // the other presets of Appendix C (HazySun, Dusk) are not built

        /// <summary>The values of section 8.2. Read-only for everyone outside WP4.</summary>
        public sealed class Preset
        {
            public string name, skyId;                 // skyId: the manifest id of the preset's HDRI
            public float skyLux, skyRotation;
            public float keyLux, keyTemperature, keyAngularDiameter, keyVolumetricDimmer;
            public float keyElevation, keyBearing;     // the light travels toward keyBearing; its source stands at keyBearing + 180
            public float shadowDistance; public int cascades; public Vector3 cascadeSplits;
            public float fogMeanFreePath, fogBaseHeight, fogMaximumHeight, fogAnisotropy, fogDepthExtent; public Color fogAlbedo; public bool fogHighQuality;
            public bool fixedExposure; public float exposureFixed, exposureMin, exposureMax, exposureCompensation;
            // Automatic exposure only. The metering and the histogram band default to what every preset used before N1.
            public MeteringMode exposureMetering = MeteringMode.CenterWeighted;
            public float exposureMaskRadius = 0.3f;    // ProceduralMask: radius of the metered disc, a fraction of the screen
            public Vector2 exposureHistogram = new Vector2(40f, 90f);
            public float whiteBalanceTemperature, saturation, contrast, bloomIntensity, grainIntensity, vignetteIntensity; public string grainType;
            // Lift Gamma Gain's gamma (the luminance channel, w): above 0 it raises the dark tones. 0 adds no override (N1).
            public float shadowGamma;

            // W1 (spec 2.7): the wind of the preset. windBearing is the direction the wind blows toward, degrees clockwise from +Z;
            // between gusts W is windCalm, at a gust's crest windGustPeak (before the run-time wander); gust fronts windGustWavelength
            // metres apart travel downwind at windGustSpeed m/s. The run-time controller (I2) reads these fields.
            public float windBearing, windCalm, windGustPeak, windGustWavelength, windGustSpeed;

            // W1 I2b (owner's decision of 2026-10-17): the forest's cull distance under this preset, metres for the player's field of view
            // (ForestCullDistance, at least 40); 0 keeps the kit's own (trees and logs about 55 m at scale 1).
            public float forestCullDistance;

            /// <summary>
            /// The preset's wind as the shader reads it: seed 0.37, the shipped distances of spec 7.2 (fade 21 to 30 m, flutter
            /// within 10 m, moon cascades at rest), without the wander the run-time controller adds.
            /// </summary>
            public ForestWindState WindState() => new ForestWindState
            {
                gustBearing = windBearing, leanBearing = windBearing,
                calm = windCalm, peak = windGustPeak, gustWavelength = windGustWavelength, gustSpeed = windGustSpeed, gustSeed = WindSeed,
                fadeStart = 0.7f * WindFadeEnd, fadeEnd = WindFadeEnd, flutterEnd = WindFlutterEnd, moonCascades = 0,
                torchShadowEnd = 0f, timeOffset = 0f,
            };
        }

        /// <summary>W1 shipped wind constants (spec 7.2): the gust seed, the animation distance and the flutter distance.</summary>
        public const float WindSeed = 0.37f, WindFadeEnd = 30f, WindFlutterEnd = 10f;

        /// <summary>
        /// Null for an unknown name. Every call returns a new record, so a caller that changes a field
        /// cannot change the preset for anyone else.
        /// </summary>
        public static Preset Get(string name)
        {
            if (name == null || Array.IndexOf(Names, name) < 0)
                return null;

            switch (name)
            {
                case "OvercastMist":
                    return OvercastMist();
                case "Night":
                    return Night();
                default:
                    // Names and this switch are edited together; a name without values must not look like a missing sky.
                    throw new InvalidOperationException($"Lighting preset '{name}' is listed in Names but has no values.");
            }
        }

        /// <summary>Null for an unknown preset.</summary>
        public static string SkyId(string preset) => Get(preset)?.skyId;

        // Heavy overcast with mist. The sky outshines its sun three to one, which is the light scanned
        // assets look most photographic in, tolerates a missing probe bake and is kind to thinned foliage.
        static Preset OvercastMist() => new Preset
        {
            name = "OvercastMist",
            skyId = "overcast_soil_puresky",
            skyLux = 5000f,
            // The sky is nearly uniform, so nothing depends on where its brightest part ends up.
            skyRotation = 0f,

            keyLux = 1500f,
            keyTemperature = 6000f,
            // A sun behind cloud: wide enough that shadows stay faint and uniformly soft.
            keyAngularDiameter = 3f,
            keyVolumetricDimmer = 1f,
            keyElevation = 52f,
            // Travels south-east, so the sun stands in the north-west, ahead-left of the first view.
            keyBearing = 135f,

            // Knob 2 of spec section 10 (the spec's 80 m and four cascades, splits 0.08 / 0.22 / 0.50). With the young
            // firs of M3 in thickets the frame missed 11 ms at hollow_rim and deadfall and had no room left at
            // crouch_hide; the young firs' shadows were a third of the triangles there. Measured as an A/B on the same
            // scene (build/issues/M3-placement.md): 50 m and three cascades take 1.2 to 1.8 M triangles and 0.3 to 0.5
            // ms off every view. The near splits stay where they were in metres (6 and 17.5 m), so the shadows the
            // player sees up close keep their resolution; beyond 50 m the mist passes less than half the contrast.
            shadowDistance = 50f,
            cascades = 3,
            cascadeSplits = new Vector3(0.12f, 0.35f, 0.70f),

            // HDRP's height fog is uniform only below baseHeight. Above it the density falls as
            // exp(-(h - base) / H) with H = 0.1448 x (maximumHeight - baseHeight) (HD Fog.cs:207-212, 269-271;
            // Core VolumeRendering.hlsl:128-133). With the spec's 2 / 40 that is H = 5.5 m: 23 % of the floor's
            // density at Y 10 and 7 % at the bank's crest (Y 16 to 19), so the mist was a pale strip at the foot of
            // the bank and the crest stood sharp against the sky in every outward view (review 2 of the M2
            // captures). The base now lies above the bank's crest, so the whole bowl is one even layer and the
            // arithmetic of spec 8.2 holds for everything below Y 18; above it the mist thins with H = 11.9 m
            // through where the fir crowns will stand (35 % at Y 30, 10 % at Y 45).
            // Contrast left at distance d below Y 18 is exp(-d / 70): 67 % at 28 m (the raptor's sight range),
            // 42 % at 60 m, 18 % at 120 m (the bank's crest from mid-valley), 16 % at 130 m. Round 1 had cut the
            // mean free path to 60 m to fog the bank; that thickened the floor layer, not the bank, and is taken
            // back half way now that the bank is inside the layer.
            //
            // D2 (density pass, build/issues/D2-density.md; the owner: a dinosaur must appear suddenly between the trees): the mist is
            // much shorter. Contrast left is exp(-d / 20): 47 % at 15 m and 37 % at 20 m (a 2 m silhouette reads), 13 % at 40 m and 8 %
            // at 50 m (it is gone), where every class is now culled (ForestKitSpec). The volumetric range ends at the tall firs' cull.
            fogMeanFreePath = 20f,
            fogBaseHeight = 18f,
            fogMaximumHeight = 100f,
            fogAnisotropy = 0.6f,
            fogDepthExtent = 55f,
            fogAlbedo = new Color(0.92f, 0.93f, 0.95f),
            fogHighQuality = false,

            // Open ground gets 5,000 + 1,500 x sin 52 = 6,200 lux, neutral at 11.0 EV. The clamp lets the
            // eye adapt under the canopy, can push the frame neither to black nor to white, and keeps the
            // clearing visibly brighter than the forest.
            fixedExposure = false,
            exposureMin = 7.5f,
            exposureMax = 10.5f,
            // On open ground the meter asks for 11.0 EV and the clamp holds it at limitMax, so compensation does not
            // move these frames (tried at -0.15 in review 2: trail_start 0.374 before, 0.372 after).
            exposureCompensation = -0.3f,
            // Read only if fixedExposure is switched on, the last fallback for a wrong sky intensity.
            // Kept between the two limits so that switch alone cannot produce a white frame.
            exposureFixed = 9.5f,

            // The sky HDRI is slightly warm and the fog takes its colour from it, so at 0 the whole frame had a
            // sepia cast and distance read as faded brown, not as cool air (review 2: trail_start channel means
            // 0.421 / 0.405 / 0.366). A small cool shift; ferns and litter keep their hue.
            whiteBalanceTemperature = -5f,
            saturation = -6f,
            contrast = 6f,
            bloomIntensity = 0.12f,
            grainType = "Thin1",
            grainIntensity = 0.10f,
            vignetteIntensity = 0.20f,

            // W1 (spec 2.7): a windier preset than the night, without a lull target.
            windBearing = 70f,
            windCalm = 0.10f,
            windGustPeak = 0.70f,
            windGustWavelength = 150f,
            windGustSpeed = 5.0f,
        };

        // N1 (build/issues/N1-night.md): a moonlit night in mist, the main mood of the game. The player carries a torch
        // (PlayerFlashlight: 300 lm, ForestFlashlight), and the night is built around it. Without the torch the trunks stand black
        // against mist that glows faintly with the moon, and the trail and the floor are barely readable for the first ten metres;
        // with it, the beam lights what it falls on and carries 20 to 30 m before the mist takes it. Starting values: spec
        // Appendix C.1 (written for the sparse forest with long mist); every change below was judged on renders.
        static Preset Night() => new Preset
        {
            name = "Night",
            // Moonless, so the moon's light is not counted twice; the key light is the moon.
            skyId = "qwantani_night_puresky",
            // The spec's 0.06 lux left the floor under the stand black (no probe bake: the sky's light is the only light there).
            // 0.1 lux makes the trail's ruts and the ferns barely readable for the first metres without the torch.
            skyLux = 0.1f,
            skyRotation = 0f,

            // A moon of about full-moon brightness, slightly warm in itself (the cold look is the grading's).
            keyLux = 0.3f,
            keyTemperature = 4100f,
            keyAngularDiameter = 0.52f,
            // The moon lights the mist twice as strongly as physics would: the mist's glow is what the trunks stand against.
            keyVolumetricDimmer = 2f,
            keyElevation = 50f,
            keyBearing = 135f,

            // W1 I2c (the owner's answer of 2026-10-17 to issues/W1-runtime.md 11.12, spec W1 7.4 step 5 option (a)): the moon's shadows
            // end at 30 m with 2 cascades, to fund the wind (N1 had 50 m and 3 cascades, as OvercastMist, M3 knob 2). The first split stays
            // at 6 m (0.2 x 30), where N1's was, so the shadows the player sees up close keep their resolution; the second cascade covers
            // 6 to 30 m (N1: 6 to 17.5 and 17.5 to 50 m). HDRP fades the last cascade out over its last 20 %, so there is no line where they
            // end. The third split is read only with 3 or more cascades; it keeps N1's 17.5 m. Night only: OvercastMist keeps its shadows.
            // Before and after: captures/w1-moon-before, w1-moon-after (issues/W1-runtime.md section 12).
            shadowDistance = 30f,
            cascades = 2,
            cascadeSplits = new Vector3(0.20f, 17.5f / 30f, 0.90f),

            // Longer than the day's 20 m, so the beam has 20 to 30 m to work with: a grey card (albedo 0.25) in the hot spot
            // reads 0.3 to 0.5 at 20 to 30 m, against 0.03 to 0.08 without the torch (section 3). At 50 to 55 m, where every
            // class is culled, the mist leaves exp(-50 / 30) = 19 % of a silhouette's contrast against a faint glow.
            // Forward scattering as the day's: at 0.2 the light in the mist right in front of the torch hid what it lit.
            fogMeanFreePath = 30f,
            fogBaseHeight = 18f,
            fogMaximumHeight = 100f,
            fogAnisotropy = 0.6f,
            fogDepthExtent = 55f,
            fogAlbedo = new Color(0.88f, 0.92f, 1.0f),
            fogHighQuality = false,

            // Automatic, like the eye. Without the torch the meter asks for far less than the lower limit and the limit holds the
            // frame there: -1.2 EV, with the shadow gamma below, is the torchless look (open ground's 0.33 lux is neutral at
            // -3.3 EV; the floor under the stand gets a fraction of that). With the torch the meter reads a wide disc in the middle
            // of the screen, where the beam's hot spot falls, and exposes for what the beam lights: a fir at arm's length keeps its
            // needles instead of going white, and a far hot spot is brought up. Switching the torch off leaves the player blind for
            // a moment while the exposure falls back (HDRP's default adaptation, 1 EV a second).
            fixedExposure = false,
            exposureMin = -1.2f,
            exposureMax = 10f,
            exposureCompensation = 0.5f,
            exposureFixed = -1f,
            exposureMetering = MeteringMode.ProceduralMask,
            exposureMaskRadius = 0.45f,
            exposureHistogram = new Vector2(40f, 98f),
            // A torch-lit fir at 1.5 m is some twelve stops brighter than the moonlit forest around it. Exposed for the fir, ACES's
            // toe crushed everything outside the beam to black (57 to 82 % of the frame below 2/255 in eight of the 32 views, which
            // the capture's black-frame check rejects, rightly: the forest around the light must still be there). A gamma of +0.25
            // on the dark tones keeps the trunks and the mist around the beam faintly visible (crushed 0 to 38 % of the frame, the
            // most at rock_close; +0.2 left that view at 49.6 %, a hair under the limit) and leaves the lit part as it was; without
            // the torch it is part of the night's look, and the lower exposure limit was raised with it (section 3).
            shadowGamma = 0.25f,

            whiteBalanceTemperature = -22f,
            saturation = -30f,
            contrast = 10f,
            bloomIntensity = 0.15f,
            grainType = "Medium1",
            grainIntensity = 0.30f,
            vignetteIntensity = 0.35f,

            // W1 (spec 2.7): misty nights are calm, and the scary beat is a gust rolling through a still stand. W rests at 0.03 and
            // rises to 0.55 in gusts every 24 to 68 s (median 36 s, a crest every 34 s at a point), still 64 % of the time
            // (B/probes/W1/rev/gust_tune.py, final_check.py; pinned by WindGlobals_GustHasLullsAndTravellingFronts).
            windBearing = 70f,
            windCalm = 0.03f,
            windGustPeak = 0.55f,
            windGustWavelength = 120f,
            windGustSpeed = 3.5f,

            // W1 I2b (owner's decision of 2026-10-17, issues/W1-runtime.md section 11): at night only the forest could end nearer than the
            // kit's 50 to 55 m (ForestCullDistance, not under 40 m), if the night captures with the torch on and off showed no difference
            // beyond run-to-run noise. At 45 m they did not (torch on 17, off 25 of 32 views over the noise; the far treeline thins against
            // the moonlit mist: captures/w1-cull45-on, -off), so the night keeps the kit's distances until the owner decides.
            forestCullDistance = NightCullDistance,
        };

        /// <summary>W1 I2b: the night preset's forest cull distance (m); 0 = the kit's own (45 m measured, visible, not shipped).</summary>
        public const float NightCullDistance = 0f;
    }
}
