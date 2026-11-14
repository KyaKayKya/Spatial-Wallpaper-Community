"""Build the existing prepared desktop into one fresh isolated release directory."""
import argparse, hashlib, json, os, subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
def digest(path):
    sha=hashlib.sha256()
    with path.open('rb') as source:
        for block in iter(lambda:source.read(1048576),b''):sha.update(block)
    return sha.hexdigest()
def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--output',type=Path,required=True);args=parser.parse_args();out=args.output.resolve()
    if out.exists():raise FileExistsError('fresh player output required')
    out.mkdir(parents=True)
    paths=[]
    for folder in ('Prototype/Assets','Packages/com.spatialwallpaper.sdk'):
        paths += [p for p in (ROOT/folder).rglob('*') if p.is_file() and p.suffix in ('.cs','.shader','.unity')]
    paths += [ROOT/'Prototype/Packages/manifest.json',ROOT/'Prototype/Packages/packages-lock.json']
    before={p.relative_to(ROOT).as_posix():digest(p) for p in paths}
    env=dict(os.environ,SPATIAL_PREPARED_OUTPUT=str(out/'SpatialWallpaper.exe'))
    env.setdefault('ALLUSERSPROFILE',env.get('PROGRAMDATA','C:/ProgramData'))
    command=['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe','-batchmode','-quit','-projectPath',str(ROOT/'Prototype'),'-buildTarget','Win64','-executeMethod','DesktopBuild.BuildPrepared','-logFile',str(out/'build-unity.log')]
    result=subprocess.run(command,cwd=ROOT,env=env)
    unchanged=before=={p.relative_to(ROOT).as_posix():digest(p) for p in paths}
    receipt={'source_commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'sources':before,'source_and_original_scene_unchanged':unchanged,'exit_code':result.returncode,'scope':'prepared original desktop player build; no visual, sound or physical-display acceptance','files':{p.relative_to(out).as_posix():digest(p) for p in out.rglob('*') if p.is_file()}}
    (out/'build.json').write_text(json.dumps(receipt,indent=2),encoding='utf-8')
    if result.returncode or not unchanged or not (out/'SpatialWallpaper.exe').is_file():raise RuntimeError('native release player build failed; inspect receipt and log')
    print(json.dumps({'player':str(out),'source_commit':receipt['source_commit']}))
if __name__=='__main__':main()
