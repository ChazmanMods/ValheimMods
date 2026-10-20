using UnityEngine;

namespace RunicHallOfHeroes.Displays
{
    internal sealed class RunicWallLightController : MonoBehaviour, Hoverable, Interactable
    {
        private static readonly Color[] Colors =
        {
            new Color(1.00f, 0.55f, 0.16f),
            new Color(0.25f, 0.65f, 1.00f),
            new Color(0.25f, 1.00f, 0.48f),
            new Color(0.78f, 0.30f, 1.00f),
            new Color(1.00f, 0.18f, 0.12f),
            new Color(0.92f, 0.88f, 0.68f)
        };

        private static readonly int ColorKey =
            "RunicHallOfHeroes_wall_light_color".GetStableHashCode();

        private ZNetView _nview;
        private int _lastColor = -1;

        private void Start()
        {
            _nview = GetComponent<ZNetView>();
            EnsureLight();
            ApplyColor(true);
            InvokeRepeating(nameof(RefreshColor), 1f, 1f);
        }

        private void EnsureLight()
        {
            if (GetComponentInChildren<Light>(true) != null) return;

            var lightObject = new GameObject("Runic overhead light");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = new Vector3(0f, -0.18f, 0.12f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 8f;
            light.intensity = 2.6f;
            light.shadows = LightShadows.Soft;
        }

        public string GetHoverName() => "Runic Display Light";

        public string GetHoverText() =>
            "[<color=yellow><b>$KEY_Use</b></color>] Change light color";

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || _nview == null || !_nview.IsValid()) return false;
            _nview.ClaimOwnership();
            int next = (_nview.GetZDO().GetInt(ColorKey, 0) + 1) % Colors.Length;
            _nview.GetZDO().Set(ColorKey, next);
            ApplyColor(true);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        private void RefreshColor() => ApplyColor(false);

        private void ApplyColor(bool force)
        {
            if (_nview == null || !_nview.IsValid()) return;
            int colorIndex = _nview.GetZDO().GetInt(ColorKey, 0);
            if (!force && colorIndex == _lastColor) return;
            _lastColor = colorIndex;
            Color color = Colors[Mathf.Abs(colorIndex) % Colors.Length];

            foreach (Light light in GetComponentsInChildren<Light>(true))
            {
                light.enabled = true;
                light.color = color;
                light.range = Mathf.Max(light.range, 6f);
                light.intensity = Mathf.Max(light.intensity, 2.2f);
            }

            foreach (ParticleSystem particles in GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particles.main;
                main.startColor = new ParticleSystem.MinMaxGradient(color);
                if (!particles.isPlaying) particles.Play();
            }

            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.materials)
                {
                    if (!material.HasProperty("_EmissionColor")) continue;
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", color * 2.5f);
                }
            }
        }
    }
}
