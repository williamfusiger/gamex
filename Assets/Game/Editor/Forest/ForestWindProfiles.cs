using System;
using UnityEngine;

namespace Gamex.EditorTools
{
    /// <summary>
    /// The per-model wind values of W1 spec section 2.6. A material moves if its manifest model has a profile; every other
    /// material (logs, stumps, rocks, dry branches, terrain, the root skirt's ground variant) stays on HDRP/Lit, static.
    /// Amplitudes are object-space metres at effective wind W = 1; push amounts are world metres; periods are at instance
    /// scale 1 and scale as scale^periodExponent.
    /// </summary>
    public static class ForestWindProfiles
    {
        public sealed class Profile
        {
            public string modelId;
            public float trunkAmplitude, trunkPeriod, branchAmplitude, branchPeriod;     // periods at instance scale 1
            public float flutterAmplitude, flutterFrequency;                              // 0 for both conifers
            public float periodExponent, shelter, rippleWavelength, phaseSpread;          // rippleWavelength 0 = none
            // W1 R2: for ferns and grass (anchored: each frond or blade turns about its own base, UV2), pushAmount is the tilt away from
            // the body and flattenShare the pitch down about the frond's own axis, both in radians at full press.
            public float pushAmount, flattenShare, pushGain;
            public bool passableStems; public float stemLean;                             // young firs only; radians at full press
            public float stemCrown;                                                       // W1 I2b: lower crown radius at scale 1 (m), where the lean feels a body
            // W1 I2c (owner's decision of 2026-10-17, option (a)): a fir's reach in the push band at scale 1 (m), where its solid instances
            // look for the bodies pressing them (GamexSolidSide); every pushed vertex of a solid fir then goes toward its own trunk's side.
            // 0 = none (ferns and grass keep each vertex on its own side of the body's axis).
            public float solidSideReach;
            // W1 R1 (review of the I2c captures): a solid fir's pushed boughs swing about its own trunk's axis by up to this angle at full
            // press (radians), in the body's travel direction, instead of I2c's 0.55 m sideways translation, which slid boughs through
            // their own trunk and squeezed the crossing ones into a flat mat. 0 = no swing.
            public float solidSwing;
            // W1 R1: fronds and grass blades shiver while a body moves through them: a signature wind does not have, for a player who
            // watches the ferns for a hidden dinosaur. 0 = none. W1 R2: a sway of the whole frond about its base, in radians and Hz (R1's was
            // a 7 Hz B-weighted shiver along the normal of 2.5 cm, 2 px at 12 m).
            public float shiverAmplitude, shiverFrequency;

            /// <summary>_GamexSway: trunk amplitude (m), trunk period (s), branch amplitude (m), branch period (s).</summary>
            public Vector4 Sway => new Vector4(trunkAmplitude, trunkPeriod, branchAmplitude, branchPeriod);
            /// <summary>_GamexFlutter: flutter amplitude (m), flutter frequency (Hz), W1 R1: the contact shiver's amplitude (m) and frequency (Hz).</summary>
            public Vector4 Flutter => new Vector4(flutterAmplitude, flutterFrequency, shiverAmplitude, shiverFrequency);
            /// <summary>_GamexMotion: period size exponent, shelter, 1 / ripple wavelength (0 = none), instance phase spread.</summary>
            public Vector4 Motion => new Vector4(periodExponent, shelter, rippleWavelength > 0f ? 1f / rippleWavelength : 0f, phaseSpread);
            /// <summary>_GamexPush: branch push (m), flatten share, push gain, W1 R1: a solid fir's swing at full press (radians).</summary>
            public Vector4 Push => new Vector4(pushAmount, flattenShare, pushGain, solidSwing);
            /// <summary>_GamexStem: passable scale (0 = none), the largest stem lean (radians; W1 R1: the spring's target for a body walking into
            /// the stem never exceeds it), lower crown radius at scale 1 (m; W1 I2b),
            /// a solid instance's reach for its side (m at scale 1; W1 I2c; 0 = none).</summary>
            public Vector4 Stem => new Vector4(passableStems ? ForestLayout.PassableYoungFirMaxScale : 0f, passableStems ? stemLean : 0f, passableStems ? stemCrown : 0f, solidSideReach);
        }

        public static readonly string[] Models = { "fir_sapling_medium", "fir_tree_01", "fern_02", "grass_medium_01" };

