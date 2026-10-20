using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RunicAutomation.Operations;
using RunicCrafting.Domain;

namespace RunicCrafting.Integration
{
    internal static class AuthorityCraftExecution
    {
        [ThreadStatic] private static Inventory materials;
        private static Func<Player, byte[], Inventory, bool> pending;
        private static AnimationIntent animation;
        private sealed class AnimationIntent
        {
            internal InventoryGui Gui;
            internal Player Player;
            internal Recipe Recipe;
            internal ItemDrop.ItemData Upgrade;
            internal CraftingStation Station;
            internal bool Multi, ReachedBoundary, Cancelled;
            internal int Amount, Variant;
            internal byte[] Request;
        }
        internal static bool Active => materials != null;
        internal static void EnterMaterials(Inventory inventory) => materials = inventory;
        internal static void ExitMaterials() => materials = null;
        internal static void Register() => CharacterOperationEscrow.RegisterDeliveryProcessor(AuthorityMaterials.Kind, Process);
        internal static bool ReserveBuilding(Player player, ZDOID station, IEnumerable<MaterialRequirement> requirements, Inventory reserved, Action completed)
        {
            if (!InventoryOperationNetwork.Ready || CharacterOperationEscrow.Busy) return false;
            var request = new AuthorityMaterialRequest { Mode = 1, Station = station };
            foreach (var requirement in requirements)
            {
                int needed = requirement.Quantity - ValheimReflection.CountRequirementItems(player.GetInventory(), requirement.ResourceId) -
                    ValheimReflection.CountRequirementItems(reserved, requirement.ResourceId);
                if (needed > 0) request.Needed[requirement.ResourceId] = needed;
            }
            if (request.Needed.Count == 0) return false;
            byte[] expected = null;
            pending = (current, payload, delivery) =>
            {
                if (current == player && OwnerOperationQueue.Same(expected, payload)) AuthorityBuildingExecution.Accept(current, delivery);
                return true;
            };
            bool started = CharacterOperationEscrow.Begin(AuthorityMaterials.Kind, escrow => { request.Escrow = escrow; return expected = request.Encode(); },
                Array.Empty<Tuple<ItemDrop.ItemData, int>>(), (accepted, reason) =>
                {
                    if (Player.m_localPlayer != player) return;
                    if (accepted) completed();
                    else player.Message(MessageHud.MessageType.Center, "Nearby building: " + reason.Replace('-', ' '));
                });
            if (!started) pending = null;
            return started;
        }
        internal static bool Stage(Player player, ZDOID station, int mode, IEnumerable<MaterialRequirement> requirements, Action resume = null)
        {
            if (!InventoryOperationNetwork.Ready || CharacterOperationEscrow.Busy)
            { player.Message(MessageHud.MessageType.Center, InventoryOperationNetwork.Ready ? CharacterOperationEscrow.Status : "Waiting for matching Runic mods on the server."); return false; }
            var request = new AuthorityMaterialRequest { Mode = mode, Station = station };
            foreach (var requirement in requirements)
            {
                int needed = requirement.Quantity - ValheimReflection.CountRequirementItems(player.GetInventory(), requirement.ResourceId);
                if (needed > 0) request.Needed[requirement.ResourceId] = needed;
            }
            if (request.Needed.Count == 0) return false;
            // Resume native world RPCs as soon as the server delivery is applied in memory.
            pending = null;
            return CharacterOperationEscrow.Begin(AuthorityMaterials.Kind, escrow => { request.Escrow = escrow; return request.Encode(); },
                Array.Empty<Tuple<ItemDrop.ItemData, int>>(), (accepted, reason) => {
                    if (Player.m_localPlayer != player) return;
                    if (accepted) resume?.Invoke();
                    else player.Message(MessageHud.MessageType.Center, "Nearby materials: " + reason.Replace('-', ' '));
                });
        }
        internal static IReadOnlyList<IMutableMaterialSource> Sources(Player player, IEnumerable<MaterialRequirement> requirements)
        {
            // Native inventory callbacks mark newly loaded/delivered items as picked up.
            // Finish that bookkeeping before taking exact transaction snapshots; otherwise
            // the first debit appears to mutate unrelated item metadata and cannot roll back.
            if (player.GetInventory().GetAllItems().Any(item => !item.m_pickedUp))
                ValheimReflection.NotifyInventoryChanged(player.GetInventory());
            var ids = requirements.Select(x => x.ResourceId).Distinct(StringComparer.Ordinal).ToArray();
            return new IMutableMaterialSource[] {
                new ValheimMaterialSource("valheim.player:" + player.GetPlayerID(), player.GetInventory(),
                    MaterialSourceKind.PlayerInventory, 0, ids, () => player == Player.m_localPlayer),
                new ValheimMaterialSource("runic.reserved-materials", materials,
                    MaterialSourceKind.NearbyContainer, 0, ids, () => Active)
            };
        }
        internal static bool Begin(InventoryGui gui, Player player, Recipe recipe, ItemDrop.ItemData upgrade,
            bool multi, int amount, CraftingStation station, IReadOnlyList<MaterialRequirement> requirements)
            => BeginCore(gui, player, recipe, upgrade, multi, amount, station, requirements, false);
        internal static void BeginDuringAnimation(InventoryGui gui, Player player, Recipe recipe, ItemDrop.ItemData upgrade,
            bool multi, int amount, CraftingStation station, IReadOnlyList<MaterialRequirement> requirements)
            => BeginCore(gui, player, recipe, upgrade, multi, amount, station, requirements, true);
        private static bool BeginCore(InventoryGui gui, Player player, Recipe recipe, ItemDrop.ItemData upgrade,
            bool multi, int amount, CraftingStation station, IReadOnlyList<MaterialRequirement> requirements, bool waitForAnimation)
        {
            if (CharacterOperationEscrow.Busy)
            { player.Message(MessageHud.MessageType.Center, CharacterOperationEscrow.Status); return false; }
            var request = new AuthorityMaterialRequest { Mode = 0 };
            foreach (var requirement in requirements)
            {
                int needed = requirement.Quantity - ValheimReflection.CountRequirementItems(player.GetInventory(), requirement.ResourceId);
                if (needed > 0) request.Needed.Add(requirement.ResourceId, needed);
            }
            if (request.Needed.Count == 0) return true; // Fully carried costs use the ordinary native craft.
            request.Station = ValheimReflection.StationZdo(station).m_uid;
            byte[] expected = null;
            int variant = (int)AccessTools.Field(typeof(InventoryGui), "m_craftVariant").GetValue(gui);
            var intent = new AnimationIntent { Gui = gui, Player = player, Recipe = recipe, Upgrade = upgrade,
                Station = station, Multi = multi, Amount = amount, Variant = variant, ReachedBoundary = !waitForAnimation };
            if (waitForAnimation) animation = intent;
            pending = (current, payload, delivery) =>
            {
                // A changed menu, station or reconnect cancels automatic crafting. The reserved
                // materials remain a recoverable delivery instead of crafting something unexpected.
                if (!OwnerOperationQueue.Same(expected, payload) || current != player || intent.Cancelled || !Matches(intent) ||
                    waitForAnimation && !intent.ReachedBoundary && (float)AccessTools.Field(typeof(InventoryGui), "m_craftTimer").GetValue(gui) < 0f)
                { if (ReferenceEquals(animation, intent)) CancelAnimation(gui); return true; }
                if (!intent.ReachedBoundary) return false;
                materials = delivery;
                try { AccessTools.Method(typeof(InventoryGui), "DoCrafting").Invoke(gui, new object[] { player }); }
                finally { materials = null; }
                return true;
            };
            bool started = CharacterOperationEscrow.Begin(AuthorityMaterials.Kind, escrow => {
                request.Escrow = escrow; return intent.Request = expected = request.Encode();
            }, Array.Empty<Tuple<ItemDrop.ItemData, int>>(), (accepted, reason) => {
                if (ReferenceEquals(animation, intent))
                { if (!accepted) CancelAnimation(gui); else animation = null; }
                if (!accepted && Player.m_localPlayer != null)
                    Player.m_localPlayer.Message(MessageHud.MessageType.Center, "Nearby crafting: " + reason.Replace('-', ' '));
            });
            if (!started) { pending = null; if (ReferenceEquals(animation, intent)) animation = null; }
            return false; // The native craft resumes only after the durable reservation arrives.
        }
        private static bool Matches(AnimationIntent intent) => intent.Player != null && intent.Player == Player.m_localPlayer &&
            !intent.Player.IsDead() && intent.Gui != null && intent.Player.GetCurrentCraftingStation() == intent.Station &&
            ReferenceEquals(AccessTools.Field(typeof(InventoryGui), "m_craftRecipe").GetValue(intent.Gui), intent.Recipe) &&
            ReferenceEquals(AccessTools.Field(typeof(InventoryGui), "m_craftUpgradeItem").GetValue(intent.Gui), intent.Upgrade) &&
            (bool)AccessTools.Field(typeof(InventoryGui), "m_multiCrafting").GetValue(intent.Gui) == intent.Multi &&
            (int)AccessTools.Field(typeof(InventoryGui), "m_multiCraftAmount").GetValue(intent.Gui) == intent.Amount &&
            (int)AccessTools.Field(typeof(InventoryGui), "m_craftVariant").GetValue(intent.Gui) == intent.Variant;
        internal static bool ReachAnimationBoundary(InventoryGui gui, Player player)
        {
            var intent = animation;
            if (Active || intent == null || intent.Gui != gui || intent.Player != player) return false;
            // Only the validated native timer-completion callsite may arm this token.
            // A UI refresh or another mod invoking DoCrafting early cannot create output.
            if (!intent.ReachedBoundary) return true;
            if (!Matches(intent)) CancelAnimation(gui);
            if (intent.Request != null) CharacterOperationEscrow.WakeDelivery(AuthorityMaterials.Kind, intent.Request);
            return true; // The processor owns the one native invocation, now or after a late grant.
        }
        internal static void NativeAnimationCompleted(InventoryGui gui, Player player)
        {
            var intent = animation;
            if (intent != null && intent.Gui == gui && intent.Player == player) intent.ReachedBoundary = true;
            AccessTools.Method(typeof(InventoryGui), "DoCrafting").Invoke(gui, new object[] { player });
        }
        internal static void CancelAnimation(InventoryGui gui)
        {
            var intent = animation;
            if (Active || intent == null || gui != intent.Gui) return;
            intent.Cancelled = true; animation = null;
            if (gui != null) AccessTools.Field(typeof(InventoryGui), "m_craftTimer").SetValue(gui, -1f);
            if (intent.Request != null) CharacterOperationEscrow.WakeDelivery(AuthorityMaterials.Kind, intent.Request);
        }
        internal static void SelectionChanged(InventoryGui gui)
        {
            var intent = animation;
            if (Active || intent == null || gui != intent.Gui) return;
            object selected = AccessTools.Field(typeof(InventoryGui), "m_selectedRecipe").GetValue(gui);
            if (!ReferenceEquals(AccessTools.Property(selected.GetType(), "Recipe").GetValue(selected), intent.Recipe) ||
                !ReferenceEquals(AccessTools.Property(selected.GetType(), "ItemData").GetValue(selected), intent.Upgrade) ||
                (int)AccessTools.Field(typeof(InventoryGui), "m_selectedVariant").GetValue(gui) != intent.Variant)
                CancelAnimation(gui);
        }
        private static bool Process(Player player, byte[] request, Inventory delivery)
        {
            var process = pending;
            bool done = process == null || process(player, request, delivery);
            if (done && ReferenceEquals(pending, process)) pending = null;
            return done;
        }
    }
}
