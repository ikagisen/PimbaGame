using UnityEngine;
using UnityEngine.UI;

namespace PimbaStage
{
    /// <summary>
    /// 3D version: the 3D parts of the stage (the set, the 3D floor, the 3D obstacles and map pieces, and the stage floor
    /// under them) are drawn by a second camera into a small picture, which is blown up behind everything else, so they
    /// get chunky pixels like the pixel-art characters. The performer, fire, roses, carts, sandbags, bridges and the
    /// finale spotlight stay sharp on top; the menus and the arrow lane are not touched.
    /// The game adds this to the main camera when the 3D scene starts. To change the pixel size, add it to the Main Camera
    /// yourself (Add Component > Pixelated 3D) and set Vertical Pixels; untick it to switch the effect off.
    /// </summary>
    [DefaultExecutionOrder(100)] // after the stage scroller has moved the camera
    [RequireComponent(typeof(Camera))]
    public class Pixelated3D : MonoBehaviour
    {
        public const string LayerName = "Pixelated 3D";

        [Tooltip("How many pixels tall the 3D picture is. 214 makes the 3D pixels the size of the performer's pixels; " +
                 "smaller = chunkier.")]
        [Range(60, 720)] public int verticalPixels = 214;

        /// <summary>The layer the pixelated parts are on (-1 until set up).</summary>
        public static int Layer { get; private set; } = -1;

        private Camera cam, pixelCam;
        private RenderTexture target;
        private Canvas backdrop;
        private RawImage picture;
        private bool ready;

        private void Start()
        {
            cam = GetComponent<Camera>();
            Layer = FindLayer();
            if (Layer < 0)
            {
                Debug.LogWarning("Pimba Stage: no free layer for the pixelated 3D look, so it is off.");
                enabled = false;
                return;
            }
            PutPartsOnLayer();
            MakePixelCamera();
            MakeBackdrop();
            ready = true;
            Apply(true);
            LateUpdate();
        }

        private void OnEnable()
        {
            if (ready) Apply(true);
        }

        private void OnDisable()
        {
            if (ready) Apply(false);
        }

        private void OnDestroy()
        {
            if (pixelCam != null) Destroy(pixelCam.gameObject);
            if (backdrop != null) Destroy(backdrop.gameObject);
            if (target != null)
            {
                target.Release();
                Destroy(target);
            }
        }

        private void Apply(bool on)
        {
            if (cam != null)
            {
                if (on) cam.cullingMask &= ~(1 << Layer);
                else cam.cullingMask |= 1 << Layer;
            }
            if (pixelCam != null) pixelCam.gameObject.SetActive(on);
            if (backdrop != null) backdrop.gameObject.SetActive(on);
        }

        private void LateUpdate()
        {
            if (!ready) return;
            pixelCam.transform.SetPositionAndRotation(transform.position, transform.rotation);
            pixelCam.fieldOfView = cam.fieldOfView; // follows the camera's beat "bop" too
            pixelCam.nearClipPlane = cam.nearClipPlane;
            pixelCam.farClipPlane = cam.farClipPlane;
            EnsureTarget();
            pixelCam.aspect = cam.aspect;
        }

        /// <summary>The small picture, as wide as the game view's shape needs.</summary>
        private void EnsureTarget()
        {
            int height = Mathf.Clamp(verticalPixels, 30, 2160);
            int width = Mathf.Max(1, Mathf.RoundToInt(height * cam.aspect));
            if (target != null && target.width == width && target.height == height) return;
            if (target != null)
            {
                pixelCam.targetTexture = null;
                target.Release();
                Destroy(target);
            }
            target = new RenderTexture(width, height, 24) { name = "Pixelated 3D", filterMode = FilterMode.Point, antiAliasing = 1 };
            target.Create();
            pixelCam.targetTexture = target;
            if (picture != null) picture.texture = target;
        }

        // ------------------------------------------------------------------ setup

        private static int FindLayer()
        {
            int named = LayerMask.NameToLayer(LayerName);
            if (named >= 0) return named;
            for (int i = 31; i >= 8; i--)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            return -1;
        }

        /// <summary>Every 3D model, and the stage floor sprites around them, goes on the pixelated layer.</summary>
        private void PutPartsOnLayer()
        {
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    if (!StaysSharp(r)) r.gameObject.layer = Layer;
        }

        private static bool StaysSharp(Renderer r)
        {
            if (r.gameObject.layer == 5) return true; // UI
            if (r.GetComponentInParent<StagePlayer>(true) != null) return true;
            if (r is MeshRenderer || r is SkinnedMeshRenderer) return false; // every 3D model
            if (!(r is SpriteRenderer)) return true;
            // 2D gameplay pieces stay sharp; the stage floor and the darkness under it are drawn with the 3D
            return r.GetComponentInParent<Rose>(true) != null
                || r.GetComponentInParent<StageHazard>(true) != null
                || r.GetComponentInParent<SwingingSandbag>(true) != null
                || r.GetComponentInParent<MovingSetPiece>(true) != null
                || r.GetComponentInParent<DifficultyFilter>(true) != null
                || r.GetComponentInParent<StageGoal>(true) != null
                || r.name == "Performer Spotlight";
        }

        /// <summary>A copy of the game camera (so it uses the same 3D renderer) that only sees the pixelated layer.</summary>
        private void MakePixelCamera()
        {
            var copy = Instantiate(gameObject, transform.position, transform.rotation);
            copy.name = "Pixel Camera (3D at low resolution)";
            copy.tag = "Untagged";
            foreach (Transform child in copy.transform) Destroy(child.gameObject);
            foreach (var c in copy.GetComponents<Component>())
            {
                if (c is Transform || c is Camera) continue;
                if (c.GetType().Name == "UniversalAdditionalCameraData") continue; // keeps the game camera's 3D renderer
                DestroyImmediate(c);
            }
            pixelCam = copy.GetComponent<Camera>();
            pixelCam.cullingMask = 1 << Layer;
            pixelCam.depth = cam.depth - 1;
            pixelCam.rect = new Rect(0f, 0f, 1f, 1f);
            pixelCam.clearFlags = CameraClearFlags.SolidColor;
            pixelCam.backgroundColor = cam.backgroundColor;
            pixelCam.allowMSAA = false;
            EnsureTarget();
        }

        /// <summary>The small picture blown up to fill the game view, behind all the sprites.</summary>
        private void MakeBackdrop()
        {
            var go = new GameObject("Pixelated 3D (picture)", typeof(RectTransform));
            go.layer = 0; // seen by the game camera, not by the pixel camera
            backdrop = go.AddComponent<Canvas>();
            backdrop.renderMode = RenderMode.ScreenSpaceCamera;
            backdrop.worldCamera = cam;
            backdrop.planeDistance = cam.farClipPlane * 0.9f;
            backdrop.sortingOrder = -30000; // drawn first, so every sprite is in front of it
            var rt = new GameObject("Picture", typeof(RectTransform));
            rt.layer = 0;
            var rect = (RectTransform)rt.transform;
            rect.SetParent(go.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            picture = rt.AddComponent<RawImage>();
            picture.raycastTarget = false;
            picture.texture = target;
        }
    }
}
