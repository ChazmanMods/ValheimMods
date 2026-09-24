using HarmonyLib;
using UnityEngine;
namespace RunicLilyPads
{
    [HarmonyPatch(typeof(Player),"UpdatePlacementGhost")]
    static class PondPlacement
    {
        static void Postfix(GameObject ___m_placementGhost,ref Player.PlacementStatus ___m_placementStatus)
        {
            if(!___m_placementGhost||!___m_placementGhost.activeSelf)return;
            if(!___m_placementGhost.GetComponent<GardenLifecycle>()||___m_placementGhost.GetComponent<LilyFloat>())return;
            var piece=___m_placementGhost.GetComponent<Piece>();
            var fixture=___m_placementGhost.GetComponent<GardenFloat>();
            if(fixture){fixture.PlaceGhost();piece.SetInvalidPlacementHeightlight(___m_placementStatus!=Player.PlacementStatus.Valid);return;}
            if(!HasSurfaceContact(___m_placementGhost,piece.m_groundOnly))___m_placementStatus=Player.PlacementStatus.Invalid;
            var pond=___m_placementGhost.GetComponent<GardenPiece>();
            if(pond&&pond.Shape>=0){
                var p=pond.transform.position;var width=pond.Settings.Width;
                if(!PrivateArea.CheckAccess(p,width*.7f,false))___m_placementStatus=Player.PlacementStatus.PrivateZone;

            }
            piece.SetInvalidPlacementHeightlight(___m_placementStatus!=Player.PlacementStatus.Valid);
        }
        internal static bool HasSurfaceContact(GameObject ghost,bool terrainOnly)
        {
            // Test the planted base, not the foliage's broad selection box. This also
            // rejects elevated placement offsets introduced by other building tools.
            if(terrainOnly)
                return Physics.Raycast(ghost.transform.position+Vector3.up*.08f,Vector3.down,.16f,LayerMask.GetMask("terrain"),QueryTriggerInteraction.Ignore);
            foreach(var hit in Physics.OverlapSphere(ghost.transform.position,.08f,LayerMask.GetMask("terrain","piece","Default","static_solid","Default_small"),QueryTriggerInteraction.Ignore)){
                if(hit.transform.IsChildOf(ghost.transform)||hit.attachedRigidbody)continue;
                var support=hit.GetComponentInParent<WearNTear>();
                if(!support||support.m_supports)return true;
            }
            return false;
        }
    }
    // Pond water is a surface, not the non-supporting shoreline stone. Keep all the
    // normal ray distance, ward, biome and obstruction checks in Player unchanged.
    [HarmonyPatch(typeof(Player),"PieceRayTest")]
    static class PondWaterRay
    {
        static void Prefix(GameObject ___m_placementGhost,ref bool water){if(___m_placementGhost&&___m_placementGhost.GetComponent<GardenFloat>())water=true;}
        static void Postfix(GameObject ___m_placementGhost,bool __result,Collider waterSurface,ref Piece piece)
        {
            if(!__result||!waterSurface||!___m_placementGhost||(!___m_placementGhost.GetComponent<LilyFloat>()&&!___m_placementGhost.GetComponent<GardenFloat>()))return;
            var pond=waterSurface.GetComponentInParent<GardenPiece>();
            if(pond&&pond.Shape>=0)piece=null;
        }
    }
}
