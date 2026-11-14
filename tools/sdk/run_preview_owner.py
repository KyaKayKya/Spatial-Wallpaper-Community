"""Actual preview owner exit must close its private Host and LPAC Worker."""
from pathlib import Path
import argparse,subprocess,uuid,json,hashlib,shutil,sys
from check_contracts import ROOT,DATA
from provenance import sources
from windows_framework import references
from toolchain_process import run_toolchain
sys.path.insert(0,str(ROOT/'Supervisor/Tests'))
from run_production import packages,require_production_build
parser=argparse.ArgumentParser();parser.add_argument('--worker',type=Path,required=True);parser.add_argument('--wrong-owner',action='store_true');parser.add_argument('--crash-owner',action='store_true');parser.add_argument('--lease',action='store_true');args=parser.parse_args();assert sum([args.wrong_owner,args.crash_owner,args.lease])<=1;worker=args.worker.resolve();wr=json.loads((worker/'report.json').read_text());require_production_build(wr)
out=ROOT/'Validation/Content/sdk-runtime/F/preview-owner'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True);report={'status':'failed','sources':sources(),'wrong_owner':args.wrong_owner,'crash_owner':args.crash_owner,'lease':args.lease,'scope':'actual private preview Host ownership, retained owner/Host/LPAC Worker kernel identities; not Editor or product lifecycle'}
try:
 ids=packages(out)
 for name in ['supervisor','validator']:subprocess.run([sys.executable,str(ROOT/f'tools/sdk/build_{name}.py'),'--output',str(out/name)],check=True)
 shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll');shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'Checks.exe.config')
 files=[ROOT/('Supervisor/Tests/PreviewLeaseChecks.cs' if args.lease else 'Supervisor/Tests/PreviewOwnerChecks.cs'),*[ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting'/n for n in ['RuntimeSession.cs','RuntimePackageInspection.cs','WorkerFrameCodec.cs','RuntimeLimits.Generated.cs']],*[ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows'/n for n in ['PinnedProcessOwner.cs','SupervisorManagerConnection.cs','SupervisorSessionClient.cs','TrustedRuntimeDistribution.cs','HostExitContract.cs']]]
 if args.lease:files.append(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows/PreviewHostLease.cs')
 subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)],check=True)
 (out/'base-config.json').write_text(json.dumps({'image':str(worker/'player'),'inventory':str(worker/'report.json'),'validator':str(out/'validator'),'state':str(out/'state'),'attempts':str(ROOT/'Validation/Sdk'/('p'+out.name[:10])),'width':640,'height':360}))
 report['process']={}
 with (out/'checks.log').open('w') as log:run_toolchain([str(out/'Checks.exe'),str(out),str(out/'supervisor/Supervisor.exe'),str(out/'healthy.spatial'),ids[0],'wrong-owner' if args.wrong_owner else 'crash-owner' if args.crash_owner else 'owner'],ROOT,log,90,report['process'])
 report['result']=json.loads((out/('wrong-owner-rejected.json' if args.wrong_owner else 'result.json')).read_text());assert report['sources']==sources();report['status']='passed-owned-preview-host-bootstrap' if args.lease else 'passed-rejected-wrong-preview-owner' if args.wrong_owner else 'passed-preview-owner-exit-cleans-private-host-and-worker'
finally:
 report['sources_unchanged']=report['sources']==sources();report['files']={p.relative_to(out).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in out.rglob('*') if p.is_file() and p.name not in ['report.json','endpoint.json']};(out/'report.json').write_text(json.dumps(report,indent=2));print(out)
