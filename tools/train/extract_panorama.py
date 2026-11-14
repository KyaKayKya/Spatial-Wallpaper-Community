import bpy,json,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Prototype/Assets/Train/Generated'
source=ROOT/'art/Train/train_compartment_v3.blend'
if not source.exists():source=ROOT.parents[1]/'art/Train/train_compartment_v3.blend'
bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False,use_scripts=False)
mat=bpy.data.materials['CC0 Snowy Park 01 photographic exterior']
for n in mat.node_tree.nodes:
 if n.type in {'TEX_IMAGE','TEX_ENVIRONMENT'} and n.image:
  img=n.image;print('PANORAMA',img.name,list(img.size),img.filepath,flush=True)
  img.scale(4096,2048)
  scene=bpy.context.scene;scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB';scene.render.image_settings.color_depth='8'
  dest=OUT/'SnowyPark.png';img.save_render(str(dest),scene=scene)
  p=OUT/'manifest.json';d=json.loads(p.read_text());d['files']=list(dict.fromkeys(d['files']+[dest.name]));d['sha256'][dest.name]=hashlib.sha256(dest.read_bytes()).hexdigest();d['exterior_source']='Packed source: '+img.name+'; source material labels CC0 Snowy Park 01';p.write_text(json.dumps(d,indent=2));break
else:raise RuntimeError('Source panorama image not found')
