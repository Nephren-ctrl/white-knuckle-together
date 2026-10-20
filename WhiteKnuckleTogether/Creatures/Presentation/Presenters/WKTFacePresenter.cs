using System;
using HarmonyLib;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The Face's body: its segments follow the head's path, which the game records and animates in the Face's own ticks
    /// (<c>UpdateSegments</c>, <c>AnimateSegments</c>). Those run on the puppet too, at the simulating machine's speed. The
    /// head's damage glow follows the damage it has taken since it last retreated, and its fuse sound and residue particles
    /// follow the residue near its head.
    /// </summary>
    internal sealed class WKTFacePresenter : WKTPuppetPresenterBase {
        private static readonly Action<DEN_Face> s_animateSegments = AccessTools.MethodDelegate<Action<DEN_Face>>(AccessTools.Method(typeof(DEN_Face), "AnimateSegments"));
        private static readonly Action<DEN_Face> s_updateSegments = AccessTools.MethodDelegate<Action<DEN_Face>>(AccessTools.Method(typeof(DEN_Face), "UpdateSegments"));
        private static readonly AccessTools.FieldRef<DEN_Face, float> s_curDamage = AccessTools.FieldRefAccess<DEN_Face, float>("curDamage");
        private static readonly int s_emissionColor = Shader.PropertyToID("_EmissionColor");
        private readonly DEN_Face m_face;
        private float m_velocityMult = 1f;
        private float m_damage;

        public override bool HasState => true;

        public WKTFacePresenter(GameEntity entity) : base(entity) => m_face = (DEN_Face)entity;

        public override void WriteState(NetDataWriter writer) {
            writer.Put(m_face.movementModule != null ? m_face.movementModule.velocityMult : 1f);
            writer.Put(m_face.maxHealth > 0f ? s_curDamage(m_face) / m_face.maxHealth : 0f);
        }

        public override bool ReadState(NetDataReader reader) {
            if (!reader.TryGetFloat(out var velocityMult) || !reader.TryGetFloat(out var damage) || !float.IsFinite(velocityMult) || !float.IsFinite(damage)) return false;
            m_velocityMult = Mathf.Clamp(velocityMult, 0f, 100f);
            m_damage = Mathf.Clamp01(damage);
            return true;
        }

        public override void PuppetFixedUpdate(float fixedDeltaTime) => s_updateSegments(m_face);

        public override void PuppetUpdate(float deltaTime) {
            if (m_face.movementModule != null)
                m_face.movementModule.velocityMult = m_velocityMult;
            s_animateSegments(m_face);
            ShowResidue(deltaTime);
            // DEN_Face.Update's glow.
            if (m_face.faceMesh == null) return;
            var pulse = Color.Lerp(Color.black, Color.red, Mathf.Sin(Time.time * m_damage * 6f) * 0.5f + 0.5f);
            m_face.faceMesh.material.SetColor(s_emissionColor, Color.Lerp(Color.white, pulse, m_damage * 2f));
        }

        /// <summary>
        /// <c>DEN_Face.TargetCollisionCheck</c>'s residue: near residue in the level, its fuse sound swells and the residue
        /// particles stream from the head. It only depends on where the head is, so the puppet judges it from its own pose.
        /// </summary>
        private void ShowResidue(float deltaTime) {
            var face = m_face;
            var near = Physics.CheckSphere(face.transform.position, face.residueRadius, face.residueMask);
            if (face.fuseAudio != null)
                face.fuseAudio.volume = Mathf.Lerp(face.fuseAudio.volume, near ? 1f : 0f, deltaTime);
            var residue = face.residueSystem;
            if (residue == null) return;
            if (near) {
                if (!residue.isPlaying)
                    residue.Play();
            }
            else if (!residue.isStopped)
                residue.Stop();
        }
    }
}
