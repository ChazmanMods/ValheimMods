"""Run with Blender --background --python make_fixture.py -- OUTPUT_DIR.

Creates an original block mannequin and animation FBXs for pipeline checks.
This is a test asset, not a high-resolution character.
"""
import math
from pathlib import Path
import sys
import bpy
from mathutils import Vector

out = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
out.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
armature = bpy.data.armatures.new("Humanoid")
rig = bpy.data.objects.new("Humanoid", armature)
bpy.context.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
# Blender Z-up; FBX exporter converts to Unity Y-up.
bones = [
    ("Hips", (0, 0, .90), (0, 0, 1.02), None),
    ("Spine", (0, 0, 1.02), (0, 0, 1.20), "Hips"),
    ("Chest", (0, 0, 1.20), (0, 0, 1.43), "Spine"),
    ("Neck", (0, 0, 1.43), (0, 0, 1.55), "Chest"),
    ("Head", (0, 0, 1.55), (0, 0, 1.78), "Neck"),
]
for side, direction in (("Left", 1), ("Right", -1)):
    x = .12 * direction
    bones += [
        (side + "UpperLeg", (x, 0, .90), (x, -.025, .49), "Hips"),
        (side + "LowerLeg", (x, -.025, .49), (x, 0, .09), side + "UpperLeg"),
        (side + "Foot", (x, 0, .09), (x, -.18, .05), side + "LowerLeg"),
        (side + "Shoulder", (.04 * direction, 0, 1.40), (.20 * direction, 0, 1.40), "Chest"),
        (side + "UpperArm", (.20 * direction, 0, 1.40), (.48 * direction, 0, 1.40), side + "Shoulder"),
        (side + "LowerArm", (.48 * direction, 0, 1.40), (.72 * direction, 0, 1.40), side + "UpperArm"),
        (side + "Hand", (.72 * direction, 0, 1.40), (.84 * direction, 0, 1.40), side + "LowerArm"),
    ]
for name, start, end, parent in bones:
    bone = armature.edit_bones.new(name)
    bone.head, bone.tail = start, end
    if parent:
        bone.parent = armature.edit_bones[parent]
bpy.ops.object.mode_set(mode="OBJECT")

image = bpy.data.images.new("MannequinBase", width=16, height=16)
image.pixels = [.25, .6, .8, 1.0] * 256
image.filepath_raw = str(out / "mannequin.png")
image.file_format = "PNG"
image.save()
material = bpy.data.materials.new("MannequinBody")
material.use_nodes = True
tex = material.node_tree.nodes.new("ShaderNodeTexImage")
tex.image = image
material.node_tree.links.new(tex.outputs["Color"], material.node_tree.nodes.get("Principled BSDF").inputs["Base Color"])
parts = []
for name, start, end, parent in bones:
    if "Shoulder" in name or name == "Neck":
        continue
    center = (Vector(start) + Vector(end)) / 2
    length = (Vector(end) - Vector(start)).length
    width = .11
    if name in ("Hips", "Spine", "Chest"):
        width = .31
    if name == "Head":
        width = .19
    bpy.ops.mesh.primitive_cube_add(size=1, location=center)
    obj = bpy.context.object
    obj.name = name + "Mesh"
    obj.scale = (width, width * .65, length * .9)
    obj.rotation_euler = (Vector(end) - Vector(start)).to_track_quat("Z", "Y").to_euler()
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    group = obj.vertex_groups.new(name=name)
    group.add(list(range(len(obj.data.vertices))), 1.0, "REPLACE")
    obj.data.materials.append(material)
    parts.append(obj)
bpy.ops.object.select_all(action="DESELECT")
for part in parts:
    part.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
mesh = bpy.context.object
mesh.name = "MannequinMesh"
mod = mesh.modifiers.new("HumanoidSkin", "ARMATURE")
mod.object = rig
mesh.parent = rig

def export(path, animated):
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, add_leaf_bones=False,
        bake_anim=animated, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
        bake_anim_simplify_factor=0, axis_forward="-Z", axis_up="Y", path_mode="RELATIVE")

export(out / "mannequin.fbx", False)
for role, amount in (("idle", .015), ("walk", .3), ("run", .55), ("attack", .7), ("wave", .8)):
    rig.animation_data_clear()
    for bone in rig.pose.bones:
        bone.rotation_mode = "XYZ"
        bone.rotation_euler = (0, 0, 0)
    bpy.context.scene.frame_start, bpy.context.scene.frame_end = 1, 31
    for frame, sign in ((1, 0), (8, 1), (16, 0), (24, -1), (31, 0)):
        for name in ("LeftUpperLeg", "RightUpperLeg", "LeftUpperArm", "RightUpperArm"):
            bone = rig.pose.bones[name]
            bone.rotation_euler.x = sign * amount * (-1 if name.startswith("Right") else 1)
            bone.keyframe_insert("rotation_euler", frame=frame)
        rig.pose.bones["Spine"].rotation_euler.y = sign * amount * .12
        rig.pose.bones["Spine"].keyframe_insert("rotation_euler", frame=frame)
    rig.animation_data.action.name = role.title()
    export(out / (role + ".fbx"), True)
rig.animation_data_clear()
bpy.ops.object.select_all(action="DESELECT")
bpy.ops.mesh.primitive_cube_add(size=1)
cube = bpy.context.object
bpy.ops.export_scene.fbx(filepath=str(out / "unrigged.fbx"), use_selection=True, bake_anim=False)
print("Original pipeline fixtures exported to", out)
