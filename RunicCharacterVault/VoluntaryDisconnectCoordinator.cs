using System;
using System.Collections;
using UnityEngine;

namespace RunicCharacterVault
{
    internal sealed class VoluntaryDisconnectCoordinator : IDisposable
    {
        private const float ConfirmationTimeoutSeconds = 10;
        private bool _allowApplicationQuit;
        private bool _allowLogout;
        private Game _game;
        private bool _logoutSave;
        private bool _logoutStartScene;
        private string _requestId;
        private VoluntaryExitKind _exitKind;
        private bool _playerEnteredWorld;

        internal bool HasPendingSave => _requestId != null;

        internal bool AllowLogout(Game game, bool save, bool changeToStartScene)
        {
            if (_allowLogout)
            {
                _allowLogout = false;
                CharacterVaultPlugin.Log.LogInfo(
                    "Allowing voluntary logout after the final character save was accepted.");
                return true;
            }

            if (!save || !Start(VoluntaryExitKind.Logout, game, save, changeToStartScene))
            {
                return true;
            }

            return false;
        }

        internal void RecordSaveCommitted(string requestId)
        {
            if (requestId != _requestId)
            {
                return;
            }

            CharacterVaultPlugin.Log.LogMessage(
                $"Final voluntary disconnect save {requestId} accepted.");
            CharacterVaultPlugin.Transfers.SuppressRedundantDisconnectUpload();
            CompletePendingExit("after the confirmed save");
        }

        internal void RecordConnectionStarted()
        {
            _playerEnteredWorld = false;
            ClearPendingRequest();
        }

        internal void RecordPlayerSpawned()
        {
            _playerEnteredWorld = true;
            CharacterVaultPlugin.Log.LogInfo(
                "CharacterVault final-save protection armed after the local player spawned.");
        }

        internal void RecordConnectionLost()
        {
            if (_requestId == null)
            {
                return;
            }

            CharacterVaultPlugin.Log.LogWarning(
                $"Connection was lost while final save {_requestId} was pending; confirmation is impossible.");
            ClearPendingRequest();
            _playerEnteredWorld = false;
        }

        internal bool AllowMenuQuit()
        {
            if (_allowApplicationQuit)
            {
                return true;
            }

            bool delayed = Start(VoluntaryExitKind.ApplicationQuit, Game.instance, true, false);
            if (delayed)
            {
                CharacterVaultPlugin.Log.LogMessage(
                    "Intercepted the in-game Quit action; waiting for the final save acceptance.");
            }
            return !delayed;
        }

        public void Dispose()
        {
            ClearPendingRequest();
        }

        private bool Start(VoluntaryExitKind kind, Game game, bool save, bool startScene)
        {
            string requestId = "disconnect-" + Guid.NewGuid().ToString("N");
            IVoluntaryExitSaveRequest request = new VoluntaryExitSaveRequest(() =>
                CharacterVaultPlugin.Transfers?.BeginFinalDisconnectSave(requestId) == true);
            VoluntaryExitSaveAction action = VoluntaryExitSavePolicy.Start(
                _playerEnteredWorld, _requestId != null, request);
            if (action != VoluntaryExitSaveAction.WaitForNewSave)
            {
                return action == VoluntaryExitSaveAction.WaitForPendingSave;
            }

            _requestId = requestId;
            _exitKind = kind;
            _game = game;
            _logoutSave = save;
            _logoutStartScene = startScene;
            CharacterVaultPlugin.Log.LogMessage(
                $"Delayed voluntary {Describe(kind)} until final save {requestId} is committed.");
            CharacterVaultPlugin.Instance.Run(WaitForConfirmation(requestId));
            return true;
        }

        private IEnumerator WaitForConfirmation(string requestId)
        {
            float deadline = Time.realtimeSinceStartup + ConfirmationTimeoutSeconds;
            while (_requestId == requestId && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (_requestId != requestId)
            {
                yield break;
            }

            CharacterVaultPlugin.Log.LogError(
                $"Allowing voluntary {Describe(_exitKind)} because final save {requestId} " +
                $"was not confirmed within {ConfirmationTimeoutSeconds:0} seconds.");
            CompletePendingExit("after the confirmation timeout");
        }

        private void CompletePendingExit(string reason)
        {
            VoluntaryExitKind exitKind = _exitKind;
            Game game = _game;
            bool logoutSave = _logoutSave;
            bool logoutStartScene = _logoutStartScene;
            ClearPendingRequest();
            if (exitKind == VoluntaryExitKind.ApplicationQuit)
            {
                _allowApplicationQuit = true;
                CharacterVaultPlugin.Log.LogInfo($"Allowing application quit {reason}.");
                Application.Quit();
                return;
            }

            _allowLogout = true;
            CharacterVaultPlugin.Log.LogInfo($"Allowing logout {reason}.");
            game.Logout(logoutSave, logoutStartScene);
        }

        private void ClearPendingRequest()
        {
            _requestId = null;
            _game = null;
        }

        private static string Describe(VoluntaryExitKind kind)
        {
            return kind == VoluntaryExitKind.ApplicationQuit ? "application quit" : "logout";
        }
    }

    internal enum VoluntaryExitKind
    {
        Logout,
        ApplicationQuit
    }

    internal sealed class VoluntaryExitSaveRequest : IVoluntaryExitSaveRequest
    {
        private readonly Func<bool> _request;

        internal VoluntaryExitSaveRequest(Func<bool> request)
        {
            _request = request;
        }

        public bool Request()
        {
            return _request();
        }
    }
}
