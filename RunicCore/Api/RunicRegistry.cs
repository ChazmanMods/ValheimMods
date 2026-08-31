using System;
using System.Collections.Generic;

namespace Runic.Foundation.Core
{
    public enum ModuleChangeKind
    {
        Registered = 0,
        Unregistered = 1
    }

    public sealed class ModuleChangedEventArgs : EventArgs
    {
        internal ModuleChangedEventArgs(ModuleChangeKind kind, ModuleDescriptor module, long generation)
        {
            Kind = kind;
            Module = module;
            Generation = generation;
        }

        public ModuleChangeKind Kind { get; }
        public ModuleDescriptor Module { get; }
        public long Generation { get; }
    }

    public enum CapabilityChangeKind
    {
        BecameAvailable = 0,
        Updated = 1,
        BecameUnavailable = 2
    }

    public enum CapabilityChangeCause
    {
        ModuleRegistered = 0,
        ModuleUnregistered = 1,
        ServiceRegistered = 2,
        ServiceUnregistered = 3
    }

    public sealed class CapabilitySnapshot
    {
        internal CapabilitySnapshot(
            string capabilityId,
            IReadOnlyList<string> providerModuleIds,
            int serviceCount,
            long generation)
        {
            CapabilityId = capabilityId;
            ProviderModuleIds = providerModuleIds;
            ServiceCount = serviceCount;
            Generation = generation;
        }

        public string CapabilityId { get; }
        public IReadOnlyList<string> ProviderModuleIds { get; }
        public int ProviderCount => ProviderModuleIds.Count;
        public int ServiceCount { get; }
        public bool IsAvailable => ProviderCount > 0;
        public long Generation { get; }
    }

    public sealed class CapabilityChangedEventArgs : EventArgs
    {
        internal CapabilityChangedEventArgs(
            CapabilityChangeKind kind,
            CapabilityChangeCause cause,
            string providerModuleId,
            CapabilitySnapshot previous,
            CapabilitySnapshot current)
        {
            Kind = kind;
            Cause = cause;
            ProviderModuleId = providerModuleId;
            Previous = previous;
            Current = current;
        }

        public string CapabilityId => Current.CapabilityId;
        public CapabilityChangeKind Kind { get; }
        public CapabilityChangeCause Cause { get; }
        public string ProviderModuleId { get; }
        public CapabilitySnapshot Previous { get; }
        public CapabilitySnapshot Current { get; }
        public long Generation => Current.Generation;
    }

    /// <summary>
    /// Thread-safe module discovery and typed service registry. Services are resolved by priority,
    /// then provider module ID, then registration order so lookup is deterministic.
    /// </summary>
    public sealed class RunicRegistry
    {
        /// <summary>The process-wide registry used by the Runic Core plugin and dependents.</summary>
        public static RunicRegistry Shared { get; } = new RunicRegistry();

        private readonly object _sync = new object();
        private readonly Dictionary<string, ModuleEntry> _modules =
            new Dictionary<string, ModuleEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ServiceEntry>> _services =
            new Dictionary<string, List<ServiceEntry>>(StringComparer.Ordinal);
        private long _nextToken;
        private long _generation;

        public event EventHandler<ModuleChangedEventArgs> ModuleChanged;
        public event EventHandler<CapabilityChangedEventArgs> CapabilityChanged;

        public ModuleRegistration RegisterModule(ModuleDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));

            List<SnapshotPair> changes;
            long token;
            long generation;
            lock (_sync)
            {
                if (_modules.ContainsKey(descriptor.ModuleId))
                {
                    throw new InvalidOperationException(
                        "A module is already registered with ID '" + descriptor.ModuleId + "'.");
                }

                Dictionary<string, CapabilitySnapshot> previous =
                    CaptureSnapshotsLocked(descriptor.OwnedCapabilities);
                token = NextTokenLocked();
                _modules.Add(descriptor.ModuleId, new ModuleEntry(descriptor, token));
                generation = ++_generation;
                changes = CompleteSnapshotsLocked(previous, generation);
            }

