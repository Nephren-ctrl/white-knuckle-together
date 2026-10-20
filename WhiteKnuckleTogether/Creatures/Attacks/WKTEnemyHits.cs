using System;
using System.Collections.Generic;
using HarmonyLib;
using Steamworks.Data;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using AttackClass = DenizenAttackModule_Basic.AttackClass;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Enemy attacks on players in a synced co-op run, decided on the victim's own screen. The machine simulating an enemy
    /// (see <see cref="WKTAuthoritySync"/>) says when its attack strikes or is underway, a client's through the host; every
    /// other machine then runs that attack's hit check on its puppet against its own player only, timed to the pose its
    /// puppet shows, applies the hit itself and tells the simulating machine through the host. The host never applies these
    /// enemies' damage to remote players. Kills stay the host's to decide.
    /// <para>
    /// Covered, per enemy type, from the game's own code:
    /// <list type="bullet">
    /// <item>The basic attack module (BasicDenizen family, BasicNavigator family, Crawler, Phage): melee hits replay their
    /// hit check; charges and leaps are tested for contact while the host's charge is underway.</item>
    /// <item>Drones, bloodbugs and basic fliers: their charge or melee contact, tested the same way.</item>
    /// <item>Hunters: their body-segment check runs every frame.</item>
    /// <item>Barnacles (see <see cref="WKTTongueSync"/>) and enemy projectiles (each client's copy hits its own player).</item>
    /// </list>
    /// Other enemies keep the host's hits until they are covered.
    /// </para>
    /// Owned by <see cref="WKTWorldSync"/>.
    /// </summary>
    internal sealed class WKTEnemyHits {
        /// <summary>How far beyond its own collider a charging puppet still hits the local player.</summary>
        private const float c_contact_reach = 0.25f;
        /// <summary>Marks a contact attack from <see cref="s_contactAttacks"/> rather than an attack module's attack index.</summary>
        private const int c_contact_attack = -1;
        private static readonly AccessTools.FieldRef<AttackClass, DenizenAttackModule_Basic> s_attackOwner =
            AccessTools.FieldRefAccess<AttackClass, DenizenAttackModule_Basic>("attackModule");
        private static readonly AccessTools.FieldRef<DenizenAttackModule, Denizen> s_moduleDenizen = AccessTools.FieldRefAccess<DenizenAttackModule, Denizen>("denizen");
        private static readonly AccessTools.FieldRef<DenizenAttackModule_Basic, bool> s_attacking = AccessTools.FieldRefAccess<DenizenAttackModule_Basic, bool>("attacking");
        private static readonly AccessTools.FieldRef<DenizenAttackModule_Basic, bool> s_hasHit = AccessTools.FieldRefAccess<DenizenAttackModule_Basic, bool>("hasHit");
        private static readonly AccessTools.FieldRef<AttackClass, bool> s_isAttacking = AccessTools.FieldRefAccess<AttackClass, bool>("isAttacking");
        private static readonly AccessTools.FieldRef<DenizenAttackModule_Basic, AttackClass> s_currentAttack =
            AccessTools.FieldRefAccess<DenizenAttackModule_Basic, AttackClass>("currentAttack");
        private static readonly AccessTools.FieldRef<DEN_BasicFlier, bool> s_flierAttacking = AccessTools.FieldRefAccess<DEN_BasicFlier, bool>("attacking");
        private static readonly AccessTools.FieldRef<DEN_BasicFlier, bool> s_flierHasHit = AccessTools.FieldRefAccess<DEN_BasicFlier, bool>("hasHit");
        private static readonly Action<DEN_Hunter> s_hunterCollisionCheck =
            AccessTools.MethodDelegate<Action<DEN_Hunter>>(AccessTools.Method(typeof(DEN_Hunter), "TargetCollisionCheck"));
        /// <summary>Enemies whose attack lands by touching the player, by type: when it is underway, how it hits, and what follows on the host.</summary>
        private static readonly Dictionary<Type, ContactAttack> s_contactAttacks = new() {
            { typeof(DEN_Drone), new ContactAttack(e => ((DEN_Drone)e).moveState == DEN_Drone.MovementState.charging, ApplyDroneHit, e => ((DEN_Drone)e).moveState = DEN_Drone.MovementState.flying) },
            { typeof(DEN_Bloodbug), new ContactAttack(e => ((DEN_Bloodbug)e).moveState == DEN_Bloodbug.MovementState.charging, ApplyBloodbugHit,
                e => ((DEN_Bloodbug)e).moveState = DEN_Bloodbug.MovementState.flying) },
            { typeof(DEN_BasicFlier), new ContactAttack(IsFlierMeleeUnderway, ApplyFlierHit, e => {
                s_flierAttacking((DEN_BasicFlier)e) = false;
                s_flierHasHit((DEN_BasicFlier)e) = true;
            }) }
        };
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        private readonly WKTDueQueue<(ushort Handle, byte Attack)> m_attacks = new();
        private readonly List<(ushort Handle, byte Attack)> m_dueScratch = new();
        /// <summary>Client only: puppets whose contact attack is underway, by handle: an attack module's attack index or <see cref="c_contact_attack"/>.</summary>
        private readonly Dictionary<ushort, int> m_contacts = new();
        /// <summary>Client only: contact attacks that already hit the local player this time.</summary>
        private readonly HashSet<ushort> m_contactHits = new();
        /// <summary>Client only: hunter puppets, whose body-segment check runs every frame.</summary>
        private readonly HashSet<ushort> m_hunters = new();
        private readonly List<ushort> m_handleScratch = new();
        private readonly Collider[] m_overlap = new Collider[16];

        public WKTEnemyHits(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
        }

        #region Simulating machine

        /// <summary>
        /// An enemy this machine simulates struck with a melee attack, so every other machine checks it against its own player.
        /// A client's goes through the host. A strike at another creature (a held bloodbug biting an enemy) is not sent, so
        /// no player standing near it is hit by it.
        /// </summary>
        public void OnLocalMeleeHitCheck(AttackClass attack) {
            if (WKTPuppetHitScope.IsOpen) return;
            var module = s_attackOwner(attack);
            var denizen = module != null ? s_moduleDenizen(module) : null;
            if (denizen == null || WKTWorldSync.IsPuppet(denizen) || !denizen.TryGetComponent<WKTNetIdentity>(out var identity) || identity.Handle == 0) return;
            if (!IsAimedAtPlayers(denizen.GetTargetEntity())) return;
            var index = module!.attacks.IndexOf(attack);
            if (index < 0 || index > byte.MaxValue) return;
            var writer = m_network.BeginMessage(WKTMessageId.AttackPerformed);
            new WKTAttackEvent(identity.Handle, (byte)index).Write(writer);
            if (m_sync.IsHost)
                m_network.Broadcast(writer, SendType.Reliable);
            else
                m_network.SendToHost(writer, SendType.Reliable);
        }

        /// <summary>Whether a strike at <paramref name="target"/> may hit players: no target (a hitbox swing), a player or a stand-in.</summary>
        public static bool IsAimedAtPlayers(GameEntity? target) => target == null || target is ENT_Player or WKTPlayerProxy;

        /// <summary>
        /// The AI bits other machines need from an enemy this machine simulates. For the attack module: whether a charge is underway and with which
        /// attack (<see cref="WKTChargeBits"/>). For contact attackers: bit 0 while their attack is underway.
        /// </summary>
        public static byte GetAiBits(GameEntity? entity) {
            if (entity == null) return 0;
            if (s_contactAttacks.TryGetValue(entity.GetType(), out var contact)) return contact.IsUnderway(entity) ? (byte)1 : (byte)0;
            if (!WKTAttackModules.TryGet(entity, out var module)) return 0;
            var current = s_currentAttack(module!);
            var charging = s_attacking(module!) && !s_hasHit(module!) && current != null && current.attackType == AttackClass.AttackType.charge;
            return charging ? WKTChargeBits.Encode(true, module!.attacks.IndexOf(current!)) : (byte)0;
        }

        /// <summary>Ends an attack that threw, so its enemy can attack again.</summary>
        public static void ResetAttack(AttackClass attack) {
            s_isAttacking(attack) = false;
            var module = s_attackOwner(attack);
            if (module != null)
                s_attacking(module) = false;
        }

        /// <summary>
        /// The machine simulating <paramref name="entity"/> only: another player was hit by it on their screen. A contact attack
        /// that hit stops, as it would have here.
        /// </summary>
        public static void OnPlayerHit(GameEntity entity, in WKTPlayerHit hit) {
            if (hit.Kind != WKTPlayerHitKind.Damaged) return;
            if (s_contactAttacks.TryGetValue(entity.GetType(), out var contact)) {
                if (contact.IsUnderway(entity))
                    contact.OnHostHit(entity);
                return;
            }
            if (!WKTAttackModules.TryGet(entity, out var module)) return;
            var current = s_currentAttack(module!);
            if (current == null || current.attackType != AttackClass.AttackType.charge || module!.attacks.IndexOf(current) != hit.Part) return;
            s_attacking(module) = false;
            s_hasHit(module) = true;
            s_isAttacking(current) = false;
        }

        #endregion

        #region Puppets

        /// <summary>
        /// An enemy another machine simulates struck. The host passes a client's on to everyone else; each machine checks it
        /// against its own player.
        /// </summary>
        public void OnAttackEvent(WKTPlayer sender, NetDataReader reader) {
            if (m_sync.IsHost == sender.IsHost || !WKTAttackEvent.TryRead(reader, out var attackEvent)) return;
            if (m_sync.IsHost) {
                if (!m_sync.TryGetIdentity(attackEvent.Handle, out var identity) || identity!.Owner != sender.Id) return;
                var writer = m_network.BeginMessage(WKTMessageId.AttackPerformed);
                attackEvent.Write(writer);
                m_network.Broadcast(writer, SendType.Reliable, sender);
            }
            // The puppet shows the host's poses this far behind: the hit is checked once it reaches the pose it struck from.
            m_attacks.Add((attackEvent.Handle, attackEvent.Attack), Time.realtimeSinceStartupAsDouble + WKTSnapshotBufferBase<WKTEntityPose>.INTERPOLATION_DELAY);
        }

        /// <summary>The simulating machine's AI bits for a puppet changed.</summary>
        public void OnAiBits(ushort handle, byte bits) {
            var contact = m_sync.TryGetIdentity(handle, out var identity) && identity!.Entity != null && WKTAttackModules.IsContactAttacker(identity.Entity.GetType());
            if (contact && (bits & 1) != 0) {
                m_contacts[handle] = c_contact_attack;
                return;
            }
            if (!contact && WKTChargeBits.TryDecode(bits, out var attack)) {
                m_contacts[handle] = attack;
                return;
            }
            m_contacts.Remove(handle);
            m_contactHits.Remove(handle);
        }

        /// <summary>A puppet was bound. Hunters check their body against the local player every frame.</summary>
        public void OnPuppetBound(WKTNetIdentity identity) {
            if (identity.Entity is DEN_Hunter)
                m_hunters.Add(identity.Handle);
        }

        /// <summary>Runs the puppets' attacks that are due, and checks contact attacks and hunters against the local player.</summary>
        public void Update(double now) {
            if (m_attacks.Count > 0) {
                m_dueScratch.Clear();
                m_attacks.TakeDue(now, m_dueScratch);
                foreach (var (handle, attack) in m_dueScratch)
                    RunMelee(handle, attack);
                m_dueScratch.Clear();
            }
            var local = ENT_Player.playerObject;
            if (local == null || local.dead) return;
            if (m_contacts.Count > 0) {
                m_handleScratch.Clear();
                foreach (var pair in m_contacts) {
                    if (!m_contactHits.Contains(pair.Key))
                        m_handleScratch.Add(pair.Key);
                }
                foreach (var handle in m_handleScratch)
                    CheckContact(local, handle, m_contacts[handle]);
            }
            if (m_hunters.Count == 0) return;
            m_handleScratch.Clear();
            foreach (var handle in m_hunters)
                m_handleScratch.Add(handle);
            foreach (var handle in m_handleScratch)
                CheckHunter(local, handle);
            m_handleScratch.Clear();
        }

        /// <summary>Runs a puppet's melee hit check against the local player, as the host's enemy just did against its target.</summary>
        private void RunMelee(ushort handle, byte attackIndex) {
            var local = ENT_Player.playerObject;
            if (local == null || local.dead || !TryGetPuppet(handle, out var denizen) || !WKTAttackModules.TryGet(denizen, out var module) ||
                attackIndex >= module!.attacks.Count || module.attacks[attackIndex] == null) return;
            var health = local.health;
            var attack = module.attacks[attackIndex];
            WKTMeleeReplay.Enter(attack.meleeAttackEndEvent, attack.attackMissSound);
            try {
                RunAgainstLocal(denizen!, local, attack, static replayed => replayed.MeleeHitCheck());
            }
            finally {
                WKTMeleeReplay.Exit();
            }
            if (local.health < health)
                SendHit(handle, attackIndex);
        }

        /// <summary>A hunter puppet's body segments hurt the local player on contact, as its own check does on the host.</summary>
        private void CheckHunter(ENT_Player local, ushort handle) {
            if (!TryGetPuppet(handle, out var denizen) || denizen is not DEN_Hunter hunter) {
                if (!m_sync.TryGetIdentity(handle, out _))
                    m_hunters.Remove(handle);
                return;
            }
            RunAgainstLocal(hunter, local, hunter, static target => s_hunterCollisionCheck(target));
        }

        /// <summary>
        /// Runs part of a puppet's own attack code with its target set to the local player, inside the hit scope so nothing but
        /// the local player can be hit. Both the denizen's <c>target</c> and its target component's record are pointed at the
        /// player, since navigators (crawlers, spriders, grubs) aim their melee at <c>targetComponent.target.targetEntity</c>.
        /// </summary>
        private static void RunAgainstLocal<T>(Denizen denizen, ENT_Player local, T state, Action<T> run) {
            var previousTarget = denizen.target;
            var record = WKTEngagement.GetTargetComponent(denizen)?.target;
            var previousTransform = record?.targetTransform;
            var previousTagger = record?.targetTagger;
            var previousEntity = record?.targetEntity;
            denizen.target = local.transform;
            if (record != null) {
                record.targetTransform = local.transform;
                record.targetTagger = local.GetTagger();
                record.targetEntity = local;
            }
            WKTPuppetHitScope.Enter();
            try {
                run(state);
            }
            finally {
                WKTPuppetHitScope.Exit();
                denizen.target = previousTarget;
                if (record != null) {
                    record.targetTransform = previousTransform;
                    record.targetTagger = previousTagger;
                    record.targetEntity = previousEntity;
                }
            }
        }

        /// <summary>
        /// A puppet whose contact attack is underway hits the local player once it touches them. Kinematic puppets never collide
        /// with the player, so the overlap is tested here and the game's hit is applied the way its collision handler would.
        /// </summary>
        private void CheckContact(ENT_Player local, ushort handle, int attackIndex) {
            if (!TryGetPuppet(handle, out var denizen) || !denizen!.TryGetComponent<Collider>(out var body) || !Touches(body, local)) return;
            m_contactHits.Add(handle);
            if (attackIndex == c_contact_attack) {
                if (!s_contactAttacks.TryGetValue(denizen.GetType(), out var contact)) return;
                contact.Apply(local, denizen);
                SendHit(handle, 0);
                return;
            }
            if (!WKTAttackModules.TryGet(denizen, out var module) || attackIndex >= module!.attacks.Count || module.attacks[attackIndex] == null) return;
            var attack = module.attacks[attackIndex];
            var tagger = local.GetTagger();
            if (attack.chargeHitIgnoreTags != null && attack.chargeHitIgnoreTags.Count > 0 && tagger != null && tagger.HasTagInList(attack.chargeHitIgnoreTags.ToArray()))
                return;
            ApplyChargeHit(local, denizen, attack);
            SendHit(handle, (byte)attackIndex);
        }

        private bool Touches(Collider body, ENT_Player local) {
            var bounds = body.bounds;
            var count = Physics.OverlapSphereNonAlloc(bounds.center, bounds.extents.magnitude + c_contact_reach, m_overlap, 1 << local.gameObject.layer,
                QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++) {
                if (m_overlap[i] != null && m_overlap[i].GetComponentInParent<ENT_Player>() == local) return true;
            }
            return false;
        }

        private bool TryGetPuppet(ushort handle, out Denizen? denizen) {
            denizen = null;
            if (!m_sync.TryGetIdentity(handle, out var identity) || identity!.Entity is not Denizen found || found.dead || !WKTWorldSync.IsPuppet(found) ||
                !found.isActiveAndEnabled) return false;
            denizen = found;
            return true;
        }

        /// <summary>Tells the machine simulating the enemy that it hit the local player, a client's through the host.</summary>
        public void SendHit(ushort handle, byte part, WKTPlayerHitKind kind = WKTPlayerHitKind.Damaged) {
            var hit = new WKTPlayerHit(handle, kind, part);
            if (m_sync.IsHost) {
                if (m_sync.TryGetIdentity(handle, out var identity))
                    m_sync.ApplyPlayerHit(identity!, hit);
                return;
            }
            var writer = m_network.BeginMessage(WKTMessageId.PlayerHit);
            hit.Write(writer);
            m_network.SendToHost(writer, SendType.Reliable);
        }

        /// <summary>
        /// Whether an enemy's own attack may run here. A puppet's may not: only the simulating machine's attacks, replayed
        /// through <see cref="RunMelee"/>, hit anything here; the puppet's animation events and timers must not add more.
        /// </summary>
        public static bool AllowPuppetAttack(AttackClass attack) {
            var sync = WKTWorldSync.Instance;
            if (sync == null || WKTPuppetHitScope.IsOpen) return true;
            var module = s_attackOwner(attack);
            var denizen = module != null ? s_moduleDenizen(module) : null;
            return denizen == null || !WKTWorldSync.IsPuppet(denizen);
        }

        #endregion

        #region Game hits

        /// <summary>The game's charge hit on a player, from <c>DenizenAttackModule_Basic.OnCollisionEnter</c>.</summary>
        private static void ApplyChargeHit(ENT_Player local, Denizen denizen, AttackClass attack) {
            var damageScale = 1f;
            var forceScale = 1f;
            if (CL_GameManager.IsHardmode()) {
                forceScale = 1.7f;
                damageScale = 2f;
            }
            local.Damage(Damageable.DamageInfo.CreateDamageInfo(attack.meleeAttackHitDamage * damageScale, attack.attackTypeTag, attack.attackTaglist, denizen));
            local.AddForce((local.transform.position - denizen.transform.position) * attack.meleeAttackHitForce * forceScale + Vector3.up * attack.meleeAttackHitUpForce,
                "denizen");
            WKTEntitySounds.PlayLocal(denizen.clipHandler, attack.attackHitSound);
            if (!attack.meleeAttackDropHang && !(attack.meleeStaminaDamage > 0f)) return;
            if (attack.meleeAttackDropHang && (attack.dropHangMinimumStaminaThreshhold < 0f ||
                                               local.GetAverageGripStrength(onlyHangingHands: true) < attack.dropHangMinimumStaminaThreshhold))
                local.DropHangWithThreshold(attack.dropHangMinimumStaminaThreshhold, 1f, cooldownOnlyOnHangingHands: false, canResist: true);
            local.DamageGripStrength(attack.meleeStaminaDamage, attack.meleeStaminaDamageOnlyToActiveHands);
        }

        /// <summary>The game's drone charge hit, from <c>DEN_Drone.OnCollisionEnter</c>.</summary>
        private static void ApplyDroneHit(ENT_Player local, GameEntity entity) {
            if (!ApplyFlyingChargeHit(local, entity, ((DEN_Drone)entity).chargeHitForce)) return;
            if (local.GetTagger() is { } tagger && tagger.HasTag("Player"))
                local.DropHang(0.1f, 1f, cooldownOnlyOnHangingHands: false, canResist: true);
        }

        /// <summary>The game's bloodbug charge hit, from <c>DEN_Bloodbug.OnCollisionEnter</c>.</summary>
        private static void ApplyBloodbugHit(ENT_Player local, GameEntity entity) => ApplyFlyingChargeHit(local, entity, ((DEN_Bloodbug)entity).chargeHitForce);

        /// <summary>What drones and bloodbugs share: a push away and up, and a hit worth one (two on hard mode). False when the player is not an entity to them.</summary>
        private static bool ApplyFlyingChargeHit(ENT_Player local, GameEntity entity, float chargeHitForce) {
            var tagger = local.GetTagger();
            if (tagger == null || !tagger.HasTag("Entity")) return false;
            var forceScale = 1f;
            var amount = 1f;
            if (CL_GameManager.IsHardmode()) {
                forceScale = 1.7f;
                amount = 2f;
            }
            local.AddForce((Vector3.Scale((local.transform.position - entity.transform.position).normalized, new Vector3(1f, 0f, 1f)) + Vector3.up * 0.5f).normalized *
                chargeHitForce * forceScale, "denizen");
            local.Damage(Damageable.DamageInfo.CreateDamageInfo(amount, entity.objectType));
            return true;
        }

        private static bool IsFlierMeleeUnderway(GameEntity entity) {
            var flier = (DEN_BasicFlier)entity;
            return s_flierAttacking(flier) && !s_flierHasHit(flier) && flier.attackType == DEN_BasicFlier.FlierAttackType.melee;
        }

        /// <summary>The game's flier melee hit, from <c>DEN_BasicFlier.OnCollisionEnter</c>.</summary>
        private static void ApplyFlierHit(ENT_Player local, GameEntity entity) {
            var flier = (DEN_BasicFlier)entity;
            local.Damage(Damageable.DamageInfo.CreateDamageInfo(flier.meleeAttackHitDamage, flier.attackDamageTag));
            local.AddForce((local.transform.position - flier.transform.position) * flier.meleeAttackHitForce + Vector3.up * flier.meleeAttackUpwardForce, "denizen");
            WKTEntitySounds.PlayLocal(flier.clipHandler, "denizen:attack-hit");
            if (flier.meleeAttackDropHang && local.GetTagger() is { } tagger && tagger.HasTag("Player"))
                local.DropHang(0.1f, 1f, cooldownOnlyOnHangingHands: false, canResist: true);
        }

        #endregion

        public void Forget(ushort handle) {
            m_contacts.Remove(handle);
            m_contactHits.Remove(handle);
            m_hunters.Remove(handle);
        }

        public void Clear() {
            m_attacks.Clear();
            m_contacts.Clear();
            m_contactHits.Clear();
            m_hunters.Clear();
        }

        /// <summary>One enemy type's attack that lands by touching the player.</summary>
        private sealed class ContactAttack {
            /// <summary>Host only: whether the attack is underway and has not hit anything yet.</summary>
            public readonly Func<GameEntity, bool> IsUnderway;
            /// <summary>Client only: applies the game's hit to the local player.</summary>
            public readonly Action<ENT_Player, GameEntity> Apply;
            /// <summary>Host only: what the hit changes for the enemy, as its own collision handler would.</summary>
            public readonly Action<GameEntity> OnHostHit;

            public ContactAttack(Func<GameEntity, bool> isUnderway, Action<ENT_Player, GameEntity> apply, Action<GameEntity> onHostHit) {
                IsUnderway = isUnderway;
                Apply = apply;
                OnHostHit = onHostHit;
            }
        }
    }
}
