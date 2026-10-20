using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>The geometry of Grab Assists, apart from Unity objects so it can be tested.</summary>
    internal static class WKTGrabAssistMath {
        /// <summary>How much further a hand reaches when it reaches for another player's open hand.</summary>
        public const float EXTRA_REACH = 2f;
        /// <summary>How close the aim must pass to an open hand to take it.</summary>
        public const float HAND_RADIUS = 0.35f;
        /// <summary>
        /// How much of the pull toward the other player a grounded player feels while the other hangs in the air: they are
        /// braced against the ground, so they haul the other up instead of being dragged off.
        /// </summary>
        public const float GROUNDED_PULL = 0.25f;
        /// <summary>How far, beyond reach, the host accepts a request, allowing for the requester's view being a little behind.</summary>
        public const float HOST_SLACK = 2f;

        /// <summary>
        /// Whether looking along <paramref name="direction"/> (normalized) from <paramref name="eye"/> takes the hand at
        /// <paramref name="hand"/>: in front, within <paramref name="reach"/>, and the aim passing within
        /// <see cref="HAND_RADIUS"/> of it. <paramref name="distance"/> is how far along the aim it is.
        /// </summary>
        public static bool IsAimedAt(Vector3 eye, Vector3 direction, Vector3 hand, float reach, out float distance) {
            var toHand = hand - eye;
            distance = Vector3.Dot(toHand, direction);
            if (distance <= 0f || toHand.sqrMagnitude > reach * reach) return false;
            var closest = eye + direction * distance;
            return (hand - closest).sqrMagnitude <= HAND_RADIUS * HAND_RADIUS;
        }

        /// <summary>
        /// Whether a free hand is held out open for another player to take: empty, its grab held, and in focus mode when
        /// <paramref name="needsFocus"/>. Only such a hand is shown green and can be taken.
        /// </summary>
        public static bool IsOffering(bool holdsItem, bool grabHeld, bool needsFocus, bool inFocus) => !holdsItem && grabHeld && (!needsFocus || inFocus);

        /// <summary>
        /// Where the local player's hand holds on: the point the other player holds their hand out at, or, while the local
        /// player stands on the ground and the other does not, only part of the way there from <paramref name="localAnchor"/>,
        /// so the grounded player is pulled less.
        /// </summary>
        public static Vector3 GetHoldPoint(Vector3 localAnchor, Vector3 partnerHand, bool localGrounded, bool partnerGrounded) =>
            localGrounded && !partnerGrounded ? Vector3.Lerp(localAnchor, partnerHand, GROUNDED_PULL) : partnerHand;

        /// <summary>
        /// Where the local player's hold moves to from <paramref name="currentHold"/>. A partner held up by nothing (not on the
        /// ground, not hanging from the world) cannot lift anyone: the hold may swing and sink but never rise. Otherwise two
        /// players holding each other would each pull the other up, and fly.
        /// </summary>
        public static Vector3 LimitRise(Vector3 wantedHold, float currentHold, bool partnerAnchored) {
            if (!partnerAnchored && wantedHold.y > currentHold)
                wantedHold.y = currentHold;
            return wantedHold;
        }

        /// <summary>
        /// Whether a partner is held up by something solid: on the ground, or a hand hanging from the world rather than from
        /// another player's hand (<paramref name="leftLinked"/>, <paramref name="rightLinked"/>).
        /// </summary>
        public static bool IsAnchored(bool grounded, bool leftHanging, bool leftLinked, bool rightHanging, bool rightLinked) =>
            grounded || (leftHanging && !leftLinked) || (rightHanging && !rightLinked);
    }
}