        static readonly Profile[] Table =
        {
            new Profile
            {
                modelId = "fir_sapling_medium",
                trunkAmplitude = 0.30f, trunkPeriod = 2.2f, branchAmplitude = 0.10f, branchPeriod = 1.25f,
                flutterAmplitude = 0f, flutterFrequency = 0f,
                periodExponent = 0.5f, shelter = 1f, rippleWavelength = 0f, phaseSpread = 1f,
                pushAmount = 0.55f, pushGain = 5f, flattenShare = 0.15f,
                // W1 I2b: 50 -> 80 degrees (1.396 rad) at full press, within the spec's tuning range (half to twice; push amounts only
                // up). With every vertex of a leaning sapling pushed the lean's way (the owner's tearing fix), a branch lying across the
                // body's axis must be carried across by the lean itself, or the push takes it into the body: at 50 degrees a branch
                // 1.4 m out at 0.44 m ended on the axis (Foliage_ProxyBendsThicketBranchesNotTrunks, issues/W1-runtime.md section 11).
                // W1 I2b: the lean feels a body pressing the lower crown (rings at 0.3, 0.6 and 0.9 of this radius times the scale), not only
                // the stem: LOD1's branch vertices between 0.3 and 2.0 m reach 2.96 m (a), 2.41 m (b) and 2.42 m (c) at scale 1 (I2's
                // LowerCrownRadius); 2.4 m is b and c's, the variants of 689 of the 690 passable saplings.
                // W1 R1 (review of the I2c captures): 1.396 -> 1.25 rad. The lean is now a spring the Instances kernel drives from the body's
                // geometry: about 0.2 rad for a body brushing the crown, and for a body that walks into the stem the angle that puts the stem
                // at 0.7 m beside the body's surface (about 66 degrees for the raptor proxy on the stem's line, 47 degrees for the player),
                // bent from the base; this is only its ceiling (B/probes/W1/R1/lean_sim2.py).
                passableStems = true, stemLean = 1.25f, stemCrown = 2.4f,
                // W1 I2c: foliage between 0.3 and 2.6 m reaches 2.96 m (a), 2.41 m (b) and 2.42 m (c) from the stem at scale 1 (scan level and
                // LOD1; B/probes/W1/I2c/reach.py on the kit's meshes).
                solidSideReach = 3.0f,
                // W1 R1: 0.5 rad (29 degrees) at full press: a bough 1.4 m out moves about 0.7 m with the body, keeps its length and its root,
                // and never reaches its own trunk. W1 R2: 0.30 rad. Only the boughs facing the body turn now (max(0, cos)^2), whose slope
                // around the trunk (at most 1 per radian) times the swing is how much a needle across it is stretched or squeezed: 0.7 to
                // 1.3 x (0.45 rad squeezed needles to 0.55 x, w1-r2-try2), within the 0.67 to 1.5 x the shape check allows.
                solidSwing = 0.30f,
            },
            new Profile
            {
                modelId = "fir_tree_01",
                trunkAmplitude = 0.45f, trunkPeriod = 4.5f, branchAmplitude = 0.15f, branchPeriod = 2.4f,
                flutterAmplitude = 0f, flutterFrequency = 0f,
                periodExponent = 0.5f, shelter = 1f, rippleWavelength = 0f, phaseSpread = 1f,
                pushAmount = 0.55f, pushGain = 5f, flattenShare = 0.10f,
                passableStems = false, stemLean = 0f,
                // W1 I2c: the tall firs' foliage below 4.5 m (2.05 m, the highest push band, at their smallest scale 0.62 is 3.3 m) stays
                // within 1.02 m (a), 1.50 m (b) and 0.77 m (c) of the trunk at scale 1 (scan level; B/probes/W1/I2c/reach.py).
                solidSideReach = 1.5f,
                solidSwing = 0.30f,   // W1 R2: as the young firs
            },
            new Profile
            {
                modelId = "fern_02",
                // W1 R2: branch amplitude 0.12 -> 0.09: each frond now nods by itself at full strength (R1's activity averaged 0.75), and the
                // hidden body must still stand out from a gust's crest (the stalk captures at 12 m).
                trunkAmplitude = 0f, trunkPeriod = 0f, branchAmplitude = 0.09f, branchPeriod = 1.1f,
                flutterAmplitude = 0.020f, flutterFrequency = 3.5f,
                // W1 R1 (review of the I2c captures): shelter 0.45 -> 0.30. Under the dense canopy the night wind's crest moved twice as many
                // pixels of the fern site's band as a raptor's pass did, so a body moving through the ferns could not be told from a gust.
                // W1 R2 (review of the R1 captures): fronds of a patch nodded as one sheet (r 0.86 to 0.96 between fronds 1 m apart): each
                // frond now nods at its own phase (4 A: a whole cycle across clumps, half of one within a clump) and frequency (+-25 %), with
                // the instance spread 0.25 -> 1.0 and no ripple (6 m -> none: with every frond's own phase it only added a travelling sheet).
                periodExponent = 0f, shelter = 0.30f, rippleWavelength = 0f, phaseSpread = 1.0f,
                // W1 R2: a frond turns rigidly about its base: tilted away from the body by up to 0.9 rad and pitched down by up to 0.7 rad
                // (R1: a 0.45 m translation and a 70 % squash, which folded fronds near a body and split those across its axis).
                pushAmount = 0.9f, pushGain = 2f, flattenShare = 0.7f,
                passableStems = false, stemLean = 0f,
                // W1 R2: a 0.12 rad sway at 2.5 Hz of the whole frond (7 cm at a 0.6 m frond's tip) while the press at its base changes.
                shiverAmplitude = 0.12f, shiverFrequency = 2.5f,
            },
            new Profile
            {
                modelId = "grass_medium_01",
                trunkAmplitude = 0f, trunkPeriod = 0f, branchAmplitude = 0.08f, branchPeriod = 0.9f,
                flutterAmplitude = 0.015f, flutterFrequency = 4.0f,
                periodExponent = 0f, shelter = 0.30f, rippleWavelength = 0f, phaseSpread = 1.0f,   // W1 R1: 0.45 -> 0.30, as ferns; W1 R2: as ferns
                pushAmount = 0.60f, pushGain = 2f, flattenShare = 0.60f,   // W1 R2: radians, as ferns
                passableStems = false, stemLean = 0f,
                shiverAmplitude = 0.10f, shiverFrequency = 3f,
            },
        };

