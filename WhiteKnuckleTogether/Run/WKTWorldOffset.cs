using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The game moves the whole world down when a run climbs too high, on each machine at its own time. Positions travel
    /// without it (y minus the offset), and each machine adds its own back.
    /// </summary>
    internal static class WKTWorldOffset {
        public static float Get(CL_GameManager gameManager) => -gameManager.GetCorrectedPosition(Vector3.zero).y;

        /// <summary>The current offset, or zero before the game manager exists.</summary>
        public static float Get() {
            var gameManager = CL_GameManager.gMan;
            return gameManager == null ? 0f : Get(gameManager);
        }

        public static Vector3 ToWire(Vector3 position, float offset) => new(position.x, position.y - offset, position.z);

        public static Vector3 FromWire(Vector3 position, float offset) => new(position.x, position.y + offset, position.z);
    }
}
