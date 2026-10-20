using System;
using System.Collections.Generic;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The sending side of world object states: the host's for everyone, or a client's for the objects it simulates. Each send
    /// tick, objects are submitted with their current state; only what changed since the last send goes out, plus a full
    /// keyframe every <see cref="KEYFRAME_INTERVAL"/> so lost packets and newly close clients catch up. Each recipient only
    /// gets objects within its relevance distance, or seen from far away, batched into packets below
    /// <see cref="WKTEntityState.MAX_PACKET_SIZE"/>.
    /// A sender that <see cref="SendsMotion"/> adds velocity to every pose and the engaged player when it changes, for the
    /// host to hand on when authority moves. Pure and allocation free after warm-up.
    /// </summary>
    public sealed class WKTEntityStateSender {
        public const float RELEVANCE_DISTANCE = 160f;
        public const double KEYFRAME_INTERVAL = 1.0;
        /// <summary>Moves smaller than this are not sent.</summary>
        public const float POSITION_EPSILON = 0.005f;
        /// <summary>Turns smaller than this, in degrees, are not sent.</summary>
        public const float ROTATION_EPSILON = 0.25f;
        public const float ANIMATOR_EPSILON = 0.001f;
        private readonly Dictionary<ushort, Tracked> m_tracked = new(256);
        private readonly List<Pending> m_pending = new(256);
        private readonly float[] m_animatorValues = new float[WKTEntityState.MAX_ANIMATOR_VALUES * 1024];
        private readonly int[] m_animatorStates = new int[WKTEntityState.MAX_ANIMATOR_LAYERS * 1024];
        private int m_animatorUsed;
        private int m_statesUsed;

        public int PendingCount => m_pending.Count;
        public bool SendsMotion { get; }

        public WKTEntityStateSender(bool sendsMotion = false) => SendsMotion = sendsMotion;

        /// <summary>Starts a send tick, dropping anything submitted but not flushed.</summary>
        public void BeginTick() {
            m_pending.Clear();
            m_animatorUsed = 0;
            m_statesUsed = 0;
        }

        /// <summary>
        /// Submits an object's current state, in world-offset-corrected space. It is queued if anything changed since it was
        /// last sent, or a keyframe is due. <paramref name="animatorValues"/> beyond
        /// <see cref="WKTEntityState.MAX_ANIMATOR_VALUES"/> and <paramref name="animatorStates"/> beyond
        /// <see cref="WKTEntityState.MAX_ANIMATOR_LAYERS"/> are ignored. The motion and engaged player only go out when
        /// <see cref="SendsMotion"/>. <paramref name="skipFor"/> is a recipient this state is never flushed to. A
        /// <paramref name="farVisible"/> object, which players see from beyond the relevance distance, goes to every recipient.
        /// </summary>
        public void Submit(ushort handle, in WKTEntityPose pose, float health, bool dead, ReadOnlySpan<float> animatorValues, double now,
                           ReadOnlySpan<int> animatorStates = default, byte aiBits = 0, in WKTEntityMotion motion = default,
                           byte skipFor = WKTEntityAuthority.NOBODY, bool farVisible = false) {
            var animatorCount = Math.Min(animatorValues.Length, WKTEntityState.MAX_ANIMATOR_VALUES);
            var stateCount = Math.Min(animatorStates.Length, WKTEntityState.MAX_ANIMATOR_LAYERS);
            if (!m_tracked.TryGetValue(handle, out var tracked)) {
                tracked = new Tracked();
                m_tracked.Add(handle, tracked);
            }
            var keyframe = !tracked.HasSent || now - tracked.LastKeyframe >= KEYFRAME_INTERVAL;
            var state = new WKTEntityState {
                Handle = handle, Pose = pose, Health = health, AiBits = aiBits, Velocity = motion.Velocity, AngularVelocity = motion.AngularVelocity,
                Engaged = motion.Engaged
            };
            if (keyframe || HasMoved(tracked.Pose, pose))
                state.Flags |= WKTEntityState.FLAG_POSE;
            if (keyframe || !Mathf.Approximately(tracked.Health, health))
                state.Flags |= WKTEntityState.FLAG_HEALTH;
            if (animatorCount > 0 && (keyframe || HasAnimatorChanged(tracked, animatorValues, animatorCount)))
                state.Flags |= WKTEntityState.FLAG_ANIMATOR;
            if (stateCount > 0 && (keyframe || HasStatesChanged(tracked, animatorStates, stateCount)))
                state.Flags |= WKTEntityState.FLAG_ANIMATOR_STATES;
            if (keyframe || aiBits != tracked.AiBits)
                state.Flags |= WKTEntityState.FLAG_AI;
            if (SendsMotion && (keyframe || motion.Engaged != tracked.Engaged))
                state.Flags |= WKTEntityState.FLAG_ENGAGED;
            if (SendsMotion && state.Has(WKTEntityState.FLAG_POSE))
                state.Flags |= WKTEntityState.FLAG_VELOCITY;
            if (dead)
                state.Flags |= WKTEntityState.FLAG_DEAD;
            const byte changes = WKTEntityState.FLAG_POSE | WKTEntityState.FLAG_HEALTH | WKTEntityState.FLAG_ANIMATOR |
                WKTEntityState.FLAG_ANIMATOR_STATES | WKTEntityState.FLAG_AI | WKTEntityState.FLAG_ENGAGED;
            var changed = (state.Flags & changes) != 0 || dead != tracked.Dead;
            if (!changed) return;
            if (state.Has(WKTEntityState.FLAG_ANIMATOR)) {
                if (m_animatorUsed + animatorCount > m_animatorValues.Length)
                    state.Flags &= unchecked((byte)~WKTEntityState.FLAG_ANIMATOR);
                else {
                    state.AnimatorCount = animatorCount;
                    animatorValues.Slice(0, animatorCount).CopyTo(m_animatorValues.AsSpan(m_animatorUsed));
                    animatorValues.Slice(0, animatorCount).CopyTo(tracked.Animator);
                    tracked.AnimatorCount = animatorCount;
                }
            }
            if (state.Has(WKTEntityState.FLAG_ANIMATOR_STATES)) {
                if (m_statesUsed + stateCount > m_animatorStates.Length)
                    state.Flags &= unchecked((byte)~WKTEntityState.FLAG_ANIMATOR_STATES);
                else {
                    state.StateCount = stateCount;
                    animatorStates.Slice(0, stateCount).CopyTo(m_animatorStates.AsSpan(m_statesUsed));
                    animatorStates.Slice(0, stateCount).CopyTo(tracked.States);
                    tracked.StateCount = stateCount;
                }
            }
            m_pending.Add(new Pending(state, m_animatorUsed, m_statesUsed, skipFor, farVisible));
            if (state.Has(WKTEntityState.FLAG_ANIMATOR))
                m_animatorUsed += animatorCount;
            if (state.Has(WKTEntityState.FLAG_ANIMATOR_STATES))
                m_statesUsed += stateCount;
            if (state.Has(WKTEntityState.FLAG_POSE))
                tracked.Pose = pose;
            tracked.Health = health;
            tracked.AiBits = aiBits;
            tracked.Engaged = motion.Engaged;
            tracked.Dead = dead;
            tracked.HasSent = true;
            if (keyframe)
                tracked.LastKeyframe = now;
        }

        /// <summary>Forgets an object, so it starts with a keyframe if it comes back.</summary>
        public void Forget(ushort handle) => m_tracked.Remove(handle);

        public void Clear() {
            m_tracked.Clear();
            BeginTick();
        }

        /// <summary>
        /// Writes the queued states relevant to <paramref name="recipient"/> at <paramref name="clientPosition"/>
        /// (world-offset-corrected): those within <paramref name="relevance"/> of it and not submitted to skip it.
        /// <paramref name="beginPacket"/> returns a writer with the message id written; <paramref name="sendPacket"/> sends it.
        /// Returns how many packets were sent.
        /// </summary>
        public int Flush(Vector3 clientPosition, ushort sequence, Func<NetDataWriter> beginPacket, Action<NetDataWriter> sendPacket,
                         byte recipient = WKTEntityAuthority.NOBODY, float relevance = RELEVANCE_DISTANCE) {
            NetDataWriter? writer = null;
            var countPosition = 0;
            var count = 0;
            var packets = 0;
            var relevantSqr = relevance * relevance;
            foreach (var pending in m_pending) {
                if (recipient != WKTEntityAuthority.NOBODY && pending.SkipFor == recipient) continue;
                var state = pending.State;
                if (!pending.FarVisible) {
                    if (state.Has(WKTEntityState.FLAG_POSE) && (state.Pose.Position - clientPosition).sqrMagnitude > relevantSqr) continue;
                    if (!state.Has(WKTEntityState.FLAG_POSE) && m_tracked.TryGetValue(state.Handle, out var tracked) &&
                        (tracked.Pose.Position - clientPosition).sqrMagnitude > relevantSqr) continue;
                }
                var size = state.GetSize();
                if (writer != null && (writer.Length + size > WKTEntityState.MAX_PACKET_SIZE || count == byte.MaxValue)) {
                    WKTEntityState.EndBatch(writer, countPosition, count);
                    sendPacket(writer);
                    packets++;
                    writer = null;
                }
                if (writer == null) {
                    writer = beginPacket();
                    countPosition = WKTEntityState.BeginBatch(writer, sequence);
                    count = 0;
                }
                state.Write(writer, m_animatorValues.AsSpan(pending.AnimatorStart, state.Has(WKTEntityState.FLAG_ANIMATOR) ? state.AnimatorCount : 0),
                    m_animatorStates.AsSpan(pending.StateStart, state.Has(WKTEntityState.FLAG_ANIMATOR_STATES) ? state.StateCount : 0));
                count++;
            }
            if (writer == null) return packets;
            WKTEntityState.EndBatch(writer, countPosition, count);
            sendPacket(writer);
            return packets + 1;
        }

        private static bool HasMoved(in WKTEntityPose from, in WKTEntityPose to) =>
            (to.Position - from.Position).sqrMagnitude > POSITION_EPSILON * POSITION_EPSILON ||
            Quaternion.Angle(from.Rotation, to.Rotation) > ROTATION_EPSILON;

        private static bool HasAnimatorChanged(Tracked tracked, ReadOnlySpan<float> values, int count) {
            if (count != tracked.AnimatorCount) return true;
            for (var i = 0; i < count; i++) {
                if (Math.Abs(values[i] - tracked.Animator[i]) > ANIMATOR_EPSILON) return true;
            }
            return false;
        }

        private static bool HasStatesChanged(Tracked tracked, ReadOnlySpan<int> states, int count) {
            if (count != tracked.StateCount) return true;
            for (var i = 0; i < count; i++) {
                if (states[i] != tracked.States[i]) return true;
            }
            return false;
        }

        private sealed class Tracked {
            public readonly float[] Animator = new float[WKTEntityState.MAX_ANIMATOR_VALUES];
            public readonly int[] States = new int[WKTEntityState.MAX_ANIMATOR_LAYERS];
            public WKTEntityPose Pose = new(Vector3.zero, Quaternion.identity);
            public float Health;
            public bool Dead;
            public int AnimatorCount;
            public int StateCount;
            public byte AiBits;
            public byte Engaged = WKTEntityAuthority.NOBODY;
            public bool HasSent;
            public double LastKeyframe;
        }

        private readonly struct Pending {
            public readonly WKTEntityState State;
            public readonly int AnimatorStart;
            public readonly int StateStart;
            public readonly byte SkipFor;
            public readonly bool FarVisible;

            public Pending(WKTEntityState state, int animatorStart, int stateStart, byte skipFor, bool farVisible) {
                State = state;
                AnimatorStart = animatorStart;
                StateStart = stateStart;
                SkipFor = skipFor;
                FarVisible = farVisible;
            }
        }
    }
}
