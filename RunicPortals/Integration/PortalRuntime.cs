using System;
using System.Collections.Generic;
using RunicPortals.Api;
using RunicPortals.Core;
using UnityEngine;

namespace RunicPortals.Integration
{
    internal sealed partial class PortalRuntime :
        IPortalDirectoryService,
        IPortalRoutePlanner,
        IPortalStatusService
    {
        private sealed class EditSession
        {
            internal TeleportWorld Portal;
            internal int InstanceId;
            internal long ActorId;
            internal ZDOID PortalId;
            internal int Schema;
            internal int Mode;
            internal int Revision;
            internal PortalEditEvidence Evidence;
            internal long Token;
        }

        private sealed class PortalEditReceiver : TextReceiver
        {
            private readonly PortalRuntime _runtime;
            private readonly long _token;

            internal PortalEditReceiver(PortalRuntime runtime, long token)
            {
                _runtime = runtime;
                _token = token;
            }

            public string GetText() => string.Empty;

            public void SetText(string text)
            {
                try { _runtime?.ConsumeEdit(_token, text); }
                catch (Exception exception)
                {
                    Plugin.DisableAfterPatchFault(exception, "text-commit");
                }
            }
        }

        private sealed class CycleCandidate
        {
            internal string PortalId;
            internal string Label;
            internal string DisplayName;
            internal long SourceRevision;
            internal long DestinationRevision;
            internal bool IsReturn;
        }

        private readonly PortalPermissionAdapter _permissions;
        private readonly PortalGroupRuntime _groups;
        private readonly PortalAuthorityGate _authority;
        private readonly CorrelatedDiagnosticBuffer _diagnostics;
        private readonly PortalGraphService _graph;
        private readonly PortalIndex _index;
        private readonly ReturnRouteStore _returns;
        private readonly PortalSelectionStore _selections;
        private readonly PortalOverwriteConfirmationGate _confirmations;
        private readonly OneWayAcknowledgementStore _oneWay = new OneWayAcknowledgementStore();
        private readonly PortalArrivalSuppression _arrivalSuppression =
            new PortalArrivalSuppression();
        private readonly Dictionary<int, string> _instancePortalIds =
            new Dictionary<int, string>();
        private readonly Dictionary<int, string> _hoverCache =
            new Dictionary<int, string>();
        private EditSession _edit;
        private bool _ready;
        private string _disabledReason = string.Empty;
        private float _nextReturnPrune;
        private long _editSequence;

        internal PortalRuntime(
            PortalGroupRuntime groups,
            PortalAuthorityGate authority,
            CorrelatedDiagnosticBuffer diagnostics,
            PortalOverwriteConfirmationGate confirmations = null)
        {
            _groups = groups ?? throw new ArgumentNullException(nameof(groups));
            _permissions = new PortalPermissionAdapter(groups);
            _authority = authority ?? throw new ArgumentNullException(nameof(authority));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _graph = new PortalGraphService(_permissions, CurrentMaximumEndpoints());
            _index = new PortalIndex(_graph, _diagnostics);
            _returns = new ReturnRouteStore(PortalContractLimits.MaximumReturnRoutes);
            _selections = new PortalSelectionStore(PortalContractLimits.MaximumSelections);
            _confirmations = confirmations ?? new PortalOverwriteConfirmationGate();
        }

        public bool FeatureEnabled =>
            _ready && PortalConfig.Enabled != null && PortalConfig.Enabled.Value &&
            PortalConfig.UniversalRouting != null && PortalConfig.UniversalRouting.Value;

        public bool RuntimeReady => _ready;
        public bool LocalHostMutationAvailable => FeatureEnabled && ValheimContracts.HasLocalPlayerAuthority;
        public bool DedicatedMutationTransportAvailable => false;
        public int IndexedEndpointCount => _graph.Count;
        public string DisabledReason => _disabledReason;

        internal void Initialize()
        {
            _ready = true;
            _disabledReason = string.Empty;
            _nextReturnPrune = 0f;
            _index.MarkDirty();
            InitializeMapOverlay();
            InitializeHoverPanel();
            _diagnostics.Record(PortalDiagnosticCode.RuntimeReady);
        }

        internal void Shutdown()
        {
            CancelCurrentEdit();
            ShutdownMapPicker();
            ShutdownDirectorySyncTransport();
            ShutdownMapOverlay();
            PortalMapMarkerSprite.Shutdown();
            _ready = false;
            _disabledReason = "runtime-shutdown";
            _oneWay.Clear();
            _arrivalSuppression.Clear();
            _selections.Clear();
            _instancePortalIds.Clear();
            _hoverCache.Clear();
            _index.Clear();
            ShutdownHoverPanel();
            _diagnostics.Record(PortalDiagnosticCode.RuntimeDisabled, RouteStopCode.FeatureDisabled);
        }

        internal void OnConfigurationChanged()
        {
            if (!FeatureEnabled)
            {
                CleanupDisabledFeatureState();
                return;
            }
            _graph.SetMaximumEndpoints(CurrentMaximumEndpoints());
            _index.MarkDirty();
            _hoverCache.Clear();
            RefreshHoverPanelConfiguration();
            if (_mapOverlay == null)
            {
                InitializeMapOverlay();
            }
        }

        internal void EnsureDisabledStateAfterConfigurationFault()
        {
            if (!FeatureEnabled) CleanupDisabledFeatureState();
        }

        private void CleanupDisabledFeatureState()
        {
            CancelCurrentEdit();
            CancelMapPicker(string.Empty, true);
            ShutdownMapOverlay();
            _oneWay.Clear();
            _arrivalSuppression.Clear();
            _selections.Clear();
        }

        internal void Tick()
        {
            if (!_ready) return;
            if (!FeatureEnabled)
            {
                CleanupDisabledFeatureState();
                return;
            }
            TickDirectorySyncTransport();
            TickMapPicker();
            float interval = PortalConfig.IndexRefreshSeconds?.Value ?? 2f;
            float realtime = Time.realtimeSinceStartup;
            if (_index.Tick(realtime, interval)) _hoverCache.Clear();
            TickMapOverlay(realtime, interval);
            if (realtime < _nextReturnPrune) return;
            _nextReturnPrune = realtime + 5f;
            _returns.Prune(DateTime.UtcNow.Ticks);
        }

        internal void Observe(TeleportWorld portal)
        {
            if (portal == null) return;
            ZNetView view = portal.GetComponent<ZNetView>();
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null || zdo.m_uid.IsNone()) return;
            RememberPortal(portal.GetInstanceID(), zdo.m_uid.ToString());
            _index.MarkDirty();
        }

