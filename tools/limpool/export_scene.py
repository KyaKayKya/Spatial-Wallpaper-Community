import bpy, bmesh, json, hashlib, shutil
from pathlib import Path
root=Path(__file__).resolve().parents[2]
source=root/'art/limpool/source/Scene/Liminal_Pool.blend'
bpy.ops.wm.open_mainfile(filepath=str(source))
# Close the draft's 18 cm gap at the left sill, with 2 cm hidden overlap.
# Only the production copy is saved; the original .blend stays unchanged.
sill=bpy.data.objects['Rear_Left_Window_Sill']
inverse=sill.matrix_world.inverted()
for vertex in sill.data.vertices:
    point=sill.matrix_world@vertex.co
    if point.x < -3.8:
        point.x=-4.02;vertex.co=inverse@point
bpy.context.view_layer.update()
out=root/'Prototype/Assets/Limpool/Generated';out.mkdir(parents=True,exist_ok=True)
items=[];health=[]
for o in bpy.data.objects:
    if o.type!='MESH' or not any(c.name in ['01_Architecture','02_Stairs_Railings'] for c in o.users_collection):continue
    eo=o.evaluated_get(bpy.context.evaluated_depsgraph_get());m=eo.to_mesh();m.calc_loop_triangles()
    bm=bmesh.new();bm.from_mesh(m)
    health.append({'name':o.name,'boundary_edges':sum(e.is_boundary for e in bm.edges),'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'signed_volume':bm.calc_volume(signed=True)})
    bm.free()
    vertices=[];normals=[];uv=[];indices=[]
    nmat=o.matrix_world.to_3x3().inverted().transposed()
    for tri in m.loop_triangles:
        start=len(vertices)
        for li in tri.loops:
            loop=m.loops[li];p=o.matrix_world@m.vertices[loop.vertex_index].co;n=(nmat@loop.normal).normalized()
            vertices.append({'x':p.x,'y':p.z,'z':p.y});normals.append({'x':n.x,'y':n.z,'z':n.y})
            v=m.uv_layers.active.data[li].uv if m.uv_layers.active else (0,0)
            uv.append({'x':v[0],'y':v[1]})
        indices.extend([start,start+2,start+1])
    items.append({'name':o.name,'vertices':vertices,'normals':normals,'uv':uv,'triangles':indices,'rail':'Rail' in o.name or 'Handrail' in o.name})
    eo.to_mesh_clear()
(out/'architecture.json').write_text(json.dumps({'items':items}),encoding='utf-8')
for p in (root/'art/limpool/source/Textures/Plaster').iterdir():shutil.copy2(p,out/p.name)
author=root/'art/limpool/Liminal_Pool_Production.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(author))
report={'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'authoring':str(author),'objects':health,'water_depth_m':1.1,'scale':.53/8,'files':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in out.iterdir() if p.is_file()}}
(root/'Validation/Limpool/export.json').write_text(json.dumps(report,indent=2))
assert all(x['boundary_edges']==0 and x['nonmanifold_edges']==0 and x['signed_volume']>0 for x in health),health
print('PASS: closed outward structural meshes',len(items))
