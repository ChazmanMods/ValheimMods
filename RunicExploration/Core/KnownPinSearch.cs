using System;
using System.Collections.Generic;

namespace RunicExploration.Core
{
    internal static class KnownPinSearch
    {
        internal const int HardMaximumResults = 24;

        internal static KnownPinSearchResult Filter(
            IReadOnlyList<KnownPinRecord> records,
            string query,
            KnownPinCategoryFilter category,
            KnownPinScopeFilter scope,
            int maximumResults)
        {
            if (records == null || records.Count > KnownPinIndexer.HardMaximumPins)
                return new KnownPinSearchResult(Array.Empty<KnownPinRecord>(), 0, false);
            string normalized = BoundedText.Query(query);
            int maximum = Math.Max(1, Math.Min(HardMaximumResults, maximumResults));
            var matches = new List<KnownPinRecord>(maximum);
            int inspected = 0;
            bool truncated = false;
            for (int index = 0; index < records.Count; index++)
            {
                KnownPinRecord record = records[index];
                inspected++;
                if (!CategoryMatches(record.Category, category) ||
                    !ScopeMatches(record.Scope, scope) ||
                    !string.IsNullOrEmpty(normalized) &&
                    record.SearchKey.IndexOf(normalized, StringComparison.Ordinal) < 0)
                    continue;
                if (matches.Count < maximum) matches.Add(record);
                else truncated = true;
            }
            return new KnownPinSearchResult(matches.ToArray(), inspected, truncated);
        }

        private static bool CategoryMatches(
            KnownPinCategory category,
            KnownPinCategoryFilter filter)
        {
            switch (filter)
            {
                case KnownPinCategoryFilter.TaggedAssets:
                    return category == KnownPinCategory.TaggedAsset;
                case KnownPinCategoryFilter.Tombstones:
                    return category == KnownPinCategory.Tombstone;
                case KnownPinCategoryFilter.Beds:
                    return category == KnownPinCategory.Bed;
                case KnownPinCategoryFilter.Custom:
                    return category == KnownPinCategory.Custom;
                case KnownPinCategoryFilter.Bosses:
                    return category == KnownPinCategory.Boss;
                default:
                    return true;
            }
        }

        private static bool ScopeMatches(KnownPinScope scope, KnownPinScopeFilter filter)
        {
            switch (filter)
            {
                case KnownPinScopeFilter.Personal:
                    return scope == KnownPinScope.Personal || scope == KnownPinScope.Mixed;
                case KnownPinScopeFilter.Shared:
                    return scope == KnownPinScope.Shared || scope == KnownPinScope.Mixed;
                default:
                    return true;
            }
        }
    }
}
