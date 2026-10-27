using HarmonyLib;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The Hadal Diver's ride ends for everyone together in a synced co-op run. Each machine rides its own diver, and the ride
    /// ends (<c>DEN_HadalDiver.ExitDiver</c>, which builds the branch it leads to) once that machine's player nears the spot it
    /// carries them to. A player who did not get on would stay behind with a diver that never lets anyone off. So the first
    /// rider let off ends everyone else's ride too (see <see cref="WKTDoorSync.OnLocalDiverExit"/>). A player who never got on
    /// (or died and was let out) leaves from where that rider got on, so the game's own exit brings them along.
    /// </summary>
    internal static class WKTDiverExit {
        private static readonly AccessTools.FieldRef<UT_ChamberLoader, M_Chamber?> s_spawnedChamber = AccessTools.FieldRefAccess<UT_ChamberLoader, M_Chamber?>("spawnedChamber");
        private static readonly AccessTools.FieldRef<UT_ChamberLoader, Vector3> s_exitPosition = AccessTools.FieldRefAccess<UT_ChamberLoader, Vector3>("exitPlayerPosition");
        private static readonly AccessTools.FieldRef<UT_ChamberLoader, Quaternion> s_exitRotation = AccessTools.FieldRefAccess<UT_ChamberLoader, Quaternion>("exitPlayerRotation");

        /// <summary>Set while this machine ends its ride because another player's ended, which is never sent on.</summary>
        public static bool IsApplying { get; private set; }

        /// <summary>Where a diver's riders leave its chamber from: where the local player got on, else where they are.</summary>
        public static Vector3 GetExit(DEN_HadalDiver diver) {
            if (diver.TryGetComponent<UT_ChamberLoader>(out var loader) && s_spawnedChamber(loader) != null) return s_exitPosition(loader);
            return ENT_Player.playerObject != null ? ENT_Player.playerObject.transform.position : diver.transform.position;
        }

        /// <summary>
        /// Whether something hangs on the local diver: its triggers, which only measure the local player, and the items and
        /// creatures placed on its segments, which stay on each machine's own diver until they are taken off it.
        /// </summary>
        public static bool IsPartOfDiver(Component component) {
            var diver = DEN_HadalDiver.instance;
            return diver != null && component.transform.IsChildOf(diver.transform);
        }

        /// <summary>
        /// Another player's ride ended: this machine's diver, if still here, lets its riders off too. Without a chamber here to
        /// leave, one is made to leave from <paramref name="exit"/>, where that player got on.
        /// </summary>
        public static void EndLocalRide(Vector3 exit) {
            var diver = DEN_HadalDiver.instance;
            if (diver == null) return;
            if (diver.TryGetComponent<UT_ChamberLoader>(out var loader) && s_spawnedChamber(loader) == null && ENT_Player.playerObject is { } player) {
                loader.LoadChamber();
                s_exitPosition(loader) = exit;
                s_exitRotation(loader) = player.transform.rotation;
                WKT.Instance.Logger.LogInfo("Not on the Hadal Diver: brought along to where the group got on.");
            }
            IsApplying = true;
            try {
                diver.ExitDiver();
            }
            finally {
                IsApplying = false;
            }
            WKT.Instance.Logger.LogInfo("Ended the Hadal Diver's ride with the group.");
        }
    }
}
