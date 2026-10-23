// Koikatsu Sunshine port of FreeHStudioSceneLoader.
// Modified for Koikatsu Sunshine, 2026. Licensed under GPL-3.0.
//
// The data list of the scene files: what each scene contains (Timeline animation, female/male characters, missing
// mods), used by the filters of the scene list. Finding this out means reading the whole scene, so the results are
// kept in BepInEx/config and only read again when the file changed.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace KKS_FreeHStudioSceneLoader
{
    internal class SceneSummary
    {
        public long Size;
        public long Time;
        public int Females;
        public int Males;
        /// <summary>The scene has Timeline keyframes.</summary>
        public bool Animation;
        /// <summary>Kinds of mods Sideloader reported missing while reading the scene.</summary>
        public int MissingMods;
        /// <summary>The scene could not be read.</summary>
        public bool Failed;
        /// <summary>Sideloader failed while resolving the scene's mod IDs (an error in the log).</summary>
        public bool ResolveError;

        /// <summary>A mod error of the filter: missing mods, a Sideloader error, or a file that can't be read.</summary>
        public bool ModError => MissingMods > 0 || ResolveError || Failed;
    }

    internal static class SceneIndex
    {
        private static readonly string FilePath = Path.Combine(BepInEx.Paths.ConfigPath, "KKS_FreeHStudioSceneLoader.scenelist.txt");
        private const string Header = "# KKS_FreeHStudioSceneLoader scene data list 1";
        private static Dictionary<string, SceneSummary> _all;
        /// <summary>Entries already compared with their file in this list (null: not in the list or changed).</summary>
        private static readonly Dictionary<string, SceneSummary> Checked = new Dictionary<string, SceneSummary>(StringComparer.OrdinalIgnoreCase);
        private static bool _dirty;

        private static string Key(string path) => Path.GetFullPath(path).ToLowerInvariant();

        /// <summary>The scene's entry when it is up to date (each file is compared once until <see cref="Forget"/>).</summary>
        internal static SceneSummary Get(string path)
        {
            if (Checked.TryGetValue(path, out var known)) return known;
            Load();
            SceneSummary result = null;
            try
            {
                if (_all.TryGetValue(Key(path), out var entry))
                {
                    var file = new FileInfo(path);
                    if (file.Exists && file.Length == entry.Size && file.LastWriteTimeUtc.Ticks == entry.Time) result = entry;
                }
            }
            catch { }
            Checked[path] = result;
            return result;
        }

        internal static void Set(string path, SceneSummary summary)
        {
            Load();
            try
            {
                var file = new FileInfo(path);
                summary.Size = file.Length;
                summary.Time = file.LastWriteTimeUtc.Ticks;
            }
            catch { return; }
            _all[Key(path)] = summary;
            Checked[path] = summary;
            _dirty = true;
        }

        /// <summary>Compare the files with the list again (after the scene list was read again).</summary>
        internal static void Forget() => Checked.Clear();

        /// <summary>Leaves out the scenes whose file is gone.</summary>
        internal static void RemoveDeleted()
        {
            Load();
            foreach (var key in _all.Keys.Where(k => !File.Exists(k)).ToList())
            {
                _all.Remove(key);
                _dirty = true;
            }
        }

        internal static void Save()
        {
            if (!_dirty || _all == null) return;
            _dirty = false;
            try
            {
                var lines = new List<string> { Header };
                foreach (var kv in _all.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    var s = kv.Value;
                    lines.Add(string.Join("\t", new[]
                    {
                        kv.Key, s.Size.ToString(CultureInfo.InvariantCulture), s.Time.ToString(CultureInfo.InvariantCulture),
                        s.Females.ToString(CultureInfo.InvariantCulture), s.Males.ToString(CultureInfo.InvariantCulture),
                        s.Animation ? "1" : "0", s.MissingMods.ToString(CultureInfo.InvariantCulture), s.Failed ? "1" : "0",
                        s.ResolveError ? "1" : "0",
                    }));
                }
                var tmp = FilePath + ".tmp";
                File.WriteAllLines(tmp, lines.ToArray(), new System.Text.UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not save the scene data list to {FilePath}: {ex.Message}");
            }
        }

        private static void Load()
        {
            if (_all != null) return;
            _all = new Dictionary<string, SceneSummary>();
            if (!File.Exists(FilePath)) return;
            try
            {
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    if (line.StartsWith("#")) continue;
                    var cols = line.Split('\t');
                    if (cols.Length < 8) continue;
                    _all[cols[0]] = new SceneSummary
                    {
                        Size = long.Parse(cols[1], CultureInfo.InvariantCulture),
                        Time = long.Parse(cols[2], CultureInfo.InvariantCulture),
                        Females = int.Parse(cols[3], CultureInfo.InvariantCulture),
                        Males = int.Parse(cols[4], CultureInfo.InvariantCulture),
                        Animation = cols[5] == "1",
                        MissingMods = int.Parse(cols[6], CultureInfo.InvariantCulture),
                        Failed = cols[7] == "1",
                        ResolveError = cols.Length > 8 && cols[8] == "1",
                    };
                }
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not read the scene data list {FilePath}: {ex.Message}");
            }
        }
    }
}
