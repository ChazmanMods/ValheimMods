using System;
using System.Collections.Generic;
using System.Globalization;

namespace RunicExploration.Core
{
    internal static class KnownPinIndexer
    {
        internal const int HardMaximumPins = 10000;
        internal const int PositionCellMeters = 1;

        internal static KnownPinIndex Build(
            IReadOnlyList<PinEvidence> evidence,
            long localPlayerId,
            bool includeShared)
        {
            if (evidence == null)
                return new KnownPinIndex(
                    KnownPinIndexStatus.InvalidSource,
                    Array.Empty<KnownPinRecord>(),
                    0,
                    0,
                    0);
            int count = evidence.Count;
            if (count > HardMaximumPins)
                return new KnownPinIndex(
                    KnownPinIndexStatus.Oversized,
                    Array.Empty<KnownPinRecord>(),
                    0,
                    0,
                    0);

            var candidates = new List<Candidate>(Math.Min(count, HardMaximumPins));
            for (int index = 0; index < count; index++)
            {
                PinEvidence item = evidence[index];
                if (!item.Saved || !item.Known || !Finite(item.X) || !Finite(item.Y) ||
                    !Finite(item.Z)) continue;
                KnownPinScope scope = item.OwnerId == 0L || item.OwnerId == localPlayerId
                    ? KnownPinScope.Personal
                    : KnownPinScope.Shared;
                if (!includeShared && scope == KnownPinScope.Shared) continue;
                string label = BoundedText.Label(item.RawName);
                if (string.IsNullOrEmpty(label)) label = TypeLabel(item.Type);
                string nameKey = label.ToLowerInvariant();
                KnownAssetKind asset = AssetKind(nameKey, item.Type);
                KnownPinCategory category = Category(item.Type, asset);
                int cellX = Quantize(item.X);
                int cellY = Quantize(item.Y);
                int cellZ = Quantize(item.Z);
                ulong rawNameHash = BoundedText.Hash(
                    item.RawName,
                    BoundedText.MaximumRawInspectionCharacters);
                bool exactName = item.RawName == null ||
                                 item.RawName.Length <= BoundedText.MaximumRawInspectionCharacters;
                ulong evidenceHash = EvidenceHash(item, rawNameHash, cellX, cellY, cellZ);
                candidates.Add(new Candidate(
                    item.SourceIndex,
                    label,
                    nameKey,
                    item.Type,
                    item.X,
                    item.Y,
                    item.Z,
                    cellX,
                    cellY,
                    cellZ,
                    rawNameHash,
                    exactName,
                    evidenceHash,
                    category,
                    asset,
                    scope,
                    item.IsChecked));
            }

            candidates.Sort(CandidateComparer.Instance);
            var records = new List<KnownPinRecord>(candidates.Count);
            int merged = 0;
            for (int start = 0; start < candidates.Count;)
            {
                Candidate primary = candidates[start];
                int end = start + 1;
                KnownPinScope scope = primary.Scope;
                bool anyChecked = primary.IsChecked;
                while (end < candidates.Count && SameKnownPin(primary, candidates[end]))
                {
                    Candidate duplicate = candidates[end];
                    if (duplicate.Scope != scope) scope = KnownPinScope.Mixed;
                    anyChecked |= duplicate.IsChecked;
                    end++;
                }
                int sources = end - start;
                merged += sources - 1;
                string searchKey = primary.NameKey + " " +
                                   CategoryLabel(primary.Category).ToLowerInvariant() + " " +
                                   AssetLabel(primary.AssetKind).ToLowerInvariant();
                string row = DisplayRow(primary.Label, primary.Category, primary.AssetKind,
                    scope, sources, anyChecked);
                records.Add(new KnownPinRecord(
                    primary.SourceIndex,
                    primary.Label,
                    searchKey,
                    primary.Type,
                    primary.X,
                    primary.Y,
                    primary.Z,
                    primary.CellX,
                    primary.CellY,
                    primary.CellZ,
                    primary.RawNameHash,
                    primary.EvidenceHash,
                    primary.Category,
                    primary.AssetKind,
                    scope,
                    sources,
                    anyChecked,
                    row));
                start = end;
            }
            return new KnownPinIndex(
                KnownPinIndexStatus.Ready,
                records.ToArray(),
                count,
                candidates.Count,
                merged);
        }

