using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gamex
{
    /// <summary>
    /// The player's hand torch: a spot light held below and to the right of the eye that follows the view with a
    /// slight, smoothed lag, switched on and off with the Flashlight action of <see cref="PlayerInputReader"/>.
    /// It lets the player see, and it will also give the player away: <see cref="IsOn"/> and
    /// <see cref="IsOnChanged"/> are what the dinosaurs' senses will read.
    ///
    /// This component owns the torch's photometry (lumens, cone, colour, range, beam pattern) and writes it to
    /// its Light. Settings that only the render pipeline knows (shadow resolution and update mode, the volumetric
    /// beam) are set on the HDRP light data by the scene builder, because this assembly does not reference HDRP.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    public class PlayerFlashlight : MonoBehaviour
    {
        [Header("State")]
        [SerializeField] bool startOn = true;

        [Header("Output")]
        [Tooltip("Luminous flux that leaves the torch, in lumens: the beam pattern and the cone's edge falloff are included.")]
        [SerializeField] float lumens = 300f;
        [Tooltip("Full angle of the beam's outer edge, in degrees.")]
        [SerializeField] float outerConeDegrees = 70f;
        [Tooltip("Full angle inside which the cone does not fade, in degrees.")]
        [SerializeField] float innerConeDegrees = 10f;
        [Tooltip("Where the light ends, in metres. It fades smoothly to nothing there.")]
        [SerializeField] float range = 35f;
        [Tooltip("Colour temperature of the LED, in kelvin.")]
        [SerializeField] float colorTemperature = 4500f;
        [Tooltip("Side of the generated beam-pattern texture, in pixels. Used when the light has no cookie assigned.")]
        [SerializeField] int cookieResolution = 256;

        [Header("Hand-held lag")]
        [Tooltip("Time constant with which the beam catches up with the view, in seconds.")]
        [SerializeField] float lagSeconds = 0.07f;
        [Tooltip("The beam never trails the view by more than this, in degrees.")]
        [SerializeField] float maxLagDegrees = 10f;
        [Tooltip("A view that moves further than this in one frame was teleported or cut to: the beam does not swing after it.")]
        [SerializeField] float snapDistance = 1.5f;

        Light beam;
        PlayerInputReader input;
        PlayerController controller;
        Quaternion restLocalRotation;
        Quaternion smoothedRotation;
        Vector3 lastViewPosition;
        bool hasSmoothedRotation;
        bool initialised;
        bool isOn;
        Texture2D generatedCookie;

        /// <summary>Raised with the new state whenever the torch is switched on or off.</summary>
        public event Action<bool> IsOnChanged;

        /// <summary>Whether the torch is switched on. It starts on.</summary>
        public bool IsOn
        {
            get
            {
                Initialise();
                return isOn;
            }
        }

        public Light Light
        {
            get
            {
                Initialise();
                return beam;
            }
        }

        public float Lumens => lumens;
        public float OuterConeDegrees => outerConeDegrees;
        public float InnerConeDegrees => innerConeDegrees;
        public float Range => range;
        public float LagSeconds => lagSeconds;
        public float MaxLagDegrees => maxLagDegrees;

        /// <summary>Intensity on the beam's axis where the pattern is brightest, in candela, for the stated lumens.</summary>
        public float PeakCandela => CandelaForLumens(lumens, outerConeDegrees, innerConeDegrees);

        void Awake()
        {
            Initialise();
        }

        void OnDestroy()
        {
            if (generatedCookie != null)
            {
                if (beam != null && beam.cookie == generatedCookie)
                    beam.cookie = null;
                Destroy(generatedCookie);
            }
        }

        void Update()
        {
            if (input == null || (controller != null && controller.ControlsLocked))
                return;
            if (input.FlashlightPressed)
                Toggle();
        }

        void LateUpdate()
        {
            Transform view = transform.parent;
            if (view == null)
                return;

            Quaternion target = view.rotation * restLocalRotation;
            float deltaTime = Time.deltaTime;
            bool cut = !hasSmoothedRotation || (view.position - lastViewPosition).sqrMagnitude > snapDistance * snapDistance;
            lastViewPosition = view.position;
            if (cut || lagSeconds <= 0f || deltaTime <= 0f)
            {
                smoothedRotation = target;
                hasSmoothedRotation = true;
            }
            else
            {
                smoothedRotation = SmoothTowards(smoothedRotation, target, deltaTime, lagSeconds, maxLagDegrees);
            }
            transform.rotation = smoothedRotation;
        }

        /// <summary>Switches the torch on or off. Raises <see cref="IsOnChanged"/> when the state changes.</summary>
        public void SetOn(bool on)
        {
            Initialise();
            if (on == isOn)
                return;
            isOn = on;
            beam.enabled = on;
            IsOnChanged?.Invoke(on);
        }

        public void Toggle() => SetOn(!IsOn);

        /// <summary>Puts the beam on the view at once, without lag (after a teleport or a camera cut).</summary>
        public void SnapToView()
        {
            hasSmoothedRotation = false;
            if (transform.parent != null)
                transform.rotation = transform.parent.rotation * restLocalRotation;
        }

        /// <summary>
        /// Writes the torch's photometry to its Light: a spot light whose intensity, in candela, makes the stated lumens leave the
        /// torch through this cone and beam pattern. The scene builder calls it too, so the saved scene shows the same light.
        /// </summary>
        public void ApplyPhotometry()
        {
            Light target = beam != null ? beam : GetComponent<Light>();
            target.type = LightType.Spot;
            target.spotAngle = outerConeDegrees;
            target.innerSpotAngle = Mathf.Min(innerConeDegrees, outerConeDegrees);
            target.range = range;
            target.useColorTemperature = true;
            target.colorTemperature = colorTemperature;
            target.color = Color.white;
            target.shadows = LightShadows.Soft;
            // Candela, with the reflector option off: the intensity is set here from the lumens, and nothing converts it again.
            target.enableSpotReflector = false;
            target.lightUnit = LightUnit.Candela;
            target.intensity = CandelaForLumens(lumens, outerConeDegrees, innerConeDegrees);
        }

        void Initialise()
        {
            if (initialised)
                return;
            initialised = true;
            beam = GetComponent<Light>();
            input = GetComponentInParent<PlayerInputReader>();
            controller = GetComponentInParent<PlayerController>();
            restLocalRotation = transform.localRotation;
            ApplyPhotometry();
            if (beam.cookie == null && cookieResolution >= 16)
            {
                generatedCookie = CreateCookie(cookieResolution);
                // Made at run time for this light alone: never saved into a scene that happens to be open.
                generatedCookie.hideFlags = HideFlags.DontSave;
                beam.cookie = generatedCookie;
            }
            isOn = startOn;
            beam.enabled = startOn;
        }

        // ---- Pure functions: the lag, the beam pattern and its photometry ----

        /// <summary>
        /// One frame of the hand-held lag: moves <paramref name="current"/> toward <paramref name="target"/> with an exponential
        /// time constant, and never leaves it more than <paramref name="maxLagDegrees"/> behind. Independent of the frame rate.
        /// </summary>
        public static Quaternion SmoothTowards(Quaternion current, Quaternion target, float deltaTime, float timeConstant, float maxLagDegrees)
        {
            if (timeConstant <= 0f || deltaTime <= 0f)
                return target;
            float follow = 1f - Mathf.Exp(-deltaTime / timeConstant);
            Quaternion next = Quaternion.Slerp(current, target, follow);
            float behind = Quaternion.Angle(next, target);
            if (behind > maxLagDegrees)
                next = Quaternion.RotateTowards(next, target, behind - maxLagDegrees);
            return next;
        }

        // The beam pattern of an LED torch with a smooth reflector, as a function of the distance from the beam's centre (0) to
        // the cone's outer edge (1): a hot spot (the reflector's image of the LED) that carries the beam's reach, a bright ring where
        // that image ends, a wide spill of the light that misses the reflector, falling off smoothly so the beam has no hard edge, and
        // the LED die's slightly darker core. The reflector's orange peel adds fine concentric ripples and a slow variation around
        // the beam. Tuned on renders of the night (build/issues/N1-night.md, section 3).
        const float HotSpotRadius = 0.20f;
        const float SpillLevel = 0.30f;
        const float SpillWidth = 0.50f;

        /// <summary>The beam pattern, 0 to 1, at normalised radius <paramref name="radius"/> (1 = the outer cone) and angle <paramref name="angle"/> (radians).</summary>
        public static float BeamPattern(float radius, float angle)
        {
            if (!(radius >= 0f) || radius >= 1f)
                return 0f;
            float hot = Mathf.Exp(-Square(radius / HotSpotRadius));
            float core = 1f - 0.10f * Mathf.Exp(-Square(radius / 0.045f));
            float spill = SpillLevel * Mathf.Exp(-Square(radius / SpillWidth)) * Mathf.Clamp01((1f - radius) / 0.1f);
            float ring = 0.09f * Mathf.Exp(-Square((radius - 1.27f * HotSpotRadius) / 0.035f));
            float peel = 1f + 0.03f * Mathf.Sin(radius * 71f) + 0.02f * Mathf.Sin(angle * 3f + radius * 13f);
            return Mathf.Clamp01((spill + (1f - SpillLevel) * hot * core + ring) * peel);
        }

        /// <summary>
        /// The beam pattern as a square cookie texture of the given size (linear, grey, mipmapped, clamped), laid out as HDRP projects a
        /// spot light's cookie: the inscribed circle is the outer cone. The caller owns the texture.
        /// </summary>
        public static Texture2D CreateCookie(int size)
        {
            if (size < 16)
                throw new ArgumentOutOfRangeException(nameof(size), "A beam pattern needs at least 16 pixels a side.");
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = "Flashlight Beam Pattern",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    byte level = (byte)Mathf.RoundToInt(BeamPattern(Mathf.Sqrt(u * u + v * v), Mathf.Atan2(v, u)) * 255f);
                    pixels[y * size + x] = new Color32(level, level, level, 255);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, false);
            return texture;
        }

        /// <summary>
        /// Lumens per candela of peak intensity for a spot light of this cone carrying the beam pattern: the integral over the cone
        /// of HDRP's cone falloff (a squared linear ramp in the cosine between the inner and outer half-angles) times the pattern.
        /// </summary>
        public static float LumensPerCandela(float outerConeDegrees, float innerConeDegrees)
        {
            if (!(outerConeDegrees > 0f) || outerConeDegrees >= 180f)
                throw new ArgumentOutOfRangeException(nameof(outerConeDegrees), "The outer cone must be between 0 and 180 degrees.");
            double half = 0.5 * outerConeDegrees * Math.PI / 180.0;
            double cosOuter = Math.Cos(half);
            double cosInner = Math.Cos(0.5 * Mathf.Clamp(innerConeDegrees, 0f, outerConeDegrees) * Math.PI / 180.0);
            double ramp = Math.Max(0.0001, cosInner - cosOuter);
            double tanHalf = Math.Tan(half);

            const int radialSteps = 600, angularSteps = 48;
            double step = half / radialSteps, sum = 0.0;
            for (int i = 0; i < radialSteps; i++)
            {
                double theta = (i + 0.5) * step;
                double falloff = Math.Min(1.0, Math.Max(0.0, (Math.Cos(theta) - cosOuter) / ramp));
                float radius = (float)(Math.Tan(theta) / tanHalf);
                double pattern = 0.0;
                for (int j = 0; j < angularSteps; j++)
                    pattern += BeamPattern(radius, (float)((j + 0.5) * 2.0 * Math.PI / angularSteps));
                pattern /= angularSteps;
                sum += falloff * falloff * pattern * Math.Sin(theta) * step;
            }
            return (float)(2.0 * Math.PI * sum);
        }

        /// <summary>Peak intensity, in candela, that makes <paramref name="lumens"/> leave a torch of this cone and beam pattern.</summary>
        public static float CandelaForLumens(float lumens, float outerConeDegrees, float innerConeDegrees)
        {
            return Mathf.Max(0f, lumens) / LumensPerCandela(outerConeDegrees, innerConeDegrees);
        }

        static float Square(float value) => value * value;
    }
}
