using System;
using RunicProduction.Contracts;
using UnityEngine;

namespace RunicProduction.Integration
{
    internal enum ProductionLinkHighlightRole
    {
        Input = 1,
        Output = 2,
        Replenishment = 3
    }

    /// <summary>
    /// A loaded-chest-only visual lease. It owns no endpoint state and does not participate in
    /// automation authorization; it simply renders the link records Production already resolved.
    /// </summary>
    internal sealed class ProductionLinkHighlight : MonoBehaviour
    {
        private const int Segments = 56;
        private const int RingCount = 3;
        private GameObject _visualRoot;
        private LineRenderer[] _rings;
        private Material _material;
        private Light _light;
        private Vector3 _center;
        private float _radius;
        private float _height;
        private float _expiresAt;
        private ProductionLinkHighlightRole _role;
        private bool _failedClosed;

        internal void Activate(ProductionLinkHighlightRole role, float lifetimeSeconds)
        {
            if (_failedClosed) return;
            try
            {
                _role = role;
                _expiresAt = Math.Max(
                    _expiresAt,
                    Time.unscaledTime + Math.Max(0.1f, lifetimeSeconds));
                if (!_visualRoot && !CreateVisuals()) return;
                if (!ApplyColor() || !UpdateVisuals()) FailClosed();
            }
            catch
            {
                FailClosed();
            }
        }

        private bool CreateVisuals()
        {
            try
            {
                Bounds bounds = new Bounds(transform.position, Vector3.one);
                Renderer[] renderers = GetComponentsInChildren<Renderer>();
                bool found = false;
                for (int index = 0; index < renderers.Length; index++)
                {
                    Renderer renderer = renderers[index];
                    if (!renderer || renderer is LineRenderer) continue;
                    if (!found)
                    {
                        bounds = renderer.bounds;
                        found = true;
                    }
                    else bounds.Encapsulate(renderer.bounds);
                }
                _center = bounds.center;
                _radius = Math.Max(
                    0.8f,
                    Math.Max(bounds.extents.x, bounds.extents.z) * 1.4f);
                _height = Math.Max(0.65f, bounds.size.y);

                _visualRoot = new GameObject("RunicProductionLinkRings");
                _visualRoot.transform.SetParent(transform, true);
                _visualRoot.transform.position = _center;
                Shader shader = Shader.Find("Sprites/Default") ??
                                Shader.Find("UI/Default");
                if (shader != null) _material = new Material(shader);
                _rings = new LineRenderer[RingCount];
                for (int index = 0; index < _rings.Length; index++)
                {
                    // Unity permits only one Renderer-derived component per GameObject. Each
                    // ring therefore owns a child object instead of stacking three
                    // LineRenderers on RunicProductionLinkRings.
                    var ringRoot = new GameObject(
                        "RunicProductionLinkRing" + index);
                    ringRoot.transform.SetParent(_visualRoot.transform, false);
                    LineRenderer ring = ringRoot.AddComponent<LineRenderer>();
                    if (!ring) return FailClosed();
                    ring.useWorldSpace = true;
                    ring.loop = true;
                    ring.positionCount = Segments;
                    ring.widthMultiplier = index == 1 ? 0.105f : 0.075f;
                    ring.numCornerVertices = 2;
                    ring.numCapVertices = 2;
                    if (_material != null) ring.sharedMaterial = _material;
                    _rings[index] = ring;
                }
                _light = _visualRoot.AddComponent<Light>();
                if (_light != null)
                {
                    _light.type = LightType.Point;
                    _light.range = _radius * 3.25f;
                    _light.shadows = LightShadows.None;
                }
                if (!VisualsReady()) return FailClosed();
                return true;
            }
            catch
            {
                return FailClosed();
            }
        }

        private void Update()
        {
            if (_failedClosed) return;
            try
            {
                if (Time.unscaledTime >= _expiresAt)
                {
                    Destroy(this);
                    return;
                }
                if (!UpdateVisuals()) FailClosed();
            }
            catch
            {
                FailClosed();
            }
        }

        private bool ApplyColor()
        {
            if (!VisualsReady()) return false;
            Color color = ColorFor(_role);
            for (int index = 0; index < _rings.Length; index++)
            {
                LineRenderer ring = _rings[index];
                if (!ring) return false;
                float intensity = index == 1 ? 1f : 0.78f;
                Color edge = Color.Lerp(
                    color,
                    Color.white,
                    index == 1 ? 0.15f : 0.04f);
                edge.a = intensity;
                ring.startColor = edge;
                ring.endColor = color;
            }
            if (_light != null) _light.color = color;
            return true;
        }

