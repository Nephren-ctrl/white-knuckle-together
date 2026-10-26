using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Bear traps (Zeta traps) in a synced co-op run are simulated by the player they hold. A trap's trigger only reacts to each
    /// machine's own player, so a player stepping into a trap another machine simulates takes it over at once, as grabbing a
    /// prop does (<see cref="WKTAuthoritySync.OnLocalGrab"/>). The grab, pull and hurt then run on their machine as in
    /// singleplayer, and everyone sees it shut. A hit from anyone reaches that machine and frees them. Once it lets go it goes
    /// back to the host. A trap another machine simulates only catches the local player, and only while it is open and ready.
    /// </summary>
    internal static class WKTBearTraps {
        /// <summary>The trap animator's bool for being shut, which the simulating machine's animator state carries everywhere.</summary>
        public const string HOLDING_PARAMETER = "Holding";
        private static readonly AccessTools.FieldRef<ENV_BearTrap, float> s_recoveryTimer = AccessTools.FieldRefAccess<ENV_BearTrap, float>("recoveryTimer");
        /// <summary>Traps this machine took over by being caught in them, by handle, until they let go.</summary>
        private static readonly HashSet<ushort> s_caught = new();

        /// <summary>Whether a trap shown here as another machine's may catch whoever stepped in.</summary>
        public static bool PuppetMayGrab(bool local, bool shut, float recovery, bool dead) => local && !shut && recovery <= 0f && !dead;

        /// <summary>
        /// Replaces the start of <c>ENV_BearTrap.TryGrabEntity</c>. False when the trap must not catch <paramref name="target"/>
        /// here. When it catches the local player in a trap another machine simulates, this machine takes it over first.
        /// </summary>
        public static bool TryCatch(ENV_BearTrap trap, Transform target) {
            var sync = WKTWorldSync.Instance;
            if (sync == null || !WKTWorldSync.IsPuppet(trap)) return true;
            if (trap.grabbedEntity != null || !trap.TryGetComponent<WKTNetIdentity>(out var identity) || identity.Handle == 0) return false;
            var local = target.TryGetComponent<Hitbox>(out var hitbox) && hitbox.GetGameEntity() == ENT_Player.playerObject;
            var shut = trap.animator != null && trap.animator.GetBool(HOLDING_PARAMETER);
            if (!PuppetMayGrab(local, shut, s_recoveryTimer(trap), trap.dead)) return false;
            sync.Authority.OnLocalGrab(identity, 0, Vector3.zero);
            s_caught.Add(identity.Handle);
            return true;
        }

        /// <summary>
        /// After <c>ENV_BearTrap.LateUpdate</c>: a trap this machine took over gives itself back once it has let go. A trap that
        /// died holding its player keeps its owner, so the kill it claims from the host is accepted.
        /// </summary>
        public static void OnLateUpdate(ENV_BearTrap trap) {
            var sync = WKTWorldSync.Instance;
            if (sync == null || s_caught.Count == 0 || trap.grabbedEntity != null || !trap.TryGetComponent<WKTNetIdentity>(out var identity) ||
                !s_caught.Remove(identity.Handle) || trap.dead) return;
            sync.Authority.OnLocalRelease(identity);
            if (!sync.IsHost)
                WKTWorldSync.MakePuppet(identity);
        }

        /// <summary>
        /// A trap stops being simulated here: whoever it holds goes free, since the trap's pull only runs where it is simulated.
        /// Covers its holder dying, the host changing, a refused catch and two players stepping in at once.
        /// </summary>
        public static void OnStopSimulating(WKTNetIdentity identity) {
            if (identity.Entity is ENV_BearTrap { grabbedEntity: not null } trap)
                trap.ReleaseGrapple();
        }

        /// <summary>A trap taken over here from a machine that left it shut with nobody in it opens again.</summary>
        public static void OnTakenOver(WKTNetIdentity identity) {
            if (identity.Entity is ENV_BearTrap { grabbedEntity: null } trap)
                trap.ReleaseAnimation();
        }

        public static void Clear() => s_caught.Clear();
    }
}
