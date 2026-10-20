using System;
using System.Linq;
using System.Reflection;

namespace RunicAutomation.Operations
{
    internal sealed class NativeInventoryMemoryCheckpoint
    {
        private static readonly FieldInfo[] Fields = typeof(ItemDrop.ItemData).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private readonly Inventory inventory;
        private readonly ItemDrop.ItemData[] originals, copies;
        private readonly byte[] bytes;
        internal NativeInventoryMemoryCheckpoint(Inventory inventory)
        {
            this.inventory = inventory;
            originals = inventory.GetAllItems().ToArray();
            copies = originals.Select(x => x.Clone()).ToArray();
            bytes = NativeInventoryCodec.ComparisonBytes(inventory);
        }
        internal void Restore()
        {
            for (int n = 0; n < originals.Length; n++)
            {
                var copy = copies[n].Clone();
                foreach (var field in Fields) field.SetValue(originals[n], field.GetValue(copy));
            }
            inventory.GetAllItems().Clear(); inventory.GetAllItems().AddRange(originals);
            if (!OwnerOperationQueue.Same(bytes, NativeInventoryCodec.ComparisonBytes(inventory)))
                throw new InvalidOperationException("Inventory memory rollback mismatch");
        }
    }
}
