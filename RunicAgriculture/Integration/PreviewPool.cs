using System;
using System.Collections.Generic;
using RunicAgriculture.Core;
using UnityEngine;

namespace RunicAgriculture.Integration
{
    internal readonly struct RuntimePreviewPosition
    {
        internal RuntimePreviewPosition(
            Vector3 position,
            Quaternion rotation,
            bool isValid,
            string reasonCode)
        {
            Position = position;
            Rotation = rotation;
            IsValid = isValid;
            ReasonCode = reasonCode;
        }

        internal Vector3 Position { get; }
        internal Quaternion Rotation { get; }
        // IsValid means selected for this click. A no-seeds decision still represents ground
        // that passed every placement rule, but is deliberately rendered red for shortage.
        internal bool IsValid { get; }
        internal bool IsResourceShortage =>
            string.Equals(ReasonCode, AgricultureReasonCodes.NoSeeds, StringComparison.Ordinal);
        internal bool IsGroundValid =>
            IsValid || IsResourceShortage;
        internal string ReasonCode { get; }
    }

    internal sealed class PreviewPool : IDisposable
    {
        private readonly int _hardMaximum;
        private readonly List<GameObject> _ghosts = new List<GameObject>();
        private readonly List<SourceRendererState> _sourceRenderers = new List<SourceRendererState>();
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorProperty = Shader.PropertyToID("_EmissionColor");
        private static MaterialPropertyBlock _blockedHighlight;
        private static MaterialPropertyBlock _shortageHighlight;
        private string _sourceIdentity;

        internal PreviewPool(int hardMaximum)
        {
            if (hardMaximum < 1) throw new ArgumentOutOfRangeException(nameof(hardMaximum));
            _hardMaximum = hardMaximum;
        }

        internal int AllocatedCount => _ghosts.Count;

        internal void Show(
            GameObject sourceGhost,
            IReadOnlyList<RuntimePreviewPosition> positions)
        {
            if (sourceGhost == null || positions == null)
            {
                Hide();
                return;
            }

            string sourceIdentity = PrefabIdentity.Of(sourceGhost);
            if (!string.Equals(_sourceIdentity, sourceIdentity, StringComparison.Ordinal))
            {
                DestroyGhosts();
                _sourceIdentity = sourceIdentity;
            }

            // Unity keeps a managed wrapper after a scene unload destroys its native object.
            // Remove those Unity-null slots before comparing counts so a same-crop preview in
            // the next world recreates its renderer proxies instead of dereferencing a dead one.
            PreviewPoolMaintenance.PruneUnavailable(_ghosts, ghost => ghost == null);

            int required = Math.Min(_hardMaximum, positions.Count);
            if (required > _ghosts.Count)
            {
                // A previous Show call hid the source renderers. Restore their
                // recorded state before copying additional renderer-only proxies, otherwise a
                // later expansion inherits enabled=false and the new preview positions vanish.
                RestoreSourceRenderers();
                Grow(sourceGhost, required);
            }
            HideSourceRenderers(sourceGhost);
            for (int index = 0; index < _ghosts.Count; index++)
            {
                GameObject ghost = _ghosts[index];
                if (index >= required)
                {
                    ghost.SetActive(false);
                    continue;
                }

                RuntimePreviewPosition preview = positions[index];
                ghost.transform.SetPositionAndRotation(preview.Position, preview.Rotation);
                ghost.SetActive(true);
                SetPreviewHighlight(ghost, preview);
            }
        }

        internal void Hide()
        {
            for (int index = 0; index < _ghosts.Count; index++)
                if (_ghosts[index] != null) _ghosts[index].SetActive(false);
            RestoreSourceRenderers();
        }

        public void Dispose() => DestroyGhosts();

        private void Grow(GameObject sourceGhost, int required)
        {
            while (_ghosts.Count < required && _ghosts.Count < _hardMaximum)
            {
                GameObject ghost = CreateRendererOnlyProxy(sourceGhost);
                _ghosts.Add(ghost);
            }
        }

        /// <summary>
        /// Builds a visual-only hierarchy rather than cloning the placement prefab. Cloning an
        /// active crop prefab invokes ZNetView.Awake before scripts can be disabled and can create
        /// phantom ZDOs. These proxies contain transforms and renderers only: no ZNetView, Plant,
        /// Piece, collider, rigidbody, terrain modifier, or other gameplay behaviour is copied.
        /// </summary>
        private static GameObject CreateRendererOnlyProxy(GameObject source)
        {
            var root = new GameObject("RunicAgriculturePreview");
            root.SetActive(false);
            root.layer = source.layer;
            root.transform.localScale = source.transform.lossyScale;

            var transformMap = new Dictionary<Transform, Transform>
            {
                [source.transform] = root.transform
            };
            CopyTransforms(source.transform, root.transform, transformMap);

            Renderer[] sourceRenderers = source.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < sourceRenderers.Length; index++)
                CopyRenderer(sourceRenderers[index], transformMap);
            return root;
        }

        private static void CopyTransforms(
            Transform source,
            Transform destination,
            IDictionary<Transform, Transform> transformMap)
        {
            for (int index = 0; index < source.childCount; index++)
            {
                Transform sourceChild = source.GetChild(index);
                var child = new GameObject(sourceChild.gameObject.name + ".Preview");
                child.layer = sourceChild.gameObject.layer;
                child.transform.SetParent(destination, false);
                child.transform.localPosition = sourceChild.localPosition;
                child.transform.localRotation = sourceChild.localRotation;
                child.transform.localScale = sourceChild.localScale;
                child.SetActive(sourceChild.gameObject.activeSelf);
                transformMap[sourceChild] = child.transform;
                CopyTransforms(sourceChild, child.transform, transformMap);
            }
        }

