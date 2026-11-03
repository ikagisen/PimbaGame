using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PimbaStage
{
    public enum ArrowDir { Left = 0, Down = 1, Up = 2, Right = 3 }

    [Serializable]
    public struct NoteData
    {
        public double time;   // seconds from the start of the audio
        public double beat;
        public ArrowDir dir;

        public NoteData(double beat, ArrowDir dir, double time)
        {
            this.beat = beat;
            this.dir = dir;
            this.time = time;
        }
    }

    /// <summary>
    /// One playable song: the audio, its tempo, and the arrows ("chart") Player 2 has to hit.
    /// Create one with Assets > Create > Pimba Stage > Song.
    /// </summary>
    [CreateAssetMenu(fileName = "New Pimba Song", menuName = "Pimba Stage/Song", order = 0)]
    public class PimbaSong : ScriptableObject
    {
        [Header("Shown on the stage banner")]
        public string title = "New Song";
        public string artist = "";

        [Header("Music")]
        [Tooltip("Your song's audio file (mp3, ogg or wav).")]
        public AudioClip music;
        [Tooltip("Ignore the audio file and play the built-in generated tune (handy for testing).")]
        public bool useDemoTrack;
        [Min(30f)] public float bpm = 120f;
        [Tooltip("Seconds from the start of the audio file to the first beat. Raise it if the arrows arrive before the music.")]
        public float firstBeatOffset;
        [Range(0f, 1f)] public float volume = 0.9f;

        [Header("Chart (which arrows, and when)")]
        [Tooltip("Text file with one arrow per line: <beat> <L|D|U|R>, for example \"8.5 U\".")]
        public TextAsset chartFile;
        [Tooltip("Same format as the chart file, typed here instead.")]
        [TextArea(3, 12)] public string chartText = "";
        [Tooltip("Changes the generated arrows (and the demo tune).")]
        public int autoSeed = 7;
        [Tooltip("Generated arrows start at this beat (skips the intro).")]
        public float autoStartBeat = 8f;

        [Header("Chart recording (Unity editor only)")]
        [Tooltip("Play the song with no arrows. Every arrow key Player 2 presses is saved to Assets/PimbaStage/Charts as a chart for this song.")]
        public bool recordChart;

        public double SecondsPerBeat => 60.0 / Math.Max(1f, bpm);
        public double BeatToTime(double beat) => firstBeatOffset + beat * SecondsPerBeat;
        public double TimeToBeat(double time) => (time - firstBeatOffset) / SecondsPerBeat;
        public string BannerText => string.IsNullOrWhiteSpace(artist) ? title : artist;

        /// <summary>
        /// Builds the list of arrows for this song, sorted by time.
        /// Easy keeps at most one arrow per beat; Hard adds extra arrows to generated charts.
        /// </summary>
        public List<NoteData> BuildChart(double songLength, List<DemoTrack.MelodyNote> demoMelody, Difficulty arrows)
        {
            var notes = new List<NoteData>();
            if (recordChart) return notes;

            string text = chartFile != null ? chartFile.text : chartText;
            if (!string.IsNullOrWhiteSpace(text)) ParseChart(text, notes);
            else if (demoMelody != null && demoMelody.Count > 0) ChartFromMelody(demoMelody, notes, arrows);
            else AutoChart(songLength, notes, arrows);

            notes.Sort((a, b) => a.time.CompareTo(b.time));
            if (arrows == Difficulty.Easy) notes = OnePerBeat(notes);
            return notes;
        }

        private static List<NoteData> OnePerBeat(List<NoteData> notes)
        {
            var kept = new List<NoteData>(notes.Count);
            double last = double.NegativeInfinity;
            foreach (var n in notes)
            {
                if (n.beat - last < 0.999) continue;
                kept.Add(n);
                last = n.beat;
            }
            return kept;
        }

        private void ParseChart(string text, List<NoteData> notes)
        {
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;

                string[] parts = line.Split(new[] { ' ', '\t', ';', ':' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 ||
                    !double.TryParse(parts[0].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double beat) ||
                    !TryParseDir(parts[1], out ArrowDir dir))
                {
                    Debug.LogWarning($"{name}: chart line {i + 1} not understood: \"{line}\" (expected: <beat> <L|D|U|R>)");
                    continue;
                }
                notes.Add(new NoteData(beat, dir, BeatToTime(beat)));
            }
        }

        private void AutoChart(double songLength, List<NoteData> notes, Difficulty autoDensity)
        {
            var rng = new System.Random(autoSeed);
            double endBeat = TimeToBeat(songLength) - 2.0;
            int previous = -1, repeats = 0;

            for (double b = autoStartBeat; b <= endBeat; b += 0.5)
            {
                bool onBeat = Math.Abs(b - Math.Round(b)) < 0.01;
                double chance;
                switch (autoDensity)
                {
                    case Difficulty.Easy: chance = onBeat && ((int)Math.Round(b)) % 2 == 0 ? 0.9 : 0.0; break;
                    case Difficulty.Hard: chance = onBeat ? 0.95 : 0.6; break;
                    default: chance = onBeat ? 0.85 : 0.2; break;
                }
                if (rng.NextDouble() >= chance) continue;

                int d = rng.Next(4);
                if (d == previous && ++repeats >= 2)
                {
                    d = (d + 1 + rng.Next(3)) % 4;
                    repeats = 0;
                }
                else if (d != previous)
                {
                    repeats = 0;
                }
                previous = d;
                notes.Add(new NoteData(b, (ArrowDir)d, BeatToTime(b)));
            }
        }

        /// <summary>Arrows follow the melody: going up = Up/Right, going down = Down/Left.</summary>
        private void ChartFromMelody(List<DemoTrack.MelodyNote> melody, List<NoteData> notes, Difficulty arrows)
        {
            int previousPitch = int.MinValue;
            ArrowDir previousDir = ArrowDir.Left;
            for (int i = 0; i < melody.Count; i++)
            {
                var m = melody[i];
                bool onBeat = Math.Abs(m.beat - Math.Round(m.beat)) < 0.01;
                if (arrows == Difficulty.Easy && !onBeat) continue;

                // Hard: an extra arrow on the off-beat inside long notes
                if (arrows == Difficulty.Hard && previousPitch != int.MinValue && i > 0)
                {
                    double gapStart = melody[i - 1].beat;
                    if (m.beat - gapStart >= 1.0)
                    {
                        double extra = gapStart + 0.5;
                        notes.Add(new NoteData(extra, (ArrowDir)(((int)previousDir + 2) % 4), BeatToTime(extra)));
                    }
                }

                ArrowDir dir;
                if (previousPitch == int.MinValue) dir = ArrowDir.Up;
                else
                {
                    int delta = m.pitch - previousPitch;
                    if (delta >= 5) dir = ArrowDir.Right;
                    else if (delta > 0) dir = ArrowDir.Up;
                    else if (delta <= -5) dir = ArrowDir.Left;
                    else if (delta < 0) dir = ArrowDir.Down;
                    else dir = previousDir;
                }
                notes.Add(new NoteData(m.beat, dir, BeatToTime(m.beat)));
                previousPitch = m.pitch;
                previousDir = dir;
            }
        }

        public static bool TryParseDir(string token, out ArrowDir dir)
        {
            dir = ArrowDir.Left;
            if (string.IsNullOrEmpty(token)) return false;
            switch (char.ToUpperInvariant(token[0]))
            {
                case 'L': case '0': case '←': dir = ArrowDir.Left; return true;
                case 'D': case '1': case '↓': dir = ArrowDir.Down; return true;
                case 'U': case '2': case '↑': dir = ArrowDir.Up; return true;
                case 'R': case '3': case '→': dir = ArrowDir.Right; return true;
                default: return false;
            }
        }

        public static char DirLetter(ArrowDir dir) => "LDUR"[(int)dir];
    }
}
