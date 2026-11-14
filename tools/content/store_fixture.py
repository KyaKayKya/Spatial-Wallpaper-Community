"""Original minimal schema2 data fixture for library compatibility checks."""
from pathlib import Path
import json,hashlib,struct,zipfile
def create(path):
 canonical=lambda value:json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()
 digest=lambda value:hashlib.sha256(value).hexdigest()
 texture=struct.pack('<I9if',0x32545753,1,1,4,1,0,0,1,1,0,0.)+bytes([255,255,255,255]);name='textures/'+digest(texture)+'.stex'
 scene={'frame':[.6,.3375,.5],'nodes':[],'meshes':[],'textures':[name],'materials':[{'shader':'Universal Render Pipeline/Unlit','bundle':'','properties':{},'keywords':[],'queue':-1,'instancing':False}],'lightmaps':[],'lightmapMode':'non-directional','environment':{'ambient':[1,1,1,1],'fog':False,'fogColor':[0,0,0,1],'fogDensity':0}}
 files={'scene.json':canonical(scene),name:texture}
 manifest={'schema':2,'template_id':'0d5909ba-4215-4613-8a6c-5bd75251a1f7','name':'Original minimal library fixture','engine':'2022.3.62f3','pipeline':'14.0.12','sdk':'0.2.0','platform':'windows-x86_64','color_space':'linear','capabilities':['static-data-v1','shader-only-2022.3.62f3-v1'],'entry':'scene.json','files':{k:{'size':len(v),'sha256':digest(v)} for k,v in files.items()},'license':{'mode':'local-validation','records':[{'name':'Original one-pixel texture and data','source':'tools/content/store_fixture.py','license':'MIT','redistribution':'verified','attribution':'Spatial Wallpaper contributors'}]},'editable':False,'reshare':False}
 manifest['build_id']=digest(canonical(manifest));files['manifest.json']=canonical(manifest);path=Path(path);path.parent.mkdir(parents=True,exist_ok=True)
 with zipfile.ZipFile(path,'w',zipfile.ZIP_DEFLATED) as z:
  for k,v in files.items():z.writestr(k,v)
 return path
