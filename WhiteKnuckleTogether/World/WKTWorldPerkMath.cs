namespace WhiteKnuckleTogether {
    /// <summary>How other players' perks change the shared goo's speed, apart from Unity so it can be tested.</summary>
    internal static class WKTWorldPerkMath {
        public const string MASS_SPEED_BUFF = "massSpeedMult";
        public const string MASS_RESIST_BUFF = "massResist";
        public const string MASS_SLOW_BUFF = "massSlow";
        /// <summary>How far below a hunted player the goo may fall behind (<c>DEN_DeathFloor.Update</c>).</summary>
        public const float HUNT_LEASH = 40f;

        /// <summary>
        /// The factor that turns the goo's speed from what the host's own buffs give into what everyone's would. The goo
        /// multiplies its speed by (1 + speed), and divides it by (1 + resist) and by (1 + slow), each summed over every
        /// perk; the game only sums the host's, so the others' sums are folded in here.
        /// </summary>
        public static float GetMassFactor(float localSpeed, float localResist, float localSlow, float otherSpeed, float otherResist, float otherSlow) =>
            (1f + localSpeed + otherSpeed) / (1f + localSpeed) * ((1f + localResist) / (1f + localResist + otherResist)) *
            ((1f + localSlow) / (1f + localSlow + otherSlow));

        /// <summary>Whether a hunted player at <paramref name="playerHeight"/> is further above the goo than its leash allows.</summary>
        public static bool IsBeyondLeash(float playerHeight, float gooHeight) => playerHeight - gooHeight > HUNT_LEASH;
    }
}
