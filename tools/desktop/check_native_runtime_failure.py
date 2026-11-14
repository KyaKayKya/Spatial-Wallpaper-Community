"""Actual owned Win32 detach failure through original DesktopRuntime methods."""
from pathlib import Path
import argparse,hashlib,json,os,subprocess,uuid
ROOT=Path(__file__).resolve().parents[2]
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--player',type=Path,required=True);p.add_argument('--profile',type=Path,required=True);a=p.parse_args()
 out=ROOT/'Validation/Content/sdk-runtime/native-render/task3/native-runtime-failure'/uuid.uuid4().hex[:12];out.mkdir(parents=True);print(out,flush=True)
 env=dict(os.environ,SPATIAL_NATIVE_FAILURE_OUTPUT=str(out),SPATIAL_NATIVE_FAILURE_PLAYER=str(a.player.resolve()),SPATIAL_NATIVE_FAILURE_PROFILE=str(a.profile.resolve()))
 sources=[ROOT/'Prototype/Assets/Desktop'/n for n in ['DesktopNativeAdapter.cs','Editor/NativeRuntimeFailureChecks.cs','Windows/NativeDesktopTarget.cs','Windows/WindowsDesktopWindow.cs']];hashes={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
 command=['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe','-batchmode','-quit','-projectPath',str(ROOT/'Prototype'),'-executeMethod','SpatialWallpaper.Desktop.Editor.NativeRuntimeFailureChecks.Run','-logFile',str(out/'unity.log')]
 result=subprocess.run(command,env=env,timeout=120)
 (out/'receipt.json').write_text(json.dumps({'exit_code':result.returncode,'command':command,'source_sha256':hashes,'player_assembly_sha256':hashlib.sha256((a.player.parent/'SpatialWallpaper_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest()},indent=2),encoding='utf-8')
 if result.returncode:return result.returncode
 report=json.loads((out/'report.json').read_text());assert report['status']=='passed-real-detach-failure-owner-retry' and not report.get('cleanup_error'),report
 assert hashes=={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
 print(report['status'],report['checks']);return 0
if __name__=='__main__':raise SystemExit(main())
