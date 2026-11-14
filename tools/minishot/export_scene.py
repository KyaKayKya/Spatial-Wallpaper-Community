"""Blender background export of the supplied MiniShotGame with a 16:9 aperture."""
import hashlib
import json
import struct
import subprocess
import zipfile
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parents[2]
common = Path(subprocess.check_output(['git','-C',str(ROOT),'rev-parse','--path-format=absolute','--git-common-dir'],text=True).strip())
source = common.parent/'art/MiniShotGame/spatial_targets_v2_package.zip'
assert source.is_file(), 'Restore the user-provided MiniShotGame source package in the primary checkout'
source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
art = ROOT/'art/MiniShotGame'
art.mkdir(parents=True,exist_ok=True)
original = art/'source/spatial_targets_v2.blend'
original.parent.mkdir(parents=True,exist_ok=True)
with zipfile.ZipFile(source) as archive:
    original.write_bytes(archive.read('spatial_targets_v2/spatial_targets_v2.blend'))
bpy.ops.wm.open_mainfile(filepath=str(original))
scene = bpy.context.scene
targets = list(bpy.data.collections['20_TARGET_DISCS_filled_geometry'].objects)
geometry = []
for name in ['10_GRID_ROOM_real_geometry','20_TARGET_DISCS_filled_geometry','30_FINE_TRAILS']:
    geometry.extend(bpy.data.collections[name].objects)

# Refit the room and centers, while leaving every target mesh circular.
for obj in geometry:
    if obj in targets:
        obj.location.z *= .9
    else:
        for vertex in obj.data.vertices:
            vertex.co.z *= .9
        if 'line_start' in obj:
            a,b=list(obj['line_start']),list(obj['line_end'])
            a[2]*=.9; b[2]*=.9
            obj['line_start']=a; obj['line_end']=b
screen=bpy.data.objects['ScreenPlane_Y0_NONRENDER']
for vertex in screen.data.vertices: vertex.co.z *= .9
screen['height_m']=.9
scene['screen_height_m']=.9
scene['unity_mapping']='Unity authored (x,y,z) = Blender (x,z,y); uniform scale 0.53/1.6'
scene['adaptation']='16:9 aperture; room height and target/trail centers refitted; circular disc meshes preserved'
scene.render.resolution_x=1600; scene.render.resolution_y=900
bpy.context.view_layer.update()
bpy.ops.wm.save_as_mainfile(filepath=str(art/'spatial_targets_16x9.blend'))

out=ROOT/'Prototype/Assets/MiniShotGame/Generated'
out.mkdir(parents=True,exist_ok=True)
manifest={'format':1,'sourceSha256':source_hash,'screen':[1.6,.9],'depth':3.6,'scale':.53/1.6,'objects':[],'targets':[]}
for obj in sorted(geometry,key=lambda o:o.name):
    mesh=obj.data; mesh.calc_loop_triangles()
    colors=[]
    for mat in mesh.materials:
        emission=next(n for n in mat.node_tree.nodes if n.type=='EMISSION')
        color=emission.inputs['Color'].default_value
        strength=emission.inputs['Strength'].default_value
        colors.append(tuple(float(color[i])*strength for i in range(3))+(1.,))
    vertices=[]; indices=[]; unique={}
    for triangle in mesh.loop_triangles:
        color=colors[triangle.material_index]
        for index in reversed(triangle.vertices):
            q=obj.matrix_world@mesh.vertices[index].co
            vertex=(float(q.x),float(q.z),float(q.y))+color
            if vertex not in unique:
                unique[vertex]=len(vertices); vertices.append(vertex)
            indices.append(unique[vertex])
    data=struct.pack('<III',0x4D534731,len(vertices),len(indices))
    data+=b''.join(struct.pack('<7f',*v) for v in vertices)
    data+=struct.pack('<%dI'%len(indices),*indices)
    filename=obj.name+'.bytes'
    (out/filename).write_bytes(data)
    manifest['objects'].append({'name':obj.name,'file':filename,'sha256':hashlib.sha256(data).hexdigest(),'vertices':len(vertices),'indices':len(indices)})
    if obj in targets:
        q=obj.matrix_world.translation
        manifest['targets'].append({'name':obj.name,'center':[q.x,q.z,q.y],'radius':obj['physical_radius_m']})
(out/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print('MINISHOT_EXPORT_OK',len(geometry),sum(x['vertices'] for x in manifest['objects']))
