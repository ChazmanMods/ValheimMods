using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RunicWorldEngine.Core;
using UnityEngine;

namespace RunicWorldEngine.Integration
{
    internal static class HealthRuntime
    {
        private const int MaximumPeers = 128;
        private sealed class PeerState
        {
            internal double LastProgress, Joined;
            internal int Sends;
        }
        private static readonly Dictionary<ZNetPeer, PeerState> Peers = new Dictionary<ZNetPeer, PeerState>();
        private static readonly Dictionary<string, WarningLatch> Warnings = new Dictionary<string, WarningLatch>();
        private static readonly TransferCounter<ZDOID> Transfers = new TransferCounter<ZDOID>(1024);
        private static readonly FieldInfo ManagerPeers = AccessTools.Field(typeof(ZDOMan), "m_peers");
        private static readonly Type PeerType = AccessTools.Inner(typeof(ZDOMan), "ZDOPeer");
        private static readonly FieldInfo NetworkPeer = AccessTools.Field(PeerType, "m_peer");
        private static readonly FieldInfo ForceSend = AccessTools.Field(PeerType, "m_forceSend");
        private static readonly FieldInfo InvalidSector = AccessTools.Field(PeerType, "m_invalidSector");
        private static object _session;
        private static double _nextSample, _lastSample, _frameTotal, _framePeak;
        private static int _frameCount, _mainThread;
        private static bool _initialized;
        internal static bool Active { get; private set; }
        internal static bool CanObserve => Active && System.Threading.Thread.CurrentThread.ManagedThreadId == _mainThread;
        internal static string[] Lines { get; private set; } = { "Health: no active session sampled." };
        private static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

        internal static void Initialize()
        {
            _mainThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            _initialized = true;
            new Terminal.ConsoleCommand("runicworld_status", "Show this process's sampled player capacity and network health (read-only).",
                args => { foreach (string line in Report()) args.Context.AddString(line); });
        }

        internal static IEnumerable<string> Report()
        {
            yield return "Runic World Engine: " + CapacityRuntime.Status;
            foreach (string line in Lines) yield return line;
        }

        internal static void Tick()
        {
            if (!_initialized) return;
            ZNet network = ZNet.instance;
            Active = WorldEngineConfig.Enabled.Value && WorldEngineConfig.HealthEnabled.Value && network != null && ZDOMan.instance != null;
            object session = Active ? (object)network : null;
            if (!ReferenceEquals(session, _session))
            {
                ClearSession();
                _session = session;
            }
            if (!Active) return;
            double now = Now;
            double frame = Time.unscaledDeltaTime * 1000d;
            if (frame >= 0 && !double.IsNaN(frame) && !double.IsInfinity(frame))
            { _frameTotal += frame; _framePeak = Math.Max(_framePeak, frame); _frameCount++; }
            if (now < _nextSample) return;
            _nextSample = now + 1d;
            try { Sample(network, now); }
            catch (Exception error)
            {
                Lines = new[] { "Health sample unavailable: " + error.GetType().Name + "; no world data changed." };
                Warn("sampling", true, now, "Health sampling unavailable: " + error.GetType().Name, network.IsServer());
            }
        }

