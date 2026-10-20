using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The <see cref="WKTMessageId.HandLink"/> payload (Grab Assists). A client asks the host to take hold of another player's
    /// open hand, the host links the two hands for everyone, and either player letting go releases both.
    /// </summary>
    internal readonly struct WKTHandLink {
        public const int HAND_COUNT = 2;

        public WKTHandLinkKind Kind { get; }
        /// <summary>The player taking hold (request, link), or letting go (release).</summary>
        public byte PlayerId { get; }
        public byte Hand { get; }
        /// <summary>The player whose hand is taken hold of. Unused for a release.</summary>
        public byte OtherId { get; }
        public byte OtherHand { get; }

        public WKTHandLink(WKTHandLinkKind kind, byte playerId, byte hand, byte otherId = 0, byte otherHand = 0) {
            Kind = kind;
            PlayerId = playerId;
            Hand = hand;
            OtherId = otherId;
            OtherHand = otherHand;
        }

        public void Write(NetDataWriter writer) {
            writer.Put((byte)Kind);
            writer.Put(PlayerId);
            writer.Put(Hand);
            writer.Put(OtherId);
            writer.Put(OtherHand);
        }

        /// <summary>Rejects truncated data, unknown kinds, invalid hands and a player linking to themselves.</summary>
        public static bool TryRead(NetDataReader reader, out WKTHandLink link) {
            link = default;
            if (!reader.TryGetByte(out var kind) || !reader.TryGetByte(out var playerId) || !reader.TryGetByte(out var hand) ||
                !reader.TryGetByte(out var otherId) || !reader.TryGetByte(out var otherHand)) return false;
            if (kind > (byte)WKTHandLinkKind.Release || hand >= HAND_COUNT || otherHand >= HAND_COUNT) return false;
            if (kind != (byte)WKTHandLinkKind.Release && otherId == playerId) return false;
            link = new WKTHandLink((WKTHandLinkKind)kind, playerId, hand, otherId, otherHand);
            return true;
        }
    }
}
