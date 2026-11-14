"""Run with Blender -b --python; converts the supplied native scene without saving it."""
import bpy, json, re, hashlib, subprocess
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[2]
common=Path(subprocess.check_output(['git','-C',str(ROOT),'rev-parse','--path-format=absolute','--git-common-dir'],text=True).strip())
SOURCE=common.parent/'art/Parking/Parking_Refinement_v2_Core/source/parking_infinity_v2.blend'
OUT=ROOT/'Prototype/Assets/Parking/Generated'
OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
assert all(not l.is_missing for l in bpy.data.libraries)
def safe(s): return re.sub('[^A-Za-z0-9_-]','_',s)
def xyz(p): return [float(p.x),float(p.z),float(p.y)]
materials=[]
for m in bpy.data.materials:
    if not m.use_nodes: continue
    bs=next((n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None)
    if not bs: continue
    textures=[]
    for n in m.node_tree.nodes:
        if n.type=='TEX_IMAGE' and n.image:
            im=n.image; name=safe(im.name)+'.png'; path=OUT/name
            if not path.exists():
                old=im.filepath_raw; fmt=im.file_format
                im.filepath_raw=str(path);im.file_format='PNG';im.save();im.filepath_raw=old;im.file_format=fmt
            textures.append({'name':im.name,'file':name})
    materials.append({'name':m.name,'color':list(bs.inputs['Base Color'].default_value),
                     'roughness':float(bs.inputs['Roughness'].default_value),'metallic':float(bs.inputs['Metallic'].default_value),'textures':textures})
def mesh(o,name,relative=None,ratio=None):
    if ratio:
        o=o.copy();o.data=o.data.copy();bpy.context.scene.collection.objects.link(o)
        mod=o.modifiers.new('RuntimeLOD','DECIMATE');mod.ratio=ratio
    ev=o.evaluated_get(bpy.context.evaluated_depsgraph_get());me=ev.to_mesh();me.calc_loop_triangles()
    mat=o.matrix_world; normal=mat.to_3x3().inverted().transposed()
    vs=[];ns=[];uv=[];sub={};lookup={}
    for tri in me.loop_triangles:
        idx=[]
        for li in reversed(tri.loops):
            loop=me.loops[li];v=me.vertices[loop.vertex_index]
            p=mat@v.co
            if relative is not None:p-=relative
            n=(normal@me.corner_normals[li].vector).normalized()
            t=me.uv_layers.active.data[li].uv if me.uv_layers.active else (0,0)
            key=tuple(round(float(x),7) for x in (*xyz(p),*xyz(n),*t))
            if key not in lookup:
                lookup[key]=len(vs)//3;vs.extend(key[:3]);ns.extend(key[3:6]);uv.extend(key[6:])
            idx.append(lookup[key])
        sub.setdefault(tri.material_index,[]).extend(idx)
    result={'name':name,'vertices':vs,'normals':ns,'uv':uv,'submeshes':[{'material':me.materials[i].name if i<len(me.materials) and me.materials[i] else '', 'indices':a} for i,a in sub.items()]}
    ev.to_mesh_clear()
    if ratio:bpy.data.objects.remove(o,do_unlink=True)
    return result
tv=[o for o in bpy.data.objects if o.type=='MESH' and o.name.startswith('TV_')]
master=next(o for o in bpy.data.collections['MASTER_Lamp_7p5m'].objects if o.type=='MESH')
print('MASTER',master.name,list(master.location),list(master.dimensions))
origin=master.matrix_world.translation.copy()
meshes=[mesh(o,o.name) for o in tv]
meshes += [mesh(master,'LampNear',origin),mesh(master,'LampLOD',origin,.025)]
# Distant emitters use three camera-facing strips: full lenses become subpixel at this angle.
# Nearby fixtures retain the original four-head geometry. This LOD keeps the luminous silhouette.
near,lod=meshes[-2:]
lod['submeshes']=[s for s in lod['submeshes'] if 'lens' not in s['material']]
indices=[]
for x0,x1 in [(-.995,-.46),(-.19,.19),(.46,.995)]:
    offset=len(lod['vertices'])//3
    for x,y,u,v in [(x0,7.18,0,0),(x0,7.36,0,1),(x1,7.36,1,1),(x1,7.18,1,0)]:
        lod['vertices'].extend([x,y,-.95]);lod['normals'].extend([0,0,-1]);lod['uv'].extend([u,v])
    indices.extend([offset,offset+1,offset+2,offset,offset+2,offset+3])
lod['submeshes'].append({'material':next(s['material'] for s in near['submeshes'] if 'lens' in s['material']),'indices':indices})
layout=json.loads((SOURCE.parent.parent/'export/layout.json').read_text())
screens=[]
for o in tv:
    if o.name not in ('TV_02_Screen','TV_05_Screen'):continue
    center=sum((o.matrix_world@Vector(v) for v in o.bound_box),Vector())/8
    screens.append({'name':o.name,'center':xyz(center)})
result={'source_sha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'materials':materials,'meshes':meshes,
        'lamps':[{'position':[p['position_m'][0],p['position_m'][2],p['position_m'][1]]} for p in layout['lamp_instances']], 'screens':screens}
(OUT/'scene.json').write_text(json.dumps(result,separators=(',',':')))
print('EXPORTED',[(m['name'],sum(len(s['indices'])//3 for s in m['submeshes'])) for m in meshes])
