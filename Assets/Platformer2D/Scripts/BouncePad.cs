using UnityEngine;

namespace Platformer2D
{
    /// <summary>Launches the player upward on touch. Needs a trigger collider.</summary>
    public class BouncePad : MonoBehaviour
    {
        public float bounceSpeed = 23f;
        public Transform visual;

        private Vector3 baseScale = Vector3.one;

        private void Start()
        {
            if (visual != null) baseScale = visual.localScale;
        }

        private void Update()
        {
            if (visual != null) visual.localScale = Vector3.Lerp(visual.localScale, baseScale, 10f * Time.deltaTime);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var player = PlatformerPlayer.FromCollider(other);
            if (player == null) return;
            player.Bounce(bounceSpeed);
            if (visual != null) visual.localScale = new Vector3(baseScale.x * 1.25f, baseScale.y * 0.4f, baseScale.z);
        }
    }
}
