using System;
using JetBrains.Annotations;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using SendType = Steamworks.Data.SendType;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Sends the local player's pose and sounds, and shows and plays everyone else's in Game-Main. Lives next to <see cref="WKTRunController"/>,
    /// so it only exists during a multiplayer run, and its message handlers are only registered while it does.
    /// <para>
    /// Runs late in LateUpdate: after the game carries the local player with what they stand on (<c>ENT_Player.LateUpdate</c>)
    /// and after every mover and animator moved this frame. The local pose is read once it is final, and others are placed on
    /// moving things where those things are drawn this frame, rather than a frame behind, which made riders jitter.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(EXECUTION_ORDER)]
    internal sealed class WKTPlayerSync : MonoBehaviour {
        /// <summary>After the game's own LateUpdates and before the spectator camera follows whoever it watches.</summary>
        public const int EXECUTION_ORDER = 10000;
        /// <summary>The game's <c>ENT_Player.Hand.id</c> of each hand, which is also its index in <c>ENT_Player.hands</c>.</summary>
        private const int c_left_hand = 0;
        private const int c_right_hand = 1;
        private readonly WKTRemotePlayer?[] m_remotes = new WKTRemotePlayer?[byte.MaxValue + 1];
        private WKTNetwork m_network = null!;
        private WKTRunController m_runController = null!;
        private WKTPlayerStateSync? m_sync;
        private WKTHeldItems m_heldItems = null!;
        private WKTPlayerLights m_lights = null!;
        private WKTPlayerBodies m_bodies = null!;
        private WKTAimMarkers m_aimMarkers = null!;
        private WKTTethers m_tethers = null!;
        private readonly WKTAvatarPreview m_preview = new();
        private readonly WKTPlatforms m_platforms = new();
        private bool m_previewRegistered;
        private Action<WKTPlayer> m_onPlayerConnected = null!;
        private Action<WKTPlayer> m_onPlayerDisconnected = null!;
        private Action<WKTPlayer> m_onPlayerMemberDataChanged = null!;
        private Action m_onHostChanged = null!;
        private int m_remoteCount;
        private ulong m_sentRoom = WKTPlayerRooms.UNKNOWN;
        private float m_nextRoomSend;

        /// <summary>The player sync of the multiplayer run in progress, if any.</summary>
        public static WKTPlayerSync? Instance { get; private set; }

        [UsedImplicitly]
        private void Awake() {
            Instance = this;
            m_network = WKT.Instance.Network;
            m_runController = GetComponent<WKTRunController>();
            var localPlayer = m_network.LocalPlayer;
            if (localPlayer == null) {
                WKT.Instance.Logger.LogError("Cannot sync player positions. There is no local player.");
                enabled = false;
                return;
            }
            m_sync = new WKTPlayerStateSync(m_network.Mode == NetworkMode.Host, localPlayer.Id, IsRemotePlayer,
                SendToHost, Broadcast);
            m_network.Messages.Register(WKTMessageId.PlayerState, OnPlayerState);
            m_network.Messages.Register(WKTMessageId.PlayerStateRelay, OnPlayerStateRelay);
            m_heldItems = new WKTHeldItems(m_network);
            m_network.Messages.Register(WKTMessageId.HeldItems, m_heldItems.OnMessage);
            m_lights = new WKTPlayerLights(m_network);
            m_network.Messages.Register(WKTMessageId.PlayerLight, m_lights.OnMessage);
            m_bodies = new WKTPlayerBodies(m_network);
            m_network.Messages.Register(WKTMessageId.PlayerBody, m_bodies.OnMessage);
            m_aimMarkers = new WKTAimMarkers(m_network);
            m_tethers = new WKTTethers(m_network);
            m_network.Messages.Register(WKTMessageId.Tether, m_tethers.OnMessage);
            m_network.Messages.Register(WKTMessageId.AimMarker, m_aimMarkers.OnMessage);
            m_network.Messages.Register(WKTMessageId.PlayerRoom, OnPlayerRoom);
            WKTPlayerRooms.Reset();
            WKTItemEffects.Instance = new WKTItemEffects(m_network);
            m_network.Messages.Register(WKTMessageId.ItemEffect, WKTItemEffects.Instance.OnMessage);
            m_onPlayerConnected = OnPlayerConnected;
            m_network.OnPlayerConnected += m_onPlayerConnected;
            m_onPlayerDisconnected = OnPlayerDisconnected;
            m_network.OnPlayerDisconnected += m_onPlayerDisconnected;
            m_onPlayerMemberDataChanged = OnPlayerMemberDataChanged;
            m_network.OnPlayerMemberDataChanged += m_onPlayerMemberDataChanged;
            m_onHostChanged = OnHostChanged;
            m_network.OnHostChanged += m_onHostChanged;
            // Voices may have changed in the lobby since it was entered.
            m_network.PublishLocalVoices();
        }

        [UsedImplicitly]
        private void OnDestroy() {
            if (Instance == this)
                Instance = null;
            WKTPlayerSoundRecorder.Stop();
            if (m_previewRegistered)
                m_preview.Dispose();
            WKTPlayerPositions.Reset();
            m_platforms.Clear();
            WKTHostStreaming.Reset();
            WKTTargets.Reset();
            WKTPlayerRooms.Reset();
            WKTRemoteFalls.Reset();
            if (m_sync == null) return;
            // The network outlives the scene, and may already be gone when the game quits.
            if (WKT.InstanceOrNull == null) return;
            m_network.Messages.Unregister(WKTMessageId.PlayerState);
            m_network.Messages.Unregister(WKTMessageId.PlayerStateRelay);
            m_network.Messages.Unregister(WKTMessageId.HeldItems);
            m_network.Messages.Unregister(WKTMessageId.PlayerLight);
            m_network.Messages.Unregister(WKTMessageId.PlayerBody);
            m_network.Messages.Unregister(WKTMessageId.ItemEffect);
            m_network.Messages.Unregister(WKTMessageId.AimMarker);
            m_network.Messages.Unregister(WKTMessageId.PlayerRoom);
            m_aimMarkers.Clear();
            m_network.Messages.Unregister(WKTMessageId.Tether);
            m_tethers.Clear();
            WKTItemEffects.Instance = null;
            WKTItemEffects.Reset();
            m_network.OnPlayerConnected -= m_onPlayerConnected;
            m_network.OnPlayerDisconnected -= m_onPlayerDisconnected;
            m_network.OnPlayerMemberDataChanged -= m_onPlayerMemberDataChanged;
            m_network.OnHostChanged -= m_onHostChanged;
        }

        /// <summary>
        /// After the game's own physics step, which is where the local player walks and hands pull them along: their place on
        /// what they ride, read at the moment both were last in step.
        /// </summary>
        [UsedImplicitly]
        private void FixedUpdate() {
            if (m_sync == null || !m_network.IsMultiplayerRun || ENT_Player.playerObject is not { } player) return;
            m_platforms.SampleFixed(player);
        }

        [UsedImplicitly]
        private void LateUpdate() {
            if (m_sync == null) return;
            // The session ended mid-run (host left, kicked): nobody is there any more.
            if (!m_network.IsMultiplayerRun) {
                WKTPlayerSoundRecorder.Stop();
                RemoveAllRemotes();
                WKTPlayerPositions.Reset();
                m_platforms.Clear();
                return;
            }
            // Re-read every frame: a client that takes over as host starts simulating for everyone.
            WKTPlayerPositions.IsCoveringAllPlayers = m_network.Mode == NetworkMode.Host && m_network.Session.IsSyncedWorld;
            WKTPlayerPositions.IsSharedWorld = m_network.Session.IsSyncedWorld;
            var now = Time.realtimeSinceStartupAsDouble;
            var player = ENT_Player.playerObject;
            var gameManager = CL_GameManager.gMan;
            if (player == null || gameManager == null) return;
            var worldOffset = WKTWorldOffset.Get(gameManager);
            var recorder = WKTPlayerSoundRecorder.Active;
            if (recorder == null || recorder.Player != player) {
                WKTPlayerSoundRecorder.Start(player);
                recorder = WKTPlayerSoundRecorder.Active!;
            }
            // Not before the spawn teleport, so others never see a slide from the shared spawn point.
            if (m_runController.HasSpawned) {
                var rotation = player.GetCurrentPlayerRotation();
                var position = player.transform.position;
                position.y -= worldOffset;
                // Camera sway can push the game's pitch slightly past its own clamp.
                var pitch = Mathf.Clamp(rotation.x, -WKTPlayerPose.MAX_PITCH, WKTPlayerPose.MAX_PITCH);
                if (WKTPlayerPose.IsValid(position, rotation.y, pitch)) {
                    var left = GetHandPose(player, c_left_hand, worldOffset);
                    var right = GetHandPose(player, c_right_hand, worldOffset);
                    var health = WKTPlayerPose.QuantizeHealth(player.health, player.maxHealth);
                    var pose = new WKTPlayerPose(position, rotation.y, pitch, GetCrouch(player), left, right, recorder.Sounds, player.dead, health,
                        player.IsGrounded(false), m_platforms.GetLocal(player));
                    m_sync.TrySendLocal(now, pose);
                    m_preview.Update(pose, worldOffset, WKTHeldItems.GetPrefabName(player, c_left_hand), WKTHeldItems.GetPrefabName(player, c_right_hand), m_bodies.LastSent);
                }
                if (!m_previewRegistered) {
                    m_previewRegistered = true;
                    m_preview.Register();
                }
                m_heldItems.Update(player);
                m_lights.Update(player);
                m_bodies.Update(player);
                m_aimMarkers.Update(player);
                m_tethers.Update(player);
                UpdateRoom();
            }
            UpdateRemotes(now, worldOffset, player);
            m_aimMarkers.Follow();
            m_tethers.Draw(this);
        }

        /// <summary>
        /// From the controller's height, so the game's crouch transition and every forced crouch (grapples, buffs) are
        /// seen as they happen.
        /// </summary>
        private static byte GetCrouch(ENT_Player player) {
            var range = player.standingHeight - player.crouchHeight;
            if (range <= 0f) return 0;
            return WKTFraction.Quantize((player.standingHeight - player.cCon.height) / range);
        }

        /// <summary>A hand holds on while it hangs from geometry, grabs a prop or holds a lever. Only hanging carries weight.</summary>
        private static WKTHandPose GetHandPose(ENT_Player player, int index, float worldOffset) {
            var hands = player.hands;
            if (hands == null || index >= hands.Length || hands[index] == null) return WKTHandPose.Rested;
            var hand = hands[index];
            var stamina = WKTHandPose.QuantizeStamina(hand.GetGripStrengthPercent());
            if (!hand.IsHolding()) {
                var holdsItem = hand.inventoryHand?.currentItem != null;
                var grabHeld = IsGrabHeld(hand);
                return new WKTHandPose(false, Vector3.zero, stamina, false, holdsItem || grabHeld, WKTGrabAssist.IsOffering(player, holdsItem, grabHeld));
            }
            var position = hand.GetHoldWorldPosition();
            position.y -= worldOffset;
            return new WKTHandPose(WKTHandPose.IsValid(position), position, stamina, hand.IsHanging());
        }

        /// <summary>
        /// A free hand is held out while it holds an item, or while its grab button is held with nothing to grab. It is open,
        /// for another player to take with Grab Assists, only as <see cref="WKTGrabAssist.IsOffering"/> says.
        /// </summary>
        private static bool IsGrabHeld(ENT_Player.Hand hand) {
            try {
                return !string.IsNullOrEmpty(hand.fireButton) && InputManager.GetButton(hand.fireButton).Pressed;
            }
            catch (Exception) {
                return false;
            }
        }

        /// <summary>Tells everyone which level the local player is in, when it changes and every few seconds.</summary>
        private void UpdateRoom() {
            var room = WKTPlayerRooms.GetLocalRoom();
            if (room == m_sentRoom && Time.unscaledTime < m_nextRoomSend) return;
            m_sentRoom = room;
            m_nextRoomSend = Time.unscaledTime + WKTPlayerRooms.RESEND_SECONDS;
            var local = m_network.LocalPlayer;
            if (local == null) return;
            var writer = m_network.BeginMessage(WKTMessageId.PlayerRoom);
            if (m_network.Mode == NetworkMode.Host) {
                writer.Put(local.Id);
                writer.Put(room);
                m_network.Broadcast(writer, SendType.Reliable);
                return;
            }
            writer.Put(room);
            m_network.SendToHost(writer, SendType.Reliable);
        }

        private void OnPlayerRoom(WKTPlayer sender, NetDataReader reader) {
            if (m_network.Mode == NetworkMode.Host) {
                if (sender.IsHost || !reader.TryGetULong(out var room)) return;
                WKTPlayerRooms.Set(sender.Id, room);
                var relay = m_network.BeginMessage(WKTMessageId.PlayerRoom);
                relay.Put(sender.Id);
                relay.Put(room);
                m_network.Broadcast(relay, SendType.Reliable, sender);
                return;
            }
            if (!sender.IsHost || !reader.TryGetByte(out var playerId) || !reader.TryGetULong(out var playerRoom)) return;
            WKTPlayerRooms.Set(playerId, playerRoom);
        }

        private void UpdateRemotes(double now, float worldOffset, ENT_Player localPlayer) {
            WKTPlayerPositions.ClearRemotes();
            foreach (var player in m_network.GetPlayersEnumerable()) {
                if (!IsRemotePlayer(player.Id) || !m_sync!.TrySample(player.Id, now, out var sampled)) continue;
                // On what they ride, as this machine has it, rather than trailing behind it.
                var pose = m_platforms.PlaceOnPlatform(sampled, worldOffset);
                var remote = m_remotes[player.Id];
                if (remote == null) {
                    remote = WKTRemotePlayer.Create(player, localPlayer, m_network.GetMemberData(player, WKTLobbyConsts.KEY_VOICES));
                    // In instanced co-op everyone plays their own copy of the world: others are only ghosts in it.
                    if (!m_network.Session.IsSyncedWorld)
                        remote.MakeGhost();
                    m_remotes[player.Id] = remote;
                    m_remoteCount++;
                    WKT.Instance.Logger.LogInfo(string.Format("Showing player {0} ({1}).", player.Id, player.friend.Name));
                }
                var room = WKTPlayerRooms.Get(player.Id);
                remote.IsPresent = WKTPlayerRooms.IsPresent(room, WKTPlayerRooms.IsLoadedHere(room));
                // Before the pose, which places hands and head on the body's size.
                if (m_bodies.TryGet(player.Id, out var body))
                    remote.SetBody(body);
                remote.Apply(pose, worldOffset, m_network.Session.GrabAssists);
                if (m_heldItems.TryGet(player.Id, out var held))
                    remote.SetHeldItems(held);
                if (m_lights.TryGet(player.Id, out var light))
                    remote.SetLight(light);
                // The death itself arrives reliably and usually before the pose that shows it.
                var alive = !pose.Dead && !WKTPlayerDeaths.IsDead(player.Id);
                // Only a death seen happening bursts: not a player who was already dead when they first showed.
                if (!alive && remote.IsShown && remote.ShownSince > 0.5)
                    remote.Explode();
                remote.SetVisible(alive && remote.IsPresent);
                // Dead by either: the goo, the gather checks and enemies never count someone between their death and the pose showing it.
                WKTPlayerPositions.AddRemote(player.Id, remote.transform.position, !alive, pose.Left.IsHanging || pose.Right.IsHanging);
                WKTRemoteFalls.Update(player.Id, remote.transform.position.y, !alive || pose.Grounded || pose.Left.IsHanging || pose.Right.IsHanging);
                if (WKTPlayerPositions.IsCoveringAllPlayers)
                    remote.EnsureProxy(localPlayer).SetDead(!alive);
                else
                    remote.DestroyProxy();
            }
        }

        /// <summary>What <paramref name="id"/> holds in their hands, by prefab name. False until they first said.</summary>
        public bool TryGetHeldItems(byte id, out WKTHeldItemsState state) => m_heldItems.TryGet(id, out state);

        /// <summary>The body shown for <paramref name="id"/>. False until their first pose arrived, and for the local player.</summary>
        public bool TryGetRemote(byte id, out WKTRemotePlayer remote) {
            remote = m_remotes[id]!;
            return remote != null;
        }

        private bool IsRemotePlayer(byte id) {
            var localPlayer = m_network.LocalPlayer;
            return localPlayer != null && id != localPlayer.Id && m_network.TryGetPlayer(id, out _);
        }

        private void SendToHost(NetDataWriter writer) => m_network.SendToHost(writer, WKTPlayerStateSync.SEND_TYPE);

        private void Broadcast(NetDataWriter writer, WKTPlayer? except) =>
            m_network.Broadcast(writer, WKTPlayerStateSync.SEND_TYPE, except);

        private void OnPlayerState(WKTPlayer sender, NetDataReader reader) =>
            m_sync?.HandlePlayerState(sender, reader, Time.realtimeSinceStartupAsDouble);

        private void OnPlayerStateRelay(WKTPlayer sender, NetDataReader reader) =>
            m_sync?.HandlePlayerStateRelay(sender, reader, Time.realtimeSinceStartupAsDouble);

        private void OnPlayerConnected(WKTPlayer player) {
            if (m_network.Mode != NetworkMode.Host) return;
            m_heldItems.SendAll(player);
            m_lights.SendAll(player);
            m_bodies.SendAll(player);
        }

        private void OnPlayerDisconnected(WKTPlayer player) {
            m_sync?.Remove(player.Id);
            m_heldItems.Forget(player.Id);
            m_lights.Forget(player.Id);
            m_bodies.Forget(player.Id);
            m_aimMarkers.Forget(player.Id);
            m_tethers.Forget(player.Id);
            if (RemoveRemote(player.Id))
                WKT.Instance.Logger.LogInfo(string.Format("Player {0} left. Removed their body.", player.Id));
        }

        /// <summary>The relay moved to another machine. Buffered states are kept, so nobody jumps while it moves.</summary>
        private void OnHostChanged() {
            if (m_sync != null)
                m_sync.IsHost = m_network.Mode == NetworkMode.Host;
        }

        private void OnPlayerMemberDataChanged(WKTPlayer player) {
            var remote = m_remotes[player.Id];
            if (remote != null)
                remote.SetVoices(m_network.GetMemberData(player, WKTLobbyConsts.KEY_VOICES));
        }

        private void RemoveAllRemotes() {
            for (var id = 0; m_remoteCount > 0 && id < m_remotes.Length; id++) {
                if (RemoveRemote((byte)id))
                    m_sync!.Remove((byte)id);
            }
        }

        private bool RemoveRemote(byte id) {
            var remote = m_remotes[id];
            if (remote == null) return false;
            m_remotes[id] = null;
            m_remoteCount--;
            Destroy(remote.gameObject);
            return true;
        }
    }
}
