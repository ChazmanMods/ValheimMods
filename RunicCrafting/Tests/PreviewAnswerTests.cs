using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RunicCrafting.Domain;

namespace RunicCrafting.Tests
{
    internal static class PreviewAnswerTests
    {
        internal static void AnswersExpireAtQuarterSecondWithoutSliding()
        {
            var cache = new PreviewAnswerCache<string, object>();
            object value = new object();
            TestAssert.True(cache.BeginWindow(10));
            cache.Store("yes", value, 10, cache.Epoch);
            for (int i = 0; i < 25; i++)
            {
                TestAssert.False(cache.BeginWindow(10 + i * 0.01));
                TestAssert.True(cache.TryGet("yes", 10 + i * 0.01, out object found));
                TestAssert.True(ReferenceEquals(value, found));
            }
            TestAssert.False(cache.TryGet("yes", 10.25, out _));
            TestAssert.True(cache.BeginWindow(10.25));
            TestAssert.Equal(0, cache.Count);
            cache.Store("no", new object(), 10.25, cache.Epoch);
            cache.Expire(10.5);
            TestAssert.Equal(0, cache.Count);
            TestAssert.Equal(0, cache.WatchedCount);
        }

        internal static void OnlyWatchedChangesInvalidateAndEpochRejectsRaces()
        {
            var cache = new PreviewAnswerCache<int, object>();
            var chest = new object(); var unrelated = new object();
            cache.BeginWindow(0); cache.Watch(chest);
            long epoch = cache.Epoch;
            cache.Store(1, new object(), 0, epoch);
            cache.Changed(unrelated);
            TestAssert.True(cache.TryGet(1, 0.1, out _));
            cache.Changed(chest);
            TestAssert.False(cache.TryGet(1, 0.1, out _));
            cache.Store(1, new object(), 0.1, epoch);
            TestAssert.Equal(0, cache.Count);
            cache.Store(1, new object(), 0.1, cache.Epoch);
            TestAssert.Equal(1, cache.Count);
            cache.Invalidate();
            TestAssert.Equal(0, cache.Count);
        }

        internal static void AnswerAndDependencyBoundsCannotReturnUntrackedResults()
        {
            var cache = new PreviewAnswerCache<int, object>(2, 2);
            cache.BeginWindow(1);
            cache.Store(1, new object(), 1, cache.Epoch);
            cache.Store(2, new object(), 1, cache.Epoch);
            cache.Store(3, new object(), 1, cache.Epoch);
            TestAssert.Equal(2, cache.Count);
            var dependency = new object();
            cache.Watch(dependency); cache.Watch(dependency); cache.Watch(new object());
            TestAssert.Equal(2, cache.WatchedCount);
            cache.Watch(new object());
            TestAssert.Equal(0, cache.Count);
            cache.Store(1, new object(), 1.1, cache.Epoch);
            TestAssert.False(cache.TryGet(1, 1.1, out _));
            cache.BeginWindow(1.25);
            cache.Store(1, new object(), 1.25, cache.Epoch);
            TestAssert.True(cache.TryGet(1, 1.25, out _));
            cache.BeginWindow(0); // Monotonic clock discontinuity must not extend stale evidence.
            TestAssert.Equal(0, cache.Count);
        }

        internal static void RequirementKeysAreCanonicalBoundedAndCollisionSafe()
        {
            static string Signature(params MaterialRequirement[] reqs)
            {
                TestAssert.True(PreviewAnswerCache<string, object>.TrySignature(reqs, out string value));
                return value;
            }
            TestAssert.Equal(Signature(new MaterialRequirement("Wood", 5), new MaterialRequirement("Stone", 2)),
                Signature(new MaterialRequirement("Stone", 2), new MaterialRequirement("Wood", 2), new MaterialRequirement("Wood", 3)));
            TestAssert.False(Signature(new MaterialRequirement("Wood", 5)) == Signature(new MaterialRequirement("Wood", 4)));
            TestAssert.False(Signature(new MaterialRequirement("A:1;B", 2)) == Signature(new MaterialRequirement("A", 1), new MaterialRequirement("B", 2)));
            TestAssert.False(PreviewAnswerCache<string, object>.TrySignature(null, out _));
            TestAssert.False(PreviewAnswerCache<string, object>.TrySignature(Array.Empty<MaterialRequirement>(), out _));
            TestAssert.False(PreviewAnswerCache<string, object>.TrySignature(new[] { new MaterialRequirement("Wood", int.MaxValue), new MaterialRequirement("Wood", 1) }, out _));
            TestAssert.False(PreviewAnswerCache<string, object>.TrySignature(Enumerable.Repeat(new MaterialRequirement("Wood", 1), 257), out _));
            TestAssert.False(PreviewAnswerCache<string, object>.TrySignature(new[] { new MaterialRequirement(new string('x', 257), 1) }, out _));
        }

        internal static void ReleasedIlKeepsCraftClicksOutsideUiMemo()
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            using var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(root, "bin", "Release", "netstandard2.1", "RunicCrafting.dll"));
            var runtime = assembly.MainModule.Types.Single(t => t.Name == "CraftingRuntime");
            var check = runtime.Methods.Single(m => m.Name == "CheckAvailability");
            TestAssert.True(check.Parameters.Last().HasConstant);
            TestAssert.Equal(false, (bool)check.Parameters.Last().Constant);
            var before = runtime.Methods.Single(m => m.Name == "BeforeCraft");
            var call = before.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "CheckAvailability");
            TestAssert.Equal(Code.Ldc_I4_0, call.Previous.OpCode.Code);
            TestAssert.False(before.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "CheckUiAvailability"));
            foreach (string name in new[] { "AddNearbyRecipeAvailability", "AddNearbyPieceAvailability" })
                TestAssert.True(runtime.Methods.Single(m => m.Name == name).Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "CheckUiAvailability"));
            var memo = assembly.MainModule.Types.Single(t => t.Name == "UiPreviewCache");
            var answer = memo.NestedTypes.Single(t => t.Name == "Answer");
            TestAssert.False(answer.Fields.Any(f => f.FieldType.FullName.Contains("MaterialPlan", StringComparison.Ordinal)));
            TestAssert.True(runtime.Methods.Single(m => m.Name == "TryResolveNearbyBreakdown").Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "UiPreviewCache"));
        }
    }
}
