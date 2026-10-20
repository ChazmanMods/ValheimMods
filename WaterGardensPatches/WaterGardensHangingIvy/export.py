import bpy,struct,shutil,math,json
from pathlib import Path
from mathutils import Vector
root=Path(r"E:/Valheim Mods/WaterGardensHangingIvy");out=root/"Assets";out.mkdir(exist_ok=True)
bpy.ops.object.select_all(action="SELECT");bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(next((root/"SourceAsset").glob("*.fbx"))))
verts=[];objs=[o for o in bpy.context.scene.objects if o.type=="MESH"]
for obj in objs:
 mesh=obj.data;mesh.calc_loop_triangles();nm=obj.matrix_world.to_3x3().inverted().transposed()
 for tri in mesh.loop_triangles:
  for loop in tri.loops:
   v=obj.matrix_world@mesh.vertices[mesh.loops[loop].vertex_index].co;n=(nm@mesh.corner_normals[loop].vector).normalized();uv=mesh.uv_layers.active.data[loop].uv
   verts.append((v.x,v.z-.99951171875,-v.y,n.x,n.z,-n.y,uv.x,uv.y))
with (out/'hanging_ivy.meshbin').open('wb') as f:
 f.write(struct.pack('<iii',0x57474D31,len(verts),len(verts)))
 for v in verts:f.write(struct.pack('<8f',*v))
 f.write(struct.pack('<'+'i'*len(verts),*range(len(verts))))
shutil.copy2(next((root/'SourceAsset').glob('**/*.jpg')),out/'hanging_ivy.jpg')
img=bpy.data.images.load(str(out/'hanging_ivy.jpg'))
for obj in objs:
 mat=bpy.data.materials.new('ivy');mat.use_nodes=True;bs=mat.node_tree.nodes.get('Principled BSDF');tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=img;mat.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color']);bs.inputs['Roughness'].default_value=.75;obj.data.materials.clear();obj.data.materials.append(mat)
scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=512;scene.render.resolution_y=512;scene.render.resolution_percentage=100;scene.render.film_transparent=True
bpy.ops.object.camera_add(location=(.9,-2, .7));cam=bpy.context.object;cam.rotation_euler=(Vector((0,0,.5))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=1.15;scene.camera=cam
for loc,power in [((1,-2,3),300),((-2,-1,1),180)]:
 bpy.ops.object.light_add(type='AREA',location=loc);bpy.context.object.data.energy=power;bpy.context.object.data.shape='DISK';bpy.context.object.data.size=3
scene.world.color=(.35,.35,.35);scene.view_settings.view_transform='Standard';scene.render.filepath=str(out/'hanging_ivy.png');bpy.ops.render.render(write_still=True)
print('EXPORTED',len(verts)//3,'triangles')
