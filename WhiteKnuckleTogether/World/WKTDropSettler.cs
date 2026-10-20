using System.Collections.Generic;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Host only: places an item a client dropped so it cannot fall out of the world. The client's sample was taken while
    /// its copy was still growing in from a tenth of its size, often right against a surface, and the host spawns it at full
    /// size at once, sometimes where the host has not loaded the level's floor yet. So the host's copy is pushed out of
    /// whatever it starts inside, sweeps its path (continuous collision) while it settles, and waits, held still, until
    /// there is ground under it.
    /// </summary>
    internal sealed class WKTDropSettler {
        /// <summary>How long a dropped item sweeps its path before it goes back to its own collision mode.</summary>
        public const double SWEEP_SECONDS = 2d;
        /// <summary>The longest a dropped item is held still waiting for ground under it before it is let fall anyway.</summary>
        public const double GROUND_WAIT_SECONDS = 10d;
        /// <summary>How far below a dropped item the ground must be for it to fall.</summary>
        public const float GROUND_PROBE = 200f;
        private const int c_depenetration_passes = 3;
        private const int c_max_overlaps = 16;
        private static readonly string[] s_worldLayers = { "Default", "World", "Platform", "WorldNoBlockSight" };
        private readonly List<Settling> m_settling = new();
        private readonly List<Collider> m_colliders = new();
        private readonly Collider[] m_overlaps = new Collider[c_max_overlaps];
        private int m_worldMask = -1;

        private int WorldMask => m_worldMask >= 0 ? m_worldMask : m_worldMask = LayerMask.GetMask(s_worldLayers);

        /// <summary>Places a just spawned drop. <paramref name="velocity"/> is the client's throw, applied once it may fall.</summary>
        public void Place(Rigidbody body, bool kinematic, Vector3 velocity, double now) {
            Depenetrate(body);
            if (kinematic) return;
            var hasGround = HasGround(body.position);
            m_settling.Add(new Settling(body, body.collisionDetectionMode, now, hasGround ? now : -1d, velocity));
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            if (hasGround)
                body.linearVelocity = velocity;
            else
                body.isKinematic = true;
        }

        public void Update(double now) {
            for (var i = m_settling.Count - 1; i >= 0; i--) {
                var settling = m_settling[i];
                var body = settling.Body;
                if (body == null) {
                    m_settling.RemoveAt(i);
                    continue;
                }
                if (settling.ReleasedAt < 0d) {
                    if (!HasGround(body.position) && now - settling.SpawnedAt < GROUND_WAIT_SECONDS) continue;
                    body.isKinematic = false;
                    body.linearVelocity = settling.Velocity;
                    m_settling[i] = settling.Released(now);
                    continue;
                }
                if (now - settling.ReleasedAt < SWEEP_SECONDS) continue;
                body.collisionDetectionMode = settling.Mode;
                m_settling.RemoveAt(i);
            }
        }

        public void Clear() => m_settling.Clear();

        private bool HasGround(Vector3 position) =>
            Physics.Raycast(position, Vector3.down, GROUND_PROBE, WorldMask, QueryTriggerInteraction.Ignore);

        /// <summary>Moves the item out of the world geometry its colliders start inside.</summary>
        private void Depenetrate(Rigidbody body) {
            body.GetComponentsInChildren(false, m_colliders);
            try {
                Physics.SyncTransforms();
                for (var pass = 0; pass < c_depenetration_passes; pass++) {
                    var moved = false;
                    foreach (var collider in m_colliders) {
                        if (collider == null || collider.isTrigger || !collider.enabled) continue;
                        var bounds = collider.bounds;
                        var count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents, m_overlaps, Quaternion.identity, WorldMask, QueryTriggerInteraction.Ignore);
                        for (var i = 0; i < count; i++) {
                            var other = m_overlaps[i];
                            if (other == null || other.attachedRigidbody == body) continue;
                            var colliderTransform = collider.transform;
                            var otherTransform = other.transform;
                            if (!Physics.ComputePenetration(collider, colliderTransform.position, colliderTransform.rotation, other, otherTransform.position,
                                    otherTransform.rotation, out var direction, out var distance)) continue;
                            body.transform.position += direction * distance;
                            moved = true;
                        }
                    }
                    if (!moved) break;
                    Physics.SyncTransforms();
                }
            }
            finally {
                m_colliders.Clear();
            }
        }

        private readonly struct Settling {
            public readonly Rigidbody Body;
            public readonly CollisionDetectionMode Mode;
            public readonly double SpawnedAt;
            /// <summary>When it was let fall, or -1 while held still for ground under it.</summary>
            public readonly double ReleasedAt;
            public readonly Vector3 Velocity;

            public Settling(Rigidbody body, CollisionDetectionMode mode, double spawnedAt, double releasedAt, Vector3 velocity) {
                Body = body;
                Mode = mode;
                SpawnedAt = spawnedAt;
                ReleasedAt = releasedAt;
                Velocity = velocity;
            }

            public Settling Released(double now) => new(Body, Mode, SpawnedAt, now, Velocity);
        }
    }
}
