using System.Collections;
using System.Linq;
using ArcaneWaterWorks;
using Jotunn.Managers;
using UnityEngine;
public sealed partial class StandaloneSmoke {
    IEnumerator ShoreVisualTest(Vector3 origin){
        float sea=OceanJoin.Sea;Vector3 coast=Vector3.zero,direction=Vector3.zero;bool found=false;
        // Locate a real shore in the test world's height field, then stream it in.
        for(int r=32;r<=768&&!found;r+=16)for(int i=0;i<48&&!found;i++){
            float angle=i*Mathf.PI*2/48;var p=origin+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*r;
            float h=WorldGenerator.instance.GetHeight(p.x,p.z);if(h<sea+1.4f||h>sea+3.5f)continue;
            for(int j=0;j<16;j++){float a=j*Mathf.PI*2/16;var d=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));var end=p+d*24;
                if(WorldGenerator.instance.GetHeight(end.x,end.z)<sea-.4f){coast=new Vector3(p.x,h,p.z);direction=d;found=true;break;}}
        }
        Check(found,"found real native ocean shoreline");if(!found)yield break;
        Player.m_localPlayer.transform.position=coast+Vector3.up*3;Player.m_localPlayer.GetComponent<Rigidbody>().position=Player.m_localPlayer.transform.position;
        yield return new WaitForSecondsRealtime(12);
        EnvMan.instance.m_debugTimeOfDay=true;EnvMan.instance.m_debugTime=.5f;EnvMan.instance.m_debugEnv="Clear";
        if(RiverTerrain.SampleBase(coast,out float ground))coast.y=ground;
        var route=new Route{Width=8,Depth=1.5f,Mouth=true};
        for(int i=0;i<=8;i++){var p=direction*(i*3);route.Points.Add(new Node(p.x,Mathf.Lerp(-.35f,sea-coast.y,i/8f),p.z));}
        var go=Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanewaterworks_spring"),coast,Quaternion.identity);
        go.GetComponent<ZNetView>().GetZDO().Set("aww_route_v1",route.Encode());yield return Settle();yield return null;
        var surface=go.transform.Find("waterworks_river/river_surface");var renderer=surface.GetComponent<MeshRenderer>();var mesh=surface.GetComponent<MeshFilter>().sharedMesh;
        Check(renderer.sharedMaterial.renderQueue==3050,"river transition renders after native water");
        Check((GameCamera.instance.GetComponent<Camera>().depthTextureMode&DepthTextureMode.Depth)!=0,"opaque scene depth available for shoreline clipping");
        Check(OceanJoin.BankWeight(0,2)==0&&OceanJoin.BankWeight(2,0)==0&&OceanJoin.BankWeight(1,1)==1,"ocean waves taper to zero at bank and dry edge");
        var initial=mesh.vertices;yield return new WaitForSecondsRealtime(.3f);var later=mesh.vertices;var shore=mesh.uv3;
        int banks=0;float movement=0;for(int i=0;i<initial.Length;i++)if(shore[i].x<=.0001f){banks++;movement=Mathf.Max(movement,Mathf.Abs(initial[i].y-later[i].y));}
        Check(mesh.vertexCount>0&&banks>0&&movement<.001f,"coastal river mesh shoreline stays anchored during ocean waves");
        foreach(var tree in Object.FindObjectsOfType<TreeBase>())if(Vector3.Distance(tree.transform.position,coast)<35)tree.gameObject.SetActive(false);
        var side=Vector3.Cross(Vector3.up,direction);
        CaptureWake("shore-bank-fixed.png",coast+direction*7+side*6+Vector3.up*1.4f,coast+direction*10);
        CaptureWake("shore-mouth-fixed.png",coast+direction*7+side*3+Vector3.up*3,coast+direction*19+Vector3.down);
        for(int i=0;i<8;i++){yield return new WaitForSecondsRealtime(.25f);CaptureWake("shore-mouth-"+i+".png",coast+direction*7+side*3+Vector3.up*3,coast+direction*19+Vector3.down);}
        var boat=Instantiate(PrefabManager.Instance.GetPrefab("Karve"),coast+direction*10+Vector3.up*.3f,Quaternion.LookRotation(direction));
        yield return new WaitForSecondsRealtime(5);
        var masks=boat.GetComponentsInChildren<MeshRenderer>().Where(r=>r.sharedMaterial&&r.sharedMaterial.shader.name=="Custom/WaterMask").ToArray();
        Check(masks.Length>0,"native Karve hull water masks present");
        var focus=boat.transform.position;
        CaptureWake("river-boat-interior.png",focus-direction*3+Vector3.up*3,focus);
        CaptureWake("river-boat-side.png",focus+side*7+Vector3.up*2,focus);
        ZNetScene.instance.Destroy(boat);
        ZNetScene.instance.Destroy(go);yield return Settle();
    }
}
