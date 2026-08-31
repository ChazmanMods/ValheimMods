using System;
using System.Globalization;
using BepInEx.Configuration;
using RunicCrafting.Domain;

namespace RunicCrafting
{
    internal static class Configuration
    {
        internal static ConfigEntry<bool> Enabled { get; private set; }
        internal static ConfigEntry<bool> CraftFromContainers { get; private set; }
        internal static ConfigEntry<bool> BuildFromContainers { get; private set; }
        internal static ConfigEntry<bool> StationlessBuildFromContainers { get; private set; }
        internal static ConfigEntry<float> StationlessBuildRangeMeters { get; private set; }
        internal static ConfigEntry<string> StationlessPieceAllowList { get; private set; }
        internal static ConfigEntry<string> StationlessPieceDenyList { get; private set; }
        internal static ConfigEntry<bool> RepairAll { get; private set; }
        internal static ConfigEntry<float> RangeCapMeters { get; private set; }
        internal static ConfigEntry<int> MaximumCandidateContainers { get; private set; }
        internal static ConfigEntry<int> MaximumReturnedContainers { get; private set; }
        internal static ConfigEntry<bool> ExcludePersonalContainers { get; private set; }
        internal static ConfigEntry<bool> RequireWardAccess { get; private set; }
        internal static ConfigEntry<WorkshopPolicyKind> DefaultStationUse { get; private set; }
        internal static ConfigEntry<WorkshopPolicyKind> DefaultLocalMaterialUse { get; private set; }
        internal static ConfigEntry<bool> ShowStatusMessages { get; private set; }
        internal static ConfigEntry<bool> DetailedLogging { get; private set; }
        internal static StationlessPiecePolicy StationlessPolicy { get; private set; }

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("General", "Enabled", true,
                "Master switch. False leaves crafting, building, and repair gameplay to vanilla while status UI may report that Runic Crafting is off.");
            CraftFromContainers = config.Bind("Materials", "CraftFromContainers", true,
                "Allow a player to satisfy ordinary recipe deficits from eligible nearby containers. Dedicated clients use the authenticated durable server path.");
            BuildFromContainers = config.Bind("Materials", "BuildFromContainers", true,
                "Allow build pieces to satisfy deficits from eligible nearby containers.");
            StationlessBuildFromContainers = config.Bind("Stationless Building", "Enabled", true,
                "Allow explicitly listed pieces that require no crafting station (for example fires, the basic cooking station, and the workbench itself) to use eligible containers around the player.");
            StationlessBuildRangeMeters = config.Bind("Stationless Building", "PlayerLocalRangeMeters", 20f,
                new ConfigDescription(
                    "Player-centered range for explicitly allowed stationless pieces. Materials.RangeCapMeters remains the global upper bound.",
                    new AcceptableValueRange<float>(1f, 50f)));
            StationlessPieceAllowList = config.Bind("Stationless Building", "AllowedPrefabIds", StationlessPiecePolicy.DefaultVanillaAllowList,
                "Comma/semicolon-separated exact Piece prefab IDs allowed to use the stationless path. Add modded prefabs here. Empty allows none; an explicit * allows all stationless prefabs.");
            StationlessPieceDenyList = config.Bind("Stationless Building", "DeniedPrefabIds", string.Empty,
                "Comma/semicolon-separated exact Piece prefab IDs denied from the stationless path. Deny rules win over allow rules; * denies all.");
            RepairAll = config.Bind("Repair", "RepairAll", true,
                "One repair-button press repairs every item that vanilla currently considers repairable.");
            RangeCapMeters = config.Bind("Materials", "RangeCapMeters", 20f,
                new ConfigDescription(
                    "Global hard cap applied after an active station's range or the configured stationless player-local range.",
                    new AcceptableValueRange<float>(1f, 50f)));
            MaximumCandidateContainers = config.Bind("Materials", "MaximumCandidateContainers", 64,
                new ConfigDescription(
                    "Maximum loaded containers considered by one event-driven query.",
                    new AcceptableValueRange<int>(1, 256)));
            MaximumReturnedContainers = config.Bind("Materials", "MaximumReturnedContainers", 32,
                new ConfigDescription(
                    "Maximum eligible containers returned to one material plan.",
                    new AcceptableValueRange<int>(1, 128)));
            ExcludePersonalContainers = config.Bind("Materials", "ExcludePersonalContainers", true,
                "Exclude Valheim personal/private container types from nearby material use.");
            RequireWardAccess = config.Bind("Materials", "RequireWardAccess", false,
                "Require ward access even for containers that do not normally check a guard stone. Native guard-stone checks are always honored.");
            DefaultStationUse = config.Bind("Workshop Access", "DefaultStationUse", WorkshopPolicyKind.Everyone,
                "Default independent policy for using a station.");
            DefaultLocalMaterialUse = config.Bind("Workshop Access", "DefaultLocalMaterialUse", WorkshopPolicyKind.Approved,
                "Default independent policy for consuming nearby materials through a station.");
            ShowStatusMessages = config.Bind("Diagnostics", "ShowStatusMessages", true,
                "Show a short controller-neutral HUD status when opening a station or changing a setting.");
            DetailedLogging = config.Bind("Diagnostics", "DetailedLogging", false,
                "Log attempts, every important early gate/no-op reason, bounded source plans, and exact denial codes. Repeated UI probes are rate-limited.");
            RefreshDerivedSettings();
        }

        internal static float SafeRangeCap => Math.Max(1f, Math.Min(50f, RangeCapMeters.Value));
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
            "; nearby building=" + OnOff(BuildFromContainers.Value) +
            "; stationless building=" + OnOff(StationlessBuildFromContainers.Value) +
            " (player-local " + SafeStationlessBuildRange.ToString("0.#", CultureInfo.InvariantCulture) + "m)" +
            "; stationless allow=" + (StationlessPolicy.AllowsWildcard
                ? "*"
                : StationlessPolicy.AllowedRuleCount.ToString(CultureInfo.InvariantCulture)) +
            "; stationless deny=" + StationlessPolicy.DeniedRuleCount.ToString(CultureInfo.InvariantCulture) +
            "; Repair All=" + OnOff(RepairAll.Value) +
            "; station HUD=" + OnOff(ShowStatusMessages.Value) +
            "; range cap=" + SafeRangeCap.ToString("0.#", CultureInfo.InvariantCulture) + "m" +
            "; local-material default=" + DefaultLocalMaterialUse.Value +
            "; detailed logging=" + OnOff(DetailedLogging.Value) +
            "; input=automatic (normal craft/build/repair controls, no hotkey)";

        internal static string HudSummary =>
            !Enabled.Value
                ? "Runic Crafting: gameplay disabled in configuration"
                : "Runic Crafting: nearby craft " + OnOff(CraftFromContainers.Value) +
                  ", nearby build " + OnOff(BuildFromContainers.Value) +
                  ", stationless build " + OnOff(StationlessBuildFromContainers.Value) +
                  ", Repair All " + OnOff(RepairAll.Value);

        private static string OnOff(bool value) => value ? "ON" : "OFF";
    }
}
