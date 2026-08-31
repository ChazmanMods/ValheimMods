using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace RunicInteraction.Tests
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
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
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
                int offset = index;
                byte first = bytes[index++];
                OpCode code = first == 0xfe ? TwoByte[bytes[index++]] : OneByte[first];
                if (string.IsNullOrEmpty(code.Name))
                    throw new InvalidOperationException("Unknown IL opcode at " + offset + " in " + method + ".");
                result.Add(new IlInstruction(offset, code, ReadOperand(method, bytes, ref index, code.OperandType)));
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
            Calls(caller).Any(method => method.DeclaringType == owner && method.Name == name);

        internal static bool CallsNamed(MethodBase caller, string ownerName, string name) =>
            Calls(caller).Any(method => method.DeclaringType?.FullName == ownerName && method.Name == name);

        internal static int CallIndex(IReadOnlyList<MethodBase> calls, Type owner, string name, int start = 0)
        {
            for (int index = Math.Max(0, start); index < calls.Count; index++)
                if (calls[index].DeclaringType == owner && calls[index].Name == name) return index;
            return -1;
        }

        internal static bool AccessesField(MethodBase method, Type owner, string name) =>
            Read(method).Any(item => item.Operand is FieldInfo field &&
                                     field.DeclaringType == owner && field.Name == name);

        internal static bool LoadsString(MethodBase method, string text) =>
            Read(method).Any(item => item.OpCode == OpCodes.Ldstr && (string)item.Operand == text);

        internal static bool LoadsFloat(MethodBase method, float value) =>
            Read(method).Any(item => item.OpCode == OpCodes.Ldc_R4 &&
                                     Math.Abs((float)item.Operand - value) < 0.00001f);

        internal static int NewObjectCount(MethodBase method) =>
            Read(method).Count(item => item.OpCode == OpCodes.Newobj);

        private static object ReadOperand(MethodBase method, byte[] bytes, ref int index, OperandType type)
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
                    for (int item = 0; item < count; item++) targets[item] = origin + ReadInt32(bytes, ref index);
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

        private static object Resolve(Func<object> resolver)
        {
            try { return resolver(); }
            catch (ArgumentException) { return null; }
        }
    }
}
