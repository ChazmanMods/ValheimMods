using UnityEngine;

namespace RunicSentinel.Runtime
{
    internal sealed partial class SentinelAdminPanel
    {
        private string _hoverText="";
        private bool HoverButton(string caption,string help,GUIStyle style,params GUILayoutOption[] options)
        {
            bool clicked=GUILayout.Button(new GUIContent(caption,help),style,options);
            if(Event.current.type==EventType.Repaint&&GUI.tooltip==help&&GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))_hoverText=help;
            return clicked;
        }
        private bool SearchIconButton()
        {
            Rect r=GUILayoutUtility.GetRect(32,28,GUILayout.Width(32),GUILayout.Height(28));
            bool clicked=GUI.Button(r,GUIContent.none,_button);
            // Draw a magnifier independently of font glyph coverage.
            if(Event.current.type==EventType.Repaint)
            {
                var color=GUI.color;GUI.color=_label.normal.textColor;
                Vector2 center=new Vector2(r.x+13,r.y+11);
                for(int i=0;i<24;i++)
                {float angle=i*Mathf.PI/12;GUI.DrawTexture(new Rect(center.x+Mathf.Cos(angle)*6,center.y+Mathf.Sin(angle)*6,2,2),Texture2D.whiteTexture);}
                for(int i=0;i<7;i++)GUI.DrawTexture(new Rect(r.x+18+i,r.y+16+i,2,2),Texture2D.whiteTexture);
                GUI.color=color;
            }
            return clicked;
        }
    }
}
