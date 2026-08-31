using System;
using System.Collections.Generic;
using RunicInventory.Core;
using UnityEngine;

namespace RunicInventory.Integration
{
    internal sealed class ControllerBindingState
    {
        internal ControllerBindingState(
            ZInput.ButtonDef modifier,
            ZInput.ButtonDef quick1,
            ZInput.ButtonDef quick2,
            ZInput.ButtonDef quick3,
            ZInput.ButtonDef sort,
            ZInput.ButtonDef toggleLock,
            string reasonCode,
            bool legacyQuick1Mapped)
        {
            Modifier = modifier;
            Quick1 = quick1;
            Quick2 = quick2;
            Quick3 = quick3;
            Sort = sort;
            ToggleLock = toggleLock;
            ReasonCode = reasonCode ?? "controller.invalid";
            LegacyQuick1Mapped = legacyQuick1Mapped;
            ValidRouteCount =
                (quick1 != null ? 1 : 0) +
                (quick2 != null ? 1 : 0) +
                (quick3 != null ? 1 : 0) +
                (sort != null ? 1 : 0) +
                (toggleLock != null ? 1 : 0);
        }

        internal ZInput.ButtonDef Modifier { get; }
        internal ZInput.ButtonDef Quick1 { get; }
        internal ZInput.ButtonDef Quick2 { get; }
        internal ZInput.ButtonDef Quick3 { get; }
        internal ZInput.ButtonDef Sort { get; }
        internal ZInput.ButtonDef ToggleLock { get; }
        internal string ReasonCode { get; }
        internal bool LegacyQuick1Mapped { get; }
        internal int ValidRouteCount { get; }
        internal bool Ready => Modifier != null && ValidRouteCount > 0;
        internal bool AllValid => Modifier != null && ValidRouteCount == ControllerBindingPolicy.RouteCount;

        internal const int DefinitionCount = 6;

