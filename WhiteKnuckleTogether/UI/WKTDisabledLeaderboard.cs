using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Marks a <see cref="Leaderboard_Panel"/> the mod keeps only because the game requires one. The panel never
    /// wakes up, so <see cref="LeaderboardPanelRefresh_Patch"/> skips its Refresh.
    /// </summary>
    internal sealed class WKTDisabledLeaderboard : MonoBehaviour { }
}
