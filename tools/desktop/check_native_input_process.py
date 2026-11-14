"""Actual per-renderer trusted input role lifecycle; no focus/RC claim."""
import argparse,hashlib,json,pathlib,shutil,subprocess,sys,uuid
ROOT=pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references,FRAMEWORK

def main():
 p=argparse.ArgumentParser(description=__doc__)
 for name in ['client','supervisor','player']:p.add_argument('--'+name,type=pathlib.Path,required=True)
 p.add_argument('--cleanup-fault',action='store_true')
 p.add_argument('--registration-reply-loss',action='store_true')
 p.add_argument('--owner-source-override',type=pathlib.Path)
 p.add_argument('--late-cleanup-fault',action='store_true')
 p.add_argument('--host-exit-on-unregister',action='store_true')
 a=p.parse_args();out=ROOT/'Validation/Content/sdk-runtime/native-render/task3/native-input-process'/uuid.uuid4().hex[:12];out.mkdir(parents=True)
 for name in ['Supervisor.exe','Newtonsoft.Json.dll']:shutil.copyfile(a.supervisor/name,out/name)
 shutil.copyfile(a.supervisor/'Supervisor.exe.config',out/'NativeInputProcessChecks.exe.config')
 sources=[ROOT/'tools/desktop/NativeInputProcessChecks.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Native/Windows/NativeInputProcess.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Native/Windows/INativeInputSession.cs']+[ROOT/'Prototype/Assets/Desktop/Windows'/name for name in ['NativeRenderProcess.cs','NativeDesktopTarget.cs','WindowsDesktopWindow.cs']]+[ROOT/'Prototype/Assets/Desktop/Core/DesktopDisplay.cs']
 if a.owner_source_override:
  replacement=a.owner_source_override.resolve();assert replacement.is_relative_to(ROOT/'Validation/Content/sdk-runtime') and replacement.name=='NativeInputProcess.cs','only owned input-role fault fixtures may override the owner'
  sources=[replacement if s.name=='NativeInputProcess.cs' else s for s in sources]
 args=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-platform:x64',*references(),'-r:'+str(FRAMEWORK/'System.Drawing.dll'),'-r:'+str(out/'Supervisor.exe'),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'NativeInputProcessChecks.exe'),*map(str,sources)]
 b=subprocess.run(args,capture_output=True,text=True);(out/'compile.log').write_text(b.stdout+b.stderr,encoding='utf-8');print(out,flush=True);print(b.stdout+b.stderr)
 if b.returncode:return b.returncode
 run=ROOT/'Validation/NB'/('native-input-process-'+out.name)
 result=subprocess.run([str(out/'NativeInputProcessChecks.exe'),str(a.player.resolve()),str(run),str((a.client/'SpatialWallpaper.Preview.exe').resolve())]+(['--host-exit-on-unregister'] if a.host_exit_on_unregister else ['--late-cleanup-fault'] if a.late_cleanup_fault else ['--cleanup-fault'] if a.cleanup_fault else ['--registration-reply-loss'] if a.registration_reply_loss else []),capture_output=True,text=True,timeout=120)
 (out/'result.log').write_text(result.stdout+result.stderr,encoding='utf-8')
 (out/'receipt.json').write_text(json.dumps({'exit_code':result.returncode,'run':run.relative_to(ROOT).as_posix(),'sources_sha256':{s.relative_to(ROOT).as_posix():hashlib.sha256(s.read_bytes()).hexdigest() for s in sources},'files_sha256':{f.name:hashlib.sha256(f.read_bytes()).hexdigest() for f in out.iterdir() if f.suffix in ['.exe','.dll']}},indent=2),encoding='utf-8');print(result.stdout+result.stderr);return result.returncode
if __name__=='__main__':raise SystemExit(main())
