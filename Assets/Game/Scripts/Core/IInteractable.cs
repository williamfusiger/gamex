namespace Gamex
{
    /// <summary>
    /// Something the player can use by looking at it and pressing the interact button.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Short action label shown on the HUD, e.g. "Esconder-se".</summary>
        string Prompt { get; }

        void Interact(PlayerInteractor interactor);
    }
}
