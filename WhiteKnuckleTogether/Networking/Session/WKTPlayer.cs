using System;
using Steamworks;
using Steamworks.Data;

namespace WhiteKnuckleTogether {
    public sealed class WKTPlayer : IEquatable<WKTPlayer> {
        public readonly Friend friend;

        public byte Id { get; }
        /// <summary>Changes when the host leaves and this player takes over, or another player does.</summary>
        public bool IsHost { get; internal set; }
        /// <summary>The player's connection to the local host. Only meaningful on the host, for remote players.</summary>
        public Connection Connection { get; private set; }
        public ConnectionInfo Info { get; private set; }
        /// <summary>The player's real progression as reported on join. Only populated on the host.</summary>
        internal WKTProgressionProfile Profile { get; } = new();
        /// <summary>The color that stands for this player wherever they are named (see <see cref="WKTPlayerColors"/>).</summary>
        public UnityEngine.Color32 Color => WKTPlayerColors.Get(Id);
        /// <summary><see cref="Color"/> as "RRGGBB", for rich text tags.</summary>
        public string ColorHex => WKTPlayerColors.GetHex(Id);

        public WKTPlayer(Connection connection, ConnectionInfo info, Friend friend, byte id, bool isHost) {
            Connection = connection;
            Info = info;
            this.friend = friend;
            Id = id;
            IsHost = isHost;
        }

        public bool Equals(WKTPlayer? other) {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Id == other.Id;
        }

        public override bool Equals(object? obj) => ReferenceEquals(this, obj) || obj is WKTPlayer other && Equals(other);

        public override int GetHashCode() => Id;

        /// <summary>Points the player at a new connection after a host migration. Their id and identity stay the same.</summary>
        internal void Rebind(Connection connection, ConnectionInfo info) {
            Connection = connection;
            Info = info;
        }
    }
}
