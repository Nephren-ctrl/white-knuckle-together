using System;
using Steamworks.Data;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Each player's round trip to the host, in milliseconds, for the lobby's player list. The host measures every
    /// connection and sends the table to everyone every few seconds; the host itself shows 0. Unknown is
    /// <see cref="UNKNOWN"/>.
    /// </summary>
    internal sealed class WKTPings {
        public const ushort UNKNOWN = ushort.MaxValue;
        public const float SEND_INTERVAL = 2f;
        private readonly ushort[] m_pings = new ushort[byte.MaxValue + 1];
        private readonly WKTNetwork m_network;
        private float m_nextSend;

        public WKTPings(WKTNetwork network) {
            m_network = network;
            Clear();
        }

        /// <summary>Raised when new pings arrived or were measured.</summary>
        public event Action? Changed;

        public ushort Get(byte playerId) => m_pings[playerId];

        public void Clear() {
            for (var i = 0; i < m_pings.Length; i++)
                m_pings[i] = UNKNOWN;
            m_nextSend = 0f;
        }

        /// <summary>A ping as shown: whole milliseconds, capped, or none when unknown.</summary>
        public static string Format(ushort ping) => ping == UNKNOWN ? string.Empty : ping >= 999 ? "999+ ms" : ping + " ms";

        /// <summary>Clamps a measured ping into what is sent; negative (unknown) becomes <see cref="UNKNOWN"/>.</summary>
        public static ushort Clamp(int ping) => ping < 0 ? UNKNOWN : (ushort)Math.Min(ping, UNKNOWN - 1);

        /// <summary>Host only: measures every client's connection and sends the table, every <see cref="SEND_INTERVAL"/> seconds.</summary>
        public void Update(float time) {
            if (m_network.Mode != NetworkMode.Host || time < m_nextSend) return;
            m_nextSend = time + SEND_INTERVAL;
            var writer = m_network.BeginMessage(WKTMessageId.PlayerPings);
            var count = 0;
            foreach (var player in m_network.GetPlayersEnumerable())
                count++;
            writer.Put((byte)count);
            foreach (var player in m_network.GetPlayersEnumerable()) {
                var ping = player.IsHost ? (ushort)0 : Measure(player.Connection);
                m_pings[player.Id] = ping;
                writer.Put(player.Id);
                writer.Put(ping);
            }
            m_network.Broadcast(writer, SendType.Unreliable);
            Changed?.Invoke();
        }

        private static ushort Measure(Connection connection) {
            try {
                return Clamp(connection.QuickStatus().Ping);
            }
            catch (Exception) {
                return UNKNOWN;
            }
        }

        public void OnPings(WKTPlayer sender, NetDataReader reader) {
            if (!sender.IsHost || m_network.Mode != NetworkMode.Client || !reader.TryGetByte(out var count)) return;
            for (var i = 0; i < count; i++) {
                if (!reader.TryGetByte(out var id) || !reader.TryGetUShort(out var ping)) return;
                m_pings[id] = ping;
            }
            Changed?.Invoke();
        }
    }
}
