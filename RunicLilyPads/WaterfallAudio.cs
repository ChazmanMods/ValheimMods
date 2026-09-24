using UnityEngine;
namespace RunicLilyPads
{
    // One emitter per high/low pond pair, attached only to the high pond's flow.
    // Rebuilding/removing the transition destroys its sound with the same object.
    public sealed class WaterfallAudio:MonoBehaviour
    {
        GardenPiece owner,lower;AudioSource source;float next;float volume;float range=16;
        int desiredTrack=9,currentTrack;
        internal static void Create(Transform parent,GardenPiece pond,GardenPiece low,Vector3 position){
            var go=new GameObject("pond_waterfall_sound");go.transform.SetParent(parent,false);go.transform.position=position;
            var audio=go.AddComponent<WaterfallAudio>();audio.owner=pond;audio.lower=low;
            audio.source=go.AddComponent<AudioSource>();var s=audio.source;
            s.playOnAwake=false;s.loop=true;s.spatialBlend=1;s.rolloffMode=AudioRolloffMode.Linear;
            s.minDistance=1;s.maxDistance=16;s.dopplerLevel=0;s.spread=0;s.reverbZoneMix=0;s.volume=0;
        }
        void Update(){
            if(!owner||!owner.Live||!lower||!lower.Live){source.Stop();return;}
            if(Time.time>=next){
                next=Time.time+.5f;volume=owner.Settings.WaterfallVolume;range=owner.Settings.WaterfallRange;
                source.maxDistance=range;source.minDistance=Mathf.Min(5,range*.2f);
                desiredTrack=WaterfallSoundRules.Track(owner.WaterHeight-lower.WaterHeight,Plugin.TallWaterfallHeight.Value);
            }
            bool near=Player.m_localPlayer&&(Player.m_localPlayer.transform.position-transform.position).sqrMagnitude<(range+2)*(range+2);
            // Fade the old sound out before switching, then fade the new one in.
            if(currentTrack!=desiredTrack&&source.volume<=.001f){source.Stop();source.clip=null;currentTrack=desiredTrack;}
            float target=near&&currentTrack==desiredTrack?volume:0;
            source.volume=Mathf.MoveTowards(source.volume,target,Time.deltaTime*.8f);
            if(target>0){if(!source.clip)source.clip=GardenAudio.Get(currentTrack);if(source.clip&&!source.isPlaying)source.Play();}
            else if(source.volume<=.001f&&source.isPlaying)source.Stop();
        }
    }
}
