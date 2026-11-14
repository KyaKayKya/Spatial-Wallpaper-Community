"""Export the five native artworks using the current Unity cache and a fresh destination."""
import argparse,hashlib,json,os,subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);a=p.parse_args();out=a.output.resolve();out.mkdir(parents=True,exist_ok=False)
 inputs=[p for folder in ['Prototype/Assets','Packages/com.spatialwallpaper.sdk'] for p in (ROOT/folder).rglob('*') if p.is_file() and p.suffix in ['.cs','.unity','.shader']];before={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
 env=dict(os.environ,SPATIAL_NATIVE_ARTWORK_OUTPUT=str(out));env.setdefault('ALLUSERSPROFILE',env.get('PROGRAMDATA','C:/ProgramData'))
 command=['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe','-batchmode','-quit','-projectPath',str(ROOT/'Prototype'),'-executeMethod','SpatialWallpaper.Desktop.Editor.NativeArtworkExport.Run','-logFile',str(out/'unity.log')]
 result=subprocess.run(command,cwd=ROOT,env=env);unchanged=all(hashlib.sha256((ROOT/name).read_bytes()).hexdigest()==digest for name,digest in before.items())
 report={'command':command,'exit_code':result.returncode,'inputs_unchanged':unchanged,'source_commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'inputs':before,'files':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in out.iterdir() if p.is_file()}}
 (out/'export-receipt.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps({k:v for k,v in report.items() if k not in ['inputs','files','command']}));return 0 if result.returncode==0 and unchanged and (out/'native-artworks.json').is_file() else 1
if __name__=='__main__':raise SystemExit(main())
