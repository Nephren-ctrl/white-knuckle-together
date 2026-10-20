using System.Text;
using JetBrains.Annotations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Logs what the game's audio is actually doing during a multiplayer run, for reports of missing or broken sound: the
    /// master output (peak and DC offset, reverb included), the mix at the listener's filters (peak and any samples that are
    /// not numbers; reverb zones are added after it), whether the audio thread is still running, the
    /// listeners, how many sources play against Unity's voice limit, the time scale and mixer, and each incoming voice's
    /// packets, decoding and buffer. Every <see cref="c_early_interval"/> seconds for the first <see cref="c_early_seconds"/>
    /// of a run, then every <see cref="c_late_interval"/>.
    /// </summary>
    internal sealed class WKTAudioDiagnostics {
        private const float c_early_interval = 2f;
        private const float c_early_seconds = 60f;
        private const float c_late_interval = 30f;
        private const int c_output_samples = 4096;
        private readonly StringBuilder m_sb = new(512);
        private readonly float[] m_output = new float[c_output_samples];
        private float m_runStart = -1f;
        private float m_nextLog;
        private double m_lastDspTime;
        private AudioListener? m_tapped;

        /// <summary>Call every frame.</summary>
        public void Update(bool inRun, WKTVoiceSpeaker?[] speakers) {
            var now = Time.unscaledTime;
            if (!inRun) {
                m_runStart = -1f;
                return;
            }
            if (m_runStart < 0f) {
                m_runStart = now;
                m_nextLog = now + c_early_interval;
                m_lastDspTime = AudioSettings.dspTime;
            }
            if (now < m_nextLog) return;
            var elapsed = now - m_runStart;
            m_nextLog = now + (elapsed < c_early_seconds ? c_early_interval : c_late_interval);
            Log(elapsed, speakers);
        }

        private void Log(float elapsed, WKTVoiceSpeaker?[] speakers) {
            var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            var enabled = 0;
            AudioListener? active = null;
            foreach (var listener in listeners) {
                if (!listener.isActiveAndEnabled) continue;
                enabled++;
                active ??= listener;
            }
            if (active != null && active != m_tapped) {
                if (!active.TryGetComponent<WKTAudioTap>(out _))
                    active.gameObject.AddComponent<WKTAudioTap>();
                m_tapped = active;
            }
            var sources = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            var playing = 0;
            foreach (var source in sources) {
                if (source.isPlaying)
                    playing++;
            }
            var config = AudioSettings.GetConfiguration();
            var dspTime = AudioSettings.dspTime;
            var dspAdvance = dspTime - m_lastDspTime;
            m_lastDspTime = dspTime;
            var camera = Camera.main;
            var listenerOffset = active != null && camera != null ? Vector3.Distance(active.transform.position, camera.transform.position) : -1f;
            WKTAudioTap.Take(out var peak, out var nonFinite, out var callbacks);
            AudioListener.GetOutputData(m_output, 0);
            var outputPeak = 0f;
            var outputSum = 0.0;
            foreach (var sample in m_output) {
                outputPeak = Mathf.Max(outputPeak, Mathf.Abs(sample));
                outputSum += sample;
            }
            var mix = string.Empty;
            var manager = AudioManager.instance;
            var mixer = manager != null && manager.gameMixer != null ? manager.gameMixer.audioMixer : null;
            if (mixer != null && mixer.GetFloat("MasterVolume", out var master) && mixer.GetFloat("pitch", out var pitch))
                mix = $" mix {master:0.#}dB pitch {pitch:0.##}";
            m_sb.Clear();
            m_sb.Append($"Audio check {elapsed:0}s: master out peak {outputPeak:0.###} dc {outputSum / c_output_samples:0.###}, ")
                .Append($"listener mix peak {peak:0.###} nonfinite {nonFinite} callbacks {callbacks}, dsp +{dspAdvance:0.##}s, ")
                .Append($"listeners {enabled}/{listeners.Length} '{(active != null ? active.name : "none")}' {listenerOffset:0.##}m from camera, ")
                .Append($"sources {playing}/{sources.Length} playing (real voices {config.numRealVoices}), timescale {Time.timeScale:0.##}, ")
                .Append($"master {AudioListener.volume:0.##}{(AudioListener.pause ? " PAUSED" : string.Empty)}{mix}");
            for (var id = 0; id < speakers.Length; id++) {
                var speaker = speakers[id];
                if (speaker == null) continue;
                var source = speaker.Source;
                m_sb.Append($" | voice {id}: rx {speaker.PacketsReceived} dec {speaker.FramesDecoded} conc {speaker.FramesConcealed} ")
                    .Append($"buf {speaker.BufferedPackets}/{speaker.DelayFrames} ring {source.Ring.Count} starved {source.StarvedReads}/{source.Reads}");
            }
            WKT.Instance.Logger.LogInfo(m_sb.ToString());
        }
    }

    /// <summary>Measures the final mix on the listener, on the audio thread. Never changes it.</summary>
    internal sealed class WKTAudioTap : MonoBehaviour {
        private static float s_peak;
        private static int s_nonFinite;
        private static int s_callbacks;
        private static readonly object s_lock = new();

        /// <summary>The mix's peak and non-finite samples since the last call, and how many audio callbacks that was.</summary>
        public static void Take(out float peak, out int nonFinite, out int callbacks) {
            lock (s_lock) {
                peak = s_peak;
                nonFinite = s_nonFinite;
                callbacks = s_callbacks;
                s_peak = 0f;
                s_nonFinite = 0;
                s_callbacks = 0;
            }
        }

        [UsedImplicitly]
        private void OnAudioFilterRead(float[] data, int channels) {
            var peak = 0f;
            var nonFinite = 0;
            foreach (var sample in data) {
                if (float.IsNaN(sample) || float.IsInfinity(sample))
                    nonFinite++;
                else if (Mathf.Abs(sample) > peak)
                    peak = Mathf.Abs(sample);
            }
            // Uncontended except for one log every few seconds.
            lock (s_lock) {
                if (peak > s_peak)
                    s_peak = peak;
                s_nonFinite += nonFinite;
                s_callbacks++;
            }
        }
    }
}
