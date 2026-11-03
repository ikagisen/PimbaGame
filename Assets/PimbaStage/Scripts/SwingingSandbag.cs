using UnityEngine;

namespace PimbaStage
{
    /// <summary>A sandbag swinging from the fly loft. Put it on the pivot; the bag child carries the hazard.</summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class SwingingSandbag : MonoBehaviour
    {
        public float amplitude = 55f;
        public float period = 2.6f;
        [Range(0f, 1f)] public float phase;

        private Rigidbody2D rb;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        private void FixedUpdate()
        {
            float angle = amplitude * Mathf.Sin((Time.time / period + phase) * Mathf.PI * 2f);
            rb.MoveRotation(angle);
        }
    }
}
