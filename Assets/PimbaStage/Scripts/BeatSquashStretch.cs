using System.Collections.Generic;
using UnityEngine;

namespace PimbaStage
{
    /// <summary>
    /// Squashes and stretches every object whose name starts with namePrefix (the "CAIXA DE SOM" speakers) on each beat of
    /// the song: short and wide on the beat, then a springy tall-and-thin rebound, then still until the next beat.
    /// Runs off the song clock, so it stays exactly on the beat, freezes while paused and rests while the menu is open.
    /// Speakers stay standing on whatever they stand on, and a stack of speakers squashes as one stack.
    /// The obstacles' colliders are separate objects ("Collider (...)"), so the bounce never moves what the player stands on.
    /// </summary>
    public class BeatSquashStretch : MonoBehaviour
    {
        public Conductor conductor;
        [Tooltip("Everything in this scene whose name starts with this bounces.")]
        public string namePrefix = "CAIXA DE SOM";
        [Tooltip("How much they squash on the beat (0.15 = 15% shorter, and a bit wider).")]
        [Range(0f, 0.5f)] public float amount = 0.15f;
        [Tooltip("Bounce every N beats (1 = every beat).")]
        [Min(1)] public int everyNBeats = 1;
        [Tooltip("Shape over one beat: -1 = fully squashed (short and wide), +1 = fully stretched (tall and thin). " +
                 "Time 0 is the beat; it should end where it starts.")]
        public AnimationCurve shape = DefaultShape();

        private class Speaker
        {
            public Transform t;
            public int upAxis;                 // the local axis that points up in the world
            public Vector3 baseScale, baseLocalPosition;
            public Vector3 fromAnchor;         // pivot minus the stack's anchor (bottom centre), in world units
            public Vector3 writtenScale, writtenLocalPosition;
            public bool wrote;
            public float minX, maxX, minY, maxY;
            public int group;
        }

        private readonly List<Speaker> speakers = new List<Speaker>();
        private float level;

        public static AnimationCurve DefaultShape()
        {
            return new AnimationCurve(
                new Keyframe(0f, -1f, -16f, 22f),   // the beat: squashed flat, springing back up
                new Keyframe(0.14f, 0.55f, 0f, 0f), // stretched tall
                new Keyframe(0.3f, -0.2f, 0f, 0f),  // a smaller squash
                new Keyframe(0.45f, 0.06f, 0f, 0f),
                new Keyframe(0.6f, 0f, 0f, 0f),     // still
                new Keyframe(0.88f, 0f, 0f, 0f),
                new Keyframe(1f, -1f, -16f, 22f));  // dropping into the next beat's squash
        }

        private void Start()
        {
            if (conductor == null) conductor = FindAnyObjectByType<Conductor>();
            Collect();
        }

