using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Platformer2D
{
    /// <summary>
    /// Keeps score, handles checkpoints, falls, winning and restarting, and draws a simple HUD.
    /// Restart (R) resets everything in place, so the scene doesn't need to be in Build Settings.
    /// </summary>
    public class PlatformerGame : MonoBehaviour
    {
        public static PlatformerGame Instance { get; private set; }

        [Tooltip("Falling below this height counts as a fall (backup for the kill zone).")]
        public float killY = -15f;

        private PlatformerPlayer player;
        private Vector2 startPoint, respawnPoint;
        private readonly List<Coin> coins = new List<Coin>();
        private readonly List<Checkpoint> checkpoints = new List<Checkpoint>();
        private int collected, falls;
        private float timer;
        private bool won;

        private GUIStyle hudStyle, hintStyle, titleStyle, centerStyle;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            var go = GameObject.FindWithTag("Player");
            if (go != null) player = go.GetComponent<PlatformerPlayer>();
            if (player != null) startPoint = respawnPoint = player.transform.position;
            else Debug.LogWarning("PlatformerGame: no GameObject tagged 'Player' with a PlatformerPlayer was found.");
        }

        private void Update()
        {
            if (!won) timer += Time.deltaTime;
            if (player != null && player.transform.position.y < killY) KillPlayer();

            bool restart = Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
            restart |= Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;
            if (restart) RestartLevel();
        }

        public void RegisterCoin(Coin coin)
        {
            if (!coins.Contains(coin)) coins.Add(coin);
        }

        public void RegisterCheckpoint(Checkpoint checkpoint)
        {
            if (!checkpoints.Contains(checkpoint)) checkpoints.Add(checkpoint);
        }

        public void CollectCoin() => collected++;

        public void SetCheckpoint(Vector2 point) => respawnPoint = point;

        public void KillPlayer()
        {
            if (player == null) return;
            if (!won) falls++;
            player.Respawn(respawnPoint);
        }

        public void Win() => won = true;

        public void RestartLevel()
        {
            collected = 0;
            falls = 0;
            timer = 0f;
            won = false;
            respawnPoint = startPoint;
            foreach (var coin in coins) if (coin != null) coin.gameObject.SetActive(true);
            foreach (var checkpoint in checkpoints) if (checkpoint != null) checkpoint.ResetFlag();
            if (player != null) player.Respawn(startPoint);
        }

        private void OnGUI()
        {
            if (hudStyle == null)
            {
                hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
                hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 15 };
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 52, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                centerStyle = new GUIStyle(hudStyle) { alignment = TextAnchor.MiddleCenter };
            }

            Label(new Rect(20, 14, 900, 40), $"Coins {collected}/{coins.Count}     Time {timer:0.0}s     Falls {falls}", hudStyle);
            Label(new Rect(20, 50, 1400, 26),
                "Move: A/D or Arrows    Jump: Space (again in mid-air = double jump)    Boost: hold N or Shift    Giant mode: T    Restart: R",
                hintStyle);

            if (won)
            {
                float y = Screen.height * 0.3f;
                Label(new Rect(0, y, Screen.width, 70), "LEVEL COMPLETE!", titleStyle);
                Label(new Rect(0, y + 74, Screen.width, 40), $"{collected}/{coins.Count} coins in {timer:0.0}s  -  press R to play again", centerStyle);
            }
        }

        private static void Label(Rect rect, string text, GUIStyle style)
        {
            Color original = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.65f);
            GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), text, style);
            style.normal.textColor = Color.white;
            GUI.Label(rect, text, style);
            style.normal.textColor = original;
        }
    }
}
