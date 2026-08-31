using HarmonyLib;
using RunicStorage.Engine;
using UnityEngine;

namespace RunicStorage.Runtime
{
    /// <summary>
    /// A recognized, context-authorized controller chord is captured from ZInput's raw ButtonDef
    /// state. An accepted chord owns a modifier session until the modifier and every configured
    /// primary are released; raw chained primary edges are re-routed inside that session. Vanilla
    /// aliases for the modifier and all configured primary paths return false during the session.
    /// A first blocked/invalid chord remains non-consuming, while a newly blocked chained edge is
    /// reported and consumed fail-closed because the session is already Storage-owned.
    /// </summary>
    internal static class StorageControllerCollisionGuard
    {
        private static StorageActionEdges _pendingEdge;
        private static ZInput.ButtonDef _latchedModifier;
        private static ControllerBindingState _sessionBindings;
        private static ControllerSessionPaths _sessionPaths;
        private static int _releasedFrame = -1;
        private static int _capturedFrame = -1;
        private static int _lastProbeFrame = -1;

        internal static StorageActionEdges ObserveAndConsume(
            ControllerBindingState bindings,
            StorageRouteContext context)
        {
            Observe(bindings, context);
            return ConsumePendingEdge();
        }

        internal static StorageActionEdges ConsumePendingEdge()
        {
            StorageActionEdges result = _pendingEdge;
            _pendingEdge = StorageActionEdges.None;
            return result;
        }

        internal static bool ShouldSuppress(string buttonName)
        {
            if (!(PluginConfig.Enabled?.Value ?? false)) return false;
            UpdateReleaseState();
            if ((PluginConfig.ControllerShortcuts?.Value ?? false) && ZInput.instance != null)
            {
                bool modifierHeld;
                if (_latchedModifier != null)
                {
                    modifierHeld = _latchedModifier.Held;
                }
                else
                {
                    // ZInput getters are a broad Valheim hot path. The ordinary case is a single
                    // dictionary lookup and raw held-state check; full route/config resolution and
                    // UI-context capture happen only while the configured modifier is held.
                    string modifierName = (PluginConfig.ControllerModifier?.Value ?? string.Empty).Trim();
                    ZInput.ButtonDef modifier = modifierName.Length == 0
                        ? null
                        : ZInput.instance.GetButtonDef(modifierName);
                    modifierHeld = modifier != null && modifier.Held;
                }
                if (modifierHeld && _lastProbeFrame != Time.frameCount)
                {
                    _lastProbeFrame = Time.frameCount;
                    Observe(
                        _sessionBindings ?? StorageControllerBindings.Resolve(),
                        Plugin.CaptureRouteContext());
                }
            }
            if (_latchedModifier == null || ZInput.instance == null) return false;
            ZInput.ButtonDef requested = ZInput.instance.GetButtonDef(buttonName);
            if (requested == null || requested.Source != ZInput.InputSource.Gamepad) return false;
            string requestedPath = requested.GetActionPath();
            return requestedPath != null && _sessionPaths.Contains(requestedPath);
        }

        internal static void Reset()
        {
            _pendingEdge = StorageActionEdges.None;
            _latchedModifier = null;
            _sessionBindings = null;
            _sessionPaths = default;
            _releasedFrame = -1;
            _capturedFrame = -1;
            _lastProbeFrame = -1;
        }

        private static void Observe(ControllerBindingState bindings, StorageRouteContext context)
        {
            UpdateReleaseState();
            if (bindings == null || !bindings.ModifierValid ||
                !bindings.Modifier.Held || _capturedFrame == Time.frameCount)
                return;

            StorageActionEdges edge = StorageActionEdges.None;
            if (bindings.SortValid && bindings.Sort.Pressed)
            {
                edge = StorageActionEdges.ControllerSort;
            }
            else if (bindings.QuickStackValid && bindings.QuickStack.Pressed)
            {
                edge = StorageActionEdges.ControllerQuickStack;
            }
            else if (bindings.RestockValid && bindings.Restock.Pressed)
            {
                edge = StorageActionEdges.ControllerRestock;
            }
            else if (bindings.ConsolidateValid && bindings.Consolidate.Pressed)
            {
                edge = StorageActionEdges.ControllerConsolidate;
            }
            else if (bindings.SearchValid && bindings.Search.Pressed)
            {
                edge = StorageActionEdges.ControllerSearch;
            }
            if (edge == StorageActionEdges.None) return;

            // Every recognized edge reaches Plugin.Update so blocked actions get an exact reason.
            _capturedFrame = Time.frameCount;
            _pendingEdge = edge;
            StorageActionRequest request = StorageActionRouter.Select(edge);
            StorageRouteDecision decision = StorageActionRouter.Route(request, context);
            bool sessionActive = _latchedModifier != null;
            ControllerChordDisposition disposition = ControllerChordSessionPolicy.Decide(sessionActive, decision);
            if (disposition == ControllerChordDisposition.ReportWithoutConsume) return;

            if (!sessionActive) StartSession(bindings);
            _releasedFrame = -1;
            if (PluginConfig.DebugTransfers.Value)
            {
                Plugin.Log?.LogInfo(
                    $"input-consumed source=controller action={StorageActionDiagnostics.ActionCode(request.Action)} " +
                    $"result={(decision.Outcome == StorageRouteOutcome.Execute ? "authorized" : "blocked-in-owned-session")} " +
                    "modifier-session=active configured-button-aliases=suppressed-until-full-release");
            }
        }

