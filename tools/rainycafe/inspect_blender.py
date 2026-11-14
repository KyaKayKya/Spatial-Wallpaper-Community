"""Read-only inventory of evaluated source geometry and shader dependencies."""
import bpy
import json
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
source = ROOT / 'art/RainyCafe/RainyCafe_Complete_Project/cafe_refinement_v2.blend'
bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False, use_scripts=False)
scene = bpy.context.scene
objects = []
for o in scene.objects:
    record = dict(name=o.name, type=o.type, hidden=o.hide_render, location=list(o.location))
    if o.type == 'MESH':
        points = [o.matrix_world @ Vector(v) for v in o.bound_box]
        record.update(vertices=len(o.data.vertices), bounds=[[min(v[i] for v in points) for i in range(3)], [max(v[i] for v in points) for i in range(3)]], materials=[m.name if m else None for m in o.data.materials])
    if o.type == 'LIGHT':
        record.update(light_type=o.data.type, energy=o.data.energy, color=list(o.data.color))
    if o.type == 'CAMERA':
        record.update(rotation=list(o.rotation_euler), lens=o.data.lens, shift=[o.data.shift_x,o.data.shift_y])
    objects.append(record)
materials=[]
for mat in bpy.data.materials:
    nodes=[]
    if mat.use_nodes:
        for n in mat.node_tree.nodes:
            info=dict(type=n.type)
            if n.type=='TEX_IMAGE': info['image']=n.image.name if n.image else None
            if n.type=='BSDF_PRINCIPLED':
                info['inputs']={s.name: ('linked' if s.is_linked else list(s.default_value) if hasattr(s.default_value,'__len__') else s.default_value) for s in n.inputs if hasattr(s,'default_value')}
            nodes.append(info)
    materials.append(dict(name=mat.name,nodes=nodes))
data=dict(blender=bpy.app.version_string, objects=objects,materials=materials,images=[dict(name=i.name,size=list(i.size),packed=bool(i.packed_file),path=i.filepath) for i in bpy.data.images],world=scene.world.name if scene.world else None)
out=ROOT/'Validation/RainyCafe/source-inventory.json'
out.write_text(json.dumps(data,indent=2))
print('INVENTORY',len(objects),'objects',len(materials),'materials',len(bpy.data.images),'images',flush=True)
for o in objects:
    if o['type'] in ('LIGHT','CAMERA') or any(t in o['name'].lower() for t in ('wall','floor','ceiling','glass','candle','flame','coffee','street','building','rain','steam')):
        print(json.dumps(o),flush=True)
