using System;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// One world object's state in an <see cref="WKTMessageId.EntityStates"/> batch: whichever of its pose, health, animator
    /// values, animator states, AI bits and death changed. Batches are sent unreliably at
    /// <see cref="WKTNetworkConsts.SEND_RATE"/>, stamped with the sender's send tick like player states.
    /// </summary>
    public struct WKTEntityState {
        public const byte FLAG_POSE = 1 << 0;
        public const byte FLAG_HEALTH = 1 << 1;
        public const byte FLAG_ANIMATOR = 1 << 2;
        public const byte FLAG_DEAD = 1 << 3;
        /// <summary>The state each animator layer is in, which also carries what one-shot triggers started.</summary>
        public const byte FLAG_ANIMATOR_STATES = 1 << 4;
        /// <summary>
        /// Bits describing what the enemy is doing, whose meaning each enemy type defines from its own fields, so clients can run
        /// its attacks against their own player.
        /// </summary>
        public const byte FLAG_AI = 1 << 5;
        /// <summary>Linear and angular velocity, so a machine taking the object over carries on its motion.</summary>
        public const byte FLAG_VELOCITY = 1 << 6;
        /// <summary>The player the enemy is engaged with, or <see cref="WKTEntityAuthority.NOBODY"/>.</summary>
        public const byte FLAG_ENGAGED = 1 << 7;
        public const int MAX_ANIMATOR_VALUES = 16;
        public const int MAX_ANIMATOR_LAYERS = 4;
        /// <summary>Sequence and entry count.</summary>
        public const int HEADER_SIZE = sizeof(ushort) + sizeof(byte);
        /// <summary>The largest an entry can be.</summary>
        public const int MAX_ENTRY_SIZE = sizeof(ushort) + sizeof(byte) + WKTWire.VECTOR3_SIZE + WKTWire.QUATERNION_SIZE +
            sizeof(float) + sizeof(byte) + sizeof(float) * MAX_ANIMATOR_VALUES + sizeof(byte) + sizeof(int) * MAX_ANIMATOR_LAYERS + sizeof(byte) +
            WKTWire.VECTOR3_SIZE * 2 + sizeof(byte);
        /// <summary>Stays under the usual path MTU, so a batch is never split into fragments that could be lost separately.</summary>
        public const int MAX_PACKET_SIZE = 1100;
        private const byte c_known_flags = FLAG_POSE | FLAG_HEALTH | FLAG_ANIMATOR | FLAG_DEAD | FLAG_ANIMATOR_STATES | FLAG_AI |
            FLAG_VELOCITY | FLAG_ENGAGED;
        /// <summary>The most health accepted. Rejects garbage without limiting any real enemy.</summary>
        private const float c_max_health = 1e7f;

        public ushort Handle;
        public byte Flags;
        public WKTEntityPose Pose;
        public float Health;
        /// <summary>How many of the animator values are used. Only meaningful with <see cref="FLAG_ANIMATOR"/>.</summary>
        public int AnimatorCount;
        /// <summary>How many animator layer states are used. Only meaningful with <see cref="FLAG_ANIMATOR_STATES"/>.</summary>
        public int StateCount;
        /// <summary>The enemy type's own AI bits. Only meaningful with <see cref="FLAG_AI"/>.</summary>
        public byte AiBits;
        /// <summary>Only meaningful with <see cref="FLAG_VELOCITY"/>.</summary>
        public Vector3 Velocity;
        public Vector3 AngularVelocity;
        /// <summary>Only meaningful with <see cref="FLAG_ENGAGED"/>.</summary>
        public byte Engaged;

        public readonly bool Has(byte flag) => (Flags & flag) != 0;

        /// <summary>Starts a batch. Returns the writer position of the entry count, for <see cref="EndBatch"/>.</summary>
        public static int BeginBatch(NetDataWriter writer, ushort sequence) {
            writer.Put(sequence);
            var countPosition = writer.Length;
            writer.Put((byte)0);
            return countPosition;
        }

        /// <summary>Writes the final entry count of a batch started by <see cref="BeginBatch"/>.</summary>
        public static void EndBatch(NetDataWriter writer, int countPosition, int count) => writer.Data[countPosition] = (byte)count;

        /// <summary>The size <see cref="Write"/> will take, so batches can be kept under <see cref="MAX_PACKET_SIZE"/>.</summary>
        public int GetSize() {
            var size = sizeof(ushort) + sizeof(byte);
            if (Has(FLAG_POSE))
                size += WKTWire.VECTOR3_SIZE + WKTWire.QUATERNION_SIZE;
            if (Has(FLAG_HEALTH))
                size += sizeof(float);
            if (Has(FLAG_ANIMATOR))
                size += sizeof(byte) + sizeof(float) * AnimatorCount;
            if (Has(FLAG_ANIMATOR_STATES))
                size += sizeof(byte) + sizeof(int) * StateCount;
            if (Has(FLAG_AI))
                size += sizeof(byte);
            if (Has(FLAG_VELOCITY))
                size += WKTWire.VECTOR3_SIZE * 2;
            if (Has(FLAG_ENGAGED))
                size += sizeof(byte);
            return size;
        }

        /// <summary>
        /// Writes this entry, taking <see cref="AnimatorCount"/> values from <paramref name="animatorValues"/> and
        /// <see cref="StateCount"/> layer states from <paramref name="animatorStates"/>.
        /// </summary>
        public void Write(NetDataWriter writer, ReadOnlySpan<float> animatorValues, ReadOnlySpan<int> animatorStates = default) {
            writer.Put(Handle);
            writer.Put(Flags);
            if (Has(FLAG_POSE)) {
                WKTWire.Put(writer, Pose.Position);
                WKTWire.Put(writer, Pose.Rotation);
            }
            if (Has(FLAG_HEALTH))
                writer.Put(Health);
            if (Has(FLAG_ANIMATOR)) {
                writer.Put((byte)AnimatorCount);
                for (var i = 0; i < AnimatorCount; i++)
                    writer.Put(animatorValues[i]);
            }
            if (Has(FLAG_ANIMATOR_STATES)) {
                writer.Put((byte)StateCount);
                for (var i = 0; i < StateCount; i++)
                    writer.Put(animatorStates[i]);
            }
            if (Has(FLAG_AI))
                writer.Put(AiBits);
            if (Has(FLAG_VELOCITY)) {
                WKTWire.Put(writer, Velocity);
                WKTWire.Put(writer, AngularVelocity);
            }
            if (Has(FLAG_ENGAGED))
                writer.Put(Engaged);
        }

        public static bool TryReadHeader(NetDataReader reader, out ushort sequence, out int count) {
            count = 0;
            if (!reader.TryGetUShort(out sequence) || !reader.TryGetByte(out var rawCount)) return false;
            count = rawCount;
            return true;
        }

        /// <summary>
        /// Reads one entry, its animator values into <paramref name="animatorValues"/> (at least
        /// <see cref="MAX_ANIMATOR_VALUES"/> long) and its layer states into <paramref name="animatorStates"/> (at least
        /// <see cref="MAX_ANIMATOR_LAYERS"/> long when present). Fails on truncation, handle 0, unknown flags, bad
        /// poses, health outside [-1e7, 1e7], too many or non-finite animator values, too many layers and speeds over
        /// <see cref="WKTWire.MAX_SPEED"/>.
        /// </summary>
        public static bool TryRead(NetDataReader reader, out WKTEntityState state, Span<float> animatorValues, Span<int> animatorStates = default) {
            state = default;
            if (!reader.TryGetUShort(out var handle) || handle == 0) return false;
            if (!reader.TryGetByte(out var flags) || (flags & ~c_known_flags) != 0) return false;
            state.Handle = handle;
            state.Flags = flags;
            if (state.Has(FLAG_POSE)) {
                if (!WKTWire.TryGetPosition(reader, out var position) || !WKTWire.TryGetRotation(reader, out var rotation)) return false;
                state.Pose = new WKTEntityPose(position, rotation);
            }
            if (state.Has(FLAG_HEALTH)) {
                if (!reader.TryGetFloat(out var health) || !(health >= -c_max_health && health <= c_max_health)) return false;
                state.Health = health;
            }
            if (state.Has(FLAG_ANIMATOR)) {
                if (!reader.TryGetByte(out var animatorCount) || animatorCount > MAX_ANIMATOR_VALUES) return false;
                for (var i = 0; i < animatorCount; i++) {
                    if (!reader.TryGetFloat(out var value) || float.IsNaN(value) || float.IsInfinity(value)) return false;
                    animatorValues[i] = value;
                }
                state.AnimatorCount = animatorCount;
            }
            if (state.Has(FLAG_ANIMATOR_STATES)) {
                if (!reader.TryGetByte(out var stateCount) || stateCount > MAX_ANIMATOR_LAYERS || stateCount > animatorStates.Length) return false;
                for (var i = 0; i < stateCount; i++) {
                    if (!reader.TryGetInt(out var hash)) return false;
                    animatorStates[i] = hash;
                }
                state.StateCount = stateCount;
            }
            if (state.Has(FLAG_AI)) {
                if (!reader.TryGetByte(out var aiBits)) return false;
                state.AiBits = aiBits;
            }
            if (state.Has(FLAG_VELOCITY)) {
                if (!WKTWire.TryGetVelocity(reader, out var velocity) || !WKTWire.TryGetVelocity(reader, out var angular)) return false;
                state.Velocity = velocity;
                state.AngularVelocity = angular;
            }
            if (state.Has(FLAG_ENGAGED)) {
                if (!reader.TryGetByte(out var engaged)) return false;
                state.Engaged = engaged;
            }
            return true;
        }
    }
}
