using UnityEngine;

namespace PimbaStage
{
    public enum Difficulty { Easy = 0, Normal = 1, Hard = 2 }

    /// <summary>Settings chosen in the Options menu. Saved between sessions (PlayerPrefs).</summary>
    public static class GameSettings
    {
        private const string PerformerKey = "PimbaStage.PerformerDifficulty";
        private const string MusicianKey = "PimbaStage.MusicianDifficulty";
        private const string SongKey = "PimbaStage.Song";
        private const string VolumeKey = "PimbaStage.Volume";
        private const string MapKey = "PimbaStage.Map";

        /// <summary>Player 1: how many obstacles are on stage.</summary>
        public static Difficulty PerformerDifficulty
        {
            get => (Difficulty)Mathf.Clamp(PlayerPrefs.GetInt(PerformerKey, 1), 0, 2);
            set { PlayerPrefs.SetInt(PerformerKey, (int)value); PlayerPrefs.Save(); }
        }

        /// <summary>Player 2: how many arrows the chart has.</summary>
        public static Difficulty MusicianDifficulty
        {
            get => (Difficulty)Mathf.Clamp(PlayerPrefs.GetInt(MusicianKey, 1), 0, 2);
            set { PlayerPrefs.SetInt(MusicianKey, (int)value); PlayerPrefs.Save(); }
        }

        /// <summary>Asset name of the chosen song.</summary>
        public static string SongName
        {
            get => PlayerPrefs.GetString(SongKey, "");
            set { PlayerPrefs.SetString(SongKey, value); PlayerPrefs.Save(); }
        }

        /// <summary>Name of the player-made map to play on (3D version); empty = the original stage.</summary>
        public static string MapName
        {
            get => PlayerPrefs.GetString(MapKey, "");
            set { PlayerPrefs.SetString(MapKey, value ?? ""); PlayerPrefs.Save(); }
        }

        /// <summary>Master volume 0-1 (applied straight away).</summary>
        public static float Volume
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 0.8f));
            set
            {
                float v = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(VolumeKey, v);
                AudioListener.volume = v;
            }
        }

        public static void ApplyVolume() => AudioListener.volume = Volume;

        public static void Save() => PlayerPrefs.Save();
    }
}