        internal ZInput.ButtonDef Definition(int index)
        {
            switch (index)
            {
                case 0: return Modifier;
                case 1: return Quick1;
                case 2: return Quick2;
                case 3: return Quick3;
                case 4: return Sort;
                case 5: return ToggleLock;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

    }

    internal static class ControllerBindings
    {
        private static ZInput _instance;
        private static ControllerBindingState _cached;

        internal static ControllerBindingState Resolve()
        {
            if (_cached != null && ReferenceEquals(_instance, ZInput.instance)) return _cached;
            string modifier = Name(InventoryConfig.ControllerModifier?.Value);
            string quick1 = Name(InventoryConfig.ControllerQuick1?.Value);
            string quick2 = Name(InventoryConfig.ControllerQuick2?.Value);
            string quick3 = Name(InventoryConfig.ControllerQuick3?.Value);
            string sort = Name(InventoryConfig.ControllerSort?.Value);
            string toggleLock = Name(InventoryConfig.ControllerToggleLock?.Value);
            quick1 = ControllerBindingPolicy.EffectiveQuick1Action(
                modifier, quick1, quick2, quick3, sort, toggleLock, out bool legacyQuick1Mapped);
            _instance = ZInput.instance;
            if (_instance == null)
                return _cached = new ControllerBindingState(
                    null, null, null, null, null, null,
                    "controller.zinput-unavailable", legacyQuick1Mapped);

            ZInput.ButtonDef modifierDef = ResolveAction(modifier, out string modifierPath);
            string[] primaryActions = { quick1, quick2, quick3, sort, toggleLock };
            string[] primaryPaths = new string[ControllerBindingPolicy.RouteCount];
            ZInput.ButtonDef[] primaryDefinitions = new ZInput.ButtonDef[ControllerBindingPolicy.RouteCount];
            for (int index = 0; index < ControllerBindingPolicy.RouteCount; index++)
                primaryDefinitions[index] = ResolveAction(primaryActions[index], out primaryPaths[index]);

            int validMask = ControllerBindingPolicy.ValidRouteMask(
                modifier, modifierPath, primaryActions, primaryPaths);
            for (int index = 0; index < ControllerBindingPolicy.RouteCount; index++)
                if (!ControllerBindingPolicy.RouteIsValid(validMask, index)) primaryDefinitions[index] = null;
            string reason = modifierDef == null
                ? "controller.modifier-invalid"
                : validMask == ControllerBindingPolicy.AllRoutesMask
                    ? "ok"
                    : validMask == 0
                        ? "controller.no-valid-routes"
                        : "controller.one-or-more-routes-invalid";
            return _cached = new ControllerBindingState(
                modifierDef,
                primaryDefinitions[0], primaryDefinitions[1], primaryDefinitions[2],
                primaryDefinitions[3], primaryDefinitions[4], reason, legacyQuick1Mapped);
        }

        internal static void Invalidate()
        {
            _instance = null;
            _cached = null;
        }

        internal static bool ShouldReserveAction(string action)
        {
            if (!(InventoryConfig.ControllerEnabled?.Value ?? false) ||
                string.IsNullOrEmpty(action) || ZInput.instance == null) return false;
            ControllerBindingState state = Resolve();
            if (!state.Ready || !state.Modifier.Held ||
                !(state.Quick1?.Pressed == true || state.Quick2?.Pressed == true ||
                  state.Quick3?.Pressed == true || state.Sort?.Pressed == true ||
                  state.ToggleLock?.Pressed == true)) return false;
            ZInput.ButtonDef candidate;
            try { candidate = ZInput.instance.GetButtonDef(action); }
            catch (Exception) { return false; }
            if (candidate == null || candidate.Source != ZInput.InputSource.Gamepad) return false;
            string path;
            try { path = candidate.GetActionPath(); }
            catch (Exception) { return false; }
            return SamePath(path, state.Modifier) || SamePath(path, state.Quick1) || SamePath(path, state.Quick2) ||
                   SamePath(path, state.Quick3) || SamePath(path, state.Sort) || SamePath(path, state.ToggleLock);
        }

        private static ZInput.ButtonDef ResolveAction(string action, out string path)
        {
            path = string.Empty;
            if (action.Length == 0 || action.Length > 64 || !action.StartsWith("Joy", StringComparison.Ordinal)) return null;
            ZInput.ButtonDef definition;
            try { definition = ZInput.instance.GetButtonDef(action); }
            catch (Exception) { return null; }
            if (definition == null || definition.Source != ZInput.InputSource.Gamepad) return null;
            try { path = definition.GetActionPath()?.Trim() ?? string.Empty; }
            catch (Exception) { return null; }
            return path.Length == 0 ? null : definition;
        }

        private static string Name(string value)
        {
            string raw = value ?? string.Empty;
            if (raw.Length == 0 || raw.Length > 64) return string.Empty;
            string action = raw.Trim();
            for (int index = 0; index < action.Length; index++)
                if (!(char.IsLetterOrDigit(action[index]) || action[index] == '_' || action[index] == '-'))
                    return string.Empty;
            return action;
        }

        private static bool SamePath(string path, ZInput.ButtonDef definition)
        {
            if (string.IsNullOrEmpty(path) || definition == null) return false;
            try { return string.Equals(path, definition.GetActionPath(), StringComparison.Ordinal); }
            catch (Exception) { return false; }
        }
    }

    internal static class KeyboardInput
    {
        private static readonly KeyCode[] ModifierKeys =
        {
            KeyCode.LeftAlt, KeyCode.RightAlt, KeyCode.LeftControl, KeyCode.RightControl,
            KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftCommand, KeyCode.RightCommand
        };

        internal static bool ShortcutDown(BepInEx.Configuration.KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None || !ZInput.GetKeyDown(shortcut.MainKey, false)) return false;
            int allowedMask = 0;
            int count = 0;
            foreach (KeyCode allowed in shortcut.Modifiers)
            {
                if (++count > 8) return false;
                if (allowed == KeyCode.None || !ZInput.GetKey(allowed, false)) return false;
                int modifierIndex = ModifierIndex(allowed);
                if (modifierIndex >= 0) allowedMask |= 1 << modifierIndex;
            }
            for (int index = 0; index < ModifierKeys.Length; index++)
                if ((allowedMask & (1 << index)) == 0 && ZInput.GetKey(ModifierKeys[index], false)) return false;
            return true;
        }

        internal static bool ShouldReserveVanillaAction(string action)
        {
            KeyCode key = action switch
            {
                "Hotbar1" => KeyCode.Alpha1,
                "Hotbar2" => KeyCode.Alpha2,
                "Hotbar3" => KeyCode.Alpha3,
                "Hotbar4" => KeyCode.Alpha4,
                "Hotbar5" => KeyCode.Alpha5,
                "Hotbar6" => KeyCode.Alpha6,
                "Hotbar7" => KeyCode.Alpha7,
                "Hotbar8" => KeyCode.Alpha8,
                _ => KeyCode.None
            };
            if (key == KeyCode.None) return false;
            return IsChordFor(key, InventoryConfig.Quick1.Value) ||
                   IsChordFor(key, InventoryConfig.Quick2.Value) ||
                   IsChordFor(key, InventoryConfig.Quick3.Value);
        }

        private static bool IsChordFor(KeyCode key, BepInEx.Configuration.KeyboardShortcut shortcut) =>
            shortcut.MainKey == key && ShortcutDown(shortcut);

        private static int ModifierIndex(KeyCode value)
        {
            for (int index = 0; index < ModifierKeys.Length; index++)
                if (ModifierKeys[index] == value) return index;
            return -1;
        }
    }

