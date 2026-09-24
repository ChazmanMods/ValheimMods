using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using RunicSentinel.Core;

namespace RunicSentinel.Runtime
{
    // Only SentinelAdminControl calls Authorize after authenticating the current peer,
    // checking roles/bans and rejecting replayed or expired requests.
    internal static class SentinelCommandProvider
    {
        internal const string ClientRoute="sentinel-client-v1\n";
        internal const string ServerRoute="sentinel-server-v1\n";
        private static readonly FieldInfo Commands=AccessTools.Field(typeof(Terminal),"commands");
        private static readonly FieldInfo Action=AccessTools.Field(typeof(Terminal.ConsoleCommand),"action");
        private static readonly FieldInfo Failable=AccessTools.Field(typeof(Terminal.ConsoleCommand),"actionFailable");
        private static List<string> _serverOutput;
        internal static bool Available(out string status)
        {
            bool ready=ZNet.instance!=null;
            status=ready?"Sentinel command service · authenticated per request":"Join a world to use Sentinel commands.";
            return ready;
        }
        internal static IReadOnlyList<Terminal.ConsoleCommand> Catalog()
        {
            if(!(Commands.GetValue(null) is Dictionary<string,Terminal.ConsoleCommand> commands))return Array.Empty<Terminal.ConsoleCommand>();
            // These entries describe Sentinel-owned RPC actions; they never replace native commands.
            if(!commands.ContainsKey("tp"))new Terminal.ConsoleCommand("tp","<player name or account> | <destination player or x,z,y> — teleport a player. Use | to separate names containing spaces.",a=>a.Context.AddString("Run this command from Sentinel F3."));
            if(!commands.ContainsKey("server"))new Terminal.ConsoleCommand("server","<world command> — execute a world command on the authenticated server.",a=>a.Context.AddString("Run this command from Sentinel F3."));
            return commands.Values.Where(c=>c!=null).OrderBy(c=>c.Command,StringComparer.OrdinalIgnoreCase).Take(2048).ToArray();
        }
        internal static bool ExecutesOnServer(string line)
        {
            string name=line.Split(' ')[0];return name.Equals("server",StringComparison.OrdinalIgnoreCase)||name.Equals("tp",StringComparison.OrdinalIgnoreCase)||name.Equals("shutdown",StringComparison.OrdinalIgnoreCase)||name.Equals("permissions",StringComparison.OrdinalIgnoreCase)||SentinelCommandRouting.IsWorldCommand(name);
        }
        internal static string Authorize(string line,string account)
        {
            Catalog();
            bool explicitServer=line.StartsWith("server ",StringComparison.OrdinalIgnoreCase);
            if(explicitServer){line=line.Substring(7).Trim();if(!RunicSentinel.Devcommands.TerminalUtils.SkipProcessing(line))line=RunicSentinel.Devcommands.TryRunCommand.CheckLogic(line);}
            string name=line.Split(' ')[0].ToLowerInvariant();
            if(explicitServer&&new[]{"spawn","god","debugmode","nocost","fly","freefly","addstatus","raiseskill","resetskill","heal","puke","adrenaline","clearstatus"}.Contains(name))throw new InvalidOperationException("This command requires a player client. Run it without the server prefix.");
            var command=Catalog().FirstOrDefault(c=>c.Command.Equals(name,StringComparison.OrdinalIgnoreCase));
            if(command==null)throw new InvalidOperationException("Command is not registered on this server: "+name);
            ZNetPeer requestingPeer=null;try{requestingPeer=ResolvePlayer(account);}catch{}
            if(requestingPeer!=null)
            {
                var character=ZDOMan.instance.GetZDO(requestingPeer.m_characterID);
                var rules=RunicSentinel.Devcommands.PermissionLoader.Data.Resolve(requestingPeer.m_rpc.GetSocket().GetHostName(),character?.GetLong(ZDOVars.s_playerID).ToString()??"");
                rules.IsAdmin=true;
                if(!rules.IsCommandAllowed(command,line,true))throw new InvalidOperationException("This command is restricted by the server's Sentinel command permissions.");
            }
            if(name=="tp"&&line.Contains("|"))return ServerRoute+Teleport(line);
            if(!explicitServer&&!SentinelCommandRouting.IsWorldCommand(name)&&name!="tp"&&name!="shutdown"&&name!="permissions")
            {
                if(explicitServer)throw new InvalidOperationException("This command requires a player client. Run it without the server prefix.");
                return ClientRoute;
            }
            if(_serverOutput!=null)throw new InvalidOperationException("A server command is already executing.");
            _serverOutput=new List<string>();
            var previousTarget=RunicSentinel.Devcommands.RedirectOutput.Target;
            try{RunicSentinel.Devcommands.RedirectOutput.Target=ResolvePlayer(account).m_rpc;}catch{RunicSentinel.Devcommands.RedirectOutput.Target=null;}
            try
            {
                if(name=="event"&&line.Split(' ').Length<=2)
                {
                    string eventName=line.Length>5?line.Substring(6).Trim():"";
                    if(string.IsNullOrEmpty(eventName)||!RandEventSystem.instance.HaveEvent(eventName))throw new InvalidOperationException("Choose a valid event name.");
                    var peer=ResolvePlayer(account);
                    RandEventSystem.instance.SetRandomEventByName(eventName,peer.m_refPos);
                    _serverOutput.Add("Event started at "+peer.m_playerName+"'s position.");
                }
                else
                {
                    if(name=="setworldpreset"||name=="setworldmodifier")
                    {
                        var options=Type.GetType("ServerOptionsGUI, assembly_valheim",false);
                        if(options?.GetField("m_instance",BindingFlags.Public|BindingFlags.Static)?.GetValue(null)==null)
                            throw new InvalidOperationException("Valheim's world modifier service is not ready; no world settings were changed.");
                    }
                    var terminal=Console.instance;
                    if(terminal==null)throw new InvalidOperationException("The server console is not ready.");
                    var args=new Terminal.ConsoleEventArgs(line,terminal,command);
                    // Invoke the loaded native handler without touching global cheat flags or
                    // a dedicated server's nonexistent character achievement profile.
                    if(Action.GetValue(command) is Terminal.ConsoleEvent action)action(args);
                    else if(Failable.GetValue(command) is Terminal.ConsoleEventFailable failable)
                    {
                        object result=failable(args);
                        if(result is bool ok&&!ok)throw new InvalidOperationException("Invalid parameters. "+command.Command+" "+command.Description);
                        if(result is string error)throw new InvalidOperationException(error);
                    }
                    else throw new InvalidOperationException("The installed command has no handler.");
                }
                return ServerRoute+(_serverOutput.Count==0?"Server handler completed. Any asynchronous operation may still be running.":string.Join("\n",_serverOutput));
            }
            finally{_serverOutput=null;RunicSentinel.Devcommands.RedirectOutput.Target=previousTarget;}
        }
        internal static void Capture(Terminal terminal,string text)
        {
            if(_serverOutput==null||!ReferenceEquals(terminal,Console.instance)||string.IsNullOrEmpty(text))return;
            if(_serverOutput.Count<80)_serverOutput.Add(text.Length>1024?text.Substring(0,1024):text);
        }
        private static ZNetPeer ResolvePlayer(string identifier)
        {
            var matches=ZNet.instance.GetPeers().Where(p=>p!=null&&p.IsReady()&&!p.m_server&&
                (string.Equals(p.m_playerName,identifier,StringComparison.OrdinalIgnoreCase)||
                (SentinelTransportIdentity.TryResolvePeer(p,out string authority,out string subject,out _)&&string.Equals(authority+":"+subject,identifier,StringComparison.Ordinal)))).ToArray();
            if(matches.Length!=1)throw new InvalidOperationException("Player not found or name is ambiguous; select an exact online account from Players.");
            return matches[0];
        }
        private static string Teleport(string line)
        {
            string arguments=line.Length>2?line.Substring(3).Trim():"";
            string[] parts=arguments.Contains("|")?arguments.Split('|'):arguments.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
            if(parts.Length!=2)throw new InvalidOperationException("Usage: tp player | destination-player OR tp player | x,z,y");
            var player=ResolvePlayer(parts[0].Trim());string destination=parts[1].Trim();Vector3 position;
            string[] coordinates=destination.Split(',');
            if(coordinates.Length==3)
            {
                if(!SentinelCommandRouting.TryCoordinates(destination,out float x,out float y,out float z))throw new InvalidOperationException("Coordinates must be finite and between -20000 and 20000.");
                position=new Vector3(x,y,z);
            }
            else position=ResolvePlayer(destination).m_refPos;
            if(player.m_characterID==ZDOID.None||ZRoutedRpc.instance==null)throw new InvalidOperationException("Player character is not ready.");
            ZRoutedRpc.instance.InvokeRoutedRPC(player.m_uid,player.m_characterID,"RPC_TeleportTo",position,Quaternion.identity,true);
            return "Teleport delivered to "+player.m_playerName+" at "+position.ToString("F1")+". Check the player's next location update; a busy client may defer or reject teleporting.";
        }
    }
    [HarmonyPatch(typeof(Terminal),nameof(Terminal.AddString),new[]{typeof(string)})]
    internal static class SentinelServerCommandOutputPatch
    {
        private static void Postfix(Terminal __instance,string text)=>SentinelCommandProvider.Capture(__instance,text);
    }
}
