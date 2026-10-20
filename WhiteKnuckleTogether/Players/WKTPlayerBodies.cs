using System;
using System.Collections.Generic;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using SendType = Steamworks.Data.SendType;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A player's body as others show it: the buffs the game sizes the player's controller by, and the perks that replace
    /// their sounds (Roach Mode halves the body and gives it a roach's voice). Buffs are added to one, as the game adds them.
    /// </summary>
    public readonly struct WKTPlayerBodyState {
        /// <summary>The most sound-replacing perks a state carries.</summary>
        public const int MAX_VOICE_PERKS = 8;
        /// <summary>The range of <see cref="ScaleBuff"/>, which keeps the body larger than nothing.</summary>
        public const float MIN_SCALE_BUFF = -0.9f;
        public const float MAX_SCALE_BUFF = 4f;
        /// <summary>The range of <see cref="WidthBuff"/>, which keeps the controller's radius above zero.</summary>
        public const float MIN_WIDTH_BUFF = -0.25f;
        public const float MAX_WIDTH_BUFF = 2f;
        /// <summary>Buffs closer than this make the same body.</summary>
        private const float c_buff_tolerance = 0.01f;
        public readonly byte PlayerId;
        /// <summary>The game's <c>addPlayerScale</c>: the player is <c>playerScale * (1 + this)</c> big.</summary>
        public readonly float ScaleBuff;
        /// <summary>The game's <c>addPlayerWidth</c>: the controller's radius before scaling is <c>0.3 + this</c>.</summary>
        public readonly float WidthBuff;
        private readonly string[]? m_voicePerks;

        /// <param name="voicePerks">Kept, never changed.</param>
        public WKTPlayerBodyState(byte playerId, float scaleBuff, float widthBuff, string[]? voicePerks) {
            PlayerId = playerId;
            ScaleBuff = scaleBuff;
            WidthBuff = widthBuff;
            m_voicePerks = voicePerks;
        }

        /// <summary>The ids of the player's perks that replace their sounds, in their order.</summary>
        public IReadOnlyList<string> VoicePerks => m_voicePerks ?? Array.Empty<string>();

        /// <summary>This state's sound-replacing perks on a body of another size.</summary>
        public WKTPlayerBodyState WithShape(byte playerId, float scaleBuff, float widthBuff) => new(playerId, scaleBuff, widthBuff, m_voicePerks);

        /// <summary>Whether this is sized like <paramref name="other"/>, whatever their sounds.</summary>
        public bool HasSameShape(in WKTPlayerBodyState other) =>
            Mathf.Abs(ScaleBuff - other.ScaleBuff) <= c_buff_tolerance && Mathf.Abs(WidthBuff - other.WidthBuff) <= c_buff_tolerance;

        /// <summary>Whether this differs enough from <paramref name="other"/> to be worth sending.</summary>
        public bool DiffersFrom(in WKTPlayerBodyState other) => !HasSameShape(other) || !HasVoicePerks(other.VoicePerks);

        /// <summary>Whether <paramref name="perkIds"/> are exactly <see cref="VoicePerks"/>, in order.</summary>
        public bool HasVoicePerks(IReadOnlyList<string> perkIds) {
            var own = VoicePerks;
            if (own.Count != perkIds.Count) return false;
            for (var i = 0; i < own.Count; i++)
                if (!string.Equals(own[i], perkIds[i], StringComparison.Ordinal)) return false;
            return true;
        }

        public void Write(NetDataWriter writer) {
            writer.Put(PlayerId);
            writer.Put(ScaleBuff);
            writer.Put(WidthBuff);
            var perks = VoicePerks;
            writer.Put((byte)perks.Count);
            foreach (var perk in perks)
                writer.Put(perk, WKTWire.MAX_ID_LENGTH);
        }

        /// <summary>Fails on truncation, buffs out of range or NaN, more than <see cref="MAX_VOICE_PERKS"/> perks, and empty or overlong ids.</summary>
        public static bool TryRead(NetDataReader reader, out WKTPlayerBodyState state) {
            state = default;
            if (!reader.TryGetByte(out var playerId) || !TryGetBuff(reader, MIN_SCALE_BUFF, MAX_SCALE_BUFF, out var scale) ||
                !TryGetBuff(reader, MIN_WIDTH_BUFF, MAX_WIDTH_BUFF, out var width) || !reader.TryGetByte(out var count) || count > MAX_VOICE_PERKS) return false;
            var perks = count == 0 ? null : new string[count];
            for (var i = 0; i < count; i++) {
                if (!WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var perk) || perk.Length == 0) return false;
                perks![i] = perk;
            }
            state = new WKTPlayerBodyState(playerId, scale, width, perks);
            return true;
        }

        private static bool TryGetBuff(NetDataReader reader, float min, float max, out float value) =>
            reader.TryGetFloat(out value) && value >= min && value <= max;
    }

    /// <summary>
    /// Shares each player's body (see <see cref="WKTPlayerBodyState"/>), so a player in Roach Mode is small and sounds like a
    /// roach for everyone, in every kind of run. Polled twice a second and sent when it changes, through the host, which
    /// keeps everyone's for players who join later. Owned by <see cref="WKTPlayerSync"/>.
    /// </summary>
    internal sealed class WKTPlayerBodies {
        private const float c_poll_interval = 0.5f;
        private const string c_scale_buff = "addPlayerScale";
        private const string c_width_buff = "addPlayerWidth";
        private readonly WKTNetwork m_network;
        private readonly Dictionary<byte, WKTPlayerBodyState> m_states = new();
        private readonly List<string> m_perkIds = new();
        private WKTPlayerBodyState m_sent;
        private bool m_sentOnce;
        private float m_nextPoll;

        public WKTPlayerBodies(WKTNetwork network) => m_network = network;

        /// <summary>The local player's body as last sent: how others show it. A normal body before the first send.</summary>
        public WKTPlayerBodyState LastSent => m_sent;

        public bool TryGet(byte playerId, out WKTPlayerBodyState state) => m_states.TryGetValue(playerId, out state);

        public void Forget(byte playerId) => m_states.Remove(playerId);

        public void Update(ENT_Player player) {
            if (m_network.LocalPlayer is not { } local || Time.unscaledTime < m_nextPoll) return;
            m_nextPoll = Time.unscaledTime + c_poll_interval;
            var buffs = player.curBuffs;
            var scale = buffs == null ? 0f : Clamp(buffs.GetBuff(c_scale_buff), WKTPlayerBodyState.MIN_SCALE_BUFF, WKTPlayerBodyState.MAX_SCALE_BUFF);
            var width = buffs == null ? 0f : Clamp(buffs.GetBuff(c_width_buff), WKTPlayerBodyState.MIN_WIDTH_BUFF, WKTPlayerBodyState.MAX_WIDTH_BUFF);
            ReadVoicePerks(player);
            var body = m_sent.HasVoicePerks(m_perkIds) ? m_sent.WithShape(local.Id, scale, width) : new WKTPlayerBodyState(local.Id, scale, width, ToArrayOrNull(m_perkIds));
            if (m_sentOnce && !body.DiffersFrom(m_sent)) return;
            m_sentOnce = true;
            m_sent = body;
            var writer = m_network.BeginMessage(WKTMessageId.PlayerBody);
            m_sent.Write(writer);
            if (m_network.Mode == NetworkMode.Host)
                m_network.Broadcast(writer, SendType.Reliable);
            else
                m_network.SendToHost(writer, SendType.Reliable);
        }

        public void OnMessage(WKTPlayer sender, NetDataReader reader) {
            if (!WKTPlayerBodyState.TryRead(reader, out var state)) return;
            if (m_network.Mode == NetworkMode.Host) {
                if (sender.IsHost || state.PlayerId != sender.Id) return;
                var relay = m_network.BeginMessage(WKTMessageId.PlayerBody);
                state.Write(relay);
                m_network.Broadcast(relay, SendType.Reliable, sender);
            }
            else if (!sender.IsHost) return;
            m_states[state.PlayerId] = state;
        }

        public void SendAll(WKTPlayer player) {
            foreach (var state in m_states.Values) {
                if (state.PlayerId == player.Id) continue;
                var writer = m_network.BeginMessage(WKTMessageId.PlayerBody);
                state.Write(writer);
                m_network.SendToPlayer(player, writer, SendType.Reliable);
            }
            if (!m_sentOnce) return;
            var own = m_network.BeginMessage(WKTMessageId.PlayerBody);
            m_sent.Write(own);
            m_network.SendToPlayer(player, own, SendType.Reliable);
        }

        /// <summary>Fills <see cref="m_perkIds"/> with the local player's sound-replacing perks that fit on the wire.</summary>
        private void ReadVoicePerks(ENT_Player player) {
            WKTVoiceCosmetics.GetLocalPerkVoiceIds(player, m_perkIds);
            m_perkIds.RemoveAll(id => id.Length > WKTWire.MAX_ID_LENGTH);
            if (m_perkIds.Count > WKTPlayerBodyState.MAX_VOICE_PERKS)
                m_perkIds.RemoveRange(WKTPlayerBodyState.MAX_VOICE_PERKS, m_perkIds.Count - WKTPlayerBodyState.MAX_VOICE_PERKS);
        }

        private static string[]? ToArrayOrNull(IReadOnlyList<string> ids) {
            if (ids.Count == 0) return null;
            var array = new string[ids.Count];
            for (var i = 0; i < array.Length; i++)
                array[i] = ids[i];
            return array;
        }

        /// <summary>NaN, which the game never makes, counts as no buff.</summary>
        private static float Clamp(float value, float min, float max) => float.IsNaN(value) ? 0f : Mathf.Clamp(value, min, max);
    }
}
