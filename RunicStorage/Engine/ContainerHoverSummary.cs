using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RunicStorage.Engine
{
    internal readonly struct HoverDisclosureFacts
    {
        internal HoverDisclosureFacts(
            bool featureEnabled,
            bool localPlayerAvailable,
            bool containerActive,
            bool networkObjectValid,
            bool nonPrivate,
            bool wardAccess,
            bool containerAccess,
            bool withinPhysicalReach,
            bool closed,
            bool mutationIdle,
            bool durableClaimAbsent,
            bool synchronized)
        {
            FeatureEnabled = featureEnabled;
            LocalPlayerAvailable = localPlayerAvailable;
            ContainerActive = containerActive;
            NetworkObjectValid = networkObjectValid;
            NonPrivate = nonPrivate;
            WardAccess = wardAccess;
            ContainerAccess = containerAccess;
            WithinPhysicalReach = withinPhysicalReach;
            Closed = closed;
            MutationIdle = mutationIdle;
            DurableClaimAbsent = durableClaimAbsent;
            Synchronized = synchronized;
        }

        internal bool FeatureEnabled { get; }
        internal bool LocalPlayerAvailable { get; }
        internal bool ContainerActive { get; }
        internal bool NetworkObjectValid { get; }
        internal bool NonPrivate { get; }
        internal bool WardAccess { get; }
        internal bool ContainerAccess { get; }
        internal bool WithinPhysicalReach { get; }
        internal bool Closed { get; }
        internal bool MutationIdle { get; }
        internal bool DurableClaimAbsent { get; }
        internal bool Synchronized { get; }
    }

    internal static class HoverDisclosurePolicy
    {
        internal static bool AllowsBeforeSynchronization(HoverDisclosureFacts facts) =>
            facts.FeatureEnabled &&
            facts.LocalPlayerAvailable &&
            facts.ContainerActive &&
            facts.NetworkObjectValid &&
            facts.NonPrivate &&
            facts.WardAccess &&
            facts.ContainerAccess &&
            facts.WithinPhysicalReach &&
            facts.Closed &&
            facts.MutationIdle &&
            facts.DurableClaimAbsent;

        internal static bool Allows(HoverDisclosureFacts facts) =>
            AllowsBeforeSynchronization(facts) && facts.Synchronized;
    }

    internal static class StrictWardDisclosurePolicy
    {
        internal static bool IsHostileOverlap(
            bool enabled,
            bool containsTarget,
            bool localAccess) =>
            enabled && containsTarget && !localAccess;
    }

    internal static class HoverRangePolicy
    {
        internal const float HardMaximumPhysicalReachMeters = 10f;

        internal static bool IsWithinPhysicalReach(
            float squaredDistance,
            float vanillaMaximumInteractDistance)
        {
            if (float.IsNaN(squaredDistance) || float.IsInfinity(squaredDistance) ||
                float.IsNaN(vanillaMaximumInteractDistance) ||
                float.IsInfinity(vanillaMaximumInteractDistance) ||
                squaredDistance < 0f || vanillaMaximumInteractDistance <= 0f) return false;
            float effective = Math.Min(
                vanillaMaximumInteractDistance,
                HardMaximumPhysicalReachMeters);
            return squaredDistance <= effective * effective;
        }
    }

    internal static class HoverSnapshotBounds
    {
        internal const int MaximumStacks = 1024;
        internal const int MaximumCustomDataEntries = 4096;
        internal const int MinimumEncodedCharacters = 16384;
        internal const int MaximumEncodedCharacters = 1048576;

        internal static bool AllowsEnvelope(
            int stackCount,
            int persistedEncodedCharacters,
            int configuredEncodedCharacterLimit)
        {
            if (stackCount < 0 || stackCount > MaximumStacks ||
                persistedEncodedCharacters < 0 || persistedEncodedCharacters % 4 != 0) return false;
            int limit = Math.Max(
                MinimumEncodedCharacters,
                Math.Min(MaximumEncodedCharacters, configuredEncodedCharacterLimit));
            return persistedEncodedCharacters <= limit;
        }

        internal static int SerializedByteCeiling(int configuredEncodedCharacterLimit)
        {
            int limit = Math.Max(
                MinimumEncodedCharacters,
                Math.Min(MaximumEncodedCharacters, configuredEncodedCharacterLimit));
            return limit / 4 * 3;
        }

        internal static int EncodedCharacterCeiling(int configuredEncodedCharacterLimit) =>
            Math.Max(
                MinimumEncodedCharacters,
                Math.Min(MaximumEncodedCharacters, configuredEncodedCharacterLimit));

        internal static int EncodedLengthForBytes(int serializedBytes)
        {
            if (serializedBytes < 0) return -1;
            long encoded = ((long)serializedBytes + 2L) / 3L * 4L;
            return encoded > int.MaxValue ? -1 : (int)encoded;
        }

        /// <summary>
        /// Validates the actual serialized snapshot after the conservative pre-serialization
        /// shape check has used the configured allocation ceiling. The shape estimate is
        /// larger than Valheim's exact payload and must never be constrained to
        /// this exact persisted length.
        /// </summary>
        internal static bool MatchesExactSerializedSize(
            int serializedBytes,
            int persistedEncodedCharacters,
            int configuredEncodedCharacterLimit) =>
            serializedBytes >= 0 &&
            serializedBytes <= SerializedByteCeiling(configuredEncodedCharacterLimit) &&
            EncodedLengthForBytes(serializedBytes) == persistedEncodedCharacters;

        internal static bool AllowsCustomDataAddition(int currentEntries, int additionalEntries) =>
            currentEntries >= 0 &&
            additionalEntries >= 0 &&
            currentEntries <= MaximumCustomDataEntries - additionalEntries;

        internal static bool TryAddStringEstimate(
            long currentBytes,
            int utf16Characters,
            int maximumSerializedBytes,
            out long nextBytes)
        {
            nextBytes = currentBytes;
            if (currentBytes < 0L || utf16Characters < 0 || maximumSerializedBytes <= 0 ||
                currentBytes > maximumSerializedBytes || utf16Characters > maximumSerializedBytes)
                return false;
            long candidate = currentBytes + 8L + (long)utf16Characters * 4L;
            if (candidate > maximumSerializedBytes) return false;
            nextBytes = candidate;
            return true;
        }
    }

    /// <summary>
    /// A stable, bounded identity for the exact persisted inventory payload. The runtime also
    /// compares the source strings ordinally before treating matching digests as exact; the two
    /// hashes make changed payloads cheap to reject without retaining another large byte copy.
    /// </summary>
    internal readonly struct HoverPersistedEvidence : IEquatable<HoverPersistedEvidence>
    {
        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;
        private const ulong SecondSeed = 7809847782465536322UL;

        private HoverPersistedEvidence(
            int encodedCharacters,
            bool admissible,
            ulong firstDigest,
            ulong secondDigest)
        {
            EncodedCharacters = encodedCharacters;
            IsAdmissible = admissible;
            FirstDigest = firstDigest;
            SecondDigest = secondDigest;
        }

        internal int EncodedCharacters { get; }
        internal bool IsAdmissible { get; }
        internal ulong FirstDigest { get; }
        internal ulong SecondDigest { get; }

        internal static HoverPersistedEvidence Capture(
            string persisted,
            int configuredEncodedCharacterLimit)
        {
            persisted ??= string.Empty;
            int length = persisted.Length;
            int ceiling = HoverSnapshotBounds.EncodedCharacterCeiling(
                configuredEncodedCharacterLimit);
            if (length > ceiling || length % 4 != 0)
                return new HoverPersistedEvidence(length, false, 0UL, 0UL);

            ulong first = FnvOffset;
            ulong second = SecondSeed;
            for (int index = 0; index < length; index++)
            {
                ushort value = persisted[index];
                first = Mix(first, (byte)value);
                first = Mix(first, (byte)(value >> 8));
                // A separate lane and byte order make accidental same-length collisions less
                // likely; exact ordinal comparison remains the final equality proof.
                second = Mix(second, (byte)(value >> 8));
                second = Mix(second, (byte)value);
            }
            return new HoverPersistedEvidence(length, true, first, second);
        }

        public bool Equals(HoverPersistedEvidence other) =>
            EncodedCharacters == other.EncodedCharacters &&
            IsAdmissible == other.IsAdmissible &&
            (!IsAdmissible ||
             FirstDigest == other.FirstDigest && SecondDigest == other.SecondDigest);

        public override bool Equals(object obj) =>
            obj is HoverPersistedEvidence other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(EncodedCharacters, IsAdmissible, FirstDigest, SecondDigest);

        private static ulong Mix(ulong current, byte value) =>
            (current ^ value) * FnvPrime;
    }

    internal enum HoverCacheDecision
    {
        Reuse,
        SuppressUntilRetry,
        Refresh
    }

    internal static class HoverCachePolicy
    {
        internal static HoverCacheDecision Decide(
            bool verified,
            bool samePersistedEvidence,
            long cachedConfigurationGeneration,
            long currentConfigurationGeneration,
            float nextRetryAt,
            float now)
        {
            bool sameState =
                samePersistedEvidence &&
                cachedConfigurationGeneration == currentConfigurationGeneration;
            if (verified && sameState) return HoverCacheDecision.Reuse;
            return sameState && now < nextRetryAt
                ? HoverCacheDecision.SuppressUntilRetry
                : HoverCacheDecision.Refresh;
        }
    }

    internal static class HoverLabelPolicy
    {
        internal const int MaximumLocalizationTokenCharacters = 128;
        internal const int MaximumDisplayLabelCharacters = 48;
        private const int MaximumDisplaySourceInspectionCharacters = 256;

        internal static bool TryPrepareLocalizationToken(string value, out string token)
        {
            token = null;
            if (string.IsNullOrEmpty(value) ||
                value.Length > MaximumLocalizationTokenCharacters) return false;

            bool hasVisibleCharacter = false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                UnicodeCategory category = char.GetUnicodeCategory(character);
                if (char.IsControl(character) || char.IsSurrogate(character) ||
                    category == UnicodeCategory.Format ||
                    character == '<' || character == '>') return false;
                if (!char.IsWhiteSpace(character)) hasVisibleCharacter = true;
            }
            if (!hasVisibleCharacter) return false;
            token = value;
            return true;
        }

        internal static string WithoutLocalizationMarker(string token)
        {
            if (string.IsNullOrEmpty(token)) return string.Empty;
            int start = 0;
            while (start < token.Length && token[start] == '$') start++;
            return start == 0 ? token : token.Substring(start);
        }

        internal static bool TryGetTranslationKey(string token, out string key)
        {
            key = null;
            if (string.IsNullOrEmpty(token) || token.Length < 2 || token[0] != '$')
                return false;
            int start = 1;
            while (start < token.Length && token[start] == '$') start++;
            if (start == token.Length) return false;
            key = token.Substring(start);
            if (key.StartsWith("KEY_", StringComparison.Ordinal))
            {
                key = null;
                return false;
            }
            return true;
        }

        internal static string NormalizeDisplayLabel(string value, string fallback)
        {
            string source = string.IsNullOrEmpty(value) ? fallback : value;
            if (string.IsNullOrEmpty(source)) return "Item";
            int inspectionLimit = Math.Min(
                source.Length,
                MaximumDisplaySourceInspectionCharacters);
            var builder = new StringBuilder(
                Math.Min(inspectionLimit, MaximumDisplayLabelCharacters));
            bool previousWhitespace = false;
            for (int index = 0;
                 index < inspectionLimit && builder.Length < MaximumDisplayLabelCharacters;
                 index++)
            {
                char character = source[index];
                bool whitespace =
                    char.IsWhiteSpace(character) ||
                    char.IsControl(character) ||
                    char.IsSurrogate(character) ||
                    char.GetUnicodeCategory(character) == UnicodeCategory.Format;
                if (whitespace)
                {
                    if (builder.Length != 0 && !previousWhitespace) builder.Append(' ');
                    previousWhitespace = true;
                    continue;
                }
                previousWhitespace = false;
                builder.Append(character == '<' ? '‹' : character == '>' ? '›' : character);
            }
            while (builder.Length > 0 && builder[builder.Length - 1] == ' ')
                builder.Length--;
            if (builder.Length != 0) return builder.ToString();
            return !ReferenceEquals(source, fallback)
                ? NormalizeDisplayLabel(fallback, null)
                : "Item";
        }
    }

    internal static class HoverBaseTextPolicy
    {
        // Vanilla text is much smaller. This ceiling prevents another mod's hostile hover string
        // from being duplicated and retained in each of the bounded cache's 512 entries.
        internal const int MaximumBaseHoverCharacters = 8192;

        internal static bool Allows(string hoverText) =>
            !string.IsNullOrEmpty(hoverText) &&
            hoverText.Length <= MaximumBaseHoverCharacters;
    }

    internal readonly struct HoverContentEntry
    {
        internal HoverContentEntry(string resourceId, string displayName, int quantity)
        {
            ResourceId = resourceId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Quantity = quantity;
        }

        internal string ResourceId { get; }
        internal string DisplayName { get; }
        internal int Quantity { get; }
    }

    internal static class ContainerHoverSummaryFormatter
    {
        private const string Header = "\n<color=#D8C48A>Contents:</color> ";

        internal static string Format(
            IReadOnlyList<HoverContentEntry> entries,
            int maximumKinds,
            int itemsPerLine,
            int maximumCharacters,
            int unscannedStacks = 0)
        {
            if (maximumKinds < 1) throw new ArgumentOutOfRangeException(nameof(maximumKinds));
            if (itemsPerLine < 1) throw new ArgumentOutOfRangeException(nameof(itemsPerLine));
            if (maximumCharacters < Header.Length + 5)
                throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
            if (unscannedStacks < 0)
                throw new ArgumentOutOfRangeException(nameof(unscannedStacks));

            List<AggregatedKind> aggregates = Aggregate(entries);
            var builder = new StringBuilder(Math.Min(maximumCharacters, 512));
            builder.Append(Header);
            if (aggregates.Count == 0 && unscannedStacks == 0)
            {
                builder.Append("Empty");
                return builder.ToString();
            }

            int kindLimit = Math.Min(maximumKinds, aggregates.Count);
            var boundaries = new List<int>(kindLimit);
            int shown = 0;
            for (int index = 0; index < kindLimit; index++)
            {
                AggregatedKind aggregate = aggregates[index];
                string separator = Separator(shown, itemsPerLine);
                string quantity = aggregate.Quantity.ToString(CultureInfo.InvariantCulture);
                int required = separator.Length + aggregate.DisplayName.Length + 2 + quantity.Length;
                if (builder.Length + required > maximumCharacters) break;
                boundaries.Add(builder.Length);
                builder.Append(separator);
                builder.Append(aggregate.DisplayName);
                builder.Append(" ×");
                builder.Append(quantity);
                shown++;
            }

            int omitted = aggregates.Count - shown;
            if (omitted > 0 || unscannedStacks > 0)
            {
                string marker = OmissionMarker(omitted, unscannedStacks);
                while (shown > 0)
                {
                    string separator = Separator(shown, itemsPerLine);
                    if (builder.Length + separator.Length + marker.Length <= maximumCharacters) break;
                    shown--;
                    builder.Length = boundaries[shown];
                    omitted++;
                    marker = OmissionMarker(omitted, unscannedStacks);
                }
                string finalSeparator = Separator(shown, itemsPerLine);
                if (builder.Length + finalSeparator.Length + marker.Length > maximumCharacters)
                    marker = CompactOmissionMarker(omitted, unscannedStacks);
                if (builder.Length + finalSeparator.Length + marker.Length <= maximumCharacters)
                {
                    builder.Append(finalSeparator);
                    builder.Append(marker);
                }
            }

            return builder.ToString();
        }

        private static List<AggregatedKind> Aggregate(IReadOnlyList<HoverContentEntry> entries)
        {
            var byResource = new Dictionary<string, AggregatedKind>(StringComparer.Ordinal);
            if (entries != null)
            {
                for (int index = 0; index < entries.Count; index++)
                {
                    HoverContentEntry entry = entries[index];
                    string resourceId = NormalizeResourceId(entry.ResourceId);
                    if (resourceId.Length == 0 || entry.Quantity <= 0) continue;
                    string displayName = HoverLabelPolicy.NormalizeDisplayLabel(
                        entry.DisplayName,
                        resourceId);
                    if (byResource.TryGetValue(resourceId, out AggregatedKind existing))
                    {
                        existing.Quantity = AddSaturated(existing.Quantity, entry.Quantity);
                        if (StringComparer.Ordinal.Compare(displayName, existing.DisplayName) < 0)
                            existing.DisplayName = displayName;
                    }
                    else
                    {
                        byResource.Add(resourceId, new AggregatedKind(resourceId, displayName, entry.Quantity));
                    }
                }
            }

            var result = new List<AggregatedKind>(byResource.Values);
            result.Sort(CompareAggregates);
            return result;
        }

        private static string OmissionMarker(int omittedKinds, int unscannedStacks)
        {
            if (unscannedStacks == 0)
                return "+" + omittedKinds.ToString(CultureInfo.InvariantCulture) + " more";
            if (omittedKinds == 0)
                return "+" + unscannedStacks.ToString(CultureInfo.InvariantCulture) + " unscanned stacks";
            return "+" + omittedKinds.ToString(CultureInfo.InvariantCulture) + " more kinds; " +
                   unscannedStacks.ToString(CultureInfo.InvariantCulture) + " unscanned stacks";
        }

        private static string CompactOmissionMarker(int omittedKinds, int unscannedStacks)
        {
            if (unscannedStacks == 0)
                return "+" + omittedKinds.ToString(CultureInfo.InvariantCulture) + " more";
            if (omittedKinds == 0)
                return "+" + unscannedStacks.ToString(CultureInfo.InvariantCulture) + " stacks";
            return "+" + omittedKinds.ToString(CultureInfo.InvariantCulture) + "k/+" +
                   unscannedStacks.ToString(CultureInfo.InvariantCulture) + "s";
        }

        private static string Separator(int shown, int itemsPerLine)
        {
            if (shown == 0) return string.Empty;
            return shown % itemsPerLine == 0 ? "\n" : ", ";
        }

        private static int CompareAggregates(AggregatedKind left, AggregatedKind right)
        {
            int display = StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName);
            if (display != 0) return display;
            display = StringComparer.Ordinal.Compare(left.DisplayName, right.DisplayName);
            return display != 0
                ? display
                : StringComparer.Ordinal.Compare(left.ResourceId, right.ResourceId);
        }

        private static string NormalizeResourceId(string value) =>
            string.IsNullOrWhiteSpace(value) || value.Length > 256 ? string.Empty : value.Trim();

        private static int AddSaturated(int left, int right) =>
            left > int.MaxValue - right ? int.MaxValue : left + right;

        private sealed class AggregatedKind
        {
            internal AggregatedKind(string resourceId, string displayName, int quantity)
            {
                ResourceId = resourceId;
                DisplayName = displayName;
                Quantity = quantity;
            }

            internal string ResourceId { get; }
            internal string DisplayName { get; set; }
            internal int Quantity { get; set; }
        }
    }
}
