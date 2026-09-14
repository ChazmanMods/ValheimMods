using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using Runic.Foundation.Core;
using Runic.Foundation.Persistence;
using RunicPermissions.Contracts;
using RunicPermissions.Evaluation;
using RunicTransactions.Coordination;

namespace RunicFoundation.Tests
{
    internal static class Program
    {
        private static int _passed;

        private static void Main()
        {
            Run("all production foundation assemblies load", ProductionAssembliesLoad);
            Run("four modules coexist in one registry", ModulesCoexist);
            Run("typed services resolve across package boundaries", ServicesResolve);
            Run("module disposal removes every owned service", DisposalRemovesServices);
            Run("protocol mismatch remains capability-local", ProtocolMismatchIsLocal);
            Run("migration service is discoverable and atomic", MigrationServiceIsAtomic);
            Run("builder archives superseded packages only during promotion", BuilderArchivalIsRecoverable);

            Console.WriteLine($"PASS: {_passed}/7 integrated foundation tests");
        }

        private static void ProductionAssembliesLoad()
        {
            Type corePlugin = typeof(Runic.Foundation.Core.Plugin);
            Type permissionsPlugin = typeof(RunicPermissions.Plugin);
            Type transactionsPlugin = typeof(RunicTransactions.Plugin);
            Type persistencePlugin = typeof(Runic.Foundation.Persistence.Plugin);
            string[] names =
            {
                typeof(RunicRegistry).Assembly.GetName().Name,
                typeof(IPermissionEvaluationService).Assembly.GetName().Name,
                typeof(ITransactionCoordinator).Assembly.GetName().Name,
                typeof(IMigrationService).Assembly.GetName().Name
            };

            Equal(4, names.Distinct(StringComparer.Ordinal).Count());
            True(names.Contains("RunicCore"), "RunicCore production assembly was not loaded.");
            True(names.Contains("RunicPermissions"), "RunicPermissions production assembly was not loaded.");
            True(names.Contains("RunicTransactions"), "RunicTransactions production assembly was not loaded.");
            True(names.Contains("RunicPersistence"), "RunicPersistence production assembly was not loaded.");

            AssertReleaseIdentity(
                corePlugin,
                "RunicCore",
                "Runic Core",
                "chazman.RunicCore",
                "1.1.0",
                new Version(1, 1, 0, 0));
            AssertReleaseIdentity(
                permissionsPlugin,
                "RunicPermissions",
                "Runic Permissions",
                "chazman.RunicPermissions",
                "1.0.0",
                new Version(1, 0, 0, 0));
            AssertReleaseIdentity(
                transactionsPlugin,
                "RunicTransactions",
                "Runic Transactions",
                "chazman.RunicTransactions",
                "1.0.0",
                new Version(1, 0, 0, 0));
            AssertReleaseIdentity(
                persistencePlugin,
                "RunicPersistence",
                "Runic Persistence",
                "chazman.RunicPersistence",
                "1.1.0",
                new Version(1, 1, 0, 0));

            Equal(0, corePlugin.GetCustomAttributes<BepInDependency>().Count());
            AssertCoreAndPersistenceDependencies(permissionsPlugin);
            AssertCoreAndPersistenceDependencies(transactionsPlugin);
            AssertCoreDependency(persistencePlugin);
        }

