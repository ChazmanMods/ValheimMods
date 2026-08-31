using System;

namespace RunicExploration.Core
{
    internal enum KnownPinCategory
    {
        Custom = 0,
        TaggedAsset = 1,
        Tombstone = 2,
        Bed = 3,
        Boss = 4
    }

    internal enum KnownAssetKind
    {
        None = 0,
        Boat = 1,
        Cart = 2,
        TamedAnimal = 3,
        Portal = 4,
        Bed = 5,
        Tombstone = 6
    }

    internal enum KnownPinScope
    {
        Personal = 0,
        Shared = 1,
        Mixed = 2
    }

    internal enum KnownPinCategoryFilter
    {
        All = 0,
        TaggedAssets = 1,
        Tombstones = 2,
        Beds = 3,
        Custom = 4,
        Bosses = 5
    }

    internal enum KnownPinScopeFilter
    {
        All = 0,
        Personal = 1,
        Shared = 2
    }

    internal enum KnownPinIndexStatus
    {
        Ready = 0,
        Oversized = 1,
        InvalidSource = 2
    }

    internal readonly struct PinEvidence
    {
        internal PinEvidence(
            int sourceIndex,
            string rawName,
            int type,
            float x,
            float y,
            float z,
            bool saved,
            bool known,
            bool isChecked,
            long ownerId)
        {
            SourceIndex = sourceIndex;
            RawName = rawName;
            Type = type;
            X = x;
            Y = y;
            Z = z;
            Saved = saved;
            Known = known;
            IsChecked = isChecked;
            OwnerId = ownerId;
        }

        internal int SourceIndex { get; }
        internal string RawName { get; }
        internal int Type { get; }
        internal float X { get; }
        internal float Y { get; }
        internal float Z { get; }
        internal bool Saved { get; }
        internal bool Known { get; }
        internal bool IsChecked { get; }
        internal long OwnerId { get; }
    }

    internal readonly struct KnownPinRecord
    {
        internal KnownPinRecord(
            int sourceIndex,
            string label,
            string searchKey,
            int type,
            float x,
            float y,
            float z,
            int cellX,
            int cellY,
            int cellZ,
            ulong rawNameHash,
            ulong evidenceHash,
            KnownPinCategory category,
            KnownAssetKind assetKind,
            KnownPinScope scope,
            int sourceCount,
            bool anyChecked,
            string displayRow)
        {
            SourceIndex = sourceIndex;
            Label = label;
            SearchKey = searchKey;
            Type = type;
            X = x;
            Y = y;
            Z = z;
            CellX = cellX;
            CellY = cellY;
            CellZ = cellZ;
            RawNameHash = rawNameHash;
            EvidenceHash = evidenceHash;
            Category = category;
            AssetKind = assetKind;
            Scope = scope;
            SourceCount = sourceCount;
            AnyChecked = anyChecked;
            DisplayRow = displayRow;
        }

        internal int SourceIndex { get; }
        internal string Label { get; }
        internal string SearchKey { get; }
        internal int Type { get; }
        internal float X { get; }
        internal float Y { get; }
        internal float Z { get; }
        internal int CellX { get; }
        internal int CellY { get; }
        internal int CellZ { get; }
        internal ulong RawNameHash { get; }
        internal ulong EvidenceHash { get; }
        internal KnownPinCategory Category { get; }
        internal KnownAssetKind AssetKind { get; }
        internal KnownPinScope Scope { get; }
        internal int SourceCount { get; }
        internal bool AnyChecked { get; }
        internal string DisplayRow { get; }
    }

    internal sealed class KnownPinIndex
    {
        internal KnownPinIndex(
            KnownPinIndexStatus status,
            KnownPinRecord[] records,
            int inspected,
            int accepted,
            int merged)
        {
            Status = status;
            Records = records ?? Array.Empty<KnownPinRecord>();
            Inspected = inspected;
            Accepted = accepted;
            Merged = merged;
        }

        internal KnownPinIndexStatus Status { get; }
        internal KnownPinRecord[] Records { get; }
        internal int Inspected { get; }
        internal int Accepted { get; }
        internal int Merged { get; }
    }

    internal sealed class KnownPinSearchResult
    {
        internal KnownPinSearchResult(KnownPinRecord[] records, int inspected, bool truncated)
        {
            Records = records ?? Array.Empty<KnownPinRecord>();
            Inspected = inspected;
            Truncated = truncated;
        }

        internal KnownPinRecord[] Records { get; }
        internal int Inspected { get; }
        internal bool Truncated { get; }
    }
}
