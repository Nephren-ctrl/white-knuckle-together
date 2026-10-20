using System;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Where each spawn slot of a multiplayer run is placed around the level's single spawn point. Pure, so every machine
    /// computes the same layout. Slot 0 is the original spawn. Every other slot owns a disjoint set of candidates on two
    /// rings around it, so a slot falling back to another candidate can never land on a spot another slot may use.
    /// </summary>
    internal static class WKTSpawnLayout {
        public const float INNER_RADIUS = 1.5f;
        public const float OUTER_RADIUS = 2.25f;
        /// <summary>Candidate angles each ring slot owns per ring. Their first candidates are spread evenly around the ring.</summary>
        public const int ANGLES_PER_SLOT = 3;
        public const int RING_COUNT = 2;
        public const int CANDIDATES_PER_RING_SLOT = ANGLES_PER_SLOT * RING_COUNT;
        private const int c_ring_slots = WKTLobbyConsts.MAX_PLAYERS - 1;
        private const int c_ring_positions = c_ring_slots * ANGLES_PER_SLOT;
        /// <summary>Measured clockwise from the spawn's forward, so slot 1 first tries the spawn's right.</summary>
        private const float c_start_angle_degrees = 90f;
        private static readonly Vector3[] s_offsets = BuildOffsets();

        public static int GetCandidateCount(int slot) {
            if ((uint)slot >= WKTLobbyConsts.MAX_PLAYERS) return 0;
            return slot == 0 ? 1 : CANDIDATES_PER_RING_SLOT;
        }

        /// <summary>
        /// The offset of <paramref name="slot"/>'s <paramref name="candidate"/>-th spot (in order of preference) in the
        /// spawn's horizontal frame: x is right and z is forward. False if either is out of range.
        /// </summary>
        public static bool TryGetLocalOffset(int slot, int candidate, out Vector3 offset) {
            offset = Vector3.zero;
            if ((uint)candidate >= (uint)GetCandidateCount(slot)) return false;
            if (slot == 0) return true;
            offset = s_offsets[(slot - 1) * CANDIDATES_PER_RING_SLOT + candidate];
            return true;
        }

        /// <summary>
        /// Places <paramref name="localOffset"/> around a spawn point. Only the spawn's yaw is used, so a pitched or
        /// rolled spawn still lays the slots out horizontally.
        /// </summary>
        public static Vector3 ToWorld(Vector3 spawnPosition, Quaternion spawnRotation, Vector3 localOffset) {
            var forward = spawnRotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) {
                // Looking straight up or down: the spawn's up still points the way its forward would have.
                forward = spawnRotation * Vector3.up;
                forward.y = 0f;
            }
            forward = forward.sqrMagnitude < 1e-6f ? Vector3.forward : forward.normalized;
            var right = new Vector3(forward.z, 0f, -forward.x);
            return spawnPosition + right * localOffset.x + forward * localOffset.z;
        }

        private static Vector3[] BuildOffsets() {
            var offsets = new Vector3[c_ring_slots * CANDIDATES_PER_RING_SLOT];
            for (var slot = 0; slot < c_ring_slots; slot++) {
                for (var ring = 0; ring < RING_COUNT; ring++) {
                    var radius = ring == 0 ? INNER_RADIUS : OUTER_RADIUS;
                    for (var angle = 0; angle < ANGLES_PER_SLOT; angle++) {
                        // Each slot owns a contiguous arc, so the first candidates of the slots are a third of a turn apart.
                        var position = slot * ANGLES_PER_SLOT + angle;
                        var radians = (c_start_angle_degrees + position * 360.0 / c_ring_positions) * Math.PI / 180.0;
                        offsets[slot * CANDIDATES_PER_RING_SLOT + ring * ANGLES_PER_SLOT + angle] =
                            new Vector3((float)(Math.Sin(radians) * radius), 0f, (float)(Math.Cos(radians) * radius));
                    }
                }
            }
            return offsets;
        }
    }
}
