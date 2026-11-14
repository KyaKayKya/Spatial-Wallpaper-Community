"""Actual owned native child surface; not a substitute for product/desktop acceptance."""
from pathlib import Path
import argparse,subprocess,uuid,json,hashlib,shutil,sys
from check_contracts import ROOT,DATA
from provenance import sources
from windows_framework import references
from trusted_client_sources import client_sources
from toolchain_process import run_toolchain
sys.path.insert(0,str(ROOT/'Supervisor/Tests'))
from run_production import packages,require_production_build
p=argparse.ArgumentParser();p.add_argument('--worker',type=Path,required=True);p.add_argument('--case',choices=['normal','held-first-frame','stalled-projection','held-initial-eye','projection-fields','unproven-resize-cleanup','held-recovery-eye','held-unload','held-first-frame-response'],default='normal');args=p.parse_args();worker=args.worker.resolve();require_production_build(json.loads((worker/'report.json').read_text()))
out=ROOT/'Validation/Content/sdk-runtime/F/content-surface'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True);report={'status':'failed','case':args.case,'sources':sources(),'scope':__doc__,'fixture_visibility':'owned topmost-noactivate test parent; frame submission and lifecycle checks, not runtime screen validation'}
try:
 ids=packages(out);runtime=out/'runtime'
 command=[sys.executable,str(ROOT/'tools/sdk/build_preview.py'),'--output',str(runtime)]
 if args.case=='held-first-frame':
  source=(ROOT/'Supervisor/Client/ContentSurfaceProgram.cs').read_text();needle='await presentation.FirstFrameAsync(';start=source.index(needle);end=source.index(';',start);source=source[:start]+source[start:end].replace(needle,'await Task.Delay(Timeout.Infinite,')+source[end:];source=source.replace('request.CancelAfter(30000);','request.CancelAfter(command=="FirstFrame"?300:30000);');override=out/'ContentSurfaceProgram.cs';override.write_text(source);command+=['--source-override',str(override)];report['mutated_source_sha256']=hashlib.sha256(override.read_bytes()).hexdigest()
 if args.case=='stalled-projection':
  source=(ROOT/'Supervisor/Client/ContentSurfaceProgram.cs').read_text();needle='await presentation.SetEyeAsync(';assert source.count(needle)==1;source=source.replace(needle,'if(presentation.LastReceipt!=null)return new JObject{{"projection_revision",presentation.RequestedProjectionRevision+1}};'+needle);override=out/'ContentSurfaceProgram.cs';override.write_text(source);command+=['--source-override',str(override)];report['mutated_source_sha256']=hashlib.sha256(override.read_bytes()).hexdigest()
 if args.case=='held-initial-eye':
  source=(ROOT/'Supervisor/Client/ContentSurfaceProgram.cs').read_text();needle='return new JObject{{"projection_revision",presentation.RequestedProjectionRevision}};';assert source.count(needle)==1;source=source.replace(needle,'if(presentation.LastReceipt==null){File.WriteAllText(Path.Combine(workspace,"initial-eye-pending"),"pending");while(!File.Exists(Path.Combine(workspace,"release-initial-eye")))await Task.Delay(10,requestToken);}'+needle);override=out/'ContentSurfaceProgram.cs';override.write_text(source);command+=['--source-override',str(override)];report['mutated_source_sha256']=hashlib.sha256(override.read_bytes()).hexdigest()
 if args.case=='unproven-resize-cleanup':
  source=(ROOT/'Supervisor/Client/ContentSurfaceProgram.cs').read_text();needle='{"host_resources_released",host==null||host.ResourcesReleased}';assert source.count(needle)==1;source=source.replace(needle,'{"host_resources_released",error=="content-surface-parent-identity-or-size"?false:host==null||host.ResourcesReleased}');override=out/'ContentSurfaceProgram.cs';override.write_text(source);command+=['--source-override',str(override)];report['mutated_source_sha256']=hashlib.sha256(override.read_bytes()).hexdigest()
 if args.case=='held-recovery-eye':
  source=(ROOT/'Supervisor/Client/ContentSurfaceProgram.cs').read_text();needle='return new JObject{{"projection_revision",presentation.RequestedProjectionRevision}};';assert source.count(needle)==1;source=source.replace(needle,'if(ClientSize.Width==800){File.WriteAllText(Path.Combine(workspace,"recovery-eye-pending"),"pending");await Task.Delay(Timeout.Infinite,requestToken);}'+needle);override=out/'ContentSurfaceProgram.cs';override.write_text(source);command+=['--source-override',str(override)];report['mutated_source_sha256']=hashlib.sha256(override.read_bytes()).hexdigest()
 if args.case=='held-unload':
  source=(ROOT/'Supervisor/Client/ContentSurfaceProgram.cs').read_text();needle='case "Unload":RuntimeDistribution.Fields(payload);';assert source.count(needle)==1;source=source.replace(needle,needle+'if(presentation.LastReceipt!=null&&!presentation.RuntimeResourcesReleased){File.WriteAllText(Path.Combine(workspace,"unload-pending"),"pending");while(!File.Exists(Path.Combine(workspace,"release-unload")))await Task.Delay(10,requestToken);}');override=out/'ContentSurfaceProgram.cs';override.write_text(source);command+=['--source-override',str(override)];report['mutated_source_sha256']=hashlib.sha256(override.read_bytes()).hexdigest()
 if args.case=='held-first-frame-response':
  source=(ROOT/'Supervisor/Client/ContentSurfaceProgram.cs').read_text();needle='await presentation.FirstFrameAsync(requestToken);';assert source.count(needle)==1;source=source.replace(needle,needle+'File.WriteAllText(Path.Combine(workspace,"first-frame-response-pending"),"actual RGB matched before held response");while(!File.Exists(Path.Combine(workspace,"release-first-frame-response")))await Task.Delay(10,requestToken);');override=out/'ContentSurfaceProgram.cs';override.write_text(source);command+=['--source-override',str(override)];report['mutated_source_sha256']=hashlib.sha256(override.read_bytes()).hexdigest()
 subprocess.run(command,check=True)
 for name in ['supervisor','validator']:subprocess.run([sys.executable,str(ROOT/f'tools/sdk/build_{name}.py'),'--output',str(runtime/name)],check=True)
 shutil.copytree(worker/'player',runtime/'worker/player');shutil.copyfile(worker/'report.json',runtime/'worker/report.json');identity=json.loads((runtime/'build.json').read_text())['identity'];inventory={f.relative_to(runtime).as_posix():hashlib.sha256(f.read_bytes()).hexdigest() for f in runtime.rglob('*') if f.is_file() and f.name!='runtime.json'};(runtime/'runtime.json').write_text(json.dumps(dict(format=1,kind='spatialwallpaper-runtime',stage='development',files=inventory,**identity)))
 shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll');shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'Checks.exe.config')
 files=[ROOT/'Supervisor/Tests/ContentSurfaceChecks.cs']+client_sources(ROOT)
 subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:System.Drawing.dll','-r:System.Windows.Forms.dll','-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)],check=True)
 report['process']={}
 with (out/'checks.log').open('w') as log:run_toolchain([str(out/'Checks.exe'),str(out),str(runtime),str(out/'healthy.spatial'),ids[0],str(ROOT/'Validation/Sdk'/('s'+out.name[:10])),args.case],ROOT,log,120,report['process'])
 report['result']=json.loads((out/'result.json').read_text());assert sources()==report['sources'];report['status']='passed-development-owned-native-content-surface'
finally:
 report['sources_unchanged']=sources()==report['sources'];report['files']={f.relative_to(out).as_posix():hashlib.sha256(f.read_bytes()).hexdigest() for f in out.rglob('*') if f.is_file() and f.name not in ['endpoint.json','report.json'] and not f.is_relative_to(out/'runtime/worker/player')};(out/'report.json').write_text(json.dumps(report,indent=2));print(out)
