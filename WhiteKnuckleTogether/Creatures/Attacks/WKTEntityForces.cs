using System.Collections.Generic;
using Steamworks.Data;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Pushes given to a creature another machine simulates, in a synced co-op run. A puppet's body is kinematic and its pose
    /// comes from that machine, so a push here (a kick, a hit's knockback, a thrown prop, an explosion) would do nothing. It
    /// goes to the simulating machine instead, which pushes its own creature, such as tipping a turret over. Pushes are summed
    /// per creature and sent once a frame. Props keep their own authority (<see cref="WKTSharedHolds"/>), and pushes inside a
    /// <see cref="WKTLocalReplayScope"/> stay local, since every machine replays those. Owned by <see cref="WKTWorldSync"/>.
    /// </summary>
    internal sealed class WKTEntityForces {
        /// <summary>The most pushes at a point sent a frame; the rest are summed into plain pushes.</summary>
        private const int c_max_point_forces = 16;
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        private readonly Dictionary<ushort, Vector3> m_sums = new();
        private readonly List<WKTEntityForce> m_pointForces = new();
        private bool m_applying;

        public WKTEntityForces(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
        }

        /// <summary>A creature is about to be pushed. False skips the game's push, for a puppet whose simulating machine pushes it.</summary>
        public static bool BeforeForce(GameEntity entity, Vector3 force, bool atPosition, Vector3 position) {
            var forces = WKTWorldSync.Instance?.Forces;
            if (forces == null || forces.m_applying || entity is CL_Prop || !WKTWorldSync.IsPuppet(entity) || WKTLocalReplayScope.IsOpen ||
                WKTPuppetHitScope.IsOpen || !entity.TryGetComponent<WKTNetIdentity>(out var identity) || identity.Handle == 0) return true;
            forces.Queue(identity.Handle, force, atPosition, position);
            return false;
        }

        private void Queue(ushort handle, Vector3 force, bool atPosition, Vector3 position) {
            if (atPosition && m_pointForces.Count < c_max_point_forces) {
                m_pointForces.Add(new WKTEntityForce(handle, force, true, WKTWorldOffset.ToWire(position, WKTWorldOffset.Get())));
                return;
            }
            m_sums[handle] = m_sums.TryGetValue(handle, out var sum) ? sum + force : force;
        }

        /// <summary>Sends this frame's pushes: a client's to the host, the host's to each pushed creature's client.</summary>
        public void Flush() {
            if (m_sums.Count == 0 && m_pointForces.Count == 0) return;
            foreach (var pair in m_sums) {
                if (pair.Value != Vector3.zero)
                    Send(new WKTEntityForce(pair.Key, pair.Value));
            }
            foreach (var force in m_pointForces)
                Send(force);
            m_sums.Clear();
            m_pointForces.Clear();
        }

        private void Send(in WKTEntityForce force) {
            if (!m_sync.IsHost) {
                var writer = m_network.BeginMessage(WKTMessageId.EntityForce);
                force.Write(writer);
                m_network.SendToHost(writer, SendType.Reliable);
                return;
            }
            if (m_sync.TryGetIdentity(force.Handle, out var identity))
                SendToOwner(identity!, force);
        }

        private void SendToOwner(WKTNetIdentity identity, in WKTEntityForce force) {
            if (identity.Owner == WKTEntityAuthority.HOST || !m_network.TryGetPlayer(identity.Owner, out var owner) || owner == null) return;
            var writer = m_network.BeginMessage(WKTMessageId.EntityForce);
            force.Write(writer);
            m_network.SendToPlayer(owner, writer, SendType.Reliable);
        }

        /// <summary>A push for a creature this machine simulates, or on the host one to pass on to the client that does.</summary>
        public void OnForce(WKTPlayer sender, NetDataReader reader) {
            if (!WKTEntityForce.TryRead(reader, out var force) || !m_sync.TryGetIdentity(force.Handle, out var identity)) return;
            var entity = identity!.Entity;
            if (entity == null || entity.dead) return;
            if (m_sync.IsHost) {
                if (sender.IsHost) return;
                if (identity.IsPuppet) {
                    if (identity.Owner != sender.Id)
                        SendToOwner(identity, force);
                    return;
                }
            }
            else if (!sender.IsHost || identity.IsPuppet) return;
            m_applying = true;
            try {
                if (force.AtPosition)
                    entity.AddForceAtPosition(force.Force, WKTWorldOffset.FromWire(force.Position, WKTWorldOffset.Get()));
                else
                    entity.AddForce(force.Force);
            }
            finally {
                m_applying = false;
            }
        }

        public void Clear() {
            m_sums.Clear();
            m_pointForces.Clear();
            m_applying = false;
        }
    }
}
