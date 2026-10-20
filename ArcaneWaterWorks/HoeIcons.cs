using UnityEngine;
using Jotunn.Managers;

namespace ArcaneWaterWorks;

// Use the game's own terrain/tool art, with the restrained gold marks of native hoe actions.
internal static class HoeIcons
{
    const int Size=128;
    internal static Sprite Create(string mode,Sprite terrain){
        if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return terrain;
        string tool=mode=="trench"||mode=="deepen"?"PickaxeBronze":mode=="smooth"||mode=="unsmooth"||mode=="shallow"?"Hoe":null;
        var prefab=tool==null?null:PrefabManager.Instance.GetPrefab(tool);
        var item=prefab?prefab.GetComponent<ItemDrop>():null;
        var source=item?item.m_itemData.m_shared.m_icons[0]:terrain;
        var rect=source.textureRect;var previous=RenderTexture.active;
        var target=RenderTexture.GetTemporary(Size,Size,0,RenderTextureFormat.ARGB32);
        Graphics.Blit(source.texture,target,new Vector2(rect.width/source.texture.width,rect.height/source.texture.height),new Vector2(rect.x/source.texture.width,rect.y/source.texture.height));
        RenderTexture.active=target;var texture=new Texture2D(Size,Size,TextureFormat.RGBA32,false){name="native hoe "+mode,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        texture.ReadPixels(new Rect(0,0,Size,Size),0,0);RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
        var pixels=texture.GetPixels();
        void Line(Vector2 a,Vector2 b,float width,bool shadow){
            var delta=b-a;
            for(int y=0;y<Size;y++)for(int x=0;x<Size;x++){
                var point=new Vector2(x,y);float t=Mathf.Clamp01(Vector2.Dot(point-a,delta)/delta.sqrMagnitude);
                float alpha=Mathf.Clamp01(width+.5f-Vector2.Distance(point,a+delta*t));if(alpha<=0)continue;
                float grain=.92f+.08f*Mathf.Sin(x*1.7f+y*2.1f);
                var ink=shadow?new Color(.10f,.065f,.025f,.9f):new Color(.73f*grain,.59f*grain,.30f*grain,1);alpha*=ink.a;
                int at=y*Size+x;var old=pixels[at];float combined=alpha+old.a*(1-alpha);
                pixels[at]=new Color((ink.r*alpha+old.r*old.a*(1-alpha))/combined,(ink.g*alpha+old.g*old.a*(1-alpha))/combined,(ink.b*alpha+old.b*old.a*(1-alpha))/combined,combined);
            }
        }
        var strokes=new System.Collections.Generic.List<(Vector2 a,Vector2 b)>();
        void Stroke(Vector2 a,Vector2 b)=>strokes.Add((a,b));
        void Arrow(Vector2 a,Vector2 b){Stroke(a,b);var d=(b-a).normalized;var side=new Vector2(-d.y,d.x);Stroke(b,b-d*12+side*10);Stroke(b,b-d*12-side*10);}
        if(mode=="start"){
            Stroke(new Vector2(64,27),new Vector2(64,94));Stroke(new Vector2(64,94),new Vector2(91,84));Stroke(new Vector2(91,84),new Vector2(64,74));
        }else if(mode=="trench")Arrow(new Vector2(72,24),new Vector2(111,24));
        else if(mode=="deepen")Arrow(new Vector2(105,67),new Vector2(105,17));
        else if(mode=="shallow")Arrow(new Vector2(105,17),new Vector2(105,67));
        else if(mode=="undo"||mode=="unsmooth"){
            Vector2 centre=new Vector2(mode=="undo"?67:96,mode=="undo"?67:37);float radius=mode=="undo"?30:21;Vector2 last=centre+Vector2.right*radius;
            for(int i=1;i<=20;i++){float angle=i/20f*Mathf.PI*1.35f;var p=centre+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;if(i==20)Arrow(last,p);else Stroke(last,p);last=p;}
        }
        foreach(var stroke in strokes)Line(stroke.a+Vector2.down,stroke.b+Vector2.down,4,true);
        foreach(var stroke in strokes)Line(stroke.a,stroke.b,2,false);
        texture.SetPixels(pixels);texture.Apply(false,false);var icon=Sprite.Create(texture,new Rect(0,0,Size,Size),new Vector2(.5f,.5f));icon.name="hoe-"+mode;return icon;
    }
}
