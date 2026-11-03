using UnityEngine;

namespace Platformer2D
{
    /// <summary>Smooth orthographic follow camera with a little look-ahead in the running direction.</summary>
    [RequireComponent(typeof(Camera))]
    public class PlatformerCamera : MonoBehaviour
    {
        public Transform target;
        public Vector2 offset = new Vector2(0f, 1.5f);
        public float lookAhead = 3f;
        public float smoothTime = 0.15f;

        private Camera cam;
        private PlatformerPlayer player;
        private Vector3 followVelocity;
        private float currentLookAhead;
        private float baseSize;

        private void Start()
        {
            cam = GetComponent<Camera>();
            baseSize = cam.orthographicSize;
            if (target == null)
            {
                var go = GameObject.FindWithTag("Player");
                if (go != null) target = go.transform;
            }
            if (target == null) return;
            player = target.GetComponent<PlatformerPlayer>();
            transform.position = new Vector3(target.position.x + offset.x, target.position.y + offset.y, transform.position.z);
        }

        private void LateUpdate()
        {
            if (target == null) return;

            float runSpeed = player != null ? player.Velocity.x : 0f;
            float desiredLook = Mathf.Clamp(runSpeed / 4f, -1f, 1f) * lookAhead;
            currentLookAhead = Mathf.Lerp(currentLookAhead, desiredLook, 2f * Time.deltaTime);

            // Zoom out a bit when the player is in giant mode.
            float zoom = Mathf.Lerp(1f, target.lossyScale.y, 0.5f);
            if (cam.orthographic)
                cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, baseSize * zoom, 3f * Time.deltaTime);

            Vector3 desired = new Vector3(target.position.x + offset.x + currentLookAhead,
                                          target.position.y + offset.y * zoom,
                                          transform.position.z);
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref followVelocity, smoothTime);
        }
    }
}
