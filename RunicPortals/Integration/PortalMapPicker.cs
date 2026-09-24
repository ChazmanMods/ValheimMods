using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RunicPortals.Api;
using RunicPortals.Core;
using UnityEngine;

namespace RunicPortals.Integration
{
    internal sealed partial class PortalRuntime
    {
        private const float MinimumPinHitRadiusPixels = 22f;

        private sealed class PickerCandidate
        {
            internal string PortalId;
            internal string DisplayName;
            internal string Label;
            internal long Revision;
            internal Vector3 Position;
            internal bool OneWay;
            internal Minimap.PinData Pin;
        }

        private sealed class PickerSession
        {
            internal TeleportWorld Source;
            internal int SourceInstanceId;
            internal string SourcePortalId;
            internal long SourceRevision;
            internal string NetworkId;
            internal Player Player;
            internal long PlayerId;
            internal Rigidbody Body;
            internal RigidbodyConstraints OriginalConstraints;
            internal Minimap Map;
            internal Minimap.MapMode OriginalMapMode;
            internal bool MapViewCaptured;
            internal float OriginalLargeZoom;
            internal Vector3 OriginalMapOffset;
            internal bool IconFilterCaptured;
            internal bool IconFilterWasVisible;
            internal PickerCandidate Pending;
            internal string DirectoryRequestId = string.Empty;
            internal ZDOID DirectorySourceZdoId;
            internal long DirectorySourceRevision;
            internal float DirectoryDeadline;
            internal float DirectoryNextAttempt;
            internal int DirectoryAttempts;
            internal bool DirectoryResponseReceived;
            internal int DirectoryExpectedCount;
            internal string DirectoryFailure = string.Empty;
            internal List<PickerCandidate> DirectoryLocallyKnown;
            internal bool DirectoryLocallyTruncated;
            internal readonly List<PickerCandidate> Candidates = new List<PickerCandidate>();
        }

        private static readonly FieldInfo LargeZoomField =
            AccessTools.Field(typeof(Minimap), "m_largeZoom");
        private static readonly FieldInfo MapOffsetField =
            AccessTools.Field(typeof(Minimap), "m_mapOffset");
        private static readonly FieldInfo VisibleIconTypesField =
            AccessTools.Field(typeof(Minimap), "m_visibleIconTypes");
        private static readonly MethodInfo ToggleIconFilterMethod =
            AccessTools.Method(typeof(Minimap), "ToggleIconFilter", new[] { typeof(Minimap.PinType) });

        private PickerSession _mapPicker;
        private GUIStyle _pickerBoxStyle;
        private GUIStyle _pickerTitleStyle;
        private GUIStyle _pickerBodyStyle;

        internal bool MapPickerActive => _mapPicker != null;
        internal bool BlocksModalMapMutation => _mapPicker != null;

        internal bool BlocksPlayerMovement(Player player) =>
            _mapPicker != null && player != null && _mapPicker.Player == player;

        internal bool ProtectsPickerPlayer(Character character) =>
            FeatureEnabled && _mapPicker != null && character != null &&
            character == Player.m_localPlayer && _mapPicker.Player == character &&
            _mapPicker.Player.GetPlayerID() == _mapPicker.PlayerId &&
            !_mapPicker.Player.IsDead() && ValheimContracts.HasLocalPlayerAuthority;

