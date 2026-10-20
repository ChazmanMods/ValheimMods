using HarmonyLib;
using UnityEngine;
namespace RunicLilyPads {
 internal static class IvyAttachment {
  static readonly Collider[] Hits=new Collider[16];static bool patched;
  internal static void Install(){if(patched)return;patched=true;var harmony=new Harmony("chazman.WaterGardens.IvyAttachment");harmony.Patch(AccessTools.Method(typeof(PondPlacement),"HasSurfaceContact"),prefix:new HarmonyMethod(typeof(IvyAttachment),nameof(Contact)));harmony.Patch(AccessTools.Method(typeof(Player),"PieceRayTest"),postfix:new HarmonyMethod(typeof(IvyAttachment),nameof(Ray)));}
  // Only the ivy-to-ivy ray bypasses the parent's non-supporting decoration flag.
  // Ward, distance, biome, ceiling and actual attachment contact checks stay native.
  static void Ray(GameObject ___m_placementGhost,ref Piece piece){if(___m_placementGhost&&___m_placementGhost.GetComponent<IvyLights>()&&piece&&piece.GetComponent<IvyLights>())piece=null;}
  static bool Contact(GameObject ghost,ref bool __result){
   if(!ghost||!ghost.GetComponent<IvyLights>())return true;
   int n=Physics.OverlapSphereNonAlloc(ghost.transform.position,.14f,Hits,LayerMask.GetMask("piece_nonsolid"),QueryTriggerInteraction.Ignore);
   for(int i=0;i<n;i++){var hit=Hits[i];if(hit.name!="Ivy chain attachment"||hit.transform.IsChildOf(ghost.transform))continue;if(hit.GetComponentInParent<IvyLights>()){__result=true;return false;}}
   return true;
  }
 }
}
