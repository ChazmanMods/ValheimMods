using System;
using System.Collections.Generic;
using RunicPortals.Api;

namespace RunicPortals.Core
{
    internal sealed class ReturnRoute
    {
        internal ReturnRoute(
            string travelerStableId,
            string originPortalId,
            string arrivalPortalId,
            long originRevision,
            long arrivalRevision,
            long expiresUtcTicks)
        {
            TravelerStableId = travelerStableId;
            OriginPortalId = originPortalId;
            ArrivalPortalId = arrivalPortalId;
            OriginRevision = originRevision;
            ArrivalRevision = arrivalRevision;
            ExpiresUtcTicks = expiresUtcTicks;
        }

        internal string TravelerStableId { get; }
        internal string OriginPortalId { get; }
        internal string ArrivalPortalId { get; }
        internal long OriginRevision { get; }
        internal long ArrivalRevision { get; }
        internal long ExpiresUtcTicks { get; }
    }

    internal sealed class ReturnRouteStore
    {
        private sealed class Slot
        {
            internal ReturnRoute Route;
            internal long Sequence;
        }

        private readonly Dictionary<string, Slot> _routes =
            new Dictionary<string, Slot>(StringComparer.Ordinal);
        private readonly List<string> _expired = new List<string>();
        private readonly int _capacity;
        private long _sequence;

        internal ReturnRouteStore(int capacity)
        {
            if (capacity < 1 || capacity > PortalContractLimits.MaximumReturnRoutes)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        internal int Count => _routes.Count;

        internal void Record(ReturnRoute route, long nowUtcTicks)
        {
            if (route == null) throw new ArgumentNullException(nameof(route));
            Prune(nowUtcTicks);
            if (_routes.TryGetValue(route.TravelerStableId, out Slot existing))
            {
                existing.Route = route;
                existing.Sequence = ++_sequence;
                return;
            }
            if (_routes.Count >= _capacity) EvictOldest();
            _routes.Add(route.TravelerStableId, new Slot { Route = route, Sequence = ++_sequence });
        }

        internal bool TryGet(
            string travelerStableId,
            string currentPortalId,
            long nowUtcTicks,
            out ReturnRoute route)
        {
            route = null;
            if (string.IsNullOrEmpty(travelerStableId) || string.IsNullOrEmpty(currentPortalId)) return false;
            if (!_routes.TryGetValue(travelerStableId, out Slot slot)) return false;
            if (slot.Route.ExpiresUtcTicks <= nowUtcTicks)
            {
                _routes.Remove(travelerStableId);
                return false;
            }
            if (!string.Equals(slot.Route.ArrivalPortalId, currentPortalId, StringComparison.Ordinal))
                return false;
            slot.Sequence = ++_sequence;
            route = slot.Route;
            return true;
        }

        internal bool Remove(string travelerStableId) =>
            !string.IsNullOrEmpty(travelerStableId) && _routes.Remove(travelerStableId);

        internal int Prune(long nowUtcTicks)
        {
            _expired.Clear();
            foreach (KeyValuePair<string, Slot> pair in _routes)
                if (pair.Value.Route.ExpiresUtcTicks <= nowUtcTicks) _expired.Add(pair.Key);
            for (int index = 0; index < _expired.Count; index++) _routes.Remove(_expired[index]);
            return _expired.Count;
        }

        private void EvictOldest()
        {
            string oldestKey = null;
            long oldest = long.MaxValue;
            foreach (KeyValuePair<string, Slot> pair in _routes)
            {
                if (pair.Value.Sequence >= oldest) continue;
                oldest = pair.Value.Sequence;
                oldestKey = pair.Key;
            }
            if (oldestKey != null) _routes.Remove(oldestKey);
        }
    }
}
