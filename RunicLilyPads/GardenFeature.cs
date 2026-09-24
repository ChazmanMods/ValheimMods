using UnityEngine;
using Jotunn.Managers;
namespace RunicLilyPads
{
    public sealed class GardenFeature:MonoBehaviour,Hoverable,Interactable
    {
        public int Kind;public float ModelHeight;
        internal bool Fountain=>Kind>0&&Kind!=4;
        internal bool HasGlobe=>Kind>=1&&Kind<=5;
        internal FeatureSettings Settings=new FeatureSettings();
        ZNetView view;Light lamp;AudioSource waterSound;Material flameMaterial;MeshRenderer model,flame;
        MaterialPropertyBlock block;string last;float poll,deadline;FeatureSettings pending;bool built;
        void Start(){view=GetComponent<ZNetView>();block=new MaterialPropertyBlock();}
        internal bool Live=>view&&view.IsValid();
        internal bool CanEdit()=>Live&&Player.m_localPlayer&&Vector3.Distance(Player.m_localPlayer.transform.position,transform.position)<8&&PrivateArea.CheckAccess(transform.position,0,false);
        internal bool Save(FeatureSettings settings){if(!settings.Valid||!CanEdit())return false;pending=settings.Copy();deadline=Time.time+5;view.ClaimOwnership();return true;}
        void Update(){
            if(!Live)return;
            if(pending!=null){if(!CanEdit()||Time.time>deadline)pending=null;else if(view.IsOwner()){view.GetZDO().Set("wg_feature_settings",pending.Encode());pending=null;poll=0;}}
            if(Time.time>=poll){poll=Time.time+.5f;string raw=view.GetZDO().GetString("wg_feature_settings","");if(raw==""&&view.IsOwner()){raw=Settings.Encode();view.GetZDO().Set("wg_feature_settings",raw);}if(raw!=last&&FeatureSettings.TryDecode(raw,out var loaded)){Settings=loaded;last=raw;}}
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return;
            if(!built)Build();
            bool near=Player.m_localPlayer&&Vector3.Distance(Player.m_localPlayer.transform.position,transform.position)<45;
            Color lightColor=Kind==-1?new Color(1,.65f,.25f):Kind==1||Kind==5?new Color(.25f,1,.7f):Kind==4?new Color(1,.75f,.2f):new Color(.65f,.4f,1);
            if(lamp){lamp.enabled=near&&Settings.Light>0;lamp.color=lightColor;lamp.intensity=Settings.Light;lamp.range=Settings.Range;}
            if(model){model.GetPropertyBlock(block);block.SetColor("_EmissionColor",Color.white*Settings.Glow);model.SetPropertyBlock(block);}
            if(flame){flame.enabled=Settings.Glow>0;flameMaterial.SetColor("_EmissionColor",lightColor*Settings.Glow);}
            if(waterSound){waterSound.volume=Mathf.MoveTowards(waterSound.volume,near?Settings.Volume:0,Time.deltaTime*.7f);if(near&&Settings.Volume>0){if(!waterSound.clip)waterSound.clip=GardenAudio.Get(8);if(waterSound.clip&&!waterSound.isPlaying)waterSound.Play();}else if(waterSound.volume<.001f)waterSound.Stop();}
            foreach(var flow in flows)if(flow)flow.gameObject.SetActive(near&&Settings.Flow);
        }
        readonly System.Collections.Generic.List<WaterFlow> flows=new System.Collections.Generic.List<WaterFlow>();
        void Build(){
            built=true;model=transform.Find("garden_visual").GetComponentInChildren<MeshRenderer>();
            if(HasGlobe||Kind==-1){
                var lightObject=new GameObject("garden_light");lightObject.transform.SetParent(transform.Find("garden_visual"),false);
                lightObject.transform.localPosition=(Kind==-1?new Vector3(0,.61f,0):Kind==1?new Vector3(-.005f,.799f,-.031f):Kind==4?new Vector3(-.145f,.78f,0):Kind==5?new Vector3(0,.748f,.229f):new Vector3(-.003f,.600f,-.002f))*ModelHeight;
                lamp=lightObject.AddComponent<Light>();lamp.type=LightType.Point;lamp.shadows=LightShadows.None;
                if(Kind==-1){var glow=GameObject.CreatePrimitive(PrimitiveType.Sphere);glow.name="lantern_warm_glow";Destroy(glow.GetComponent<Collider>());glow.transform.SetParent(lightObject.transform,false);glow.transform.localScale=Vector3.one*.1f;flame=glow.GetComponent<MeshRenderer>();flameMaterial=new Material(model.sharedMaterial);flameMaterial.mainTexture=Texture2D.whiteTexture;flameMaterial.SetTexture("_EmissiveTex",Texture2D.whiteTexture);flameMaterial.SetColor("_Color",new Color(1,.6f,.15f));flame.sharedMaterial=flameMaterial;}
            }
            if(!Fountain)return;
            var soundObject=new GameObject("fountain_audio");soundObject.transform.SetParent(transform.Find("garden_visual"),false);
            soundObject.transform.localPosition=(Kind==1?new Vector3(0,.55f,0):Kind==5?new Vector3(0,.47f,.34f):new Vector3(0,.29f,0))*ModelHeight;
            waterSound=soundObject.AddComponent<AudioSource>();waterSound.playOnAwake=false;waterSound.loop=true;waterSound.spatialBlend=1;waterSound.rolloffMode=AudioRolloffMode.Custom;waterSound.SetCustomCurve(AudioSourceCurveType.CustomRolloff,new AnimationCurve(new Keyframe(0,1),new Keyframe(.15f,.6f),new Keyframe(.4f,.18f),new Keyframe(1,0)));waterSound.minDistance=.6f;waterSound.maxDistance=10;waterSound.spread=0;waterSound.reverbZoneMix=0;waterSound.volume=0;waterSound.dopplerLevel=0;
            // Coordinates measured from the normalized supplied meshes and their basin surfaces.
            if(Kind==1){
                for(int i=0;i<4;i++){float angle=i*Mathf.PI*.5f+.4f;var radial=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                    AddFlow(new Vector3(-.005f,.70f,-.031f)+radial*.13f,new Vector3(-.005f,.553f,-.031f)+radial*.20f,.017f);}
            }else if(Kind==2||Kind==3){AddFlow(new Vector3(-.008f,.478f,-.015f),new Vector3(-.008f,.292f,-.04f),.024f);}
            else if(Kind==5){AddFlow(new Vector3(-.08f,.64f,.305f),new Vector3(-.08f,.475f,.345f),.015f);AddFlow(new Vector3(.08f,.64f,.305f),new Vector3(.08f,.475f,.345f),.015f);}
        }
        void AddFlow(Vector3 start,Vector3 finish,float radius){
            flows.Add(WaterFlow.Create(transform.Find("garden_visual"),(start-Vector3.right*radius)*ModelHeight,(start+Vector3.right*radius)*ModelHeight,finish*ModelHeight,false));

        }
        public string GetHoverName()=>Kind==4?global::Runic.Localization.RunicText.Get("text_79eafab85a1b"):Fountain?global::Runic.Localization.RunicText.Get("text_6a84e2bf653c")+Kind:global::Runic.Localization.RunicText.Get("text_0961a763d612");
        public float GetHoverOffset()=>0;
        public string GetHoverText()=>GetHoverName()+global::Runic.Localization.RunicText.Get("text_a2b67a1fd860");
        public bool Interact(Humanoid user,bool hold,bool alt){if(hold||user!=Player.m_localPlayer||!CanEdit())return false;FeatureEditor.Open(this);return true;}
        public bool UseItem(Humanoid user,ItemDrop.ItemData item)=>false;
        void OnDestroy(){if(FeatureEditor.Target==this)FeatureEditor.Close();if(flameMaterial)Destroy(flameMaterial);}
    }
}
