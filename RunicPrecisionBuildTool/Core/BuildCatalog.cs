using System;
using System.Collections.Generic;
using System.Globalization;

namespace QuietBuildRotation
{
    internal readonly struct BuildCatalogEntry
    {
        internal BuildCatalogEntry(
            string stableId,
            string displayName,
            int category,
            int x,
            int y,
            int gridWidth = 0)
        {
            StableId = stableId;
            DisplayName = displayName;
            Category = category;
            X = x;
            Y = y;
            GridWidth = gridWidth;
        }

        internal string StableId { get; }
        internal string DisplayName { get; }
        internal int Category { get; }
        internal int X { get; }
        internal int Y { get; }
        internal int GridWidth { get; }
        internal bool IsValid => !string.IsNullOrEmpty(StableId) && !string.IsNullOrEmpty(DisplayName);
    }

    /// <summary>
    /// Local selection metadata only. Entries must already have passed PieceTable's availability
    /// check; this class cannot unlock or instantiate a piece. Every collection has a hard cap.
    /// </summary>
    internal sealed class BoundedBuildCatalog
    {
        internal const int EntryCapacity = 256;
        internal const int CandidateInspectionCapacity = 1024;
        internal const int FavoriteCapacity = 64;
        internal const int RecentCapacity = 16;
        internal const int MaximumIdLength = 96;
        internal const int MaximumDisplayLength = 96;
        internal const int MaximumQueryLength = 64;
        internal const int MaximumPersistedFavoritesLength =
            FavoriteCapacity * (MaximumIdLength + 1);
        internal const int MaximumPersistedRecentsLength =
            RecentCapacity * (MaximumIdLength + 1);

        private readonly BuildCatalogEntry[] _entries = new BuildCatalogEntry[EntryCapacity];
        private readonly string[] _favorites = new string[FavoriteCapacity];
        private readonly string[] _recents = new string[RecentCapacity];
        private int _entryCount;
        private int _favoriteCount;
        private int _recentCount;

        internal int EntryCount => _entryCount;
        internal int FavoriteCount => _favoriteCount;
        internal int RecentCount => _recentCount;

        internal void Rebuild(IEnumerable<BuildCatalogEntry> candidates)
        {
            Array.Clear(_entries, 0, _entries.Length);
            _entryCount = 0;
            if (candidates == null)
                return;

            int inspected = 0;
            foreach (BuildCatalogEntry candidate in candidates)
            {
                if (_entryCount >= EntryCapacity || inspected++ >= CandidateInspectionCapacity)
                    break;
                if (!TryBoundId(candidate.StableId, out string id) ||
                    !TryBoundDisplay(candidate.DisplayName, out string display) ||
                    IndexOfEntry(id) >= 0)
                {
                    continue;
                }

                _entries[_entryCount++] = new BuildCatalogEntry(
                    id,
                    display,
                    candidate.Category,
                    candidate.X,
                    candidate.Y,
                    candidate.GridWidth);
            }

            CompactKnown(_favorites, ref _favoriteCount, FavoriteCapacity);
            CompactKnown(_recents, ref _recentCount, RecentCapacity);
        }

