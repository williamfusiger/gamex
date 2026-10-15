using UnityEngine;

namespace Gamex
{
    /// <summary>
    /// The goal of the run: reaching it and calling for rescue ends the session.
    /// </summary>
    public class RescueRadio : MonoBehaviour, IInteractable
    {
        [SerializeField] GameSession session;

        public string Prompt => "Chamar resgate";

        public void Interact(PlayerInteractor interactor) => session.Rescue();
    }
}
