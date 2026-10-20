using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Marks this machine's copy of a thrown return rebar shared by <see cref="WKTReturnRebarSync"/>: the thrower's own
    /// projectile, or everyone else's copy of it. Reports when it goes away. Added at runtime.
    /// </summary>
    internal sealed class WKTReturnRebarCopy : MonoBehaviour {
        public uint Id { get; set; }
        public Projectile_ReturnRebar Rebar { get; set; } = null!;
        /// <summary>Who this copy flies back to, or <see cref="WKTReturnRebarRegistry.NOBODY"/>.</summary>
        public byte Recaller { get; set; } = WKTReturnRebarRegistry.NOBODY;
        /// <summary>When it started flying back, for the copies that fly to another player.</summary>
        public float ReturnStartTime { get; set; }
        /// <summary>Until when a claim the local player sent waits for the host's answer, in realtime seconds.</summary>
        public double ClaimPendingUntil { get; set; }
        /// <summary>Destroyed on purpose by the sync, which is not reported.</summary>
        public bool Removing { get; set; }

        [UsedImplicitly]
        private void OnDestroy() => WKTWorldSync.Instance?.ReturnRebars.OnCopyDestroyed(this);
    }
}
