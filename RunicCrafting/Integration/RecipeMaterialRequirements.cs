using System;
using System.Collections.Generic;
using RunicCrafting.Domain;

namespace RunicCrafting.Integration
{
    internal static class RecipeMaterialRequirements
    {
        // null is piece construction: do not apply crafting-station ingredient filtering.
        internal static bool TryBuild(Piece.Requirement[] source, int quality, int multiplier,
            out List<MaterialRequirement> requirements, bool? upgraderStation = null)
        {
            requirements = new List<MaterialRequirement>();
            if (source == null || multiplier <= 0) return false;
            try
            {
                foreach (Piece.Requirement requirement in source)
                {
                    if (requirement?.m_resItem == null) continue;
                    // Match Player.HaveRequirementItems and InventoryGui.SetupRequirementList.
                    // An ordinary workbench and a native upgrader use different ingredient sets.
                    if (upgraderStation.HasValue &&
                        requirement.m_upgraderResource != upgraderStation.Value) continue;
                    int amount = checked(requirement.GetAmount(quality) * multiplier);
                    if (amount <= 0) continue;
                    string resource = ValheimReflection.ResourceId(requirement.m_resItem);
                    if (resource.Length == 0) { requirements.Clear(); return false; }
                    requirements.Add(new MaterialRequirement(resource, amount));
                }
                return requirements.Count > 0;
            }
            catch (OverflowException) { requirements.Clear(); return false; }
        }
    }
}
