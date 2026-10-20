using System.Collections.Generic;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The thrown return rebars of a synced co-op run, kept the same on every machine so the host can replay the stuck ones to
    /// late joiners and a client that takes over as host already has them. Each rebar is named by its thrower
    /// (<see cref="WKTPitonRegistry.MakeId"/>).
    /// <para>
    /// One machine decides each rebar at a time (<see cref="GetAuthority"/>): whoever it flies back to, else its living
    /// thrower, else the host. Only the thrower may recall it, unless they are dead or gone: then anyone alive may claim it,
    /// first come first served. Pure, so it can be tested without Unity.
    /// </para>
    /// </summary>
    internal sealed class WKTReturnRebarRegistry {
        public const byte NOBODY = byte.MaxValue;
        private readonly Dictionary<uint, Entry> m_rebars = new();
        private readonly HashSet<uint> m_removed = new();
        private readonly List<uint> m_idScratch = new();

        public int Count => m_rebars.Count;
        public Dictionary<uint, Entry>.ValueCollection Rebars => m_rebars.Values;

        /// <summary>Who decides <paramref name="entry"/> now: where it sticks, when it is gone, and whether it is released.</summary>
        public static byte GetAuthority(Entry entry, byte hostId) {
            if (entry.Recaller != NOBODY) return entry.Recaller;
            return entry.Thrown.IsOwnerless ? hostId : entry.Thrown.Owner;
        }

        /// <summary>Whether a player other than a living thrower may recall <paramref name="entry"/>.</summary>
        public static bool IsClaimable(Entry entry, bool claimerDead, bool ownerDead, bool ownerPresent) =>
            entry.Recaller == NOBODY && !claimerDead && (entry.Thrown.IsOwnerless || ownerDead || !ownerPresent);

        /// <summary>Records a thrown rebar, or one replayed as stuck. False for an id already known or removed, or not the thrower's.</summary>
        public bool Add(WKTReturnRebarThrown thrown) {
            if (thrown.Id == 0 || WKTPitonRegistry.GetPlayer(thrown.Id) != thrown.Owner || m_removed.Contains(thrown.Id) || m_rebars.ContainsKey(thrown.Id))
                return false;
            var entry = new Entry();
            entry.Thrown.CopyFrom(thrown);
            m_rebars.Add(thrown.Id, entry);
            return true;
        }

        public bool TryGet(uint id, out Entry entry) => m_rebars.TryGetValue(id, out entry!);

        /// <summary><paramref name="player"/> says it stuck in a wall. False unless they decide it and nobody is recalling it.</summary>
        public bool Stick(uint id, byte player, byte hostId, ulong levelKey, Vector3 position, Quaternion rotation) {
            if (levelKey == 0 || !m_rebars.TryGetValue(id, out var entry) || entry.Recaller != NOBODY || GetAuthority(entry, hostId) != player) return false;
            var thrown = entry.Thrown;
            thrown.Flags |= WKTReturnRebarThrown.FLAG_STUCK;
            thrown.LevelKey = levelKey;
            thrown.Position = position;
            thrown.Rotation = rotation;
            thrown.Velocity = Vector3.zero;
            return true;
        }

        /// <summary>The thrower recalls their own rebar. False if it is not theirs to recall.</summary>
        public bool OwnerRecall(uint id, byte player, bool ownerDead) {
            if (ownerDead || !m_rebars.TryGetValue(id, out var entry) || entry.Recaller != NOBODY || entry.Thrown.IsOwnerless || entry.Thrown.Owner != player)
                return false;
            SetRecaller(entry, player);
            return true;
        }

        /// <summary>Host only: <paramref name="claimer"/> asks to recall a rebar whose thrower is dead or gone. The first claim wins.</summary>
        public bool Claim(uint id, byte claimer, bool claimerDead, bool ownerDead, bool ownerPresent) {
            if (!m_rebars.TryGetValue(id, out var entry) || !IsClaimable(entry, claimerDead, ownerDead, ownerPresent)) return false;
            SetRecaller(entry, claimer);
            return true;
        }

        /// <summary>Client only: the host says <paramref name="recaller"/> recalls it, as it already checked.</summary>
        public bool Grant(uint id, byte recaller) {
            if (recaller == NOBODY || !m_rebars.TryGetValue(id, out var entry) || entry.Recaller != NOBODY) return false;
            SetRecaller(entry, recaller);
            return true;
        }

        /// <summary>
        /// Whoever it flew back to died, or the machine deciding it left: it flies on with no owner. Only its recaller may release
        /// it, or the host once whoever decides it is gone.
        /// </summary>
        public bool Release(uint id, byte player, byte hostId, bool authorityPresent, Vector3 position, Vector3 velocity) {
            if (!m_rebars.TryGetValue(id, out var entry)) return false;
            var byRecaller = entry.Recaller != NOBODY && player == entry.Recaller;
            var byHost = player == hostId && !authorityPresent;
            if (!byRecaller && !byHost) return false;
            entry.Recaller = NOBODY;
            var thrown = entry.Thrown;
            thrown.Flags = WKTReturnRebarThrown.FLAG_OWNERLESS;
            thrown.LevelKey = 0;
            thrown.Position = position;
            thrown.Velocity = velocity;
            return true;
        }

        /// <summary>Removes a rebar for good. False unless <paramref name="player"/> decides it.</summary>
        public bool Remove(uint id, byte player, byte hostId) {
            if (!m_rebars.TryGetValue(id, out var entry) || GetAuthority(entry, hostId) != player) return false;
            m_rebars.Remove(id);
            m_removed.Add(id);
            return true;
        }

        /// <summary>Forgets the rebars stuck in a level that unloaded.</summary>
        public void ForgetLevel(ulong levelKey) {
            m_idScratch.Clear();
            foreach (var pair in m_rebars) {
                if (pair.Value.Recaller == NOBODY && pair.Value.Thrown.IsStuck && pair.Value.Thrown.LevelKey == levelKey)
                    m_idScratch.Add(pair.Key);
            }
            foreach (var id in m_idScratch)
                m_rebars.Remove(id);
            m_idScratch.Clear();
        }

        /// <summary>Fills <paramref name="ids"/> with every rebar's id, so callers can change the registry while going through them.</summary>
        public void GetIds(List<uint> ids) {
            ids.Clear();
            foreach (var id in m_rebars.Keys)
                ids.Add(id);
        }

        private static void SetRecaller(Entry entry, byte recaller) {
            entry.Recaller = recaller;
            // A rebar flying back is no longer in a wall, so it is not replayed.
            entry.Thrown.Flags = (byte)(entry.Thrown.Flags & ~WKTReturnRebarThrown.FLAG_STUCK);
            entry.Thrown.LevelKey = 0;
        }

        internal sealed class Entry {
            public WKTReturnRebarThrown Thrown { get; } = new();
            /// <summary>Who it flies back to, or <see cref="NOBODY"/>.</summary>
            public byte Recaller { get; set; } = NOBODY;
        }
    }
}
