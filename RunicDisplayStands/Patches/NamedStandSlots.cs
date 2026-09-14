using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace RunicDisplayStands
{
    [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
    internal static class NamedStandSlotLabels
    {
        private static void Postfix(InventoryGrid __instance, List<InventoryElement> ___m_elements)
        {
            bool named = ContainerBridge.Opened != null && ContainerBridge.Opened.IsArmorStand &&
                ContainerBridge.IsStandInventory(__instance.GetInventory());
            for (int i = 0; i < ___m_elements.Count; i++)
            {
                var element = ___m_elements[i];
                var label = element.transform.Find("RunicStandSlotLabel");
                if ((named || label != null) && i >= ArmorStandSlots.Width && i < ArmorStandSlots.Count)
                {
                    // Four cells under five need half a cell of inset. Move the
                    // entire element so icons, labels and pointer targets stay together.
                    // Anchor to the unshifted top row every frame to avoid drift, and
                    // restore native alignment when this grid is reused by a container.
                    var slotRect = (RectTransform)element.transform;
                    var topRect = (RectTransform)___m_elements[i % ArmorStandSlots.Width].transform;
                    var position = slotRect.anchoredPosition;
                    position.x = topRect.anchoredPosition.x + (named ? __instance.m_elementSpace * 0.5f : 0f);
                    slotRect.anchoredPosition = position;
                }
                if (!named)
                {
                    if (label != null) { label.gameObject.SetActive(false); element.gameObject.SetActive(true); }
                    continue;
                }
                if (label == null)
                {
                    var source = element.transform.Find("binding");
                    if (source == null) continue;
                    label = Object.Instantiate(source.gameObject, element.transform, false).transform;
                    label.name = "RunicStandSlotLabel";
                }
                element.gameObject.SetActive(i < ArmorStandSlots.Count);
                if (i >= ArmorStandSlots.Count) continue;
                label.gameObject.SetActive(true);
                var text = label.GetComponent<TMP_Text>();
                text.enabled = true;
                text.text = ArmorStandSlots.Labels[i];
                text.fontSize = 11;
                text.enableAutoSizing = false;
                text.alignment = TextAlignmentOptions.Top;
                text.color = new Color(1f, 0.76f, 0.35f);
                text.raycastTarget = false;
                var rect = text.rectTransform;
                rect.anchorMin = new Vector2(0, 1);
                rect.anchorMax = new Vector2(1, 1);
                rect.pivot = new Vector2(0.5f, 1);
                rect.anchoredPosition = new Vector2(0, -2);
                rect.sizeDelta = new Vector2(0, 15);
            }
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData) })]
    internal static class NamedStandAutoAdd
    {
        private static bool Prefix(Inventory __instance, ItemDrop.ItemData __0, ref bool __result)
        {
            var bridge = ContainerBridge.Opened;
            if (bridge == null || !bridge.IsArmorStand || !ContainerBridge.IsStandInventory(__instance)) return true;
            __result = false;
            if (!bridge.InTransfer) return false;
            int preferred = ArmorStandSlots.DefaultSlot(__0);
            foreach (int slot in new[] { preferred, ArmorStandSlots.RightHand, ArmorStandSlots.LeftHand })
                if (ArmorStandSlots.Accepts(slot, __0) && ArmorStandSlots.GetItem(__instance, slot) == null)
                {
                    __result = __instance.AddItem(__0, ArmorStandSlots.Position(slot));
                    break;
                }
            return false;
        }
    }

    [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.DropItem))]
    internal static class NamedStandDropGuard
    {
        private static bool Prefix(InventoryGrid __instance, ItemDrop.ItemData __1, Vector2i __3, ref bool __result)
        {
            var bridge = ContainerBridge.Opened;
            if (bridge == null || !bridge.IsArmorStand || !ContainerBridge.IsStandInventory(__instance.GetInventory())) return true;
            if (__3.x >= 0 && __3.x < ArmorStandSlots.Width && __3.y >= 0 && __3.y < ArmorStandSlots.Height &&
                ArmorStandSlots.Accepts(__3.y * ArmorStandSlots.Width + __3.x, __1)) return true;
            bridge.Warn("That item does not fit the selected stand slot.");
            __result = false;
            return false;
        }
    }
}
