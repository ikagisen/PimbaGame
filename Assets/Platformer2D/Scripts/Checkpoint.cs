using UnityEngine;

namespace Platformer2D
{
    /// <summary>Sets the respawn point when the player walks through it. Needs a trigger collider.</summary>
    public class Checkpoint : MonoBehaviour
    {
        public SpriteRenderer flag;
        public Color activeColor = new Color(0.2f, 0.85f, 0.3f);
        public Vector2 respawnOffset = new Vector2(0f, 1f);

        private Color idleColor = Color.white;
        private bool reached;

        private void Start()
        {
            if (flag != null) idleColor = flag.color;
            var game = PlatformerGame.Instance;
            if (game != null) game.RegisterCheckpoint(this);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (reached || PlatformerPlayer.FromCollider(other) == null) return;
            reached = true;
            if (flag != null) flag.color = activeColor;
            var game = PlatformerGame.Instance;
            if (game != null) game.SetCheckpoint((Vector2)transform.position + respawnOffset);
        }

        public void ResetFlag()
        {
            reached = false;
            if (flag != null) flag.color = idleColor;
        }
    }
}
