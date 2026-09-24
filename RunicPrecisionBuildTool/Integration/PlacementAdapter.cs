using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Splatform;
using UnityEngine;

namespace QuietBuildRotation
{
    internal static class PlacementAdapter
    {
        internal delegate bool PieceRayTestDelegate(
            Player player,
            out Vector3 point,
            out Vector3 normal,
            out Piece piece,
            out Heightmap heightmap,
            out Collider waterSurface,
            bool water);

        private delegate Piece GetSelectedPieceDelegate(Player player);
        private delegate bool InPlaceModeDelegate(Player player);
        private delegate bool TakeInputDelegate(Player player);
        private delegate void SetupPlacementGhostDelegate(Player player);

        private static AccessTools.FieldRef<Player, int> _placeRotation;
        private static AccessTools.FieldRef<Player, float> _scrollAmount;
        private static AccessTools.FieldRef<Player, int> _manualSnapPoint;
        private static AccessTools.FieldRef<Player, float> _placeRotationDegrees;
        private static AccessTools.FieldRef<Player, GameObject> _placementGhost;
        private static AccessTools.FieldRef<Player, PieceTable> _buildPieces;
        private static AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> _rightItem;

        private static PieceRayTestDelegate _pieceRayTest;
        private static GetSelectedPieceDelegate _getSelectedPiece;
        private static InPlaceModeDelegate _inPlaceMode;
        private static TakeInputDelegate _takeInput;
        private static SetupPlacementGhostDelegate _setupPlacementGhost;

        private static FieldInfo _placeRotationField;
        private static FieldInfo _placeRotationDegreesField;
        private static FieldInfo _tempPiecesField;
        private static MethodInfo _clearPiecesMethod;
        private static MethodInfo _quaternionEulerMethod;
        private static MethodInfo _composeRotationMethod;
        private static MethodInfo _applyTranslationMethod;

        internal static MethodInfo UpdatePlacementMethod { get; private set; }
        internal static MethodInfo UpdatePlacementGhostMethod { get; private set; }
        internal static MethodInfo SetupPlacementGhostMethod { get; private set; }
        internal static MethodInfo FindClosestSnapPointsMethod { get; private set; }
        internal static MethodInfo HandleRadialInputMethod { get; private set; }
        internal static MethodInfo PieceSetCreatorMethod { get; private set; }

        internal static bool PatchShapeVerified { get; private set; }
        internal static int RotationAnchorMatches { get; private set; }
        internal static int TranslationAnchorMatches { get; private set; }

