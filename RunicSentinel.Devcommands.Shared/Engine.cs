#nullable enable
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using RunicSentinel.Devcommands.Service;
namespace RunicSentinel.Devcommands;
public static class Engine
{
 public const string GUID="chazman.RunicSentinel";
 public const string NAME="Runic Sentinel Commands";
 public const string VERSION="1.109-integrated";
 public static void Stop()
 {
  Yaml.StopWatchers();SentinelBridge.Reset();SentinelBridge.Control=null;
  new Harmony("chazman.RunicSentinel.commands.comfygizmo").UnpatchSelf();
  new Harmony("chazman.RunicSentinel.commands.gizmoreloaded").UnpatchSelf();
 }
 public static void Init(ConfigFile config,ManualLogSource log){Log.Init(log);Settings.Init(config);PermissionLoader.SetupWatcher();}
 private static bool IntegrationsReady;
 public static void Integrations()
 {
  if(IntegrationsReady)return;IntegrationsReady=true;
  if(BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("bruce.valheim.comfymods.gizmo",out var comfy))ComfyGizmoPatcher.DoPatching(comfy.Instance.GetType().Assembly);
  if(BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("m3to.mods.GizmoReloaded",out var reloaded))GizmoReloadedPatcher.DoPatching(reloaded.Instance.GetType().Assembly);
  EWP.Api.RegisterGroupHandler("runicsentinel",PermissionApi.HasGroup);
 }
 public static void Tick(){Integrations();SentinelBridge.Tick();AutoExec.Tick();MultiCommands.Execute(Time.deltaTime);if(Player.m_localPlayer!=null)MouseWheelBinding.Execute(ZInput.GetMouseScrollWheel()*20f);if(AliasManager.ToBeSaved)AliasManager.ToFile();if(BindManager.ToBeSaved)BindManager.ToFile();
#if !RUNIC_SENTINEL_SERVER_ONLY
CommandInputResolver.CheckActiveRequest();
#endif
}
}
[HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal)), HarmonyPriority(Priority.HigherThanNormal)]
public class InitializeTerminal
{
  private static bool Initialized = false;
  static void Postfix()
  {
    if (Initialized) return;
    Initialized = true;
    new DevcommandsCommand();
    new ConfigCommand();
    new StartEventCommand();
    new PosCommand();
    new AliasCommand();
    new SearchIdCommand();
    new UndoRedoCommand();
    new ResolutionCommand();
    new WaitCommand();
    // Server routing stays owned by Sentinel.
    new HUDCommand();
    new NoMapCommand();
    new BindCommand();
    new BroadcastCommand();
    new SeedCommand();
    new MoveSpawn();
    new MappingCommand();
    new WindCommand();
    new EnvCommand();
    new GotoCommand();
    new InventoryCommand();
    new CalmCommand();
    new RepairCommand();
    new AddStatusCommand();
    new PlayerListCommand();
    new FindCommand();
    new PullCommand();
    new ResetDungeonCommand();
    new TeleportCommand();
    new SearchComponentCommand();
    new SearchItemCommand();
    new RPCCommand();
    new DmgCommand();
    new KillCommand();
    new PermissionsCommand();
    new ShutdownCommand();
    RunicSentinel.Runtime.SentinelCommandProvider.Catalog();
    AutoComplete.RegisterEmpty("server");AutoComplete.Offsets["server"]=0;
    DefaultAutoComplete.Register();
    AliasManager.Init();
    BindManager.Init();
  }
}

[HarmonyPatch(typeof(Chat), nameof(Chat.Awake))]
public class InitializeChat
{
  static void Postfix()
  {
    // Chat.Awake loads binds from player profile, so need to handle them after that.
    BindManager.Load();
  }
}

[HarmonyPatch(typeof(Console), nameof(Console.IsConsoleEnabled))]
public class IsConsoleEnabled
{
  static void Postfix(ref bool __result)
  {
    __result = true;
  }
}
