using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using Object = UnityEngine.Object;
using SendType = Steamworks.Data.SendType;

namespace WhiteKnuckleTogether {
    /// <summary>What a hazard call does on the machines it is replayed on.</summary>
    public enum WKTHazardCall : byte {
        /// <summary>A rising death wall starts (<c>DEN_DeathwallManager.StartIfNoneActive</c>).</summary>
        DeathwallStart,
        /// <summary>A lightning discharge strikes (<c>ENV_LightningDischarge.Discharge</c>).</summary>
        LightningStrike,
        /// <summary>A flashflower flashes (<c>ENV_Flashflower.StartFlash</c>).</summary>
        FlowerFlash,
        /// <summary>A signal orchid is killed (its <c>UT_Damage.Kill</c>), which grows its vine or drops its rope.</summary>
        OrchidKill,
        /// <summary>A recycler destroys what it took (its <c>recycleEvent</c>: sparks, a shake and its sound).</summary>
        RecyclerRecycle,
        /// <summary>A recycler pays out (its <c>cashEvent</c>: its sound).</summary>
        RecyclerCash
    }

    /// <summary>
    /// Level hazards that act on their own, kept in step in a synced co-op run. Each is named by its content key (the same on
    /// every machine) and a call:
    /// <list type="bullet">
    /// <item>Rising death walls (Delta Labs' lasers) start only once every living player has climbed past where they were
    /// set off, then on every machine at once. A client never starts one by itself.</item>
    /// <item>Lightning discharges strike when the host's timer says; clients' own timers never fire.</item>
    /// <item>A flashflower flashes for everyone when it flashes for anyone looking at it.</item>
    /// <item>A signal orchid killed by anyone dies for everyone, so its grabbable vine grows (or rope drops) everywhere. Kills
    /// are remembered for players who join later and for levels built later.</item>
    /// <item>Only the host's recyclers run (see <see cref="WKTRecyclers"/>), so each recycle and payout plays its effects on
    /// every client's copy too, and the per-tag events that went off: what they show (a grub's gore and alarm, an explosion,
    /// which hurts each player by their own copy) and the recycler stopping. Their drones and score stay the host's.</item>
    /// </list>
    /// Every machine runs a strike or flash itself, and each player is only hurt by their own copy (it runs in
    /// <see cref="WKTSharedEffectScope"/>). Owned by <see cref="WKTWorldSync"/>.
    /// </summary>
    internal sealed class WKTHazardSync {
        /// <summary>How far below the highest living player the others must climb before a death wall they set off starts.</summary>
        public const float PASS_MARGIN = 4f;
        /// <summary>A death wall waits at most this long for players who never come up.</summary>
        public const float PASS_TIMEOUT = 90f;
        /// <summary>Signal orchid prefabs (<c>ENV_SignalOrchid_Blossom</c>, <c>ENV_SignalOrchid_RopeFlower</c>) start with this.</summary>
        public const string ORCHID_PREFIX = "ENV_SignalOrchid";
        private static readonly MethodInfo? s_discharge = AccessTools.Method(typeof(ENV_LightningDischarge), "Discharge");
        private static readonly AccessTools.FieldRef<UT_Damage, bool> s_damageDead = AccessTools.FieldRefAccess<UT_Damage, bool>("dead");
        private static readonly List<UT_Damage> s_damageScratch = new();
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        private readonly Dictionary<ulong, MonoBehaviour> m_byKey = new();
        private readonly List<PendingWall> m_pendingWalls = new();
        private readonly HashSet<ulong> m_killedOrchids = new();
        private readonly Dictionary<UT_Damage, bool> m_isOrchid = new();
        private bool m_replaying;

        public WKTHazardSync(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
        }

        /// <summary>Whether players must all pass a death wall's start: true unless every player is above <paramref name="threshold"/>.</summary>
        public static bool AllPassed(ReadOnlySpan<float> livingHeights, float threshold) {
            foreach (var height in livingHeights) {
                if (height < threshold) return false;
            }
            return true;
        }

