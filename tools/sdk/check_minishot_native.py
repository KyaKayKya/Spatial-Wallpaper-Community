"""Compile the real MiniShot author source directly, without rewriting it against offline test facades.
This tests author adaptation only, not VM/LPAC/CAS implementation, scene ACK gates,
real audio, rendered appearance or hardware input. No Unity process is launched.
"""
import argparse,hashlib,json,subprocess,time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);p.add_argument('--unity-data',type=Path,default=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data'));a=p.parse_args();out=a.output.resolve();out.mkdir(parents=True,exist_ok=False)
 source=ROOT/'Prototype/Assets/NativeArtworks/Runtime/MiniShotNative.cs';facade=ROOT/'tools/sdk/MiniShotNativeChecks.cs';sources={str(f.relative_to(ROOT)):sha(f) for f in [source,facade]};mono=a.unity_data/'MonoBleedingEdge/bin/mono.exe';compiler=a.unity_data/'MonoBleedingEdge/lib/mono/4.5/csc.exe';exe=out/'MiniShotNativeChecks.exe';cmd=[str(mono),str(compiler),'-nologo','-langversion:latest','-out:'+str(exe),str(source),str(facade)];compile=subprocess.run(cmd,capture_output=True,text=True);(out/'compile.log').write_text(compile.stdout+compile.stderr,encoding='utf-8');report={'source_commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'sources':sources,'compile_exit':compile.returncode,'scope':__doc__.strip(),'passed':False}
 if compile.returncode==0:
  started=time.monotonic();run=subprocess.run([str(exe)],capture_output=True,text=True,timeout=15);report.update({'execution_exit':run.returncode,'elapsed_seconds':time.monotonic()-started,'checks':[line.split('\t',1)[1] for line in run.stdout.splitlines() if line.startswith('PASS\t')]});(out/'run.log').write_text(run.stdout+run.stderr,encoding='utf-8');report['passed']=run.returncode==0 and len(report['checks'])>=25 and sources=={str(f.relative_to(ROOT)):sha(f) for f in [source,facade]}
 (out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report,indent=2));return 0 if report['passed'] else 1
if __name__=='__main__':raise SystemExit(main())
