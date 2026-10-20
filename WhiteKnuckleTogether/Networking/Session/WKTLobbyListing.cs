using System;
using System.Collections.Generic;
using Steamworks;
using Steamworks.Data;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A public lobby as the lobby browser shows it, read from the lobby data its host writes. Lobby data is untrusted, so
    /// every text is sanitized. Instances are reused from refresh to refresh.
    /// </summary>
    internal sealed class WKTLobbyListing {
        public const int UNKNOWN_PING = -1;
        /// <summary>A version longer than this is cut, as only a few characters of it are shown.</summary>
        private const int c_max_version_length = 16;

        public SteamId Id { get; private set; }
        public string Name { get; private set; } = string.Empty;
        /// <summary>The names of the players in the session, the host first.</summary>
        public List<string> Members { get; } = new(WKTLobbyConsts.MAX_PLAYERS);
        public int MemberCount { get; private set; }
        public int MaxMembers { get; private set; }
        /// <summary>The estimated round trip to the host in milliseconds, or <see cref="UNKNOWN_PING"/>.</summary>
        public int Ping { get; private set; }
        public string ModVersion { get; private set; } = string.Empty;
        public WKTLobbyListingStatus Status { get; private set; }

        public void Read(Lobby lobby, string localModVersion, string localGameVersion) {
            Id = lobby.Id;
            Name = WKTLobbyData.SanitizeName(lobby.GetData(WKTLobbyConsts.KEY_NAME), WKTLobbyConsts.MAX_LOBBY_NAME_LENGTH);
            WKTLobbyData.ParseMemberNames(lobby.GetData(WKTLobbyConsts.KEY_MEMBERS), Members);
            MemberCount = lobby.MemberCount;
            MaxMembers = lobby.MaxMembers > 0 ? Math.Min(lobby.MaxMembers, WKTLobbyConsts.MAX_PLAYERS) : WKTLobbyConsts.MAX_PLAYERS;
            Ping = EstimatePing(lobby.GetData(WKTLobbyConsts.KEY_PING_LOCATION));
            ModVersion = WKTLobbyData.SanitizeName(lobby.GetData(WKTLobbyConsts.KEY_MOD_VERSION), c_max_version_length);
            Status = WKTLobbyData.GetListingStatus(ModVersion, lobby.GetData(WKTLobbyConsts.KEY_GAME_VERSION),
                lobby.GetData(WKTLobbyConsts.KEY_IN_RUN) == "1", MemberCount, MaxMembers, localModVersion, localGameVersion);
        }

        /// <summary>Joinable lobbies first, then the closest, then by name.</summary>
        public static int Compare(WKTLobbyListing a, WKTLobbyListing b) {
            var joinable = (b.Status == WKTLobbyListingStatus.Joinable).CompareTo(a.Status == WKTLobbyListingStatus.Joinable);
            if (joinable != 0) return joinable;
            var ping = ComparePing(a.Ping, b.Ping);
            return ping != 0 ? ping : string.CompareOrdinal(a.Name, b.Name);
        }

        /// <summary>Known pings before unknown ones, lowest first.</summary>
        private static int ComparePing(int a, int b) {
            if (a == b) return 0;
            if (a == UNKNOWN_PING) return 1;
            if (b == UNKNOWN_PING) return -1;
            return a.CompareTo(b);
        }

        /// <summary>The round trip to a host at <paramref name="location"/>, from Steam's relay ping data.</summary>
        private static int EstimatePing(string? location) {
            if (string.IsNullOrEmpty(location)) return UNKNOWN_PING;
            var parsed = NetPingLocation.TryParseFromString(location);
            if (!parsed.HasValue) return UNKNOWN_PING;
            var ping = SteamNetworkingUtils.EstimatePingTo(parsed.Value);
            return ping >= 0 ? ping : UNKNOWN_PING;
        }
    }
}
