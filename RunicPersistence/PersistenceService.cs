namespace Runic.Foundation.Persistence
{
    public interface IMigrationService
    {
        MigrationRegistration Register(MigrationStep step);

        MigrationResult Migrate(
            string moduleId,
            int targetVersion,
            IRecordStore store,
            IMigrationBackupSink backupSink);
    }

    public interface IProtocolNegotiationService
    {
        NegotiationResult Negotiate(
            ProtocolHello local,
            ProtocolHello remote,
            ProtocolRequirement[] requirements);
    }

    public sealed class PersistenceService : IMigrationService, IProtocolNegotiationService
    {
        public PersistenceService()
        {
            Migrations = new MigrationRegistry();
            Idempotency = new IdempotencyWindow();
        }

        public MigrationRegistry Migrations { get; }

        public IdempotencyWindow Idempotency { get; }

        public MigrationRegistration Register(MigrationStep step)
        {
            return Migrations.Register(step);
        }

        public MigrationResult Migrate(
            string moduleId,
            int targetVersion,
            IRecordStore store,
            IMigrationBackupSink backupSink)
        {
            return Migrations.Migrate(moduleId, targetVersion, store, backupSink);
        }

        public NegotiationResult Negotiate(ProtocolHello local, ProtocolHello remote, ProtocolRequirement[] requirements)
        {
            return ProtocolNegotiator.Negotiate(local, remote, requirements);
        }
    }
}
