using BepInEx;
using HarmonyLib;
using Jotunn.Managers;

namespace RunicHallOfHeroes
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicHallOfHeroes";
        public const string Name = "RunicHallOfHeroes";
        public const string Version = "0.3.0";

        internal static Plugin Instance;
        internal static BepInEx.Logging.ManualLogSource Log;
        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            PrefabManager.OnVanillaPrefabsAvailable += PieceRegistrar.Register;
            StartCoroutine(Echoes.EchoRecorder.CaptureLoop());
            Logger.LogInfo($"{Name} v{Version} awakened. Equipment will remember.");
        }

        private void OnDestroy()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= PieceRegistrar.Register;
            _harmony?.UnpatchSelf();
        }

    }
}
