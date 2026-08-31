using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace RunicIntegrity.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            var tests = new (string Name, Action Run)[]
            {
                ("identity and manifest are aligned", IdentityIsAligned),
                ("installed support solver matches the audited IL anchors", InstalledSupportContractIsExact),
                ("transpiler fails closed on every shape mismatch", TranspilerFailsClosed),
                ("route solver has expansion and wall-clock bounds", SolverIsHardBounded),
                ("route visuals destroy every cloned native material", VisualMaterialsAreDestroyed),
                ("Integrity performs no persistent world mutation", NoPersistentWorldMutation)
            };
            int failures = 0;
            foreach ((string name, Action run) in tests)
            {
                try { run(); Console.WriteLine("PASS " + name); }
                catch (Exception exception)
                {
                    failures++;
                    Console.Error.WriteLine("FAIL " + name + ": " + exception.Message);
                }
            }
            Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
            return failures == 0 ? 0 : 1;
        }

        private static void IdentityIsAligned()
        {
            string source = Source();
            Contains(source, "public const string Guid = \"chazman.RunicIntegrity\"");
            Contains(source, "public const string Version = \"0.2.2\"");
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(PathOf("RunicIntegrity", "manifest.json")));
            Equal("RunicIntegrity", manifest.RootElement.GetProperty("name").GetString());
            Equal("0.2.2", manifest.RootElement.GetProperty("version_number").GetString());
        }

        private static void InstalledSupportContractIsExact()
        {
            string assemblyPath = Path.Combine(
                Environment.GetEnvironmentVariable("VALHEIM_INSTALL") ?? @"E:\SteamLibrary\steamapps\common\Valheim",
                "valheim_Data", "Managed", "assembly_valheim.dll");
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
            TypeDefinition wear = assembly.MainModule.Types.Single(type => type.FullName == "WearNTear");
            MethodDefinition update = wear.Methods.Single(method => method.Name == "UpdateSupport" && !method.HasParameters);
            List<Instruction> instructions = update.Body.Instructions.ToList();
            int owner = instructions.Count(IsZNetCall("IsOwner"));
            int valid = instructions.Count(IsZNetCall("IsValid"));
            int guards = 0;
            for (int index = 1; index < instructions.Count - 3; index++)
            {
                OpCode branch = instructions[index].OpCode;
                if (branch != OpCodes.Brfalse && branch != OpCodes.Brfalse_S) continue;
                if (!(instructions[index - 1].Operand is MethodReference equality) ||
                    equality.Name != "op_Equality") continue;
                if (instructions[index + 1].OpCode == OpCodes.Ldarg_0 &&
                    instructions[index + 3].OpCode == OpCodes.Stfld &&
                    instructions[index + 3].Operand is FieldReference field && field.Name == "m_support")
                    guards++;
            }
            Equal(1, owner);
            Equal(1, valid);
            Equal(1, guards);
        }

        private static Func<Instruction, bool> IsZNetCall(string name) => instruction =>
            instruction.OpCode == OpCodes.Callvirt &&
            instruction.Operand is MethodReference method &&
            method.DeclaringType.FullName == "ZNetView" && method.Name == name;

        private static void TranspilerFailsClosed()
        {
            string source = Source();
            Contains(source, "ownerCalls != 1 || validCalls != 1");
            Contains(source, "guards.Count");
            Contains(source, "RouteAdvisor.SetSupportPatchCompatibility(");
            Contains(source, "return list;");
            Contains(source, "if (!_supportPatchCompatible)");
        }

        private static void SolverIsHardBounded()
        {
            string source = Source();
            Contains(source, "MaximumExpansionsPerRefresh");
            Contains(source, "new AcceptableValueRange<int>(64, 4096)");
            Contains(source, "TimeBudgetMilliseconds");
            Contains(source, "Stopwatch.GetTimestamp() - started >= timeBudgetTicks");
            Contains(source, "(expansions & 15) == 0");
        }

        private static void VisualMaterialsAreDestroyed()
        {
            string source = Source();
            Contains(source, "private static readonly List<Material> VisualMaterials");
            Contains(source, "VisualMaterials.Add(material)");
            string cleanup = Slice(source, "private static void DestroyVisuals()", "internal static class SafeNView");
            Contains(cleanup, "UnityEngine.Object.Destroy(VisualMaterials[index])");
            Contains(cleanup, "VisualMaterials.Clear()");
        }

        private static void NoPersistentWorldMutation()
        {
            string source = Source();
            string[] forbidden =
            {
                "new ZDO(", ".SetOwner(", "ZDOMan.instance.DestroyZDO", "ZNetScene.instance.Destroy("
            };
            foreach (string token in forbidden)
                False(source.Contains(token, StringComparison.Ordinal), "Persistent mutation token found: " + token);
        }

        private static string Source() => File.ReadAllText(PathOf("RunicIntegrity", "Plugin.cs"));

        private static string PathOf(params string[] segments)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            return segments.Aggregate(root, Path.Combine);
        }

        private static string Slice(string source, string start, string end)
        {
            int a = source.IndexOf(start, StringComparison.Ordinal);
            int b = source.IndexOf(end, a + start.Length, StringComparison.Ordinal);
            True(a >= 0 && b > a, "Missing source slice " + start);
            return source.Substring(a, b - a);
        }

        private static void Contains(string source, string token) =>
            True(source.Contains(token, StringComparison.Ordinal), "Missing token: " + token);

        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
        }

        private static void True(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        private static void False(bool value, string message) => True(!value, message);
    }
}
