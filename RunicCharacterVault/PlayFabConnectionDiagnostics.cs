using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using HarmonyLib;
using PlayFab;
using PlayFab.MultiplayerModels;
using PlayFab.Party;

namespace RunicCharacterVault
{
    internal static class PlayFabVerboseDiagnostics
    {
        private static bool logged;

        internal static void Enable()
        {
            PlayFabMultiplayerManager manager = PlayFabMultiplayerManager.Get();
            if (manager == null)
            {
                return;
            }
            CharacterVaultLeaveNetworkDiagnosticsPatch.Observe(manager);
            PlayFabEndpointDiagnostics.Observe(manager);
            if (!CharacterVaultPlugin.PlayFabVerboseLogging)
            {
                return;
            }
            manager.LogLevel = PlayFabMultiplayerManager.LogLevelType.Verbose;
            if (!logged)
            {
                logged = true;
                CharacterVaultPlugin.Log?.LogDebug("Verbose PlayFab Party logging is enabled.");
            }
        }
    }

    internal static class PlayFabConnectionDiagnostics
    {
        private static readonly ConditionalWeakTable<ZPlayFabSocket, Attempt> Attempts =
            new ConditionalWeakTable<ZPlayFabSocket, Attempt>();
        private static int nextAttempt;
        private static string selectedOrigin = "unknown";
        private static WeakReference currentClientSocket;

        internal static void Start(ZPlayFabSocket socket)
        {
            CharacterVaultRejection.ClearPendingClientMessage();
            var attempt = new Attempt(Interlocked.Increment(ref nextAttempt), selectedOrigin);
            Attempts.Add(socket, attempt);
            currentClientSocket = new WeakReference(socket);
            selectedOrigin = "unknown";
            CharacterVaultPlugin.Log?.LogDebug(
                $"PlayFab connection attempt {attempt.Id} started; origin={attempt.Origin}.");
        }

        internal static void SelectOrigin(string origin) => selectedOrigin = origin ?? "unknown";

        internal static void SessionFound(ZPlayFabSocket socket, PlayFabMatchmakingServerData server)
        {
            if (!Attempts.TryGetValue(socket, out Attempt attempt)) return;
            CharacterVaultPlugin.Log?.LogDebug(
                $"PlayFab connection attempt {attempt.Id} resolved lobby={Fingerprint(server?.lobbyId)}, " +
                $"network={Fingerprint(server?.networkId)}.");
        }

        internal static void NetworkJoined(ZPlayFabSocket socket, string networkId)
        {
            if (!Attempts.TryGetValue(socket, out Attempt attempt)) return;
            CharacterVaultPlugin.Log?.LogDebug(
                $"PlayFab connection attempt {attempt.Id} joined network={Fingerprint(networkId)}.");
        }

        internal static void Connected(ZPlayFabSocket socket)
        {
            if (!Attempts.TryGetValue(socket, out Attempt attempt) || attempt.Connected) return;
            attempt.Connected = true;
            CharacterVaultPlugin.Log?.LogDebug(
                $"PlayFab connection attempt {attempt.Id} established the remote transport.");
        }

        internal static void Admitted()
        {
            ZPlayFabSocket socket = currentClientSocket?.Target as ZPlayFabSocket;
            if (socket == null || !Attempts.TryGetValue(socket, out Attempt attempt) || attempt.Admitted) return;
            attempt.Admitted = true;
            attempt.Failures.Clear();
            CharacterVaultRejection.ClearPendingClientMessage();
            CharacterVaultPlugin.Log?.LogDebug(
                $"PlayFab connection attempt {attempt.Id} completed the Valheim peer handshake.");
        }

        internal static void Failed(ZPlayFabSocket socket, ZPLayFabMatchmakingFailReason reason)
        {
            if (!Attempts.TryGetValue(socket, out Attempt attempt)) return;
            attempt.Failed = true;
            if (attempt.Failures.Count == 0)
            {
                attempt.AddFailure(
                    PlayFabConnectionErrorMessages.ForMatchmaking(reason), reason.ToString());
            }
            CharacterVaultPlugin.Log?.LogWarning(
                $"PlayFab connection attempt {attempt.Id} failed while locating the network: {reason}.");
        }

