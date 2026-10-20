using System;
using System.Collections;
using System.Collections.Generic;
using DarkMachine.Collections;
using HarmonyLib;
using Steamworks.Data;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using Random = UnityEngine.Random;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Creatures that act on whoever looks at or comes near them, shown to each player by their own machine in a synced co-op
    /// run:
    /// <list type="bullet">
    /// <item><b>Remains</b> freeze while any living player has them on screen; clients report theirs. Each player's own machine
    /// builds their dread (danger effect, proximity sound) and, when it fills, runs the neck snap on them; the host then holds
    /// the Remains behind that player's head for everyone. The host's own Remains code only reads the host's player.</item>
    /// <item><b>Hunter and Face</b> overlays follow each player's own distance from them.</item>
    /// <item><b>Trackers</b>' unease builds on each player who lingers near one.</item>
    /// <item><b>Phages</b> no longer simulated here let go of the watched feeling they left on this player.</item>
    /// <item><b>Teeth</b> fear every player's lights: clients report how much theirs frighten each Teeth near them. Whom they
    /// hunt and their kills of remote players are <see cref="WKTTeethKills"/>'.</item>
    /// </list>
    /// The engraved door and pseudopod run for each player on their own machine (see <see cref="PuppetTick_Patch"/>). Owned by
    /// <see cref="WKTWorldSync"/>.
    /// </summary>
    internal sealed class WKTViewerCreatures {
        /// <summary>How long the host holds a Remains for a remote player's neck snap before letting go anyway.</summary>
        public const double REMOTE_KILL_TIMEOUT = 6.0;
        /// <summary>How often a client reports its lights' fear at each Teeth, per second.</summary>
        public const float LIGHT_FEAR_RATE = 5f;
        /// <summary>A light fear report older than this no longer counts.</summary>
        public const double LIGHT_FEAR_EXPIRY = 0.6;
        /// <summary>Teeth further than this are not checked for this machine's lights.</summary>
        private const float c_light_fear_range = 60f;
        private const string c_murder_activate = "denizen:murder-activate";
        private const string c_murder_start = "denizen:murder-start";
        private const string c_murder = "denizen:murder";
        private const string c_fade = "fade";
        private const string c_wet = "wet";
        private const string c_kill_type = "remains";
        /// <summary>The animator state <c>NeckSnap</c> plays.</summary>
        private const string c_neck_snap_state = "Float_01";
        /// <summary>The neck snap's pauses between its sounds, as the game waits.</summary>
        private static readonly WaitForSeconds s_neckSnapPause = new(0.4f);
        private static readonly AccessTools.FieldRef<DEN_Remains_Active, bool> s_isKillingPlayer = AccessTools.FieldRefAccess<DEN_Remains_Active, bool>("isKillingPlayer");
        private static readonly AccessTools.FieldRef<DEN_Phage, bool> s_phageCanSee = AccessTools.FieldRefAccess<DEN_Phage, bool>("canSeeTarget");
        private static readonly AccessTools.FieldRef<DEN_Phage, bool> s_phageCanRemember = AccessTools.FieldRefAccess<DEN_Phage, bool>("canRememberTarget");
        private static readonly AccessTools.FieldRef<DEN_Face, List<Transform>> s_faceSegments = AccessTools.FieldRefAccess<DEN_Face, List<Transform>>("segments");
        private static readonly Action<float> s_hunterScreenEffect = AccessTools.MethodDelegate<Action<float>>(AccessTools.Method(typeof(DEN_Hunter), "SetScreenEffect"));
        private static readonly Action<float> s_faceScreenEffect = AccessTools.MethodDelegate<Action<float>>(AccessTools.Method(typeof(DEN_Face), "SetScreenEffect"));
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        private readonly List<WKTNetIdentity> m_remains = new();
        private readonly List<WKTNetIdentity> m_phages = new();
        private readonly List<WKTNetIdentity> m_teeth = new();
        private readonly List<DEN_Tracker> m_trackers = new();
        /// <summary>Host only: who sees each Remains, other than the host.</summary>
        private readonly WKTSeerSet m_seers = new();
        /// <summary>Client only: the Remains this client last said it sees.</summary>
        private readonly HashSet<ushort> m_seen = new();
        /// <summary>Client only: each Remains puppet's dread for this player.</summary>
        private readonly Dictionary<ushort, WKTRemainsHazard> m_hazards = new();
        /// <summary>Host only: Remains held behind a remote player's head for their neck snap: the player and since when.</summary>
        private readonly Dictionary<ushort, (byte Victim, double Since)> m_remoteKills = new();
        /// <summary>Host only: the newest light fear each client reported at each Teeth, and when.</summary>
        private readonly Dictionary<(ushort Teeth, byte Player), (float Rating, double At)> m_lightFear = new();
        /// <summary>Client only: the light fear last reported at each Teeth.</summary>
        private readonly Dictionary<ushort, float> m_reportedFear = new();
        /// <summary>This player's own unease at each Tracker: how long it has lingered near it, and the effect's blend.</summary>
        private readonly Dictionary<int, (float Fulfilled, float Blend)> m_trackerUnease = new();
        private readonly List<ushort> m_handleScratch = new();
        private readonly List<(ushort, byte)> m_fearScratch = new();
        private double m_nextFearReport;

        public WKTViewerCreatures(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
            TeethKills = new WKTTeethKills(sync);
        }

        /// <summary>Who the Teeth hunts and its kills of remote players.</summary>
        public WKTTeethKills TeethKills { get; }

        #region Registration

        public void Register(WKTNetIdentity identity) {
            switch (identity.Entity) {
                case DEN_Remains_Active:
                    m_remains.Add(identity);
                    break;
                case DEN_Phage:
                    m_phages.Add(identity);
                    break;
                case DEN_Teeth:
                    m_teeth.Add(identity);
                    break;
                case DEN_Tracker tracker:
                    m_trackers.Add(tracker);
                    break;
            }
        }

        public void Forget(WKTNetIdentity identity) {
            m_remains.Remove(identity);
            m_phages.Remove(identity);
            m_teeth.Remove(identity);
            if (identity.Entity is DEN_Tracker tracker) {
                m_trackers.Remove(tracker);
                m_trackerUnease.Remove(tracker.GetInstanceID());
            }
            if (identity.Handle == 0) return;
            m_seers.Forget(identity.Handle);
            m_seen.Remove(identity.Handle);
            m_hazards.Remove(identity.Handle);
            m_remoteKills.Remove(identity.Handle);
            m_reportedFear.Remove(identity.Handle);
        }

        public void OnPlayerLeft(byte player) {
            m_seers.RemovePlayer(player);
            foreach (var pair in m_remoteKills) {
                if (pair.Value.Victim == player)
                    m_handleScratch.Add(pair.Key);
            }
            ReleaseRemoteKills();
        }

        public void Clear() {
            m_remains.Clear();
            m_phages.Clear();
            m_teeth.Clear();
            m_trackers.Clear();
            m_seers.Clear();
            TeethKills.Clear();
            m_seen.Clear();
            m_hazards.Clear();
            m_remoteKills.Clear();
            m_lightFear.Clear();
            m_reportedFear.Clear();
            m_trackerUnease.Clear();
        }

        #endregion

        public void Update(double now) {
            var local = ENT_Player.playerObject;
            var alive = local != null && !local.dead;
            ShowOverlays(local, alive);
            ShowTrackers(local, alive);
            UpdateRemains(local, alive, now);
            ReleasePhages();
            if (m_sync.IsHost) {
                ForgetDeadSeers();
                ExpireLightFear(now);
                TeethKills.UpdateHost(m_teeth);
            }
            else if (now >= m_nextFearReport) {
                m_nextFearReport = now + 1.0 / LIGHT_FEAR_RATE;
                ReportLightFear(alive);
            }
        }

        #region Overlays

        /// <summary>
        /// Replaces the Hunter's and Face's <c>SetScreenEffect</c> calls from their AI, which judge the distance to the player they
        /// chase. In a synced co-op run each machine judges its own player's (see <see cref="ShowOverlays"/>).
        /// </summary>
        public static void SetHunterScreenEffectFromAI(float amount) {
            if (WKTWorldSync.Instance == null)
                s_hunterScreenEffect(amount);
        }

        public static void SetFaceScreenEffectFromAI(float amount) {
            if (WKTWorldSync.Instance == null)
                s_faceScreenEffect(amount);
        }

        /// <summary>
        /// The overlays the Hunter's <c>Movement</c> and the Face's <c>TargetCollisionCheck</c> set, from this player's own distance.
        /// Their <c>LateUpdate</c>, which draws and then clears it, runs on every machine.
        /// </summary>
        private static void ShowOverlays(ENT_Player? local, bool alive) {
            if (!alive) return;
            var position = local!.transform.position;
            foreach (var hunter in DEN_Hunter.activeHunters) {
                if (hunter == null || !hunter.isActiveAndEnabled || hunter.dead) continue;
                var distance = Vector3.Distance(position, hunter.transform.position);
                if (distance < hunter.screenEffectDistance)
                    s_hunterScreenEffect(1f - distance / hunter.screenEffectDistance);
            }
            if (DEN_Face.faceInstances == null) return;
            foreach (var face in DEN_Face.faceInstances) {
                if (face == null || !face.isActiveAndEnabled) continue;
                var closest = float.PositiveInfinity;
                var segments = s_faceSegments(face);
                if (segments != null) {
                    foreach (var segment in segments) {
                        if (segment != null)
                            closest = Mathf.Min(closest, Vector3.Distance(segment.position, position));
                    }
                }
                closest = Mathf.Min(closest, Vector3.Distance(face.transform.position, position));
                if (closest < face.screenEffectDistance)
                    s_faceScreenEffect(1f - closest / face.screenEffectDistance);
            }
        }

        #endregion

        #region Trackers

        /// <summary>Replaces a Tracker's own unease effect, which follows the player it tracks; each machine shows its own player's.</summary>
        public static void EnqueueTrackerEffectFromAI(PriorityQueue<FXManager.FXZoneStore, byte> queue, FXManager.FXZoneStore zone, byte priority) {
            if (WKTWorldSync.Instance == null)
                queue.Enqueue(zone, priority);
        }

        /// <summary><c>DEN_Tracker.UpdateFulfilled</c>'s unease, for this player: it builds while they stay near the Tracker.</summary>
        private void ShowTrackers(ENT_Player? local, bool alive) {
            var deltaTime = Time.deltaTime;
            foreach (var tracker in m_trackers) {
                if (tracker == null || !tracker.isActiveAndEnabled) continue;
                var id = tracker.GetInstanceID();
                var (fulfilled, blend) = m_trackerUnease.TryGetValue(id, out var unease) ? unease : (tracker.fulfilledTimer, 0f);
                blend = Mathf.Clamp01(blend - tracker.proximityEffectBlendFadeRate * deltaTime);
                if (alive && Vector3.Distance(local!.transform.position, tracker.transform.position) <= tracker.fulfilledDistance) {
                    fulfilled -= deltaTime;
                    blend = Mathf.Max(Mathf.Clamp01(1f - fulfilled / (tracker.fulfilledTimer - tracker.fulfilledTimerVariance)), blend);
                }
                else
                    fulfilled = tracker.fulfilledTimer + Random.Range(-tracker.fulfilledTimerVariance, tracker.fulfilledTimerVariance);
                m_trackerUnease[id] = (fulfilled, blend);
                if (blend > 0f && tracker.proximityEffect != null)
                    FXManager.fxZones.Enqueue(new FXManager.FXZoneStore { blend = blend, priority = 15, data = tracker.proximityEffect }, 15);
            }
        }

        #endregion

        #region Remains

        /// <summary>
        /// Replaces <c>meshRenderer.isVisible</c> in <c>DEN_Remains_Active.Update</c>: on the host, the Remains is seen while the
        /// host's living player or any client's sees it. Everywhere else, the renderer's own answer.
        /// </summary>
        public static bool IsSeenByAnyone(Renderer renderer) {
            var viewers = WKTWorldSync.Instance?.Viewers;
            if (viewers == null || !viewers.m_sync.IsHost) return renderer.isVisible;
            var local = ENT_Player.playerObject;
            if (renderer.isVisible && local != null && !local.dead) return true;
            if (!renderer.TryGetComponent<DEN_Remains_Active>(out var remains) && (remains = renderer.GetComponentInParent<DEN_Remains_Active>()) == null) return false;
            return remains.TryGetComponent<WKTNetIdentity>(out var identity) && viewers.m_seers.IsSeen(identity.Handle);
        }

        /// <summary>Host only: whether a Remains is held for a remote player's neck snap, so its own code must not move it.</summary>
        public static bool IsHeldForKill(DEN_Remains_Active remains) {
            var viewers = WKTWorldSync.Instance?.Viewers;
            return viewers != null && viewers.m_remoteKills.Count != 0 && remains.TryGetComponent<WKTNetIdentity>(out var identity) &&
                viewers.m_remoteKills.ContainsKey(identity.Handle);
        }

        private void UpdateRemains(ENT_Player? local, bool alive, double now) {
            if (m_sync.IsHost) {
                HoldRemoteKills(now);
                // The host's own neck snap leaves its Remains stuck killing; once the host is dead it goes on for the living.
                if (alive) return;
                foreach (var identity in m_remains) {
                    if (identity != null && identity.Entity is DEN_Remains_Active remains && s_isKillingPlayer(remains) && !m_remoteKills.ContainsKey(identity.Handle))
                        s_isKillingPlayer(remains) = false;
                }
                return;
            }
            foreach (var identity in m_remains) {
                if (identity == null || identity.Handle == 0 || !identity.IsPuppet || identity.Entity is not DEN_Remains_Active remains || !remains.isActiveAndEnabled) continue;
                ReportSeen(identity.Handle, alive && remains.active && remains.meshRenderer != null && remains.meshRenderer.isVisible);
                if (remains.active)
                    Dread(identity, remains, local, alive);
            }
        }

        private void ReportSeen(ushort handle, bool seen) {
            if (seen == m_seen.Contains(handle)) return;
            if (seen)
                m_seen.Add(handle);
            else
                m_seen.Remove(handle);
            var writer = m_network.BeginMessage(WKTMessageId.RemainsSeen);
            writer.Put(handle);
            writer.Put(seen);
            m_network.SendToHost(writer, SendType.Reliable);
        }

        /// <summary><c>DEN_Remains_Active.Update</c>'s dread for this player, on a puppet.</summary>
        private void Dread(WKTNetIdentity identity, DEN_Remains_Active remains, ENT_Player? local, bool alive) {
            var deltaTime = Time.deltaTime;
            var hazard = m_hazards.GetValueOrDefault(identity.Handle);
            var killing = s_isKillingPlayer(remains);
            if (killing)
                FXManager.fxZones.Enqueue(new FXManager.FXZoneStore { blend = remains.proximityBlend, priority = 30, data = remains.killFX }, 30);
            else {
                var distance = alive ? Vector3.Distance(remains.transform.position, local!.transform.position) : float.PositiveInfinity;
                var noise = Mathf.PerlinNoise1D(Time.time * remains.proximityPerlinSpeed);
                if (hazard.Step(distance, remains.proximityRadius, remains.proximityHazardBuildRate, noise, deltaTime) && alive && !local!.IsLocked() && !local.GetGodmode())
                    BeginNeckSnap(identity, remains, local);
                remains.proximityBlend = hazard.Blend;
                remains.proximityKillTime = hazard.KillTime;
                if (remains.proximityLoop != null)
                    remains.proximityLoop.volume = hazard.LoopVolume;
            }
            m_hazards[identity.Handle] = hazard;
            FXManager.fxZones.Enqueue(new FXManager.FXZoneStore { blend = remains.proximityBlend, priority = 15, data = remains.dangerFX }, 15);
        }

        /// <summary>
        /// The neck snap from <c>DEN_Remains_Active.Update</c> on this player. The Remains is held here, behind the player's head,
        /// and the host is told, so it holds it there for everyone.
        /// </summary>
        private void BeginNeckSnap(WKTNetIdentity identity, DEN_Remains_Active remains, ENT_Player local) {
            local.Lock();
            s_isKillingPlayer(remains) = true;
            identity.SelfDriven = true;
            m_sync.EnemyHits.SendHit(identity.Handle, 0, WKTPlayerHitKind.Grabbed);
            remains.StartCoroutine(NeckSnap(identity, remains, local));
        }

        private IEnumerator NeckSnap(WKTNetIdentity identity, DEN_Remains_Active remains, ENT_Player local) {
            Play(remains, c_murder_activate);
            WKTLocalReplayScope.Enter();
            try {
                remains.onStartKill?.Invoke();
            }
            finally {
                WKTLocalReplayScope.Exit();
            }
            if (remains.animator != null)
                remains.animator.Play(c_neck_snap_state);
            CL_UIManager.instance?.QuickFlashVignette(c_fade);
            CL_CameraControl.Shake(0.5f);
            remains.proximityBlend = 1f;
            local.AddEffect(c_wet, 1f);
            var camera = local.cam.transform;
            HoldBehind(remains.transform, camera);
            yield return s_neckSnapPause;
            Play(remains, c_murder_start);
            CL_CameraControl.Shake(0.15f);
            yield return s_neckSnapPause;
            Play(remains, c_murder);
            CL_CameraControl.Shake(0.15f);
            // DOLocalRotate(neckSnapRotation, neckSnapDuration) eased by neckSnapCurve.
            var start = camera.localRotation;
            var end = Quaternion.Euler(remains.neckSnapRotation);
            for (var time = 0f; time < remains.neckSnapDuration; time += Time.deltaTime) {
                var eased = remains.neckSnapCurve != null ? remains.neckSnapCurve.Evaluate(time / remains.neckSnapDuration) : time / remains.neckSnapDuration;
                camera.localRotation = Quaternion.LerpUnclamped(start, end, eased);
                yield return null;
            }
            camera.localRotation = end;
            local.Kill(c_kill_type);
            CL_UIManager.instance?.SetVignette(c_fade, 1f);
            // The puppet follows the host again, which lets the Remains go on for the living.
            s_isKillingPlayer(remains) = false;
            if (identity == null) yield break;
            identity.SelfDriven = WKTAmbientMotion.IsSelfDriven(identity.gameObject);
            m_hazards.Remove(identity.Handle);
        }

        private static void Play(DEN_Remains_Active remains, string sound) => WKTEntitySounds.PlayLocal(remains.clipHandler, sound);

        /// <summary>Where the neck snap puts the Remains: just behind and below the victim's eyes, facing them.</summary>
        private static void HoldBehind(Transform remains, Transform head) {
            var position = head.position - head.forward * 0.5f;
            var rotation = Quaternion.LookRotation(head.position - position);
            remains.SetPositionAndRotation(position - rotation * Vector3.up, rotation);
        }

        /// <summary>Host only: a client's player is having their neck snapped by a Remains on their screen.</summary>
        public void OnRemainsClaim(WKTPlayer sender, WKTNetIdentity identity) {
            if (identity.Entity is not DEN_Remains_Active remains || identity.IsPuppet || m_remoteKills.ContainsKey(identity.Handle)) return;
            m_remoteKills[identity.Handle] = (sender.Id, Time.realtimeSinceStartupAsDouble);
            if (remains.animator != null)
                remains.animator.Play(c_neck_snap_state);
            m_sync.Sounds.PlayAndSendExcept(remains.clipHandler, c_murder_activate, sender);
        }

        /// <summary>Host only: holds each Remains behind its remote victim's head until they die, leave or it times out.</summary>
        private void HoldRemoteKills(double now) {
            if (m_remoteKills.Count == 0) return;
            foreach (var pair in m_remoteKills) {
                var (victim, since) = pair.Value;
                if (now - since > REMOTE_KILL_TIMEOUT || !m_sync.TryGetIdentity(pair.Key, out var identity) || identity!.Entity == null ||
                    !WKTPlayerProxy.TryGet(victim, out var proxy) || proxy!.Remote == null || IsRemoteDead(victim)) {
                    m_handleScratch.Add(pair.Key);
                    continue;
                }
                HoldBehind(identity.Entity.transform, proxy.Head.transform);
            }
            ReleaseRemoteKills();
        }

        private void ReleaseRemoteKills() {
            foreach (var handle in m_handleScratch)
                m_remoteKills.Remove(handle);
            m_handleScratch.Clear();
        }

        private static bool IsRemoteDead(byte player) {
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                if (WKTPlayerPositions.GetRemoteId(i) == player)
                    return WKTPlayerPositions.IsRemoteDead(i);
            }
            return true;
        }

        /// <summary>Host only: a client's player started or stopped seeing a Remains.</summary>
        public void OnRemainsSeen(WKTPlayer sender, NetDataReader reader) {
            if (!m_sync.IsHost || sender.IsHost || !reader.TryGetUShort(out var handle) || !reader.TryGetBool(out var seen)) return;
            if (seen && (IsRemoteDead(sender.Id) || !m_sync.TryGetIdentity(handle, out var identity) || identity!.Entity is not DEN_Remains_Active)) return;
            m_seers.Set(handle, sender.Id, seen);
        }

        /// <summary>Host only: dead players see nothing.</summary>
        private void ForgetDeadSeers() {
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                if (WKTPlayerPositions.IsRemoteDead(i))
                    m_seers.RemovePlayer(WKTPlayerPositions.GetRemoteId(i));
            }
        }

        #endregion

        #region Phages

        /// <summary>
        /// A Phage's watched feeling (<c>DEN_Phage.LookCheck</c>) only runs where it is simulated. Where it is a puppet, it fades
        /// as if the Phage could no longer see this player, so a feeling left over from before a handoff wears off.
        /// </summary>
        private void ReleasePhages() {
            foreach (var identity in m_phages) {
                if (identity == null || !identity.IsPuppet || identity.Entity is not DEN_Phage phage || !phage.isActiveAndEnabled) continue;
                s_phageCanSee(phage) = false;
                s_phageCanRemember(phage) = false;
                phage.LookCheck();
            }
        }

        #endregion

        #region Teeth

        /// <summary>Client only: a bite from the host's Teeth would kill the local player, which plays the Teeth's kill here.</summary>
        public void OnLethalTeethBite(ENT_Player player) => TeethKills.OnLethalBite(player, m_teeth);

        /// <summary>Client only: tells the host how much this machine's lights frighten each Teeth near this player.</summary>
        private void ReportLightFear(bool alive) {
            var local = ENT_Player.playerObject;
            foreach (var identity in m_teeth) {
                if (identity == null || identity.Handle == 0 || !identity.IsPuppet || identity.Entity is not DEN_Teeth teeth || teeth.geoRoot == null) continue;
                var near = alive && Vector3.Distance(teeth.transform.position, local!.transform.position) < c_light_fear_range;
                var rating = near ? CL_Lamp.GetHighestEffectorFearRating(teeth.geoRoot.position) : 0f;
                var reported = m_reportedFear.GetValueOrDefault(identity.Handle);
                if (rating <= 0f && reported <= 0f) continue;
                m_reportedFear[identity.Handle] = rating;
                var writer = m_network.BeginMessage(WKTMessageId.TeethLightFear);
                writer.Put(identity.Handle);
                writer.Put(rating);
                m_network.SendToHost(writer, SendType.Unreliable);
            }
        }

        /// <summary>Host only: a client's lights at a Teeth.</summary>
        public void OnTeethLightFear(WKTPlayer sender, NetDataReader reader) {
            if (!m_sync.IsHost || sender.IsHost || !reader.TryGetUShort(out var handle) || !reader.TryGetFloat(out var rating) || !float.IsFinite(rating)) return;
            if (rating <= 0f) {
                m_lightFear.Remove((handle, sender.Id));
                return;
            }
            m_lightFear[(handle, sender.Id)] = (Mathf.Min(rating, 100f), Time.realtimeSinceStartupAsDouble);
        }

        private void ExpireLightFear(double now) {
            if (m_lightFear.Count == 0) return;
            foreach (var pair in m_lightFear) {
                if (now - pair.Value.At > LIGHT_FEAR_EXPIRY)
                    m_fearScratch.Add(pair.Key);
            }
            foreach (var key in m_fearScratch)
                m_lightFear.Remove(key);
            m_fearScratch.Clear();
        }

        /// <summary>
        /// Replaces <c>CL_Lamp.GetHighestEffectorFearRating(teeth.geoRoot.position)</c> in <c>AIC_Teeth_Chase</c>: the host's own
        /// lights, or any client's at that Teeth, whichever frightens it most.
        /// </summary>
        public static float GetFearRating(Vector3 position) {
            var rating = CL_Lamp.GetHighestEffectorFearRating(position);
            var viewers = WKTWorldSync.Instance?.Viewers;
            if (viewers == null || viewers.m_lightFear.Count == 0) return rating;
            foreach (var pair in viewers.m_lightFear) {
                if (pair.Value.Rating <= rating || !viewers.m_sync.TryGetIdentity(pair.Key.Teeth, out var identity) || identity!.Entity is not DEN_Teeth teeth ||
                    teeth.geoRoot == null || (teeth.geoRoot.position - position).sqrMagnitude > 0.01f) continue;
                rating = pair.Value.Rating;
            }
            return rating;
        }

        #endregion
    }
}
