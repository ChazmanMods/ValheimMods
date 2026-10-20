import bpy
import bmesh
import os
import sys
from mathutils.kdtree import KDTree


ROOT = r"E:\Valheim Mods\ArcaneWarriors"
SOURCE_ROOT = os.path.join(ROOT, "ArmorCandidateReview")
OUTPUT_ROOT = os.path.join(ROOT, "ArcaneWarriorArmor")
TEXTURE_ROOT = os.path.join(OUTPUT_ROOT, "Textures")


def arg(name):
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return argv[argv.index("--" + name) + 1]


gender = arg("gender")
output_blend = os.path.abspath(arg("output"))
gender_width = 0.93 if gender == "Female" else 1.0
gender_depth = 0.95 if gender == "Female" else 1.0


armature = bpy.data.objects.get("Valheim_Player_Armature")
body = next((obj for obj in bpy.context.scene.objects
             if obj.type == "MESH" and "Reference_body" in obj.name), None)
if not armature or not body:
    raise RuntimeError("Open this builder with a Valheim player-kit .blend file")

armor_root = bpy.data.collections.new("ARCANE_WARRIOR_ARMOR")
bpy.context.scene.collection.children.link(armor_root)
chest_collection = bpy.data.collections.new("AW_CHEST_COWL")
legs_collection = bpy.data.collections.new("AW_LEGS_BOOTS")
armor_root.children.link(chest_collection)
armor_root.children.link(legs_collection)


def import_mesh(filepath, name, collection):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=filepath, use_custom_normals=True, ignore_leaf_bones=True)
    imported = [obj for obj in set(bpy.data.objects) - before if obj.type == "MESH"]
    if len(imported) != 1:
        raise RuntimeError("Expected one mesh in " + filepath)
    obj = imported[0]
    obj.name = name
    obj.data.name = name + "_Mesh"
    for old_collection in list(obj.users_collection):
        old_collection.objects.unlink(obj)
    collection.objects.link(obj)
    return obj


def material(name, texture_path, roughness=0.9):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    principled = nodes.get("Principled BSDF")
    principled.inputs["Roughness"].default_value = roughness
    principled.inputs["Metallic"].default_value = 0.0
    if "Specular IOR Level" in principled.inputs:
        principled.inputs["Specular IOR Level"].default_value = 0.18
    image = bpy.data.images.load(texture_path, check_existing=True)
    texture = nodes.new("ShaderNodeTexImage")
    texture.image = image
    texture.interpolation = "Linear"
    mat.node_tree.links.new(texture.outputs["Color"], principled.inputs["Base Color"])
    return mat


def fit(obj, uniform_scale, z_offset):
    obj.scale = (uniform_scale * gender_width,
                 uniform_scale * gender_depth,
                 uniform_scale)
    obj.location.z += z_offset
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    obj.select_set(False)