    internal static class InputReservation
    {
        internal static bool ShouldSuppress(string action)
        {
            InventoryRuntime runtime = Plugin.Instance?.Runtime;
            if (!Plugin.RuntimeReady || runtime == null || !runtime.AcceptsInput) return false;
            return ControllerChordSession.ShouldSuppress(action) ||
                   ControllerBindings.ShouldReserveAction(action) ||
                   KeyboardInput.ShouldReserveVanillaAction(action);
        }
    }

    internal static class ControllerInput
    {
        private static ControllerBindingState _loggedBindings;

        internal static void Tick(InventoryRuntime runtime, bool inventoryVisible, bool gameplayInput)
        {
            if (runtime == null || !(InventoryConfig.ControllerEnabled?.Value ?? false) || ZInput.instance == null) return;
            ControllerBindingState bindings = ControllerBindings.Resolve();
            if (!ReferenceEquals(_loggedBindings, bindings))
            {
                _loggedBindings = bindings;
                if (bindings.AllValid)
                    Diagnostics.Info(bindings.LegacyQuick1Mapped
                        ? "Inventory controller chords validated by six unique effective gamepad paths; the exact legacy default Quick 1 route is using JoyMap."
                        : "Inventory controller chords validated by six unique effective gamepad paths.");
                else if (bindings.Ready)
                    Diagnostics.Warn("Inventory controller chords partially available (" +
                                     bindings.ValidRouteCount + "/5 routes): " + bindings.ReasonCode + ".");
                else
                    Diagnostics.Warn("Inventory controller chords disabled: " + bindings.ReasonCode + ".");
            }
            bool sessionActive = ControllerChordSession.Active;
            if (!bindings.Ready || sessionActive || !bindings.Modifier.Held) return;

            if (inventoryVisible && bindings.ToggleLock?.Pressed == true)
            {
                ControllerChordSession.Begin(bindings);
                runtime.ToggleFocusedLock("controller");
                return;
            }
            if (inventoryVisible && bindings.Sort?.Pressed == true)
            {
                ControllerChordSession.Begin(bindings);
                runtime.SortSelectedRows("controller");
                return;
            }
            if (!gameplayInput) return;
            if (bindings.Quick1?.Pressed == true)
            {
                ControllerChordSession.Begin(bindings);
                runtime.UseQuick(Api.InventoryRoleKind.Quick1, "controller");
            }
            else if (bindings.Quick2?.Pressed == true)
            {
                ControllerChordSession.Begin(bindings);
                runtime.UseQuick(Api.InventoryRoleKind.Quick2, "controller");
            }
            else if (bindings.Quick3?.Pressed == true)
            {
                ControllerChordSession.Begin(bindings);
                runtime.UseQuick(Api.InventoryRoleKind.Quick3, "controller");
            }
        }

        internal static void ResetLog() => _loggedBindings = null;
    }

    internal static class ControllerChordSession
    {
        private static ControllerBindingState _bindings;
        private static readonly HashSet<string> Paths = new HashSet<string>(StringComparer.Ordinal);
        private static int _releasedFrame = -1;

        internal static bool Active
        {
            get
            {
                UpdateRelease();
                return _bindings != null;
            }
        }

        internal static void Begin(ControllerBindingState bindings)
        {
            if (bindings == null || !bindings.Ready || _bindings != null) return;
            _bindings = bindings;
            Paths.Clear();
            for (int index = 0; index < ControllerBindingState.DefinitionCount; index++)
            {
                ZInput.ButtonDef definition = bindings.Definition(index);
                string path = definition?.GetActionPath();
                if (!string.IsNullOrEmpty(path)) Paths.Add(path);
            }
            _releasedFrame = -1;
        }

        internal static bool ShouldSuppress(string action)
        {
            UpdateRelease();
            if (_bindings == null || ZInput.instance == null || string.IsNullOrEmpty(action)) return false;
            ZInput.ButtonDef definition;
            try { definition = ZInput.instance.GetButtonDef(action); }
            catch (Exception) { return false; }
            if (definition == null || definition.Source != ZInput.InputSource.Gamepad) return false;
            string path;
            try { path = definition.GetActionPath(); }
            catch (Exception) { return false; }
            return !string.IsNullOrEmpty(path) && Paths.Contains(path);
        }

        internal static void Reset()
        {
            _bindings = null;
            Paths.Clear();
            _releasedFrame = -1;
            ControllerBindings.Invalidate();
            ControllerInput.ResetLog();
        }

        private static void UpdateRelease()
        {
            if (_bindings == null) return;
            bool held = false;
            for (int index = 0; index < ControllerBindingState.DefinitionCount; index++)
                if (_bindings.Definition(index)?.Held == true) { held = true; break; }
            if (held)
            {
                _releasedFrame = -1;
                return;
            }
            if (_releasedFrame < 0)
            {
                _releasedFrame = Time.frameCount;
                return;
            }
            if (_releasedFrame == Time.frameCount) return;
            _bindings = null;
            Paths.Clear();
            _releasedFrame = -1;
        }
    }
}
