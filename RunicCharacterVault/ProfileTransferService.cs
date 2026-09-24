using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using RunicCharacterVault.Shared;
using UnityEngine;

namespace RunicCharacterVault
{
    internal sealed class ProfileTransferService : IDisposable
    {
        internal const string HelloRpc = "RunicCharacterVault_Hello_v1";
        internal const string AdmissionRpc = "RunicCharacterVault_Admission_v1";
        internal const string DownloadBeginRpc = "RunicCharacterVault_DownloadBegin_v1";
        internal const string DownloadChunkRpc = "RunicCharacterVault_DownloadChunk_v1";
        internal const string DownloadCompleteRpc = "RunicCharacterVault_DownloadComplete_v1";
        internal const string UploadBeginRpc = "RunicCharacterVault_UploadBegin_v1";
        internal const string UploadChunkRpc = "RunicCharacterVault_UploadChunk_v1";
        internal const string UploadCompleteRpc = "RunicCharacterVault_UploadComplete_v1";
        internal const string SaveRequestRpc = "RunicCharacterVault_SaveRequest_v1";
        internal const string SaveAckRpc = "RunicCharacterVault_SaveAck_v1";
        internal const string CommitAckRpc = "RunicCharacterVault_CommitAck_v1";
        private const int MaximumProfileBytes = 64 * 1024 * 1024;
        private readonly Dictionary<ZRpc, VaultSession> _sessions = new Dictionary<ZRpc, VaultSession>();
        private readonly Dictionary<ZRpc, IncomingTransfer> _uploads = new Dictionary<ZRpc, IncomingTransfer>();
        private readonly Dictionary<string, ZRpc> _enrollments = new Dictionary<string, ZRpc>(StringComparer.Ordinal);
        private readonly ClientSaveLifecycle _clientLifecycle = new ClientSaveLifecycle();
        private readonly VaultStorage _storage = new VaultStorage();
        private readonly CharacterAdmissionEvaluator _admission;
        private readonly ProfileCommitQueue _commits;
        private readonly ServerFinalSaveMonitor _finalSaveMonitor = new ServerFinalSaveMonitor();
        private IncomingTransfer _download;
        private bool _clientUploadBusy;
        private bool _suppressNextClientUpload;
        private string _pendingRequest;
        private PlayerProfile _pendingProfile;
        private IReadOnlyList<StartingItem> _serverStartingItems = Array.Empty<StartingItem>();

        internal ProfileTransferService(SynchronizationContext unityContext)
        {
            if (unityContext == null)
            {
                throw new ArgumentNullException(nameof(unityContext));
            }
            _admission = new CharacterAdmissionEvaluator(_storage);
            _commits = new ProfileCommitQueue(_storage, unityContext,
                ConfirmBackgroundCommit);
        }

        internal void Register(ZNet network, ZNetPeer peer)
        {
            if (network.IsServer())
            {
                RegisterServer(peer.m_rpc);
            }
            else
            {
                RegisterClient(peer.m_rpc);
            }
        }

        internal void SendHello(ZRpc serverRpc)
        {
            PlayerProfile profile = Game.instance?.GetPlayerProfile();
            if (profile == null)
            {
                return;
            }

            ZPackage package = new ZPackage();
            package.Write(profile.GetPlayerID());
            package.Write(profile.GetName());
            bool newCharacter = NewCharacterPolicy.HasNeverJoinedAWorld(profile);
            if (!newCharacter) ProfileFile.BackupSelectedProfile(profile, profile.GetPath());
            package.Write(newCharacter);
            serverRpc.Invoke(HelloRpc, package);
        }

