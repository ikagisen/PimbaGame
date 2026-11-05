using UnityEngine;
using UnityEngine.UI;

namespace PimbaStage
{
    /// <summary>Helpers for the UI that is made while the game runs (map creator, maps list).</summary>
    public static class RuntimeUI
    {
        public static readonly Color Gold = new Color(1f, 0.84f, 0.25f);
        public static readonly Color PanelColor = new Color(0.1f, 0.05f, 0.07f, 1f);
        public static readonly Color ButtonColor = new Color(0.27f, 0.14f, 0.18f);
        public static readonly Color DarkText = new Color(0.2f, 0.08f, 0.05f);
        public static readonly Color HintColor = new Color(0.85f, 0.82f, 0.78f);

        public static RectTransform MakeRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static RectTransform MakeRectAt(string name, Transform parent, Vector2 anchor, Vector2 size, Vector2 offset)
        {
            var rt = MakeRect(name, parent, anchor, anchor);
            rt.sizeDelta = size;
            rt.anchoredPosition = offset;
            return rt;
        }

        public static Image AddImage(RectTransform rt, Sprite sprite, Color colour, bool blocksClicks = false)
        {
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.color = colour;
            image.raycastTarget = blocksClicks;
            return image;
        }

        public static Text AddText(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, string text, Font font, int size,
            Color colour, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var rt = MakeRect(name, parent, anchorMin, anchorMax);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = colour;
            t.alignment = alignment;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(2.5f, -2.5f);
            return t;
        }

        /// <summary>
        /// A rounded button with a label. keyboard = false: mouse only (never selected, so keys used for playing can't press it).
        /// </summary>
        public static Button AddButton(RectTransform rt, Sprite sprite, Font font, string text, int size, Color colour, bool keyboard, out Text label)
        {
            var image = AddImage(rt, sprite, colour, true);
            label = AddText("Label", rt, Vector2.zero, Vector2.one, text, font, size, Color.white);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = image;
            var colors = b.colors;
            colors.normalColor = new Color(0.92f, 0.92f, 0.92f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            b.colors = colors;
            if (keyboard) rt.gameObject.AddComponent<MenuButtonFX>().selectedScale = 1.08f;
            else b.navigation = new Navigation { mode = Navigation.Mode.None };
            return b;
        }

        /// <summary>Dark panel with a gold border, like the scoreboard.</summary>
        public static RectTransform MakeWindow(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Sprite sprite, float border = 7f)
        {
            var window = MakeRect(name, parent, anchorMin, anchorMax);
            AddImage(window, sprite, Gold, true);
            var inside = MakeRect("Background", window, Vector2.zero, Vector2.one);
            inside.offsetMin = new Vector2(border, border);
            inside.offsetMax = new Vector2(-border, -border);
            AddImage(inside, sprite, PanelColor);
            return window;
        }
    }
}
