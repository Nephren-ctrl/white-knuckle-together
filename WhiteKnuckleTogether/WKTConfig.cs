using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace WhiteKnuckleTogether {
    internal sealed class WKTConfig {
        public const string MAIN_MENU_HIERARCHY_FILE = "WKT_MainMenuHierarchy.txt";
        public const float MAX_VOLUME = 2f;
        public const float MIN_ACTIVATION_THRESHOLD_DB = -60f;
        public const float MAX_ACTIVATION_THRESHOLD_DB = 0f;
        private const string c_debug_section = "Debug";
        private const string c_keybinds_section = "Keybinds";
        private const string c_voice_section = "Voice";
        private const string c_voice_players_section = "VoicePlayers";
        private const string c_chat_section = "Chat";
        private const string c_gameplay_section = "Gameplay";
        private readonly ConfigFile m_file;
        private readonly Dictionary<ulong, ConfigEntry<float>> m_playerVolumes = new();
        private readonly Dictionary<ulong, ConfigEntry<bool>> m_playerMutes = new();

        public ConfigEntry<bool> DumpMainMenuHierarchy { get; }
        public WKTKeyBinding PushToTalk { get; }
        public WKTKeyBinding OpenChat { get; }
        public WKTKeyBinding ChatHistoryUp { get; }
        public WKTKeyBinding ChatHistoryDown { get; }
        /// <summary>Every rebindable key, in the order the Controls tab lists them.</summary>
        public IReadOnlyList<WKTKeyBinding> KeyBindings { get; }
        /// <summary>Off, the microphone stays closed and nobody is heard.</summary>
        public ConfigEntry<bool> VoiceEnabled { get; }
        public ConfigEntry<VoiceInputMode> VoiceInputMode { get; }
        /// <summary>The microphone's name in <c>Microphone.devices</c>. Empty for the system default.</summary>
        public ConfigEntry<string> VoiceInputDevice { get; }
        /// <summary>Gain on the local microphone, 0 to <see cref="MAX_VOLUME"/>.</summary>
        public ConfigEntry<float> MicVolume { get; }
        /// <summary>The loudness, in dBFS, the microphone must reach to send with voice activation.</summary>
        public ConfigEntry<float> ActivationThresholdDb { get; }
        /// <summary>Takes steady background noise (fans, hum) out of the microphone (see <see cref="WKTNoiseSuppressor"/>).</summary>
        public ConfigEntry<bool> NoiseSuppression { get; }
        /// <summary>Brings the microphone to a steady speaking level (see <see cref="WKTVoiceAutoGain"/>).</summary>
        public ConfigEntry<bool> AutoGain { get; }
        /// <summary>Gain on every other player's voice, 0 to <see cref="MAX_VOLUME"/>.</summary>
        public ConfigEntry<float> VoiceChatVolume { get; }
        /// <summary>Hides bad words in chat messages, as this player sees them.</summary>
        public ConfigEntry<bool> CensorChat { get; }
        /// <summary>Whether this player holds a hand out for others to take (Grab Assists) only in the game's focus mode.</summary>
        public ConfigEntry<bool> GrabAssistsNeedFocus { get; }
        /// <summary>Whether this player sees the faint ghost heads of dead players talking to the living.</summary>
        public ConfigEntry<bool> ShowDeadGhosts { get; }

        public WKTConfig(ConfigFile file) {
            m_file = file;
            DumpMainMenuHierarchy = file.Bind(c_debug_section, "DumpMainMenuHierarchy", false,
                $"Writes the main menu hierarchy to BepInEx/{MAIN_MENU_HIERARCHY_FILE} when the main menu loads. " +
                "Diff the file across game updates to see which UI the mod depends on has moved.");
            PushToTalk = new WKTKeyBinding(file, c_keybinds_section, "PushToTalk", "Push to Talk", KeyCode.T,
                "Held to talk when the voice input mode is push to talk.");
            OpenChat = new WKTKeyBinding(file, c_keybinds_section, "OpenChat", "Open Chat", KeyCode.Return, "Opens the text chat.");
            ChatHistoryUp = new WKTKeyBinding(file, c_keybinds_section, "ChatHistoryUp", "History Up", KeyCode.UpArrow,
                "While typing, fills in the previous message you sent.");
            ChatHistoryDown = new WKTKeyBinding(file, c_keybinds_section, "ChatHistoryDown", "History Down", KeyCode.DownArrow,
                "While typing, fills in the next message you sent.");
            KeyBindings = new[] { PushToTalk, OpenChat, ChatHistoryUp, ChatHistoryDown };
            VoiceEnabled = file.Bind(c_voice_section, "Enabled", true, "Voice chat. Off (Completely Disable Voice Chat in the settings), the microphone stays closed and nobody is heard. To only never speak, set InputMode to Disabled.");
            VoiceInputMode = file.Bind(c_voice_section, "InputMode", WhiteKnuckleTogether.VoiceInputMode.PushToTalk,
                "When the microphone is sent: never, while push to talk is held, or on voice activation.");
            VoiceInputDevice = file.Bind(c_voice_section, "InputDevice", string.Empty, "The microphone to use. Empty for the system default.");
            MicVolume = file.Bind(c_voice_section, "MicVolume", 1f, new ConfigDescription("Gain on your microphone.",
                new AcceptableValueRange<float>(0f, MAX_VOLUME)));
            ActivationThresholdDb = file.Bind(c_voice_section, "ActivationThresholdDb", -40f, new ConfigDescription(
                "How loud, in dBFS, the microphone must be to send with voice activation.",
                new AcceptableValueRange<float>(MIN_ACTIVATION_THRESHOLD_DB, MAX_ACTIVATION_THRESHOLD_DB)));
            NoiseSuppression = file.Bind(c_voice_section, "NoiseSuppression", true,
                "Takes steady background noise such as fans and hum out of your microphone. Sounds you make are kept.");
            AutoGain = file.Bind(c_voice_section, "AutoGain", true,
                "Turns your voice up or down to a steady level as loud as the game, whatever your microphone. Mic Volume is applied on top.");
            VoiceChatVolume = file.Bind(c_voice_section, "VoiceChatVolume", 1f, new ConfigDescription("Gain on every other player's voice.",
                new AcceptableValueRange<float>(0f, MAX_VOLUME)));
            CensorChat = file.Bind(c_chat_section, "CensorChat", true, "Hides bad words in chat messages.");
            GrabAssistsNeedFocus = file.Bind(c_gameplay_section, "GrabAssistsNeedFocus", true,
                "With Grab Assists on in a lobby, holds a hand out for other players to take only while in focus mode, so a climbing grab never offers a hand by accident.");
            ShowDeadGhosts = file.Bind(c_gameplay_section, "ShowDeadGhosts", true,
                "With Dead Can Talk on in a lobby, shows a faint ghost head where a dead player talking to the living watches from. Off, they are only heard.");
        }

        /// <summary>The volume this player set for <paramref name="steamId"/>'s voice, 0 to <see cref="MAX_VOLUME"/>.</summary>
        public ConfigEntry<float> GetPlayerVolume(ulong steamId) {
            if (m_playerVolumes.TryGetValue(steamId, out var entry)) return entry;
            entry = m_file.Bind(c_voice_players_section, steamId + ".Volume", 1f, new ConfigDescription(
                "Volume of this Steam user's voice.", new AcceptableValueRange<float>(0f, MAX_VOLUME)));
            m_playerVolumes.Add(steamId, entry);
            return entry;
        }

        /// <summary>Whether this player muted <paramref name="steamId"/>'s voice.</summary>
        public ConfigEntry<bool> GetPlayerMuted(ulong steamId) {
            if (m_playerMutes.TryGetValue(steamId, out var entry)) return entry;
            entry = m_file.Bind(c_voice_players_section, steamId + ".Muted", false, "Whether this Steam user's voice is muted.");
            m_playerMutes.Add(steamId, entry);
            return entry;
        }
    }
}
