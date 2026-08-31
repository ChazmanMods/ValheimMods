using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace RunicBuildCamera.Tests
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

        public override string ToString() =>
            $"IL_{Offset:x4}: {OpCode.Name}" + (Operand == null ? string.Empty : " " + Operand);
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
                if (value < 0x100)
                    OneByte[value] = opCode;
                else if ((value & 0xff00) == 0xfe00)
                    TwoByte[value & 0xff] = opCode;
            }
        }

        internal static IReadOnlyList<IlInstruction> Read(MethodBase method)
        {
            MethodBody body = TestAssert.NotNull(
                method.GetMethodBody(), method.DeclaringType?.FullName + "." + method.Name + " has no IL body.");
            byte[] il = TestAssert.NotNull(
                body.GetILAsByteArray(), method.DeclaringType?.FullName + "." + method.Name + " has no IL bytes.");
            var instructions = new List<IlInstruction>();
            int index = 0;
            while (index < il.Length)
            {
                int offset = index;
                OpCode opCode;
                byte first = il[index++];
                if (first == 0xfe)
                    opCode = TwoByte[il[index++]];
                else
                    opCode = OneByte[first];
                if (string.IsNullOrEmpty(opCode.Name))
                    throw new InvalidOperationException($"Unknown IL opcode at {offset:x4} in {method}.");

                object operand = ReadOperand(method, il, ref index, opCode.OperandType);
                instructions.Add(new IlInstruction(offset, opCode, operand));
            }
            return instructions;
        }

        internal static IReadOnlyList<MethodBase> Calls(MethodBase method) =>
            Read(method)
                .Where(item => item.OpCode == OpCodes.Call || item.OpCode == OpCodes.Callvirt ||
                               item.OpCode == OpCodes.Newobj)
                .Select(item => item.Operand as MethodBase)
                .Where(item => item != null)
                .ToArray();

        internal static bool Calls(MethodBase caller, Type owner, string methodName) =>
            Calls(caller).Any(method =>
                method.DeclaringType == owner &&
                string.Equals(method.Name, methodName, StringComparison.Ordinal));

        internal static int CallIndex(
            IReadOnlyList<MethodBase> calls,
            Type owner,
            string methodName,
            int startAt = 0)
        {
            for (int index = Math.Max(0, startAt); index < calls.Count; index++)
            {
                MethodBase method = calls[index];
                if (method.DeclaringType == owner &&
                    string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    return index;
            }
            return -1;
        }

        internal static bool LoadsString(MethodBase method, string value) =>
            Read(method).Any(item =>
                item.OpCode == OpCodes.Ldstr && string.Equals(item.Operand as string, value, StringComparison.Ordinal));

        internal static bool AccessesField(MethodBase method, Type owner, string fieldName) =>
            Read(method).Any(item =>
                item.Operand is FieldInfo field && field.DeclaringType == owner &&
                string.Equals(field.Name, fieldName, StringComparison.Ordinal));

        internal static bool AccessesFieldNamed(MethodBase method, string fieldName) =>
            Read(method).Any(item =>
                item.Operand is FieldInfo field &&
                string.Equals(field.Name, fieldName, StringComparison.Ordinal));

        internal static bool ReferencesMethod(
            MethodBase caller,
            Type owner,
            string methodName) =>
            Read(caller).Any(item =>
                item.Operand is MethodBase method && method.DeclaringType == owner &&
                string.Equals(method.Name, methodName, StringComparison.Ordinal));

        internal static bool LoadsInt32(MethodBase method, int value) =>
            Read(method).Any(item => TryGetInt32Constant(item, out int actual) && actual == value);

        internal static bool ReferencesType(MethodBase method, Type type) =>
            Read(method).Any(item =>
                item.Operand is Type operandType && operandType == type ||
                item.Operand is MemberInfo member && member.DeclaringType == type);

        private static bool TryGetInt32Constant(IlInstruction instruction, out int value)
        {
            value = 0;
            if (instruction.OpCode == OpCodes.Ldc_I4_M1) { value = -1; return true; }
            if (instruction.OpCode == OpCodes.Ldc_I4_0) { value = 0; return true; }
            if (instruction.OpCode == OpCodes.Ldc_I4_1) { value = 1; return true; }
            if (instruction.OpCode == OpCodes.Ldc_I4_2) { value = 2; return true; }
            if (instruction.OpCode == OpCodes.Ldc_I4_3) { value = 3; return true; }
            if (instruction.OpCode == OpCodes.Ldc_I4_4) { value = 4; return true; }
            if (instruction.OpCode == OpCodes.Ldc_I4_5) { value = 5; return true; }
            if (instruction.OpCode == OpCodes.Ldc_I4_6) { value = 6; return true; }
            if (instruction.OpCode == OpCodes.Ldc_I4_7) { value = 7; return true; }
            if (instruction.OpCode == OpCodes.Ldc_I4_8) { value = 8; return true; }
            if (instruction.OpCode == OpCodes.Ldc_I4 ||
                instruction.OpCode == OpCodes.Ldc_I4_S)
            {
                value = Convert.ToInt32(instruction.Operand);
                return true;
            }
            return false;
        }

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
                case OperandType.InlineNone:
                    return null;
                case OperandType.ShortInlineI:
                    return unchecked((sbyte)il[index++]);
                case OperandType.InlineI:
                    return ReadInt32(il, ref index);
                case OperandType.InlineI8:
                    long longValue = BitConverter.ToInt64(il, index);
                    index += 8;
                    return longValue;
                case OperandType.ShortInlineR:
                    float floatValue = BitConverter.ToSingle(il, index);
                    index += 4;
                    return floatValue;
                case OperandType.InlineR:
                    double doubleValue = BitConverter.ToDouble(il, index);
                    index += 8;
                    return doubleValue;
                case OperandType.ShortInlineVar:
                    return il[index++];
                case OperandType.InlineVar:
                    ushort variable = BitConverter.ToUInt16(il, index);
                    index += 2;
                    return variable;
                case OperandType.ShortInlineBrTarget:
                    sbyte shortDelta = unchecked((sbyte)il[index++]);
                    return index + shortDelta;
                case OperandType.InlineBrTarget:
                    int delta = ReadInt32(il, ref index);
                    return index + delta;
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
                    return Resolve(() => module.ResolveField(
                        fieldToken, typeArguments, methodArguments));
                case OperandType.InlineMethod:
                    int methodToken = ReadInt32(il, ref index);
                    return Resolve(() => module.ResolveMethod(
                        methodToken, typeArguments, methodArguments));
                case OperandType.InlineType:
                    int typeToken = ReadInt32(il, ref index);
                    return Resolve(() => module.ResolveType(
                        typeToken, typeArguments, methodArguments));
                case OperandType.InlineTok:
                    int memberToken = ReadInt32(il, ref index);
                    return Resolve(() => module.ResolveMember(
                        memberToken, typeArguments, methodArguments));
                case OperandType.InlineSig:
                    return ReadInt32(il, ref index);
                default:
                    throw new NotSupportedException("Unsupported operand type " + operandType + ".");
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
            try
            {
                return resolver();
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
