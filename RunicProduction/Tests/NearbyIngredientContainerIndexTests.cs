using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RunicProduction.Integration;
using UnityEngine;

namespace RunicProduction.Tests
{
    internal static class NearbyIngredientContainerIndexTests
    {
        private static readonly IReadOnlyDictionary<short, OpCode> OpCodesByValue =
            typeof(OpCodes)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.FieldType == typeof(OpCode))
                .Select(field => (OpCode)field.GetValue(null))
                .ToDictionary(opcode => opcode.Value);

        internal static IReadOnlyList<KeyValuePair<string, Action>> Cases() =>
            new List<KeyValuePair<string, Action>>
            {
                Case("nearby ingredient discovery bounds and ordering are exact", BoundsAndOrderingAreExact),
                Case("nearby ingredient discovery lifecycle is event maintained", LifecycleIsEventMaintained),
                Case("unchanged container refresh retains spatial membership", UnchangedRefreshRetainsMembership),
                Case("nearby ingredient discovery rejects mobile container roots structurally", MobileRootsAreRejectedStructurally),
                Case("nearby ingredient configuration is explicit and opt in", ConfigurationSurfaceIsExplicit)
            }.AsReadOnly();

        private static void BoundsAndOrderingAreExact()
        {
            Equal(10f, NearbyIngredientContainerIndex.CellSizeMeters);
            Equal(30f, NearbyIngredientContainerIndex.HardMaximumRadiusMeters);
            Equal(64, NearbyIngredientContainerIndex.HardMaximumSourceChests);

            var lowUser = new ZDOID(2L, 8U);
            var highUser = new ZDOID(3L, 1U);
            var lowId = new ZDOID(2L, 7U);
            Require(NearbyIngredientContainerIndex.CompareKeys(
                1f, highUser, 2f, lowUser) < 0);
            Require(NearbyIngredientContainerIndex.CompareKeys(
                2f, lowUser, 2f, highUser) < 0);
            Require(NearbyIngredientContainerIndex.CompareKeys(
                2f, lowId, 2f, lowUser) < 0);
            Equal(0, NearbyIngredientContainerIndex.CompareKeys(
                2f, lowUser, 2f, lowUser));

            var result = new NearbyIngredientContainerQueryResult(
                Array.Empty<NearbyIngredientContainerCandidate>(),
                candidateCount: 3,
                truncated: true);
            Equal(0, result.Candidates.Count);
            Equal(3, result.CandidateCount);
            Require(result.Truncated);
        }

        private static void LifecycleIsEventMaintained()
        {
            Assembly assembly = typeof(NearbyIngredientContainerIndex).Assembly;
            Type awakePatch = assembly.GetType(
                "RunicProduction.Integration.NearbyIngredientContainerAwakePatch", true);
            Type destroyedPatch = assembly.GetType(
                "RunicProduction.Integration.NearbyIngredientContainerDestroyedPatch", true);
            Type refreshPatch = assembly.GetType(
                "RunicProduction.Integration.NearbyIngredientContainerRefreshPatch", true);
            MethodInfo register = Method(typeof(NearbyIngredientContainerIndex), "Register");
            MethodInfo unregister = Method(typeof(NearbyIngredientContainerIndex), "Unregister");
            MethodInfo seed = Method(typeof(NearbyIngredientContainerIndex), "SeedLoadedContainers");
            MethodInfo clear = Method(typeof(NearbyIngredientContainerIndex), "Clear");

            Require(Calls(Method(awakePatch, "Postfix"), register));
            MethodInfo destroyed = Method(destroyedPatch, "Postfix");
            Require(Calls(destroyed, unregister));
            ParameterInfo runOriginal = destroyed.GetParameters().Single(parameter =>
                string.Equals(parameter.Name, "__runOriginal", StringComparison.Ordinal));
            Require(runOriginal.ParameterType == typeof(bool));
            Require(Calls(Method(refreshPatch, "Postfix"), register));

            Type plugin = typeof(ProductionConfig).Assembly.GetType("RunicProduction.Plugin", true);
            MethodInfo awake = Method(plugin, "Awake");
            MethodInfo cleanup = Method(plugin, "Cleanup");
            int patchAll = CallOffset(awake, typeof(Harmony), nameof(Harmony.PatchAll));
            int seedOffset = CallOffset(awake, seed);
            Require(patchAll >= 0 && seedOffset > patchAll);
            Require(Calls(cleanup, clear));

            Require(Calls(seed, clear));
            Require(ReferencedMembers(seed).OfType<MethodInfo>().Any(method =>
                string.Equals(method.Name, nameof(UnityEngine.Object.FindObjectsByType),
                    StringComparison.Ordinal)));
        }

