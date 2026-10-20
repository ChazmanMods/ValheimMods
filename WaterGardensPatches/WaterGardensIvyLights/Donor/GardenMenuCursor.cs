using HarmonyLib;
using UnityEngine;

namespace RunicLilyPads
{
    public sealed class GardenMenuCursor : MonoBehaviour
    {
        static int openCount;
        public static bool IsOpen => openCount>0;
        void OnEnable(){openCount++;Canvas.willRenderCanvases+=Keep;Keep();}
        void OnDisable(){openCount=Mathf.Max(0,openCount-1);Canvas.willRenderCanvases-=Keep;}
        void LateUpdate()=>Keep();
        public static void Keep(){
            if(!IsOpen)return;
            ZCursor.LockState=CursorLockMode.None;
            ZCursor.Show();
            // Also keep the hardware pointer visible when the last input was a controller.
            Cursor.visible=true;
        }
    }
    [HarmonyPatch(typeof(GameCamera),"UpdateMouseCapture")]
    static class GardenMenuCapture
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix()=>GardenMenuCursor.Keep();
    }
}
