// Koikatsu Sunshine port of FreeHStudioSceneLoader.
// Modified for Koikatsu Sunshine, 2026. Licensed under GPL-3.0.
//
// Settings kept for each studio scene file: which characters are placed, whether the scene's sounds play, and
// the layouts saved in H (where the characters and the scene were moved to).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using UnityEngine;

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
        /// <summary>Set the Free H time of day to the scene's (Studio's map light: morning, day, evening, night).</summary>
        public bool MatchTime;
        /// <summary>The Free H map chosen when several use the scene's studio map (-1: not chosen).</summary>
        public int FreeHMap = -1;
        /// <summary>dicKey of the characters turned off in the selection window.</summary>
        public readonly HashSet<int> HiddenCharacters = new HashSet<int>();
        /// <summary>Layouts saved in H (<see cref="LayoutSlots"/> slots, null: empty).</summary>
        public readonly SceneLayout[] Layouts = new SceneLayout[LayoutSlots];
        /// <summary>When the scene is placed in H, apply the layout saved last.</summary>
        public bool AutoLayout = true;

        public const int LayoutSlots = 3;

        /// <summary>The slot saved last, or -1 when all are empty.</summary>
        public int LastSavedLayout
        {
            get
            {
                int last = -1;
                for (int i = 0; i < Layouts.Length; i++)
                    if (Layouts[i] != null && (last < 0 || Layouts[i].Saved > Layouts[last].Saved)) last = i;
                return last;
            }
        }
    }

    /// <summary>
    /// Where the scene and its characters were moved to in H, independent of the H map and its start position:
    /// the scene's root relative to where it was first placed, the characters' own values (relative to the folder or
    /// item they are under) and the offsets of the folders/items moved with them.
    /// </summary>
    internal class SceneLayout
    {
        public DateTime Saved;
        public Vector3 RootPos;
        public Quaternion RootRot = Quaternion.identity;
        /// <summary>Characters by dicKey: changeAmount position and rotation, and the visibility switch.</summary>
        public readonly Dictionary<int, LayoutPose> Characters = new Dictionary<int, LayoutPose>();
        /// <summary>Folders/items moved with linked characters, by dicKey: local position and rotation of the offset.</summary>
        public readonly Dictionary<int, LayoutPose> Offsets = new Dictionary<int, LayoutPose>();
    }

    internal class LayoutPose
    {
        public Vector3 Pos;
        public Quaternion Rot = Quaternion.identity;
        public bool Visible = true;
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
            MatchTime = FreeHStudioSceneLoader.MatchTimeOfDay.Value,
        };

        private static bool ReadBool(XmlElement e, string name, bool fallback) =>
            e.HasAttribute(name) ? e.GetAttribute(name) == "true" : fallback;

        private static string Floats(params float[] values) =>
            string.Join(",", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)).ToArray());

        private static float[] ReadFloats(XmlElement e, string name, int count)
        {
            var parts = e.GetAttribute(name).Split(',');
            if (parts.Length != count) return null;
            var values = new float[count];
            for (int i = 0; i < count; i++)
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])) return null;
            return values;
        }

        private static void WritePose(XmlElement e, Vector3 pos, Quaternion rot)
        {
            e.SetAttribute("pos", Floats(pos.x, pos.y, pos.z));
            e.SetAttribute("rot", Floats(rot.x, rot.y, rot.z, rot.w));
        }

        private static bool ReadPose(XmlElement e, out Vector3 pos, out Quaternion rot)
        {
            var p = ReadFloats(e, "pos", 3);
            var r = ReadFloats(e, "rot", 4);
            pos = p != null ? new Vector3(p[0], p[1], p[2]) : Vector3.zero;
            rot = r != null ? new Quaternion(r[0], r[1], r[2], r[3]) : Quaternion.identity;
            return p != null && r != null;
        }

        private static void WriteLayouts(XmlElement e, SceneSettings settings)
        {
            var doc = e.OwnerDocument;
            if (!settings.AutoLayout) e.SetAttribute("autoLayout", "false");
            for (int i = 0; i < settings.Layouts.Length; i++)
            {
                var layout = settings.Layouts[i];
                if (layout == null) continue;
                var le = (XmlElement)e.AppendChild(doc.CreateElement("layout"));
                le.SetAttribute("slot", i.ToString());
                le.SetAttribute("saved", layout.Saved.ToString("o", CultureInfo.InvariantCulture));
                WritePose(le, layout.RootPos, layout.RootRot);
                foreach (var kv in layout.Characters.OrderBy(k => k.Key))
                {
                    var ce = (XmlElement)le.AppendChild(doc.CreateElement("character"));
                    ce.SetAttribute("key", kv.Key.ToString());
                    WritePose(ce, kv.Value.Pos, kv.Value.Rot);
                    if (!kv.Value.Visible) ce.SetAttribute("visible", "false");
                }
                foreach (var kv in layout.Offsets.OrderBy(k => k.Key))
                {
                    var oe = (XmlElement)le.AppendChild(doc.CreateElement("offset"));
                    oe.SetAttribute("key", kv.Key.ToString());
                    WritePose(oe, kv.Value.Pos, kv.Value.Rot);
                }
            }
        }

        private static void ReadLayouts(XmlElement e, SceneSettings settings)
        {
            settings.AutoLayout = ReadBool(e, "autoLayout", true);
            foreach (XmlElement le in e.ChildNodes.OfType<XmlElement>().Where(x => x.Name == "layout"))
            {
                if (!int.TryParse(le.GetAttribute("slot"), out var slot) || slot < 0 || slot >= settings.Layouts.Length) continue;
                var layout = new SceneLayout();
                DateTime.TryParse(le.GetAttribute("saved"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out layout.Saved);
                ReadPose(le, out layout.RootPos, out layout.RootRot);
                foreach (XmlElement ce in le.ChildNodes.OfType<XmlElement>())
                {
                    if (!int.TryParse(ce.GetAttribute("key"), out var key) || !ReadPose(ce, out var pos, out var rot)) continue;
                    var pose = new LayoutPose { Pos = pos, Rot = rot, Visible = ReadBool(ce, "visible", true) };
                    if (ce.Name == "character") layout.Characters[key] = pose;
                    else if (ce.Name == "offset") layout.Offsets[key] = pose;
                }
                settings.Layouts[slot] = layout;
            }
        }

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
                    e.SetAttribute("matchTime", kv.Value.MatchTime ? "true" : "false");
                    if (kv.Value.FreeHMap >= 0) e.SetAttribute("freeHMap", kv.Value.FreeHMap.ToString());
                    if (kv.Value.HiddenCharacters.Count > 0)
                        e.SetAttribute("hiddenCharacters", string.Join(",", kv.Value.HiddenCharacters.OrderBy(i => i).Select(i => i.ToString()).ToArray()));
                    WriteLayouts(e, kv.Value);
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
                        MatchTime = ReadBool(e, "matchTime", defaults.MatchTime),
                    };
                    if (int.TryParse(e.GetAttribute("freeHMap"), out var map)) settings.FreeHMap = map;
                    foreach (var s in e.GetAttribute("hiddenCharacters").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                        if (int.TryParse(s, out var key)) settings.HiddenCharacters.Add(key);
                    ReadLayouts(e, settings);
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