        private static void AssertReleaseIdentity(
            Type pluginType,
            string assemblyName,
            string productName,
            string pluginGuid,
            string releaseVersion,
            Version assemblyVersion)
        {
            Assembly assembly = pluginType.Assembly;
            Equal(assemblyName, assembly.GetName().Name);
            Equal(assemblyVersion, assembly.GetName().Version);
            Equal(
                releaseVersion + ".0",
                assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version);
            Equal(
                releaseVersion,
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
            Equal(
                productName,
                assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product);

            BepInPlugin plugin = pluginType.GetCustomAttribute<BepInPlugin>();
            True(plugin != null, assemblyName + " has no BepInPlugin release identity.");
            Equal(pluginGuid, plugin.GUID);
            Equal(productName, plugin.Name);
            Equal(releaseVersion, plugin.Version.ToString());
        }

        private static void AssertCoreDependency(Type pluginType)
        {
            BepInDependency[] dependencies = pluginType.GetCustomAttributes<BepInDependency>().ToArray();
            Equal(1, dependencies.Length);
            Equal("chazman.RunicCore", dependencies[0].DependencyGUID);
            Equal(new Version(1, 1, 0), dependencies[0].MinimumVersion);
            Equal(BepInDependency.DependencyFlags.HardDependency, dependencies[0].Flags);
        }

        private static void AssertCoreAndPersistenceDependencies(Type pluginType)
        {
            BepInDependency[] dependencies = pluginType.GetCustomAttributes<BepInDependency>()
                .OrderBy(value => value.DependencyGUID, StringComparer.Ordinal)
                .ToArray();
            Equal(2, dependencies.Length);
            Equal("chazman.RunicCore", dependencies[0].DependencyGUID);
            Equal(new Version(1, 0, 0), dependencies[0].MinimumVersion);
            Equal(BepInDependency.DependencyFlags.HardDependency, dependencies[0].Flags);
            Equal("chazman.RunicPersistence", dependencies[1].DependencyGUID);
            Equal(new Version(1, 0, 0), dependencies[1].MinimumVersion);
            Equal(BepInDependency.DependencyFlags.HardDependency, dependencies[1].Flags);
        }

        private static void ModulesCoexist()
        {
            var fixture = new RegistryFixture();
            try
            {
                Equal(4, fixture.Registry.GetModules().Count);
                string[] required =
                {
                    RunicCapabilityIds.PermissionsEvaluate,
                    RunicCapabilityIds.MaterialsReserve,
                    RunicCapabilityIds.MaterialsConsume,
                    RunicCapabilityIds.PersistenceMigrate,
                    RunicCapabilityIds.NetworkProtocol
                };
                foreach (string capability in required)
                    True(fixture.Registry.IsCapabilityAvailable(capability), capability + " is unavailable.");
                True(!fixture.Registry.IsCapabilityAvailable(RunicCapabilityIds.ContainerQuery),
                    "Transactions advertised an unimplemented container query provider.");
                True(!fixture.Registry.IsCapabilityAvailable(RunicCapabilityIds.ContainerTransfer),
                    "Transactions advertised an unimplemented container transfer provider.");
            }
            finally
            {
                fixture.Dispose();
            }
        }

        private static void ServicesResolve()
        {
            var fixture = new RegistryFixture();
            try
            {
                True(fixture.Registry.TryGetService(
                    RunicCapabilityIds.PermissionsEvaluate,
                    out IPermissionEvaluationService permissions),
                    "Permissions service was not discoverable.");
                True(fixture.Registry.TryGetService(
                    RunicCapabilityIds.MaterialsReserve,
                    out ITransactionCoordinatorFactory transactions),
                    "Transaction factory was not discoverable.");
                True(fixture.Registry.TryGetService(
                    RunicCapabilityIds.PersistenceMigrate,
                    out IMigrationService migrations),
                    "Migration service was not discoverable.");
                True(fixture.Registry.TryGetService(
                    RunicCapabilityIds.NetworkProtocol,
                    out IProtocolNegotiationService protocol),
                    "Protocol service was not discoverable.");
                True(permissions != null && transactions != null && migrations != null && protocol != null,
                    "A resolved service was null.");
            }
            finally
            {
                fixture.Dispose();
            }
        }

        private static void DisposalRemovesServices()
        {
            var fixture = new RegistryFixture();
            fixture.Dispose();

            Equal(0, fixture.Registry.GetModules().Count);
            True(!fixture.Registry.TryGetService(
                RunicCapabilityIds.PermissionsEvaluate,
                out IPermissionEvaluationService _),
                "Permissions service survived module disposal.");
            True(!fixture.Registry.TryGetService(
                RunicCapabilityIds.MaterialsReserve,
                out ITransactionCoordinatorFactory _),
                "Transaction service survived module disposal.");
            True(!fixture.Registry.TryGetService(
                RunicCapabilityIds.PersistenceMigrate,
                out IMigrationService _),
                "Migration service survived module disposal.");
        }

        private static void ProtocolMismatchIsLocal()
        {
            var service = new PersistenceService();
            var local = new ProtocolHello("local", Array.Empty<ModuleProtocolState>());
            var remote = new ProtocolHello("remote", new[]
            {
                new ModuleProtocolState("runic.transactions", "0.1.0", 1,
                    new[] { RunicCapabilityIds.MaterialsReserve }),
                new ModuleProtocolState("runic.future", "9.0.0", 9,
                    new[] { "future.capability" })
            });
            NegotiationResult result = service.Negotiate(local, remote, new[]
            {
                new ProtocolRequirement("runic.crafting", RunicCapabilityIds.MaterialsReserve, 1, 1),
                new ProtocolRequirement("runic.example", "future.capability", 1, 2)
            });

            True(result.Decisions.Single(item =>
                item.Requirement.CapabilityId == RunicCapabilityIds.MaterialsReserve).Enabled,
                "A compatible transaction capability was disabled by an unrelated mismatch.");
            True(!result.Decisions.Single(item =>
                item.Requirement.CapabilityId == "future.capability").Enabled,
                "An incompatible capability was enabled.");
        }

        private static void MigrationServiceIsAtomic()
        {
            IMigrationService service = new PersistenceService();
            service.Register(new MigrationStep("runic.example", 0, 1, snapshot =>
                snapshot.Set("runic.example.value", Encoding.UTF8.GetBytes("after"))));
            var store = new MemoryRecordStore("runic.example", new RecordSetSnapshot(
                0,
                new Dictionary<string, byte[]>
                {
                    ["thirdparty.value"] = Encoding.UTF8.GetBytes("preserved")
                }));

            MigrationResult result = service.Migrate("runic.example", 1, store, null);

            True(result.Success && result.Changed && result.FinalVersion == 1,
                "Migration did not commit atomically.");
            True(store.ReadSnapshot().TryGet("thirdparty.value", out byte[] preserved),
                "Unknown data was removed.");
            Equal("preserved", Encoding.UTF8.GetString(preserved));
        }

        private static void BuilderArchivalIsRecoverable()
        {
            string root = FindRepositoryRoot();
            string builder = File.ReadAllText(Path.Combine(root, "Build-RunicFoundation.ps1"));
            string releaseDocument = File.ReadAllText(Path.Combine(root, "RUNIC_FOUNDATION.md"));
            int promotionComment = builder.IndexOf(
                "# Final artifacts remain untouched",
                StringComparison.Ordinal);
            int promotionGuard = builder.IndexOf(
                "if (-not $SkipTests)",
                promotionComment,
                StringComparison.Ordinal);
            int archiveCall = builder.IndexOf(
                "Move-SupersededFoundationPackages -Packages",
                promotionGuard,
                StringComparison.Ordinal);
            int packageCopy = builder.IndexOf(
                "Copy-Item -LiteralPath $package.Source",
                archiveCall,
                StringComparison.Ordinal);

            True(promotionComment >= 0 && promotionGuard > promotionComment,
                "Foundation promotion is not guarded by -SkipTests.");
            True(archiveCall > promotionGuard && packageCopy > archiveCall,
                "Superseded packages are not archived before promotion.");
            True(builder.Contains("Get-CollisionSafeArchivePath", StringComparison.Ordinal),
                "Foundation builder has no collision-safe archive allocator.");
            True(builder.Contains("GetTempPath()", StringComparison.Ordinal),
                "Validation-only staging is not isolated from published artifacts.");
            True(builder.Contains("Move-Item -LiteralPath", StringComparison.Ordinal) &&
                 !builder.Contains("Move-Item -LiteralPath $resolvedSource -Destination $archivePath -Force",
                     StringComparison.Ordinal),
                "Foundation archival can overwrite an existing recovery artifact.");
            True(releaseDocument.Contains(
                    "artifacts/RunicFoundation/Obsolete/",
                    StringComparison.Ordinal) &&
                 releaseDocument.Contains(
                    "does not run the\ntest suites, create or change the Foundation artifact directory",
                    StringComparison.Ordinal),
                "Foundation release documentation omits archive or validation-only guarantees.");
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Build-RunicFoundation.ps1")) &&
                    File.Exists(Path.Combine(directory.FullName, "RUNIC_FOUNDATION.md")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new InvalidOperationException("Could not locate the Foundation repository root.");
        }

        private static ModuleDescriptor Descriptor(string id, string name, params string[] capabilities) =>
            new ModuleDescriptor(id, name, "0.1.0", "1.0", capabilities);

        private static void Run(string name, Action action)
        {
            action();
            _passed++;
            Console.WriteLine("PASS " + name);
        }

        private static void True(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException($"Expected {expected}, got {actual}.");
        }

        private sealed class RegistryFixture : IDisposable
        {
            private readonly List<IDisposable> _leases = new List<IDisposable>();

            internal RegistryFixture()
            {
                Registry = new RunicRegistry();
                var permissionService = new PermissionEvaluationService(new PermissionEvaluator(), null);
                var transactionService = new TransactionCoordinatorFactory();
                var persistenceService = new PersistenceService();

                RegisterModule(Descriptor(
                    "runic.permissions",
                    "Runic Permissions",
                    RunicCapabilityIds.PermissionsEvaluate));
                RegisterService(
                    RunicCapabilityIds.PermissionsEvaluate,
                    "runic.permissions",
                    (IPermissionEvaluationService)permissionService);

                RegisterModule(Descriptor(
                    "runic.transactions",
                    "Runic Transactions",
                    RunicCapabilityIds.MaterialsReserve,
                    RunicCapabilityIds.MaterialsConsume));
                RegisterService(
                    RunicCapabilityIds.MaterialsReserve,
                    "runic.transactions",
                    (ITransactionCoordinatorFactory)transactionService);
                RegisterService(
                    RunicCapabilityIds.MaterialsConsume,
                    "runic.transactions",
                    (ITransactionCoordinatorFactory)transactionService);

                RegisterModule(Descriptor(
                    "runic.persistence",
                    "Runic Persistence",
                    RunicCapabilityIds.PersistenceMigrate,
                    RunicCapabilityIds.NetworkProtocol));
                RegisterService(
                    RunicCapabilityIds.PersistenceMigrate,
                    "runic.persistence",
                    (IMigrationService)persistenceService);
                RegisterService(
                    RunicCapabilityIds.NetworkProtocol,
                    "runic.persistence",
                    (IProtocolNegotiationService)persistenceService);

                RegisterModule(Descriptor(
                    "runic.consumer",
                    "Runic Consumer",
                    "consumer.health"));
            }

            internal RunicRegistry Registry { get; }

            public void Dispose()
            {
                for (int index = _leases.Count - 1; index >= 0; index--)
                    _leases[index].Dispose();
                _leases.Clear();
            }

            private void RegisterModule(ModuleDescriptor descriptor) =>
                _leases.Add(Registry.RegisterModule(descriptor));

            private void RegisterService<T>(string capability, string provider, T service) =>
                _leases.Add(Registry.RegisterService(capability, provider, service));
        }
    }
}
