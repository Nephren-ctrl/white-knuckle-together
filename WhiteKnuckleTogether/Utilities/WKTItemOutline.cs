using System.Collections.Generic;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The game's item outline: one of an item mesh's materials (usually its second, <c>MAT_Outline</c>) drawn with the outline
    /// shader, tinted by the shader's <c>_Color</c>. A renderer can also be all outline.
    /// </summary>
    internal static class WKTItemOutline {
        private const string c_outline_shader = "Dark Machine/SHDR_Outline";
        private static readonly int s_colorId = Shader.PropertyToID("_Color");
        private static readonly List<Material> s_materials = new();

        /// <summary>The outline's color, opaque. False if <paramref name="renderer"/> draws no outline or has no color.</summary>
        public static bool TryGetColor(Renderer? renderer, out Color color) {
            color = default;
            if (FindSlot(renderer, out var material) < 0 || !material!.HasProperty(s_colorId)) return false;
            color = material.GetColor(s_colorId);
            color.a = 1f;
            return true;
        }

        /// <summary>
        /// Hides the outlines of an item copy's renderers, adding each to <paramref name="parts"/> so they can be shown again
        /// (see <see cref="WKTOutlinePart.Show"/>). Allocates, so call it when the copy is made.
        /// </summary>
        public static void HideOutlines(GameObject root, List<WKTOutlinePart> parts) {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) {
                var slot = FindSlot(renderer, out _);
                if (slot < 0) continue;
                var with = renderer.sharedMaterials;
                if (with.Length == 1) {
                    parts.Add(new WKTOutlinePart(renderer, null, null));
                    renderer.enabled = false;
                    continue;
                }
                var without = new Material[with.Length - 1];
                for (int i = 0, j = 0; i < with.Length; i++) {
                    if (i != slot)
                        without[j++] = with[i];
                }
                parts.Add(new WKTOutlinePart(renderer, with, without));
                renderer.sharedMaterials = without;
            }
        }

        /// <summary>The index of the outline among <paramref name="renderer"/>'s materials, or -1.</summary>
        private static int FindSlot(Renderer? renderer, out Material? material) {
            material = null;
            if (renderer == null) return -1;
            renderer.GetSharedMaterials(s_materials);
            try {
                for (var i = 0; i < s_materials.Count; i++) {
                    var candidate = s_materials[i];
                    if (candidate == null || candidate.shader == null || candidate.shader.name != c_outline_shader) continue;
                    material = candidate;
                    return i;
                }
                return -1;
            }
            finally {
                s_materials.Clear();
            }
        }
    }

    /// <summary>An outline hidden by <see cref="WKTItemOutline.HideOutlines"/>.</summary>
    internal readonly struct WKTOutlinePart {
        private readonly Renderer m_renderer;
        /// <summary>The renderer's materials with and without the outline; null when the renderer is all outline.</summary>
        private readonly Material[]? m_with;
        private readonly Material[]? m_without;

        public WKTOutlinePart(Renderer renderer, Material[]? with, Material[]? without) {
            m_renderer = renderer;
            m_with = with;
            m_without = without;
        }

        public void Show(bool shown) {
            if (m_renderer == null) return;
            if (m_with == null)
                m_renderer.enabled = shown;
            else
                m_renderer.sharedMaterials = shown ? m_with : m_without;
        }
    }
}
