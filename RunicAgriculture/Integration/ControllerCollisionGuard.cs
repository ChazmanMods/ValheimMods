using System;
using System.Collections.Generic;
using RunicAgriculture.Core;
using UnityEngine;

namespace RunicAgriculture.Integration
{
    /// <summary>
    /// Suppresses the effective gamepad paths belonging to accepted agriculture chords. A
    /// modifier session may contain several sequential primaries: this lets a player cycle and
    /// then confirm or harvest without releasing Controller Alt, while every accepted edge and
    /// all of its Valheim aliases remain unavailable to vanilla until their release frame ends.
    /// </summary>
    internal static class AgricultureControllerCollisionGuard
    {
        private static readonly AgricultureControllerSuppressionSession Session =
            new AgricultureControllerSuppressionSession();
        private static readonly AgricultureUnmodifiedControllerSuppression EditorSession =
            new AgricultureUnmodifiedControllerSuppression();
        private static readonly Dictionary<string, ZInput.ButtonDef> PrimaryDefinitions =
            new Dictionary<string, ZInput.ButtonDef>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, ZInput.ButtonDef> EditorDefinitions =
            new Dictionary<string, ZInput.ButtonDef>(StringComparer.OrdinalIgnoreCase);
        private static ZInput.ButtonDef _modifier;

        internal static bool TryCapture(
            AgricultureControllerBindings bindings,
            ValheimControllerAction primaryAction,
            out string problem)
        {
            UpdateReleaseState();
            if (!bindings.ContainsChordPrimary(primaryAction))
            {
                problem = global::Runic.Localization.RunicText.Get("text_73bf533f8721");
                return false;
            }
            if (!ValheimAccess.TryVerifyControllerActions(bindings, out problem, out _))
                return false;

            ZInput.ButtonDef modifier;
            ZInput.ButtonDef primary;
            string modifierPath;
            string primaryPath;
            try
            {
                modifier = ValheimAccess.ControllerButtonDefinition(bindings.Modifier);
                primary = ValheimAccess.ControllerButtonDefinition(primaryAction);
                modifierPath = ValheimAccess.ControllerEffectivePath(modifier);
                primaryPath = ValheimAccess.ControllerEffectivePath(primary);
            }
            catch (Exception exception)
            {
                problem = global::Runic.Localization.RunicText.Get("text_7c21bc514016") +
                          exception.GetType().Name + ")";
                return false;
            }
            if (modifier == null || primary == null || !modifier.Held || !primary.Pressed)
            {
                problem = global::Runic.Localization.RunicText.Get("text_28de8044ab27");
                return false;
            }
            if (_modifier != null && !ReferenceEquals(_modifier, modifier))
            {
                problem = global::Runic.Localization.RunicText.Get("text_76bedf30c430");
                return false;
            }
            if (!Session.TryCapture(modifierPath, primaryPath, out problem)) return false;

            _modifier = modifier;
            PrimaryDefinitions[primaryPath] = primary;
            problem = string.Empty;
            return true;
        }

        internal static bool TryCaptureEditorControl(
            AgricultureControllerBindings bindings,
            ValheimControllerAction primaryAction,
            out string problem)
        {
            UpdateReleaseState();
            if (!bindings.ContainsEditorPrimary(primaryAction))
            {
                problem = global::Runic.Localization.RunicText.Get("text_be968fdb8e50");
                return false;
            }
            if (!ValheimAccess.TryVerifyControllerActions(bindings, out problem, out _))
                return false;

            ZInput.ButtonDef modifier;
            ZInput.ButtonDef primary;
            string primaryPath;
            try
            {
                modifier = ValheimAccess.ControllerButtonDefinition(bindings.Modifier);
                primary = ValheimAccess.ControllerButtonDefinition(primaryAction);
                primaryPath = ValheimAccess.ControllerEffectivePath(primary);
            }
            catch (Exception exception)
            {
                problem = global::Runic.Localization.RunicText.Get("text_e6ab83d0d9b1") +
                          exception.GetType().Name + ")";
                return false;
            }
            if (modifier == null || primary == null || modifier.Held || !primary.Pressed)
            {
                problem = global::Runic.Localization.RunicText.Get("text_454b9804f802");
                return false;
            }
            if (!EditorSession.TryCapture(primaryPath, out problem)) return false;
            EditorDefinitions[primaryPath] = primary;
            problem = string.Empty;
            return true;
        }

