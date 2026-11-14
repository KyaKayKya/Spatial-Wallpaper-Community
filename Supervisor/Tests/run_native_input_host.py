"""Native preview window -> authenticated independent Host -> real LPAC author."""
from pathlib import Path
import argparse,sys,subprocess,uuid,shutil,json,hashlib,time,platform
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from provenance import sources
from windows_framework import references,FRAMEWORK
from native_exit_trace import capture_exit_trace
parser=argparse.ArgumentParser();parser.add_argument('--worker',type=Path,required=True);parser.add_argument('--author',type=Path,required=True);parser.add_argument('--negative-no-adapter',action='store_true');parser.add_argument('--negative-cleanup-fault',action='store_true');parser.add_argument('--compile-only',action='store_true');parser.add_argument('--trace-self-test',action='store_true');parser.add_argument('--negative-trace-sleep',action='store_true');parser.add_argument('--soak-seconds',type=int,default=0);parser.add_argument('--desktop',action='store_true');parser.add_argument('--background-injection',action='store_true');parser.add_argument('--diagnostic-worker',action='store_true');parser.add_argument('--injector-self-test',action='store_true');parser.add_argument('--pending-interruption',choices=['move','wheel']);parser.add_argument('--automatic-recovery',action='store_true');parser.add_argument('--automatic-boundary',choices=['mouse','key','cleanup','timeout-active','timeout-pending','heartbeat-exit']);parser.add_argument('--recover-helper',action='store_true');parser.add_argument('--recover-helper-active',action='store_true');parser.add_argument('--inflight-reply',choices=['control','move','wheel']);parser.add_argument('--negative-inflight-omit-revoke',action='store_true');parser.add_argument('--queued-notification',choices=['control','revoke']);parser.add_argument('--negative-queued-ignore-token',action='store_true');parser.add_argument('--window-destruction',choices=['ready','mouse','close','connecting']);parser.add_argument('--thread-sampling',action='store_true');parser.add_argument("--expired-activation",action="store_true");parser.add_argument("--expiry-after-validation",action="store_true");parser.add_argument('--heartbeat-expiry',action='store_true');parser.add_argument('--heartbeat-lease-expiry',action='store_true');parser.add_argument('--negative-heartbeat-stale',action='store_true');parser.add_argument('--native-evidence-expiry',choices=['focus','publish']);parser.add_argument('--continuous-motion',action='store_true');parser.add_argument('--continuous-unmapped',action='store_true');parser.add_argument('--interaction-mixed-motion',action='store_true');parser.add_argument('--interaction-drop-release',action='store_true');parser.add_argument('--interaction-seconds',type=int,default=0);args=parser.parse_args();worker=args.worker.resolve();author=args.author.resolve()
from input_continuity_inputs import validate_modes,validate_inputs
validate_modes(args)
assert not args.interaction_drop_release or 0<args.interaction_seconds<1800
if args.interaction_seconds:
 assert 1<=args.interaction_seconds<=1800 and args.desktop and args.background_injection and not args.automatic_recovery and not args.soak_seconds
 assert not any([args.continuous_unmapped,args.continuous_motion,args.pending_interruption,args.inflight_reply,args.queued_notification,args.recover_helper,args.negative_no_adapter,args.negative_cleanup_fault,args.native_evidence_expiry,args.expired_activation,args.heartbeat_expiry,args.window_destruction])
