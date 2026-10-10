// Koikatsu Sunshine port of FreeHStudioSceneLoader.
// Original: IllusionMods/KK_Plugins - FreeHStudioSceneLoader.KK by DeathWeasel1337 (GPL-3.0).
// Modified for Koikatsu Sunshine, 2026. Licensed under GPL-3.0.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Studio;
using UnityEngine;

namespace KKS_FreeHStudioSceneLoader
{
    /// <summary>
    /// Runs the minimum of Studio inside the main game: object lists (Info), guide objects and tree nodes
    /// (both stubbed by <see cref="Hooks"/>), and the scene data. Only items, folders and lights are placed;
    /// the map itself is loaded by the game as a normal Free H map so that its H points work.
    /// </summary>
    internal static class StudioHost
    {
        private static string Tr(string en, string ja) => FreeHStudioSceneLoader.Tr(en, ja);

        private static GameObject _host;
        private static GameObject _overlayRoot;
        private static SceneInfo _sceneInfo;

        /// <summary>Scene characters waiting for <see cref="LoadCharacters"/>, with the folder or item they are placed under.</summary>
        private static readonly List<KeyValuePair<OICharInfo, ObjectInfo>> _pendingCharacters = new List<KeyValuePair<OICharInfo, ObjectInfo>>();
        private static readonly HashSet<ChaControl> _characters = new HashSet<ChaControl>();

        /// <summary>True while a scene character is created; its body loads Studio's bone prefabs (see Hooks).</summary>
        internal static bool IsLoadingCharacter { get; private set; }

        internal static bool IsSceneCharacter(ChaControl chara) => IsLoadingCharacter || (chara != null && _characters.Contains(chara));

        /// <summary>A character of the selected scene: chosen in the selection window, adjusted in H.</summary>
        internal class SceneCharacter
        {
            public OICharInfo Info;
            public string Name;
            public bool Female;
            /// <summary>Chosen to be loaded (the female/male switches apply on top of this).</summary>
            public bool Enabled = true;
            public OCIChar Oci;
            /// <summary>The scene's own card of a placed character, saved before it was first replaced (to go back to).</summary>
            public string OriginalCardFile;
            /// <summary>The state of each clothing part as the scene had it when the character was placed.</summary>
            public byte[] OriginalClothesState;
            public Vector3 OriginalPos;
            public Vector3 OriginalRot;
            /// <summary>PVCopy's switches in the last frame, and its hand/foot bones with their positions before it ever ran.</summary>
            public bool[] PvCopyLast;
            public Transform[] PvBones;
            public Vector3[] PvBoneDefaults;

            public bool Placed => Oci != null && Oci.charInfo != null;
            /// <summary>The name of the card it is replaced with, or null.</summary>
            public string ReplacedName => Settings != null && Settings.Replacements.TryGetValue(Info.dicKey, out var r) ? (string.IsNullOrEmpty(r.Name) ? System.IO.Path.GetFileNameWithoutExtension(r.Card) : r.Name) : null;
            public string DisplayName => ReplacedName ?? Name;
        }

        private static readonly List<SceneCharacter> _sceneCharacters = new List<SceneCharacter>();

        /// <summary>The characters of the selected scene, in the order of the scene's tree.</summary>
        internal static IList<SceneCharacter> SceneCharacters => _sceneCharacters;

        internal static bool HasPlacedCharacters => _sceneCharacters.Any(c => c.Placed);
        /// <summary>The scene is placed in the current H scene (with or without characters).</summary>
        internal static bool HasPlacedScene => _overlayRoot != null;
        /// <summary>Where the scene's origin is now (the pivot to turn a scene without characters around).</summary>
        internal static Vector3 SceneOrigin => _overlayRoot != null ? _overlayRoot.transform.position : Vector3.zero;
        internal static Renderer[] SceneRenderers => _overlayRoot != null ? _overlayRoot.GetComponentsInChildren<Renderer>(true) : new Renderer[0];

        private static bool IsCharacterEnabled(OICharInfo info)
        {
            var entry = _sceneCharacters.FirstOrDefault(c => c.Info == info);
            if (entry != null && !entry.Enabled) return false;
            return info.sex == 1 ? FemalesOn : MalesOn;
        }

        private static void CollectCharacters(IEnumerable<ObjectInfo> infos)
        {
            foreach (var info in infos)
            {
                switch (info)
                {
                    case OICharInfo chara:
                        _sceneCharacters.Add(new SceneCharacter
                        {
                            Info = chara,
                            Name = string.IsNullOrEmpty(chara.charFile?.parameter?.fullname) ? Tr("(no name)", "(名前なし)") : chara.charFile.parameter.fullname,
                            Female = chara.sex == 1,
                        });
                        foreach (var list in chara.child.Values) CollectCharacters(list);
                        break;
                    case OIFolderInfo folder:
                        CollectCharacters(folder.child);
                        break;
                    case OIItemInfo item:
                        CollectCharacters(item.child);
                        break;
                }
            }
        }

        /// <summary>True once the Studio objects were created. Hooks only change behaviour after that.</summary>
        internal static bool Created => _host != null;
        internal static bool IsReady => Created && Singleton<Info>.IsInstance() && Singleton<Info>.Instance.isLoadList;
        internal static bool HasSceneData => _sceneInfo != null;

        /// <summary>True while <see cref="LoadSceneData"/> reads a scene file (Studio-style card loading).</summary>
        internal static bool IsLoadingScene { get; private set; }

        /// <summary>Parent of the UI-less tree nodes, so that cleaning them up never touches other host objects.</summary>
        internal static Transform TreeNodeRoot { get; private set; }

        /// <summary>Set when creating the host or loading the studio lists failed; shown in the window.</summary>
        internal static string Error { get; private set; }

        internal static void EnsureCreated()
        {
            if (Created || Error != null) return;

            try
            {
                _host = new GameObject("FreeHStudioSceneLoader_Studio");
                UnityEngine.Object.DontDestroyOnLoad(_host);
                _host.SetActive(false);

                // Awake of these is patched in Hooks to skip UI setup
                var studio = _host.AddComponent<Studio.Studio>();
                _host.AddComponent<Info>();
                _host.AddComponent<GuideObjectManager>();
                // SceneInfo's constructor hooks caMap to MapCtrl.Reflect (patched to do nothing here)
                _host.AddComponent<MapCtrl>();
                studio.m_TreeNodeCtrl = _host.AddComponent<TreeNodeCtrl>();

                // SceneInfo.Init reads Studio.cameraCtrl.ExportResetData(). The camera control is kept on an
                // inactive object so that none of its Awake/Start/Update (which take over the camera) ever run.
                var cameraHolder = new GameObject("CameraControl (inactive)");
                cameraHolder.SetActive(false);
                cameraHolder.transform.SetParent(_host.transform, false);
                studio.m_CameraCtrl = cameraHolder.AddComponent<Studio.CameraControl>();
                // OCIItem.SetPatternTex looks pattern IDs up in this list; its Start needs the Studio UI, so it stays inactive too
                studio._patternSelectListCtrl = cameraHolder.AddComponent<PatternSelectListCtrl>();
                FillPatternList(studio._patternSelectListCtrl);

                var treeNodeRoot = new GameObject("TreeNodes");
                treeNodeRoot.transform.SetParent(_host.transform, false);
                TreeNodeRoot = treeNodeRoot.transform;

                _host.SetActive(true);

                studio.sceneInfo = new SceneInfo();
                studio.dicInfo.Clear();
                studio.dicObjectCtrl.Clear();
                studio.dicChangeAmount.Clear();
            }
            catch (Exception ex)
            {
                Fail("Failed to create the studio host", ex);
                return;
            }

            FreeHStudioSceneLoader.Instance.StartCoroutine(LoadListsSafe(Singleton<Info>.Instance.LoadExcelDataCoroutine()));
            FreeHStudioSceneLoader.Logger.LogInfo("Studio host created, loading studio lists");
        }

        /// <summary>Runs the list loading coroutine and catches exceptions thrown inside it.</summary>
        private static IEnumerator LoadListsSafe(IEnumerator inner)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(inner);
            while (stack.Count > 0)
            {
                object current;
                try
                {
                    if (!stack.Peek().MoveNext())
                    {
                        stack.Pop();
                        continue;
                    }
                    current = stack.Peek().Current;
                }
                catch (Exception ex)
                {
                    Fail("Failed to load the studio lists", ex);
                    yield break;
                }
                if (current is IEnumerator nested) stack.Push(nested);
                else yield return current;
            }
            FreeHStudioSceneLoader.Logger.LogInfo("Studio lists loaded");
        }

        /// <summary>Same entries as StudioScene.CreatePatternList, without creating the UI.</summary>
        private static void FillPatternList(PatternSelectListCtrl list)
        {
            var infos = Manager.Character.chaListCtrl.GetCategoryInfo(ChaListDefine.CategoryNo.mt_pattern).Values.ToList();
            foreach (var info in infos)
                list.AddList(info.Id, info.Name, info.GetInfo(ChaListDefine.KeyType.ThumbAB), info.GetInfo(ChaListDefine.KeyType.ThumbTex));
            // AddOutside would create the folder when it is missing
            var thumbDir = UserData.Path + "pattern_thumb";
            if (infos.Count > 0 && System.IO.Directory.Exists(thumbDir))
            {
                int index = infos.Max(i => i.Id) + 1;
                foreach (var file in System.IO.Directory.GetFiles(thumbDir, "*.png"))
                {
                    var name = System.IO.Path.GetFileName(file);
                    list.AddList(index++, System.IO.Path.GetFileNameWithoutExtension(name), "", name);
                }
            }
        }

        private static void Fail(string what, Exception ex)
        {
            Error = $"{what}: {ex.GetBaseException().Message}";
            FreeHStudioSceneLoader.Logger.LogError($"{what}: {ex}");
            if (_host != null) UnityEngine.Object.Destroy(_host);
            _host = null;
        }

        /// <summary>
        /// Reads the scene file. ExtendedSave/Sideloader resolve mod item and map IDs during SceneInfo.Load,
        /// using Studio.Instance.sceneInfo, so the scene info has to be assigned to the Studio instance first.
        /// </summary>
        internal static bool LoadSceneData(string path, out int studioMapNo)
        {
            studioMapNo = -1;
            EnsureCreated();
            DiscardSceneData();
            MissingMods.Clear();
            var sceneInfo = new SceneInfo();
            Singleton<Studio.Studio>.Instance.sceneInfo = sceneInfo;
            IsLoadingScene = true;
            try
            {
                // Call the (path, out version) overload directly: ExtendedSave/Sideloader hook this one, while
                // studio-only plugins (e.g. KKS_AssetImport) hook Load(path) and fail outside Studio.
                if (!sceneInfo.Load(path, out _))
                    return false;
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogError($"Failed to load scene {path}: {ex}");
                return false;
            }
            finally
            {
                IsLoadingScene = false;
            }
            _sceneInfo = sceneInfo;
            CollectCharacters(sceneInfo.dicObject.Values.Where(i => i != null));
            ScenePath = path;
            Settings = SceneSettingsStore.Get(path);
            foreach (var chara in _sceneCharacters) chara.Enabled = !Settings.HiddenCharacters.Contains(chara.Info.dicKey);
            CollectObjects(sceneInfo.dicObject.Values, null);
            ReadPluginData();
            _backgroundPng = ReadBackgroundImage(sceneInfo);
            studioMapNo = sceneInfo.map;
            return true;
        }

        /// <summary>
        /// The studio background image (a picture drawn behind everything, see Studio.BackgroundCtrl). StudioImageEmbed
        /// keeps it in the scene's extended data; without it the scene refers to a file under UserData/bg.
        /// The extended data of a scene is only available right after reading it.
        /// </summary>
        private static byte[] ReadBackgroundImage(SceneInfo sceneInfo)
        {
            try
            {
                var embed = ExtensibleSaveFormat.ExtendedSave.GetSceneExtendedDataById(ImageEmbedGUID);
                if (embed?.data != null && embed.data.TryGetValue("BGData", out var bg) && bg is byte[] bytes && bytes.Length > 0)
                    return bytes;
                if (!string.IsNullOrEmpty(sceneInfo.background))
                {
                    var path = System.IO.Path.Combine(BepInEx.Paths.GameRootPath, sceneInfo.background);
                    if (System.IO.File.Exists(path)) return System.IO.File.ReadAllBytes(path);
                    FreeHStudioSceneLoader.Logger.LogInfo($"Background image of the scene not found: {sceneInfo.background}");
                }
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not read the background image of the scene: {ex.Message}");
            }
            return null;
        }

