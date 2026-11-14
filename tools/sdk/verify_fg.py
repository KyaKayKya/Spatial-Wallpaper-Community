"""Run existing F/G regressions with explicit source/installed-bit scope; release coverage remains blocked."""
from dataclasses import asdict
from pathlib import Path
import argparse,json,sys,time,uuid
from provenance import ROOT,sources
from release_identity import identity
from verify_e import BASE,Case,digest,execute,publish,require_case_set,selection
from verify_delivery import verify_candidate

# These are original release requirements not yet supplied by the development
# slices below. A successful slice never removes them or certifies a stage.
MISSING={
 'F':{'final-configuration-fidelity':'Ordinary Unity, sandbox and client comparison at fixed views and natural playback, including effect-removal negatives, must cover the final supported configurations.',
      'complete-product-platform-matrix':'Final installed product must complete applicable Explorer/session/DPI, physical tracking/calibration and legacy-content compatibility checks.'},
 'G':{'final-D-E-F-acceptance':'Final candidate needs complete D/E/F evidence, including history closure and the frozen input duration/cycle thresholds.',
      'installation-upgrade-removal':'Final installed artifacts need the missing/mismatched/corrupt dependency, upgrade and removal matrix with retained content.',
      'independent-author':'Independent original scene and one-hour clean-client validation remain required after internal RC; procurement needs separate confirmation.'}}

def registry(stage,delivery):
 delivery=Path(delivery).resolve();runtime=str(delivery/'runtime');worker=str(delivery/'runtime/worker');client=str(delivery/'client')
 def case(name,script,args=(),folder='',statuses=(),**kw):
  return Case(name,'tools/sdk/'+script,tuple(map(str,args)),folder=folder,statuses=tuple(statuses),**kw)
 if stage=='F':
  cases=[case('session','check_runtime_session.py',folder='F/session-owner',statuses=['passed-controlled-session-ownership-only']),
         case('session-late-stop','check_runtime_session.py',['--negative','skip-terminal-wait'],'F/session-owner',['expected-session-mutation-rejected']),
         case('session-cleanup-failure','check_runtime_session.py',['--negative','release-on-cleanup-failure'],'F/session-owner',['expected-session-mutation-rejected']),
         case('supervisor-session','check_supervised_session_owner.py',folder='F/supervised-session-owner',statuses=['passed-controlled-supervisor-session-only']),
         case('preview-status','check_preview_status.py',folder='F/preview-status',statuses=['passed-controlled-preview-snapshot-rejections'])]
  frame_cases=['normal','reentrant-stop','stop-blocking','cross-session','projection','projection-edge','projection-fit','input-region','input','inspection','application','application-projection','application-preview','application-preview-input','application-desktop-input','desktop-channel','desktop-channel-races']
  for name in frame_cases:
   status='passed-development-shared-desktop-input-channel' if name.startswith('desktop-channel') else 'passed-development-content-application' if name.startswith('application') else 'passed-development-shared-restricted-inspection' if name=='inspection' else 'passed-development-LPAC-presented-native-input' if name=='input' else 'passed-development-LPAC-projection-screen-readback' if name.startswith('projection') else 'passed-development-LPAC-frame-screen-readback'
   # Region/reentrant/cross-session rejection cases use their existing explicit status.
   if name in ['reentrant-stop','stop-blocking','cross-session']:status='passed-development-presentation-rejection'
   cases.append(case('frame-'+name,'run_frame_presentation.py',['--worker',worker,'--case',name],'F/frame-presentation',[status],resource='desktop',timeout=360,required={'case':name,'sources_unchanged':True}))
  cases.append(case('frame-missing-draw','run_frame_presentation.py',['--worker',worker,'--negative-skip-draw'],'F/frame-presentation',['expected-missing-window-draw-rejected'],resource='desktop',timeout=360,required={'sources_unchanged':True}))
  for name,flag,status in [('lease','--lease','passed-owned-preview-host-bootstrap'),('wrong-owner','--wrong-owner','passed-rejected-wrong-preview-owner'),('owner-exit','--crash-owner','passed-preview-owner-exit-cleans-private-host-and-worker')]:
   cases.append(case('preview-'+name,'run_preview_owner.py',['--worker',worker,flag],'F/preview-owner',[status],required={'sources_unchanged':True},timeout=360))
  for name in ['normal','cancel-start','wrong-runtime','wrong-runtime-file']:
   cases.append(case('native-preview-'+name,'run_preview_program.py',['--worker',worker,'--runtime',runtime,'--case',name],'F/preview-program',['passed-development-standalone-preview-'+name],required={'sources_unchanged':True},resource='desktop',timeout=360))
  for name in ['normal','recovery','reload','restart']:
   cases.append(case('editor-'+name,'check_editor_preview.py',['--runtime',runtime,'--case',name],'F/editor-preview',['passed-selected-editor-preview'],resource='unity',timeout=1800))
  cases.append(case('installed-client','check_client_ui.py',['--client',client,'--runtime',runtime,'--native-ui','--audio-picker'],'F/client-ui',['passed-development-original-manager-tray-lifecycle'],resource='desktop',timeout=360))
  return cases
 if stage!='G':raise ValueError('F/G stage required')
 sdk=str(delivery/('com.spatialwallpaper.sdk-'+identity()['sdk']+'.tgz'))
 return [case('installed-samples','check_clean_samples.py',['--sdk-tgz',sdk,'--runtime',runtime],'G/clean-samples',['passed-development-clean-sample-install-and-preview'],resource='unity',timeout=2400,sources_required=False),
         case('installed-play-llm','check_clean_samples.py',['--sdk-tgz',sdk,'--runtime',runtime,'--ordinary-play','--sample','LlmReply'],'G/clean-samples',['passed-development-clean-sdk-ordinary-play'],resource='unity',timeout=1800,sources_required=False),
         case('installed-audio','check_clean_samples.py',['--sdk-tgz',sdk,'--runtime',runtime,'--sample','Audio','--audio-tone-fixture'],'G/clean-samples',['passed-development-clean-sample-install-and-preview'],resource='unity',timeout=1800,sources_required=False),
         case('installed-client-audio','check_client_audio.py',['--client',client,'--runtime',runtime],'G/client-audio',['passed-development-original-client-audio-rebind'],resource='desktop',timeout=360,required={'sources_unchanged':True}),
         case('performance','run_performance.py',['--delivery',str(delivery)],'G/performance',['completed-frozen-performance-matrix-results-unmeasured'],resource='performance',timeout=7200,required={'source_unchanged':True})]

