using System;
using HarmonyLib;
using JetBrains.Annotations;
using AttackClass = DenizenAttackModule_Basic.AttackClass;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// An enemy's melee attack striking on the machine that simulates it is replayed on every other machine against its own
    /// player; a puppet never strikes on its own.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(AttackClass), nameof(AttackClass.MeleeHitCheck))]
    internal sealed class EnemyMeleeHitCheck_Patch {
        [UsedImplicitly]
        private static bool Prefix(AttackClass __instance) {
            var sync = WKTWorldSync.Instance;
            if (sync == null) return true;
            if (!WKTEnemyHits.AllowPuppetAttack(__instance)) return false;
            sync.EnemyHits.OnLocalMeleeHitCheck(__instance);
            return true;
        }

        /// <summary>No exception from an attack may leave its enemy thinking it is still attacking, so it never attacks again.</summary>
        [UsedImplicitly]
        private static Exception? Finalizer(Exception? __exception, AttackClass __instance) {
            if (__exception != null)
                WKTEnemyHits.ResetAttack(__instance);
            return __exception;
        }
    }

    /// <summary>
    /// A crawler (grub aunt, grub sister, sprider) biting a player debuffs them through <c>GetComponent&lt;ENT_Player&gt;()</c>,
    /// which a remote player's stand-in on the host does not have (it copies the player's tags). The debuff is that
    /// player's own machine's to give, so the failed lookup at the end of the method is dropped there. A hack around game code
    /// that assumes one player.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(DEN_Crawler), "OnAttackHit")]
    internal sealed class CrawlerAttackHitProxy_Patch {
        [UsedImplicitly]
        private static Exception? Finalizer(Exception? __exception, GameEntity __0) =>
            __exception is NullReferenceException && __0 is WKTPlayerProxy ? null : __exception;
    }

    /// <summary>
    /// A puppet does not start attacks itself, such as from its replicated attack animation's events: the simulating machine's
    /// attacks reach everyone else as hit checks and projectiles.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(AttackClass), nameof(AttackClass.AttackActivate))]
    internal sealed class EnemyAttackActivate_Patch {
        [UsedImplicitly]
        private static bool Prefix(AttackClass __instance) => WKTEnemyHits.AllowPuppetAttack(__instance);
    }
}
