using System;
using UnityEngine;

namespace Gamex
{
    /// <summary>
    /// The wind of the forest as the shader reads it (W1 spec sections 2.7, 2.8 and 3.4). The gust frame (gustBearing) is fixed;
    /// only the lean direction and the peak wander, which the run-time controller adds.
    /// </summary>
    [Serializable]
    public struct ForestWindState
    {
        public float gustBearing, leanBearing;                                            // degrees toward which the wind blows, clockwise from +Z
        public float calm, peak, gustWavelength, gustSpeed, gustSeed;                     // peak includes the wander
        public float fadeStart, fadeEnd, flutterEnd; public int moonCascades;             // metres; 0 rest pose, 1 sway only, 2 full
        public float torchShadowEnd;                                                      // 0 = fadeEnd (knob 4)
        public float timeOffset;                                                          // captures only
        // W1 I2c, tests only: solid firs push each vertex to its own side of a body's axis (I2b's push, which tore branches lying across
        // the axis), the positive control of the solid-fir tearing check. False as shipped.
        public bool solidSideOff;
    }

    /// <summary>
    /// The only writer of the wind and interaction globals of ForestWind.hlsl (spec section 3.4). Every global is zero when
    /// nothing writes it, and all zero is the rest pose: the greybox, edit mode and every scene without a controller render
    /// foliage still. Also the CPU mirror of the shader's gust function.
    /// </summary>
    public static class ForestWindGlobals
    {
        public static readonly int Wind = Shader.PropertyToID("_GamexWind");
        public static readonly int Gust = Shader.PropertyToID("_GamexWindGust");
        public static readonly int Params = Shader.PropertyToID("_GamexWindParams");
        public static readonly int Fade = Shader.PropertyToID("_GamexWindFade");
        public static readonly int GustTex = Shader.PropertyToID("_GamexGustTex");
        public static readonly int GustRect = Shader.PropertyToID("_GamexGustRect");
        public static readonly int InteractionTex = Shader.PropertyToID("_GamexInteractionTex");
        public static readonly int InteractionTexPrev = Shader.PropertyToID("_GamexInteractionTexPrev");
        public static readonly int InteractionRect = Shader.PropertyToID("_GamexInteractionRect");
        public static readonly int InteractionRectPrev = Shader.PropertyToID("_GamexInteractionRectPrev");
        /// <summary>
        /// I2 addition: _GamexInteractionBox, world XZ (minX, minZ, maxX, maxZ) of everything the field has stamped in the last 4 s; the
        /// vertex shader samples the field only for instances whose pivot lies within 4 m of it. All zero = no box (sample everywhere, as
        /// before); min above max = nothing stamped (sample nowhere).
        /// </summary>
        public static readonly int InteractionBox = Shader.PropertyToID("_GamexInteractionBox");
        /// <summary>W1 R1: _GamexInteractionParams, x = 1 / the field's last step (1/s; 0 while paused): how fast a press changes.</summary>
        public static readonly int InteractionParams = Shader.PropertyToID("_GamexInteractionParams");
        /// <summary>
        /// I2b additions: the per-instance wind terms of every registered renderer at this frame's time and at last frame's
        /// (<see cref="ForestWindInstances"/>), and _GamexInstanceParams (x = registered slots; 0 = every instance computes its own).
        /// </summary>
        public static readonly int InstanceTex = Shader.PropertyToID("_GamexInstanceTex");
        public static readonly int InstanceTexPrev = Shader.PropertyToID("_GamexInstanceTexPrev");
        public static readonly int InstanceParams = Shader.PropertyToID("_GamexInstanceParams");

        /// <summary>The smoothstep of GamexGust: the shaped gust e rises from 0 at g = GustLow to 1 at g = GustHigh.</summary>
        public const float GustLow = 0.62f, GustHigh = 0.92f;

        const double Tau = 2.0 * Math.PI;

        /// <summary>Writes the wind globals (_GamexWind, _GamexWindGust, _GamexWindParams, _GamexWindFade).</summary>
        public static void Write(in ForestWindState state)
        {
            Shader.SetGlobalVector(Wind, WindVector(state));
            Shader.SetGlobalVector(Gust, GustVector(state));
            Shader.SetGlobalVector(Params, ParamsVector(state));
            Shader.SetGlobalVector(Fade, new Vector4(state.fadeStart, state.fadeEnd, state.flutterEnd, state.moonCascades));
        }

