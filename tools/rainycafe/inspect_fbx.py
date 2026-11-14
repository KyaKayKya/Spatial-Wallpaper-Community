import bpy,json
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[2]
for name in ('Furniture','Plants','Lamps','Glass'):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(root/'Prototype/Assets/RainyCafe/Generated'/(name+'.fbx')))
    for o in bpy.context.scene.objects:
        if o.type!='MESH':continue
        points=[o.matrix_world@v.co for v in o.data.vertices]
        print(name,o.name,'parent',o.parent.name if o.parent else None,'matrix',list(map(list,o.matrix_world)),'bounds',[[min(v[i] for v in points) for i in range(3)],[max(v[i] for v in points) for i in range(3)]],flush=True)