        internal static bool Initialize(out string error)
        {
            error = null;
            try
            {
                _placeRotationField = RequireField("m_placeRotation", typeof(int));
                FieldInfo scrollAmountField = RequireField("m_scrollCurrAmount", typeof(float));
                FieldInfo manualSnapPointField = RequireField("m_manualSnapPoint", typeof(int));
                _placeRotationDegreesField = RequireField("m_placeRotationDegrees", typeof(float));
                FieldInfo placementGhostField = RequireField("m_placementGhost", typeof(GameObject));
                FieldInfo buildPiecesField = RequireField("m_buildPieces", typeof(PieceTable));
                FieldInfo rightItemField = AccessTools.Field(typeof(Humanoid), "m_rightItem");
                if (rightItemField == null || rightItemField.FieldType != typeof(ItemDrop.ItemData))
                    throw new MissingFieldException(typeof(Humanoid).FullName, "m_rightItem");
                _tempPiecesField = RequireField("m_tempPieces", typeof(List<Piece>));

                _placeRotation = AccessTools.FieldRefAccess<Player, int>(_placeRotationField);
                _scrollAmount = AccessTools.FieldRefAccess<Player, float>(scrollAmountField);
                _manualSnapPoint = AccessTools.FieldRefAccess<Player, int>(manualSnapPointField);
                _placeRotationDegrees = AccessTools.FieldRefAccess<Player, float>(_placeRotationDegreesField);
                _placementGhost = AccessTools.FieldRefAccess<Player, GameObject>(placementGhostField);
                _buildPieces = AccessTools.FieldRefAccess<Player, PieceTable>(buildPiecesField);
                _rightItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>(rightItemField);

                UpdatePlacementMethod = RequireMethod(
                    "UpdatePlacement", typeof(void), typeof(bool), typeof(float));
                UpdatePlacementGhostMethod = RequireMethod(
                    "UpdatePlacementGhost", typeof(void), typeof(bool));
                SetupPlacementGhostMethod = RequireMethod("SetupPlacementGhost", typeof(void));
                HandleRadialInputMethod = RequireMethod("HandleRadialInput", typeof(void));
                FindClosestSnapPointsMethod = RequireMethod(
                    "FindClosestSnapPoints",
                    typeof(bool),
                    typeof(Transform),
                    typeof(float),
                    typeof(Transform).MakeByRefType(),
                    typeof(Transform).MakeByRefType(),
                    typeof(List<Piece>));
                PieceSetCreatorMethod = AccessTools.Method(
                    typeof(Piece),
                    nameof(Piece.SetCreator),
                    new[] { typeof(long), typeof(PlatformUserID) });
                if (PieceSetCreatorMethod == null || PieceSetCreatorMethod.ReturnType != typeof(void))
                    throw new MissingMethodException(typeof(Piece).FullName, nameof(Piece.SetCreator));

                MethodInfo pieceRayTestMethod = RequireMethod(
                    "PieceRayTest",
                    typeof(bool),
                    typeof(Vector3).MakeByRefType(),
                    typeof(Vector3).MakeByRefType(),
                    typeof(Piece).MakeByRefType(),
                    typeof(Heightmap).MakeByRefType(),
                    typeof(Collider).MakeByRefType(),
                    typeof(bool));
                MethodInfo getSelectedPieceMethod = RequireMethod("GetSelectedPiece", typeof(Piece));
                MethodInfo inPlaceModeMethod = RequireMethod("InPlaceMode", typeof(bool));
                MethodInfo takeInputMethod = RequireMethod("TakeInput", typeof(bool));

                _pieceRayTest = AccessTools.MethodDelegate<PieceRayTestDelegate>(pieceRayTestMethod);
                _getSelectedPiece = AccessTools.MethodDelegate<GetSelectedPieceDelegate>(getSelectedPieceMethod);
                _inPlaceMode = AccessTools.MethodDelegate<InPlaceModeDelegate>(inPlaceModeMethod);
                _takeInput = AccessTools.MethodDelegate<TakeInputDelegate>(takeInputMethod);
                _setupPlacementGhost =
                    AccessTools.MethodDelegate<SetupPlacementGhostDelegate>(SetupPlacementGhostMethod);

                _quaternionEulerMethod = AccessTools.Method(
                    typeof(Quaternion), nameof(Quaternion.Euler),
                    new[] { typeof(float), typeof(float), typeof(float) });
                _clearPiecesMethod = AccessTools.Method(typeof(List<Piece>), nameof(List<Piece>.Clear));
                _composeRotationMethod = AccessTools.Method(
                    typeof(PlacementRuntime), nameof(PlacementRuntime.ComposeCandidateRotation));
                _applyTranslationMethod = AccessTools.Method(
                    typeof(PlacementRuntime),
                    nameof(PlacementRuntime.ApplyCandidateTranslation),
                    new[] { typeof(bool), typeof(Player) });

                if (_quaternionEulerMethod == null || _clearPiecesMethod == null ||
                    _composeRotationMethod == null || _applyTranslationMethod == null)
                    throw new MissingMethodException("one or more Runic placement hook methods could not be resolved");

                PatchShapeVerified = false;
                RotationAnchorMatches = 0;
                TranslationAnchorMatches = 0;
                return true;
            }
            catch (Exception exception)
            {
                error = global::Runic.Localization.RunicText.Format("text_72f7576af8fa", exception.GetType().Name, exception.Message);
                ClearDelegates();
                return false;
            }
        }

        internal static IEnumerable<CodeInstruction> TranspileUpdatePlacementGhost(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            List<int> rotationAnchors = FindRotationAnchors(code);
            List<int> translationAnchors = FindTranslationAnchors(code);

            RotationAnchorMatches = rotationAnchors.Count;
            TranslationAnchorMatches = translationAnchors.Count;
            PatchShapeVerified = rotationAnchors.Count == 1 && translationAnchors.Count == 1;
            if (!PatchShapeVerified)
            {
                Diagnostics.DisableAdapter(
                    $"UpdatePlacementGhost semantic anchors did not match exactly " +
                    $"(rotation={RotationAnchorMatches}, pre-snap={TranslationAnchorMatches})");
                return code;
            }

            code[rotationAnchors[0]].operand = _composeRotationMethod;

            // The local AltPlace flag is already on the stack here. The exact Runic hook consumes
            // and returns that flag after composing position. Ordinary relative movement returns
            // it unchanged; an explicit absolute-position/snap/repeat lock returns true so only
            // automatic snapping is skipped. All overlap/ward/collision/support validation after
            // this audited branch remains untouched.
            int insertAt = translationAnchors[0] + 1;
            code.Insert(insertAt, new CodeInstruction(OpCodes.Ldarg_0));
            code.Insert(insertAt + 1, new CodeInstruction(OpCodes.Call, _applyTranslationMethod));
            return code;
        }

