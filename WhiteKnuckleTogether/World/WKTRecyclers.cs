using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Recyclers in a synced co-op run (<c>ENV_Recycler</c>). Every machine would run its own against its own copy of each
    /// item and pay its own player, so one recycled roach would pay everyone. Only the host's recyclers run; an item's
    /// roaches go to the player who last dropped it into the world (the host for anything else), through a player effect
    /// when that is a remote player. Owned by <see cref="WKTWorldSync"/>.
    /// </summary>
    internal sealed class WKTRecyclers {
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        /// <summary>Host only: who last dropped each item into the world, by handle.</summary>
        private readonly Dictionary<ushort, byte> m_droppers = new();
        /// <summary>Host only: for each recycler, by instance id, whom its pending payouts are for, oldest first.</summary>
        private readonly Dictionary<int, Queue<byte>> m_payouts = new();
        /// <summary>The recycler whose Update is running, while it does.</summary>
        private ENV_Recycler? m_running;

        public WKTRecyclers(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
        }

        /// <summary>Whom a payout is for: a remote dropper, else the host's own player.</summary>
        public static byte PayeeOf(bool hasDropper, byte dropper, byte hostId) => hasDropper ? dropper : hostId;

        public void Clear() {
            m_droppers.Clear();
            m_payouts.Clear();
            m_running = null;
        }

        public void OnDropped(ushort handle, byte playerId) {
            if (handle != 0)
                m_droppers[handle] = playerId;
        }

        public void Forget(ushort handle) => m_droppers.Remove(handle);

        /// <summary>A recycler is about to run its Update. False skips it: only the host's recyclers run.</summary>
        public bool BeforeUpdate(ENV_Recycler recycler) {
            if (!m_sync.IsHost) return false;
            m_running = recycler;
            return true;
        }

        public void AfterUpdate() => m_running = null;

        /// <summary>An item is being destroyed. Inside a recycler's Update that is the item it just took, whose payout follows.</summary>
        public void OnItemDestroyed(Item item) {
            if (m_running == null || item.hasBeenDestroyed) return;
            var dropObject = item.GetDropObject(false);
            var hasDropper = false;
            byte dropper = 0;
            if (dropObject != null && dropObject.TryGetComponent<WKTNetIdentity>(out var identity) && identity.Handle != 0)
                hasDropper = m_droppers.TryGetValue(identity.Handle, out dropper);
            var id = m_running.GetInstanceID();
            if (!m_payouts.TryGetValue(id, out var queue)) {
                queue = new Queue<byte>();
                m_payouts[id] = queue;
            }
            queue.Enqueue(PayeeOf(hasDropper, dropper, m_network.LocalPlayer?.Id ?? WKTEntityAuthority.NOBODY));
        }

        /// <summary>A recycler pays out, a few seconds after taking an item: to the local player, or to the remote one it was for.</summary>
        public void Pay(ENV_Recycler recycler, int amount, bool global) {
            // Its cash sound follows this call on the host's copy alone.
            m_sync.Hazards.OnRecycler(recycler, WKTHazardCall.RecyclerCash);
            var payee = m_payouts.TryGetValue(recycler.GetInstanceID(), out var queue) && queue.Count > 0 ? queue.Dequeue() : WKTEntityAuthority.NOBODY;
            if (payee == WKTEntityAuthority.NOBODY || payee == m_network.LocalPlayer?.Id) {
                CL_GameManager.AddRoaches(amount, global);
                return;
            }
            WKTTargets.SendEffect(payee, new WKTPlayerEffect(WKTPlayerEffectId.AddRoaches, amount));
        }
    }
}
