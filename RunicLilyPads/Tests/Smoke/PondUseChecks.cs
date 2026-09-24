using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Jotunn.Managers;
public partial class Smoke
{
    IEnumerator CheckPondUse(Assembly asm,Player player)
    {
        var pos=player.transform.position+new Vector3(25,0,0);
        if(!Physics.Raycast(pos+Vector3.up*100,Vector3.down,out var hit,300,LayerMask.GetMask("terrain"))){worldErrors++;Logger.LogError("USE FAIL no terrain");yield break;}
        pos=hit.point;
        var table=new GameObject("placement_test_table").AddComponent<PieceTable>();
        var buildField=AccessTools.Field(typeof(Player),"m_buildPieces");var originalTable=buildField.GetValue(player);
        var ghostField=AccessTools.Field(typeof(Player),"m_placementGhost");var rangeField=AccessTools.Field(typeof(Player),"m_maxPlaceDistance");var originalRange=rangeField.GetValue(player);
        var camera=GameCamera.instance.transform;var oldCameraPos=camera.position;var oldCameraRot=camera.rotation;
        var lilyPrefab=PrefabManager.Instance.GetPrefab("piece_runic_lilypad_small");
        var categories=new List<List<Piece>>();for(int c=0;c<32;c++)categories.Add(new List<Piece>{lilyPrefab.GetComponent<Piece>()});
        AccessTools.Field(typeof(PieceTable),"m_availablePiecesByCategory").SetValue(table,categories);
        buildField.SetValue(player,table);rangeField.SetValue(player,50f);
        for(int shape=0;shape<4;shape++)foreach(float depth in new[]{.4f,1.2f,3f}){
            var ids=new[]{"round","oval","kidney","wild"};
            var pond=UnityEngine.Object.Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanedecor_pond_"+ids[shape]),pos,Quaternion.identity);
            pond.GetComponent<ZNetView>().GetZDO().Set("arcane_garden_settings","1|6|"+depth.ToString(System.Globalization.CultureInfo.InvariantCulture)+"|1|1|1|40|30|0");
            yield return new WaitForSecondsRealtime(1.5f);
            GameObject ghost=null;
            try{
                ZNetView.m_forceDisableInit=true;ghost=UnityEngine.Object.Instantiate(lilyPrefab);ZNetView.m_forceDisableInit=false;
                foreach(var t in ghost.GetComponentsInChildren<Transform>())t.gameObject.layer=LayerMask.NameToLayer("ghost");
                ghostField.SetValue(player,ghost);AccessTools.Field(typeof(Player),"m_manualSnapPoint").SetValue(player,-1);
                var target=pos+new Vector3(.3f,-.15f,.2f);camera.position=target+new Vector3(0,4,-2);camera.LookAt(target);
                Physics.SyncTransforms();
                AccessTools.Method(typeof(Player),"UpdatePlacementGhost").Invoke(player,new object[]{false});
                var status=(Player.PlacementStatus)AccessTools.Field(typeof(Player),"m_placementStatus").GetValue(player);
                if(status!=Player.PlacementStatus.Valid)throw new Exception("lily ghost status "+status);
                if(Math.Abs(ghost.transform.position.y-(pos.y-.135f))>.03f)throw new Exception("wrong lily waterline");
                Logger.LogInfo("USE PASS actual hammer ghost shape="+shape+" depth="+depth);
                var sound=pond.transform.Find("garden_generated").GetComponent<AudioSource>();
                if(!sound||!sound.isPlaying||sound.volume<=0||sound.timeSamples<=0)throw new Exception("live pond sound not playing");
                Logger.LogInfo("USE PASS live pond ambience volume="+sound.volume+" samples="+sound.timeSamples);
                if(shape==0&&depth==.4f){
                    var lanternPoint=pos+new Vector3(.6f,1,0);
                    if(!Physics.Raycast(lanternPoint,Vector3.down,out var bed,5,LayerMask.GetMask("terrain")))throw new Exception("missing pond bed");
                    var lantern=UnityEngine.Object.Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanedecor_lantern_001"),bed.point,Quaternion.identity);
                    if(lantern.GetComponentInChildren<MeshFilter>().sharedMesh.vertexCount!=9918)throw new Exception("lantern mesh");
                    if(!(bool)asm.GetType("RunicLilyPads.PondPlacement").GetMethod("HasSurfaceContact",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{lantern,true}))throw new Exception("lantern not grounded");
                    Logger.LogInfo("USE PASS stone lantern grounded in pond, shader="+lantern.GetComponentInChildren<MeshRenderer>().sharedMaterial.shader.name);
                    lantern.GetComponent<WearNTear>().Remove(true);
                }
            }catch(Exception e){worldErrors++;Logger.LogError("USE FAIL shape="+shape+" depth="+depth+" "+e);}
            finally{ZNetView.m_forceDisableInit=false;ghostField.SetValue(player,null);if(ghost)UnityEngine.Object.Destroy(ghost);pond.GetComponent<WearNTear>().Remove(true);}
            yield return new WaitForSecondsRealtime(.3f);
        }
        buildField.SetValue(player,originalTable);rangeField.SetValue(player,originalRange);camera.position=oldCameraPos;camera.rotation=oldCameraRot;UnityEngine.Object.Destroy(table.gameObject);
    }
}
