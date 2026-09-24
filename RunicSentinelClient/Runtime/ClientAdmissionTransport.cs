using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RunicSentinel.Admission;

namespace RunicSentinelClient.Runtime
{
    internal sealed class ClientAdmissionTransport : IDisposable
    {
        internal const int MaximumNativeResumeAttempts = 3;
        internal const int MaximumTrackedPeers = 64;

        private static readonly FieldInfo RpcFunctionsField = typeof(ZRpc).GetField(
            "m_functions", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo InviteSecretKeyField = typeof(ZNet).GetField(
            "m_inviteSecretKey", BindingFlags.Static | BindingFlags.NonPublic);

        private readonly object _gate = new object();
        private readonly ClientProfileCollector _profiles;
        private readonly Action<string> _information;
        private readonly Action<string> _warning;
        private ZNet _network;
        private ZNetPeer _serverPeer;
        private ZRpc _serverRpc;
        private object _registeredHandler;
        private ZNet _blockedNetwork;
        private ZNetPeer _blockedPeer;
        private ZRpc _blockedRpc;
        private AdmissionChallenge _challenge;
        private string _sentRequestId = string.Empty;
        private string _completedRequestId = string.Empty;
        private string _reportedProfileFailureStatus = string.Empty;
        private string _nativeHandshakeSecret = string.Empty;
        private int _nativeResumeAttempts;
        private bool _nativeHandshakeResumed;
        private bool _disposed;

        internal ClientAdmissionTransport(
            ClientProfileCollector profiles,
            Action<string> information,
            Action<string> warning)
        {
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            _information = information;
            _warning = warning;
        }

        internal bool AttachServerPeer(ZNet network, ZNetPeer peer)
        {
            if (network == null || !ReferenceEquals(ZNet.instance, network) || network.IsServer() ||
                peer == null || !peer.m_server || peer.m_rpc == null)
                return false;
            ZRpc rpc = peer.m_rpc;
            lock (_gate)
            {
                if (_disposed) return false;
                if (ReferenceEquals(_network, network) && ReferenceEquals(_serverPeer, peer) &&
                    ReferenceEquals(_serverRpc, rpc)) return true;
                if (ReferenceEquals(_blockedNetwork, network) &&
                    ReferenceEquals(_blockedPeer, peer) &&
                    ReferenceEquals(_blockedRpc, rpc)) return false;
                DetachLocked();
                if (!TryReadInviteSecretKey(out string inviteSecretKey))
                {
                    BlockLocked(network, peer, rpc);
                    Notify(_warning, global::Runic.Localization.RunicText.Get("text_fa3bfcf34dd5"));
                    return false;
                }
                int rpcKey = StableHash(AdmissionProtocolV2.DirectRpcName);
                if (!TryGetFunctionMap(rpc, out IDictionary functions))
                {
                    BlockLocked(network, peer, rpc);
                    Notify(_warning, global::Runic.Localization.RunicText.Get("text_965172dfe6a8"));
                    return false;
                }
                if (functions.Contains(rpcKey))
                {
                    BlockLocked(network, peer, rpc);
                    Notify(_warning,
                        global::Runic.Localization.RunicText.Get("text_47ce59380df7"));
                    return false;
                }
                try
                {
                    rpc.Register<ZPackage>(AdmissionProtocolV2.DirectRpcName, ReceivePackage);
                    object installed = functions[rpcKey];
                    if (installed == null)
                    {
                        rpc.Unregister(AdmissionProtocolV2.DirectRpcName);
                        BlockLocked(network, peer, rpc);
                        Notify(_warning,
                            global::Runic.Localization.RunicText.Get("text_80678fbd62f4"));
                        return false;
                    }
                    _network = network;
                    _serverPeer = peer;
                    _serverRpc = rpc;
                    _registeredHandler = installed;
                    _nativeHandshakeSecret = inviteSecretKey;
                    ResetExchangeLocked();
                    Notify(_information,
                        global::Runic.Localization.RunicText.Get("text_256802b36bf6"));
                    return true;
                }
                catch (Exception exception)
                {
                    try
                    {
                        if (functions.Contains(rpcKey))
                            rpc.Unregister(AdmissionProtocolV2.DirectRpcName);
                    }
                    catch { }
                    BlockLocked(network, peer, rpc);
                    Notify(_warning,
                        global::Runic.Localization.RunicText.Get("text_f5547dddf418") +
                        exception.GetType().Name + ").");
                    return false;
                }
            }
        }

        internal void Tick()
        {
            ZNet network = ZNet.instance;
            if (network == null || network.IsServer())
            {
                ClearDisconnected();
                return;
            }

            ZNetPeer peer;
            try { peer = network.GetServerPeer(); }
            catch { peer = null; }
            if (peer?.m_rpc != null) AttachServerPeer(network, peer);

            ZRpc rpc;
            AdmissionChallenge challenge;
            lock (_gate)
            {
                if (_disposed) return;
                if (_serverRpc == null)
                {
                    if (_blockedNetwork != null &&
                        (!ReferenceEquals(_blockedNetwork, network) ||
                         !IsTrackedServerPeer(network, _blockedPeer)))
                        ClearBlockedLocked();
                    return;
                }
                if (!ReferenceEquals(_network, network) || _serverPeer == null ||
                    !_serverPeer.m_server ||
                    !ReferenceEquals(_serverPeer.m_rpc, _serverRpc) ||
                    !IsTrackedServerPeer(network, _serverPeer))
                {
                    DetachLocked();
                    return;
                }
                if (_challenge == null ||
                    string.Equals(_sentRequestId, _challenge.RequestId, StringComparison.Ordinal)) return;
                rpc = _serverRpc;
                challenge = _challenge;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!AdmissionProtocolV2.IsChallengeCurrent(challenge, now, out string challengeFailure))
            {
                lock (_gate)
                    if (ReferenceEquals(_challenge, challenge)) _challenge = null;
                Notify(_warning, global::Runic.Localization.RunicText.Get("text_3154b686bf87") + challengeFailure + ".");
                return;
            }
            if (!_profiles.TryGet(out AdmissionClientProfile profile, out string profileStatus))
            {
                bool reportFailure = false;
                lock (_gate)
                {
                    if (!string.Equals(profileStatus, "capturing-profile", StringComparison.Ordinal) &&
                        !string.Equals(profileStatus, "not-started", StringComparison.Ordinal) &&
                        !string.Equals(
                            _reportedProfileFailureStatus, profileStatus, StringComparison.Ordinal))
                    {
                        _reportedProfileFailureStatus = profileStatus;
                        reportFailure = true;
                    }
                }
                if (reportFailure)
                    Notify(_warning,
                        global::Runic.Localization.RunicText.Get("text_4b41a28d971b") +
                        profileStatus + ").");
                return;
            }

            AdmissionReport report;
            byte[] encoded;
            try
            {
                report = AdmissionProtocolV2.CreateReport(
                    challenge, profile, Plugin.Version, now);
                encoded = AdmissionProtocolV2.EncodeReport(report);
            }
            catch (Exception exception)
            {
                Notify(_warning,
                    global::Runic.Localization.RunicText.Get("text_6bb69ccb1357") +
                    exception.GetType().Name + ").");
                return;
            }

            lock (_gate)
            {
                if (_disposed || !ReferenceEquals(_serverRpc, rpc) ||
                    !ReferenceEquals(_challenge, challenge) ||
                    string.Equals(_sentRequestId, challenge.RequestId, StringComparison.Ordinal)) return;
                _sentRequestId = challenge.RequestId;
            }
            try
            {
                if (!RpcConnected(rpc))
                {
                    lock (_gate)
                        if (string.Equals(
                                _sentRequestId, challenge.RequestId, StringComparison.Ordinal))
                            _sentRequestId = string.Empty;
                    return;
                }
                rpc.Invoke(
                    AdmissionProtocolV2.DirectRpcName,
                    new object[] { new ZPackage(encoded) });
                Notify(_information,
                    global::Runic.Localization.RunicText.Get("text_7e3fa6436397") +
                    profile.Plugins.Count + global::Runic.Localization.RunicText.Get("text_24c65f4c73bd"));
            }
            catch (Exception exception)
            {
                lock (_gate)
                    if (string.Equals(_sentRequestId, challenge.RequestId, StringComparison.Ordinal))
                        _sentRequestId = string.Empty;
                Notify(_warning,
                    global::Runic.Localization.RunicText.Get("text_f441b5e60c09") +
                    exception.GetType().Name + ").");
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                DetachLocked();
            }
        }

        private void ReceivePackage(ZRpc rpc, ZPackage package)
        {
            if (rpc == null || package == null) return;
            lock (_gate)
                if (_disposed || !ReferenceEquals(_serverRpc, rpc) || _network == null ||
                    !ReferenceEquals(ZNet.instance, _network) || _network.IsServer() ||
                    _serverPeer == null || !_serverPeer.m_server ||
                    !ReferenceEquals(_serverPeer.m_rpc, rpc)) return;
            byte[] bytes;
            try
            {
                if (package.Size() <= 0 || package.Size() > AdmissionProtocolV2.MaximumFrameBytes)
                    return;
                bytes = package.GetArray();
            }
            catch { return; }
            if (!AdmissionProtocolV2.TryGetKind(bytes, out AdmissionMessageKind kind, out _)) return;
            if (kind == AdmissionMessageKind.Challenge) ReceiveChallenge(rpc, bytes);
            else if (kind == AdmissionMessageKind.Decision) ReceiveDecision(rpc, bytes);
        }

        private void ReceiveChallenge(ZRpc rpc, byte[] bytes)
        {
            if (!AdmissionProtocolV2.TryDecodeChallenge(
                    bytes, out AdmissionChallenge challenge, out string failure) ||
                !AdmissionProtocolV2.IsChallengeCurrent(
                    challenge, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out failure))
            {
                Notify(_warning, global::Runic.Localization.RunicText.Get("text_3154b686bf87") + failure + ".");
                return;
            }
            bool newRequest;
            lock (_gate)
            {
                if (_disposed || !ReferenceEquals(_serverRpc, rpc) ||
                    string.Equals(_completedRequestId, challenge.RequestId, StringComparison.Ordinal)) return;
                newRequest = _challenge == null || !string.Equals(
                    _challenge.RequestId, challenge.RequestId, StringComparison.Ordinal);
                _challenge = challenge;
                _sentRequestId = string.Empty;
            }
            if (newRequest)
                Notify(_information, global::Runic.Localization.RunicText.Get("text_77a95578092e"));
        }

        private void ReceiveDecision(ZRpc rpc, byte[] bytes)
        {
            if (!AdmissionProtocolV2.TryDecodeDecision(
                    bytes, out AdmissionDecisionMessage decision, out string failure) ||
                !AdmissionProtocolV2.IsDecisionFresh(
                    decision, DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
            {
                Notify(_warning, global::Runic.Localization.RunicText.Get("text_862b290fb35b") + failure + ".");
                return;
            }

            bool resume = false;
            string nativeHandshakeSecret = string.Empty;
            lock (_gate)
            {
                if (_disposed || !ReferenceEquals(_serverRpc, rpc) || _challenge == null ||
                    !string.Equals(_challenge.RequestId, decision.RequestId, StringComparison.Ordinal) ||
                     !string.Equals(_sentRequestId, decision.RequestId, StringComparison.Ordinal) ||
                     string.Equals(_completedRequestId, decision.RequestId, StringComparison.Ordinal)) return;
                resume = decision.Accepted && decision.ResumeHandshake;
                if (resume)
                {
                    if (_nativeHandshakeResumed ||
                        _nativeResumeAttempts >= MaximumNativeResumeAttempts) return;
                    _nativeResumeAttempts++;
                    nativeHandshakeSecret = _nativeHandshakeSecret;
                }
                else
                {
                    _completedRequestId = decision.RequestId;
                }
            }

            if (decision.Accepted)
                Notify(_information,
                    global::Runic.Localization.RunicText.Get("text_cfad5f821987") + decision.ReasonCode + ").");
            else
                Notify(_warning,
                    global::Runic.Localization.RunicText.Get("text_2d3f4fc3053f") + decision.ReasonCode + ").");

            if (!resume) return;
            try
            {
                // Required-mode server gating consumes its approved-resume state and permits this
                // exact native direct-RPC handshake once. Optional decisions never set ResumeHandshake.
                rpc.Invoke("ServerHandshake", nativeHandshakeSecret);
                lock (_gate)
                {
                    if (!_disposed && ReferenceEquals(_serverRpc, rpc) && _challenge != null &&
                        string.Equals(
                            _challenge.RequestId,
                            decision.RequestId,
                            StringComparison.Ordinal))
                    {
                        _nativeHandshakeResumed = true;
                        _completedRequestId = decision.RequestId;
                    }
                }
            }
            catch (Exception exception)
            {
                Notify(_warning,
                    global::Runic.Localization.RunicText.Get("text_66337c067689") +
                    exception.GetType().Name + ").");
            }
        }

        private void ClearDisconnected()
        {
            lock (_gate) ClearDisconnectedLocked();
        }

        private void ClearDisconnectedLocked()
        {
            if (_network != null && ReferenceEquals(ZNet.instance, _network) &&
                !_network.IsServer() && _serverPeer != null && _serverPeer.m_server &&
                _serverRpc != null && ReferenceEquals(_serverPeer.m_rpc, _serverRpc) &&
                IsTrackedServerPeer(_network, _serverPeer)) return;
            if (_serverRpc == null && _blockedNetwork != null &&
                ReferenceEquals(ZNet.instance, _blockedNetwork) &&
                !_blockedNetwork.IsServer() &&
                IsTrackedServerPeer(_blockedNetwork, _blockedPeer)) return;
            DetachLocked();
        }

        private void DetachLocked()
        {
            ZRpc rpc = _serverRpc;
            object registered = _registeredHandler;
            _network = null;
            _serverPeer = null;
            _serverRpc = null;
            _registeredHandler = null;
            _nativeHandshakeSecret = string.Empty;
            ClearBlockedLocked();
            ResetExchangeLocked();
            if (rpc == null || registered == null ||
                !TryGetFunctionMap(rpc, out IDictionary functions)) return;
            int key = StableHash(AdmissionProtocolV2.DirectRpcName);
            try
            {
                if (ReferenceEquals(functions[key], registered))
                    rpc.Unregister(AdmissionProtocolV2.DirectRpcName);
            }
            catch { }
        }

        private void BlockLocked(ZNet network, ZNetPeer peer, ZRpc rpc)
        {
            _blockedNetwork = network;
            _blockedPeer = peer;
            _blockedRpc = rpc;
        }

        private void ClearBlockedLocked()
        {
            _blockedNetwork = null;
            _blockedPeer = null;
            _blockedRpc = null;
        }

        private static bool IsTrackedServerPeer(ZNet network, ZNetPeer expected)
        {
            if (network == null || expected == null || !expected.m_server) return false;
            List<ZNetPeer> peers;
            try { peers = network.GetPeers(); }
            catch { return false; }
            if (peers == null) return false;
            int inspected = 0;
            foreach (ZNetPeer peer in peers)
            {
                if (inspected++ >= MaximumTrackedPeers) break;
                if (ReferenceEquals(peer, expected)) return true;
            }
            return false;
        }

        private static bool RpcConnected(ZRpc rpc)
        {
            try { return rpc != null && rpc.IsConnected(); }
            catch { return false; }
        }

        private static bool TryReadInviteSecretKey(out string value)
        {
            value = string.Empty;
            if (InviteSecretKeyField == null || !InviteSecretKeyField.IsStatic ||
                InviteSecretKeyField.FieldType != typeof(string)) return false;
            try
            {
                value = InviteSecretKeyField.GetValue(null) as string ?? string.Empty;
                return value.Length <= 1024;
            }
            catch
            {
                value = string.Empty;
                return false;
            }
        }

        private static void Notify(Action<string> sink, string message)
        {
            try { sink?.Invoke(message); }
            catch { }
        }

        private static bool TryGetFunctionMap(ZRpc rpc, out IDictionary functions)
        {
            functions = null;
            if (rpc == null || RpcFunctionsField == null) return false;
            try
            {
                functions = RpcFunctionsField.GetValue(rpc) as IDictionary;
                return functions != null;
            }
            catch { functions = null; return false; }
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                int first = 5381;
                int second = first;
                int index = 0;
                while (index < value.Length && value[index] != '\0')
                {
                    first = ((first << 5) + first) ^ value[index];
                    if (index == value.Length - 1 || value[index + 1] == '\0') break;
                    second = ((second << 5) + second) ^ value[index + 1];
                    index += 2;
                }
                return first + second * 1566083941;
            }
        }

        private void ResetExchangeLocked()
        {
            _challenge = null;
            _sentRequestId = string.Empty;
            _completedRequestId = string.Empty;
            _reportedProfileFailureStatus = string.Empty;
            _nativeResumeAttempts = 0;
            _nativeHandshakeResumed = false;
        }
    }
}
