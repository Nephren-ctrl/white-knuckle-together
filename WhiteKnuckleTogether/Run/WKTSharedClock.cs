using Steamworks.Data;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The host's game clocks, shared by every machine of a synced co-op run, for motion the game computes from the time alone,
    /// such as <c>UT_Wiggle</c>'s bobbing (the abyss's floating rope bags). Each machine starts its own clock at a different
    /// moment, so the same code would move the same thing differently. Clients ask the host for its clocks every
    /// <see cref="REQUEST_INTERVAL"/> seconds and keep an estimate (<see cref="WKTClockOffset"/>). Outside synced runs, and on
    /// the host, it reads the local clocks. Owned by <see cref="WKTWorldSync"/>.
    /// </summary>
    internal sealed class WKTSharedClock {
        public const double REQUEST_INTERVAL = 2.0;
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        private readonly WKTClockOffset m_gameTime = new();
        private readonly WKTClockOffset m_unscaledTime = new();
        private double m_nextRequest;

        public WKTSharedClock(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
        }

        /// <summary>The host's <c>Time.time</c>, here. Replaces the game's reads where machines must agree.</summary>
        /// <summary>Whether this machine knows the host's clock: always on the host, after the first answer on a client.</summary>
        public static bool IsReady => WKTWorldSync.Instance is { } sync && (sync.IsHost || sync.Clock.m_gameTime.HasOffset);

        public static float GetTime() {
            var clock = WKTWorldSync.Instance?.Clock;
            return clock == null || !clock.m_gameTime.HasOffset ? Time.time : (float)(Time.timeAsDouble + clock.m_gameTime.Offset);
        }

        /// <summary>The host's <c>Time.unscaledTime</c>, here.</summary>
        public static float GetUnscaledTime() {
            var clock = WKTWorldSync.Instance?.Clock;
            return clock == null || !clock.m_unscaledTime.HasOffset ? Time.unscaledTime : (float)(Time.unscaledTimeAsDouble + clock.m_unscaledTime.Offset);
        }

        /// <summary>Client only: asks the host for its clocks when due, and eases the estimates.</summary>
        public void Update(double now) {
            if (m_sync.IsHost) return;
            var deltaTime = Time.unscaledDeltaTime;
            m_gameTime.Update(deltaTime);
            m_unscaledTime.Update(deltaTime);
            if (now < m_nextRequest) return;
            m_nextRequest = now + REQUEST_INTERVAL;
            var writer = m_network.BeginMessage(WKTMessageId.ClockSync);
            writer.Put(now);
            m_network.SendToHost(writer, SendType.Unreliable);
        }

        /// <summary>The host answers a client's request with its clocks; a client takes the answer as a sample.</summary>
        public void OnClockSync(WKTPlayer sender, NetDataReader reader) {
            if (!reader.TryGetDouble(out var sent)) return;
            if (m_sync.IsHost) {
                if (sender.IsHost || !m_network.TryGetPlayer(sender.Id, out var player) || player == null) return;
                var writer = m_network.BeginMessage(WKTMessageId.ClockSync);
                writer.Put(sent);
                writer.Put(Time.timeAsDouble);
                writer.Put(Time.unscaledTimeAsDouble);
                m_network.SendToPlayer(player, writer, SendType.Unreliable);
                return;
            }
            if (!sender.IsHost || !reader.TryGetDouble(out var hostTime) || !reader.TryGetDouble(out var hostUnscaledTime)) return;
            var received = Time.realtimeSinceStartupAsDouble;
            m_gameTime.AddSample(sent, received, hostTime, Time.timeAsDouble);
            m_unscaledTime.AddSample(sent, received, hostUnscaledTime, Time.unscaledTimeAsDouble);
        }

        /// <summary>A new host has different clocks: start over and ask at once.</summary>
        public void Reset() {
            m_gameTime.Reset();
            m_unscaledTime.Reset();
            m_nextRequest = 0.0;
        }
    }
}
