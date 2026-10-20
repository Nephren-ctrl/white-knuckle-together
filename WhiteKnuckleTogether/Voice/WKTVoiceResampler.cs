namespace WhiteKnuckleTogether {
    /// <summary>
    /// Streaming linear resampler for mono audio. Keeps its position between calls, so audio fed in pieces of any size comes
    /// out continuous. Linear interpolation is plenty for speech.
    /// </summary>
    internal sealed class WKTVoiceResampler {
        private double m_step;
        /// <summary>Position of the next output sample, in input samples, relative to the start of the next input piece.</summary>
        private double m_position;
        /// <summary>The last input sample of the previous piece, the left neighbour of a position below zero.</summary>
        private float m_previous;

        public int InputRate { get; private set; }
        public int OutputRate { get; private set; }

        public WKTVoiceResampler(int inputRate, int outputRate) => SetRates(inputRate, outputRate);

        /// <summary>The most output samples <see cref="Process"/> can produce from <paramref name="inputCount"/> input samples.</summary>
        public int MaxOutput(int inputCount) => (int)(inputCount / m_step) + 2;

        public void SetRates(int inputRate, int outputRate) {
            InputRate = inputRate;
            OutputRate = outputRate;
            m_step = (double)inputRate / outputRate;
            Reset();
        }

        public void Reset() {
            m_position = 0d;
            m_previous = 0f;
        }

        /// <summary>
        /// Resamples <paramref name="count"/> samples of <paramref name="input"/> into <paramref name="output"/> from
        /// <paramref name="outputOffset"/>, which needs room for <see cref="MaxOutput"/>. Returns how many it wrote.
        /// </summary>
        public int Process(float[] input, int count, float[] output, int outputOffset) {
            if (count <= 0) return 0;
            var written = 0;
            if (InputRate == OutputRate) {
                System.Array.Copy(input, 0, output, outputOffset, count);
                m_previous = input[count - 1];
                return count;
            }
            // Each output sample sits between input samples floor(p) and floor(p) + 1; index -1 is the previous piece's last.
            while (m_position < count - 1) {
                var index = (int)System.Math.Floor(m_position);
                var fraction = (float)(m_position - index);
                var left = index < 0 ? m_previous : input[index];
                var right = input[index + 1];
                output[outputOffset + written++] = left + (right - left) * fraction;
                m_position += m_step;
            }
            m_position -= count;
            m_previous = input[count - 1];
            return written;
        }
    }
}