        internal bool Approve(ZRpc rpc)
        {
            if (!_sessions.TryGetValue(rpc, out VaultSession session))
            {
                Reject(rpc, global::Runic.Localization.RunicText.Get("text_61c64500b935"));
                return false;
            }

            if (session.State.Verified)
            {
                return session.State.Admitted;
            }

            session.State.Verified = true;
            if (_storage.TryRead(session.AccountId, session.Name, out byte[] data))
            {
                SendDownload(rpc, session, data);
                session.State.Admitted = true;
                return true;
            }

            CharacterRestoreResult restored = TryRestore(session);
            if (restored?.Status == CharacterRestoreStatus.Restored)
            {
                if (!ValidateProfile(rpc, session, restored.Profile)) return false;
                _storage.Commit(session.AccountId, session.Name, restored.Profile);
                SendDownload(rpc, session, restored.Profile);
                session.State.Admitted = true;
                return true;
            }
            if (restored?.Status == CharacterRestoreStatus.Failed)
            {
                _sessions.Remove(rpc);
                Reject(rpc, global::Runic.Localization.RunicText.Get("text_375a0a56d618"));
                return false;
            }

            session.State.Admitted = AdmitEnrollment(rpc, session);
            return session.State.Admitted;
        }

        internal void RecordPermission(string hostName, bool permitted)
        {
            foreach (VaultSession session in _sessions.Values.Where(candidate =>
                string.Equals(candidate.AccountId, hostName,
                    StringComparison.Ordinal)))
            {
                session.State.RecordPermission(permitted);
            }
        }

        internal void Remove(ZNetPeer peer)
        {
            if (peer?.m_rpc == null)
            {
                return;
            }

            if (_sessions.TryGetValue(peer.m_rpc, out VaultSession session))
            {
                _finalSaveMonitor.RecordRemoved(peer.m_rpc, session.Name);
            }
            _sessions.Remove(peer.m_rpc);
            _uploads.Remove(peer.m_rpc);
            ReleaseEnrollment(peer.m_rpc);
            CharacterVaultPlugin.ServerDisconnects?.RecordDisconnected(peer.m_rpc);
            if (ZNet.instance?.IsServer() == false)
            {
                ResetClientState();
            }
        }

        internal string DescribeDisconnect(ZNetPeer peer, bool server)
        {
            ZRpc rpc = peer?.m_rpc;
            VaultSession session = null;
            bool tracked = rpc != null && _sessions.TryGetValue(rpc, out session);
            ServerProfileSessionState state = tracked ? session.State : null;
            bool pendingSave = server
                ? CharacterVaultPlugin.ServerDisconnects?.HasPendingSave(rpc) == true
                : CharacterVaultPlugin.DisconnectCoordinator?.HasPendingSave == true;
            return global::Runic.Localization.RunicText.Format("text_b1fab7769201", (server ? "server" : "client"), peer?.IsReady() == true) +
                global::Runic.Localization.RunicText.Format("text_adfbf221e4c4", tracked, state?.Verified == true) +
                global::Runic.Localization.RunicText.Format("text_3f23f65911bb", state?.Admitted == true, state?.PermissionChecked == true) +
                global::Runic.Localization.RunicText.Format("text_9bb2ff594060", state?.Permitted == true, state?.CanSave == true) +
                global::Runic.Localization.RunicText.Format("text_154964a5398a", _clientLifecycle.IsActive, _clientLifecycle.IsEnrolling) +
                global::Runic.Localization.RunicText.Format("text_46b015aabed8", _clientLifecycle.HasSpawned, _clientUploadBusy) +
                $"incomingUpload={rpc != null && _uploads.ContainsKey(rpc)}, " +
                global::Runic.Localization.RunicText.Format("text_1abebbbbe6c1", _download != null, pendingSave);
        }

        private static CharacterRestoreResult TryRestore(VaultSession session)
        {
            ICharacterRestoreProvider provider = CharacterRestoreApi.GetProvider();
            if (provider == null) return null;
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                try
                {
                    CharacterRestoreResult result = provider.RestoreAsync(
                        session.AccountId, session.Name, timeout.Token).GetAwaiter().GetResult();
                    if (result?.Status == CharacterRestoreStatus.Restored &&
                        (result.Profile == null || result.Profile.Length == 0 ||
                         result.Profile.Length > MaximumProfileBytes)) return CharacterRestoreResult.Failed();
                    return result ?? CharacterRestoreResult.Failed();
                }
                catch (Exception exception)
                {
                    CharacterVaultPlugin.Log.LogWarning("Character restore failed: " + exception);
                    return CharacterRestoreResult.Failed();
                }
            }
        }