        private static void Sample(ZNet network, double now)
        {
            bool server = network.IsServer();
            if (server) CapacityRuntime.CheckIntegrity();
            double elapsed = _lastSample > 0 ? now - _lastSample : 1d;
            _lastSample = now;
            double meanFrame = _frameCount > 0 ? _frameTotal / _frameCount : 0;
            double peakFrame = _framePeak;
            _frameTotal = _framePeak = 0; _frameCount = 0;
            var connected = network.GetPeers();
            int ready = connected.Take(MaximumPeers).Count(p => p.IsReady());
            int players = network.GetNrOfPlayers();
            var lines = new List<string>(MaximumPeers + 3)
            {
                $"Health ({(server ? "server" : "local client; not remote-server telemetry")}): players={players}, ready remote peers={ready}, connections={connected.Count}; frame mean/peak={meanFrame:F1}/{peakFrame:F1}ms.",
                $"Ownership / {elapsed:F1}s: assigned={Transfers.Assigned}, released={Transfers.Released}, transferred={Transfers.Transferred}, rapid repeated transfers={Transfers.RapidTransfers} (10s window; up to 1024 tracked IDs)."
            };
            var zdoPeers = ManagerPeers.GetValue(ZDOMan.instance) as IList;
            var backlog = new Dictionary<ZNetPeer, (int Forced, int Invalid)>();
            if (zdoPeers == null) throw new MissingFieldException("ZDOMan.m_peers");
            for (int i = 0; i < Math.Min(MaximumPeers, zdoPeers.Count); i++)
            {
                object peer = zdoPeers[i];
                if (NetworkPeer.GetValue(peer) is ZNetPeer p)
                    backlog[p] = (((HashSet<ZDOID>)ForceSend.GetValue(peer)).Count, ((HashSet<ZDOID>)InvalidSector.GetValue(peer)).Count);
            }
            bool latency = false, queues = false, starvation = false, heartbeat = false, unavailable = false, sendWindow = false;
            // Remove disconnected states before accepting replacements at the bounded table limit.
            var live = new HashSet<ZNetPeer>(connected.Take(MaximumPeers));
            foreach (var old in Peers.Keys.Where(p => !live.Contains(p)).ToArray()) Peers.Remove(old);
            for (int i = 0; i < Math.Min(MaximumPeers, connected.Count); i++)
            {
                var peer = connected[i];
                if (!peer.IsReady()) continue;
                if (!Peers.TryGetValue(peer, out PeerState state))
                    Peers[peer] = state = new PeerState { Joined = now, LastProgress = now };
                TransportSample transport = TransportSampler.Read(peer.m_socket, now);
                backlog.TryGetValue(peer, out var pending);
                double age = peer.m_rpc.GetTimeSinceLastPing();
                double quiet = now - state.LastProgress;
                bool warm = now - state.Joined >= 15d;
                bool pressured = transport.SendWindowPressure == true;
                bool stalled = warm && (pending.Forced + pending.Invalid > 0 || pressured) && quiet >= WorldEngineConfig.StarvationSeconds.Value;
                sendWindow |= warm && pressured;
                latency |= warm && transport.Rtt >= WorldEngineConfig.RttWarningMilliseconds.Value;
                queues |= warm && ((transport.PendingBytes ?? 0) + (transport.InFlightBytes ?? 0) >= WorldEngineConfig.BacklogWarningKiB.Value * 1024d ||
                    transport.SendMessages >= WorldEngineConfig.QueueWarningMessages.Value || transport.ReceiveMessages >= WorldEngineConfig.QueueWarningMessages.Value ||
                    transport.OutOfOrder >= WorldEngineConfig.QueueWarningMessages.Value ||
                    pending.Forced + pending.Invalid >= WorldEngineConfig.QueueWarningMessages.Value);
                starvation |= stalled;
                heartbeat |= warm && age >= WorldEngineConfig.HeartbeatWarningSeconds.Value;
                unavailable |= transport.Error != null || !transport.Rtt.HasValue;
                lines.Add($"Peer {peer.m_uid} '{SafeName(peer.m_playerName)}' [{transport.Kind}]: RTT={Metric(transport.Rtt)}ms; send/recv={Metric(transport.Sent)}/{Metric(transport.Received)} B/s; queued={Metric(transport.PendingBytes)}B, in-flight={Metric(transport.InFlightBytes)}B; app send/recv queues={Metric(transport.SendMessages)}/{Metric(transport.ReceiveMessages)} messages, out-of-order={Metric(transport.OutOfOrder)}; send-window pressure={(transport.SendWindowPressure.HasValue ? (pressured ? "yes" : "no") : "n/a")}; priority/invalid ZDOs={pending.Forced}/{pending.Invalid}; ZDO sends={state.Sends}, last progress={quiet:F1}s ago; heartbeat age={age:F1}s; {(stalled ? "POSSIBLE STARVATION" : "no starvation indicator")}{(transport.Error == null ? "" : "; sample error=" + transport.Error)}.");
                state.Sends = 0;
            }
            if (connected.Count > MaximumPeers || zdoPeers.Count > MaximumPeers) lines.Add("Peer detail and ready-peer count truncated at 128; aggregate connection count remains complete.");
            Warn("capacity", server && CapacityRuntime.ValidatedOverride && players >= Math.Ceiling(CapacityRuntime.PlayerLimit * 0.9d), now, "Player count is at or above 90% of the validated cap.", server);
            Warn("frames", meanFrame >= WorldEngineConfig.FrameWarningMilliseconds.Value, now, $"Sustained slow server frames: mean {meanFrame:F1}ms.", server);
            Warn("latency", latency, now, "One or more peers have sustained high RTT; inspect runicworld_status/peer summaries.", server);
            Warn("backlog", queues, now, "Sustained queued/unacknowledged traffic or priority ZDO backlog; inspect peer summaries.", server);
            Warn("send-window", sendWindow, now, "Sustained pressure near Valheim's ZDO send-window limit; synchronization may be throttled.", server);
            Warn("starvation", starvation, now, "Possible synchronization starvation: priority/invalid ZDO work or send-window pressure without successful ZDO sends.", server);
            Warn("heartbeat", heartbeat, now, "One or more peer heartbeat replies are overdue; disconnect risk is rising.", server);
            Warn("ownership", Transfers.RapidTransfers / elapsed >= WorldEngineConfig.OwnershipWarningPerSecond.Value, now, "Sustained rapid ownership transfers; inspect server load and peer movement.", server);
            Warn("sampling", false, now, "Health sampling restored.", server);
            if (unavailable) lines.Add("n/a means unavailable/uninitialized, not zero. Heartbeat age is not RTT. Priority ZDO counts are not the full unsent world backlog.");
            Lines = lines.ToArray();
            Transfers.ResetInterval();
        }

