using System.Collections.Generic;
using Steamworks.Data;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Enemies a session event spawns at locators near the player who entered a place (<c>SessionEventModule_LocatorSpawner</c>,
    /// such as drone drops). A client's own spawns stay hidden like anything else it builds, so it sends the host what it
    /// spawned, and the host spawns the same for everyone. Each place's spawns are counted, by its level and locator group:
    /// whichever machine first makes a place's next spawn has it made, and the others' same spawn is skipped. Owned by
    /// <see cref="WKTWorldSync"/>.
    /// </summary>
    internal sealed class WKTEventSpawns {
        private const int c_max_spawns = 32;
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        /// <summary>How many spawns this machine's own events made at each place.</summary>
        private readonly Dictionary<(ulong Level, string Locator), int> m_local = new();
        /// <summary>Host only: how many spawns were made at each place, by anyone.</summary>
        private readonly Dictionary<(ulong Level, string Locator), int> m_made = new();
        private readonly List<WKTNetIdentity> m_captured = new();
        private (ulong Level, string Locator) m_place;
        private int m_index;
        private bool m_capturing;

        public WKTEventSpawns(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
        }

        public void Clear() {
            m_local.Clear();
            m_made.Clear();
            m_captured.Clear();
            m_capturing = false;
        }

        /// <summary>
        /// A locator spawner is about to spawn. False skips it: on the host, a spawn some client already had made there. On a
        /// client, what it spawns is collected for the host.
        /// </summary>
        public bool BeforeSpawn(SessionEventModule_LocatorSpawner spawner) {
            if (CL_EventManager.currentLevel is not { } level || !WKTContentSeeding.TryGetLevelKey(level, out var levelKey)) return true;
            var place = (levelKey, spawner.locatorID ?? string.Empty);
            var index = m_local.TryGetValue(place, out var count) ? count + 1 : 1;
            m_local[place] = index;
            if (m_sync.IsHost) return TryMake(place, index);
            m_place = place;
            m_index = index;
            m_captured.Clear();
            m_capturing = true;
            return true;
        }

        /// <summary>Client only: a spawn collected since <see cref="BeforeSpawn"/> goes to the host.</summary>
        public void AfterSpawn() {
            if (!m_capturing) return;
            m_capturing = false;
            var count = 0;
            foreach (var identity in m_captured) {
                if (identity != null && identity.Entity != null)
                    count++;
            }
            if (count == 0 || count > c_max_spawns) {
                m_captured.Clear();
                return;
            }
            var offset = WKTWorldOffset.Get();
            var writer = m_network.BeginMessage(WKTMessageId.EventSpawn);
            writer.Put(m_place.Level);
            writer.Put(m_place.Locator);
            writer.Put(m_index);
            writer.Put((byte)count);
            foreach (var identity in m_captured) {
                if (identity == null || identity.Entity == null) continue;
                var spawned = identity.transform;
                writer.Put(WKTWorldSync.GetPrefabId(identity));
                WKTWire.Put(writer, WKTWorldOffset.ToWire(spawned.position, offset));
                WKTWire.Put(writer, spawned.rotation);
            }
            m_captured.Clear();
            m_network.SendToHost(writer, SendType.Reliable);
        }

        /// <summary>
        /// Client only: an object this machine built that no host object matches, such as a spawn being collected. A creature
        /// nested in another (a barnacle zombie's barnacle) comes with its prefab.
        /// </summary>
        public void OnUnmatched(WKTNetIdentity identity) {
            if (m_capturing && identity.Entity != null && identity.NestedIn == null)
                m_captured.Add(identity);
        }

        public void OnMessage(WKTPlayer sender, NetDataReader reader) {
            if (!m_sync.IsHost || sender.IsHost || !reader.TryGetULong(out var levelKey) || !reader.TryGetString(out var locator) ||
                !reader.TryGetInt(out var index) || !reader.TryGetByte(out var count) || count > c_max_spawns) return;
            if (!WKTContentSeeding.TryGetLevel(levelKey, out var level) || level == null || !TryMake((levelKey, locator), index)) return;
            var offset = WKTWorldOffset.Get();
            for (var i = 0; i < count; i++) {
                if (!reader.TryGetString(out var prefabId) || !WKTWire.TryGetPosition(reader, out var position) || !WKTWire.TryGetRotation(reader, out var rotation)) return;
                var prefab = WKTWorldSync.GetPrefab(WKTNetKind.Enemy, prefabId);
                if (prefab != null)
                    Object.Instantiate(prefab, WKTWorldOffset.FromWire(position, offset), rotation, level.transform);
            }
            WKT.Instance.Logger.LogInfo($"Spawned {count} event enemies a client's {locator} spawner made in {level.name}.");
        }

        /// <summary>Host only: whether a place's <paramref name="index"/>th spawn is still to be made, which it now is.</summary>
        private bool TryMake((ulong, string) place, int index) {
            if (m_made.TryGetValue(place, out var made) && made >= index) return false;
            m_made[place] = index;
            return true;
        }
    }
}
