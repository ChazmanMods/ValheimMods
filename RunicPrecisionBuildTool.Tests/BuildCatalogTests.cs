using System;
using System.Collections;
using System.Collections.Generic;

namespace QuietBuildRotation.Tests
{
    internal static class BuildCatalogTests
    {
        internal static void Register()
        {
            TestRunner.Run("Build catalog inspects at most its exact hard bound", CandidateInspectionIsBounded);
            TestRunner.Run("Build catalog retains at most 256 unique unlocked entries", EntryCapacityIsExact);
            TestRunner.Run("Build search is deterministic and wraps once", SearchWrapIsDeterministic);
            TestRunner.Run("Build search rejects rich-text and control injection", SearchTextIsSafe);
            TestRunner.Run("Favorites and recents retain exact bounded order", ListsAreBoundedAndDeterministic);
            TestRunner.Run("Oversized persisted catalog state fails closed before splitting", OversizedPersistenceIsRejected);
            TestRunner.Run("Catalog 100/1k/10k probes have a constant inspection ceiling", InstrumentedScaleProbe);
        }

        private static void CandidateInspectionIsBounded()
        {
            BoundedBuildCatalog catalog = new BoundedBuildCatalog();
            CountingCandidates candidates = new CountingCandidates(10000, duplicate: true);
            catalog.Rebuild(candidates);
            TestAssert.Equal(1, catalog.EntryCount);
            TestAssert.True(candidates.Visited <= BoundedBuildCatalog.CandidateInspectionCapacity + 1,
                "Hostile duplicate input exceeded the hard inspection ceiling: " + candidates.Visited);
        }

        private static void EntryCapacityIsExact()
        {
            BoundedBuildCatalog catalog = new BoundedBuildCatalog();
            CountingCandidates candidates = new CountingCandidates(10000, duplicate: false);
            catalog.Rebuild(candidates);
            TestAssert.Equal(BoundedBuildCatalog.EntryCapacity, catalog.EntryCount);
            TestAssert.True(candidates.Visited <= BoundedBuildCatalog.EntryCapacity + 1);
            TestAssert.Equal("piece_000", catalog.EntryAt(0).StableId);
            TestAssert.Equal("piece_255", catalog.EntryAt(255).StableId);
        }

        private static void SearchWrapIsDeterministic()
        {
            BoundedBuildCatalog catalog = NewSmallCatalog();
            TestAssert.True(catalog.TryFindNext("beam", "piece_beam_a", out BuildCatalogEntry next));
            TestAssert.Equal("piece_beam_b", next.StableId);
            TestAssert.True(catalog.TryFindNext("beam", "piece_beam_b", out next));
            TestAssert.Equal("piece_beam_a", next.StableId);
            TestAssert.True(catalog.TryFindNext(string.Empty, "piece_wall", out next));
            TestAssert.Equal("piece_beam_b", next.StableId);
        }

        private static void SearchTextIsSafe()
        {
            TestAssert.Equal(string.Empty, BoundedBuildCatalog.BoundQuery("<size=999>beam"));
            TestAssert.Equal(string.Empty, BoundedBuildCatalog.BoundQuery("beam\nwood"));
            TestAssert.Equal(string.Empty, BoundedBuildCatalog.BoundQuery("beam\u202Ewood"));
            string million = new string('a', 1000000);
            string bounded = BoundedBuildCatalog.BoundQuery(million);
            TestAssert.Equal(BoundedBuildCatalog.MaximumQueryLength, bounded.Length);
        }

        private static void ListsAreBoundedAndDeterministic()
        {
            var entries = new List<BuildCatalogEntry>();
            for (int index = 0; index < 100; index++)
                entries.Add(Entry("piece_" + index.ToString("000"), "Piece " + index));
            BoundedBuildCatalog catalog = new BoundedBuildCatalog();
            catalog.Rebuild(entries);
            for (int index = 0; index < 100; index++)
            {
                bool changed = catalog.ToggleFavorite("piece_" + index.ToString("000"), out _);
                TestAssert.Equal(index < BoundedBuildCatalog.FavoriteCapacity, changed);
                catalog.RecordRecent("piece_" + index.ToString("000"));
            }
            TestAssert.Equal(BoundedBuildCatalog.FavoriteCapacity, catalog.FavoriteCount);
            TestAssert.Equal(BoundedBuildCatalog.RecentCapacity, catalog.RecentCount);
            TestAssert.True(catalog.TryCycleRecent("piece_099", out BuildCatalogEntry recent));
            TestAssert.Equal("piece_098", recent.StableId);
            TestAssert.True(catalog.SerializeFavorites().Length <=
                            BoundedBuildCatalog.MaximumPersistedFavoritesLength);
            TestAssert.True(catalog.SerializeRecents().Length <=
                            BoundedBuildCatalog.MaximumPersistedRecentsLength);
        }

        private static void OversizedPersistenceIsRejected()
        {
            BoundedBuildCatalog catalog = NewSmallCatalog();
            catalog.LoadFavorites(new string('x',
                BoundedBuildCatalog.MaximumPersistedFavoritesLength + 1));
            catalog.LoadRecents(new string('x',
                BoundedBuildCatalog.MaximumPersistedRecentsLength + 1));
            TestAssert.Equal(0, catalog.FavoriteCount);
            TestAssert.Equal(0, catalog.RecentCount);
        }

        private static void InstrumentedScaleProbe()
        {
            int previous = 0;
            foreach (int size in new[] { 100, 1000, 10000 })
            {
                BoundedBuildCatalog catalog = new BoundedBuildCatalog();
                CountingCandidates candidates = new CountingCandidates(size, duplicate: true);
                catalog.Rebuild(candidates);
                TestAssert.True(candidates.Visited >= previous);
                TestAssert.True(candidates.Visited <=
                                BoundedBuildCatalog.CandidateInspectionCapacity + 1);
                previous = candidates.Visited;
            }
        }

        private static BoundedBuildCatalog NewSmallCatalog()
        {
            BoundedBuildCatalog catalog = new BoundedBuildCatalog();
            catalog.Rebuild(new[]
            {
                Entry("piece_beam_a", "Oak Beam"),
                Entry("piece_wall", "Stone Wall"),
                Entry("piece_beam_b", "Darkwood Beam")
            });
            return catalog;
        }

        private static BuildCatalogEntry Entry(string id, string display) =>
            new BuildCatalogEntry(id, display, 0, 0, 0);

        private sealed class CountingCandidates : IEnumerable<BuildCatalogEntry>
        {
            private readonly int _count;
            private readonly bool _duplicate;

            internal CountingCandidates(int count, bool duplicate)
            {
                _count = count;
                _duplicate = duplicate;
            }

            internal int Visited { get; private set; }

            public IEnumerator<BuildCatalogEntry> GetEnumerator()
            {
                for (int index = 0; index < _count; index++)
                {
                    Visited++;
                    int id = _duplicate ? 0 : index;
                    yield return Entry(
                        "piece_" + id.ToString("000"),
                        "Piece " + id);
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
