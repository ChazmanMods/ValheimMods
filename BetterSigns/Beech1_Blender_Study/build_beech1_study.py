import bpy, os, math
from mathutils import Vector

base = r"E:\Valheim Mods\Beech1_Blender_Study"
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.wm.obj_import(filepath=os.path.join(base, 'Beech1.obj'))

for obj in bpy.context.selected_objects:
    obj.name = obj.name.replace('.', '_')
    if 'Lod1' in obj.name or 'Lod3' in obj.name:
        obj.hide_viewport = True
        obj.hide_render = True
    if obj.type == 'MESH':
        obj['study_note'] = 'Static Valheim mesh. Runtime wind comes from the Unity vegetation shader.'

for mat in bpy.data.materials:
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    if not bsdf: continue
    tex = next((n for n in mat.node_tree.nodes if n.type == 'TEX_IMAGE'), None)
    if tex and 'leaf' in mat.name.lower():
        mat.surface_render_method = 'DITHERED'
        mat.use_transparency_overlap = False
        mat.node_tree.links.new(tex.outputs.get('Alpha'), bsdf.inputs.get('Alpha'))
        bsdf.inputs['Roughness'].default_value = 0.8
    elif 'bark' in mat.name.lower():
        bsdf.inputs['Roughness'].default_value = 0.9

# Add an explicitly labeled controller and a non-destructive wind-study animation.
bpy.ops.object.empty_add(type='ARROWS', location=(0,0,0))
ctrl = bpy.context.object
ctrl.name = 'WIND_PREVIEW_CONTROLLER'
ctrl['Important'] = 'Approximate Blender study animation; Valheim wind is shader-driven, not a skeletal clip.'

for obj in [o for o in bpy.context.scene.objects if o.type == 'MESH' and not o.hide_render]:
    if not any('leaf' in s.material.name.lower() for s in obj.material_slots if s.material):
        continue
    basis = obj.shape_key_add(name='Basis')
    wind = obj.shape_key_add(name='Wind Preview')
    for src, dst in zip(basis.data, wind.data):
        height = max(0.0, src.co.z)
        dst.co.x += 0.10 * height * height
        dst.co.y += 0.025 * height * math.sin(src.co.x * 3.0)
    wind.value = 0.0; wind.keyframe_insert('value', frame=1)
    wind.value = 1.0; wind.keyframe_insert('value', frame=30)
    wind.value = -0.65; wind.keyframe_insert('value', frame=60)
    wind.value = 0.75; wind.keyframe_insert('value', frame=90)
    wind.value = 0.0; wind.keyframe_insert('value', frame=120)

bpy.context.scene.frame_start = 1
bpy.context.scene.frame_end = 120
bpy.context.scene.render.engine = 'BLENDER_EEVEE'
bpy.context.scene.world.color = (0.04, 0.04, 0.04)

# Ground, light, and camera make the file immediately readable.
bpy.ops.mesh.primitive_plane_add(size=20, location=(0,0,0))
ground=bpy.context.object; ground.name='STUDY_GROUND'
bpy.ops.object.light_add(type='SUN', location=(4,-4,8))
bpy.context.object.rotation_euler=(math.radians(25),0,math.radians(-30)); bpy.context.object.data.energy=2.0
bpy.ops.object.camera_add(location=(8,-10,6))
cam=bpy.context.object; bpy.context.scene.camera=cam
def point_at(o, p): o.rotation_euler=(Vector(p)-o.location).to_track_quat('-Z','Y').to_euler()
point_at(cam,(0,0,3))

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(base, 'Beech1_Study.blend'))
