using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Pushes for remote players, summed per player until they are sent, so a creature pulling every frame sends one push per
    /// player a frame. A push from a creature, hazard or projectile is kept apart from any other, since the player's knockback
    /// resistance only weakens those. Pure and allocation free, so it can be tested without Unity's runtime.
    /// </summary>
    internal sealed class WKTForceAccumulator {
        private const int c_players = byte.MaxValue + 1;
        private readonly Vector3[] m_resisted = new Vector3[c_players];
        private readonly Vector3[] m_plain = new Vector3[c_players];
        private readonly bool[] m_dirty = new bool[c_players];
        private readonly byte[] m_dirtyIds = new byte[c_players];
        private int m_dirtyCount;
        private int m_next;
        private bool m_takingResisted = true;

        public bool IsEmpty => m_dirtyCount == 0;

        /// <summary>Adds a push for <paramref name="player"/>. <paramref name="resisted"/>: from a creature, hazard or projectile.</summary>
        public void Add(byte player, Vector3 force, bool resisted) {
            if (force == Vector3.zero) return;
            if (resisted)
                m_resisted[player] += force;
            else
                m_plain[player] += force;
            if (m_dirty[player]) return;
            m_dirty[player] = true;
            m_dirtyIds[m_dirtyCount++] = player;
        }

        /// <summary>Whether a push source weakens with the player's knockback resistance, as in <c>ENT_Player.AddForce</c>.</summary>
        public static bool IsResisted(string? source) => source is "hazard" or "denizen" or "projectile";

        /// <summary>Takes the next summed push, emptying the accumulator as it goes. False once nothing is left.</summary>
        public bool TryTake(out byte player, out Vector3 force, out bool resisted) {
            while (m_next < m_dirtyCount) {
                player = m_dirtyIds[m_next];
                if (m_takingResisted) {
                    m_takingResisted = false;
                    force = m_resisted[player];
                    m_resisted[player] = Vector3.zero;
                    resisted = true;
                    if (force != Vector3.zero) return true;
                }
                m_takingResisted = true;
                m_next++;
                force = m_plain[player];
                m_plain[player] = Vector3.zero;
                m_dirty[player] = false;
                resisted = false;
                if (force != Vector3.zero) return true;
            }
            m_next = 0;
            m_dirtyCount = 0;
            player = 0;
            force = Vector3.zero;
            resisted = false;
            return false;
        }
    }
}
