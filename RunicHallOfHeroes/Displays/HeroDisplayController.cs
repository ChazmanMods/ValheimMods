using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using RunicHallOfHeroes.Chronicles;
using UnityEngine;

namespace RunicHallOfHeroes.Displays
{
    internal sealed class HeroDisplayController : MonoBehaviour
    {
        private ZNetView _nview;

        private void Start()
        {
            _nview = GetComponent<ZNetView>();
        }

        internal List<ItemDrop.ItemData> ReadChronicleItems()
        {
            var result = new List<ItemDrop.ItemData>();
            var armorStand = GetComponent<ArmorStand>();
            if (armorStand == null || _nview == null || !_nview.IsValid()) return result;

            var slotsField = AccessTools.Field(typeof(ArmorStand), "m_slots");
            int slotCount = ((ICollection)slotsField.GetValue(armorStand)).Count;
            for (int i = 0; i < slotCount; i++)
            {
                byte[] bytes = _nview.GetZDO().GetByteArray(
                    $"RunicDisplayStands_itemdata_{i}".GetStableHashCode(), null);
                ItemDrop.ItemData item = DeserializeItem(bytes);
                if (item != null) result.Add(item);
            }
            return result;
        }

        private static ItemDrop.ItemData DeserializeItem(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                var inventory = new Inventory("Hall Chronicle", null, 1, 1);
                inventory.Load(new ZPackage(bytes));
                return inventory.GetAllItems().Count > 0 ? inventory.GetAllItems()[0] : null;
            }
            catch { return null; }
        }
    }
}