assert not args.continuous_unmapped or args.automatic_recovery and not args.continuous_motion and not args.automatic_boundary and not args.heartbeat_expiry and not args.window_destruction
assert not args.continuous_motion or args.automatic_recovery and not args.automatic_boundary and not args.expired_activation and not args.heartbeat_expiry and not args.native_evidence_expiry and not args.window_destruction
assert not args.native_evidence_expiry or args.automatic_recovery and not args.automatic_boundary and not args.expired_activation and not args.heartbeat_expiry and not args.window_destruction
assert not args.heartbeat_lease_expiry or args.heartbeat_expiry
assert not args.negative_heartbeat_stale or args.heartbeat_expiry and not args.heartbeat_lease_expiry
assert not args.heartbeat_expiry or args.automatic_recovery and not args.automatic_boundary and not args.expired_activation and not args.window_destruction
assert not args.expiry_after_validation or args.expired_activation
assert not args.expired_activation or args.automatic_recovery and not args.automatic_boundary and not args.window_destruction,'expiry probe requires standalone automatic session'
assert not args.window_destruction or args.automatic_recovery and not args.automatic_boundary and not args.inflight_reply and not args.queued_notification and not args.pending_interruption,'window destruction requires standalone automatic session'
assert not args.negative_queued_ignore_token or args.queued_notification=='revoke','ignored token negative requires revoke mode'
assert not args.queued_notification or args.desktop and args.background_injection and not args.inflight_reply and not args.automatic_recovery and not args.recover_helper and not args.pending_interruption and not args.negative_no_adapter and not args.negative_cleanup_fault and not args.compile_only,'queued notification requires standalone desktop fixture'
assert not args.negative_inflight_omit_revoke or args.inflight_reply in ['wheel','move'] and not args.compile_only,'omitted revoke negative requires real interruption mode'
assert not args.inflight_reply or args.desktop and args.background_injection and not args.automatic_recovery and not args.recover_helper and not args.pending_interruption and not args.negative_no_adapter and not args.negative_cleanup_fault,'inflight reply requires standalone desktop fixture'
assert not args.automatic_boundary or args.automatic_recovery,'boundary requires automatic recovery'
assert not args.automatic_recovery or args.desktop and args.background_injection and not args.recover_helper and not args.negative_no_adapter and not args.negative_cleanup_fault and not args.compile_only,'automatic recovery requires standalone desktop mode'
assert not args.recover_helper_active or args.recover_helper,'active recovery requires helper recovery'
assert not args.recover_helper or args.desktop and args.background_injection and not args.negative_no_adapter and not args.negative_cleanup_fault and not args.compile_only,'recovery requires real desktop adapter'
assert not args.pending_interruption or args.desktop and args.background_injection and not args.negative_no_adapter and not args.negative_cleanup_fault and not args.compile_only,'pending negative requires real desktop adapter'
assert not args.background_injection or args.desktop,'background injection requires desktop fixture'
assert not args.negative_trace_sleep or args.trace_self_test,'negative sleep requires trace self-test'
assert not args.trace_self_test or args.desktop and not args.compile_only and not args.negative_no_adapter and not args.negative_cleanup_fault,'trace self-test requires desktop and cannot combine with runtime modes'
assert not args.negative_cleanup_fault or args.desktop and not args.negative_no_adapter,'cleanup fault requires desktop adapter'
policy=json.loads((ROOT/'tools/sdk/release-policy.json').read_text());assert 0<=args.soak_seconds<=policy['input']['idle_seconds']
assert not args.thread_sampling or args.desktop and args.soak_seconds>0
out=ROOT/'Validation/Content/sdk-runtime/E'/('native-desktop-input-host' if args.desktop else 'native-input-host')/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources();print(out,flush=True)
wr=json.loads((worker/'report.json').read_text());ar=json.loads((author/'report.json').read_text());assert hashlib.sha256((author/'scene.spatial').read_bytes()).hexdigest()==ar['package_sha256']
continuity_inputs=validate_inputs(worker,author,receipt) if args.interaction_seconds else None
assert {p.relative_to(worker/'player').as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in (worker/'player').rglob('*') if p.is_file()}==wr['runtime_files']
assert args.diagnostic_worker == (wr.get('release_candidate') is False),'diagnostic Worker must be explicitly labeled'
for name in ['supervisor','validator']:
 command=[sys.executable,str(ROOT/'tools/sdk/build_input_diagnostic_host.py'),str(out/name)] if name=='supervisor' and args.diagnostic_worker else [sys.executable,str(ROOT/f'tools/sdk/build_{name}.py'),'--output',str(out/name)]
 subprocess.run(command,check=True)
shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
from native_input_client_sources import native_input_client_sources
files=native_input_client_sources(ROOT,desktop=args.desktop)
if args.desktop:
 sys.path.insert(0,str(ROOT/'tools/sdk/experiments'))
 from native_build import compile_probe
 compile_probe(ROOT/'Supervisor/Interactive/DesktopHitTester.cpp',out/'DesktopHitTester.exe')
