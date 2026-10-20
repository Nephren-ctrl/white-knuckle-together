using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A basic denizen that aims with turret segments (<c>DenizenMovementModule_Turret</c>, such as the Ravelin's swivelling
    /// body): its movement module's Update turns them toward its target, which never runs on a puppet. The simulating
    /// machine sends each segment's local rotation, which the puppet follows, so other players see where it faces and their
    /// replayed hit checks start from the same place. Denizens without turret segments send nothing.
    /// </summary>
    internal sealed class WKTTurretSegmentsPresenter : WKTPuppetPresenterBase {
        /// <summary>The most segments sent; the game's turrets have one or two.</summary>
        public const int MAX_SEGMENTS = 8;
        private const float c_follow_rate = 15f;
        private readonly Transform?[] m_segments;
        private readonly Quaternion[] m_targets;
        private bool m_hasState;

        public override bool HasState => m_segments.Length > 0;

        public WKTTurretSegmentsPresenter(GameEntity entity) : base(entity) {
            var segments = (entity as DEN_BasicDenizen)?.movementModule is DenizenMovementModule_Turret { turretSegments: { } list } ? list : null;
            var count = segments == null ? 0 : Mathf.Min(segments.Count, MAX_SEGMENTS);
            m_segments = new Transform?[count];
            m_targets = new Quaternion[count];
            for (var i = 0; i < count; i++) {
                m_segments[i] = segments![i]?.turretTransform;
                m_targets[i] = m_segments[i] != null ? m_segments[i]!.localRotation : Quaternion.identity;
            }
        }

        public override void WriteState(NetDataWriter writer) {
            writer.Put((byte)m_segments.Length);
            foreach (var segment in m_segments)
                WKTWire.Put(writer, segment != null ? segment.localRotation : Quaternion.identity);
        }

        public override bool ReadState(NetDataReader reader) {
            if (!reader.TryGetByte(out var count) || count != m_segments.Length) return false;
            for (var i = 0; i < count; i++) {
                if (!WKTWire.TryGetRotation(reader, out var rotation)) return false;
                m_targets[i] = rotation;
            }
            m_hasState = true;
            return true;
        }

        public override void PuppetUpdate(float deltaTime) {
            if (!m_hasState) return;
            var blend = 1f - Mathf.Exp(-c_follow_rate * deltaTime);
            for (var i = 0; i < m_segments.Length; i++) {
                var segment = m_segments[i];
                if (segment != null)
                    segment.localRotation = Quaternion.Slerp(segment.localRotation, m_targets[i], blend);
            }
        }
    }
}
