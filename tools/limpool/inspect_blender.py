import bpy, json
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[2]
bpy.ops.wm.open_mainfile(filepath=str(root/'art/limpool/source/Scene/Liminal_Pool.blend'))
report={'units':{'system':bpy.context.scene.unit_settings.system,'scale':bpy.context.scene.unit_settings.scale_length},'objects':[]}
for o in bpy.data.objects:
    d={'name':o.name,'type':o.type,'position':list(o.location),'rotation':list(o.rotation_euler),'scale':list(o.scale),'collections':[c.name for c in o.users_collection]}
    if o.type=='MESH':
        pts=[o.matrix_world@Vector(v) for v in o.bound_box]
        d.update(bounds=[[min(p[i] for p in pts) for i in range(3)],[max(p[i] for p in pts) for i in range(3)]],vertices=len(o.data.vertices),polygons=len(o.data.polygons),materials=[m.name if m else None for m in o.data.materials],modifiers=[m.type for m in o.modifiers])
    if o.type=='LIGHT':d.update(light_type=o.data.type,power=o.data.energy,color=list(o.data.color))
    report['objects'].append(d)
(root/'Validation/Limpool/source-model.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
