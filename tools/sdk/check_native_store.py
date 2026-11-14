"""Install real five native packages and check durable explicit-update semantics."""
import argparse, hashlib, json, shutil, subprocess, zipfile
from pathlib import Path
from check_contracts import ROOT, DATA
from windows_framework import references

def revised(source, output, forged=False):
    with zipfile.ZipFile(source) as archive:
        files={name:archive.read(name) for name in archive.namelist()}
    manifest=json.loads(files['manifest.json']); manifest.pop('build_id')
    manifest['revision'] += '-transaction-check'
    if forged: manifest['scene']['source_sha256']='0'*64
    canonical=lambda value:json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()
    manifest['build_id']=hashlib.sha256(canonical(manifest)).hexdigest()
    files['manifest.json']=canonical(manifest)
    with zipfile.ZipFile(output,'x',compression=zipfile.ZIP_DEFLATED) as archive:
        for name,data in files.items():archive.writestr(name,data)

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--packages',type=Path,required=True);parser.add_argument('--platform-index',type=Path,required=True);parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args();out=args.output.resolve();out.mkdir(parents=True,exist_ok=False)
    packages=sorted(args.packages.glob('*.spatial'));assert len(packages)==5,'exactly five real packages required'
    revised(packages[0],out/'update.spatial');revised(packages[0],out/'forged.spatial',True)
    shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
    shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'Checks.exe.config')
    files=[ROOT/'tools/sdk/NativeStoreChecks.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeLimits.Generated.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimePackageInspection.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/InputSnapshot.cs']
    for folder in ['Security','Data','Execution','Resources','Native']:files+=sorted((ROOT/'Packages/com.spatialwallpaper.sdk/Runtime'/folder).glob('*.cs'))
    files+=sorted((ROOT/'Prototype/Assets/ContentRuntime/Core').glob('*.cs'))
    receipt={'sources':{f.relative_to(ROOT).as_posix():hashlib.sha256(f.read_bytes()).hexdigest() for f in files},'packages':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in packages},'passed':False}
    try:
        result=subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-r:'+str(out/'Newtonsoft.Json.dll'),*references(),'-r:System.Net.Http.dll','-out:'+str(out/'Checks.exe'),*map(str,files)],capture_output=True,text=True)
        (out/'compile.log').write_text(result.stdout+result.stderr);result.check_returncode()
        result=subprocess.run([str(out/'Checks.exe'),str(out/'data'),str(args.platform_index.resolve()),*map(lambda p:str(p.resolve()),packages),str(out/'update.spatial'),str(out/'forged.spatial')],capture_output=True,text=True)
        (out/'checks.log').write_text(result.stdout+result.stderr);result.check_returncode()
        receipt['result']=json.loads((out/'data/result.json').read_text());receipt['passed']=True
    finally:(out/'report.json').write_text(json.dumps(receipt,indent=2))
    print(json.dumps({'output':str(out),'passed':receipt['passed']}))
if __name__=='__main__':main()
