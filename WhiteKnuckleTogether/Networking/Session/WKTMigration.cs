namespace WhiteKnuckleTogether {
    /// <summary>
    /// A client's side of a host migration: who takes over, and when to try reaching them again. Pure, so it can be
    /// tested without Steam.
    /// </summary>
    public sealed class WKTMigration {
        private float m_deadline;
        /// <summary>When to try reaching the successor again. Infinity while an attempt is in flight.</summary>
        private float m_retryTime;

        /// <summary>The player taking over from a host that left. Null when no migration is in progress.</summary>
        public WKTPlayer? Successor { get; private set; }
        public bool IsActive => Successor != null;

        /// <summary>False, changing nothing, when a migration is already in progress.</summary>
        public bool TryBegin(WKTPlayer successor, float time) {
            if (Successor != null) return false;
            Successor = successor;
            m_deadline = time + WKTNetworkConsts.MIGRATION_TIMEOUT;
            m_retryTime = time;
            return true;
        }

        /// <summary>Call once a <see cref="WKTMigrationStep.Connect"/> attempt has started.</summary>
        public void OnAttemptStarted() => m_retryTime = float.PositiveInfinity;

        /// <summary>The attempt to reach the successor failed. The next one waits <see cref="WKTNetworkConsts.MIGRATION_RETRY_DELAY"/>.</summary>
        public void OnAttemptFailed(float time) {
            if (Successor != null)
                m_retryTime = time + WKTNetworkConsts.MIGRATION_RETRY_DELAY;
        }

        public void Clear() => Successor = null;

        public WKTMigrationStep Update(float time, WKTPlayer? localPlayer) {
            if (Successor == null) return WKTMigrationStep.None;
            if (ReferenceEquals(Successor, localPlayer)) return WKTMigrationStep.Promote;
            if (time >= m_deadline) return WKTMigrationStep.GiveUp;
            return time >= m_retryTime ? WKTMigrationStep.Connect : WKTMigrationStep.None;
        }
    }
}
