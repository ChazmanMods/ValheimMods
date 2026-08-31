using System;

namespace RunicAgriculture.Core
{
    public static class AgricultureReasonCodes
    {
        public const string Valid = "agriculture.valid";
        public const string Disabled = "agriculture.disabled";
        public const string NotAuthoritative = "agriculture.not-authoritative";
        public const string CropChanged = "agriculture.crop-changed";
        public const string TerrainUnavailable = "agriculture.terrain-unavailable";
        public const string WaterBlocked = "agriculture.water-blocked";
        public const string SlopeInvalid = "agriculture.slope-invalid";
        public const string BiomeInvalid = "agriculture.biome-invalid";
        public const string NotCultivated = "agriculture.not-cultivated";
        public const string SpacingBlocked = "agriculture.spacing-blocked";
        public const string OutOfRange = "agriculture.out-of-range";
        public const string WardDenied = "agriculture.ward-denied";
        public const string NoBuildZone = "agriculture.no-build-zone";
        public const string PlayerBlocked = "agriculture.player-blocked";
        public const string NoSeeds = "agriculture.no-seeds";
        public const string NoDurability = "agriculture.no-durability";
        public const string NoStamina = "agriculture.no-stamina";
        public const string InvalidBatchBlocked = "agriculture.invalid-batch-blocked";
        public const string CostBatchBlocked = "agriculture.cost-batch-blocked";
        public const string ReplantNotOffered = "agriculture.replant-not-offered";
        public const string ReplantCropMismatch = "agriculture.replant-crop-mismatch";
        public const string PlacementFailed = "agriculture.placement-failed";
    }

    public readonly struct PlacementValidationInputs
    {
        public PlacementValidationInputs(
            bool terrainAvailable,
            bool slopeValid,
            bool biomeValid,
            bool cultivated,
            bool spacingClear,
            bool inRange,
            bool authorized,
            bool worldBuildAllowed,
            bool playersClear,
            bool waterClear = true)
        {
            TerrainAvailable = terrainAvailable;
            SlopeValid = slopeValid;
            BiomeValid = biomeValid;
            Cultivated = cultivated;
            SpacingClear = spacingClear;
            InRange = inRange;
            Authorized = authorized;
            WorldBuildAllowed = worldBuildAllowed;
            PlayersClear = playersClear;
            WaterClear = waterClear;
        }

        public bool TerrainAvailable { get; }
        public bool SlopeValid { get; }
        public bool BiomeValid { get; }
        public bool Cultivated { get; }
        public bool SpacingClear { get; }
        public bool InRange { get; }
        public bool Authorized { get; }
        public bool WorldBuildAllowed { get; }
        public bool PlayersClear { get; }
        public bool WaterClear { get; }
    }

    public readonly struct PlacementValidationResult
    {
        public PlacementValidationResult(bool isValid, string reasonCode)
        {
            if (string.IsNullOrWhiteSpace(reasonCode))
                throw new ArgumentException("A stable reason code is required.", nameof(reasonCode));
            IsValid = isValid;
            ReasonCode = reasonCode;
        }

        public bool IsValid { get; }
        public string ReasonCode { get; }
    }

    public static class PlacementValidation
    {
        public static PlacementValidationResult Evaluate(PlacementValidationInputs inputs)
        {
            if (!inputs.TerrainAvailable) return Deny(AgricultureReasonCodes.TerrainUnavailable);
            if (!inputs.WaterClear) return Deny(AgricultureReasonCodes.WaterBlocked);
            if (!inputs.SlopeValid) return Deny(AgricultureReasonCodes.SlopeInvalid);
            if (!inputs.BiomeValid) return Deny(AgricultureReasonCodes.BiomeInvalid);
            if (!inputs.Cultivated) return Deny(AgricultureReasonCodes.NotCultivated);
            if (!inputs.SpacingClear) return Deny(AgricultureReasonCodes.SpacingBlocked);
            if (!inputs.InRange) return Deny(AgricultureReasonCodes.OutOfRange);
            if (!inputs.Authorized) return Deny(AgricultureReasonCodes.WardDenied);
            if (!inputs.WorldBuildAllowed) return Deny(AgricultureReasonCodes.NoBuildZone);
            if (!inputs.PlayersClear) return Deny(AgricultureReasonCodes.PlayerBlocked);
            return new PlacementValidationResult(true, AgricultureReasonCodes.Valid);
        }

        private static PlacementValidationResult Deny(string reason) =>
            new PlacementValidationResult(false, reason);
    }
}
