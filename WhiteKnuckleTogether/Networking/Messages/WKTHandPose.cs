using System;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// One hand of a player: whether it holds on to something, where (world-offset-corrected like
    /// <see cref="WKTPlayerPose.Position"/>, zero while not grabbing), whether that hold carries the player's weight, and
    /// how much grip stamina it has left, quantized so <see cref="MAX_STAMINA"/> is full.
    /// </summary>
    public readonly struct WKTHandPose : IEquatable<WKTHandPose> {
        public const byte MAX_STAMINA = WKTFraction.MAX;
        /// <summary>Not grabbing, full stamina: a hand at rest.</summary>
        public static readonly WKTHandPose Rested = new(false, Vector3.zero, MAX_STAMINA);

        public bool IsGrabbing { get; }
        /// <summary>Hanging from geometry, as opposed to holding a prop or a lever. Only while <see cref="IsGrabbing"/>.</summary>
        public bool IsHanging { get; }
        /// <summary>
        /// Not holding on, but held out in front of the player: holding an item, or reaching to grab with nothing in reach.
        /// It then follows where they look, so others see where they aim or try to grab.
        /// </summary>
        public bool IsReaching { get; }
        /// <summary>
        /// Reaching with an empty hand, grab held (in focus mode, if the player needs it): offered to another player to take
        /// hold of (Grab Assists). Only while <see cref="IsReaching"/>.
        /// </summary>
        public bool IsOpen { get; }
        public Vector3 Position { get; }
        public byte Stamina { get; }
        public float StaminaPercent => WKTFraction.ToFloat(Stamina);

        /// <summary>Not hanging.</summary>
        public WKTHandPose(bool isGrabbing, Vector3 position, byte stamina) : this(isGrabbing, position, stamina, false) { }

        /// <summary>
        /// <paramref name="position"/> and <paramref name="isHanging"/> are only kept while <paramref name="isGrabbing"/>.
        /// Does not validate.
        /// </summary>
        public WKTHandPose(bool isGrabbing, Vector3 position, byte stamina, bool isHanging, bool isReaching = false, bool isOpen = false) {
            IsGrabbing = isGrabbing;
            IsHanging = isGrabbing && isHanging;
            IsReaching = !isGrabbing && isReaching;
            IsOpen = IsReaching && isOpen;
            Position = isGrabbing ? position : Vector3.zero;
            Stamina = stamina;
        }

        /// <summary>The same hand, its hold point moved by <paramref name="delta"/> while it holds on.</summary>
        public WKTHandPose Shifted(Vector3 delta) => IsGrabbing ? new WKTHandPose(true, Position + delta, Stamina, IsHanging) : this;

        /// <summary>Maps a stamina fraction to <see cref="Stamina"/>. Out-of-range and NaN fractions are clamped to [0, 1].</summary>
        public static byte QuantizeStamina(float percent) => WKTFraction.Quantize(percent);

        public static bool IsValid(Vector3 position) =>
            WKTPlayerPose.IsCoordinate(position.x) && WKTPlayerPose.IsCoordinate(position.y) &&
            WKTPlayerPose.IsCoordinate(position.z);

        /// <summary>
        /// Stamina blends. Grabbing and hanging switch halfway; a hand grabbing on both sides moves between the two points,
        /// and one grabbing on one side only stays on that side's point.
        /// </summary>
        public static WKTHandPose Lerp(in WKTHandPose from, in WKTHandPose to, float t) {
            var stamina = WKTFraction.Lerp(from.Stamina, to.Stamina, t);
            var nearest = t >= 0.5f ? to : from;
            if (from.IsGrabbing && to.IsGrabbing)
                return new WKTHandPose(true, from.Position + (to.Position - from.Position) * t, stamina, nearest.IsHanging);
            return new WKTHandPose(nearest.IsGrabbing, nearest.Position, stamina, nearest.IsHanging, nearest.IsReaching, nearest.IsOpen);
        }

        public bool Equals(WKTHandPose other) =>
            IsGrabbing == other.IsGrabbing && IsHanging == other.IsHanging && IsReaching == other.IsReaching && IsOpen == other.IsOpen &&
            Position.Equals(other.Position) &&
            Stamina == other.Stamina;

        public override bool Equals(object? obj) => obj is WKTHandPose other && Equals(other);

        public override int GetHashCode() =>
            ((((IsGrabbing.GetHashCode() * 397 ^ IsHanging.GetHashCode()) * 397 ^ IsReaching.GetHashCode()) * 397 ^ IsOpen.GetHashCode()) * 397 ^
             Position.GetHashCode()) * 397 ^ Stamina;

        public override string ToString() =>
            IsGrabbing
                ? $"{(IsHanging ? "hanging" : "grabbing")} ({Position.x:F2}, {Position.y:F2}, {Position.z:F2}) stamina {Stamina}"
                : $"{(IsOpen ? "open" : IsReaching ? "reaching" : "free")} stamina {Stamina}";
    }
}
