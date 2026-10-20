using System;
using System.Collections.Generic;
using System.IO;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using RunicHallOfHeroes.Displays;
using RunicHallOfHeroes.Echoes;
using UnityEngine;

namespace RunicHallOfHeroes
{
    internal static class PieceRegistrar
    {
        private enum SnapProfile
        {
            Planar,
            Axial,
            Block
        }

        private const string HallCategory = "Hall of Heroes";
        private static bool _registered;
        private static Texture2D _stackedStoneTexture;
        private static Texture2D _carvedStoneTexture;
        private static Texture2D _mahoganyTexture;
        private static Texture2D _cleanStoneFloorTexture;

        internal static void Register()
        {
            if (_registered) return;
            _registered = true;
            PrefabManager.OnVanillaPrefabsAvailable -= Register;

            _stackedStoneTexture = LoadTexture("runic_stone_facade.png");
            _carvedStoneTexture = LoadTexture("monumental_carved_stone.png");
            _mahoganyTexture = LoadTexture("majestic_mahogany.png");
            _cleanStoneFloorTexture = LoadTexture("runic_stone_floor_clean.png");

            RegisterStoneFamily(
                "runic_stacked",
                "Rune-Stacked Stone",
                "Fitted rune-cut masonry for the walls of a heroic hall.",
                _stackedStoneTexture,
                new Color(0.88f, 0.88f, 0.84f));
            RegisterStoneFamily(
                "monumental",
                "Monumental Carved Stone",
                "Great smooth-cut slabs bearing restrained heroic reliefs.",
                _carvedStoneTexture,
                new Color(0.82f, 0.86f, 0.92f));
            RegisterMahoganyFamily();

            RegisterHeroDisplay();
            RegisterRunicDisplayLight();
            RegisterHeroPlaque();
            RegisterEchoRunestone();
            Plugin.Log.LogInfo("Registered the first Runic Hall architecture and chronicle displays.");
        }

        private static void RegisterStoneFamily(
            string family,
            string familyName,
            string description,
            Texture2D texture,
            Color tint)
        {
            RegisterMaterialPiece($"piece_{family}_wall", "stone_wall_4x2",
                $"{familyName} Full Wall", description, texture, tint, "Stone", 12, "Bronze", 1,
                PieceCategories.Building, CraftingStations.Stonecutter);
            RegisterMaterialPiece($"piece_{family}_wall_half", "stone_wall_2x1",
                $"{familyName} Half Wall", description, texture, tint, "Stone", 6, "Bronze", 1,
                PieceCategories.Building, CraftingStations.Stonecutter);
            RegisterMaterialPiece($"piece_{family}_brick_single", "stone_wall_1x1",
                $"{familyName} Single Brick", description, texture, tint, "Stone", 3, "Bronze", 1,
                PieceCategories.Building, CraftingStations.Stonecutter, SnapProfile.Block);
            RegisterMaterialPiece($"piece_{family}_brick_double", "stone_wall_2x1",
                $"{familyName} Double Brick", description, texture, tint, "Stone", 5, "Bronze", 1,
                PieceCategories.Building, CraftingStations.Stonecutter, SnapProfile.Block);
            RegisterMaterialPiece($"piece_{family}_arch", "stone_arch",
                $"{familyName} Arch", description, texture, tint, "Stone", 10, "Bronze", 1,
                PieceCategories.Building, CraftingStations.Stonecutter);
            RegisterMaterialPiece($"piece_{family}_column", "stone_pillar",
                $"{familyName} Grand Column", description, texture, tint, "Stone", 8, "Bronze", 1,
                PieceCategories.Building, CraftingStations.Stonecutter, SnapProfile.Axial);
            RegisterMaterialPiece($"piece_{family}_column_small", "blackmarble_column_1",
                $"{familyName} Compact Column", description, texture, tint, "Stone", 4, "Bronze", 1,
                PieceCategories.Building, CraftingStations.Stonecutter, SnapProfile.Axial);
            RegisterMaterialPiece($"piece_{family}_floor", "blackmarble_floor",
                $"{familyName} Floor 2x2", description, _cleanStoneFloorTexture ?? texture, tint, "Stone", 10, "Bronze", 1,
                PieceCategories.Building, CraftingStations.Stonecutter);
            RegisterMaterialPiece($"piece_{family}_floor_triangle", "blackmarble_floor_triangle",
                $"{familyName} Triangle Floor", description, _cleanStoneFloorTexture ?? texture, tint, "Stone", 5, "Bronze", 1,
                PieceCategories.Building, CraftingStations.Stonecutter);
            RegisterMaterialPiece($"piece_{family}_stair", "stone_stair",
                $"{familyName} Stair", description, texture, tint, "Stone", 8, "Bronze", 1,
                PieceCategories.Building, CraftingStations.Stonecutter);
            RegisterMaterialPiece($"piece_{family}_plinth", "blackmarble_base_1",
                $"{familyName} Plinth", description, texture, tint, "Stone", 5, "Bronze", 1,
                PieceCategories.Building, CraftingStations.Stonecutter);
            RegisterMaterialPiece($"piece_{family}_plinth_corner", "blackmarble_basecorner",
                $"{familyName} Plinth Corner", description, texture, tint, "Stone", 6, "Bronze", 1,
                PieceCategories.Building, CraftingStations.Stonecutter);
            RegisterMaterialPiece($"piece_{family}_cornice", "blackmarble_out_1",
                $"{familyName} Cornice", description, texture, tint, "Stone", 5, "Bronze", 1,
                PieceCategories.Building, CraftingStations.Stonecutter);
            RegisterMaterialPiece($"piece_{family}_cornice_corner", "blackmarble_outcorner",
                $"{familyName} Cornice Corner", description, texture, tint, "Stone", 6, "Bronze", 1,
                PieceCategories.Building, CraftingStations.Stonecutter);
            RegisterMaterialPiece($"piece_{family}_spire", "blackmarble_tip",
                $"{familyName} Quarter Spire", description, texture, tint, "Stone", 4, "Bronze", 1,
                PieceCategories.Building, CraftingStations.Stonecutter);
        }

