using System;

namespace Runic.Foundation.Persistence
{
    public sealed class PersistentRecordHeader
    {
        public PersistentRecordHeader(
            string moduleId,
            int schemaVersion,
            string ownerStableId,
            string lastEditorStableId,
            long lastModifiedUtcTicks)
        {
            if (string.IsNullOrWhiteSpace(moduleId))
            {
                throw new ArgumentException("Module ID is required.", nameof(moduleId));
            }

            if (schemaVersion < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            }

            ModuleId = moduleId.Trim();
            SchemaVersion = schemaVersion;
            OwnerStableId = ownerStableId ?? string.Empty;
            LastEditorStableId = lastEditorStableId ?? string.Empty;
            LastModifiedUtcTicks = lastModifiedUtcTicks;
        }

        public string ModuleId { get; }

        public int SchemaVersion { get; }

        public string OwnerStableId { get; }

        public string LastEditorStableId { get; }

        public long LastModifiedUtcTicks { get; }
    }
}
