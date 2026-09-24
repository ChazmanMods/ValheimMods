using RunicProduction.Contracts;
using RunicProduction.Core;

namespace RunicProduction.Integration
{
    internal static partial class ProductionRuntime
    {
        private static bool RunBeehive(Beehive station)
        {
            ZDO zdo = ValheimAccess.Zdo(station);
            if (zdo == null || station.m_honeyItem == null || Game.instance == null) return false;
            int level = zdo.GetInt(ZDOVars.s_level);
            if (level <= 0 || level > station.m_maxHoney) return false;
            string prefab = ValheimAccess.PrefabName(station.m_honeyItem.gameObject);
            // Vanilla scales each individual honey drop, rather than the total batch.
            int amount = checked(level * Game.instance.ScaleDrops(station.m_honeyItem.m_itemData, 1));
            if (amount <= 0) return false;
            var output = new StockOutputDefinition(prefab, amount, 1, 0, 0L, string.Empty);
            foreach (ProductionEndpoint destination in ResolveRoleEndpoints(station, ProductionLinkRole.Output))
            {
                if (!ExactStockInventoryMutation.TryPrepareDestination(
                        destination.Inventory, output, out StockInventoryTransition transition, out _)) continue;
                if (!ApplyDestinationFromStation(
                        station, destination, transition,
                        () => zdo.Set(ZDOVars.s_level, 0),
                        () => zdo.Set(ZDOVars.s_level, level),
                        () => zdo.GetInt(ZDOVars.s_level) == level,
                        () => zdo.GetInt(ZDOVars.s_level) == 0)) continue;
                Stop(station, ProductionStopCode.Ready, "stored " + prefab);
                return true;
            }
            return false;
        }

    }
}
