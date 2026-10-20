using BepInEx.Configuration;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A mod key the player can rebind in the game's Controls tab. The game's own bindings are fixed Rewired actions, so mod
    /// keys are read with Unity's legacy input instead, the same way the game's console reads its keys.
    /// </summary>
    internal sealed class WKTKeyBinding {
        private readonly ConfigEntry<KeyCode> m_entry;

        /// <summary>A key is being captured for a rebind. No binding reads as pressed meanwhile.</summary>
        public static bool IsCapturing { get; set; }
        public string DisplayName { get; }
        public KeyCode DefaultKey { get; }
        public KeyCode Key {
            get => m_entry.Value;
            set => m_entry.Value = value;
        }
        public bool IsDown => IsReadable && Input.GetKeyDown(Key);
        public bool IsHeld => IsReadable && Input.GetKey(Key);
        private bool IsReadable => !IsCapturing && Key != KeyCode.None;

        public WKTKeyBinding(ConfigFile file, string section, string key, string displayName, KeyCode defaultKey, string description) {
            DisplayName = displayName;
            DefaultKey = defaultKey;
            m_entry = file.Bind(section, key, defaultKey, description);
        }
    }
}
