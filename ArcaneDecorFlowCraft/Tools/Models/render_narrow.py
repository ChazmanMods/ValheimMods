import bpy,struct,math
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[2]/'Assets/Plumbing'
for key in ['pipe','elbow','intake','powered','pipe4','powered2','dragon']:
 bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
 base={'pipe4':'pipe','powered2':'powered'}.get(key,key);data=(root/(base+'.meshbin')).read_bytes();_,count,nt=struct.unpack_from('<iii',data)
 vertices=[];uv=[]
 for i in range(count):
  x,y,z,nx,ny,nz,u,v=struct.unpack_from('<8f',data,12+i*32)
  if key in ['pipe4','powered2']:z*=2
  if key=='dragon':x*=.5;y*=.5;z*=.5
  elif key=='intake':x*=.5;z*=.5
  elif key=='elbow':
   angle=max(0,min(math.pi*.5,math.atan2(z/.74099,(y+.69195)/.69195)));cy=-.69195+.69195*math.cos(angle);cz=.74099*math.sin(angle);x*=.5;y=cy+(y-cy)*.5;z=cz+(z-cz)*.5
  else:x*=.5;y*=.5
  vertices.append((z,-x,y));uv.append((u,v))
 indices=struct.unpack_from('<'+'i'*nt,data,12+count*32);faces=[(indices[i],indices[i+2],indices[i+1]) for i in range(0,nt,3)]
 mesh=bpy.data.meshes.new(key);mesh.from_pydata(vertices,[],faces);mesh.update();layer=mesh.uv_layers.new()
 for poly in mesh.polygons:
  poly.use_smooth=True
  for loop in poly.loop_indices:layer.data[loop].uv=uv[mesh.loops[loop].vertex_index]
 obj=bpy.data.objects.new(key,mesh);bpy.context.collection.objects.link(obj)
 mat=bpy.data.materials.new(key);mat.use_nodes=True;bsdf=mat.node_tree.nodes.get('Principled BSDF');bsdf.inputs['Roughness'].default_value=.48
 tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(root/(base+'.jpg')));mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color']);obj.data.materials.append(mat)
 lo=Vector(tuple(min(p[i] for p in vertices) for i in range(3)));hi=Vector(tuple(max(p[i] for p in vertices) for i in range(3)));center=(lo+hi)*.5
 view=Vector((2.7,-1.6,1.0)) if key=='dragon' else Vector((1.2,-3,1.7))
 bpy.ops.object.camera_add(location=center+view);cam=bpy.context.object;cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=max(hi-lo)*1.25
 for pos,power in [((1,-2,3),140),((-2,-1,1),75),((0,2,2),100)]:
  bpy.ops.object.light_add(type='AREA',location=center+Vector(pos));o=bpy.context.object;o.data.energy=power;o.data.shape='DISK';o.data.size=3;o.rotation_euler=(center-o.location).to_track_quat('-Z','Y').to_euler()
 scene=bpy.context.scene;scene.camera=cam;scene.render.engine='CYCLES';scene.cycles.samples=24;scene.render.resolution_x=512;scene.render.resolution_y=512;scene.render.resolution_percentage=100;scene.render.film_transparent=True;scene.world.color=(.12,.12,.12);scene.render.image_settings.file_format='PNG';scene.render.filepath=str(root/(key+'_narrow.png'));bpy.ops.render.render(write_still=True)
