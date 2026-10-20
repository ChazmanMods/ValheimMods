using UnityEngine;

namespace ArcaneDecorFlowCraft.Plumbing;

// Imported flange lips are irregular. A hollow copper socket seats inside both lips
// and closes their seam without changing the nominal pipe length or hydraulic ports.
internal sealed class PipeSocketJoint:MonoBehaviour
{
    Mesh mesh;static Material copper;Vector3 oldA,oldB,oldDirection;float oldSize;
    internal void Fit(Vector3 a,Vector3 directionA,Vector3 b,Vector3 directionB,float size){
        gameObject.SetActive(true);var axis=(directionA-directionB).normalized;
        if(mesh&&a==oldA&&b==oldB&&axis==oldDirection&&size==oldSize)return;
        oldA=a;oldB=b;oldDirection=axis;oldSize=size;
        if(!mesh){mesh=new Mesh{name="FlowCraft seated copper socket"};gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            if(!copper){copper=new Material(PipeAssets.Load("pipe").Material){name="FlowCraft copper socket",mainTexture=Texture2D.whiteTexture};copper.SetColor("_Color",new Color(.55f,.24f,.09f));}
            gameObject.AddComponent<MeshRenderer>().sharedMaterial=copper;
        }
        const int sides=32;var vertices=new Vector3[sides*4];var normals=new Vector3[vertices.Length];var uv=new Vector2[vertices.Length];var triangles=new int[sides*24];
        var right=Vector3.Cross(axis,Mathf.Abs(axis.y)<.9f?Vector3.up:Vector3.right).normalized;var up=Vector3.Cross(axis,right);
        var start=a-directionA*(.14f*size);var end=b-directionB*(.14f*size);
        for(int ring=0;ring<4;ring++)for(int j=0;j<sides;j++){
            var n=right*Mathf.Cos(j*Mathf.PI*2/sides)+up*Mathf.Sin(j*Mathf.PI*2/sides);int at=ring*sides+j;
            vertices[at]=transform.InverseTransformPoint((ring%2==0?start:end)+n*((ring<2?.255f:.19f)*size));normals[at]=transform.InverseTransformDirection(ring<2?n:-n);uv[at]=new Vector2(j/(float)sides,ring%2);
        }
        int cursor=0;
        void Quad(int a0,int a1,int b0,int b1){triangles[cursor++]=a0;triangles[cursor++]=a1;triangles[cursor++]=b0;triangles[cursor++]=a1;triangles[cursor++]=b1;triangles[cursor++]=b0;}
        for(int j=0;j<sides;j++){int k=(j+1)%sides;
            Quad(j,k,sides+j,sides+k);Quad(2*sides+k,2*sides+j,3*sides+k,3*sides+j);
            Quad(k,j,2*sides+k,2*sides+j);Quad(sides+j,sides+k,3*sides+j,3*sides+k);
        }
        mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateBounds();
    }
    void OnDestroy(){if(mesh)Destroy(mesh);}
}
