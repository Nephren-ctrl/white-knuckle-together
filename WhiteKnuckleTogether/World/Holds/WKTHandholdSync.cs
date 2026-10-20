using System.Collections.Generic;
using HarmonyLib;
using Steamworks.Data;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Handholds in a synced co-op run, named the same on every machine (see <see cref="WKTHandholdKeys"/>): a level's by
    /// their content keys, and those on anything shared at runtime (placed pitons and ropes, stuck return rebars, replicated
    /// world objects) through <see cref="RegisterRoot"/>, which whatever shares such an object calls once it has a shared id.
    /// <list type="bullet">
    /// <item>A handhold's own sounds (an icy hold slipping, a breakable one shaking, a handhold's grab and release clips, a
    /// damaging or poisonous hold) play only on the machine of the player holding it, so that machine sends each one and
    /// everyone else plays the same clip from the same place.</item>
    /// <item>With <see cref="ModifierId.ShareTimedHandholds"/> on, a breakable handhold that breaks breaks for everyone, its
    /// total hold time follows whoever holds it, and a handhold's timer running out runs its events everywhere. Each holder's
    /// machine times its own hands as the game does, so it gives way exactly as in singleplayer. Off, each player's hold only
    /// affects their own copy.</item>
    /// </list>
    /// Every machine remembers the broken handholds of each level, so the host can replay them to late joiners. Owned by
    /// <see cref="WKTWorldSync"/>.
    /// </summary>
    internal sealed class WKTHandholdSync {
        /// <summary>How often a held breakable's total hold time is sent, per second.</summary>
        public const float HOLD_TIME_RATE = 10f;
        private const SendType c_hold_time_send_type = SendType.Unreliable | SendType.NoNagle;
        /// <summary>How many roots are registered between sweeps of handholds that were destroyed.</summary>
        private const int c_sweep_interval = 64;
        private static readonly AccessTools.FieldRef<CL_Handhold, List<ENT_Player.Hand>> s_hands = AccessTools.FieldRefAccess<CL_Handhold, List<ENT_Player.Hand>>("hands");
        private static readonly AccessTools.FieldRef<HandholdModule, CL_Handhold> s_moduleHandhold = AccessTools.FieldRefAccess<HandholdModule, CL_Handhold>("handhold");
        private static readonly AccessTools.FieldRef<CL_Handhold_Breakable, float> s_totalHoldTime = AccessTools.FieldRefAccess<CL_Handhold_Breakable, float>("totalHoldTime");
        private static readonly AccessTools.FieldRef<HandholdModule_Timer, bool> s_timerRunning = AccessTools.FieldRefAccess<HandholdModule_Timer, bool>("timerRunning");
        private static readonly AccessTools.FieldRef<HandholdModule_Timer, float> s_timerTime = AccessTools.FieldRefAccess<HandholdModule_Timer, float>("currentTime");
        private static readonly List<CL_Handhold> s_handholdScratch = new();
        /// <summary>How deep the patched handhold methods run, and whether the handhold was held here when the outermost began.</summary>
        private static int s_depth;
        private static bool s_heldAtEntry;
        /// <summary>Set while a break that everyone sees runs, whose sounds every machine plays itself.</summary>
        private static bool s_inSharedBreak;
        /// <summary>Set while another machine's break or timer is applied here, so it is not sent back.</summary>
        private static bool s_applying;
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        private readonly Dictionary<ulong, CL_Handhold> m_byKey = new();
        private readonly Dictionary<CL_Handhold, ulong> m_keyOf = new();
        private readonly List<CL_Handhold> m_deadScratch = new();
        private readonly Dictionary<ulong, List<ulong>> m_keysByLevel = new();
        /// <summary>Broken breakables, by key, with the level each is in, so a late joiner or a level built later gets them.</summary>
        private readonly Dictionary<ulong, ulong> m_broken = new();
        /// <summary>Each handhold's clips (see <see cref="WKTFieldTable"/>): its own, then each module's.</summary>
        private readonly Dictionary<CL_Handhold, List<AudioClip?>> m_clips = new();
        /// <summary>Breakables counting their total hold time that the local player took hold of.</summary>
        private readonly List<CL_Handhold_Breakable> m_heldTimed = new();
        private readonly List<ulong> m_keyScratch = new();
        private double m_nextHoldTimeSend;
        private int m_rootsSinceSweep;

        public WKTHandholdSync(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
        }

        private bool IsShared => m_network.Session.ShareTimedHandholds;

        #region Levels

        /// <summary>Every level this machine has keyed already.</summary>
        public void IndexExisting() {
            var levels = new List<KeyValuePair<ulong, M_Level>>();
            WKTContentSeeding.GetLevels(levels);
            foreach (var pair in levels)
                IndexLevel(pair.Value, pair.Key);
        }

        /// <summary>A level was keyed: its handholds can be found by key, and the ones broken before break here too.</summary>
        public void IndexLevel(M_Level level, ulong levelKey) {
            if (m_keysByLevel.ContainsKey(levelKey)) return;
            var keys = new List<ulong>();
            level.GetComponentsInChildren(true, s_handholdScratch);
            foreach (var handhold in s_handholdScratch) {
                // A shared runtime object in the level keeps the name its root gave it.
                if (handhold == null || m_keyOf.ContainsKey(handhold) || !WKTContentSeeding.TryGetKey(handhold, out var key)) continue;
                Name(handhold, key);
                keys.Add(key);
                if (m_broken.ContainsKey(key))
                    m_broken[key] = levelKey;
            }
            s_handholdScratch.Clear();
            m_keysByLevel[levelKey] = keys;
        }

        /// <summary>
        /// A level unloaded here. Its handholds are gone; the host forgets which were broken, since it keeps every level any
        /// player is in.
        /// </summary>
        public void OnLevelForgetting(ulong levelKey) {
            if (m_keysByLevel.Remove(levelKey, out var keys)) {
                foreach (var key in keys) {
                    if (!m_byKey.Remove(key, out var handhold)) continue;
                    m_keyOf.Remove(handhold);
                    m_clips.Remove(handhold);
                }
            }
            if (!m_sync.IsHost) return;
            m_keyScratch.Clear();
            foreach (var pair in m_broken) {
                if (pair.Value == levelKey)
                    m_keyScratch.Add(pair.Key);
            }
            foreach (var key in m_keyScratch)
                m_broken.Remove(key);
            m_keyScratch.Clear();
        }

        /// <summary>
        /// Names the handholds of a shared runtime object, whose <paramref name="rootKey"/> (see
        /// <see cref="WKTHandholdKeys.GetRootKey"/>) every machine gives it. Call it on every machine once the object has its
        /// shared id; anything that shares a new kind of object with handholds only needs this.
        /// </summary>
        public void RegisterRoot(GameObject root, ulong rootKey, bool ownOnly = false) {
            if (root == null) return;
            root.GetComponentsInChildren(true, s_handholdScratch);
            var index = 0;
            foreach (var handhold in s_handholdScratch) {
                // A creature nested in this one (a barnacle zombie's barnacle) names its own handholds.
                if (ownOnly && handhold.GetComponentInParent<GameEntity>(true) is { } entity && entity.gameObject != root) continue;
                Name(handhold, WKTHandholdKeys.GetChildKey(rootKey, index++));
            }
            s_handholdScratch.Clear();
            if (++m_rootsSinceSweep >= c_sweep_interval)
                Sweep();
        }

        private void Name(CL_Handhold handhold, ulong key) {
            if (m_keyOf.TryGetValue(handhold, out var previous) && m_byKey.TryGetValue(previous, out var named) && named == handhold)
                m_byKey.Remove(previous);
            m_keyOf[handhold] = key;
            m_byKey[key] = handhold;
            if (m_broken.ContainsKey(key) && handhold is CL_Handhold_Breakable breakable)
                ShowBroken(breakable);
        }

        /// <summary>Forgets the handholds of runtime objects that were destroyed.</summary>
        private void Sweep() {
            m_rootsSinceSweep = 0;
            m_deadScratch.Clear();
            foreach (var handhold in m_keyOf.Keys) {
                if (handhold == null)
                    m_deadScratch.Add(handhold!);
            }
            foreach (var handhold in m_deadScratch) {
                if (m_keyOf.Remove(handhold, out var key) && m_byKey.TryGetValue(key, out var named) && named == handhold)
                    m_byKey.Remove(key);
                m_clips.Remove(handhold);
            }
            m_deadScratch.Clear();
        }

        private bool TryGetKey(CL_Handhold handhold, out ulong key) => m_keyOf.TryGetValue(handhold, out key);

        /// <summary>Host only: tells a player who (re)connected which handholds are broken.</summary>
        public void SendAll(WKTPlayer player) {
            foreach (var key in m_broken.Keys) {
                var writer = m_network.BeginMessage(WKTMessageId.HandholdEvent);
                new WKTHandholdEvent(key, WKTHandholdEvent.BROKE).Write(writer);
                m_network.SendToPlayer(player, writer, SendType.Reliable);
            }
        }

        #endregion

        #region Local

        /// <summary>A patched handhold method begins. Whether the local player held the handhold then decides whether its sounds are theirs.</summary>
        public static void Enter(object owner) {
            if (s_depth++ == 0)
                s_heldAtEntry = GetHandhold(owner) is { } handhold && s_hands(handhold).Count > 0;
        }

        /// <summary>The local player takes hold of or lets go of a handhold: the sounds that makes are theirs.</summary>
        public static void EnterLocalHold() {
            if (s_depth++ == 0)
                s_heldAtEntry = true;
        }

        public static void Exit() {
            if (s_depth > 0)
                s_depth--;
        }

        /// <summary>
        /// Plays a handhold's sound like <c>AudioManager.PlaySound</c>. If it was the local player's doing, everyone else hears
        /// it too.
        /// </summary>
        public static void PlaySound(AudioClip clip, Vector3 position, float volume, float pitch, float spatial, bool bypass, float reverbMix,
            UnityEngine.Audio.AudioMixerGroup mixerGroup, string sourceType, object owner) {
            AudioManager.PlaySound(clip, position, volume, pitch, spatial, bypass, reverbMix, mixerGroup, sourceType);
            if (s_heldAtEntry && !s_inSharedBreak && !s_applying)
                WKTWorldSync.Instance?.Handholds.OnLocalSound(GetHandhold(owner), clip, position, volume, pitch);
        }

        private void OnLocalSound(CL_Handhold? handhold, AudioClip clip, Vector3 position, float volume, float pitch) {
            if (handhold == null || clip == null || !TryGetKey(handhold, out var key)) return;
            // Its shaking follows the shared hold time, so every machine shakes it and plays that sound itself.
            if (IsShared && handhold is CL_Handhold_Breakable { useTotalHoldTime: true }) return;
            var index = GetClips(handhold).IndexOf(clip);
            if (index < 0 || index > ushort.MaxValue) return;
            var writer = m_network.BeginMessage(WKTMessageId.HandholdSound);
            new WKTHandholdSound(key, (ushort)index, volume, pitch, position - handhold.transform.position).Write(writer);
            Send(writer, SendType.Reliable);
        }

        /// <summary>The local player took hold of a handhold. A breakable counting its total hold time shares that time while held.</summary>
        public void OnLocalGrab(CL_Handhold handhold) {
            if (handhold is CL_Handhold_Breakable { useTotalHoldTime: true } breakable && !m_heldTimed.Contains(breakable))
                m_heldTimed.Add(breakable);
        }

        /// <summary>
        /// A breakable is about to break. Shared, the break is sent to everyone and its sounds are each machine's own; a
        /// handhold that already broke here does not break again. False skips the game's break.
        /// </summary>
        public bool BeforeBreak(CL_Handhold_Breakable breakable, out bool shared) {
            shared = false;
            if (!IsShared || !TryGetKey(breakable, out var key)) return true;
            if (m_broken.ContainsKey(key)) return false;
            m_broken[key] = GetLevelKey(key);
            shared = true;
            s_inSharedBreak = true;
            if (!s_applying) {
                var writer = m_network.BeginMessage(WKTMessageId.HandholdEvent);
                new WKTHandholdEvent(key, WKTHandholdEvent.BROKE).Write(writer);
                Send(writer, SendType.Reliable);
            }
            return true;
        }

        public static void AfterBreak(bool shared) {
            if (shared)
                s_inSharedBreak = false;
        }

        /// <summary>Whether a timer is running before its update, to tell when it runs out.</summary>
        public static bool IsTimerRunning(HandholdModule_Timer timer) => s_timerRunning(timer);

        /// <summary>A timer ran out here. Shared, its events run everywhere; a breakable's own break is shared by itself.</summary>
        public void OnLocalTimerFinished(HandholdModule_Timer timer) {
            var handhold = s_moduleHandhold(timer);
            if (s_applying || !IsShared || handhold == null || handhold is CL_Handhold_Breakable || handhold.modules == null ||
                !TryGetKey(handhold, out var key)) return;
            var index = handhold.modules.IndexOf(timer);
            if (index < 0 || index > byte.MaxValue) return;
            var writer = m_network.BeginMessage(WKTMessageId.HandholdEvent);
            new WKTHandholdEvent(key, WKTHandholdEvent.TIMER_FINISHED, (byte)index).Write(writer);
            Send(writer, SendType.Reliable);
        }

        public void Update(double now) {
            if (m_heldTimed.Count == 0 || now < m_nextHoldTimeSend) return;
            m_nextHoldTimeSend = now + 1.0 / HOLD_TIME_RATE;
            for (var i = m_heldTimed.Count - 1; i >= 0; i--) {
                var breakable = m_heldTimed[i];
                if (breakable == null || !breakable.GetHolding() || !breakable.gameObject.activeInHierarchy) {
                    m_heldTimed.RemoveAt(i);
                    continue;
                }
                if (!IsShared || !TryGetKey(breakable, out var key)) continue;
                var writer = m_network.BeginMessage(WKTMessageId.HandholdHoldTime);
                writer.Put(key);
                writer.Put(s_totalHoldTime(breakable));
                Send(writer, c_hold_time_send_type);
            }
        }

        #endregion

        #region Remote

        public void OnSound(WKTPlayer sender, NetDataReader reader) {
            if (!WKTHandholdSound.TryRead(reader, out var sound) || !Accept(sender)) return;
            if (m_sync.IsHost) {
                var writer = m_network.BeginMessage(WKTMessageId.HandholdSound);
                sound.Write(writer);
                m_network.Broadcast(writer, SendType.Reliable, sender);
            }
            if (!m_byKey.TryGetValue(sound.Key, out var handhold) || handhold == null) return;
            var clips = GetClips(handhold);
            if (sound.Clip >= clips.Count || clips[sound.Clip] == null) return;
            var clip = clips[sound.Clip];
            var position = handhold.transform.position + sound.Offset;
            AudioManager.PlaySound(clip, position, sound.Volume, sound.Pitch);
            ShowSporePuff(handhold, clip, position);
        }

        /// <summary>
        /// A metafungus hold's poison sound comes with its spore puff, which the game spawns only for the player poisoned
        /// (<c>HandholdModule_Metafungus.OnGrab</c>): others see it where they hear it.
        /// </summary>
        private static void ShowSporePuff(CL_Handhold handhold, AudioClip clip, Vector3 position) {
            if (handhold.modules == null) return;
            foreach (var module in handhold.modules) {
                if (module is not HandholdModule_Metafungus fungus || fungus.sporePuffAsset == null || fungus.poisonSound == null || !fungus.poisonSound.Contains(clip)) continue;
                Object.Instantiate(fungus.sporePuffAsset, position, Quaternion.identity, handhold.transform);
                return;
            }
        }

        public void OnEvent(WKTPlayer sender, NetDataReader reader) {
            if (!WKTHandholdEvent.TryRead(reader, out var handholdEvent) || !Accept(sender)) return;
            if (handholdEvent.Kind == WKTHandholdEvent.BROKE && m_broken.ContainsKey(handholdEvent.Key)) return;
            if (m_sync.IsHost) {
                var writer = m_network.BeginMessage(WKTMessageId.HandholdEvent);
                handholdEvent.Write(writer);
                m_network.Broadcast(writer, SendType.Reliable, sender);
            }
            switch (handholdEvent.Kind) {
                case WKTHandholdEvent.BROKE:
                    if (!m_byKey.TryGetValue(handholdEvent.Key, out var handhold) || handhold is not CL_Handhold_Breakable breakable) {
                        // Its level is not built here yet; it is shown broken once it is.
                        m_broken[handholdEvent.Key] = 0;
                        return;
                    }
                    ApplyBreak(breakable);
                    return;
                case WKTHandholdEvent.TIMER_FINISHED:
                    if (!m_byKey.TryGetValue(handholdEvent.Key, out var timed) || timed == null || timed.modules == null ||
                        handholdEvent.Module >= timed.modules.Count || timed.modules[handholdEvent.Module] is not HandholdModule_Timer timer) return;
                    s_applying = true;
                    try {
                        s_timerRunning(timer) = false;
                        s_timerTime(timer) = 0f;
                        timer.timerFinishEvent?.Invoke();
                    }
                    finally {
                        s_applying = false;
                    }
                    return;
            }
        }

        public void OnHoldTime(WKTPlayer sender, NetDataReader reader) {
            if (!reader.TryGetULong(out var key) || !reader.TryGetFloat(out var time) || !(time >= 0f && time <= WKTWire.MAX_AMOUNT) || !Accept(sender)) return;
            if (m_sync.IsHost) {
                var writer = m_network.BeginMessage(WKTMessageId.HandholdHoldTime);
                writer.Put(key);
                writer.Put(time);
                m_network.Broadcast(writer, c_hold_time_send_type, sender);
            }
            // Whoever has held it longest decides, so two holders count it as one would, never twice as fast.
            if (m_byKey.TryGetValue(key, out var handhold) && handhold is CL_Handhold_Breakable breakable && s_totalHoldTime(breakable) < time)
                s_totalHoldTime(breakable) = time;
        }

        /// <summary>A client's message counts from the host; the host's from any client. Shared state only with the setting on.</summary>
        private bool Accept(WKTPlayer sender) => m_sync.IsHost ? !sender.IsHost : sender.IsHost;

        private static void ApplyBreak(CL_Handhold_Breakable breakable) {
            s_applying = true;
            try {
                breakable.Break();
            }
            finally {
                s_applying = false;
            }
        }

        /// <summary>A breakable broken before its level was built here: hidden as it would be, without breaking it again.</summary>
        private static void ShowBroken(CL_Handhold_Breakable breakable) {
            if (breakable.hideOnBreak)
                breakable.gameObject.SetActive(false);
            else
                ApplyBreak(breakable);
        }

        #endregion

        private void Send(NetDataWriter writer, SendType sendType) {
            if (m_sync.IsHost)
                m_network.Broadcast(writer, sendType);
            else
                m_network.SendToHost(writer, sendType);
        }

        private ulong GetLevelKey(ulong key) {
            foreach (var pair in m_keysByLevel) {
                if (pair.Value.Contains(key)) return pair.Key;
            }
            return 0;
        }

        private List<AudioClip?> GetClips(CL_Handhold handhold) {
            if (m_clips.TryGetValue(handhold, out var clips)) return clips;
            clips = new List<AudioClip?>();
            WKTFieldTable.Collect(handhold, clips);
            if (handhold.modules != null) {
                foreach (var module in handhold.modules)
                    WKTFieldTable.Collect(module, clips);
            }
            m_clips[handhold] = clips;
            return clips;
        }

        private static CL_Handhold? GetHandhold(object owner) => owner switch {
            CL_Handhold handhold => handhold,
            HandholdModule module => s_moduleHandhold(module),
            _ => null
        };
    }
}
