using HarmonyLib;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Numbers the sounds the local player's body makes during a multiplayer run, as the game plays them, so they travel
    /// with the player's state (see <see cref="WKTPlayerSounds"/>). The patches in <c>PlayerSounds_Patch</c> report every
    /// sound to <see cref="Active"/>, and only the local player's are kept. Nothing is active outside a multiplayer run.
    /// </summary>
    internal sealed class WKTPlayerSoundRecorder {
        /// <summary>
        /// The game plays the jump boost prime every physics step of a crouched coyote time, restarting it on one channel, so
        /// its player hears it once. Repeats this soon after are left out, so they never push other sounds out of the pose.
        /// </summary>
        private const float c_prime_repeat_seconds = 0.25f;
        private static readonly AccessTools.FieldRef<ENT_Player, ENT_Player.Footsteps?> s_currentFootstep =
            AccessTools.FieldRefAccess<ENT_Player, ENT_Player.Footsteps?>("curFootstep");
        private readonly UT_AudioClipHandler.AudioSet?[] m_sets = new UT_AudioClipHandler.AudioSet?[(int)WKTPlayerSound.MAX_ID + 1];
        private float m_lastPrime = float.NegativeInfinity;

        public static WKTPlayerSoundRecorder? Active { get; private set; }
        public ENT_Player Player { get; }
        public WKTPlayerSounds Sounds { get; private set; }

        private WKTPlayerSoundRecorder(ENT_Player player) {
            Player = player;
            WKTPlayerSoundSets.Resolve(player.clipHandler, m_sets);
        }

        /// <summary>Records <paramref name="player"/>'s sounds from now on, numbered from zero.</summary>
        public static void Start(ENT_Player player) => Active = new WKTPlayerSoundRecorder(player);

        public static void Stop() => Active = null;

        /// <summary>A set played one of its own clips.</summary>
        public void OnSetPlayed(UT_AudioClipHandler.AudioSet set) {
            for (var id = 0; id < m_sets.Length; id++) {
                // Shares the heavy landing set, but only ever plays a clip of its own (see OnSetPlayedClip).
                if (id == (int)WKTPlayerSoundId.LandMaterial || !ReferenceEquals(m_sets[id], set)) continue;
                if (id == (int)WKTPlayerSoundId.JumpBoostPrime) {
                    if (Time.time - m_lastPrime < c_prime_repeat_seconds) return;
                    m_lastPrime = Time.time;
                }
                Record(new WKTPlayerSound((WKTPlayerSoundId)id));
                return;
            }
        }

        /// <summary>A set played a clip it was given.</summary>
        public void OnSetPlayedClip(UT_AudioClipHandler.AudioSet set) {
            if (!ReferenceEquals(m_sets[(int)WKTPlayerSoundId.LandHeavy], set)) return;
            Record(new WKTPlayerSound(WKTPlayerSoundId.LandMaterial, GetMaterial(s_currentFootstep(Player))));
        }

        public void OnFootstep(ENT_Player player, ENT_Player.Footsteps? footstep) {
            if (player == Player)
                Record(new WKTPlayerSound(WKTPlayerSoundId.Footstep, GetMaterial(footstep)));
        }

        /// <summary>The player took hold of a handhold made of <paramref name="material"/> and heard its grab.</summary>
        public void OnHandholdSound(ENT_Player player, string? material) {
            if (player != Player) return;
            Record(new WKTPlayerSound(WKTPlayerSoundId.Grab, WKTPlayerSoundSets.GetHandholdMaterial(material)));
        }

        private static byte GetMaterial(ENT_Player.Footsteps? footstep) => footstep == null ? (byte)0 : (byte)footstep.material;

        private void Record(in WKTPlayerSound sound) => Sounds = Sounds.Push(sound);
    }
}
