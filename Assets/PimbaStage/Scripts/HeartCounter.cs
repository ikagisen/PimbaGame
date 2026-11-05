using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PimbaStage
{
    /// <summary>
    /// Player 1's heart counter: a box with the yellow heart and a rolling number (UI/HeartCounter_0..11.png).
    /// Frame 0 shows 3, frame 4 shows 2, frame 8 shows 1; the frames in between are the number reel spinning.
    /// When a heart is lost the reel spins to the next number and the heart gives a little jolt.
    /// </summary>
    public class HeartCounter : MonoBehaviour
    {
        [Tooltip("The box (shows the number).")]
        public Image panel;
        [Tooltip("The yellow heart inside the box.")]
        public Image heart;
        public Sprite[] frames = new Sprite[0];
        [Tooltip("Highest number the frames show (the first frame).")]
        public int maxNumber = 3;
        [Tooltip("Frames per number: the number itself plus the reel spinning to the next one.")]
        public int framesPerNumber = 4;
        [Tooltip("Frame shown when no hearts are left (the empty reel).")]
        public int emptyFrame = 10;
        [Tooltip("Seconds per frame of the reel spin (the GIF uses 130 ms).")]
        public float frameTime = 0.13f;
        [Tooltip("Heart colour when no hearts are left.")]
        public Color emptyHeartColor = new Color(0.3f, 0.3f, 0.3f, 1f);

        private readonly Queue<int> queue = new Queue<int>();
        private int shown = -1;
        private float frameTimer, bumpTime = 99f;
        private Vector2 panelHome;
        private bool homeSet;

        /// <summary>Shows how many hearts are left. Fewer than before = the reel spins down; otherwise it jumps straight there.</summary>
        public void Show(int hearts)
        {
            hearts = Mathf.Clamp(hearts, 0, maxNumber);
            if (shown < 0 || hearts > shown)
            {
                queue.Clear();
                SetFrame(FrameFor(hearts));
                if (heart != null) heart.color = hearts > 0 ? Color.white : emptyHeartColor;
                shown = hearts;
                return;
            }
            if (hearts == shown) return;

            for (int n = shown; n > hearts; n--)
            {
                int start = (maxNumber - n) * framesPerNumber;
                if (n - 1 > 0)
                {
                    for (int k = 1; k < framesPerNumber; k++) queue.Enqueue(start + k);
                    queue.Enqueue(FrameFor(n - 1));
                }
                else
                {
                    queue.Enqueue(start + 1);   // last heart: the reel turns over and stays empty
                    queue.Enqueue(emptyFrame);
                }
            }
            shown = hearts;
            bumpTime = 0f;
            frameTimer = 0f;
        }

        private int FrameFor(int hearts) => hearts > 0 ? (maxNumber - hearts) * framesPerNumber : emptyFrame;

        private void SetFrame(int index)
        {
            if (panel != null && frames != null && index >= 0 && index < frames.Length && frames[index] != null)
                panel.sprite = frames[index];
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (queue.Count > 0)
            {
                frameTimer -= dt;
                if (frameTimer <= 0f)
                {
                    SetFrame(queue.Dequeue());
                    frameTimer += frameTime;
                    if (queue.Count == 0 && shown == 0 && heart != null) heart.color = emptyHeartColor;
                }
            }

            // Jolt when hit: the heart pops and the box shakes for a moment.
            bumpTime += dt;
            float k = Mathf.Clamp01(1f - bumpTime / 0.35f);
            if (heart != null) heart.rectTransform.localScale = Vector3.one * (1f + 0.35f * k * k);
            if (panel != null)
            {
                var rt = panel.rectTransform;
                if (!homeSet) { panelHome = rt.anchoredPosition; homeSet = true; }
                rt.anchoredPosition = panelHome + (k > 0f ? new Vector2(Mathf.Sin(bumpTime * 90f), Mathf.Cos(bumpTime * 70f)) * 4f * k : Vector2.zero);
            }
        }
    }
}
