using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace RunicCharacterVault
{
    internal sealed class GracefulShutdownCoordinator : IDisposable
    {
        private const string ExitFilePath = "character_vault.drp";
        private const int MaximumConcurrentSaves = 4;
        private const int ShutdownTimeoutSeconds = 90;
        private const int ClientDisconnectGraceSeconds = 2;
        private readonly HashSet<ZNetPeer> _pendingPeers = new HashSet<ZNetPeer>();
        private readonly HashSet<ZNetPeer> _requestedPeers = new HashSet<ZNetPeer>();
        private readonly HashSet<ZRpc> _shutdownPeerRpcs = new HashSet<ZRpc>();
        private readonly Queue<ZNetPeer> _queuedPeers = new Queue<ZNetPeer>();
        private readonly FileSystemWatcher _exitFileWatcher;
        private readonly SynchronizationContext _unityContext;
        private System.Threading.Timer _timeoutTimer;
        private System.Threading.Timer _disconnectTimer;
        private int _disposed;
        private int _exitRequestQueued;
        private volatile bool _watcherFailed;
        private bool _exitRequestPending;
        private bool _shutdownCommitted;
        private string _requestId;

        internal GracefulShutdownCoordinator(SynchronizationContext unityContext)
        {
            _unityContext = unityContext ?? throw new ArgumentNullException(nameof(unityContext));
            try
            {
                _exitFileWatcher = CreateExitFileWatcher();
                if (File.Exists(ExitFilePath))
                {
                    QueueExitRequest();
                }
            }
            catch (Exception exception)
            {
                CharacterVaultPlugin.Log.LogError(
                    $"Could not watch {ExitFilePath}; shutdown requests cannot be detected: {exception}");
            }
        }

        private void TryStartFromExitFile()
        {
            if (!CanCoordinateShutdown())
            {
                _exitRequestPending = true;
                return;
            }

            _exitRequestPending = false;
            try
            {
                if (!File.Exists(ExitFilePath))
                {
                    return;
                }

                string content = File.ReadAllText(ExitFilePath).Trim();
                if (!int.TryParse(content, out int requestedProcessId) ||
                    requestedProcessId != Process.GetCurrentProcess().Id)
                {
                    File.Delete(ExitFilePath);
                    CharacterVaultPlugin.Log.LogWarning(
                        $"Ignored a stale or invalid {ExitFilePath} request for process '{content}'.");
                    return;
                }

                File.Delete(ExitFilePath);
                Start(ZNet.instance);
            }
            catch (Exception exception)
            {
                CharacterVaultPlugin.Log.LogError(
                    $"Could not process {ExitFilePath}: {exception}");
            }
        }

        internal void RecordSaveCommitted(ZRpc peerRpc, string committedRequestId)
        {
            if (_requestId == null || committedRequestId != _requestId)
            {
                return;
            }

            ZNetPeer peer = _pendingPeers.FirstOrDefault(candidate => candidate.m_rpc == peerRpc);
            if (peer == null || !_pendingPeers.Remove(peer))
            {
                return;
            }

            _requestedPeers.Remove(peer);
            CharacterVaultPlugin.Log.LogMessage(
                $"Confirmed graceful character save for {peer.m_playerName} ({_pendingPeers.Count} remaining).");
            RequestNextProfiles();
            if (_pendingPeers.Count == 0)
            {
                Complete();
            }
        }

        private static bool CanCoordinateShutdown()
        {
            return Application.isBatchMode && ZNet.instance?.IsServer() == true;
        }

        private void Start(ZNet network)
        {
            _requestId = Guid.NewGuid().ToString("N");
            _timeoutTimer = new System.Threading.Timer(
                OnTimeoutElapsed, _requestId, TimeSpan.FromSeconds(ShutdownTimeoutSeconds),
                System.Threading.Timeout.InfiniteTimeSpan);
            foreach (ZNetPeer peer in network.GetPeers().Where(HasActiveCharacter))
            {
                _pendingPeers.Add(peer);
                _shutdownPeerRpcs.Add(peer.m_rpc);
                _queuedPeers.Enqueue(peer);
            }
            CharacterVaultPlugin.Log.LogMessage(
                $"Shutdown requested; saving {_pendingPeers.Count} connected character(s).");
            RequestNextProfiles();
            CompleteIfFinished();
        }

        internal void ProcessPendingExitRequest()
        {
            if (_exitRequestPending)
            {
                ProcessExitRequest();
            }
        }

        internal bool TryRequestShutdown()
        {
            if (_shutdownCommitted || _requestId != null ||
                !CanCoordinateShutdown())
            {
                return false;
            }
            Start(ZNet.instance);
            return true;
        }

        private void RequestNextProfiles()
        {
            while (_requestedPeers.Count < MaximumConcurrentSaves && _queuedPeers.Count > 0)
            {
                ZNetPeer peer = _queuedPeers.Dequeue();
                if (!_pendingPeers.Contains(peer) || !HasActiveCharacter(peer))
                {
                    _pendingPeers.Remove(peer);
                    continue;
                }

                _requestedPeers.Add(peer);
                CharacterVaultPlugin.Transfers.RequestSave(peer, _requestId);
            }
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _disposed, 1);
            _exitFileWatcher?.Dispose();
            _timeoutTimer?.Dispose();
            _disconnectTimer?.Dispose();
        }

        private void ProcessExitRequest()
        {
            Interlocked.Exchange(ref _exitRequestQueued, 0);
            if (Volatile.Read(ref _disposed) != 0 || _shutdownCommitted)
            {
                return;
            }

            if (_watcherFailed)
            {
                _watcherFailed = false;
                CharacterVaultPlugin.Log.LogError(
                    $"The {ExitFilePath} watcher failed; shutdown requests may no longer be detected.");
            }

            if (_requestId == null)
            {
                TryStartFromExitFile();
            }
        }

        private FileSystemWatcher CreateExitFileWatcher()
        {
            FileSystemWatcher watcher = new FileSystemWatcher(Path.GetFullPath("."), ExitFilePath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
            };
            watcher.Created += OnExitFileChanged;
            watcher.Changed += OnExitFileChanged;
            watcher.Renamed += OnExitFileRenamed;
            watcher.Error += OnWatcherError;
            watcher.EnableRaisingEvents = true;
            return watcher;
        }

        private void OnExitFileChanged(object sender, FileSystemEventArgs args)
        {
            QueueExitRequest();
        }

        private void OnExitFileRenamed(object sender, RenamedEventArgs args)
        {
            QueueExitRequest();
        }

        private void OnWatcherError(object sender, ErrorEventArgs args)
        {
            _watcherFailed = true;
            QueueExitRequest();
        }

        private void QueueExitRequest()
        {
            if (Volatile.Read(ref _disposed) == 0 &&
                Interlocked.Exchange(ref _exitRequestQueued, 1) == 0)
            {
                _unityContext.Post(_ => ProcessExitRequest(), null);
            }
        }

        private void OnTimeoutElapsed(object state)
        {
            string timedOutRequestId = (string)state;
            if (Volatile.Read(ref _disposed) == 0)
            {
                _unityContext.Post(_ => CompleteAfterTimeoutIfCurrent(timedOutRequestId), null);
            }
        }

        private void CompleteAfterTimeoutIfCurrent(string timedOutRequestId)
        {
            if (!_shutdownCommitted && timedOutRequestId == _requestId)
            {
                CompleteAfterTimeout();
            }
        }

        private void CompleteIfFinished()
        {
            if (_pendingPeers.Count == 0 && !_shutdownCommitted)
            {
                Complete();
            }
        }

        private void Complete()
        {
            CharacterVaultPlugin.Log.LogMessage(
                "All connected character profiles were written to disk; requesting normal client disconnection.");
            RequestClientDisconnects();
        }

        private void CompleteAfterTimeout()
        {
            string players = string.Join(", ", _pendingPeers.Select(peer => peer.m_playerName).ToArray());
            CharacterVaultPlugin.Log.LogWarning(
                $"The {ShutdownTimeoutSeconds}-second shutdown save timeout expired with " +
                $"{_pendingPeers.Count} unsaved character(s): {players}.");
            CharacterVaultPlugin.Log.LogWarning(
                "Requesting normal client disconnection after the character save timeout.");
            RequestClientDisconnects();
        }

        private void RequestClientDisconnects()
        {
            ZNetPeer[] peers = ConnectedShutdownPeers();
            foreach (ZNetPeer peer in peers)
            {
                peer.m_rpc.Invoke("Disconnect");
            }
            if (peers.Length == 0)
            {
                ContinueVanillaShutdown();
                return;
            }
            CharacterVaultPlugin.Log.LogMessage(
                $"Requested normal disconnection for {peers.Length} client(s); " +
                $"waiting {ClientDisconnectGraceSeconds} seconds before the server fallback.");
            _disconnectTimer = new System.Threading.Timer(
                OnDisconnectGraceElapsed, null,
                TimeSpan.FromSeconds(ClientDisconnectGraceSeconds),
                System.Threading.Timeout.InfiniteTimeSpan);
        }

        private ZNetPeer[] ConnectedShutdownPeers() =>
            (ZNet.instance?.GetPeers() ?? Enumerable.Empty<ZNetPeer>())
            .Where(peer => peer?.m_rpc != null &&
                           _shutdownPeerRpcs.Contains(peer.m_rpc))
            .ToArray();

        private void OnDisconnectGraceElapsed(object state)
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                _unityContext.Post(_ => CompleteClientDisconnects(), null);
            }
        }

        private void CompleteClientDisconnects()
        {
            ZNetPeer[] peers = ConnectedShutdownPeers();
            foreach (ZNetPeer peer in peers)
            {
                ZNet.instance.Disconnect(peer);
            }
            CharacterVaultPlugin.Log.LogMessage(
                peers.Length == 0
                    ? "All clients disconnected normally; continuing the vanilla shutdown."
                    : $"Closed {peers.Length} remaining client connection(s); continuing the vanilla shutdown.");
            ContinueVanillaShutdown();
        }

        private void ContinueVanillaShutdown()
        {
            _timeoutTimer?.Dispose();
            _timeoutTimer = null;
            _disconnectTimer?.Dispose();
            _disconnectTimer = null;
            _pendingPeers.Clear();
            _requestedPeers.Clear();
            _shutdownPeerRpcs.Clear();
            _queuedPeers.Clear();
            _requestId = null;
            _shutdownCommitted = true;
            CharacterVaultPlugin.Log.LogMessage("Starting the vanilla application shutdown.");
            CharacterVaultPlugin.Instance.QuitNextFrame();
        }

        private static bool HasActiveCharacter(ZNetPeer peer)
        {
            return peer?.m_rpc != null && peer.m_socket?.IsConnected() == true &&
                !string.IsNullOrWhiteSpace(peer.m_playerName);
        }
    }
}
