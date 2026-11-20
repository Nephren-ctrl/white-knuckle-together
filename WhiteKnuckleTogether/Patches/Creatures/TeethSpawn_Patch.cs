using System;
using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// <c>DEN_Teeth.Awake</c> plays <c>teeth:spawn</c> and shakes the camera. On a client that is its own copy waking (its
    /// level's trigger, or a spawn for the host's), not the host's Teeth spawning: a client that got there first heard it, then
    /// saw the host's spawn later in silence. A client's copy skips both; the host sends its spawn sound once the Teeth is
    /// registered (see <see cref="WKTWorldSync.Register"/>), and clients near it shake with it (see <see cref="WKTEntitySounds"/>).
    /// A client copy that wakes after the host's spawned, and is bound at once, plays the spawn itself where the Teeth is.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(DEN_Teeth), "Awake")]
    internal sealed class TeethSpawnCue_Patch {
        public const string SPAWN_SOUND = "teeth:spawn";
        public const string SPAWN_SET = "spawn";
        /// <summary>The shake <c>DEN_Teeth.Awake</c> gives, and how far from the Teeth a client still feels some of it.</summary>
        public const float SHAKE = 0.15f;
        public const float SHAKE_DISTANCE = 40f;
        private static readonly Action<float> s_noShake = _ => { };

        /// <summary>True while a client's Teeth runs its <c>Awake</c>: its sounds are skipped.</summary>
        public static bool IsMuting { get; private set; }

        [UsedImplicitly]
        private static void Prefix(out Action<float>? __state) {
            __state = null;
            if (WKTWorldSync.Instance is not { IsHost: false }) return;
            IsMuting = true;
            __state = CL_CameraControl.Shake;
            // Awake invokes the delegate unchecked, so it gets an empty one rather than null.
            CL_CameraControl.Shake = s_noShake;
        }

        [UsedImplicitly]
        private static void Finalizer(Action<float>? __state) {
            if (!IsMuting) return;
            IsMuting = false;
            CL_CameraControl.Shake = __state;
        }
    }
}
