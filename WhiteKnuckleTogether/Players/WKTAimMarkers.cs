using System.Collections.Generic;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using Object = UnityEngine.Object;
using SendType = Steamworks.Data.SendType;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Where another player's item aims, shown in the world: the glowing spot a blink eye or translocator will take them to
    /// while they squeeze it. The holder sends the spot ten times a second while aiming, and once when they stop; others show
    /// a copy of that item's own target effect and light there. Owned by <see cref="WKTPlayerSync"/>.
    /// </summary>
    internal sealed class WKTAimMarkers {
        private const float c_send_interval = 0.1f;
        private const float c_follow_rate = 20f;
        private readonly WKTNetwork m_network;
        private readonly Dictionary<byte, Marker> m_markers = new();
        private string m_sentPrefab = string.Empty;
        private float m_nextSend;

        public WKTAimMarkers(WKTNetwork network) => m_network = network;

        public void Clear() {
            foreach (var marker in m_markers.Values)
                marker.Destroy();
            m_markers.Clear();
        }

        public void Forget(byte playerId) {
            if (!m_markers.Remove(playerId, out var marker)) return;
            marker.Destroy();
        }

        /// <summary>Sends where the local player's blink item aims, while they squeeze it.</summary>
        public void Update(ENT_Player player) {
            if (m_network.LocalPlayer is not { } local) return;
            var aiming = FindAiming(player, out var prefab, out var position);
            if (!aiming && m_sentPrefab.Length == 0) return;
            if (aiming && Time.unscaledTime < m_nextSend) return;
            m_nextSend = Time.unscaledTime + c_send_interval;
            m_sentPrefab = aiming ? prefab : string.Empty;
            var writer = m_network.BeginMessage(WKTMessageId.AimMarker);
            writer.Put(local.Id);
            writer.Put(m_sentPrefab, WKTWire.MAX_ID_LENGTH);
            WKTWire.Put(writer, WKTWorldOffset.ToWire(position, WKTWorldOffset.Get()));
            Send(writer, null);
        }

        private void Send(NetDataWriter writer, WKTPlayer? except) {
            if (m_network.Mode == NetworkMode.Host)
                m_network.Broadcast(writer, SendType.Unreliable, except);
            else
                m_network.SendToHost(writer, SendType.Unreliable);
        }

        private static bool FindAiming(ENT_Player player, out string prefab, out Vector3 position) {
            prefab = string.Empty;
            position = Vector3.zero;
            if (player.hands == null) return false;
            foreach (var hand in player.hands) {
                if (hand?.inventoryHand?.currentItem is not { } item || item.GetHandItem() is not HandItem_BlinkEye { used: true } eye || eye.blinkTargetEffect == null ||
                    string.IsNullOrEmpty(item.prefabName)) continue;
                prefab = item.prefabName;
                position = eye.blinkTargetEffect.transform.position;
                return true;
            }
            return false;
        }

        public void OnMessage(WKTPlayer sender, NetDataReader reader) {
            if (!reader.TryGetByte(out var playerId) || !WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var prefab) ||
                !WKTWire.TryGetPosition(reader, out var position)) return;
            if (m_network.Mode == NetworkMode.Host) {
                if (sender.IsHost || playerId != sender.Id) return;
                var relay = m_network.BeginMessage(WKTMessageId.AimMarker);
                relay.Put(playerId);
                relay.Put(prefab, WKTWire.MAX_ID_LENGTH);
                WKTWire.Put(relay, position);
                Send(relay, sender);
            }
            else if (!sender.IsHost) return;
            if (string.IsNullOrEmpty(prefab)) {
                Forget(playerId);
                return;
            }
            if (!m_markers.TryGetValue(playerId, out var marker) || marker.Prefab != prefab) {
                marker?.Destroy();
                marker = Marker.Create(prefab);
                if (marker == null) return;
                m_markers[playerId] = marker;
            }
            marker.Target = WKTWorldOffset.FromWire(position, WKTWorldOffset.Get());
        }

        /// <summary>Moves the shown markers smoothly toward where their players aim.</summary>
        public void Follow() {
            foreach (var marker in m_markers.Values)
                marker.Follow(Time.deltaTime * c_follow_rate);
        }

        private sealed class Marker {
            public readonly string Prefab;
            private readonly GameObject? m_effect;
            private readonly GameObject? m_lamp;
            private bool m_placed;
            public Vector3 Target;

            private Marker(string prefab, GameObject? effect, GameObject? lamp) {
                Prefab = prefab;
                m_effect = effect;
                m_lamp = lamp;
            }

            public static Marker? Create(string prefabName) {
                Item_Object? prefab;
                try {
                    prefab = CL_AssetManager.GetItemObjectPrefab(prefabName);
                }
                catch (System.Exception) {
                    prefab = null;
                }
                if (prefab?.itemData?.handItemAsset is not HandItem_BlinkEye eye) return null;
                var effect = eye.blinkTargetEffect != null ? Copy(eye.blinkTargetEffect.gameObject) : null;
                var lamp = eye.blinkTargetLamp != null ? Copy(eye.blinkTargetLamp.gameObject) : null;
                if (effect != null && effect.TryGetComponent<ParticleSystem>(out var particles))
                    particles.Play(true);
                if (lamp != null && lamp.TryGetComponent<CL_Lamp>(out var light))
                    light.lampActive = true;
                return new Marker(prefabName, effect, lamp);
            }

            private static GameObject Copy(GameObject source) {
                var holder = new GameObject("Remote Aim");
                holder.SetActive(false);
                var copy = Object.Instantiate(source, holder.transform, false);
                copy.transform.SetParent(null, false);
                Object.Destroy(holder);
                copy.SetActive(true);
                return copy;
            }

            public void Follow(float blend) {
                Move(m_effect, blend);
                Move(m_lamp, blend);
                m_placed = true;
            }

            private void Move(GameObject? part, float blend) {
                if (part == null) return;
                var partTransform = part.transform;
                partTransform.position = m_placed ? Vector3.Lerp(partTransform.position, Target, Mathf.Clamp01(blend)) : Target;
            }

            public void Destroy() {
                if (m_effect != null)
                    Object.Destroy(m_effect);
                if (m_lamp != null)
                    Object.Destroy(m_lamp);
            }
        }
    }
}
