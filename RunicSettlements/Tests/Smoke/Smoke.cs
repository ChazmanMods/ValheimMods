using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using RunicSettlements.Core;
using UnityEngine;
namespace RunicSettlements.Smoke;
[BepInPlugin("com.chazman.runicsettlements.smoke","Runic Settlements Isolated Smoke","0.1.0")]
[BepInDependency(Plugin.Id)]
public sealed class Smoke:BaseUnityPlugin
{
    internal static Vector3? ForcedReference;
    private float started,next,stageAt;private int stage;private bool done;
    private SettlementPlan? plan;private PreparedSurface? surface;private Heightmap? map;private float[]? heights;
    private SettlementDecoration? lamp;
    private ZDOID generatedWall,playerWall;
    private void Awake()
    {
        if(Environment.GetEnvironmentVariable("RUNIC_SETTLEMENTS_ISOLATED_SMOKE")!="1") throw new InvalidOperationException("Isolated test only");
        Utils.SetSaveDataPath(Path.Combine(Paths.GameRootPath,"saves"));SaveSystem.SetSessionFlags(SaveSystemSessionFlags.DontSaveAnything);
        new Harmony("com.chazman.runicsettlements.smoke").PatchAll(typeof(Smoke).Assembly);started=Time.realtimeSinceStartup;
    }
    private void Finish(string text){done=true;Logger.LogInfo(text);System.IO.File.WriteAllText(Path.Combine(Paths.GameRootPath,"smoke-result.txt"),text);Application.Quit(text.StartsWith("PASS")?0:1);}
    private void Update()
    {
        if(done) return;if(Time.realtimeSinceStartup-started>600){Finish("FAIL timeout at stage "+stage);return;}
        if(Time.realtimeSinceStartup<next)return;next=Time.realtimeSinceStartup+1;
        try
        {
            var type=typeof(Plugin).Assembly.GetType("RunicSettlements.PreparedRuntime");
            surface=(PreparedSurface?)AccessTools.Field(type,"Active").GetValue(null);if(surface==null)return;
            if(stage==0)
            {
                string file=Path.Combine(Paths.ConfigPath,"RunicSettlements",surface.World.ToString(),"settlements.json");plan=SettlementPlan.Load(file,surface.World,surface.Seed)!;
                if(plan.Sites.Count==0)throw new Exception("No settlement sites");
                Logger.LogInfo("Native plan: "+plan.Report);
                if(System.IO.File.Exists(Path.Combine(Paths.GameRootPath,"startup-only.flag")))
                {
                    Logger.LogInfo($"PASS populated save preparation reached Ready at {Time.realtimeSinceStartup-started:F1}s including engine startup");
                    var ruinType=typeof(Plugin).Assembly.GetType("RunicSettlements.SettlementRuins");
                    var locations=ZoneSystem.instance.m_locationInstances.Values.ToList();
                    var indexed=(Func<ZDO,bool>)AccessTools.Method(ruinType,"ReplacementFilter").Invoke(null,new object[]{locations,true});
                    var records=((Dictionary<ZDOID,ZDO>)AccessTools.Field(typeof(ZDOMan),"m_objectsByID").GetValue(ZDOMan.instance)).Values.ToArray();
                    var clock=System.Diagnostics.Stopwatch.StartNew();int matches=records.Count(z=>indexed(z));clock.Stop();
                    Logger.LogInfo($"Indexed {records.Length} saved objects against {locations.Count} locations in {clock.Elapsed.TotalMilliseconds:F1}ms; {matches} matching generated objects.");
                    foreach(var zdo in records.Where(z=>indexed(z)).Take(64).Concat(records.Where(z=>!indexed(z)).Take(64)))
                        if(indexed(zdo)!=(bool)AccessTools.Method(ruinType,"ReplaceableObject").Invoke(null,new object[]{zdo,locations,true}))throw new Exception("Spatial lookup changed ruin protection");
                    var marker=plan.Decorations.First(d=>d.Id=="wayfinder-spawn-front");
                    ForcedReference=new Vector3(marker.Position.X,marker.Height,marker.Position.Z);ZNet.instance.SetReferencePosition(ForcedReference.Value);
                    stageAt=Time.realtimeSinceStartup;stage=10;return;
                }
                var site=plan.Sites[0];ForcedReference=new Vector3(site.Center.X,site.Height,site.Center.Z);ZNet.instance.SetReferencePosition(ForcedReference.Value);
                stageAt=Time.realtimeSinceStartup;stage=1;return;
            }
            if(stage==1 && Time.realtimeSinceStartup-stageAt>20)
            {
                var site=plan!.Sites[0];map=Heightmap.FindHeightmap(ForcedReference!.Value);if(!map || !ZNetScene.instance.IsAreaReady(map.transform.position))return;
                int checkedVertices=0,paved=0;
                foreach(var loaded in Heightmap.GetAllHeightmaps())
                {
                    if(loaded.IsDistantLod || loaded.m_width!=64 || loaded.m_scale!=1)continue;
                    for(int z=0;z<=64;z++)for(int x=0;x<=64;x++)
                    {var point=new Point(loaded.transform.position.x-32+x,loaded.transform.position.z-32+z);if(Point.Distance(point,site.Center)>58)continue;
                     if(Math.Abs(loaded.GetHeight(x,z)+loaded.transform.position.y-site.Height)>.03f)throw new Exception("Clearing not flat at "+point.X+","+point.Z);
                     checkedVertices++;if(loaded.GetPaintMask(x,z).b>.9f)paved++;}
                }
                if(checkedVertices<8000 || paved<30)throw new Exception("Incomplete native clearing/pavement: "+checkedVertices+" / "+paved);
                // Every original natural vegetation candidate inside the platform is excluded.
                var filter=typeof(Plugin).Assembly.GetType("RunicSettlements.PreparedVegetationFootprints");
                var vegetation=new ZoneSystem.ZoneVegetation {m_prefab=ZNetScene.instance.GetPrefab("Beech1"),m_scaleMin=1,m_scaleMax=1};
                if(!(bool)AccessTools.Method(filter,"Filter").Invoke(null,new object[]{false,ForcedReference.Value,vegetation}))throw new Exception("Natural vegetation not excluded");
                heights=((List<float>)AccessTools.Field(typeof(Heightmap),"m_heights").GetValue(map)).ToArray();map.Poke(0,false);
                Logger.LogInfo($"PASS native platform: {checkedVertices} flat vertices; {paved} paved pixels; vegetation filtered");
                stageAt=Time.realtimeSinceStartup;stage=2;return;
            }
            if(stage==2 && Time.realtimeSinceStartup-stageAt>5)
            {
                if(!heights!.SequenceEqual((List<float>)AccessTools.Field(typeof(Heightmap),"m_heights").GetValue(map)))throw new Exception("Terrain changed on regeneration");
                var compiler=map!.GetAndCreateTerrainCompiler();
                AccessTools.Method(typeof(Plugin).Assembly.GetType("RunicSettlements.PreparedLegacy"),"OriginalProtection").Invoke(null,new object[]{compiler});
                var delta=(float[])AccessTools.Field(typeof(TerrainComp),"m_levelDelta").GetValue(compiler);var flags=(bool[])AccessTools.Field(typeof(TerrainComp),"m_modifiedHeight").GetValue(compiler);
                int index=32*65+32;delta[index]=.25f;flags[index]=true;
                AccessTools.Method(typeof(TerrainComp),"Save",new[]{typeof(bool)}).Invoke(compiler,new object[]{false});map.Poke(0,false);
                if(Math.Abs(map.GetHeight(32,32)-heights![index]-.25f)>.03f)throw new Exception("Player height edit lost");
                var records=((Dictionary<ZDOID,ZDO>)AccessTools.Field(typeof(ZDOMan),"m_objectsByID").GetValue(ZDOMan.instance)).Values;
                foreach(var group in records.Where(z=>z.GetString("RunicSettlements.Decoration","")!="").GroupBy(z=>z.GetString("RunicSettlements.Decoration","")))
                    if(group.Count()!=1)throw new Exception("Duplicated decoration "+group.Key);
                lamp=plan!.Decorations.FirstOrDefault(d=>d.Lamp && !plan.RemovedDecorations.Contains(d.Id));if(lamp==null)throw new Exception("No lamps");
                ForcedReference=new Vector3(lamp.Position.X,lamp.Height,lamp.Position.Z);ZNet.instance.SetReferencePosition(ForcedReference.Value);
                stageAt=Time.realtimeSinceStartup;stage=3;return;
            }
            if(stage==3 && Time.realtimeSinceStartup-stageAt>20)
            {
                var light=FindObjectsByType<Fireplace>(FindObjectsSortMode.None).FirstOrDefault(f=>f.GetComponent<ZNetView>()?.GetZDO()?.GetString("RunicSettlements.Decoration","")==lamp!.Id);
                if(!light)return;if(!light.m_infiniteFuel)throw new Exception("Generated lamp still consumes fuel");
                if(light.GetComponentsInChildren<Light>(true).Any(l=>l.shadows!=LightShadows.None))throw new Exception("Lamp shadows left enabled");
                if(!light.CanBeRemoved())throw new Exception("Generated infinite-fuel lamp cannot be removed");
                var decorations=typeof(Plugin).Assembly.GetType("RunicSettlements.SettlementDecorations");
                var current=(SettlementPlan)AccessTools.Field(decorations,"Current").GetValue(null);
                string planFile=(string)AccessTools.Field(decorations,"File").GetValue(null);
                var lampId=light.GetComponent<ZNetView>().GetZDO().m_uid;
                AccessTools.Method(typeof(ZDOMan),"HandleDestroyedZDO").Invoke(ZDOMan.instance,new object[]{lampId});
                if(!current.RemovedDecorations.Contains(lamp!.Id))throw new Exception("Removed lamp not remembered");
                var terrain=(ITerrain)Activator.CreateInstance(typeof(Plugin).Assembly.GetType("RunicSettlements.GameTerrain"),true);
                foreach(int _ in (IEnumerable<int>)AccessTools.Method(decorations,"Prepare").Invoke(null,new object[]{current,surface!,terrain,planFile,new List<Exclusion>()})){}
                var all=((Dictionary<ZDOID,ZDO>)AccessTools.Field(typeof(ZDOMan),"m_objectsByID").GetValue(ZDOMan.instance)).Values;
                if(all.Any(z=>z.GetString("RunicSettlements.Decoration","")==lamp.Id))throw new Exception("Deleted lamp respawned");
                var ruinType=typeof(Plugin).Assembly.GetType("RunicSettlements.SettlementRuins");
                var selected=ZoneSystem.instance.m_locationInstances.Values.First(l=>(bool)AccessTools.Method(ruinType,"Minor").Invoke(null,new object[]{l.m_location}) && current.Sites.Any(site=>Point.Distance(site.Center,new Point(l.m_position.x,l.m_position.z))<current.Options.Radius+current.Options.Blend+l.m_location.m_exteriorRadius));
                ZDO Wall(long creator)
                {if(!ZNetScene.instance.GetPrefab("woodwall"))throw new Exception("Native woodwall prefab missing");var z=ZDOMan.instance.CreateNewZDO(selected.m_position,"woodwall".GetStableHashCode());z.Persistent=true;z.SetPrefab("woodwall".GetStableHashCode());z.Set(ZDOVars.s_creator,creator);return z;}
                generatedWall=Wall(0).m_uid;playerWall=Wall(12345).m_uid;
                foreach(int _ in (IEnumerable<int>)AccessTools.Method(ruinType,"Clear").Invoke(null,new object[]{current,planFile})){}
                stageAt=Time.realtimeSinceStartup;stage=4;return;

            }
            if(stage==4 && Time.realtimeSinceStartup-stageAt>5)
            {
                if(ZDOMan.instance.GetZDO(generatedWall)!=null || ZDOMan.instance.GetZDO(playerWall)==null)throw new Exception("Generated ruin removal/player-wall preservation failed");
                var marker=plan!.Decorations.First(d=>d.Id=="wayfinder-spawn-front");
                if(Point.Distance(marker.Position,plan.Spawn)>65 || !marker.Text.Contains("ROAD"))throw new Exception("Missing nearby spawn directions");
                if(plan.Decorations.Count(d=>d.Id.StartsWith("wayfinder-"))!=12)throw new Exception("Missing marker pieces");
                ForcedReference=new Vector3(marker.Position.X,marker.Height,marker.Position.Z);ZNet.instance.SetReferencePosition(ForcedReference.Value);
                stageAt=Time.realtimeSinceStartup;stage=5;return;
            }
            if(stage==10 && Time.realtimeSinceStartup-stageAt>15)
            {
                var sign=FindObjectsByType<Sign>(FindObjectsSortMode.None).FirstOrDefault(s=>s.GetComponent<ZNetView>()?.GetZDO()?.GetString("RunicSettlements.Decoration","")=="wayfinder-spawn-front");
                if(!sign || !sign.GetText().Contains("ROAD"))throw new Exception("Spawn marker failed to load from populated save");
                var site=plan!.Sites[0];ForcedReference=new Vector3(site.Center.X,site.Height,site.Center.Z);ZNet.instance.SetReferencePosition(ForcedReference.Value);
                stageAt=Time.realtimeSinceStartup;stage=11;return;
            }
            if(stage==11 && Time.realtimeSinceStartup-stageAt>30)
            {
                var instances=(Dictionary<ZDO,ZNetView>)AccessTools.Field(typeof(ZNetScene),"m_instances").GetValue(ZNetScene.instance);
                var lines=new List<string>{"Loaded native instances at settlement centre: "+ZNetScene.instance.NrOfInstances(),"Simulation distance: "+ZNet.instance.GetSyncedSimulationDistance()};
                lines.AddRange(instances.GroupBy(p=>ZNetScene.instance.GetPrefab(p.Key.GetPrefab())?.name??"unknown").OrderByDescending(g=>g.Count()).Take(30).Select(g=>g.Key+": "+g.Count()));
                foreach(int radius in new[]{60,100,160,224,288})
                    lines.Add($"Within {radius}m: "+instances.Count(p=>Point.Distance(new Point(p.Key.GetPosition().x,p.Key.GetPosition().z),plan!.Sites[0].Center)<=radius));
                System.IO.File.WriteAllLines(Path.Combine(Paths.GameRootPath,"instance-audit.txt"),lines);
                Logger.LogInfo(string.Join("\n",lines));
                Finish($"PASS populated-world startup: {plan!.Sites.Count} existing sites, native spawn sign loaded, indexed protection matches original scan. Instance audit captured at settlement. Elapsed {Time.realtimeSinceStartup-started:F1}s including engine startup and test waits. Headless; client UI not measured.");return;
            }
            if(stage==5 && Time.realtimeSinceStartup-stageAt>20)
            {
                var signs=FindObjectsByType<Sign>(FindObjectsSortMode.None).Where(s=>s.GetComponent<ZNetView>()?.GetZDO()?.GetString("RunicSettlements.Decoration","").StartsWith("wayfinder-spawn-")==true).ToArray();
                if(signs.Length!=2)throw new Exception("Native double-sided spawn sign not instantiated");
                foreach(var sign in signs)
                {
                    if(!sign.GetText().Contains("ROAD") || Math.Abs(sign.transform.localScale.x-2.5f)>.01f)throw new Exception("Sign text/scale incorrect");
                    var wear=sign.GetComponent<WearNTear>();
                    if(wear && (wear.m_noRoofWear || wear.m_noSupportWear))throw new Exception("Marker decay enabled");
                }
                Logger.LogInfo("PASS existing-layout marker migration: 12 unique native pieces, double-sided 2.5x spawn signs, readable text, weather protection. "+signs[0].GetText());
                Finish($"PASS RunicSettlements: {plan!.Sites.Count} sites, {plan.Options.Radius*2}m diameter, {plan.Roads.Count} roads, {plan.Decorations.Count(d=>d.Lamp)} permanent lamps, {plan.Decorations.Count(d=>d.Prefab=="Oak1" || d.Prefab=="PineTree")} trees; native flat terrain, paving, regeneration, natural vegetation exclusion, unique decoration IDs, unlimited fuel, no lamp shadows, player edit preserved. Elapsed {Time.realtimeSinceStartup-started:F1}s. Generated ruin removal, player-wall preservation, decoration removal and idempotency verified. Headless; visual quality and multiplayer not measured.");
            }

        }catch(Exception e){Finish("FAIL "+e);}
    }
}
[HarmonyPatch(typeof(ZNet),nameof(ZNet.GetReferencePosition))]
internal static class Observer {private static void Postfix(ref Vector3 __result){if(Smoke.ForcedReference.HasValue)__result=Smoke.ForcedReference.Value;}}
