using System;
using UnityEngine;

namespace RunicLilyPads
{
    // Only attached to our prefabs. Keeps vanilla damage/refunds/network removal intact.
    public sealed class GardenLifecycle : MonoBehaviour
    {
        internal static void Configure(GameObject root, GameObject visual)
        {
            var wear=root.GetComponent<WearNTear>();
            // Valheim expects all three states when any state is set. These decorations
            // retain the same appearance at every health level.
            wear.m_new=wear.m_worn=wear.m_broken=visual;
            wear.m_wet=null;
            wear.m_snow=wear.m_snowWorn=wear.m_snowBroken=null;
            // wood_floor's fragment roots refer to children removed during construction.
            // Never fragment an entire garden (including its water and particle meshes).
            wear.m_autoCreateFragments=false;
            wear.m_fragmentRoots=Array.Empty<GameObject>();
            wear.m_nonSolidRenderers.Clear();
            // These flags mean damage FROM lack of support/roof, not immunity to it.
            wear.m_noSupportWear=wear.m_noRoofWear=false;
            root.GetComponent<Piece>().m_canBeRemoved=true;
            root.AddComponent<GardenLifecycle>();
        }

        void Start()=>InvokeRepeating(nameof(CleanupPreviouslyRemoved),0,.5f);
        void CleanupPreviouslyRemoved()
        {
            var view=GetComponent<ZNetView>();
            if(!view||!view.IsValid()){CancelInvoke();return;}
            // 0.2.1 removal set health to zero and refunded before its fragment RPC failed.
            // Finish that removal on the owner, without issuing a second resource refund.
            if(view.IsOwner()&&view.GetZDO().GetFloat(ZDOVars.s_health,1f)<=0&&ZNetScene.instance)
                ZNetScene.instance.Destroy(gameObject);
        }
    }
}
