using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The color that stands for a player wherever they are named: the lobby list, the chat, the voice HUD and pause panel, the
    /// race results and the name shown when looking at them. One per player id, so it is the same on every machine and stays
    /// theirs for the session. Readable on dark backgrounds. Use <see cref="WKTPlayer.Color"/> where a player is at hand.
    /// </summary>
    internal static class WKTPlayerColors {
        private static readonly Color32[] s_colors = {
            new(255, 196, 64, 255),
            new(102, 204, 255, 255),
            new(140, 230, 110, 255),
            new(255, 128, 170, 255),
            new(190, 150, 255, 255),
            new(255, 150, 90, 255),
            new(90, 230, 200, 255),
            new(240, 240, 140, 255)
        };
        private static readonly string[] s_hex = BuildHex();

        public static Color32 Get(byte playerId) => s_colors[playerId % s_colors.Length];

        /// <summary>The color as "RRGGBB", for rich text tags.</summary>
        public static string GetHex(byte playerId) => s_hex[playerId % s_hex.Length];

        private static string[] BuildHex() {
            var hex = new string[s_colors.Length];
            for (var i = 0; i < hex.Length; i++)
                hex[i] = s_colors[i].r.ToString("X2") + s_colors[i].g.ToString("X2") + s_colors[i].b.ToString("X2");
            return hex;
        }
    }
}
