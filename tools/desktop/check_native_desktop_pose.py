"""Per-target original calibration reuse; partial native development checks."""
import hashlib,json,pathlib,shutil,subprocess,sys,uuid
ROOT=pathlib.Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references

def main():
 out=ROOT/'Validation/Content/sdk-runtime/native-render/task3/native-desktop-pose'/uuid.uuid4().hex[:12];out.mkdir(parents=True);print(out,flush=True)
 shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll');shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'NativeDesktopPoseChecks.exe.config')
 files=[ROOT/'tools/desktop/NativeDesktopPoseChecks.cs']
 for folder in ['Prototype/Assets/Tracking/Core','Prototype/Assets/Calibration/Core']:files+=list((ROOT/folder).glob('*.cs'))
 files+=[ROOT/'Prototype/Assets/Desktop/Core'/n for n in ['DesktopDisplay.cs','DesktopTrackingState.cs','NativeDesktopPose.cs'] if (ROOT/'Prototype/Assets/Desktop/Core'/n).exists()]
 command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-platform:x64',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'NativeDesktopPoseChecks.exe'),*map(str,files)]
 b=subprocess.run(command,capture_output=True,text=True);(out/'compile.log').write_text(b.stdout+b.stderr,encoding='utf-8');print(b.stdout+b.stderr)
 if b.returncode:return b.returncode
 result=subprocess.run([str(out/'NativeDesktopPoseChecks.exe')],capture_output=True,text=True,timeout=15);(out/'result.log').write_text(result.stdout+result.stderr,encoding='utf-8');(out/'report.json').write_text(json.dumps({'exit_code':result.returncode,'scope':'original calibration/metadata only; no Tracker launch, Unity or physical display claim','sources_sha256':{p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in files}},indent=2),encoding='utf-8');print(result.stdout+result.stderr);return result.returncode
if __name__=='__main__':raise SystemExit(main())
