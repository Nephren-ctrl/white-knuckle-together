namespace WhiteKnuckleTogether {
    /// <summary>
    /// Received states of one remote object, replayed <see cref="INTERPOLATION_DELAY"/> behind the newest so there are
    /// usually two samples to interpolate between. Samples are placed on the sender's timeline (sequence times
    /// <see cref="WKTNetworkConsts.SEND_DELTA"/>), mapped to local time by an offset that follows the fastest recent
    /// arrival, so network jitter does not make motion uneven. Fixed capacity, allocation free after construction.
    /// </summary>
    public class WKTSnapshotBufferBase<TPose> where TPose : struct, IWKTInterpolatable<TPose> {
        public const int CAPACITY = 32;
        /// <summary>Two send intervals: one lost or late packet still leaves a pair to interpolate between.</summary>
        public const double INTERPOLATION_DELAY = 2.0 * WKTNetworkConsts.SEND_DELTA;
        /// <summary>How far past the newest sample motion is continued before holding still.</summary>
        public const double MAX_EXTRAPOLATION = 2.0 * WKTNetworkConsts.SEND_DELTA;
        /// <summary>Consecutive samples further apart than this are a teleport, and are never blended.</summary>
        public const float TELEPORT_DISTANCE = 10f;
        /// <summary>
        /// Arrivals slower than the current offset only pull it up by this fraction per sample, so one late packet barely
        /// moves playback while a lasting increase in latency is still followed.
        /// </summary>
        private const double c_offset_rise = 0.05;
        /// <summary>A jump in arrival offset larger than this (a hitch or pause) is taken at once.</summary>
        private const double c_resync_threshold = 1.0;
        private readonly Snapshot[] m_snapshots = new Snapshot[CAPACITY];
        private int m_start;
        private long m_latestTick;
        private double m_clockOffset;

        public int Count { get; private set; }
        public ushort LatestSequence { get; private set; }

        /// <summary>
        /// Adds a state received at local time <paramref name="receiveTime"/>. Duplicates and states older than the newest
        /// are dropped and return false. When full, the oldest sample is overwritten.
        /// </summary>
        public bool Add(ushort sequence, in TPose pose, double receiveTime) {
            long tick;
            if (Count == 0) {
                tick = sequence;
            } else {
                // Signed difference handles wrap-around: 0 follows 65535.
                var difference = unchecked((short)(sequence - LatestSequence));
                if (difference <= 0) return false;
                tick = m_latestTick + difference;
            }
            var senderTime = tick * (double)WKTNetworkConsts.SEND_DELTA;
            var offset = receiveTime - senderTime;
            if (Count == 0 || offset < m_clockOffset || offset - m_clockOffset > c_resync_threshold)
                m_clockOffset = offset;
            else
                m_clockOffset += (offset - m_clockOffset) * c_offset_rise;
            if (Count == CAPACITY) {
                m_start = (m_start + 1) % CAPACITY;
                Count--;
            }
            m_snapshots[(m_start + Count) % CAPACITY] = new Snapshot(senderTime, pose);
            Count++;
            m_latestTick = tick;
            LatestSequence = sequence;
            return true;
        }

        /// <summary>The pose to show at local time <paramref name="now"/>. False until a state has been added.</summary>
        public bool TrySample(double now, out TPose pose) {
            pose = default;
            if (Count == 0) return false;
            var renderTime = now - m_clockOffset - INTERPOLATION_DELAY;
            ref readonly var oldest = ref Get(0);
            if (Count == 1 || renderTime <= oldest.time) {
                pose = oldest.pose;
                return true;
            }
            ref readonly var newest = ref Get(Count - 1);
            if (renderTime >= newest.time) {
                pose = Extrapolate(renderTime - newest.time);
                return true;
            }
            // Newest first: playback runs close to the end of the buffer.
            for (var i = Count - 2; i >= 0; i--) {
                ref readonly var from = ref Get(i);
                if (from.time > renderTime) continue;
                ref readonly var to = ref Get(i + 1);
                pose = Blend(from, to, (float)((renderTime - from.time) / (to.time - from.time)));
                return true;
            }
            pose = oldest.pose;
            return true;
        }

        public void Clear() {
            m_start = 0;
            Count = 0;
            m_latestTick = 0;
            m_clockOffset = 0;
            LatestSequence = 0;
        }

        private static TPose Blend(in Snapshot from, in Snapshot to, float t) {
            if ((to.pose.Position - from.pose.Position).sqrMagnitude > TELEPORT_DISTANCE * TELEPORT_DISTANCE)
                return from.pose;
            return from.pose.LerpTo(to.pose, t);
        }

        /// <summary>
        /// Continues the newest motion for at most <see cref="MAX_EXTRAPOLATION"/>, then holds. Everything but the position holds.
        /// </summary>
        private TPose Extrapolate(double elapsed) {
            ref readonly var newest = ref Get(Count - 1);
            if (Count < 2 || elapsed <= 0) return newest.pose;
            ref readonly var previous = ref Get(Count - 2);
            var interval = newest.time - previous.time;
            var motion = newest.pose.Position - previous.pose.Position;
            // A gap of several lost packets says little about current velocity, and a teleport says nothing.
            if (interval > MAX_EXTRAPOLATION || motion.sqrMagnitude > TELEPORT_DISTANCE * TELEPORT_DISTANCE) return newest.pose;
            var scale = (float)((elapsed < MAX_EXTRAPOLATION ? elapsed : MAX_EXTRAPOLATION) / interval);
            return newest.pose.WithPosition(newest.pose.Position + motion * scale);
        }

        private ref readonly Snapshot Get(int index) => ref m_snapshots[(m_start + index) % CAPACITY];

        private readonly struct Snapshot {
            public readonly double time;
            public readonly TPose pose;

            public Snapshot(double time, in TPose pose) {
                this.time = time;
                this.pose = pose;
            }
        }
    }
}
