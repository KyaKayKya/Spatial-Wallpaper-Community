"""Normalize existing FBX roots while preserving baked UVs and world geometry."""
import bpy,json,hashlib
from pathlib import Path
root=Path(__file__).resolve().parents[2]
folder=root/'Prototype/Assets/RainyCafe/Generated'
manifest=json.loads((folder/'manifest.json').read_text())
for record in manifest['objects']:
    name=record['name'];path=folder/(name+'.fbx')
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path))
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    assert len(meshes)==1,(name,len(meshes))
    o=meshes[0];bpy.context.view_layer.objects.active=o;o.select_set(True)
    world=o.matrix_world.copy();o.parent=None;o.matrix_world=world
    before=[o.matrix_world@v.co for v in o.data.vertices]
    uv_before=[tuple(v.uv) for v in o.data.uv_layers.active.data]
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    assert max((a-b.co).length for a,b in zip(before,o.data.vertices))<.00001
    assert uv_before==[tuple(v.uv) for v in o.data.uv_layers.active.data]
    points=[(v.co.x,v.co.z-.008-3.0375/2,v.co.y) for v in o.data.vertices]
    record['bounds']=[[min(v[i] for v in points) for i in range(3)],[max(v[i] for v in points) for i in range(3)]]
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False,add_leaf_bones=False)
    manifest['sha256'][path.name]=hashlib.sha256(path.read_bytes()).hexdigest()
    print('NORMALIZED',name,record['bounds'],flush=True)
manifest['root_normalization']='World vertices preserved within 0.00001 authored metres; UVs byte-value identical before/after transform application'
(folder/'manifest.json').write_text(json.dumps(manifest,indent=2))
