using UnityEngine;

namespace ArcaneDecorFlowCraft.Plumbing;

// A depth-tested, unlit halo makes the running indicator readable without relying
// on the player's bloom setting or the direction of sunlight. No flare through walls.
internal sealed class PipePowerGlow:MonoBehaviour
{
    static Mesh disc;static Material material;
    MeshRenderer visual;readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
    internal void SetColor(Color color){
        if(!visual){
            if(!material){
                var shader=Shader.Find("Sprites/Default");
                if(!shader)throw new System.InvalidOperationException("FlowCraft indicator requires Sprites/Default shader");
                material=new Material(shader){name="FlowCraft unlit power halo",mainTexture=Texture2D.whiteTexture};
            }
            if(!disc){
                const int sides=48;var radii=new[]{0f,.085f,.13f,.19f,.26f};var alpha=new[]{.4f,.4f,.28f,.09f,0f};
                var v=new Vector3[sides*radii.Length];var c=new Color[v.Length];var triangles=new int[(radii.Length-1)*sides*6];
                for(int ring=0;ring<radii.Length;ring++)for(int j=0;j<sides;j++){
                    int at=ring*sides+j;float a=j*Mathf.PI*2/sides;v[at]=new Vector3(Mathf.Cos(a),Mathf.Sin(a),0)*radii[ring];c[at]=new Color(1,1,1,alpha[ring]);
                    if(ring==0)continue;int k=((ring-1)*sides+j)*6,p=(ring-1)*sides+j,q=(ring-1)*sides+(j+1)%sides;
                    triangles[k]=p;triangles[k+1]=q;triangles[k+2]=at;triangles[k+3]=q;triangles[k+4]=ring*sides+(j+1)%sides;triangles[k+5]=at;
                }
                disc=new Mesh{name="FlowCraft radial glow falloff",vertices=v,colors=c,triangles=triangles};disc.RecalculateBounds();
            }
            gameObject.AddComponent<MeshFilter>().sharedMesh=disc;visual=gameObject.AddComponent<MeshRenderer>();visual.sharedMaterial=material;
            visual.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;visual.receiveShadows=false;
        }
        properties.SetColor("_Color",new Color(color.r,color.g,color.b,1));visual.SetPropertyBlock(properties);
    }
    void OnWillRenderObject(){var camera=Camera.current;if(camera)transform.rotation=camera.transform.rotation;}
}
