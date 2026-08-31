using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace RunicIntegrity
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicIntegrity";
        public const string Name = "Runic Integrity";
        public const string Version = "0.2.2";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> SearchRadius;
        internal static ConfigEntry<int> MaximumRoutePieces;
        internal static ConfigEntry<int> MaximumSolverExpansions;
        internal static ConfigEntry<float> SolverTimeBudgetMilliseconds;
        internal static ConfigEntry<float> GhostOpacity;
        internal static ConfigEntry<KeyboardShortcut> CycleKey;
        internal static BepInEx.Logging.ManualLogSource Log;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, "Show useful structural routes while placing weak pieces.");
            SearchRadius = Config.Bind("Solver", "SearchRadius", 12f,
                new ConfigDescription("Radius searched for strong structural anchors.", new AcceptableValueRange<float>(4f, 24f)));
            MaximumRoutePieces = Config.Bind("Solver", "MaximumRoutePieces", 3,
                new ConfigDescription("Maximum number of pieces in a suggested route.", new AcceptableValueRange<int>(1, 5)));
            MaximumSolverExpansions = Config.Bind("Solver", "MaximumExpansionsPerRefresh", 768,
                new ConfigDescription("Hard search-work bound for one route refresh.", new AcceptableValueRange<int>(64, 4096)));
            SolverTimeBudgetMilliseconds = Config.Bind("Solver", "TimeBudgetMilliseconds", 2f,
                new ConfigDescription("Main-thread time budget for one route refresh; partial safe results are allowed.", new AcceptableValueRange<float>(0.25f, 8f)));
            GhostOpacity = Config.Bind("Visual", "GhostOpacity", 0.34f,
                new ConfigDescription("Opacity of suggested pieces.", new AcceptableValueRange<float>(0.08f, 0.8f)));
            CycleKey = Config.Bind("Controls", "CycleSuggestion", new KeyboardShortcut(KeyCode.Tab),
                "Cycle through successful alternate routes.");
            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            if (!RouteAdvisor.SupportPatchCompatible)
                Logger.LogWarning(
                    "Structural route analysis is disabled because the installed support solver " +
                    "did not pass the exact IL compatibility check. Vanilla support is unchanged.");
            Logger.LogInfo($"{Name} v{Version} loaded.");
        }

        private void LateUpdate()
        {
            if (CycleKey.Value.IsDown()) RouteAdvisor.Cycle();
            RouteAdvisor.Update();
        }

        private void OnDestroy()
        {
            RouteAdvisor.Destroy();
            _harmony?.UnpatchSelf();
        }
    }

    internal sealed class Connector
    {
        internal GameObject Prefab;
        internal Vector3 EndA;
        internal Vector3 EndB;
        internal float Length;
        internal int Cost;
    }

    internal struct RoutePiece
    {
        internal Connector Connector;
        internal Vector3 Position;
        internal Quaternion Rotation;
        internal Vector3 FarEnd;
    }

    internal sealed class Route
    {
        internal readonly List<RoutePiece> Pieces = new List<RoutePiece>();
        internal Anchor Destination;
        internal float Score;
    }

    internal struct Anchor
    {
        internal Vector3 Position;
        internal float Strength;
        internal bool Foundation;
    }

    internal sealed class SearchState
    {
        internal Vector3 Point;
        internal Vector3 Start;
        internal readonly List<RoutePiece> Pieces = new List<RoutePiece>();
        internal float Heuristic;
    }

    internal static class RouteAdvisor
    {
        private const float GreenThreshold = 0.66f;
        private const float SnapTolerance = 0.5f;
        private const int YawSteps = 16;
        private const int BeamWidth = 24;
        private const int MaximumResults = 4;

        private static readonly AccessTools.FieldRef<Player, GameObject> PlacementGhost =
            AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");
        private static readonly FieldInfo BuildPieces = AccessTools.Field(typeof(Player), "m_buildPieces");
        private static readonly FieldInfo TablePieces = AccessTools.Field(typeof(PieceTable), "m_pieces");
        private static readonly MethodInfo PieceSnapPoints = AccessTools.Method(typeof(Piece), "GetSnapPoints", new[] { typeof(List<Transform>) });
        private static readonly MethodInfo UpdateSupport = AccessTools.Method(typeof(WearNTear), "UpdateSupport");
        private static readonly MethodInfo ClearSupport = AccessTools.Method(typeof(WearNTear), "ClearCachedSupport");
        private static readonly MethodInfo SetupColliders = AccessTools.Method(typeof(WearNTear), "SetupColliders");
        private static readonly MethodInfo SupportColor = AccessTools.Method(typeof(WearNTear), "GetSupportColorValue");
        private static readonly Collider[] Nearby = new Collider[512];
        private static readonly List<Transform> SnapBuffer = new List<Transform>(32);

        private static GameObject _visualRoot;
        private static readonly List<Material> VisualMaterials = new List<Material>();
        private static List<Route> _routes = new List<Route>();
        private static int _routeIndex;
        private static int _lastGhostId;
        private static Vector3 _lastPosition;
        private static Quaternion _lastRotation;
        private static float _nextRefresh;
        private static bool _analysisPending;
        private static bool _supportPatchCompatible;

        internal static bool SupportPatchCompatible => _supportPatchCompatible;

        internal static void Update()
        {
            Player player = Player.m_localPlayer;
            GameObject ghost = player ? PlacementGhost(player) : null;
            if (!Plugin.Enabled.Value || !player || !player.InPlaceMode() || !ghost || !ghost.activeInHierarchy)
            {
                Hide();
                return;
            }

            int id = ghost.GetInstanceID();
            bool moved = id != _lastGhostId || Vector3.SqrMagnitude(ghost.transform.position - _lastPosition) > 0.001f ||
                         Quaternion.Angle(ghost.transform.rotation, _lastRotation) > 0.5f;
            if (moved)
            {
                _lastGhostId = id;
                _lastPosition = ghost.transform.position;
                _lastRotation = ghost.transform.rotation;
                _nextRefresh = Time.unscaledTime + 0.18f;
                _analysisPending = true;
                return;
            }
            if (!_analysisPending || Time.unscaledTime < _nextRefresh) return;
            _analysisPending = false;
            Analyze(player, ghost);
        }

        internal static void Cycle()
        {
            if (_routes.Count < 2) return;
            _routeIndex = (_routeIndex + 1) % _routes.Count;
            Render(_routes[_routeIndex]);
        }

        internal static void Destroy()
        {
            DestroyVisuals();
            _routes.Clear();
        }

        internal static void SetSupportPatchCompatibility(bool compatible, string reason)
        {
            _supportPatchCompatible = compatible;
            if (!compatible)
            {
                ClearRoutes();
                Plugin.Log?.LogError(
                    "Runic Integrity structural probing was disabled because the installed " +
                    "WearNTear.UpdateSupport IL contract did not match exactly: " + reason);
            }
        }

        private static void Analyze(Player player, GameObject ghost)
        {
            if (!_supportPatchCompatible)
            {
                ClearRoutes();
                return;
            }
            WearNTear targetWear = ghost.GetComponent<WearNTear>();
            Piece targetPiece = ghost.GetComponent<Piece>();
            if (!targetWear || !targetPiece || !targetWear.m_supports || ProbeColor(targetWear) >= GreenThreshold)
            {
                ClearRoutes();
                return;
            }

            List<Vector3> starts = WorldSnapPoints(targetPiece);
            List<Anchor> anchors = FindAnchors(ghost, targetWear);
            List<Connector> connectors = BuildConnectors(player);
            if (starts.Count == 0 || anchors.Count == 0 || connectors.Count == 0)
            {
                ClearRoutes();
                return;
            }

            _routes = Solve(starts, anchors, connectors, Plugin.MaximumRoutePieces.Value);
            Plugin.Log.LogDebug($"Route analysis: {starts.Count} target snaps, {anchors.Count} anchor snaps, " +
                                $"{connectors.Count} connector types, {_routes.Count} accepted routes.");
            _routeIndex = 0;
            if (_routes.Count == 0) Hide();
            else Render(_routes[0]);
        }

        private static List<Route> Solve(List<Vector3> starts, List<Anchor> anchors, List<Connector> connectors, int maxPieces)
        {
            var results = new List<Route>();
            var frontier = starts.Select(point => new SearchState { Point = point, Start = point, Heuristic = NearestDistance(point, anchors) }).ToList();
            var visited = new Dictionary<Vector3Int, int>();
            int expansions = 0;
            int expansionLimit = Mathf.Clamp(Plugin.MaximumSolverExpansions.Value, 64, 4096);
            double milliseconds = Mathf.Clamp(Plugin.SolverTimeBudgetMilliseconds.Value, 0.25f, 8f);
            long started = Stopwatch.GetTimestamp();
            long timeBudgetTicks = Math.Max(
                1L,
                (long)Math.Ceiling(milliseconds / 1000d * Stopwatch.Frequency));
            bool budgetExhausted = false;

            for (int depth = 1; depth <= maxPieces && frontier.Count > 0 &&
                 results.Count < MaximumResults && !budgetExhausted; depth++)
            {
                var next = new List<SearchState>();
                foreach (SearchState state in frontier)
                {
                    foreach (Connector connector in connectors)
                    {
                        for (int direction = 0; direction < 2; direction++)
                        {
                            Vector3 localNear = direction == 0 ? connector.EndA : connector.EndB;
                            Vector3 localFar = direction == 0 ? connector.EndB : connector.EndA;
                            for (int yaw = 0; yaw < YawSteps; yaw++)
                            {
                                expansions++;
                                if (expansions > expansionLimit ||
                                    (expansions & 15) == 0 &&
                                    Stopwatch.GetTimestamp() - started >= timeBudgetTicks)
                                {
                                    budgetExhausted = true;
                                    break;
                                }
                                Quaternion rotation = Quaternion.Euler(0f, yaw * (360f / YawSteps), 0f);
                                Vector3 position = state.Point - rotation * localNear;
                                Vector3 far = position + rotation * localFar;
                                if (Vector3.Distance(far, state.Point) < 0.4f) continue;
                                if (Vector3.Distance(far, state.Start) > Plugin.SearchRadius.Value + 1f) continue;

                                RoutePiece placement = new RoutePiece
                                {
                                    Connector = connector,
                                    Position = position,
                                    Rotation = rotation,
                                    FarEnd = far
                                };

                                if (TryReachAnchor(far, anchors, out Anchor destination))
                                {
                                    var route = new Route { Destination = destination };
                                    route.Pieces.AddRange(state.Pieces);
                                    route.Pieces.Add(placement);
                                    route.Score = Score(route, state.Start);
                                    results.Add(route);
                                    if (results.Count >= MaximumResults) break;
                                }

                                Vector3Int key = Quantize(far);
                                if (visited.TryGetValue(key, out int oldDepth) && oldDepth <= depth) continue;
                                visited[key] = depth;
                                var child = new SearchState { Point = far, Start = state.Start };
                                child.Pieces.AddRange(state.Pieces);
                                child.Pieces.Add(placement);
                                child.Heuristic = NearestDistance(far, anchors) + depth * 0.12f;
                                next.Add(child);
                            }
                            if (budgetExhausted) break;
                            if (results.Count >= MaximumResults) break;
                        }
                        if (budgetExhausted) break;
                        if (results.Count >= MaximumResults) break;
                    }
                    if (budgetExhausted) break;
                    if (results.Count >= MaximumResults) break;
                }
                frontier = next.OrderBy(x => x.Heuristic).Take(BeamWidth).ToList();
            }

            List<Route> plausible = results.Where(PlausiblyReachesGreen).OrderBy(route => route.Score).ToList();
            return plausible.Take(3).ToList();
        }

        private static bool PlausiblyReachesGreen(Route route)
        {
            // Native support is nonlinear and material-dependent. Only surface short routes to
            // very strong anchors; weak-anchor guesses are rejected.
            float required = 0.90f + Mathf.Max(0, route.Pieces.Count - 2) * 0.04f;
            return route.Destination.Foundation || route.Destination.Strength >= required;
        }

        private static float Score(Route route, Vector3 start)
        {
            int materialCost = route.Pieces.Sum(piece => piece.Connector.Cost);
            float horizontal = Vector2.Distance(new Vector2(start.x, start.z),
                new Vector2(route.Destination.Position.x, route.Destination.Position.z));
            float obviousVerticalPenalty = horizontal < 0.45f ? 8f : 0f;
            float foundationBonus = route.Destination.Foundation ? -0.4f : 0f;
            return route.Pieces.Count * 10f + materialCost * 0.25f + obviousVerticalPenalty + foundationBonus;
        }

        private static List<Anchor> FindAnchors(GameObject target, WearNTear targetWear)
        {
            var anchors = new List<Anchor>();
            var seen = new HashSet<int>();
            int count = Physics.OverlapSphereNonAlloc(target.transform.position, Plugin.SearchRadius.Value, Nearby,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider collider = Nearby[i];
                WearNTear wear = collider ? collider.GetComponentInParent<WearNTear>() : null;
                if (!wear || wear == targetWear || !wear.m_supports || !seen.Add(wear.GetInstanceID())) continue;
                float strength = ProbeColor(wear, false);
                if (strength < GreenThreshold) continue;
                Piece piece = wear.GetComponent<Piece>();
                if (!piece) continue;
                bool foundation = strength >= 0.99f;
                foreach (Vector3 point in WorldSnapPoints(piece))
                    if (Vector3.Distance(point, target.transform.position) <= Plugin.SearchRadius.Value + 1f)
                        anchors.Add(new Anchor { Position = point, Strength = strength, Foundation = foundation });
            }
            return anchors.OrderByDescending(x => x.Strength).Take(96).ToList();
        }

        private static List<Connector> BuildConnectors(Player player)
        {
            var result = new List<Connector>();
            object table = BuildPieces?.GetValue(player);
            IEnumerable pieces = table != null ? TablePieces?.GetValue(table) as IEnumerable : null;
            if (pieces == null) return result;
            foreach (object item in pieces)
            {
                GameObject prefab = item as GameObject;
                if (!prefab || !IsStructuralConnector(prefab) || !prefab.GetComponent<WearNTear>()) continue;
                Piece piece = prefab.GetComponent<Piece>();
                List<Vector3> snaps = LocalSnapPoints(piece);
                if (snaps.Count < 2) continue;
                FindFarthestPair(snaps, out Vector3 a, out Vector3 b, out float length);
                if (length < 0.7f || length > 5.1f) continue;
                result.Add(new Connector
                {
                    Prefab = prefab,
                    EndA = a,
                    EndB = b,
                    Length = length,
                    Cost = ResourceCost(piece)
                });
            }
            return result.OrderByDescending(x => x.Length).Take(12).ToList();
        }

        private static bool IsStructuralConnector(GameObject prefab)
        {
            string name = PrefabName(prefab).ToLowerInvariant();
            return name.Contains("beam") || name.Contains("pole") || name.Contains("log") || name.Contains("woodiron");
        }

        private static int ResourceCost(Piece piece)
        {
            try
            {
                if (piece.m_resources == null) return 1;
                int total = 0;
                foreach (Piece.Requirement requirement in piece.m_resources) total += Mathf.Max(1, requirement.m_amount);
                return Mathf.Max(1, total);
            }
            catch { return 1; }
        }

        private static List<Vector3> WorldSnapPoints(Piece piece)
        {
            SnapBuffer.Clear();
            try { PieceSnapPoints?.Invoke(piece, new object[] { SnapBuffer }); }
            catch { }
            var result = new List<Vector3>(SnapBuffer.Count);
            foreach (Transform snap in SnapBuffer) if (snap) result.Add(snap.position);
            return result;
        }

        private static List<Vector3> LocalSnapPoints(Piece piece)
        {
            SnapBuffer.Clear();
            try { PieceSnapPoints?.Invoke(piece, new object[] { SnapBuffer }); }
            catch { }
            var result = new List<Vector3>(SnapBuffer.Count);
            Transform root = piece.transform;
            foreach (Transform snap in SnapBuffer) if (snap) result.Add(root.InverseTransformPoint(snap.position));
            return result;
        }

        private static void FindFarthestPair(List<Vector3> points, out Vector3 a, out Vector3 b, out float distance)
        {
            a = points[0];
            b = points[1];
            distance = Vector3.Distance(a, b);
            for (int i = 0; i < points.Count; i++)
                for (int j = i + 1; j < points.Count; j++)
                {
                    float candidate = Vector3.Distance(points[i], points[j]);
                    if (candidate <= distance) continue;
                    a = points[i];
                    b = points[j];
                    distance = candidate;
                }
        }

        private static bool TryReachAnchor(Vector3 point, List<Anchor> anchors, out Anchor anchor)
        {
            float best = SnapTolerance;
            anchor = default;
            bool found = false;
            foreach (Anchor candidate in anchors)
            {
                float distance = Vector3.Distance(point, candidate.Position);
                if (distance > best) continue;
                best = distance;
                anchor = candidate;
                found = true;
            }
            return found;
        }

        private static float NearestDistance(Vector3 point, List<Anchor> anchors)
        {
            float best = float.MaxValue;
            foreach (Anchor anchor in anchors) best = Mathf.Min(best, Vector3.Distance(point, anchor.Position));
            return best;
        }

        private static Vector3Int Quantize(Vector3 point) => new Vector3Int(
            Mathf.RoundToInt(point.x / SnapTolerance), Mathf.RoundToInt(point.y / SnapTolerance), Mathf.RoundToInt(point.z / SnapTolerance));

        private static float ProbeColor(WearNTear wear, bool update = true)
        {
            if (!wear) return 0f;
            if (update && !_supportPatchCompatible) return 0f;
            try
            {
                if (update)
                {
                    ClearSupport?.Invoke(wear, null);
                    SetupColliders?.Invoke(wear, null);
                    try { UpdateSupport?.Invoke(wear, null); }
                    catch (TargetInvocationException e) when (e.InnerException is NullReferenceException) { }
                }
                return SupportColor != null ? Mathf.Clamp01((float)SupportColor.Invoke(wear, null)) : 0f;
            }
            catch { return 0f; }
        }

        private static void Render(Route route)
        {
            DestroyVisuals();
            _visualRoot = new GameObject("RunicIntegrity_Route") { hideFlags = HideFlags.HideAndDontSave };
            Color green = new Color(0.12f, 1f, 0.25f, Plugin.GhostOpacity.Value);
            Color cyan = new Color(0.08f, 0.72f, 1f, Plugin.GhostOpacity.Value);
            for (int i = 0; i < route.Pieces.Count; i++)
            {
                RoutePiece placement = route.Pieces[i];
                Color color = route.Destination.Foundation && i == route.Pieces.Count - 1 ? cyan : green;
                CreateMeshOnlyVisual(placement, color);
            }
        }

        private static void CreateMeshOnlyVisual(RoutePiece placement, Color color)
        {
            GameObject pieceRoot = new GameObject("SuggestedMesh_" + PrefabName(placement.Connector.Prefab));
            pieceRoot.hideFlags = HideFlags.HideAndDontSave;
            pieceRoot.transform.SetParent(_visualRoot.transform, false);
            pieceRoot.transform.position = placement.Position;
            pieceRoot.transform.rotation = placement.Rotation;
            Transform prefabRoot = placement.Connector.Prefab.transform;
            Vector3 rootScale = prefabRoot.lossyScale;
            foreach (MeshFilter sourceFilter in placement.Connector.Prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshRenderer sourceRenderer = sourceFilter.GetComponent<MeshRenderer>();
                if (!sourceRenderer || !sourceFilter.sharedMesh) continue;
                GameObject meshObject = new GameObject(sourceFilter.gameObject.name + "_MeshOnly");
                meshObject.hideFlags = HideFlags.HideAndDontSave;
                meshObject.transform.SetParent(pieceRoot.transform, false);
                meshObject.transform.localPosition = prefabRoot.InverseTransformPoint(sourceFilter.transform.position);
                meshObject.transform.localRotation = Quaternion.Inverse(prefabRoot.rotation) * sourceFilter.transform.rotation;
                Vector3 sourceScale = sourceFilter.transform.lossyScale;
                meshObject.transform.localScale = new Vector3(
                    SafeDivide(sourceScale.x, rootScale.x), SafeDivide(sourceScale.y, rootScale.y), SafeDivide(sourceScale.z, rootScale.z));
                meshObject.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
                MeshRenderer renderer = meshObject.AddComponent<MeshRenderer>();
                Material[] sourceMaterials = sourceRenderer.sharedMaterials;
                Material[] materials = new Material[sourceMaterials.Length];
                for (int i = 0; i < materials.Length; i++)
                {
                    if (!sourceMaterials[i]) continue;
                    Material material = new Material(sourceMaterials[i]) { hideFlags = HideFlags.HideAndDontSave };
                    material.color = color;
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_ZWrite", 0);
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.EnableKeyword("_ALPHABLEND_ON");
                    material.renderQueue = 3000;
                    materials[i] = material;
                    VisualMaterials.Add(material);
                }
                renderer.materials = materials;
            }
        }

        private static float SafeDivide(float value, float divisor) => Mathf.Abs(divisor) < 0.0001f ? value : value / divisor;

        private static string PrefabName(GameObject go)
        {
            string name = go ? go.name : string.Empty;
            const string clone = "(Clone)";
            return name.EndsWith(clone, StringComparison.Ordinal) ? name.Substring(0, name.Length - clone.Length) : name;
        }

        private static void ClearRoutes()
        {
            _routes.Clear();
            _routeIndex = 0;
            DestroyVisuals();
        }

        private static void Hide()
        {
            if (_visualRoot) _visualRoot.SetActive(false);
        }

        private static void DestroyVisuals()
        {
            if (_visualRoot) UnityEngine.Object.Destroy(_visualRoot);
            _visualRoot = null;
            for (int index = 0; index < VisualMaterials.Count; index++)
                if (VisualMaterials[index]) UnityEngine.Object.Destroy(VisualMaterials[index]);
            VisualMaterials.Clear();
        }
    }

    internal static class SafeNView
    {
        internal static bool IsOwner(ZNetView view) => view && view.IsOwner();
        internal static bool IsValid(ZNetView view) => view && view.IsValid();
    }

    // Allows Valheim's native support scan to evaluate an unplaced placement ghost.
    // Technique adapted from MIT-licensed StructureHealth; see NOTICE.
    [HarmonyPatch(typeof(WearNTear), "UpdateSupport")]
    internal static class WearNTearUpdateSupportPatch
    {
        private static readonly MethodInfo NViewIsOwner = AccessTools.Method(typeof(ZNetView), "IsOwner");
        private static readonly MethodInfo NViewIsValid = AccessTools.Method(typeof(ZNetView), "IsValid");
        private static readonly MethodInfo SafeIsOwner = AccessTools.Method(typeof(SafeNView), nameof(SafeNView.IsOwner));
        private static readonly MethodInfo SafeIsValid = AccessTools.Method(typeof(SafeNView), nameof(SafeNView.IsValid));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = instructions.ToList();
            List<int> guards = FindEarlyExitGuards(list);
            int ownerCalls = CountCalls(list, NViewIsOwner);
            int validCalls = CountCalls(list, NViewIsValid);
            int guard = guards.Count == 1 ? guards[0] : -1;
            Label? continueLabel = guard > 0 ? PriorContinueLabel(list, guard) : null;
            if (NViewIsOwner == null || NViewIsValid == null || SafeIsOwner == null ||
                SafeIsValid == null || ownerCalls != 1 || validCalls != 1 ||
                guard < 0 || !continueLabel.HasValue)
            {
                RouteAdvisor.SetSupportPatchCompatibility(
                    false,
                    $"ownerCalls={ownerCalls}, validCalls={validCalls}, guards={guards.Count}, " +
                    $"continueLabel={(continueLabel.HasValue ? 1 : 0)}");
                return list;
            }

            for (int i = 0; i < list.Count; i++)
            {
                CodeInstruction instruction = list[i];
                if (instruction.opcode == OpCodes.Callvirt && instruction.operand is MethodInfo method)
                {
                    if (method == NViewIsOwner)
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = SafeIsOwner;
                    }
                    else if (method == NViewIsValid)
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = SafeIsValid;
                    }
                }
            }
            list.Insert(guard + 1, new CodeInstruction(OpCodes.Br, continueLabel.Value));
            RouteAdvisor.SetSupportPatchCompatibility(true, string.Empty);
            return list;
        }

        private static List<int> FindEarlyExitGuards(List<CodeInstruction> list)
        {
            var result = new List<int>();
            for (int i = 1; i < list.Count - 3; i++)
            {
                if (list[i].opcode != OpCodes.Brfalse_S && list[i].opcode != OpCodes.Brfalse) continue;
                if (list[i - 1].opcode != OpCodes.Call || (list[i - 1].operand as MethodInfo)?.Name != "op_Equality") continue;
                if (list[i + 1].opcode == OpCodes.Ldarg_0 && list[i + 3].opcode == OpCodes.Stfld &&
                    (list[i + 3].operand as FieldInfo)?.Name == "m_support") result.Add(i);
            }
            return result;
        }

        private static int CountCalls(List<CodeInstruction> list, MethodInfo target) =>
            target == null
                ? 0
                : list.Count(instruction =>
                    instruction.opcode == OpCodes.Callvirt && Equals(instruction.operand, target));

        private static Label? PriorContinueLabel(List<CodeInstruction> list, int before)
        {
            for (int i = before - 1; i >= 0; i--)
                if (list[i].opcode == OpCodes.Br && list[i].operand is Label label) return label;
            return null;
        }
    }
}
