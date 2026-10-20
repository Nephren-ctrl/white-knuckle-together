using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A gasbag picks its size and mesh at random when it starts. Gasbags spawned while the run goes on roll differently on
    /// each machine, so the puppet takes the simulating machine's.
    /// </summary>
    internal sealed class WKTGasbagPresenter : WKTPuppetPresenterBase {
        private const byte c_no_mesh = byte.MaxValue;
        /// <summary>Larger than any gasbag, but rejects garbage.</summary>
        private const float c_max_scale = 100f;
        private readonly DEN_Gasbag m_gasbag;
        private readonly MeshFilter? m_meshFilter;
        private Vector3 m_scale;
        private byte m_mesh = c_no_mesh;
        private bool m_applied;

        public override bool HasState => true;

        public WKTGasbagPresenter(GameEntity entity) : base(entity) {
            m_gasbag = (DEN_Gasbag)entity;
            m_meshFilter = entity.GetComponent<MeshFilter>();
        }

        public override void WriteState(NetDataWriter writer) {
            var scale = m_gasbag.transform.localScale;
            writer.Put(scale.x);
            writer.Put(scale.y);
            writer.Put(scale.z);
            var index = m_meshFilter != null && m_gasbag.meshOptions != null ? m_gasbag.meshOptions.IndexOf(m_meshFilter.sharedMesh) : -1;
            writer.Put(index is >= 0 and < c_no_mesh ? (byte)index : c_no_mesh);
        }

        public override bool ReadState(NetDataReader reader) {
            if (!reader.TryGetFloat(out var x) || !reader.TryGetFloat(out var y) || !reader.TryGetFloat(out var z) || !reader.TryGetByte(out var mesh)) return false;
            var scale = new Vector3(x, y, z);
            if (!float.IsFinite(scale.sqrMagnitude) || Mathf.Abs(x) > c_max_scale || Mathf.Abs(y) > c_max_scale || Mathf.Abs(z) > c_max_scale) return false;
            if (m_applied && scale == m_scale && mesh == m_mesh) return true;
            m_scale = scale;
            m_mesh = mesh;
            m_applied = false;
            return true;
        }

        public override void PuppetUpdate(float deltaTime) {
            if (m_applied || m_scale == Vector3.zero) return;
            m_applied = true;
            m_gasbag.transform.localScale = m_scale;
            var options = m_gasbag.meshOptions;
            if (m_meshFilter != null && options != null && m_mesh < options.Count && options[m_mesh] != null)
                m_meshFilter.sharedMesh = options[m_mesh];
        }
    }
}
