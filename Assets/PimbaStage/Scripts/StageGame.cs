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

        [Header("Songs (picked in Options)")]
        public List<PimbaSong> songs = new List<PimbaSong>();

        [Header("Systems")]
        public Conductor conductor;
        public RhythmLane lane;
        public StageScroller scroller;
        public StagePlayer player;
        public StageMenu menu;
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
        public Image[] musicianHeartFills = new Image[0];

        [Header("UI")]
        public Text bannerText;
        public Text countdownText;
        public Text scoreText;
        public GameObject pausePanel;
        public GameObject resultsPanel;
        public Text resultsTitle;
        public Text resultsBody;
        public RectTransform curtainLeft;
        public RectTransform curtainRight;
        public BeatPulse[] beatPulses = new BeatPulse[0];

        private enum State { Menu, Countdown, Playing, Paused, Ended }

        private State state = State.Menu, stateBeforePause;
        private PimbaSong song;
        private int hearts, heartsLost, score, roses, totalRoses;
        private float musicianHealth = 1f;
        private readonly int[] counts = new int[5];
        private bool won;
        private float endTimer, curtainAmount = 1f, curtainTarget = 1f;
        private float leftOpenMax, rightOpenMin;

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
            song = CurrentSong();
            if (bannerText != null) bannerText.text = song.BannerText;
            ApplyPerformerDifficulty();

            if (skipMenu)
            {
                skipMenu = false;
                StartShow();
            }
            else ShowMenu();
        }

        private PimbaSong CurrentSong()
        {
            var chosen = menu != null ? menu.SelectedSong : null;
            return chosen != null ? chosen : songs[0];
        }

        private void ApplyPerformerDifficulty()
        {
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
                    if (resultsPanel != null && !resultsPanel.activeSelf && curtainAmount > 0.95f) resultsPanel.SetActive(true);
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
            if (resultsTitle != null)
            {
                resultsTitle.text = success ? "BRAVO!" : "BOOO!";
                resultsTitle.color = success ? new Color(1f, 0.84f, 0.2f) : new Color(1f, 0.4f, 0.35f);
            }
            if (resultsBody != null)
            {
                string body = reason + "\n\n"
                    + "Score  " + score.ToString("N0") + "\n"
                    + "Amazing " + counts[0] + "   Good " + counts[1] + "   OK " + counts[2] + "   Bad " + counts[3] + "   Miss " + counts[4] + "\n"
                    + "Accuracy " + accuracy.ToString("0.0") + "%     Max combo " + lane.MaxCombo + "\n"
                    + "Roses " + roses + "/" + totalRoses + "     Hearts lost " + heartsLost + "\n"
                    + "Performer " + GameSettings.PerformerDifficulty + "     Musician " + GameSettings.MusicianDifficulty + "\n";
                if (chartPath != null) body += "\nChart saved: " + chartPath + "\n";
                body += "\nENTER / R  play again        ESC  main menu";
                resultsBody.text = body;
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
            for (int i = 0; i < performerHeartFills.Length; i++)
                if (performerHeartFills[i] != null) performerHeartFills[i].fillAmount = i < hearts ? 1f : 0f;

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
