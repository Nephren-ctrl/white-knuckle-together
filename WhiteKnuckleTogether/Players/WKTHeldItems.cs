using System.Collections.Generic;
using Steamworks.Data;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>What a player holds in each hand: the item prefab names (<c>Item.prefabName</c>), empty for none.</summary>
    public readonly struct WKTHeldItemsState {
        public readonly byte PlayerId;
        public readonly string Left;
        public readonly string Right;

        public WKTHeldItemsState(byte playerId, string? left, string? right) {
            PlayerId = playerId;
            Left = left ?? string.Empty;
            Right = right ?? string.Empty;
        }

        public string Get(int hand) => hand == 0 ? Left : Right;

        public void Write(NetDataWriter writer) {
            writer.Put(PlayerId);
            writer.Put(Left, WKTWire.MAX_ID_LENGTH);
            writer.Put(Right, WKTWire.MAX_ID_LENGTH);
        }

        public static bool TryRead(NetDataReader reader, out WKTHeldItemsState state) {
            state = default;
            if (!reader.TryGetByte(out var playerId) || !WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var left) ||
                !WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var right)) return false;
            state = new WKTHeldItemsState(playerId, left, right);
            return true;
        }
    }

    /// <summary>
    /// Shares what each player holds in their hands, so other players see the item in that hand. Sent when it changes, through
    /// the host, which keeps everyone's for players who join later. Owned by <see cref="WKTPlayerSync"/>.
    /// </summary>
    internal sealed class WKTHeldItems {
        private readonly WKTNetwork m_network;
        private readonly Dictionary<byte, WKTHeldItemsState> m_states = new();
        private string m_sentLeft = string.Empty;
        private string m_sentRight = string.Empty;
        private bool m_sentOnce;

        public WKTHeldItems(WKTNetwork network) => m_network = network;

        public bool TryGet(byte playerId, out WKTHeldItemsState state) => m_states.TryGetValue(playerId, out state);

        public void Forget(byte playerId) => m_states.Remove(playerId);

        /// <summary>Sends the local player's held items when they changed.</summary>
        public void Update(ENT_Player player) {
            var local = m_network.LocalPlayer;
            if (local == null) return;
            var left = GetPrefabName(player, 0);
            var right = GetPrefabName(player, 1);
            if (m_sentOnce && left == m_sentLeft && right == m_sentRight) return;
            m_sentOnce = true;
            m_sentLeft = left;
            m_sentRight = right;
            var state = new WKTHeldItemsState(local.Id, left, right);
            var writer = m_network.BeginMessage(WKTMessageId.HeldItems);
            state.Write(writer);
            if (m_network.Mode == NetworkMode.Host)
                m_network.Broadcast(writer, SendType.Reliable);
            else
                m_network.SendToHost(writer, SendType.Reliable);
        }

        public void OnMessage(WKTPlayer sender, NetDataReader reader) {
            if (!WKTHeldItemsState.TryRead(reader, out var state)) return;
            if (m_network.Mode == NetworkMode.Host) {
                // A client speaks only for itself; the host passes it on.
                if (sender.IsHost || state.PlayerId != sender.Id) return;
                var relay = m_network.BeginMessage(WKTMessageId.HeldItems);
                state.Write(relay);
                m_network.Broadcast(relay, SendType.Reliable, sender);
            }
            else if (!sender.IsHost) return;
            m_states[state.PlayerId] = state;
        }

        /// <summary>Host only: everyone's held items, for a player who just joined.</summary>
        public void SendAll(WKTPlayer player) {
            foreach (var state in m_states.Values) {
                if (state.PlayerId == player.Id) continue;
                var writer = m_network.BeginMessage(WKTMessageId.HeldItems);
                state.Write(writer);
                m_network.SendToPlayer(player, writer, SendType.Reliable);
            }
            if (m_network.LocalPlayer is not { } local) return;
            var own = m_network.BeginMessage(WKTMessageId.HeldItems);
            new WKTHeldItemsState(local.Id, m_sentLeft, m_sentRight).Write(own);
            m_network.SendToPlayer(player, own, SendType.Reliable);
        }

        public static string GetPrefabName(ENT_Player player, int hand) {
            var hands = player.hands;
            if (hands == null || hand >= hands.Length || hands[hand]?.inventoryHand?.currentItem is not { } item) return string.Empty;
            var name = item.prefabName;
            return string.IsNullOrEmpty(name) || name.Length > WKTWire.MAX_ID_LENGTH ? string.Empty : name;
        }
    }
}