        private static void RegisterMahoganyFamily()
        {
            Color tint = new Color(0.95f, 0.82f, 0.78f);
            const string description =
                "Deep mahogany timber, hand-finished and carved for a majestic great hall.";
            RegisterMaterialPiece("piece_majestic_mahogany_wall", "woodwall",
                "Majestic Mahogany Wall", description, _mahoganyTexture, tint,
                "FineWood", 8, "Bronze", 1, PieceCategories.Building, CraftingStations.Workbench);
            RegisterMaterialPiece("piece_majestic_mahogany_wall_half", "wood_wall_half",
                "Majestic Mahogany Half Wall", description, _mahoganyTexture, tint,
                "FineWood", 4, "Bronze", 1, PieceCategories.Building, CraftingStations.Workbench);
            RegisterMaterialPiece("piece_majestic_mahogany_brick_single", "wood_wall_quarter",
                "Majestic Mahogany Single Block", description, _mahoganyTexture, tint,
                "FineWood", 3, "Bronze", 1, PieceCategories.Building, CraftingStations.Workbench,
                SnapProfile.Block);
            RegisterMaterialPiece("piece_majestic_mahogany_brick_double", "wood_wall_half",
                "Majestic Mahogany Double Block", description, _mahoganyTexture, tint,
                "FineWood", 5, "Bronze", 1, PieceCategories.Building, CraftingStations.Workbench,
                SnapProfile.Block);
            RegisterMaterialPiece("piece_majestic_mahogany_floor", "wood_floor",
                "Majestic Mahogany Floor", description, _mahoganyTexture, tint,
                "FineWood", 6, "Bronze", 1, PieceCategories.Building, CraftingStations.Workbench);
            RegisterMaterialPiece("piece_majestic_mahogany_floor_small", "wood_floor_1x1",
                "Majestic Mahogany Floor 1x1", description, _mahoganyTexture, tint,
                "FineWood", 3, "Bronze", 1, PieceCategories.Building, CraftingStations.Workbench);
            RegisterMaterialPiece("piece_majestic_mahogany_beam", "darkwood_beam",
                "Majestic Mahogany Beam 2m", description, _mahoganyTexture, tint,
                "FineWood", 4, "Bronze", 1, PieceCategories.Building, CraftingStations.Workbench,
                SnapProfile.Axial);
            RegisterMaterialPiece("piece_majestic_mahogany_beam_long", "darkwood_beam4x4",
                "Majestic Mahogany Beam 4m", description, _mahoganyTexture, tint,
                "FineWood", 8, "Bronze", 1, PieceCategories.Building, CraftingStations.Workbench,
                SnapProfile.Axial);
            RegisterMaterialPiece("piece_majestic_mahogany_beam_half", "wood_beam_1",
                "Majestic Mahogany Beam 1m", description, _mahoganyTexture, tint,
                "FineWood", 2, "Bronze", 1, PieceCategories.Building, CraftingStations.Workbench,
                SnapProfile.Axial);
            RegisterMaterialPiece("piece_majestic_mahogany_pole", "darkwood_pole",
                "Majestic Mahogany Upright Beam 2m", description, _mahoganyTexture, tint,
                "FineWood", 4, "Bronze", 1, PieceCategories.Building, CraftingStations.Workbench,
                SnapProfile.Axial);
            RegisterMaterialPiece("piece_majestic_mahogany_pole_long", "darkwood_pole4",
                "Majestic Mahogany Upright Beam 4m", description, _mahoganyTexture, tint,
                "FineWood", 8, "Bronze", 1, PieceCategories.Building, CraftingStations.Workbench,
                SnapProfile.Axial);
            RegisterMaterialPiece("piece_majestic_mahogany_pole_half", "wood_pole",
                "Majestic Mahogany Upright Beam 1m", description, _mahoganyTexture, tint,
                "FineWood", 2, "Bronze", 1, PieceCategories.Building, CraftingStations.Workbench,
                SnapProfile.Axial);
            RegisterMaterialPiece("piece_majestic_mahogany_column_small", "darkwood_pole",
                "Majestic Mahogany Compact Column", description, _mahoganyTexture, tint,
                "FineWood", 4, "Bronze", 1, PieceCategories.Building, CraftingStations.Workbench,
                SnapProfile.Axial);
            RegisterMaterialPiece("piece_majestic_mahogany_beam_26", "darkwood_beam_26",
                "Majestic Mahogany Beam 26°", description, _mahoganyTexture, tint,
                "FineWood", 4, "Bronze", 1, PieceCategories.Building, CraftingStations.Workbench,
                SnapProfile.Axial);
            RegisterMaterialPiece("piece_majestic_mahogany_beam_45", "darkwood_beam_45",
                "Majestic Mahogany Beam 45°", description, _mahoganyTexture, tint,
                "FineWood", 4, "Bronze", 1, PieceCategories.Building, CraftingStations.Workbench,
                SnapProfile.Axial);
            RegisterMaterialPiece("piece_majestic_mahogany_arch", "darkwood_arch",
                "Majestic Mahogany Arch", description, _mahoganyTexture, tint,
                "FineWood", 6, "Bronze", 1, PieceCategories.Building, CraftingStations.Workbench);
            RegisterMaterialPiece("piece_majestic_mahogany_stair", "wood_stair",
                "Majestic Mahogany Stair", description, _mahoganyTexture, tint,
                "FineWood", 8, "Bronze", 1, PieceCategories.Building, CraftingStations.Workbench);
            RegisterMaterialPiece("piece_majestic_mahogany_divider", "darkwood_decowall",
                "Majestic Mahogany Carved Divider", description, _mahoganyTexture, tint,
                "FineWood", 5, "Bronze", 1, PieceCategories.Building, CraftingStations.Workbench);
            RegisterMaterialPiece("piece_majestic_mahogany_gate", "darkwood_gate",
                "Majestic Mahogany Gate", description, _mahoganyTexture, tint,
                "FineWood", 12, "Bronze", 2, PieceCategories.Building, CraftingStations.Workbench);
        }

