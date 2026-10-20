using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Picks who takes over when the host leaves. Every player computes this from the same player list, so they all agree
    /// on the successor without talking to each other, even when the host crashed. Pure, so it can be tested without Steam.
    /// </summary>
    public static class WKTHostSuccession {
        /// <summary>The player with the lowest id other than the leaving host. False when nobody else is left.</summary>
        public static bool TryPickSuccessor(Dictionary<byte, WKTPlayer> players, byte leavingHostId, out WKTPlayer? successor) {
            successor = null;
            foreach (var player in players.Values) {
                if (player.Id == leavingHostId) continue;
                if (successor == null || player.Id < successor.Id)
                    successor = player;
            }
            return successor != null;
        }
    }
}
