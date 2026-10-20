using System;
using System.Collections.Generic;
using ArcaneHunter.Core;

// The actual production prefix is compiled against this small spawn-data adapter.
// Game/API compatibility is checked separately against the installed assemblies.
public class SpawnSystem
{
    public class Prefab { public string name; }
    public class SpawnData
    {
        public Prefab m_prefab;
        public bool m_enabled = true, m_spawnAtDay, m_spawnAtNight = true;
        public float m_spawnChance = 5, m_spawnInterval = 1000;
        public int m_maxSpawned = 1;
        public List<string> m_requiredEnvironments = new List<string> { "ThunderStorm" };
        public SpawnData Clone()
        {
            var copy = (SpawnData)MemberwiseClone();
            copy.m_requiredEnvironments = new List<string>(m_requiredEnvironments);
            return copy;
        }
    }
}
namespace ArcaneHunter
{
    internal static class Plugin { public static FakeSettings Settings = new FakeSettings(); }
    internal class FakeEntry { public bool Value = true; }
    internal class FloatEntry { public float Value = 500; }
    internal class FakeSettings
    {
        public FakeEntry Enabled = new FakeEntry();
        public FakeEntry AutoPickup = new FakeEntry();
        public FloatEntry AutoPickupRange = new FloatEntry();
        public Dictionary<string, SpawnRule> Rules = new Dictionary<string, SpawnRule>();
        public SpawnRule Rule(string prefab) => Rules.TryGetValue(prefab, out var rule) ? rule : null;
    }
}
