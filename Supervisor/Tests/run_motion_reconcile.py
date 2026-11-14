"""Production control-flow replay with native APIs substituted; no desktop input."""
from pathlib import Path
import sys,subprocess,uuid,json,hashlib,argparse
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from provenance import sources
from windows_framework import references
from motion_reconcile_source import transformed_source
parser=argparse.ArgumentParser();parser.add_argument('--negative',choices=['requery','skip-intermediate','release-forward','deferred-time','deferred-held','deferred-identity','cursor-lag','pipe-held-clear']);args=parser.parse_args()
out=ROOT/'Validation/Content/sdk-runtime/E/motion-reconcile'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
report={'status':'failed','sources':sources(),'scope':'production Heartbeat/MouseCore/Keyboard/Publish/InputHeartbeat/InputGate; real NamedPipe/DesktopInputChannel/InputBroker/InputFrames; native query/ownership/clock/forwarding/message posting substituted; no desktop or LPAC Worker claim'}
try:
 text=(ROOT/'Supervisor/Interactive/WindowsDesktopInput.cs').read_text()
 report['negative']=args.negative
 text=transformed_source(text,args.negative)
 copy=out/'MotionState.cs';copy.write_text(text);report['substituted_source_sha256']=hashlib.sha256(copy.read_bytes()).hexdigest()
 channel=ROOT/'Supervisor/Broker/DesktopInputChannel.cs'
 if args.negative=='pipe-held-clear':
  channel_text=channel.read_text();old='finally{target.Owner.DetachSource(target);}'
  assert channel_text.count(old)==1
  channel=out/'DesktopInputChannel.cs'
  channel.write_text(channel_text.replace(old,'finally{if(target.Downs[2]==0)target.Owner.DetachSource(target);}'))
  report['mutated_channel_sha256']=hashlib.sha256(channel.read_bytes()).hexdigest()
 files=[ROOT/'Supervisor/Tests/DesktopMotionReconcileChecks.cs',copy,channel,ROOT/'Supervisor/Windows/Native.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs']
 files += [ROOT/'Supervisor/Interactive'/n for n in ['DesktopHitClient.cs','OwnedInputThread.cs','InputCommitFence.cs','InputHeartbeat.cs']]
 files += [ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities'/n for n in ['InputBroker.cs','InputGate.cs','InputSnapshot.cs','WindowsMachineClock.cs']]
 command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-out:'+str(out/'Checks.exe'),*map(str,files)]
 for name,cmd in [('compile',command),('result',[str(out/'Checks.exe'),*(['pipe-accepted-down-overflow'] if args.negative=='pipe-held-clear' else ['icon'] if args.negative=='skip-intermediate' else ['release'] if args.negative=='release-forward' else ['cursor-lags-hook'] if args.negative=='cursor-lag' else [])])]:
  result=subprocess.run(cmd,capture_output=True,text=True,timeout=30);(out/(name+'.log')).write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr);report[name+'_exit']=result.returncode
  if name=='compile' and result.returncode==0:report['checks_sha256']=hashlib.sha256((out/'Checks.exe').read_bytes()).hexdigest()
  if name=='result':
   report['accepted_down_overflow_preconditions']=all('PASS '+label in result.stdout for label in (
    'pipe-host-accepted-down-before-capacity-fault','pipe-author-observed-held-down-before-overflow',
    'move-reports-channel-overflow','pipe-overflow-client-cleaned','pipe-overflow-server-cleaned',
    'pipe-overflow-owner-releases-deferred-events'))
  result.check_returncode()
 report['pipe_short_tap']='PASS pipe-worker-frame-exposes-short-tap' in result.stdout
 report['pipe_overflow_cleanup']='PASS pipe-overflow-final-author-state-cleared' in result.stdout
 report['pipe_accepted_down_overflow_cleanup']='PASS pipe-accepted-down-overflow-final-author-state-cleared' in result.stdout
 assert report['sources']==sources();report['status']='passed-state-replay-only'
finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
