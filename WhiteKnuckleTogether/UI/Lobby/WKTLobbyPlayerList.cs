using System.Collections.Generic;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;

namespace WhiteKnuckleTogether {
    internal sealed class WKTLobbyPlayerList : MonoBehaviour {
        [SerializeField] private TMP_Text? m_header;
        [SerializeField] private Transform? m_rowRoot;
        private readonly Dictionary<WKTPlayer, WKTLobbyPlayer> m_playerMap = new();
        private readonly List<WKTLobbyPlayer> m_rows = new();

        public int Count => m_rows.Count;
        public Transform RowRoot => m_rowRoot != null ? m_rowRoot : transform;

        // SetText buffers are not serialized, so clones of the template lose the header text set in SetupReferences.
        [UsedImplicitly]
        private void Awake() {
            RefreshHeader();
            WKT.Instance.Network.OnPlayerMemberDataChanged += OnPlayerMemberDataChanged;
        }

        [UsedImplicitly]
        private void OnDestroy() {
            var wkt = WKT.InstanceOrNull;
            if (wkt != null)
                wkt.Network.OnPlayerMemberDataChanged -= OnPlayerMemberDataChanged;
        }

        /// <summary>Shows when a player turns voice chat on or off.</summary>
        private void OnPlayerMemberDataChanged(WKTPlayer player) {
            if (m_playerMap.TryGetValue(player, out var row))
                row.RefreshTag(player);
        }

        public void SetupReferences(TMP_Text? header, Transform rowRoot) {
            m_header = header;
            m_rowRoot = rowRoot;
            RefreshHeader();
        }

        public void RegisterPlayer(WKTPlayer player, WKTLobbyPlayer lobbyPlayer) {
            m_playerMap.Add(player, lobbyPlayer);
            m_rows.Add(lobbyPlayer);
            lobbyPlayer.Setup(player, m_rows.Count - 1);
            RefreshHeader();
        }

        public bool TryGetLobbyPlayer(WKTPlayer player, out WKTLobbyPlayer lobbyPlayer) => m_playerMap.TryGetValue(player, out lobbyPlayer);

        public bool UnregisterPlayer(WKTPlayer player) {
            if (!m_playerMap.Remove(player, out var lobbyPlayer)) return false;
            var index = m_rows.IndexOf(lobbyPlayer);
            m_rows.RemoveAt(index);
            Destroy(lobbyPlayer.gameObject);
            for (var i = index; i < m_rows.Count; i++)
                m_rows[i].SetIndex(i);
            RefreshHeader();
            return true;
        }

        /// <summary>Moves the host tag after a host migration.</summary>
        public void RefreshTags() {
            foreach (var pair in m_playerMap)
                pair.Value.RefreshTag(pair.Key);
        }

        public void Clear() {
            foreach (var lobbyPlayer in m_rows)
                Destroy(lobbyPlayer.gameObject);
            m_playerMap.Clear();
            m_rows.Clear();
            RefreshHeader();
        }

        private void RefreshHeader() {
            if (m_header != null)
                m_header.SetText("Players  {0}/{1}", m_rows.Count, WKTLobbyConsts.MAX_PLAYERS);
        }
    }
}
