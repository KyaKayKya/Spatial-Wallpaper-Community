from pathlib import Path
import hashlib,json,shutil,subprocess,sys,uuid

root=Path(__file__).resolve().parents[2]
out=root/'Validation/Content/sdk-runtime/G/content-remove'/uuid.uuid4().hex
out.mkdir(parents=True)
state=root/'Validation/Sdk'/('remove-'+uuid.uuid4().hex[:10])
data=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data')
mono=data/'MonoBleedingEdge/bin/mono.exe'
shutil.copy2(data/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
inputs=[Path(__file__),Path(__file__).with_name('ContentRemoveChecks.cs'),*list((root/'Packages/com.spatialwallpaper.sdk/Runtime/Data').glob('*.cs')),*list((root/'Prototype/Assets/ContentRuntime/Core').glob('*.cs')),root/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimePackageInspection.cs']
receipt={p.relative_to(root).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
command=[str(mono),str(data/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-r:'+str(out/'Newtonsoft.Json.dll'),'-r:'+str(data/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),'-r:System.IO.Compression.dll','-r:System.IO.Compression.FileSystem.dll','-out:'+str(out/'Checks.exe'),*map(str,inputs[1:])]
result=subprocess.run(command,capture_output=True,text=True);output=result.stdout+result.stderr
if result.returncode==0:
 result=subprocess.run([str(mono),str(out/'Checks.exe'),str(state)],capture_output=True,text=True,timeout=30);output+=result.stdout+result.stderr
(out/'checks.txt').write_text(output,encoding='utf-8')
unchanged=all(hashlib.sha256((root/p).read_bytes()).hexdigest()==digest for p,digest in receipt.items())
(out/'report.json').write_text(json.dumps({'status':'passed-development-content-remove' if result.returncode==0 and unchanged else 'failed','exit_code':result.returncode,'sources':receipt,'sources_unchanged':unchanged,'test_state_path':str(state),'scope':'owned on-disk library removal, references and failure order; not client/Host or complete G acceptance'},indent=2),encoding='utf-8')
print(output);print(out)
raise SystemExit(result.returncode if unchanged else 1)
