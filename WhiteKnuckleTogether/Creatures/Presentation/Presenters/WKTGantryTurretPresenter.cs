using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A gantry turret's eyes, which its Update swivels to random angles while it has a target (DOTween's
    /// <c>DOLocalRotate</c>), swivel the same way on puppets while the simulating machine says it has one. Its turret segments
    /// are shown as for any other basic denizen (see <see cref="WKTTurretSegmentsPresenter"/>).
    /// </summary>
    internal sealed class WKTGantryTurretPresenter : WKTPuppetPresenterBase {
        private readonly DEN_GantryTurret m_gantry;
        private readonly WKTTurretSegmentsPresenter m_segments;
        private readonly EyeTurn[] m_turns;
        private bool m_hasTarget;

        public override bool HasState => true;

        public WKTGantryTurretPresenter(GameEntity entity) : base(entity) {
            m_gantry = (DEN_GantryTurret)entity;
            m_segments = new WKTTurretSegmentsPresenter(entity);
            m_turns = new EyeTurn[m_gantry.eyes?.Count ?? 0];
        }

        public override void WriteState(NetDataWriter writer) {
            m_segments.WriteState(writer);
            writer.Put(m_gantry.targetComponent != null && m_gantry.targetComponent.HasTarget());
        }

        public override bool ReadState(NetDataReader reader) => m_segments.ReadState(reader) && reader.TryGetBool(out m_hasTarget);

        public override void PuppetUpdate(float deltaTime) {
            m_segments.PuppetUpdate(deltaTime);
            for (var i = 0; i < m_turns.Length; i++) {
                var eye = m_gantry.eyes[i];
                if (eye == null) continue;
                ref var turn = ref m_turns[i];
                if (turn.Left <= 0f) {
                    // A turn under way finishes, as the game's tween does; a new one starts only while it has a target.
                    if (!m_hasTarget) continue;
                    turn = new EyeTurn(eye.localRotation, Quaternion.Euler(0f, 0f, Random.Range(-90, 90)), Random.Range(0.5f, 1.5f));
                }
                turn.Left -= deltaTime;
                eye.localRotation = Quaternion.Slerp(turn.From, turn.To, Mathf.SmoothStep(0f, 1f, 1f - Mathf.Max(turn.Left, 0f) / turn.Duration));
            }
        }

        private struct EyeTurn {
            public readonly Quaternion From;
            public readonly Quaternion To;
            public readonly float Duration;
            public float Left;

            public EyeTurn(Quaternion from, Quaternion to, float duration) {
                From = from;
                To = to;
                Duration = duration;
                Left = duration;
            }
        }
    }
}
