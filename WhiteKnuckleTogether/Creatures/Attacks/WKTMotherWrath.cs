using System.Collections.Generic;
using Steamworks.Data;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Mother's wrath in a synced co-op run. Hurting a grub (its "player" damage effect), eating one or killing a slug grub
    /// enrages Mother (<c>DEN_Mother.Enrage</c>), who crushes the player a few seconds later (<c>DEN_Mother.KillPlayer</c>).
    /// The host simulates Mother and decides it:
    /// <list type="bullet">
    /// <item>The player whose act enraged her (<see cref="WKTDamageCause"/>, else whoever's machine called it) is noted when
    /// her crush starts. A client's own call is sent to the host instead of enraging its puppet.</item>
    /// <item>The crush kills that player, through a player effect when they are remote. Puppets never kill anyone.</item>
    /// <item>Her lines during it are shown to that player.</item>
    /// </list>
    /// Owned by <see cref="WKTWorldSync"/>.
    /// </summary>
    internal sealed class WKTMotherWrath {
        public const byte NOBODY = WKTDamageCause.NOBODY;
        /// <summary>The longest line Mother says, in characters.</summary>
        public const int MAX_LINE_LENGTH = 512;
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        /// <summary>Host only: whom each enraged Mother crushes, by her instance id.</summary>
        private readonly Dictionary<int, byte> m_culprits = new();

        public WKTMotherWrath(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
        }

        /// <summary>Whom an enraging act is for: the player whose hit set it off, else the player on the machine that called it.</summary>
        public static byte ChooseCulprit(byte cause, byte caller) => cause != NOBODY ? cause : caller;

        private byte LocalId => m_network.LocalPlayer?.Id ?? NOBODY;

        public void Clear() => m_culprits.Clear();

        /// <summary>
        /// Mother is about to be enraged. False skips the game's call: a client's goes to the host. The host notes whom she
        /// crushes, once per crush.
        /// </summary>
        public bool BeforeEnrage(DEN_Mother mother, bool crush) {
            if (!mother.TryGetComponent<WKTNetIdentity>(out var identity) || identity.Handle == 0) return true;
            var culprit = ChooseCulprit(WKTDamageCause.Current, LocalId);
            if (!m_sync.IsHost) {
                if (!identity.IsPuppet) return true;
                var writer = m_network.BeginMessage(WKTMessageId.MotherEnrage);
                writer.Put(identity.Handle);
                writer.Put(crush);
                writer.Put(culprit);
                m_network.SendToHost(writer, SendType.Reliable);
                return false;
            }
            // The same checks as the game's: only the call that starts her crush picks whom it is for.
            if (crush && !mother.angry && !mother.focused && mother.canAnger)
                m_culprits[mother.GetInstanceID()] = culprit;
            return true;
        }

        /// <summary>Host only: a client enraged its copy of Mother.</summary>
        public void OnEnrage(WKTPlayer sender, NetDataReader reader) {
            if (!m_sync.IsHost || sender.IsHost || !reader.TryGetUShort(out var handle) || !reader.TryGetBool(out var crush) ||
                !reader.TryGetByte(out var culprit) || !m_sync.TryGetIdentity(handle, out var identity) || identity!.Entity is not DEN_Mother mother) return;
            if (!m_network.TryGetPlayer(culprit, out _))
                culprit = sender.Id;
            var previous = WKTDamageCause.Enter(culprit);
            try {
                mother.Enrage(crush);
            }
            finally {
                WKTDamageCause.Exit(previous);
            }
        }

        /// <summary>
        /// Mother's crush lands. False skips the game's kill of the local player: always on clients, and on the host when the
        /// one she crushes is remote, who is killed on their own machine instead.
        /// </summary>
        public bool BeforeKillPlayer(DEN_Mother mother) {
            if (!m_sync.IsHost) return false;
            if (!m_culprits.Remove(mother.GetInstanceID(), out var culprit) || culprit == NOBODY || culprit == LocalId) return true;
            WKTTargets.SendEffect(culprit, WKTTargets.KillEffect("mother", null, mother.transform.position));
            return false;
        }

        /// <summary>
        /// Mother says something. False keeps it off this screen: on the host, a line of a crush meant for a remote player is
        /// shown to them instead.
        /// </summary>
        public bool BeforeLine(DEN_Mother mother, string? line) {
            if (!m_sync.IsHost) return true;
            // What she says about an item she was given is for everyone.
            if (WKTMotherTrades.IsTrading(mother) && mother.TryGetComponent<WKTNetIdentity>(out var trading) && trading.Handle != 0) {
                var broadcast = m_network.BeginMessage(WKTMessageId.MotherLine);
                broadcast.Put(trading.Handle);
                broadcast.Put(line ?? string.Empty, MAX_LINE_LENGTH);
                m_network.Broadcast(broadcast, SendType.Reliable);
                return true;
            }
            if (!m_culprits.TryGetValue(mother.GetInstanceID(), out var culprit) || culprit == NOBODY || culprit == LocalId) return true;
            if (!mother.TryGetComponent<WKTNetIdentity>(out var identity) || identity.Handle == 0 ||
                !m_network.TryGetPlayer(culprit, out var player) || player == null) return true;
            var writer = m_network.BeginMessage(WKTMessageId.MotherLine);
            writer.Put(identity.Handle);
            writer.Put(line ?? string.Empty, MAX_LINE_LENGTH);
            m_network.SendToPlayer(player, writer, SendType.Reliable);
            return false;
        }

        /// <summary>Client only: the host's Mother says something to the local player.</summary>
        public void OnLine(WKTPlayer sender, NetDataReader reader) {
            if (m_sync.IsHost || !sender.IsHost || !reader.TryGetUShort(out var handle) ||
                !WKTReaderUtility.TryGetBoundedString(reader, MAX_LINE_LENGTH, out var line) || !m_sync.TryGetIdentity(handle, out var identity) ||
                identity!.Entity is not DEN_Mother { psychicCommunicator: { } communicator }) return;
            communicator.PlayString(line);
        }
    }
}