        private const string ImageEmbedGUID = "com.deathweasel.bepinex.studioimageembed";

        #region Studio plugin animation (Timeline, KKPE, NodesConstraints)

        private static string _timelineXml, _constraintsXml, _kkpeSceneXml;

        /// <summary>Every object of the scene with the object it is placed under, before anything is left out.</summary>
        private static readonly List<KeyValuePair<ObjectInfo, ObjectInfo>> _allObjects = new List<KeyValuePair<ObjectInfo, ObjectInfo>>();

        private static void CollectObjects(IEnumerable<ObjectInfo> infos, ObjectInfo parent)
        {
            foreach (var info in infos)
            {
                if (info == null) continue;
                _allObjects.Add(new KeyValuePair<ObjectInfo, ObjectInfo>(info, parent));
                switch (info)
                {
                    case OIFolderInfo folder: CollectObjects(folder.child, info); break;
                    case OIItemInfo item: CollectObjects(item.child, info); break;
                    case OIRouteInfo route: CollectObjects(route.child, info); break;
                    case OICharInfo chara:
                        foreach (var list in chara.child.Values) CollectObjects(list, info);
                        break;
                }
            }
        }

        private static string Describe(ObjectInfo info)
        {
            string what;
            switch (info)
            {
                case OICharInfo c: what = $"character \"{c.charFile?.parameter?.fullname?.Trim()}\" (sex {c.sex})"; break;
                case OIFolderInfo f: what = $"folder \"{f.name}\""; break;
                case OIItemInfo i: what = $"item {i.group}/{i.category}/{i.no}"; break;
                default: what = $"kind {info.kind}"; break;
            }
            return $"dicKey {info.dicKey}, {what}";
        }

        /// <summary>The plugins' scene data is only available right after the scene was read.</summary>
        private static void ReadPluginData()
        {
            _timelineXml = ReadSceneString("timeline", "sceneInfo");
            _constraintsXml = ReadSceneString("nodesConstraints", "constraints");
            _kkpeSceneXml = ReadSceneString("kkpe", "sceneInfo");
        }

