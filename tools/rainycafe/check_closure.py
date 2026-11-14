import bpy,json
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
root=Path(__file__).resolve().parents[2]
bpy.ops.wm.open_mainfile(filepath=str(root/'art/RainyCafe/rainycafe_production.blend'),load_ui=False,use_scripts=False)
verts=[];faces=[];names=[]
for o in bpy.context.scene.objects:
    if o.type!='MESH' or o.hide_render:continue
    pts=[o.matrix_world@Vector(v) for v in o.bound_box]
    name=o.name.lower()
    if not any(s in name for s in ('wall','floor','ceiling','rear','window','shaker','glaz','door')):continue
    if any(s in name for s in ('plant','vase','latte','candle','table','chair')):continue
    names.append(o.name)
    base=len(verts);verts.extend(o.matrix_world@v.co for v in o.data.vertices)
    faces.extend(tuple(base+i for i in p.vertices) for p in o.data.polygons)
bvh=BVHTree.FromPolygons(verts,faces)
miss=[];rays=0;s=.53/5.4
for x in (-.2,0,.2):
 for y in (-.1,0,.1):
  for z in (-.35,-.6,-.95):
   eye=Vector((x/s,z/s,y/s+3.0375/2+.008))
   count=0;examples=[]
   for ix in range(41):
    for iy in range(25):
     target=Vector(((-.5+ix/40)*5.4*.9999,0,.008+(iy/24*.9999+.00005)*3.0375))
     rays+=1
     if bvh.ray_cast(eye,(target-eye).normalized(),100)[0] is None:
      count+=1
      if len(examples)<5:examples.append([ix,iy])
   miss.append(dict(eye=[x,y,z],misses=count,examples=examples))
report=dict(objects=names,eyes=miss,misses=sum(e['misses'] for e in miss),rays=rays,eye_count=27)
(root/'Validation/RainyCafe/closure-diagnostic.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report))
