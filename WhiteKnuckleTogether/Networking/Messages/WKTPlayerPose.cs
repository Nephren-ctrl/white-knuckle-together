using System;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Where a player is and where they look. <see cref="Position"/> is in world-offset-corrected space (the game recenters
    /// the world on each machine independently), <see cref="Yaw"/> is in [0, 360) and <see cref="Pitch"/> in
    /// [-<see cref="MAX_PITCH"/>, <see cref="MAX_PITCH"/>], positive looking down like the game's camera. <see cref="Crouch"/>
    /// is how far the body has shrunk from standing to crouching, so it follows the game's crouch transition,
    /// <see cref="Left"/> and <see cref="Right"/> are the hands, <see cref="Sounds"/> the latest sounds the body made and
    /// <see cref="Health"/> how much of their health is left. <see cref="Platform"/> is what they stand on or hang from, if
    /// others can find it, to place them on their own copy of it.
    /// </summary>
    public readonly struct WKTPlayerPose : IEquatable<WKTPlayerPose>, IWKTInterpolatable<WKTPlayerPose> {
        public const float MAX_PITCH = 90f;
        /// <summary>Generous bound on any coordinate. Rejects garbage without limiting how high a run can climb.</summary>
        public const float MAX_COORDINATE = 1e7f;
        public const byte MAX_HEALTH = WKTFraction.MAX;

        public Vector3 Position { get; }
        public float Yaw { get; }
        public float Pitch { get; }
        /// <summary>0 standing, <see cref="WKTFraction.MAX"/> fully crouched.</summary>
        public byte Crouch { get; }
        public float CrouchPercent => WKTFraction.ToFloat(Crouch);
        public WKTHandPose Left { get; }
        public WKTHandPose Right { get; }
        public WKTPlayerSounds Sounds { get; }
        /// <summary>Whether the player is dead. Dead players are not targets, and do not hold the group's doors.</summary>
        public bool Dead { get; }
        /// <summary>Standing on something, as opposed to hanging or in the air. Grab Assists pulls a grounded player less.</summary>
        public bool Grounded { get; }
        /// <summary>Health as a fraction of the maximum, quantized so <see cref="MAX_HEALTH"/> is full.</summary>
        public byte Health { get; }
        public float HealthPercent => WKTFraction.ToFloat(Health);
        public WKTPlatformRef Platform { get; }

        /// <summary>Standing, both hands <see cref="WKTHandPose.Rested"/>.</summary>
        public WKTPlayerPose(Vector3 position, float yaw, float pitch)
            : this(position, yaw, pitch, 0, WKTHandPose.Rested, WKTHandPose.Rested) { }

        /// <summary>Standing.</summary>
        public WKTPlayerPose(Vector3 position, float yaw, float pitch, in WKTHandPose left, in WKTHandPose right)
            : this(position, yaw, pitch, 0, left, right) { }

        /// <summary>No sounds made yet.</summary>
        public WKTPlayerPose(Vector3 position, float yaw, float pitch, byte crouch, in WKTHandPose left, in WKTHandPose right)
            : this(position, yaw, pitch, crouch, left, right, default) { }

        /// <summary>Normalizes <paramref name="yaw"/> into [0, 360). Does not validate; see <see cref="IsValid"/>.</summary>
        public WKTPlayerPose(Vector3 position, float yaw, float pitch, byte crouch, in WKTHandPose left, in WKTHandPose right,
                             in WKTPlayerSounds sounds, bool dead = false, byte health = MAX_HEALTH, bool grounded = false,
                             in WKTPlatformRef platform = default) {
            Dead = dead;
            Platform = platform;
            Grounded = grounded;
            Health = health;
            Position = position;
            Yaw = NormalizeYaw(yaw);
            Pitch = pitch;
            Crouch = crouch;
            Left = left;
            Right = right;
            Sounds = sounds;
        }

        public static bool IsValid(Vector3 position, float yaw, float pitch) =>
            IsCoordinate(position.x) && IsCoordinate(position.y) && IsCoordinate(position.z) &&
            !float.IsNaN(yaw) && !float.IsInfinity(yaw) && pitch >= -MAX_PITCH && pitch <= MAX_PITCH;

        /// <summary>Maps any finite angle into [0, 360).</summary>
        public static float NormalizeYaw(float yaw) {
            var normalized = yaw % 360f;
            if (normalized < 0f)
                normalized += 360f;
            // -0.00001 % 360 + 360 rounds to exactly 360 in float.
            return normalized >= 360f ? 0f : normalized;
        }

        /// <summary>The shortest signed turn from <paramref name="from"/> to <paramref name="to"/>, in (-180, 180].</summary>
        public static float DeltaYaw(float from, float to) {
            var delta = NormalizeYaw(to - from);
            return delta > 180f ? delta - 360f : delta;
        }

        /// <summary>
        /// Maps health to <see cref="Health"/>, clamped to [0, 1] of <paramref name="maxHealth"/>. Without a positive maximum
        /// the player counts as unhurt.
        /// </summary>
        public static byte QuantizeHealth(float health, float maxHealth) =>
            maxHealth > 0f ? WKTFraction.Quantize(health / maxHealth) : MAX_HEALTH;

        public static bool IsCoordinate(float value) => value >= -MAX_COORDINATE && value <= MAX_COORDINATE;

        /// <summary>
        /// Interpolates position, pitch, crouch and health linearly, yaw the short way around and the hands with
        /// <see cref="WKTHandPose.Lerp"/>. Sounds are those of <paramref name="from"/> until <paramref name="t"/> reaches 1,
        /// so each is heard when playback passes the pose that brought it. The platform blends with
        /// <see cref="WKTPlatformRef.Lerp"/>. <paramref name="t"/> is not clamped.
        /// </summary>
        public static WKTPlayerPose Lerp(in WKTPlayerPose from, in WKTPlayerPose to, float t) =>
            new(from.Position + (to.Position - from.Position) * t, from.Yaw + DeltaYaw(from.Yaw, to.Yaw) * t,
                from.Pitch + (to.Pitch - from.Pitch) * t, WKTFraction.Lerp(from.Crouch, to.Crouch, t),
                WKTHandPose.Lerp(from.Left, to.Left, t), WKTHandPose.Lerp(from.Right, to.Right, t),
                t >= 1f ? to.Sounds : from.Sounds, t >= 1f ? to.Dead : from.Dead, WKTFraction.Lerp(from.Health, to.Health, t),
                t >= 0.5f ? to.Grounded : from.Grounded, WKTPlatformRef.Lerp(from.Platform, to.Platform, t));

        /// <summary>The same pose moved to <paramref name="position"/>.</summary>
        public WKTPlayerPose WithPosition(Vector3 position) => new(position, Yaw, Pitch, Crouch, Left, Right, Sounds, Dead, Health, Grounded, Platform);

        /// <summary>The same pose, body and held points, moved by <paramref name="delta"/>.</summary>
        public WKTPlayerPose Shifted(Vector3 delta) =>
            new(Position + delta, Yaw, Pitch, Crouch, Left.Shifted(delta), Right.Shifted(delta), Sounds, Dead, Health, Grounded, Platform);

        public WKTPlayerPose LerpTo(in WKTPlayerPose to, float t) => Lerp(this, to, t);

        public bool Equals(WKTPlayerPose other) =>
            Position.Equals(other.Position) && Yaw.Equals(other.Yaw) && Pitch.Equals(other.Pitch) && Crouch == other.Crouch &&
            Left.Equals(other.Left) && Right.Equals(other.Right) && Sounds.Equals(other.Sounds) && Dead == other.Dead &&
            Health == other.Health && Grounded == other.Grounded && Platform.Equals(other.Platform);

        public override bool Equals(object? obj) => obj is WKTPlayerPose other && Equals(other);

        public override int GetHashCode() {
            var hash = ((Position.GetHashCode() * 397 ^ Yaw.GetHashCode()) * 397 ^ Pitch.GetHashCode()) * 397 ^ Crouch;
            hash = ((hash * 397 ^ Left.GetHashCode()) * 397 ^ Right.GetHashCode()) * 397 ^ Sounds.GetHashCode();
            hash = ((hash * 397 ^ Health) * 2 + (Dead ? 1 : 0)) * 2 + (Grounded ? 1 : 0);
            return hash * 397 ^ Platform.GetHashCode();
        }

        public override string ToString() =>
            $"({Position.x:F2}, {Position.y:F2}, {Position.z:F2}) yaw {Yaw:F1} pitch {Pitch:F1} crouch {Crouch} left {Left} right {Right} {Sounds} health {Health}{(Dead ? " dead" : "")}{(Grounded ? " grounded" : "")}{(Platform.IsSet ? " " + Platform : "")}";
    }
}
