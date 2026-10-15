using UnityEngine;

namespace Gamex
{
    /// <summary>
    /// Throwaway HUD for the greybox. It stands in for feedback the final game will give through
    /// sound and animation (how loud the player is, how close a raptor is to spotting them), and
    /// adds a debug readout for tuning. Drawn with IMGUI so it needs no assets.
    /// </summary>
    public class PrototypeHud : MonoBehaviour
    {
        [SerializeField] GameSession session;
        [SerializeField] PlayerInteractor interactor;
        [SerializeField] RaptorAI[] raptors = System.Array.Empty<RaptorAI>();
        [SerializeField] bool showDebug;

        // Layout is authored for a 1080p-tall screen and scaled to the real one.
        const float ReferenceHeight = 1080f;

        GUIStyle label;
        GUIStyle title;
        float width;

        void Update()
        {
            if (session.Player.Input.ToggleDebugPressed)
                showDebug = !showDebug;
        }

        void OnGUI()
        {
            EnsureStyles();

            float scale = Screen.height / ReferenceHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            width = Screen.width / scale;

            if (session.State != SessionState.Playing)
            {
                DrawEndScreen();
                return;
            }

            DrawCrosshair();
            DrawPrompt();
            DrawStance();
            DrawAwareness();
            if (showDebug)
                DrawDebug();
        }

        void DrawEndScreen()
        {
            string message = session.State == SessionState.Dead ? "VOCÊ FOI PEGO" : "RESGATE A CAMINHO";
            GUI.color = session.State == SessionState.Dead ? new Color(0.9f, 0.15f, 0.1f) : new Color(0.5f, 0.9f, 0.5f);
            GUI.Label(new Rect(0f, 430f, width, 90f), message, title);
            GUI.color = Color.white;
            GUI.Label(new Rect(0f, 530f, width, 40f), "[R] Recomeçar", label);
        }

        void DrawCrosshair()
        {
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            GUI.DrawTexture(new Rect(width * 0.5f - 2f, ReferenceHeight * 0.5f - 2f, 4f, 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        void DrawPrompt()
        {
            string text = interactor.Prompt != null ? $"[E] {interactor.Prompt}" : null;
            if (interactor.HeldItem != null && !session.Player.IsHidden)
                text = text == null ? "[Clique] Arremessar" : $"{text}    [Clique] Arremessar";

            if (text != null)
                GUI.Label(new Rect(0f, 640f, width, 40f), text, label);
        }

        void DrawStance()
        {
            PlayerController player = session.Player;
            string stance = player.IsHidden ? "Escondido"
                : player.IsCrouching ? "Agachado"
                : player.IsSprinting ? "Correndo"
                : "Em pé";

            label.alignment = TextAnchor.MiddleLeft;
            GUI.Label(new Rect(40f, 980f, 300f, 40f), stance, label);
            label.alignment = TextAnchor.MiddleCenter;

            // Longest bar = sprinting; the bar is how far the player's footsteps carry.
            DrawBar(new Rect(40f, 1025f, 260f, 10f), player.CurrentNoiseRadius / 18f, new Color(1f, 0.8f, 0.3f));
        }

        void DrawAwareness()
        {
            float awareness = 0f;
            bool chased = false;
            foreach (RaptorAI raptor in raptors)
            {
                awareness = Mathf.Max(awareness, raptor.Awareness);
                chased |= raptor.State == RaptorState.Chase;
            }

            if (awareness <= 0f)
                return;

            Color colour = chased ? new Color(0.95f, 0.15f, 0.1f) : new Color(1f, 1f, 1f, 0.85f);
            DrawBar(new Rect(width * 0.5f - 130f, 90f, 260f, 10f), awareness, colour);
        }

        void DrawDebug()
        {
            Vector3 playerPosition = session.Player.transform.position;
            label.alignment = TextAnchor.UpperLeft;
            float y = 40f;
            foreach (RaptorAI raptor in raptors)
            {
                float distance = Vector3.Distance(raptor.transform.position, playerPosition);
                string sees = raptor.CanSeePlayer ? "  VÊ O JOGADOR" : "";
                GUI.Label(new Rect(40f, y, 900f, 40f),
                    $"{raptor.name}: {raptor.State}  alerta {raptor.Awareness:0.00}  {distance:0} m{sees}", label);
                y += 36f;
            }
            label.alignment = TextAnchor.MiddleCenter;
        }

        static void DrawBar(Rect rect, float fill, Color colour)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = colour;
            rect.width *= Mathf.Clamp01(fill);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        void EnsureStyles()
        {
            if (label != null)
                return;

            label = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
            };
            title = new GUIStyle(label) { fontSize = 72, fontStyle = FontStyle.Bold };
        }
    }
}