if args.queued_notification:
 original=ROOT/'Supervisor/Interactive/WindowsDesktopInput.cs';copy=out/'ExperimentalQueuedNotification.cs';text=original.read_text()
 declaration='  AcquisitionWakeup wakeup;'
 assert text.count(declaration)==1
 text=text.replace(declaration,'  int acceptanceAccepted,acceptanceRejected,acceptanceDecisionCount;long acceptanceDecisionAt,acceptanceOwner,acceptanceRequest,acceptanceWindow;'+declaration)
 needle='if(message==QueryReady){if(!closed&&wakeup!=null&&wakeup.Matches(w.ToInt64(),l.ToInt64()))PostOwner(Acquire);}'
 assert text.count(needle)==1
 condition='true' if args.negative_queued_ignore_token else 'wakeup.Matches(w.ToInt64(),l.ToInt64())'
 replacement='if(message==QueryReady){bool accepted=!closed&&wakeup!=null&&('+condition+');acceptanceDecisionAt=WindowsMachineClock.Milliseconds();acceptanceOwner=w.ToInt64();acceptanceRequest=l.ToInt64();acceptanceWindow=hwnd.ToInt64();acceptanceDecisionCount++;if(accepted){acceptanceAccepted++;PostOwner(Acquire);}else acceptanceRejected++;}'
 copy.write_text(text.replace(needle,replacement));files=[copy if path==original else path for path in files]

if args.negative_inflight_omit_revoke:
 original=ROOT/'Supervisor/Interactive/WindowsDesktopInput.cs';copy=out/'ExperimentalOmittedRevoke.cs';text=original.read_text()
 needle='Revoke(wheel?"pending-wheel":"pending-pointer-left-click");'
 assert text.count(needle)==1
 copy.write_text(text.replace(needle,'{ /* Deliberately omitted in acceptance negative only. */ }'))
 files=[copy if path==original else path for path in files]

if args.native_evidence_expiry:
 original=ROOT/'Supervisor/Interactive/WindowsDesktopInput.cs';copy=out/'ExperimentalNativeEvidenceExpiry.cs';text=original.read_text()
 needle='InputHeartbeat<HeartbeatSample> heartbeat;';assert text.count(needle)==1
 declaration='volatile int acceptanceEvidenceArm;int acceptanceEvidenceHolds,acceptanceEvidenceCalls;long acceptanceEvidenceHitTime,acceptanceEvidenceAge;void AcceptanceEvidenceDelay(DesktopHitResult hit){if(System.Threading.Interlocked.Exchange(ref acceptanceEvidenceArm,0)!=1)return;acceptanceEvidenceHitTime=hit.ObservedAt;System.Threading.Thread.Sleep(80);acceptanceEvidenceAge=WindowsMachineClock.Milliseconds()-hit.ObservedAt;acceptanceEvidenceHolds++;}'
 text=text.replace(needle,declaration+needle)
 if args.native_evidence_expiry=='focus':
  needle='if(!fence.TryCommit(request.Fence,()=>';hit='request.Hit';publish='target.AcquireFocus(new InputObservation'
 else:
  needle='bool sent=false;if(!fence.TryCommit(token,';hit='hit';publish='target.Observe(new InputObservation(Next(),at'
 assert text.count(needle)==1 and text.count(publish)==1
 text=text.replace(needle,'AcceptanceEvidenceDelay('+hit+');'+needle)
 text=text.replace(publish,'if(acceptanceEvidenceHolds>0&&'+hit+'.ObservedAt==acceptanceEvidenceHitTime)acceptanceEvidenceCalls++;'+publish)
 copy.write_text(text);files=[copy if path==original else path for path in files]


