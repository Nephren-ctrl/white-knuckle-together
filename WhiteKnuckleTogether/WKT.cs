using System;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WhiteKnuckleTogether {
    public sealed class WKT : MonoBehaviour {
        private static WKT? s_instance;
        private static bool s_hasEverInitialized;

        public static WKT Instance {
            get {
                if (s_instance == null)
                    throw new Exception(s_hasEverInitialized ?
                        "WKT has already been destroyed. Use InstanceOrNull and manually check for null in " +
                        "non-deterministically ordered callbacks like OnDestroy." :
                        "WKT has not yet been initialized.");
                return s_instance;
            }
        }

        public static WKT? InstanceOrNull => s_instance.OrNull();

        public WKTAPI API { get; } = new();
        internal WKTNetwork Network { get; } = new();
        internal WKTSettings Settings { get; } = new();
        internal WKTLogger Logger { get; } = new();

        [UsedImplicitly]
        private void Awake() {
            s_instance = this;
            s_hasEverInitialized = true;
            WKTProgressionLock.Log = Logger.LogInfo;
            HookEvents();
            Logger.LogInfo("Initialized in scene.");
            Logger.LogInfo("Initializing network...");
            Network.Initialize();
            Logger.LogInfo("Initialized network.");
            gameObject.AddComponent<WKTVoiceManager>();
            ProcessSceneChange(SceneManager.GetActiveScene());
        }

        [UsedImplicitly]
        private void OnDestroy() {
            Network.Dispose();
            UnhookEvents();
            WKTToast.DestroyInstance();
            WKTWaitHud.DestroyInstance();
            WKTAssets.Unload();
            s_instance = null;
        }

        [UsedImplicitly]
        private void Update() => Network.Update(Time.realtimeSinceStartup);

        private void HookEvents() {
            SceneManager.sceneLoaded += OnSceneLoaded;
            Network.OnHostChanged += OnHostChanged;
            Network.OnHostStarted += OnSessionStarted;
            Network.OnClientStarted += OnSessionStarted;
        }

        private void UnhookEvents() {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Network.OnHostChanged -= OnHostChanged;
            Network.OnHostStarted -= OnSessionStarted;
            Network.OnClientStarted -= OnSessionStarted;
        }

        private static void OnSessionStarted() => WKTChat.Create();

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
            ProcessSceneChange(scene);
        }

        private void OnHostChanged() {
            var host = Network.HostPlayer;
            if (host == null) return;
            WKTToast.Show(host == Network.LocalPlayer ? "You are now the host" : $"{host.friend.Name} is now the host");
        }

        private void ProcessSceneChange(Scene scene) {
            switch (scene.name) {
                case WKTSceneConsts.MAIN_MENU:
                    OnMainMenuLoaded();
                    break;
                case WKTSceneConsts.GAME_MAIN:
                    OnGameMainLoaded();
                    break;
                case WKTSceneConsts.PLAYGROUND:
                    OnPlaygroundLoaded();
                    break;
            }
        }

        private void OnMainMenuLoaded() {
            Network.EndRun();
            WKTContentSeeding.End();
            // The menu's StatManager has just reloaded the save from disk, so the run's progression is already discarded.
            WKTProgressionGuard.RollbackAndRelease();
            new GameObject(nameof(WKTMainMenuUI)).AddComponent<WKTMainMenuUI>();
            Network.JoinPendingLobby();
        }

        private void OnGameMainLoaded() {
            if (Network.Mode == NetworkMode.None) return;
            new GameObject(nameof(WKTGameUI)).AddComponent<WKTGameUI>();
            if (!Network.IsMultiplayerRun) return;
            var run = new GameObject(nameof(WKTRunController));
            run.AddComponent<WKTRunController>();
            // Reads the controller in Awake, so it must come second.
            run.AddComponent<WKTPlayerSync>();
            // Reads the player sync in Awake, so it must come after it.
            run.AddComponent<WKTMultiplayerHud>();
            run.AddComponent<WKTVoicePausePanel>();
            run.AddComponent<WKTVoiceHud>();
            // Reads the player sync in Awake, so it must come after it.
            run.AddComponent<WKTItemStealing>();
            run.AddComponent<WKTGrabAssist>();
            run.AddComponent<WKTPlayerDeaths>();
            // Nobody comes back in a race.
            if (!Network.Session.IsRace)
                run.AddComponent<WKTDiskRevives>();
            // Reads the player sync in Awake, so it must come after it.
            run.AddComponent<WKTSpectator>();
            run.AddComponent<WKTShortcutSync>();
            // These disable themselves outside synced co-op runs.
            run.AddComponent<WKTWorldSync>();
            run.AddComponent<WKTDoorSync>();
            run.AddComponent<WKTGooSync>();
            run.AddComponent<WKTWorldPerks>();
            run.AddComponent<WKTLoadingSync>();
            run.AddComponent<WKTSharedRoaches>();
            run.AddComponent<WKTMotherJudgement>();
            run.AddComponent<WKTMotherTrades>();
            if (Network.Session.IsRace)
                run.AddComponent<WKTRace>();
        }

        private void OnPlaygroundLoaded() {

        }
    }
}