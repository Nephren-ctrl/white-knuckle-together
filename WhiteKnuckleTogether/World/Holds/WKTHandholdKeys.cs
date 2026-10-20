namespace WhiteKnuckleTogether {
    /// <summary>
    /// Names for handholds that every machine agrees on. A level's handholds use their level content keys. Handholds on
    /// anything shared at runtime are named from that object's own shared id (its root key, salted by what kind of object it
    /// is) and their place among the object's handholds, which is the same on every machine that built it from the same
    /// prefab. Pure, so it can be tested without Unity.
    /// </summary>
    internal static class WKTHandholdKeys {
        public static readonly ulong PitonSalt = WKTStableHash.Of("WKT.Handhold.Piton");
        public static readonly ulong PlacedRopeSalt = WKTStableHash.Of("WKT.Handhold.PlacedRope");
        public static readonly ulong ReturnRebarSalt = WKTStableHash.Of("WKT.Handhold.ReturnRebar");
        public static readonly ulong IdentitySalt = WKTStableHash.Of("WKT.Handhold.Identity");
        /// <summary>An enemy's shot resting where it hit, such as a grub aunt's climbable spike (see <see cref="WKTEnemyShots"/>).</summary>
        public static readonly ulong EnemyShotSalt = WKTStableHash.Of("WKT.Handhold.EnemyShot");

        /// <summary>The root key of a shared object of the kind <paramref name="salt"/> names, with the shared id <paramref name="id"/>.</summary>
        public static ulong GetRootKey(ulong salt, ulong id) => WKTStableHash.Mix(salt, id);

        /// <summary>The key of the <paramref name="index"/>th handhold under a root.</summary>
        public static ulong GetChildKey(ulong rootKey, int index) => WKTStableHash.Mix(rootKey, (ulong)index);
    }
}
