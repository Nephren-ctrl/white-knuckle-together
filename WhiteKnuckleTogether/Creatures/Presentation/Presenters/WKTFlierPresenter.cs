using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A flying creature's buzz (<c>flyAudio</c>), which its own ticks fade out when it lands or dies and back in when it flies,
    /// follows the simulating machine's volume. For fliers steered by <c>DenizenMovement_BasicFlier</c>, the steering values its
    /// rotors (<c>UT_DenizenFlier_Rotors</c>, which run everywhere) tilt by come along too. Covers drones, bloodbugs and basic
    /// fliers such as moths.
    /// </summary>
    internal sealed class WKTFlierPresenter : WKTPuppetPresenterBase {
        private const float c_volume_rate = 3f;
        private static readonly AccessTools.FieldRef<DenizenMovement_BasicFlier, float> s_angleDifference =
            AccessTools.FieldRefAccess<DenizenMovement_BasicFlier, float>("angleDifference");
        private static readonly AccessTools.FieldRef<DenizenMovement_BasicFlier, Vector3> s_lastPidOutput =
            AccessTools.FieldRefAccess<DenizenMovement_BasicFlier, Vector3>("lastPIDOutput");
        private readonly FieldInfo? m_flyAudioField;
        private AudioSource? m_flyAudio;
        private float m_volume;
        private bool m_playing;
        private float m_angle;
        private Vector3 m_pid;
        private bool m_hasState;

        public override bool HasState => true;

        public WKTFlierPresenter(GameEntity entity) : base(entity) => m_flyAudioField = AccessTools.Field(entity.GetType(), "flyAudio");

        private AudioSource? FlyAudio {
            get {
                // Bloodbugs only find theirs when they start.
                if (m_flyAudio == null && m_flyAudioField != null)
                    m_flyAudio = m_flyAudioField.GetValue(Entity) as AudioSource;
                return m_flyAudio;
            }
        }

        private DenizenMovement_BasicFlier? Steering => (Entity as Denizen)?.GetMovementModule() as DenizenMovement_BasicFlier;

        public override void WriteState(NetDataWriter writer) {
            var audio = FlyAudio;
            writer.Put(audio != null ? audio.volume : 0f);
            writer.Put(audio != null && audio.isPlaying);
            var steering = Steering;
            writer.Put(steering != null ? s_angleDifference(steering) : 0f);
            var pid = steering != null ? s_lastPidOutput(steering) : Vector3.zero;
            writer.Put(pid.x);
            writer.Put(pid.y);
            writer.Put(pid.z);
        }

        public override bool ReadState(NetDataReader reader) {
            if (!reader.TryGetFloat(out var volume) || !reader.TryGetBool(out var playing) || !reader.TryGetFloat(out var angle) || !reader.TryGetFloat(out var x) ||
                !reader.TryGetFloat(out var y) || !reader.TryGetFloat(out var z)) return false;
            var pid = new Vector3(x, y, z);
            if (!float.IsFinite(volume) || !float.IsFinite(angle) || !float.IsFinite(pid.sqrMagnitude)) return false;
            m_volume = Mathf.Clamp01(volume);
            m_playing = playing;
            m_angle = angle;
            m_pid = pid;
            m_hasState = true;
            return true;
        }

        public override void PuppetUpdate(float deltaTime) {
            if (!m_hasState) return;
            var audio = FlyAudio;
            if (audio != null) {
                if (m_playing && !audio.isPlaying)
                    audio.Play();
                else if (!m_playing && audio.isPlaying)
                    audio.Stop();
                audio.volume = Mathf.Lerp(audio.volume, m_volume, 1f - Mathf.Exp(-c_volume_rate * deltaTime));
            }
            var steering = Steering;
            if (steering == null) return;
            s_angleDifference(steering) = m_angle;
            s_lastPidOutput(steering) = m_pid;
        }
    }
}
