using System;

namespace RunicCrafting.Integration
{
    /// <summary>
    /// Valheim renders recipe rows from m_selectedRecipe inside SetupRequirementList and hammer
    /// rows from Hud.SetupPieceInfo. Scope those exact render calls so the shared
    /// InventoryGui.SetupRequirement hook can distinguish a recipe cost from a build cost.
    /// </summary>
    internal static class CraftingRequirementUiContext
    {
        [ThreadStatic]
        private static Recipe _currentRecipe;

        [ThreadStatic]
        private static Piece _currentPiece;

        internal static Recipe CurrentRecipe => _currentRecipe;
        internal static Piece CurrentPiece => _currentPiece;

        internal static Recipe Push(InventoryGui inventoryGui)
        {
            Recipe previous = _currentRecipe;
            _currentRecipe = ValheimReflection.SelectedRecipe(inventoryGui);
            return previous;
        }

        internal static void Restore(Recipe previous) => _currentRecipe = previous;

        internal static Piece PushPiece(Piece piece)
        {
            Piece previous = _currentPiece;
            _currentPiece = piece;
            return previous;
        }

        internal static void RestorePiece(Piece previous) => _currentPiece = previous;
    }
}
