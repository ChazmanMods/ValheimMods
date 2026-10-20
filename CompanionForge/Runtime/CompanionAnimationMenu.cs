using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;

namespace CompanionForge
{
    // No scene search, asset load, or network request while browsing.
    public sealed class CompanionAnimationMenu : MonoBehaviour
    {
        private CompanionPose target;
        private CompanionPose lastAnimationTarget;
        private CompanionBed tubTarget;
        private readonly List<string> categories=new List<string>();
        private readonly List<MenuAnimation> visible=new List<MenuAnimation>();
        private Vector2 scroll;
        private int category, selected;
        private string[] categoryLabels;
        private GUIStyle title, row, hint;
        internal bool IsOpen { get; private set; }
        internal void Open(Player player)
        {
            tubTarget=CompanionBed.WithPlayerInTub(player);
            if (tubTarget!=null)
            {
                target=null; categories.Clear(); categories.Add("Together"); categoryLabels=categories.ToArray(); category=0;
                visible.Clear(); visible.Add(new MenuAnimation { id="shoulder_touch", label="Touch shoulder · hand on knee", category="Together" });
                selected=1; scroll=Vector2.zero; IsOpen=true; GUIManager.BlockInput(true); return;
            }
            target=lastAnimationTarget != null && lastAnimationTarget.Performing && lastAnimationTarget.CanBrowse(player)
                ? lastAnimationTarget : CompanionPose.Nearest(player);
            if (target==null) { player.Message(MessageHud.MessageType.Center,"Bring your following companion closer first"); return; }
            if (!target.EnsureMenu()) { player.Message(MessageHud.MessageType.Center,"This companion pack has no available animation menu"); target=null; return; }
            categories.Clear(); categories.Add("All");
            foreach (var item in target.Menu) if (!categories.Contains(item.category)) categories.Add(item.category);
            categoryLabels=categories.ToArray(); category=0; Filter();
            IsOpen=true; GUIManager.BlockInput(true);
        }
        internal void Close()
        {
            if (!IsOpen) return;
            IsOpen=false; target=null; tubTarget=null; GUIManager.BlockInput(false);
        }
        private void Filter()
        {
            visible.Clear();
            foreach (var item in target.Menu) if (category==0 || item.category==categories[category]) visible.Add(item);
            selected=0; scroll=Vector2.zero;
        }
        private void Update()
        {
            if (!IsOpen) return;
            var player=Player.m_localPlayer;
            if (tubTarget!=null)
            {
                if (player==null || player.IsDead() || !player.IsAttached() || !tubTarget.InHotTub) { Close(); return; }
            }
            else if (target==null || player==null || player.IsDead() || !target.CanBrowse(player)) { Close(); return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (Input.GetKeyDown(KeyCode.DownArrow)) Move(1);
            if (Input.GetKeyDown(KeyCode.UpArrow)) Move(-1);
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Play(selected);
        }
        private void Move(int direction)
        {
            selected=Mathf.Clamp(selected+direction,0,visible.Count);
            scroll.y=Mathf.Max(0,(selected-2)*42);
        }
        private void Play(int index)
        {
            if (tubTarget!=null && Player.m_localPlayer!=null)
            {
                if (index==0) CompanionTubTouch.Active?.Stop();
                else CompanionTubTouch.Begin(Player.m_localPlayer,tubTarget);
                Close(); return;
            }
            if (target==null || Player.m_localPlayer==null) { Close(); return; }
            string id=index==0 ? "" : visible[index-1].id;
            if (index>0) lastAnimationTarget=target;
            target.Select(Player.m_localPlayer,id); Close();
        }
        private void OnGUI()
        {
            if (!IsOpen || (target==null && tubTarget==null)) return;
            if (title==null)
            {
                title=new GUIStyle(GUI.skin.label) { fontSize=22,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter };
                row=new GUIStyle(GUI.skin.button) { fontSize=17,alignment=TextAnchor.MiddleLeft,padding=new RectOffset(14,12,7,7) };
                hint=new GUIStyle(GUI.skin.label) { fontSize=13,alignment=TextAnchor.MiddleCenter,wordWrap=true };
            }
            float width=Mathf.Min(560,Screen.width-32), height=Mathf.Min(690,Screen.height-40);
            var rect=new Rect((Screen.width-width)/2,(Screen.height-height)/2,width,height);
            GUI.Box(rect,GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x+14,rect.y+12,width-28,height-24));
            GUILayout.Label(tubTarget!=null ? "Together in the hot tub" : target.DisplayName+" · Animations",title,GUILayout.Height(35));
            int next=GUILayout.SelectionGrid(category,categoryLabels,3,GUILayout.Height(58));
            if (next!=category) { category=next; Filter(); }
            GUILayout.Space(8);
            var stopColor=GUI.backgroundColor;
            if (selected==0) GUI.backgroundColor=new Color(.55f,.85f,.65f,1);
            bool stop=GUILayout.Button(tubTarget!=null ? "Stop touch" : "Stand up / stop animation",row,GUILayout.Height(42));
            GUI.backgroundColor=stopColor;
            if (stop) { Play(0); GUILayout.EndArea(); return; }
            scroll=GUILayout.BeginScrollView(scroll,GUILayout.ExpandHeight(true));
            for (int i=1;i<=visible.Count;i++)
            {
                var original=GUI.backgroundColor;
                if (i==selected) GUI.backgroundColor=new Color(.55f,.85f,.65f,1);
                string label=visible[i-1].label;
                bool clicked=GUILayout.Button(label,row,GUILayout.Height(38));
                GUI.backgroundColor=original;
                if (clicked) { Play(i); break; }
            }
            GUILayout.EndScrollView();
            GUILayout.Label("Mouse wheel to browse · Click or Enter to play\nArrow keys to select · Escape to close",hint,GUILayout.Height(42));
            if (GUILayout.Button("Close",GUILayout.Height(30))) Close();
            GUILayout.EndArea();
        }
        private void OnDisable() { Close(); }
        private void OnDestroy() { Close(); }
    }
}
