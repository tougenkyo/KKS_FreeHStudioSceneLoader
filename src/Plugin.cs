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
        public const string Version = "0.3.23";

        internal static new ManualLogSource Logger;
        internal static FreeHStudioSceneLoader Instance;

        internal static ConfigEntry<bool> LoadFemales;
        internal static ConfigEntry<bool> LoadMales;
        internal static ConfigEntry<bool> LoadLights;
        internal static ConfigEntry<bool> PlayTimeline;
        internal static ConfigEntry<bool> ApplyPoseEditor;
        internal static ConfigEntry<bool> ApplyFKAndIK;
        internal static ConfigEntry<bool> KkpeSavedBones, KkpeTimelineBones, KkpeBlendShapes, KkpeOptimizeIK, KkpeItems;
        internal static ConfigEntry<string> KkpeSkipSavedBones;
        internal static ConfigEntry<bool> KkpeTrace;
        internal static ConfigEntry<KeyboardShortcut> WindowKey;
        internal static ConfigEntry<bool> PlaySoundItems;
        internal static ConfigEntry<bool> PlayCharaVoices;
        internal static ConfigEntry<bool> ShowButtonInH;
        internal static ConfigEntry<Vector2> ButtonInHPosition;
        internal static ConfigEntry<bool> HideBaseMap;
        internal static ConfigEntry<bool> ShowBackgroundImage;
        internal static ConfigEntry<Color> NoMapBackgroundColor;
        internal static ConfigEntry<bool> ShowButton;
        internal static ConfigEntry<Vector2> ButtonPosition;

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;

            LoadFemales = Config.Bind("General", "Load studio female characters", true,
                "Default for scenes not set up yet: also load the female characters of the studio scene, posed as in the scene. They are only decoration and do not take part in H. Each scene keeps its own setting once changed in the scene selection window.");
            LoadMales = Config.Bind("General", "Load studio male characters", true,
                "Default for scenes not set up yet: also load the male characters of the studio scene, posed as in the scene. They are only decoration and do not take part in H. Each scene keeps its own setting once changed in the scene selection window.");
            LoadLights = Config.Bind("General", "Load studio lights", true,
                "Also load lights placed in the studio scene.");
            PlayTimeline = Config.Bind("Studio plugins", "Play Timeline animation", true,
                "Play the scene's Timeline animation and apply its NodesConstraints links (both plugins only run in CharaStudio). Studio camera, colors, clothes and some other tracks are not reproduced.");
            ApplyPoseEditor = Config.Bind("Studio plugins", "Apply KKPE pose edits", true,
                "Apply the bone and blend shape (facial expression) edits made with KKPE, which only runs in CharaStudio.");
            ApplyFKAndIK = Config.Bind("Studio plugins", "Apply FK and IK", true,
                "Characters saved with FK and IK on at the same time (studio plugin \"FK and IK\", which only runs in CharaStudio) get both again, instead of IK only.");
            const string diag = "Studio plugins (diagnostics)";
            const string diagNote = " Only used when 'Apply KKPE pose edits' is on; read when the H scene starts. For finding differences from Studio.";
            KkpeSavedBones = Config.Bind(diag, "KKPE: saved bone edits of characters", true, "Bone edits saved in the character (KKPE advanced mode)." + diagNote);
            KkpeTimelineBones = Config.Bind(diag, "KKPE: bone edits animated by Timeline", true, "KKPE bone position/rotation/scale tracks of Timeline." + diagNote);
            KkpeBlendShapes = Config.Bind(diag, "KKPE: blend shapes", true, "Saved and Timeline-animated blend shape weights (facial expressions)." + diagNote);
            KkpeOptimizeIK = Config.Bind(diag, "KKPE: optimizeIK", true, "KKPE's IK setting on every character (spine stiffness and vertical body pull 0)." + diagNote);
            KkpeItems = Config.Bind(diag, "KKPE: items", true, "KKPE edits of the scene's items." + diagNote);
            KkpeTrace = Config.Bind(diag, "KKPE: log bone values every 2 seconds", false,
                "Writes the saved, Timeline and actual rotation of the bones edited by KKPE to the log (before and after IK).");
            KkpeSkipSavedBones = Config.Bind(diag, "KKPE: skip saved bone edits matching", "",
                "Comma separated texts. A saved bone edit is skipped when \"character name:bone path\" contains one of them, e.g. \"Male 60\" or \"cf_d_bust\". A text ending with $ only matches the end of the bone path, e.g. \"cf_j_spine03$\"." + diagNote);
            PlaySoundItems = Config.Bind("General", "Play studio sound items", true,
                "Default for scenes not set up yet: play sound/voice items placed in the studio scene (they start playing as soon as the scene is loaded). Each scene keeps its own setting once changed in the windows.");
            PlayCharaVoices = Config.Bind("General", "Play character voice lists", false,
                "Default for scenes not set up yet: play the characters' voice lists (Studio's voice playback) when they are placed. Each scene keeps its own setting once changed in the windows.");
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
            ShowButtonInH = Config.Bind("Button", "Show button in H", true,
                "During H, show a button that opens the window for the scene's characters (same as the key).");
            ButtonInHPosition = Config.Bind("Button", "Button in H position", new Vector2(0.5f, 0.01f),
                "Position of the button during H as a fraction of the screen size (x: 0 = left, 1 = right; y: 0 = top, 1 = bottom).");

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
            try
            {
                PoseProbe.InstallPatches(harmony);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Pose probe patch failed (diagnostics only): {ex.GetBaseException().Message}");
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
                // During H the same key opens the window for the scene's characters
                if (!StudioHost.HasPlacedCharacters) _showCharaWindow = false;
                else if (WindowKey.Value.IsDown()) _showCharaWindow = !_showCharaWindow;
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
            if (_freeH == null)
            {
                if (!StudioHost.HasPlacedCharacters) return;
                if (ShowButtonInH.Value)
                {
                    var label = _showCharaWindow ? $"スタジオシーンのキャラクターを閉じる ({WindowKey.Value})" : $"スタジオシーンのキャラクター ({WindowKey.Value})";
                    if (DrawButton(label, ButtonInHPosition.Value)) _showCharaWindow = !_showCharaWindow;
                }
                if (_showCharaWindow)
                    _charaWindowRect = GUILayout.Window(GUID.GetHashCode() + 1, _charaWindowRect, DrawCharaWindow, "スタジオシーンのキャラクター", GUILayout.Width(440));
                return;
            }
            if (ShowButton.Value) DrawOpenButton();
            if (!_showWindow) return;
            _windowRect = GUILayout.Window(GUID.GetHashCode(), _windowRect, DrawWindow, "スタジオシーンをFree-Hのマップにする", GUILayout.Width(560));
        }

        private void DrawOpenButton()
        {
            var label = Selection.IsSelected
                ? $"スタジオシーン: {Path.GetFileNameWithoutExtension(Selection.ScenePath)} ({WindowKey.Value})"
                : $"スタジオシーンを選ぶ ({WindowKey.Value})";
            if (_showWindow) label = $"スタジオシーンの窓を閉じる ({WindowKey.Value})";
            if (DrawButton(label, ButtonPosition.Value))
                ToggleWindow();
        }

        /// <summary>A large button centered at <paramref name="pos"/> (fractions of the screen size).</summary>
        private bool DrawButton(string label, Vector2 pos)
        {
            if (_buttonStyle == null)
                _buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
            var size = _buttonStyle.CalcSize(new GUIContent(label));
            size.x = Mathf.Max(size.x + 24, 260);
            size.y = Mathf.Max(size.y + 10, 34);
            var rect = new Rect(
                Mathf.Clamp(pos.x * Screen.width - size.x / 2, 0, Screen.width - size.x),
                Mathf.Clamp(pos.y * Screen.height, 0, Screen.height - size.y),
                size.x, size.y);
            return GUI.Button(rect, label, _buttonStyle);
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
            if (Selection.IsSelected && !Selection.NoMap && Selection.Candidates.Count > 1)
            {
                GUILayout.Label("土台のマップ: このスタジオマップを使うFree-H用マップが複数あります（H位置が違います。シーンごとに保存します）");
                GUILayout.BeginHorizontal();
                foreach (var map in Selection.Candidates)
                    if (GUILayout.Button($"{map.DisplayName}（No {map.No}）", map.No == Selection.MapNo ? GUI.skin.box : GUI.skin.button))
                        Selection.ChooseMap(_freeH, map);
                GUILayout.EndHorizontal();
            }
            if (Selection.IsSelected)
            {
                DrawCharacterChoice();
                GUILayout.Label("音（シーンごとに保存します。H 中も F7 の窓で切り替えられます）:");
                DrawSoundChoice();
            }
            GUI.DragWindow();
        }

        private Vector2 _charaChoiceScroll;

        /// <summary>Which of the scene's characters are placed in H.</summary>
        private void DrawCharacterChoice()
        {
            var charas = StudioHost.SceneCharacters;
            if (charas.Count == 0)
            {
                GUILayout.Label("このシーンにはキャラクターがいません。");
                return;
            }
            GUILayout.Label($"シーンのキャラクター（{charas.Count} 人）: チェックしたキャラクターを、スタジオのポーズのまま置きます。");
            GUILayout.BeginHorizontal();
            StudioHost.SetSexEnabled(true, GUILayout.Toggle(StudioHost.FemalesOn, $"女性を表示（{charas.Count(c => c.Female)} 人）"));
            StudioHost.SetSexEnabled(false, GUILayout.Toggle(StudioHost.MalesOn, $"男性を表示（{charas.Count(c => !c.Female)} 人）"));
            GUILayout.EndHorizontal();
            _charaChoiceScroll = GUILayout.BeginScrollView(_charaChoiceScroll, GUILayout.Height(Mathf.Min(charas.Count, 5) * 24 + 8));
            foreach (var chara in charas)
            {
                GUI.enabled = chara.Female ? StudioHost.FemalesOn : StudioHost.MalesOn;
                StudioHost.SetCharacterEnabled(chara, GUILayout.Toggle(chara.Enabled, (chara.Female ? "♀ " : "♂ ") + chara.Name));
            }
            GUI.enabled = true;
            GUILayout.EndScrollView();
        }

        /// <summary>The scene's sounds: when it is loaded (selection window) and while it is placed (H window). Saved per scene.</summary>
        private static void DrawSoundChoice()
        {
            var settings = StudioHost.Settings;
            if (settings == null) return;
            StudioHost.SetSoundItems(GUILayout.Toggle(settings.SoundItems, "シーンの音・声アイテムを鳴らす（置かれた効果音・ボイスのアイテム）"));
            int voices = StudioHost.CharactersWithVoices;
            if (voices > 0 || settings.CharaVoices)
                StudioHost.SetCharaVoices(GUILayout.Toggle(settings.CharaVoices, $"キャラのボイスを再生（スタジオのボイスリスト、{voices} 人）"));
            else
                GUILayout.Label("キャラのボイス: このシーンのキャラにはボイスリストがありません");
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

        #region Scene character window (H)

        private bool _showCharaWindow;
        private Rect _charaWindowRect = new Rect(40, 120, 440, 400);
        private Vector2 _charaScroll;
        private StudioHost.SceneCharacter _selectedChara;
        private bool _moveAll;
        private int _moveMode = 1;
        private static readonly string[] MoveModeNames = { "キャラだけ", "キャラと連結先", "シーン全体" };
        private int _moveStep = 1;
        private int _turnStep = 1;
        private static readonly float[] MoveSteps = { 0.01f, 0.1f, 1f };
        private static readonly string[] MoveStepNames = { "1cm", "10cm", "1m" };
        private static readonly float[] TurnSteps = { 1f, 15f, 90f };
        private static readonly string[] TurnStepNames = { "1°", "15°", "90°" };

        private void DrawCharaWindow(int id)
        {
            var placed = StudioHost.SceneCharacters.Where(c => c.Placed).ToList();
            if (_selectedChara == null || !placed.Contains(_selectedChara)) _selectedChara = placed.FirstOrDefault();

            GUILayout.Label($"表示のオンオフと、位置の調整ができます（{WindowKey.Value} キーで閉じる）");
            _charaScroll = GUILayout.BeginScrollView(_charaScroll, GUILayout.Height(Mathf.Min(placed.Count, 6) * 26 + 8));
            foreach (var chara in placed)
            {
                GUILayout.BeginHorizontal();
                var visible = StudioHost.IsVisible(chara);
                var newVisible = GUILayout.Toggle(visible, "表示", GUILayout.Width(56));
                if (newVisible != visible) StudioHost.SetVisible(chara, newVisible);
                var label = (chara.Female ? "♀ " : "♂ ") + chara.Name;
                if (GUILayout.Button(label, chara == _selectedChara ? GUI.skin.box : GUI.skin.button)) _selectedChara = chara;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            if (_selectedChara != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("動かす対象", GUILayout.Width(70));
                _moveMode = GUILayout.Toolbar(_moveMode, MoveModeNames);
                GUILayout.EndHorizontal();
                if (_moveMode != 2) _moveAll = GUILayout.Toggle(_moveAll, "全員まとめて動かす（キャラ同士の位置関係を保つ）");
                var targets = _moveAll ? placed : new List<StudioHost.SceneCharacter> { _selectedChara };
                var who = _moveAll ? "全員" : _selectedChara.Name;
                switch (_moveMode)
                {
                    case 0:
                        GUILayout.Label($"動かすキャラクター: {who}（回転は選んだキャラクターが中心）");
                        break;
                    case 1:
                        GUILayout.Label($"動かすもの: {who}と、連結（NodesConstraints）でつながったフォルダ・アイテム {StudioHost.GetLinkedObjects(targets).Count} 個");
                        break;
                    default:
                        GUILayout.Label("動かすもの: スタジオから置いたもの全部（キャラ・フォルダ・アイテム。回転は選んだキャラクターが中心）");
                        break;
                }
                void move(Vector3 delta)
                {
                    if (_moveMode == 0) StudioHost.Move(targets, delta);
                    else if (_moveMode == 1) StudioHost.MoveLinked(targets, delta);
                    else StudioHost.MoveScene(delta);
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label("移動量", GUILayout.Width(50));
                _moveStep = GUILayout.Toolbar(_moveStep, MoveStepNames);
                GUILayout.EndHorizontal();
                // Left/right and forward/back follow the camera, so the buttons move the character as seen on screen
                var cam = Camera.main != null ? Camera.main.transform : null;
                var right = cam != null ? Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized : Vector3.right;
                var forward = cam != null ? Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized : Vector3.forward;
                if (forward == Vector3.zero) forward = Vector3.Cross(right, Vector3.up);
                var step = MoveSteps[_moveStep];
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("← 左")) move(-right * step);
                if (GUILayout.Button("右 →")) move(right * step);
                if (GUILayout.Button("奥へ")) move(forward * step);
                if (GUILayout.Button("手前へ")) move(-forward * step);
                if (GUILayout.Button("上へ")) move(Vector3.up * step);
                if (GUILayout.Button("下へ")) move(Vector3.down * step);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("回転量", GUILayout.Width(50));
                _turnStep = GUILayout.Toolbar(_turnStep, TurnStepNames);
                GUILayout.EndHorizontal();
                var pivot = StudioHost.GetPosition(_selectedChara);
                void turn(float degrees)
                {
                    if (_moveMode == 0) StudioHost.Rotate(targets, pivot, degrees);
                    else if (_moveMode == 1) StudioHost.RotateLinked(targets, pivot, degrees);
                    else StudioHost.RotateScene(pivot, degrees);
                }
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("左に回す")) turn(-TurnSteps[_turnStep]);
                if (GUILayout.Button("右に回す")) turn(TurnSteps[_turnStep]);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("カメラの注視点へ移動"))
                {
                    var focus = GetCameraFocus();
                    if (focus.HasValue) move(focus.Value - pivot);
                }
                if (GUILayout.Button("元の位置に戻す"))
                {
                    if (_moveMode == 0) StudioHost.ResetPose(targets);
                    else if (_moveMode == 1) StudioHost.ResetLinked(targets);
                    else StudioHost.ResetScene();
                }
                GUILayout.EndHorizontal();
                GUILayout.Label("「カメラの注視点へ移動」: カメラを動かして、置きたい場所を画面の中心に合わせてから押してください。");
            }
            GUILayout.Label("音（シーンごとに保存します）:");
            DrawSoundChoice();
            DrawDiagnostics();
            GUI.DragWindow();
        }

        private bool _showDiagnostics;
        private Vector2 _diagScroll;
        private readonly HashSet<string> _openPoses = new HashSet<string>();

        /// <summary>Switches parts of the studio plugin reproduction off, to find what differs from Studio.</summary>
        private void DrawDiagnostics()
        {
            var animator = SceneAnimator.Instance;
            if (animator == null) return;
            _showDiagnostics = GUILayout.Toggle(_showDiagnostics, "診断（スタジオとの違いを調べる用。チェックを外すとその場で無効になります）");
            if (!_showDiagnostics) return;
            _diagScroll = GUILayout.BeginScrollView(_diagScroll, GUILayout.Height(260));
            animator.ConstraintsOn = GUILayout.Toggle(animator.ConstraintsOn, $"NodesConstraints の連結（{animator.ConstraintCount} 件）");
            animator.LateBeforeIK = GUILayout.Toggle(animator.LateBeforeIK, "連結と Timeline の後処理を IK の前に当てる（外すと IK・FK の後）");
            foreach (var pose in animator.Poses.ToList())
            {
                var key = pose.Name ?? "";
                GUILayout.BeginHorizontal();
                bool open = _openPoses.Contains(key);
                if (GUILayout.Button(open ? "▼" : "▶", GUILayout.Width(28)))
                {
                    if (open) _openPoses.Remove(key); else _openPoses.Add(key);
                }
                GUILayout.Label($"KKPE: {key}");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Space(30);
                bool staticOn = GUILayout.Toggle(pose.StaticOn, $"固定ポーズ（{pose.Static.Count}）");
                bool animatedOn = GUILayout.Toggle(pose.AnimatedOn, $"Timeline（{pose.Animated.Count}）");
                pose.OptimizeIK = GUILayout.Toggle(pose.OptimizeIK, "optimizeIK");
                GUILayout.EndHorizontal();
                if (staticOn != pose.StaticOn || animatedOn != pose.AnimatedOn)
                {
                    pose.StaticOn = staticOn;
                    pose.AnimatedOn = animatedOn;
                    pose.RestoreAll();
                }
                if (!open) continue;
                foreach (var kv in pose.Static.Concat(pose.Animated).ToList())
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(50);
                    var kind = pose.Static.ContainsKey(kv.Key) && pose.Static[kv.Key] == kv.Value ? "固定" : "TL";
                    var parts = (kv.Value.Position.HasValue ? "位置" : "") + (kv.Value.Rotation.HasValue ? "回転" : "") + (kv.Value.Scale.HasValue ? "大きさ" : "");
                    bool on = GUILayout.Toggle(kv.Value.On, $"[{kind}] {SceneAnimator.BoneName(kv.Key)} {parts}");
                    if (on != kv.Value.On)
                    {
                        kv.Value.On = on;
                        pose.Restore(kv.Key);
                    }
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
        }

        /// <summary>The point the H camera turns around (the center of the view).</summary>
        private static Vector3? GetCameraFocus()
        {
            var camera = FindObjectOfType<CameraControl_Ver2>();
            if (camera == null) return null;
            return camera.transBase != null ? camera.transBase.TransformPoint(camera.TargetPos) : camera.TargetPos;
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
            var candidates = StudioHost.FindFreeHMaps(baseMap, studioMapNo, out var reason, out var approximate);
            var param = candidates.FirstOrDefault();
            if (param == null)
            {
                StudioHost.DiscardSceneData();
                return reason;
            }
            Candidates = candidates;

            ScenePath = path;
            MapNo = param.No;
            // Setting the result map makes the game load this map (and its H points) when H starts
            freeH.member.resultMapInfo.Value = param;
            Hooks.ApplySceneThumbnail(freeH);
            FreeHStudioSceneLoader.Logger.LogInfo($"Selected studio scene {path} on Free H map {param.No} ({param.MapName}){(approximate ? " [approximate match]" : "")}" +
                (candidates.Count > 1 ? $", other Free H maps with the same studio map: {string.Join(", ", candidates.Skip(1).Select(c => $"{c.No} ({c.MapName})").ToArray())}" : ""));
            return $"選択しました。土台のマップ: {param.DisplayName}（No {param.No}）。" +
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

        /// <summary>Free H maps that load the same studio map as the scene (best first), when there is one.</summary>
        internal static List<MapInfo.Param> Candidates { get; private set; } = new List<MapInfo.Param>();

        /// <summary>Uses another of the <see cref="Candidates"/> (they differ in the H positions) and remembers it for the scene.</summary>
        internal static void ChooseMap(FreeHScene freeH, MapInfo.Param param)
        {
            if (!IsSelected || NoMap || param == null) return;
            MapNo = param.No; // first, so that the map change is not taken as leaving the scene
            freeH.member.resultMapInfo.Value = param;
            Hooks.ApplySceneThumbnail(freeH);
            StudioHost.SetFreeHMap(param.No);
            FreeHStudioSceneLoader.Logger.LogInfo($"Studio scene now on Free H map {param.No} ({param.MapName})");
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
            Candidates = new List<MapInfo.Param>();
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
