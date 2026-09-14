using System;
using System.Collections.Generic;
using System.Reflection;
using ItemData = ItemDrop.ItemData;

namespace RunicCrafting.Integration
{
    // Transaction-local memory only. Never reload live items through Valheim's lossy disk codec.
    internal sealed class CraftingInventorySnapshot
    {
        private static readonly FieldInfo[] ItemFields = typeof(ItemData).GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private readonly Inventory _inventory;
        private readonly List<ItemData> _originals;
        private readonly List<ItemData> _copies = new List<ItemData>();
        private readonly string _serialized;

        internal CraftingInventorySnapshot(Inventory inventory)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _serialized = ValheimReflection.SaveInventory(inventory).GetBase64();
            _originals = new List<ItemData>(inventory.GetAllItems());
            foreach (ItemData item in _originals) _copies.Add(item.Clone());
            CreateShadow(); // Verify the snapshot before any live mutation.
        }

        internal Inventory CreateShadow()
        {
            var shadow = new Inventory(_inventory.GetName(), null,
                _inventory.GetWidth(), _inventory.GetHeight());
            foreach (ItemData item in _copies) shadow.GetAllItems().Add(item.Clone());
            Verify(shadow);
            return shadow;
        }

        internal void Restore(Inventory inventory)
        {
            if (!ReferenceEquals(inventory, _inventory))
                throw new InvalidOperationException("A Crafting snapshot belongs to another inventory.");
            // Keep the original item references: equipped items and other game systems hold them.
            for (int index = 0; index < _originals.Count; index++)
            {
                ItemData copy = _copies[index].Clone();
                foreach (FieldInfo field in ItemFields)
                    field.SetValue(_originals[index], field.GetValue(copy));
            }
            inventory.GetAllItems().Clear();
            inventory.GetAllItems().AddRange(_originals);
            Verify(inventory);
        }

        internal void Verify(Inventory inventory)
        {
            if (!string.Equals(ValheimReflection.SaveInventory(inventory).GetBase64(),
                    _serialized, StringComparison.Ordinal))
                throw new InvalidOperationException("The exact in-memory Crafting snapshot changed.");
        }
    }
}
