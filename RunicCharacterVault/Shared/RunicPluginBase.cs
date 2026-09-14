using System;
using System.Reflection;
using BepInEx;
using HarmonyLib;

namespace RunicCharacterVault.Shared
{
    public abstract class RunicPluginBase : BaseUnityPlugin
    {
        private Harmony _harmony;
        private bool _patchesApplied;

        protected ModLog InitializePlugin(string pluginGuid)
        {
            ModLog log = new ModLog(Logger);
            System.Version assemblyVersion = GetType().Assembly.GetName().Version;
            log.LogInfo($"AssemblyVersion: {assemblyVersion}.");
            EnsureSharedPatches(log);
            _harmony = new Harmony(pluginGuid);
            PatchOwnNamespace(log);
            return log;
        }

        protected ModLog InitializePlugin(string pluginGuid, Type[] patchTypes)
        {
            ModLog log = new ModLog(Logger);
            System.Version assemblyVersion = GetType().Assembly.GetName().Version;
            log.LogInfo($"AssemblyVersion: {assemblyVersion}.");
            EnsureSharedPatches(log);
            _harmony = new Harmony(pluginGuid);
            foreach (Type patchType in patchTypes)
            {
                _harmony.CreateClassProcessor(patchType).Patch();
                log.LogInfo($"Server patch: {patchType.Name}");
            }
            _patchesApplied = true;
            return log;
        }

        private static void EnsureSharedPatches(ModLog log)
        {
            const string key = "RunicCharacterVault.Shared.ConnectionFailureMenuPatch.v1";
            lock (AppDomain.CurrentDomain)
            {
                if (AppDomain.CurrentDomain.GetData(key) != null) return;
                new Harmony("RunicCharacterVault.Shared")
                    .CreateClassProcessor(typeof(ConnectionFailureMenuPatch)).Patch();
                AppDomain.CurrentDomain.SetData(key, true);
                log.LogDebug("Shared connection failure menu patch was applied.");
            }
        }

        protected void PatchOwnNamespace(ModLog log)
        {
            if (_patchesApplied)
            {
                log.LogDebug("Harmony patches are already active; skipping registration.");
                return;
            }

            string pluginNamespace = GetType().Namespace;
            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (type.Namespace == pluginNamespace)
                {
                    _harmony.CreateClassProcessor(type).Patch();
                }
            }

            _patchesApplied = true;
            log.LogDebug("Harmony patches were applied for the plugin namespace.");
        }

        protected void ShutdownPlugin()
        {
            if (!_patchesApplied)
            {
                return;
            }

            _harmony?.UnpatchSelf();
            _patchesApplied = false;
        }
    }
}
