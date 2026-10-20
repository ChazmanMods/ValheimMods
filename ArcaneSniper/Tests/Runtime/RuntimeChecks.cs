using System;
using System.Collections;
using System.IO;
using System.Linq;
using ArcaneSniper;
using BepInEx;
using HarmonyLib;
using UnityEngine;

[BepInPlugin("test.arcanesniper.runtime", "ArcaneSniper isolated runtime checks", "0.3.0")]
[BepInDependency(Plugin.Guid)]
public sealed class RuntimeChecks : BaseUnityPlugin
{
    private Plugin sniper;
    private Type world;
    private int failures, checks;
    private int nativeExceptions;
    private bool finished;
    private void Awake()
    {
        var save = Path.GetFullPath(Path.Combine(Paths.BepInExRootPath, "../saves"));
        if (!save.Contains("ArcaneSniper") || !save.Contains("artifacts")) throw new Exception("Runtime checks require isolated artifact saves.");
        Utils.SetSaveDataPath(save);
        SaveSystem.SetSessionFlags(SaveSystemSessionFlags.DontSaveAnything);
        Application.logMessageReceived += (message, stack, type) => { if (type == LogType.Exception) nativeExceptions++; };
        StartCoroutine(Run()); StartCoroutine(Watchdog());
    }
    private IEnumerator Watchdog()
    {
        yield return new WaitForSecondsRealtime(240);
        if (!finished) { Logger.LogError("SNIPER_RUNTIME_TIMEOUT"); Application.Quit(2); }
    }
    private void Check(bool value, string label)
    {
        checks++; if (!value) failures++;
        Logger.LogMessage((value ? "SNIPER_PASS " : "SNIPER_FAIL ") + label);
    }
    private void Call(string name) => AccessTools.Method(typeof(Plugin), name).Invoke(sniper, null);
    private Vector3 Position
    {
        get => (Vector3)AccessTools.Field(typeof(Plugin), "position").GetValue(sniper);
        set => AccessTools.Field(typeof(Plugin), "position").SetValue(sniper, value);
    }
    private void Focus(Vector3 point) { Position = point; AccessTools.Method(world, "Tick").Invoke(null, new object[] { true, point }); }
    private IEnumerator WaitArea(Vector3 point, float seconds = 35)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until && !ZNetScene.instance.IsAreaReady(point)) yield return null;
    }
    private IEnumerator Run()
    {
        while (!UnityEngine.Object.FindFirstObjectByType<FejdStartup>()) yield return null;
        yield return new WaitForSecondsRealtime(2);
        var fixtureWorld = new World("ArcaneSniperValidation", "sniper-validation") { m_fileSource = FileHelpers.FileSource.Local };
        Game.SetProfile("ArcaneSniperValidation", FileHelpers.FileSource.Local);
        ZNet.SetServer(true, false, false, "ArcaneSniperValidation", "", fixtureWorld); ZNet.ResetServerHost();
        AccessTools.Method(typeof(FejdStartup), "LoadMainScene").Invoke(UnityEngine.Object.FindFirstObjectByType<FejdStartup>(), null);
        while (!Player.m_localPlayer || !ZNetScene.instance || !ZoneSystem.instance.LocationsGenerated) yield return null;
        var player = Player.m_localPlayer;
        player.SetGodMode(true);
        foreach (var bird in UnityEngine.Object.FindObjectsByType<Valkyrie>(FindObjectsSortMode.None)) bird.DropPlayer(true);
        player.SetIntro(false);
        yield return new WaitForSecondsRealtime(4);
        while (player.IsTeleporting() || player.InCutscene() || !ZNetScene.instance.IsAreaReady(player.transform.position)) yield return null;
        player.GetComponent<Rigidbody>().constraints = RigidbodyConstraints.FreezeAll;
        var body = player.transform.position;
        var reference = ZNet.instance.GetReferencePosition();
        sniper = UnityEngine.Object.FindFirstObjectByType<Plugin>();
        world = typeof(Plugin).Assembly.GetType("ArcaneSniper.CameraWorld");
        Check(sniper != null && world != null, "production plugin loaded without patch errors");
        Call("StartFlight");
        Logger.LogMessage("SNIPER_START freefly=" + GameCamera.InFreeFly() + " usable=" + !player.IsTeleporting() + "/" + !player.InCutscene());
        var remote = body + new Vector3(768, 2, 0);
        float start = Time.realtimeSinceStartup;
        Focus(remote);
        yield return WaitArea(remote);
        Check(ZNetScene.instance.IsAreaReady(remote), "remote terrain and object area becomes ready, seconds=" + (Time.realtimeSinceStartup - start));
        Check((player.transform.position - body).sqrMagnitude < .01f && (ZNet.instance.GetReferencePosition() - reference).sqrMagnitude < .01f, "body and original streaming reference preserved");
        Check(ZoneSystem.instance.IsZoneLoaded(body) && !ZNetScene.instance.OutsideActiveArea(remote), "body remains loaded and camera region is active");
        Logger.LogMessage("SNIPER_FOCUS active=" + AccessTools.Field(typeof(Plugin), "active").GetValue(sniper) + " local=" + AccessTools.Property(world, "HasLocal").GetValue(null) + " position=" + Position);
        var wood = Instantiate(ZNetScene.instance.GetPrefab("Wood"), remote, Quaternion.identity);
        var zdo = wood.GetComponent<ZNetView>().GetZDO();
        var remoteList = new System.Collections.Generic.List<ZDO>();
        ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(body), new SimulationDistance(1, 0), remoteList);
        Check(!remoteList.Contains(zdo), "unrelated sector queries remain narrow");
        var sceneContext = world.GetNestedType("SceneContext", System.Reflection.BindingFlags.NonPublic);
        var prefix = AccessTools.Method(sceneContext, "Prefix"); var finalizer = AccessTools.Method(sceneContext, "Finalizer");
        object[] arguments = { null }; prefix.Invoke(null, arguments);
        try
        {
            remoteList.Clear(); ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(body), new SimulationDistance(1, 0), remoteList);
            Check(remoteList.Contains(zdo) && remoteList.Distinct().Count() == remoteList.Count, "native scene interest adds distant ZDO once");
        }
        finally { finalizer.Invoke(null, new object[] { null, arguments[0] }); }
        var monsterPrefab = ZNetScene.instance.GetPrefab("Greydwarf");
        var monster = Instantiate(monsterPrefab, remote + Vector3.up * 2, Quaternion.identity);
        var ai = monster.GetComponent<MonsterAI>();
        var realView = monster.GetComponent<ZNetView>();
        ai.m_wakeupRange = 20; ai.m_fallAsleepDistance = 30;
        AccessTools.Field(typeof(MonsterAI), "m_sleeping").SetValue(ai, true);
        AccessTools.Field(typeof(MonsterAI), "m_sleepDelay").SetValue(ai, 0f);
        AccessTools.Method(typeof(MonsterAI), "UpdateSleep").Invoke(ai, new object[] { 1f });
        Check(realView.IsOwner() && !ai.IsSleeping(), "owner wakes a real native sleeping creature near camera");
        float health = monster.GetComponent<Character>().GetHealth();
        Check((bool)AccessTools.Method(world, "PlayerInRange", new[] { typeof(Vector3), typeof(float) }).Invoke(null, new object[] { remote, 10f }), "spawner camera-presence check succeeds with body far away");
        Check(monster.GetComponent<Character>().GetHealth() == health, "activation itself does not damage creatures");
        ZNetScene.instance.Destroy(monster);
        CheckOutdoorSpawn(remote, player);
        foreach (string dungeon in new[] { "MountainCave02", "Mistlands_DvergrTownEntrance1" })
        {
            var site = remote + new Vector3(dungeon == "MountainCave02" ? 0 : 192, 0, 128);
            Focus(site); yield return WaitArea(site);
            if (ZoneSystem.instance.GetGroundHeight(site, out float height)) site.y = height;
            var definition = ZoneSystem.instance.m_locations.FirstOrDefault(l => l.m_prefabName == dungeon);
            Check(definition != null, "native dungeon asset exists: " + dungeon);
            if (definition == null) continue;
            ZoneSystem.instance.TestSpawnLocation(dungeon, site);
            yield return new WaitForSecondsRealtime(2);
            var entrances = UnityEngine.Object.FindObjectsByType<Teleport>(FindObjectsSortMode.None);
            var passage = entrances.Where(t => t.m_targetPoint && !Character.InInterior(t.transform.position)).OrderBy(t => (t.transform.position - site).sqrMagnitude).FirstOrDefault();
            Check(passage != null && (passage.transform.position - site).sqrMagnitude < 10000, "native linked dungeon entrance created: " + dungeon);
            if (!passage) continue;
            var collider = passage.GetComponent<Collider>() ?? passage.GetComponentInChildren<Collider>();
            Focus(collider ? collider.bounds.center : passage.transform.position);
            Physics.SyncTransforms();
            Call("TravelDungeon");
            Check(Character.InInterior(Position) && (player.transform.position - body).sqrMagnitude < .01f, "first interaction enters real dungeon with body outside: " + dungeon);
            var destination = Position;
            yield return WaitArea(destination);
            var exit = passage.m_targetPoint;
            var exitCollider = exit.GetComponent<Collider>() ?? exit.GetComponentInChildren<Collider>();
            Focus(exitCollider ? exitCollider.bounds.center : exit.transform.position);
            Physics.SyncTransforms(); Call("TravelDungeon");
            Check(!Character.InInterior(Position) && (player.transform.position - body).sqrMagnitude < .01f, "first interaction leaves real dungeon: " + dungeon);
        }
        Focus(remote);
        Call("StopFlight");
        Check(!(bool)AccessTools.Property(world, "HasLocal").GetValue(null) && !GameCamera.InFreeFly(), "exit releases local camera interest and free-camera mode");
        Check((player.transform.position - body).sqrMagnitude < .01f, "all camera operations preserve player position");
        Check(nativeExceptions == 0, "native runtime exception audit, count=" + nativeExceptions);
        finished = true;
        Logger.LogMessage("SNIPER_RUNTIME_" + (failures == 0 ? "PASS" : "FAIL") + " checks=" + checks + " failures=" + failures + "; isolated single-process native world; no joined client or reconnect evidence");
        Application.Quit(failures == 0 ? 0 : 2);
    }

    private void CheckOutdoorSpawn(Vector3 remote, Player player)
    {
        var systems = (System.Collections.Generic.List<SpawnSystem>)AccessTools.Field(typeof(SpawnSystem), "m_instances").GetValue(null);
        var system = systems.FirstOrDefault(s => ZoneSystem.GetZone(s.transform.position) == ZoneSystem.GetZone(remote));
        Check(system != null, "native outdoor spawn system available in camera-only zone");
        if (!system) return;
        var view = system.GetComponent<ZNetView>();
        var oldLists = system.m_spawnLists;
        var fixture = new GameObject("ArcaneSniperSpawnFixture").AddComponent<SpawnSystemList>();
        var spawn = new SpawnSystem.SpawnData
        {
            m_prefab = ZNetScene.instance.GetPrefab("Greydwarf"), m_biome = (Heightmap.Biome)(-1),
            m_biomeArea = Heightmap.BiomeArea.Everything, m_spawnInterval = 1, m_spawnChance = 0,
            m_spawnRadiusMin = 1, m_spawnRadiusMax = 5, m_minAltitude = -1000, m_maxAltitude = 1000,
            m_maxTilt = 90, m_canSpawnCloseToPlayer = true, m_insidePlayerBase = true
        };
        fixture.m_spawners.Add(spawn);
        int timerKey = "b_Greydwarf1".GetStableHashCode();
        long oldTimer = view.GetZDO().GetLong(timerKey, 0);
        try
        {
            system.m_spawnLists = new System.Collections.Generic.List<SpawnSystemList> { fixture };
            view.GetZDO().Set(timerKey, 0L);
            Player.m_localPlayer = null;
            AccessTools.Method(typeof(SpawnSystem), "UpdateSpawning").Invoke(system, null);
            var actualPlayers = (System.Collections.Generic.List<Player>)AccessTools.Field(typeof(SpawnSystem), "m_tempNearPlayers").GetValue(null);
            Check(view.GetZDO().GetLong(timerKey, 0) > 0 && actualPlayers.Count == 0, "native dedicated-style spawning progresses without local or fake players");
            var origins = new System.Collections.Generic.List<Player>();
            object[] arguments = { spawn, origins, Vector3.zero, null };
            bool found = (bool)AccessTools.Method(typeof(SpawnSystem), "FindBaseSpawnPoint").Invoke(system, arguments);
            var point = (Vector3)arguments[2]; var delta = point - remote; delta.y = 0;
            Check(found && delta.sqrMagnitude <= 25.01f && origins.Count == 0, "native spawn-point checks accept an origin around the remote camera without editing player lists");
            spawn.m_maxAltitude = -2000;
            arguments = new object[] { spawn, origins, Vector3.zero, null };
            Check(!(bool)AccessTools.Method(typeof(SpawnSystem), "FindBaseSpawnPoint").Invoke(system, arguments), "native altitude restriction still rejects invalid camera spawns");
        }
        finally
        {
            Player.m_localPlayer = player; system.m_spawnLists = oldLists;
            view.GetZDO().Set(timerKey, oldTimer); Destroy(fixture.gameObject);
        }
    }
}
