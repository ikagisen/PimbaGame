using UnityEngine;
using UnityEngine.UI;

namespace PimbaStage
{
    /// <summary>Bounces and/or flashes something on the beat (stage lights, the musician, the banner).</summary>
    public class BeatPulse : MonoBehaviour
    {
        [Tooltip("Extra scale on the beat, as a fraction (0.1 = 10% bigger).")]
        public Vector3 punch = new Vector3(0.08f, 0.08f, 0f);
        [Tooltip("Pulse every N beats (1 = every beat).")]
        [Min(1)] public int everyNBeats = 1;
        public int beatOffset;
        public float returnSpeed = 8f;

        [Header("Optional colour flash")]
        public Graphic graphic;
        public SpriteRenderer sprite;
        [Tooltip("Added to the base alpha on the beat.")]
        public float flashAlpha;
        [Tooltip("Colours to cycle through, one per bar.")]
        public Color[] palette = new Color[0];

        private Vector3 baseScale;
        private float baseAlpha, flash;
        private int paletteIndex;

        private void Awake()
        {
            baseScale = transform.localScale;
            baseAlpha = graphic != null ? graphic.color.a : sprite != null ? sprite.color.a : 1f;
        }

        public void Pulse(int beat)
        {
            if (Mod(beat - beatOffset, everyNBeats) != 0) return;
            transform.localScale = baseScale + Vector3.Scale(baseScale, punch);
            flash = 1f;
            if (palette != null && palette.Length > 0 && Mod(beat, 4) == 0)
            {
                paletteIndex = (paletteIndex + 1) % palette.Length;
                SetRgb(palette[paletteIndex]);
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            transform.localScale = Vector3.Lerp(transform.localScale, baseScale, returnSpeed * dt);
            if (flashAlpha != 0f)
            {
                flash = Mathf.MoveTowards(flash, 0f, dt * 3f);
                SetAlpha(baseAlpha + flashAlpha * flash);
            }
        }

        private void SetRgb(Color c)
        {
            if (graphic != null) { c.a = graphic.color.a; graphic.color = c; }
            if (sprite != null) { c.a = sprite.color.a; sprite.color = c; }
        }

        private void SetAlpha(float a)
        {
            if (graphic != null) { var c = graphic.color; c.a = a; graphic.color = c; }
            if (sprite != null) { var c = sprite.color; c.a = a; sprite.color = c; }
        }

        private static int Mod(int a, int n) => ((a % n) + n) % n;
    }
}
