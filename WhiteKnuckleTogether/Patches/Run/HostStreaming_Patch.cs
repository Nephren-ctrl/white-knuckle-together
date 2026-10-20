using System;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>Runs <see cref="WKTHostStreaming"/> around the game's level loading pass.</summary>
    [WKTCriticalPatch(WKTPatchCategory.HostSimulation), HarmonyPatch(typeof(WorldLoader), "LoadUnload")]
    internal sealed class WorldLoaderLoadUnload_Patch {
        [UsedImplicitly]
        private static void Prefix(WorldLoader __instance) => WKTHostStreaming.BeforeLoadUnload(__instance);

        [UsedImplicitly]
        private static void Postfix(WorldLoader __instance) {
            if (WKTPlayerPositions.IsCoveringAllPlayers)
                WKTHostStreaming.AfterLoadUnload(__instance);
        }
    }

    /// <summary>Keeps the levels remote players need loaded, where the game would unload them.</summary>
    [WKTCriticalPatch(WKTPatchCategory.HostSimulation), HarmonyPatch]
    internal sealed class WorldLoaderCheckLevelLoad_Patch {
        /// <summary>The local function <c>CheckLevelLoad</c> inside <c>WorldLoader.LoadUnload</c>.</summary>
        public static MethodInfo FindTarget() {
            foreach (var method in typeof(WorldLoader).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)) {
                if (method.Name.Contains("<LoadUnload>g__CheckLevelLoad"))
                    return method;
            }
            throw new MissingMethodException(nameof(WorldLoader), "<LoadUnload>g__CheckLevelLoad");
        }

        [UsedImplicitly]
        private static MethodBase TargetMethod() => FindTarget();

        [UsedImplicitly]
        private static bool Prefix(WorldLoader.LevelInfo __0, WorldLoader.LevelInfo __1, ref bool __result) {
            if (!WKTHostStreaming.TryKeepLoaded(__0, __1)) return true;
            __result = true;
            return false;
        }
    }

    /// <summary>Unloads old levels below the lowest living player rather than the local one, on every machine of a synced run.</summary>
    [WKTCriticalPatch(WKTPatchCategory.HostSimulation), HarmonyPatch(typeof(WorldLoader), nameof(WorldLoader.UnloadPreviousLevels))]
    internal sealed class WorldLoaderUnloadPreviousLevels_Patch {
        [UsedImplicitly]
        private static bool Prefix(WorldLoader __instance, int offset) => !WKTHostStreaming.TryUnloadPreviousLevels(__instance, offset);
    }
}
