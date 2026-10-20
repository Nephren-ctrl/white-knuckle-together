using System;
using System.Collections.Generic;
using HarmonyLib;
using Pathfinding;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The Vent Thing's hand and arms. Its <c>LateUpdate</c> moves the hand along a path from its body and lays an arm along
    /// that path (<c>CalculateArms</c>); all of it is AI. The simulating machine sends the hand's position, whether it is
    /// retreating, its stamina and whether it holds someone, and the arm's path points (packed by <see cref="WKTOffsetCodec"/>
    /// from the body), and which player it holds. The puppet lays its arms along those points as <c>CalculateArms</c> does,
    /// and fades its fatigue and tension loops as the game does. On the held player's own machine it shows what
    /// <c>PullTarget</c> shows the held player: the hand over their camera, reddening as it tires, and the grab's vignettes,
    /// until it lets go (see <see cref="WKTVentThingGrabs"/>).
    /// </summary>
    internal sealed class WKTVentThingPresenter : WKTPuppetPresenterBase {
        /// <summary>The most arm points sent. A longer path keeps its first points; the last arm still reaches the hand.</summary>
        public const int MAX_POINTS = 32;
        /// <summary><c>CalculateArms</c>' cap on arm objects.</summary>
        private const int c_max_arms = 50;
        private const float c_follow_rate = 14f;
        private const float c_snap_distance = 6f;
        private const byte c_retreating = 1;
        private const byte c_holding = 2;
        private const float c_grab_rate = 5f;
        private const string c_bloodied = "bloodied";
        private const string c_fade = "fade";
        private const string c_hurt = "hurt";
        private static readonly AccessTools.FieldRef<DEN_VentThing, Path> s_reachPath = AccessTools.FieldRefAccess<DEN_VentThing, Path>("reachPath");
        private static readonly AccessTools.FieldRef<DEN_VentThing, List<Vector3>> s_chasePlayerPath = AccessTools.FieldRefAccess<DEN_VentThing, List<Vector3>>("chasePlayerPath");
        private static readonly AccessTools.FieldRef<DEN_VentThing, int> s_currentWaypoint = AccessTools.FieldRefAccess<DEN_VentThing, int>("currentWaypoint");
        private static readonly AccessTools.FieldRef<DEN_VentThing, int> s_lastWaypoint = AccessTools.FieldRefAccess<DEN_VentThing, int>("lastWaypoint");
        private static readonly AccessTools.FieldRef<DEN_VentThing, Vector3> s_navOffset = AccessTools.FieldRefAccess<DEN_VentThing, Vector3>("navOffset");
        private static readonly AccessTools.FieldRef<DEN_VentThing, bool> s_retreating = AccessTools.FieldRefAccess<DEN_VentThing, bool>("retreating");
        private static readonly AccessTools.FieldRef<DEN_VentThing, bool> s_hasGrabbedTarget = AccessTools.FieldRefAccess<DEN_VentThing, bool>("hasGrabbedTarget");
        private static readonly AccessTools.FieldRef<DEN_VentThing, float> s_curHandStamina = AccessTools.FieldRefAccess<DEN_VentThing, float>("curHandStamina");
        private static readonly AccessTools.FieldRef<DEN_VentThing, Rigidbody> s_handRigidbody = AccessTools.FieldRefAccess<DEN_VentThing, Rigidbody>("handRigidbody");
        private static readonly AccessTools.FieldRef<DEN_VentThing, List<Transform>> s_arms = AccessTools.FieldRefAccess<DEN_VentThing, List<Transform>>("arms");
        private static readonly AccessTools.FieldRef<DEN_VentThing, List<MeshRenderer>> s_armRenderers = AccessTools.FieldRefAccess<DEN_VentThing, List<MeshRenderer>>("armRenderers");
        private static readonly int s_retreatingBool = Animator.StringToHash("retreating");
        private static readonly int s_slack = Shader.PropertyToID("_Slack");
        private static readonly int s_wiggle = Shader.PropertyToID("_Wiggle");
        /// <summary>Simulating machine: the arm's points for this frame's state, reused.</summary>
        private static readonly List<Vector3> s_points = new(MAX_POINTS + 1);
        private readonly DEN_VentThing m_ventThing;
        private readonly Vector3[] m_points = new Vector3[MAX_POINTS];
        private int m_pointCount;
        private Vector3 m_hand;
        private bool m_retreating;
        private bool m_holding;
        /// <summary>The player the hand holds, or <see cref="WKTEntityAuthority.NOBODY"/>.</summary>
        private byte m_heldPlayer = WKTEntityAuthority.NOBODY;
        /// <summary>Whether the local player is shown held here, and how far the hand over their camera has come up.</summary>
        private bool m_showingHeld;
        private float m_grabTime;
        private float m_stamina = 1f;
        private bool m_hasState;
        private bool m_handReady;

        public override bool HasState => true;

        public WKTVentThingPresenter(GameEntity entity) : base(entity) => m_ventThing = (DEN_VentThing)entity;

        public override void WriteState(NetDataWriter writer) {
            var thing = m_ventThing;
            WKTWire.Put(writer, WKTWorldOffset.ToWire(thing.hand.position, WKTWorldOffset.Get()));
            byte flags = 0;
            if (s_retreating(thing))
                flags |= c_retreating;
            if (s_hasGrabbedTarget(thing))
                flags |= c_holding;
            writer.Put(flags);
            writer.Put((byte)Mathf.RoundToInt(Mathf.Clamp01(thing.handStamina > 0f ? s_curHandStamina(thing) / thing.handStamina : 1f) * byte.MaxValue));
            writer.Put(s_hasGrabbedTarget(thing) ? HeldPlayerOf(thing) : WKTEntityAuthority.NOBODY);
            CollectPoints(thing, s_points);
            var count = Math.Min(s_points.Count, MAX_POINTS);
            writer.Put((byte)count);
            var origin = thing.transform.position;
            for (var i = 0; i < count; i++) {
                var offset = s_points[i] - origin;
                writer.Put(WKTOffsetCodec.Pack(offset.x));
                writer.Put(WKTOffsetCodec.Pack(offset.y));
                writer.Put(WKTOffsetCodec.Pack(offset.z));
            }
            s_points.Clear();
        }

        public override bool ReadState(NetDataReader reader) {
            if (!WKTWire.TryGetPosition(reader, out var hand) || !reader.TryGetByte(out var flags) || !reader.TryGetByte(out var stamina) ||
                !reader.TryGetByte(out var heldPlayer) || !reader.TryGetByte(out var count) || count > MAX_POINTS || reader.AvailableBytes < count * 3 * sizeof(short)) return false;
            var origin = m_ventThing.transform.position;
            for (var i = 0; i < count; i++)
                m_points[i] = origin + new Vector3(WKTOffsetCodec.Unpack(reader.GetShort()), WKTOffsetCodec.Unpack(reader.GetShort()), WKTOffsetCodec.Unpack(reader.GetShort()));
            m_pointCount = count;
            m_hand = WKTWorldOffset.FromWire(hand, WKTWorldOffset.Get());
            m_retreating = (flags & c_retreating) != 0;
            m_holding = (flags & c_holding) != 0;
            m_stamina = stamina / (float)byte.MaxValue;
            m_heldPlayer = m_holding ? heldPlayer : WKTEntityAuthority.NOBODY;
            m_hasState = true;
            return true;
        }

        /// <summary>The points <c>CalculateArms</c> lays the arm along, between the body and the hand.</summary>
        public static void CollectPoints(DEN_VentThing thing, List<Vector3> points) {
            points.Clear();
            var current = s_currentWaypoint(thing);
            var path = s_reachPath(thing)?.vectorPath;
            var navOffset = s_navOffset(thing);
            if (current > 0 && path != null) {
                for (var i = 0; i <= current && i < path.Count; i++)
                    points.Add(path[i] + navOffset);
            }
            if (s_lastWaypoint(thing) <= current && points.Count > current)
                points.RemoveAt(current);
            var chase = s_chasePlayerPath(thing);
            if (chase != null)
                points.AddRange(chase);
        }

        public override void PuppetUpdate(float deltaTime) {
            if (!m_hasState) return;
            var thing = m_ventThing;
            var hand = thing.hand;
            if (!m_handReady) {
                // The puppet's hand only follows the simulating machine's: it must not fall or be pushed around here.
                var body = s_handRigidbody(thing);
                if (body != null)
                    body.isKinematic = true;
                hand.position = m_hand;
                m_handReady = true;
            }
            else if ((hand.position - m_hand).sqrMagnitude > c_snap_distance * c_snap_distance)
                hand.position = m_hand;
            else
                hand.position = Vector3.Lerp(hand.position, m_hand, 1f - Mathf.Exp(-c_follow_rate * deltaTime));
            if (thing.handAnimator != null)
                thing.handAnimator.SetBool(s_retreatingBool, m_retreating);
            LayArms(thing);
            FadeLoops(thing);
            ShowHeld(thing, deltaTime);
        }

        /// <summary>The id of the player a simulating machine's Vent Thing holds: its engaged player.</summary>
        private static byte HeldPlayerOf(DEN_VentThing thing) {
            var target = WKTTargets.TargetOf(thing);
            if (target != WKTTargets.LOCAL) return target;
            var local = WKT.Instance.Network.LocalPlayer;
            return local != null ? local.Id : WKTEntityAuthority.NOBODY;
        }

        /// <summary><c>PullTarget</c>'s part for the held player, on their own machine, and <c>DropTarget</c>'s once let go.</summary>
        private void ShowHeld(DEN_VentThing thing, float deltaTime) {
            var local = WKT.Instance.Network.LocalPlayer;
            var player = ENT_Player.playerObject;
            var held = local != null && m_heldPlayer == local.Id && player != null && !player.dead;
            if (!held) {
                if (m_showingHeld)
                    Release(thing);
                return;
            }
            m_showingHeld = true;
            if (thing.handRenderer != null && thing.handRenderer.activeSelf)
                thing.handRenderer.SetActive(false);
            var sprite = thing.handSpriteRenderer;
            if (sprite != null) {
                if (!sprite.gameObject.activeSelf)
                    sprite.gameObject.SetActive(true);
                sprite.enabled = true;
                sprite.color = Color.Lerp(Color.red, Color.white, m_stamina);
                m_grabTime = Mathf.Lerp(m_grabTime, 1f, deltaTime * c_grab_rate);
                var camera = player!.cam.transform;
                var resting = camera.TransformPoint(thing.handOffset);
                sprite.transform.SetPositionAndRotation(Vector3.Lerp(resting - camera.up, resting, m_grabTime), camera.rotation);
            }
            var ui = CL_UIManager.instance;
            if (ui == null) return;
            ui.SetVignetteTarget(c_bloodied, 0.8f);
            ui.SetVignetteTarget(c_fade, 0.5f);
            ui.SetVignetteTarget(c_hurt, 0.5f);
        }

        private void Release(DEN_VentThing thing) {
            m_showingHeld = false;
            m_grabTime = 0f;
            if (thing.handRenderer != null)
                thing.handRenderer.SetActive(true);
            if (thing.handSpriteRenderer != null)
                thing.handSpriteRenderer.gameObject.SetActive(false);
            var ui = CL_UIManager.instance;
            if (ui == null) return;
            ui.SetVignetteTarget(c_bloodied, 0f);
            ui.SetVignetteTarget(c_fade, 0f);
            ui.SetVignetteTarget(c_hurt, 0f);
        }

        /// <summary><c>DEN_VentThing.CalculateArms</c> along the received points.</summary>
        private void LayArms(DEN_VentThing thing) {
            var arms = s_arms(thing);
            var renderers = s_armRenderers(thing);
            if (arms == null || renderers == null || thing.armAsset == null) return;
            var count = m_pointCount;
            var last = count - 1;
            var total = Mathf.Max(count, Mathf.Max(arms.Count, 1));
            for (var k = 0; k < total; k++) {
                if ((k >= arms.Count && arms.Count < c_max_arms) || arms.Count == 0) {
                    arms.Add(UnityEngine.Object.Instantiate(thing.armAsset, thing.transform).transform);
                    renderers.Add(arms[arms.Count - 1].GetComponent<MeshRenderer>());
                }
                if (k >= arms.Count) break;
                if (k > last && k > 0) {
                    if (arms[k].gameObject.activeSelf)
                        arms[k].gameObject.SetActive(false);
                    continue;
                }
                if (!arms[k].gameObject.activeSelf)
                    arms[k].gameObject.SetActive(true);
                var hasPoints = count > last && count > 0;
                var start = k > 0 && hasPoints ? m_points[k] : thing.transform.position;
                var end = last > k && hasPoints ? m_points[k + 1] : thing.hand.position;
                arms[k].position = start;
                if (end - start != Vector3.zero)
                    arms[k].rotation = Quaternion.LookRotation(end - start, Vector3.up);
                var length = Vector3.Distance(end, start);
                arms[k].localScale = new Vector3(1f, 1f, length);
                var slack = Mathf.Clamp(length, 0f, 1f) * 0.1f;
                var renderer = renderers[k];
                if (renderer == null) continue;
                renderer.material.SetFloat(s_slack, slack);
                renderer.material.SetFloat(s_wiggle, slack);
            }
        }

        /// <summary>The fatigue and tension loops' fades from <c>DEN_VentThing.LateUpdate</c>.</summary>
        private void FadeLoops(DEN_VentThing thing) {
            var group = thing.clipHandler != null ? thing.clipHandler.GetGroup("hand") : null;
            if (group == null) return;
            var deltaTime = CL_GameManager.GetPlayerPerceivedDeltaTime();
            var fatigue = group.GetSet("fatigue");
            if (fatigue?.aud != null)
                fatigue.SetVolume(Mathf.Lerp(fatigue.GetCurrentVolume(), 1f - m_stamina, deltaTime * 3f));
            var tension = group.GetSet("tension");
            if (tension?.aud != null)
                tension.SetVolume(Mathf.Lerp(tension.GetCurrentVolume(), m_holding ? tension.maxVolume : 0f, deltaTime));
        }
    }
}
