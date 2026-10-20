using System;
using Jotunn.Managers;
using UnityEngine;
namespace RunicLilyPads
{
    internal static class GardenFlora
    {
        internal static void RegisterAll(){
            Plant("grass_plains_gold",GardenLanguage.Token("text_e49cfd9c9343"),"instanced_heathgrass",1.5f,1.35f);
            Plant("grass_plains_green",GardenLanguage.Token("text_73f0b3c64655"),"grasscross_heath_green",1.3f,1.15f);
            // This shared meadow/forest grass is explicitly used by Black Forest clutter.
            Plant("grass_blackforest",GardenLanguage.Token("text_1392e24d496e"),"instanced_meadows_grass",1.5f,1.3f);
            Plant("bush_forest",GardenLanguage.Token("text_b51a7cb6d769"),"Bush01",1.7f,1.4f);
            Plant("bush_plains",GardenLanguage.Token("text_53d43f87f970"),"Bush01_heath",1.7f,1.3f);
            Plant("yggdrasil_001",GardenLanguage.Token("text_e78107033b90"),"YggaShoot1",0,10,true);
            Plant("yggdrasil_002",GardenLanguage.Token("text_35281087dcb1"),"YggaShoot2",0,12,true);
            Plant("yggdrasil_003",GardenLanguage.Token("text_d198b32ba85b"),"YggaShoot3",0,14,true);
        }
        static void Plant(string id,string name,string source,float width,float height,bool tree=false){
            try{
                var native=PrefabManager.Instance.GetPrefab(source);if(!native)throw new Exception("Missing native prefab "+source);
                var root=GardenPrefabs.Root("piece_arcanedecor_"+id,name,"");var visual=root.transform.Find("garden_visual");
                GardenPrefabs.AddVisual(native,visual,width,false,null,height,tree);
                if(!tree)visual.gameObject.AddComponent<PlantTouch>();else root.AddComponent<GardenTree>();
                root.AddComponent<GardenWind>().Strength=tree?.009f:.035f;
                GardenIcons.Assign(root,visual.gameObject,false,id.StartsWith("bush_")?.6f:1);
                GardenPrefabs.Register(root,name,tree?GardenLanguage.Token("text_b4db4e75bd08"):GardenLanguage.Token("text_649ff9e7a9f6"),tree?10:2);
            }catch(Exception e){Plugin.Log.LogError(name+": "+e);}
        }
    }
}


