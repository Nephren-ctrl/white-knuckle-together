using System.Collections.Generic;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using SendType = Steamworks.Data.SendType;

namespace WhiteKnuckleTogether {
    /// <summary>What an item effect message plays.</summary>
    public enum WKTItemEffectKind : byte {
        Sound,
        Particle,
        Tracer
    }

    /// <summary>
    /// The sounds and particles of the local player's items, shown to every other player where they happened: a gun's shot
    /// and muzzle flash, eating, an injector, a grub's squelch, an artifact's hum. The game plays them from the item in the
    /// local player's hand (its own code, or its clip handler from an animation event); while that runs, what it plays
    /// through <c>AudioManager</c> and <c>FXManager</c>, or its clip handler's own audio sources, is recorded and sent through
    /// the host. Others play the sounds in 3D at that spot, even ones the game plays flat for the player holding the item.
    /// Owned by <see cref="WKTPlayerSync"/>.
    /// </summary>
    internal sealed class WKTItemEffects {
        public const int MAX_NAME_LENGTH = 96;
        private const float c_clip_index_interval = 10f;
        private static readonly Dictionary<string, AudioClip> s_clips = new();
        private static readonly HashSet<string> s_missingClips = new();
        private static float s_nextClipIndex;
        private readonly WKTNetwork m_network;

        public WKTItemEffects(WKTNetwork network) => m_network = network;

        public static WKTItemEffects? Instance { get; set; }
        /// <summary>Open while the local player's item plays its effects, which are then sent.</summary>
        public static int Depth { get; private set; }
        public static bool IsRecording => Depth > 0 && Instance != null;

        public static void Enter() => Depth++;

        public static void Exit() {
            if (Depth > 0)
                Depth--;
        }

        public static void Reset() => Depth = 0;

        /// <summary>Stops recording for a while, such as while an enemy hit by the item plays its own (already shared) sound. Returns what <see cref="Resume"/> restores.</summary>
        public static int Suspend() {
            var depth = Depth;
            Depth = 0;
            return depth;
        }

        public static void Resume(int depth) => Depth = depth;

        public void OnSound(AudioClip? clip, Vector3 position, float volume, float pitch) {
            if (clip == null || string.IsNullOrEmpty(clip.name) || clip.name.Length > MAX_NAME_LENGTH) return;
            Send(WKTItemEffectKind.Sound, clip.name, position, Vector3.zero, Mathf.Clamp(volume, 0f, 4f), Mathf.Clamp(pitch, 0.05f, 4f), 0);
        }

        public void OnParticle(string? id, Vector3 position, int amount) {
            if (string.IsNullOrEmpty(id) || id!.Length > MAX_NAME_LENGTH) return;
            Send(WKTItemEffectKind.Particle, id, position, Vector3.zero, 0f, 0f, Mathf.Clamp(amount, 0, 500));
        }

        public void OnTracer(string? id, Vector3 start, Vector3 end, float speed) {
            if (string.IsNullOrEmpty(id) || id!.Length > MAX_NAME_LENGTH) return;
            Send(WKTItemEffectKind.Tracer, id, start, end, Mathf.Clamp(speed, 0f, 10000f), 0f, 0);
        }

        private void Send(WKTItemEffectKind kind, string name, Vector3 position, Vector3 end, float a, float b, int amount) {
            var offset = WKTWorldOffset.Get();
            var writer = m_network.BeginMessage(WKTMessageId.ItemEffect);
            writer.Put((byte)kind);
            writer.Put(name, MAX_NAME_LENGTH);
            WKTWire.Put(writer, WKTWorldOffset.ToWire(position, offset));
            WKTWire.Put(writer, WKTWorldOffset.ToWire(end, offset));
            writer.Put(a);
            writer.Put(b);
            writer.Put((ushort)amount);
            if (m_network.Mode == NetworkMode.Host)
                m_network.Broadcast(writer, SendType.Unreliable);
            else
                m_network.SendToHost(writer, SendType.Unreliable);
        }

        public void OnMessage(WKTPlayer sender, NetDataReader reader) {
            if (!reader.TryGetByte(out var kindByte) || kindByte > (byte)WKTItemEffectKind.Tracer ||
                !WKTReaderUtility.TryGetBoundedString(reader, MAX_NAME_LENGTH, out var name) || string.IsNullOrEmpty(name) ||
                !WKTWire.TryGetPosition(reader, out var position) || !WKTWire.TryGetPosition(reader, out var end) || !reader.TryGetFloat(out var a) ||
                !reader.TryGetFloat(out var b) || !reader.TryGetUShort(out var amount) || float.IsNaN(a) || float.IsNaN(b)) return;
            if (m_network.Mode == NetworkMode.Host) {
                if (sender.IsHost) return;
                var relay = m_network.BeginMessage(WKTMessageId.ItemEffect);
                relay.Put(kindByte);
                relay.Put(name, MAX_NAME_LENGTH);
                WKTWire.Put(relay, position);
                WKTWire.Put(relay, end);
                relay.Put(a);
                relay.Put(b);
                relay.Put(amount);
                m_network.Broadcast(relay, SendType.Unreliable, sender);
            }
            else if (!sender.IsHost) return;
            var offset = WKTWorldOffset.Get();
            var at = WKTWorldOffset.FromWire(position, offset);
            try {
                switch ((WKTItemEffectKind)kindByte) {
                    case WKTItemEffectKind.Sound when FindClip(name) is { } clip:
                        AudioManager.PlaySound(clip, at, Mathf.Clamp(a, 0f, 4f), Mathf.Clamp(b, 0.05f, 4f));
                        break;
                    case WKTItemEffectKind.Particle:
                        FXManager.PlayParticle(name, at, amount);
                        break;
                    case WKTItemEffectKind.Tracer:
                        FXManager.PlayTracer(name, at, WKTWorldOffset.FromWire(end, offset), a);
                        break;
                }
            }
            catch (System.Exception exception) {
                WKT.Instance.Logger.LogWarning($"Could not play another player's item effect {name}: {exception.Message}");
            }
        }

        /// <summary>A loaded audio clip by name, looked up again at most every few seconds for names not found yet.</summary>
        private static AudioClip? FindClip(string name) {
            if (s_clips.TryGetValue(name, out var clip) && clip != null) return clip;
            if (Time.unscaledTime < s_nextClipIndex && s_missingClips.Contains(name)) return null;
            s_nextClipIndex = Time.unscaledTime + c_clip_index_interval;
            s_missingClips.Clear();
            foreach (var loaded in Resources.FindObjectsOfTypeAll<AudioClip>()) {
                if (loaded != null && !string.IsNullOrEmpty(loaded.name))
                    s_clips[loaded.name] = loaded;
            }
            if (s_clips.TryGetValue(name, out clip)) return clip;
            s_missingClips.Add(name);
            return null;
        }
    }
}
