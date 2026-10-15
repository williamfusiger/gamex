using UnityEngine;

namespace Gamex
{
    /// <summary>
    /// Lets the player use what they are looking at, and carry and throw one object.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] Transform holdPoint;
        [SerializeField] float reach = 2.5f;
        [SerializeField] float throwSpeed = 15f;
        [Tooltip("How much the throw is aimed above the crosshair, so it arcs.")]
        [SerializeField] float throwLift = 0.15f;

        Collider body;

        public PlayerController Controller { get; private set; }
        public Throwable HeldItem { get; private set; }

        /// <summary>Label for what the interact button would do now, or null.</summary>
        public string Prompt { get; private set; }

        void Awake()
        {
            Controller = GetComponent<PlayerController>();
            body = GetComponent<Collider>();
        }

        void Update()
        {
            Prompt = null;
            if (Controller.ControlsLocked)
                return;

            PlayerInputReader input = Controller.Input;

            if (Controller.IsHidden)
            {
                Prompt = "Sair do esconderijo";
                if (input.InteractPressed)
                    Controller.ExitHiding();
                return;
            }

            IInteractable focused = FindFocused();
            Prompt = focused?.Prompt;

            if (input.InteractPressed)
                focused?.Interact(this);
            if (input.ThrowPressed)
                ThrowHeldItem();
        }

        public void Hold(Throwable item)
        {
            if (HeldItem != null)
                return;

            HeldItem = item;
            item.OnHeld(holdPoint);
        }

        public void ThrowHeldItem()
        {
            if (HeldItem == null)
                return;

            Transform view = Controller.CameraPivot;
            Vector3 direction = (view.forward + Vector3.up * throwLift).normalized;
            HeldItem.Throw(direction * throwSpeed, body);
            HeldItem = null;
        }

        IInteractable FindFocused()
        {
            Transform view = Controller.CameraPivot;
            bool hitSomething = Physics.Raycast(view.position, view.forward, out RaycastHit hit, reach,
                ~0, QueryTriggerInteraction.Collide);
            return hitSomething ? hit.collider.GetComponentInParent<IInteractable>() : null;
        }
    }
}
