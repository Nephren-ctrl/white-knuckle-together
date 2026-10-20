using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Picking or dropping a trinket or binding in a multiplayer lobby unreadies the local player: they were ready with other
    /// picks. Not critical: without it, a ready player keeps their changed picks ready.
    /// </summary>
    [HarmonyPatch(typeof(UI_TrinketPicker), nameof(UI_TrinketPicker.SelectTrinket))]
    internal sealed class TrinketPickerSelectTrinket_Patch {
        [UsedImplicitly]
        private static void Postfix() {
            var network = WKT.InstanceOrNull?.Network;
            if (network == null || network.Mode == NetworkMode.None || network.IsMultiplayerRun || !network.IsLocalReady) return;
            network.SetLocalReady(false);
        }
    }
}
