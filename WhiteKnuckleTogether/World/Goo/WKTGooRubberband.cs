using System;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// How far the shared goo is from the players it keeps up with. In singleplayer the goo speeds up with its distance to
    /// the player. On the host of a synced co-op run that distance is measured to the lowest, average or highest living
    /// player instead (<see cref="GooFollowId"/>). Everywhere else the game's own distance is used unchanged.
    /// </summary>
    /// <para>
    /// The followed height moves toward the followed players at no more than <see cref="FOLLOW_RATE"/>. It would otherwise
    /// jump whenever a player dies, comes back or is moved, and the goo, which speeds up with distance and for good once the
    /// players pass its easing height, would lurch and keep the speed it gained.
    /// </para>
    /// <para>
    /// Only how far the followed height is above the goo counts. The game's distance is unsigned, which in singleplayer only
    /// matters once the player is already dead. With several players, someone still counted below the goo made it rise faster
    /// the further it got past them.
    /// </para>
    /// <para>
    /// The host logs what the goo follows every <see cref="LOG_INTERVAL"/> seconds while it rises, to tell from a log why it
    /// sped up.
    /// </para>
    internal static class WKTGooRubberband {
        /// <summary>How fast the followed height may move, in meters per second: faster than anyone climbs.</summary>
        public const float FOLLOW_RATE = 12f;
        /// <summary>Seconds between the host's log lines about the goo.</summary>
        public const float LOG_INTERVAL = 5f;
        private static readonly float[] s_remoteHeights = new float[WKTLobbyConsts.MAX_PLAYERS];
        private static readonly bool[] s_remoteDead = new bool[WKTLobbyConsts.MAX_PLAYERS];
        private static float s_followHeight;
        /// <summary>The frame <see cref="s_followHeight"/> was last moved on; a gap means the goo stopped, and it starts over.</summary>
        private static int s_followFrame = -1;
        /// <summary>Where the followed players were this frame, before <see cref="s_followHeight"/> walked toward it.</summary>
        private static float s_followTarget;
        private static float s_nextLog;
        private static readonly StringBuilder s_log = new();

        /// <summary>
        /// Replaces the goo's own <c>playerDistance</c> where its speed is worked out. Returns <paramref name="localDistance"/>
        /// unless the host covers every player.
        /// </summary>
        public static float Distance(float localDistance, DEN_DeathFloor self) {
            if (!WKTPlayerPositions.IsCoveringAllPlayers) return localDistance;
            var local = ENT_Player.playerObject;
            var localAlive = local != null && !local.dead;
            var localHeight = local != null ? local.transform.position.y : 0f;
            if (!TryGetFollowHeight(localAlive, localHeight, out var height)) return localDistance;
            var distance = Above(self.transform.position.y, height);
            LogIfDue(self, height, distance, localAlive, localHeight);
            return distance;
        }

        /// <summary>
        /// Replaces the travel distance the goo starts rising (<c>playerStartHeight</c>) and easing (<c>playerEaseHeight</c>)
        /// at: the local player's, moved by how far the followed players are above or below them. Unchanged unless the host
        /// covers every player. <paramref name="self"/> is the goo, as the call site passes it.
        /// </summary>
        public static float TravelDistance(float localTravel, DEN_DeathFloor self) {
            var local = ENT_Player.playerObject;
            if (!WKTPlayerPositions.IsCoveringAllPlayers || local == null || self == null) return localTravel;
            var localHeight = local.transform.position.y;
            return TryGetFollowHeight(!local.dead, localHeight, out var height) ? localTravel + (height - localHeight) : localTravel;
        }

        /// <summary>
        /// The followed height this frame, moved at most <see cref="FOLLOW_RATE"/> toward where the followed players are; false
        /// when nobody is alive.
        /// </summary>
        private static bool TryGetFollowHeight(bool localAlive, float localHeight, out float height) {
            var count = CollectRemotes();
            if (!TryGetReference(GetFollow(), localAlive, localHeight, s_remoteHeights.AsSpan(0, count), s_remoteDead.AsSpan(0, count), out var target)) {
                s_followFrame = -1;
                height = 0f;
                return false;
            }
            s_followTarget = target;
            var frame = Time.frameCount;
            if (frame != s_followFrame) {
                s_followHeight = s_followFrame >= 0 && frame - s_followFrame == 1 ? StepFollow(s_followHeight, target, Time.deltaTime) : target;
                s_followFrame = frame;
            }
            height = s_followHeight;
            return true;
        }

        /// <summary>The followed height after <paramref name="deltaTime"/> seconds, moved toward <paramref name="target"/> at <see cref="FOLLOW_RATE"/>.</summary>
        public static float StepFollow(float current, float target, float deltaTime) => Mathf.MoveTowards(current, target, FOLLOW_RATE * deltaTime);

        /// <summary>The followed players' travel, from the local player's travel and the height difference to them.</summary>
        public static float TravelDistance(GooFollowId follow, float localTravel, bool localAlive, float localHeight, ReadOnlySpan<float> remoteHeights,
                                           ReadOnlySpan<bool> remoteDead) =>
            TryGetReference(follow, localAlive, localHeight, remoteHeights, remoteDead, out var reference) ? localTravel + (reference - localHeight) : localTravel;

        private static int CollectRemotes() {
            var count = WKTPlayerPositions.RemoteCount;
            for (var i = 0; i < count; i++) {
                s_remoteHeights[i] = WKTPlayerPositions.GetRemotePosition(i).y;
                s_remoteDead[i] = WKTPlayerPositions.IsRemoteDead(i);
            }
            return count;
        }

        private static GooFollowId GetFollow() => WKT.InstanceOrNull?.Network.Session.GooFollow ?? default;

        /// <summary>
        /// The distance from <paramref name="gooHeight"/> to the lowest, average or highest height of the living players,
        /// the local one included when <paramref name="localAlive"/>. <paramref name="localDistance"/> when nobody is alive.
        /// </summary>
        public static float Distance(GooFollowId follow, float gooHeight, float localDistance, bool localAlive, float localHeight,
                                     ReadOnlySpan<float> remoteHeights, ReadOnlySpan<bool> remoteDead) =>
            TryGetReference(follow, localAlive, localHeight, remoteHeights, remoteDead, out var reference) ? Above(gooHeight, reference) : localDistance;

        /// <summary>How far <paramref name="height"/> is above the goo; 0 at or below it.</summary>
        private static float Above(float gooHeight, float height) => Math.Max(height - gooHeight, 0f);

        /// <summary>
        /// One host log line every <see cref="LOG_INTERVAL"/> seconds: the goo, what it follows, its speed and every player's
        /// height. Its own method, so the engine calls in it are not compiled into <see cref="Distance(float, DEN_DeathFloor)"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void LogIfDue(DEN_DeathFloor goo, float height, float distance, bool localAlive, float localHeight) {
            if (Time.unscaledTime < s_nextLog) return;
            s_nextLog = Time.unscaledTime + LOG_INTERVAL;
            s_log.Clear();
            s_log.Append("Goo follows ").Append(GetFollow()).Append(": goo y ").Append(goo.transform.position.y.ToString("F1"))
                .Append(", followed ").Append(height.ToString("F1")).Append(" (players at ").Append(s_followTarget.ToString("F1"))
                .Append("), distance ").Append(distance.ToString("F1")).Append(", natural speed ").Append(goo.speed.ToString("F3"))
                .Append('/').Append(goo.maxSpeed.ToString("F3")).Append(", travel ").Append(CL_GameManager.GetPlayerTravelDistance().ToString("F1"))
                .Append(". Local ").Append(localHeight.ToString("F1")).Append(localAlive ? string.Empty : " dead");
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                s_log.Append(", player ").Append(WKTPlayerPositions.GetRemoteId(i)).Append(' ')
                    .Append(WKTPlayerPositions.GetRemotePosition(i).y.ToString("F1")).Append(WKTPlayerPositions.IsRemoteDead(i) ? " dead" : string.Empty);
            }
            WKT.Instance.Logger.LogInfo(s_log.Append('.').ToString());
        }

        /// <summary>The lowest, average or highest height of the living players; false when nobody is alive.</summary>
        private static bool TryGetReference(GooFollowId follow, bool localAlive, float localHeight, ReadOnlySpan<float> remoteHeights,
                                            ReadOnlySpan<bool> remoteDead, out float reference) {
            var count = 0;
            var lowest = float.PositiveInfinity;
            var highest = float.NegativeInfinity;
            var sum = 0d;
            if (localAlive)
                Add(localHeight, ref count, ref lowest, ref highest, ref sum);
            for (var i = 0; i < remoteHeights.Length; i++) {
                if (!remoteDead[i])
                    Add(remoteHeights[i], ref count, ref lowest, ref highest, ref sum);
            }
            reference = follow switch {
                GooFollowId.Average => (float)(sum / count),
                GooFollowId.Highest => highest,
                _ => lowest
            };
            return count > 0;
        }

        private static void Add(float height, ref int count, ref float lowest, ref float highest, ref double sum) {
            count++;
            sum += height;
            if (height < lowest)
                lowest = height;
            if (height > highest)
                highest = height;
        }
    }
}
