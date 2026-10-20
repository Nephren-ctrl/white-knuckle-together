using System;
using System.Collections.Generic;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A set of progression: unlocked <c>ProgressionUnlock</c> ids, flagged achievement ids, true save flags, and facility
    /// upgrades. Used both for one player's real progression and for the session's shared (intersected) progression.
    /// Anything missing from a profile counts as locked, so every limit here can only ever hide content, never reveal it.
    /// </summary>
    public sealed class WKTProgressionProfile {
        private static readonly Func<WKTProgressionProfile, HashSet<string>> s_unlocksOf = p => p.m_unlocks;
        private static readonly Func<WKTProgressionProfile, HashSet<string>> s_achievementsOf = p => p.m_achievements;
        private static readonly Func<WKTProgressionProfile, HashSet<string>> s_flagsOf = p => p.m_flags;
        private readonly HashSet<string> m_unlocks = new();
        private readonly HashSet<string> m_achievements = new();
        private readonly HashSet<string> m_flags = new();
        /// <summary>Facility id to (upgrade id to owned).</summary>
        private readonly Dictionary<string, Dictionary<string, bool>> m_upgrades = new();

        public int UnlockCount => m_unlocks.Count;
        public int AchievementCount => m_achievements.Count;
        public int FlagCount => m_flags.Count;
        public int FacilityCount => m_upgrades.Count;
        public int UpgradeCount { get; private set; }

        public bool HasUnlock(string? id) => id != null && m_unlocks.Contains(id);

        public bool HasAchievement(string? id) => id != null && m_achievements.Contains(id);

        public bool HasFlag(string? name) => name != null && m_flags.Contains(name);

        /// <summary>Whether the facility's upgrade list contains the upgrade, owned or not (mirrors <c>Facility.HasUpgrade</c>).</summary>
        public bool HasUpgrade(string? facilityId, string? upgradeId) =>
            facilityId != null && upgradeId != null && m_upgrades.TryGetValue(facilityId, out var upgrades) &&
            upgrades.ContainsKey(upgradeId);

        /// <summary>Whether the upgrade is present and owned (mirrors <c>FacilityUpgrade.IsOwned</c>).</summary>
        public bool OwnsUpgrade(string? facilityId, string? upgradeId) =>
            facilityId != null && upgradeId != null && m_upgrades.TryGetValue(facilityId, out var upgrades) &&
            upgrades.TryGetValue(upgradeId, out var owned) && owned;

        /// <summary>
        /// Whether a gamemode gated by <paramref name="unlockAchievement"/> (<c>M_Gamemode.unlockAchievement</c>, empty
        /// when ungated) is available. The host may only start runs in gamemodes available in the shared profile.
        /// </summary>
        public bool IsGamemodeUnlocked(string? unlockAchievement) =>
            string.IsNullOrEmpty(unlockAchievement) || HasAchievement(unlockAchievement);

        /// <summary>Ids that are null, empty, or too long, and entries past the limits, are ignored and so read as locked.</summary>
        public void AddUnlock(string? id) => AddBounded(m_unlocks, id);

        public void AddAchievement(string? id) => AddBounded(m_achievements, id);

        public void AddFlag(string? name) => AddBounded(m_flags, name);

        /// <summary>Adds or updates an upgrade. Adding it again as owned marks it owned, but never the reverse.</summary>
        public void AddUpgrade(string? facilityId, string? upgradeId, bool owned) {
            if (!IsValidId(facilityId) || !IsValidId(upgradeId)) return;
            if (m_upgrades.TryGetValue(facilityId!, out var upgrades) && upgrades.TryGetValue(upgradeId!, out var wasOwned)) {
                upgrades[upgradeId!] = wasOwned || owned;
                return;
            }
            if (UpgradeCount >= WKTNetworkConsts.MAX_PROFILE_ENTRIES) return;
            if (upgrades == null) {
                if (m_upgrades.Count >= WKTNetworkConsts.MAX_PROFILE_FACILITIES) return;
                upgrades = new Dictionary<string, bool>();
                m_upgrades.Add(facilityId!, upgrades);
            }
            upgrades.Add(upgradeId!, owned);
            UpgradeCount++;
        }

        public void Clear() {
            m_unlocks.Clear();
            m_achievements.Clear();
            m_flags.Clear();
            m_upgrades.Clear();
            UpgradeCount = 0;
        }

        public void CopyFrom(WKTProgressionProfile other) {
            if (ReferenceEquals(other, this)) return;
            Clear();
            m_unlocks.UnionWith(other.m_unlocks);
            m_achievements.UnionWith(other.m_achievements);
            m_flags.UnionWith(other.m_flags);
            foreach (var facility in other.m_upgrades)
                m_upgrades.Add(facility.Key, new Dictionary<string, bool>(facility.Value));
            UpgradeCount = other.UpgradeCount;
        }

        /// <summary>
        /// Replaces this profile with what every one of <paramref name="profiles"/> has. An upgrade is present only if
        /// every profile has it, and owned only if every profile owns it. No profiles means nothing is shared.
        /// </summary>
        /// <exception cref="ArgumentException">This profile is one of the inputs, which clearing it would destroy.</exception>
        public void SetToIntersection(IReadOnlyList<WKTProgressionProfile> profiles) {
            for (var i = 0; i < profiles.Count; i++) {
                if (ReferenceEquals(profiles[i], this))
                    throw new ArgumentException("The result profile cannot also be an input.", nameof(profiles));
            }
            Clear();
            switch (profiles.Count) {
                case 0:
                    return;
                case 1:
                    CopyFrom(profiles[0]);
                    return;
            }
            var first = profiles[0];
            IntersectSet(first.m_unlocks, profiles, s_unlocksOf, m_unlocks);
            IntersectSet(first.m_achievements, profiles, s_achievementsOf, m_achievements);
            IntersectSet(first.m_flags, profiles, s_flagsOf, m_flags);
            foreach (var facility in first.m_upgrades) {
                foreach (var upgrade in facility.Value) {
                    var present = true;
                    var owned = upgrade.Value;
                    for (var i = 1; i < profiles.Count; i++) {
                        if (!profiles[i].m_upgrades.TryGetValue(facility.Key, out var other) ||
                            !other.TryGetValue(upgrade.Key, out var otherOwned)) {
                            present = false;
                            break;
                        }
                        owned &= otherOwned;
                    }
                    if (present)
                        AddUpgrade(facility.Key, upgrade.Key, owned);
                }
            }
        }

        /// <summary>The adders enforce every wire limit, so whatever is written here always reads back.</summary>
        public void Write(NetDataWriter writer) {
            WriteSet(writer, m_unlocks);
            WriteSet(writer, m_achievements);
            WriteSet(writer, m_flags);
            writer.Put((ushort)m_upgrades.Count);
            foreach (var facility in m_upgrades) {
                writer.Put(facility.Key, WKTNetworkConsts.MAX_PROFILE_ID_LENGTH);
                writer.Put((ushort)facility.Value.Count);
                foreach (var upgrade in facility.Value) {
                    writer.Put(upgrade.Key, WKTNetworkConsts.MAX_PROFILE_ID_LENGTH);
                    writer.Put(upgrade.Value);
                }
            }
        }

        /// <summary>
        /// Reads a profile written by <see cref="Write"/> into <paramref name="profile"/> (cleared first). Fails without
        /// throwing on truncated data, counts above the wire limits, duplicates, or invalid ids. On failure the profile
        /// is left empty, which shares nothing.
        /// </summary>
        public static bool TryRead(NetDataReader reader, WKTProgressionProfile profile) {
            profile.Clear();
            if (TryReadSet(reader, profile.m_unlocks) && TryReadSet(reader, profile.m_achievements) &&
                TryReadSet(reader, profile.m_flags) && TryReadUpgrades(reader, profile))
                return true;
            profile.Clear();
            return false;
        }

        private static bool IsValidId(string? id) =>
            !string.IsNullOrEmpty(id) && id!.Length <= WKTNetworkConsts.MAX_PROFILE_ID_LENGTH;

        private static void AddBounded(HashSet<string> set, string? id) {
            if (IsValidId(id) && set.Count < WKTNetworkConsts.MAX_PROFILE_ENTRIES)
                set.Add(id!);
        }

        private static void IntersectSet(HashSet<string> first, IReadOnlyList<WKTProgressionProfile> profiles,
                                         Func<WKTProgressionProfile, HashSet<string>> selector, HashSet<string> result) {
            foreach (var id in first) {
                var shared = true;
                for (var i = 1; i < profiles.Count; i++) {
                    if (selector(profiles[i]).Contains(id)) continue;
                    shared = false;
                    break;
                }
                if (shared)
                    result.Add(id);
            }
        }

        private static void WriteSet(NetDataWriter writer, HashSet<string> set) {
            writer.Put((ushort)set.Count);
            foreach (var id in set)
                writer.Put(id, WKTNetworkConsts.MAX_PROFILE_ID_LENGTH);
        }

        private static bool TryReadSet(NetDataReader reader, HashSet<string> set) {
            if (!reader.TryGetUShort(out var count) || count > WKTNetworkConsts.MAX_PROFILE_ENTRIES) return false;
            for (var i = 0; i < count; i++) {
                if (!TryReadId(reader, out var id) || !set.Add(id)) return false;
            }
            return true;
        }

        private static bool TryReadUpgrades(NetDataReader reader, WKTProgressionProfile profile) {
            if (!reader.TryGetUShort(out var facilityCount) || facilityCount > WKTNetworkConsts.MAX_PROFILE_FACILITIES)
                return false;
            for (var i = 0; i < facilityCount; i++) {
                if (!TryReadId(reader, out var facilityId) || profile.m_upgrades.ContainsKey(facilityId)) return false;
                if (!reader.TryGetUShort(out var upgradeCount) || upgradeCount == 0 ||
                    profile.UpgradeCount + upgradeCount > WKTNetworkConsts.MAX_PROFILE_ENTRIES)
                    return false;
                for (var j = 0; j < upgradeCount; j++) {
                    if (!TryReadId(reader, out var upgradeId) || profile.HasUpgrade(facilityId, upgradeId)) return false;
                    if (!reader.TryGetBool(out var owned)) return false;
                    profile.AddUpgrade(facilityId, upgradeId, owned);
                }
            }
            return true;
        }

        /// <summary>Empty ids are malformed since the writer never produces them.</summary>
        private static bool TryReadId(NetDataReader reader, out string id) =>
            WKTReaderUtility.TryGetBoundedString(reader, WKTNetworkConsts.MAX_PROFILE_ID_LENGTH, out id) && id.Length > 0;
    }
}
