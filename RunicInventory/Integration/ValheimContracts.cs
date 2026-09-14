using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using RunicInventory.Core;
using UnityEngine;

namespace RunicInventory.Integration
{
    internal static class ValheimContracts
    {
        internal const string AuditedGameVersion = "1.0.12";
        internal static bool IsSupportedVersion(string version) =>
            string.Equals(version, AuditedGameVersion, StringComparison.Ordinal) ||
            string.Equals(version, "1.0.7", StringComparison.Ordinal);
        private const BindingFlags InstanceAll = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal delegate void InventoryChangedDelegate(
            Inventory instance,
            bool success,
            bool cheatedStateChanged);
        internal delegate Vector2i GridButtonPositionDelegate(InventoryGrid instance, GameObject button);
        internal delegate bool PlayerTakeInputDelegate(Player instance);

        private static InventoryChangedDelegate _changed;
        private static GridButtonPositionDelegate _buttonPosition;
        private static PlayerTakeInputDelegate _takeInput;
        private static FieldInfo _gridSelected;
        private static MethodInfo _hoveredElement;
        private static MethodInfo _getElement;
        private static FieldInfo _craftUpgradeItem;
        private static bool _ready;

        internal static void ValidateGameVersion(Type versionType)
        {
            // GetVersionString is a display label and can include an OS prefix (e.g. l-).
            // CurrentVersion identifies the game build without weakening the audited-version gate.
            PropertyInfo property = versionType?.GetProperty(
                "CurrentVersion", BindingFlags.Public | BindingFlags.Static);
            if (property == null || property.GetGetMethod() == null ||
                property.GetIndexParameters().Length != 0)
                throw new MissingMemberException("Version.CurrentVersion");
            string installed = property.GetValue(null, null)?.ToString() ?? string.Empty;
            if (!IsSupportedVersion(installed))
                throw new MissingMethodException(
                    "Runic Inventory " + Plugin.Version + " is audited for Valheim " +
                    "1.0.7 or " + AuditedGameVersion + "; installed " + (installed.Length == 0 ? "unknown" : installed) + ".");
        }

