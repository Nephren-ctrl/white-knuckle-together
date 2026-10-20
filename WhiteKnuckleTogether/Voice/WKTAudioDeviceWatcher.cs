using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Keeps the game's sound going when the audio device changes under it. Unity resets its audio then and stops every
    /// source, and the game never starts its looping ones (ambience, machines, music) again. Opening a microphone is a common
    /// cause: a Bluetooth headset switches to its hands-free profile, and Windows swaps the output device.
    /// <para>
    /// Just before the voice chat opens or closes the microphone, <see cref="Snapshot"/> notes the loops that are playing;
    /// after a change, <see cref="RestoreLoops"/> starts those again. A change with no recent snapshot falls back to the
    /// loops that play on their own (<c>playOnAwake</c>). Both look through every source, which is fine for something that
    /// happens a few times a session at most.
    /// </para>
    /// </summary>
    internal sealed class WKTAudioDeviceWatcher {
        /// <summary>How long a snapshot is trusted to describe what was playing before a change.</summary>
        private const float c_snapshot_seconds = 30f;
        private readonly HashSet<AudioSource> m_playingLoops = new();
        private float m_snapshotTime = float.NegativeInfinity;

        /// <summary>Notes the looping sources playing now. Call right before opening or closing the microphone.</summary>
        public void Snapshot() {
            m_playingLoops.Clear();
            foreach (var source in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)) {
                if (source.loop && source.isPlaying && !IsVoice(source))
                    m_playingLoops.Add(source);
            }
            m_snapshotTime = Time.unscaledTime;
        }

        /// <summary>Starts the loops a device change stopped again. Returns how many.</summary>
        public int RestoreLoops() {
            var useSnapshot = Time.unscaledTime - m_snapshotTime < c_snapshot_seconds;
            var restarted = 0;
            foreach (var source in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)) {
                if (!source.loop || source.isPlaying || source.clip == null || !source.isActiveAndEnabled || IsVoice(source)) continue;
                if (useSnapshot ? !m_playingLoops.Contains(source) : !source.playOnAwake) continue;
                source.Play();
                restarted++;
            }
            m_playingLoops.Clear();
            m_snapshotTime = float.NegativeInfinity;
            return restarted;
        }

        /// <summary>Logs the state of the game's audio, so a report of missing sound comes with what the game was doing.</summary>
        public static void LogState(string reason, string microphone) {
            var config = AudioSettings.GetConfiguration();
            var mix = string.Empty;
            var manager = AudioManager.instance;
            var mixer = manager != null && manager.gameMixer != null ? manager.gameMixer.audioMixer : null;
            if (mixer != null && mixer.GetFloat("MasterVolume", out var master) && mixer.GetFloat("pitch", out var pitch))
                mix = $", game mix {master:0.#} dB at pitch {pitch:0.##}";
            WKT.Instance.Logger.LogInfo($"Audio ({reason}): {config.sampleRate} Hz {config.speakerMode}, buffer {config.dspBufferSize}, " +
                $"master {AudioListener.volume:0.##}{(AudioListener.pause ? " paused" : string.Empty)}, focused {Application.isFocused}{mix}, " +
                $"microphone '{microphone}'.");
        }

        private static bool IsVoice(AudioSource source) => source.TryGetComponent<WKTVoiceSource>(out _);
    }
}
