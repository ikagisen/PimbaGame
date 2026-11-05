using System.Collections.Generic;
using UnityEngine;

namespace PimbaStage
{
    /// <summary>A piece of a player-made map (on the piece's root).</summary>
    public class MapPieceTag : MonoBehaviour
    {
        public string kind;
        public float size = 1f;
        public bool flip;
        [Tooltip("The piece's traced collider; switched off while the piece is being moved in the map creator.")]
        public GameObject colliderObject;
    }

    /// <summary>
    /// The 3D pieces players build maps from, and putting a player-made map on the stage (3D version).
    /// The pieces are copies of the 3D obstacles already on the 3D stage (speakers, TV, staircase, block), with their
    /// traced colliders, so they work exactly like the original obstacles. A player-made map replaces the whole layout:
    /// the original obstacles, fire, sandbags, carts, bridges and roses go, and the trapdoor gaps get floor.
    /// </summary>
    public class MapStage : MonoBehaviour
    {
        public class Kind
        {
            public string id, label;
            public GameObject template;
            public float halfWidth, height;
            public Texture thumbnail;
        }

        // id, label, model in Scene_test02.fbx, obstacle it is copied from
        private static readonly string[,] Pieces =
        {
            { "SpeakerA", "SPEAKER", "CAIXA DE SOM", "Speaker" },
            { "SpeakerB", "SPEAKER 2", "CAIXA DE SOM.002", "Speaker" },
            { "TV", "TV", "Cylinder.002", "TV" },
            { "Stairs", "STAIRS", "Plane.009", "Stairs" },
            { "Block", "BLOCK", "Cube.005", "Block" },
        };

        public const float MaxPieceTop = 8f;
        public const int MaxPieces = 400;

        public readonly List<Kind> Kinds = new List<Kind>();
        public bool Available => Kinds.Count > 0;
        /// <summary>True once the original layout has been cleared for a player-made map.</summary>
        public bool CustomApplied { get; private set; }
        public Vector2 SpawnPoint { get; private set; } = new Vector2(2f, 0.8f);
        public float GoalX { get; private set; } = 138f;
        /// <summary>Pieces go between the start and the finale spotlight.</summary>
        public float MinPieceX => SpawnPoint.x + 1.5f;
        public float MaxPieceX => GoalX - 2f;
        public int PieceCount => pieces != null ? pieces.childCount : 0;

        private Transform templates, pieces;
        private GameObject originalObstacles;

        public void Init(StagePlayer player)
        {
            if (templates != null) return;
            if (player != null) SpawnPoint = player.transform.position;
            var goal = FindAnyObjectByType<StageGoal>();
            if (goal != null) GoalX = goal.transform.position.x;

            foreach (var root in gameObject.scene.GetRootGameObjects())
                if (root.activeSelf && root.name.StartsWith("3D Obstacles")) { originalObstacles = root; break; }
            if (originalObstacles == null) return; // the 2D version has no 3D pieces

            var holder = new GameObject("Map Pieces (templates)");
            holder.SetActive(false);
            templates = holder.transform;
            pieces = new GameObject("Map Pieces").transform;

            for (int i = 0; i < Pieces.GetLength(0); i++)
            {
                var source = FindObstacle(Pieces[i, 3], Pieces[i, 2]);
                if (source == null)
                {
                    Debug.LogWarning("Pimba Stage: no '" + Pieces[i, 3] + "' obstacle with " + Pieces[i, 2] + " in the 3D scene, so the map creator has no " + Pieces[i, 1] + ".");
                    continue;
                }
                var kind = MakeTemplate(Pieces[i, 0], Pieces[i, 1], Pieces[i, 2], source);
                if (kind != null) Kinds.Add(kind);
            }
        }

        public Kind Find(string id)
        {
            foreach (var k in Kinds) if (k.id == id) return k;
            return null;
        }

        private Transform FindObstacle(string kind, string model)
        {
            string prefix = "Obstacle: " + kind + " (";
            foreach (Transform obstacle in originalObstacles.transform)
            {
                if (!obstacle.name.StartsWith(prefix)) continue;
                var piece = obstacle.Find(model);
                if (piece != null && piece.GetComponentInChildren<Renderer>(true) != null) return obstacle;
            }
            return null;
        }

