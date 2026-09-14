using System;
using System.Collections.Generic;

namespace RunicCharacterVault
{
    internal static class StartingItemGrantPolicy
    {
        internal static IReadOnlyList<StartingItem> ForEnrollment(bool newCharacter,
            IReadOnlyList<StartingItem> configured) => newCharacter
                ? configured : Array.Empty<StartingItem>();

        internal static bool ApplyEnrollment<TItem>(ClientSaveLifecycle lifecycle,
            bool isLocalPlayer, IEnumerable<StartingItem> startingItems,
            Func<string, TItem> findItem, Func<TItem, int, bool> addItem,
            Action saveProfile, Action<StartingItem> reportFailure)
            where TItem : class
        {
            if (!lifecycle.RecordSpawn(isLocalPlayer))
            {
                return false;
            }

            foreach (StartingItem item in startingItems)
            {
                if (!Grant(item.Prefab, item.Quantity, findItem, addItem))
                {
                    reportFailure(item);
                }
            }

            saveProfile();
            return true;
        }

        internal static bool Grant<TItem>(string prefabName, int quantity,
            Func<string, TItem> findItem, Func<TItem, int, bool> addItem)
            where TItem : class
        {
            TItem item = findItem(prefabName);
            return item != null && addItem(item, quantity);
        }
    }

    internal sealed class StartingItem
    {
        internal StartingItem(string prefab, int quantity)
        {
            Prefab = prefab;
            Quantity = quantity;
        }

        internal string Prefab { get; }
        internal int Quantity { get; }
    }
}
