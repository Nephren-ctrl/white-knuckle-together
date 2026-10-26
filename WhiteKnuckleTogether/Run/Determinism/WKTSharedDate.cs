using System;
using System.Globalization;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// <c>SSM_DateTimeRange.Check</c> against a clock every machine of a run shares, the host's at run start
    /// (<see cref="WKTRunStart.ClockTicks"/>), rather than each player's own clock, time zone and culture. It gates the
    /// Halloween season (Arterial Bypass, decorations, masks) and night-time scares, so players would otherwise generate
    /// different worlds.
    /// </summary>
    internal static class WKTSharedDate {
        /// <summary>
        /// <paramref name="trueValue"/> while <paramref name="now"/> lies between <paramref name="start"/> and
        /// <paramref name="end"/>, as the game's check does, and <paramref name="falseValue"/> otherwise or when either cannot
        /// be read.
        /// </summary>
        public static float Check(string? start, string? end, DateTime now, float trueValue, float falseValue) =>
            TryResolve(start, now, out var from) && TryResolve(end, now, out var to) && now >= from && now <= to ? trueValue : falseValue;

        /// <summary>
        /// Reads a date as the game's <c>DateTime.Parse</c> does on an English machine whose clock reads <paramref name="now"/>: a
        /// time alone ("3AM") is on <paramref name="now"/>'s day, and a date without a year ("October 25") in its year.
        /// </summary>
        public static bool TryResolve(string? text, DateTime now, out DateTime result) {
            result = default;
            if (string.IsNullOrEmpty(text) ||
                !DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault | DateTimeStyles.AllowWhiteSpaces, out var parsed)) return false;
            if (parsed.Date == DateTime.MinValue.Date) {
                result = now.Date + parsed.TimeOfDay;
                return true;
            }
            if (HasYear(text!)) {
                result = parsed;
                return true;
            }
            if (parsed.Month == 2 && parsed.Day == 29 && !DateTime.IsLeapYear(now.Year)) return false;
            result = new DateTime(now.Year, parsed.Month, parsed.Day) + parsed.TimeOfDay;
            return true;
        }

        /// <summary>Whether the text names a year: four digits in a row.</summary>
        private static bool HasYear(string text) {
            var run = 0;
            foreach (var character in text) {
                run = char.IsDigit(character) ? run + 1 : 0;
                if (run == 4) return true;
            }
            return false;
        }
    }
}
