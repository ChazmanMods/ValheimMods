# Companion Forge

A local Windows wizard that guides a rigged character, textures and animations into a separate Valheim companion mod.

Double-click **Launch Companion Forge.cmd**. Python 3.11 or newer with Tkinter is required; there are no Python package dependencies. Install the .NET SDK for plugin compilation. Choose the Unity Editor version matching your installed Valheim; the tool detects the version from the game files.

Click **Load practice character** to try the workflow with an included original block mannequin, textures and simple motion clips. Save its project, inspect it and build its install ZIP before selecting your own character assets. The mannequin is a workflow example, not a high-resolution character.

## Workflow

1. **Gather:** follow the asset checklist and export guidance. Gather an adult humanoid model, idle/walk/run clips, textures and asset credits.
2. **Character:** select a rigged FBX, name the companion, set its stable ID, adjust scale/facing/vertical offset and record licenses. Keep the stable ID when updating an existing world.
3. **Animations:** assign idle, walk and run FBX or Humanoid `.anim` clips. Attack and wave clips are optional. Enter exact clip names when a file includes several clips. An import attempt reports all discovered choices.
4. **Materials:** inspect once to discover material names. Assign base color, tangent-space normal, metallic/gloss and emission textures per material. Use Blender or an image editor to convert roughness to smoothness before providing a metallic/gloss map. Texture limits cap imported dimensions; they do not manufacture missing source detail.
5. **Build:** choose your game, matching Unity executable and a BepInEx folder containing Jötunn. Inspect assets, review the generated Unity Preview scene, then build the install ZIP.

Builds use fresh isolated Unity projects beside your saved project. Inputs and the shared ValheimUnityProject are preserved. Each run retains its report, Unity log and compiler log. The UI stays responsive during imports and builds; Cancel stops the active process. A previous successful ZIP is replaced only after the new package is built and checked.

For an unusual skeleton, correct the bone names and T-pose in Blender, or map the imported FBX in Unity’s Rig > Configure view. Save the corrected `.fbx.meta` beside the original source FBX; the next import preserves its bone mapping. Imported animation files can likewise carry their `.meta` settings. Changes to assets, metadata or the Unity builder invalidate the prior bundle automatically.

## Installing your generated companion

Install BepInEx and Jötunn in Valheim. Import the generated install ZIP into your mod manager as a local mod, or extract the ZIP’s BepInEx folder into the Valheim installation. Install the same packs on all players and the server.

Press **F8** to summon the companion. The generated BepInEx configuration selects a companion by its stable ID and changes the shortcut. Interact for Valheim’s tame-creature follow/stay commands. **Left Shift+interact** shows a greeting and triggers a supplied wave animation; **Left Alt+interact** shows non-explicit affection text.

The runtime uses a clone of Valheim’s Dverger creature for native movement and combat. Your visual model has a Humanoid animator driven by movement speed and native attack state. Native colliders and attack reach are retained. The runtime is shared across generated character packs and discovers their settings once at startup. It requires neither Wife Companion nor another Runic module.

## Current scope

This version focuses on high-resolution models, material assignments, scale/tint customization and the asset-to-mod pipeline. Dialogue consists of configurable greeting and affection lines. Household task automation, branching dialogue, persistent relationship progression, character-specific gear attachments and custom attack damage/event timing are not implemented. The optional sit clip is included in the preview controller; the runtime does not yet expose a sit command. Custom model alignment, actual follow/stay behavior, combat presentation and multiplayer persistence need live Valheim testing with the chosen assets. Build and import reports are kept outside generated mod packages.

No purchased or downloaded character assets are included. Include source, author, license, attribution and redistribution permission for each asset. Generated packages include your credits and the tool/runtime license. They are local install ZIPs; Thunderstore submission metadata and icons are not generated.

## Command line

```powershell
python forge.py path\to\companion.json --inspect
python forge.py path\to\companion.json
```

## Reference documentation

- [Unity Humanoid avatar configuration](https://docs.unity3d.com/6000.0/Documentation/Manual/ConfiguringtheAvatar.html)
- [Blender FBX export](https://docs.blender.org/manual/en/latest/addons/import_export/scene_fbx.html)
- [Unity Standard material parameters](https://docs.unity3d.com/6000.0/Documentation/Manual/StandardShaderMaterialParameters.html)
- [Jötunn custom creatures](https://valheim-modding.github.io/Jotunn/tutorials/creatures.html)
