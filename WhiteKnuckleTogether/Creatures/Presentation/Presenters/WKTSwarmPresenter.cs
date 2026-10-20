using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A swarm flier's members are separate hitboxes that its <c>FixedUpdate</c> keeps around it. On the puppet they follow it
    /// the same way, from the puppet's own speed, so they are shown and can be hit where the swarm is. Its buzz is a flier's
    /// (<see cref="WKTFlierPresenter"/>).
    /// </summary>
    internal sealed class WKTSwarmPresenter : WKTPuppetPresenterBase {
        private const float c_speed_smoothing = 8f;
        private static readonly FieldInfo? s_swarmObjects = AccessTools.Field(typeof(DEN_BasicDenizen_Flier_Swarm), "swarmObjects");
        private readonly DEN_BasicDenizen_Flier_Swarm m_swarm;
        private readonly WKTFlierPresenter m_flier;
        private Transform[]? m_hitboxes;
        private Vector3[] m_localPositions = System.Array.Empty<Vector3>();
        private Vector3[] m_velocities = System.Array.Empty<Vector3>();
        private float[] m_expands = System.Array.Empty<float>();
        private Vector3 m_lastPosition;
        private Vector3 m_velocity;
        private bool m_hasPosition;

        public override bool HasState => true;

        public WKTSwarmPresenter(GameEntity entity) : base(entity) {
            m_swarm = (DEN_BasicDenizen_Flier_Swarm)entity;
            m_flier = new WKTFlierPresenter(entity);
        }

        public override void WriteState(NetDataWriter writer) => m_flier.WriteState(writer);

        public override bool ReadState(NetDataReader reader) => m_flier.ReadState(reader);

        public override void PuppetUpdate(float deltaTime) => m_flier.PuppetUpdate(deltaTime);

        /// <summary>The members' part of <c>DEN_BasicDenizen_Flier_Swarm.FixedUpdate</c>, facing the way the swarm moves.</summary>
        public override void PuppetFixedUpdate(float fixedDeltaTime) {
            if (m_hitboxes == null && !TryFindMembers()) return;
            var swarm = m_swarm;
            var position = swarm.transform.position;
            if (m_hasPosition && fixedDeltaTime > 0f)
                m_velocity = Vector3.Lerp(m_velocity, (position - m_lastPosition) / fixedDeltaTime, 1f - Mathf.Exp(-c_speed_smoothing * fixedDeltaTime));
            m_lastPosition = position;
            m_hasPosition = true;
            var time = Time.time;
            var deltaTime = Time.deltaTime;
            var speed = m_velocity.magnitude;
            for (var i = 0; i < m_hitboxes!.Length; i++) {
                var hitbox = m_hitboxes[i];
                if (hitbox == null) continue;
                var noise = new Vector3(Mathf.PerlinNoise(time * swarm.swarmNoiseFrequency + i * swarm.swarmNoiseFrequencyOffset + 10.026f, 0f) - 0.5f,
                    Mathf.PerlinNoise(time * swarm.swarmNoiseFrequency + i * swarm.swarmNoiseFrequencyOffset + 600f + 10.026f, 0f) - 0.5f,
                    Mathf.PerlinNoise(time * swarm.swarmNoiseFrequency + i * swarm.swarmNoiseFrequencyOffset + 1500f + 10.026f, 0f) - 0.5f) * swarm.swarmNoiseIntensity;
                // Each member's follow amount is its index, as the swarm sets it when it starts.
                var follow = swarm.transform.InverseTransformVector(m_velocity * i * swarm.swarmFollowRate) + noise;
                m_velocities[i] = Vector3.Lerp(m_velocities[i], follow, deltaTime);
                m_expands[i] = Mathf.Lerp(m_expands[i], speed * 0.1f + 1f + Mathf.Sin(time + i * 0.2f) * 0.25f, deltaTime);
                var target = swarm.transform.TransformPoint(m_localPositions[i] * m_expands[i] / swarm.swarmTightness - m_velocities[i]);
                hitbox.position = Vector3.Lerp(hitbox.position, target, deltaTime * (i + 1f) * 5f);
                if (speed > 0.8f)
                    hitbox.rotation = Quaternion.Lerp(hitbox.rotation, Quaternion.LookRotation(m_velocity), deltaTime * (i + 1) * 0.5f);
            }
        }

        /// <summary>Reads the members the swarm made when it started. Reflection, once per swarm.</summary>
        private bool TryFindMembers() {
            if (s_swarmObjects?.GetValue(m_swarm) is not IList { Count: > 0 } members) return false;
            var memberType = members[0]?.GetType();
            var hitboxField = memberType != null ? AccessTools.Field(memberType, "hitbox") : null;
            var localPositionField = memberType != null ? AccessTools.Field(memberType, "localPosition") : null;
            if (hitboxField == null || localPositionField == null) return false;
            var hitboxes = new Transform[members.Count];
            m_localPositions = new Vector3[members.Count];
            m_velocities = new Vector3[members.Count];
            m_expands = new float[members.Count];
            for (var i = 0; i < members.Count; i++) {
                if (members[i] is not { } member) continue;
                if (hitboxField.GetValue(member) is Component hitbox)
                    hitboxes[i] = hitbox.transform;
                if (localPositionField.GetValue(member) is Vector3 localPosition)
                    m_localPositions[i] = localPosition;
            }
            m_hitboxes = hitboxes;
            return true;
        }
    }
}
