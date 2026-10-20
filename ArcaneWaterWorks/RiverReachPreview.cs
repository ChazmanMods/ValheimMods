using UnityEngine;

namespace ArcaneWaterWorks;
// Visual only: never move the placement ghost or change its terrain operation.
internal sealed class RiverReachPreview:MonoBehaviour {
    LineRenderer ring;
    Material material;
    Vector3 center;
    float radius=-1,next;
    internal void Show(Vector3 origin,float reach){
        if(!ring){
            var child=new GameObject("river_trench_reach");child.transform.SetParent(transform,false);
            ring=child.AddComponent<LineRenderer>();
            material=new Material(Shader.Find("Sprites/Default"));ring.sharedMaterial=material;
            ring.useWorldSpace=true;ring.loop=true;ring.positionCount=128;
            ring.startWidth=ring.endWidth=.07f;
            ring.startColor=ring.endColor=new Color(1,.8f,.25f,.75f);
            ring.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;ring.receiveShadows=false;
        }
        if(center==origin&&radius==reach&&Time.time<next)return;
        center=origin;radius=reach;next=Time.time+1;
        for(int i=0;i<ring.positionCount;i++){
            float angle=i*Mathf.PI*2/ring.positionCount;
            var p=origin+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*reach;
            if(RiverTerrain.SampleCurrent(p,out var ground))p.y=ground;
            p.y+=.08f;ring.SetPosition(i,p);
        }
    }
    void OnDestroy(){if(material)Destroy(material);}
}
