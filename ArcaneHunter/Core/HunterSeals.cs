using System;
using System.Linq;

namespace ArcaneHunter.Core
{
    public sealed class HunterSeal
    {
        public readonly string Name, Description;
        private readonly Func<JournalData, bool> test;
        public HunterSeal(string name, string description, Func<JournalData, bool> test)
        { Name = name; Description = description; this.test = test; }
        public bool Earned(JournalData data) => test(data);
    }
    public static class HunterSeals
    {
        public static readonly HunterSeal[] All = {
            new HunterSeal("First Blood", "Record your first wild kill.", j => j.Records.Any(r => r.Kills > 0)),
            new HunterSeal("Ghost of the Grove", "Record ten clean opening kills.", j => j.Records.Sum(r => (long)r.CleanKills) >= 10),
            new HunterSeal("Beyond the Bowstring", "Record a bow or crossbow kill from at least 80m.", j => j.Records.Any(r => r.LongestShot >= 80)),
            new HunterSeal("Stormchaser", "Hunt five serpents.", j => j.Records.Any(r => r.Prefab == "Serpent" && r.Kills >= 5)),
            new HunterSeal("The Living Atlas", "Observe twenty different species.", j => j.Records.Count(r => r.Observed) >= 20),
            new HunterSeal("Crown of the Wild", "Hunt a two-star creature.", j => j.Records.Any(r => r.HighestLevel >= 3)),
            new HunterSeal("Keeper of Three Oaths", "Complete all three commissions on the same daily board.", j => j.Board.Count == 3 && j.Board.All(c => c.Paid)),
            new HunterSeal("A Hundred Footprints", "Reach Legend mastery with one species.", j => j.Records.Any(r => r.Kills >= 100))
        };
    }
}
