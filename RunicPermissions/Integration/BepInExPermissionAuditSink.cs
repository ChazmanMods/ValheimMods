using System;
using BepInEx.Logging;
using RunicPermissions.Contracts;

namespace RunicPermissions.Integration
{
    internal sealed class BepInExPermissionAuditSink : IPermissionAuditSink
    {
        private const int MaximumFieldLength = 160;
        private readonly ManualLogSource _log;

        internal BepInExPermissionAuditSink(ManualLogSource log)
        {
            _log = log;
        }

        public bool TryRecord(AdminBypassAuditRecord record, out string failureReason)
        {
            if (_log == null || record == null)
            {
                failureReason = "The permission audit logger is unavailable.";
                return false;
            }

            try
            {
                _log.LogWarning(
                    "ADMIN PERMISSION BYPASS" +
                    " evaluation=" + Safe(record.EvaluationId) +
                    " subject=" + Safe(record.Subject?.CanonicalKey) +
                    " action=" + Safe(PermissionActionCodec.ToWireName(record.Action)) +
                    " resource=" + Safe(record.ResourceId) +
                    " session=" + Safe(record.AdminSessionId) +
                    " justification=" + Safe(record.Justification));
                failureReason = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                failureReason = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        private static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value)) return "<none>";
            string sanitized = value
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\t', ' ')
                .Trim();
            return sanitized.Length <= MaximumFieldLength
                ? sanitized
                : sanitized.Substring(0, MaximumFieldLength) + "...";
        }
    }
}
