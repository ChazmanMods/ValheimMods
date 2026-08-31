using System;
using System.Collections.Generic;
using System.Linq;
using RunicExploration.Core;

namespace RunicExploration.Tests
{
    internal static class CoreBehaviorTests
    {
        internal static void Register()
        {
            TestRunner.Run("text is bounded and strips rich/bidi controls", BoundedTextIsSafe);
            TestRunner.Run("unknown unsaved and nonfinite evidence is rejected", RejectsInvalidEvidence);
            TestRunner.Run("shared records are opt-in", SharedIsOptIn);
            TestRunner.Run("explicit asset tags only", ExplicitTagsOnly);
            TestRunner.Run("vanilla tombstone bed and boss categories", VanillaCategories);
            TestRunner.Run("exact display duplicates merge without source mutation", DuplicateMerge);
            TestRunner.Run("oversized ambiguous names never merge", OversizedNamesDoNotMerge);
            TestRunner.Run("index order is deterministic", DeterministicOrder);
            TestRunner.Run("search and category filters are bounded", SearchFilters);
            TestRunner.Run("personal shared and mixed scope filters", ScopeFilters);
            TestRunner.Run("navigation directions and distances are truthful", Navigation);
            TestRunner.Run("tombstones carry topology and recovery warning", TombstoneWarning);
            TestRunner.Run("sailing is labeled live local state", SailingTruthLabel);
            TestRunner.Run("hard source ceiling rejects before inspection", SourceCeiling);
            TestRunner.Run("changed evidence waits for quiet or bounded maximum deferral", RebuildGateCoalesces);
        }

        private static void BoundedTextIsSafe()
        {
            string raw = "  <color=red>North\u202e\u2066\ud800\nRoad  " +
                         new string('x', 1000);
            string label = BoundedText.Label(raw);
            TestAssert.True(label.Length <= BoundedText.MaximumLabelCharacters);
            TestAssert.DoesNotContain("<", label);
            TestAssert.DoesNotContain(">", label);
            TestAssert.DoesNotContain("\u202e", label);
            TestAssert.DoesNotContain("\u2066", label);
            TestAssert.DoesNotContain("\ud800", label);
            TestAssert.DoesNotContain("\n", label);
            TestAssert.True(label.EndsWith("…", StringComparison.Ordinal));
            TestAssert.True(BoundedText.Query(new string('Q', 500)).Length <= 48);
        }

        private static void RejectsInvalidEvidence()
        {
            var source = new[]
            {
                Pin(0, "known", 0, 0, 0, 0, true, true),
                Pin(1, "unknown", 0, 1, 0, 0, true, false),
                Pin(2, "unsaved", 0, 2, 0, 0, false, true),
                Pin(3, "nan", 0, float.NaN, 0, 0, true, true)
            };
            KnownPinIndex index = KnownPinIndexer.Build(source, 7L, true);
            TestAssert.Equal(KnownPinIndexStatus.Ready, index.Status);
            TestAssert.Equal(4, index.Inspected);
            TestAssert.Equal(1, index.Records.Length);
            TestAssert.Equal("known", index.Records[0].Label);
        }

        private static void SharedIsOptIn()
        {
            var source = new[]
            {
                Pin(0, "personal", 0, 0, 0, 0, true, true, 7L),
                Pin(1, "owner zero", 0, 1, 0, 0, true, true, 0L),
                Pin(2, "shared", 0, 2, 0, 0, true, true, 99L)
            };
            TestAssert.Equal(2, KnownPinIndexer.Build(source, 7L, false).Records.Length);
            TestAssert.Equal(3, KnownPinIndexer.Build(source, 7L, true).Records.Length);
        }

        private static void ExplicitTagsOnly()
        {
            var source = new[]
            {
                Pin(0, "my boat", 0, 0, 0, 0),
                Pin(1, "[boat] Karve", 0, 1, 0, 0),
                Pin(2, "[cart] Ore", 0, 2, 0, 0),
                Pin(3, "[tame] Boar", 0, 3, 0, 0),
                Pin(4, "[portal] Home", 0, 4, 0, 0)
            };
            KnownPinRecord[] records = KnownPinIndexer.Build(source, 7L, true).Records;
            TestAssert.Equal(KnownAssetKind.None,
                records.Single(record => record.Label == "my boat").AssetKind);
            TestAssert.Equal(KnownAssetKind.Boat,
                records.Single(record => record.Label.Contains("Karve")).AssetKind);
            TestAssert.Equal(KnownAssetKind.Cart,
                records.Single(record => record.Label.Contains("Ore")).AssetKind);
            TestAssert.Equal(KnownAssetKind.TamedAnimal,
                records.Single(record => record.Label.Contains("Boar")).AssetKind);
            TestAssert.Equal(KnownAssetKind.Portal,
                records.Single(record => record.Label.Contains("Home")).AssetKind);
            TestAssert.True(records.All(record => record.DisplayRow.Contains("LAST KNOWN")));
        }

