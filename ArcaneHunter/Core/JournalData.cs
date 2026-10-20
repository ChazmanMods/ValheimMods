using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace ArcaneHunter.Core
{
    [DataContract]
    public sealed class SpeciesRecord
    {
        [DataMember] public string Prefab;
        [DataMember] public int Kills, CleanKills, HighestLevel;
        [DataMember] public float LongestShot;
        [DataMember] public bool Observed;
        [DataMember] public List<string> Biomes = new List<string>();
    }
    [DataContract]
    public sealed class Commission
    {
        [DataMember] public string Prefab, Kind;
        [DataMember] public int Progress, Goal, Reward;
        [DataMember] public bool Paid;
        public string Label => Kind == "clean" ? "Silent Arrow: one clean kill" : Kind == "range" ? "Farshot: one ranged kill at 40m+" : "Provisioner's Oath: three wild kills";
    }
    [DataContract]
    public sealed class JournalData
    {
        [DataMember] public int Version = 1, XP;
        [DataMember] public string BoardKey = "", Focus = "";
        [DataMember] public List<SpeciesRecord> Records = new List<SpeciesRecord>();
        [DataMember] public List<Commission> Board = new List<Commission>();
        [DataMember] public List<string> Receipts = new List<string>();
        [DataMember] public List<string> Seals = new List<string>();

        public List<string> UnlockSeals()
        {
            var earned = new List<string>();
            foreach (var seal in HunterSeals.All)
                if (!Seals.Contains(seal.Name) && seal.Earned(this)) { Seals.Add(seal.Name); earned.Add(seal.Name); }
            return earned;
        }

        public SpeciesRecord Get(string prefab)
        {
            var record = Records.FirstOrDefault(r => r.Prefab == prefab);
            if (record != null) return record;
            record = new SpeciesRecord { Prefab = prefab };
            Records.Add(record);
            return record;
        }
        public bool Observe(string prefab, string biome)
        {
            var r = Get(prefab);
            bool first = !r.Observed;
            r.Observed = true;
            if (!r.Biomes.Contains(biome)) r.Biomes.Add(biome);
            if (first) XP = Math.Min(100000000, XP + 5);
            return first;
        }
        public void PrepareBoard(string key, string target)
        {
            if (BoardKey == key || string.IsNullOrEmpty(target)) return;
            BoardKey = key;
            Board = new List<Commission> {
                new Commission { Prefab = target, Kind = "kills", Goal = 3, Reward = 40 },
                new Commission { Prefab = target, Kind = "clean", Goal = 1, Reward = 60 },
                new Commission { Prefab = target, Kind = "range", Goal = 1, Reward = 60 }
            };
        }
        public int RecordKill(string receipt, string prefab, int level, bool clean, bool ranged, float distance, string biome, bool commissions)
        {
            if (string.IsNullOrEmpty(receipt) || Receipts.Contains(receipt)) return 0;
            Receipts.Add(receipt);
            if (Receipts.Count > 256) Receipts.RemoveAt(0);
            Observe(prefab, biome);
            var r = Get(prefab);
            r.Kills = Math.Min(10000000, r.Kills + 1);
            if (clean) r.CleanKills = Math.Min(10000000, r.CleanKills + 1);
            r.HighestLevel = Math.Max(r.HighestLevel, Math.Max(1, Math.Min(10, level)));
            distance = HuntingRules.Clamp(distance, 0, 1000, 0);
            if (ranged) r.LongestShot = Math.Max(r.LongestShot, distance);
            int earned = HuntingRules.KillXP(level, clean, ranged, distance);
            if (commissions)
                foreach (var c in Board.Where(c => !c.Paid && c.Prefab == prefab))
                {
                    if (c.Kind == "kills" || c.Kind == "clean" && clean || c.Kind == "range" && ranged && distance >= 40)
                        c.Progress = Math.Min(c.Goal, c.Progress + 1);
                    if (c.Progress >= c.Goal) { c.Paid = true; earned += c.Reward; }
                }
            XP = Math.Min(100000000, XP + earned);
            return earned;
        }
        public string Serialize()
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(JournalData)).WriteObject(stream, this);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
        public static JournalData Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new JournalData();
            if (json.Length > 2000000) throw new InvalidDataException("Journal exceeds supported size.");
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var data = (JournalData)new DataContractJsonSerializer(typeof(JournalData)).ReadObject(stream);
                if (data == null || data.Version != 1 || data.Records == null || data.Board == null || data.Receipts == null ||
                    data.Records.Any(r => r == null || r.Prefab == null || r.Biomes == null) ||
                    data.Board.Any(c => c == null || c.Goal <= 0 || c.Prefab == null))
                    throw new InvalidDataException("Unsupported or damaged hunter journal.");
                if (data.Seals == null) data.Seals = new List<string>();
                return data;
            }
        }
    }
}
