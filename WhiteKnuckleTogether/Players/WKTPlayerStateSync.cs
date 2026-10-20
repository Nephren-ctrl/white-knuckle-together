using System;
using Steamworks.Data;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Position sync over the star topology. Clients send <see cref="WKTMessageId.PlayerState"/> to the host. The host
    /// keeps it and relays it to every other client as <see cref="WKTMessageId.PlayerStateRelay"/>, together with its own
    /// state. Senders are identified by their connection on the host, never by the payload, so a client cannot move
    /// someone else. Pure: transport and time come from the caller.
    /// </summary>
    public sealed class WKTPlayerStateSync {
        public const SendType SEND_TYPE = SendType.Unreliable | SendType.NoNagle;
        private readonly WKTSnapshotBuffer?[] m_buffers = new WKTSnapshotBuffer?[byte.MaxValue + 1];
        private readonly NetDataWriter m_writer = new();
        private readonly WKTSendClock m_clock = new();
        private readonly Action<NetDataWriter> m_sendToHost;
        private readonly Action<NetDataWriter, WKTPlayer?> m_broadcast;
        private readonly Func<byte, bool> m_isRemotePlayer;

        /// <summary>Changes when the local player takes over as host, or another player does.</summary>
        public bool IsHost { get; set; }
        public byte LocalPlayerId { get; }

        /// <param name="isRemotePlayer">Whether an id belongs to a connected player other than the local one.</param>
        /// <param name="sendToHost">Client only. Sends unreliably to the host.</param>
        /// <param name="broadcast">Host only. Sends unreliably to every client except the given player.</param>
        public WKTPlayerStateSync(bool isHost, byte localPlayerId, Func<byte, bool> isRemotePlayer,
                                  Action<NetDataWriter> sendToHost, Action<NetDataWriter, WKTPlayer?> broadcast) {
            IsHost = isHost;
            LocalPlayerId = localPlayerId;
            m_isRemotePlayer = isRemotePlayer;
            m_sendToHost = sendToHost;
            m_broadcast = broadcast;
        }

        /// <summary>Sends the local pose if a send tick has passed. Returns whether it sent.</summary>
        public bool TrySendLocal(double realtime, in WKTPlayerPose pose) {
            if (!m_clock.TryAdvance(realtime, out var sequence)) return false;
            var state = new WKTPlayerState(sequence, pose);
            if (IsHost) {
                WriteRelay(LocalPlayerId, state);
                m_broadcast(m_writer, null);
            } else {
                WKTMessageDispatcher.BeginMessage(m_writer, WKTMessageId.PlayerState);
                state.Write(m_writer);
                m_sendToHost(m_writer);
            }
            return true;
        }

        /// <summary>Host handler for <see cref="WKTMessageId.PlayerState"/>. Returns whether the state was accepted.</summary>
        public bool HandlePlayerState(WKTPlayer sender, NetDataReader reader, double receiveTime) {
            if (!IsHost || sender.IsHost || sender.Id == LocalPlayerId || !m_isRemotePlayer(sender.Id)) return false;
            if (!WKTPlayerState.TryRead(reader, out var state)) return false;
            // Only relay what is new, so duplicates and stale packets are not amplified to every client.
            if (!GetOrCreateBuffer(sender.Id).Add(state, receiveTime)) return false;
            WriteRelay(sender.Id, state);
            m_broadcast(m_writer, sender);
            return true;
        }

        /// <summary>Client handler for <see cref="WKTMessageId.PlayerStateRelay"/>. Returns whether the state was accepted.</summary>
        public bool HandlePlayerStateRelay(WKTPlayer sender, NetDataReader reader, double receiveTime) {
            if (IsHost || !sender.IsHost) return false;
            if (!reader.TryGetByte(out var playerId) || playerId == LocalPlayerId || !m_isRemotePlayer(playerId)) return false;
            if (!WKTPlayerState.TryRead(reader, out var state)) return false;
            return GetOrCreateBuffer(playerId).Add(state, receiveTime);
        }

        /// <summary>The pose to show for <paramref name="playerId"/> at <paramref name="now"/>. False before any state arrived.</summary>
        public bool TrySample(byte playerId, double now, out WKTPlayerPose pose) {
            var buffer = m_buffers[playerId];
            if (buffer != null) return buffer.TrySample(now, out pose);
            pose = default;
            return false;
        }

        public bool HasState(byte playerId) => m_buffers[playerId]?.Count > 0;

        /// <summary>Forgets a player's states, e.g. when they leave. A rejoin under the same id starts a fresh timeline.</summary>
        public void Remove(byte playerId) => m_buffers[playerId]?.Clear();

        private void WriteRelay(byte playerId, in WKTPlayerState state) {
            WKTMessageDispatcher.BeginMessage(m_writer, WKTMessageId.PlayerStateRelay);
            m_writer.Put(playerId);
            state.Write(m_writer);
        }

        private WKTSnapshotBuffer GetOrCreateBuffer(byte playerId) => m_buffers[playerId] ??= new WKTSnapshotBuffer();
    }
}
