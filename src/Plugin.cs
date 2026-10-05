// Koikatsu Sunshine port of FreeHStudioSceneLoader.
// Original: IllusionMods/KK_Plugins - FreeHStudioSceneLoader.KK by DeathWeasel1337 (GPL-3.0).
// Modified for Koikatsu Sunshine, 2026. Licensed under GPL-3.0.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace KKS_FreeHStudioSceneLoader
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInDependency("com.bepis.bepinex.sideloader")]
    [BepInDependency("com.bepis.bepinex.extendedsave")]
    public class FreeHStudioSceneLoader : BaseUnityPlugin
    {
        public const string GUID = "local.kks.freehstudiosceneloader";
        public const string PluginName = "Free H Studio Scene Loader (KKS port, experimental)";
        public const string Version = "0.2.4";

        internal static new ManualLogSource Logger;
        internal static FreeHStudioSceneLoader Instance;

        internal static ConfigEntry<bool> LoadLights;
        internal static ConfigEntry<KeyboardShortcut> WindowKey;
        internal static ConfigEntry<bool> PlaySoundItems;
        internal static ConfigEntry<bool> HideBaseMap;
        internal static ConfigEntry<bool> ShowBackgroundImage;
        internal static ConfigEntry<Color> NoMapBackgroundColor;
        internal static ConfigEntry<bool> ShowButton;
        internal static ConfigEntry<Vector2> ButtonPosition;

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;

            LoadLights = Config.Bind("General", "Load studio lights", true,
                "Also load lights placed in the studio scene.");
            PlaySoundItems = Config.Bind("General", "Play studio sound items", false,
                "Play sound/voice items placed in the studio scene (they start playing as soon as the scene is loaded).");
            WindowKey = Config.Bind("General", "Scene selection window", new KeyboardShortcut(KeyCode.F7),
                "Opens the studio scene selection window on the Free H selection screen.");
            HideBaseMap = Config.Bind("General", "Hide base map for scenes without a map", true,
                "For studio scenes without a map, hide the Free H map that provides the H positions, so only the scene's own objects are visible.");
            NoMapBackgroundColor = Config.Bind("General", "Background color for scenes without a map", Color.black,
                "Color drawn behind scenes without a map when the base map is hidden (Studio shows black there).");
            ShowBackgroundImage = Config.Bind("General", "Show studio background image", true,
                "Show the scene's studio background image (the picture behind everything, also when embedded by StudioImageEmbed).");
            ShowButton = Config.Bind("Button", "Show button", true,
                "Show a button that opens the scene selection window on the Free H selection screen.");
            ButtonPosition = Config.Bind("Button", "Button position", new Vector2(0.5f, 0.01f),
                "Position of the button as a fraction of the screen size (x: 0 = left, 1 = right; y: 0 = top, 1 = bottom).");

            ApplyPatches();
            Logger.LogInfo($"{PluginName} {Version} loaded");
        }

        /// <summary>
        /// Patches one method at a time so that one failing target does not stop the others.
        /// </summary>
        private static void ApplyPatches()
        {
            var harmony = new Harmony(GUID);
            int ok = 0, failed = 0;
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(Hooks)))
            {
                if (HarmonyMethodExtensions.GetFromMethod(method).Count == 0) continue;
                try
                {
                    var attr = HarmonyMethod.Merge(HarmonyMethodExtensions.GetFromMethod(method));
                    var target = PatchTools_GetOriginal(attr);
                    var processor = harmony.CreateProcessor(target);
                    if (method.GetCustomAttributes(typeof(HarmonyPrefix), false).Length > 0) processor.AddPrefix(method);
                    else if (method.GetCustomAttributes(typeof(HarmonyPostfix), false).Length > 0) processor.AddPostfix(method);
                    else if (method.GetCustomAttributes(typeof(HarmonyFinalizer), false).Length > 0) processor.AddFinalizer(method);
                    else if (method.GetCustomAttributes(typeof(HarmonyTranspiler), false).Length > 0) processor.AddTranspiler(method);
                    processor.Patch();
                    ok++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Logger.LogError($"Failed to patch {method.Name}: {ex.GetBaseException().Message}");
                }
            }
            Logger.Log(failed == 0 ? LogLevel.Info : LogLevel.Warning, $"Patches applied: {ok}, failed: {failed}");
        }

        private static System.Reflection.MethodBase PatchTools_GetOriginal(HarmonyMethod attr)
        {
            switch (attr.methodType)
            {
                case MethodType.Getter: return AccessTools.DeclaredPropertyGetter(attr.declaringType, attr.methodName);
                case MethodType.Setter: return AccessTools.DeclaredPropertySetter(attr.declaringType, attr.methodName);
                default:
                    return attr.argumentTypes != null
                        ? AccessTools.DeclaredMethod(attr.declaringType, attr.methodName, attr.argumentTypes)
                        : AccessTools.DeclaredMethod(attr.declaringType, attr.methodName);
            }
        }

        #region Scene selection window (Free H screen)

        private static readonly string SceneRoot = Path.GetFullPath(Path.Combine(Paths.GameRootPath, "UserData/studio/scene"));

        private bool _showWindow;
        private Rect _windowRect = new Rect(80, 80, 560, 640);
        private Vector2 _scroll;
        private List<string> _files;
        private string[] _pending;   // files still to be checked for scene data
        private int _pendingIndex;
        private bool _reloadRequested;
        private string _filter = "";
        private string _previewPath;
        private Texture2D _preview;
        private string _status = "";

        private FreeHScene _freeH;

        private void Update()
        {
            if (_freeH == null)
            {
                _showWindow = false;
                return;
            }
            if (_reloadRequested) { _files = null; _reloadRequested = false; }
            if (_showWindow) ScanSceneFiles();
            if (WindowKey.Value.IsDown())
                ToggleWindow();
        }

        private void ToggleWindow()
        {
            _showWindow = !_showWindow;
            if (_showWindow)
            {
                _files = null;
                StudioHost.EnsureCreated();
            }
        }

        internal void OnFreeHSceneStart(FreeHScene freeH)
        {
            _freeH = freeH;
            Selection.Clear();
            Logger.LogMessage($"スタジオシーン読み込み: ボタンか {WindowKey.Value} キーでシーンを選べます");
        }

        private GUIStyle _buttonStyle;

        private void OnGUI()
        {
            if (_freeH == null) return;
            if (ShowButton.Value) DrawOpenButton();
            if (!_showWindow) return;
            _windowRect = GUILayout.Window(GUID.GetHashCode(), _windowRect, DrawWindow, "スタジオシーンをFree-Hのマップにする", GUILayout.Width(560));
        }

        private void DrawOpenButton()
        {
            if (_buttonStyle == null)
                _buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
            var label = Selection.IsSelected
                ? $"スタジオシーン: {Path.GetFileNameWithoutExtension(Selection.ScenePath)} ({WindowKey.Value})"
                : $"スタジオシーンを選ぶ ({WindowKey.Value})";
            if (_showWindow) label = $"スタジオシーンの窓を閉じる ({WindowKey.Value})";
            var size = _buttonStyle.CalcSize(new GUIContent(label));
            size.x = Mathf.Max(size.x + 24, 260);
            size.y = Mathf.Max(size.y + 10, 34);
            var pos = ButtonPosition.Value;
            var rect = new Rect(
                Mathf.Clamp(pos.x * Screen.width - size.x / 2, 0, Screen.width - size.x),
                Mathf.Clamp(pos.y * Screen.height, 0, Screen.height - size.y),
                size.x, size.y);
            if (GUI.Button(rect, label, _buttonStyle))
                ToggleWindow();
        }

        private void DrawWindow(int id)
        {
            if (StudioHost.Error != null)
            {
                GUILayout.Label("初期化に失敗しました。output_log.txt を確認してください。");
                GUILayout.Label(StudioHost.Error);
                GUI.DragWindow();
                return;
            }
            if (!StudioHost.IsReady)
            {
                GUILayout.Label("スタジオのデータを読み込み中…（初回のみ、時間がかかります）");
                GUI.DragWindow();
                return;
            }

            if (_files == null) return; // filled in by ScanSceneFiles (Update), never while drawing

            GUILayout.BeginHorizontal();
            GUILayout.Label("絞り込み:", GUILayout.Width(60));
            _filter = GUILayout.TextField(_filter);
            if (GUILayout.Button("再読込", GUILayout.Width(70))) _reloadRequested = true;
            GUILayout.EndHorizontal();

            if (_pending != null)
                GUILayout.Label($"シーンファイルを確認中… {_pendingIndex}/{_pending.Length}（シーン以外のPNGは表示しません）");
            else
                GUILayout.Label($"シーン: {_files.Count} 件　[KK] は無印コイカツ形式（読み込めないものがあります）");

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(300));
            foreach (var f in _files.ToArray())
            {
                var rel = (SceneFile.IsKoikatsuFormat(f) ? "[KK] " : "[KKS] ") + f.Substring(SceneRoot.Length).TrimStart('\\', '/');
                if (_filter.Length > 0 && rel.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var style = f == _previewPath ? GUI.skin.box : GUI.skin.button;
                if (GUILayout.Button(rel, style, GUILayout.Width(500)))
                    SetPreview(f);
            }
            GUILayout.EndScrollView();

            // Fixed size so the window does not grow to the texture size
            var previewRect = GUILayoutUtility.GetRect(520, 180, GUILayout.Width(520), GUILayout.Height(180));
            if (_preview != null)
                GUI.DrawTexture(previewRect, _preview, ScaleMode.ScaleToFit);

            GUILayout.BeginHorizontal();
            GUI.enabled = _previewPath != null;
            if (GUILayout.Button("このシーンを使う"))
                _status = Selection.Select(_freeH, _previewPath);
            GUI.enabled = Selection.IsSelected;
            if (GUILayout.Button("解除"))
            {
                Selection.Clear();
                _status = "解除しました。通常のマップを使います。";
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Label(_status);
            GUI.DragWindow();
        }

        /// <summary>
        /// Builds the scene list a few files per frame. Runs in Update: changing the list between the
        /// IMGUI layout and repaint passes breaks GUILayout.
        /// </summary>
        private void ScanSceneFiles()
        {
            if (!StudioHost.IsReady) return;
            if (_files == null)
            {
                _pending = Directory.Exists(SceneRoot)
                    ? Directory.GetFiles(SceneRoot, "*.png", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTime).ToArray()
                    : new string[0];
                _pendingIndex = 0;
                _files = new List<string>();
            }
            if (_pending == null) return;
            var until = Time.realtimeSinceStartup + 0.02f;
            while (_pendingIndex < _pending.Length && Time.realtimeSinceStartup < until)
            {
                var p = _pending[_pendingIndex++];
                if (SceneFile.IsScene(p)) _files.Add(p);
            }
            if (_pendingIndex >= _pending.Length) _pending = null;
        }

        private void SetPreview(string path)
        {
            _previewPath = path;
            if (_preview != null) Destroy(_preview);
            _preview = null;
            try
            {
                var bytes = PngFile.LoadPngBytes(path);
                int w = 0, h = 0;
                _preview = PngAssist.ChangeTextureFromPngByte(bytes, ref w, ref h);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not load preview for {path}: {ex.Message}");
            }
        }

        #endregion
    }

    /// <summary>
    /// The studio scene chosen for the next H scene, and the Free H map it was matched to.
    /// </summary>
    internal static class Selection
    {
        internal static string ScenePath { get; private set; }
        internal static int MapNo { get; private set; } = -1;
        internal static bool IsSelected => ScenePath != null;
        /// <summary>The scene has no studio map: the chosen Free H map only provides the H positions.</summary>
        internal static bool NoMap { get; private set; }
        internal static string BaseMapName { get; private set; }

        internal static string Select(FreeHScene freeH, string path)
        {
            Clear();
            if (!SceneFile.IsScene(path))
                return "このファイルはスタジオのシーンではありません（シーンのデータが入っていない画像です）。";
            if (!StudioHost.LoadSceneData(path, out var studioMapNo))
                return SceneFile.IsKoikatsuFormat(path)
                    ? "このシーンを読み込めませんでした。無印コイカツ（KK）形式のシーンで、キャラクターのデータを読めなかった可能性があります。"
                    : "このシーンを読み込めませんでした。output_log.txt を確認してください。";

            if (studioMapNo < 0)
                return SelectWithoutMap(freeH, path);

            var baseMap = freeH.member.map;
            var param = StudioHost.FindFreeHMap(baseMap, studioMapNo, out var reason, out var approximate);
            if (param == null)
            {
                StudioHost.DiscardSceneData();
                return reason;
            }

            ScenePath = path;
            MapNo = param.No;
            // Setting the result map makes the game load this map (and its H points) when H starts
            freeH.member.resultMapInfo.Value = param;
            Hooks.ApplySceneThumbnail(freeH);
            FreeHStudioSceneLoader.Logger.LogInfo($"Selected studio scene {path} on Free H map {param.No} ({param.MapName}){(approximate ? " [approximate match]" : "")}");
            return $"選択しました。土台のマップ: {param.DisplayName}（No {param.No}）。シーン内のキャラクターは読み込みません。" +
                   (approximate ? "\n注意: スタジオ専用マップを通常版のマップに対応させました。アイテムの位置が少しずれる可能性があります。" : "");
        }

        /// <summary>
        /// A scene built from items only. The map currently chosen in the Free H screen provides the H positions;
        /// the scene is placed around the H start position and the map itself is hidden.
        /// </summary>
        private static string SelectWithoutMap(FreeHScene freeH, string path)
        {
            var param = freeH.member.resultMapInfo.Value;
            if (param == null || !param.isFreeH)
            {
                StudioHost.DiscardSceneData();
                return "このシーンには土台のマップがありません。先に通常のマップ選択で、H位置に使うマップを選んでから、もう一度選んでください。";
            }
            ScenePath = path;
            NoMap = true;
            MapNo = param.No;
            BaseMapName = param.DisplayName;
            Hooks.ApplySceneThumbnail(freeH);
            FreeHStudioSceneLoader.Logger.LogInfo($"Selected studio scene {path} (no studio map), H positions from Free H map {param.No} ({param.MapName})");
            return $"選択しました。このシーンには土台のマップがないため、「{param.DisplayName}」のH位置を使います" +
                   (FreeHStudioSceneLoader.HideBaseMap.Value ? "（マップ自体は表示しません）。" : "。") +
                   "\nシーンは、シーン内の女の子の位置がHの開始位置に来るように置きます。通常のマップ選択で別のマップを選ぶと、H位置だけ変わります。";
        }

        internal static void ChangeBaseMap(MapInfo.Param param)
        {
            MapNo = param.No;
            BaseMapName = param.DisplayName;
            FreeHStudioSceneLoader.Logger.LogInfo($"H positions for the studio scene now from Free H map {param.No} ({param.MapName})");
        }

        internal static void Clear()
        {
            ScenePath = null;
            MapNo = -1;
            NoMap = false;
            BaseMapName = null;
            StudioHost.DiscardSceneData();
        }
    }

    /// <summary>
    /// Tells studio scene PNGs apart from plain images and other cards: scene data after the PNG image
    /// starts with a version string such as "1.0.4.2" or "1.1.2.1".
    /// </summary>
    internal static class SceneFile
    {
        private static readonly Dictionary<string, KeyValuePair<DateTime, bool>> Cache = new Dictionary<string, KeyValuePair<DateTime, bool>>();
        private static readonly Dictionary<string, Version> Versions = new Dictionary<string, Version>();

        /// <summary>Scenes saved by Koikatsu (KK) have data version 1.0.x; Koikatsu Sunshine saves 1.1.x.</summary>
        internal static bool IsKoikatsuFormat(string path) =>
            Versions.TryGetValue(path, out var v) && v < new Version(1, 1);
        private static readonly System.Text.RegularExpressions.Regex VersionPattern = new System.Text.RegularExpressions.Regex(@"^\d+\.\d+\.\d+\.\d+$");

        internal static bool IsScene(string path)
        {
            DateTime time;
            try { time = File.GetLastWriteTimeUtc(path); }
            catch { return false; }
            if (Cache.TryGetValue(path, out var cached) && cached.Key == time) return cached.Value;

            bool result = false;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var br = new BinaryReader(fs))
                {
                    PngFile.SkipPng(br);
                    if (fs.Position < fs.Length)
                    {
                        // A short length-prefixed string; avoid reading garbage as a huge string
                        int len = fs.ReadByte();
                        if (len > 0 && len < 32 && fs.Position + len <= fs.Length)
                        {
                            var bytes = br.ReadBytes(len);
                            var text = System.Text.Encoding.UTF8.GetString(bytes);
                            result = VersionPattern.IsMatch(text);
                            if (result) Versions[path] = new Version(text);
                        }
                    }
                }
            }
            catch
            {
                result = false;
            }
            Cache[path] = new KeyValuePair<DateTime, bool>(time, result);
            return result;
        }
    }
}