        /// <summary>The profile of a manifest model, or null for a static model.</summary>
        public static Profile ForModel(string modelId)
        {
            foreach (Profile profile in Table)
                if (profile.modelId == modelId) return profile;
            return null;
        }

        /// <summary>
        /// The profile of a manifest material (sameAs is followed), or null for a static material. A kit material name with its
        /// variant suffixes (__tint1, __triplanar, __crowncore, __mv) resolves to its manifest material; __ground never moves.
        /// </summary>
        public static Profile ForMaterial(string materialName)
        {
            if (string.IsNullOrEmpty(materialName)) return null;
            string name = materialName;
            if (name.EndsWith("__mv", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 4);
            if (name.EndsWith("__ground", StringComparison.Ordinal)) return null;
            foreach (string suffix in new[] { "__tint1", "__triplanar", "__crowncore" })
                if (name.EndsWith(suffix, StringComparison.Ordinal)) name = name.Substring(0, name.Length - suffix.Length);
            ForestMaterialEntry entry = ForestManifest.ResolveMaterial(name);
            if (entry == null) return null;
            ForestAssetEntry owner = ForestManifest.FindMaterialOwner(entry.name);
            return owner == null ? null : ForModel(owner.id);
        }

        /// <summary>Every profile value as text, for material stamps and the kit hash.</summary>
        public static string Describe(Profile profile)
        {
            if (profile == null) return "static";
            return string.Join(",", Number(profile.trunkAmplitude), Number(profile.trunkPeriod), Number(profile.branchAmplitude), Number(profile.branchPeriod),
                Number(profile.flutterAmplitude), Number(profile.flutterFrequency), Number(profile.periodExponent), Number(profile.shelter),
                Number(profile.rippleWavelength), Number(profile.phaseSpread), Number(profile.pushAmount), Number(profile.flattenShare),
                Number(profile.pushGain), profile.passableStems ? "passable" : "solid", Number(profile.stemLean), Number(profile.stemCrown), Number(ForestLayout.PassableYoungFirMaxScale),
                Number(profile.solidSideReach), Number(profile.solidSwing), Number(profile.shiverAmplitude), Number(profile.shiverFrequency));
        }

        static string Number(float value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }
}
