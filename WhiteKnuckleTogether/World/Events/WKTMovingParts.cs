using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using SendType = Steamworks.Data.SendType;

namespace WhiteKnuckleTogether {
    /// <summary>Which kind of moving part a <see cref="WKTMessageId.MovingPart"/> message is about.</summary>
    public enum WKTMovingPartKind : byte {
        /// <summary>A door opened bit by bit, by a crank or lever (<c>UT_Door.AddOpenAmount</c>, <c>SetOpenAmount</c>).</summary>
        Door,
        /// <summary>A platform moved along its track, such as a Haunted Pier crane (<c>UT_Platform</c>).</summary>
        Platform,
        /// <summary>
        /// A crane players steer from its cab (<c>VEH_Crane</c>): its turn and its tether's length. Its cab turns in its own
        /// <c>Update</c> from its buttons, which reach each machine at different times, so the copies drift apart.
        /// </summary>
        Crane
    }

    /// <summary>
    /// Parts of the level that players move by hand, kept in step in a synced co-op run: doors opened by cranks and levers,
    /// platforms moved by their buttons, and cranes steered from their cabs. The machine moving one sends where it is now, ten times a second at
    /// most, through the host, and every other machine moves its copy there. Owned by <see cref="WKTDoorSync"/>.
    /// </summary>
    internal sealed class WKTMovingParts {
        private const float c_send_interval = 0.1f;
        private const float c_platform_threshold = 0.01f;
        /// <summary>How far, in degrees or metres of tether, a crane moves before the host sends it again.</summary>
        private const float c_crane_threshold = 0.01f;
        private static readonly AccessTools.FieldRef<VEH_Crane, float> s_craneAngle = AccessTools.FieldRefAccess<VEH_Crane, float>("curAngle");
        private static readonly AccessTools.FieldRef<VEH_Crane, float> s_craneTether = AccessTools.FieldRefAccess<VEH_Crane, float>("tetherDistanceTarget");
        private static readonly AccessTools.FieldRef<UT_Door, float> s_doorAmount = AccessTools.FieldRefAccess<UT_Door, float>("lerpAmount");
        private static readonly AccessTools.FieldRef<UT_Platform, float> s_platformSpeed = AccessTools.FieldRefAccess<UT_Platform, float>("moveSpeed");
        private readonly WKTNetwork m_network;
        private readonly Dictionary<UT_Door, float> m_movedDoors = new();
        private readonly Dictionary<ulong, UT_Platform> m_platforms = new();
        private readonly Dictionary<UT_Platform, (float Position, float Speed)> m_sentPlatforms = new();
        private readonly Dictionary<ulong, VEH_Crane> m_cranes = new();
        private readonly Dictionary<VEH_Crane, (float Angle, float Tether)> m_sentCranes = new();
        private readonly List<UT_Door> m_doorScratch = new();
        private float m_nextSend;
        private bool m_applying;

        public WKTMovingParts(WKTNetwork network) => m_network = network;

        public static bool IsApplying { get; private set; }

        public void Clear() {
            m_movedDoors.Clear();
            m_platforms.Clear();
            m_sentPlatforms.Clear();
            m_cranes.Clear();
            m_sentCranes.Clear();
        }

        /// <summary>A door's open amount changed here by hand. It is sent with the next batch.</summary>
        public void OnLocalDoorMoved(UT_Door door) {
            if (!m_applying)
                m_movedDoors[door] = s_doorAmount(door);
        }

