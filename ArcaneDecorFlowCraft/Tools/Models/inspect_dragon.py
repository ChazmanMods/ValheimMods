import bpy,json
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parent
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(root/'AD_Dragon_Spout_001/AD_Dragon_Spout_001.fbx'))
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
pts=[o.matrix_world@v.co for o in meshes for v in o.data.vertices]
lo=Vector(tuple(min(p[i] for p in pts) for i in range(3)));hi=Vector(tuple(max(p[i] for p in pts) for i in range(3)));center=(lo+hi)*.5
print('DRAGON',json.dumps({'bounds':[list(lo),list(hi)],'meshes':[(o.name,len(o.data.vertices)) for o in meshes]}))
for i,(axis,pos) in enumerate([('front',(0,-3,0)),('side',(3,0,0)),('top',(0,0,3))]):
 bpy.ops.object.camera_add(location=center+Vector(pos));camera=bpy.context.object;camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=max(hi-lo)*1.25;bpy.context.scene.camera=camera
 scene=bpy.context.scene;scene.render.engine='BLENDER_WORKBENCH';scene.display.shading.light='STUDIO';scene.display.shading.color_type='TEXTURE';scene.render.resolution_x=768;scene.render.resolution_y=768;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.filepath=str(root/('dragon-'+axis+'.png'));bpy.ops.render.render(write_still=True)
