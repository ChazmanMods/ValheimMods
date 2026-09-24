using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace RunicAgriculture.Integration
{
    internal static class ValheimAccess
    {
        private delegate bool PlayerInputDelegate(Player player);
        private static readonly FieldInfo PlacementGhostField =
            AccessTools.Field(typeof(Player), "m_placementGhost");
        private static readonly FieldInfo PlaceRotationDegreesField =
            AccessTools.Field(typeof(Player), "m_placeRotationDegrees");
        private static readonly MethodInfo GetBuildStaminaMethod =
            AccessTools.Method(typeof(Player), "GetBuildStamina", Type.EmptyTypes);
        private static readonly MethodInfo GetPlaceDurabilityMethod =
            AccessTools.Method(typeof(Player), "GetPlaceDurability", new[] { typeof(ItemDrop.ItemData) });
        private static readonly MethodInfo InventoryChangedMethod =
            AccessTools.Method(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) });
        private static readonly PlayerInputDelegate TakeInput = ResolveTakeInput();
        private static readonly MethodInfo PlantGrowTimeMethod =
            AccessTools.Method(typeof(Plant), "GetGrowTime", Type.EmptyTypes);
        private static readonly MethodInfo PlantAgeMethod =
            AccessTools.Method(typeof(Plant), "TimeSincePlanted", Type.EmptyTypes);
        private static readonly MethodInfo BeeHoneyMethod =
            AccessTools.Method(typeof(Beehive), "GetHoneyLevel", Type.EmptyTypes);
        private static readonly MethodInfo BeeBiomeMethod =
            AccessTools.Method(typeof(Beehive), "CheckBiome", Type.EmptyTypes);
        private static readonly MethodInfo BeeSpaceMethod =
            AccessTools.Method(typeof(Beehive), "HaveFreeSpace", Type.EmptyTypes);
        private static readonly MethodInfo GetControllerButtonMethod =
            AccessTools.Method(typeof(ZInput), nameof(ZInput.GetButton), new[] { typeof(string) });
        private static readonly MethodInfo GetControllerButtonDownMethod =
            AccessTools.Method(typeof(ZInput), nameof(ZInput.GetButtonDown), new[] { typeof(string) });
        private static readonly MethodInfo GetControllerButtonDefinitionMethod =
            AccessTools.Method(typeof(ZInput), nameof(ZInput.GetButtonDef), new[] { typeof(string) });
        private static readonly MethodInfo GetControllerActionPathMethod =
            AccessTools.Method(typeof(ZInput.ButtonDef), nameof(ZInput.ButtonDef.GetActionPath),
                new[] { typeof(bool) });
        private static readonly MethodInfo GetKeyboardKeyMethod =
            AccessTools.Method(typeof(ZInput), nameof(ZInput.GetKey), new[] { typeof(KeyCode), typeof(bool) });
        private static readonly MethodInfo GetKeyboardKeyDownMethod =
            AccessTools.Method(typeof(ZInput), nameof(ZInput.GetKeyDown), new[] { typeof(KeyCode), typeof(bool) });
        private static readonly MethodInfo GetBoundKeyStringMethod =
            AccessTools.Method(typeof(ZInput), nameof(ZInput.GetBoundKeyString),
                new[] { typeof(string), typeof(bool) });
        private static readonly MethodInfo GetMouseScrollWheelMethod =
            AccessTools.Method(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel), Type.EmptyTypes);

        internal static void Verify()
        {
            if (PlacementGhostField == null || PlaceRotationDegreesField == null ||
                PlaceRotationDegreesField.FieldType != typeof(float) || GetBuildStaminaMethod == null ||
                GetPlaceDurabilityMethod == null || InventoryChangedMethod == null || TakeInput == null ||
                PlantGrowTimeMethod == null || PlantAgeMethod == null ||
                BeeHoneyMethod == null || BeeBiomeMethod == null || BeeSpaceMethod == null ||
                GetControllerButtonMethod == null || GetControllerButtonDownMethod == null ||
                GetControllerButtonDefinitionMethod == null || GetControllerActionPathMethod == null ||
                GetKeyboardKeyMethod == null || GetKeyboardKeyDownMethod == null ||
                GetBoundKeyStringMethod == null || GetMouseScrollWheelMethod == null)
                throw new MissingMemberException(
                    "Valheim placement or ZInput signatures do not match the audited Valheim 1.0 contract.");
        }

        internal static GameObject GetPlacementGhost(Player player) =>
            PlacementGhostField?.GetValue(player) as GameObject;

        internal static float PlaceRotationDegrees(Player player) =>
            player == null ? 22.5f : Convert.ToSingle(PlaceRotationDegreesField.GetValue(player));

        internal static float GetBuildStamina(Player player) =>
            Convert.ToSingle(GetBuildStaminaMethod.Invoke(player, Array.Empty<object>()));

        internal static float GetPlaceDurability(Player player, ItemDrop.ItemData tool) =>
            Convert.ToSingle(GetPlaceDurabilityMethod.Invoke(player, new object[] { tool }));

        internal static void NotifyInventoryChanged(Inventory inventory)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            InventoryChangedMethod.Invoke(inventory, new object[] { false, false });
        }

        internal static bool PlayerTakesInput(Player player) =>
            player != null && TakeInput(player);

        private static PlayerInputDelegate ResolveTakeInput()
        {
            try
            {
                MethodInfo method = AccessTools.Method(typeof(Player), "TakeInput", Type.EmptyTypes);
                return method == null
                    ? null
                    : AccessTools.MethodDelegate<PlayerInputDelegate>(method);
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static double PlantGrowTime(Plant plant) =>
            Convert.ToDouble(PlantGrowTimeMethod.Invoke(plant, Array.Empty<object>()));

        internal static double PlantAge(Plant plant) =>
            Convert.ToDouble(PlantAgeMethod.Invoke(plant, Array.Empty<object>()));

        internal static int BeeHoney(Beehive hive) =>
            Convert.ToInt32(BeeHoneyMethod.Invoke(hive, Array.Empty<object>()));

        internal static bool BeeBiomeValid(Beehive hive) =>
            Convert.ToBoolean(BeeBiomeMethod.Invoke(hive, Array.Empty<object>()));

        internal static bool BeeHasSpace(Beehive hive) =>
            Convert.ToBoolean(BeeSpaceMethod.Invoke(hive, Array.Empty<object>()));

        internal static bool ControllerButtonHeldRaw(Core.ValheimControllerAction action)
        {
            try
            {
                ZInput.ButtonDef definition = ControllerButtonDefinition(action);
                return definition != null && definition.Source == ZInput.InputSource.Gamepad &&
                       definition.Held;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static bool ShortcutDown(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None ||
                !ZInput.GetKeyDown(shortcut.MainKey, false)) return false;
            IEnumerable<KeyCode> modifiers = shortcut.Modifiers;
            if (modifiers == null) return true;
            foreach (KeyCode modifier in modifiers)
                if (modifier == KeyCode.None || !ZInput.GetKey(modifier, false)) return false;
            return true;
        }

        internal static bool KeyHeld(KeyCode left, KeyCode right) =>
            ZInput.GetKey(left, false) || ZInput.GetKey(right, false);

        internal static bool KeyDown(KeyCode key) => ZInput.GetKeyDown(key, false);

        internal static float MouseWheel() => ZInput.GetMouseScrollWheel();

        internal static bool ButtonDown(string action) =>
            !string.IsNullOrWhiteSpace(action) && ZInput.GetButtonDown(action);

        internal static string BoundKeyLabel(string action, bool gamepad, string fallback)
        {
            try
            {
                string label = ZInput.instance?.GetBoundKeyString(action, gamepad)?.Trim();
                return string.IsNullOrEmpty(label) ? fallback : label;
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        internal static string ControllerChordLabel(
            Core.AgricultureControllerBindings bindings,
            Core.ValheimControllerAction primary)
        {
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            return ControllerControlLabel(bindings.Modifier) + " + " +
                   ControllerControlLabel(primary);
        }

        internal static string ControllerControlLabel(Core.ValheimControllerAction action)
        {
            string fallback = Core.ControllerActionDisplay.Friendly(action);
            try
            {
                string path = ControllerEffectivePath(ControllerButtonDefinition(action));
                return ControllerPathLabel(path, fallback);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        // ZInput.GetBoundKeyString returns TextMeshPro sprite tags for gamepad buttons. The
        // replacement bar uses Unity IMGUI, which cannot render those tags, so controller labels
        // are derived from the currently effective action path instead. This still follows live
        // rebinding and Valheim's Default/Alt1/Alt2 layout changes without displaying raw markup.
        internal static string ControllerPathLabel(string path, string fallback)
        {
            if (string.IsNullOrWhiteSpace(path)) return fallback;
            string normalized = path.Trim().Replace('\\', '/');
            string lower = normalized.ToLowerInvariant();
            if (lower.EndsWith("/buttonsouth", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_b81cc6f28763");
            if (lower.EndsWith("/buttoneast", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_7df3fba69cfe");
            if (lower.EndsWith("/buttonwest", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_9affb90082db");
            if (lower.EndsWith("/buttonnorth", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_7c3f8373297f");
            if (lower.EndsWith("/leftshoulder", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_056a789f13d6");
            if (lower.EndsWith("/rightshoulder", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_5dc809519564");
            if (lower.EndsWith("/lefttrigger", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_b32623c3cbec");
            if (lower.EndsWith("/righttrigger", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_b4a4bae77812");
            if (lower.EndsWith("/leftstickpress", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_3c1817d8b64c");
            if (lower.EndsWith("/rightstickpress", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_ec9d76d1eaab");
            if (lower.EndsWith("/dpad/up", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_1f85615c359b");
            if (lower.EndsWith("/dpad/down", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_6e47237e9353");
            if (lower.EndsWith("/dpad/left", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_321cd19f454a");
            if (lower.EndsWith("/dpad/right", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_89cbe5b197bb");
            if (lower.EndsWith("/start", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_ed078579d5a0");
            if (lower.EndsWith("/select", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_0b2e04edf664");
            return fallback;
        }

        internal static bool ControllerButtonDownRaw(Core.ValheimControllerAction action)
        {
            try
            {
                ZInput.ButtonDef definition = ControllerButtonDefinition(action);
                return definition != null && definition.Source == ZInput.InputSource.Gamepad &&
                       definition.Pressed;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static ZInput.ButtonDef ControllerButtonDefinition(
            Core.ValheimControllerAction action) => ZInput.instance?.GetButtonDef(action.ToString());

        internal static string ControllerEffectivePath(ZInput.ButtonDef definition) =>
            definition?.GetActionPath(true)?.Trim();

        internal static bool TryVerifyControllerActions(
            Core.AgricultureControllerBindings bindings,
            out string problem,
            out string pathSignature)
        {
            pathSignature = string.Empty;
            if (ZInput.instance == null)
            {
                problem = "Valheim ZInput is not initialized";
                return false;
            }
            if (!bindings.TryValidate(out problem)) return false;
            Core.ValheimControllerAction[] actions =
            {
                bindings.Modifier,
                bindings.Confirm,
                bindings.Cycle,
                bindings.AreaHarvest,
                bindings.PreviousEditorField,
                bindings.NextEditorField,
                bindings.DecreaseEditorValue,
                bindings.IncreaseEditorValue
            };
            var definitions = new ZInput.ButtonDef[actions.Length];
            var paths = new string[actions.Length];
            for (int index = 0; index < actions.Length; index++)
            {
                string name = actions[index].ToString();
                try
                {
                    definitions[index] = ZInput.instance.GetButtonDef(name);
                }
                catch (Exception exception)
                {
                    problem = "Valheim input action '" + name + "' could not be queried (" +
                              exception.GetType().Name + ")";
                    return false;
                }
                if (definitions[index] == null)
                {
                    problem = "Valheim input action '" + name + "' is unavailable";
                    return false;
                }
                if (definitions[index].Source != ZInput.InputSource.Gamepad)
                {
                    problem = "Valheim input action '" + name + "' is not a gamepad action";
                    return false;
                }
                try { paths[index] = definitions[index].GetActionPath(true)?.Trim(); }
                catch (Exception exception)
                {
                    problem = "Valheim input action '" + name + "' has no queryable effective path (" +
                              exception.GetType().Name + ")";
                    return false;
                }
            }
            if (!bindings.TryValidateEffectivePaths(
                    paths[0], paths[1], paths[2], paths[3], paths[4], paths[5], paths[6], paths[7],
                    out problem)) return false;
            var signature = new System.Text.StringBuilder();
            for (int index = 0; index < actions.Length; index++)
            {
                if (index > 0) signature.Append('|');
                signature.Append(actions[index]).Append('=').Append(paths[index]);
            }
            pathSignature = signature.ToString();
            return true;
        }
    }

    internal static class PrefabIdentity
    {
        internal static string Of(GameObject gameObject)
        {
            if (gameObject == null) return string.Empty;
            string name = gameObject.name ?? string.Empty;
            const string cloneSuffix = "(Clone)";
            if (name.EndsWith(cloneSuffix, StringComparison.Ordinal))
                name = name.Substring(0, name.Length - cloneSuffix.Length);
            return name.Trim();
        }
    }
}
