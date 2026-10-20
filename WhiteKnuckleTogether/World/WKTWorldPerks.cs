using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using Random = UnityEngine.Random;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Host only, in a shared world: the perks of other players that act on the world act on it for everyone, as the
    /// host's own already do.
    /// <list type="bullet">
    /// <item>The shared goo goes as fast or as slow as everyone's mass perks make it (Mass Damper, Employee Retention and
    /// Motivator, Paranoia).</item>
    /// <item>Item spawners (Friends Upstairs' briefcases) and hunters (Binding: Hunted) spawn by each player who has them,
    /// on the host, so everyone sees them. Clients never run their own copies.</item>
    /// </list>
    /// Spawn tables that count a perk count any player's (see <c>PerkChecker_Patch</c>).
    /// </summary>
    internal sealed class WKTWorldPerks : MonoBehaviour {
        /// <summary>The session flag Binding: Hunted's perk sets, which ties the goo to its player.</summary>
        private const string c_hunt_flag = "masshuntplayer";
        /// <summary>How long a failed spawn waits before trying again, as the game's own spawner does.</summary>
        private const float c_retry_seconds = 2f;
        private const int c_spawn_steps = 3;
        private const float c_drop_distance = 20f;
        private const float c_locator_search_height = 25f;
        /// <summary>Internal to the game: the Face slows or speeds along with the mass.</summary>
        private static readonly Action<float>? s_setFaceSpeed =
            AccessTools.MethodDelegate<Action<float>>(AccessTools.Method(typeof(DEN_Face), "SetGlobalUpdateSpeedMultiplier"));
        private readonly Dictionary<(byte, string, int), float> m_spawnTimers = new();
        private readonly Dictionary<(byte, string, int), Denizen> m_hunters = new();
        private readonly List<(byte, string, int)> m_staleKeys = new();
        private WKTNetwork m_network = null!;

        [UsedImplicitly]
        private void Awake() {
            m_network = WKT.Instance.Network;
            m_network.OnPlayerDisconnected += OnPlayerGone;
        }

        [UsedImplicitly]
        private void OnDestroy() {
            var wkt = WKT.InstanceOrNull;
            if (wkt != null)
                wkt.Network.OnPlayerDisconnected -= OnPlayerGone;
        }

        [UsedImplicitly]
        private void Update() {
            if (m_network.Mode != NetworkMode.Host || !m_network.Session.IsSyncedWorld || WKTWorldSync.Instance == null) return;
            var player = ENT_Player.playerObject;
            var gameManager = CL_GameManager.gMan;
            if (player == null || gameManager == null || gameManager.isPaused) return;
            var otherSpeed = 0f;
            var otherResist = 0f;
            var otherSlow = 0f;
            var controller = 1f;
            var leashHeight = float.NegativeInfinity;
            var leashTarget = Vector3.zero;
            foreach (var playerId in WKTPlayerPerks.Players) {
                var perks = WKTPlayerPerks.Get(playerId);
                if (perks == null || WKTPlayerDeaths.IsDead(playerId) || !WKTPlayerPositions.TryGetRemotePosition(playerId, out var position)) continue;
                foreach (var pair in perks) {
                    var perk = WKTPerkAssets.Get(pair.Key);
                    if (perk == null) continue;
                    AddMassBuffs(perk, pair.Value, ref otherSpeed, ref otherResist, ref otherSlow);
                    RunModules(perk, playerId, position, ref controller);
                    if (!HasFlag(perk, c_hunt_flag) || position.y <= leashHeight) continue;
                    leashHeight = position.y;
                    leashTarget = position;
                }
            }
            ApplyMass(player, otherSpeed, otherResist, otherSlow, controller);
            if (!float.IsNegativeInfinity(leashHeight))
                ApplyLeash(leashTarget);
        }

        private static bool HasFlag(Perk perk, string flag) => perk.flags != null && perk.flags.Contains(flag);

        /// <summary>
        /// Binding: Hunted's leash for another player who has it (the game's <c>DEN_DeathFloor.Update</c> only follows the
        /// local player): the goo never stays more than its leash below them. The shared goo carries it to everyone.
        /// </summary>
        private static void ApplyLeash(Vector3 hunted) {
            var goo = DEN_DeathFloor.instance;
            if (goo == null) return;
            var gooTransform = goo.transform;
            if (WKTWorldPerkMath.IsBeyondLeash(hunted.y, gooTransform.position.y))
                gooTransform.position = hunted - Vector3.up * WKTWorldPerkMath.HUNT_LEASH;
        }

        /// <summary>Sums a perk's mass buffs, scaled by its stacks as the game scales them.</summary>
        private static void AddMassBuffs(Perk perk, int stack, ref float speed, ref float resist, ref float slow) {
            if (!perk.useBuff) return;
            if (perk.buff?.buffs != null) {
                var multiplier = GetStackMultiplier(perk, stack) * perk.buffMultiplier;
                AddBuffs(perk.buff.buffs, multiplier, ref speed, ref resist, ref slow);
            }
            if (perk.useBaseBuff && perk.baseBuff?.buffs != null)
                AddBuffs(perk.baseBuff.buffs, 1f, ref speed, ref resist, ref slow);
        }

        private static void AddBuffs(List<BuffContainer.Buff> buffs, float multiplier, ref float speed, ref float resist, ref float slow) {
            foreach (var buff in buffs) {
                switch (buff.id) {
                    case WKTWorldPerkMath.MASS_SPEED_BUFF:
                        speed += buff.amount * multiplier;
                        break;
                    case WKTWorldPerkMath.MASS_RESIST_BUFF:
                        resist += buff.amount * multiplier;
                        break;
                    case WKTWorldPerkMath.MASS_SLOW_BUFF:
                        slow += buff.amount * multiplier;
                        break;
                }
            }
        }

        /// <summary>The game's <c>Perk.GetStackEvaluationMultiplier</c>, for a stack count instead of a live perk.</summary>
        private static float GetStackMultiplier(Perk perk, int stack) {
            var multiplier = 1f;
            if (stack <= 1 || perk.multiplierCurve == null || perk.stackMax <= 0) return multiplier;
            for (var i = 1; i < Mathf.Min(stack, perk.stackMax); i++)
                multiplier += perk.multiplierCurve.Evaluate((float)i / perk.stackMax);
            return multiplier;
        }

        /// <summary>The perk's modules that act on the world, run for the player who has it.</summary>
        private void RunModules(Perk perk, byte playerId, Vector3 position, ref float controller) {
            if (perk.modules == null) return;
            for (var i = 0; i < perk.modules.Count; i++) {
                switch (perk.modules[i]) {
                    case PerkModule_MassController mass:
                        controller *= mass.massMult;
                        break;
                    case PerkModule_MassProximityEffect proximity:
                        var goo = DEN_DeathFloor.instance;
                        if (goo != null && goo.IsActiveAndMoving() && position.y - DEN_DeathFloor.GetHeight() < proximity.massProximityDistance)
                            controller *= proximity.proximityMassSpeedMult;
                        break;
                    case PerkModule_ObjectSpawner spawner:
                        RunSpawner(spawner, (playerId, perk.id, i), position);
                        break;
                    case PerkModule_Hunted hunted:
                        RunHunter(hunted, (playerId, perk.id, i), position);
                        break;
                }
            }
        }

        private static void ApplyMass(ENT_Player player, float otherSpeed, float otherResist, float otherSlow, float controller) {
            var goo = DEN_DeathFloor.instance;
            if (controller != 1f)
                s_setFaceSpeed?.Invoke(controller);
            if (goo == null) return;
            var buffs = player.curBuffs;
            var factor = WKTWorldPerkMath.GetMassFactor(buffs.GetBuff(WKTWorldPerkMath.MASS_SPEED_BUFF), buffs.GetBuff(WKTWorldPerkMath.MASS_RESIST_BUFF),
                buffs.GetBuff(WKTWorldPerkMath.MASS_SLOW_BUFF), otherSpeed, otherResist, otherSlow) * controller;
            if (factor != 1f)
                goo.SetUpdateSpeedMultiplier(factor);
        }

        #region Spawners

        /// <summary>The game's <c>PerkModule_ObjectSpawner</c>, by another player.</summary>
        private void RunSpawner(PerkModule_ObjectSpawner spawner, (byte, string, int) key, Vector3 position) {
            if (spawner.asset == null) return;
            // Waits a full interval before the first spawn, like a newly picked perk counts down on its owner's machine.
            if (!m_spawnTimers.TryGetValue(key, out var timer))
                timer = spawner.spawnFrequency;
            timer -= Time.deltaTime;
            if (timer <= 0f)
                timer = TrySpawn(spawner, position) ? spawner.spawnFrequency : c_retry_seconds;
            m_spawnTimers[key] = timer;
        }

        private bool TrySpawn(PerkModule_ObjectSpawner spawner, Vector3 origin) {
            if (spawner.useLocators) {
                var locator = UT_Locator.GetClosestLocatorInGroupToPosition(spawner.locatorGroupID, origin + Vector3.up * c_locator_search_height);
                if (locator != null && locator.position.y - origin.y > spawner.minimumDistance) {
                    SpawnObject(spawner, locator.position);
                    return true;
                }
            }
            var point = origin;
            var direction = Vector3.up + Random.insideUnitSphere * 0.8f;
            for (var i = 0; i < c_spawn_steps; i++) {
                point = Physics.Raycast(point, direction, out var hit, spawner.checkStepDistance, spawner.hitMask) ? hit.point : point + direction * spawner.checkStepDistance;
                direction = Vector3.up + Random.insideUnitSphere;
            }
            if (!Physics.Raycast(point, Vector3.down, out var ground, c_drop_distance, spawner.hitMask) ||
                Vector3.Distance(ground.point, origin) < spawner.minimumDistance) return false;
            SpawnObject(spawner, ground.point + ground.normal * spawner.hitOffset);
            return true;
        }

        private void SpawnObject(PerkModule_ObjectSpawner spawner, Vector3 position) {
            Transform? parent = null;
            if (WorldLoader.initialized && WorldLoader.instance.GetCurrentLevel() != null)
                parent = WorldLoader.instance.GetCurrentLevel().GetLevel().transform;
            var spawned = Instantiate(spawner.asset, position, Quaternion.Euler(spawner.spawnRotation), parent);
            StartCoroutine(ScaleUp(spawned.transform));
        }

        private static IEnumerator ScaleUp(Transform spawned) {
            var time = 0f;
            while (time < 1f && spawned != null) {
                time += Time.deltaTime;
                spawned.localScale = Vector3.Lerp(Vector3.zero, Vector3.one, time);
                yield return null;
            }
        }

        /// <summary>The game's <c>PerkModule_Hunted</c>, by another player: a hunter that comes back whenever it dies.</summary>
        private void RunHunter(PerkModule_Hunted hunted, (byte, string, int) key, Vector3 position) {
            if (hunted.huntEntity == null) return;
            if (m_hunters.TryGetValue(key, out var hunter) && hunter != null && !hunter.IsDead()) return;
            if (hunter != null)
                Destroy(hunter.gameObject);
            m_hunters[key] = Instantiate(hunted.huntEntity, position + hunted.spawnOffset, Quaternion.identity);
        }

        #endregion

        private void OnPlayerGone(WKTPlayer player) {
            m_staleKeys.Clear();
            foreach (var key in m_hunters.Keys)
                if (key.Item1 == player.Id) m_staleKeys.Add(key);
            foreach (var key in m_staleKeys) {
                if (m_hunters[key] != null)
                    Destroy(m_hunters[key].gameObject);
                m_hunters.Remove(key);
            }
            m_staleKeys.Clear();
            foreach (var key in m_spawnTimers.Keys)
                if (key.Item1 == player.Id) m_staleKeys.Add(key);
            foreach (var key in m_staleKeys)
                m_spawnTimers.Remove(key);
            m_staleKeys.Clear();
        }
    }
}
