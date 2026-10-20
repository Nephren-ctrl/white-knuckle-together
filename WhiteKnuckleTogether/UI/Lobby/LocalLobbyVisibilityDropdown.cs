using JetBrains.Annotations;
using TMPro;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>Picks the visibility of lobbies the local player hosts. The option index is the <see cref="LobbyVisibilityId"/>.</summary>
    internal sealed class LocalLobbyVisibilityDropdown : MonoBehaviour {
        private static readonly string[] s_optionNames = { "Friends Only", "Invite Only", "Public", "Join Code" };

        /// <summary>The dropdown option text for each <see cref="LobbyVisibilityId"/>, in enum order.</summary>
        public static string[] OptionNames => s_optionNames;

        [UsedImplicitly]
        private void Awake() => GetComponent<TMP_Dropdown>().onValueChanged.AddListener(OnLobbyTypeValueChanged);

        private static void OnLobbyTypeValueChanged(int index) {
            WKT.Instance.Settings.LocalLobbyVisibilityId = (LobbyVisibilityId)index;
            WKT.Instance.Logger.LogInfo($"Set local lobby visibility to {(LobbyVisibilityId)index}.");
        }
    }
}