        private static void MobileRootsAreRejectedStructurally()
        {
            MethodInfo check = Method(
                typeof(NearbyIngredientContainerIndex), "IsStaticNonWagon");
            MemberInfo[] members = ReferencedMembers(check).ToArray();
            Require(members.OfType<FieldInfo>().Any(field =>
                field.DeclaringType == typeof(Container) &&
                string.Equals(field.Name, "m_wagon", StringComparison.Ordinal)));
            Require(members.OfType<MethodInfo>().Any(method =>
                string.Equals(method.Name, "GetComponentInParent", StringComparison.Ordinal) &&
                method.IsGenericMethod &&
                method.GetGenericArguments().SequenceEqual(new[] { typeof(Rigidbody) })));

            MethodInfo create = Method(
                typeof(NearbyIngredientContainerIndex), "TryCreateCandidate");
            Require(Calls(create, check));
            Require(!ReferencedMembers(Method(
                    typeof(NearbyIngredientContainerIndex), "Query"))
                .OfType<MethodInfo>()
                .Any(method => method.DeclaringType == typeof(Container) &&
                               string.Equals(method.Name, "GetInventory", StringComparison.Ordinal)));
        }

        private static void UnchangedRefreshRetainsMembership()
        {
            MethodInfo register = Method(typeof(NearbyIngredientContainerIndex), "Register");
            MethodInfo contains = Method(typeof(NearbyIngredientContainerIndex), "ContainsExact");
            MethodInfo unregister = Method(typeof(NearbyIngredientContainerIndex), "UnregisterLocked");
            int containsOffset = CallOffset(register, contains);
            int[] unregisterOffsets = References(register)
                .Where(reference => reference.Member is MethodBase called &&
                    called.Module == unregister.Module &&
                    called.MetadataToken == unregister.MetadataToken)
                .Select(reference => reference.Offset)
                .ToArray();
            Require(containsOffset >= 0 && unregisterOffsets.Any(offset => offset > containsOffset));
        }

        private static void ConfigurationSurfaceIsExplicit()
        {
            PropertyInfo enabled = typeof(ProductionConfig).GetProperty(
                "RecipeNearbyIngredientsEnabled",
                BindingFlags.Static | BindingFlags.NonPublic);
            PropertyInfo maximum = typeof(ProductionConfig).GetProperty(
                "RecipeNearbyMaximumSourceChests",
                BindingFlags.Static | BindingFlags.NonPublic);
            Require(enabled != null && maximum != null);

            MethodInfo bind = Method(typeof(ProductionConfig), "Bind");
            var english = typeof(ProductionConfig).Assembly.GetType("Runic.Localization.RunicText", true).GetMethod("English", BindingFlags.Static | BindingFlags.NonPublic);
            string[] strings = ReferencedStrings(bind).Select(value => value.StartsWith("text_", StringComparison.Ordinal) ? (string)english.Invoke(null, new object[]{value}) : value).ToArray();
            Require(strings.Contains("Recipe Nearby Ingredients", StringComparer.Ordinal));
            Require(strings.Contains("Enabled", StringComparer.Ordinal));
            Require(strings.Contains("MaximumSourceChests", StringComparer.Ordinal));
            Require(strings.Any(value => value.IndexOf(
                "False preserves exact designated-Input-only behavior",
                StringComparison.Ordinal) >= 0));
            Require(strings.Any(value => value.IndexOf(
                "Links.MaximumLinkRange", StringComparison.Ordinal) >= 0));
        }

        private static MethodInfo Method(Type type, string name) =>
            type.GetMethod(
                name,
                BindingFlags.Static | BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic) ??
            throw new MissingMethodException(type.FullName, name);

