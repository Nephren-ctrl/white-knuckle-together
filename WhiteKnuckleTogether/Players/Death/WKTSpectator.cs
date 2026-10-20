using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// What a dead player sees in a multiplayer run. First the game's "You Died" popup, without the rest of its end of
    /// run, then a third person camera that orbits a living player. Look turns it, level geometry pushes it in, and left
    /// and right click switch to the previous and next living player. The dead player's body follows whoever they
    /// spectate, so the world streams in around what they watch. Lives next to <see cref="WKTPlayerDeaths"/>.
    /// </summary>
    [DefaultExecutionOrder(c_execution_order)]
    internal sealed class WKTSpectator : MonoBehaviour {
        /// <summary>After the game's own camera code and everything else that moves the camera in LateUpdate.</summary>
        private const int c_execution_order = 32000;
        /// <summary>Extra real seconds before the hidden popup is switched off, after its own fade.</summary>
        private const float c_popup_hide_margin = 0.1f;
        /// <summary>
        /// The goo sphere around the player's body. Its own camera draws it over everything, so on a dead body that follows
        /// whoever is spectated it would paint goo over them.
        /// </summary>
        private const string c_goo_surround_path = "EffectRoot/CorruptionSurround";
        private const string c_goo_surround_shader = "Dark Machine/Mass/Mass Only Corruption";
        /// <summary>The game's screen effect for lost health (<c>CL_UIManager</c> vignettes are named).</summary>
        private const string c_hurt_vignette = "hurt";
        /// <summary>The layers the game treats as level geometry.</summary>
        private static readonly string[] s_worldLayers = { "Default", "World", "Platform", "WorldNoBlockSight" };
        private readonly byte[] m_aliveIds = new byte[byte.MaxValue + 1];
        /// <summary>Parts of the score screen hidden while only its popup shows, put back afterwards.</summary>
        private readonly List<GameObject> m_hiddenForPopup = new();
        private WKTNetwork m_network = null!;
        private WKTPlayerSync m_playerSync = null!;
        private WKTSpectateHud m_hud = null!;
        private UI_ScoreScreen? m_popupScreen;
        private float m_popupOffTime = float.MaxValue;
        private State m_state;
        private float m_deathTime;
        private int m_target = WKTSpectateTargets.NONE;
        private float m_yaw;
        private float m_pitch;
        private float m_distance;
        private bool m_snapDistance;
        private int m_worldMask;
        private Renderer? m_gooSurround;

        [UsedImplicitly]
        private void Awake() {
            m_network = WKT.Instance.Network;
            m_playerSync = GetComponent<WKTPlayerSync>();
            m_worldMask = LayerMask.GetMask(s_worldLayers);
            m_hud = WKTSpectateHud.Create();
        }

        [UsedImplicitly]
        private void OnDestroy() {
            WKTDeadPlayerInput.Reset();
            SetGooSurroundVisible(true);
            SetCrosshairVisible(true);
            if (m_hud != null)
                Destroy(m_hud.gameObject);
        }

        [UsedImplicitly]
        private void Update() {
            UpdatePopup();
            var sync = WKTPlayerDeaths.Sync;
            if (sync == null || m_playerSync == null) return;
            // Everyone dead in co-op, or a race won or lost with players still alive.
            var runOver = sync.IsRunOver || WKTPlayerDeaths.IsRunEnded;
            switch (m_state) {
                case State.Alive when runOver:
                    m_state = State.RunOver;
                    break;
                case State.Alive when sync.IsLocalDead:
                    BeginDeath();
                    break;
                case State.DeathPopup or State.Spectating when runOver:
                    EndForRunOver();
                    break;
                case State.DeathPopup or State.Spectating when !sync.IsLocalDead:
                    Revive();
                    break;
                case State.DeathPopup when Time.unscaledTime - m_deathTime >= WKTDeathConsts.SPECTATE_DELAY:
                    BeginSpectating();
                    break;
                case State.Spectating:
                    UpdateTarget(sync);
                    break;
            }
        }

        [UsedImplicitly]
        private void LateUpdate() {
            if (m_state != State.Spectating || !TryGetTarget(out var remote)) return;
            var player = ENT_Player.playerObject;
            if (player == null || player.cam == null) return;
            var settings = SettingsManager.settings;
            WKTSpectateOrbit.ApplyLook(ref m_yaw, ref m_pitch, WKTDeadPlayerInput.Look, settings != null && settings.invertY);
            // Around the middle of their body, rather than their head, so the camera does not swing with every look around.
            var pivot = remote.BodyCenter;
            var forward = WKTSpectateOrbit.Forward(m_yaw, m_pitch);
            var allowed = WKTSpectateOrbit.GetAllowedDistance(CastBehind(pivot, -forward));
            m_distance = m_snapDistance ? allowed : WKTSpectateOrbit.StepDistance(m_distance, allowed, Time.unscaledDeltaTime);
            m_snapDistance = false;
            // Whatever narrowed the view, a death cutscene or a computer someone else uses, the spectator keeps their own.
            if (settings != null)
                player.cam.fieldOfView = settings.playerFOV;
            player.cam.transform.SetPositionAndRotation(pivot - forward * m_distance, Quaternion.Euler(m_pitch, m_yaw, 0f));
        }

        /// <summary>How far geometry lets the camera go from the body, or -1 for all the way.</summary>
        private float CastBehind(Vector3 pivot, Vector3 direction) {
            const float cast_distance = WKTDeathConsts.ORBIT_DISTANCE + WKTDeathConsts.CAMERA_SKIN;
            const QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore;
            // A sphere cast misses whatever it starts inside, such as a wall the head is pressed against, so a ray takes over.
            if (Physics.CheckSphere(pivot, WKTDeathConsts.CAMERA_RADIUS, m_worldMask, triggers))
                return Physics.Raycast(pivot, direction, out var rayHit, cast_distance, m_worldMask, triggers) ? rayHit.distance : -1f;
            return Physics.SphereCast(pivot, WKTDeathConsts.CAMERA_RADIUS, direction, out var hit, cast_distance, m_worldMask, triggers)
                ? hit.distance : -1f;
        }

        private void BeginDeath() {
            m_state = State.DeathPopup;
            m_deathTime = Time.unscaledTime;
            // From the moment of death, not only once spectating: a dead player does nothing in the world.
            WKTDeadPlayerInput.IsActive = true;
            SetGooSurroundVisible(false);
            // The hurt effect follows health only while the player is alive, so it would stay as it was at the death.
            if (CL_UIManager.instance != null)
                CL_UIManager.instance.SetVignette(c_hurt_vignette, 0f);
            ShowPopup();
        }

        private void BeginSpectating() {
            HidePopup();
            m_state = State.Spectating;
            m_target = WKTSpectateTargets.NONE;
            m_pitch = WKTDeathConsts.START_PITCH;
            SetCrosshairVisible(false);
            WKT.Instance.Logger.LogInfo("Spectating.");
        }

        /// <summary>A dead player came back. The game's own camera takes over again.</summary>
        private void Revive() {
            HidePopup();
            m_hud.Hide();
            WKTDeadPlayerInput.Reset();
            SetGooSurroundVisible(true);
            SetCrosshairVisible(true);
            m_target = WKTSpectateTargets.NONE;
            m_state = State.Alive;
        }

        /// <summary>The game's end of run takes over: its score screen must find itself as the game left it.</summary>
        private void EndForRunOver() {
            m_hud.Hide();
            WKTDeadPlayerInput.Reset();
            SetCrosshairVisible(true);
            m_target = WKTSpectateTargets.NONE;
            m_state = State.RunOver;
            if (m_popupScreen == null) return;
            m_popupScreen.youDiedPopup.gameObject.SetActive(false);
            m_popupScreen.gameObject.SetActive(false);
            RestorePopupHidden();
        }

        private void UpdateTarget(WKTDeathSync sync) {
            var localId = sync.LocalPlayerId;
            var count = 0;
            // In id order, so cycling is the same every time.
            for (var id = 0; id <= byte.MaxValue; id++) {
                if (id == localId || sync.Roster.IsDead((byte)id) || !m_network.TryGetPlayer((byte)id, out _) ||
                    !m_playerSync.TryGetRemote((byte)id, out _))
                    continue;
                m_aliveIds[count++] = (byte)id;
            }
            ReadOnlySpan<byte> aliveIds = m_aliveIds.AsSpan(0, count);
            var cycle = WKTDeadPlayerInput.TakeCycle();
            var target = cycle != 0 ? WKTSpectateTargets.Next(m_target, cycle, aliveIds) : WKTSpectateTargets.Resolve(m_target, aliveIds);
            if (target != m_target)
                SetTarget(target);
            if (!TryGetTarget(out var remote)) {
                m_hud.Hide();
                return;
            }
            if (m_network.TryGetPlayer(remote.PlayerId, out var targetPlayer) && targetPlayer != null)
                m_hud.Show(targetPlayer.friend.Name, GetTalk());
            FollowTarget(remote);
        }

        /// <summary>Whether the living can hear the local dead player, now or by holding focus.</summary>
        private WKTDeadTalk GetTalk() {
            if (!m_network.Session.DeadCanTalk) return WKTDeadTalk.None;
            return WKTVoiceManager.Instance is { IsTalkingToLiving: true } ? WKTDeadTalk.Talking : WKTDeadTalk.CanTalk;
        }

        private void SetTarget(int target) {
            m_target = target;
            if (!TryGetTarget(out var remote)) return;
            // From behind whoever is now spectated.
            m_yaw = remote.transform.eulerAngles.y;
            m_snapDistance = true;
            WKT.Instance.Logger.LogInfo($"Spectating player {target}.");
        }

        /// <summary>
        /// Keeps the dead body with the spectated player, so the levels around them stay loaded here and the world shifts
        /// with them. Dead bodies have no controller, so this touches nothing.
        /// </summary>
        private static void FollowTarget(WKTRemotePlayer remote) {
            var player = ENT_Player.playerObject;
            if (player != null && player.dead)
                player.transform.position = remote.transform.position;
        }

        /// <summary>Shows or hides the local body's goo sphere. Only a living player sees the goo around themselves.</summary>
        private void SetGooSurroundVisible(bool visible) {
            if (m_gooSurround == null)
                m_gooSurround = FindGooSurround();
            if (m_gooSurround != null)
                m_gooSurround.enabled = visible;
        }

        /// <summary>Shows or hides the game's crosshair, which means nothing to a spectator.</summary>
        private static void SetCrosshairVisible(bool visible) {
            var crosshair = CL_GameManager.gMan != null && CL_GameManager.gMan.uiMan != null ? CL_GameManager.gMan.uiMan.crosshairController : null;
            if (crosshair != null && crosshair.crosshairRoot != null)
                crosshair.crosshairRoot.gameObject.SetActive(visible);
        }

        private static Renderer? FindGooSurround() {
            var player = ENT_Player.playerObject;
            if (player == null) return null;
            var surround = player.transform.Find(c_goo_surround_path);
            if (surround != null && surround.TryGetComponent<Renderer>(out var renderer)) return renderer;
            foreach (var candidate in player.GetComponentsInChildren<MeshRenderer>(true)) {
                var material = candidate.sharedMaterial;
                if (material != null && material.shader != null && material.shader.name == c_goo_surround_shader) return candidate;
            }
            WKT.Instance.Logger.LogWarning("Found no goo sphere on the player. Spectators may see goo drawn over other players.");
            return null;
        }

        private bool TryGetTarget(out WKTRemotePlayer remote) {
            if (m_target != WKTSpectateTargets.NONE) return m_playerSync.TryGetRemote((byte)m_target, out remote);
            remote = null!;
            return false;
        }

        /// <summary>The score screen's "You Died" popup alone, the way its own animation shows it.</summary>
        private void ShowPopup() {
            var gameManager = CL_GameManager.gMan;
            var screen = gameManager != null && gameManager.statManager != null ? gameManager.statManager.GetScoreScreen() : null;
            if (screen == null || screen.youDiedPopup == null) return;
            m_hiddenForPopup.Clear();
            HideForPopup(screen.mainScorePanel != null ? screen.mainScorePanel.gameObject : null);
            HideForPopup(screen.scoreInfoButton);
            if (screen.settingsGameObjects != null) {
                foreach (var settingsObject in screen.settingsGameObjects)
                    HideForPopup(settingsObject);
            }
            screen.gameObject.SetActive(true);
            screen.youDiedPopup.gameObject.SetActive(true);
            screen.youDiedPopup.Show();
            m_popupScreen = screen;
            m_popupOffTime = float.MaxValue;
        }

        private void HideForPopup(GameObject? obj) {
            if (obj == null || !obj.activeSelf) return;
            obj.SetActive(false);
            m_hiddenForPopup.Add(obj);
        }

        private void HidePopup() {
            if (m_popupScreen == null || m_popupOffTime < float.MaxValue) return;
            m_popupScreen.youDiedPopup.Hide();
            m_popupOffTime = Time.unscaledTime + m_popupScreen.youDiedPopup.fadeOutLerpTime + c_popup_hide_margin;
        }

        /// <summary>Switches the popup's screen off once its fade has finished.</summary>
        private void UpdatePopup() {
            if (m_popupScreen == null || Time.unscaledTime < m_popupOffTime) return;
            m_popupScreen.youDiedPopup.gameObject.SetActive(false);
            m_popupScreen.gameObject.SetActive(false);
            RestorePopupHidden();
        }

        private void RestorePopupHidden() {
            foreach (var obj in m_hiddenForPopup) {
                if (obj != null)
                    obj.SetActive(true);
            }
            m_hiddenForPopup.Clear();
            m_popupScreen = null;
            m_popupOffTime = float.MaxValue;
        }

        private enum State : byte {
            Alive,
            /// <summary>"You Died" shows, and the camera stays where the player died.</summary>
            DeathPopup,
            Spectating,
            /// <summary>Every player is dead, and the game's own end of run has taken over.</summary>
            RunOver
        }
    }
}
