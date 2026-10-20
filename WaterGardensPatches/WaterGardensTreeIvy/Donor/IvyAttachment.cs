using HarmonyLib;
using UnityEngine;
namespace RunicLilyPads {
 internal static class IvyAttachment {
  static readonly Collider[] Hits=new Collider[16];static bool patched;static GameObject preview;static IvyLights parent;static Vector3 snapped;
  internal static void Install(){if(patched)return;patched=true;var h=new Harmony("chazman.WaterGardens.IvyAttachment");h.Patch(AccessTools.Method(typeof(PondPlacement),"HasSurfaceContact"),prefix:new HarmonyMethod(typeof(IvyAttachment),nameof(Contact)));h.Patch(AccessTools.Method(typeof(Player),"PieceRayTest"),postfix:new HarmonyMethod(typeof(IvyAttachment),nameof(Ray)));h.Patch(AccessTools.Method(typeof(Player),"UpdatePlacementGhost"),postfix:new HarmonyMethod(typeof(IvyAttachment),nameof(Align)));h.Patch(AccessTools.Method(typeof(WearNTear),"OnPlaced"),postfix:new HarmonyMethod(typeof(IvyAttachment),nameof(Placed)));}
  static bool Hanging(GameObject go)=>go&&go.name.StartsWith("piece_arcanedecor_ivy_gold_hanging");
  static void Ray(GameObject ___m_placementGhost,Player __instance,float ___m_maxPlaceDistance,ref bool __result,ref Vector3 point,ref Vector3 normal,ref Piece piece){
   preview=___m_placementGhost;parent=null;
   var ivy=preview?preview.GetComponent<IvyLights>():null;if(!ivy)return;
   // Native tree geometry is a valid ivy anchor, without granting structural support.
   if(__result&&piece&&piece.GetComponent<GardenTree>()&&piece.name.StartsWith("piece_arcanedecor_yggdrasil_")){
    piece=null;ivy.SetJoined(false);ivy.PreviewChainSize(1);return;
   }
   if(!__result||!Hanging(preview)||!piece||!Hanging(piece.gameObject)||piece.gameObject==preview){ivy.SetJoined(false);ivy.PreviewChainSize(1);return;}
   var target=piece.GetComponent<IvyLights>();if(!target){ivy.SetJoined(false);ivy.PreviewChainSize(1);return;}var visual=target.transform.Find("garden_visual");var local=visual.InverseTransformPoint(point);
   if(local.y>-.95f||local.y<-2.08f){ivy.SetJoined(false);ivy.PreviewChainSize(1);return;}
   var end=visual.TransformPoint(new Vector3(0,-1.35f,0));
   // Preserve native building reach at the snapped endpoint.
   if(Vector3.Distance(__instance.GetEyePoint(),end)>___m_maxPlaceDistance+preview.GetComponent<Piece>().m_extraPlacementDistance){ivy.SetJoined(false);ivy.PreviewChainSize(1);return;}
   parent=target;snapped=end;point=end;normal=Vector3.down;piece=null;ivy.SetJoined(true);ivy.PreviewChainSize(visual.localScale.x);
  }
  static void Align(GameObject ___m_placementGhost){if(parent&&preview==___m_placementGhost&&preview)preview.transform.rotation=parent.transform.rotation;}
  static bool Contact(GameObject ghost,ref bool __result){
   if(!ghost||!ghost.GetComponent<IvyLights>())return true;
   int n=Physics.OverlapSphereNonAlloc(ghost.transform.position,.14f,Hits,LayerMask.GetMask("piece_nonsolid","piece"),QueryTriggerInteraction.Ignore);
   for(int i=0;i<n;i++){var hit=Hits[i];if(hit.transform.IsChildOf(ghost.transform))continue;
    var tree=hit.GetComponentInParent<GardenTree>();if(tree&&tree.name.StartsWith("piece_arcanedecor_yggdrasil_")){__result=true;return false;}
    if(!Hanging(ghost))continue;
    if(hit.name!="Ivy chain attachment"||hit.transform.IsChildOf(ghost.transform))continue;if(Hanging(hit.transform.root.gameObject)||hit.GetComponentInParent<IvyLights>()){__result=true;return false;}}
   return true;
  }
  static void Placed(WearNTear __instance){
   if(!parent||!preview||!Hanging(__instance.gameObject)||Vector3.Distance(__instance.transform.position,snapped)>.08f)return;
   var ivy=__instance.GetComponent<IvyLights>();var view=__instance.GetComponent<ZNetView>();if(!ivy||!view||!view.IsValid()||!view.IsOwner())return;
   view.GetZDO().Set("wg_tree_scale",parent.transform.Find("garden_visual").localScale.x);view.GetZDO().Set("wg_ivy_join",true);ivy.SetJoined(true);
  }
 }
}

