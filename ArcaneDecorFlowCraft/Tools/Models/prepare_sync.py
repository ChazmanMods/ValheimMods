import bpy
from mathutils import Vector
from pathlib import Path
exec(Path(__file__).with_name('inspect_sync.py').read_text())
tex=bpy.data.images.load(str(Path(__file__).parent/'SyncFountain/FC_Sync_Fountain_basecolor.jpg'))
for o in list(bpy.context.scene.objects):
 if o.type=='MESH':
  for mat in o.data.materials:
   mat.use_nodes=True
   bs=mat.node_tree.nodes.get('Principled BSDF');t=mat.node_tree.nodes.new('ShaderNodeTexImage');t.image=tex;mat.node_tree.links.new(t.outputs['Color'],bs.inputs['Base Color']);bs.inputs['Metallic'].default_value=.65;bs.inputs['Roughness'].default_value=.38
center=Vector((0,0,.22));bpy.ops.object.camera_add(location=(1.5,-2,1.4));cam=bpy.context.object;cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=1.3
for pos,p in [((2,-2,3),400),((-2,-1,2),200),((1,2,3),300)]:
 bpy.ops.object.light_add(type='AREA',location=pos);o=bpy.context.object;o.data.energy=p;o.data.size=3;o.rotation_euler=(center-o.location).to_track_quat('-Z','Y').to_euler()
s=bpy.context.scene;s.camera=cam;s.render.engine='CYCLES';s.cycles.samples=16;s.render.resolution_x=s.render.resolution_y=512;s.render.resolution_percentage=100;s.render.film_transparent=True;s.world.color=(.2,.2,.2);s.render.image_settings.file_format='PNG';s.render.filepath=str(Path(__file__).parents[2]/'Assets/Plumbing/sync_dish.png');bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=str(Path(__file__).parent/'SyncFountain/prepared.blend'))
