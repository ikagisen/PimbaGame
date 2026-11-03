using System;
using UnityEngine;
using UnityEngine.UI;

namespace PimbaStage
{
    /// <summary>
    /// Plays a frame-by-frame sprite animation (the character GIFs) on a SpriteRenderer or a UI Image.
    /// With framesPerBeat set, the animation locks to the music while a song is playing.
    /// Call Play() to switch to another animation (the performer's idle / walk / jump / fall / hurt).
    /// </summary>
    public class SpriteFrameAnimator : MonoBehaviour
    {
        public Sprite[] frames = new Sprite[0];
        [Tooltip("Speed when not locked to the music (the GIFs use 130 ms per frame = 7.7 fps).")]
        public float framesPerSecond = 7.7f;
        [Tooltip("While a song plays, show this many frames per beat (0 = ignore the music).")]
        public int framesPerBeat;
        public Conductor conductor;

        public float SpeedMultiplier { get; set; } = 1f;

        private SpriteRenderer spriteRenderer;
        private Image image;
        private float time;
        private int shown = -1;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            image = GetComponent<Image>();
        }

        private void Update()
        {
            if (frames == null || frames.Length == 0) return;

            int index;
            if (framesPerBeat > 0 && conductor != null && conductor.IsRunning && conductor.SongTime >= 0)
            {
                index = Mod((int)Math.Floor(conductor.Beat * framesPerBeat), frames.Length);
            }
            else
            {
                time = Mathf.Repeat(time + Time.deltaTime * framesPerSecond * Mathf.Max(0f, SpeedMultiplier), frames.Length);
                index = Mathf.Clamp((int)time, 0, frames.Length - 1);
            }
            Show(index);
        }

        /// <summary>
        /// Switches to another animation and starts it from its first frame.
        /// Does nothing (apart from taking the new speed) if that animation is already playing, unless restart is set.
        /// </summary>
        public void Play(Sprite[] clip, float fps, bool restart = false)
        {
            if (clip == null || clip.Length == 0) return;
            framesPerSecond = fps;
            if (clip == frames && !restart) return;
            frames = clip;
            time = 0f;
            shown = -1;
            Show(0);
        }

        private void Show(int index)
        {
            if (index == shown) return;
            shown = index;
            if (spriteRenderer == null && image == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
                image = GetComponent<Image>();
            }
            if (spriteRenderer != null) spriteRenderer.sprite = frames[index];
            if (image != null) image.sprite = frames[index];
        }

        private static int Mod(int a, int n) => ((a % n) + n) % n;
    }
}
