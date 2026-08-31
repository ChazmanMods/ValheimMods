using System;

namespace RunicSafety.Api
{
    public enum ProtectionDestination
    {
        Obliterator = 0,
        SmelterInput = 1,
        SmelterFuel = 2,
        CookingStation = 3,
        CookingFuel = 4,
        Fermenter = 5,
        ItemStand = 6,
        UnsupportedDestructivePath = 7
    }

    public enum ItemLockState
    {
        NotApplicable = 0,
        Unlocked = 1,
        Locked = 2,
        Unknown = 3
    }

    public enum ProtectionOutcome
    {
        Allow = 0,
        RequireConfirmation = 1,
        Deny = 2
    }

    public enum ProtectionReason
    {
        None = 0,
        Equipped = 1,
        Locked = 2,
        QuestItem = 3,
        ConfiguredRareItem = 4,
        ProviderUnavailable = 5,
        ProviderFailure = 6,
        ExternalPolicy = 7,
        AdministratorBypass = 8,
        InvalidRequest = 9
    }

    public sealed class ProtectedItemDescriptor
    {
        public ProtectedItemDescriptor(
            string stableItemId,
            bool equipped,
            bool questItem,
            ItemLockState lockState,
            bool configuredRare)
        {
            StableItemId = stableItemId ?? string.Empty;
            Equipped = equipped;
            QuestItem = questItem;
            LockState = lockState;
            ConfiguredRare = configuredRare;
        }

        public string StableItemId { get; }
        public bool Equipped { get; }
        public bool QuestItem { get; }
        public ItemLockState LockState { get; }
        public bool ConfiguredRare { get; }
    }

    public sealed class ItemProtectionRequest
    {
        public ItemProtectionRequest(
            ProtectedItemDescriptor item,
            ProtectionDestination destination,
            string correlationId,
            bool administrator,
            bool externalInventoryCapabilityAdvertised,
            object nativeItemHandle = null)
        {
            Item = item;
            Destination = destination;
            CorrelationId = correlationId ?? string.Empty;
            Administrator = administrator;
            ExternalInventoryCapabilityAdvertised = externalInventoryCapabilityAdvertised;
            NativeItemHandle = nativeItemHandle;
        }

        public ProtectedItemDescriptor Item { get; }
        public ProtectionDestination Destination { get; }
        public string CorrelationId { get; }
        public bool Administrator { get; }
        public bool ExternalInventoryCapabilityAdvertised { get; }
        /// <summary>
        /// Ephemeral source item supplied only for the duration of Evaluate. Providers must not retain it.
        /// Safety never serializes or records this handle.
        /// </summary>
        public object NativeItemHandle { get; }
    }

    public sealed class ItemProtectionDecision
    {
        public ItemProtectionDecision(
            ProtectionOutcome outcome,
            ProtectionReason reason,
            string providerId = null)
        {
            Outcome = outcome;
            Reason = reason;
            ProviderId = providerId ?? string.Empty;
        }

        public ProtectionOutcome Outcome { get; }
        public ProtectionReason Reason { get; }
        public string ProviderId { get; }
        public bool MayTransfer => Outcome != ProtectionOutcome.Deny;
    }

    public interface IItemProtectionProvider
    {
        string ProviderId { get; }
        ItemProtectionDecision Evaluate(ItemProtectionRequest request);
    }

    public interface IProtectedItemPolicy
    {
        ItemProtectionDecision Evaluate(ItemProtectionRequest request);
        IDisposable RegisterProvider(IItemProtectionProvider provider, int priority = 0);
        bool HasExternalProvider { get; }
        int ProviderCount { get; }
    }
}
