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
            studioMapNo = sceneInfo.map;
            return true;
        }

        internal static void DiscardSceneData()
        {
            _sceneInfo = null;
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
                reason = "このシーンにはスタジオのマップがありません（アイテムで背景を作ったシーン）。まだ対応していません。";
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
        /// Places the scene's items, folders and lights. Call after the H map has been loaded.
        /// </summary>
        internal static void LoadOverlay()
        {
            if (_sceneInfo == null) return;
            var studio = Singleton<Studio.Studio>.Instance;
            studio.sceneInfo = _sceneInfo;

            // Studio moves the map by caMap; in H the map stays at the origin, so move the objects the other way
            _overlayRoot = new GameObject("FreeHStudioSceneLoader_Objects");
            var mapRot = Quaternion.Euler(_sceneInfo.caMap.rot);
            var inv = Quaternion.Inverse(mapRot);
            _overlayRoot.transform.rotation = inv;
            _overlayRoot.transform.position = inv * -_sceneInfo.caMap.pos;

            int loaded = 0, skipped = 0;
            foreach (var info in _sceneInfo.dicObject.Values.ToList())
            {
                if (!ShouldLoad(info))
                {
                    skipped++;
                    continue;
                }
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
            FreeHStudioSceneLoader.Logger.LogInfo($"Studio scene objects placed: {loaded} (skipped {skipped} characters/cameras/routes/texts)");
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
