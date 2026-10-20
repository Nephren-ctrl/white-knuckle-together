using HarmonyLib;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using SendType = Steamworks.Data.SendType;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The lines from another player's hand to where their barnacle hook or grapple holds on. The holder sends each hand's
    /// hold point ten times a second while it holds, and once when it lets go; others draw a line from that player's hand to it.
    /// Owned by <see cref="WKTPlayerSync"/>.
    /// </summary>
    internal sealed class WKTTethers {
        public const int HANDS = 2;
        private const float c_send_interval = 0.1f;
        private const float c_width = 0.035f;
        private static readonly Color s_color = new(0.35f, 0.08f, 0.08f, 1f);
        private static readonly AccessTools.FieldRef<HandItem_GrapplingHook, bool> s_hookHolds = AccessTools.FieldRefAccess<HandItem_GrapplingHook, bool>("hasGrappledPoint");
        private static readonly AccessTools.FieldRef<HandItem_Shoot_Grapple, Projectile_Grapple?> s_grapple =
            AccessTools.FieldRefAccess<HandItem_Shoot_Grapple, Projectile_Grapple?>("grappleProjectile");
        private readonly WKTNetwork m_network;
        private readonly LineRenderer?[,] m_lines = new LineRenderer?[byte.MaxValue + 1, HANDS];
        private readonly Vector3[,] m_points = new Vector3[byte.MaxValue + 1, HANDS];
        private readonly bool[] m_sent = new bool[HANDS];
        private Material? m_material;
        private float m_nextSend;

        public WKTTethers(WKTNetwork network) => m_network = network;

        public void Clear() {
            for (var id = 0; id <= byte.MaxValue; id++)
                Forget((byte)id);
            if (m_material != null)
                Object.Destroy(m_material);
        }

        public void Forget(byte playerId) {
            for (var hand = 0; hand < HANDS; hand++) {
                if (m_lines[playerId, hand] is not { } line) continue;
                Object.Destroy(line.gameObject);
                m_lines[playerId, hand] = null;
            }
        }

        /// <summary>Sends where the local player's hooks hold on.</summary>
        public void Update(ENT_Player player) {
            if (m_network.LocalPlayer is not { } local || Time.unscaledTime < m_nextSend) return;
            m_nextSend = Time.unscaledTime + c_send_interval;
            for (var hand = 0; hand < HANDS; hand++) {
                var holds = TryGetHold(player, hand, out var point);
                if (!holds && !m_sent[hand]) continue;
                m_sent[hand] = holds;
                var writer = m_network.BeginMessage(WKTMessageId.Tether);
                writer.Put(local.Id);
                writer.Put((byte)hand);
                writer.Put(holds);
                WKTWire.Put(writer, WKTWorldOffset.ToWire(point, WKTWorldOffset.Get()));
                Send(writer, null);
            }
        }

        private static bool TryGetHold(ENT_Player player, int hand, out Vector3 point) {
            point = Vector3.zero;
            if (player.hands == null || hand >= player.hands.Length || player.hands[hand]?.inventoryHand?.currentItem?.GetHandItem() is not { } item) return false;
            switch (item) {
                case HandItem_GrapplingHook hook when s_hookHolds(hook):
                    point = hook.grapplePoint;
                    return true;
                case HandItem_Shoot_Grapple grapple when s_grapple(grapple) is { } projectile && projectile != null:
                    point = projectile.transform.position;
                    return true;
                default:
                    return false;
            }
        }

        private void Send(NetDataWriter writer, WKTPlayer? except) {
            if (m_network.Mode == NetworkMode.Host)
                m_network.Broadcast(writer, SendType.Unreliable, except);
            else
                m_network.SendToHost(writer, SendType.Unreliable);
        }

        public void OnMessage(WKTPlayer sender, NetDataReader reader) {
            if (!reader.TryGetByte(out var playerId) || !reader.TryGetByte(out var hand) || hand >= HANDS || !reader.TryGetBool(out var holds) ||
                !WKTWire.TryGetPosition(reader, out var point)) return;
            if (m_network.Mode == NetworkMode.Host) {
                if (sender.IsHost || playerId != sender.Id) return;
                var relay = m_network.BeginMessage(WKTMessageId.Tether);
                relay.Put(playerId);
                relay.Put(hand);
                relay.Put(holds);
                WKTWire.Put(relay, point);
                Send(relay, sender);
            }
            else if (!sender.IsHost) return;
            if (!holds) {
                if (m_lines[playerId, hand] is { } line)
                    line.enabled = false;
                return;
            }
            m_points[playerId, hand] = WKTWorldOffset.FromWire(point, WKTWorldOffset.Get());
            (m_lines[playerId, hand] ??= CreateLine()).enabled = true;
        }

        /// <summary>Draws each holding line from its player's hand, as shown here, to where it holds.</summary>
        public void Draw(WKTPlayerSync players) {
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                var id = WKTPlayerPositions.GetRemoteId(i);
                for (var hand = 0; hand < HANDS; hand++) {
                    if (m_lines[id, hand] is not { enabled: true } line) continue;
                    if (!players.TryGetRemote(id, out var remote) || !remote.IsShown) {
                        line.enabled = false;
                        continue;
                    }
                    line.SetPosition(0, remote.GetHandPosition(hand));
                    line.SetPosition(1, m_points[id, hand]);
                }
            }
        }

        private LineRenderer CreateLine() {
            var line = new GameObject("Remote Tether").AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.startWidth = c_width;
            line.endWidth = c_width;
            line.useWorldSpace = true;
            if (m_material == null) {
                var shader = Shader.Find("Sprites/Default");
                if (shader != null)
                    m_material = new Material(shader);
            }
            if (m_material != null)
                line.sharedMaterial = m_material;
            line.startColor = s_color;
            line.endColor = s_color;
            return line;
        }
    }
}