        /// <summary>
        /// I2b: the same wind vectors on a compute shader (spec 3.1: every value a kernel reads is set on the compute shader itself).
        /// </summary>
        public static void WriteTo(ComputeShader shader, in ForestWindState state)
        {
            shader.SetVector(Wind, WindVector(state));
            shader.SetVector(Gust, GustVector(state));
            shader.SetVector(Params, ParamsVector(state));
            shader.SetVector(Fade, new Vector4(state.fadeStart, state.fadeEnd, state.flutterEnd, state.moonCascades));
        }

        static Vector4 WindVector(in ForestWindState state)
        {
            Vector2 lean = Direction(state.leanBearing);
            return new Vector4(lean.x, lean.y, Mathf.Max(0f, state.calm), Mathf.Max(0f, state.peak));
        }

        static Vector4 GustVector(in ForestWindState state)
        {
            Vector2 gust = Direction(state.gustBearing);
            return new Vector4(gust.x, gust.y, state.gustWavelength > 0f ? 1f / state.gustWavelength : 0f, state.gustSpeed);
        }

        static Vector4 ParamsVector(in ForestWindState state) => new Vector4(state.timeOffset, state.gustSeed, state.torchShadowEnd, state.solidSideOff ? 1f : 0f);

        /// <summary>I2b: the per-instance terms (this frame, last frame) of <paramref name="count"/> registered slots; 0 or null clears them.</summary>
        public static void WriteInstances(Texture current, Texture previous, int count)
        {
            bool valid = current != null && previous != null && count > 0;
            Shader.SetGlobalTexture(InstanceTex, valid ? current : Texture2D.blackTexture);
            Shader.SetGlobalTexture(InstanceTexPrev, valid ? previous : Texture2D.blackTexture);
            Shader.SetGlobalVector(InstanceParams, new Vector4(valid ? count : 0f, 0f, 0f, 0f));
        }

        /// <summary>Knob 2 (I2): the gust texture and its rect (origin X, origin Z, 1 / size, valid). rect.w = 0 clears it.</summary>
        public static void WriteGustTexture(Texture gust, Vector4 rect)
        {
            bool valid = gust != null && rect.w > 0f;
            Shader.SetGlobalTexture(GustTex, valid ? gust : Texture2D.blackTexture);
            Shader.SetGlobalVector(GustRect, valid ? rect : Vector4.zero);
        }

        /// <summary>The interaction field this frame and last frame, each with its rect (origin X, origin Z, 1 / size, valid).</summary>
        public static void WriteInteraction(Texture current, Vector4 rect, Texture previous, Vector4 previousRect)
        {
            bool valid = current != null && rect.w > 0f;
            bool previousValid = valid && previous != null && previousRect.w > 0f;
            Shader.SetGlobalTexture(InteractionTex, valid ? current : Texture2D.blackTexture);
            Shader.SetGlobalVector(InteractionRect, valid ? rect : Vector4.zero);
            Shader.SetGlobalTexture(InteractionTexPrev, previousValid ? previous : Texture2D.blackTexture);
            Shader.SetGlobalVector(InteractionRectPrev, previousValid ? previousRect : Vector4.zero);
        }

        /// <summary>I2 addition: the region the field has stamped lately (see <see cref="InteractionBox"/>).</summary>
        public static void WriteInteractionBox(Vector4 box) => Shader.SetGlobalVector(InteractionBox, box);

        /// <summary>W1 R1: the field's step parameters (see <see cref="InteractionParams"/>).</summary>
        public static void WriteInteractionParams(Vector4 parameters) => Shader.SetGlobalVector(InteractionParams, parameters);

        /// <summary>Both interaction rects to zero (no push anywhere).</summary>
        public static void ClearInteraction()
        {
            Shader.SetGlobalVector(InteractionRect, Vector4.zero);
            Shader.SetGlobalVector(InteractionRectPrev, Vector4.zero);
            Shader.SetGlobalVector(InteractionBox, Vector4.zero);
            Shader.SetGlobalVector(InteractionParams, Vector4.zero);
            Shader.SetGlobalTexture(InteractionTex, Texture2D.blackTexture);
            Shader.SetGlobalTexture(InteractionTexPrev, Texture2D.blackTexture);
        }

