using System;
using RunicProduction;
using RunicProduction.Integration;
using UnityEngine;

internal static class Program
{
    private static int _passed;
    private static Container _old, _next;
    private static Component _station;
    private static void Main()
    {
        Run("solo local ownership does not send RPCs", () =>
        {
            Sim.Reset(); var c = Sim.Chest(1, 10, 1); var s = Sim.Station(1, 20, 1);
            Check(ProductionChestHandoff.TryAcquire(s, c, 777)); Check(Sim.Requests == 0);
        });
        foreach (long next in new long[] { 2, 99 })
            Run(next == 99 ? "dedicated process inherits work without a local player" : "another client inherits work without the linking player", () =>
            {
                Setup(next); Request(next); Check(Sim.OwnerChanges == 0);
                Sim.RequestsNow(); Check(_old.View.Data.Owner == next);
                Sim.Local = 1; Check(!ValheimAccess.IsNativeOwner(_old));
                Sim.SnapshotsNow(); Sim.Local = next;
                Check(ProductionChestHandoff.TryAcquire(_station, _next, 777));
                Check(ValheimAccess.TrySynchronizeLocallyOwnedContainer(_next, out _));
                Check(_next.LiveItems == 40);
                _next.View.Data.Items--; // a single native-owner production commit
                Check(_next.View.Data.Items == 39 && Sim.OwnerChanges == 1);
            });
        Run("ownership notification before inventory snapshot cannot consume stale contents", () =>
        {
            Setup(); _next.View.Data.Items = 2; Request(); Sim.RequestsNow();
            _next.View.Data.Owner = 2; Sim.Local = 2;
            Check(!ProductionChestHandoff.TryAcquire(_station, _next, 777));
            Sim.SnapshotsNow(); Check(ProductionChestHandoff.TryAcquire(_station, _next, 777));
            Check(_next.View.Data.Items == 40);
        });
        Run("wrong receipt cannot unlock a pending transfer", () =>
        {
            Setup(); Request(); _next.View.Data.Owner = 2;
            _next.View.Data.Receipt = Guid.NewGuid().ToString("N");
            Check(!ProductionChestHandoff.TryAcquire(_station, _next, 777));
        });
        Run("duplicate requests yield ownership once", () =>
        {
            Setup(); Request(); Sim.Advance(); Request(); Sim.RequestsNow();
            Check(Sim.OwnerChanges == 1); Sim.SnapshotsNow(); Sim.Local = 2;
            Check(ProductionChestHandoff.TryAcquire(_station, _next, 777));
        });
        Run("open chest defers and resumes after closing", () =>
        {
            Setup(); _old.Busy = true; Request(); Sim.RequestsNow(); Check(Sim.OwnerChanges == 0);
            _old.Busy = false; Sim.Advance(); Request(); Sim.RequestsNow();
            Check(Sim.OwnerChanges == 1);
        });
        Run("unsynchronized owner never yields", () =>
        {
            Setup(); _old.Synchronizable = false; Request(); Sim.RequestsNow(); Check(Sim.OwnerChanges == 0);
        });
        Run("revoked ward or personal-chest permission denies at current owner", () =>
        {
            Setup(); Request(); _old.Access = false; Sim.RequestsNow(); Check(Sim.OwnerChanges == 0);
        });
        Run("removed link between request and grant denies", () =>
        {
            Setup(); Request(); Sim.Authorized = false; Sim.RequestsNow(); Check(Sim.OwnerChanges == 0);
        });
        Run("station ownership changed before grant denies stale requester", () =>
        {
            Setup(); Request(); Sim.Stations[(1, 20)].View.Data.Owner = 3;
            Sim.RequestsNow(); Check(Sim.OwnerChanges == 0);
        });
        Run("unloaded station denies grant", () =>
        {
            Setup(); Request(); Sim.Stations.Remove((1, 20)); Sim.RequestsNow(); Check(Sim.OwnerChanges == 0);
        });
        Run("unloaded chest denies request", () =>
        {
            Setup(); _next.Loaded = false; Request(); Check(Sim.Requests == 0);
        });
        Run("client cannot invent an authorization principal", () =>
        {
            Setup(); Sim.Local = 1;
            _old.View.Handler(2, new ZDOID(20), 999, Guid.NewGuid().ToString("N"));
            Check(Sim.OwnerChanges == 0);
        });
        Run("RPC sender must own the specified station", () =>
        {
            Setup(); Sim.Local = 1;
            _old.View.Handler(3, new ZDOID(20), 777, Guid.NewGuid().ToString("N"));
            Check(Sim.OwnerChanges == 0);
        });
        Run("malformed empty or unbounded receipt rejected", () =>
        {
            Setup(); Sim.Local = 1;
            foreach (string s in new[] { null, "", new string('a', 1024), Guid.Empty.ToString("N"), "not-a-token" })
                _old.View.Handler(2, new ZDOID(20), 777, s);
            Check(Sim.OwnerChanges == 0);
        });
        Run("no mod on old owner does not force ownership or edit items", () =>
        {
            Setup(); _old.View.Handler = null; Request(); Sim.RequestsNow(); Sim.Advance();
            Request(); Sim.RequestsNow(); Check(Sim.OwnerChanges == 0 && _old.View.Data.Items == 40);
        });
        Run("retry preserves receipt when request packet is lost", () =>
        {
            Setup(); Request(); Sim.PendingRequests.Clear(); Sim.Advance(); Request();
            Sim.RequestsNow(); Sim.SnapshotsNow(); Sim.Local = 2;
            Check(ProductionChestHandoff.TryAcquire(_station, _next, 777));
        });
        Run("unowned chest waits for native assignment", () =>
        {
            Setup(); _next.View.Data.Owner = 0; Request(); Check(Sim.Requests == 0);
        });
        Run("disabled mod denies owner handoff", () =>
        {
            Setup(); Request(); ProductionConfig.Enabled.Value = false;
            Sim.RequestsNow(); Check(Sim.OwnerChanges == 0);
        });
        Run("unloaded plugin denies queued requests", () =>
        {
            Setup(); Request(); Sim.Local = 1; ProductionChestHandoff.Shutdown(); Sim.RequestsNow(); Check(Sim.OwnerChanges == 0);
        });
        Run("per-frame request budget limits a large factory", () =>
        {
            Sim.Reset(); Sim.Local = 2; var s = Sim.Station(2, 20, 2);
            for (int i = 0; i < 50; i++)
            {
                Container c = Sim.Chest(2, 100 + i, 1);
                Check(!ProductionChestHandoff.TryAcquire(s, c, 777));
            }
            Check(Sim.Requests == 32);
        });
        Run("per-chest retries are throttled", () =>
        {
            Setup(); for (int i = 0; i < 100; i++) Request(); Check(Sim.Requests == 1);
        });
        Run("shared chest yields in turn, never to two station owners at once", () =>
        {
            Setup(); Request(); Sim.RequestsNow(); Sim.SnapshotsNow(); Sim.Local = 2;
            Check(ProductionChestHandoff.TryAcquire(_station, _next, 777));
            Container third = Sim.Chest(3, 10, 2);
            Component s3 = Sim.Station(3, 30, 3); Sim.Station(2, 30, 3);
            Sim.Local = 3; Check(!ProductionChestHandoff.TryAcquire(s3, third, 777));
            Sim.RequestsNow(); Check(_next.View.Data.Owner == 2); // first owner gets a scheduler quantum
            Sim.Advance(); Sim.Local = 3; Check(!ProductionChestHandoff.TryAcquire(s3, third, 777));
            Sim.RequestsNow(); Check(_next.View.Data.Owner == 3);
            Sim.SnapshotsNow(); Sim.Local = 3; Check(ProductionChestHandoff.TryAcquire(s3, third, 777));
            Sim.Local = 2; Check(!ValheimAccess.IsNativeOwner(_next));
        });
        Run("station departing during handoff does not strand its chest", () =>
        {
            Setup(); Request(); Sim.RequestsNow(); Sim.SnapshotsNow();
            // Peer 2 loses the station before it next runs, so TryAcquire won't observe receipt.
            Sim.Stations[(2, 20)].View.Data.Owner = 3;
            Container third = Sim.Chest(3, 10, 2);
            Component s3 = Sim.Station(3, 20, 3);
            Sim.Local = 3; Check(!ProductionChestHandoff.TryAcquire(s3, third, 777));
            Sim.RequestsNow(); Sim.Advance(); Sim.Local = 3;
            Check(!ProductionChestHandoff.TryAcquire(s3, third, 777)); Sim.RequestsNow();
            Check(_next.View.Data.Owner == 3); Sim.SnapshotsNow(); Sim.Local = 3;
            Check(ProductionChestHandoff.TryAcquire(s3, third, 777));
        });
        Run("reciprocal two-chest acquisition converges instead of swapping forever", () =>
        {
            Sim.Reset();
            Container ax = Sim.Chest(1, 10, 1), ay = Sim.Chest(1, 11, 2);
            Container bx = Sim.Chest(2, 10, 1), by = Sim.Chest(2, 11, 2);
            Component a = Sim.Station(1, 20, 1), b = Sim.Station(2, 30, 2);
            Sim.Station(1, 30, 2); Sim.Station(2, 20, 1);
            Sim.Local = 1; Check(ProductionChestHandoff.TryAcquire(a, ax, 777));
            Check(!ProductionChestHandoff.TryAcquire(a, ay, 777));
            Sim.Local = 2; Check(ProductionChestHandoff.TryAcquire(b, by, 777));
            Check(!ProductionChestHandoff.TryAcquire(b, bx, 777));
            Sim.RequestsNow(); Sim.SnapshotsNow(); Sim.Local = 1;
            Check(ProductionChestHandoff.TryAcquire(a, ax, 777));
            Check(ProductionChestHandoff.TryAcquire(a, ay, 777));
            Check(Sim.OwnerChanges == 1);
        });
        Run("unavailable unrelated chest cannot cause an indefinite contention hold", () =>
        {
            Setup(); var extra = Sim.Chest(1, 11, 3);
            Component a = Sim.Station(1, 30, 1);
            Sim.Local = 1; Check(!ProductionChestHandoff.TryAcquire(a, extra, 777));
            Request(); Sim.RequestsNow(); Check(Sim.OwnerChanges == 0);
            Sim.Advance(5); Sim.Local = 1;
            Check(!ProductionChestHandoff.TryAcquire(a, extra, 777));
            Request(); Sim.RequestsNow(); Check(Sim.OwnerChanges == 1);
        });
        Console.WriteLine($"PASS: {_passed} source-linked handoff scenarios (simulated native transport)");
    }

    private static void Setup(long next = 2)
    {
        Sim.Reset(); _old = Sim.Chest(1, 10, 1); _next = Sim.Chest(next, 10, 1);
        Sim.Station(1, 20, next); _station = Sim.Station(next, 20, next); Sim.Local = next;
    }
    private static void Request(long peer = 2)
    {
        Sim.Local = peer; Check(!ProductionChestHandoff.TryAcquire(_station, _next, 777));
    }
    private static void Check(bool condition) { if (!condition) throw new Exception("assertion failed"); }
    private static void Run(string name, Action test)
    {
        try { test(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { Console.Error.WriteLine("FAIL " + name + ": " + e); Environment.ExitCode = 1; }
    }
}
