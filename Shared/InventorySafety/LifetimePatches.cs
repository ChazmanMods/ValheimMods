using HarmonyLib;
namespace RunicAutomation
{
    [HarmonyPatch(typeof(ZNet), "Start")]
    internal static class RegisterSnapshotProtocol
    {
        private static void Postfix() { Operations.ManagedDrawerInteraction.Register(); }
    }
    [HarmonyPatch(typeof(ZNet), "OnDestroy")]
    internal static class EndWorld
    {
        private static void Postfix()
        {
            Operations.InventoryOperationNetwork.Clear(); Operations.ManagedChestLedger.Clear(); Operations.CharacterOperationEscrow.Clear();
            Operations.NativeChestInterop.Clear();
            Operations.InventoryWorldLifecycle.Clear(); Operations.WorldInventoryOrigin.Clear();
            Operations.ManagedWorldDrops.Clear();
            Operations.ManagedDrawerRetrieval.Clear(); Operations.ManagedAnimalConsumption.Clear();
            ContainerAuthority.Clear(); MutationGate.EndSession();
        }
    }
    [HarmonyPatch(typeof(ZNet), "Update")]
    internal static class InventoryOperationLoop
    {
        private static string lastFailure;
        private static void Postfix()
        {
            try
            {
                Operations.InventoryOperationNetwork.Tick();
                if (!Operations.InventoryOperationNetwork.Ready) return;
                Operations.ManagedChestLedger.Tick(); Operations.NativeChestInterop.Tick(); Operations.CharacterOperationEscrow.Tick();
                Operations.ManagedWorldDrops.Tick();
                Operations.WorldInventoryOrigin.Tick(); Operations.InventoryWorldLifecycle.Tick();
            }
            catch (System.Exception error)
            {
                string failure = error.ToString();
                if (lastFailure != failure) { lastFailure = failure; UnityEngine.Debug.LogError("Runic inventory operation update failed: " + failure); }
                MutationGate.Report("inventory operation update: " + error);
            }
        }
    }
    [HarmonyPatch(typeof(PlayerProfile), "LoadPlayerData")]
    internal static class ReloadCharacterOperation
    { private static void Postfix() => Operations.CharacterOperationEscrow.Clear(); }
    [HarmonyPatch(typeof(ZDO), "Deserialize")]
    internal static class PreserveManagedChestProjection
    {
        private static void Postfix(ZDO __instance)
        {
            if (!Operations.InventoryOperationNetwork.Ready) return;
            try { Operations.ManagedChestLedger.Project(__instance); Operations.ManagedWorldDrops.Project(__instance); }
            catch (System.Exception error) { MutationGate.Report("inventory projection reconciliation: " + error); }
        }
    }
}