        private static string ReadSceneString(string id, string key)
        {
            try
            {
                var data = ExtensibleSaveFormat.ExtendedSave.GetSceneExtendedDataById(id);
                if (data?.data != null && data.data.TryGetValue(key, out var value)) return value as string;
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not read the {id} data of the scene: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Starts the scene's Timeline/KKPE/NodesConstraints animation. Call after all objects and characters were
        /// placed. The plugins refer to objects by their position in Studio's object list (sorted by dicKey).
        /// </summary>
        internal static void StartAnimation()
        {
            if (_sceneInfo == null || _overlayRoot == null) return;
            if (_timelineXml == null && _constraintsXml == null && _kkpeSceneXml == null && !_sceneCharacters.Any(c => c.Placed)) return;
            var placed = Singleton<Studio.Studio>.Instance.dicObjectCtrl;
            var objects = new List<ObjectCtrlInfo>();
            var isItem = new List<bool>();
            foreach (var entry in _allObjects.OrderBy(e => e.Key.dicKey))
            {
                var info = entry.Key;
                // Route points are not objects of their own in Studio either
                if (info.kind == 6) continue;
                bool loaded = placed.TryGetValue(info.dicKey, out var oci);
                // An item that was tried here but could not be loaded (missing mod) is missing in Studio too.
                // Everything else that is not placed here (cameras, texts, characters turned off) exists there.
                if (!loaded && info.kind == 1 && (entry.Value == null || placed.ContainsKey(entry.Value.dicKey)))
                {
                    FreeHStudioSceneLoader.Logger.LogDebug($"Studio object (not counted, failed to load): {Describe(info)}");
                    continue;
                }
                FreeHStudioSceneLoader.Logger.LogDebug($"Studio object {objects.Count}: {Describe(info)}{(loaded ? "" : " (not placed)")}");
                objects.Add(loaded ? oci : null);
                isItem.Add(info.kind == 1);
            }
            SceneAnimator.Create(_overlayRoot.transform, objects, isItem, _timelineXml, _constraintsXml, _kkpeSceneXml);
        }

        #endregion
        private static byte[] _backgroundPng;
        private static GameObject _backgroundCanvas;
        private static Texture2D _backgroundTexture;

        /// <summary>
        /// Shows the studio background image behind everything, filling the view like Studio does
        /// (a camera-space canvas at the far end of the view, so the scene and characters stay in front of it).
        /// </summary>
        internal static void ShowBackground(Camera camera)
        {
            if (_backgroundPng == null || camera == null || !FreeHStudioSceneLoader.ShowBackgroundImage.Value) return;
            int w = 0, h = 0;
            _backgroundTexture = PngAssist.ChangeTextureFromPngByte(_backgroundPng, ref w, ref h);
            if (_backgroundTexture == null) return;

            _backgroundCanvas = new GameObject("FreeHStudioSceneLoader_Background");
            _backgroundCanvas.layer = FirstRenderedLayer(camera);
            var canvas = _backgroundCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = camera.farClipPlane * 0.98f;
            canvas.sortingOrder = -1000;

            var image = new GameObject("Image");
            image.layer = _backgroundCanvas.layer;
            image.transform.SetParent(_backgroundCanvas.transform, false);
            var raw = image.AddComponent<UnityEngine.UI.RawImage>();
            raw.texture = _backgroundTexture;
            raw.raycastTarget = false;
            var rt = raw.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            FreeHStudioSceneLoader.Logger.LogInfo($"Studio background image shown ({w}x{h})");
        }

        private static int FirstRenderedLayer(Camera camera)
        {
            for (int i = 0; i < 32; i++)
                if ((camera.cullingMask & (1 << i)) != 0) return i;
            return 0;
        }

        private static void HideBackground()
        {
            if (_backgroundCanvas != null) UnityEngine.Object.Destroy(_backgroundCanvas);
            if (_backgroundTexture != null) UnityEngine.Object.Destroy(_backgroundTexture);
            _backgroundCanvas = null;
            _backgroundTexture = null;
        }

        internal static void DiscardSceneData()
        {
            _sceneInfo = null;
            ScenePath = null;
            Settings = null;
            _backgroundPng = null;
            _sceneCharacters.Clear();
            _allObjects.Clear();
            _timelineXml = _constraintsXml = _kkpeSceneXml = null;
            if (!Created) return;
            // Reading a scene registers each object's ChangeAmount by dicKey, and Studio.AddChangeAmount keeps the first
            // one for a key. Without clearing, reading the scene again (choosing it twice) left the old ones there: the
            // guide objects took those, and the saved IK targets (elbows, knees, feet...) stayed at the character's origin.
            // Nothing is placed while a scene is chosen (UnloadOverlay clears them when H ends).
            if (_overlayRoot == null) Singleton<Studio.Studio>.Instance.dicChangeAmount.Clear();
            try { Singleton<Studio.Studio>.Instance.sceneInfo = new SceneInfo(); }
            catch (Exception ex) { FreeHStudioSceneLoader.Logger.LogWarning($"Could not reset the studio scene info: {ex.Message}"); }
        }

        /// <summary>
        /// Studio and Free H use different map lists. Match them by the scene asset (bundle + scene name).
        /// </summary>
        internal static MapInfo.Param FindFreeHMap(BaseMap baseMap, int studioMapNo, out string reason) =>
            FindFreeHMap(baseMap, studioMapNo, out reason, out _);

        internal static MapInfo.Param FindFreeHMap(BaseMap baseMap, int studioMapNo, out string reason, out bool approximate) =>
            FindFreeHMaps(baseMap, studioMapNo, out reason, out approximate).FirstOrDefault();

        /// <summary>
        /// The Free H maps that load the scene's studio map, best first. Several mod maps can use the same scene with
        /// different H positions (e.g. AnotherH's ラブホテル 960 and ピンクルーム 999 both load studio/map/lovehotel.unity3d):
        /// the one chosen for this scene before comes first, then the one named like the studio map, then by number.
        /// </summary>
        internal static List<MapInfo.Param> FindFreeHMaps(BaseMap baseMap, int studioMapNo, out string reason, out bool approximate)
        {
            reason = null;
            approximate = false;
            var none = new List<MapInfo.Param>();
            if (studioMapNo < 0)
            {
                reason = Tr("This scene has no studio map.", "このシーンにはスタジオのマップがありません。");
                return none;
            }
            if (!Singleton<Info>.Instance.dicMapLoadInfo.TryGetValue(studioMapNo, out var mapLoadInfo))
            {
                reason = Tr($"The studio map (No {studioMapNo}) is not installed.", $"スタジオのマップ（No {studioMapNo}）がインストールされていません。");
                return none;
            }
            var matches = baseMap.infoDic.Values.Where(p =>
                string.Equals(p.AssetBundleName, mapLoadInfo.bundlePath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.AssetName, mapLoadInfo.fileName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
            {
                // Many KKS island maps use a separate "xxx_studio" scene in Studio; fall back to the scene name
                // without that suffix. The two versions may not be laid out identically.
                var key = StripStudioSuffix(mapLoadInfo.fileName);
                matches = baseMap.infoDic.Values.Where(p => p.isFreeH &&
                    string.Equals(StripStudioSuffix(p.AssetName), key, StringComparison.OrdinalIgnoreCase)).ToList();
                approximate = matches.Count > 0;
            }
            if (matches.Count == 0)
            {
                reason = Tr($"No Free H map matches the scene's map \"{mapLoadInfo.name}\" ({mapLoadInfo.bundlePath}).", $"シーンのマップ「{mapLoadInfo.name}」（{mapLoadInfo.bundlePath}）に対応するFree-H用マップがありません。");
                return none;
            }
            var freeH = matches.Where(p => p.isFreeH).ToList();
            if (freeH.Count == 0)
            {
                reason = Tr($"The scene's map \"{matches[0].DisplayName}\" can't be used in Free H.", $"シーンのマップ「{matches[0].DisplayName}」はFree-Hでは使えません。");
                return none;
            }
            int chosen = Settings != null ? Settings.FreeHMap : -1;
            return freeH
                .OrderBy(p => p.No == chosen ? 0 : 1)
                .ThenBy(p => SameName(p, mapLoadInfo.name) ? 0 : 1)
                .ThenBy(p => p.No)
                .ToList();
        }

        private static bool SameName(MapInfo.Param p, string studioName) =>
            !string.IsNullOrEmpty(studioName) && (p.MapName == studioName || p.DisplayName == studioName);

        /// <summary>Remembers the Free H map chosen for this scene.</summary>
        internal static void SetFreeHMap(int no)
        {
            if (Settings == null || Settings.FreeHMap == no) return;
            Settings.FreeHMap = no;
            SaveSettings();
        }

        internal static void SetMatchTime(bool on)
        {
            if (Settings == null || Settings.MatchTime == on) return;
            Settings.MatchTime = on;
            SaveSettings();
        }

        /// <summary>The time of day of the scene's studio map (SunLightInfo.Info.Type: 0 morning .. 3 night).</summary>
        internal static int SceneSunLightType => _sceneInfo != null ? _sceneInfo.sunLightType : -1;

        private static string StripStudioSuffix(string assetName)
        {
            var s = (assetName ?? "").ToLowerInvariant().Replace("hotel_suite", "hotelsuite");
            if (s.EndsWith("_studio")) s = s.Substring(0, s.Length - "_studio".Length);
            else if (s.EndsWith("_s")) s = s.Substring(0, s.Length - 2);
            return s;
        }

        /// <summary>
        /// Places the scene's items, folders and lights relative to the H map. Call after the H map has been loaded.
        /// </summary>
        internal static void LoadOverlay(Transform hMapRoot)
        {
            if (_sceneInfo == null) return;
            // Studio puts the map root at caMap (MapCtrl.Reflect overwrites the root's own position, e.g. (-50, 0, -115)
            // for the suite room). In H the root stays where the map scene has it, so map the objects from the studio
            // root to the H root.
            var inv = Quaternion.Inverse(Quaternion.Euler(_sceneInfo.caMap.rot));
            var rootRot = hMapRoot.rotation * inv;
            var rootPos = hMapRoot.position + hMapRoot.rotation * (inv * -_sceneInfo.caMap.pos);
            FreeHStudioSceneLoader.Logger.LogInfo($"Map root in H at {hMapRoot.position}, studio map position {_sceneInfo.caMap.pos}");
            LoadOverlay(rootPos, rootRot);
        }

        /// <summary>
        /// Places the objects of a scene that has no studio map, so that the scene's (first female) character
        /// stands where the H scene starts. Only the yaw of the character is used, so the scene stays upright.
        /// </summary>
        internal static void LoadOverlayAt(Vector3 startPos, Quaternion startRot)
        {
            if (_sceneInfo == null) return;
            FindAnchor(out var anchorPos, out var anchorYaw, out var anchorName);
            var rot = startRot * Quaternion.Inverse(Quaternion.Euler(0f, anchorYaw, 0f));
            FreeHStudioSceneLoader.Logger.LogInfo($"Scene without a map: aligning {anchorName} at {anchorPos} to the H start position {startPos}");
            LoadOverlay(startPos - rot * anchorPos, rot);
        }

        private static void LoadOverlay(Vector3 rootPos, Quaternion rootRot)
        {
            var studio = Singleton<Studio.Studio>.Instance;
            studio.sceneInfo = _sceneInfo;

            _overlayRoot = new GameObject("FreeHStudioSceneLoader_Objects");
            _overlayRoot.transform.rotation = rootRot;
            _overlayRoot.transform.position = rootPos;
            _overlayRootPos = rootPos;
            _overlayRootRot = rootRot;
            _moveOffsets.Clear();

            int loaded = 0, skipped = 0, lights = 0;
            _pendingCharacters.Clear();
            foreach (var info in _sceneInfo.dicObject.Values.ToList())
            {
                if (!ShouldLoad(info))
                {
                    if (!DeferCharacter(info, null)) skipped++;
                    continue;
                }
                // Folders and items load their children too; characters are taken out and loaded later
                skipped += RemoveSkippedChildren(info);
                try
                {
                    var oci = AddObjectAssist.LoadChild(info);
                    if (oci?.guideObject?.transformTarget != null)
                    {
                        oci.guideObject.transformTarget.SetParent(_overlayRoot.transform, false);
                        loaded++;
                        if (oci is OCILight) lights++;
                    }
                }
                catch (Exception ex)
                {
                    FreeHStudioSceneLoader.Logger.LogWarning($"Failed to load studio object (kind {info.kind}, key {info.dicKey}): {ex}");
                }
            }
            ApplyScales();
            HideInsideHiddenNodes();
            FreeHStudioSceneLoader.Logger.LogInfo($"Studio scene objects placed: {loaded}, {lights} of them lights (skipped {skipped} cameras/routes/texts/characters turned off), {_pendingCharacters.Count} character(s) to load");
        }

        private static bool DeferCharacter(ObjectInfo info, ObjectInfo parent)
        {
            if (!(info is OICharInfo chara) || !IsCharacterEnabled(chara)) return false;
            _pendingCharacters.Add(new KeyValuePair<OICharInfo, ObjectInfo>(chara, parent));
            return true;
        }

        /// <summary>
        /// Creates the scene's characters, posed as in Studio. Call after the other objects were placed and the H
        /// characters exist, so that the game creates its own characters first.
        /// </summary>
        internal static void LoadCharacters()
        {
            if (_sceneInfo == null || _overlayRoot == null || _pendingCharacters.Count == 0) return;
            SceneAnimator.InstallPlayerLoopStep(); // diagnostics (pose probe) also without plugin animation
            var studio = Singleton<Studio.Studio>.Instance;
            studio.sceneInfo = _sceneInfo;
            int loaded = 0;
            // Loading a character can queue more (characters in a folder attached to it), so take them one by one
            while (_pendingCharacters.Count > 0)
            {
                var pending = _pendingCharacters[0];
                _pendingCharacters.RemoveAt(0);
                var info = pending.Key;
                var name = info.charFile?.parameter?.fullname;
                ObjectCtrlInfo parent = null;
                if (pending.Value != null && !studio.dicObjectCtrl.TryGetValue(pending.Value.dicKey, out parent))
                {
                    FreeHStudioSceneLoader.Logger.LogWarning($"Character {name} not loaded: the folder/item it is placed under was not loaded");
                    continue;
                }
                // Items attached to the character load with it
                RemoveSkippedChildren(info);
                LoadReplacementCard(info);
                var before = new HashSet<ChaControl>(Manager.Character.dictEntryChara.Values);
                // Loading turns FK off when IK is on; the FKIK plugin restores both (see EnableFKIK)
                bool fkAndIK = info.enableFK && info.enableIK;
                bool fkSkirt = info.activeFK[6];
                IsLoadingCharacter = true;
                try
                {
                    var oci = AddObjectAssist.LoadChild(info, parent, null) as OCIChar;
                    if (oci?.charInfo == null) throw new InvalidOperationException("no character was created");
                    if (fkAndIK && FreeHStudioSceneLoader.ApplyFKAndIK.Value)
                    {
                        EnableFKIK(oci);
                        if (fkSkirt) info.activeFK[6] = true;
                    }
                    _characters.Add(oci.charInfo);
                    if (parent == null) oci.guideObject.transformTarget.SetParent(_overlayRoot.transform, false);
                    var entry = _sceneCharacters.FirstOrDefault(c => c.Info == info);
                    if (entry != null)
                    {
                        entry.Oci = oci;
                        entry.OriginalClothesState = (byte[])oci.charInfo.fileStatus.clothesState.Clone();
                        ApplyOutfit(entry);
                        entry.OriginalPos = info.changeAmount.pos;
                        entry.OriginalRot = info.changeAmount.rot;
                        SavePvCopy(entry);
                    }
                    // Studio plays the animation chosen in the scene; FK (a fixed pose) overrides it
                    FreeHStudioSceneLoader.Logger.LogInfo($"Character {name}: animation {info.animeInfo.group}/{info.animeInfo.category}/{info.animeInfo.no}, speed {info.animeSpeed}, FK {info.enableFK}, IK {info.enableIK}{(fkAndIK ? " (saved with FK and IK)" : "")}");
                    loaded++;
                }
                catch (Exception ex)
                {
                    FreeHStudioSceneLoader.Logger.LogWarning($"Failed to load studio character {name}: {ex}");
                    // Don't leave a half-created character standing around
                    foreach (var chara in Manager.Character.dictEntryChara.Values.Where(c => !before.Contains(c)).ToList())
                        RemoveCharacter(chara);
                }
                finally
                {
                    IsLoadingCharacter = false;
                }
            }
            ApplyScales();
            HideInsideHiddenNodes();
            FreeHStudioSceneLoader.Logger.LogInfo($"Studio scene characters placed: {loaded}");
            if (Settings != null && Settings.CharaVoices) PlayVoices();
            if (loaded > 0)
                FreeHStudioSceneLoader.Logger.LogMessage(Tr($"Studio scene characters: show/hide and move them with the {FreeHStudioSceneLoader.WindowKey.Value} key", $"スタジオシーンのキャラクター: {FreeHStudioSceneLoader.WindowKey.Value} キーで表示の切り替えと位置の調整ができます"));
        }

        #region Missing mods of the scene

        internal class MissingMod
        {
            public string Guid, Name, Author, Website;
            /// <summary>How many times Sideloader reported it (about one per item, character part, etc.).</summary>
            public int Count;
        }

        /// <summary>The mods Sideloader could not find while the current scene was read, in the order first reported.</summary>
        internal static readonly List<MissingMod> MissingMods = new List<MissingMod>();

        /// <summary>
        /// Postfix of Sideloader's UniversalAutoResolver.ShowGUIDError, which Sideloader calls for every object of a
        /// card or scene that needs a mod that is not installed (the "Missing mod detected!" warnings).
        /// </summary>
        internal static void OnMissingMod(string guid, string author, string website, string name)
        {
            if (!IsLoadingScene) return;
            var mod = MissingMods.FirstOrDefault(m => m.Guid == guid);
            if (mod == null) MissingMods.Add(mod = new MissingMod { Guid = guid, Name = name, Author = author, Website = website });
            mod.Count++;
        }

        /// <summary>The missing mods as text to paste somewhere (a list to look the mods up with).</summary>
        internal static string MissingModsText(string scenePath)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Scene: {System.IO.Path.GetFileName(scenePath)}");
            if (MissingMods.Count == 0)
            {
                sb.AppendLine("Missing mods: none");
                return sb.ToString();
            }
            sb.AppendLine($"Missing mods: {MissingMods.Count}");
            foreach (var m in MissingMods.OrderByDescending(m => m.Count))
            {
                sb.Append($"- {(string.IsNullOrEmpty(m.Name) ? m.Guid : m.Name)} (GUID: {m.Guid})");
                if (!string.IsNullOrEmpty(m.Author)) sb.Append($" by {m.Author}");
                sb.Append($", used {m.Count} times");
                if (!string.IsNullOrEmpty(m.Website)) sb.Append($", {m.Website}");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        #endregion

        #region Settings of the scene (saved per scene file)

        internal static string ScenePath { get; private set; }
        internal static SceneSettings Settings { get; private set; }

        private static void SaveSettings()
        {
            if (ScenePath != null && Settings != null) SceneSettingsStore.Save(ScenePath, Settings);
        }

        internal static bool FemalesOn => Settings != null ? Settings.Females : FreeHStudioSceneLoader.LoadFemales.Value;
        internal static bool MalesOn => Settings != null ? Settings.Males : FreeHStudioSceneLoader.LoadMales.Value;

        /// <summary>The selection window's switches for all females / all males of the scene.</summary>
        internal static void SetSexEnabled(bool female, bool enabled)
        {
            if (Settings == null || (female ? Settings.Females : Settings.Males) == enabled) return;
            if (female) Settings.Females = enabled;
            else Settings.Males = enabled;
            SaveSettings();
        }

        internal static void SetCharacterEnabled(SceneCharacter chara, bool enabled)
        {
            if (chara.Enabled == enabled) return;
            chara.Enabled = enabled;
            if (Settings == null) return;
            if (enabled) Settings.HiddenCharacters.Remove(chara.Info.dicKey);
            else Settings.HiddenCharacters.Add(chara.Info.dicKey);
            SaveSettings();
        }

        /// <summary>Whether Studio's sound/voice items may play (checked when one is enabled).</summary>
        internal static bool SoundItemsOn => Settings != null ? Settings.SoundItems : FreeHStudioSceneLoader.PlaySoundItems.Value;

        internal static void SetSoundItems(bool on)
        {
            if (Settings == null || Settings.SoundItems == on) return;
            Settings.SoundItems = on;
            SaveSettings();
            if (_overlayRoot == null) return;
            // Re-enabling starts or stops them: OnDisable stops the sound, OnEnable plays it only when allowed
            foreach (var se in _overlayRoot.GetComponentsInChildren<Studio.Sound.SEComponent>())
            {
                se.enabled = false;
                se.enabled = true;
            }
        }

        internal static int CharactersWithVoices => _sceneCharacters.Count(c => c.Info.voiceCtrl != null && c.Info.voiceCtrl.list.Count > 0);

        internal static void SetCharaVoices(bool on)
        {
            if (Settings == null || Settings.CharaVoices == on) return;
            Settings.CharaVoices = on;
            SaveSettings();
            if (on) PlayVoices();
            else
                foreach (var chara in _sceneCharacters.Where(c => c.Placed))
                    chara.Oci.StopVoice();
        }

        /// <summary>Plays each placed character's voice list from the start, like Studio's play button.</summary>
        #region Replacing characters with other cards

        internal static readonly string CardRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(BepInEx.Paths.GameRootPath, "UserData/chara"));

        private static string CardPath(string card) => System.IO.Path.IsPathRooted(card) ? card : System.IO.Path.Combine(CardRoot, card);

        private static string CardKey(string path)
        {
            var full = System.IO.Path.GetFullPath(path);
            return full.StartsWith(CardRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(CardRoot.Length + 1).Replace('\\', '/')
                : full;
        }

        /// <summary>The card file a character is replaced with (null: none, or the file is gone).</summary>
        private static string ReplacementFile(OICharInfo info)
        {
            if (Settings == null || !Settings.Replacements.TryGetValue(info.dicKey, out var r)) return null;
            var path = CardPath(r.Card);
            if (System.IO.File.Exists(path)) return path;
            // Also asked while drawing the window: say it once
            if (_missingCards.Add(path))
                FreeHStudioSceneLoader.Logger.LogWarning($"Card to replace {info.charFile?.parameter?.fullname} with not found: {path}");
            return null;
        }

        private static readonly HashSet<string> _missingCards = new HashSet<string>();

        /// <summary>Before a character is created: reads the replacement card into its data (the scene's pose and state stay).</summary>
        private static void LoadReplacementCard(OICharInfo info)
        {
            var path = ReplacementFile(info);
            if (path == null) return;
            try
            {
                var entry = _sceneCharacters.FirstOrDefault(c => c.Info == info);
                if (entry != null) entry.OriginalCardFile = SaveOriginalCard(info);
                // Like Studio's 「変更」 (OCIChar.ChangeChara): the card's looks, the scene's status (clothes, etc.)
                if (info.charFile.LoadCharaFile(path, 255, true, true))
                    FreeHStudioSceneLoader.Logger.LogInfo($"Character replaced with card {path}");
                else
                    FreeHStudioSceneLoader.Logger.LogWarning($"Could not read the card {path}");
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not replace the character with {path}: {ex.Message}");
            }
        }

        /// <summary>Saves the scene's own card of a character to a file (with its extended data), to go back to.</summary>
        private static string SaveOriginalCard(OICharInfo info)
        {
            var dir = System.IO.Path.Combine(BepInEx.Paths.CachePath, "FreeHStudioSceneLoader");
            System.IO.Directory.CreateDirectory(dir);
            var file = System.IO.Path.Combine(dir, $"scene_chara_{info.dicKey}.png");
            using (var stream = System.IO.File.Create(file))
                info.charFile.SaveCharaFile(stream, true);
            return file;
        }

        /// <summary>Reads only the header of a file: a Koikatsu Sunshine character card.</summary>
        internal static bool IsCard(string path)
        {
            try
            {
                using (var reader = new System.IO.BinaryReader(System.IO.File.OpenRead(path)))
                {
                    PngFile.SkipPng(reader);
                    if (reader.BaseStream.Length - reader.BaseStream.Position < 8) return false;
                    if (reader.ReadInt32() > 100) return false;
                    return reader.ReadString() == "【KoiKatuCharaSun】";
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Replaces a scene character with a card (null: back to the scene's own), saved for the scene. A placed
        /// character changes at once, like Studio's 「変更」; otherwise when it is next placed. Returns an error or null.
        /// </summary>
        internal static string SetReplacement(SceneCharacter chara, string path)
        {
            if (Settings == null) return Tr("No scene is loaded.", "シーンが読み込まれていません。");
            string name = null;
            if (path != null)
            {
                var file = new ChaFileControl();
                if (!file.LoadCharaFile(path, (byte)(chara.Female ? 1 : 0), true, true))
                    return Tr($"This card can't be used (not a Koikatsu Sunshine {(chara.Female ? "female" : "male")} character card).", $"このカードは読み込めません（コイカツサンシャインの{(chara.Female ? "女性" : "男性")}キャラカードではありません）。");
                name = file.parameter.fullname;
                Settings.Replacements[chara.Info.dicKey] = new CardReplacement { Card = CardKey(path), Name = name };
            }
            else
                Settings.Replacements.Remove(chara.Info.dicKey);
            SaveSettings();
            if (!chara.Placed) return null;

            try
            {
                if (chara.OriginalCardFile == null) chara.OriginalCardFile = SaveOriginalCard(chara.Info);
                ChangeCard(chara, path ?? chara.OriginalCardFile);
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not change the card of {chara.Name}: {ex}");
                return Tr("Could not replace the character (see output_log.txt).", "キャラクターを差し替えられませんでした（output_log.txt を確認してください）。");
            }
            return null;
        }

        private static void ChangeCard(SceneCharacter chara, string path)
        {
            var oci = chara.Oci;
            bool fkAndIK = oci.oiCharInfo.enableFK && oci.oiCharInfo.enableIK;
            bool fkSkirt = oci.oiCharInfo.activeFK[6];
            IsLoadingCharacter = true; // the body uses Studio's bones, as when it was created
            try
            {
                oci.ChangeChara(path);
            }
            finally
            {
                IsLoadingCharacter = false;
            }
            // ChangeChara turns FK back on alone; keep FK and IK together as when the character was placed
            if (fkAndIK && FreeHStudioSceneLoader.ApplyFKAndIK.Value)
            {
                EnableFKIK(oci);
                if (fkSkirt) oci.oiCharInfo.activeFK[6] = true;
            }
            ApplyScales();
            FreeHStudioSceneLoader.Logger.LogInfo($"Scene character {chara.Name} now uses card {path} ({oci.charInfo.chaFile.parameter.fullname})");
        }

        #endregion

        #region H points at the scene's characters

        /// <summary>
        /// 場所移動 (HPointMove): adds an H point where each placed scene character stands, once for every kind
        /// (category) of point the map offers, so that choosing a kind and then the character's place gives that kind's
        /// positions there. The points are made from the prefab the game uses for the point H started at
        /// (h/common/01.unity3d HPointBase) and are put in the HPointMove scene, which is unloaded with them.
        /// </summary>
        internal static void AddCharacterPoints(HPointMove move, Dictionary<int, List<GameObject>> points)
        {
            if (!HasPlacedScene || !FreeHStudioSceneLoader.ScenePoints.Value || points == null) return;
            var categories = points.Where(p => p.Value.Count > 0).Select(p => p.Key).OrderBy(c => c).ToList();
            // One point for characters standing together (a couple posed in the same place)
            var places = new List<Transform>();
            foreach (var chara in _sceneCharacters)
            {
                if (!chara.Placed) continue;
                var t = chara.Oci.charInfo.transform;
                if (!places.Any(p => (p.position - t.position).sqrMagnitude < 0.2f * 0.2f)) places.Add(t);
            }
            if (categories.Count == 0 || places.Count == 0) return;

            var setCategoryObjects = HarmonyLib.AccessTools.Method(typeof(HPointMove), "SetCategoryObjects");
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName("HPointMove");
            int added = 0;
            try
            {
                foreach (var place in places)
                {
                    foreach (var category in categories)
                    {
                        var obj = CommonLib.LoadAsset<GameObject>("h/common/01.unity3d", "HPointBase", true, "", true);
                        if (obj == null) return;
                        if (scene.IsValid()) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obj, scene);
                        obj.name = "FreeHStudioScenePoint";
                        obj.transform.SetPositionAndRotation(place.position, Quaternion.Euler(0f, place.eulerAngles.y, 0f));
                        var data = obj.GetComponent<H.HPointData>();
                        if (data == null)
                        {
                            UnityEngine.Object.Destroy(obj);
                            return;
                        }
                        data.category = new[] { category };
                        data.SetKindOffset(Vector3.zero, Vector3.zero);
                        data.BackUpPosition();
                        obj.SetActive(false);
                        // Sorts it in like the map's own points (checks that the kind has positions in this mode)
                        setCategoryObjects.Invoke(move, new object[] { data, false });
                        if (points.TryGetValue(category, out var list) && list.Contains(obj)) added++;
                        else UnityEngine.Object.Destroy(obj);
                    }
                }
            }
            finally
            {
                AssetBundleManager.UnloadAssetBundle("h/common/01.unity3d", true, null, false);
                FreeHStudioSceneLoader.Logger.LogInfo($"Point move: {added} H points at {places.Count} studio character places ({categories.Count} kinds of point)");
            }
        }

        #endregion

        #region Outfits (kind of clothes, coordinate cards)

        internal static readonly string CoordinateRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(BepInEx.Paths.GameRootPath, "UserData/coordinate"));
        internal static string[] OutfitTypeNames => FreeHStudioSceneLoader.Japanese ? OutfitTypeNamesJa : OutfitTypeNamesEn;
        private static readonly string[] OutfitTypeNamesEn = { "Casual", "Swimsuit", "Pajamas", "Bath towel" };
        private static readonly string[] OutfitTypeNamesJa = { "普段着", "水着", "パジャマ", "バスタオル" };

        // MoreOutfits (KK_Plugins, optional) adds outfit slots after the game's four and names them
        private static System.Reflection.MethodInfo _moreOutfitsName;
        private static string _moreOutfitsGuid;
        private static bool _moreOutfitsChecked;

        private static void FindMoreOutfits()
        {
            if (_moreOutfitsChecked) return;
            _moreOutfitsChecked = true;
            var plugin = HarmonyLib.AccessTools.TypeByName("KK_Plugins.MoreOutfits.Plugin");
            if (plugin == null) return;
            _moreOutfitsName = HarmonyLib.AccessTools.Method(plugin, "GetCoodinateName", new[] { typeof(ChaControl), typeof(int) });
            _moreOutfitsGuid = HarmonyLib.AccessTools.Field(plugin, "PluginGUID")?.GetValue(null) as string;
        }

        /// <summary>How many outfits the character has (4, or more with MoreOutfits).</summary>
        internal static int OutfitCount(SceneCharacter chara)
        {
            var file = chara.Placed ? chara.Oci.charInfo.chaFile : CardBeforePlacing(chara);
            return Math.Max(OutfitTypeNames.Length, file?.coordinate?.Length ?? 0);
        }

        private static readonly Dictionary<string, ChaFileControl> _replacementCards = new Dictionary<string, ChaFileControl>();

        /// <summary>
        /// The card a character will be created from: the replacement card when there is one (read once and kept,
        /// with its extended data, so its outfit slots and their names show before H), else the scene's.
        /// </summary>
        private static ChaFile CardBeforePlacing(SceneCharacter chara)
        {
            var path = ReplacementFile(chara.Info);
            if (path == null) return chara.Info.charFile;
            if (!_replacementCards.TryGetValue(path, out var file))
            {
                file = new ChaFileControl();
                try
                {
                    if (!file.LoadCharaFile(path, 255, true, true)) file = null;
                }
                catch (Exception ex)
                {
                    FreeHStudioSceneLoader.Logger.LogDebug($"Could not read the card {path}: {ex.Message}");
                    file = null;
                }
                _replacementCards[path] = file;
            }
            return (ChaFile)file ?? chara.Info.charFile;
        }

        /// <summary>The name of an outfit slot: the game's for the first four, MoreOutfits' for the ones it added.</summary>
        internal static string OutfitName(SceneCharacter chara, int index)
        {
            if (index < OutfitTypeNames.Length) return OutfitTypeNames[index];
            FindMoreOutfits();
            try
            {
                if (chara.Placed && _moreOutfitsName != null)
                {
                    var name = _moreOutfitsName.Invoke(null, new object[] { chara.Oci.charInfo, index }) as string;
                    if (!string.IsNullOrEmpty(name)) return name;
                }
                else if (!chara.Placed && _moreOutfitsGuid != null)
                {
                    // Not created yet: MoreOutfits keeps the names in the card's extended data
                    var data = ExtensibleSaveFormat.ExtendedSave.GetExtendedDataById(CardBeforePlacing(chara), _moreOutfitsGuid);
                    if (data?.data != null && data.data.TryGetValue("CoordinateNames", out var names) && names is System.Collections.IDictionary dict)
                        foreach (System.Collections.DictionaryEntry e in dict)
                            if (Convert.ToInt32(e.Key) == index && e.Value is string n && n.Length > 0) return n;
                }
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogDebug($"MoreOutfits name of outfit {index}: {ex.Message}");
            }
            return Tr($"Extra outfit {index - OutfitTypeNames.Length + 1}", $"追加の服 {index - OutfitTypeNames.Length + 1}");
        }

        private static CharaOutfit OutfitOf(SceneCharacter chara) =>
            Settings != null && Settings.Outfits.TryGetValue(chara.Info.dicKey, out var o) ? o : null;

        /// <summary>The kind of clothes the character wears now (placed) or will wear (chosen, else the scene's).</summary>
        internal static int OutfitType(SceneCharacter chara)
        {
            if (chara.Placed) return chara.Oci.charInfo.fileStatus.coordinateType;
            var o = OutfitOf(chara);
            return o != null && o.Type >= 0 ? o.Type : chara.Info.charFile.status.coordinateType;
        }

        internal static string OutfitCardName(SceneCharacter chara)
        {
            var o = OutfitOf(chara);
            if (o == null || string.IsNullOrEmpty(o.Card)) return null;
            return string.IsNullOrEmpty(o.Name) ? System.IO.Path.GetFileNameWithoutExtension(o.Card) : o.Name;
        }

        internal static bool HasOutfit(SceneCharacter chara)
        {
            var o = OutfitOf(chara);
            return o != null && (o.Type >= 0 || !string.IsNullOrEmpty(o.Card) || o.ClothesState >= 0);
        }

        internal static string[] ClothesStateNames => FreeHStudioSceneLoader.Japanese ? ClothesStateNamesJa : ClothesStateNamesEn;
        private static readonly string[] ClothesStateNamesEn = { "As in scene", "On", "Half", "Off" };
        private static readonly string[] ClothesStateNamesJa = { "シーンのまま", "全着", "半脱", "全脱" };

        /// <summary>The clothes state chosen for the character: 0 全着, 1 半脱, 2 全脱, -1 as in the scene.</summary>
        internal static int ClothesState(SceneCharacter chara) => OutfitOf(chara)?.ClothesState ?? -1;

        /// <summary>全着・半脱・全脱 of all clothes at once (Studio's buttons), or -1 for each part as the scene has it.</summary>
        internal static void SetClothesState(SceneCharacter chara, int state)
        {
            if (Settings == null) return;
            var o = EditOutfit(chara);
            if (o.ClothesState == state) return;
            o.ClothesState = state;
            SaveSettings();
            if (chara.Placed) ApplyClothesState(chara, state);
        }

        private static void ApplyClothesState(SceneCharacter chara, int state)
        {
            var cha = chara.Oci.charInfo;
            if (state >= 0)
                cha.SetClothesStateAll((byte)state);
            else if (chara.OriginalClothesState != null)
                for (int i = 0; i < chara.OriginalClothesState.Length && i < cha.fileStatus.clothesState.Length; i++)
                    cha.SetClothesState(i, chara.OriginalClothesState[i], false);
        }

        private static CharaOutfit EditOutfit(SceneCharacter chara)
        {
            if (!Settings.Outfits.TryGetValue(chara.Info.dicKey, out var o))
                Settings.Outfits[chara.Info.dicKey] = o = new CharaOutfit();
            return o;
        }

        /// <summary>Reads only the header of a file: a coordinate card.</summary>
        internal static bool IsCoordinateCard(string path)
        {
            try
            {
                using (var reader = new System.IO.BinaryReader(System.IO.File.OpenRead(path)))
                {
                    PngFile.SkipPng(reader);
                    if (reader.BaseStream.Length - reader.BaseStream.Position < 8) return false;
                    if (reader.ReadInt32() > 100) return false;
                    return reader.ReadString() == "【KoiKatuClothes】";
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Changes the kind of clothes (Studio's 服の種類), saved for the scene.</summary>
        internal static void SetOutfitType(SceneCharacter chara, int type)
        {
            if (Settings == null) return;
            var o = EditOutfit(chara);
            if (o.Type == type && (!chara.Placed || OutfitType(chara) == type)) return;
            // A coordinate card belongs to the kind it was put on
            if (o.Type != type) { o.Card = null; o.Name = null; }
            o.Type = type;
            SaveSettings();
            if (chara.Placed) chara.Oci.SetCoordinateInfo((ChaFileDefine.CoordinateType)type, false);
        }

        /// <summary>Puts on a coordinate card for the current kind of clothes (Studio's 衣装の読み込み). Returns an error or null.</summary>
        internal static string SetOutfitCard(SceneCharacter chara, string path)
        {
            if (Settings == null) return Tr("No scene is loaded.", "シーンが読み込まれていません。");
            var file = new ChaFileCoordinate();
            if (!file.LoadFile(path)) return Tr("This coordinate card can't be loaded.", "このコーデカードは読み込めません。");
            var o = EditOutfit(chara);
            o.Type = OutfitType(chara);
            var full = System.IO.Path.GetFullPath(path);
            o.Card = full.StartsWith(CoordinateRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(CoordinateRoot.Length + 1).Replace('\\', '/') : full;
            o.Name = string.IsNullOrEmpty(file.coordinateName) ? System.IO.Path.GetFileNameWithoutExtension(path) : file.coordinateName;
            SaveSettings();
            if (!chara.Placed) return null;
            try
            {
                if (chara.OriginalCardFile == null) chara.OriginalCardFile = SaveOriginalCard(chara.Info);
                chara.Oci.LoadClothesFile(path);
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not put the coordinate {path} on {chara.Name}: {ex}");
                return Tr("Could not load the coordinate (see output_log.txt).", "コーデを読み込めませんでした（output_log.txt を確認してください）。");
            }
            return null;
        }

        /// <summary>Back to the scene's clothes: reads the character's card again (a coordinate card changed it).</summary>
        internal static string ResetOutfit(SceneCharacter chara)
        {
            if (Settings == null) return null;
            Settings.Outfits.Remove(chara.Info.dicKey);
            SaveSettings();
            if (!chara.Placed) return null;
            try
            {
                var card = ReplacementFile(chara.Info) ?? chara.OriginalCardFile;
                if (card != null) ChangeCard(chara, card);
                chara.Oci.SetCoordinateInfo((ChaFileDefine.CoordinateType)chara.Info.charFile.status.coordinateType, true);
                ApplyClothesState(chara, -1);
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not reset the clothes of {chara.Name}: {ex}");
                return Tr("Could not reset the clothes (see output_log.txt).", "服を戻せませんでした（output_log.txt を確認してください）。");
            }
            return null;
        }

        /// <summary>When a character was placed: the kind of clothes and the coordinate card chosen for it.</summary>
        private static void ApplyOutfit(SceneCharacter chara)
        {
            var o = OutfitOf(chara);
            if (o == null) return;
            try
            {
                if (o.Type >= 0) chara.Oci.SetCoordinateInfo((ChaFileDefine.CoordinateType)o.Type, true);
                if (!string.IsNullOrEmpty(o.Card))
                {
                    var path = System.IO.Path.IsPathRooted(o.Card) ? o.Card : System.IO.Path.Combine(CoordinateRoot, o.Card);
                    if (!System.IO.File.Exists(path))
                        FreeHStudioSceneLoader.Logger.LogWarning($"Coordinate card for {chara.Name} not found: {path}");
                    else
                    {
                        if (chara.OriginalCardFile == null && ReplacementFile(chara.Info) == null) chara.OriginalCardFile = SaveOriginalCard(chara.Info);
                        chara.Oci.LoadClothesFile(path);
                        FreeHStudioSceneLoader.Logger.LogInfo($"Coordinate {path} put on {chara.Name}");
                    }
                }
                if (o.ClothesState >= 0) ApplyClothesState(chara, o.ClothesState);
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not set the clothes of {chara.Name}: {ex.Message}");
            }
        }

        #endregion

        private static void StopVoices(OCIChar chara)
        {
            try
            {
                chara.voiceCtrl?.Stop();
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not stop the voices of {chara.charInfo?.fileParam?.fullname}: {ex.Message}");
            }
        }

        private static void PlayVoices()
        {
            foreach (var chara in _sceneCharacters.Where(c => c.Placed && c.Info.voiceCtrl != null && c.Info.voiceCtrl.list.Count > 0))
            {
                try { chara.Oci.PlayVoice(0); }
                catch (Exception ex) { FreeHStudioSceneLoader.Logger.LogWarning($"Could not play the voices of {chara.Name}: {ex.Message}"); }
            }
        }

        #endregion

        #region Adjusting the scene characters in H

        internal static bool IsVisible(SceneCharacter chara) => chara.Placed && chara.Oci.treeNodeObject != null && chara.Oci.treeNodeObject.visible;

        /// <summary>Hides or shows the character with the items attached to it, like the visibility switch in Studio.</summary>
        internal static void SetVisible(SceneCharacter chara, bool visible)
        {
            if (chara.Placed && chara.Oci.treeNodeObject != null) chara.Oci.treeNodeObject.SetVisible(visible);
        }

        internal static Vector3 GetPosition(SceneCharacter chara) => chara.Oci.guideObject.transformTarget.position;

        /// <summary>
        /// Places the character at a world position/rotation. The pose is kept in the scene data (changeAmount,
        /// relative to the folder/item it is under), which moves the character like the Studio move tool does.
        /// </summary>
        private static void SetWorldPose(SceneCharacter chara, Vector3 position, Quaternion rotation)
        {
            var target = chara.Oci.guideObject.transformTarget;
            var parent = target.parent;
            var changeAmount = chara.Oci.objectInfo.changeAmount;
            changeAmount.pos = parent != null ? parent.InverseTransformPoint(position) : position;
            changeAmount.rot = (parent != null ? Quaternion.Inverse(parent.rotation) * rotation : rotation).eulerAngles;
        }

        internal static void Move(IEnumerable<SceneCharacter> charas, Vector3 delta)
        {
            foreach (var chara in charas.Where(c => c.Placed))
            {
                var target = chara.Oci.guideObject.transformTarget;
                SetWorldPose(chara, target.position + delta, target.rotation);
            }
        }

        /// <summary>Turns the characters around the vertical axis through <paramref name="pivot"/>.</summary>
        internal static void Rotate(IEnumerable<SceneCharacter> charas, Vector3 pivot, float degrees)
        {
            var turn = Quaternion.AngleAxis(degrees, Vector3.up);
            foreach (var chara in charas.Where(c => c.Placed))
            {
                var target = chara.Oci.guideObject.transformTarget;
                SetWorldPose(chara, pivot + turn * (target.position - pivot), turn * target.rotation);
            }
        }

        internal static void ResetPose(IEnumerable<SceneCharacter> charas)
        {
            foreach (var chara in charas.Where(c => c.Placed))
            {
                var changeAmount = chara.Oci.objectInfo.changeAmount;
                changeAmount.pos = chara.OriginalPos;
                changeAmount.rot = chara.OriginalRot;
            }
        }

        // NodesConstraints often hold the body's IK targets (hips, waist, shoulders) at folders of the scene, so moving
        // only the character leaves the body where it was and moves the arms and legs. Those folders may be moved by
        // Timeline every frame in their parent's space, so they are moved with a transform put between them and their
        // parent, which leaves their own values (and Timeline) alone.
        private static readonly Dictionary<ObjectCtrlInfo, Transform> _moveOffsets = new Dictionary<ObjectCtrlInfo, Transform>();
        private static Vector3 _overlayRootPos;
        private static Quaternion _overlayRootRot = Quaternion.identity;

        /// <summary>The folders/items linked to the characters, without those inside another moved one.</summary>
        internal static List<ObjectCtrlInfo> GetLinkedObjects(IEnumerable<SceneCharacter> charas)
        {
            var ocis = charas.Where(c => c.Placed).Select(c => (ObjectCtrlInfo)c.Oci).ToList();
            var linked = SceneAnimator.Instance != null ? SceneAnimator.Instance.LinkedParents(ocis) : new List<ObjectCtrlInfo>();
            linked = linked.Where(o => o?.guideObject?.transformTarget != null).ToList();
            var roots = ocis.Concat(linked).Select(o => o.guideObject.transformTarget).ToList();
            return linked.Where(o =>
            {
                var t = o.guideObject.transformTarget;
                return !roots.Any(r => r != t && t.IsChildOf(r));
            }).ToList();
        }

        private static Transform GetMoveOffset(ObjectCtrlInfo oci)
        {
            if (_moveOffsets.TryGetValue(oci, out var offset) && offset != null) return offset;
            var target = oci.guideObject.transformTarget;
            offset = new GameObject("FreeHStudioSceneLoader_MoveOffset").transform;
            offset.SetParent(target.parent, false);
            target.SetParent(offset, false);
            _moveOffsets[oci] = offset;
            return offset;
        }

        private static void Turn(Transform t, Vector3 pivot, Quaternion turn)
        {
            t.position = pivot + turn * (t.position - pivot);
            t.rotation = turn * t.rotation;
        }

        /// <summary>The characters that don't already move with one of the linked objects (placed inside it).</summary>
        private static List<SceneCharacter> NotInside(List<SceneCharacter> charas, List<ObjectCtrlInfo> linked) =>
            charas.Where(c => c.Placed && !linked.Any(o => c.Oci.guideObject.transformTarget.IsChildOf(o.guideObject.transformTarget))).ToList();

        internal static void MoveLinked(IEnumerable<SceneCharacter> charas, Vector3 delta)
        {
            var list = charas.ToList();
            var linked = GetLinkedObjects(list);
            var own = NotInside(list, linked);
            foreach (var oci in linked) GetMoveOffset(oci).position += delta;
            Move(own, delta);
        }

        internal static void RotateLinked(IEnumerable<SceneCharacter> charas, Vector3 pivot, float degrees)
        {
            var list = charas.ToList();
            var linked = GetLinkedObjects(list);
            var own = NotInside(list, linked);
            var turn = Quaternion.AngleAxis(degrees, Vector3.up);
            foreach (var oci in linked) Turn(GetMoveOffset(oci), pivot, turn);
            Rotate(own, pivot, degrees);
        }

        internal static void ResetLinked(IEnumerable<SceneCharacter> charas)
        {
            var list = charas.ToList();
            foreach (var oci in GetLinkedObjects(list))
            {
                if (!_moveOffsets.TryGetValue(oci, out var offset) || offset == null) continue;
                offset.localPosition = Vector3.zero;
                offset.localRotation = Quaternion.identity;
            }
            ResetPose(list);
        }

        /// <summary>Moves everything placed from the scene (characters, folders, items) together.</summary>
        internal static void MoveScene(Vector3 delta)
        {
            if (_overlayRoot != null) _overlayRoot.transform.position += delta;
        }

        internal static void RotateScene(Vector3 pivot, float degrees)
        {
            if (_overlayRoot != null) Turn(_overlayRoot.transform, pivot, Quaternion.AngleAxis(degrees, Vector3.up));
        }

        internal static void ResetScene()
        {
            if (_overlayRoot == null) return;
            _overlayRoot.transform.position = _overlayRootPos;
            _overlayRoot.transform.rotation = _overlayRootRot;
        }

        #endregion

        #region Layouts saved in H (per scene, a few slots)

        /// <summary>Saves where the scene and its characters are now into a slot of the scene's settings.</summary>
        internal static bool SaveLayout(int slot)
        {
            if (_overlayRoot == null || Settings == null || slot < 0 || slot >= Settings.Layouts.Length) return false;
            var inv = Quaternion.Inverse(_overlayRootRot);
            var root = _overlayRoot.transform;
            var layout = new SceneLayout
            {
                Saved = DateTime.Now,
                RootPos = inv * (root.position - _overlayRootPos),
                RootRot = inv * root.rotation,
            };
            foreach (var chara in _sceneCharacters.Where(c => c.Placed))
            {
                var changeAmount = chara.Oci.objectInfo.changeAmount;
                layout.Characters[chara.Info.dicKey] = new LayoutPose
                {
                    Pos = changeAmount.pos,
                    Rot = Quaternion.Euler(changeAmount.rot),
                    Visible = IsVisible(chara),
                };
            }
            foreach (var kv in _moveOffsets)
            {
                if (kv.Key?.objectInfo == null || kv.Value == null) continue;
                if (kv.Value.localPosition == Vector3.zero && kv.Value.localRotation == Quaternion.identity) continue;
                layout.Offsets[kv.Key.objectInfo.dicKey] = new LayoutPose { Pos = kv.Value.localPosition, Rot = kv.Value.localRotation };
            }
            Settings.Layouts[slot] = layout;
            SaveSettings();
            FreeHStudioSceneLoader.Logger.LogInfo($"Layout saved to slot {slot + 1}: {layout.Characters.Count} character(s), {layout.Offsets.Count} moved folder(s)/item(s)");
            return true;
        }

        /// <summary>Puts the scene and its characters where a slot has them (what is not in the slot goes back to the start).</summary>
        internal static bool LoadLayout(int slot)
        {
            var layout = Settings != null && slot >= 0 && slot < Settings.Layouts.Length ? Settings.Layouts[slot] : null;
            if (_overlayRoot == null || layout == null) return false;
            var root = _overlayRoot.transform;
            root.position = _overlayRootPos + _overlayRootRot * layout.RootPos;
            root.rotation = _overlayRootRot * layout.RootRot;
            // Offsets of folders/items moved with the characters
            foreach (var offset in _moveOffsets.Values.Where(o => o != null))
            {
                offset.localPosition = Vector3.zero;
                offset.localRotation = Quaternion.identity;
            }
            var placed = Singleton<Studio.Studio>.Instance.dicObjectCtrl;
            int offsets = 0;
            foreach (var kv in layout.Offsets)
            {
                if (!placed.TryGetValue(kv.Key, out var oci) || oci?.guideObject?.transformTarget == null) continue;
                var offset = GetMoveOffset(oci);
                offset.localPosition = kv.Value.Pos;
                offset.localRotation = kv.Value.Rot;
                offsets++;
            }
            int charas = 0;
            foreach (var chara in _sceneCharacters.Where(c => c.Placed))
            {
                var changeAmount = chara.Oci.objectInfo.changeAmount;
                if (layout.Characters.TryGetValue(chara.Info.dicKey, out var pose))
                {
                    changeAmount.pos = pose.Pos;
                    changeAmount.rot = pose.Rot.eulerAngles;
                    SetVisible(chara, pose.Visible);
                    charas++;
                }
                else
                {
                    changeAmount.pos = chara.OriginalPos;
                    changeAmount.rot = chara.OriginalRot;
                }
            }
            FreeHStudioSceneLoader.Logger.LogInfo($"Layout of slot {slot + 1} applied: {charas} character(s), {offsets} moved folder(s)/item(s)");
            return true;
        }

        internal static void DeleteLayout(int slot)
        {
            if (Settings == null || slot < 0 || slot >= Settings.Layouts.Length) return;
            Settings.Layouts[slot] = null;
            SaveSettings();
        }

        internal static bool AutoLayoutOn => Settings != null && Settings.AutoLayout;

        internal static void SetAutoLayout(bool on)
        {
            if (Settings == null || Settings.AutoLayout == on) return;
            Settings.AutoLayout = on;
            SaveSettings();
        }

        /// <summary>When the scene was placed in H: applies the layout saved last, if the scene wants that.</summary>
        internal static void ApplySavedLayout()
        {
            if (_overlayRoot == null || Settings == null || !Settings.AutoLayout) return;
            int slot = Settings.LastSavedLayout;
            if (slot < 0) return;
            try
            {
                if (LoadLayout(slot))
                    FreeHStudioSceneLoader.Logger.LogMessage(Tr($"Studio scene layout: applied slot {slot + 1} (the last saved one)", $"スタジオシーンの配置: スロット{slot + 1}（最後に保存したもの）を再現しました"));
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not apply the saved layout: {ex}");
            }
        }

        #endregion

        /// <summary>
        /// FK and IK at the same time, as the studio plugin "FK and IK" (KK_Plugins FKIK, GPL-3.0, Studio only) does when a
        /// character saved that way is loaded: IK for the body, FK for the other parts that are on, except the body FK
        /// (FKCtrl.parts[3]). Without it FK stays off and the hands, neck and hair keep the animation instead of the pose.
        /// </summary>
        private static void EnableFKIK(OCIChar ociChar)
        {
            var origPtn = ociChar.neckLookCtrl != null ? ociChar.neckLookCtrl.ptnNo : -1;
            ociChar.oiCharInfo.enableIK = true;
            ociChar.ActiveIK(OIBoneInfo.BoneGroup.Body, ociChar.oiCharInfo.activeIK[0], true);
            ociChar.ActiveIK(OIBoneInfo.BoneGroup.RightLeg, ociChar.oiCharInfo.activeIK[1], true);
            ociChar.ActiveIK(OIBoneInfo.BoneGroup.LeftLeg, ociChar.oiCharInfo.activeIK[2], true);
            ociChar.ActiveIK(OIBoneInfo.BoneGroup.RightArm, ociChar.oiCharInfo.activeIK[3], true);
            ociChar.ActiveIK(OIBoneInfo.BoneGroup.LeftArm, ociChar.oiCharInfo.activeIK[4], true);
            ociChar.ActiveKinematicMode(OICharInfo.KinematicMode.IK, true, true);

            ociChar.oiCharInfo.activeFK[3] = false;
            ociChar.fkCtrl.enabled = true;
            ociChar.oiCharInfo.enableFK = true;
            for (int j = 0; j < FKCtrl.parts.Length; j++)
                ociChar.ActiveFK(FKCtrl.parts[j], ociChar.oiCharInfo.activeFK[j], true);

            // The neck look pattern is overwritten on the way
            if (origPtn != -1 && ociChar.neckLookCtrl != null && origPtn != ociChar.neckLookCtrl.ptnNo)
                ociChar.ChangeLookNeckPtn(origPtn);
        }

        #region PVCopy

        // Studio's PVCopy (on the studio body bones) copies the animation's cf_pv_hand/leg to these bones, by world
        // position, every LateUpdate before IK. The copy also changes their local position (the forearm/shin length).
        // ABMX turns it off a few frames after a character is created (when it takes its baseline) and leaves it off.
        // In Studio the copies until then match the animation pose, so the bones keep their own position. Here the
        // KKPE edits (shoulders, spine) already apply in those frames: the copied hand ends up off the forearm, and
        // IK then twists the forearm toward it. So when a switch goes off, the bone is put back.
        private static readonly string[] PvCopyBones = { "cf_j_hand_L", "cf_j_hand_R", "cf_j_leg03_L", "cf_j_leg03_R" };

        private static int _pvCopyFrame = -1;

        private static void SavePvCopy(SceneCharacter entry)
        {
            var pvCopy = entry.Oci.preparation != null ? entry.Oci.preparation.pvCopy : null;
            if (pvCopy == null) return;
            entry.PvCopyLast = Enumerable.Range(0, PvCopyBones.Length).Select(i => pvCopy[i]).ToArray();
            var root = entry.Oci.charInfo.objBodyBone != null ? entry.Oci.charInfo.objBodyBone.transform : entry.Oci.charInfo.transform;
            var bones = root.GetComponentsInChildren<Transform>(true);
            entry.PvBones = PvCopyBones.Select(n => bones.FirstOrDefault(t => t.name == n)).ToArray();
            entry.PvBoneDefaults = entry.PvBones.Select(t => t != null ? t.localPosition : Vector3.zero).ToArray();
        }

        /// <summary>Puts a hand/foot bone back at its own position when its PVCopy switch went off. Once per frame, before IK.</summary>
        internal static void CheckPvCopy()
        {
            if (_pvCopyFrame == Time.frameCount) return;
            _pvCopyFrame = Time.frameCount;
            for (int c = 0; c < _sceneCharacters.Count; c++)
            {
                var chara = _sceneCharacters[c];
                if (chara.PvCopyLast == null || !chara.Placed || chara.Oci.preparation == null) continue;
                var pvCopy = chara.Oci.preparation.pvCopy;
                if (pvCopy == null) continue;
                for (int i = 0; i < chara.PvCopyLast.Length; i++)
                {
                    bool on = pvCopy[i];
                    if (chara.PvCopyLast[i] && !on && chara.PvBones[i] != null)
                    {
                        FreeHStudioSceneLoader.Logger.LogInfo($"Character {chara.Name}: PVCopy of {PvCopyBones[i]} went off, local position {chara.PvBones[i].localPosition.ToString("F4")} put back to {chara.PvBoneDefaults[i].ToString("F4")}");
                        chara.PvBones[i].localPosition = chara.PvBoneDefaults[i];
                    }
                    chara.PvCopyLast[i] = on;
                }
            }
        }

        #endregion

        /// <summary>Character.DeleteChara compares with Unity's ==, which also matches other destroyed characters.</summary>
        private static void RemoveCharacter(ChaControl chara)
        {
            _characters.Remove(chara);
            foreach (var kv in Manager.Character.dictEntryChara.ToList())
            {
                if (!ReferenceEquals(kv.Value, chara)) continue;
                Manager.Character.dictEntryChara.Remove(kv.Key);
                Manager.Character.ChaControls.Remove(chara);
                break;
            }
            if (chara != null)
            {
                chara.transform.SetParent(null);
                UnityEngine.Object.Destroy(chara.gameObject);
            }
        }

        /// <summary>
        /// In Studio, GuideObject.LateUpdate sets each object's scale every frame (as a world scale, not inherited from
        /// the parent). The guide objects here are created inactive, so do the same once, parents first.
        /// </summary>
        /// <summary>GuideObject.LateUpdate's scale for one guide object (its scale is animated).</summary>
        internal static void ApplyScale(GuideObject guide)
        {
            if (guide == null || guide.transformTarget == null || !guide.calcScale) return;
            var target = guide.transformTarget;
            var local = target.localScale;
            var lossy = target.lossyScale;
            if (Mathf.Approximately(lossy.x, 0f) || Mathf.Approximately(lossy.y, 0f) || Mathf.Approximately(lossy.z, 0f)) return;
            var scale = guide.enableScale ? guide.changeAmount.scale : Vector3.one;
            target.localScale = new Vector3(local.x / lossy.x * scale.x, local.y / lossy.y * scale.y, local.z / lossy.z * scale.z);
        }

        private static void ApplyScales()
        {
            int Depth(Transform t)
            {
                int d = 0;
                for (; t != null; t = t.parent) d++;
                return d;
            }

            var guides = Singleton<GuideObjectManager>.Instance.dicGuideObject.Values
                .Where(g => g != null && g.transformTarget != null && g.calcScale)
                .OrderBy(g => Depth(g.transformTarget))
                .ToList();
            foreach (var guide in guides)
            {
                var target = guide.transformTarget;
                var local = target.localScale;
                var lossy = target.lossyScale;
                if (Mathf.Approximately(lossy.x, 0f) || Mathf.Approximately(lossy.y, 0f) || Mathf.Approximately(lossy.z, 0f)) continue;
                var scale = guide.enableScale ? guide.changeAmount.scale : Vector3.one;
                target.localScale = new Vector3(local.x / lossy.x * scale.x, local.y / lossy.y * scale.y, local.z / lossy.z * scale.z);
            }
        }

        /// <summary>
        /// The scene position of the first female character (or any character, or the origin), following the
        /// folders and items it is placed under.
        /// </summary>
        private static void FindAnchor(out Vector3 pos, out float yaw, out string name)
        {
            OICharInfo found = null;
            var foundMatrix = Matrix4x4.identity;

            void Visit(IEnumerable<ObjectInfo> infos, Matrix4x4 parent)
            {
                foreach (var info in infos)
                {
                    if (info == null) continue;
                    var ca = info.changeAmount;
                    var m = parent * Matrix4x4.TRS(ca.pos, Quaternion.Euler(ca.rot), ca.scale);
                    if (info is OICharInfo chara)
                    {
                        if (found == null || (found.sex != 1 && chara.sex == 1))
                        {
                            found = chara;
                            foundMatrix = m;
                        }
                    }
                    else if (info is OIFolderInfo folder) Visit(folder.child, m);
                    else if (info is OIItemInfo item) Visit(item.child, m);
                }
            }
            Visit(_sceneInfo.dicObject.Values, Matrix4x4.identity);

            if (found == null)
            {
                pos = Vector3.zero;
                yaw = 0f;
                name = "the scene origin (no characters)";
                return;
            }
            pos = foundMatrix.MultiplyPoint3x4(Vector3.zero);
            var forward = foundMatrix.MultiplyVector(Vector3.forward);
            yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            name = found.sex == 1 ? "the female character" : "the character";
        }

        /// <summary>
        /// Hides what is inside a hidden folder/item, as Studio's TreeNodeCtrl.RefreshVisibleLoop does after loading
        /// (patched out with the rest of the tree UI): the children keep their own switch but are not shown.
        /// </summary>
        private static void HideInsideHiddenNodes()
        {
            int hidden = 0;
            void visit(TreeNodeObject node, bool parentHidden)
            {
                if (node == null) return;
                if (parentHidden)
                {
                    try { node.onVisible?.Invoke(false); hidden++; }
                    catch (Exception ex) { FreeHStudioSceneLoader.Logger.LogDebug($"Hiding a node: {ex.Message}"); }
                }
                bool hide = parentHidden || !node.visible;
                foreach (var child in node.child.ToList()) visit(child, hide);
            }
            foreach (var root in Singleton<Studio.Studio>.Instance.treeNodeCtrl.m_TreeNodeObject.Where(n => n != null && n.parent == null).ToList())
                visit(root, false);
            if (hidden > 0) FreeHStudioSceneLoader.Logger.LogInfo($"Objects inside hidden folders/items hidden: {hidden}");
        }

        #region Shadows and bloom of the scene

        private static int _oldQualityLevel = -1;
        private static Color? _oldAmbientShadow;
        private static Behaviour _occlusion;
        private static bool _oldOcclusionEnabled, _oldConfigOcclusion;
        private static readonly Dictionary<string, object> _oldOcclusionValues = new Dictionary<string, object>();

        /// <summary>
        /// The ambient occlusion of Studio's camera (CharaStudio_Data/level2, AmplifyOcclusionEffect) apart from what
        /// the scene sets (on/off, tint, radius). The game's H camera has its own (e.g. radius 0, power 0, black),
        /// which darkened the edges around the objects much more.
        /// </summary>
        private static readonly KeyValuePair<string, object>[] StudioOcclusion =
        {
            new KeyValuePair<string, object>("SampleCount", "High"),
            new KeyValuePair<string, object>("Intensity", 1f),
            new KeyValuePair<string, object>("PowerExponent", 1f),
            new KeyValuePair<string, object>("Bias", 0.1f),
            new KeyValuePair<string, object>("Downsample", false),
            new KeyValuePair<string, object>("FadeEnabled", false),
            new KeyValuePair<string, object>("BlurEnabled", true),
            new KeyValuePair<string, object>("BlurRadius", 4),
            new KeyValuePair<string, object>("BlurPasses", 4),
            new KeyValuePair<string, object>("BlurSharpness", 20f),
        };

        private static void ApplySceneOcclusion(Camera camera)
        {
            var occlusion = camera != null ? camera.GetComponents<Behaviour>().FirstOrDefault(b => b.GetType().Name == "AmplifyOcclusionEffect") : null;
            if (occlusion == null) return;
            var values = new List<KeyValuePair<string, object>>(StudioOcclusion)
            {
                new KeyValuePair<string, object>("Tint", _sceneInfo.aoeColor),
                new KeyValuePair<string, object>("Radius", _sceneInfo.aoeRadius),
            };
            if (_occlusion == null)
            {
                _occlusion = occlusion;
                _oldOcclusionEnabled = occlusion.enabled;
                _oldOcclusionValues.Clear();
                foreach (var kv in values)
                {
                    var field = HarmonyLib.AccessTools.Field(occlusion.GetType(), kv.Key);
                    if (field != null) _oldOcclusionValues[kv.Key] = field.GetValue(occlusion);
                }
                // The game's config switches it every frame (CameraEffectorConfig.Update) unless told not to
                var config = camera.GetComponent<CameraEffectorConfig>();
                if (config != null)
                {
                    if (_effectorConfig == null) _effectorConfig = config;
                    var use = HarmonyLib.Traverse.Create(config).Field("_useAmplifyOcclus");
                    _oldConfigOcclusion = use.GetValue<bool>();
                    use.SetValue(false);
                }
            }
            foreach (var kv in values)
            {
                var field = HarmonyLib.AccessTools.Field(occlusion.GetType(), kv.Key);
                if (field == null) continue;
                var value = kv.Value is string name && field.FieldType.IsEnum ? Enum.Parse(field.FieldType, name) : kv.Value;
                field.SetValue(occlusion, value);
            }
            occlusion.enabled = _sceneInfo.enableAOE;
            FreeHStudioSceneLoader.Logger.LogInfo($"Scene ambient occlusion {(_sceneInfo.enableAOE ? "on" : "off")}: tint {_sceneInfo.aoeColor}, radius {_sceneInfo.aoeRadius}");
        }

        private static void RestoreSceneOcclusion()
        {
            if (_occlusion != null)
            {
                foreach (var kv in _oldOcclusionValues)
                    HarmonyLib.AccessTools.Field(_occlusion.GetType(), kv.Key)?.SetValue(_occlusion, kv.Value);
                _occlusion.enabled = _oldOcclusionEnabled;
                if (_effectorConfig != null) HarmonyLib.Traverse.Create(_effectorConfig).Field("_useAmplifyOcclus").SetValue(_oldConfigOcclusion);
            }
            _occlusion = null;
            _oldOcclusionValues.Clear();
        }
        private static Behaviour _bloom;
        private static CameraEffectorConfig _effectorConfig;
        private static bool _oldBloomEnabled, _oldConfigBloom;
        private static float _oldBloomIntensity, _oldBloomThreshold, _oldBloomBlur;

        /// <summary>
        /// Studio saves shadows, bloom and the ambient shadow color with the scene; the game uses its graphic settings
        /// instead (self shadow off means a quality level without shadows, so the scene's lights shine through walls).
        /// Use the scene's while it is placed, like Studio's SystemButtonCtrl does.
        /// </summary>
        internal static void ApplySceneEffects(Camera camera)
        {
            if (_sceneInfo == null) return;
            try
            {
                // The game's quality levels come in pairs: even with shadows, odd without (Config.GraphicSetting)
                int level = QualitySettings.GetQualityLevel();
                int wanted = level / 2 * 2 + (_sceneInfo.enableShadow ? 0 : 1);
                if (wanted != level)
                {
                    if (_oldQualityLevel < 0) _oldQualityLevel = level;
                    QualitySettings.SetQualityLevel(wanted, true);
                }
                FreeHStudioSceneLoader.Logger.LogInfo($"Scene shadows {(_sceneInfo.enableShadow ? "on" : "off")}: quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not set the shadows of the scene: {ex.Message}");
            }
            try
            {
                // 「全体の陰影の色」: the shadow color of the toon shaders, items (main_StandardMDK_studio) included
                // (SystemButtonCtrl.SetAmbientShadow)
                if (_oldAmbientShadow == null) _oldAmbientShadow = Shader.GetGlobalColor(ChaShader._ambientshadowG);
                var shadow = _sceneInfo.ambientShadow;
                shadow.a = 1f - _sceneInfo.ambientShadowG;
                Shader.SetGlobalColor(ChaShader._ambientshadowG, shadow);
                FreeHStudioSceneLoader.Logger.LogInfo($"Scene ambient shadow color {shadow}");
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not set the ambient shadow color of the scene: {ex.Message}");
            }
            try
            {
                ApplySceneOcclusion(camera);
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not set the ambient occlusion of the scene: {ex.Message}");
            }
            try
            {
                var bloom = camera != null ? camera.GetComponents<Behaviour>().FirstOrDefault(b => b.GetType().Name == "BloomAndFlares") : null;
                if (bloom == null) return;
                var t = HarmonyLib.Traverse.Create(bloom);
                if (_bloom == null)
                {
                    _bloom = bloom;
                    _oldBloomEnabled = bloom.enabled;
                    _oldBloomIntensity = t.Field("bloomIntensity").GetValue<float>();
                    _oldBloomThreshold = t.Field("bloomThreshold").GetValue<float>();
                    _oldBloomBlur = t.Field("sepBlurSpread").GetValue<float>();
                    // The game's config switches bloom every frame (CameraEffectorConfig.Update) unless told not to
                    if (_effectorConfig == null) _effectorConfig = camera.GetComponent<CameraEffectorConfig>();
                    if (_effectorConfig != null)
                    {
                        _oldConfigBloom = _effectorConfig.useBloom;
                        _effectorConfig.useBloom = false;
                    }
                }
                t.Field("bloomIntensity").SetValue(_sceneInfo.bloomIntensity);
                t.Field("bloomThreshold").SetValue(_sceneInfo.bloomThreshold);
                t.Field("sepBlurSpread").SetValue(_sceneInfo.bloomBlur);
                bloom.enabled = _sceneInfo.enableBloom;
                FreeHStudioSceneLoader.Logger.LogInfo($"Scene bloom {(_sceneInfo.enableBloom ? "on" : "off")}: intensity {_sceneInfo.bloomIntensity}, threshold {_sceneInfo.bloomThreshold}, blur {_sceneInfo.bloomBlur}");
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not set the bloom of the scene: {ex.Message}");
            }
        }

        private static void RestoreSceneEffects()
        {
            try { RestoreSceneOcclusion(); }
            catch (Exception ex) { FreeHStudioSceneLoader.Logger.LogWarning($"Could not restore the ambient occlusion: {ex.Message}"); }
            if (_oldAmbientShadow != null)
            {
                Shader.SetGlobalColor(ChaShader._ambientshadowG, _oldAmbientShadow.Value);
                _oldAmbientShadow = null;
            }
            if (_oldQualityLevel >= 0)
            {
                try { QualitySettings.SetQualityLevel(_oldQualityLevel, true); }
                catch (Exception ex) { FreeHStudioSceneLoader.Logger.LogWarning($"Could not restore the quality level: {ex.Message}"); }
                _oldQualityLevel = -1;
            }
            if (_bloom != null)
            {
                var t = HarmonyLib.Traverse.Create(_bloom);
                t.Field("bloomIntensity").SetValue(_oldBloomIntensity);
                t.Field("bloomThreshold").SetValue(_oldBloomThreshold);
                t.Field("sepBlurSpread").SetValue(_oldBloomBlur);
                _bloom.enabled = _oldBloomEnabled;
            }
            if (_effectorConfig != null && _bloom != null) _effectorConfig.useBloom = _oldConfigBloom;
            _bloom = null;
            _effectorConfig = null;
        }

        #endregion

        private static readonly List<Renderer> _hiddenRenderers = new List<Renderer>();

        /// <summary>Hides the base map's visuals; its H points and colliders keep working.</summary>
        internal static void HideMap(GameObject mapRoot)
        {
            if (mapRoot == null) return;
            foreach (var r in mapRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) continue;
                r.enabled = false;
                _hiddenRenderers.Add(r);
            }
            FreeHStudioSceneLoader.Logger.LogInfo($"Base map hidden ({_hiddenRenderers.Count} renderers)");
        }

        private struct LightState
        {
            public Light Light;
            public int CullingMask;
        }

        private static readonly List<LightState> _limitedLights = new List<LightState>();

        /// <summary>
        /// In Studio, a scene without a map has no light on the layers of items (Map, MapNoShadow): the character
        /// light only lights characters, so the items are lit by the scene's own lights only. The hidden H map's lights
        /// (sun, room lights) stay on in H. Take those layers out of every light that is not part of the studio scene
        /// or of a character (the H characters keep their lighting), and remove the map's ambient light.
        /// </summary>
        internal static void KeepMapLightsOffItems()
        {
            int itemLayers = LayerMask.GetMask("Map", "MapNoShadow");
            foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>())
            {
                if ((light.cullingMask & itemLayers) == 0) continue;
                if (_overlayRoot != null && light.transform.IsChildOf(_overlayRoot.transform)) continue;
                if (light.GetComponentInParent<ChaControl>() != null) continue;
                _limitedLights.Add(new LightState { Light = light, CullingMask = light.cullingMask });
                FreeHStudioSceneLoader.Logger.LogDebug($"Light {light.name} ({light.type}, intensity {light.intensity}, color {light.color}, mask {light.cullingMask:X}) kept off the studio items");
                light.cullingMask &= ~itemLayers;
            }
            FreeHStudioSceneLoader.Logger.LogInfo($"Lights of the H map kept off the studio items: {_limitedLights.Count}");

            // The studio items (Shader Forge/main_StandardMDK_studio, Standard based) are lit by the ambient light
            // (spherical harmonics) too. Studio's own scene (CharaStudio_Data/level2) has none: its ambient probe is
            // all zero, so without a map only the scene's lights light the items. In H the hidden map's ambient
            // (e.g. L0 (0.18, 0.23, 0.30) for the club room) lit the whole room evenly. Use Studio's values.
            // The item shader also adds the ambient color itself (UNITY_LIGHTMODEL_AMBIENT): Studio's is a dark
            // blue gray, the H maps' white, which washed the whole room out.
            if (!_ambientChanged)
            {
                _oldAmbientProbe = RenderSettings.ambientProbe;
                _oldReflectionIntensity = RenderSettings.reflectionIntensity;
                _oldAmbientSky = RenderSettings.ambientSkyColor;
                _oldAmbientEquator = RenderSettings.ambientEquatorColor;
                _oldAmbientGround = RenderSettings.ambientGroundColor;
                _oldAmbientIntensity = RenderSettings.ambientIntensity;
                _ambientChanged = true;
            }
            RenderSettings.ambientProbe = new UnityEngine.Rendering.SphericalHarmonicsL2();
            RenderSettings.reflectionIntensity = 0.7f;
            RenderSettings.ambientSkyColor = StudioAmbientSky;
            RenderSettings.ambientEquatorColor = StudioAmbientEquator;
            RenderSettings.ambientGroundColor = StudioAmbientGround;
            RenderSettings.ambientIntensity = 1.5f;
            FreeHStudioSceneLoader.Logger.LogInfo($"Ambient light set as in Studio: color {RenderSettings.ambientLight}, no ambient probe, reflections x0.7");
        }

        // Studio's own scene (CharaStudio_Data/level2, RenderSettings)
        private static readonly Color StudioAmbientSky = new Color(0.318f, 0.3405f, 0.3885f, 1.5f);
        private static readonly Color StudioAmbientEquator = new Color(0.171f, 0.1875f, 0.1995f, 1.5f);
        private static readonly Color StudioAmbientGround = new Color(0.0705f, 0.0645f, 0.0525f, 1.5f);

        private static bool _ambientChanged;
        private static UnityEngine.Rendering.SphericalHarmonicsL2 _oldAmbientProbe;
        private static float _oldReflectionIntensity, _oldAmbientIntensity;
        private static Color _oldAmbientSky, _oldAmbientEquator, _oldAmbientGround;

        private struct CameraState
        {
            public Camera Camera;
            public CameraClearFlags ClearFlags;
            public Color BackgroundColor;
        }

        private static readonly List<CameraState> _paintedCameras = new List<CameraState>();
        private static Material _oldSkybox;
        private static bool _skyboxRemoved;

        /// <summary>
        /// Studio's camera clears to a solid color; with no map nothing else is drawn there. Do the same instead of
        /// showing the sky of the hidden map: remove the skybox and paint every camera that clears the screen.
        /// </summary>
        internal static void PaintCameraBackground(Camera hCamera)
        {
            var color = FreeHStudioSceneLoader.NoMapBackgroundColor.Value;
            if (!_skyboxRemoved)
            {
                _oldSkybox = RenderSettings.skybox;
                RenderSettings.skybox = null;
                _skyboxRemoved = true;
            }
            foreach (var camera in Camera.allCameras)
            {
                FreeHStudioSceneLoader.Logger.LogDebug($"Camera {camera.name}: depth {camera.depth}, clear {camera.clearFlags}, main {camera == Camera.main}, H {camera == hCamera}");
                if (camera.clearFlags != CameraClearFlags.Skybox && camera.clearFlags != CameraClearFlags.SolidColor) continue;
                if (camera.targetTexture != null) continue;
                _paintedCameras.Add(new CameraState { Camera = camera, ClearFlags = camera.clearFlags, BackgroundColor = camera.backgroundColor });
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = color;
            }
            FreeHStudioSceneLoader.Logger.LogInfo($"Sky removed, {_paintedCameras.Count} camera(s) painted");
        }

        private static void RestoreMap()
        {
            RestoreSceneEffects();
            foreach (var r in _hiddenRenderers) if (r != null) r.enabled = true;
            _hiddenRenderers.Clear();
            foreach (var state in _limitedLights) if (state.Light != null) state.Light.cullingMask = state.CullingMask;
            _limitedLights.Clear();
            if (_ambientChanged)
            {
                RenderSettings.ambientProbe = _oldAmbientProbe;
                RenderSettings.reflectionIntensity = _oldReflectionIntensity;
                RenderSettings.ambientSkyColor = _oldAmbientSky;
                RenderSettings.ambientEquatorColor = _oldAmbientEquator;
                RenderSettings.ambientGroundColor = _oldAmbientGround;
                RenderSettings.ambientIntensity = _oldAmbientIntensity;
                _ambientChanged = false;
            }
            foreach (var state in _paintedCameras)
            {
                if (state.Camera == null) continue;
                state.Camera.clearFlags = state.ClearFlags;
                state.Camera.backgroundColor = state.BackgroundColor;
            }
            _paintedCameras.Clear();
            if (_skyboxRemoved) RenderSettings.skybox = _oldSkybox;
            _skyboxRemoved = false;
            _oldSkybox = null;
        }

        /// <summary>
        /// Removes the objects that are not placed with their parent from the children of a folder, item or character,
        /// recursively. Characters under folders and items are kept for <see cref="LoadCharacters"/>.
        /// </summary>
        private static int RemoveSkippedChildren(ObjectInfo info)
        {
            IEnumerable<List<ObjectInfo>> lists;
            if (info is OIFolderInfo folder) lists = new[] { folder.child };
            else if (info is OIItemInfo item) lists = new[] { item.child };
            else if (info is OICharInfo chara) lists = chara.child.Values;
            else return 0;
            int removed = 0;
            foreach (var children in lists)
            {
                children.RemoveAll(c => c == null);
                foreach (var child in children.Where(c => !ShouldLoad(c)).ToList())
                {
                    children.Remove(child);
                    if (info is OICharInfo || !DeferCharacter(child, info)) removed++;
                }
                foreach (var child in children)
                    removed += RemoveSkippedChildren(child);
            }
            return removed;
        }

        private static bool ShouldLoad(ObjectInfo info)
        {
            switch (info.kind)
            {
                case 1: return true; // item
                case 3: return true; // folder
                case 2: return FreeHStudioSceneLoader.LoadLights.Value; // light
                default: return false; // 0 character, 4 route, 5 camera, 7 text
            }
        }

        /// <summary>
        /// Removes everything placed by <see cref="LoadOverlay"/>. Based on Studio.InitScene without the UI parts.
        /// </summary>
        internal static void UnloadOverlay()
        {
            SceneAnimator.Destroy();
            RestoreMap();
            HideBackground();
            if (!Created) return;
            var studio = Singleton<Studio.Studio>.Instance;
            foreach (var oci in studio.dicObjectCtrl.Values.ToList())
            {
                try
                {
                    if (oci is OCIChar chara)
                    {
                        // Studio's voice list goes on to the next voice when one ends (the audio is not on the
                        // character), so it kept playing after H. OCIChar.OnDelete stops it in Studio.
                        StopVoices(chara);
                        RemoveCharacter(chara.charInfo);
                    }
                    else if (oci?.guideObject?.transformTarget != null)
                        UnityEngine.Object.Destroy(oci.guideObject.transformTarget.gameObject);
                }
                catch (Exception ex)
                {
                    FreeHStudioSceneLoader.Logger.LogWarning($"Cleanup: {ex.Message}");
                }
            }
            foreach (var chara in _characters.ToList())
                RemoveCharacter(chara);
            _pendingCharacters.Clear();
            studio.dicInfo.Clear();
            studio.dicObjectCtrl.Clear();
            studio.dicChangeAmount.Clear();
            Singleton<GuideObjectManager>.Instance.DeleteAll();
            studio.treeNodeCtrl.DeleteAllNode();
            if (_overlayRoot != null) UnityEngine.Object.Destroy(_overlayRoot);
            _overlayRoot = null;
            _moveOffsets.Clear();
            DiscardSceneData();
        }
    }
}
