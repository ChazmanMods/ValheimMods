using BepInEx;
using HarmonyLib;
using Jotunn.Managers;

namespace BetterSigns
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.BetterSigns";
        public const string Name = "BetterSigns";
        public const string Version = "0.2.0";
        internal static BepInEx.Logging.ManualLogSource Log;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            gameObject.AddComponent<SignEditor>();
            PrefabManager.OnVanillaPrefabsAvailable += SignPieceRegistrar.Register;
            Logger.LogInfo($"{Name} v{Version} loaded.");
        }

        private void OnDestroy()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= SignPieceRegistrar.Register;
            _harmony?.UnpatchSelf();
        }
    }
}
