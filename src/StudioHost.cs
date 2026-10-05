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
            _backgroundPng = null;
            if (!Created) return;
            try { Singleton<Studio.Studio>.Instance.sceneInfo = new SceneInfo(); }
            catch (Exception ex) { FreeHStudioSceneLoader.Logger.LogWarning($"Could not reset the studio scene info: {ex.Message}"); }
        }

        /// <summary>
        /// Studio and Free H use different map lists. Match them by the scene asset (bundle + scene name).
        /// </summary>
        internal static MapInfo.Param FindFreeHMap(BaseMap baseMap, int studioMapNo, out string reason) =>
            FindFreeHMap(baseMap, studioMapNo, out reason, out _);

        internal static MapInfo.Param FindFreeHMap(BaseMap baseMap, int studioMapNo, out string reason, out bool approximate)
        {
            reason = null;
            approximate = false;
            if (studioMapNo < 0)
            {
                reason = "このシーンにはスタジオのマップがありません。";
                return null;
            }
            if (!Singleton<Info>.Instance.dicMapLoadInfo.TryGetValue(studioMapNo, out var mapLoadInfo))
            {
                reason = $"スタジオのマップ（No {studioMapNo}）がインストールされていません。";
                return null;
            }
            var match = baseMap.infoDic.Values.FirstOrDefault(p =>
                string.Equals(p.AssetBundleName, mapLoadInfo.bundlePath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.AssetName, mapLoadInfo.fileName, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                // Many KKS island maps use a separate "xxx_studio" scene in Studio; fall back to the scene name
                // without that suffix. The two versions may not be laid out identically.
                var key = StripStudioSuffix(mapLoadInfo.fileName);
                var candidates = baseMap.infoDic.Values.Where(p => p.isFreeH &&
                    string.Equals(StripStudioSuffix(p.AssetName), key, StringComparison.OrdinalIgnoreCase)).ToList();
                if (candidates.Count == 1)
                {
                    approximate = true;
                    match = candidates[0];
                }
            }
            if (match == null)
            {
                reason = $"シーンのマップ「{mapLoadInfo.name}」（{mapLoadInfo.bundlePath}）に対応するFree-H用マップがありません。";
                return null;
            }
            if (!match.isFreeH)
            {
                reason = $"シーンのマップ「{match.DisplayName}」はFree-Hでは使えません。";
                return null;
            }
            return match;
        }

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
        internal static void LoadOverlay()
        {
            if (_sceneInfo == null) return;
            // Studio moves the map by caMap; in H the map stays at the origin, so move the objects the other way
            var inv = Quaternion.Inverse(Quaternion.Euler(_sceneInfo.caMap.rot));
            LoadOverlay(inv * -_sceneInfo.caMap.pos, inv);
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

            int loaded = 0, skipped = 0;
            foreach (var info in _sceneInfo.dicObject.Values.ToList())
            {
                if (!ShouldLoad(info))
                {
                    skipped++;
                    continue;
                }
                // Folders and items load their children too; characters inside them would fail and abort the folder
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
            FreeHStudioSceneLoader.Logger.LogInfo($"Studio scene objects placed: {loaded} (skipped {skipped} characters/cameras/routes/texts)");
        }

        /// <summary>
        /// In Studio, GuideObject.LateUpdate sets each object's scale every frame (as a world scale, not inherited from
        /// the parent). The guide objects here are created inactive, so do the same once, parents first.
        /// </summary>
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

        /// <summary>Removes the objects that are not placed from the children of a folder or item, recursively.</summary>
        private static int RemoveSkippedChildren(ObjectInfo info)
        {
            List<ObjectInfo> children;
            if (info is OIFolderInfo folder) children = folder.child;
            else if (info is OIItemInfo item) children = item.child;
            else return 0;
            int removed = children.RemoveAll(c => c == null || !ShouldLoad(c));
            foreach (var child in children)
                removed += RemoveSkippedChildren(child);
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
            RestoreMap();
            HideBackground();
            if (!Created) return;
            var studio = Singleton<Studio.Studio>.Instance;
            foreach (var oci in studio.dicObjectCtrl.Values.ToList())
            {
                try
                {
                    if (oci?.guideObject?.transformTarget != null)
                        UnityEngine.Object.Destroy(oci.guideObject.transformTarget.gameObject);
                }
                catch (Exception ex)
                {
                    FreeHStudioSceneLoader.Logger.LogWarning($"Cleanup: {ex.Message}");
                }
            }
            studio.dicInfo.Clear();
            studio.dicObjectCtrl.Clear();
            studio.dicChangeAmount.Clear();
            Singleton<GuideObjectManager>.Instance.DeleteAll();
            studio.treeNodeCtrl.DeleteAllNode();
            if (_overlayRoot != null) UnityEngine.Object.Destroy(_overlayRoot);
            _overlayRoot = null;
            DiscardSceneData();
        }
    }
}
