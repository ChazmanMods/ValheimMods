using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BepInEx.Logging;
using UnityEngine;
using Local = RunicSentinel.Contracts;

namespace RunicSentinel.Runtime
{
    internal sealed class SentinelOperatorCommands : IDisposable
    {
        private const int MaximumReportBytes = 512 * 1024;
        private readonly SentinelRuntime _runtime;
        private readonly ManualLogSource _log;
        private readonly string _reportRoot;
        private readonly SentinelManagedPolicyService _managed;
        private readonly Terminal.ConsoleCommand _command;
        private readonly ConcurrentQueue<string> _dedicatedInput =
            new ConcurrentQueue<string>();
        private Thread _dedicatedInputThread;
        private int _queuedDedicatedLines;
        private bool _disposed;

        internal SentinelOperatorCommands(
            SentinelRuntime runtime,
            ManualLogSource log,
            string configRoot,
            SentinelManagedPolicyService managed)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _log = log;
            _managed = managed ?? throw new ArgumentNullException(nameof(managed));
            _reportRoot = Path.Combine(
                Path.GetFullPath(configRoot),
                "RunicSentinel",
                "reports");
            _command = new Terminal.ConsoleCommand(
                "runic_sentinel",
                "Runic Sentinel: status | report | networks | bootstrap <authority> <subject>",
                OnCommand);
            if (Application.isBatchMode) StartDedicatedConsoleInput();
        }

        private void OnCommand(Terminal.ConsoleEventArgs args)
        {
            if (_disposed || args?.Context == null) return;
            Execute(args.Args, value => args.Context.AddString(value));
        }