        internal static bool CompleteRuntimeVerification(string harmonyId, out string error)
        {
            error = null;
            try
            {
                if (!PatchShapeVerified || RotationAnchorMatches != 1 || TranslationAnchorMatches != 1)
                    throw new InvalidOperationException(
                        $"semantic hook verification failed (rotation={RotationAnchorMatches}, pre-snap={TranslationAnchorMatches})");

                MethodBase[] requiredTargets =
                {
                    UpdatePlacementMethod,
                    UpdatePlacementGhostMethod,
                    SetupPlacementGhostMethod,
                    FindClosestSnapPointsMethod,
                    HandleRadialInputMethod,
                    PieceSetCreatorMethod
                };
                foreach (MethodBase target in requiredTargets)
                {
                    Patches patches = Harmony.GetPatchInfo(target);
                    if (patches == null || !patches.Owners.Contains(harmonyId))
                        throw new InvalidOperationException($"Harmony ownership missing for {target?.Name ?? "unknown method"}");
                }

                MethodInfo currentInstructionsMethod = typeof(PatchProcessor)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(method =>
                    {
                        if (method.Name != nameof(PatchProcessor.GetCurrentInstructions)) return false;
                        ParameterInfo[] parameters = method.GetParameters();
                        return parameters.Length == 3 && parameters[1].ParameterType == typeof(int);
                    });
                object instructionResult = currentInstructionsMethod.Invoke(
                    null,
                    new object[] { UpdatePlacementGhostMethod, int.MaxValue, null });
                IEnumerable<CodeInstruction> currentInstructions =
                    instructionResult as IEnumerable<CodeInstruction>;
                if (currentInstructions == null)
                    throw new InvalidOperationException("Harmony did not return the patched instruction stream");

                int composeCalls = 0;
                int translationCalls = 0;
                foreach (CodeInstruction instruction in currentInstructions)
                {
                    if (IsCall(instruction, _composeRotationMethod)) composeCalls++;
                    if (IsCall(instruction, _applyTranslationMethod)) translationCalls++;
                }
                if (composeCalls != 1 || translationCalls != 1)
                    throw new InvalidOperationException(
                        $"patched instruction verification failed " +
                        $"(rotation calls={composeCalls}, pre-snap calls={translationCalls})");

                return true;
            }
            catch (Exception exception)
            {
                error = global::Runic.Localization.RunicText.Format("text_31206b75eb09", exception.GetType().Name, exception.Message);
                return false;
            }
        }

        internal static GameObject GetPlacementGhost(Player player) =>
            player == null || _placementGhost == null ? null : _placementGhost(player);

        internal static int GetPlaceRotation(Player player) => _placeRotation(player);
        internal static void SetPlaceRotation(Player player, int value) => _placeRotation(player) = value;
        internal static float GetScrollAmount(Player player) => _scrollAmount(player);
        internal static void SetScrollAmount(Player player, float value) => _scrollAmount(player) = value;
        internal static int GetManualSnapPoint(Player player) => _manualSnapPoint(player);
        internal static void SetManualSnapPoint(Player player, int value) => _manualSnapPoint(player) = value;
        internal static float GetPlaceRotationDegrees(Player player) => _placeRotationDegrees(player);

        internal static bool IsInPlaceMode(Player player) =>
            player != null && _inPlaceMode != null && _inPlaceMode(player);

