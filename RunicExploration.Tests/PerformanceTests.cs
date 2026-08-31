using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RunicExploration.Core;

namespace RunicExploration.Tests
{
    internal static class PerformanceTests
    {
        internal static void Register()
        {
            TestRunner.Run("100 pin work profile is bounded", () => Profile(100));
            TestRunner.Run("1,000 pin work profile is bounded", () => Profile(1000));
            TestRunner.Run("10,000 pin work profile is bounded", () => Profile(10000));
            TestRunner.Run("100 pin repeated-change profile is coalesced", () => RepeatedChangedProfile(100, 512L * 1024L));
            TestRunner.Run("1,000 pin repeated-change profile is coalesced", () => RepeatedChangedProfile(1000, 4L * 1024L * 1024L));
            TestRunner.Run("10,000 pin repeated-change profile is coalesced", () => RepeatedChangedProfile(10000, 24L * 1024L * 1024L));
            TestRunner.Run("10,000 pin index is deterministic across rebuilds", DeterministicAtCeiling);
            TestRunner.Run("runtime rebuilds only on interval and evidence changes", RuntimeIsStateGated);
        }

        private static void Profile(int size)
        {
            List<PinEvidence> evidence = Data(size);
            KnownPinIndexer.Build(evidence, 7L, true);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            long before = GC.GetAllocatedBytesForCurrentThread();
            Stopwatch watch = Stopwatch.StartNew();
            KnownPinIndex index = KnownPinIndexer.Build(evidence, 7L, true);
            KnownPinSearchResult search = KnownPinSearch.Filter(index.Records, "known",
                KnownPinCategoryFilter.All, KnownPinScopeFilter.All, 24);
            watch.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            TestAssert.Equal(KnownPinIndexStatus.Ready, index.Status);
            TestAssert.Equal(size, index.Inspected);
            TestAssert.Equal(size, index.Accepted);
            TestAssert.Equal(size, index.Records.Length);
            TestAssert.Equal(size, search.Inspected);
            TestAssert.Equal(Math.Min(size, 24), search.Records.Length);
            TestAssert.Equal(size > 24, search.Truncated);
            TestAssert.True(watch.Elapsed < TimeSpan.FromSeconds(3),
                size + " pin profile took " + watch.Elapsed.TotalMilliseconds + " ms.");
            TestAssert.True(allocated < 64L * 1024 * 1024,
                size + " pin profile allocated " + allocated + " bytes.");
            System.Console.WriteLine("     profile pins=" + size + " inspected=" + index.Inspected +
                              " results=" + search.Records.Length + " ms=" +
                              watch.Elapsed.TotalMilliseconds.ToString("0.00") + " alloc=" +
                              allocated);
        }

        private static void DeterministicAtCeiling()
        {
            List<PinEvidence> evidence = Data(KnownPinIndexer.HardMaximumPins);
            KnownPinRecord[] first = KnownPinIndexer.Build(evidence, 7L, true).Records;
            KnownPinRecord[] second = KnownPinIndexer.Build(evidence, 7L, true).Records;
            TestAssert.Equal(first.Length, second.Length);
            ulong left = Digest(first);
            ulong right = Digest(second);
            TestAssert.Equal(left, right);
        }

        private static void RepeatedChangedProfile(int size, long allocationBudget)
        {
            List<PinEvidence> evidence = Data(size);
            KnownPinIndexer.Build(evidence, 7L, true);
            var gate = new KnownPinRebuildGate();
            ulong published = 0UL;
            TestAssert.True(gate.ShouldRebuild(1UL, published, false, 0f));
            published = 1UL;
            gate.MarkPublished(0f);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            long before = GC.GetAllocatedBytesForCurrentThread();
            Stopwatch watch = Stopwatch.StartNew();
            int rebuilds = 0;
            for (int tick = 1; tick <= 16; tick++)
            {
                float now = tick * 0.25f;
                ulong candidate = (ulong)(tick + 1);
                if (!gate.ShouldRebuild(candidate, published, true, now)) continue;
                KnownPinIndex index = KnownPinIndexer.Build(evidence, 7L, true);
                TestAssert.Equal(size, index.Records.Length);
                published = candidate;
                gate.MarkPublished(now);
                rebuilds++;
            }
            watch.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            TestAssert.Equal(2, rebuilds,
                "Continuously changing evidence should force at most one rebuild every two seconds.");
            TestAssert.True(allocated < allocationBudget,
                size + " pin repeated-change profile allocated " + allocated + " bytes.");
            TestAssert.True(watch.Elapsed < TimeSpan.FromSeconds(3), watch.Elapsed.ToString());
            System.Console.WriteLine("     repeated-change pins=" + size + " rebuilds=" + rebuilds +
                              " ms=" + watch.Elapsed.TotalMilliseconds.ToString("0.00") +
                              " alloc=" + allocated);
        }

        private static void RuntimeIsStateGated()
        {
            string source = System.IO.File.ReadAllText(TestPaths.Plugin(
                System.IO.Path.Combine("Integration", "ExplorationRuntime.cs")));
            TestAssert.Contains("if (now < _nextRefresh", source);
            TestAssert.Contains("_rebuildGate.ShouldRebuild(", source);
            TestAssert.Contains("_rebuildGate.MarkPublished(now)", source);
            TestAssert.Contains("if (_sensitiveCleared) return;", source);
            TestAssert.Contains("if (_index.Status != status", source);
            TestAssert.DoesNotContain("source=", source);
            TestAssert.Contains("_resultContent = new GUIContent[_search.Records.Length]", source);
            int draw = source.IndexOf("internal void Draw()", StringComparison.Ordinal);
            int config = source.IndexOf("internal void OnConfigurationChanged", draw,
                StringComparison.Ordinal);
            string drawSource = source.Substring(draw, config - draw);
            TestAssert.DoesNotContain("KnownPinIndexer.Build", drawSource);
            TestAssert.DoesNotContain("KnownPinSearch.Filter", drawSource);
            TestAssert.DoesNotContain("new GUIContent[", drawSource);
        }

        private static List<PinEvidence> Data(int size)
        {
            var result = new List<PinEvidence>(size);
            for (int index = 0; index < size; index++)
            {
                string tag = index % 17 == 0 ? "[boat] " : string.Empty;
                result.Add(CoreBehaviorTests.Pin(
                    index,
                    tag + "known pin " + index.ToString("D5"),
                    index % 101 == 0 ? 4 : index % 103 == 0 ? 5 : 0,
                    index * 2f,
                    index % 37,
                    index * 3f,
                    true,
                    true,
                    index % 5 == 0 ? 99L : 7L,
                    index % 11 == 0));
            }
            return result;
        }

        private static ulong Digest(IEnumerable<KnownPinRecord> records)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                foreach (KnownPinRecord record in records)
                {
                    hash ^= record.EvidenceHash;
                    hash *= 1099511628211UL;
                    hash ^= (ulong)record.SourceCount;
                    hash *= 1099511628211UL;
                }
                return hash;
            }
        }
    }
}
