using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Lists the methods a method body calls, straight from its IL. Works on any runtime, unlike readers that need to
    /// rebuild the method first.
    /// </summary>
    internal static class WKTILCallReader {
        private static readonly OpCode[] s_oneByte = new OpCode[0x100];
        private static readonly OpCode[] s_twoByte = new OpCode[0x100];

        static WKTILCallReader() {
            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)) {
                if (field.GetValue(null) is not OpCode opCode) continue;
                var value = (ushort)opCode.Value;
                if (opCode.Size == 1)
                    s_oneByte[value] = opCode;
                else
                    s_twoByte[value & 0xFF] = opCode;
            }
        }

        /// <summary>
        /// Adds every method <paramref name="method"/> calls, constructs or takes a delegate to into <paramref name="callees"/>.
        /// False, adding nothing, when it has no IL or the IL cannot be read.
        /// </summary>
        public static bool TryGetCallees(MethodBase method, List<MethodBase> callees) {
            var members = new List<MemberInfo>();
            if (!TryGetMemberReferences(method, members, false)) return false;
            foreach (var member in members)
                callees.Add((MethodBase)member);
            return true;
        }

        /// <summary>
        /// Adds, in order, every method <paramref name="method"/> calls, constructs or takes a delegate to, and with
        /// <paramref name="includeFields"/> every field it loads or stores, into <paramref name="members"/>. False, adding
        /// nothing, when it has no IL or the IL cannot be read.
        /// </summary>
        public static bool TryGetMemberReferences(MethodBase method, List<MemberInfo> members, bool includeFields) {
            byte[]? il;
            try {
                il = method.GetMethodBody()?.GetILAsByteArray();
            }
            catch (Exception) {
                return false;
            }
            if (il == null) return false;
            var start = members.Count;
            var typeArgs = method.DeclaringType is { IsGenericType: true } type ? type.GetGenericArguments() : null;
            var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
            var position = 0;
            while (position < il.Length) {
                OpCode opCode;
                var first = il[position++];
                if (first == 0xFE) {
                    if (position >= il.Length) return Fail(members, start);
                    opCode = s_twoByte[il[position++]];
                }
                else
                    opCode = s_oneByte[first];
                if (opCode.Size == 0) return Fail(members, start);
                switch (opCode.OperandType) {
                    case OperandType.InlineField when includeFields:
                        if (position + 4 > il.Length) return Fail(members, start);
                        try {
                            var field = method.Module.ResolveField(BitConverter.ToInt32(il, position), typeArgs, methodArgs);
                            if (field != null)
                                members.Add(field);
                        }
                        catch (Exception) {
                            // Same as an unresolvable method below.
                        }
                        position += 4;
                        break;
                    case OperandType.InlineMethod:
                        if (position + 4 > il.Length) return Fail(members, start);
                        var token = BitConverter.ToInt32(il, position);
                        try {
                            var callee = method.Module.ResolveMethod(token, typeArgs, methodArgs);
                            if (callee != null)
                                members.Add(callee);
                        }
                        catch (Exception) {
                            // A callee in an assembly that cannot load is not game code, so it cannot reach the game's random.
                        }
                        position += 4;
                        break;
                    case OperandType.InlineSwitch:
                        if (position + 4 > il.Length) return Fail(members, start);
                        position += 4 + BitConverter.ToInt32(il, position) * 4;
                        break;
                    default:
                        position += GetOperandSize(opCode.OperandType);
                        break;
                }
            }
            return true;
        }

        private static bool Fail(List<MemberInfo> members, int start) {
            members.RemoveRange(start, members.Count - start);
            return false;
        }

        private static int GetOperandSize(OperandType type) => type switch {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget => 1,
            OperandType.ShortInlineI => 1,
            OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 => 8,
            OperandType.InlineR => 8,
            _ => 4
        };
    }
}
