using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gamex
{
    public enum SessionState
    {
        Playing,
        Dead,
        Rescued,
    }

    /// <summary>
    /// Tracks whether the run is still going, and how it ended.
    /// </summary>
    public class GameSession : MonoBehaviour
    {
        [SerializeField] PlayerController player;

        public PlayerController Player => player;
        public SessionState State { get; private set; } = SessionState.Playing;

        public event Action<SessionState> StateChanged;

        void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void Update()
        {
            if (State != SessionState.Playing && player.Input.RestartPressed)
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void KillPlayer() => End(SessionState.Dead);

        public void Rescue() => End(SessionState.Rescued);

        void End(SessionState result)
        {
            if (State != SessionState.Playing)
                return;

            State = result;
            player.ControlsLocked = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            StateChanged?.Invoke(result);
        }
    }
}
