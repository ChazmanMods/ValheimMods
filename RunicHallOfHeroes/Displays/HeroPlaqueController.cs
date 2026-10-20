using System.Collections.Generic;
using System.Linq;
using RunicHallOfHeroes.Chronicles;
using UnityEngine;

namespace RunicHallOfHeroes.Displays
{
    internal sealed class HeroPlaqueController : MonoBehaviour
    {
        private Sign _sign;
        private string _lastText;

        private void Start()
        {
            _sign = GetComponent<Sign>();
            if (_sign != null)
            {
                _sign.m_name = "Heroic Chronicle";
                _sign.m_characterLimit = 500;
            }
            InvokeRepeating(nameof(Refresh), 2f, 3f);
        }

        private void Refresh()
        {
            if (_sign == null) return;
            HeroDisplayController display = FindNearestDisplay();
            string text = BuildInscription(display?.ReadChronicleItems());
            if (text == _lastText) return;
            _lastText = text;

            var nview = GetComponent<ZNetView>();
            if (nview != null && nview.IsValid())
            {
                nview.ClaimOwnership();
                _sign.SetText(text);
            }
        }

        private HeroDisplayController FindNearestDisplay()
        {
            HeroDisplayController best = null;
            float bestDistance = 6f;
            foreach (HeroDisplayController display in FindObjectsOfType<HeroDisplayController>())
            {
                float distance = Vector3.Distance(transform.position, display.transform.position);
                if (distance >= bestDistance) continue;
                best = display;
                bestDistance = distance;
            }
            return best;
        }

        private static string BuildInscription(List<ItemDrop.ItemData> items)
        {
            if (items == null || items.Count == 0)
                return "<color=#D9A441>THE EMPTY OATH</color>\nThis place awaits a deed worthy of remembrance.";

            var chronicles = items
                .Select(item => new { Item = item, Chronicle = ItemChronicle.Read(item) })
                .OrderByDescending(x => x.Chronicle.Renown)
                .ToList();
            var leader = chronicles[0];

            string title = string.IsNullOrEmpty(leader.Chronicle.EarnedName)
                ? "THE UNNAMED COMPANY"
                : leader.Chronicle.EarnedName.ToUpperInvariant();
            int kills = chronicles.Sum(x => x.Chronicle.Kills);
            int witnessed = chronicles.Sum(x => x.Chronicle.WitnessedKills);
            int bosses = chronicles.Sum(x => x.Chronicle.BossKills);

            string deed = !string.IsNullOrEmpty(leader.Chronicle.LastExploit)
                ? leader.Chronicle.LastExploit
                : "Its greatest verse has not yet been written.";
            return $"<color=#D9A441>{title}</color>\n{deed}\n" +
                   $"Remembered: {kills + witnessed} victories, {bosses} boss-falls.";
        }
    }
}
