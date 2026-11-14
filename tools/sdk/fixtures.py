import json,hashlib,zipfile,io,copy
from render_profiles import default_identity
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
RULES=json.loads((ROOT/'tools/sdk/rules/v1.json').read_text(encoding='utf-8'))
def canonical(value):return json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()
def ins(op,value=None):return {'op':op,'value':value}
def component(kind,config,fields=None,callbacks=None):return {'kind':kind,'config':config,'fields':fields or {},'callbacks':callbacks or []}
def node():return {'id':'n0','parent':-1,'components':[component('transform',{'position':[0,0,0],'rotation':[0,0,0,1],'scale':[1,1,1]})]}
def sample():
 program={'classes':[{'id':'Demo','fields':[{'id':'target','type':'node'},{'id':'counter','type':'i32'}],'methods':[{'id':'Update','returns':'void','parameters':[],'locals':[],'body':[ins('ldarg',0),ins('dup'),ins('ldfield','counter'),ins('ldc.i4',1),ins('add'),ins('stfield','counter'),ins('ret')]}]}]}
 n=node();n['components'].append(component('script',{'class':'Demo'},{'target':0,'counter':0},['Update']))
 return program,{'frame':[.6,.3375,.5],'resources':[],'nodes':[n],'prefabs':[]}
def archive(program,content,extras=None,manifest_edit=None):
 files={'program.json':canonical(program),'content.json':canonical(content),**(extras or {})}
 manifest={key:RULES[key] for key in ['schema','sdk','vm','engine','pipeline','backend']}
 manifest['render_profile']=default_identity()
 manifest.update(platform='windows-x86_64',rules=RULES['id'],rules_sha256=hashlib.sha256(canonical(RULES)).hexdigest(),files={name:{'size':len(data),'sha256':hashlib.sha256(data).hexdigest()} for name,data in files.items()},licenses=[{'name':'SDK original sample','source':'Spatial Wallpaper tools/sdk/fixtures.py','license':'MIT','attribution':'Spatial Wallpaper contributors'}])
 if manifest_edit:manifest_edit(manifest)
 manifest['build_id']=hashlib.sha256(canonical(manifest)).hexdigest()
 files['manifest.json']=canonical(manifest)
 output=io.BytesIO()
 with zipfile.ZipFile(output,'w',compression=zipfile.ZIP_DEFLATED) as z:
  for name,data in files.items():z.writestr(name,data)
 return output.getvalue()

def light_config():
 return dict(type=2,color=[1,1,1,1],intensity=1.,range=10.,spot=30.,innerSpot=20.,shadows=0,shadowStrength=1.,bias=.05,normalBias=.4,near=.2,usePipelineSettings=True)


def presentation_config():
 return dict(ambient=True,gradient=False,sky=[.1,.15,.2,1],equator=[0,0,0,1],ground=[0,0,0,1],background=True,backgroundColor=[.04,.08,.12,1],post=True,threshold=.75,intensity=.3,scatter=.6,aces=True,exposure=.4,saturation=-12.,vignette=.25,smoothness=.7)
