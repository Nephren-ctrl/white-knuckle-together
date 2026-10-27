using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Where every player in the run is, in this machine's world. In a synced co-op run the host simulates the world for
    /// everyone, so proximity checks that the game makes against the local player are redirected here and use the nearest
    /// player instead. Everywhere else, including singleplayer, <see cref="IsCoveringAllPlayers"/> is false and every query
    /// answers for the local player exactly as the game would.
    /// </summary>
    internal static class WKTPlayerPositions {
        private static readonly Vector3[] s_remotePositions = new Vector3[WKTLobbyConsts.MAX_PLAYERS];
        private static readonly byte[] s_remoteIds = new byte[WKTLobbyConsts.MAX_PLAYERS];
        private static readonly bool[] s_remoteDead = new bool[WKTLobbyConsts.MAX_PLAYERS];
        private static readonly bool[] s_remoteHanging = new bool[WKTLobbyConsts.MAX_PLAYERS];

        /// <summary>Whether proximity checks consider every player. Only true for the host of a synced co-op run.</summary>
        public static bool IsCoveringAllPlayers { get; set; }
        /// <summary>
        /// Whether the other players are in this machine's world too: any machine of a synced run. No level a living player is
        /// in is ever unloaded here (see <see cref="WKTHostStreaming.TryUnloadPreviousLevels"/>).
        /// </summary>
        public static bool IsSharedWorld { get; set; }
        /// <summary>Set while a proximity check that is the local player's alone runs: <see cref="NearestDistance"/> measures only them.</summary>
        public static bool IsMeasuringLocalOnly { get; set; }
        /// <summary>How many remote players have a known position this frame.</summary>
        public static int RemoteCount { get; private set; }

        public static Vector3 GetRemotePosition(int index) => s_remotePositions[index];

        public static byte GetRemoteId(int index) => s_remoteIds[index];

        /// <summary>Where remote player <paramref name="playerId"/> is this frame. False until their position is known.</summary>
        public static bool TryGetRemotePosition(byte playerId, out Vector3 position) => TryGetRemote(playerId, out position, out _);

        /// <summary>Where remote player <paramref name="playerId"/> is this frame, and whether they are dead. False until their position is known.</summary>
        public static bool TryGetRemote(byte playerId, out Vector3 position, out bool dead) {
            for (var i = 0; i < RemoteCount; i++) {
                if (s_remoteIds[i] != playerId) continue;
                position = s_remotePositions[i];
                dead = s_remoteDead[i];
                return true;
            }
            position = default;
            dead = false;
            return false;
        }

        /// <summary>Whether a remote player is dead. Dead players are neither targets nor near anything.</summary>
        public static bool IsRemoteDead(int index) => s_remoteDead[index];

        /// <summary>Whether a remote player hangs from a hold with either hand.</summary>
        public static bool IsRemoteHanging(int index) => s_remoteHanging[index];

        /// <summary>Forgets every remote position. Call before adding this frame's.</summary>
        public static void ClearRemotes() => RemoteCount = 0;

        /// <summary>Records a remote player's world position for this frame. Ignored once full.</summary>
        public static void AddRemote(byte playerId, Vector3 position, bool dead = false, bool hanging = false) {
            if (RemoteCount == s_remotePositions.Length) return;
            s_remoteIds[RemoteCount] = playerId;
            s_remoteDead[RemoteCount] = dead;
            s_remoteHanging[RemoteCount] = hanging;
            s_remotePositions[RemoteCount++] = position;
        }

        /// <summary>Clears everything at the end of a run.</summary>
        public static void Reset() {
            IsCoveringAllPlayers = false;
            IsSharedWorld = false;
            RemoteCount = 0;
        }

        /// <summary>
        /// The position, of <paramref name="localPosition"/> and every living remote player when covering all of them, closest
        /// to <paramref name="from"/>. Ties go to the local player.
        /// </summary>
        public static Vector3 Nearest(Vector3 from, Vector3 localPosition) {
            if (!IsCoveringAllPlayers) return localPosition;
            var nearest = localPosition;
            var nearestSqr = (localPosition - from).sqrMagnitude;
            for (var i = 0; i < RemoteCount; i++) {
                if (s_remoteDead[i]) continue;
                var sqr = (s_remotePositions[i] - from).sqrMagnitude;
                if (sqr >= nearestSqr) continue;
                nearestSqr = sqr;
                nearest = s_remotePositions[i];
            }
            return nearest;
        }

        /// <summary>
        /// Replaces <c>ENT_Player.GetPlayer().transform.position</c> in the game's proximity checks: the player nearest to
        /// <paramref name="self"/>. Fails like the original when there is no local player.
        /// </summary>
        public static Vector3 NearestTo(Component self) {
            var local = ENT_Player.GetPlayer().transform.position;
            return IsCoveringAllPlayers ? Nearest(self.transform.position, local) : local;
        }

        /// <summary>Replaces <c>CL_GameManager.PlayerDistance</c> in the game's proximity checks: the distance to the nearest player.</summary>
        public static float NearestDistance(CL_GameManager gameManager, Vector3 location) {
            var local = gameManager.PlayerDistance(location);
            if (!IsCoveringAllPlayers || IsMeasuringLocalOnly) return local;
            for (var i = 0; i < RemoteCount; i++) {
                if (s_remoteDead[i]) continue;
                var distance = Vector3.Distance(s_remotePositions[i], location);
                if (distance < local)
                    local = distance;
            }
            return local;
        }
    }
}
