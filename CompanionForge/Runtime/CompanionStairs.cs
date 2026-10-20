using System;
using UnityEngine;
using HarmonyLib;

namespace CompanionForge
{
    // A narrow local guide for built stairs. Native Character physics and AutoJumpLedge
    // still move the body; no teleporting, global navigation changes, or AI replacement.
    public sealed class CompanionStairs : MonoBehaviour
    {
        private Character character;
        private MonsterAI ai;
        private ZNetView view;
        private CompanionBed rest;
        private CompanionPose pose;
        private int solidMask;
        private float nextProbe;
        private bool guiding;
        private Vector3 direction;
        private GameObject followed;

        private void Awake()
        {
            character=GetComponent<Character>(); ai=GetComponent<MonsterAI>(); view=GetComponent<ZNetView>();
            solidMask=LayerMask.GetMask("Default","piece","terrain","static_solid","Default_small");
        }
        private void Start() { rest=GetComponent<CompanionBed>(); pose=GetComponent<CompanionPose>(); }
        internal void Guide(GameObject target)
        {
            if (target==null || ai==null || view==null || !view.IsValid() || !view.IsOwner() || !ai.enabled ||
                character.IsDead() || character.InAttack() || character.IsStaggering() || ai.IsAlerted() ||
                (rest!=null && rest.Resting) || (pose!=null && pose.Performing))
            { guiding=false; followed=null; return; }
            if (target!=followed) { followed=target; nextProbe=0; }
            if (Time.time>=nextProbe)
            {
                nextProbe=Time.time+.12f;
                guiding=TryDirection(transform.position,target.transform.position,solidMask,out direction);
            }
            if (!guiding) return;
            character.SetLookDir(direction); character.SetMoveDir(direction); character.SetRun(false);
        }
        internal static bool TryDirection(Vector3 position,Vector3 target,int mask,out Vector3 direction)
        {
            direction=Vector3.zero;
            Vector3 delta=target-position;
            if (Mathf.Abs(delta.y)<.35f || delta.sqrMagnitude>144f) return false;
            var horizontal=new Vector3(delta.x,0,delta.z).normalized;
            Piece stair=null;
            // Two short support probes, only for a following companion on different levels.
            for (int i=0;i<2 && stair==null;i++)
            {
                var origin=position+horizontal*(i*.65f)+Vector3.up*.85f;
                if (!Physics.Raycast(origin,Vector3.down,out var hit,1.55f,mask,QueryTriggerInteraction.Ignore)) continue;
                var piece=hit.collider.GetComponentInParent<Piece>();
                if (piece==null) continue;
                string name=Utils.GetPrefabName(piece.gameObject);
                if (name=="wood_stair" || name=="wood_stepladder" || name=="ashwood_stair") stair=piece;
            }
            if (stair==null) return false;
            var local=stair.transform.InverseTransformPoint(position);
            // These authored pieces rise along local -Z. Center on the tread instead
            // of cutting diagonally across its rail toward an off-axis player.
            var next=stair.transform.TransformPoint(new Vector3(0,local.y,local.z+(delta.y>0 ? -.7f : .7f)));
            direction=next-position; direction.y=0;
            if (direction.sqrMagnitude<.001f) return false;
            direction.Normalize();
            // Native collision remains authoritative; this probe avoids driving into a
            // low ceiling or wall while overriding the native three-metre follow stop.
            if (Physics.SphereCast(position+Vector3.up*1.5f,.2f,direction,out _,.65f,mask,QueryTriggerInteraction.Ignore))
            { direction=Vector3.zero; return false; }
            return true;
        }
    }
    // Run immediately after native follow decides its direction, before Character
    // consumes it. A render-frame LateUpdate would lose to the next AI update.
    [HarmonyPatch(typeof(BaseAI),"Follow")]
    static class CompanionStairFollowPatch
    {
        static void Postfix(BaseAI __instance,GameObject go)
        {
            if (__instance.TryGetComponent<CompanionStairs>(out var stairs)) stairs.Guide(go);
        }
    }
}
