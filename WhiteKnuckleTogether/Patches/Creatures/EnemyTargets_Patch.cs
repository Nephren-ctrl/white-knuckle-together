using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Enemy code that reads the local player directly goes for whichever player it engages instead (see
    /// <see cref="WKTTargets"/>). Covers every method of every enemy, AI module and hazard that reads the player, including
    /// their coroutines and closures.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class EnemyTargets_Patch {
        private const BindingFlags c_declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly;
        /// <summary>World hazards and spawners that act on the player but are not entities.</summary>
        /// <summary>Flashflowers are not among them: each machine flashes them for its own player (see <see cref="WKTHazardSync"/>).</summary>
        private static readonly Type[] s_hazards = { typeof(ENV_DenizenWaveZone), typeof(ENV_TickEggSack), typeof(ENV_KillBubble) };
        private static readonly string[] s_modulePrefixes = { "Denizen", "DEN_", "AI" };
        /// <summary>Creatures each machine runs for its own player, as in singleplayer (see <see cref="WKTViewerCreatures"/>).</summary>
        private static readonly Type[] s_perViewer = { typeof(DEN_EngravedDoor), typeof(DEN_Pseudopod) };
        /// <summary>
        /// Single methods of other types that aim at the local player: a barnacle tongue aiming (hard mode, or tongues set to
        /// aim) goes for the nearest player. The rest of the tongue's code stays as it is, for the tongue sync.
        /// </summary>
        private static readonly MethodInfo?[] s_extraTargets = { AccessTools.Method(typeof(Barnacle_Tongue_Basic), nameof(Barnacle_Tongue_Basic.FindTarget)) };
        private static readonly MethodInfo s_entityAddForce = AccessTools.Method(typeof(GameEntity), nameof(GameEntity.AddForce), new[] { typeof(Vector3), typeof(string) });
        private static readonly MethodInfo s_playerAddForce = AccessTools.Method(typeof(ENT_Player), nameof(ENT_Player.AddForce), new[] { typeof(Vector3), typeof(string) });
        private static readonly Dictionary<MethodBase, List<CodeInstruction>> s_selfLoaders = new();

        /// <summary>Every method that reads the local player in an enemy, AI module or hazard type, or in a type nested in one.</summary>
        public static List<MethodBase> GetTargets() {
            var targets = new List<MethodBase>();
            var members = new List<MemberInfo>();
            foreach (var type in WKTGameTypes.All) {
                if (!IsEnemyCode(type) || type.ContainsGenericParameters) continue;
                foreach (var method in type.GetMethods(c_declared)) {
                    if (method.IsAbstract || method.GetMethodBody() == null || IsLocalOnly(method)) continue;
                    members.Clear();
                    if (!WKTILCallReader.TryGetMemberReferences(method, members, true) || (!ReadsLocalPlayer(members) && !PushesEntities(members))) continue;
                    targets.Add(method);
                }
            }
            foreach (var extra in s_extraTargets) {
                if (extra != null && !targets.Contains(extra))
                    targets.Add(extra);
            }
            targets.Sort((a, b) => string.CompareOrdinal(WKTContentRandomTargets.GetId(a), WKTContentRandomTargets.GetId(b)));
            return targets;
        }

        /// <summary>
        /// Whether a method only ever acts for this machine's own player, so it is never pointed at another: the per-viewer
        /// creatures, and the Remains' dread and neck snap (its <c>Update</c> and what it starts), which each player's machine
        /// runs for them (see <see cref="WKTViewerCreatures"/>). The Remains still moves toward the player it engages.
        /// </summary>
        public static bool IsLocalOnly(MethodBase method) {
            var type = method.DeclaringType;
            if (type == null) return false;
            var outer = type;
            while (outer.DeclaringType != null)
                outer = outer.DeclaringType;
            foreach (var perViewer in s_perViewer) {
                if (perViewer.IsAssignableFrom(outer)) return true;
            }
            if (outer != typeof(DEN_Remains_Active)) return false;
            return type == outer ? method.Name == nameof(DEN_Remains_Active.Update) : type.Name.Contains("<" + nameof(DEN_Remains_Active.Update) + ">");
        }

        /// <summary>Whether a type belongs to an enemy, AI module or hazard, or is nested in one.</summary>
        public static bool IsEnemyCode(Type type) {
            var outer = type;
            while (outer.DeclaringType != null)
                outer = outer.DeclaringType;
            // The goo is about each machine's own player; WKTGooSync shares it.
            if (typeof(ENT_Player).IsAssignableFrom(outer) || outer == typeof(GameEntity) || typeof(CL_Prop).IsAssignableFrom(outer) ||
                outer == typeof(WKTPlayerProxy) || outer == typeof(DEN_DeathFloor)) return false;
            if (typeof(GameEntity).IsAssignableFrom(outer) || Array.IndexOf(s_hazards, outer) >= 0) return true;
            foreach (var prefix in s_modulePrefixes) {
                if (outer.Name.StartsWith(prefix, StringComparison.Ordinal) && outer.Namespace == null) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether a method pushes an entity, which may be a remote player's proxy it got from its target rather than from
        /// reading the player, such as a Face knocking its target back.
        /// </summary>
        private static bool PushesEntities(List<MemberInfo> members) {
            foreach (var member in members) {
                if (Equals(member, s_entityAddForce) || Equals(member, s_playerAddForce)) return true;
            }
            return false;
        }

        private static bool ReadsLocalPlayer(List<MemberInfo> members) {
            foreach (var member in members) {
                if (Equals(member, WKTCallRewriter.GetPlayer) || Equals(member, WKTCallRewriter.PlayerObject)) return true;
            }
            return false;
        }

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() {
            var targets = GetTargets();
            foreach (var target in targets)
                s_selfLoaders[target] = WKTPlayerReferenceRewriter.BuildSelfLoader(target);
            WKT.InstanceOrNull?.Logger.LogInfo($"Pointing {targets.Count} enemy methods at every player.");
            return targets;
        }

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
            if (!s_selfLoaders.TryGetValue(__originalMethod, out var loader))
                loader = WKTPlayerReferenceRewriter.BuildSelfLoader(__originalMethod);
            return WKTPlayerReferenceRewriter.Rewrite(instructions, loader, out _);
        }
    }

    /// <summary>
    /// Proxies only exist for enemies to see. What a player walks into is handled on that player's own machine, so no
    /// trigger in the host's world reacts to a proxy.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class ProxyTriggers_Patch {
        private static readonly string[] s_messages = { "OnTriggerEnter", "OnTriggerStay", "OnTriggerExit" };

        /// <summary>Every trigger message a game MonoBehaviour handles, including those its generic bases declare.</summary>
        public static List<MethodBase> GetTargets() {
            var targets = new List<MethodBase>();
            foreach (var type in WKTGameTypes.All) {
                if (!typeof(MonoBehaviour).IsAssignableFrom(type) || type.ContainsGenericParameters) continue;
                for (var current = type; current != null && current != typeof(MonoBehaviour); current = current.BaseType) {
                    // Concrete types are visited themselves; only closed generic bases need finding through their subclasses.
                    if (current != type && !current.IsGenericType) break;
                    foreach (var name in s_messages) {
                        var method = current.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                            null, new[] { typeof(Collider) }, null);
                        if (method != null && method.GetMethodBody() != null && !targets.Contains(method))
                            targets.Add(method);
                    }
                }
            }
            return targets;
        }

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => GetTargets();

        [UsedImplicitly]
        private static bool Prefix(Collider __0) => !WKTPlayerProxy.IsProxyCollider(__0);
    }
}
