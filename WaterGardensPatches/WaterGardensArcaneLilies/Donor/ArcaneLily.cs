using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Jotunn.Managers;
using Jotunn.Entities;
using Jotunn.Configs;
namespace RunicLilyPads {
 // One shared mesh/material pair per stage. No texture/mesh copies per placed lily.
 public sealed class ArcaneLily:MonoBehaviour,Hoverable,Interactable {
  static ArcaneLily active;
  ZNetView view;Transform visual;MeshRenderer flower;Light lamp;MaterialPropertyBlock block;
  uint revision=uint.MaxValue;float nextCheck;bool pending;float deadline;
  float size=1,glow=1.5f,brightness=.8f,range=3;Color color=new Color(.7f,.45f,1);
  PondMenu menu;Canvas canvas;bool oldVisible,refreshing;CursorLockMode oldLock;
  Slider red,green,blue;TMP_InputField hex;TMP_Text swatch;
  internal static void Register(){
   string[] titles={"Arcane Lily Bud","Arcane Lily Bloom","Arcane Lily Full Bloom"};
   for(int stage=1;stage<=3;stage++){
    string asset="whitelilly_00"+stage;
    var source=PrefabManager.Instance.GetPrefab("piece_arcanedecor_lily_white_00"+stage);
    var root=PrefabManager.Instance.CreateClonedPrefab("piece_arcanedecor_lily_arcane_00"+stage,source);
    UnityEngine.Object.DestroyImmediate(root.GetComponent<GardenFlowerScale>());
    var moving=root.transform.Find("lily_visual/floating_lily");moving.localScale=Vector3.one*.5f;
    var model=moving.Find(asset);var data=GardenModels.Load(asset);
    // Separate pale petals from green foliage using the original texture at triangle UVs.
    // Work is done once during registration, never on interaction or during animation.
    var readable=new Texture2D(2,2);using(var s=typeof(Plugin).Assembly.GetManifestResourceStream("WaterGardens.Models."+asset+".jpg"))using(var b=new MemoryStream()){s.CopyTo(b);ImageConversion.LoadImage(readable,b.ToArray(),false);}
    var mesh=UnityEngine.Object.Instantiate(data.Mesh);var uv=mesh.uv;var triangles=mesh.triangles;var petals=new List<int>();var foliage=new List<int>();
    for(int i=0;i<triangles.Length;i+=3){var at=(uv[triangles[i]]+uv[triangles[i+1]]+uv[triangles[i+2]])/3;var c=readable.GetPixelBilinear(at.x,at.y);var list=c.r>.58f&&c.g>.48f&&c.b>.32f&&c.b>c.r*.48f?petals:foliage;list.Add(triangles[i]);list.Add(triangles[i+1]);list.Add(triangles[i+2]);}
    UnityEngine.Object.Destroy(readable);if(petals.Count==0)throw new InvalidDataException("No lily petals "+asset);
    mesh.subMeshCount=2;mesh.SetTriangles(foliage,0);mesh.SetTriangles(petals,1);model.GetComponent<MeshFilter>().sharedMesh=mesh;
    var tint=new Material(data.Material){name=asset+" arcane petals",enableInstancing=true};tint.SetTexture("_EmissiveTex",data.Material.mainTexture);tint.SetColor("_EmissionColor",Color.white);
    model.GetComponent<MeshRenderer>().sharedMaterials=new[]{data.Material,tint};
    root.AddComponent<ArcaneLily>();GardenIcons.Assign(root,root.transform.Find("lily_visual").gameObject,true);
    var config=new PieceConfig{Name=titles[stage-1],Description="A floating lily with its own color, glow, light and size. Press E to configure.",PieceTable="Hammer",Category="Water Garden"};config.AddRequirement("Wood",2);config.AddRequirement("Dandelion",1);
    if(!PieceManager.Instance.AddPiece(new CustomPiece(root,false,config)))throw new Exception(root.name);
   }
  }
  void Start(){
   view=GetComponent<ZNetView>();visual=transform.Find("lily_visual/floating_lily");
   foreach(var r in visual.GetComponentsInChildren<MeshRenderer>())if(r.name.StartsWith("whitelilly_")){flower=r;break;}
   block=new MaterialPropertyBlock();
   if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null){var go=new GameObject("Arcane lily light");go.transform.SetParent(flower.transform,false);go.transform.localPosition=flower.GetComponent<MeshFilter>().sharedMesh.bounds.center;lamp=go.AddComponent<Light>();lamp.type=LightType.Point;lamp.shadows=LightShadows.None;lamp.renderMode=LightRenderMode.Auto;}
   Load();Apply();nextCheck=Time.unscaledTime+Mathf.Abs(GetInstanceID()%251)/1000f;
  }
  static float Clean(float v,float min,float max,float fallback)=>float.IsNaN(v)||float.IsInfinity(v)?fallback:Mathf.Clamp(v,min,max);
  bool CanEdit()=>view&&view.IsValid()&&Player.m_localPlayer&&Vector3.Distance(Player.m_localPlayer.transform.position,transform.position)<8&&PrivateArea.CheckAccess(transform.position,0,false);
  void Load(){var z=view?view.GetZDO():null;if(z==null)return;size=Clean(z.GetFloat("wg_lily_size",1),.25f,2,1);glow=Clean(z.GetFloat("wg_lily_glow",1.5f),0,8,1.5f);brightness=Clean(z.GetFloat("wg_lily_light",.8f),0,3,.8f);range=Clean(z.GetFloat("wg_lily_range",3),1,6,3);color=new Color(Clean(z.GetFloat("wg_lily_r",.7f),0,1,.7f),Clean(z.GetFloat("wg_lily_g",.45f),0,1,.45f),Clean(z.GetFloat("wg_lily_b",1),0,1,1));revision=z.DataRevision;}
  void Apply(){if(!flower)return;visual.localScale=Vector3.one*(.5f*size);block.SetColor("_Color",color);block.SetColor("_EmissionColor",color*glow);flower.SetPropertyBlock(block,1);if(lamp){lamp.color=color;lamp.intensity=brightness;lamp.range=range;RefreshLight();}}
  void RefreshLight(){if(lamp)lamp.enabled=brightness>0&&Player.m_localPlayer&&(Player.m_localPlayer.transform.position-transform.position).sqrMagnitude<1600;}
  void Update(){
   if(menu!=null&&(!CanEdit()||Input.GetKeyDown(KeyCode.Escape)||GardenEditor.IsOpen||FeatureEditor.IsOpen||GardenTreeEditor.IsOpen))Cancel();
   if(pending){if(!CanEdit()||Time.unscaledTime>deadline){pending=false;Load();Apply();}else if(view.IsOwner()){Write();pending=false;}}
   if(Time.unscaledTime<nextCheck)return;nextCheck=Time.unscaledTime+.25f;
   if(view&&view.IsValid()&&menu==null&&!pending&&view.GetZDO().DataRevision!=revision){Load();Apply();}RefreshLight();
  }
  void Write(){var z=view.GetZDO();z.Set("wg_lily_size",size);z.Set("wg_lily_glow",glow);z.Set("wg_lily_light",brightness);z.Set("wg_lily_range",range);z.Set("wg_lily_r",color.r);z.Set("wg_lily_g",color.g);z.Set("wg_lily_b",color.b);revision=z.DataRevision;}
  void Save(){if(!CanEdit())return;pending=true;deadline=Time.unscaledTime+5;view.ClaimOwnership();if(view.IsOwner()){Write();pending=false;}Close();}
  void Cancel(){Close();Load();Apply();}
  void Close(){menu?.Dispose();menu=null;if(active==this){active=null;Cursor.visible=oldVisible;Cursor.lockState=oldLock;}canvas=null;hex=null;swatch=null;red=green=blue=null;}
  public string GetHoverName()=>GetComponent<Piece>().m_name;
  public string GetHoverText()=>GetHoverName()+"\n[<color=yellow><b>E</b></color>] Configure lily";
  public float GetHoverOffset()=>0;
  public bool UseItem(Humanoid user,ItemDrop.ItemData item)=>false;
  public bool Interact(Humanoid user,bool hold,bool alt){if(hold||user!=Player.m_localPlayer||!CanEdit()||pending||GardenMenuCursor.IsOpen)return false;Open();return true;}
  void Open(){if(active)active.Cancel();Load();oldVisible=Cursor.visible;oldLock=Cursor.lockState;active=this;
   menu=new PondMenu(740);canvas=menu.Heading(GetHoverName().ToUpperInvariant()).canvas;menu.Heading("Only this lily changes. Escape cancels.",64,18,28);float y=106;
   menu.Range("Lily size",.25f,2,size,SetSize,SizeText,ref y);
   menu.Range("Red",0,1,color.r,SetRed,RedText,ref y);red=LastSlider("Red");
   menu.Range("Green",0,1,color.g,SetGreen,GreenText,ref y);green=LastSlider("Green");
   menu.Range("Blue",0,1,color.b,SetBlue,BlueText,ref y);blue=LastSlider("Blue");
   swatch=menu.Heading("COLOR",y,20,30);y+=36;MakeHex(y);y+=44;
   menu.Range("Petal glow",0,8,glow,SetGlow,GlowText,ref y);
   menu.Range("Light brightness",0,3,brightness,SetLight,LightText,ref y);
   menu.Range("Light distance",1,6,range,SetRange,RangeText,ref y);
   menu.Heading("Set brightness to 0 to turn off the light.",y,18,28);y+=36;
   menu.Action("Save lily",y,Save);menu.Action("Cancel",y+46,Cancel);foreach(var slider in canvas.GetComponentsInChildren<Slider>()){slider.handleRect.sizeDelta=new Vector2(14,0);slider.fillRect.sizeDelta=Vector2.zero;}RefreshColor();
  }
  Slider LastSlider(string name){foreach(var s in canvas.GetComponentsInChildren<Slider>())if(s.name==name)return s;return null;}
  void MakeHex(float y){var label=menu.Heading("",y,20,32);var go=new GameObject("Hex color",typeof(RectTransform),typeof(Image),typeof(TMP_InputField));var rect=go.GetComponent<RectTransform>();rect.SetParent(label.transform.parent,false);rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(22,-y);rect.sizeDelta=new Vector2(536,32);go.GetComponent<Image>().color=new Color(.12f,.14f,.14f,1);label.transform.SetParent(rect,false);label.rectTransform.anchoredPosition=Vector2.zero;hex=go.GetComponent<TMP_InputField>();hex.textViewport=rect;hex.textComponent=label;hex.characterLimit=7;hex.onEndEdit.AddListener(SetHex);}
  void RefreshColor(){if(swatch){swatch.text="COLOR: #"+ColorUtility.ToHtmlStringRGB(color);swatch.color=color;}if(hex)hex.SetTextWithoutNotify("#"+ColorUtility.ToHtmlStringRGB(color));}
  void Tint(){Apply();RefreshColor();}
  void SetHex(string value){if(ColorUtility.TryParseHtmlString(value.StartsWith("#")?value:"#"+value,out var c)){color=new Color(c.r,c.g,c.b);refreshing=true;if(red)red.value=color.r;if(green)green.value=color.g;if(blue)blue.value=color.b;refreshing=false;Tint();}else RefreshColor();}
  void SetSize(float v){size=Clean(v,.25f,2,1);Apply();}void SetGlow(float v){glow=Clean(v,0,8,1.5f);Apply();}void SetLight(float v){brightness=Clean(v,0,3,.8f);Apply();}void SetRange(float v){range=Clean(v,1,6,3);Apply();}
  void SetRed(float v){color.r=v;if(!refreshing)Tint();}void SetGreen(float v){color.g=v;if(!refreshing)Tint();}void SetBlue(float v){color.b=v;if(!refreshing)Tint();}
  string SizeText()=>Mathf.RoundToInt(size*100)+"%";string RedText()=>Mathf.RoundToInt(color.r*255).ToString();string GreenText()=>Mathf.RoundToInt(color.g*255).ToString();string BlueText()=>Mathf.RoundToInt(color.b*255).ToString();string GlowText()=>glow.ToString("0.0");string LightText()=>brightness.ToString("0.0");string RangeText()=>range.ToString("0.0")+" m";
  void OnDisable(){if(menu!=null)Cancel();}void OnDestroy(){Close();}
 }
}
