using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    [HarmonyPatch(typeof(CL_GameManager), "Start")]
    public sealed class GameManagerStart_Patch {
        [UsedImplicitly]
        public static void Prefix(CL_GameManager __instance) {
            if (WKT.InstanceOrNull != null) return;
            var wtk = new GameObject("WKT");
            wtk.AddComponent<WKT>();
            Object.DontDestroyOnLoad(wtk);
        }
    }
}