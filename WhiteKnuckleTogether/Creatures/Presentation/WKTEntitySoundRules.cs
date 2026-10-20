using System;
using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The pure rules of entity sound replication: who plays a sound, and how a sound id ("group:set") names an audio set by
    /// its indices in a clip handler's lists. Kept apart from the game's runtime so it can be tested.
    /// </summary>
    internal static class WKTEntitySoundRules {
        /// <summary>
        /// The route of a sound an entity plays here. A sound from anything but a replicated world entity, or one played inside
        /// a local scope, stays local. A puppet's own sounds are muted, since its simulating machine sends them; the simulating
        /// machine plays and sends.
        /// </summary>
        public static WKTSoundRoute Decide(bool isReplicatedEntity, bool isPuppet, bool localScope) {
            if (!isReplicatedEntity || localScope) return WKTSoundRoute.Play;
            return isPuppet ? WKTSoundRoute.Mute : WKTSoundRoute.PlayAndSend;
        }

        /// <summary>
        /// Finds the audio set <paramref name="sound"/> ("group:set") names among <paramref name="groups"/>, splitting at the
        /// first colon as the handler does. Indices above <see cref="byte.MaxValue"/> are not found. Allocation-free.
        /// </summary>
        public static bool TryFind(IReadOnlyList<UT_AudioClipHandler.AudioGroup>? groups, string? sound, out byte group, out byte set) {
            group = 0;
            set = 0;
            if (groups == null || sound == null) return false;
            var colon = sound.IndexOf(':');
            if (colon <= 0 || colon >= sound.Length - 1) return false;
            var groupName = sound.AsSpan(0, colon);
            var setName = sound.AsSpan(colon + 1);
            var groupCount = Math.Min(groups.Count, byte.MaxValue + 1);
            for (var g = 0; g < groupCount; g++) {
                var audioGroup = groups[g];
                if (audioGroup?.name == null || audioGroup.audioSets == null || !groupName.SequenceEqual(audioGroup.name.AsSpan())) continue;
                var setCount = Math.Min(audioGroup.audioSets.Count, byte.MaxValue + 1);
                for (var s = 0; s < setCount; s++) {
                    var audioSet = audioGroup.audioSets[s];
                    if (audioSet?.name == null || !setName.SequenceEqual(audioSet.name.AsSpan())) continue;
                    group = (byte)g;
                    set = (byte)s;
                    return true;
                }
                return false;
            }
            return false;
        }

        /// <summary>The audio set at <paramref name="group"/> and <paramref name="set"/>, or null when out of range.</summary>
        public static UT_AudioClipHandler.AudioSet? Get(IReadOnlyList<UT_AudioClipHandler.AudioGroup>? groups, byte group, byte set) {
            if (groups == null || group >= groups.Count) return null;
            var sets = groups[group]?.audioSets;
            return sets == null || set >= sets.Count ? null : sets[set];
        }
    }
}