        internal bool IsNetworkPortal(TeleportWorld portal)
        {
            if (!FeatureEnabled || portal == null) return false;
            ZNetView view = portal.GetComponent<ZNetView>();
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            return zdo != null && zdo.IsValid() && !zdo.m_uid.IsNone() &&
                   PortalZdoCodec.GetMode(zdo) == (int)PortalMode.Network;
        }

        internal bool HasSelectedTarget(TeleportWorld portal)
        {
            if (!IsNetworkPortal(portal) || Player.m_localPlayer == null) return false;
            string traveler = PortalPermissionAdapter.Identity(Player.m_localPlayer.GetPlayerID());
            if (!TryGetPortalId(portal, out string sourceId) ||
                !_selections.TryGet(traveler, sourceId, out PortalSelection selection)) return false;
            return _graph.TryGetEndpoint(
                       selection.DestinationPortalId, out PortalEndpoint destination) &&
                   destination.OnlineState == PortalOnlineState.Online;
        }

        internal bool TryGetHoverText(TeleportWorld portal, out string text)
        {
            text = null;
            if (!TryGetHoverPanelState(portal, out PortalHoverPanelState panelState) ||
                !panelState.IsNetwork) return false;
            int instanceId = portal.GetInstanceID();
            if (!panelState.DetailsVisible)
            {
                _hoverCache.Remove(instanceId);
                text = "Runic Portal: Restricted Network Portal\n" +
                       "Details hidden by current permissions";
                return true;
            }
            if (_hoverCache.TryGetValue(instanceId, out text)) return true;
            string selected = panelState.SelectedDestination.Length == 0
                ? "none"
                : panelState.SelectedDestination;
            text = "Runic Portal: " + panelState.DisplayName + " [" + panelState.NetworkId + "]\n" +
                   "Selected: " + selected;
            _hoverCache[instanceId] = text;
            return true;
        }

        private static bool TryReadVisibleEndpoint(
            TeleportWorld portal,
            out PortalEndpoint endpoint)
        {
            endpoint = null;
            if (portal == null) return false;
            ZNetView view = portal.GetComponent<ZNetView>();
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            return zdo != null && zdo.IsValid() && !zdo.m_uid.IsNone() &&
                   PortalZdoCodec.TryRead(zdo, out endpoint, out _);
        }

        internal bool TryHandleInteract(
            TeleportWorld portal,
            Humanoid human,
            bool hold,
            bool alt,
            out bool result)
        {
            result = false;
            if (!FeatureEnabled || portal == null || hold) return false;
            Player actor = human as Player;
            bool network = IsNetworkPortal(portal);
            if (!network && !alt)
            {
                CancelEditFor(portal);
                return false;
            }
            if (actor == null)
            {
                result = false;
                return true;
            }
            if (network && alt)
            {
                result = TryCycle(portal, actor);
                return true;
            }
            result = TryBeginEdit(portal, actor);
            return true;
        }

        internal bool TryConsumeSetText(TeleportWorld portal, string text)
        {
            if (_edit == null || portal == null || _edit.InstanceId != portal.GetInstanceID()) return false;
            EditSession session = _edit;
            _edit = null;
            string confirmationTarget = PortalEditEvidence.OpaqueTarget(session.PortalId.ToString());
            Player actor = Player.m_localPlayer;
            if (actor == null || actor.GetPlayerID() != session.ActorId)
            {
                _confirmations.Cancel(confirmationTarget);
                Message(actor, "Runic portal edit cancelled: actor identity changed.");
                RejectEdit(portal, AuthorityStopCode.SenderIdentityUnbound);
                return true;
            }
            PortalEditCommand command = PortalEditCommand.Parse(text);
            if (command.Kind == PortalEditKind.Invalid)
            {
                _confirmations.Cancel(confirmationTarget);
                Message(actor, "Runic portal edit cancelled: " + command.Error);
                _diagnostics.Record(PortalDiagnosticCode.EditRejected, RouteStopCode.StaleSelection,
                    session.PortalId.ToString());
                return true;
            }
            if (!TryBindAndAuthorizeGroupEdit(
                    actor,
                    command,
                    out command,
                    out string groupFailure))
            {
                _confirmations.Cancel(confirmationTarget);
                Message(actor, "Runic portal edit cancelled: " + groupFailure + ".");
                RejectEdit(portal, AuthorityStopCode.OwnerPermissionDenied);
                return true;
            }
            if (!TryRevalidateEdit(
                    session,
                    portal,
                    actor,
                    out PortalEditEvidence observed,
                    out ZDO zdo,
                    out long creator,
                    out AuthorityStopCode stop,
                    out string reason))
            {
                _confirmations.Cancel(confirmationTarget);
                Message(actor, "Runic portal edit cancelled: " + reason + ".");
                RejectEdit(portal, stop);
                return true;
            }
            if (command.Kind == PortalEditKind.PublicNetwork &&
                zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) != ZDOID.None)
            {
                _confirmations.Cancel(confirmationTarget);
                Message(actor, "Unlink this Standard Pair with a unique vanilla tag before opting into a network.");
                RejectEdit(portal, AuthorityStopCode.CurrentStateChanged);
                return true;
            }
            if (observed.Matches(command, creator))
            {
                _confirmations.Cancel(confirmationTarget);
                Message(actor, "Portal metadata already matches; no world state was changed.");
                return true;
            }

