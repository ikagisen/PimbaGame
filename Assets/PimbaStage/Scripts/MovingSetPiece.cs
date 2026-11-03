using UnityEngine;

namespace PimbaStage
{
    /// <summary>Set piece on rails (cart or elevator) that eases back and forth. The performer rides along.</summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class MovingSetPiece : MonoBehaviour
    {
        [Tooltip("Offset from the start position to the far end of the path.")]
        public Vector2 travel = new Vector2(4f, 0f);
        public float speed = 2f;
        [Range(0f, 1f)] public float startPhase;

        public Vector2 Velocity { get; private set; }

        private Rigidbody2D rb;
        private Vector2 start;
        private float time;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            start = rb.position;
        }

        private void FixedUpdate()
        {
            float distance = travel.magnitude;
            if (distance < 0.001f) return;
            time += Time.fixedDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.PingPong(time * speed / distance + startPhase * 2f, 1f));
            Vector2 next = start + travel * t;
            Velocity = (next - rb.position) / Time.fixedDeltaTime;
            rb.MovePosition(next);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 from = Application.isPlaying ? (Vector3)start : transform.position;
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(from, from + (Vector3)travel);
        }
    }
}
