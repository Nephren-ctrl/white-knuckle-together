using System;
using UnityEngine;
using UnityEngine.Events;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Level scripting switched off in a synced co-op run, where it only works for one player: listeners of a level object's
    /// events, by the level, the object's path below it and the listener's method. Everything else the object does stays.
    /// <list type="bullet">
    /// <item>
    /// The Rotten Duct preview in <c>M3_Habitation_Shaft_Intro</c>: the first time a player reaches the computer room before
    /// the Teeth, the camera is held on the prison door for ten seconds while a light hints at the duct beyond. Its camera
    /// hold reaches every player, who stand frozen while the room's airlock door shuts them out of the safe room. The
    /// preview is skipped (the camera hold, its screen effect, its timer and sound, and the "seen it" save flag); the prison
    /// door's lever is still unlocked, which is the shortcut itself.
    /// </item>
    /// </list>
    /// </summary>
    internal static class WKTLevelTweaks {
        private static readonly Tweak[] s_tweaks = {
            new("M3_Habitation_Shaft_Intro", "Scripting/Body Cavity - Intro Event/Trigger - Check Event",
                new[] { "SetSaveFlag", "SetActive", "FadeTo", "Play" })
        };

        /// <summary>Switches off the listeners of <paramref name="level"/> the table names. Call once the level is keyed.</summary>
        public static void Apply(M_Level level) {
            foreach (var tweak in s_tweaks) {
                if (!level.name.StartsWith(tweak.Level, StringComparison.Ordinal)) continue;
                var target = level.transform.Find(tweak.Path);
                if (target == null) {
                    WKT.Instance.Logger.LogWarning($"Level {level.name} has no {tweak.Path} to change.");
                    continue;
                }
                var muted = 0;
                foreach (var behaviour in target.GetComponents<MonoBehaviour>()) {
                    if (behaviour == null) continue;
                    foreach (var field in WKTUnityEventFields.Get(behaviour.GetType())) {
                        if (field.GetValue(behaviour) is UnityEventBase unityEvent)
                            muted += Mute(unityEvent, tweak.Methods);
                    }
                }
                WKT.Instance.Logger.LogInfo($"Switched off {muted} listeners of {tweak.Path} in {level.name}.");
            }
        }

        /// <summary>Switches off the listeners of <paramref name="unityEvent"/> calling one of <paramref name="methods"/>. Returns how many.</summary>
        public static int Mute(UnityEventBase unityEvent, string[] methods) {
            var muted = 0;
            var count = unityEvent.GetPersistentEventCount();
            for (var i = 0; i < count; i++) {
                if (Array.IndexOf(methods, unityEvent.GetPersistentMethodName(i)) < 0) continue;
                unityEvent.SetPersistentListenerState(i, UnityEventCallState.Off);
                muted++;
            }
            return muted;
        }

        private readonly struct Tweak {
            public readonly string Level;
            public readonly string Path;
            public readonly string[] Methods;

            public Tweak(string level, string path, string[] methods) {
                Level = level;
                Path = path;
                Methods = methods;
            }
        }
    }
}
