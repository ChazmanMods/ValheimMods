#nullable enable
using HarmonyLib;
namespace RunicSentinel.Devcommands;
public static class Admin
{
 public static bool Checking{get;set;}
 public static void ManualCheck()=>SentinelBridge.Refresh();
 public static void AutomaticCheck()=>SentinelBridge.Refresh();
 public static bool Verify(string text)=>false;
 public static void Reset(){SentinelBridge.Reset();DevcommandsCommand.Set(false);}
 public static void ReceivePermissions(ZRpc rpc,ZPackage pkg){if(ZNet.instance!=null&&ReferenceEquals(rpc,ZNet.instance.GetServerRPC())&&SentinelBridge.Authorized){PermissionManager.Instance.Read(pkg);PermissionManager.Instance.IsAdmin=true;}}
}
[HarmonyPatch(typeof(Game),nameof(Game.Start))]public class AdminReset{static void Postfix()=>Admin.Reset();}
[HarmonyPatch(typeof(Player),nameof(Player.OnSpawned))]public class AdminCheck{static void Postfix(){Admin.AutomaticCheck();if(SentinelBridge.Authorized)DevcommandsCommand.EnableAutoFeatures();}}
