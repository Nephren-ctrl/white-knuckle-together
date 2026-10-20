using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>A state that <see cref="WKTSnapshotBufferBase{TPose}"/> can replay: it has a position and blends between samples.</summary>
    public interface IWKTInterpolatable<TPose> where TPose : struct {
        Vector3 Position { get; }

        /// <summary>This state blended towards <paramref name="to"/>. <paramref name="t"/> is not clamped.</summary>
        TPose LerpTo(in TPose to, float t);

        TPose WithPosition(Vector3 position);
    }
}
