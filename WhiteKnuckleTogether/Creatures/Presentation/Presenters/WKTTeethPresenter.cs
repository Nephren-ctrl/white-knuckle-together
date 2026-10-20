using HarmonyLib;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The Teeth's model (<c>geoRoot</c>) moves apart from its body: it snaps to walls and floors, leaps and hides while
    /// burrowed, all in the Teeth's own code. Its pose and whether it is shown come from the simulating machine, as do the
    /// burn and cohesion effects on its material. Its hand particles and the hands on the screen follow this machine's own
    /// player, as <c>DEN_Teeth.Animation</c> does.
    /// </summary>
    internal sealed class WKTTeethPresenter : WKTPuppetPresenterBase {
        /// <summary>Further than this from its shown pose, the model jumps rather than glides, such as after a burrow.</summary>
        private const float c_snap_distance = 4f;
        private const float c_follow_rate = 12f;
        private const byte c_shown = 1;
        private static readonly AccessTools.FieldRef<DEN_Teeth, Material> s_bodyMaterial = AccessTools.FieldRefAccess<DEN_Teeth, Material>("bodyMaterial");
        private static readonly AccessTools.FieldRef<DEN_Teeth, float> s_cohesionEffect = AccessTools.FieldRefAccess<DEN_Teeth, float>("cohesionEffect");
        private static readonly AccessTools.FieldRef<DEN_Teeth, float> s_burnEffect = AccessTools.FieldRefAccess<DEN_Teeth, float>("burnEffect");
        private static readonly int s_wiggle = Shader.PropertyToID("_Wiggle");
        private static readonly int s_shimmerColor = Shader.PropertyToID("_ShimmerColor");
        private static readonly int s_emissionColor = Shader.PropertyToID("_EmissionColor");
        private readonly DEN_Teeth m_teeth;
        private Vector3 m_position;
        private Quaternion m_rotation = Quaternion.identity;
        private bool m_shown = true;
        private bool m_hasState;

        public override bool HasState => true;

        public WKTTeethPresenter(GameEntity entity) : base(entity) => m_teeth = (DEN_Teeth)entity;

        public override void WriteState(NetDataWriter writer) {
            var geo = m_teeth.geoRoot;
            WKTWire.Put(writer, WKTWorldOffset.ToWire(geo.position, WKTWorldOffset.Get()));
            WKTWire.Put(writer, geo.rotation);
            writer.Put(geo.gameObject.activeSelf ? c_shown : (byte)0);
            writer.Put(s_cohesionEffect(m_teeth));
            writer.Put(s_burnEffect(m_teeth));
        }

        public override bool ReadState(NetDataReader reader) {
            if (!WKTWire.TryGetPosition(reader, out var position) || !WKTWire.TryGetRotation(reader, out var rotation) || !reader.TryGetByte(out var flags) ||
                !reader.TryGetFloat(out var cohesion) || !reader.TryGetFloat(out var burn) || !float.IsFinite(cohesion) || !float.IsFinite(burn)) return false;
            m_position = WKTWorldOffset.FromWire(position, WKTWorldOffset.Get());
            m_rotation = rotation;
            m_shown = (flags & c_shown) != 0;
            s_cohesionEffect(m_teeth) = Mathf.Clamp(cohesion, 0f, 100f);
            s_burnEffect(m_teeth) = Mathf.Clamp(burn, 0f, 100f);
            m_hasState = true;
            return true;
        }

        public override void PuppetUpdate(float deltaTime) {
            if (m_hasState)
                FollowModel(deltaTime);
            // DEN_Teeth.Update's material effects, which wear off between states as on the simulating machine.
            var cohesion = s_cohesionEffect(m_teeth) = Mathf.Max(s_cohesionEffect(m_teeth) - deltaTime, 0f);
            var burn = s_burnEffect(m_teeth) = Mathf.Max(s_burnEffect(m_teeth) - deltaTime * 0.5f, 0f);
            var material = s_bodyMaterial(m_teeth);
            if (material != null) {
                material.SetFloat(s_wiggle, cohesion * m_teeth.cohesionEffectMultiplier);
                var color = Color.Lerp(Color.black, Color.red, burn);
                material.SetColor(s_shimmerColor, color);
                material.SetColor(s_emissionColor, color);
            }
            Animate(deltaTime);
        }

        private void FollowModel(float deltaTime) {
            var geo = m_teeth.geoRoot;
            if (geo.gameObject.activeSelf != m_shown) {
                geo.gameObject.SetActive(m_shown);
                geo.SetPositionAndRotation(m_position, m_rotation);
                return;
            }
            if ((geo.position - m_position).sqrMagnitude > c_snap_distance * c_snap_distance) {
                geo.SetPositionAndRotation(m_position, m_rotation);
                return;
            }
            var t = 1f - Mathf.Exp(-c_follow_rate * deltaTime);
            geo.SetPositionAndRotation(Vector3.Lerp(geo.position, m_position, t), Quaternion.Slerp(geo.rotation, m_rotation, t));
        }

        /// <summary>The viewer's part of <c>DEN_Teeth.Animation</c>: its effect zone and hand particles, by this machine's own player.</summary>
        private void Animate(float deltaTime) {
            if (m_teeth.fxZone != null)
                m_teeth.fxZone.blend = Mathf.Lerp(m_teeth.fxZone.blend, m_teeth.geoRoot.gameObject.activeSelf ? 1f : 0f, deltaTime);
            var hand = m_teeth.handParticle;
            if (hand != null && m_teeth.handParticleLocator != null) {
                hand.transform.position = m_teeth.handParticleLocator.position;
                hand.transform.rotation = Quaternion.Lerp(hand.transform.rotation, m_teeth.handParticleLocator.rotation, deltaTime);
            }
            ShowLocalEffects(m_teeth);
        }

        /// <summary>
        /// The parts of <c>DEN_Teeth.Animation</c> that go by the local player's own distance to the Teeth: its hand particles'
        /// rate and the hands over the screen. The simulating machine measures the distance to the nearest player for the
        /// Teeth's AI, which Animation also used for these, so they covered the screen of whoever simulated it when only
        /// someone else was close.
        /// </summary>
        public static void ShowLocalEffects(DEN_Teeth teeth) {
            var player = ENT_Player.playerObject;
            var distance = player != null && !player.dead ? Vector3.Distance(teeth.transform.position, player.transform.position) : float.PositiveInfinity;
            if (teeth.handParticle != null) {
                var emission = teeth.handParticle.emission;
                emission.rateOverTimeMultiplier = Mathf.Lerp(25f, 0f, distance / 10f);
            }
            var screen = teeth.screenHandEffect;
            if (screen == null) return;
            if (distance < 10f) {
                if (!screen.isPlaying)
                    screen.Play();
            }
            else if (screen.isPlaying)
                screen.Stop();
            var shape = screen.shape;
            shape.radius = Mathf.Lerp(2f, 3f, distance / 10f);
            var screenEmission = screen.emission;
            screenEmission.rateOverTimeMultiplier = Mathf.Lerp(9f, 0f, distance / 10f);
        }
    }
}
