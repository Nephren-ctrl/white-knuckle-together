using System;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Which projectile types other players see a cosmetic copy of. Return rebar is left out: a plain copy would let the watching
    /// player recall someone else's rebar into their own bag, so <see cref="WKTReturnRebarSync"/> shares it instead. Grapples
    /// are left out: their tether needs the shooter.
    /// </summary>
    internal static class WKTPlayerShotTypes {
        public static readonly Type[] Shown = { typeof(Projectile), typeof(Projectile_Homing), typeof(Projectile_Homing_Rocket) };

        public static bool IsShown(Type type) => Array.IndexOf(Shown, type) >= 0;
    }
}
