// Koikatsu Sunshine port of FreeHStudioSceneLoader.
// Modified for Koikatsu Sunshine, 2026. Licensed under GPL-3.0.
//
// Diagnostics: the lighting a studio scene's items get, logged the same way in H and in CharaStudio (the
// StudioPoseProbe tool links this file) so the two can be compared.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace KKS_FreeHStudioSceneLoader
{
    internal static class LightingProbe
    {
        private static string C(Color c) => $"({c.r:0.###}, {c.g:0.###}, {c.b:0.###}, {c.a:0.###})";

        private static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (int i = 0; t != null && i < 4; i++, t = t.parent) parts.Insert(0, t.name);
            return (t != null ? ".../" : "") + string.Join("/", parts.ToArray());
        }

        private static string Layers(int mask)
        {
            var names = new List<string>();
            for (int i = 0; i < 32; i++)
                if ((mask & (1 << i)) != 0) names.Add(string.IsNullOrEmpty(LayerMask.LayerToName(i)) ? i.ToString() : LayerMask.LayerToName(i));
            return mask == -1 ? "Everything" : string.Join("|", names.ToArray());
        }

        private static readonly HashSet<string> EffectNames = new HashSet<string>
        {
            "AmplifyColorEffect", "AmplifyOcclusionEffect", "BloomAndFlares", "VignetteAndChromaticAberration", "DepthOfField", "GlobalFog", "SunShafts", "CrossFade",
        };

        /// <summary>The simple public values of a component (numbers, switches, colors, texture names).</summary>
        private static string Values(object obj)
        {
            var parts = new List<string>();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly;
            for (var type = obj.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
            {
                foreach (var field in type.GetFields(flags))
                {
                    try { parts.Add(field.Name + "=" + Value(field.FieldType, field.GetValue(obj))); }
                    catch { }
                }
            }
            return string.Join(", ", parts.Where(p => !p.EndsWith("=?")).ToArray());
        }

        private static string Value(Type type, object value)
        {
            if (value == null) return type.IsSubclassOf(typeof(UnityEngine.Object)) ? "none" : "?";
            if (value is float f) return f.ToString("0.###");
            if (value is bool || value is int || type.IsEnum) return value.ToString();
            if (value is Color c) return C(c);
            if (value is Vector2 || value is Vector3 || value is Vector4) return value.ToString();
            if (value is UnityEngine.Object o) return o != null ? o.name : "none";
            return "?";
        }

        /// <summary>Everything that decides how the items look, as text lines.</summary>
        internal static string Describe(string side, IEnumerable<Renderer> sceneRenderers, Camera camera)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[Lighting {side}]");
            sb.AppendLine($"  ambient mode {RenderSettings.ambientMode}, light {C(RenderSettings.ambientLight)}, sky {C(RenderSettings.ambientSkyColor)}, equator {C(RenderSettings.ambientEquatorColor)}, ground {C(RenderSettings.ambientGroundColor)}, intensity {RenderSettings.ambientIntensity:0.###}");
            var sh = RenderSettings.ambientProbe;
            sb.AppendLine($"  ambient probe L0 ({sh[0, 0]:0.###}, {sh[1, 0]:0.###}, {sh[2, 0]:0.###}), L1 r ({sh[0, 1]:0.###}, {sh[0, 2]:0.###}, {sh[0, 3]:0.###})");
            sb.AppendLine($"  skybox {(RenderSettings.skybox != null ? RenderSettings.skybox.name + " / " + RenderSettings.skybox.shader.name : "none")}, reflection {RenderSettings.defaultReflectionMode} x{RenderSettings.reflectionIntensity:0.###}, fog {RenderSettings.fog} {RenderSettings.fogMode} {C(RenderSettings.fogColor)} {RenderSettings.fogStartDistance:0.#}-{RenderSettings.fogEndDistance:0.#}");
            sb.AppendLine($"  quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}, pixel lights {QualitySettings.pixelLightCount}, shadows {QualitySettings.shadows} {QualitySettings.shadowDistance:0.#}m, color space {QualitySettings.activeColorSpace}");
            try
            {
                sb.AppendLine($"  globals ambientshadowG {C(Shader.GetGlobalColor("_ambientshadowG"))}, LineColorG {C(Shader.GetGlobalColor("_LineColorG"))}, linewidthG {Shader.GetGlobalFloat("_linewidthG"):0.###}, KanoVerShift {Shader.GetGlobalFloat("_KanoVerShift"):0.###}, RampG {(Shader.GetGlobalTexture("_RampG") != null ? Shader.GetGlobalTexture("_RampG").name : "none")}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  globals: {ex.Message}");
            }
            if (camera != null)
            {
                var effects = camera.GetComponents<Behaviour>().Where(b => !(b is Camera)).Select(b => b.GetType().Name + (b.enabled ? "" : "(off)"));
                sb.AppendLine($"  camera {camera.name}: path {camera.actualRenderingPath}, HDR {camera.allowHDR}, mask {Layers(camera.cullingMask)}, effects {string.Join(", ", effects.ToArray())}");
                foreach (var effect in camera.GetComponents<Behaviour>().Where(b => b.enabled && EffectNames.Contains(b.GetType().Name)))
                    sb.AppendLine($"    {effect.GetType().Name}: {Values(effect)}");
            }
            // Color grading/effect volumes placed in the scene (they change the camera's effects when it is inside)
            foreach (var volume in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>().Where(b => b.GetType().Name.Contains("Volume")))
                sb.AppendLine($"  volume {volume.GetType().Name} at {PathOf(volume.transform)}, on {volume.enabled}: {Values(volume)}");
            foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>())
                sb.AppendLine($"  light {PathOf(light.transform)}: {light.type}, on {light.enabled}, intensity {light.intensity:0.###}, color {C(light.color)}, range {light.range:0.##}, mask {Layers(light.cullingMask)}, shadows {light.shadows}, mode {light.renderMode}, pos {light.transform.position}");
            var renderers = sceneRenderers.Where(r => r != null).ToList();
            sb.AppendLine($"  scene renderers {renderers.Count} (enabled {renderers.Count(r => r.enabled && r.gameObject.activeInHierarchy)})");
            foreach (var group in renderers.GroupBy(r => r.gameObject.layer).OrderBy(g => g.Key))
                sb.AppendLine($"    layer {LayerMask.LayerToName(group.Key)} ({group.Key}): {group.Count()}");
            foreach (var group in renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null && m.shader != null).GroupBy(m => m.shader.name).OrderByDescending(g => g.Count()).Take(12))
                sb.AppendLine($"    shader {group.Key}: {group.Count()} material(s)");
            var text = sb.ToString();
            // Studio and the game overwrite the same output_log.txt on launch: keep the last one of each side
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, $"FreeHStudioLighting_{side}.txt"), DateTime.Now + Environment.NewLine + text);
            }
            catch { }
            return text;
        }
    }
}
