using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using Jotunn.Managers;
public partial class Smoke {
 IEnumerator CheckWaterfallAudio(Assembly asm,Player player,Vector3 ground){
  var soundType=asm.GetType("RunicLilyPads.WaterfallAudio");var pieceType=asm.GetType("RunicLilyPads.GardenPiece");
  var high=UnityEngine.Object.Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanedecor_pond_round"),ground,Quaternion.identity);
  var low=UnityEngine.Object.Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanedecor_pond_round"),ground+new Vector3(5,-1,0),Quaternion.identity);
  var vHigh=high.GetComponent<ZNetView>();var vLow=low.GetComponent<ZNetView>();
  // Legacy ponds gain the default waterfall volume; night-only ambience remains off by day.
  vHigh.GetZDO().Set("arcane_garden_settings","2|8|1.2|0|0|1|18|0|1|0.4|1|1|1|100");
  vLow.GetZDO().Set("arcane_garden_settings","2|8|1.2|0|0|1|18|0|1|0.4|1|1|1|0");
  var playerPosition=player.transform.position;player.transform.position=ground+new Vector3(2,1,-1);yield return new WaitForSecondsRealtime(4);
  var sounds=high.GetComponentsInChildren(soundType);var other=low.GetComponentsInChildren(soundType);
  FeatureCheck(sounds.Length==1&&other.Length==0,"exactly one waterfall sound per elevated overlapping pair");
  AudioSource source=sounds.Length==1?((Component)sounds[0]).GetComponent<AudioSource>():null;
  FeatureCheck(source&&source.clip&&source.clip.name=="waterfall_001"&&source.isPlaying&&source.loop&&source.volume>.3f,"legacy ponds play embedded waterfall independently of night-only ambience");
  FeatureCheck(source&&source.spatialBlend==1&&source.minDistance==1&&source.maxDistance==16&&source.rolloffMode==AudioRolloffMode.Linear,"waterfall sound emanates from join with distance falloff");
  int sample=source?source.timeSamples:0;yield return new WaitForSecondsRealtime(.3f);FeatureCheck(source&&source.timeSamples!=sample,"waterfall recording advances during play");
  // Edit from the lower pond: the group control must affect the upper pond emitter.
  vLow.GetZDO().Set("arcane_garden_settings","3|8|1.2|0|0|1|18|0|1|0.4|1|1|1|200|0");yield return new WaitForSecondsRealtime(3);
  source=high.GetComponentInChildren(soundType)?.GetComponent<AudioSource>();
  FeatureCheck(source&&!source.isPlaying&&source.volume<=.001f,"group waterfall mute from lower pond stops upper emitter");
  vLow.GetZDO().Set("arcane_garden_settings","3|8|1.2|0|0|1|18|0|1|0.4|1|1|1|201|0.2");yield return new WaitForSecondsRealtime(3);
  source=high.GetComponentInChildren(soundType)?.GetComponent<AudioSource>();FeatureCheck(source&&source.isPlaying&&Mathf.Abs(source.volume-.2f)<.01f,"saved waterfall group volume resumes independent playback");
  // Hold both the body and transform at the test locations so Character physics /
  // interpolation cannot undo an artificial transform-only teleport between frames.
  var body=player.GetComponent<Rigidbody>();
  foreach(bool away in new[]{true,false}){
   var location=away?ground+new Vector3(-25,1,0):high.GetComponentInChildren(soundType).transform.position+Vector3.up;
   float until=Time.realtimeSinceStartup+1.5f;
   while(Time.realtimeSinceStartup<until){body.linearVelocity=Vector3.zero;body.position=location;player.transform.position=location;Physics.SyncTransforms();yield return null;}
   source=high.GetComponentInChildren(soundType)?.GetComponent<AudioSource>();float distance=source?Vector3.Distance(player.transform.position,source.transform.position):-1;
   FeatureCheck(source&&(away?!source.isPlaying:source.isPlaying), (away?"distant waterfall stops unnecessary playback":"waterfall resumes when player returns")+" distance="+distance+" volume="+(source?source.volume:-1));
  }
  ZNetScene.instance.Destroy(low);yield return new WaitForSecondsRealtime(2);FeatureCheck(high.GetComponentsInChildren(soundType).Length==0&&!source,"removing overlap removes waterfall emitter");
  low=UnityEngine.Object.Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanedecor_pond_round"),ground+new Vector3(5,0,0),Quaternion.identity);
  low.GetComponent<ZNetView>().GetZDO().Set("arcane_garden_settings","2|8|1.2|0|0|1|18|0|1|0.4|1|1|1|0");yield return new WaitForSecondsRealtime(3);
  FeatureCheck(high.GetComponentsInChildren(soundType).Length==0&&low.GetComponentsInChildren(soundType).Length==0,"equal-level overlapping ponds stay silent");
  ZNetScene.instance.Destroy(high);ZNetScene.instance.Destroy(low);player.transform.position=playerPosition;yield return new WaitForSecondsRealtime(2);
 }
}
