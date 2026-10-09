// Koikatsu Sunshine port of FreeHStudioSceneLoader.
// Original: IllusionMods/KK_Plugins - FreeHStudioSceneLoader.KK by DeathWeasel1337 (GPL-3.0).
// Modified for Koikatsu Sunshine, 2026. Licensed under GPL-3.0.
//
// Studio UI patches below are carried over from the original plugin. They were checked against the
// Koikatsu Sunshine implementations of the same methods; only the ones needed to place items, folders
// and lights without the Studio UI are kept. All of them do nothing until the Studio host is created.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Studio;
using UnityEngine;
using UnityEngine.UI;

namespace KKS_FreeHStudioSceneLoader
{
    internal static class Hooks
    {
        /// <summary>Run the original method (Studio host not created).</summary>
        private static bool Off => !StudioHost.Created;

        #region Game: Free H screen and H scene

        [HarmonyPostfix, HarmonyPatch(typeof(FreeHScene), nameof(FreeHScene.Start))]
        private static void FreeHScene_Start(FreeHScene __instance)
        {
            // Never let this plugin break the Free H screen
            try { FreeHStudioSceneLoader.Instance.OnFreeHSceneStart(__instance); }
            catch (System.Exception ex) { FreeHStudioSceneLoader.Logger.LogError($"OnFreeHSceneStart: {ex}"); }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(FreeHScene), nameof(FreeHScene.SetMapSprite))]
        private static void FreeHScene_SetMapSprite(FreeHScene __instance, MapInfo.Param _mapInfo)
        {
            if (!Selection.IsSelected || _mapInfo == null) return;
            if (_mapInfo.No != Selection.MapNo)
            {
                // Another map was chosen in the normal map selection. A scene without a map keeps the scene
                // and uses the new map for the H positions; otherwise the scene no longer fits.
                if (Selection.NoMap && _mapInfo.isFreeH)
                    Selection.ChangeBaseMap(_mapInfo);
                else
                {
                    Selection.Clear();
                    Selection.ForgetMapBefore();
                    return;
                }
            }
            ApplySceneThumbnail(__instance);
        }

