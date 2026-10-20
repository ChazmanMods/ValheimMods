using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Jotunn.Managers;
public partial class IvySmoke {
 void TestHammer(string id,Vector3 origin,bool chain=false){
  for(int i=0;i<40&&Location.IsInsideNoBuildLocation(origin);i++)origin+=Vector3.right*15;
  var player=Player.m_localPlayer;var camera=GameCamera.instance.transform;var oldPlayer=player.transform.position;var oldCamera=camera.position;var oldRotation=camera.rotation;
  var previous=typeof(Player).GetField("m_buildPieces",Hidden).GetValue(player);
  var support=chain?Instantiate(PrefabManager.Instance.GetPrefab(id),origin,Quaternion.identity):GameObject.CreatePrimitive(PrimitiveType.Cube);support.layer=LayerMask.NameToLayer("piece");support.transform.position=origin;support.transform.localScale=Vector3.one;
  var aim=chain?origin+Vector3.down*1.5f:origin+(id.EndsWith("hanging")?Vector3.down:Vector3.back)*.5f;
  var prefab=PrefabManager.Instance.GetPrefab(id);var piece=prefab.GetComponent<Piece>();
  try{
   player.AddKnownItem(PrefabManager.Instance.GetPrefab("Stone").GetComponent<ItemDrop>().m_itemData);
   typeof(Player).GetMethod("UpdateKnownRecipesList",Hidden).Invoke(player,null);
   var table=PrefabManager.Instance.GetPrefab("Hammer").GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces;
   typeof(Player).GetMethod("SetPlaceMode",Hidden).Invoke(player,new object[]{table});player.SetNoPlacementCost(true);player.SetBuildCategory(piece.m_category);
   Check(player.SetSelectedPiece(piece),"Hammer selects "+id);
   var ghost=(GameObject)typeof(Player).GetField("m_placementGhost",Hidden).GetValue(player);Check(ghost,"Hammer creates preview "+id);if(!ghost)return;
   camera.position=aim+(id.EndsWith("hanging")?new Vector3(0,-2,-1):new Vector3(0,0,-2));camera.LookAt(aim);
   player.transform.position=aim+new Vector3(0,-1,-2);player.GetComponent<Rigidbody>().position=player.transform.position;
   Physics.SyncTransforms();typeof(Player).GetMethod("UpdatePlacementGhost",Hidden).Invoke(player,new object[]{false});
   var status=(Player.PlacementStatus)typeof(Player).GetField("m_placementStatus",Hidden).GetValue(player);
   Check(ghost.activeInHierarchy&&Vector3.Distance(ghost.transform.position,chain?origin+Vector3.down*1.35f:aim)<.1f,"Hammer top anchor at attachment "+id+" distance="+Vector3.Distance(ghost.transform.position,chain?origin+Vector3.down*1.35f:aim));
   Check(status==Player.PlacementStatus.Valid,"Hammer valid support "+(chain?"IVY CHAIN ":"")+id+" status="+status);
   if(status==Player.PlacementStatus.Valid){
    var before=UnityEngine.Object.FindObjectsOfType<RunicLilyPads.IvyLights>();
    player.PlacePiece(piece,ghost.transform.position,ghost.transform.rotation,false);
    var placed=UnityEngine.Object.FindObjectsOfType<RunicLilyPads.IvyLights>().FirstOrDefault(t=>t.gameObject!=ghost&&t.name.StartsWith(id)&&!before.Contains(t));
    Check(placed&&Vector3.Distance(placed.transform.position,chain?origin+Vector3.down*1.35f:aim)<.1f,"native Hammer placement preserves attachment "+id);if(placed)ZNetScene.instance.Destroy(placed.gameObject);
   }
  }finally{
   typeof(Player).GetMethod("SetPlaceMode",Hidden).Invoke(player,new[]{previous});camera.position=oldCamera;camera.rotation=oldRotation;player.transform.position=oldPlayer;player.GetComponent<Rigidbody>().position=oldPlayer;DestroyImmediate(support);
  }
 }
}
