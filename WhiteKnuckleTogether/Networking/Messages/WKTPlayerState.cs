using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// One sample of a player's pose, sent unreliably at <see cref="WKTNetworkConsts.SEND_RATE"/>. The sequence is
    /// the sender's send tick (see <see cref="WKTSendClock"/>), so receivers can order samples and place them on the
    /// sender's timeline even when packets are lost, duplicated or reordered.
    /// </summary>
    public readonly struct WKTPlayerState {
        /// <summary>Flags byte, stamina byte and the grab point.</summary>
        public const int HAND_SIZE = sizeof(byte) * 2 + sizeof(float) * 3;
        /// <summary>Newest sound number, sound count, then every slot's id and material, used or not.</summary>
        public const int SOUNDS_SIZE = sizeof(byte) * 2 + sizeof(byte) * 2 * WKTPlayerSounds.CAPACITY;
        public const int SIZE = sizeof(ushort) + sizeof(float) * 5 + sizeof(byte) * 3 + HAND_SIZE * 2 + SOUNDS_SIZE;
        private const byte c_body_dead = 1 << 0;
        private const byte c_body_grounded = 1 << 1;
        /// <summary>A <see cref="WKTPlatformRef"/> follows the sounds.</summary>
        private const byte c_body_platform = 1 << 2;
        private const byte c_known_body_flags = c_body_dead | c_body_grounded | c_body_platform;
        private const byte c_flag_grabbing = 1 << 0;
        private const byte c_flag_hanging = 1 << 1;
        private const byte c_flag_reaching = 1 << 2;
        private const byte c_flag_open = 1 << 3;
        private const byte c_known_flags = c_flag_grabbing | c_flag_hanging | c_flag_reaching | c_flag_open;

        public ushort Sequence { get; }
        public WKTPlayerPose Pose { get; }

        public WKTPlayerState(ushort sequence, in WKTPlayerPose pose) {
            Sequence = sequence;
            Pose = pose;
        }

        public void Write(NetDataWriter writer) {
            var position = Pose.Position;
            writer.Put(Sequence);
            writer.Put(position.x);
            writer.Put(position.y);
            writer.Put(position.z);
            writer.Put(Pose.Yaw);
            writer.Put(Pose.Pitch);
            writer.Put(Pose.Crouch);
            writer.Put(Pose.Health);
            writer.Put((byte)((Pose.Dead ? c_body_dead : 0) | (Pose.Grounded ? c_body_grounded : 0) | (Pose.Platform.IsSet ? c_body_platform : 0)));
            WriteHand(writer, Pose.Left);
            WriteHand(writer, Pose.Right);
            WriteSounds(writer, Pose.Sounds);
            if (Pose.Platform.IsSet)
                Pose.Platform.Write(writer);
        }

        /// <summary>
        /// Fails on truncated data, non-finite or out-of-range values, unknown body or hand flags, a free hand that hangs, more than
        /// <see cref="WKTPlayerSounds.CAPACITY"/> sounds, unknown sound ids and a bad platform. A free hand's grab point and unused sound
        /// slots are ignored. Trailing bytes are ignored.
        /// </summary>
        public static bool TryRead(NetDataReader reader, out WKTPlayerState state) {
            state = default;
            if (reader.AvailableBytes < SIZE) return false;
            var sequence = reader.GetUShort();
            var position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            var yaw = reader.GetFloat();
            var pitch = reader.GetFloat();
            var crouch = reader.GetByte();
            var health = reader.GetByte();
            var bodyFlags = reader.GetByte();
            if (!WKTPlayerPose.IsValid(position, yaw, pitch) || (bodyFlags & ~c_known_body_flags) != 0) return false;
            if (!TryReadHand(reader, out var left) || !TryReadHand(reader, out var right)) return false;
            if (!TryReadSounds(reader, out var sounds)) return false;
            var platform = default(WKTPlatformRef);
            if ((bodyFlags & c_body_platform) != 0 && !WKTPlatformRef.TryRead(reader, out platform)) return false;
            state = new WKTPlayerState(sequence, new WKTPlayerPose(position, yaw, pitch, crouch, left, right, sounds, (bodyFlags & c_body_dead) != 0, health,
                (bodyFlags & c_body_grounded) != 0, platform));
            return true;
        }

        private static void WriteHand(NetDataWriter writer, in WKTHandPose hand) {
            var position = hand.Position;
            var flags = hand.IsGrabbing ? c_flag_grabbing : (byte)0;
            if (hand.IsHanging)
                flags |= c_flag_hanging;
            if (hand.IsReaching)
                flags |= c_flag_reaching;
            if (hand.IsOpen)
                flags |= c_flag_open;
            writer.Put(flags);
            writer.Put(hand.Stamina);
            writer.Put(position.x);
            writer.Put(position.y);
            writer.Put(position.z);
        }

        private static bool TryReadHand(NetDataReader reader, out WKTHandPose hand) {
            hand = default;
            var flags = reader.GetByte();
            var stamina = reader.GetByte();
            var position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            if ((flags & ~c_known_flags) != 0) return false;
            var isGrabbing = (flags & c_flag_grabbing) != 0;
            var isHanging = (flags & c_flag_hanging) != 0;
            var isReaching = (flags & c_flag_reaching) != 0;
            var isOpen = (flags & c_flag_open) != 0;
            if ((isHanging && !isGrabbing) || (isReaching && isGrabbing) || (isOpen && !isReaching)) return false;
            if (isGrabbing && !WKTHandPose.IsValid(position)) return false;
            hand = new WKTHandPose(isGrabbing, position, stamina, isHanging, isReaching, isOpen);
            return true;
        }

        private static void WriteSounds(NetDataWriter writer, in WKTPlayerSounds sounds) {
            writer.Put(sounds.Newest);
            writer.Put((byte)sounds.Count);
            for (var age = 0; age < WKTPlayerSounds.CAPACITY; age++) {
                var sound = age < sounds.Count ? sounds.Get(age) : default;
                writer.Put((byte)sound.Id);
                writer.Put(sound.Material);
            }
        }

        private static bool TryReadSounds(NetDataReader reader, out WKTPlayerSounds sounds) {
            sounds = default;
            var newest = reader.GetByte();
            var count = reader.GetByte();
            if (count > WKTPlayerSounds.CAPACITY) return false;
            var entries = 0UL;
            for (var age = 0; age < WKTPlayerSounds.CAPACITY; age++) {
                var id = (WKTPlayerSoundId)reader.GetByte();
                var material = reader.GetByte();
                if (age >= count) continue;
                if (!WKTPlayerSound.IsDefined(id)) return false;
                entries |= WKTPlayerSounds.Pack(new WKTPlayerSound(id, material)) << (age * WKTPlayerSounds.ENTRY_BITS);
            }
            sounds = new WKTPlayerSounds(newest, count, entries);
            return true;
        }
    }
}
