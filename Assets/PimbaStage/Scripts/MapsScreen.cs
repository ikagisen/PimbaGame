using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static PimbaStage.RuntimeUI;

namespace PimbaStage
{
    /// <summary>
    /// MAPS window (main menu, 3D version): pick the stage to play on (the original stage or a player-made map),
    /// make a NEW MAP, EDIT or DELETE the chosen one.
    /// </summary>
    public class MapsScreen : MonoBehaviour
    {
        public bool IsOpen { get; private set; }
        /// <summary>True while open and just after closing, so the key that closed it doesn't press something else too.</summary>
        public bool BlocksInput => IsOpen || Time.unscaledTime < closedAt + 0.3f;

        private class Row
        {
            public Button button;
            public Image back;
            public Text label;
            public StageMap map; // null = the original stage
        }

        private StageGame game;
        private Font font;
        private Sprite sprite;
        private RectTransform window, listContent;
        private ScrollRect scroll;
        private Text subtitle, deleteLabel;
        private Button newButton, editButton, deleteButton, backButton;
        private readonly List<Row> rows = new List<Row>();
        private Action onClose;
        private float closedAt = -10f, openedAt, deleteArmedUntil;
        private GameObject lastSelected;

        public void Setup(StageGame owner, RectTransform playfield, Font uiFont, Sprite boxSprite)
        {
            if (window != null || playfield == null) return;
            game = owner;
            font = uiFont;
            sprite = boxSprite;
            Build(playfield);
            window.gameObject.SetActive(false);
        }

        public void Show(Action closed)
        {
            onClose = closed;
            window.SetAsLastSibling();
            window.gameObject.SetActive(true);
            IsOpen = true;
            openedAt = Time.unscaledTime;
            Refresh();
            var chosen = rows.Find(r => IsChosen(r.map));
            Select(chosen != null ? chosen.button : newButton);
        }

        private void Close(bool runCallback = true)
        {
            if (!IsOpen) return;
            IsOpen = false;
            window.gameObject.SetActive(false);
            closedAt = Time.unscaledTime;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            var closed = onClose;
            onClose = null;
            if (runCallback) closed?.Invoke();
        }

        private void Update()
        {
            if (!IsOpen) return;
            var kb = Keyboard.current;
            bool back = kb != null && (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame);
            foreach (var pad in Gamepad.all) back |= pad.buttonEast.wasPressedThisFrame;
            if (back && Time.unscaledTime > openedAt + 0.2f)
            {
                Close();
                return;
            }

            if (deleteArmedUntil > 0f && Time.unscaledTime > deleteArmedUntil)
            {
                deleteArmedUntil = 0f;
                deleteLabel.text = "DELETE";
            }

            var es = EventSystem.current;
            if (es == null) return;
            if (es.currentSelectedGameObject == null && Navigated(kb)) Select(rows.Count > 0 ? rows[0].button : newButton);
            if (es.currentSelectedGameObject != lastSelected)
            {
                lastSelected = es.currentSelectedGameObject;
                KeepVisible(lastSelected);
            }
        }

        private static bool Navigated(Keyboard kb)
        {
            bool pressed = kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame ||
                                          kb.wKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame);
            foreach (var pad in Gamepad.all) pressed |= pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame;
            return pressed;
        }

        // ------------------------------------------------------------------ list

        private static bool IsChosen(StageMap map)
        {
            string chosen = GameSettings.MapName;
            return map == null ? string.IsNullOrEmpty(chosen) : map.name == chosen;
        }

        private void Refresh()
        {
            foreach (var row in rows)
            {
                row.button.gameObject.SetActive(false);
                Destroy(row.button.gameObject);
            }
            rows.Clear();
            var maps = MapStorage.LoadAll();
            if (!string.IsNullOrEmpty(GameSettings.MapName) && maps.Find(m => m.name == GameSettings.MapName) == null)
                GameSettings.MapName = ""; // the chosen map is gone

            AddRow(null);
            foreach (var map in maps) AddRow(map);
            subtitle.text = maps.Count == 0 ? "NO MAPS YET - MAKE ONE WITH NEW MAP" : "PICK THE STAGE TO PLAY ON";
            UpdateLook();
        }