        private bool TryOpenMapPicker(TeleportWorld portal, Player player)
        {
            if (portal == null || player == null || player != Player.m_localPlayer) return true;
            if (_mapPicker != null) return true;
            if (!ValheimContracts.HasLocalPlayerAuthority)
            {
                Message(player, global::Runic.Localization.RunicText.Get("text_2f93a24cb65d"));
                return true;
            }
            WardContext sourceWard = ValheimContracts.ResolveWard(
                portal.transform.position,
                player.GetPlayerID());
            if (!ValheimContracts.WardAllows(sourceWard))
            {
                Message(player, global::Runic.Localization.RunicText.Get("text_8e0fb756f550"));
                RejectRoute(portal, RouteStopCode.AuthorityUnavailable);
                return true;
            }
            TravelPolicyState policy = CurrentTravelPolicy(portal, player);
            if (policy != TravelPolicyState.Allowed)
            {
                Message(player, RouteMessage(TravelStop(policy)));
                RejectRoute(portal, TravelStop(policy));
                return true;
            }
            if (!TryReadVisibleEndpoint(portal, out PortalEndpoint source) ||
                !source.PermitsDeparture ||
                !_permissions.Allows(source, PortalPermissionAdapter.Identity(player.GetPlayerID()), PortalAccessAction.ViewDiscover) ||
                !_permissions.Allows(source, PortalPermissionAdapter.Identity(player.GetPlayerID()), PortalAccessAction.Depart))
            {
                Message(player, global::Runic.Localization.RunicText.Get("text_75e68bce57ee"));
                RejectRoute(portal, RouteStopCode.DepartureDenied);
                return true;
            }
            if (!BeginPicker(portal, player, source)) return true;
            if (!TryBuildPickerCandidates(
                    source, player, out List<PickerCandidate> candidates,
                    out bool truncated, out string failure))
            {
                CancelMapPicker(failure, true);
                return true;
            }
            if (!BeginDirectorySync(_mapPicker, source, candidates, truncated))
                CompletePicker(candidates, truncated);
            return true;
        }

        private bool BeginPicker(TeleportWorld portal, Player player, PortalEndpoint source)
        {
            Minimap map = Minimap.instance;
            Rigidbody body = player.GetComponent<Rigidbody>();
            if (map == null || body == null)
            {
                Message(player, global::Runic.Localization.RunicText.Get("text_9f4761c479e8"));
                return false;
            }
            var session = new PickerSession
            {
                Source = portal,
                SourceInstanceId = portal.GetInstanceID(),
                SourcePortalId = source.PortalId,
                SourceRevision = source.Revision,
                NetworkId = source.NetworkId,
                Player = player,
                PlayerId = player.GetPlayerID(),
                Body = body,
                OriginalConstraints = body.constraints,
                Map = map,
                OriginalMapMode = map.m_mode
            };
            CaptureMapViewState(session);
            _mapPicker = session;
            NoteMapNetworkUsed(source.NetworkId);
            try
            {
                EnforcePickerFreeze(session);
                map.ShowPointOnMap(portal.transform.position);
                if (!ExactLargeMapOpen(session))
                    throw new InvalidOperationException("The large map could not open.");
                _mapOverlay?.Tick(map, true);
                return true;
            }
            catch (Exception exception)
            {
                CancelMapPicker(global::Runic.Localization.RunicText.Get("text_ecd03d8e0d3e") + exception.Message, true);
                return false;
            }
        }

        private bool TryBuildPickerCandidates(
            PortalEndpoint source,
            Player player,
            out List<PickerCandidate> candidates,
            out bool truncated,
            out string failure)
        {
            candidates = new List<PickerCandidate>();
            truncated = false;
            failure = string.Empty;
            _index.MarkDirty();
            if (!_index.Rebuild(Time.realtimeSinceStartup,
                    PortalConfig.IndexRefreshSeconds?.Value ?? 2f) ||
                !_graph.TryGetEndpoint(source.PortalId, out PortalEndpoint currentSource) ||
                !SameRouteState(source, currentSource))
            {
                failure = global::Runic.Localization.RunicText.Get("text_6330e83dbcb7");
                return false;
            }
            string traveler = PortalPermissionAdapter.Identity(player.GetPlayerID());
            PortalDirectoryResult directory = _graph.Query(new PortalDirectoryQuery(
                traveler,
                source.PortalId,
                source.NetworkId,
                string.Empty,
                PortalContractLimits.MaximumDirectoryResults,
                false));
            if (directory.StopCode != RouteStopCode.Ready)
            {
                failure = RouteMessage(directory.StopCode);
                return false;
            }
            foreach (PortalDirectoryEntry entry in directory.Entries)
            {
                if (!_graph.TryGetEndpoint(entry.PortalId, out PortalEndpoint destination) ||
                    !_index.TryGetZdo(entry.PortalId, out ZDO zdo) ||
                    !PortalZdoCodec.TryReadDestination(zdo, out PortalEndpoint current, out _) ||
                    !SameRouteState(destination, current)) continue;
                WardContext ward = ValheimContracts.ResolveWard(zdo.GetPosition(), player.GetPlayerID());
                bool wardAllowed = ValheimContracts.DestinationWardAllows(ward);
                if (!wardAllowed ||
                    !_permissions.Allows(
                        current, traveler, PortalAccessAction.ViewDiscover) ||
                    !_permissions.Allows(
                        current, traveler, PortalAccessAction.Arrive)) continue;
                RoutePlan plan = _graph.Plan(new RoutePlanRequest(
                    traveler,
                    source.PortalId,
                    destination.PortalId,
                    TravelPolicyState.Allowed,
                    true,
                    source.Revision,
                    destination.Revision));
                if (!plan.IsReady) continue;
                string duplicate = entry.DuplicateName ? " [" + entry.Disambiguator + "]" : string.Empty;
                string scope = destination.Mode == PortalMode.StandardPair
                    ? "Standard Pair" : PolicyLabel(destination.NetworkKind);
                string direction = destination.Mode == PortalMode.StandardPair
                    ? "Vanilla return route" : destination.PermitsDeparture ? "Both" : "Arrival only";
                candidates.Add(new PickerCandidate
                {
                    PortalId = destination.PortalId,
                    DisplayName = destination.DisplayName,
                    Label = destination.DisplayName + duplicate + " - " + scope + " - " + direction +
                            (plan.IsOneWay ? global::Runic.Localization.RunicText.Get("text_c4f175a5609f") : string.Empty),
                    Revision = destination.Revision,
                    Position = zdo.GetPosition(),
                    OneWay = plan.IsOneWay
                });
            }
            truncated = directory.Truncated;
            return true;
        }

