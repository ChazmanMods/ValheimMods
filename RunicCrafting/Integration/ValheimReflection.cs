using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RunicCrafting.Domain;
using UnityEngine;

namespace RunicCrafting.Integration
{
    internal static class ValheimReflection
    {
        private static readonly FieldInfo ContainerViewField = AccessTools.Field(typeof(Container), "m_nview");
        private static readonly FieldInfo StationViewField = AccessTools.Field(typeof(CraftingStation), "m_nview");
        private static readonly FieldInfo WardAreasField = AccessTools.Field(typeof(PrivateArea), "m_allAreas");
        private static readonly MethodInfo InventoryChangedMethod = AccessTools.Method(typeof(Inventory), "Changed");
        private static readonly MethodInfo ContainerCheckAccessMethod = AccessTools.Method(typeof(Container), "CheckAccess");
        private static readonly MethodInfo WardIsEnabledMethod = AccessTools.Method(typeof(PrivateArea), "IsEnabled");
        private static readonly MethodInfo WardIsInsideMethod = AccessTools.Method(typeof(PrivateArea), "IsInside");
        private static readonly FieldInfo SelectedRecipePairField =
            AccessTools.Field(typeof(InventoryGui), "m_selectedRecipe");
        private static readonly FieldInfo SelectedRecipeValueField =
            SelectedRecipePairField?.FieldType.GetField(
                "<Recipe>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);

        internal static bool CanMutateLocalPlayer(Player player) =>
            player != null && ReferenceEquals(player, Player.m_localPlayer) && player.IsOwner();

        internal static Recipe SelectedRecipe(InventoryGui inventoryGui)
        {
            if (inventoryGui == null || SelectedRecipePairField == null ||
                SelectedRecipeValueField == null) return null;
            try
            {
                object pair = SelectedRecipePairField.GetValue(inventoryGui);
                return pair == null ? null : SelectedRecipeValueField.GetValue(pair) as Recipe;
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogWarning(
                    "Could not resolve Valheim's selected crafting recipe: " +
                    exception.GetType().Name + ".");
                return null;
            }
        }

        internal static ZNetView GetView(Container container) =>
            container == null ? null : ContainerViewField?.GetValue(container) as ZNetView;

        internal static ZNetView GetView(CraftingStation station) =>
            station == null ? null : StationViewField?.GetValue(station) as ZNetView;

        internal static string ContainerEndpointId(Container container)
        {
            ZNetView view = GetView(container);
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            return zdo != null && zdo.IsValid()
                ? "valheim.zdo:" + zdo.m_uid
                : "valheim.instance:" + container.GetInstanceID();
        }

        internal static string StationEndpointId(CraftingStation station)
        {
            ZNetView view = GetView(station);
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            return zdo != null && zdo.IsValid()
                ? "valheim.zdo:" + zdo.m_uid
                : "valheim.station:" + station.GetInstanceID();
        }

        internal static long StationRevision(CraftingStation station)
        {
            ZNetView view = GetView(station);
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            return zdo == null ? 0L : zdo.DataRevision;
        }

        internal static ZDO StationZdo(CraftingStation station)
        {
            ZNetView view = GetView(station);
            return view != null && view.IsValid() ? view.GetZDO() : null;
        }

        internal static void NotifyInventoryChanged(Inventory inventory) =>
            InventoryChangedMethod?.Invoke(inventory, Array.Empty<object>());

        internal static ZPackage SaveInventory(Inventory inventory)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            var package = new ZPackage();
            inventory.Save(package);
            return package;
        }

        internal static void RestoreInventory(Inventory inventory, ZPackage package)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            if (package == null) throw new ArgumentNullException(nameof(package));
            byte[] targetPayload = package.GetArray();
            string target = Convert.ToBase64String(targetPayload);
            LoadExactInventoryShadow(inventory, targetPayload, target);

            ZPackage rollback = SaveInventory(inventory);
            byte[] rollbackPayload = rollback.GetArray();
            string rollbackFingerprint = Convert.ToBase64String(rollbackPayload);
            LoadExactInventoryShadow(inventory, rollbackPayload, rollbackFingerprint);

