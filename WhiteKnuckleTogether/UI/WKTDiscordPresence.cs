using Discord.Sdk;
using HarmonyLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Discord rich presence in a multiplayer session: the game's own details and timer, plus how the player is playing
    /// together ("In a lobby", "Co-op", "Race · 3 alive") and the party's size. Outside a session the game's own presence
    /// is left alone; leaving one makes the game send its own again.
    /// </summary>
    internal static class WKTDiscordPresence {
        private const float c_check_interval = 1f;
        private static readonly AccessTools.FieldRef<DiscordManager, Client?> s_client = AccessTools.FieldRefAccess<DiscordManager, Client?>("client");
        private static readonly AccessTools.FieldRef<DiscordManager, int> s_version = AccessTools.FieldRefAccess<DiscordManager, int>("richPresenceVersion");
        private static string s_lastState = string.Empty;
        private static int s_lastPlayers;
        private static int s_lastGameVersion = -1;
        private static float s_nextCheck;
        private static bool s_showing;

        /// <summary>In place of the game's presence update. False when it was handled here.</summary>
        public static bool BeforeUpdate(DiscordManager manager) {
            var network = WKT.InstanceOrNull?.Network;
            if (network == null || network.Mode == NetworkMode.None || network.LocalPlayer == null) {
                if (s_showing) {
                    // The game only sends when its own version changes: make it send its presence again.
                    s_showing = false;
                    s_version(manager) = -1;
                }
                return true;
            }
            var client = s_client(manager);
            if (client == null || UnityEngine.Time.unscaledTime < s_nextCheck) return false;
            s_nextCheck = UnityEngine.Time.unscaledTime + c_check_interval;
            var players = CountPlayers(network);
            var state = GetState(network);
            var gameVersion = CL_GameManager.RichPresenceVersion();
            if (s_showing && state == s_lastState && players == s_lastPlayers && gameVersion == s_lastGameVersion) return false;
            s_showing = true;
            s_lastState = state;
            s_lastPlayers = players;
            s_lastGameVersion = gameVersion;
            s_version(manager) = gameVersion;
            client.UpdateRichPresence(Build(network, state, players), OnUpdated);
            return false;
        }

        /// <summary>How the local player is playing together, for the presence's state line.</summary>
        public static string GetState(WKTNetwork network) {
            if (!network.IsMultiplayerRun) return "In a lobby";
            var session = network.Session;
            if (!session.IsRace) return session.IsSyncedWorld ? "Co-op" : "Co-op (Instanced)";
            var alive = 0;
            foreach (var player in network.GetPlayersEnumerable())
                if (!WKTPlayerDeaths.IsDead(player.Id)) alive++;
            return $"Race · {alive} alive";
        }

        private static Activity Build(WKTNetwork network, string state, int players) {
            var activity = new Activity();
            activity.SetDetails(CL_GameManager.RichPresenceStringifyEn(CL_GameManager.RichPresencePeek()));
            activity.SetState(state);
            var timestamps = new ActivityTimestamps();
            timestamps.SetStart((ulong)CL_GameManager.RichPresenceStartTime());
            activity.SetTimestamps(timestamps);
            var party = new ActivityParty();
            // The host's Steam id names the session the same for everyone in it.
            party.SetId((network.HostPlayer?.friend.Id.Value ?? 0UL).ToString());
            party.SetCurrentSize(players);
            party.SetMaxSize(WKTLobbyConsts.MAX_PLAYERS);
            activity.SetParty(party);
            return activity;
        }

        private static int CountPlayers(WKTNetwork network) {
            var count = 0;
            foreach (var _ in network.GetPlayersEnumerable())
                count++;
            return count;
        }

        private static void OnUpdated(ClientResult result) {
            if (!result.Successful())
                WKT.InstanceOrNull?.Logger.LogWarning("Discord did not take the multiplayer rich presence.");
        }
    }
}
