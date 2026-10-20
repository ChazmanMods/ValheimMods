using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Jotunn.Managers;
namespace ArcaneWaterWorks.Pools
{
    public sealed class PondMenu:IDisposable
    {
        readonly GameObject canvas;readonly RectTransform panel;readonly TMP_FontAsset font;
        static readonly Color Ink=new Color(.82f,.79f,.66f),ButtonColor=new Color(.22f,.27f,.28f,1);
        public PondMenu(float height){            var sign=PrefabManager.Instance.GetPrefab("sign")?.GetComponent<Sign>();font=sign?sign.m_textWidget.font:TMP_Settings.defaultFontAsset;
            foreach(var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())if(f.name.IndexOf("Norse",StringComparison.OrdinalIgnoreCase)>=0&&f.name.IndexOf("Bold",StringComparison.OrdinalIgnoreCase)<0){font=f;break;}
            canvas=new GameObject("ArcaneNaturalWaters pool menu",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            canvas.AddComponent<GardenMenuCursor>();
            var c=canvas.GetComponent<Canvas>();c.renderMode=RenderMode.ScreenSpaceOverlay;c.sortingOrder=1000;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;scaler.matchWidthOrHeight=1;
            panel=Rect(canvas.transform,16,16,580,height);Box(panel,new Color(.045f,.05f,.045f,1));
        }
        internal PondMenu(GardenPiece target,GardenSettings draft,Action save,Action cancel):this(780){
            Heading("RIVER AMBIENCE · 20 m");
            float y=100;
            Range("River volume",0,1,draft.WaterfallVolume,v=>draft.WaterfallVolume=v,()=>Mathf.RoundToInt(draft.WaterfallVolume*100)+"%",ref y);
            Range("River sound distance",5,100,draft.WaterfallRange,v=>draft.WaterfallRange=v,()=>draft.WaterfallRange.ToString("0")+" m",ref y);
            Range("Ambience",0,7,draft.Track,v=>draft.Track=Mathf.RoundToInt(v),()=>Track(draft.Track),ref y);
            Range("Ambience volume",0,1,draft.Volume,v=>draft.Volume=v,()=>Mathf.RoundToInt(draft.Volume*100)+"%",ref y);
            Range("Playback speed",.5f,1.5f,draft.Speed,v=>draft.Speed=v,()=>draft.Speed.ToString("0.00")+"x",ref y);
            Range("Audible distance",5,40,draft.Range,v=>draft.Range=v,()=>draft.Range.ToString("0")+" m",ref y);
            Range("Fireflies",0,100,draft.Fireflies,v=>draft.Fireflies=Mathf.RoundToInt(v),()=>draft.Fireflies.ToString(),ref y);
            Option("Night only",()=>draft.NightOnly,v=>draft.NightOnly=v,ref y);
            Action("Preview ambience",y,()=>GardenEditor.TestSound(draft));
            Action("Save",670,save);Action("Cancel",720,cancel);
        }
        static string Track(int i)=>i==0?"Sound: Off":"Sound: night_ambience_"+i.ToString("000");
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
            TMP_Text text=null;var button=Button("",22,y,536,34,()=>{set(!get());text.text=caption+": "+(get()?"On":"Off");});text=button.GetComponentInChildren<TMP_Text>();text.text=caption+": "+(get()?"On":"Off");y+=42;
        }
        Button Button(string text,float x,float y,float w,float h,Action action){var r=Rect(panel,x,y,w,h);var image=Box(r,ButtonColor);var button=r.gameObject.AddComponent<Button>();button.targetGraphic=image;var colors=button.colors;colors.highlightedColor=new Color(1.2f,1.2f,1.2f);button.colors=colors;button.onClick.AddListener(()=>action());var label=Label(r,text,4,0,w-8,h,20);label.alignment=TextAlignmentOptions.Center;return button;}
        public TMP_Text Heading(string text,float y=18,int size=30,float height=40)=>Label(panel,text,22,y,536,height,size);
        public void Action(string text,float y,Action action)=>Button(text,22,y,536,38,action);
        public void Option(string text,Func<bool> get,Action<bool> set,ref float y)=>Toggle(text,get,set,ref y);
        public void Range(string text,float min,float max,float value,Action<float> set,Func<string> display,ref float y)=>Slider(text,text,min,max,value,set,display,ref y);
        TMP_Text Label(Transform parent,string text,float x,float y,float w,float h,int size){var r=Rect(parent,x,y,w,h);var label=r.gameObject.AddComponent<TextMeshProUGUI>();label.font=font;label.text=text;label.fontSize=size;label.color=Ink;label.raycastTarget=false;label.alignment=TextAlignmentOptions.MidlineLeft;label.textWrappingMode=TextWrappingModes.Normal;return label;}
        static RectTransform Rect(Transform parent,float x,float y,float w,float h){var go=new GameObject("Control",typeof(RectTransform));var r=go.GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r;}
        static Image Box(RectTransform rect,Color color){var image=rect.gameObject.AddComponent<Image>();image.color=color;return image;}
        public void Dispose(){if(canvas){canvas.SetActive(false);UnityEngine.Object.Destroy(canvas);}}
    }
}


