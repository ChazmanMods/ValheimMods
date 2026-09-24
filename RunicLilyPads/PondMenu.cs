using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Jotunn.Managers;
namespace RunicLilyPads
{
    internal sealed class PondMenu:IDisposable
    {
        readonly GameObject canvas;readonly RectTransform panel;readonly TMP_FontAsset font;
        static readonly Color Ink=new Color(.82f,.79f,.66f),ButtonColor=new Color(.22f,.27f,.28f,1);
        PondMenu(float height){            var sign=PrefabManager.Instance.GetPrefab("sign")?.GetComponent<Sign>();font=sign?sign.m_textWidget.font:TMP_Settings.defaultFontAsset;
            foreach(var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())if(f.name.IndexOf("Norse",StringComparison.OrdinalIgnoreCase)>=0&&f.name.IndexOf("Bold",StringComparison.OrdinalIgnoreCase)<0){font=f;break;}
            canvas=new GameObject("WaterGardens pond menu",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            var c=canvas.GetComponent<Canvas>();c.renderMode=RenderMode.ScreenSpaceOverlay;c.sortingOrder=1000;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;scaler.matchWidthOrHeight=1;
            panel=Rect(canvas.transform,16,16,580,height);Box(panel,new Color(.045f,.05f,.045f,1));
        }
        internal PondMenu(GardenFeature target,FeatureSettings draft,Action save,Action cancel):this(620){
            canvas.name="WaterGardens feature menu";
            Label(panel,target.GetHoverName().ToUpperInvariant(),22,18,536,38,30);
            Label(panel,global::Runic.Localization.RunicText.Get("text_fb2784e0597c"),22,64,536,28,18);
            float y=106;
            if(target.HasGlobe||!target.Fountain){
                Slider("Light brightness",global::Runic.Localization.RunicText.Get("text_d449ff1d6933"),0,5,draft.Light,v=>draft.Light=v,()=>draft.Light.ToString("0.0"),ref y);
                Slider("Light distance",global::Runic.Localization.RunicText.Get("text_cccfd1c62a25"),2,12,draft.Range,v=>draft.Range=v,()=>draft.Range.ToString("0.0")+" m",ref y);
                Slider("Glow",global::Runic.Localization.RunicText.Get("text_f534ed976dc8"),0,8,draft.Glow,v=>draft.Glow=v,()=>draft.Glow.ToString("0.0"),ref y);
            }else{Label(panel,global::Runic.Localization.RunicText.Get("text_345fd40849b3"),22,y,536,32,18);y+=40;}
            if(target.Fountain){
                y+=10;
                Slider("Fountain sound volume",global::Runic.Localization.RunicText.Get("text_a77ae5ec6a5d"),0,1,draft.Volume,v=>draft.Volume=v,()=>Mathf.RoundToInt(draft.Volume*100)+"%",ref y);
                Toggle(global::Runic.Localization.RunicText.Get("text_7ba071a12eed"),()=>draft.Flow,v=>draft.Flow=v,ref y);
                Label(panel,global::Runic.Localization.RunicText.Get("text_961f92fa5ee5"),22,y,536,48,18);
            }
            Label(panel,global::Runic.Localization.RunicText.Get("text_a3a5b3d026d5"),22,482,536,48,18);
            Button(global::Runic.Localization.RunicText.Get("text_1509f561f241"),22,556,262,42,save);Button(global::Runic.Localization.RunicText.Get("text_19766ed6ccb2"),296,556,262,42,cancel);
        }
        internal PondMenu(GardenTree target,float initial,Action<float> change,Action save,Action cancel):this(330){
            canvas.name="WaterGardens tree menu";
            Label(panel,global::Runic.Localization.RunicText.Get("text_09b799d0042b"),22,18,536,38,30);
            Label(panel,global::Runic.Localization.RunicText.Get("text_74ef7988ca8c"),22,64,536,28,18);
            float value=initial,y=112;
            Slider("Tree size",global::Runic.Localization.RunicText.Get("text_d062e2a0a5fe"),GardenTree.MinSize,GardenTree.MaxSize,value,v=>{value=Round(v,.05f);change(value);},()=>Mathf.RoundToInt(value*100)+"%",ref y);
            Label(panel,global::Runic.Localization.RunicText.Get("text_afcffb913d6c"),22,192,536,48,18);
            Button(global::Runic.Localization.RunicText.Get("text_a497f8c5bf73"),22,266,262,42,save);Button(global::Runic.Localization.RunicText.Get("text_19766ed6ccb2"),296,266,262,42,cancel);
        }
        internal PondMenu(GardenPiece target,GardenSettings draft,Action save,Action cancel):this(1040){            Label(panel,target.Shape<0?global::Runic.Localization.RunicText.Get("text_a3a74fe11cae"):global::Runic.Localization.RunicText.Get("text_903f033ec09d"),22,18,536,38,30);
            Label(panel,global::Runic.Localization.RunicText.Get("text_fb2784e0597c"),22,64,536,28,18);
            float y=106;
            Slider("Pond width",target.Shape<0?global::Runic.Localization.RunicText.Get("text_523487a5de21"):global::Runic.Localization.RunicText.Get("text_3019947b6daf"),4,24,draft.Width,v=>draft.Width=Round(v,.25f),()=>draft.Width.ToString("0.0")+" m",ref y);
            if(target.Shape>=0){
                Slider("Pond depth",global::Runic.Localization.RunicText.Get("text_f1dbc33978a9"),.4f,3,draft.Depth,v=>draft.Depth=Round(v,.1f),()=>draft.Depth.ToString("0.0")+" m",ref y);
                Slider("Flowers and buds",global::Runic.Localization.RunicText.Get("text_ac6979f09ecf"),.15f,1.5f,draft.FlowerScale,v=>draft.FlowerScale=Round(v,.05f),()=>Mathf.RoundToInt(draft.FlowerScale*100)+"%",ref y);
                Label(panel,global::Runic.Localization.RunicText.Get("text_25b9e32a4fcb"),22,y,536,25,17);y+=30;
                Toggle(global::Runic.Localization.RunicText.Get("text_51148564b900"),()=>draft.FloatLanterns,v=>draft.FloatLanterns=v,ref y);
                Toggle(global::Runic.Localization.RunicText.Get("text_c86dc62b39c2"),()=>draft.FloatFountains,v=>draft.FloatFountains=v,ref y);
                Toggle(global::Runic.Localization.RunicText.Get("text_b9d1cbfcfe7b"),()=>draft.FloatStatues,v=>draft.FloatStatues=v,ref y);
                Slider("Waterfall volume",global::Runic.Localization.RunicText.Get("text_5b47bceaed7f"),0,1,draft.WaterfallVolume,v=>draft.WaterfallVolume=v,()=>Mathf.RoundToInt(draft.WaterfallVolume*100)+"%",ref y);
                Slider("Waterfall distance",global::Runic.Localization.RunicText.Get("text_7dd70deb027c"),5,100,draft.WaterfallRange,v=>draft.WaterfallRange=Mathf.Round(v),()=>draft.WaterfallRange.ToString("0")+" m",ref y);
            }
            y+=8;var track=Label(panel,Track(draft.Track),22,y,536,28,20);y+=32;
            Button(global::Runic.Localization.RunicText.Get("text_ea1256d4fffa"),22,y,262,36,()=>{draft.Track=(draft.Track+7)%8;track.text=Track(draft.Track);});
            Button(global::Runic.Localization.RunicText.Get("text_0f6eed1ba9bf"),296,y,262,36,()=>{draft.Track=(draft.Track+1)%8;track.text=Track(draft.Track);});y+=48;
            Slider("Loudness",global::Runic.Localization.RunicText.Get("text_3efdeb9e9b0d"),0,1,draft.Volume,v=>draft.Volume=v,()=>Mathf.RoundToInt(draft.Volume*100)+"%",ref y);
            Slider("Playback speed",global::Runic.Localization.RunicText.Get("text_afec2b22114c"),.5f,1.5f,draft.Speed,v=>draft.Speed=v,()=>draft.Speed.ToString("0.00")+"x",ref y);
            Slider("Audible distance",global::Runic.Localization.RunicText.Get("text_c1d4cd06afa3"),5,40,draft.Range,v=>draft.Range=Mathf.Round(v),()=>draft.Range.ToString("0")+" m",ref y);
            Slider("Fireflies",global::Runic.Localization.RunicText.Get("text_b553e42907ee"),0,100,draft.Fireflies,v=>draft.Fireflies=Mathf.RoundToInt(v),()=>draft.Fireflies.ToString(),ref y);
            Toggle(global::Runic.Localization.RunicText.Get("text_d75a748843a4"),()=>draft.NightOnly,v=>draft.NightOnly=v,ref y);
            Button(global::Runic.Localization.RunicText.Get("text_e03160d6d69a"),22,y,536,38,()=>GardenEditor.TestSound(draft));
            Label(panel,global::Runic.Localization.RunicText.Get("text_236947e11f0c"),22,927,536,40,17);
            Button(global::Runic.Localization.RunicText.Get("text_1b824b381983"),22,976,262,42,save);Button(global::Runic.Localization.RunicText.Get("text_19766ed6ccb2"),296,976,262,42,cancel);
        }
        static string Track(int i)=>i==0?global::Runic.Localization.RunicText.Get("text_d18c7d1b8fca"):global::Runic.Localization.RunicText.Get("text_1598c10defff")+i.ToString("000");
        static float Round(float v,float step)=>Mathf.Round(v/step)*step;
        void Slider(string name,string caption,float min,float max,float value,Action<float> set,Func<string> display,ref float y){
            Label(panel,caption,22,y,300,26,20);var number=Label(panel,display(),364,y,194,26,20);number.alignment=TextAlignmentOptions.MidlineRight;y+=26;
            var root=Rect(panel,22,y,536,22);root.name=name;var track=Rect(root,0,9,536,5);Box(track,new Color(.035f,.045f,.045f,1));
            var fillArea=Rect(root,0,9,536,5);var fill=Rect(fillArea,0,0,0,5);Box(fill,ButtonColor);
            var handleArea=Rect(root,7,0,522,22);var handle=Rect(handleArea,0,0,14,22);handle.pivot=new Vector2(.5f,.5f);handle.anchorMin=handle.anchorMax=new Vector2(0,.5f);var image=Box(handle,Ink);
            var slider=root.gameObject.AddComponent<Slider>();slider.direction=UnityEngine.UI.Slider.Direction.LeftToRight;slider.fillRect=fill;slider.handleRect=handle;slider.targetGraphic=image;slider.minValue=min;slider.maxValue=max;slider.SetValueWithoutNotify(value);
            slider.onValueChanged.AddListener(v=>{set(v);number.text=display();});y+=28;
        }
        void Toggle(string caption,Func<bool> get,Action<bool> set,ref float y){
            TMP_Text text=null;var button=Button("",22,y,536,34,()=>{set(!get());text.text=caption+": "+(get()?global::Runic.Localization.RunicText.Get("text_130011756125"):global::Runic.Localization.RunicText.Get("text_ca7981b46ecf"));});text=button.GetComponentInChildren<TMP_Text>();text.text=caption+": "+(get()?global::Runic.Localization.RunicText.Get("text_130011756125"):global::Runic.Localization.RunicText.Get("text_ca7981b46ecf"));y+=42;
        }
        Button Button(string text,float x,float y,float w,float h,Action action){var r=Rect(panel,x,y,w,h);var image=Box(r,ButtonColor);var button=r.gameObject.AddComponent<Button>();button.targetGraphic=image;var colors=button.colors;colors.highlightedColor=new Color(1.2f,1.2f,1.2f);button.colors=colors;button.onClick.AddListener(()=>action());var label=Label(r,text,4,0,w-8,h,20);label.alignment=TextAlignmentOptions.Center;return button;}
        TMP_Text Label(Transform parent,string text,float x,float y,float w,float h,int size){var r=Rect(parent,x,y,w,h);var label=r.gameObject.AddComponent<TextMeshProUGUI>();label.font=font;label.text=text;label.fontSize=size;label.color=Ink;label.raycastTarget=false;label.alignment=TextAlignmentOptions.MidlineLeft;label.textWrappingMode=TextWrappingModes.Normal;return label;}
        static RectTransform Rect(Transform parent,float x,float y,float w,float h){var go=new GameObject("Control",typeof(RectTransform));var r=go.GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r;}
        static Image Box(RectTransform rect,Color color){var image=rect.gameObject.AddComponent<Image>();image.color=color;return image;}
        public void Dispose(){if(canvas){canvas.SetActive(false);UnityEngine.Object.Destroy(canvas);}}
    }
}