        /// <summary>Finds the speakers again (call it after adding or removing some while playing).</summary>
        public void Collect()
        {
            Restore();
            speakers.Clear();
            if (string.IsNullOrEmpty(namePrefix)) return;

            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                if (!root.activeSelf) continue; // e.g. the hidden copy of the model the 3D obstacles were cut from
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.name.StartsWith(namePrefix) || InsideAnother(t)) continue;
                    var sp = new Speaker { t = t, baseScale = t.localScale, baseLocalPosition = t.localPosition };
                    if (!MeshBounds(t, out Bounds b)) continue;
                    sp.minX = b.min.x; sp.maxX = b.max.x; sp.minY = b.min.y; sp.maxY = b.max.y;
                    Vector3 up = t.InverseTransformDirection(Vector3.up);
                    sp.upAxis = Mathf.Abs(up.x) > Mathf.Abs(up.y) ? (Mathf.Abs(up.x) > Mathf.Abs(up.z) ? 0 : 2) : (Mathf.Abs(up.y) > Mathf.Abs(up.z) ? 1 : 2);
                    sp.group = speakers.Count;
                    speakers.Add(sp);
                }
            }

            // Speakers with the same parent that stand on, or overlap, each other bounce together (a stack, or parts of one).
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < speakers.Count; i++)
                    for (int j = i + 1; j < speakers.Count; j++)
                    {
                        Speaker a = speakers[i], c = speakers[j];
                        if (a.group == c.group || a.t.parent != c.t.parent) continue;
                        float overlapX = Mathf.Min(a.maxX, c.maxX) - Mathf.Max(a.minX, c.minX);
                        float narrower = Mathf.Min(a.maxX - a.minX, c.maxX - c.minX);
                        float gap = 0.1f * Mathf.Min(a.maxY - a.minY, c.maxY - c.minY);
                        bool touchY = c.minY <= a.maxY + gap && c.maxY >= a.minY - gap;
                        if (overlapX > 0.5f * narrower && touchY)
                        {
                            int from = c.group, to = a.group;
                            foreach (var s in speakers) if (s.group == from) s.group = to;
                            merged = true;
                        }
                    }
            }

            // Each group squashes towards its bottom centre, so it stays on the floor (or on top of the speaker below).
            foreach (var sp in speakers)
            {
                float minX = float.MaxValue, maxX = float.MinValue, ground = float.MaxValue;
                foreach (var s in speakers)
                    if (s.group == sp.group) { minX = Mathf.Min(minX, s.minX); maxX = Mathf.Max(maxX, s.maxX); ground = Mathf.Min(ground, s.minY); }
                Vector3 pivot = sp.t.position;
                sp.fromAnchor = pivot - new Vector3((minX + maxX) * 0.5f, ground, pivot.z);
            }
        }

        private void LateUpdate()
        {
            bool playing = conductor != null && conductor.IsRunning;
            level = Mathf.MoveTowards(level, playing ? 1f : 0f, Time.unscaledDeltaTime * 3f);
            float s = 0f;
            if (level > 0f && conductor != null)
            {
                double beats = conductor.Beat / everyNBeats;
                float phase = (float)(beats - System.Math.Floor(beats));
                s = amount * level * shape.Evaluate(phase);
            }
            foreach (var sp in speakers) Apply(sp, s);
        }

        private static void Apply(Speaker sp, float s)
        {
            var t = sp.t;
            if (t == null || !t.gameObject.activeInHierarchy) return;

            // If something else moved it since last frame (the model's own animation, an edit in the Inspector), that's the new rest pose.
            if (sp.wrote && t.localScale != sp.writtenScale) sp.baseScale = t.localScale;
            if (sp.wrote && t.localPosition != sp.writtenLocalPosition) sp.baseLocalPosition = t.localPosition;

            float up = 1f + s;
            float side = 1f / Mathf.Sqrt(Mathf.Max(0.05f, up)); // about the same volume: shorter means wider
            Vector3 scale = sp.baseScale * side;
            scale[sp.upAxis] = sp.baseScale[sp.upAxis] * up;
            Vector3 move = new Vector3(sp.fromAnchor.x * (side - 1f), sp.fromAnchor.y * (up - 1f), 0f);
            t.localScale = scale;
            t.localPosition = sp.baseLocalPosition + (t.parent != null ? t.parent.InverseTransformVector(move) : move);
            sp.writtenScale = t.localScale;
            sp.writtenLocalPosition = t.localPosition;
            sp.wrote = true;
        }

        private void OnDisable() => Restore();

        private void Restore()
        {
            foreach (var sp in speakers)
            {
                if (sp.t == null || !sp.wrote) continue;
                sp.t.localScale = sp.baseScale;
                sp.t.localPosition = sp.baseLocalPosition;
                sp.writtenScale = sp.baseScale;
                sp.writtenLocalPosition = sp.baseLocalPosition;
            }
        }

        private bool InsideAnother(Transform t)
        {
            for (var p = t.parent; p != null; p = p.parent)
                if (p.name.StartsWith(namePrefix)) return true; // the outer one already squashes it
            return false;
        }

        /// <summary>World bounds of the meshes under t, worked out from the meshes so it also works for hidden ones.</summary>
        private static bool MeshBounds(Transform t, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (var filter in t.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                Bounds m = filter.sharedMesh.bounds;
                var matrix = filter.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = matrix.MultiplyPoint3x4(m.center + Vector3.Scale(m.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { bounds = new Bounds(corner, Vector3.zero); any = true; }
                    else bounds.Encapsulate(corner);
                }
            }
            return any;
        }
    }
}
