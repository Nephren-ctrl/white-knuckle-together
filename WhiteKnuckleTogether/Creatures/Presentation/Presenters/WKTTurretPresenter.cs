using System;
using HarmonyLib;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A turret's gun, which its <c>FixedUpdate</c> turns toward its target, scans with when idle and sprays with once knocked
    /// over, follows the simulating machine's. So do whether it is switched on and whether it is dead, which it can become
    /// without <c>Kill</c> (knocked over). Each shot's tracer, flash and hit particles (<c>PlayTracer</c>) replay from the
    /// puppet's barrel to where the shot ended.
    /// </summary>
    internal sealed class WKTTurretPresenter : WKTPuppetPresenterBase {
        private const float c_follow_rate = 15f;
        private const byte c_active = 1;
        private const byte c_dead = 2;
        private const byte c_hit_particle = 4;
        private static readonly Action<DEN_Turret, Vector3, Vector3, float, bool> s_playTracer =
            AccessTools.MethodDelegate<Action<DEN_Turret, Vector3, Vector3, float, bool>>(AccessTools.Method(typeof(DEN_Turret), "PlayTracer"));
        private readonly DEN_Turret m_turret;
        private Quaternion m_gun = Quaternion.identity;
        private bool m_hasState;
        private byte m_shots;
        private Vector3 m_shotEnd;
        private bool m_shotHit;
        private byte m_shownShots;
        private bool m_shotsStarted;
        private bool m_replaying;

        public override bool HasState => true;

        public WKTTurretPresenter(GameEntity entity) : base(entity) => m_turret = (DEN_Turret)entity;

        /// <summary>Simulating machine: a shot's tracer was played toward <paramref name="end"/>.</summary>
        public void OnTracer(Vector3 end, bool playHitParticle) {
            if (m_replaying) return;
            m_shots++;
            m_shotEnd = end;
            m_shotHit = playHitParticle;
        }

        public override void WriteState(NetDataWriter writer) {
            WKTWire.Put(writer, m_turret.turretGun != null ? m_turret.turretGun.localRotation : Quaternion.identity);
            byte flags = 0;
            if (m_turret.active)
                flags |= c_active;
            if (m_turret.dead)
                flags |= c_dead;
            if (m_shotHit)
                flags |= c_hit_particle;
            writer.Put(flags);
            writer.Put(m_shots);
            WKTWire.Put(writer, WKTWorldOffset.ToWire(m_shotEnd, WKTWorldOffset.Get()));
        }

        public override bool ReadState(NetDataReader reader) {
            if (!WKTWire.TryGetRotation(reader, out var gun) || !reader.TryGetByte(out var flags) || !reader.TryGetByte(out var shots) ||
                !WKTWire.TryGetPosition(reader, out var end)) return false;
            m_gun = gun;
            m_turret.active = (flags & c_active) != 0;
            // Knocked over, a turret dies without Kill: the puppet only learns of it here.
            if ((flags & c_dead) != 0)
                m_turret.dead = true;
            m_shotHit = (flags & c_hit_particle) != 0;
            m_shots = shots;
            m_shotEnd = WKTWorldOffset.FromWire(end, WKTWorldOffset.Get());
            if (!m_shotsStarted) {
                m_shotsStarted = true;
                m_shownShots = shots;
            }
            m_hasState = true;
            return true;
        }

        public override void PuppetUpdate(float deltaTime) {
            if (!m_hasState) return;
            var gun = m_turret.turretGun;
            if (gun != null)
                gun.localRotation = Quaternion.Slerp(gun.localRotation, m_gun, 1f - Mathf.Exp(-c_follow_rate * deltaTime));
            if (m_shownShots == m_shots || m_turret.turretBarrel == null) return;
            m_shownShots = m_shots;
            m_replaying = true;
            try {
                s_playTracer(m_turret, m_turret.turretBarrel.position, m_shotEnd, 100f, m_shotHit);
            }
            finally {
                m_replaying = false;
            }
        }
    }
}
