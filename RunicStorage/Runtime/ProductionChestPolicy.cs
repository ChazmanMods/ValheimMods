using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RunicAutomation.Operations;
using UnityEngine;

namespace RunicStorage.Runtime;

// Optional integration: use Production's existing link reader when it is installed.
// Storage remains standalone; no companion DLL is referenced or required.
internal sealed class ProductionChestPolicy
{
    internal bool Excluded;
    internal readonly HashSet<string> Inputs = new(StringComparer.Ordinal);
    internal string Signature => (Excluded ? "excluded:" : "input:") + string.Join(",", Inputs.OrderBy(x=>x,StringComparer.Ordinal));
    internal int Rank(ItemDrop.ItemData item) => Inputs.Contains(ValheimContainerIdentity.ResourceId(item)) ? 0 : int.MaxValue;

    internal static Dictionary<ZDOID, ProductionChestPolicy> Read(IReadOnlyList<ZDO> chests, Vector3 center, float radius)
    {
        var result = new Dictionary<ZDOID, ProductionChestPolicy>();
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name=="RunicProduction");
        if (assembly == null || chests.Count == 0) return result;
        try
        {
            var store = assembly.GetType("RunicProduction.Integration.ProductionRoleLinkCatalogStore", true);
            var roleType = assembly.GetType("RunicProduction.Contracts.ProductionLinkRole", true);
            var read = AccessTools.Method(store, "ReadWithLegacy");
            // Production's supported link range is bounded at 30 m. Include stations
            // outside the sort radius whose linked chest is inside it, including unloaded ZDOs.
            foreach (var station in NetworkInventoryAccess.Nearby(center, radius + 30f))
            {
                var prefab = ZNetScene.instance.GetPrefab(station.GetPrefab());
                if (prefab == null || !(prefab.GetComponent<Fireplace>() || prefab.GetComponent<Smelter>() ||
                    prefab.GetComponent<CookingStation>() || prefab.GetComponent<Fermenter>() ||
                    prefab.GetComponent<CraftingStation>() || prefab.GetComponent<Beehive>())) continue;
                for (int role = 1; role <= 4; role++)
                {
                    object[] args = { station, Enum.ToObject(roleType, role), null };
                    if (read.Invoke(null, args).ToString() != "Valid")
                        throw new InvalidOperationException("Production links could not be read for SmartSort.");
                    var links = (IEnumerable)AccessTools.Property(args[2].GetType(), "Links").GetValue(args[2]);
                    foreach (object link in links)
                    {
                        var type = link.GetType();
                        string token = (string)AccessTools.Field(type, "TargetToken").GetValue(link);
                        int prefabHash = (int)AccessTools.Field(type, "TargetPrefabHash").GetValue(link);
                        var id = (ZDOID)AccessTools.Field(type, "Target").GetValue(link);
                        var position = (Vector3)AccessTools.Field(type, "ExpectedPosition").GetValue(link);
                        foreach (var chest in chests.Where(c => c.GetPrefab() == prefabHash &&
                            (!string.IsNullOrEmpty(token) ? c.GetString("runic.transactions.world-object.token", "") == token :
                            c.m_uid == id && (c.GetPosition() - position).sqrMagnitude < .01f)))
                        {
                            if (!result.TryGetValue(chest.m_uid, out var policy)) result[chest.m_uid] = policy = new ProductionChestPolicy();
                            if (role >= 3) policy.Excluded = true;
                            else policy.Inputs.UnionWith(Accepted(prefab, role == 2));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        { throw new InvalidOperationException("SmartSort could not read Production chest roles: " + ex.GetBaseException().Message, ex); }
        return result;
    }

    private static IEnumerable<string> Accepted(GameObject prefab, bool fuelOnly)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Add(ItemDrop item) { if (item != null) ids.Add(Utils.GetPrefabName(item.gameObject)); }
        var fire = prefab.GetComponent<Fireplace>();
        var smelter = prefab.GetComponent<Smelter>();
        var cooking = prefab.GetComponent<CookingStation>();
        var fermenter = prefab.GetComponent<Fermenter>();
        var crafting = prefab.GetComponent<CraftingStation>();
        if (fire) Add(fire.m_fuelItem);
        if (smelter) { Add(smelter.m_fuelItem); if (!fuelOnly) foreach (var conversion in smelter.m_conversion) Add(conversion.m_from); }
        if (cooking) { if (cooking.m_useFuel) Add(cooking.m_fuelItem); if (!fuelOnly) foreach (var conversion in cooking.m_conversion) Add(conversion.m_from); }
        if (fermenter && !fuelOnly) foreach (var conversion in fermenter.m_conversion) Add(conversion.m_from);
        if (crafting && !fuelOnly && ObjectDB.instance)
            foreach (var recipe in ObjectDB.instance.m_recipes.Where(r => r && r.m_enabled && r.m_craftingStation && r.m_craftingStation.m_name == crafting.m_name))
                foreach (var resource in recipe.m_resources) if (resource.m_resItem && !resource.m_upgraderResource) Add(resource.m_resItem);
        return ids;
    }
}
