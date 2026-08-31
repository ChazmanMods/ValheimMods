using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using RunicVelocity.Contracts;
using RunicVelocity.Core;

namespace RunicVelocity.Integration
{
    internal sealed class VelocityRuntime
    {
        private static readonly long ProcessOrigin = Stopwatch.GetTimestamp();
        private readonly StartupTimeline _timeline = new StartupTimeline(ProcessOrigin);
        private readonly object _gate = new object();
        private Task<PluginManifestSnapshot> _scan;
        private CancellationTokenSource _cancellation;
        private int _generation;
        private bool _logged;
        private bool _enabled;
        private bool _firstUpdatePending;
        private bool _mainMenuPending;
        private bool _networkPending;
        private bool _worldPending;
        private bool _playerPending;
        private bool _scanPending;

        internal void Start()
        {
            _enabled = VelocityConfig.Enabled?.Value ?? true;
            if (!_enabled) return;
            _firstUpdatePending = true;
            _mainMenuPending = true;
            _networkPending = true;
            _worldPending = true;
            _playerPending = true;
            _timeline.Record("plugin-awake");
            BeginScan();
        }

        internal void Tick()
        {
            if (!_enabled) return;
            if (_firstUpdatePending)
            {
                _timeline.Record("first-update");
                _firstUpdatePending = false;
            }
            if (_mainMenuPending && FejdStartup.instance != null)
            {
                _timeline.Record("main-menu-instance");
                _mainMenuPending = false;
            }
            if (_networkPending && ZNet.instance != null)
            {
                _timeline.Record("network-session-instance");
                _networkPending = false;
            }
            if (_worldPending && Game.instance != null)
            {
                _timeline.Record("world-game-instance");
                _worldPending = false;
            }
            if (_playerPending && Player.m_localPlayer != null)
            {
                _timeline.Record("local-player-ready");
                _playerPending = false;
            }

            if (!_firstUpdatePending && !_mainMenuPending && !_networkPending &&
                !_worldPending && !_playerPending && !_scanPending) return;

            Task<PluginManifestSnapshot> scan;
            lock (_gate) scan = _scan;
            if (scan == null || !scan.IsCompleted) return;
            PluginManifestSnapshot completed;
            try { completed = scan.GetAwaiter().GetResult(); }
            catch { completed = new PluginManifestSnapshot(Array.Empty<PluginManifestEntry>(), 0, 0, true, "scan-failed"); }
            lock (_gate)
            {
                if (!ReferenceEquals(_scan, scan)) return;
                _scan = null;
                _scanPending = false;
            }
            _timeline.Record("manifest-scan-complete");
            if (!_logged && (VelocityConfig.DetailedTracing?.Value ?? false))
            {
                _logged = true;
                Plugin.Log?.LogInfo($"Velocity manifest: {completed.Entries.Count} DLLs, " +
                    $"{completed.ReusedFiles} reused, {completed.HashedFiles} hashed, status={completed.Status}.");
            }
        }

        internal void Stop()
        {
            _enabled = false;
            Interlocked.Increment(ref _generation);
            try { _cancellation?.Cancel(); } catch { }
            try { _cancellation?.Dispose(); } catch { }
            _cancellation = null;
            lock (_gate) _scan = null;
            _scanPending = false;
        }

        private void BeginScan()
        {
            int generation = Interlocked.Increment(ref _generation);
            try { _cancellation?.Cancel(); } catch { }
            try { _cancellation?.Dispose(); } catch { }
            _cancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = _cancellation.Token;
            string root = Paths.PluginPath;
            string cache = Path.Combine(Paths.ConfigPath, "RunicVelocity.manifest-cache.bin");
            bool warm = VelocityConfig.WarmManifest?.Value ?? true;
            int maximum = VelocityConfig.MaximumFiles?.Value ?? 2048;
            _timeline.Record("manifest-scan-start");
            _scanPending = true;
            var scanner = new PluginManifestScanner();
            Task<PluginManifestSnapshot> task = Task.Run(
                () => scanner.Scan(root, cache, warm, maximum, cancellationToken),
                cancellationToken);
            lock (_gate)
            {
                if (generation == _generation) _scan = task;
            }
        }
    }
}
