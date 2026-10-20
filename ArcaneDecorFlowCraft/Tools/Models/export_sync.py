import bpy,struct,shutil,math
from pathlib import Path
from mathutils import Vector
root=Path(__file__).parent;out=root.parents[1]/'Assets/Plumbing'
bpy.ops.wm.open_mainfile(filepath=str(root/'SyncFountain/prepared.blend'))
pivot=Vector((0,0,.34));axis=Vector((1,0,1)).normalized();groups={'sync_dish':[],'sync_nozzle':[]}
for obj in bpy.context.scene.objects:
 if obj.type!='MESH':continue
 m=obj.data;m.calc_loop_triangles();nm=obj.matrix_world.to_3x3().inverted().transposed()
 for tri in m.loop_triangles:
  points=[obj.matrix_world@m.vertices[m.loops[l].vertex_index].co for l in tri.loops]
  # Swivel assembly above the pedestal; the dish and bearing remain stationary.
  key='sync_nozzle' if sum(p.z for p in points)/3>.255 else 'sync_dish'
  for l,p in zip(tri.loops,points):
   n=(nm@m.corner_normals[l].vector).normalized();uv=m.uv_layers.active.data[l].uv
   if key=='sync_nozzle':p-=pivot
   groups[key].append((-p.y,p.z,p.x,-n.y,n.z,n.x,uv.x,uv.y))
for key,v in groups.items():
 with (out/(key+'.meshbin')).open('wb') as f:
  f.write(struct.pack('<iii',0x57474D31,len(v),len(v)))
  for p in v:f.write(struct.pack('<8f',*p))
  ind=[i for t in range(0,len(v),3) for i in (t,t+2,t+1)];f.write(struct.pack('<'+'i'*len(ind),*ind))
 shutil.copyfile(root/'SyncFountain/FC_Sync_Fountain_basecolor.jpg',out/(key+'.jpg'))
 if key!='sync_dish':shutil.copyfile(out/'sync_dish.png',out/(key+'.png'))
 print(key,len(v))
