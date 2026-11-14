"""Compile actual LibraryNativePreview.cs with offline host facades and real SoundMix.
No GUI, Unity process, rendered pixels, RPC, real sound or hardware input test.
"""
import argparse,hashlib,json,shutil,subprocess,time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);p.add_argument('--unity-data',type=Path,default=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data'));a=p.parse_args();out=a.output.resolve();out.mkdir(parents=True,exist_ok=False);sources=[ROOT/'Prototype/Assets/Library/Runtime/LibraryNativePreview.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/SoundMix.cs',ROOT/'tools/sdk/NativePreviewSoundChecks.cs'];hashes={str(f.relative_to(ROOT)):hashlib.sha256(f.read_bytes()).hexdigest() for f in sources};shutil.copyfile(a.unity_data/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll');cmd=[str(a.unity_data/'MonoBleedingEdge/bin/mono.exe'),str(a.unity_data/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-define:UNITY_STANDALONE_WIN','-r:'+str(out/'Newtonsoft.Json.dll'),'-r:Microsoft.CSharp.dll','-r:'+str(a.unity_data/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),'-out:'+str(out/'NativePreviewSoundChecks.exe'),*map(str,sources)];r=subprocess.run(cmd,capture_output=True,text=True);(out/'compile.log').write_text(r.stdout+r.stderr,encoding='utf-8');report={'source_commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'sources':hashes,'compile_exit':r.returncode,'scope':__doc__.strip(),'passed':False}
 if r.returncode==0:
  started=time.monotonic();run=subprocess.run([str(out/'NativePreviewSoundChecks.exe')],capture_output=True,text=True,timeout=10);(out/'run.log').write_text(run.stdout+run.stderr,encoding='utf-8');report.update({'execution_exit':run.returncode,'elapsed_seconds':time.monotonic()-started,'checks':[s.split('\t',1)[1] for s in run.stdout.splitlines() if s.startswith('PASS\t')],'passed':run.returncode==0})
 (out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report,indent=2));return 0 if report['passed'] else 1
if __name__=='__main__':raise SystemExit(main())
