using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
namespace RunicLilyPads {
 public sealed class IvyLights:MonoBehaviour,Hoverable,Interactable {
  static Mesh template;static Vector3[] original,centers;static int[] groups;static Material bulbs;
  static IvyLights active;static int[] fullLeaves,joinedLeaves;internal bool Joined;
  ZNetView view;Transform visual;Mesh mesh;MeshRenderer renderer;Vector3[] vertices;MaterialPropertyBlock block;
  uint revision=uint.MaxValue;bool pending;float deadline;PondMenu menu;bool oldVisible;CursorLockMode oldLock;
  float size=1,glow=2.2f;float[] scales=new float[3];Color color=new Color(1,.82f,.27f);int selected;
  Canvas menuCanvas;Slider groupSlider,red,green,blue;TMP_Text groupTitle,swatch;TMP_InputField hex;bool refreshing;
  internal static void Prepare(){
   if(template)return;
   var data=GardenModels.Load("hanging_ivy");template=Instantiate(data.Mesh);original=template.vertices;
   using(var r=new BinaryReader(typeof(IvyLights).Assembly.GetManifestResourceStream("WaterGardens.Models.hanging_ivy.bulbs"))){
    int n=r.ReadInt32();if(n!=original.Length)throw new InvalidDataException("Ivy bulb geometry mismatch");groups=new int[n];centers=new Vector3[n];
    for(int i=0;i<n;i++){groups[i]=r.ReadInt32();centers[i]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());}
   }
   var leaves=new List<int>();var lights=new List<int>();var indices=template.triangles;
   for(int i=0;i<indices.Length;i+=3){var list=groups[indices[i]]<0?leaves:lights;list.Add(indices[i]);list.Add(indices[i+1]);list.Add(indices[i+2]);}
   fullLeaves=leaves.ToArray();var trimmed=new List<int>();for(int i=0;i<fullLeaves.Length;i+=3){if(original[fullLeaves[i]].y>-.13f||original[fullLeaves[i+1]].y>-.13f||original[fullLeaves[i+2]].y>-.13f)continue;trimmed.Add(fullLeaves[i]);trimmed.Add(fullLeaves[i+1]);trimmed.Add(fullLeaves[i+2]);}joinedLeaves=trimmed.ToArray();
   template.subMeshCount=2;template.SetTriangles(leaves,0);template.SetTriangles(lights,1);
   bulbs=new Material(data.Material){name="Ivy bulb color"};bulbs.mainTexture=Texture2D.whiteTexture;bulbs.SetTexture("_EmissiveTex",Texture2D.whiteTexture);bulbs.SetColor("_Color",Color.white);bulbs.SetColor("_EmissionColor",Color.white);
   data.Material.SetColor("_EmissionColor",Color.black);
  }
  void Start(){
   for(int i=0;i<3;i++)scales[i]=1;view=GetComponent<ZNetView>();visual=transform.Find("garden_visual");renderer=visual.GetComponentInChildren<MeshRenderer>();
   mesh=Instantiate(template);vertices=(Vector3[])original.Clone();renderer.GetComponent<MeshFilter>().sharedMesh=mesh;renderer.sharedMaterials=new[]{GardenModels.Load("hanging_ivy").Material,bulbs};block=new MaterialPropertyBlock();Load();Apply();
  }
  internal bool CanEdit()=>view&&view.IsValid()&&Player.m_localPlayer&&Vector3.Distance(Player.m_localPlayer.transform.position,transform.position)<8&&PrivateArea.CheckAccess(transform.position,0,false);
  void Load(){var z=view?view.GetZDO():null;if(z==null)return;Joined=z.GetBool("wg_ivy_join",false);size=Clean(z.GetFloat("wg_tree_scale",1),.25f,2,1);glow=Clean(z.GetFloat("wg_ivy_glow",2.2f),0,8,2.2f);
   color=new Color(Clean(z.GetFloat("wg_ivy_r",1),0,1,1),Clean(z.GetFloat("wg_ivy_g",.82f),0,1,.82f),Clean(z.GetFloat("wg_ivy_b",.27f),0,1,.27f));
   for(int i=0;i<3;i++)scales[i]=Clean(z.GetFloat("wg_ivy_group"+i,1),1,3,1);revision=z.DataRevision;
  }
  static float Clean(float x,float min,float max,float fallback)=>float.IsNaN(x)||float.IsInfinity(x)?fallback:Mathf.Clamp(x,min,max);
  void Apply(){if(!mesh)return;visual.localScale=Vector3.one*size;
   for(int i=0;i<vertices.Length;i++)vertices[i]=groups[i]<0?original[i]:centers[i]+(original[i]-centers[i])*scales[groups[i]];
   mesh.vertices=vertices;mesh.SetTriangles(Joined?joinedLeaves:fullLeaves,0);mesh.RecalculateBounds();block.SetColor("_Color",color);block.SetColor("_EmissionColor",color*glow);renderer.SetPropertyBlock(block,1);
  }
  internal void PreviewChainSize(float value){if(visual&&size!=value){size=value;visual.localScale=Vector3.one*size;}}
  internal void SetJoined(bool value){if(Joined==value)return;Joined=value;if(mesh)mesh.SetTriangles(Joined?joinedLeaves:fullLeaves,0);}
  void Tint(){if(block!=null){block.SetColor("_Color",color);block.SetColor("_EmissionColor",color*glow);renderer.SetPropertyBlock(block,1);}RefreshColor();}
  void Update(){
   if(menu!=null&&(!CanEdit()||Input.GetKeyDown(KeyCode.Escape)||GardenEditor.IsOpen||FeatureEditor.IsOpen||GardenTreeEditor.IsOpen))Cancel();
   if(!view||!view.IsValid())return;
   if(pending){if(!CanEdit()||Time.unscaledTime>deadline){pending=false;Load();Apply();}else if(view.IsOwner()){Write();pending=false;}}
   if(menu==null&&!pending&&view.GetZDO().DataRevision!=revision){Load();Apply();}
  }
  void Write(){var z=view.GetZDO();z.Set("wg_tree_scale",size);z.Set("wg_ivy_glow",glow);z.Set("wg_ivy_r",color.r);z.Set("wg_ivy_g",color.g);z.Set("wg_ivy_b",color.b);for(int i=0;i<3;i++)z.Set("wg_ivy_group"+i,scales[i]);revision=z.DataRevision;}
  internal void Save(){if(!CanEdit())return;pending=true;deadline=Time.unscaledTime+5;view.ClaimOwnership();if(view.IsOwner()){Write();pending=false;}Close();}
  internal void Cancel(){Close();Load();Apply();}
  void Close(){menu?.Dispose();menu=null;if(active==this){active=null;Cursor.visible=oldVisible;Cursor.lockState=oldLock;}hex=null;swatch=null;menuCanvas=null;}
  public string GetHoverName()=>Localization.instance.Localize(GetComponent<Piece>().m_name);
  public string GetHoverText()=>GetHoverName()+"\n[<color=yellow><b>E</b></color>] Configure ivy lights";
  public float GetHoverOffset()=>0;
  public bool UseItem(Humanoid user,ItemDrop.ItemData item)=>false;
  public bool Interact(Humanoid user,bool hold,bool alt){if(hold||user!=Player.m_localPlayer||!CanEdit())return false;Open();return true;}
  internal void Open(){if(active)active.Cancel();GardenEditor.Close();FeatureEditor.Close();GardenTreeEditor.Close();Load();oldVisible=Cursor.visible;oldLock=Cursor.lockState;active=this;selected=0;
   menu=new PondMenu(870);menuCanvas=menu.Heading("CONFIGURE IVY LIGHTS").canvas;menu.Heading("Only this ivy changes. Escape cancels.",64,18,28);float y=106;
   menu.Range("Ivy size",.25f,2,size,SetSize,SizeText,ref y);
   menu.Range("Red",0,1,color.r,SetRed,RedText,ref y);red=LastSlider("Red");
   menu.Range("Green",0,1,color.g,SetGreen,GreenText,ref y);green=LastSlider("Green");
   menu.Range("Blue",0,1,color.b,SetBlue,BlueText,ref y);blue=LastSlider("Blue");
   swatch=menu.Heading("COLOR",y,20,30);y+=36;MakeHex(y);y+=42;
   menu.Range("Glow",0,8,glow,SetGlow,GlowText,ref y);
   groupTitle=menu.Heading("BULB GROUP 1 OF 3",y,22,30);y+=34;menu.Action("Next bulb group",y,NextGroup);y+=46;
   menu.Range("Bulb size",1,3,scales[selected],SetBulbs,BulbText,ref y);groupSlider=LastSlider("Bulb size");
   menu.Heading("Five bulbs per group. Vines stay the same size.",y,18,38);y+=46;
   menu.Action("Save ivy",y,Save);menu.Action("Cancel",y+46,Cancel);RefreshColor();
  }
  Slider LastSlider(string name){foreach(var s in menuCanvas.GetComponentsInChildren<Slider>())if(s.name==name)return s;return null;}
  void MakeHex(float y){var label=menu.Heading("",y,20,32);var r=new GameObject("Hex color",typeof(RectTransform),typeof(Image),typeof(TMP_InputField));var rect=r.GetComponent<RectTransform>();rect.SetParent(label.transform.parent,false);rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(22,-y);rect.sizeDelta=new Vector2(536,32);r.GetComponent<Image>().color=new Color(.12f,.14f,.14f,1);label.transform.SetParent(rect,false);label.rectTransform.anchoredPosition=Vector2.zero;hex=r.GetComponent<TMP_InputField>();hex.textViewport=rect;hex.textComponent=label;hex.characterLimit=7;hex.onEndEdit.AddListener(SetHex);}
  void RefreshColor(){if(swatch){swatch.text="COLOR: #"+ColorUtility.ToHtmlStringRGB(color);swatch.color=color;}if(hex)hex.SetTextWithoutNotify("#"+ColorUtility.ToHtmlStringRGB(color));}
  void SetHex(string value){if(ColorUtility.TryParseHtmlString(value.StartsWith("#")?value:"#"+value,out var c)){color=new Color(c.r,c.g,c.b);refreshing=true;red.value=color.r;green.value=color.g;blue.value=color.b;refreshing=false;Tint();}else RefreshColor();}
  void NextGroup(){selected=(selected+1)%3;groupTitle.text="BULB GROUP "+(selected+1)+" OF 3";groupSlider.value=scales[selected];}
  void SetSize(float v){size=v;if(visual)visual.localScale=Vector3.one*size;}
  void SetRed(float v){color.r=v;if(!refreshing)Tint();}void SetGreen(float v){color.g=v;if(!refreshing)Tint();}void SetBlue(float v){color.b=v;if(!refreshing)Tint();}
  void SetGlow(float v){glow=v;Tint();}void SetBulbs(float v){scales[selected]=v;Apply();}
  string SizeText()=>Mathf.RoundToInt(size*100)+"%";string RedText()=>Mathf.RoundToInt(color.r*255).ToString();string GreenText()=>Mathf.RoundToInt(color.g*255).ToString();string BlueText()=>Mathf.RoundToInt(color.b*255).ToString();string GlowText()=>glow.ToString("0.0");string BulbText()=>Mathf.RoundToInt(scales[selected]*100)+"%";
  void OnDestroy(){Close();if(mesh)Destroy(mesh);}
 }
}

