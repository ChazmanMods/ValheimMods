using BepInEx;
using HarmonyLib;

namespace Ghostbuster
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "chazman.Ghostbuster";
        public const string PluginName = "Ghostbuster";
        public const string PluginVersion = "1.0.0";

        private Harmony _harmony;

        private void Awake()
        {
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo("Ghostbuster 1.0.0 loaded: server-side player sector removal fix.");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }
}
