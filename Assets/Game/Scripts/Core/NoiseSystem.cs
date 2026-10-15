using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gamex
{
    public enum NoiseKind
    {
        Footstep,
        Impact,
    }

    public readonly struct NoiseEvent
    {
        public readonly Vector3 Position;
        public readonly float Radius;
        public readonly NoiseKind Kind;

        public NoiseEvent(Vector3 position, float radius, NoiseKind kind)
        {
            Position = position;
            Radius = radius;
            Kind = kind;
        }
    }

    public interface INoiseListener
    {
        void OnNoise(in NoiseEvent noise);
    }

    /// <summary>
    /// Channel for every sound a creature can react to. Emitters only say where the sound came
    /// from and how far it carries; each listener decides whether it is close enough to hear it.
    /// </summary>
    public static class NoiseSystem
    {
        static readonly List<INoiseListener> listeners = new();

        public static event Action<NoiseEvent> Emitted;

        // Play mode can start without a domain reload, so static state has to be cleared by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            listeners.Clear();
            Emitted = null;
        }

        public static void Register(INoiseListener listener)
        {
            if (!listeners.Contains(listener))
                listeners.Add(listener);
        }

        public static void Unregister(INoiseListener listener) => listeners.Remove(listener);

        public static void Emit(Vector3 position, float radius, NoiseKind kind)
        {
            if (radius <= 0f)
                return;

            var noise = new NoiseEvent(position, radius, kind);

            // Backwards, so a listener may unregister itself while reacting.
            for (int i = listeners.Count - 1; i >= 0; i--)
                listeners[i].OnNoise(noise);

            Emitted?.Invoke(noise);
        }
    }
}