        internal void MonitorFinalSaves()
        {
            if (ZNet.instance?.IsServer() != true)
            {
                return;
            }
            foreach (KeyValuePair<ZRpc, VaultSession> pair in _sessions)
            {
                if (pair.Value.State.CanSave)
                {
                    _finalSaveMonitor.Observe(pair.Key, pair.Value.Name,
                        pair.Key.GetSocket()?.IsConnected() == true);
                }
            }
            _finalSaveMonitor.Update();
        }

        internal void RequestWorldCheckpoint()
        {
            if (ZNet.instance?.IsServer() != true)
            {
                return;
            }

            string request = "world-" + Guid.NewGuid().ToString("N");
            foreach (ZNetPeer peer in ZNet.instance.GetPeers().Where(IsReady))
            {
                RequestSave(peer, request);
            }
        }

        internal void RequestSave(ZNetPeer peer, string requestId)
        {
            if (peer?.m_rpc != null && CanSave(_sessions, peer.m_rpc))
            {
                peer.m_rpc.Invoke(SaveRequestRpc, requestId);
            }
        }

        internal bool CanRequestSave(ZNetPeer peer)
        {
            return peer?.m_rpc != null && CanSave(_sessions, peer.m_rpc);
        }

        internal KickSaveEligibility GetKickSaveEligibility(ZNetPeer peer)
        {
            if (peer?.m_rpc != null &&
                (!peer.IsReady() || peer.m_characterID.IsNone()))
            {
                return KickSaveEligibility.Rejected;
            }
            if (peer?.m_rpc == null || !_sessions.TryGetValue(peer.m_rpc, out VaultSession session))
            {
                return KickSaveEligibility.Unmanaged;
            }
            if (session.State.CanSave)
            {
                return KickSaveEligibility.SaveRequired;
            }
            return session.State.Verified && session.State.Admitted &&
                session.State.PermissionChecked && !session.State.Permitted
                ? KickSaveEligibility.Rejected : KickSaveEligibility.Unmanaged;
        }

        internal bool SaveManualClientProfile()
        {
            if (!_clientLifecycle.IsActive || ZNet.instance?.IsServer() != false || Game.instance == null)
            {
                return false;
            }

            Game.instance.SavePlayerProfile(true);
            return true;
        }

        internal void UploadSavedProfile(PlayerProfile profile)
        {
            if (_suppressNextClientUpload)
            {
                _suppressNextClientUpload = false;
                CharacterVaultPlugin.Log.LogInfo(
                    "Skipped the redundant local save upload after a confirmed voluntary disconnect save.");
                return;
            }

            if (!_clientLifecycle.IsActive || ZNet.instance?.IsServer() != false)
            {
                return;
            }

            if (!_clientLifecycle.CanUpload)
            {
                CharacterVaultPlugin.Log.LogInfo(
                    "Skipped the server upload for a local save before Player.OnSpawned completed.");
                return;
            }

            ZRpc serverRpc = ZNet.instance.GetServerRPC();
            if (serverRpc == null)
            {
                ResetClientState();
                return;
            }

            string request = _pendingRequest ?? "save-" + Guid.NewGuid().ToString("N");
            _pendingRequest = null;
            if (_clientUploadBusy)
            {
                _pendingRequest = request;
                CharacterVaultPlugin.Log.LogInfo(
                    $"Queued character save request {request} while another upload is awaiting confirmation.");
                return;
            }

            byte[] data = ProfileFile.Read(profile);
            _clientUploadBusy = true;
            CharacterVaultPlugin.SaveStatus?.ShowSaving(request);
            CharacterVaultPlugin.Log.LogInfo(
                $"Uploading character profile {profile.GetName()} for save request {request}.");
            CharacterVaultPlugin.Instance.Run(SendUpload(serverRpc, profile, data, request));
        }

