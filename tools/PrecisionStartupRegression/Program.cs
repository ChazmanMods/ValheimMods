using System.Reflection;
using QuietBuildRotation;
using HarmonyLib;

static class Program {
 static void Check(bool value, string message) { if (!value) throw new Exception(message); }
 static void Call(Plugin p, string name) => typeof(Plugin).GetMethod(name, BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(p, null);
 static void Reset() { Diagnostics.RuntimeAvailable=false; Harmony.Deferred=false; Harmony.Throw=false; Harmony.Patched=false; Harmony.Removals=0; PlacementAdapter.Valid=true; PlacementAdapter.InitializeOk=true; PlacementAdapter.Verifications=0; PlacementRuntime.Fail=false; PlacementRuntime.Initializations=0; CompatibilityGuard.Conflict=false; }
 static void Main() {
  foreach (bool deferred in new[]{false,true}) {
   Reset(); Harmony.Deferred=deferred; var p=new Plugin(); Call(p,"Awake");
   Check(!Diagnostics.RuntimeAvailable && PlacementAdapter.Verifications==0 && Harmony.Removals==0,"Awake must not verify, activate, or remove pending patches");
   if (deferred) Harmony.Flush();
   Call(p,"Start"); Check(Diagnostics.RuntimeAvailable && PlacementRuntime.Initializations==1,"Startup must activate after applied patches pass verification");
   Call(p,"Start"); Check(PlacementAdapter.Verifications==1 && PlacementRuntime.Initializations==1,"Completion must be one-shot");
   Call(p,"OnDestroy"); Console.WriteLine("PASS "+(deferred?"deferred batch":"immediate patches"));
  }
  foreach(string failure in new[]{"unflushed","invalid shape","runtime init","patch exception","conflict","adapter init","destroyed"}) {
   Reset();
   Harmony.Deferred=failure=="unflushed";
   PlacementAdapter.Valid=failure!="invalid shape";
   PlacementRuntime.Fail=failure=="runtime init";
   Harmony.Throw=failure=="patch exception";
   CompatibilityGuard.Conflict=failure=="conflict";
   PlacementAdapter.InitializeOk=failure!="adapter init";
   var p=new Plugin(); Call(p,"Awake"); if(failure=="destroyed") Call(p,"OnDestroy"); Call(p,"Start");
   Check(!Diagnostics.RuntimeAvailable,"Failure must not activate: "+failure);
   if(failure=="unflushed" || failure=="invalid shape" || failure=="runtime init" || failure=="patch exception") Check(Harmony.Removals==1,"Failed startup must unpatch: "+failure);
   if(failure=="conflict" || failure=="adapter init" || failure=="patch exception" || failure=="destroyed") Check(PlacementAdapter.Verifications==0,"Start must not retry failed/cancelled registration: "+failure);
   if(failure!="destroyed") Call(p,"OnDestroy"); Console.WriteLine("PASS "+failure);
  }
  Console.WriteLine("PASS: 9 startup lifecycle scenarios using the production Plugin class");
 }
}
namespace BepInEx {
 [AttributeUsage(AttributeTargets.Class)] public class BepInPlugin:Attribute { public BepInPlugin(string a,string b,string c){} }
 [AttributeUsage(AttributeTargets.Class)] public class BepInDependency:Attribute { public enum DependencyFlags{SoftDependency} public BepInDependency(string a,DependencyFlags b){} }
 public class BaseUnityPlugin { public Logging.ManualLogSource Logger=new(); public object Config=new(); }
}
namespace BepInEx.Logging { public class ManualLogSource { public void LogInfo(object s){} public void LogWarning(object s){} } }
namespace HarmonyLib { public class Harmony {
 public static bool Deferred,Throw,Patched; public static int Removals;
 public Harmony(string s){} public void PatchAll(Assembly a){if(Throw)throw new Exception("patch");if(!Deferred)Patched=true;}
 public static void Flush()=>Patched=true;
 public void UnpatchSelf(){Removals++;Patched=false;}
} }
namespace UnityEngine { public class Hud { public static Hud instance=null; } }
namespace Splatform { }
namespace QuietBuildRotation.Integration { }
namespace QuietBuildRotation {
 static class PluginConfig { public static event Action Changed { add {} remove {} } public static void Bind(object c){} public static string FindExactChordConflicts()=>null; }
 static class CompatibilityGuard { public const string PerfectPlacementGuid="test"; public static bool Conflict; public static bool ShouldDisableForPerfectPlacement(out string r){r=Conflict?"conflict":null;return Conflict;} }
 static class Diagnostics {
 public static bool RuntimeAvailable; public static void Initialize(object l)=>RuntimeAvailable=false;
 public static void DisableForCompatibility(string s)=>RuntimeAvailable=false; public static void DisableAdapter(string s)=>RuntimeAvailable=false;
 public static void MarkRuntimeAvailable()=>RuntimeAvailable=true; public static string GetValheimVersion()=>"test";
 }
 static class PlacementAdapter {
 public static bool Valid=true,InitializeOk=true; public static int Verifications;
 public static bool Initialize(out string e){e="adapter";return InitializeOk;}
 public static bool CompleteRuntimeVerification(string s,out string e){Verifications++;e="invalid hooks";return Harmony.Patched && Valid;}
 }
 static class PlacementRuntime {
 public static bool Fail; public static int Initializations;
 public static void Initialize(){Initializations++;if(Fail)throw new Exception("init");}
 public static void Shutdown(){} public static void OnApplicationFocus(bool b){} public static void UpdateUtilities(){} public static void OnConfigurationChanged(){}
 }
 static class RunicHookGuard { public static void Disable(string s,Exception e)=>Diagnostics.DisableAdapter(s); }
}
namespace QuietBuildRotation.UI { static class OrientationPresenter { public static void Attach(UnityEngine.Hud h){} public static void Destroy(){} } }
