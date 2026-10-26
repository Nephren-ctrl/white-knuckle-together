using System;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    [BepInPlugin(GUID, NAME, VERSION)]
    public sealed class WKTPlugin : BaseUnityPlugin {
        public const string GUID = "danon5.WhiteKnuckleTogether";
        public const string NAME = "White Knuckle Together";
        public const string VERSION = "0.1.8";
        private readonly Harmony m_harmony = new(GUID);

        internal static WKTConfig? ModConfig { get; private set; }
        /// <summary>
        /// False if any <see cref="WKTCriticalPatchAttribute"/> patch failed to apply. Multiplayer must not start then,
        /// since progression could leak or worlds could desync.
        /// </summary>
        internal static bool IsMultiplayerAvailable { get; private set; }

        [UsedImplicitly]
        private void Awake() {
            ModConfig = new WKTConfig(Config);
            Logger.LogMessage("Patching...");
            ApplyPatches();
        }

        /// <summary>Patches one class at a time so a single broken patch is isolated and its criticality known.</summary>
        private void ApplyPatches() {
            var applied = 0;
            var failed = 0;
            var criticalFailed = 0;
            foreach (var type in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly())) {
                if (!type.IsDefined(typeof(HarmonyAttribute), false)) continue;
                var isCritical = type.IsDefined(typeof(WKTCriticalPatchAttribute), false);
                try {
                    var patched = m_harmony.CreateClassProcessor(type).Patch();
                    if (patched == null || patched.Count == 0)
                        throw new Exception("No methods were patched.");
                    applied++;
                }
                catch (Exception e) {
                    failed++;
                    if (isCritical)
                        criticalFailed++;
                    // BepInEx's interpolated-string overloads need C# 10, so messages are formatted up front.
                    Logger.LogError(string.Format("Failed to apply {0}patch {1}: {2}", isCritical ? "critical " : string.Empty, type.Name, e));
                }
            }
            IsMultiplayerAvailable = criticalFailed == 0;
            if (failed == 0)
                Logger.LogMessage(string.Format("Applied {0} patches.", applied));
            else
                Logger.LogError(string.Format("Applied {0} patches, {1} failed ({2} critical).", applied, failed, criticalFailed));
            if (!IsMultiplayerAvailable)
                Logger.LogError("Multiplayer is disabled because a critical patch failed. Singleplayer is unaffected.");
        }
    }
}
