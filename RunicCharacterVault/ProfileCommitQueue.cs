using System;
using System.Collections.Generic;
using System.Threading;

namespace RunicCharacterVault
{
    internal sealed class ProfileCommitQueue
    {
        private readonly Queue<PendingCommit> _commits = new Queue<PendingCommit>();
        private readonly object _lock = new object();
        private readonly VaultStorage _storage;
        private readonly SynchronizationContext _unityContext;
        private readonly Action<PendingCommit> _confirm;
        private bool _workerRunning;

        internal ProfileCommitQueue(VaultStorage storage,
            SynchronizationContext unityContext, Action<PendingCommit> confirm)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _unityContext = unityContext ??
                throw new ArgumentNullException(nameof(unityContext));
            _confirm = confirm ?? throw new ArgumentNullException(nameof(confirm));
        }

        internal void Enqueue(PendingCommit commit)
        {
            lock (_lock)
            {
                _commits.Enqueue(commit);
                if (_workerRunning)
                {
                    return;
                }

                _workerRunning = true;
                ThreadPool.QueueUserWorkItem(_ => Process());
            }
        }

        private void Process()
        {
            while (TryDequeue(out PendingCommit commit))
            {
                try
                {
                    _storage.Commit(commit.Session.AccountId,
                        commit.Session.Name, commit.Data);
                    _unityContext.Post(_ => _confirm(commit), null);
                }
                catch (Exception exception)
                {
                    CharacterVaultPlugin.Log.LogError(
                        $"Character vault commit failed: {exception}");
                }
            }
        }

        private bool TryDequeue(out PendingCommit commit)
        {
            lock (_lock)
            {
                commit = _commits.Count > 0 ? _commits.Dequeue() : null;
                if (commit == null)
                {
                    _workerRunning = false;
                }
                return commit != null;
            }
        }
    }
}
