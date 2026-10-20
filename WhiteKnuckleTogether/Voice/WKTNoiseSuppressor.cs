using System;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Takes steady background noise (fans, hum, hiss) out of the microphone, and only that. Each frequency band learns its
    /// noise floor from the quietest it has recently been, rising back only slowly, so a sound that stays put becomes the
    /// floor and anything that rises above it (speech, breaths, laughs, sudden sounds) passes. The floor is turned down by at
    /// most <see cref="MAX_REDUCTION_DB"/>, never muted, so nothing a player makes is ever cut out entirely.
    /// <para>
    /// Streaming short-time Fourier transform: <see cref="FFT_SIZE"/>-sample square-root Hann windows at 50% overlap, which
    /// rebuild the signal exactly when nothing is changed. The gain per band is a Wiener gain from a decision-directed estimate
    /// of its signal to noise ratio, which keeps the leftover noise smooth rather than twittering. Adds
    /// <see cref="LATENCY_SAMPLES"/> of delay. Allocates nothing after construction.
    /// </para>
    /// </summary>
    internal sealed class WKTNoiseSuppressor {
        public const int FFT_SIZE = 512;
        public const int HOP = FFT_SIZE / 2;
        public const int BINS = FFT_SIZE / 2 + 1;
        /// <summary>The delay the suppressor adds, in samples.</summary>
        public const int LATENCY_SAMPLES = FFT_SIZE;
        /// <summary>The most the noise floor is turned down, in dB.</summary>
        public const float MAX_REDUCTION_DB = 15f;
        /// <summary>How fast the noise floor may rise while louder sound goes on, in dB per second: slow, so speech never becomes the floor.</summary>
        public const float FLOOR_RISE_DB_PER_SECOND = 2.5f;
        /// <summary>How much of a band's earlier SNR estimate carries over (decision-directed smoothing).</summary>
        private const float c_prior_weight = 0.96f;
        /// <summary>How much a band's power is smoothed over time before it is compared with the floor.</summary>
        private const float c_power_smoothing = 0.6f;
        /// <summary>How fast the floor follows a quieter band down.</summary>
        private const float c_floor_fall = 0.15f;
        /// <summary>Blocks the floor is learned over before any gain is applied (about a quarter of a second).</summary>
        private const int c_warmup_blocks = 48;
        private const float c_epsilon = 1e-12f;
        private const int c_buffer = FFT_SIZE * 8;
        private static readonly float[] s_window = CreateWindow();
        private static readonly float[] s_cos = new float[FFT_SIZE / 2];
        private static readonly float[] s_sin = new float[FFT_SIZE / 2];
        private static readonly int[] s_bitReverse = CreateBitReverse();
        private static readonly float s_minGain = (float)Math.Pow(10.0, -MAX_REDUCTION_DB / 20.0);
        private static readonly float s_floorRise =
            (float)Math.Pow(10.0, FLOOR_RISE_DB_PER_SECOND / 10.0 * HOP / WKTVoiceConsts.SAMPLE_RATE);
        private readonly float[] m_real = new float[FFT_SIZE];
        private readonly float[] m_imaginary = new float[FFT_SIZE];
        private readonly float[] m_frameIn = new float[FFT_SIZE];
        private readonly float[] m_overlap = new float[FFT_SIZE];
        private readonly float[] m_power = new float[BINS];
        private readonly float[] m_floor = new float[BINS];
        private readonly float[] m_prior = new float[BINS];
        private readonly float[] m_lastPosterior = new float[BINS];
        private readonly float[] m_gain = new float[BINS];
        private readonly float[] m_smoothedGain = new float[BINS];
        private readonly float[] m_input = new float[c_buffer];
        private readonly float[] m_output = new float[c_buffer];
        private int m_inputCount;
        private int m_outputStart;
        private int m_outputCount;
        private int m_blocks;

        static WKTNoiseSuppressor() {
            for (var i = 0; i < FFT_SIZE / 2; i++) {
                var angle = -2.0 * Math.PI * i / FFT_SIZE;
                s_cos[i] = (float)Math.Cos(angle);
                s_sin[i] = (float)Math.Sin(angle);
            }
        }

        public WKTNoiseSuppressor() => Reset();

        /// <summary>When true, the bands are still rebuilt but never turned down: for checking the reconstruction.</summary>
        public bool Bypass { get; set; }

        /// <summary>Forgets the learned noise and any buffered sound, as for a new microphone.</summary>
        public void Reset() {
            Array.Clear(m_frameIn, 0, m_frameIn.Length);
            Array.Clear(m_overlap, 0, m_overlap.Length);
            Array.Clear(m_power, 0, m_power.Length);
            Array.Clear(m_floor, 0, m_floor.Length);
            Array.Clear(m_prior, 0, m_prior.Length);
            Array.Clear(m_lastPosterior, 0, m_lastPosterior.Length);
            for (var i = 0; i < BINS; i++) {
                m_gain[i] = 1f;
                m_smoothedGain[i] = 1f;
            }
            m_inputCount = 0;
            m_blocks = 0;
            // Primed with silence, so a whole frame is always ready to give back.
            Array.Clear(m_output, 0, m_output.Length);
            m_outputStart = 0;
            m_outputCount = LATENCY_SAMPLES - HOP;
        }

        /// <summary>Cleans <paramref name="samples"/> in place, delayed by <see cref="LATENCY_SAMPLES"/>.</summary>
        public void Process(Span<float> samples) {
            var read = 0;
            while (read < samples.Length) {
                var take = Math.Min(HOP - m_inputCount, samples.Length - read);
                samples.Slice(read, take).CopyTo(m_input.AsSpan(m_inputCount, take));
                m_inputCount += take;
                read += take;
                if (m_inputCount < HOP) continue;
                ProcessBlock();
                m_inputCount = 0;
            }
            for (var i = 0; i < samples.Length; i++) {
                samples[i] = m_output[m_outputStart];
                m_outputStart = (m_outputStart + 1) % c_buffer;
            }
            m_outputCount -= samples.Length;
        }

        /// <summary>One hop: slide the window, transform, turn the noise down, transform back, overlap and add.</summary>
        private void ProcessBlock() {
            Array.Copy(m_frameIn, HOP, m_frameIn, 0, FFT_SIZE - HOP);
            Array.Copy(m_input, 0, m_frameIn, FFT_SIZE - HOP, HOP);
            for (var i = 0; i < FFT_SIZE; i++) {
                m_real[i] = m_frameIn[i] * s_window[i];
                m_imaginary[i] = 0f;
            }
            Transform(m_real, m_imaginary, false);
            UpdateGains();
            for (var bin = 0; bin < BINS; bin++) {
                var gain = Bypass ? 1f : m_smoothedGain[bin];
                m_real[bin] *= gain;
                m_imaginary[bin] *= gain;
                // Mirror, so the inverse stays real.
                if (bin == 0 || bin == FFT_SIZE / 2) continue;
                m_real[FFT_SIZE - bin] = m_real[bin];
                m_imaginary[FFT_SIZE - bin] = -m_imaginary[bin];
            }
            Transform(m_real, m_imaginary, true);
            for (var i = 0; i < FFT_SIZE; i++)
                m_overlap[i] += m_real[i] * s_window[i];
            // The first hop is now complete.
            var write = (m_outputStart + m_outputCount) % c_buffer;
            for (var i = 0; i < HOP; i++) {
                m_output[write] = m_overlap[i];
                write = (write + 1) % c_buffer;
            }
            m_outputCount += HOP;
            Array.Copy(m_overlap, HOP, m_overlap, 0, FFT_SIZE - HOP);
            Array.Clear(m_overlap, FFT_SIZE - HOP, HOP);
            m_blocks++;
        }

        private void UpdateGains() {
            var learning = m_blocks < c_warmup_blocks;
            for (var bin = 0; bin < BINS; bin++) {
                var power = m_real[bin] * m_real[bin] + m_imaginary[bin] * m_imaginary[bin];
                m_power[bin] = m_blocks == 0 ? power : c_power_smoothing * m_power[bin] + (1f - c_power_smoothing) * power;
                m_floor[bin] = TrackFloor(m_floor[bin], m_power[bin], m_blocks == 0);
                var noise = m_floor[bin] + c_epsilon;
                var posterior = power / noise;
                m_prior[bin] = DecisionDirectedPrior(m_gain[bin], m_lastPosterior[bin], posterior);
                m_lastPosterior[bin] = posterior;
                m_gain[bin] = learning ? 1f : WienerGain(m_prior[bin]);
            }
            // A little smoothing across neighbouring bands keeps the leftover noise from twittering.
            for (var bin = 0; bin < BINS; bin++) {
                var left = m_gain[Math.Max(bin - 1, 0)];
                var right = m_gain[Math.Min(bin + 1, BINS - 1)];
                var target = 0.25f * left + 0.5f * m_gain[bin] + 0.25f * right;
                // Opens at once for a sound starting, closes more gently after it.
                m_smoothedGain[bin] = target > m_smoothedGain[bin] ? target : 0.7f * m_smoothedGain[bin] + 0.3f * target;
            }
        }

        /// <summary>
        /// The noise floor of a band: follows the band's smoothed power down quickly and rises only slowly, so it settles on
        /// whatever never goes away.
        /// </summary>
        public static float TrackFloor(float floor, float power, bool first) {
            if (first) return power;
            if (power < floor) return floor + c_floor_fall * (power - floor);
            return Math.Min(floor * s_floorRise, power);
        }

        /// <summary>
        /// The decision-directed estimate of a band's a priori signal to noise ratio: mostly what the last block's gain left
        /// of its power over the noise, plus a little of this block's power over the noise.
        /// </summary>
        public static float DecisionDirectedPrior(float previousGain, float previousPosterior, float posterior) =>
            c_prior_weight * previousGain * previousGain * previousPosterior + (1f - c_prior_weight) * Math.Max(posterior - 1f, 0f);

        /// <summary>The Wiener gain for a signal to noise ratio, never below the floor of <see cref="MAX_REDUCTION_DB"/>.</summary>
        public static float WienerGain(float prior) => Math.Max(prior / (1f + prior), s_minGain);

        /// <summary>The smallest gain applied: <see cref="MAX_REDUCTION_DB"/> down.</summary>
        public static float MinGain => s_minGain;

        /// <summary>In-place radix-2 complex FFT, or its inverse (scaled by 1/N).</summary>
        private static void Transform(float[] real, float[] imaginary, bool inverse) {
            for (var i = 0; i < FFT_SIZE; i++) {
                var j = s_bitReverse[i];
                if (j <= i) continue;
                (real[i], real[j]) = (real[j], real[i]);
                (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
            }
            for (var size = 2; size <= FFT_SIZE; size <<= 1) {
                var half = size >> 1;
                var step = FFT_SIZE / size;
                for (var start = 0; start < FFT_SIZE; start += size) {
                    for (var k = 0; k < half; k++) {
                        var cos = s_cos[k * step];
                        var sin = inverse ? -s_sin[k * step] : s_sin[k * step];
                        var a = start + k;
                        var b = a + half;
                        var tr = real[b] * cos - imaginary[b] * sin;
                        var ti = real[b] * sin + imaginary[b] * cos;
                        real[b] = real[a] - tr;
                        imaginary[b] = imaginary[a] - ti;
                        real[a] += tr;
                        imaginary[a] += ti;
                    }
                }
            }
            if (!inverse) return;
            const float scale = 1f / FFT_SIZE;
            for (var i = 0; i < FFT_SIZE; i++) {
                real[i] *= scale;
                imaginary[i] *= scale;
            }
        }

        /// <summary>A periodic square-root Hann window: applied on the way in and out, at 50% overlap the two sum to one.</summary>
        private static float[] CreateWindow() {
            var window = new float[FFT_SIZE];
            for (var i = 0; i < FFT_SIZE; i++)
                window[i] = (float)Math.Sqrt(0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / FFT_SIZE));
            return window;
        }

        private static int[] CreateBitReverse() {
            var bits = 0;
            while (1 << bits < FFT_SIZE)
                bits++;
            var table = new int[FFT_SIZE];
            for (var i = 0; i < FFT_SIZE; i++) {
                var reversed = 0;
                for (var b = 0; b < bits; b++)
                    reversed |= ((i >> b) & 1) << (bits - 1 - b);
                table[i] = reversed;
            }
            return table;
        }
    }
}
