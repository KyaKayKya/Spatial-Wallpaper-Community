import bpy,bmesh,json
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'art/showcase/blender-study';EVID=ROOT/'Validation/Showcase/blender-study'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'showcase-organic-final.blend'),load_ui=False,use_scripts=False)
prop=bpy.data.objects['Organic_Sculpture__Flareworks_Studio']
ev=prop.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh()
bm=bmesh.new();bm.from_mesh(mesh)
boundary=sum(e.is_boundary for e in bm.edges);nonmanifold=sum(not e.is_manifold for e in bm.edges)
bmesh.ops.triangulate(bm,faces=list(bm.faces))
volume=0;centroid=Vector((0,0,0))
for f in bm.faces:
 a,b,c=[prop.matrix_world@v.co for v in f.verts]
 v=a.dot(b.cross(c))/6
 volume+=v;centroid+=(a+b+c)*(v/4)
centroid/=volume
pts=[prop.matrix_world@v.co for v in bm.verts];z=min(v.z for v in pts)
base=sorted(set((round(v.x,7),round(v.y,7)) for v in pts if abs(v.z-z)<.00005))
def cross(o,a,b):return (a[0]-o[0])*(b[1]-o[1])-(a[1]-o[1])*(b[0]-o[0])
lower=[]
for p in base:
 while len(lower)>=2 and cross(lower[-2],lower[-1],p)<=0:lower.pop()
 lower.append(p)
upper=[]
for p in reversed(base):
 while len(upper)>=2 and cross(upper[-2],upper[-1],p)<=0:upper.pop()
 upper.append(p)
hull=lower[:-1]+upper[:-1]
inside=all(cross(a,b,centroid)>=-1e-8 for a,b in zip(hull,hull[1:]+hull[:1]))
seen=set();components=[]
for v in bm.verts:
 if v in seen:continue
 stack=[v];group=[];seen.add(v)
 while stack:
  k=stack.pop();group.append(k)
  for e in k.link_edges:
   q=e.other_vert(k)
   if q not in seen:seen.add(q);stack.append(q)
 components.append(len(group))
info={'boundary_edges':boundary,'nonmanifold_edges':nonmanifold,'connected_components_vertices':components,'uniform_density_volume_m3':volume,'uniform_density_centroid_m':list(centroid),'centroid_projection_inside_contact_hull':inside,'source_images_packed':all(i.packed_file for i in bpy.data.images if i.source=='FILE'),'saved_camera':bpy.context.scene.camera.name,'all_cabinet_panels_visible':all(not o.hide_render for o in bpy.context.scene.objects if o.name.startswith('Cabinet_'))}
assert nonmanifold==0
assert inside
assert len(components)==2
assert info['source_images_packed'] and info['all_cabinet_panels_visible']
(EVID/'verification.json').write_text(json.dumps(info,indent=2))
print(json.dumps(info))
bm.free();ev.to_mesh_clear()
