using System;
using Newtonsoft.Json;
using UnityEngine;

namespace CompanionForge
{
    [Serializable] public class MenuAnimation
    {
        public string id, label, category;
        public bool loop, hold;
    }

    [Serializable] public class Pack
    {
        public string id, name, version, prefab, bundlePrefab, greeting, affection;
        public bool adult;
        public SecondaryMotionSettings secondaryMotion;
        public MenuAnimation[] animationMenu = Array.Empty<MenuAnimation>();
        [NonSerialized] public GameObject visual;

        // Runtime-loaded mod types have no player-baked Unity serialization data.
        // Read their nested metadata with Valheim's existing JSON.NET assembly.
        internal static Pack Read(string json) => JsonConvert.DeserializeObject<Pack>(json);
    }
}
