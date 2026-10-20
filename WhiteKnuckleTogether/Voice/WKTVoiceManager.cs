using System;
using JetBrains.Annotations;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using SendType = Steamworks.Data.SendType;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Voice chat. Records the local microphone while in a multiplayer session (or testing it in the settings), sends the
    /// frames that pass voice activation or push to talk, and plays every other player's voice: flat in the lobby, from their
    /// head during a run, muffled by walls in between. Clients send to the host, which relays each frame only to the players
    /// allowed to hear it (see <see cref="WKTVoiceRules.CanHear"/>).
    /// <para>Lives on the mod's persistent object, so the microphone test also works outside a session.</para>
    /// <para>
    /// Voices in the world get the listener's room reverb (see <see cref="WKTVoiceRoomReverb"/>).
    /// </para>
    /// <para>
    /// When the audio device changes (opening a Bluetooth headset's microphone does), Unity resets its audio: voices are
    /// rebuilt for the new output rate, and in a session the game's stopped loops start again (<see cref="WKTAudioDeviceWatcher"/>).
    /// </para>
    /// </summary>
    internal sealed class WKTVoiceManager : MonoBehaviour {
        private const SendType c_send_type = SendType.Unreliable | SendType.NoNagle;
        private const float c_occlusion_interval = 1f / 15f;
        private const int c_max_wall_hits = 8;
        /// <summary>Head height above a body's root, where a body has no head to follow.</summary>
        private const float c_fallback_head_height = 1.6f;
        /// <summary>How long into a run the game's audio state is logged, once it has settled.</summary>
        private const float c_run_log_delay = 5f;
        /// <summary>How long after a dead player's last frame to the living their voice still comes from their camera.</summary>
        private const float c_to_living_seconds = 0.4f;
        private readonly WKTVoiceSpeaker?[] m_speakers = new WKTVoiceSpeaker?[byte.MaxValue + 1];
        private readonly WKTVoiceCapture m_capture = new();
        private readonly WKTVoiceActivation m_activation = new();
        private readonly WKTNoiseSuppressor m_suppressor = new();
        private readonly WKTVoiceAutoGain m_autoGain = new();
        private readonly WKTVoiceLimiter m_limiter = new();
        private readonly WKTAudioDeviceWatcher m_deviceWatcher = new();
        private readonly WKTAudioDiagnostics m_diagnostics = new();
        private readonly WKTVoiceRoomReverb m_roomReverb = new();
        private bool m_suppressing;
        private readonly float[] m_frame = new float[WKTVoiceConsts.FRAME_SAMPLES];
        private readonly byte[] m_packet = new byte[WKTVoiceConsts.MAX_PACKET_BYTES];
        private readonly byte[] m_received = new byte[WKTVoiceConsts.MAX_PACKET_BYTES];
        private readonly RaycastHit[] m_hits = new RaycastHit[c_max_wall_hits];
        private readonly Collider?[] m_wallColliders = new Collider?[c_max_wall_hits];
        /// <summary>Per speaker id: until when a dead player's voice is meant for the living, and where it comes from (wire space).</summary>
        private readonly float[] m_toLivingUntil = new float[byte.MaxValue + 1];
        private readonly Vector3[] m_toLivingPosition = new Vector3[byte.MaxValue + 1];
        private WKTNetwork m_network = null!;
        private WKTConfig m_config = null!;
        private WKTVoiceEncoder? m_encoder;
        /// <summary>Plays the local microphone back during the settings' microphone test.</summary>
        private WKTVoiceSpeaker? m_loopback;
        private AudioListener? m_listener;
        private ushort m_sequence;
        private bool m_codecFailed;
        /// <summary>Set by Unity's audio configuration callback, handled in the next update.</summary>
        private bool m_audioChanged;
        private bool m_audioDeviceChanged;
        private float m_runSeenTime = -1f;
        private bool m_loggedRunAudio;
        /// <summary>The Bluetooth microphone warning was shown this session.</summary>
        private bool m_warnedHandsFree;

        public static WKTVoiceManager? Instance { get; private set; }
        /// <summary>Plays the microphone back to its owner, for the settings' test toggle. Records even outside a session.</summary>
        public bool IsTestingMicrophone { get; set; }
        /// <summary>The loudness of the last recorded frame, in dBFS after the microphone volume, for the settings' level meter.</summary>
        public float LocalLevelDb { get; private set; } = WKTVoiceActivation.SILENCE_DB;
        /// <summary>The local microphone is being sent.</summary>
        public bool IsLocalSpeaking => m_activation.IsOpen && m_capture.IsRecording;
        /// <summary>The local player is dead and holds focus with Dead Can Talk on: the living hear them, from their camera.</summary>
        public bool IsTalkingToLiving => m_network.Session.DeadCanTalk && WKTDeadPlayerInput.IsActive && WKTDeadPlayerInput.FocusHeld && !WKTPlayerDeaths.IsRunEnded;

        [UsedImplicitly]
        private void Awake() {
            Instance = this;
            m_network = WKT.Instance.Network;
            m_config = WKTPlugin.ModConfig!;
            m_network.Messages.Register(WKTMessageId.VoiceFrame, OnVoiceFrame);
            m_network.OnPlayerDisconnected += OnPlayerDisconnected;
            m_network.OnNetworkStopping += OnNetworkStopping;
            m_config.VoiceEnabled.SettingChanged += OnVoiceEnabledChanged;
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }

        [UsedImplicitly]
        private void OnDestroy() {
            if (Instance == this)
                Instance = null;
            m_config.VoiceEnabled.SettingChanged -= OnVoiceEnabledChanged;
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            ClearSpeakers();
            m_capture.Dispose();
            m_encoder?.Dispose();
            m_loopback?.Dispose();
            var wkt = WKT.InstanceOrNull;
            if (wkt == null) return;
            wkt.Network.Messages.Unregister(WKTMessageId.VoiceFrame);
            wkt.Network.OnPlayerDisconnected -= OnPlayerDisconnected;
            wkt.Network.OnNetworkStopping -= OnNetworkStopping;
        }

        [UsedImplicitly]
        private void Update() {
            if (m_audioChanged)
                HandleAudioChange();
            LogRunAudio();
            m_diagnostics.Update(m_network.IsMultiplayerRun, m_speakers);
            UpdateCapture();
            m_capture.Pump();
            while (m_capture.TryReadFrame(m_frame))
                ProcessLocalFrame();
            UpdateSpeakers();
        }

        /// <summary>Whether <paramref name="playerId"/>'s voice was heard in the last moment. The local player too.</summary>
        public bool IsSpeaking(byte playerId) {
            var local = m_network.LocalPlayer;
            if (local != null && local.Id == playerId) return IsLocalSpeaking;
            var speaker = m_speakers[playerId];
            return speaker != null && speaker.IsSpeaking;
        }

        #region Sending

        private bool IsInSession => m_network.Mode != NetworkMode.None && m_network.LocalPlayer != null;

        private void UpdateCapture() {
            var wanted = !m_codecFailed && (IsTestingMicrophone ||
                m_config.VoiceEnabled.Value && m_config.VoiceInputMode.Value != VoiceInputMode.Disabled && IsInSession);
            if (wanted && (!m_capture.IsRecording || m_capture.RequestedDevice != m_config.VoiceInputDevice.Value)) {
                // Opening a microphone may switch the audio device (see WKTAudioDeviceWatcher).
                if (IsInSession)
                    m_deviceWatcher.Snapshot();
                if (!EnsureCodec() || !m_capture.Start(m_config.VoiceInputDevice.Value)) {
                    // No microphone: listening still works. Tries again when the device setting changes.
                    m_capture.Stop();
                    return;
                }
                m_activation.Reset();
                m_suppressor.Reset();
                m_autoGain.Reset();
                m_limiter.Reset();
                var device = WKTVoiceCapture.ResolveDevice(m_config.VoiceInputDevice.Value);
                WKT.Instance.Logger.LogInfo("Recording the microphone for voice chat.");
                WKTAudioDeviceWatcher.LogState("microphone opened", device);
                WarnIfHandsFree(device);
            }
            else if (!wanted && m_capture.IsRecording) {
                if (IsInSession)
                    m_deviceWatcher.Snapshot();
                m_capture.Stop();
                m_activation.Reset();
                LocalLevelDb = WKTVoiceActivation.SILENCE_DB;
                WKT.Instance.Logger.LogInfo("Stopped recording the microphone.");
            }
            if (!IsTestingMicrophone && m_loopback != null) {
                m_loopback.Dispose();
                m_loopback = null;
            }
        }

        /// <summary>
        /// A Bluetooth headset's microphone switches the headset to its hands-free profile while it is open: Windows lowers
        /// all of the game's sound to phone quality, or loses it. Told once a session, in the chat.
        /// </summary>
        private void WarnIfHandsFree(string device) {
            if (m_warnedHandsFree || !IsInSession || !WKTVoiceRules.IsHandsFreeDevice(device)) return;
            m_warnedHandsFree = true;
            WKT.Instance.Logger.LogWarning($"The microphone '{device}' is a Bluetooth headset in hands-free mode.");
            WKTChat.PostSystem(WKTVoiceRules.HANDS_FREE_WARNING);
        }

        /// <summary>
        /// Before the level is measured, so steady noise never opens voice activation. Turned back on, it starts learning the
        /// noise afresh.
        /// </summary>
        private void SuppressNoise() {
            var wanted = m_config.NoiseSuppression.Value;
            if (wanted && !m_suppressing)
                m_suppressor.Reset();
            m_suppressing = wanted;
            if (wanted)
                m_suppressor.Process(m_frame);
        }

        private bool EnsureCodec() {
            if (m_encoder != null) return true;
            try {
                m_encoder = new WKTVoiceEncoder();
                return true;
            }
            catch (Exception e) {
                m_codecFailed = true;
                WKT.Instance.Logger.LogError($"Voice chat is unavailable: {e.Message}");
                return false;
            }
        }

        private void ProcessLocalFrame() {
            SuppressNoise();
            // Mic Volume sets both how loud the microphone counts for voice activation and how loud it is sent.
            var gain = m_config.MicVolume.Value;
            LocalLevelDb = WKTVoiceActivation.GetLevelDb(m_frame, m_frame.Length, gain);
            var pushToTalk = m_config.PushToTalk.IsHeld && !WKTChat.IsTyping;
            if (!m_activation.Update(m_config.VoiceInputMode.Value, LocalLevelDb, m_config.ActivationThresholdDb.Value, pushToTalk)) return;
            // Measured after voice activation decides, on sent frames only, so silence is never turned up.
            if (m_config.AutoGain.Value) {
                m_autoGain.Update(m_frame);
                gain *= m_autoGain.Gain;
            }
            else
                gain *= WKTVoiceConsts.MANUAL_MIC_GAIN;
            m_limiter.Process(m_frame, gain);
            var length = m_encoder!.Encode(m_frame, m_packet);
            if (length == 0) return;
            var sequence = m_sequence++;
            if (IsTestingMicrophone) {
                m_loopback ??= new WKTVoiceSpeaker("WKT Voice Test");
                m_loopback.Receive(sequence, m_packet, length);
            }
            if (!m_config.VoiceEnabled.Value || !IsInSession) return;
            var toLiving = TryGetLivingVoicePosition(out var wirePosition);
            if (m_network.Mode == NetworkMode.Host) {
                Relay(m_network.LocalPlayer!, sequence, m_packet, length, toLiving, wirePosition);
                return;
            }
            var writer = m_network.BeginMessage(WKTMessageId.VoiceFrame);
            WKTVoiceFrame.Write(writer, sequence, m_packet, length);
            if (toLiving)
                WKTVoiceFrame.WriteToLiving(writer, wirePosition);
            m_network.SendToHost(writer, c_send_type);
        }

        /// <summary>Where the local player's voice to the living comes from: their spectator camera. False when they are not talking to the living.</summary>
        private bool TryGetLivingVoicePosition(out Vector3 wirePosition) {
            wirePosition = default;
            var camera = Camera.main;
            if (!IsTalkingToLiving || camera == null) return false;
            wirePosition = WKTWorldOffset.ToWire(camera.transform.position, WKTWorldOffset.Get());
            return true;
        }

        #endregion

        #region Receiving

        private void OnVoiceFrame(WKTPlayer sender, NetDataReader reader) {
            if (m_network.Mode == NetworkMode.Host) {
                if (sender.IsHost || !WKTVoiceFrame.TryRead(reader, out var sequence, m_received, out var length)) return;
                var toLiving = WKTVoiceFrame.TryReadToLiving(reader, out var wirePosition) && m_network.Session.DeadCanTalk;
                Relay(sender, sequence, m_received, length, toLiving, wirePosition);
                return;
            }
            if (!sender.IsHost || !reader.TryGetByte(out var speakerId) ||
                !WKTVoiceFrame.TryRead(reader, out var relayedSequence, m_received, out var relayedLength)) return;
            var relayedToLiving = WKTVoiceFrame.TryReadToLiving(reader, out var relayedPosition);
            Play(speakerId, relayedSequence, m_received, relayedLength, relayedToLiving, relayedPosition);
        }

        /// <summary>
        /// Host only: sends <paramref name="speaker"/>'s frame to everyone who may hear it, and plays it here if the host may.
        /// A dead speaker's frame to the living (<paramref name="toLiving"/>) reaches them too, from <paramref name="wirePosition"/>.
        /// </summary>
        private void Relay(WKTPlayer speaker, ushort sequence, byte[] packet, int length, bool toLiving, Vector3 wirePosition) {
            var speakerDead = IsDead(speaker.Id);
            var speakerOff = m_network.HasVoiceChatOff(speaker);
            var runOver = WKTPlayerDeaths.IsRunEnded;
            NetDataWriter? writer = null;
            foreach (var listener in m_network.GetPlayersEnumerable()) {
                if (listener == speaker || !WKTVoiceRules.CanHear(speakerDead, speakerOff, IsDead(listener.Id), m_network.HasVoiceChatOff(listener), runOver,
                        toLiving && speakerDead))
                    continue;
                if (listener.IsHost) {
                    Play(speaker.Id, sequence, packet, length, toLiving, wirePosition);
                    continue;
                }
                if (writer == null) {
                    writer = m_network.BeginMessage(WKTMessageId.VoiceFrame);
                    writer.Put(speaker.Id);
                    WKTVoiceFrame.Write(writer, sequence, packet, length);
                    if (toLiving)
                        WKTVoiceFrame.WriteToLiving(writer, wirePosition);
                }
                m_network.SendToPlayer(listener, writer, c_send_type);
            }
        }

        private void Play(byte speakerId, ushort sequence, byte[] packet, int length, bool toLiving, Vector3 wirePosition) {
            if (!m_config.VoiceEnabled.Value || m_codecFailed) return;
            if (toLiving) {
                m_toLivingUntil[speakerId] = Time.unscaledTime + c_to_living_seconds;
                m_toLivingPosition[speakerId] = wirePosition;
            }
            var local = m_network.LocalPlayer;
            if (local != null && local.Id == speakerId) return;
            var speaker = m_speakers[speakerId];
            if (speaker == null) {
                try {
                    speaker = new WKTVoiceSpeaker("WKT Voice " + speakerId);
                }
                catch (Exception e) {
                    m_codecFailed = true;
                    WKT.Instance.Logger.LogError($"Voice chat is unavailable: {e.Message}");
                    return;
                }
                m_speakers[speakerId] = speaker;
            }
            speaker.Receive(sequence, packet, length);
        }

        private static bool IsDead(byte playerId) {
            var sync = WKTPlayerDeaths.Sync;
            return sync != null && sync.Roster.IsDead(playerId);
        }

        #endregion

        #region Playing

        private void UpdateSpeakers() {
            var deltaTime = Time.unscaledDeltaTime;
            var chatVolume = m_config.VoiceChatVolume.Value;
            m_loopback?.Update(chatVolume, false, deltaTime);
            if (!IsInSession) return;
            var inRun = m_network.IsMultiplayerRun;
            var runOver = WKTPlayerDeaths.IsRunEnded;
            var proximity = m_network.Session.ProximityVoice;
            var players = WKTPlayerSync.Instance;
            var listener = inRun ? GetListener() : null;
            m_roomReverb.Update(listener != null ? listener.transform.position : null, deltaTime);
            var now = Time.unscaledTime;
            var worldOffset = inRun ? WKTWorldOffset.Get() : 0f;
            var showGhosts = m_config.ShowDeadGhosts.Value;
            for (var id = 0; id < m_speakers.Length; id++) {
                var speaker = m_speakers[id];
                if (speaker == null) continue;
                var playerId = (byte)id;
                if (!m_network.TryGetPlayer(playerId, out var player) || player == null) continue;
                WKTRemotePlayer? remote = null;
                var hasBody = players != null && players.TryGetRemote(playerId, out remote);
                var dead = IsDead(playerId);
                // A dead player talking to the living speaks from their spectator camera.
                var toLiving = dead && inRun && !runOver && m_toLivingUntil[id] > now;
                var ghost = toLiving ? WKTWorldOffset.FromWire(m_toLivingPosition[id], worldOffset) : Vector3.zero;
                var spatial = toLiving ? proximity : WKTVoiceRules.IsSpatial(inRun, runOver, proximity, dead, hasBody);
                if (spatial) {
                    if (toLiving)
                        speaker.Source.transform.position = ghost;
                    else
                        PlaceAtHead(speaker, remote!);
                    UpdateWalls(speaker, listener);
                }
                var steamId = player.friend.Id.Value;
                // A voice from a level not loaded here is as far away as the player is: out of earshot.
                var away = spatial && !toLiving && !remote!.IsPresent;
                var volume = away || m_config.GetPlayerMuted(steamId).Value ? 0f : chatVolume * m_config.GetPlayerVolume(steamId).Value;
                speaker.Update(volume, spatial, deltaTime);
                if (remote != null)
                    remote.SetGhostHead(ghost, toLiving && showGhosts ? WKTVoiceRules.GhostAlpha(speaker.LevelDb) : 0f);
                // Only voices heard from a head in the world are in the room; flat ones are in the listener's ears.
                speaker.Source.SetReverb(spatial ? m_roomReverb.Current : WKTReverbSettings.Off);
            }
        }

        /// <summary>Voices come from where the speaker's head is.</summary>
        private static void PlaceAtHead(WKTVoiceSpeaker speaker, WKTRemotePlayer remote) {
            var head = remote.Head;
            speaker.Source.transform.position = head != null ? head.position : remote.transform.position + Vector3.up * c_fallback_head_height;
        }

        private AudioListener? GetListener() {
            if (m_listener == null || !m_listener.isActiveAndEnabled)
                m_listener = FindAnyObjectByType<AudioListener>();
            return m_listener;
        }

        /// <summary>Counts, a few times a second, the separate pieces of world between the listener and the speaker's head.</summary>
        private void UpdateWalls(WKTVoiceSpeaker speaker, AudioListener? listener) {
            var now = Time.unscaledTime;
            if (now < speaker.NextOcclusionCheck) return;
            speaker.NextOcclusionCheck = now + c_occlusion_interval;
            var player = ENT_Player.playerObject;
            if (listener == null || player == null) {
                speaker.Walls = 0;
                return;
            }
            var from = listener.transform.position;
            var to = speaker.Source.transform.position;
            var direction = to - from;
            var distance = direction.magnitude;
            if (distance < 0.01f) {
                speaker.Walls = 0;
                return;
            }
            int mask = player.jumpMask;
            var count = Physics.RaycastNonAlloc(from, direction / distance, m_hits, distance, mask == 0 ? Physics.DefaultRaycastLayers : mask,
                QueryTriggerInteraction.Ignore);
            var walls = 0;
            for (var i = 0; i < count; i++) {
                var collider = m_hits[i].collider;
                if (collider == null || collider.transform.IsChildOf(player.transform) || Array.IndexOf(m_wallColliders, collider, 0, walls) >= 0)
                    continue;
                m_wallColliders[walls++] = collider;
            }
            Array.Clear(m_wallColliders, 0, walls);
            speaker.Walls = walls;
        }

        #endregion

        #region Audio device

        /// <summary>Unity's callback, after it reset its audio. Handled in the next update.</summary>
        private void OnAudioConfigurationChanged(bool deviceWasChanged) {
            m_audioChanged = true;
            m_audioDeviceChanged |= deviceWasChanged;
        }

        /// <summary>
        /// Every source stopped and the output rate may differ: voices start over with the new rate (each comes back with
        /// its player's next packet), and in a session the game's loops play again. A microphone the reset closed is opened
        /// again by <see cref="WKTVoiceCapture.Pump"/>.
        /// </summary>
        private void HandleAudioChange() {
            var deviceChanged = m_audioDeviceChanged;
            m_audioChanged = m_audioDeviceChanged = false;
            ClearSpeakers();
            m_loopback?.Dispose();
            m_loopback = null;
            var device = m_capture.IsRecording ? WKTVoiceCapture.ResolveDevice(m_capture.RequestedDevice) : "none";
            WKTAudioDeviceWatcher.LogState(deviceChanged ? "output device changed" : "audio settings changed", device);
            if (!IsInSession) return;
            var restarted = m_deviceWatcher.RestoreLoops();
            WKT.Instance.Logger.LogInfo($"Restarted {restarted} looping sounds the audio reset stopped.");
        }

        /// <summary>Logs the game's audio once a run has settled, so a report of missing sound comes with its state.</summary>
        private void LogRunAudio() {
            if (!m_network.IsMultiplayerRun) {
                m_runSeenTime = -1f;
                m_loggedRunAudio = false;
                return;
            }
            if (m_loggedRunAudio) return;
            var now = Time.unscaledTime;
            if (m_runSeenTime < 0f)
                m_runSeenTime = now;
            if (now - m_runSeenTime < c_run_log_delay) return;
            m_loggedRunAudio = true;
            WKTAudioDeviceWatcher.LogState("run started", m_capture.IsRecording ? WKTVoiceCapture.ResolveDevice(m_capture.RequestedDevice) : "none");
        }

        #endregion

        private void OnPlayerDisconnected(WKTPlayer player) => RemoveSpeaker(player.Id);

        private void OnNetworkStopping() {
            ClearSpeakers();
            m_warnedHandsFree = false;
        }

        private void OnVoiceEnabledChanged(object sender, EventArgs e) {
            m_network.PublishLocalVoices();
            if (!m_config.VoiceEnabled.Value)
                ClearSpeakers();
        }

        private void RemoveSpeaker(byte playerId) {
            m_speakers[playerId]?.Dispose();
            m_speakers[playerId] = null;
        }

        private void ClearSpeakers() {
            for (var i = 0; i < m_speakers.Length; i++)
                RemoveSpeaker((byte)i);
        }
    }
}
