using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using HarmonyLib;
using Mono.Cecil;
using RunicSentinel.Core;
using RunicSentinel.Runtime;

namespace RunicSentinel.Tests
{
    internal static class BuildingLimitsTests
    {
        private const string Managed = "E:/SteamLibrary/steamapps/common/Valheim/valheim_Data/Managed";
        internal static int Run()
        {
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                string file = Path.Combine(Managed, name.Name + ".dll");
                return File.Exists(file) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(file) : null;
            };
            var checks = new (string, Action)[]
            {
                ("vanilla defaults and independent raised/dig limits", Limits),
                ("finite bounded configuration rejects unsafe values", Bounds),
                ("settings wire round trip rejects malformed input", Wire),
                ("four actual terrain IL paths replace both limits and preserve neighboring instructions", Terrain),
                ("dig-stop threshold follows depth and incompatible IL is rejected", Threshold),
                ("vertical support scaling preserves material strengths and horizontal loss", Support),
                ("compiled transport and reset hooks retain host authority", Network),
                ("all building patches coordinate ownership when full and client plugins coexist", Coexistence)
            };
            int failed = 0;
            foreach (var check in checks)
            {
                try { check.Item2(); System.Console.WriteLine("PASS " + check.Item1); }
                catch (Exception e) { failed++; System.Console.WriteLine("FAIL " + check.Item1 + ": " + e); }
            }
            System.Console.WriteLine($"{checks.Length - failed}/{checks.Length} focused checks passed.");
            return failed == 0 ? 0 : 1;
        }
        private static void Require(bool value) { if (!value) throw new Exception("Assertion failed."); }
        private static void Reject(Action action)
        {
            try { action(); } catch (ArgumentException) { return; } catch (InvalidDataException) { return; }
            throw new Exception("Invalid input accepted.");
        }
        private static void Limits()
        {
            Require(BuildingLimits.Vanilla.IsVanilla);
            var custom = new BuildingLimits(2, 24, 32);
            Require(!custom.IsVanilla && custom.RaiseHeight == 24 && custom.DigDepth == 32);
            Require(Math.Clamp(40f, -custom.DigDepth, custom.RaiseHeight) == 24f);
            Require(Math.Clamp(-40f, -custom.DigDepth, custom.RaiseHeight) == -32f);
            Require(Math.Clamp(12f, -8, 8) == 8); // Original limit reproduced numerically, not in-game.
        }
        private static void Bounds()
        {
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -1f, 65f })
            {
                Reject(() => new BuildingLimits(1, invalid, 8));
                Reject(() => new BuildingLimits(1, 8, invalid));
            }
            Reject(() => new BuildingLimits(11, 8, 8));
            Reject(() => new BuildingLimits(float.NaN, 8, 8));
            Require(new BuildingLimits(10, 64, 1).DigDepth == 1);
        }
        private static void Wire()
        {
            var value = new BuildingLimits(3, 20, 40);
            Require(BuildingLimits.Decode(value.Encode()).Encode().SequenceEqual(value.Encode()));
            Reject(() => BuildingLimits.Decode(null));
            Reject(() => BuildingLimits.Decode(new byte[15]));
            Reject(() => BuildingLimits.Decode(new byte[17]));
            byte[] bad = value.Encode(); bad[0] = 2;
            Reject(() => BuildingLimits.Decode(bad));
            bad = value.Encode(); Array.Copy(BitConverter.GetBytes(float.NaN), 0, bad, 4, 4);
            Reject(() => BuildingLimits.Decode(bad));
        }
        private static MethodInfo GameMethod(Type type, string name) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private static CodeInstruction[] Original(Type type, string name)
        {
            // Read installed game IL without invoking Unity or Mono-only Harmony detours in .NET 8.
            using var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(Managed, "assembly_valheim.dll"));
            var method = assembly.MainModule.GetType(type.FullName).Methods.Single(m => m.Name == name);
            var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => o.Value);
            return method.Body.Instructions.Select(i => new CodeInstruction(opcodes[i.OpCode.Value], i.Operand)).ToArray();
        }
        private static void Terrain()
        {
            foreach (var site in new[] { (typeof(TerrainComp), "RaiseTerrain"), (typeof(TerrainComp), "LevelTerrain"), (typeof(TerrainComp), "ApplyToHeightmap"), (typeof(Heightmap), "LevelTerrain") })
            {
                var original = Original(site.Item1, site.Item2);
                var input = original.Select(i => new CodeInstruction(i)).ToArray();
                var patched = SentinelBuildingLimits.TerrainPatch.Transpiler(input, GameMethod(site.Item1, site.Item2)).ToArray();
                var calls = patched.Where(i => i.opcode == OpCodes.Call && i.operand is MethodInfo m && m.DeclaringType == typeof(SentinelBuildingLimits)).ToArray();
                Require(calls.Length == 2);
                Require(calls.Any(i => ((MethodInfo)i.operand).Name == "Raise"));
                Require(calls.Any(i => new[] { "Dig", "NegativeDig" }.Contains(((MethodInfo)i.operand).Name)));
                // All non-limit instructions remain in order, including clamping, writes, paint and smoothing.
                var unchanged = original.Where(i => !(i.opcode == OpCodes.Ldc_R4 || i.opcode == OpCodes.Ldc_R8) || Math.Abs(Convert.ToDouble(i.operand)) != 8).Select(i => (i.opcode, i.operand)).ToArray();
                var remaining = patched.Where(i => !calls.Contains(i)).ToList();
                if (site.Item1 == typeof(Heightmap))
                {
                    // Two float getters need conversion back to the original double stack type.
                    foreach (var call in calls)
                    {
                        int index = Array.IndexOf(patched, call);
                        Require(patched[index + 1].opcode == OpCodes.Conv_R8);
                        remaining.Remove(patched[index + 1]);
                    }
                }
                Require(unchanged.SequenceEqual(remaining.Select(i => (i.opcode, i.operand))));
            }
        }
        private static void Threshold()
        {
            var method = GameMethod(typeof(Heightmap), "AtMaxWorldLevelDepth");
            var patched = SentinelBuildingLimits.TerrainPatch.Transpiler(Original(typeof(Heightmap), method.Name), method).ToArray();
            Require(patched.Count(i => i.operand is MethodInfo m && m.Name == "DigThreshold") == 1);
            try { SentinelBuildingLimits.TerrainPatch.Transpiler(new[] { new CodeInstruction(OpCodes.Ret) }, method).ToArray(); }
            catch (InvalidOperationException) { return; }
            throw new Exception("Incompatible game IL was accepted.");
        }
        private static void Support()
        {
            Require(BuildingLimits.Vanilla.VerticalLoss(0.125f) == 0.125f);
            Require(new BuildingLimits(2, 8, 8).VerticalLoss(0.125f) == 0.0625f);
            var method = GameMethod(typeof(WearNTear), "GetMaterialProperties");
            Require(method.GetParameters().Select(p => p.Name).SequenceEqual(new[] { "maxSupport", "minSupport", "horizontalLoss", "verticalLoss" }));
            var patch = typeof(SentinelBuildingLimits).GetNestedType("SupportPatch", BindingFlags.NonPublic).GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static);
            Require(patch.GetParameters().Length == 1 && patch.GetParameters()[0].Name == "verticalLoss");
            // RunicIntegrity invokes the native solver and color calculation, so it sees the same patched material properties.
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            string integration = File.ReadAllText(Path.Combine(root, "RunicIntegrity", "Plugin.cs"));
            Require(integration.Contains("AccessTools.Method(typeof(WearNTear), \"UpdateSupport\")") && integration.Contains("AccessTools.Method(typeof(WearNTear), \"GetSupportColorValue\")"));
        }
        private static void Network()
        {
            using var assembly = AssemblyDefinition.ReadAssembly(typeof(BuildingLimits).Assembly.Location);
            var type = assembly.MainModule.GetType("RunicSentinel.Runtime.SentinelBuildingLimits");
            bool client = assembly.Name.Name == "RunicSentinelClient";
            if (client)
            {
                Require(!type.NestedTypes.Any(t => t.Name == "AdmissionPatch" || t.Name == "HandshakePatch"));
                Require(!type.Methods.Any(m => m.Name == "Configure"));
                Require(!assembly.MainModule.AssemblyReferences.Any(a => a.Name == "RunicSentinel"));
                string receiver = string.Join("\n", Descendants(type).SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions));
                Require(receiver.Contains("Decode") && receiver.Contains("_serverRpc") && receiver.Contains("ZNet::get_instance") && receiver.Contains("IsServer") && receiver.Contains("bne.un"));
            }
            else
            {
                var admission = type.NestedTypes.Single(t => t.Name == "AdmissionPatch").Methods.Single(m => m.Name == "Prefix");
                string il = string.Join("\n", admission.Body.Instructions);
                Require(il.Contains("IsServer") && il.Contains("IsVanilla") && il.Contains("Contains") && il.Contains("Disconnect"));
                var config = type.Methods.Single(m => m.Name == "Configure");
                Require(config.Body.Instructions.Any(i => i.Operand is string s && s == "Building and Terrain"));
            }
            Require(type.NestedTypes.Any(t => t.Name == "ResetPatch") && type.NestedTypes.Any(t => t.Name == "DestroyPatch") && type.NestedTypes.Any(t => t.Name == "DisconnectPatch"));
        }
        private static void Coexistence()
        {
            using var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(AppContext.BaseDirectory);
            using var assembly = AssemblyDefinition.ReadAssembly(typeof(BuildingLimits).Assembly.Location, new ReaderParameters { AssemblyResolver = resolver });
            var type = assembly.MainModule.GetType("RunicSentinel.Runtime.SentinelBuildingLimits");
            foreach (var patch in type.NestedTypes.Where(t => t.CustomAttributes.Any(a => a.AttributeType.Name == "HarmonyPatch")))
            {
                var prepare = patch.Methods.Single(m => m.Name == "Prepare");
                Require(prepare.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "ShouldInstall"));
            }
            if (assembly.Name.Name == "RunicSentinelClient")
            {
                Require(SentinelBuildingLimits.ShouldInstall()); // Full plugin absent in this test process.
                string owner = string.Join("\n", type.Methods.Single(m => m.Name == "ShouldInstall").Body.Instructions);
                Require(owner.Contains("BuildingLimitsAvailable") && owner.Contains("GetValue"));
                var plugin = assembly.MainModule.GetType("RunicSentinelClient.Plugin");
                Require(plugin.CustomAttributes.Any(a => a.AttributeType.Name == "BepInDependency" && (string)a.ConstructorArguments[0].Value == "chazman.RunicSentinel"));
            }
        }
        private static System.Collections.Generic.IEnumerable<TypeDefinition> Descendants(TypeDefinition type)
        {
            yield return type;
            foreach (var child in type.NestedTypes)
                foreach (var item in Descendants(child)) yield return item;
        }
    }
}


