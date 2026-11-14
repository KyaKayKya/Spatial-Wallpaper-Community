"""Short real Unity GPU resource lifecycle check; no desktop input or UI automation."""
import argparse,hashlib,json,os,subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);a=p.parse_args();out=a.output.resolve();out.mkdir(parents=True,exist_ok=False)
 env=dict(os.environ,SPATIAL_NATIVE_DYNAMIC_CHECK_OUTPUT=str(out));env.setdefault('ALLUSERSPROFILE',env.get('PROGRAMDATA','C:/ProgramData'))
 command=['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe','-batchmode','-projectPath',str(ROOT/'Prototype'),'-executeMethod','SpatialWallpaper.Desktop.Editor.NativeDynamicResourceChecks.Run','-logFile',str(out/'unity.log')]
 files=[p for base in [ROOT/'Prototype/Assets',ROOT/'Packages/com.spatialwallpaper.sdk'] for p in base.rglob('*.cs')];before={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
 r=subprocess.run(command,cwd=ROOT,env=env);unchanged=before=={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files};(out/'receipt.json').write_text(json.dumps({'exit_code':r.returncode,'inputs_unchanged':unchanged,'sources':before},indent=2),encoding='utf-8');print(json.dumps({'output':str(out),'exit_code':r.returncode,'inputs_unchanged':unchanged}));return r.returncode if unchanged else 1
if __name__=='__main__':raise SystemExit(main())