        internal bool BeginFinalDisconnectSave(string requestId)
        {
            PlayerProfile profile = Game.instance?.GetPlayerProfile();
            if (!_clientLifecycle.IsActive || ZNet.instance?.IsServer() != false || profile == null)
            {
                return false;
            }

            _pendingRequest = requestId;
            if (_clientUploadBusy)
            {
                CharacterVaultPlugin.Log.LogInfo(
                    $"Final save request {requestId} is waiting for the active upload to finish.");
                return true;
            }

            CharacterVaultPlugin.Log.LogInfo(
                $"Writing the final local profile for {profile.GetName()} before disconnect.");
            Game.instance.SavePlayerProfile(true);
            return true;
        }

        internal void SuppressRedundantDisconnectUpload()
        {
            _suppressNextClientUpload = true;
        }

        internal void RecordPlayerSpawned(Player player)
        {
            if (player == null || player != Player.m_localPlayer)
            {
                return;
            }

            StartingItemGrantPolicy.ApplyEnrollment(_clientLifecycle, true,
                _serverStartingItems, FindItem, (prefab, quantity) =>
                    player.GetInventory().AddItem(prefab, quantity),
                () => Game.instance.SavePlayerProfile(true), item =>
                    CharacterVaultPlugin.Log.LogError(
                        $"Could not grant starting item {item.Prefab}:{item.Quantity}."));
        }

        internal void ApplyPendingProfile(ref PlayerProfile profile)
        {
            if (_pendingProfile == null)
            {
                return;
            }

            profile = _pendingProfile;
            _pendingProfile = null;
        }

        public void Dispose()
        {
            _sessions.Clear();
            _uploads.Clear();
            _enrollments.Clear();
            _finalSaveMonitor.Clear();
            _download = null;
        }

        private void RegisterServer(ZRpc rpc)
        {
            CharacterVaultPlugin.Settings.InitializeServer();
            CharacterVaultRejection.RegisterServer(rpc);
            rpc.Register<ZPackage>(HelloRpc, ReceiveHello);
            rpc.Register<ZPackage>(UploadBeginRpc, ReceiveUploadBegin);
            rpc.Register<ZPackage>(UploadChunkRpc, ReceiveUploadChunk);
            rpc.Register<ZPackage>(UploadCompleteRpc, ReceiveUploadComplete);
        }

        private void RegisterClient(ZRpc rpc)
        {
            ResetClientState();
            CharacterVaultPlugin.DisconnectCoordinator?.RecordConnectionStarted();
            CharacterVaultRejection.RegisterClient(rpc);
            rpc.Register<ZPackage>(AdmissionRpc, ReceiveAdmission);
            rpc.Register<ZPackage>(DownloadBeginRpc, ReceiveDownloadBegin);
            rpc.Register<ZPackage>(DownloadChunkRpc, ReceiveDownloadChunk);
            rpc.Register<ZPackage>(DownloadCompleteRpc, ReceiveDownloadComplete);
            rpc.Register<string>(SaveRequestRpc, ReceiveSaveRequest);
            rpc.Register<string>(SaveAckRpc, ReceiveSaveAck);
            rpc.Register<string>(CommitAckRpc, ReceiveCommitAck);
        }

        private void ReceiveHello(ZRpc rpc, ZPackage package)
        {
            long characterId = package.ReadLong();
            string name = package.ReadString();
            bool newCharacter = package.ReadBool();
            string accountId = rpc.GetSocket().GetHostName();
            _sessions[rpc] = new VaultSession(accountId, characterId, name, newCharacter);
        }

