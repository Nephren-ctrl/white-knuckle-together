using System;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Turns a rope's node positions into <see cref="WKTRopeLink"/>s and back. Positions are in the rope's own space, with
    /// the anchor (node 0) at the origin, so neither the world offset nor a moving anchor matters.
    /// <list type="bullet">
    /// <item>Each link's direction is given in the frame of the link before it. Frames are carried down the rope by the
    /// smallest rotation from one link to the next (parallel transport), starting from the rope's own axes, so a rope hanging
    /// straight down has no degenerate frame.</item>
    /// <item>The encoder rebuilds the rope exactly as the receiver will and aims each link from the rebuilt node at the true
    /// next one, so quantization error never adds up along the rope: every node is within one link's error of the truth.</item>
    /// </list>
    /// Managed math only, so it runs in tests; allocation free.
    /// </summary>
    internal static class WKTRopeCodec {
        /// <summary>The most nodes a synced rope may have. The game's longest has a few dozen.</summary>
        public const int MAX_NODES = 64;
        private const int c_direction_bits = 12;
        private const int c_direction_steps = (1 << c_direction_bits) - 1;
        private const float c_min_length = 1e-5f;

        /// <summary>
        /// The longest a link can be. <c>ENV_Rope</c> keeps its target within one unit past its full length, and the last
        /// link reaches the target.
        /// </summary>
        public static float GetMaxLength(float nodeDistance, int nodeCount) => Mathf.Max(nodeDistance, 0f) * nodeCount + 1f;

        /// <summary>
        /// Encodes <paramref name="nodes"/> (node 0 at the origin) into <c>nodes.Length - 1</c> links. Fills
        /// <paramref name="rebuilt"/> with what <see cref="Decode"/> will give back for them.
        /// </summary>
        public static void Encode(ReadOnlySpan<Vector3> nodes, float maxLength, Span<WKTRopeLink> links, Span<Vector3> rebuilt) {
            var frame = Frame.Identity;
            rebuilt[0] = Vector3.zero;
            for (var i = 1; i < nodes.Length; i++) {
                var offset = nodes[i] - rebuilt[i - 1];
                var length = offset.magnitude;
                var direction = length > c_min_length ? offset / length : frame.Forward;
                var link = new WKTRopeLink(PackDirection(frame.ToLocal(direction)), PackLength(length, maxLength));
                links[i - 1] = link;
                rebuilt[i] = Step(ref frame, rebuilt[i - 1], link, maxLength);
            }
        }

        /// <summary>Rebuilds <c>links.Length + 1</c> nodes from <paramref name="links"/>, node 0 at the origin.</summary>
        public static void Decode(ReadOnlySpan<WKTRopeLink> links, float maxLength, Span<Vector3> nodes) {
            var frame = Frame.Identity;
            nodes[0] = Vector3.zero;
            for (var i = 0; i < links.Length; i++)
                nodes[i + 1] = Step(ref frame, nodes[i], links[i], maxLength);
        }

        /// <summary>
        /// Blends two rebuilt ropes link by link: directions are blended and renormalized and lengths blended, so links keep
        /// their length through a swing instead of shortening as straight node blending would.
        /// </summary>
        public static void Blend(ReadOnlySpan<Vector3> from, ReadOnlySpan<Vector3> to, float t, Span<Vector3> result) {
            result[0] = Vector3.zero;
            for (var i = 1; i < result.Length; i++) {
                var fromLink = from[i] - from[i - 1];
                var toLink = to[i] - to[i - 1];
                var fromLength = fromLink.magnitude;
                var toLength = toLink.magnitude;
                var fromDirection = fromLength > c_min_length ? fromLink / fromLength : Vector3.zero;
                var toDirection = toLength > c_min_length ? toLink / toLength : Vector3.zero;
                var direction = fromDirection + (toDirection - fromDirection) * t;
                var directionLength = direction.magnitude;
                var length = fromLength + (toLength - fromLength) * t;
                // Opposite directions blend through zero: fall back to blending the links themselves.
                result[i] = result[i - 1] + (directionLength > c_min_length ? direction * (length / directionLength) : fromLink + (toLink - fromLink) * t);
            }
        }

        /// <summary>Octahedral encoding of a unit vector into two 12 bit coordinates.</summary>
        public static uint PackDirection(Vector3 direction) {
            var sum = Mathf.Abs(direction.x) + Mathf.Abs(direction.y) + Mathf.Abs(direction.z);
            // NaN fails the comparison too.
            if (!(sum >= c_min_length)) return PackCoordinates(0f, 0f);
            var x = direction.x / sum;
            var y = direction.y / sum;
            if (direction.z < 0f) {
                var foldedX = (1f - Mathf.Abs(y)) * SignOf(x);
                y = (1f - Mathf.Abs(x)) * SignOf(y);
                x = foldedX;
            }
            return PackCoordinates(x, y);
        }

        /// <summary>The unit vector <see cref="PackDirection"/> encoded.</summary>
        public static Vector3 UnpackDirection(uint packed) {
            var x = (packed & c_direction_steps) * (2f / c_direction_steps) - 1f;
            var y = ((packed >> c_direction_bits) & c_direction_steps) * (2f / c_direction_steps) - 1f;
            var z = 1f - Mathf.Abs(x) - Mathf.Abs(y);
            if (z < 0f) {
                var foldedX = (1f - Mathf.Abs(y)) * SignOf(x);
                y = (1f - Mathf.Abs(x)) * SignOf(y);
                x = foldedX;
            }
            var vector = new Vector3(x, y, z);
            return vector / vector.magnitude;
        }

        public static ushort PackLength(float length, float maxLength) {
            if (!(length > 0f) || !(maxLength > 0f)) return 0;
            return (ushort)Mathf.Clamp(Mathf.RoundToInt(length / maxLength * ushort.MaxValue), 0, ushort.MaxValue);
        }

        public static float UnpackLength(ushort packed, float maxLength) => packed * (maxLength / ushort.MaxValue);

        private static Vector3 Step(ref Frame frame, Vector3 from, in WKTRopeLink link, float maxLength) {
            var direction = frame.ToWorld(UnpackDirection(link.Direction));
            frame.TransportTo(direction);
            return from + direction * UnpackLength(link.Length, maxLength);
        }

        private static uint PackCoordinates(float x, float y) {
            var qx = (uint)Mathf.Clamp(Mathf.RoundToInt((x * 0.5f + 0.5f) * c_direction_steps), 0, c_direction_steps);
            var qy = (uint)Mathf.Clamp(Mathf.RoundToInt((y * 0.5f + 0.5f) * c_direction_steps), 0, c_direction_steps);
            return qx | (qy << c_direction_bits);
        }

        private static float SignOf(float value) => value >= 0f ? 1f : -1f;

        /// <summary>An orthonormal frame: <see cref="Up"/> is <see cref="Forward"/> × <see cref="Right"/>.</summary>
        private struct Frame {
            public static readonly Frame Identity = new() { m_right = new Vector3(1f, 0f, 0f), m_up = new Vector3(0f, 1f, 0f), Forward = new Vector3(0f, 0f, 1f) };
            public Vector3 Forward;
            private Vector3 m_right;
            private Vector3 m_up;

            public readonly Vector3 ToLocal(Vector3 direction) =>
                new(Vector3.Dot(direction, m_right), Vector3.Dot(direction, m_up), Vector3.Dot(direction, Forward));

            public readonly Vector3 ToWorld(Vector3 local) => m_right * local.x + m_up * local.y + Forward * local.z;

            /// <summary>Turns the frame by the smallest rotation that points <see cref="Forward"/> along <paramref name="direction"/>.</summary>
            public void TransportTo(Vector3 direction) {
                var cos = Vector3.Dot(Forward, direction);
                var axis = Vector3.Cross(Forward, direction);
                var sin = axis.magnitude;
                if (sin > c_min_length) {
                    axis /= sin;
                    m_right = Rotate(m_right, axis, cos, sin);
                }
                else if (cos < 0f) {
                    // Folded straight back: turn half way round the up axis.
                    m_right = -m_right;
                }
                Forward = direction;
                // Re-orthonormalize against drift down a long rope.
                m_right = (m_right - Forward * Vector3.Dot(m_right, Forward)).normalized;
                m_up = Vector3.Cross(Forward, m_right);
            }

            /// <summary>Rodrigues' rotation of <paramref name="vector"/> about the unit <paramref name="axis"/>.</summary>
            private static Vector3 Rotate(Vector3 vector, Vector3 axis, float cos, float sin) =>
                vector * cos + Vector3.Cross(axis, vector) * sin + axis * (Vector3.Dot(axis, vector) * (1f - cos));
        }
    }
}
