"""Actual restricted Validator and Host cleanup exit contract; controlled cleanup throw."""
from pathlib import Path
import argparse,subprocess,uuid,shutil,json,hashlib,sys
from check_contracts import ROOT,DATA
from provenance import sources
from windows_framework import references
from trusted_client_sources import client_sources
sys.path.insert(0,str(ROOT/'Supervisor/Tests'))
from run_production import packages,require_production_build
p=argparse.ArgumentParser();p.add_argument('--worker',type=Path,required=True);p.add_argument('--case',choices=['validator-cleanup','ordinary-rejection','contract-image-mismatch','worker-cleanup','worker-constructor','audio-initialize-cleanup','audio-initialize-rejection'],required=True);args=p.parse_args();worker=args.worker.resolve();require_production_build(json.loads((worker/'report.json').read_text()))
out=ROOT/'Validation/Content/sdk-runtime/F/host-cleanup-contract'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True);report={'status':'failed','case':args.case,'sources':sources(),'scope':__doc__}
try:
 packages(out);(out/'invalid.spatial').write_bytes(b'controlled invalid archive');runtime=out/'runtime';command=[sys.executable,str(ROOT/'tools/sdk/build_supervisor.py'),'--output',str(runtime/'supervisor')]
 if args.case=='validator-cleanup':
  source=(ROOT/'Supervisor/Hosting/ValidatorLauncher.cs').read_text();needle='workspace.Dispose();';assert source.count(needle)==1;source=source.replace(needle,needle+'throw new IOException("controlled-validator-workspace-cleanup-failure");');override=out/'ValidatorLauncher.cs';override.write_text(source);command+=['--source-override',str(override)];report['mutated_source_sha256']=hashlib.sha256(override.read_bytes()).hexdigest()
 if args.case=='worker-cleanup':
  source=(ROOT/'Supervisor/Hosting/WindowsWorkerLauncher.cs').read_text();needle='workspace.Dispose();disposed=true;';assert source.count(needle)==1;source=source.replace(needle,needle+'throw new IOException("controlled-worker-dispose-cleanup-failure");');override=out/'WindowsWorkerLauncher.cs';override.write_text(source);command+=['--source-override',str(override)];report['mutated_source_sha256']=hashlib.sha256(override.read_bytes()).hexdigest()
 if args.case=='worker-constructor':
  source=(ROOT/'Supervisor/Hosting/WindowsWorkerLauncher.cs').read_text();needle='result=new WindowsManagedWorker(now,width,height,workspace,gpu);';assert source.count(needle)==1;source=source.replace(needle,'throw new IOException("controlled-worker-constructor-failure");');override=out/'WindowsWorkerLauncher.cs';override.write_text(source);command+=['--source-override',str(override)];report['mutated_source_sha256']=hashlib.sha256(override.read_bytes()).hexdigest()
 if args.case in ['audio-initialize-cleanup','audio-initialize-rejection']:
  source=(ROOT/'Supervisor/Windows/TrustedAudioProcess.cs').read_text();needle='image=new FileStream(exe,FileMode.Open,FileAccess.Read,FileShare.Read);';assert source.count(needle)==1;source=source.replace(needle,needle+'throw new IOException("controlled-audio-initialization-failure");');needle='image?.Dispose();';assert source.count(needle)==1;source=source.replace(needle,needle+('throw new IOException("controlled-audio-initialization-cleanup-failure");' if args.case=='audio-initialize-cleanup' else ''));override=out/'TrustedAudioProcess.cs';override.write_text(source);command+=['--source-override',str(override)];report['mutated_source_sha256']=hashlib.sha256(override.read_bytes()).hexdigest()
 subprocess.run(command,check=True);subprocess.run([sys.executable,str(ROOT/'tools/sdk/build_validator.py'),'--output',str(runtime/'validator')],check=True)
 if args.case=='contract-image-mismatch':shutil.copyfile(ROOT/'Validation/Content/sdk-runtime/F/host-cleanup-contract/6ffa74075abc4b42b9b4a1847cf342dd/runtime/supervisor/Supervisor.exe',out/'old-host.exe')
 shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll');shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'Checks.exe.config')
 files=[ROOT/'Supervisor/Tests/HostCleanupChecks.cs']+client_sources(ROOT)
 with (out/'compile.log').open('w') as log:subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:System.Drawing.dll','-r:System.Windows.Forms.dll','-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)],stdout=log,stderr=subprocess.STDOUT,check=True)
 done=subprocess.run([str(out/'Checks.exe'),str(out),str(runtime),str(worker),str(ROOT/'Validation/Sdk'/('c'+out.name[:10])),args.case],capture_output=True,text=True,timeout=60);(out/'checks.log').write_text(done.stdout+done.stderr);report['exit_code']=done.returncode;done.check_returncode();assert sources()==report['sources'];report['status']='passed-development-host-cleanup-contract'
finally:
 report['sources_unchanged']=sources()==report['sources'];(out/'report.json').write_text(json.dumps(report,indent=2));print(out)
