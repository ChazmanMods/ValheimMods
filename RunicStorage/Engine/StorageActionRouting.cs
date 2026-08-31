using System;

namespace RunicStorage.Engine
{
    internal enum StorageActionKind
    {
        None = 0,
        QuickStack = 1,
        Restock = 2,
        SortOpenedContainer = 3,
        Consolidate = 4,
        Search = 5,
        StoreAllOpenedContainer = 6
    }

    internal enum StorageInputOrigin
    {
        None = 0,
        Keyboard = 1,
        Controller = 2
    }

    [Flags]
    internal enum StorageActionEdges
    {
        None = 0,
        KeyboardQuickStack = 1 << 0,
        KeyboardRestock = 1 << 1,
        KeyboardSort = 1 << 2,
        KeyboardConsolidate = 1 << 3,
        KeyboardSearch = 1 << 4,
        ControllerQuickStack = 1 << 5,
        ControllerRestock = 1 << 6,
        ControllerSort = 1 << 7,
        ControllerConsolidate = 1 << 8,
        ControllerSearch = 1 << 9,
        KeyboardStoreAll = 1 << 10
    }

    internal enum StorageRouteOutcome
    {
        None = 0,
        Execute = 1,
        Blocked = 2
    }

    internal enum StorageRouteReason
    {
        None = 0,
        Disabled = 1,
        GameplayInputBlocked = 2,
        DraggedItem = 3,
        StoreOpen = 4,
        MapOpen = 5,
        InventoryOpen = 6,
        SortRequiresOpenedContainer = 7,
        HostAuthorityRequired = 8,
        LocalPlayerOwnershipRequired = 9,
        StoreAllRequiresOpenedContainer = 10
    }

    internal readonly struct StorageActionRequest
    {
        internal StorageActionRequest(StorageActionKind action, StorageInputOrigin origin)
        {
            Action = action;
            Origin = origin;
        }

        internal StorageActionKind Action { get; }
        internal StorageInputOrigin Origin { get; }
        internal bool IsPresent => Action != StorageActionKind.None;
    }

    internal readonly struct StorageRouteContext
    {
        internal StorageRouteContext(
            bool enabled,
            bool gameplayInputBlocked,
            bool draggedItem,
            bool inventoryOpen,
            bool openedContainer,
            bool storeOpen,
            bool mapOpen,
            bool mutationAuthorityAvailable = true,
            bool localPlayerOwnershipAvailable = true)
        {
            Enabled = enabled;
            GameplayInputBlocked = gameplayInputBlocked;
            DraggedItem = draggedItem;
            InventoryOpen = inventoryOpen;
            OpenedContainer = openedContainer;
            StoreOpen = storeOpen;
            MapOpen = mapOpen;
            MutationAuthorityAvailable = mutationAuthorityAvailable;
            LocalPlayerOwnershipAvailable = localPlayerOwnershipAvailable;
        }

        internal bool Enabled { get; }
        internal bool GameplayInputBlocked { get; }
        internal bool DraggedItem { get; }
        internal bool InventoryOpen { get; }
        internal bool OpenedContainer { get; }
        internal bool StoreOpen { get; }
        internal bool MapOpen { get; }
        internal bool MutationAuthorityAvailable { get; }
        internal bool LocalPlayerOwnershipAvailable { get; }
    }

    internal readonly struct StorageRouteDecision
    {
        internal StorageRouteDecision(
            StorageRouteOutcome outcome,
            StorageActionRequest request,
            StorageRouteReason reason)
        {
            Outcome = outcome;
            Request = request;
            Reason = reason;
        }

        internal StorageRouteOutcome Outcome { get; }
        internal StorageActionRequest Request { get; }
        internal StorageRouteReason Reason { get; }
    }

    internal static class StorageActionRouter
    {
        internal static StorageActionRequest Select(StorageActionEdges edges)
        {
            // Preserve the original sort-first behavior so a chest-open sort cannot also trigger
            // the same-frame gameplay action. Keyboard wins exact simultaneous duplicates.
            StorageActionRequest request = Select(
                edges,
                StorageActionEdges.KeyboardSort,
                StorageActionEdges.ControllerSort,
                StorageActionKind.SortOpenedContainer);
            if (request.IsPresent) return request;
            request = Select(
                edges,
                StorageActionEdges.KeyboardStoreAll,
                StorageActionEdges.None,
                StorageActionKind.StoreAllOpenedContainer);
            if (request.IsPresent) return request;
            request = Select(
                edges,
                StorageActionEdges.KeyboardQuickStack,
                StorageActionEdges.ControllerQuickStack,
                StorageActionKind.QuickStack);
            if (request.IsPresent) return request;
            request = Select(
                edges,
                StorageActionEdges.KeyboardRestock,
                StorageActionEdges.ControllerRestock,
                StorageActionKind.Restock);
            if (request.IsPresent) return request;
            request = Select(
                edges,
                StorageActionEdges.KeyboardConsolidate,
                StorageActionEdges.ControllerConsolidate,
                StorageActionKind.Consolidate);
            if (request.IsPresent) return request;
            return Select(
                edges,
                StorageActionEdges.KeyboardSearch,
                StorageActionEdges.ControllerSearch,
                StorageActionKind.Search);
        }