        private Kind MakeTemplate(string id, string label, string model, Transform source)
        {
            var copy = Instantiate(source.gameObject, source.parent);
            copy.SetActive(true); // measured while shown, even if the difficulty hides the original right now
            var modelT = copy.transform.Find(model);
            var colliderT = copy.transform.Find("Collider (" + model + ")");
            if (modelT == null) { Destroy(copy); return null; }
            Bounds b = RendererBounds(modelT.gameObject);

            // The piece's root sits at its bottom centre, so a map stores where it stands.
            var root = new GameObject("Map Piece: " + label).transform;
            root.SetParent(templates, false);
            root.position = new Vector3(b.center.x, b.min.y, 0f);
            modelT.SetParent(root, true);
            if (colliderT != null) colliderT.SetParent(root, true);
            copy.SetActive(false);
            Destroy(copy); // ropes and anything else

            var tag = root.gameObject.AddComponent<MapPieceTag>();
            tag.kind = id;
            tag.colliderObject = colliderT != null ? colliderT.gameObject : null;
            return new Kind { id = id, label = label, template = root.gameObject, halfWidth = b.extents.x, height = b.size.y };
        }

        // ------------------------------------------------------------------ the stage

        /// <summary>Takes the original layout away: obstacles, hazards, carts, bridges and roses; the trapdoors get floor.</summary>
        public void ClearOriginalLayout()
        {
            if (CustomApplied || !Available) return;
            CustomApplied = true;
            originalObstacles.SetActive(false);
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                foreach (var c in root.GetComponentsInChildren<Rose>(true)) c.gameObject.SetActive(false);
                foreach (var c in root.GetComponentsInChildren<MovingSetPiece>(true)) c.gameObject.SetActive(false);
                foreach (var c in root.GetComponentsInChildren<SwingingSandbag>(true)) c.gameObject.SetActive(false);
                foreach (var c in root.GetComponentsInChildren<StageHazard>(true)) c.gameObject.SetActive(false);
                foreach (var c in root.GetComponentsInChildren<DifficultyFilter>(true)) c.gameObject.SetActive(false);
            }
            CoverTrapdoors();
        }

        /// <summary>One floor from the first floor piece to the last, instead of the pieces with trapdoor gaps.</summary>
        private void CoverTrapdoors()
        {
            var floors = new List<BoxCollider2D>();
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var box in root.GetComponentsInChildren<BoxCollider2D>())
                    if (box.gameObject.name == "Stage Floor") floors.Add(box);
            if (floors.Count < 2) return;

