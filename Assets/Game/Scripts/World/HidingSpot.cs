using UnityEngine;

namespace Gamex
{
    /// <summary>
    /// A place the player can get into to break line of sight. It only protects them if no
    /// dinosaur was watching when they got in.
    /// </summary>
    public class HidingSpot : MonoBehaviour, IInteractable
    {
        [Tooltip("Where the player's feet go while hidden.")]
        [SerializeField] Transform hidePoint;
        [Tooltip("Where the player is put back when leaving. Must be on walkable ground.")]
        [SerializeField] Transform exitPoint;

        public Transform HidePoint => hidePoint;
        public Transform ExitPoint => exitPoint;
        public string Prompt => "Esconder-se";

        public void Interact(PlayerInteractor interactor) => interactor.Controller.EnterHiding(this);
    }
}