        public void Clear() {
            m_byKey.Clear();
            m_pendingWalls.Clear();
            m_killedOrchids.Clear();
            m_isOrchid.Clear();
            m_replaying = false;
        }

        #region Death walls

        /// <summary>A death wall is about to start. False holds it: the host starts it once everyone passed, clients only when told.</summary>
        public bool BeforeDeathwallStart(DEN_DeathwallManager manager) {
            if (m_replaying) return true;
            if (!m_sync.IsHost) return false;
            if (!WKTContentSeeding.TryGetKey(manager, out _)) return true;
            foreach (var pending in m_pendingWalls) {
                if (pending.Manager == manager) return false;
            }
            m_pendingWalls.Add(new PendingWall(manager, HighestLivingHeight() - PASS_MARGIN, Time.time));
            return false;
        }

        /// <summary>Host only: starts the death walls everyone has now passed.</summary>
        public void Update() {
            if (!m_sync.IsHost || m_pendingWalls.Count == 0) return;
            Span<float> heights = stackalloc float[WKTLobbyConsts.MAX_PLAYERS + 1];
            var count = LivingHeights(heights);
            for (var i = m_pendingWalls.Count - 1; i >= 0; i--) {
                var pending = m_pendingWalls[i];
                if (pending.Manager == null) {
                    m_pendingWalls.RemoveAt(i);
                    continue;
                }
                if (!AllPassed(heights.Slice(0, count), pending.Threshold) && Time.time - pending.Since < PASS_TIMEOUT) continue;
                m_pendingWalls.RemoveAt(i);
                Run(pending.Manager, WKTHazardCall.DeathwallStart);
                Broadcast(pending.Manager, WKTHazardCall.DeathwallStart, null);
            }
        }

        private static float HighestLivingHeight() {
            Span<float> heights = stackalloc float[WKTLobbyConsts.MAX_PLAYERS + 1];
            var count = LivingHeights(heights);
            var highest = float.NegativeInfinity;
            for (var i = 0; i < count; i++)
                highest = Mathf.Max(highest, heights[i]);
            return float.IsNegativeInfinity(highest) ? 0f : highest;
        }

        private static int LivingHeights(Span<float> heights) {
            var count = 0;
            if (ENT_Player.playerObject is { dead: false } local)
                heights[count++] = local.transform.position.y;
            for (var i = 0; i < WKTPlayerPositions.RemoteCount && count < heights.Length; i++) {
                if (!WKTPlayerPositions.IsRemoteDead(i))
                    heights[count++] = WKTPlayerPositions.GetRemotePosition(i).y;
            }
            return count;
        }

        #endregion

        #region Strikes and flashes

        /// <summary>A lightning discharge's own timer. False on clients: only the host's timer strikes, for everyone.</summary>
        public bool BeforeLightningTimer() => m_sync.IsHost;

        /// <summary>Host only: the host's lightning is about to strike, so every client's does too.</summary>
        public void OnLightningStrike(ENV_LightningDischarge lightning) => Broadcast(lightning, WKTHazardCall.LightningStrike, null);

        /// <summary>A flashflower is about to flash here. Flashes this machine started reach everyone else.</summary>
        public void OnFlowerFlash(ENV_Flashflower flower) {
            if (!m_replaying)
                Broadcast(flower, WKTHazardCall.FlowerFlash, null);
        }

        /// <summary>
        /// Host only: a recycler destroyed what it took or paid out, so every client's copy shows it too. <paramref name="tags"/>
        /// has a bit for each of its per-tag events that went off (see <see cref="RecycleTags"/>).
        /// </summary>
        public void OnRecycler(ENV_Recycler recycler, WKTHazardCall call, byte tags = 0) => Broadcast(recycler, call, null, tags);

        /// <summary>A bit for each of a recycler's per-tag events (up to 8) that <paramref name="tagger"/> sets off, as the game picks them.</summary>
        public static byte RecycleTags(ENV_Recycler recycler, ObjectTagger? tagger) {
            byte tags = 0;
            if (tagger == null) return tags;
            for (var i = 0; i < recycler.recycleEvents.Count && i < 8; i++) {
                foreach (var tag in recycler.recycleEvents[i].eventTags) {
                    if (!tagger.HasTag(tag)) continue;
                    tags |= (byte)(1 << i);
                    break;
                }
            }
            return tags;
        }

