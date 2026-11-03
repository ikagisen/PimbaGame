using UnityEngine;

namespace Platformer2D
{
    /// <summary>Moves a background layer by a fraction of the camera's movement (0 = fixed in world, 1 = stuck to camera).</summary>
    [DefaultExecutionOrder(100)] // after the camera has moved this frame
    public class Parallax : MonoBehaviour
    {
        [Range(0f, 1f)] public float factor = 0.5f;

        private Transform cam;
        private Vector3 startPosition, cameraStart;

        private void Start()
        {
            if (Camera.main == null) return;
            cam = Camera.main.transform;
            startPosition = transform.position;
            cameraStart = cam.position;
        }

        private void LateUpdate()
        {
            if (cam == null) return;
            Vector3 delta = cam.position - cameraStart;
            transform.position = startPosition + new Vector3(delta.x * factor, delta.y * factor, 0f);
        }
    }
}