        internal static bool IsHammerBuildMode(Player player)
        {
            if (!IsInPlaceMode(player) || _rightItem == null || _buildPieces == null) return false;
            try
            {
                ItemDrop.ItemData item = _rightItem(player);
                PieceTable itemTable = item?.m_shared?.m_buildPieces;
                if (itemTable == null || !ReferenceEquals(itemTable, _buildPieces(player))) return false;

                string prefab = item.m_dropPrefab
                    ? Utils.GetPrefabName(item.m_dropPrefab)
                    : string.Empty;
                return string.Equals(prefab, "Hammer", System.StringComparison.Ordinal) ||
                       string.Equals(item.m_shared.m_name, "$item_hammer", System.StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        internal static bool CanTakeInput(Player player) =>
            player != null && _takeInput != null && _takeInput(player);

        internal static Piece GetSelectedPiece(Player player) =>
            player == null || _getSelectedPiece == null ? null : _getSelectedPiece(player);

        internal static PieceTable GetBuildPieceTable(Player player) =>
            player == null || _buildPieces == null ? null : _buildPieces(player);

        internal static void RefreshPlacementGhost(Player player)
        {
            if (player != null && _setupPlacementGhost != null)
                _setupPlacementGhost(player);
        }

        internal static int GetSelectedPrefabId(Player player)
        {
            Piece selectedPiece = GetSelectedPiece(player);
            return selectedPiece ? selectedPiece.gameObject.GetInstanceID() : 0;
        }

        internal static bool TryPieceRayTest(Player player, out Piece piece)
        {
            piece = null;
            if (player == null || _pieceRayTest == null) return false;

            bool hit = _pieceRayTest(
                player,
                out Vector3 point,
                out Vector3 normal,
                out piece,
                out Heightmap heightmap,
                out Collider waterSurface,
                false);
            return hit && piece;
        }

        private static FieldInfo RequireField(string name, Type expectedType)
        {
            FieldInfo field = AccessTools.Field(typeof(Player), name);
            if (field == null) throw new MissingFieldException(typeof(Player).FullName, name);
            if (field.FieldType != expectedType)
                throw new InvalidOperationException(
                    $"Player.{name} has type {field.FieldType.FullName}; expected {expectedType.FullName}");
            return field;
        }

        private static MethodInfo RequireMethod(string name, Type returnType, params Type[] parameters)
        {
            MethodInfo method = AccessTools.Method(typeof(Player), name, parameters);
            if (method == null) throw new MissingMethodException(typeof(Player).FullName, name);
            if (method.ReturnType != returnType)
                throw new InvalidOperationException(
                    $"Player.{name} returns {method.ReturnType.FullName}; expected {returnType.FullName}");
            return method;
        }

        private static List<int> FindRotationAnchors(IReadOnlyList<CodeInstruction> code)
        {
            List<int> matches = new List<int>();
            for (int i = 8; i < code.Count; i++)
            {
                if (!IsCall(code[i], _quaternionEulerMethod) ||
                    !IsLoadFloatZero(code[i - 8]) ||
                    code[i - 7].opcode != OpCodes.Ldarg_0 ||
                    code[i - 6].opcode != OpCodes.Ldfld ||
                    !Equals(code[i - 6].operand, _placeRotationDegreesField) ||
                    code[i - 5].opcode != OpCodes.Ldarg_0 ||
                    code[i - 4].opcode != OpCodes.Ldfld ||
                    !Equals(code[i - 4].operand, _placeRotationField) ||
                    code[i - 3].opcode != OpCodes.Conv_R4 ||
                    code[i - 2].opcode != OpCodes.Mul ||
                    !IsLoadFloatZero(code[i - 1]))
                    continue;

                matches.Add(i);
            }
            return matches;
        }

        private static List<int> FindTranslationAnchors(IReadOnlyList<CodeInstruction> code)
        {
            List<int> matches = new List<int>();
            for (int i = 0; i + 4 < code.Count; i++)
            {
                if (!LoadsLocal(code[i]) || !IsBranchTrue(code[i + 1]) ||
                    code[i + 2].opcode != OpCodes.Ldarg_0 ||
                    code[i + 3].opcode != OpCodes.Ldfld ||
                    !Equals(code[i + 3].operand, _tempPiecesField) ||
                    !IsCall(code[i + 4], _clearPiecesMethod))
                    continue;

                matches.Add(i);
            }
            return matches;
        }

        private static bool IsCall(CodeInstruction instruction, MethodInfo method) =>
            (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
            Equals(instruction.operand, method);

        private static bool IsLoadFloatZero(CodeInstruction instruction) =>
            instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float value && value == 0f;

        private static bool LoadsLocal(CodeInstruction instruction)
        {
            OpCode opcode = instruction.opcode;
            return opcode == OpCodes.Ldloc || opcode == OpCodes.Ldloc_S ||
                   opcode == OpCodes.Ldloc_0 || opcode == OpCodes.Ldloc_1 ||
                   opcode == OpCodes.Ldloc_2 || opcode == OpCodes.Ldloc_3;
        }

        private static bool IsBranchTrue(CodeInstruction instruction) =>
            instruction.opcode == OpCodes.Brtrue || instruction.opcode == OpCodes.Brtrue_S;

        private static void ClearDelegates()
        {
            _placeRotation = null;
            _scrollAmount = null;
            _manualSnapPoint = null;
            _placeRotationDegrees = null;
            _placementGhost = null;
            _buildPieces = null;
            _rightItem = null;
            _pieceRayTest = null;
            _getSelectedPiece = null;
            _inPlaceMode = null;
            _takeInput = null;
            _setupPlacementGhost = null;
            HandleRadialInputMethod = null;
            PieceSetCreatorMethod = null;
        }
    }
}