        private void CompletePicker(List<PickerCandidate> candidates, bool truncated)
        {
            PickerSession session = _mapPicker;
            if (session == null) return;
            session.Candidates.AddRange(candidates ?? new List<PickerCandidate>());
            if (session.Candidates.Count == 0)
            {
                CancelMapPicker(
                    global::Runic.Localization.RunicText.Get("text_5449d5e12c2e") +
                    session.NetworkId + "'.", true);
                return;
            }
            CaptureAndEnablePickerIcon(session);
            foreach (PickerCandidate candidate in session.Candidates)
            {
                candidate.Pin = session.Map.AddPin(
                    candidate.Position,
                    Minimap.PinType.Icon3,
                    candidate.Label,
                    false,
                    false);
                if (candidate.Pin == null)
                {
                    CancelMapPicker(global::Runic.Localization.RunicText.Get("text_96b2b222ad70"), true);
                    return;
                }
                PortalMapMarkerSprite.Apply(candidate.Pin);
                candidate.Pin.m_doubleSize = true;
                candidate.Pin.m_animate = true;
            }
            // The directory can finish after the player pans or zooms. Add pins without changing that view.
            Message(session.Player,
                session.Candidates.Count + global::Runic.Localization.RunicText.Get("text_3f57fc99c65e") +
                (session.Candidates.Count == 1 ? string.Empty : "s") +
                global::Runic.Localization.RunicText.Get("text_ad4abfd7ce03") +
                (truncated ? global::Runic.Localization.RunicText.Get("text_a899fb11b1be") : string.Empty));
        }

        internal bool TryHandleMapPickerClick(Vector3 screenPoint)
        {
            PickerSession session = _mapPicker;
            if (session == null) return false;
            if (!ExactLargeMapOpen(session) || PickerOverlayBounds().Contains(
                    new Vector2(screenPoint.x, Screen.height - screenPoint.y))) return true;
            PickerCandidate closest = null;
            float closestDistance = float.MaxValue;
            foreach (PickerCandidate candidate in session.Candidates)
            {
                RectTransform marker = candidate.Pin?.m_uiElement;
                if (marker == null || !marker.gameObject.activeInHierarchy) continue;
                Vector2 point = RectTransformUtility.WorldToScreenPoint(null, marker.position);
                float radius = Mathf.Max(
                    MinimumPinHitRadiusPixels,
                    Mathf.Max(marker.rect.width, marker.rect.height) * 0.75f);
                float distance = Vector2.Distance(point, new Vector2(screenPoint.x, screenPoint.y));
                if (distance > radius || distance >= closestDistance) continue;
                closest = candidate;
                closestDistance = distance;
            }
            if (closest != null) session.Pending = closest;
            return true;
        }

