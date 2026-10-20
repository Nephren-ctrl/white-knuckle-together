using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The Rat King's sparks while it rolls fast, which its <c>Movement</c> switches by speed, follow the puppet's own speed.
    /// Its collisions shake the camera and, when hard, throw out slam particles, as its collision handler does on the
    /// simulating machine. Its spin-up and slam sounds and events arrive as sounds and events.
    /// </summary>
    internal sealed class WKTRatKingPresenter : WKTPuppetPresenterBase {
        /// <summary><c>DEN_RatKing.Movement</c>'s speed for sparks.</summary>
        private const float c_spark_speed = 1f;
        private const float c_collision_shake_distance = 30f;
        /// <summary>How quickly the measured speed follows the puppet, so interpolation steps do not flicker the sparks.</summary>
        private const float c_speed_smoothing = 8f;
        private readonly DEN_RatKing m_ratKing;
        private WKTSlamReplay m_slams;
        private Vector3 m_lastPosition;
        private float m_speed;
        private bool m_hasPosition;

        public override bool HasState => true;

        public WKTRatKingPresenter(GameEntity entity) : base(entity) => m_ratKing = (DEN_RatKing)entity;

        /// <summary>Simulating machine: the Rat King hit something with this impulse, as its own collision handler judged.</summary>
        public void OnCollision(float impulse) => m_slams.Record(m_ratKing.collideShakeAmount * impulse, impulse > m_ratKing.minimumSlamVelocity);

        public override void WriteState(NetDataWriter writer) => m_slams.Write(writer);

        public override bool ReadState(NetDataReader reader) => m_slams.Read(reader);

        public override void PuppetUpdate(float deltaTime) {
            var position = m_ratKing.transform.position;
            if (m_hasPosition && deltaTime > 0f)
                m_speed = Mathf.Lerp(m_speed, Vector3.Distance(position, m_lastPosition) / deltaTime, 1f - Mathf.Exp(-c_speed_smoothing * deltaTime));
            m_lastPosition = position;
            m_hasPosition = true;
            var sparks = m_ratKing.particleSparks;
            if (sparks != null) {
                if (m_speed > c_spark_speed) {
                    if (!sparks.isPlaying)
                        sparks.Play();
                }
                else if (sparks.isPlaying)
                    sparks.Stop();
            }
            if (!m_slams.TakeDue()) return;
            CL_CameraControl.ShakeAtPosition(position, m_slams.Strength, c_collision_shake_distance);
            if (m_slams.Flag && m_ratKing.particleSlam != null)
                m_ratKing.particleSlam.Play();
        }
    }
}
