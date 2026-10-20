using System;
using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// This is the main class to use for implementing / extending WKT functionality.
    /// It is mostly limited to in-game functionality. The main menu functionality is very hard-coded.
    /// </summary>
    public sealed class WKTAPI {
        /// <summary>
        /// A player of the multiplayer run died (true) or came back (false). Each player's own machine decides their death,
        /// and every machine raises this once it knows, for the local player too.
        /// </summary>
        public event Action<WKTPlayer, bool>? PlayerDeathChanged;
        /// <summary>Every player of the multiplayer run is dead, so it is over. Raised once per run on every machine.</summary>
        public event Action? RunOver;

        public NetworkMode GetNetworkMode() => WKT.Instance.Network.Mode;

        public IEnumerable<WKTPlayer> GetPlayersEnumerable() => WKT.Instance.Network.GetPlayersEnumerable();

        /// <summary>Whether the player is dead in the multiplayer run in progress. Always false outside one.</summary>
        public bool IsPlayerDead(byte playerId) => WKTPlayerDeaths.IsDead(playerId);

        internal void RaisePlayerDeathChanged(WKTPlayer player, bool dead) => PlayerDeathChanged?.Invoke(player, dead);

        internal void RaiseRunOver() => RunOver?.Invoke();
    }
}
