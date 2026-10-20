using System;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// One sound a player made. <see cref="Material"/> is the game's <c>ObjectMaterial.MaterialType</c> they stood on for
    /// <see cref="WKTPlayerSoundId.Footstep"/> and <see cref="WKTPlayerSoundId.LandMaterial"/>, the one of the handhold they
    /// took hold of for <see cref="WKTPlayerSoundId.Grab"/>, and 0 otherwise.
    /// </summary>
    public readonly struct WKTPlayerSound : IEquatable<WKTPlayerSound> {
        public const WKTPlayerSoundId MAX_ID = WKTPlayerSoundId.JumpBoostPrime;

        public WKTPlayerSoundId Id { get; }
        public byte Material { get; }

        public WKTPlayerSound(WKTPlayerSoundId id, byte material = 0) {
            Id = id;
            Material = material;
        }

        /// <summary>Whether <paramref name="id"/> is a sound that can be sent. <see cref="WKTPlayerSoundId.None"/> is not.</summary>
        public static bool IsDefined(WKTPlayerSoundId id) => id > WKTPlayerSoundId.None && id <= MAX_ID;

        public bool Equals(WKTPlayerSound other) => Id == other.Id && Material == other.Material;

        public override bool Equals(object? obj) => obj is WKTPlayerSound other && Equals(other);

        public override int GetHashCode() => (int)Id << 8 | Material;

        public override string ToString() => $"{Id} material {Material}";
    }
}
