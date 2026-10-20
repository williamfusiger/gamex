using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gamex
{
    /// <summary>Which foliage a body is in: none, the low layer (ferns and grass) or the mid layer (young-fir lower crowns).</summary>
    public enum FoliageLayer { None = 0, Low = 1, Mid = 2 }

    /// <summary>A pusher's contact with the foliage this frame, from the density map (W1 spec 2.10).</summary>
    public readonly struct FoliageContact
    {
        public readonly Vector3 Position;
        public readonly float Density, Speed, Intensity;
        public readonly FoliageLayer Layer;

        public FoliageContact(Vector3 position, float density, float speed, float intensity, FoliageLayer layer)
        {
            Position = position;
            Density = density;
            Speed = speed;
            Intensity = intensity;
            Layer = layer;
        }
    }

    /// <summary>
    /// "A pusher is moving through dense foliage here": raised at a footstep-like cadence (W1 spec 2.10). The hook for rustle audio
    /// and for stealth noise later (a bridge to NoiseSystem.Emit in the dinosaur step); nothing listens in W1.
    /// </summary>
    public readonly struct FoliageRustle
    {
        public readonly Vector3 Position;
        public readonly float Intensity, Density, Speed, Time;
        public readonly FoliageLayer Layer;
        public readonly FoliagePusher Source;
        public readonly bool IsPlayer;

        public FoliageRustle(Vector3 position, float intensity, float density, float speed, float time, FoliageLayer layer, FoliagePusher source, bool isPlayer)
        {
            Position = position;
            Intensity = intensity;
            Density = density;
            Speed = speed;
            Time = time;
            Layer = layer;
            Source = source;
            IsPlayer = isPlayer;
        }
    }

    /// <summary>
    /// The registry of pushers, the rustle event and the density query (W1 spec 2.10, 6.2). Gameplay reads the CPU density map,
    /// never the GPU field.
    /// </summary>
    public static class FoliageInteraction
    {
        /// <summary>Heights above the ground of the two layers: ferns and grass up to 1 m, young-fir lower crowns 0.3 to 2 m.</summary>
        public const float LowTop = 1.0f, MidBottom = 0.3f, MidTop = 2.0f;

        /// <summary>A body is in foliage (and rustles) where the density under it reaches this.</summary>
        public const float InFoliageDensity = 0.25f;

        static readonly List<FoliagePusher> pushers = new List<FoliagePusher>();

        /// <summary>Every enabled pusher.</summary>
        public static IReadOnlyList<FoliagePusher> Pushers => pushers;

        /// <summary>Every pusher's rustles, after its own event.</summary>
        public static event Action<FoliageRustle> Rustled;

        /// <summary>The scene's density map (set by FoliageInteractionField when it is enabled); null = no foliage anywhere.</summary>
        public static FoliageDensityMap Density { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            pushers.Clear();
            Rustled = null;
            Density = null;
        }

        /// <summary>
        /// The densest foliage, 0..1, that a body of the given radius standing at <paramref name="position"/> (its base, on the
        /// ground) meets between <paramref name="bandBottom"/> and <paramref name="bandTop"/> metres above that base.
        /// </summary>
        public static float SampleDensity(Vector3 position, float radius, float bandBottom, float bandTop) =>
            SampleDensity(position, radius, bandBottom, bandTop, out _);

        /// <summary>As <see cref="SampleDensity(Vector3, float, float, float)"/>, with the layer that gave the value.</summary>
        public static float SampleDensity(Vector3 position, float radius, float bandBottom, float bandTop, out FoliageLayer layer)
        {
            layer = FoliageLayer.None;
            FoliageDensityMap map = Density;
            if (map == null || bandTop <= bandBottom)
                return 0f;
            bool low = bandBottom < LowTop && bandTop > 0f;
            bool mid = bandBottom < MidTop && bandTop > MidBottom;
            float best = 0f;
            float r = Mathf.Max(0f, radius) * 0.7f;
            var centre = new Vector2(position.x, position.z);
            for (int i = 0; i < 5; i++)
            {
                Vector2 xz = i == 0 ? centre : centre + new Vector2(i == 1 ? r : i == 2 ? -r : 0f, i == 3 ? r : i == 4 ? -r : 0f);
                if (low)
                {
                    float d = map.Sample(xz, FoliageLayer.Low);
                    if (d > best) { best = d; layer = FoliageLayer.Low; }
                }
                if (mid)
                {
                    float d = map.Sample(xz, FoliageLayer.Mid);
                    if (d > best) { best = d; layer = FoliageLayer.Mid; }
                }
            }
            return best;
        }

        internal static void Register(FoliagePusher pusher)
        {
            if (!pushers.Contains(pusher))
                pushers.Add(pusher);
        }

        internal static void Unregister(FoliagePusher pusher) => pushers.Remove(pusher);

        internal static void Raise(in FoliageRustle rustle) => Rustled?.Invoke(rustle);
    }
}
