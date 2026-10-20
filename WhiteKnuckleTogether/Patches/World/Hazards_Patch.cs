using System;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>A rising death wall starts once every player passed it, on every machine (see <see cref="WKTHazardSync"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(DEN_DeathwallManager), nameof(DEN_DeathwallManager.StartIfNoneActive))]
    internal sealed class DeathwallStart_Patch {
        [UsedImplicitly]
        private static bool Prefix(DEN_DeathwallManager __instance) => WKTWorldSync.Instance?.Hazards.BeforeDeathwallStart(__instance) ?? true;
    }

    /// <summary>Only the host's lightning timers strike; the host's strike reaches every client.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(ENV_LightningDischarge), "Update")]
    internal sealed class LightningTimer_Patch {
        private static readonly AccessTools.FieldRef<ENV_LightningDischarge, float> s_wait = AccessTools.FieldRefAccess<ENV_LightningDischarge, float>("strikeWaitTime");

        [UsedImplicitly]
        private static bool Prefix(ENV_LightningDischarge __instance) {
            var hazards = WKTWorldSync.Instance?.Hazards;
            if (hazards == null) return true;
            if (!hazards.BeforeLightningTimer()) return false;
            if (s_wait(__instance) <= 0f)
                hazards.OnLightningStrike(__instance);
            return true;
        }
    }

    /// <summary>A lightning strike runs on every machine; each player is only hurt by their own machine's (see <see cref="WKTSharedEffectScope"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class LightningStrikeScope_Patch {
        public static MethodBase GetTarget() {
            foreach (var nested in typeof(ENV_LightningDischarge).GetNestedTypes(BindingFlags.NonPublic)) {
                if (nested.Name.Contains("Discharge") && AccessTools.Method(nested, "MoveNext") is { } moveNext) return moveNext;
            }
            throw new InvalidOperationException("ENV_LightningDischarge's Discharge coroutine was not found.");
        }

        [UsedImplicitly]
        private static MethodBase TargetMethod() => GetTarget();

        [UsedImplicitly]
        private static void Prefix(out bool __state) {
            __state = WKTWorldSync.Instance != null;
            if (__state)
                WKTSharedEffectScope.Enter();
        }

        [UsedImplicitly]
        private static void Finalizer(bool __state) {
            if (__state)
                WKTSharedEffectScope.Exit();
        }
    }

    /// <summary>A flashflower flashing for anyone flashes for everyone; each player's own look decides whether it hurts them.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(ENV_Flashflower), nameof(ENV_Flashflower.StartFlash))]
    internal sealed class FlowerFlash_Patch {
        [UsedImplicitly]
        private static void Prefix(ENV_Flashflower __instance) {
            if (__instance.active)
                WKTWorldSync.Instance?.Hazards.OnFlowerFlash(__instance);
        }
    }

    /// <summary>A signal orchid killed by anyone dies for everyone, so its vine grows everywhere (see <see cref="WKTHazardSync"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(UT_Damage), nameof(UT_Damage.Kill))]
    internal sealed class OrchidKill_Patch {
        [UsedImplicitly]
        private static void Prefix(UT_Damage __instance) => WKTWorldSync.Instance?.Hazards.OnDamageKill(__instance);
    }
}