        private bool UpdateVisuals()
        {
            if (!VisualsReady()) return false;
            float time = Time.unscaledTime;
            for (int ringIndex = 0; ringIndex < _rings.Length; ringIndex++)
            {
                LineRenderer ring = _rings[ringIndex];
                if (!ring) return false;
                float direction = ringIndex % 2 == 0 ? 1f : -1f;
                float phase = time * (1.6f + ringIndex * 0.35f) * direction;
                float y = _center.y - _height * 0.42f + ringIndex * _height * 0.42f;
                for (int segment = 0; segment < Segments; segment++)
                {
                    float angle = segment * Mathf.PI * 2f / Segments + phase;
                    float wave = Mathf.Sin(angle * 4f + time * 4.5f) * 0.075f;
                    ring.SetPosition(
                        segment,
                        new Vector3(
                            _center.x + Mathf.Cos(angle) * (_radius + wave),
                            y + Mathf.Sin(angle * 2f + time * 3.2f) * 0.07f,
                            _center.z + Mathf.Sin(angle) * (_radius + wave)));
                }
            }
            if (_light != null)
                _light.intensity = 2.1f + Mathf.Sin(time * 5.5f) * 0.5f;
            return true;
        }

        private bool VisualsReady()
        {
            bool missingRing = false;
            if (_rings != null)
                for (int index = 0; index < _rings.Length; index++)
                    if (!_rings[index])
                    {
                        missingRing = true;
                        break;
                    }
            return VisualGraphReady(
                _visualRoot,
                _rings?.Length ?? 0,
                missingRing);
        }

        internal static bool VisualGraphReady(
            bool rootReady,
            int ringCount,
            bool missingRing) =>
            rootReady && ringCount == RingCount && !missingRing;

        private bool FailClosed()
        {
            if (_failedClosed) return false;
            _failedClosed = true;
            enabled = false;
            DestroyVisualObjects();
            Destroy(this);
            return false;
        }

        private void DestroyVisualObjects()
        {
            GameObject root = _visualRoot;
            Material material = _material;
            _visualRoot = null;
            _rings = null;
            _light = null;
            _material = null;
            try { if (root) Destroy(root); }
            catch { }
            try { if (material) Destroy(material); }
            catch { }
        }

        private void OnDestroy()
        {
            _failedClosed = true;
            DestroyVisualObjects();
        }

        internal static ProductionLinkHighlightRole VisualRole(ProductionLinkRole role) =>
            role == ProductionLinkRole.Output
                ? ProductionLinkHighlightRole.Output
                : role == ProductionLinkRole.Replenishment
                    ? ProductionLinkHighlightRole.Replenishment
                    : ProductionLinkHighlightRole.Input;

        internal static Color ColorFor(ProductionLinkHighlightRole role)
        {
            switch (role)
            {
                case ProductionLinkHighlightRole.Output:
                    return new Color(1f, 0.82f, 0.02f, 1f);
                case ProductionLinkHighlightRole.Replenishment:
                    return new Color(0.02f, 1f, 0.82f, 1f);
                default:
                    return new Color(0.12f, 1f, 0.18f, 1f);
            }
        }

        internal static void Show(
            Container container,
            ProductionLinkRole role,
            float lifetimeSeconds)
        {
            if (!container || !container.isActiveAndEnabled) return;
            ProductionLinkHighlight marker =
                container.GetComponent<ProductionLinkHighlight>() ??
                container.gameObject.AddComponent<ProductionLinkHighlight>();
            marker.Activate(VisualRole(role), lifetimeSeconds);
        }

        internal static void Hide(Container container)
        {
            if (!container) return;
            ProductionLinkHighlight marker =
                container.GetComponent<ProductionLinkHighlight>();
            if (marker) Destroy(marker);
        }

        internal static void ClearAll()
        {
            ProductionLinkHighlight[] markers =
                FindObjectsByType<ProductionLinkHighlight>(FindObjectsSortMode.None);
            for (int index = 0; index < markers.Length; index++)
                if (markers[index]) Destroy(markers[index]);
        }
    }
}
