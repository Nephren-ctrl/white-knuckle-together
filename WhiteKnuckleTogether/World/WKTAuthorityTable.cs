using System;
using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Host side record of which machine simulates each world object. Each object is owned by:
    /// <list type="number">
    /// <item>the one living player holding it;</item>
    /// <item>the host, when two or more players hold it;</item>
    /// <item>otherwise the living player the enemy is engaged with;</item>
    /// <item>otherwise the host.</item>
    /// </list>
    /// Engagement switches are held back by <see cref="MinHold"/> and <see cref="DamageCooldown"/> so authority does not
    /// bounce between players. Objects are told apart by <typeparamref name="THandle"/>: an entity's handle, or a rope's key.
    /// Pure, so it can be tested without Unity, and allocation free once warm.
    /// </summary>
    internal sealed class WKTAuthorityTable<THandle> where THandle : unmanaged, IEquatable<THandle> {
        public const byte HOST = WKTAuthorityTable.HOST;
        public const byte NOBODY = WKTAuthorityTable.NOBODY;
        private readonly Dictionary<THandle, Record> m_records = new();
        private readonly List<THandle> m_scratch = new();
        private readonly Func<byte, bool> m_isEligible;

        /// <summary>The host's own player id. A grab or engagement by it means <see cref="HOST"/>.</summary>
        public byte HostId { get; set; } = HOST;
        public double MinHold { get; }
        public double DamageCooldown { get; }
        /// <summary>How long a client owner may go unheard before the host takes the object back.</summary>
        public double Timeout { get; }
        public int Count => m_records.Count;

        /// <param name="isEligible">Whether a player id is connected and alive, and so may own an object.</param>
        /// <param name="minHold">How long an engagement stands before a closer player or the AI may change it.</param>
        /// <param name="damageCooldown">How long after a hit took an enemy over before another hit may.</param>
        /// <param name="timeout">How long a client owner may go unheard.</param>
        public WKTAuthorityTable(Func<byte, bool> isEligible, double minHold, double damageCooldown, double timeout) {
            m_isEligible = isEligible;
            MinHold = minHold;
            DamageCooldown = damageCooldown;
            Timeout = timeout;
        }

        public byte OwnerOf(THandle handle) => m_records.TryGetValue(handle, out var record) ? record.Owner : HOST;

        public bool IsHeldBy(THandle handle, byte playerId) => m_records.TryGetValue(handle, out var record) && record.Grabbers.Contains(playerId);

        public byte EngagedOf(THandle handle) =>m_records.TryGetValue(handle, out var record) ? record.Engaged : NOBODY;

        /// <summary><paramref name="playerId"/> grabbed the object. True if its owner changed, which is then in <paramref name="owner"/>.</summary>
        public bool Grab(THandle handle, byte playerId, double now, out byte owner) {
            var record = Get(handle);
            record.Grabbers.Add(playerId);
            return Commit(handle, record, now, out owner);
        }

        /// <summary><paramref name="playerId"/> let go of the object. True if its owner changed.</summary>
        public bool Release(THandle handle, byte playerId, double now, out byte owner) {
            if (!m_records.TryGetValue(handle, out var record) || !record.Grabbers.Remove(playerId)) {
                owner = OwnerOf(handle);
                return false;
            }
            return Commit(handle, record, now, out owner);
        }

        /// <summary>
        /// The enemy's engaged player should become <paramref name="playerId"/> (or <see cref="NOBODY"/>) for
        /// <paramref name="reason"/>. Ignored if a hold time forbids it or the player may not own anything. True if the
        /// owner changed.
        /// </summary>
        public bool SetEngaged(THandle handle, byte playerId, WKTEngageReason reason, double now, out byte owner) {
            var record = Get(handle);
            if (record.Engaged == playerId || !MayEngage(record, playerId, reason, now)) {
                owner = record.Owner;
                return false;
            }
            record.Engaged = playerId;
            record.EngagedAt = now;
            if (reason == WKTEngageReason.Damage)
                record.DamagedAt = now;
            return Commit(handle, record, now, out owner);
        }

        /// <summary>A state from <paramref name="playerId"/>. True if they own the object, which keeps their authority alive.</summary>
        public bool Heard(THandle handle, byte playerId, double now) {
            if (!m_records.TryGetValue(handle, out var record) || record.Owner != playerId) return false;
            record.LastHeard = now;
            m_records[handle] = record;
            return true;
        }

        /// <summary>
        /// <paramref name="playerId"/> died or left: they hold nothing and engage nothing. Every owner change goes into
        /// <paramref name="changes"/>.
        /// </summary>
        public void OnPlayerGone(byte playerId, double now, List<Change> changes) {
            CollectKeys();
            foreach (var handle in m_scratch) {
                var record = m_records[handle];
                var touched = record.Grabbers.Remove(playerId);
                if (record.Engaged == playerId) {
                    record.Engaged = NOBODY;
                    record.EngagedAt = now;
                    touched = true;
                }
                if (touched || record.Owner == playerId)
                    CommitInto(handle, record, now, changes);
            }
        }

        /// <summary>
        /// Takes back every object whose client owner went unheard for longer than <see cref="Timeout"/>, as if that owner
        /// let go and disengaged, and forgets objects idle for longer than the hold times. Every owner change goes into
        /// <paramref name="changes"/>.
        /// </summary>
        public void Expire(double now, List<Change> changes) {
            CollectKeys();
            foreach (var handle in m_scratch) {
                var record = m_records[handle];
                if (IsIdle(record, now)) {
                    m_records.Remove(handle);
                    continue;
                }
                if (record.Owner == HOST || now - record.LastHeard <= Timeout) continue;
                var silent = record.Owner;
                record.Grabbers.Remove(silent);
                if (record.Engaged == silent) {
                    record.Engaged = NOBODY;
                    record.EngagedAt = now;
                }
                CommitInto(handle, record, now, changes);
            }
        }

        /// <summary>The object is gone.</summary>
        public void Forget(THandle handle) => m_records.Remove(handle);

        public void Clear() => m_records.Clear();

        private bool MayEngage(in Record record, byte playerId, WKTEngageReason reason, double now) {
            if (playerId != NOBODY && playerId != HostId && !m_isEligible(playerId)) return false;
            switch (reason) {
                case WKTEngageReason.Lost:
                    return true;
                case WKTEngageReason.Acquired:
                case WKTEngageReason.Nearest:
                    return now - record.EngagedAt >= MinHold;
                case WKTEngageReason.Damage:
                    return playerId != NOBODY && now - record.DamagedAt >= DamageCooldown;
                default:
                    return false;
            }
        }

        /// <summary>The host's, held by nobody, engaged with nobody, and no longer within a hold time that must be remembered.</summary>
        private bool IsIdle(in Record record, double now) =>
            record.Owner == HOST && record.Grabbers.Count == 0 && record.Engaged == NOBODY && now - record.EngagedAt >= MinHold &&
            now - record.DamagedAt >= DamageCooldown;

        private Record Get(THandle handle) => m_records.TryGetValue(handle, out var record) ? record : Record.Empty;

        private bool Commit(THandle handle, Record record, double now, out byte owner) {
            var previous = record.Owner;
            owner = WKTAuthorityTable.Resolve(record.Grabbers, record.Engaged, m_isEligible, HostId);
            record.Owner = owner;
            if (owner != previous)
                record.LastHeard = now;
            if (IsIdle(record, now))
                m_records.Remove(handle);
            else
                m_records[handle] = record;
            return owner != previous;
        }

        private void CommitInto(THandle handle, Record record, double now, List<Change> changes) {
            if (Commit(handle, record, now, out var owner))
                changes.Add(new Change(handle, owner));
        }

        private void CollectKeys() {
            m_scratch.Clear();
            foreach (var handle in m_records.Keys)
                m_scratch.Add(handle);
        }

        public readonly struct Change {
            public readonly THandle Handle;
            public readonly byte Owner;

            public Change(THandle handle, byte owner) {
                Handle = handle;
                Owner = owner;
            }
        }

        private struct Record {
            public static readonly Record Empty = new() {
                Owner = HOST,
                Engaged = NOBODY,
                EngagedAt = double.NegativeInfinity,
                DamagedAt = double.NegativeInfinity
            };
            public WKTGrabberSet Grabbers;
            public byte Owner;
            public byte Engaged;
            public double EngagedAt;
            public double DamagedAt;
            public double LastHeard;
        }
    }

    /// <summary>The ownership rules every <see cref="WKTAuthorityTable{THandle}"/> follows.</summary>
    internal static class WKTAuthorityTable {
        /// <summary>The owner value meaning the host simulates the object, and the engaged value meaning nobody.</summary>
        public const byte HOST = 255;
        public const byte NOBODY = 255;

        /// <summary>The machine that should simulate an object held by <paramref name="grabbers"/> and engaged with <paramref name="engaged"/>.</summary>
        public static byte Resolve(in WKTGrabberSet grabbers, byte engaged, Func<byte, bool> isEligible, byte hostId) {
            var holder = HOST;
            var holders = 0;
            for (var i = 0; i < grabbers.Count; i++) {
                var grabber = grabbers[i];
                if (grabber != hostId && !isEligible(grabber)) continue;
                holder = grabber;
                holders++;
            }
            switch (holders) {
                case 1:
                    return holder == hostId ? HOST : holder;
                case > 1:
                    return HOST;
            }
            if (engaged == NOBODY || engaged == hostId || !isEligible(engaged)) return HOST;
            return engaged;
        }
    }
}
