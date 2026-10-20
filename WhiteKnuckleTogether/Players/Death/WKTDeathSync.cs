using System;
using Steamworks.Data;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Which players of a multiplayer run are dead, the same on every machine. Each player owns their own death: only
    /// their machine ever decides it, whatever killed them, and sends <see cref="WKTMessageId.PlayerDeath"/> to the host.
    /// The host relays it to everyone else as <see cref="WKTMessageId.PlayerDeathRelay"/>, and never decides anyone's death
    /// but its own player's. It only owns what follows from them: once every player is dead, it ends the run for everyone
    /// with <see cref="WKTMessageId.RunOver"/>. Pure: transport comes from the caller.
    /// </summary>
    public sealed class WKTDeathSync {
        public const SendType SEND_TYPE = SendType.Reliable;
        /// <summary>A player died (true) or came back (false). Raised on every machine, for the local player too.</summary>
        public event Action<byte, bool>? DeathChanged;
        /// <summary>Every player is dead. Raised once per run.</summary>
        public event Action? RunOver;
        private readonly NetDataWriter m_writer = new();
        private readonly Action<NetDataWriter> m_sendToHost;
        private readonly Action<NetDataWriter, WKTPlayer?> m_broadcast;
        private readonly Func<byte, bool> m_isRemotePlayer;

        public WKTDeathRoster Roster { get; } = new();
        /// <summary>Changes when the local player takes over as host, or another player does.</summary>
        public bool IsHost { get; set; }
        public byte LocalPlayerId { get; }
        public bool IsLocalDead => Roster.IsDead(LocalPlayerId);
        public bool IsRunOver { get; private set; }

        /// <param name="isRemotePlayer">Whether an id belongs to a connected player other than the local one.</param>
        /// <param name="sendToHost">Client only. Sends reliably to the host.</param>
        /// <param name="broadcast">Host only. Sends reliably to every client except the given player.</param>
        public WKTDeathSync(bool isHost, byte localPlayerId, Func<byte, bool> isRemotePlayer, Action<NetDataWriter> sendToHost,
                            Action<NetDataWriter, WKTPlayer?> broadcast) {
            IsHost = isHost;
            LocalPlayerId = localPlayerId;
            m_isRemotePlayer = isRemotePlayer;
            m_sendToHost = sendToHost;
            m_broadcast = broadcast;
        }

        /// <summary>The local player died or came back on this machine. Returns whether that changed anything.</summary>
        public bool SetLocalDead(bool dead) {
            if (!Roster.Set(LocalPlayerId, dead)) return false;
            DeathChanged?.Invoke(LocalPlayerId, dead);
            SendLocal();
            return true;
        }

        /// <summary>
        /// Sends the local player's state again, such as to a new host after a migration, so everyone agrees on it even
        /// if the old host never relayed it.
        /// </summary>
        public void SendLocal() {
            var dead = IsLocalDead;
            if (IsHost) {
                WriteRelay(LocalPlayerId, dead);
                m_broadcast(m_writer, null);
                return;
            }
            WKTMessageDispatcher.BeginMessage(m_writer, WKTMessageId.PlayerDeath);
            m_writer.Put(dead);
            m_sendToHost(m_writer);
        }

        /// <summary>Host handler for <see cref="WKTMessageId.PlayerDeath"/>. Returns whether the state was accepted.</summary>
        public bool HandlePlayerDeath(WKTPlayer sender, NetDataReader reader) {
            // The sender comes from its connection, so a client can only ever report its own death.
            if (!IsHost || sender.IsHost || sender.Id == LocalPlayerId || !m_isRemotePlayer(sender.Id)) return false;
            if (!reader.TryGetBool(out var dead)) return false;
            var changed = Roster.Set(sender.Id, dead);
            // Relayed even when unchanged here, so a state sent again after a migration reaches everyone.
            WriteRelay(sender.Id, dead);
            m_broadcast(m_writer, sender);
            if (changed)
                DeathChanged?.Invoke(sender.Id, dead);
            return true;
        }

        /// <summary>Client handler for <see cref="WKTMessageId.PlayerDeathRelay"/>. Returns whether the state was accepted.</summary>
        public bool HandlePlayerDeathRelay(WKTPlayer sender, NetDataReader reader) {
            if (IsHost || !sender.IsHost) return false;
            // Nobody else decides the local player's death, the host included.
            if (!reader.TryGetByte(out var playerId) || playerId == LocalPlayerId || !m_isRemotePlayer(playerId)) return false;
            if (!reader.TryGetBool(out var dead)) return false;
            if (Roster.Set(playerId, dead))
                DeathChanged?.Invoke(playerId, dead);
            return true;
        }

        /// <summary>Client handler for <see cref="WKTMessageId.RunOver"/>. Returns whether it ended the run here.</summary>
        public bool HandleRunOver(WKTPlayer sender) {
            if (IsHost || !sender.IsHost) return false;
            return EndRun();
        }

        /// <summary>
        /// Host only. Ends the run for everyone once every player in <paramref name="playerIds"/> (everyone in the session,
        /// the host included) is dead. Never while players are still reconnecting after a migration, since they may be
        /// alive. Returns whether it ended the run.
        /// </summary>
        public bool TryEndRun(ReadOnlySpan<byte> playerIds, bool isAwaitingPlayers) {
            if (!IsHost || IsRunOver || isAwaitingPlayers || !Roster.AreAllDead(playerIds)) return false;
            WKTMessageDispatcher.BeginMessage(m_writer, WKTMessageId.RunOver);
            m_broadcast(m_writer, null);
            return EndRun();
        }

        /// <summary>Forgets a player who left. Not reported as coming back.</summary>
        public void Remove(byte playerId) {
            if (playerId != LocalPlayerId)
                Roster.Set(playerId, false);
        }

        private bool EndRun() {
            if (IsRunOver) return false;
            IsRunOver = true;
            RunOver?.Invoke();
            return true;
        }

        private void WriteRelay(byte playerId, bool dead) {
            WKTMessageDispatcher.BeginMessage(m_writer, WKTMessageId.PlayerDeathRelay);
            m_writer.Put(playerId);
            m_writer.Put(dead);
        }
    }
}
