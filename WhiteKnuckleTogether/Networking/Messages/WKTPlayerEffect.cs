using System;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The payload of <see cref="WKTMessageId.PlayerEffect"/>: an effect and its arguments. Which fields matter depends on the
    /// effect; unused ones are zero. Positions are world-offset-corrected.
    /// </summary>
    public readonly struct WKTPlayerEffect {
        private static readonly bool[] s_known = BuildKnown();

        public WKTPlayerEffectId Effect { get; }
        public float Amount { get; }
        public Vector3 Position { get; }
        public Vector3 Vector { get; }
        public bool Flag { get; }
        public string Text { get; }

        public WKTPlayerEffect(WKTPlayerEffectId effect, float amount = 0f, Vector3 position = default, Vector3 vector = default, bool flag = false,
                               string? text = null) {
            Effect = effect;
            Amount = amount;
            Position = position;
            Vector = vector;
            Flag = flag;
            Text = text ?? string.Empty;
        }

        public void Write(NetDataWriter writer) {
            writer.Put((byte)Effect);
            writer.Put(Amount);
            WKTWire.Put(writer, Position);
            WKTWire.Put(writer, Vector);
            writer.Put(Flag);
            writer.Put(Text, WKTWire.MAX_ID_LENGTH);
        }

        /// <summary>Fails on truncation, unknown effects, non-finite or out-of-range values and overlong text.</summary>
        public static bool TryRead(NetDataReader reader, out WKTPlayerEffect effect) {
            effect = default;
            if (!reader.TryGetByte(out var id) || !s_known[id]) return false;
            if (!reader.TryGetFloat(out var amount) || float.IsNaN(amount) || Math.Abs(amount) > WKTWire.MAX_AMOUNT) return false;
            if (!WKTWire.TryGetPosition(reader, out var position) || !WKTWire.TryGetPosition(reader, out var vector)) return false;
            if (!reader.TryGetByte(out var flag) || flag > 1) return false;
            if (!WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var text)) return false;
            effect = new WKTPlayerEffect((WKTPlayerEffectId)id, amount, position, vector, flag == 1, text);
            return true;
        }

        private static bool[] BuildKnown() {
            var known = new bool[byte.MaxValue + 1];
            foreach (WKTPlayerEffectId effect in Enum.GetValues(typeof(WKTPlayerEffectId)))
                known[(byte)effect] = true;
            return known;
        }
    }
}
