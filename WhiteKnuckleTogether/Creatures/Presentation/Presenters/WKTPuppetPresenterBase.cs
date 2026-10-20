using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Shows one creature type on machines where it is a puppet. A puppet's own ticks never run, so whatever the creature's
    /// AI moves or animates by itself (body segments, its model, arms, loops, particles) stands still there. A presenter
    /// either repeats that presentation-only part of the game's code on the puppet, or sends a small state from the machine
    /// simulating the creature that the puppet shows. One-shot moments, such as a slam, travel as counters in that state, so
    /// a lost packet only delays them. One instance per entity, on every machine; see <see cref="WKTPuppetPresentation"/>.
    /// </summary>
    internal abstract class WKTPuppetPresenterBase {
        /// <summary>The most bytes a presenter's state may take.</summary>
        public const int MAX_STATE_SIZE = byte.MaxValue;

        public GameEntity Entity { get; }
        /// <summary>Whether the simulating machine sends a state for this creature.</summary>
        public virtual bool HasState => false;

        protected WKTPuppetPresenterBase(GameEntity entity) => Entity = entity;

        /// <summary>Simulating machine: writes the state puppets show, at most <see cref="MAX_STATE_SIZE"/> bytes.</summary>
        public virtual void WriteState(NetDataWriter writer) { }

        /// <summary>Puppet: reads a state <see cref="WriteState"/> wrote. False on a malformed state, which is then ignored.</summary>
        public virtual bool ReadState(NetDataReader reader) => true;

        /// <summary>Puppet: every frame, after its pose was applied.</summary>
        public virtual void PuppetUpdate(float deltaTime) { }

        /// <summary>Puppet: every physics step.</summary>
        public virtual void PuppetFixedUpdate(float fixedDeltaTime) { }

        /// <summary>
        /// This machine simulates the creature again after showing it as a puppet: undoes what presenting it changed that the
        /// creature's own code would not set back by itself.
        /// </summary>
        public virtual void OnTakenOver() { }
    }
}
