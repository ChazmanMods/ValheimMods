using UnityEngine;
namespace RunicLilyPads
{
    public sealed class FeatureEditor:MonoBehaviour
    {
        internal static GardenFeature Target;static FeatureSettings draft;static bool visible;static CursorLockMode cursorLock;static PondMenu menu;
        internal static bool IsOpen=>Target;
        internal static void Open(GardenFeature target){GardenEditor.Close();GardenTreeEditor.Close();Close();Target=target;draft=target.Settings.Copy();visible=Cursor.visible;cursorLock=Cursor.lockState;KeepCursor();menu=new PondMenu(target,draft,Save,Close);}
        static void Save(){if(Target&&Target.Save(draft))Close();}
        internal static void Close(){menu?.Dispose();menu=null;if(Target){Cursor.visible=visible;Cursor.lockState=cursorLock;}Target=null;draft=null;}
        internal static void KeepCursor(){if(IsOpen){Cursor.visible=true;Cursor.lockState=CursorLockMode.None;}}
        void Update(){if(IsOpen){if(!Target.CanEdit()||UnityEngine.Input.GetKeyDown(KeyCode.Escape))Close();else KeepCursor();}}
        void LateUpdate()=>KeepCursor();void OnDisable()=>Close();
    }
}