def verdict(stage,catalog,rows):
 names=[row['id'] for row in rows]
 if len(names)!=len(set(names)) or not set(names)<={c.id for c in catalog}:raise ValueError('unknown-or-duplicate-case')
 missing=dict(MISSING[stage])
 absent=sorted({c.id for c in catalog}-set(names))
 if absent:missing['complete-case-catalog']=absent
 failed=[row['id'] for row in rows if row['status']!='passed']
 return {'status':'blocked-release-coverage' if missing and not failed else 'failed','missing_release_checks':missing,'failed_cases':failed,'scope':'Fresh existing regression cases only; never F/G or RC acceptance.'}

def execution_scope(case):
 if case.id.startswith('frame-') or case.id in ['preview-lease','preview-wrong-owner','preview-owner-exit']:
  return 'Current-source harness/Host/SDK regression using the candidate Worker; not execution of the delivered Host/presenter.'
 if case.id.startswith('editor-'):
  return 'Current-source SDK copy in a new Editor project with installed candidate runtime; not tarball SDK installation proof.'
 if case.id.startswith('native-preview-'):
  return 'Installed candidate runtime; wrong-runtime negatives mutate only owned fixture copies.'
 if case.id.startswith('installed-') or case.id=='performance':
  return 'Installed candidate artifacts named in the command; current-source external validation harness.'
 return 'Current-source controlled regression only; candidate inventory is checked but its delivered binaries are not exercised.'

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--stage',choices=['F','G'],required=True);p.add_argument('--delivery',type=Path,required=True);p.add_argument('--list',action='store_true');p.add_argument('--case',action='append',default=[]);args=p.parse_args()
 catalog=registry(args.stage,args.delivery);chosen=selection(catalog,args.case)
 if args.list:
  print(json.dumps({'status':'catalog-only-not-stage-acceptance','cases':[dict(asdict(c),execution_scope=execution_scope(c)) for c in chosen],'missing_release_checks':MISSING[args.stage]},indent=2));return 0
 # No case launches until the complete installed candidate inventory/identity is verified.
 delivery=args.delivery.resolve();manifest=verify_candidate(delivery)
 out=BASE/args.stage/'acceptance'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
 receipt=sources();report={'status':'running','stage':args.stage,'sources':receipt,'delivery':str(delivery),'delivery_manifest_sha256':digest(delivery/'assembly-report.json'),'identity':manifest['identity'],'steps':[],'started_at_unix':time.time()};target=BASE/'reports'/('stage-'+args.stage+'.json')
 def save():publish(out/'report.json',report);publish(target,report)
 save()
 try:
  artifacts={}
  # During development only explicitly selected regressions run. Known missing
  # release coverage blocks the default entry before any long suite starts.
  runnable=chosen if args.case or not MISSING[args.stage] else []
  for case in runnable:
   row=execute(case,artifacts,receipt,out,catalog=catalog);row['execution_scope']=execution_scope(case);report['steps'].append(row);save()
   if row['status']!='passed':break
   if 'artifact' in row:artifacts[case.id]=row['artifact']
  report.update(verdict(args.stage,catalog,report['steps']))
  verify_candidate(delivery)
  if sources()!=receipt:raise ValueError('source-drift')
 except BaseException as error:report.update(status='failed',error=repr(error))
 finally:report['finished_at_unix']=time.time();save()
 print(out,flush=True)
 return 1 # Remaining original release requirements are explicit above.

if __name__=='__main__':raise SystemExit(main())
