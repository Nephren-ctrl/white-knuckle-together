using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A Hopper Tick is simulated by the player it chases, so its leap, latching on, draining and letting go are that player's
    /// own, as in singleplayer. Everyone else sees whether it is latched on: its world model hides and its latched model shows,
    /// as <c>DEN_Hopper_Tick.Attach</c> and <c>Detach</c> switch them.
    /// </summary>
    internal sealed class WKTTickPresenter : WKTPuppetPresenterBase {
        private readonly DEN_Hopper_Tick m_tick;
        private bool m_attached;
        private bool m_hasState;

        public override bool HasState => true;

        public WKTTickPresenter(GameEntity entity) : base(entity) => m_tick = (DEN_Hopper_Tick)entity;

        public override void WriteState(NetDataWriter writer) => writer.Put(m_tick.attachModel != null && m_tick.attachModel.activeSelf);

        public override bool ReadState(NetDataReader reader) {
            if (!reader.TryGetBool(out m_attached)) return false;
            m_hasState = true;
            return true;
        }

        public override void PuppetUpdate(float deltaTime) {
            if (!m_hasState) return;
            Show(m_tick.worldModel, !m_attached);
            Show(m_tick.attachModel, m_attached);
        }

        /// <summary>
        /// A tick taken over here is not latched onto anyone on this machine (it is only ever latched on its victim's), so the
        /// latched model the previous owner's state showed goes back to the world model.
        /// </summary>
        public override void OnTakenOver() {
            if (!m_hasState) return;
            m_hasState = false;
            m_attached = false;
            Show(m_tick.worldModel, true);
            Show(m_tick.attachModel, false);
        }

        private static void Show(GameObject? model, bool shown) {
            if (model != null && model.activeSelf != shown)
                model.SetActive(shown);
        }
    }
}