        internal static void Closed(ZPlayFabSocket socket)
        {
            if (!Attempts.TryGetValue(socket, out Attempt attempt)) return;
            string outcome = attempt.Failed ? "failed" : attempt.Admitted ? "admitted" :
                attempt.Connected ? "transport-only" : "incomplete";
            CharacterVaultPlugin.Log?.LogDebug(
                $"PlayFab connection attempt {attempt.Id} socket closed; outcome={outcome}, " +
                $"status={ZNet.GetConnectionStatus()}.");
            if (!attempt.Admitted) PublishFailures(attempt);
            Attempts.Remove(socket);
            if (ReferenceEquals(currentClientSocket?.Target, socket)) currentClientSocket = null;
        }

        internal static void LobbyFailure(string stage, PlayFabError error, string lobbyId = null)
        {
            if (error?.Error == PlayFabErrorCode.LobbyPlayerAlreadyJoined)
            {
                CharacterVaultPlugin.Log?.LogDebug(
                    $"PlayFab lobby stage {stage} reported an existing membership for " +
                    $"lobby={Fingerprint(lobbyId)}; continuing.");
                return;
            }
            RecordCurrentFailure(
                PlayFabConnectionErrorMessages.ForApi(error), DescribeApiError(stage, error));
            CharacterVaultPlugin.Log?.LogWarning(
                $"PlayFab lobby stage {stage} failed for lobby={Fingerprint(lobbyId)}: " +
                $"code={error?.Error}, http={error?.HttpCode}, message={error?.ErrorMessage ?? "unavailable"}.");
        }

        internal static void ManagerError(int code, string systemMessage)
        {
            RecordCurrentFailure(
                PlayFabConnectionErrorMessages.ForParty(code), systemMessage ?? $"PlayFab Party error {code}");
        }

        internal static void PublishBlockingFailure()
        {
            ZPlayFabSocket socket = currentClientSocket?.Target as ZPlayFabSocket;
            if (socket == null || !Attempts.TryGetValue(socket, out Attempt attempt) ||
                attempt.Admitted || attempt.Failures.Count == 0)
            {
                return;
            }
            PublishFailures(attempt);
            CharacterVaultPlugin.Log?.LogWarning(
                $"Published {attempt.Failures.Count} blocking PlayFab error(s) for " +
                $"connection attempt {attempt.Id}.");
        }

        private static void RecordCurrentFailure(string userMessage, string systemMessage)
        {
            ZPlayFabSocket socket = currentClientSocket?.Target as ZPlayFabSocket;
            if (socket != null && Attempts.TryGetValue(socket, out Attempt attempt) && !attempt.Admitted)
            {
                attempt.AddFailure(userMessage, systemMessage);
            }
        }

        private static void PublishFailures(Attempt attempt)
        {
            if (attempt.FailuresPublished) return;
            foreach (Tuple<string, string> failure in attempt.Failures)
                CharacterVaultRejection.SetClientMessage(failure.Item1, failure.Item2);
            attempt.FailuresPublished = true;
        }

        private static string DescribeApiError(string stage, PlayFabError error)
        {
            return $"PlayFab {stage} failed: code={error?.Error}, http={error?.HttpCode}, " +
                $"message={error?.ErrorMessage ?? "unavailable"}";
        }

        internal static string Fingerprint(string value)
        {
            if (string.IsNullOrEmpty(value)) return "none";
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                return BitConverter.ToString(hash, 0, 4).Replace("-", string.Empty);
            }
        }

        private sealed class Attempt
        {
            internal Attempt(int id, string origin)
            {
                Id = id;
                Origin = origin;
            }
            internal int Id { get; }
            internal string Origin { get; }
            internal bool Connected { get; set; }
            internal bool Admitted { get; set; }
            internal bool Failed { get; set; }
            internal List<Tuple<string, string>> Failures { get; } =
                new List<Tuple<string, string>>();
            internal bool FailuresPublished { get; set; }

