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
        private static readonly PreviewSnapshotCache<ZDOID, PreviewMaterialCounts> PreviewCache =
            new PreviewSnapshotCache<ZDOID, PreviewMaterialCounts>();

        internal static void MaintainPreviewCacheContext() =>
            PreviewCache.SetContext(ZNet.instance, Player.m_localPlayer, ObjectDB.instance);

        internal static void ClearPreviewCache() => PreviewCache.Clear();

        private static readonly FieldInfo ContainerViewField = AccessTools.Field(typeof(Container), "m_nview");
        private static readonly FieldInfo StationViewField = AccessTools.Field(typeof(CraftingStation), "m_nview");
        private static readonly FieldInfo WardAreasField = AccessTools.Field(typeof(PrivateArea), "m_allAreas");
        private static readonly MethodInfo InventoryChangedMethod = AccessTools.Method(
            typeof(Inventory),
            "Changed",
            new[] { typeof(bool), typeof(bool) });
        private static readonly MethodInfo ContainerCheckAccessMethod = AccessTools.Method(typeof(Container), "CheckAccess");
        private static readonly MethodInfo ContainerLoadMethod = AccessTools.Method(typeof(Container), "Load");
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
            InventoryChangedMethod?.Invoke(inventory, new object[] { false, false });

        internal static string Fingerprint(Inventory inventory)
        {
            var text = new System.Text.StringBuilder(SaveInventory(inventory).GetBase64());
            foreach (ItemDrop.ItemData item in inventory.GetAllItems()) text.Append('|').Append(item.m_stack);
            return text.ToString();
        }
        internal static ZPackage SaveInventory(Inventory inventory)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            var package = new ZPackage();
            inventory.Save(package);
            return package;
        }

        internal static void RestoreInventory(Inventory inventory, CraftingInventorySnapshot snapshot)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            Action changed = inventory.m_onChanged;
            inventory.m_onChanged = null;
            try { snapshot.Restore(inventory); }
            finally { inventory.m_onChanged = changed; }
            NotifyInventoryChanged(inventory);
            snapshot.Verify(inventory);
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

        internal static void ObservePreviewWards()
        {
            var areas = WardAreasField?.GetValue(null) as List<PrivateArea>;
            if (areas == null) return;
            foreach (PrivateArea area in areas)
                if (area != null)
                    UiPreviewCache.Watch(area.GetComponent<ZNetView>()?.GetZDO());
        }

        internal static bool TryReadContainerInventory(Container container, out PreviewMaterialCounts snapshot)
        {
            snapshot = null;
            MaintainPreviewCacheContext();
            if (Runic.Compatibility.ModdedContainerCompatibility.IsDrawer(container))
            {
                if (!Runic.Compatibility.ModdedContainerCompatibility.TryPreview(container, out var preview)) return false;
                snapshot = new PreviewMaterialCounts(preview, Game.m_worldLevel);
                return true;
            }
            ZNetView view = GetView(container);
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            Inventory live = container != null ? container.GetInventory() : null;
            if (zdo == null || !zdo.IsValid() || live == null) return false;
            try
            {
                byte[] persisted = zdo.GetByteArray(ZDOVars.s_items);
                int width = live.GetWidth(), height = live.GetHeight(), worldLevel = Game.m_worldLevel;
                string name = live.GetName();
                bool empty = persisted == null || persisted.Length == 0;
                if (empty && live.GetAllItems().Count != 0) return false;
                if (persisted != null && persisted.Length > PreviewSnapshotCache<ZDOID, PreviewMaterialCounts>.MaximumPayloadBytes)
                    return false;
                if (PreviewCache.TryGet(zdo.m_uid, zdo, persisted, width, height, name, worldLevel, out snapshot))
                {
                    CachePerformance.PayloadHits++;
                    return true;
                }

                CachePerformance.PayloadLoads++;
                byte[] before = empty ? Array.Empty<byte>() : (byte[])persisted.Clone();
                var preview = new Inventory(name, null, width, height);
                if (!empty)
                {
                    Inventory previousPreview = PreviewRefreshRuntime.LoadingPreview;
                    PreviewRefreshRuntime.LoadingPreview = preview;
                    try { preview.Load(new ZPackage(before)); }
                    finally { PreviewRefreshRuntime.LoadingPreview = previousPreview; }
                    if (!CraftingInventoryPayloadComparison.MatchesLoaded(before, SaveInventory(preview).GetArray()))
                        return false;
                }
                byte[] after = zdo.GetByteArray(ZDOVars.s_items);
                if (!view.IsValid() || !ReferenceEquals(view.GetZDO(), zdo) || !zdo.IsValid() ||
                    !PreviewSnapshotCache<ZDOID, PreviewMaterialCounts>.SamePayload(before, after)) return false;
                snapshot = new PreviewMaterialCounts(preview, worldLevel);
                PreviewCache.Store(zdo.m_uid, zdo, before, width, height, name, worldLevel, snapshot);
                return true;
            }
            catch { return false; }
        }

        internal static bool RefreshOwnedContainer(Container container, ZDO expectedZdo)
        {
            if (container == null || expectedZdo == null || ContainerLoadMethod == null || RunicAutomation.ContainerAuthority.Blocked(container)) return false;
            ZNetView view = GetView(container);
            if (view == null || !view.IsValid() || !view.IsOwner() ||
                !ReferenceEquals(view.GetZDO(), expectedZdo) ||
                expectedZdo.GetOwner() != ZNet.GetUID()) return false;
            try
            {
                if (Runic.Compatibility.ModdedContainerCompatibility.IsDrawer(container))
                    return Runic.Compatibility.ModdedContainerCompatibility.TryRefresh(container, out _);
                ContainerLoadMethod.Invoke(container, Array.Empty<object>());
                Inventory inventory = container.GetInventory();
                if (inventory == null) return false;
                byte[] persisted = expectedZdo.GetByteArray(ZDOVars.s_items);
                string persistedBase64 = persisted == null || persisted.Length == 0
                    ? string.Empty
                    : Convert.ToBase64String(persisted);
                return persistedBase64.Length == 0
                    ? inventory.GetAllItems().Count == 0
                    : CraftingInventoryPayloadComparison.MatchesLoaded(
                        persisted, SaveInventory(inventory).GetArray());
            }
            catch { return false; }
        }

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
