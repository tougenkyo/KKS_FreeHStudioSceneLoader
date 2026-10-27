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
        public const string Version = "0.6.5";

        internal static new ManualLogSource Logger;
        internal static FreeHStudioSceneLoader Instance;

        public enum UiLanguage { English, Japanese }
        public enum HasFilter { All, With, Without }
        public enum CharacterFilter { All, FemalesOnly, MalesOnly, Both }

        internal static ConfigEntry<UiLanguage> Language;
        internal static ConfigEntry<bool> ScenePoints;
        internal static ConfigEntry<string> CardFolderFemale;
        internal static ConfigEntry<string> CardFolderMale;
        internal static ConfigEntry<string> CoordinateFolder;
        internal static ConfigEntry<bool> CardSubfolders;
        internal static ConfigEntry<string> SceneFolder;
        internal static ConfigEntry<bool> SceneSubfolders;
        internal static ConfigEntry<bool> SetLastScene;
        internal static ConfigEntry<string> LastScene;
        internal static ConfigEntry<HasFilter> FilterAnimation;
        internal static ConfigEntry<CharacterFilter> FilterCharacters;
        internal static ConfigEntry<HasFilter> FilterMissingMods;
        internal static ConfigEntry<bool> LoadFemales;
        internal static ConfigEntry<bool> LoadMales;
        internal static ConfigEntry<bool> LoadLights;
        internal static ConfigEntry<bool> PlayTimeline;
        internal static ConfigEntry<bool> ApplyPoseEditor;
        internal static ConfigEntry<bool> ApplyFKAndIK;
        internal static ConfigEntry<bool> LogPoses;
        internal static ConfigEntry<bool> LogLighting;
        internal static ConfigEntry<KeyboardShortcut> WindowKey;
        internal static ConfigEntry<bool> PlaySoundItems;
        internal static ConfigEntry<bool> PlayCharaVoices;
        internal static ConfigEntry<bool> MatchTimeOfDay;
        internal static ConfigEntry<bool> ShowButtonInH;
        internal static ConfigEntry<bool> ShowThumbnails;
        internal static ConfigEntry<float> WindowOpacity;
        internal static ConfigEntry<Vector2> ButtonInHPosition;
        internal static ConfigEntry<bool> HideBaseMap;
        internal static ConfigEntry<bool> StudioLightingWithoutMap;
        internal static ConfigEntry<bool> UseSceneEffects;
        internal static ConfigEntry<bool> ShowBackgroundImage;
        internal static ConfigEntry<Color> NoMapBackgroundColor;
        internal static ConfigEntry<bool> ShowButton;
        internal static ConfigEntry<Vector2> ButtonPosition;

        internal static bool Japanese => Language != null && Language.Value == UiLanguage.Japanese;

        /// <summary>The text in the chosen language (General / Language).</summary>
        internal static string Tr(string en, string ja) => Japanese ? ja : en;

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;

            Language = Config.Bind("General", "Language", UiLanguage.English,
                "Language of the plugin's windows and messages (also switched with the button at the top right of the windows).");
            ScenePoints = Config.Bind("General", "H points at studio characters", true,
                "In H, the point move menu (場所移動) also offers the places where the studio scene's characters stand, under every kind of point the map has.");
            CardFolderFemale = Config.Bind("Cards", "Female card folder", "UserData/chara/female",
                "Folder listed when replacing a female scene character (relative to the game folder, or a full path). Also chosen in the card window.");
            CardFolderMale = Config.Bind("Cards", "Male card folder", "UserData/chara/male",
                "Folder listed when replacing a male scene character (relative to the game folder, or a full path). Also chosen in the card window.");
            CoordinateFolder = Config.Bind("Cards", "Coordinate card folder", "UserData/coordinate",
                "Folder listed when loading a coordinate card (relative to the game folder, or a full path). Also chosen in the card window.");
            CardSubfolders = Config.Bind("Cards", "Include subfolders", true,
                "List the cards in the subfolders of the chosen folder too.");
            SceneFolder = Config.Bind("Scenes", "Scene folder", "UserData/studio/scene",
                "Folder the scene list is read from (relative to the game folder, or a full path). Also chosen in the scene selection window.");
            SceneSubfolders = Config.Bind("Scenes", "Include subfolders", true,
                "List the scenes in the subfolders of the chosen folder too.");
            SetLastScene = Config.Bind("Scenes", "Set the last scene again", true,
                "When Free H opens after the game was started, set the scene that was set last time again.");
            LastScene = Config.Bind("Scenes", "Last scene", "",
                "The scene set last (kept by the plugin; cleared by Unset).");
            FilterAnimation = Config.Bind("Scenes", "Filter: Timeline animation", HasFilter.All,
                "List only the scenes with (or without) Timeline animation. Uses the scene data list (made with the button in the scene selection window).");
            FilterCharacters = Config.Bind("Scenes", "Filter: characters", CharacterFilter.All,
                "List only the scenes with female characters only, male characters only, or both. Uses the scene data list.");
            FilterMissingMods = Config.Bind("Scenes", "Filter: missing mods", HasFilter.All,
                "List only the scenes with mod errors: mods that are not installed, a Sideloader error, or a file that can't be read (or the ones without). Uses the scene data list.");
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
            LogPoses = Config.Bind("Diagnostics", "Log character poses", false,
                "Every 2 seconds during H, write the bone values, IK targets and FK/IK state of the studio scene's characters to the log and to BepInEx/FreeHStudioProbe.log (for comparing with Studio).");
            LogLighting = Config.Bind("Diagnostics", "Log scene lighting", false,
                "3 seconds after a studio scene is placed in H, write the lights, ambient light and graphic settings that light its items to the log and to BepInEx/FreeHStudioLighting_H.txt (for comparing with Studio).");
            PlaySoundItems = Config.Bind("General", "Play studio sound items", true,
                "Default for scenes not set up yet: play sound/voice items placed in the studio scene (they start playing as soon as the scene is loaded). Each scene keeps its own setting once changed in the windows.");
            PlayCharaVoices = Config.Bind("General", "Play character voice lists", false,
                "Default for scenes not set up yet: play the characters' voice lists (Studio's voice playback) when they are placed. Each scene keeps its own setting once changed in the windows.");
            MatchTimeOfDay = Config.Bind("General", "Match studio time of day", false,
                "Default for scenes not set up yet: when a scene is chosen, set the Free H time of day (morning, day, evening, night) to the one saved in the studio scene, which decides how bright the map is. Each scene keeps its own setting once changed in the selection window.");
            WindowKey = Config.Bind("General", "Scene selection window", new KeyboardShortcut(KeyCode.F7),
                "Opens the studio scene selection window on the Free H selection screen.");
            HideBaseMap = Config.Bind("General", "Hide base map for scenes without a map", true,
                "For studio scenes without a map, hide the Free H map that provides the H positions, so only the scene's own objects are visible.");
            StudioLightingWithoutMap = Config.Bind("General", "Studio lighting for scenes without a map", true,
                "With the base map hidden, keep its lights (sun, room lights) and its ambient light off the studio items, as in Studio where only the scene's own lights light them.");
            UseSceneEffects = Config.Bind("General", "Use the shadows and bloom of the scene", true,
                "While a studio scene is placed in H, use its shadow, bloom, ambient occlusion and ambient shadow color settings (saved with the scene in Studio) instead of the game's graphic settings. Without shadows the scene's lights shine through walls.");
            NoMapBackgroundColor = Config.Bind("General", "Background color for scenes without a map", Color.black,
                "Color drawn behind scenes without a map when the base map is hidden (Studio shows black there).");
            ShowBackgroundImage = Config.Bind("General", "Show studio background image", true,
                "Show the scene's studio background image (the picture behind everything, also when embedded by StudioImageEmbed).");
            ShowButton = Config.Bind("Button", "Show button", true,
                "Show a button that opens the scene selection window on the Free H selection screen.");
            ButtonPosition = Config.Bind("Button", "Button position", new Vector2(0.5f, 0.01f),
                "Position of the button as a fraction of the screen size (x: 0 = left, 1 = right; y: 0 = top, 1 = bottom).");
            WindowOpacity = Config.Bind("Button", "Window background opacity", 0.85f,
                new ConfigDescription("Opacity of the dark background of the plugin's windows (0 = transparent, 1 = opaque).", new AcceptableValueRange<float>(0.3f, 1f)));
            ShowThumbnails = Config.Bind("Button", "Show scene thumbnails", true,
                "Show the scene list as thumbnails (3 per row) instead of file names. Also switched with the checkbox next to Unset (解除).");
            ShowButtonInH = Config.Bind("Button", "Show button in H", true,
                "During H, show a button that opens the window for the scene's characters (same as the key).");
            ButtonInHPosition = Config.Bind("Button", "Button in H position", new Vector2(0.5f, 0.01f),
                "Position of the button during H as a fraction of the screen size (x: 0 = left, 1 = right; y: 0 = top, 1 = bottom).");

            _sceneFolder = new FolderChooser
            {
                Entry = () => SceneFolder,
                IncludeSubfolders = SceneSubfolders,
                Changed = () => { _reloadRequested = true; _scroll = Vector2.zero; },
                Error = message => _status = message,
                MaxRows = 2,
            };
            _cardFolder = new FolderChooser
            {
                Entry = () => CardFolderEntry,
                IncludeSubfolders = CardSubfolders,
                Changed = () => { _cardFiles = null; _cardScroll = Vector2.zero; _cardStatus = null; },
                Error = message => _cardStatus = message,
            };

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
                // Sideloader is another plugin: patch it by name, the scene loader works without this
                var resolver = AccessTools.TypeByName("Sideloader.AutoResolver.UniversalAutoResolver");
                var showError = resolver != null ? AccessTools.Method(resolver, "ShowGUIDError") : null;
                if (showError != null) harmony.Patch(showError, prefix: new HarmonyMethod(typeof(StudioHost), nameof(StudioHost.OnMissingModForDataList)),
                    postfix: new HarmonyMethod(typeof(StudioHost), nameof(StudioHost.OnMissingMod)));
                var uarHooks = resolver != null ? AccessTools.Inner(resolver, "Hooks") : null;
                var sceneLoad = uarHooks != null ? AccessTools.Method(uarHooks, "ExtendedSceneLoad") : null;
                if (sceneLoad != null) harmony.Patch(sceneLoad, finalizer: new HarmonyMethod(typeof(StudioHost), nameof(StudioHost.OnSceneResolveFinished)));
                else Logger.LogWarning("Sideloader's UniversalAutoResolver.ShowGUIDError not found; missing mods of scenes can't be listed");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not watch Sideloader's missing mod reports: {ex.GetBaseException().Message}");
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

        private FolderChooser _sceneFolder;

        private bool _showWindow;
        private Rect _windowRect = new Rect(40, 60, WindowWidth, 600);
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
            ScanCardFiles();
            UpdateMouseCapture();
            if (_freeH == null)
            {
                _showWindow = false;
                // During H the same key opens the window for the scene's characters
                if (!StudioHost.HasPlacedScene) _showCharaWindow = false;
                else if (WindowKey.Value.IsDown()) _showCharaWindow = !_showCharaWindow;
                if (_cardTarget != null) LoadWantedThumbnails();
                return;
            }
            if (_reloadRequested) { _files = null; _reloadRequested = false; ClearThumbnails(); }
            if (_showWindow) ScanSceneFiles();
            BuildDataList();
            if (_showWindow) LoadWantedThumbnails();
            else _cardTarget = null;
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
                // Show the scene whose details are loaded; a preview left from before (its data was discarded when the
                // Free H screen restarted) would look chosen while nothing is set
                if (Selection.PreviewPath != null && _previewPath != Selection.PreviewPath) SetPreview(Selection.PreviewPath);
                else if (Selection.PreviewPath == null && _previewPath != null) ClearPreview();
            }
        }

        /// <summary>A map was chosen in the game's own map selection: unsets the scene, like 解除 (the details stay shown).</summary>
        internal void OnNormalMapChosen()
        {
            if (!Selection.IsSelected) return;
            // The game sets the chosen map right after this; until then the map shown stays the current one
            Selection.ForgetMapBefore();
            Selection.Unset(_freeH);
            LastScene.Value = "";
            _status = Tr("Unset: a map was chosen in the normal map selection. Click a scene again to set it.", "通常のマップ選択でマップを選んだので、シーンを解除しました。もう一度シーンをクリックするとセットします。");
        }

        internal void OnFreeHSceneStart(FreeHScene freeH)
        {
            _freeH = freeH;
            Selection.Clear();
            Selection.ForgetMapBefore();
            ClearPreview();
            _status = "";
            var path = Selection.ReturnScenePath;
            Selection.ReturnScenePath = null;
            // First time after the game was started: the scene set last time
            if (path == null && SetLastScene.Value && !string.IsNullOrEmpty(LastScene.Value)) path = LastScene.Value;
            if (path != null && File.Exists(path))
                StartCoroutine(SetSceneAgain(freeH, path));
            else
                Logger.LogMessage(Tr($"Studio scene loader: choose a scene with the button or the {WindowKey.Value} key", $"スタジオシーン読み込み: ボタンか {WindowKey.Value} キーでシーンを選べます"));
        }

        /// <summary>
        /// Back from H: sets the scene that was used again. FreeHScene.Start is a coroutine that ends by choosing the
        /// map of the H scene, so wait until it has done that.
        /// </summary>
        private System.Collections.IEnumerator SetSceneAgain(FreeHScene freeH, string path)
        {
            float timeout = Time.realtimeSinceStartup + 180f;
            while (freeH != null && Time.realtimeSinceStartup < timeout &&
                   (freeH.member?.resultMapInfo == null || freeH.member.resultMapInfo.Value == null))
                yield return null;
            // After the game was started Studio is not made yet (it is made when the window first opens)
            if (freeH != null) StudioHost.EnsureCreated();
            while (freeH != null && Time.realtimeSinceStartup < timeout && !StudioHost.IsReady && StudioHost.Error == null)
                yield return null;
            yield return new WaitForSeconds(0.5f);
            if (freeH == null || freeH != _freeH || Selection.IsSelected || freeH.member?.resultMapInfo?.Value == null)
                yield break;
            try
            {
                ChooseScene(path);
                Logger.LogMessage(Selection.IsSelected
                    ? Tr($"Studio scene \"{Path.GetFileNameWithoutExtension(path)}\" set again (change it with the {WindowKey.Value} key)", $"スタジオシーン「{Path.GetFileNameWithoutExtension(path)}」をもう一度セットしました（{WindowKey.Value} キーで変更できます）")
                    : Tr($"Could not set the studio scene \"{Path.GetFileNameWithoutExtension(path)}\" again: {_status}", $"スタジオシーン「{Path.GetFileNameWithoutExtension(path)}」をもう一度セットできませんでした: {_status}"));
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not set the studio scene again: {ex}");
            }
        }

        private GUIStyle _buttonStyle;

        /// <summary>Screen rects of the windows and buttons drawn in this frame (see <see cref="GuiBlocker"/>).</summary>
        private readonly List<Rect> _blockRects = new List<Rect>();

        private void OnGUI()
        {
            bool repaint = Event.current.type == EventType.Repaint;
            if (repaint) _blockRects.Clear();
            DrawGui();
            if (repaint)
            {
                GuiBlocker.SetRects(_blockRects);
                _windowRects.Clear();
                _windowRects.AddRange(_blockRects);
            }
        }

        private readonly List<Rect> _windowRects = new List<Rect>();
        private bool _mouseCaptured;

        /// <summary>
        /// The mouse is over one of the plugin's windows or buttons, or a button was pressed there and is still held
        /// (dragging a window, a slider). The game's camera reads the mouse directly (BaseCameraControl_Ver2), not
        /// through the UI, so it is kept still then (see Hooks).
        /// </summary>
        internal static bool MouseOnPluginWindow => Instance != null && (Instance._mouseCaptured || Instance.MouseOverWindow());

        private bool MouseOverWindow()
        {
            var mouse = Input.mousePosition;
            var point = new Vector2(mouse.x, Screen.height - mouse.y);
            foreach (var r in _windowRects)
                if (r.Contains(point)) return true;
            return false;
        }

        private void UpdateMouseCapture()
        {
            bool down = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
            bool held = Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);
            if (down && MouseOverWindow()) _mouseCaptured = true;
            else if (!held) _mouseCaptured = false;
        }

        private void DrawGui()
        {
            if (_freeH == null)
            {
                if (!StudioHost.HasPlacedScene) return;
                if (ShowButtonInH.Value)
                {
                    var label = _showCharaWindow ? Tr($"Close studio scene characters ({WindowKey.Value})", $"スタジオシーンのキャラクターを閉じる ({WindowKey.Value})") : Tr($"Studio scene characters ({WindowKey.Value})", $"スタジオシーンのキャラクター ({WindowKey.Value})");
                    if (DrawButton(label, ButtonInHPosition.Value)) _showCharaWindow = !_showCharaWindow;
                }
                if (_showCharaWindow)
                {
                    _charaWindowRect = KeepOnScreen(GUILayout.Window(GUID.GetHashCode() + 1, _charaWindowRect, DrawCharaWindow, Tr("Studio scene characters", "スタジオシーンのキャラクター"), WindowStyle, GUILayout.Width(CharaWindowWidth + ScrollbarWidth)));
                    _blockRects.Add(_charaWindowRect);
                }
                else _cardTarget = null;
                DrawCardWindowIfOpen();
                return;
            }
            if (ShowButton.Value) DrawOpenButton();
            if (!_showWindow) return;
            // Two columns: the scene list on the left, the chosen scene's settings on the right
            float width = Mathf.Min(WindowWidth, Screen.width - 20);
            _windowRect.width = width;
            _windowRect = KeepOnScreen(GUILayout.Window(GUID.GetHashCode(), _windowRect, DrawWindow, Tr("Use a studio scene as the Free H map", "スタジオシーンをFree-Hのマップにする"), WindowStyle, GUILayout.Width(width)));
            _blockRects.Add(_windowRect);
            DrawCardWindowIfOpen();
        }

        #region Card list (replacing a scene character)

        private StudioHost.SceneCharacter _cardTarget;
        private Rect _cardWindowRect = new Rect(120, 80, 640, 640);
        private Vector2 _cardScroll;
        private string _cardFilter = "";
        private string _cardStatus;
        private List<string> _cardFiles;
        private string[] _cardPending;
        private int _cardPendingIndex;
        private bool? _cardListFemale;
        /// <summary>The character cards listed are female ones (a character can be replaced with the other sex).</summary>
        private bool _cardFemale;
        /// <summary>The list shows coordinate cards (to put on) instead of character cards (to replace with).</summary>
        private bool _cardCoordinates;
        private bool? _cardListCoordinates;
        private const int CardColumns = 4;
        private const float CardListHeight = 520f;

        private bool _focusCardWindow;

        private FolderChooser _cardFolder;

        /// <summary>The setting holding the folder of the list now shown (female cards, male cards or coordinates).</summary>
        private ConfigEntry<string> CardFolderEntry => _cardCoordinates ? CoordinateFolder : _cardFemale ? CardFolderFemale : CardFolderMale;

        /// <summary>
        /// The folder a list is read from, kept in a setting: typed in, one level up, back to the default, or one of
        /// its subfolders; with or without the files in the subfolders.
        /// </summary>
        private sealed class FolderChooser
        {
            public Func<ConfigEntry<string>> Entry;
            public ConfigEntry<bool> IncludeSubfolders;
            /// <summary>The list has to be read again.</summary>
            public Action Changed;
            public Action<string> Error;
            public int MaxRows = 3;
            private const int Columns = 4;

            private string _input;
            private string[] _subfolders = new string[0];
            private Vector2 _scroll;

            public string Full => FullFolder(Entry().Value);

            /// <summary>Shows the folder of the setting again (when the list switches to another setting).</summary>
            public void Reset()
            {
                _input = Entry().Value;
                _scroll = Vector2.zero;
            }

            /// <summary>The PNG files in the folder (and its subfolders when chosen), newest first; also lists the subfolders.</summary>
            public string[] Scan()
            {
                if (_input == null) _input = Entry().Value;
                var dir = Full;
                try
                {
                    if (dir == null || !Directory.Exists(dir))
                    {
                        _subfolders = new string[0];
                        return new string[0];
                    }
                    _subfolders = Directory.GetDirectories(dir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
                    return Directory.GetFiles(dir, "*.png", IncludeSubfolders.Value ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                        .OrderByDescending(File.GetLastWriteTime).ToArray();
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Could not list the files in {dir}: {ex.Message}");
                    _subfolders = new string[0];
                    return new string[0];
                }
            }

            private void Set(string full)
            {
                if (full == null || !Directory.Exists(full))
                {
                    Error(Tr("This folder does not exist.", "このフォルダはありません。"));
                    return;
                }
                Entry().Value = ShortFolder(full);
                _input = Entry().Value;
                _scroll = Vector2.zero;
                Changed();
            }

            public void Draw()
            {
                var entry = Entry();
                var full = Full;
                GUILayout.BeginHorizontal();
                GUILayout.Label(Tr("Folder:", "フォルダ:"), GUILayout.Width(60));
                _input = GUILayout.TextField(_input ?? entry.Value);
                if (GUILayout.Button(Tr("Open", "開く"), GUILayout.ExpandWidth(false))) Set(FullFolder(_input));
                if (GUILayout.Button(Tr("↑ Up", "↑ 上へ"), GUILayout.ExpandWidth(false)) && full != null)
                {
                    var parent = Path.GetDirectoryName(full);
                    if (parent != null) Set(parent);
                }
                if (GUILayout.Button(Tr("Default", "既定"), GUILayout.ExpandWidth(false))) Set(FullFolder((string)entry.DefaultValue));
                GUILayout.EndHorizontal();
                var sub = GUILayout.Toggle(IncludeSubfolders.Value, Tr("Include subfolders", "サブフォルダも表示"), GUILayout.ExpandWidth(false));
                if (sub != IncludeSubfolders.Value)
                {
                    IncludeSubfolders.Value = sub;
                    Changed();
                }
                if (_subfolders.Length > 0)
                {
                    int rows = (_subfolders.Length + Columns - 1) / Columns;
                    _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(Mathf.Min(rows, MaxRows) * 24f + 6f));
                    int chosen = GUILayout.SelectionGrid(-1, _subfolders, Columns);
                    GUILayout.EndScrollView();
                    if (chosen >= 0 && full != null) Set(Path.Combine(full, _subfolders[chosen]));
                }
            }
        }

        private static string FullFolder(string folder)
        {
            try { return Path.GetFullPath(Path.IsPathRooted(folder) ? folder : Path.Combine(Paths.GameRootPath, folder)); }
            catch { return null; }
        }

        /// <summary>The folder as it is shown and saved: relative to the game folder when inside it.</summary>
        private static string ShortFolder(string full)
        {
            var root = Path.GetFullPath(Paths.GameRootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length).Replace('\\', '/') : full;
        }

        private void OpenCardWindow(StudioHost.SceneCharacter chara, bool coordinates)
        {
            _focusCardWindow = true; // also when it is already open behind another window
            _cardTarget = chara;
            _cardStatus = null;
            _cardCoordinates = coordinates;
            if (!coordinates) _cardFemale = chara.NowFemale;
            UpdateCardList();
        }

        /// <summary>Reads the card list again when another kind of card is to be listed.</summary>
        private void UpdateCardList()
        {
            bool coordinates = _cardCoordinates;
            // Coordinate cards are the same list for both sexes
            bool? female = coordinates ? (bool?)null : _cardFemale;
            if (_cardListCoordinates != coordinates || _cardListFemale != female)
            {
                _cardFiles = null;
                _cardFolder.Reset();
                _cardListCoordinates = coordinates;
                _cardListFemale = female;
                _cardScroll = Vector2.zero;
            }
        }

        private void DrawCardWindowIfOpen()
        {
            if (_cardTarget == null) return;
            if (!StudioHost.SceneCharacters.Contains(_cardTarget)) { _cardTarget = null; return; }
            if (_focusCardWindow)
            {
                GUI.BringWindowToFront(GUID.GetHashCode() + 2);
                GUI.FocusWindow(GUID.GetHashCode() + 2);
                _focusCardWindow = false;
            }
            _cardWindowRect = KeepOnScreen(GUILayout.Window(GUID.GetHashCode() + 2, _cardWindowRect, DrawCardWindow,
                _cardCoordinates ? Tr("Coordinate card to load", "読み込むコーデカード") : Tr($"Character card to replace with ({(_cardFemale ? "female" : "male")})", $"差し替えるキャラカード（{(_cardFemale ? "女性" : "男性")}）"), WindowStyle, GUILayout.Width(_cardWindowRect.width)));
            _blockRects.Add(_cardWindowRect);
        }

        /// <summary>Finds the cards a few files per frame (from Update), like the scene list.</summary>
        private void ScanCardFiles()
        {
            if (_cardTarget == null) return;
            if (_cardFiles == null)
            {
                _cardPending = _cardFolder.Scan();
                _cardPendingIndex = 0;
                _cardFiles = new List<string>();
            }
            if (_cardPending == null) return;
            var until = Time.realtimeSinceStartup + 0.02f;
            while (_cardPendingIndex < _cardPending.Length && Time.realtimeSinceStartup < until)
            {
                var p = _cardPending[_cardPendingIndex++];
                if (_cardCoordinates ? StudioHost.IsCoordinateCard(p) : StudioHost.IsCard(p)) _cardFiles.Add(p);
            }
            if (_cardPendingIndex >= _cardPending.Length) _cardPending = null;
        }

        private void DrawCardWindow(int id)
        {
            var chara = _cardTarget;
            if (CloseButton(_cardWindowRect)) { _cardTarget = null; return; }
            LanguageButton(_cardWindowRect, true);
            if (_cardCoordinates)
                GUILayout.Label($"{(chara.NowFemale ? "♀ " : "♂ ")}{chara.DisplayName} ({StudioHost.OutfitName(chara, StudioHost.OutfitType(chara))})" +
                                Tr("   Click a coordinate card to load it into the current outfit (saved per scene).", "　コーデカードをクリックすると、今の服の種類に読み込みます（シーンごとに保存）。"));
            else
            {
                GUILayout.Label($"{(chara.Female ? "♀ " : "♂ ")}{chara.Name}" + (chara.ReplacedName != null ? Tr($" (now: {SexMark(chara)}{chara.ReplacedName})", $"（今: {SexMark(chara)}{chara.ReplacedName}）") : "") +
                                Tr("   Click a card to replace the character with it. The pose and clothes state stay as in the scene (saved per scene).", "　カードをクリックすると差し替えます。ポーズと服の状態はシーンのままです（シーンごとに保存）。"));
                // A card of the other sex changes the character's sex (posed as in the scene)
                GUILayout.BeginHorizontal();
                bool female = GUILayout.Toolbar(_cardFemale ? 0 : 1, new[] { Tr("Female cards", "女性カード"), Tr("Male cards", "男性カード") }, GUILayout.Width(220)) == 0;
                if (female != _cardFemale)
                {
                    _cardFemale = female;
                    UpdateCardList();
                }
                if (_cardFemale != chara.Female)
                    GUILayout.Label(Tr($"The character becomes {(_cardFemale ? "female" : "male")}.", $"{(_cardFemale ? "女性" : "男性")}に変わります。"));
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(Tr("Filter:", "絞り込み:"), GUILayout.Width(60));
            _cardFilter = GUILayout.TextField(_cardFilter);
            if (GUILayout.Button(Tr("Reload", "再読込"), GUILayout.Width(70))) _cardFiles = null;
            GUILayout.EndHorizontal();
            _cardFolder.Draw();
            if (_cardFiles == null || _cardPending != null)
                GUILayout.Label(Tr($"Checking cards… {_cardPendingIndex}/{_cardPending?.Length ?? 0}", $"カードを確認中… {_cardPendingIndex}/{_cardPending?.Length ?? 0}"));
            else
                GUILayout.Label(_cardCoordinates
                    ? Tr($"Coordinate cards: {_cardFiles.Count}", $"コーデカード: {_cardFiles.Count} 枚")
                    : Tr($"Cards: {_cardFiles.Count} (Koikatsu Sunshine cards only)", $"カード: {_cardFiles.Count} 枚（コイカツサンシャインのカードのみ）"));

            _cardScroll = GUILayout.BeginScrollView(_cardScroll, GUILayout.Height(CardListHeight));
            if (_cardFiles != null)
            {
                if (Event.current.type == EventType.Repaint && !(_showWindow && ShowThumbnails.Value)) _thumbWanted.Clear();
                var files = _cardFiles.Where(f => _cardFilter.Length == 0 || f.IndexOf(_cardFilter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                float width = _cardWindowRect.width - 50f;
                float cellW = (width - ThumbGap * (CardColumns - 1)) / CardColumns;
                float imageH = cellW * 352f / 252f;
                float cellH = imageH + ThumbLabelHeight + ThumbGap;
                int rows = (files.Count + CardColumns - 1) / CardColumns;
                var area = GUILayoutUtility.GetRect(width, rows * cellH, GUILayout.Width(width), GUILayout.Height(rows * cellH));
                int firstRow = Mathf.Max(0, Mathf.FloorToInt(_cardScroll.y / cellH));
                int lastRow = Mathf.Min(rows - 1, Mathf.CeilToInt((_cardScroll.y + CardListHeight) / cellH));
                for (int row = firstRow; row <= lastRow; row++)
                {
                    for (int col = 0; col < CardColumns; col++)
                    {
                        int index = row * CardColumns + col;
                        if (index >= files.Count) break;
                        var f = files[index];
                        var cell = new Rect(area.x + col * (cellW + ThumbGap), area.y + row * cellH, cellW, imageH + ThumbLabelHeight);
                        if (GUI.Button(cell, GUIContent.none))
                            _cardStatus = _cardCoordinates
                                ? StudioHost.SetOutfitCard(chara, f) ?? Tr($"Loaded the coordinate \"{StudioHost.OutfitCardName(chara)}\" on {chara.DisplayName}.", $"{chara.DisplayName} にコーデ「{StudioHost.OutfitCardName(chara)}」を読み込みました。")
                                : StudioHost.SetReplacement(chara, f) ?? Tr($"Replaced {chara.Name} with \"{chara.ReplacedName}\".", $"{chara.Name} を「{chara.ReplacedName}」に差し替えました。");
                        var imageRect = new Rect(cell.x + 2, cell.y + 2, cell.width - 4, imageH - 4);
                        if (_thumbs.TryGetValue(f, out var tex) && tex != null)
                        {
                            if (Event.current.type == EventType.Repaint) GUI.DrawTexture(imageRect, tex, ScaleMode.ScaleToFit);
                        }
                        else if (!_thumbFailed.Contains(f) && Event.current.type == EventType.Repaint)
                            _thumbWanted.Add(f);
                        GUI.Label(new Rect(cell.x, cell.y + imageH - 2, cell.width, ThumbLabelHeight), Path.GetFileNameWithoutExtension(f), _thumbLabelStyle ?? GUI.skin.label);
                    }
                }
            }
            GUILayout.EndScrollView();
            if (!string.IsNullOrEmpty(_cardStatus)) GUILayout.Label(_cardStatus);
            GUILayout.BeginHorizontal();
            if (!_cardCoordinates && chara.ReplacedName != null && GUILayout.Button(Tr("Back to the original character", "元のキャラに戻す"), GUILayout.ExpandWidth(false)))
                _cardStatus = StudioHost.SetReplacement(chara, null) ?? Tr($"Back to {chara.Name}.", $"{chara.Name} に戻しました。");
            if (_cardCoordinates && StudioHost.HasOutfit(chara) && GUILayout.Button(Tr("Reset clothes", "服を戻す"), GUILayout.ExpandWidth(false)))
                _cardStatus = StudioHost.ResetOutfit(chara) ?? Tr($"{chara.Name}'s clothes are back as in the scene.", $"{chara.Name} の服をシーンのままに戻しました。");
            if (GUILayout.Button(Tr("Close", "閉じる"), GUILayout.ExpandWidth(false))) _cardTarget = null;
            GUILayout.EndHorizontal();
            GUI.DragWindow();
        }

        #endregion

        private const float WindowWidth = 1180f;

        private GUIStyle _windowStyle;
        private Texture2D _windowTexture;
        private float _windowStyleOpacity = -1f;

        /// <summary>The default window with a darker, still translucent background (the default one is hard to read on bright screens).</summary>
        private GUIStyle WindowStyle
        {
            get
            {
                float opacity = WindowOpacity.Value;
                if (_windowStyle != null && _windowTexture != null && Mathf.Approximately(opacity, _windowStyleOpacity)) return _windowStyle;
                if (_windowTexture == null) _windowTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false) { hideFlags = HideFlags.HideAndDontSave };
                _windowTexture.SetPixel(0, 0, new Color(0.08f, 0.08f, 0.1f, opacity));
                _windowTexture.Apply();
                _windowStyle = new GUIStyle(GUI.skin.window);
                foreach (var state in new[] { _windowStyle.normal, _windowStyle.onNormal, _windowStyle.focused, _windowStyle.onFocused, _windowStyle.hover, _windowStyle.onHover, _windowStyle.active, _windowStyle.onActive })
                {
                    state.background = _windowTexture;
                    state.textColor = Color.white;
                }
                _windowStyleOpacity = opacity;
                return _windowStyle;
            }
        }

        /// <summary>How much of a window stays on the screen when it is dragged past the left or right edge.</summary>
        private const float KeepVisibleWidth = 100f;

        /// <summary>
        /// Keeps a window's title bar on the screen, so it can't be dragged away: it may go past the left and right
        /// edges and the bottom as long as a part of it is left to drag it back.
        /// </summary>
        private static Rect KeepOnScreen(Rect r)
        {
            r.x = Mathf.Clamp(r.x, KeepVisibleWidth - r.width, Screen.width - KeepVisibleWidth);
            r.y = Mathf.Clamp(r.y, 0, Mathf.Max(0, Screen.height - 40));
            return r;
        }

        private void DrawOpenButton()
        {
            var label = Selection.IsSelected
                ? Tr($"Studio scene: {Path.GetFileNameWithoutExtension(Selection.ScenePath)} ({WindowKey.Value})", $"スタジオシーン: {Path.GetFileNameWithoutExtension(Selection.ScenePath)} ({WindowKey.Value})")
                : Tr($"Choose a studio scene ({WindowKey.Value})", $"スタジオシーンを選ぶ ({WindowKey.Value})");
            if (_showWindow) label = Tr($"Close the studio scene window ({WindowKey.Value})", $"スタジオシーンの窓を閉じる ({WindowKey.Value})");
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
            _blockRects.Add(rect);
            return GUI.Button(rect, label, _buttonStyle);
        }

        private void DrawWindow(int id)
        {
            LanguageButton(_windowRect, false);
            if (StudioHost.Error != null)
            {
                GUILayout.Label(Tr("Initialization failed. See output_log.txt.", "初期化に失敗しました。output_log.txt を確認してください。"));
                GUILayout.Label(StudioHost.Error);
                GUI.DragWindow();
                return;
            }
            if (!StudioHost.IsReady)
            {
                GUILayout.Label(Tr("Loading the studio data… (first time only, this takes a while)", "スタジオのデータを読み込み中…（初回のみ、時間がかかります）"));
                GUI.DragWindow();
                return;
            }

            if (_files == null) return; // filled in by ScanSceneFiles (Update), never while drawing

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(ListColumnWidth));
            DrawSceneList();
            GUILayout.EndVertical();
            GUILayout.Space(8);
            GUILayout.BeginVertical();
            _settingsScroll = GUILayout.BeginScrollView(_settingsScroll, GUILayout.Height(ColumnHeight));
            DrawSceneSettings();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUI.DragWindow();
        }

        private const float ListColumnWidth = 520f;
        private const float ColumnHeight = 600f;
        private Vector2 _settingsScroll;

        /// <summary>Left column: finding and choosing a scene.</summary>
        private void DrawSceneList()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Tr("Filter:", "絞り込み:"), GUILayout.Width(60));
            _filter = GUILayout.TextField(_filter);
            if (GUILayout.Button(Tr("Reload", "再読込"), GUILayout.Width(70))) _reloadRequested = true;
            GUILayout.EndHorizontal();
            _sceneFolder.Draw();
            DrawFilters();
            // Worked out once per layout pass, so the layout and the repaint pass show the same scenes
            if (_shown == null || Event.current.type == EventType.Layout) UpdateShownScenes();

            if (_pending != null)
                GUILayout.Label(Tr($"Checking scene files… {_pendingIndex}/{_pending.Length} (PNGs that are not scenes are not listed)", $"シーンファイルを確認中… {_pendingIndex}/{_pending.Length}（シーン以外のPNGは表示しません）"));
            else if (_notInDataList > 0)
                GUILayout.Label(Tr($"Scenes: {_shown.Count}/{_files.Count}   {_notInDataList} not in the data list are left out (Make data list)", $"シーン: {_shown.Count}/{_files.Count} 件　データリストにない {_notInDataList} 件は表示していません（データリスト作成）"));
            else
                GUILayout.Label(Tr($"Scenes: {_shown.Count}/{_files.Count}   [KK] = Koikatsu (KK) format (some can't be loaded)", $"シーン: {_shown.Count}/{_files.Count} 件　[KK] は無印コイカツ形式（読み込めないものがあります）"));

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(CurrentListHeight));
            if (ShowThumbnails.Value)
                DrawThumbnailGrid();
            else
            {
                foreach (var f in _shown.ToArray())
                {
                    var rel = ListName(f);
                    var marks = DataListMarks(f);
                    if (marks.Length > 0) rel += "   (" + marks + ")";
                    var style = f == _previewPath ? GUI.skin.box : GUI.skin.button;
                    // Clicking a scene reads it and sets it for H (clicking it again after 解除 sets it again)
                    if (GUILayout.Button(rel, style, GUILayout.Width(ListColumnWidth - 30)) && (f != _previewPath || Selection.PreviewPath != f || Selection.ScenePath != f))
                        ChooseScene(f);
                }
            }
            GUILayout.EndScrollView();

            // The name list shows the chosen scene's image below it; the thumbnails take that space instead.
            // Fixed size so the window does not grow to the texture size
            if (!ShowThumbnails.Value)
            {
                var previewRect = GUILayoutUtility.GetRect(ListColumnWidth, PreviewHeight, GUILayout.Width(ListColumnWidth), GUILayout.Height(PreviewHeight));
                if (_preview != null)
                    GUI.DrawTexture(previewRect, _preview, ScaleMode.ScaleToFit);
            }

            GUILayout.Label(_status);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUI.enabled = Selection.IsSelected;
            if (GUILayout.Button(Tr("Unset (use the normal map for H)", "解除（通常のマップでHをする）"), GUILayout.ExpandWidth(false)))
            {
                Selection.Unset(_freeH);
                LastScene.Value = "";
                _status = Tr("Unset. The normal map is used. Click a scene again to set it.", "解除しました。通常のマップを使います。もう一度シーンをクリックするとセットします。");
            }
            GUI.enabled = true;
            GUILayout.Space(12);
            var thumbs = GUILayout.Toggle(ShowThumbnails.Value, Tr("Show thumbnails", "サムネイルで表示"), GUILayout.ExpandWidth(false));
            if (thumbs != ShowThumbnails.Value) ShowThumbnails.Value = thumbs;
            GUILayout.EndHorizontal();
        }

        private const float ListHeight = 250f;
        private const float PreviewHeight = 180f;
        private float CurrentListHeight => ShowThumbnails.Value ? ListHeight + PreviewHeight + 4f : ListHeight;

        /// <summary>The folder the listed scenes were read from (their names are shown relative to it).</summary>
        private string _sceneListRoot = "";

        private string ListName(string path) =>
            (SceneFile.IsKoikatsuFormat(path) ? "[KK] " : "[KKS] ") +
            (path.StartsWith(_sceneListRoot, StringComparison.OrdinalIgnoreCase) ? path.Substring(_sceneListRoot.Length) : path).TrimStart('\\', '/');

        #region Scene thumbnails

        private const int ThumbColumns = 3;
        private const float ThumbGap = 4f;
        private const float ThumbLabelHeight = 18f;
        /// <summary>Loaded thumbnails are kept up to this many (least recently shown dropped first).</summary>
        private const int MaxThumbnails = 150;
        private readonly Dictionary<string, Texture2D> _thumbs = new Dictionary<string, Texture2D>();
        private readonly LinkedList<string> _thumbOrder = new LinkedList<string>();
        private readonly HashSet<string> _thumbFailed = new HashSet<string>();
        private readonly List<string> _thumbWanted = new List<string>();
        private GUIStyle _thumbLabelStyle;

        /// <summary>
        /// Three thumbnails per row, scaled to fit the list. Only the rows in view are drawn, and their images are
        /// loaded a few per frame (in Update), so long lists stay quick.
        /// </summary>
        private void DrawThumbnailGrid()
        {
            if (_thumbLabelStyle == null)
                _thumbLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.UpperCenter, wordWrap = false, clipping = TextClipping.Clip };
            var files = _shown;
            float width = ListColumnWidth - 30f;
            float cellW = (width - ThumbGap * (ThumbColumns - 1)) / ThumbColumns;
            float imageH = cellW * 9f / 16f;
            float cellH = imageH + ThumbLabelHeight + ThumbGap;
            int rows = (files.Count + ThumbColumns - 1) / ThumbColumns;
            var area = GUILayoutUtility.GetRect(width, rows * cellH, GUILayout.Width(width), GUILayout.Height(rows * cellH));
            if (Event.current.type == EventType.Repaint) _thumbWanted.Clear();
            int firstRow = Mathf.Max(0, Mathf.FloorToInt(_scroll.y / cellH));
            int lastRow = Mathf.Min(rows - 1, Mathf.CeilToInt((_scroll.y + CurrentListHeight) / cellH));
            for (int row = firstRow; row <= lastRow; row++)
            {
                for (int col = 0; col < ThumbColumns; col++)
                {
                    int index = row * ThumbColumns + col;
                    if (index >= files.Count) break;
                    var f = files[index];
                    var cell = new Rect(area.x + col * (cellW + ThumbGap), area.y + row * cellH, cellW, imageH + ThumbLabelHeight);
                    bool chosen = f == _previewPath;
                    if (GUI.Button(cell, GUIContent.none) && (f != _previewPath || Selection.PreviewPath != f || Selection.ScenePath != f))
                        ChooseScene(f);
                    if (chosen && Event.current.type == EventType.Repaint) DrawFrame(cell, ChosenFrameColor, 3f);
                    var imageRect = new Rect(cell.x + 2, cell.y + 2, cell.width - 4, imageH - 4);
                    if (_thumbs.TryGetValue(f, out var tex) && tex != null)
                    {
                        if (Event.current.type == EventType.Repaint) GUI.DrawTexture(imageRect, tex, ScaleMode.ScaleToFit);
                    }
                    else if (!_thumbFailed.Contains(f) && Event.current.type == EventType.Repaint)
                        _thumbWanted.Add(f);
                    var name = (SceneFile.IsKoikatsuFormat(f) ? "[KK] " : "") + Path.GetFileNameWithoutExtension(f);
                    GUI.Label(new Rect(cell.x, cell.y + imageH - 2, cell.width, ThumbLabelHeight), name, _thumbLabelStyle);
                    if (chosen && Selection.ScenePath == f && Event.current.type == EventType.Repaint)
                        GUI.Label(new Rect(cell.x + 4, cell.y + 2, cell.width, ThumbLabelHeight), Tr("In use", "セット中"), _thumbLabelStyle);
                    var marks = DataListMarks(f);
                    if (marks.Length > 0 && Event.current.type == EventType.Repaint)
                    {
                        var marksRect = new Rect(cell.x + 2, cell.y + imageH - ThumbLabelHeight - 2, cell.width - 4, ThumbLabelHeight - 2);
                        var old = GUI.color;
                        GUI.color = new Color(0f, 0f, 0f, 0.6f);
                        GUI.DrawTexture(marksRect, Texture2D.whiteTexture);
                        GUI.color = old;
                        GUI.Label(marksRect, marks, _thumbLabelStyle);
                    }
                }
            }
        }

        private static readonly Color ChosenFrameColor = new Color(1f, 0.55f, 0.55f, 1f);

        private static void DrawFrame(Rect r, Color color, float thickness)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - thickness, r.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, thickness, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - thickness, r.y, thickness, r.height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// <summary>Loads a few of the thumbnails the list wants to show (called from Update).</summary>
        private void LoadWantedThumbnails()
        {
            if (_thumbWanted.Count == 0) return;
            var until = Time.realtimeSinceStartup + 0.012f;
            foreach (var path in _thumbWanted.ToList())
            {
                if (Time.realtimeSinceStartup > until) break;
                _thumbWanted.Remove(path);
                if (_thumbs.ContainsKey(path) || _thumbFailed.Contains(path)) continue;
                try
                {
                    int w = 0, h = 0;
                    var tex = PngAssist.ChangeTextureFromPngByte(PngFile.LoadPngBytes(path), ref w, ref h);
                    if (tex == null) { _thumbFailed.Add(path); continue; }
                    _thumbs[path] = tex;
                    _thumbOrder.AddLast(path);
                    while (_thumbOrder.Count > MaxThumbnails)
                    {
                        var old = _thumbOrder.First.Value;
                        _thumbOrder.RemoveFirst();
                        if (_thumbs.TryGetValue(old, out var oldTex) && oldTex != null) Destroy(oldTex);
                        _thumbs.Remove(old);
                    }
                }
                catch (Exception ex)
                {
                    _thumbFailed.Add(path);
                    Logger.LogDebug($"Could not load the thumbnail of {path}: {ex.Message}");
                }
            }
        }

        private void ClearThumbnails()
        {
            foreach (var tex in _thumbs.Values) if (tex != null) Destroy(tex);
            _thumbs.Clear();
            _thumbOrder.Clear();
            _thumbFailed.Clear();
            _thumbWanted.Clear();
        }

        #endregion

        private void ChooseScene(string path)
        {
            if (path != _previewPath) SetPreview(path);
            _status = Selection.Preview(_freeH, path) ?? "";
            if (Selection.PreviewPath == path && StudioHost.HasSceneData)
                _status = Selection.Use(_freeH);
            // A scene that could not be set leaves nothing set: show the normal map again
            if (!Selection.IsSelected) Selection.ShowNormalMap(_freeH);
            LastScene.Value = Selection.IsSelected ? Selection.ScenePath : "";
        }

        /// <summary>Right column: the settings of the chosen scene (saved per scene).</summary>
        private void DrawSceneSettings()
        {
            if (Selection.PreviewPath == null || !StudioHost.HasSceneData)
            {
                GUILayout.Label(Tr("Click a scene in the list on the left to set it for Free H. Its details and settings (base map, time of day, characters, sounds) appear here. Settings are saved per scene.", "左の一覧でシーンをクリックすると、フリーHにセットして、ここにシーンの詳細と設定（土台のマップ、時間帯、キャラクター、音）を出します。設定はシーンごとに保存します。"));
                return;
            }
            GUILayout.Label(Tr($"Scene: {Path.GetFileNameWithoutExtension(Selection.PreviewPath)}", $"シーン: {Path.GetFileNameWithoutExtension(Selection.PreviewPath)}"));
            GUILayout.Label(Selection.ScenePath == Selection.PreviewPath ? Tr("Status: set (H starts with this scene)", "状態: セット済み（このシーンでHを始めます）") : Tr("Status: not set (click it in the list again to set it)", "状態: 未セット（左の一覧でもう一度クリックするとセットします）"));
            GUILayout.Label(Selection.MapDescription);
            DrawMissingMods();
            if (!Selection.NoMap && Selection.Candidates.Count > 1)
            {
                GUILayout.Label(Tr("Base map: several Free H maps use this studio map (with different H positions; saved per scene)", "土台のマップ: このスタジオマップを使うFree-H用マップが複数あります（H位置が違います。シーンごとに保存します）"));
                GUILayout.BeginHorizontal();
                foreach (var map in Selection.Candidates)
                    if (GUILayout.Button($"{map.DisplayName} (No {map.No})", map.No == Selection.MapNo ? GUI.skin.box : GUI.skin.button))
                        Selection.ChooseMap(_freeH, map);
                GUILayout.EndHorizontal();
            }
            if (StudioHost.Settings != null && StudioHost.SceneSunLightType >= 0 && StudioHost.SceneSunLightType < TimeNames.Length)
            {
                bool match = GUILayout.Toggle(StudioHost.Settings.MatchTime, Tr($"Use the studio scene's time of day (scene: {TimeNames[StudioHost.SceneSunLightType]}; changes how bright the map is)", $"時間帯をスタジオのシーンに合わせる（シーン: {TimeNames[StudioHost.SceneSunLightType]}。マップの明るさが変わります）"));
                if (match != StudioHost.Settings.MatchTime)
                {
                    StudioHost.SetMatchTime(match);
                    if (match && Selection.IsSelected) Selection.ApplySceneTime(_freeH);
                }
            }
            GUILayout.Space(6);
            DrawCharacterChoice();
            GUILayout.Space(6);
            GUILayout.Label(Tr("Sounds (also switched in the F7 window during H):", "音（H 中も F7 の窓で切り替えられます）:"));
            DrawSoundChoice();
        }

        internal static string[] TimeNames => Japanese ? TimeNamesJa : TimeNamesEn;
        private static readonly string[] TimeNamesEn = { "morning", "day", "evening", "night" };
        private static readonly string[] TimeNamesJa = { "朝", "昼", "夕方", "夜" };

        private Vector2 _missingScroll;
        private string _copyStatus;
        private string _copyStatusFor;
        private GUIStyle _missingStyle;
        private GUIStyle _allModsStyle;

        /// <summary>
        /// The mods the scene needs that are not installed, as reported by Sideloader when the scene was read (that
        /// happens when the scene is clicked in the list).
        /// </summary>
        private void DrawMissingMods()
        {
            if (Selection.PreviewPath == null || !StudioHost.HasSceneData) return;
            if (_missingStyle == null)
            {
                // Light red for what is missing, light yellow-green when nothing is
                _missingStyle = new GUIStyle(GUI.skin.label) { normal = { textColor = new Color(1f, 0.62f, 0.62f) } };
                _allModsStyle = new GUIStyle(GUI.skin.label) { normal = { textColor = new Color(0.78f, 1f, 0.5f) } };
            }
            if (_copyStatusFor != Selection.PreviewPath) _copyStatus = null;
            var mods = StudioHost.MissingMods;
            GUILayout.BeginHorizontal();
            if (mods.Count == 0)
                GUILayout.Label(Tr("All mods are installed (as checked by Sideloader when the scene was loaded).", "MODは揃っています（シーンを読み込んだときに Sideloader が調べたもの）。"), _allModsStyle);
            else
                GUILayout.Label(Tr($"Missing mods: {mods.Count} (most used first)", $"足りないMOD: {mods.Count} 種類（使われている回数の多い順）"), _missingStyle);
            if (GUILayout.Button(Tr("Copy to clipboard", "クリップボードにコピー"), GUILayout.ExpandWidth(false)))
            {
                GUIUtility.systemCopyBuffer = StudioHost.MissingModsText(Selection.PreviewPath);
                _copyStatus = Tr("Copied.", "コピーしました。");
                _copyStatusFor = Selection.PreviewPath;
            }
            GUILayout.EndHorizontal();
            if (_copyStatus != null) GUILayout.Label(_copyStatus);
            if (StudioHost.ResolveFailed)
                GUILayout.Label(Tr("Sideloader failed while resolving the mods of this scene (error in the log). Some mod items may be missing or wrong.", "Sideloader がこのシーンのMODの解決中にエラーになりました（ログに出ています）。MODのアイテムが出ない・違うものになることがあります。"), _missingStyle);
            if (mods.Count == 0) return;
            _missingScroll = GUILayout.BeginScrollView(_missingScroll, GUILayout.Height(Mathf.Min(mods.Count, 8) * 22 + 10));
            foreach (var m in mods.OrderByDescending(m => m.Count))
            {
                var label = $"{(string.IsNullOrEmpty(m.Name) ? m.Guid : m.Name)} ({m.Count})";
                if (!string.IsNullOrEmpty(m.Author)) label += Tr($"  by {m.Author}", $"  作者: {m.Author}");
                if (!string.IsNullOrEmpty(m.Website)) label += $"  {m.Website}";
                GUILayout.Label(label, _missingStyle);
            }
            GUILayout.EndScrollView();
            GUILayout.Label(Tr("The number in ( ) is how often the scene uses the mod. Mods only found when the scene is placed in H (such as shaders) are not included.", "（ ）内はシーンで使われている回数です。シェーダーなど、Hで置いたときに初めて分かるものは含みません。"));
        }

        /// <summary>Which of the scene's characters are placed in H.</summary>
        private StudioHost.SceneCharacter _outfitOpen;
        private GUIStyle _frameStyle, _chosenFrameStyle;
        private GUIStyle FrameStyle => _frameStyle ?? (_frameStyle = MakeFrameStyle(new Color(0.65f, 0.65f, 0.7f, 0.9f)));
        private GUIStyle ChosenFrameStyle => _chosenFrameStyle ?? (_chosenFrameStyle = MakeFrameStyle(ChosenFrameColor));

        /// <summary>A box style that only draws a thin outline (the window background shows through).</summary>
        private static GUIStyle MakeFrameStyle(Color color)
        {
            const int size = 8, line = 2;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, x < line || y < line || x >= size - line || y >= size - line ? color : Color.clear);
            tex.Apply();
            var style = new GUIStyle
            {
                border = new RectOffset(line + 1, line + 1, line + 1, line + 1),
                padding = new RectOffset(6, 6, 4, 4),
                margin = new RectOffset(0, 0, 2, 2),
            };
            style.normal.background = tex;
            return style;
        }

        private void DrawCharacterChoice()
        {
            var charas = StudioHost.SceneCharacters;
            if (charas.Count == 0)
            {
                GUILayout.Label(Tr("This scene has no characters.", "このシーンにはキャラクターがいません。"));
                return;
            }
            GUILayout.Label(Tr($"Scene characters ({charas.Count}): the checked ones are placed posed as in Studio. \"Replace\" swaps one for another character card.", $"シーンのキャラクター（{charas.Count} 人）: チェックしたキャラクターを、スタジオのポーズのまま置きます。「差し替え」で別のキャラカードにできます。"));
            foreach (var female in new[] { true, false })
            {
                var list = charas.Where(c => c.Female == female).ToList();
                if (list.Count == 0) continue;
                bool on = female ? StudioHost.FemalesOn : StudioHost.MalesOn;
                StudioHost.SetSexEnabled(female, GUILayout.Toggle(on, Tr($"Show {(female ? "females" : "males")} ({list.Count})", $"{(female ? "女性" : "男性")}を表示（{list.Count} 人）")));
                GUI.enabled = female ? StudioHost.FemalesOn : StudioHost.MalesOn;
                foreach (var chara in list)
                {
                    // A frame around each character (light red while its clothes are open)
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(14);
                    GUILayout.BeginVertical(_outfitOpen == chara ? ChosenFrameStyle : FrameStyle);
                    GUILayout.BeginHorizontal();
                    StudioHost.SetCharacterEnabled(chara, GUILayout.Toggle(chara.Enabled, (chara.Female ? "♀ " : "♂ ") + chara.Name + (chara.ReplacedName != null ? $" → {SexMark(chara)}{chara.ReplacedName}" : "")));
                    DrawReplaceButtons(chara);
                    bool open = _outfitOpen == chara;
                    if (GUILayout.Button(open ? Tr("Clothes ▲", "服 ▲") : Tr("Clothes ▼", "服 ▼"), GUILayout.ExpandWidth(false))) _outfitOpen = open ? null : chara;
                    GUILayout.EndHorizontal();
                    if (_outfitOpen == chara)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Space(20);
                        GUILayout.BeginVertical();
                        bool enabled = GUI.enabled;
                        DrawOutfitControls(chara);
                        GUI.enabled = enabled;
                        GUILayout.EndVertical();
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();
                }
                GUI.enabled = true;
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(Tr("Defaults for new scenes:", "新しいシーンの初期設定:"), GUILayout.ExpandWidth(false));
            var females = GUILayout.Toggle(LoadFemales.Value, Tr("Show females", "女性を表示"), GUILayout.ExpandWidth(false));
            if (females != LoadFemales.Value) LoadFemales.Value = females;
            var males = GUILayout.Toggle(LoadMales.Value, Tr("Show males", "男性を表示"), GUILayout.ExpandWidth(false));
            if (males != LoadMales.Value) LoadMales.Value = males;
            GUILayout.EndHorizontal();
            GUILayout.Label(Tr("(for scenes whose settings were never changed)", "（設定を一度も変えていないシーンに使います）"));
        }

        /// <summary>A × at the top right of a window; true when it was pressed.</summary>
        private static bool CloseButton(Rect window)
        {
            return GUI.Button(new Rect(window.width - 26, 3, 22, 18), "×");
        }

        /// <summary>Switches the language; shows the name of the other one.</summary>
        private static void LanguageButton(Rect window, bool besideClose)
        {
            float right = besideClose ? window.width - 30 : window.width - 4;
            if (GUI.Button(new Rect(right - 64, 3, 64, 18), Japanese ? "English" : "日本語"))
                Language.Value = Japanese ? UiLanguage.English : UiLanguage.Japanese;
        }

        /// <summary>The clothes of one scene character: the kind of clothes and a coordinate card (saved per scene).</summary>
        private void DrawOutfitControls(StudioHost.SceneCharacter chara)
        {
            int type = StudioHost.OutfitType(chara);
            int count = StudioHost.OutfitCount(chara);
            var names = new string[count];
            for (int i = 0; i < count; i++) names[i] = StudioHost.OutfitName(chara, i);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Tr("Outfit", "服の種類"), GUILayout.Width(56));
            // Four per row: the game's four, then the ones MoreOutfits added
            int newType = GUILayout.SelectionGrid(Mathf.Clamp(type, 0, count - 1), names, 4);
            if (newType != type) StudioHost.SetOutfitType(chara, newType);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label(Tr("Clothes", "服の状態"), GUILayout.Width(56));
            int state = StudioHost.ClothesState(chara);
            int newState = GUILayout.Toolbar(state + 1, StudioHost.ClothesStateNames) - 1;
            if (newState != state) StudioHost.SetClothesState(chara, newState);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            var card = StudioHost.OutfitCardName(chara);
            GUILayout.Label(card != null ? Tr($"Coordinate: {card}", $"コーデ: {card}") : Tr("Coordinate: the character's own", "コーデ: キャラのまま"), GUILayout.ExpandWidth(true));
            if (GUILayout.Button(Tr("Load coordinate", "コーデを読み込む"), GUILayout.ExpandWidth(false))) OpenCardWindow(chara, true);
            GUI.enabled = GUI.enabled && StudioHost.HasOutfit(chara);
            if (GUILayout.Button(Tr("Reset clothes", "服を戻す"), GUILayout.ExpandWidth(false)))
                _cardStatus = StudioHost.ResetOutfit(chara) ?? Tr($"{chara.Name}'s clothes are back as in the scene.", $"{chara.Name} の服をシーンのままに戻しました。");
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        /// <summary>「差し替え」 (opens the card list) and 「戻す」 for one scene character.</summary>
        private void DrawReplaceButtons(StudioHost.SceneCharacter chara)
        {
            if (GUILayout.Button(Tr("Replace", "差し替え"), GUILayout.ExpandWidth(false))) OpenCardWindow(chara, false);
            if (chara.ReplacedName != null && GUILayout.Button(Tr("Original", "戻す"), GUILayout.ExpandWidth(false)))
                _cardStatus = StudioHost.SetReplacement(chara, null) ?? Tr($"Back to {chara.Name}.", $"{chara.Name} に戻しました。");
            if (chara.SexChanged)
                StudioHost.SetSon(chara, GUILayout.Toggle(StudioHost.SonShown(chara), Tr("Penis", "ちんちん"), GUILayout.ExpandWidth(false)));
        }

        /// <summary>The sex mark before the name of the card a character is replaced with, when the sex changes.</summary>
        private static string SexMark(StudioHost.SceneCharacter chara) => chara.SexChanged ? (chara.Female ? "♂ " : "♀ ") : "";

        /// <summary>The scene's sounds: when it is loaded (selection window) and while it is placed (H window). Saved per scene.</summary>
        private static void DrawSoundChoice()
        {
            var settings = StudioHost.Settings;
            if (settings == null) return;
            StudioHost.SetSoundItems(GUILayout.Toggle(settings.SoundItems, Tr("Play the scene's sound and voice items", "シーンの音・声アイテムを鳴らす（置かれた効果音・ボイスのアイテム）")));
            int voices = StudioHost.CharactersWithVoices;
            if (voices > 0 || settings.CharaVoices)
                StudioHost.SetCharaVoices(GUILayout.Toggle(settings.CharaVoices, Tr($"Play the characters' voice lists ({voices})", $"キャラのボイスを再生（スタジオのボイスリスト、{voices} 人）")));
            else
                GUILayout.Label(Tr("Character voices: no character in this scene has a voice list", "キャラのボイス: このシーンのキャラにはボイスリストがありません"));
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
                SceneIndex.Forget();
                _shown = null;
                _pending = _sceneFolder.Scan();
                _sceneListRoot = _sceneFolder.Full ?? "";
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

        #region Scene data list and filters

        private List<string> _shown;      // the scenes the list shows (worked out once per layout pass)
        private int _notInDataList;       // scenes left out by the filters because they are not in the data list
        private List<string> _dataQueue;  // scenes still to be read for the data list
        private int _dataDone, _dataTotal;

        private static bool FiltersOn =>
            FilterAnimation.Value != HasFilter.All || FilterCharacters.Value != CharacterFilter.All || FilterMissingMods.Value != HasFilter.All;

        private static bool Matches(HasFilter filter, bool has) =>
            filter == HasFilter.All || (filter == HasFilter.With) == has;

        private static bool MatchesFilters(SceneSummary s)
        {
            if (!Matches(FilterAnimation.Value, s.Animation)) return false;
            if (!Matches(FilterMissingMods.Value, s.ModError)) return false;
            switch (FilterCharacters.Value)
            {
                case CharacterFilter.FemalesOnly: return s.Females > 0 && s.Males == 0;
                case CharacterFilter.MalesOnly: return s.Males > 0 && s.Females == 0;
                case CharacterFilter.Both: return s.Females > 0 && s.Males > 0;
                default: return true;
            }
        }

        /// <summary>The scenes that pass the name filter and the data list filters.</summary>
        private void UpdateShownScenes()
        {
            _shown = new List<string>();
            _notInDataList = 0;
            bool filtersOn = FiltersOn;
            foreach (var f in _files)
            {
                if (_filter.Length > 0 && ListName(f).IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (filtersOn)
                {
                    var summary = SceneIndex.Get(f);
                    if (summary == null) { _notInDataList++; continue; }
                    if (!MatchesFilters(summary)) continue;
                }
                _shown.Add(f);
            }
        }

        private static readonly string[] HasFilterNamesEn = { "All", "With", "Without" };
        private static readonly string[] HasFilterNamesJa = { "すべて", "あり", "なし" };
        private static readonly string[] CharacterFilterNamesEn = { "All", "Females only", "Males only", "Both" };
        private static readonly string[] CharacterFilterNamesJa = { "すべて", "女性のみ", "男性のみ", "両方" };
        private static string[] HasFilterNames => Japanese ? HasFilterNamesJa : HasFilterNamesEn;
        private static string[] CharacterFilterNames => Japanese ? CharacterFilterNamesJa : CharacterFilterNamesEn;

        private void DrawFilters()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Tr("Animation:", "アニメ:"), GUILayout.Width(70));
            var anim = (HasFilter)GUILayout.Toolbar((int)FilterAnimation.Value, HasFilterNames, GUILayout.Width(160));
            GUILayout.Space(10);
            GUILayout.Label(Tr("Missing mods:", "MOD不足:"), GUILayout.Width(90));
            var mods = (HasFilter)GUILayout.Toolbar((int)FilterMissingMods.Value, HasFilterNames, GUILayout.Width(160));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label(Tr("Characters:", "キャラ:"), GUILayout.Width(70));
            var charas = (CharacterFilter)GUILayout.Toolbar((int)FilterCharacters.Value, CharacterFilterNames, GUILayout.Width(330));
            GUILayout.EndHorizontal();
            if (anim != FilterAnimation.Value) FilterAnimation.Value = anim;
            if (mods != FilterMissingMods.Value) FilterMissingMods.Value = mods;
            if (charas != FilterCharacters.Value) FilterCharacters.Value = charas;

            GUILayout.BeginHorizontal();
            if (_dataQueue != null)
            {
                GUILayout.Label(Tr($"Making the data list… {_dataDone}/{_dataTotal}", $"データリストを作成中… {_dataDone}/{_dataTotal}"));
                if (GUILayout.Button(Tr("Stop", "中止"), GUILayout.Width(70))) StopDataList();
            }
            else
            {
                int listed = _pending == null ? _files.Count(f => SceneIndex.Get(f) != null) : 0;
                GUILayout.Label(_pending != null ? "" : Tr($"Data list: {listed}/{_files.Count} scenes", $"データリスト: {listed}/{_files.Count} 件"));
                GUI.enabled = _pending == null && listed < _files.Count;
                if (GUILayout.Button(Tr("Make data list", "データリスト作成"), GUILayout.Width(130))) StartDataList(false);
                GUI.enabled = _pending == null && _files.Count > 0;
                if (GUILayout.Button(Tr("Remake all", "全部作り直す"), GUILayout.Width(100))) StartDataList(true);
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>Queues the scenes of the list that are not in the data list (or all of them, after installing mods).</summary>
        private void StartDataList(bool all)
        {
            _dataQueue = _files.Where(f => all || SceneIndex.Get(f) == null).Reverse().ToList();
            _dataDone = 0;
            _dataTotal = _dataQueue.Count;
            if (_dataTotal == 0) _dataQueue = null;
        }

        private void StopDataList()
        {
            _dataQueue = null;
            SceneIndex.Save();
            _shown = null;
        }

        /// <summary>
        /// Reads a scene or a few per frame for the data list (on the Free H screen only, with the window open or not).
        /// A scene takes as long as choosing it, so the list is made once and kept.
        /// </summary>
        private void BuildDataList()
        {
            if (_dataQueue == null || !StudioHost.IsReady) return;
            var until = Time.realtimeSinceStartup + 0.03f;
            do
            {
                var path = _dataQueue[_dataQueue.Count - 1];
                _dataQueue.RemoveAt(_dataQueue.Count - 1);
                _dataDone++;
                if (File.Exists(path)) SceneIndex.Set(path, StudioHost.ReadSummary(path));
                if (_dataDone % 50 == 0) SceneIndex.Save();
            }
            while (_dataQueue.Count > 0 && Time.realtimeSinceStartup < until);
            _shown = null;
            if (_dataQueue.Count > 0) return;
            _dataQueue = null;
            SceneIndex.RemoveDeleted();
            SceneIndex.Save();
            _status = Tr($"Data list made ({_dataTotal} scenes read).", $"データリストを作成しました（{_dataTotal} 件を読み込みました）。");
        }

        /// <summary>What the data list knows about the scene, short (shown on the thumbnails and in the name list).</summary>
        private static string DataListMarks(string path)
        {
            var s = SceneIndex.Get(path);
            if (s == null) return "";
            if (s.Failed) return Tr("Can't read", "読めません");
            var parts = new List<string>();
            if (s.Females > 0) parts.Add(Tr($"F{s.Females}", $"女{s.Females}"));
            if (s.Males > 0) parts.Add(Tr($"M{s.Males}", $"男{s.Males}"));
            if (s.Animation) parts.Add(Tr("Anim", "アニメ"));
            if (s.MissingMods > 0) parts.Add(Tr($"Missing mods {s.MissingMods}", $"MOD不足{s.MissingMods}"));
            if (s.ResolveError) parts.Add(Tr("Mod error", "MODエラー"));
            return string.Join(" ", parts.ToArray());
        }

        #endregion

        private void ClearPreview()
        {
            _previewPath = null;
            if (_preview != null) Destroy(_preview);
            _preview = null;
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
        private static string[] MoveModeNames => Japanese ? MoveModeNamesJa : MoveModeNamesEn;
        private static readonly string[] MoveModeNamesEn = { "Character", "With linked", "Whole scene" };
        private static readonly string[] MoveModeNamesJa = { "キャラだけ", "キャラと連結先", "シーン全体" };
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

            if (CloseButton(_charaWindowRect)) _showCharaWindow = false;
            LanguageButton(_charaWindowRect, true);
            // Taller than the screen: the content scrolls instead of the window running off the screen. The view is as
            // tall as the content (measured on the last repaint), the width as before plus the scrollbar.
            float maxHeight = Mathf.Max(200f, Screen.height - 60f);
            _charaWindowScroll = GUILayout.BeginScrollView(_charaWindowScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar,
                GUILayout.Height(Mathf.Min(_charaContentHeight + 4f, maxHeight)));
            GUILayout.BeginVertical(GUILayout.Width(CharaWindowWidth - WindowStyle.padding.horizontal));
            GUILayout.Label(Tr($"Show/hide and move the scene's characters ({WindowKey.Value} closes)", $"表示のオンオフと、位置の調整ができます（{WindowKey.Value} キーで閉じる）"));
            _charaScroll = GUILayout.BeginScrollView(_charaScroll, GUILayout.Height(Mathf.Min(placed.Count, 6) * 26 + 8));
            foreach (var chara in placed)
            {
                GUILayout.BeginHorizontal();
                var visible = StudioHost.IsVisible(chara);
                var newVisible = GUILayout.Toggle(visible, Tr("Show", "表示"), GUILayout.Width(56));
                if (newVisible != visible) StudioHost.SetVisible(chara, newVisible);
                var label = (chara.NowFemale ? "♀ " : "♂ ") + chara.DisplayName;
                if (GUILayout.Button(label, chara == _selectedChara ? GUI.skin.box : GUI.skin.button)) _selectedChara = chara;
                DrawReplaceButtons(chara);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            {
                // A scene without characters (or with all of them turned off) can still be moved as a whole
                bool noChara = _selectedChara == null;
                int moveMode = noChara ? 2 : _moveMode;
                if (noChara)
                    GUILayout.Label(Tr("This scene has no characters. The whole scene can be moved.", "このシーンにはキャラクターがいません。シーン全体を動かせます。"));
                else
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(Tr("Move", "動かす対象"), GUILayout.Width(70));
                    _moveMode = moveMode = GUILayout.Toolbar(_moveMode, MoveModeNames);
                    GUILayout.EndHorizontal();
                    if (moveMode != 2) _moveAll = GUILayout.Toggle(_moveAll, Tr("Move all together (keeps them in place relative to each other)", "全員まとめて動かす（キャラ同士の位置関係を保つ）"));
                }
                var targets = noChara ? placed : _moveAll ? placed : new List<StudioHost.SceneCharacter> { _selectedChara };
                var who = noChara ? "" : _moveAll ? Tr("everyone", "全員") : _selectedChara.Name;
                switch (moveMode)
                {
                    case 0:
                        GUILayout.Label(Tr($"Moves: {who} (turns around the chosen character)", $"動かすキャラクター: {who}（回転は選んだキャラクターが中心）"));
                        break;
                    case 1:
                        GUILayout.Label(Tr($"Moves: {who} and {StudioHost.GetLinkedObjects(targets).Count} folders/items linked by NodesConstraints", $"動かすもの: {who}と、連結（NodesConstraints）でつながったフォルダ・アイテム {StudioHost.GetLinkedObjects(targets).Count} 個"));
                        break;
                    default:
                        GUILayout.Label(noChara
                            ? Tr("Moves: everything placed from Studio (folders and items; turns around the scene's origin)", "動かすもの: スタジオから置いたもの全部（フォルダ・アイテム。回転はシーンの原点が中心）")
                            : Tr("Moves: everything placed from Studio (characters, folders and items; turns around the chosen character)", "動かすもの: スタジオから置いたもの全部（キャラ・フォルダ・アイテム。回転は選んだキャラクターが中心）"));
                        break;
                }
                void move(Vector3 delta)
                {
                    if (moveMode == 0) StudioHost.Move(targets, delta);
                    else if (moveMode == 1) StudioHost.MoveLinked(targets, delta);
                    else StudioHost.MoveScene(delta);
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label(Tr("Step", "移動量"), GUILayout.Width(50));
                _moveStep = GUILayout.Toolbar(_moveStep, MoveStepNames);
                GUILayout.EndHorizontal();
                // Left/right and forward/back follow the camera, so the buttons move the character as seen on screen
                var cam = Camera.main != null ? Camera.main.transform : null;
                var right = cam != null ? Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized : Vector3.right;
                var forward = cam != null ? Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized : Vector3.forward;
                if (forward == Vector3.zero) forward = Vector3.Cross(right, Vector3.up);
                var step = MoveSteps[_moveStep];
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(Tr("← Left", "← 左"))) move(-right * step);
                if (GUILayout.Button(Tr("Right →", "右 →"))) move(right * step);
                if (GUILayout.Button(Tr("Away", "奥へ"))) move(forward * step);
                if (GUILayout.Button(Tr("Closer", "手前へ"))) move(-forward * step);
                if (GUILayout.Button(Tr("Up", "上へ"))) move(Vector3.up * step);
                if (GUILayout.Button(Tr("Down", "下へ"))) move(Vector3.down * step);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label(Tr("Turn", "回転量"), GUILayout.Width(50));
                _turnStep = GUILayout.Toolbar(_turnStep, TurnStepNames);
                GUILayout.EndHorizontal();
                var pivot = noChara ? StudioHost.SceneOrigin : StudioHost.GetPosition(_selectedChara);
                void turn(float degrees)
                {
                    if (moveMode == 0) StudioHost.Rotate(targets, pivot, degrees);
                    else if (moveMode == 1) StudioHost.RotateLinked(targets, pivot, degrees);
                    else StudioHost.RotateScene(pivot, degrees);
                }
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(Tr("Turn left", "左に回す"))) turn(-TurnSteps[_turnStep]);
                if (GUILayout.Button(Tr("Turn right", "右に回す"))) turn(TurnSteps[_turnStep]);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(Tr("Move to camera target", "カメラの注視点へ移動")))
                {
                    var focus = GetCameraFocus();
                    if (focus.HasValue) move(focus.Value - pivot);
                }
                if (GUILayout.Button(Tr("Back to original position", "元の位置に戻す")))
                {
                    if (moveMode == 0) StudioHost.ResetPose(targets);
                    else if (moveMode == 1) StudioHost.ResetLinked(targets);
                    else StudioHost.ResetScene();
                }
                GUILayout.EndHorizontal();
                GUILayout.Label(Tr("\"Move to camera target\": move the camera so the place you want is in the center of the screen, then press it.", "「カメラの注視点へ移動」: カメラを動かして、置きたい場所を画面の中心に合わせてから押してください。"));
            }
            if (_selectedChara != null)
            {
                GUILayout.Label(Tr($"Clothes ({_selectedChara.DisplayName}, saved per scene):", $"服（{_selectedChara.DisplayName}。シーンごとに保存します）:"));
                DrawOutfitControls(_selectedChara);
                if (!string.IsNullOrEmpty(_cardStatus) && _cardTarget == null) GUILayout.Label(_cardStatus);
            }
            DrawLayoutSlots();
            GUILayout.Label(Tr("Sounds (saved per scene):", "音（シーンごとに保存します）:"));
            DrawSoundChoice();
            GUILayout.EndVertical();
            if (Event.current.type == EventType.Repaint) _charaContentHeight = GUILayoutUtility.GetLastRect().height;
            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        private Vector2 _charaWindowScroll;
        private float _charaContentHeight = 400f;
        private const float CharaWindowWidth = 490f;
        private const float ScrollbarWidth = 18f;

        private string _layoutStatus;

        /// <summary>Save/load slots for where the scene and its characters were moved to (saved per scene).</summary>
        private void DrawLayoutSlots()
        {
            var settings = StudioHost.Settings;
            if (settings == null) return;
            GUILayout.Label(Tr("Saved layouts (position, rotation, visibility; saved per scene):", "配置の保存（移動・回転・表示。シーンごとに保存します）:"));
            int last = settings.LastSavedLayout;
            for (int i = 0; i < settings.Layouts.Length; i++)
            {
                var layout = settings.Layouts[i];
                GUILayout.BeginHorizontal();
                var label = Tr($"Slot {i + 1}: ", $"スロット{i + 1}: ") + (layout != null ? layout.Saved.ToString("M/d HH:mm") + (i == last ? Tr(" (last saved)", "（最後に保存）") : "") : Tr("empty", "空"));
                GUILayout.Label(label, GUILayout.Width(200));
                if (GUILayout.Button(Tr("Save", "保存")))
                    _layoutStatus = StudioHost.SaveLayout(i) ? Tr($"Saved to slot {i + 1}.", $"スロット{i + 1}に保存しました。") : Tr("Could not save.", "保存できませんでした。");
                GUI.enabled = layout != null;
                if (GUILayout.Button(Tr("Load", "ロード")))
                    _layoutStatus = StudioHost.LoadLayout(i) ? Tr($"Loaded slot {i + 1}.", $"スロット{i + 1}を読み込みました。") : Tr("Could not load.", "読み込めませんでした。");
                if (GUILayout.Button(Tr("Delete", "消す")))
                {
                    StudioHost.DeleteLayout(i);
                    _layoutStatus = Tr($"Deleted slot {i + 1}.", $"スロット{i + 1}を消しました。");
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            var auto = GUILayout.Toggle(StudioHost.AutoLayoutOn, Tr("Apply the last saved slot when the scene is loaded", "シーンを読み込んだら、最後に保存したスロットを再現する"));
            if (auto != StudioHost.AutoLayoutOn) StudioHost.SetAutoLayout(auto);
            if (!string.IsNullOrEmpty(_layoutStatus)) GUILayout.Label(_layoutStatus);
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

        /// <summary>The scene that was set when H ended, to set again when the Free H screen comes back.</summary>
        internal static string ReturnScenePath { get; set; }

        /// <summary>The scene whose data is loaded and shown in the window (not necessarily set for H).</summary>
        internal static string PreviewPath { get; private set; }
        private static string _error;
        private static bool _approximate;

        /// <summary>
        /// Reads a scene to show its details. The data of one scene is loaded at a time, so reading another one also
        /// unsets a scene that was set before.
        /// </summary>
        internal static string Preview(FreeHScene freeH, string path)
        {
            if (path == PreviewPath && StudioHost.HasSceneData) return null;
            bool unset = IsSelected;
            Clear();
            string prefix = unset ? FreeHStudioSceneLoader.Tr("The scene that was set has been unset.\n", "セットしていたシーンは解除しました。\n") : "";
            if (!SceneFile.IsScene(path))
                return prefix + FreeHStudioSceneLoader.Tr("This file is not a studio scene (the image has no scene data).", "このファイルはスタジオのシーンではありません（シーンのデータが入っていない画像です）。");
            if (!StudioHost.LoadSceneData(path, out var studioMapNo))
                return prefix + (SceneFile.IsKoikatsuFormat(path)
                    ? FreeHStudioSceneLoader.Tr("Could not load this scene. It is a Koikatsu (KK) scene, and its character data may not be readable.", "このシーンを読み込めませんでした。無印コイカツ（KK）形式のシーンで、キャラクターのデータを読めなかった可能性があります。")
                    : FreeHStudioSceneLoader.Tr("Could not load this scene. See output_log.txt.", "このシーンを読み込めませんでした。output_log.txt を確認してください。"));
            PreviewPath = path;
            if (studioMapNo < 0)
            {
                NoMap = true;
                return prefix;
            }
            Candidates = StudioHost.FindFreeHMaps(freeH.member.map, studioMapNo, out _error, out _approximate);
            if (Candidates.Count == 0) return prefix + _error;
            MapNo = Candidates[0].No;
            return prefix;
        }

        /// <summary>The base map line of the details.</summary>
        internal static string MapDescription
        {
            get
            {
                if (PreviewPath == null) return "";
                if (NoMap) return FreeHStudioSceneLoader.Tr("Base map: none (a scene of items only; the H positions of the map chosen in Free H are used)", "土台のマップ: なし（アイテムだけのシーン。フリーHで選んでいるマップのH位置を使います）");
                if (_error != null) return FreeHStudioSceneLoader.Tr("Base map: ", "土台のマップ: ") + _error;
                var map = Candidates.FirstOrDefault(c => c.No == MapNo);
                return map == null ? "" : FreeHStudioSceneLoader.Tr($"Base map: {map.DisplayName} (No {map.No})", $"土台のマップ: {map.DisplayName}（No {map.No}）") +
                    (_approximate ? FreeHStudioSceneLoader.Tr("\nNote: the studio-only map was matched to the main game's version. Items may be slightly off.", "\n注意: スタジオ専用マップを通常版のマップに対応させました。アイテムの位置が少しずれる可能性があります。") : "");
            }
        }

        /// <summary>Sets the scene shown in the window for H: switches the Free H map to its base map.</summary>
        internal static string Use(FreeHScene freeH)
        {
            if (PreviewPath == null || !StudioHost.HasSceneData) return FreeHStudioSceneLoader.Tr("Choose a scene in the list first.", "先に一覧でシーンを選んでください。");
            if (NoMap) return UseWithoutMap(freeH);
            var param = Candidates.FirstOrDefault(c => c.No == MapNo);
            if (param == null) return _error ?? FreeHStudioSceneLoader.Tr("No Free H map can be used for this scene.", "このシーンに使えるフリーHのマップがありません。");

            RememberMap(freeH);
            ScenePath = PreviewPath;
            // Setting the result map makes the game load this map (and its H points) when H starts
            freeH.member.resultMapInfo.Value = param;
            Hooks.ApplySceneThumbnail(freeH);
            if (StudioHost.Settings != null && StudioHost.Settings.MatchTime) ApplySceneTime(freeH);
            FreeHStudioSceneLoader.Logger.LogInfo($"Selected studio scene {ScenePath} on Free H map {param.No} ({param.MapName}){(_approximate ? " [approximate match]" : "")}" +
                (Candidates.Count > 1 ? $", other Free H maps with the same studio map: {string.Join(", ", Candidates.Where(c => c != param).Select(c => $"{c.No} ({c.MapName})").ToArray())}" : ""));
            return FreeHStudioSceneLoader.Tr($"Set. Base map: {param.DisplayName} (No {param.No}).", $"セットしました。土台のマップ: {param.DisplayName}（No {param.No}）。");
        }

        /// <summary>
        /// A scene built from items only. The map currently chosen in the Free H screen provides the H positions;
        /// the scene is placed around the H start position and the map itself is hidden.
        /// </summary>
        private static string UseWithoutMap(FreeHScene freeH)
        {
            var param = freeH.member.resultMapInfo.Value;
            if (param == null || !param.isFreeH)
                return FreeHStudioSceneLoader.Tr("This scene has no base map. Choose the map for the H positions in the normal map selection first, then click the scene again.", "このシーンには土台のマップがありません。先に通常のマップ選択で、H位置に使うマップを選んでから、もう一度シーンをクリックしてください。");
            RememberMap(freeH);
            ScenePath = PreviewPath;
            MapNo = param.No;
            BaseMapName = param.DisplayName;
            Hooks.ApplySceneThumbnail(freeH);
            if (StudioHost.Settings != null && StudioHost.Settings.MatchTime) ApplySceneTime(freeH);
            FreeHStudioSceneLoader.Logger.LogInfo($"Selected studio scene {ScenePath} (no studio map), H positions from Free H map {param.No} ({param.MapName})");
            return FreeHStudioSceneLoader.Tr($"Set. This scene has no base map, so the H positions of \"{param.DisplayName}\" are used", $"セットしました。このシーンには土台のマップがないため、「{param.DisplayName}」のH位置を使います") +
                   (FreeHStudioSceneLoader.HideBaseMap.Value ? FreeHStudioSceneLoader.Tr(" (the map itself is hidden).", "（マップ自体は表示しません）。") : FreeHStudioSceneLoader.Tr(".", "。")) +
                   FreeHStudioSceneLoader.Tr("\nThe scene is placed so that its female character stands at the H start position. Choosing a map in the normal map selection unsets the scene.", "\nシーンは、シーン内の女の子の位置がHの開始位置に来るように置きます。通常のマップ選択でマップを選ぶと、シーンは解除されます。");
        }

        /// <summary>Stops using the scene for H (its details stay shown) and shows the normal map again.</summary>
        internal static void Unset(FreeHScene freeH)
        {
            ScenePath = null;
            BaseMapName = null;
            ShowNormalMap(freeH);
        }

        /// <summary>
        /// The Free H map chosen before a scene was set (a scene with a studio map switches to its base map), to go
        /// back to when no scene is set any more. Null when the map in use is the player's own choice.
        /// </summary>
        private static MapInfo.Param _mapBefore;

        private static void RememberMap(FreeHScene freeH)
        {
            if (_mapBefore == null) _mapBefore = freeH.member.resultMapInfo.Value;
        }

        /// <summary>The map now in use is the one chosen in the normal map selection.</summary>
        internal static void ForgetMapBefore() => _mapBefore = null;

        /// <summary>Puts back the map chosen before the scene, with its own thumbnail and name.</summary>
        internal static void ShowNormalMap(FreeHScene freeH)
        {
            if (freeH == null || IsSelected) return;
            var current = freeH.member.resultMapInfo.Value;
            var map = _mapBefore ?? current;
            _mapBefore = null;
            if (map == null) return;
            try
            {
                if (map != current)
                    freeH.member.resultMapInfo.Value = map; // the game shows its thumbnail (SetMapSprite)
                else
                    freeH.SetMapSprite(map);
                FreeHStudioSceneLoader.Logger.LogInfo($"Studio scene unset, Free H map {map.No} ({map.MapName})");
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not show the Free H map again: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets the Free H time of day to the studio scene's (the toggles of every H mode, like FreeHScene.SetMapTimeZone).
        /// The secret beach (map 9) has its own night-only times and is left alone. It can still be changed afterwards.
        /// </summary>
        internal static void ApplySceneTime(FreeHScene freeH)
        {
            int time = StudioHost.SceneSunLightType;
            var map = freeH.member.resultMapInfo.Value;
            if (time < 0 || time > 3 || map == null || map.No == 9) return;
            try
            {
                freeH.member.resultTimeZone.Value = time;
                var t = Traverse.Create(freeH);
                for (int mode = 0; mode < 4; mode++)
                    t.Method("SetTimeZone", new[] { typeof(int), typeof(int), typeof(bool) }).GetValue(mode, time, false);
                FreeHStudioSceneLoader.Logger.LogInfo($"Free H time of day set to the studio scene's: {FreeHStudioSceneLoader.TimeNames[time]} ({time})");
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not set the Free H time of day: {ex.Message}");
            }
        }

        /// <summary>Free H maps that load the same studio map as the scene (best first), when there is one.</summary>
        internal static List<MapInfo.Param> Candidates { get; private set; } = new List<MapInfo.Param>();

        /// <summary>Uses another of the <see cref="Candidates"/> (they differ in the H positions) and remembers it for the scene.</summary>
        internal static void ChooseMap(FreeHScene freeH, MapInfo.Param param)
        {
            if (PreviewPath == null || NoMap || param == null) return;
            MapNo = param.No; // first, so that the map change is not taken as leaving the scene
            StudioHost.SetFreeHMap(param.No);
            if (!IsSelected) return;
            freeH.member.resultMapInfo.Value = param;
            Hooks.ApplySceneThumbnail(freeH);
            FreeHStudioSceneLoader.Logger.LogInfo($"Studio scene now on Free H map {param.No} ({param.MapName})");
        }

        internal static void Clear()
        {
            ScenePath = null;
            PreviewPath = null;
            _error = null;
            _approximate = false;
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