def trim_cloak_for_animation(obj):
    """Turn the generated full cloak into an animation-safe outer mantle."""
    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    remove = []
    for face in bm.faces:
        center = face.calc_center_median()
        is_outer_sleeve = abs(center.x) > 0.34 and 0.68 < center.z < 1.64
        is_face_mask = center.z > 1.69 and center.y < -0.075 and abs(center.x) < 0.25
        is_unriggable_long_panel = center.z < 1.45
        if is_outer_sleeve or is_face_mask or is_unriggable_long_panel:
            remove.append(face)
    bmesh.ops.delete(bm, geom=remove, context="FACES")
    loose = [vert for vert in bm.verts if not vert.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()


def widen_lower_armor(obj):
    for vertex in obj.data.vertices:
        vertex.co.x *= 1.09
        vertex.co.y *= 1.05
    obj.data.update()


def transfer_weights(obj):
    # These source garments were authored in an arms-down pose close to
    # Valheim's hidden native A-pose. Bone-heat weighting therefore gives much
    # cleaner sleeve and robe assignments than closest-body surface transfer.
    for group in list(obj.vertex_groups):
        obj.vertex_groups.remove(group)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.parent_set(type="ARMATURE_AUTO", keep_transform=True)
    bpy.ops.object.select_all(action="DESELECT")

    weighted_vertices = set()
    for vertex in obj.data.vertices:
        if any(group.weight > 0.0001 for group in vertex.groups):
            weighted_vertices.add(vertex.index)
    if not weighted_vertices:
        raise RuntimeError(obj.name + " received no automatic weights")
    if len(weighted_vertices) != len(obj.data.vertices):
        tree = KDTree(len(weighted_vertices))
        for index in weighted_vertices:
            tree.insert(obj.data.vertices[index].co, index)
        tree.balance()
        for vertex in obj.data.vertices:
            if vertex.index in weighted_vertices:
                continue
            _co, nearest_index, _distance = tree.find(vertex.co)
            nearest = obj.data.vertices[nearest_index]
            for assignment in nearest.groups:
                obj.vertex_groups[assignment.group].add(
                    [vertex.index], assignment.weight, "REPLACE")

    armature_modifier = next((m for m in obj.modifiers if m.type == "ARMATURE"), None)
    if armature_modifier is None:
        raise RuntimeError(obj.name + " did not receive an armature modifier")
    armature_modifier.name = "Valheim_Player_Rig"
    armature_modifier.use_deform_preserve_volume = True


cloak = import_mesh(
    os.path.join(SOURCE_ROOT, "hooded cloak 3d model", "hooded+cloak+3d+model.fbx"),
    "AW_ArcaneCloak_" + gender, chest_collection)
tunic = import_mesh(
    os.path.join(SOURCE_ROOT, "kimono robe 3d model", "kimono+robe+3d+model.fbx"),
    "AW_ArcaneTunic_" + gender, chest_collection)
legs = import_mesh(
    os.path.join(SOURCE_ROOT, "pants with boots 3d model", "pants+with+boots+3d+model.fbx"),
    "AW_ArcaneLegs_" + gender, legs_collection)

fit(cloak, 1.90, 0.04)
fit(tunic, 1.35, 0.53)
fit(legs, 1.16, 0.0)
trim_cloak_for_animation(cloak)
widen_lower_armor(legs)

cloak.data.materials.clear()
cloak.data.materials.append(material("AW_Cloak_WornMahogany", os.path.join(TEXTURE_ROOT, "AW_Cloak_BaseColor.png"), 0.95))
tunic.data.materials.clear()
tunic.data.materials.append(material("AW_Tunic_AgedCream", os.path.join(TEXTURE_ROOT, "AW_Tunic_BaseColor.png"), 0.92))
legs.data.materials.clear()
legs.data.materials.append(material("AW_Legs_CharcoalLeather", os.path.join(TEXTURE_ROOT, "AW_Legs_BaseColor.png"), 0.88))

for garment in (cloak, tunic, legs):
    transfer_weights(garment)
    garment["ARCANE_WARRIOR_ARMOR"] = True
    garment["GENDER_FIT"] = gender

body.hide_render = True
body.hide_viewport = False
armature.show_in_front = True

instructions = bpy.data.texts.get("README_ARCANE_WARRIOR_ARMOR") or bpy.data.texts.new("README_ARCANE_WARRIOR_ARMOR")
instructions.clear()
instructions.write(
    "Arcane Warrior Armor fitting workspace - " + gender + "\n"
    "Meshes use native Valheim bone names and transferred body weights.\n"
    "AW_ArcaneCloak and AW_ArcaneTunic form the chest/cowl equipment.\n"
    "AW_ArcaneLegs forms the legs/boots equipment.\n"
    "Validate shoulders, elbows, hips, knees, hood and robe hem before Unity export.\n"
)

os.makedirs(os.path.dirname(output_blend), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=output_blend)
print("AW_ARMOR_BUILT", gender, output_blend,
      [(obj.name, len(obj.data.vertices), len(obj.vertex_groups)) for obj in (cloak, tunic, legs)])
