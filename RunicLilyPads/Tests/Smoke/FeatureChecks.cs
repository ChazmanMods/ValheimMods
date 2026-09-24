using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using Jotunn.Managers;
public partial class Smoke
{
    void FeatureCheck(bool ok,string label){if(ok)Logger.LogInfo("FEATURE PASS "+label);else{worldErrors++;Logger.LogError("FEATURE FAIL "+label);}}
    IEnumerator CheckFeatures(Assembly asm,Player player){
        var floatType=asm.GetType("RunicLilyPads.LilyFloat");
        foreach(var series in new[]{new[]{"blue_001","blue_002","blue_003"},new[]{"white_001","white_002","white_003"}}){
            float previousHeight=0,previousWidth=0;
            foreach(string id in series){
                var prefab=PrefabManager.Instance.GetPrefab("piece_arcanedecor_lily_"+id);
                var flower=prefab.transform.Find("lily_visual/floating_lily").GetChild(1);
                var bounds=flower.GetComponent<MeshFilter>().sharedMesh.bounds.size*flower.localScale.x;
                FeatureCheck(bounds.y>previousHeight&&bounds.x>previousWidth,"ordered blossom dimensions "+id+" "+bounds);
                previousHeight=bounds.y;previousWidth=bounds.x;
                bool triggers=true;foreach(var c in prefab.GetComponentsInChildren<Collider>(true))triggers&=c.isTrigger;
                FeatureCheck(triggers,"pass-through colliders "+id);
            }
        }
        var lily=UnityEngine.Object.Instantiate(PrefabManager.Instance.GetPrefab("piece_runic_lilypad_small"),player.transform.position+Vector3.right*.2f,Quaternion.identity);
        ((Behaviour)lily.GetComponent(floatType)).enabled=false;
        var moving=lily.transform.Find("lily_visual/floating_lily");var anchor=lily.transform.position;
        yield return new WaitForSecondsRealtime(.6f);
        FeatureCheck(moving.localPosition.magnitude>.05f&&lily.transform.position==anchor,"lily brushes aside without moving saved anchor");
        lily.transform.position+=Vector3.right*6;
        yield return new WaitForSecondsRealtime(1.2f);
        FeatureCheck(moving.localPosition.magnitude<.02f,"lily returns after player passes");
        ZNetScene.instance.Destroy(lily);
        var pond=UnityEngine.Object.Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanedecor_nightgarden"),player.transform.position+Vector3.left*2,Quaternion.identity);
        pond.GetComponent<ZNetView>().GetZDO().Set("arcane_garden_settings","1|6|1.2|1|1|1|40|30|0");
        for(int kind=0;kind<=5;kind++){
            if(kind==3)continue;
            var id=kind==0?"piece_arcanedecor_lantern_001":"piece_arcanedecor_fountain_00"+kind;
            var obj=UnityEngine.Object.Instantiate(PrefabManager.Instance.GetPrefab(id),player.transform.position+Vector3.forward*3,Quaternion.identity);

            var zdo=obj.GetComponent<ZNetView>().GetZDO();zdo.Set("wg_feature_settings","1|3|8|4|0.7|1");
            yield return new WaitForSecondsRealtime(1.7f);
            if(kind<=5){var light=obj.GetComponentInChildren<Light>();FeatureCheck(light&&light.enabled&&Mathf.Abs(light.intensity-3)<.01f&&Mathf.Abs(light.range-8)<.01f,"saved light controls "+id);}
            var model=obj.transform.Find("garden_visual").GetComponentInChildren<MeshRenderer>();var block=new MaterialPropertyBlock();model.GetPropertyBlock(block);
            FeatureCheck(block.GetColor("_EmissionColor").r==4,"saved glow control "+id);
            if(kind>0&&kind!=4){
                var audio=obj.GetComponentInChildren<AudioSource>();var ambience=pond.GetComponentInChildren<AudioSource>();
                FeatureCheck(audio&&audio.isPlaying&&audio.timeSamples>0&&audio.volume>.65f&&ambience&&ambience.isPlaying,"independent simultaneous fountain and night ambience "+kind);
                var ps=obj.GetComponentInChildren<ParticleSystem>();var drops=new ParticleSystem.Particle[64];int count=ps?ps.GetParticles(drops):0;var first=drops[0].position;
                yield return new WaitForSecondsRealtime(.15f);if(ps)ps.GetParticles(drops);
                FeatureCheck(count==32&&Vector3.Distance(first,drops[0].position)>.005f,"animated water particles "+kind);
            }
            CaptureFeature(obj,kind);
            zdo.Set("wg_feature_settings","1|0|4|0|0|0");yield return new WaitForSecondsRealtime(1.8f);
            var lamp=obj.GetComponentInChildren<Light>();FeatureCheck(!lamp||!lamp.enabled,"light disabled "+id);
            if(kind>0&&kind!=4)FeatureCheck(!obj.GetComponentInChildren<AudioSource>().isPlaying&&!obj.transform.Find("garden_visual/fountain_water_stream").gameObject.activeSelf&&pond.GetComponentInChildren<AudioSource>().isPlaying,"fountain mute and flow off preserve ambience "+kind);
            obj.GetComponent<WearNTear>().Remove(true);
        }
        var table=PrefabManager.Instance.GetPrefab("Hammer").GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces;
        FeatureCheck(table.m_pieces.Exists(p=>p.name=="piece_arcanedecor_ferns"),"original decorations restored to hammer");
        int pieceCount=0;foreach(var p in table.m_pieces)if(p.name.StartsWith("piece_arcanedecor_")||p.name.StartsWith("piece_runic_lilypad_")){pieceCount++;FeatureCheck(p.GetComponent<Piece>().m_icon,"matching icon assigned "+p.name);}
        FeatureCheck(pieceCount==50,"50 distinct garden hammer entries");
        pond.GetComponent<WearNTear>().Remove(true);
    }
}

