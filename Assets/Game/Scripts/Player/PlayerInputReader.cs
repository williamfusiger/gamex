using UnityEngine;
using UnityEngine.InputSystem;

namespace Gamex
{
    /// <summary>
    /// Owns the player's input actions. Bindings live in code so the prototype has no input
    /// asset to keep in sync; keyboard and mouse and a gamepad are bound side by side.
    /// </summary>
    public class PlayerInputReader : MonoBehaviour
    {
        InputAction move;
        InputAction lookPointer;
        InputAction lookStick;
        InputAction sprint;
        InputAction crouch;
        InputAction interact;
        InputAction throwItem;
        InputAction restart;
        InputAction toggleDebug;

        public Vector2 Move => move.ReadValue<Vector2>();

        /// <summary>Mouse movement this frame, in pixels.</summary>
        public Vector2 LookPointerDelta => lookPointer.ReadValue<Vector2>();

        /// <summary>Right stick deflection, -1..1 per axis.</summary>
        public Vector2 LookStick => lookStick.ReadValue<Vector2>();

        public bool SprintHeld => sprint.IsPressed();
        public bool CrouchPressed => crouch.WasPressedThisFrame();
        public bool InteractPressed => interact.WasPressedThisFrame();
        public bool ThrowPressed => throwItem.WasPressedThisFrame();
        public bool RestartPressed => restart.WasPressedThisFrame();
        public bool ToggleDebugPressed => toggleDebug.WasPressedThisFrame();

        void Awake()
        {
            move = new InputAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            move.AddBinding("<Gamepad>/leftStick");

            lookPointer = new InputAction("LookPointer", InputActionType.Value, "<Mouse>/delta");
            lookStick = new InputAction("LookStick", InputActionType.Value, "<Gamepad>/rightStick");

            sprint = Button("Sprint", "<Keyboard>/leftShift", "<Gamepad>/leftTrigger");
            crouch = Button("Crouch", "<Keyboard>/c", "<Keyboard>/leftCtrl", "<Gamepad>/buttonEast");
            interact = Button("Interact", "<Keyboard>/e", "<Gamepad>/buttonWest");
            throwItem = Button("Throw", "<Mouse>/leftButton", "<Gamepad>/rightTrigger");
            restart = Button("Restart", "<Keyboard>/r", "<Gamepad>/start");
            toggleDebug = Button("ToggleDebug", "<Keyboard>/f1");
        }

        void OnEnable() => SetEnabled(true);

        void OnDisable() => SetEnabled(false);

        void OnDestroy()
        {
            foreach (InputAction action in AllActions())
                action.Dispose();
        }

        static InputAction Button(string name, params string[] bindings)
        {
            var action = new InputAction(name, InputActionType.Button);
            foreach (string binding in bindings)
                action.AddBinding(binding);
            return action;
        }

        void SetEnabled(bool enabled)
        {
            foreach (InputAction action in AllActions())
            {
                if (enabled)
                    action.Enable();
                else
                    action.Disable();
            }
        }

        InputAction[] AllActions() => new[]
        {
            move, lookPointer, lookStick, sprint, crouch, interact, throwItem, restart, toggleDebug,
        };
    }
}