        private static void VanillaCategories()
        {
            KnownPinRecord[] records = KnownPinIndexer.Build(new[]
            {
                Pin(0, "", 4, 0, 0, 0),
                Pin(1, "", 5, 1, 0, 0),
                Pin(2, "", 9, 2, 0, 0)
            }, 1L, true).Records;
            TestAssert.True(records.Any(item => item.Category == KnownPinCategory.Tombstone));
            TestAssert.True(records.Any(item => item.Category == KnownPinCategory.Bed));
            TestAssert.True(records.Any(item => item.Category == KnownPinCategory.Boss));
        }

        private static void DuplicateMerge()
        {
            PinEvidence first = Pin(7, "[boat] Bay", 0, 10.1f, 2f, 20.1f,
                true, true, 7L, false);
            PinEvidence second = Pin(3, "[boat] Bay", 0, 10.2f, 2.1f, 20.2f,
                true, true, 99L, true);
            var source = new[] { first, second };
            KnownPinIndex index = KnownPinIndexer.Build(source, 7L, true);
            TestAssert.Equal(1, index.Records.Length);
            TestAssert.Equal(1, index.Merged);
            TestAssert.Equal(2, index.Records[0].SourceCount);
            TestAssert.Equal(KnownPinScope.Mixed, index.Records[0].Scope);
            TestAssert.True(index.Records[0].AnyChecked);
            TestAssert.Equal(3, index.Records[0].SourceIndex);
            TestAssert.Equal(7, first.SourceIndex);
            TestAssert.Equal(3, second.SourceIndex);
        }

        private static void DeterministicOrder()
        {
            var forward = new[]
            {
                Pin(4, "Zulu", 0, 5, 0, 0), Pin(2, "alpha", 1, 4, 0, 0),
                Pin(1, "Alpha", 0, 3, 0, 0), Pin(8, "beta", 0, 2, 0, 0)
            };
            var reverse = forward.Reverse().ToArray();
            string left = string.Join("|", KnownPinIndexer.Build(forward, 7, true).Records
                .Select(record => record.Label + ":" + record.Type + ":" + record.SourceIndex));
            string right = string.Join("|", KnownPinIndexer.Build(reverse, 7, true).Records
                .Select(record => record.Label + ":" + record.Type + ":" + record.SourceIndex));
            TestAssert.Equal(left, right);
        }

        private static void OversizedNamesDoNotMerge()
        {
            string prefix = new string('n', BoundedText.MaximumRawInspectionCharacters);
            KnownPinIndex index = KnownPinIndexer.Build(new[]
            {
                Pin(1, prefix + "A", 0, 10, 0, 10),
                Pin(2, prefix + "B", 0, 10, 0, 10)
            }, 7, true);
            TestAssert.Equal(2, index.Records.Length);
            TestAssert.Equal(0, index.Merged);
            TestAssert.True(index.Records.All(record => record.Label.Length <= 64));
        }

        private static void SearchFilters()
        {
            var source = new List<PinEvidence>();
            for (int index = 0; index < 80; index++)
                source.Add(Pin(index, index % 2 == 0 ? "[boat] north " + index : "road " + index,
                    0, index * 2, 0, 0));
            KnownPinIndex built = KnownPinIndexer.Build(source, 7L, true);
            KnownPinSearchResult result = KnownPinSearch.Filter(built.Records, "NORTH",
                KnownPinCategoryFilter.TaggedAssets, KnownPinScopeFilter.All, 500);
            TestAssert.Equal(KnownPinSearch.HardMaximumResults, result.Records.Length);
            TestAssert.True(result.Truncated);
            TestAssert.Equal(built.Records.Length, result.Inspected);
            TestAssert.True(result.Records.All(item => item.Category == KnownPinCategory.TaggedAsset));
        }

