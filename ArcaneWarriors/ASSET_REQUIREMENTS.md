# Arcane Warrior Wearable Asset Contract

## Preferred delivery

Provide the original Blender `.blend` file. Also export an FBX backup and place the textures beside it. A `.blend` file is preferred because it preserves modifiers, material slots, armature relationships, and unapplied transforms for diagnosis.

## Required wearable objects

- `AW_Chest_Tunic`: cream wrap tunic, layered collar, wide sleeves, and waist skirt panels.
- `AW_Chest_OuterRobe`: ankle-length dark-brown open-front robe with wide sleeves.
- `AW_Chest_Belt`: brown belt and buckle.
- `AW_Legs_Pants`: cream trousers.
- `AW_Legs_Boots`: knee-high fitted brown leather boots.
- `AW_Hood`: optional dark-brown hood. Keep it separate from the chest mesh.

Objects may use multiple material slots, but do not join chest, legs, and hood into one wearable.

## Rigging

- Use the Valheim player armature as the only deforming armature.
- Preserve every Valheim bone name exactly. Do not rename, delete, re-parent, or add a second root bone.
- Add an Armature modifier to every wearable mesh and weight it to the Valheim skeleton.
- Test the shoulders, elbows, wrists, hips, knees, and ankles in several poses.
- Weight the robe hem mainly to `Hips`, with modest leg influence near the front opening. Avoid rigidly weighting the entire coat to one spine bone.
- No cloth simulation or collision objects are required. The game uses skinned meshes for these wearables.

## Modeling

- Model at the exact scale and position of the imported Valheim player body.
- Leave enough clearance to prevent the player body and inner tunic from showing through during running, crouching, and attacks.
- Keep the robe front open and the boots visibly separate from the trousers.
- Apply mesh Rotation and Scale before delivery: rotation `0, 0, 0`, scale `1, 1, 1`.
- Keep the armature transforms unchanged.
- Use UV maps named `UVMap`.
- A practical budget is 20,000-40,000 triangles for the complete equipped set.

## Textures

PNG or TGA, preferably 2048x2048:

- `AW_Cloth_Cream_d.png`
- `AW_Cloth_Brown_d.png`
- `AW_Leather_Brown_d.png`
- Matching `_n` normal maps when available.
- Matching `_m` material/mask maps when available.

Keep source PSD/Krita files if the layers matter. Do not use a costume photograph itself as a UV texture; paint clean tileable fabric and leather surfaces onto the actual UV layout.

## FBX backup settings

- Selected Objects: enabled
- Object Types: Mesh and Armature only
- Apply Transform: enabled
- Forward: `-Z Forward`
- Up: `Y Up`
- Apply Modifiers: enabled
- Add Leaf Bones: disabled
- Only Deform Bones: enabled
- Bake Animation: disabled
- Path Mode: Copy, with textures also supplied as separate files

The FBX should contain one Valheim armature and the named wearable meshes. Do not export cameras, lights, empties, collision meshes, or the reference player body.
