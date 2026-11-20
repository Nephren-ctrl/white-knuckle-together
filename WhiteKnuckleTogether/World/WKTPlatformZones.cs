using System.Collections.Generic;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Host only: what moving thing carries a world object. The game carries what lies in a <c>UT_SoftParentZone</c> (on lifts,
    /// trams and other movers) along with it. Movers run on every machine, so an object sent with its place on the zone is shown
    /// on the receiver's own copy of it, rather than trailing behind it by the interpolation delay and the ping. Only while the
    /// zone moves (and <see cref="WKTPlatforms.MOVING_HOLD_SECONDS"/> after), as for players riding (<see cref="WKTPlatforms"/>).
    /// </summary>
    internal sealed class WKTPlatformZones {
        private readonly List<UT_SoftParentZone> m_zones = new();
        private readonly Dictionary<UT_SoftParentZone, Tracked> m_tracked = new();
        private readonly List<(UT_SoftParentZone Zone, WKTPlatformRef Frame)> m_moving = new();
        private readonly WKTPlatforms m_platforms;

        public WKTPlatformZones(WKTPlatforms platforms) => m_platforms = platforms;

        /// <summary>The zones in a level that was just keyed.</summary>
        public void IndexLevel(M_Level level) {
            // Only the host's send ticks drop destroyed zones otherwise.
            for (var i = m_zones.Count - 1; i >= 0; i--) {
                if (m_zones[i] != null) continue;
                m_tracked.Remove(m_zones[i]);
                m_zones.RemoveAt(i);
            }
            foreach (var zone in level.GetComponentsInChildren<UT_SoftParentZone>(true)) {
                if (!m_zones.Contains(zone))
                    m_zones.Add(zone);
            }
        }

        /// <summary>Finds the zones moving now. Call once per send tick, before <see cref="Find"/>.</summary>
        public void BeginTick(float now) {
            m_moving.Clear();
            for (var i = m_zones.Count - 1; i >= 0; i--) {
                var zone = m_zones[i];
                if (zone == null) {
                    m_tracked.Remove(zone!);
                    m_zones.RemoveAt(i);
                    continue;
                }
                if (!zone.isActiveAndEnabled) continue;
                zone.transform.GetPositionAndRotation(out var position, out var rotation);
                if (!m_tracked.TryGetValue(zone, out var tracked))
                    tracked.Frame = m_platforms.TryDescribe(zone.transform, out var frame) ? frame : default;
                else if (WKTPlatforms.IsMoving(tracked.Position, tracked.Rotation, position, rotation, now - tracked.Time))
                    tracked.MovingUntil = now + WKTPlatforms.MOVING_HOLD_SECONDS;
                tracked.Position = position;
                tracked.Rotation = rotation;
                tracked.Time = now;
                m_tracked[zone] = tracked;
                if (tracked.Frame.IsSet && now < tracked.MovingUntil)
                    m_moving.Add((zone, tracked.Frame));
            }
        }

        /// <summary>
        /// The moving zone carrying <paramref name="identity"/>, with its place on it, or nothing. A zone carries an object with
        /// its own <see cref="GameEntity"/> on a layer it scans, unless its body is kinematic (<c>UT_SoftParentZone.UpdateMovement</c>).
        /// </summary>
        public WKTPlatformRef Find(WKTNetIdentity identity) {
            if (m_moving.Count == 0 || (identity.Body != null && identity.Body.isKinematic) || !identity.TryGetComponent<GameEntity>(out _))
                return default;
            var position = identity.transform.position;
            var layer = 1 << identity.gameObject.layer;
            foreach (var (zone, frame) in m_moving) {
                if (zone == null || (zone.scanMask.value & layer) == 0 || !zone.IsPositionInBounds(position)) continue;
                return new WKTPlatformRef(frame.LevelKey, frame.Depth, frame.PathLow, frame.PathHigh, frame.NameHash, zone.transform.InverseTransformPoint(position));
            }
            return default;
        }

        private struct Tracked {
            public WKTPlatformRef Frame;
            public Vector3 Position;
            public Quaternion Rotation;
            public float Time;
            public float MovingUntil;
        }
    }
}
