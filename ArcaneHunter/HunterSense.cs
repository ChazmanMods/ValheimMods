using System.Collections.Generic;
using System.Linq;
using ArcaneHunter.Core;
using UnityEngine;

namespace ArcaneHunter
{
    internal sealed class TrailClue
    {
        public string Prefab;
        public Vector3 Position;
        public float Created, Expires;
        public int Level;
        public bool Alerted;
    }
    internal sealed class HunterSense
    {
        private readonly Plugin plugin;
        internal readonly List<TrailClue> Clues = new List<TrailClue>();
        internal float ReadyAt;
        internal string LastMessage = "Listen. Observe. Follow the trail.";
        public HunterSense(Plugin plugin) { this.plugin = plugin; }
        public void ClearClues() => Clues.Clear();
        public void Clear() { Clues.Clear(); ReadyAt = 0; LastMessage = "Listen. Observe. Follow the trail."; }

        public void Focus(Player player)
        {
            if (!Plugin.Settings.Tracking.Value) { plugin.Notify("Hunter's Sense is disabled by the host."); return; }
            Camera camera = Utils.GetMainCamera();
            if (!camera) return;
            Character selected = null;
            float best = 0.965f;
            float radius = Plugin.Settings.Radius.Value;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (!HuntingPatches.Quarry(c) || Vector3.Distance(player.transform.position, c.transform.position) > radius) continue;
                var delta = c.GetCenterPoint() - camera.transform.position;
                float dot = Vector3.Dot(camera.transform.forward, delta.normalized);
                if (dot > best) { best = dot; selected = c; }
            }
            plugin.Journal.Focus = selected ? HuntingPatches.Prefab(selected) : "";
            plugin.SaveJournal(); Clues.Clear();
            plugin.Notify(selected ? "Hunter's focus: " + Bestiary.Name(plugin.Journal.Focus) : "Hunter's focus cleared. Reading all tracks.");
        }
        public void Pulse(Player player)
        {
            if (!Plugin.Settings.Tracking.Value) { plugin.Notify("Hunter's Sense is disabled by the host."); return; }
            if (Time.time < ReadyAt) { plugin.Notify($"Gather your senses: {ReadyAt - Time.time:0}s"); return; }
            float cost = Plugin.Settings.Stamina.Value;
            if (!player.HaveStamina(cost)) { plugin.Notify($"Hunter's Sense requires {cost:0} stamina."); return; }
            player.UseStamina(cost);
            ReadyAt = Time.time + Plugin.Settings.Cooldown.Value;
            plugin.PrepareBoard(); Clues.Clear();
            float radius = Plugin.Settings.Radius.Value;
            string focus = plugin.Journal.Focus;
            var prey = Character.GetAllCharacters().Where(HuntingPatches.Quarry)
                .Where(c => string.IsNullOrEmpty(focus) || HuntingPatches.Prefab(c) == focus)
                .Where(c => (c.transform.position - player.transform.position).sqrMagnitude <= radius * radius)
                .OrderBy(c => (c.transform.position - player.transform.position).sqrMagnitude).Take(8);
            int discoveries = 0;
            foreach (Character c in prey)
            {
                string prefab = HuntingPatches.Prefab(c);
                if (plugin.Journal.Observe(prefab, HuntingPatches.Biome(c))) discoveries++;
                Clues.Add(new TrailClue { Prefab = prefab, Position = c.transform.position, Level = c.GetLevel(),
                    Alerted = c.GetComponent<BaseAI>().IsAlerted(), Created = Time.time,
                    Expires = Time.time + Plugin.Settings.TrailLifetime.Value });
            }
            plugin.Journal.UnlockSeals();
            plugin.SaveJournal();
            LastMessage = Clues.Count == 0 ? "No fresh signs within reach. Try another stretch of wilderness." :
                $"{Clues.Count} fresh trail{(Clues.Count == 1 ? "" : "s")}" + (discoveries > 0 ? $" | {discoveries} new field notes" : "");
            plugin.Notify(LastMessage);
        }
        internal string Wind(TrailClue clue, Player player)
        {
            if (!EnvMan.instance || EnvMan.instance.GetWindIntensity() < 0.1f) return "Still air";
            Vector3 toward = clue.Position - player.transform.position; toward.y = 0;
            float dot = Vector3.Dot(toward.normalized, EnvMan.instance.GetWindDir());
            return dot > 0.35f ? "Wind toward quarry" : dot < -0.35f ? "Wind toward you" : "Crosswind";
        }
    }
}
