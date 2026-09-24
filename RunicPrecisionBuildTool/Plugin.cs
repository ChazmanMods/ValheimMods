using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using QuietBuildRotation.Integration;
using QuietBuildRotation.UI;
using Splatform;
using UnityEngine;

namespace QuietBuildRotation
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(
        CompatibilityGuard.PerfectPlacementGuid,
        BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicPrecisionBuildTool";
        public const string Name = "Runic Precision Build Tool";
        public const string Version = "2.0.7";

        internal static ManualLogSource Log { get; private set; }

        private Harmony _harmony;
        private string _lastConflictReport;

        private void Awake()
        {
            Log = Logger;
            Diagnostics.Initialize(Logger);
            PluginConfig.Bind(Config);
            PluginConfig.Changed += OnConfigurationChanged;
            ReportBindingConflicts();

            string compatibilityReason;
            try
            {
                if (!CompatibilityGuard.ShouldDisableForPerfectPlacement(out compatibilityReason))
                    compatibilityReason = null;
            }
            catch (Exception exception)
            {
                compatibilityReason =
                    $"PerfectPlacement compatibility state could not be verified: " +
                    $"{exception.GetType().Name}: {exception.Message}";
            }

            if (!string.IsNullOrEmpty(compatibilityReason))
            {
                Diagnostics.DisableForCompatibility(compatibilityReason);
                Logger.LogInfo($"{Name} v{Version} loaded with Runic manipulation disabled.");
                return;
            }

            if (!PlacementAdapter.Initialize(out string adapterError))
            {
                Diagnostics.DisableAdapter(adapterError);
                Logger.LogInfo($"{Name} v{Version} loaded with Runic manipulation disabled.");
                return;
            }

            _harmony = new Harmony(Guid);
            try
            {
                _harmony.PatchAll(typeof(Plugin).Assembly);
            }
            catch (Exception exception)
            {
                Diagnostics.DisableAdapter(
                    $"Harmony patching failed: {exception.GetType().Name}: {exception.Message}");
                UnpatchFailedStartup();
                Logger.LogInfo($"{Name} v{Version} loaded with Runic manipulation disabled.");
                return;
            }

            if (!PlacementAdapter.CompleteRuntimeVerification(Guid, out string verificationError))
            {
                Diagnostics.DisableAdapter(verificationError);
                UnpatchFailedStartup();
                Logger.LogInfo($"{Name} v{Version} loaded with Runic manipulation disabled.");
                return;
            }

            try
            {
                PlacementRuntime.Initialize();
            }
            catch (Exception exception)
            {
                PlacementRuntime.Shutdown();
                Diagnostics.DisableAdapter(
                    $"runtime initialization failed: {exception.GetType().Name}: {exception.Message}");
                UnpatchFailedStartup();
                Logger.LogInfo($"{Name} v{Version} loaded with Runic manipulation disabled.");
                return;
            }
            Diagnostics.MarkRuntimeAvailable();
            try
            {
                if (Hud.instance != null)
                    OrientationPresenter.Attach(Hud.instance);
            }
            catch (Exception exception)
            {
                RunicHookGuard.Disable("existing selected-piece panel attachment", exception);
            }

            if (!Diagnostics.RuntimeAvailable)
            {
                PlacementRuntime.Shutdown();
                UnpatchFailedStartup();
                Logger.LogInfo($"{Name} v{Version} loaded with Runic manipulation disabled.");
                return;
            }
            Logger.LogInfo(
                $"{Name} v{Version} ready on Valheim {Diagnostics.GetValheimVersion()}. " +
                "Runic wheel and movement actions use explicit configurable chords and fail closed.");
        }

        private void OnApplicationFocus(bool hasFocus) =>
            PlacementRuntime.OnApplicationFocus(hasFocus);

        private void Update()
        {
            try
            {
                PlacementRuntime.UpdateUtilities();
            }
            catch (Exception exception)
            {
                RunicHookGuard.Disable("bounded utility input", exception);
            }
        }

        private void OnDestroy()
        {
            PluginConfig.Changed -= OnConfigurationChanged;
            PlacementRuntime.Shutdown();
            OrientationPresenter.Destroy();
            _harmony?.UnpatchSelf();
        }

        private void OnConfigurationChanged()
        {
            ReportBindingConflicts();
            PlacementRuntime.OnConfigurationChanged();
        }

        private void ReportBindingConflicts()
        {
            string conflicts = PluginConfig.FindExactChordConflicts();
            if (string.IsNullOrEmpty(conflicts) || string.Equals(conflicts, _lastConflictReport, StringComparison.Ordinal))
                return;

            _lastConflictReport = conflicts;
            Logger.LogWarning(
                $"Exact Runic input chord conflict detected: {conflicts}. " +
                "Bindings were preserved; the conflicting chord will not be guessed or remapped.");
        }

        private void UnpatchFailedStartup()
        {
            if (_harmony == null) return;
            try
            {
                _harmony.UnpatchSelf();
            }
            catch (Exception exception)
            {
                Logger.LogWarning(
                    $"Runic startup cleanup could not remove every partial patch; all remaining " +
                    $"hooks stay disabled: {exception.GetType().Name}: {exception.Message}");
            }
            _harmony = null;
        }
    }

    [HarmonyPatch(typeof(Player), "HandleRadialInput")]
    internal static class PlayerHandleRadialInputPatch
    {
        private static bool Prefix(Player __instance)
        {
            try
            {
                return !PlacementRuntime.ShouldSuppressAxisGuideRadial(__instance);
            }
            catch (Exception exception)
            {
                // Any uncertainty preserves Valheim's original action.
                RunicHookGuard.Disable("radial input guard", exception);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacement")]
    internal static class PlayerUpdatePlacementPatch
    {
        [HarmonyPriority(Priority.Normal)]
        [HarmonyAfter("chazman.RunicBuildCamera")]
        [HarmonyBefore("chazman.RunicCrafting")]
        private static void Prefix(Player __instance, bool takeInput, float dt)
        {
            try
            {
                PlacementRuntime.BeforePlacementInput(__instance, takeInput, dt);
            }
            catch (Exception exception)
            {
                RunicHookGuard.Disable("UpdatePlacement prefix", exception);
            }
        }

        private static void Postfix(Player __instance, bool takeInput, float dt)
        {
            try
            {
                PlacementRuntime.AfterPlacementInput(__instance, takeInput, dt);
            }
            catch (Exception exception)
            {
                RunicHookGuard.Disable("UpdatePlacement postfix", exception);
            }
        }

        private static Exception Finalizer(Player __instance, Exception __exception)
        {
            if (__exception == null) return null;
            try
            {
                PlacementRuntime.AbortPlacementInput(__instance);
            }
            catch (Exception cleanupException)
            {
                RunicHookGuard.Disable("UpdatePlacement exception cleanup", cleanupException);
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Player), "SetupPlacementGhost")]
    internal static class PlayerSetupPlacementGhostPatch
    {
        private static void Postfix(Player __instance)
        {
            try
            {
                PlacementRuntime.OnPlacementGhostSetup(__instance);
            }
            catch (Exception exception)
            {
                RunicHookGuard.Disable("SetupPlacementGhost postfix", exception);
            }
        }
    }

    [HarmonyPatch(typeof(Player), "FindClosestSnapPoints")]
    internal static class PlayerFindClosestSnapPointsPatch
    {
        private static void Postfix(
            Player __instance,
            [HarmonyArgument("ghost")] Transform searchRoot,
            bool __result,
            [HarmonyArgument("a")] Transform ghostSnapPoint,
            [HarmonyArgument("b")] Transform targetSnapPoint)
        {
            try
            {
                PlacementRuntime.ObserveSnapSearch(
                    __instance,
                    searchRoot,
                    __result,
                    ghostSnapPoint,
                    targetSnapPoint);
            }
            catch (Exception exception)
            {
                RunicHookGuard.Disable("FindClosestSnapPoints postfix", exception);
            }
        }
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    internal static class PlayerUpdatePlacementGhostPatch
    {
        [HarmonyPriority(Priority.Normal)]
        [HarmonyAfter("chazman.RunicBuildCamera")]
        private static void Prefix(Player __instance)
        {
            try
            {
                PlacementRuntime.BeforePlacementGhost(__instance);
            }
            catch (Exception exception)
            {
                RunicHookGuard.Disable("UpdatePlacementGhost prefix", exception);
            }
        }

        [HarmonyPriority(Priority.Normal)]
        [HarmonyBefore("chazman.RunicAgriculture")]
        private static void Postfix(Player __instance)
        {
            try
            {
                PlacementRuntime.AfterPlacementGhost(__instance);
            }
            catch (Exception exception)
            {
                RunicHookGuard.Disable("UpdatePlacementGhost postfix", exception);
            }
        }

        private static Exception Finalizer(Exception __exception)
        {
            if (__exception == null) return null;
            try
            {
                PlacementRuntime.AbortPlacementGhost();
            }
            catch (Exception cleanupException)
            {
                RunicHookGuard.Disable("UpdatePlacementGhost exception cleanup", cleanupException);
            }
            return __exception;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            PlacementAdapter.TranspileUpdatePlacementGhost(instructions);
    }

    [HarmonyPatch(typeof(Hud), "UpdateBuild", new[] { typeof(Player), typeof(bool) })]
    internal static class HudUpdateBuildPrecisionInfoPatch
    {
        private static void Postfix(Hud __instance, Player player)
        {
            try
            {
                PlacementRuntime.AfterHudUpdateBuild(__instance, player);
            }
            catch (Exception exception)
            {
                // The presenter owns only its child rows. Valheim's build HUD and piece menu
                // remain active even if augmentation fails.
                RunicHookGuard.Disable("build information augmentation", exception);
            }
        }
    }

    [HarmonyPatch(
        typeof(Piece),
        nameof(Piece.SetCreator),
        new[] { typeof(long), typeof(PlatformUserID) })]
    internal static class PieceSetCreatorPlacementObserverPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Piece __instance, long uid)
        {
            try
            {
                PlacementRuntime.ObservePieceCreator(__instance, uid);
            }
            catch (Exception exception)
            {
                RunicHookGuard.Disable("placed-piece history observer", exception);
            }
        }
    }

    [HarmonyPatch(typeof(Hud), "Awake")]
    internal static class HudAwakePrecisionInfoPatch
    {
        private static void Postfix(Hud __instance)
        {
            try
            {
                OrientationPresenter.Attach(__instance);
            }
            catch (Exception exception)
            {
                RunicHookGuard.Disable("selected-piece panel attachment", exception);
            }
        }
    }

    internal static class RunicHookGuard
    {
        internal static void Disable(string hook, Exception exception)
        {
            Diagnostics.DisableAdapter(
                $"{hook} failed: {exception.GetType().Name}: {exception.Message}");
            try
            {
                AxisGuidePresenter.Hide();
                OrientationPresenter.Hide();
            }
            catch (Exception)
            {
                // The adapter is already disabled. Never replace the original hook failure with
                // a presentation cleanup failure.
            }
        }
    }
}
