using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PlayFab.Party;

namespace RunicCharacterVault
{
    [HarmonyPatch(typeof(ZNet), "StopAll")]
    internal static class CharacterVaultZNetStopAllDiagnosticsPatch
    {
        private static void Prefix(ZNet __instance, bool suspending)
        {
            CharacterVaultPlugin.Log.LogDebug(
                $"PlayFab teardown: entering ZNet.StopAll(suspending={suspending}), " +
                $"isServer={__instance.IsServer()}, peers={__instance.GetPeers().Count}.");
        }

        private static void Postfix(ZNet __instance, bool suspending)
        {
            CharacterVaultPlugin.Log.LogDebug(
                $"PlayFab teardown: ZNet.StopAll(suspending={suspending}) returned, " +
                $"isServer={__instance.IsServer()}, peers={__instance.GetPeers().Count}.");
        }
    }

    [HarmonyPatch(typeof(ZPlayFabSocket), "Dispose")]
    internal static class CharacterVaultPlayFabSocketDisposeDiagnosticsPatch
    {
        private static void Prefix(ZPlayFabSocket __instance)
        {
            CharacterVaultPlugin.Log.LogDebug(
                "PlayFab teardown: entering ZPlayFabSocket.Dispose: " + Describe(__instance) + ".");
        }

        private static void Postfix(ZPlayFabSocket __instance)
        {
            CharacterVaultPlugin.Log.LogDebug(
                "PlayFab teardown: ZPlayFabSocket.Dispose returned: " + Describe(__instance) + ".");
        }

        private static string Describe(ZPlayFabSocket socket)
        {
            Traverse fields = Traverse.Create(socket);
            return $"state={fields.Field("m_state").GetValue()}, " +
                $"lobbyId={fields.Field<string>("m_lobbyId").Value ?? "<null>"}, " +
                $"remotePlayerId={socket.m_remotePlayerId ?? "<null>"}, " +
                $"connected={socket.IsConnected()}";
        }
    }

    [HarmonyPatch(typeof(ZPlayFabMatchmaking), "LeaveLobby")]
    internal static class CharacterVaultLeaveLobbyDiagnosticsPatch
    {
        private static void Prefix(string lobbyId)
        {
            CharacterVaultPlugin.Log.LogDebug(
                $"PlayFab teardown: requesting LeaveLobby for {lobbyId ?? "<null>"}.");
        }
    }

    [HarmonyPatch(typeof(PlayFabMultiplayerManager), "LeaveNetwork")]
    internal static class CharacterVaultLeaveNetworkDiagnosticsPatch
    {
        private static readonly HashSet<PlayFabMultiplayerManager> Observed =
            new HashSet<PlayFabMultiplayerManager>();

        private static void Prefix(PlayFabMultiplayerManager __instance)
        {
            Observe(__instance);

            CharacterVaultPlugin.Log.LogDebug(
                $"PlayFab teardown: requesting LeaveNetwork: state={__instance.State}, " +
                $"networkId={__instance.NetworkId ?? "<null>"}.");
        }

        private static void Postfix(PlayFabMultiplayerManager __instance)
        {
            CharacterVaultPlugin.Log.LogDebug(
                $"PlayFab teardown: LeaveNetwork returned: state={__instance.State}, " +
                $"networkId={__instance.NetworkId ?? "<null>"}.");
        }

        internal static void Observe(PlayFabMultiplayerManager manager)
        {
            if (manager != null && Observed.Add(manager))
            {
                manager.OnNetworkLeft += NetworkLeft;
                manager.OnError += NetworkError;
            }
        }

        private static void NetworkLeft(object sender, string networkId)
        {
            PlayFabMultiplayerManager manager = sender as PlayFabMultiplayerManager;
            CharacterVaultPlugin.Log.LogDebug(
                $"PlayFab teardown: OnNetworkLeft completed for networkId={networkId ?? "<null>"}, " +
                $"state={manager?.State.ToString() ?? "<unknown>"}, " +
                $"currentNetworkId={manager?.NetworkId ?? "<null>"}.");
        }

        private static void NetworkError(object sender, PlayFabMultiplayerManagerErrorArgs args)
        {
            PlayFabMultiplayerManager manager = sender as PlayFabMultiplayerManager;
            if (args != null)
            {
                PlayFabConnectionDiagnostics.ManagerError(args.Code, args.Message);
            }
            CharacterVaultPlugin.Log.LogError(
                $"PlayFab manager error: code={args?.Code}, type={args?.Type}, " +
                $"message={args?.Message ?? "<null>"}, state={manager?.State.ToString() ?? "<unknown>"}, " +
                $"networkId={manager?.NetworkId ?? "<null>"}.");
        }
    }

    [HarmonyPatch(typeof(ZPlayFabSocket), "ScheduleResetParty")]
    internal static class CharacterVaultScheduleResetPartyDiagnosticsPatch
    {
        private static void Prefix()
        {
            CharacterVaultPlugin.Log.LogWarning(
                "PlayFab recovery: ZPlayFabSocket.ScheduleResetParty requested.");
        }

