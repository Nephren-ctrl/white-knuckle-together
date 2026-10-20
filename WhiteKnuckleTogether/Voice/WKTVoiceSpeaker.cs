using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// One remote player's voice on this machine: their packets are reordered by a jitter buffer, decoded, resampled to the
    /// speakers' rate and handed to a <see cref="WKTVoiceSource"/> in the world. Allocates nothing per frame.
    /// </summary>
    internal sealed class WKTVoiceSpeaker : IDisposable {
        /// <summary>How much decoded voice is kept ready for the audio thread, which takes it in bursts.</summary>
        private const float c_ready_seconds = 0.06f;
        /// <summary>How long after their last packet a player still shows as speaking.</summary>
        private const float c_speaking_seconds = 0.25f;
        private const float c_occlusion_smoothing = 10f;
        /// <summary>How fast the shown loudness falls back once the voice gets quieter, in dB per second.</summary>
        private const float c_level_fall_db = 60f;
        private readonly WKTVoiceJitterBuffer m_jitter = new();
        private readonly WKTVoiceDecoder m_decoder = new();
        private readonly WKTVoiceResampler m_resampler;
        private readonly float[] m_frame = new float[WKTVoiceConsts.FRAME_SAMPLES];
        private readonly float[] m_resampled;
        private readonly int m_readySamples;
        private float m_lastHeard = float.NegativeInfinity;
        private float m_cutoff = WKTVoiceRules.CLEAR_CUTOFF;
        private float m_wallGain = 1f;

        public WKTVoiceSource Source { get; }
        /// <summary>Walls between the listener and this speaker at the last check, for muffling.</summary>
        public int Walls { get; set; }
        /// <summary>When the walls are checked next.</summary>
        public float NextOcclusionCheck { get; set; }
        public bool IsSpeaking => Time.unscaledTime - m_lastHeard < c_speaking_seconds;
        /// <summary>Packets taken into the jitter buffer so far, for diagnostics.</summary>
        public int PacketsReceived { get; private set; }
        /// <summary>Frames decoded from their own packet so far, for diagnostics.</summary>
        public int FramesDecoded { get; private set; }
        /// <summary>Frames rebuilt or concealed because their packet was missing, for diagnostics.</summary>
        public int FramesConcealed { get; private set; }
        public int BufferedPackets => m_jitter.BufferedCount;
        public int DelayFrames => m_jitter.DelayFrames;
        /// <summary>How loud the voice is now, in dBFS: the loudest frame lately, falling off smoothly.</summary>
        public float LevelDb { get; private set; } = WKTVoiceActivation.SILENCE_DB;

        public WKTVoiceSpeaker(string name) {
            Source = WKTVoiceSource.Create(name);
            var outputRate = AudioSettings.outputSampleRate;
            m_resampler = new WKTVoiceResampler(WKTVoiceConsts.SAMPLE_RATE, outputRate);
            m_resampled = new float[m_resampler.MaxOutput(WKTVoiceConsts.FRAME_SAMPLES)];
            m_readySamples = (int)(outputRate * c_ready_seconds);
        }

        public void Receive(ushort sequence, byte[] packet, int length) {
            if (!m_jitter.Insert(sequence, packet, 0, length)) return;
            m_lastHeard = Time.unscaledTime;
            PacketsReceived++;
        }

        /// <summary>
        /// Decodes enough to keep the audio thread fed, and applies the volume (<paramref name="volume"/> up to
        /// <see cref="WKTConfig.MAX_VOLUME"/>, on top of <see cref="WKTVoiceConsts.PLAYBACK_GAIN"/>) and muffling for this frame.
        /// </summary>
        public void Update(float volume, bool spatial, float deltaTime) {
            var smoothing = 1f - Mathf.Exp(-c_occlusion_smoothing * deltaTime);
            var walls = spatial ? Walls : 0;
            m_cutoff = Mathf.Lerp(m_cutoff, WKTVoiceRules.GetCutoff(walls), smoothing);
            m_wallGain = Mathf.Lerp(m_wallGain, WKTVoiceRules.GetWallGain(walls), smoothing);
            WKTVoiceRules.SplitVolume(volume * WKTVoiceConsts.PLAYBACK_GAIN * m_wallGain, out var sourceVolume, out var boost);
            Source.SetSpatial(spatial);
            Source.SetMix(sourceVolume, boost, m_cutoff);
            LevelDb = Mathf.Max(WKTVoiceActivation.SILENCE_DB, LevelDb - c_level_fall_db * deltaTime);
            var ring = Source.Ring;
            while (ring.Count < m_readySamples) {
                var kind = m_jitter.Pull(out var packet, out var length);
                int decoded;
                switch (kind) {
                    case WKTVoiceFrameKind.Packet:
                        decoded = m_decoder.Decode(packet, length, m_frame);
                        FramesDecoded++;
                        break;
                    case WKTVoiceFrameKind.FromNext:
                        decoded = m_decoder.DecodeFromNext(packet, length, m_frame);
                        FramesConcealed++;
                        break;
                    case WKTVoiceFrameKind.Lost:
                        decoded = m_decoder.Conceal(m_frame);
                        FramesConcealed++;
                        break;
                    default:
                        return;
                }
                if (decoded == 0)
                    Array.Clear(m_frame, 0, m_frame.Length);
                else
                    LevelDb = Mathf.Max(LevelDb, WKTVoiceActivation.GetLevelDb(m_frame, decoded));
                var resampled = m_resampler.Process(m_frame, m_frame.Length, m_resampled, 0);
                ring.Write(m_resampled, 0, resampled);
            }
        }

        public void Dispose() {
            m_decoder.Dispose();
            if (Source != null)
                Object.Destroy(Source.gameObject);
        }
    }
}