        private static void RegisterMaterialPiece(
            string prefabName,
            string sourceName,
            string displayName,
            string description,
            Texture2D texture,
            Color tint,
            string primaryResource,
            int primaryAmount,
            string trimResource,
            int trimAmount,
            string category,
            string craftingStation,
            SnapProfile snapProfile = SnapProfile.Planar)
        {
            GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(prefabName, sourceName);
            if (prefab == null)
            {
                Plugin.Log.LogError($"Could not register {displayName}: source prefab '{sourceName}' was not found.");
                return;
            }
            ApplyMaterial(prefab, texture, tint);
            NormalizeSnapPoints(prefab, snapProfile);
            var config = BaseConfig(displayName, description);
            config.Category = HallCategory;
            config.CraftingStation = craftingStation;
            config.AddRequirement(primaryResource, primaryAmount);
            config.AddRequirement(trimResource, trimAmount);
            PieceManager.Instance.AddPiece(new CustomPiece(prefab, false, config));
        }

        private static void NormalizeSnapPoints(GameObject prefab, SnapProfile profile)
        {
            var oldPoints = new List<Transform>();
            foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (child != prefab.transform &&
                    child.name.StartsWith("_snappoint", StringComparison.OrdinalIgnoreCase))
                    oldPoints.Add(child);
            }

