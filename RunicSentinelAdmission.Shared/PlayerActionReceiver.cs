using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RunicSentinel.PlayerActions
{
    // There is no public arbitrary-command endpoint. Only this connection's server
    // can send a bounded allowlisted action for the currently loaded character.
    internal sealed class PlayerActionReceiver:IDisposable
    {
        [ThreadStatic] private static int _notificationDepth;
        internal static void WithoutNotifications(Action action)
        {
            _notificationDepth++;
            try { action(); }
            finally { _notificationDepth--; }
        }
        // Suppress only synchronous HUD notices produced by a received administrator action
        // or restoration of its granted modes. Normal gameplay messages remain unaffected.
        [HarmonyLib.HarmonyPatch(typeof(MessageHud), "ShowMessage")]
        private static class AdministratorNotificationPatch
        {
            internal static bool Prefix() => _notificationDepth == 0;
        }
        private ZNet _network;
        private ZRpc _server;
        private float _nextHello;
        private static PlayerActionReceiver _active;
        private Player _modePlayer;
        private readonly Dictionary<string,bool> _modeOriginal=new Dictionary<string,bool>();
        private readonly HashSet<string> _grantedModes=new HashSet<string>();
        internal static bool Granted(string mode)
        {
            var a=_active;
            return a!=null&&a._server!=null&&a._modePlayer!=null&&ReferenceEquals(a._modePlayer,Player.m_localPlayer)&&ReferenceEquals(a._network,ZNet.instance)&&ReferenceEquals(a._server,ZNet.instance?.GetServerPeer()?.m_rpc)&&a._grantedModes.Contains(mode);
        }
        private static bool Mode(Player p,string mode)=>mode=="god"?p.InGodMode():mode=="ghost"?p.InGhostMode():mode=="fly"?p.IsDebugFlying():p.NoCostCheat();
        private static void SetMode(Player p,string mode,bool value)
        {
            if(mode=="god")p.SetGodMode(value);
            else if(mode=="ghost")p.SetGhostMode(value);
            else if(mode=="nocost")p.SetNoPlacementCost(value);
            else
            {
                var f=typeof(Player).GetField("m_debugFly",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                if(f==null)throw new InvalidOperationException("Flight state is unavailable.");
                if((bool)f.GetValue(p)!=value)p.ToggleDebugFly();
            }
        }
        private void SetGrantedMode(Player p,string mode,bool value)
        {
            if(!ReferenceEquals(_modePlayer,p)){ClearModes();_modePlayer=p;}
            if(!_modeOriginal.ContainsKey(mode))_modeOriginal[mode]=Mode(p,mode);
            _active=this;_grantedModes.Add(mode);
            try {SetMode(p,mode,value);}
            catch {_grantedModes.Remove(mode);throw;}
            if(!value)_grantedModes.Remove(mode);
        }
        private void ClearModes()
        {
            if(_modePlayer!=null)foreach(var pair in _modeOriginal)try{WithoutNotifications(()=>SetMode(_modePlayer,pair.Key,pair.Value));}catch{}
            _modeOriginal.Clear();_grantedModes.Clear();_modePlayer=null;if(ReferenceEquals(_active,this))_active=null;
        }
        private static string Modes(Player p)=>"God: "+(p.InGodMode()?"On":"Off")+" · Ghost: "+(p.InGhostMode()?"On":"Off")+" · Fly: "+(p.IsDebugFlying()?"On":"Off")+" · No-cost: "+(p.NoCostCheat()?"On":"Off");
        private static FieldInfo EffectField(Type type,string name)
        {
            for(var current=type;current!=null;current=current.BaseType)
            {
                var field=current.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
                if(field!=null)return field;
            }
            return null;
        }
        private static void SetEffectField(object target,string name,float value)
        {
            var field=EffectField(target.GetType(),name);
            if(field==null)throw new InvalidOperationException("Status parameter is unavailable: "+name);
            field.SetValue(target,value);
        }
        private readonly Dictionary<string,Receipt> _seen=new Dictionary<string,Receipt>();
        private sealed class Receipt{internal string Body,Message;internal bool Success;internal long Expires;}
        internal void Tick()
        {
            var network=ZNet.instance;var peer=network!=null&&!network.IsServer()?network.GetServerPeer():null;
            var rpc=peer!=null&&peer.IsReady()?peer.m_rpc:null;
            if(!ReferenceEquals(network,_network)||!ReferenceEquals(rpc,_server))
            {Dispose();_network=network;_server=rpc;_nextHello=0;if(rpc!=null)rpc.Register<ZPackage>(PlayerActionProtocol.ActionRpc,Receive);}
            if(_modePlayer!=null&&!ReferenceEquals(_modePlayer,Player.m_localPlayer))ClearModes();
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
                    if(action!="modes"&&!Achievements.IsCheatedAtAll())throw new InvalidOperationException("This player must explicitly run confirmcheats in their own F5 console before receiving cheat actions. Sentinel does not confirm it for them.");
                    WithoutNotifications(()=>Apply(player,action,args));receipt.Success=true;receipt.Message=(action=="modes"||new[]{"god","ghost","fly","nocost"}.Contains(action))?Modes(player):action+" completed on "+player.GetPlayerName()+".";
                }
                catch(Exception error){receipt.Message=error.Message;}
                Reply(id,receipt.Success,receipt.Message);
            }
            catch(Exception error){if(id.Length==32)Reply(id,false,error.Message);}
        }
        private void Apply(Player player,string action,string args)
        {
            var parts=args.Split(' ');
            switch(action)
            {
                case "modes":break;
                case "god":case "ghost":case "fly":case "nocost":SetGrantedMode(player,action,args=="on");break;
                case "raiseskill":player.GetSkills().CheatRaiseSkill(parts[0],int.Parse(parts[1],CultureInfo.InvariantCulture));break;
                case "resetskill":player.GetSkills().CheatResetSkill(parts[0]);break;
                case "heal":player.Heal(player.GetMaxHealth());player.AddStamina(player.GetMaxStamina());player.AddEitr(player.GetMaxEitr());break;
                case "puke":player.ClearFood();break;
                case "clearstatus":player.ClearHardDeath();player.GetSEMan().RemoveAllStatusEffects();break;
                case "adrenaline":player.AddAdrenaline(int.Parse(args,CultureInfo.InvariantCulture)-player.GetAdrenaline());break;
                case "addstatus":
                    var template=ObjectDB.instance?.m_StatusEffects.FirstOrDefault(e=>e!=null&&e.name.Equals(parts[0],StringComparison.OrdinalIgnoreCase));
                    if(template==null)throw new InvalidOperationException("Status effect is not installed on this player's client.");
                    if(parts.Length>2 && !(template is SE_Shield || template is SE_Burning || template is SE_Poison))throw new InvalidOperationException("This status effect has no intensity parameter.");
                    var effect=player.GetSEMan().AddStatusEffect(template.name.GetStableHashCode(),true);
                    if(effect==null)throw new InvalidOperationException("The player could not receive this status effect.");
                    if(parts.Length>1)effect.m_ttl=float.Parse(parts[1],CultureInfo.InvariantCulture);
                    if(parts.Length>2)
                    {
                        float intensity=float.Parse(parts[2],CultureInfo.InvariantCulture);
                        if(effect is SE_Shield)SetEffectField(effect,"m_absorbDamage",intensity);
                        if(effect is SE_Burning burning)
                        {
                            bool fire=template.name=="Burning";
                            SetEffectField(effect,fire?"m_fireDamageLeft":"m_spiritDamageLeft",0);
                            var method=effect.GetType().GetMethod(fire?"AddFireDamage":"AddSpiritDamage",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                            if(method==null)throw new InvalidOperationException("Burning intensity is unavailable.");
                            method.Invoke(burning,new object[]{intensity});
                        }
                        if(effect is SE_Poison)
                        {
                            SetEffectField(effect,"m_damageLeft",intensity);
                            var interval=EffectField(effect.GetType(),"m_damageInterval");
                            if(interval==null)throw new InvalidOperationException("Poison intensity is unavailable.");
                            SetEffectField(effect,"m_damagePerHit",intensity/Math.Max(0.001f,effect.m_ttl)*(float)interval.GetValue(effect));
                        }
                    }
                    break;
            }
        }
        private void Reply(string id,bool ok,string text)
        {var p=new ZPackage();p.Write(id);p.Write(ok);p.Write(text.Length>600?text.Substring(0,600):text);_server?.Invoke(PlayerActionProtocol.ResultRpc,p);}
        public void Dispose()
        {ClearModes();if(_server!=null)try{_server.Unregister(PlayerActionProtocol.ActionRpc);}catch{} _server=null;_network=null;_seen.Clear();}
    }
}
