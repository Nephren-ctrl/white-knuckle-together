using System;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Decides, frame by frame, whether the microphone is sent. Voice activation opens when a frame is louder than the
    /// threshold and stays open for a short hangover, so pauses between words are not cut. Push to talk sends while the key is
    /// held and a short tail after, so the end of the last word is not cut.
    /// </summary>
    internal sealed class WKTVoiceActivation {
        public const float HANGOVER_SECONDS = 0.3f;
        public const float PUSH_TO_TALK_TAIL_SECONDS = 0.15f;
        /// <summary>Quieter than any real microphone signal: the floor reported for silence.</summary>
        public const float SILENCE_DB = -100f;
        private static readonly int s_hangoverFrames = (int)Math.Ceiling(HANGOVER_SECONDS / WKTVoiceConsts.FRAME_SECONDS);
        private static readonly int s_tailFrames = (int)Math.Ceiling(PUSH_TO_TALK_TAIL_SECONDS / WKTVoiceConsts.FRAME_SECONDS);
        private int m_openFrames;

        public bool IsOpen { get; private set; }

        /// <summary>
        /// Call once per frame. <paramref name="levelDb"/> is the frame's loudness (see <see cref="GetLevelDb"/>).
        /// <paramref name="keyHeld"/> is the push to talk key. Returns whether this frame is sent.
        /// </summary>
        public bool Update(VoiceInputMode mode, float levelDb, float thresholdDb, bool keyHeld) {
            if (mode == VoiceInputMode.Disabled) {
                Reset();
                return false;
            }
            var trigger = mode == VoiceInputMode.PushToTalk ? keyHeld : levelDb >= thresholdDb;
            if (trigger) {
                m_openFrames = mode == VoiceInputMode.PushToTalk ? s_tailFrames : s_hangoverFrames;
                IsOpen = true;
                return true;
            }
            IsOpen = m_openFrames > 0;
            if (IsOpen)
                m_openFrames--;
            return IsOpen;
        }

        public void Reset() {
            m_openFrames = 0;
            IsOpen = false;
        }

        /// <summary>
        /// The RMS loudness of <paramref name="count"/> samples turned up by <paramref name="gain"/>, in dB relative to full
        /// scale. <see cref="SILENCE_DB"/> at most quiet.
        /// </summary>
        public static float GetLevelDb(float[] samples, int count, float gain = 1f) {
            if (count <= 0) return SILENCE_DB;
            var sum = 0d;
            for (var i = 0; i < count; i++)
                sum += samples[i] * samples[i];
            var rms = Math.Sqrt(sum / count) * gain;
            return rms <= 1e-5d ? SILENCE_DB : (float)(20d * Math.Log10(rms));
        }
    }
}
