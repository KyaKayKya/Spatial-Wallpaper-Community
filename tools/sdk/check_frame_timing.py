"""Short real LPAC Worker FrameTimingManager wiring probe; never RC performance acceptance."""
from pathlib import Path
import argparse,base64,hashlib,json,shutil,subprocess,sys,uuid
from check_contracts import ROOT,DATA
from provenance import sources
from release_identity import identity
from windows_framework import references
from toolchain_process import run_toolchain
from frame_timings import TimingSeries,PREFIX,POLICY
from sdk_environment import configuration_identity
sys.path.insert(0,str(ROOT/'Supervisor/Tests'));from run_production import packages,require_production_build

p=argparse.ArgumentParser(description=__doc__);p.add_argument('--worker',type=Path,required=True);p.add_argument('--supervisor',type=Path,required=True);p.add_argument('--validator',type=Path,required=True);p.add_argument('--fps',type=int,choices=[30,60],default=60);p.add_argument('--diagnostic-worker',action='store_true');p.add_argument('--rc-performance',action='store_true');p.add_argument('--width',type=int,default=640);p.add_argument('--height',type=int,default=360);p.add_argument('--repetition',type=int,default=1);args=p.parse_args()
if args.rc_performance and (args.diagnostic_worker or [args.width,args.height] not in POLICY['resolutions'] or not 1<=args.repetition<=POLICY['repetitions']):p.error('RC sampling requires a frozen resolution, repetition and production Worker')
if not args.rc_performance and (args.width,args.height)!=(640,360):p.error('development wiring probe remains 640x360')
width,height=args.width,args.height;warmup=POLICY['warmup_seconds'] if args.rc_performance else 0;sample_seconds=POLICY['sample_seconds'] if args.rc_performance else 10
out=ROOT/'Validation/Content/sdk-runtime/G/frame-timing'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
report={'status':'failed','sources':sources(),'scope':__doc__,'rc_performance_sampling':args.rc_performance,'warmup_seconds':warmup,'sample_seconds':sample_seconds,'target_fps':args.fps,'width':width,'height':height,'repetition':args.repetition,'configuration_before':configuration_identity()}
try:
 worker=args.worker.resolve();build=json.loads((worker/'report.json').read_text())
 report['worker_build']={'path':str(worker),'receipt_sha256':hashlib.sha256((worker/'report.json').read_bytes()).hexdigest()}
 if args.diagnostic_worker:
  assert worker.is_relative_to(ROOT/'Validation/Content/sdk-runtime/G/frame-timing-worker') and build.get('release_candidate') is False and set(build.get('diagnostic_mutations',{}))=={'Worker_Data/boot.config'},'only owned boot-config diagnostic Worker allowed'
  report['diagnostic_mutations']=build['diagnostic_mutations']
 else:require_production_build(build)
 expected=identity();expected.pop('package_sha256');assert build['identity']==expected and build['worker_types']['SpatialWallpaper.Sdk.dll']['identity']==expected
 assert build['runtime_files']=={f.relative_to(worker/'player').as_posix():hashlib.sha256(f.read_bytes()).hexdigest() for f in (worker/'player').rglob('*') if f.is_file()},'Worker runtime inventory changed'
 ids=packages(out);config={'image':str(worker/'player'),'inventory':str(worker/'report.json'),'validator':str(args.validator.resolve()),'state':str(out/'state'),'attempts':str(ROOT/'Validation/Sdk'/('ft'+out.name[:8])),'width':width,'height':height,'timing_fps':args.fps};(out/'host-config.json').write_text(json.dumps(config))
 shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll');shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'Checks.exe.config')
 names=['Supervisor/Tests/FrameTimingChecks.cs','Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows/PinnedProcessOwner.cs'];done=subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*[str(ROOT/name) for name in names]],capture_output=True,text=True);(out/'compile.log').write_text(done.stdout+done.stderr);done.check_returncode();report['process']={}
 with (out/'checks.log').open('w') as log:run_toolchain([out/'Checks.exe',out,args.supervisor.resolve()/'Supervisor.exe',out/'host-config.json',out/'healthy.spatial',ids[0],str(sample_seconds*1000),str(warmup*1000)],ROOT,log,warmup+sample_seconds+90,report['process'])
 result=json.loads((out/'result.json').read_text());context={k:v for k,v in result['identity'].items() if k!='host'};context.update(build=ids[0],width=width,height=height,target_fps=args.fps);series=TimingSeries(context);profile=None
 assert result['sample_started_at_ms']>=warmup*1000 and result['sample_ended_at_ms']-result['sample_started_at_ms']>=sample_seconds*1000,'monotonic sample window incomplete'
 stop=result['terminal']['result']['stop_evidence'];assert stop['graceful_stop'] and not stop['forced_stop'] and stop['normal_stop_to_kernel_exit_ms']<=500,'normal Worker stop failed frozen grace'
 for line in (out/'timing-snapshots.jsonl').read_text().splitlines():
  text=base64.b64decode(json.loads(line)['worker_log_base64'],validate=True).decode('utf-8',errors='replace');series.read(text)
  for raw in text.splitlines():
   if raw.startswith('SDK_RENDER_PROFILE ') and raw.rstrip().endswith('}'):profile=json.loads(raw[len('SDK_RENDER_PROFILE '):])
 assert series.rows,'real Worker returned no FrameTimingManager rows'
 summary=series.summary();report['all_rows_measurement']=summary
 (out/'timing-raw-rows.json').write_text(json.dumps({'identity':context,'cpu_timer_frequency':series.frequency,'rows':list(series.rows.values())},indent=2))
 # The real fixed-engine probe exposed an invalid reported clock frequency,
 # zero GPU/render-thread samples and no association with offscreen submissions.
 # Do not carve a purported timed window out of that clock or invent a grade.
 report.update(result=result,measurement=summary,render_profile=profile,
               sample_window_status='Frame timing is unmeasured; raw thread statistics include warmup. Host/proxy CPU and memory cover the full monotonic sample window.',host_cpu_average_logical_cores=result['host_cpu_seconds']/result['observed_wall_seconds'],measurement_client_cpu_average_logical_cores=result['measurement_client_cpu_seconds']/result['observed_wall_seconds'])
 assert profile and all(profile[k]==result['identity'][k] for k in ['attempt','instance','generation']),'Worker render profile session mismatch'
 assert report['sources']==sources(),'frame timing probe source drift';report['status']='completed-RC-performance-sampling-results-unmeasured' if args.rc_performance else 'passed-development-real-worker-diagnostics-performance-unmeasured'
finally:
 report['configuration_after']=configuration_identity()
 if report['configuration_after']!=report['configuration_before']:report['status']='failed-configuration-drift'
 report['sources_unchanged']=report['sources']==sources();(out/'report.json').write_text(json.dumps(report,indent=2));print(out,flush=True)
