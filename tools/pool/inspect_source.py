import bpy,json
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[2]
bpy.ops.wm.open_mainfile(filepath=str(root/'.local-assets/pool/approved_pool.blend'),load_ui=False,use_scripts=False)
items=[]
for o in bpy.context.scene.objects:
 if o.type=='MESH' and ('wall' in o.name.lower() or 'ceiling' in o.name.lower()):
  points=[o.matrix_world@Vector(c) for c in o.bound_box]
  items.append({'name':o.name,'min':[min(p[i] for p in points) for i in range(3)],'max':[max(p[i] for p in points) for i in range(3)],'modifiers':[(m.name,m.type,getattr(m,'width',None)) for m in o.modifiers]})
(root/'Validation/Pool/source-seams.json').write_text(json.dumps(items,indent=2))
