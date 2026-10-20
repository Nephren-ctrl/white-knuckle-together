using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Marks a world object replicated by <see cref="WKTWorldSync"/> during a synced co-op run, and reports when the object
    /// itself is enabled, disabled or destroyed. Added at runtime; never part of a prefab.
    /// </summary>
    internal sealed class WKTNetIdentity : MonoBehaviour {
        /// <summary>The same on every machine that built this object, or a host-assigned runtime key.</summary>
        public ulong Key { get; set; }
        /// <summary>The host's handle for it. Zero on a client until the host binds it.</summary>
        public ushort Handle { get; set; }
        public WKTNetKind Kind { get; set; }
        /// <summary>Driven by another machine's states rather than simulated here.</summary>
        public bool IsPuppet { get; set; }
        /// <summary>The player whose machine simulates it, or <see cref="WKTEntityAuthority.HOST"/>.</summary>
        public byte Owner { get; set; } = WKTEntityAuthority.HOST;
        /// <summary>Client only: the client's own drop or spawn, handed to the host and never hidden while it waits for the bind.</summary>
        public bool IsLocalDrop { get; set; }
        public GameEntity? Entity { get; set; }
        /// <summary>
        /// The object's prop, if it can be held: <see cref="Entity"/> itself for plain props, a second component for creatures
        /// players can pick up (spriders, bloodbugs, drones), whose <see cref="Entity"/> is the creature.
        /// </summary>
        public CL_Prop? Prop { get; set; }
        /// <summary>
        /// The world object this one is nested in, such as a barnacle zombie for its barnacle. Its key comes from that object's,
        /// and it moves with it, so its own streamed poses are not applied.
        /// </summary>
        public WKTNetIdentity? NestedIn { get; set; }
        public Item_Object? Item { get; set; }
        public Rigidbody? Body { get; set; }
        public Animator? Animator { get; set; }
        /// <summary>Whether the body was kinematic before becoming a puppet.</summary>
        public bool BodyWasKinematic { get; set; }
        /// <summary>Host only: a kill has been sent, so overrides calling their base kill send only one.</summary>
        public bool KillSent { get; set; }
        /// <summary>Whether the object counts as active the last time it was reported: its own active flag, not its parents'.</summary>
        public bool ReportedActive { get; set; } = true;
        /// <summary>
        /// Its own components move it from the shared clock the same way on every machine (see <see cref="WKTAmbientMotion"/>), so a
        /// puppet never takes the simulating machine's pose.
        /// </summary>
        public bool SelfDriven { get; set; }
        /// <summary>The level it belongs to when registered. Zero when none.</summary>
        public ulong LevelKey { get; set; }

        [UsedImplicitly]
        private void OnEnable() => WKTWorldSync.Instance?.OnIdentityActivationChanged(this);

        [UsedImplicitly]
        private void OnDisable() => WKTWorldSync.Instance?.OnIdentityActivationChanged(this);

        [UsedImplicitly]
        private void OnDestroy() => WKTWorldSync.Instance?.OnIdentityDestroyed(this);
    }
}
