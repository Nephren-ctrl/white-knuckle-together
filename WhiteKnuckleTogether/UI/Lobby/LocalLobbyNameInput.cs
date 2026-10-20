using JetBrains.Annotations;
using TMPro;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>Names the lobbies the local player hosts, as the lobby browser shows them. Left empty, they are named after the player.</summary>
    internal sealed class LocalLobbyNameInput : MonoBehaviour {
        private TMP_InputField? m_field;

        [UsedImplicitly]
        private void Awake() {
            m_field = GetComponent<TMP_InputField>();
            m_field.SetTextWithoutNotify(WKT.Instance.Settings.LocalLobbyName);
            m_field.onEndEdit.AddListener(OnEndEdit);
        }

        private void OnEndEdit(string value) {
            var name = WKTLobbyData.SanitizeName(value, WKTLobbyConsts.MAX_LOBBY_NAME_LENGTH);
            WKT.Instance.Settings.LocalLobbyName = name;
            if (m_field != null)
                m_field.SetTextWithoutNotify(name);
            WKT.Instance.Logger.LogInfo($"Set local lobby name to \"{name}\".");
        }
    }
}
