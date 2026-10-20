using System;
using HarmonyLib;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The Hunter's tendrils and hurt colouring, which its own <c>Update</c> animates (<c>AnimateTendrils</c>,
    /// <c>AnimateMaterials</c>, by its replicated health), run on the puppet too. Its eyes turn red while it is frozen, and its
    /// wall slams shake the camera, as on the simulating machine.
    /// </summary>
    internal sealed class WKTHunterPresenter : WKTPuppetPresenterBase {
        private const float c_slam_shake_distance = 15f;
        private static readonly Action<DEN_Hunter> s_animateTendrils = AccessTools.MethodDelegate<Action<DEN_Hunter>>(AccessTools.Method(typeof(DEN_Hunter), "AnimateTendrils"));
        private static readonly Action<DEN_Hunter> s_animateMaterials = AccessTools.MethodDelegate<Action<DEN_Hunter>>(AccessTools.Method(typeof(DEN_Hunter), "AnimateMaterials"));
        private static readonly AccessTools.FieldRef<DEN_Hunter, float> s_freezeTime = AccessTools.FieldRefAccess<DEN_Hunter, float>("freezeTime");
        private readonly DEN_Hunter m_hunter;
        private WKTSlamReplay m_slams;
        private bool m_frozen;
        private bool m_shownFrozen;

        public override bool HasState => true;

        public WKTHunterPresenter(GameEntity entity) : base(entity) => m_hunter = (DEN_Hunter)entity;

        /// <summary>Simulating machine: the Hunter slammed into a wall as hard as <paramref name="speed"/>, as its own collision handler judged.</summary>
        public void OnSlam(float speed) => m_slams.Record(m_hunter.slamShakeMult * speed, false);

        public override void WriteState(NetDataWriter writer) {
            writer.Put(s_freezeTime(m_hunter) > 0f);
            m_slams.Write(writer);
        }

        public override bool ReadState(NetDataReader reader) => reader.TryGetBool(out m_frozen) && m_slams.Read(reader);

        public override void PuppetUpdate(float deltaTime) {
            s_animateTendrils(m_hunter);
            s_animateMaterials(m_hunter);
            if (m_shownFrozen != m_frozen && m_hunter.eyeParticles != null) {
                m_shownFrozen = m_frozen;
                var main = m_hunter.eyeParticles.main;
                main.startColor = m_frozen ? Color.red : Color.white;
            }
            if (m_slams.TakeDue())
                CL_CameraControl.ShakeAtPosition(m_hunter.transform.position, m_slams.Strength, c_slam_shake_distance);
        }
    }
}
