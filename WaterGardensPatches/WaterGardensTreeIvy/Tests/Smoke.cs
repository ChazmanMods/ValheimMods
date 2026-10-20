using System;using System.Collections;using System.Linq;using System.Reflection;using BepInEx;using UnityEngine;using Jotunn.Managers;using RunicLilyPads;using HarmonyLib;
[BepInPlugin("chazman.IvySmoke","Yggdrasil ivy test","1.0.0")][BepInDependency("chazman.RunicLilyPads")]
public partial class IvySmoke:BaseUnityPlugin{
 bool ready;int failures;bool baseline;const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;const BindingFlags H=BindingFlags.Instance|BindingFlags.NonPublic;
 void Check(bool ok,string s){Logger.LogInfo((ok?"CHECK PASS ":"CHECK FAIL ")+s);if(!ok)failures++;}
 void Awake(){Utils.SetSaveDataPath("E:/Valheim Mods/WaterGardensIvyChain/runtime/saves");PrefabManager.OnVanillaPrefabsAvailable+=()=>ready=true;baseline=Environment.GetEnvironmentVariable("TREE_BASELINE")=="1";StartCoroutine(Run());}
 IEnumerator Run(){while(!ready)yield return null;var w=World.GetCreateWorld("IvyValidation",FileHelpers.FileSource.Local);Game.SetProfile("ivy_validation",FileHelpers.FileSource.Local);ZNet.SetServer(true,false,false,"IvyValidation","",w);ZNet.ResetServerHost();AccessTools.Method(typeof(FejdStartup),"LoadMainScene").Invoke(FindObjectOfType<FejdStartup>(),null);while(!Player.m_localPlayer)yield return null;
 var p=Player.m_localPlayer;p.SetGodMode(true);foreach(var v in FindObjectsOfType<Valkyrie>())v.DropPlayer(true);p.SetIntro(false);p.GetComponent<Rigidbody>().isKinematic=true;
 var origin=p.transform.position+Vector3.up*12;for(int n=0;n<40&&Location.IsInsideNoBuildLocation(origin);n++)origin+=Vector3.right*15;
 p.transform.position=origin;p.GetComponent<Rigidbody>().position=origin;yield return new WaitForSecondsRealtime(3);
 GameCamera.instance.enabled=false;p.AddKnownItem(PrefabManager.Instance.GetPrefab("Stone").GetComponent<ItemDrop>().m_itemData);p.SetNoPlacementCost(true);AccessTools.Method(typeof(Player),"UpdateKnownRecipesList").Invoke(p,null);
 var table=PrefabManager.Instance.GetPrefab("Hammer").GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces;AccessTools.Method(typeof(Player),"SetPlaceMode").Invoke(p,new object[]{table});var ivy=PrefabManager.Instance.GetPrefab("piece_arcanedecor_ivy_gold_hanging").GetComponent<Piece>();p.SetBuildCategory(ivy.m_category);p.SetSelectedPiece(ivy);
 var camera=GameCamera.instance.transform;
 for(int variant=1;variant<=3;variant++){
 var tree=Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanedecor_yggdrasil_00"+variant),origin+Vector3.right*12,Quaternion.identity);yield return null;tree.GetComponent<GardenTree>().enabled=false;
 foreach(float scale in new[]{.25f,1f,2f}){
 tree.transform.Find("garden_visual").localScale=Vector3.one*scale;Physics.SyncTransforms();var colliders=tree.GetComponentsInChildren<Collider>().Where(c=>c.enabled&&!c.isTrigger).ToArray();Logger.LogInfo("TREE "+variant+" scale="+scale+" colliders="+colliders.Length);
 bool hit=false;RaycastHit selected=default;Vector3 cam=default;
 foreach(var c in colliders.OrderByDescending(c=>(c.bounds.center-tree.transform.position).sqrMagnitude)){
 foreach(var dir in new[]{Vector3.up,Vector3.forward,Vector3.right,Vector3.back,Vector3.left,Vector3.down}){
 for(int ox=-1;ox<=1&&!hit;ox++)for(int oz=-1;oz<=1&&!hit;oz++){
 var center=c.bounds.center+new Vector3(ox*c.bounds.extents.x*.55f,0,oz*c.bounds.extents.z*.55f);cam=center-dir*(c.bounds.extents.magnitude+.5f);
 if(c.Raycast(new Ray(cam,dir),out selected,100)){hit=true;cam=selected.point+selected.normal*.6f;}
 }if(hit)break;}if(hit)break;}
 Check(hit,"tree collider target exists "+variant+" @"+scale);if(!hit)continue;
 camera.position=cam;camera.LookAt(selected.point);p.transform.position=selected.point+Vector3.back*3-Vector3.up*1.4f;p.GetComponent<Rigidbody>().position=p.transform.position;Physics.SyncTransforms();AccessTools.Method(typeof(Player),"UpdatePlacementGhost").Invoke(p,new object[]{false});var ghost=(GameObject)AccessTools.Field(typeof(Player),"m_placementGhost").GetValue(p);var status=(Player.PlacementStatus)AccessTools.Field(typeof(Player),"m_placementStatus").GetValue(p);
 Check(baseline?status!=Player.PlacementStatus.Valid:status==Player.PlacementStatus.Valid,(baseline?"baseline rejects":"ivy accepted on")+" tree "+variant+" @"+scale+" status="+status);
 Check(!tree.GetComponent<WearNTear>().m_supports,"tree remains nonstructural, with no added canopy collider");
 if(!baseline&&status==Player.PlacementStatus.Valid){var old=FindObjectsOfType<IvyLights>();p.PlacePiece(ivy,ghost.transform.position,ghost.transform.rotation,false);var placed=FindObjectsOfType<IvyLights>().FirstOrDefault(v=>v.gameObject!=ghost&&!old.Contains(v));Check(placed,"native hammer places ivy on tree "+variant+" @"+scale);if(placed)ZNetScene.instance.Destroy(placed.gameObject);}
 }
 ZNetScene.instance.Destroy(tree);yield return null;
 }
 if(!baseline){TestHammer("piece_arcanedecor_ivy_gold_hanging",origin+Vector3.left*12);TestHammer("piece_arcanedecor_ivy_gold_hanging",origin+Vector3.left*18,true);TestHammer("piece_arcanedecor_ivy_gold_clinging",origin+Vector3.left*24);}
 AccessTools.Method(typeof(Player),"SetPlaceMode").Invoke(p,new object[]{null});Logger.LogInfo("TREE COMPLETE failures="+failures);Application.Quit(failures==0?0:1);
 }
}
