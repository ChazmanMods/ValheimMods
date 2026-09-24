using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RunicSentinel.Runtime
{
    internal sealed partial class SentinelAdminPanel
    {
        // Reuse the game's assets without adding a mandatory Jotunn dependency.
        // Atlas regions must be extracted: assigning sprite.texture would draw the entire atlas.
        private void ApplyVanillaMenuAssets()
        {
            object manager=null;
            Type managerType=Type.GetType("Jotunn.Managers.GUIManager, Jotunn",false);
            try{manager=managerType?.GetProperty("Instance",BindingFlags.Static|BindingFlags.Public|BindingFlags.FlattenHierarchy)?.GetValue(null);}catch{}
            var fonts=Resources.FindObjectsOfTypeAll<Font>();
            Font regular=fonts.FirstOrDefault(f=>f.name=="AveriaSerifLibre-Regular");
            if(regular!=null)
            {
                _skin.font=regular;
                foreach(var style in new[]{_title,_subtitle,_heading,_section,_label,_value,_statusStyle,_textArea,_textField,_button,_tabStyle,_selectedTabStyle,_skin.toggle})style.font=regular;
            }
            _title.font=regular;_title.fontSize=26;
            var sprites=Resources.FindObjectsOfTypeAll<Sprite>();
            Sprite Find(string name)
            {
                try
                {
                    var sprite=managerType?.GetMethod("GetSprite",new[]{typeof(string)})?.Invoke(manager,new object[]{name}) as Sprite;
                    if(sprite!=null)return sprite;
                }
                catch{}
                return sprites.FirstOrDefault(s=>s.name==name||s.name==name+"(Clone)");
            }
            var panel=Find("woodpanel_settings");var button=Find("button");var field=Find("text_field");
            ApplySprite(_windowStyle,panel);
            foreach(var style in new[]{_button,_tabStyle,_selectedTabStyle})ApplySprite(style,button);
            foreach(var style in new[]{_textField,_textArea})ApplySprite(style,field);
            // Clear inherited active-window backgrounds as well as the normal state.
            foreach(var style in new[]{_windowStyle,_contentStyle,_footerStyle,_row})SetMenuBackground(style,style.normal.background);
            _selectedTabStyle.normal.textColor=new Color(1f,0.72f,0.22f);
            _selectedTabStyle.fontStyle=FontStyle.Bold;
            var knob=Find("UISprite");
            if(knob!=null)
            {
                var texture=CopyMenuSprite(knob);
                if(texture!=null)
                {
                    SetMenuBackground(_skin.verticalScrollbarThumb,texture);
                    SetMenuBackground(_skin.horizontalScrollbarThumb,texture);
                }
            }
            Debug.Log("Runic Sentinel vanilla menu assets: panel="+(panel!=null)+", button="+(button!=null)+", input="+(field!=null)+", font="+(regular!=null));
        }
        private void ApplySprite(GUIStyle style,Sprite sprite)
        {
            if(sprite==null){SetMenuBackground(style,style.normal.background);return;}
            Texture2D texture=CopyMenuSprite(sprite);if(texture==null)return;
            SetMenuBackground(style,texture);
            Vector4 border=sprite.border;
            style.border=new RectOffset((int)border.x,(int)border.z,(int)border.w,(int)border.y);
        }
        private static void SetMenuBackground(GUIStyle style,Texture2D texture)
        {
            foreach(var state in new[]{style.normal,style.hover,style.active,style.focused,style.onNormal,style.onHover,style.onActive,style.onFocused})
            {state.background=texture;}
        }
        private Texture2D CopyMenuSprite(Sprite sprite)
        {
            RenderTexture temporary=null;var previous=RenderTexture.active;
            try
            {
                Rect r=sprite.textureRect;var source=sprite.texture;
                temporary=RenderTexture.GetTemporary((int)r.width,(int)r.height,0,RenderTextureFormat.ARGB32);
                Graphics.Blit(source,temporary,new Vector2(r.width/source.width,r.height/source.height),new Vector2(r.x/source.width,r.y/source.height));
                RenderTexture.active=temporary;
                var texture=new Texture2D((int)r.width,(int)r.height,TextureFormat.RGBA32,false){name="Sentinel-vanilla-"+sprite.name,hideFlags=HideFlags.HideAndDontSave};
                _ownedTextures.Add(texture);texture.ReadPixels(new Rect(0,0,r.width,r.height),0,0);texture.Apply();return texture;
            }
            catch(Exception e){Debug.LogWarning("Sentinel could not reuse menu sprite "+sprite.name+": "+e.GetType().Name);return null;}
            finally{RenderTexture.active=previous;if(temporary!=null)RenderTexture.ReleaseTemporary(temporary);}
        }
    }
}
