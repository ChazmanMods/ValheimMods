using System;
using System.Collections.Generic;
using System.Linq;
using RunicSentinel.PlayerActions;

namespace RunicSentinel.Runtime
{
    internal sealed class SentinelPlayerCommandService:IDisposable
    {
        internal static SentinelPlayerCommandService Current;
        private ZNet _network;
        private readonly Dictionary<ZRpc,Capability> _clients=new Dictionary<ZRpc,Capability>();
        private readonly Dictionary<string,Operation> _operations=new Dictionary<string,Operation>();
        private sealed class Capability{internal ZNetPeer Peer;internal long Character,Seen;}
        private sealed class Operation{internal ZNetPeer Peer;internal string Owner,Target,Action,Message;internal long Character,Deadline;internal bool Done,Success;}
        internal SentinelPlayerCommandService(){Current=this;}
        internal void Tick(ZNet network)
        {
            if(!ReferenceEquals(network,_network)){Dispose();Current=this;_network=network;}
            if(network==null||!network.IsServer())return;
            var peers=network.GetPeers().Where(p=>p!=null&&p.IsReady()&&!p.m_server&&p.m_rpc!=null).Take(128).ToArray();
            foreach(var rpc in _clients.Keys.ToArray())if(!peers.Any(p=>ReferenceEquals(p,_clients[rpc].Peer)&&ReferenceEquals(p.m_rpc,rpc)))
            {rpc.Unregister(PlayerActionProtocol.CapabilityRpc);rpc.Unregister(PlayerActionProtocol.ResultRpc);_clients.Remove(rpc);}
            foreach(var peer in peers)if(!_clients.ContainsKey(peer.m_rpc))
            {_clients.Add(peer.m_rpc,new Capability{Peer=peer});peer.m_rpc.Register<ZPackage>(PlayerActionProtocol.CapabilityRpc,Hello);peer.m_rpc.Register<ZPackage>(PlayerActionProtocol.ResultRpc,Reply);}
            long now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach(var pair in _operations.ToArray())
            {
                var op=pair.Value;
                if(!op.Done&&(op.Deadline<now||!peers.Contains(op.Peer)||op.Character!=op.Peer.m_playerID))
                {op.Done=true;op.Message="No completion confirmed: player disconnected, changed character, or timed out. Do not assume the action ran.";}
                if(op.Deadline+60<now)_operations.Remove(pair.Key);
            }
        }
        private bool Exact(ZRpc rpc,out Capability client)
        {
            client=null;
            return _network!=null&&ReferenceEquals(_network,ZNet.instance)&&_network.IsServer()&&_clients.TryGetValue(rpc,out client)&&client.Peer.IsReady()&&ReferenceEquals(client.Peer.m_rpc,rpc)&&_network.GetPeers().Contains(client.Peer);
        }
        private void Hello(ZRpc rpc,ZPackage p)
        {
            if(!Exact(rpc,out var client)||p==null||p.Size()>64)return;
            try{int schema=p.ReadInt();long character=p.ReadLong();if(schema!=PlayerActionProtocol.Schema||p.GetPos()!=p.Size()||character!=client.Peer.m_playerID)return;client.Character=character;client.Seen=DateTimeOffset.UtcNow.ToUnixTimeSeconds();}catch{}
        }
        internal bool Ready(string account,string character)
        {return _clients.Values.Any(c=>c.Seen>=DateTimeOffset.UtcNow.ToUnixTimeSeconds()-15&&c.Character==c.Peer.m_playerID&&c.Character.ToString()==character&&Identity(c.Peer)==account&&c.Peer.IsReady());}
        private static string Identity(ZNetPeer peer)=>SentinelTransportIdentity.TryResolvePeer(peer,out string a,out string s,out _)?a+":"+s:"";
        internal string Start(string request,string owner)
        {
            var fields=request.Split('\n');if(fields.Length!=4)throw new InvalidOperationException("Invalid player action request.");
            string target=fields[0],character=fields[1],action=fields[2],args=PlayerActionProtocol.Validate(fields[2],fields[3]);
            if(!Ready(target,character))throw new InvalidOperationException("The selected character is not ready or needs RunicSentinel 1.4.6 / RunicSentinelClient 1.0.3. Wait for the client handshake after joining.");
            var client=_clients.Values.Single(c=>Identity(c.Peer)==target&&c.Character.ToString()==character);
            if(_operations.Count>=64)throw new InvalidOperationException("Player action capacity reached. Wait before sending another action.");
            string id=Guid.NewGuid().ToString("N");long deadline=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+30;
            var op=new Operation{Peer=client.Peer,Owner=owner,Target=target,Character=client.Character,Action=action,Deadline=deadline,Message="Waiting for the selected player's client to confirm completion."};_operations.Add(id,op);
            var p=new ZPackage();p.Write(id);p.Write(op.Character);p.Write(deadline);p.Write(action);p.Write(args);
            try{client.Peer.m_rpc.Invoke(PlayerActionProtocol.ActionRpc,p);}catch{op.Done=true;op.Message="The player connection could not receive the action.";}
            SentinelPlayerReports.Record(owner,"Player command request",action+" "+args+" -> "+target);
            return id;
        }
        private void Reply(ZRpc rpc,ZPackage p)
        {
            if(!Exact(rpc,out var client)||p==null||p.Size()>PlayerActionProtocol.MaximumBytes)return;
            try
            {
                string id=p.ReadString();bool ok=p.ReadBool();string message=p.ReadString();
                if(p.GetPos()!=p.Size()||message.Length>600||!_operations.TryGetValue(id,out var op)||op.Done||op.Deadline<DateTimeOffset.UtcNow.ToUnixTimeSeconds()||!ReferenceEquals(op.Peer,client.Peer)||op.Character!=client.Peer.m_playerID)return;
                op.Done=true;op.Success=ok;op.Message=message;
                SentinelPlayerReports.Record(op.Owner,"Player command result",op.Action+" -> "+op.Target+": "+(ok?"client confirmed: ":"client rejected: ")+message);
                if(op.Owner!=op.Target)SentinelPlayerReports.Record(op.Target,"Administrator action",op.Action+" requested by "+op.Owner+": "+(ok?"client confirmed: ":"client rejected: ")+message);
            }catch{}
        }
        internal string Result(string id,string owner)
        {
            if(!_operations.TryGetValue(id,out var op)||op.Owner!=owner)throw new InvalidOperationException("Player action result is unavailable for this administrator.");
            return (op.Done?(op.Success?"done":"failed"):"pending")+"\n"+op.Message;
        }
        public void Dispose()
        {foreach(var rpc in _clients.Keys){try{rpc.Unregister(PlayerActionProtocol.CapabilityRpc);rpc.Unregister(PlayerActionProtocol.ResultRpc);}catch{}}_clients.Clear();_operations.Clear();_network=null;if(ReferenceEquals(Current,this))Current=null;}
    }
}
