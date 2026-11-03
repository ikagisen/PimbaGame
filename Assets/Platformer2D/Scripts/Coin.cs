using UnityEngine;

namespace Platformer2D
{
    /// <summary>Spinning, bobbing collectible. Needs a trigger collider.</summary>
    public class Coin : MonoBehaviour
    {
        public Transform visual;
        public float spinSpeed = 4f;
        public float bobHeight = 0.12f;

        private Vector3 basePosition;
        private Vector3 visualScale = Vector3.one;
        private float phase;

        private void Start()
        {
            if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
            if (visual != null) visualScale = visual.localScale;
            basePosition = transform.position;
            phase = basePosition.x; // so neighbouring coins don't move in sync
            var game = PlatformerGame.Instance;
            if (game != null) game.RegisterCoin(this);
        }

        private void Update()
        {
            transform.position = basePosition + Vector3.up * (Mathf.Sin(Time.time * 3f + phase) * bobHeight);
            if (visual != null)
            {
                // Fake a 3D spin by squashing the sprite horizontally.
                float spin = Mathf.Abs(Mathf.Cos(Time.time * spinSpeed + phase));
                visual.localScale = new Vector3(visualScale.x * Mathf.Max(0.12f, spin), visualScale.y, visualScale.z);
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (PlatformerPlayer.FromCollider(other) == null) return;
            var game = PlatformerGame.Instance;
            if (game != null) game.CollectCoin();
            gameObject.SetActive(false); // hidden, not destroyed, so Restart can bring it back
        }
    }
}
