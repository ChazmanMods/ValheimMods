import bpy,struct,json
from array import array
from pathlib import Path
from mathutils import Vector
root=Path(r'E:/Valheim Mods/ArcaneDecorNorseFurnishings')
source=Path(r'C:/Users/Charles Sammons/OneDrive/Documents/ChatGPT/Verdant Machina/LocalModelStudio/outputs/20261011-170251-15423c')
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(source/'textured.glb'))
verts=[];indices=[]
for obj in list(bpy.context.scene.objects):
 if obj.type!='MESH':continue
 mesh=obj.data;mesh.calc_loop_triangles();normalmat=obj.matrix_world.to_3x3().inverted().transposed()
 coords=array('f',[0.0])*(len(mesh.vertices)*3);mesh.vertices.foreach_get('co',coords)
 norms=array('f',[0.0])*(len(mesh.corner_normals)*3);mesh.corner_normals.foreach_get('vector',norms)
 uvs=array('f',[0.0])*(len(mesh.loops)*2);mesh.uv_layers.active.data.foreach_get('uv',uvs)
 loopverts=array('i',[0])*len(mesh.loops);mesh.loops.foreach_get('vertex_index',loopverts)
 for tri in mesh.loop_triangles:
  start=len(verts)
  for li in tri.loops:
   vi=loopverts[li];p=obj.matrix_world @ Vector(coords[vi*3:vi*3+3]);n=normalmat @ Vector(norms[li*3:li*3+3]);n.normalize();uv=uvs[li*2:li*2+2]
   verts.append((p.x,p.z,p.y,n.x,n.z,n.y,uv[0],uv[1]))
  indices.extend([start,start+2,start+1])
lo=[min(v[a] for v in verts) for a in range(3)];hi=[max(v[a] for v in verts) for a in range(3)]
center=[(lo[0]+hi[0])/2,lo[1],(lo[2]+hi[2])/2]
with (root/'Assets/bed.meshbin').open('wb') as f:
 f.write(struct.pack('<iii',0x57474D31,len(verts),len(indices)))
 for v in verts:f.write(struct.pack('<8f',*(v[a]-center[a] for a in range(3)),*v[3:]))
 f.write(struct.pack('<%di'%len(indices),*indices))
report={'vertices':len(verts),'triangles':len(indices)//3,'bounds_min':lo,'bounds_max':hi,'unity_size':[hi[a]-lo[a] for a in range(3)],'head_z_mean':sum(v[2] for v in verts if v[1]>lo[1]+(hi[1]-lo[1])*.8)/sum(v[1]>lo[1]+(hi[1]-lo[1])*.8 for v in verts)}
(root/'Assets/model-report.json').write_text(json.dumps(report,indent=2));print(report)
# Render the actual textured export for orientation and visual inspection.
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=16
scene.render.resolution_x=768;scene.render.resolution_y=768;scene.render.resolution_percentage=100;scene.render.film_transparent=True
bounds=[obj.matrix_world@Vector(c) for obj in scene.objects if obj.type=='MESH' for c in obj.bound_box]
mid=sum(bounds,Vector())/len(bounds);extent=max(max(p[a] for p in bounds)-min(p[a] for p in bounds) for a in range(3))
bpy.ops.object.camera_add(location=mid+Vector((1.5,-2.2,1.25))*extent);cam=bpy.context.object;cam.rotation_euler=(mid-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=extent*1.45;scene.camera=cam
for loc,power,size in [((2,-3,4),700,4),((-3,-1,2),400,3),((0,3,4),500,3)]:
 bpy.ops.object.light_add(type='AREA',location=mid+Vector(loc)*extent);light=bpy.context.object;light.data.energy=power*extent*extent;light.data.shape='DISK';light.data.size=size*extent;light.rotation_euler=(mid-light.location).to_track_quat('-Z','Y').to_euler()
scene.world.color=(.3,.3,.3);scene.render.filepath=str(root/'Assets/model-preview.png');bpy.ops.render.render(write_still=True)