        private static void Warn(string key, bool condition, double now, string message, bool server)
        {
            if (!server || !WorldEngineConfig.HealthWarnings.Value) { Warnings.Clear(); return; }
            if (!Warnings.TryGetValue(key, out WarningLatch latch)) Warnings[key] = latch = new WarningLatch();
            int change = latch.Observe(condition, now, WorldEngineConfig.WarningHoldSeconds.Value, WorldEngineConfig.WarningCooldownSeconds.Value);
            if (change == 1) Plugin.Log.LogWarning("Server health [" + key + "]: " + message);
            else if (change == -1) Plugin.Log.LogInfo("Server health [" + key + "]: recovered below warning threshold.");
        }

        internal static void RecordProgress(object zdoPeer, bool sent)
        {
            if (!sent || !Active || System.Threading.Thread.CurrentThread.ManagedThreadId != _mainThread) return;
            try
            {
                if (NetworkPeer.GetValue(zdoPeer) is ZNetPeer peer && Peers.TryGetValue(peer, out PeerState state))
                { state.LastProgress = Now; state.Sends++; }
            }
            catch { /* Observation must never alter a send result. */ }
        }

        internal static void RecordTransfer(ZDO zdo, long before)
        {
            if (!Active || System.Threading.Thread.CurrentThread.ManagedThreadId != _mainThread) return;
            try { Transfers.Record(zdo.m_uid, before, zdo.GetOwner(), Now); }
            catch { /* Observation must never alter ownership. */ }
        }

        private static string Metric(double? value) => value.HasValue ? value.Value.ToString("F0", CultureInfo.InvariantCulture) : "n/a";
        private static string SafeName(string name) => new string((name ?? "unnamed").Take(48).Select(c => char.IsControl(c) || c == '<' || c == '>' ? '_' : c).ToArray());
        private static void ClearSession()
        {
            Peers.Clear(); Warnings.Clear(); Transfers.Clear();
            PayloadObservation.Reset();
            _lastSample = _nextSample = _frameTotal = _framePeak = 0; _frameCount = 0;
            Lines = new[] { "Health: no active session sampled." };
        }
        internal static void Reset() { Active = _initialized = false; _session = null; ClearSession(); }
    }

    [HarmonyPatch(typeof(ZDO), "SetOwnerInternal", typeof(long))]
    internal static class TransferObservationPatch
    {
        private static void Prefix(ZDO __instance, out long? __state)
        {
            __state = null;
            if (!HealthRuntime.CanObserve) return;
            try { __state = __instance.GetOwner(); } catch { }
        }
        private static void Postfix(ZDO __instance, long? __state)
        {
            if (__state.HasValue) HealthRuntime.RecordTransfer(__instance, __state.Value);
        }
    }

    [HarmonyPatch(typeof(ZDOMan), "SendZDOs")]
    internal static class SendProgressObservationPatch
    {
        private static void Postfix(object __0, bool __result) => HealthRuntime.RecordProgress(__0, __result);
    }
}
