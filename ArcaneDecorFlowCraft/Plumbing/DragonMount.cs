using System;
using UnityEngine;

namespace ArcaneDecorFlowCraft.Plumbing;

// A wall connection belongs to one actual supporting piece, never an arbitrary nearby pipe.
internal static class DragonMount
{
    internal struct Mount {internal Piece Wall;internal Vector3 Back,Normal;}
    internal static bool Allowed(Piece piece){
        if(!piece||piece.GetComponent<PipePiece>())return false;
        var wear=piece.GetComponent<WearNTear>();if(!wear)return false;
        return wear.m_materialType==WearNTear.MaterialType.Stone||wear.m_materialType==WearNTear.MaterialType.Marble||wear.m_materialType==WearNTear.MaterialType.Iron||wear.m_materialType==WearNTear.MaterialType.Ashstone;
    }
    internal static Vector3 CenterOnFace(Vector3 hitPoint,Vector3 normal,float modelCenterY){
        Piece wall=null;Collider hitCollider=null;float nearest=float.PositiveInfinity;
        foreach(var hit in Physics.RaycastAll(hitPoint+normal*.12f,-normal,.3f,LayerMask.GetMask("piece"),QueryTriggerInteraction.Ignore)){
            var candidate=hit.collider.GetComponentInParent<Piece>();if(!Allowed(candidate)||hit.distance>=nearest)continue;
            wall=candidate;hitCollider=hit.collider;nearest=hit.distance;
        }
        if(!wall)return hitPoint;
        // Aggregate the actual piece's colliders in its own coordinates, not world AABB coordinates.
        var bounds=new Bounds();bool first=true;
        foreach(var collider in wall.GetComponentsInChildren<Collider>()){
            if(!collider.enabled||collider.isTrigger||!collider.gameObject.activeInHierarchy)continue;
            if(collider is BoxCollider box){
                for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2){
                    var point=wall.transform.InverseTransformPoint(box.transform.TransformPoint(box.center+Vector3.Scale(box.size*.5f,new Vector3(x,y,z))));
                    if(first){bounds=new Bounds(point,Vector3.zero);first=false;}else bounds.Encapsulate(point);
                }
            }else{var point=wall.transform.InverseTransformPoint(collider.bounds.center);if(first){bounds=new Bounds(point,Vector3.zero);first=false;}else bounds.Encapsulate(point);}
        }
        var centre=wall.transform.TransformPoint(bounds.center);
        var face=centre+normal*Vector3.Dot(hitPoint-centre,normal);
        // Offset the root so the visible head, rather than its mouth origin, is centered on the block.
        var rotation=Quaternion.LookRotation(normal,Vector3.up);var root=face-rotation*Vector3.up*modelCenterY;
        // Use the collider's true face at the new mouth height (important on thick/rotated blocks).
        if(hitCollider.Raycast(new Ray(root+normal*4,-normal),out var surface,8))root=surface.point;
        return root;
    }
    internal static bool Find(PipePiece dragon,out Mount mount){
        mount=default;if(!PipePrefabs.Dragon(dragon.Kind))return false;
        Vector3 n=dragon.transform.forward,p=dragon.transform.position;
        if(Mathf.Abs(Vector3.Dot(n,Vector3.up))>.45f)return false;
        float nearest=float.PositiveInfinity;Piece wall=null;
        foreach(var hit in Physics.RaycastAll(p+n*.12f,-n,.3f,LayerMask.GetMask("piece"),QueryTriggerInteraction.Ignore)){
            var piece=hit.collider.GetComponentInParent<Piece>();
            if(!Allowed(piece)||hit.distance>=nearest||Vector3.Dot(hit.normal,n)<.9f)continue;
            nearest=hit.distance;wall=piece;
        }
        if(!wall)return false;
        // Cast from behind the piece to its rear face. Collider geometry handles rotations and thickness.
        var ray=new Ray(p-n*8,n);float distance=float.PositiveInfinity;Vector3 back=default;
        foreach(var collider in wall.GetComponentsInChildren<Collider>()){
            if(!collider.enabled||collider.isTrigger||!collider.gameObject.activeInHierarchy)continue;
            if(collider.Raycast(ray,out var hit,8.1f)&&hit.distance<distance){distance=hit.distance;back=hit.point;}
        }
        if(float.IsPositiveInfinity(distance)||Vector3.Dot(p-back,n)<.02f)return false;
        mount=new Mount{Wall=wall,Back=back,Normal=n};return true;
    }
    internal static bool Target(PipePiece pipe,int port,PipePiece dragon,Mount mount,out Vector3 target,out bool elbow){
        target=default;elbow=false;
        if(PipePrefabs.Dragon(pipe.Kind)||PipePrefabs.BaseKind(pipe.Kind)==PipeKind.Intake||PipePrefabs.Narrow(pipe.Kind)!=PipePrefabs.Narrow(dragon.Kind))return false;
        var direction=pipe.Direction(port);float dot=Vector3.Dot(direction,mount.Normal);
        if(dot>.94f){target=mount.Back+mount.Normal*.015f;return true;}
        // A tangential run turns into the wall, keeping the pipe body clear of its back surface.
        if(Mathf.Abs(dot)>.12f)return false;
        float radius=.4f*PipePrefabs.DiameterScale(pipe.Kind);
        target=mount.Back-(mount.Normal+direction)*radius;elbow=true;return true;
    }
    internal static bool MatchesFeed(PipePiece pipe,int port,Mount mount,Vector3 target,bool elbow,out float distance){
        var offset=pipe.Port(port)-target;distance=offset.sqrMagnitude;
        if(distance<=PipeNetwork.ConnectionTolerance*PipeNetwork.ConnectionTolerance)return true;
        // A direct pipe may enter anywhere near the head on the SAME supporting block.
        // The hidden passage inside that block joins it to the mouth; do not require
        // the visible pipe to match the head mesh's offset origin to the centimetre.
        if(elbow||Mathf.Abs(Vector3.Dot(offset,mount.Normal))>PipeNetwork.ConnectionTolerance||distance>.55f*.55f)return false;
        var ray=new Ray(pipe.Port(port)-mount.Normal*.12f,mount.Normal);
        foreach(var collider in mount.Wall.GetComponentsInChildren<Collider>())
            if(collider.enabled&&!collider.isTrigger&&collider.gameObject.activeInHierarchy&&collider.Raycast(ray,out var hit,.24f))return true;
        return false;
    }
    internal static bool Snap(PipePiece preview){
        PipeNetwork.RefreshIfDirty();
        float best=.55f*.55f;Vector3 chosenTarget=default,chosenNormal=default;int chosenPort=-1;bool chosenElbow=false;
        foreach(var dragon in PipePiece.Active){
            if(!dragon||!dragon.Live||!PipePrefabs.Dragon(dragon.Kind)||dragon.Connected[0]||!Find(dragon,out var mount))continue;
            for(int port=0;port<PipePiece.PortCount(preview.Kind);port++){
                if(!Target(preview,port,dragon,mount,out var target,out bool elbow))continue;
                var delta=target-preview.Port(port);if(delta.sqrMagnitude>=best)continue;
                best=delta.sqrMagnitude;chosenTarget=target;chosenNormal=mount.Normal;chosenPort=port;chosenElbow=elbow;
            }
        }
        if(chosenPort<0)return false;
        if(!chosenElbow)preview.transform.rotation=Quaternion.FromToRotation(preview.Direction(chosenPort),chosenNormal)*preview.transform.rotation;
        preview.transform.position=chosenTarget-preview.transform.rotation*PipePiece.LocalPort(preview.Kind,chosenPort);return true;
    }
}

