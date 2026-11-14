"""Targeted schema5 package admission positives and security negatives (no rendering)."""
import argparse,copy,hashlib,json,subprocess,zipfile
from pathlib import Path

def canonical(v):return json.dumps(v,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--validator',required=True,type=Path);p.add_argument('--script',required=True,type=Path);p.add_argument('--platform-index',required=True,type=Path);p.add_argument('--output',required=True,type=Path);a=p.parse_args();a.output.mkdir(parents=True,exist_ok=False)
 script=a.script.read_bytes();definition=json.loads(script);index=json.loads(a.platform_index.read_text());scene=index['scenes'][0]
 base={'format':'spatial-native-wallpaper','schema':5,'sdk':definition['sdk'],'command_abi':definition['command_abi'],'catalog_digest':definition['catalog_digest'],'work_id':'3bda8a11-1641-41b9-81da-fab68e0e9bc7','revision':'check-only','name':'Admission fixture','scene':{k:v for k,v in scene.items() if k in ['id','source_sha256','library_digest']},'node_paths':['']*len(definition['content']['nodes']),'resources':[],'license':{'mode':'local-validation','records':[{'name':'fixture','source':'repository','license':'MIT','redistribution':'verified','attribution':'Spatial Wallpaper'}]},'files':{'script.json':{'size':len(script),'sha256':hashlib.sha256(script).hexdigest()}}}
 cases=[('valid',None,True),('schema4',lambda m:m.update(schema=4),False),('abi',lambda m:m.update(command_abi=99),False),('scene-digest',lambda m:m['scene'].update(source_sha256='0'*64),False),('library-digest',lambda m:m['scene'].update(library_digest='0'*64),False),('traversal',lambda m:m.update(node_paths=['../outside']),False),('node-count',lambda m:m.update(node_paths=['','']),False),('resource-spoof',lambda m:m['resources'].append({'name':'foreign','asset_key':'absent','kind':'mesh','source_sha256':'0'*64}),False),('unverified-share',lambda m:(m['license'].update(mode='share'),m['license']['records'][0].update(redistribution='unverified')),False),('tampered-script',None,False),('archive-path',None,False)]
 records=[]
 for label,change,expected in cases:
  m=copy.deepcopy(base)
  if change:change(m)
  m['build_id']=hashlib.sha256(canonical(m)).hexdigest();file=a.output/(label+'.spatial')
  with zipfile.ZipFile(file,'w',compression=zipfile.ZIP_DEFLATED) as z:
   z.writestr('manifest.json',canonical(m));z.writestr('../script.json' if label=='archive-path' else 'script.json',script+b' ' if label=='tampered-script' else script)
  result=subprocess.run([str(a.validator.resolve()),'--package',str(file.resolve()),'--platform-index',str(a.platform_index.resolve())],capture_output=True,text=True);report=json.loads(result.stdout)
  passed=(result.returncode==0)==expected and report['valid']==expected;records.append({'case':label,'passed':passed,'result':report})
  if not passed:raise AssertionError(records[-1])
 (a.output/'checks.json').write_text(json.dumps({'passed':True,'checks':records,'scope':'real .NET admission CLI; no author execution or Player/visual acceptance'},indent=2));print(json.dumps({'passed':True,'checks':len(records)}))
if __name__=='__main__':main()
