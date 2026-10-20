import bpy
import os
import sys


def arg(name):
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return argv[argv.index("--" + name) + 1]


gender = arg("gender")
output_dir = os.path.abspath(arg("output-dir"))
os.makedirs(output_dir, exist_ok=True)

armature = bpy.data.objects.get("Valheim_Player_Armature")
if armature is None:
    raise RuntimeError("Valheim_Player_Armature was not found")


def export_set(filename, mesh_names):
    meshes = [bpy.data.objects.get(name) for name in mesh_names]
    missing = [name for name, obj in zip(mesh_names, meshes) if obj is None]
    if missing:
        raise RuntimeError("Missing armor meshes: " + ", ".join(missing))

    bpy.ops.object.select_all(action="DESELECT")
    armature.select_set(True)
    for mesh in meshes:
        mesh.select_set(True)
    bpy.context.view_layer.objects.active = armature

    path = os.path.join(output_dir, filename)
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        # Keep every bone Blender's FBX exporter can retain. Unweighted terminal
        # finger bones may still be optimized out of garment-only exports.
        use_armature_deform_only=False,
        bake_anim=False,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        path_mode="COPY",
        embed_textures=False,
    )
    print("AW_ARMOR_EXPORTED", path)


export_set(
    "AW_ChestCowl_" + gender + ".fbx",
    ["AW_ArcaneCloak_" + gender, "AW_ArcaneTunic_" + gender],
)
export_set(
    "AW_LegsBoots_" + gender + ".fbx",
    ["AW_ArcaneLegs_" + gender],
)
