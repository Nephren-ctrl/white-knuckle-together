using System.Collections.Generic;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Widens a lobby settings grid's cells to fill the settings section, which takes whatever room the player list leaves:
    /// the grid is built for the section's narrowest width. Each cell's texts (a toggle's label, a dropdown's title and
    /// choice) then end at the cell's right edge, shrinking down to <see cref="MIN_TEXT_SCALE"/> of their size to fit before
    /// they are cut short, so none runs under the next cell. Redone whenever that width changes.
    /// </summary>
    [RequireComponent(typeof(GridLayoutGroup))]
    internal sealed class WKTLobbyGridFit : MonoBehaviour {
        /// <summary>How small a cell's text may shrink, as a fraction of its own size, before it is cut short.</summary>
        public const float MIN_TEXT_SCALE = 0.7f;
        private static readonly List<TMP_Text> s_texts = new();
        private readonly List<(TMP_Text Text, float Size, float RightMargin)> m_texts = new();
        private GridLayoutGroup m_grid = null!;
        private RectTransform m_parent = null!;
        private float m_fittedWidth = -1f;
        private bool m_textsPending;

        [UsedImplicitly]
        private void Awake() {
            m_grid = GetComponent<GridLayoutGroup>();
            m_parent = (RectTransform)transform.parent;
        }

        [UsedImplicitly]
        private void LateUpdate() {
            // A frame after the cells changed, once the layout has placed them.
            if (m_textsPending) {
                m_textsPending = false;
                FitTexts();
            }
            var width = m_parent.rect.width;
            if (width <= 0f || Mathf.Approximately(width, m_fittedWidth)) return;
            m_fittedWidth = width;
            var columns = m_grid.constraintCount;
            var cell = m_grid.cellSize;
            cell.x = (width - m_grid.spacing.x * (columns - 1)) / columns;
            m_grid.cellSize = cell;
            WKTMainMenuUI.SizeGrid((RectTransform)transform);
            m_textsPending = true;
        }

        private void FitTexts() {
            if (m_texts.Count == 0)
                CollectTexts();
            foreach (var (text, size, rightMargin) in m_texts) {
                if (text == null || text.transform.parent == null) continue;
                var cell = FindCell(text.transform);
                if (cell == null) continue;
                // Measured with the text's own margin, then cut back to the cell's edge.
                var margin = text.margin;
                margin.z = rightMargin;
                text.margin = margin;
                var rect = text.rectTransform;
                var right = cell.InverseTransformPoint(rect.TransformPoint(new Vector3(rect.rect.xMax, 0f, 0f))).x - rightMargin;
                margin.z = rightMargin + Mathf.Max(0f, right - cell.rect.xMax);
                text.margin = margin;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.fontSizeMax = size;
                text.fontSizeMin = size * MIN_TEXT_SCALE;
                text.enableAutoSizing = true;
            }
        }

        /// <summary>Every shown text in the cells, with the size and right margin it was made with.</summary>
        private void CollectTexts() {
            GetComponentsInChildren(false, s_texts);
            foreach (var text in s_texts)
                m_texts.Add((text, text.enableAutoSizing ? text.fontSizeMax : text.fontSize, text.margin.z));
            s_texts.Clear();
        }

        /// <summary>The grid cell <paramref name="target"/> is in: the grid's child it sits under.</summary>
        private RectTransform? FindCell(Transform target) {
            var current = target;
            while (current.parent != null && current.parent != transform)
                current = current.parent;
            return current.parent == transform ? current as RectTransform : null;
        }
    }
}
