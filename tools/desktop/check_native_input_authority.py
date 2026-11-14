"""Broker-authoritative native commit gate, bounded metadata only; not RC."""
import argparse,hashlib,json,pathlib,shutil,subprocess,sys,uuid
ROOT=pathlib.Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA,compile_checks
from windows_framework import references

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--supervisor',type=pathlib.Path,required=True);p.add_argument('--cached-authority',action='store_true');a=p.parse_args()
 out=ROOT/'Validation/Content/sdk-runtime/native-render/task3/input-authority'/uuid.uuid4().hex[:12];out.mkdir(parents=True);print(out,flush=True)
 if compile_checks(out,framework=True):return 1
 shutil.copyfile(a.supervisor/'Supervisor.exe',out/'Supervisor.exe');shutil.copyfile(a.supervisor/'Supervisor.exe.config',out/'NativeInputAuthorityChecks.exe.config')
 sources=[ROOT/'tools/desktop/NativeInputAuthorityChecks.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/InputSnapshot.cs']+[ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Native'/n for n in ['SceneCommandCatalog.Generated.cs','SceneCommandProtocol.cs','ScriptBridgePump.cs']]
 command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-platform:x64',*references(),'-r:supervisor='+str(out/'Supervisor.exe'),'-r:'+str(out/'ContractChecks.exe'),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'NativeInputAuthorityChecks.exe'),*map(str,sources)]
 b=subprocess.run(command,capture_output=True,text=True);(out/'compile.log').write_text(b.stdout+b.stderr,encoding='utf-8');print(b.stdout+b.stderr)
 if b.returncode:return b.returncode
 result=subprocess.run([str(out/'NativeInputAuthorityChecks.exe'),str(out)]+(['--cached-authority'] if a.cached_authority else []),capture_output=True,text=True,timeout=30)
 (out/'result.log').write_text(result.stdout+result.stderr,encoding='utf-8');(out/'receipt.json').write_text(json.dumps({'exit_code':result.returncode,'mode':'cached-authority-negative' if a.cached_authority else 'authoritative-metadata','sources_sha256':{s.relative_to(ROOT).as_posix():hashlib.sha256(s.read_bytes()).hexdigest() for s in sources},'files_sha256':{f.name:hashlib.sha256(f.read_bytes()).hexdigest() for f in out.iterdir() if f.suffix in ['.exe','.dll']}},indent=2),encoding='utf-8');print(result.stdout+result.stderr);return result.returncode
if __name__=='__main__':raise SystemExit(main())
