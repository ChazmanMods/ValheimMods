using System;
using RunicSentinel.Core;
using UnityEngine;

namespace RunicSentinel.Runtime
{
    internal sealed partial class SentinelAdminPanel
    {
        private BuildingSettings _buildingDraft;
        private bool _buildingEnabled;
        private string _buildingSupport = "1", _buildingRaise = "8", _buildingDig = "8";
        private void ResetBuildingDraft()
        {
            _buildingDraft = null;
            try
            {
                if (string.IsNullOrEmpty(_document.BuildingSaved)) return;
                _buildingDraft = BuildingSettings.Decode(Convert.FromBase64String(_document.BuildingSaved));
                _buildingEnabled = _buildingDraft.Enabled;
                _buildingSupport = BuildingSettings.Number(_buildingDraft.Limits.SupportMultiplier);
                _buildingRaise = BuildingSettings.Number(_buildingDraft.Limits.RaiseHeight);
                _buildingDig = BuildingSettings.Number(_buildingDraft.Limits.DigDepth);
            }
            catch { _buildingDraft = null; }
        }
        private void BuildingField(string label, ref string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(PT(label), _label, GUILayout.ExpandWidth(true));
            value = GUILayout.TextField(value ?? "", 8, _textField, GUILayout.Width(110f), GUILayout.Height(32f));
            GUILayout.EndHorizontal();
        }
        private void DrawBuilding()
        {
            Header(PT("Building and terrain"));
            if (_buildingDraft == null)
            {
                GUILayout.Label(string.IsNullOrEmpty(_document.BuildingStatus) ? PT("Update the full Sentinel host to use building settings.") : _document.BuildingStatus, _label);
                GUILayout.Space(16f);
                return;
            }
            Row(PT("Running limits"), _document.BuildingActive);
            GUILayout.Label(_document.BuildingStatus, _statusStyle);
            GUI.enabled = !_requesting;
            _buildingEnabled = GUILayout.Toggle(_buildingEnabled, PT("Enable custom building and terrain limits"));
            BuildingField("Vertical support multiplier (1–10)", ref _buildingSupport);
            BuildingField("Maximum terrain raise in metres (1–64)", ref _buildingRaise);
            BuildingField("Maximum dig depth in metres (1–64)", ref _buildingDig);
            bool valid = BuildingSettings.TryLimits(_buildingSupport, _buildingRaise, _buildingDig, out var limits);
            bool changed = valid && (_buildingEnabled != _buildingDraft.Enabled || limits.SupportMultiplier != _buildingDraft.Limits.SupportMultiplier || limits.RaiseHeight != _buildingDraft.Limits.RaiseHeight || limits.DigDepth != _buildingDraft.Limits.DigDepth);
            GUI.enabled = !_requesting && valid && changed;
            if (GUILayout.Button(PT("Save building limits for next restart"), _button, GUILayout.Height(40f)))
            {
                var request = new BuildingSettings { Revision = _buildingDraft.Revision, Enabled = _buildingEnabled, Limits = limits };
                _requesting = true;
                _status = PT("Saving building limits…");
                _control.SaveBuilding(request, (ok, message) => Enqueue(() =>
                {
                    _requesting = false; _status = message;
                    if (ok) Refresh();
                }));
            }
            GUI.enabled = true;
            if (!valid) GUILayout.Label(PT("Enter support 1–10 and terrain limits 1–64. Use a dot for decimals."), _statusStyle);
            GUILayout.Label(PT("Support 2 halves vertical support loss; it is not an exact height. Terrain limits are measured from the original ground."), _label);
            GUILayout.Label(PT("Restart the host to apply. Players need the updated Sentinel Client. Back up your world: reducing limits can collapse buildings or clamp terrain."), _label);
            GUILayout.Space(20f);
        }
    }
}