        private bool AdmitEnrollment(ZRpc rpc, VaultSession session)
        {
            bool allowMultiple = CharacterVaultPlugin.Settings.AllowMultipleCharacters;
            // Use the authenticated socket identity, never a client-supplied admin flag.
            ZNet network = ZNet.instance;
            bool isServerAdmin = network != null && network.IsServer() && network.IsAdmin(session.AccountId);
            CharacterAdmission admission = _admission.Decide(false, session.AccountId,
                session.NewCharacter, allowMultiple, true,
                CharacterVaultPlugin.Settings.AllowExistingCharacters, isServerAdmin);
            if (admission == CharacterAdmission.NewEnrollment && !ReserveEnrollment(rpc, session))
            {
                admission = CharacterAdmission.RejectConcurrentEnrollment;
            }
            if (admission != CharacterAdmission.NewEnrollment)
            {
                _sessions.Remove(rpc);
                Reject(rpc, CharacterAdmissionMessages.ForRejection(admission,
                    _storage.GetProfileNames(session.AccountId)));
                return false;
            }

            session.Enrolling = true;
            ZPackage response = new ZPackage();
            response.Write(session.CharacterId);
            IReadOnlyList<StartingItem> startingItems = StartingItemGrantPolicy.ForEnrollment(
                session.NewCharacter, CharacterVaultPlugin.Settings.StartingItems);
            response.Write(startingItems.Count);
            foreach (StartingItem item in startingItems)
            {
                response.Write(item.Prefab);
                response.Write(item.Quantity);
            }
            rpc.Invoke(AdmissionRpc, response);
            CharacterVaultPlugin.Log.LogInfo(session.NewCharacter
                ? "New character enrollment accepted; awaiting the first validated save."
                : "Existing character enrollment accepted without starter items; awaiting the first validated save.");
            return true;
        }

        private void SendDownload(ZRpc rpc, VaultSession session, byte[] data)
        {
            string transferId = Guid.NewGuid().ToString("N");
            string hash = VaultStorage.Hash(data);
            rpc.Invoke(DownloadBeginRpc, ProfileTransferProtocol.Begin(transferId, data.Length, hash));
            for (int offset = 0; offset < data.Length; offset += ProfileTransferProtocol.ChunkSize)
            {
                rpc.Invoke(DownloadChunkRpc, ProfileTransferProtocol.Chunk(transferId, data, offset));
            }

            ZPackage complete = new ZPackage();
            complete.Write(transferId);
            rpc.Invoke(DownloadCompleteRpc, complete);
        }

        private void ReceiveAdmission(ZRpc rpc, ZPackage package)
        {
            long characterId = package.ReadLong();
            if (Game.instance.GetPlayerProfile().GetPlayerID() != characterId)
            {
                throw new InvalidDataException("The server admitted a different character.");
            }

            int count = package.ReadInt();
            List<StartingItem> items = new List<StartingItem>(count);
            for (int index = 0; index < count; index++)
            {
                items.Add(new StartingItem(package.ReadString(), package.ReadInt()));
            }

            _serverStartingItems = items;
            _clientLifecycle.BeginEnrollment();
        }

        private void ReceiveDownloadBegin(ZRpc rpc, ZPackage package)
        {
            _download = IncomingTransfer.Create(package, MaximumProfileBytes);
        }

        private void ReceiveDownloadChunk(ZRpc rpc, ZPackage package)
        {
            _download?.Add(package);
        }

        private void ReceiveDownloadComplete(ZRpc rpc, ZPackage package)
        {
            string transferId = package.ReadString();
            byte[] data = _download?.Complete(transferId);
            _download = null;
            if (data == null)
            {
                throw new InvalidDataException("The authoritative profile transfer was incomplete.");
            }

            _pendingProfile = ProfileFile.ReplaceSelected(data);
            _clientLifecycle.ActivateExisting();
        }

        private void ReceiveSaveRequest(ZRpc rpc, string requestId)
        {
            if (!_clientLifecycle.IsActive || string.IsNullOrWhiteSpace(requestId))
            {
                return;
            }

            _pendingRequest = requestId;
            if (!_clientUploadBusy)
            {
                Game.instance.SavePlayerProfile(true);
            }
        }

        private void ReceiveSaveAck(ZRpc rpc, string requestId)
        {
            _clientUploadBusy = false;
            CharacterVaultPlugin.SaveStatus?.ShowAccepted(requestId);
            CharacterVaultPlugin.Log.LogInfo(
                $"Server accepted character save request {requestId}.");
            CharacterVaultPlugin.DisconnectCoordinator?.RecordSaveCommitted(requestId);
            if (_pendingRequest != null)
            {
                Game.instance.SavePlayerProfile(true);
            }
        }

