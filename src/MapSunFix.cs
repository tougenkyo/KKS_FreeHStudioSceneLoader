// Koikatsu Sunshine port of FreeHStudioSceneLoader.
// Modified for Koikatsu Sunshine, 2026. Licensed under GPL-3.0.
//
// Some mod maps save a sun light (Lighting settings, RenderSettings.sun) that is not a light: in the kPlug maps
// (map 97/98, scene "StaffRoom") it points to a SphereCollider. Unity reads it as a light when it draws with shadows
// (the scene's shadows turn them on) and the game crashes in UnityPlayer.dll (reading 0x148). Point it to the map's
// own directional light instead, when a scene becomes active.
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KKS_FreeHStudioSceneLoader
{
    internal static class MapSunFix
    {
        internal static void Install()
        {
            SceneManager.activeSceneChanged += (from, to) => Check(to);
        }

        private static void Check(Scene scene)
        {
            try
            {
                object sun = RenderSettings.sun;
                if (ReferenceEquals(sun, null) || sun is Light) return;
                string what = sun is UnityEngine.Object obj ? $"{obj.GetType().Name} \"{obj.name}\"" : sun.GetType().Name;
                var light = scene.IsValid() && scene.isLoaded
                    ? scene.GetRootGameObjects()
                        .SelectMany(go => go.GetComponentsInChildren<Light>(true))
                        .Where(l => l.type == LightType.Directional)
                        .OrderByDescending(l => l.isActiveAndEnabled)
                        .ThenByDescending(l => l.intensity)
                        .FirstOrDefault()
                    : null;
                RenderSettings.sun = light;
                FreeHStudioSceneLoader.Logger.LogInfo($"Scene {scene.name}: its sun light was a {what}, not a light (broken map data that crashes the game with shadows on); set to {(light != null ? $"\"{light.name}\"" : "none")}");
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not check the sun light of scene {scene.name}: {ex.Message}");
            }
        }
    }
}
