using System.Collections;
using System.Collections.Generic;
using ArcaneWaterWorks;
using Jotunn.Managers;
using UnityEngine;

public sealed partial class StandaloneSmoke
{
    IEnumerator TrenchTerrainTest(Vector3 origin){
        if(RiverTerrain.SampleCurrent(origin,out var ground))origin.y=ground;
        var samples=new List<Vector3>();
        for(int z=-8;z<=24;z+=2)for(int x=-8;x<=16;x+=2){var p=origin+new Vector3(x,0,z);if(RiverTerrain.SampleCurrent(p,out var h)){p.y=h;samples.Add(p);}}
        var high=origin+Vector3.up*20;
        var route=new Route{Width=4,Depth=.8f};route.Points.Add(new Node(0,0,0));route.Points.Add(new Node(0,-12,2));route.Points.Add(new Node(0,-20,6));
        // Reproduce the old calculation explicitly: its bank was raised to the steep plane.
        var oldBed=typeof(Route).GetMethod("UnboundedBed",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        Check((float)oldBed.Invoke(route,new object[]{2f,-2f,-20f,null})>-5,"reproduce old bank calculation raising low terrain over fifteen metres");
        var go=Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanewaterworks_spring"),high,Quaternion.identity);var spring=go.GetComponent<Spring>();go.GetComponent<ZNetView>().GetZDO().Set("aww_route_v1",route.Encode());
        yield return Settle();Check(spring.Route!=null,"saved steep route loads with terrain safeguard");
        void CheckBanks(string phase){float highest=float.NegativeInfinity;foreach(var p in samples){if(RiverTerrain.SampleCurrent(p,out var h))highest=Mathf.Max(highest,h-p.y);}Check(highest<=.351f,phase+" maximum bank rise "+highest.ToString("F3")+" m");}
        CheckBanks("loading existing steep route");
        var mesh=go.transform.Find("waterworks_river/river_surface").GetComponent<MeshFilter>().sharedMesh;
        int apron=0;float biggestGap=0;
        foreach(var vertex in mesh.vertices){
            spring.Surface(vertex.x,vertex.z,out float distance,out float nominal,out _);
            if(distance<spring.Route.Width*.5f+1.45f)continue;
            if(!RiverTerrain.SamplePlanned(spring,high+vertex,out float terrain))continue;
            Check(Mathf.Abs(vertex.y-nominal)<.05f,"water edge is not draped down over the hillside");
            apron++;biggestGap=Mathf.Max(biggestGap,vertex.y+high.y-terrain);
        }
        Check(mesh.vertexCount>0&&biggestGap<.25f,"bank-constrained water edge meets terrain; maximum gap "+biggestGap);
        Check(!PrefabManager.Instance.GetPrefab("piece_arcanewaters_size"),"sizing icon removed");
        foreach(string mode in new[]{"configure","end","reopen"})Check(PrefabManager.Instance.GetPrefab("piece_arcanewaters_"+mode),"branch tool registered: "+mode);
        var previewObject=new GameObject("reach preview check");var preview=previewObject.AddComponent<RiverReachPreview>();
        preview.Show(origin,4);var ring=previewObject.GetComponentInChildren<LineRenderer>();
        Check(ring&&Mathf.Abs(ring.GetPosition(0).x-origin.x-4)<.01f,"reach preview shows smaller radius");
        preview.Show(origin,64);Check(Mathf.Abs(ring.GetPosition(0).x-origin.x-64)<.01f&&previewObject.transform.position==Vector3.zero,"reach preview enlarges without moving placement root");Destroy(previewObject);
        RiverHoe.StrokeLength=32;RiverHoe.Resize(1);Check(RiverHoe.StrokeLength==36,"wheel up enlarges trench reach");
        RiverHoe.Resize(-1);Check(RiverHoe.StrokeLength==32,"wheel down reduces trench reach");
        for(int i=0;i<30;i++)RiverHoe.Resize(-1);Check(RiverHoe.StrokeLength==4,"wheel size clamps at minimum without wrapping");
        for(int i=0;i<30;i++)RiverHoe.Resize(1);Check(RiverHoe.StrokeLength==64,"wheel size clamps at maximum without wrapping");
        RiverHoe.StrokeLength=32;

        RiverHoe.Select(spring);
        var probe=spring.Route.Copy();var target=origin+new Vector3(0,0,10)-spring.transform.position;
        bool preflight=probe.AppendFrom(probe.EditableTip,new Node(target.x,target.y,target.z),spring.Ground,spring.Sea,out string reason,spring.Receiving);
        Check(RiverHoe.Extend(origin+new Vector3(0,0,10)),"first real River Trench extension accepted (preflight="+preflight+", "+reason+", "+spring.Status+")");yield return Settle();CheckBanks("first trench stroke");
        Check(RiverHoe.Extend(origin+new Vector3(0,0,14)),"second real River Trench extension accepted");yield return Settle();CheckBanks("second trench stroke");
        for(int i=0;i<3;i++){RiverTerrain.Refresh(spring,spring.Route);yield return Settle();CheckBanks("rebuild "+i);}
        ZNetScene.instance.Destroy(go);yield return Settle();
        float error=0;foreach(var p in samples)if(RiverTerrain.SampleCurrent(p,out var h))error=Mathf.Max(error,Mathf.Abs(h-p.y));
        Check(error<.01f,"removing river restores original terrain without native terrain edits");
        float receiving=origin.y;foreach(var p in samples)receiving=Mathf.Max(receiving,p.y);receiving+=.05f;
        var mouth=new Route{Width=4,Depth=.8f,Mouth=true};mouth.Points.Add(new Node(0,0,0));for(int i=1;i<=8;i++)mouth.Points.Add(new Node(0,0,i*2));
        var mouthOrigin=new Vector3(origin.x,receiving,origin.z);
        var outlet=Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanewaterworks_spring"),mouthOrigin,Quaternion.identity);outlet.GetComponent<ZNetView>().GetZDO().Set("aww_route_v1",mouth.Encode());
        yield return Settle();float rise=float.NegativeInfinity,aboveWater=float.NegativeInfinity;int checkedPoints=0;
        foreach(var p in samples){var local=p-origin;if(Mathf.Abs(local.x)>2.001f||local.z<-.001f||local.z>16.001f)continue;
            if(RiverTerrain.SampleCurrent(p,out var h)){rise=Mathf.Max(rise,h-p.y);aboveWater=Mathf.Max(aboveWater,h-receiving);checkedPoints++;}}
        Check(checkedPoints>=20&&rise<=.001f,"saved outlet tail does not raise natural underwater terrain (samples="+checkedPoints+", rise="+rise.ToString("F6")+" m)");
        Check(aboveWater<=-.29f,"entire natural-water outlet rim remains submerged");
        ZNetScene.instance.Destroy(outlet);yield return Settle();error=0;
        foreach(var p in samples)if(RiverTerrain.SampleCurrent(p,out var h))error=Mathf.Max(error,Mathf.Abs(h-p.y));
        Check(error<.01f,"removing outlet restores original natural shoreline");
    }
}
