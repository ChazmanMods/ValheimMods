using System;
using System.Collections.Generic;

namespace RunicProduction.Contracts
{
    public enum ProductionLinkRole
    {
        Input = 1,
        Fuel = 2,
        Output = 3,
        Replenishment = 4
    }

    public enum ProductionStopCode
    {
        Ready = 0,
        Disabled = 1,
        LinkMissing = 2,
        LinkTargetDestroyed = 3,
        LinkTargetMoved = 4,
        LinkOutOfRange = 5,
        OwnershipChanged = 6,
        WardChanged = 7,
        PermissionDenied = 8,
        StationCapacityFull = 9,
        InputUnavailable = 10,
        FuelReserveProtected = 11,
        OutputFull = 12,
        OutputInaccessible = 13,
        NativeOwnerRequired = 14,
        InventoryUnavailable = 15,
        InternalFailure = 16,
        LinkTargetUnavailable = 17,
        ReplenishmentPlanMissing = 18,
        ReplenishmentTargetMissing = 19,
        ProducerIneligible = 20,
        IngredientReserveProtected = 21,
        RecipeUnavailable = 22,
        StationUnusable = 23,
        RecordInvalid = 24
    }
}
