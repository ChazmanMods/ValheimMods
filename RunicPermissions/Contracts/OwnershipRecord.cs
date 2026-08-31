namespace RunicPermissions.Contracts
{
    public enum RecordTrust
    {
        Current = 0,
        Missing = 1,
        Ambiguous = 2,
        Stale = 3
    }

    /// <summary>
    /// Canonical synchronized ownership metadata for a configurable world object.
    /// Transfers must create a new record with a higher revision on the server.
    /// </summary>
    public sealed class OwnershipRecord
    {
        public OwnershipRecord(
            StableIdentity owner,
            StableIdentity builder,
            int schemaVersion,
            long revision,
            RecordTrust trust = RecordTrust.Current,
            string ownerDisplayNameSnapshot = "",
            string builderDisplayNameSnapshot = "")
        {
            Owner = owner;
            Builder = builder;
            SchemaVersion = schemaVersion;
            Revision = revision;
            Trust = trust;
            OwnerDisplayNameSnapshot = ownerDisplayNameSnapshot ?? string.Empty;
            BuilderDisplayNameSnapshot = builderDisplayNameSnapshot ?? string.Empty;
        }

        public StableIdentity Owner { get; }

        public StableIdentity Builder { get; }

        public int SchemaVersion { get; }

        public long Revision { get; }

        public RecordTrust Trust { get; }

        public string OwnerDisplayNameSnapshot { get; }

        public string BuilderDisplayNameSnapshot { get; }

        public bool IsCurrent =>
            Trust == RecordTrust.Current && Owner != null && Builder != null &&
            SchemaVersion > 0 && Revision >= 0;
    }

    /// <summary>Stable synchronized keys shared by participating Runic modules.</summary>
    public static class PermissionStorageKeys
    {
        public const string SchemaVersion = "runic.permissions.schema";
        public const string OwnerAuthority = "runic.permissions.owner.authority";
        public const string OwnerSubject = "runic.permissions.owner.subject";
        public const string OwnerDisplaySnapshot = "runic.permissions.owner.display";
        public const string BuilderAuthority = "runic.permissions.builder.authority";
        public const string BuilderSubject = "runic.permissions.builder.subject";
        public const string BuilderDisplaySnapshot = "runic.permissions.builder.display";
        public const string OwnershipRevision = "runic.permissions.owner.revision";
        public const string ProfileId = "runic.permissions.profile";
        public const string ProfileRevision = "runic.permissions.profile.revision";
    }
}
