using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PimbaStage
{
    /// <summary>
    /// The arcade scoreboard window, built on top of the playfield when the game starts.
    ///  - After a show it opens in front of the results / GAME OVER screen: both players pick a 3-letter name at the same time,
    ///    then it shows the duo's place on the scoreboard. CONTINUE closes it, leaving the results screen.
    ///    P1: W / S letter, A / D move, SPACE ok (or the first gamepad).
    ///    P2: UP / DOWN letter, LEFT / RIGHT move, ENTER ok (or the second gamepad).
    ///    Mouse: the arrows above / below a letter, and OK.
    ///  - Options > SCOREBOARD shows the list, with BACK.
    /// </summary>
    public class ScoreboardScreen : MonoBehaviour
    {
        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        [Tooltip("How many places the list shows.")]
        [Range(5, 12)] public int rowsShown = 10;
        public Color gold = new Color(1f, 0.84f, 0.25f);
        public Color panelColor = new Color(0.1f, 0.05f, 0.07f, 1f); // fully solid: any see-through lets the gold border tint it brown

        public bool IsOpen => mode != Mode.Hidden;
        /// <summary>True while the window is open and just after it closes, so the key that closed it can't also restart the show.</summary>
        public bool BlocksGameInput => IsOpen || Time.unscaledTime < closedAt + 0.3f;

        private enum Mode { Hidden, Entry, Result, View }

        private class Player
        {
            public string role, hint;
            public int gamepad;
            public char[] letters = { 'A', 'A', 'A' };
            public int cursor;
            public bool ready;
            public readonly Image[] boxes = new Image[3];
            public readonly Text[] texts = new Text[3];
            public readonly Image[] arrows = new Image[6];
            public Image okBack;
            public Text okLabel;
            public int vDir, hDir;
            public float vTimer, hTimer;
            public string Name => new string(letters);
        }

        private class Row
        {
            public Image back;
            public Text place, performer, singer, levels, score;
        }

        private static readonly Color BoxColor = new Color(0.27f, 0.14f, 0.18f);
        private static readonly Color DarkText = new Color(0.2f, 0.08f, 0.05f);
        private static readonly Color HintColor = new Color(0.85f, 0.82f, 0.78f);

        private Mode mode = Mode.Hidden;
        private float closedAt = -10f, buttonReadyAt, inputReadyAt;
        private Action onClose;
        private int score;
        private string song;
        private string levels;
        private int highlightRow = -1;

        private RectTransform window;
        private Text title, subtitle, buttonLabel;
        private Button button;
        private GameObject entryGroup, boardGroup;
        private readonly Player[] players = new Player[2];
        private Row[] rows;
        private Font font;
        private Sprite box, arrow;

        /// <summary>Builds the window (hidden) inside the playfield.</summary>
        public void Setup(RectTransform parent, Font uiFont, Sprite boxSprite, int performerGamepad, int singerGamepad)
        {
            if (window != null || parent == null) return;
            font = uiFont != null ? uiFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            box = boxSprite;
            arrow = MakeArrowSprite();
            players[0] = new Player { role = "PERFORMER (P1)", gamepad = performerGamepad, hint = "W / S  LETTER\nA / D  MOVE    SPACE  OK" };
            players[1] = new Player { role = "SINGER (P2)", gamepad = singerGamepad, hint = "UP / DOWN  LETTER\nLEFT / RIGHT  MOVE    ENTER  OK" };
            Build(parent);
            window.gameObject.SetActive(false);
        }

        /// <summary>After a show: both players enter their names, then see their place.</summary>
        public void ShowNameEntry(int finalScore, string songTitle, string levelText = null)
        {
            if (window == null) return;
            score = finalScore;
            song = songTitle;
            levels = levelText ?? Scoreboard.LevelText(GameSettings.PerformerDifficulty, GameSettings.MusicianDifficulty);
            players[0].letters = Scoreboard.LastPerformer.ToCharArray();
            players[1].letters = Scoreboard.LastSinger.ToCharArray();
            foreach (var p in players)
            {
                p.cursor = 0;
                p.ready = false;
            }
            mode = Mode.Entry;
            inputReadyAt = Time.unscaledTime + 0.4f; // keys still held from the show don't count
            title.text = "ENTER YOUR NAMES";
            subtitle.text = "SCORE  " + score.ToString("N0") + "     " + levels;
            entryGroup.SetActive(true);
            boardGroup.SetActive(false);
            button.gameObject.SetActive(false);
            Open();
            RefreshEntry();
        }

        /// <summary>The list on its own (Options > SCOREBOARD). closed runs when BACK is pressed.</summary>
        public void ShowBoard(Action closed)
        {
            if (window == null) { closed?.Invoke(); return; }
            onClose = closed;
            mode = Mode.View;
            title.text = "SCOREBOARD";
            subtitle.text = Scoreboard.Entries.Count == 0 ? "NO SCORES YET - PLAY A SHOW!" : "THE BEST DUOS";
            entryGroup.SetActive(false);
            boardGroup.SetActive(true);
            ShowRows(0);
            ShowButton("BACK", 0.2f);
            Open();
        }

        private void Open()
        {
            window.SetAsLastSibling(); // in front of the results screen and the menus
            window.gameObject.SetActive(true);
            if (mode == Mode.Entry && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        private void Close()
        {
            if (mode == Mode.Hidden) return;
            mode = Mode.Hidden;
            window.gameObject.SetActive(false);
            closedAt = Time.unscaledTime;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            var closed = onClose;
            onClose = null;
            closed?.Invoke();
        }

        private void OnButton()
        {
            if (Time.unscaledTime < buttonReadyAt) return;
            Close();
        }

        private void ShowButton(string label, float delay)
        {
            buttonLabel.text = label;
            button.gameObject.SetActive(true);
            buttonReadyAt = Time.unscaledTime + delay; // the key that opened this can't also press the button
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
                EventSystem.current.SetSelectedGameObject(button.gameObject);
            }
        }

        // ------------------------------------------------------------------ input

        private void Update()
        {
            if (mode == Mode.Hidden) return;
            var kb = Keyboard.current;

            if (mode == Mode.Entry)
            {
                HandlePlayer(players[0], true);
                HandlePlayer(players[1], false);
                if (players[0].ready && players[1].ready) Submit();
                else RefreshEntry();
                return;
            }

            // Result / list: CONTINUE or BACK (Enter, Space, a gamepad's A; BACK also takes Esc and B).
            bool press = kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame);
            bool back = kb != null && (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame);
            foreach (var pad in Gamepad.all)
            {
                press |= pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame;
                back |= pad.buttonEast.wasPressedThisFrame;
            }
            if (press || (mode == Mode.View && back))
            {
                OnButton();
                return;
            }
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != button.gameObject)
                EventSystem.current.SetSelectedGameObject(button.gameObject);

            if (highlightRow >= 0)
            {
                float blink = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
                rows[highlightRow].back.color = new Color(gold.r, gold.g, gold.b, Mathf.Lerp(0.15f, 0.4f, blink));
            }
        }

        private void HandlePlayer(Player p, bool performer)
        {
            var kb = Keyboard.current;
            var pad = p.gamepad >= 0 && p.gamepad < Gamepad.all.Count ? Gamepad.all[p.gamepad] : null;
            bool up = false, down = false, left = false, right = false, ok = false, undo = false;
            if (kb != null)
            {
                if (performer)
                {
                    up = kb.wKey.isPressed; down = kb.sKey.isPressed; left = kb.aKey.isPressed; right = kb.dKey.isPressed;
                    ok = kb.spaceKey.wasPressedThisFrame;
                }
                else
                {
                    up = kb.upArrowKey.isPressed; down = kb.downArrowKey.isPressed; left = kb.leftArrowKey.isPressed; right = kb.rightArrowKey.isPressed;
                    ok = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
                }
            }
            if (pad != null)
            {
                Vector2 stick = pad.leftStick.ReadValue();
                up |= pad.dpad.up.isPressed || stick.y > 0.5f;
                down |= pad.dpad.down.isPressed || stick.y < -0.5f;
                left |= pad.dpad.left.isPressed || stick.x < -0.5f;
                right |= pad.dpad.right.isPressed || stick.x > 0.5f;
                ok |= pad.buttonSouth.wasPressedThisFrame;
                undo |= pad.buttonEast.wasPressedThisFrame;
            }

            int v = up ? 1 : down ? -1 : 0;
            int h = right ? 1 : left ? -1 : 0;
            if (Time.unscaledTime < inputReadyAt)
            {
                p.vDir = v; // held since the show: wait for a fresh press
                p.hDir = h;
                return;
            }
            v = Repeat(v, ref p.vDir, ref p.vTimer, 0.35f, 0.08f);
            h = Repeat(h, ref p.hDir, ref p.hTimer, 0.4f, 0.18f);

            if (p.ready)
            {
                if (h < 0 || undo) p.ready = false; // changed their mind
                return;
            }
            if (v != 0) Step(p, v);
            if (h != 0) p.cursor = Mathf.Clamp(p.cursor + h, 0, Scoreboard.NameLength - 1);
            if (undo && p.cursor > 0) p.cursor--;
            if (ok)
            {
                if (p.cursor < Scoreboard.NameLength - 1) p.cursor++;
                else p.ready = true;
            }
        }

        /// <summary>A press, then repeats while held (for scrolling through the letters).</summary>
        private static int Repeat(int dir, ref int lastDir, ref float timer, float firstDelay, float rate)
        {
            if (dir == 0) { lastDir = 0; return 0; }
            if (dir != lastDir)
            {
                lastDir = dir;
                timer = firstDelay;
                return dir;
            }
            timer -= Time.unscaledDeltaTime;
            if (timer > 0f) return 0;
            timer += rate;
            return dir;
        }

        private static void Step(Player p, int by)
        {
            int i = Alphabet.IndexOf(p.letters[p.cursor]);
            if (i < 0) i = 0;
            p.letters[p.cursor] = Alphabet[(i + by + Alphabet.Length) % Alphabet.Length];
        }

        private void Submit()
        {
            string performer = players[0].Name, singer = players[1].Name;
            Scoreboard.LastPerformer = performer;
            Scoreboard.LastSinger = singer;
            var result = Scoreboard.Submit(performer, singer, score, song, levels);

            mode = Mode.Result;
            title.text = "YOUR PLACE  #" + result.place;
            if (result.newDuo) subtitle.text = performer + " & " + singer + "   NEW DUO!   " + score.ToString("N0");
            else if (result.improved) subtitle.text = "NEW BEST!   " + score.ToString("N0") + "   (WAS " + result.previousBest.ToString("N0") + ")";
            else subtitle.text = "SCORE " + score.ToString("N0") + "   -   YOUR BEST " + result.entry.score.ToString("N0") + " STAYS";
            entryGroup.SetActive(false);
            boardGroup.SetActive(true);
            ShowRows(result.place);
            ShowButton("CONTINUE", 0.5f);
        }

        // ------------------------------------------------------------------ display

        private void RefreshEntry()
        {
            float blink = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f);
            foreach (var p in players)
            {
                for (int i = 0; i < Scoreboard.NameLength; i++)
                {
                    bool current = !p.ready && i == p.cursor;
                    p.texts[i].text = p.letters[i].ToString();
                    p.boxes[i].color = p.ready ? gold : current ? Color.Lerp(gold * 0.75f, gold, blink) : BoxColor;
                    p.texts[i].color = p.ready || current ? DarkText : Color.white;
                    var arrowColor = p.ready ? new Color(1f, 1f, 1f, 0.15f) : current ? gold : new Color(1f, 1f, 1f, 0.45f);
                    p.arrows[i * 2].color = arrowColor;
                    p.arrows[i * 2 + 1].color = arrowColor;
                }
                p.okLabel.text = p.ready ? "READY!" : "OK";
                p.okLabel.color = p.ready ? DarkText : Color.white;
                p.okBack.color = p.ready ? gold : BoxColor;
            }
        }

        /// <summary>Fills the list. place > 0 highlights that place (and shows it below the top rows if it's further down).</summary>
        private void ShowRows(int place)
        {
            var entries = Scoreboard.Entries;
            int lines = rows.Length - 1;
            var shown = new List<int>(); // places, 0 = "..."
            if (place <= lines) for (int i = 1; i <= lines; i++) shown.Add(i);
            else
            {
                for (int i = 1; i <= lines - 2; i++) shown.Add(i);
                shown.Add(0);
                shown.Add(place);
            }

            highlightRow = -1;
            for (int r = 1; r < rows.Length; r++)
            {
                var row = rows[r];
                int p = shown[r - 1];
                bool has = p > 0 && p <= entries.Count;
                var e = has ? entries[p - 1] : null;
                row.place.text = p == 0 ? "..." : has ? p.ToString() : "";
                row.performer.text = has ? e.performer : "";
                row.singer.text = has ? e.singer : "";
                row.levels.text = has ? (string.IsNullOrEmpty(e.levels) ? "---" : e.levels) : "";
                row.score.text = has ? e.score.ToString("N0") : "";
                bool mine = has && p == place;
                if (mine) highlightRow = r;
                row.back.enabled = mine;
                row.place.color = row.performer.color = row.singer.color = row.levels.color = row.score.color = Color.white; // the blinking bar marks your row
            }
        }

        // ------------------------------------------------------------------ building

        private void Build(RectTransform parent)
        {
            window = Rect("Scoreboard Window", parent, new Vector2(0.15f, 0.09f), new Vector2(0.85f, 0.89f));
            var border = AddImage(window, box, gold);
            border.raycastTarget = true; // clicks don't go through to what's behind
            var inside = Rect("Background", window, Vector2.zero, Vector2.one);
            inside.offsetMin = new Vector2(7f, 7f);
            inside.offsetMax = new Vector2(-7f, -7f);
            AddImage(inside, box, panelColor);

            title = AddText("Title", window, new Vector2(0.03f, 0.86f), new Vector2(0.97f, 0.97f), "", 62, gold);
            subtitle = AddText("Subtitle", window, new Vector2(0.03f, 0.78f), new Vector2(0.97f, 0.86f), "", 30, Color.white);

            var entry = Rect("Name Entry", window, new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.78f));
            entryGroup = entry.gameObject;
            BuildPlayer(players[0], entry, 0f, 0.5f);
            BuildPlayer(players[1], entry, 0.5f, 1f);

            var board = Rect("Board", window, new Vector2(0.05f, 0.15f), new Vector2(0.95f, 0.77f));
            boardGroup = board.gameObject;
            BuildBoard(board);

            var buttonRect = Rect("Button", window, new Vector2(0.33f, 0.03f), new Vector2(0.67f, 0.125f));
            button = MakeButton(buttonRect, BoxColor, true, out buttonLabel, "CONTINUE", 34);
            button.onClick.AddListener(OnButton);
            buttonRect.gameObject.AddComponent<MenuButtonFX>().selectedScale = 1.1f;
        }

        private void BuildPlayer(Player p, RectTransform parent, float x0, float x1)
        {
            var col = Rect(p.role, parent, new Vector2(x0 + 0.01f, 0f), new Vector2(x1 - 0.01f, 1f));
            AddText("Name", col, new Vector2(0f, 0.86f), new Vector2(1f, 1f), p.role, 32, gold);
            for (int i = 0; i < Scoreboard.NameLength; i++)
            {
                int index = i;
                var centre = new Vector2(0.5f + (i - 1) * 0.27f, 0.58f);
                var slot = RectAt("Letter " + (i + 1), col, centre, new Vector2(104f, 124f), Vector2.zero);
                p.boxes[i] = AddImage(slot, box, BoxColor);
                p.texts[i] = AddText("Letter", slot, Vector2.zero, Vector2.one, "A", 92, Color.white);
                Clickable(p.boxes[i], () => { if (!p.ready) p.cursor = index; });

                var up = RectAt("Up", col, centre, new Vector2(54f, 30f), new Vector2(0f, 98f));
                p.arrows[i * 2] = AddImage(up, arrow, Color.white);
                Clickable(p.arrows[i * 2], () => { if (p.ready) return; p.cursor = index; Step(p, 1); });
                var down = RectAt("Down", col, centre, new Vector2(54f, 30f), new Vector2(0f, -98f));
                down.localEulerAngles = new Vector3(0f, 0f, 180f);
                p.arrows[i * 2 + 1] = AddImage(down, arrow, Color.white);
                Clickable(p.arrows[i * 2 + 1], () => { if (p.ready) return; p.cursor = index; Step(p, -1); });
            }
            var ok = Rect("OK", col, new Vector2(0.3f, 0.19f), new Vector2(0.7f, 0.31f));
            var okButton = MakeButton(ok, BoxColor, false, out p.okLabel, "OK", 32);
            p.okBack = (Image)okButton.targetGraphic;
            okButton.onClick.AddListener(() => { p.ready = !p.ready; if (!p.ready) p.cursor = Scoreboard.NameLength - 1; });
            AddText("Controls", col, new Vector2(0f, 0f), new Vector2(1f, 0.17f), p.hint, 20, HintColor);
        }

        private void BuildBoard(RectTransform parent)
        {
            int n = rowsShown + 1;
            rows = new Row[n];
            for (int i = 0; i < n; i++)
            {
                var rt = Rect(i == 0 ? "Header" : "Place " + i, parent, new Vector2(0f, 1f - (i + 1) / (float)n), new Vector2(1f, 1f - i / (float)n));
                var row = new Row { back = AddImage(rt, box, new Color(gold.r, gold.g, gold.b, 0.25f)) };
                row.back.enabled = false;
                int size = i == 0 ? 24 : 32;
                Color c = i == 0 ? gold : Color.white;
                row.place = AddText("Place", rt, new Vector2(0.02f, 0f), new Vector2(0.09f, 1f), "", size, c, TextAnchor.MiddleLeft);
                row.performer = AddText("Performer", rt, new Vector2(0.09f, 0f), new Vector2(0.31f, 1f), "", size, c);
                row.singer = AddText("Singer", rt, new Vector2(0.31f, 0f), new Vector2(0.53f, 1f), "", size, c);
                row.levels = AddText("Difficulty", rt, new Vector2(0.53f, 0f), new Vector2(0.75f, 1f), "", size, c);
                row.score = AddText("Score", rt, new Vector2(0.75f, 0f), new Vector2(0.98f, 1f), "", size, c, TextAnchor.MiddleRight);
                rows[i] = row;
            }
            rows[0].place.text = "#";
            rows[0].performer.text = "PERFORMER";
            rows[0].singer.text = "SINGER";
            rows[0].levels.text = "DIFFICULTY";
            rows[0].score.text = "SCORE";
        }

        private Button MakeButton(RectTransform rt, Color colour, bool navigable, out Text label, string text, int size)
        {
            var image = AddImage(rt, box, colour);
            image.raycastTarget = true;
            label = AddText("Label", rt, Vector2.zero, Vector2.one, text, size, Color.white);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = image;
            var colors = b.colors;
            colors.normalColor = new Color(0.92f, 0.92f, 0.92f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f);
            b.colors = colors;
            if (!navigable) b.navigation = new Navigation { mode = Navigation.Mode.None };
            return b;
        }

        /// <summary>A mouse-only button: never selected, so the players' keys can't press it.</summary>
        private static void Clickable(Image image, Action click)
        {
            image.raycastTarget = true;
            var b = image.gameObject.AddComponent<Button>();
            b.targetGraphic = image;
            b.transition = Selectable.Transition.None;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(() => click());
        }

        private RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
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

        private RectTransform RectAt(string name, Transform parent, Vector2 anchor, Vector2 size, Vector2 offset)
        {
            var rt = Rect(name, parent, anchor, anchor);
            rt.sizeDelta = size;
            rt.anchoredPosition = offset;
            return rt;
        }

        private static Image AddImage(RectTransform rt, Sprite sprite, Color colour)
        {
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.color = colour;
            image.raycastTarget = false;
            return image;
        }

        private Text AddText(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, string text, int size, Color colour,
            TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var rt = Rect(name, parent, anchorMin, anchorMax);
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

        /// <summary>A little pixel-art triangle pointing up.</summary>
        private static Sprite MakeArrowSprite()
        {
            const int w = 9, h = 5;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    tex.SetPixel(x, y, x >= y && x < w - y ? Color.white : Color.clear);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}
