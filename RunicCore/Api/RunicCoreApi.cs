using System;
using System.Collections.Generic;
using System.Threading;

namespace Runic.Foundation.Core
{
    /// <summary>Process-wide entry point used by dependent Runic plugins.</summary>
    public static class RunicCoreApi
    {
        private static int _pluginLoaded;

        static RunicCoreApi()
        {
            Registry = RunicRegistry.Shared;
            Keybindings = new KeybindingConflictRegistry();
            Notifications = new NotificationBus();
            CoreModule = new ModuleDescriptor(
                RunicModuleIds.Core,
                "Runic Core",
                RunicCoreMetadata.SemanticVersion,
                RunicCoreMetadata.ProtocolVersion,
                new[]
                {
                    RunicCapabilityIds.FoundationModules,
                    RunicCapabilityIds.FoundationServices,
                    RunicCapabilityIds.KeybindingsRegistry,
                    RunicCapabilityIds.NotificationPublish
                });
        }

        public static RunicRegistry Registry { get; }
        public static KeybindingConflictRegistry Keybindings { get; }
        public static NotificationBus Notifications { get; }
        public static ModuleDescriptor CoreModule { get; }
        public static SemanticVersion SemanticVersion => RunicCoreMetadata.SemanticVersion;
        public static ProtocolVersion ProtocolVersion => RunicCoreMetadata.ProtocolVersion;
        public static bool IsPluginLoaded => Volatile.Read(ref _pluginLoaded) != 0;

        internal static void SetPluginLoaded(bool loaded) =>
            Volatile.Write(ref _pluginLoaded, loaded ? 1 : 0);
    }
}
