import bpy, struct, shutil, json
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parent
output=root.parents[1]/'Assets/Plumbing'
output.mkdir(parents=True,exist_ok=True)
settings={
 'AW_Copper_Pipe_2m':('pipe',2/.99951171875,Vector((0,0,.1485))),
 'AW_Copper_Pipe_Surtling':('powered',1.36,Vector((0,0,.2195))),
 'AW_Copper_Elbow_90':('elbow',1.05,Vector((-.499755859375,0,.659))),
 'AW_Copper_Intake':('intake',2.10,Vector((0,0,.99951171875)))
}
report={}
for name,(key,scale,origin) in settings.items():
 bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
 folder=root/name;bpy.ops.import_scene.fbx(filepath=str(next(folder.glob('*.fbx'))))
 vertices=[]
 for obj in bpy.context.scene.objects:
  if obj.type!='MESH':continue
  mesh=obj.data;mesh.calc_loop_triangles();normal_matrix=obj.matrix_world.to_3x3().inverted().transposed()
  for tri in mesh.loop_triangles:
   for loop in tri.loops:
    point=(obj.matrix_world@mesh.vertices[mesh.loops[loop].vertex_index].co-origin)*scale
    normal=(normal_matrix@mesh.corner_normals[loop].vector).normalized()
    if key=='powered':
     axial=1/.99951171875/scale;point.x*=axial;normal.x/=axial;normal.normalize()
    uv=mesh.uv_layers.active.data[loop].uv
    # Blender Z-up -> Unity Y-up. This reflection requires reversing triangle winding below.
    vertices.append((-point.y,point.z,point.x,-normal.y,normal.z,normal.x,uv.x,uv.y))
 with (output/(key+'.meshbin')).open('wb') as stream:
  stream.write(struct.pack('<iii',0x57474D31,len(vertices),len(vertices)))
  for v in vertices:stream.write(struct.pack('<8f',*v))
  indices=[index for start in range(0,len(vertices),3) for index in (start,start+2,start+1)]
  stream.write(struct.pack('<'+'i'*len(vertices),*indices))
 shutil.copy2(next(folder.glob('**/*.jpg')),output/(key+'.jpg'))
 if not (output/(key+'.png')).exists():shutil.copy2(root/(name+'.png'),output/(key+'.png'))
 report[key]={'vertices':len(vertices),'triangles':len(vertices)//3,'bounds':[[min(v[i] for v in vertices),max(v[i] for v in vertices)] for i in range(3)],'scale':scale}
(root/'export-report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
