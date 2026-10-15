using System;
using UnityEngine;

namespace Gamex.EditorTools
{
    /// <summary>
    /// The numbers behind each lighting preset of the forest scene. They are starting values: none has
    /// been judged in a render yet. <see cref="ForestLighting"/> turns a preset into a volume profile and
    /// a key light; the audit and the capture viewpoints read the same record, so a retuned value cannot
    /// drift away from what is checked.
    /// </summary>
    public static class ForestLightingPresets
    {
        public const string Default = "OvercastMist";
        public static readonly string[] Names = { "OvercastMist" };   // M5 appends the presets of Appendix C

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
            public float whiteBalanceTemperature, saturation, contrast, bloomIntensity, grainIntensity, vignetteIntensity; public string grainType;
        }

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
        };
    }
}
