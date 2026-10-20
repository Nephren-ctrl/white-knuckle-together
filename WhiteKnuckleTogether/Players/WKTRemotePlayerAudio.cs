using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The sounds of one remote player's body, heard from where it is shown. Clips and their volume and pitch come from the
    /// local player's own sounds, replaced by the remote player's voice cosmetics and perks (Roach Mode's roach voice) like
    /// the game replaces the local player's.
    /// Only audible: nothing reaches the game's sound listeners, so enemies never react to it.
    /// </summary>
    internal sealed class WKTRemotePlayerAudio {
        /// <summary>How fast the fatigue loop follows its target volume, as the game fades the local player's.</summary>
        private const float c_fatigue_fade_rate = 3f;
        /// <summary>Below this, the fatigue loop is inaudible and stops while it is not needed.</summary>
        private const float c_silent_volume = 0.001f;
        private const float c_spatial = 1f;
        /// <summary>The game's sound sources copy their falloff from this private template.</summary>
        private static readonly AccessTools.FieldRef<AudioSource?> s_defaultAudio =
            AccessTools.StaticFieldRefAccess<AudioSource?>(AccessTools.Field(typeof(AudioManager), "defaultAudio"));
        /// <summary>Voice cosmetics replace footsteps by material name. Indexed by material, null in gaps.</summary>
        private static readonly string?[] s_materialNames = CreateMaterialNames();
        private readonly List<AudioClipHandlerOverride> m_overrides = new();
        private readonly UT_AudioClipHandler.AudioSet?[] m_sets = new UT_AudioClipHandler.AudioSet?[(int)WKTPlayerSound.MAX_ID + 1];
        /// <summary>The set a grab of each handhold material plays, and its name for voice replacements. Indexed by material.</summary>
        private readonly UT_AudioClipHandler.AudioSet?[] m_grabSets = new UT_AudioClipHandler.AudioSet?[s_materialNames.Length];
        private readonly string[] m_grabSetNames = new string[s_materialNames.Length];
        private readonly Transform m_transform;
        private readonly ENT_Player m_localPlayer;
        private readonly AudioSource? m_fatigue;
        private readonly float m_fatigueMaxVolume;
        private string? m_voices;
        private IReadOnlyList<string> m_perkVoices = Array.Empty<string>();
        /// <summary>Number of the last sound played. Negative until the first pose, whose sounds are from before it was seen.</summary>
        private int m_lastSound = -1;

        /// <param name="localPlayer">Its sounds are the clips this plays. Must outlive this.</param>
        public WKTRemotePlayerAudio(GameObject body, ENT_Player localPlayer) {
            m_transform = body.transform;
            m_localPlayer = localPlayer;
            var handler = localPlayer.clipHandler;
            WKTPlayerSoundSets.Resolve(handler, m_sets);
            ResolveGrabSets(handler);
            var fatigueSet = handler == null ? null : handler.GetGroup(WKTPlayerSoundSets.FATIGUE_GROUP)?.GetSet(WKTPlayerSoundSets.FATIGUE_LEVEL_SET);
            if (fatigueSet?.clips == null || fatigueSet.clips.Count == 0) return;
            m_fatigueMaxVolume = fatigueSet.maxVolume;
            // The game starts the local player's loop before voices apply, so voices never replace it.
            m_fatigue = CreateLoop(body, fatigueSet.clips[0], fatigueSet.reverbZoneMix);
        }

        /// <param name="memberData">The remote player's <see cref="WKTLobbyConsts.KEY_VOICES"/>.</param>
        public void SetVoices(string? memberData) {
            m_voices = memberData;
            WKTVoiceCosmetics.Resolve(m_localPlayer, m_voices, m_perkVoices, m_overrides);
        }

        /// <param name="perkIds">The remote player's perks that replace their sounds (see <see cref="WKTPlayerBodyState.VoicePerks"/>). Kept, never changed.</param>
        public void SetPerkVoices(IReadOnlyList<string> perkIds) {
            m_perkVoices = perkIds;
            WKTVoiceCosmetics.Resolve(m_localPlayer, m_voices, m_perkVoices, m_overrides);
        }

        /// <summary>Plays the sounds that are new in <paramref name="pose"/> and follows its grip with the fatigue loop.</summary>
        public void Apply(in WKTPlayerPose pose, float deltaTime) {
            var sounds = pose.Sounds;
            for (var age = sounds.CountSince(m_lastSound) - 1; age >= 0; age--)
                Play(sounds.Get(age));
            m_lastSound = sounds.Newest;
            UpdateFatigue(pose, deltaTime);
        }

        private void Play(in WKTPlayerSound sound) {
            switch (sound.Id) {
                case WKTPlayerSoundId.Footstep:
                    PlayFootstep(sound.Material);
                    return;
                case WKTPlayerSoundId.LandMaterial when TryPlayMaterialLanding(sound.Material):
                    return;
                case WKTPlayerSoundId.Grab:
                    PlayGrab(sound.Material);
                    return;
            }
            var set = m_sets[(int)sound.Id];
            if (set == null || !WKTPlayerSoundSets.TryGetNames(sound.Id, out var group, out var setName)) return;
            var clip = GetOverrideClip(group, setName);
            if (clip == null)
                clip = set.GetClip();
            PlayOneShot(clip, Random.Range(set.minVolume, set.maxVolume), Random.Range(set.minPitch, set.maxPitch), set.reverbZoneMix);
        }

        /// <summary>Like the game: the material's own clip, with the heavy landing set's volume and pitch. Never replaced by voices.</summary>
        private bool TryPlayMaterialLanding(byte material) {
            var footstep = GetFootstep(material);
            var set = m_sets[(int)WKTPlayerSoundId.LandMaterial];
            if (footstep == null || set == null || !footstep.useLandingAudio || footstep.landingAudio == null ||
                footstep.landingAudio.Count == 0)
                return false;
            var clip = footstep.landingAudio[Random.Range(0, footstep.landingAudio.Count)];
            var volume = Random.Range(set.minVolume, set.maxVolume) * footstep.landingAudioVolume;
            PlayOneShot(clip, volume, Random.Range(set.minPitch, set.maxPitch), set.reverbZoneMix);
            return true;
        }

        /// <summary>Like the game: the material's own grab set if there is one, else the standard grab, either replaced by voices.</summary>
        private void PlayGrab(byte material) {
            var index = material < m_grabSets.Length && m_grabSets[material] != null ? material : 0;
            var set = m_grabSets[index];
            if (set == null) return;
            var clip = GetOverrideClip(WKTPlayerSoundSets.CLIMBING_GROUP, m_grabSetNames[index]);
            if (clip == null)
                clip = set.GetClip();
            PlayOneShot(clip, Random.Range(set.minVolume, set.maxVolume), Random.Range(set.minPitch, set.maxPitch), set.reverbZoneMix);
        }

        private void ResolveGrabSets(UT_AudioClipHandler? handler) {
            var climbing = handler == null ? null : handler.GetGroup(WKTPlayerSoundSets.CLIMBING_GROUP);
            for (var material = 0; material < m_grabSets.Length; material++) {
                var name = s_materialNames[material];
                m_grabSetNames[material] = handler == null || name == null ? WKTPlayerSoundSets.GRAB_SET : WKTPlayerSoundSets.GetGrabSetName(handler, name.ToLowerInvariant());
                m_grabSets[material] = climbing?.GetSet(m_grabSetNames[material]);
            }
        }

        private void PlayFootstep(byte material) {
            var footstep = GetFootstep(material);
            if (footstep == null) return;
            var name = material < s_materialNames.Length ? s_materialNames[material] : null;
            var clip = name == null ? null : GetOverrideClip(WKTPlayerSoundSets.FOOTSTEP_GROUP, name);
            if (clip == null && footstep.audio != null && footstep.audio.Count > 0)
                clip = footstep.audio[Random.Range(0, footstep.audio.Count)];
            PlayOneShot(clip, footstep.volume, Random.Range(footstep.minPitch, footstep.maxPitch), 1f);
        }

        /// <summary>Follows the body until it ends. The game's own falloff makes it fade with distance.</summary>
        private void PlayOneShot(AudioClip? clip, float volume, float pitch, float reverbMix) {
            if (clip != null)
                AudioManager.PlaySound(clip, m_transform, volume, pitch, c_spatial, false, reverbMix);
        }

        /// <summary>The game's formula: silent above half grip, full at none, while either hand hangs.</summary>
        private void UpdateFatigue(in WKTPlayerPose pose, float deltaTime) {
            if (m_fatigue == null) return;
            var target = 0f;
            if (pose.Left.IsHanging || pose.Right.IsHanging) {
                var lowest = Mathf.Min(pose.Left.StaminaPercent, pose.Right.StaminaPercent);
                target = Mathf.Max((0.5f - lowest) * 2f * m_fatigueMaxVolume, 0f);
            }
            var volume = Mathf.Lerp(m_fatigue.volume, target, deltaTime * c_fatigue_fade_rate);
            m_fatigue.volume = volume;
            if (volume > c_silent_volume) {
                if (!m_fatigue.isPlaying)
                    m_fatigue.Play();
            } else if (target <= 0f && m_fatigue.isPlaying) {
                m_fatigue.Stop();
            }
        }

        /// <summary>The game's first replacement for a set wins, even when it has no clips.</summary>
        private AudioClip? GetOverrideClip(string group, string set) {
            foreach (var handlerOverride in m_overrides) {
                if (handlerOverride == null || handlerOverride.setOverrides == null) continue;
                foreach (var setOverride in handlerOverride.setOverrides)
                    if (setOverride.groupName == group && setOverride.setName == set) return setOverride.GetClip();
            }
            return null;
        }

        /// <summary>Like the game: the footsteps of the material, or the first ones when it has none.</summary>
        private ENT_Player.Footsteps? GetFootstep(byte material) {
            var footsteps = m_localPlayer.footsteps;
            if (footsteps == null || footsteps.Count == 0) return null;
            foreach (var footstep in footsteps)
                if ((int)footstep.material == material) return footstep;
            return footsteps[0];
        }

        private static AudioSource CreateLoop(GameObject body, AudioClip clip, float reverbMix) {
            var source = body.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.volume = 0f;
            source.spatialBlend = c_spatial;
            source.reverbZoneMix = reverbMix;
            if (AudioManager.instance != null)
                source.outputAudioMixerGroup = AudioManager.instance.gameMixer;
            var template = s_defaultAudio();
            if (template == null) return source;
            source.rolloffMode = template.rolloffMode;
            source.minDistance = template.minDistance;
            source.maxDistance = template.maxDistance;
            source.spread = template.spread;
            source.dopplerLevel = template.dopplerLevel;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, template.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
            return source;
        }

        private static string?[] CreateMaterialNames() {
            var materials = (ObjectMaterial.MaterialType[])Enum.GetValues(typeof(ObjectMaterial.MaterialType));
            var max = 0;
            foreach (var material in materials)
                max = Math.Max(max, (int)material);
            var names = new string?[Math.Min(max, byte.MaxValue) + 1];
            foreach (var material in materials) {
                if ((int)material >= 0 && (int)material < names.Length)
                    names[(int)material] = material.ToString();
            }
            return names;
        }
    }
}
