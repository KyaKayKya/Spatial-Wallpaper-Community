"""Background-only explicit thread identity/counter controls; no desktop access."""
from pathlib import Path
import sys, subprocess, uuid, json, shutil, hashlib, argparse
root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(root/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references
from case_process import CaseProcess

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--negative-cached-cycles',action='store_true')
    parser.add_argument('--binding',action='store_true')
    args=parser.parse_args()
    out = root/'Validation/Content/sdk-runtime/E/thread-evidence'/uuid.uuid4().hex
    out.mkdir(parents=True)
    compile_files = [root/'Supervisor/Tests'/name for name in ['OwnedThreadEvidence.cs', 'ThreadEvidenceChecks.cs']]
    if args.binding:
        if args.negative_cached_cycles:raise ValueError('incompatible modes')
        compile_files=[root/name for name in ['Supervisor/Tests/DesktopHitThreadBindingChecks.cs','Supervisor/Tests/OwnedThreadEvidence.cs','Supervisor/Tests/OwnedThreadSampler.cs','Supervisor/Interactive/DesktopHitClient.cs','Supervisor/Windows/Native.cs','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/WindowsMachineClock.cs']]
    files=list(compile_files)
    if args.binding:
        files += [root/'Supervisor/Tests/DesktopHitThreadIdentityChecks.cpp',root/'Supervisor/Interactive/DesktopHitTester.cpp',root/'tools/sdk/experiments/native_build.py']
    files += [Path(__file__), root/'tools/sdk/case_process.py', root/'tools/sdk/case_bootstrap.py', root/'tools/sdk/windows_framework.py']
    receipt = {str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
    report = {'status':'failed', 'sources':receipt, 'bindingControls':args.binding, 'negativeCachedCycles':args.negative_cached_cycles, 'scope':'background control only; no real desktop helper execution', 'wct':{'status':'unavailable','reason':'not-enabled-no-privilege-adjustment'}}
    owner = None
    try:
        report['toolchain']={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in [Path(sys.executable),DATA/'MonoBleedingEdge/bin/mono.exe',DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe',DATA/'Managed/Newtonsoft.Json.dll']}
        if args.binding:
            vc=sorted(Path('C:/Program Files/Microsoft Visual Studio/2022/Community/VC/Tools/MSVC').iterdir())[-1]
            for p in [vc/'bin/Hostx64/x64/cl.exe',vc/'bin/Hostx64/x64/link.exe']:
                report['toolchain'][str(p)]=hashlib.sha256(p.read_bytes()).hexdigest()
        shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
        if args.negative_cached_cycles:
            text=files[0].read_text(encoding='utf-8');needle='row["cycles"]=cycles;'
            if text.count(needle)!=1:raise RuntimeError('negative-patch-shape')
            copy=out/'OwnedThreadEvidence.cs';copy.write_text(text.replace(needle,'row["cycles"]=0UL;'),encoding='utf-8')
            compile_files=[copy,files[1]]
        subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,compile_files)],check=True,timeout=30)
        # The trusted gated Python bootstrap owns the checks executable as a descendant.
        script = out/'launch.py'
        launch='import subprocess\n'
        if args.binding:
            launch+='import sys\nfrom pathlib import Path\nsys.path.insert(0,'+repr(str(root/'tools/sdk/experiments'))+')\nfrom native_build import compile_probe\n'
            launch+='compile_probe(Path('+repr(str(root/'Supervisor/Tests/DesktopHitThreadIdentityChecks.cpp'))+'),Path('+repr(str(out/'NativeChecks.exe'))+'))\n'
            launch+='subprocess.run('+repr([str(out/'NativeChecks.exe')])+',check=True,timeout=5)\n'
        check_args=[str(out/'Checks.exe'),str(out),*([str(out/'NativeChecks.exe')] if args.binding else [])]
        script.write_text(launch+'raise SystemExit(subprocess.call('+repr(check_args)+'))\n',encoding='utf-8')
        with (out/'case.log').open('w',encoding='utf-8') as log:
            owner=CaseProcess();owner.start(script,[],root,log)
            code=owner.wait(30 if args.binding else 15);report['exitCode']=code
            if code:raise RuntimeError('thread-evidence-control-failed')
            if args.negative_cached_cycles:raise RuntimeError('negative-unexpected-pass')
        if receipt!={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}:raise RuntimeError('source-drift')
        report['status']='partial-thread-evidence-control'
    except BaseException as error:
        report['error']=repr(error)
    finally:
        if owner:
            try:owner.close()
            except BaseException as error:report.update(status='cleanup_error',cleanupError=repr(error))
            report['process_tree']=owner.evidence
            if owner.evidence.get('unexpected_remaining_members'):report['status']='cleanup_error'
        report['artifacts']={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in out.iterdir() if p.is_file()}
        (out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
        print(out)
    return 0 if report['status']=='partial-thread-evidence-control' else 1
if __name__=='__main__':raise SystemExit(main())
