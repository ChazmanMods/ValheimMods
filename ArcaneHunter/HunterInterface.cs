using System;
using System.Linq;
using ArcaneHunter.Core;
using Jotunn.Managers;
using UnityEngine;

namespace ArcaneHunter
{
    internal sealed class HunterInterface
    {
        private readonly Plugin plugin;
        internal bool IsOpen { get; private set; }
        private Vector2 scroll;
        private string search = "";
        private int tab;
        private GUIStyle title, heading, body, muted, button, card, shell;
        private Texture2D backdrop, panel;
        public HunterInterface(Plugin plugin) { this.plugin = plugin; }
        public void Open() { if (IsOpen) return; IsOpen = true; GUIManager.BlockInput(true); }
        public void Close() { if (!IsOpen) return; IsOpen = false; GUIManager.BlockInput(false); }

        private void Styles()
        {
            if (title != null) return;
            backdrop = Solid(new Color(0.035f, 0.026f, 0.065f, 0.98f));
            panel = Solid(new Color(0.105f, 0.08f, 0.16f, 0.96f));
            shell = new GUIStyle(GUI.skin.box) { padding = new RectOffset(24, 24, 20, 20) };
            shell.normal.background = backdrop;
            title = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, wordWrap = true };
            title.normal.textColor = new Color(0.91f, 0.76f, 0.44f);
            heading = new GUIStyle(title) { fontSize = 19 };
            body = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, richText = false };
            body.normal.textColor = new Color(0.91f, 0.89f, 0.95f);
            muted = new GUIStyle(body) { fontSize = 13 };
            muted.normal.textColor = new Color(0.68f, 0.70f, 0.79f);
            button = new GUIStyle(GUI.skin.button) { fontSize = 14, padding = new RectOffset(14, 14, 9, 9) };
            card = new GUIStyle(GUI.skin.box) { padding = new RectOffset(14, 14, 10, 10), margin = new RectOffset(0, 0, 4, 7) };
            card.normal.background = panel;
        }
        private static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1); texture.SetPixel(0, 0, color); texture.Apply(); return texture;
        }
        internal void Draw()
        {
            Styles();
            Matrix4x4 original = GUI.matrix;
            float scale = Mathf.Min(Plugin.Settings.UiScale.Value, Mathf.Min(Screen.width / 900f, Screen.height / 720f));
            scale = Mathf.Max(0.35f, scale);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
            float width = Screen.width / scale, height = Screen.height / scale;
            try
            {
                if (IsOpen)
                {
                    GUILayout.BeginArea(new Rect((width - 860) / 2, (height - 660) / 2, 860, 660), shell);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("ARCANE HUNTER", title);
                    if (GUILayout.Button("Close", button, GUILayout.Width(80))) Close();
                    GUILayout.EndHorizontal();
                    GUILayout.Label(HuntingRules.Rank(plugin.Journal.XP) + "  /  " + plugin.Journal.XP + " renown  /  " +
                        plugin.Journal.Records.Count(r => r.Observed) + " species studied", muted);
                    GUILayout.Space(12);
                    tab = GUILayout.Toolbar(tab, new[] { "JOURNAL", "COMMISSIONS", "SEALS", "HUNTER'S WAY", "SPAWNS" }, button);
                    GUILayout.Space(8);
                    scroll = GUILayout.BeginScrollView(scroll);
                    switch (tab) { case 0: Journal(); break; case 1: Commissions(); break; case 2: Seals(); break; case 3: Guide(); break; default: Spawns(); break; }
                    GUILayout.EndScrollView();
                    GUILayout.Space(6);
                    GUILayout.Label($"{Plugin.Settings.JournalKey.Value}: journal    {Plugin.Settings.PulseKey.Value}: sense    {Plugin.Settings.FocusKey.Value}: focus", muted);
                    GUILayout.EndArea();
                }
                else if (!Plugin.InputBusy()) DrawClues(width);
            }
            finally { GUI.matrix = original; }
        }
        private void Journal()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", muted, GUILayout.Width(55));
            search = GUILayout.TextField(search, 100);
            if (GUILayout.Button("All tracks", button, GUILayout.Width(110))) { plugin.Journal.Focus = ""; plugin.SaveJournal(); plugin.Sense.ClearClues(); }
            GUILayout.EndHorizontal();
            GUILayout.Label("Focus: " + (string.IsNullOrEmpty(plugin.Journal.Focus) ? "All wild creatures" : Bestiary.Name(plugin.Journal.Focus)), muted);
            var names = Bestiary.All.Select(s => s.Prefab).Concat(plugin.Journal.Records.Select(r => r.Prefab)).Distinct();
            foreach (string name in names)
            {
                Species species = Bestiary.Find(name);
                string label = Bestiary.Name(name);
                if (search.Length > 0 && (label + " " + name + " " + species?.Region).IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                SpeciesRecord record = plugin.Journal.Records.FirstOrDefault(r => r.Prefab == name);
                GUILayout.BeginVertical(card);
                GUILayout.BeginHorizontal();
                GUILayout.Label(label + "  /  " + (species?.Region ?? "Field discovery"), heading);
                if (GUILayout.Button(plugin.Journal.Focus == name ? "Focused" : "Track", button, GUILayout.Width(90)))
                { plugin.Journal.Focus = name; plugin.SaveJournal(); plugin.Sense.ClearClues(); }
                GUILayout.EndHorizontal();
                GUILayout.Label(record == null ? "No field observations yet." :
                    $"{HuntingRules.Mastery(record.Kills)}  |  {record.Kills} kills  |  {record.CleanKills} clean  |  Best ranged kill {record.LongestShot:0}m  |  Highest stars {Math.Max(0, record.HighestLevel - 1)}", body);
                if (species != null) GUILayout.Label(species.Hint, muted);
                if (record?.Biomes.Count > 0) GUILayout.Label("Observed in: " + string.Join(", ", record.Biomes), muted);
                GUILayout.EndVertical();
            }
        }
        private void Commissions()
        {
            GUILayout.Label("OATHS OF THE WILD", heading);
            GUILayout.Label("Three personal challenges for one quarry. The board is chosen in your current biome on your first hunt, pulse or journal visit each world day. Rewards grant journal renown and titles.", body);
            if (!Plugin.Settings.Commissions.Value) { GUILayout.Label("Commissions are disabled by the host.", muted); return; }
            if (plugin.Journal.Board.Count == 0) GUILayout.Label("Explore or observe a creature, then reopen your journal to find a commission.", body);
            foreach (Commission c in plugin.Journal.Board)
            {
                GUILayout.BeginVertical(card);
                GUILayout.Label(c.Label, heading);
                GUILayout.Label(Bestiary.Name(c.Prefab) + $"  /  {c.Progress} of {c.Goal}  /  +{c.Reward} renown", body);
                GUILayout.Label(c.Paid ? "Oath fulfilled. Renown awarded." : "Active — progress is recorded automatically.", muted);
                GUILayout.EndVertical();
            }
            GUILayout.Label("Clean kill: defeat full-health, unalerted quarry with the opening hit. Farshot: bow or crossbow kill with at least 40m between hunter and quarry when the hit lands. Tamed animals, bosses and cheat-flagged kills are excluded.", muted);
        }
        private void Guide()
        {
            GUILayout.Label("READ THE WILDERNESS", heading);
            GUILayout.Label("Hunter's Sense spends stamina to reveal up to eight nearby wild creatures. Clues preserve the last known position: bearings change as you move, but the quarry can leave the trail behind. Read freshness and alertness before committing.", body);
            GUILayout.Space(12);
            GUILayout.Label("CHOOSE YOUR QUARRY", heading);
            GUILayout.Label("Use Track in the journal to filter your next pulse to one species. The focus shortcut selects a creature near your crosshair within tracking range, including creatures concealed by terrain. With no target in view it clears the filter.", body);
            GUILayout.Space(12);
            GUILayout.Label("WIND, DISTANCE AND PATIENCE", heading);
            GUILayout.Label("Wind hints describe its direction relative to the last sign. They are navigation advice; ArcaneHunter does not change creature smell or detection AI. Tracking only reads loaded creatures and never generates prey.", body);
            GUILayout.Space(12);
            GUILayout.Label("WRITE YOUR LEGEND", heading);
            GUILayout.Label("First observations grant 5 renown. Wild kills grant 10, plus 5 per star, 10 for a clean opening kill and 10 for a 40m ranged kill. Species mastery grows at 1 / 10 / 30 / 100 kills. Earn Pathfinder, Trailwarden, Arcane Ranger, Wildspeaker and Mythic Huntsman titles. Titles and renown are personal records; they do not increase damage or loot.", body);
            GUILayout.Space(12);
            GUILayout.Label("Your field journal travels with your character. Normal game saves persist it. Spawn and tracking settings come from the host; keyboard and display preferences stay local.", muted);
        }
        private void Seals()
        {
            GUILayout.Label("THE HUNTER'S CONSTELLATION", heading);
            GUILayout.Label("Eight feats to write into your legend. Earned seals remain with your character, even when a daily commission board turns over.", body);
            foreach (HunterSeal seal in HunterSeals.All)
            {
                GUILayout.BeginVertical(card);
                GUILayout.Label((plugin.Journal.Seals.Contains(seal.Name) ? "[ EARNED ]  " : "[          ]  ") + seal.Name, heading);
                GUILayout.Label(seal.Description, body);
                GUILayout.EndVertical();
            }
        }
        private void Spawns()
        {
            GUILayout.Label("THE HOST'S HUNTING GROUNDS", heading);
            GUILayout.Label("Current ambient spawn settings. Edit chazman.ArcaneHunter.cfg on the host and restart, or use a compatible configuration manager. All multipliers default to 1. Raids, breeding, dungeon/location spawners and boss summoning are unchanged.", body);
            GUILayout.Space(8);
            foreach (Species s in Bestiary.All)
            {
                SpawnRule rule = Plugin.Settings.Rule(s.Prefab);
                GUILayout.Label($"{s.Name}: chance {rule.Chance:0.##}x  /  frequency {rule.Frequency:0.##}x  /  cap {rule.Population:0.##}x" +
                    (rule.AnyTime ? "  /  any time" : "") + (rule.AnyWeather ? "  /  any weather" : ""), body);
            }
            GUILayout.Space(10);
            GUILayout.Label("SERPENT STARTER: chance 3x, frequency 2x, population 1x. This keeps weather and night conditions intact. A larger chance cannot bypass an invalid habitat or a full population cap.", muted);
            if (!string.IsNullOrWhiteSpace(Plugin.Settings.Overrides.Value)) GUILayout.Label("Custom overrides: " + Plugin.Settings.Overrides.Value, muted);
        }
        private void DrawClues(float width)
        {
            plugin.Sense.Clues.RemoveAll(c => Time.time >= c.Expires);
            if (plugin.Sense.Clues.Count == 0) return;
            Player player = Player.m_localPlayer;
            GUILayout.BeginArea(new Rect(24, 180, 330, 460), card);
            GUILayout.Label("HUNTER'S SENSE", heading);
            GUILayout.Label("Last-known signs • not live positions", muted);
            foreach (TrailClue clue in plugin.Sense.Clues)
            {
                Vector3 delta = clue.Position - player.transform.position;
                float age = Time.time - clue.Created;
                string freshness = age < 8 ? "fresh" : age < 20 ? "fading" : "old";
                GUILayout.Label($"{Bestiary.Name(clue.Prefab)}  {HuntingRules.Bearing(delta.x, delta.z)}  {delta.magnitude:0}m", body);
                GUILayout.Label($"{freshness} / {(clue.Alerted ? "alerted" : "calm")} / {plugin.Sense.Wind(clue, player)}", muted);
            }
            GUILayout.EndArea();
        }
        internal void Dispose()
        {
            Close();
            if (backdrop) UnityEngine.Object.Destroy(backdrop);
            if (panel) UnityEngine.Object.Destroy(panel);
        }
    }
}
