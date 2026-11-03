using UnityEngine;

namespace PimbaStage
{
    /// <summary>A rose thrown on stage by the audience. Worth bonus points. Needs a trigger collider.</summary>
    public class Rose : MonoBehaviour
    {
        public Transform visual;

        private Vector3 basePosition;
        private float phase;

        private void Start()
        {
            basePosition = transform.position;
            phase = basePosition.x;
            var game = StageGame.Instance;
            if (game != null) game.RegisterRose();
        }

        private void Update()
        {
            transform.position = basePosition + Vector3.up * (Mathf.Sin(Time.time * 3f + phase) * 0.12f);
            if (visual != null) visual.Rotate(0f, 120f * Time.deltaTime, 0f, Space.Self);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.GetComponentInParent<StagePlayer>() == null) return;
            var game = StageGame.Instance;
            if (game != null) game.CollectRose();
            gameObject.SetActive(false);
        }
    }
}
