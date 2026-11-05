using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PimbaStage
{
    /// <summary>
    /// Runs the show: main menu, curtains, countdown, hearts for both players, scoring, win / lose and restart.
    /// </summary>
    public class StageGame : MonoBehaviour
    {
        public static StageGame Instance { get; private set; }

        private static bool skipMenu;
        private static bool openMapsOnStart;

        [Header("Songs (picked in Options)")]
        public List<PimbaSong> songs = new List<PimbaSong>();

        [Header("Systems")]
        public Conductor conductor;
        public RhythmLane lane;
        public StageScroller scroller;
        public StagePlayer player;
        public StageMenu menu;
        [Tooltip("Arcade scoreboard (names after a show, and Options > SCOREBOARD). Added when the game starts if empty.")]
        public ScoreboardScreen scoreboard;
        [Tooltip("Obstacles and bridges that depend on the Performer difficulty.")]
        public DifficultyFilter[] difficultyObjects = new DifficultyFilter[0];

        [Header("Hearts")]
        public int performerHearts = 3;
        [Tooltip("Musician health lost per miss (3 hearts = 1.0).")]
        public float missDamage = 0.08f;
        public float badDamage = 0.04f;
        public float goodHeal = 0.015f;
        public float amazingHeal = 0.03f;
        public Image[] performerHeartFills = new Image[0];
        [Tooltip("Player 1's heart counter (box with the yellow heart and a rolling number).")]
        public HeartCounter performerHeartCounter;
        public Image[] musicianHeartFills = new Image[0];
        [Tooltip("Player 2's skull health bar (used instead of the hearts when set).")]
        public HealthBarSprites musicianHealthBar;

        [Header("UI")]
        public Text bannerText;
        public Text countdownText;
        public Text scoreText;
        public GameObject pausePanel;
        public GameObject resultsPanel;
        [Tooltip("Background image of the results panel.")]
        public Image resultsBackground;
        [Tooltip("Background shown instead when the show is lost (the red screen).")]
        public Sprite loseBackground;
        [Tooltip("GAME OVER panel laid on top of the red lose screen; the stats go in its box. Empty = the old BOOO! title.")]
        public Sprite gameOverPanel;
        [Tooltip("How big the GAME OVER panel is on the red screen (1 = as big as it fits).")]
        [Range(0.3f, 1f)] public float gameOverPanelSize = 0.97f;
        [Tooltip("Where the stats go on the GAME OVER panel: x, y (from the bottom-left), width, height, as 0-1 of the panel.")]
        public Rect loseTextArea = new Rect(0.075f, 0.10f, 0.82f, 0.50f);
        public Text resultsTitle;
        public Text resultsBody;
        public RectTransform curtainLeft;
        public RectTransform curtainRight;
        public BeatPulse[] beatPulses = new BeatPulse[0];

        private enum State { Menu, Countdown, Playing, Paused, Ended, Creator }

        private State state = State.Menu, stateBeforePause;
        private PimbaSong song;
        private int hearts, heartsLost, score, roses, totalRoses;
        private float musicianHealth = 1f;
        private readonly int[] counts = new int[5];
        private bool won;
        private float endTimer, curtainAmount = 1f, curtainTarget = 1f;
        private bool layoutGameOver;
        private Image gameOverImage;
        private float leftOpenMax, rightOpenMin;
        private MapStage mapStage;
        private MapCreator creator;
        private MapsScreen mapsScreen;
        private StageMap customMap;

        private void Awake()
        {
            Instance = this;
            if (curtainLeft != null) leftOpenMax = curtainLeft.anchorMax.x;
            if (curtainRight != null) rightOpenMin = curtainRight.anchorMin.x;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            Time.timeScale = 1f;
            GameSettings.ApplyVolume();
            if (lane != null) lane.Judged += OnJudged;
            if (conductor != null) conductor.OnBeat += OnBeat;
            if (scroller != null) scroller.PlayerOut += OnPlayerOut;

            hearts = performerHearts;
            musicianHealth = 1f;
            UpdateHearts();
            SetCurtains(1f, true);
            if (resultsPanel != null) resultsPanel.SetActive(false);
            if (pausePanel != null) pausePanel.SetActive(false);
            if (countdownText != null) countdownText.text = "";

            songs.RemoveAll(s => s == null);
            if (songs.Count == 0)
            {
                Debug.LogError("Pimba Stage: no songs in the StageGame's Songs list.");
                return;
            }

            if (menu != null)
            {
                menu.Init(songs);
                menu.PlayPressed += StartShow;
                menu.SongChanged += s => { song = s; if (bannerText != null) bannerText.text = s.BannerText; };
                menu.SettingsChanged += ApplyPerformerDifficulty;
            }
            SetUpScoreboard();
            SetUpMaps();
            SetUpPixelLook();
            song = CurrentSong();
            if (bannerText != null) bannerText.text = song.BannerText;
            ApplyPerformerDifficulty();

            bool openMaps = openMapsOnStart;
            openMapsOnStart = false;
            if (skipMenu)
            {
                skipMenu = false;
                StartShow();
            }
            else
            {
                ShowMenu();
                if (openMaps) OpenMaps();
            }
        }

        private void SetUpScoreboard()
        {
            if (scoreboard == null) scoreboard = GetComponent<ScoreboardScreen>();
            if (scoreboard == null) scoreboard = gameObject.AddComponent<ScoreboardScreen>();
            var playfield = resultsPanel != null ? resultsPanel.transform.parent as RectTransform : null;
            Font font = menu != null && menu.font != null ? menu.font : resultsBody != null ? resultsBody.font : null;
            scoreboard.Setup(playfield, font, menu != null ? menu.entrySprite : null,
                player != null ? player.gamepadIndex : 0, lane != null ? lane.gamepadIndex : 1);
            if (menu != null)
                menu.ScoreboardPressed += () =>
                {
                    if (scoreboard.BlocksGameInput) return; // the key that closed it
                    menu.Hide();
                    scoreboard.ShowBoard(menu.ReturnFromScoreboard);
                };
        }

        // ------------------------------------------------------------------ player-made maps (3D version)

        private void SetUpMaps()
        {
            mapStage = gameObject.AddComponent<MapStage>();
            mapStage.Init(player);
            if (!mapStage.Available || menu == null || resultsPanel == null) return; // the 2D version
            var playfield = resultsPanel.transform.parent as RectTransform;
            Font font = menu.font != null ? menu.font : resultsBody != null ? resultsBody.font : null;
            creator = gameObject.AddComponent<MapCreator>();
            creator.Setup(this, mapStage, scroller, playfield, font, menu.entrySprite);
            mapsScreen = gameObject.AddComponent<MapsScreen>();
            mapsScreen.Setup(this, playfield, font, menu.entrySprite);
            menu.AddMapsButton();
            menu.MapsPressed += OpenMaps;
        }

        /// <summary>3D version: chunky pixels for the 3D parts, like the pixel-art characters (unless Pixelated 3D is on the camera, maybe switched off).</summary>
        private void SetUpPixelLook()
        {
            if (scroller == null || scroller.cam == null || scroller.cam.GetComponent<Pixelated3D>() != null) return;
            foreach (var root in gameObject.scene.GetRootGameObjects())
                if (root.name.StartsWith("3D Set"))
                {
                    scroller.cam.gameObject.AddComponent<Pixelated3D>();
                    return;
                }
        }

        private void OpenMaps()
        {
            if (mapsScreen == null || mapsScreen.BlocksInput || state != State.Menu) return;
            if (menu != null) menu.Hide();
            mapsScreen.Show(() => { if (menu != null) menu.ReturnFromMaps(); });
        }

        /// <summary>The map creator, on the open stage. map = null: a new, empty map.</summary>
        public void OpenCreator(StageMap map)
        {
            if (creator == null) return;
            state = State.Creator;
            if (menu != null) menu.Hide();
            if (player != null) player.gameObject.SetActive(false);
            if (bannerText != null) bannerText.text = "";
            curtainTarget = 0f;
            creator.Begin(map);
        }

        /// <summary>Plays a show on a saved map straight away (TEST in the map creator).</summary>
        public void PlayMapNow(string mapName)
        {
            GameSettings.MapName = mapName;
            skipMenu = true;
            ReloadScene();
        }

        /// <summary>Leaves the map creator, back to the MAPS window.</summary>
        public void LeaveCreator()
        {
            openMapsOnStart = true;
            skipMenu = false;
            ReloadScene();
        }

        private PimbaSong CurrentSong()
        {
            var chosen = menu != null ? menu.SelectedSong : null;
            return chosen != null ? chosen : songs[0];
        }

        private void ApplyPerformerDifficulty()
        {
            if (mapStage != null && mapStage.CustomApplied) return; // a player-made map has none of these
            var difficulty = GameSettings.PerformerDifficulty;
            foreach (var filter in difficultyObjects)
                if (filter != null) filter.Apply(difficulty);
        }

        // ------------------------------------------------------------------ flow

        private void ShowMenu()
        {
            state = State.Menu;
            if (player != null) player.ControlsEnabled = false;
            if (menu != null) menu.ShowMain();
        }

        private void StartShow()
        {
            if (state != State.Menu) return;
            song = CurrentSong();
            if (menu != null) menu.Hide();
            if (bannerText != null) bannerText.text = song.BannerText;
            customMap = mapStage != null && mapStage.Available ? MapStorage.Find(GameSettings.MapName) : null;
            if (customMap != null)
            {
                mapStage.ApplyLayout(customMap); // the player-made map instead of the original stage
                totalRoses = 0;
            }
            ApplyPerformerDifficulty();

            AudioClip clip = song.music;
            List<DemoTrack.MelodyNote> melody = null;
            if (song.useDemoTrack || clip == null)
            {
                if (!song.useDemoTrack) Debug.LogWarning(song.name + " has no music assigned, playing the demo tune instead.");
                clip = DemoTrack.Create(song.bpm, song.autoSeed, out melody);
            }

            conductor.Load(song, clip);
            var chart = song.BuildChart(clip.length, melody, GameSettings.MusicianDifficulty);
            lane.Begin(conductor, chart, song.recordChart);
            int scrollSteps = song.recordChart ? Mathf.Max(1, (int)(clip.length / song.SecondsPerBeat) - 8) : chart.Count;
            scroller.Setup(scrollSteps);

            hearts = performerHearts;
            musicianHealth = 1f;
            heartsLost = score = roses = 0;
            Array.Clear(counts, 0, counts.Length);
            won = false;
            endTimer = 0f;
            UpdateHearts();

            conductor.Play(4.0 * song.SecondsPerBeat);
            curtainTarget = 0f; // curtains open during the count-in
            state = State.Countdown;
        }

        private void Update()
        {
            AnimateCurtains();
            var kb = Keyboard.current;
            bool enter = kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
            bool esc = kb != null && kb.escapeKey.wasPressedThisFrame;
            bool restart = kb != null && kb.rKey.wasPressedThisFrame;
            foreach (var pad in Gamepad.all)
            {
                bool start = pad.startButton.wasPressedThisFrame;
                if (state == State.Ended) enter |= start;
                else esc |= start;
            }

            switch (state)
            {
                case State.Countdown:
                case State.Playing:
                    UpdateCountdown();
                    UpdateScore();
                    if (esc) Pause();
                    else if (restart) Restart();
                    else if (state == State.Playing && conductor.Finished && !won)
                    {
                        endTimer += Time.deltaTime;
                        if (endTimer > 1.5f)
                        {
                            if (song.recordChart) Finish(true, "Chart recorded!");
                            else Finish(false, "The song ended before the show reached the finale!");
                        }
                    }
                    break;

                case State.Paused:
                    if (esc) Resume();
                    else if (restart) Restart();
                    else if (kb != null && kb.qKey.wasPressedThisFrame) BackToMenu();
                    break;

                case State.Ended:
                    if (resultsPanel != null && !resultsPanel.activeSelf && curtainAmount > 0.95f)
                    {
                        resultsPanel.SetActive(true);
                        if (layoutGameOver) LayoutGameOver();
                        // Both players write their names in a window in front of the results, then CONTINUE.
                        // (only on the original stage: player-made maps would not be a fair comparison)
                        if (scoreboard != null && song != null && !song.recordChart && customMap == null) scoreboard.ShowNameEntry(score, song.title);
                    }
                    if (scoreboard != null && scoreboard.BlocksGameInput) break;
                    if (enter || restart) Restart();
                    else if (esc) BackToMenu();
                    break;
            }
        }

        private void UpdateCountdown()
        {
            if (countdownText == null || song == null) return;
            double t = conductor.SongTime;
            double spb = song.SecondsPerBeat;
            string text = "";
            if (t < 0)
            {
                int n = (int)Math.Ceiling(-t / spb);
                text = n >= 4 ? "3" : n == 3 ? "2" : n == 2 ? "1" : "GO!";
                if (n <= 1 && state == State.Countdown) BeginPlaying();
            }
            else if (t < spb) text = "GO!";
            if (t >= 0 && state == State.Countdown) BeginPlaying();
            countdownText.text = text;
        }

        private void BeginPlaying()
        {
            state = State.Playing;
            if (player != null) player.ControlsEnabled = true;
        }

        private void Pause()
        {
            stateBeforePause = state;
            state = State.Paused;
            Time.timeScale = 0f;
            conductor.SetPaused(true);
            if (pausePanel != null) pausePanel.SetActive(true);
        }

        private void Resume()
        {
            state = stateBeforePause;
            Time.timeScale = 1f;
            conductor.SetPaused(false);
            if (pausePanel != null) pausePanel.SetActive(false);
        }

        private void Restart()
        {
            skipMenu = true;
            ReloadScene();
        }

        private void BackToMenu()
        {
            skipMenu = false;
            ReloadScene();
        }

        private static void ReloadScene()
        {
            Time.timeScale = 1f;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            var scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0)
            {
                SceneManager.LoadScene(scene.buildIndex);
                return;
            }
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(scene.name);
#endif
        }

        // ------------------------------------------------------------------ events

        private void OnBeat(int beat)
        {
            foreach (var pulse in beatPulses)
                if (pulse != null) pulse.Pulse(beat);
            if (beat >= 0 && beat % 4 == 0 && scroller != null && state == State.Playing) scroller.Bop();
            if (player != null) player.OnBeat();
            if (song != null && song.recordChart && beat >= 0 && state == State.Playing) scroller.Advance(Judgement.Amazing);
        }

        private void OnJudged(Judgement j)
        {
            if (state != State.Playing && state != State.Countdown) return;
            counts[(int)j]++;
            switch (j)
            {
                case Judgement.Amazing: score += 350; musicianHealth += amazingHeal; break;
                case Judgement.Good: score += 200; musicianHealth += goodHeal; break;
                case Judgement.OK: score += 100; break;
                case Judgement.Bad: score += 50; musicianHealth -= badDamage; break;
                default: musicianHealth -= missDamage; break;
            }
            musicianHealth = Mathf.Clamp01(musicianHealth);
            scroller.Advance(j);
            UpdateHearts();
            if (musicianHealth <= 0f) Finish(false, "Too many missed notes - the audience booed the band!");
        }

        private void OnPlayerOut()
        {
            HurtPlayer(true, player != null ? player.transform.position.x : 0f);
        }

        /// <summary>Called by hazards (respawn = false) and by falls / squashes (respawn = true).</summary>
        public void HurtPlayer(bool respawn, float fromX)
        {
            if (player == null || (state != State.Playing && state != State.Countdown)) return;

            if (player.Invulnerable)
            {
                if (respawn) player.Respawn(scroller.FindSafeSpot());
                return;
            }

            hearts--;
            heartsLost++;
            UpdateHearts();
            player.PlayHurt();
            if (hearts <= 0)
            {
                Finish(false, "The performer ran out of hearts!");
                return;
            }

            if (respawn) player.Respawn(scroller.FindSafeSpot());
            else player.Knockback(fromX);
            player.MakeInvulnerable(1.5f);
        }

        public void RegisterRose() => totalRoses++;

        public void CollectRose()
        {
            roses++;
            score += 500;
        }

        public void Win()
        {
            if (state != State.Playing || won) return;
            won = true;
            Finish(true, "The performer reached the spotlight!");
        }

        private void Finish(bool success, string reason)
        {
            if (state == State.Ended) return;
            state = State.Ended;
            if (player != null) player.ControlsEnabled = false;
            lane.Stop();
            string chartPath = song != null && song.recordChart ? lane.SaveRecording(song) : null;
            conductor.FadeOut(success ? 4f : 1.5f);
            curtainTarget = 1f;
            if (countdownText != null) countdownText.text = "";

            float accuracy = Accuracy(out _);
            if (!success && resultsBackground != null && loseBackground != null)
            {
                resultsBackground.sprite = loseBackground;
                resultsBackground.type = Image.Type.Simple;
                resultsBackground.color = Color.white;
            }
            bool gameOverArt = !success && resultsBackground != null && gameOverPanel != null;
            layoutGameOver = gameOverArt;
            if (resultsTitle != null)
            {
                resultsTitle.text = success ? "BRAVO!" : gameOverArt ? "" : "BOOO!";
                // White reads better than red on the red lose background.
                resultsTitle.color = success ? new Color(1f, 0.84f, 0.2f)
                    : loseBackground != null ? Color.white : new Color(1f, 0.4f, 0.35f);
            }
            if (resultsBody != null)
            {
                string body = reason + "\n\n"
                    + "Score  " + score.ToString("N0") + "\n"
                    + "Amazing " + counts[0] + "   Good " + counts[1] + "   OK " + counts[2] + "   Bad " + counts[3] + "   Miss " + counts[4] + "\n"
                    + "Accuracy " + accuracy.ToString("0.0") + "%     Max combo " + lane.MaxCombo + "\n"
                    + "Roses " + roses + "/" + totalRoses + "     Hearts lost " + heartsLost + "\n"
                    + "Performer " + GameSettings.PerformerDifficulty + "     Singer " + GameSettings.MusicianDifficulty + "\n";
                if (customMap != null) body += "Map  " + customMap.name + "\n";
                if (chartPath != null) body += "\nChart saved: " + chartPath + "\n";
                body += "\nENTER / R  play again        ESC  main menu";
                resultsBody.text = body;
            }
        }

        /// <summary>
        /// Lose screen with the GAME OVER art: keeps the art's shape on any screen and puts the stats
        /// in the box under the title, shrinking them until every line fits.
        /// </summary>
        private void LayoutGameOver()
        {
            layoutGameOver = false;
            var results = resultsBackground.rectTransform;

            // The GAME OVER panel goes on top of the red screen (behind the text), centred, at its own shape.
            if (gameOverImage == null)
            {
                var go = new GameObject("Game Over Panel", typeof(RectTransform));
                go.layer = results.gameObject.layer;
                go.transform.SetParent(results, false);
                go.transform.SetAsFirstSibling();
                gameOverImage = go.AddComponent<Image>();
                gameOverImage.raycastTarget = false;
                gameOverImage.preserveAspect = true;
            }
            gameOverImage.sprite = gameOverPanel;
            float size = Mathf.Clamp01(gameOverPanelSize);
            var art = gameOverImage.rectTransform;
            art.anchorMin = new Vector2(0.5f - size * 0.5f, 0.5f - size * 0.5f);
            art.anchorMax = new Vector2(0.5f + size * 0.5f, 0.5f + size * 0.5f);
            art.offsetMin = art.offsetMax = Vector2.zero;
            gameOverImage.gameObject.SetActive(true);
            if (resultsBody == null) return;

            // Work out where the art is drawn (as 0-1 of the results area), then put the stats in its box.
            Canvas.ForceUpdateCanvases();
            Rect area0 = results.rect;
            float boxW = area0.width * size, boxH = area0.height * size;
            float artAspect = gameOverPanel.rect.width / Mathf.Max(1f, gameOverPanel.rect.height);
            float drawW = boxW, drawH = boxH;
            if (boxW / Mathf.Max(1f, boxH) > artAspect) drawW = boxH * artAspect;
            else drawH = boxW / artAspect;
            float w = drawW / Mathf.Max(1f, area0.width);
            float h = drawH / Mathf.Max(1f, area0.height);
            var origin = new Vector2((1f - w) * 0.5f, (1f - h) * 0.5f);
            var area = resultsBody.rectTransform;
            area.anchorMin = origin + Vector2.Scale(loseTextArea.min, new Vector2(w, h));
            area.anchorMax = origin + Vector2.Scale(loseTextArea.max, new Vector2(w, h));
            area.offsetMin = area.offsetMax = Vector2.zero;
            FitWithoutWrapping(resultsBody, 10);
        }

        /// <summary>Shrinks the text until every line fits on one line and all lines fit in the box.</summary>
        private static void FitWithoutWrapping(Text text, int minSize)
        {
            text.resizeTextForBestFit = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            Canvas.ForceUpdateCanvases();
            Rect box = text.rectTransform.rect;
            for (int size = text.fontSize; size >= minSize; size--)
            {
                text.fontSize = size;
                if (text.preferredWidth <= box.width && text.preferredHeight <= box.height) break;
            }
        }

        // ------------------------------------------------------------------ HUD

        private float Accuracy(out int judged)
        {
            judged = 0;
            for (int i = 0; i < counts.Length; i++) judged += counts[i];
            float weighted = counts[0] + counts[1] * 0.75f + counts[2] * 0.5f + counts[3] * 0.25f;
            return judged > 0 ? 100f * weighted / judged : 0f;
        }

        private void UpdateScore()
        {
            if (scoreText == null) return;
            float accuracy = Accuracy(out int judged);
            string acc = judged > 0 ? accuracy.ToString("0.0") + "%" : "--";
            scoreText.text = "SCORE " + score.ToString("N0") + "    " + acc;
        }

        private void UpdateHearts()
        {
            if (performerHeartCounter != null) performerHeartCounter.Show(hearts);
            for (int i = 0; i < performerHeartFills.Length; i++)
                if (performerHeartFills[i] != null) performerHeartFills[i].fillAmount = i < hearts ? 1f : 0f;

            if (musicianHealthBar != null) musicianHealthBar.Show(musicianHealth);
            int n = musicianHeartFills.Length;
            for (int i = 0; i < n; i++)
                if (musicianHeartFills[i] != null) musicianHeartFills[i].fillAmount = Mathf.Clamp01(musicianHealth * n - i);
        }

        private void AnimateCurtains()
        {
            curtainAmount = Mathf.MoveTowards(curtainAmount, curtainTarget, Time.unscaledDeltaTime * 0.8f);
            SetCurtains(curtainAmount, false);
        }

        private void SetCurtains(float amount, bool instant)
        {
            if (instant) curtainAmount = curtainTarget = amount;
            float eased = Mathf.SmoothStep(0f, 1f, curtainAmount);
            if (curtainLeft != null)
            {
                var max = curtainLeft.anchorMax;
                max.x = Mathf.Lerp(leftOpenMax, 0.52f, eased);
                curtainLeft.anchorMax = max;
            }
            if (curtainRight != null)
            {
                var min = curtainRight.anchorMin;
                min.x = Mathf.Lerp(rightOpenMin, 0.48f, eased);
                curtainRight.anchorMin = min;
            }
        }
    }
}
