using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Finds what the local player stands on or hangs from as a <see cref="WKTPlatformRef"/>, and places remote players on
    /// this machine's copy of what they stand on. Moving things run on every machine, so a rider placed on the local copy
    /// moves with it exactly, where their world position alone would trail behind by the interpolation delay and by however
    /// far the copies are apart. Only while the platform moves (and <see cref="MOVING_HOLD_SECONDS"/> after), and only in
    /// keyed levels of a synced run or on the Hadal Diver (<see cref="DIVER_KEY"/>); anything else is sent and placed by world position, so a hierarchy that differs between
    /// machines can never misplace someone standing on still ground.
    /// </summary>
    internal sealed class WKTPlatforms {
        /// <summary>
        /// A placement this far from the world position is not trusted: the hierarchy differs here, or the copies are too far
        /// apart to be the same moment. The world position is used instead.
        /// </summary>
        public const float MAX_CORRECTION = 6f;
        /// <summary>How long a platform counts as moving after it last moved, so a rider is not let go the moment it stops.</summary>
        public const float MOVING_HOLD_SECONDS = 1f;
        /// <summary>Slower than this, in metres per second (or the same of turning at a metre out), a platform is still.</summary>
        public const float MOVING_SPEED = 0.05f;
        /// <summary>
        /// The level key that stands for the Hadal Diver, which has no level above it once it unparents itself. Each machine
        /// rides its own diver, each started when its own player got on, so riders are placed on the local diver's segment
        /// however far its world position is from theirs. The segments are built alike everywhere (content seeding).
        /// </summary>
        public const ulong DIVER_KEY = ulong.MaxValue;
        /// <summary>How far up from a platform a level's root is looked for.</summary>
        private const int c_max_search_depth = 24;
        /// <summary>Resolved platforms kept before the cache starts over.</summary>
        private const int c_max_cached = 64;
        /// <summary>How long a platform not found here waits before it is looked for again, as its level may load.</summary>
        private const float c_retry_seconds = 1f;
        private static readonly AccessTools.FieldRef<ENT_Player, SoftParent?> s_softParent = AccessTools.FieldRefAccess<ENT_Player, SoftParent?>("softParent");
        private readonly ushort[] m_indices = new ushort[WKTPlatformRef.MAX_DEPTH];
        private readonly Dictionary<WKTPlatformRef, (Transform? Transform, float RetryAt)> m_resolved = new();
        private Transform? m_lastPlatform;
        private WKTPlatformRef m_lastFrame;
        private Vector3 m_lastPosition;
        private Quaternion m_lastRotation;
        private float m_lastTime;
        private float m_movingUntil;
        /// <summary>What the local player rode at the last physics step, and their place on it then.</summary>
        private Transform? m_fixedPlatform;
        private Vector3 m_fixedLocal;

        /// <summary>
        /// Called after each physics step, where the local player moves and is pulled by their hands. Between steps, movers
        /// move the platform every frame while the player stays put until the game carries them, so their place on it read then
        /// is off by however far it moved since: a sawtooth beating against the frame rate. Read here, it is exact.
        /// </summary>
        public void SampleFixed(ENT_Player player) {
            m_fixedPlatform = GetPlatform(player);
            if (m_fixedPlatform != null)
                m_fixedLocal = m_fixedPlatform.InverseTransformPoint(player.transform.position);
        }

        /// <summary>What the local player stands on, or else what a hand hangs from, with the player's place on it.</summary>
        public WKTPlatformRef GetLocal(ENT_Player player) {
            var platform = GetPlatform(player);
            if (platform == null) return default;
            var now = Time.unscaledTime;
            platform.GetPositionAndRotation(out var position, out var rotation);
            if (platform != m_lastPlatform) {
                m_lastPlatform = platform;
                m_lastFrame = TryDescribe(platform, out var frame) ? frame : default;
                m_movingUntil = 0f;
            }
            else if (IsMoving(m_lastPosition, m_lastRotation, position, rotation, now - m_lastTime))
                m_movingUntil = now + MOVING_HOLD_SECONDS;
            m_lastPosition = position;
            m_lastRotation = rotation;
            m_lastTime = now;
            if (!m_lastFrame.IsSet || now >= m_movingUntil) return default;
            var frameRef = m_lastFrame;
            var local = platform == m_fixedPlatform ? m_fixedLocal : platform.InverseTransformPoint(player.transform.position);
            return new WKTPlatformRef(frameRef.LevelKey, frameRef.Depth, frameRef.PathLow, frameRef.PathHigh, frameRef.NameHash, local);
        }

        /// <summary>
        /// Moves <paramref name="pose"/> onto this machine's copy of its platform, if there is one close enough to where its
        /// world position says (world-offset-corrected by <paramref name="worldOffset"/>).
        /// </summary>
        public WKTPlayerPose PlaceOnPlatform(in WKTPlayerPose pose, float worldOffset) {
            if (!pose.Platform.IsSet || !TryResolve(pose.Platform, out var platform)) return pose;
            var world = pose.Position;
            world.y += worldOffset;
            var delta = platform.TransformPoint(pose.Platform.Local) - world;
            return pose.Platform.LevelKey == DIVER_KEY || IsTrusted(delta) ? pose.Shifted(delta) : pose;
        }

        /// <summary>Whether a platform moved faster than <see cref="MOVING_SPEED"/> over <paramref name="deltaTime"/>.</summary>
        public static bool IsMoving(Vector3 fromPosition, Quaternion fromRotation, Vector3 toPosition, Quaternion toRotation, float deltaTime) {
            if (deltaTime <= 0f) return false;
            var limit = MOVING_SPEED * deltaTime;
            return (toPosition - fromPosition).sqrMagnitude > limit * limit || Quaternion.Angle(fromRotation, toRotation) * Mathf.Deg2Rad > limit;
        }

        /// <summary>Whether a placement <paramref name="delta"/> away from the world position is believed.</summary>
        public static bool IsTrusted(Vector3 delta) => delta.sqrMagnitude <= MAX_CORRECTION * MAX_CORRECTION;

        public void Clear() {
            m_resolved.Clear();
            m_lastPlatform = null;
            m_lastFrame = default;
            m_fixedPlatform = null;
        }

        private static Transform? GetPlatform(ENT_Player player) {
            var softParent = s_softParent(player);
            if (softParent != null && softParent.IsParented()) {
                var parent = softParent.GetParent();
                if (parent != null) return parent;
            }
            foreach (var hand in player.hands) {
                if (hand != null && hand.IsHanging() && hand.handhold != null)
                    return hand.handhold.transform;
            }
            return null;
        }

        /// <summary>
        /// The path down from the keyed level holding <paramref name="platform"/>, or from the Hadal Diver, if it is shallow
        /// enough to send.
        /// </summary>
        private bool TryDescribe(Transform platform, out WKTPlatformRef frame) {
            frame = default;
            var depth = 0;
            var current = platform;
            var diver = DEN_HadalDiver.instance != null ? DEN_HadalDiver.instance.transform : null;
            for (var step = 0; step < c_max_search_depth && current != null; step++) {
                var levelKey = DIVER_KEY;
                if (current == diver || (current.TryGetComponent<M_Level>(out var level) && WKTContentSeeding.TryGetLevelKey(level, out levelKey))) {
                    var pathLow = 0UL;
                    var pathHigh = 0UL;
                    // Collected from the platform up; the path runs from the root down.
                    for (var i = 0; i < depth; i++)
                        WKTPlatformRef.SetIndex(ref pathLow, ref pathHigh, i, m_indices[depth - 1 - i]);
                    frame = new WKTPlatformRef(levelKey, (byte)depth, pathLow, pathHigh, GetNameHash(platform), Vector3.zero);
                    return true;
                }
                if (depth == WKTPlatformRef.MAX_DEPTH) return false;
                var index = current.GetSiblingIndex();
                if (index > ushort.MaxValue) return false;
                m_indices[depth++] = (ushort)index;
                current = current.parent;
            }
            return false;
        }

        private bool TryResolve(in WKTPlatformRef platform, out Transform transform) {
            var frame = new WKTPlatformRef(platform.LevelKey, platform.Depth, platform.PathLow, platform.PathHigh, platform.NameHash, Vector3.zero);
            var now = Time.unscaledTime;
            if (m_resolved.TryGetValue(frame, out var cached)) {
                if (cached.Transform != null) {
                    transform = cached.Transform;
                    return true;
                }
                // Not found last time, or destroyed since.
                if (cached.Transform is null && now < cached.RetryAt) {
                    transform = null!;
                    return false;
                }
            }
            if (m_resolved.Count >= c_max_cached)
                m_resolved.Clear();
            var found = Walk(frame);
            m_resolved[frame] = (found, now + c_retry_seconds);
            transform = found!;
            return found != null;
        }

        private static Transform? Walk(in WKTPlatformRef frame) {
            Transform current;
            if (frame.LevelKey == DIVER_KEY) {
                if (DEN_HadalDiver.instance == null) return null;
                current = DEN_HadalDiver.instance.transform;
            }
            else if (WKTContentSeeding.TryGetLevel(frame.LevelKey, out var level))
                current = level!.transform;
            else
                return null;
            for (var i = 0; i < frame.Depth; i++) {
                var index = frame.GetIndex(i);
                if (index >= current.childCount) return null;
                current = current.GetChild(index);
            }
            return GetNameHash(current) == frame.NameHash ? current : null;
        }

        private static uint GetNameHash(Transform transform) => (uint)WKTStableHash.Of(transform.name);
    }
}
