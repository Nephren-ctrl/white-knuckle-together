using System;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Received states of one remote rope, replayed like every other object (see <see cref="WKTSnapshotBufferBase{TPose}"/>).
    /// Each accepted state's rebuilt nodes go in their own slot; there is one slot more than the buffer holds samples, so a new
    /// state never overwrites one still being played. Allocation free after construction.
    /// </summary>
    public sealed class WKTRopeSnapshotBuffer {
        private const int c_slots = WKTSnapshotBufferBase<WKTRopeSample>.CAPACITY + 1;
        private readonly WKTSnapshotBufferBase<WKTRopeSample> m_samples = new();
        private readonly Vector3[] m_nodes;
        private int m_next;

        public WKTRopeSnapshotBuffer(int nodeCount) {
            NodeCount = nodeCount;
            m_nodes = new Vector3[c_slots * nodeCount];
        }

        public int NodeCount { get; }

        /// <summary>Adds rebuilt <paramref name="nodes"/> received at <paramref name="receiveTime"/>. False for duplicates and old states.</summary>
        public bool Add(ushort sequence, ReadOnlySpan<Vector3> nodes, Vector3 velocity, double receiveTime) {
            if (nodes.Length != NodeCount) return false;
            var slot = m_next;
            nodes.CopyTo(Slot(slot));
            if (!m_samples.Add(sequence, new WKTRopeSample(slot, nodes[NodeCount - 1], velocity), receiveTime)) return false;
            m_next = (slot + 1) % c_slots;
            return true;
        }

        /// <summary>The nodes to show at <paramref name="now"/>, in the rope's own space. False until a state has been added.</summary>
        public bool TrySample(double now, Span<Vector3> nodes, out Vector3 velocity) {
            velocity = Vector3.zero;
            if (nodes.Length != NodeCount || !m_samples.TrySample(now, out var sample)) return false;
            velocity = sample.Velocity;
            if (sample.From == sample.To || sample.T <= 0f)
                Slot(sample.From).CopyTo(nodes);
            else if (sample.T >= 1f)
                Slot(sample.To).CopyTo(nodes);
            else
                WKTRopeCodec.Blend(Slot(sample.From), Slot(sample.To), sample.T, nodes);
            return true;
        }

        public void Clear() {
            m_samples.Clear();
            m_next = 0;
        }

        private Span<Vector3> Slot(int slot) => m_nodes.AsSpan(slot * NodeCount, NodeCount);
    }
}
