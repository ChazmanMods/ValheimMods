using System;
using RunicCrafting;
using RunicCrafting.Integration;
using UnityEngine;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool value, string name)
    { if (!value) throw new Exception(name); _assertions++; }
    private static Piece Add(string name, float x = 0)
    {
        var go = new GameObject { name=name+"(Clone)" }; go.transform.position=new Vector3(x);
        var piece=go.Add(new Piece()); go.Add(new WearNTear()); go.Add(new ZNetView());
        Piece.Loaded.Add(piece); return piece;
    }
    private static int Sent(Piece piece) => piece.GetComponent<WearNTear>().Requests;
    private static void Reset()
    {
        AreaRepairRuntime.Reset(); Piece.Loaded.Clear(); Time.unscaledTime=10;
        EffectList.Sounds=0; EffectList.Fail=false;
        Player.m_localPlayer=new GameObject().Add(new Player()); ZNet.instance=new ZNet();
        Configuration.Enabled.Value=Configuration.AreaRepairEnabled.Value=true;
        Configuration.AreaRepairRadius.Value=50; Configuration.AreaRepairKey.Value.Down=true;
        Chat.instance=null; InventoryGui.Visible=false; Game.Paused=false;
        Cursor.lockState=CursorLockMode.Locked; PrivateArea.Allows=_=>true; CraftingStation.Nearby=null;
        ObjectDB.instance=new ObjectDB { Hammer=new GameObject() };
        var hammer=ObjectDB.instance.Hammer.Add(new ItemDrop());
        var prefab=new GameObject { name="woodwall" }; var wood=prefab.Add(new Piece());
        var sound=new GameObject { name="sfx_build_hammer_wood" }; sound.Add(new ZSFX());
        wood.m_placeEffect.m_effectPrefabs=new[] {
            new EffectList.EffectData { m_prefab=new GameObject { name="vfx_Place_wood_wall" } },
            new EffectList.EffectData { m_prefab=sound }
        };
        hammer.m_itemData.m_shared.m_buildPieces.m_pieces.Add(prefab);
    }
    private static int Main()
    {
        Reset(); var near=Add("woodwall",49); var far=Add("woodwall",51); var item=Add("IronSword",1);
        AreaRepairRuntime.Tick(); Check(Sent(near)==1 && Sent(far)==0 && Sent(item)==0,"radius and hammer-only filter");
        Check(EffectList.Sounds==1 && EffectList.Position.x==0,"wood sound audible at player, without placement VFX");
        Reset(); near=Add("woodwall"); near.GetComponent<WearNTear>().Accept=false; AreaRepairRuntime.Tick();
        Check(EffectList.Sounds==0,"rejected repairs are silent");
        Reset(); near=Add("woodwall"); EffectList.Fail=true; AreaRepairRuntime.Tick(); Check(Sent(near)==1,"sound failure does not undo repair");
        Reset(); Configuration.AreaRepairRadius.Value=100; near=Add("woodwall",99); AreaRepairRuntime.Tick(); Check(Sent(near)==1,"100m configurable radius");
        Reset(); near=Add("woodwall"); near.GetComponent<ZNetView>().Record.Health=100; AreaRepairRuntime.Tick(); Check(Sent(near)==0,"healthy ignored");
        Reset(); near=Add("woodwall"); PrivateArea.Allows=_=>false; AreaRepairRuntime.Tick(); Check(Sent(near)==0,"ward denied");
        Reset(); near=Add("woodwall"); near.gameObject.Add(new Container { Allowed=false }); AreaRepairRuntime.Tick(); Check(Sent(near)==0,"personal chest denied");
        Reset(); near=Add("woodwall"); near.GetComponent<ZNetView>().Owned=false; AreaRepairRuntime.Tick(); Check(Sent(near)==0,"unowned network object skipped");
        Reset(); near=Add("woodwall"); near.m_craftingStation=new CraftingStation(); AreaRepairRuntime.Tick(); Check(Sent(near)==0,"required station missing");
        Reset(); near=Add("woodwall"); near.m_craftingStation=new CraftingStation(); CraftingStation.Nearby=new CraftingStation { Allowed=false }; AreaRepairRuntime.Tick(); Check(Sent(near)==0,"station access denied");
        Reset(); near=Add("woodwall"); near.m_craftingStation=new CraftingStation(); CraftingStation.Nearby=new CraftingStation(); AreaRepairRuntime.Tick(); Check(Sent(near)==1,"station-covered structure repaired");
        Reset(); near=Add("woodwall"); Configuration.AreaRepairEnabled.Value=false; AreaRepairRuntime.Tick(); Check(Sent(near)==0,"disabled hotkey inert");
        Reset(); near=Add("woodwall"); Configuration.Enabled.Value=false; AreaRepairRuntime.Tick(); Check(Sent(near)==0,"master disable inert");
        Reset(); near=Add("woodwall"); Chat.instance=new Chat { Focus=true }; AreaRepairRuntime.Tick(); Check(Sent(near)==0,"chat typing inert");
        Reset(); near=Add("woodwall"); InventoryGui.Visible=true; AreaRepairRuntime.Tick(); Check(Sent(near)==0,"inventory UI inert");
        Reset(); near=Add("woodwall"); Cursor.lockState=CursorLockMode.None; AreaRepairRuntime.Tick(); Check(Sent(near)==0,"unlocked mod panel cursor inert");
        Reset(); near=Add("woodwall"); Player.m_localPlayer.Input=false; AreaRepairRuntime.Tick(); Check(Sent(near)==0,"native input gate honored");
        Reset(); for(int i=0;i<20;i++) Add("woodwall"); AreaRepairRuntime.Tick();
        Check(Total()==8,"one pulse limited to eight requests");
        Check(EffectList.Sounds==1,"eight repaired pieces share one sound");
        AreaRepairRuntime.Tick(); Check(Total()==8,"pulse time spacing");
        Configuration.AreaRepairEnabled.Value=false; Time.unscaledTime+=1; AreaRepairRuntime.Tick();
        Check(Total()==8,"disable cancels queued work");
        Configuration.AreaRepairEnabled.Value=true; AreaRepairRuntime.Tick(); Check(Total()==8,"re-enable cannot replay old batch");
        Reset(); for(int i=0;i<20;i++) Add("woodwall"); AreaRepairRuntime.Tick();
        Time.unscaledTime+=1; ZNet.instance=new ZNet(); AreaRepairRuntime.Tick(); Check(Total()==8,"network change cancels pending repairs");
        Reset(); for(int i=0;i<20;i++) Add("woodwall"); AreaRepairRuntime.Tick();
        Time.unscaledTime+=1; Player.m_localPlayer.transform.position=new Vector3(3); AreaRepairRuntime.Tick(); Check(Total()==8,"movement cancels pending reach");
        Reset(); for(int i=0;i<20;i++) Add("woodwall"); AreaRepairRuntime.Tick();
        Time.unscaledTime+=1; AreaRepairRuntime.Tick(); Time.unscaledTime+=1; AreaRepairRuntime.Tick();
        Check(Total()==20,"all eligible pieces finish across pulses");
        Check(EffectList.Sounds==3,"successful spaced pulses give progress feedback");
        System.Console.WriteLine(_assertions+" actual area-repair runtime checks passed (managed fixtures)."); return 0;
    }
    private static int Total() { int n=0; foreach(var p in Piece.Loaded) n+=Sent(p); return n; }
}
