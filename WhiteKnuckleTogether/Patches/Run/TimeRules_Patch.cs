using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>Time runs at full speed in a multiplayer run, unless it waits for loading (see <see cref="WKTTimeRules"/>).</summary>
    [HarmonyPatch(typeof(CL_GameManager), nameof(CL_GameManager.RefreshTimescale))]
    internal sealed class MultiplayerTimescale_Patch {
        private static readonly AccessTools.FieldRef<float> s_perceived = AccessTools.StaticFieldRefAccess<float>(AccessTools.Field(typeof(CL_GameManager), "playerPerceivedTimeScale"));

        [UsedImplicitly]
        private static void Postfix() {
            if (WKT.InstanceOrNull?.Network is not { IsMultiplayerRun: true }) return;
            Time.timeScale = WKTTimeRules.Resolve(CL_GameManager.GetBaseTimescale(), WKTLoadingSync.Waiting > 0);
            s_perceived() = 1f;
        }
    }

    /// <summary>Hits never freeze time in a multiplayer run.</summary>
    [HarmonyPatch(typeof(CL_GameManager), nameof(CL_GameManager.FreezeForTime))]
    internal sealed class MultiplayerHitFreeze_Patch {
        [UsedImplicitly]
        private static bool Prefix() => WKT.InstanceOrNull?.Network is not { IsMultiplayerRun: true };
    }

    /// <summary>The game's own loading freeze is told to the host, which holds every machine while anyone loads.</summary>
    [HarmonyPatch(typeof(CL_GameManager), nameof(CL_GameManager.SetLoading))]
    internal sealed class LoadingState_Patch {
        [UsedImplicitly]
        private static void Postfix(bool b) {
            WKTLoadingSync.Instance?.OnLocalLoading(b);
            // SetLoading sets the time scale itself; the run's rules apply on top.
            CL_GameManager.RefreshTimescale();
        }
    }
}
