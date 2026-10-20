using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Runtime.Loader;
using RunicSentinel.Core;
using RunicSentinel.Runtime;
using RunicSentinel.PlayerActions;

namespace RunicSentinel.Tests
{
    internal static partial class Program
    {
        private const string BuildingConfig = "# keep\n[General]\nEnabled = true\n[Building and Terrain]\nEnabled = false\nVerticalSupportMultiplier = 1\nMaximumTerrainRaise = 8\nMaximumDigDepth = 8\n[Other]\nSecret = unchanged\n";
        private static int WorldPanelChecks()
        {
            AssemblyLoadContext.Default.Resolving += (_, name) => { string p=Path.Combine("E:/SteamLibrary/steamapps/common/Valheim/valheim_Data/Managed",name.Name+".dll");return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null; };
            var checks = new (string,Action)[] { ("building request validation", BuildingWire), ("status compatibility", BuildingDocument), ("scoped config format and ambiguity", BuildingConfigFormat), ("atomic save backup and stale rejection", BuildingSave), ("authorized restart-only F3 routing", BuildingRouting), ("recipient notification scope and recovery", QuietActions) };
            int failed=0;
            foreach(var check in checks)try{check.Item2();System.Console.WriteLine("PASS "+check.Item1);}catch(Exception e){failed++;System.Console.WriteLine("FAIL "+check.Item1+": "+e);}
            return failed==0?0:1;
        }
        private static void BuildingWire()
        {
            var request=new BuildingSettings{Revision=new string('A',64),Enabled=true,Limits=new BuildingLimits(2,24,32)};
            var result=BuildingSettings.Decode(request.Encode()); Equal(32f,result.Limits.DigDepth);True(result.Enabled);
            False(BuildingSettings.TryLimits("NaN","24","32",out _));False(BuildingSettings.TryLimits("11","24","32",out _));False(BuildingSettings.TryLimits("2","65","32",out _));
            False(BuildingSettings.TryLimits("2","24","0",out _));True(BuildingSettings.TryLimits("2.5","24","32",out _));
            Throws<InvalidDataException>(()=>BuildingSettings.Decode(request.Encode().Concat(new byte[]{10}).ToArray()));
            request.Revision="../config";Throws<InvalidDataException>(()=>BuildingSettings.Decode(request.Encode()));
        }
        private static void BuildingDocument()
        {
            var input=new SentinelAdminDocument{Sequence=1,BuildingSaved="abc",BuildingActive="running",BuildingStatus="restart",CapacitySavedPlayers="20",Profile="keep"};
            True(SentinelAdminProtocol.TryDecode(SentinelAdminProtocol.Encode(input),out var output));Equal("abc",output.BuildingSaved);Equal("20",output.CapacitySavedPlayers);Equal("keep",output.Profile);
            var old=string.Join("\n",Encoding.UTF8.GetString(SentinelAdminProtocol.Encode(input)).Split('\n').Where(l=>!l.StartsWith("building-")));
            True(SentinelAdminProtocol.TryDecode(Encoding.UTF8.GetBytes(old),out output));Equal("",output.BuildingSaved);
        }
        private static void BuildingConfigFormat()
        {
            foreach(var bad in new[]{BuildingConfig.Replace("MaximumDigDepth = 8","MaximumDigDepth = 8\nMaximumDigDepth = 9"),BuildingConfig+"[Building and Terrain]\n",BuildingConfig.Replace("MaximumDigDepth = 8",""),BuildingConfig.Replace("MaximumDigDepth = 8","MaximumDigDepth = NaN")})
                Throws<InvalidDataException>(()=>BuildingSettings.Parse(Encoding.UTF8.GetBytes(bad)));
            foreach(string newline in new[]{"\n","\r\n"})foreach(bool bom in new[]{false,true})
            {
                var parsed=BuildingSettings.Parse(Encoding.UTF8.GetBytes((bom?"\ufeff":"")+BuildingConfig.Replace("\n",newline)));
                Equal(8f,parsed.Limits.DigDepth);False(parsed.Enabled);
            }
        }
        private static void BuildingSave()
        {
            string root=Path.Combine(Path.GetTempPath(),"SentinelWorldPanel-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            foreach(string newline in new[]{"\n","\r\n"})foreach(bool bom in new[]{false,true})
            {
                string path=Path.Combine(root,Guid.NewGuid().ToString("N")+".cfg");
                string original=(bom?"\ufeff":"")+BuildingConfig.Replace("\n",newline);File.WriteAllBytes(path,Encoding.UTF8.GetBytes(original));
                var request=BuildingSettings.Read(path);request.Enabled=true;request.Limits=new BuildingLimits(2,24,32);
                BuildingSettings.Save(path,request);
                Equal(original,Encoding.UTF8.GetString(File.ReadAllBytes(path+".building.bak")));
                string expected=original.Replace("Enabled = false","Enabled = true").Replace("VerticalSupportMultiplier = 1","VerticalSupportMultiplier = 2").Replace("MaximumTerrainRaise = 8","MaximumTerrainRaise = 24").Replace("MaximumDigDepth = 8","MaximumDigDepth = 32");
                Equal(expected,Encoding.UTF8.GetString(File.ReadAllBytes(path)));
                Throws<InvalidOperationException>(()=>BuildingSettings.Save(path,request));Equal(expected,Encoding.UTF8.GetString(File.ReadAllBytes(path)));
                File.Delete(path);File.Delete(path+".building.bak");
            }
            Equal(0,Directory.GetFiles(root).Length);Directory.Delete(root);
        }
        private static void BuildingRouting()
        {
            string control=Read("RunicSentinel","Runtime","SentinelAdminControl.cs");string execute=control.Substring(control.IndexOf("private void Execute",StringComparison.Ordinal));
            True(execute.IndexOf("SentinelAdministratorRules.Allows",StringComparison.Ordinal)<execute.IndexOf("SentinelBuildingBridge.Save",StringComparison.Ordinal));
            True(execute.IndexOf("_runtime.IsBanned",StringComparison.Ordinal)<execute.IndexOf("SentinelBuildingBridge.Save",StringComparison.Ordinal));
            Contains(control,"TryExecuteLocal(\"building\"");Contains(control,"Submit(\"building\"");
            string panel=Read("RunicSentinel","Runtime","SentinelAdminPanel.cs");Contains(panel,"PT(\"World\")");Contains(panel,"DrawBuilding();");Contains(panel,"ResetBuildingDraft();");
            string building=Read("RunicSentinel","Runtime","SentinelAdminPanel.Building.cs");Contains(building,"!_requesting && valid && changed");Contains(building,"_control.SaveBuilding");
            string bridge=Read("RunicSentinel","Runtime","SentinelBuildingBridge.cs");Contains(bridge,"!ZNet.instance.IsServer()");Contains(bridge,"config.SaveOnConfigSet = false");False(bridge.Contains("Configure("));False(bridge.Contains("Config.Reload"));
        }
        private static void QuietActions()
        {
            var patch=typeof(PlayerActionReceiver).GetNestedType("AdministratorNotificationPatch",BindingFlags.NonPublic);
            var prefix=patch.GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static);
            Func<bool> allowed=()=> (bool)prefix.Invoke(null,null);
            True(allowed());
            PlayerActionReceiver.WithoutNotifications(()=>{False(allowed());PlayerActionReceiver.WithoutNotifications(()=>False(allowed()));False(allowed());});True(allowed());
            Throws<InvalidOperationException>(()=>PlayerActionReceiver.WithoutNotifications(()=>throw new InvalidOperationException("fixture")));True(allowed());
            string source=Read("RunicSentinelAdmission.Shared","PlayerActionReceiver.cs");False(source.Contains("Server administrator: "));Contains(source,"Reply(id,receipt.Success,receipt.Message)");Contains(source,"WithoutNotifications(()=>Apply(player,action,args))");Contains(source,"Achievements.IsCheatedAtAll()");
        }
    }
}
