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
        public const string Version = "0.5.9";

        internal static new ManualLogSource Logger;
        internal static FreeHStudioSceneLoader Instance;

        internal static ConfigEntry<bool> LoadFemales;
        internal static ConfigEntry<bool> LoadMales;
        internal static ConfigEntry<bool> LoadLights;
        internal static ConfigEntry<bool> PlayTimeline;
        internal static ConfigEntry<bool> ApplyPoseEditor;
        internal static ConfigEntry<bool> ApplyFKAndIK;
        internal static ConfigEntry<bool> LogPoses;
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
            LogPoses = Config.Bind("Diagnostics", "Log character poses", false,
                "Every 2 seconds during H, write the bone values, IK targets and FK/IK state of the studio scene's characters to the log and to BepInEx/FreeHStudioProbe.log (for comparing with Studio).");
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
                "Show the scene list as thumbnails (3 per row) instead of file names. Also switched with the checkbox next to 解除.");
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
                // Sideloader is another plugin: patch it by name, the scene loader works without this
                var resolver = AccessTools.TypeByName("Sideloader.AutoResolver.UniversalAutoResolver");
                var showError = resolver != null ? AccessTools.Method(resolver, "ShowGUIDError") : null;
                if (showError != null) harmony.Patch(showError, postfix: new HarmonyMethod(typeof(StudioHost), nameof(StudioHost.OnMissingMod)));
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

        private static readonly string SceneRoot = Path.GetFullPath(Path.Combine(Paths.GameRootPath, "UserData/studio/scene"));

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

        internal void OnFreeHSceneStart(FreeHScene freeH)
        {
            _freeH = freeH;
            Selection.Clear();
            Selection.ForgetMapBefore();
            ClearPreview();
            _status = "";
            var path = Selection.ReturnScenePath;
            Selection.ReturnScenePath = null;
            if (path != null && File.Exists(path))
                StartCoroutine(SetSceneAgain(freeH, path));
            else
                Logger.LogMessage($"スタジオシーン読み込み: ボタンか {WindowKey.Value} キーでシーンを選べます");
        }

        /// <summary>
        /// Back from H: sets the scene that was used again. FreeHScene.Start is a coroutine that ends by choosing the
        /// map of the H scene, so wait until it has done that.
        /// </summary>
        private System.Collections.IEnumerator SetSceneAgain(FreeHScene freeH, string path)
        {
            float timeout = Time.realtimeSinceStartup + 60f;
            while (freeH != null && Time.realtimeSinceStartup < timeout &&
                   (freeH.member?.resultMapInfo == null || freeH.member.resultMapInfo.Value == null || !StudioHost.IsReady))
                yield return null;
            yield return new WaitForSeconds(0.5f);
            if (freeH == null || freeH != _freeH || Selection.IsSelected || freeH.member?.resultMapInfo?.Value == null)
                yield break;
            try
            {
                ChooseScene(path);
                Logger.LogMessage(Selection.IsSelected
                    ? $"スタジオシーン「{Path.GetFileNameWithoutExtension(path)}」をもう一度セットしました（{WindowKey.Value} キーで変更できます）"
                    : $"スタジオシーン「{Path.GetFileNameWithoutExtension(path)}」をもう一度セットできませんでした: {_status}");
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
                    var label = _showCharaWindow ? $"スタジオシーンのキャラクターを閉じる ({WindowKey.Value})" : $"スタジオシーンのキャラクター ({WindowKey.Value})";
                    if (DrawButton(label, ButtonInHPosition.Value)) _showCharaWindow = !_showCharaWindow;
                }
                if (_showCharaWindow)
                {
                    _charaWindowRect = KeepOnScreen(GUILayout.Window(GUID.GetHashCode() + 1, _charaWindowRect, DrawCharaWindow, "スタジオシーンのキャラクター", WindowStyle, GUILayout.Width(CharaWindowWidth + ScrollbarWidth)));
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
            _windowRect = KeepOnScreen(GUILayout.Window(GUID.GetHashCode(), _windowRect, DrawWindow, "スタジオシーンをFree-Hのマップにする", WindowStyle, GUILayout.Width(width)));
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
        /// <summary>The list shows coordinate cards (to put on) instead of character cards (to replace with).</summary>
        private bool _cardCoordinates;
        private bool? _cardListCoordinates;
        private const int CardColumns = 4;
        private const float CardListHeight = 520f;

        private bool _focusCardWindow;

        private void OpenCardWindow(StudioHost.SceneCharacter chara, bool coordinates)
        {
            _focusCardWindow = true; // also when it is already open behind another window
            _cardTarget = chara;
            _cardStatus = null;
            _cardCoordinates = coordinates;
            // Coordinate cards are the same list for both sexes
            bool? female = coordinates ? (bool?)null : chara.Female;
            if (_cardListCoordinates != coordinates || _cardListFemale != female)
            {
                _cardFiles = null;
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
                _cardCoordinates ? "読み込むコーデカード" : $"差し替えるキャラカード（{(_cardTarget.Female ? "女性" : "男性")}）", WindowStyle, GUILayout.Width(_cardWindowRect.width)));
            _blockRects.Add(_cardWindowRect);
        }

        /// <summary>Finds the cards a few files per frame (from Update), like the scene list.</summary>
        private void ScanCardFiles()
        {
            if (_cardTarget == null) return;
            if (_cardFiles == null)
            {
                var dir = _cardCoordinates ? StudioHost.CoordinateRoot : Path.Combine(StudioHost.CardRoot, _cardTarget.Female ? "female" : "male");
                _cardPending = Directory.Exists(dir)
                    ? Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTime).ToArray()
                    : new string[0];
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
            if (_cardCoordinates)
                GUILayout.Label($"{(chara.Female ? "♀ " : "♂ ")}{chara.DisplayName}（{StudioHost.OutfitName(chara, StudioHost.OutfitType(chara))}）" +
                                "　コーデカードをクリックすると、今の服の種類に読み込みます（シーンごとに保存）。");
            else
                GUILayout.Label($"{(chara.Female ? "♀ " : "♂ ")}{chara.Name}" + (chara.ReplacedName != null ? $"（今: {chara.ReplacedName}）" : "") +
                                "　カードをクリックすると差し替えます。ポーズと服の状態はシーンのままです（シーンごとに保存）。");
            GUILayout.BeginHorizontal();
            GUILayout.Label("絞り込み:", GUILayout.Width(60));
            _cardFilter = GUILayout.TextField(_cardFilter);
            if (GUILayout.Button("再読込", GUILayout.Width(70))) _cardFiles = null;
            GUILayout.EndHorizontal();
            if (_cardFiles == null || _cardPending != null)
                GUILayout.Label($"カードを確認中… {_cardPendingIndex}/{_cardPending?.Length ?? 0}");
            else
                GUILayout.Label(_cardCoordinates
                    ? $"UserData/coordinate のコーデカード: {_cardFiles.Count} 枚"
                    : $"UserData/chara/{(chara.Female ? "female" : "male")} のカード: {_cardFiles.Count} 枚（コイカツサンシャインのカードのみ）");

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
                                ? StudioHost.SetOutfitCard(chara, f) ?? $"{chara.DisplayName} にコーデ「{StudioHost.OutfitCardName(chara)}」を読み込みました。"
                                : StudioHost.SetReplacement(chara, f) ?? $"{chara.Name} を「{chara.ReplacedName}」に差し替えました。";
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
            if (!_cardCoordinates && chara.ReplacedName != null && GUILayout.Button("元のキャラに戻す", GUILayout.ExpandWidth(false)))
                _cardStatus = StudioHost.SetReplacement(chara, null) ?? $"{chara.Name} に戻しました。";
            if (_cardCoordinates && StudioHost.HasOutfit(chara) && GUILayout.Button("服を戻す", GUILayout.ExpandWidth(false)))
                _cardStatus = StudioHost.ResetOutfit(chara) ?? $"{chara.Name} の服をシーンのままに戻しました。";
            if (GUILayout.Button("閉じる", GUILayout.ExpandWidth(false))) _cardTarget = null;
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

        /// <summary>Keeps a window's title bar on the screen, so it can't be dragged away.</summary>
        private static Rect KeepOnScreen(Rect r)
        {
            r.x = Mathf.Clamp(r.x, 0, Mathf.Max(0, Screen.width - r.width));
            r.y = Mathf.Clamp(r.y, 0, Mathf.Max(0, Screen.height - 40));
            return r;
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
            _blockRects.Add(rect);
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
            GUILayout.Label("絞り込み:", GUILayout.Width(60));
            _filter = GUILayout.TextField(_filter);
            if (GUILayout.Button("再読込", GUILayout.Width(70))) _reloadRequested = true;
            GUILayout.EndHorizontal();

            if (_pending != null)
                GUILayout.Label($"シーンファイルを確認中… {_pendingIndex}/{_pending.Length}（シーン以外のPNGは表示しません）");
            else
                GUILayout.Label($"シーン: {_files.Count} 件　[KK] は無印コイカツ形式（読み込めないものがあります）");

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(CurrentListHeight));
            if (ShowThumbnails.Value)
                DrawThumbnailGrid();
            else
            {
                foreach (var f in _files.ToArray())
                {
                    var rel = ListName(f);
                    if (_filter.Length > 0 && rel.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
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
            if (GUILayout.Button("解除（通常のマップでHをする）", GUILayout.ExpandWidth(false)))
            {
                Selection.Unset(_freeH);
                _status = "解除しました。通常のマップを使います。もう一度シーンをクリックするとセットします。";
            }
            GUI.enabled = true;
            GUILayout.Space(12);
            var thumbs = GUILayout.Toggle(ShowThumbnails.Value, "サムネイルで表示", GUILayout.ExpandWidth(false));
            if (thumbs != ShowThumbnails.Value) ShowThumbnails.Value = thumbs;
            GUILayout.EndHorizontal();
        }

        private const float ListHeight = 300f;
        private const float PreviewHeight = 180f;
        private float CurrentListHeight => ShowThumbnails.Value ? ListHeight + PreviewHeight + 4f : ListHeight;

        private string ListName(string path) =>
            (SceneFile.IsKoikatsuFormat(path) ? "[KK] " : "[KKS] ") + path.Substring(SceneRoot.Length).TrimStart('\\', '/');

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
            var files = _files.Where(f => _filter.Length == 0 || ListName(f).IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
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
                        GUI.Label(new Rect(cell.x + 4, cell.y + 2, cell.width, ThumbLabelHeight), "セット中", _thumbLabelStyle);
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
        }

        /// <summary>Right column: the settings of the chosen scene (saved per scene).</summary>
        private void DrawSceneSettings()
        {
            if (Selection.PreviewPath == null || !StudioHost.HasSceneData)
            {
                GUILayout.Label("左の一覧でシーンをクリックすると、フリーHにセットして、ここにシーンの詳細と設定（土台のマップ、時間帯、キャラクター、音）を出します。設定はシーンごとに保存します。");
                return;
            }
            GUILayout.Label($"シーン: {Path.GetFileNameWithoutExtension(Selection.PreviewPath)}");
            GUILayout.Label(Selection.ScenePath == Selection.PreviewPath ? "状態: セット済み（このシーンでHを始めます）" : "状態: 未セット（左の一覧でもう一度クリックするとセットします）");
            GUILayout.Label(Selection.MapDescription);
            DrawMissingMods();
            if (!Selection.NoMap && Selection.Candidates.Count > 1)
            {
                GUILayout.Label("土台のマップ: このスタジオマップを使うFree-H用マップが複数あります（H位置が違います。シーンごとに保存します）");
                GUILayout.BeginHorizontal();
                foreach (var map in Selection.Candidates)
                    if (GUILayout.Button($"{map.DisplayName}（No {map.No}）", map.No == Selection.MapNo ? GUI.skin.box : GUI.skin.button))
                        Selection.ChooseMap(_freeH, map);
                GUILayout.EndHorizontal();
            }
            if (StudioHost.Settings != null && StudioHost.SceneSunLightType >= 0 && StudioHost.SceneSunLightType < TimeNames.Length)
            {
                bool match = GUILayout.Toggle(StudioHost.Settings.MatchTime, $"時間帯をスタジオのシーンに合わせる（シーン: {TimeNames[StudioHost.SceneSunLightType]}。マップの明るさが変わります）");
                if (match != StudioHost.Settings.MatchTime)
                {
                    StudioHost.SetMatchTime(match);
                    if (match && Selection.IsSelected) Selection.ApplySceneTime(_freeH);
                }
            }
            GUILayout.Space(6);
            DrawCharacterChoice();
            GUILayout.Space(6);
            GUILayout.Label("音（H 中も F7 の窓で切り替えられます）:");
            DrawSoundChoice();
        }

        internal static readonly string[] TimeNames = { "朝", "昼", "夕方", "夜" };

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
                GUILayout.Label("MODは揃っています（シーンを読み込んだときに Sideloader が調べたもの）。", _allModsStyle);
            else
                GUILayout.Label($"足りないMOD: {mods.Count} 種類（使われている回数の多い順）", _missingStyle);
            if (GUILayout.Button("クリップボードにコピー", GUILayout.ExpandWidth(false)))
            {
                GUIUtility.systemCopyBuffer = StudioHost.MissingModsText(Selection.PreviewPath);
                _copyStatus = "コピーしました。";
                _copyStatusFor = Selection.PreviewPath;
            }
            GUILayout.EndHorizontal();
            if (_copyStatus != null) GUILayout.Label(_copyStatus);
            if (mods.Count == 0) return;
            _missingScroll = GUILayout.BeginScrollView(_missingScroll, GUILayout.Height(Mathf.Min(mods.Count, 8) * 22 + 10));
            foreach (var m in mods.OrderByDescending(m => m.Count))
            {
                var label = $"{(string.IsNullOrEmpty(m.Name) ? m.Guid : m.Name)}（{m.Count}）";
                if (!string.IsNullOrEmpty(m.Author)) label += $"  作者: {m.Author}";
                if (!string.IsNullOrEmpty(m.Website)) label += $"  {m.Website}";
                GUILayout.Label(label, _missingStyle);
            }
            GUILayout.EndScrollView();
            GUILayout.Label("（ ）内はシーンで使われている回数です。シェーダーなど、Hで置いたときに初めて分かるものは含みません。");
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
                GUILayout.Label("このシーンにはキャラクターがいません。");
                return;
            }
            GUILayout.Label($"シーンのキャラクター（{charas.Count} 人）: チェックしたキャラクターを、スタジオのポーズのまま置きます。「差し替え」で別のキャラカードにできます。");
            foreach (var female in new[] { true, false })
            {
                var list = charas.Where(c => c.Female == female).ToList();
                if (list.Count == 0) continue;
                bool on = female ? StudioHost.FemalesOn : StudioHost.MalesOn;
                StudioHost.SetSexEnabled(female, GUILayout.Toggle(on, $"{(female ? "女性" : "男性")}を表示（{list.Count} 人）"));
                GUI.enabled = female ? StudioHost.FemalesOn : StudioHost.MalesOn;
                foreach (var chara in list)
                {
                    // A frame around each character (light red while its clothes are open)
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(14);
                    GUILayout.BeginVertical(_outfitOpen == chara ? ChosenFrameStyle : FrameStyle);
                    GUILayout.BeginHorizontal();
                    StudioHost.SetCharacterEnabled(chara, GUILayout.Toggle(chara.Enabled, (chara.Female ? "♀ " : "♂ ") + chara.Name + (chara.ReplacedName != null ? $" → {chara.ReplacedName}" : "")));
                    DrawReplaceButtons(chara);
                    bool open = _outfitOpen == chara;
                    if (GUILayout.Button(open ? "服 ▲" : "服 ▼", GUILayout.ExpandWidth(false))) _outfitOpen = open ? null : chara;
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
            GUILayout.Label("新しいシーンの初期設定:", GUILayout.ExpandWidth(false));
            var females = GUILayout.Toggle(LoadFemales.Value, "女性を表示", GUILayout.ExpandWidth(false));
            if (females != LoadFemales.Value) LoadFemales.Value = females;
            var males = GUILayout.Toggle(LoadMales.Value, "男性を表示", GUILayout.ExpandWidth(false));
            if (males != LoadMales.Value) LoadMales.Value = males;
            GUILayout.EndHorizontal();
            GUILayout.Label("（設定を一度も変えていないシーンに使います）");
        }

        /// <summary>A × at the top right of a window; true when it was pressed.</summary>
        private static bool CloseButton(Rect window)
        {
            return GUI.Button(new Rect(window.width - 26, 3, 22, 18), "×");
        }

        /// <summary>The clothes of one scene character: the kind of clothes and a coordinate card (saved per scene).</summary>
        private void DrawOutfitControls(StudioHost.SceneCharacter chara)
        {
            int type = StudioHost.OutfitType(chara);
            int count = StudioHost.OutfitCount(chara);
            var names = new string[count];
            for (int i = 0; i < count; i++) names[i] = StudioHost.OutfitName(chara, i);
            GUILayout.BeginHorizontal();
            GUILayout.Label("服の種類", GUILayout.Width(56));
            // Four per row: the game's four, then the ones MoreOutfits added
            int newType = GUILayout.SelectionGrid(Mathf.Clamp(type, 0, count - 1), names, 4);
            if (newType != type) StudioHost.SetOutfitType(chara, newType);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("服の状態", GUILayout.Width(56));
            int state = StudioHost.ClothesState(chara);
            int newState = GUILayout.Toolbar(state + 1, StudioHost.ClothesStateNames) - 1;
            if (newState != state) StudioHost.SetClothesState(chara, newState);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            var card = StudioHost.OutfitCardName(chara);
            GUILayout.Label(card != null ? $"コーデ: {card}" : "コーデ: キャラのまま", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("コーデを読み込む", GUILayout.ExpandWidth(false))) OpenCardWindow(chara, true);
            GUI.enabled = GUI.enabled && StudioHost.HasOutfit(chara);
            if (GUILayout.Button("服を戻す", GUILayout.ExpandWidth(false)))
                _cardStatus = StudioHost.ResetOutfit(chara) ?? $"{chara.Name} の服をシーンのままに戻しました。";
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        /// <summary>「差し替え」 (opens the card list) and 「戻す」 for one scene character.</summary>
        private void DrawReplaceButtons(StudioHost.SceneCharacter chara)
        {
            if (GUILayout.Button("差し替え", GUILayout.ExpandWidth(false))) OpenCardWindow(chara, false);
            if (chara.ReplacedName != null && GUILayout.Button("戻す", GUILayout.ExpandWidth(false)))
                _cardStatus = StudioHost.SetReplacement(chara, null) ?? $"{chara.Name} に戻しました。";
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

            if (CloseButton(_charaWindowRect)) _showCharaWindow = false;
            // Taller than the screen: the content scrolls instead of the window running off the screen. The view is as
            // tall as the content (measured on the last repaint), the width as before plus the scrollbar.
            float maxHeight = Mathf.Max(200f, Screen.height - 60f);
            _charaWindowScroll = GUILayout.BeginScrollView(_charaWindowScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar,
                GUILayout.Height(Mathf.Min(_charaContentHeight + 4f, maxHeight)));
            GUILayout.BeginVertical(GUILayout.Width(CharaWindowWidth - WindowStyle.padding.horizontal));
            GUILayout.Label($"表示のオンオフと、位置の調整ができます（{WindowKey.Value} キーで閉じる）");
            _charaScroll = GUILayout.BeginScrollView(_charaScroll, GUILayout.Height(Mathf.Min(placed.Count, 6) * 26 + 8));
            foreach (var chara in placed)
            {
                GUILayout.BeginHorizontal();
                var visible = StudioHost.IsVisible(chara);
                var newVisible = GUILayout.Toggle(visible, "表示", GUILayout.Width(56));
                if (newVisible != visible) StudioHost.SetVisible(chara, newVisible);
                var label = (chara.Female ? "♀ " : "♂ ") + chara.DisplayName;
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
                    GUILayout.Label("このシーンにはキャラクターがいません。シーン全体を動かせます。");
                else
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("動かす対象", GUILayout.Width(70));
                    _moveMode = moveMode = GUILayout.Toolbar(_moveMode, MoveModeNames);
                    GUILayout.EndHorizontal();
                    if (moveMode != 2) _moveAll = GUILayout.Toggle(_moveAll, "全員まとめて動かす（キャラ同士の位置関係を保つ）");
                }
                var targets = noChara ? placed : _moveAll ? placed : new List<StudioHost.SceneCharacter> { _selectedChara };
                var who = noChara ? "" : _moveAll ? "全員" : _selectedChara.Name;
                switch (moveMode)
                {
                    case 0:
                        GUILayout.Label($"動かすキャラクター: {who}（回転は選んだキャラクターが中心）");
                        break;
                    case 1:
                        GUILayout.Label($"動かすもの: {who}と、連結（NodesConstraints）でつながったフォルダ・アイテム {StudioHost.GetLinkedObjects(targets).Count} 個");
                        break;
                    default:
                        GUILayout.Label(noChara
                            ? "動かすもの: スタジオから置いたもの全部（フォルダ・アイテム。回転はシーンの原点が中心）"
                            : "動かすもの: スタジオから置いたもの全部（キャラ・フォルダ・アイテム。回転は選んだキャラクターが中心）");
                        break;
                }
                void move(Vector3 delta)
                {
                    if (moveMode == 0) StudioHost.Move(targets, delta);
                    else if (moveMode == 1) StudioHost.MoveLinked(targets, delta);
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
                var pivot = noChara ? StudioHost.SceneOrigin : StudioHost.GetPosition(_selectedChara);
                void turn(float degrees)
                {
                    if (moveMode == 0) StudioHost.Rotate(targets, pivot, degrees);
                    else if (moveMode == 1) StudioHost.RotateLinked(targets, pivot, degrees);
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
                    if (moveMode == 0) StudioHost.ResetPose(targets);
                    else if (moveMode == 1) StudioHost.ResetLinked(targets);
                    else StudioHost.ResetScene();
                }
                GUILayout.EndHorizontal();
                GUILayout.Label("「カメラの注視点へ移動」: カメラを動かして、置きたい場所を画面の中心に合わせてから押してください。");
            }
            if (_selectedChara != null)
            {
                GUILayout.Label($"服（{_selectedChara.DisplayName}。シーンごとに保存します）:");
                DrawOutfitControls(_selectedChara);
                if (!string.IsNullOrEmpty(_cardStatus) && _cardTarget == null) GUILayout.Label(_cardStatus);
            }
            DrawLayoutSlots();
            GUILayout.Label("音（シーンごとに保存します）:");
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
            GUILayout.Label("配置の保存（移動・回転・表示。シーンごとに保存します）:");
            int last = settings.LastSavedLayout;
            for (int i = 0; i < settings.Layouts.Length; i++)
            {
                var layout = settings.Layouts[i];
                GUILayout.BeginHorizontal();
                var label = $"スロット{i + 1}: " + (layout != null ? layout.Saved.ToString("M/d HH:mm") + (i == last ? "（最後に保存）" : "") : "空");
                GUILayout.Label(label, GUILayout.Width(200));
                if (GUILayout.Button("保存"))
                    _layoutStatus = StudioHost.SaveLayout(i) ? $"スロット{i + 1}に保存しました。" : "保存できませんでした。";
                GUI.enabled = layout != null;
                if (GUILayout.Button("ロード"))
                    _layoutStatus = StudioHost.LoadLayout(i) ? $"スロット{i + 1}を読み込みました。" : "読み込めませんでした。";
                if (GUILayout.Button("消す"))
                {
                    StudioHost.DeleteLayout(i);
                    _layoutStatus = $"スロット{i + 1}を消しました。";
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            var auto = GUILayout.Toggle(StudioHost.AutoLayoutOn, "シーンを読み込んだら、最後に保存したスロットを再現する");
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
            string prefix = unset ? "セットしていたシーンは解除しました。\n" : "";
            if (!SceneFile.IsScene(path))
                return prefix + "このファイルはスタジオのシーンではありません（シーンのデータが入っていない画像です）。";
            if (!StudioHost.LoadSceneData(path, out var studioMapNo))
                return prefix + (SceneFile.IsKoikatsuFormat(path)
                    ? "このシーンを読み込めませんでした。無印コイカツ（KK）形式のシーンで、キャラクターのデータを読めなかった可能性があります。"
                    : "このシーンを読み込めませんでした。output_log.txt を確認してください。");
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
                if (NoMap) return "土台のマップ: なし（アイテムだけのシーン。フリーHで選んでいるマップのH位置を使います）";
                if (_error != null) return "土台のマップ: " + _error;
                var map = Candidates.FirstOrDefault(c => c.No == MapNo);
                return map == null ? "" : $"土台のマップ: {map.DisplayName}（No {map.No}）" +
                    (_approximate ? "\n注意: スタジオ専用マップを通常版のマップに対応させました。アイテムの位置が少しずれる可能性があります。" : "");
            }
        }

        /// <summary>Sets the scene shown in the window for H: switches the Free H map to its base map.</summary>
        internal static string Use(FreeHScene freeH)
        {
            if (PreviewPath == null || !StudioHost.HasSceneData) return "先に一覧でシーンを選んでください。";
            if (NoMap) return UseWithoutMap(freeH);
            var param = Candidates.FirstOrDefault(c => c.No == MapNo);
            if (param == null) return _error ?? "このシーンに使えるフリーHのマップがありません。";

            RememberMap(freeH);
            ScenePath = PreviewPath;
            // Setting the result map makes the game load this map (and its H points) when H starts
            freeH.member.resultMapInfo.Value = param;
            Hooks.ApplySceneThumbnail(freeH);
            if (StudioHost.Settings != null && StudioHost.Settings.MatchTime) ApplySceneTime(freeH);
            FreeHStudioSceneLoader.Logger.LogInfo($"Selected studio scene {ScenePath} on Free H map {param.No} ({param.MapName}){(_approximate ? " [approximate match]" : "")}" +
                (Candidates.Count > 1 ? $", other Free H maps with the same studio map: {string.Join(", ", Candidates.Where(c => c != param).Select(c => $"{c.No} ({c.MapName})").ToArray())}" : ""));
            return $"セットしました。土台のマップ: {param.DisplayName}（No {param.No}）。";
        }

        /// <summary>
        /// A scene built from items only. The map currently chosen in the Free H screen provides the H positions;
        /// the scene is placed around the H start position and the map itself is hidden.
        /// </summary>
        private static string UseWithoutMap(FreeHScene freeH)
        {
            var param = freeH.member.resultMapInfo.Value;
            if (param == null || !param.isFreeH)
                return "このシーンには土台のマップがありません。先に通常のマップ選択で、H位置に使うマップを選んでから、もう一度シーンをクリックしてください。";
            RememberMap(freeH);
            ScenePath = PreviewPath;
            MapNo = param.No;
            BaseMapName = param.DisplayName;
            Hooks.ApplySceneThumbnail(freeH);
            if (StudioHost.Settings != null && StudioHost.Settings.MatchTime) ApplySceneTime(freeH);
            FreeHStudioSceneLoader.Logger.LogInfo($"Selected studio scene {ScenePath} (no studio map), H positions from Free H map {param.No} ({param.MapName})");
            return $"セットしました。このシーンには土台のマップがないため、「{param.DisplayName}」のH位置を使います" +
                   (FreeHStudioSceneLoader.HideBaseMap.Value ? "（マップ自体は表示しません）。" : "。") +
                   "\nシーンは、シーン内の女の子の位置がHの開始位置に来るように置きます。通常のマップ選択で別のマップを選ぶと、H位置だけ変わります。";
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

        internal static void ChangeBaseMap(MapInfo.Param param)
        {
            ForgetMapBefore();
            MapNo = param.No;
            BaseMapName = param.DisplayName;
            FreeHStudioSceneLoader.Logger.LogInfo($"H positions for the studio scene now from Free H map {param.No} ({param.MapName})");
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