        internal static ulong EvidenceHash(PinEvidence item)
        {
            int x = Finite(item.X) ? Quantize(item.X) : int.MinValue;
            int y = Finite(item.Y) ? Quantize(item.Y) : int.MinValue;
            int z = Finite(item.Z) ? Quantize(item.Z) : int.MinValue;
            return EvidenceHash(
                item,
                BoundedText.Hash(item.RawName, BoundedText.MaximumRawInspectionCharacters),
                x,
                y,
                z);
        }

        internal static string TypeLabel(int type)
        {
            switch (type)
            {
                case 0: return "Icon 0";
                case 1: return "Icon 1";
                case 2: return "Icon 2";
                case 3: return "Icon 3";
                case 4: return "Tombstone";
                case 5: return "Bed";
                case 6: return "Icon 4";
                case 9: return "Boss";
                default: return "Pin type " + type.ToString(CultureInfo.InvariantCulture);
            }
        }

        internal static string CategoryLabel(KnownPinCategory category)
        {
            switch (category)
            {
                case KnownPinCategory.TaggedAsset: return "tagged asset";
                case KnownPinCategory.Tombstone: return "tombstone";
                case KnownPinCategory.Bed: return "bed";
                case KnownPinCategory.Boss: return "boss";
                default: return "custom pin";
            }
        }

        internal static string AssetLabel(KnownAssetKind asset)
        {
            switch (asset)
            {
                case KnownAssetKind.Boat: return "boat";
                case KnownAssetKind.Cart: return "cart";
                case KnownAssetKind.TamedAnimal: return "tamed animal";
                case KnownAssetKind.Portal: return "portal";
                case KnownAssetKind.Bed: return "bed";
                case KnownAssetKind.Tombstone: return "tombstone";
                default: return string.Empty;
            }
        }

        private static KnownAssetKind AssetKind(string nameKey, int type)
        {
            if (type == 4) return KnownAssetKind.Tombstone;
            if (type == 5) return KnownAssetKind.Bed;
            if (nameKey.StartsWith("[boat]", StringComparison.Ordinal)) return KnownAssetKind.Boat;
            if (nameKey.StartsWith("[cart]", StringComparison.Ordinal)) return KnownAssetKind.Cart;
            if (nameKey.StartsWith("[animal]", StringComparison.Ordinal) ||
                nameKey.StartsWith("[tame]", StringComparison.Ordinal))
                return KnownAssetKind.TamedAnimal;
            if (nameKey.StartsWith("[portal]", StringComparison.Ordinal)) return KnownAssetKind.Portal;
            if (nameKey.StartsWith("[bed]", StringComparison.Ordinal)) return KnownAssetKind.Bed;
            if (nameKey.StartsWith("[tombstone]", StringComparison.Ordinal) ||
                nameKey.StartsWith("[grave]", StringComparison.Ordinal))
                return KnownAssetKind.Tombstone;
            return KnownAssetKind.None;
        }

        private static KnownPinCategory Category(int type, KnownAssetKind asset)
        {
            if (type == 4 || asset == KnownAssetKind.Tombstone)
                return KnownPinCategory.Tombstone;
            if (type == 5 || asset == KnownAssetKind.Bed) return KnownPinCategory.Bed;
            if (type == 9) return KnownPinCategory.Boss;
            return asset != KnownAssetKind.None
                ? KnownPinCategory.TaggedAsset
                : KnownPinCategory.Custom;
        }

        private static string DisplayRow(
            string label,
            KnownPinCategory category,
            KnownAssetKind asset,
            KnownPinScope scope,
            int sources,
            bool isChecked)
        {
            string kind = asset == KnownAssetKind.None
                ? CategoryLabel(category)
                : AssetLabel(asset);
            string source = scope == KnownPinScope.Personal
                ? "personal"
                : scope == KnownPinScope.Shared ? "shared" : "personal + shared";
            return label + "  |  " + kind + "  |  LAST KNOWN, " + source +
                   (sources > 1 ? ", " + sources.ToString(CultureInfo.InvariantCulture) +
                                  " merged sources" : string.Empty) +
                   (isChecked ? ", checked" : string.Empty);
        }

