using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RunicAutomation.Operations;
using RunicCrafting.Domain;
using UnityEngine;

namespace RunicCrafting.Integration
{
    // Reservations are character-owned materials, included in ordinary character saves
    // separately from backpack slots. Placement never waits for a profile checkpoint.
    // Native placement consumes this inventory through the existing material lease. The
    // entire native UpdatePlacement path still owns validation, stamina, durability and XP.
    internal static class AuthorityBuildingExecution
    {
        private const string Key = "runic.crafting.build-materials.v1";
        private static Player owner;
        private static Inventory reserved;
        private static string cached = "";
        private static bool scoped;
        private sealed class PlacementIntent
        {
            internal Player Player; internal Piece Piece; internal object Tool;
            internal long World;
            internal bool Ready, Armed, Attempted, Notice; internal float Started;
        }
        private static PlacementIntent placement;
        private static readonly System.Reflection.FieldInfo Pressed = AccessTools.Field(typeof(Player), "m_placePressedTime");
        private static readonly System.Reflection.FieldInfo LastToolUse = AccessTools.Field(typeof(Player), "m_lastToolUseTime");
        private static readonly System.Reflection.MethodInfo RightItem = AccessTools.Method(typeof(Humanoid), "GetRightItem");
        private static byte[] beforeScope;