        internal static StorageRouteDecision Route(
            StorageActionRequest request,
            StorageRouteContext context)
        {
            if (!request.IsPresent)
                return new StorageRouteDecision(StorageRouteOutcome.None, request, StorageRouteReason.None);
            if (!context.Enabled)
                return Blocked(request, StorageRouteReason.Disabled);
            if (context.GameplayInputBlocked)
                return Blocked(request, StorageRouteReason.GameplayInputBlocked);
            if (context.DraggedItem)
                return Blocked(request, StorageRouteReason.DraggedItem);
            if (context.StoreOpen)
                return Blocked(request, StorageRouteReason.StoreOpen);
            if (context.MapOpen)
                return Blocked(request, StorageRouteReason.MapOpen);
            if (request.Action == StorageActionKind.Consolidate &&
                !context.LocalPlayerOwnershipAvailable)
                return Blocked(request, StorageRouteReason.LocalPlayerOwnershipRequired);
            if (request.Action != StorageActionKind.Search &&
                request.Action != StorageActionKind.Consolidate &&
                request.Action != StorageActionKind.StoreAllOpenedContainer &&
                !context.MutationAuthorityAvailable)
                return Blocked(request, StorageRouteReason.HostAuthorityRequired);

            if (request.Action == StorageActionKind.SortOpenedContainer)
            {
                if (!context.InventoryOpen || !context.OpenedContainer)
                    return Blocked(request, StorageRouteReason.SortRequiresOpenedContainer);
                return Execute(request);
            }

            if (request.Action == StorageActionKind.StoreAllOpenedContainer)
            {
                if (!context.InventoryOpen || !context.OpenedContainer)
                    return Blocked(request, StorageRouteReason.StoreAllRequiresOpenedContainer);
                return Execute(request);
            }

            if (request.Action == StorageActionKind.Restock ||
                request.Action == StorageActionKind.Search)
                return Execute(request);

            if (request.Action == StorageActionKind.QuickStack &&
                context.InventoryOpen && !context.OpenedContainer)
                return Execute(request);

            if (context.InventoryOpen)
                return Blocked(request, StorageRouteReason.InventoryOpen);
            return Execute(request);
        }

        private static StorageActionRequest Select(
            StorageActionEdges edges,
            StorageActionEdges keyboard,
            StorageActionEdges controller,
            StorageActionKind action)
        {
            if ((edges & keyboard) != 0)
                return new StorageActionRequest(action, StorageInputOrigin.Keyboard);
            return (edges & controller) != 0
                ? new StorageActionRequest(action, StorageInputOrigin.Controller)
                : new StorageActionRequest(StorageActionKind.None, StorageInputOrigin.None);
        }

        private static StorageRouteDecision Execute(StorageActionRequest request) =>
            new StorageRouteDecision(StorageRouteOutcome.Execute, request, StorageRouteReason.None);

        private static StorageRouteDecision Blocked(
            StorageActionRequest request,
            StorageRouteReason reason) =>
            new StorageRouteDecision(StorageRouteOutcome.Blocked, request, reason);
    }

    internal static class StorageActionDiagnostics
    {
        internal static string ActionCode(StorageActionKind action)
        {
            switch (action)
            {
                case StorageActionKind.QuickStack: return "quick-stack";
                case StorageActionKind.Restock: return "restock";
                case StorageActionKind.SortOpenedContainer: return "sort-opened-container";
                case StorageActionKind.Consolidate: return "consolidate";
                case StorageActionKind.Search: return "search";
                case StorageActionKind.StoreAllOpenedContainer: return "store-all-opened-container";
                default: return "none";
            }
        }

        internal static string OriginCode(StorageInputOrigin origin)
        {
            switch (origin)
            {
                case StorageInputOrigin.Keyboard: return "keyboard";
                case StorageInputOrigin.Controller: return "controller";
                default: return "none";
            }
        }

        internal static string RouteReasonCode(StorageRouteReason reason)
        {
            switch (reason)
            {
                case StorageRouteReason.Disabled: return "config.disabled";
                case StorageRouteReason.GameplayInputBlocked: return "input.context-blocked";
                case StorageRouteReason.DraggedItem: return "inventory.drag-active";
                case StorageRouteReason.StoreOpen: return "ui.store-open";
                case StorageRouteReason.MapOpen: return "ui.map-open";
                case StorageRouteReason.InventoryOpen: return "ui.inventory-open";
                case StorageRouteReason.SortRequiresOpenedContainer: return "ui.open-container-required";
                case StorageRouteReason.HostAuthorityRequired: return "authority.local-player-owner-required";
                case StorageRouteReason.LocalPlayerOwnershipRequired: return "authority.local-player-owner-required";
                case StorageRouteReason.StoreAllRequiresOpenedContainer: return "ui.open-container-required";
                default: return "ok";
            }
        }