            string confirmationFingerprint = observed.Fingerprint(command, creator);
            PortalConfirmationAdmission confirmation = _confirmations.Request(
                observed.RequiresConfirmation(command, creator),
                confirmationTarget,
                confirmationFingerprint);
            if (confirmation == PortalConfirmationAdmission.Pending)
            {
                Message(actor, "Repeat the identical portal overwrite to confirm it.");
                return true;
            }
            if (confirmation == PortalConfirmationAdmission.Denied ||
                confirmation == PortalConfirmationAdmission.Unavailable)
            {
                _confirmations.Cancel(confirmationTarget);
                Message(actor, confirmation == PortalConfirmationAdmission.Unavailable
                    ? "Runic portal edit denied: configured Safety confirmation is unavailable or incompatible."
                    : "Runic portal edit denied by the current Safety confirmation policy.");
                RejectEdit(portal, AuthorityStopCode.CurrentStateChanged);
                return true;
            }

            // A synchronous provider is still third-party code. Rebuild all authority, ward,
            // range, ownership, identity, schema, revision, and exact metadata evidence after it
            // returns and immediately before the ZDO commit.
            if (!TryRevalidateEdit(
                    session,
                    portal,
                    actor,
                    out PortalEditEvidence commitEvidence,
                    out zdo,
                    out creator,
                    out stop,
                    out reason) ||
                !commitEvidence.Matches(observed) ||
                !string.Equals(
                    commitEvidence.Fingerprint(command, creator),
                    confirmationFingerprint,
                    StringComparison.Ordinal) ||
                confirmation != PortalConfirmationAdmission.NotRequired &&
                !_confirmations.RequesterIsActive())
            {
                _confirmations.Cancel(confirmationTarget);
                Message(actor, "Runic portal edit cancelled: authority or metadata changed after confirmation.");
                RejectEdit(portal, stop);
                return true;
            }
            if (command.Kind == PortalEditKind.PublicNetwork &&
                zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) != ZDOID.None)
            {
                _confirmations.Cancel(confirmationTarget);
                Message(actor, "Runic portal edit cancelled: the Standard Pair connection changed.");
                RejectEdit(portal, AuthorityStopCode.CurrentStateChanged);
                return true;
            }
            if (!TryAuthorizeBoundGroupEdit(actor, command, out groupFailure))
            {
                _confirmations.Cancel(confirmationTarget);
                Message(actor, "Runic portal edit cancelled: " + groupFailure + ".");
                RejectEdit(portal, AuthorityStopCode.OwnerPermissionDenied);
                return true;
            }
            if (!PortalZdoCodec.TryWrite(zdo, command, creator, out _, out string writeFailure))
            {
                _confirmations.Cancel(confirmationTarget);
                Message(actor, "Runic portal edit failed closed: " + writeFailure + ".");
                RejectEdit(portal, AuthorityStopCode.CurrentStateChanged);
                return true;
            }
            _confirmations.Cancel(confirmationTarget);
            _index.MarkDirty();
            _index.Rebuild(Time.realtimeSinceStartup, PortalConfig.IndexRefreshSeconds?.Value ?? 2f);
            _hoverCache.Remove(portal.GetInstanceID());
            _selections.Clear();
            _oneWay.Clear();
            _diagnostics.Record(PortalDiagnosticCode.EditAccepted, RouteStopCode.Ready,
                zdo.m_uid.ToString());
            if (command.Kind == PortalEditKind.PublicNetwork)
                NoteMapNetworkUsed(command.NetworkId);
            Message(actor, command.Kind == PortalEditKind.PublicNetwork
                ? PolicyLabel(command.NetworkKind) +
                  " network portal saved. Walk into it, then click an authorized destination on the map."
                : "Portal restored to Standard Pair mode; set its vanilla tag normally.");
            return true;
        }

        private bool TryBindAndAuthorizeGroupEdit(
            Player actor,
            PortalEditCommand command,
            out PortalEditCommand bound,
            out string failure)
        {
            bound = command;
            failure = string.Empty;
            if (command == null || command.NetworkKind != PortalNetworkKind.Group)
                return command != null;
            long playerId = actor == null ? 0L : actor.GetPlayerID();
            if (playerId <= 0L)
            {
                failure = "your stable player identity is unavailable";
                return false;
            }
            if (command.RequiresActiveGroup)
            {
                if (!_groups.TryGetActive(playerId, out string groupId, out _))
                {
                    failure = "no active Group is selected; open chat and use /group list, then /group use <group name>";
                    return false;
                }
                bound = command.BindGroup(groupId);
                if (bound.Kind == PortalEditKind.Invalid)
                {
                    failure = bound.Error;
                    return false;
                }
            }
            return TryAuthorizeBoundGroupEdit(actor, bound, out failure);
        }

        private bool TryAuthorizeBoundGroupEdit(
            Player actor,
            PortalEditCommand command,
            out string failure)
        {
            failure = string.Empty;
            if (command == null || command.NetworkKind != PortalNetworkKind.Group) return true;
            long playerId = actor == null ? 0L : actor.GetPlayerID();
            if (playerId <= 0L)
            {
                failure = "your stable player identity is unavailable";
                return false;
            }
            if (!_groups.TryGetActive(playerId, out string activeGroup, out _) ||
                !string.Equals(activeGroup, command.GroupId, StringComparison.Ordinal))
            {
                failure = "the portal Group is not your current active Group";
                return false;
            }
            if (_groups.TryIsMember(command.GroupId, playerId, out bool member) && member)
                return true;
            failure = "the selected Group is unavailable or you are no longer a member";
            return false;
        }

        private void ConsumeEdit(long token, string text)
        {
            EditSession session = _edit;
            if (session == null || session.Token != token) return;
            TryConsumeSetText(session.Portal, text);
        }

        internal void OnTextInputHidden(TextInput input)
        {
            if (input != null) CancelCurrentEdit();
        }

        internal bool TryHandleTeleport(TeleportWorld portal, Player player, out bool handled)
        {
            handled = false;
            if (!FeatureEnabled || portal == null || player == null) return false;
            if (!IsNetworkPortal(portal))
            {
                if (!StandardPairTargetsNetwork(portal)) return false;
                handled = true;
                Message(player,
                    "This Standard Pair is linked to a Runic network portal. Give it a unique " +
                    "vanilla tag and pair it with another Standard portal.");
                RejectRoute(portal, RouteStopCode.DestinationUnavailable);
                return true;
            }
            handled = true;
            if (TryGetPortalId(portal, out string arrivalPortalId) &&
                _arrivalSuppression.Blocks(
                    player.GetPlayerID(), arrivalPortalId, DateTime.UtcNow.Ticks))
                return true;
            return TryOpenMapPicker(portal, player);
        }

        private bool TryCommitSelectedTeleport(
            TeleportWorld portal,
            Player player,
            bool oneWayAcknowledged,
            out bool handled)
        {
            handled = true;
            if (!ValheimContracts.HasLocalPlayerAuthority || player != Player.m_localPlayer)
            {
                Message(player, "Portal travel requires native ownership of the local player.");
                RejectRoute(portal, RouteStopCode.AuthorityUnavailable);
                return true;
            }
            _index.MarkDirty();
            if (!_index.Rebuild(Time.realtimeSinceStartup, PortalConfig.IndexRefreshSeconds?.Value ?? 2f) ||
                !TryGetPortalId(portal, out string sourceId) ||
                !_graph.TryGetEndpoint(sourceId, out PortalEndpoint source))
            {
                Message(player, "Portal route cancelled: the server graph is unavailable.");
                RejectRoute(portal, RouteStopCode.SourceUnavailable);
                return true;
            }
            string traveler = PortalPermissionAdapter.Identity(player.GetPlayerID());
            if (!_selections.TryGet(traveler, sourceId, out PortalSelection selection))
            {
                Message(player, "No destination selected. Walk into the portal and click a destination marker on the map.");
                RejectRoute(portal, RouteStopCode.StaleSelection);
                return true;
            }
            TravelPolicyState travelPolicy = CurrentTravelPolicy(portal, player);
            var request = new RoutePlanRequest(
                traveler,
                sourceId,
                selection.DestinationPortalId,
                travelPolicy,
                false,
                selection.SourceRevision,
                selection.DestinationRevision);
            RoutePlan plan = _graph.Plan(request);
            if (plan.StopCode == RouteStopCode.OneWayWarningRequired)
            {
                if (!oneWayAcknowledged)
                {
                    long now = DateTime.UtcNow.Ticks;
                    long duration = TimeSpan.FromSeconds(
                        PortalConfig.OneWayAcknowledgementSeconds?.Value ?? 10).Ticks;
                    if (!_oneWay.ConsumeOrArm(
                            traveler, sourceId, selection.DestinationPortalId, now, duration))
                    {
                        Message(player, "This destination is one-way. Walk into the portal and click its map marker to acknowledge and travel.");
                        RejectRoute(portal, RouteStopCode.OneWayWarningRequired);
                        return true;
                    }
                }
                request = new RoutePlanRequest(
                    traveler, sourceId, selection.DestinationPortalId, travelPolicy, true,
                    selection.SourceRevision, selection.DestinationRevision);
                plan = _graph.Plan(request);
            }
            else
            {
                _oneWay.Clear();
            }
            if (!plan.IsReady)
            {
                Message(player, RouteMessage(plan.StopCode));
                RejectRoute(portal, plan.StopCode);
                return true;
            }
            if (!_index.TryGetZdo(sourceId, out ZDO sourceZdo) ||
                !_index.TryGetZdo(selection.DestinationPortalId, out ZDO destinationZdo) ||
                !_graph.TryGetEndpoint(selection.DestinationPortalId, out PortalEndpoint destination))
            {
                Message(player, "Portal route cancelled: an endpoint changed after selection.");
                RejectRoute(portal, RouteStopCode.DestinationUnavailable);
                return true;
            }

            WardContext sourceWard = ValheimContracts.ResolveWard(sourceZdo.GetPosition(), player.GetPlayerID());
            WardContext destinationWard = ValheimContracts.ResolveWard(destinationZdo.GetPosition(), player.GetPlayerID());
            PortalEndpoint currentSource = null;
            PortalEndpoint currentDestination = null;
            bool metadataMatches = PortalZdoCodec.TryRead(sourceZdo,
                                       out currentSource, out _) &&
                                   PortalZdoCodec.TryRead(destinationZdo,
                                       out currentDestination, out _) &&
                                   SameRouteState(source, currentSource) &&
                                   SameRouteState(destination, currentDestination);
            PortalRoutePermissionEvidence routePermissions =
                PortalRoutePermissionEvidence.Evaluate(
                    _permissions,
                    currentSource ?? source,
                    currentDestination ?? destination,
                    traveler,
                    ValheimContracts.WardAllows(sourceWard),
                    ValheimContracts.DestinationWardAllows(destinationWard));
            ZNetView playerView = player.GetComponent<ZNetView>();
            ZNetView sourceView = portal.GetComponent<ZNetView>();
            ZDO observedSource = sourceView != null && sourceView.IsValid() ? sourceView.GetZDO() : null;
            var evidence = new PortalAuthorityEvidence(
                PortalMutationKind.CommitTravel,
                FeatureEnabled,
                ValheimContracts.IsServer,
                false,
                false,
                player == Player.m_localPlayer && player.GetPlayerID() != 0L,
                player == Player.m_localPlayer,
                sourceZdo.IsValid(),
                destinationZdo.IsValid(),
                false,
                playerView != null && playerView.IsValid() && playerView.IsOwner(),
                routePermissions.PolicyAllowed,
                routePermissions.SourceWardAllowed,
                routePermissions.DestinationWardAllowed,
                InRange(player.transform.position, portal.transform.position, CurrentEditRange()),
                metadataMatches && observedSource == sourceZdo &&
                PortalZdoCodec.GetRevision(sourceZdo) == plan.SourceRevision &&
                PortalZdoCodec.GetRevision(destinationZdo) == plan.DestinationRevision &&
                ZDOMan.instance != null &&
                ZDOMan.instance.GetZDO(sourceZdo.m_uid) == sourceZdo &&
                ZDOMan.instance.GetZDO(destinationZdo.m_uid) == destinationZdo,
                travelPolicy == TravelPolicyState.Allowed);
            PortalAuthorityDecision authority = _authority.Evaluate(evidence);
            if (!authority.IsAllowed)
            {
                Message(player, "Portal route denied: " + AuthorityLabel(authority.StopCode) + ".");
                _diagnostics.Record(PortalDiagnosticCode.AuthorityRejected,
                    RouteStopCode.AuthorityUnavailable, sourceId);
                return true;
            }

            if (!PortalTravelTransformPolicy.TryResolve(
                    destinationZdo.GetPosition(),
                    destinationZdo.GetRotation(),
                    portal.m_exitDistance,
                    out Vector3 position,
                    out Quaternion rotation))
            {
                Message(player, "Portal route cancelled: the destination transform is invalid.");
                _diagnostics.Record(PortalDiagnosticCode.RouteRejected,
                    RouteStopCode.TeleportRejected, selection.DestinationPortalId);
                return true;
            }
            _arrivalSuppression.Arm(
                player.GetPlayerID(),
                destination.PortalId,
                DateTime.UtcNow.Ticks,
                TimeSpan.FromSeconds(10).Ticks);
            if (!player.TeleportTo(position, rotation, true))
            {
                _arrivalSuppression.Clear();
                Message(player, "Portal route cancelled: Valheim rejected the teleport state.");
                RejectRoute(portal, RouteStopCode.TeleportRejected);
                return true;
            }
            Game.instance?.IncrementPlayerStat(PlayerStatType.PortalsUsed);
            long expiry = checked(DateTime.UtcNow.Ticks +
                TimeSpan.FromMinutes(PortalConfig.ReturnRouteMinutes?.Value ?? 15).Ticks);
            _returns.Record(new ReturnRoute(
                    traveler,
                    sourceId,
                    destination.PortalId,
                    source.Revision,
                    destination.Revision,
                    expiry), DateTime.UtcNow.Ticks);
            _diagnostics.Record(PortalDiagnosticCode.RouteCommitted, RouteStopCode.Ready, sourceId);
            return true;
        }

        public PortalDirectoryResult Query(PortalDirectoryQuery query)
        {
            if (!FeatureEnabled)
                return new PortalDirectoryResult(Array.Empty<PortalDirectoryEntry>(), false,
                    RouteStopCode.FeatureDisabled);
            return _graph.Query(query);
        }

        public PortalNameResolution ResolveName(PortalDirectoryQuery scope, string displayName)
        {
            if (!FeatureEnabled)
                return new PortalNameResolution(RouteStopCode.FeatureDisabled,
                    Array.Empty<PortalDirectoryEntry>());
            return _graph.ResolveName(scope, displayName);
        }

        public RoutePlan Plan(RoutePlanRequest request)
        {
            if (!FeatureEnabled)
                return new RoutePlan(RouteStopCode.FeatureDisabled,
                    request?.SourcePortalId, request?.DestinationPortalId, false, -1, -1);
            return _graph.Plan(request);
        }

        private bool TryCycle(TeleportWorld portal, Player actor)
        {
            if (!ValheimContracts.HasLocalPlayerAuthority || actor != Player.m_localPlayer)
            {
                Message(actor, "Portal selection requires native ownership of the local player.");
                RejectRoute(portal, RouteStopCode.AuthorityUnavailable);
                return true;
            }
            WardContext sourceWard = ValheimContracts.ResolveWard(
                portal.transform.position,
                actor.GetPlayerID());
            if (!ValheimContracts.WardAllows(sourceWard))
            {
                Message(actor, "Portal selection denied: ward access was denied at this portal.");
                RejectRoute(portal, RouteStopCode.AuthorityUnavailable);
                return true;
            }
            _index.MarkDirty();
            if (!_index.Rebuild(Time.realtimeSinceStartup, PortalConfig.IndexRefreshSeconds?.Value ?? 2f) ||
                !TryGetPortalId(portal, out string sourceId) ||
                !_graph.TryGetEndpoint(sourceId, out PortalEndpoint source))
            {
                Message(actor, "Portal directory unavailable.");
                return true;
            }
            string traveler = PortalPermissionAdapter.Identity(actor.GetPlayerID());
            var query = new PortalDirectoryQuery(
                traveler,
                sourceId,
                source.NetworkId,
                string.Empty,
                PortalConfig.DirectoryPageSize?.Value ?? 32,
                false);
            PortalDirectoryResult directory = _graph.Query(query);
            if (directory.StopCode != RouteStopCode.Ready)
            {
                Message(actor, RouteMessage(directory.StopCode));
                return true;
            }
            var candidates = new List<CycleCandidate>(directory.Entries.Count + 1);
            if (_returns.TryGet(traveler, sourceId, DateTime.UtcNow.Ticks, out ReturnRoute returnRoute))
            {
                if (!_graph.TryGetEndpoint(returnRoute.OriginPortalId, out PortalEndpoint returnEndpoint) ||
                    !_index.TryGetZdo(returnEndpoint.PortalId, out ZDO returnZdo) ||
                    !ValheimContracts.DestinationWardAllows(ValheimContracts.ResolveWard(
                        returnZdo.GetPosition(), actor.GetPlayerID())) ||
                    returnRoute.OriginRevision != returnEndpoint.Revision ||
                    returnRoute.ArrivalRevision != source.Revision)
                    _returns.Remove(traveler);
                else
                {
                    RoutePlan returnPlan = _graph.Plan(new RoutePlanRequest(
                        traveler, sourceId, returnEndpoint.PortalId, TravelPolicyState.Allowed, true,
                        source.Revision, returnEndpoint.Revision));
                    if (returnPlan.IsReady)
                        candidates.Add(new CycleCandidate
                        {
                            PortalId = returnEndpoint.PortalId,
                            Label = "Return: " + returnEndpoint.DisplayName,
                            DisplayName = returnEndpoint.DisplayName,
                            SourceRevision = source.Revision,
                            DestinationRevision = returnEndpoint.Revision,
                            IsReturn = true
                        });
                    else
                        _returns.Remove(traveler);
                }
            }
            for (int index = 0; index < directory.Entries.Count; index++)
            {
                PortalDirectoryEntry entry = directory.Entries[index];
                bool alreadyReturn = candidates.Count > 0 &&
                    string.Equals(candidates[0].PortalId, entry.PortalId, StringComparison.Ordinal);
                if (alreadyReturn ||
                    !_graph.TryGetEndpoint(entry.PortalId, out PortalEndpoint endpoint) ||
                    !_index.TryGetZdo(endpoint.PortalId, out ZDO destinationZdo) ||
                    !ValheimContracts.DestinationWardAllows(ValheimContracts.ResolveWard(
                        destinationZdo.GetPosition(), actor.GetPlayerID()))) continue;
                candidates.Add(new CycleCandidate
                {
                    PortalId = entry.PortalId,
                    Label = entry.DisplayName +
                            (entry.DuplicateName ? " [" + entry.Disambiguator + "]" : string.Empty),
                    DisplayName = entry.DisplayName,
                    SourceRevision = source.Revision,
                    DestinationRevision = endpoint.Revision,
                    IsReturn = false
                });
            }
            if (candidates.Count == 0)
            {
                Message(actor, "No authorized online destinations are available in this network.");
                return true;
            }
            int next = 0;
            if (_selections.TryGet(traveler, sourceId, out PortalSelection current))
            {
                for (int index = 0; index < candidates.Count; index++)
                {
                    if (!string.Equals(candidates[index].PortalId, current.DestinationPortalId,
                            StringComparison.Ordinal) || candidates[index].IsReturn != current.IsReturn) continue;
                    next = (index + 1) % candidates.Count;
                    break;
                }
            }
            CycleCandidate selected = candidates[next];
            _selections.Set(new PortalSelection(
                traveler, sourceId, selected.PortalId, selected.SourceRevision,
                selected.DestinationRevision, selected.IsReturn, DateTime.UtcNow.Ticks,
                selected.DisplayName));
            _hoverCache.Remove(portal.GetInstanceID());
            _oneWay.Clear();
            _diagnostics.Record(PortalDiagnosticCode.RouteSelected, RouteStopCode.Ready, sourceId);
            Message(actor, "Destination selected: " + selected.Label + ".");
            return true;
        }

        private bool TryBeginEdit(TeleportWorld portal, Player actor)
        {
            if (!TryConfigureEvidence(portal, actor, out PortalAuthorityEvidence evidence,
                    out ZDO zdo, out _, out string reason))
            {
                Message(actor, "Runic portal editor unavailable: " + reason + ".");
                return true;
            }
            PortalAuthorityDecision decision = _authority.Evaluate(evidence);
            if (!decision.IsAllowed)
            {
                Message(actor, "Runic portal editor denied: " + AuthorityLabel(decision.StopCode) + ".");
                RejectEdit(portal, decision.StopCode);
                return true;
            }
            if (PortalZdoCodec.GetMode(zdo) != (int)PortalMode.Network &&
                zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) != ZDOID.None)
            {
                Message(actor, "Unlink this Standard Pair with a unique vanilla tag before opting into a network.");
                return true;
            }
            if (TextInput.instance == null)
            {
                Message(actor, "Runic portal editor unavailable: text input is not ready.");
                return true;
            }
            long token = unchecked(++_editSequence);
            if (token == 0L) token = unchecked(++_editSequence);
            if (!PortalEditEvidence.TryCapture(zdo, out PortalEditEvidence editEvidence))
            {
                Message(actor, "Runic portal editor unavailable: current metadata evidence is invalid.");
                return true;
            }
            CancelCurrentEdit();
            _edit = new EditSession
            {
                Portal = portal,
                InstanceId = portal.GetInstanceID(),
                ActorId = actor.GetPlayerID(),
                PortalId = zdo.m_uid,
                Schema = PortalZdoCodec.GetSchema(zdo),
                Mode = PortalZdoCodec.GetMode(zdo),
                Revision = PortalZdoCodec.GetRevision(zdo),
                Evidence = editEvidence,
                Token = token
            };
            TextInput.instance.RequestText(new PortalEditReceiver(this, token),
                "network|public/private/group|... (or standard)", 256);
            return true;
        }

        private bool TryRevalidateEdit(
            EditSession session,
            TeleportWorld portal,
            Player actor,
            out PortalEditEvidence current,
            out ZDO zdo,
            out long creator,
            out AuthorityStopCode stop,
            out string reason)
        {
            current = default;
            zdo = null;
            creator = 0L;
            stop = AuthorityStopCode.CurrentStateChanged;
            reason = "current portal evidence is unavailable";
            if (session == null || portal == null || actor == null) return false;
            if (actor.GetPlayerID() != session.ActorId)
            {
                stop = AuthorityStopCode.SenderIdentityUnbound;
                reason = "actor identity changed";
                return false;
            }
            if (!TryConfigureEvidence(
                    portal,
                    actor,
                    out PortalAuthorityEvidence evidence,
                    out zdo,
                    out creator,
                    out reason))
                return false;
            PortalAuthorityDecision decision = _authority.Evaluate(evidence);
            if (!decision.IsAllowed)
            {
                stop = decision.StopCode;
                reason = AuthorityLabel(decision.StopCode);
                return false;
            }
            if (zdo.m_uid != session.PortalId)
            {
                reason = "portal identity changed";
                return false;
            }
            if (!PortalEditEvidence.TryCapture(zdo, out current))
            {
                reason = "portal metadata is invalid or unsupported";
                return false;
            }
            if (current.Schema != session.Schema ||
                current.Mode != session.Mode ||
                current.Revision != session.Revision ||
                !current.Matches(session.Evidence))
            {
                reason = "portal metadata changed while the editor was open";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        private bool TryConfigureEvidence(
            TeleportWorld portal,
            Player actor,
            out PortalAuthorityEvidence evidence,
            out ZDO zdo,
            out long creator,
            out string reason)
        {
            evidence = null;
            zdo = null;
            creator = 0L;
            reason = "authority context is incomplete";
            if (portal == null || actor == null) return false;
            ZNetView view = portal.GetComponent<ZNetView>();
            Piece piece = portal.GetComponent<Piece>() ?? portal.GetComponentInParent<Piece>();
            zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            creator = piece == null ? 0L : piece.GetCreator();
            if (zdo == null || zdo.m_uid.IsNone() || creator == 0L)
            {
                reason = "portal owner or stable object identity is missing";
                return false;
            }
            int schema = PortalZdoCodec.GetSchema(zdo);
            int mode = PortalZdoCodec.GetMode(zdo);
            bool knownMode = mode == (int)PortalMode.StandardPair || mode == (int)PortalMode.Network;
            bool schemaCurrent = schema == 0 && mode == (int)PortalMode.StandardPair ||
                                 schema == PortalZdoCodec.SchemaVersion;
            if (!knownMode || !schemaCurrent)
            {
                reason = "portal metadata mode or schema is unsupported";
                return false;
            }
            string actorStable = PortalPermissionAdapter.Identity(actor.GetPlayerID());
            PortalEndpoint permissionEndpoint;
            if (mode == (int)PortalMode.Network)
            {
                if (!PortalZdoCodec.TryRead(zdo, out PortalEndpoint currentEndpoint, out _))
                {
                    reason = "portal metadata is invalid or uses an unsupported schema";
                    return false;
                }
                // Piece creator is authoritative for editing; metadata owner is descriptive and cannot grant control.
                permissionEndpoint = new PortalEndpoint(
                    currentEndpoint.PortalId,
                    currentEndpoint.Mode,
                    currentEndpoint.DisplayName,
                    currentEndpoint.NetworkId,
                    currentEndpoint.NetworkKind,
                    PortalPermissionAdapter.Identity(creator),
                    currentEndpoint.OwnerDisplayName,
                    currentEndpoint.OnlineState,
                    currentEndpoint.AcceptsArrival,
                    currentEndpoint.PermitsDeparture,
                    currentEndpoint.Access,
                    currentEndpoint.Revision,
                    currentEndpoint.KnownBiome);
            }
            else
            {
                permissionEndpoint = new PortalEndpoint(
                    zdo.m_uid.ToString(),
                    PortalMode.StandardPair,
                    string.Empty,
                    string.Empty,
                    PortalNetworkKind.Custom,
                    PortalPermissionAdapter.Identity(creator),
                    string.Empty,
                    PortalOnlineState.Online,
                    true,
                    true,
                    PortalAccessProfile.PublicNetwork,
                    Math.Max(0, PortalZdoCodec.GetRevision(zdo)));
            }
            WardContext ward = ValheimContracts.ResolveWard(portal.transform.position, actor.GetPlayerID());
            bool permission = _permissions.Allows(
                permissionEndpoint, actorStable, PortalAccessAction.Edit);
            evidence = new PortalAuthorityEvidence(
                PortalMutationKind.ConfigureMetadata,
                FeatureEnabled,
                ValheimContracts.IsServer,
                false,
                false,
                actor == Player.m_localPlayer && actor.GetPlayerID() != 0L,
                actor == Player.m_localPlayer,
                zdo.IsValid(),
                true,
                view.IsOwner(),
                false,
                permission,
                ValheimContracts.WardAllows(ward),
                true,
                InRange(actor.transform.position, portal.transform.position, CurrentEditRange()),
                schemaCurrent && ZDOMan.instance != null && ZDOMan.instance.GetZDO(zdo.m_uid) == zdo,
                true);
            reason = string.Empty;
            return true;
        }

        private bool HasVisibleDuplicate(PortalEndpoint source, string traveler, string name)
        {
            var scope = new PortalDirectoryQuery(
                traveler, source.PortalId, source.NetworkId, string.Empty,
                PortalContractLimits.MaximumDirectoryResults, false);
            RouteStopCode stop = _graph.ResolveName(scope, name).StopCode;
            return stop == RouteStopCode.DuplicateName || stop == RouteStopCode.GraphLimitExceeded;
        }

        private bool TryGetPortalId(TeleportWorld portal, out string portalId)
        {
            portalId = null;
            if (portal == null) return false;
            int instanceId = portal.GetInstanceID();
            if (_instancePortalIds.TryGetValue(instanceId, out portalId)) return true;
            ZNetView view = portal.GetComponent<ZNetView>();
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null || zdo.m_uid.IsNone()) return false;
            portalId = zdo.m_uid.ToString();
            RememberPortal(instanceId, portalId);
            return true;
        }

        private void RememberPortal(int instanceId, string portalId)
        {
            if (!_instancePortalIds.ContainsKey(instanceId) &&
                _instancePortalIds.Count >= ValheimContracts.MaximumPortalObjectsScanned)
            {
                _instancePortalIds.Clear();
                _hoverCache.Clear();
            }
            _instancePortalIds[instanceId] = portalId;
            _hoverCache.Remove(instanceId);
        }

        private void CancelEditFor(TeleportWorld portal)
        {
            if (_edit != null && portal != null && _edit.InstanceId == portal.GetInstanceID())
                CancelCurrentEdit();
        }

        private void CancelCurrentEdit()
        {
            EditSession session = _edit;
            _edit = null;
            if (session != null)
                _confirmations.Cancel(PortalEditEvidence.OpaqueTarget(session.PortalId.ToString()));
        }

        private void RejectEdit(TeleportWorld portal, AuthorityStopCode stop)
        {
            string portalId = TryGetPortalId(portal, out string value) ? value : string.Empty;
            _diagnostics.Record(PortalDiagnosticCode.EditRejected,
                RouteStopCode.AuthorityUnavailable, portalId);
            Diagnostics.Trace("Portal edit rejected: " + stop + ".");
        }

        private void RejectRoute(TeleportWorld portal, RouteStopCode stop)
        {
            string portalId = TryGetPortalId(portal, out string value) ? value : string.Empty;
            _diagnostics.Record(PortalDiagnosticCode.RouteRejected, stop, portalId);
            Diagnostics.Trace("Portal route rejected: " + stop + ".");
        }

        private static TravelPolicyState CurrentTravelPolicy(TeleportWorld portal, Player player)
        {
            if (ZoneSystem.instance == null) return TravelPolicyState.Unknown;
            if (ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoPortals))
                return TravelPolicyState.PortalsDisabled;
            if (ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBossPortals) &&
                (RandEventSystem.instance?.GetBossEvent() != null ||
                 ZoneSystem.instance.GetGlobalKey(GlobalKeys.activeBosses, out float activeBosses) &&
                 activeBosses > 0f))
                return TravelPolicyState.BossTravelBlocked;
            if (!portal.m_allowAllItems && !player.IsTeleportable())
                return TravelPolicyState.RestrictedItems;
            return TravelPolicyState.Allowed;
        }

        private static bool InRange(Vector3 actor, Vector3 portal, float range) =>
            (actor - portal).sqrMagnitude <= range * range;

        private static bool SameRouteState(PortalEndpoint expected, PortalEndpoint current) =>
            expected != null && current != null &&
            string.Equals(expected.PortalId, current.PortalId, StringComparison.Ordinal) &&
            string.Equals(expected.DisplayName, current.DisplayName, StringComparison.Ordinal) &&
            string.Equals(expected.NetworkId, current.NetworkId, StringComparison.Ordinal) &&
            string.Equals(expected.OwnerStableId, current.OwnerStableId, StringComparison.Ordinal) &&
            expected.Mode == current.Mode &&
            expected.AcceptsArrival == current.AcceptsArrival &&
            expected.PermitsDeparture == current.PermitsDeparture &&
            expected.Revision == current.Revision;

        private static float CurrentEditRange() => PortalConfig.EditRangeMeters?.Value ?? 5f;
        private static int CurrentMaximumEndpoints() => PortalConfig.MaximumEndpoints?.Value ?? 1024;

        private static void Message(Player player, string text)
        {
            if (player != null && !string.IsNullOrEmpty(text))
                player.Message(MessageHud.MessageType.Center, text, 0, null);
        }

        private static string RouteMessage(RouteStopCode stop)
        {
            switch (stop)
            {
                case RouteStopCode.RestrictedItems: return "You cannot teleport with this inventory under the current world rules.";
                case RouteStopCode.PortalsDisabled: return "Portals are disabled by the current world rules.";
                case RouteStopCode.BossTravelBlocked: return "Portal travel is blocked by the active boss rule.";
                case RouteStopCode.DepartureDenied: return "You are not permitted to depart through this portal.";
                case RouteStopCode.ArrivalDenied: return "You are not permitted to arrive at that portal.";
                case RouteStopCode.DestinationUnavailable: return "The selected destination is no longer available.";
                case RouteStopCode.SourceUnavailable: return "This portal is no longer available.";
                case RouteStopCode.StaleSelection: return "The selected route changed; select a destination again.";
                default: return "Portal route cancelled: " + stop + ".";
            }
        }

        private static string AuthorityLabel(AuthorityStopCode stop)
        {
            switch (stop)
            {
                case AuthorityStopCode.ServerAuthorityMissing: return "server authority is missing";
                case AuthorityStopCode.DedicatedTransportUnavailable: return "authenticated dedicated transport is unavailable";
                case AuthorityStopCode.SenderIdentityUnbound: return "the sender identity is not bound";
                case AuthorityStopCode.ActorNotLocalAuthority: return "the actor is not locally authoritative";
                case AuthorityStopCode.SourceObjectNotOwned: return "the portal object is not owned by this authority";
                case AuthorityStopCode.PlayerObjectNotOwned: return "the player object is not owned by this authority";
                case AuthorityStopCode.OwnerPermissionDenied: return "portal permission was denied";
                case AuthorityStopCode.SourceWardDenied:
                case AuthorityStopCode.DestinationWardDenied: return "ward access was denied";
                case AuthorityStopCode.ActorOutOfRange: return "the actor is out of range";
                case AuthorityStopCode.CurrentStateChanged: return "the portal changed before commit";
                case AuthorityStopCode.VanillaTravelPolicyDenied: return "Valheim travel policy denied the route";
                default: return stop.ToString();
            }
        }
    }
}
