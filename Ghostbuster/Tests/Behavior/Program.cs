using Ghostbuster;
using UnityEngine;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
void Test(string name, Action test)
{
    ZNet.instance = new ZNet();
    ZDOMan.instance = new ZDOMan();
    test();
    Console.WriteLine("PASS " + name);
    passed++;
}
void Move(ZDO zdo, Vector3 destination)
{
    PlayerSectorPatch.Prefix(zdo, destination, out bool state);
    zdo.InternalSetPosition(destination);
    PlayerSectorPatch.Postfix(zdo, state);
}
Peer AddPeer(long owner, float x, bool known = true)
{
    var peer = new Peer { Owner = owner, Position = new Vector3(x, 0, 0), Knows = known };
    ZDOMan.instance.Peers.Add(peer);
    return peer;
}

Test("Unpatched teleport leaves entrance observer stale", () => {
    var peer = AddPeer(20, 0);
    new ZDO().InternalSetPosition(new(1024, 0, 0));
    Check(peer.Knows && !peer.Invalidated, "Baseline must reproduce missed notice");
});
Test("Patched teleport removes old observer immediately after position assignment", () => {
    var peer = AddPeer(20, 0); var zdo = new ZDO();
    Move(zdo, new(1024, 0, 0));
    Check(peer.Invalidated && !peer.Knows, "Entrance observer was not invalidated");
    Check(ZDOMan.instance.CheckedPositions.SequenceEqual(new[] {new Vector3(0,0,0), new Vector3(1024,0,0)}), "Wrong notification order");
    Check(zdo.StoredSector == ZoneSystem.GetSectorIndex(zdo.Position), "Sector bookkeeping damaged");
});
Test("Traveler staying in destination sector needs no later boundary crossing", () => {
    var peer = AddPeer(20, 0); var zdo = new ZDO(); Move(zdo, new(1024,0,0));
    for (int i = 0; i < 100; i++) Move(zdo, new(1024 + i%10,0,0));
    Check(peer.Invalidated && ZDOMan.instance.CheckedPositions.Count == 2, "Missing notice or unnecessary repeated notices");
});
Test("Owning peer is never invalidated", () => {
    var peer = AddPeer(10, 0); Move(new ZDO(), new(1024,0,0));
    Check(peer.Knows && !peer.Invalidated, "Owner was invalidated");
});
Test("Overlapping-area observer is retained", () => {
    var peer = AddPeer(20, 32); Move(new ZDO(), new(64,0,0));
    Check(peer.Knows && !peer.Invalidated, "Nearby observer was invalidated");
});
Test("New-position notification keeps destination-area observer", () => {
    var peer = AddPeer(20, 1024); var zdo = new ZDO();
    PlayerSectorPatch.Prefix(zdo, new(1024,0,0), out bool state);
    zdo.Position = new(1024,0,0); // isolate the additional notification from vanilla
    PlayerSectorPatch.Postfix(zdo, state);
    Check(peer.Knows && !peer.Invalidated, "Correction invalidated destination observer");
});
Test("Unknown peers are not notified", () => {
    var peer = AddPeer(20, 0, false); Move(new ZDO(), new(1024,0,0));
    Check(!peer.Invalidated, "Unknown object generated notice");
});
Test("Same-sector and unchanged positions do not notify", () => {
    var zdo = new ZDO(); Move(zdo, new(0,0,0)); Move(zdo, new(10,0,0));
    Check(ZDOMan.instance.CheckedPositions.Count == 0, "Unnecessary notice");
});
Test("Non-player objects receive vanilla behavior only", () => {
    var peer = AddPeer(20, 0); Move(new ZDO { Prefab = "Boar".GetStableHashCode() }, new(1024,0,0));
    Check(peer.Knows && ZDOMan.instance.CheckedPositions.Count == 1, "Non-player patched");
});
Test("Non-host clients do not notify", () => {
    ZNet.instance.Server = false; Move(new ZDO(), new(1024,0,0));
    Check(ZDOMan.instance.CheckedPositions.Count == 0, "Client notified");
});
Test("Missing network and shutdown between prefix/postfix are safe", () => {
    ZNet.instance = null; Move(new ZDO(), new(1024,0,0));
    ZNet.instance = new ZNet(); var zdo = new ZDO();
    PlayerSectorPatch.Prefix(zdo, new(1024,0,0), out bool state);
    ZDOMan.instance = null; PlayerSectorPatch.Postfix(zdo, state);
    PlayerSectorPatch.Prefix(zdo, new(1024,0,0), out state);
    Check(!state, "Missing manager should disable patch");
});
Test("Return teleport removes observer at former exit", () => {
    var peer = AddPeer(20,1024); var zdo = new ZDO {Position = new(1024,0,0)};
    Move(zdo, new(0,0,0)); Check(peer.Invalidated, "Return trip missed");
});
Test("Tiny move straddling boundary follows vanilla approximate equality", () => {
    var zdo = new ZDO { Position = new(31.999998f,0,0) };
    Move(zdo, new(32.000002f,0,0));
    Check(ZDOMan.instance.CheckedPositions.Count == 0, "Notified without actual vanilla move");
});
Console.WriteLine($"PASS {passed}/{passed} behavior tests. Model only; no live multiplayer run.");