        internal bool TryHandleMapPickerClick() =>
            TryHandleMapPickerClick(ZInput.pointerPosition);

        private void TickMapPicker()
        {
            PickerSession session = _mapPicker;
            if (session == null) return;
            if (session.Player == null || session.Player != Player.m_localPlayer ||
                session.Player.GetPlayerID() != session.PlayerId || session.Player.IsDead() ||
                session.Player.IsTeleporting() || !ValheimContracts.HasLocalPlayerAuthority ||
                session.Source == null || session.Source.GetInstanceID() != session.SourceInstanceId ||
                session.Map == null || session.Map != Minimap.instance || !ExactLargeMapOpen(session))
            {
                CancelMapPicker(string.Empty, true);
                return;
            }
            EnforcePickerFreeze(session);
            if (!string.IsNullOrEmpty(session.DirectoryRequestId))
            {
                TickDirectoryPicker(session);
                return;
            }
            if (session.Pending == null) return;
            PickerCandidate selected = session.Pending;
            string traveler = PortalPermissionAdapter.Identity(session.PlayerId);
            _selections.Set(new PortalSelection(
                traveler,
                session.SourcePortalId,
                selected.PortalId,
                session.SourceRevision,
                selected.Revision,
                false,
                DateTime.UtcNow.Ticks,
                selected.DisplayName));
            _hoverCache.Remove(session.SourceInstanceId);
            _oneWay.Clear();
            _diagnostics.Record(
                PortalDiagnosticCode.RouteSelected,
                RouteStopCode.Ready,
                session.SourcePortalId);
            TeleportWorld source = session.Source;
            Player player = session.Player;
            EndMapPicker(true);
            TryCommitSelectedTeleport(source, player, true, out _);
        }

        private static void EnforcePickerFreeze(PickerSession session)
        {
            if (session?.Body == null) return;
            session.Body.constraints = RigidbodyConstraints.FreezeAll;
            session.Body.linearVelocity = Vector3.zero;
            session.Body.angularVelocity = Vector3.zero;
            session.Body.Sleep();
        }

        private static bool ExactLargeMapOpen(PickerSession session) =>
            session?.Map != null && session.Map == Minimap.instance &&
            session.Map.m_mode == Minimap.MapMode.Large && session.Map.m_largeRoot != null &&
            session.Map.m_largeRoot.activeInHierarchy;

        private void CancelMapPicker(string message, bool closeMap)
        {
            Player player = _mapPicker?.Player;
            EndMapPicker(closeMap);
            if (!string.IsNullOrEmpty(message)) Message(player, message);
        }

        private void EndMapPicker(bool closeMap)
        {
            PickerSession session = _mapPicker;
            _mapPicker = null;
            if (session == null) return;
            RemovePickerPins(session);
            RestorePickerIconFilter(session);
            if (closeMap && session.Map != null && session.Map == Minimap.instance)
            {
                try { session.Map.SetMapMode(session.OriginalMapMode); }
                catch (Exception) { }
            }
            RestoreMapViewState(session);
            if (session.Body != null)
            {
                try
                {
                    session.Body.constraints = session.OriginalConstraints;
                    session.Body.linearVelocity = Vector3.zero;
                    session.Body.angularVelocity = Vector3.zero;
                    session.Body.WakeUp();
                }
                catch (Exception) { }
            }
        }

        private static void CaptureMapViewState(PickerSession session)
        {
            if (session?.Map == null || LargeZoomField == null || MapOffsetField == null) return;
            try
            {
                if (LargeZoomField.GetValue(session.Map) is float zoom &&
                    MapOffsetField.GetValue(session.Map) is Vector3 offset)
                {
                    session.OriginalLargeZoom = zoom;
                    session.OriginalMapOffset = offset;
                    session.MapViewCaptured = true;
                }
            }
            catch (Exception) { }
        }

        private static void RestoreMapViewState(PickerSession session)
        {
            if (session?.Map == null || !session.MapViewCaptured ||
                LargeZoomField == null || MapOffsetField == null) return;
            try
            {
                LargeZoomField.SetValue(session.Map, session.OriginalLargeZoom);
                MapOffsetField.SetValue(session.Map, session.OriginalMapOffset);
            }
            catch (Exception) { }
        }