        internal static bool ShouldSuppress(string buttonName)
        {
            UpdateReleaseState();
            if ((!Session.IsActive && !EditorSession.IsActive) || ZInput.instance == null ||
                string.IsNullOrEmpty(buttonName))
                return false;
            ZInput.ButtonDef requested;
            try { requested = ZInput.instance.GetButtonDef(buttonName); }
            catch (Exception) { return false; }
            if (requested == null || requested.Source != ZInput.InputSource.Gamepad) return false;
            string path;
            try { path = requested.GetActionPath(true)?.Trim(); }
            catch (Exception) { return false; }
            return ReferenceEquals(requested, _modifier) || IsCapturedPrimary(requested) ||
                   IsCapturedEditorControl(requested) || Session.ShouldSuppress(path) ||
                   EditorSession.ShouldSuppress(path);
        }

        internal static void Poll() => UpdateReleaseState();

        internal static void Reset()
        {
            Session.Reset();
            EditorSession.Reset();
            PrimaryDefinitions.Clear();
            EditorDefinitions.Clear();
            _modifier = null;
        }

        private static void UpdateReleaseState()
        {
            if (!Session.IsActive && !EditorSession.IsActive) return;
            if ((Session.IsActive && !DefinitionsRemainCurrent()) ||
                (EditorSession.IsActive && !EditorDefinitionsRemainCurrent()))
            {
                Reset();
                return;
            }

            if (Session.IsActive)
            {
                Session.Advance(
                    Time.frameCount,
                    _modifier.Held,
                    path => PrimaryDefinitions.TryGetValue(path, out ZInput.ButtonDef primary) &&
                            primary != null && primary.Held);
                RemoveReleasedDefinitions();
                if (!Session.IsActive)
                {
                    PrimaryDefinitions.Clear();
                    _modifier = null;
                }
            }
            if (EditorSession.IsActive)
            {
                EditorSession.Advance(
                    Time.frameCount,
                    path => EditorDefinitions.TryGetValue(path, out ZInput.ButtonDef primary) &&
                            primary != null && primary.Held);
                RemoveReleasedEditorDefinitions();
            }
        }

        private static bool DefinitionsRemainCurrent()
        {
            if (ZInput.instance == null || _modifier == null) return false;
            try
            {
                ZInput.ButtonDef modifier = ZInput.instance.GetButtonDef(_modifier.Name);
                if (!ReferenceEquals(modifier, _modifier) ||
                    !string.Equals(
                        modifier.GetActionPath(true)?.Trim(),
                        Session.ModifierPath,
                        StringComparison.OrdinalIgnoreCase)) return false;
                foreach (KeyValuePair<string, ZInput.ButtonDef> pair in PrimaryDefinitions)
                {
                    ZInput.ButtonDef primary = ZInput.instance.GetButtonDef(pair.Value.Name);
                    if (!ReferenceEquals(primary, pair.Value) ||
                        !string.Equals(
                            primary.GetActionPath(true)?.Trim(),
                            pair.Key,
                            StringComparison.OrdinalIgnoreCase)) return false;
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsCapturedPrimary(ZInput.ButtonDef requested)
        {
            foreach (ZInput.ButtonDef primary in PrimaryDefinitions.Values)
                if (ReferenceEquals(requested, primary)) return true;
            return false;
        }

        private static bool IsCapturedEditorControl(ZInput.ButtonDef requested)
        {
            foreach (ZInput.ButtonDef primary in EditorDefinitions.Values)
                if (ReferenceEquals(requested, primary)) return true;
            return false;
        }

        private static bool EditorDefinitionsRemainCurrent()
        {
            if (ZInput.instance == null) return false;
            try
            {
                foreach (KeyValuePair<string, ZInput.ButtonDef> pair in EditorDefinitions)
                {
                    ZInput.ButtonDef primary = ZInput.instance.GetButtonDef(pair.Value.Name);
                    if (!ReferenceEquals(primary, pair.Value) ||
                        !string.Equals(
                            primary.GetActionPath(true)?.Trim(),
                            pair.Key,
                            StringComparison.OrdinalIgnoreCase)) return false;
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void RemoveReleasedDefinitions()
        {
            if (PrimaryDefinitions.Count == 0) return;
            var released = new List<string>();
            foreach (string path in PrimaryDefinitions.Keys)
                if (!Session.ContainsPrimary(path)) released.Add(path);
            for (int index = 0; index < released.Count; index++)
                PrimaryDefinitions.Remove(released[index]);
        }

        private static void RemoveReleasedEditorDefinitions()
        {
            if (EditorDefinitions.Count == 0) return;
            var released = new List<string>();
            foreach (string path in EditorDefinitions.Keys)
                if (!EditorSession.Contains(path)) released.Add(path);
            for (int index = 0; index < released.Count; index++)
                EditorDefinitions.Remove(released[index]);
        }
    }
}
