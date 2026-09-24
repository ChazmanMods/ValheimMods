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
                return Fail(global::Runic.Localization.RunicText.Get("text_09e471e119ac"), out failure);
            if (station.gameObject.GetComponentsInChildren<CraftingStation>(true).Length != 1 ||
                HasPeerAdapter(station.gameObject))
                return Fail(global::Runic.Localization.RunicText.Get("text_62e3686ff197"), out failure);
            view = station.gameObject.GetComponent<ZNetView>();
            wear = station.gameObject.GetComponent<WearNTear>();
            if (view == null || !view.IsValid() || view.GetZDO() == null || wear == null ||
                !ReferenceEquals(wear.gameObject, station.gameObject) ||
                station.gameObject.GetComponentsInChildren<ZNetView>(true).Length != 1 ||
                station.gameObject.GetComponentsInChildren<WearNTear>(true).Length != 1)
                return Fail(global::Runic.Localization.RunicText.Get("text_258feb51da83"), out failure);
            if (string.IsNullOrWhiteSpace(station.m_name) || station.m_name.Length > 128)
                return Fail(global::Runic.Localization.RunicText.Get("text_49fc143576d8"), out failure);
            if (station.m_craftRequireRoof && station.m_roofCheckPoint == null)
                return Fail(global::Runic.Localization.RunicText.Get("text_6f1af50a138b"), out failure);
            if (ZNetScene.instance == null)
                return Fail(global::Runic.Localization.RunicText.Get("text_0edd723f7d57"), out failure);
            GameObject registered = ZNetScene.instance.GetPrefab(view.GetZDO().GetPrefab());
            CraftingStation registeredStation = registered?.GetComponent<CraftingStation>();
            if (registered == null || registeredStation == null ||
                registered.GetComponentsInChildren<CraftingStation>(true).Length != 1 ||
                registered.GetComponentsInChildren<ZNetView>(true).Length != 1 ||
                registered.GetComponent<ZNetView>() == null ||
                registered.GetComponentsInChildren<WearNTear>(true).Length != 1 ||
                registered.GetComponent<WearNTear>() == null ||
                HasPeerAdapter(registered))
                return Fail(global::Runic.Localization.RunicText.Get("text_d28c121d4d80"), out failure);
            prefabId = ValheimAccess.PrefabName(registered);
            if (!StockDomainValidation.IsExactPrefabId(prefabId))
                return Fail(global::Runic.Localization.RunicText.Get("text_21b232680e8d"), out failure);
            if (!string.Equals(registeredStation.m_name, station.m_name, StringComparison.Ordinal) ||
                registeredStation.m_craftRequireRoof != station.m_craftRequireRoof ||
                registeredStation.m_craftRequireFire != station.m_craftRequireFire)
                return Fail(global::Runic.Localization.RunicText.Get("text_495364041f52"), out failure);
            if (requireAllowed && (policy == null || !policy.Allows(prefabId)))
                return Fail(global::Runic.Localization.RunicText.Get("text_d4b04100966b"), out failure);
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
                return Fail(global::Runic.Localization.RunicText.Get("text_8e3239cfd883"), out failure);
            if (station.GetLevel() < requiredLevel)
                return Fail(global::Runic.Localization.RunicText.Format("text_b5c55bd8a0c5", requiredLevel), out failure);
            if (station.m_craftRequireRoof)
            {
                if (station.m_roofCheckPoint == null)
                    return Fail(global::Runic.Localization.RunicText.Get("text_91b5b519f43a"), out failure);
                Cover.GetCoverForPoint(
                    station.m_roofCheckPoint.position,
                    out float coverPercentage,
                    out bool underRoof);
                if (!underRoof)
                    return Fail(global::Runic.Localization.RunicText.Get("text_b5bbaa74adb7"), out failure);
                if (coverPercentage < 0.7f)
                    return Fail(global::Runic.Localization.RunicText.Get("text_85d1432fdc4e"), out failure);
            }
            if (station.m_craftRequireFire &&
                !EffectArea.IsPointPlus025InsideBurningArea(station.transform.position))
                return Fail(global::Runic.Localization.RunicText.Get("text_f6a4c8c3a91f"), out failure);
            return true;
        }

        private static bool Fail(string detail, out string failure)
        {
            failure = detail;
            return false;
        }
    }
}
