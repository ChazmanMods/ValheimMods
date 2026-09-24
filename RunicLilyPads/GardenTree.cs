using UnityEngine;
namespace RunicLilyPads
{
    public sealed class GardenTree:MonoBehaviour,Hoverable,Interactable
    {
        internal const float MinSize=.25f,MaxSize=2f;
        internal float Size{get;private set;}=1;
        ZNetView view;Transform visual;float pending,deadline;bool saving;
        void Start(){view=GetComponent<ZNetView>();visual=transform.Find("garden_visual");}
        internal bool CanEdit()=>view&&view.IsValid()&&Player.m_localPlayer&&Vector3.Distance(Player.m_localPlayer.transform.position,transform.position)<8&&PrivateArea.CheckAccess(transform.position,0,false);
        internal bool Save(float size){if(!CanEdit()||float.IsNaN(size)||float.IsInfinity(size))return false;pending=Mathf.Clamp(size,MinSize,MaxSize);saving=true;deadline=Time.time+5;view.ClaimOwnership();return true;}
        internal void Preview(float size){var scale=Vector3.one*Mathf.Clamp(size,MinSize,MaxSize);if(visual&&visual.localScale!=scale)visual.localScale=scale;}
        void Update(){
            if(!view||!view.IsValid())return;
            if(saving){if(!CanEdit()||Time.time>deadline){saving=false;Player.m_localPlayer?.Message(MessageHud.MessageType.Center,global::Runic.Localization.RunicText.Get("text_c3734c3a2012"));}else if(view.IsOwner()){view.GetZDO().Set("wg_tree_scale",pending);saving=false;}}
            float loaded=view.GetZDO().GetFloat("wg_tree_scale",1);Size=float.IsNaN(loaded)||float.IsInfinity(loaded)?1:Mathf.Clamp(loaded,MinSize,MaxSize);
            if(GardenTreeEditor.Target!=this)Preview(saving?pending:Size);
        }
        public string GetHoverName()=>Localization.instance.Localize(GetComponent<Piece>().m_name);
        public string GetHoverText()=>GetHoverName()+global::Runic.Localization.RunicText.Get("text_bd2a9ffdc67a");
        public float GetHoverOffset()=>0;
        public bool Interact(Humanoid user,bool hold,bool alt){if(hold||user!=Player.m_localPlayer||!CanEdit())return false;GardenTreeEditor.Open(this);return true;}
        public bool UseItem(Humanoid user,ItemDrop.ItemData item)=>false;
        void OnDestroy(){if(GardenTreeEditor.Target==this)GardenTreeEditor.Close();}
    }
    public sealed class GardenTreeEditor:MonoBehaviour
    {
        internal static GardenTree Target;internal static bool IsOpen=>Target;
        static float draft;static bool visible;static CursorLockMode cursorLock;static PondMenu menu;
        internal static void Open(GardenTree tree){GardenEditor.Close();FeatureEditor.Close();Close();Target=tree;draft=tree.Size;visible=Cursor.visible;cursorLock=Cursor.lockState;KeepCursor();menu=new PondMenu(tree,draft,v=>{draft=v;if(Target)Target.Preview(v);},Save,Close);}
        static void Save(){if(Target&&Target.Save(draft))Close();}
        internal static void Close(){menu?.Dispose();menu=null;if(Target){Target.Preview(Target.Size);Cursor.visible=visible;Cursor.lockState=cursorLock;}Target=null;}
        internal static void KeepCursor(){if(IsOpen){Cursor.visible=true;Cursor.lockState=CursorLockMode.None;}}
        void Update(){if(IsOpen){if(!Target.CanEdit()||UnityEngine.Input.GetKeyDown(KeyCode.Escape))Close();else KeepCursor();}}
        void LateUpdate()=>KeepCursor();void OnDisable()=>Close();
    }
}
