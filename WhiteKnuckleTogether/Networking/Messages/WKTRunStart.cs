using System;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Everything every machine needs to start the same multiplayer run: the world seed, the gamemode, the play mode, what the
    /// goo follows, the race orb's size, how players collide, the host's lobby modifiers, the shortcuts it ticked, each player's spawn slot, and the shared progression
    /// the run is restricted to. The host builds it and
    /// broadcasts it as the StartRun payload, and every machine (the host included) starts the run from it.
    /// </summary>
    public sealed class WKTRunStart {
        /// <summary><c>WorldLoader</c> treats a preset seed of 0 as "none", and picks random seeds below this.</summary>
        public const int MAX_SEED = 10000000;
        private static readonly ushort s_knownModifierMask = BuildKnownModifierMask();
        /// <summary>Indexed by raw gamemode byte. Avoids Enum.IsDefined, which boxes.</summary>
        private static readonly bool[] s_knownGamemodes = BuildKnown<GamemodeId>();
        /// <summary>Indexed by raw play mode byte.</summary>
        private static readonly bool[] s_knownPlayModes = BuildKnown<PlayModeId>();
        /// <summary>Indexed by raw goo follow byte.</summary>
        private static readonly bool[] s_knownGooFollows = BuildKnown<GooFollowId>();
        /// <summary>Indexed by raw orb radius byte.</summary>
        private static readonly bool[] s_knownOrbRadii = BuildKnown<OrbRadiusId>();
        /// <summary>Indexed by raw player collision byte.</summary>
        private static readonly bool[] s_knownPlayerCollisions = BuildKnown<PlayerCollisionId>();
        private readonly byte[] m_slotPlayerIds = new byte[WKTLobbyConsts.MAX_PLAYERS];

        public int Seed { get; set; }
        public GamemodeId Gamemode { get; set; }
        public PlayModeId PlayMode { get; set; }
        public GooFollowId GooFollow { get; set; }
        /// <summary>The race orb's size. Only used in a race.</summary>
        public OrbRadiusId OrbRadius { get; set; } = OrbRadiusId.Medium;
        public PlayerCollisionId PlayerCollision { get; set; } = PlayerCollisionId.Soft;
        /// <summary>One bit per <see cref="ModifierId"/>.</summary>
        public ushort ModifierMask { get; set; }
        /// <summary>
        /// One bit per <see cref="ShortcutId"/> the host ticked. Which are open also depends on the shared progression, see
        /// <see cref="GetOpenShortcutMask"/>.
        /// </summary>
        public byte ShortcutMask { get; set; }
        public int SlotCount { get; private set; }
        public WKTProgressionProfile SharedProgression { get; } = new();

        public static bool IsValidSeed(int seed) => seed > 0 && seed < MAX_SEED;

        public static ushort GetModifierBit(ModifierId modifier) => (ushort)(1 << (int)modifier);

        public bool HasModifier(ModifierId modifier) => (ModifierMask & GetModifierBit(modifier)) != 0;

        public void SetModifier(ModifierId modifier, bool active) {
            var bit = GetModifierBit(modifier);
            ModifierMask = (ushort)(active ? ModifierMask | bit : ModifierMask & ~bit);
        }

        /// <summary>
        /// The shortcuts open in this run: ticked by the host and unlocked by every player, and none with Iron Knuckle. Every
        /// machine works it out alike from the same payload.
        /// </summary>
        public byte GetOpenShortcutMask() =>
            WKTShortcuts.GetOpenMask(ShortcutMask, WKTShortcuts.GetAvailableMask(SharedProgression), HasModifier(ModifierId.IronKnuckle));

        /// <summary>Gives <paramref name="playerId"/> the next spawn slot. False if it already has one or slots are full.</summary>
        public bool AddSlot(byte playerId) {
            if (SlotCount >= m_slotPlayerIds.Length || TryGetSlot(playerId, out _)) return false;
            m_slotPlayerIds[SlotCount++] = playerId;
            return true;
        }

        public bool TryGetSlot(byte playerId, out byte slot) {
            for (var i = 0; i < SlotCount; i++) {
                if (m_slotPlayerIds[i] != playerId) continue;
                slot = (byte)i;
                return true;
            }
            slot = 0;
            return false;
        }

        public byte GetSlotPlayerId(int slot) {
            if ((uint)slot >= (uint)SlotCount)
                throw new ArgumentOutOfRangeException(nameof(slot));
            return m_slotPlayerIds[slot];
        }

        public void Clear() {
            Seed = 0;
            Gamemode = default;
            PlayMode = default;
            GooFollow = default;
            OrbRadius = OrbRadiusId.Medium;
            PlayerCollision = PlayerCollisionId.Soft;
            ModifierMask = 0;
            ShortcutMask = 0;
            SlotCount = 0;
            SharedProgression.Clear();
        }

        public void CopyFrom(WKTRunStart other) {
            if (ReferenceEquals(other, this)) return;
            Seed = other.Seed;
            Gamemode = other.Gamemode;
            PlayMode = other.PlayMode;
            GooFollow = other.GooFollow;
            OrbRadius = other.OrbRadius;
            PlayerCollision = other.PlayerCollision;
            ModifierMask = other.ModifierMask;
            ShortcutMask = other.ShortcutMask;
            SlotCount = other.SlotCount;
            Array.Copy(other.m_slotPlayerIds, m_slotPlayerIds, SlotCount);
            SharedProgression.CopyFrom(other.SharedProgression);
        }

        public void Write(NetDataWriter writer) {
            writer.Put(Seed);
            writer.Put((byte)Gamemode);
            writer.Put((byte)PlayMode);
            writer.Put((byte)GooFollow);
            writer.Put((byte)OrbRadius);
            writer.Put((byte)PlayerCollision);
            writer.Put(ModifierMask);
            writer.Put(ShortcutMask);
            writer.Put((byte)SlotCount);
            for (var i = 0; i < SlotCount; i++)
                writer.Put(m_slotPlayerIds[i]);
            SharedProgression.Write(writer);
        }

        /// <summary>
        /// Reads a payload written by <see cref="Write"/> into <paramref name="run"/> (cleared first). Rejects truncated
        /// data, invalid seeds, unknown gamemodes, play modes, goo follows, orb radii, player collisions, modifiers or
        /// shortcuts, no slots, too many slots, duplicate slot players, and a
        /// malformed profile. On failure the run is left cleared.
        /// </summary>
        public static bool TryRead(NetDataReader reader, WKTRunStart run) {
            run.Clear();
            if (TryReadFields(reader, run)) return true;
            run.Clear();
            return false;
        }

        private static bool TryReadFields(NetDataReader reader, WKTRunStart run) {
            if (!reader.TryGetInt(out var seed) || !IsValidSeed(seed)) return false;
            if (!reader.TryGetByte(out var gamemode) || !s_knownGamemodes[gamemode]) return false;
            if (!reader.TryGetByte(out var playMode) || !s_knownPlayModes[playMode]) return false;
            if (!reader.TryGetByte(out var gooFollow) || !s_knownGooFollows[gooFollow]) return false;
            if (!reader.TryGetByte(out var orbRadius) || !s_knownOrbRadii[orbRadius]) return false;
            if (!reader.TryGetByte(out var playerCollision) || !s_knownPlayerCollisions[playerCollision]) return false;
            if (!reader.TryGetUShort(out var modifierMask) || (modifierMask & ~s_knownModifierMask) != 0) return false;
            if (!reader.TryGetByte(out var shortcutMask) || (shortcutMask & ~WKTShortcuts.AllMask) != 0) return false;
            if (!reader.TryGetByte(out var slotCount) || slotCount == 0 || slotCount > WKTLobbyConsts.MAX_PLAYERS)
                return false;
            run.Seed = seed;
            run.Gamemode = (GamemodeId)gamemode;
            run.PlayMode = (PlayModeId)playMode;
            run.GooFollow = (GooFollowId)gooFollow;
            run.OrbRadius = (OrbRadiusId)orbRadius;
            run.PlayerCollision = (PlayerCollisionId)playerCollision;
            run.ModifierMask = modifierMask;
            run.ShortcutMask = shortcutMask;
            for (var i = 0; i < slotCount; i++) {
                if (!reader.TryGetByte(out var playerId) || !run.AddSlot(playerId)) return false;
            }
            return WKTProgressionProfile.TryRead(reader, run.SharedProgression);
        }

        private static bool[] BuildKnown<T>() where T : Enum {
            var known = new bool[byte.MaxValue + 1];
            foreach (var value in Enum.GetValues(typeof(T)))
                known[Convert.ToByte(value)] = true;
            return known;
        }

        private static ushort BuildKnownModifierMask() {
            ushort mask = 0;
            foreach (ModifierId modifier in Enum.GetValues(typeof(ModifierId)))
                mask |= GetModifierBit(modifier);
            return mask;
        }
    }
}
