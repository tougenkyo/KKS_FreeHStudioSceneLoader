// Koikatsu Sunshine port of FreeHStudioSceneLoader.
// Modified for Koikatsu Sunshine, 2026. Licensed under GPL-3.0.
//
// Settings kept for each studio scene file: which characters are placed and whether the scene's sounds play.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace KKS_FreeHStudioSceneLoader
{
    internal class SceneSettings
    {
        /// <summary>The female/male switches of the selection window (all females, all males).</summary>
        public bool Females = true;
        public bool Males = true;
        /// <summary>Sound and voice items placed in the scene (they play as soon as they are loaded).</summary>
        public bool SoundItems;
        /// <summary>The characters' voice lists (Studio's voice playback), started when the characters are placed.</summary>
        public bool CharaVoices;
        /// <summary>The Free H map chosen when several use the scene's studio map (-1: not chosen).</summary>
        public int FreeHMap = -1;
        /// <summary>dicKey of the characters turned off in the selection window.</summary>
        public readonly HashSet<int> HiddenCharacters = new HashSet<int>();
    }

    /// <summary>
    /// Stores <see cref="SceneSettings"/> per scene file in BepInEx/config, keyed by the path under
    /// UserData/studio/scene. Scenes without saved settings use the defaults from the plugin's config.
    /// </summary>
    internal static class SceneSettingsStore
    {
        private static readonly string FilePath = Path.Combine(BepInEx.Paths.ConfigPath, "KKS_FreeHStudioSceneLoader.scenes.xml");
        private static readonly string SceneRoot = Path.GetFullPath(Path.Combine(BepInEx.Paths.GameRootPath, "UserData/studio/scene"));
        private static Dictionary<string, SceneSettings> _all;

        private static string Key(string scenePath)
        {
            var full = Path.GetFullPath(scenePath);
            if (full.StartsWith(SceneRoot, StringComparison.OrdinalIgnoreCase)) full = full.Substring(SceneRoot.Length);
            return full.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
        }

        internal static SceneSettings Get(string scenePath)
        {
            Load();
            if (_all.TryGetValue(Key(scenePath), out var settings)) return settings;
            return Defaults();
        }

        private static SceneSettings Defaults() => new SceneSettings
        {
            Females = FreeHStudioSceneLoader.LoadFemales.Value,
            Males = FreeHStudioSceneLoader.LoadMales.Value,
            SoundItems = FreeHStudioSceneLoader.PlaySoundItems.Value,
            CharaVoices = FreeHStudioSceneLoader.PlayCharaVoices.Value,
        };

        private static bool ReadBool(XmlElement e, string name, bool fallback) =>
            e.HasAttribute(name) ? e.GetAttribute(name) == "true" : fallback;

        internal static void Save(string scenePath, SceneSettings settings)
        {
            Load();
            _all[Key(scenePath)] = settings;
            try
            {
                var doc = new XmlDocument();
                var root = doc.AppendChild(doc.CreateElement("scenes"));
                foreach (var kv in _all.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    var e = (XmlElement)root.AppendChild(doc.CreateElement("scene"));
                    e.SetAttribute("path", kv.Key);
                    e.SetAttribute("females", kv.Value.Females ? "true" : "false");
                    e.SetAttribute("males", kv.Value.Males ? "true" : "false");
                    e.SetAttribute("soundItems", kv.Value.SoundItems ? "true" : "false");
                    e.SetAttribute("charaVoices", kv.Value.CharaVoices ? "true" : "false");
                    if (kv.Value.FreeHMap >= 0) e.SetAttribute("freeHMap", kv.Value.FreeHMap.ToString());
                    if (kv.Value.HiddenCharacters.Count > 0)
                        e.SetAttribute("hiddenCharacters", string.Join(",", kv.Value.HiddenCharacters.OrderBy(i => i).Select(i => i.ToString()).ToArray()));
                }
                var tmp = FilePath + ".tmp";
                doc.Save(tmp);
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not save the scene settings to {FilePath}: {ex.Message}");
            }
        }

        private static void Load()
        {
            if (_all != null) return;
            _all = new Dictionary<string, SceneSettings>();
            if (!File.Exists(FilePath)) return;
            try
            {
                var doc = new XmlDocument();
                doc.Load(FilePath);
                foreach (XmlElement e in doc.DocumentElement.ChildNodes.OfType<XmlElement>())
                {
                    var path = e.GetAttribute("path");
                    if (string.IsNullOrEmpty(path)) continue;
                    // Missing values (saved by an older version) take the defaults
                    var defaults = Defaults();
                    var settings = new SceneSettings
                    {
                        Females = ReadBool(e, "females", defaults.Females),
                        Males = ReadBool(e, "males", defaults.Males),
                        SoundItems = ReadBool(e, "soundItems", defaults.SoundItems),
                        CharaVoices = ReadBool(e, "charaVoices", defaults.CharaVoices),
                    };
                    if (int.TryParse(e.GetAttribute("freeHMap"), out var map)) settings.FreeHMap = map;
                    foreach (var s in e.GetAttribute("hiddenCharacters").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                        if (int.TryParse(s, out var key)) settings.HiddenCharacters.Add(key);
                    _all[path] = settings;
                }
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not read the scene settings in {FilePath}: {ex.Message}");
            }
        }
    }
}
