using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gamex
{
    /// <summary>
    /// The run-time knobs of the wind (W1 spec 6.2, 7.2 and 7.4). <see cref="Shipped"/> is the starting configuration of 7.2; the
    /// harness and the tests set others through <see cref="ForestWind.Knobs"/>, never saved.
    /// </summary>
    [Serializable]
    public struct ForestWindKnobs
    {
        public float fadeEnd;          // m; fadeStart = 0.7 x fadeEnd; at most 32 m when gustTexture is on (the texture's half window)
        public float flutterEnd;       // m; never above 0.7 x fadeEnd (ferns and grass only)
        public int moonCascades;       // 0 rest pose, 1 sway only, 2 full
        public float strengthScale;    // 1 shipped; scales calm and peak; 0 = the "off" state
        public bool fieldEnabled;
        public bool gustTexture;       // knob 2: e from FoliageInteraction.compute's Gust kernel instead of per vertex
        public float torchShadowEnd;   // knob 4: m; 0 = the torch's shadow map animated to fadeEnd
        public bool solidSideOff;      // W1 I2c, tests only: solid firs push each vertex to its own side (I2b's tearing push); false as shipped

        /// <summary>30 m, 10 m, moon at rest, strength 1, field on, analytic gust, torch shadows to the fade end.</summary>
        public static readonly ForestWindKnobs Shipped = new ForestWindKnobs
        {
            fadeEnd = 30f, flutterEnd = 10f, moonCascades = 0, strengthScale = 1f, fieldEnabled = true, gustTexture = false, torchShadowEnd = 0f,
        };

        public override string ToString() =>
            $"fade {fadeEnd:0.#} m, flutter {flutterEnd:0.#} m, moon {moonCascades}, strength {strengthScale:0.##}, field {(fieldEnabled ? "on" : "off")}, " +
            $"gust {(gustTexture ? "texture" : "analytic")}, torch shadows {(torchShadowEnd > 0f ? torchShadowEnd.ToString("0.#") + " m" : "to the fade end")}" +
            (solidSideOff ? ", solid firs each vertex to its own side (I2b's push, a test control)" : "");
    }

    /// <summary>Which wind materials draw the motion vector pass: as built (the "__mv" twins only), every one, or none.</summary>
    public enum WindMotionVectors { Shipped, All, None }

    /// <summary>
    /// The forest's wind (W1 spec 2.7, 6.2). One per lighting preset object (Lighting/Preset_&lt;name&gt;), with that preset's values;
    /// only the active preset's is enabled. Not [ExecuteAlways]: in edit mode every global stays zero and the forest is still.
    /// Each frame it adds the slow wander of the lean direction and of the gust peak to the preset's values (two incommensurate
    /// sines of Time.time each: deterministic, smooth, no integration), applies the knobs, and writes the globals through
    /// <see cref="ForestWindGlobals"/>. The gust frame itself never wanders (2.7). Everything that moves within a frame is computed
    /// in the shader from its Time input, so the motion vector pass reproduces the previous frame exactly.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class ForestWind : MonoBehaviour
    {
        // Ratio of the two sines of each wander: the golden ratio, so the sum never repeats.
        const float WanderRatio = 1.618034f;
        const string MotionVectorPass = "MotionVectors";
        static readonly string[] WindShaders = { "Gamex/ForestWind", "Gamex/ForestWindTriplanar" };

        [SerializeField] float bearing = 70f, calm = 0.03f, gustPeak = 0.55f, gustWavelength = 120f, gustSpeed = 3.5f, gustSeed = 0.37f;
        [SerializeField] float leanWander = 15f, leanWanderPeriod = 90f, peakWander = 0.25f, peakWanderPeriod = 40f;   // the gust frame never wanders (2.7)
        [SerializeField] ForestWindKnobs knobs = ForestWindKnobs.Shipped;

        static ForestWind active;
        // The pass state of every wind material as it was before SetMotionVectorPass first changed it.
        static readonly Dictionary<Material, bool> recordedPasses = new Dictionary<Material, bool>();

        ForestWindState current;
        bool hasOverride;
        ForestWindKnobs overrideKnobs;
        float timeOffset, wanderTimeOffset;
        Vector2? strengthOverride;
        int generation;

        /// <summary>The enabled controller, or null.</summary>
        public static ForestWind Active => active;

        /// <summary>What was written this frame.</summary>
        public ForestWindState Current => current;

        /// <summary>Run-time overrides (harness, tests); never saved. Reading returns the serialized knobs until one is set.</summary>
        public ForestWindKnobs Knobs
        {
            get => hasOverride ? overrideKnobs : knobs;
            set { overrideKnobs = value; hasOverride = true; generation++; }
        }

        /// <summary>Back to the serialized knobs.</summary>
        public void ResetKnobs()
        {
            if (hasOverride) generation++;
            hasOverride = false;
        }

        /// <summary>
        /// W1 R2: counts every change of the knobs, the overrides and the time offsets (the harness's and the tests' jumps; never in the
        /// game). The field restarts the wind's dynamics from the new wind when it changes, so a jump does not ring through the forest.
        /// </summary>
        public int Generation => generation;

        /// <summary>
        /// I2 addition (tests and captures only): seconds added to the shader's time input (_GamexWindParams.x), so a sequence can
        /// put a gust crest where it looks. 0 in the game.
        /// </summary>
        public float TimeOffset
        {
            get => timeOffset;
            set { if (value != timeOffset) generation++; timeOffset = value; }
        }

        /// <summary>
        /// I2 addition (tests and captures only): (calm, peak) in place of the preset's, without the peak wander; null = the preset.
        /// The play-mode states "steady" (0.35, 0.35) and "gusty" (0.2, 0.9) of spec 8.2.
        /// </summary>
        public Vector2? StrengthOverride
        {
            get => strengthOverride;
            set { if (value != strengthOverride) generation++; strengthOverride = value; }
        }

        /// <summary>
        /// W1 R1 (tests and captures only): seconds added to Time.time for the slow wander of the lean direction and of the gust peak. Two
        /// runs that must see the same wind (a walk and its wind-only twin) set it so the wander starts from the same moment. 0 in the game.
        /// </summary>
        public float WanderTimeOffset
        {
            get => wanderTimeOffset;
            set { if (value != wanderTimeOffset) generation++; wanderTimeOffset = value; }
        }

        /// <summary>The preset's values as serialized: bearing, calm, gust peak, wavelength, speed, seed.</summary>
        public float Bearing => bearing;
        public float Calm => calm;
        public float GustPeak => gustPeak;
        public float GustWavelength => gustWavelength;
        public float GustSpeed => gustSpeed;
        public float GustSeed => gustSeed;

        // Play mode is entered without a domain reload in this project: every static is reset here.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            active = null;
            recordedPasses.Clear();
        }

        void OnEnable()
        {
            if (active != null && active != this)
                Debug.LogWarning($"ForestWind: {name} is enabled while {active.name} is; the last one enabled writes the wind.");
            active = this;
            Write();
        }

        void OnDisable()
        {
            if (active == this)
            {
                active = null;
                ForestWindGlobals.ClearWind();
            }
        }

        void LateUpdate()
        {
            if (active == this)
                Write();
        }

        /// <summary>
        /// W1 R1 (tests): writes this frame's wind now. A frame rendered from a test coroutine runs before this frame's LateUpdate, so it
        /// would show last frame's globals (with <see cref="FoliageInteractionField.RefreshInstanceTerms"/>, this frame's pose).
        /// </summary>
        public void WriteNow()
        {
            if (active == this) Write();
        }

        void Write()
        {
            current = StateAt(Time.time);
            ForestWindGlobals.Write(current);
        }

        /// <summary>The state this controller writes at a given Time.time, knobs and overrides applied.</summary>
        public ForestWindState StateAt(float time)
        {
            ForestWindKnobs k = Knobs;
            float wanderTime = time + WanderTimeOffset;
            float lean = bearing + leanWander * Wander(wanderTime, leanWanderPeriod, 0.3, 1.1);
            float calmNow = calm, peakNow = gustPeak * (1f + peakWander * Wander(wanderTime, peakWanderPeriod, 0.7, 2.1));
            if (StrengthOverride is Vector2 strength)
            {
                calmNow = strength.x;
                peakNow = strength.y;
            }
            float scale = Mathf.Max(0f, k.strengthScale);
            float fadeEnd = Mathf.Max(0f, k.gustTexture ? Mathf.Min(k.fadeEnd, FoliageInteractionField.GustHalfWindow) : k.fadeEnd);
            float fadeStart = 0.7f * fadeEnd;
            return new ForestWindState
            {
                gustBearing = bearing, leanBearing = lean,
                calm = calmNow * scale, peak = peakNow * scale,
                gustWavelength = gustWavelength, gustSpeed = gustSpeed, gustSeed = gustSeed,
                fadeStart = fadeStart, fadeEnd = fadeEnd, flutterEnd = Mathf.Clamp(k.flutterEnd, 0f, fadeStart),
                moonCascades = Mathf.Clamp(k.moonCascades, 0, 2),
                torchShadowEnd = Mathf.Max(0f, k.torchShadowEnd),
                timeOffset = TimeOffset,
                solidSideOff = k.solidSideOff,
            };
        }

        // In [-1, 1]: 0.6 and 0.4 of two sines whose periods are P and 1.618 P. The lean's (15 degrees, 90 s) turns at most
        // 0.2618 x 2 pi x (0.6 / 90 + 0.4 / 145.6) = 0.0155 rad/s, under the spec's 0.018.
        static float Wander(float time, float period, double phaseA, double phaseB)
        {
            if (period <= 0f) return 0f;
            double t = time;
            return (float)(0.6 * Math.Sin(2.0 * Math.PI * t / period + phaseA) + 0.4 * Math.Sin(2.0 * Math.PI * t / (WanderRatio * period) + phaseB));
        }

        /// <summary>e now at a point, 0..1, the shader's GamexGust.</summary>
        public float SampleGust(Vector3 position) =>
            ForestWindGlobals.SampleGust(current, new Vector2(position.x, position.z), Time.time + current.timeOffset);

        /// <summary>W at a point now: calm + (peak - calm) e, before the distance fade and the shelter.</summary>
        public float SampleStrength(Vector3 position) =>
            ForestWindGlobals.SampleStrength(current, new Vector2(position.x, position.z), Time.time + current.timeOffset);

        /// <summary>
        /// The motion vector pass on every loaded wind material: on for all, off for all, or back to the states recorded before the
        /// first change (Shipped: the "__mv" twins on, the plain variants off). Callers that change it restore Shipped in finally:
        /// in the Editor the materials are assets.
        /// </summary>
        public static void SetMotionVectorPass(WindMotionVectors mode)
        {
            if (mode == WindMotionVectors.Shipped)
            {
                foreach (KeyValuePair<Material, bool> entry in recordedPasses)
                    if (entry.Key != null && entry.Key.GetShaderPassEnabled(MotionVectorPass) != entry.Value)
                        entry.Key.SetShaderPassEnabled(MotionVectorPass, entry.Value);
                recordedPasses.Clear();
                return;
            }
            foreach (Material material in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (!IsWindMaterial(material))
                    continue;
                bool enabled = material.GetShaderPassEnabled(MotionVectorPass);
                if (!recordedPasses.ContainsKey(material))
                    recordedPasses.Add(material, enabled);
                bool wanted = mode == WindMotionVectors.All;
                if (enabled != wanted)
                    material.SetShaderPassEnabled(MotionVectorPass, wanted);
            }
        }

        /// <summary>A material on one of the two wind Shader Graphs.</summary>
        public static bool IsWindMaterial(Material material) =>
            material != null && material.shader != null && Array.IndexOf(WindShaders, material.shader.name) >= 0;
    }
}
