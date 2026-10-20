using System;
using System.Collections.Generic;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace ArcaneWaterWorks;

internal static class RiverHoe
{
    const string Key="arcanewaters_hoe";
    internal static Spring Selected;
    static int start=-1;
    internal static float StrokeLength=32;
    static int wheelFrame=-1;
    static readonly System.Reflection.MethodInfo TakeInput=AccessTools.Method(typeof(Player),"TakeInput");
    static readonly Dictionary<string,string> Modes=new Dictionary<string,string>();
    static readonly List<Piece> Pieces=new List<Piece>();
    internal static void Register(){
        Add("start","River Start","Choose the point on a selected river where the next trench starts. Use this to create a branch.");
        Add("trench","River Trench","Extend the selected river toward this point. Hold Alt and scroll to shrink or enlarge the trench reach (4-64 metres).");
        Add("deepen","Deepen River","Deepen the selected riverbed by 0.4 metres without lowering its water. Maximum 8 metres.");
        Add("shallow","Shallower River","Reduce the selected river's excavation depth by 0.4 metres.");
        Add("smooth","Smooth River Banks","Soften the selected river bank in an 8 metre brush around the aimed point. Use at merges and along steep banks; water level stays unchanged.");
        Add("unsmooth","Undo Bank Smoothing","Undo the latest smoothing stroke in the selected river section.");
        Add("undo","Undo River","Remove the most recently added segment from the selected river.");
        Add("configure","Configure River","Aim at a river branch to configure its width, depth, sound and whitewater.");
        Add("end","End River Branch","Aim at a branch endpoint to stop expansion of that branch only.");
        Add("reopen","Reopen River End","Aim at a manually ended branch to allow expansion again. Natural-water outlets remain connected.");
    }
    static void Add(string mode,string name,string description){
        string id="piece_arcanewaters_"+mode;
        var root=PrefabManager.Instance.CreateClonedPrefab(id,"mud_road");
        var piece=root.GetComponent<Piece>();piece.m_craftingStation=null;piece.m_resources=Array.Empty<Piece.Requirement>();
        piece.m_icon=HoeIcons.Create(mode,piece.m_icon);
        var config=new PieceConfig{Enabled=true,Name=name,Description=description,PieceTable="Hoe",Category="Landscaping"};
        if(!PieceManager.Instance.AddPiece(new CustomPiece(root,false,config)))throw new InvalidOperationException("Hoe tool registration failed: "+name);
        piece.m_enabled=false;Modes[id]=mode;Pieces.Add(piece);
    }
    internal static void Unlock(Player player){
        if(!player||player!=Player.m_localPlayer)return;
        if(!player.HaveUniqueKey(Key))player.AddUniqueKey(Key);
        foreach(var piece in Pieces){piece.m_enabled=true;AccessTools.Method(typeof(Player),"AddKnownPiece").Invoke(player,new object[]{piece});}
        AccessTools.Method(typeof(Player),"UpdateAvailablePiecesList").Invoke(player,null);
    }
    internal static void Select(Spring spring){
        Selected=spring;start=spring.Route?.EditableTip??-1;
        if(spring.CanEdit())Unlock(Player.m_localPlayer);
    }
    static bool Ready(){
        if(!Selected||!Selected.Live||Selected.Route==null){Say("Use E on a River Source and select it for hoe tools first.");return false;}
        if(Selected.Busy){Say("Wait for the previous river change to save.");return false;}
        if(!Selected.CanEdit()){Say("This river is too far away or crosses protected land.");return false;}
        return true;
    }
    static void Say(string text){if(Player.m_localPlayer)Player.m_localPlayer.Message(MessageHud.MessageType.Center,text);}
    internal static bool Extend(Vector3 world){
        if(!Ready())return false;
        var local=world-Selected.transform.position;var route=Selected.Route.Copy();int tip=start>=0&&start<route.Points.Count?start:route.Points.Count-1;
        var target=new Node(local.x,local.y,local.z);float distance=Node.Distance(route.Points[tip],target);
        if(float.IsNaN(distance)||float.IsInfinity(distance)){Say("Aim at valid terrain.");return false;}
        bool advancing=distance>StrokeLength;
        if(advancing){target=Node.Lerp(route.Points[tip],target,StrokeLength/distance);local=new Vector3(target.X,target.Y,target.Z);world=Selected.transform.position+local;}
        if(!route.IsOutlet(tip)&&!route.ClosedEnds.Contains(tip)&&(!route.IsTip(tip)||route.NeedsContinuation(target,tip)))return Continue(world,tip);
        if(!route.AppendFrom(tip,new Node(local.x,local.y,local.z),Selected.Ground,Selected.Sea,out string error,Selected.Receiving)){Say(error);return false;}
        if(!Selected.Save(route)){Say(Selected.Status);return false;}
        start=route.Points.Count-1;Say(route.Mouth?"This branch connected to natural water. Other branches remain editable.":advancing?"Trench advanced "+StrokeLength+" metres. Repeat to keep extending.":"Trench saved. Aim further along to continue.");return true;
    }
    static bool Continue(Vector3 world,int tip){
        var previous=Selected;var end=previous.Route.Points[tip];var origin=previous.transform.position+GardenBridge.V(end)+Vector3.up*.35f;
        var local=world-origin;var route=new Route{Width=previous.Route.Width,Depth=previous.Route.Depth};route.CopyAppearance(previous.Route);route.Points.Add(new Node(0,-.35f,0));
        float? Ground(float x,float z)=>RiverTerrain.SampleBase(origin+new Vector3(x,0,z),out var h)?h-origin.y:(float?)null;
        float sea=(ZoneSystem.instance?ZoneSystem.instance.m_waterLevel:30)-origin.y;
        float? Receiving(float x,float z){float h=ArcaneWaterWorks.Pools.RiverGardens.PondLevel(origin+new Vector3(x,0,z));return float.IsNegativeInfinity(h)?sea:h-origin.y;}
        if(!route.Append(new Node(local.x,local.y,local.z),Ground,sea,out string error,Receiving)){Say(error);return false;}
        // Validate before creating any network object. Each continuation has its own streamed anchor.
        for(int i=1;i<route.Points.Count;i++){var a=route.Points[route.Parent(i)];var b=route.Points[i];for(int j=0;j<=4;j++){
            var p=Node.Lerp(a,b,j/4f);if(!PrivateArea.CheckAccess(origin+GardenBridge.V(p),route.Width*.5f+route.BankPadding,false)){Say("The next section crosses protected land.");return false;}}}
        var prefab=PrefabManager.Instance.GetPrefab("piece_arcanewaterworks_spring");if(!prefab)return false;
        var go=UnityEngine.Object.Instantiate(prefab,origin,Quaternion.identity);var next=go.GetComponent<Spring>();var view=go.GetComponent<ZNetView>();
        if(!view||!view.IsValid()){UnityEngine.Object.Destroy(go);Say("Could not create the next section.");return false;}
        view.GetZDO().Set("aww_continuation",true);view.GetZDO().Set("aww_route_v1",route.Encode());
        next.View=view;Selected=next;start=route.Points.Count-1;Say("River extended into its next section. Keep trenching; there is no total length cap.");return true;
    }
    internal static bool Undo(){
        if(!Ready())return false;var route=Selected.Route.Copy();
        if(route.Points.Count<=2){Say("The spring needs at least one segment.");return false;}
        route.ClosedEnds.Remove(route.Points.Count-1);route.Points.RemoveAt(route.Points.Count-1);route.Mouth=false;
        if(!Selected.Save(route))return false;start=route.Points.Count-1;return true;
    }
    static bool Use(string mode,Vector3 position){
        if(mode=="start"||mode=="configure"||mode=="end"||mode=="reopen"){
            Spring nearest=null;float best=float.PositiveInfinity;
            foreach(var candidate in Spring.Active){if(!candidate||!candidate.Live||candidate.Route==null)continue;var p=position-candidate.transform.position;
                candidate.Route.Sample(p.x,p.z,out var distance,out _,out _);if(distance<=candidate.Route.Width*.5f+2&&distance<best){best=distance;nearest=candidate;}}
            if(nearest)Select(nearest);else{Say("Aim at a river branch.");return false;}
        }
        if(!Ready())return false;
        if(mode=="configure"){RiverEditor.Show(Selected);return true;}
        if(mode=="trench")return Extend(position);
        if(mode=="undo")return Undo();
        var route=Selected.Route.Copy();
        if(mode=="start"||mode=="end"||mode=="reopen"){
            var local=position-Selected.transform.position;float nearest=float.PositiveInfinity;int index=-1;
            for(int i=0;i<route.Points.Count;i++){float distance=Node.Distance(route.Points[i],new Node(local.x,0,local.z));if(distance<nearest){nearest=distance;index=i;}}
            if(nearest>route.Width*.5f+2){Say("Aim at the selected river to choose a branch point.");return false;}
            start=index;
            if(mode!="start"){
                if(!route.SetEnd(index,mode=="end")){Say("Aim at the endpoint of a branch, not its junction.");return false;}
                if(!Selected.Save(route))return false;
                Say(mode=="end"?"This branch is ended. Other branches remain editable.":route.IsOutlet(index)?"This branch connects to natural water. Select an upstream point to branch.":"This branch is open for expansion.");return true;
            }
            Say("Branch start selected. Use River Trench to dig away from this point.");return true;
        }
        if(mode=="smooth"||mode=="unsmooth"){
            var local=position-Selected.transform.position;
            if(mode=="smooth"&&!route.SmoothBank(local.x,local.z)){Say("Aim near this section's bank, move the brush at least 1 metre, or undo an older stroke (64 per section).");return false;}
            if(mode=="unsmooth"){if(route.BankStrokes.Count==0){Say("No bank smoothing to undo.");return false;}route.BankStrokes.RemoveAt(route.BankStrokes.Count-1);route.Invalidate();}
            if(!Selected.Save(route))return false;Say(mode=="smooth"?"Bank softened. Work along the join; select adjoining sections to soften both banks.":"Last bank smoothing stroke removed.");return true;
        }
        route.Depth=Mathf.Clamp(route.Depth+(mode=="deepen"?.4f:-.4f),.4f,8);
        if(!Selected.Save(route))return false;Say("Riverbed depth: "+route.Depth.ToString("0.0")+" m. Water level unchanged.");return true;
    }
    [HarmonyPatch(typeof(Player),"UpdateAvailablePiecesList")]
    static class Availability{
        static void Prefix(Player __instance){if(__instance!=Player.m_localPlayer)return;foreach(var p in Pieces)p.m_enabled=__instance.HaveUniqueKey(Key);}
    }
    internal static void Resize(float wheel){
        if(wheel==0)return;
        StrokeLength=Mathf.Clamp(StrokeLength+(wheel>0?4:-4),4,64);
    }
    static bool SizingActive(){
        var player=Player.m_localPlayer;
        if(!player||!player.InPlaceMode()||RiverEditor.Open||Hud.IsPieceSelectionVisible()||!(bool)TakeInput.Invoke(player,null))return false;
        var piece=player.GetSelectedPiece();
        return piece&&Modes.TryGetValue(piece.gameObject.name,out var mode)&&mode=="trench";
    }
    [HarmonyPatch(typeof(ZInput),nameof(ZInput.GetMouseScrollWheel))]
    static class SizeWheel{
        static void Postfix(ref float __result){
            if(!(UnityEngine.Input.GetKey(KeyCode.LeftAlt)||UnityEngine.Input.GetKey(KeyCode.RightAlt))||!SizingActive())return;
            // All wheel readers see zero, so sizing cannot also rotate or zoom.
            float delta=__result;__result=0;
            if(delta==0||wheelFrame==Time.frameCount)return;
            wheelFrame=Time.frameCount;Resize(delta);Say("River trench reach: "+StrokeLength+" m");
        }
    }
    [HarmonyPatch(typeof(Player),"UpdatePlacementGhost")]
    static class SizePreview{
        static void Postfix(Player __instance,GameObject ___m_placementGhost){
            if(__instance!=Player.m_localPlayer||!___m_placementGhost||!Selected||Selected.Route==null)return;
            if(!Modes.TryGetValue(___m_placementGhost.name,out var mode)||mode!="trench")return;
            int tip=start>=0&&start<Selected.Route.Points.Count?start:Selected.Route.EditableTip;
            if(tip<0)return;
            var preview=___m_placementGhost.GetComponent<RiverReachPreview>();
            if(!preview)preview=___m_placementGhost.AddComponent<RiverReachPreview>();
            preview.Show(Selected.transform.position+GardenBridge.V(Selected.Route.Points[tip]),StrokeLength);
        }
    }
    [HarmonyPatch(typeof(Player),"PlacePiece")]
    static class BuiltSpring{
        static void Postfix(Player __instance,Piece piece){if(piece&&piece.GetComponent<Spring>())Unlock(__instance);}
    }
    [HarmonyPatch(typeof(Player),"TryPlacePiece")]
    static class HoeStroke{
        static bool Prefix(Player __instance,Piece piece,ref bool __result){
            if(!piece||!Modes.TryGetValue(piece.gameObject.name,out string mode))return true;
            __result=false;
            if(__instance!=Player.m_localPlayer||!__instance.HaveUniqueKey(Key))return false;
            // Keep the native raycast, placement restrictions, range and ward checks.
            AccessTools.Method(typeof(Player),"UpdatePlacementGhost").Invoke(__instance,new object[]{true});
            var ghost=AccessTools.Field(typeof(Player),"m_placementGhost").GetValue(__instance) as GameObject;
            var placement=(Player.PlacementStatus)AccessTools.Field(typeof(Player),"m_placementStatus").GetValue(__instance);
            if(!ghost||!ghost.activeInHierarchy||placement!=Player.PlacementStatus.Valid){Say("Aim at valid, unprotected terrain within hoe reach.");return false;}
            __result=Use(mode,ghost.transform.position);return false;
        }
    }
}
