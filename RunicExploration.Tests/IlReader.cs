using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace RunicExploration.Tests
{
    internal readonly struct IlInstruction
    {
        internal IlInstruction(int offset, OpCode opCode, object operand)
        {
            Offset = offset;
            OpCode = opCode;
            Operand = operand;
        }
        internal int Offset { get; }
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
                if (!(field.GetValue(null) is OpCode opCode)) continue;
                ushort value = unchecked((ushort)opCode.Value);
                if (value < 256) OneByte[value] = opCode;
                else if ((value & 0xff00) == 0xfe00) TwoByte[value & 0xff] = opCode;
            }
        }

        internal static IReadOnlyList<IlInstruction> Read(MethodBase method)
        {
            MethodBody body = TestAssert.NotNull(method.GetMethodBody(),
                method.DeclaringType?.FullName + "." + method.Name + " has no body.");
            byte[] bytes = TestAssert.NotNull(body.GetILAsByteArray(), "Method has no IL.");
            var result = new List<IlInstruction>();
            int index = 0;
            while (index < bytes.Length)
            {
                int offset = index;
                byte first = bytes[index++];
                OpCode code = first == 0xfe ? TwoByte[bytes[index++]] : OneByte[first];
                if (string.IsNullOrEmpty(code.Name))
                    throw new InvalidOperationException("Unknown opcode at " + offset + ".");
                result.Add(new IlInstruction(offset, code,
                    ReadOperand(method, bytes, ref index, code.OperandType)));
            }
            return result;
        }

        internal static IReadOnlyList<MethodBase> Calls(MethodBase method) =>
            Read(method)
                .Where(item => item.OpCode == OpCodes.Call || item.OpCode == OpCodes.Callvirt ||
                               item.OpCode == OpCodes.Newobj)
                .Select(item => item.Operand as MethodBase)
                .Where(item => item != null)
                .ToArray();

        internal static bool Calls(MethodBase caller, Type owner, string name) =>
            Calls(caller).Any(call => call.DeclaringType == owner &&
                                      string.Equals(call.Name, name, StringComparison.Ordinal));

        internal static bool AccessesField(MethodBase method, Type owner, string name) =>
            Read(method).Any(item => item.Operand is FieldInfo field &&
                                     field.DeclaringType == owner && field.Name == name);

        internal static int FirstFieldOffset(MethodBase method, Type owner, string name)
        {
            foreach (IlInstruction item in Read(method))
                if (item.Operand is FieldInfo field && field.DeclaringType == owner &&
                    field.Name == name) return item.Offset;
            return -1;
        }

        internal static int FirstCallOffset(MethodBase method, Type owner, string name)
        {
            foreach (IlInstruction item in Read(method))
                if (item.Operand is MethodBase call && call.DeclaringType == owner &&
                    call.Name == name) return item.Offset;
            return -1;
        }

        private static object ReadOperand(MethodBase method, byte[] bytes, ref int index,
            OperandType operandType)
        {
            Module module = method.Module;
            Type[] typeArguments = method.DeclaringType?.GetGenericArguments();
            Type[] methodArguments = method.IsGenericMethod
                ? method.GetGenericArguments() : Type.EmptyTypes;
            switch (operandType)
            {
                case OperandType.InlineNone: return null;
                case OperandType.ShortInlineI: return unchecked((sbyte)bytes[index++]);
                case OperandType.InlineI: return ReadInt32(bytes, ref index);
                case OperandType.InlineI8:
                    long longValue = BitConverter.ToInt64(bytes, index); index += 8; return longValue;
                case OperandType.ShortInlineR:
                    float floatValue = BitConverter.ToSingle(bytes, index); index += 4; return floatValue;
                case OperandType.InlineR:
                    double doubleValue = BitConverter.ToDouble(bytes, index); index += 8; return doubleValue;
                case OperandType.ShortInlineVar: return bytes[index++];
                case OperandType.InlineVar:
                    ushort variable = BitConverter.ToUInt16(bytes, index); index += 2; return variable;
                case OperandType.ShortInlineBrTarget:
                    sbyte shortDelta = unchecked((sbyte)bytes[index++]); return index + shortDelta;
                case OperandType.InlineBrTarget:
                    int delta = ReadInt32(bytes, ref index); return index + delta;
                case OperandType.InlineSwitch:
                    int count = ReadInt32(bytes, ref index);
                    int switchBase = index + count * 4;
                    int[] targets = new int[count];
                    for (int item = 0; item < count; item++)
                        targets[item] = switchBase + ReadInt32(bytes, ref index);
                    return targets;
                case OperandType.InlineString:
                    int stringToken = ReadInt32(bytes, ref index);
                    return Resolve(() => module.ResolveString(stringToken));
                case OperandType.InlineField:
                    int fieldToken = ReadInt32(bytes, ref index);
                    return Resolve(() => module.ResolveField(fieldToken, typeArguments, methodArguments));
                case OperandType.InlineMethod:
                    int methodToken = ReadInt32(bytes, ref index);
                    return Resolve(() => module.ResolveMethod(methodToken, typeArguments, methodArguments));
                case OperandType.InlineType:
                    int typeToken = ReadInt32(bytes, ref index);
                    return Resolve(() => module.ResolveType(typeToken, typeArguments, methodArguments));
                case OperandType.InlineTok:
                    int memberToken = ReadInt32(bytes, ref index);
                    return Resolve(() => module.ResolveMember(memberToken, typeArguments, methodArguments));
                case OperandType.InlineSig: return ReadInt32(bytes, ref index);
                default: throw new NotSupportedException("Unsupported operand " + operandType + ".");
            }
        }

        private static int ReadInt32(byte[] bytes, ref int index)
        {
            int value = BitConverter.ToInt32(bytes, index);
            index += 4;
            return value;
        }

        private static object Resolve(Func<object> resolver)
        {
            try { return resolver(); }
            catch (ArgumentException) { return null; }
        }
    }
}
