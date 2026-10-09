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
            public Vector3 OriginalPos;
            public Vector3 OriginalRot;
            /// <summary>PVCopy's switches in the last frame, and its hand/foot bones with their positions before it ever ran.</summary>
            public bool[] PvCopyLast;
            public Transform[] PvBones;
            public Vector3[] PvBoneDefaults;

            public bool Placed => Oci != null && Oci.charInfo != null;
        }

        private static readonly List<SceneCharacter> _sceneCharacters = new List<SceneCharacter>();

        /// <summary>The characters of the selected scene, in the order of the scene's tree.</summary>
        internal static IList<SceneCharacter> SceneCharacters => _sceneCharacters;

        internal static bool HasPlacedCharacters => _sceneCharacters.Any(c => c.Placed);
        /// <summary>The scene is placed in the current H scene (with or without characters).</summary>
        internal static bool HasPlacedScene => _overlayRoot != null;
        /// <summary>Where the scene's origin is now (the pivot to turn a scene without characters around).</summary>
        internal static Vector3 SceneOrigin => _overlayRoot != null ? _overlayRoot.transform.position : Vector3.zero;

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
                            Name = string.IsNullOrEmpty(chara.charFile?.parameter?.fullname) ? "(名前なし)" : chara.charFile.parameter.fullname,
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
                reason = "このシーンにはスタジオのマップがありません。";
                return none;
            }
            if (!Singleton<Info>.Instance.dicMapLoadInfo.TryGetValue(studioMapNo, out var mapLoadInfo))
            {
                reason = $"スタジオのマップ（No {studioMapNo}）がインストールされていません。";
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
                reason = $"シーンのマップ「{mapLoadInfo.name}」（{mapLoadInfo.bundlePath}）に対応するFree-H用マップがありません。";
                return none;
            }
            var freeH = matches.Where(p => p.isFreeH).ToList();
            if (freeH.Count == 0)
            {
                reason = $"シーンのマップ「{matches[0].DisplayName}」はFree-Hでは使えません。";
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

            int loaded = 0, skipped = 0;
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
                    }
                }
                catch (Exception ex)
                {
                    FreeHStudioSceneLoader.Logger.LogWarning($"Failed to load studio object (kind {info.kind}, key {info.dicKey}): {ex}");
                }
            }
            ApplyScales();
            FreeHStudioSceneLoader.Logger.LogInfo($"Studio scene objects placed: {loaded} (skipped {skipped} cameras/routes/texts/characters turned off), {_pendingCharacters.Count} character(s) to load");
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
            FreeHStudioSceneLoader.Logger.LogInfo($"Studio scene characters placed: {loaded}");
            if (Settings != null && Settings.CharaVoices) PlayVoices();
            if (loaded > 0)
                FreeHStudioSceneLoader.Logger.LogMessage($"スタジオシーンのキャラクター: {FreeHStudioSceneLoader.WindowKey.Value} キーで表示の切り替えと位置の調整ができます");
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
                    FreeHStudioSceneLoader.Logger.LogMessage($"スタジオシーンの配置: スロット{slot + 1}（最後に保存したもの）を再現しました");
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
            foreach (var r in _hiddenRenderers) if (r != null) r.enabled = true;
            _hiddenRenderers.Clear();
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
