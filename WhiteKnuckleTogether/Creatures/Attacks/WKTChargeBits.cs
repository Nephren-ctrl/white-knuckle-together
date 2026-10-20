namespace WhiteKnuckleTogether {
    /// <summary>
    /// The AI bits of an enemy using the game's basic attack module: bit 0 is set while a charge or leap is underway and has
    /// not hit anything yet, and the other seven bits hold that attack's index plus one. Pure, so it can be tested without Unity.
    /// </summary>
    internal static class WKTChargeBits {
        public const int MAX_ATTACK_INDEX = 126;
        private const byte c_charging = 1;

        /// <summary>The bits for a charge with attack index <paramref name="attack"/>, or zero when none (or past the index limit).</summary>
        public static byte Encode(bool charging, int attack) =>
            charging && attack >= 0 && attack <= MAX_ATTACK_INDEX ? (byte)(c_charging | ((attack + 1) << 1)) : (byte)0;

        /// <summary>Whether a charge is underway, and with which attack.</summary>
        public static bool TryDecode(byte bits, out int attack) {
            attack = (bits >> 1) - 1;
            return (bits & c_charging) != 0 && attack >= 0;
        }
    }
}
