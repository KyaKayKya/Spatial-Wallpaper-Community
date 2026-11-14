"""Original installed-layout Manager close, tray reopen and background ownership."""
from pathlib import Path
import argparse,hashlib,json,shutil,sys,uuid,subprocess
from check_contracts import ROOT,DATA
from windows_framework import references,FRAMEWORK
from provenance import sources
from toolchain_process import run_toolchain
sys.path.insert(0,str(ROOT/'Supervisor/Tests'))
from run_production import packages

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--client',type=Path,required=True)
parser.add_argument('--runtime',type=Path,required=True)
parser.add_argument('--package',type=Path,help='Use this exported author scene instead of the generated fixture.')
parser.add_argument('--show-startup-diagnostic',action='store_true')
parser.add_argument('--native-ui',action='store_true')
parser.add_argument('--manual-focus',action='store_true')
parser.add_argument('--audio-picker',action='store_true')
args=parser.parse_args();client=args.client.resolve();runtime=args.runtime.resolve()
assert not args.audio_picker or args.native_ui,'audio picker validation requires native UI input checks'
build=json.loads((client/'client-build.json').read_text())
assert build['status']=='passed-development-original-client-build'
nested=client/'player/sdk-runtime'
if nested.exists():
    assert (nested/'runtime.json').read_bytes()==(runtime/'runtime.json').read_bytes(),'different runtime already installed'
else:shutil.copytree(runtime,nested)
run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/F/client-ui'/run
out.mkdir(parents=True);print(out,flush=True)
report={'status':'failed','sources':sources(),'scope':__doc__,
        'show_startup_diagnostic':args.show_startup_diagnostic,
        'native_ui':args.native_ui,
        'client_build_sha256':hashlib.sha256((client/'client-build.json').read_bytes()).hexdigest(),
        'runtime_manifest_sha256':hashlib.sha256((runtime/'runtime.json').read_bytes()).hexdigest()}
try:
    packages(out)
    if args.package:
        import zipfile
        package=args.package.resolve();shutil.copyfile(package,out/'healthy.spatial')
        with zipfile.ZipFile(package) as archive:report['package_build_id']=json.loads(archive.read('manifest.json'))['build_id']
        report['package_sha256']=hashlib.sha256(package.read_bytes()).hexdigest()
    shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
    shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'Checks.exe.config')
    names=['Supervisor/Tests/ContentClientUiChecks.cs','Supervisor/Tests/ContentManagerNativeChecks.cs',
        'Prototype/Assets/ContentRuntime/Runtime/ContentHostClient.cs',
        'Prototype/Assets/Tracking/Core/TrackingWire.cs','Prototype/Assets/Tracking/Core/RuntimeLease.cs',
        'Prototype/Assets/Desktop/Core/DesktopManagerLease.cs',
        'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows/PinnedProcessOwner.cs']
    command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),
        '-nologo','-langversion:latest',*references(),'-r:'+str(FRAMEWORK/'System.Drawing.dll'),'-r:'+str(out/'Newtonsoft.Json.dll'),
        '-out:'+str(out/'Checks.exe'),*[str(ROOT/name) for name in names]]
    built=subprocess.run(command,capture_output=True,text=True)
    (out/'compile.log').write_text(built.stdout+built.stderr);built.check_returncode()
    report['process']={}
    with (out/'checks.log').open('w') as log:
        run_toolchain([out/'Checks.exe',out,client,ROOT/'Validation/Sdk'/('gui'+run[:10]),out/'healthy.spatial',str(args.show_startup_diagnostic),str(args.native_ui),str(args.manual_focus),str(args.audio_picker)],
                      ROOT,log,180,report['process'])
    report['result']=json.loads((out/'result.json').read_text())
    if args.package:assert report['result']['first_receipt']['build']==report['package_build_id'],'client-presented-different-package'
    assert report['result']['all_retained_kernels_exited']
    report['status']='diagnostic-only-manager-lifecycle-with-forced-startup-show' if args.show_startup_diagnostic else 'passed-development-original-manager-tray-lifecycle'
finally:
    report['sources_unchanged']=report['sources']==sources()
    (out/'report.json').write_text(json.dumps(report,indent=2));print(out,flush=True)
