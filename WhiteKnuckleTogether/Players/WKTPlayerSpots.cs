using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>Where each player stands around a spot the game moves every player to, by run slot. Pure.</summary>
    internal static class WKTPlayerSpots {
        /// <summary>How far from the spot each player stands.</summary>
        public const float RADIUS = 1f;

        /// <summary>The horizontal offset of the player in run slot <paramref name="slot"/>: evenly around a circle.</summary>
        public static Vector3 Offset(int slot) {
            var angle = slot * (360f / WKTLobbyConsts.MAX_PLAYERS) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * RADIUS;
        }
    }
}
