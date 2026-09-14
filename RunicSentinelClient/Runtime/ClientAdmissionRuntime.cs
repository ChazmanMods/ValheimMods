using System;

namespace RunicSentinelClient.Runtime
{
    internal sealed class ClientAdmissionRuntime : IDisposable
    {
        private readonly Action<string> _information;
        private readonly Action<string> _warning;
        private ClientProfileCollector _profiles;
        private ClientAdmissionTransport _transport;
        private bool _disposed;

        internal ClientAdmissionRuntime(Action<string> information, Action<string> warning)
        {
            _information = information;
            _warning = warning;
        }

        internal void OnConnectionStarted(ZNet network, ZNetPeer peer)
        {
            if (_disposed || network == null ||
                !ReferenceEquals(ZNet.instance, network) || network.IsServer() ||
                peer == null || !peer.m_server || peer.m_rpc == null) return;
            EnsureStarted();
            _transport.AttachServerPeer(network, peer);
        }

        internal void Tick()
        {
            if (_disposed) return;
            ZNet network = ZNet.instance;
            if (network != null && network.IsServer())
            {
                StopClientServices();
                return;
            }
            if (network == null)
            {
                StopClientServices();
                return;
            }
            EnsureStarted();
            _transport.Tick();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopClientServices();
        }

        private void EnsureStarted()
        {
            if (_profiles != null) return;
            _profiles = new ClientProfileCollector();
            _transport = new ClientAdmissionTransport(_profiles, _information, _warning);
            _profiles.Start();
            _information?.Invoke("Sentinel Client started a bounded local plugin profile.");
        }

        private void StopClientServices()
        {
            try { _transport?.Dispose(); } catch { }
            _transport = null;
            try { _profiles?.Dispose(); } catch { }
            _profiles = null;
        }
    }
}