            float min = float.MaxValue, max = float.MinValue;
            foreach (var f in floors)
            {
                min = Mathf.Min(min, f.bounds.min.x);
                max = Mathf.Max(max, f.bounds.max.x);
            }
            var first = floors[0];
            var whole = Instantiate(first.gameObject, first.transform.parent);
            whole.name = "Stage Floor (whole, player map)";
            float scaleX = Mathf.Max(0.0001f, Mathf.Abs(first.transform.lossyScale.x));
            float width = (max - min) / scaleX;
            float k = width / Mathf.Max(0.0001f, first.size.x);
            var t = whole.transform;
            t.position = new Vector3((min + max) * 0.5f, t.position.y, t.position.z);
            var box2 = whole.GetComponent<BoxCollider2D>();
            box2.size = new Vector2(width, box2.size.y);
            box2.offset = new Vector2(0f, box2.offset.y);
            foreach (var sr in whole.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr.drawMode != SpriteDrawMode.Simple) sr.size = new Vector2(sr.size.x * k, sr.size.y);
                else if (sr.transform != t)
                {
                    var s = sr.transform.localScale;
                    sr.transform.localScale = new Vector3(s.x * k, s.y, s.z);
                }
            }
            foreach (var f in floors) f.gameObject.SetActive(false);
        }

        /// <summary>Puts a player-made map on the stage (after taking the original layout away).</summary>
        public void ApplyLayout(StageMap map)
        {
            ClearOriginalLayout();
            ClearPieces();
            if (map != null)
                foreach (var p in map.pieces)
                    Spawn(p.kind, p.x, p.y, p.size, p.flip, true, null);
            var bounce = FindAnyObjectByType<BeatSquashStretch>();
            if (bounce != null) bounce.Collect(); // the new speakers bounce on the beat too
        }

        public void ClearPieces()
        {
            if (pieces == null) return;
            foreach (Transform t in pieces)
            {
                t.gameObject.SetActive(false);
                Destroy(t.gameObject);
            }
        }

        /// <summary>A copy of a piece. solid = false: no collider (while it follows the mouse in the map creator).</summary>
        public MapPieceTag Spawn(string id, float x, float y, float size, bool flip, bool solid, Transform parent)
        {
            var kind = Find(id);
            if (kind == null) return null;
            var go = Instantiate(kind.template, new Vector3(x, y, 0f), Quaternion.identity, parent != null ? parent : pieces);
            go.name = kind.label;
            var tag = go.GetComponent<MapPieceTag>();
            if (tag.colliderObject != null) tag.colliderObject.SetActive(solid);
            Pose(tag, x, y, size, flip);
            return tag;
        }

        public static void Pose(MapPieceTag tag, float x, float y, float size, bool flip)
        {
            tag.size = size;
            tag.flip = flip;
            tag.transform.position = new Vector3(x, y, 0f);
            tag.transform.localScale = new Vector3(flip ? -size : size, size, size);
        }

        /// <summary>The placed piece under a point on the stage (the last one placed wins), or null.</summary>
        public MapPieceTag PieceAt(Vector2 point)
        {
            if (pieces == null) return null;
            for (int i = pieces.childCount - 1; i >= 0; i--)
            {
                var t = pieces.GetChild(i);
                if (!t.gameObject.activeSelf) continue;
                Bounds b = RendererBounds(t.gameObject);
                if (point.x >= b.min.x && point.x <= b.max.x && point.y >= b.min.y && point.y <= b.max.y)
                    return t.GetComponent<MapPieceTag>();
            }
            return null;
        }

        /// <summary>The pieces on the stage as a map.</summary>
        public StageMap ToMap(string name)
        {
            var map = new StageMap { name = name };
            if (pieces == null) return map;
            foreach (Transform t in pieces)
            {
                var tag = t.GetComponent<MapPieceTag>();
                if (tag == null || !t.gameObject.activeSelf) continue;
                Vector3 p = t.position;
                map.pieces.Add(new MapPiece { kind = tag.kind, x = Round(p.x), y = Round(p.y), size = tag.size, flip = tag.flip });
            }
            return map;
        }

        private static float Round(float v) => Mathf.Round(v * 100f) / 100f;

        public static Bounds RendererBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        // ------------------------------------------------------------------ pictures for the object list

        /// <summary>Renders a small picture of each piece (with the game camera, far away from the stage).</summary>
        public void RenderThumbnails(Camera cam)
        {
            if (cam == null) return;
            foreach (var kind in Kinds)
            {
                if (kind.thumbnail != null) continue;
                var go = Instantiate(kind.template, templates); // stays hidden under the template holder for now
                var tag = go.GetComponent<MapPieceTag>();
                if (tag.colliderObject != null) DestroyImmediate(tag.colliderObject);
                go.transform.SetParent(null, false);
                go.transform.position = new Vector3(-3000f, -3000f, 0f);
                go.transform.localScale = Vector3.one;

                var rt = new RenderTexture(192, 192, 24) { name = "Map piece " + kind.label };
                rt.Create();
                Bounds b = RendererBounds(go);
                float size = Mathf.Max(b.size.x, b.size.y);

                var t = cam.transform;
                Vector3 pos = t.position;
                Quaternion rot = t.rotation;
                float fov = cam.fieldOfView;
                Rect rect = cam.rect;
                var target = cam.targetTexture;
                int mask = cam.cullingMask;
                try
                {
                    cam.rect = new Rect(0f, 0f, 1f, 1f);
                    cam.fieldOfView = 30f;
                    cam.cullingMask = 0;
                    foreach (var r in go.GetComponentsInChildren<Renderer>()) cam.cullingMask |= 1 << r.gameObject.layer; // just the piece
                    t.rotation = Quaternion.Euler(12f, -24f, 0f);
                    float distance = size * 0.5f / Mathf.Tan(15f * Mathf.Deg2Rad) * 1.3f + b.extents.z;
                    t.position = b.center - t.forward * distance;
                    cam.targetTexture = rt;
                    cam.Render();
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("Pimba Stage: could not draw the picture of " + kind.label + ": " + e.Message);
                }
                finally
                {
                    cam.targetTexture = target;
                    cam.cullingMask = mask;
                    cam.rect = rect;
                    cam.fieldOfView = fov;
                    t.SetPositionAndRotation(pos, rot);
                    go.SetActive(false);
                    Destroy(go);
                }
                kind.thumbnail = rt;
            }
        }
    }
}
