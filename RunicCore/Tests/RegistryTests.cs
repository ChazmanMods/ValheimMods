using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace Runic.Foundation.Core.Tests
{
    internal static class RegistryTests
    {
        internal static void Register()
        {
            TestRunner.Run("Registry exposes one process-wide shared instance", SharedInstance);
            TestRunner.Run("Modules advertise capability availability and transitions", CapabilityTransitions);
            TestRunner.Run("Typed service lookup is deterministic and falls back", DeterministicServiceLookup);
            TestRunner.Run("Service providers must register and declare capabilities", ServiceValidation);
            TestRunner.Run("Services resolve only through their declared contract", DeclaredContractBoundary);
            TestRunner.Run("Explicit module unregistration removes owned services", ExplicitUnregistration);
            TestRunner.Run("Stale registration leases cannot remove replacements", StaleLeaseSafety);
            TestRunner.Run("Registration provenance is exact to one registry", ExactRegistryProvenance);
            TestRunner.Run("Concurrent module registration preserves every module", ConcurrentRegistration);
            TestRunner.Run("Throwing registry subscribers are isolated", SubscriberIsolation);
        }

        private static void SharedInstance()
        {
            TestAssert.True(object.ReferenceEquals(RunicRegistry.Shared, RunicRegistry.Shared));
            TestAssert.True(object.ReferenceEquals(RunicRegistry.Shared, RunicCoreApi.Registry));
        }

        private static void CapabilityTransitions()
        {
            RunicRegistry registry = new RunicRegistry();
            List<CapabilityChangedEventArgs> changes = new List<CapabilityChangedEventArgs>();
            registry.CapabilityChanged += (_, arguments) => changes.Add(arguments);
            ModuleRegistration module = registry.RegisterModule(Module(
                "test.alpha",
                RunicCapabilityIds.ContainerQuery));
            TestAssert.True(registry.IsCapabilityAvailable(RunicCapabilityIds.ContainerQuery));
            TestAssert.Equal(CapabilityChangeKind.BecameAvailable, changes[0].Kind);
            TestAssert.Equal(CapabilityChangeCause.ModuleRegistered, changes[0].Cause);

            ServiceRegistration service = registry.RegisterService<ITestService>(
                RunicCapabilityIds.ContainerQuery,
                "test.alpha",
                new TestService("alpha"));
            TestAssert.Equal(CapabilityChangeKind.Updated, changes[1].Kind);
            TestAssert.Equal(1, changes[1].Current.ServiceCount);

            service.Dispose();
            module.Dispose();
            TestAssert.Equal(CapabilityChangeKind.BecameUnavailable, changes[3].Kind);
            TestAssert.False(registry.IsCapabilityAvailable(RunicCapabilityIds.ContainerQuery));
        }

        private static void DeterministicServiceLookup()
        {
            RunicRegistry registry = new RunicRegistry();
            registry.RegisterModule(Module("test.beta", RunicCapabilityIds.ContainerQuery));
            registry.RegisterModule(Module("test.alpha", RunicCapabilityIds.ContainerQuery));
            registry.RegisterService<ITestService>(
                RunicCapabilityIds.ContainerQuery,
                "test.beta",
                new TestService("beta"));
            registry.RegisterService<ITestService>(
                RunicCapabilityIds.ContainerQuery,
                "test.alpha",
                new TestService("alpha"));

            TestAssert.True(registry.TryGetService(
                RunicCapabilityIds.ContainerQuery,
                out ITestService selected));
            TestAssert.Equal("alpha", selected.Name);

            ServiceRegistration preferred = registry.RegisterService<ITestService>(
                RunicCapabilityIds.ContainerQuery,
                "test.beta",
                new TestService("preferred"),
                priority: 50);
            registry.TryGetService(RunicCapabilityIds.ContainerQuery, out selected);
            TestAssert.Equal("preferred", selected.Name);
            preferred.Dispose();
            registry.TryGetService(RunicCapabilityIds.ContainerQuery, out selected);
            TestAssert.Equal("alpha", selected.Name);
        }

        private static void ServiceValidation()
        {
            RunicRegistry registry = new RunicRegistry();
            TestAssert.Throws<InvalidOperationException>(() => registry.RegisterService<ITestService>(
                RunicCapabilityIds.ContainerQuery,
                "test.missing",
                new TestService("missing")));
            registry.RegisterModule(Module("test.alpha", RunicCapabilityIds.ZdoObserve));
            TestAssert.Throws<InvalidOperationException>(() => registry.RegisterService<ITestService>(
                RunicCapabilityIds.ContainerQuery,
                "test.alpha",
                new TestService("undeclared")));
        }

        private static void DeclaredContractBoundary()
        {
            RunicRegistry registry = new RunicRegistry();
            registry.RegisterModule(Module("test.alpha", RunicCapabilityIds.ContainerQuery));
            registry.RegisterService<ITestService>(
                RunicCapabilityIds.ContainerQuery,
                "test.alpha",
                new TestService("alpha"));

            TestAssert.False(registry.TryGetService<IUndeclaredService>(
                RunicCapabilityIds.ContainerQuery,
                out _));
            TestAssert.Equal(0, registry.GetServices<IUndeclaredService>(
                RunicCapabilityIds.ContainerQuery).Count);
        }

        private static void ExplicitUnregistration()
        {
            RunicRegistry registry = new RunicRegistry();
            registry.RegisterModule(Module("test.alpha", RunicCapabilityIds.ContainerQuery));
            registry.RegisterService<ITestService>(
                RunicCapabilityIds.ContainerQuery,
                "test.alpha",
                new TestService("alpha"));
            TestAssert.True(registry.UnregisterModule("test.alpha"));
            TestAssert.False(registry.TryGetService<ITestService>(
                RunicCapabilityIds.ContainerQuery,
                out _));
            TestAssert.False(registry.UnregisterModule("test.alpha"));
        }

        private static void StaleLeaseSafety()
        {
            RunicRegistry registry = new RunicRegistry();
            ModuleRegistration stale = registry.RegisterModule(Module(
                "test.alpha",
                RunicCapabilityIds.ContainerQuery));
            registry.UnregisterModule("test.alpha");
            ModuleRegistration current = registry.RegisterModule(Module(
                "test.alpha",
                RunicCapabilityIds.ContainerQuery));
            stale.Dispose();
            TestAssert.True(current.IsActive);
        }

        private static void ExactRegistryProvenance()
        {
            RunicRegistry first = new RunicRegistry();
            RunicRegistry second = new RunicRegistry();
            using ModuleRegistration registration = first.RegisterModule(Module("test.alpha"));
            TestAssert.True(first.IsModuleRegistrationActive(registration));
            TestAssert.False(second.IsModuleRegistrationActive(registration));
            registration.Dispose();
            TestAssert.False(first.IsModuleRegistrationActive(registration));
        }

        private static void ConcurrentRegistration()
        {
            RunicRegistry registry = new RunicRegistry();
            Parallel.For(0, 64, index =>
            {
                string id = "test.concurrent." + index.ToString("D3", CultureInfo.InvariantCulture);
                registry.RegisterModule(Module(id, RunicCapabilityIds.StartupMeasure));
            });
            TestAssert.Equal(64, registry.GetModules().Count);
            TestAssert.Equal(64, registry.GetCapabilitySnapshot(
                RunicCapabilityIds.StartupMeasure).ProviderCount);
        }

        private static void SubscriberIsolation()
        {
            RunicRegistry registry = new RunicRegistry();
            int called = 0;
            registry.CapabilityChanged += (_, __) => throw new InvalidOperationException("listener");
            registry.CapabilityChanged += (_, __) => called++;
            registry.RegisterModule(Module("test.alpha", RunicCapabilityIds.ZdoObserve));
            TestAssert.Equal(1, called);
        }

        private static ModuleDescriptor Module(string id, params string[] capabilities) =>
            new ModuleDescriptor(id, id, "0.1.0", "1.0", capabilities);

        private interface ITestService
        {
            string Name { get; }
        }

        private interface IUndeclaredService
        {
            string Name { get; }
        }

        private sealed class TestService : ITestService, IUndeclaredService
        {
            internal TestService(string name) { Name = name; }
            public string Name { get; }
        }
    }
}
