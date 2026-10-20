using System.Collections.Generic;
using System.Reflection;
using Companions;
using HarmonyLib;
using UnityEngine;

namespace WifeMod
{
    public static class WifePrefab
    {
        private static readonly MethodInfo SpawnManagedMethod = AccessTools.Method(
            typeof(CompanionManager), "SpawnCompanion", new[] { typeof(CompanionAppearance), typeof(CompanionTierDef) });

        public static WifeCompanion SpawnFor(Player player, bool child = false)
        {
            if (player == null || ZNetScene.instance == null || SpawnManagedMethod == null) return null;

            HashSet<int> existing = new HashSet<int>();
            foreach (CompanionSetup setup in UnityEngine.Object.FindObjectsOfType<CompanionSetup>())
                existing.Add(setup.GetInstanceID());

            CompanionAppearance appearance = new CompanionAppearance
            {
                ModelIndex = 1,
                HairItem = "Hair3",
                BeardItem = string.Empty,
                SkinColor = new Vector3(1f, 0.72f, 0.55f),
                HairColor = new Vector3(0.12f, 0.07f, 0.04f)
            };
            CompanionTierDef tier = new CompanionTierDef
            {
                PrefabName = "HC_Companion",
                DisplayName = child ? "Child" : "Wife",
                SourcePrefab = "Player",
                Health = child ? 50f : 100f,
                WalkSpeed = 2f,
                RunSpeed = 5f,
                CanWearArmor = true,
                GiveStarterGear = !child
            };

            bool spawned = (bool)SpawnManagedMethod.Invoke(null, new object[] { appearance, tier });
            if (!spawned)
            {
                Plugin.Log.LogError("Offline Companions manager rejected the wife spawn.");
                player.Message(MessageHud.MessageType.Center, "Companion manager could not spawn her; check the log");
                return null;
            }

            CompanionSetup newest = null;
            float nearest = float.MaxValue;
            foreach (CompanionSetup setup in UnityEngine.Object.FindObjectsOfType<CompanionSetup>())
            {
                if (existing.Contains(setup.GetInstanceID())) continue;
                float distance = Vector3.Distance(player.transform.position, setup.transform.position);
                if (distance < nearest) { nearest = distance; newest = setup; }
            }
            if (newest == null)
            {
                Plugin.Log.LogError("The managed companion spawned, but its new instance could not be identified.");
                return null;
            }

            GameObject instance = newest.gameObject;
            WifeCompanion wife = instance.GetComponent<WifeCompanion>() ?? instance.AddComponent<WifeCompanion>();
            if (instance.GetComponent<FireTender>() == null) instance.AddComponent<FireTender>();
            if (!wife.Initialize(player.GetPlayerID(), child))
            {
                Plugin.Log.LogError("Managed companion was missing a usable ZNetView.");
                player.Message(MessageHud.MessageType.Center, "Companion initialization failed; check the log");
                return null;
            }
            player.Message(MessageHud.MessageType.Center, child ? "A child joins your household" : "Your wife joins you");
            return wife;
        }
    }
}
