using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace RunicInventory.Tests
{
    internal readonly struct IlInstruction
    {
        internal IlInstruction(OpCode code, object operand) { Code = code; Operand = operand; }
        internal OpCode Code { get; }
        internal object Operand { get; }
    }

    internal static class IlReader
    {
        private static readonly OpCode[] One = new OpCode[256];
        private static readonly OpCode[] Two = new OpCode[256];

        static IlReader()
        {
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!(field.GetValue(null) is OpCode code)) continue;
                ushort value = unchecked((ushort)code.Value);
                if (value < 256) One[value] = code;
                else if ((value & 0xff00) == 0xfe00) Two[value & 0xff] = code;
            }
        }

        internal static IReadOnlyList<IlInstruction> Read(MethodBase method)
        {
            MethodBody body = TestAssert.NotNull(method.GetMethodBody(), method + " has no body.");
            byte[] bytes = TestAssert.NotNull(body.GetILAsByteArray(), method + " has no IL.");
            var result = new List<IlInstruction>();
            int index = 0;
            while (index < bytes.Length)
            {
                byte first = bytes[index++];
                OpCode code = first == 0xfe ? Two[bytes[index++]] : One[first];
                result.Add(new IlInstruction(code, Operand(method, bytes, ref index, code.OperandType)));
            }
            return result;
        }

        internal static IReadOnlyList<MethodBase> Calls(MethodBase method) =>
            Read(method).Where(item => item.Code == OpCodes.Call || item.Code == OpCodes.Callvirt || item.Code == OpCodes.Newobj)
                .Select(item => item.Operand as MethodBase).Where(item => item != null).ToArray();

        internal static bool Calls(MethodBase method, Type owner, string name) =>
            Calls(method).Any(call => call.DeclaringType == owner && call.Name == name);

        internal static bool CallsNamed(MethodBase method, string owner, string name) =>
            Calls(method).Any(call => call.DeclaringType?.FullName == owner && call.Name == name);

        internal static bool Accesses(MethodBase method, Type owner, string name) =>
            Read(method).Any(item => item.Operand is FieldInfo field && field.DeclaringType == owner && field.Name == name);

        internal static bool LoadsString(MethodBase method, string value) =>
            Read(method).Any(item => item.Code == OpCodes.Ldstr && string.Equals(item.Operand as string, value, StringComparison.Ordinal));

        internal static bool HasFinally(MethodBase method) =>
            method.GetMethodBody()?.ExceptionHandlingClauses.Any(clause => clause.Flags == ExceptionHandlingClauseOptions.Finally) == true;

        internal static int CallIndex(IReadOnlyList<MethodBase> calls, Type owner, string name)
        {
            for (int index = 0; index < calls.Count; index++)
                if (calls[index].DeclaringType == owner && calls[index].Name == name) return index;
            return -1;
        }

        private static object Operand(MethodBase method, byte[] bytes, ref int index, OperandType type)
        {
            Module module = method.Module;
            Type[] typeArgs = method.DeclaringType?.GetGenericArguments();
            Type[] methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : Type.EmptyTypes;
            switch (type)
            {
                case OperandType.InlineNone: return null;
                case OperandType.ShortInlineI: return unchecked((sbyte)bytes[index++]);
                case OperandType.InlineI: return I32(bytes, ref index);
                case OperandType.InlineI8: long i8 = BitConverter.ToInt64(bytes, index); index += 8; return i8;
                case OperandType.ShortInlineR: float r4 = BitConverter.ToSingle(bytes, index); index += 4; return r4;
                case OperandType.InlineR: double r8 = BitConverter.ToDouble(bytes, index); index += 8; return r8;
                case OperandType.ShortInlineVar: return bytes[index++];
                case OperandType.InlineVar: ushort variable = BitConverter.ToUInt16(bytes, index); index += 2; return variable;
                case OperandType.ShortInlineBrTarget: sbyte sd = unchecked((sbyte)bytes[index++]); return index + sd;
                case OperandType.InlineBrTarget: int delta = I32(bytes, ref index); return index + delta;
                case OperandType.InlineSwitch:
                    int count = I32(bytes, ref index); int origin = index + count * 4; int[] targets = new int[count];
                    for (int item = 0; item < count; item++) targets[item] = origin + I32(bytes, ref index); return targets;
                case OperandType.InlineString: int st = I32(bytes, ref index); return Resolve(() => module.ResolveString(st));
                case OperandType.InlineField: int ft = I32(bytes, ref index); return Resolve(() => module.ResolveField(ft, typeArgs, methodArgs));
                case OperandType.InlineMethod: int mt = I32(bytes, ref index); return Resolve(() => module.ResolveMethod(mt, typeArgs, methodArgs));
                case OperandType.InlineType: int tt = I32(bytes, ref index); return Resolve(() => module.ResolveType(tt, typeArgs, methodArgs));
                case OperandType.InlineTok: int tok = I32(bytes, ref index); return Resolve(() => module.ResolveMember(tok, typeArgs, methodArgs));
                case OperandType.InlineSig: return I32(bytes, ref index);
                default: throw new NotSupportedException(type.ToString());
            }
        }

        private static int I32(byte[] bytes, ref int index) { int value = BitConverter.ToInt32(bytes, index); index += 4; return value; }
        private static object Resolve(Func<object> resolver) { try { return resolver(); } catch (ArgumentException) { return null; } }
    }
}
