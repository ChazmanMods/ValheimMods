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
            internal string RawVanillaTag;
            internal ZDOID VanillaConnection;
            internal string PendingVanillaTag;
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
        private PortalEditorPanel _portalEditor;
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
            if (!Application.isBatchMode)
                _portalEditor = new PortalEditorPanel(
                    _groups,
                    SubmitEditorDraft,
                    CancelCurrentEdit);
            _diagnostics.Record(PortalDiagnosticCode.RuntimeReady);
        }

        internal void Shutdown()
        {
            CancelCurrentEdit();
            _portalEditor?.Dispose();
            _portalEditor = null;
            PortalEditorInputGuard.Reset();
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
            _portalEditor?.Tick();
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
                text = global::Runic.Localization.RunicText.Get("text_4fb7e55fd803") +
                       global::Runic.Localization.RunicText.Get("text_8e8c8bd55b5d");
                return true;
            }
            if (_hoverCache.TryGetValue(instanceId, out text)) return true;
            string selected = panelState.SelectedDestination.Length == 0
                ? "none"
                : panelState.SelectedDestination;
            text = global::Runic.Localization.RunicText.Get("text_8fed301cb760") + panelState.DisplayName + " [" + panelState.NetworkId + "]\n" +
                   global::Runic.Localization.RunicText.Get("text_1f153fd1824c") + selected;
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
            PortalEditorSubmitResult result = TryCommitEditorCommand(
                portal,
                PortalEditCommand.Parse(text));
            if (result.Message.Length != 0) Message(Player.m_localPlayer, result.Message);
            return true;
        }

        internal PortalEditorSubmitResult SubmitEditorDraft(PortalEditorDraft draft)
        {
            if (_edit == null || draft == null)
                return PortalEditorSubmitResult.Reject(global::Runic.Localization.RunicText.Get("text_819f01290e68"));
            PortalEditCommand command = draft.BuildCommand();
            return TryCommitEditorCommand(_edit.Portal, command);
        }

        internal void DrawPortalEditor() => _portalEditor?.Draw();

        private PortalEditorSubmitResult TryCommitEditorCommand(
            TeleportWorld portal,
            PortalEditCommand command)
        {
            EditSession session = _edit;
            if (session == null || portal == null ||
                session.InstanceId != portal.GetInstanceID())
                return PortalEditorSubmitResult.Reject(
                    global::Runic.Localization.RunicText.Get("text_a87ddde51dcd"));
            string confirmationTarget = PortalEditEvidence.OpaqueTarget(session.PortalId.ToString());
            Player actor = Player.m_localPlayer;
            if (actor == null || actor.GetPlayerID() != session.ActorId)
            {
                _confirmations.Cancel(confirmationTarget);
                RejectEdit(portal, AuthorityStopCode.SenderIdentityUnbound);
                CancelCurrentEdit();
                return PortalEditorSubmitResult.Reject(
                    global::Runic.Localization.RunicText.Get("text_780d3e7593cf"));
            }
            if (command == null || command.Kind == PortalEditKind.Invalid)
            {
                _confirmations.Cancel(confirmationTarget);
                _diagnostics.Record(PortalDiagnosticCode.EditRejected, RouteStopCode.StaleSelection,
                    session.PortalId.ToString());
                return PortalEditorSubmitResult.Reject(
                    command?.Error ?? global::Runic.Localization.RunicText.Get("text_fb78de4e74cb"));
            }
            if (!TryBindAndAuthorizeGroupEdit(
                    actor,
                    command,
                    out command,
                    out string groupFailure))
            {
                _confirmations.Cancel(confirmationTarget);
                RejectEdit(portal, AuthorityStopCode.OwnerPermissionDenied);
                return PortalEditorSubmitResult.Reject(
                    Sentence(global::Runic.Localization.RunicText.Get("text_730ea66cb20e") + groupFailure));
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
                RejectEdit(portal, stop);
                CancelCurrentEdit();
                return PortalEditorSubmitResult.Reject(
                    Sentence(global::Runic.Localization.RunicText.Get("text_2a626cc8e1f4") + reason));
            }
            if (command.Kind == PortalEditKind.PublicNetwork &&
                zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) != ZDOID.None)
            {
                _confirmations.Cancel(confirmationTarget);
                RejectEdit(portal, AuthorityStopCode.CurrentStateChanged);
                return PortalEditorSubmitResult.Reject(
                    global::Runic.Localization.RunicText.Get("text_147969ee347f"));
            }

            bool metadataMatches = observed.Matches(command, creator);
            bool vanillaTagMatches = command.Kind != PortalEditKind.StandardPair ||
                                     !command.HasVanillaTag ||
                                     string.Equals(
                                         RawVanillaTag(zdo),
                                         command.VanillaTag,
                                         StringComparison.Ordinal);
            if (metadataMatches && vanillaTagMatches)
            {
                _confirmations.Cancel(confirmationTarget);
                CancelCurrentEdit();
                return PortalEditorSubmitResult.Saved(
                    "These portal settings are already saved.");
            }

            // Set a Standard Pair tag through Valheim's native method before clearing Runic
            // metadata. Runic Safety can intentionally stop the first overwrite; observing the
            // exact raw tag keeps the dialog open for the player's second click without ever
            // bypassing that confirmation.
            if (command.Kind == PortalEditKind.StandardPair && command.HasVanillaTag &&
                !vanillaTagMatches)
            {
                session.PendingVanillaTag = command.VanillaTag;
                portal.SetText(command.VanillaTag);
                if (!string.Equals(
                        RawVanillaTag(zdo),
                        command.VanillaTag,
                        StringComparison.Ordinal))
                    return PortalEditorSubmitResult.Confirm(
                        "Confirm the vanilla tag overwrite by clicking Save again.");
                session.RawVanillaTag = command.VanillaTag;
                session.VanillaConnection = zdo.GetConnectionZDOID(
                    ZDOExtraData.ConnectionType.Portal);
                session.PendingVanillaTag = null;
                vanillaTagMatches = true;
                if (metadataMatches)
                {
                    FinishSuccessfulEdit(portal, zdo, command);
                    return PortalEditorSubmitResult.Saved(
                        "Standard Pair tag saved. Valheim will link it to one portal with the same tag.");
                }
            }

            string confirmationFingerprint = observed.Fingerprint(command, creator);
            PortalConfirmationAdmission confirmation = _confirmations.Request(
                observed.RequiresConfirmation(command, creator),
                confirmationTarget,
                confirmationFingerprint);
            if (confirmation == PortalConfirmationAdmission.Pending)
            {
                return PortalEditorSubmitResult.Confirm(
                    "This replaces existing Runic portal settings. Click Save again to confirm.");
            }
            if (confirmation == PortalConfirmationAdmission.Denied ||
                confirmation == PortalConfirmationAdmission.Unavailable)
            {
                _confirmations.Cancel(confirmationTarget);
                RejectEdit(portal, AuthorityStopCode.CurrentStateChanged);
                return PortalEditorSubmitResult.Reject(
                    confirmation == PortalConfirmationAdmission.Unavailable
                        ? global::Runic.Localization.RunicText.Get("text_da5796eed920")
                        : global::Runic.Localization.RunicText.Get("text_eff2b8438662"));
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
                RejectEdit(portal, stop);
                CancelCurrentEdit();
                return PortalEditorSubmitResult.Reject(
                    global::Runic.Localization.RunicText.Get("text_f68295b60b61"));
            }
            if (command.Kind == PortalEditKind.PublicNetwork &&
                zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) != ZDOID.None)
            {
                _confirmations.Cancel(confirmationTarget);
                RejectEdit(portal, AuthorityStopCode.CurrentStateChanged);
                return PortalEditorSubmitResult.Reject(
                    global::Runic.Localization.RunicText.Get("text_968d8051027b"));
            }
            if (!TryAuthorizeBoundGroupEdit(actor, command, out groupFailure))
            {
                _confirmations.Cancel(confirmationTarget);
                RejectEdit(portal, AuthorityStopCode.OwnerPermissionDenied);
                return PortalEditorSubmitResult.Reject(
                    Sentence(global::Runic.Localization.RunicText.Get("text_730ea66cb20e") + groupFailure));
            }
            if (!PortalZdoCodec.TryWrite(zdo, command, creator, out _, out string writeFailure))
            {
                _confirmations.Cancel(confirmationTarget);
                RejectEdit(portal, AuthorityStopCode.CurrentStateChanged);
                return PortalEditorSubmitResult.Reject(
                    Sentence(global::Runic.Localization.RunicText.Get("text_736510a6e168") + writeFailure));
            }
            _confirmations.Cancel(confirmationTarget);
            FinishSuccessfulEdit(portal, zdo, command);
            return PortalEditorSubmitResult.Saved(
                command.Kind == PortalEditKind.PublicNetwork
                    ? PolicyLabel(command.NetworkKind) +
                      " network portal saved. Walk into it and choose a destination on the map."
                    : "Standard Pair saved. Valheim will link it to one portal with the same tag.");
        }

        private void FinishSuccessfulEdit(
            TeleportWorld portal,
            ZDO zdo,
            PortalEditCommand command)
        {
            _index.MarkDirty();
            _index.Rebuild(Time.realtimeSinceStartup, PortalConfig.IndexRefreshSeconds?.Value ?? 2f);
            _hoverCache.Remove(portal.GetInstanceID());
            _selections.Clear();
            _oneWay.Clear();
            _diagnostics.Record(PortalDiagnosticCode.EditAccepted, RouteStopCode.Ready,
                zdo.m_uid.ToString());
            if (command.Kind == PortalEditKind.PublicNetwork)
                NoteMapNetworkUsed(command.NetworkId);
            CancelCurrentEdit();
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
            if (playerId == 0L)
            {
                failure = global::Runic.Localization.RunicText.Get("text_fe19556c7188");
                return false;
            }
            if (command.RequiresActiveGroup)
            {
                if (!_groups.TryGetActive(playerId, out string groupId, out _))
                {
                    failure = global::Runic.Localization.RunicText.Get("text_ce20aa84e398");
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
            if (playerId == 0L)
            {
                failure = global::Runic.Localization.RunicText.Get("text_fe19556c7188");
                return false;
            }
            if (_groups.TryIsMember(command.GroupId, playerId, out bool member) && member)
                return true;
            failure = global::Runic.Localization.RunicText.Get("text_62081d6b28ef");
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
                    global::Runic.Localization.RunicText.Get("text_8a9f4cb852bd") +
                    global::Runic.Localization.RunicText.Get("text_165133bbabff"));
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
                Message(player, global::Runic.Localization.RunicText.Get("text_2d9166bd3c34"));
                RejectRoute(portal, RouteStopCode.AuthorityUnavailable);
                return true;
            }
            _index.MarkDirty();
            if (!_index.Rebuild(Time.realtimeSinceStartup, PortalConfig.IndexRefreshSeconds?.Value ?? 2f) ||
                !TryGetPortalId(portal, out string sourceId) ||
                !_graph.TryGetEndpoint(sourceId, out PortalEndpoint source))
            {
                Message(player, global::Runic.Localization.RunicText.Get("text_a4a10e1c947a"));
                RejectRoute(portal, RouteStopCode.SourceUnavailable);
                return true;
            }
            string traveler = PortalPermissionAdapter.Identity(player.GetPlayerID());
            if (!_selections.TryGet(traveler, sourceId, out PortalSelection selection))
            {
                Message(player, global::Runic.Localization.RunicText.Get("text_a80b334d2868"));
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
                        Message(player, global::Runic.Localization.RunicText.Get("text_849ac9020a5e"));
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
                Message(player, global::Runic.Localization.RunicText.Get("text_79e7bd779438"));
                RejectRoute(portal, RouteStopCode.DestinationUnavailable);
                return true;
            }

            WardContext sourceWard = ValheimContracts.ResolveWard(sourceZdo.GetPosition(), player.GetPlayerID());
            WardContext destinationWard = ValheimContracts.ResolveWard(destinationZdo.GetPosition(), player.GetPlayerID());
            PortalEndpoint currentSource = null;
            PortalEndpoint currentDestination = null;
            bool metadataMatches = PortalZdoCodec.TryRead(sourceZdo,
                                       out currentSource, out _) &&
                                   PortalZdoCodec.TryReadDestination(destinationZdo,
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
                currentDestination != null && currentDestination.Revision == plan.DestinationRevision &&
                ZDOMan.instance != null &&
                ZDOMan.instance.GetZDO(sourceZdo.m_uid) == sourceZdo &&
                ZDOMan.instance.GetZDO(destinationZdo.m_uid) == destinationZdo,
                travelPolicy == TravelPolicyState.Allowed);
            PortalAuthorityDecision authority = _authority.Evaluate(evidence);
            if (!authority.IsAllowed)
            {
                Message(player, global::Runic.Localization.RunicText.Get("text_2b43b5feedea") + AuthorityLabel(authority.StopCode) + ".");
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
                Message(player, global::Runic.Localization.RunicText.Get("text_5b22546ca5e6"));
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
                Message(player, global::Runic.Localization.RunicText.Get("text_67d3054da639"));
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
                Message(actor, global::Runic.Localization.RunicText.Get("text_a690a14fde68"));
                RejectRoute(portal, RouteStopCode.AuthorityUnavailable);
                return true;
            }
            WardContext sourceWard = ValheimContracts.ResolveWard(
                portal.transform.position,
                actor.GetPlayerID());
            if (!ValheimContracts.WardAllows(sourceWard))
            {
                Message(actor, global::Runic.Localization.RunicText.Get("text_a0911139a5ff"));
                RejectRoute(portal, RouteStopCode.AuthorityUnavailable);
                return true;
            }
            _index.MarkDirty();
            if (!_index.Rebuild(Time.realtimeSinceStartup, PortalConfig.IndexRefreshSeconds?.Value ?? 2f) ||
                !TryGetPortalId(portal, out string sourceId) ||
                !_graph.TryGetEndpoint(sourceId, out PortalEndpoint source))
            {
                Message(actor, global::Runic.Localization.RunicText.Get("text_74762c0fb840"));
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
                            Label = global::Runic.Localization.RunicText.Get("text_a5149a4145a0") + returnEndpoint.DisplayName,
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
                Message(actor, global::Runic.Localization.RunicText.Get("text_9126359f4725"));
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
            Message(actor, global::Runic.Localization.RunicText.Get("text_b3d6b9c3a8a3") + selected.Label + ".");
            return true;
        }

        private bool TryBeginEdit(TeleportWorld portal, Player actor)
        {
            if (!TryConfigureEvidence(portal, actor, out PortalAuthorityEvidence evidence,
                    out ZDO zdo, out _, out string reason))
            {
                Message(actor, global::Runic.Localization.RunicText.Get("text_d2881d217dd1") + reason + ".");
                return true;
            }
            PortalAuthorityDecision decision = _authority.Evaluate(evidence);
            if (!decision.IsAllowed)
            {
                Message(actor, global::Runic.Localization.RunicText.Get("text_92465b146e7d") + AuthorityLabel(decision.StopCode) + ".");
                RejectEdit(portal, decision.StopCode);
                return true;
            }
            if (_portalEditor == null)
            {
                Message(actor, global::Runic.Localization.RunicText.Get("text_773522b52c4e"));
                return true;
            }
            long token = unchecked(++_editSequence);
            if (token == 0L) token = unchecked(++_editSequence);
            if (!PortalEditEvidence.TryCapture(zdo, out PortalEditEvidence editEvidence))
            {
                Message(actor, global::Runic.Localization.RunicText.Get("text_f45e4ba6b041"));
                return true;
            }
            int mode = PortalZdoCodec.GetMode(zdo);
            string rawVanillaTag = RawVanillaTag(zdo);
            ZDOID vanillaConnection = zdo.GetConnectionZDOID(
                ZDOExtraData.ConnectionType.Portal);
            PortalEditorDraft draft;
            if (mode == (int)PortalMode.Network)
            {
                if (!PortalZdoCodec.TryRead(zdo, out PortalEndpoint endpoint, out _))
                {
                    Message(actor,
                        global::Runic.Localization.RunicText.Get("text_0c775bfb3367"));
                    return true;
                }
                draft = PortalEditorDraft.ForNetwork(rawVanillaTag, endpoint);
            }
            else
            {
                draft = PortalEditorDraft.ForStandard(rawVanillaTag);
            }
            CancelCurrentEdit();
            _edit = new EditSession
            {
                Portal = portal,
                InstanceId = portal.GetInstanceID(),
                ActorId = actor.GetPlayerID(),
                PortalId = zdo.m_uid,
                Schema = PortalZdoCodec.GetSchema(zdo),
                Mode = mode,
                Revision = PortalZdoCodec.GetRevision(zdo),
                Evidence = editEvidence,
                RawVanillaTag = rawVanillaTag,
                VanillaConnection = vanillaConnection,
                PendingVanillaTag = null,
                Token = token
            };
            _groups.RequestLocalGroupChoiceRefresh();
            _portalEditor.Open(draft, vanillaConnection != ZDOID.None);
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
            reason = global::Runic.Localization.RunicText.Get("text_d9077a54d794");
            if (session == null || portal == null || actor == null) return false;
            if (actor.GetPlayerID() != session.ActorId)
            {
                stop = AuthorityStopCode.SenderIdentityUnbound;
                reason = global::Runic.Localization.RunicText.Get("text_318a5002df08");
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
                reason = global::Runic.Localization.RunicText.Get("text_adeedd759da9");
                return false;
            }
            if (!PortalEditEvidence.TryCapture(zdo, out current))
            {
                reason = global::Runic.Localization.RunicText.Get("text_d178d343e110");
                return false;
            }
            if (current.Schema != session.Schema ||
                current.Mode != session.Mode ||
                current.Revision != session.Revision ||
                !current.Matches(session.Evidence))
            {
                reason = global::Runic.Localization.RunicText.Get("text_8f2b90170d12");
                return false;
            }
            string rawTag = RawVanillaTag(zdo);
            ZDOID connection = zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal);
            bool completedPendingTag = session.PendingVanillaTag != null &&
                                       string.Equals(
                                           rawTag,
                                           session.PendingVanillaTag,
                                           StringComparison.Ordinal);
            if (!string.Equals(rawTag, session.RawVanillaTag, StringComparison.Ordinal) &&
                !completedPendingTag)
            {
                reason = global::Runic.Localization.RunicText.Get("text_c52aa230cd0d");
                return false;
            }
            if (connection != session.VanillaConnection && !completedPendingTag)
            {
                reason = global::Runic.Localization.RunicText.Get("text_853179b3a09e");
                return false;
            }
            if (completedPendingTag)
            {
                session.RawVanillaTag = rawTag;
                session.VanillaConnection = connection;
                session.PendingVanillaTag = null;
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
            reason = global::Runic.Localization.RunicText.Get("text_a8ffd7109709");
            if (portal == null || actor == null) return false;
            ZNetView view = portal.GetComponent<ZNetView>();
            Piece piece = portal.GetComponent<Piece>() ?? portal.GetComponentInParent<Piece>();
            zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            creator = piece == null ? 0L : piece.GetCreator();
            if (zdo == null || zdo.m_uid.IsNone() || creator == 0L)
            {
                reason = global::Runic.Localization.RunicText.Get("text_c5d27d2e817b");
                return false;
            }
            int schema = PortalZdoCodec.GetSchema(zdo);
            int mode = PortalZdoCodec.GetMode(zdo);
            bool knownMode = mode == (int)PortalMode.StandardPair || mode == (int)PortalMode.Network;
            bool schemaCurrent = schema == 0 && mode == (int)PortalMode.StandardPair ||
                                 schema == PortalZdoCodec.SchemaVersion;
            if (!knownMode || !schemaCurrent)
            {
                reason = global::Runic.Localization.RunicText.Get("text_f6a766452d61");
                return false;
            }
            string actorStable = PortalPermissionAdapter.Identity(actor.GetPlayerID());
            PortalEndpoint permissionEndpoint;
            if (mode == (int)PortalMode.Network)
            {
                if (!PortalZdoCodec.TryRead(zdo, out PortalEndpoint currentEndpoint, out _))
                {
                    reason = global::Runic.Localization.RunicText.Get("text_3db9c90a6d2d");
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
            _portalEditor?.Close();
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
            if (!player.IsTeleportable(portal.m_allowAllItems))
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

        private static string RawVanillaTag(ZDO zdo) =>
            zdo?.GetString(ZDOVars.s_tag, string.Empty) ?? string.Empty;

        private static string Sentence(string text)
        {
            string value = (text ?? string.Empty).Trim();
            return value.Length == 0 || value.EndsWith(".", StringComparison.Ordinal)
                ? value
                : value + ".";
        }

        private static void Message(Player player, string text)
        {
            if (player != null && !string.IsNullOrEmpty(text))
                player.Message(MessageHud.MessageType.Center, text, 0, null);
        }

        private static string RouteMessage(RouteStopCode stop)
        {
            switch (stop)
            {
                case RouteStopCode.RestrictedItems: return global::Runic.Localization.RunicText.Get("text_22273165bea2");
                case RouteStopCode.PortalsDisabled: return global::Runic.Localization.RunicText.Get("text_e104b4b5d0bd");
                case RouteStopCode.BossTravelBlocked: return global::Runic.Localization.RunicText.Get("text_26f5131c8251");
                case RouteStopCode.DepartureDenied: return global::Runic.Localization.RunicText.Get("text_ff75fdf0b84e");
                case RouteStopCode.ArrivalDenied: return global::Runic.Localization.RunicText.Get("text_df1e534a998b");
                case RouteStopCode.DestinationUnavailable: return global::Runic.Localization.RunicText.Get("text_fb03ca843cbd");
                case RouteStopCode.SourceUnavailable: return global::Runic.Localization.RunicText.Get("text_ec9efb819c1d");
                case RouteStopCode.StaleSelection: return global::Runic.Localization.RunicText.Get("text_701ec266628a");
                default: return global::Runic.Localization.RunicText.Get("text_1099f59edbe4") + stop + ".";
            }
        }

        private static string AuthorityLabel(AuthorityStopCode stop)
        {
            switch (stop)
            {
                case AuthorityStopCode.ServerAuthorityMissing: return global::Runic.Localization.RunicText.Get("text_41fe8d7def0b");
                case AuthorityStopCode.DedicatedTransportUnavailable: return global::Runic.Localization.RunicText.Get("text_78fecc062885");
                case AuthorityStopCode.SenderIdentityUnbound: return global::Runic.Localization.RunicText.Get("text_36191a340e28");
                case AuthorityStopCode.ActorNotLocalAuthority: return global::Runic.Localization.RunicText.Get("text_8e3f0f094cfc");
                case AuthorityStopCode.SourceObjectNotOwned: return global::Runic.Localization.RunicText.Get("text_a0071df6af41");
                case AuthorityStopCode.PlayerObjectNotOwned: return global::Runic.Localization.RunicText.Get("text_9f9780b785a7");
                case AuthorityStopCode.OwnerPermissionDenied: return global::Runic.Localization.RunicText.Get("text_6116269f2237");
                case AuthorityStopCode.SourceWardDenied:
                case AuthorityStopCode.DestinationWardDenied: return global::Runic.Localization.RunicText.Get("text_8ec3605fe18c");
                case AuthorityStopCode.ActorOutOfRange: return global::Runic.Localization.RunicText.Get("text_792af1d2714c");
                case AuthorityStopCode.CurrentStateChanged: return global::Runic.Localization.RunicText.Get("text_a4f937d33f50");
                case AuthorityStopCode.VanillaTravelPolicyDenied: return global::Runic.Localization.RunicText.Get("text_8e29305dbf8c");
                default: return stop.ToString();
            }
        }
    }
}
