"""Read a production LPAC Worker frame through Host and verify actual window pixels."""
from pathlib import Path
import argparse,sys,uuid,subprocess,json,hashlib,shutil
from check_contracts import ROOT,DATA
from provenance import sources
from windows_framework import references
from toolchain_process import run_toolchain
from trusted_client_sources import client_sources
sys.path.insert(0,str(ROOT/'Supervisor/Tests'))
from run_production import packages,require_production_build

def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--worker',type=Path,required=True);parser.add_argument('--package',type=Path);parser.add_argument('--manual-focus',action='store_true');parser.add_argument('--negative-input-open',choices=['cancel','stop']);parser.add_argument('--negative-skip-draw',action='store_true');parser.add_argument('--negative-unload-owner',action='store_true');parser.add_argument('--slow-presentation',action='store_true');parser.add_argument('--negative-queued-eye',action='store_true');parser.add_argument('--negative-no-input-adapter',action='store_true');parser.add_argument('--negative-ignore-presented-region',action='store_true');parser.add_argument('--case',choices=['content-capture','normal','reentrant-stop','stop-blocking','cross-session','projection','projection-edge','projection-fit','input-region','input','inspection','application','application-projection','application-preview','application-preview-input','application-desktop-input','desktop-channel','desktop-channel-races'],default='normal');args=parser.parse_args()
 assert (args.package is not None)==(args.case=='content-capture'),'custom-package-only-with-content-capture'
 assert not args.negative_skip_draw or args.case=='normal','screen-negative-is-an-explicit-visual-test-not-an-application-gate'
 assert not args.negative_input_open or args.case=='desktop-channel-races'
 assert not (args.negative_no_input_adapter or args.negative_ignore_presented_region) or args.case=='input'
 assert sum([args.negative_skip_draw,args.negative_no_input_adapter,args.negative_ignore_presented_region,args.negative_unload_owner,args.negative_queued_eye])<=1
 assert not args.negative_unload_owner or args.case=='application'
 assert not args.negative_queued_eye or args.slow_presentation and args.case=='application-preview'
 worker=args.worker.resolve();wr=json.loads((worker/'report.json').read_text());require_production_build(wr)
 assert wr['build'].get('frame_protocol')==2 and wr['build'].get('projection_protocol')==1,'matching-frame-and-projection-Worker-required'
 assert {p.relative_to(worker/'player').as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in (worker/'player').rglob('*') if p.is_file()}==wr['runtime_files']
 out=ROOT/'Validation/Content/sdk-runtime/F/frame-presentation'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True);receipt=sources();ids=packages(out,[.6,.6,.5] if args.case=='projection-fit' else None,input_feedback=args.case in ['input','application-preview-input','application-desktop-input','desktop-channel','desktop-channel-races'])
 if args.package:
  import zipfile
  data=args.package.read_bytes()
  with zipfile.ZipFile(args.package) as archive:ids[0]=json.loads(archive.read('manifest.json'))['build_id']
  (out/'healthy.spatial').write_bytes(data)
 report={'status':'failed','sources':receipt,'worker_report_sha256':hashlib.sha256((worker/'report.json').read_bytes()).hexdigest(),'case':args.case,'negative_skip_draw':args.negative_skip_draw,'manual_focus':args.manual_focus,'negative_input_open':args.negative_input_open,'scope':'development actual screen readback, selected projection/native preview input cases and explicit rejections; no full projection/input matrix, fidelity, performance or release acceptance'}
 report.update(slow_presentation=args.slow_presentation,negative_queued_eye=args.negative_queued_eye,negative_unload_owner=args.negative_unload_owner,negative_no_input_adapter=args.negative_no_input_adapter,negative_ignore_presented_region=args.negative_ignore_presented_region)
 if args.case=='content-capture':report['scope']='Current-source Host/SDK harness and actual production LPAC Worker custom snapshot capture; no window presentation, rendered fidelity or RC acceptance.'
 try:
  for name in ['supervisor','validator']:subprocess.run([sys.executable,str(ROOT/f'tools/sdk/build_{name}.py'),'--output',str(out/name)],check=True)
  shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
  files=[ROOT/'Supervisor/Tests/ScreenFrameValidation.cs',ROOT/'Supervisor/Tests/FramePresentationChecks.cs',ROOT/'Supervisor/Tests/PresentedInputChecks.cs',ROOT/'Supervisor/Tests/ContentApplicationChecks.cs']
  files+=client_sources(ROOT)
  files+=[ROOT/'Supervisor/Windows'/name for name in ['Native.cs','RestrictedProcess.cs']]
  files=list(dict.fromkeys(files))
  if args.case=='application-desktop-input':
   from experiments.native_build import compile_probe
   compile_probe(ROOT/'Supervisor/Interactive/DesktopHitTester.cpp',out/'DesktopHitTester.exe')
  if args.case=='desktop-channel-races':
   original=ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows/SupervisorManagerConnection.cs';text=original.read_text();start=text.index('  Task<Stream> OpenInputStream(');end=text.index('  // One bounded binary transfer',start);section=text[start:end]
   before='stream.Connect(RuntimeLimits.ManagerIoMs);cancellation.ThrowIfCancellationRequested();';after='stream.Connect(RuntimeLimits.ManagerIoMs);global::FramePresentationChecks.InputOpenProbe?.Invoke("connected");cancellation.ThrowIfCancellationRequested();';assert section.count(before)==1;section=section.replace(before,after)
   before='    cancellation.ThrowIfCancellationRequested();if(!session.TryUseRunning';after='    global::FramePresentationChecks.InputOpenProbe?.Invoke("request-written");cancellation.ThrowIfCancellationRequested();if(!session.TryUseRunning';assert section.count(before)==1;section=section.replace(before,after)
   if args.negative_input_open=='cancel':
    # Retain the initial cancellation check; restore only the old late-open bug.
    first=section.index('cancellation.ThrowIfCancellationRequested();')+len('cancellation.ThrowIfCancellationRequested();');section=section[:first]+section[first:].replace('cancellation.ThrowIfCancellationRequested();','')
   if args.negative_input_open=='stop':
    before='if(!session.TryUseRunning(identity,()=>{}))throw new InvalidOperationException(kind+"-input-session-ended");';assert section.count(before)==1;section=section.replace(before,'')
   mutated=out/original.name;mutated.write_text(text[:start]+section+text[end:]);files=[mutated if p==original else p for p in files];report.setdefault('mutated_sources_sha256',{})[original.relative_to(ROOT).as_posix()]=hashlib.sha256(mutated.read_bytes()).hexdigest()
  if args.slow_presentation:
   original=ROOT/'Supervisor/Presentation/WindowsContentPresentation.cs';text=original.read_text();needle='var frame=await connection.ReadVerifiedFrame(session,width,height,after);Check(current,cancellation);';assert text.count(needle)==1
   mutated=out/original.name;mutated.write_text(text.replace(needle,needle+'await Task.Delay(250,cancellation);'));files=[mutated if p==original else p for p in files];report.setdefault('mutated_sources_sha256',{})[original.relative_to(ROOT).as_posix()]=hashlib.sha256(mutated.read_bytes()).hexdigest()
  if args.negative_queued_eye:
   original=ROOT/'Supervisor/Preview/PreviewWindow.cs';text=original.read_text();needle='if(!eyeUpdate.IsCompleted||lastReceipt==null||(long)lastReceipt["projection_revision"]<requestedEyeRevision)return;';assert text.count(needle)==1
   mutated=out/original.name;mutated.write_text(text.replace(needle,'if(!eyeUpdate.IsCompleted)return;'));files=[mutated if p==original else p for p in files];report.setdefault('mutated_sources_sha256',{})[original.relative_to(ROOT).as_posix()]=hashlib.sha256(mutated.read_bytes()).hexdigest()
  if args.negative_unload_owner:
   original=ROOT/'Supervisor/Presentation/WindowsContentPresentation.cs';text=original.read_text();start=text.index('  public Task UnloadAsync(){');end=text.index('\n',start)
   mutated=out/original.name;mutated.write_text(text[:start]+'  public Task UnloadAsync(){Available();if(unloading!=null&&!unloading.IsCompleted)return unloading;return unloading=UnloadCore();}'+text[end:]);files=[mutated if p==original else p for p in files];report['mutated_source_sha256']=hashlib.sha256(mutated.read_bytes()).hexdigest()
  if args.negative_skip_draw:
   original=ROOT/'Supervisor/Presentation/WindowsFramePresenter.cs';text=original.read_text();needle='graphics.DrawImageUnscaled(bitmap,0,0);';assert text.count(needle)==1
   mutated=out/'WindowsFramePresenter.cs';mutated.write_text(text.replace(needle,''));files=[mutated if p==original else p for p in files];report['mutated_source_sha256']=hashlib.sha256(mutated.read_bytes()).hexdigest()
  if args.negative_ignore_presented_region:
   original=ROOT/'Supervisor/Presentation/WindowsPresentedPreviewInput.cs';text=original.read_text();needle='if(disposed||!presenter.TryGetPresentedRegion(out Rectangle region))return false;';assert text.count(needle)==1
   mutated=out/original.name;mutated.write_text(text.replace(needle,'if(disposed)return false;var region=new Rectangle(target.PointToScreen(Point.Empty),target.ClientSize);'));files=[mutated if p==original else p for p in files];report['mutated_source_sha256']=hashlib.sha256(mutated.read_bytes()).hexdigest()
  subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:System.Drawing.dll','-r:System.Windows.Forms.dll','-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Client.exe'),*map(str,files)],check=True)
  shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'Client.exe.config')
  state=out/'state';(out/'config.json').write_text(json.dumps({'image':str(worker/'player'),'inventory':str(worker/'report.json'),'validator':str(out/'validator'),'state':str(state),'attempts':str(ROOT/'Validation/Sdk'/('f'+out.name[:10])),'width':640,'height':360}))
  report['client_sha256']=hashlib.sha256((out/'Client.exe').read_bytes()).hexdigest();report['process']={}
  try:
    with (out/'client.log').open('w') as log:run_toolchain([str(out/'Client.exe'),str(out),str(out/'supervisor/Supervisor.exe'),str(out/'healthy.spatial'),ids[0],args.case,'no-input-adapter' if args.negative_no_input_adapter else 'adapter','manual-focus' if args.manual_focus else 'automatic-focus'],ROOT,log,150 if args.manual_focus else 90,report['process'])
  except subprocess.CalledProcessError:
   if not (args.negative_input_open or args.negative_skip_draw or args.negative_no_input_adapter or args.negative_ignore_presented_region or args.negative_unload_owner or args.negative_queued_eye):raise
   expected=('shared-desktop-cancel-after-connect-returned-stream' if args.negative_input_open=='cancel' else 'shared-desktop-stop-after-request-returned-stream') if args.negative_input_open else 'preview-automatic-eye-never-presented' if args.negative_queued_eye else 'reentrant-unload-or-post-commit-release-incoherent' if args.negative_unload_owner else 'input-before-first-presentation-reached-author' if args.negative_ignore_presented_region else 'presented-real-input-did-not-reach-author' if args.negative_no_input_adapter else 'presenter-screen-readback-mismatch'
   assert report['process']['exit_code']==1 and expected in (out/'client.log').read_text(),'wrong-presentation-negative'
  else:assert not (args.negative_input_open or args.negative_skip_draw or args.negative_no_input_adapter or args.negative_ignore_presented_region or args.negative_unload_owner or args.negative_queued_eye),'missing-adapter-or-draw-not-rejected'
  assert sources()==receipt,'presentation-source-drift';result=json.loads((out/'result.json').read_text());report['result']=result
  assert all(result.get(k) is True for k in ['session_resources_released','retained_worker_kernel_exited','retained_host_kernel_exited'])
  assert not result.get('cleanup_error')
  if args.case.startswith('application'):
   report['application_cleanup']=json.loads((out/'application-cleanup.json').read_text());assert report['application_cleanup']=={'runtime_resources_released':True,'presentation_disposed':True}
  if args.negative_input_open:report['status']='expected-shared-desktop-input-late-open-rejected'
  elif args.negative_queued_eye:report['status']='expected-queued-eye-starvation-rejected'
  elif args.negative_unload_owner:report['status']='expected-reentrant-unload-owner-rejected'
  elif args.negative_ignore_presented_region:report['status']='expected-unpresented-input-region-rejected'
  elif args.negative_no_input_adapter:report['status']='expected-missing-presented-input-adapter-rejected'
  elif args.negative_skip_draw:report['status']='expected-missing-window-draw-rejected'
  elif args.case=='content-capture':
   assert result.get('captured_content',{}).get('build_id')==ids[0];report['status']='passed-development-LPAC-content-capture-not-presentation';report['package_sha256']=hashlib.sha256((out/'healthy.spatial').read_bytes()).hexdigest()
  elif args.case.startswith('desktop-channel'):
   assert result.get('desktop_channel',{}).get('same_formal_session') is True;report['status']='passed-development-shared-desktop-input-channel'
  elif args.case in ['normal','projection','projection-edge','projection-fit','input-region','input','inspection','application','application-projection','application-preview','application-preview-input','application-desktop-input']:
   assert result.get('presentation') or args.case.startswith('application') and result.get('application',{}).get('applied_after_real_presentation');report['status']='passed-development-LPAC-projection-screen-readback' if args.case.startswith('projection') else 'passed-development-LPAC-frame-screen-readback'
   if args.case.startswith('projection'):assert result.get('projection_verified') is True
   if args.case=='input-region':assert result.get('presented_input_region_verified') is True
   if args.case=='input':assert result.get('presented_input',{}).get('same_presented_region') is True;report['status']='passed-development-LPAC-presented-native-input'
   if args.case=='inspection':assert result.get('restricted_inspection_verified') is True;report['status']='passed-development-shared-restricted-inspection'
   if args.case.startswith('application'):report['status']='passed-development-content-application'
  else:
   assert result.get('cross_session_rejected' if args.case=='cross-session' else 'paint_stop_rejected') is True
   report['status']='passed-development-presentation-rejection'
 except BaseException as error:
  report['primary_failure']=repr(error);raise
 finally:
  report['sources_unchanged']=sources()==receipt;report['files']={p.relative_to(out).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in out.rglob('*') if p.is_file() and p!=out/'report.json'};(out/'report.json').write_text(json.dumps(report,indent=2));print(out)
if __name__=='__main__':main()
