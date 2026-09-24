using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace RunicSentinel.PlayerActions
{
    // There is no public arbitrary-command endpoint. Only this connection's server
    // can send a bounded allowlisted action for the currently loaded character.
    internal sealed class PlayerActionReceiver:IDisposable
    {
        private ZNet _network;
        private ZRpc _server;
        private float _nextHello;
        private readonly Dictionary<string,Receipt> _seen=new Dictionary<string,Receipt>();
        private sealed class Receipt{internal string Body,Message;internal bool Success;internal long Expires;}
        internal void Tick()
        {
            var network=ZNet.instance;var peer=network!=null&&!network.IsServer()?network.GetServerPeer():null;
            var rpc=peer!=null&&peer.IsReady()?peer.m_rpc:null;
            if(!ReferenceEquals(network,_network)||!ReferenceEquals(rpc,_server))
            {Dispose();_network=network;_server=rpc;_nextHello=0;if(rpc!=null)rpc.Register<ZPackage>(PlayerActionProtocol.ActionRpc,Receive);}
            long now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach(var id in _seen.Where(p=>p.Value.Expires<now).Select(p=>p.Key).ToArray())_seen.Remove(id);
            if(_server==null||Player.m_localPlayer==null||Time.realtimeSinceStartup<_nextHello)return;
            _nextHello=Time.realtimeSinceStartup+5;
            var hello=new ZPackage();hello.Write(PlayerActionProtocol.Schema);hello.Write(Player.m_localPlayer.GetPlayerID());
            _server.Invoke(PlayerActionProtocol.CapabilityRpc,hello);
        }
        private void Receive(ZRpc rpc,ZPackage package)
        {
            if(_server==null||!ReferenceEquals(rpc,_server)||!ReferenceEquals(_network,ZNet.instance)||_network.IsServer()||!ReferenceEquals(_network.GetServerPeer()?.m_rpc,rpc)||package==null||package.Size()>PlayerActionProtocol.MaximumBytes)return;
            string id="";
            try
            {
                id=package.ReadString();long character=package.ReadLong(),expires=package.ReadLong();string action=package.ReadString(),args=package.ReadString();
                long now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if(id.Length!=32||!Guid.TryParseExact(id,"N",out _)||package.GetPos()!=package.Size()||expires<now||expires>now+45)throw new InvalidOperationException("Invalid or expired player action.");
                string body=character+"\n"+expires+"\n"+action+"\n"+args;
                if(_seen.TryGetValue(id,out var old))
                {if(old.Body!=body)throw new InvalidOperationException("Reused action identity.");Reply(id,old.Success,old.Message);return;}
                if(_seen.Count>=128)throw new InvalidOperationException("Player action capacity reached.");
                // Reserve the operation before invoking game code: exceptions must not permit replay.
                var receipt=new Receipt{Body=body,Expires=now+90,Message="Action did not complete."};_seen.Add(id,receipt);
                try
                {
                    args=PlayerActionProtocol.Validate(action,args);
                    var player=Player.m_localPlayer;
                    if(player==null||player.GetPlayerID()!=character||player.IsDead())throw new InvalidOperationException("The selected character changed, is dead, or is not ready.");
                    if(!Achievements.IsCheatedAtAll())throw new InvalidOperationException("This player must explicitly run confirmcheats in their own F5 console before receiving cheat actions. Sentinel does not confirm it for them.");
                    Apply(player,action,args);receipt.Success=true;receipt.Message=action+" completed on "+player.GetPlayerName()+".";
                    player.Message(MessageHud.MessageType.TopLeft,"Server administrator: "+action+" "+args);
                }
                catch(Exception error){receipt.Message=error.Message;}
                Reply(id,receipt.Success,receipt.Message);
            }
            catch(Exception error){if(id.Length==32)Reply(id,false,error.Message);}
        }
        private static void Apply(Player player,string action,string args)
        {
            var parts=args.Split(' ');
            switch(action)
            {
                case "raiseskill":player.GetSkills().CheatRaiseSkill(parts[0],int.Parse(parts[1],CultureInfo.InvariantCulture));break;
                case "resetskill":player.GetSkills().CheatResetSkill(parts[0]);break;
                case "heal":player.Heal(player.GetMaxHealth());player.AddStamina(player.GetMaxStamina());player.AddEitr(player.GetMaxEitr());break;
                case "puke":player.ClearFood();break;
                case "clearstatus":player.ClearHardDeath();player.GetSEMan().RemoveAllStatusEffects();break;
                case "adrenaline":player.AddAdrenaline(int.Parse(args,CultureInfo.InvariantCulture)-player.GetAdrenaline());break;
                case "addstatus":
                    var effect=ObjectDB.instance?.m_StatusEffects.FirstOrDefault(s=>s!=null&&s.name==args);
                    if(effect==null)throw new InvalidOperationException("Status effect is not installed on this player's client.");
                    if(player.GetSEMan().AddStatusEffect(effect.name.GetStableHashCode(),true)==null)throw new InvalidOperationException("The player could not receive this status effect.");break;
            }
        }
        private void Reply(string id,bool ok,string text)
        {var p=new ZPackage();p.Write(id);p.Write(ok);p.Write(text.Length>600?text.Substring(0,600):text);_server?.Invoke(PlayerActionProtocol.ResultRpc,p);}
        public void Dispose()
        {if(_server!=null)try{_server.Unregister(PlayerActionProtocol.ActionRpc);}catch{} _server=null;_network=null;_seen.Clear();}
    }
}
