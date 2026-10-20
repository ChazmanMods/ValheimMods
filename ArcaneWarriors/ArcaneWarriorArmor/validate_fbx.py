import bpy
import os
import sys


filepath = os.path.abspath(sys.argv[sys.argv.index("--") + 1])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=filepath, ignore_leaf_bones=True)

armatures = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
if len(armatures) != 1:
    raise RuntimeError(f"{filepath}: expected 1 armature, found {len(armatures)}")
if not meshes:
    raise RuntimeError(f"{filepath}: no meshes imported")

bone_count = len(armatures[0].data.bones)
# The editable masters contain the full 39-bone rig and 39 matching vertex
# groups. Blender omits 13 unweighted terminal finger bones from garment-only
# FBXs; the 26 retained bones are the complete hierarchy used by this armor.
if bone_count != 26:
    raise RuntimeError(f"{filepath}: expected 26 exported garment bones, found {bone_count}")

for mesh in meshes:
    if not any(mod.type == "ARMATURE" for mod in mesh.modifiers):
        raise RuntimeError(f"{filepath}: {mesh.name} has no armature modifier")
    if not mesh.vertex_groups:
        raise RuntimeError(f"{filepath}: {mesh.name} has no skin weights")

print("AW_FBX_VALID", os.path.basename(filepath), bone_count,
      [(mesh.name, len(mesh.data.vertices), len(mesh.vertex_groups)) for mesh in meshes])
