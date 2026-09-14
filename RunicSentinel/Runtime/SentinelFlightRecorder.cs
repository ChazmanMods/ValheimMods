using System;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx.Logging;
using RunicSentinel.Contracts;
using RunicSentinel.Core;

namespace RunicSentinel.Runtime
{
    internal sealed class SentinelFlightRecorder : IDisposable
    {
        internal const long MaximumFileBytes = 512L * 1024L;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static readonly byte[] Header = Utf8.GetBytes("RUNIC-SENTINEL-FLIGHT/1\n");
        private readonly object _gate = new object();
        private readonly EvidenceLedger _ledger;
        private readonly ManualLogSource _log;
        private readonly string _activePath;
        private readonly string _previousPath;
        private bool _disposed;
        private bool _faultLogged;

        internal SentinelFlightRecorder(
            EvidenceLedger ledger,
            ManualLogSource log,
            string configRoot)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _log = log;
            string root = Path.Combine(
                Path.GetFullPath(configRoot),
                "RunicSentinel",
                "flight-recorder");
            _activePath = Path.Combine(root, "security-current.log");
            _previousPath = Path.Combine(root, "security-previous.log");
            _ledger.Accepted += OnAccepted;
        }

        internal string ActivePath => _activePath;

        private void OnAccepted(SecurityEvidence evidence)
        {
            if (evidence == null) return;
            byte[] record = Utf8.GetBytes(Encode(evidence));
            lock (_gate)
            {
                if (_disposed) return;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_activePath));
                    long existing = File.Exists(_activePath)
                        ? new FileInfo(_activePath).Length
                        : 0L;
                    long required = record.Length + (existing == 0L ? Header.Length : 0L);
                    if (existing + required > MaximumFileBytes)
                    {
                        if (File.Exists(_previousPath)) File.Delete(_previousPath);
                        if (File.Exists(_activePath)) File.Move(_activePath, _previousPath);
                        existing = 0L;
                    }
                    using (var stream = new FileStream(
                               _activePath,
                               FileMode.Append,
                               FileAccess.Write,
                               FileShare.Read,
                               4096,
                               FileOptions.WriteThrough))
                    {
                        if (existing == 0L)
                        {
                            stream.Write(Header, 0, Header.Length);
                        }
                        stream.Write(record, 0, record.Length);
                        stream.Flush(true);
                    }
                }
                catch (Exception exception)
                {
                    if (_faultLogged) return;
                    _faultLogged = true;
                    _log?.LogWarning(
                        "Sentinel flight-recorder write failed; enforcement remains active: " +
                        exception.GetType().Name + ".");
                }
            }
        }

        private static string Encode(SecurityEvidence value) =>
            value.Sequence.ToString(CultureInfo.InvariantCulture) + "|" +
            value.UnixSeconds.ToString(CultureInfo.InvariantCulture) + "|" +
            Base64(value.ProviderModuleId) + "|" + Base64(value.Actor) + "|" +
            Base64(value.Rule) + "|" + Base64(value.CorrelationId) + "|" +
            ((int)value.Confidence).ToString(CultureInfo.InvariantCulture) + "|" +
            ((int)value.RequestedAction).ToString(CultureInfo.InvariantCulture) + "|" +
            ((int)value.EffectiveAction).ToString(CultureInfo.InvariantCulture) + "|" +
            value.PolicySequence.ToString(CultureInfo.InvariantCulture) + "|" +
            Base64(value.Detail) + "\n";

        private static string Base64(string value) => Convert.ToBase64String(
            Utf8.GetBytes(value ?? string.Empty));

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }
            _ledger.Accepted -= OnAccepted;
        }
    }
}
