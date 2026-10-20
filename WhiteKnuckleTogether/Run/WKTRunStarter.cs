using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine.SceneManagement;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Starts a multiplayer run on this machine from a <see cref="WKTRunStart"/>. The host and every client go through
    /// the same path, so they all apply the same seed, gamemode settings, and shared progression.
    /// </summary>
    internal static class WKTRunStarter {
        private static readonly List<string> s_settingIds = new();

        /// <summary>
        /// Begins the run in <paramref name="session"/>, locks progression to the run's shared set, presets the seed, and
        /// loads the gamemode's scene. Only from the main menu. All or nothing: on failure, everything is undone.
        /// </summary>
        public static bool TryStart(WKTRunStart run, M_Gamemode gamemode, byte localPlayerId, WKTSession session) {
            var logger = WKT.Instance.Logger;
            if (SceneManager.GetActiveScene().name != WKTSceneConsts.MAIN_MENU) {
                logger.LogError("Cannot start a multiplayer run outside the main menu.");
                return false;
            }
            if (!session.TryBegin(run, localPlayerId)) {
                logger.LogError(session.IsRunActive ? "Cannot start a multiplayer run. One is already active." :
                    $"Cannot start a multiplayer run. Player {localPlayerId} has no spawn slot.");
                return false;
            }
            var active = session.Run;
            try {
                // Before the run's settings replace the player's own, which name the save entry the picks are under.
                WKTTrinkets.CaptureRunPicks(gamemode);
                WKTRunGamemodeSettings.Resolve(gamemode, active, s_settingIds);
                // Locks before anything else so nothing this run touches can ever be saved.
                WKTProgressionGuard.EngageForRun(active.SharedProgression);
                WKTSharedProgressionOverride.SetRunGamemodeSettings(gamemode.gamemodeName, s_settingIds);
                if (StatManager.saveData != null)
                    WKTSharedProgressionOverride.FilterSaveData(StatManager.saveData);
                // A pending singleplayer session save must never load into a multiplayer run, or be consumed by one.
                CL_SaveManager.loadSessionSaveOnLoad = false;
                WorldLoader.SetPresetSeed(active.Seed.ToString(CultureInfo.InvariantCulture));
                CL_GameManager.gamemode = gamemode;
                logger.LogInfo($"Starting multiplayer run: seed {active.Seed}, gamemode {active.Gamemode}, " +
                    $"settings [{string.Join(", ", s_settingIds)}], slot {session.LocalSlot} of {active.SlotCount}.");
                SceneManager.LoadScene(gamemode.gamemodeScene);
            }
            catch (Exception e) {
                logger.LogError($"Failed to start multiplayer run: {e}");
                Abort(session);
                return false;
            }
            if (UI_GamemodeScreen.instance != null && UI_GamemodeScreen.instance.loadingText != null)
                UI_GamemodeScreen.instance.loadingText.SetActive(true);
            return true;
        }

        private static void Abort(WKTSession session) {
            session.End();
            try {
                WorldLoader.SetPresetSeed("0");
            }
            finally {
                WKTProgressionGuard.RollbackAndRelease();
            }
        }
    }
}
