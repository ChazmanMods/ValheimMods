using System;

namespace Runic.Foundation.Core
{
    public enum HighImpactOperation
    {
        Unknown = 0,
        PortalOverwrite = 1
    }

    public enum ConfirmationDecision
    {
        Pending = 0,
        Approved = 1,
        Denied = 2
    }

    /// <summary>
    /// Immutable, metadata-free confirmation context. StateFingerprint is an opaque fingerprint of
    /// observed state, never raw portal, sign, or player text.
    /// </summary>
    public sealed class ConfirmationContext
    {
        public ConfirmationContext(
            string requesterModuleId,
            HighImpactOperation operation,
            string targetId,
            string stateFingerprint)
        {
            RequesterModuleId = RunicIdentifier.Require(
                requesterModuleId,
                nameof(requesterModuleId));
            if (RequesterModuleId.Length > 96)
                throw new ArgumentOutOfRangeException(nameof(requesterModuleId));
            if (operation != HighImpactOperation.PortalOverwrite)
                throw new ArgumentOutOfRangeException(nameof(operation));
            TargetId = RequireOpaque(targetId, 96, nameof(targetId));
            StateFingerprint = RequireOpaque(stateFingerprint, 128, nameof(stateFingerprint));
            Operation = operation;
        }

        public string RequesterModuleId { get; }
        public HighImpactOperation Operation { get; }
        public string TargetId { get; }
        public string StateFingerprint { get; }

        private static string RequireOpaque(string value, int maximum, string parameterName)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maximum)
                throw new ArgumentOutOfRangeException(parameterName);
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool safe = character >= 'a' && character <= 'z' ||
                            character >= 'A' && character <= 'Z' ||
                            character >= '0' && character <= '9' ||
                            character == '.' || character == '_' || character == '-' ||
                            character == ':' || character == '/';
                if (!safe) throw new ArgumentException("Opaque context contains unsafe text.", parameterName);
            }
            return value;
        }
    }

    /// <summary>
    /// Canonical synchronous confirmation boundary. False means unavailable/incompatible. Approved
    /// is one-shot and consumed by the matching request; consumers fail closed when confirmation is
    /// required but this service is absent or returns false. Providers require an active requester
    /// registration and an exact match between its descriptor ID and Context.RequesterModuleId.
    /// </summary>
    public interface IHighImpactConfirmation
    {
        bool TryRequest(
            ModuleRegistration requester,
            ConfirmationContext context,
            out ConfirmationDecision decision);
        void Cancel(
            ModuleRegistration requester,
            HighImpactOperation operation,
            string targetId);
    }
}
