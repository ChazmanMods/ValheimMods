using System;
using System.IO;
using BepInEx;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using Object = UnityEngine.Object;
namespace ArcaneDecorNorseFurnishings
{
 [BepInPlugin(Guid, "ArcaneDecor: NorseFurnishings", "0.1.1")]
 [BepInDependency(Jotunn.Main.ModGuid)]
 public sealed class Plugin : BaseUnityPlugin
 {
  public const string Guid="chazman.ArcaneDecorNorseFurnishings";
  public const string BedPrefab="piece_arcanedecor_norse_king_bed";
  void Awake()=>PrefabManager.OnVanillaPrefabsAvailable+=Register;
  void OnDestroy()=>PrefabManager.OnVanillaPrefabsAvailable-=Register;
  void Register()
  {
   PrefabManager.OnVanillaPrefabsAvailable-=Register;
   GameObject root=null;
   try
   {
    var source=PrefabManager.Instance.GetPrefab("piece_bed02");
    if(!source)throw new InvalidOperationException("Dragon Bed prefab is unavailable.");
    // Validate only at registration; no checks, scans or model I/O on player actions.
    string[] ingredients={"WolfPelt","LoxPelt","Frostwood","JuteRed","AmberPearl"};
    foreach(var id in ingredients)if(!PrefabManager.Instance.GetPrefab(id)?.GetComponent<ItemDrop>())throw new InvalidOperationException("Recipe item unavailable: "+id);
    var nativeBounds=VisualBounds(source);
    var target=nativeBounds.size*1.5f;
    var mesh=LoadMesh();var original=mesh.bounds;
    var scale=new Vector3(target.x/original.size.x,target.y/original.size.y,target.z/original.size.z);
    // Bake dimensions once. Instances share this mesh/material; root scale stays native.
    var vertices=mesh.vertices;var normals=mesh.normals;
    for(int i=0;i<vertices.Length;i++)
    {
     vertices[i]=Vector3.Scale(vertices[i],scale)+new Vector3(nativeBounds.center.x*1.5f,nativeBounds.min.y*1.5f,nativeBounds.center.z*1.5f);
     normals[i]=new Vector3(normals[i].x/scale.x,normals[i].y/scale.y,normals[i].z/scale.z).normalized;
    }
    mesh.vertices=vertices;mesh.normals=normals;mesh.RecalculateBounds();mesh.RecalculateTangents();
    var nativeMaterial=source.GetComponent<WearNTear>().m_new.GetComponentInChildren<MeshRenderer>(true).sharedMaterial;
    var material=new Material(nativeMaterial){name="Norse King Bed",mainTexture=LoadTexture("bed-texture.png")};
    // Imported albedo already contains deep shadows; lift detail without emissive glow.
    material.SetColor("_Color",new Color(1.65f,1.65f,1.65f,1f));
    if(material.HasProperty("_ValueNoise"))material.SetFloat("_ValueNoise",0);
    if(material.HasProperty("_ValueNoiseVertex"))material.SetFloat("_ValueNoiseVertex",0);
    material.DisableKeyword("_NORMALMAP");
    if(material.HasProperty("_BumpMap"))material.SetTexture("_BumpMap",null);
    if(material.HasProperty("_BumpScale"))material.SetFloat("_BumpScale",0);
    if(material.HasProperty("_Metallic"))material.SetFloat("_Metallic",0);
    if(material.HasProperty("_Glossiness"))material.SetFloat("_Glossiness",.15f);
    if(material.HasProperty("_EmissionColor"))material.SetColor("_EmissionColor",Color.black);
    root=PrefabManager.Instance.CreateClonedPrefab(BedPrefab,"piece_bed02");
    foreach(var lod in root.GetComponentsInChildren<LODGroup>(true))Object.DestroyImmediate(lod);
    foreach(var r in root.GetComponentsInChildren<Renderer>(true))Object.DestroyImmediate(r);
    foreach(var f in root.GetComponent<WearNTear>().m_new.GetComponentsInChildren<MeshFilter>(true))Object.DestroyImmediate(f);
    foreach(var c in root.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
    // Keep native attach/spawn transforms, scaled alongside the larger furniture.
    var bed=root.GetComponent<Bed>();bed.m_spawnPoint.localPosition*=1.5f;bed.m_hoverOffset*=1.5f;
    // The imported headboard is at +Z, opposite the native bed animation orientation.
    bed.m_spawnPoint.localRotation*=Quaternion.Euler(0,180,0);
    var visual=new GameObject("norse_king_bed_visual"){layer=root.layer};visual.transform.SetParent(root.transform,false);
    visual.AddComponent<MeshFilter>().sharedMesh=mesh;visual.AddComponent<MeshRenderer>().sharedMaterial=material;
    var b=mesh.bounds;
    var spawn=bed.m_spawnPoint.localPosition;spawn.y=b.min.y+b.size.y*.450499f;bed.m_spawnPoint.localPosition=spawn;
    AddBox(visual,new Vector3(b.center.x,b.min.y+b.size.y*.22f,b.center.z),new Vector3(b.size.x,b.size.y*.44f,b.size.z));
    // Cheap box colliders instead of a 40k-triangle physics mesh.
    AddBox(visual,new Vector3(b.center.x,b.center.y,b.max.z-b.size.z*.04f),new Vector3(b.size.x,b.size.y,b.size.z*.08f));
    var wear=root.GetComponent<WearNTear>();wear.m_new=wear.m_worn=wear.m_broken=visual;
    wear.m_wet=null;wear.m_snow=wear.m_snowWorn=wear.m_snowBroken=null;
    wear.m_autoCreateFragments=false;wear.m_fragmentRoots=Array.Empty<GameObject>();wear.m_nonSolidRenderers.Clear();
    var icon=LoadTexture("bed-icon.png");root.GetComponent<Piece>().m_icon=Sprite.Create(icon,new Rect(0,0,icon.width,icon.height),new Vector2(.5f,.5f));
    var config=new PieceConfig{Enabled=true,Name="Norse King Bed",Description="A grand Norse bed carved for a king. Offers the comfort and sleeping conditions of a Dragon Bed.",PieceTable="Hammer",Category="Furniture",CraftingStation="piece_workbench"};
    int[] amounts={2,2,3,2,1};
    var custom=new CustomPiece(root,false,config);
    var requirements=new Piece.Requirement[ingredients.Length];
    for(int i=0;i<ingredients.Length;i++)requirements[i]=new Piece.Requirement{m_resItem=PrefabManager.Instance.GetPrefab(ingredients[i]).GetComponent<ItemDrop>(),m_amount=amounts[i],m_recover=true};
    root.GetComponent<Piece>().m_resources=requirements;
    if(!PieceManager.Instance.AddPiece(custom))throw new InvalidOperationException("Norse King Bed registration rejected.");
    Logger.LogInfo($"Registered Norse King Bed: Dragon Bed {nativeBounds.size}, king bed {mesh.bounds.size}; recipe 2 WolfPelt, 2 LoxPelt, 3 Frostwood (Timberwood), 2 JuteRed, 1 AmberPearl.");
   }
   catch(Exception e){Logger.LogError("Norse King Bed registration failed: "+e);if(root)Object.DestroyImmediate(root);}
  }
  static void AddBox(GameObject go,Vector3 center,Vector3 size){var c=go.AddComponent<BoxCollider>();c.center=center;c.size=size;}
  static Bounds VisualBounds(GameObject root)
  {
   var bounds=new Bounds();bool first=true;
   foreach(var f in root.GetComponent<WearNTear>().m_new.GetComponentsInChildren<MeshFilter>(true))
   {
    if(!f.sharedMesh)continue;
    var matrix=root.transform.worldToLocalMatrix*f.transform.localToWorldMatrix;var b=f.sharedMesh.bounds;
    for(int i=0;i<8;i++)
    {
     var p=matrix.MultiplyPoint3x4(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));
     if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);
    }
   }
   if(first||bounds.size.x<=0||bounds.size.y<=0||bounds.size.z<=0)throw new InvalidDataException("Invalid Dragon Bed bounds.");return bounds;
  }
  static Stream Open(string name)=>typeof(Plugin).Assembly.GetManifestResourceStream("NorseFurnishings."+name)??throw new FileNotFoundException(name);
  static Texture2D LoadTexture(string name)
  {
   using(var stream=Open(name))using(var buffer=new MemoryStream())
   {stream.CopyTo(buffer);var t=new Texture2D(2,2){name=name};if(!ImageConversion.LoadImage(t,buffer.ToArray(),true))throw new InvalidDataException(name);return t;}
  }
  static Mesh LoadMesh()
  {
   using(var r=new BinaryReader(Open("bed.meshbin")))
   {
    if(r.ReadInt32()!=0x57474D31)throw new InvalidDataException("Bed mesh header");int count=r.ReadInt32(),indexCount=r.ReadInt32();
    var v=new Vector3[count];var n=new Vector3[count];var uv=new Vector2[count];var indices=new int[indexCount];
    for(int i=0;i<count;i++){v[i]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());n[i]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());uv[i]=new Vector2(r.ReadSingle(),r.ReadSingle());}
    for(int i=0;i<indexCount;i++)indices[i]=r.ReadInt32();
    var mesh=new Mesh{name="Norse King Bed",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32,vertices=v,normals=n,uv=uv,triangles=indices};mesh.RecalculateBounds();return mesh;
   }
  }
 }
}