            internal void AddFailure(string userMessage, string systemMessage)
            {
                var failure = Tuple.Create(userMessage, systemMessage);
                if (!Failures.Contains(failure)) Failures.Add(failure);
            }
        }
    }

    [HarmonyPatch(typeof(ZPlayFabSocket), MethodType.Constructor,
        typeof(string), typeof(Action<PlayFabMatchmakingServerData>))]
    internal static class PlayFabClientSocketCreatedPatch
    {
        private static void Postfix(ZPlayFabSocket __instance)
        {
            PlayFabVerboseDiagnostics.Enable();
            PlayFabConnectionDiagnostics.Start(__instance);
        }
    }

    [HarmonyPatch(typeof(ZPlayFabSocket), MethodType.Constructor)]
    internal static class PlayFabServerSocketCreatedPatch
    {
        private static void Postfix() => PlayFabVerboseDiagnostics.Enable();
    }

    [HarmonyPatch(typeof(ZPlayFabSocket), "ClientConnect")]
    internal static class PlayFabClientConnectVerboseLoggingPatch
    {
        private static void Prefix() => PlayFabVerboseDiagnostics.Enable();
    }

    [HarmonyPatch(typeof(ZPlayFabSocket), "OnRemotePlayerSessionFound")]
    internal static class PlayFabSessionFoundPatch
    {
        private static void Prefix(ZPlayFabSocket __instance, PlayFabMatchmakingServerData serverData) =>
            PlayFabConnectionDiagnostics.SessionFound(__instance, serverData);
    }

    [HarmonyPatch(typeof(ZPlayFabSocket), "OnRemotePlayerNotFound")]
    internal static class PlayFabSessionNotFoundPatch
    {
        private static void Prefix(ZPlayFabSocket __instance, ZPLayFabMatchmakingFailReason failReason) =>
            PlayFabConnectionDiagnostics.Failed(__instance, failReason);
    }

    [HarmonyPatch(typeof(ZPlayFabSocket), "OnNetworkJoined")]
    internal static class PlayFabNetworkJoinedPatch
    {
        private static void Prefix(ZPlayFabSocket __instance, string networkId) =>
            PlayFabConnectionDiagnostics.NetworkJoined(__instance, networkId);
    }

    [HarmonyPatch(typeof(ZPlayFabSocket), "Connect")]
    internal static class PlayFabTransportConnectedPatch
    {
        private static void Postfix(ZPlayFabSocket __instance) =>
            PlayFabConnectionDiagnostics.Connected(__instance);
    }

    [HarmonyPatch(typeof(ZPlayFabSocket), "Dispose")]
    internal static class PlayFabSocketDisposedPatch
    {
        private static void Prefix(ZPlayFabSocket __instance) =>
            PlayFabConnectionDiagnostics.Closed(__instance);
    }

    [HarmonyPatch(typeof(ZPlayFabLobbySearch), "OnJoinLobbyFailed")]
    internal static class PlayFabJoinLobbyFailedPatch
    {
        private static void Prefix(PlayFabError error, string lobbyId) =>
            PlayFabConnectionDiagnostics.LobbyFailure("JoinLobby", error, lobbyId);
    }

    [HarmonyPatch(typeof(ZPlayFabLobbySearch), "OnGetLobbyFailed")]
    internal static class PlayFabGetLobbyFailedPatch
    {
        private static void Prefix(PlayFabError error) =>
            PlayFabConnectionDiagnostics.LobbyFailure("GetLobby", error);
    }

    [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
    internal static class PlayFabPeerHandshakeCompletedPatch
    {
        private static void Postfix(bool ___m_isServer)
        {
            if (!___m_isServer && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected)
                PlayFabConnectionDiagnostics.Admitted();
        }
    }

    [HarmonyPatch(typeof(ServerListGui), "OnSelectedServer")]
    internal static class PlayFabServerListOriginPatch
    {
        private static void Prefix(List<IServerList> ___m_serverLists, int ___m_currentServerList,
            LocalServerList ___m_favoriteServersList, LocalServerList ___m_recentServersList)
        {
            if (___m_currentServerList < 0 || ___m_currentServerList >= ___m_serverLists.Count) return;
            IServerList current = ___m_serverLists[___m_currentServerList];
            string origin = ReferenceEquals(current, ___m_favoriteServersList) ? "favorite" :
                ReferenceEquals(current, ___m_recentServersList) ? "recent" :
                current is FriendsServerList ? "friends" :
                current is CommunityServerList ? "community" : "server-list";
            PlayFabConnectionDiagnostics.SelectOrigin(origin);
        }
    }

    [HarmonyPatch(typeof(FejdStartup), "AutoJoinServer")]
    internal static class PlayFabJoinCodeOriginPatch
    {
        private static void Prefix() => PlayFabConnectionDiagnostics.SelectOrigin("join-code");
    }
}
