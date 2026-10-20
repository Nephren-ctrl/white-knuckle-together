using System;
using System.Collections;
using System.Collections.Generic;
using DarkMachine.AI;
using HarmonyLib;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Who the Teeth hunts and how it kills, in a synced co-op run. The host alone simulates the Teeth.
    /// <list type="bullet">
    /// <item>The game picks its target once, when it spawns. The host picks again every frame (<see cref="WKTTargets"/>), so a
    /// Teeth whose target died, or whose target is the dead host's body, goes for someone alive.</item>
    /// <item>A bite on a remote player reaches their machine, which decides whether it kills them, as for every other hit. A
    /// killing bite plays the game's kill on that machine, with its own copy of the Teeth taking over the camera
    /// (<c>AIC_Teeth_Chase.KillPlayerAnimation</c>), and tells the host. The host holds its Teeth still for as long.</item>
    /// <item>After a kill the game leaves the Teeth done for good (<c>hasKilledPlayer</c>), as the run would be over. Once the
    /// victim is dead it hunts the living again.</item>
    /// </list>
    /// Owned by <see cref="WKTViewerCreatures"/>.
    /// </summary>
    internal sealed class WKTTeethKills {
        /// <summary>How long the kill takes: the camera's two second move to the Teeth's mouth, then 2.8 s held there.</summary>
        public const float KILL_DURATION = 4.8f;
        /// <summary>How much longer than the kill the host holds its Teeth for a remote victim before letting go anyway.</summary>
        public const float HOLD_MARGIN = 1.5f;
        private const string c_damage_type = "teeth";
        private const string c_kill_sound = "teeth:killplayer";
        private const string c_chomp_sound = "teeth:killchomp";
        private const float c_camera_move_seconds = 2f;
        private const float c_camera_hold_seconds = 2.8f;
        private const float c_hand_emission = 15f;
        private static readonly int s_facingId = Animator.StringToHash("playerfacing");
        private static readonly int s_killTriggerId = Animator.StringToHash("killplayer");
        private static readonly AccessTools.FieldRef<AIGameEntity, List<AIStateComponent>> s_states =
            AccessTools.FieldRefAccess<AIGameEntity, List<AIStateComponent>>("aiStates");
        private static readonly AccessTools.FieldRef<AIC_Teeth_Chase, bool> s_hasKilled = AccessTools.FieldRefAccess<AIC_Teeth_Chase, bool>("hasKilledPlayer");
        private static readonly AccessTools.FieldRef<DEN_Teeth, bool> s_locked = AccessTools.FieldRefAccess<DEN_Teeth, bool>("locked");
        private static readonly AccessTools.FieldRef<DEN_Teeth, float> s_speedMult = AccessTools.FieldRefAccess<DEN_Teeth, float>("speedMult");
        private static readonly AccessTools.FieldRef<DEN_Teeth, bool> s_charging = AccessTools.FieldRefAccess<DEN_Teeth, bool>("charging");
        private static readonly Action<ENT_Player, bool, float> s_dropEverything =
            AccessTools.MethodDelegate<Action<ENT_Player, bool, float>>(AccessTools.Method(typeof(ENT_Player), "DropEverything"));
        private readonly WKTWorldSync m_sync;
        /// <summary>Host only: each Teeth held for a remote player's kill: the victim and since when.</summary>
        private readonly Dictionary<ushort, (byte Victim, float Since)> m_holds = new();
        private readonly List<ushort> m_releaseScratch = new();
        /// <summary>
        /// Client only: until when the local player is being killed by a Teeth. A time rather than a flag, as a coroutine
        /// dropped with its object never finishes.
        /// </summary>
        private float m_dyingUntil;

        public WKTTeethKills(WKTWorldSync sync) => m_sync = sync;

        /// <summary>Client only: the local player's kill is under way, so further bites do nothing.</summary>
        public bool IsDying => Time.time < m_dyingUntil;

        public void Clear() {
            m_holds.Clear();
            m_dyingUntil = 0f;
        }

        /// <summary>
        /// Replaces <c>component.Damage(info)</c> in <c>AIC_Teeth_Chase.Execute</c>. A bite on a remote player's stand-in goes to
        /// their machine and never counts as a kill here: the game would run its kill on the host's own player.
        /// </summary>
        public static bool Bite(GameEntity target, Damageable.DamageInfo info) {
            if (target is not WKTPlayerProxy proxy) return target.Damage(info);
            proxy.Damage(info);
            return false;
        }

        /// <summary>Whether a remote kill's hold is over: the victim died or left, or it ran past the kill by <see cref="HOLD_MARGIN"/>.</summary>
        public static bool IsHoldOver(float now, float since, bool victimGone) => victimGone || now - since > KILL_DURATION + HOLD_MARGIN;

        /// <summary>
        /// Whether a Teeth the game left done for good after killing the host hunts again: the host is dead by now and someone
        /// else is alive. While the host is still alive its kill is under way.
        /// </summary>
        public static bool ShouldHuntAgain(bool hasKilled, bool hostDead, bool anyoneAlive) => hasKilled && hostDead && anyoneAlive;

        /// <summary>Host only: picks each Teeth's target, holds those killing a remote player, and lets go of those done killing.</summary>
        public void UpdateHost(List<WKTNetIdentity> teeth) {
            var now = Time.time;
            var local = ENT_Player.playerObject;
            var hostDead = local == null || local.dead;
            var anyoneAlive = !hostDead || AnyRemoteAlive();
            foreach (var identity in teeth) {
                if (identity == null || identity.IsPuppet || identity.Entity is not DEN_Teeth entity || !entity.isActiveAndEnabled) continue;
                var chase = GetChase(entity);
                if (m_holds.TryGetValue(identity.Handle, out var hold)) {
                    if (IsHoldOver(now, hold.Since, IsRemoteGone(hold.Victim))) {
                        m_releaseScratch.Add(identity.Handle);
                        Release(entity, chase);
                    }
                    else
                        Freeze(entity, chase);
                    continue;
                }
                if (chase != null && ShouldHuntAgain(s_hasKilled(chase), hostDead, anyoneAlive))
                    Release(entity, chase);
                if (chase == null || !s_hasKilled(chase))
                    entity.target = WKTTargets.TransformFor(entity);
            }
            foreach (var handle in m_releaseScratch)
                m_holds.Remove(handle);
            m_releaseScratch.Clear();
        }

        /// <summary>Host only: a client's player is being killed by this Teeth on their screen.</summary>
        public void OnClaim(WKTPlayer sender, WKTNetIdentity identity) {
            if (identity.Entity is not DEN_Teeth entity || identity.IsPuppet || m_holds.ContainsKey(identity.Handle)) return;
            m_holds[identity.Handle] = (sender.Id, Time.time);
            Freeze(entity, GetChase(entity));
            if (entity.anim != null) {
                if (WKTPlayerProxy.TryGet(sender.Id, out var proxy) && proxy!.Head != null) {
                    var head = proxy.Head.transform;
                    entity.anim.SetBool(s_facingId, Vector3.Dot((entity.transform.position - head.position).normalized, head.forward) > 0f);
                }
                entity.anim.SetTrigger(s_killTriggerId);
            }
            m_sync.Sounds.PlayAndSendExcept(entity.clipHandler, c_kill_sound, sender);
            WKT.Instance.Logger.LogInfo($"Teeth is killing player {sender.Id}.");
        }

        /// <summary>
        /// Client only: a bite from the host's Teeth would kill the local player (<c>ENT_Player.Damage</c> leaves that to the
        /// Teeth). Plays the game's kill against this machine's copy of the nearest Teeth and tells the host.
        /// </summary>
        public void OnLethalBite(ENT_Player player, List<WKTNetIdentity> teeth) {
            if (IsDying || player.dead) return;
            WKTNetIdentity? nearest = null;
            var nearestSqr = float.PositiveInfinity;
            foreach (var identity in teeth) {
                if (identity == null || identity.Handle == 0 || identity.Entity is not DEN_Teeth entity || !entity.isActiveAndEnabled || entity.cameraTakeover == null) continue;
                var sqr = (entity.transform.position - player.transform.position).sqrMagnitude;
                if (sqr >= nearestSqr) continue;
                nearestSqr = sqr;
                nearest = identity;
            }
            if (nearest == null) {
                player.Kill(c_damage_type);
                return;
            }
            m_dyingUntil = Time.time + KILL_DURATION + HOLD_MARGIN;
            m_sync.EnemyHits.SendHit(nearest.Handle, 0, WKTPlayerHitKind.Grabbed);
            // Run by the player, who outlives a Teeth that despawns during it.
            player.StartCoroutine(KillLocal((DEN_Teeth)nearest.Entity!, player));
        }

        /// <summary><c>AIC_Teeth_Chase.KillPlayerAnimation</c>, on the local player, by this machine's copy of the Teeth.</summary>
        private IEnumerator KillLocal(DEN_Teeth teeth, ENT_Player player) {
            try {
                WKTEntitySounds.PlayLocal(teeth.clipHandler, c_kill_sound);
                var camera = player.cam;
                var cameraTransform = camera.transform;
                if (teeth.anim != null) {
                    teeth.anim.SetBool(s_facingId, Vector3.Dot((teeth.transform.position - cameraTransform.position).normalized, cameraTransform.forward) > 0f);
                    teeth.anim.SetTrigger(s_killTriggerId);
                }
                // The game's own DropEverything(), with its defaults.
                s_dropEverything(player, true, 5f);
                player.Lock();
                if (teeth.screenHandEffect != null) {
                    var emission = teeth.screenHandEffect.emission;
                    emission.rateOverTimeMultiplier = c_hand_emission;
                }
                var startPosition = cameraTransform.position;
                var startRotation = cameraTransform.rotation;
                for (var progress = 0f; progress < 1f;) {
                    if (teeth == null || player.dead) yield break;
                    player.Lock();
                    progress += Time.deltaTime / c_camera_move_seconds;
                    cameraTransform.position = Vector3.Lerp(startPosition, teeth.cameraTakeover.position, progress);
                    cameraTransform.rotation = Quaternion.Lerp(startRotation, teeth.cameraTakeover.rotation, progress);
                    yield return null;
                }
                CL_CameraControl.Shake(0.1f);
                for (var held = 0f; held < c_camera_hold_seconds; held += Time.deltaTime) {
                    if (teeth == null || player.dead) yield break;
                    player.Lock();
                    cameraTransform.position = teeth.cameraTakeover.position;
                    cameraTransform.rotation = teeth.cameraTakeover.rotation;
                    yield return null;
                }
                WKTEntitySounds.PlayLocal(teeth.clipHandler, c_chomp_sound);
                player.Kill(c_damage_type);
            }
            finally {
                m_dyingUntil = 0f;
                // The bite was a killing one, even if the Teeth went away during it.
                if (player != null && !player.dead)
                    player.Kill(c_damage_type);
            }
        }

        /// <summary>Holds a Teeth still with its kill under way, as the game does while it kills its own player.</summary>
        private static void Freeze(DEN_Teeth teeth, AIC_Teeth_Chase? chase) {
            if (chase != null)
                s_hasKilled(chase) = true;
            s_locked(teeth) = true;
            s_speedMult(teeth) = 0f;
        }

        /// <summary>Undoes the kill's hold: the Teeth hunts again, from the speed its chase starts at.</summary>
        private static void Release(DEN_Teeth teeth, AIC_Teeth_Chase? chase) {
            if (chase != null)
                s_hasKilled(chase) = false;
            s_locked(teeth) = false;
            s_charging(teeth) = false;
            s_speedMult(teeth) = 1f;
        }

        private static AIC_Teeth_Chase? GetChase(DEN_Teeth teeth) {
            var states = s_states(teeth);
            if (states == null) return null;
            foreach (var state in states) {
                if (state is AIC_Teeth_Chase chase)
                    return chase;
            }
            return null;
        }

        private static bool AnyRemoteAlive() {
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                if (!WKTPlayerPositions.IsRemoteDead(i))
                    return true;
            }
            return false;
        }

        /// <summary>Whether a remote player died or is no longer in the run.</summary>
        private static bool IsRemoteGone(byte player) {
            if (!WKTPlayerProxy.TryGet(player, out var proxy) || proxy!.Remote == null) return true;
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                if (WKTPlayerPositions.GetRemoteId(i) == player)
                    return WKTPlayerPositions.IsRemoteDead(i);
            }
            return true;
        }
    }
}
