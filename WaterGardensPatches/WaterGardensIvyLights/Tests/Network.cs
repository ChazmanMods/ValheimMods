using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using RunicLilyPads;
using Jotunn.Managers;
public partial class IvySmoke {
 IEnumerator NetworkServer(){
  float until=Time.realtimeSinceStartup+260;
  while(Time.realtimeSinceStartup<until){
   if(ZNet.instance&&ZDOMan.instance!=null&&ZNet.instance.GetPeers().Any(p=>p.IsReady())){
    var list=new List<ZDO>();int index=0;while(!ZDOMan.instance.GetAllZDOsWithPrefabIterative("piece_arcanedecor_ivy_gold_hanging",list,ref index)){}
    if(list.Any(z=>Math.Abs(z.GetFloat("wg_ivy_group2",0)-2.75f)<.001f&&z.GetFloat("wg_ivy_r",0)==1&&z.GetFloat("wg_ivy_b",0)==1&&z.GetFloat("wg_ivy_g",1)==0)){
     Check(true,"separate dedicated server received joined-client ivy resize");File.WriteAllText(root+"/network-observed.txt","PASS");yield break;
    }
   }
   yield return new WaitForSecondsRealtime(1);
  }
  Check(false,"joined-client resize did not reach server within test window");
 }
 IEnumerator NetworkClient(){
  var profile=new PlayerProfile("ivy_network",FileHelpers.FileSource.Local);profile.SetName("IvyTest");profile.m_firstSpawn=false;profile.Save();Game.SetProfile("ivy_network",FileHelpers.FileSource.Local);
  var fejd=UnityEngine.Object.FindObjectOfType<FejdStartup>();
  HarmonyLib.AccessTools.Property(typeof(FejdStartup),"ServerPassword").SetValue(null,"ivytest123");
  HarmonyLib.AccessTools.Field(typeof(FejdStartup),"m_joinServer").SetValue(fejd,new ServerJoinData(new ServerJoinDataDedicated("127.0.0.1:2476")));
  HarmonyLib.AccessTools.Field(typeof(FejdStartup),"m_instantStart").SetValue(fejd,true);fejd.JoinServer();
  float until=Time.realtimeSinceStartup+150;
  while(!Player.m_localPlayer&&Time.realtimeSinceStartup<until)yield return null;
  Check(Player.m_localPlayer&&ZNet.GetConnectionStatus()==ZNet.ConnectionStatus.Connected&&!ZNet.instance.IsServer(),"genuinely joined separate dedicated server");
  if(!Player.m_localPlayer){Application.Quit(1);yield break;}
  var p=Player.m_localPlayer;p.SetGodMode(true);foreach(var bird in UnityEngine.Object.FindObjectsOfType<Valkyrie>())bird.DropPlayer(true);p.SetIntro(false);
  var coords=File.ReadAllText(root+"/persisted-position.txt").Split(',');var pos=new Vector3(float.Parse(coords[0]),float.Parse(coords[1]),float.Parse(coords[2]));
  p.transform.position=pos;p.GetComponent<Rigidbody>().position=pos;
  TestHammer("piece_arcanedecor_ivy_gold_hanging",pos+Vector3.up*6+Vector3.forward*10,true);yield return null;
  var go=Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanedecor_ivy_gold_hanging"),pos+Vector3.forward*2,Quaternion.identity);yield return null;
  var tree=go.GetComponent<IvyLights>();float start=Time.realtimeSinceStartup;bool accepted=tree.Interact(p,false,false);Call(tree,"SetHex","#FF00FF");Call(tree,"SetBulbs",2.25f);Call(tree,"NextGroup");Call(tree,"SetBulbs",1.5f);Call(tree,"NextGroup");Call(tree,"SetBulbs",2.75f);Call(tree,"Save");
  yield return null;Check(accepted&&go.GetComponent<ZNetView>().GetZDO().GetFloat("wg_ivy_group2",0)==2.75f,"joined client resize applies on next frame ("+(Time.realtimeSinceStartup-start)+" s)");
  yield return new WaitForSecondsRealtime(8);
  Logger.LogInfo(errors==0?"IVY NETWORK CLIENT PASS":"IVY NETWORK CLIENT FAIL");Application.Quit(errors==0?0:1);
 }
}