// Visible automatic elbow. It adds no inventory piece, pump or extra outlet.
internal sealed class WallPipeAdapter:MonoBehaviour
{
    Mesh mesh;MeshRenderer renderer;static Material copper;
    Vector3 lastStart,lastDirection,lastNormal;float lastRadius;
    internal void Show(Vector3 start,Vector3 direction,Vector3 normal,float size){
        gameObject.SetActive(true);float radius=.4f*size;
        if(mesh&&start==lastStart&&direction==lastDirection&&normal==lastNormal&&radius==lastRadius)return;
        lastStart=start;lastDirection=direction;lastNormal=normal;lastRadius=radius;
        if(!mesh){mesh=new Mesh{name="FlowCraft automatic wall elbow"};gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;renderer=gameObject.AddComponent<MeshRenderer>();
            if(!copper){copper=new Material(PipeAssets.Load("elbow").Material);copper.name="FlowCraft wall fitting copper";copper.mainTexture=Texture2D.whiteTexture;copper.SetColor("_Color",new Color(.55f,.24f,.09f));}
            renderer.sharedMaterial=copper;
        }
        const int rings=17,sides=16;var vertices=new Vector3[rings*sides];var normals=new Vector3[vertices.Length];var uv=new Vector2[vertices.Length];var triangles=new int[(rings-1)*sides*6];
        var side=Vector3.Cross(direction,normal).normalized;
        for(int i=0;i<rings;i++){
            float a=i/(float)(rings-1)*Mathf.PI*.5f;var centre=start+direction*(radius*Mathf.Sin(a))+normal*(radius*(1-Mathf.Cos(a)));
            var tangent=direction*Mathf.Cos(a)+normal*Mathf.Sin(a);var radial=Vector3.Cross(side,tangent);
            // Socket collars at both ends cover the joint without exposing a gap.
            float width=(i<2||i>rings-3?.285f:.225f)*size;
            for(int j=0;j<sides;j++){
                float angle=j*Mathf.PI*2/sides;var n=side*Mathf.Cos(angle)+radial*Mathf.Sin(angle);int at=i*sides+j;
                vertices[at]=transform.InverseTransformPoint(centre+n*width);normals[at]=transform.InverseTransformDirection(n);uv[at]=new Vector2(j/(float)sides,i/(float)rings);
                if(i==0)continue;int k=((i-1)*sides+j)*6,one=(i-1)*sides+j,two=(i-1)*sides+(j+1)%sides,three=i*sides+j,four=i*sides+(j+1)%sides;
                triangles[k]=one;triangles[k+1]=three;triangles[k+2]=two;triangles[k+3]=two;triangles[k+4]=three;triangles[k+5]=four;
            }
        }
        mesh.Clear();mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateBounds();
    }
    void OnDestroy(){if(mesh)Destroy(mesh);}
}
