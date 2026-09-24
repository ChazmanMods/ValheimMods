using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using PartyCSharpSDK;
using PlayFab.Party;

namespace RunicCharacterVault
{
    internal static class PlayFabEndpointDiagnostics
    {
        [ThreadStatic] private static string pendingSend;
        [ThreadStatic] private static string resolvedHandles;
        private static readonly HashSet<PlayFabMultiplayerManager> Observed =
            new HashSet<PlayFabMultiplayerManager>();

        internal static void Observe(PlayFabMultiplayerManager manager)
        {
            if (manager == null || !Observed.Add(manager)) return;
            manager.OnRemotePlayerJoined += PlayerJoined;
            manager.OnRemotePlayerLeft += PlayerLeft;
            CharacterVaultPlugin.Log.LogDebug("PlayFab endpoint diagnostics started: " +
                DescribeManager(manager) + ".");
        }

        internal static void BeginSend(PlayFabMultiplayerManager manager,
            IEnumerable<PlayFabPlayer> players, int payloadSize, DeliveryOption delivery)
        {
            pendingSend = $"manager={DescribeManager(manager)}, recipients={DescribePlayers(players)}, " +
                $"payloadBytes={payloadSize}, delivery={delivery}";
            resolvedHandles = "not-resolved";
        }

        internal static void HandlesResolved(PARTY_ENDPOINT_HANDLE[] handles)
        {
            resolvedHandles = handles == null ? "<null>" :
                string.Join(",", handles.Select(handle => handle.ToString()).ToArray());
        }

        internal static void EndSend(bool succeeded)
        {
            if (!succeeded)
            {
                CharacterVaultPlugin.Log.LogError(
                    $"PlayFab SendDataMessage rejected: {pendingSend}, endpointHandles={resolvedHandles}. " +
                    "Call stack:\n" + Environment.StackTrace);
            }
            pendingSend = null;
            resolvedHandles = null;
        }

        private static void PlayerJoined(object sender, PlayFabPlayer player) =>
            LogPlayerEvent("joined", sender as PlayFabMultiplayerManager, player);

        private static void PlayerLeft(object sender, PlayFabPlayer player) =>
            LogPlayerEvent("left", sender as PlayFabMultiplayerManager, player);

        private static void LogPlayerEvent(string action, PlayFabMultiplayerManager manager,
            PlayFabPlayer player)
        {
            CharacterVaultPlugin.Log.LogDebug(
                $"PlayFab remote player {action}: player={DescribePlayer(player)}, " +
                $"{DescribeManager(manager)}.");
        }

        private static string DescribeManager(PlayFabMultiplayerManager manager)
        {
            if (manager == null) return "manager=<null>";
            return global::Runic.Localization.RunicText.Format("text_8a39f07c27f4", RuntimeHelpers.GetHashCode(manager), manager.State) +
                $"network={PlayFabConnectionDiagnostics.Fingerprint(manager.NetworkId)}, " +
                $"remotePlayers={DescribePlayers(manager.RemotePlayers)}";
        }

        private static string DescribePlayers(IEnumerable<PlayFabPlayer> players)
        {
            if (players == null) return "<null>";
            return "[" + string.Join(",", players.Select(DescribePlayer).ToArray()) + "]";
        }

        private static string DescribePlayer(PlayFabPlayer player)
        {
            if (player == null) return "<null>";
            return $"{PlayFabConnectionDiagnostics.Fingerprint(player.EntityKey?.Id)}@" +
                $"{RuntimeHelpers.GetHashCode(player):X8}";
        }
    }

    [HarmonyPatch(typeof(PlayFabMultiplayerManager), nameof(PlayFabMultiplayerManager.SendDataMessage),
        new[] { typeof(byte[]), typeof(IEnumerable<PlayFabPlayer>), typeof(DeliveryOption) })]
    internal static class CharacterVaultPlayFabSendDataDiagnosticsPatch
    {
        private static void Prefix(PlayFabMultiplayerManager __instance, byte[] buffer,
            IEnumerable<PlayFabPlayer> recipients, DeliveryOption deliveryOption)
        {
            PlayFabEndpointDiagnostics.BeginSend(
                __instance, recipients, buffer?.Length ?? 0, deliveryOption);
        }

        private static void Postfix(bool __result) => PlayFabEndpointDiagnostics.EndSend(__result);
    }

    [HarmonyPatch(typeof(PlayFabMultiplayerManager), "EndPointHandlesFromPlayFabPlayerListNoGC")]
    internal static class CharacterVaultPlayFabEndpointResolutionDiagnosticsPatch
    {
        private static void Postfix(PARTY_ENDPOINT_HANDLE[] __result) =>
            PlayFabEndpointDiagnostics.HandlesResolved(__result);
    }
}