        private static void CaptureAndEnablePickerIcon(PickerSession session)
        {
            bool[] visible = VisibleIconTypesField?.GetValue(session.Map) as bool[];
            int icon = (int)Minimap.PinType.Icon3;
            if (visible == null || icon < 0 || icon >= visible.Length)
                throw new InvalidOperationException("The installed map icon filter is unavailable.");
            session.IconFilterCaptured = true;
            session.IconFilterWasVisible = visible[icon];
            if (!session.IconFilterWasVisible) TogglePickerIcon(session.Map);
        }

        private static void RestorePickerIconFilter(PickerSession session)
        {
            if (session?.Map == null || !session.IconFilterCaptured) return;
            try
            {
                bool[] visible = VisibleIconTypesField?.GetValue(session.Map) as bool[];
                int icon = (int)Minimap.PinType.Icon3;
                if (visible != null && icon >= 0 && icon < visible.Length &&
                    visible[icon] != session.IconFilterWasVisible)
                    TogglePickerIcon(session.Map);
            }
            catch (Exception) { }
        }

        private static void TogglePickerIcon(Minimap map)
        {
            if (map == null || ToggleIconFilterMethod == null)
                throw new MissingMethodException(typeof(Minimap).FullName, "ToggleIconFilter");
            ToggleIconFilterMethod.Invoke(map, new object[] { Minimap.PinType.Icon3 });
        }

        private static void RemovePickerPins(PickerSession session)
        {
            if (session == null) return;
            foreach (PickerCandidate candidate in session.Candidates)
            {
                if (candidate.Pin == null || session.Map == null) continue;
                try { session.Map.RemovePin(candidate.Pin); }
                catch (Exception) { }
                candidate.Pin = null;
            }
        }

        internal void DrawMapPickerOverlay()
        {
            PickerSession session = _mapPicker;
            if (session == null || !ExactLargeMapOpen(session)) return;
            if (_pickerBoxStyle == null)
            {
                _pickerBoxStyle = new GUIStyle(GUI.skin.box);
                _pickerTitleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 18,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                _pickerTitleStyle.normal.textColor = new Color(0.96f, 0.82f, 0.4f, 1f);
                _pickerBodyStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true
                };
                _pickerBodyStyle.normal.textColor = Color.white;
            }
            Rect box = PickerOverlayBounds();
            GUI.Box(box, GUIContent.none, _pickerBoxStyle);
            GUI.Label(new Rect(box.x + 10f, box.y + 6f, box.width - 20f, 26f),
                global::Runic.Localization.RunicText.Get("text_90dc8cdfaad1"), _pickerTitleStyle);
            GUI.Label(new Rect(box.x + 10f, box.y + 34f, box.width - 20f, 34f),
                global::Runic.Localization.RunicText.Get("text_afd57636b9fb"),
                _pickerBodyStyle);
        }

        private static Rect PickerOverlayBounds()
        {
            Rect safe = Screen.safeArea;
            float width = Mathf.Min(720f, Mathf.Max(0f, safe.width - 24f));
            return new Rect(
                safe.x + (safe.width - width) * 0.5f,
                Screen.height - safe.yMax + 12f,
                width,
                76f);
        }

        internal void FailMapPickerUi(Exception exception)
        {
            Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_6e380ff1faec"));
            CancelMapPicker(global::Runic.Localization.RunicText.Get("text_38e2f2a667db"), true);
        }

        private void ShutdownMapPicker()
        {
            EndMapPicker(true);
            _pickerBoxStyle = _pickerTitleStyle = _pickerBodyStyle = null;
        }

        private static RouteStopCode TravelStop(TravelPolicyState policy)
        {
            switch (policy)
            {
                case TravelPolicyState.RestrictedItems: return RouteStopCode.RestrictedItems;
                case TravelPolicyState.PortalsDisabled: return RouteStopCode.PortalsDisabled;
                case TravelPolicyState.BossTravelBlocked: return RouteStopCode.BossTravelBlocked;
                default: return RouteStopCode.PolicyUnknown;
            }
        }
    }
}
