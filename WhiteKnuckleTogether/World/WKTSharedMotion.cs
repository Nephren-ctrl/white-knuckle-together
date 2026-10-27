using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Level machinery that moves on its own, in step on every machine of a synced co-op run, from the shared clock
    /// (<see cref="WKTSharedClock"/>):
    /// <list type="bullet">
    /// <item>Looping path movers that run from the start (crushers, <c>UT_PathMover</c>) take their place in the loop from the
    /// clock instead of from when their level loaded on that machine (<see cref="WKTPathPhase"/>). Ones that loaded before a
    /// client knew the clock are set again once it does. Movers started later, by a button, keep the game's own start.</item>
    /// <item>Spinners turning at full speed from the start (fans, grinders, <c>UT_Spin</c>) and gears (<c>UT_Gear</c>) take their
    /// angle from the clock. Spinners a button starts or stops keep the game's own turning.</item>
    /// </list>
    /// They drift apart only by frame timing until the next level loads them again.
    /// </summary>
    internal static class WKTSharedMotion {
        private static readonly AccessTools.FieldRef<UT_PathMover, int> s_moverIndex = AccessTools.FieldRefAccess<UT_PathMover, int>("currentPointIndex");
        private static readonly AccessTools.FieldRef<UT_PathMover, float> s_moverLerp = AccessTools.FieldRefAccess<UT_PathMover, float>("currentLerpTime");
        private static readonly AccessTools.FieldRef<UT_PathMover, float> s_moverWait = AccessTools.FieldRefAccess<UT_PathMover, float>("currentWaitTime");
        private static readonly AccessTools.FieldRef<UT_PathMover, float> s_moverDistance = AccessTools.FieldRefAccess<UT_PathMover, float>("currentSegmentDistance");
        private static readonly AccessTools.FieldRef<UT_PathMover, Vector3> s_moverStart = AccessTools.FieldRefAccess<UT_PathMover, Vector3>("segmentStartPosition");
        private static readonly AccessTools.FieldRef<UT_PathMover, Quaternion> s_moverStartRotation =
            AccessTools.FieldRefAccess<UT_PathMover, Quaternion>("segmentStartRotation");
        private static readonly AccessTools.FieldRef<UT_PathMover, bool> s_moverStarted = AccessTools.FieldRefAccess<UT_PathMover, bool>("hasPlayedStartSound");
        private static readonly AccessTools.FieldRef<UT_Spin, Vector3> s_spinRate = AccessTools.FieldRefAccess<UT_Spin, Vector3>("curRotate");
        private static readonly AccessTools.FieldRef<UT_Spin, Quaternion> s_spinStart = AccessTools.FieldRefAccess<UT_Spin, Quaternion>("initialRotation");
        private static readonly AccessTools.FieldRef<UT_Spin, int> s_spinDirection = AccessTools.FieldRefAccess<UT_Spin, int>("dir");
        /// <summary>The name ending of the movers that raise Delta Labs' security fields (see <see cref="WKTGatherCheck.IsPassStart"/>).</summary>
        private const string c_security_field_mover = "MovingSecurityZone";
        /// <summary>Movers that woke before the clock was known, to phase once it is.</summary>
        private static readonly HashSet<UT_PathMover> s_unphased = new();
        private static readonly Dictionary<int, Quaternion> s_gearStarts = new();
        /// <summary>Spinners that were ever stopped or toggled, by instance id, which never take their angle from the clock.</summary>
        private static readonly HashSet<int> s_freeSpinners = new();
        private static float[] s_waits = new float[8];
        private static float[] s_travelTimes = new float[8];

        private static bool IsActive => WKTWorldSync.Instance != null;

        public static void Reset() {
            s_unphased.Clear();
            s_gearStarts.Clear();
            s_freeSpinners.Clear();
        }

        /// <summary>
        /// A path mover woke. One that loops and runs from the start takes its place in the loop from the clock. Movers that
        /// wait to be started (a button's <c>StartMove</c>) keep the game's own start, so every machine starts them from the same
        /// place when the start reaches it.
        /// </summary>
        /// <summary>
        /// Whether a security field's mover is about to reach the last node of its path: the top of Lab_08's room, or the bottom
        /// of the hard mode rooms', which it sweeps down from the top. Its loop would take it back the other way.
        /// </summary>
        public static bool IsFieldReachingEnd(UT_PathMover mover) =>
            IsActive && s_moverIndex(mover) == mover.pathPoints.Count - 1 && mover.name.EndsWith(c_security_field_mover, StringComparison.Ordinal);

        /// <summary>
        /// A security field swept its room once. It stops and switches off there rather than going back the other way, so
        /// nobody climbing after it is trapped by it or caught on its way back. Every machine moves its own copy the same way.
        /// </summary>
        public static void OnFieldReachedEnd(UT_PathMover mover) {
            mover.active = false;
            if (mover.moveObject != null)
                mover.moveObject.gameObject.SetActive(false);
        }

        /// <summary>
        /// A door set itself up. The normal mode security fields (Lab_02 and Lab_04) are raised up their rooms by a door on the
        /// field itself, which stops at the top with the field still on, blocking the way on. It switches off there instead,
        /// on every machine, as each opens its own copy.
        /// </summary>
        public static void OnDoorStarted(UT_Door door) {
            // SecurityField is the only object with UT_InstantDeath.
            if (IsActive && door.TryGetComponent<UT_InstantDeath>(out _))
                door.finishOpenEvent.AddListener(() => door.gameObject.SetActive(false));
        }

        public static void AfterMoverAwake(UT_PathMover mover) {
            if (!IsActive || !mover.loop || !mover.active) return;
            if (!WKTSharedClock.IsReady) {
                s_unphased.Add(mover);
                return;
            }
            Phase(mover);
        }

        /// <summary>Once a client knows the host's clock, the movers that woke before it take their place from it.</summary>
        public static void Update() {
            if (s_unphased.Count == 0 || !WKTSharedClock.IsReady) return;
            foreach (var mover in s_unphased) {
                if (mover != null && mover.active)
                    Phase(mover);
            }
            s_unphased.Clear();
        }

        /// <summary>Puts a looping mover where it is in its loop at the clock's time plus its own offset.</summary>
        private static void Phase(UT_PathMover mover) {
            var points = mover.pathPoints;
            if (points == null || points.Count == 0 || mover.moveObject == null) return;
            var count = points.Count;
            if (s_waits.Length < count) {
                s_waits = new float[count * 2];
                s_travelTimes = new float[count * 2];
            }
            for (var i = 0; i < count; i++) {
                var from = points[i];
                var to = points[(i + 1) % count];
                if (from?.node == null || to?.node == null) return;
                // MoveAlongPath travels toward a node at that node's speed.
                var rate = mover.speed * to.speed;
                if (rate <= 0f) return;
                s_waits[i] = from.waitTime;
                s_travelTimes[i] = Vector3.Distance(from.node.position, to.node.position) / rate;
            }
            if (!WKTPathPhase.TryFind(s_waits.AsSpan(0, count), s_travelTimes.AsSpan(0, count), (double)mover.offset + WKTSharedClock.GetTime(), out var phase))
                return;
            var origin = points[phase.From];
            var target = points[(phase.From + 1) % count];
            var eased = phase.IsWaiting ? 0f : (target.useCustomCurve ? target.movementCurve : mover.movementCurve).Evaluate(phase.Progress);
            var moveObject = mover.moveObject;
            moveObject.position = Vector3.Lerp(origin.node.position, target.node.position, eased);
            moveObject.rotation = Quaternion.Lerp(origin.node.rotation, target.node.rotation, eased);
            s_moverIndex(mover) = (phase.From + 1) % count;
            s_moverLerp(mover) = phase.Progress;
            s_moverWait(mover) = phase.WaitLeft;
            s_moverDistance(mover) = Vector3.Distance(origin.node.position, target.node.position);
            s_moverStart(mover) = origin.node.position;
            s_moverStartRotation(mover) = origin.node.rotation;
            // A trip under way has already played its start; a mover still waiting plays it as it leaves.
            s_moverStarted(mover) = !phase.IsWaiting;
        }

        /// <summary>A spinner was started or stopped, by a button: it keeps the game's own speeding up and turning from then on.</summary>
        public static void OnSpinToggled(UT_Spin spin) {
            if (IsActive)
                s_freeSpinners.Add(spin.GetInstanceID());
        }

        /// <summary>
        /// A spinner at full speed takes its angle from the clock. False when it was set, skipping the game's own step. Only
        /// spinners that turn from the start do: one a button starts spins up from where it stood, and snapping it onto the
        /// clock's angle once at full speed jumped it (and anyone riding it) partway round.
        /// </summary>
        public static bool TrySpin(UT_Spin spin) {
            if (!IsActive || CL_GameManager.gMan == null || CL_GameManager.gMan.isPaused) return false;
            if (!spin.spinning) {
                s_freeSpinners.Add(spin.GetInstanceID());
                return false;
            }
            if (s_freeSpinners.Contains(spin.GetInstanceID())) return false;
            var full = spin.rotate * spin.multiplier;
            if (full.sqrMagnitude < 1e-6f || (s_spinRate(spin) - full).sqrMagnitude > full.sqrMagnitude * 1e-4f) return false;
            s_spinRate(spin) = full;
            var rotation = s_spinStart(spin) * Quaternion.Euler(AngleAt(full * s_spinDirection(spin), WKTSharedClock.GetTime()));
            if (spin.local)
                spin.transform.localRotation = rotation;
            else
                spin.transform.rotation = rotation;
            return true;
        }

        /// <summary>A gear takes its angle from the clock. False when it was set, skipping the game's own step.</summary>
        public static bool TryTurnGear(UT_Gear gear) {
            if (!IsActive || CL_GameManager.gMan == null || CL_GameManager.gMan.isPaused || gear.rotateSpeed.sqrMagnitude < 1e-6f) return false;
            var id = gear.GetInstanceID();
            if (!s_gearStarts.TryGetValue(id, out var start)) {
                start = gear.transform.localRotation;
                s_gearStarts[id] = start;
            }
            gear.transform.localRotation = start * Quaternion.Euler(AngleAt(gear.rotateSpeed, WKTSharedClock.GetTime()));
            return true;
        }

        /// <summary>Degrees turned at <paramref name="degreesPerSecond"/> by <paramref name="time"/>, kept within a turn for precision.</summary>
        public static Vector3 AngleAt(Vector3 degreesPerSecond, float time) =>
            new(Mathf.Repeat(degreesPerSecond.x * time, 360f), Mathf.Repeat(degreesPerSecond.y * time, 360f), Mathf.Repeat(degreesPerSecond.z * time, 360f));
    }
}