        private static bool Calls(MethodInfo caller, MethodInfo callee) =>
            CallOffset(caller, callee) >= 0;

        private static int CallOffset(MethodInfo caller, MethodInfo callee)
        {
            foreach ((int Offset, MemberInfo Member) reference in References(caller))
                if (reference.Member is MethodBase called &&
                    called.Module == callee.Module &&
                    called.MetadataToken == callee.MetadataToken)
                    return reference.Offset;
            return -1;
        }

        private static int CallOffset(MethodInfo caller, Type declaringType, string methodName)
        {
            foreach ((int Offset, MemberInfo Member) reference in References(caller))
                if (reference.Member is MethodBase called &&
                    called.DeclaringType == declaringType &&
                    string.Equals(called.Name, methodName, StringComparison.Ordinal))
                    return reference.Offset;
            return -1;
        }

        private static IEnumerable<MemberInfo> ReferencedMembers(MethodInfo method) =>
            References(method).Select(reference => reference.Member)
                .Where(member => member != null);

        private static IEnumerable<string> ReferencedStrings(MethodInfo method)
        {
            MethodBody body = method.GetMethodBody();
            byte[] il = body?.GetILAsByteArray() ?? Array.Empty<byte>();
            int offset = 0;
            while (offset < il.Length)
            {
                short value = il[offset++] == 0xfe
                    ? (short)(0xfe00 | il[offset++])
                    : (short)il[offset - 1];
                if (!OpCodesByValue.TryGetValue(value, out OpCode opcode))
                    throw new InvalidOperationException("Unknown IL opcode.");
                int operandOffset = offset;
                int operandSize = OperandSize(opcode, il, operandOffset);
                if (opcode == OpCodes.Ldstr && operandSize == 4)
                    yield return method.Module.ResolveString(
                        BitConverter.ToInt32(il, operandOffset));
                offset += operandSize;
            }
        }

        private static IEnumerable<(int Offset, MemberInfo Member)> References(
            MethodInfo method)
        {
            MethodBody body = method.GetMethodBody();
            byte[] il = body?.GetILAsByteArray() ?? Array.Empty<byte>();
            int offset = 0;
            while (offset < il.Length)
            {
                int instructionOffset = offset;
                short value = il[offset++] == 0xfe
                    ? (short)(0xfe00 | il[offset++])
                    : (short)il[offset - 1];
                if (!OpCodesByValue.TryGetValue(value, out OpCode opcode))
                    throw new InvalidOperationException("Unknown IL opcode.");
                int operandOffset = offset;
                int operandSize = OperandSize(opcode, il, operandOffset);
                MemberInfo member = null;
                if (operandSize == 4 &&
                    (opcode.OperandType == OperandType.InlineField ||
                     opcode.OperandType == OperandType.InlineMethod ||
                     opcode.OperandType == OperandType.InlineTok ||
                     opcode.OperandType == OperandType.InlineType))
                {
                    int token = BitConverter.ToInt32(il, operandOffset);
                    try
                    {
                        member = method.Module.ResolveMember(
                            token,
                            method.DeclaringType?.GetGenericArguments(),
                            method.GetGenericArguments());
                    }
                    catch (ArgumentException) { }
                }
                if (member != null) yield return (instructionOffset, member);
                offset += operandSize;
            }
        }

        private static int OperandSize(OpCode opcode, byte[] il, int offset)
        {
            switch (opcode.OperandType)
            {
                case OperandType.InlineNone: return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: return 1;
                case OperandType.InlineVar: return 2;
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineI:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR: return 4;
                case OperandType.InlineI8:
                case OperandType.InlineR: return 8;
                case OperandType.InlineSwitch:
                    int count = BitConverter.ToInt32(il, offset);
                    return checked(4 + count * 4);
                default: throw new InvalidOperationException("Unsupported IL operand type.");
            }
        }

        private static KeyValuePair<string, Action> Case(string name, Action action) =>
            new KeyValuePair<string, Action>(name, action);

        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(
                    "Expected " + expected + " but found " + actual + ".");
        }

        private static void Require(bool condition)
        {
            if (!condition) throw new InvalidOperationException("Assertion failed.");
        }
    }
}