        private static void UpdateReleaseState()
        {
            if (_latchedModifier == null) return;
            if (_latchedModifier.Held || AnySessionPrimaryHeld())
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
            _latchedModifier = null;
            _sessionBindings = null;
            _sessionPaths = default;
            _releasedFrame = -1;
        }

        private static void StartSession(ControllerBindingState bindings)
        {
            _latchedModifier = bindings.Modifier;
            _sessionBindings = bindings;
            _sessionPaths = new ControllerSessionPaths(
                Path(bindings.Modifier),
                Path(bindings.QuickStack),
                Path(bindings.Restock),
                Path(bindings.Sort),
                Path(bindings.Consolidate),
                Path(bindings.Search));
        }

        private static bool AnySessionPrimaryHeld() =>
            IsHeld(_sessionBindings?.QuickStack) ||
            IsHeld(_sessionBindings?.Restock) ||
            IsHeld(_sessionBindings?.Sort) ||
            IsHeld(_sessionBindings?.Consolidate) ||
            IsHeld(_sessionBindings?.Search);

        private static bool IsHeld(ZInput.ButtonDef definition) => definition != null && definition.Held;

        private static string Path(ZInput.ButtonDef definition) =>
            definition?.GetActionPath() ?? string.Empty;
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButton), typeof(string))]
    internal static class StorageControllerGetButtonPatch
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("chazman.RunicAgriculture", "chazman.RunicInventory")]
        private static bool Prefix(string name, ref bool __result) => AllowOrConsume(name, ref __result);

        private static bool AllowOrConsume(string name, ref bool result)
        {
            if (StorageSearchGameplayInputGuard.ShouldSuppressPrimaryAttack(name))
            {
                result = false;
                return false;
            }
            if (!StorageControllerCollisionGuard.ShouldSuppress(name)) return true;
            result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown), typeof(string))]
    internal static class StorageControllerGetButtonDownPatch
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("chazman.RunicAgriculture", "chazman.RunicInventory")]
        private static bool Prefix(string name, ref bool __result)
        {
            if (StorageSearchGameplayInputGuard.ShouldSuppressPrimaryAttack(name))
            {
                __result = false;
                return false;
            }
            if (!StorageControllerCollisionGuard.ShouldSuppress(name)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonUp), typeof(string))]
    internal static class StorageControllerGetButtonUpPatch
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("chazman.RunicAgriculture", "chazman.RunicInventory")]
        private static bool Prefix(string name, ref bool __result)
        {
            if (StorageSearchGameplayInputGuard.ShouldSuppressPrimaryAttack(name))
            {
                __result = false;
                return false;
            }
            if (!StorageControllerCollisionGuard.ShouldSuppress(name)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonPressedTimer), typeof(string))]
    internal static class StorageControllerPressedTimerPatch
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("chazman.RunicAgriculture", "chazman.RunicInventory")]
        private static bool Prefix(string name, ref float __result)
        {
            if (StorageSearchGameplayInputGuard.ShouldSuppressPrimaryAttack(name))
            {
                __result = 0f;
                return false;
            }
            if (!StorageControllerCollisionGuard.ShouldSuppress(name)) return true;
            __result = 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonLastPressedTimer), typeof(string))]
    internal static class StorageControllerLastPressedTimerPatch
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("chazman.RunicAgriculture", "chazman.RunicInventory")]
        private static bool Prefix(string name, ref float __result)
        {
            if (StorageSearchGameplayInputGuard.ShouldSuppressPrimaryAttack(name))
            {
                __result = 0f;
                return false;
            }
            if (!StorageControllerCollisionGuard.ShouldSuppress(name)) return true;
            __result = 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetKeyDown), typeof(KeyCode), typeof(bool))]
    internal static class StorageSearchEscapeKeyPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(KeyCode key, ref bool __result)
        {
            if (!StorageSearchGameplayInputGuard.ShouldSuppressEscape(key)) return true;
            __result = false;
            return false;
        }
    }
}