            RaiseModuleChanged(new ModuleChangedEventArgs(
                ModuleChangeKind.Registered,
                descriptor,
                generation));
            RaiseCapabilityChanges(
                changes,
                CapabilityChangeCause.ModuleRegistered,
                descriptor.ModuleId);
            return new ModuleRegistration(this, descriptor, token);
        }

        public bool UnregisterModule(string moduleId)
        {
            RunicIdentifier.Require(moduleId, nameof(moduleId));
            return UnregisterModule(moduleId, null);
        }

        public bool TryGetModule(string moduleId, out ModuleDescriptor descriptor)
        {
            descriptor = null;
            if (!RunicIdentifier.IsValid(moduleId)) return false;
            lock (_sync)
            {
                if (!_modules.TryGetValue(moduleId, out ModuleEntry entry)) return false;
                descriptor = entry.Descriptor;
                return true;
            }
        }

        public IReadOnlyList<ModuleDescriptor> GetModules()
        {
            lock (_sync)
            {
                List<ModuleDescriptor> modules = new List<ModuleDescriptor>(_modules.Count);
                foreach (ModuleEntry entry in _modules.Values) modules.Add(entry.Descriptor);
                modules.Sort((left, right) =>
                    StringComparer.Ordinal.Compare(left.ModuleId, right.ModuleId));
                return modules.AsReadOnly();
            }
        }

        public IReadOnlyList<string> GetCapabilities()
        {
            lock (_sync)
            {
                SortedSet<string> capabilities = new SortedSet<string>(StringComparer.Ordinal);
                foreach (ModuleEntry entry in _modules.Values)
                    foreach (string capability in entry.Descriptor.OwnedCapabilities)
                        capabilities.Add(capability);
                string[] copy = new string[capabilities.Count];
                capabilities.CopyTo(copy);
                return Array.AsReadOnly(copy);
            }
        }

        public bool IsCapabilityAvailable(string capabilityId)
        {
            RunicIdentifier.Require(capabilityId, nameof(capabilityId));
            lock (_sync) return CreateSnapshotLocked(capabilityId, _generation).IsAvailable;
        }

        public CapabilitySnapshot GetCapabilitySnapshot(string capabilityId)
        {
            RunicIdentifier.Require(capabilityId, nameof(capabilityId));
            lock (_sync) return CreateSnapshotLocked(capabilityId, _generation);
        }

        public ServiceRegistration RegisterService<T>(
            string capabilityId,
            string providerModuleId,
            T service)
        {
            return RegisterService(capabilityId, providerModuleId, typeof(T), service, 0);
        }

        public ServiceRegistration RegisterService<T>(
            string capabilityId,
            string providerModuleId,
            T service,
            int priority)
        {
            return RegisterService(capabilityId, providerModuleId, typeof(T), service, priority);
        }

        public ServiceRegistration RegisterService(
            string capabilityId,
            string providerModuleId,
            Type contractType,
            object service,
            int priority = 0)
        {
            RunicIdentifier.Require(capabilityId, nameof(capabilityId));
            RunicIdentifier.Require(providerModuleId, nameof(providerModuleId));
            if (contractType == null) throw new ArgumentNullException(nameof(contractType));
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (!contractType.IsInstanceOfType(service))
            {
                throw new ArgumentException(
                    "The service instance does not implement the declared contract type.",
                    nameof(service));
            }

            SnapshotPair change;
            long token;
            lock (_sync)
            {
                if (!_modules.TryGetValue(providerModuleId, out ModuleEntry provider))
                {
                    throw new InvalidOperationException(
                        "Service provider module '" + providerModuleId + "' is not registered.");
                }
                if (!provider.Descriptor.HasCapability(capabilityId))
                {
                    throw new InvalidOperationException(
                        "Module '" + providerModuleId + "' did not declare capability '" +
                        capabilityId + "'.");
                }

                CapabilitySnapshot previous = CreateSnapshotLocked(capabilityId, _generation);
                token = NextTokenLocked();
                if (!_services.TryGetValue(capabilityId, out List<ServiceEntry> services))
                {
                    services = new List<ServiceEntry>();
                    _services.Add(capabilityId, services);
                }
                services.Add(new ServiceEntry(
                    capabilityId,
                    providerModuleId,
                    contractType,
                    service,
                    priority,
                    token));
                long generation = ++_generation;
                change = new SnapshotPair(
                    previous,
                    CreateSnapshotLocked(capabilityId, generation));
            }

            RaiseCapabilityChange(
                change,
                CapabilityChangeCause.ServiceRegistered,
                providerModuleId);
            return new ServiceRegistration(
                this,
                capabilityId,
                providerModuleId,
                contractType,
                service,
                priority,
                token);
        }

        public bool TryGetService<T>(string capabilityId, out T service)
        {
            service = default;
            if (!RunicIdentifier.IsValid(capabilityId)) return false;

            lock (_sync)
            {
                if (!_services.TryGetValue(capabilityId, out List<ServiceEntry> services))
                    return false;

                ServiceEntry best = null;
                foreach (ServiceEntry candidate in services)
                {
                    if (candidate.ContractType != typeof(T) || !(candidate.Service is T)) continue;
                    if (best == null || CompareServices(candidate, best) < 0) best = candidate;
                }

                if (best == null) return false;
                service = (T)best.Service;
                return true;
            }
        }

        public IReadOnlyList<T> GetServices<T>(string capabilityId)
        {
            RunicIdentifier.Require(capabilityId, nameof(capabilityId));
            lock (_sync)
            {
                if (!_services.TryGetValue(capabilityId, out List<ServiceEntry> services))
                    return Array.Empty<T>();

                List<ServiceEntry> matches = new List<ServiceEntry>();
                foreach (ServiceEntry candidate in services)
                    if (candidate.ContractType == typeof(T) && candidate.Service is T) matches.Add(candidate);
                matches.Sort(CompareServices);

                List<T> result = new List<T>(matches.Count);
                foreach (ServiceEntry match in matches) result.Add((T)match.Service);
                return result.AsReadOnly();
            }
        }

        /// <summary>
        /// Returns true only when the supplied lease was issued by this exact registry instance and
        /// its private registration token still owns the module identity.
        /// </summary>
        public bool IsModuleRegistrationActive(ModuleRegistration registration)
        {
            if (registration == null || !ReferenceEquals(registration.Registry, this)) return false;
            return IsModuleRegistrationActive(
                registration.Descriptor.ModuleId,
                registration.Token);
        }

        internal bool IsModuleRegistrationActive(string moduleId, long token)
        {
            lock (_sync)
                return _modules.TryGetValue(moduleId, out ModuleEntry entry) && entry.Token == token;
        }

        internal bool IsServiceRegistrationActive(string capabilityId, long token)
        {
            lock (_sync)
            {
                if (!_services.TryGetValue(capabilityId, out List<ServiceEntry> services)) return false;
                foreach (ServiceEntry service in services)
                    if (service.Token == token) return true;
                return false;
            }
        }

        internal bool UnregisterModule(string moduleId, long? requiredToken)
        {
            ModuleDescriptor descriptor;
            List<SnapshotPair> changes;
            long generation;
            lock (_sync)
            {
                if (!_modules.TryGetValue(moduleId, out ModuleEntry entry) ||
                    requiredToken.HasValue && entry.Token != requiredToken.Value)
                {
                    return false;
                }

                descriptor = entry.Descriptor;
                SortedSet<string> affected = new SortedSet<string>(StringComparer.Ordinal);
                foreach (string capability in descriptor.OwnedCapabilities) affected.Add(capability);
                foreach (KeyValuePair<string, List<ServiceEntry>> pair in _services)
                    foreach (ServiceEntry service in pair.Value)
                        if (string.Equals(service.ProviderModuleId, moduleId, StringComparison.Ordinal))
                            affected.Add(pair.Key);

                Dictionary<string, CapabilitySnapshot> previous =
                    CaptureSnapshotsLocked(affected);
                _modules.Remove(moduleId);

                List<string> emptyCapabilities = new List<string>();
                foreach (KeyValuePair<string, List<ServiceEntry>> pair in _services)
                {
                    pair.Value.RemoveAll(service =>
                        string.Equals(service.ProviderModuleId, moduleId, StringComparison.Ordinal));
                    if (pair.Value.Count == 0) emptyCapabilities.Add(pair.Key);
                }
                foreach (string capability in emptyCapabilities) _services.Remove(capability);

                generation = ++_generation;
                changes = CompleteSnapshotsLocked(previous, generation);
            }

            RaiseModuleChanged(new ModuleChangedEventArgs(
                ModuleChangeKind.Unregistered,
                descriptor,
                generation));
            RaiseCapabilityChanges(
                changes,
                CapabilityChangeCause.ModuleUnregistered,
                descriptor.ModuleId);
            return true;
        }

        internal bool UnregisterService(string capabilityId, long token)
        {
            SnapshotPair change;
            string providerModuleId;
            lock (_sync)
            {
                if (!_services.TryGetValue(capabilityId, out List<ServiceEntry> services))
                    return false;
                int index = services.FindIndex(service => service.Token == token);
                if (index < 0) return false;

                CapabilitySnapshot previous = CreateSnapshotLocked(capabilityId, _generation);
                providerModuleId = services[index].ProviderModuleId;
                services.RemoveAt(index);
                if (services.Count == 0) _services.Remove(capabilityId);
                long generation = ++_generation;
                change = new SnapshotPair(
                    previous,
                    CreateSnapshotLocked(capabilityId, generation));
            }

            RaiseCapabilityChange(
                change,
                CapabilityChangeCause.ServiceUnregistered,
                providerModuleId);
            return true;
        }

        private long NextTokenLocked()
        {
            if (_nextToken == long.MaxValue)
                throw new InvalidOperationException("The registry registration token space is exhausted.");
            return ++_nextToken;
        }

        private Dictionary<string, CapabilitySnapshot> CaptureSnapshotsLocked(
            IEnumerable<string> capabilities)
        {
            Dictionary<string, CapabilitySnapshot> snapshots =
                new Dictionary<string, CapabilitySnapshot>(StringComparer.Ordinal);
            foreach (string capability in capabilities)
                snapshots[capability] = CreateSnapshotLocked(capability, _generation);
            return snapshots;
        }

        private List<SnapshotPair> CompleteSnapshotsLocked(
            Dictionary<string, CapabilitySnapshot> previous,
            long generation)
        {
            List<SnapshotPair> changes = new List<SnapshotPair>(previous.Count);
            foreach (KeyValuePair<string, CapabilitySnapshot> pair in previous)
                changes.Add(new SnapshotPair(pair.Value, CreateSnapshotLocked(pair.Key, generation)));
            changes.Sort((left, right) =>
                StringComparer.Ordinal.Compare(
                    left.Current.CapabilityId,
                    right.Current.CapabilityId));
            return changes;
        }

        private CapabilitySnapshot CreateSnapshotLocked(string capabilityId, long generation)
        {
            List<string> providers = new List<string>();
            foreach (ModuleEntry entry in _modules.Values)
                if (entry.Descriptor.HasCapability(capabilityId))
                    providers.Add(entry.Descriptor.ModuleId);
            providers.Sort(StringComparer.Ordinal);
            int serviceCount =
                _services.TryGetValue(capabilityId, out List<ServiceEntry> services)
                    ? services.Count
                    : 0;
            return new CapabilitySnapshot(
                capabilityId,
                providers.AsReadOnly(),
                serviceCount,
                generation);
        }

        private static int CompareServices(ServiceEntry left, ServiceEntry right)
        {
            int comparison = right.Priority.CompareTo(left.Priority);
            if (comparison != 0) return comparison;
            comparison = StringComparer.Ordinal.Compare(left.ProviderModuleId, right.ProviderModuleId);
            return comparison != 0 ? comparison : left.Token.CompareTo(right.Token);
        }

        private void RaiseModuleChanged(ModuleChangedEventArgs arguments)
        {
            EventHandler<ModuleChangedEventArgs> handlers = ModuleChanged;
            if (handlers == null) return;
            foreach (EventHandler<ModuleChangedEventArgs> handler in handlers.GetInvocationList())
            {
                try { handler(this, arguments); }
                catch (Exception) { }
            }
        }

        private void RaiseCapabilityChanges(
            IEnumerable<SnapshotPair> changes,
            CapabilityChangeCause cause,
            string providerModuleId)
        {
            foreach (SnapshotPair change in changes)
                RaiseCapabilityChange(change, cause, providerModuleId);
        }

        private void RaiseCapabilityChange(
            SnapshotPair change,
            CapabilityChangeCause cause,
            string providerModuleId)
        {
            EventHandler<CapabilityChangedEventArgs> handlers = CapabilityChanged;
            if (handlers == null) return;
            CapabilityChangedEventArgs arguments = new CapabilityChangedEventArgs(
                DetermineChangeKind(change.Previous, change.Current),
                cause,
                providerModuleId,
                change.Previous,
                change.Current);
            foreach (EventHandler<CapabilityChangedEventArgs> handler in handlers.GetInvocationList())
            {
                try { handler(this, arguments); }
                catch (Exception) { }
            }
        }

        private static CapabilityChangeKind DetermineChangeKind(
            CapabilitySnapshot previous,
            CapabilitySnapshot current)
        {
            if (!previous.IsAvailable && current.IsAvailable)
                return CapabilityChangeKind.BecameAvailable;
            if (previous.IsAvailable && !current.IsAvailable)
                return CapabilityChangeKind.BecameUnavailable;
            return CapabilityChangeKind.Updated;
        }

        private sealed class ModuleEntry
        {
            internal ModuleEntry(ModuleDescriptor descriptor, long token)
            {
                Descriptor = descriptor;
                Token = token;
            }

            internal ModuleDescriptor Descriptor { get; }
            internal long Token { get; }
        }

        private sealed class ServiceEntry
        {
            internal ServiceEntry(
                string capabilityId,
                string providerModuleId,
                Type contractType,
                object service,
                int priority,
                long token)
            {
                CapabilityId = capabilityId;
                ProviderModuleId = providerModuleId;
                ContractType = contractType;
                Service = service;
                Priority = priority;
                Token = token;
            }

            internal string CapabilityId { get; }
            internal string ProviderModuleId { get; }
            internal Type ContractType { get; }
            internal object Service { get; }
            internal int Priority { get; }
            internal long Token { get; }
        }

        private readonly struct SnapshotPair
        {
            internal SnapshotPair(CapabilitySnapshot previous, CapabilitySnapshot current)
            {
                Previous = previous;
                Current = current;
            }

            internal CapabilitySnapshot Previous { get; }
            internal CapabilitySnapshot Current { get; }
        }
    }

    public sealed class ModuleRegistration : IDisposable
    {
        private readonly RunicRegistry _registry;
        private readonly long _token;

        internal ModuleRegistration(RunicRegistry registry, ModuleDescriptor descriptor, long token)
        {
            _registry = registry;
            Descriptor = descriptor;
            _token = token;
        }

        public ModuleDescriptor Descriptor { get; }
        /// <summary>
        /// True while this lease owns its identity in the registry that issued it. A service that
        /// authenticates callers against a particular registry must instead call that target's
        /// RunicRegistry.IsModuleRegistrationActive(this) provenance check.
        /// </summary>
        public bool IsActive => _registry.IsModuleRegistrationActive(Descriptor.ModuleId, _token);
        internal RunicRegistry Registry => _registry;
        internal long Token => _token;
        public void Dispose() => _registry.UnregisterModule(Descriptor.ModuleId, _token);
    }

    public sealed class ServiceRegistration : IDisposable
    {
        private readonly RunicRegistry _registry;
        private readonly long _token;

        internal ServiceRegistration(
            RunicRegistry registry,
            string capabilityId,
            string providerModuleId,
            Type contractType,
            object service,
            int priority,
            long token)
        {
            _registry = registry;
            CapabilityId = capabilityId;
            ProviderModuleId = providerModuleId;
            ContractType = contractType;
            Service = service;
            Priority = priority;
            _token = token;
        }

        public string CapabilityId { get; }
        public string ProviderModuleId { get; }
        public Type ContractType { get; }
        public object Service { get; }
        public int Priority { get; }
        public bool IsActive => _registry.IsServiceRegistrationActive(CapabilityId, _token);
        public void Dispose() => _registry.UnregisterService(CapabilityId, _token);
    }
}
