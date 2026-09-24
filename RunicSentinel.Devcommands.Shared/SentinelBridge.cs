#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using RunicSentinel.Runtime;
using RunicSentinel.Core;
using UnityEngine;
namespace RunicSentinel.Devcommands;
internal static class SentinelBridge
{
 internal static SentinelAdminControl? Control;
 private static ZNet? Network;
 private static float LeaseUntil;
#if !RUNIC_SENTINEL_SERVER_ONLY
 private static float NextRefresh;
 private static bool AutoActivated;
#endif
 internal static bool Authorized=>ZNet.instance!=null&&ReferenceEquals(Network,ZNet.instance)&&Time.realtimeSinceStartup<LeaseUntil;
 internal static void Reset(){Pending=0;MultiCommands.Clear();Network=null;LeaseUntil=0;
#if !RUNIC_SENTINEL_SERVER_ONLY
 NextRefresh=0;AutoActivated=false;
#endif
 PermissionManager.Instance.IsAdmin=false;}
 internal static void Tick()
 {
 #if !RUNIC_SENTINEL_SERVER_ONLY
  if(!ReferenceEquals(Network,ZNet.instance)){Reset();Network=ZNet.instance;}
  if(!Authorized)PermissionManager.Instance.IsAdmin=false;
  if(ZNet.instance==null||Player.m_localPlayer==null||Control==null||Time.realtimeSinceStartup<NextRefresh)return;
  NextRefresh=Time.realtimeSinceStartup+10;Refresh();
 #endif
 }
 internal static void Refresh()
 {
 #if !RUNIC_SENTINEL_SERVER_ONLY
  var network=ZNet.instance;
  Control?.RequestStatus((ok,document,reason)=>{
   if(network==null||!ReferenceEquals(network,ZNet.instance))return;
   Network=network;LeaseUntil=ok?Time.realtimeSinceStartup+15:0;
   PermissionManager.Instance.IsAdmin=ok;
   if(!ok)DevcommandsCommand.Set(false);
   else if(!AutoActivated){AutoActivated=true;if(Settings.AutoDevcommands)DevcommandsCommand.Set(true);ServerExecution.RequestIds();}
  });
 #endif
 }
 private static int Pending;
 internal static bool Busy=>Pending>0;
 internal static void Submit(Terminal terminal,string line)
 {
 #if !RUNIC_SENTINEL_SERVER_ONLY
  if(ZNet.instance==null)
  {
   string localName=line.Split(' ')[0];
   if(new[]{"alias","bind","unbind","printbinds","resetbinds","dev_config","resolution","help","clear"}.Contains(localName)&&Terminal.commands.TryGetValue(localName,out var localCommand))
   {localCommand.RunAction(new Terminal.ConsoleEventArgs(line,terminal,localCommand));return;}
   terminal.AddString("Join a Sentinel server before using administrator commands.");return;
  }
  if(Control==null){terminal.AddString("The Sentinel administrator channel is not ready.");return;}
  if(Pending>=16){terminal.AddString("The Sentinel command queue is full.");return;}
  string commandName=line.StartsWith("server ",StringComparison.OrdinalIgnoreCase)?line.Substring(7).TrimStart().Split(' ')[0]:line.Split(' ')[0];
  var handler=SentinelCommandProvider.Catalog().FirstOrDefault(c=>c.Command.Equals(commandName,StringComparison.OrdinalIgnoreCase));
  if(SentinelCommandProvider.ExecutesOnServer(line)&&handler?.IsCheat==true&&!Achievements.IsCheatedAtAll()){terminal.AddString("Run confirmcheats explicitly before using cheat commands.");return;}
  Pending++;var network=ZNet.instance;var rpc=network.GetServerRPC();string route="";
  SentinelCommandRequest.Dispatch(line,(text,callback)=>Control.AuthorizeCommand(text,(ok,result)=>{route=result;callback(ok,result);}),
   ()=>ReferenceEquals(network,ZNet.instance)&&(network.IsServer()||ReferenceEquals(rpc,network.GetServerRPC())),
   text=>{if(route==SentinelCommandProvider.ClientRoute)SentinelCommandConsole.Execute(text);else if(route.StartsWith(SentinelCommandProvider.ServerRoute,StringComparison.Ordinal)){var result=route.Substring(SentinelCommandProvider.ServerRoute.Length);terminal.AddString(result);SentinelCommandConsole.Write(result);}else throw new InvalidOperationException("Incompatible Sentinel server command response.");},
   (ok,result)=>{Pending=Math.Max(0,Pending-1);if(!ok){terminal.AddString(result);SentinelCommandConsole.Write(result);}});
 #else
  terminal.AddString("Use the local server console or the authenticated Sentinel admin channel.");
 #endif
 }
 internal static bool AllowRpc(ZRpc rpc)=>Control?.AllowsCommandPeer(rpc)==true;
}
