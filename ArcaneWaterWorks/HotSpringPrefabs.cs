using System;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using ArcaneWaterWorks.Pools;
using UnityEngine;

namespace ArcaneWaterWorks;
internal static class HotSpringPrefabs
{
    internal static readonly string[] Shapes={"round","oval","kidney","wild"};
    internal static void Register(Sprite icon){
        for(int i=0;i<Shapes.Length;i++){
            var root=GardenPrefabs.Root("piece_arcanewaters_hotspring_"+Shapes[i],"Arcane Hot Spring","");
            var anchor=GardenPrefabs.AddVisual(GardenPrefabs.Rock,root.transform.Find("garden_visual"),.65f,true);
            anchor.name="control_stone";
            root.AddComponent<GardenPiece>().Shape=i;
            var piece=root.GetComponent<Piece>();piece.m_icon=Plugin.BuildIcon(piece,icon);piece.m_comfort=2;
            var native=PrefabManager.Instance.GetPrefab("piece_bathtub").GetComponent<Piece>();
            piece.m_comfortGroup=native.m_comfortGroup;piece.m_comfortObject=root;piece.m_noInWater=false;
            piece.m_craftingStation=null;piece.m_canBeRemoved=true;piece.m_name="Arcane Hot Spring - "+Shapes[i];
            root.AddComponent<HotSpring>();root.GetComponent<ZNetView>().m_distant=false;
            var config=new PieceConfig{Enabled=true,Name=piece.m_name,Description="A naturally heated, fuel-free pool with submerged white sitting stones. Use its control stone to adjust the pool, bubbling, music and ward.",PieceTable="Hammer",Category="Arcane Natural Waters"};
            config.AddRequirement("Stone",30);config.AddRequirement("SurtlingCore",2);config.AddRequirement("Crystal",5);
            if(!PieceManager.Instance.AddPiece(new CustomPiece(root,false,config)))throw new InvalidOperationException("Hot spring registration failed: "+root.name);
        }
    }
}
