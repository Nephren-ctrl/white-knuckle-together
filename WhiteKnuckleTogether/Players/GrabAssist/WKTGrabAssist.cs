using System;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using SendType = Steamworks.Data.SendType;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Grab Assists (a lobby option): holding grab with an empty hand in focus mode (or without focus mode, if the player turned
    /// that setting off) holds it out open, shown green to the others. Grabbing toward another player's open hand, from a
    /// little further than a normal grab reaches, takes hold of it. Both players
    /// then hang from each other like from a handhold, each from the point where the other holds their hand out, so the two
    /// pull on each other like a tug of war. A player standing on the ground is pulled far less than one in the air, so they
    /// can haul the other up; a player held up by nothing solid can lift nobody, so two players holding each other cannot fly.
    /// Either letting go, dying or leaving frees both hands.
    /// <para>
    /// Each machine moves only its own player: the game's own hanging does the pulling, toward a stand-in handhold the mod
    /// moves with the other player's body. The host decides who holds whom, so no hand is ever taken twice.
    /// </para>
    /// </summary>
    internal sealed class WKTGrabAssist : MonoBehaviour {
        private const int c_hands = WKTHandLink.HAND_COUNT;
        /// <summary>How often a hand that keeps grabbing toward an open hand asks the host again.</summary>
        private const float c_request_interval = 0.25f;
        private const string c_hold_name = "WKT Grab Assist Hold";
        private static readonly Vector3 s_localOpenOffset = new(0.2f, -0.1f, 1.1f);
        /// <summary>
        /// The game's focus mode (held or toggled), in which hands ignore handholds. With <see cref="WKTConfig.GrabAssistsNeedFocus"/>
        /// on, a hand is only offered in it, so a climbing grab never holds a hand out by accident.
        /// </summary>
        private static readonly Func<ENT_Player, bool> s_isFocusModeActive =
            AccessTools.MethodDelegate<Func<ENT_Player, bool>>(AccessTools.Method(typeof(ENT_Player), "IsFocusModeActive"));
        private readonly WKTHandLinkTable m_links = new();
        private readonly CL_Handhold?[] m_holds = new CL_Handhold?[c_hands];
        private readonly float[] m_nextRequest = new float[c_hands];
        private WKTNetwork m_network = null!;

        [UsedImplicitly]
        private void Awake() {
            m_network = WKT.Instance.Network;
            m_network.Messages.Register(WKTMessageId.HandLink, OnMessage);
            m_network.OnPlayerDisconnected += OnPlayerGone;
        }

        [UsedImplicitly]
        private void OnDestroy() {
            for (byte hand = 0; hand < c_hands; hand++) {
                if (m_holds[hand] != null)
                    Destroy(m_holds[hand]!.gameObject);
            }
            var wkt = WKT.InstanceOrNull;
            if (wkt == null) return;
            wkt.Network.Messages.Unregister(WKTMessageId.HandLink);
            wkt.Network.OnPlayerDisconnected -= OnPlayerGone;
        }

        [UsedImplicitly]
        private void Update() {
            var local = m_network.LocalPlayer;
            var player = ENT_Player.playerObject;
            var players = WKTPlayerSync.Instance;
            if (local == null || player == null || players == null || !m_network.Session.GrabAssists) return;
            for (byte hand = 0; hand < c_hands && hand < player.hands.Length; hand++) {
                if (m_links.TryGetPartner(local.Id, hand, out var partnerId, out var partnerHand))
                    FollowPartner(player, players, hand, partnerId, partnerHand);
                else
                    TryReach(player, players, local, hand);
            }
        }

        #region Local hands

        /// <summary>Asks to take hold of an open hand the local hand grabs toward with nothing in it.</summary>
        private void TryReach(ENT_Player player, WKTPlayerSync players, WKTPlayer local, byte hand) {
            var playerHand = player.hands[hand];
            if (playerHand == null || playerHand.interactState != ENT_Player.InteractType.none || playerHand.inventoryHand?.currentItem != null ||
                Time.unscaledTime < m_nextRequest[hand] || !IsGrabHeld(playerHand)) return;
            var eye = player.camTransform.position;
            var aim = player.camTransform.forward;
            var reach = player.GetInteractDistance(hand) + WKTGrabAssistMath.EXTRA_REACH;
            var found = false;
            var bestDistance = float.MaxValue;
            byte targetId = 0;
            byte targetHand = 0;
            foreach (var other in m_network.GetPlayersEnumerable()) {
                if (other == local || WKTPlayerDeaths.IsDead(other.Id) || !players.TryGetRemote(other.Id, out var remote)) continue;
                for (byte otherHand = 0; otherHand < c_hands; otherHand++) {
                    if (!remote.IsHandOpen(otherHand) || m_links.IsLinked(other.Id, otherHand)) continue;
                    if (!WKTGrabAssistMath.IsAimedAt(eye, aim, remote.GetHandPosition(otherHand), reach, out var distance) || distance >= bestDistance) continue;
                    found = true;
                    bestDistance = distance;
                    targetId = other.Id;
                    targetHand = otherHand;
                }
            }
            if (!found) return;
            m_nextRequest[hand] = Time.unscaledTime + c_request_interval;
            var request = new WKTHandLink(WKTHandLinkKind.Request, local.Id, hand, targetId, targetHand);
            if (m_network.Mode == NetworkMode.Host)
                HostHandleRequest(local, request);
            else
                Send(request);
        }

        /// <summary>Keeps the stand-in handhold where the partner holds their hand out, and notices the local hand letting go.</summary>
        private void FollowPartner(ENT_Player player, WKTPlayerSync players, byte hand, byte partnerId, byte partnerHand) {
            var hold = m_holds[hand];
            var playerHand = player.hands[hand];
            if (hold == null || playerHand == null || playerHand.handhold != hold || playerHand.interactState != ENT_Player.InteractType.hanging ||
                WKTPlayerDeaths.IsDead(partnerId) || !players.TryGetRemote(partnerId, out var remote)) {
                LetGo(hand);
                return;
            }
            var anchor = GetLocalOpenPoint(player, hand);
            var wanted = WKTGrabAssistMath.GetHoldPoint(anchor, remote.GetOpenHandPoint(partnerHand), player.IsGrounded(false), remote.IsGrounded);
            var anchored = WKTGrabAssistMath.IsAnchored(remote.IsGrounded, remote.IsHandHanging(0), m_links.IsLinked(partnerId, 0), remote.IsHandHanging(1),
                m_links.IsLinked(partnerId, 1));
            hold.transform.position = WKTGrabAssistMath.LimitRise(wanted, hold.transform.position.y, anchored);
        }

        /// <summary>Takes hold of the partner's hand on this machine, through a stand-in handhold.</summary>
        private void Attach(byte hand, byte partnerId, byte partnerHand) {
            var player = ENT_Player.playerObject;
            var players = WKTPlayerSync.Instance;
            if (player == null || players == null || hand >= player.hands.Length || !players.TryGetRemote(partnerId, out var remote)) {
                SendRelease(hand);
                return;
            }
            var playerHand = player.hands[hand];
            // The hand grabbed something else in the meantime, or holds an item now.
            if (playerHand == null || playerHand.interactState != ENT_Player.InteractType.none || playerHand.inventoryHand?.currentItem != null) {
                SendRelease(hand);
                return;
            }
            var hold = EnsureHold(player, hand);
            if (hold == null) {
                SendRelease(hand);
                return;
            }
            hold.transform.position = remote.GetOpenHandPoint(partnerHand);
            playerHand.GrabHold(hold.transform, hold.transform.position);
        }

        /// <summary>The local hand let go: frees the link everywhere.</summary>
        private void LetGo(byte hand) {
            var local = m_network.LocalPlayer;
            if (local == null || !m_links.IsLinked(local.Id, hand)) return;
            StopHand(hand);
            SendRelease(hand);
        }

        private static void StopHand(byte hand) {
            var player = ENT_Player.playerObject;
            if (player == null || hand >= player.hands.Length) return;
            var playerHand = player.hands[hand];
            if (playerHand != null && playerHand.interactState == ENT_Player.InteractType.hanging && playerHand.handhold != null &&
                playerHand.handhold.name.StartsWith(c_hold_name))
                player.StopInteraction(hand);
        }

        /// <summary>A copy of the game's own stand-in handhold for this hand (made for its "grab anything" buff), kept hidden until used.</summary>
        private CL_Handhold? EnsureHold(ENT_Player player, byte hand) {
            if (m_holds[hand] != null) return m_holds[hand];
            var fakes = player.grabAnywhereFakeHandholds;
            if (fakes == null || hand >= fakes.Length || fakes[hand] == null) {
                WKT.Instance.Logger.LogError("Grab Assists found no stand-in handhold to copy.");
                return null;
            }
            var copy = Instantiate(fakes[hand].gameObject);
            copy.name = c_hold_name + " " + hand;
            copy.SetActive(true);
            var hold = copy.GetComponent<CL_Handhold>();
            m_holds[hand] = hold;
            return hold;
        }

        private static Vector3 GetLocalOpenPoint(ENT_Player player, byte hand) {
            var offset = s_localOpenOffset;
            if (hand == 0)
                offset.x = -offset.x;
            return player.camTransform.TransformPoint(offset);
        }

        /// <summary>Whether the local player holds a hand out to be taken: <see cref="WKTGrabAssistMath.IsOffering"/>.</summary>
        public static bool IsOffering(ENT_Player player, bool holdsItem, bool grabHeld) =>
            WKTGrabAssistMath.IsOffering(holdsItem, grabHeld, IsFocusRequired, grabHeld && !holdsItem && s_isFocusModeActive(player));

        private static bool IsFocusRequired => WKTPlugin.ModConfig is not { GrabAssistsNeedFocus: { Value: false } };

        private static bool IsGrabHeld(ENT_Player.Hand hand) {
            if (string.IsNullOrEmpty(hand.fireButton)) return false;
            var button = InputManager.GetButton(hand.fireButton);
            return button != null && button.Pressed;
        }

        #endregion

        #region Network

        private void OnMessage(WKTPlayer sender, NetDataReader reader) {
            if (!WKTHandLink.TryRead(reader, out var link)) return;
            if (m_network.Mode == NetworkMode.Host) {
                if (sender.IsHost || link.PlayerId != sender.Id) return;
                switch (link.Kind) {
                    case WKTHandLinkKind.Request:
                        HostHandleRequest(sender, link);
                        return;
                    case WKTHandLinkKind.Release:
                        HostRelease(link.PlayerId, link.Hand);
                        return;
                }
                return;
            }
            if (!sender.IsHost) return;
            switch (link.Kind) {
                case WKTHandLinkKind.Link:
                    ApplyLink(link);
                    return;
                case WKTHandLinkKind.Release:
                    ApplyRelease(link.PlayerId, link.Hand);
                    return;
            }
        }

        /// <summary>Host only: links the hands if both are free, the taken one is open, and the taker is close enough to it.</summary>
        private void HostHandleRequest(WKTPlayer requester, in WKTHandLink request) {
            if (!m_network.Session.GrabAssists || !m_links.CanLink(request.PlayerId, request.Hand, request.OtherId, request.OtherHand)) return;
            if (WKTPlayerDeaths.IsDead(request.PlayerId) || WKTPlayerDeaths.IsDead(request.OtherId)) return;
            if (!IsOpenNear(request.OtherId, request.OtherHand, requester)) return;
            var link = new WKTHandLink(WKTHandLinkKind.Link, request.PlayerId, request.Hand, request.OtherId, request.OtherHand);
            var writer = m_network.BeginMessage(WKTMessageId.HandLink);
            link.Write(writer);
            m_network.Broadcast(writer, SendType.Reliable);
            ApplyLink(link);
        }

        /// <summary>Host only: whether the hand is open as the host sees it, and the requester within reach of it.</summary>
        private bool IsOpenNear(byte otherId, byte otherHand, WKTPlayer requester) {
            var player = ENT_Player.playerObject;
            var players = WKTPlayerSync.Instance;
            if (player == null || players == null) return false;
            var local = m_network.LocalPlayer;
            Vector3 handPoint;
            if (local != null && otherId == local.Id) {
                // The host's own hand: open if it is offered as the others see it.
                var hand = player.hands[otherHand];
                if (hand == null || hand.interactState != ENT_Player.InteractType.none ||
                    !WKTGrabAssistMath.IsOffering(hand.inventoryHand?.currentItem != null, IsGrabHeld(hand), IsFocusRequired, s_isFocusModeActive(player)))
                    return false;
                handPoint = GetLocalOpenPoint(player, otherHand);
            }
            else {
                if (!players.TryGetRemote(otherId, out var other) || !other.IsHandOpen(otherHand)) return false;
                handPoint = other.GetHandPosition(otherHand);
            }
            Vector3 requesterPosition;
            if (local != null && requester.Id == local.Id)
                requesterPosition = player.camTransform.position;
            else if (players.TryGetRemote(requester.Id, out var remoteRequester))
                requesterPosition = remoteRequester.Head.position;
            else
                return false;
            var limit = player.GetInteractDistance() + WKTGrabAssistMath.EXTRA_REACH + WKTGrabAssistMath.HOST_SLACK;
            return (handPoint - requesterPosition).sqrMagnitude <= limit * limit;
        }

        private void HostRelease(byte playerId, byte hand) {
            if (!m_links.TryGetPartner(playerId, hand, out _, out _)) return;
            var writer = m_network.BeginMessage(WKTMessageId.HandLink);
            new WKTHandLink(WKTHandLinkKind.Release, playerId, hand).Write(writer);
            m_network.Broadcast(writer, SendType.Reliable);
            ApplyRelease(playerId, hand);
        }

        private void ApplyLink(in WKTHandLink link) {
            if (!m_links.Link(link.PlayerId, link.Hand, link.OtherId, link.OtherHand)) return;
            var local = m_network.LocalPlayer;
            if (local == null) return;
            if (link.PlayerId == local.Id)
                Attach(link.Hand, link.OtherId, link.OtherHand);
            else if (link.OtherId == local.Id)
                Attach(link.OtherHand, link.PlayerId, link.Hand);
        }

        private void ApplyRelease(byte playerId, byte hand) {
            var local = m_network.LocalPlayer;
            if (!m_links.TryGetPartner(playerId, hand, out var partnerId, out var partnerHand)) return;
            m_links.Release(playerId, hand);
            if (local == null) return;
            if (playerId == local.Id)
                StopHand(hand);
            else if (partnerId == local.Id)
                StopHand(partnerHand);
        }

        private void SendRelease(byte hand) {
            var local = m_network.LocalPlayer;
            if (local == null) return;
            if (m_network.Mode == NetworkMode.Host) {
                HostRelease(local.Id, hand);
                return;
            }
            // Freed here at once, so the hand can reach again; the host confirms for everyone.
            m_links.Release(local.Id, hand);
            Send(new WKTHandLink(WKTHandLinkKind.Release, local.Id, hand));
        }

        private void Send(in WKTHandLink link) {
            var writer = m_network.BeginMessage(WKTMessageId.HandLink);
            link.Write(writer);
            m_network.SendToHost(writer, SendType.Reliable);
        }

        private void OnPlayerGone(WKTPlayer player) {
            var local = m_network.LocalPlayer;
            for (byte hand = 0; hand < c_hands; hand++) {
                if (local != null && m_links.TryGetPartner(player.Id, hand, out var partnerId, out var partnerHand) && partnerId == local.Id)
                    StopHand(partnerHand);
            }
            m_links.ReleasePlayer(player.Id);
        }

        #endregion
    }
}
