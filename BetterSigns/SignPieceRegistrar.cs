using System;
using System.Collections.Generic;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace BetterSigns
{
    internal static class SignPieceRegistrar
    {
        private sealed class Variant
        {
            internal string Id, Name, Description;
            internal Vector3 Scale;
            internal bool Tileable, Transparent;
            internal int FineWood = 1;
        }

        private static bool _registered;
        private static readonly Variant[] Variants =
        {
            new Variant { Id="box_label", Name="Box Label", Description="A compact label for chests, crates, and shelves.", Scale=new Vector3(.52f,.38f,.5f) },
            new Variant { Id="door_plaque", Name="Door Plaque", Description="A tidy nameplate for doors and rooms.", Scale=new Vector3(.78f,.42f,.5f), FineWood=2 },
            new Variant { Id="mailbox", Name="Mailbox Sign", Description="A small address and mail marker.", Scale=new Vector3(.58f,.72f,.5f), FineWood=2 },
            new Variant { Id="signpost", Name="Signpost Arrow", Description="A long directional sign for roads and crossings.", Scale=new Vector3(1.35f,.42f,.5f), FineWood=2 },
            new Variant { Id="hanging", Name="Hanging Shop Sign", Description="A broad sign for inns, shops, and guild halls.", Scale=new Vector3(1.15f,1.25f,.5f), FineWood=3 },
            new Variant { Id="glass", Name="Glass Sign", Description="A transparent panel for windows and refined builds.", Scale=new Vector3(1.15f,.8f,.35f), FineWood=1, Transparent=true },
            new Variant { Id="panel", Name="Modular Sign Panel", Description="A seamless panel that snaps into larger signboards.", Scale=new Vector3(1f,1f,.35f), FineWood=2, Tileable=true },
            new Variant { Id="panel_wide", Name="Modular Wide Sign Panel", Description="A two-wide seamless panel for large-format lettering.", Scale=new Vector3(2f,1f,.35f), FineWood=4, Tileable=true }
        };

        internal static void Register()
        {
            if (_registered) return;
            _registered = true;
            PrefabManager.OnVanillaPrefabsAvailable -= Register;
            foreach (Variant variant in Variants) RegisterVariant(variant);
            RuntimeSelfTest.Run();
            Plugin.Log.LogInfo($"Registered {Variants.Length} Better Signs pieces.");
        }

        private static void RegisterVariant(Variant variant)
        {
            GameObject prefab = PrefabManager.Instance.CreateClonedPrefab("piece_bettersigns_" + variant.Id, "sign");
            if (prefab == null) { Plugin.Log.LogError("Vanilla sign prefab was not found."); return; }
            prefab.transform.localScale = variant.Scale;
            Piece piece = prefab.GetComponent<Piece>();
            if (piece != null) piece.m_name = variant.Name;
            if (variant.Tileable) AddPanelSnapPoints(prefab);
            if (variant.Transparent) MakeTransparent(prefab);
            var config = new PieceConfig
            {
                Name = variant.Name,
                Description = variant.Description,
                Category = "Better Signs",
                PieceTable = "Hammer",
                CraftingStation = CraftingStations.Workbench
            };
            config.AddRequirement("FineWood", variant.FineWood);
            if (variant.Transparent) config.AddRequirement("Crystal", 1);
            PieceManager.Instance.AddPiece(new CustomPiece(prefab, false, config));
        }

        private static void AddPanelSnapPoints(GameObject prefab)
        {
            foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
                if (child != prefab.transform && child.name.StartsWith("_snappoint", StringComparison.OrdinalIgnoreCase))
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            Vector3[] points = { new Vector3(-.5f,0,0), new Vector3(.5f,0,0), new Vector3(0,-.5f,0), new Vector3(0,.5f,0) };
            for (int i = 0; i < points.Length; i++)
            {
                var snap = new GameObject("_snappoint_panel_" + i);
                snap.transform.SetParent(prefab.transform, false);
                snap.transform.localPosition = points[i];
            }
        }

        private static void MakeTransparent(GameObject prefab)
        {
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                Material material = new Material(renderer.sharedMaterial);
                Color color = material.color; color.a = .18f; material.color = color;
                material.SetFloat("_Mode", 3f);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON"); material.EnableKeyword("_ALPHABLEND_ON");
                material.renderQueue = 3000;
                renderer.sharedMaterial = material;
            }
        }
    }
}
