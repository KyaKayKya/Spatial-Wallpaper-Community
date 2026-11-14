"""Compile the shared SDK/content boundary sources and run their actual checks."""
from pathlib import Path
import argparse
import shutil
import subprocess
ROOT=Path(__file__).resolve().parents[2]
DATA=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data')
OUT=ROOT/'Validation/Content/p2/checks'

def main():
    global OUT
    parser=argparse.ArgumentParser()
    parser.add_argument('--suite',default='package,projection,loader')
    parser.add_argument('--package')
    parser.add_argument('--output-root')
    args=parser.parse_args()
    evidence=Path(args.output_root).resolve() if args.output_root else ROOT/'Validation/Content/p2'
    assert evidence.is_relative_to(ROOT/'Validation/Content'),'Evidence must stay under Validation/Content'
    OUT=evidence/'checks'
    (evidence/'reports').mkdir(parents=True,exist_ok=True)
    assert set(args.suite.split(',')) <= {'package','projection','loader'},'unsupported suite'
    args.package=str(Path(args.package).resolve()) if args.package else str(ROOT/'Validation/Content/p2/sample.spatial')
    if not Path(args.package).exists():raise SystemExit('Export the SDK sample first: python tools/content/build.py author')
    if not (ROOT/'Validation/Content/resource-probe/bundles/shaders').exists():
        subprocess.run([__import__('sys').executable,str(ROOT/'tools/content/resource_probe.py')],check=True)
    OUT.mkdir(parents=True,exist_ok=True)
    if args.package:
        from shader_attacks import generate
        generate(Path(args.package),OUT/'fixtures/shader-attacks')
    shutil.copy2(DATA/'Managed/Newtonsoft.Json.dll',OUT/'Newtonsoft.Json.dll')
    sources=list((ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Data').glob('*.cs'))
    sources+=list((ROOT/'Prototype/Assets/ContentRuntime/Core').glob('*.cs'))
    sources.append(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimePackageInspection.cs')
    command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',
        '-r:'+str(OUT/'Newtonsoft.Json.dll'),'-r:'+str(DATA/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),
        '-r:System.IO.Compression.dll','-r:System.IO.Compression.FileSystem.dll','-out:'+str(OUT/'ContentChecks.exe'),str(ROOT/'tools/content/ContentChecks.cs'),str(ROOT/'tools/content/ShaderProgramChecks.cs'),*map(str,sources)]
    result=subprocess.run(command,capture_output=True,text=True)
    text=result.stdout+result.stderr
    if result.returncode==0:
        command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(OUT/'ContentChecks.exe'),str(OUT/'fixtures')]
        if args.package:command.append(args.package)
        result=subprocess.run(command,capture_output=True,text=True)
        text+=result.stdout+result.stderr
    (OUT/'report.txt').write_text(text,encoding='utf-8')
    print(text,flush=True)
    print('Evidence:',OUT/'report.txt',flush=True)
    if result.returncode==0 and 'loader' in args.suite.split(','):
        import build
        build.run()
        build.fingerprint()
    if result.returncode==0:
        shutil.copy2(OUT/'report.txt',evidence/'reports/boundary.txt')
    raise SystemExit(result.returncode)
if __name__=='__main__':main()
