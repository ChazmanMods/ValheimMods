using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using RunicInventory.Core;
using UnityEngine;

namespace RunicInventory.Integration
{
    // Optional adapter for the audited Thunderstore 1.9.99 binary (plugin version 1.9.9).
    // Never edits Better Archery's configuration, item instances, or quiver equipment state.
    internal static class BetterArcheryCompatibility
    {
        internal const string Guid = "ishid4.mods.betterarchery";
        internal const string AuditedHash = "549B3B6AC69C512E4B3BA1EEECD34687ACB72712A3B068BD1D2783B06BE1F823";
        private static ConfigEntry<bool> _enabled;
        private static FieldInfo _row;
        private static MethodInfo _equipped;
        private static MethodInfo _getElement;
        private static MethodInfo _binding;
        private static ConfigEntry<KeyboardShortcut> _holdingKey;
        private static FieldInfo _hasAuga;
        internal static bool Active => _enabled?.Value == true && _row != null;
        internal static int QuiverRow => Active ? (int)_row.GetValue(null) : -1;
        internal static bool Equipped => Active && (bool)_equipped.Invoke(null, null);

        internal static void Initialize(Harmony harmony)
        {
            if (!Chainloader.PluginInfos.TryGetValue(Guid, out var info) || info.Instance == null) return;
            Assembly assembly = info.Instance.GetType().Assembly;
            using (var stream = File.OpenRead(assembly.Location))
            using (var sha = SHA256.Create())
                if (BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") != AuditedHash)
                {
                    Diagnostics.Warn("Better Archery binary is not the audited quiver build; automatic layout integration was not enabled.");
                    return;
                }
            Type api = assembly.GetType("BetterArchery.BetterArchery", true);
            FieldInfo row = AccessTools.Field(api, "QuiverRowIndex");
            var enabled = AccessTools.Field(api, "ConfigQuiverEnabled")?.GetValue(null) as ConfigEntry<bool>;
            MethodInfo equipped = AccessTools.Method(api, "IsQuiverEquipped", Type.EmptyTypes);
            MethodInfo binding = AccessTools.Method(api, "GetBindingKeycode", new[] { typeof(int) });
            var holding = AccessTools.Field(api, "HoldingKeyCode")?.GetValue(null) as ConfigEntry<KeyboardShortcut>;
            MethodInfo hide = AccessTools.Method(assembly.GetType("BetterArchery.InventoryGrid_UpdateGui_Patch", true), "HideModRows");
            MethodInfo gridPostfix = AccessTools.Method(assembly.GetType("BetterArchery.InventoryGrid_UpdateGui_Patch", true), "Postfix");
            MethodInfo recovery = AccessTools.Method(assembly.GetType("BetterArchery.Tombstone+TombStone_OnTakeAllSuccess_Patch", true), "Postfix");
            MethodInfo debugCommand = AccessTools.Method(assembly.GetType("BetterArchery.TerminalAwake_Patch+<>c", true), "<Postfix>b__0_0");
            MethodInfo resize = AccessTools.Method(assembly.GetType("BetterArchery.Player_SetInventorySize_Patch", true), "Prefix");
            MethodInfo find = AccessTools.Method(assembly.GetType("BetterArchery.Inventory_FindEmptySlot_Patch", true), "Prefix");
            MethodInfo capacity = AccessTools.Method(assembly.GetType("BetterArchery.Inventory_HaveEmptySlot_Patch", true), "Prefix");
            MethodInfo element = AccessTools.Method(typeof(InventoryGrid), "GetElement", new[] { typeof(int), typeof(int), typeof(int) });
            if (row?.FieldType != typeof(int) || enabled == null || equipped?.ReturnType != typeof(bool) ||
                hide == null || recovery == null || debugCommand == null || resize == null || find == null || capacity == null ||
                element == null || binding?.ReturnType != typeof(KeyCode) || holding == null)
                throw new MissingMemberException("Better Archery quiver contract changed.");
            harmony.Patch(hide, prefix: new HarmonyMethod(typeof(BetterArcheryCompatibility), nameof(HideRows)));
            harmony.Patch(recovery, prefix: new HarmonyMethod(typeof(BetterArcheryCompatibility), nameof(AllowLegacyRecovery)));
            harmony.Patch(debugCommand, prefix: new HarmonyMethod(typeof(BetterArcheryCompatibility), nameof(AllowDebugCommand)));
            // HarmonyX calls ALL prefixes even after our Player/Inventory prefix returns false.
            // Guard the BA patch bodies themselves: priority alone cannot prevent their writes.
            harmony.Patch(resize, prefix: new HarmonyMethod(typeof(BetterArcheryCompatibility), nameof(AllowArcheryResize)));
            harmony.Patch(find, prefix: new HarmonyMethod(typeof(BetterArcheryCompatibility), nameof(AllowArcheryFind)));
            harmony.Patch(capacity, prefix: new HarmonyMethod(typeof(BetterArcheryCompatibility), nameof(AllowArcheryCapacity)));
            // Run after BA has positioned its visible quiver cells, regardless of Harmony owner IDs.
            try
            {
                if (gridPostfix != null)
                    harmony.Patch(gridPostfix, postfix: new HarmonyMethod(typeof(BetterArcheryCompatibility), nameof(CompactPanel)));
            }
            catch (Exception exception) { CompactQuiverPanel.Fault(exception); }
            _hasAuga = AccessTools.Field(api, "HasAuga");
            _row = row;
            _equipped = equipped;
            _getElement = element;
            _enabled = enabled;
            _binding = binding;
            _holdingKey = holding;
            Diagnostics.Info("Better Archery quiver compatibility ready with HarmonyX resize/slot guards. Quiver and Runic roles use separate reserved rows. Configure distinct quiver/quick-slot hotkeys if needed.");
        }

        private static bool AllowArcheryResize(Player __0, ref bool __result)
        {
            if (!(Plugin.Instance?.Runtime?.HandlesNativeInventorySize(__0) ?? false)) return true;
            __result = false;
            return false;
        }

        private static bool AllowArcheryFind(Inventory __0, ref Vector2i __1, bool __2, ref bool __result)
        {
            var runtime = Plugin.Instance?.Runtime;
            if (runtime == null || !runtime.TryFindEmptySlot(__0, __2, out Vector2i slot)) return true;
            __1 = slot;
            __result = false;
            return false;
        }

        private static bool AllowArcheryCapacity(Inventory __0, ref bool __1, ref bool __result)
        {
            var runtime = Plugin.Instance?.Runtime;
            if (runtime == null || !runtime.TryFindEmptySlot(__0, true, out Vector2i slot)) return true;
            __1 = slot.x >= 0 && slot.y >= 0;
            __result = false;
            return false;
        }

        internal static void SetQuiverRow(int row) { if (Active) _row.SetValue(null, row); }

        internal static bool QuiverShortcutDown()
        {
            if (!Equipped) return false;
            KeyCode hold = _holdingKey.Value.MainKey;
            if (hold != KeyCode.None && !Input.GetKey(hold)) return false;
            for (int i = 0; i < 3; i++)
                if (Input.GetKeyDown((KeyCode)_binding.Invoke(null, new object[] { i }))) return true;
            return false;
        }

        internal static void Reset()
        {
            CompactQuiverPanel.Restore();
            _enabled = null;
            _holdingKey = null;
            _row = null;
            _equipped = _getElement = _binding = null;
            _hasAuga = null;
        }

        private static void CompactPanel(InventoryGrid __0)
        {
            try
            {
                if (__0 == null || __0.name != "PlayerGrid") return;
                if (!OwnsLayout(__0.GetInventory()) || __0.GetInventory().GetHeight() != QuiverRow + 2 ||
                    (bool?)_hasAuga?.GetValue(null) == true)
                { CompactQuiverPanel.Restore(); return; }
                CompactQuiverPanel.Apply(__0,
                    (x, y) => _getElement.Invoke(__0, new object[] { x, y, 8 }) as Component, Equipped);
            }
            catch (Exception exception) { CompactQuiverPanel.Fault(exception); }
        }

        private static bool OwnsLayout(Inventory inventory) => Active && Player.m_localPlayer != null &&
            ReferenceEquals(Player.m_localPlayer.GetInventory(), inventory) &&
            Player.m_localPlayer.m_customData?.ContainsKey(DedicatedRowPlan.QuiverMetadataKey) == true &&
            Player.m_localPlayer.m_customData.ContainsKey(DedicatedRowPlan.MetadataKey);

        private static bool HideRows(InventoryGrid __0, IList __1)
        {
            Inventory inventory = __0?.GetInventory();
            if (!OwnsLayout(inventory)) return true;
            int quiverRow = QuiverRow;
            if (quiverRow < 5 || quiverRow + 1 != inventory.GetHeight() - 1) return false;
            bool equipped = Equipped;
            for (int y = quiverRow - 1; y <= quiverRow + 1; y++)
                for (int x = 0; x < 8; x++)
                {
                    var element = _getElement.Invoke(__0, new object[] { x, y, inventory.GetWidth() }) as Component;
                    if (element != null) element.gameObject.SetActive(y == quiverRow + 1 || y == quiverRow && x < 3 && equipped);
                }
            // BA's CreateQuiverSlots runs next and reveals only the three ammo cells.
            return false;
        }

        private static bool AllowLegacyRecovery() => !OwnsLayout(Player.m_localPlayer?.GetInventory());

        private static bool AllowDebugCommand(Terminal.ConsoleEventArgs __0)
        {
            if (!OwnsLayout(Player.m_localPlayer?.GetInventory()) || __0.Length < 2 ||
                __0.FullLine.Substring(__0[0].Length + 1) != "drop") return true;
            __0.Context.AddString("Runic Inventory: ba drop assumes extra rows are invalid. Move items manually while the combined quiver layout is active.");
            return false;
        }
    }
}
