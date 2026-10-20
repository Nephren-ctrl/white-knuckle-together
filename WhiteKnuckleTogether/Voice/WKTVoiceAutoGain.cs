using System;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Works out the gain that brings the microphone to a steady speaking level, as loud as the game's own sounds, whatever
    /// the microphone or its system volume. It follows the loudness of speech (the louder frames, not the pauses between
    /// words) and steers the gain toward <see cref="TARGET_DB"/>, rising slowly and falling faster, within
    /// <see cref="MIN_GAIN_DB"/> to <see cref="MAX_GAIN_DB"/>. It only measures: the gain is applied by a
    /// <see cref="WKTVoiceLimiter"/>, which catches the peaks. Meant for frames that are being sent, so silence never pumps
    /// the gain up. Allocates nothing.
    /// </summary>
    internal sealed class WKTVoiceAutoGain {
        /// <summary>The level of the louder speech frames aimed for, in dBFS: the limiter rounds off the peaks above it.</summary>
        public const float TARGET_DB = -20f;
        public const float MAX_GAIN_DB = 24f;
        public const float MIN_GAIN_DB = -18f;
        public const float RISE_DB_PER_SECOND = 10f;
        /// <summary>Fast, so a word louder than the last few is brought down within a syllable rather than slammed by the limiter.</summary>
        public const float FALL_DB_PER_SECOND = 120f;
        /// <summary>Frames quieter than this (dBFS) carry nothing to measure.</summary>
        private const float c_floor_db = -75f;
        /// <summary>Frames this far under the speech level are pauses between words, and leave it alone.</summary>
        private const float c_pause_db = 15f;
        /// <summary>How fast the speech level follows louder frames, and quieter ones, per frame.</summary>
        private const float c_level_attack = 0.3f;
        private const float c_level_release = 0.05f;
        private static readonly float s_riseStep = RISE_DB_PER_SECOND * WKTVoiceConsts.FRAME_SECONDS;
        private static readonly float s_fallStep = FALL_DB_PER_SECOND * WKTVoiceConsts.FRAME_SECONDS;
        private float m_levelDb;
        private bool m_hasLevel;

        public float GainDb { get; private set; }
        public float Gain => (float)Math.Pow(10d, GainDb / 20d);

        public void Reset() {
            m_hasLevel = false;
            m_levelDb = 0f;
            GainDb = 0f;
        }

        /// <summary>Measures one frame about to be sent and moves <see cref="GainDb"/> toward the level it calls for.</summary>
        public void Update(ReadOnlySpan<float> samples) {
            if (samples.Length == 0) return;
            var sum = 0d;
            for (var i = 0; i < samples.Length; i++)
                sum += samples[i] * samples[i];
            var levelDb = (float)(10d * Math.Log10(Math.Max(sum / samples.Length, 1e-20)));
            if (levelDb < c_floor_db) return;
            if (!m_hasLevel) {
                // The first word is already at the right level, rather than a few seconds later.
                m_levelDb = levelDb;
                m_hasLevel = true;
                GainDb = GetWantedGainDb();
                return;
            }
            if (levelDb > m_levelDb)
                m_levelDb += (levelDb - m_levelDb) * c_level_attack;
            else if (levelDb > m_levelDb - c_pause_db)
                m_levelDb += (levelDb - m_levelDb) * c_level_release;
            else
                return;
            var wanted = GetWantedGainDb();
            GainDb = wanted > GainDb ? Math.Min(GainDb + s_riseStep, wanted) : Math.Max(GainDb - s_fallStep, wanted);
        }

        private float GetWantedGainDb() => Math.Min(Math.Max(TARGET_DB - m_levelDb, MIN_GAIN_DB), MAX_GAIN_DB);
    }
}
