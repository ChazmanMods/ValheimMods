using HarmonyLib;
using Jotunn.Managers;

namespace ArcaneDecorFlowCraft.Plumbing;

// Both size sets become discoverable from the same raw materials. Construction still
// consumes the correctly sized crafted pipe; this patch never grants free placement.
[HarmonyPatch(typeof(Player),nameof(Player.HaveRequirements),typeof(Piece),typeof(Player.RequirementMode))]
internal static class PipeDiscovery
{
    static void Postfix(Player __instance,Piece piece,Player.RequirementMode mode,ref bool __result){
        if(__result||mode!=Player.RequirementMode.IsKnown||!piece)return;
        var pipe=piece.GetComponent<PipePiece>();if(!pipe||!PipePrefabs.Powered(pipe.Kind))return;
        var copper=PrefabManager.Instance.GetPrefab("Copper")?.GetComponent<ItemDrop>();
        var core=PrefabManager.Instance.GetPrefab("SurtlingCore")?.GetComponent<ItemDrop>();
        __result=copper&&core&&__instance.IsKnownMaterial(copper.m_itemData.m_shared.m_name)&&__instance.IsKnownMaterial(core.m_itemData.m_shared.m_name);
    }
}
