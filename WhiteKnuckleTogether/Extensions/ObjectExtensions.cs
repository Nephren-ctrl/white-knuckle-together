using UnityEngine;

namespace WhiteKnuckleTogether {
    internal static class ObjectExtensions {
        public static T? OrNull<T>(this T? obj) where T : Object => obj != null ? obj : null;
    }
}