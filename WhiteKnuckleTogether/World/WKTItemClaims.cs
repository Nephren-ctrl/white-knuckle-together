using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Client only: the host's items the local player took into a hand that the host has not given them yet. Asked for by
    /// handle once bound (pending), or by key while the host's bind is still on its way (picked before bind). Until the host
    /// answers, a drop of such an item is held back: if the host says no (someone else was first), the item was never the
    /// client's, and a drop already sent would have left a second copy in the world.
    /// </summary>
    internal sealed class WKTItemClaims<TItem> where TItem : class {
        /// <summary>How long a pickup waits for the host's bind before it is given up: the host had no such item.</summary>
        public const double BIND_TIMEOUT = 5d;
        private readonly Dictionary<ushort, TItem> m_pending = new();
        private readonly Dictionary<ulong, (TItem Item, double Time)> m_beforeBind = new();
        private readonly List<ulong> m_expired = new();

        public int PendingCount => m_pending.Count;
        public int BeforeBindCount => m_beforeBind.Count;

        /// <summary>The pickup of <paramref name="item"/> was asked for by <paramref name="handle"/>.</summary>
        public void AddPending(ushort handle, TItem item) => m_pending[handle] = item;

        /// <summary>The host answered for <paramref name="handle"/>. False if nothing was asked for by it.</summary>
        public bool TryResolve(ushort handle, out TItem item) => m_pending.Remove(handle, out item!);

        /// <summary><paramref name="item"/> was taken at <paramref name="now"/> before the host bound it under <paramref name="key"/>.</summary>
        public void AddBeforeBind(ulong key, TItem item, double now) => m_beforeBind[key] = (item, now);

        /// <summary>The host's bind of <paramref name="key"/> arrived. False if nothing waits for it.</summary>
        public bool TryTakeBeforeBind(ulong key, out TItem item) {
            if (m_beforeBind.Remove(key, out var entry)) {
                item = entry.Item;
                return true;
            }
            item = null!;
            return false;
        }

        /// <summary>Whether the host has yet to answer for <paramref name="item"/>, so a drop of it must wait.</summary>
        public bool IsUnresolved(TItem item) {
            foreach (var pending in m_pending.Values) {
                if (ReferenceEquals(pending, item)) return true;
            }
            foreach (var entry in m_beforeBind.Values) {
                if (ReferenceEquals(entry.Item, item)) return true;
            }
            return false;
        }

        /// <summary>Gives up the pickups whose bind never came, so their items are the client's own and drop normally.</summary>
        public void ExpireBeforeBind(double now) {
            foreach (var pair in m_beforeBind) {
                if (now - pair.Value.Time >= BIND_TIMEOUT)
                    m_expired.Add(pair.Key);
            }
            foreach (var key in m_expired)
                m_beforeBind.Remove(key);
            m_expired.Clear();
        }

        public void ClearBeforeBind() => m_beforeBind.Clear();

        public void Clear() {
            m_pending.Clear();
            m_beforeBind.Clear();
        }
    }
}
