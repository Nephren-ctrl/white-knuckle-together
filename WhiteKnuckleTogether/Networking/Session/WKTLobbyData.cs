using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Parses Steam lobby data written by the host, and member data written by each player. Lobby data is untrusted, so
    /// only exact names of defined values are accepted (no numbers, whitespace, case differences or comma separated
    /// combinations), and lists are capped.
    /// </summary>
    internal static class WKTLobbyData {
        /// <summary>Steam starts the game with this argument followed by the lobby id when an invite launches it.</summary>
        private const string c_connect_lobby_arg = "+connect_lobby";
        private const char c_modifier_separator = ',';
        /// <summary>Cosmetic ids come from cosmetic files, which may put commas in them but never line breaks.</summary>
        private const char c_id_separator = '\n';
        private const string c_default_lobby_name_suffix = "'s Lobby";
        private static readonly GamemodeId[] s_gamemodeIds = (GamemodeId[])Enum.GetValues(typeof(GamemodeId));
        private static readonly string[] s_gamemodeNames = Array.ConvertAll(s_gamemodeIds, id => id.ToString());
        private static readonly PlayModeId[] s_playModeIds = (PlayModeId[])Enum.GetValues(typeof(PlayModeId));
        private static readonly string[] s_playModeNames = Array.ConvertAll(s_playModeIds, id => id.ToString());
        private static readonly GooFollowId[] s_gooFollowIds = (GooFollowId[])Enum.GetValues(typeof(GooFollowId));
        private static readonly string[] s_gooFollowNames = Array.ConvertAll(s_gooFollowIds, id => id.ToString());
        private static readonly OrbRadiusId[] s_orbRadiusIds = (OrbRadiusId[])Enum.GetValues(typeof(OrbRadiusId));
        private static readonly string[] s_orbRadiusNames = Array.ConvertAll(s_orbRadiusIds, id => id.ToString());
        private static readonly PlayerCollisionId[] s_playerCollisionIds = (PlayerCollisionId[])Enum.GetValues(typeof(PlayerCollisionId));
        private static readonly string[] s_playerCollisionNames = Array.ConvertAll(s_playerCollisionIds, id => id.ToString());
        private static readonly ModifierId[] s_modifierIds = (ModifierId[])Enum.GetValues(typeof(ModifierId));
        private static readonly string[] s_modifierNames = Array.ConvertAll(s_modifierIds, id => id.ToString());
        private static readonly ushort[] s_modifierBits = Array.ConvertAll(s_modifierIds, WKTRunStart.GetModifierBit);
        private static readonly string[] s_shortcutNames = Array.ConvertAll(WKTShortcuts.All, id => id.ToString());
        private static readonly ushort[] s_shortcutBits = Array.ConvertAll(WKTShortcuts.All, id => (ushort)WKTShortcuts.GetBit(id));
        private static readonly StringBuilder s_nameBuilder = new(WKTLobbyConsts.MAX_LOBBY_NAME_LENGTH);

        public static bool TryParseGamemode(string? value, out GamemodeId id) => TryParseName(value, s_gamemodeNames, s_gamemodeIds, out id);

        public static bool TryParsePlayMode(string? value, out PlayModeId id) => TryParseName(value, s_playModeNames, s_playModeIds, out id);

        public static bool TryParseGooFollow(string? value, out GooFollowId id) => TryParseName(value, s_gooFollowNames, s_gooFollowIds, out id);

        public static bool TryParseOrbRadius(string? value, out OrbRadiusId id) => TryParseName(value, s_orbRadiusNames, s_orbRadiusIds, out id);

        public static bool TryParsePlayerCollision(string? value, out PlayerCollisionId id) =>
            TryParseName(value, s_playerCollisionNames, s_playerCollisionIds, out id);

        /// <summary>
        /// Parses the host's comma separated modifier list into a <see cref="WKTRunStart.ModifierMask"/>. Unknown names
        /// are skipped, so a list from a newer version still shows the modifiers this version knows.
        /// </summary>
        public static ushort ParseModifierMask(string? value) => ParseNameMask(value, s_modifierNames, s_modifierBits);

        /// <summary>Parses a comma separated shortcut list, written by <see cref="FormatShortcutMask"/>, into one bit per <see cref="ShortcutId"/>. Unknown names are skipped.</summary>
        public static byte ParseShortcutMask(string? value) => (byte)ParseNameMask(value, s_shortcutNames, s_shortcutBits);

        /// <summary>The shortcuts in <paramref name="mask"/> as a comma separated list of their names.</summary>
        public static string FormatShortcutMask(byte mask, StringBuilder sb) {
            sb.Clear();
            for (var i = 0; i < s_shortcutNames.Length; i++) {
                if ((mask & s_shortcutBits[i]) == 0) continue;
                if (sb.Length > 0)
                    sb.Append(c_modifier_separator);
                sb.Append(s_shortcutNames[i]);
            }
            var str = sb.ToString();
            sb.Clear();
            return str;
        }

        private static ushort ParseNameMask(string? value, string[] names, ushort[] bits) {
            if (value == null) return 0;
            ushort mask = 0;
            var start = 0;
            while (start < value.Length) {
                var end = value.IndexOf(c_modifier_separator, start);
                if (end < 0)
                    end = value.Length;
                var length = end - start;
                for (var i = 0; i < names.Length; i++) {
                    var name = names[i];
                    if (name.Length != length || string.CompareOrdinal(value, start, name, 0, length) != 0) continue;
                    mask |= bits[i];
                    break;
                }
                start = end + 1;
            }
            return mask;
        }

        /// <summary>Adds every modifier set in <paramref name="mask"/> to <paramref name="modifiers"/>.</summary>
        public static void AddModifiers(ushort mask, ISet<ModifierId> modifiers) {
            foreach (var modifierId in s_modifierIds) {
                if ((mask & WKTRunStart.GetModifierBit(modifierId)) != 0)
                    modifiers.Add(modifierId);
            }
        }

        /// <summary>
        /// Joins up to <see cref="WKTLobbyConsts.MAX_VOICES"/> ids for <see cref="WKTLobbyConsts.KEY_VOICES"/>. Ids that are
        /// empty, longer than <see cref="WKTLobbyConsts.MAX_VOICE_ID_LENGTH"/> or contain the separator are left out.
        /// </summary>
        public static string FormatVoiceIds(IEnumerable<string> ids, StringBuilder sb) {
            sb.Clear();
            var count = 0;
            foreach (var id in ids) {
                if (!IsVoiceId(id, 0, id.Length) || id.IndexOf(c_id_separator) >= 0) continue;
                if (count > 0)
                    sb.Append(c_id_separator);
                sb.Append(id);
                if (++count == WKTLobbyConsts.MAX_VOICES) break;
            }
            var result = sb.ToString();
            sb.Clear();
            return result;
        }

        /// <summary>
        /// Replaces <paramref name="ids"/> with the ids in <paramref name="value"/>, as <see cref="FormatVoiceIds"/> writes
        /// them. Empty and overlong ids are skipped, and at most <see cref="WKTLobbyConsts.MAX_VOICES"/> are read.
        /// </summary>
        public static void ParseVoiceIds(string? value, List<string> ids) {
            ids.Clear();
            if (value == null) return;
            var start = 0;
            while (start < value.Length && ids.Count < WKTLobbyConsts.MAX_VOICES) {
                var end = value.IndexOf(c_id_separator, start);
                if (end < 0)
                    end = value.Length;
                if (IsVoiceId(value, start, end - start))
                    ids.Add(value.Substring(start, end - start));
                start = end + 1;
            }
        }

        /// <summary>
        /// <paramref name="value"/> on one line: control characters and runs of whitespace become one space, the ends are
        /// trimmed and the rest is cut to <paramref name="maxLength"/>, never through a surrogate pair. Empty if nothing is left.
        /// </summary>
        public static string SanitizeName(string? value, int maxLength) {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            s_nameBuilder.Clear();
            var pendingSpace = false;
            foreach (var c in value!) {
                if (char.IsControl(c) || char.IsWhiteSpace(c)) {
                    pendingSpace = s_nameBuilder.Length > 0;
                    continue;
                }
                var length = (pendingSpace ? 1 : 0) + (char.IsHighSurrogate(c) ? 2 : 1);
                if (s_nameBuilder.Length + length > maxLength) break;
                if (pendingSpace) {
                    s_nameBuilder.Append(' ');
                    pendingSpace = false;
                }
                s_nameBuilder.Append(c);
            }
            // A high surrogate with no low one after it, from a broken string, is dropped too.
            if (s_nameBuilder.Length > 0 && char.IsHighSurrogate(s_nameBuilder[s_nameBuilder.Length - 1]))
                s_nameBuilder.Length--;
            var result = s_nameBuilder.ToString();
            s_nameBuilder.Clear();
            return result;
        }

        /// <summary>The name of a lobby the local player hosts: their own <paramref name="localName"/>, or "Name's Lobby".</summary>
        public static string GetLobbyName(string? localName, string personaName) {
            var name = SanitizeName(localName, WKTLobbyConsts.MAX_LOBBY_NAME_LENGTH);
            if (name.Length > 0) return name;
            return SanitizeName(SanitizeName(personaName, WKTLobbyConsts.MAX_MEMBER_NAME_LENGTH) + c_default_lobby_name_suffix,
                WKTLobbyConsts.MAX_LOBBY_NAME_LENGTH);
        }

        /// <summary>
        /// Joins up to <see cref="WKTLobbyConsts.MAX_PLAYERS"/> player names for <see cref="WKTLobbyConsts.KEY_MEMBERS"/>, each
        /// sanitized to <see cref="WKTLobbyConsts.MAX_MEMBER_NAME_LENGTH"/>. Names left empty are skipped.
        /// </summary>
        public static string FormatMemberNames(IReadOnlyList<string> names, StringBuilder sb) {
            sb.Clear();
            var count = 0;
            for (var i = 0; i < names.Count && count < WKTLobbyConsts.MAX_PLAYERS; i++) {
                var name = SanitizeName(names[i], WKTLobbyConsts.MAX_MEMBER_NAME_LENGTH);
                if (name.Length == 0) continue;
                if (count > 0)
                    sb.Append(c_id_separator);
                sb.Append(name);
                count++;
            }
            var result = sb.ToString();
            sb.Clear();
            return result;
        }

        /// <summary>
        /// Replaces <paramref name="names"/> with the names in <paramref name="value"/>, as <see cref="FormatMemberNames"/> writes
        /// them, sanitized again since lobby data is untrusted. At most <see cref="WKTLobbyConsts.MAX_PLAYERS"/> are read.
        /// </summary>
        public static void ParseMemberNames(string? value, List<string> names) {
            names.Clear();
            if (value == null) return;
            var start = 0;
            while (start < value.Length && names.Count < WKTLobbyConsts.MAX_PLAYERS) {
                var end = value.IndexOf(c_id_separator, start);
                if (end < 0)
                    end = value.Length;
                var name = SanitizeName(value.Substring(start, end - start), WKTLobbyConsts.MAX_MEMBER_NAME_LENGTH);
                if (name.Length > 0)
                    names.Add(name);
                start = end + 1;
            }
        }

        /// <summary>
        /// Whether a listed lobby can be joined, in the order the host's handshake checks: versions first, then whether a run
        /// is under way, then room.
        /// </summary>
        public static WKTLobbyListingStatus GetListingStatus(string? modVersion, string? gameVersion, bool inRun, int memberCount,
            int maxMembers, string localModVersion, string localGameVersion) {
            if (!string.Equals(modVersion, localModVersion, StringComparison.Ordinal))
                return WKTLobbyListingStatus.ModVersionMismatch;
            if (!string.Equals(gameVersion, localGameVersion, StringComparison.Ordinal))
                return WKTLobbyListingStatus.GameVersionMismatch;
            if (inRun)
                return WKTLobbyListingStatus.InRun;
            // Steam reports no limit as zero.
            var capacity = maxMembers > 0 ? Math.Min(maxMembers, WKTLobbyConsts.MAX_PLAYERS) : WKTLobbyConsts.MAX_PLAYERS;
            if (memberCount >= capacity)
                return WKTLobbyListingStatus.Full;
            return WKTLobbyListingStatus.Joinable;
        }

        /// <summary><paramref name="value"/> (below <see cref="WKTLobbyConsts.JOIN_CODE_COUNT"/>) as a join code, padded with leading zeros.</summary>
        public static string FormatJoinCode(int value) =>
            value.ToString("D" + WKTLobbyConsts.JOIN_CODE_LENGTH.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        /// <summary>
        /// Whether <paramref name="value"/> is a join code: exactly <see cref="WKTLobbyConsts.JOIN_CODE_LENGTH"/> digits 0 to 9,
        /// ignoring whitespace around them, as typed or pasted.
        /// </summary>
        public static bool TryParseJoinCode(string? value, out string code) {
            code = string.Empty;
            if (value == null) return false;
            var trimmed = value.Trim();
            if (trimmed.Length != WKTLobbyConsts.JOIN_CODE_LENGTH) return false;
            foreach (var c in trimmed)
                if (c < '0' || c > '9') return false;
            code = trimmed;
            return true;
        }

        public static bool TryGetConnectLobbyId(string[] args, out ulong lobbyId) {
            lobbyId = 0;
            for (var i = 0; i < args.Length - 1; i++) {
                if (string.Equals(args[i], c_connect_lobby_arg, StringComparison.Ordinal))
                    return ulong.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out lobbyId) && lobbyId != 0;
            }
            return false;
        }

        private static bool TryParseName<T>(string? value, string[] names, T[] ids, out T id) where T : struct {
            id = default;
            if (value == null) return false;
            for (var i = 0; i < names.Length; i++) {
                if (!string.Equals(value, names[i], StringComparison.Ordinal)) continue;
                id = ids[i];
                return true;
            }
            return false;
        }

        private static bool IsVoiceId(string value, int start, int length) {
            if (length == 0 || length > WKTLobbyConsts.MAX_VOICE_ID_LENGTH) return false;
            for (var i = start; i < start + length; i++)
                if (!char.IsWhiteSpace(value[i])) return true;
            return false;
        }
    }
}