        internal static string RouteFeedback(StorageRouteReason reason)
        {
            switch (reason)
            {
                case StorageRouteReason.Disabled:
                    return "Runic Storage is disabled in configuration.";
                case StorageRouteReason.GameplayInputBlocked:
                    return "Runic Storage: close the current menu, text window, or dialog first.";
                case StorageRouteReason.DraggedItem:
                    return "Runic Storage: place or cancel the item currently attached to the cursor first.";
                case StorageRouteReason.StoreOpen:
                    return "Runic Storage: close the trader window first.";
                case StorageRouteReason.MapOpen:
                    return "Runic Storage: close the map first.";
                case StorageRouteReason.InventoryOpen:
                    return "Runic Storage: close the opened chest first, or use Sort/Store All for that chest.";
                case StorageRouteReason.SortRequiresOpenedContainer:
                    return "Runic Storage: open a non-personal container before sorting.";
                case StorageRouteReason.HostAuthorityRequired:
                    return "Runic Storage: this action requires the owning local player.";
                case StorageRouteReason.LocalPlayerOwnershipRequired:
                    return "Runic Storage: Consolidate requires the owning local player; no carried items were changed.";
                case StorageRouteReason.StoreAllRequiresOpenedContainer:
                    return "Runic Storage: open a non-personal container before using Store All.";
                default:
                    return string.Empty;
            }
        }
    }

    internal enum ControllerChordDisposition
    {
        ReportWithoutConsume = 0,
        ExecuteAndConsume = 1,
        ReportAndConsume = 2
    }

    internal static class ControllerChordSessionPolicy
    {
        /// <summary>
        /// A first blocked chord remains vanilla-owned. Once an executable chord starts a Storage
        /// modifier session, later recognized edges are Storage-owned until full release; a newly
        /// blocked chained edge is therefore reported and consumed instead of leaking to vanilla.
        /// </summary>
        internal static ControllerChordDisposition Decide(
            bool sessionActive,
            StorageRouteDecision decision) =>
            Decide(sessionActive, decision.Outcome);

        internal static ControllerChordDisposition Decide(
            bool sessionActive,
            StorageRouteOutcome outcome)
        {
            if (outcome == StorageRouteOutcome.Execute)
                return ControllerChordDisposition.ExecuteAndConsume;
            return sessionActive && outcome == StorageRouteOutcome.Blocked
                ? ControllerChordDisposition.ReportAndConsume
                : ControllerChordDisposition.ReportWithoutConsume;
        }
    }

    internal enum QuickStackNoOpReason
    {
        None = 0,
        InventoryEmpty = 1,
        AllStacksProtected = 2,
        NoAuthorizedContainers = 3,
        NoMatchingResources = 4,
        DestinationsUnavailable = 5
    }

    internal readonly struct QuickStackObservation
    {
        internal QuickStackObservation(
            int carriedStacks,
            int eligibleStacks,
            int protectedStacks,
            int authorizedContainers,
            int matchedStacks,
            int movedQuantity)
        {
            CarriedStacks = Math.Max(0, carriedStacks);
            EligibleStacks = Math.Max(0, eligibleStacks);
            ProtectedStacks = Math.Max(0, protectedStacks);
            AuthorizedContainers = Math.Max(0, authorizedContainers);
            MatchedStacks = Math.Max(0, matchedStacks);
            MovedQuantity = Math.Max(0, movedQuantity);
        }

        internal int CarriedStacks { get; }
        internal int EligibleStacks { get; }
        internal int ProtectedStacks { get; }
        internal int AuthorizedContainers { get; }
        internal int MatchedStacks { get; }
        internal int MovedQuantity { get; }
    }

    internal static class QuickStackDiagnostics
    {
        internal static QuickStackNoOpReason Classify(QuickStackObservation observation)
        {
            if (observation.MovedQuantity > 0) return QuickStackNoOpReason.None;
            if (observation.CarriedStacks == 0) return QuickStackNoOpReason.InventoryEmpty;
            if (observation.EligibleStacks == 0 && observation.ProtectedStacks > 0)
                return QuickStackNoOpReason.AllStacksProtected;
            if (observation.AuthorizedContainers == 0)
                return QuickStackNoOpReason.NoAuthorizedContainers;
            if (observation.MatchedStacks == 0)
                return QuickStackNoOpReason.NoMatchingResources;
            return QuickStackNoOpReason.DestinationsUnavailable;
        }

        internal static string ReasonCode(QuickStackNoOpReason reason)
        {
            switch (reason)
            {
                case QuickStackNoOpReason.InventoryEmpty: return "inventory.empty";
                case QuickStackNoOpReason.AllStacksProtected: return "inventory.all-protected";
                case QuickStackNoOpReason.NoAuthorizedContainers: return "discovery.none-authorized";
                case QuickStackNoOpReason.NoMatchingResources: return "resource.no-existing-match";
                case QuickStackNoOpReason.DestinationsUnavailable: return "destination.full-or-unavailable";
                default: return "ok";
            }
        }
    }
}
