using System;
using RunicStorage.Engine;

var items = new[] { new object() };
if (!StorageItemProtection.TryCapture(items, out var general, out _) || general.StateAt(0) != StorageProtectionState.Unlocked)
    throw new Exception("General storage actions must not inherit Quick Stack-only locks.");
if (!StorageItemProtection.TryCapture(items, out var quick, out _, quickStack: true) || quick.StateAt(0) != StorageProtectionState.Locked)
    throw new Exception("Quick Stack must honor its dedicated locked-slot protection query.");
if (RunicInventory.Api.InventoryIntegrationApi.GeneralCalls != 1 || RunicInventory.Api.InventoryIntegrationApi.QuickCalls != 1)
    throw new Exception("Wrong integration query selected.");
Console.WriteLine("PASS: general actions remain unlocked; Quick Stack respects locked slots through its dedicated query.");

namespace RunicInventory.Api
{
    public static class InventoryIntegrationApi
    {
        public static int GeneralCalls, QuickCalls;
        public static bool TryGetProtection(object item, out int state) { GeneralCalls++; state = 1; return true; }
        public static bool TryGetQuickStackProtection(object item, out int state) { QuickCalls++; state = 2; return true; }
    }
}
