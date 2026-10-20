using System.Collections.Generic;
using UnityEngine;

namespace ArcaneDecorFlowCraft.Plumbing;

internal sealed class PipePiece:MonoBehaviour,Hoverable
{
    internal static readonly HashSet<PipePiece> Active=new HashSet<PipePiece>();
    public PipeKind Kind;
    internal WallPipeAdapter WallAdapter;
    internal readonly PipeSocketJoint[] Joints=new PipeSocketJoint[2];
    internal void ShowJoint(int port,Vector3 other,Vector3 direction){
        if(!Joints[port]){var go=new GameObject("seated socket "+port);go.transform.SetParent(transform,false);Joints[port]=go.AddComponent<PipeSocketJoint>();}
        Joints[port].Fit(Port(port),Direction(port),other,direction,PipePrefabs.DiameterScale(Kind));
    }
    internal ZNetView View;
    internal bool Live=>View&&View.IsValid();
    internal readonly float[] Flow=new float[2];
    internal readonly bool[] Connected=new bool[2];
    internal bool Supplied,Running;internal int Pumps;
    readonly PipeOutlet[] outlets=new PipeOutlet[2];
    public Color PowerColor=Color.red;
    Light glow;PipePowerGlow halo;MeshRenderer powerRenderer;readonly MaterialPropertyBlock powerProperties=new MaterialPropertyBlock();
    internal PipePowerGlow PowerHalo=>halo;
    internal Light PowerLight=>glow;
    void Awake(){View=GetComponent<ZNetView>();}
    void OnEnable(){Active.Add(this);PipeNetwork.Invalidate();}
    void OnDisable(){Active.Remove(this);PipeNetwork.Invalidate();Flow[0]=Flow[1]=0;Connected[0]=Connected[1]=false;Running=Supplied=false;if(glow)glow.enabled=false;if(halo)halo.gameObject.SetActive(false);for(int i=0;i<outlets.Length;i++)if(outlets[i])outlets[i].gameObject.SetActive(false);}
    internal static int PortCount(PipeKind kind)=>PipePrefabs.BaseKind(kind)==PipeKind.Intake?1:2;
    internal static Vector3 LocalPort(PipeKind kind,int port){
        if(PipePrefabs.Dragon(kind))return port==0?Vector3.zero:new Vector3(0,0,.96354f)*PipePrefabs.DiameterScale(kind);
        switch(PipePrefabs.BaseKind(kind)){
            case PipeKind.Straight:return Vector3.forward*(port==0?-1:1);
            case PipeKind.Straight4:return Vector3.forward*(port==0?-2:2);
            case PipeKind.Powered:return Vector3.forward*(port==0?-.5f:.5f);
            case PipeKind.Powered2:return Vector3.forward*(port==0?-1:1);
            case PipeKind.Powered4:return Vector3.forward*(port==0?-2:2);
            case PipeKind.LongElbow:return port==0?Vector3.zero:new Vector3(0,-1.3839f,1.48198f);
            case PipeKind.Elbow:return port==0?Vector3.zero:new Vector3(0,-.69195f,.74099f);
            default:return Vector3.zero;
        }
    }
    internal static Vector3 LocalDirection(PipeKind kind,int port)=>PipePrefabs.Dragon(kind)?(port==0?Vector3.back:new Vector3(0,-.15f,1).normalized):PipePrefabs.BaseKind(kind)==PipeKind.Intake?Vector3.up:PipePrefabs.Elbow(kind)&&port==1?Vector3.down:port==0?Vector3.back:Vector3.forward;
    internal Vector3 Port(int i)=>transform.TransformPoint(LocalPort(Kind,i));
    internal Vector3 Direction(int i)=>transform.TransformDirection(LocalDirection(Kind,i));
    internal double Source(){
        if(PipePrefabs.BaseKind(Kind)!=PipeKind.Intake)return double.NaN;
        // The strainer extends to -2.099 m. Sample its openings, not just one point
        // above its bottom: shallow water and partially buried shore intakes still work.
        float radius=.5f*PipePrefabs.DiameterScale(Kind);
        for(int level=0;level<5;level++){
            float y=-1.15f-level*.22f;
            for(int side=0;side<5;side++){
                var local=new Vector3(side==1?radius:side==2?-radius:0,y,side==3?radius:side==4?-radius:0);
                var probe=transform.TransformPoint(local);
                // Never draw from the ocean plane under dry land.
                if(PipeEnvironment.SampleGround(probe,out float ground)&&ground>probe.y+.01f)continue;
                float water=Floating.GetLiquidLevel(probe,.1f,LiquidType.Water);
                if(water>=probe.y+.025f)return water;
            }
        }
        return double.NaN;
    }
    internal void Apply(PipeFlowModel.Node result){
        Flow[0]=Flow[1]=0;Supplied=result.Supplied;Running=result.Running;Pumps=result.ConnectedPumps;
        foreach(var outlet in result.Outlets)Flow[outlet.Port]=outlet.Flow;
        UpdateVisuals();
    }
    void Update()=>UpdateVisuals();
    void UpdateVisuals(){
        if(!Live||SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return;
        bool nearby=Player.m_localPlayer&&(Player.m_localPlayer.transform.position-transform.position).sqrMagnitude<80*80;
        for(int i=0;i<PortCount(Kind);i++){
            bool on=nearby&&Flow[i]>.001f;
            if(on&&!outlets[i]){var go=new GameObject("pipe_outlet_"+i);go.transform.SetParent(transform,false);outlets[i]=go.AddComponent<PipeOutlet>();outlets[i].Pipe=this;outlets[i].PortIndex=i;}
            if(outlets[i])outlets[i].gameObject.SetActive(on);
        }
        UpdatePowerIndicator(nearby);
    }
    internal void UpdatePowerIndicator(bool nearby){
        if(!PipePrefabs.Powered(Kind))return;
        if(!powerRenderer)powerRenderer=transform.Find("plumbing_visual")?.GetComponent<MeshRenderer>();
        bool on=nearby&&Running;
        if(powerRenderer){powerRenderer.GetPropertyBlock(powerProperties,1);powerProperties.SetColor("_Color",PowerColor*(on?.8f:.16f));powerProperties.SetColor("_EmissionColor",on?PowerColor*1.8f:Color.black);powerRenderer.SetPropertyBlock(powerProperties,1);}
        if(on&&!glow){var go=new GameObject("arcane_power_light");go.transform.SetParent(transform,false);go.transform.localPosition=new Vector3(.38f,0,0)*PipePrefabs.DiameterScale(Kind);glow=go.AddComponent<Light>();glow.type=LightType.Point;glow.range=1.6f*PipePrefabs.DiameterScale(Kind);glow.shadows=LightShadows.None;glow.intensity=1.1f;}
        if(on&&!halo){var go=new GameObject("arcane_power_halo");go.transform.SetParent(transform,false);go.transform.localPosition=new Vector3(.36f,0,0)*PipePrefabs.DiameterScale(Kind);go.transform.localScale=Vector3.one*PipePrefabs.DiameterScale(Kind);halo=go.AddComponent<PipePowerGlow>();}
        if(glow){glow.color=PowerColor;glow.enabled=on;}
        if(halo){halo.gameObject.SetActive(on);if(on)halo.SetColor(PowerColor);}
    }

    public string GetHoverName()=>PipePrefabs.Names[(int)Kind];
    public float GetHoverOffset()=>0;
    public string GetHoverText(){
        float flow=Flow[0]+Flow[1];
        string status=!Supplied?"No water supply — immerse a connected intake.":flow>0?"Outlet: "+flow.ToString("0.##")+" gal/sec": "Water supplied";
        return GetHoverName()+"\n"+status+"\n"+Pumps+" powered pipe(s): "+(Pumps*5)+" gal/sec pumping capacity";
    }
}
