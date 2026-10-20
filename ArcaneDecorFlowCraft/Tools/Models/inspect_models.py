import bpy, json
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parent
report=[]
for folder in sorted(root.glob('AW_*')):
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(next(folder.glob('*.fbx'))))
    entries=[]
    for obj in bpy.context.scene.objects:
        if obj.type!='MESH':continue
        pts=[obj.matrix_world@Vector(p) for p in obj.bound_box]
        entries.append(dict(name=obj.name,vertices=len(obj.data.vertices),polygons=len(obj.data.polygons),min=[min(p[i] for p in pts) for i in range(3)],max=[max(p[i] for p in pts) for i in range(3)],materials=[m.name for m in obj.data.materials]))
    report.append(dict(model=folder.name,objects=entries))
(root/'model-inspection.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report))
