using System;
using RunicProduction.Core;
using UnityEngine;

namespace RunicProduction.Integration
{
    internal static class RecipeStationCompatibility
    {
        internal static bool TryValidate(
            CraftingStation station,
            StockStationPrefabPolicy policy,
            bool requireAllowed,
            out string prefabId,
            out ZNetView view,
            out WearNTear wear,
            out string failure)
        {
            prefabId = string.Empty;
            view = null;
            wear = null;
            failure = string.Empty;
            if (station == null || !ReferenceEquals(
                    station.gameObject.GetComponent<CraftingStation>(), station))
                return Fail("The CraftingStation is not the unique root component.", out failure);
            if (station.gameObject.GetComponentsInChildren<CraftingStation>(true).Length != 1 ||
                HasPeerAdapter(station.gameObject))
                return Fail("Hybrid or duplicate station components are not supported.", out failure);
            view = station.gameObject.GetComponent<ZNetView>();
            wear = station.gameObject.GetComponent<WearNTear>();
            if (view == null || !view.IsValid() || view.GetZDO() == null || wear == null ||
                !ReferenceEquals(wear.gameObject, station.gameObject) ||
                station.gameObject.GetComponentsInChildren<ZNetView>(true).Length != 1 ||
                station.gameObject.GetComponentsInChildren<WearNTear>(true).Length != 1)
                return Fail("The station requires one valid root ZNetView and WearNTear.", out failure);
            if (string.IsNullOrWhiteSpace(station.m_name) || station.m_name.Length > 128)
                return Fail("The station name is missing or unbounded.", out failure);
            if (station.m_craftRequireRoof && station.m_roofCheckPoint == null)
                return Fail("The station requires a missing roof-check point.", out failure);
            if (ZNetScene.instance == null)
                return Fail("The registered prefab scene is unavailable.", out failure);
            GameObject registered = ZNetScene.instance.GetPrefab(view.GetZDO().GetPrefab());
            CraftingStation registeredStation = registered?.GetComponent<CraftingStation>();
            if (registered == null || registeredStation == null ||
                registered.GetComponentsInChildren<CraftingStation>(true).Length != 1 ||
                registered.GetComponentsInChildren<ZNetView>(true).Length != 1 ||
                registered.GetComponent<ZNetView>() == null ||
                registered.GetComponentsInChildren<WearNTear>(true).Length != 1 ||
                registered.GetComponent<WearNTear>() == null ||
                HasPeerAdapter(registered))
                return Fail("The registered prefab is not a strict recipe-station adapter.", out failure);
            prefabId = ValheimAccess.PrefabName(registered);
            if (!StockDomainValidation.IsExactPrefabId(prefabId))
                return Fail("The registered station prefab ID is invalid.", out failure);
            if (!string.Equals(registeredStation.m_name, station.m_name, StringComparison.Ordinal) ||
                registeredStation.m_craftRequireRoof != station.m_craftRequireRoof ||
                registeredStation.m_craftRequireFire != station.m_craftRequireFire)
                return Fail("The live station policy differs from its registered prefab.", out failure);
            if (requireAllowed && (policy == null || !policy.Allows(prefabId)))
                return Fail("The exact recipe-station prefab is not locally allowed.", out failure);
            return true;
        }

        internal static bool HasPeerAdapter(GameObject root) =>
            root == null ||
            root.GetComponentsInChildren<Smelter>(true).Length != 0 ||
            root.GetComponentsInChildren<CookingStation>(true).Length != 0 ||
            root.GetComponentsInChildren<Fermenter>(true).Length != 0 ||
            root.GetComponentsInChildren<Container>(true).Length != 0;

        internal static bool IsPhysicallyUsable(
            CraftingStation station,
            int requiredLevel,
            out string failure)
        {
            failure = string.Empty;
            if (station == null || !station.isActiveAndEnabled || requiredLevel < 1)
                return Fail("The recipe station or required level is invalid.", out failure);
            if (station.GetLevel() < requiredLevel)
                return Fail($"The recipe requires station level {requiredLevel}.", out failure);
            if (station.m_craftRequireRoof)
            {
                if (station.m_roofCheckPoint == null)
                    return Fail("The station roof-check point is unavailable.", out failure);
                Cover.GetCoverForPoint(
                    station.m_roofCheckPoint.position,
                    out float coverPercentage,
                    out bool underRoof);
                if (!underRoof)
                    return Fail("The recipe station needs a roof.", out failure);
                if (coverPercentage < 0.7f)
                    return Fail("The recipe station is too exposed.", out failure);
            }
            if (station.m_craftRequireFire &&
                !EffectArea.IsPointPlus025InsideBurningArea(station.transform.position))
                return Fail("The recipe station needs a lit fire.", out failure);
            return true;
        }

        private static bool Fail(string detail, out string failure)
        {
            failure = detail;
            return false;
        }
    }
}
