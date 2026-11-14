"""Ray-test authored opaque cabin surfaces, excluding the intentional window."""
import bpy,json,itertools
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[2]
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'art/Train/train_compartment_complete.blend'),load_ui=False,use_scripts=False)
verts=[];faces=[]
for o in bpy.context.scene.objects:
 if o.type!='MESH' or not any(s in o.name for s in ['continuous cabin wall','Continuous ivory cabin ceiling','Complete rear walnut bulkhead','TrainFloor']):continue
 offset=len(verts);verts.extend(o.matrix_world@v.co for v in o.data.vertices)
 faces.extend(tuple(offset+i for i in p.vertices) for p in o.data.polygons)
bvh=BVHTree.FromPolygons(verts,faces,all_triangles=False)
W,H,F=4.266666666666667,2.4,-1.7
scale=.53/W
misses=[];backfaces=[];tested=0;windows=0
for ex,ez,distance in itertools.product((-.2,0,.2),(-.1,0,.1),(.35,.6,.95)):
 eye=Vector((ex/scale,F-distance/scale,H/2+ez/scale))
 for ix,iz in itertools.product(range(81),range(45)):
  p=Vector((W*((ix+.5)/81-.5),F,H*(iz+.5)/45));direction=(p-eye).normalized()
  hit,normal,index,dist=bvh.ray_cast(eye,direction,30);tested+=1
  if hit is None:
   q=eye+direction*((2.72-eye.y)/direction.y)
   if abs(q.x)<=1.04+1e-5 and .755-1e-5<=q.z<=1.965+1e-5:windows+=1
   else:misses.append([ex,ez,distance,ix,iz,list(q)])
  elif normal.dot(direction)>=0:backfaces.append([ex,ez,distance,ix,iz])
result=dict(pass_test=not misses and not backfaces,rays=tested,window_rays=windows,uncovered=len(misses),backfaces=len(backfaces),examples=(misses+backfaces)[:8])
(ROOT/'Validation/Train/enclosure.json').write_text(json.dumps(result,indent=2))
print('ENCLOSURE',json.dumps(result),flush=True)
if not result['pass_test']:raise RuntimeError('Incomplete or backward-facing cabin enclosure')
