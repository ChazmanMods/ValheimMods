using UnityEngine;
namespace RunicLilyPads {
 internal static class HangingIvy {
  internal static void Register(){Build(false);Build(true);}
  static void Build(bool free){
   string name=free?"Golden Ivy - Hanging":"Golden Ivy - Clinging";
   var root=GardenPrefabs.Root(free?"piece_arcanedecor_ivy_gold_hanging":"piece_arcanedecor_ivy_gold_clinging",name,"");
   var piece=root.GetComponent<Piece>();piece.m_groundOnly=false;
   var visual=root.transform.Find("garden_visual");
   var model=GardenModels.Model("hanging_ivy",visual,2,false);
   // Top is the attachment point; flatten only the wall-facing variant.
   model.transform.localScale=new Vector3(2,2,free?2:.65f);
   if(!free)model.transform.localPosition=new Vector3(0,0,-.08f);
   var data=GardenModels.Load("hanging_ivy");
   if(data.Material.shader.name!="Custom/Vegetation"){
    GardenWind.ConfigureImported(data.Material);
    data.Material.SetFloat("_Height",-1);
    data.Material.SetFloat("_SwayDistance",.035f);
    data.Material.SetFloat("_RippleDistance",.008f);
    data.Material.SetColor("_EmissionColor",new Color(2.2f,1.8f,.6f));
   }
   piece.m_icon=data.Icon;
   root.AddComponent<GardenTree>();
   GardenPrefabs.Register(root,name,"Attach to a tree, wall or overhang. Press E to resize this ivy. Yellow bulbs glow in the dark.",2);
  }
  internal static string MenuText(string text,GardenTree target){
   if(!target.name.StartsWith("piece_arcanedecor_ivy_gold_"))return text;
   if(text.Contains("YGGDRASIL"))return "CONFIGURE GOLDEN IVY";
   if(text.Contains("Only this tree"))return "Only this ivy changes. Escape cancels.";
   if(text=="Tree size")return "Ivy size";
   if(text.StartsWith("Live preview."))return "Live preview. The top attachment stays fixed.";
   if(text=="Save tree")return "Save ivy";
   return text;
  }
 }
}