if args.heartbeat_expiry:
 original=ROOT/'Supervisor/Interactive/WindowsDesktopInput.cs';copy=out/'ExperimentalHeartbeatExpiry.cs';text=original.read_text()
 needle='InputHeartbeat<HeartbeatSample> heartbeat;'
 assert text.count(needle)==1
 text=text.replace(needle,'volatile int acceptanceHeartbeatArm;int acceptanceHeartbeatHolds,acceptanceHeartbeatStalePublishes;long acceptanceHeartbeatHitTime,acceptanceHeartbeatSequence,acceptanceHeartbeatAge;'+needle)
 needle='bool owns=Owns();heartbeatOwnedQpc='
 assert text.count(needle)==1
 text=text.replace(needle,'bool owns=Owns();if(owns&&acceptanceHeartbeatArm!=0){if(acceptanceHeartbeatArm==1)acceptanceHeartbeatArm=0;acceptanceHeartbeatHitTime=sample.Hit.ObservedAt;acceptanceHeartbeatSequence=sequence;System.Threading.Thread.Sleep(80);acceptanceHeartbeatAge=WindowsMachineClock.Milliseconds()-sample.Hit.ObservedAt;acceptanceHeartbeatHolds++;}heartbeatOwnedQpc=')
 needle='target.Observe(new InputObservation(Next(),sample.Time'
 assert text.count(needle)==1
 text=text.replace(needle,'if(acceptanceHeartbeatHolds>0&&sample.Hit.ObservedAt==acceptanceHeartbeatHitTime)acceptanceHeartbeatStalePublishes++;'+needle)
 if args.negative_heartbeat_stale:
  needle='!sample.Hit.IsFresh(now)?"hit-age":null';assert text.count(needle)==1;text=text.replace(needle,'false?"hit-age":null')
 copy.write_text(text);files=[copy if path==original else path for path in files]

if args.expired_activation:
 original=ROOT/'Supervisor/Interactive/WindowsDesktopInput.cs';copy=out/'ExperimentalActivationTracing.cs';text=original.read_text()
 assert text.count('SetForegroundWindow(window)')==1 and text.count('SetFocus(window)')==1
 assert text.count('ShellOrReceiver(request.Hit.ShellProcess)')==1
 copy.write_text(text.replace('SetForegroundWindow(window)','NativeDesktopInputHostChecks.ActivationSetForeground(window)').replace('SetFocus(window)','NativeDesktopInputHostChecks.ActivationSetFocus(window)').replace('ShellOrReceiver(request.Hit.ShellProcess)','NativeDesktopInputHostChecks.ActivationAfterValidation(ShellOrReceiver(request.Hit.ShellProcess))'))
 files=[copy if path==original else path for path in files]

subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(FRAMEWORK/'Microsoft.CSharp.dll'),'-r:'+str(FRAMEWORK/'System.Windows.Forms.dll'),'-r:'+str(FRAMEWORK/'System.Drawing.dll'),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Client.exe'),*map(str,files)],check=True)
state=out/'state';config=out/'config.json';config.write_text(json.dumps({'image':str(worker/'player'),'inventory':str(worker/'report.json'),'validator':str(out/'validator'),'state':str(state),'attempts':str(ROOT/'Validation/Sdk'/('i'+out.name[:10])),'width':640,'height':360}));host=None;host_creation=None
report={'status':'not-complete','interaction_mixed_motion':args.interaction_mixed_motion,'interaction_seconds':args.interaction_seconds,'interaction_drop_release':args.interaction_drop_release,'continuous_unmapped':args.continuous_unmapped,'continuous_motion':args.continuous_motion,'native_evidence_expiry':args.native_evidence_expiry,'heartbeat_expiry':args.heartbeat_expiry,'heartbeat_lease_expiry':args.heartbeat_lease_expiry,'negative_heartbeat_stale':args.negative_heartbeat_stale,'expired_activation_probe':args.expired_activation,'expiry_after_validation':args.expiry_after_validation,'diagnostic_only':args.diagnostic_worker,'pending_interruption':args.pending_interruption,'inflight_reply':args.inflight_reply,'queued_notification':args.queued_notification,'negative_queued_ignore_token':args.negative_queued_ignore_token,'negative_inflight_omit_revoke':args.negative_inflight_omit_revoke,'manual_helper_recovery':args.recover_helper,'automatic_recovery':args.automatic_recovery,'automatic_boundary':args.automatic_boundary,'window_destruction':args.window_destruction,'recovery_was_active':args.recover_helper_active,'background_injection':args.background_injection,'sources':receipt,'platform':platform.platform(),'worker_report_sha256':hashlib.sha256((worker/'report.json').read_bytes()).hexdigest(),'author_report_sha256':hashlib.sha256((author/'report.json').read_bytes()).hexdigest()}
report['client_sources']=[p.relative_to(ROOT).as_posix() for p in files]
if continuity_inputs is not None:report['continuity_inputs']=continuity_inputs
if args.injector_self_test:
 assert args.desktop and not args.diagnostic_worker
 result=subprocess.run([str(out/'Client.exe'),'--injector-self-test',str(out)],capture_output=True,text=True,timeout=20)
 log=result.stdout+result.stderr;(out/'injector-self-test.log').write_text(log,encoding='utf-8');print(log)
 report.update(status='partial-owned-injector-self-test' if result.returncode==0 else 'owned-injector-self-test-failed',exit_code=result.returncode,source_unchanged=sources()==receipt)
 (out/'report.json').write_text(json.dumps(report,indent=2));result.check_returncode();assert report['source_unchanged'];raise SystemExit(0)
