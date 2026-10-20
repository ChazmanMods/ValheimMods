import bpy,struct,shutil
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parent;output=root.parents[1]/'Assets/Plumbing'
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(root/'AD_Dragon_Spout_001/AD_Dragon_Spout_001.fbx'))
vertices=[];origin=Vector((-.311767578125,0,.395));scale=2
for obj in list(bpy.context.scene.objects):
 if obj.type!='MESH':continue
 mesh=obj.data;mesh.calc_loop_triangles();nm=obj.matrix_world.to_3x3().inverted().transposed()
 for tri in mesh.loop_triangles:
  for loop in tri.loops:
   point=(obj.matrix_world@mesh.vertices[mesh.loops[loop].vertex_index].co-origin)*scale;n=(nm@mesh.corner_normals[loop].vector).normalized();uv=mesh.uv_layers.active.data[loop].uv
   vertices.append((-point.y,point.z,point.x,-n.y,n.z,n.x,uv.x,uv.y))
with (output/'dragon.meshbin').open('wb') as s:
 s.write(struct.pack('<iii',0x57474D31,len(vertices),len(vertices)))
 for v in vertices:s.write(struct.pack('<8f',*v))
 indices=[i for t in range(0,len(vertices),3) for i in (t,t+2,t+1)];s.write(struct.pack('<'+'i'*len(indices),*indices))
shutil.copy2(root/'AD_Dragon_Spout_001/AD_Dragon_Spout_001.fbm/AD_Dragon_Spout_001_basecolor.jpg',output/'dragon.jpg')
# Render the supplied model itself for the hammer icon.
center=Vector((0,0,.5));bpy.ops.object.camera_add(location=center+Vector((2.7,-1.6,1.0)));cam=bpy.context.object;cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=1.3
for pos,power in [((3,-2,3),380),((-1,-2,1),200),((1,2,2),280)]:
 bpy.ops.object.light_add(type='AREA',location=Vector(pos));o=bpy.context.object;o.data.energy=power;o.data.shape='DISK';o.data.size=3;o.rotation_euler=(center-o.location).to_track_quat('-Z','Y').to_euler()
scene=bpy.context.scene;scene.camera=cam;scene.render.engine='CYCLES';scene.cycles.samples=32;scene.render.resolution_x=512;scene.render.resolution_y=512;scene.render.resolution_percentage=100;scene.render.film_transparent=True;scene.world.color=(.2,.2,.2);scene.render.image_settings.file_format='PNG';scene.render.filepath=str(output/'dragon.png');bpy.ops.render.render(write_still=True)
