using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RunicProduction.Contracts;
using RunicProduction.Core;

namespace RunicProduction.Integration
{
    internal enum MultiReplenishmentCatalogCommitCode
    {
        Committed = 0,
        NoChange = 1,
        Absent = 2,
        Invalid = 3,
        Conflict = 4,
        Rejected = 5
    }

    /// <summary>A fully bounded, canonical final publication used by the ZDO store and pure tests.</summary>
    internal sealed class MultiReplenishmentCatalogPublication
    {
        private readonly string[] _slotRecords;

        internal MultiReplenishmentCatalogPublication(
            MultiReplenishmentCatalog catalog,
            string finalIndexRecord,
            string[] slotRecords,
            int aggregateEncodedCharacters,
            int aggregateDecodedBytes,
            object snapshot)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            FinalIndexRecord = finalIndexRecord ?? throw new ArgumentNullException(nameof(finalIndexRecord));
            if (slotRecords == null ||
                slotRecords.Length != MultiReplenishmentCatalog.HardMaximumDestinations)
                throw new ArgumentException("Exactly sixteen fixed slot values are required.", nameof(slotRecords));
            _slotRecords = (string[])slotRecords.Clone();
            AggregateEncodedCharacters = aggregateEncodedCharacters;
            AggregateDecodedBytes = aggregateDecodedBytes;
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        internal MultiReplenishmentCatalog Catalog { get; }
        internal string FinalIndexRecord { get; }
        internal int AggregateEncodedCharacters { get; }
        internal int AggregateDecodedBytes { get; }
        internal string SlotRecord(int slot) => _slotRecords[slot] ?? string.Empty;
        internal IReadOnlyList<string> SlotRecords =>
            new ReadOnlyCollection<string>((string[])_slotRecords.Clone());
        internal object Snapshot { get; }
    }

    /// <summary>
    /// Copy-on-intent persistence for up to sixteen replenishment destinations. A compact index is
    /// authoritative and points only at sixteen fixed slot keys. Updates first publish an index
    /// transition containing both old and new slot proofs, then replace at most one slot, and then
    /// publish the final index. A restart can therefore resolve every individual crash boundary to
    /// either the complete old catalog or the complete new catalog; mixed records never authorize.
    /// </summary>
    internal static class MultiReplenishmentCatalogStore
    {
        internal const int MaximumAggregateEncodedCharacters = 64 * 1024;
        internal const int MaximumAggregateDecodedBytes = 64 * 1024;

        private const int LegacySchemaVersion = 1;
        private const int StableIdentitySchemaVersion = 2;
        private const int SchemaVersion = 3;
        private const int DigestBytes = 32;
        private const int MaximumStableTextCharacters = 256;
        private static readonly string IndexKey =
            Plugin.ModuleId + ".stock.destinations.index";
        private static readonly string[] SlotKeys = CreateSlotKeys();

        internal static string IndexStorageKey => IndexKey;
        internal static IReadOnlyList<string> FixedSlotStorageKeys =>
            new ReadOnlyCollection<string>((string[])SlotKeys.Clone());

        internal static StoredRecordState Read(
            ZDO stationZdo,
            out MultiReplenishmentCatalog catalog)
        {
            catalog = null;
            if (stationZdo == null) return StoredRecordState.Absent;
            return Parse(
                stationZdo.GetString(IndexKey, string.Empty),
                ReadSlots(stationZdo),
                out catalog,
                out _);
        }

        /// <summary>
        /// Returns a canonical digest/revision for the selected Replenishment catalog only.
        /// Physical transition/orphan slot details and unrelated Input/Output role records are
        /// excluded. A valid transition is classified by the complete old-or-new catalog chosen
        /// by the existing crash truth table.
        /// </summary>
        internal static bool TryCaptureCanonicalState(
            ZDO stationZdo,
            out MultiReplenishmentCatalog catalog,
            out string fingerprint,
            out string semanticRevision,
            out string failure)
        {
            catalog = null;
            fingerprint = string.Empty;
            semanticRevision = string.Empty;
            failure = "production-replenishment-catalog-unavailable";
            if (stationZdo == null || !stationZdo.IsValid()) return false;
            StoredRecordState state = Read(stationZdo, out catalog);
            if (state == StoredRecordState.Invalid)
            {
                failure = "production-replenishment-catalog-corrupt";
                return false;
            }
            if (state == StoredRecordState.Absent)
            {
                fingerprint = CanonicalAbsentFingerprint();
                semanticRevision = "production.replenishment.absent.v1";
                failure = "ok";
                return true;
            }
            try
            {
                fingerprint = CanonicalFingerprint(catalog);
                semanticRevision = "production.replenishment.v1." +
                    catalog.Revision.ToString(CultureInfo.InvariantCulture);
                failure = "ok";
                return true;
            }
            catch
            {
                catalog = null;
                fingerprint = string.Empty;
                semanticRevision = string.Empty;
                failure = "production-replenishment-catalog-canonicalization-failed";
                return false;
            }
        }

        internal static string CanonicalFingerprint(MultiReplenishmentCatalog catalog)
        {
            if (catalog == null) return CanonicalAbsentFingerprint();
            MultiReplenishmentCatalogPublication publication = Prepare(catalog);
            using (var stream = new System.IO.MemoryStream())
            using (var writer = new System.IO.BinaryWriter(
                       stream, new UTF8Encoding(false, true), true))
            {
                writer.Write("production.replenishment.catalog.v1");
                writer.Write(publication.FinalIndexRecord);
                for (int slot = 0; slot < MultiReplenishmentCatalog.HardMaximumDestinations; slot++)
                    writer.Write(publication.SlotRecord(slot));
                writer.Flush();
                using SHA256 algorithm = SHA256.Create();
                return LowerHex(algorithm.ComputeHash(stream.ToArray()));
            }
        }