        #endregion

        #region Orchids

        /// <summary>Whether an object with this name is a signal orchid (or part of one): only their kills are shared.</summary>
        public static bool IsOrchidName(string name) => name.StartsWith(ORCHID_PREFIX, StringComparison.Ordinal);

        /// <summary>Something here is about to be killed. A signal orchid this machine killed dies for everyone.</summary>
        public void OnDamageKill(UT_Damage damage) {
            if (m_replaying || !IsOrchid(damage) || !WKTContentSeeding.TryGetKey(damage, out var key)) return;
            m_killedOrchids.Add(key);
            Broadcast(damage, WKTHazardCall.OrchidKill, null);
        }

        /// <summary>A level was keyed: orchids killed before it was built here die now, catching up.</summary>
        public void OnLevelKeyed(M_Level level) {
            if (m_killedOrchids.Count == 0) return;
            level.GetComponentsInChildren(true, s_damageScratch);
            foreach (var damage in s_damageScratch) {
                if (damage != null && WKTContentSeeding.TryGetKey(damage, out var key) && m_killedOrchids.Contains(key))
                    Run(damage, WKTHazardCall.OrchidKill);
            }
            s_damageScratch.Clear();
        }

        /// <summary>Host only: tells a player who just joined which orchids are already dead.</summary>
        public void SendAll(WKTPlayer player) {
            foreach (var key in m_killedOrchids) {
                var writer = m_network.BeginMessage(WKTMessageId.HazardCall);
                writer.Put(key);
                writer.Put((byte)WKTHazardCall.OrchidKill);
                m_network.SendToPlayer(player, writer, SendType.Reliable);
            }
        }

        private bool IsOrchid(UT_Damage damage) {
            if (m_isOrchid.TryGetValue(damage, out var known)) return known;
            var isOrchid = false;
            for (var current = damage.transform; current != null; current = current.parent) {
                if (!IsOrchidName(current.name)) continue;
                isOrchid = true;
                break;
            }
            m_isOrchid[damage] = isOrchid;
            return isOrchid;
        }

        /// <summary>Kills an orchid as its own damage would, once: a dead one stays as it is, so its vine never grows twice.</summary>
        private static void KillOrchid(UT_Damage damage) {
            if (s_damageDead(damage)) return;
            s_damageDead(damage) = true;
            damage.health = 0f;
            damage.Kill();
        }

        #endregion

        private void Broadcast(MonoBehaviour hazard, WKTHazardCall call, WKTPlayer? except, byte tags = 0) {
            if (!WKTContentSeeding.TryGetKey(hazard, out var key)) return;
            var writer = m_network.BeginMessage(WKTMessageId.HazardCall);
            writer.Put(key);
            writer.Put((byte)call);
            if (call == WKTHazardCall.RecyclerRecycle)
                writer.Put(tags);
            if (m_sync.IsHost)
                m_network.Broadcast(writer, SendType.Reliable, except);
            else
                m_network.SendToHost(writer, SendType.Reliable);
        }

        public void OnHazardCall(WKTPlayer sender, NetDataReader reader) {
            if (!reader.TryGetULong(out var key) || !reader.TryGetByte(out var callByte) || callByte > (byte)WKTHazardCall.RecyclerCash) return;
            var call = (WKTHazardCall)callByte;
            byte tags = 0;
            if (call == WKTHazardCall.RecyclerRecycle && !reader.TryGetByte(out tags)) return;
            if (m_sync.IsHost) {
                // Only flashes and orchid kills come from clients: the host decides walls and lightning.
                if (sender.IsHost || call is not (WKTHazardCall.FlowerFlash or WKTHazardCall.OrchidKill)) return;
            }
            else if (!sender.IsHost) return;
            if (call == WKTHazardCall.OrchidKill)
                m_killedOrchids.Add(key);
            var hazard = Find(key, call);
            if (hazard == null) return;
            if (m_sync.IsHost)
                Broadcast(hazard, call, sender);
            Run(hazard, call, tags);
        }

