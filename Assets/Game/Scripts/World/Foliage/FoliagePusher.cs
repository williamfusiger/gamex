using System;
using UnityEngine;

namespace Gamex
{
    /// <summary>
    /// A body that parts foliage (W1 spec 2.2, 2.10, 6.2). The interaction field stamps it every frame: a capsule seen from above,
    /// a segment of <see cref="Length"/> along the body's forward (a point for a round body) with <see cref="Radius"/>, pressing
    /// foliage below <see cref="BandTopWorld"/>. The player's sits on its own child "FoliagePusher" of the Player root and reads the
    /// CharacterController in its parents (radius, height: crouching lowers the band); dinosaurs get one later.
    /// It also reports its <see cref="Contact"/> with the foliage each frame from the density map, and raises
    /// <see cref="Rustled"/> (and <see cref="FoliageInteraction.Rustled"/>) once on entering foliage and once per
    /// <c>rustleStep</c> metres travelled in it.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class FoliagePusher : MonoBehaviour
    {
        [SerializeField] float radius = 0.35f, length = 0f, bandTop = 1.8f, softness = 0.25f, strength = 1f, rustleStep = 1.0f;
        [SerializeField] bool isPlayer;

        CharacterController body;
        Vector3 lastAxis;
        bool hasLast, wasInside;
        float travelled;
        Vector3 velocity;
        FoliageContact contact;

        /// <summary>Horizontal radius of the body, metres (the CharacterController's for the player).</summary>
        public float Radius => body != null ? body.radius * HorizontalScale(body.transform) : radius;
        /// <summary>Total length of the body along its forward, metres; 0 for a round body.</summary>
        public float Length => length;
        /// <summary>World Y of the top of the body: foliage above it is not pressed.</summary>
        public float BandTopWorld => body != null ? body.transform.TransformPoint(body.center + Vector3.up * (body.height * 0.5f)).y : transform.position.y + bandTop;
        public float Strength => strength;
        /// <summary>How far beyond the radius the press fades out, metres.</summary>
        public float Softness => softness;
        public bool IsPlayer => isPlayer;
        /// <summary>World velocity of the body axis, from the last frame's movement.</summary>
        public Vector3 Velocity => velocity;
        /// <summary>This frame's contact with the foliage, from the density map.</summary>
        public FoliageContact Contact => contact;

        /// <summary>Raised on entering foliage and once per rustleStep metres travelled in it.</summary>
        public event Action<FoliageRustle> Rustled;

        /// <summary>
        /// I2 addition: sets the body of a pusher made in code (the raptor proxy of the tests and the harness). The player's comes
        /// from the scene builder.
        /// </summary>
        public void Configure(float radius, float length, float bandTop, float softness = 0.25f, float strength = 1f, float rustleStep = 1f, bool isPlayer = false)
        {
            this.radius = Mathf.Max(0.01f, radius);
            this.length = Mathf.Max(0f, length);
            this.bandTop = Mathf.Max(0f, bandTop);
            this.softness = Mathf.Max(0f, softness);
            this.strength = Mathf.Clamp01(strength);
            this.rustleStep = Mathf.Max(0.05f, rustleStep);
            this.isPlayer = isPlayer;
        }

        /// <summary>The body axis this frame (world XZ); from == to for a round body.</summary>
        public void GetStamp(out Vector2 from, out Vector2 to)
        {
            Vector3 axis = AxisPosition();
            var centre = new Vector2(axis.x, axis.z);
            float half = Mathf.Max(0f, Length * 0.5f - Radius);
            Vector3 forward = transform.forward;
            var along = new Vector2(forward.x, forward.z);
            if (half <= 0f || along.sqrMagnitude < 1e-8f)
            {
                from = to = centre;
                return;
            }
            along.Normalize();
            from = centre - along * half;
            to = centre + along * half;
        }

        /// <summary>World position of the body's base on the ground (the CharacterController's foot for the player).</summary>
        public Vector3 BasePosition => body != null ? body.transform.TransformPoint(body.center - Vector3.up * (body.height * 0.5f)) : transform.position;

        Vector3 AxisPosition() => body != null ? body.transform.TransformPoint(body.center) : transform.position;

        static float HorizontalScale(Transform t)
        {
            Vector3 s = t.lossyScale;
            return Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
        }

        void Awake()
        {
            body = GetComponentInParent<CharacterController>();
        }

        void OnEnable()
        {
            if (body == null) body = GetComponentInParent<CharacterController>();
            hasLast = false;
            wasInside = false;
            travelled = 0f;
            velocity = Vector3.zero;
            FoliageInteraction.Register(this);
        }

        void OnDisable()
        {
            FoliageInteraction.Unregister(this);
            contact = default;
        }

        void LateUpdate()
        {
            Vector3 axis = AxisPosition();
            float dt = Time.deltaTime;
            Vector3 step = hasLast ? axis - lastAxis : Vector3.zero;
            if (dt > 0f && hasLast)
                velocity = step / dt;
            else if (!hasLast)
                velocity = Vector3.zero;
            lastAxis = axis;
            hasLast = true;

            Vector3 foot = BasePosition;
            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            float density = FoliageInteraction.SampleDensity(foot, Radius, 0f, BandTopWorld - foot.y, out FoliageLayer layer);
            float intensity = density * Mathf.Clamp01(speed / 1.5f);
            contact = new FoliageContact(foot, density, speed, intensity, layer);

            bool inside = density >= FoliageInteraction.InFoliageDensity;
            if (inside)
            {
                bool moving = speed > 0.1f;
                if (!wasInside && moving)
                {
                    travelled = 0f;
                    Raise();
                }
                else if (wasInside)
                {
                    travelled += new Vector2(step.x, step.z).magnitude;
                    if (travelled >= rustleStep)
                    {
                        travelled -= rustleStep * Mathf.Floor(travelled / rustleStep);
                        Raise();
                    }
                }
            }
            else
            {
                travelled = 0f;
            }
            wasInside = inside;
        }

        void Raise()
        {
            var rustle = new FoliageRustle(contact.Position, contact.Intensity, contact.Density, contact.Speed, Time.time, contact.Layer, this, isPlayer);
            Rustled?.Invoke(rustle);
            FoliageInteraction.Raise(rustle);
        }
    }
}
