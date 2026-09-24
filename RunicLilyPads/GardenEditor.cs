using UnityEngine;
using HarmonyLib;
namespace RunicLilyPads
{
    public sealed class GardenEditor : MonoBehaviour
    {
        internal static GardenPiece Target;
        static GardenSettings draft;
        static bool oldVisible;static CursorLockMode oldLock;
        static PondMenu menu;
        static AudioSource preview;static float previewUntil;
        internal static bool IsOpen=>Target;
        internal static void Open(GardenPiece piece){FeatureEditor.Close();GardenTreeEditor.Close();Close();Target=piece;draft=piece.Settings.Copy();draft.CopyGroupFrom(PondNetwork.GroupSettings(piece));oldVisible=Cursor.visible;oldLock=Cursor.lockState;KeepCursor();menu=new PondMenu(piece,draft,Save,Close);}
        internal static void Close(){menu?.Dispose();menu=null;if(preview)preview.Stop();if(Target){Cursor.visible=oldVisible;Cursor.lockState=oldLock;}Target=null;draft=null;}
        internal static void KeepCursor(){if(IsOpen){Cursor.visible=true;Cursor.lockState=CursorLockMode.None;}}
        void Update(){if(preview&&Time.unscaledTime>=previewUntil)preview.Stop();if(!IsOpen)return;if(!Target.CanEdit()||UnityEngine.Input.GetKeyDown(KeyCode.Escape))Close();else KeepCursor();}
        void LateUpdate()=>KeepCursor();
        void OnDisable()=>Close();
        static void Save(){if(Target&&Target.Save(draft))Close();else Player.m_localPlayer?.Message(MessageHud.MessageType.Center,global::Runic.Localization.RunicText.Get("text_55c069c39dad"));}
        internal static void TestSound(GardenSettings settings)
        {
            if(settings.Track==0){Player.m_localPlayer?.Message(MessageHud.MessageType.Center,global::Runic.Localization.RunicText.Get("text_4f7f167be4f3"));return;}
            if(!preview){preview=new GameObject("WaterGardens sound preview").AddComponent<AudioSource>();Object.DontDestroyOnLoad(preview.gameObject);preview.playOnAwake=false;preview.spatialBlend=0;}
            preview.Stop();preview.clip=GardenAudio.Get(settings.Track);preview.volume=settings.Volume*Plugin.MasterVolume.Value;preview.pitch=settings.Speed;preview.loop=false;
            previewUntil=Time.unscaledTime+6;preview.Play();
        }
        void OnDestroy(){if(preview)Object.Destroy(preview.gameObject);preview=null;}
        static float Snap(float value,float step)=>Mathf.Round(value/step)*step;
    }
    [HarmonyPatch(typeof(GameCamera),"UpdateMouseCapture")]
    static class GardenCursor{static void Postfix(){GardenEditor.KeepCursor();FeatureEditor.KeepCursor();GardenTreeEditor.KeepCursor();}}
    [HarmonyPatch(typeof(ZInput),"GetKeyDown",typeof(KeyCode),typeof(bool))]
    static class GardenEscape{static void Postfix(KeyCode key,ref bool __result){if(key==KeyCode.Escape&&(GardenEditor.IsOpen||FeatureEditor.IsOpen||GardenTreeEditor.IsOpen))__result=false;}}
}

