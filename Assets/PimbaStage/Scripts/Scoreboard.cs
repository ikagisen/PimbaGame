using System;
using System.Collections.Generic;
using UnityEngine;

namespace PimbaStage
{
    /// <summary>One line of the scoreboard: a duo (Performer + Singer, 3 letters each) and their best score.</summary>
    [Serializable]
    public class ScoreEntry
    {
        public string performer;
        public string singer;
        public int score;
        public string levels; // difficulties the best score was played on, Performer/Singer, e.g. "NOR/HAR"
        public string song;
        public long order; // earlier records rank higher on equal scores
    }

    /// <summary>
    /// The arcade scoreboard, saved on this computer (PlayerPrefs). A duo is a Performer name + a Singer name;
    /// each duo has one line, holding its best score.
    /// </summary>
    public static class Scoreboard
    {
        public const int NameLength = 3;
        public const int MaxEntries = 500;
        private const string SaveKey = "PimbaStage.Scoreboard";
        private const string LastPerformerKey = "PimbaStage.LastPerformerName";
        private const string LastSingerKey = "PimbaStage.LastSingerName";

        [Serializable]
        private class SaveData
        {
            public List<ScoreEntry> entries = new List<ScoreEntry>();
            public long next;
        }

        public struct Result
        {
            public int place;          // 1 = first
            public int total;
            public bool newDuo;        // first record for this duo
            public bool improved;      // this show beat the duo's old best (or is their first)
            public int previousBest;
            public ScoreEntry entry;
        }

        private static SaveData data;

        /// <summary>Best first.</summary>
        public static List<ScoreEntry> Entries
        {
            get { Load(); return data.entries; }
        }

        public static string LastPerformer
        {
            get => Clean(PlayerPrefs.GetString(LastPerformerKey, "AAA"));
            set => PlayerPrefs.SetString(LastPerformerKey, Clean(value));
        }

        public static string LastSinger
        {
            get => Clean(PlayerPrefs.GetString(LastSingerKey, "AAA"));
            set => PlayerPrefs.SetString(LastSingerKey, Clean(value));
        }

        /// <summary>Saves a show's score for a duo. A duo that is already on the board keeps only its best score.</summary>
        public static Result Submit(string performer, string singer, int score, string song, string levels)
        {
            Load();
            performer = Clean(performer);
            singer = Clean(singer);
            var result = new Result();
            var entry = data.entries.Find(e => e.performer == performer && e.singer == singer);
            if (entry == null)
            {
                entry = new ScoreEntry { performer = performer, singer = singer, score = score, levels = levels, song = song, order = data.next++ };
                data.entries.Add(entry);
                result.newDuo = true;
                result.improved = true;
            }
            else
            {
                result.previousBest = entry.score;
                if (score > entry.score)
                {
                    entry.score = score;
                    entry.levels = levels;
                    entry.song = song;
                    entry.order = data.next++;
                    result.improved = true;
                }
            }
            Sort();
            result.entry = entry;
            result.place = data.entries.IndexOf(entry) + 1;
            result.total = data.entries.Count;
            if (data.entries.Count > MaxEntries) data.entries.RemoveRange(MaxEntries, data.entries.Count - MaxEntries);
            Save();
            return result;
        }

        /// <summary>Both players' difficulties, Performer first: "EAS/HAR".</summary>
        public static string LevelText(Difficulty performer, Difficulty singer) => Short(performer) + "/" + Short(singer);

        private static string Short(Difficulty d) => d == Difficulty.Easy ? "EAS" : d == Difficulty.Hard ? "HAR" : "NOR";

        /// <summary>Upper-case A-Z only, exactly 3 letters.</summary>
        public static string Clean(string name)
        {
            var chars = new char[NameLength];
            int n = 0;
            if (name != null)
                foreach (char c in name.ToUpperInvariant())
                    if (c >= 'A' && c <= 'Z' && n < NameLength) chars[n++] = c;
            while (n < NameLength) chars[n++] = 'A';
            return new string(chars);
        }

        private static void Load()
        {
            if (data != null) return;
            string json = PlayerPrefs.GetString(SaveKey, "");
            try { data = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<SaveData>(json); }
            catch (Exception e)
            {
                Debug.LogWarning("Pimba Stage: the saved scoreboard could not be read, starting a new one. " + e.Message);
                data = null;
            }
            if (data == null) data = new SaveData();
            if (data.entries == null) data.entries = new List<ScoreEntry>();
            data.entries.RemoveAll(e => e == null);
            foreach (var e in data.entries) { e.performer = Clean(e.performer); e.singer = Clean(e.singer); }
            Sort();
        }

        private static void Save()
        {
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }

        private static void Sort()
        {
            data.entries.Sort((a, b) => a.score != b.score ? b.score.CompareTo(a.score) : a.order.CompareTo(b.order));
        }
    }
}
