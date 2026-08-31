using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuietBuildRotation.UI
{
    /// <summary>
    /// Shared, color-blind-friendly colors for the piece-local axis lines and native-hint legend.
    /// Explicit labels remain authoritative; color is a secondary cue only.
    /// </summary>
    internal static class AxisGuidePalette
    {
        internal const string PitchHex = "#FFB000";
        internal const string YawHex = "#56D4FF";
        internal const string RollHex = "#E878C4";

        internal static readonly Color Pitch = new Color32(255, 176, 0, 255);
        internal static readonly Color Yaw = new Color32(86, 212, 255, 255);
        internal static readonly Color Roll = new Color32(232, 120, 196, 255);
    }

    /// <summary>
    /// A late-frame fail-closed watchdog owned by the transient guide root. The high execution
    /// order also lets the lines follow the final pivot produced by Player.LateUpdate.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    internal sealed class AxisGuideWatchdog : MonoBehaviour
    {
        private void LateUpdate() => AxisGuidePresenter.LateUpdate();
    }

    /// <summary>
    /// Transient, client-only piece-local axes drawn through the active placement ghost's root
    /// pivot. The presenter never parents anything to the ghost and creates no collider, Piece,
    /// ZNetView, or saved component.
    /// </summary>
    internal static class AxisGuidePresenter
    {
        private const float MinimumHalfLength = 0.8f;
        private const float MaximumHalfLength = 12f;
        private const float LengthPadding = 1.15f;
        private const float MinimumWidth = 0.025f;
        private const float MaximumWidth = 0.09f;
        private const float RelativeWidth = 0.018f;

        private static GameObject _root;
        private static GameObject _ghost;
        private static LineRenderer _pitchLine;
        private static LineRenderer _yawLine;
        private static LineRenderer _rollLine;
        private static Material _lineMaterial;
        private static int _ghostInstanceId;
        private static int _lastPresentedFrame = -1;
        private static float _halfLength;
        private static bool _activationLogged;
        private static bool _failureLogged;

        /// <summary>
        /// Stamps and presents the guide for this frame. Runtime owns all input and placement
        /// gates, while the presenter independently fails closed if no current-frame stamp arrives.
        /// </summary>
        internal static void Present(GameObject ghost)
        {
            if (!Diagnostics.CanRun || !ghost || !ghost.activeInHierarchy)
            {
                Hide();
                return;
            }

            try
            {
                int instanceId = ghost.GetInstanceID();
                if (!_root || !_ghost || instanceId != _ghostInstanceId)
                {
                    Hide();
                    Create(ghost, instanceId);
                }

                _lastPresentedFrame = Time.frameCount;
                UpdateGeometry();
                OrientationPresenter.SetAxisGuideLegend(true);

                if (!_activationLogged)
                {
                    _activationLogged = true;
                    Diagnostics.Info(
                        "Runic v2 piece-local axis guides are active: Pitch X, Roll Z, Yaw Y.");
                }
            }
            catch (Exception exception)
            {
                Hide();
                if (_failureLogged) return;
                _failureLogged = true;
                Diagnostics.Warn(
                    "Runic piece-axis guides were hidden after a presentation failure: " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        /// <summary>
        /// Deactivates immediately, then destroys all transient world objects and material.
        /// Unity destruction is deferred, so deactivation is what guarantees release-frame hiding.
        /// </summary>
        internal static void Hide()
        {
            try
            {
                OrientationPresenter.SetAxisGuideLegend(false);
            }
            catch (Exception)
            {
                // Cleanup must remain fail closed even if Valheim is rebuilding its hint hierarchy.
            }

            DestroyWorldObjects();
        }

        internal static void Destroy() => Hide();

        /// <summary>
        /// Called only by the transient watchdog. A skipped placement-input frame cannot leave the
        /// previous frame's guide visible, and a valid guide follows Valheim's final LateUpdate pose.
        /// </summary>
        internal static void LateUpdate()
        {
            if (!_root) return;
            if (_lastPresentedFrame != Time.frameCount || !_ghost || !_ghost.activeInHierarchy)
            {
                Hide();
                return;
            }

            try
            {
                UpdateGeometry();
            }
            catch (Exception exception)
            {
                Hide();
                if (_failureLogged) return;
                _failureLogged = true;
                Diagnostics.Warn(
                    "Runic piece-axis guides were hidden after a late-frame failure: " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        private static void Create(GameObject ghost, int instanceId)
        {
            Shader shader = Shader.Find("Sprites/Default") ??
                            Shader.Find("UI/Default") ??
                            Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (!shader)
                throw new InvalidOperationException("no safe vertex-color line shader was found");

            _lineMaterial = new Material(shader)
            {
                name = "RunicPrecision_AxisGuideMaterial",
                hideFlags = HideFlags.HideAndDontSave,
                color = Color.white,
                renderQueue = 4000
            };

            _root = new GameObject(
                "RunicPrecision_LocalAxisGuides",
                typeof(AxisGuideWatchdog));
            _root.hideFlags = HideFlags.HideAndDontSave;
            _root.layer = ghost.layer;
            _root.transform.SetParent(null, false);
            _root.transform.SetPositionAndRotation(
                ghost.transform.position,
                ghost.transform.rotation);

            _ghost = ghost;
            _ghostInstanceId = instanceId;
            _halfLength = MeasureHalfLength(ghost);
            float width = Mathf.Clamp(
                _halfLength * RelativeWidth,
                MinimumWidth,
                MaximumWidth);

            _pitchLine = CreateLine("Pitch_X", AxisGuidePalette.Pitch, width, ghost.layer);
            _yawLine = CreateLine("Yaw_Y", AxisGuidePalette.Yaw, width, ghost.layer);
            _rollLine = CreateLine("Roll_Z", AxisGuidePalette.Roll, width, ghost.layer);
        }

        private static LineRenderer CreateLine(
            string name,
            Color color,
            float width,
            int layer)
        {
            GameObject lineObject = new GameObject(name, typeof(LineRenderer));
            lineObject.hideFlags = HideFlags.HideAndDontSave;
            lineObject.layer = layer;
            lineObject.transform.SetParent(_root.transform, false);

            LineRenderer line = lineObject.GetComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;
            line.sharedMaterial = _lineMaterial;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 6;
            line.numCornerVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            line.allowOcclusionWhenDynamic = false;
            line.sortingOrder = short.MaxValue;
            return line;
        }

        private static void UpdateGeometry()
        {
            if (!_root || !_ghost || !_pitchLine || !_yawLine || !_rollLine)
                throw new InvalidOperationException("axis-guide objects are incomplete");

            Vector3 pivot = _ghost.transform.position;
            Quaternion orientation = _ghost.transform.rotation;
            _root.transform.SetPositionAndRotation(pivot, orientation);
            SetAxis(_pitchLine, pivot, orientation * Vector3.right);
            SetAxis(_yawLine, pivot, orientation * Vector3.up);
            SetAxis(_rollLine, pivot, orientation * Vector3.forward);
        }

        private static void SetAxis(LineRenderer line, Vector3 pivot, Vector3 worldAxis)
        {
            Vector3 extent = worldAxis * _halfLength;
            line.SetPosition(0, pivot - extent);
            line.SetPosition(1, pivot + extent);
        }

        private static float MeasureHalfLength(GameObject ghost)
        {
            Vector3 pivot = ghost.transform.position;
            float radius = 0f;
            Renderer[] renderers = ghost.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                // Particle/trail bounds can be much larger than the actual build piece. Mesh
                // renderers describe the stable visible piece; colliders remain the fallback.
                if (!renderer ||
                    (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)))
                    continue;
                AccumulateRadius(renderer.bounds, pivot, ref radius);
            }

            // Some placement ghosts expose their useful size only through disabled colliders.
            // Read them as a fallback; the guide itself never creates or changes a collider.
            if (radius < 0.001f)
            {
                Collider[] colliders = ghost.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider collider = colliders[i];
                    if (collider) AccumulateRadius(collider.bounds, pivot, ref radius);
                }
            }

            return Mathf.Clamp(
                radius * LengthPadding,
                MinimumHalfLength,
                MaximumHalfLength);
        }

        private static void AccumulateRadius(Bounds bounds, Vector3 pivot, ref float radius)
        {
            Vector3 centerDelta = bounds.center - pivot;
            Vector3 extents = bounds.extents;
            if (!IsFinite(centerDelta) || !IsFinite(extents)) return;

            Vector3 farthest = new Vector3(
                Mathf.Abs(centerDelta.x) + Mathf.Abs(extents.x),
                Mathf.Abs(centerDelta.y) + Mathf.Abs(extents.y),
                Mathf.Abs(centerDelta.z) + Mathf.Abs(extents.z));
            radius = Mathf.Max(radius, farthest.magnitude);
        }

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static void DestroyWorldObjects()
        {
            _lastPresentedFrame = -1;
            _ghostInstanceId = 0;
            _halfLength = 0f;
            _ghost = null;
            _pitchLine = null;
            _yawLine = null;
            _rollLine = null;

            if (_root)
            {
                _root.SetActive(false);
                UnityEngine.Object.Destroy(_root);
            }
            _root = null;

            if (_lineMaterial)
                UnityEngine.Object.Destroy(_lineMaterial);
            _lineMaterial = null;
        }
    }
}
