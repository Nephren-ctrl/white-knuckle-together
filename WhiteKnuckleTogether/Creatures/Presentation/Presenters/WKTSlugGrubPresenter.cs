using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A slug grub faces left or right at random once made (its <c>Start</c> mirrors it on each machine by itself), and its
    /// Update wakes it while something it fears is near or it is in a bag. Puppets show the simulating machine's side and
    /// whether it is awake.
    /// </summary>
    internal sealed class WKTSlugGrubPresenter : WKTPuppetPresenterBase {
        private const byte c_mirrored = 1;
        private const byte c_awake = 2;
        private static readonly int s_awake = Animator.StringToHash("awake");
        private readonly DEN_SlugGrub m_grub;
        private byte m_flags;
        private bool m_hasState;

        public override bool HasState => true;

        public WKTSlugGrubPresenter(GameEntity entity) : base(entity) => m_grub = (DEN_SlugGrub)entity;

        public override void WriteState(NetDataWriter writer) {
            byte flags = 0;
            if (m_grub.transform.localScale.x < 0f)
                flags |= c_mirrored;
            if (m_grub.animator != null && m_grub.animator.isActiveAndEnabled && m_grub.animator.GetBool(s_awake))
                flags |= c_awake;
            writer.Put(flags);
        }

        public override bool ReadState(NetDataReader reader) {
            if (!reader.TryGetByte(out m_flags)) return false;
            m_hasState = true;
            return true;
        }

        public override void PuppetUpdate(float deltaTime) {
            if (!m_hasState) return;
            var transform = m_grub.transform;
            var scale = transform.localScale;
            if (scale.x < 0f != ((m_flags & c_mirrored) != 0)) {
                scale.x = -scale.x;
                transform.localScale = scale;
            }
            if (m_grub.animator != null && m_grub.animator.isActiveAndEnabled)
                m_grub.animator.SetBool(s_awake, (m_flags & c_awake) != 0);
        }
    }
}
