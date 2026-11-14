"""Focused real native-window input collector lifecycle; no focus/RC claim."""
import argparse,hashlib,json,pathlib,shutil,subprocess,sys,uuid
ROOT=pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references,FRAMEWORK

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--client',type=pathlib.Path,required=True);p.add_argument('--player',type=pathlib.Path,required=True);p.add_argument('--cleanup-fault',action='store_true');a=p.parse_args()
 out=ROOT/'Validation/Content/sdk-runtime/native-render/task3/native-input-source'/uuid.uuid4().hex[:12];out.mkdir(parents=True)
 for name in ['SpatialWallpaper.Preview.exe','Newtonsoft.Json.dll','DesktopHitTester.exe']:shutil.copyfile(a.client/name,out/name)
 shutil.copyfile(a.client/'SpatialWallpaper.Preview.exe.config',out/'NativeInputSourceChecks.exe.config')
 sources=[ROOT/'tools/desktop/NativeInputSourceChecks.cs']+[ROOT/'Prototype/Assets/Desktop/Windows'/name for name in ['NativeRenderProcess.cs','NativeDesktopTarget.cs','WindowsDesktopWindow.cs']]+[ROOT/'Prototype/Assets/Desktop/Core/DesktopDisplay.cs']
 args=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-platform:x64',*references(),'-r:'+str(FRAMEWORK/'System.Drawing.dll'),'-r:'+str(FRAMEWORK/'System.Windows.Forms.dll'),'-r:'+str(out/'SpatialWallpaper.Preview.exe'),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'NativeInputSourceChecks.exe'),*map(str,sources)]
 b=subprocess.run(args,capture_output=True,text=True);(out/'compile.log').write_text(b.stdout+b.stderr,encoding='utf-8');print(out,flush=True);print(b.stdout+b.stderr)
 if b.returncode:return b.returncode
 run=ROOT/'Validation/NB'/('native-input-'+out.name)
 result=subprocess.run([str(out/'NativeInputSourceChecks.exe'),str(a.player.resolve()),str(run),str(out/'DesktopHitTester.exe')]+(['--cleanup-fault'] if a.cleanup_fault else []),capture_output=True,text=True,timeout=120)
 (out/'result.log').write_text(result.stdout+result.stderr,encoding='utf-8')
 (out/'receipt.json').write_text(json.dumps({'exit_code':result.returncode,'run':run.relative_to(ROOT).as_posix(),'sources_sha256':{s.relative_to(ROOT).as_posix():hashlib.sha256(s.read_bytes()).hexdigest() for s in sources},'files_sha256':{f.name:hashlib.sha256(f.read_bytes()).hexdigest() for f in out.iterdir() if f.suffix in ['.exe','.dll']}},indent=2),encoding='utf-8');print(result.stdout+result.stderr);return result.returncode
if __name__=='__main__':raise SystemExit(main())
