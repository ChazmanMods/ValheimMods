using System;

namespace RunicStorage.Engine
{
    /// <summary>
    /// Allocation-free identity for the resolved controller binding snapshot. ZInput identity is
    /// reference-based because a replacement instance owns a different ButtonDef table.
    /// </summary>
    internal readonly struct ControllerBindingCacheKey : IEquatable<ControllerBindingCacheKey>
    {
        internal ControllerBindingCacheKey(
            object zInputInstance,
            string modifier,
            string quickStack,
            string restock,
            string sort,
            string consolidate,
            string search)
        {
            ZInputInstance = zInputInstance;
            Modifier = modifier ?? string.Empty;
            QuickStack = quickStack ?? string.Empty;
            Restock = restock ?? string.Empty;
            Sort = sort ?? string.Empty;
            Consolidate = consolidate ?? string.Empty;
            Search = search ?? string.Empty;
        }

        internal object ZInputInstance { get; }
        internal string Modifier { get; }
        internal string QuickStack { get; }
        internal string Restock { get; }
        internal string Sort { get; }
        internal string Consolidate { get; }
        internal string Search { get; }

        public bool Equals(ControllerBindingCacheKey other) =>
            object.ReferenceEquals(ZInputInstance, other.ZInputInstance) &&
            string.Equals(Modifier, other.Modifier, StringComparison.Ordinal) &&
            string.Equals(QuickStack, other.QuickStack, StringComparison.Ordinal) &&
            string.Equals(Restock, other.Restock, StringComparison.Ordinal) &&
            string.Equals(Sort, other.Sort, StringComparison.Ordinal) &&
            string.Equals(Consolidate, other.Consolidate, StringComparison.Ordinal) &&
            string.Equals(Search, other.Search, StringComparison.Ordinal);

        public override bool Equals(object value) =>
            value is ControllerBindingCacheKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ZInputInstance == null
                    ? 0
                    : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(ZInputInstance);
                hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(Modifier);
                hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(QuickStack);
                hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(Restock);
                hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(Sort);
                hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(Consolidate);
                return hash * 397 ^ StringComparer.Ordinal.GetHashCode(Search);
            }
        }
    }
}
