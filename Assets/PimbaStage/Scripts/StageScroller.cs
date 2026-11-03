using System;
using System.Collections.Generic;
using UnityEngine;

namespace PimbaStage
{
    /// <summary>
    /// Moves the stage view forward when Player 2 hits arrows, and keeps Player 1 inside the view:
    /// the left edge pushes the performer along, the right curtain stops them running ahead.
    /// Getting squashed against a wall or falling through a trapdoor raises PlayerOut.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class StageScroller : MonoBehaviour
    {
        public Camera cam;
        public StagePlayer player;

        [Header("Stage length (camera X)")]
        public float startX = 6.5f;
        public float endX = 141f;

        [Header("Scrolling")]
        [Tooltip("Fastest the stage can move (units per second).")]
        public float maxScrollSpeed = 6f;
        [Tooltip("Accuracy needed to reach the end exactly when the song ends (lower = easier).")]
        [Range(0.3f, 1f)] public float targetAccuracy = 0.8f;

        [Header("View")]
        [Tooltip("Part of the view hidden by the curtains (0-0.5 of the width).")]
        [Range(0f, 0.5f)] public float leftInset = 0.07f;
        [Range(0f, 0.5f)] public float rightInset = 0.07f;
        public float killY = -4f;
        [Tooltip("Camera zoom 'bop' on every bar, in degrees of field of view.")]
        public float bopDegrees = 0.7f;

        public event Action PlayerOut;
        public float ScrollSpeed { get; private set; }
        public float Progress => Mathf.InverseLerp(startX, endX, cam.transform.position.x);

        private float targetX, distancePerNote = 1f, baseFov, bop;
        private ContactFilter2D solidFilter, anyFilter;
        private readonly List<RaycastHit2D> hits = new List<RaycastHit2D>(8);
        private readonly List<Collider2D> overlaps = new List<Collider2D>(8);

        private void Awake()
        {
            if (cam == null) cam = GetComponent<Camera>();
            baseFov = cam.fieldOfView;
            solidFilter = new ContactFilter2D { useTriggers = false };
            solidFilter.SetLayerMask(Physics2D.DefaultRaycastLayers);
            anyFilter = new ContactFilter2D { useTriggers = true };
            anyFilter.SetLayerMask(Physics2D.DefaultRaycastLayers);
            targetX = startX;
        }

        /// <summary>Spreads the stage length over the song's arrows.</summary>
        public void Setup(int noteCount)
        {
            distancePerNote = (endX - startX) / Mathf.Max(1f, noteCount * targetAccuracy);
            targetX = startX;
            Vector3 p = cam.transform.position;
            cam.transform.position = new Vector3(startX, p.y, p.z);
        }

        public void Advance(Judgement j)
        {
            float factor;
            switch (j)
            {
                case Judgement.Amazing: factor = 1f; break;
                case Judgement.Good: factor = 0.85f; break;
                case Judgement.OK: factor = 0.6f; break;
                case Judgement.Bad: factor = 0.2f; break;
                default: factor = 0f; break;
            }
            targetX = Mathf.Min(endX, targetX + distancePerNote * factor);
        }

        public void Bop() => bop = bopDegrees;

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 p = cam.transform.position;
            float gap = targetX - p.x;
            float speed = gap > 0f ? Mathf.Min(maxScrollSpeed, gap * 2.5f + 0.3f) : 0f;
            float x = Mathf.MoveTowards(p.x, targetX, speed * dt);
            ScrollSpeed = (x - p.x) / dt;
            cam.transform.position = new Vector3(x, p.y, p.z);

            bop = Mathf.MoveTowards(bop, 0f, bopDegrees * 4f * dt);
            cam.fieldOfView = baseFov - bop;
        }

        private void FixedUpdate()
        {
            if (player == null || !player.isActiveAndEnabled) return;

            var body = player.Body;
            Vector2 pos = body.position;
            float halfWidth = player.HalfWidth;
            float left = ViewX(leftInset) + halfWidth;
            float right = ViewX(1f - rightInset) - halfWidth;

            if (pos.x < left)
            {
                if (player.IsBlockedRight(left - pos.x + 0.02f))
                {
                    PlayerOut?.Invoke(); // squashed between the stage edge and a wall
                    return;
                }
                pos.x = left;
                body.position = pos;
                Vector2 v = body.linearVelocity;
                v.x = Mathf.Max(v.x, ScrollSpeed);
                body.linearVelocity = v;
            }
            else if (pos.x > right)
            {
                pos.x = right;
                body.position = pos;
                Vector2 v = body.linearVelocity;
                v.x = Mathf.Min(v.x, 0f);
                body.linearVelocity = v;
            }

            if (pos.y < killY) PlayerOut?.Invoke();
        }

        /// <summary>World X at a horizontal viewport position, on the gameplay plane (z = 0).</summary>
        public float ViewX(float viewportX)
        {
            float distance = -cam.transform.position.z;
            return cam.ViewportToWorldPoint(new Vector3(viewportX, 0.5f, distance)).x;
        }

        /// <summary>Finds solid ground inside the view, away from hazards, to drop the performer back in.</summary>
        public Vector2 FindSafeSpot()
        {
            float left = ViewX(leftInset) + 1.2f;
            float right = ViewX(1f - rightInset) - 1.5f;
            float start = Mathf.Lerp(left, right, 0.3f);
            float step = 0.5f;

            for (int i = 0; i < 60; i++)
            {
                float offset = ((i + 1) / 2) * step * (i % 2 == 0 ? 1f : -1f);
                float x = start + offset;
                if (x < left || x > right) continue;

                int count = Physics2D.Raycast(new Vector2(x, 9f), Vector2.down, solidFilter, hits, 14f);
                if (count == 0) continue;
                RaycastHit2D best = hits[0];
                for (int h = 1; h < count; h++) if (hits[h].distance < best.distance) best = hits[h];

                if (best.normal.y < 0.7f || best.point.y > 6.5f) continue;
                if (best.collider.GetComponentInParent<MovingSetPiece>() != null) continue;
                Vector2 spot = best.point + Vector2.up * 0.9f;
                if (HazardNear(spot, 1.4f)) continue;
                return spot;
            }
            return new Vector2(start, 3f);
        }

        private bool HazardNear(Vector2 point, float radius)
        {
            int count = Physics2D.OverlapCircle(point, radius, anyFilter, overlaps);
            for (int i = 0; i < count; i++)
                if (overlaps[i].GetComponentInParent<StageHazard>() != null) return true;
            return false;
        }
    }
}
