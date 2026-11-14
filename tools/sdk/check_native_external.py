"""Short schema5 external-data envelope admission checks. Executes no author code."""
import argparse,copy,hashlib,json,subprocess,zipfile,io
from pathlib import Path

def canonical(x): return json.dumps(x,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()
def identity(x): return hashlib.sha256(canonical({k:v for k,v in x.items() if k!='build_id'})).hexdigest()
def archive(files):
 b=io.BytesIO()
 with zipfile.ZipFile(b,'w',zipfile.ZIP_DEFLATED) as z:
  for k,v in files.items(): z.writestr(k,v)
 return b.getvalue()
def main():
 p=argparse.ArgumentParser();p.add_argument('--validator',type=Path,required=True);p.add_argument('--native-fixture',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();a.output.mkdir(parents=True,exist_ok=False)
 with zipfile.ZipFile(a.native_fixture) as z:s=json.loads(z.read('script.json'))
 current=json.loads(subprocess.check_output([str(a.validator.resolve()),'--identity'],text=True));s.update(current)
 node={'name':'Original node','parent':-1,'position':[0,0,0],'rotation':[0,0,0,1],'scale':[1,1,1],'mesh':-1,'materials':[],'lightmap':-1,'lightmapST':[1,1,0,0]}
 scene={'frame':[.53,.3,1],'nodes':[node],'meshes':[],'textures':[],'materials':[],'lightmaps':[],'lightmapMode':'non-directional','environment':{'ambient':[1,1,1,1],'fog':False,'fogColor':[0,0,0,1],'fogDensity':0}}
 license={'mode':'share','records':[{'name':'Original empty diagnostic graph','source':'Original procedural data','license':'CC0-1.0','redistribution':'verified','attribution':'Diagnostic only; no third-party art'}]}
 sf=canonical(scene);inner={'schema':2,'template_id':'577c5307-5be9-49b2-96db-e7b5c11151ba','name':'Data check','build_id':'','engine':'2022.3.62f3','pipeline':'14.0.12','sdk':'0.2.0','platform':'windows-x86_64','color_space':'linear','capabilities':['static-data-v1','shader-only-2022.3.62f3-v1'],'entry':'scene.json','files':{'scene.json':{'size':len(sf),'sha256':hashlib.sha256(sf).hexdigest()}},'license':license,'editable':True,'reshare':True};inner['build_id']=identity(inner);assets=archive({'manifest.json':canonical(inner),'scene.json':sf})
 s['program']={'classes':[]};s['content']={'frame':scene['frame'],'resources':[],'prefabs':[],'nodes':[{'id':'n0','parent':-1,'components':[{'kind':'transform','config':{k:node[k] for k in ['position','rotation','scale']},'fields':{},'callbacks':[]}]}]};s['build_id']=identity(s);script=canonical(s)
 m={'format':'spatial-external-native-wallpaper','schema':5,'sdk':s['sdk'],'command_abi':s['command_abi'],'catalog_digest':s['catalog_digest'],'work_id':inner['template_id'],'revision':'1.0.0','name':'Data check','assets':{'format':'canonical-static-data-v1','build_id':inner['build_id']},'files':{k:{'size':len(v),'sha256':hashlib.sha256(v).hexdigest()} for k,v in {'script.json':script,'assets.spatial':assets}.items()},'license':license,'build_id':''};m['build_id']=identity(m)
 results=[]
 def check(name,manifest,scriptbytes=script,assetbytes=assets,extra=None,accept=False):
  manifest=copy.deepcopy(manifest);manifest['build_id']=identity(manifest);files={'manifest.json':canonical(manifest),'script.json':scriptbytes,'assets.spatial':assetbytes};files.update(extra or {});path=a.output/(name+'.spatial');path.write_bytes(archive(files));r=subprocess.run([str(a.validator.resolve()),'--package',str(path.resolve()),'--platform-index','unused'],capture_output=True,text=True);report=json.loads(r.stdout);assert report['valid']==accept,(name,report);results.append({'name':name,'accepted':report['valid'],'error':report.get('error')})
 check('valid',m,accept=True)
 bad=copy.deepcopy(m);bad['schema']=4;check('schema4',bad)
 bad=copy.deepcopy(m);bad['work_id']='../bad';check('work-path',bad)
 bad=copy.deepcopy(m);bad['license']['records'][0]['redistribution']='unverified';check('license-mismatch',bad)
 check('asset-byte-tamper',m,assetbytes=assets+b'!')
 check('extra-prefab',m,extra={'scene.prefab':b'untrusted'})
 bad=copy.deepcopy(m);bad['assets']['build_id']='0'*64;check('inner-build-mismatch',bad)
 check('script-byte-tamper',m,scriptbytes=script+b'!')
 (a.output/'checks.json').write_text(json.dumps({'passed':True,'checks':results,'scope':'pure strict admission only; full GPU import separate'},indent=2));print(json.dumps(results));return 0
if __name__=='__main__':raise SystemExit(main())
