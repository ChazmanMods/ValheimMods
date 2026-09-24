using System;
using System.Globalization;
using BepInEx.Configuration;
using RunicCrafting.Domain;
using UnityEngine;

namespace RunicCrafting
{
    internal static class Configuration
    {
        internal static ConfigEntry<string> PullPrefabIds { get; private set; }
        internal static ConfigEntry<bool> Enabled { get; private set; }
        internal static ConfigEntry<bool> CraftFromContainers { get; private set; }
        internal static ConfigEntry<bool> CookFromContainers { get; private set; }
        internal static ConfigEntry<bool> RefuelFromContainers { get; private set; }
        internal static ConfigEntry<float> InteractionRangeMeters { get; private set; }
        internal static ConfigEntry<bool> BuildFromContainers { get; private set; }
        internal static ConfigEntry<bool> StationlessBuildFromContainers { get; private set; }
        internal static ConfigEntry<float> StationlessBuildRangeMeters { get; private set; }
        internal static ConfigEntry<string> StationlessPieceAllowList { get; private set; }
        internal static ConfigEntry<string> StationlessPieceDenyList { get; private set; }
        internal static ConfigEntry<bool> RepairAll { get; private set; }
        internal static ConfigEntry<bool> AreaRepairEnabled { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> AreaRepairKey { get; private set; }
        internal static ConfigEntry<float> AreaRepairRadius { get; private set; }
        internal static ConfigEntry<float> RangeCapMeters { get; private set; }
        internal static ConfigEntry<int> MaximumCandidateContainers { get; private set; }
        internal static ConfigEntry<int> MaximumReturnedContainers { get; private set; }
        internal static ConfigEntry<bool> ExcludePersonalContainers { get; private set; }
        internal static ConfigEntry<bool> RequireWardAccess { get; private set; }
        internal static ConfigEntry<WorkshopPolicyKind> DefaultStationUse { get; private set; }
        internal static ConfigEntry<WorkshopPolicyKind> DefaultLocalMaterialUse { get; private set; }
        internal static ConfigEntry<bool> ShowStatusMessages { get; private set; }
        internal static ConfigEntry<bool> DetailedLogging { get; private set; }
        internal static ConfigEntry<bool> LogCacheStats { get; private set; }
        internal static StationlessPiecePolicy StationlessPolicy { get; private set; }

        internal static void Bind(ConfigFile config)
        {
            PullPrefabIds = config.Bind("Modded Containers", "PullPrefabIds", "piece_drawer",
                global::Runic.Localization.RunicText.Get("text_77150f752767"));
            CookFromContainers = config.Bind("Manual Interactions", "CookFromContainers", true,
                global::Runic.Localization.RunicText.Get("text_e03cbb27645f"));
            RefuelFromContainers = config.Bind("Manual Interactions", "RefuelFromContainers", true,
                global::Runic.Localization.RunicText.Get("text_90375be5dc8f"));
            InteractionRangeMeters = config.Bind("Manual Interactions", "RangeMeters", 20f,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_4c4f2dd95083"),
                    new AcceptableValueRange<float>(1f, 50f)));
            Enabled = config.Bind("General", "Enabled", true,
                global::Runic.Localization.RunicText.Get("text_6bd156c93a9b"));
            CraftFromContainers = config.Bind("Materials", "CraftFromContainers", true,
                global::Runic.Localization.RunicText.Get("text_f4640532ec20"));
            BuildFromContainers = config.Bind("Materials", "BuildFromContainers", true,
                global::Runic.Localization.RunicText.Get("text_424db1887435"));
            StationlessBuildFromContainers = config.Bind("Stationless Building", "Enabled", true,
                global::Runic.Localization.RunicText.Get("text_91f098b85243"));
            StationlessBuildRangeMeters = config.Bind("Stationless Building", "PlayerLocalRangeMeters", 20f,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_3f7fe2b0efa5"),
                    new AcceptableValueRange<float>(1f, 50f)));
            StationlessPieceAllowList = config.Bind("Stationless Building", "AllowedPrefabIds", StationlessPiecePolicy.DefaultVanillaAllowList,
                global::Runic.Localization.RunicText.Get("text_cb4dd44b5b3e"));
            StationlessPieceDenyList = config.Bind("Stationless Building", "DeniedPrefabIds", string.Empty,
                global::Runic.Localization.RunicText.Get("text_5ade04c4a315"));
            RepairAll = config.Bind("Repair", "RepairAll", true,
                global::Runic.Localization.RunicText.Get("text_5952f0508429"));
            AreaRepairEnabled = config.Bind("Area Repair", "Enabled", true,
                global::Runic.Localization.RunicText.Get("text_384ceeb93847"));
            AreaRepairKey = config.Bind("Area Repair", "Hotkey", new KeyboardShortcut(KeyCode.Semicolon),
                global::Runic.Localization.RunicText.Get("text_d1a1107b1474"));
            AreaRepairRadius = config.Bind("Area Repair", "RadiusMeters", 50f,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_6475ce6958cb"),
                    new AcceptableValueRange<float>(1f, 100f)));
            RangeCapMeters = config.Bind("Materials", "RangeCapMeters", 20f,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_b9ad29db59e6"),
                    new AcceptableValueRange<float>(1f, 50f)));
            MaximumCandidateContainers = config.Bind("Materials", "MaximumCandidateContainers", 64,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_9290eb01abd5"),
                    new AcceptableValueRange<int>(1, 256)));
            MaximumReturnedContainers = config.Bind("Materials", "MaximumReturnedContainers", 32,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_d3516f50ae14"),
                    new AcceptableValueRange<int>(1, 128)));
            ExcludePersonalContainers = config.Bind("Materials", "ExcludePersonalContainers", true,
                global::Runic.Localization.RunicText.Get("text_6e9065fb59e1"));
            RequireWardAccess = config.Bind("Materials", "RequireWardAccess", false,
                global::Runic.Localization.RunicText.Get("text_94559666383d"));
            DefaultStationUse = config.Bind("Workshop Access", "DefaultStationUse", WorkshopPolicyKind.Everyone,
                global::Runic.Localization.RunicText.Get("text_870bfd90f3b8"));
            DefaultLocalMaterialUse = config.Bind("Workshop Access", "DefaultLocalMaterialUse", WorkshopPolicyKind.Everyone,
                global::Runic.Localization.RunicText.Get("text_67c02e8de4e4"));
            ShowStatusMessages = config.Bind("Diagnostics", "ShowStatusMessages", true,
                global::Runic.Localization.RunicText.Get("text_4f7b727993b5"));
            DetailedLogging = config.Bind("Diagnostics", "DetailedLogging", false,
                global::Runic.Localization.RunicText.Get("text_1bdd0a38f5d9"));
            LogCacheStats = config.Bind("Diagnostics", "LogCacheStats", false,
                global::Runic.Localization.RunicText.Get("text_14a24eb3ec32"));
            RefreshDerivedSettings();
        }

        internal static float SafeRangeCap => Math.Max(1f, Math.Min(50f, RangeCapMeters.Value));
        internal static float SafeInteractionRange => Math.Max(1f, Math.Min(SafeRangeCap, InteractionRangeMeters.Value));
        internal static int SafeMaximumCandidates => Math.Max(1, Math.Min(256, MaximumCandidateContainers.Value));
        internal static int SafeMaximumReturned =>
            Math.Max(1, Math.Min(Math.Min(128, SafeMaximumCandidates), MaximumReturnedContainers.Value));
        internal static float SafeStationlessBuildRange =>
            Math.Max(1f, Math.Min(SafeRangeCap, StationlessBuildRangeMeters.Value));

        internal static void RefreshDerivedSettings() =>
            StationlessPolicy = StationlessPiecePolicy.Parse(
                StationlessPieceAllowList?.Value,
                StationlessPieceDenyList?.Value);

        internal static string StateSummary =>
            "master=" + OnOff(Enabled.Value) +
            "; nearby crafting=" + OnOff(CraftFromContainers.Value) +
            "; nearby cooking=" + OnOff(CookFromContainers.Value) +
            "; nearby refueling=" + OnOff(RefuelFromContainers.Value) +
            "; nearby building=" + OnOff(BuildFromContainers.Value) +
            "; stationless building=" + OnOff(StationlessBuildFromContainers.Value) +
            " (player-local " + SafeStationlessBuildRange.ToString("0.#", CultureInfo.InvariantCulture) + "m)" +
            "; stationless allow=" + (StationlessPolicy.AllowsWildcard
                ? "*"
                : StationlessPolicy.AllowedRuleCount.ToString(CultureInfo.InvariantCulture)) +
            "; stationless deny=" + StationlessPolicy.DeniedRuleCount.ToString(CultureInfo.InvariantCulture) +
            "; Repair All=" + OnOff(RepairAll.Value) +
            "; area repair=" + OnOff(AreaRepairEnabled.Value) + " (" + AreaRepairKey.Value + ")" +
            "; station HUD=" + OnOff(ShowStatusMessages.Value) +
            "; range cap=" + SafeRangeCap.ToString("0.#", CultureInfo.InvariantCulture) + "m" +
            "; local-material default=" + DefaultLocalMaterialUse.Value +
            "; detailed logging=" + OnOff(DetailedLogging.Value) +
            "; input=normal craft/build/repair controls; optional area-repair hotkey";

        internal static string HudSummary =>
            !Enabled.Value
                ? "Runic Crafting: gameplay disabled in configuration"
                : "Runic Crafting: nearby craft " + OnOff(CraftFromContainers.Value) +
                  ", nearby build " + OnOff(BuildFromContainers.Value) +
                  ", stationless build " + OnOff(StationlessBuildFromContainers.Value) +
                  ", Repair All " + OnOff(RepairAll.Value);

        private static string OnOff(bool value) => value ? global::Runic.Localization.RunicText.Get("text_e8a01133b135") : global::Runic.Localization.RunicText.Get("text_38cca6bea010");
    }
}
