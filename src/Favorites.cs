// Koikatsu Sunshine port of FreeHStudioSceneLoader.
// Modified for Koikatsu Sunshine, 2026. Licensed under GPL-3.0.
//
// The favorite scenes of the scene list (the ★ checkbox), kept in BepInEx/config, one scene per line (the same key as
// the scene settings: the path under UserData/studio/scene, or the full path for scenes elsewhere).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KKS_FreeHStudioSceneLoader
{
    internal static class Favorites
    {
        private static readonly string FilePath = Path.Combine(BepInEx.Paths.ConfigPath, "KKS_FreeHStudioSceneLoader.favorites.txt");
        private const string Header = "# KKS_FreeHStudioSceneLoader favorite scenes 1";
        private static HashSet<string> _all;
        /// <summary>Answers by file path, so the list does not work out the key of every scene each frame.</summary>
        private static readonly Dictionary<string, bool> Known = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        internal static bool IsFavorite(string path)
        {
            if (path == null) return false;
            if (Known.TryGetValue(path, out var known)) return known;
            Load();
            bool result;
            try { result = _all.Contains(SceneSettingsStore.Key(path)); }
            catch { result = false; }
            Known[path] = result;
            return result;
        }

        internal static void Set(string path, bool favorite)
        {
            if (path == null || IsFavorite(path) == favorite) return;
            string key;
            try { key = SceneSettingsStore.Key(path); }
            catch { return; }
            if (favorite) _all.Add(key); else _all.Remove(key);
            Known[path] = favorite;
            Save();
        }

        private static void Save()
        {
            try
            {
                var lines = new List<string> { Header };
                lines.AddRange(_all.OrderBy(k => k, StringComparer.Ordinal));
                var tmp = FilePath + ".tmp";
                File.WriteAllLines(tmp, lines.ToArray(), new System.Text.UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not save the favorite scenes to {FilePath}: {ex.Message}");
            }
        }

        private static void Load()
        {
            if (_all != null) return;
            _all = new HashSet<string>(StringComparer.Ordinal);
            if (!File.Exists(FilePath)) return;
            try
            {
                foreach (var line in File.ReadAllLines(FilePath))
                    if (line.Length > 0 && !line.StartsWith("#")) _all.Add(line.Trim());
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not read the favorite scenes {FilePath}: {ex.Message}");
            }
        }
    }
}