        internal static bool Initialize(out string problem)
        {
            try
            {
                ValidateGameVersion(typeof(Player).Assembly.GetType("Version", true));

                _changed = AccessTools.MethodDelegate<InventoryChangedDelegate>(Exact(
                    typeof(Inventory), "Changed", InstanceAll,
                    typeof(bool), typeof(bool)));
                _buttonPosition = AccessTools.MethodDelegate<GridButtonPositionDelegate>(
                    Exact(typeof(InventoryGrid), "GetButtonPos", InstanceAll, typeof(GameObject)));
                _takeInput = AccessTools.MethodDelegate<PlayerTakeInputDelegate>(Exact(typeof(Player), "TakeInput", InstanceAll));
                _gridSelected = ExactField(typeof(InventoryGrid), "m_selected", typeof(Vector2i));
                _hoveredElement = Exact(typeof(InventoryGrid), "GetHoveredElement", InstanceAll);
                _getElement = Exact(
                    typeof(InventoryGrid), "GetElement", InstanceAll,
                    typeof(int), typeof(int), typeof(int));
                Type elementType = typeof(InventoryElement);
                if (_hoveredElement.ReturnType != elementType ||
                    _getElement.ReturnType != elementType)
                    throw new MissingMemberException("InventoryElement/GetHoveredElement");
                _craftUpgradeItem = ExactField(typeof(InventoryGui), "m_craftUpgradeItem", typeof(ItemDrop.ItemData));

                Exact(typeof(Inventory), nameof(Inventory.GetWidth), InstanceAll);
                Exact(typeof(Inventory), nameof(Inventory.GetHeight), InstanceAll);
                Exact(typeof(Inventory), nameof(Inventory.GetAllItems), InstanceAll);
                Exact(typeof(Inventory), nameof(Inventory.GetItemAt), InstanceAll, typeof(int), typeof(int));
                Exact(typeof(Inventory), nameof(Inventory.Save), InstanceAll, typeof(ZPackage));
                Exact(typeof(Inventory), "FindEmptySlot", InstanceAll, typeof(bool));
                Exact(typeof(Inventory), "FindFreeStackItem", InstanceAll,
                    typeof(string), typeof(int), typeof(float));
                Exact(typeof(Inventory), nameof(Inventory.CanAddItem), InstanceAll,
                    typeof(ItemDrop.ItemData), typeof(int));
                Exact(typeof(Inventory), nameof(Inventory.SetHeight), InstanceAll, typeof(int));
                Exact(typeof(Player), nameof(Player.SetInventorySize), InstanceAll, typeof(int));
                Exact(typeof(InventoryGui), nameof(InventoryGui.SetInventorySize), InstanceAll, typeof(int));
                Exact(typeof(Inventory), "AddItem", InstanceAll,
                    typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool));
                Exact(typeof(Inventory), nameof(Inventory.AddItem), InstanceAll,
                    typeof(ItemDrop.ItemData), typeof(Vector2i));
                Exact(typeof(InventoryGrid), nameof(InventoryGrid.DropItem), InstanceAll,
                    typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int), typeof(Vector2i));
                Exact(typeof(InventoryGrid), "OnLeftClick", InstanceAll, typeof(UIInputHandler));
                Exact(typeof(InventoryGrid), "OnRightDown", InstanceAll, typeof(UIInputHandler));
                Exact(typeof(InventoryGui), "OnSelectedItem", InstanceAll,
                    typeof(InventoryGrid), typeof(ItemDrop.ItemData), typeof(Vector2i), typeof(InventoryGrid.Modifier));
                Exact(typeof(Humanoid), nameof(Humanoid.DropItem), InstanceAll,
                    typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int));
                Exact(typeof(Humanoid), nameof(Humanoid.UseItem), InstanceAll,
                    typeof(Inventory), typeof(ItemDrop.ItemData), typeof(bool));
                Exact(typeof(Humanoid), nameof(Humanoid.EquipItem), InstanceAll,
                    typeof(ItemDrop.ItemData), typeof(bool));
                Exact(typeof(Humanoid), "HideHandItems", InstanceAll,
                    typeof(bool), typeof(bool));
                Exact(typeof(Humanoid), "ShowHandItems", InstanceAll,
                    typeof(bool), typeof(bool));
                Exact(typeof(Humanoid), "SetUseHandVisual", InstanceAll,
                    typeof(GameObject), typeof(float));
                Exact(typeof(Humanoid), "DoInteractAnimation", InstanceAll,
                    typeof(GameObject));
                Exact(typeof(Humanoid), nameof(Humanoid.Pickup), InstanceAll,
                    typeof(GameObject), typeof(bool), typeof(bool));
                Exact(typeof(Player), "UpdateActionQueue", InstanceAll, typeof(float));
                Exact(typeof(Player), "UpdateWeaponLoading", InstanceAll,
                    typeof(ItemDrop.ItemData), typeof(float));
                Exact(typeof(Player), "SetWeaponLoaded", InstanceAll,
                    typeof(ItemDrop.ItemData));
                Exact(typeof(Player), "ResetLoadedWeapon", InstanceAll);
                Exact(typeof(Player), "ToggleEquipped", InstanceAll,
                    typeof(ItemDrop.ItemData));
                Exact(typeof(Player), "TryPlacePiece", InstanceAll, typeof(Piece));
                Exact(typeof(Player), "SetCraftingStation", InstanceAll,
                    typeof(CraftingStation));
                Exact(typeof(Player), "AttachStart", InstanceAll,
                    typeof(Transform), typeof(GameObject), typeof(bool), typeof(bool),
                    typeof(bool), typeof(string), typeof(Vector3), typeof(Transform));
                Exact(typeof(Player), nameof(Player.SetLocalPlayer), InstanceAll);
                Exact(typeof(Player), nameof(Player.CreateTombStone), InstanceAll);
                Exact(typeof(Player), nameof(Player.Save), InstanceAll, typeof(ZPackage));
                Exact(typeof(Player), nameof(Player.Load), InstanceAll, typeof(ZPackage));
                Exact(typeof(ItemDrop), nameof(ItemDrop.GetHoverText), InstanceAll);
                Exact(typeof(Smelter), "OnAddOre", InstanceAll,
                    typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData));
                Exact(typeof(Smelter), "OnAddFuel", InstanceAll,
                    typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData));
                Exact(typeof(CookingStation), "CookItem", InstanceAll,
                    typeof(Humanoid), typeof(ItemDrop.ItemData));
                Exact(typeof(CookingStation), "OnAddFuelSwitch", InstanceAll,
                    typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData));
                Exact(typeof(Fermenter), "AddItem", InstanceAll,
                    typeof(Humanoid), typeof(ItemDrop.ItemData));
                Exact(typeof(Incinerator), "OnIncinerate", InstanceAll,
                    typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData));
                Exact(typeof(ItemStand), nameof(ItemStand.UseItem), InstanceAll,
                    typeof(Humanoid), typeof(ItemDrop.ItemData));
                Exact(typeof(Attack), "ConsumeItem", InstanceAll);
                Exact(typeof(Attack), "UseAmmo", InstanceAll,
                    typeof(ItemDrop.ItemData).MakeByRefType());
                Exact(typeof(ArmorStand), "UpdateAttach", InstanceAll);
                Exact(typeof(ItemStand), "UpdateAttach", InstanceAll);
                Exact(typeof(Catapult), "OnLoadPointUse", InstanceAll,
                    typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData));
                Exact(typeof(OfferingBowl), nameof(OfferingBowl.UseItem), InstanceAll,
                    typeof(Humanoid), typeof(ItemDrop.ItemData));
                Exact(typeof(ShieldGenerator), "OnAddFuel", InstanceAll,
                    typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData));
                Exact(typeof(Container), "RPC_StackResponse", InstanceAll,
                    typeof(long), typeof(bool));
                Exact(typeof(Container), "RPC_TakeAllResponse", InstanceAll,
                    typeof(long), typeof(bool));
                Exact(typeof(Fireplace), nameof(Fireplace.Interact), InstanceAll,
                    typeof(Humanoid), typeof(bool), typeof(bool));
                Exact(typeof(Fireplace), nameof(Fireplace.UseItem), InstanceAll,
                    typeof(Humanoid), typeof(ItemDrop.ItemData));
                Exact(typeof(Turret), nameof(Turret.UseItem), InstanceAll,
                    typeof(Humanoid), typeof(ItemDrop.ItemData));
                Exact(typeof(Trader), nameof(Trader.UseItem), InstanceAll,
                    typeof(Humanoid), typeof(ItemDrop.ItemData));
                Exact(typeof(Pet), nameof(Pet.UseItem), InstanceAll,
                    typeof(Humanoid), typeof(ItemDrop.ItemData));
                Exact(typeof(Tameable), nameof(Tameable.UseItem), InstanceAll,
                    typeof(Humanoid), typeof(ItemDrop.ItemData));
                Exact(typeof(StoreGui), "SellItem", InstanceAll);
                Exact(typeof(FishingFloat), "FixedUpdate", InstanceAll);
                MethodInfo fishingOwner = Exact(
                    typeof(FishingFloat), "GetOwner", InstanceAll);
                if (fishingOwner.ReturnType != typeof(Character))
                    throw new MissingMethodException("FishingFloat.GetOwner() -> Character");
                Exact(typeof(OfferingBowl), "RPC_RemoveBossSpawnInventoryItems", InstanceAll,
                    typeof(long));
                Exact(typeof(OfferingBowl), "InitiateSpawnBoss", InstanceAll,
                    typeof(Vector3), typeof(bool));
                ExactField(typeof(OfferingBowl), "m_interactUser", typeof(Humanoid));
                ExactField(typeof(OfferingBowl), "m_usedSpawnItem", typeof(ItemDrop.ItemData));
                Exact(typeof(PlayerCustomizaton), "ShowBarberGui",
                    BindingFlags.Public | BindingFlags.Static);

                if (typeof(Player).GetField("m_customData", InstanceAll)?.FieldType != typeof(System.Collections.Generic.Dictionary<string, string>))
                    throw new MissingFieldException("Player.m_customData dictionary is missing.");
                foreach (string field in new[]
                         {
                             "m_gridPos", "m_stack", "m_durability", "m_equipped", "m_quality", "m_variant",
                             "m_crafterID", "m_crafterName", "m_customData", "m_worldLevel", "m_pickedUp", "m_cheated",
                             "m_shared", "m_dropPrefab"
                         })
                    if (typeof(ItemDrop.ItemData).GetField(field, InstanceAll) == null)
                        throw new MissingFieldException("ItemDrop.ItemData." + field + " is missing.");

                _ready = true;
                problem = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                _ready = false;
                problem = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        internal static void NotifyChanged(Inventory inventory)
        {
            if (!_ready || inventory == null) throw new InvalidOperationException("Installed Inventory.Changed contract is unavailable.");
            _changed(inventory, false, false);
        }

        internal static bool PlayerMayTakeInput(Player player) => _ready && player && _takeInput(player);

        internal static bool TryClickedSlot(InventoryGrid grid, UIInputHandler clicked, out Vector2i coordinate)
        {
            coordinate = new Vector2i(-1, -1);
            if (!_ready || !grid || !clicked) return false;
            // Use the exact cell delivered by the native mouse callback, not gamepad focus.
            coordinate = _buttonPosition(grid, clicked.gameObject);
            return coordinate.x >= 0 && coordinate.y >= 0;
        }

        internal static bool TryFocusedSlot(InventoryGrid grid, out Vector2i coordinate)
        {
            coordinate = new Vector2i(-1, -1);
            if (!_ready || !grid) return false;
            try
            {
                if (ZInput.IsGamepadActive())
                {
                    coordinate = (Vector2i)_gridSelected.GetValue(grid);
                    return coordinate.x >= 0 && coordinate.y >= 0;
                }
                object hovered = _hoveredElement.Invoke(grid, null);
                GameObject hoveredObject = (hovered as Component)?.gameObject;
                if (hoveredObject)
                {
                    coordinate = _buttonPosition(grid, hoveredObject);
                    if (coordinate.x >= 0 && coordinate.y >= 0) return true;
                }
                if (UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject)
                {
                    GameObject selected = UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject;
                    if (selected.transform.IsChildOf(grid.transform))
                    {
                        coordinate = _buttonPosition(grid, selected);
                        return coordinate.x >= 0 && coordinate.y >= 0;
                    }
                }
            }
            catch (Exception) { }
            return false;
        }

        internal static bool TryBottomRowScreenRects(InventoryGrid grid, int row, Rect[] slots)
        {
            if (!_ready || !grid || row < 0 || slots == null || slots.Length != TopologyLayout.RequiredWidth)
                return false;
            for (int i = 0; i < slots.Length; i++) slots[i] = default;
            try
            {
                for (int column = 0; column < slots.Length; column++)
                {
                    if (!TrySlotScreenRect(grid, column, row, out slots[column])) return false;
                }
                return true;
            }
            catch (Exception) { }
            return false;
        }

        internal static ItemDrop.ItemData CraftingCommitItem(InventoryGui gui)
        {
            try { return gui == null ? null : _craftUpgradeItem.GetValue(gui) as ItemDrop.ItemData; }
            catch (Exception) { return null; }
        }

        internal static InventoryItemCategory Category(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null) return InventoryItemCategory.Unknown;
            switch (item.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.Material: return InventoryItemCategory.Material;
                case ItemDrop.ItemData.ItemType.Consumable: return InventoryItemCategory.Consumable;
                case ItemDrop.ItemData.ItemType.Helmet: return InventoryItemCategory.Helmet;
                case ItemDrop.ItemData.ItemType.Chest: return InventoryItemCategory.Chest;
                case ItemDrop.ItemData.ItemType.Legs: return InventoryItemCategory.Legs;
                case ItemDrop.ItemData.ItemType.Shoulder: return InventoryItemCategory.Cape;
                case ItemDrop.ItemData.ItemType.Utility: return InventoryItemCategory.Utility;
                case ItemDrop.ItemData.ItemType.Tool: return InventoryItemCategory.Tool;
                case ItemDrop.ItemData.ItemType.Ammo: return InventoryItemCategory.Ammunition;
                default: return InventoryItemCategory.Other;
            }
        }

        internal static bool TrySlotScreenRect(
            InventoryGrid grid,
            int column,
            int row,
            out Rect slot)
        {
            slot = default;
            if (!_ready || !grid || column < 0 || row < 0) return false;
            try
            {
                Inventory inventory = grid.GetInventory();
                if (inventory == null || column >= inventory.GetWidth() || row >= inventory.GetHeight())
                    return false;
                object element = _getElement.Invoke(
                    grid,
                    new object[] { column, row, inventory.GetWidth() });
                GameObject elementObject = (element as Component)?.gameObject;
                RectTransform rect = elementObject != null
                    ? elementObject.GetComponent<RectTransform>()
                    : null;
                if (!rect) return false;
                Canvas canvas = grid.GetComponentInParent<Canvas>();
                Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? canvas.worldCamera
                    : null;
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                Vector2 lowerLeft = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                Vector2 upperRight = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                float left = Math.Min(lowerLeft.x, upperRight.x);
                float right = Math.Max(lowerLeft.x, upperRight.x);
                float bottom = Math.Min(lowerLeft.y, upperRight.y);
                float top = Math.Max(lowerLeft.y, upperRight.y);
                if (right - left < 8f || top - bottom < 8f) return false;
                slot = new Rect(left, Screen.height - top, right - left, top - bottom);
                return true;
            }
            catch (Exception) { return false; }
        }

        internal static bool InventoryModalVisible()
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null) return false;
            return gui.m_splitDialog != null && gui.m_splitDialog.IsActive ||
                   Active(gui.m_variantDialog) ||
                   Active(gui.m_skillsDialog) ||
                   Active(gui.m_textsDialog);
        }

        private static bool Active(Component component) =>
            component != null && component.gameObject.activeInHierarchy;

        internal static string PrefabId(ItemDrop.ItemData item)
        {
            string value = item?.m_dropPrefab ? item.m_dropPrefab.name : item?.m_shared?.m_name;
            value = value ?? string.Empty;
            const string clone = "(Clone)";
            if (value.EndsWith(clone, StringComparison.Ordinal)) value = value.Substring(0, value.Length - clone.Length);
            value = value.Trim();
            return value.Length <= 128 ? value : string.Empty;
        }

        private static MethodInfo Exact(Type type, string name, BindingFlags flags, params Type[] parameters)
        {
            MethodInfo method = type.GetMethod(name, flags, null, parameters, null);
            return method ?? throw new MissingMethodException(type.FullName, name);
        }

        private static FieldInfo ExactField(Type type, string name, Type expected)
        {
            FieldInfo field = type.GetField(name, InstanceAll) ?? throw new MissingFieldException(type.FullName, name);
            if (expected != null && field.FieldType != expected) throw new MissingFieldException(type.FullName, name + ":" + expected.FullName);
            return field;
        }
    }
}
