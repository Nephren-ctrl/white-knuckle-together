using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>A save disk slotted into a computer revives the dead in a multiplayer run (see <see cref="WKTDiskRevives"/>).</summary>
    [HarmonyPatch(typeof(OS_FloppyReader), nameof(OS_FloppyReader.InsertFloppy))]
    internal sealed class DiskRevive_Patch {
        [UsedImplicitly]
        private static bool Prefix(OS_FloppyReader __instance, ENT_Player __0, Item __1) =>
            WKTDiskRevives.Instance is not { } revives || __0 == null || __1 == null || revives.BeforeInsert(__instance, __0, __1);
    }
}
