"""Pack an exported schema5 manifest/script and require the shipped native validator.
Author tooling requires Python 3.9+. Players do not use Python.
"""
import argparse,hashlib,json,subprocess,tempfile,zipfile
from pathlib import Path

def canonical(value):return json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode('utf-8')
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--manifest',type=Path,required=True);p.add_argument('--script',type=Path,required=True);p.add_argument('--validator',type=Path,required=True);p.add_argument('--platform-index',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
 manifest=json.loads(a.manifest.read_text(encoding='utf-8-sig'));script=a.script.read_bytes()
 if manifest.get('format')!='spatial-native-wallpaper' or manifest.get('schema')!=5:raise ValueError('Requires a real schema5 export, never a schema4 archive')
 manifest['files']={'script.json':{'size':len(script),'sha256':hashlib.sha256(script).hexdigest()}};manifest.pop('build_id',None);manifest['build_id']=hashlib.sha256(canonical(manifest)).hexdigest()
 target=a.output.resolve();target.parent.mkdir(parents=True,exist_ok=True)
 if target.exists():raise FileExistsError(target)
 with tempfile.TemporaryDirectory(prefix='spatial-native-export-') as work:
  candidate=Path(work)/'candidate.spatial'
  with zipfile.ZipFile(candidate,'w',compression=zipfile.ZIP_DEFLATED) as z:
   z.writestr('manifest.json',canonical(manifest));z.writestr('script.json',script)
  result=subprocess.run([str(a.validator.resolve()),'--package',str(candidate),'--platform-index',str(a.platform_index.resolve())],capture_output=True,text=True)
  if result.returncode:raise ValueError('Native validator rejected export: '+result.stdout+result.stderr)
  report=json.loads(result.stdout)
  if report.get('valid') is not True or report.get('build_id')!=manifest['build_id']:raise ValueError('Validator identity mismatch')
  # Exclusive creation prevents replacing user content even if target appears during validation.
  with target.open('xb') as stream:stream.write(candidate.read_bytes())
  print(json.dumps({**report,'package':str(target)},ensure_ascii=False))
if __name__=='__main__':main()
