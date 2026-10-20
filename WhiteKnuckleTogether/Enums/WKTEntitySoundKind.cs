namespace WhiteKnuckleTogether {
    /// <summary>Which of an entity's <c>UT_AudioClipHandler</c> calls a <see cref="WKTEntitySound"/> repeats.</summary>
    public enum WKTEntitySoundKind : byte {
        /// <summary><c>PlaySound(string)</c>.</summary>
        Play,
        /// <summary><c>PlaySound(string, float)</c>, with a volume.</summary>
        PlayVolume,
        /// <summary><c>PlaySound(string, Vector3)</c>, at a world position.</summary>
        PlayAt,
        /// <summary><c>StopSound(string)</c>.</summary>
        Stop
    }
}
