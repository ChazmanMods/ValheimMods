using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace RunicSafety.Tests
{
    internal static class IlReader
    {
        private static readonly OpCode[] Single = new OpCode[256];
        private static readonly OpCode[] Double = new OpCode[256];

        static IlReader()
        {
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!(field.GetValue(null) is OpCode code)) continue;
                ushort value = unchecked((ushort)code.Value);
                if (value < 256) Single[value] = code;
                else if ((value & 0xff00) == 0xfe00) Double[value & 0xff] = code;
            }
        }

        internal static bool Calls(MethodInfo method, Type declaringType, string name)
        {
            foreach (MemberInfo member in ReferencedMembers(method))
                if (member is MethodBase called && called.DeclaringType == declaringType && called.Name == name)
                    return true;
            return false;
        }

        internal static IReadOnlyList<MethodBase> Calls(MethodInfo method)
        {
            var calls = new List<MethodBase>();
            foreach (MemberInfo member in ReferencedMembers(method))
                if (member is MethodBase called) calls.Add(called);
            return calls.AsReadOnly();
        }

        internal static int CallIndex(IReadOnlyList<MethodBase> calls, Type type, string name)
        {
            for (int index = 0; index < calls.Count; index++)
                if (calls[index].DeclaringType == type && calls[index].Name == name) return index;
            return -1;
        }

        internal static bool AccessesField(MethodInfo method, Type declaringType, string name)
        {
            foreach (MemberInfo member in ReferencedMembers(method))
                if (member is FieldInfo field && field.DeclaringType == declaringType && field.Name == name)
                    return true;
            return false;
        }

        internal static IReadOnlyList<string> ReferencedStrings(MethodInfo method)
        {
            var strings = new List<string>();
            byte[] bytes = method.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>();
            int index = 0;
            while (index < bytes.Length)
            {
                OpCode opcode = bytes[index++] == 0xfe ? Double[bytes[index++]] : Single[bytes[index - 1]];
                int size = OperandSize(opcode.OperandType, bytes, index);
                if (opcode.OperandType == OperandType.InlineString && index + 4 <= bytes.Length)
                {
                    try
                    {
                        strings.Add(method.Module.ResolveString(BitConverter.ToInt32(bytes, index)));
                    }
                    catch (Exception) { }
                }
                index += size;
            }
            return strings.AsReadOnly();
        }

        internal static IEnumerable<MemberInfo> ReferencedMembers(MethodInfo method)
        {
            byte[] bytes = method.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>();
            int index = 0;
            while (index < bytes.Length)
            {
                OpCode opcode = bytes[index++] == 0xfe ? Double[bytes[index++]] : Single[bytes[index - 1]];
                int size = OperandSize(opcode.OperandType, bytes, index);
                if ((opcode.OperandType == OperandType.InlineMethod ||
                     opcode.OperandType == OperandType.InlineField ||
                     opcode.OperandType == OperandType.InlineType ||
                     opcode.OperandType == OperandType.InlineTok) && index + 4 <= bytes.Length)
                {
                    int token = BitConverter.ToInt32(bytes, index);
                    MemberInfo member = null;
                    try { member = method.Module.ResolveMember(token); }
                    catch (Exception) { }
                    if (member != null) yield return member;
                }
                index += size;
            }
        }

        private static int OperandSize(OperandType type, byte[] bytes, int index)
        {
            switch (type)
            {
                case OperandType.InlineNone: return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: return 1;
                case OperandType.InlineVar: return 2;
                case OperandType.InlineI:
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR: return 4;
                case OperandType.InlineI8:
                case OperandType.InlineR: return 8;
                case OperandType.InlineSwitch:
                    return index + 4 <= bytes.Length ? 4 + BitConverter.ToInt32(bytes, index) * 4 : 0;
                default: return 0;
            }
        }
    }
}
