namespace WhiteKnuckleTogether {
    /// <summary>Received states of one remote player. See <see cref="WKTSnapshotBufferBase{TPose}"/>.</summary>
    public sealed class WKTSnapshotBuffer : WKTSnapshotBufferBase<WKTPlayerPose> {
        /// <summary>Adds a player state received at local time <paramref name="receiveTime"/>. See <see cref="WKTSnapshotBufferBase{TPose}.Add"/>.</summary>
        public bool Add(in WKTPlayerState state, double receiveTime) => Add(state.Sequence, state.Pose, receiveTime);
    }
}
