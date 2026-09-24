#nullable enable
using HarmonyLib;
namespace RunicSentinel.Devcommands;
[HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.Awake))]
public class FejdStartupAwake {
  static void Postfix() {
    if (Settings.AutoExecBoot != "") Console.instance.TryRunCommand(Settings.AutoExecBoot);
  }
}
[HarmonyPatch(typeof(Game), nameof(Game.Awake))]
public class AutoExec {
  private static Game? PendingGame;
  private static string Pending="";
  internal static void Tick(){if(Pending.Length==0)return;if(PendingGame!=Game.instance){Pending="";return;}if(Console.instance==null||(!SentinelBridge.Authorized&&!(ZNet.instance!=null&&ZNet.instance.IsServer())))return;var command=Pending;Pending="";Console.instance.TryRunCommand(command);}
  static void Postfix() {
    PendingGame=Game.instance;Pending=Settings.AutoExec;
  }
}