            Action changed = inventory.m_onChanged;
            inventory.m_onChanged = null;
            try
            {
                try
                {
                    inventory.Load(new ZPackage(targetPayload));
                    if (!string.Equals(
                            SaveInventory(inventory).GetBase64(), target,
                            StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            "The exact crafting inventory snapshot did not round-trip.");
                }
                catch (Exception applyFailure)
                {
                    try
                    {
                        inventory.Load(new ZPackage(rollbackPayload));
                        if (!string.Equals(
                                SaveInventory(inventory).GetBase64(), rollbackFingerprint,
                                StringComparison.Ordinal))
                            throw new InvalidOperationException(
                                "The original crafting inventory did not restore.");
                    }
                    catch (Exception rollbackFailure)
                    {
                        throw new InvalidOperationException(
                            "Crafting snapshot restoration failed and its original inventory could not be recovered.",
                            new AggregateException(applyFailure, rollbackFailure));
                    }
                    throw new InvalidOperationException(
                        "Crafting snapshot restoration was rejected; its original inventory was restored.",
                        applyFailure);
                }
            }
            finally
            {
                inventory.m_onChanged = changed;
            }
            try { NotifyInventoryChanged(inventory); }
            catch (Exception exception)
            {
                Plugin.Log?.LogWarning(
                    "Inventory state was restored exactly, but a Changed subscriber threw: " +
                    exception.GetType().Name + ".");
                throw;
            }
            if (!string.Equals(
                    SaveInventory(inventory).GetBase64(), target,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "The restored crafting inventory changed during publication.");
        }

        internal static bool CanRoundTripInventory(Inventory inventory)
        {
            if (inventory == null) return false;
            try
            {
                ZPackage package = SaveInventory(inventory);
                byte[] payload = package.GetArray();
                LoadExactInventoryShadow(
                    inventory, payload, Convert.ToBase64String(payload));
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static Inventory LoadExactInventoryShadow(
            Inventory shape,
            byte[] payload,
            string expected)
        {
            if (shape == null || payload == null || payload.Length == 0 ||
                string.IsNullOrEmpty(expected))
                throw new InvalidOperationException("An exact crafting inventory snapshot is unavailable.");
            var shadow = new Inventory(
                shape.GetName(), null, shape.GetWidth(), shape.GetHeight());
            shadow.Load(new ZPackage(payload));
            if (!string.Equals(
                    SaveInventory(shadow).GetBase64(), expected,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "A crafting inventory snapshot is not an exact round trip under current item definitions.");
            return shadow;
        }

        internal static bool DlcAllows(string dlcId)
        {
            if (string.IsNullOrEmpty(dlcId)) return true;
            DLCMan manager = DLCMan.instance;
            return DlcEligibility.IsAllowed(
                dlcId,
                manager != null,
                manager != null && manager.IsDLCInstalled(dlcId));
        }

        internal static bool ContainerAllows(Container container, long playerId) =>
            container != null && ContainerCheckAccessMethod != null &&
            (bool)ContainerCheckAccessMethod.Invoke(container, new object[] { playerId });

        internal static string ResourceId(ItemDrop drop)
        {
            if (drop == null) return string.Empty;
            string prefab = drop.m_itemData?.m_dropPrefab != null
                ? drop.m_itemData.m_dropPrefab.name
                : drop.gameObject != null ? drop.gameObject.name : string.Empty;
            return NormalizePrefabId(prefab, drop.m_itemData?.m_shared?.m_name);
        }

        internal static string ResourceId(ItemDrop.ItemData item)
        {
            if (item == null) return string.Empty;
            string prefab = item.m_dropPrefab != null ? item.m_dropPrefab.name : string.Empty;
            return NormalizePrefabId(prefab, item.m_shared?.m_name);
        }

        internal static string PiecePrefabId(Piece piece) =>
            piece == null
                ? string.Empty
                : NormalizePrefabId(
                    piece.gameObject != null ? piece.gameObject.name : string.Empty,
                    piece.m_name);

        internal static bool IsUsableRequirementItem(ItemDrop.ItemData item, string resourceId) =>
            item != null && item.m_worldLevel >= Game.m_worldLevel &&
            string.Equals(ResourceId(item), resourceId, StringComparison.Ordinal);

        internal static int CountRequirementItems(Inventory inventory, string resourceId)
        {
            if (inventory == null || string.IsNullOrEmpty(resourceId)) return 0;
            int count = 0;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (!IsUsableRequirementItem(item, resourceId)) continue;
                count = count > int.MaxValue - Math.Max(0, item.m_stack)
                    ? int.MaxValue
                    : count + Math.Max(0, item.m_stack);
            }
            return count;
        }

        internal static bool WardIsEnabled(PrivateArea area) =>
            area != null && WardIsEnabledMethod != null &&
            (bool)WardIsEnabledMethod.Invoke(area, Array.Empty<object>());

        internal static bool WardContains(PrivateArea area, Vector3 position) =>
            area != null && WardIsInsideMethod != null &&
            (bool)WardIsInsideMethod.Invoke(area, new object[] { position, 0f });

        internal static WardResolution ResolveWard(Vector3 position)
        {
            var areas = WardAreasField?.GetValue(null) as List<PrivateArea>;
            if (areas == null) return new WardResolution(false, true, false);

            int containing = 0;
            for (int index = 0; index < areas.Count; index++)
            {
                PrivateArea area = areas[index];
                if (area != null && WardIsEnabled(area) && WardContains(area, position)) containing++;
            }
            if (containing == 0) return new WardResolution(false, true, false);

            bool allowed = PrivateArea.CheckAccess(position, 0f, flash: false, wardCheck: false);
            return new WardResolution(true, allowed, containing > 1);
        }

        private static string NormalizePrefabId(string prefab, string fallback)
        {
            string value = string.IsNullOrWhiteSpace(prefab) ? fallback : prefab;
            value = (value ?? string.Empty).Trim();
            const string cloneSuffix = "(Clone)";
            if (value.EndsWith(cloneSuffix, StringComparison.Ordinal))
                value = value.Substring(0, value.Length - cloneSuffix.Length).TrimEnd();
            return value;
        }
    }

    internal readonly struct WardResolution
    {
        internal WardResolution(bool hasWard, bool allowed, bool ambiguous)
        {
            HasWard = hasWard;
            Allowed = allowed;
            Ambiguous = ambiguous;
        }

        internal bool HasWard { get; }
        internal bool Allowed { get; }
        internal bool Ambiguous { get; }
    }
}
