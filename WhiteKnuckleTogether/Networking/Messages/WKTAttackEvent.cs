using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Host to clients: an enemy's attack struck just now, such as a melee swing reaching its hit frame. Each client runs the
    /// same attack's hit check on its puppet against its own player. <see cref="Attack"/> is the attack's index on the enemy.
    /// </summary>
    public readonly struct WKTAttackEvent {
        public const int SIZE = sizeof(ushort) + sizeof(byte);

        public readonly ushort Handle;
        public readonly byte Attack;

        public WKTAttackEvent(ushort handle, byte attack) {
            Handle = handle;
            Attack = attack;
        }

        public void Write(NetDataWriter writer) {
            writer.Put(Handle);
            writer.Put(Attack);
        }

        /// <summary>Fails on truncation and handle 0.</summary>
        public static bool TryRead(NetDataReader reader, out WKTAttackEvent attackEvent) {
            attackEvent = default;
            if (!reader.TryGetUShort(out var handle) || handle == 0 || !reader.TryGetByte(out var attack)) return false;
            attackEvent = new WKTAttackEvent(handle, attack);
            return true;
        }
    }
}