        private static bool SameKnownPin(Candidate left, Candidate right) =>
            left.Type == right.Type && left.CellX == right.CellX && left.CellY == right.CellY &&
            left.CellZ == right.CellZ && left.RawNameHash == right.RawNameHash &&
            left.ExactName && right.ExactName &&
            string.Equals(left.NameKey, right.NameKey, StringComparison.Ordinal);

        private static bool Finite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static int Quantize(float value)
        {
            double scaled = Math.Round(
                value / PositionCellMeters,
                MidpointRounding.AwayFromZero);
            return scaled > int.MaxValue ? int.MaxValue :
                scaled < int.MinValue ? int.MinValue : (int)scaled;
        }

        private static ulong EvidenceHash(
            PinEvidence item,
            ulong rawNameHash,
            int x,
            int y,
            int z)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                hash = Mix(hash, item.SourceIndex);
                hash = Mix(hash, item.Type);
                hash = Mix(hash, x);
                hash = Mix(hash, y);
                hash = Mix(hash, z);
                hash = Mix(hash, item.Saved ? 1 : 0);
                hash = Mix(hash, item.Known ? 1 : 0);
                hash = Mix(hash, item.IsChecked ? 1 : 0);
                hash ^= (ulong)item.OwnerId;
                hash *= 1099511628211UL;
                hash ^= rawNameHash;
                return hash * 1099511628211UL;
            }
        }

        private static ulong Mix(ulong hash, int value)
        {
            unchecked
            {
                hash ^= (uint)value;
                return hash * 1099511628211UL;
            }
        }

        private readonly struct Candidate
        {
            internal Candidate(
                int sourceIndex,
                string label,
                string nameKey,
                int type,
                float x,
                float y,
                float z,
                int cellX,
                int cellY,
                int cellZ,
                ulong rawNameHash,
                bool exactName,
                ulong evidenceHash,
                KnownPinCategory category,
                KnownAssetKind assetKind,
                KnownPinScope scope,
                bool isChecked)
            {
                SourceIndex = sourceIndex;
                Label = label;
                NameKey = nameKey;
                Type = type;
                X = x;
                Y = y;
                Z = z;
                CellX = cellX;
                CellY = cellY;
                CellZ = cellZ;
                RawNameHash = rawNameHash;
                ExactName = exactName;
                EvidenceHash = evidenceHash;
                Category = category;
                AssetKind = assetKind;
                Scope = scope;
                IsChecked = isChecked;
            }

            internal int SourceIndex { get; }
            internal string Label { get; }
            internal string NameKey { get; }
            internal int Type { get; }
            internal float X { get; }
            internal float Y { get; }
            internal float Z { get; }
            internal int CellX { get; }
            internal int CellY { get; }
            internal int CellZ { get; }
            internal ulong RawNameHash { get; }
            internal bool ExactName { get; }
            internal ulong EvidenceHash { get; }
            internal KnownPinCategory Category { get; }
            internal KnownAssetKind AssetKind { get; }
            internal KnownPinScope Scope { get; }
            internal bool IsChecked { get; }
        }

        private sealed class CandidateComparer : IComparer<Candidate>
        {
            internal static readonly CandidateComparer Instance = new CandidateComparer();

            public int Compare(Candidate left, Candidate right)
            {
                int order = string.CompareOrdinal(left.NameKey, right.NameKey);
                if (order != 0) return order;
                order = left.Type.CompareTo(right.Type);
                if (order != 0) return order;
                order = left.CellX.CompareTo(right.CellX);
                if (order != 0) return order;
                order = left.CellZ.CompareTo(right.CellZ);
                if (order != 0) return order;
                order = left.CellY.CompareTo(right.CellY);
                if (order != 0) return order;
                order = left.RawNameHash.CompareTo(right.RawNameHash);
                return order != 0 ? order : left.SourceIndex.CompareTo(right.SourceIndex);
            }
        }
    }
}