        /// <summary>Sends what moved since the last batch: hand-moved doors here, and on the host every moving platform.</summary>
        public void Update(WKTDoorSync doors) {
            if (Time.unscaledTime < m_nextSend) return;
            m_nextSend = Time.unscaledTime + c_send_interval;
            if (m_movedDoors.Count > 0) {
                m_doorScratch.Clear();
                m_doorScratch.AddRange(m_movedDoors.Keys);
                foreach (var door in m_doorScratch) {
                    if (door != null && doors.TryGetDoorKey(door, out var key))
                        Send(WKTMovingPartKind.Door, key, s_doorAmount(door), 0f, null);
                }
                m_movedDoors.Clear();
            }
            if (m_network.Mode != NetworkMode.Host) return;
            foreach (var pair in m_platforms) {
                var platform = pair.Value;
                if (platform == null) continue;
                var state = (platform.curPosition, s_platformSpeed(platform));
                if (m_sentPlatforms.TryGetValue(platform, out var sent) && Mathf.Abs(sent.Position - state.Item1) < c_platform_threshold && sent.Speed == state.Item2) continue;
                m_sentPlatforms[platform] = state;
                Send(WKTMovingPartKind.Platform, pair.Key, state.Item1, state.Item2, null);
            }
            foreach (var pair in m_cranes) {
                var crane = pair.Value;
                if (crane == null) continue;
                var state = (s_craneAngle(crane), s_craneTether(crane));
                if (m_sentCranes.TryGetValue(crane, out var sent) && Mathf.Abs(sent.Angle - state.Item1) < c_crane_threshold &&
                    Mathf.Abs(sent.Tether - state.Item2) < c_crane_threshold) continue;
                m_sentCranes[crane] = state;
                Send(WKTMovingPartKind.Crane, pair.Key, state.Item1, state.Item2, null);
            }
        }

        /// <summary>Platforms in a level that was just keyed, so the host can send where they are.</summary>
        public void IndexLevel(M_Level level) {
            foreach (var platform in level.GetComponentsInChildren<UT_Platform>(true)) {
                if (platform != null && WKTContentSeeding.TryGetKey(platform, out var key))
                    m_platforms[key] = platform;
            }
            foreach (var crane in level.GetComponentsInChildren<VEH_Crane>(true)) {
                if (crane != null && WKTContentSeeding.TryGetKey(crane, out var key))
                    m_cranes[key] = crane;
            }
        }

        private void Send(WKTMovingPartKind kind, ulong key, float value, float speed, WKTPlayer? except) {
            var writer = m_network.BeginMessage(WKTMessageId.MovingPart);
            writer.Put((byte)kind);
            writer.Put(key);
            writer.Put(value);
            writer.Put(speed);
            if (m_network.Mode == NetworkMode.Host)
                m_network.Broadcast(writer, SendType.Reliable, except);
            else
                m_network.SendToHost(writer, SendType.Reliable);
        }

        public void OnMessage(WKTPlayer sender, NetDataReader reader, WKTDoorSync doors) {
            if (!reader.TryGetByte(out var kindByte) || kindByte > (byte)WKTMovingPartKind.Crane || !reader.TryGetULong(out var key) ||
                !reader.TryGetFloat(out var value) || !reader.TryGetFloat(out var speed) || float.IsNaN(value) || float.IsNaN(speed)) return;
            var kind = (WKTMovingPartKind)kindByte;
            if (m_network.Mode == NetworkMode.Host) {
                // Clients only move doors by hand; platforms and cranes are the host's.
                if (sender.IsHost || kind != WKTMovingPartKind.Door) return;
                Send(kind, key, value, speed, sender);
            }
            else if (!sender.IsHost) return;
            m_applying = true;
            IsApplying = true;
            try {
                switch (kind) {
                    case WKTMovingPartKind.Door when doors.TryGetDoor(key, out var door):
                        MoveDoorTo(door!, Mathf.Clamp01(value));
                        break;
                    case WKTMovingPartKind.Platform when m_platforms.TryGetValue(key, out var platform) && platform != null:
                        platform.curPosition = value;
                        platform.SetMoveSpeed(speed);
                        break;
                    case WKTMovingPartKind.Crane when m_cranes.TryGetValue(key, out var crane) && crane != null:
                        // Its own Update turns the cab and sets the tether from these, and carries on from them until the next.
                        s_craneAngle(crane) = value;
                        s_craneTether(crane) = speed;
                        break;
                }
            }
            finally {
                m_applying = false;
                IsApplying = false;
            }
        }

        /// <summary>Moves a door to an open amount the way a crank does, so its own sounds and finish events play here too.</summary>
        private static void MoveDoorTo(UT_Door door, float amount) {
            if (door.moveSpeed <= 0f) return;
            var delta = (amount - s_doorAmount(door)) / door.moveSpeed;
            if (Mathf.Abs(delta) > 1e-5f)
                door.AddOpenAmount(delta);
        }
    }
}
