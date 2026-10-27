using System.Collections.Generic;
using Steamworks.Data;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Every other player's perks and their stacks, on every machine. Each player sends their own list when it changes; the
    /// host keeps it and relays it to everyone else. Enemy code asks the player it goes for about a perk (a hopper leaves a
    /// player with Pheromone Glands alone, see <see cref="WKTTargets.HasPerkOf"/>), spawn tables count a perk any player has,
    /// and the host applies other players' perks that act on the world (see <see cref="WKTWorldPerks"/>). Each client also
    /// sends the host its mass buffs, whatever gave them (perks, items, events), which the shared goo goes by. Owned by
    /// <see cref="WKTWorldSync"/>.
    /// </summary>
    internal sealed class WKTPlayerPerks {
        /// <summary>The most perks a player's list carries.</summary>
        public const int MAX_PERKS = 64;
        private const float c_poll_interval = 1f;
        /// <summary>The largest mass buff sum a client may report: far beyond what any stack of perks gives.</summary>
        private const float c_max_mass_buff = 100f;
        private static readonly Dictionary<byte, Dictionary<string, int>> s_byPlayer = new();
        /// <summary>Host only: each client's mass buffs (speed, resist, slow), as its machine last said.</summary>
        private static readonly Dictionary<byte, Vector3> s_massBuffs = new();
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        /// <summary>The local perks last sent, in the player's order.</summary>
        private readonly List<WKTPerkEntry> m_sent = new();
        private readonly List<WKTPerkEntry> m_scratch = new();
        private double m_nextPoll;
        private bool m_sentOnce;
        private Vector3 m_sentMass;

        public WKTPlayerPerks(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
        }

        /// <summary>Whether another player has a perk, as their machine last said.</summary>
        public static bool Has(byte playerId, string? perkId) =>
            perkId != null && s_byPlayer.TryGetValue(playerId, out var perks) && perks.ContainsKey(perkId);

        /// <summary>Whether any other player has a perk.</summary>
        public static bool HasAny(string? perkId) {
            if (perkId == null) return false;
            foreach (var perks in s_byPlayer.Values)
                if (perks.ContainsKey(perkId)) return true;
            return false;
        }

        /// <summary>Another player's perks, by id with their stack. Null if they never said.</summary>
        public static IReadOnlyDictionary<string, int>? Get(byte playerId) => s_byPlayer.TryGetValue(playerId, out var perks) ? perks : null;

        /// <summary>The players whose perks are known.</summary>
        public static IEnumerable<byte> Players => s_byPlayer.Keys;

        /// <summary>Stores a player's perks, replacing what they had.</summary>
        public static void Set(byte playerId, IReadOnlyList<WKTPerkEntry> entries) {
            if (!s_byPlayer.TryGetValue(playerId, out var perks)) {
                perks = new Dictionary<string, int>();
                s_byPlayer[playerId] = perks;
            }
            perks.Clear();
            foreach (var entry in entries)
                perks[entry.Id] = entry.Stack;
        }

        /// <summary>Host only: a client's mass buffs (speed, resist, slow), whatever gave them. False until it said.</summary>
        public static bool TryGetMassBuffs(byte playerId, out Vector3 buffs) => s_massBuffs.TryGetValue(playerId, out buffs);

        public static void Remove(byte playerId) {
            s_byPlayer.Remove(playerId);
            s_massBuffs.Remove(playerId);
        }

        public void Clear() {
            s_byPlayer.Clear();
            s_massBuffs.Clear();
            m_sentMass = Vector3.zero;
            m_sent.Clear();
            m_nextPoll = 0;
            m_sentOnce = false;
        }

        /// <summary>Sends the local player's perks when they changed, checked once a second.</summary>
        public void Update(double now) {
            if (now < m_nextPoll) return;
            m_nextPoll = now + c_poll_interval;
            var player = ENT_Player.playerObject;
            var local = m_network.LocalPlayer;
            if (player != null && !m_sync.IsHost)
                SendMassBuffs(player);
            if (player == null || player.perks == null || local == null || (m_sentOnce && Matches(player.perks))) return;
            m_sent.Clear();
            foreach (var perk in player.perks) {
                if (m_sent.Count == MAX_PERKS) break;
                if (IsSendable(perk))
                    m_sent.Add(new WKTPerkEntry(perk.id, ClampStack(perk.GetStackAmount())));
            }
            m_sentOnce = true;
            if (m_sync.IsHost) {
                Broadcast(local.Id, m_sent, null);
                return;
            }
            var writer = m_network.BeginMessage(WKTMessageId.PlayerPerks);
            Write(writer, m_sent);
            m_network.SendToHost(writer, SendType.Reliable);
        }

        /// <summary>Host only: tells a player who just arrived everyone's perks.</summary>
        public void SendAllTo(WKTPlayer player) {
            if (!m_sync.IsHost) return;
            var local = m_network.LocalPlayer;
            if (local != null && m_sentOnce)
                SendTo(player, local.Id, m_sent);
            foreach (var pair in s_byPlayer) {
                if (pair.Key == player.Id) continue;
                m_scratch.Clear();
                foreach (var perk in pair.Value)
                    m_scratch.Add(new WKTPerkEntry(perk.Key, perk.Value));
                SendTo(player, pair.Key, m_scratch);
            }
            m_scratch.Clear();
        }

        /// <summary>A client's own perks reaching the host, or anyone's relayed by the host.</summary>
        public void OnPerks(WKTPlayer sender, NetDataReader reader) {
            if (m_sync.IsHost) {
                if (sender.IsHost || !TryRead(reader, m_scratch)) return;
                Set(sender.Id, m_scratch);
                Broadcast(sender.Id, m_scratch, sender);
                m_scratch.Clear();
                return;
            }
            if (!sender.IsHost || !reader.TryGetByte(out var playerId) || !TryRead(reader, m_scratch)) return;
            var local = m_network.LocalPlayer;
            if (local == null || playerId != local.Id)
                Set(playerId, m_scratch);
            m_scratch.Clear();
        }

        /// <summary>
        /// Client only: sends the host the local player's mass buffs, every poll while any is set (so a new host learns them
        /// too) and once more when they all went back to none.
        /// </summary>
        private void SendMassBuffs(ENT_Player player) {
            var buffs = player.curBuffs;
            if (buffs == null) return;
            var mass = new Vector3(buffs.GetBuff(WKTWorldPerkMath.MASS_SPEED_BUFF), buffs.GetBuff(WKTWorldPerkMath.MASS_RESIST_BUFF),
                buffs.GetBuff(WKTWorldPerkMath.MASS_SLOW_BUFF));
            if (mass == Vector3.zero && m_sentMass == Vector3.zero) return;
            m_sentMass = mass;
            var writer = m_network.BeginMessage(WKTMessageId.PlayerMassBuffs);
            writer.Put(mass.x);
            writer.Put(mass.y);
            writer.Put(mass.z);
            m_network.SendToHost(writer, SendType.Reliable);
        }

        public void OnMassBuffs(WKTPlayer sender, NetDataReader reader) {
            if (!m_sync.IsHost || sender.IsHost || !reader.TryGetFloat(out var speed) || !reader.TryGetFloat(out var resist) || !reader.TryGetFloat(out var slow) ||
                !IsMassBuff(speed) || !IsMassBuff(resist) || !IsMassBuff(slow)) return;
            s_massBuffs[sender.Id] = new Vector3(speed, resist, slow);
        }

        private static bool IsMassBuff(float value) => value >= -c_max_mass_buff && value <= c_max_mass_buff;

        public static void Write(NetDataWriter writer, IReadOnlyList<WKTPerkEntry> entries) {
            writer.Put((byte)entries.Count);
            foreach (var entry in entries) {
                writer.Put(entry.Id, WKTWire.MAX_ID_LENGTH);
                writer.Put(ClampStack(entry.Stack));
            }
        }

        /// <summary>Fails on truncation, more than <see cref="MAX_PERKS"/> perks, empty or overlong ids, and stacks of zero.</summary>
        public static bool TryRead(NetDataReader reader, List<WKTPerkEntry> entries) {
            entries.Clear();
            if (!reader.TryGetByte(out var count) || count > MAX_PERKS) return false;
            for (var i = 0; i < count; i++) {
                if (!WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var perkId) || string.IsNullOrEmpty(perkId) ||
                    !reader.TryGetByte(out var stack) || stack == 0) {
                    entries.Clear();
                    return false;
                }
                entries.Add(new WKTPerkEntry(perkId, stack));
            }
            return true;
        }

        private void Broadcast(byte playerId, IReadOnlyList<WKTPerkEntry> entries, WKTPlayer? except) {
            var writer = m_network.BeginMessage(WKTMessageId.PlayerPerks);
            writer.Put(playerId);
            Write(writer, entries);
            m_network.Broadcast(writer, SendType.Reliable, except);
        }

        private void SendTo(WKTPlayer player, byte playerId, IReadOnlyList<WKTPerkEntry> entries) {
            var writer = m_network.BeginMessage(WKTMessageId.PlayerPerks);
            writer.Put(playerId);
            Write(writer, entries);
            m_network.SendToPlayer(player, writer, SendType.Reliable);
        }

        private bool Matches(List<Perk> perks) {
            var index = 0;
            foreach (var perk in perks) {
                if (!IsSendable(perk)) continue;
                if (index == MAX_PERKS) break;
                if (index >= m_sent.Count || m_sent[index].Id != perk.id || m_sent[index].Stack != ClampStack(perk.GetStackAmount())) return false;
                index++;
            }
            return index == m_sent.Count;
        }

        private static bool IsSendable(Perk? perk) => perk != null && !string.IsNullOrEmpty(perk.id) && perk.id.Length <= WKTWire.MAX_ID_LENGTH;

        private static byte ClampStack(int stack) => (byte)(stack < 1 ? 1 : stack > byte.MaxValue ? byte.MaxValue : stack);
    }
}