        private void ReceiveCommitAck(ZRpc rpc, string requestId)
        {
            CharacterVaultPlugin.SaveStatus?.ShowCommitted(requestId);
            CharacterVaultPlugin.Log.LogInfo(
                $"Server confirmed durable character save request {requestId}.");
        }

        private void ResetClientState()
        {
            _clientLifecycle.Reset();
            _clientUploadBusy = false;
            _suppressNextClientUpload = false;
            _pendingRequest = null;
            _pendingProfile = null;
            CharacterVaultPlugin.SaveStatus?.Hide();
            CharacterVaultPlugin.DisconnectCoordinator?.RecordConnectionLost();
        }

        private IEnumerator SendUpload(
            ZRpc rpc, PlayerProfile profile, byte[] data, string requestId)
        {
            string transferId = Guid.NewGuid().ToString("N");
            bool sent = false;
            try
            {
                ZPackage begin = ProfileTransferProtocol.Begin(
                    transferId, data.Length, VaultStorage.Hash(data));
                begin.Write(requestId);
                begin.Write(profile.GetPlayerID());
                rpc.Invoke(UploadBeginRpc, begin);
                for (int offset = 0; offset < data.Length; offset += ProfileTransferProtocol.ChunkSize)
                {
                    rpc.Invoke(UploadChunkRpc, ProfileTransferProtocol.Chunk(transferId, data, offset));
                    yield return null;
                }

                ZPackage complete = new ZPackage();
                complete.Write(transferId);
                rpc.Invoke(UploadCompleteRpc, complete);
                sent = true;
            }
            finally
            {
                if (!sent && ZNet.instance?.GetServerRPC() == rpc)
                {
                    _clientUploadBusy = false;
                    CharacterVaultPlugin.Log.LogWarning(
                        $"Character save upload {requestId} was interrupted before completion.");
                }
            }
        }

        private void ReceiveUploadBegin(ZRpc rpc, ZPackage package)
        {
            if (!TryGetVerifiedSession(rpc, out VaultSession session))
            {
                return;
            }

            IncomingTransfer transfer = IncomingTransfer.Create(package, MaximumProfileBytes);
            transfer.RequestId = package.ReadString();
            long characterId = package.ReadLong();
            if (characterId != session.CharacterId)
            {
                throw new InvalidDataException("A peer attempted to save a different character.");
            }

            _uploads[rpc] = transfer;
        }

        private void ReceiveUploadChunk(ZRpc rpc, ZPackage package)
        {
            if (_uploads.TryGetValue(rpc, out IncomingTransfer transfer))
            {
                transfer.Add(package);
            }
        }

        private void ReceiveUploadComplete(ZRpc rpc, ZPackage package)
        {
            string transferId = package.ReadString();
            if (!_uploads.TryGetValue(rpc, out IncomingTransfer transfer) ||
                !TryGetVerifiedSession(rpc, out VaultSession session))
            {
                return;
            }

            _uploads.Remove(rpc);
            byte[] data = transfer.Complete(transferId);
            if (!ValidateProfile(rpc, session, data)) return;
            _finalSaveMonitor.RecordSaveReceived(rpc, transfer.RequestId);
            if (!SaveAcknowledgementPolicy.CanAcknowledge(session.State))
            {
                CharacterVaultPlugin.Log.LogWarning(
                    $"Rejected character save {transfer.RequestId} for {session.Name}: " +
                    "the player is not permitted to save on this server.");
                return;
            }
            if (session.Enrolling)
            {
                _storage.CommitEnrollment(session.AccountId, session.Name, data);
                ConfirmCommit(rpc, session, transfer.RequestId);
                return;
            }

            ConfirmReceipt(rpc, session, transfer.RequestId);
            QueueCommit(rpc, session, transfer.RequestId, data);
        }

