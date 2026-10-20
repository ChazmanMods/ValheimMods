using BepInEx;
using HarmonyLib;
using UnityEngine;
namespace RunicSettlements;
[BepInPlugin(Id,"Runic Settlements",Version)]
[BepInIncompatibility("com.chazman.runicpaths")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Id="com.chazman.runicsettlements",Version="0.1.2";
    internal const string NaturalKey="RunicSettlements.Natural";
    internal static Plugin? Instance;
    private PreparedRuntime? runtime;
    private Harmony? harmony;
    internal static bool CanTagNature=>Instance!=null;
    internal static void Report(string message)=>Instance?.Logger.LogInfo(message);
    internal static bool SuppressRoadClutter(Vector3 point,Heightmap map)=>PreparedRuntime.Active!=null && PreparedRuntime.Active.Cleared(GameTerrain.PointOf(point));
    private void Awake()
    {
        Instance=this;runtime=new PreparedRuntime(this,Config);
        harmony=new Harmony(Id);harmony.PatchAll();
        Report("Runic Settlements "+Version+": community clearings and paved roads; terrain is prepared before spawn.");
    }
    private void Update()=>runtime?.Update(true);
    private void OnGUI()=>runtime?.DrawStatus();
    private void OnDestroy(){runtime?.Dispose();harmony?.UnpatchSelf();Instance=null;}
}