            if (oldPoints.Count == 0)
            {
                Plugin.Log.LogWarning($"{prefab.name} has no source snap points to normalize.");
                return;
            }

            Vector3 minimum = prefab.transform.InverseTransformPoint(oldPoints[0].position);
            Vector3 maximum = minimum;
            int snapLayer = oldPoints[0].gameObject.layer;
            foreach (Transform point in oldPoints)
            {
                Vector3 local = prefab.transform.InverseTransformPoint(point.position);
                minimum = Vector3.Min(minimum, local);
                maximum = Vector3.Max(maximum, local);
            }

            Vector3 center = (minimum + maximum) * 0.5f;
            Vector3 span = maximum - minimum;
            int firstAxis = LargestAxis(span);
            int secondAxis = SecondLargestAxis(span, firstAxis);
            int thirdAxis = 3 - firstAxis - secondAxis;
            var targets = new List<Vector3>();

            if (profile == SnapProfile.Axial)
            {
                Vector3 low = center;
                Vector3 high = center;
                low[firstAxis] = minimum[firstAxis];
                high[firstAxis] = maximum[firstAxis];
                targets.Add(low);
                targets.Add(high);
            }
            else
            {
                AddCornerTargets(
                    targets, center, minimum, maximum, firstAxis, secondAxis);

                if (profile == SnapProfile.Block)
                {
                    int planarTargetCount = targets.Count;
                    for (int index = 0; index < planarTargetCount; index++)
                    {
                        Vector3 low = targets[index];
                        Vector3 high = targets[index];
                        low[thirdAxis] = minimum[thirdAxis];
                        high[thirdAxis] = maximum[thirdAxis];
                        targets[index] = low;
                        targets.Add(high);
                    }
                }
            }

            var retained = SelectNearestSnapPoints(prefab.transform, oldPoints, targets);
            foreach (Transform point in oldPoints)
            {
                if (!retained.Contains(point))
                    UnityEngine.Object.DestroyImmediate(point.gameObject);
            }

            int retainedIndex = 0;
            foreach (Transform point in retained)
            {
                point.name = "_snappoint_hall_" + retainedIndex++;
                point.gameObject.layer = snapLayer;
            }
        }

        private static void AddCornerTargets(
            List<Vector3> targets,
            Vector3 center,
            Vector3 minimum,
            Vector3 maximum,
            int firstAxis,
            int secondAxis)
        {
            for (int first = 0; first < 2; first++)
            {
                for (int second = 0; second < 2; second++)
                {
                    Vector3 target = center;
                    target[firstAxis] =
                        first == 0 ? minimum[firstAxis] : maximum[firstAxis];
                    target[secondAxis] =
                        second == 0 ? minimum[secondAxis] : maximum[secondAxis];
                    targets.Add(target);
                }
            }
        }

        private static HashSet<Transform> SelectNearestSnapPoints(
            Transform root,
            List<Transform> candidates,
            List<Vector3> targets)
        {
            var selected = new HashSet<Transform>();
            foreach (Vector3 target in targets)
            {
                Transform nearest = null;
                float nearestDistance = float.MaxValue;
                foreach (Transform candidate in candidates)
                {
                    if (selected.Contains(candidate)) continue;
                    Vector3 local = root.InverseTransformPoint(candidate.position);
                    float distance = (local - target).sqrMagnitude;
                    if (distance >= nearestDistance) continue;
                    nearest = candidate;
                    nearestDistance = distance;
                }

                if (nearest != null)
                    selected.Add(nearest);
            }
            return selected;
        }

        private static int LargestAxis(Vector3 value)
        {
            if (value.x >= value.y && value.x >= value.z) return 0;
            return value.y >= value.z ? 1 : 2;
        }

        private static int SecondLargestAxis(Vector3 value, int excluded)
        {
            int first = excluded == 0 ? 1 : 0;
            int second = excluded == 2 ? 1 : 2;
            return value[first] >= value[second] ? first : second;
        }