if args.trace_self_test:
 result=subprocess.run([str(out/'Client.exe'),'--trace-self-test-sleep' if args.negative_trace_sleep else '--trace-self-test',str(out)],capture_output=True,text=True,timeout=10)
 (out/'trace-self-test.log').write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr)
 log_text=result.stdout+result.stderr
 negative_ok=args.negative_trace_sleep and result.returncode==1 and all('PASS '+name in log_text for name in ['unrelated-message-queued-before-wait','own-message-dispatched-before-sender-join','message-wait-sender-cleanup']) and 'System.Exception: message-arrival-wakes-wait-before-sleep-window' in log_text and 'PASS message-arrival-wakes-wait-before-sleep-window' not in log_text
 report['status']='expected-sleeping-pump-rejected' if negative_ok else 'partial-acquisition-trace-self-test' if result.returncode==0 and not args.negative_trace_sleep else 'trace-self-test-failed'
 report['exit_code']=result.returncode
 report['source_unchanged']=sources()==receipt
 (out/'report.json').write_text(json.dumps(report,indent=2))
 if args.negative_trace_sleep:assert negative_ok,'sleep negative must fail only at dispatch latency after own message was handled'
 else:result.check_returncode()
 assert report['source_unchanged'];raise SystemExit(0)
if args.compile_only:
 report['status']='compiled-not-runtime-verified';(out/'report.json').write_text(json.dumps(report,indent=2));print(out,flush=True);raise SystemExit(0)
