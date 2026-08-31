using System;
using System.Collections.Generic;
using RunicPortals.Api;

namespace RunicPortals.Core
{
    internal sealed class PortalSelection
    {
        internal PortalSelection(
            string travelerStableId,
            string sourcePortalId,
            string destinationPortalId,
            long sourceRevision,
            long destinationRevision,
            bool isReturn,
            long selectedUtcTicks,
            string destinationDisplayName = "")
        {
            TravelerStableId = travelerStableId;
            SourcePortalId = sourcePortalId;
            DestinationPortalId = destinationPortalId;
            SourceRevision = sourceRevision;
            DestinationRevision = destinationRevision;
            IsReturn = isReturn;
            SelectedUtcTicks = selectedUtcTicks;
            DestinationDisplayName = PortalText.NormalizeOptional(
                destinationDisplayName,
                PortalContractLimits.MaximumNameLength,
                nameof(destinationDisplayName));
        }

        internal string TravelerStableId { get; }
        internal string SourcePortalId { get; }
        internal string DestinationPortalId { get; }
        internal long SourceRevision { get; }
        internal long DestinationRevision { get; }
        internal bool IsReturn { get; }
        internal long SelectedUtcTicks { get; }
        internal string DestinationDisplayName { get; }
    }

    internal sealed class PortalSelectionStore
    {
        private sealed class Slot
        {
            internal PortalSelection Selection;
            internal long Sequence;
        }

        private readonly Dictionary<string, Slot> _values =
            new Dictionary<string, Slot>(StringComparer.Ordinal);
        private readonly int _capacity;
        private long _sequence;

        internal PortalSelectionStore(int capacity)
        {
            if (capacity < 1 || capacity > PortalContractLimits.MaximumSelections)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        internal int Count => _values.Count;

        internal void Set(PortalSelection selection)
        {
            if (selection == null) throw new ArgumentNullException(nameof(selection));
            if (_values.TryGetValue(selection.TravelerStableId, out Slot slot))
            {
                slot.Selection = selection;
                slot.Sequence = ++_sequence;
                return;
            }
            if (_values.Count >= _capacity) EvictOldest();
            _values.Add(selection.TravelerStableId,
                new Slot { Selection = selection, Sequence = ++_sequence });
        }

        internal bool TryGet(string travelerStableId, string sourcePortalId, out PortalSelection selection)
        {
            selection = null;
            if (!_values.TryGetValue(travelerStableId ?? string.Empty, out Slot slot) ||
                !string.Equals(slot.Selection.SourcePortalId, sourcePortalId, StringComparison.Ordinal))
                return false;
            slot.Sequence = ++_sequence;
            selection = slot.Selection;
            return true;
        }

        internal void Clear() => _values.Clear();

        private void EvictOldest()
        {
            string oldestKey = null;
            long oldest = long.MaxValue;
            foreach (KeyValuePair<string, Slot> pair in _values)
            {
                if (pair.Value.Sequence >= oldest) continue;
                oldest = pair.Value.Sequence;
                oldestKey = pair.Key;
            }
            if (oldestKey != null) _values.Remove(oldestKey);
        }
    }

    internal sealed class OneWayAcknowledgementStore
    {
        private string _traveler;
        private string _source;
        private string _destination;
        private long _expiresUtcTicks;

        internal bool ConsumeOrArm(
            string traveler,
            string source,
            string destination,
            long nowUtcTicks,
            long durationTicks)
        {
            bool matches = _expiresUtcTicks > nowUtcTicks &&
                string.Equals(_traveler, traveler, StringComparison.Ordinal) &&
                string.Equals(_source, source, StringComparison.Ordinal) &&
                string.Equals(_destination, destination, StringComparison.Ordinal);
            if (matches)
            {
                Clear();
                return true;
            }
            _traveler = traveler;
            _source = source;
            _destination = destination;
            _expiresUtcTicks = checked(nowUtcTicks + Math.Max(1L, durationTicks));
            return false;
        }

        internal void Clear()
        {
            _traveler = null;
            _source = null;
            _destination = null;
            _expiresUtcTicks = 0;
        }
    }
}
