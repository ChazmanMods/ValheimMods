using System;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using Object=UnityEngine.Object;

namespace ArcaneDecorFlowCraft.Plumbing;

internal enum PipeKind { Straight, Elbow, Intake, Powered, Straight4, Powered2, StraightNarrow, ElbowNarrow, IntakeNarrow, PoweredNarrow, Straight4Narrow, Powered2Narrow, Dragon, DragonNarrow, LongElbow, LongElbowNarrow, Powered4, Powered4Narrow }
internal static class PipePrefabs
{
    internal const string CopperItem="FC_CopperPipe",Copper4Item="FC_CopperPipe4m",PoweredItem="FC_ArcanePipe",Powered2Item="FC_ArcanePipe2m",Powered4Item="FC_ArcanePipe4m";
    internal static readonly string[] Keys={"pipe","elbow","intake","powered","pipe4","powered2","pipe_narrow","elbow_narrow","intake_narrow","powered_narrow","pipe4_narrow","powered2_narrow","dragon","dragon_narrow","elbow_long","elbow_long_narrow","powered4","powered4_narrow"};
    internal static readonly string[] Names=MakeNames();
    static string[] MakeNames(){
        var names=new string[Keys.Length];var pieces=new[]{"Copper Pipe — 2 m","Copper Elbow — 90°","Copper Intake","Arcane Powered Pipe — 1 m","Copper Pipe — 4 m","Arcane Powered Pipe — 2 m"};
        for(int i=0;i<12;i++)names[i]=pieces[i%6]+(i<6?" (8 cm)":" (4 cm)");
        names[12]="Dragon Fountain — 8 cm";names[13]="Dragon Fountain — 4 cm";names[14]="Copper Long Elbow — 90° (8 cm)";names[15]="Copper Long Elbow — 90° (4 cm)";names[16]="Arcane Powered Pipe — 4 m (8 cm)";names[17]="Arcane Powered Pipe — 4 m (4 cm)";return names;
    }
    internal static bool Narrow(PipeKind kind)=>(int)kind>=6&&(int)kind<12||kind==PipeKind.DragonNarrow||kind==PipeKind.LongElbowNarrow||kind==PipeKind.Powered4Narrow;
    internal static PipeKind BaseKind(PipeKind kind)=>(int)kind>=6&&(int)kind<12?(PipeKind)((int)kind-6):kind==PipeKind.DragonNarrow?PipeKind.Dragon:kind==PipeKind.LongElbowNarrow?PipeKind.LongElbow:kind==PipeKind.Powered4Narrow?PipeKind.Powered4:kind;
    internal static bool Elbow(PipeKind kind)=>BaseKind(kind)==PipeKind.Elbow||BaseKind(kind)==PipeKind.LongElbow;
    internal static bool Dragon(PipeKind kind)=>BaseKind(kind)==PipeKind.Dragon;
    internal static float DiameterScale(PipeKind kind)=>Narrow(kind)?.5f:1;
    internal static bool Powered(PipeKind kind)=>BaseKind(kind)==PipeKind.Powered||BaseKind(kind)==PipeKind.Powered2||BaseKind(kind)==PipeKind.Powered4;
    internal static string ItemId(string wide,bool narrow)=>wide+(narrow?"4cm":"");
    internal static string Id(PipeKind kind)=>"piece_flowcraft_"+Keys[(int)kind];
    internal static void Register(){
        foreach(bool narrow in new[]{false,true}){
            string suffix=narrow?"_narrow":"",size=narrow?" (4 cm)":" (8 cm)";
            string copper=ItemId(CopperItem,narrow),copper4=ItemId(Copper4Item,narrow),powered=ItemId(PoweredItem,narrow),powered2=ItemId(Powered2Item,narrow);
            Item(copper,"Copper Pipe — 2 m"+size,"Copper pipe for crafting matching powered pipes.","pipe"+suffix,1,new[]{new RequirementConfig("Copper",1)});
            Item(copper4,"Copper Pipe — 4 m"+size,"Copper pipe for crafting matching powered pipes.","pipe4"+suffix,1,new[]{new RequirementConfig("Copper",2)});
            Item(powered,"Arcane Powered Pipe — 1 m"+size,"Adds 5 gallons/sec to a supplied line. Its red orb lights while water is running.","powered"+suffix,2,new[]{new RequirementConfig(copper,1),new RequirementConfig("SurtlingCore",1)});
            var batch=new RecipeConfig{Name="Recipe_FlowCraft_ArcanePipe_Batch4"+suffix,Item=powered,Amount=4,CraftingStation=null,Requirements=new[]{new RequirementConfig(copper4,1),new RequirementConfig("SurtlingCore",2)}};
            if(!ItemManager.Instance.AddRecipe(new CustomRecipe(batch)))throw new InvalidOperationException("Powered pipe batch recipe failed");
            Item(powered2,"Arcane Powered Pipe — 2 m"+size,"Adds 5 gallons/sec. Its red orb lights while water is running.","powered2"+suffix,2,new[]{new RequirementConfig(copper4,1),new RequirementConfig("SurtlingCore",2)});
            Item(ItemId(Powered4Item,narrow),"Arcane Powered Pipe — 4 m"+size,"Adds 5 gallons/sec. Its red orb glows while water is running.","powered4"+suffix,1,new[]{new RequirementConfig(copper4,1),new RequirementConfig("SurtlingCore",2)});
        }
        foreach(bool narrow in new[]{false,true})foreach(var kind in Catalogue(narrow))Build(kind);
    }
    internal static PipeKind[] Catalogue(bool narrow)=>narrow?
        new[]{PipeKind.ElbowNarrow,PipeKind.LongElbowNarrow,PipeKind.IntakeNarrow,PipeKind.StraightNarrow,PipeKind.Straight4Narrow,PipeKind.PoweredNarrow,PipeKind.Powered2Narrow,PipeKind.Powered4Narrow,PipeKind.DragonNarrow}:
        new[]{PipeKind.Elbow,PipeKind.LongElbow,PipeKind.Intake,PipeKind.Straight,PipeKind.Straight4,PipeKind.Powered,PipeKind.Powered2,PipeKind.Powered4,PipeKind.Dragon};
    static void Item(string id,string name,string description,string key,int amount,RequirementConfig[] requirements){
        var root=PrefabManager.Instance.CreateClonedPrefab(id,"Copper");
        var attach=root.transform.Find("attach");if(attach)Object.DestroyImmediate(attach.gameObject);
        var visual=PipeAssets.Visual(key,root.transform,.25f);visual.name="attach";visual.layer=LayerMask.NameToLayer("item");
        var drop=root.GetComponent<ItemDrop>();drop.m_itemData.m_shared.m_itemType=ItemDrop.ItemData.ItemType.Material;
        drop.m_itemData.m_shared.m_maxQuality=1;
        var config=new ItemConfig{Name=name,Description=description,Amount=amount,CraftingStation=null,StackSize=50,Weight=1,Icons=new[]{PipeAssets.Load(key).Icon},Requirements=requirements};
        if(!ItemManager.Instance.AddItem(new CustomItem(root,false,config)))throw new InvalidOperationException("Pipe item registration failed: "+id);
    }
    static void Build(PipeKind kind){
        string key=Keys[(int)kind];var basic=BaseKind(kind);float radius=.29f*DiameterScale(kind);var root=PrefabManager.Instance.CreateClonedPrefab(Id(kind),"wood_floor");
        for(int i=root.transform.childCount-1;i>=0;i--)Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
        foreach(var collider in root.GetComponents<Collider>())Object.DestroyImmediate(collider);
        var visual=PipeAssets.Visual(key,root.transform);var piece=root.GetComponent<Piece>();
        piece.m_usage=0;piece.m_craftingStation=null;piece.m_groundOnly=false;piece.m_groundPiece=false;piece.m_waterPiece=false;piece.m_noInWater=false;piece.m_clipEverything=false;piece.m_clipGround=false;piece.m_noClipping=false;
        piece.m_canBeRemoved=true;piece.m_icon=PipeAssets.Load(key).Icon;
        // Native metal integrity: transfer support through contacts and collapse when unsupported.
        var wear=root.GetComponent<WearNTear>();wear.m_new=wear.m_worn=wear.m_broken=visual;wear.m_wet=null;wear.m_snow=wear.m_snowWorn=wear.m_snowBroken=null;
        wear.m_autoCreateFragments=false;wear.m_fragmentRoots=Array.Empty<GameObject>();wear.m_nonSolidRenderers.Clear();wear.m_supports=true;wear.m_health=300;wear.m_burnable=false;wear.m_noSupportWear=true;wear.m_noRoofWear=false;wear.m_materialType=WearNTear.MaterialType.Iron;
        var plumbing=root.AddComponent<PipePiece>();plumbing.Kind=kind;
        for(int i=0;i<(Dragon(kind)?0:PipePiece.PortCount(kind));i++){
            var snap=new GameObject("Pipe connection "+(i+1)){tag="snappoint"};snap.transform.SetParent(root.transform,false);snap.transform.localPosition=PipePiece.LocalPort(kind,i);snap.transform.localRotation=Quaternion.LookRotation(PipePiece.LocalDirection(kind,i));
        }
        if(Elbow(kind)){
            float bend=basic==PipeKind.LongElbow?2:1;var previous=Vector3.zero;
            for(int section=1;section<=8;section++){float angle=section*Mathf.PI/16;var next=new Vector3(0,-.69195f+.69195f*Mathf.Cos(angle),.74099f*Mathf.Sin(angle))*bend;Capsule(root.transform,previous,next,radius*.93f);previous=next;}
        }
        else if(basic==PipeKind.Intake||Dragon(kind)){
            var collider=visual.AddComponent<BoxCollider>();var bounds=PipeAssets.Load(key).Mesh.bounds;
            if(Dragon(kind)){collider.center=new Vector3(0,bounds.center.y,.04f);collider.size=new Vector3(bounds.size.x,bounds.size.y,.1f);}
            else{collider.center=bounds.center;collider.size=bounds.size;}
        }
        else Capsule(root.transform,PipePiece.LocalPort(kind,0),PipePiece.LocalPort(kind,1),radius);
        root.GetComponent<ZNetView>().m_distant=false;
        string description=Dragon(kind)?"Hang on stone, marble or metal. Bring a matching pipe to the back of the supporting piece at mouth height; a flush straight or elbow connection is fitted automatically.":basic==PipeKind.Intake?"Immerse the strainer in a lake, river or hot spring. Connect pipes to the top opening.":Powered(kind)?"Adds pumping capacity to a supplied line and pumps uphill. Craft 4 x 1 m, 2 x 2 m or 1 x 4 m from 1 Copper Pipe (4 m) + 2 Surtling Cores.":"Snap the open ends to other pipes. Gravity feeds downhill; Arcane powered pipes lift water. Can be covered with terrain using the hoe.";
        description+=" During placement: "+Plugin.PipeTilt.Value+" tilts 45 degrees, "+Plugin.PipeRoll.Value+" rolls 90 degrees; mouse wheel turns. An unconnected end pours supplied water.";
        var config=new PieceConfig{Enabled=true,Name=Names[(int)kind],Description=description,PieceTable="Hammer",Category="ArcaneDecor: FlowCraft"};
        config.AddRequirement(basic==PipeKind.Powered?ItemId(PoweredItem,Narrow(kind)):basic==PipeKind.Powered2?ItemId(Powered2Item,Narrow(kind)):basic==PipeKind.Powered4?ItemId(Powered4Item,Narrow(kind)):"Copper",Dragon(kind)?(Narrow(kind)?1:2):basic==PipeKind.Straight4?2:1);
        if(!PieceManager.Instance.AddPiece(new CustomPiece(root,false,config)))throw new InvalidOperationException("Pipe registration failed: "+root.name);
    }
    static void Capsule(Transform parent,Vector3 a,Vector3 b,float radius){
        var go=new GameObject("pipe_collider"){layer=LayerMask.NameToLayer("piece")};go.transform.SetParent(parent,false);go.transform.localPosition=(a+b)*.5f;go.transform.localRotation=Quaternion.FromToRotation(Vector3.up,(b-a).normalized);
        var col=go.AddComponent<CapsuleCollider>();col.direction=1;col.radius=radius;col.height=Mathf.Max(radius*2,Vector3.Distance(a,b));
    }
}
