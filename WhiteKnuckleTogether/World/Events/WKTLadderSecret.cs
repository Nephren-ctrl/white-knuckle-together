using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using SendType = Steamworks.Data.SendType;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The Delta Labs ladder secret in a synced run (see GAME_INTERNALS.md, "Ladder secret"):
    /// <list type="bullet">
    /// <item>The way in is a vent closed off by a plain blocker that only a teleport (a blink eye, an artifact) gets past.
    /// When a player teleports through one, that blocker, and only that one, is removed for everyone with the game's vent
    /// hit sound and some debris, so players without a teleport can follow. Entering the ladder then gathers the group like
    /// any branch (<see cref="WKTGatherCheck"/>).</item>
    /// <item>The ladder's nightmare attacks whoever falls too far. Its attack is shared: on every machine it flies at the
    /// player who fell, and kills any local player it touches on the way, so one player's slip can take down those below
    /// them unless they dodge it.</item>
    /// </list>
    /// Owned by <see cref="WKTWorldSync"/>.
    /// </summary>
    internal sealed class WKTLadderSecret {
        /// <summary>The blockers' object names, exact. Nothing else is ever removed.</summary>
        public static readonly string[] BLOCKER_NAMES = { "SecretVentBlocker", "L_VentBlocker_Eye" };
        /// <summary>The game's nightmare kills within this distance (<c>DEN_LadderNightmare.Attack</c>).</summary>
        public const float KILL_DISTANCE = 3f;
        /// <summary>How fast the nightmare flies at its victim, in m/s (<c>DEN_LadderNightmare.Attack</c>).</summary>
        public const float ATTACK_SPEED = 20f;
        /// <summary>How far around a teleport's path blockers are looked for.</summary>
        public const float SCAN_RADIUS = 0.75f;
        /// <summary>A second attack this soon after one is the same attack, reported by another machine.</summary>
        public const float ATTACK_DEBOUNCE = 5f;
        private const string c_attack_sound = "nightmare:attack";
        private const string c_death_type = "laddernightmare";
        private const string c_vent_clip_prefix = "vent_hit";
        private static readonly string[] s_debrisParticles = { "hit-metal", "spark-small" };
        private const int c_debris_amount = 12;
        private static readonly Collider[] s_overlaps = new Collider[32];
        private static readonly List<GameObject> s_blockers = new();
        private static readonly List<Bounds> s_bounds = new();
        private static readonly List<string> s_names = new();
        private static AudioClip[]? s_ventClips;
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        /// <summary>Opened blockers: their level's key and path under it.</summary>
        private readonly HashSet<(ulong Level, string Path)> m_opened = new();
        private byte m_victim;
        private bool m_hasVictim;
        private Vector3 m_lastTarget;
        private float m_offsetBeforeAttack;
        private float m_lastAttack = float.NegativeInfinity;

        public WKTLadderSecret(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
        }

        public void Clear() {
            m_opened.Clear();
            m_hasVictim = false;
            m_lastAttack = float.NegativeInfinity;
        }

        #region Vent

        /// <summary>Whether an object name is a ladder vent blocker's.</summary>
        public static bool IsBlockerName(string name) => Array.IndexOf(BLOCKER_NAMES, name) >= 0;

        /// <summary>
        /// Of candidate blockers' bounds, the one a teleport from <paramref name="from"/> to <paramref name="to"/> went
        /// through, or else the one nearest its path; -1 for none.
        /// </summary>
        public static int PickBlocker(Vector3 from, Vector3 to, IReadOnlyList<Bounds> candidates) {
            var best = -1;
            var bestDistance = float.PositiveInfinity;
            var middle = Vector3.Lerp(from, to, 0.5f);
            for (var i = 0; i < candidates.Count; i++) {
                var bounds = candidates[i];
                if (SegmentHits(from, to, bounds.min, bounds.max)) return i;
                var distance = DistanceToSegment(ClosestPoint(bounds.min, bounds.max, middle), from, to);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = i;
            }
            return best;
        }

        /// <summary>Whether the segment from <paramref name="from"/> to <paramref name="to"/> touches the box (slab test).</summary>
        public static bool SegmentHits(Vector3 from, Vector3 to, Vector3 min, Vector3 max) {
            var enter = 0f;
            var exit = 1f;
            for (var axis = 0; axis < 3; axis++) {
                var start = from[axis];
                var delta = to[axis] - start;
                if (Mathf.Abs(delta) < 1e-6f) {
                    if (start < min[axis] || start > max[axis]) return false;
                    continue;
                }
                var t0 = (min[axis] - start) / delta;
                var t1 = (max[axis] - start) / delta;
                if (t0 > t1)
                    (t0, t1) = (t1, t0);
                enter = Mathf.Max(enter, t0);
                exit = Mathf.Min(exit, t1);
                if (enter > exit) return false;
            }
            return true;
        }

        private static Vector3 ClosestPoint(Vector3 min, Vector3 max, Vector3 point) =>
            new(Mathf.Clamp(point.x, min.x, max.x), Mathf.Clamp(point.y, min.y, max.y), Mathf.Clamp(point.z, min.z, max.z));

        public static float DistanceToSegment(Vector3 point, Vector3 from, Vector3 to) {
            var path = to - from;
            var lengthSquared = path.sqrMagnitude;
            var t = lengthSquared > 0f ? Mathf.Clamp01(Vector3.Dot(point - from, path) / lengthSquared) : 0f;
            return Vector3.Distance(point, from + path * t);
        }

        /// <summary>The local player teleported: a ladder vent blocker they went through opens for everyone.</summary>
        public void OnLocalTeleport(Vector3 from, Vector3 to) {
            var count = Physics.OverlapCapsuleNonAlloc(from, to, SCAN_RADIUS, s_overlaps, ~0, QueryTriggerInteraction.Collide);
            for (var i = 0; i < count; i++) {
                var collider = s_overlaps[i];
                if (collider == null || !IsBlockerName(collider.gameObject.name) || s_blockers.Contains(collider.gameObject)) continue;
                s_blockers.Add(collider.gameObject);
                s_bounds.Add(collider.bounds);
            }
            Array.Clear(s_overlaps, 0, count);
            var picked = PickBlocker(from, to, s_bounds);
            var blocker = picked >= 0 ? s_blockers[picked] : null;
            s_blockers.Clear();
            s_bounds.Clear();
            if (blocker == null || !TryGetAddress(blocker, out var level, out var path)) return;
            Open(level, path, true);
            var writer = m_network.BeginMessage(WKTMessageId.VentOpened);
            writer.Put(level);
            writer.Put(path, WKTWire.MAX_PATH_LENGTH);
            if (m_sync.IsHost)
                m_network.Broadcast(writer, SendType.Reliable);
            else
                m_network.SendToHost(writer, SendType.Reliable);
        }

        public void OnVentOpened(WKTPlayer sender, NetDataReader reader) {
            if (!reader.TryGetULong(out var level) || !WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_PATH_LENGTH, out var path) || path.Length == 0) return;
            if (m_sync.IsHost) {
                if (sender.IsHost) return;
                var relay = m_network.BeginMessage(WKTMessageId.VentOpened);
                relay.Put(level);
                relay.Put(path, WKTWire.MAX_PATH_LENGTH);
                m_network.Broadcast(relay, SendType.Reliable, sender);
            }
            else if (!sender.IsHost) return;
            Open(level, path, true);
        }

        /// <summary>A level was keyed: blockers opened before it was built here are gone from it too, quietly.</summary>
        public void OnLevelKeyed(M_Level level, ulong levelKey) {
            foreach (var opened in m_opened) {
                if (opened.Level == levelKey)
                    Remove(level, opened.Path, false);
            }
        }

        /// <summary>Host only: tells a player who just joined which blockers are open.</summary>
        public void SendAll(WKTPlayer player) {
            foreach (var opened in m_opened) {
                var writer = m_network.BeginMessage(WKTMessageId.VentOpened);
                writer.Put(opened.Level);
                writer.Put(opened.Path, WKTWire.MAX_PATH_LENGTH);
                m_network.SendToPlayer(player, writer, SendType.Reliable);
            }
        }

        private void Open(ulong levelKey, string path, bool loud) {
            // Blockers are only ever found by name, so a path not ending in one is not one: nothing else can be removed.
            if (!IsBlockerName(LastName(path)) || !m_opened.Add((levelKey, path))) return;
            if (WKTContentSeeding.TryGetLevel(levelKey, out var level) && level != null)
                Remove(level, path, loud);
        }

        private static void Remove(M_Level level, string path, bool loud) {
            var blocker = level.transform.Find(path);
            if (blocker == null || !blocker.gameObject.activeSelf) return;
            var center = blocker.TryGetComponent<Renderer>(out var renderer) ? renderer.bounds.center : blocker.position;
            blocker.gameObject.SetActive(false);
            WKT.Instance.Logger.LogInfo($"Opened the ladder vent blocker {path} in {level.name}.");
            if (loud)
                PlayBreak(center);
        }

        /// <summary>The game's vent hit sound and some debris where the blocker was.</summary>
        private static void PlayBreak(Vector3 position) {
            var clip = GetVentClip();
            if (clip != null)
                AudioManager.PlaySound(clip, position);
            foreach (var particle in s_debrisParticles)
                FXManager.PlayParticle(particle, position, c_debris_amount);
        }

        private static AudioClip? GetVentClip() {
            if (s_ventClips == null || s_ventClips.Length == 0) {
                var found = new List<AudioClip>();
                foreach (var clip in Resources.FindObjectsOfTypeAll<AudioClip>()) {
                    if (clip != null && clip.name.StartsWith(c_vent_clip_prefix, StringComparison.Ordinal))
                        found.Add(clip);
                }
                s_ventClips = found.ToArray();
            }
            return s_ventClips.Length > 0 ? s_ventClips[UnityEngine.Random.Range(0, s_ventClips.Length)] : null;
        }

        /// <summary>A blocker's level key and path below the level, which name it on every machine (it has no script to key).</summary>
        private static bool TryGetAddress(GameObject blocker, out ulong levelKey, out string path) {
            levelKey = 0;
            path = string.Empty;
            var level = blocker.GetComponentInParent<M_Level>(true);
            if (level == null || !WKTContentSeeding.TryGetLevelKey(level, out levelKey)) return false;
            path = GetPath(level.transform, blocker.transform);
            return path.Length > 0 && path.Length <= WKTWire.MAX_PATH_LENGTH;
        }

        private static string GetPath(Transform root, Transform target) {
            for (var current = target; current != null && current != root; current = current.parent)
                s_names.Add(current.name);
            s_names.Reverse();
            var path = string.Join("/", s_names);
            s_names.Clear();
            return path;
        }

        private static string LastName(string path) {
            var slash = path.LastIndexOf('/');
            return slash < 0 ? path : path.Substring(slash + 1);
        }

        #endregion

        #region Nightmare

        /// <summary>
        /// The nightmare's frame. False runs this machine's shared attack instead of the game's: at the victim, killing any
        /// local player it touches. Before an attack, the game's own update runs, and its attack starting is told to everyone.
        /// </summary>
        public bool BeforeNightmareUpdate(DEN_LadderNightmare nightmare) {
            if (!Nightmare.Attacking(nightmare)) return true;
            if (!m_hasVictim) {
                // The game started an attack here this frame: the local player fell.
                var local = m_network.LocalPlayer;
                if (local == null) return true;
                StartAttack(nightmare, local.Id, false);
                SendAttack(local.Id);
            }
            if (CL_GameManager.GetGamemodeName(false) != "Ladder") return true;
            FlyAtVictim(nightmare);
            return false;
        }

        public void OnNightmareAttack(WKTPlayer sender, NetDataReader reader) {
            byte victim;
            if (m_sync.IsHost) {
                if (sender.IsHost) return;
                victim = sender.Id;
                if (Time.time - m_lastAttack < ATTACK_DEBOUNCE) return;
                SendAttack(victim, sender);
            }
            else if (!sender.IsHost || !reader.TryGetByte(out victim)) return;
            var nightmare = Nightmare.Instance?.GetValue(null) as DEN_LadderNightmare;
            if (nightmare == null || m_hasVictim) return;
            StartAttack(nightmare, victim, true);
        }

        private void SendAttack(byte victim, WKTPlayer? except = null) {
            m_lastAttack = Time.time;
            var writer = m_network.BeginMessage(WKTMessageId.LadderNightmare);
            if (m_sync.IsHost) {
                writer.Put(victim);
                m_network.Broadcast(writer, SendType.Reliable, except);
            }
            else
                m_network.SendToHost(writer, SendType.Reliable);
        }

        private void StartAttack(DEN_LadderNightmare nightmare, byte victim, bool playSound) {
            m_hasVictim = true;
            m_victim = victim;
            m_lastAttack = Time.time;
            m_offsetBeforeAttack = nightmare.playerOffset;
            m_lastTarget = nightmare.transform.position;
            Nightmare.Attacking(nightmare) = true;
            if (playSound)
                Nightmare.ClipHandler(nightmare)?.PlaySound(c_attack_sound);
            WKT.Instance.Logger.LogInfo($"The ladder nightmare attacks player {victim}.");
        }

        /// <summary>The game's attack, aimed at the victim wherever they are, and deadly to every local player on its way.</summary>
        private void FlyAtVictim(DEN_LadderNightmare nightmare) {
            nightmare.playerOffset = 0f;
            var victimAlive = TryGetVictimPosition(out var target);
            if (victimAlive)
                m_lastTarget = target;
            var transform = nightmare.transform;
            var toTarget = m_lastTarget - transform.position;
            transform.position += Vector3.ClampMagnitude(toTarget.normalized * (ATTACK_SPEED * Time.deltaTime), toTarget.magnitude);
            transform.rotation *= Quaternion.Euler(0f, Time.deltaTime * nightmare.spinSpeed * 3f, 0f);
            CL_CameraControl.Shake(0.3f * Time.deltaTime);
            var local = ENT_Player.playerObject;
            if (local != null && !local.dead && Vector3.Distance(transform.position, local.transform.position) < KILL_DISTANCE)
                local.Kill(c_death_type);
            // The victim is gone and the nightmare reached where they were: it goes back to lurking.
            if (!victimAlive && toTarget.sqrMagnitude < KILL_DISTANCE * KILL_DISTANCE)
                EndAttack(nightmare);
        }

        private bool TryGetVictimPosition(out Vector3 position) {
            position = default;
            var local = m_network.LocalPlayer;
            if (local != null && local.Id == m_victim) {
                var player = ENT_Player.playerObject;
                if (player == null || player.dead) return false;
                position = player.transform.position;
                return true;
            }
            if (WKTPlayerDeaths.IsDead(m_victim)) return false;
            return WKTPlayerPositions.TryGetRemotePosition(m_victim, out position);
        }

        private void EndAttack(DEN_LadderNightmare nightmare) {
            Nightmare.Attacking(nightmare) = false;
            nightmare.playerOffset = m_offsetBeforeAttack;
            m_hasVictim = false;
        }

        #endregion

        /// <summary>The nightmare's private state, bound on first use.</summary>
        private static class Nightmare {
            public static readonly AccessTools.FieldRef<DEN_LadderNightmare, bool> Attacking = AccessTools.FieldRefAccess<DEN_LadderNightmare, bool>("attacking");
            public static readonly AccessTools.FieldRef<DEN_LadderNightmare, UT_AudioClipHandler> ClipHandler =
                AccessTools.FieldRefAccess<DEN_LadderNightmare, UT_AudioClipHandler>("clipHandler");
            /// <summary>The game's <c>DEN_LadderNightmare.instance</c>: the last nightmare to start.</summary>
            public static readonly FieldInfo? Instance = AccessTools.Field(typeof(DEN_LadderNightmare), "instance");
        }
    }
}
