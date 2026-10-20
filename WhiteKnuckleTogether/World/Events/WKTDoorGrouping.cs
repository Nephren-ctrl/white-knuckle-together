using System.Collections.Generic;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Keeps a co-op group together at the gate doors that close behind the players. A gate closes the way the game closes
    /// it in singleplayer, the moment a player touches the trigger past it, but only once every living player has.
    /// <para>
    /// Each living player is through a gate once they touch its pass zone (its trigger) or are straight above it, and stays through
    /// until they are truly behind it again: in a lower room and well below the zone for <see cref="BEHIND_SECONDS"/>. So
    /// walking on past a door, a dip or a fall onto the closed door never reopens it, and nothing flickers.
    /// </para>
    /// <para>
    /// A room is one of the game's levels along the route, so doors to side rooms inside a level never count as rooms of
    /// their own. A gate never closes while a living player who is not through it stands in its doorway. Pure, so it can be
    /// tested without Unity.
    /// </para>
    /// </summary>
    internal sealed class WKTDoorGrouping {
        /// <summary>How far below a gate's pass zone a player must be before they can count as behind it again.</summary>
        public const float HYSTERESIS = 1.5f;
        /// <summary>How long a player must stay truly behind a gate before it reopens for them.</summary>
        public const float BEHIND_SECONDS = 1f;
        private readonly Dictionary<ulong, WKTGateLatch[]> m_latches = new();

        /// <summary>
        /// Decides which gates should be open. <paramref name="gates"/> and <paramref name="players"/> are this tick's, and
        /// <paramref name="deltaTime"/> the seconds since the last call. Fills <paramref name="open"/> and
        /// <paramref name="passed"/> (both cleared first) per gate.
        /// </summary>
        public void Evaluate(IReadOnlyList<WKTGroupGate> gates, IReadOnlyList<WKTGroupPlayer> players, float deltaTime, List<bool> open, List<bool> passed) {
            open.Clear();
            passed.Clear();
            for (var g = 0; g < gates.Count; g++) {
                var gate = gates[g];
                var latches = GetLatches(gate.Key);
                var living = 0;
                var through = 0;
                var blocked = false;
                for (var p = 0; p < players.Count; p++) {
                    var player = players[p];
                    if (!player.Alive) continue;
                    ref var latch = ref latches[player.Id];
                    var side = GetSide(player.Position, gate.ZoneMin, gate.ZoneMax);
                    UpdateLatch(ref latch, side, player.Room, gate.Room, gate.Current, deltaTime);
                    living++;
                    if (latch.Through)
                        through++;
                    if (!blocked && !IsInside(player.Position, gate.ZoneMin, gate.ZoneMax) && IsInside(player.Position, gate.DoorwayMin, gate.DoorwayMax))
                        blocked = true;
                }
                // With nobody alive to decide, the gate stays as it is.
                var isPassed = living == 0 ? !gate.Current : through == living;
                passed.Add(isPassed);
                open.Add(Decide(isPassed, blocked, gate.Current));
            }
        }

        /// <summary>Forgets a gate that went away.</summary>
        public void Forget(ulong key) => m_latches.Remove(key);

        /// <summary>Forgets every player's progress, such as after a change of host.</summary>
        public void Clear() => m_latches.Clear();

        /// <summary>Whether a player has gone through a gate. False for gates or players never seen.</summary>
        public bool IsThrough(ulong key, byte playerId) => m_latches.TryGetValue(key, out var latches) && latches[playerId].Through;

        private WKTGateLatch[] GetLatches(ulong key) {
            if (m_latches.TryGetValue(key, out var latches)) return latches;
            latches = new WKTGateLatch[byte.MaxValue + 1];
            m_latches.Add(key, latches);
            return latches;
        }

        /// <summary>
        /// Where a player is relative to a gate's pass zone, already grown by the player's capsule so that inside means touching,
        /// the way the zone's trigger sees it. Above the zone counts as through only straight above it, as in a shaft: someone
        /// higher up beside a side room's trigger has not gone into that room.
        /// </summary>
        public static WKTGateSide GetSide(Vector3 position, Vector3 zoneMin, Vector3 zoneMax) {
            var overZone = position.x >= zoneMin.x && position.x <= zoneMax.x && position.z >= zoneMin.z && position.z <= zoneMax.z;
            if (overZone && position.y >= zoneMin.y) return WKTGateSide.Through;
            return position.y < zoneMin.y - HYSTERESIS ? WKTGateSide.Below : WKTGateSide.Near;
        }

        /// <summary>
        /// Steps one player's latch on one gate. The first time a player is seen, such as a late joiner or everyone after a
        /// change of host, they count as through if they are past it, or beside it in a later room, or beside a closed gate in
        /// its own room: standing on a closed hatch, say. Beside an open gate in its own room is not enough: a side door can
        /// lead into the room the players start in.
        /// </summary>
        public static void UpdateLatch(ref WKTGateLatch latch, WKTGateSide side, int playerRoom, int gateRoom, bool gateOpen, float deltaTime) {
            if (!latch.Known) {
                latch.Known = true;
                latch.Through = side == WKTGateSide.Through ||
                    (side == WKTGateSide.Near && gateRoom >= 0 && (playerRoom > gateRoom || (!gateOpen && playerRoom == gateRoom)));
                latch.BehindTime = 0f;
                return;
            }
            if (side == WKTGateSide.Through) {
                latch.Through = true;
                latch.BehindTime = 0f;
                return;
            }
            if (!latch.Through) return;
            if (!IsTrulyBehind(side, playerRoom, gateRoom)) {
                latch.BehindTime = 0f;
                return;
            }
            latch.BehindTime += deltaTime;
            if (latch.BehindTime < BEHIND_SECONDS) return;
            latch.Through = false;
            latch.BehindTime = 0f;
        }

        /// <summary>
        /// Whether a player is back behind a gate: well below it, and in a lower room. When either room is unknown, height
        /// alone decides, so a player is never shut behind a gate for good on a guess.
        /// </summary>
        public static bool IsTrulyBehind(WKTGateSide side, int playerRoom, int gateRoom) =>
            side == WKTGateSide.Below && (playerRoom < 0 || gateRoom < 0 || playerRoom < gateRoom);

        /// <summary>
        /// A player's room, from this tick's lookup or, when that comes back unknown (-1), their last known one, so a player
        /// briefly between levels keeps the room they were in.
        /// </summary>
        public static int RememberRoom(int room, ref int lastRoom) {
            if (room < 0) return lastRoom;
            lastRoom = room;
            return room;
        }

        /// <summary>Whether a gate should be open. Closing on a player would trap or crush them, so a blocked open gate waits.</summary>
        public static bool Decide(bool passed, bool blocked, bool current) => !passed || (blocked && current);

        /// <summary>
        /// The room a gate counts as being in: the one its pass zone's height is in, but never more than one past the level its
        /// door belongs to. A zone above its level's exit (the vent out of the Teeth's shaft is passed by climbing above it)
        /// otherwise put the gate several rooms ahead, so it stayed shut for everyone. Either room is -1 when unknown.
        /// </summary>
        public static int GetGateRoom(int zoneRoom, int levelRoom) {
            if (levelRoom < 0) return zoneRoom;
            return zoneRoom < 0 ? levelRoom : Mathf.Min(zoneRoom, levelRoom + 1);
        }

        private static bool IsInside(Vector3 position, Vector3 min, Vector3 max) =>
            position.x >= min.x && position.x <= max.x && position.y >= min.y && position.y <= max.y && position.z >= min.z && position.z <= max.z;
    }

    /// <summary>One gate as <see cref="WKTDoorGrouping"/> sees it this tick, in world space.</summary>
    internal readonly struct WKTGroupGate {
        public readonly ulong Key;
        /// <summary>The corners of the gate's pass zone, grown by the player's capsule.</summary>
        public readonly Vector3 ZoneMin;
        public readonly Vector3 ZoneMax;
        /// <summary>The corners of the doorway in which a player stops the gate from closing.</summary>
        public readonly Vector3 DoorwayMin;
        public readonly Vector3 DoorwayMax;
        /// <summary>The route room the gate leads into, -1 if unknown.</summary>
        public readonly int Room;
        public readonly bool Current;

        public WKTGroupGate(ulong key, Vector3 zoneMin, Vector3 zoneMax, Vector3 doorwayMin, Vector3 doorwayMax, int room, bool current) {
            Key = key;
            ZoneMin = zoneMin;
            ZoneMax = zoneMax;
            DoorwayMin = doorwayMin;
            DoorwayMax = doorwayMax;
            Room = room;
            Current = current;
        }
    }

    /// <summary>One player as <see cref="WKTDoorGrouping"/> sees them this tick.</summary>
    internal readonly struct WKTGroupPlayer {
        public readonly byte Id;
        public readonly Vector3 Position;
        /// <summary>The route room the player is in, -1 if unknown.</summary>
        public readonly int Room;
        public readonly bool Alive;

        public WKTGroupPlayer(byte id, Vector3 position, int room, bool alive) {
            Id = id;
            Position = position;
            Room = room;
            Alive = alive;
        }
    }

    /// <summary>Where a player is relative to a gate's pass zone.</summary>
    internal enum WKTGateSide : byte {
        /// <summary>Beside or just below the zone, neither through nor clearly behind.</summary>
        Near,
        /// <summary>Touching the zone or straight above it.</summary>
        Through,
        /// <summary>More than <see cref="WKTDoorGrouping.HYSTERESIS"/> below the zone.</summary>
        Below
    }

    /// <summary>One player's progress through one gate.</summary>
    internal struct WKTGateLatch {
        public bool Known;
        public bool Through;
        /// <summary>How long the player has been truly behind the gate while through it.</summary>
        public float BehindTime;
    }
}
