using System;

namespace RunicStorage.Engine
{
    /// <summary>Exact effective controller paths owned by an accepted Storage modifier session.</summary>
    internal readonly struct ControllerSessionPaths
    {
        internal ControllerSessionPaths(
            string modifier,
            string quickStack,
            string restock,
            string sort,
            string consolidate,
            string search)
        {
            Modifier = modifier ?? string.Empty;
            QuickStack = quickStack ?? string.Empty;
            Restock = restock ?? string.Empty;
            Sort = sort ?? string.Empty;
            Consolidate = consolidate ?? string.Empty;
            Search = search ?? string.Empty;
        }

        internal string Modifier { get; }
        internal string QuickStack { get; }
        internal string Restock { get; }
        internal string Sort { get; }
        internal string Consolidate { get; }
        internal string Search { get; }

        internal bool Contains(string path) =>
            Same(path, Modifier) ||
            Same(path, QuickStack) ||
            Same(path, Restock) ||
            Same(path, Sort) ||
            Same(path, Consolidate) ||
            Same(path, Search);

        private static bool Same(string left, string right) =>
            !string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right) &&
            string.Equals(left, right, StringComparison.Ordinal);
    }
}
