using System;

namespace WhiteKnuckleTogether {
    public sealed class ByteIdPool : IdPoolBase<byte> {
        public ByteIdPool() => Reset();

        /// <summary>
        /// Moves the pool past every id up to <paramref name="maxId"/>, recycling the ones <paramref name="isInUse"/> rejects,
        /// so ids already taken by players of an adopted session are never handed out again.
        /// </summary>
        public void SkipInUse(byte maxId, Func<byte, bool> isInUse) {
            // Bounded, since an exhausted pool starts handing back recycled ids below maxId.
            for (var step = 0; step < byte.MaxValue; step++) {
                var id = GetNext();
                if (!isInUse(id))
                    Recycle(id);
                if (id >= maxId) return;
            }
        }

        protected override byte GetMinimum() => 1;

        protected override void Increment(ref byte value) => value++;

        protected override bool IsMaxValue(byte value) => value == byte.MaxValue;
    }
}