        private static void RegisterHeroDisplay()
        {
            GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(
                "piece_runic_hero_display", "ArmorStand");
            if (prefab == null)
            {
                Plugin.Log.LogError("Could not register Runic Hero Display: ArmorStand was not found.");
                return;
            }
            prefab.AddComponent<HeroDisplayController>();
            var config = BaseConfig(
                "Runic Hero Display",
                "An illuminated display whose equipment remembers the deeds performed with it.");
            config.Category = HallCategory;
            config.CraftingStation = CraftingStations.Workbench;
            config.AddRequirement("FineWood", 10);
            config.AddRequirement("Bronze", 4);
            config.AddRequirement("SurtlingCore", 2);
            PieceManager.Instance.AddPiece(new CustomPiece(prefab, false, config));
        }

        private static void RegisterHeroPlaque()
        {
            GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(
                "piece_runic_hero_plaque", "sign");
            if (prefab == null)
            {
                Plugin.Log.LogError("Could not register Heroic Chronicle Plaque: sign was not found.");
                return;
            }
            prefab.AddComponent<HeroPlaqueController>();
            var config = BaseConfig(
                "Heroic Chronicle Plaque",
                "Records the earned name and greatest exploits of the nearest hero display.");
            config.Category = HallCategory;
            config.CraftingStation = CraftingStations.Workbench;
            config.AddRequirement("FineWood", 2);
            config.AddRequirement("Bronze", 1);
            PieceManager.Instance.AddPiece(new CustomPiece(prefab, false, config));
        }

        private static void RegisterRunicDisplayLight()
        {
            GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(
                "piece_runic_display_light", "piece_walltorch");
            if (prefab == null)
            {
                Plugin.Log.LogError("Could not register Runic Display Light: piece_walltorch was not found.");
                return;
            }

            foreach (Fireplace fireplace in prefab.GetComponentsInChildren<Fireplace>(true))
                UnityEngine.Object.DestroyImmediate(fireplace);

            prefab.AddComponent<RunicWallLightController>();
            var config = BaseConfig(
                "Runic Display Light",
                "A wall-mounted heroic light that illuminates displays and cycles through six colors.");
            config.Category = HallCategory;
            config.CraftingStation = CraftingStations.Workbench;
            config.AddRequirement("FineWood", 2);
            config.AddRequirement("Bronze", 2);
            config.AddRequirement("SurtlingCore", 1);
            PieceManager.Instance.AddPiece(new CustomPiece(prefab, false, config));
        }

        private static void RegisterEchoRunestone()
        {
            GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(
                "piece_runic_echo_stone", "guard_stone");
            if (prefab == null)
            {
                Plugin.Log.LogError("Could not register Echo Runestone: guard_stone was not found.");
                return;
            }
            foreach (PrivateArea privateArea in prefab.GetComponentsInChildren<PrivateArea>(true))
                UnityEngine.Object.DestroyImmediate(privateArea);
            prefab.AddComponent<EchoRunestoneController>();
            var config = BaseConfig(
                "Echo Runestone",
                "Relives the spectral final moments of the last fallen hero.");
            config.Category = HallCategory;
            config.CraftingStation = CraftingStations.Stonecutter;
            config.AddRequirement("Stone", 12);
            config.AddRequirement("SurtlingCore", 2);
            PieceManager.Instance.AddPiece(new CustomPiece(prefab, false, config));
        }

        private static PieceConfig BaseConfig(string name, string description) =>
            new PieceConfig
            {
                Name = name,
                Description = description,
                PieceTable = PieceTables.Hammer,
                AllowedInDungeons = true
            };

        private static Texture2D LoadTexture(string fileName)
        {
            try
            {
                string path = Path.Combine(
                    Path.GetDirectoryName(Plugin.Instance.Info.Location) ?? "", fileName);
                if (!File.Exists(path))
                {
                    Plugin.Log.LogWarning($"Optional hall texture not found: {path}");
                    return null;
                }
                byte[] bytes = File.ReadAllBytes(path);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                texture.name = "RunicHallFacade";
                texture.wrapMode = TextureWrapMode.Repeat;
                texture.filterMode = FilterMode.Bilinear;
                if (!texture.LoadImage(bytes)) return null;
                return texture;
            }
            catch (Exception error)
            {
                Plugin.Log.LogWarning($"Could not load runic facade texture: {error.Message}");
                return null;
            }
        }

        private static void ApplyMaterial(GameObject prefab, Texture2D texture, Color tint)
        {
            if (texture == null) return;
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.materials;
                for (int i = 0; i < materials.Length; i++)
                {
                    materials[i] = new Material(materials[i]);
                    materials[i].mainTexture = texture;
                    materials[i].color = tint;
                }
                renderer.materials = materials;
            }
        }
    }
}
