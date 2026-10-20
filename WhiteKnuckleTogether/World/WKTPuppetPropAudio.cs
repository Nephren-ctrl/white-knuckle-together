using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The drag sound (a chair rolling, a crate scraping) of a prop another machine simulates. The game starts it on any
    /// contact and only turns it down or pauses it in <c>CL_Prop.FixedUpdate</c>, which puppets skip, so on a puppet it went on
    /// forever at whatever volume it had. This runs just that part of the game's update, from how fast the synced prop
    /// actually moves: the sound follows its speed while it touches something and pauses once silent.
    /// </summary>
    internal static class WKTPuppetPropAudio {
        /// <summary>How fast the volume follows the speed, per second: slow enough to smooth over poses that arrive between steps.</summary>
        public const float FOLLOW_RATE = 12f;
        /// <summary>Below this the sound pauses, as the game's does once faded out.</summary>
        public const float SILENT_VOLUME = 0.005f;
        private static readonly AccessTools.FieldRef<CL_Prop, AudioSource?> s_dragAud = AccessTools.FieldRefAccess<CL_Prop, AudioSource?>("dragAud");
        private static readonly AccessTools.FieldRef<CL_Prop, bool> s_colliding = AccessTools.FieldRefAccess<CL_Prop, bool>("colliding");
        private static readonly AccessTools.FieldRef<CL_Prop, bool> s_isDragAudPlaying = AccessTools.FieldRefAccess<CL_Prop, bool>("isDragAudPlaying");
        private static readonly AccessTools.FieldRef<CL_Prop, Vector3> s_lastPos = AccessTools.FieldRefAccess<CL_Prop, Vector3>("lastPos");

        /// <summary>Call in place of the game's physics step for a puppet prop.</summary>
        public static void FixedUpdate(CL_Prop prop) {
            var audio = s_dragAud(prop);
            var position = prop.transform.position;
            var speed = (position - s_lastPos(prop)).magnitude / Time.fixedDeltaTime;
            s_lastPos(prop) = position;
            if (audio == null) return;
            var playing = audio.isPlaying;
            // The game's Update keeps this, and puppets skip it: stale, the contacts restart the sound every step.
            s_isDragAudPlaying(prop) = playing;
            if (!playing) return;
            var sounds = prop.dragSounds;
            var touching = s_colliding(prop) && !prop.stuck && sounds != null && sounds.Count > 0;
            var target = 0f;
            if (touching) {
                var effect = sounds![GetEffectIndex(sounds, speed)];
                target = GetDragVolume(prop.dragVolume, effect.maxVolume, effect.maxVelocity, speed);
                audio.pitch = Mathf.Lerp(effect.minPitch, effect.maxPitch, effect.maxVelocity > 0f ? speed / effect.maxVelocity : 0f);
            }
            audio.volume = Mathf.Lerp(audio.volume, target, Time.fixedDeltaTime * FOLLOW_RATE);
            if (audio.volume < SILENT_VOLUME) {
                audio.volume = 0f;
                audio.Pause();
                s_isDragAudPlaying(prop) = false;
            }
        }

        /// <summary>The drag sound for a speed, as the game picks it: the last whose minimum speed is passed, else the first.</summary>
        public static int GetEffectIndex(IReadOnlyList<CL_Prop.PropAudioEffect> effects, float speed) {
            var index = 0;
            for (var i = 0; i < effects.Count; i++) {
                if (effects[i] != null && speed > effects[i].minVelocity)
                    index = i;
            }
            return index;
        }

        /// <summary>The drag sound's volume at a speed, as the game sets it while the prop touches something.</summary>
        public static float GetDragVolume(float dragVolume, float maxVolume, float maxVelocity, float speed) =>
            maxVelocity > 0f && speed > 0f ? dragVolume * maxVolume * (speed / maxVelocity) : 0f;
    }
}