try:
 with (out/'host.log').open('w') as log:
  host=subprocess.Popen([str(out/'supervisor/Supervisor.exe'),str(config)],stdout=log,stderr=subprocess.STDOUT);deadline=time.monotonic()+15
  from retained_process_identity import creation_filetime
  host_creation=creation_filetime(int(host._handle));report['host_creation_filetime']=host_creation
  while not (state/'endpoint.json').exists():
   assert host.poll() is None and time.monotonic()<deadline,'Host did not publish endpoint';time.sleep(.02)
  result=subprocess.run([str(out/'Client.exe'),str(out),str(author/'scene.spatial'),ar['author']['build'],str(host.pid),'--no-adapter' if args.negative_no_adapter else '--native',str(args.soak_seconds)]+([str(out/'DesktopHitTester.exe'),'--host-creation-filetime='+str(host_creation)] if args.desktop else [])+(['--expiry-after-validation'] if args.expiry_after_validation else [])+(['--heartbeat-lease-expiry'] if args.heartbeat_lease_expiry else [])+(['--native-evidence-'+args.native_evidence_expiry] if args.native_evidence_expiry else [])+(['--interaction-mixed-motion'] if args.interaction_mixed_motion else [])+(['--interaction-drop-release'] if args.interaction_drop_release else [])+(['--interaction-seconds='+str(args.interaction_seconds)] if args.interaction_seconds else [])+(['--continuous-unmapped'] if args.continuous_unmapped else [])+(['--continuous-motion'] if args.continuous_motion else [])+(['--heartbeat-expiry'] if args.heartbeat_expiry else [])+(['--expired-activation'] if args.expired_activation else [])+(['--thread-sampling'] if args.thread_sampling else [])+(['--cleanup-fault'] if args.negative_cleanup_fault else [])+(['--background-injection'] if args.background_injection else [])+(['--queued-'+args.queued_notification] if args.queued_notification else [])+(['--inflight-'+args.inflight_reply] if args.inflight_reply else [])+(['--pending-'+args.pending_interruption] if args.pending_interruption else [])+(['--automatic-recovery'] if args.automatic_recovery else [])+(['--window-'+args.window_destruction] if args.window_destruction else [])+(['--automatic-'+args.automatic_boundary] if args.automatic_boundary else [])+(['--recover-helper'] if args.recover_helper else [])+(['--recover-helper-active'] if args.recover_helper_active else []),capture_output=True,text=True,timeout=45+args.soak_seconds+(args.interaction_seconds+75 if args.interaction_seconds else 0));log_text=result.stdout+result.stderr;(out/'client.log').write_text(log_text,encoding='utf-8');print(log_text)
  if args.interaction_drop_release:
   observation=json.loads((out/'interaction-omission.json').read_text())
   assert observation['omitted_edge']==2 and observation['revision_before']==1 and observation['source_edges']==1 and observation['owed']==1
   assert result.returncode==1 and 'System.Exception: continuity-missing-author-edge' in log_text,'omitted release must fail at missing author ack'
   for marker in ['actual-source-cleanup-completed','actual-source-helper-kernel-exited','actual-input-channel-cleanup-completed','continuity-paired-release','heartbeat-helper-kernel-exited-']:
    assert 'PASS '+marker in log_text,'omission cleanup missing: '+marker
   assert (out/'input-obligations.bin').read_bytes()==b'\0'
   assert not (out/'interaction-continuity.json').exists() and not (out/'result.json').exists()
  elif args.negative_queued_ignore_token:
   observation=json.loads((out/'queued-notification.json').read_text())
   assert observation['mode']=='revoke' and observation['sent']==1 and observation['hook_observed'] is True and observation['revoked'] is True,'ignored token negative must first perform real revoke'
   required=['queued-exact-message-reached-native-dispatch-once','queued-dispatch-keeps-original-query-and-click-freshness','queued-real-wheel-revoked-before-dispatch','actual-source-cleanup-completed','actual-source-helper-kernel-exited','actual-source-retry-hooks-removed','actual-source-retry-registration-released','actual-input-channel-cleanup-completed']
   assert result.returncode!=0 and all('PASS '+name in log_text for name in required) and 'System.Exception: queued-revoked-message-rejected-by-token-gate' in log_text and 'PASS queued-revoked-message-rejected-by-token-gate' not in log_text and not (out/'result.json').exists(),'ignored token must fail exact decision assertion with fresh real query and native cleanup'
  elif args.negative_inflight_omit_revoke:
   observation=json.loads((out/'inflight-reply.json').read_text())
   assert observation['mode']==args.inflight_reply and observation['sent']==1 and observation['hook_observed'] is True and observation['interruption_observed'] is False,'omitted revoke negative requires successful native injection and observed hook'
   required=['inflight-real-reply-succeeds-with-original-freshness','actual-source-cleanup-completed','actual-source-helper-kernel-exited','actual-source-retry-hooks-removed','actual-source-retry-registration-released','actual-input-channel-cleanup-completed']
   assert result.returncode!=0 and all('PASS '+name in log_text for name in required) and 'System.Exception: inflight-native-interruption-observed-before-reply' in log_text and 'PASS inflight-native-interruption-observed-before-reply' not in log_text and not (out/'result.json').exists(),'omitted revoke must fail exact interruption assertion after fresh successful query and clean native shutdown'
  elif args.negative_no_adapter:
   assert result.returncode!=0 and ('PASS independent-desktop-worker-running' if args.desktop else 'PASS independent-preview-worker-running') in log_text and 'PASS native-input-delivered' in log_text and 'System.Exception: author-storage-revision-1' in log_text and 'PASS author-storage-revision-1' not in log_text and not (out/'result.json').exists(),'missing adapter negative did not detect absent author input'
  elif args.negative_cleanup_fault:
   assert result.returncode!=0 and all('PASS '+name in log_text for name in ['author-storage-revision-3','actual-source-cleanup-completed','actual-source-retry-hooks-removed','actual-source-retry-registration-released','actual-source-helper-kernel-exited-after-retry','actual-input-channel-cleanup-completed']) and 'System.ComponentModel.Win32Exception' in log_text and not (out/'result.json').exists(),'cleanup fault must invalidate an otherwise successful author run and release actual native resources'
  elif args.negative_heartbeat_stale:
   assert result.returncode==1 and 'System.Exception: heartbeat-expired-sample-never-published' in log_text,'stale heartbeat negative must fail at actual stale publication'
  else:result.check_returncode();assert host.wait(5)==0
  if args.interaction_seconds and not args.interaction_drop_release:
   from input_continuity_evidence import validate_folder
   proof=validate_folder(out,args.interaction_seconds,mixed=args.interaction_mixed_motion)
   report['interaction_proof']=proof
  if args.continuous_motion:
   observed=json.loads((out/'continuous-motion.json').read_text());assert observed['moves']>=20 and observed['elapsed_ms']>=1500 and observed['focused'] and not observed['failure'],'continuous motion fixture must run'
  if args.native_evidence_expiry:
   observed=json.loads((out/'native-evidence-expiry.json').read_text());assert observed['holds']==1 and observed['stale_calls']==0 and not observed['focused'],'native evidence expiry probe must execute'
  if args.heartbeat_expiry:
   observation=json.loads((out/'heartbeat-expiry.json').read_text());assert observation['holds']>0,'heartbeat expiry probe must actually execute'
   if args.negative_heartbeat_stale:assert observation['stale_publishes']>0
   elif args.heartbeat_lease_expiry:assert observation['holds']>=2 and observation['stale_publishes']==0 and not observation['focused']
   else:assert observation['holds']==1 and observation['stale_publishes']==0 and observation['focused']

 assert sources()==receipt,'native input Host source drift'
 if continuity_inputs is not None:assert validate_inputs(worker,author,receipt)==continuity_inputs,'continuity artifact drift during execution'
 if args.interaction_seconds:report['interaction_scope']='formal-1800s' if args.interaction_seconds==1800 else 'development-short-not-formal'
 report.update(status='expected-omitted-continuity-edge-rejected' if args.interaction_drop_release else 'expected-stale-heartbeat-rejected' if args.negative_heartbeat_stale else 'expected-ignored-queued-token-rejected' if args.negative_queued_ignore_token else 'expected-omitted-inflight-revoke-rejected' if args.negative_inflight_omit_revoke else 'expected-native-cleanup-fault-rejected' if args.negative_cleanup_fault else 'expected-missing-native-adapter-rejected' if args.negative_no_adapter else 'partial-native-desktop-Host-author' if args.desktop else 'partial-native-preview-Host-author',host_build=json.loads((out/'supervisor/build.json').read_text()),result={'omitted_continuity_edge_detected':True} if args.interaction_drop_release else {'stale_heartbeat_detected':True} if args.negative_heartbeat_stale else {'ignored_queued_token_detected':True} if args.negative_queued_ignore_token else {'omitted_inflight_revoke_detected':True} if args.negative_inflight_omit_revoke else {'cleanup_fault_detected':True} if args.negative_cleanup_fault else {'author_input_absent':True} if args.negative_no_adapter else json.loads((out/'result.json').read_text()))
