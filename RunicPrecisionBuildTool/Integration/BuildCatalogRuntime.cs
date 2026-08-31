using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace QuietBuildRotation.Integration
{
    internal enum BuildCatalogAction : byte
    {
        SearchNext = 0,
        ToggleFavorite,
        NextFavorite,
        NextRecent
    }

    /// <summary>
    /// Bounded adapter over PieceTable's already-computed available lists. It never calls
    /// UpdateAvailable, edits m_pieces, or adds an unlock; selection is revalidated against the
    /// same available list immediately before SetCategory/SetSelected.
    /// </summary>
    internal static class BuildCatalogRuntime
    {
        internal const int MaximumLocalizationTokenLength = 128;
        internal const int MaximumGridWidth = BoundedBuildCatalog.CandidateInspectionCapacity;

        private static readonly BoundedBuildCatalog Catalog = new BoundedBuildCatalog();
        private static readonly List<BuildCatalogEntry> Scratch =
            new List<BuildCatalogEntry>(BoundedBuildCatalog.EntryCapacity);

        private static AccessTools.FieldRef<PieceTable, List<List<Piece>>> _availablePieces;
        private static int _gridWidth;
        private static bool _hasGridWidth;
        private delegate string TranslateDelegate(Localization localization, string key);
        private static TranslateDelegate _translate;
        private static string _lastSelectedId;
        private static int _lastSelectedInstanceId;
        private static bool _loadedPersistedLists;

        internal static bool Initialize(out string error)
        {
            error = null;
            try
            {
                FieldInfo field = AccessTools.Field(typeof(PieceTable), "m_availablePieces");
                if (field == null || field.FieldType != typeof(List<List<Piece>>))
                    throw new MissingFieldException(typeof(PieceTable).FullName, "m_availablePieces");
                _availablePieces =
                    AccessTools.FieldRefAccess<PieceTable, List<List<Piece>>>(field);
                if (!InitializeGridWidthReader(out string gridWidthError))
                    throw new InvalidOperationException(gridWidthError);
                MethodInfo translate = AccessTools.Method(
                    typeof(Localization), "Translate", new[] { typeof(string) });
                if (translate == null || translate.IsStatic || translate.ReturnType != typeof(string))
                    throw new MissingMethodException(typeof(Localization).FullName, "Translate");
                _translate = AccessTools.MethodDelegate<TranslateDelegate>(translate);
                LoadPersistedLists();
                return true;
            }
            catch (Exception exception)
            {
                _availablePieces = null;
                _gridWidth = 0;
                _hasGridWidth = false;
                _translate = null;
                error = "bounded build catalog adapter failed: " +
                        exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        internal static void Shutdown()
        {
            PersistLists();
            _availablePieces = null;
            _gridWidth = 0;
            _hasGridWidth = false;
            _translate = null;
            _lastSelectedId = null;
            _lastSelectedInstanceId = 0;
            _loadedPersistedLists = false;
            Scratch.Clear();
        }

        internal static void ObserveSelection(Player player, Piece selected)
        {
            if (!selected) return;
            if (Catalog.EntryCount == 0 && player)
            {
                PieceTable table = PlacementAdapter.GetBuildPieceTable(player);
                if (table) Rebuild(table);
            }
            int instanceId = selected.GetInstanceID();
            if (instanceId == _lastSelectedInstanceId) return;
            string id = StableId(selected);
            if (string.IsNullOrEmpty(id) || string.Equals(id, _lastSelectedId, StringComparison.Ordinal))
            {
                _lastSelectedInstanceId = instanceId;
                return;
            }
            _lastSelectedInstanceId = instanceId;
            _lastSelectedId = id;
            Catalog.RecordRecent(id);
            PersistRecents();
        }

        internal static bool Execute(
            Player player,
            BuildCatalogAction action,
            out string message)
        {
            message = null;
            if (!player || player != Player.m_localPlayer ||
                _availablePieces == null || !_hasGridWidth)
            {
                message = "Build catalog is unavailable.";
                return false;
            }

            PieceTable table = PlacementAdapter.GetBuildPieceTable(player);
            if (!table || !Rebuild(table))
            {
                message = "No currently unlocked build pieces are available.";
                return false;
            }

            Piece selected = table.GetSelectedPiece();
            string selectedId = StableId(selected);
            BuildCatalogEntry entry;
            switch (action)
            {
                case BuildCatalogAction.ToggleFavorite:
                    if (string.IsNullOrEmpty(selectedId) ||
                        !Catalog.ToggleFavorite(selectedId, out bool favorite))
                    {
                        message = Catalog.FavoriteCount >= BoundedBuildCatalog.FavoriteCapacity
                            ? "Favorite limit reached (64)."
                            : "The current piece cannot be favorited.";
                        return false;
                    }
                    PersistFavorites();
                    message = favorite ? "Build favorite added." : "Build favorite removed.";
                    return true;

                case BuildCatalogAction.NextFavorite:
                    if (!Catalog.TryCycleFavorite(selectedId, out entry))
                    {
                        message = "No currently unlocked favorites are available.";
                        return false;
                    }
                    break;

                case BuildCatalogAction.NextRecent:
                    if (!Catalog.TryCycleRecent(selectedId, out entry))
                    {
                        message = "No currently unlocked recent pieces are available.";
                        return false;
                    }
                    break;

                default:
                    string query = BoundedBuildCatalog.BoundQuery(
                        PluginConfig.BuildSearchQuery?.Value);
                    if (!Catalog.TryFindNext(query, selectedId, out entry))
                    {
                        message = "No currently unlocked piece matches \"" + query + "\".";
                        return false;
                    }
                    break;
            }

            if (!TrySelectFresh(table, in entry))
            {
                message = "Piece availability changed; selection was not applied.";
                return false;
            }

            PlacementAdapter.RefreshPlacementGhost(player);
            _lastSelectedId = entry.StableId;
            Piece selectedNow = table.GetSelectedPiece();
            _lastSelectedInstanceId = selectedNow ? selectedNow.GetInstanceID() : 0;
            Catalog.RecordRecent(entry.StableId);
            PersistRecents();
            message = "Selected " + SafeDisplay(entry.DisplayName) + ".";
            return true;
        }

        private static bool Rebuild(PieceTable table)
        {
            Scratch.Clear();
            List<List<Piece>> categories = _availablePieces(table);
            if (categories == null || !TryReadGridWidth(out int gridWidth)) return false;

            for (int category = 0;
                 category < categories.Count && Scratch.Count < BoundedBuildCatalog.EntryCapacity;
                 category++)
            {
                List<Piece> pieces = categories[category];
                if (pieces == null) continue;
                for (int index = 0;
                     index < pieces.Count && Scratch.Count < BoundedBuildCatalog.EntryCapacity;
                     index++)
                {
                    Piece piece = pieces[index];
                    if (!piece || !piece.m_enabled) continue;
                    string id = StableId(piece);
                    string display = SafePieceName(piece);
                    Scratch.Add(new BuildCatalogEntry(
                        id,
                        display,
                        category,
                        index % gridWidth,
                        index / gridWidth,
                        gridWidth));
                }
            }

            Catalog.Rebuild(Scratch);
            LoadPersistedLists();
            return Catalog.EntryCount > 0;
        }

        private static bool TrySelectFresh(PieceTable table, in BuildCatalogEntry entry)
        {
            List<List<Piece>> categories = _availablePieces(table);
            if (categories == null || !TryReadGridWidth(out int gridWidth) ||
                gridWidth != entry.GridWidth ||
                entry.Category < 0 || entry.Category >= categories.Count)
                return false;
            List<Piece> pieces = categories[entry.Category];
            long linearWide = (long)entry.Y * gridWidth + entry.X;
            if (pieces == null || entry.X < 0 || entry.X >= gridWidth || entry.Y < 0 ||
                linearWide < 0 || linearWide >= pieces.Count)
                return false;
            int linear = (int)linearWide;
            Piece fresh = pieces[linear];
            if (!fresh || !fresh.m_enabled ||
                !string.Equals(StableId(fresh), entry.StableId, StringComparison.Ordinal))
                return false;

            table.SetCategory(entry.Category);
            table.SetSelected(new Vector2Int(entry.X, entry.Y));
            Piece selected = table.GetSelectedPiece();
            return selected && selected.m_enabled &&
                   string.Equals(StableId(selected), entry.StableId, StringComparison.Ordinal);
        }

        internal static bool TryReadLiteralGridWidth(FieldInfo field, out int gridWidth)
        {
            gridWidth = 0;
            if (field == null || field.FieldType != typeof(int) || !field.IsStatic ||
                !field.IsLiteral || !field.IsPublic)
                return false;
            try
            {
                object value = field.GetRawConstantValue();
                if (!(value is int exact) || exact <= 0 || exact > MaximumGridWidth)
                    return false;
                gridWidth = exact;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static bool InitializeGridWidthReader(out string error)
        {
            error = null;
            _gridWidth = 0;
            _hasGridWidth = false;
            FieldInfo field = typeof(PieceTable).GetField(
                "m_gridWidth",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (field?.DeclaringType != typeof(PieceTable) ||
                !TryReadLiteralGridWidth(field, out int exact))
            {
                error = "PieceTable.m_gridWidth is not the supported bounded public const int.";
                return false;
            }
            _gridWidth = exact;
            _hasGridWidth = true;
            return true;
        }

        private static bool TryReadGridWidth(out int gridWidth)
        {
            gridWidth = _gridWidth;
            return _hasGridWidth && gridWidth > 0 && gridWidth <= MaximumGridWidth;
        }

        private static string StableId(Piece piece)
        {
            if (!piece) return null;
            string value = Utils.GetPrefabName(piece.gameObject);
            return string.IsNullOrEmpty(value) || value.Length > BoundedBuildCatalog.MaximumIdLength
                ? null
                : value;
        }

        private static string SafePieceName(Piece piece)
        {
            string raw = piece ? piece.m_name : null;
            if (!TryPrepareLocalizationToken(raw, out string token)) return StableId(piece);
            string translated = raw;
            try
            {
                if (Localization.instance != null &&
                    TryGetTranslationKey(token, out string key))
                {
                    translated = _translate(Localization.instance, key);
                }
                else
                {
                    translated = WithoutLocalizationMarker(token);
                }
            }
            catch
            {
                translated = WithoutLocalizationMarker(token);
            }
            return SafeDisplay(translated) ??
                   SafeDisplay(WithoutLocalizationMarker(token)) ??
                   StableId(piece);
        }

        private static string SafeDisplay(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            int inspection = Math.Min(
                value.Length,
                BoundedBuildCatalog.MaximumDisplayLength);
            string trimmed = value.Substring(0, inspection).Trim();
            if (trimmed.Length == 0) return null;
            for (int index = 0; index < trimmed.Length; index++)
                if (char.IsControl(trimmed[index]) ||
                    char.GetUnicodeCategory(trimmed[index]) == UnicodeCategory.Format ||
                    trimmed[index] == '<' || trimmed[index] == '>')
                    return null;
            return trimmed;
        }

        private static bool TryPrepareLocalizationToken(string value, out string token)
        {
            token = null;
            if (string.IsNullOrEmpty(value) || value.Length > MaximumLocalizationTokenLength)
                return false;
            bool visible = false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (char.IsControl(character) ||
                    char.GetUnicodeCategory(character) == UnicodeCategory.Format ||
                    character == '<' || character == '>')
                    return false;
                if (!char.IsWhiteSpace(character)) visible = true;
            }
            if (!visible) return false;
            token = value;
            return true;
        }

        private static bool TryGetTranslationKey(string token, out string key)
        {
            key = null;
            if (string.IsNullOrEmpty(token) || token.Length < 2 || token[0] != '$')
                return false;
            int start = 1;
            while (start < token.Length && token[start] == '$') start++;
            if (start == token.Length) return false;
            key = token.Substring(start);
            if (key.StartsWith("KEY_", StringComparison.Ordinal))
            {
                key = null;
                return false;
            }
            return true;
        }

        private static string WithoutLocalizationMarker(string token)
        {
            if (string.IsNullOrEmpty(token)) return string.Empty;
            int start = 0;
            while (start < token.Length && token[start] == '$') start++;
            return start == 0 ? token : token.Substring(start);
        }

        private static void LoadPersistedLists()
        {
            if (_loadedPersistedLists) return;
            Catalog.LoadFavorites(PluginConfig.FavoritePieceIds?.Value);
            Catalog.LoadRecents(PluginConfig.RecentPieceIds?.Value);
            _loadedPersistedLists = true;
        }

        private static void PersistLists()
        {
            PersistFavorites();
            PersistRecents();
        }

        private static void PersistFavorites()
        {
            if (PluginConfig.FavoritePieceIds == null) return;
            string value = Catalog.SerializeFavorites();
            if (!string.Equals(PluginConfig.FavoritePieceIds.Value, value, StringComparison.Ordinal))
                PluginConfig.FavoritePieceIds.Value = value;
        }

        private static void PersistRecents()
        {
            if (PluginConfig.RecentPieceIds == null) return;
            string value = Catalog.SerializeRecents();
            if (!string.Equals(PluginConfig.RecentPieceIds.Value, value, StringComparison.Ordinal))
                PluginConfig.RecentPieceIds.Value = value;
        }
    }
}
