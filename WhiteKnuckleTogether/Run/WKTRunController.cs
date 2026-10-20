using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Drives the local side of a multiplayer run in Game-Main. Only ever created while a multiplayer run is active, so
    /// singleplayer never gets one. Moves the local player to their spawn slot once the world has loaded.
    /// </summary>
    internal sealed class WKTRunController : MonoBehaviour {
        /// <summary>How far below a candidate's feet there must be ground, so nobody spawns over a drop.</summary>
        private const float c_ground_probe_distance = 1f;
        private const float c_clearance_tolerance = 0.05f;
        /// <summary>Items the player gets this soon after spawning are what they start with, not finds.</summary>
        private const float c_start_items_seconds = 2f;
        private static float s_spawnTime = -1f;
        private byte m_slot;
        private bool m_markedStartItems;

        /// <summary>Whether the local player has been moved to their spawn slot (or the attempt finished).</summary>
        public bool HasSpawned { get; private set; }

        /// <summary>Whether the local player spawned a little while ago, past the items they start with.</summary>
        public static bool HasLocalSpawned => s_spawnTime >= 0f && Time.time - s_spawnTime > c_start_items_seconds;

        [UsedImplicitly]
        private void Awake() {
            m_slot = WKT.Instance.Network.Session.LocalSlot;
            s_spawnTime = -1f;
        }

        [UsedImplicitly]
        private void OnDestroy() => s_spawnTime = -1f;

        [UsedImplicitly]
        private void Update() {
            if (HasSpawned && !m_markedStartItems && HasLocalSpawned) {
                m_markedStartItems = true;
                WKTRunEvents.MarkStartingItems();
            }
            if (HasSpawned || !WorldLoader.isLoaded || WorldLoader.instance == null || ENT_Player.playerObject == null) return;
            HasSpawned = true;
            s_spawnTime = Time.time;
            MoveToSpawnSlot();
        }

        private void MoveToSpawnSlot() {
            var logger = WKT.Instance.Logger;
            if (m_slot == 0) {
                logger.LogInfo("Spawned in slot 0 at the level's spawn point.");
                return;
            }
            var branch = WorldLoader.instance.GetCurrentBranch();
            if (branch?.levelTracker == null || branch.levelTracker.Count == 0) {
                logger.LogWarning("No level was generated. Staying at the default spawn.");
                return;
            }
            var level = branch.levelTracker[0].GetLevel();
            if (level == null || level.spawnPosition == null) {
                logger.LogWarning("The first level has no spawn point. Staying at the default spawn.");
                return;
            }
            var spawnPosition = level.spawnPosition.position;
            var spawnRotation = level.spawnPosition.rotation;
            var player = ENT_Player.playerObject;
            // The level was instantiated this frame or shortly before, so make sure queries see its colliders.
            Physics.SyncTransforms();
            var candidateCount = WKTSpawnLayout.GetCandidateCount(m_slot);
            for (var candidate = 0; candidate < candidateCount; candidate++) {
                if (!WKTSpawnLayout.TryGetLocalOffset(m_slot, candidate, out var offset)) break;
                var position = WKTSpawnLayout.ToWorld(spawnPosition, spawnRotation, offset);
                if (!IsValidSpot(player, spawnPosition, position)) continue;
                player.Teleport(position, spawnRotation);
                logger.LogInfo(string.Format("Spawned in slot {0} (candidate {1}) at {2}.", m_slot, candidate, position));
                return;
            }
            logger.LogWarning(string.Format("No free spot for slot {0}. Staying at the level's spawn point.", m_slot));
        }

        /// <summary>
        /// Whether the player's capsule fits at <paramref name="position"/> without overlapping geometry, stands on ground,
        /// and can see the spawn point (so it is not on the far side of a wall).
        /// </summary>
        private static bool IsValidSpot(ENT_Player player, Vector3 spawnPosition, Vector3 position) {
            var controller = player.cCon;
            var radius = controller.radius;
            var center = controller.center;
            var halfSegment = Mathf.Max(0f, controller.height * 0.5f - radius);
            var mask = ~(1 << player.gameObject.layer);
            const QueryTriggerInteraction ignore_triggers = QueryTriggerInteraction.Ignore;
            var bottom = position + center - Vector3.up * halfSegment;
            var top = position + center + Vector3.up * halfSegment;
            if (Physics.CheckCapsule(bottom, top, radius - c_clearance_tolerance, mask, ignore_triggers)) return false;
            if (!Physics.Raycast(bottom, Vector3.down, radius + c_ground_probe_distance, mask, ignore_triggers)) return false;
            return !Physics.Linecast(spawnPosition + center, position + center, mask, ignore_triggers);
        }
    }
}
