using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PimbaStage
{
    /// <summary>
    /// Main menu (Play / Options / Quit) and the Options screen:
    /// difficulty for each player, song list and volume. Works with mouse, keyboard and gamepad.
    /// </summary>
    public class StageMenu : MonoBehaviour
    {
        private static readonly Color Chosen = new Color(1f, 0.8f, 0.25f);
        private static readonly Color NotChosen = new Color(0.16f, 0.09f, 0.11f, 0.95f);
        private static readonly Color ChosenText = new Color(0.2f, 0.08f, 0.05f);

        private static readonly string[] PerformerHints =
        {
            "Fewer obstacles, and planks over some trapdoors",
            "The full stage",
            "Extra fire and sandbags",
        };
        private static readonly string[] MusicianHints =
        {
            "Fewer arrows - at most one per beat",
            "All of the song's arrows",
            "Extra arrows (on generated charts)",
        };

        [Header("Main menu")]
        public GameObject mainPanel;
        public Button playButton;
        public Button optionsButton;
        public Button quitButton;
        public Text summaryText;

        [Header("Options")]
        public GameObject optionsPanel;
        public Button[] performerButtons = new Button[3];
        public Button[] musicianButtons = new Button[3];
        public Text performerHint;
        public Text musicianHint;
        public ScrollRect songScroll;
        public RectTransform songListContent;
        public Sprite entrySprite;
        public Font font;
        public Slider volumeSlider;
        public Text volumeText;
        public Button backButton;

        public event Action PlayPressed;
        public event Action<PimbaSong> SongChanged;
        public event Action SettingsChanged;

        private List<PimbaSong> songs = new List<PimbaSong>();
        private readonly List<Button> songButtons = new List<Button>();
        private GameObject lastSelected;
        private bool initialised;

        public bool IsOpen => (mainPanel != null && mainPanel.activeSelf) || (optionsPanel != null && optionsPanel.activeSelf);

        public PimbaSong SelectedSong
        {
            get
            {
                if (songs.Count == 0) return null;
                string wanted = GameSettings.SongName;
                foreach (var s in songs)
                    if (s != null && s.name == wanted) return s;
                return songs[0];
            }
        }

        public void Init(List<PimbaSong> songList)
        {
            songs = songList ?? new List<PimbaSong>();
            if (!initialised)
            {
                initialised = true;
                if (playButton != null) playButton.onClick.AddListener(() => PlayPressed?.Invoke());
                if (optionsButton != null) optionsButton.onClick.AddListener(ShowOptions);
                if (quitButton != null) quitButton.onClick.AddListener(Quit);
                if (backButton != null) backButton.onClick.AddListener(BackToMain);

                for (int i = 0; i < performerButtons.Length; i++)
                {
                    var level = (Difficulty)i;
                    if (performerButtons[i] != null)
                        performerButtons[i].onClick.AddListener(() =>
                        {
                            GameSettings.PerformerDifficulty = level;
                            Refresh();
                            SettingsChanged?.Invoke();
                        });
                }
                for (int i = 0; i < musicianButtons.Length; i++)
                {
                    var level = (Difficulty)i;
                    if (musicianButtons[i] != null)
                        musicianButtons[i].onClick.AddListener(() =>
                        {
                            GameSettings.MusicianDifficulty = level;
                            Refresh();
                            SettingsChanged?.Invoke();
                        });
                }
                if (volumeSlider != null)
                {
                    volumeSlider.minValue = 0f;
                    volumeSlider.maxValue = 1f;
                    volumeSlider.SetValueWithoutNotify(GameSettings.Volume);
                    volumeSlider.onValueChanged.AddListener(v =>
                    {
                        GameSettings.Volume = v;
                        RefreshVolume();
                    });
                }
            }
            BuildSongList();
            Refresh();
        }

        public void ShowMain()
        {
            if (mainPanel != null) mainPanel.SetActive(true);
            if (optionsPanel != null) optionsPanel.SetActive(false);
            Refresh();
            Select(playButton);
        }

        public void ShowOptions()
        {
            if (mainPanel != null) mainPanel.SetActive(false);
            if (optionsPanel != null) optionsPanel.SetActive(true);
            Refresh();
            int i = (int)GameSettings.PerformerDifficulty;
            Select(i < performerButtons.Length ? performerButtons[i] : backButton);
        }

        public void Hide()
        {
            if (mainPanel != null) mainPanel.SetActive(false);
            if (optionsPanel != null) optionsPanel.SetActive(false);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        private void BackToMain()
        {
            GameSettings.Save();
            ShowMain();
            Select(optionsButton);
        }

        private static void Quit()
        {
            GameSettings.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void Update()
        {
            if (!IsOpen) return;
            var es = EventSystem.current;
            var kb = Keyboard.current;
            bool optionsOpen = optionsPanel != null && optionsPanel.activeSelf;

            bool back = kb != null && (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame);
            foreach (var pad in Gamepad.all) back |= pad.buttonEast.wasPressedThisFrame;
            if (optionsOpen && back)
            {
                BackToMain();
                return;
            }

            // Nothing selected (e.g. after clicking empty space): arrow keys pick the default button again.
            if (es != null && es.currentSelectedGameObject == null && NavigationPressed(kb))
            {
                int i = (int)GameSettings.PerformerDifficulty;
                Select(optionsOpen ? performerButtons[Mathf.Clamp(i, 0, performerButtons.Length - 1)] : playButton);
            }

            if (es != null && es.currentSelectedGameObject != lastSelected)
            {
                lastSelected = es.currentSelectedGameObject;
                KeepSongVisible(lastSelected);
            }
        }

        private static bool NavigationPressed(Keyboard kb)
        {
            bool pressed = kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame ||
                                          kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame ||
                                          kb.wKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame);
            foreach (var pad in Gamepad.all)
                pressed |= pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame;
            return pressed;
        }

        // ------------------------------------------------------------------ song list

        private void BuildSongList()
        {
            if (songListContent == null) return;
            for (int i = songListContent.childCount - 1; i >= 0; i--) Destroy(songListContent.GetChild(i).gameObject);
            songButtons.Clear();

            foreach (var song in songs)
            {
                if (song == null) continue;
                var go = new GameObject(song.name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(MenuButtonFX));
                go.layer = gameObject.layer;
                var rect = (RectTransform)go.transform;
                rect.SetParent(songListContent, false);
                var image = go.GetComponent<Image>();
                image.sprite = entrySprite;
                image.type = entrySprite != null && entrySprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
                go.GetComponent<LayoutElement>().preferredHeight = 58f;
                go.GetComponent<MenuButtonFX>().selectedScale = 1.03f;

                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
                labelGo.layer = go.layer;
                var labelRect = (RectTransform)labelGo.transform;
                labelRect.SetParent(rect, false);
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(24f, 0f);
                labelRect.offsetMax = new Vector2(-24f, 0f);
                var label = labelGo.GetComponent<Text>();
                label.font = font;
                label.fontSize = 28;
                label.fontStyle = FontStyle.Bold;
                label.alignment = TextAnchor.MiddleLeft;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.raycastTarget = false;
                label.text = string.IsNullOrWhiteSpace(song.artist) ? song.title : song.title + "   -   " + song.artist;

                var button = go.GetComponent<Button>();
                button.targetGraphic = image;
                var chosenSong = song;
                button.onClick.AddListener(() =>
                {
                    GameSettings.SongName = chosenSong.name;
                    Refresh();
                    SongChanged?.Invoke(chosenSong);
                });
                songButtons.Add(button);
            }
        }

        private void KeepSongVisible(GameObject selected)
        {
            if (selected == null || songScroll == null || songListContent == null || selected.transform.parent != songListContent) return;
            var item = (RectTransform)selected.transform;
            var viewport = songScroll.viewport != null ? songScroll.viewport : (RectTransform)songScroll.transform;
            float viewHeight = viewport.rect.height;
            float scroll = songListContent.anchoredPosition.y;
            float top = -item.localPosition.y - item.rect.height * (1f - item.pivot.y);
            float bottom = top + item.rect.height;
            if (top < scroll) scroll = top;
            else if (bottom > scroll + viewHeight) scroll = bottom - viewHeight;
            songListContent.anchoredPosition = new Vector2(songListContent.anchoredPosition.x, Mathf.Max(0f, scroll));
        }

        // ------------------------------------------------------------------ display

        private void Refresh()
        {
            int p1 = (int)GameSettings.PerformerDifficulty;
            int p2 = (int)GameSettings.MusicianDifficulty;
            ShowChoice(performerButtons, p1);
            ShowChoice(musicianButtons, p2);
            if (performerHint != null) performerHint.text = PerformerHints[p1];
            if (musicianHint != null) musicianHint.text = MusicianHints[p2];

            var song = SelectedSong;
            for (int i = 0, b = 0; i < songs.Count && b < songButtons.Count; i++)
            {
                if (songs[i] == null) continue;
                SetLook(songButtons[b++], songs[i] == song);
            }

            if (summaryText != null)
            {
                string songName = song != null ? song.title : "-";
                summaryText.text = "SONG: " + songName + "      PERFORMER: " + ((Difficulty)p1).ToString().ToUpperInvariant()
                                 + "      SINGER: " + ((Difficulty)p2).ToString().ToUpperInvariant();
            }
            RefreshVolume();
        }

        private void RefreshVolume()
        {
            if (volumeText != null) volumeText.text = Mathf.RoundToInt(GameSettings.Volume * 100f) + "%";
        }

        private static void ShowChoice(Button[] buttons, int chosen)
        {
            for (int i = 0; i < buttons.Length; i++)
                if (buttons[i] != null) SetLook(buttons[i], i == chosen);
        }

        private static void SetLook(Button button, bool chosen)
        {
            var image = button.targetGraphic as Image;
            if (image != null) image.color = chosen ? Chosen : NotChosen;
            var label = button.GetComponentInChildren<Text>(true);
            if (label != null) label.color = chosen ? ChosenText : Color.white;
        }

        private static void Select(Selectable selectable)
        {
            if (EventSystem.current == null || selectable == null) return;
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(selectable.gameObject);
        }
    }
}