        private static void ScopeFilters()
        {
            KnownPinIndex built = KnownPinIndexer.Build(new[]
            {
                Pin(1, "personal", 0, 1, 0, 0, true, true, 7),
                Pin(2, "shared", 0, 2, 0, 0, true, true, 8),
                Pin(3, "mixed", 0, 3, 0, 0, true, true, 7),
                Pin(4, "mixed", 0, 3, 0, 0, true, true, 8)
            }, 7, true);
            KnownPinSearchResult personal = KnownPinSearch.Filter(built.Records, "",
                KnownPinCategoryFilter.All, KnownPinScopeFilter.Personal, 24);
            KnownPinSearchResult shared = KnownPinSearch.Filter(built.Records, "",
                KnownPinCategoryFilter.All, KnownPinScopeFilter.Shared, 24);
            TestAssert.Equal(2, personal.Records.Length);
            TestAssert.Equal(2, shared.Records.Length);
            TestAssert.True(personal.Records.Any(item => item.Scope == KnownPinScope.Mixed));
            TestAssert.True(shared.Records.Any(item => item.Scope == KnownPinScope.Mixed));
        }

        private static void Navigation()
        {
            KnownPinRecord northEast = KnownPinIndexer.Build(
                new[] { Pin(0, "target", 0, 300, 0, 400) }, 1, true).Records[0];
            string text = NavigationFormatter.FormatSelected(northEast, 0, 0, 0);
            TestAssert.Contains("500 m NE", text);
            TestAssert.Equal("N", NavigationFormatter.Direction(0, 1));
            TestAssert.Equal("W", NavigationFormatter.Direction(-1, 0));
            TestAssert.Equal("1.5 km", NavigationFormatter.FormatDistance(1500));
            TestAssert.Equal("unknown", NavigationFormatter.FormatDistance(double.NaN));
        }

        private static void TombstoneWarning()
        {
            KnownPinRecord tomb = KnownPinIndexer.Build(
                new[] { Pin(0, "Grave", 4, 0, 0, 10) }, 1, true).Records[0];
            string text = NavigationFormatter.FormatSelected(tomb, 0, 0, 0);
            TestAssert.Contains("LAST KNOWN PIN", text);
            TestAssert.Contains("Topology warning", text);
            TestAssert.Contains("no remote recovery", text);
        }

        private static void SailingTruthLabel()
        {
            string text = NavigationFormatter.FormatSailing(
                "Full", true, 0.75f, 0.5f, "Ocean", 1234d);
            TestAssert.Contains("LIVE LOCAL SHIP", text);
            TestAssert.Contains("current biome Ocean", text);
            TestAssert.Contains("1.2 km", text);
            TestAssert.Equal(string.Empty,
                NavigationFormatter.FormatSailing("Full", true, float.NaN, 1, "Ocean", null));
        }

        private static void SourceCeiling()
        {
            var source = new PinEvidence[KnownPinIndexer.HardMaximumPins + 1];
            KnownPinIndex index = KnownPinIndexer.Build(source, 1, true);
            TestAssert.Equal(KnownPinIndexStatus.Oversized, index.Status);
            TestAssert.Equal(0, index.Inspected);
            TestAssert.Equal(0, index.Records.Length);
        }

        private static void RebuildGateCoalesces()
        {
            var gate = new KnownPinRebuildGate();
            TestAssert.True(gate.ShouldRebuild(10UL, 0UL, false, 0f));
            gate.MarkPublished(0f);
            TestAssert.False(gate.ShouldRebuild(11UL, 10UL, true, 0.25f));
            TestAssert.False(gate.ShouldRebuild(11UL, 10UL, true, 0.75f));
            TestAssert.True(gate.ShouldRebuild(11UL, 10UL, true, 1f));
            gate.MarkPublished(1f);
            TestAssert.False(gate.ShouldRebuild(12UL, 11UL, true, 1.25f));
            TestAssert.False(gate.ShouldRebuild(13UL, 11UL, true, 1.5f));
            TestAssert.True(gate.ShouldRebuild(14UL, 11UL, true, 3f),
                "Continuous churn must still publish a bounded fresh snapshot.");
            gate.Reset();
            TestAssert.True(gate.ShouldRebuild(15UL, 14UL, false, 3.25f));
        }

        internal static PinEvidence Pin(int index, string name, int type, float x, float y, float z,
            bool saved = true, bool known = true, long owner = 0L, bool isChecked = false) =>
            new PinEvidence(index, name, type, x, y, z, saved, known, isChecked, owner);
    }
}
