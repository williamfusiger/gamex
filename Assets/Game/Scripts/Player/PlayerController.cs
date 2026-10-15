using UnityEngine;

namespace Gamex
{
    /// <summary>
    /// First-person movement. How the player moves decides how much noise they make, which is
    /// what the dinosaurs hear.
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(PlayerInputReader))]
    public class PlayerController : MonoBehaviour
    {
        [Header("View")]
        [SerializeField] Transform cameraPivot;
        [SerializeField] float mouseSensitivity = 0.12f;
        [SerializeField] float stickLookSpeed = 140f;
        [SerializeField] float maxPitch = 85f;
        [SerializeField] float standingEyeHeight = 1.65f;
        [SerializeField] float crouchEyeHeight = 0.95f;

        [Header("Movement")]
        [SerializeField] float walkSpeed = 3.2f;
        [SerializeField] float crouchSpeed = 1.6f;
        [SerializeField] float sprintSpeed = 6f;
        [SerializeField] float standingHeight = 1.8f;
        [SerializeField] float crouchHeight = 1.1f;
        [SerializeField] float gravity = -20f;

        [Header("Noise")]
        [Tooltip("Metres walked between two footstep noises.")]
        [SerializeField] float stepLength = 1.4f;
        [SerializeField] float crouchNoiseRadius = 2f;
        [SerializeField] float walkNoiseRadius = 8f;
        [SerializeField] float sprintNoiseRadius = 18f;

        CharacterController controller;
        float yaw;
        float pitch;
        float verticalVelocity;
        float distanceSinceStep;

        public PlayerInputReader Input { get; private set; }
        public Transform CameraPivot => cameraPivot;
        public Vector3 EyePosition => cameraPivot.position;

        public bool IsCrouching { get; private set; }
        public bool IsSprinting { get; private set; }
        public HidingSpot CurrentHidingSpot { get; private set; }
        public bool IsHidden => CurrentHidingSpot != null;

        /// <summary>Set when the run is over; the player can no longer move or look.</summary>
        public bool ControlsLocked { get; set; }

        /// <summary>How far the player's movement carries right now; 0 when silent.</summary>
        public float CurrentNoiseRadius { get; private set; }

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            Input = GetComponent<PlayerInputReader>();
            yaw = transform.eulerAngles.y;
            ApplyStance(snap: true);
        }

        void Update()
        {
            CurrentNoiseRadius = 0f;
            if (ControlsLocked)
                return;

            Look();
            if (IsHidden)
                return;

            if (Input.CrouchPressed && (!IsCrouching || CanStandUp()))
                IsCrouching = !IsCrouching;

            ApplyStance(snap: false);
            Move();
        }

        public void Teleport(Vector3 position, float yawDegrees)
        {
            // A CharacterController overrides transform changes while it is enabled.
            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            yaw = yawDegrees;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            controller.enabled = wasEnabled;
        }

        public void EnterHiding(HidingSpot spot)
        {
            if (IsHidden || ControlsLocked)
                return;

            CurrentHidingSpot = spot;
            IsCrouching = true;
            IsSprinting = false;
            controller.enabled = false;
            Teleport(spot.HidePoint.position, spot.HidePoint.eulerAngles.y);
            ApplyStance(snap: true);
        }

        public void ExitHiding()
        {
            if (!IsHidden)
                return;

            Transform exit = CurrentHidingSpot.ExitPoint;
            CurrentHidingSpot = null;
            Teleport(exit.position, yaw);
            controller.enabled = true;
        }

        void Look()
        {
            Vector2 delta = Input.LookPointerDelta * mouseSensitivity
                + Input.LookStick * (stickLookSpeed * Time.deltaTime);

            yaw += delta.x;
            pitch = Mathf.Clamp(pitch - delta.y, -maxPitch, maxPitch);

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        void Move()
        {
            Vector2 input = Vector2.ClampMagnitude(Input.Move, 1f);
            IsSprinting = !IsCrouching && Input.SprintHeld && input.y > 0.1f;

            float speed = IsCrouching ? crouchSpeed : IsSprinting ? sprintSpeed : walkSpeed;
            Vector3 velocity = (transform.right * input.x + transform.forward * input.y) * speed;

            if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -2f;
            verticalVelocity += gravity * Time.deltaTime;
            velocity.y = verticalVelocity;

            Vector3 before = transform.position;
            controller.Move(velocity * Time.deltaTime);

            // Measure what was actually covered, so pushing against a wall stays silent.
            Vector3 moved = transform.position - before;
            moved.y = 0f;
            EmitFootsteps(moved.magnitude);
        }

        void EmitFootsteps(float distance)
        {
            if (distance < 0.0005f)
                return;

            float radius = IsCrouching ? crouchNoiseRadius : IsSprinting ? sprintNoiseRadius : walkNoiseRadius;
            CurrentNoiseRadius = radius;

            distanceSinceStep += distance;
            if (distanceSinceStep < stepLength)
                return;

            distanceSinceStep = 0f;
            NoiseSystem.Emit(transform.position, radius, NoiseKind.Footstep);
        }

        void ApplyStance(bool snap)
        {
            float height = IsCrouching ? crouchHeight : standingHeight;
            controller.height = height;
            controller.center = new Vector3(0f, height * 0.5f, 0f);

            float eyeHeight = IsCrouching ? crouchEyeHeight : standingEyeHeight;
            Vector3 local = cameraPivot.localPosition;
            local.y = snap ? eyeHeight : Mathf.MoveTowards(local.y, eyeHeight, 4f * Time.deltaTime);
            cameraPivot.localPosition = local;
        }

        bool CanStandUp()
        {
            // Only the space above the crouched body is checked, with a gap so the player's own
            // collider is not counted as an obstruction.
            float radius = controller.radius * 0.9f;
            Vector3 top = transform.position + Vector3.up * (standingHeight - radius);
            Vector3 bottom = transform.position + Vector3.up * (crouchHeight + radius + 0.05f);
            return !Physics.CheckCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore);
        }
    }
}