        internal void TickDedicatedConsole()
        {
            if (_disposed) return;
            int handled = 0;
            while (handled++ < 8 && _dedicatedInput.TryDequeue(out string line))
            {
                Interlocked.Decrement(ref _queuedDedicatedLines);
                string[] parts = (line ?? string.Empty).Split(
                    new[] { ' ', '\t' },
                    StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                if (!string.Equals(parts[0], "runic_sentinel", StringComparison.OrdinalIgnoreCase))
                {
                    _log?.LogMessage(
                        "Unknown dedicated-server command. Sentinel commands begin with runic_sentinel.");
                    continue;
                }
                Execute(parts, value => _log?.LogMessage(value));
            }
        }

        private void Execute(string[] arguments, Action<string> output)
        {
            if (_disposed || arguments == null || output == null) return;
            string verb = arguments.Length > 1
                ? arguments[1].Trim().ToLowerInvariant()
                : "status";
            if (verb == "status")
            {
                SentinelIntegritySnapshot integrity = _runtime.GetIntegritySnapshot();
                output(
                    "Runic Sentinel: " + integrity.State +
                    "; profile=" + (_runtime.PolicyProfile.Length == 0 ? "none" : _runtime.PolicyProfile) +
                    "; sequence=" + _runtime.PolicySequence.ToString(CultureInfo.InvariantCulture) +
                    "; admission=" + (_runtime.AuthoritativeTransportReady ? "direct-pre-handshake" : "unavailable") +
                    "; last-denial=" + (_runtime.LastAdmissionFailure.Length == 0
                        ? "none"
                        : _runtime.LastAdmissionFailure) + ".");
                return;
            }
            if (verb == "report")
            {
                try
                {
                    string path = WriteReport();
                    output("Runic Sentinel support report created: " + path);
                }
                catch (Exception exception)
                {
                    output("Runic Sentinel report failed closed: " + exception.GetType().Name + ".");
                    _log?.LogWarning("Sentinel report failed: " + exception.GetType().Name + ".");
                }
                return;
            }
            if (verb == "networks")
            {
                try
                {
                    string path = WriteReport(true);
                    output("Runic Sentinel administrator network snapshot created: " + path);
                }
                catch (Exception exception)
                {
                    output(
                        exception.Message == "server-console-required"
                            ? "Runic Sentinel network maps are available only on the authoritative server."
                            : "Runic Sentinel network snapshot failed closed: " + exception.GetType().Name + ".");
                }
                return;
            }
            if (verb == "bootstrap")
            {
                if (arguments.Length != 4)
                {
                    output("Usage: runic_sentinel bootstrap <authority> <subject>");
                    return;
                }
                try
                {
                    if (ZNet.instance == null || !ZNet.instance.IsServer())
                        throw new InvalidOperationException("authoritative-server-console-required");
                    output(_managed.Bootstrap(arguments[2], arguments[3]));
                }
                catch (Exception exception)
                {
                    output("Runic Sentinel bootstrap failed closed: " + exception.Message);
                }
                return;
            }
            output("Usage: runic_sentinel status | report | networks | bootstrap <authority> <subject>");
        }

        private void StartDedicatedConsoleInput()
        {
            _dedicatedInputThread = new Thread(ReadDedicatedConsole)
            {
                IsBackground = true,
                Name = "RunicSentinel.DedicatedConsole"
            };
            _dedicatedInputThread.Start();
            _log?.LogMessage(
                "Runic Sentinel dedicated console input is ready. Type runic_sentinel status for help.");
        }

        private void ReadDedicatedConsole()
        {
            while (!_disposed)
            {
                string line;
                try { line = System.Console.ReadLine(); }
                catch { return; }
                if (line == null) return;
                if (line.Length > 1024)
                {
                    _log?.LogWarning("An oversized dedicated-server console line was ignored.");
                    continue;
                }
                if (Interlocked.Increment(ref _queuedDedicatedLines) > 32)
                {
                    Interlocked.Decrement(ref _queuedDedicatedLines);
                    _log?.LogWarning("The bounded dedicated-server command queue is full.");
                    continue;
                }
                _dedicatedInput.Enqueue(line);
            }
        }

        internal string WriteReport(bool includeNetworks = false)
        {
            var builder = new StringBuilder(16384);
            SentinelIntegritySnapshot integrity = _runtime.GetIntegritySnapshot();
            builder.Append("RUNIC-SENTINEL-SUPPORT/1\n")
                .Append("created-utc=").Append(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)).Append('\n')
                .Append("world=").Append(ZNet.instance?.GetWorldName()??"Unknown").Append('\n')
                .Append("integrity=").Append(integrity.State).Append('\n')
                .Append("integrity-reason=").Append(integrity.ReasonCode).Append('\n')
                .Append("policy-profile=").Append(_runtime.PolicyProfile).Append('\n')
                .Append("policy-sequence=").Append(_runtime.PolicySequence.ToString(CultureInfo.InvariantCulture)).Append('\n')
                .Append("policy-digest=").Append(integrity.PolicyDigest).Append('\n')
                .Append("admission-transport=").Append(_runtime.AuthoritativeTransportReady ? "ready" : "unavailable").Append('\n')
                .Append("last-admission-denial=").Append(_runtime.LastAdmissionFailure).Append('\n');

            if (_runtime.TryGetCurrent(out Local.AttestationSnapshot snapshot, out string status))
            {
                builder.Append("snapshot-status=").Append(status).Append('\n')
                    .Append("snapshot-digest=").Append(snapshot.Digest).Append('\n')
                    .Append("plugins=").Append(snapshot.Plugins.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
                foreach (Local.AttestedPlugin plugin in snapshot.Plugins)
                    builder.Append("plugin=").Append(plugin.Id).Append('|')
                        .Append(plugin.Version).Append('|').Append(plugin.Sha256).Append('\n');
            }
            else builder.Append("snapshot-status=").Append(status).Append('\n');

            builder.Append("transport=standalone-valheim\n");

            Local.EvidenceReadSnapshot evidence = _runtime.Evidence.ReadAfter(0L, 256);
            builder.Append("evidence-newest=")
                .Append(evidence.NewestSequence.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (Local.SecurityEvidence entry in evidence.Entries)
                builder.Append("evidence=").Append(entry.Sequence.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(entry.UnixSeconds.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(entry.ProviderModuleId).Append('|').Append(entry.Rule).Append('|')
                    .Append(entry.Confidence).Append('|').Append(entry.EffectiveAction).Append('|')
                    .Append(entry.Detail).Append('\n');

            if (includeNetworks && !SentinelNetworkMapWriter.TryAppend(builder, out string mapFailure))
                throw new InvalidOperationException(mapFailure);

            byte[] bytes = new UTF8Encoding(false).GetBytes(SentinelReadableReport.Format(builder.ToString(),includeNetworks));
            if (bytes.Length > MaximumReportBytes)
                throw new InvalidDataException("The bounded support report exceeded 512 KiB.");
            Directory.CreateDirectory(_reportRoot);
            string path = Path.Combine(
                _reportRoot,
                (includeNetworks?"sentinel-networks-":"sentinel-health-") + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N").Substring(0,8) + ".txt");
            using (var stream = new FileStream(
                       path,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.Read,
                       65536,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            return path;
        }
        internal string ReadReportChunk(string request)
        {
            var fields=request.Split('\n');
            if(fields.Length!=2||fields[0]!=Path.GetFileName(fields[0])||!fields[0].EndsWith(".txt",StringComparison.Ordinal)||!(fields[0].StartsWith("sentinel-health-",StringComparison.Ordinal)||fields[0].StartsWith("sentinel-networks-",StringComparison.Ordinal))||!int.TryParse(fields[1],out int offset)||offset<0)
                throw new InvalidDataException("Invalid report download request.");
            string path=Path.Combine(_reportRoot,fields[0]);
            using(var stream=File.OpenRead(path))
            {
                if(stream.Length>MaximumReportBytes||offset>stream.Length)throw new InvalidDataException("Report is outside download bounds.");
                stream.Position=offset;var buffer=new byte[Math.Min(32768,(int)stream.Length-offset)];int count=stream.Read(buffer,0,buffer.Length);
                return SentinelJson.Write(new SentinelReportChunk{data=Convert.ToBase64String(buffer,0,count),offset=offset,total=(int)stream.Length});
            }
        }

        public void Dispose()
        {
            _disposed = true;
            while (_dedicatedInput.TryDequeue(out _)) { }
            Interlocked.Exchange(ref _queuedDedicatedLines, 0);
        }
    }
}