        private void AddRow(StageMap map)
        {
            var rt = MakeRect(map == null ? "Original Stage" : map.name, listContent, Vector2.zero, Vector2.one);
            rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 56f;
            var row = new Row { map = map };
            row.button = AddButton(rt, sprite, font, "", 30, ButtonColor, true, out row.label);
            rt.GetComponent<MenuButtonFX>().selectedScale = 1.03f;
            row.back = (Image)row.button.targetGraphic;
            row.label.alignment = TextAnchor.MiddleLeft;
            row.label.rectTransform.offsetMin = new Vector2(24f, 0f);
            row.label.rectTransform.offsetMax = new Vector2(-24f, 0f);
            row.label.text = map == null ? "ORIGINAL STAGE" : map.name + "      " + map.pieces.Count + " PIECES";
            row.button.onClick.AddListener(() =>
            {
                GameSettings.MapName = map == null ? "" : map.name;
                UpdateLook();
            });
            rows.Add(row);
        }

        private void UpdateLook()
        {
            foreach (var row in rows)
            {
                bool chosen = IsChosen(row.map);
                row.back.color = chosen ? Gold : ButtonColor;
                row.label.color = chosen ? DarkText : Color.white;
            }
            bool custom = !string.IsNullOrEmpty(GameSettings.MapName);
            editButton.interactable = custom;
            deleteButton.interactable = custom;
            deleteArmedUntil = 0f;
            deleteLabel.text = "DELETE";
        }

        private void KeepVisible(GameObject selected)
        {
            if (selected == null || selected.transform.parent != listContent) return;
            var item = (RectTransform)selected.transform;
            var viewport = scroll.viewport;
            float viewHeight = viewport.rect.height;
            float y = listContent.anchoredPosition.y;
            float top = -item.localPosition.y - item.rect.height * (1f - item.pivot.y);
            float bottom = top + item.rect.height;
            if (top < y) y = top;
            else if (bottom > y + viewHeight) y = bottom - viewHeight;
            listContent.anchoredPosition = new Vector2(listContent.anchoredPosition.x, Mathf.Max(0f, y));
        }

        // ------------------------------------------------------------------ buttons

        private StageMap ChosenMap() => MapStorage.Find(GameSettings.MapName);

        private void NewMap()
        {
            Close(false);
            game.OpenCreator(null);
        }

        private void EditMap()
        {
            var map = ChosenMap();
            if (map == null) return;
            Close(false);
            game.OpenCreator(map);
        }

        private void DeleteMap()
        {
            var map = ChosenMap();
            if (map == null) return;
            if (deleteArmedUntil <= 0f)
            {
                deleteArmedUntil = Time.unscaledTime + 3f;
                deleteLabel.text = "SURE?";
                return;
            }
            MapStorage.Delete(map);
            GameSettings.MapName = "";
            Refresh();
            Select(rows[0].button);
        }

        private static void Select(Selectable s)
        {
            if (EventSystem.current == null || s == null) return;
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(s.gameObject);
        }

        private void Build(RectTransform playfield)
        {
            window = MakeWindow("Maps Window", playfield, new Vector2(0.15f, 0.09f), new Vector2(0.85f, 0.89f), sprite);
            AddText("Title", window, new Vector2(0.03f, 0.86f), new Vector2(0.97f, 0.97f), "MAPS", font, 62, Gold);
            subtitle = AddText("Subtitle", window, new Vector2(0.03f, 0.79f), new Vector2(0.97f, 0.86f), "", font, 26, Color.white);

            var list = MakeRect("Map List", window, new Vector2(0.06f, 0.18f), new Vector2(0.94f, 0.78f));
            AddImage(list, sprite, new Color(0f, 0f, 0f, 0.35f), true);
            scroll = list.gameObject.AddComponent<ScrollRect>();
            var viewport = MakeRect("Viewport", list, Vector2.zero, Vector2.one);
            viewport.offsetMin = new Vector2(10f, 10f);
            viewport.offsetMax = new Vector2(-10f, -10f);
            viewport.gameObject.AddComponent<RectMask2D>();
            AddImage(viewport, null, new Color(0f, 0f, 0f, 0f), true);
            listContent = MakeRect("Content", viewport, new Vector2(0f, 1f), Vector2.one);
            listContent.pivot = new Vector2(0.5f, 1f);
            listContent.sizeDelta = Vector2.zero;
            var layout = listContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            listContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = listContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            newButton = WindowButton("NEW MAP", 0.04f, NewMap, out _);
            editButton = WindowButton("EDIT", 0.275f, EditMap, out _);
            deleteButton = WindowButton("DELETE", 0.51f, DeleteMap, out deleteLabel);
            backButton = WindowButton("BACK", 0.745f, () => Close(), out _);
        }

        private Button WindowButton(string label, float x, UnityEngine.Events.UnityAction click, out Text text)
        {
            var rt = MakeRect(label, window, new Vector2(x, 0.04f), new Vector2(x + 0.215f, 0.135f));
            var b = AddButton(rt, sprite, font, label, 30, ButtonColor, true, out text);
            b.onClick.AddListener(click);
            return b;
        }
    }
}
