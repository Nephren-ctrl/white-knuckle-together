using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>Marks a placed piton shared by <see cref="WKTPitonSync"/>, and reports when it goes away. Added at runtime.</summary>
    internal sealed class WKTPlacedPiton : MonoBehaviour {
        public uint Id { get; set; }
        public CL_Handhold? Handhold { get; set; }
        /// <summary>Removed because another machine said so, which is not reported back.</summary>
        public bool RemovedRemotely { get; set; }
        /// <summary>Taken out of the wall by the local player, as the piton's own release module does.</summary>
        public bool Taken { get; set; }

        [UsedImplicitly]
        private void OnDestroy() => WKTWorldSync.Instance?.Pitons.OnPitonDestroyed(this);
    }
}
