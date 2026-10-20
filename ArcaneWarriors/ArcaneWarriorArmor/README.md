# Arcane Warrior Armor asset preparation

This folder contains the first animation-ready pass of the purchased cloak,
kimono robe, and pants/boots assets for ArcaneWarriors.

## Prepared equipment

- `AW_ChestCowl`: aged-cream robe with a worn dark-mahogany hood/cowl.
- `AW_LegsBoots`: charcoal trousers with weathered dark leather boots.
- Separate male and female Valheim fits are provided as `.blend` workspaces and
  Unity-importable `.fbx` files.
- Editable garment meshes carry all 39 native Valheim bone groups. Blender's
  garment-only FBX export retains the 26-bone hierarchy actually used by the
  armor and omits unweighted terminal finger bones.
- Base-color textures retain the original UV layouts and have been visually
  weathered to better suit Valheim.

## Deliberate changes

The source cloak was an unrigged, arms-down full-body mesh. Its wide outer
sleeves and long lower panels could not follow Valheim combat animations without
severe intersections and tearing. It has therefore been converted into a hood
and shoulder cowl. The kimono supplies the animated sleeves and robe skirt.

A future long rear cloak should be implemented as a dedicated cape mesh using
Valheim's cape/cloth treatment, rather than leg-weighting the original panels.

## Unity integration notes

1. Import the appropriate gender FBXs and all three PNG textures.
2. Rebuild the materials with a Valheim-compatible shader; these files currently
   contain only authored base color and roughness intent.
3. Register the chest/cowl and legs/boots as separate equipment items.
4. Hide the player body regions covered by the chest, arms, hips, legs, and feet
   to prevent the base body from showing through extreme animations.
5. Validate in Unity and in game with idle, run, dodge, attack, block, swim, sit,
   jump, and ArcaneWarriors spell animations before release.

The `.blend` files remain the editable masters. The FBXs are handoff exports,
not the final ArcaneWarriors prefab or AssetBundle.
