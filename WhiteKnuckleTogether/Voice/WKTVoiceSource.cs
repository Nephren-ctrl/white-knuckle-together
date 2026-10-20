using System.Threading;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Plays one speaker's voice through a regular 3D audio source, so distance falloff and master volume apply. The source
    /// plays a constant clip of ones and this filter multiplies it by the decoded voice, read
    /// from a <see cref="WKTVoicePcmRing"/> on the audio thread, times a boost for volumes above 100%, rounded off by
    /// <see cref="WKTVoiceRules.SoftClip"/> so a loud voice never clips harshly at the speakers. A reverb filter after it gives
    /// the voice the listener's room (see <see cref="WKTVoiceRoomReverb"/>), and a low-pass filter after that muffles it
    /// through walls; each is off when it has nothing to do.
    /// <para>
    /// The game's reverb zones are bypassed: their send takes the clip before this filter, so they would get the constant
    /// ones, a full-scale offset that clipped the whole mix near any voice, talking or not (measured: a silent voice 2 m away
    /// added 0.36 DC to the output at 20% master volume). The reverb filter only ever hears the voice.
    /// </para>
    /// <para>Unity stops every source when the audio device changes, so <see cref="Restart"/> starts it again.</para>
    /// </summary>
    internal sealed class WKTVoiceSource : MonoBehaviour {
        /// <summary>Half a second of decoded voice, far more than is ever kept buffered.</summary>
        private const int c_ring_samples = 32768;
        /// <summary>Larger than any audio buffer Unity hands a filter.</summary>
        private const int c_scratch_samples = 16384;
        /// <summary>Muffling cutoffs at least this high leave the voice clear, and the filter is switched off.</summary>
        private const float c_clear_cutoff = 20000f;
        /// <summary>The highest cutoff used, as a share of the output rate, safely under the Nyquist frequency.</summary>
        private const float c_max_cutoff_fraction = 0.45f;
        private static AudioClip? s_onesClip;
        private readonly float[] m_scratch = new float[c_scratch_samples];
        private AudioSource m_source = null!;
        private AudioReverbFilter m_reverb = null!;
        private AudioLowPassFilter m_lowPass = null!;
        /// <summary>Read on the audio thread; a float write is atomic.</summary>
        private volatile float m_boost = 1f;
        private int m_starvedReads;
        private int m_reads;

        public WKTVoicePcmRing Ring { get; } = new(c_ring_samples);
        /// <summary>Audio thread reads so far that found less voice buffered than they needed, for diagnostics.</summary>
        public int StarvedReads => Volatile.Read(ref m_starvedReads);
        /// <summary>Audio thread reads so far, for diagnostics.</summary>
        public int Reads => Volatile.Read(ref m_reads);

        /// <summary>A new voice in the world, kept across scene loads.</summary>
        public static WKTVoiceSource Create(string name) {
            var obj = new GameObject(name);
            DontDestroyOnLoad(obj);
            // The order matters: the source, then this filter writing the voice, the room's reverb on it, then the muffling.
            var source = obj.AddComponent<AudioSource>();
            var voice = obj.AddComponent<WKTVoiceSource>();
            voice.m_source = source;
            voice.m_reverb = obj.AddComponent<AudioReverbFilter>();
            voice.m_lowPass = obj.AddComponent<AudioLowPassFilter>();
            voice.Setup();
            return voice;
        }

        [UsedImplicitly]
        private void OnAudioFilterRead(float[] data, int channels) {
            var frames = data.Length / channels;
            if (frames > m_scratch.Length)
                frames = m_scratch.Length;
            var real = Ring.Read(m_scratch, 0, frames);
            Interlocked.Increment(ref m_reads);
            if (real < frames)
                Interlocked.Increment(ref m_starvedReads);
            var boost = m_boost;
            for (var i = 0; i < frames; i++) {
                var sample = WKTVoiceRules.SoftClip(m_scratch[i] * boost);
                var start = i * channels;
                for (var c = 0; c < channels; c++)
                    data[start + c] *= sample;
            }
        }

        /// <summary>Flat in the listener's ears, or from this object's position in the world.</summary>
        public void SetSpatial(bool spatial) {
            var blend = spatial ? 1f : 0f;
            if (m_source.spatialBlend != blend)
                m_source.spatialBlend = blend;
        }

        /// <summary><paramref name="volume"/> up to 1 on the audio source, and <paramref name="boost"/> on the samples beyond that.</summary>
        public void SetMix(float volume, float boost, float cutoff) {
            m_source.volume = volume;
            m_boost = boost;
            var muffled = cutoff < c_clear_cutoff;
            if (m_lowPass.enabled != muffled)
                m_lowPass.enabled = muffled;
            if (muffled)
                m_lowPass.cutoffFrequency = Mathf.Min(cutoff, AudioSettings.outputSampleRate * c_max_cutoff_fraction);
        }

        /// <summary>The room's reverb on this voice, or none.</summary>
        public void SetReverb(in WKTReverbSettings settings) => WKTVoiceRoomReverb.Apply(m_reverb, settings);

        /// <summary>Plays again after the audio device changed, which stops every source, on a carrier at the new output rate.</summary>
        public void Restart() {
            m_source.Stop();
            m_source.clip = GetOnesClip();
            m_source.Play();
        }

        private void Setup() {
            m_source.clip = GetOnesClip();
            m_source.loop = true;
            m_source.playOnAwake = false;
            m_source.dopplerLevel = 0f;
            m_source.spread = 0f;
            m_source.priority = 0;
            // Reverb zones would get the constant carrier, not the voice (see the class summary).
            m_source.bypassReverbZones = true;
            m_source.reverbZoneMix = 0f;
            // The pause menu pauses every other sound, but players keep talking.
            m_source.ignoreListenerPause = true;
            m_source.rolloffMode = AudioRolloffMode.Custom;
            m_source.maxDistance = WKTVoiceConsts.RANGE;
            m_source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, BuildRolloff());
            m_source.spatialBlend = 0f;
            m_reverb.reverbPreset = AudioReverbPreset.User;
            m_reverb.dryLevel = 0f;
            m_reverb.enabled = false;
            m_lowPass.enabled = false;
            m_lowPass.lowpassResonanceQ = 1f;
            m_source.Play();
        }

        /// <summary>Full volume up close, then falling off to nothing at <see cref="WKTVoiceConsts.RANGE"/>.</summary>
        private static AnimationCurve BuildRolloff() {
            var full = WKTVoiceConsts.FULL_VOLUME_DISTANCE / WKTVoiceConsts.RANGE;
            return new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(full, 1f), new Keyframe(0.3f, 0.4f), new Keyframe(0.6f, 0.12f),
                new Keyframe(1f, 0f));
        }

        /// <summary>A second of ones at the output rate, made again when the rate changes.</summary>
        private static AudioClip GetOnesClip() {
            var rate = AudioSettings.outputSampleRate;
            if (s_onesClip != null && s_onesClip.frequency == rate) return s_onesClip;
            var samples = new float[rate];
            for (var i = 0; i < samples.Length; i++)
                samples[i] = 1f;
            s_onesClip = AudioClip.Create("WKT Voice Carrier", samples.Length, 1, rate, false);
            s_onesClip.SetData(samples, 0);
            return s_onesClip;
        }
    }
}
