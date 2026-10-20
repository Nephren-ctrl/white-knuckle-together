using System;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Encodes 20 ms frames of 48 kHz mono speech with Opus, over the full audio band, with in-band error correction for
    /// lost packets.
    /// </summary>
    internal sealed class WKTVoiceEncoder : IDisposable {
        /// <summary>How much loss the error correction is sized for, in percent.</summary>
        /// <summary>Kept low, as the error correction is paid for out of the bitrate and would narrow the band.</summary>
        public const int EXPECTED_LOSS_PERCENT = 5;
        /// <summary>The best quality: costs well under a millisecond per frame for one mono voice.</summary>
        public const int COMPLEXITY = 10;
        private readonly WKTOpus.EncoderHandle m_encoder;

        /// <exception cref="InvalidOperationException">libopus is missing or refused the settings.</exception>
        public WKTVoiceEncoder() {
            if (!WKTOpus.TryLoad(out var loadError))
                throw new InvalidOperationException("libopus is unavailable: " + loadError);
            m_encoder = WKTOpus.opus_encoder_create(WKTVoiceConsts.SAMPLE_RATE, 1, WKTOpus.APPLICATION_VOIP, out var error);
            if (error != WKTOpus.OK || m_encoder.IsInvalid)
                throw new InvalidOperationException($"Could not create an Opus encoder (error {error}).");
            Set(WKTOpus.SET_BITRATE, WKTVoiceConsts.BITRATE);
            Set(WKTOpus.SET_MAX_BANDWIDTH, WKTOpus.BANDWIDTH_FULLBAND);
            Set(WKTOpus.SET_INBAND_FEC, 1);
            Set(WKTOpus.SET_PACKET_LOSS_PERC, EXPECTED_LOSS_PERCENT);
            Set(WKTOpus.SET_SIGNAL, WKTOpus.SIGNAL_VOICE);
            Set(WKTOpus.SET_COMPLEXITY, COMPLEXITY);
        }

        /// <summary>Encodes one frame into <paramref name="packet"/>. Returns the packet's length, or 0 on failure.</summary>
        public int Encode(float[] frame, byte[] packet) {
            var length = WKTOpus.opus_encode_float(m_encoder, frame, WKTVoiceConsts.FRAME_SAMPLES, packet, packet.Length);
            return length > 0 ? length : 0;
        }

        public void Dispose() => m_encoder.Dispose();

        private void Set(int request, int value) {
            var result = WKTOpus.opus_encoder_ctl(m_encoder, request, value);
            if (result != WKTOpus.OK)
                throw new InvalidOperationException($"Opus refused setting {request} = {value} (error {result}).");
        }
    }

    /// <summary>Decodes one speaker's Opus stream, and conceals lost frames.</summary>
    internal sealed class WKTVoiceDecoder : IDisposable {
        private readonly WKTOpus.DecoderHandle m_decoder;

        /// <exception cref="InvalidOperationException">libopus is missing.</exception>
        public WKTVoiceDecoder() {
            if (!WKTOpus.TryLoad(out var loadError))
                throw new InvalidOperationException("libopus is unavailable: " + loadError);
            m_decoder = WKTOpus.opus_decoder_create(WKTVoiceConsts.SAMPLE_RATE, 1, out var error);
            if (error != WKTOpus.OK || m_decoder.IsInvalid)
                throw new InvalidOperationException($"Could not create an Opus decoder (error {error}).");
        }

        /// <summary>Decodes a received packet into one frame of <paramref name="frame"/>. Returns the samples written, or 0 if it was corrupt.</summary>
        public int Decode(byte[] packet, int length, float[] frame) => Decode(packet, length, frame, false);

        /// <summary>
        /// Rebuilds the frame before <paramref name="nextPacket"/> from the error correction data it carries, for a packet that
        /// never arrived.
        /// </summary>
        public int DecodeFromNext(byte[] nextPacket, int length, float[] frame) => Decode(nextPacket, length, frame, true);

        /// <summary>Makes up a plausible frame for a lost packet when no error correction data is at hand.</summary>
        public int Conceal(float[] frame) => Decode(null, 0, frame, false);

        public void Dispose() => m_decoder.Dispose();

        private int Decode(byte[]? packet, int length, float[] frame, bool fec) {
            // A corrupt packet from a remote peer only returns an error code: drop the packet, not the speaker.
            var samples = WKTOpus.opus_decode_float(m_decoder, packet, length, frame, WKTVoiceConsts.FRAME_SAMPLES, fec ? 1 : 0);
            return samples > 0 ? samples : 0;
        }
    }
}
