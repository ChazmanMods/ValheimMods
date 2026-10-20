# ArcaneDecor: NorseFurnishings 0.1.1

Private test build featuring the **Norse King Bed**.

## Install and test

Requires BepInExPack Valheim and Jotunn. Import the ZIP as a local mod in r2modman/Thunderstore Mod Manager, or copy `plugins/ArcaneDecorNorseFurnishings/ArcaneDecorNorseFurnishings.dll` into your profile's `BepInEx/plugins/ArcaneDecorNorseFurnishings/` folder.

Launch modded Valheim. Equip your Hammer and find **Norse King Bed** in **Furniture**. A workbench is required. The recipe is:

- 2 Wolf pelts
- 2 Lox pelts
- 3 Timberwood (vanilla prefab `Frostwood`)
- 2 Red jute
- 1 Amber pearl

The model is sized independently on all three axes to exactly **1.5 times the live Dragon Bed's undamaged visual bounds**. In the installed Valheim 1.0.17 game, the target is approximately 4.25 m wide, 2.62 m tall, and 5.24 m long.

Claiming, sleeping, spawn ownership, shelter/fire requirements, comfort, placement, repair, damage, refunds and saving use native Valheim components. Comfort is inherited from the live Dragon Bed. The visual stays the same at all damage levels. Version 0.1.1 uses smooth surface normals, brighter non-emissive materials, and a 180-degree corrected sleep orientation. Existing beds receive these prefab changes after restarting with the updated DLL. No Runic module is required. Multiplayer participants need this mod installed to see its prefab.

The model and texture are embedded in the DLL. There are no separate model files to install, new gameplay RPCs, update loops or disk reads during normal bed interactions. Imported visuals use one shared mesh/material and two simple box colliders.

## Validation still needed during play

Place the bed with the exact recipe; check the placement preview, texture, collision and floor contact. Claim and sleep in a sheltered room with heat; inspect the sleeping pose. Remove a test bed and confirm exact refunds. Save and reload a world containing a placed and claimed bed. Have a genuinely joined second client place/use it and observe the same state. Measure first-input behavior and frame pacing around multiple beds.

This is a private test artifact, not a validated public release. See VALIDATION.md for actual evidence. Removing the mod makes its placed beds unavailable until it is restored.

## Model source

User-created Local Model Studio output `20261011-170251-15423c`, completed October 11, 2026. Uses `textured.glb` and `textures/texture_00.png`. The rendered import preview is stored in the build workspace under `Assets/model-preview.png`.

