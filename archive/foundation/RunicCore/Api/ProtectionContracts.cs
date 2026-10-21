namespace Runic.Foundation.Core
{
    /// <summary>Bounded protection result for one native inventory item.</summary>
    public enum ItemProtectionState
    {
        /// <summary>The provider governs this item but cannot prove a decisive state.</summary>
        Unknown = 0,
        /// <summary>The provider governs this exact item and proves it is unlocked.</summary>
        Unlocked = 1,
        /// <summary>The provider governs this exact item and proves it is protected.</summary>
        Locked = 2
    }

    /// <summary>
    /// Canonical optional query shared by inventory providers and protection consumers.
    /// Implementations must not retain the native object beyond this synchronous call.
    /// </summary>
    public interface IItemProtectionQuery
    {
        /// <summary>
        /// Returns false only when the supplied object is outside this provider's governed domain;
        /// the caller then applies its own independent policy and must never infer Unlocked. A true
        /// result with Unknown means the object is governed but its protection cannot be proven and
        /// destructive consumers must fail closed. True with Unlocked or Locked is decisive.
        /// </summary>
        bool TryGetProtection(object nativeItem, out ItemProtectionState state);
    }
}
