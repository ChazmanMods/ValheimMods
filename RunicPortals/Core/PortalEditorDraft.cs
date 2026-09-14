using System;
using RunicPortals.Api;

namespace RunicPortals.Core
{
    internal enum PortalEditorDirection
    {
        Both = 0,
        ArrivalsOnly = 1,
        DeparturesOnly = 2
    }

    internal enum PortalEditorSubmitState
    {
        Rejected = 0,
        ConfirmationRequired = 1,
        Saved = 2
    }

    internal readonly struct PortalEditorSubmitResult
    {
        internal PortalEditorSubmitResult(
            PortalEditorSubmitState state,
            string message)
        {
            State = state;
            Message = message ?? string.Empty;
        }

        internal PortalEditorSubmitState State { get; }
        internal string Message { get; }
        internal bool ClosesEditor => State == PortalEditorSubmitState.Saved;

        internal static PortalEditorSubmitResult Reject(string message) =>
            new PortalEditorSubmitResult(PortalEditorSubmitState.Rejected, message);

        internal static PortalEditorSubmitResult Confirm(string message) =>
            new PortalEditorSubmitResult(
                PortalEditorSubmitState.ConfirmationRequired,
                message);

        internal static PortalEditorSubmitResult Saved(string message) =>
            new PortalEditorSubmitResult(PortalEditorSubmitState.Saved, message);
    }

    /// <summary>
    /// The editor keeps presentation values separate from the legacy pipe-delimited text format.
    /// A draft can only become a command through the same bounded domain validation used by the
    /// runtime commit path.
    /// </summary>
    internal sealed class PortalEditorDraft
    {
        internal bool StandardPair { get; set; }
        internal string VanillaTag { get; set; } = string.Empty;
        internal string NetworkName { get; set; } = string.Empty;
        internal string PortalName { get; set; } = string.Empty;
        internal PortalNetworkKind Access { get; set; } = PortalNetworkKind.Public;
        internal string GroupId { get; set; } = string.Empty;
        internal PortalEditorDirection Direction { get; set; } = PortalEditorDirection.Both;

        internal static PortalEditorDraft ForStandard(string vanillaTag) =>
            new PortalEditorDraft
            {
                StandardPair = true,
                VanillaTag = vanillaTag ?? string.Empty
            };

        internal static PortalEditorDraft ForNetwork(
            string vanillaTag,
            PortalEndpoint endpoint)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            return new PortalEditorDraft
            {
                StandardPair = false,
                VanillaTag = vanillaTag ?? string.Empty,
                NetworkName = endpoint.NetworkId,
                PortalName = endpoint.DisplayName,
                Access = endpoint.NetworkKind,
                GroupId = endpoint.Access?.GetPolicy(PortalAccessAction.ViewDiscover)?.GroupId ??
                          string.Empty,
                Direction = DirectionFor(
                    endpoint.AcceptsArrival,
                    endpoint.PermitsDeparture)
            };
        }

        internal PortalEditCommand BuildCommand()
        {
            if (StandardPair) return PortalEditCommand.CreateStandard(VanillaTag);
            bool arrive = Direction != PortalEditorDirection.DeparturesOnly;
            bool depart = Direction != PortalEditorDirection.ArrivalsOnly;
            return PortalEditCommand.CreateNetwork(
                NetworkName,
                PortalName,
                Access,
                Access == PortalNetworkKind.Group ? GroupId : string.Empty,
                arrive,
                depart);
        }

        internal static PortalEditorDirection DirectionFor(bool arrive, bool depart)
        {
            if (arrive && depart) return PortalEditorDirection.Both;
            if (arrive) return PortalEditorDirection.ArrivalsOnly;
            if (depart) return PortalEditorDirection.DeparturesOnly;
            return PortalEditorDirection.Both;
        }
    }
}