        /// <summary>
        /// Encodes exactly one destination record for a persistent add/refresh delta. This stays
        /// bounded independently from the complete sixteen-slot catalog and can therefore be
        /// retained in a bounded metadata record without truncating catalog capacity.
        /// </summary>
        internal static byte[] EncodeCanonicalDestinationDelta(
            MultiReplenishmentCatalog catalog,
            ReplenishmentDestinationRecord destination)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            return EncodeCanonicalDestinationDelta(
                catalog.CatalogId, catalog.StationId, destination);
        }

        internal static byte[] EncodeCanonicalDestinationDelta(
            string catalogId,
            string stationId,
            ReplenishmentDestinationRecord destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            byte[] bytes = SerializeSlotBytes(catalogId, stationId, destination);
            SlotEnvelope parsed = ParseSlotBytes(bytes);
            if (!string.Equals(parsed.CatalogId, catalogId, StringComparison.Ordinal) ||
                !string.Equals(parsed.StationId, stationId, StringComparison.Ordinal) ||
                !string.Equals(
                    Encode(SerializeSlotBytes(catalogId, stationId, parsed.Record)),
                    Encode(bytes),
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "The replenishment destination delta did not round-trip canonically.");
            return bytes;
        }

        internal static bool TryDecodeCanonicalDestinationDelta(
            byte[] bytes,
            out string catalogId,
            out string stationId,
            out ReplenishmentDestinationRecord destination)
        {
            catalogId = string.Empty;
            stationId = string.Empty;
            destination = null;
            try
            {
                SlotEnvelope parsed = ParseSlotBytes(bytes);
                byte[] canonical = SerializeSlotBytes(
                    parsed.CatalogId, parsed.StationId, parsed.Record);
                if (!ByteEqual(bytes, canonical)) return false;
                catalogId = parsed.CatalogId;
                stationId = parsed.StationId;
                destination = parsed.Record;
                return true;
            }
            catch
            {
                catalogId = string.Empty;
                stationId = string.Empty;
                destination = null;
                return false;
            }
        }

        private static string CanonicalAbsentFingerprint()
        {
            using SHA256 algorithm = SHA256.Create();
            return LowerHex(algorithm.ComputeHash(
                Encoding.UTF8.GetBytes("production.replenishment.catalog.absent.v1")));
        }

        private static string LowerHex(byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes)
                text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        private static bool ByteEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int index = 0; index < left.Length; index++)
                difference |= left[index] ^ right[index];
            return difference == 0;
        }

        /// <summary>
        /// Resolves a persistent transition and replaces it with the canonical final index. Orphaned
        /// fixed slots are cleared only after that final authoritative write.
        /// </summary>
        internal static StoredRecordState Normalize(
            ZDO stationZdo,
            out MultiReplenishmentCatalog catalog)
        {
            catalog = null;
            if (stationZdo == null) return StoredRecordState.Absent;
            string[] slots = ReadSlots(stationZdo);
            StoredRecordState state = Parse(
                stationZdo.GetString(IndexKey, string.Empty),
                slots,
                out catalog,
                out bool wasTransition);
            if (state != StoredRecordState.Valid || !wasTransition) return state;
            MultiReplenishmentCatalogPublication publication = Prepare(catalog);
            stationZdo.Set(IndexKey, publication.FinalIndexRecord);
            ClearUnreferencedSlots(stationZdo, publication);
            return StoredRecordState.Valid;
        }

        /// <summary>
        /// Initializes an absent store. Slot bodies are written first; without the final index they
        /// remain inert orphans, so an interrupted initialization is safe to retry.
        /// </summary>
        internal static MultiReplenishmentCatalogCommitCode TryInitialize(
            ZDO stationZdo,
            MultiReplenishmentCatalog initial)
        {
            if (stationZdo == null) throw new ArgumentNullException(nameof(stationZdo));
            if (initial == null) throw new ArgumentNullException(nameof(initial));
            string existing = stationZdo.GetString(IndexKey, string.Empty);
            if (!string.IsNullOrEmpty(existing))
                return Read(stationZdo, out MultiReplenishmentCatalog current) ==
                       StoredRecordState.Valid && CatalogsEqual(current, initial)
                    ? MultiReplenishmentCatalogCommitCode.NoChange
                    : MultiReplenishmentCatalogCommitCode.Conflict;

            MultiReplenishmentCatalogPublication publication;
            try
            {
                publication = Prepare(initial);
            }
            catch
            {
                return MultiReplenishmentCatalogCommitCode.Rejected;
            }
            for (int slot = 0; slot < SlotKeys.Length; slot++)
                if (!string.IsNullOrEmpty(publication.SlotRecord(slot)))
                    stationZdo.Set(SlotKeys[slot], publication.SlotRecord(slot));
            stationZdo.Set(IndexKey, publication.FinalIndexRecord);
            ClearUnreferencedSlots(stationZdo, publication);
            return Read(stationZdo, out MultiReplenishmentCatalog verified) ==
                       StoredRecordState.Valid && CatalogsEqual(verified, initial)
                ? MultiReplenishmentCatalogCommitCode.Committed
                : MultiReplenishmentCatalogCommitCode.Invalid;
        }

        /// <summary>
        /// Commits exactly one pure policy mutation with optimistic catalog revision checking.
        /// Add, refresh/state, and remove respectively change one slot by addition, replacement,
        /// or removal; cursor-only updates change none.
        /// </summary>
        internal static MultiReplenishmentCatalogCommitCode TryCommit(
            ZDO stationZdo,
            MultiReplenishmentCatalog expected,
            MultiReplenishmentCatalog updated)
        {
            if (stationZdo == null) throw new ArgumentNullException(nameof(stationZdo));
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            if (updated == null) throw new ArgumentNullException(nameof(updated));

            StoredRecordState normalized = Normalize(stationZdo, out MultiReplenishmentCatalog current);
            if (normalized == StoredRecordState.Absent)
                return MultiReplenishmentCatalogCommitCode.Absent;
            if (normalized != StoredRecordState.Valid)
                return MultiReplenishmentCatalogCommitCode.Invalid;
            if (!CatalogsEqual(current, expected))
                return MultiReplenishmentCatalogCommitCode.Conflict;
            if (CatalogsEqual(expected, updated))
                return MultiReplenishmentCatalogCommitCode.NoChange;

            MultiReplenishmentCatalogPublication oldPublication;
            MultiReplenishmentCatalogPublication newPublication;
            string transition;
            int changedSlot;
            try
            {
                oldPublication = Prepare(expected);
                newPublication = Prepare(updated);
                transition = CreateTransitionIndex(
                    oldPublication,
                    newPublication,
                    out changedSlot);
            }
            catch
            {
                return MultiReplenishmentCatalogCommitCode.Rejected;
            }

            // A valid final index makes every non-indexed slot inert, so cleanup before intent is
            // safe and keeps the physical aggregate within the same 64 KiB bound as the plan.
            ClearUnreferencedSlots(stationZdo, oldPublication);
            stationZdo.Set(IndexKey, transition);
            if (changedSlot >= 0)
            {
                string replacement = newPublication.SlotRecord(changedSlot);
                if (!string.IsNullOrEmpty(replacement))
                    stationZdo.Set(SlotKeys[changedSlot], replacement);
                // Removal commits in the transition index itself. The old body stays available
                // until the final index has replaced that intent record.
            }
            stationZdo.Set(IndexKey, newPublication.FinalIndexRecord);
            ClearUnreferencedSlots(stationZdo, newPublication);

            return Read(stationZdo, out MultiReplenishmentCatalog verified) ==
                       StoredRecordState.Valid && CatalogsEqual(verified, updated)
                ? MultiReplenishmentCatalogCommitCode.Committed
                : MultiReplenishmentCatalogCommitCode.Invalid;
        }

        internal static MultiReplenishmentCatalogPublication Prepare(
            MultiReplenishmentCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            var descriptors = new List<SlotDescriptor>(catalog.Destinations.Count);
            var slots = new string[MultiReplenishmentCatalog.HardMaximumDestinations];
            foreach (ReplenishmentDestinationRecord destination in catalog.Destinations)
            {
                byte[] bytes = SerializeSlotBytes(catalog, destination);
                string encoded = Encode(bytes);
                slots[destination.Slot] = encoded;
                StoredProductionLink link = destination.Link;
                descriptors.Add(new SlotDescriptor(
                    destination.Slot,
                    destination.Ordinal,
                    destination.RecordRevision,
                    destination.LinkId,
                    link.Target,
                    link.TargetToken,
                    link.TargetPrefabHash,
                    link.Revision,
                    ComputeDigest(bytes)));
            }
            descriptors.Sort((left, right) => left.Slot.CompareTo(right.Slot));
            var snapshot = new IndexSnapshot(
                catalog.CatalogId,
                catalog.StationId,
                catalog.Revision,
                catalog.NextOrdinal,
                catalog.DestinationCursorOrdinal,
                descriptors);
            byte[] indexBytes = SerializeIndexBytes(IndexMode.Final, snapshot, null);
            string index = Encode(indexBytes);
            Measure(index, slots, out int encodedCharacters, out int decodedBytes);
            return new MultiReplenishmentCatalogPublication(
                catalog,
                index,
                slots,
                encodedCharacters,
                decodedBytes,
                snapshot);
        }

        /// <summary>
        /// Creates the one-record intent used between two final publications. Exposed internally so
        /// the crash truth table can be tested without a live ZDO.
        /// </summary>
        internal static string CreateTransitionIndex(
            MultiReplenishmentCatalogPublication oldPublication,
            MultiReplenishmentCatalogPublication newPublication,
            out int changedSlot)
        {
            if (oldPublication == null) throw new ArgumentNullException(nameof(oldPublication));
            if (newPublication == null) throw new ArgumentNullException(nameof(newPublication));
            var oldSnapshot = (IndexSnapshot)oldPublication.Snapshot;
            var newSnapshot = (IndexSnapshot)newPublication.Snapshot;
            ValidateTransition(oldSnapshot, newSnapshot, out changedSlot);
            byte[] transitionBytes = SerializeIndexBytes(
                IndexMode.Transition,
                oldSnapshot,
                newSnapshot);
            string transition = Encode(transitionBytes);

            var worstSlots = new string[MultiReplenishmentCatalog.HardMaximumDestinations];
            for (int slot = 0; slot < worstSlots.Length; slot++)
            {
                string oldValue = oldPublication.SlotRecord(slot);
                string newValue = newPublication.SlotRecord(slot);
                worstSlots[slot] = (oldValue?.Length ?? 0) >= (newValue?.Length ?? 0)
                    ? oldValue
                    : newValue;
            }
            Measure(transition, worstSlots, out _, out _);
            return transition;
        }

        /// <summary>Pure parser for final and in-flight publications using the fixed slot array.</summary>
        internal static StoredRecordState Parse(
            string encodedIndex,
            IReadOnlyList<string> encodedSlots,
            out MultiReplenishmentCatalog catalog,
            out bool wasTransition)
        {
            catalog = null;
            wasTransition = false;
            if (string.IsNullOrEmpty(encodedIndex)) return StoredRecordState.Absent;
            if (encodedSlots == null ||
                encodedSlots.Count != MultiReplenishmentCatalog.HardMaximumDestinations)
                return StoredRecordState.Invalid;
            try
            {
                string[] slots = CopySlots(encodedSlots);
                // Encoded bytes dominate decoded Base64 bytes, so checking the physical encoded
                // aggregate first also bounds any unreferenced orphan that will be cleaned later.
                long encodedTotal = encodedIndex.Length;
                foreach (string slot in slots) encodedTotal += slot?.Length ?? 0;
                if (encodedTotal > MaximumAggregateEncodedCharacters)
                    return StoredRecordState.Invalid;

                byte[] indexBytes = Decode(encodedIndex);
                if (indexBytes.Length == 0 || indexBytes.Length > MaximumAggregateDecodedBytes)
                    return StoredRecordState.Invalid;
                var package = new ZPackage(indexBytes);
                int schema = package.ReadInt();
                if (!IsSupportedSchema(schema))
                    return StoredRecordState.Invalid;
                var mode = (IndexMode)package.ReadInt();
                if (!Enum.IsDefined(typeof(IndexMode), mode))
                    return StoredRecordState.Invalid;
                IndexSnapshot first = ReadSnapshot(package, schema);
                IndexSnapshot second = mode == IndexMode.Transition
                    ? ReadSnapshot(package, schema)
                    : null;
                if (package.GetPos() != package.Size()) return StoredRecordState.Invalid;

                if (mode == IndexMode.Final)
                {
                    if (!TryLoad(first, slots, out catalog, out int decodedSlots))
                        return StoredRecordState.Invalid;
                    if (indexBytes.Length + decodedSlots > MaximumAggregateDecodedBytes)
                    {
                        catalog = null;
                        return StoredRecordState.Invalid;
                    }
                    return StoredRecordState.Valid;
                }

                wasTransition = true;
                ValidateTransition(first, second, out _);
                bool oldValid = TryLoad(first, slots, out MultiReplenishmentCatalog oldCatalog,
                    out int oldDecoded);
                bool newValid = TryLoad(second, slots, out MultiReplenishmentCatalog newCatalog,
                    out int newDecoded);
                if (newValid && indexBytes.Length + newDecoded <= MaximumAggregateDecodedBytes)
                {
                    catalog = newCatalog;
                    return StoredRecordState.Valid;
                }
                if (oldValid && indexBytes.Length + oldDecoded <= MaximumAggregateDecodedBytes)
                {
                    catalog = oldCatalog;
                    return StoredRecordState.Valid;
                }
                catalog = null;
                return StoredRecordState.Invalid;
            }
            catch
            {
                catalog = null;
                wasTransition = false;
                return StoredRecordState.Invalid;
            }
        }

        internal static bool CatalogsEqual(
            MultiReplenishmentCatalog left,
            MultiReplenishmentCatalog right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;
            try
            {
                return string.Equals(
                    Prepare(left).FinalIndexRecord,
                    Prepare(right).FinalIndexRecord,
                    StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryLoad(
            IndexSnapshot snapshot,
            string[] slots,
            out MultiReplenishmentCatalog catalog,
            out int decodedSlotBytes)
        {
            catalog = null;
            decodedSlotBytes = 0;
            try
            {
                var records = new List<ReplenishmentDestinationRecord>(snapshot.Descriptors.Count);
                foreach (SlotDescriptor descriptor in snapshot.Descriptors)
                {
                    string encoded = slots[descriptor.Slot];
                    if (string.IsNullOrEmpty(encoded)) return false;
                    byte[] bytes = Decode(encoded);
                    decodedSlotBytes = checked(decodedSlotBytes + bytes.Length);
                    if (!DigestMatches(descriptor.Digest, ComputeDigest(bytes))) return false;
                    SlotEnvelope envelope = ParseSlotBytes(bytes);
                    StoredProductionLink link = envelope.Record.Link;
                    if (!string.Equals(
                            envelope.CatalogId,
                            snapshot.CatalogId,
                            StringComparison.Ordinal) ||
                        !string.Equals(
                            envelope.StationId,
                            snapshot.StationId,
                            StringComparison.Ordinal) ||
                        envelope.Record.Slot != descriptor.Slot ||
                        envelope.Record.Ordinal != descriptor.Ordinal ||
                        envelope.Record.RecordRevision != descriptor.RecordRevision ||
                        !string.Equals(
                            envelope.Record.LinkId,
                            descriptor.LinkId,
                            StringComparison.Ordinal) ||
                        link.Target != descriptor.Target ||
                        link.TargetPrefabHash != descriptor.TargetPrefabHash ||
                        !string.Equals(
                            link.TargetToken,
                            descriptor.TargetToken,
                            StringComparison.Ordinal) ||
                        link.Revision != descriptor.LinkRevision)
                        return false;
                    records.Add(envelope.Record);
                }
                catalog = new MultiReplenishmentCatalog(
                    snapshot.CatalogId,
                    snapshot.StationId,
                    snapshot.CatalogRevision,
                    snapshot.NextOrdinal,
                    snapshot.CursorOrdinal,
                    records);
                return true;
            }
            catch
            {
                catalog = null;
                decodedSlotBytes = 0;
                return false;
            }
        }

        private static byte[] SerializeSlotBytes(
            MultiReplenishmentCatalog catalog,
            ReplenishmentDestinationRecord destination) => SerializeSlotBytes(
                catalog?.CatalogId ?? throw new ArgumentNullException(nameof(catalog)),
                catalog.StationId,
                destination);

        private static byte[] SerializeSlotBytes(
            string catalogId,
            string stationId,
            ReplenishmentDestinationRecord destination)
        {
            if (string.IsNullOrEmpty(catalogId) || string.IsNullOrEmpty(stationId) ||
                destination == null) throw new ArgumentException(
                "A complete catalog destination identity is required.");
            var package = new ZPackage();
            package.Write(SchemaVersion);
            package.Write(catalogId);
            package.Write(stationId);
            package.Write(destination.Slot);
            package.Write(destination.Ordinal);
            package.Write(destination.RecordRevision);
            package.Write((int)destination.State);
            package.Write((int)destination.FaultCode);
            WriteStoredLink(package, destination.Link, includeStableIdentity: true);
            WritePlan(package, destination.Plan, includeStableIdentity: true);
            byte[] bytes = package.GetArray();
            if (bytes.Length == 0 || bytes.Length > MaximumAggregateDecodedBytes)
                throw new InvalidOperationException(
                    "A replenishment destination exceeds its decoded storage bound.");
            return bytes;
        }

        private static SlotEnvelope ParseSlotBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaximumAggregateDecodedBytes)
                throw new InvalidOperationException("The destination slot is outside its size bound.");
            var package = new ZPackage(bytes);
            int schema = package.ReadInt();
            if (!IsSupportedSchema(schema))
                throw new InvalidOperationException("Unsupported destination slot schema.");
            string catalogId = ReadStableText(package, 200);
            string stationId = ReadStableText(package, 200);
            int slot = package.ReadInt();
            long ordinal = package.ReadLong();
            int recordRevision = package.ReadInt();
            var state = (ReplenishmentDestinationState)package.ReadInt();
            var fault = (ProductionStopCode)package.ReadInt();
            StoredProductionLink link = ReadStoredLink(
                package,
                schema >= StableIdentitySchemaVersion);
            ReplenishmentPlan plan = ReadPlan(package, schema, link.OwnerId);
            if (package.GetPos() != package.Size())
                throw new InvalidOperationException("The destination slot contains trailing data.");
            return new SlotEnvelope(
                catalogId,
                stationId,
                new ReplenishmentDestinationRecord(
                    slot,
                    ordinal,
                    recordRevision,
                    state,
                    fault,
                    link,
                    plan));
        }

        private static byte[] SerializeIndexBytes(
            IndexMode mode,
            IndexSnapshot first,
            IndexSnapshot second)
        {
            var package = new ZPackage();
            package.Write(SchemaVersion);
            package.Write((int)mode);
            WriteSnapshot(package, first);
            if (mode == IndexMode.Transition)
                WriteSnapshot(package, second ?? throw new ArgumentNullException(nameof(second)));
            byte[] bytes = package.GetArray();
            if (bytes.Length == 0 || bytes.Length > MaximumAggregateDecodedBytes)
                throw new InvalidOperationException("The catalog index exceeds its decoded size bound.");
            return bytes;
        }

        private static void WriteSnapshot(ZPackage package, IndexSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            package.Write(snapshot.CatalogId);
            package.Write(snapshot.StationId);
            package.Write(snapshot.CatalogRevision);
            package.Write(snapshot.NextOrdinal);
            package.Write(snapshot.CursorOrdinal);
            package.Write(snapshot.Descriptors.Count);
            foreach (SlotDescriptor descriptor in snapshot.Descriptors)
            {
                package.Write(descriptor.Slot);
                package.Write(descriptor.Ordinal);
                package.Write(descriptor.RecordRevision);
                package.Write(descriptor.LinkId);
                package.Write(descriptor.Target);
                package.Write(descriptor.TargetToken);
                package.Write(descriptor.TargetPrefabHash);
                package.Write(descriptor.LinkRevision);
                package.Write(descriptor.CopyDigest());
            }
        }

        private static IndexSnapshot ReadSnapshot(ZPackage package, int schema)
        {
            string catalogId = ReadStableText(package, 200);
            string stationId = ReadStableText(package, 200);
            int catalogRevision = package.ReadInt();
            long nextOrdinal = package.ReadLong();
            long cursorOrdinal = package.ReadLong();
            int count = package.ReadInt();
            if (catalogRevision < 1 || nextOrdinal < 1L || count < 0 ||
                count > MultiReplenishmentCatalog.HardMaximumDestinations)
                throw new InvalidOperationException("Invalid catalog index bounds.");
            var descriptors = new List<SlotDescriptor>(count);
            var slots = new HashSet<int>();
            var ordinals = new HashSet<long>();
            var linkIds = new HashSet<string>(StringComparer.Ordinal);
            var targets = new HashSet<ZDOID>();
            var targetTokens = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < count; index++)
            {
                int slot = package.ReadInt();
                long ordinal = package.ReadLong();
                int recordRevision = package.ReadInt();
                string linkId = ReadStableText(package, 200);
                ZDOID target = package.ReadZDOID();
                string targetToken = schema >= StableIdentitySchemaVersion
                    ? package.ReadString()
                    : string.Empty;
                int targetPrefabHash = schema >= StableIdentitySchemaVersion
                    ? package.ReadInt()
                    : 0;
                int linkRevision = package.ReadInt();
                byte[] digest = package.ReadByteArray();
                var descriptor = new SlotDescriptor(
                    slot,
                    ordinal,
                    recordRevision,
                    linkId,
                    target,
                    targetToken,
                    targetPrefabHash,
                    linkRevision,
                    digest);
                if (!slots.Add(slot) || !ordinals.Add(ordinal) ||
                    !linkIds.Add(linkId) || !targets.Add(target))
                    throw new InvalidOperationException("The catalog index contains duplicates.");
                if (!string.IsNullOrEmpty(targetToken) &&
                    !targetTokens.Add(targetToken))
                    throw new InvalidOperationException(
                        "The catalog index contains duplicate persistent targets.");
                descriptors.Add(descriptor);
            }
            descriptors.Sort((left, right) => left.Slot.CompareTo(right.Slot));
            return new IndexSnapshot(
                catalogId,
                stationId,
                catalogRevision,
                nextOrdinal,
                cursorOrdinal,
                descriptors);
        }

        private static void ValidateTransition(
            IndexSnapshot oldSnapshot,
            IndexSnapshot newSnapshot,
            out int changedSlot)
        {
            if (oldSnapshot == null || newSnapshot == null)
                throw new ArgumentNullException(nameof(newSnapshot));
            if (!string.Equals(
                    oldSnapshot.CatalogId,
                    newSnapshot.CatalogId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    oldSnapshot.StationId,
                    newSnapshot.StationId,
                    StringComparison.Ordinal) ||
                oldSnapshot.CatalogRevision == int.MaxValue ||
                newSnapshot.CatalogRevision != oldSnapshot.CatalogRevision + 1)
                throw new InvalidOperationException(
                    "Catalog transitions must advance one exact station revision.");

            changedSlot = -1;
            for (int slot = 0;
                 slot < MultiReplenishmentCatalog.HardMaximumDestinations;
                 slot++)
            {
                SlotDescriptor oldDescriptor = oldSnapshot.Find(slot);
                SlotDescriptor newDescriptor = newSnapshot.Find(slot);
                if (SlotDescriptor.Same(oldDescriptor, newDescriptor)) continue;
                if (changedSlot >= 0)
                    throw new InvalidOperationException(
                        "A crash-safe catalog mutation may replace at most one fixed slot.");
                changedSlot = slot;
            }
        }

        private static void WriteStoredLink(
            ZPackage package,
            StoredProductionLink link,
            bool includeStableIdentity)
        {
            package.Write(link.LinkId);
            package.Write((int)link.Role);
            package.Write(link.Target);
            package.Write(link.ExpectedPosition);
            package.Write(link.OwnerId);
            package.Write(link.StationOwnerId);
            package.Write(link.TargetOwnerId);
            package.Write(link.Revision);
            if (includeStableIdentity)
            {
                package.Write(link.TargetToken ?? string.Empty);
                package.Write(link.TargetPrefabHash);
            }
        }

        private static StoredProductionLink ReadStoredLink(
            ZPackage package,
            bool includesStableIdentity)
        {
            var link = new StoredProductionLink
            {
                LinkId = ReadStableText(package, 200),
                Role = (ProductionLinkRole)package.ReadInt(),
                Target = package.ReadZDOID(),
                ExpectedPosition = package.ReadVector3(),
                OwnerId = package.ReadLong(),
                StationOwnerId = package.ReadLong(),
                TargetOwnerId = package.ReadLong(),
                Revision = package.ReadInt()
            };
            if (includesStableIdentity)
            {
                link.TargetToken = package.ReadString();
                link.TargetPrefabHash = package.ReadInt();
            }
            else
            {
                link.TargetToken = string.Empty;
                link.TargetPrefabHash = 0;
            }
            return link;
        }

        private static void WritePlan(
            ZPackage package,
            ReplenishmentPlan plan,
            bool includeStableIdentity)
        {
            package.Write(plan.PlanId);
            package.Write(plan.Revision);
            package.Write(plan.Cursor);
            package.Write(plan.StationPrefabId);
            package.Write((int)plan.AdapterKind);
            package.Write(plan.AuthorizedPlayerId);
            package.Write(plan.AuthorizedPlayerName);
            package.Write(plan.Link.LinkId);
            package.Write(plan.Link.StationId);
            package.Write(plan.Link.TargetId);
            if (includeStableIdentity)
            {
                package.Write(plan.Link.TargetToken);
                package.Write(plan.Link.TargetPrefabHash);
            }
            package.Write(plan.Link.Revision);
            package.Write(plan.Targets.Count);
            foreach (ReplenishmentTargetAuthorization target in plan.Targets)
            {
                package.Write(target.OutputPrefabId);
                package.Write((int)target.ProducerKind);
                package.Write(target.ProducerId);
                package.Write(target.CopyProducerSignature());
                package.Write(target.OutputAmount);
                package.Write(target.RequiredStationName);
                package.Write(target.RequiredStationLevel);
                package.Write(target.AuthorizedPlayerId);
                package.Write(target.AuthorizedPlayerName);
                package.Write(target.Requirements.Count);
                foreach (ReplenishmentRequirement requirement in target.Requirements)
                {
                    package.Write(requirement.PrefabId);
                    package.Write(requirement.Amount);
                }
            }
        }

        private static ReplenishmentPlan ReadPlan(
            ZPackage package,
            int schema,
            long expectedOwnerId)
        {
            string planId = ReadStableText(package, 200);
            int revision = package.ReadInt();
            int cursor = package.ReadInt();
            string stationPrefab = package.ReadString();
            var adapterKind = (ReplenishmentProducerKind)package.ReadInt();
            long authorizedPlayerId = schema >= SchemaVersion
                ? package.ReadLong()
                : 0L;
            string authorizedPlayerName = schema >= SchemaVersion
                ? package.ReadString()
                : string.Empty;
            string linkId = ReadStableText(package, 200);
            string stationId = ReadStableText(package, 200);
            string targetId = ReadStableText(package, 200);
            string targetToken = schema >= StableIdentitySchemaVersion
                ? package.ReadString()
                : string.Empty;
            int targetPrefabHash = schema >= StableIdentitySchemaVersion
                ? package.ReadInt()
                : 0;
            var link = new ReplenishmentLinkBinding(
                linkId,
                stationId,
                targetId,
                targetToken,
                targetPrefabHash,
                package.ReadInt());
            int targetCount = package.ReadInt();
            if (targetCount < 0 || targetCount > ReplenishmentPlan.MaximumTargets)
                throw new InvalidOperationException("Invalid replenishment target count.");
            var targets = new List<ReplenishmentTargetAuthorization>(targetCount);
            for (int index = 0; index < targetCount; index++)
            {
                string outputPrefab = package.ReadString();
                var producerKind = (ReplenishmentProducerKind)package.ReadInt();
                string producerId = package.ReadString();
                byte[] signature = package.ReadByteArray();
                int outputAmount = package.ReadInt();
                string requiredStation = package.ReadString();
                int requiredLevel = package.ReadInt();
                long playerId = package.ReadLong();
                string playerName = package.ReadString();
                int requirementCount = package.ReadInt();
                if (requirementCount <= 0 ||
                    requirementCount > ReplenishmentTargetAuthorization.MaximumRequirements)
                    throw new InvalidOperationException("Invalid replenishment requirement count.");
                var requirements = new List<ReplenishmentRequirement>(requirementCount);
                for (int requirement = 0; requirement < requirementCount; requirement++)
                    requirements.Add(new ReplenishmentRequirement(
                        package.ReadString(),
                        package.ReadInt()));
                targets.Add(new ReplenishmentTargetAuthorization(
                    outputPrefab,
                    producerKind,
                    producerId,
                    signature,
                    outputAmount,
                    requiredStation,
                    requiredLevel,
                    playerId,
                    playerName,
                    requirements));
            }
            if (schema < SchemaVersion &&
                !TryDeriveLegacyPrincipal(
                    targets,
                    out authorizedPlayerId,
                    out authorizedPlayerName))
                throw new InvalidOperationException(
                    "A legacy empty or mixed-principal plan cannot be authorized.");
            if (authorizedPlayerId == 0L || authorizedPlayerId != expectedOwnerId)
                throw new InvalidOperationException(
                    "The replenishment plan principal does not match its stored link owner.");
            return new ReplenishmentPlan(
                planId,
                revision,
                cursor,
                stationPrefab,
                adapterKind,
                link,
                authorizedPlayerId,
                authorizedPlayerName,
                targets);
        }

        private static bool TryDeriveLegacyPrincipal(
            IReadOnlyList<ReplenishmentTargetAuthorization> targets,
            out long playerId,
            out string playerName)
        {
            playerId = 0L;
            playerName = string.Empty;
            if (targets == null || targets.Count == 0) return false;
            ReplenishmentTargetAuthorization first = targets[0];
            if (first == null || first.AuthorizedPlayerId == 0L ||
                string.IsNullOrWhiteSpace(first.AuthorizedPlayerName)) return false;
            for (int index = 1; index < targets.Count; index++)
            {
                ReplenishmentTargetAuthorization target = targets[index];
                if (target == null ||
                    target.AuthorizedPlayerId != first.AuthorizedPlayerId ||
                    !string.Equals(
                        target.AuthorizedPlayerName,
                        first.AuthorizedPlayerName,
                        StringComparison.Ordinal)) return false;
            }
            playerId = first.AuthorizedPlayerId;
            playerName = first.AuthorizedPlayerName;
            return true;
        }

        private static bool IsSupportedSchema(int schema) =>
            schema == LegacySchemaVersion ||
            schema == StableIdentitySchemaVersion ||
            schema == SchemaVersion;

        private static string[] ReadSlots(ZDO zdo)
        {
            var slots = new string[SlotKeys.Length];
            for (int slot = 0; slot < slots.Length; slot++)
                slots[slot] = zdo.GetString(SlotKeys[slot], string.Empty);
            return slots;
        }

        private static string[] CopySlots(IReadOnlyList<string> source)
        {
            var copy = new string[MultiReplenishmentCatalog.HardMaximumDestinations];
            for (int slot = 0; slot < copy.Length; slot++)
                copy[slot] = source[slot] ?? string.Empty;
            return copy;
        }

        private static void ClearUnreferencedSlots(
            ZDO zdo,
            MultiReplenishmentCatalogPublication publication)
        {
            for (int slot = 0; slot < SlotKeys.Length; slot++)
                if (string.IsNullOrEmpty(publication.SlotRecord(slot)))
                    zdo.Set(SlotKeys[slot], string.Empty);
        }

        private static void Measure(
            string encodedIndex,
            IReadOnlyList<string> slots,
            out int encodedCharacters,
            out int decodedBytes)
        {
            long encoded = encodedIndex?.Length ?? 0;
            long decoded = string.IsNullOrEmpty(encodedIndex) ? 0 : Decode(encodedIndex).Length;
            foreach (string slot in slots)
            {
                encoded += slot?.Length ?? 0;
                if (!string.IsNullOrEmpty(slot)) decoded += Decode(slot).Length;
            }
            if (encoded <= 0L || encoded > MaximumAggregateEncodedCharacters ||
                decoded <= 0L || decoded > MaximumAggregateDecodedBytes)
                throw new InvalidOperationException(
                    "The replenishment catalog exceeds its aggregate 64 KiB storage bound.");
            encodedCharacters = (int)encoded;
            decodedBytes = (int)decoded;
        }

        private static string Encode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaximumAggregateDecodedBytes)
                throw new InvalidOperationException("The record exceeds its decoded storage bound.");
            string encoded = Convert.ToBase64String(bytes);
            if (encoded.Length > MaximumAggregateEncodedCharacters)
                throw new InvalidOperationException("The record exceeds its encoded storage bound.");
            return encoded;
        }

        private static byte[] Decode(string encoded)
        {
            if (string.IsNullOrEmpty(encoded) ||
                encoded.Length > MaximumAggregateEncodedCharacters)
                throw new InvalidOperationException("The record exceeds its encoded storage bound.");
            byte[] bytes = Convert.FromBase64String(encoded);
            if (bytes.Length == 0 || bytes.Length > MaximumAggregateDecodedBytes)
                throw new InvalidOperationException("The record exceeds its decoded storage bound.");
            return bytes;
        }

        private static byte[] ComputeDigest(byte[] bytes)
        {
            using SHA256 algorithm = SHA256.Create();
            return algorithm.ComputeHash(bytes);
        }

        private static bool DigestMatches(byte[] expected, byte[] actual)
        {
            if (expected == null || actual == null || expected.Length != actual.Length) return false;
            int difference = 0;
            for (int index = 0; index < expected.Length; index++)
                difference |= expected[index] ^ actual[index];
            return difference == 0;
        }

        private static string ReadStableText(ZPackage package, int maximum)
        {
            string value = package.ReadString();
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximum) throw new InvalidOperationException();
            foreach (char character in value)
                if (char.IsControl(character)) throw new InvalidOperationException();
            return value;
        }

        private static string[] CreateSlotKeys()
        {
            var keys = new string[MultiReplenishmentCatalog.HardMaximumDestinations];
            for (int slot = 0; slot < keys.Length; slot++)
                keys[slot] = Plugin.ModuleId + ".stock.destinations.slot." + slot.ToString("00");
            return keys;
        }

        private enum IndexMode
        {
            Final = 1,
            Transition = 2
        }

        private sealed class SlotEnvelope
        {
            internal SlotEnvelope(
                string catalogId,
                string stationId,
                ReplenishmentDestinationRecord record)
            {
                CatalogId = catalogId;
                StationId = stationId;
                Record = record;
            }

            internal string CatalogId { get; }
            internal string StationId { get; }
            internal ReplenishmentDestinationRecord Record { get; }
        }

        private sealed class IndexSnapshot
        {
            private readonly ReadOnlyCollection<SlotDescriptor> _descriptors;

            internal IndexSnapshot(
                string catalogId,
                string stationId,
                int catalogRevision,
                long nextOrdinal,
                long cursorOrdinal,
                IEnumerable<SlotDescriptor> descriptors)
            {
                CatalogId = catalogId;
                StationId = stationId;
                CatalogRevision = catalogRevision;
                NextOrdinal = nextOrdinal;
                CursorOrdinal = cursorOrdinal;
                _descriptors = new List<SlotDescriptor>(descriptors).AsReadOnly();
            }

            internal string CatalogId { get; }
            internal string StationId { get; }
            internal int CatalogRevision { get; }
            internal long NextOrdinal { get; }
            internal long CursorOrdinal { get; }
            internal IReadOnlyList<SlotDescriptor> Descriptors => _descriptors;

            internal SlotDescriptor Find(int slot)
            {
                foreach (SlotDescriptor descriptor in _descriptors)
                    if (descriptor.Slot == slot) return descriptor;
                return null;
            }
        }

        private sealed class SlotDescriptor
        {
            private readonly byte[] _digest;

            internal SlotDescriptor(
                int slot,
                long ordinal,
                int recordRevision,
                string linkId,
                ZDOID target,
                string targetToken,
                int targetPrefabHash,
                int linkRevision,
                byte[] digest)
            {
                if (slot < 0 || slot >= MultiReplenishmentCatalog.HardMaximumDestinations ||
                    ordinal < 1L || recordRevision < 1 || target.IsNone() || linkRevision < 1 ||
                    string.IsNullOrWhiteSpace(linkId) || linkId.Length > 200 ||
                    !StockDomainValidation.IsLegacyOrWorldObjectIdentity(
                        targetToken,
                        targetPrefabHash) ||
                    digest == null || digest.Length != DigestBytes)
                    throw new InvalidOperationException("Invalid catalog slot descriptor.");
                Slot = slot;
                Ordinal = ordinal;
                RecordRevision = recordRevision;
                LinkId = linkId;
                Target = target;
                TargetToken = targetToken ?? string.Empty;
                TargetPrefabHash = targetPrefabHash;
                LinkRevision = linkRevision;
                _digest = (byte[])digest.Clone();
            }

            internal int Slot { get; }
            internal long Ordinal { get; }
            internal int RecordRevision { get; }
            internal string LinkId { get; }
            internal ZDOID Target { get; }
            internal string TargetToken { get; }
            internal int TargetPrefabHash { get; }
            internal int LinkRevision { get; }
            internal byte[] Digest => CopyDigest();
            internal byte[] CopyDigest() => (byte[])_digest.Clone();

            internal static bool Same(SlotDescriptor left, SlotDescriptor right)
            {
                if (ReferenceEquals(left, right)) return true;
                if (left == null || right == null) return false;
                return left.Slot == right.Slot &&
                       left.Ordinal == right.Ordinal &&
                       left.RecordRevision == right.RecordRevision &&
                       left.Target == right.Target &&
                       left.TargetPrefabHash == right.TargetPrefabHash &&
                       left.LinkRevision == right.LinkRevision &&
                       string.Equals(
                           left.TargetToken,
                           right.TargetToken,
                           StringComparison.Ordinal) &&
                       string.Equals(left.LinkId, right.LinkId, StringComparison.Ordinal) &&
                       DigestMatches(left._digest, right._digest);
            }
        }
    }
}
