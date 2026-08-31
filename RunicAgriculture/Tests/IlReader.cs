using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace RunicAgriculture.Tests
{
    internal readonly struct IlInstruction
    {
        internal IlInstruction(OpCode opCode, object operand)
        {
            OpCode = opCode;
            Operand = operand;
        }

        internal OpCode OpCode { get; }
        internal object Operand { get; }
    }

    internal static class IlReader
    {
        private static readonly OpCode[] OneByte = new OpCode[256];
        private static readonly OpCode[] TwoByte = new OpCode[256];

        static IlReader()
        {
            foreach (FieldInfo field in typeof(OpCodes).GetFields(
                         BindingFlags.Public | BindingFlags.Static))
            {
                if (!(field.GetValue(null) is OpCode code)) continue;
                ushort value = unchecked((ushort)code.Value);
                if (value < 256) OneByte[value] = code;
                else if ((value & 0xff00) == 0xfe00) TwoByte[value & 0xff] = code;
            }
        }

        internal static IReadOnlyList<IlInstruction> Read(MethodBase method)
        {
            MethodBody body = TestAssert.NotNull(method.GetMethodBody(), method + " has no IL body.");
            byte[] bytes = TestAssert.NotNull(body.GetILAsByteArray(), method + " has no IL bytes.");
            var result = new List<IlInstruction>();
            int index = 0;
            while (index < bytes.Length)
            {
                byte first = bytes[index++];
                OpCode code = first == 0xfe ? TwoByte[bytes[index++]] : OneByte[first];
                result.Add(new IlInstruction(code, ReadOperand(method, bytes, ref index, code.OperandType)));
            }
            return result;
        }

        internal static IReadOnlyList<MethodBase> Calls(MethodBase method) => Read(method)
            .Where(item => item.OpCode == OpCodes.Call || item.OpCode == OpCodes.Callvirt ||
                           item.OpCode == OpCodes.Newobj)
            .Select(item => item.Operand as MethodBase)
            .Where(item => item != null)
            .ToArray();

        internal static bool Calls(MethodBase caller, Type owner, string name) =>
            Calls(caller).Any(method => method.DeclaringType == owner && method.Name == name);

        internal static bool CallsNamed(MethodBase caller, string owner, string name) =>
            Calls(caller).Any(method => method.DeclaringType?.FullName == owner && method.Name == name);

        internal static bool LoadsString(MethodBase method, string text) =>
            Read(method).Any(item => item.OpCode == OpCodes.Ldstr &&
                                     string.Equals(item.Operand as string, text, StringComparison.Ordinal));

        private static object ReadOperand(
            MethodBase method,
            byte[] bytes,
            ref int index,
            OperandType type)
        {
            Module module = method.Module;
            Type[] typeArgs = method.DeclaringType?.GetGenericArguments();
            Type[] methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : Type.EmptyTypes;
            switch (type)
            {
                case OperandType.InlineNone: return null;
                case OperandType.ShortInlineI: return unchecked((sbyte)bytes[index++]);
                case OperandType.InlineI: return ReadInt32(bytes, ref index);
                case OperandType.InlineI8:
                    long i8 = BitConverter.ToInt64(bytes, index); index += 8; return i8;
                case OperandType.ShortInlineR:
                    float r4 = BitConverter.ToSingle(bytes, index); index += 4; return r4;
                case OperandType.InlineR:
                    double r8 = BitConverter.ToDouble(bytes, index); index += 8; return r8;
                case OperandType.ShortInlineVar: return bytes[index++];
                case OperandType.InlineVar:
                    ushort variable = BitConverter.ToUInt16(bytes, index); index += 2; return variable;
                case OperandType.ShortInlineBrTarget:
                    sbyte shortDelta = unchecked((sbyte)bytes[index++]); return index + shortDelta;
                case OperandType.InlineBrTarget:
                    int delta = ReadInt32(bytes, ref index); return index + delta;
                case OperandType.InlineSwitch:
                    int count = ReadInt32(bytes, ref index);
                    int origin = index + count * 4;
                    int[] targets = new int[count];
                    for (int item = 0; item < count; item++)
                        targets[item] = origin + ReadInt32(bytes, ref index);
                    return targets;
                case OperandType.InlineString:
                    int stringToken = ReadInt32(bytes, ref index);
                    return Resolve(() => module.ResolveString(stringToken));
                case OperandType.InlineField:
                    int fieldToken = ReadInt32(bytes, ref index);
                    return Resolve(() => module.ResolveField(fieldToken, typeArgs, methodArgs));
                case OperandType.InlineMethod:
                    int methodToken = ReadInt32(bytes, ref index);
                    return Resolve(() => module.ResolveMethod(methodToken, typeArgs, methodArgs));
                case OperandType.InlineType:
                    int typeToken = ReadInt32(bytes, ref index);
                    return Resolve(() => module.ResolveType(typeToken, typeArgs, methodArgs));
                case OperandType.InlineTok:
                    int memberToken = ReadInt32(bytes, ref index);
                    return Resolve(() => module.ResolveMember(memberToken, typeArgs, methodArgs));
                case OperandType.InlineSig: return ReadInt32(bytes, ref index);
                default: throw new NotSupportedException("Unsupported operand type " + type + ".");
            }
        }

        private static int ReadInt32(byte[] bytes, ref int index)
        {
            int value = BitConverter.ToInt32(bytes, index);
            index += 4;
            return value;
        }

        private static object Resolve(Func<object> resolve)
        {
            try { return resolve(); }
            catch (ArgumentException) { return null; }
        }
    }
}
