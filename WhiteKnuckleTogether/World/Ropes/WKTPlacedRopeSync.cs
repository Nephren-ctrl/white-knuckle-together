using System;
using System.Collections.Generic;
using HarmonyLib;
using Steamworks.Data;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using Object = UnityEngine.Object;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Ropes thrown rebars leave hanging in walls in a synced co-op run. The game makes one as the hit effect of the rope
    /// rebar's projectile, which is a handhold rather than an item, so it is shared here instead of as a drop: the thrower
    /// tells everyone where it landed, and everyone builds the same hit effect there, in the same level, with its ropes keyed
    /// by the placement so <see cref="WKTRopeSync"/> can sync them. They are secure and never fall out, so they last until
    /// their level unloads. Every machine keeps the same registry, so the host can replay it to late joiners and a client that
    /// takes over as host already has it. Owned by <see cref="WKTWorldSync"/>.
    /// <para>
    /// Another player's shot shown here (<see cref="WKTPlayerShots.IsCosmetic"/>) still lands and makes its hit effect, so one
    /// that carries a rope is replaced by an empty object: the rope comes from the shooter's placement, never twice.
    /// </para>
    /// </summary>
    internal sealed class WKTPlacedRopeSync {
        /// <summary>How far before and behind the hit point the surface it landed on is looked for, to follow it like the original.</summary>
        private const float c_surface_search = 0.5f;
        private const int c_max_tracked_shots = 64;
        private static readonly Action<M_Level, GameObject> s_addPlacedObject = AccessTools.MethodDelegate<Action<M_Level, GameObject>>(
            AccessTools.Method(typeof(M_Level), "AddPlacedObject"));
        private static readonly Dictionary<GameObject, bool> s_effectHasRope = new();
        private static readonly List<Projectile> s_projectileScratch = new();
        /// <summary>The local player's shot whose hit effect is being made, and which of its effects; null outside one.</summary>
        private static Projectile? s_hitting;
        private static sbyte s_hittingEffect;
        private static GameObject? s_hitSpawn;
        /// <summary>Set while another player's shot shown here makes its hit effect, whose rope must not be built.</summary>
        private static bool s_cosmeticHit;
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        private readonly WKTPlacedRopeRegistry m_registry = new();
        /// <summary>The local player's shots in flight that can leave a rope, with the item and projectile they came from.</summary>
        private readonly Dictionary<Projectile, (string Item, string Projectile)> m_shots = new();
        /// <summary>Placements in levels this machine has not built yet, by level key.</summary>
        private readonly Dictionary<ulong, List<WKTRopePlaced>> m_pending = new();
        private readonly HashSet<uint> m_built = new();
        private uint m_counter;

        public WKTPlacedRopeSync(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
        }

        #region Local

        /// <summary>The local player fired <paramref name="projectile"/> from <paramref name="handItem"/>. A shot that can leave a rope is followed.</summary>
        public void OnLocalShot(HandItem handItem, Projectile projectile) {
            if (handItem.item == null || string.IsNullOrEmpty(handItem.item.prefabName) || !CanLeaveRope(projectile)) return;
            if (m_shots.Count >= c_max_tracked_shots)
                PruneShots();
            m_shots[projectile] = (handItem.item.prefabName, WKTPrefabSearch.GetPrefabName(projectile.name));
        }

        /// <summary>A projectile is about to make a hit effect. Opens a placement if it is a followed shot of the local player.</summary>
        public static void BeginHit(Projectile projectile, Projectile.HitEffect effect) {
            s_cosmeticHit = WKTPlayerShots.IsCosmetic(projectile);
            var sync = WKTWorldSync.Instance;
            if (sync == null || !sync.PlacedRopes.m_shots.ContainsKey(projectile)) return;
            s_hitting = projectile;
            s_hittingEffect = IndexOf(projectile, effect);
            s_hitSpawn = null;
        }

        /// <summary>
        /// Spawns a hit effect like <c>Object.Instantiate</c>, remembering it if a placement is open. Another player's shot
        /// shown here gets an empty object, gone at the end of the frame, in place of an effect with a rope.
        /// </summary>
        public static GameObject SpawnHitEffect(GameObject original, Vector3 position, Quaternion rotation) {
            if (s_cosmeticHit && HasRope(original)) {
                var placeholder = new GameObject(original.name + " (shown elsewhere)");
                placeholder.transform.SetPositionAndRotation(position, rotation);
                Object.Destroy(placeholder);
                return placeholder;
            }
            var spawned = Object.Instantiate(original, position, rotation);
            if (s_hitting != null && HasRope(original))
                s_hitSpawn = spawned;
            return spawned;
        }

        /// <summary>Closes a placement opened by <see cref="BeginHit"/>: a rope it left is shared with everyone.</summary>
        public static void EndHit() {
            var projectile = s_hitting;
            var spawned = s_hitSpawn;
            s_hitting = null;
            s_hitSpawn = null;
            s_cosmeticHit = false;
            var sync = WKTWorldSync.Instance;
            if (projectile == null || spawned == null || sync == null || !sync.PlacedRopes.m_shots.Remove(projectile, out var shot)) return;
            sync.PlacedRopes.OnLocalPlaced(shot.Item, shot.Projectile, s_hittingEffect, spawned);
        }

        private void OnLocalPlaced(string itemPrefab, string projectile, sbyte effect, GameObject spawned) {
            var local = m_network.LocalPlayer;
            var levelInfo = WorldLoader.initialized ? WorldLoader.GetCurrentLevelFromBounds() : null;
            var level = levelInfo?.GetLevel();
            if (local == null || level == null || !WKTContentSeeding.TryGetLevelKey(level, out var levelKey)) return;
            var spawnedTransform = spawned.transform;
            var placed = new WKTRopePlaced(WKTPitonRegistry.MakeId(local.Id, ++m_counter), itemPrefab, projectile, effect, levelKey,
                WKTWorldOffset.ToWire(spawnedTransform.position, WKTWorldOffset.Get()), spawnedTransform.rotation);
            if (!m_registry.Add(placed)) return;
            m_built.Add(placed.Id);
            // Its ropes have not started yet, so they take the placement's keys when they do.
            m_sync.Ropes.PreKey(spawned, placed.Id);
            m_sync.Handholds.RegisterRoot(spawned, WKTHandholdKeys.GetRootKey(WKTHandholdKeys.PlacedRopeSalt, placed.Id));
            var writer = m_network.BeginMessage(WKTMessageId.RopePlaced);
            placed.Write(writer);
            if (m_sync.IsHost)
                m_network.Broadcast(writer, SendType.Reliable);
            else
                m_network.SendToHost(writer, SendType.Reliable);
        }

        private static bool CanLeaveRope(Projectile projectile) {
            if (projectile.hitEffect != null && HasRope(projectile.hitEffect)) return true;
            if (projectile.customHitEffects == null) return false;
            foreach (var effect in projectile.customHitEffects) {
                if (effect?.hitEffect != null && HasRope(effect.hitEffect)) return true;
            }
            return false;
        }

        private static bool HasRope(GameObject effect) {
            if (s_effectHasRope.TryGetValue(effect, out var hasRope)) return hasRope;
            hasRope = effect.GetComponentInChildren<ENV_Rope>(true) != null;
            s_effectHasRope[effect] = hasRope;
            return hasRope;
        }

        /// <summary>The index of <paramref name="effect"/> among the projectile's custom hit effects, or the default effect.</summary>
        private static sbyte IndexOf(Projectile projectile, Projectile.HitEffect effect) {
            var effects = projectile.customHitEffects;
            if (effects == null) return WKTRopePlaced.DEFAULT_EFFECT;
            for (var i = 0; i < effects.Count && i <= sbyte.MaxValue; i++) {
                if (ReferenceEquals(effects[i], effect)) return (sbyte)i;
            }
            return WKTRopePlaced.DEFAULT_EFFECT;
        }

        /// <summary>Forgets shots that were destroyed without leaving a rope.</summary>
        private void PruneShots() {
            s_projectileScratch.Clear();
            foreach (var projectile in m_shots.Keys) {
                if (projectile == null)
                    s_projectileScratch.Add(projectile!);
            }
            foreach (var projectile in s_projectileScratch)
                m_shots.Remove(projectile);
            s_projectileScratch.Clear();
        }

        #endregion

        #region Remote

        public void OnPlaced(WKTPlayer sender, NetDataReader reader) {
            if (!WKTRopePlaced.TryRead(reader, out var placed)) return;
            if (m_sync.IsHost) {
                // A client only names its own placements.
                if (sender.IsHost || WKTPitonRegistry.GetPlayer(placed.Id) != sender.Id || !m_registry.Add(placed)) return;
                Build(placed);
                var writer = m_network.BeginMessage(WKTMessageId.RopePlaced);
                placed.Write(writer);
                m_network.Broadcast(writer, SendType.Reliable, sender);
                return;
            }
            if (!sender.IsHost) return;
            // A replay after a reconnect can carry placements this machine already built.
            m_registry.Add(placed);
            Build(placed);
        }

        /// <summary>Builds another player's placed rope from the projectile's own hit effect, as the game makes it.</summary>
        private void Build(in WKTRopePlaced placed) {
            if (m_built.Contains(placed.Id)) return;
            if (!WKTContentSeeding.TryGetLevel(placed.LevelKey, out var level)) {
                if (!m_pending.TryGetValue(placed.LevelKey, out var waiting)) {
                    waiting = new List<WKTRopePlaced>();
                    m_pending.Add(placed.LevelKey, waiting);
                }
                waiting.Add(placed);
                return;
            }
            if (!TryFindEffect(placed, out var prefab, out var disableOnNoParent)) return;
            m_built.Add(placed.Id);
            var position = WKTWorldOffset.FromWire(placed.Position, WKTWorldOffset.Get());
            var spawned = Object.Instantiate(prefab!, position, placed.Rotation, level!.transform);
            m_sync.Ropes.PreKey(spawned, placed.Id);
            m_sync.Handholds.RegisterRoot(spawned, WKTHandholdKeys.GetRootKey(WKTHandholdKeys.PlacedRopeSalt, placed.Id));
            // The game soft-parents it to what the rebar hit, which is along its flight.
            var forward = placed.Rotation * Vector3.forward;
            if (Physics.Raycast(position - forward * c_surface_search, forward, out var surface, c_surface_search * 2f, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore)) {
                var softParent = spawned.AddComponent<UT_SoftParent>();
                softParent.Initialize(surface.collider.transform);
                softParent.disableOnNoParent = disableOnNoParent;
            }
            s_addPlacedObject(level, spawned);
        }

        /// <summary>The hit effect a placement names. Only effects that carry a rope are built.</summary>
        private bool TryFindEffect(in WKTRopePlaced placed, out GameObject? prefab, out bool disableOnNoParent) {
            prefab = null;
            // The game's default hit effect is made disabling itself once what it hangs from is gone.
            disableOnNoParent = true;
            var projectile = m_sync.PlayerShots.FindPrefab(placed.ItemPrefab, placed.Projectile);
            if (projectile == null) return false;
            if (placed.Effect == WKTRopePlaced.DEFAULT_EFFECT)
                prefab = projectile.hitEffect;
            else if (projectile.customHitEffects != null && placed.Effect < projectile.customHitEffects.Count) {
                var effect = projectile.customHitEffects[placed.Effect];
                prefab = effect?.hitEffect;
                disableOnNoParent = effect?.disableOnNoParent ?? true;
            }
            return prefab != null && HasRope(prefab);
        }

        #endregion

        #region Lifetime

        /// <summary>A level was built here: the ropes placed in it before are built now.</summary>
        public void OnLevelKeyed(ulong levelKey) {
            if (!m_pending.Remove(levelKey, out var waiting)) return;
            foreach (var placed in waiting)
                Build(placed);
        }

        /// <summary>
        /// A level unloaded here, and its placed ropes with it. The host forgets them, since it keeps every level any player is
        /// in. A client keeps them in its registry, as the host still has them, but builds them again if it comes back.
        /// </summary>
        public void OnLevelForgetting(ulong levelKey) {
            m_pending.Remove(levelKey);
            foreach (var placed in m_registry.Ropes) {
                if (placed.LevelKey == levelKey)
                    m_built.Remove(placed.Id);
            }
            if (m_sync.IsHost)
                m_registry.ForgetLevel(levelKey);
        }

        /// <summary>Host only: tells a player who (re)connected about every placed rope.</summary>
        public void SendAll(WKTPlayer player) {
            foreach (var placed in m_registry.Ropes) {
                var writer = m_network.BeginMessage(WKTMessageId.RopePlaced);
                placed.Write(writer);
                m_network.SendToPlayer(player, writer, SendType.Reliable);
            }
        }

        #endregion
    }
}
