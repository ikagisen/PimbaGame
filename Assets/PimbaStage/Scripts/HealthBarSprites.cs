using UnityEngine;
using UnityEngine.UI;

namespace PimbaStage
{
    /// <summary>
    /// Player 2's skull health bar: one picture per number of segments left (UI/SingerHealth_0..11.png,
    /// 0 = empty, 11 = full). Segments go out from the far end; the bar shakes when it loses one.
    /// </summary>
    public class HealthBarSprites : MonoBehaviour
    {
        public Image image;
        [Tooltip("One picture per amount left: the first is empty, the last is full.")]
        public Sprite[] levels = new Sprite[0];

        private int shown = -1;
        private float shakeTime = 99f;
        private Vector2 home;
        private bool homeSet;

        /// <summary>Shows the health (0-1) as whole segments; a segment stays lit until it is fully used up.</summary>
        public void Show(float health)
        {
            if (image == null || levels == null || levels.Length == 0) return;
            int full = levels.Length - 1;
            int segments = Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp01(health) * full - 0.001f), 0, full);
            if (segments == shown) return;
            if (shown >= 0 && segments < shown) shakeTime = 0f;
            shown = segments;
            if (levels[segments] != null) image.sprite = levels[segments];
        }

        private void Update()
        {
            if (image == null) return;
            var rt = image.rectTransform;
            if (!homeSet) { home = rt.anchoredPosition; homeSet = true; }
            shakeTime += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(1f - shakeTime / 0.3f);
            rt.anchoredPosition = home + (k > 0f ? new Vector2(Mathf.Sin(shakeTime * 95f), Mathf.Cos(shakeTime * 75f)) * 5f * k : Vector2.zero);
        }
    }
}
