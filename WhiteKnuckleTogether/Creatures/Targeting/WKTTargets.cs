using System;
using System.Collections.Generic;
using HarmonyLib;
using Steamworks.Data;
using UnityEngine;
using Color = UnityEngine.Color;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Which player each enemy goes for, in a synced co-op run's host. Enemy code written for one player reads
    /// <c>ENT_Player.GetPlayer()</c> directly; those reads are rewritten to call the methods here, which answer for the
    /// player that enemy is engaging: the nearest living one, kept until another is clearly closer. When that is a remote
    /// player, positions come from their <see cref="WKTPlayerProxy"/> and effects are sent to their machine.
    /// <para>
    /// Everywhere else (singleplayer, clients, instanced runs) every method here does exactly what the code it replaces did.
    /// </para>
    /// </summary>
    internal static class WKTTargets {
        /// <summary>The local player, in <see cref="Assignment.PlayerId"/>.</summary>
        public const byte LOCAL = byte.MaxValue;
        /// <summary>Another player must be this much closer than the current target before an enemy switches to them.</summary>
        public const float SWITCH_RATIO = 0.8f;
        /// <summary>How many frames assignments are kept before stale ones are dropped.</summary>
        private const int c_prune_interval = 600;
        /// <summary>The camera shake and grab cooldown of a flier's buck, from <c>HandholdModule_FlierSteering.LateUpdate</c>.</summary>
        private const float c_buck_shake = 0.1f;
        private const float c_buck_drop_cooldown = 0.3f;
        /// <summary>A push source the player's knockback resistance weakens, as <c>ENT_Player.AddForce</c> checks.</summary>
        private const string c_resisted_source = "denizen";
        /// <summary>The Teeth's damage type, whose killing bite <c>ENT_Player.Damage</c> leaves to the Teeth.</summary>
        private const string c_teeth_damage = "teeth";
        private static readonly Dictionary<int, Assignment> s_assignments = new(256);
        private static readonly List<int> s_pruneScratch = new();
        /// <summary>Enemies holding on to their target (see <see cref="Pin"/>), by instance id.</summary>
        private static readonly HashSet<int> s_pinned = new();
        private static readonly byte[] s_ids = new byte[WKTLobbyConsts.MAX_PLAYERS];
        private static readonly Vector3[] s_positions = new Vector3[WKTLobbyConsts.MAX_PLAYERS];
        private static readonly bool[] s_eligible = new bool[WKTLobbyConsts.MAX_PLAYERS];
        private static readonly Action<ENT_Player, bool, float> s_dropEverything =
            AccessTools.MethodDelegate<Action<ENT_Player, bool, float>>(AccessTools.Method(typeof(ENT_Player), "DropEverything"));
        private static readonly WKTForceAccumulator s_forces = new();
        private static readonly Func<byte, bool> s_isEligible = IsEligible;
        private static readonly Func<byte, bool> s_isReachable = IsReachable;
        private static int s_lastPrune;
        /// <summary>The Teeth a target is being chosen for while it avoids players it cannot reach; null otherwise.</summary>
        private static Component? s_choosing;

        /// <summary>
        /// The player <paramref name="self"/> goes for: <see cref="LOCAL"/> or a remote player's id. Always the local player
        /// unless the host covers every player.
        /// </summary>
        public static byte TargetOf(Component? self) =>
            WKTPlayerPositions.IsCoveringAllPlayers && !ReferenceEquals(self, null) ? ChooseFor(self) : LOCAL;

        private static byte ChooseFor(Component self) {
            if (self == null) return LOCAL;
            var id = self.GetInstanceID();
            var frame = Time.frameCount;
            if (s_assignments.TryGetValue(id, out var assignment) && (assignment.Frame == frame || (s_pinned.Contains(id) && IsEligible(assignment.PlayerId)))) {
                if (assignment.Frame != frame)
                    s_assignments[id] = new Assignment(assignment.PlayerId, frame);
                return assignment.PlayerId;
            }
            // A Teeth that found no way to someone goes for those it can reach, as long as there is anyone.
            s_choosing = self is DEN_Teeth && WKTTeethReach.HasUnreachable(self) && AnyReachable(self) ? self : null;
            var isEligible = s_choosing != null ? s_isReachable : s_isEligible;
            if (WKTProgressTargets.Covers(self) && WKTProgressTargets.TryChoose(self, isEligible, out var chosen)) {
                s_choosing = null;
                s_assignments[id] = new Assignment(chosen, frame);
                return chosen;
            }
            var target = Choose(self.transform.position, assignment.Frame != 0 ? assignment.PlayerId : LOCAL);
            s_choosing = null;
            s_assignments[id] = new Assignment(target, frame);
            if (frame - s_lastPrune > c_prune_interval)
                Prune(frame);
            return target;
        }

        /// <summary>
        /// The nearest living player to <paramref name="position"/>, from this frame's player positions: <see cref="LOCAL"/> or a
        /// remote id, keeping <paramref name="current"/> unless another is clearly closer.
        /// </summary>
        public static byte Choose(Vector3 position, byte current) {
            var local = ENT_Player.playerObject;
            var count = WKTPlayerPositions.RemoteCount;
            for (var i = 0; i < count; i++) {
                var id = WKTPlayerPositions.GetRemoteId(i);
                s_ids[i] = id;
                s_positions[i] = WKTPlayerPositions.GetRemotePosition(i);
                s_eligible[i] = !WKTPlayerPositions.IsRemoteDead(i) && WKTPlayerProxy.TryGet(id, out _) && (s_choosing == null || !WKTTeethReach.IsUnreachable(s_choosing, id));
            }
            var localEligible = local != null && !local.dead && (s_choosing == null || !WKTTeethReach.IsUnreachable(s_choosing, LOCAL));
            return Choose(position, current, localEligible, local != null ? local.transform.position : Vector3.zero,
                s_ids.AsSpan(0, count), s_positions.AsSpan(0, count), s_eligible.AsSpan(0, count));
        }

        /// <summary>
        /// The nearest eligible player to <paramref name="position"/>: <see cref="LOCAL"/> or a remote id. Keeps
        /// <paramref name="current"/> while it is eligible, unless another is at least <see cref="SWITCH_RATIO"/> times its
        /// distance. The local player when nobody is eligible.
        /// </summary>
        public static byte Choose(Vector3 position, byte current, bool localAlive, Vector3 localPosition, ReadOnlySpan<byte> ids,
                                  ReadOnlySpan<Vector3> positions, ReadOnlySpan<bool> eligible) {
            var best = LOCAL;
            var bestSqr = float.PositiveInfinity;
            var currentSqr = float.PositiveInfinity;
            if (localAlive) {
                bestSqr = (localPosition - position).sqrMagnitude;
                if (current == LOCAL)
                    currentSqr = bestSqr;
            }
            for (var i = 0; i < ids.Length; i++) {
                if (!eligible[i]) continue;
                var sqr = (positions[i] - position).sqrMagnitude;
                if (ids[i] == current)
                    currentSqr = sqr;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = ids[i];
            }
            // Stick with the current target unless the best is clearly closer, so equally near players are not flipped between.
            return !float.IsPositiveInfinity(currentSqr) && bestSqr >= currentSqr * SWITCH_RATIO * SWITCH_RATIO ? current : best;
        }

        /// <summary>
        /// Keeps <paramref name="self"/> on the player it goes for while <paramref name="pinned"/>, however near others come, such
        /// as a Vent Thing holding someone. A pinned target that dies or leaves is let go.
        /// </summary>
        public static void Pin(Component self, bool pinned) {
            if (pinned)
                s_pinned.Add(self.GetInstanceID());
            else if (s_pinned.Count != 0)
                s_pinned.Remove(self.GetInstanceID());
        }

        public static void Reset() {
            s_assignments.Clear();
            s_pinned.Clear();
            WKTProgressTargets.Reset();
            WKTTeethReach.Reset();
        }

        /// <summary>Whether a player can be gone for: alive, and for a remote player, with a stand-in to go for.</summary>
        private static bool IsEligible(byte playerId) {
            if (playerId == LOCAL) return ENT_Player.playerObject is { dead: false };
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                if (WKTPlayerPositions.GetRemoteId(i) == playerId)
                    return !WKTPlayerPositions.IsRemoteDead(i) && WKTPlayerProxy.TryGet(playerId, out _);
            }
            return false;
        }

        /// <summary>Whether a player can be gone for and the Teeth being chosen for (<see cref="s_choosing"/>) can get to them.</summary>
        private static bool IsReachable(byte playerId) => IsEligible(playerId) && !WKTTeethReach.IsUnreachable(s_choosing!, playerId);

        private static bool AnyReachable(Component teeth) {
            if (IsEligible(LOCAL) && !WKTTeethReach.IsUnreachable(teeth, LOCAL)) return true;
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                var id = WKTPlayerPositions.GetRemoteId(i);
                if (IsEligible(id) && !WKTTeethReach.IsUnreachable(teeth, id)) return true;
            }
            return false;
        }

        /// <summary>Whether <paramref name="self"/> engages a remote player. One static check when not covering every player.</summary>
        private static bool TryGetRemote(Component? self, out WKTPlayerProxy? proxy) {
            proxy = null;
            var target = TargetOf(self);
            return target != LOCAL && WKTPlayerProxy.TryGet(target, out proxy);
        }

        private static bool IsLocalPlayer(GameEntity entity) => entity is ENT_Player && entity == ENT_Player.playerObject;

        #region Replacements

        /// <summary>Replaces <c>ENT_Player.GetPlayer().transform</c>.</summary>
        public static Transform TransformFor(Component? self) =>
            TryGetRemote(self, out var proxy) ? proxy!.transform : ENT_Player.GetPlayer().transform;

        /// <summary>Replaces reading <c>player.cam</c>.</summary>
        /// <summary>Whether the player an enemy goes for has a perk: a remote player's as their machine last said.</summary>
        public static bool HasPerkOf(ENT_Player player, string perkId, Component? self) =>
            TryGetRemote(self, out var proxy) && IsLocalPlayer(player) ? WKTPlayerPerks.Has(proxy!.PlayerId, perkId) : player.HasPerk(perkId);

        public static Camera CamOf(ENT_Player player, Component? self) => TryGetRemote(self, out var proxy) ? proxy!.Head : player.cam;

        /// <summary>Replaces reading <c>entity.dead</c>.</summary>
        public static bool DeadOf(GameEntity entity, Component? self) =>
            TryGetRemote(self, out var proxy) && IsLocalPlayer(entity) ? proxy!.dead : entity.dead;

        /// <summary>Replaces <c>entity.IsDead()</c>.</summary>
        public static bool IsDeadOf(GameEntity entity, Component? self) =>
            TryGetRemote(self, out var proxy) && IsLocalPlayer(entity) ? proxy!.dead : entity.IsDead();

        /// <summary>Replaces <c>entity.Damage(info)</c>.</summary>
        public static bool DamageVia(GameEntity entity, Damageable.DamageInfo info, Component? self) {
            if (!TryGetRemote(self, out var proxy) || !IsLocalPlayer(entity)) return entity.Damage(info);
            return !WKTAttackModules.IsClientDecided(self) && proxy!.Damage(info);
        }

        /// <summary>
        /// Replaces <c>entity.AddForce(v, source)</c>. A push on the player another machine plays, or on their proxy, reaches
        /// their machine, unless that machine decides this enemy's hits itself. A push from code that cannot name its enemy
        /// (an attack's helper object) never does, since its hit may be one the player's machine replays.
        /// </summary>
        public static void AddForceVia(GameEntity entity, Vector3 v, string source, Component? self) {
            if (entity is WKTPlayerProxy proxy || (TryGetRemote(self, out proxy) && IsLocalPlayer(entity))) {
                if (self != null && !WKTAttackModules.IsClientDecided(self))
                    s_forces.Add(proxy!.PlayerId, v, WKTForceAccumulator.IsResisted(source));
                return;
            }
            entity.AddForce(v, source);
        }

        /// <summary>Host only: sends this frame's summed pushes to the players they are for.</summary>
        public static void FlushForces() {
            while (s_forces.TryTake(out var player, out var force, out var resisted))
                SendEffect(player, new WKTPlayerEffect(WKTPlayerEffectId.AddForce, vector: ClampVector(force), flag: resisted));
        }

        /// <summary>Replaces <c>entity.Kill(type, info)</c>.</summary>
        public static void KillVia(GameEntity entity, string type, Damageable.DamageInfo info, Component? self) {
            if (TryGetRemote(self, out var proxy) && IsLocalPlayer(entity))
                proxy!.Kill(type, info);
            else
                entity.Kill(type, info);
        }

        /// <summary>Replaces <c>entity.SetGrappled(b, grappler)</c>.</summary>
        public static void SetGrappledVia(GameEntity entity, bool b, AIGameEntity.Grappler grappler, Component? self) {
            if (TryGetRemote(self, out var proxy) && IsLocalPlayer(entity))
                SendEffect(proxy!.PlayerId, new WKTPlayerEffect(WKTPlayerEffectId.SetGrappled, flag: b), self);
            else
                entity.SetGrappled(b, grappler);
        }

        public static bool IsHanging(ENT_Player player, Component? self) {
            if (!TryGetRemote(self, out var proxy)) return player.IsHanging();
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                if (WKTPlayerPositions.GetRemoteId(i) == proxy!.PlayerId)
                    return WKTPlayerPositions.IsRemoteHanging(i);
            }
            return false;
        }

        public static float GetCurrentFallDistance(ENT_Player player, Component? self) =>
            TryGetRemote(self, out var proxy) ? WKTRemoteFalls.Get(proxy!.PlayerId) : player.GetCurrentFallDistance();

        public static void DropEverything(ENT_Player player, bool destroyItems, float dropTime, Component? self) {
            if (TryGetRemote(self, out var proxy))
                SendEffect(proxy!.PlayerId, new WKTPlayerEffect(WKTPlayerEffectId.DropEverything, dropTime, flag: destroyItems), self);
            else
                s_dropEverything(player, destroyItems, dropTime);
        }

        public static void Lock(ENT_Player player, Component? self) {
            if (TryGetRemote(self, out var proxy))
                SendEffect(proxy!.PlayerId, new WKTPlayerEffect(WKTPlayerEffectId.Lock), self);
            else
                player.Lock();
        }

        public static void SetGravity(ENT_Player player, bool b, Component? self) {
            if (TryGetRemote(self, out var proxy))
                SendEffect(proxy!.PlayerId, new WKTPlayerEffect(WKTPlayerEffectId.SetGravity, flag: b), self);
            else
                player.SetGravity(b);
        }

        public static void SetGravityMult(ENT_Player player, float g, Component? self) {
            if (TryGetRemote(self, out var proxy))
                SendEffect(proxy!.PlayerId, new WKTPlayerEffect(WKTPlayerEffectId.SetGravityMult, g), self);
            else
                player.SetGravityMult(g);
        }

        public static void ScaleVelocityMagnitude(ENT_Player player, float magnitude, Component? self) {
            if (TryGetRemote(self, out var proxy))
                SendEffect(proxy!.PlayerId, new WKTPlayerEffect(WKTPlayerEffectId.ScaleVelocityMagnitude, magnitude), self);
            else
                player.ScaleVelocityMagnitude(magnitude);
        }

        public static void AddRotationVelocity(ENT_Player player, Vector3 v, Component? self) {
            if (TryGetRemote(self, out var proxy))
                SendEffect(proxy!.PlayerId, new WKTPlayerEffect(WKTPlayerEffectId.AddRotationVelocity, vector: v), self);
            else
                player.AddRotationVelocity(v);
        }

        public static void DamageGripStrength(ENT_Player player, float amount, bool onlyToGrabbingHands, Component? self) {
            if (TryGetRemote(self, out var proxy))
                SendEffect(proxy!.PlayerId, new WKTPlayerEffect(WKTPlayerEffectId.DamageGripStrength, amount, flag: onlyToGrabbingHands), self);
            else
                player.DamageGripStrength(amount, onlyToGrabbingHands);
        }

        public static void AddGripStrength(ENT_Player player, float amount, bool onlyToGrabbingHands, bool food, Component? self) {
            if (TryGetRemote(self, out var proxy))
                SendEffect(proxy!.PlayerId, new WKTPlayerEffect(WKTPlayerEffectId.AddGripStrength, amount, flag: onlyToGrabbingHands), self);
            else
                player.AddGripStrength(amount, onlyToGrabbingHands, food);
        }

        public static void SplatterScreenBlood(ENT_Player player, int amount, Component? self) {
            if (TryGetRemote(self, out var proxy))
                SendEffect(proxy!.PlayerId, new WKTPlayerEffect(WKTPlayerEffectId.SplatterScreenBlood, amount), self);
            else
                player.SplatterScreenBlood(amount);
        }

        public static void SplatterScreenBloodColored(ENT_Player player, int amount, Color color, Component? self) {
            if (TryGetRemote(self, out var proxy))
                SendEffect(proxy!.PlayerId, new WKTPlayerEffect(WKTPlayerEffectId.SplatterScreenBlood, amount), self);
            else
                player.SplatterScreenBlood(amount, color);
        }

        public static void SplatterScreenBloodTagged(ENT_Player player, int amount, List<string> tags, Component? self) {
            if (TryGetRemote(self, out var proxy))
                SendEffect(proxy!.PlayerId, new WKTPlayerEffect(WKTPlayerEffectId.SplatterScreenBlood, amount), self);
            else
                player.SplatterScreenBlood(amount, tags);
        }

        #endregion

        #region Effects

        public static WKTPlayerEffect DamageEffect(Damageable.DamageInfo info) =>
            new(WKTPlayerEffectId.Damage, Mathf.Clamp(info.amount, 0f, WKTWire.MAX_AMOUNT), WKTWorldOffset.ToWire(info.position, WKTWorldOffset.Get()),
                ClampVector(info.force), false, info.type);

        public static WKTPlayerEffect KillEffect(string? type, Damageable.DamageInfo? info, Vector3 fallbackPosition) =>
            new(WKTPlayerEffectId.Kill, 0f, WKTWorldOffset.ToWire(info?.position ?? fallbackPosition, WKTWorldOffset.Get()), default, false, type);

        /// <summary>
        /// Host only: sends an effect to the machine of the player it hits, unless <paramref name="self"/> is an enemy whose hits
        /// that player's own machine decides.
        /// </summary>
        public static void SendEffect(byte playerId, in WKTPlayerEffect effect, Component? self = null) {
            if (WKTAttackModules.IsClientDecided(self)) return;
            var network = WKT.InstanceOrNull?.Network;
            if (network == null || network.Mode != NetworkMode.Host || !network.TryGetPlayer(playerId, out var player) || player == null) return;
            var writer = network.BeginMessage(WKTMessageId.PlayerEffect);
            effect.Write(writer);
            network.SendToPlayer(player, writer, SendType.Reliable);
        }

        /// <summary>Client only: the host's world did something to the local player.</summary>
        public static void Apply(ENT_Player player, in WKTPlayerEffect effect) {
            switch (effect.Effect) {
                case WKTPlayerEffectId.Damage:
                    var viewers = WKTWorldSync.Instance?.Viewers;
                    var teethBite = string.Equals(effect.Text, c_teeth_damage, StringComparison.OrdinalIgnoreCase);
                    // Its kill is under way: the Teeth bites no more.
                    if (teethBite && viewers != null && viewers.TeethKills.IsDying) break;
                    var damage = Damageable.DamageInfo.CreateDamageInfo(effect.Amount, effect.Text);
                    damage.position = WKTWorldOffset.FromWire(effect.Position, WKTWorldOffset.Get());
                    damage.force = effect.Vector;
                    // The game leaves a killing bite to the Teeth, which here is the host's: its kill plays on this machine.
                    if (player.Damage(damage) && teethBite)
                        viewers?.OnLethalTeethBite(player);
                    break;
                case WKTPlayerEffectId.Kill:
                    var kill = Damageable.DamageInfo.CreateDamageInfo(0f, effect.Text);
                    kill.position = WKTWorldOffset.FromWire(effect.Position, WKTWorldOffset.Get());
                    player.Kill(effect.Text, kill);
                    break;
                case WKTPlayerEffectId.DropEverything:
                    s_dropEverything(player, effect.Flag, effect.Amount);
                    break;
                case WKTPlayerEffectId.Lock:
                    player.Lock();
                    break;
                case WKTPlayerEffectId.SetGravity:
                    player.SetGravity(effect.Flag);
                    break;
                case WKTPlayerEffectId.SetGravityMult:
                    player.SetGravityMult(effect.Amount);
                    break;
                case WKTPlayerEffectId.ScaleVelocityMagnitude:
                    player.ScaleVelocityMagnitude(effect.Amount);
                    break;
                case WKTPlayerEffectId.AddRotationVelocity:
                    player.AddRotationVelocity(effect.Vector);
                    break;
                case WKTPlayerEffectId.DamageGripStrength:
                    player.DamageGripStrength(effect.Amount, effect.Flag);
                    break;
                case WKTPlayerEffectId.AddGripStrength:
                    player.AddGripStrength(effect.Amount, effect.Flag);
                    break;
                case WKTPlayerEffectId.SplatterScreenBlood:
                    player.SplatterScreenBlood(Mathf.RoundToInt(effect.Amount));
                    break;
                case WKTPlayerEffectId.SetGrappled:
                    player.SetGrappled(effect.Flag);
                    break;
                case WKTPlayerEffectId.AddForce:
                    player.AddForce(effect.Vector, effect.Flag ? c_resisted_source : string.Empty);
                    break;
                case WKTPlayerEffectId.AddRoaches:
                    if (effect.Amount >= 1f)
                        CL_GameManager.AddRoaches(Mathf.RoundToInt(effect.Amount));
                    break;
                case WKTPlayerEffectId.Buck:
                    // HandholdModule_FlierSteering.LateUpdate's buck, on its rider.
                    player.Damage(Damageable.DamageInfo.CreateDamageInfo(effect.Amount, effect.Text));
                    CL_CameraControl.Shake(c_buck_shake);
                    player.DamageGripStrength(effect.Vector.x, onlyToGrabbingHands: true);
                    if (effect.Flag)
                        player.DropHang(c_buck_drop_cooldown);
                    break;
            }
        }

        private static Vector3 ClampVector(Vector3 vector) {
            var limit = WKTPlayerPose.MAX_COORDINATE;
            return new Vector3(Mathf.Clamp(vector.x, -limit, limit), Mathf.Clamp(vector.y, -limit, limit), Mathf.Clamp(vector.z, -limit, limit));
        }

        #endregion

        private static void Prune(int frame) {
            s_lastPrune = frame;
            s_pruneScratch.Clear();
            foreach (var pair in s_assignments) {
                if (frame - pair.Value.Frame > c_prune_interval)
                    s_pruneScratch.Add(pair.Key);
            }
            foreach (var id in s_pruneScratch) {
                s_assignments.Remove(id);
                s_pinned.Remove(id);
            }
        }

        private readonly struct Assignment {
            public readonly byte PlayerId;
            public readonly int Frame;

            public Assignment(byte playerId, int frame) {
                PlayerId = playerId;
                Frame = frame;
            }
        }
    }
}
