using UnityEngine;

namespace Platformer2D
{
    /// <summary>Finishes the level when the player reaches it. Needs a trigger collider.</summary>
    public class GoalFlag : MonoBehaviour
    {
        public Transform flag;
        public float waveSpeed = 5f;

        private Vector3 flagScale = Vector3.one;

        private void Start()
        {
            if (flag != null) flagScale = flag.localScale;
        }

        private void Update()
        {
            if (flag == null) return;
            float wave = 0.85f + 0.15f * Mathf.Sin(Time.time * waveSpeed);
            flag.localScale = new Vector3(flagScale.x * wave, flagScale.y, flagScale.z);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (PlatformerPlayer.FromCollider(other) == null) return;
            var game = PlatformerGame.Instance;
            if (game != null) game.Win();
        }
    }
}