        private static void Postfix()
        {
            float delay = Traverse.Create(typeof(ZPlayFabSocket))
                .Field<float>("s_durationToPartyReset").Value;
            CharacterVaultPlugin.Log.LogWarning(
                $"PlayFab recovery: global ResetParty scheduled in {delay:0.000}s.");
        }
    }

    [HarmonyPatch(typeof(ZPlayFabSocket), "ResetPartyTimeout")]
    internal static class CharacterVaultResetPartyTimeoutDiagnosticsPatch
    {
        private static void Prefix(ZPlayFabSocket __instance)
        {
            CharacterVaultPlugin.Log.LogWarning(
                "PlayFab recovery: entering socket ResetPartyTimeout. " + Describe(__instance) + ".");
        }

        private static void Postfix(ZPlayFabSocket __instance)
        {
            Traverse fields = Traverse.Create(__instance);
            CharacterVaultPlugin.Log.LogWarning(
                $"PlayFab recovery: socket reset timers set: " +
                $"reconnect={fields.Field<float>("m_partyResetConnectTimeout").Value:0.000}s, " +
                $"reset={fields.Field<float>("m_partyResetTimeout").Value:0.000}s.");
        }

        private static string Describe(ZPlayFabSocket socket)
        {
            Traverse fields = Traverse.Create(socket);
            return $"state={fields.Field("m_state").GetValue()}, " +
                $"remotePlayerId={socket.m_remotePlayerId ?? "<null>"}, " +
                $"isClient={fields.Field<bool>("m_isClient").Value}";
        }
    }

    [HarmonyPatch(typeof(ZPlayFabSocket), "CancelResetParty")]
    internal static class CharacterVaultCancelResetPartyDiagnosticsPatch
    {
        private static void Prefix(ZPlayFabSocket __instance)
        {
            Traverse fields = Traverse.Create(__instance);
            CharacterVaultPlugin.Log.LogDebug(
                $"PlayFab recovery: CancelResetParty: " +
                $"remotePlayerId={__instance.m_remotePlayerId ?? "<null>"}, " +
                $"resetRemaining={fields.Field<float>("m_partyResetTimeout").Value:0.000}s, " +
                $"reconnectRemaining={fields.Field<float>("m_partyResetConnectTimeout").Value:0.000}s.");
        }
    }

    [HarmonyPatch(typeof(PlayFabMultiplayerManager), "ResetParty")]
    internal static class CharacterVaultResetPartyDiagnosticsPatch
    {
        private static void Prefix(PlayFabMultiplayerManager __instance)
        {
            CharacterVaultPlugin.Log.LogWarning(
                $"PlayFab recovery: entering ResetParty: state={__instance.State}, " +
                $"networkId={__instance.NetworkId ?? "<null>"}.");
        }

        private static void Postfix(PlayFabMultiplayerManager __instance)
        {
            Traverse fields = Traverse.Create(__instance);
            object tasks = fields.Field("_tasks").GetValue();
            CharacterVaultPlugin.Log.LogWarning(
                $"PlayFab recovery: ResetParty queued tasks: state={__instance.State}, " +
                $"networkId={__instance.NetworkId ?? "<null>"}, tasks={tasks}.");
        }
    }

    [HarmonyPatch]
    internal static class CharacterVaultResetPartyTaskDiagnosticsPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            Type manager = typeof(PlayFabMultiplayerManager);
            string[] taskNames = { "LeaveNetworkTask", "CleanPartyTask", "InitPartyTask", "JoinPartyTask" };
            foreach (string taskName in taskNames)
            {
                Type task = AccessTools.Inner(manager, taskName);
                yield return AccessTools.Method(task, "Begin");
                yield return AccessTools.Method(task, "End");
            }
        }

        private static void Prefix(object __instance, MethodBase __originalMethod)
        {
            PlayFabMultiplayerManager manager = PlayFabMultiplayerManager.Get();
            CharacterVaultPlugin.Log.LogDebug(
                $"PlayFab recovery task: {__instance.GetType().Name}.{__originalMethod.Name} entering; " +
                $"managerState={manager.State}, networkId={manager.NetworkId ?? "<null>"}.");
        }

        private static void Postfix(object __instance, MethodBase __originalMethod)
        {
            PlayFabMultiplayerManager manager = PlayFabMultiplayerManager.Get();
            CharacterVaultPlugin.Log.LogDebug(
                $"PlayFab recovery task: {__instance.GetType().Name}.{__originalMethod.Name} returned; " +
                $"managerState={manager.State}, networkId={manager.NetworkId ?? "<null>"}.");
        }
    }

    internal static class CharacterVaultLobbyLeftDiagnostics
    {
        internal static void Register()
        {
            ZPlayFabMatchmaking.LobbyLeft -= LobbyLeft;
            ZPlayFabMatchmaking.LobbyLeft += LobbyLeft;
        }

        internal static void Unregister()
        {
            ZPlayFabMatchmaking.LobbyLeft -= LobbyLeft;
        }

        private static void LobbyLeft(bool success)
        {
            CharacterVaultPlugin.Log.LogDebug(
                $"PlayFab teardown: LobbyLeft callback completed with success={success}.");
        }
    }
}
