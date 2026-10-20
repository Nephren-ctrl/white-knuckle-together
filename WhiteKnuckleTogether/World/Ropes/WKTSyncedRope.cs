using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// One <c>ENV_Rope</c> as <see cref="WKTRopeSync"/> tracks it: who simulates it, what it shows when another machine does,
    /// and the scratch space to encode it. Node positions here are in the rope's own space: node 0 at its anchor, axes the
    /// anchor's rotation. Everything is allocated once, when the rope starts.
    /// </summary>
    internal sealed class WKTSyncedRope {
        public WKTSyncedRope(ENV_Rope rope, int nodeCount) {
            Rope = rope;
            Target = rope.GetTargetRigidbody();
            NodeCount = nodeCount;
            MaxLength = WKTRopeCodec.GetMaxLength(rope.nodeDistance, nodeCount);
            Local = new Vector3[nodeCount];
            Rebuilt = new Vector3[nodeCount];
            Sent = new Vector3[nodeCount];
            Shown = new Vector3[nodeCount];
            Latest = new Vector3[nodeCount];
            Links = new WKTRopeLink[nodeCount - 1];
            Buffer = new WKTRopeSnapshotBuffer(nodeCount);
            RemoteSwings = new RemoteSwing[WKTLobbyConsts.MAX_PLAYERS];
        }

        public ENV_Rope Rope { get; }
        public Rigidbody? Target { get; }
        public int NodeCount { get; }
        /// <summary>The longest one link can be, which scales the links' lengths on the wire.</summary>
        public float MaxLength { get; }
        /// <summary>The same on every machine. Zero until the rope could be keyed.</summary>
        public ulong Key { get; set; }
        /// <summary>The player whose machine simulates it, or <see cref="WKTEntityAuthority.HOST"/>.</summary>
        public byte Owner { get; set; } = WKTEntityAuthority.HOST;
        /// <summary>Shown from another machine's states, with the game's own simulation of it skipped.</summary>
        public bool IsPuppet { get; set; }
        public bool TargetWasKinematic { get; set; }
        /// <summary>Client only: let go of here, and still simulated here until this time while the host's states arrive.</summary>
        public double HandbackUntil { get; set; }
        /// <summary>Whether <see cref="Shown"/> holds a shape another machine sent.</summary>
        public bool HasShown { get; set; }
        /// <summary>How fast the end moves on the machine simulating it, as last shown here.</summary>
        public Vector3 ShownVelocity { get; set; }
        /// <summary>Whether the local player has a hand on any of its links.</summary>
        public bool LocalHolding { get; set; }
        /// <summary>When the local player's let go counts, unless they take hold again first. Infinite when not letting go.</summary>
        public double ReleaseAt { get; set; } = double.PositiveInfinity;
        public double LastHoldChange { get; set; } = double.NegativeInfinity;
        public double LastSent { get; set; } = double.NegativeInfinity;
        /// <summary>The local player's swing on it this physics step, and when, while another machine simulates it.</summary>
        public Vector3 LocalSwing { get; set; }
        public double LocalSwingTime { get; set; } = double.NegativeInfinity;
        /// <summary>Host only: whether <see cref="Latest"/> holds the owning client's newest state.</summary>
        public bool HasLatest { get; set; }
        public Vector3 LatestVelocity { get; set; }
        /// <summary>The nodes as simulated here, in the rope's space.</summary>
        public Vector3[] Local { get; }
        /// <summary>The nodes as every receiver rebuilds them from the newest encoding.</summary>
        public Vector3[] Rebuilt { get; }
        /// <summary>The nodes as last sent, to tell whether the rope still moves.</summary>
        public Vector3[] Sent { get; }
        /// <summary>The nodes shown while a puppet.</summary>
        public Vector3[] Shown { get; }
        /// <summary>Host only: the owning client's newest state, to hand on when the rope changes hands.</summary>
        public Vector3[] Latest { get; }
        public WKTRopeLink[] Links { get; }
        public WKTRopeSnapshotBuffer Buffer { get; }
        /// <summary>Host only: the swings of remote players holding it, by slot.</summary>
        public RemoteSwing[] RemoteSwings { get; }

        public struct RemoteSwing {
            public byte Player;
            public Vector3 Force;
            public double Time;
        }
    }
}
