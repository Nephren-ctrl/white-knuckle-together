using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The Hadal Diver's ride ends for everyone together in a synced co-op run. Each machine rides its own diver, and the ride
    /// ends (<c>DEN_HadalDiver.ExitDiver</c>, which builds the branch it leads to) once that machine's player nears the spot it
    /// carries them to. A player who did not get on would stay behind with a diver that never lets anyone off. So the first
    /// rider let off brings everyone else to that spot (see <see cref="WKTDoorSync.OnLocalDiverExit"/>), ending their own
    /// ride first.
    /// </summary>
    internal static class WKTDiverExit {
        /// <summary>The diver head's trigger that ends the ride once the player is near it, which is where the ride takes them.</summary>
        private const string c_exit_target = "Player Mover Target";

        /// <summary>Set while this machine ends its ride because another player's ended, which is never sent on.</summary>
        public static bool IsApplying { get; private set; }

        /// <summary>Where a diver lets its riders off: its exit trigger, else the local player.</summary>
        public static Vector3 GetExit(DEN_HadalDiver diver) {
            foreach (var trigger in diver.GetComponentsInChildren<UT_PlayerTriggerRadius>(true)) {
                if (trigger.name == c_exit_target) return trigger.transform.position;
            }
            return ENT_Player.playerObject != null ? ENT_Player.playerObject.transform.position : diver.transform.position;
        }

        /// <summary>Another player's ride ended: this machine's diver, if still here, lets its riders off too.</summary>
        public static void EndLocalRide() {
            var diver = DEN_HadalDiver.instance;
            if (diver == null) return;
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