        private void ConfirmReceipt(ZRpc rpc, VaultSession session, string requestId)
        {
            rpc.Invoke(SaveAckRpc, requestId);
            CharacterVaultPlugin.Log.LogMessage(
                $"Accepted character profile for {session.Name} for request {requestId}; " +
                "durable commit queued.");
        }

        private void QueueCommit(ZRpc rpc, VaultSession session, string requestId, byte[] data)
        {
            _commits.Enqueue(new PendingCommit(rpc, session, requestId, data));
        }

        private void ConfirmBackgroundCommit(PendingCommit commit)
        {
            CharacterVaultPlugin.Log.LogMessage(
                $"Committed character profile for {commit.Session.Name} " +
                $"for request {commit.RequestId}.");
            if (!_sessions.TryGetValue(commit.Rpc, out VaultSession current) ||
                current != commit.Session)
            {
                return;
            }
            commit.Rpc.Invoke(CommitAckRpc, commit.RequestId);
            CharacterVaultPlugin.Coordinator?.RecordSaveCommitted(
                commit.Rpc, commit.RequestId);
            CharacterVaultPlugin.ServerDisconnects?.RecordCommitted(
                commit.Rpc, commit.RequestId);
        }

        private void ConfirmCommit(ZRpc rpc, VaultSession session, string requestId)
        {
            if (!_sessions.TryGetValue(rpc, out VaultSession current) || current != session)
            {
                return;
            }

            session.Enrolling = false;
            ReleaseEnrollment(rpc);
            rpc.Invoke(SaveAckRpc, requestId);
            rpc.Invoke(CommitAckRpc, requestId);
            CharacterVaultPlugin.Log.LogMessage(
                $"Saved character profile for {session.Name} for request {requestId}.");
            CharacterVaultPlugin.Coordinator?.RecordSaveCommitted(rpc, requestId);
            CharacterVaultPlugin.ServerDisconnects?.RecordCommitted(rpc, requestId);
        }

        private bool TryGetVerifiedSession(ZRpc rpc, out VaultSession session)
        {
            session = null;
            return _sessions.TryGetValue(rpc, out session) && session.State.CanSave;
        }

        private bool ValidateProfile(ZRpc rpc, VaultSession session, byte[] data)
        {
            try
            {
                ProfileUploadValidator.Validate(session, data);
                return true;
            }
            catch (InvalidDataException exception)
            {
                CharacterVaultPlugin.Log.LogError(
                    $"Character profile validation failed for {session.Name}: {exception}");
                _sessions.Remove(rpc);
                _uploads.Remove(rpc);
                ReleaseEnrollment(rpc);
                CharacterVaultRejection.Reject(rpc,
                    global::Runic.Localization.RunicText.Get("text_b00f6fb0efd0"),
                    exception.Message);
                return false;
            }
        }

        private bool ReserveEnrollment(ZRpc rpc, VaultSession session)
        {
            if (_enrollments.TryGetValue(session.AccountId, out ZRpc existing) && existing != rpc)
            {
                return false;
            }

            _enrollments[session.AccountId] = rpc;
            return true;
        }

        private void ReleaseEnrollment(ZRpc rpc)
        {
            string account = _enrollments.FirstOrDefault(pair => pair.Value == rpc).Key;
            if (account != null)
            {
                _enrollments.Remove(account);
            }
        }

        private static void Reject(ZRpc rpc, string message)
        {
            CharacterVaultRejection.Reject(rpc, message);
        }

        private static bool IsReady(ZNetPeer peer)
        {
            return peer?.m_rpc != null && peer.IsReady() && peer.m_socket?.IsConnected() == true;
        }

        private static bool CanSave(Dictionary<ZRpc, VaultSession> sessions, ZRpc rpc)
        {
            return sessions.TryGetValue(rpc, out VaultSession session) && session.State.CanSave;
        }

        private static GameObject FindItem(string name)
        {
            return ObjectDB.instance?.m_items.FirstOrDefault(item =>
                string.Equals(item.name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

}
