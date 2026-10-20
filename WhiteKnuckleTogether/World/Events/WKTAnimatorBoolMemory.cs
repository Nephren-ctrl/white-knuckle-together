using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The bools level scripting set on an animator while it was switched off, put on it once it is on again. Unity drops a
    /// bool set on an animator that is not playing, and a level far from a player is switched off on their machine, so an
    /// event the host applied there for everyone (the giant door into Habitation opening) was lost for good on the machines
    /// of players who were not near it yet. Added to the animator's object in a multiplayer run, only when needed.
    /// </summary>
    internal sealed class WKTAnimatorBoolMemory : MonoBehaviour {
        private readonly Dictionary<string, bool> m_bools = new();
        private Animator? m_animator;
        private bool m_pending;

        /// <summary>Keeps <paramref name="value"/> for <paramref name="parameter"/> of <paramref name="animator"/>, set once it plays.</summary>
        public static void Remember(Animator animator, string parameter, bool value) {
            if (!animator.TryGetComponent<WKTAnimatorBoolMemory>(out var memory))
                memory = animator.gameObject.AddComponent<WKTAnimatorBoolMemory>();
            memory.m_animator = animator;
            memory.m_bools[parameter] = value;
            memory.m_pending = true;
        }

        [UsedImplicitly]
        private void OnEnable() => m_pending = m_bools.Count > 0;

        /// <summary>A frame after it is switched on, once the animator plays again.</summary>
        [UsedImplicitly]
        private void Update() {
            if (!m_pending || m_animator == null || !m_animator.isActiveAndEnabled) return;
            m_pending = false;
            foreach (var pair in m_bools)
                m_animator.SetBool(pair.Key, pair.Value);
        }
    }
}
