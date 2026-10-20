namespace WhiteKnuckleTogether {
    /// <summary>Who hears whom, and how: the decisions of voice chat, kept apart from Unity so they can be tested.</summary>
    internal static class WKTVoiceRules {
        /// <summary>Cutoff of the muffling filter with nothing in the way: no filtering at all.</summary>
        public const float CLEAR_CUTOFF = 22000f;
        public const float ONE_WALL_CUTOFF = 3000f;
        public const float WALLS_CUTOFF = 1400f;
        public const float ONE_WALL_GAIN = 0.85f;
        public const float WALLS_GAIN = 0.7f;
        /// <summary>How see-through a dead player's ghost head is at its most: barely there, so it never gets in the way.</summary>
        public const float GHOST_MAX_ALPHA = 0.12f;
        /// <summary>A voice this quiet shows no ghost, and one this loud shows it fully.</summary>
        public const float GHOST_QUIET_DB = -50f;
        public const float GHOST_LOUD_DB = -20f;
        /// <summary>Played voice samples up to this pass untouched; louder ones are rounded off toward <see cref="SOFT_CLIP_LIMIT"/>.</summary>
        public const float SOFT_CLIP_KNEE = 0.6f;
        /// <summary>No played voice sample gets louder than this, however loud it is turned up.</summary>
        public const float SOFT_CLIP_LIMIT = 1f;
        /// <summary>Told to a player whose microphone is a Bluetooth headset's (see <see cref="IsHandsFreeDevice"/>).</summary>
        public const string HANDS_FREE_WARNING =
            "Your microphone is a Bluetooth headset's: while it is open, Windows drops your game audio to phone quality or cuts it. " +
            "Pick another microphone, or set Input Mode to Disabled to still hear everyone.";
        /// <summary>
        /// What Windows calls a Bluetooth headset's microphone. Opening it switches the headset to its hands-free profile: mono,
        /// low quality output, and the game's audio device changes under it.
        /// </summary>
        private static readonly string[] s_handsFreeMarks = { "hands-free", "handsfree", "hands free", "ag audio", "hf audio" };

        /// <summary>
        /// Whether a listener's machine should get a speaker's voice. Nobody with voice chat off hears or is heard. While the
        /// run goes on the dead are heard only by the dead, and the dead hear everyone, unless a dead speaker talks to the living
        /// (<paramref name="toLiving"/>); once it is over (<paramref name="runOver"/>, such as a race's end) everyone hears
        /// everyone again.
        /// </summary>
        public static bool CanHear(bool speakerDead, bool speakerVoiceOff, bool listenerDead, bool listenerVoiceOff, bool runOver, bool toLiving = false) =>
            !speakerVoiceOff && !listenerVoiceOff && (runOver || !speakerDead || listenerDead || toLiving);

        /// <summary>How visible a talking dead player's ghost head is for a voice at <paramref name="levelDb"/>: 0 to <see cref="GHOST_MAX_ALPHA"/>.</summary>
        public static float GhostAlpha(float levelDb) {
            var t = (levelDb - GHOST_QUIET_DB) / (GHOST_LOUD_DB - GHOST_QUIET_DB);
            return GHOST_MAX_ALPHA * (t <= 0f ? 0f : t >= 1f ? 1f : t);
        }

        /// <summary>
        /// Whether a voice comes from the speaker's head in the world. In the lobby everyone is heard flat, and so are the
        /// dead (only other dead hear them), anyone whose body is not shown yet, and everyone when the lobby turned
        /// proximity voice off, and everyone once the run is over.
        /// </summary>
        public static bool IsSpatial(bool inRun, bool runOver, bool proximity, bool speakerDead, bool hasBody) =>
            inRun && !runOver && proximity && !speakerDead && hasBody;

        /// <summary>The muffling filter's cutoff, in Hz, for this many walls between the listener and the speaker.</summary>
        public static float GetCutoff(int walls) => walls switch {
            <= 0 => CLEAR_CUTOFF,
            1 => ONE_WALL_CUTOFF,
            _ => WALLS_CUTOFF
        };

        /// <summary>How much of a voice gets through this many walls.</summary>
        public static float GetWallGain(int walls) => walls switch {
            <= 0 => 1f,
            1 => ONE_WALL_GAIN,
            _ => WALLS_GAIN
        };

        /// <summary>
        /// Rounds off a played voice sample that would go past <see cref="SOFT_CLIP_KNEE"/>, smoothly and never past
        /// <see cref="SOFT_CLIP_LIMIT"/>, so a voice turned up loud gets dense rather than clipping harshly at the speakers. Odd,
        /// monotonic, and with no corner at the knee. Anything not a number becomes silence, so one bad sample can never
        /// poison the whole mix.
        /// </summary>
        public static float SoftClip(float sample) {
            if (float.IsNaN(sample) || float.IsInfinity(sample)) return 0f;
            var magnitude = sample < 0f ? -sample : sample;
            if (magnitude <= SOFT_CLIP_KNEE) return sample;
            const float room = SOFT_CLIP_LIMIT - SOFT_CLIP_KNEE;
            var over = (magnitude - SOFT_CLIP_KNEE) / room;
            var clipped = SOFT_CLIP_KNEE + room * over / (1f + over);
            return sample < 0f ? -clipped : clipped;
        }

        /// <summary>Whether a microphone's name is a Bluetooth headset's hands-free microphone (see <see cref="s_handsFreeMarks"/>).</summary>
        public static bool IsHandsFreeDevice(string? deviceName) {
            if (string.IsNullOrEmpty(deviceName)) return false;
            foreach (var mark in s_handsFreeMarks) {
                if (deviceName!.IndexOf(mark, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Splits a total volume (up to <see cref="WKTConfig.MAX_VOLUME"/>) into what the audio source plays at, at most 1,
        /// and the boost applied to the samples themselves for anything above that (see <see cref="WKTVoiceSource"/>).
        /// </summary>
        public static void SplitVolume(float total, out float sourceVolume, out float sampleGain) {
            if (total <= 1f) {
                sourceVolume = total < 0f ? 0f : total;
                sampleGain = 1f;
                return;
            }
            sourceVolume = 1f;
            sampleGain = total;
        }
    }
}
