using System;
using System.Collections.Generic;
using HarmonyLib;
using Steamworks.Data;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using Object = UnityEngine.Object;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Enemy projectiles in a synced co-op run (a sturge's nails, a spitter's acid, a grub aunt's spikes). The machine
    /// simulating the shooter fires the real shot; every other machine shows a copy that only hits its own player
    /// (see the projectile patches), sent through the host:
    /// <list type="bullet">
    /// <item>A shot is sent the frame after the game sets it up, with its final damage and whether it is held in place
    /// (<c>projectileShootHold</c>). Letting a held shot go sends where it flies off to.</item>
    /// <item>A shot that stays where it hits (<c>destroyOnHit</c> off, such as a grub aunt's climbable spike) sends where it
    /// came to rest, and every copy moves there; its handholds are then shared like other runtime objects. When the real
    /// shot goes, its copies go too. The host keeps the resting shots for players who join later.</item>
    /// </list>
    /// Owned by <see cref="WKTWorldSync"/>.
    /// </summary>
    internal sealed class WKTEnemyShots {
        /// <summary>A copy of a shot that stays put outlives the real one by this much at most, in case its end is never heard.</summary>
        private const float c_copy_lifetime_margin = 30f;
        /// <summary>How far around a resting shot a copy looks for the surface it rests on.</summary>
        private const float c_surface_probe = 0.6f;
        private static readonly HashSet<Projectile> s_copies = new();
        private static readonly Dictionary<(Type, string), Projectile?> s_prefabs = new();
        private static readonly HashSet<string> s_missingPrefabs = new();
        private static readonly AccessTools.FieldRef<Projectile, bool> s_hasHitSurface = AccessTools.FieldRefAccess<Projectile, bool>("hasHitSurface");
        private static readonly AccessTools.FieldRef<Projectile, Vector3> s_velocity = AccessTools.FieldRefAccess<Projectile, Vector3>("velocity");
        private static readonly AccessTools.FieldRef<Projectile, float> s_velocityMultiplier = AccessTools.FieldRefAccess<Projectile, float>("velMult");
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        private readonly Dictionary<uint, Shot> m_shots = new();
        /// <summary>This machine's own shots, by their projectile's instance id.</summary>
        private readonly Dictionary<int, uint> m_ownIds = new();
        /// <summary>Own shots fired this frame, sent once their attack has finished setting them up.</summary>
        private readonly List<Shot> m_pending = new();
        private readonly List<uint> m_scratch = new();
        private uint m_sequence;

        public WKTEnemyShots(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
        }

        /// <summary>Whether a projectile is a copy of another machine's shot, which only hits the local player.</summary>
        public static bool IsCopy(Projectile projectile) => s_copies.Count != 0 && s_copies.Contains(projectile);

        public void Clear() {
            s_copies.Clear();
            m_shots.Clear();
            m_ownIds.Clear();
            m_pending.Clear();
            m_sequence = 0;
        }

        #region Own shots

        /// <summary>
        /// The game set up a projectile (<c>Projectile.Initialize</c>). An enemy this machine simulates fired it, or let a held
        /// shot go. A shot from a creature another machine simulates is not shown: that machine fires its own, which every
        /// machine sees, so one fired here (an attack running on as the creature changed hands) is removed.
        /// </summary>
        public void OnInitialize(Projectile projectile, GameEntity? shooter, Vector3 direction) {
            if (shooter == null || IsCopy(projectile)) return;
            if (m_ownIds.TryGetValue(projectile.GetInstanceID(), out var ownId)) {
                OnReleased(ownId, projectile, direction);
                return;
            }
            if (!shooter.TryGetComponent<WKTNetIdentity>(out var identity) || identity.Handle == 0) return;
            if (identity.IsPuppet || !m_sync.Authority.SimulatesHere(identity)) {
                Object.Destroy(projectile.gameObject);
                return;
            }
            if (m_network.LocalPlayer is not { } local) return;
            m_sequence = WKTShotId.Next(m_sequence);
            var shot = new Shot(WKTShotId.Of(local.Id, m_sequence), projectile, true, !projectile.destroyOnHit, identity.Handle,
                WKTPrefabSearch.GetPrefabName(projectile.name)) { Direction = Vector3.ClampMagnitude(direction, 1f) };
            m_shots[shot.Id] = shot;
            m_ownIds[shot.InstanceId] = shot.Id;
            m_pending.Add(shot);
        }

        /// <summary>A held shot of this machine's was let go.</summary>
        private void OnReleased(uint shotId, Projectile projectile, Vector3 direction) {
            if (!m_shots.TryGetValue(shotId, out var shot)) return;
            shot.Direction = Vector3.ClampMagnitude(direction, 1f);
            // Let go before it was ever sent: it is sent as an ordinary shot flying off.
            if (m_pending.Remove(shot)) {
                SendFired(shot, false);
                return;
            }
            var writer = m_network.BeginMessage(WKTMessageId.ProjectileReleased);
            WKTShotPose.Released(shotId, WKTWorldOffset.ToWire(projectile.transform.position, WKTWorldOffset.Get()), shot.Direction).WriteReleased(writer);
            Send(writer, null);
        }

        /// <summary>Sends the shots fired last frame, and notices own shots coming to rest or going.</summary>
        public void Update() {
            foreach (var pending in m_pending) {
                if (pending.Projectile != null)
                    SendFired(pending, s_velocityMultiplier(pending.Projectile) == 0f);
            }
            m_pending.Clear();
            if (m_shots.Count == 0) return;
            m_scratch.Clear();
            foreach (var pair in m_shots) {
                var shot = pair.Value;
                if (shot.Projectile == null) {
                    if (shot.Own) {
                        if (shot.Sticks)
                            SendGone(shot.Id, null);
                        m_scratch.Add(shot.Id);
                    }
                    // The host keeps a resting shot for players joining later until its end is heard, unless its machine left.
                    else if (!m_sync.IsHost || !shot.Stuck || !m_network.TryGetPlayer(WKTShotId.OriginOf(shot.Id), out _))
                        m_scratch.Add(shot.Id);
                    continue;
                }
                if (shot.Own && shot.Sticks && !shot.Stuck && shot.Projectile.HasHitSurface())
                    OnOwnStuck(shot);
            }
            foreach (var shotId in m_scratch)
                Forget(shotId);
            m_scratch.Clear();
        }

        private void OnOwnStuck(Shot shot) {
            var shotTransform = shot.Projectile!.transform;
            shot.Stuck = true;
            shot.RestPosition = WKTWorldOffset.ToWire(shotTransform.position, WKTWorldOffset.Get());
            shot.RestRotation = shotTransform.rotation;
            ShareHandholds(shot);
            var writer = m_network.BeginMessage(WKTMessageId.ProjectileStuck);
            shot.StuckPose.WriteStuck(writer);
            Send(writer, null);
        }

        private void SendFired(Shot shot, bool held) {
            var projectile = shot.Projectile!;
            var shotTransform = projectile.transform;
            var writer = m_network.BeginMessage(WKTMessageId.ProjectileFired);
            new WKTShotFired(shot.Id, shot.Shooter, shot.Prefab, WKTWorldOffset.ToWire(shotTransform.position, WKTWorldOffset.Get()), shotTransform.rotation,
                shot.Direction, Mathf.Clamp(projectile.damage, 0f, WKTWire.MAX_AMOUNT), held).Write(writer);
            Send(writer, null);
        }

        private void SendGone(uint shotId, WKTPlayer? except) {
            var writer = m_network.BeginMessage(WKTMessageId.ProjectileGone);
            writer.Put(shotId);
            Send(writer, except);
        }

        /// <summary>From the host to every client but <paramref name="except"/>; from a client to the host.</summary>
        private void Send(NetDataWriter writer, WKTPlayer? except) {
            if (m_sync.IsHost)
                m_network.Broadcast(writer, SendType.Reliable, except);
            else
                m_network.SendToHost(writer, SendType.Reliable);
        }

        #endregion

        #region Other machines' shots

        /// <summary>
        /// Whether a shot message from <paramref name="sender"/> may be acted on: the host takes a client's own shots, a client
        /// takes the host's.
        /// </summary>
        private bool Accepts(WKTPlayer sender, uint shotId) =>
            m_sync.IsHost ? !sender.IsHost && WKTShotId.OriginOf(shotId) == sender.Id : sender.IsHost && WKTShotId.OriginOf(shotId) != m_network.LocalPlayer?.Id;

        public void OnFired(WKTPlayer sender, NetDataReader reader) {
            if (!WKTShotFired.TryRead(reader, out var fired) || !Accepts(sender, fired.ShotId) || m_shots.ContainsKey(fired.ShotId)) return;
            if (!m_sync.TryGetIdentity(fired.Shooter, out var identity) || identity!.Entity == null || !identity.IsPuppet) return;
            if (m_sync.IsHost) {
                // Only the client simulating the shooter fires its shots.
                if (identity.Owner != sender.Id) return;
                var relay = m_network.BeginMessage(WKTMessageId.ProjectileFired);
                fired.Write(relay);
                Send(relay, sender);
            }
            var copy = SpawnCopy(fired.ShotId, identity.Entity, fired.Prefab, fired.Position, fired.Rotation);
            if (copy == null) return;
            copy.damage = fired.Damage;
            copy.Initialize(fired.Direction, identity.Entity);
            if (fired.Held)
                copy.SetVelocityMultiplier(0f);
        }

        public void OnReleased(WKTPlayer sender, NetDataReader reader) {
            if (!WKTShotPose.TryReadReleased(reader, out var released) || !Accepts(sender, released.ShotId)) return;
            if (m_sync.IsHost) {
                var relay = m_network.BeginMessage(WKTMessageId.ProjectileReleased);
                released.WriteReleased(relay);
                Send(relay, sender);
            }
            if (!m_shots.TryGetValue(released.ShotId, out var shot) || shot.Projectile == null) return;
            var copy = shot.Projectile;
            copy.transform.position = WKTWorldOffset.FromWire(released.Position, WKTWorldOffset.Get());
            copy.SetVelocityMultiplier(1f);
            m_sync.TryGetIdentity(shot.Shooter, out var identity);
            copy.Initialize(released.Direction, identity != null ? identity.Entity : null);
        }

        public void OnStuck(WKTPlayer sender, NetDataReader reader) {
            if (!WKTShotPose.TryReadStuck(reader, out var stuck) || !Accepts(sender, stuck.ShotId)) return;
            if (m_sync.IsHost) {
                var relay = m_network.BeginMessage(WKTMessageId.ProjectileStuck);
                stuck.WriteStuck(relay);
                Send(relay, sender);
            }
            if (!m_shots.TryGetValue(stuck.ShotId, out var shot) || shot.Projectile == null) {
                // Missed, or this player joined after it was fired: built where it rests.
                m_sync.TryGetIdentity(stuck.Shooter, out var identity);
                var built = SpawnCopy(stuck.ShotId, identity != null ? identity.Entity : null, stuck.Prefab, stuck.Position, stuck.Rotation);
                if (built == null || !m_shots.TryGetValue(stuck.ShotId, out shot)) return;
                built.Initialize(built.transform.forward, identity != null ? identity.Entity : null);
            }
            shot.Stuck = true;
            shot.RestPosition = stuck.Position;
            shot.RestRotation = stuck.Rotation;
            Rest(shot.Projectile!, WKTWorldOffset.FromWire(stuck.Position, WKTWorldOffset.Get()), stuck.Rotation);
            ShareHandholds(shot);
        }

        public void OnGone(WKTPlayer sender, NetDataReader reader) {
            if (!reader.TryGetUInt(out var shotId) || shotId == 0 || !Accepts(sender, shotId)) return;
            if (m_sync.IsHost)
                SendGone(shotId, sender);
            if (m_shots.TryGetValue(shotId, out var shot) && shot.Projectile != null)
                Object.Destroy(shot.Projectile.gameObject);
            Forget(shotId);
        }

        /// <summary>Host only: the shots resting in the world, for a player who just joined.</summary>
        public void SendAll(WKTPlayer player) {
            foreach (var shot in m_shots.Values) {
                if (!shot.Stuck) continue;
                var writer = m_network.BeginMessage(WKTMessageId.ProjectileStuck);
                shot.StuckPose.WriteStuck(writer);
                m_network.SendToPlayer(player, writer, SendType.Reliable);
            }
        }

        /// <summary>Builds a copy of another machine's shot, which shows it but only hits the local player.</summary>
        private Projectile? SpawnCopy(uint shotId, GameEntity? shooter, string prefabName, Vector3 wirePosition, Quaternion rotation) {
            var prefab = FindPrefab(shooter, prefabName);
            if (prefab == null) return null;
            var copy = Object.Instantiate(prefab, WKTWorldOffset.FromWire(wirePosition, WKTWorldOffset.Get()), rotation);
            if (s_copies.Count > 256)
                s_copies.RemoveWhere(p => p == null);
            s_copies.Add(copy);
            if (shooter != null)
                copy.AddIgnore(shooter.gameObject);
            var sticks = !copy.destroyOnHit;
            // A copy that stays put goes when the real shot does, not by its own clock.
            if (sticks)
                copy.SetLifetime(copy.lifetime * 2f + c_copy_lifetime_margin);
            m_shots[shotId] = new Shot(shotId, copy, false, sticks, shooter != null && shooter.TryGetComponent<WKTNetIdentity>(out var identity) ? identity.Handle : (ushort)0,
                prefabName);
            return copy;
        }

        /// <summary>Puts a copy where the real shot rests, held there by the surface under it as the game holds a stuck shot.</summary>
        private static void Rest(Projectile copy, Vector3 position, Quaternion rotation) {
            var copyTransform = copy.transform;
            copyTransform.SetPositionAndRotation(position, rotation);
            s_velocity(copy) = Vector3.zero;
            s_hasHitSurface(copy) = true;
            copy.SetVelocityMultiplier(0f);
            if (copy.TryGetComponent<UT_SoftParent>(out var softParent)) {
                if (softParent.parent != null)
                    softParent.Initialize(softParent.parent);
                return;
            }
            var forward = rotation * Vector3.forward;
            if (!Physics.Raycast(position - forward * c_surface_probe, forward, out var hit, c_surface_probe * 2f, copy.hitMask, QueryTriggerInteraction.Ignore)) return;
            softParent = copy.gameObject.AddComponent<UT_SoftParent>();
            softParent.UseLateUpdate = true;
            softParent.Initialize(hit.collider.transform);
        }

        private void ShareHandholds(Shot shot) {
            if (shot.Projectile != null)
                m_sync.Handholds.RegisterRoot(shot.Projectile.gameObject, WKTHandholdKeys.GetRootKey(WKTHandholdKeys.EnemyShotSalt, shot.Id));
        }

        private void Forget(uint shotId) {
            if (!m_shots.Remove(shotId, out var shot)) return;
            if (shot.Own)
                m_ownIds.Remove(shot.InstanceId);
        }

        /// <summary>
        /// The projectile prefab a shooter fires by this name, from the shooter's own references, else (a shot resting from a
        /// shooter that is gone) among the loaded prefabs. Searched once per shooter type and name.
        /// </summary>
        private static Projectile? FindPrefab(GameEntity? shooter, string name) {
            var cacheKey = (shooter != null ? shooter.GetType() : typeof(Projectile), name);
            if (s_prefabs.TryGetValue(cacheKey, out var cached) && cached != null) return cached;
            var found = shooter != null ? WKTPrefabSearch.FindProjectile(shooter.gameObject, name) : FindLoadedPrefab(name);
            s_prefabs[cacheKey] = found;
            if (found == null && s_missingPrefabs.Add(name))
                WKT.Instance.Logger.LogError($"Cannot show the projectile {name} of {(shooter != null ? shooter.GetType().Name : "a creature that is gone")}: no such prefab.");
            return found;
        }

        private static Projectile? FindLoadedPrefab(string name) {
            foreach (var projectile in Resources.FindObjectsOfTypeAll<Projectile>()) {
                if (projectile != null && projectile.name == name && !projectile.gameObject.scene.IsValid())
                    return projectile;
            }
            return null;
        }

        #endregion

        private sealed class Shot {
            public readonly uint Id;
            public readonly Projectile? Projectile;
            public readonly int InstanceId;
            /// <summary>Fired here: the real shot. Otherwise a copy.</summary>
            public readonly bool Own;
            /// <summary>It stays where it hits.</summary>
            public readonly bool Sticks;
            public readonly ushort Shooter;
            public readonly string Prefab;
            public Vector3 Direction;
            public bool Stuck;
            public Vector3 RestPosition;
            public Quaternion RestRotation;

            public Shot(uint id, Projectile projectile, bool own, bool sticks, ushort shooter, string prefab) {
                Id = id;
                Projectile = projectile;
                InstanceId = projectile.GetInstanceID();
                Own = own;
                Sticks = sticks;
                Shooter = shooter;
                Prefab = prefab;
            }

            public WKTShotPose StuckPose => WKTShotPose.Stuck(Id, Shooter, Prefab, RestPosition, RestRotation);
        }
    }
}
