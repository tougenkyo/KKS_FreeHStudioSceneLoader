// Koikatsu Sunshine port of FreeHStudioSceneLoader.
// Modified for Koikatsu Sunshine, 2026. Licensed under GPL-3.0.
//
// MaterialEditor's edits of the scene's items (shaders, colors, values, textures, renderer and projector settings).
// MaterialEditor only loads them in Studio (its SceneController is registered with KKAPI's Studio save/load API), so
// items kept their original materials in H (e.g. additive projectors and effect shaders drawing white). Its own
// SceneController.OnSceneLoad applies them to the objects it is given by dicKey; call it with the placed objects.
// MaterialEditor is optional: everything goes through reflection.
using System;
using System.Collections.Generic;
using System.Linq;
using ExtensibleSaveFormat;
using HarmonyLib;
using Studio;
using UnityEngine;

namespace KKS_FreeHStudioSceneLoader
{
    internal static class SceneMaterials
    {
        private const string DataId = "com.deathweasel.bepinex.materialeditor";
        private static PluginData _data;
        private static GameObject _controllerObject;

        /// <summary>Keeps the scene's MaterialEditor data: it is only available right after the scene was read.</summary>
        internal static void Read()
        {
            try { _data = ExtendedSave.GetSceneExtendedDataById(DataId); }
            catch (Exception ex)
            {
                _data = null;
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not read the MaterialEditor data of the scene: {ex.Message}");
            }
        }

        internal static void Clear() => _data = null;

        /// <summary>Applies the edits to the placed objects. Call once after all objects (also those on characters) were placed.</summary>
        internal static void Apply(Transform parent)
        {
            if (_data == null) return;
            var type = AccessTools.TypeByName("KK_Plugins.MaterialEditor.SceneController");
            var dictType = AccessTools.TypeByName("KKAPI.Utilities.ReadOnlyDictionary`2");
            var kindType = AccessTools.TypeByName("KKAPI.Studio.SaveLoad.SceneOperationKind");
            if (type == null || dictType == null || kindType == null)
            {
                FreeHStudioSceneLoader.Logger.LogInfo("The scene has MaterialEditor edits, but MaterialEditor was not found");
                return;
            }
            PluginData old = null;
            bool swapped = false;
            try
            {
                if (_controllerObject == null)
                {
                    _controllerObject = new GameObject("FreeHStudioSceneLoader_MaterialEditor");
                    if (parent != null) _controllerObject.transform.SetParent(parent, false);
                }
                var controller = (Behaviour)(_controllerObject.GetComponent(type) ?? _controllerObject.AddComponent(type));
                // Its Update handles Studio's paste hotkey on the selected tree nodes
                controller.enabled = false;
                AccessTools.Property(type, "ExtendedDataId").GetSetMethod(true).Invoke(controller, new object[] { DataId });

                var placed = Singleton<Studio.Studio>.Instance.dicObjectCtrl
                    .Where(kv => kv.Value is OCIItem item && item.objectItem != null)
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
                var items = Activator.CreateInstance(dictType.MakeGenericType(typeof(int), typeof(ObjectCtrlInfo)), placed);

                // GetExtendedData reads the scene data ExtendedSave holds now, which may be another scene's by now
                old = ExtendedSave.GetSceneExtendedDataById(DataId);
                ExtendedSave.SetSceneExtendedDataById(DataId, _data);
                swapped = true;
                AccessTools.Method(type, "OnSceneLoad").Invoke(controller, new[] { Enum.Parse(kindType, "Load"), items });
                FreeHStudioSceneLoader.Logger.LogInfo($"MaterialEditor edits of the scene applied ({placed.Count} items)");
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not apply the MaterialEditor edits of the scene: {(ex.InnerException ?? ex).Message}");
            }
            finally
            {
                if (swapped)
                {
                    try { ExtendedSave.SetSceneExtendedDataById(DataId, old); }
                    catch { }
                }
            }
        }
    }
}