        private void Run(MonoBehaviour hazard, WKTHazardCall call, byte tags = 0) {
            m_replaying = true;
            try {
                switch (call) {
                    case WKTHazardCall.DeathwallStart when hazard is DEN_DeathwallManager manager:
                        manager.StartIfNoneActive();
                        break;
                    case WKTHazardCall.LightningStrike when hazard is ENV_LightningDischarge lightning && !m_sync.IsHost:
                        if (s_discharge?.Invoke(lightning, null) is IEnumerator strike)
                            lightning.StartCoroutine(strike);
                        break;
                    case WKTHazardCall.FlowerFlash when hazard is ENV_Flashflower flower:
                        flower.StartFlash();
                        break;
                    case WKTHazardCall.OrchidKill when hazard is UT_Damage damage:
                        KillOrchid(damage);
                        break;
                    case WKTHazardCall.RecyclerRecycle when hazard is ENV_Recycler recycler && !m_sync.IsHost:
                        recycler.recycleEvent.Invoke();
                        RunRecycleEvents(recycler, tags);
                        break;
                    case WKTHazardCall.RecyclerCash when hazard is ENV_Recycler recycler && !m_sync.IsHost:
                        recycler.cashEvent.Invoke();
                        break;
                }
            }
            finally {
                m_replaying = false;
            }
        }

        /// <summary>
        /// Runs here the part of a recycler's per-tag events that went off on the host which only belongs to the recycler (see
        /// <see cref="WKTEntityEvents.Sort"/>): what they show, and its own stopping. Its drone spawner and score stay the host's.
        /// </summary>
        private static void RunRecycleEvents(ENV_Recycler recycler, byte tags) {
            for (var i = 0; i < recycler.recycleEvents.Count && i < 8; i++) {
                if ((tags & (1 << i)) == 0) continue;
                var unityEvent = recycler.recycleEvents[i].recycleUnityEvent;
                var masks = WKTEntityEvents.Sort(unityEvent, recycler.transform);
                for (var j = 0; j < unityEvent.GetPersistentEventCount(); j++) {
                    if (unityEvent.GetPersistentTarget(j) is UT_ScoreSetter)
                        masks = masks.WithSkipped(j);
                }
                WKTUnityEventFields.InvokeFiltered(unityEvent, null, masks, false);
            }
        }

        /// <summary>The hazard with this content key, found once by searching that hazard's type.</summary>
        private MonoBehaviour? Find(ulong key, WKTHazardCall call) {
            if (m_byKey.TryGetValue(key, out var known) && known != null) return known;
            MonoBehaviour[] candidates = call switch {
                WKTHazardCall.DeathwallStart => Object.FindObjectsByType<DEN_DeathwallManager>(FindObjectsInactive.Include, FindObjectsSortMode.None),
                WKTHazardCall.LightningStrike => Object.FindObjectsByType<ENV_LightningDischarge>(FindObjectsInactive.Include, FindObjectsSortMode.None),
                WKTHazardCall.OrchidKill => Object.FindObjectsByType<UT_Damage>(FindObjectsInactive.Include, FindObjectsSortMode.None),
                WKTHazardCall.RecyclerRecycle or WKTHazardCall.RecyclerCash =>
                    Object.FindObjectsByType<ENV_Recycler>(FindObjectsInactive.Include, FindObjectsSortMode.None),
                _ => Object.FindObjectsByType<ENV_Flashflower>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            };
            foreach (var candidate in candidates) {
                if (candidate == null || !WKTContentSeeding.TryGetKey(candidate, out var candidateKey)) continue;
                m_byKey[candidateKey] = candidate;
            }
            return m_byKey.TryGetValue(key, out known) ? known : null;
        }

        private readonly struct PendingWall {
            public readonly DEN_DeathwallManager Manager;
            public readonly float Threshold;
            public readonly float Since;

            public PendingWall(DEN_DeathwallManager manager, float threshold, float since) {
                Manager = manager;
                Threshold = threshold;
                Since = since;
            }
        }
    }
}
