import bpy
import os
import sys
from mathutils import Vector


def arg(name):
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return argv[argv.index("--" + name) + 1]


output = os.path.abspath(arg("output"))
frame = int(arg("frame")) if "--frame" in sys.argv else 1
show_body = "--show-body" in sys.argv
scene = bpy.context.scene
scene.frame_set(frame)

for obj in scene.objects:
    if obj.type == "ARMATURE" or obj.name.startswith("GUIDE_"):
        obj.hide_render = True
    if obj.type == "MESH" and "Reference_body" in obj.name:
        obj.hide_render = not show_body

def look_at(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat("-Z", "Y").to_euler()

bpy.ops.object.camera_add(location=(3.15, -5.7, 2.35))
camera = bpy.context.object
camera.data.lens = 68
look_at(camera, (0.0, 0.0, 1.05))
scene.camera = camera

bpy.ops.object.light_add(type="AREA", location=(3.0, -3.5, 4.5))
key = bpy.context.object
key.data.energy = 1250
key.data.shape = "DISK"
key.data.size = 4.0
look_at(key, (0.0, 0.0, 1.0))
bpy.ops.object.light_add(type="AREA", location=(-3.0, 1.5, 2.5))
fill = bpy.context.object
fill.data.energy = 850
fill.data.size = 3.5
look_at(fill, (0.0, 0.0, 1.0))

scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 900
scene.render.resolution_y = 1000
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.filepath = output
scene.world.color = (0.025, 0.03, 0.04)
bpy.ops.render.render(write_still=True)
print("AW_ARMOR_RENDER", output, "frame", frame)
