using UnityEngine;

namespace Platformer2D
{
    /// <summary>Sends the player back to the last checkpoint on touch (spikes, kill zones). Needs a trigger collider.</summary>
    public class Hazard : MonoBehaviour
    {
        private void OnTriggerEnter2D(Collider2D other)
        {
            if (PlatformerPlayer.FromCollider(other) == null) return;
            var game = PlatformerGame.Instance;
            if (game != null) game.KillPlayer();
        }
    }
}
