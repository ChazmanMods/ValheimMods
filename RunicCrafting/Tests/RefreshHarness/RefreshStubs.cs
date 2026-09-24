using System;
using System.Reflection;

public sealed class InventoryGui
{
    public static InventoryGui instance;
    public static bool Visible;
    public static bool IsVisible() => Visible;
    public int Refreshes;
    public Action DuringRefresh;
    private void UpdateCraftingPanel(bool focus)
    {
        Refreshes++;
        DuringRefresh?.Invoke();
    }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name, Type[] arguments) =>
            type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, arguments, null);
    }
}
namespace RunicCrafting
{
    internal static class Plugin { internal static Logger Log = new Logger(); }
    internal sealed class Logger { internal void LogWarning(string message) { } }
}
namespace RunicCrafting.Integration
{
    internal static class CraftingRuntime { internal static bool HasMaterialOperation; }
}
