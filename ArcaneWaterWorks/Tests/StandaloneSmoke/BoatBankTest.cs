using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ArcaneWaterWorks;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;
public sealed partial class StandaloneSmoke {
    IEnumerator BoatBankTest(Vector3 origin){
        var route=new Route{Width=8,Depth=2};
        for(int i=0;i<=6;i++)route.Points.Add(new Node(0,-.35f,i*4));
        var go=Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanewaterworks_spring"),origin,Quaternion.identity);
        go.GetComponent<ZNetView>().GetZDO().Set("aww_route_v1",route.Encode());yield return Settle();
        var spring=go.GetComponent<Spring>();
        float rawJump=0,fixedJump=0;WaterVolume cache=null;
        foreach(float z in new[]{4f,12f,20f})foreach(float sign in new[]{-1f,1f}){
            var inside=origin+new Vector3(sign*3.999f,0,z);var outside=origin+new Vector3(sign*4.001f,0,z);
            float a=Floating.GetWaterLevel(inside,ref cache),b=Floating.GetWaterLevel(outside,ref cache);
            rawJump=Mathf.Max(rawJump,Mathf.Abs(a-b));
            a=RiverShipWater.Level(inside,ref cache);b=RiverShipWater.Level(outside,ref cache);
            fixedJump=Mathf.Max(fixedJump,Mathf.Abs(a-b));
        }
        Logger.LogInfo("BANK probe jump old="+rawJump+" corrected="+fixedJump);
        Check(rawJump>1,"fixture reproduces river-bank water-level discontinuity");
        Check(fixedJump<.03f,"ship probes cross both banks without water-height cliff");
        var dry=origin+new Vector3(4.1f,0,12);
        Check(Floating.GetWaterLevel(dry,ref cache)<RiverShipWater.Level(dry,ref cache)-1,"dry bank continuation is restricted to ship probes");
        var far=origin+new Vector3(40,0,12);
        Check(RiverShipWater.Level(far,ref cache)==Floating.GetWaterLevel(far,ref cache),"water outside river vicinity stays native");
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(Ship),nameof(Ship.CustomFixedUpdate))).Transpilers.Count>0,"ship physics uses corrected sampling patch");
        spring.Surface(0,12,out _,out float mean,out _);
        var boat=Instantiate(PrefabManager.Instance.GetPrefab("Karve"),origin+new Vector3(2.5f,mean+.4f,12),Quaternion.identity);
        yield return new WaitForSecondsRealtime(4);
        var ship=boat.GetComponent<Ship>();var body=boat.GetComponent<Rigidbody>();var wear=boat.GetComponent<WearNTear>();
        // Register a rider so native water-impact damage is exercised even though the
        // isolated test player is kept safely beside the boat.
        var players=(List<Player>)AccessTools.Field(typeof(Ship),"m_players").GetValue(ship);players.Add(Player.m_localPlayer);

        float before=wear.GetComponent<ZNetView>().GetZDO().GetFloat(ZDOVars.s_health,wear.m_health),maxAngular=0,maxVertical=0;
        for(int bump=0;bump<4;bump++){
            float end=Time.time+2.5f;
            while(Time.time<end&&boat){
                var velocity=body.linearVelocity;velocity.x=bump%2==0?.6f:-.6f;body.linearVelocity=velocity;
                maxAngular=Mathf.Max(maxAngular,body.angularVelocity.magnitude);maxVertical=Mathf.Max(maxVertical,Mathf.Abs(body.linearVelocity.y));
                yield return new WaitForFixedUpdate();
            }
        }
        float after=boat?wear.GetComponent<ZNetView>().GetZDO().GetFloat(ZDOVars.s_health,wear.m_health):0;
        Logger.LogInfo("BANK Karve health="+before+" -> "+after+" maxAngular="+maxAngular+" maxVertical="+maxVertical+" position="+(boat?boat.transform.position.ToString():"destroyed"));
        Check(boat&&after>=before-20,"four gentle bank approaches do not destroy or heavily damage Karve");
        Check(maxAngular<2&&maxVertical<3,"bank approaches avoid violent spin or vertical launch");
        if(boat){players.Remove(Player.m_localPlayer);ZNetScene.instance.Destroy(boat);}
        ZNetScene.instance.Destroy(go);yield return Settle();
    }
}

