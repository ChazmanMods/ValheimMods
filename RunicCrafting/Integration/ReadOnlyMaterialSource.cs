using RunicCrafting.Domain;

namespace RunicCrafting.Integration
{
    // Preview data can never be passed off as authority to consume items.
    internal sealed class ReadOnlyMaterialSource : IMutableMaterialSource
    {
        private readonly MaterialSourceSnapshot _snapshot;
        internal ReadOnlyMaterialSource(MaterialSourceSnapshot snapshot) => _snapshot = snapshot;
        public string SourceId => _snapshot.SourceId;
        public MaterialSourceSnapshot Snapshot() => _snapshot;
        public bool TryTake(string resourceId, int quantity, out IMaterialRestoreToken restoreToken)
        {
            restoreToken = null;
            return false;
        }
    }
}
