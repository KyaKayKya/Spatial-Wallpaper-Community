"""Actual original preview heartbeat marker; bounded development, no full preview claim."""
import argparse,hashlib,json,pathlib,shutil,subprocess,sys,uuid
ROOT=pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references,FRAMEWORK

def main():
 p=argparse.ArgumentParser(description=__doc__);group=p.add_mutually_exclusive_group(required=True);group.add_argument('--supervisor',type=pathlib.Path);group.add_argument('--client',type=pathlib.Path,help='verify the actual compiled production trusted preview instead of compiling its source into the check');case=p.add_mutually_exclusive_group();case.add_argument('--channel-checks',action='store_true',help='run real IPC codec/Gate checks, without UI');case.add_argument('--original-preview-checks',action='store_true',help='run existing original preview input exclusions against the production client');a=p.parse_args()
 assert not a.original_preview_checks or a.client,'original preview checks require production client'
 out=ROOT/'Validation/Content/sdk-runtime/native-render/task3/preview-heartbeat'/uuid.uuid4().hex[:12];out.mkdir(parents=True);print(out,flush=True)
 folder=a.client or a.supervisor;image='SpatialWallpaper.Preview.exe' if a.client else 'Supervisor.exe'
 for name in [image,'Newtonsoft.Json.dll']:shutil.copyfile(folder/name,out/name)
 shutil.copyfile(folder/(image+'.config'),out/'NativePreviewHeartbeatChecks.exe.config')
 sources=[ROOT/('Supervisor/Tests/InputChannelChecks.cs' if a.channel_checks else 'Supervisor/Tests/PreviewInputChecks.cs' if a.original_preview_checks else 'tools/desktop/NativePreviewHeartbeatChecks.cs')]+([] if a.client or a.channel_checks else [ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Native/Windows/WindowsPreviewInput.cs'])
 frozen={str(f.resolve()):hashlib.sha256(f.read_bytes()).hexdigest() for f in sources+[pathlib.Path(__file__)]+[f for f in folder.rglob('*') if f.is_file()]}
 command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-platform:x64',*references(),'-r:'+str(FRAMEWORK/'System.Drawing.dll'),'-r:'+str(FRAMEWORK/'System.Windows.Forms.dll'),'-r:'+str(out/image),'-out:'+str(out/'NativePreviewHeartbeatChecks.exe'),*map(str,sources)]
 compiled=subprocess.run(command,capture_output=True,text=True);(out/'compile.log').write_text(compiled.stdout+compiled.stderr,encoding='utf-8')
 if compiled.returncode:print(compiled.stdout+compiled.stderr);return compiled.returncode
 compiled_hashes={str(f.resolve()):hashlib.sha256(f.read_bytes()).hexdigest() for f in out.iterdir() if f.suffix in ['.exe','.dll','.config']}
 result=subprocess.run([str(out/'NativePreviewHeartbeatChecks.exe')],capture_output=True,text=True,timeout=15)
 (out/'result.log').write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr)
 unchanged=all(pathlib.Path(p).is_file() and hashlib.sha256(pathlib.Path(p).read_bytes()).hexdigest()==h for p,h in {**frozen,**compiled_hashes}.items())
 exit_code=result.returncode if unchanged else 1
 (out/'receipt.json').write_text(json.dumps({'exit_code':exit_code,'inputs_unchanged':unchanged,'scope':__doc__,'assembly_mode':'production-client' if a.client else 'source-with-Supervisor','source_sha256':frozen,'compiled_sha256':compiled_hashes},indent=2),encoding='utf-8')
 return exit_code
if __name__=='__main__':raise SystemExit(main())
