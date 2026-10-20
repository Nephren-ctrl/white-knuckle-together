using System.Collections.Generic;
using Steamworks.Data;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Creatures a player kills by touching them, decided on that player's own screen in a synced co-op run, like enemy hits:
    /// <list type="bullet">
    /// <item>A gasbag pops when a player comes close, launching them upward when they come from above
    /// (<c>DEN_Gasbag.ExplodeScan</c>, the game's own code, run on the puppet against the local player).</item>
    /// <item>A roach or a critter is squished under a player's feet (from <c>DEN_Roach.AI</c> and <c>DEN_Critter.AI</c>).</item>
    /// </list>
    /// The player's machine kills the creature at once, with its sound, launch and score, and claims the kill from the host
    /// (<see cref="WKTPlayerHitKind.Triggered"/>), which kills it for everyone else. The host's own scans ignore remote
    /// players' proxies for these (see the touch kill patches), since those players decide for themselves. Owned by
    /// <see cref="WKTWorldSync"/>.
    /// </summary>
    internal sealed class WKTTouchKills {
        /// <summary>How close a player's body must come to squish a roach or critter, from the game's code.</summary>
        public const float SQUISH_DISTANCE = 0.45f;
        /// <summary>A roach only squishes once it has settled after dropping in (<c>DEN_Roach.dropTime</c>).</summary>
        public const double ROACH_SETTLE_TIME = 2.0;
        /// <summary>Beyond a creature's reach plus this, the player's body cannot touch it, so it is not checked.</summary>
        private const float c_check_margin = 3f;
        /// <summary>A squished roach's score, from <c>DEN_Roach.AI</c>.</summary>
        private const string c_roach_score = "Roaches Smashed";
        private const float c_roach_score_bonus = 10f;
        private const string c_pacifying_perk = "Perk_PheromoneGlands";
        private const string c_pacify_flag = "pacifyroaches";
        /// <summary>Host only: the player whose claim is being carried out, who already saw the squish, or nobody.</summary>
        private static byte s_claimant = WKTEntityAuthority.NOBODY;
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        /// <summary>Puppets that can be touched to death, by handle, with when they were bound.</summary>
        private readonly Dictionary<ushort, double> m_puppets = new();
        private readonly List<ushort> m_scratch = new();

        public WKTTouchKills(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
        }

        public static bool IsTouchKillable(GameEntity? entity) => entity is DEN_Gasbag or DEN_Roach or DEN_Critter;

        public void OnPuppetBound(WKTNetIdentity identity) {
            if (IsTouchKillable(identity.Entity))
                m_puppets[identity.Handle] = Time.realtimeSinceStartupAsDouble;
        }

        public void Clear() {
            m_puppets.Clear();
            s_claimant = WKTEntityAuthority.NOBODY;
        }

        /// <summary>Checks the touchable puppets near the local player, as each creature's own code would on the host.</summary>
        public void Update(double now) {
            if (m_puppets.Count == 0) return;
            var local = ENT_Player.playerObject;
            if (local == null || local.dead || local.cCon == null) return;
            m_scratch.Clear();
            foreach (var pair in m_puppets)
                m_scratch.Add(pair.Key);
            var position = local.transform.position;
            foreach (var handle in m_scratch) {
                if (!m_sync.TryGetIdentity(handle, out var identity) || !identity!.IsPuppet || identity.Entity == null || identity.Entity.dead) {
                    m_puppets.Remove(handle);
                    continue;
                }
                var entity = identity.Entity;
                if (!entity.isActiveAndEnabled) continue;
                var touched = entity switch {
                    DEN_Gasbag gasbag => IsNear(position, gasbag.transform.position, gasbag.explodeDetectRadius) && TryPop(identity),
                    DEN_Roach roach => now - m_puppets[handle] >= ROACH_SETTLE_TIME && TrySquishRoach(local, roach, identity),
                    DEN_Critter critter => TrySquishCritter(local, critter),
                    _ => false
                };
                if (!touched) continue;
                m_puppets.Remove(handle);
                m_sync.EnemyHits.SendHit(handle, 0, WKTPlayerHitKind.Triggered);
            }
            m_scratch.Clear();
        }

        private static bool IsNear(Vector3 player, Vector3 creature, float reach) {
            var range = reach + c_check_margin;
            return (player - creature).sqrMagnitude <= range * range;
        }

        /// <summary>The gasbag's own scan, which only finds the local player here: it pops, launching and hurting them.</summary>
        private bool TryPop(WKTNetIdentity identity) => m_sync.RunPredictedKill(identity, static entity => ((DEN_Gasbag)entity).ExplodeScan());

        /// <summary><c>DEN_Roach.AI</c>'s squish, against the local player only.</summary>
        private bool TrySquishRoach(ENT_Player local, DEN_Roach roach, WKTNetIdentity identity) {
            if (!roach.canSquish || local.HasPerk(c_pacifying_perk) || CL_GameManager.HasActiveFlag(c_pacify_flag)) return false;
            var tagger = local.GetTagger();
            if (tagger == null || !tagger.HasTagInList(roach.fearTags) || !IsUnderfoot(local, roach.transform.position)) return false;
            if (!m_sync.RunPredictedKill(identity, static entity => entity.Kill("", null))) return false;
            CL_ScoreManager.sessionScore?.AddScore(c_roach_score, c_roach_score, c_roach_score_bonus, 0f, 1);
            return true;
        }

        /// <summary><c>DEN_Critter.AI</c>'s squish, against the local player only: its death effect, and it is gone here.</summary>
        private static bool TrySquishCritter(ENT_Player local, DEN_Critter critter) {
            if (!critter.fearPlayer || (critter.playerMask.value & (1 << local.gameObject.layer)) == 0 || !IsUnderfoot(local, critter.transform.position)) return false;
            ShowSquish(critter);
            return true;
        }

        private static bool IsUnderfoot(ENT_Player local, Vector3 creature) =>
            Vector3.Distance(local.cCon.ClosestPoint(creature), creature) < SQUISH_DISTANCE;

        private static void ShowSquish(DEN_Critter critter) {
            if (critter.deathEffect != null)
                Object.Instantiate(critter.deathEffect, critter.transform.position, Quaternion.identity);
            critter.gameObject.SetActive(false);
        }

        /// <summary>
        /// Host only: a client touched a creature to death on its screen. The host kills it as the creature's own code would,
        /// which every other player then sees.
        /// </summary>
        public void OnClaim(WKTPlayer sender, WKTNetIdentity identity) {
            var entity = identity.Entity;
            if (entity == null || entity.dead || identity.IsPuppet || !IsTouchKillable(entity)) return;
            s_claimant = sender.Id;
            try {
                if (entity is DEN_Critter critter) {
                    Object.Instantiate(critter.deathEffect, critter.transform.position, Quaternion.identity);
                    DestroySquished(critter.gameObject);
                }
                else
                    entity.Kill("", null);
            }
            finally {
                s_claimant = WKTEntityAuthority.NOBODY;
            }
        }

        /// <summary>
        /// Replaces the squished critter's <c>Object.Destroy(gameObject)</c> in <c>DEN_Critter.AI</c>. The host tells the other
        /// players first, so they show its death effect before it goes; its destruction then removes it everywhere.
        /// </summary>
        public static void DestroySquished(Object target) {
            var sync = WKTWorldSync.Instance;
            if (sync != null && sync.IsHost && target is GameObject gameObject && gameObject.TryGetComponent<WKTNetIdentity>(out var identity) && identity.Handle != 0)
                sync.TouchKills.BroadcastSquish(identity.Handle);
            Object.Destroy(target);
        }

        private void BroadcastSquish(ushort handle) {
            var writer = m_network.BeginMessage(WKTMessageId.PlayerHit);
            new WKTPlayerHit(handle, WKTPlayerHitKind.Triggered, 0).Write(writer);
            var claimant = s_claimant != WKTEntityAuthority.NOBODY && m_network.TryGetPlayer(s_claimant, out var player) ? player : null;
            m_network.Broadcast(writer, SendType.Reliable, claimant);
        }

        /// <summary>Client only: the host says a critter was squished. Its death effect shows here, before it is removed.</summary>
        public void OnRemoteSquish(WKTNetIdentity identity) {
            if (identity.IsPuppet && identity.Entity is DEN_Critter critter && critter.gameObject.activeSelf)
                ShowSquish(critter);
        }
    }
}
