using System;
using System.Collections.Generic;
using RunicAgriculture.Core;
using UnityEngine;

namespace RunicAgriculture.Integration
{
    internal readonly struct RuntimePlacementValidation
    {
        internal RuntimePlacementValidation(
            Vector3 position,
            Vector3 groundNormal,
            Heightmap.Biome biome,
            PlacementValidationResult result)
        {
            Position = position;
            GroundNormal = groundNormal;
            Biome = biome;
            Result = result;
        }

        internal Vector3 Position { get; }
        internal Vector3 GroundNormal { get; }
        internal Heightmap.Biome Biome { get; }
        internal PlacementValidationResult Result { get; }
    }

    internal sealed class ValheimPlacementValidator
    {
        private readonly Collider[] _spacingHits = new Collider[96];
        private readonly List<Character> _nearbyCharacters = new List<Character>(16);

        internal RuntimePlacementValidation Validate(
            Player player,
            Piece piece,
            Vector3 requested,
            Quaternion rotation)
        {
            if (player == null || piece == null || ZoneSystem.instance == null)
                return Invalid(requested, AgricultureReasonCodes.TerrainUnavailable);

            Plant plant = piece.GetComponent<Plant>();
            if (plant == null) return Invalid(requested, AgricultureReasonCodes.CropChanged);

            Vector3 groundPosition = requested;
            Vector3 normal;
            Heightmap.Biome biome;
            Heightmap.BiomeArea biomeArea;
            Heightmap heightmap;
            try
            {
                ZoneSystem.instance.GetGroundData(
                    ref groundPosition,
                    out normal,
                    out biome,
                    out biomeArea,
                    out heightmap);
            }
            catch (Exception)
            {
                return Invalid(requested, AgricultureReasonCodes.TerrainUnavailable);
            }

            bool terrainAvailable = heightmap != null;
            bool slopeValid = !piece.m_notOnTiltingSurface || normal.y >= 0.8f;
            Heightmap.Biome allowedBiome = piece.m_onlyInBiome != Heightmap.Biome.None
                ? piece.m_onlyInBiome
                : plant.m_biome;
            bool biomeValid = allowedBiome == Heightmap.Biome.None || (biome & allowedBiome) != 0;
            bool requiresCultivation = piece.m_cultivatedGroundOnly || plant.m_needCultivatedGround;
            bool cultivated = !requiresCultivation || terrainAvailable && heightmap.IsCultivated(groundPosition);
            float liquidLevel = Floating.GetLiquidLevel(groundPosition, 1f, LiquidType.All);
            bool waterClear = !piece.m_noInWater || liquidLevel <= groundPosition.y + 0.02f;
            bool spacingClear = IsSpacingClear(plant, groundPosition);
            bool inRange = PlacementRange.IsWithin(
                Vector3.Distance(player.GetEyePoint(), groundPosition),
                player.m_maxPlaceDistance,
                piece.m_extraPlacementDistance);
            bool authorized;
            try { authorized = PrivateArea.CheckAccess(groundPosition, 0f, false, false); }
            catch (Exception) { authorized = false; }
            bool worldBuildAllowed = !Location.IsInsideNoBuildLocation(groundPosition) &&
                                     (piece.m_allowedInDungeons || !Character.InInterior(groundPosition));
            bool playersClear = AreCharactersClear(player, groundPosition, rotation);

            var inputs = new PlacementValidationInputs(
                terrainAvailable,
                slopeValid,
                biomeValid,
                cultivated,
                spacingClear,
                inRange,
                authorized,
                worldBuildAllowed,
                playersClear,
                waterClear);
            return new RuntimePlacementValidation(
                groundPosition,
                normal,
                biome,
                PlacementValidation.Evaluate(inputs));
        }

        internal float RequiredSpacing(Piece piece)
        {
            Plant plant = piece != null ? piece.GetComponent<Plant>() : null;
            return plant != null ? Math.Max(0.1f, plant.m_growRadius) : 0.1f;
        }

        private bool IsSpacingClear(Plant candidate, Vector3 position)
        {
            float radius = Math.Max(0.1f, candidate.m_growRadius);
            int layerMask = LayerMask.GetMask(
                "Default",
                "static_solid",
                "Default_small",
                "piece",
                "piece_nonsolid");
            int count = Physics.OverlapSphereNonAlloc(
                position,
                radius,
                _spacingHits,
                layerMask,
                QueryTriggerInteraction.Collide);
            if (count >= _spacingHits.Length) return false;

            for (int index = 0; index < count; index++)
            {
                Collider hit = _spacingHits[index];
                _spacingHits[index] = null;
                if (hit == null || !hit.enabled) continue;
                Plant existing = hit.GetComponent<Plant>();
                if (existing == null) existing = hit.GetComponentInParent<Plant>();
                if (existing != null && existing.gameObject.activeInHierarchy &&
                    existing.GetStatus() != Plant.Status.Healthy) continue;
                // This mirrors Plant.HaveGrowSpace: non-plant colliders and healthy plants
                // block growth; an already-unhealthy plant is ignored by vanilla.
                return false;
            }

            if (candidate.m_growRadiusVines > 0f)
            {
                count = Physics.OverlapSphereNonAlloc(
                    position,
                    candidate.m_growRadiusVines,
                    _spacingHits,
                    layerMask,
                    QueryTriggerInteraction.Collide);
                if (count >= _spacingHits.Length) return false;
                for (int index = 0; index < count; index++)
                {
                    Collider hit = _spacingHits[index];
                    _spacingHits[index] = null;
                    if (hit != null && hit.GetComponentInParent<Vine>() != null) return false;
                }
            }
            return true;
        }

        private bool AreCharactersClear(Player player, Vector3 position, Quaternion rotation)
        {
            GameObject ghost = ValheimAccess.GetPlacementGhost(player);
            if (ghost == null) return false;
            _nearbyCharacters.Clear();
            Character.GetCharactersInRange(position, 30f, _nearbyCharacters);
            Collider[] placementColliders = ghost.GetComponentsInChildren<Collider>(true);
            Quaternion rootInverse = Quaternion.Inverse(ghost.transform.rotation);
            Vector3 rootScale = ghost.transform.lossyScale;
            for (int colliderIndex = 0; colliderIndex < placementColliders.Length; colliderIndex++)
            {
                Collider placement = placementColliders[colliderIndex];
                if (placement == null || !placement.enabled || placement.isTrigger ||
                    placement.gameObject == ghost) continue;
                if (placement is MeshCollider mesh && !mesh.convex) continue;
                Vector3 local = ghost.transform.InverseTransformPoint(placement.transform.position);
                Vector3 placementPosition = position + rotation * Vector3.Scale(local, rootScale);
                Quaternion placementRotation = rotation * rootInverse * placement.transform.rotation;
                for (int characterIndex = 0; characterIndex < _nearbyCharacters.Count; characterIndex++)
                {
                    CapsuleCollider character = _nearbyCharacters[characterIndex]?.GetCollider();
                    if (character == null || !character.enabled) continue;
                    if (Physics.ComputePenetration(
                            placement,
                            placementPosition,
                            placementRotation,
                            character,
                            character.transform.position,
                            character.transform.rotation,
                            out _,
                            out _)) return false;
                }
            }
            return true;
        }

        private static RuntimePlacementValidation Invalid(Vector3 position, string reason) =>
            new RuntimePlacementValidation(
                position,
                Vector3.up,
                Heightmap.Biome.None,
                new PlacementValidationResult(false, reason));
    }
}
