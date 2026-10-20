using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;

namespace RunicAutomation
{
    // Recovery for native ownership changes that did not issue our handoff receipt.
    // The server independently compares its accepted snapshot; requests never write
    // ZDOs, claim ownership, or supply inventory contents for the server to trust.
    internal static class ServerSnapshotBarrier
    {
        private const string RequestRpc = "RunicAutomation_SnapshotCheck_v2";
        private const string ReplyRpc = "RunicAutomation_SnapshotAck_v2";
        private sealed class Check
        {
            public string Nonce, Digest;
            public int OwnerRevision;
            public uint DataRevision;
            public float Retry, AcceptedUntil;
        }
        private static readonly Dictionary<ZDOID, Check> Checks = new Dictionary<ZDOID, Check>();
        private static readonly Dictionary<string, float> Requests = new Dictionary<string, float>();
        private static ZRoutedRpc Registered;
        private static int Frame = -1, Sent;

        internal static void Register()
        {
            if (ZRoutedRpc.instance == null || ReferenceEquals(Registered, ZRoutedRpc.instance)) return;
            Registered = ZRoutedRpc.instance;
            Registered.Register<ZPackage>(RequestRpc, ReceiveRequest);
            Registered.Register<ZPackage>(ReplyRpc, ReceiveReply);
        }

        internal static void Clear()
        {
            Checks.Clear(); Requests.Clear(); Registered = null; Frame = -1; Sent = 0;
        }
        internal static void Remove(ZDOID id) => Checks.Remove(id);

        private static string Digest(ZDO zdo)
        {
            byte[] items = zdo.GetByteArray(ZDOVars.s_items);
            // Custom storage without a native item snapshot needs its own protocol.
            if (items == null || items.Length == 0) return null;
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(items));
        }

        internal static bool Ready(Container chest, string nonce, Func<Container, bool> refresh)
        {
            Register();
            ZNet network = ZNet.instance;
            var view = ContainerAuthority.View(chest);
            var zdo = view?.GetZDO();
            if (network == null || network.IsServer() || Registered == null ||
                network.GetServerPeer() == null || zdo == null || !view.IsOwner() ||
                !ContainerAuthority.Writable(chest)) return false;
            string digest = Digest(zdo);
            if (digest == null) return false;
            if (!Checks.TryGetValue(zdo.m_uid, out Check check))
            {
                if (Checks.Count >= 16384) return false;
                Checks[zdo.m_uid] = check = new Check();
            }
            float now = Time.realtimeSinceStartup;
            bool same = check.Nonce == nonce && check.OwnerRevision == zdo.OwnerRevision &&
                check.DataRevision == zdo.DataRevision && check.Digest == digest;
            if (same && now < check.AcceptedUntil)
            {
                if (!refresh(chest)) return false;
                if (!view.IsValid() || !view.IsOwner() || !ReferenceEquals(zdo, view.GetZDO()) ||
                    !ContainerAuthority.Writable(chest) || check.OwnerRevision != zdo.OwnerRevision ||
                    check.DataRevision != zdo.DataRevision || check.Digest != Digest(zdo)) return false;
                MutationGate.Report("authority server-snapshot-confirmed; endpoint=" + zdo.m_uid +
                    "; ownerRevision=" + zdo.OwnerRevision + "; dataRevision=" + zdo.DataRevision);
                Checks.Remove(zdo.m_uid);
                return true;
            }
            if (now < check.Retry) return false;
            if (Frame != Time.frameCount) { Frame = Time.frameCount; Sent = 0; }
            if (Sent >= 32) return false;
            Sent++;
            check.Nonce = nonce; check.OwnerRevision = zdo.OwnerRevision;
            check.DataRevision = zdo.DataRevision; check.Digest = digest;
            check.AcceptedUntil = 0; check.Retry = now + 3f;
            var request = Pack(zdo.m_uid, check);
            Registered.InvokeRoutedRPC(network.GetServerPeer().m_uid, RequestRpc, request);
            MutationGate.Report("authority server-snapshot-requested; endpoint=" + zdo.m_uid);
            return false;
        }

        private static ZPackage Pack(ZDOID id, Check check)
        {
            var package = new ZPackage();
            package.Write(id); package.Write(check.Nonce); package.Write(check.OwnerRevision);
            package.Write(check.DataRevision); package.Write(check.Digest);
            return package;
        }

        private static bool Read(ZPackage package, out ZDOID id, out Check check)
        {
            id = ZDOID.None; check = null;
            if (package == null || package.Size() > 256) return false;
            id = package.ReadZDOID();
            string nonce = package.ReadString();
            int ownerRevision = package.ReadInt();
            uint dataRevision = package.ReadUInt();
            string digest = package.ReadString();
            if (id.IsNone() || nonce.Length != 32 || !Guid.TryParseExact(nonce, "N", out Guid token) ||
                token == Guid.Empty || ownerRevision < 0 || ownerRevision > ushort.MaxValue || digest.Length != 44) return false;
            check = new Check { Nonce = nonce, OwnerRevision = ownerRevision, DataRevision = dataRevision, Digest = digest };
            return true;
        }

        private static void ReceiveRequest(long sender, ZPackage package)
        {
            try
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer() || sender == 0 ||
                    ZNet.instance.GetPeer(sender) == null || MutationGate.Current != null ||
                    !Read(package, out ZDOID id, out Check request)) return;
                string key = sender + ":" + id;
                float now = Time.realtimeSinceStartup;
                if (Requests.TryGetValue(key, out float retry) && now < retry) return;
                if (Requests.Count >= 16384)
                {
                    var expired = new List<string>();
                    foreach (var pair in Requests) if (now >= pair.Value) expired.Add(pair.Key);
                    foreach (string old in expired) Requests.Remove(old);
                    if (Requests.Count >= 16384) return;
                }
                Requests[key] = now + 2f;
                ZDO zdo = ZDOMan.instance?.GetZDO(id);
                if (zdo == null || zdo.GetOwner() != sender || zdo.OwnerRevision != request.OwnerRevision ||
                    zdo.GetInt(ZDOVars.s_inUse, 0) != 0 || MutationGate.IsBlocked(id) ||
                    MutationGate.IsBlocked("valheim.zdo:" + id)) return;
                if (zdo.DataRevision != request.DataRevision || Digest(zdo) != request.Digest)
                {
                    ZDOMan.instance.ForceSendZDO(sender, id);
                    return;
                }
                Registered.InvokeRoutedRPC(sender, ReplyRpc, Pack(id, request));
            }
            catch (Exception error) { MutationGate.Report("snapshot check deferred: " + error.Message); }
        }

        private static void ReceiveReply(long sender, ZPackage package)
        {
            try
            {
                if (ZNet.instance == null || ZNet.instance.IsServer() ||
                    ZNet.instance.GetServerPeer()?.m_uid != sender ||
                    !Read(package, out ZDOID id, out Check reply) || !Checks.TryGetValue(id, out Check check)) return;
                if (check.Nonce != reply.Nonce || check.OwnerRevision != reply.OwnerRevision ||
                    check.DataRevision != reply.DataRevision || check.Digest != reply.Digest) return;
                check.AcceptedUntil = Time.realtimeSinceStartup + 3f;
            }
            catch (Exception error) { MutationGate.Report("snapshot confirmation deferred: " + error.Message); }
        }
    }
}
