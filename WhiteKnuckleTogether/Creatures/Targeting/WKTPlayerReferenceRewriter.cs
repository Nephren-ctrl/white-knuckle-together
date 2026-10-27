using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Rewrites enemy code written for a single player so it acts on whichever player that enemy engages (see
    /// <see cref="WKTTargets"/>). Reads of the player's transform and camera, death checks, and effects on the player become
    /// calls into <see cref="WKTTargets"/> that also get the enemy itself, pushed by a "self" loader built per method.
    /// </summary>
    internal static class WKTPlayerReferenceRewriter {
        private const BindingFlags c_any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        public static readonly MethodInfo TransformFor = AccessTools.Method(typeof(WKTTargets), nameof(WKTTargets.TransformFor));
        private static readonly FieldInfo s_controller = AccessTools.Field(typeof(ENT_Player), nameof(ENT_Player.cCon));
        private static readonly MethodInfo s_controllerVelocity = AccessTools.PropertyGetter(typeof(CharacterController), nameof(CharacterController.velocity));
        private static readonly MethodInfo s_velocityOf = Target(nameof(WKTTargets.VelocityOf));
        /// <summary>Each call on the player, and the <see cref="WKTTargets"/> method taking its receiver, its arguments, then self.</summary>
        public static readonly Dictionary<MethodBase, MethodInfo> CallReplacements = BuildCallReplacements();
        /// <summary>Each field read on the player, and the <see cref="WKTTargets"/> method taking the receiver, then self.</summary>
        public static readonly Dictionary<FieldInfo, MethodInfo> FieldReplacements = new() {
            { AccessTools.Field(typeof(ENT_Player), nameof(ENT_Player.cam)), Target(nameof(WKTTargets.CamOf)) },
            { AccessTools.Field(typeof(GameEntity), nameof(GameEntity.dead)), Target(nameof(WKTTargets.DeadOf)) }
        };

        /// <summary>
        /// Rewrites <paramref name="instructions"/>, pushing self with <paramref name="selfLoader"/> before each replacement.
        /// Returns the number of rewrites in <paramref name="count"/>.
        /// </summary>
        public static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, IReadOnlyList<CodeInstruction> selfLoader, out int count) {
            var list = new List<CodeInstruction>(instructions);
            count = 0;
            for (var i = 0; i < list.Count; i++) {
                var instruction = list[i];
                if (WKTCallRewriter.LoadsLocalPlayer(instruction) && i + 1 < list.Count && list[i + 1].Calls(WKTCallRewriter.GetTransform)) {
                    // player.transform: self, then the target's transform.
                    list.RemoveAt(i + 1);
                    list.RemoveAt(i);
                    var inserted = InsertSelf(list, i, selfLoader, instruction);
                    list.Insert(i + inserted, new CodeInstruction(OpCodes.Call, TransformFor));
                    i += inserted;
                    count++;
                    continue;
                }
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, s_controller) && i + 1 < list.Count && list[i + 1].Calls(s_controllerVelocity)) {
                    // player.cCon.velocity, such as a barnacle tongue leading its aim: the target's velocity.
                    list.RemoveAt(i + 1);
                    i += InsertSelf(list, i, selfLoader, instruction);
                    list[i] = new CodeInstruction(OpCodes.Call, s_velocityOf);
                    count++;
                    continue;
                }
                MethodInfo? replacement = null;
                // Only virtual calls: a non-virtual call is an override calling its base, which dispatching again would loop.
                if (instruction.operand is MethodBase method && instruction.opcode == OpCodes.Callvirt)
                    CallReplacements.TryGetValue(method, out replacement);
                else if (instruction.operand is FieldInfo field && instruction.opcode == OpCodes.Ldfld)
                    FieldReplacements.TryGetValue(field, out replacement);
                if (replacement == null) continue;
                // Arguments are already on the stack: self goes on top, then the replacement takes them all.
                var selfCount = InsertSelf(list, i, selfLoader, instruction);
                i += selfCount;
                list[i] = new CodeInstruction(OpCodes.Call, replacement);
                count++;
            }
            return list;
        }

        /// <summary>
        /// The instructions that push the enemy a method belongs to: <c>this</c> for a component, the component a coroutine
        /// or closure was started from, or the component a helper object holds. Null pushes when there is none, which makes
        /// every replacement act on the local player as before.
        /// </summary>
        public static List<CodeInstruction> BuildSelfLoader(MethodBase method) {
            var loader = new List<CodeInstruction>();
            var type = method.DeclaringType;
            if (method.IsStatic || type == null) {
                loader.Add(new CodeInstruction(OpCodes.Ldnull));
                return loader;
            }
            if (typeof(Component).IsAssignableFrom(type)) {
                loader.Add(new CodeInstruction(OpCodes.Ldarg_0));
                return loader;
            }
            if (WKTContentRandomTargets.TryGetOwnerChain(type, out var chain)) {
                loader.Add(new CodeInstruction(OpCodes.Ldarg_0));
                foreach (var field in chain)
                    loader.Add(new CodeInstruction(OpCodes.Ldfld, field));
                return loader;
            }
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
                if (!typeof(Component).IsAssignableFrom(field.FieldType)) continue;
                loader.Add(new CodeInstruction(OpCodes.Ldarg_0));
                loader.Add(new CodeInstruction(OpCodes.Ldfld, field));
                return loader;
            }
            loader.Add(new CodeInstruction(OpCodes.Ldnull));
            return loader;
        }

        /// <summary>Inserts copies of <paramref name="selfLoader"/> at <paramref name="index"/>, taking over the labels and blocks of <paramref name="from"/>.</summary>
        private static int InsertSelf(List<CodeInstruction> list, int index, IReadOnlyList<CodeInstruction> selfLoader, CodeInstruction from) {
            for (var j = 0; j < selfLoader.Count; j++) {
                var copy = new CodeInstruction(selfLoader[j].opcode, selfLoader[j].operand);
                if (j == 0) {
                    from.MoveLabelsTo(copy);
                    from.MoveBlocksTo(copy);
                }
                list.Insert(index + j, copy);
            }
            return selfLoader.Count;
        }

        private static MethodInfo Target(string name) => AccessTools.Method(typeof(WKTTargets), name);

        private static Dictionary<MethodBase, MethodInfo> BuildCallReplacements() {
            var replacements = new Dictionary<MethodBase, MethodInfo>();
            void Add(Type type, string name, Type[] parameters, string replacement) {
                var method = type.GetMethod(name, c_any, null, parameters, null);
                if (method != null)
                    replacements[method] = Target(replacement);
            }
            var damage = new[] { typeof(Damageable.DamageInfo) };
            var kill = new[] { typeof(string), typeof(Damageable.DamageInfo) };
            var grappled = new[] { typeof(bool), typeof(AIGameEntity.Grappler) };
            foreach (var type in new[] { typeof(GameEntity), typeof(ENT_Player) }) {
                Add(type, nameof(GameEntity.Damage), damage, nameof(WKTTargets.DamageVia));
                Add(type, nameof(GameEntity.Kill), kill, nameof(WKTTargets.KillVia));
                Add(type, nameof(GameEntity.IsDead), Type.EmptyTypes, nameof(WKTTargets.IsDeadOf));
                Add(type, nameof(GameEntity.SetGrappled), grappled, nameof(WKTTargets.SetGrappledVia));
                Add(type, nameof(GameEntity.AddForce), new[] { typeof(Vector3), typeof(string) }, nameof(WKTTargets.AddForceVia));
            }
            var player = typeof(ENT_Player);
            Add(player, nameof(ENT_Player.IsHanging), Type.EmptyTypes, nameof(WKTTargets.IsHanging));
            Add(player, nameof(ENT_Player.HasPerk), new[] { typeof(string) }, nameof(WKTTargets.HasPerkOf));
            Add(player, nameof(ENT_Player.GetCurrentFallDistance), Type.EmptyTypes, nameof(WKTTargets.GetCurrentFallDistance));
            Add(player, "DropEverything", new[] { typeof(bool), typeof(float) }, nameof(WKTTargets.DropEverything));
            Add(player, nameof(ENT_Player.Lock), Type.EmptyTypes, nameof(WKTTargets.Lock));
            Add(player, nameof(ENT_Player.SetGravity), new[] { typeof(bool) }, nameof(WKTTargets.SetGravity));
            Add(player, nameof(ENT_Player.SetGravityMult), new[] { typeof(float) }, nameof(WKTTargets.SetGravityMult));
            Add(player, nameof(ENT_Player.ScaleVelocityMagnitude), new[] { typeof(float) }, nameof(WKTTargets.ScaleVelocityMagnitude));
            Add(player, nameof(ENT_Player.AddRotationVelocity), new[] { typeof(Vector3) }, nameof(WKTTargets.AddRotationVelocity));
            Add(player, nameof(ENT_Player.DamageGripStrength), new[] { typeof(float), typeof(bool) }, nameof(WKTTargets.DamageGripStrength));
            Add(player, nameof(ENT_Player.AddGripStrength), new[] { typeof(float), typeof(bool), typeof(bool) }, nameof(WKTTargets.AddGripStrength));
            Add(player, nameof(ENT_Player.SplatterScreenBlood), new[] { typeof(int) }, nameof(WKTTargets.SplatterScreenBlood));
            Add(player, nameof(ENT_Player.SplatterScreenBlood), new[] { typeof(int), typeof(Color) }, nameof(WKTTargets.SplatterScreenBloodColored));
            Add(player, nameof(ENT_Player.SplatterScreenBlood), new[] { typeof(int), typeof(List<string>) }, nameof(WKTTargets.SplatterScreenBloodTagged));
            return replacements;
        }
    }
}
