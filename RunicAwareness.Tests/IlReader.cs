using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace RunicAwareness.Tests
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
        private static readonly OpCode[] OneByte = new OpCode[0x100];
        private static readonly OpCode[] TwoByte = new OpCode[0x100];

        static IlReader()
        {
            foreach (FieldInfo field in typeof(OpCodes).GetFields(
                         BindingFlags.Public | BindingFlags.Static))
            {
                if (!(field.GetValue(null) is OpCode opCode)) continue;
                ushort value = unchecked((ushort)opCode.Value);
                if (value < 0x100) OneByte[value] = opCode;
                else if ((value & 0xff00) == 0xfe00) TwoByte[value & 0xff] = opCode;
            }
        }

        internal static IReadOnlyList<IlInstruction> Read(MethodBase method)
        {
            MethodBody body = TestAssert.NotNull(
                method.GetMethodBody(), method.DeclaringType?.FullName + "." + method.Name +
                                        " has no IL body.");
            byte[] il = TestAssert.NotNull(body.GetILAsByteArray(), "Method has no IL bytes.");
            var instructions = new List<IlInstruction>();
            int index = 0;
            while (index < il.Length)
            {
                int offset = index;
                byte first = il[index++];
                OpCode opCode = first == 0xfe ? TwoByte[il[index++]] : OneByte[first];
                if (string.IsNullOrEmpty(opCode.Name))
                    throw new InvalidOperationException("Unknown opcode at " + offset + ".");
                instructions.Add(new IlInstruction(
                    offset, opCode, ReadOperand(method, il, ref index, opCode.OperandType)));
            }
            return instructions;
        }

        internal static IReadOnlyList<MethodBase> Calls(MethodBase method) =>
            Read(method)
                .Where(instruction => instruction.OpCode == OpCodes.Call ||
                                      instruction.OpCode == OpCodes.Callvirt ||
                                      instruction.OpCode == OpCodes.Newobj)
                .Select(instruction => instruction.Operand as MethodBase)
                .Where(call => call != null)
                .ToArray();

        internal static bool Calls(MethodBase caller, Type owner, string name) =>
            Calls(caller).Any(call => call.DeclaringType == owner &&
                                      string.Equals(call.Name, name, StringComparison.Ordinal));

        internal static bool AccessesField(MethodBase method, Type owner, string name) =>
            Read(method).Any(instruction => instruction.Operand is FieldInfo field &&
                                            field.DeclaringType == owner &&
                                            string.Equals(field.Name, name, StringComparison.Ordinal));

        internal static bool HasNewArray(MethodBase method) =>
            Read(method).Any(instruction => instruction.OpCode == OpCodes.Newarr);

        private static object ReadOperand(
            MethodBase method,
            byte[] il,
            ref int index,
            OperandType operandType)
        {
            Module module = method.Module;
            Type[] typeArguments = method.DeclaringType?.GetGenericArguments();
            Type[] methodArguments = method.IsGenericMethod
                ? method.GetGenericArguments()
                : Type.EmptyTypes;
            switch (operandType)
            {
                case OperandType.InlineNone: return null;
                case OperandType.ShortInlineI: return unchecked((sbyte)il[index++]);
                case OperandType.InlineI: return ReadInt32(il, ref index);
                case OperandType.InlineI8:
                    long longValue = BitConverter.ToInt64(il, index); index += 8; return longValue;
                case OperandType.ShortInlineR:
                    float floatValue = BitConverter.ToSingle(il, index); index += 4; return floatValue;
                case OperandType.InlineR:
                    double doubleValue = BitConverter.ToDouble(il, index); index += 8; return doubleValue;
                case OperandType.ShortInlineVar: return il[index++];
                case OperandType.InlineVar:
                    ushort variable = BitConverter.ToUInt16(il, index); index += 2; return variable;
                case OperandType.ShortInlineBrTarget:
                    sbyte shortDelta = unchecked((sbyte)il[index++]); return index + shortDelta;
                case OperandType.InlineBrTarget:
                    int delta = ReadInt32(il, ref index); return index + delta;
                case OperandType.InlineSwitch:
                    int count = ReadInt32(il, ref index);
                    int switchBase = index + count * 4;
                    int[] targets = new int[count];
                    for (int item = 0; item < count; item++)
                        targets[item] = switchBase + ReadInt32(il, ref index);
                    return targets;
                case OperandType.InlineString:
                    int stringToken = ReadInt32(il, ref index);
                    return Resolve(() => module.ResolveString(stringToken));
                case OperandType.InlineField:
                    int fieldToken = ReadInt32(il, ref index);
                    return Resolve(() => module.ResolveField(fieldToken, typeArguments, methodArguments));
                case OperandType.InlineMethod:
                    int methodToken = ReadInt32(il, ref index);
                    return Resolve(() => module.ResolveMethod(methodToken, typeArguments, methodArguments));
                case OperandType.InlineType:
                    int typeToken = ReadInt32(il, ref index);
                    return Resolve(() => module.ResolveType(typeToken, typeArguments, methodArguments));
                case OperandType.InlineTok:
                    int memberToken = ReadInt32(il, ref index);
                    return Resolve(() => module.ResolveMember(memberToken, typeArguments, methodArguments));
                case OperandType.InlineSig: return ReadInt32(il, ref index);
                default: throw new NotSupportedException("Unsupported operand " + operandType + ".");
            }
        }

        private static int ReadInt32(byte[] il, ref int index)
        {
            int value = BitConverter.ToInt32(il, index);
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
