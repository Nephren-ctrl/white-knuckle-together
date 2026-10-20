namespace WhiteKnuckleTogether {
    /// <summary>When the local microphone is sent to the other players. In the order the settings list them.</summary>
    public enum VoiceInputMode : byte {
        /// <summary>Never: the microphone stays closed, but other players are still heard.</summary>
        Disabled,
        /// <summary>Only while the push to talk key is held.</summary>
        PushToTalk,
        /// <summary>Whenever the microphone hears more than the activation threshold.</summary>
        VoiceActivation
    }
}
