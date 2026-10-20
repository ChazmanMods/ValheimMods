using System.Collections;
using ArcaneWaterWorks;
using Jotunn.Managers;
using UnityEngine;
public sealed partial class StandaloneSmoke {
    IEnumerator StairVisualTest(Vector3 origin){
        EnvMan.instance.m_debugTimeOfDay=true;EnvMan.instance.m_debugTime=.5f;EnvMan.instance.m_debugEnv="Clear";
        foreach(var tree in Object.FindObjectsOfType<TreeBase>())if(Vector3.Distance(tree.transform.position,origin)<35)tree.gameObject.SetActive(false);
        float[] heights={0,0,0,-2,-2,-2,-4,-4,-4,-6};var route=new Route{Width=8,Depth=1.5f};
        for(int i=0;i<heights.Length;i++)route.Points.Add(new Node(0,heights[i],i*2));
        var go=Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanewaterworks_spring"),origin,Quaternion.identity);
        go.GetComponent<ZNetView>().GetZDO().Set("aww_route_v1",route.Encode());yield return Settle();
        var spring=go.GetComponent<Spring>();
        Check(spring.BankRoute!=null&&spring.BankRoute.Points[2].Y<spring.Route.Points[2].Y-.2f,"loaded staircase receives a gradual effective profile");
        CaptureWake("river-stairs-smoothed.png",origin+new Vector3(-9,5,12),origin+new Vector3(0,-2,9));
        CaptureWake("river-stairs-low.png",origin+new Vector3(-2,3,-2),origin+new Vector3(0,-3,12));
        ZNetScene.instance.Destroy(go);yield return Settle();
    }
}