        internal static void ApplySceneThumbnail(FreeHScene freeH)
        {
            if (!Selection.IsSelected) return;
            Sprite sprite;
            try { sprite = PngAssist.LoadSpriteFromFile(Selection.ScenePath); }
            catch { return; }
            var name = "[Studio] " + Path.GetFileNameWithoutExtension(Selection.ScenePath);
            if (Selection.NoMap) name += $"（H位置: {Selection.BaseMapName}）";
            foreach (var img in new[] { freeH.mapImageNormal, freeH.mapImageMasturbation, freeH.mapImageLesbian, freeH.mapImage3P })
                if (img != null) img.sprite = sprite;
            foreach (var txt in new[] { freeH.mapNameNormal, freeH.mapNameMasturbation, freeH.mapNameLesbian, freeH.mapName3P })
                if (txt != null) txt.text = name;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(HSceneProc), nameof(HSceneProc.Start))]
        private static void HSceneProc_Start(HSceneProc __instance)
        {
            if (!Selection.IsSelected || !StudioHost.HasSceneData) return;
            FreeHStudioSceneLoader.Instance.StartCoroutine(LoadOverlayWhenMapReady(__instance, Selection.MapNo, Selection.NoMap));
        }

        private static IEnumerator LoadOverlayWhenMapReady(HSceneProc proc, int mapNo, bool noMap)
        {
            float timeout = Time.realtimeSinceStartup + 120f;
            while (proc != null && Time.realtimeSinceStartup < timeout)
            {
                var map = proc.map;
                if (map != null && !map.isMapLoading && map.mapRoot != null && map.no == mapNo && proc.flags != null)
                    break;
                yield return null;
            }
            if (proc == null || Time.realtimeSinceStartup >= timeout)
            {
                FreeHStudioSceneLoader.Logger.LogWarning("H map was not loaded in time, studio objects not placed");
                yield break;
            }
            if (!proc.flags.isFreeH)
                yield break;
            try
            {
                var timeZone = Traverse.Create(proc).Field("dataH").Field("timezoneFreeH").GetValue<int>();
                var times = FreeHStudioSceneLoader.TimeNames;
                string Name(int t) => t >= 0 && t < times.Length ? times[t] : t.ToString();
                FreeHStudioSceneLoader.Logger.LogInfo($"H map {proc.map.no} ({proc.map.ConvertMapName(proc.map.no)}), Free H time of day {Name(timeZone)} ({timeZone}), studio scene time of day {Name(StudioHost.SceneSunLightType)} ({StudioHost.SceneSunLightType})");
            }
            catch (System.Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogDebug($"Could not log the H map and time of day: {ex.Message}");
            }
            yield return null;
            var camera = proc.flags.ctrlCamera != null ? proc.flags.ctrlCamera.thisCmaera : Camera.main;
            StudioHost.ShowBackground(camera);
            if (FreeHStudioSceneLoader.UseSceneEffects.Value) StudioHost.ApplySceneEffects(camera);
            if (!noMap)
                StudioHost.LoadOverlay(proc.map.mapRoot.transform);

            // The H start position (initPos/initRot) is final once the characters start to be created
            while (proc != null && proc.lstFemale.Count == 0 && Time.realtimeSinceStartup < timeout)
                yield return null;
            if (proc == null || proc.lstFemale.Count == 0)
            {
                FreeHStudioSceneLoader.Logger.LogWarning("H characters were not created in time, studio objects not placed");
                yield break;
            }
            if (noMap)
            {
                if (FreeHStudioSceneLoader.HideBaseMap.Value)
                {
                    StudioHost.HideMap(proc.map.mapRoot);
                    StudioHost.PaintCameraBackground(camera);
                    if (FreeHStudioSceneLoader.StudioLightingWithoutMap.Value) StudioHost.KeepMapLightsOffItems();
                }
                StudioHost.LoadOverlayAt(proc.initPos, proc.initRot);
            }

            // Scene characters come last, after the game created all of its own characters (MotionIK is set up
            // right after that), so the H characters keep the first character slots
            timeout = Time.realtimeSinceStartup + 120f;
            while (proc != null && (proc.male == null || proc.lstMotionIK.Count == 0) && Time.realtimeSinceStartup < timeout)
                yield return null;
            if (proc == null || proc.male == null)
            {
                FreeHStudioSceneLoader.Logger.LogWarning("H characters were not ready in time, studio characters not placed");
                yield break;
            }
            StudioHost.LoadCharacters();
            StudioHost.StartAnimation();
            StudioHost.ApplySavedLayout();

            // Diagnostics: compare with the same scene in CharaStudio (StudioPoseProbe logs the same lines there)
            yield return new WaitForSeconds(3f);
            if (proc == null || !StudioHost.HasPlacedScene) yield break;
            try
            {
                FreeHStudioSceneLoader.Logger.LogInfo(LightingProbe.Describe("H", StudioHost.SceneRenderers, camera));
            }
            catch (System.Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not log the lighting: {ex.Message}");
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(HSceneProc), nameof(HSceneProc.OnDestroy))]
        private static void HSceneProc_OnDestroy()
        {
            if (Off) return;
            // Set the scene again when the Free H screen comes back
            if (Selection.IsSelected) Selection.ReturnScenePath = Selection.ScenePath;
            StudioHost.UnloadOverlay();
            Selection.Clear();
        }

        #endregion

        #region Studio: Studio

        [HarmonyPrefix, HarmonyPatch(typeof(Studio.Studio), nameof(Studio.Studio.Awake))]
        private static bool Studio_Awake(Studio.Studio __instance)
        {
            // Only the singleton setup; no UI, and no studio option/workspace files are touched
            if (!__instance.CheckInstance()) return false;
            Object.DontDestroyOnLoad(__instance.gameObject);
            Studio.Studio.optionSystem = new OptionSystem("Option");
            if (__instance.workInfo == null) __instance.workInfo = new WorkInfo();
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(Studio.Studio), nameof(Studio.Studio.ChangeCamera), typeof(OCICamera), typeof(bool), typeof(bool))]
        private static bool Studio_ChangeCamera() => Off;

        [HarmonyPrefix, HarmonyPatch(typeof(Studio.Studio), nameof(Studio.Studio.SetSunCaster))]
        private static bool Studio_SetSunCaster() => Off;

        // Do not save studio options/workspace from the main game
        [HarmonyPrefix, HarmonyPatch(typeof(Studio.Studio), nameof(Studio.Studio.OnApplicationQuit))]
        private static bool Studio_OnApplicationQuit() => Off;

        #endregion

        #region Studio: MapCtrl / CameraControl

        [HarmonyPrefix, HarmonyPatch(typeof(MapCtrl), nameof(MapCtrl.Awake))]
        private static bool MapCtrl_Awake(MapCtrl __instance)
        {
            if (Off) return true;
            // Only the singleton setup; the rest wires up the map UI buttons that don't exist here
            __instance.CheckInstance();
            return false;
        }

        // The map is placed by the game; the scene's map offset (caMap) is applied to the objects instead
        [HarmonyPrefix, HarmonyPatch(typeof(MapCtrl), nameof(MapCtrl.Reflect))]
        private static bool MapCtrl_Reflect() => Off;
        [HarmonyPrefix, HarmonyPatch(typeof(MapCtrl), nameof(MapCtrl.UpdateUI))]
        private static bool MapCtrl_UpdateUI() => Off;

        [HarmonyPrefix, HarmonyPatch(typeof(Studio.CameraControl), nameof(Studio.CameraControl.ExportResetData))]
        private static bool CameraControl_ExportResetData(Studio.CameraControl __instance, ref Studio.CameraControl.CameraData __result)
        {
            if (Off || __instance.cameraReset != null) return true;
            __result = new Studio.CameraControl.CameraData();
            return false;
        }

        #endregion

        #region Studio: GuideObject (from the original plugin)

        private static GuideSelect _dummyGuideSelect;

        [HarmonyPrefix, HarmonyPatch(typeof(GuideObject), nameof(GuideObject.guideMove), MethodType.Getter)]
        private static bool GuideObject_guideMove(ref GuideMove[] __result)
        {
            if (Off) return true;
            __result = new GuideMove[0];
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(GuideObject), nameof(GuideObject.guideSelect), MethodType.Getter)]
        private static bool GuideObject_guideSelect(ref GuideSelect __result)
        {
            if (Off) return true;
            if (_dummyGuideSelect == null) _dummyGuideSelect = new GuideSelect();
            __result = _dummyGuideSelect;
            return false;
        }

        // The setters also show or hide the (missing) guide parts; only keep the flag, which decides whether the scale applies
        [HarmonyPrefix, HarmonyPatch(typeof(GuideObject), nameof(GuideObject.enablePos), MethodType.Setter)]
        private static bool GuideObject_enablePos(GuideObject __instance, bool value) => SetEnable(__instance, 0, value);
        [HarmonyPrefix, HarmonyPatch(typeof(GuideObject), nameof(GuideObject.enableRot), MethodType.Setter)]
        private static bool GuideObject_enableRot(GuideObject __instance, bool value) => SetEnable(__instance, 1, value);
        [HarmonyPrefix, HarmonyPatch(typeof(GuideObject), nameof(GuideObject.enableScale), MethodType.Setter)]
        private static bool GuideObject_enableScale(GuideObject __instance, bool value) => SetEnable(__instance, 2, value);

        private static bool SetEnable(GuideObject guide, int index, bool value)
        {
            if (Off) return true;
            if (guide.m_Enables != null && index < guide.m_Enables.Length) guide.m_Enables[index] = value;
            return false;
        }
        [HarmonyPrefix, HarmonyPatch(typeof(GuideObject), nameof(GuideObject.SetMode))]
        private static bool GuideObject_SetMode() => Off;
        [HarmonyPrefix, HarmonyPatch(typeof(GuideObject), nameof(GuideObject.SetScale))]
        private static bool GuideObject_SetScale() => Off;
        [HarmonyPrefix, HarmonyPatch(typeof(GuideObject), nameof(GuideObject.SetVisibleCenter))]
        private static bool GuideObject_SetVisibleCenter() => Off;

        // The guide objects are cloned from an inactive original, so Awake only runs when one is activated later
        // (the IK/FK guides of characters). By then it is set up; resetting dicKey/calcScale like the original Awake
        // would make LateUpdate force the world scale of the character's bones to 1. The rest of Awake is UI.
        [HarmonyPrefix, HarmonyPatch(typeof(GuideObject), nameof(GuideObject.Awake))]
        private static bool GuideObject_Awake() => Off;

        [HarmonyPrefix, HarmonyPatch(typeof(GuideObject), nameof(GuideObject.LateUpdate))]
        private static bool GuideObject_LateUpdate(GuideObject __instance)
        {
            if (Off) return true;
            // KKS LateUpdate with null checks for the guide parts that don't exist here
            if (__instance.transformTarget == null) return false;
            if (__instance.parent && __instance.nonconnect)
            {
                __instance.CalcPosition();
                __instance.CalcRotation();
            }
            __instance.transform.position = __instance.transformTarget.position;
            __instance.transform.rotation = __instance.transformTarget.rotation;
            var root0 = __instance.roots != null && __instance.roots.Length > 0 ? __instance.roots[0] : null;
            if (root0 != null)
            {
                switch (__instance.mode)
                {
                    case GuideObject.Mode.Local:
                        root0.transform.eulerAngles = __instance.parent ? __instance.parent.eulerAngles : Vector3.zero;
                        break;
                    case GuideObject.Mode.LocalIK:
                        root0.transform.localEulerAngles = Vector3.zero;
                        break;
                    case GuideObject.Mode.World:
                        root0.transform.eulerAngles = Vector3.zero;
                        break;
                }
            }
            if (__instance.calcScale)
            {
                var localScale = __instance.transformTarget.localScale;
                var lossyScale = __instance.transformTarget.lossyScale;
                var scale = __instance.enableScale ? __instance.changeAmount.scale : Vector3.one;
                __instance.transformTarget.localScale = new Vector3(localScale.x / lossyScale.x * scale.x, localScale.y / lossyScale.y * scale.y, localScale.z / lossyScale.z * scale.z);
            }
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(OCILight), nameof(OCILight.SetDrawTarget))]
        private static bool OCILight_SetDrawTarget() => Off;

        // Studio sound/voice items start playing as soon as they are enabled; don't let them play during H unless enabled
        [HarmonyPrefix, HarmonyPatch(typeof(Studio.Sound.SEComponent), nameof(Studio.Sound.SEComponent.OnEnable))]
        private static bool SEComponent_OnEnable() => Off || StudioHost.SoundItemsOn;

        #endregion

        #region Studio: GuideObjectManager (from the original plugin)

        [HarmonyPrefix, HarmonyPatch(typeof(GuideObjectManager), nameof(GuideObjectManager.Awake))]
        private static void GuideObjectManager_Awake(GuideObjectManager __instance)
        {
            if (Off) return;
            // The guide object prefab normally comes from the Studio scene; use an empty one
            if (__instance.objectOriginal == null)
            {
                var original = new GameObject("GuideObjectOriginal");
                original.SetActive(false);
                original.AddComponent<GuideObject>();
                Object.DontDestroyOnLoad(original);
                __instance.objectOriginal = original;
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(GuideObjectManager), nameof(GuideObjectManager.DeleteAll))]
        private static bool GuideObjectManager_DeleteAll(GuideObjectManager __instance)
        {
            if (Off) return true;
            __instance.hashSelectObject.Clear();
            __instance.operationTarget = null;
            var objects = new List<GameObject>();
            foreach (var kv in __instance.dicGuideObject)
                if (kv.Value != null) objects.Add(kv.Value.gameObject);
            foreach (var go in objects)
                if (go != null) Object.DestroyImmediate(go);
            __instance.dicGuideObject.Clear();
            __instance.dicTransLight.Clear();
            __instance.dicGuideLight.Clear();
            if (__instance.drawLightLine != null) __instance.drawLightLine.Clear();
            if (__instance.guideInput != null) __instance.guideInput.Stop();
            return false;
        }

        #endregion

        #region Studio: TreeNodeCtrl / TreeNodeObject (from the original plugin)

        [HarmonyPrefix, HarmonyPatch(typeof(TreeNodeCtrl), nameof(TreeNodeCtrl.AddNode), typeof(string), typeof(TreeNodeObject))]
        private static bool TreeNodeCtrl_AddNode(TreeNodeCtrl __instance, TreeNodeObject _parent, ref TreeNodeObject __result)
        {
            if (Off) return true;
            // Node without UI. Awake sets enableAddChild etc.
            var go = new GameObject("TreeNode");
            go.transform.SetParent(StudioHost.TreeNodeRoot, false);
            var node = go.AddComponent<TreeNodeObject>();
            node.m_TreeNodeCtrl = __instance;
            if (_parent != null)
                node.SetParent(_parent);
            else
                __instance.m_TreeNodeObject.Add(node);
            __result = node;
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(TreeNodeCtrl), nameof(TreeNodeCtrl.DeleteAllNode))]
        private static bool TreeNodeCtrl_DeleteAllNode(TreeNodeCtrl __instance)
        {
            if (Off) return true;
            foreach (var node in __instance.m_TreeNodeObject.ToArray())
                if (node != null) Object.Destroy(node.gameObject);
            __instance.m_TreeNodeObject.Clear();
            __instance.hashSelectNode.Clear();
            // Only the node container: other host objects (e.g. the camera control) are siblings of it
            foreach (Transform child in StudioHost.TreeNodeRoot)
                Object.Destroy(child.gameObject);
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(TreeNodeCtrl), nameof(TreeNodeCtrl.Start))]
        private static bool TreeNodeCtrl_Start() => Off;
        [HarmonyPrefix, HarmonyPatch(typeof(TreeNodeCtrl), nameof(TreeNodeCtrl.RefreshHierachyLoop))]
        private static bool TreeNodeCtrl_RefreshHierachyLoop() => Off;
        [HarmonyPrefix, HarmonyPatch(typeof(TreeNodeCtrl), nameof(TreeNodeCtrl.RefreshVisibleLoop))]
        private static bool TreeNodeCtrl_RefreshVisibleLoop() => Off;
        [HarmonyPrefix, HarmonyPatch(typeof(TreeNodeCtrl), nameof(TreeNodeCtrl.SetSelectNode))]
        private static bool TreeNodeCtrl_SetSelectNode() => Off;
        // TreeNodeCtrl.SelectMultiple is overloaded in KKS and is not used when loading, so it is not patched

        [HarmonyPrefix, HarmonyPatch(typeof(TreeNodeObject), nameof(TreeNodeObject.colorSelect), MethodType.Setter)]
        private static bool TreeNodeObject_colorSelect() => Off;
        [HarmonyPrefix, HarmonyPatch(typeof(TreeNodeObject), nameof(TreeNodeObject.textName), MethodType.Setter)]
        private static bool TreeNodeObject_textName_set() => Off;

        [HarmonyPrefix, HarmonyPatch(typeof(TreeNodeObject), nameof(TreeNodeObject.textName), MethodType.Getter)]
        private static bool TreeNodeObject_textName_get(ref string __result)
        {
            if (Off) return true;
            __result = "";
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(TreeNodeObject), nameof(TreeNodeObject.SetVisible))]
        private static bool TreeNodeObject_SetVisible(TreeNodeObject __instance, bool _visible)
        {
            if (Off) return true;
            // Keep the visibility callback (hides items that were hidden in the scene), skip the button UI
            __instance.m_Visible = _visible;
            __instance.onVisible?.Invoke(_visible);
            foreach (var child in __instance.m_child)
                if (child != null) child.SetVisible(_visible);
            return false;
        }

        // Child visibility is handled in TreeNodeObject_SetVisible above; the originals touch the button UI
        [HarmonyPrefix, HarmonyPatch(typeof(TreeNodeObject), nameof(TreeNodeObject.SetVisibleChild))]
        private static bool TreeNodeObject_SetVisibleChild() => Off;
        [HarmonyPrefix, HarmonyPatch(typeof(TreeNodeObject), nameof(TreeNodeObject.SetVisibleLoop))]
        private static bool TreeNodeObject_SetVisibleLoop() => Off;

        [HarmonyPrefix, HarmonyPatch(typeof(TreeNodeObject), nameof(TreeNodeObject.ResetVisible))]
        private static bool TreeNodeObject_ResetVisible(TreeNodeObject __instance)
        {
            if (Off) return true;
            __instance.onVisible?.Invoke(__instance.m_Visible);
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(TreeNodeObject), nameof(TreeNodeObject.enableVisible), MethodType.Setter)]
        private static bool TreeNodeObject_enableVisible() => Off;
        [HarmonyPrefix, HarmonyPatch(typeof(TreeNodeObject), nameof(TreeNodeObject.SetTreeState))]
        private static bool TreeNodeObject_SetTreeState() => Off;

        // Other studio plugins (e.g. KKSUS "OptimizeNEO") postfix the tree state and expect the Studio UI.
        // A prefix cannot stop other plugins' postfixes, so swallow their exceptions while the host is active.
        [HarmonyFinalizer, HarmonyPatch(typeof(TreeNodeObject), nameof(TreeNodeObject.treeState), MethodType.Setter)]
        private static System.Exception TreeNodeObject_treeState_Finalizer(System.Exception __exception) => SwallowIfHost(__exception, "TreeNodeObject.treeState");

        [HarmonyFinalizer, HarmonyPatch(typeof(TreeNodeObject), nameof(TreeNodeObject.SetTreeState))]
        private static System.Exception TreeNodeObject_SetTreeState_Finalizer(System.Exception __exception) => SwallowIfHost(__exception, "TreeNodeObject.SetTreeState");

        // KKSUS also patches these; an exception there would abort the item before its colors/materials are set
        [HarmonyFinalizer, HarmonyPatch(typeof(AddObjectItem), nameof(AddObjectItem.Load), typeof(OIItemInfo), typeof(ObjectCtrlInfo), typeof(TreeNodeObject), typeof(bool), typeof(int))]
        private static System.Exception AddObjectItem_Load_Finalizer(System.Exception __exception) => SwallowIfHost(__exception, "AddObjectItem.Load", true);

        [HarmonyFinalizer, HarmonyPatch(typeof(ItemFKCtrl), nameof(ItemFKCtrl.InitBone))]
        private static System.Exception ItemFKCtrl_InitBone_Finalizer(System.Exception __exception) => SwallowIfHost(__exception, "ItemFKCtrl.InitBone", true);

        private static readonly HashSet<string> _reportedSwallowed = new HashSet<string>();

        private static System.Exception SwallowIfHost(System.Exception exception, string where, bool important = false)
        {
            if (exception == null || Off) return exception;
            if (_reportedSwallowed.Add(where))
            {
                var msg = $"Ignored an exception on {where} (probably another plugin's patch expecting the Studio UI): {exception}";
                if (important) FreeHStudioSceneLoader.Logger.LogWarning(msg);
                else FreeHStudioSceneLoader.Logger.LogDebug(msg);
            }
            return null;
        }

        #endregion

        #region Character cards inside Koikatsu-format scenes

        private const string KKSCharaMark = "【KoiKatuCharaSun】";

        // The main game's ChaFile.LoadFile(BinaryReader) stops when the card mark is not the KKS one, leaving the
        // reader in the middle of the card, so every following object in a Koikatsu-format scene is misread.
        // Studio's version of the same method does not check the mark. Do the same while loading a scene.
        [HarmonyTranspiler, HarmonyPatch(typeof(ChaFile), nameof(ChaFile.LoadFile), typeof(BinaryReader), typeof(bool), typeof(bool))]
        private static IEnumerable<CodeInstruction> ChaFile_LoadFile_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var inequality = AccessTools.Method(typeof(string), "op_Inequality", new[] { typeof(string), typeof(string) });
            var list = new List<CodeInstruction>(instructions);
            var found = false;
            for (int i = 1; i < list.Count; i++)
            {
                if (list[i - 1].opcode == System.Reflection.Emit.OpCodes.Ldstr && (string)list[i - 1].operand == KKSCharaMark && list[i].Calls(inequality))
                {
                    list[i].operand = AccessTools.Method(typeof(Hooks), nameof(CharaMarkMismatch));
                    found = true;
                    break;
                }
            }
            if (!found) throw new System.InvalidOperationException("Card mark check not found in ChaFile.LoadFile");
            return list;
        }

        private static bool CharaMarkMismatch(string mark, string expected)
        {
            if (mark == expected) return false;
            return !(StudioHost.IsLoadingScene && System.Linq.Enumerable.Contains(ChaFileDefine.KoikatsuCharaFileMarkCheck, mark));
        }

        #endregion

        #region Scene characters

        // Studio's order (see SceneAnimator): KKPE bones go in before IK solves; Timeline's late values and the
        // constraints after IK/FK and before the dynamic bones (hair etc.) simulate. Whichever of these
        // LateUpdates comes first in the frame starts the phase. The PVCopy check (StudioHost.CheckPvCopy) goes
        // before IK as well.
        [HarmonyPrefix, HarmonyPatch(typeof(RootMotion.SolverManager), "LateUpdate")]
        private static void SolverManager_LateUpdate() => BeforeIK();
        [HarmonyPrefix, HarmonyPatch(typeof(NeckLookControllerVer2), "LateUpdate")]
        private static void NeckLookControllerVer2_LateUpdate() => BeforeIK();
        [HarmonyPrefix, HarmonyPatch(typeof(FKCtrl), "LateUpdate")]
        private static void FKCtrl_LateUpdate() => BeforeIK();
        [HarmonyPrefix, HarmonyPatch(typeof(DynamicBone_Ver02), "LateUpdate")]
        private static void DynamicBone_Ver02_LateUpdate() => SceneAnimator.RunLatePhase();
        [HarmonyPrefix, HarmonyPatch(typeof(DynamicBone), "LateUpdate")]
        private static void DynamicBone_LateUpdate() => SceneAnimator.RunLatePhase();

        private static void BeforeIK()
        {
            StudioHost.CheckPvCopy();
            SceneAnimator.RunPosePhase();
        }

        private const string GameBoneBundle = "chara/oo_base.unity3d";
        private const string StudioBoneBundle = "studio/base/00.unity3d";

        // Studio's ChaControl loads the body and head bone prefabs from studio/base/00.unity3d. Those carry the
        // Studio components (Preparation, CharAnimeCtrl, IKCtrl, PVCopy) that posing a studio character needs;
        // the game's copies in chara/oo_base.unity3d don't. Use Studio's bundle while a scene character is created.
        // AddObjectFemale/Male create it with ChaControl.Load, which runs LoadNoAsync.
        [HarmonyTranspiler, HarmonyPatch(typeof(ChaControl), nameof(ChaControl.LoadNoAsync))]
        private static IEnumerable<CodeInstruction> ChaControl_LoadNoAsync_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            int replaced = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].opcode != System.Reflection.Emit.OpCodes.Ldstr || (string)list[i].operand != GameBoneBundle) continue;
                // e.g. text = "chara/oo_base.unity3d"; text2 = hiPoly ? "p_cf_body_bone" : "p_cf_body_bone_low";
                bool loadsBones = false;
                for (int j = i + 1; j < list.Count && j <= i + 10; j++)
                    if (list[j].opcode == System.Reflection.Emit.OpCodes.Ldstr && list[j].operand is string asset && asset.StartsWith("p_c") && asset.Contains("_bone"))
                        loadsBones = true;
                if (!loadsBones) continue;
                list.Insert(i + 1, new CodeInstruction(System.Reflection.Emit.OpCodes.Call, AccessTools.Method(typeof(Hooks), nameof(BoneBundle))));
                replaced++;
            }
            // Expected: body bones and head bones. Characters can't be posed otherwise, but the game is not affected.
            if (replaced != 2) FreeHStudioSceneLoader.Logger.LogWarning($"ChaControl.LoadNoAsync: {replaced} bone bundle loads found instead of 2");
            return list;
        }

        private static string BoneBundle(string bundle) => StudioHost.IsLoadingCharacter ? StudioBoneBundle : bundle;

        // In H, ChaControl.UpdateVisible applies the H options (male body visible, silhouette) to every male.
        // Scene characters follow their own settings like in Studio instead: the check for the H scene is
        // answered with false for them.
        [HarmonyTranspiler, HarmonyPatch(typeof(ChaControl), nameof(ChaControl.UpdateVisible))]
        private static IEnumerable<CodeInstruction> ChaControl_UpdateVisible_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var any = AccessTools.GetDeclaredMethods(typeof(System.Linq.Enumerable))
                .First(m => m.Name == nameof(System.Linq.Enumerable.Any) && m.GetParameters().Length == 2)
                .MakeGenericMethod(typeof(string));
            var list = new List<CodeInstruction>(instructions);
            int replaced = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].Calls(any)) continue;
                // Pass `this` as an extra argument; labels move to the inserted instruction so jumps still push it
                var self = new CodeInstruction(System.Reflection.Emit.OpCodes.Ldarg_0);
                self.labels.AddRange(list[i].labels);
                list[i].labels.Clear();
                list[i].operand = AccessTools.Method(typeof(Hooks), nameof(IsInHForVisibility));
                list.Insert(i, self);
                i++;
                replaced++;
            }
            if (replaced != 1) FreeHStudioSceneLoader.Logger.LogWarning($"ChaControl.UpdateVisible: {replaced} scene checks found instead of 1");
            return list;
        }

        private static bool IsInHForVisibility(IEnumerable<string> sceneNames, System.Func<string, bool> predicate, ChaControl chara) =>
            System.Linq.Enumerable.Any(sceneNames, predicate) && !StudioHost.IsSceneCharacter(chara);

        #endregion

        #region Studio: tree nodes (continued)

        [HarmonyPrefix, HarmonyPatch(typeof(TreeNodeObject), nameof(TreeNodeObject.Select))]
        private static bool TreeNodeObject_Select() => Off;
        [HarmonyPrefix, HarmonyPatch(typeof(TreeNodeObject), nameof(TreeNodeObject.SetStateVisible))]
        private static bool TreeNodeObject_SetStateVisible() => Off;
        [HarmonyPrefix, HarmonyPatch(typeof(TreeNodeObject), nameof(TreeNodeObject.RecalcSelectButtonPos))]
        private static bool TreeNodeObject_RecalcSelectButtonPos() => Off;

        #endregion
    }
}
