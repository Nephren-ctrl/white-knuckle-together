using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WhiteKnuckleTogether {
    /// <summary>A reverb's settings, as both <c>AudioReverbZone</c> and <c>AudioReverbFilter</c> take them. Levels in millibels.</summary>
    internal struct WKTReverbSettings {
        public const float SILENT_LEVEL = -10000f;
        /// <summary>No reverb at all: what the game has where no zone reaches.</summary>
        public static readonly WKTReverbSettings Off = new() {
            Room = SILENT_LEVEL, RoomHF = SILENT_LEVEL, RoomLF = 0f, DecayTime = 1f, DecayHFRatio = 1f, Reflections = SILENT_LEVEL,
            ReflectionsDelay = 0f, Reverb = SILENT_LEVEL, ReverbDelay = 0f, HFReference = 5000f, LFReference = 250f, Diffusion = 100f,
            Density = 100f
        };
        public float Room;
        public float RoomHF;
        public float RoomLF;
        public float DecayTime;
        public float DecayHFRatio;
        public float Reflections;
        public float ReflectionsDelay;
        public float Reverb;
        public float ReverbDelay;
        public float HFReference;
        public float LFReference;
        public float Diffusion;
        public float Density;

        /// <summary>Whether this can be heard at all.</summary>
        public bool IsAudible => Room > SILENT_LEVEL + 100f;

        public static WKTReverbSettings Lerp(in WKTReverbSettings a, in WKTReverbSettings b, float t) => new() {
            Room = Mathf.Lerp(a.Room, b.Room, t), RoomHF = Mathf.Lerp(a.RoomHF, b.RoomHF, t), RoomLF = Mathf.Lerp(a.RoomLF, b.RoomLF, t),
            DecayTime = Mathf.Lerp(a.DecayTime, b.DecayTime, t), DecayHFRatio = Mathf.Lerp(a.DecayHFRatio, b.DecayHFRatio, t),
            Reflections = Mathf.Lerp(a.Reflections, b.Reflections, t), ReflectionsDelay = Mathf.Lerp(a.ReflectionsDelay, b.ReflectionsDelay, t),
            Reverb = Mathf.Lerp(a.Reverb, b.Reverb, t), ReverbDelay = Mathf.Lerp(a.ReverbDelay, b.ReverbDelay, t),
            HFReference = Mathf.Lerp(a.HFReference, b.HFReference, t), LFReference = Mathf.Lerp(a.LFReference, b.LFReference, t),
            Diffusion = Mathf.Lerp(a.Diffusion, b.Diffusion, t), Density = Mathf.Lerp(a.Density, b.Density, t)
        };

        public void Accumulate(in WKTReverbSettings other, float weight) {
            Room += other.Room * weight;
            RoomHF += other.RoomHF * weight;
            RoomLF += other.RoomLF * weight;
            DecayTime += other.DecayTime * weight;
            DecayHFRatio += other.DecayHFRatio * weight;
            Reflections += other.Reflections * weight;
            ReflectionsDelay += other.ReflectionsDelay * weight;
            Reverb += other.Reverb * weight;
            ReverbDelay += other.ReverbDelay * weight;
            HFReference += other.HFReference * weight;
            LFReference += other.LFReference * weight;
            Diffusion += other.Diffusion * weight;
            Density += other.Density * weight;
        }
    }

    /// <summary>
    /// The room reverb voices get. The game's reverb zones cannot take a voice: their send takes a source's clip before its
    /// filters, which for a voice is a constant carrier (see <see cref="WKTVoiceSource"/>). So the room the listener stands in
    /// is worked out here, from the same zones and as Unity blends them, and each voice plays it through its own reverb
    /// filter, after the voice is mixed in. Changes are eased in over <see cref="SMOOTHING_SECONDS"/>, so walking between rooms
    /// never jumps.
    /// </summary>
    internal sealed class WKTVoiceRoomReverb {
        /// <summary>How quickly the voices' reverb follows the listener's room.</summary>
        public const float SMOOTHING_SECONDS = 0.3f;
        /// <summary>How often the zones are looked for again, as levels load and unload.</summary>
        private const float c_refresh_interval = 2f;
        private readonly List<AudioReverbZone> m_zones = new();
        private readonly List<(WKTReverbSettings, float)> m_weighted = new();
        private float m_nextRefresh;
        private bool m_hasCurrent;

        /// <summary>The reverb voices get this frame.</summary>
        public WKTReverbSettings Current { get; private set; } = WKTReverbSettings.Off;

        /// <summary>Follows the room at <paramref name="listener"/>, or fades out the reverb when there is none.</summary>
        public void Update(Vector3? listener, float deltaTime) {
            var target = WKTReverbSettings.Off;
            if (listener != null) {
                RefreshZones();
                target = GetRoom(listener.Value);
            }
            if (!m_hasCurrent) {
                m_hasCurrent = true;
                Current = target;
                return;
            }
            Current = WKTReverbSettings.Lerp(Current, target, GetSmoothing(deltaTime));
        }

        /// <summary>How much of the way to the target the reverb moves this frame.</summary>
        public static float GetSmoothing(float deltaTime) => deltaTime <= 0f ? 0f : 1f - Mathf.Exp(-deltaTime / SMOOTHING_SECONDS);

        /// <summary>How much a zone with these distances applies at <paramref name="distance"/> from it: fully inside the near one, none past the far one.</summary>
        public static float GetWeight(float distance, float minDistance, float maxDistance) {
            if (distance <= minDistance) return 1f;
            if (distance >= maxDistance || maxDistance <= minDistance) return 0f;
            return 1f - (distance - minDistance) / (maxDistance - minDistance);
        }

        /// <summary>
        /// The zones' settings weighted by how much each applies, filled up to a whole with no reverb where they reach less.
        /// Where they overlap past a whole, they share it.
        /// </summary>
        public static WKTReverbSettings Blend(IReadOnlyList<(WKTReverbSettings Settings, float Weight)> zones) {
            var total = 0f;
            foreach (var zone in zones)
                total += Mathf.Max(0f, zone.Weight);
            if (total <= 0f) return WKTReverbSettings.Off;
            var scale = total > 1f ? 1f / total : 1f;
            var blended = default(WKTReverbSettings);
            foreach (var zone in zones)
                blended.Accumulate(zone.Settings, Mathf.Max(0f, zone.Weight) * scale);
            if (total < 1f)
                blended.Accumulate(WKTReverbSettings.Off, 1f - total);
            return blended;
        }

        private WKTReverbSettings GetRoom(Vector3 listener) {
            m_weighted.Clear();
            foreach (var zone in m_zones) {
                if (zone == null || !zone.isActiveAndEnabled) continue;
                var weight = GetWeight(Vector3.Distance(listener, zone.transform.position), zone.minDistance, zone.maxDistance);
                if (weight > 0f)
                    m_weighted.Add((Read(zone), weight));
            }
            return Blend(m_weighted);
        }

        private void RefreshZones() {
            var now = Time.unscaledTime;
            if (now < m_nextRefresh) return;
            m_nextRefresh = now + c_refresh_interval;
            m_zones.Clear();
            m_zones.AddRange(Object.FindObjectsByType<AudioReverbZone>(FindObjectsSortMode.None));
        }

        private static WKTReverbSettings Read(AudioReverbZone zone) => new() {
            Room = zone.room, RoomHF = zone.roomHF, RoomLF = zone.roomLF, DecayTime = zone.decayTime, DecayHFRatio = zone.decayHFRatio,
            Reflections = zone.reflections, ReflectionsDelay = zone.reflectionsDelay, Reverb = zone.reverb, ReverbDelay = zone.reverbDelay,
            HFReference = zone.HFReference, LFReference = zone.LFReference, Diffusion = zone.diffusion, Density = zone.density
        };

        /// <summary>Sets a voice's reverb filter to <paramref name="settings"/>, and switches it off when there is nothing to hear.</summary>
        public static void Apply(AudioReverbFilter filter, in WKTReverbSettings settings) {
            var audible = settings.IsAudible;
            if (filter.enabled != audible)
                filter.enabled = audible;
            if (!audible) return;
            filter.room = settings.Room;
            filter.roomHF = settings.RoomHF;
            filter.roomLF = settings.RoomLF;
            filter.decayTime = settings.DecayTime;
            filter.decayHFRatio = settings.DecayHFRatio;
            filter.reflectionsLevel = settings.Reflections;
            filter.reflectionsDelay = settings.ReflectionsDelay;
            filter.reverbLevel = settings.Reverb;
            filter.reverbDelay = settings.ReverbDelay;
            filter.hfReference = settings.HFReference;
            filter.lfReference = settings.LFReference;
            filter.diffusion = settings.Diffusion;
            filter.density = settings.Density;
        }
    }
}
