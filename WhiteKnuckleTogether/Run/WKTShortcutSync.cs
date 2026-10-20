using System;
using System.Collections.Generic;
using System.Text;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Opens or closes the campaign's shortcuts in a multiplayer run, as the host chose in the lobby (see
    /// <see cref="WKTShortcuts"/>). An open shortcut needs no code, key or first-time step; a closed one cannot be opened at
    /// all this run. Each shortcut's level is found as it is built:
    /// <list type="bullet">
    /// <item>Tangled Sink: the vent door is opened, or kept shut.</item>
    /// <item>Rotten Duct: the prison door's lever is unlocked, or kept locked.</item>
    /// <item>Hadal Diver: the pod computer's unlock is kept from playing when closed. Its flag check answers either way.</item>
    /// <item>Expulsion Chute: its flag check answers from the host's choice.</item>
    /// </list>
    /// Every machine applies the same choice from the run start. In a synced co-op run the host's door state is the one
    /// everyone sees. Lives next to <see cref="WKTRunController"/> and only exists during a multiplayer run.
    /// </summary>
    internal sealed class WKTShortcutSync : MonoBehaviour {
        private static WKTShortcutSync? s_active;
        /// <summary>Instance ids of the targets of closed shortcuts, which must not open.</summary>
        private readonly HashSet<int> m_blocked = new();
        /// <summary>Open shortcuts' targets waiting for their level to wake, as the game sets them up in Start.</summary>
        private readonly List<PendingOpen> m_pending = new();
        private WKTSession m_session = null!;
        private Action<M_Level, ulong> m_onLevelKeyed = null!;

        [UsedImplicitly]
        private void Awake() {
            m_session = WKT.Instance.Network.Session;
            s_active = this;
            m_onLevelKeyed = OnLevelKeyed;
            WKTContentSeeding.LevelKeyed += m_onLevelKeyed;
            WKT.Instance.Logger.LogInfo($"Shortcuts open this run: {WKTLobbyData.FormatShortcutMask(m_session.OpenShortcutMask, new StringBuilder())}.");
            // Levels keyed before this existed.
            var levels = new List<KeyValuePair<ulong, M_Level>>();
            WKTContentSeeding.GetLevels(levels);
            foreach (var pair in levels)
                OnLevelKeyed(pair.Value, pair.Key);
        }

        [UsedImplicitly]
        private void OnDestroy() {
            WKTContentSeeding.LevelKeyed -= m_onLevelKeyed;
            if (s_active == this)
                s_active = null;
        }

        [UsedImplicitly]
        private void Update() {
            for (var i = m_pending.Count - 1; i >= 0; i--) {
                var pending = m_pending[i];
                if (pending.Target == null) {
                    m_pending.RemoveAt(i);
                    continue;
                }
                if (!pending.Target.gameObject.activeInHierarchy) continue;
                // Start runs on the first frame the target is awake, so it is set up from the frame after.
                if (pending.AwakeFrame < 0) {
                    m_pending[i] = new PendingOpen(pending.Id, pending.Target, Time.frameCount);
                    continue;
                }
                if (Time.frameCount <= pending.AwakeFrame) continue;
                Open(pending.Id, pending.Target);
                m_pending.RemoveAt(i);
            }
        }

        /// <summary>
        /// The answer to the game's check of a shortcut's flag, or null when <paramref name="flagName"/> is no shortcut's or
        /// no multiplayer run is in progress, so the game answers as usual.
        /// </summary>
        public static bool? GetFlagOverride(string? flagName) {
            if (s_active == null || !WKTShortcuts.TryGetByFlag(flagName, out var id)) return null;
            return s_active.m_session.IsShortcutOpen(id);
        }

        /// <summary>Whether something may open or unlock <paramref name="target"/>. False only for a closed shortcut's.</summary>
        public static bool AllowOpen(Component target) => s_active == null || !s_active.m_blocked.Contains(target.GetInstanceID());

        private void OnLevelKeyed(M_Level level, ulong levelKey) {
            foreach (var id in WKTShortcuts.All) {
                if (!WKTShortcuts.IsLevel(level.name, id)) continue;
                var path = WKTShortcuts.GetTargetPath(id);
                if (path == null) continue;
                var target = FindTarget(level.transform.Find(path), id);
                if (target == null) {
                    WKT.Instance.Logger.LogWarning($"Shortcut {id}: {path} not found in {level.name}. It works as in singleplayer.");
                    continue;
                }
                if (m_session.IsShortcutOpen(id)) {
                    // Hadal Diver's own flag check plays the unlock once the level starts.
                    if (id != ShortcutId.HadalDiver)
                        m_pending.Add(new PendingOpen(id, target, -1));
                    WKT.Instance.Logger.LogInfo($"Shortcut {id} opens in {level.name}.");
                    continue;
                }
                m_blocked.Add(target.GetInstanceID());
                WKT.Instance.Logger.LogInfo($"Shortcut {id} is closed in {level.name}.");
            }
        }

        private static Component? FindTarget(Transform? transform, ShortcutId id) {
            if (transform == null) return null;
            return id switch {
                ShortcutId.TangledSink => transform.GetComponent<UT_Door>(),
                ShortcutId.RottenDuct => transform.GetComponent<CL_HoldLever>(),
                ShortcutId.HadalDiver => transform.GetComponent<UT_EventController>(),
                _ => null
            };
        }

        private static void Open(ShortcutId id, Component target) {
            switch (target) {
                case UT_Door door when !door.IsOpen():
                    // Only the host's door moves in a synced co-op run. A client's follows the host's.
                    door.Open();
                    break;
                case CL_HoldLever lever:
                    lever.SetLocked(false);
                    break;
            }
            WKT.Instance.Logger.LogInfo($"Opened shortcut {id}.");
        }

        private readonly struct PendingOpen {
            public readonly ShortcutId Id;
            public readonly Component Target;
            /// <summary>The frame the target was first seen awake, or -1 while its level sleeps.</summary>
            public readonly int AwakeFrame;

            public PendingOpen(ShortcutId id, Component target, int awakeFrame) {
                Id = id;
                Target = target;
                AwakeFrame = awakeFrame;
            }
        }
    }
}
