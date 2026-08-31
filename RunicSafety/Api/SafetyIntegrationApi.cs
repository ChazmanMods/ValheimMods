using System;
using System.Collections.Generic;
using RunicSafety.Integration;

namespace RunicSafety.Api
{
    public interface ISafetyStatusService
    {
        bool IsOperational { get; }
        bool InventoryTopologyProviderAttached { get; }
        IReadOnlyList<string> DisabledGates { get; }
    }

    /// <summary>
    /// Optional in-process seam for independently installed mods. Callers should resolve this
    /// type dynamically and tolerate unavailable services.
    /// </summary>
    public static class SafetyIntegrationApi
    {
        private static readonly object Gate = new object();
        private static SafetyRuntime _runtime;

        public static IContextualConfirmationService Confirmations
        {
            get { lock (Gate) return _runtime?.Confirmations; }
        }

        public static IProtectedItemPolicy Protection
        {
            get { lock (Gate) return _runtime?.Protection; }
        }

        public static IRecoveryPlanningService Recovery
        {
            get { lock (Gate) return _runtime?.Recovery; }
        }

        public static IMigrationBackupService Backups
        {
            get { lock (Gate) return _runtime?.Backups; }
        }

        public static ICompatibilityGate Compatibility
        {
            get { lock (Gate) return _runtime?.Compatibility; }
        }

        public static ISafetyDiagnosticService Diagnostics
        {
            get { lock (Gate) return _runtime?.DiagnosticService; }
        }

        public static ISafetyStatusService Status
        {
            get { lock (Gate) return _runtime; }
        }

        internal static void Attach(SafetyRuntime runtime)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            lock (Gate) _runtime = runtime;
        }

        internal static void Detach(SafetyRuntime runtime)
        {
            lock (Gate)
                if (ReferenceEquals(_runtime, runtime)) _runtime = null;
        }
    }
}
