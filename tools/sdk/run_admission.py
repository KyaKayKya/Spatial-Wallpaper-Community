from pathlib import Path
import sys,subprocess,json,shutil,uuid,hashlib,platform
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/sdk/experiments'))
from windows_sandbox import launch
from check_contracts import compile_checks,OUT,DATA
from fixtures import sample,archive,ins
from provenance import sources
def main():
 source_hashes=sources()
 if compile_checks():return 1
 run_id=uuid.uuid4().hex
 root=ROOT/'Validation/Sdk'/('b'+run_id[:10])
 runtime=root/'runtime'
 shutil.copytree(DATA/'MonoBleedingEdge',runtime,ignore=shutil.ignore_patterns('include','msbuild','xbuild','monodoc','mono-configuration-crypto'))
 shutil.copyfile(OUT/'ContractChecks.exe',root/'ContractChecks.exe');shutil.copyfile(OUT/'Newtonsoft.Json.dll',root/'Newtonsoft.Json.dll')
 p,c=sample();(root/'positive.spatial').write_bytes(archive(p,c))
 p['classes'][0]['methods'][0]['body']=[ins('call','System.IO::File::ReadAllText'),ins('ret')]
 (root/'malicious.spatial').write_bytes(archive(p,c))
 results={}
 for name,expected in [('positive',True),('malicious',False)]:
  output=root/'output'/(name+'.json')
  code,sid=launch(runtime/'bin/mono.exe',[root/'ContractChecks.exe',root/(name+'.spatial'),output],root,isolated_desktop=True,timeout=30)
  if not output.is_file():raise RuntimeError('restricted Validator produced no result: '+str(code))
  result=json.loads(output.read_text(encoding='utf-8-sig'))
  if (code==0)!=expected or result['accepted']!=expected:raise RuntimeError('wrong admission outcome: '+json.dumps(result))
  results[name]={'exit_code':code,'package_sha256':hashlib.sha256((root/(name+'.spatial')).read_bytes()).hexdigest(),**result}
 if sources()!=source_hashes:raise RuntimeError('source changed during admission run')
 report={'source_hashes':source_hashes,'status':'passed-admission-contracts-only','commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'platform':platform.platform(),'run_id':run_id,'cases':results,'validator_sha256':hashlib.sha256((root/'ContractChecks.exe').read_bytes()).hexdigest(),'not_verified':['C# export conversion','VM execution/bindings','production import/preview/client','macOS']}
 target=ROOT/'Validation/Content/sdk-runtime/reports/stage-B-admission.json'
 target.parent.mkdir(parents=True,exist_ok=True);target.write_text(json.dumps(report,indent=2),encoding='utf-8')
 print(target)
 return 0
if __name__=='__main__':raise SystemExit(main())
