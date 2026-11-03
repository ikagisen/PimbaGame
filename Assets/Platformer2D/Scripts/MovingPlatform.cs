using UnityEngine;

namespace Platformer2D
{
    /// <summary>
    /// Kinematic platform that eases back and forth. The player reads <see cref="Velocity"/>
    /// to ride along with it.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class MovingPlatform : MonoBehaviour
    {
        [Tooltip("Offset from the start position to the far end of the path.")]
        public Vector2 travel = new Vector2(8f, 0f);
        public float speed = 2.5f;

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
            float t = Mathf.SmoothStep(0f, 1f, Mathf.PingPong(time * speed / distance, 1f));
            Vector2 next = start + travel * t;
            Velocity = (next - rb.position) / Time.fixedDeltaTime;
            rb.MovePosition(next);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 from = Application.isPlaying ? (Vector3)start : transform.position;
            Vector3 to = from + (Vector3)travel;
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(from, to);
            Gizmos.DrawWireCube(to, transform.lossyScale);
        }
    }
}
