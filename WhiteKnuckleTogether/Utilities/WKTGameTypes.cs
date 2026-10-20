using System;
using System.Reflection;

namespace WhiteKnuckleTogether {
    /// <summary>The game's types, for patches that find their targets by scanning them.</summary>
    internal static class WKTGameTypes {
        private static Type[]? s_types;

        /// <summary>Every type of the game assembly that loads. Types that cannot load are left out rather than failing the scan.</summary>
        public static Type[] All => s_types ??= Load(typeof(GameEntity).Assembly);

        public static Type[] Load(Assembly assembly) {
            try {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e) {
                return Array.FindAll(e.Types, t => t != null);
            }
        }
    }
}