        /// <summary>Every global of section 3.4 to zero: the rest pose.</summary>
        public static void Clear()
        {
            ClearWind();
            ClearInteraction();
            WriteInstances(null, null, 0);
        }

        /// <summary>
        /// I2: the wind globals and the gust texture to zero, the interaction field's left alone (ForestWind.OnDisable; the field
        /// clears its own). With no field either, this is the rest pose.
        /// </summary>
        public static void ClearWind()
        {
            Shader.SetGlobalVector(Wind, Vector4.zero);
            Shader.SetGlobalVector(Gust, Vector4.zero);
            Shader.SetGlobalVector(Params, Vector4.zero);
            Shader.SetGlobalVector(Fade, Vector4.zero);
            Shader.SetGlobalVector(GustRect, Vector4.zero);
            Shader.SetGlobalTexture(GustTex, Texture2D.blackTexture);
        }

        /// <summary>Unit XZ direction of a bearing in degrees: (sin bearing, cos bearing); 0 = +Z, 90 = +X.</summary>
        public static Vector2 Direction(float bearing)
        {
            double radians = bearing * Math.PI / 180.0;
            return new Vector2((float)Math.Sin(radians), (float)Math.Cos(radians));
        }

        /// <summary>e, the shader's GamexGust, on the CPU (same constants, same wraps): 0 in a lull, 1 at a crest.</summary>
        public static float SampleGust(in ForestWindState state, Vector2 xz, float time)
        {
            return (float)SmoothStep(GustLow, GustHigh, RawGust(state, xz, time));
        }

        // g = 0.5 + 0.5 s, before the smoothstep.
        static double RawGust(in ForestWindState state, Vector2 xz, float time)
        {
            Vector2 d = Direction(state.gustBearing);
            double invL = state.gustWavelength > 0f ? 1.0 / state.gustWavelength : 0.0;
            double seed = state.gustSeed;
            double t = time;
            double along = (double)xz.x * d.x + (double)xz.y * d.y;
            double across = -(double)xz.x * d.y + (double)xz.y * d.x;   // dot(xz, (-d.y, d.x))
            double u = (along - state.gustSpeed * t) * invL;
            double w = across * invL * 0.5;
            double s = 0.70 * S(u + (1.3 / Tau) * S(w + seed))
                     + 0.20 * S(1.73 * u + 2.1 / Tau + (0.7 / Tau) * S(1.31 * w + 2.0 * seed))
                     + 0.10 * S(2.71 * u - 0.6 * w + seed + 4.2 / Tau);
            return 0.5 + 0.5 * s;
        }

        /// <summary>W at a point: calm + (peak - calm) e, before the distance fade and the shelter.</summary>
        public static float SampleStrength(in ForestWindState state, Vector2 xz, float time)
        {
            return state.calm + (state.peak - state.calm) * SampleGust(state, xz, time);
        }

        /// <summary>
        /// The time offset that puts a crest (the largest e within one gust period, wavelength / speed) over xz, the given number
        /// of seconds into a window: SampleGust(state, xz, offset + seconds) is that crest. Used by ForestCapture (I1) and the PlayMode
        /// helper (I2).
        /// </summary>
        public static float GustCentredOffset(in ForestWindState state, Vector2 xz, float seconds)
        {
            float period = state.gustSpeed > 0f && state.gustWavelength > 0f ? state.gustWavelength / state.gustSpeed : 0f;
            if (period <= 0f) return -seconds;
            const float step = 0.05f;
            // The crest of the unshaped g: e saturates at 1 over a plateau, g has one peak in it.
            float best = 0f;
            double bestG = double.NegativeInfinity;
            int count = Mathf.CeilToInt(period / step);
            for (int i = 0; i < count; i++)
            {
                float t = i * step;
                double g = RawGust(state, xz, t);
                if (g > bestG)
                {
                    bestG = g;
                    best = t;
                }
            }
            return best - seconds;
        }

        static double S(double cycles)
        {
            double x = cycles + 0.5;
            return Math.Sin(Tau * ((x - Math.Floor(x)) - 0.5));
        }

        static double SmoothStep(double a, double b, double x)
        {
            double t = Math.Min(1.0, Math.Max(0.0, (x - a) / (b - a)));
            return t * t * (3.0 - 2.0 * t);
        }
    }
}
