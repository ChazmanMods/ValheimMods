using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RunicInteraction.Integration
{
    internal static class ValheimAccess
    {
        internal const string AuditedGameVersion = "1.0.15";

        private const BindingFlags InstanceAll =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal delegate void DoorRpcDelegate(Door instance, long sender, bool forward);
        internal delegate bool DoorCanInteractDelegate(Door instance);
        internal delegate bool CookingHaveDoneDelegate(CookingStation instance);
        internal delegate Vector2i GridButtonPositionDelegate(InventoryGrid instance, GameObject button);
        internal delegate bool ContainerCheckAccessDelegate(Container instance, long playerId);
        internal delegate void InventoryGuiSetRecipeDelegate(InventoryGui instance, int index, bool center);
        internal delegate void InventoryGuiSetActiveGroupDelegate(InventoryGui instance, int index, bool sound);

        private static DoorRpcDelegate _doorRpc;
        private static DoorCanInteractDelegate _doorCanInteract;
        private static CookingHaveDoneDelegate _cookingHaveDone;
        private static GridButtonPositionDelegate _gridButtonPosition;
        private static ContainerCheckAccessDelegate _containerCheckAccess;
        private static InventoryGuiSetRecipeDelegate _setRecipe;
        private static InventoryGuiSetActiveGroupDelegate _setActiveGroup;
        private static FieldInfo _currentContainer;
        private static FieldInfo _dragObject;
        private static FieldInfo _queuedTextReceiver;
        private static FieldInfo _selectedRecipe;
        private static FieldInfo _availableRecipes;
        private static PropertyInfo _pairRecipe;
        private static PropertyInfo _pairItem;
        private static bool _ready;

        // Version is diagnostic only; Initialize validates the APIs we actually use.
        internal static string ReadGameVersion(Type versionType = null)
        {
            try
            {
                versionType = versionType ?? typeof(Player).Assembly.GetType("Version", false);
                return versionType?.GetProperty("CurrentVersion", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null, null)?.ToString() ?? "unknown";
            }
            catch (Exception) { return "unknown"; }
        }

        internal static bool Initialize(out string problem)
        {
            problem = string.Empty;
            try
            {

                _doorRpc = DelegateFor<DoorRpcDelegate>(typeof(Door), "RPC_UseDoor", typeof(long), typeof(bool));
                _doorCanInteract = DelegateFor<DoorCanInteractDelegate>(typeof(Door), "CanInteract");
                _cookingHaveDone = DelegateFor<CookingHaveDoneDelegate>(typeof(CookingStation), "HaveDoneItem");
                _gridButtonPosition = DelegateFor<GridButtonPositionDelegate>(
                    typeof(InventoryGrid), "GetButtonPos", typeof(GameObject));
                _containerCheckAccess = DelegateFor<ContainerCheckAccessDelegate>(
                    typeof(Container), "CheckAccess", typeof(long));
                _setRecipe = DelegateFor<InventoryGuiSetRecipeDelegate>(
                    typeof(InventoryGui), "SetRecipe", typeof(int), typeof(bool));
                _setActiveGroup = DelegateFor<InventoryGuiSetActiveGroupDelegate>(
                    typeof(InventoryGui), "SetActiveGroup", typeof(int), typeof(bool));

                _currentContainer = ExactField(typeof(InventoryGui), "m_currentContainer");
                _dragObject = ExactField(typeof(InventoryGui), "m_dragGo");
                _queuedTextReceiver = ExactField(typeof(TextInput), "m_queuedSign");
                _selectedRecipe = ExactField(typeof(InventoryGui), "m_selectedRecipe");
                _availableRecipes = ExactField(typeof(InventoryGui), "m_availableRecipes");
                Type pair = typeof(InventoryGui).GetNestedType("RecipeDataPair", BindingFlags.NonPublic);
                if (pair == null) throw new MissingMemberException("InventoryGui.RecipeDataPair");
                _pairRecipe = ExactProperty(pair, "Recipe", typeof(Recipe));
                _pairItem = ExactProperty(pair, "ItemData", typeof(ItemDrop.ItemData));

                Exact(typeof(Switch), nameof(Switch.Interact), InstanceAll,
                    typeof(Humanoid), typeof(bool), typeof(bool));
                Exact(typeof(Smelter), "Awake", InstanceAll);
                Exact(typeof(CookingStation), "Awake", InstanceAll);
                Exact(typeof(CookingStation), nameof(CookingStation.Interact), InstanceAll,
                    typeof(Humanoid), typeof(bool), typeof(bool));
                Exact(typeof(Fermenter), nameof(Fermenter.Interact), InstanceAll,
                    typeof(Humanoid), typeof(bool), typeof(bool));
                Exact(typeof(ShieldGenerator), "Start", InstanceAll);
                Exact(typeof(Door), nameof(Door.Interact), InstanceAll,
                    typeof(Humanoid), typeof(bool), typeof(bool));
                Exact(typeof(InventoryGrid), "OnLeftClick", InstanceAll, typeof(UIInputHandler));
                Exact(typeof(InventoryGui), "OnSelectedItem", InstanceAll,
                    typeof(InventoryGrid), typeof(ItemDrop.ItemData), typeof(Vector2i),
                    typeof(InventoryGrid.Modifier));
                Exact(typeof(Humanoid), nameof(Humanoid.EquipItem), InstanceAll,
                    typeof(ItemDrop.ItemData), typeof(bool));
                Exact(typeof(Humanoid), nameof(Humanoid.UnequipItem), InstanceAll,
                    typeof(ItemDrop.ItemData), typeof(bool));
                Exact(typeof(Humanoid), nameof(Humanoid.HideHandItems), InstanceAll,
                    typeof(bool), typeof(bool));
                Exact(typeof(Humanoid), "ShowHandItems", InstanceAll,
                    typeof(bool), typeof(bool));
                Exact(typeof(InventoryGui), "SetupCrafting", InstanceAll);
                Exact(typeof(InventoryGui), nameof(InventoryGui.Hide), InstanceAll);
                Exact(typeof(InventoryGui), nameof(InventoryGui.OnTabCraftPressed), InstanceAll);
                Exact(typeof(InventoryGui), nameof(InventoryGui.OnTabUpgradePressed), InstanceAll);
                Exact(typeof(TextInput), nameof(TextInput.RequestText), InstanceAll,
                    typeof(TextReceiver), typeof(string), typeof(int));
                Exact(typeof(TextInput), "setText", InstanceAll, typeof(string));
                Exact(typeof(TextInput), nameof(TextInput.Hide), InstanceAll);
                Exact(typeof(Humanoid), nameof(Humanoid.Pickup), InstanceAll,
                    typeof(GameObject), typeof(bool), typeof(bool));

                _ready = true;
                return true;
            }
            catch (Exception exception)
            {
                _ready = false;
                problem = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        internal static bool CookingHasDoneItem(CookingStation station) =>
            _ready && station && _cookingHaveDone(station);

        internal static bool DoorCanInteract(Door door) =>
            _ready && door && _doorCanInteract(door);

        internal static void CloseDoor(Door door, long sender) => _doorRpc(door, sender, false);

        internal static Vector2i GetButtonPosition(InventoryGrid grid, GameObject button) =>
            _gridButtonPosition(grid, button);

        internal static Container GetCurrentContainer(InventoryGui gui) =>
            gui == null ? null : (Container)_currentContainer.GetValue(gui);

        internal static bool HasDrag(InventoryGui gui) =>
            gui != null && (GameObject)_dragObject.GetValue(gui);

        internal static bool ContainerAllows(Container container, long playerId) =>
            container && _containerCheckAccess(container, playerId);

        internal static TextReceiver GetQueuedTextReceiver(TextInput input) =>
            input == null ? null : (TextReceiver)_queuedTextReceiver.GetValue(input);

        internal static void ClearQueuedTextReceiver(TextInput input)
        {
            if (input != null) _queuedTextReceiver.SetValue(input, null);
        }

        internal static object GetSelectedRecipePair(InventoryGui gui) =>
            gui == null ? null : _selectedRecipe.GetValue(gui);

        internal static IList GetAvailableRecipePairs(InventoryGui gui) =>
            gui == null ? null : _availableRecipes.GetValue(gui) as IList;

        internal static Recipe GetPairRecipe(object pair) =>
            pair == null ? null : (Recipe)_pairRecipe.GetValue(pair, null);

        internal static ItemDrop.ItemData GetPairItem(object pair) =>
            pair == null ? null : (ItemDrop.ItemData)_pairItem.GetValue(pair, null);

        internal static void SetRecipe(InventoryGui gui, int index, bool center) =>
            _setRecipe(gui, index, center);

        internal static void SetActiveGroup(InventoryGui gui, int group, bool sound) =>
            _setActiveGroup(gui, group, sound);

        private static T DelegateFor<T>(Type type, string name, params Type[] parameters)
            where T : Delegate
        {
            MethodInfo method = Exact(type, name, InstanceAll, parameters);
            return AccessTools.MethodDelegate<T>(method);
        }

        private static MethodInfo Exact(
            Type type,
            string name,
            BindingFlags flags,
            params Type[] parameters)
        {
            MethodInfo method = type.GetMethod(name, flags, null, parameters, null);
            return method ?? throw new MissingMethodException(type.FullName, name);
        }

        private static FieldInfo ExactField(Type type, string name) =>
            type.GetField(name, InstanceAll) ?? throw new MissingFieldException(type.FullName, name);

        private static PropertyInfo ExactProperty(Type type, string name, Type propertyType)
        {
            PropertyInfo property = type.GetProperty(name, InstanceAll);
            if (property == null || property.PropertyType != propertyType)
                throw new MissingMemberException(type.FullName, name);
            return property;
        }
    }
}
