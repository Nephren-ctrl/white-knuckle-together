using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Records the local microphone and hands it out in 48 kHz Opus-sized frames, whatever rate the device runs at. Reads the
    /// microphone's looping clip in fixed 10 ms chunks into reused buffers, so recording allocates nothing per frame.
    /// </summary>
    internal sealed class WKTVoiceCapture : IDisposable {
        /// <summary>Length of the microphone's looping clip. Frames are read every game frame, far more often.</summary>
        private const int c_clip_seconds = 1;
        private readonly WKTVoiceResampler m_resampler = new(WKTVoiceConsts.SAMPLE_RATE, WKTVoiceConsts.SAMPLE_RATE);
        private readonly WKTVoiceFrameBuffer m_frames = new(WKTVoiceConsts.FRAME_SAMPLES * 8);
        private AudioClip? m_clip;
        private string? m_device;
        private float[] m_chunk = Array.Empty<float>();
        private float[] m_resampled = Array.Empty<float>();
        private int m_readPosition;

        public bool IsRecording => m_clip != null;
        /// <summary>The device name asked for when recording started, empty for the default.</summary>
        public string RequestedDevice { get; private set; } = string.Empty;

        /// <summary>
        /// The name of the microphone <paramref name="requested"/> records: itself if it is plugged in, else the first one
        /// Windows lists, usually the default. Empty if there is none.
        /// </summary>
        public static string ResolveDevice(string requested) {
            var devices = Microphone.devices;
            if (!string.IsNullOrEmpty(requested) && Array.IndexOf(devices, requested) >= 0) return requested;
            return devices.Length > 0 ? devices[0] : string.Empty;
        }

        /// <summary>Starts recording <paramref name="deviceName"/>, or the default microphone if it is empty or gone. False if there is none.</summary>
        public bool Start(string deviceName) {
            Stop();
            RequestedDevice = deviceName;
            m_device = string.IsNullOrEmpty(deviceName) || Array.IndexOf(Microphone.devices, deviceName) < 0 ? null : deviceName;
            if (Microphone.devices.Length == 0) return false;
            Microphone.GetDeviceCaps(m_device, out var minRate, out var maxRate);
            var rate = minRate == 0 && maxRate == 0 ? WKTVoiceConsts.SAMPLE_RATE : Mathf.Clamp(WKTVoiceConsts.SAMPLE_RATE, minRate, maxRate);
            m_clip = Microphone.Start(m_device, true, c_clip_seconds, rate);
            if (m_clip == null) return false;
            // The clip may run at another rate than asked.
            rate = m_clip.frequency;
            m_chunk = new float[rate / 100];
            m_resampler.SetRates(rate, WKTVoiceConsts.SAMPLE_RATE);
            m_resampled = new float[m_resampler.MaxOutput(m_chunk.Length)];
            m_frames.Clear();
            m_readPosition = Microphone.GetPosition(m_device);
            return true;
        }

        public void Stop() {
            if (m_clip == null) return;
            Microphone.End(m_device);
            Object.Destroy(m_clip);
            m_clip = null;
        }

        /// <summary>Reads what the microphone recorded since the last call. Call once per game frame, then take frames.</summary>
        public void Pump() {
            if (m_clip == null) return;
            if (!Microphone.IsRecording(m_device)) {
                // Unplugged: try again, on the default device if it is the one that left.
                Start(RequestedDevice);
                return;
            }
            var length = m_clip.samples;
            var position = Microphone.GetPosition(m_device);
            var available = (position - m_readPosition + length) % length;
            while (available >= m_chunk.Length) {
                // Reads past the clip's end wrap to its start.
                m_clip.GetData(m_chunk, m_readPosition);
                m_readPosition = (m_readPosition + m_chunk.Length) % length;
                available -= m_chunk.Length;
                var resampled = m_resampler.Process(m_chunk, m_chunk.Length, m_resampled, 0);
                m_frames.Write(m_resampled, 0, resampled);
            }
        }

        /// <summary>Takes the next recorded 20 ms frame, if a whole one is ready.</summary>
        public bool TryReadFrame(float[] frame) => m_frames.TryReadFrame(frame);

        public void Dispose() => Stop();
    }
}
