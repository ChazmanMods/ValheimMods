using System;
using System.Collections.Generic;

namespace RunicAutomation.Operations
{
    // Compose the crafting material plan into the station transaction. No character
    // round trip or native ownership handoff is needed for a nearby station input.
    internal static class NearbyInteractionMaterials
    {
        private static Dictionary<string, Delegate> Routes => SharedState.Get("operations.nearby-interaction.v2", () => new Dictionary<string, Delegate>());
        internal static void Register(Func<bool, bool> enabled,
            Func<string, ZDOID, bool, string[], Func<string, byte[]>, Tuple<Tuple<string, byte[], byte[]>[], byte[]>> plan)
        { Routes["enabled"] = enabled; Routes["plan"] = plan; }
        internal static bool Enabled(bool fuel) => Routes.TryGetValue("enabled", out var callback) && ((Func<bool, bool>)callback)(fuel);
        internal static Tuple<Tuple<string, byte[], byte[]>[], byte[]> Plan(string actor, ZDOID station, bool fuel, string[] items, Func<string, byte[]> read)
        {
            if (!Enabled(fuel) || !Routes.TryGetValue("plan", out var callback)) throw new OperationRejectedException("nearby-interaction-disabled");
            return ((Func<string, ZDOID, bool, string[], Func<string, byte[]>, Tuple<Tuple<string, byte[], byte[]>[], byte[]>>)callback)(actor, station, fuel, items, read);
        }
    }
}