        internal bool TryFindNext(string query, string currentId, out BuildCatalogEntry result)
        {
            result = default;
            string boundedQuery = BoundQuery(query);
            if (_entryCount == 0) return false;

            int current = IndexOfEntry(currentId);
            for (int offset = 1; offset <= _entryCount; offset++)
            {
                int index = (current + offset + _entryCount) % _entryCount;
                BuildCatalogEntry candidate = _entries[index];
                if (boundedQuery.Length != 0 &&
                    candidate.DisplayName.IndexOf(boundedQuery, StringComparison.OrdinalIgnoreCase) < 0 &&
                    candidate.StableId.IndexOf(boundedQuery, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                result = candidate;
                return true;
            }

            return false;
        }

        internal bool TryCycleFavorite(string currentId, out BuildCatalogEntry result) =>
            TryCycleList(_favorites, _favoriteCount, currentId, out result);

        internal bool TryCycleRecent(string currentId, out BuildCatalogEntry result) =>
            TryCycleList(_recents, _recentCount, currentId, out result);

        internal bool ToggleFavorite(string stableId, out bool isFavorite)
        {
            isFavorite = false;
            if (!TryBoundId(stableId, out string id) || IndexOfEntry(id) < 0)
                return false;

            int index = IndexOf(_favorites, _favoriteCount, id);
            if (index >= 0)
            {
                RemoveAt(_favorites, ref _favoriteCount, index);
                return true;
            }

            if (_favoriteCount >= FavoriteCapacity)
                return false;
            _favorites[_favoriteCount++] = id;
            isFavorite = true;
            return true;
        }

        internal void RecordRecent(string stableId)
        {
            if (!TryBoundId(stableId, out string id) || IndexOfEntry(id) < 0)
                return;

            int existing = IndexOf(_recents, _recentCount, id);
            if (existing >= 0)
                RemoveAt(_recents, ref _recentCount, existing);
            else if (_recentCount == RecentCapacity)
                _recentCount--;

            for (int index = _recentCount; index > 0; index--)
                _recents[index] = _recents[index - 1];
            _recents[0] = id;
            _recentCount++;
        }

        internal string SerializeFavorites() => Serialize(_favorites, _favoriteCount);

        internal string SerializeRecents() => Serialize(_recents, _recentCount);

        internal void LoadFavorites(string value) =>
            Load(
                value,
                _favorites,
                ref _favoriteCount,
                FavoriteCapacity,
                MaximumPersistedFavoritesLength);

        internal void LoadRecents(string value) =>
            Load(
                value,
                _recents,
                ref _recentCount,
                RecentCapacity,
                MaximumPersistedRecentsLength);

        internal BuildCatalogEntry EntryAt(int index) =>
            index >= 0 && index < _entryCount ? _entries[index] : default;

        internal static string BoundQuery(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            int inspection = Math.Min(value.Length, MaximumQueryLength);
            string trimmed = value.Substring(0, inspection).Trim();
            if (trimmed.Length == 0) return string.Empty;
            for (int index = 0; index < trimmed.Length; index++)
            {
                char character = trimmed[index];
                if (char.IsControl(character) ||
                    char.GetUnicodeCategory(character) == UnicodeCategory.Format ||
                    character == '<' || character == '>')
                    return string.Empty;
            }
            return trimmed.Length <= MaximumQueryLength
                ? trimmed
                : trimmed.Substring(0, MaximumQueryLength);
        }

        private bool TryCycleList(
            string[] values,
            int count,
            string currentId,
            out BuildCatalogEntry result)
        {
            result = default;
            if (count == 0)
                return false;

            int current = IndexOf(values, count, currentId);
            for (int offset = 1; offset <= count; offset++)
            {
                int listIndex = (current + offset + count) % count;
                int entryIndex = IndexOfEntry(values[listIndex]);
                if (entryIndex < 0) continue;
                result = _entries[entryIndex];
                return true;
            }

            return false;
        }

        private int IndexOfEntry(string stableId)
        {
            if (string.IsNullOrEmpty(stableId)) return -1;
            for (int index = 0; index < _entryCount; index++)
                if (string.Equals(_entries[index].StableId, stableId, StringComparison.Ordinal))
                    return index;
            return -1;
        }

        private void CompactKnown(string[] values, ref int count, int capacity)
        {
            int write = 0;
            for (int read = 0; read < count && write < capacity; read++)
            {
                string id = values[read];
                if (IndexOfEntry(id) < 0 || IndexOf(values, write, id) >= 0)
                    continue;
                values[write++] = id;
            }
            for (int index = write; index < count; index++) values[index] = null;
            count = write;
        }

        private static void Load(
            string value,
            string[] target,
            ref int count,
            int capacity,
            int maximumSerializedLength)
        {
            Array.Clear(target, 0, target.Length);
            count = 0;
            if (string.IsNullOrEmpty(value) || value.Length > maximumSerializedLength) return;

            string[] parts = value.Split('|');
            for (int index = 0; index < parts.Length && count < capacity; index++)
            {
                if (!TryBoundId(parts[index], out string id) ||
                    IndexOf(target, count, id) >= 0)
                    continue;
                target[count++] = id;
            }
        }

        private static string Serialize(string[] values, int count)
        {
            if (count <= 0) return string.Empty;
            return string.Join("|", values, 0, count);
        }

        private static bool TryBoundId(string value, out string bounded)
        {
            bounded = null;
            if (string.IsNullOrEmpty(value) || value.Length > MaximumIdLength) return false;
            string trimmed = value.Trim();
            if (trimmed.Length == 0 || trimmed.Length > MaximumIdLength) return false;
            for (int index = 0; index < trimmed.Length; index++)
            {
                char character = trimmed[index];
                bool safe = char.IsLetterOrDigit(character) || character == '_' ||
                            character == '-' || character == '.' || character == '$';
                if (!safe) return false;
            }
            bounded = trimmed;
            return true;
        }

        private static bool TryBoundDisplay(string value, out string bounded)
        {
            bounded = null;
            if (string.IsNullOrEmpty(value)) return false;
            int inspection = Math.Min(value.Length, MaximumDisplayLength);
            string trimmed = value.Substring(0, inspection).Trim();
            if (trimmed.Length == 0) return false;
            for (int index = 0; index < trimmed.Length; index++)
                if (char.IsControl(trimmed[index]) ||
                    char.GetUnicodeCategory(trimmed[index]) == UnicodeCategory.Format ||
                    trimmed[index] == '<' || trimmed[index] == '>')
                    return false;
            bounded = trimmed;
            return true;
        }

        private static int IndexOf(string[] values, int count, string value)
        {
            if (string.IsNullOrEmpty(value)) return -1;
            for (int index = 0; index < count; index++)
                if (string.Equals(values[index], value, StringComparison.Ordinal)) return index;
            return -1;
        }

        private static void RemoveAt(string[] values, ref int count, int index)
        {
            if (index < 0 || index >= count) return;
            for (int next = index + 1; next < count; next++) values[next - 1] = values[next];
            values[--count] = null;
        }
    }
}
