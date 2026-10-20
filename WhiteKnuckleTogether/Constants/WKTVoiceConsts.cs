namespace WhiteKnuckleTogether {
    public static class WKTVoiceConsts {
        /// <summary>Voice is encoded, sent and decoded at Opus's native rate, whatever the microphone or speakers run at.</summary>
        public const int SAMPLE_RATE = 48000;
        /// <summary>20 ms per Opus frame.</summary>
        public const int FRAME_SAMPLES = 960;
        public const float FRAME_SECONDS = (float)FRAME_SAMPLES / SAMPLE_RATE;
        /// <summary>
        /// Enough for Opus to keep the full audio band (up to 20 kHz) with error correction on top, as clear as other games'
        /// voice chat. About 6 KB/s for each player talking.
        /// </summary>
        public const int BITRATE = 48000;
        /// <summary>The largest Opus packet accepted, far above what <see cref="BITRATE"/> produces for one frame.</summary>
        public const int MAX_PACKET_BYTES = 400;
        /// <summary>How far a player's voice carries in a run.</summary>
        public const float RANGE = 60f;
        /// <summary>Within this distance a voice is at full volume.</summary>
        public const float FULL_VOLUME_DISTANCE = 4.5f;
        /// <summary>
        /// How loud a voice plays at 100% against its samples, so speech sits a little above the game's sounds. Measured in a
        /// run: the game's sounds peak around 0.15 of full scale, and an auto-levelled voice around 0.45 to 0.6 at this gain. At
        /// 2.5 voices peaked at full scale and drowned the game out; 0.8 was a little quiet.
        /// </summary>
        public const float PLAYBACK_GAIN = 1.2f;
        /// <summary>
        /// How much the microphone is turned up at 100% Mic Volume with auto mic level off, since microphones deliver speech
        /// far under full scale. Only the sent voice: the voice activation level is measured without it.
        /// </summary>
        public const float MANUAL_MIC_GAIN = 2f;
    }
}