finally:
 primary_failure=sys.exc_info()[1]
 cleanup_error=None
 if args.desktop:
  from owned_input_cleanup import cleanup
  try:cleanup(out)
  except Exception as error:cleanup_error=error;report['external_input_cleanup_error']=repr(error)
 try:
  report['host_exit_trace_before_fallback']=capture_exit_trace(out,host.pid if host is not None else None,host_creation,'before-fallback')
  if host is not None and host.poll() is None:host.terminate();host.wait(10)
 except Exception as error:
  report['host_cleanup_error']=repr(error)
  if cleanup_error is None:cleanup_error=error
 finally:
  report['host_exit_trace_after_fallback']=capture_exit_trace(out,host.pid if host is not None else None,host_creation,'after-fallback')
 if args.diagnostic_worker:report['status']='diagnostic-only-'+report['status']
 report['host_exited']=host is not None and host.poll() is not None
 if args.desktop:
  try:
   from native_lifecycle_evidence import attach
   attach(report,out,host.pid if host is not None else None,host_creation)
  except Exception as error:
   report['lifecycle_error']=repr(error);report['status']='failed-native-lifecycle'
   if cleanup_error is None:cleanup_error=error
 if primary_failure is not None:report['primary_failure']=repr(primary_failure)
 from failure_report import save_report
 save_report(out/'report.json',report,primary_failure if primary_failure is not None else cleanup_error)
 if cleanup_error is not None and primary_failure is None:raise cleanup_error
