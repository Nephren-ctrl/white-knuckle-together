namespace WhiteKnuckleTogether {
    public sealed class UShortIdPool : IdPoolBase<ushort> {
        public UShortIdPool() => Reset();
        
        protected override ushort GetMinimum() => 1;
        
        protected override void Increment(ref ushort value) => value++;
        
        protected override bool IsMaxValue(ushort value) => value == ushort.MaxValue;
    }
}