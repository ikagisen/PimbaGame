using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace PimbaStage
{
    /// <summary>One 3D piece on a player-made map. x, y = bottom centre on the stage.</summary>
    [Serializable]
    public class MapPiece
    {
        public string kind;
        public float x;
        public float y;
        public float size = 1f;
        public bool flip;
    }

    /// <summary>A player-made map: a name and the 3D pieces on the stage.</summary>
    [Serializable]
    public class StageMap
    {
        public string name = "";
        public List<MapPiece> pieces = new List<MapPiece>();
        [NonSerialized] public string path;
    }

    /// <summary>
    /// Player-made maps, one JSON file each in the game's save folder (Application.persistentDataPath/Maps),
    /// so they work in the built game too.
    /// </summary>
    public static class MapStorage
    {
        public const int MaxNameLength = 14;

        public static string Folder => Path.Combine(Application.persistentDataPath, "Maps");

        /// <summary>Every saved map, sorted by name.</summary>
        public static List<StageMap> LoadAll()
        {
            var maps = new List<StageMap>();
            if (!Directory.Exists(Folder)) return maps;
            foreach (string file in Directory.GetFiles(Folder, "*.json"))
            {
                try
                {
                    var map = JsonUtility.FromJson<StageMap>(File.ReadAllText(file));
                    if (map == null) continue;
                    if (map.pieces == null) map.pieces = new List<MapPiece>();
                    map.pieces.RemoveAll(p => p == null || string.IsNullOrEmpty(p.kind));
                    map.name = CleanName(string.IsNullOrEmpty(map.name) ? Path.GetFileNameWithoutExtension(file) : map.name);
                    map.path = file;
                    maps.Add(map);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Pimba Stage: could not read the map " + file + ": " + e.Message);
                }
            }
            maps.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return maps;
        }

        /// <summary>The saved map with this name, or null.</summary>
        public static StageMap Find(string name)
        {
            name = CleanName(name);
            if (name.Length == 0) return null;
            return LoadAll().Find(m => m.name == name);
        }

        /// <summary>Saves the map under its name. previousPath (the file it was loaded from) is removed if the map was renamed.</summary>
        public static string Save(StageMap map, string previousPath)
        {
            Directory.CreateDirectory(Folder);
            map.name = CleanName(map.name);
            string path = Path.Combine(Folder, map.name.Replace(' ', '_') + ".json");
            File.WriteAllText(path, JsonUtility.ToJson(map, true));
            if (!string.IsNullOrEmpty(previousPath) && !string.Equals(Path.GetFullPath(previousPath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)
                && File.Exists(previousPath))
                File.Delete(previousPath);
            map.path = path;
            return path;
        }

        public static void Delete(StageMap map)
        {
            if (map != null && !string.IsNullOrEmpty(map.path) && File.Exists(map.path)) File.Delete(map.path);
        }

        /// <summary>Upper case; letters, digits, spaces and dashes; at most MaxNameLength characters.</summary>
        public static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var sb = new StringBuilder();
            foreach (char raw in name.ToUpperInvariant())
            {
                char c = raw;
                bool ok = (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == ' ';
                if (!ok) continue;
                if (c == ' ' && (sb.Length == 0 || sb[sb.Length - 1] == ' ')) continue;
                sb.Append(c);
                if (sb.Length >= MaxNameLength) break;
            }
            return sb.ToString().Trim();
        }
    }
}
