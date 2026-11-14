"""Short core export checks; no Unity build, desktop input or RC claim."""
from pathlib import Path
import hashlib,json,shutil,subprocess,sys,uuid,zipfile
from check_contracts import ROOT,DATA
sys.path.insert(0,str(ROOT/'tools/content'))
from store_fixture import create

def main():
 out=ROOT/'Validation/Content/sdk-runtime/G/content-share'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
 create(out/'denied.spatial')
 with zipfile.ZipFile(out/'denied.spatial') as z:files={n:z.read(n) for n in z.namelist()}
 manifest=json.loads(files['manifest.json']);manifest.pop('build_id');manifest['reshare']=True;manifest['license']['mode']='share'
 canonical=lambda v:json.dumps(v,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()
 manifest['build_id']=hashlib.sha256(canonical(manifest)).hexdigest();files['manifest.json']=canonical(manifest)
 with zipfile.ZipFile(out/'allowed.spatial','w',zipfile.ZIP_DEFLATED) as z:
  for name,data in files.items():z.writestr(name,data)
 shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
 sources=[*list((ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Data').glob('*.cs')),ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimePackageInspection.cs',ROOT/'Prototype/Assets/ContentRuntime/Core/ContentStore.cs',ROOT/'Prototype/Assets/ContentRuntime/Core/SandboxContentBuild.cs',ROOT/'tools/sdk/ContentShareChecks.cs']
 receipt={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
 command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-r:'+str(out/'Newtonsoft.Json.dll'),'-r:'+str(DATA/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),'-r:System.IO.Compression.dll','-r:System.IO.Compression.FileSystem.dll','-out:'+str(out/'Checks.exe'),*map(str,sources)]
 report={'status':'failed','sources':receipt}
 try:
  built=subprocess.run(command,capture_output=True,text=True);(out/'compile.log').write_text(built.stdout+built.stderr);built.check_returncode()
  run=subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(out/'Checks.exe'),str(out/'data'),str(out/'denied.spatial'),str(out/'allowed.spatial')],capture_output=True,text=True);(out/'checks.log').write_text(run.stdout+run.stderr);run.check_returncode()
  report['result']=json.loads((out/'data/result.json').read_text());report['status']='passed-core-share-not-RC'
 finally:
  (out/'report.json').write_text(json.dumps(report,indent=2))
if __name__=='__main__':main()
