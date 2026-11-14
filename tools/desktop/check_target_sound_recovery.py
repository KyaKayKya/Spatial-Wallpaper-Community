"""Target sound preferences, exact offline persistence and native pause routing; no GUI."""
import hashlib,json,pathlib,shutil,subprocess,sys,uuid
ROOT=pathlib.Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references

def main():
 out=ROOT/'Validation/Content/sdk-runtime/native-render/target-sound-recovery'/uuid.uuid4().hex[:12];out.mkdir(parents=True);print(out,flush=True)
 shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll');shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'TargetSoundRecoveryChecks.exe.config')
 files=[ROOT/'tools/desktop/TargetSoundRecoveryChecks.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/SoundMix.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeLimits.Generated.cs',ROOT/'Prototype/Assets/Library/Core/ArtworkCatalog.cs']
 for folder in ['Prototype/Assets/Tracking/Core','Prototype/Assets/Calibration/Core']:files+=list((ROOT/folder).glob('*.cs'))
 files+=list((ROOT/'Prototype/Assets/Desktop/Core').glob('DesktopAppliedStore.cs'))
 files+=[ROOT/'Prototype/Assets/Desktop/Core/DesktopPolicy.cs',ROOT/'Prototype/Assets/ContentRuntime/Core/ContentTargetSoundStore.cs']
 command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-platform:x64',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'TargetSoundRecoveryChecks.exe'),*map(str,files)]
 b=subprocess.run(command,capture_output=True,text=True);(out/'compile.log').write_text(b.stdout+b.stderr,encoding='utf-8');print(b.stdout+b.stderr)
 if b.returncode:return b.returncode
 result=subprocess.run([str(out/'TargetSoundRecoveryChecks.exe'),str(out/'data')],capture_output=True,text=True,timeout=15);(out/'result.log').write_text(result.stdout+result.stderr,encoding='utf-8');(out/'report.json').write_text(json.dumps({'exit_code':result.returncode,'scope':'pure metadata/state and actual owned file IO; no formal Launch or rendering claim','sources_sha256':{p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in files}},indent=2),encoding='utf-8');print(result.stdout+result.stderr);return result.returncode
if __name__=='__main__':raise SystemExit(main())
