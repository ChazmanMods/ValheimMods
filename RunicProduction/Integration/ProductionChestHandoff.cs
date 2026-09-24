using UnityEngine;
namespace RunicProduction.Integration
{
    // All inventory consumers use the same owner-approved receipt protocol.
    internal static class ProductionChestHandoff
    {
        private static bool _active;
        internal static void Initialize()
        {
            _active = true;
            RunicAutomation.ContainerAuthority.RegisterPolicy("production-setup",
                (chest, context, principal, sender) => _active && ProductionConfig.Enabled.Value &&
                    RunicAutomation.ContainerAuthority.PlayerAccess(chest, context, principal, sender, 10f, true),
                chest => ValheimAccess.TrySynchronizeLocallyOwnedContainer(chest, out _));
            RunicAutomation.ContainerAuthority.RegisterPolicy("production",
            (chest, context, principal, sender) =>
            {
                Component station = ProductionRuntime.FindStation(context);
                return _active && ProductionDiagnostics.RuntimeAvailable && ProductionConfig.Enabled.Value &&
                    ValheimAccess.Zdo(station)?.GetOwner() == sender &&
                    ProductionRuntime.AuthorizesChest(station, chest, principal);
            }, chest => ValheimAccess.TrySynchronizeLocallyOwnedContainer(chest, out _));
        }
        internal static void Shutdown() { _active = false; }
        internal static void Clear() { }
        internal static void Register(Container chest) => RunicAutomation.ContainerAuthority.Register(chest);
        internal static bool TryAcquire(Component station, Container chest, long principal) =>
            ValheimAccess.IsNativeOwner(station) && RunicAutomation.ContainerAuthority.TryAcquire(chest,
                "production", ValheimAccess.Zdo(station).m_uid, principal);
    }
}
