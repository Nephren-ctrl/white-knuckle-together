namespace WhiteKnuckleTogether {
    /// <summary>Reads and writes a global random generator's state. A seam so state isolation is testable without Unity.</summary>
    internal interface IRandomStateAccess<TState> where TState : struct {
        TState State { get; set; }
    }
}
