using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Shows the lobby settings in a grid only in the play modes they belong to, and re-sizes the grid so the others close up.
    /// Sits on the grid, which is always active, because the game clones the lobby from a template and a hidden row's own
    /// components would never wake in the clone. The rows are serialized, so the clone points at its own.
    /// </summary>
    internal sealed class WKTLobbyModeVisibility : MonoBehaviour {
        [SerializeField] private List<GameObject> m_coopOnly = new();
        [SerializeField] private List<GameObject> m_raceOnly = new();

        public void AddCoopOnly(GameObject row) => m_coopOnly.Add(row);

        public void AddRaceOnly(GameObject row) => m_raceOnly.Add(row);

        [UsedImplicitly]
        private void Awake() {
            var network = WKT.Instance.Network;
            network.OnLobbyPlayModeChanged += Refresh;
            network.OnClientStarted += Refresh;
            network.OnHostChanged += Refresh;
            network.OnNetworkStopping += Refresh;
        }

        [UsedImplicitly]
        private void OnEnable() => Refresh();

        [UsedImplicitly]
        private void OnDestroy() {
            var wkt = WKT.InstanceOrNull;
            if (wkt == null) return;
            wkt.Network.OnLobbyPlayModeChanged -= Refresh;
            wkt.Network.OnClientStarted -= Refresh;
            wkt.Network.OnHostChanged -= Refresh;
            wkt.Network.OnNetworkStopping -= Refresh;
        }

        /// <summary>Shows the rows of the lobby's play mode now. Call once all rows are added.</summary>
        public void Refresh() {
            var mode = WKT.Instance.Network.LobbyPlayMode;
            var changed = SetShown(m_coopOnly, mode == PlayModeId.Coop);
            changed |= SetShown(m_raceOnly, mode == PlayModeId.Race);
            if (!changed) return;
            var grid = (RectTransform)transform;
            if (!TryGetComponent<GridLayoutGroup>(out _)) return;
            WKTMainMenuUI.SizeGrid(grid);
            LayoutRebuilder.MarkLayoutForRebuild(grid);
        }

        private static bool SetShown(List<GameObject> rows, bool shown) {
            var changed = false;
            foreach (var row in rows) {
                if (row == null || row.activeSelf == shown) continue;
                row.SetActive(shown);
                changed = true;
            }
            return changed;
        }
    }
}
