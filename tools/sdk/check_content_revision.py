"""Short content update transaction checks; no Unity or input long test."""
import hashlib,json,shutil,subprocess,sys,uuid,zipfile
from check_contracts import ROOT,DATA
sys.path.insert(0,str(ROOT/'tools/content'))
from store_fixture import create

def main():
 out=ROOT/'Validation/Content/sdk-runtime/G/content-revision'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
 create(out/'first.spatial')
 with zipfile.ZipFile(out/'first.spatial') as z:files={n:z.read(n) for n in z.namelist()}
 manifest=json.loads(files['manifest.json']);manifest.pop('build_id');manifest['name']='Updated library fixture'
 canonical=lambda v:json.dumps(v,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()
 manifest['build_id']=hashlib.sha256(canonical(manifest)).hexdigest();files['manifest.json']=canonical(manifest)
 with zipfile.ZipFile(out/'second.spatial','w',zipfile.ZIP_DEFLATED) as z:
  for name,value in files.items():z.writestr(name,value)
 shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
 sources=[*list((ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Data').glob('*.cs')),*list((ROOT/'Prototype/Assets/ContentRuntime/Core').glob('*.cs')),ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimePackageInspection.cs',ROOT/'tools/sdk/ContentRevisionChecks.cs']
 report={'status':'failed','sources':{p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}}
 try:
  command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-r:'+str(out/'Newtonsoft.Json.dll'),'-r:'+str(DATA/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),'-r:System.IO.Compression.dll','-r:System.IO.Compression.FileSystem.dll','-out:'+str(out/'Checks.exe'),*map(str,sources)]
  result=subprocess.run(command,capture_output=True,text=True);(out/'compile.log').write_text(result.stdout+result.stderr);result.check_returncode()
  result=subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(out/'Checks.exe'),str(out/'data'),str(out/'first.spatial'),str(out/'second.spatial')],capture_output=True,text=True);(out/'checks.log').write_text(result.stdout+result.stderr);result.check_returncode()
  report['result']=json.loads((out/'data/result.json').read_text());report['status']='passed-core-content-update-not-RC'
 finally:(out/'report.json').write_text(json.dumps(report,indent=2))
if __name__=='__main__':main()
