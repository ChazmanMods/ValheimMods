using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using PartyCSharpSDK;
using PlayFab.Party;
using RunicWorldEngine.Core;
using Steamworks;

namespace RunicWorldEngine.Integration
{
    internal sealed class TransportSample
    {
        internal double? Rtt, Sent, Received, PendingBytes, InFlightBytes;
        internal int? SendMessages, ReceiveMessages, OutOfOrder;
        internal bool? SendWindowPressure;
        internal string Kind = "unavailable", Error;
    }

    internal static class TransportSampler
    {
        private static readonly FieldInfo SteamConnection = AccessTools.Field(typeof(ZSteamSocket), "m_con");
        private static readonly FieldInfo SteamSend = AccessTools.Field(typeof(ZSteamSocket), "m_sendQueue");
        private static readonly FieldInfo SteamReceive = AccessTools.Field(typeof(ZSteamSocket), "m_pkgQueue");
        private static readonly FieldInfo PartyPeer = AccessTools.Field(typeof(ZPlayFabSocket), "m_peer");
        private static readonly FieldInfo PartyFlight = AccessTools.Field(typeof(ZPlayFabSocket), "m_inFlightQueue");
        private static readonly FieldInfo PartySend = AccessTools.Field(typeof(ZPlayFabSocket), "m_sendQueue");
        private static readonly FieldInfo PartyReceive = AccessTools.Field(typeof(ZPlayFabSocket), "m_recvQueue");
        private static readonly FieldInfo PartyOrder = AccessTools.Field(typeof(ZPlayFabSocket), "m_outOfOrderQueue");
        private static readonly FieldInfo LocalEndpoint = AccessTools.Field(typeof(PlayFabMultiplayerManager), "_localEndPointHandle");
        private static readonly FieldInfo RemoteEndpoint = AccessTools.Field(typeof(PlayFabPlayer), "_endPointHandle");
        private static readonly PARTY_ENDPOINT_STATISTIC[] Statistics =
        {
            PARTY_ENDPOINT_STATISTIC.PARTY_ENDPOINT_STATISTIC_AVERAGE_DEVICE_ROUND_TRIP_LATENCY_IN_MILLISECONDS,
            PARTY_ENDPOINT_STATISTIC.PARTY_ENDPOINT_STATISTIC_CURRENTLY_QUEUED_SEND_MESSAGE_BYTES
        };

        internal static TransportSample Read(ISocket socket, double now)
        {
            var sample = new TransportSample();
            try
            {
                if (socket == null || !socket.IsConnected()) return sample;
                if (socket is ZSteamSocket)
                {
                    sample.Kind = "Steam/native bytes";
                    sample.SendMessages = Count(SteamSend, socket);
                    sample.ReceiveMessages = Count(SteamReceive, socket);
                    var connection = (HSteamNetConnection)SteamConnection.GetValue(socket);
                    var status = default(SteamNetConnectionRealTimeStatus_t);
                    var lanes = default(SteamNetConnectionRealTimeLaneStatus_t);
                    if (SteamNetworkingSockets.GetConnectionRealTimeStatus(connection, ref status, 0, ref lanes) == EResult.k_EResultOK)
                    {
                        sample.Rtt = Nonnegative(status.m_nPing);
                        sample.Sent = Nonnegative(status.m_flOutBytesPerSec);
                        sample.Received = Nonnegative(status.m_flInBytesPerSec);
                        sample.PendingBytes = (double)status.m_cbPendingReliable + status.m_cbPendingUnreliable;
                        sample.InFlightBytes = status.m_cbSentUnackedReliable;
                    }
                }
                else if (socket is ZPlayFabSocket)
                {
                    sample.Kind = "PlayFab/socket payload bytes";
                    sample.SendMessages = Count(PartySend, socket);
                    sample.ReceiveMessages = Count(PartyReceive, socket);
                    sample.OutOfOrder = Count(PartyOrder, socket);
                    if (PartyFlight.GetValue(socket) is ZPlayFabSocket.InFlightQueue flight) sample.InFlightBytes = flight.Bytes;
                    // Independent passive counters remain valid even if another statistics consumer
                    // resets Valheim's built-in totals. Rates are payload, not wire/compressed rates.
                    var rates = PayloadObservation.Meter((ZNetStats)socket).Sample(now);
                    sample.Sent = rates.Sent;
                    sample.Received = rates.Received;
                    var peers = PartyPeer.GetValue(socket) as PlayFabPlayer[];
                    var manager = PlayFabMultiplayerManager.Get();
                    var local = manager == null ? null : LocalEndpoint.GetValue(manager) as PARTY_ENDPOINT_HANDLE;
                    var remote = peers != null && peers.Length == 1 ? RemoteEndpoint.GetValue(peers[0]) as PARTY_ENDPOINT_HANDLE : null;
                    if (local != null && remote != null && SDK.PartyEndpointGetEndpointStatistics(local, new[] { remote }, Statistics, out ulong[] values) == 0 && values != null && values.Length == 2)
                    {
                        // Party reports 0 until it has a latency estimate; do not label that as a measured zero RTT.
                        sample.Rtt = values[0] > 0 ? (double?)values[0] : null;
                        sample.PendingBytes = values[1];
                    }
                }
                else sample.Kind = socket.GetType().Name + "/unsupported metrics";
            }
            catch (Exception error) { sample.Error = error.GetType().Name; }
            if (socket is ZSteamSocket || socket is ZPlayFabSocket)
                sample.SendWindowPressure = Core.SendWindowPressure.Estimate(socket is ZPlayFabSocket, sample.PendingBytes, sample.InFlightBytes, sample.SendMessages);
            return sample;
        }

        private static double? Nonnegative(double value) => value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value) ? value : (double?)null;
        private static int? Count(FieldInfo field, object instance) => field?.GetValue(instance) is ICollection collection ? collection.Count : (int?)null;
    }
}
