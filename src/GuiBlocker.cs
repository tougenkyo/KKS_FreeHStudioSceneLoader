// Koikatsu Sunshine port of FreeHStudioSceneLoader.
// Modified for Koikatsu Sunshine, 2026. Licensed under GPL-3.0.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KKS_FreeHStudioSceneLoader
{
    /// <summary>
    /// IMGUI windows don't stop clicks from reaching the game's uGUI behind them. This puts transparent images
    /// under the plugin's windows and buttons on a top-most overlay canvas, so the EventSystem hits them instead
    /// of the game's UI (IMGUI itself does not go through the EventSystem and keeps working).
    /// </summary>
    internal static class GuiBlocker
    {
        private static Canvas _canvas;
        private static readonly List<RectTransform> Blocks = new List<RectTransform>();

        /// <summary>Covers exactly these screen rects (IMGUI coordinates: origin top left). Call once per frame.</summary>
        internal static void SetRects(List<Rect> rects)
        {
            if (_canvas == null)
            {
                if (rects.Count == 0) return;
                Create();
            }
            while (Blocks.Count < rects.Count) Blocks.Add(CreateBlock());
            for (int i = 0; i < Blocks.Count; i++)
            {
                var block = Blocks[i];
                bool on = i < rects.Count;
                if (block.gameObject.activeSelf != on) block.gameObject.SetActive(on);
                if (!on) continue;
                var r = rects[i];
                block.anchoredPosition = new Vector2(r.x, Screen.height - r.yMax);
                block.sizeDelta = new Vector2(r.width, r.height);
            }
        }

        private static void Create()
        {
            var go = new GameObject("FreeHStudioSceneLoader_GuiBlocker");
            Object.DontDestroyOnLoad(go);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Overlay canvases are raycast by their sorting order, before any camera-space canvas
            _canvas.sortingOrder = short.MaxValue;
            go.AddComponent<GraphicRaycaster>();
            Blocks.Clear();
        }

        private static RectTransform CreateBlock()
        {
            var go = new GameObject("Block", typeof(RectTransform));
            go.transform.SetParent(_canvas.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            var image = go.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;
            return rt;
        }
    }
}
