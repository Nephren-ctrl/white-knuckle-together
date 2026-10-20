using System.Collections.Generic;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using SendType = Steamworks.Data.SendType;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A player's flashlight as others light it: which flashlight (its item prefab, for its lamp), and the lamp's brightness
    /// and flicker as the game sets them from its charge. An empty prefab means it is off.
    /// </summary>
    public readonly struct WKTPlayerLightState {
        public const float MAX_VALUE = 100f;
        public readonly byte PlayerId;
        public readonly string Prefab;
        public readonly float Intensity;
        public readonly float FlickerAmplitude;
        public readonly float FlickerSpeed;

        public WKTPlayerLightState(byte playerId, string? prefab, float intensity, float flickerAmplitude, float flickerSpeed) {
            PlayerId = playerId;
            Prefab = prefab ?? string.Empty;
            Intensity = intensity;
            FlickerAmplitude = flickerAmplitude;
            FlickerSpeed = flickerSpeed;
        }

        public bool IsOn => Prefab.Length > 0;

        /// <summary>Whether this differs enough from <paramref name="other"/> to be worth sending.</summary>
        public bool DiffersFrom(in WKTPlayerLightState other) =>
            Prefab != other.Prefab || Mathf.Abs(Intensity - other.Intensity) > 0.02f || Mathf.Abs(FlickerAmplitude - other.FlickerAmplitude) > 0.05f ||
            Mathf.Abs(FlickerSpeed - other.FlickerSpeed) > 0.1f;

        public void Write(NetDataWriter writer) {
            writer.Put(PlayerId);
            writer.Put(Prefab, WKTWire.MAX_ID_LENGTH);
            writer.Put(Intensity);
            writer.Put(FlickerAmplitude);
            writer.Put(FlickerSpeed);
        }

        public static bool TryRead(NetDataReader reader, out WKTPlayerLightState state) {
            state = default;
            if (!reader.TryGetByte(out var playerId) || !WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var prefab) ||
                !TryGetValue(reader, out var intensity) || !TryGetValue(reader, out var amplitude) || !TryGetValue(reader, out var speed)) return false;
            state = new WKTPlayerLightState(playerId, prefab, intensity, amplitude, speed);
            return true;
        }

        private static bool TryGetValue(NetDataReader reader, out float value) =>
            reader.TryGetFloat(out value) && !float.IsNaN(value) && value >= 0f && value <= MAX_VALUE;
    }

    /// <summary>
    /// Shares each player's flashlight, so every machine lights the same places: a remote player's flashlight shines from
    /// their head as a stowed flashlight does, turning smoothly after where they look. Sent when it changes, a few times a
    /// second at most, through the host, which keeps everyone's for players who join later. Owned by <see cref="WKTPlayerSync"/>.
    /// </summary>
    internal sealed class WKTPlayerLights {
        private const float c_min_interval = 0.2f;
        private readonly WKTNetwork m_network;
        private readonly Dictionary<byte, WKTPlayerLightState> m_states = new();
        private WKTPlayerLightState m_sent;
        private bool m_sentOnce;
        private float m_nextSend;

        public WKTPlayerLights(WKTNetwork network) => m_network = network;

        public bool TryGet(byte playerId, out WKTPlayerLightState state) => m_states.TryGetValue(playerId, out state);

        public void Forget(byte playerId) => m_states.Remove(playerId);

        public void Update(ENT_Player player) {
            if (m_network.LocalPlayer is not { } local || Time.unscaledTime < m_nextSend) return;
            m_nextSend = Time.unscaledTime + c_min_interval;
            var state = Read(player, local.Id);
            if (m_sentOnce && !state.DiffersFrom(m_sent)) return;
            m_sentOnce = true;
            m_sent = state;
            var writer = m_network.BeginMessage(WKTMessageId.PlayerLight);
            state.Write(writer);
            if (m_network.Mode == NetworkMode.Host)
                m_network.Broadcast(writer, SendType.Reliable);
            else
                m_network.SendToHost(writer, SendType.Reliable);
        }

        public void OnMessage(WKTPlayer sender, NetDataReader reader) {
            if (!WKTPlayerLightState.TryRead(reader, out var state)) return;
            if (m_network.Mode == NetworkMode.Host) {
                if (sender.IsHost || state.PlayerId != sender.Id) return;
                var relay = m_network.BeginMessage(WKTMessageId.PlayerLight);
                state.Write(relay);
                m_network.Broadcast(relay, SendType.Reliable, sender);
            }
            else if (!sender.IsHost) return;
            m_states[state.PlayerId] = state;
        }

        public void SendAll(WKTPlayer player) {
            foreach (var state in m_states.Values) {
                if (state.PlayerId == player.Id) continue;
                var writer = m_network.BeginMessage(WKTMessageId.PlayerLight);
                state.Write(writer);
                m_network.SendToPlayer(player, writer, SendType.Reliable);
            }
            if (!m_sentOnce) return;
            var own = m_network.BeginMessage(WKTMessageId.PlayerLight);
            m_sent.Write(own);
            m_network.SendToPlayer(player, own, SendType.Reliable);
        }

        /// <summary>The brightest lit flashlight the player carries, in hand or stowed.</summary>
        private static WKTPlayerLightState Read(ENT_Player player, byte localId) {
            var inventory = Inventory.instance;
            Item_Object_Flashlight? best = null;
            if (inventory != null && !player.dead) {
                if (inventory.bagItems != null) {
                    foreach (var item in inventory.bagItems)
                        Consider(item, ref best);
                }
                if (inventory.itemHands != null) {
                    foreach (var hand in inventory.itemHands)
                        Consider(hand?.currentItem, ref best);
                }
            }
            if (best == null || best.itemData == null || string.IsNullOrEmpty(best.itemData.prefabName)) return new WKTPlayerLightState(localId, null, 0f, 0f, 0f);
            var bestLamp = best.lamp;
            return new WKTPlayerLightState(localId, best.itemData.prefabName, Mathf.Min(bestLamp.intensity, WKTPlayerLightState.MAX_VALUE),
                Mathf.Clamp(bestLamp.animAmplitude, 0f, WKTPlayerLightState.MAX_VALUE), Mathf.Clamp(bestLamp.animSpeed, 0f, WKTPlayerLightState.MAX_VALUE));
        }

        private static void Consider(Item? item, ref Item_Object_Flashlight? best) {
            if (item == null || item.GetDropObject(false) is not { } dropObject || !dropObject.TryGetComponent<Item_Object_Flashlight>(out var flashlight) ||
                flashlight.lamp == null || !flashlight.lamp.gameObject.activeInHierarchy || flashlight.lamp.intensity <= 0f) return;
            if (best == null || flashlight.lamp.intensity > best.lamp.intensity)
                best = flashlight;
        }
    }
}