        internal static Inventory Materials(Player player)
        {
            string encoded = player != null && player.m_customData.TryGetValue(Key, out var value) ? value : "";
            if (!ReferenceEquals(owner, player) || encoded != cached)
            {
                owner = player; reserved = null; scoped = false; placement = null;
                cached = encoded;
                if (encoded.Length > 0)
                    reserved = NativeInventoryCodec.Load(Convert.FromBase64String(encoded), 32, 32);
            }
            return reserved ??= new Inventory("Reserved building materials", null, 32, 32);
        }
        private static void Persist(Player player)
        {
            if (reserved.NrOfItems() == 0) player.m_customData.Remove(Key);
            else player.m_customData[Key] = Convert.ToBase64String(NativeInventoryCodec.Save(reserved));
            cached = player.m_customData.TryGetValue(Key, out var encoded) ? encoded : "";
        }
        internal static void Accept(Player player, Inventory delivery)
        {
            var target = NativeInventoryCodec.Clone(Materials(player));
            if (!NativeInventoryCodec.AddAll(target, delivery)) throw new InvalidOperationException("Building reservation exceeds capacity");
            reserved = target;
            delivery.GetAllItems().Clear(); Persist(player);
        }
        internal static bool Begin(Player player, Piece piece, ZDOID station, IEnumerable<MaterialRequirement> requirements)
        {
            var world = ZNet.instance?.GetWorld();
            if (player == null || piece == null || world == null || world.m_uid == 0) return false;
            var inventory = Materials(player);
            if (placement != null && placement.Player == player && placement.Piece == piece && ValidPlacement(placement)) return true;
            // Preserve one build click while materials arrive. Aim, snapping and rotation
            // remain live; native TryPlacePiece refreshes and validates the current target.
            var intent = new PlacementIntent { Player = player, Piece = piece, Tool = RightItem.Invoke(player, null),
                World = world.m_uid, Started = Time.realtimeSinceStartup };
            if (!ValidPlacement(intent)) return false;
            placement = intent;
            bool started = AuthorityCraftExecution.ReserveBuilding(player, station, requirements, inventory, () =>
            {
                // A loaded receipt delivers materials but never replays a stale world action.
                if (!ReferenceEquals(placement, intent)) return;
                intent.Ready = true;
                CraftingDiagnostics.TraceAction("nearby-build-reservation", "materials-ready", piece.name);
            });
            if (!started && ReferenceEquals(placement, intent)) placement = null;
            if (started) CraftingDiagnostics.TraceAction("nearby-build-reservation", "requested", piece.name);
            return started;
        }
        private static bool ValidPlacement(PlacementIntent intent)
        {
            var player = intent.Player;
            var world = ZNet.instance?.GetWorld();
            if (player == null || player != Player.m_localPlayer || intent.Piece == null || player.IsDead() || player.IsTeleporting() || player.InCutscene() ||
                world == null || world.m_uid != intent.World || !player.InPlaceMode() ||
                player.GetSelectedPiece() != intent.Piece || !ReferenceEquals(RightItem.Invoke(player, null), intent.Tool)) return false;
            return true;
        }
        private static void CancelPlacement(string reason, bool notify)
        {
            var previous = placement; placement = null;
            if (previous == null) return;
            if (previous.Armed && previous.Player != null) Pressed.SetValue(previous.Player, -9999f);
            CraftingDiagnostics.TraceAction("nearby-build-reservation", "cancelled", reason);
            if (notify && previous.Player == Player.m_localPlayer && previous.Player != null)
                previous.Player.Message(MessageHud.MessageType.TopLeft, "Pending build cancelled; reserved materials remain available.");
        }
        private static void ArmPlacement(Player player)
        {
            var intent = placement;
            if (intent == null || !intent.Ready || intent.Player != player) return;
            if (!ValidPlacement(intent)) { CancelPlacement("build-context-changed", true); return; }
            if (CancelInput()) { CancelPlacement("cancel-input", false); return; }
            if (Hud.IsPieceSelectionVisible() || Time.time - (float)LastToolUse.GetValue(player) <= player.m_placeDelay) return;
            // Keep the intent until native TryPlacePiece actually consumes it. A native UI,
            // cooldown or stamina gate can return from UpdatePlacement without an attempt.
            Pressed.SetValue(player, Time.time); intent.Armed = true; intent.Attempted = false;
        }
        internal static void NativePlacementEntered(Player player)
        {
            if (placement != null && placement.Player == player && placement.Armed) placement.Attempted = true;
        }
        internal static void NativePlacementReturned(Player player, bool succeeded)
        {
            // A physical click can succeed using newly carried materials while the older
            // reservation is still returning. Its delivery must not place a second copy.
            if (succeeded && placement != null && placement.Player == player) CancelPlacement("placement-completed", false);
        }
        internal static bool ResumeAfterGhostUpdate(Player player, Piece piece)
        {
            var intent = placement;
            if (intent == null || !intent.Armed || intent.Player != player) return true;
            if (piece != intent.Piece || !ValidPlacement(intent))
            { CancelPlacement("build-context-changed-during-native-update", true); return false; }
            if (CancelInput()) { CancelPlacement("cancel-input", false); return false; }
            placement = null;
            Pressed.SetValue(player, -9999f);
            CraftingDiagnostics.TraceAction("nearby-build-reservation", "native-attempt", piece.name);
            return true;
        }
        internal static void Enter(Player player, bool takeInput)
        {
            if (player != Player.m_localPlayer || !InventoryOperationNetwork.Ready || !takeInput || player.IsDead()) return;
            var inventory = Materials(player);
            if (inventory.NrOfItems() == 0 || AuthorityCraftExecution.Active || CharacterOperationEscrow.Busy) return;
            var piece = player.GetSelectedPiece();
            if (piece == null || (piece.m_resources ?? Array.Empty<Piece.Requirement>()).Any(r => r?.m_resItem != null &&
                ValheimReflection.CountRequirementItems(player.GetInventory(), ValheimReflection.ResourceId(r.m_resItem)) +
                ValheimReflection.CountRequirementItems(inventory, ValheimReflection.ResourceId(r.m_resItem)) < r.GetAmount(0)))
            {
                if (placement != null && placement.Ready) CancelPlacement("materials-or-piece-changed", true);
                return;
            }
            beforeScope = NativeInventoryCodec.Save(inventory);
            AuthorityCraftExecution.EnterMaterials(inventory); scoped = true;
            ArmPlacement(player);
        }
        internal static void Exit(Player player)
        {
            if (!scoped) return;
            scoped = false; AuthorityCraftExecution.ExitMaterials();
            if (placement != null && placement.Armed && placement.Player == player)
            {
                if (placement.Attempted) CancelPlacement("native-attempt-rejected", false);
                // Native UpdatePlacement did not reach TryPlacePiece. Retry the same intent
                // on a later permitted update instead of allowing its 0.2-second input to expire.
                else { placement.Armed = false; Pressed.SetValue(player, -9999f); }
            }
            if (!OwnerOperationQueue.Same(beforeScope, NativeInventoryCodec.Save(reserved)))
            {
                Persist(player);
            }
        }
        internal static void RunReserved(Player player, Action action)
        {
            var inventory = Materials(player);
            if (scoped || AuthorityCraftExecution.Active || CharacterOperationEscrow.Busy || player.IsDead()) return;
            beforeScope = NativeInventoryCodec.Save(inventory);
            AuthorityCraftExecution.EnterMaterials(inventory); scoped = true;
            try { action(); }
            finally { Exit(player); }
        }
        internal static void Tick()
        {
            var player = Player.m_localPlayer;
            if (placement != null && !ValidPlacement(placement)) CancelPlacement("build-context-changed", true);
            if (placement != null && CancelInput()) CancelPlacement("cancel-input", false);
            if (placement != null && !placement.Notice && Time.realtimeSinceStartup - placement.Started >= .5f)
            {
                placement.Notice = true;
                player.Message(MessageHud.MessageType.TopLeft, "Preparing nearby building materials…");
            }
            if (player == null || player.IsDead() || scoped || CharacterOperationEscrow.Busy) return;
            var inventory = Materials(player);
            if (placement != null && !placement.Ready) CancelPlacement("reservation-did-not-complete", false);
            if (player.InPlaceMode() || inventory.NrOfItems() == 0) return;
            // Cancelled reservations return automatically whenever backpack space is available.
            bool moved = false;
            foreach (var item in inventory.GetAllItems().ToArray())
            {
                int count = Math.Min(item.m_stack, NativeInventoryCodec.CapacityFor(player.GetInventory(), item));
                if (count > 0) moved |= NativeInventoryCodec.Transfer(inventory, player.GetInventory(), item.m_gridPos, NativeInventoryCodec.ItemIdentity(item), count);
            }
            if (moved) { Persist(player); UiPreviewCache.Invalidate(); }
        }
        private static bool CancelInput() => ZInput.GetKeyDown(KeyCode.Escape, false) || ZInput.GetButtonDown("JoyButtonB");
    }
}