        private static void CopyRenderer(
            Renderer source,
            IReadOnlyDictionary<Transform, Transform> transformMap)
        {
            if (source == null || !transformMap.TryGetValue(source.transform, out Transform targetTransform)) return;
            Renderer target = null;
            if (source is MeshRenderer sourceMeshRenderer)
            {
                MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
                if (sourceFilter == null || sourceFilter.sharedMesh == null) return;
                MeshFilter targetFilter = targetTransform.gameObject.AddComponent<MeshFilter>();
                targetFilter.sharedMesh = sourceFilter.sharedMesh;
                target = targetTransform.gameObject.AddComponent<MeshRenderer>();
            }
            else if (source is SkinnedMeshRenderer sourceSkinned)
            {
                var targetSkinned = targetTransform.gameObject.AddComponent<SkinnedMeshRenderer>();
                targetSkinned.sharedMesh = sourceSkinned.sharedMesh;
                targetSkinned.localBounds = sourceSkinned.localBounds;
                targetSkinned.updateWhenOffscreen = sourceSkinned.updateWhenOffscreen;
                targetSkinned.quality = sourceSkinned.quality;
                if (sourceSkinned.rootBone != null && transformMap.TryGetValue(sourceSkinned.rootBone, out Transform rootBone))
                    targetSkinned.rootBone = rootBone;
                Transform[] sourceBones = sourceSkinned.bones;
                var targetBones = new Transform[sourceBones.Length];
                for (int index = 0; index < sourceBones.Length; index++)
                    if (sourceBones[index] != null && transformMap.TryGetValue(sourceBones[index], out Transform bone))
                        targetBones[index] = bone;
                targetSkinned.bones = targetBones;
                target = targetSkinned;
            }
            if (target == null) return;
            target.sharedMaterials = source.sharedMaterials;
            target.enabled = source.enabled;
            target.shadowCastingMode = source.shadowCastingMode;
            target.receiveShadows = source.receiveShadows;
            target.lightProbeUsage = source.lightProbeUsage;
            target.reflectionProbeUsage = source.reflectionProbeUsage;
            target.sortingLayerID = source.sortingLayerID;
            target.sortingOrder = source.sortingOrder;
        }

        private static void SetPreviewHighlight(
            GameObject ghost,
            RuntimePreviewPosition preview)
        {
            if (!preview.IsGroundValid && _blockedHighlight == null)
            {
                // Terrain, spacing, access, and biome failures are muted amber. Red is reserved
                // exclusively for an otherwise-valid cell that lacks planting resources.
                Color amber = new Color(0.62f, 0.43f, 0.20f, 0.72f);
                _blockedHighlight = new MaterialPropertyBlock();
                _blockedHighlight.SetColor(ColorProperty, amber);
                _blockedHighlight.SetColor(EmissionColorProperty, amber * 0.4f);
            }
            if (preview.IsResourceShortage && _shortageHighlight == null)
            {
                Color shortage = new Color(1f, 0.08f, 0.06f, 0.82f);
                _shortageHighlight = new MaterialPropertyBlock();
                _shortageHighlight.SetColor(ColorProperty, shortage);
                _shortageHighlight.SetColor(EmissionColorProperty, shortage * 0.65f);
            }
            MaterialPropertyBlock highlight = preview.IsValid
                ? null
                : preview.IsResourceShortage ? _shortageHighlight : _blockedHighlight;
            Renderer[] renderers = ghost.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
                renderers[index].SetPropertyBlock(highlight);
        }

        private void DestroyGhosts()
        {
            RestoreSourceRenderers();
            for (int index = 0; index < _ghosts.Count; index++)
                if (_ghosts[index] != null) UnityEngine.Object.Destroy(_ghosts[index]);
            _ghosts.Clear();
            _sourceIdentity = null;
        }

        private void HideSourceRenderers(GameObject sourceGhost)
        {
            if (_sourceRenderers.Count > 0 && ReferenceEquals(_sourceRenderers[0].Source, sourceGhost))
            {
                for (int index = 0; index < _sourceRenderers.Count; index++)
                    if (_sourceRenderers[index].Renderer != null) _sourceRenderers[index].Renderer.enabled = false;
                return;
            }
            RestoreSourceRenderers();
            Renderer[] renderers = sourceGhost.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                _sourceRenderers.Add(new SourceRendererState(sourceGhost, renderer, renderer.enabled));
                renderer.enabled = false;
            }
        }

        private void RestoreSourceRenderers()
        {
            for (int index = 0; index < _sourceRenderers.Count; index++)
            {
                SourceRendererState state = _sourceRenderers[index];
                if (state.Renderer != null) state.Renderer.enabled = state.WasEnabled;
            }
            _sourceRenderers.Clear();
        }

        private readonly struct SourceRendererState
        {
            internal SourceRendererState(GameObject source, Renderer renderer, bool wasEnabled)
            {
                Source = source;
                Renderer = renderer;
                WasEnabled = wasEnabled;
            }

            internal GameObject Source { get; }
            internal Renderer Renderer { get; }
            internal bool WasEnabled { get; }
        }
    }
}
