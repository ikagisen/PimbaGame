using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PimbaStage
{
    public enum Judgement { Amazing = 0, Good = 1, OK = 2, Bad = 3, Miss = 4 }

    /// <summary>
    /// Player 2's arrow lane (Friday Night Funkin' style). Arrows fall towards the hit box;
    /// pressing the matching arrow key on time gives AMAZING / GOOD / OK / BAD.
    /// </summary>
    public class RhythmLane : MonoBehaviour
    {
        public static readonly Color[] ArrowColors =
        {
            new Color(0.76f, 0.29f, 0.60f), // left  - purple
            new Color(0.00f, 0.90f, 1.00f), // down  - cyan
            new Color(0.10f, 0.95f, 0.10f), // up    - green
            new Color(0.98f, 0.22f, 0.25f), // right - red
        };
        public static readonly string[] RatingLabels = { "AMAZING!", "GOOD!", "OK", "BAD", "MISS" };
        public static readonly Color[] RatingColors =
        {
            new Color(1.00f, 0.84f, 0.20f),
            new Color(0.45f, 1.00f, 0.45f),
            new Color(0.55f, 0.80f, 1.00f),
            new Color(1.00f, 0.45f, 0.20f),
            new Color(0.65f, 0.65f, 0.65f),
        };

        [Header("Look (wired up by the stage builder)")]
        public RectTransform noteArea;
        public Sprite arrowSprite;
        public RectTransform singleReceptor;
        public RectTransform[] laneReceptors = new RectTransform[0];
        public Image hitFlash;
        public Text ratingText;
        public Text comboText;
        public Text modeText;

        [Header("Gameplay")]
        [Tooltip("Off = one column of mixed arrows (like the sketch). On = four columns like Friday Night Funkin'.")]
        public bool fourLanes;
        [Tooltip("Arrows fall from the top down to the hit box.")]
        public bool downScroll = true;
        [Tooltip("How fast the arrows move, in UI pixels per second.")]
        public float scrollSpeed = 520f;
        [Tooltip("Distance of the hit box from the bottom of the lane.")]
        public float hitLineOffset = 80f;
        public float noteSize = 96f;
        public float fourLaneNoteSize = 64f;
        public float laneSpacing = 70f;
        [Tooltip("Pressing an arrow when nothing is there does not count as a miss.")]
        public bool ghostTapping = true;

        [Header("Timing windows (milliseconds)")]
        public float amazingMs = 45f;
        public float goodMs = 90f;
        public float okMs = 135f;
        public float badMs = 166f;

        [Header("Player 2")]
        [Tooltip("Which connected gamepad Player 2 uses (0 = first). Arrow keys always work.")]
        public int gamepadIndex = 1;
        [Tooltip("The game hits every arrow perfectly (toggle with F1). Handy for testing alone.")]
        public bool botplay;

        public event Action<Judgement> Judged;
        public int Combo { get; private set; }
        public int MaxCombo { get; private set; }
        public int NoteCount => chart != null ? chart.Count : 0;
        public bool Recording { get; private set; }

        private class LiveNote
        {
            public NoteData data;
            public RectTransform rect;
            public Image image;
            public bool judged, hit;
        }

        private Conductor conductor;
        private List<NoteData> chart;
        private int nextSpawn;
        private bool running;
        private readonly List<LiveNote> live = new List<LiveNote>();
        private readonly Stack<LiveNote> pool = new Stack<LiveNote>();
        private readonly List<string> recorded = new List<string>();
        private float ratingAge = 99f, flashAge = 99f;
        private Color flashColor = Color.white;
        private RectTransform punched;

        private float CurrentNoteSize => fourLanes ? fourLaneNoteSize : noteSize;
        private double Window => badMs / 1000.0;

        private void Start()
        {
            LayoutReceptors();
            if (ratingText != null) ratingText.text = "";
            if (comboText != null) comboText.text = "";
            UpdateModeText();
        }

        public void Begin(Conductor songConductor, List<NoteData> notes, bool record)
        {
            conductor = songConductor;
            chart = notes;
            nextSpawn = 0;
            Combo = 0;
            MaxCombo = 0;
            Recording = record;
            recorded.Clear();
            for (int i = live.Count - 1; i >= 0; i--) Recycle(live[i]);
            live.Clear();
            running = true;
            LayoutReceptors();
            UpdateModeText();
        }

        public void Stop()
        {
            running = false;
            for (int i = live.Count - 1; i >= 0; i--) Recycle(live[i]);
            live.Clear();
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
            {
                botplay = !botplay;
                UpdateModeText();
            }

            if (!running || conductor == null || chart == null || Time.timeScale == 0f)
            {
                AnimateFeedback();
                return;
            }

            double now = conductor.SongTime;
            SpawnNotes(now);
            if (Recording) RecordInput();
            else if (botplay) BotPlay(now);
            else HandleInput(now);
            if (!Recording) MissPassedNotes(now);
            PositionNotes(now);
            AnimateFeedback();
        }

        // ------------------------------------------------------------------ input & judging

        private void HandleInput(double now)
        {
            for (int d = 0; d < 4; d++)
                if (WasPressed((ArrowDir)d)) Press((ArrowDir)d, now);
        }

        private void Press(ArrowDir dir, double now)
        {
            Flash(dir);
            LiveNote target = null;
            for (int i = 0; i < live.Count; i++)
            {
                var n = live[i];
                if (n.judged) continue;
                if (fourLanes && n.data.dir != dir) continue;
                double diff = n.data.time - now;
                if (diff > Window) break;
                if (diff >= -Window) { target = n; break; }
            }

            if (target == null)
            {
                if (!ghostTapping)
                {
                    Combo = 0;
                    ShowRating(Judgement.Miss);
                    Judged?.Invoke(Judgement.Miss);
                }
                return;
            }

            if (target.data.dir != dir)
            {
                Judge(target, Judgement.Miss); // wrong arrow
                return;
            }

            double errorMs = Math.Abs(target.data.time - now) * 1000.0;
            Judgement j = errorMs <= amazingMs ? Judgement.Amazing
                        : errorMs <= goodMs ? Judgement.Good
                        : errorMs <= okMs ? Judgement.OK
                        : Judgement.Bad;
            Judge(target, j);
        }

        private void BotPlay(double now)
        {
            for (int i = 0; i < live.Count; i++)
            {
                var n = live[i];
                if (n.judged || n.data.time > now) continue;
                Flash(n.data.dir);
                Judge(n, Judgement.Amazing);
            }
        }

        private void MissPassedNotes(double now)
        {
            for (int i = 0; i < live.Count; i++)
            {
                var n = live[i];
                if (!n.judged && now - n.data.time > Window) Judge(n, Judgement.Miss);
            }
        }

        private void Judge(LiveNote n, Judgement j)
        {
            n.judged = true;
            if (j == Judgement.Miss)
            {
                Combo = 0;
                n.image.color = new Color(0.35f, 0.35f, 0.35f, 0.45f);
            }
            else
            {
                Combo++;
                MaxCombo = Math.Max(MaxCombo, Combo);
                n.hit = true;
            }
            ShowRating(j);
            Judged?.Invoke(j);
        }

        private bool WasPressed(ArrowDir dir)
        {
            bool pressed = false;
            var kb = Keyboard.current;
            if (kb != null)
            {
                switch (dir)
                {
                    case ArrowDir.Left: pressed = kb.leftArrowKey.wasPressedThisFrame; break;
                    case ArrowDir.Down: pressed = kb.downArrowKey.wasPressedThisFrame; break;
                    case ArrowDir.Up: pressed = kb.upArrowKey.wasPressedThisFrame; break;
                    case ArrowDir.Right: pressed = kb.rightArrowKey.wasPressedThisFrame; break;
                }
            }
            var pad = gamepadIndex >= 0 && gamepadIndex < Gamepad.all.Count ? Gamepad.all[gamepadIndex] : null;
            if (pad != null)
            {
                switch (dir)
                {
                    case ArrowDir.Left: pressed |= pad.dpad.left.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame; break;
                    case ArrowDir.Down: pressed |= pad.dpad.down.wasPressedThisFrame || pad.buttonSouth.wasPressedThisFrame; break;
                    case ArrowDir.Up: pressed |= pad.dpad.up.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame; break;
                    case ArrowDir.Right: pressed |= pad.dpad.right.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame; break;
                }
            }
            return pressed;
        }

        // ------------------------------------------------------------------ chart recording

        private void RecordInput()
        {
            for (int d = 0; d < 4; d++)
            {
                var dir = (ArrowDir)d;
                if (!WasPressed(dir)) continue;
                Flash(dir);
                double beat = Math.Round(conductor.Beat * 4.0) / 4.0;
                recorded.Add(beat.ToString("0.##", CultureInfo.InvariantCulture) + " " + PimbaSong.DirLetter(dir));
                if (ratingText != null)
                {
                    ratingText.text = "REC " + PimbaSong.DirLetter(dir);
                    ratingText.color = ArrowColors[d];
                    ratingAge = 0f;
                }
            }
        }

        /// <summary>Writes the recorded arrows to Assets/PimbaStage/Charts. Returns the asset path (editor only).</summary>
        public string SaveRecording(PimbaSong song)
        {
            if (recorded.Count == 0 || song == null) return null;
            string text = "# " + song.title + " - recorded " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n"
                        + "# one arrow per line: <beat> <L|D|U|R>\n"
                        + string.Join("\n", recorded) + "\n";
#if UNITY_EDITOR
            string safe = string.Join("_", song.name.Split(Path.GetInvalidFileNameChars()));
            string folder = Path.Combine(Application.dataPath, "PimbaStage", "Charts");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, safe + "_chart.txt"), text);
            UnityEditor.AssetDatabase.Refresh();
            string assetPath = "Assets/PimbaStage/Charts/" + safe + "_chart.txt";
            Debug.Log("Chart saved to " + assetPath + " (" + recorded.Count + " arrows). Drag it into the song's Chart File and untick Record Chart.");
            return assetPath;
#else
            Debug.Log(text);
            return null;
#endif
        }

        // ------------------------------------------------------------------ visuals

        private void SpawnNotes(double now)
        {
            float height = noteArea.rect.height;
            double lookAhead = (height + CurrentNoteSize) / scrollSpeed + 0.1;
            while (nextSpawn < chart.Count && chart[nextSpawn].time - now <= lookAhead)
            {
                var data = chart[nextSpawn++];
                var n = pool.Count > 0 ? pool.Pop() : CreateNote();
                n.data = data;
                n.judged = false;
                n.hit = false;
                n.image.color = ArrowColors[(int)data.dir];
                n.rect.sizeDelta = new Vector2(CurrentNoteSize, CurrentNoteSize);
                n.rect.localEulerAngles = new Vector3(0f, 0f, Rotation(data.dir));
                n.rect.gameObject.SetActive(true);
                live.Add(n);
            }
        }

        private void PositionNotes(double now)
        {
            float height = noteArea.rect.height;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var n = live[i];
                if (n.hit)
                {
                    Recycle(n);
                    live.RemoveAt(i);
                    continue;
                }
                float y = NoteY(n.data.time - now, height);
                n.rect.anchoredPosition = new Vector2(LaneX(n.data.dir), y);
                bool gone = downScroll ? y < -CurrentNoteSize : y > height + CurrentNoteSize;
                if (gone && n.judged)
                {
                    Recycle(n);
                    live.RemoveAt(i);
                }
            }
        }

        private float NoteY(double secondsUntilHit, float height)
        {
            float travel = (float)secondsUntilHit * scrollSpeed;
            return downScroll ? hitLineOffset + travel : height - hitLineOffset - travel;
        }

        private float LaneX(ArrowDir dir) => fourLanes ? ((int)dir - 1.5f) * laneSpacing : 0f;

        private static float Rotation(ArrowDir dir)
        {
            switch (dir)
            {
                case ArrowDir.Left: return 90f;
                case ArrowDir.Down: return 180f;
                case ArrowDir.Right: return -90f;
                default: return 0f;
            }
        }

        private LiveNote CreateNote()
        {
            var go = new GameObject("Arrow", typeof(RectTransform), typeof(Image));
            go.layer = gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(noteArea, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            var image = go.GetComponent<Image>();
            image.sprite = arrowSprite;
            image.raycastTarget = false;
            image.preserveAspect = true;
            return new LiveNote { rect = rect, image = image };
        }

        private void Recycle(LiveNote n)
        {
            n.rect.gameObject.SetActive(false);
            pool.Push(n);
        }

        private void LayoutReceptors()
        {
            if (noteArea == null) return;
            float height = noteArea.rect.height;
            float y = downScroll ? hitLineOffset : height - hitLineOffset;

            if (singleReceptor != null)
            {
                singleReceptor.gameObject.SetActive(!fourLanes);
                singleReceptor.anchoredPosition = new Vector2(0f, y);
            }
            for (int i = 0; i < laneReceptors.Length; i++)
            {
                if (laneReceptors[i] == null) continue;
                laneReceptors[i].gameObject.SetActive(fourLanes);
                laneReceptors[i].anchoredPosition = new Vector2(LaneX((ArrowDir)i), y);
                laneReceptors[i].sizeDelta = new Vector2(fourLaneNoteSize, fourLaneNoteSize);
                laneReceptors[i].localEulerAngles = new Vector3(0f, 0f, Rotation((ArrowDir)i));
            }
        }

        private void Flash(ArrowDir dir)
        {
            flashColor = ArrowColors[(int)dir];
            flashAge = 0f;
            float y = downScroll ? hitLineOffset : noteArea.rect.height - hitLineOffset;
            if (hitFlash != null) hitFlash.rectTransform.anchoredPosition = new Vector2(LaneX(dir), y);

            punched = fourLanes && (int)dir < laneReceptors.Length ? laneReceptors[(int)dir] : singleReceptor;
            if (punched != null) punched.localScale = Vector3.one * 1.15f;
            if (!fourLanes && singleReceptor != null)
            {
                var img = singleReceptor.GetComponent<Image>();
                if (img != null) img.color = Color.Lerp(Color.white, flashColor, 0.7f);
            }
        }

        private void ShowRating(Judgement j)
        {
            ratingAge = 0f;
            if (ratingText != null)
            {
                ratingText.text = RatingLabels[(int)j];
                ratingText.color = RatingColors[(int)j];
            }
            if (comboText != null) comboText.text = Combo >= 3 ? Combo + " COMBO" : "";
        }

        private void AnimateFeedback()
        {
            float dt = Time.unscaledDeltaTime;
            ratingAge += dt;
            flashAge += dt;

            if (ratingText != null)
            {
                float s = ratingAge < 0.1f ? Mathf.Lerp(1.35f, 1f, ratingAge / 0.1f) : 1f;
                ratingText.rectTransform.localScale = new Vector3(s, s, 1f);
                var c = ratingText.color;
                c.a = Mathf.Clamp01(1.5f - ratingAge * 1.6f);
                ratingText.color = c;
            }
            if (comboText != null)
            {
                var c = comboText.color;
                c.a = Mathf.Clamp01(1.5f - ratingAge * 1.6f);
                comboText.color = c;
            }
            if (hitFlash != null)
            {
                var c = flashColor;
                c.a = Mathf.Clamp01(0.85f - flashAge * 4f);
                hitFlash.color = c;
                float s = 1f + flashAge * 1.5f;
                hitFlash.rectTransform.localScale = new Vector3(s, s, 1f);
            }
            if (punched != null)
                punched.localScale = Vector3.Lerp(punched.localScale, Vector3.one, 14f * dt);
            if (!fourLanes && singleReceptor != null)
            {
                var img = singleReceptor.GetComponent<Image>();
                if (img != null) img.color = Color.Lerp(img.color, Color.white, 8f * dt);
            }
        }

        private void UpdateModeText()
        {
            if (modeText == null) return;
            modeText.text = Recording ? "RECORDING CHART" : botplay ? "BOTPLAY  (F1)" : "";
        }
    }
}
