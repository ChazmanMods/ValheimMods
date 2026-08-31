using System;
using System.Collections.Generic;
using System.Linq;
using RunicTransactions.Contracts;

namespace RunicTransactions.Coordination
{
    public static class DeterministicLockOrder
    {
        public static IReadOnlyList<EndpointId> OrderEndpoints(IEnumerable<EndpointId> endpoints)
        {
            if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));
            var unique = new HashSet<EndpointId>();
            foreach (EndpointId endpoint in endpoints)
            {
                if (!endpoint.IsValid) throw new ArgumentException("Endpoint list contains an invalid identifier.", nameof(endpoints));
                unique.Add(endpoint);
            }
            return unique.OrderBy(endpoint => endpoint).ToArray();
        }

        public static IReadOnlyList<EndpointId> OrderEndpoints(IEnumerable<ReservationLine> lines)
        {
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            return OrderEndpoints(lines.Select(line => line.Endpoint));
        }
    }
}
